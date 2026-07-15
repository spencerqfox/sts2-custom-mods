using System;
using System.Collections.Generic;
using System.Linq;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Potions;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.CardPools;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Runs.History;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Runs;
using JoinInProgress.Resync;

namespace JoinInProgress.Networking;

internal static class LateJoinSubmissionValidator
{
    private const int MaxInventoryEntries = 256;
    private const int MaxHistoryEntriesPerFloor = 128;

    public static SerializablePlayer ValidateAndSanitize(
        RunState state,
        Player hostPlayer,
        SerializablePlayer baseline,
        SerializablePlayer submitted,
        IReadOnlyList<PlayerMapPointHistoryEntry> history,
        CatchUpRewardPlan rewardPlan)
    {
        if (submitted.NetId != hostPlayer.NetId || baseline.NetId != hostPlayer.NetId)
        {
            throw new InvalidOperationException("The submitted player ID does not match the joining peer.");
        }
        if (submitted.CharacterId != baseline.CharacterId)
        {
            throw new InvalidOperationException("The joining character changed during catch-up.");
        }
        if (submitted.Deck.Count > MaxInventoryEntries ||
            submitted.Relics.Count > MaxInventoryEntries ||
            submitted.Potions.Count > MaxInventoryEntries)
        {
            throw new InvalidOperationException("The submitted inventory is too large.");
        }
        if (submitted.Rng.Counters.Count > 16 ||
            submitted.Rng.Counters.Any(pair => pair.Value < 0 || pair.Value > 10_000_000))
        {
            throw new InvalidOperationException("The submitted player RNG counters are invalid.");
        }

        List<MapPointHistoryEntry> floors = state.MapPointHistory.SelectMany(act => act).ToList();
        if (history.Count != floors.Count)
        {
            throw new InvalidOperationException(
                $"Catch-up history length mismatch. Expected {floors.Count}, received {history.Count}.");
        }
        if (rewardPlan.Floors.Count != floors.Count)
        {
            throw new InvalidOperationException("The authoritative catch-up reward plan is incomplete.");
        }

        Dictionary<ModelId, int> expectedDeck = CountCards(baseline.Deck);
        Dictionary<ModelId, int> gainedCards = new();
        Dictionary<ModelId, int> removedCards = new();
        Dictionary<ModelId, int> upgrades = new();
        List<ModelId> acquiredRelics = new();
        Dictionary<ModelId, int> baselinePotions = CountIds(
            baseline.Potions.Select(potion => RequirePotion(potion.Id)));
        Dictionary<ModelId, int> requiredPotionAdds = new();
        Dictionary<ModelId, int> requiredPotionRemovals = new();
        HashSet<ModelId> allowedPotionIds = baseline.Potions.Select(potion => RequirePotion(potion.Id)).ToHashSet();
        int shopRemovalCount = 0;
        int currentHp = baseline.CurrentHp;
        int maxHp = baseline.MaxHp;
        int gold = baseline.Gold;
        int removalPriceIndex = baseline.ExtraFields.CardShopRemovalsUsed;

        for (int i = 0; i < floors.Count; i++)
        {
            MapPointHistoryEntry floor = floors[i];
            CatchUpFloorRewardPlan floorPlan = rewardPlan.Floors[i];
            PlayerMapPointHistoryEntry entry = history[i];
            PlayerMapPointHistoryEntry reference = GetReferenceEntry(state, floor, hostPlayer);
            ValidateHistoryEntryShape(entry, reference, hostPlayer.NetId);

            List<SerializableCard> plannedCards = floorPlan.Rooms
                .SelectMany(room => room.CardRewardGroups.SelectMany(group => group.Cards)
                    .Concat(room.Shop?.Cards.Select(offer => offer.Card) ?? []))
                .ToList();
            List<ModelId> plannedRelics = floorPlan.Rooms
                .SelectMany(room => room.RelicRewards
                    .Concat(room.Shop?.Relics.Select(offer => offer.ModelId) ?? [])
                    .Concat(room.AncientRelicChoices.Select(offer => RequireRelic(offer.Relic.Id))))
                .ToList();
            List<ModelId> plannedPotions = floorPlan.Rooms
                .SelectMany(room => room.PotionRewards
                    .Concat(room.Shop?.Potions.Select(offer => offer.ModelId) ?? []))
                .ToList();
            HashSet<ModelId> plannedAncientRelics = floorPlan.Rooms
                .SelectMany(room => room.AncientRelicChoices.Select(offer => RequireRelic(offer.Relic.Id)))
                .ToHashSet();
            HashSet<ModelId> allowedCards = plannedCards
                .Select(card => RequireCard(card.Id))
                .Concat(reference.CardsGained.Select(card => RequireCard(card.Id)))
                .ToHashSet();
            HashSet<ModelId> allowedRelics = plannedRelics
                .Select(RequireRelic)
                .Concat(reference.RelicChoices.Select(choice => RequireRelic(choice.choice)))
                .ToHashSet();
            HashSet<ModelId> floorPotionIds = plannedPotions
                .Select(RequirePotion)
                .Concat(reference.PotionChoices.Select(choice => RequirePotion(choice.choice)))
                .ToHashSet();
            allowedPotionIds.UnionWith(floorPotionIds);
            bool hasCombat = floor.Rooms.Any(room => room.RoomType is RoomType.Monster or RoomType.Elite or RoomType.Boss);
            bool hasShop = floor.HasRoomOfType(RoomType.Shop);
            bool hasTreasure = floor.HasRoomOfType(RoomType.Treasure);
            bool eventOnly = floor.MapPointType != MegaCrit.Sts2.Core.Map.MapPointType.Ancient &&
                             floor.Rooms.Count > 0 && floor.Rooms.All(room => room.RoomType == RoomType.Event);
            bool followedEvent = entry.EventChoices.Count > 0 ||
                                 (eventOnly && HasRecordedEventOutcome(entry));
            int cardRewardGroups = floorPlan.Rooms.Sum(room => room.CardRewardGroups.Count);
            int plannedShopCards = floorPlan.Rooms.Sum(room => room.Shop?.Cards.Count ?? 0);
            int plannedRelicRewards = floorPlan.Rooms.Sum(room => room.RelicRewards.Count);
            int plannedShopRelics = floorPlan.Rooms.Sum(room => room.Shop?.Relics.Count ?? 0);
            int plannedAncientChoices = floorPlan.Rooms.Sum(room => room.AncientRelicChoices.Count);
            int plannedPotionRewards = floorPlan.Rooms.Sum(room => room.PotionRewards.Count);
            int plannedShopPotions = floorPlan.Rooms.Sum(room => room.Shop?.Potions.Count ?? 0);
            int maxCardGains = cardRewardGroups + plannedShopCards +
                               (eventOnly && followedEvent ? reference.CardsGained.Count : 0);
            int maxRelicGains = plannedRelicRewards + plannedShopRelics +
                                (plannedAncientChoices > 0 ? 1 : 0) +
                                (eventOnly && followedEvent
                                    ? reference.RelicChoices.Count(choice => choice.wasPicked)
                                    : 0);
            int maxPotionGains = plannedPotionRewards + plannedShopPotions +
                                 (eventOnly && followedEvent
                                     ? reference.PotionChoices.Count(choice => choice.wasPicked)
                                     : 0);
            int maxCardRemovals = (hasShop ? 1 : 0) +
                                  (eventOnly && followedEvent ? reference.CardsRemoved.Count : 0);

            if (entry.CardsGained.Count > maxCardGains ||
                entry.RelicChoices.Count(choice => choice.wasPicked) > maxRelicGains ||
                entry.PotionChoices.Count(choice => choice.wasPicked) > maxPotionGains ||
                entry.PotionDiscarded.Count > maxPotionGains ||
                entry.CardsRemoved.Count > maxCardRemovals ||
                entry.BoughtPotions.Count > plannedShopPotions)
            {
                throw new InvalidOperationException($"Too many rewards were selected on catch-up floor {i + 1}.");
            }
            if (entry.AncientChoices.Count > plannedAncientChoices ||
                entry.AncientChoices.Count(choice => choice.WasChosen) > (plannedAncientChoices > 0 ? 1 : 0))
            {
                throw new InvalidOperationException($"The Ancient choice was altered on catch-up floor {i + 1}.");
            }

            foreach (SerializableCard card in entry.CardsGained)
            {
                ModelId id = RequireCard(card.Id);
                if (!allowedCards.Contains(id))
                {
                    throw new InvalidOperationException($"Card {id.Entry} was not offered on catch-up floor {i + 1}.");
                }
                Increment(gainedCards, id);
            }
            foreach (SerializableCard card in entry.CardsRemoved)
            {
                Increment(removedCards, RequireCard(card.Id));
            }
            foreach (ModelId id in entry.UpgradedCards)
            {
                RequireCard(id);
                Increment(upgrades, id);
            }
            int restCount = floor.Rooms.Count(room => room.RoomType == RoomType.RestSite);
            int allowedUpgrades = entry.RestSiteChoices.Count(choice => choice == "SMITH") +
                                  (eventOnly && followedEvent ? reference.UpgradedCards.Count : 0);
            if (entry.RestSiteChoices.Count > restCount || entry.UpgradedCards.Count > allowedUpgrades)
            {
                throw new InvalidOperationException($"Too many rest-site changes were selected on floor {i + 1}.");
            }
            foreach (CardChoiceHistoryEntry choice in entry.CardChoices)
            {
                if (!allowedCards.Contains(RequireCard(choice.Card.Id)))
                {
                    throw new InvalidOperationException("A submitted card choice was not present in the run history.");
                }
            }
            Dictionary<ModelId, int> permittedCardGains = CountIds(entry.CardChoices
                .Where(choice => choice.wasPicked)
                .Select(choice => RequireCard(choice.Card.Id)));
            if (eventOnly && followedEvent)
            {
                foreach (SerializableCard card in reference.CardsGained)
                {
                    Increment(permittedCardGains, RequireCard(card.Id));
                }
            }
            Dictionary<ModelId, int> floorCardGains = CountCards(entry.CardsGained);
            if (floorCardGains.Any(pair => pair.Value > permittedCardGains.GetValueOrDefault(pair.Key)))
            {
                throw new InvalidOperationException("A gained card was not selected from a recorded offer.");
            }
            foreach (ModelChoiceHistoryEntry choice in entry.RelicChoices)
            {
                ModelId id = RequireRelic(choice.choice);
                if (!allowedRelics.Contains(id))
                {
                    throw new InvalidOperationException("A submitted relic choice was not present in the run history.");
                }
                if (choice.wasPicked)
                {
                    RelicModel relic = ModelDb.GetById<RelicModel>(id);
                    if (!CanReplayRelic(relic) && !plannedAncientRelics.Contains(id))
                    {
                        throw new InvalidOperationException(
                            $"Relic {relic.Id.Entry} has custom pickup logic that cannot be synchronized safely.");
                    }
                    acquiredRelics.Add(id);
                }
            }
            foreach (ModelChoiceHistoryEntry choice in entry.PotionChoices)
            {
                ModelId potionId = RequirePotion(choice.choice);
                if (!floorPotionIds.Contains(potionId))
                {
                    throw new InvalidOperationException("A submitted potion choice was not present in the run history.");
                }
                if (choice.wasPicked)
                {
                    Increment(requiredPotionAdds, potionId);
                }
            }
            foreach (ModelId id in entry.BoughtPotions)
            {
                ModelId potionId = RequirePotion(id);
                if (!floorPotionIds.Contains(potionId))
                {
                    throw new InvalidOperationException("A submitted potion purchase was not present in the shop history.");
                }
                Increment(requiredPotionAdds, potionId);
            }
            foreach (ModelId id in entry.PotionDiscarded)
            {
                ModelId potionId = RequirePotion(id);
                if (!allowedPotionIds.Contains(potionId))
                {
                    throw new InvalidOperationException("A discarded potion was not present during catch-up.");
                }
                Increment(requiredPotionRemovals, potionId);
            }
            int expectedDamage = reference.DamageTaken;
            int expectedGoldGain = hasCombat
                ? floorPlan.Rooms.Sum(room => Math.Max(0, room.Gold))
                : Math.Max(0, reference.GoldGained);
            if (entry.DamageTaken != expectedDamage ||
                entry.GoldGained != expectedGoldGain ||
                entry.GoldLost != Math.Max(0, reference.GoldLost + reference.GoldStolen))
            {
                throw new InvalidOperationException($"The floor ledger was altered on catch-up floor {i + 1}.");
            }

            int expectedMaxHpDelta = reference.MaxHpGained - reference.MaxHpLost;
            int expectedMaxHpGain = followedEvent ? Math.Max(0, expectedMaxHpDelta) : 0;
            int expectedMaxHpLoss = followedEvent ? Math.Max(0, -expectedMaxHpDelta) : 0;
            if (entry.MaxHpGained != expectedMaxHpGain || entry.MaxHpLost != expectedMaxHpLoss)
            {
                throw new InvalidOperationException($"The maximum-HP ledger was altered on catch-up floor {i + 1}.");
            }

            maxHp = Math.Max(1, maxHp + entry.MaxHpGained - entry.MaxHpLost);
            currentHp = Math.Min(currentHp, maxHp);
            int mendCount = entry.RestSiteChoices.Count(choice => choice == "MEND");
            int ancientHealPercent = floorPlan.Rooms.Select(room => room.AncientHealPercent).DefaultIfEmpty(0).Max();
            int ancientHealBudget = (int)((maxHp - currentHp) * ancientHealPercent / 100m);
            if (ancientHealPercent > 0 && entry.HpHealed != ancientHealBudget)
            {
                throw new InvalidOperationException($"The Ancient healing ledger was altered on catch-up floor {i + 1}.");
            }
            int referenceHealBudget = floor.MapPointType == MegaCrit.Sts2.Core.Map.MapPointType.Ancient
                ? 0
                : Math.Max(0, reference.HpHealed);
            int healBudget = referenceHealBudget + mendCount * maxHp + ancientHealBudget;
            if (entry.HpHealed < 0 ||
                entry.HpHealed > healBudget ||
                entry.HpHealed > Math.Max(0, maxHp - currentHp))
            {
                throw new InvalidOperationException($"The healing ledger was altered on catch-up floor {i + 1}.");
            }
            currentHp = Math.Min(maxHp, currentHp + entry.HpHealed);
            currentHp -= Math.Min(entry.DamageTaken, Math.Max(0, currentHp - 1));

            int expectedSpend = GetExpectedSpend(floor, floorPlan, entry, reference, removalPriceIndex);
            int removalsThisFloor = floor.HasRoomOfType(RoomType.Shop) ? entry.CardsRemoved.Count : 0;
            removalPriceIndex += removalsThisFloor;
            shopRemovalCount += removalsThisFloor;
            if (entry.GoldSpent != expectedSpend || entry.GoldSpent > gold)
            {
                throw new InvalidOperationException($"The purchase ledger was altered on catch-up floor {i + 1}.");
            }
            gold -= entry.GoldSpent;
            gold = Math.Max(0, gold + entry.GoldGained - entry.GoldLost);

            if (entry.CurrentHp != currentHp || entry.MaxHp != maxHp || entry.CurrentGold != gold)
            {
                throw new InvalidOperationException($"The floor snapshot was altered on catch-up floor {i + 1}.");
            }
        }

        foreach (var (id, count) in gainedCards)
        {
            Add(expectedDeck, id, count);
        }
        foreach (var (id, count) in removedCards)
        {
            Add(expectedDeck, id, -count);
        }
        if (expectedDeck.Any(pair => pair.Value < 0) || !CountsEqual(expectedDeck, CountCards(submitted.Deck)))
        {
            throw new InvalidOperationException("The submitted deck does not match the recorded catch-up choices.");
        }
        if (upgrades.Keys.Any(id => expectedDeck.GetValueOrDefault(id) <= 0))
        {
            throw new InvalidOperationException("A card upgrade targeted a card outside the final deck.");
        }

        ValidateFinalStats(submitted, baseline, currentHp, maxHp, gold, shopRemovalCount);
        List<SerializableCard> sanitizedDeck = SanitizeDeck(
            submitted.Deck, baseline, floors, upgrades, state, hostPlayer, rewardPlan);
        List<SerializableRelic> sanitizedRelics = SanitizeRelics(
            baseline, acquiredRelics, submitted.Relics, floors.Count, rewardPlan);
        ValidateSubmittedRelics(submitted.Relics, sanitizedRelics);
        ValidateSubmittedPotions(
            submitted.Potions,
            baseline,
            allowedPotionIds,
            baselinePotions,
            requiredPotionAdds,
            requiredPotionRemovals);

        RelicGrabBag relicBag = RelicGrabBag.FromSerializable(baseline.RelicGrabBag);
        foreach (ModelId relicId in acquiredRelics)
        {
            relicBag.Remove(ModelDb.GetById<RelicModel>(relicId));
        }

        submitted.CharacterId = baseline.CharacterId;
        submitted.CurrentHp = currentHp;
        submitted.MaxHp = maxHp;
        submitted.Gold = gold;
        submitted.MaxEnergy = baseline.MaxEnergy;
        submitted.MaxPotionSlotCount = baseline.MaxPotionSlotCount;
        submitted.BaseOrbSlotCount = baseline.BaseOrbSlotCount;
        submitted.Deck = sanitizedDeck;
        submitted.Relics = sanitizedRelics;
        submitted.Rng = baseline.Rng;
        submitted.Odds = baseline.Odds;
        submitted.RelicGrabBag = relicBag.ToSerializable();
        submitted.ExtraFields = new SerializableExtraPlayerFields
        {
            CardShopRemovalsUsed = baseline.ExtraFields.CardShopRemovalsUsed + shopRemovalCount,
            WongoPoints = baseline.ExtraFields.WongoPoints,
            CccomboBadgeUnlocked = baseline.ExtraFields.CccomboBadgeUnlocked,
            DamageDealt = baseline.ExtraFields.DamageDealt,
            DebuffsApplied = baseline.ExtraFields.DebuffsApplied
        };
        submitted.UnlockState = baseline.UnlockState;
        submitted.DiscoveredCards = baseline.DiscoveredCards
            .Concat(submitted.Deck.Select(card => RequireCard(card.Id))).Distinct().ToList();
        submitted.DiscoveredRelics = baseline.DiscoveredRelics
            .Concat(submitted.Relics.Select(relic => RequireRelic(relic.Id))).Distinct().ToList();
        submitted.DiscoveredPotions = baseline.DiscoveredPotions
            .Concat(submitted.Potions.Select(potion => RequirePotion(potion.Id))).Distinct().ToList();
        submitted.DiscoveredEnemies = baseline.DiscoveredEnemies.ToList();
        submitted.DiscoveredEpochs = baseline.DiscoveredEpochs.ToList();

        _ = Player.FromSerializable(submitted);
        return submitted;
    }

