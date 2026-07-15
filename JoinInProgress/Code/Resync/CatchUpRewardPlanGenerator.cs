using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Ascension;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Factories;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Runs.History;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Runs;

namespace JoinInProgress.Resync;

/// <summary>
/// Generates a personal catch-up offer stream from the late player's native RNG, odds and relic bag.
/// Generation is transactional: the returned plan contains the progressed per-player state, while the supplied
/// player, run-wide relic bag and run-state card registry are restored before this method returns.
/// </summary>
public static class CatchUpRewardPlanGenerator
{
    private static readonly System.Reflection.FieldInfo CurrentActIndexField =
        AccessTools.Field(typeof(RunState), "_currentActIndex");

    public static Task<CatchUpRewardPlan> GenerateAsync(RunState state, Player player)
    {
        if (!ReferenceEquals(player.RunState, state))
        {
            throw new InvalidOperationException("The catch-up player does not belong to the supplied run state.");
        }
        if (RunManager.Instance == null)
        {
            throw new InvalidOperationException("Catch-up rewards can only be generated during an active run.");
        }

        SerializablePlayerRngSet initialRng = player.PlayerRng.ToSerializable();
        SerializablePlayerOddsSet initialOdds = player.PlayerOdds.ToSerializable();
        SerializableRelicGrabBag initialRelicBag = player.RelicGrabBag.ToSerializable();
        SerializableRelicGrabBag initialSharedRelicBag = state.SharedRelicGrabBag.ToSerializable();
        int initialActIndex = state.CurrentActIndex;
        int initialHp = player.Creature.CurrentHp;
        int initialMaxHp = player.Creature.MaxHp;
        List<CardModel> scratchCards = new();

        CatchUpRewardPlan plan = new();
        int globalFloor = 0;
        try
        {
            for (int actIndex = 0; actIndex < state.MapPointHistory.Count; actIndex++)
            {
                CurrentActIndexField.SetValue(state, actIndex);
                IReadOnlyList<MapPointHistoryEntry> act = state.MapPointHistory[actIndex];
                for (int floorIndex = 0; floorIndex < act.Count; floorIndex++)
                {
                    globalFloor++;
                    MapPointHistoryEntry history = act[floorIndex];
                    CatchUpFloorRewardPlan floorPlan = new()
                    {
                        ActIndex = actIndex,
                        FloorIndex = floorIndex,
                        GlobalFloor = globalFloor,
                        MapPointType = history.MapPointType
                    };

                    bool recordedAncient = history.MapPointType == MapPointType.Ancient &&
                                           history.PlayerStats.Any(entry =>
                                               entry.PlayerId != player.NetId && entry.AncientChoices.Count > 0);
                    foreach (MapPointRoomHistoryEntry room in history.Rooms.Where(room =>
                                 room.RoomType is not RoomType.Map and not RoomType.Unassigned))
                    {
                        CatchUpRoomRewardPlan roomPlan = new()
                        {
                            RoomType = room.RoomType,
                            ModelId = room.ModelId
                        };
                        switch (room.RoomType)
                        {
                            case RoomType.Monster:
                            case RoomType.Elite:
                            case RoomType.Boss:
                                GenerateCombatRoom(state, player, history, room, roomPlan, scratchCards);
                                break;
                            case RoomType.Shop:
                                GenerateShop(player, roomPlan, scratchCards);
                                break;
                            case RoomType.Treasure:
                                roomPlan.RelicRewards.Add(PullPersonalRelicFromFront(state, player).Id);
                                break;
                            case RoomType.Event when recordedAncient:
                                GenerateAncientChoices(player, room, roomPlan);
                                break;
                        }
                        floorPlan.Rooms.Add(roomPlan);
                    }
                    plan.Floors.Add(floorPlan);
                }
            }

            plan.FinalPlayerRng = player.PlayerRng.ToSerializable();
            plan.FinalPlayerOdds = player.PlayerOdds.ToSerializable();
            plan.FinalRelicGrabBag = player.RelicGrabBag.ToSerializable();
            return Task.FromResult(plan);
        }
        finally
        {
            foreach (CardModel card in scratchCards)
            {
                if (state.ContainsCard(card))
                {
                    state.RemoveCard(card);
                }
            }
            player.PlayerRng.LoadFromSerializable(initialRng);
            player.PlayerOdds.LoadFromSerializable(initialOdds);
            player.RelicGrabBag.LoadFromSerializable(initialRelicBag);
            state.SharedRelicGrabBag.LoadFromSerializable(initialSharedRelicBag);
            player.Creature.SetMaxHpInternal(initialMaxHp);
            player.Creature.SetCurrentHpInternal(initialHp);
            CurrentActIndexField.SetValue(state, initialActIndex);
        }
    }

