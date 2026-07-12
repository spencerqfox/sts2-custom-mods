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
        IReadOnlyList<PlayerMapPointHistoryEntry> history)
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

        Dictionary<ModelId, int> expectedDeck = CountCards(baseline.Deck);
        Dictionary<ModelId, int> gainedCards = new();
        Dictionary<ModelId, int> removedCards = new();
        Dictionary<ModelId, int> upgrades = new();
        List<ModelId> acquiredRelics = new();
        Dictionary<ModelId, int> baselinePotions = CountIds(
            baseline.Potions.Select(potion => RequirePotion(potion.Id)));
        Dictionary<ModelId, int> requiredPotionAdds = new();
        Dictionary<ModelId, int> optionalEventPotionAdds = new();
        HashSet<ModelId> allowedPotionIds = baseline.Potions.Select(potion => RequirePotion(potion.Id)).ToHashSet();
        int shopRemovalCount = 0;
        int currentHp = baseline.CurrentHp;
        int maxHp = baseline.MaxHp;
        int gold = baseline.Gold;
        int removalPriceIndex = baseline.ExtraFields.CardShopRemovalsUsed;

        for (int i = 0; i < floors.Count; i++)
        {
            MapPointHistoryEntry floor = floors[i];
            PlayerMapPointHistoryEntry entry = history[i];
            PlayerMapPointHistoryEntry reference = floor.PlayerStats.First(stat => stat.PlayerId != hostPlayer.NetId);
            ValidateHistoryEntryShape(entry, reference, hostPlayer.NetId);

            HashSet<ModelId> allowedCards = reference.CardChoices
                .Select(choice => RequireCard(choice.Card.Id))
                .Concat(reference.CardsGained.Select(card => RequireCard(card.Id)))
                .ToHashSet();
            HashSet<ModelId> allowedRelics = reference.RelicChoices
                .Select(choice => RequireRelic(choice.choice))
                .ToHashSet();
            HashSet<ModelId> floorPotionIds = reference.PotionChoices
                .Select(choice => RequirePotion(choice.choice))
                .ToHashSet();
            allowedPotionIds.UnionWith(floorPotionIds);
            bool hasCombat = floor.Rooms.Any(room => room.RoomType is RoomType.Monster or RoomType.Elite or RoomType.Boss);
            bool hasShop = floor.HasRoomOfType(RoomType.Shop);
            bool hasTreasure = floor.HasRoomOfType(RoomType.Treasure);
            bool eventOnly = floor.Rooms.Count > 0 && floor.Rooms.All(room => room.RoomType == RoomType.Event);
            bool followedEvent = entry.EventChoices.Count > 0;
            int distinctCardOffers = reference.CardChoices.Select(choice => RequireCard(choice.Card.Id)).Distinct().Count();
            int distinctRelicOffers = reference.RelicChoices.Select(choice => RequireRelic(choice.choice)).Distinct().Count();
            int maxCardGains = (hasCombat && reference.CardChoices.Count > 0 ? 1 : 0) +
                               (hasShop ? distinctCardOffers : 0) +
                               (eventOnly && followedEvent ? reference.CardsGained.Count : 0);
            int maxRelicGains = (hasCombat && reference.RelicChoices.Count > 0 ? 1 : 0) +
                                (hasTreasure && reference.RelicChoices.Count > 0 ? 1 : 0) +
                                (hasShop ? distinctRelicOffers : 0) +
                                (eventOnly && followedEvent
                                    ? reference.RelicChoices.Count(choice => choice.wasPicked)
                                    : 0);
            int maxCardRemovals = (hasShop ? 1 : 0) +
                                  (eventOnly && followedEvent ? reference.CardsRemoved.Count : 0);

            if (entry.CardsGained.Count > maxCardGains ||
                entry.RelicChoices.Count(choice => choice.wasPicked) > maxRelicGains ||
                entry.CardsRemoved.Count > maxCardRemovals ||
                entry.BoughtPotions.Count > (hasShop ? floorPotionIds.Count : 0))
            {
                throw new InvalidOperationException($"Too many rewards were selected on catch-up floor {i + 1}.");
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
                    if (relic.HasUponPickupEffect)
                    {
                        throw new InvalidOperationException(
                            $"Relic {relic.Id.Entry} has a pickup effect that cannot be synchronized safely.");
                    }
                    acquiredRelics.Add(id);
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
            if (eventOnly && followedEvent)
            {
                foreach (ModelChoiceHistoryEntry potion in reference.PotionChoices.Where(choice => choice.wasPicked))
                {
                    Increment(optionalEventPotionAdds, RequirePotion(potion.choice));
                }
            }

            int expectedDamage = floor.PlayerStats
                .Where(stat => stat.PlayerId != hostPlayer.NetId)
                .Select(stat => stat.DamageTaken)
                .DefaultIfEmpty(0)
                .Max();
            if (entry.DamageTaken != expectedDamage ||
                entry.GoldGained != Math.Max(0, reference.GoldGained) ||
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
            int healBudget = Math.Max(0, reference.HpHealed) +
                             restCount * (int)Math.Ceiling(maxHp * 0.3m);
            if (entry.HpHealed < 0 ||
                entry.HpHealed > healBudget ||
                entry.HpHealed > Math.Max(0, maxHp - currentHp))
            {
                throw new InvalidOperationException($"The healing ledger was altered on catch-up floor {i + 1}.");
            }
            currentHp = Math.Min(maxHp, currentHp + entry.HpHealed);
            currentHp -= Math.Min(entry.DamageTaken, Math.Max(0, currentHp - 1));

            int expectedSpend = GetExpectedSpend(floor, entry, reference, removalPriceIndex);
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
        List<SerializableCard> sanitizedDeck = SanitizeDeck(submitted.Deck, baseline, floors, upgrades);
        List<SerializableRelic> sanitizedRelics = SanitizeRelics(baseline, acquiredRelics);
        ValidateSubmittedRelics(submitted.Relics, sanitizedRelics);
        ValidateSubmittedPotions(
            submitted.Potions,
            baseline,
            allowedPotionIds,
            baselinePotions,
            requiredPotionAdds,
            optionalEventPotionAdds);

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
                        entry.EventChoices.Count + entry.RestSiteChoices.Count + entry.BoughtRelics.Count +
                        entry.BoughtPotions.Count + entry.BoughtColorless.Count + entry.CompletedQuests.Count;
        if (listCount > MaxHistoryEntriesPerFloor)
        {
            throw new InvalidOperationException("A catch-up history entry is too large.");
        }
        if (entry.GoldGained < 0 || entry.GoldSpent < 0 || entry.GoldLost < 0 ||
            entry.DamageTaken < 0 || entry.HpHealed < 0 || entry.MaxHpGained < 0 || entry.MaxHpLost < 0 ||
            entry.GoldStolen != 0 || entry.StolenLoot != 0 ||
            entry.PotionChoices.Count != 0 || entry.PotionDiscarded.Count != 0 || entry.PotionUsed.Count != 0 ||
            entry.RelicsRemoved.Count != 0 || entry.CardsEnchanted.Count != 0 ||
            entry.CardsTransformed.Count != 0 || entry.DowngradedCards.Count != 0 ||
            entry.AncientChoices.Count != 0 || entry.BoughtRelics.Count != 0 ||
            entry.BoughtColorless.Count != 0 || entry.CompletedQuests.Count != 0)
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
        IReadOnlyDictionary<ModelId, int> upgrades)
    {
        Dictionary<ModelId, Queue<SerializableCard>> baselineCards = baseline.Deck
            .GroupBy(card => RequireCard(card.Id))
            .ToDictionary(group => group.Key, group => new Queue<SerializableCard>(group));
        Dictionary<ModelId, SerializableCard> recordedCards = floors
            .Select(floor => floor.PlayerStats.First(stat => stat.PlayerId != baseline.NetId))
            .SelectMany(entry => entry.CardChoices.Select(choice => choice.Card).Concat(entry.CardsGained))
            .GroupBy(card => RequireCard(card.Id))
            .ToDictionary(group => group.Key, group => group.First());
        Dictionary<ModelId, int> maxBaseUpgrade = baseline.Deck
            .Concat(recordedCards.Values)
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

            SerializableCard template;
            if (baselineCards.TryGetValue(id, out Queue<SerializableCard>? queue) && queue.Count > 0)
            {
                template = queue.Dequeue();
            }
            else if (!recordedCards.TryGetValue(id, out template!))
            {
                throw new InvalidOperationException($"Card {id.Entry} has no authoritative template.");
            }
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
        IReadOnlyList<ModelId> acquiredRelics)
    {
        List<SerializableRelic> result = baseline.Relics.ToList();
        foreach (ModelId id in acquiredRelics)
        {
            RelicModel canonical = ModelDb.GetById<RelicModel>(id);
            if (!canonical.IsStackable && result.Any(relic => RequireRelic(relic.Id) == id))
            {
                throw new InvalidOperationException($"Relic {id.Entry} cannot be acquired more than once.");
            }
            result.Add(canonical.ToMutable().ToSerializable());
        }
        return result;
    }

    private static void ValidateSubmittedRelics(
        IReadOnlyList<SerializableRelic> submitted,
        IReadOnlyList<SerializableRelic> expected)
    {
        Dictionary<ModelId, int> submittedCounts = CountIds(submitted.Select(relic => RequireRelic(relic.Id)));
        Dictionary<ModelId, int> expectedCounts = CountIds(expected.Select(relic => RequireRelic(relic.Id)));
        if (!CountsEqual(submittedCounts, expectedCounts))
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
        IReadOnlyDictionary<ModelId, int> optionalEventAdds)
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
            .Concat(optionalEventAdds.Keys)
            .Concat(submittedCounts.Keys)
            .ToHashSet();
        foreach (ModelId id in allIds)
        {
            int minimum = baselinePotions.GetValueOrDefault(id) + requiredAdds.GetValueOrDefault(id);
            int maximum = minimum + optionalEventAdds.GetValueOrDefault(id);
            int actual = submittedCounts.GetValueOrDefault(id);
            if (actual < minimum || actual > maximum)
            {
                throw new InvalidOperationException("The submitted potions do not match the recorded catch-up choices.");
            }
        }
    }

    private static int GetExpectedSpend(
        MapPointHistoryEntry floor,
        PlayerMapPointHistoryEntry entry,
        PlayerMapPointHistoryEntry reference,
        int removalPriceIndex)
    {
        int spend = 0;
        if (floor.HasRoomOfType(RoomType.Shop))
        {
            spend += entry.CardChoices.Where(choice => choice.wasPicked)
                .Sum(choice => GetCardPrice(ModelDb.GetById<CardModel>(RequireCard(choice.Card.Id))));
            spend += entry.RelicChoices.Where(choice => choice.wasPicked)
                .Sum(choice => ModelDb.GetById<RelicModel>(RequireRelic(choice.choice)).MerchantCost);
            spend += entry.BoughtPotions
                .Sum(id => GetPotionPrice(ModelDb.GetById<PotionModel>(RequirePotion(id))));
            for (int i = 0; i < entry.CardsRemoved.Count; i++)
            {
                spend += 75 + 25 * (removalPriceIndex + i);
            }
        }
        if (floor.HasRoomOfType(RoomType.Event) &&
            !floor.HasRoomOfType(RoomType.Shop) &&
            entry.EventChoices.Count > 0)
        {
            spend += Math.Max(0, reference.GoldSpent);
        }
        return spend;
    }

    private static int GetCardPrice(CardModel card)
    {
        int cost = card.Rarity switch
        {
            CardRarity.Rare => 150,
            CardRarity.Uncommon => 75,
            _ => 50
        };
        return card.Pool is ColorlessCardPool ? (int)Math.Round(cost * 1.15f) : cost;
    }

    private static int GetPotionPrice(PotionModel potion)
    {
        return potion.Rarity switch
        {
            PotionRarity.Rare => 100,
            PotionRarity.Uncommon => 75,
            _ => 50
        };
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