    private static void ValidateHistoryEntryShape(
        PlayerMapPointHistoryEntry entry,
        PlayerMapPointHistoryEntry reference,
        ulong playerId)
    {
        if (entry.PlayerId != playerId)
        {
            throw new InvalidOperationException("A catch-up history entry belongs to the wrong player.");
        }

        int listCount = entry.CardsGained.Count + entry.CardChoices.Count + entry.RelicChoices.Count +
                        entry.PotionChoices.Count + entry.PotionDiscarded.Count + entry.PotionUsed.Count +
                        entry.CardsRemoved.Count + entry.RelicsRemoved.Count + entry.CardsEnchanted.Count +
                        entry.CardsTransformed.Count + entry.UpgradedCards.Count + entry.DowngradedCards.Count +
                        entry.EventChoices.Count + entry.AncientChoices.Count + entry.RestSiteChoices.Count + entry.BoughtRelics.Count +
                        entry.BoughtPotions.Count + entry.BoughtColorless.Count + entry.CompletedQuests.Count;
        if (listCount > MaxHistoryEntriesPerFloor)
        {
            throw new InvalidOperationException("A catch-up history entry is too large.");
        }
        if (entry.GoldGained < 0 || entry.GoldSpent < 0 || entry.GoldLost < 0 ||
            entry.DamageTaken < 0 || entry.HpHealed < 0 || entry.MaxHpGained < 0 || entry.MaxHpLost < 0 ||
            entry.GoldStolen != 0 || entry.StolenLoot != 0 ||
            entry.PotionUsed.Count != 0 ||
            entry.RelicsRemoved.Count != 0 || entry.CardsEnchanted.Count != 0 ||
            entry.CardsTransformed.Count != 0 || entry.DowngradedCards.Count != 0 ||
            entry.BoughtRelics.Count != 0 || entry.BoughtColorless.Count != 0 || entry.CompletedQuests.Count != 0)
        {
            throw new InvalidOperationException("A catch-up history entry contains unsupported changes.");
        }
        if (entry.EventChoices.Count != 0 && entry.EventChoices.Count != reference.EventChoices.Count)
        {
            throw new InvalidOperationException("The submitted event outcome does not match the recorded event.");
        }
        if (entry.RestSiteChoices.Any(choice => choice is not ("MEND" or "SMITH")))
        {
            throw new InvalidOperationException("The submitted rest-site choice is invalid.");
        }
    }