    /// <summary>
    /// Commits the per-player progression consumed while the host generated the plan. Call only after the host has
    /// retained the plan for submission validation.
    /// </summary>
    public static void ApplyProgression(Player player, CatchUpRewardPlan plan)
    {
        player.PlayerRng.LoadFromSerializable(plan.FinalPlayerRng);
        player.PlayerOdds.LoadFromSerializable(plan.FinalPlayerOdds);
        player.RelicGrabBag.LoadFromSerializable(plan.FinalRelicGrabBag);
    }

    private static void GenerateCombatRoom(
        RunState state,
        Player player,
        MapPointHistoryEntry floor,
        MapPointRoomHistoryEntry room,
        CatchUpRoomRewardPlan plan,
        ICollection<CardModel> scratchCards)
    {
        int recordedCardCount = floor.PlayerStats
            .Where(entry => entry.PlayerId != player.NetId)
            .Select(entry => entry.CardChoices.Count)
            .DefaultIfEmpty(0)
            .Max();
        int groupCount = (recordedCardCount + 2) / 3;
        for (int groupIndex = 0; groupIndex < groupCount; groupIndex++)
        {
            CardCreationOptions options = CardCreationOptions.ForRoom(player, room.RoomType)
                .WithFlags(CardCreationFlags.NoModifyHooks);
            List<CardCreationResult> createdCards = CardFactory.CreateForReward(player, 3, options).ToList();
            foreach (CardCreationResult result in createdCards)
            {
                scratchCards.Add(result.Card);
            }
            plan.CardRewardGroups.Add(new CatchUpCardRewardGroup
            {
                Cards = createdCards.Select(result => result.Card.ToSerializable()).ToList()
            });
        }

        if (room.ModelId is ModelId encounterId &&
            floor.PlayerStats.Any(entry => entry.PlayerId != player.NetId && entry.GoldGained > 0) &&
            ModelDb.GetByIdOrNull<EncounterModel>(encounterId) is EncounterModel encounter)
        {
            GoldReward gold = new(encounter.MinGoldReward, encounter.MaxGoldReward, player);
            gold.Populate();
            plan.Gold = gold.Amount;
        }

        if (player.PlayerOdds.PotionReward.Roll(
                player, RunManager.Instance.AscensionManager, room.RoomType))
        {
            PotionModel potion = PotionFactory.CreateRandomPotionOutOfCombat(
                player, player.PlayerRng.Rewards);
            plan.PotionRewards.Add(potion.Id);
        }

        if (room.RoomType == RoomType.Elite)
        {
            plan.RelicRewards.Add(PullPersonalRelicFromFront(state, player).Id);
        }
    }

    private static void GenerateShop(
        Player player,
        CatchUpRoomRewardPlan roomPlan,
        ICollection<CardModel> scratchCards)
    {
        MerchantInventory inventory = MerchantInventory.CreateForNormalMerchant(player);
        CatchUpShopRewardPlan shop = new();
        foreach (MerchantCardEntry entry in inventory.CharacterCardEntries)
        {
            AddShopCard(entry, isColorless: false, shop, scratchCards);
        }
        foreach (MerchantCardEntry entry in inventory.ColorlessCardEntries)
        {
            AddShopCard(entry, isColorless: true, shop, scratchCards);
        }
        foreach (MerchantRelicEntry entry in inventory.RelicEntries.Where(entry => entry.Model != null))
        {
            shop.Relics.Add(new CatchUpShopModelOffer
            {
                ModelId = entry.Model!.Id,
                Cost = entry.Cost
            });
        }
        foreach (MerchantPotionEntry entry in inventory.PotionEntries.Where(entry => entry.Model != null))
        {
            shop.Potions.Add(new CatchUpShopModelOffer
            {
                ModelId = entry.Model!.Id,
                Cost = entry.Cost
            });
        }
        roomPlan.Shop = shop;
    }