    private static void ValidateFinalStats(
        SerializablePlayer submitted,
        SerializablePlayer baseline,
        int currentHp,
        int maxHp,
        int gold,
        int shopRemovalCount)
    {
        if (submitted.CurrentHp != currentHp || submitted.MaxHp != maxHp || submitted.Gold != gold ||
            submitted.CurrentHp < 1 || submitted.CurrentHp > submitted.MaxHp || submitted.MaxHp < 1 ||
            submitted.MaxEnergy != baseline.MaxEnergy ||
            submitted.MaxPotionSlotCount != baseline.MaxPotionSlotCount ||
            submitted.BaseOrbSlotCount != baseline.BaseOrbSlotCount ||
            submitted.ExtraFields.CardShopRemovalsUsed !=
            baseline.ExtraFields.CardShopRemovalsUsed + shopRemovalCount)
        {
            throw new InvalidOperationException("The submitted final player stats do not match the catch-up ledger.");
        }
    }

    private static List<SerializableCard> SanitizeDeck(
        IReadOnlyList<SerializableCard> submitted,
        SerializablePlayer baseline,
        IReadOnlyList<MapPointHistoryEntry> floors,
        IReadOnlyDictionary<ModelId, int> upgrades,
        RunState state,
        Player hostPlayer,
        CatchUpRewardPlan rewardPlan)
    {
        Dictionary<ModelId, List<SerializableCard>> authoritativeCards = baseline.Deck
            .Concat(rewardPlan.Floors.SelectMany(floor => floor.Rooms)
                .SelectMany(room => room.CardRewardGroups.SelectMany(group => group.Cards)
                    .Concat(room.Shop?.Cards.Select(offer => offer.Card) ?? [])))
            .Concat(floors
                .Select(floor => GetReferenceEntry(state, floor, hostPlayer))
                .SelectMany(entry => entry.CardsGained))
            .GroupBy(card => RequireCard(card.Id))
            .ToDictionary(group => group.Key, group => group.ToList());
        Dictionary<ModelId, int> maxBaseUpgrade = authoritativeCards.Values
            .SelectMany(cards => cards)
            .GroupBy(card => RequireCard(card.Id))
            .ToDictionary(group => group.Key, group => group.Max(card => card.CurrentUpgradeLevel));

        List<SerializableCard> result = new();
        foreach (SerializableCard card in submitted)
        {
            ModelId id = RequireCard(card.Id);
            int upgradeAllowance = maxBaseUpgrade.GetValueOrDefault(id) + upgrades.GetValueOrDefault(id);
            if (card.CurrentUpgradeLevel < 0 || card.CurrentUpgradeLevel > upgradeAllowance)
            {
                throw new InvalidOperationException($"Card {id.Entry} has an invalid upgrade level.");
            }

            if (!authoritativeCards.TryGetValue(id, out List<SerializableCard>? templates))
            {
                throw new InvalidOperationException($"Card {id.Entry} has no authoritative template.");
            }
            int templateIndex = templates.FindIndex(template => CardMetadataMatches(template, card));
            if (templateIndex < 0)
            {
                throw new InvalidOperationException($"Card {id.Entry} has unrecorded serialized state.");
            }
            SerializableCard template = templates[templateIndex];
            templates.RemoveAt(templateIndex);
            result.Add(new SerializableCard
            {
                Id = id,
                CurrentUpgradeLevel = card.CurrentUpgradeLevel,
                Enchantment = template.Enchantment,
                Props = template.Props,
                FloorAddedToDeck = template.FloorAddedToDeck
            });
        }
        return result;
    }

    private static List<SerializableRelic> SanitizeRelics(
        SerializablePlayer baseline,
        IReadOnlyList<ModelId> acquiredRelics,
        IReadOnlyList<SerializableRelic> submitted,
        int floorCount,
        CatchUpRewardPlan rewardPlan)
    {
        List<SerializableRelic> result = baseline.Relics.ToList();
        Dictionary<ModelId, Queue<SerializableRelic>> submittedById = submitted
            .GroupBy(relic => RequireRelic(relic.Id))
            .ToDictionary(group => group.Key, group => new Queue<SerializableRelic>(group));
        foreach (SerializableRelic relic in baseline.Relics)
        {
            submittedById[RequireRelic(relic.Id)].Dequeue();
        }
        Dictionary<ModelId, Queue<SerializableRelic>> ancientRelics = rewardPlan.Floors
            .SelectMany(floor => floor.Rooms)
            .SelectMany(room => room.AncientRelicChoices)
            .Select(offer => offer.Relic)
            .GroupBy(relic => RequireRelic(relic.Id))
            .ToDictionary(group => group.Key, group => new Queue<SerializableRelic>(group));
        foreach (ModelId id in acquiredRelics)
        {
            RelicModel canonical = ModelDb.GetById<RelicModel>(id);
            if (!canonical.IsStackable && result.Any(relic => RequireRelic(relic.Id) == id))
            {
                throw new InvalidOperationException($"Relic {id.Entry} cannot be acquired more than once.");
            }
            SerializableRelic submittedRelic = submittedById[id].Dequeue();
            if (submittedRelic.FloorAddedToDeck is not int floor || floor < 1 || floor > floorCount)
            {
                throw new InvalidOperationException($"Relic {id.Entry} has an invalid acquisition floor.");
            }
            SerializableRelic template = ancientRelics.TryGetValue(id, out Queue<SerializableRelic>? ancientQueue) &&
                                           ancientQueue.Count > 0
                ? ancientQueue.Dequeue()
                : canonical.ToMutable().ToSerializable();
            SerializableRelic sanitized = new()
            {
                Id = template.Id,
                Props = template.Props
            };
            sanitized.FloorAddedToDeck = floor;
            result.Add(sanitized);
        }
        return result;
    }