    private static void AddShopCard(
        MerchantCardEntry entry,
        bool isColorless,
        CatchUpShopRewardPlan shop,
        ICollection<CardModel> scratchCards)
    {
        if (entry.CreationResult == null)
        {
            return;
        }
        CardModel card = entry.CreationResult.Card;
        scratchCards.Add(card);
        shop.Cards.Add(new CatchUpShopCardOffer
        {
            Card = card.ToSerializable(),
            Cost = entry.Cost,
            IsColorless = isColorless,
            IsOnSale = entry.IsOnSale
        });
    }

    private static RelicModel PullPersonalRelicFromFront(RunState state, Player player)
    {
        RelicRarity rarity = RelicFactory.RollRarity(player.PlayerRng.Rewards);
        return player.RelicGrabBag.PullFromFront(rarity, state) ?? RelicFactory.FallbackRelic;
    }

    private static void GenerateAncientChoices(
        Player player,
        MapPointRoomHistoryEntry room,
        CatchUpRoomRewardPlan plan)
    {
        if (room.ModelId is not ModelId eventId ||
            ModelDb.GetByIdOrNull<AncientEventModel>(eventId) is not AncientEventModel canonical)
        {
            return;
        }

        AncientEventModel ancient = (AncientEventModel)canonical.ToMutable();
        AccessTools.Property(typeof(EventModel), nameof(EventModel.Owner)).SetValue(ancient, player);
        MegaCrit.Sts2.Core.Random.Rng eventRng = new(player, eventId);
        AccessTools.Property(typeof(EventModel), nameof(EventModel.Rng)).SetValue(
            ancient,
            eventRng);
        ancient.CalculateVars();
        var options = (IReadOnlyList<MegaCrit.Sts2.Core.Events.EventOption>)AccessTools
            .Method(typeof(AncientEventModel), "GenerateInitialOptionsWrapper")
            .Invoke(ancient, null)!;
        foreach (var option in options.Where(option =>
                     !option.IsLocked && option.Relic != null && CanReplayRelic(option.Relic)))
        {
            plan.AncientRelicChoices.Add(new CatchUpAncientRelicOffer
            {
                Relic = option.Relic!.ToSerializable(),
                OptionTextKey = option.TextKey
            });
        }
        HashSet<ModelId> offeredRelics = plan.AncientRelicChoices
            .Select(offer => offer.Relic.Id)
            .Where(id => id != null)
            .Select(id => id!)
            .ToHashSet();
        List<RelicModel> fallbackRelics = ModelDb.AllRelicPools
            .SelectMany(pool => pool.GetUnlockedRelics(player.UnlockState))
            .Where(relic => relic.Rarity == RelicRarity.Ancient &&
                            CanReplayRelic(relic) &&
                            !offeredRelics.Contains(relic.Id) &&
                            (relic.IsStackable || player.Relics.All(owned => owned.Id != relic.Id)))
            .DistinctBy(relic => relic.Id)
            .OrderBy(relic => relic.Id.Entry, StringComparer.Ordinal)
            .ToList();
        while (plan.AncientRelicChoices.Count < 3 && fallbackRelics.Count > 0)
        {
            RelicModel fallback = eventRng.NextItem(fallbackRelics)!;
            fallbackRelics.Remove(fallback);
            plan.AncientRelicChoices.Add(new CatchUpAncientRelicOffer
            {
                Relic = fallback.ToMutable().ToSerializable(),
                OptionTextKey = fallback.Id.Entry
            });
        }
        plan.AncientHealPercent = RunManager.Instance.HasAscension(AscensionLevel.WearyTraveler) ? 80 : 100;
    }

    private static bool CanReplayRelic(RelicModel relic)
    {
        return !relic.HasUponPickupEffect &&
               relic.GetType().GetMethod(nameof(RelicModel.AfterObtained))?.DeclaringType == typeof(RelicModel);
    }
}