    private static void ValidateSubmittedRelics(
        IReadOnlyList<SerializableRelic> submitted,
        IReadOnlyList<SerializableRelic> expected)
    {
        Dictionary<ModelId, int> submittedCounts = CountIds(submitted.Select(relic => RequireRelic(relic.Id)));
        Dictionary<ModelId, int> expectedCounts = CountIds(expected.Select(relic => RequireRelic(relic.Id)));
        bool stateMatches = submitted.Count == expected.Count && submitted.Zip(expected).All(pair =>
            pair.First.Id == pair.Second.Id &&
            pair.First.FloorAddedToDeck == pair.Second.FloorAddedToDeck &&
            string.Equals(pair.First.Props?.ToString(), pair.Second.Props?.ToString(), StringComparison.Ordinal));
        if (!CountsEqual(submittedCounts, expectedCounts) || !stateMatches)
        {
            throw new InvalidOperationException("The submitted relics do not match the recorded catch-up choices.");
        }
    }

    private static void ValidateSubmittedPotions(
        IReadOnlyList<SerializablePotion> submitted,
        SerializablePlayer baseline,
        IReadOnlySet<ModelId> allowedPotionIds,
        IReadOnlyDictionary<ModelId, int> baselinePotions,
        IReadOnlyDictionary<ModelId, int> requiredAdds,
        IReadOnlyDictionary<ModelId, int> requiredRemovals)
    {
        if (submitted.Count > baseline.MaxPotionSlotCount ||
            submitted.Select(potion => potion.SlotIndex).Distinct().Count() != submitted.Count)
        {
            throw new InvalidOperationException("The submitted potion slots are invalid.");
        }
        foreach (SerializablePotion potion in submitted)
        {
            ModelId id = RequirePotion(potion.Id);
            if (!allowedPotionIds.Contains(id) ||
                potion.SlotIndex < 0 || potion.SlotIndex >= baseline.MaxPotionSlotCount)
            {
                throw new InvalidOperationException("A submitted potion was not available during catch-up.");
            }
        }

        Dictionary<ModelId, int> submittedCounts = CountIds(
            submitted.Select(potion => RequirePotion(potion.Id)));
        HashSet<ModelId> allIds = baselinePotions.Keys
            .Concat(requiredAdds.Keys)
            .Concat(requiredRemovals.Keys)
            .Concat(submittedCounts.Keys)
            .ToHashSet();
        foreach (ModelId id in allIds)
        {
            int expected = baselinePotions.GetValueOrDefault(id) + requiredAdds.GetValueOrDefault(id) -
                           requiredRemovals.GetValueOrDefault(id);
            int actual = submittedCounts.GetValueOrDefault(id);
            if (actual != expected)
            {
                throw new InvalidOperationException("The submitted potions do not match the recorded catch-up choices.");
            }
        }
    }

    private static PlayerMapPointHistoryEntry GetReferenceEntry(
        RunState state,
        MapPointHistoryEntry floor,
        Player hostPlayer)
    {
        List<PlayerMapPointHistoryEntry> originals = floor.PlayerStats
            .Where(entry => entry.PlayerId != hostPlayer.NetId)
            .ToList();
        return originals.FirstOrDefault(entry =>
                   state.GetPlayer(entry.PlayerId)?.Character.Id == hostPlayer.Character.Id)
               ?? originals.First();
    }

    private static bool CanReplayRelic(RelicModel relic)
    {
        return !relic.HasUponPickupEffect &&
               relic.GetType().GetMethod(nameof(RelicModel.AfterObtained))?.DeclaringType == typeof(RelicModel);
    }

    private static bool CardMetadataMatches(SerializableCard authoritative, SerializableCard submitted)
    {
        return authoritative.Id == submitted.Id &&
               authoritative.FloorAddedToDeck == submitted.FloorAddedToDeck &&
               string.Equals(authoritative.Props?.ToString(), submitted.Props?.ToString(), StringComparison.Ordinal) &&
               authoritative.Enchantment?.Id == submitted.Enchantment?.Id &&
               authoritative.Enchantment?.Amount == submitted.Enchantment?.Amount &&
               string.Equals(
                   authoritative.Enchantment?.Props?.ToString(),
                   submitted.Enchantment?.Props?.ToString(),
                   StringComparison.Ordinal);
    }

    private static bool HasRecordedEventOutcome(PlayerMapPointHistoryEntry entry)
    {
        return entry.GoldSpent > 0 || entry.MaxHpGained > 0 || entry.MaxHpLost > 0 ||
               entry.HpHealed > 0 || entry.CardsGained.Count > 0 || entry.CardsRemoved.Count > 0 ||
               entry.UpgradedCards.Count > 0 || entry.RelicChoices.Any(choice => choice.wasPicked) ||
               entry.PotionChoices.Any(choice => choice.wasPicked);
    }

    private static int GetExpectedSpend(
        MapPointHistoryEntry floor,
        CatchUpFloorRewardPlan floorPlan,
        PlayerMapPointHistoryEntry entry,
        PlayerMapPointHistoryEntry reference,
        int removalPriceIndex)
    {
        int spend = 0;
        if (floor.HasRoomOfType(RoomType.Shop))
        {
            List<CatchUpShopCardOffer> cardOffers = floorPlan.Rooms
                .SelectMany(room => room.Shop?.Cards ?? [])
                .ToList();
            Dictionary<ModelId, int> relicCosts = floorPlan.Rooms
                .SelectMany(room => room.Shop?.Relics ?? [])
                .ToDictionary(offer => offer.ModelId, offer => offer.Cost);
            Dictionary<ModelId, int> potionCosts = floorPlan.Rooms
                .SelectMany(room => room.Shop?.Potions ?? [])
                .ToDictionary(offer => offer.ModelId, offer => offer.Cost);
            spend += entry.CardChoices.Where(choice => choice.wasPicked)
                .Where(choice => cardOffers.Any(offer => CardMetadataMatches(offer.Card, choice.Card)))
                .Sum(choice => cardOffers.First(offer => CardMetadataMatches(offer.Card, choice.Card)).Cost);
            spend += entry.RelicChoices.Where(choice => choice.wasPicked)
                .Where(choice => relicCosts.ContainsKey(RequireRelic(choice.choice)))
                .Sum(choice => relicCosts[RequireRelic(choice.choice)]);
            spend += entry.BoughtPotions
                .Sum(id => potionCosts[RequirePotion(id)]);
            for (int i = 0; i < entry.CardsRemoved.Count; i++)
            {
                spend += 75 + 25 * (removalPriceIndex + i);
            }
        }
        if (floor.HasRoomOfType(RoomType.Event) &&
            !floor.HasRoomOfType(RoomType.Shop) &&
            HasRecordedEventOutcome(entry))
        {
            spend += Math.Max(0, reference.GoldSpent);
        }
        return spend;
    }

    private static Dictionary<ModelId, int> CountCards(IEnumerable<SerializableCard> cards)
    {
        return CountIds(cards.Select(card => RequireCard(card.Id)));
    }

    private static Dictionary<ModelId, int> CountIds(IEnumerable<ModelId> ids)
    {
        Dictionary<ModelId, int> result = new();
        foreach (ModelId id in ids)
        {
            Increment(result, id);
        }
        return result;
    }

    private static bool CountsEqual(
        IReadOnlyDictionary<ModelId, int> left,
        IReadOnlyDictionary<ModelId, int> right)
    {
        return left.Where(pair => pair.Value != 0).All(pair => right.GetValueOrDefault(pair.Key) == pair.Value) &&
               right.Where(pair => pair.Value != 0).All(pair => left.GetValueOrDefault(pair.Key) == pair.Value);
    }

    private static void Increment(Dictionary<ModelId, int> counts, ModelId id)
    {
        Add(counts, id, 1);
    }

    private static void Add(Dictionary<ModelId, int> counts, ModelId id, int amount)
    {
        counts[id] = counts.GetValueOrDefault(id) + amount;
    }

    private static ModelId RequireCard(ModelId? id)
    {
        if (id is not ModelId value || ModelDb.GetByIdOrNull<CardModel>(value) == null)
        {
            throw new InvalidOperationException("The catch-up payload contains an unknown card.");
        }
        return value;
    }

    private static ModelId RequireRelic(ModelId? id)
    {
        if (id is not ModelId value || ModelDb.GetByIdOrNull<RelicModel>(value) == null)
        {
            throw new InvalidOperationException("The catch-up payload contains an unknown relic.");
        }
        return value;
    }

    private static ModelId RequirePotion(ModelId? id)
    {
        if (id is not ModelId value || ModelDb.GetByIdOrNull<PotionModel>(value) == null)
        {
            throw new InvalidOperationException("The catch-up payload contains an unknown potion.");
        }
        return value;
    }
}
