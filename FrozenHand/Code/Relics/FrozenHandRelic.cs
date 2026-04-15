using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using BaseLib.Abstracts;
using BaseLib.Utils;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.RelicPools;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Runs;

namespace FrozenHand.Relics;

[Pool(typeof(SharedRelicPool))]
public sealed class FrozenHandRelic : CustomRelicModel
{
    private const string CustomIconPath = "res://FrozenHand/images/relics/frozen_hand.png";
    private static readonly JsonSerializerOptions SnapshotJsonOptions = new()
    {
        IncludeFields = true
    };

    public override RelicRarity Rarity => RelicRarity.Ancient;

    public override string PackedIconPath => CustomIconPath;

    protected override string BigIconPath => CustomIconPath;

    protected override string PackedIconOutlinePath => CustomIconPath;

    public override bool IsAllowed(IRunState runState) => false;

    [SavedProperty]
    public bool FrozenHandHasStoredPiles { get; set; }

    [SavedProperty]
    public string FrozenHandStoredSnapshotJson { get; set; } = string.Empty;

    public override async Task AfterCardGeneratedForCombat(CardModel card, bool addedByPlayer)
    {
        if (!ShouldCreatePermanentDeckVersion(card))
        {
            return;
        }

        var deckCard = Owner.RunState.LoadCard(card.ToSerializable(), Owner);
        var addResult = await CardPileCmd.Add(deckCard, PileType.Deck, source: this, skipVisuals: true);
        if (!addResult.success)
        {
            Owner.RunState.RemoveCard(deckCard);
            return;
        }

        card.DeckVersion = addResult.cardAdded;
    }

    public override Task AfterCombatEnd(CombatRoom room)
    {
        if (Owner.PlayerCombatState is null)
        {
            return Task.CompletedTask;
        }

        SavePiles(Owner);
        return Task.CompletedTask;
    }

    internal bool TryPopulateCombatStateFromStoredPiles(Player owner, CombatState combatState)
    {
        if (!FrozenHandHasStoredPiles ||
            string.IsNullOrWhiteSpace(FrozenHandStoredSnapshotJson) ||
            owner.PlayerCombatState is null)
        {
            return false;
        }

        var snapshot = DeserializeSnapshot();
        if (snapshot is null)
        {
            return false;
        }

        RestorePiles(owner, combatState, snapshot);
        return true;
    }

    private void SavePiles(Player owner)
    {
        var deckCards = owner.Deck.Cards.ToList();
        var deckIndexLookup = deckCards
            .Select((card, index) => new { card, index })
            .ToDictionary(pair => pair.card, pair => pair.index);
        var combatState = owner.PlayerCombatState!;

        var playPileToDiscard = new List<CardModel>();
        var playPileToExhaust = new List<CardModel>();

        foreach (var card in combatState.PlayPile.Cards)
        {
            switch (GetPersistedPlayPileDestination(card))
            {
                case PileType.Discard:
                    playPileToDiscard.Add(card);
                    break;
                case PileType.Exhaust:
                    playPileToExhaust.Add(card);
                    break;
            }
        }

        var snapshot = new FrozenHandSnapshotData
        {
            Hand = SnapshotPile(combatState.Hand.Cards, deckIndexLookup),
            Draw = SnapshotPile(combatState.DrawPile.Cards, deckIndexLookup),
            Discard = SnapshotPile(combatState.DiscardPile.Cards.Concat(playPileToDiscard), deckIndexLookup),
            Exhaust = SnapshotPile(combatState.ExhaustPile.Cards.Concat(playPileToExhaust), deckIndexLookup)
        };

        FrozenHandStoredSnapshotJson = JsonSerializer.Serialize(snapshot, SnapshotJsonOptions);
        FrozenHandHasStoredPiles = true;

        var totalCount = snapshot.Hand.Count + snapshot.Draw.Count + snapshot.Discard.Count + snapshot.Exhaust.Count;
        Log.Info(
            $"[FrozenHand] Saved piles Hand:{snapshot.Hand.Count} Draw:{snapshot.Draw.Count} Discard:{snapshot.Discard.Count} Exhaust:{snapshot.Exhaust.Count} Total:{totalCount}");
    }

    private void RestorePiles(Player owner, CombatState combatState, FrozenHandSnapshotData snapshot)
    {
        var playerCombatState = owner.PlayerCombatState!;
        var deckCards = owner.Deck.Cards.ToList();
        var restoredDeckIndexes = new HashSet<int>();

        var restoredHand = RestorePile(
            combatState,
            owner,
            playerCombatState.Hand,
            snapshot.Hand,
            snapshot.Version,
            deckCards,
            restoredDeckIndexes);
        var restoredDraw = RestorePile(
            combatState,
            owner,
            playerCombatState.DrawPile,
            snapshot.Draw,
            snapshot.Version,
            deckCards,
            restoredDeckIndexes);
        var restoredDiscard = RestorePile(
            combatState,
            owner,
            playerCombatState.DiscardPile,
            snapshot.Discard,
            snapshot.Version,
            deckCards,
            restoredDeckIndexes);
        var restoredExhaust = RestorePile(
            combatState,
            owner,
            playerCombatState.ExhaustPile,
            snapshot.Exhaust,
            snapshot.Version,
            deckCards,
            restoredDeckIndexes);
        var appendedDeckCards = AppendMissingDeckCards(
            combatState,
            playerCombatState.DrawPile,
            deckCards,
            restoredDeckIndexes);

        var totalCount = restoredHand + restoredDraw + restoredDiscard + restoredExhaust + appendedDeckCards;
        Log.Info(
            $"[FrozenHand] Restored piles Hand:{restoredHand} Draw:{restoredDraw} Discard:{restoredDiscard} Exhaust:{restoredExhaust} Appended:{appendedDeckCards} Total:{totalCount}");
    }

    private static List<FrozenHandCardSnapshot> SnapshotPile(
        IEnumerable<CardModel> cards,
        IReadOnlyDictionary<CardModel, int> deckIndexLookup)
    {
        return cards
            .Select(card => FrozenHandCardStateReflection.CreateCardSnapshot(card, deckIndexLookup))
            .ToList();
    }

    private static int RestorePile(
        CombatState combatState,
        Player owner,
        CardPile targetPile,
        IReadOnlyList<FrozenHandCardSnapshot>? snapshots,
        int snapshotVersion,
        IReadOnlyList<CardModel> deckCards,
        ISet<int> restoredDeckIndexes)
    {
        if (snapshots is null)
        {
            return 0;
        }

        var restoredCount = 0;

        foreach (var snapshot in snapshots)
        {
            var restoredCard = RestoreCardSnapshot(
                combatState,
                owner,
                snapshot,
                snapshotVersion,
                deckCards,
                restoredDeckIndexes);
            if (restoredCard is null)
            {
                continue;
            }

            targetPile.AddInternal(restoredCard, silent: true);
            restoredCount++;
        }

        return restoredCount;
    }

    private static CardModel? RestoreCardSnapshot(
        CombatState combatState,
        Player owner,
        FrozenHandCardSnapshot snapshot,
        int snapshotVersion,
        IReadOnlyList<CardModel> deckCards,
        ISet<int> restoredDeckIndexes)
    {
        if (snapshot.Card.Id is null)
        {
            return null;
        }

        if (snapshot.DeckIndex is int deckIndex)
        {
            if (deckIndex < 0 || deckIndex >= deckCards.Count || !restoredDeckIndexes.Add(deckIndex))
            {
                return null;
            }
        }

        var restoredCard = CardModel.FromSerializable(snapshot.Card);
        combatState.AddCard(restoredCard, owner);
        CardModel? currentDeckCard = null;

        if (snapshot.DeckIndex is int index)
        {
            currentDeckCard = deckCards[index];
            restoredCard.DeckVersion = currentDeckCard;
        }

        if (currentDeckCard is not null)
        {
            FrozenHandCardStateReflection.ApplyPermanentDeckUpgradeDelta(
                restoredCard,
                currentDeckCard,
                snapshot,
                snapshotVersion);
        }

        return restoredCard;
    }

    private static int AppendMissingDeckCards(
        CombatState combatState,
        CardPile drawPile,
        IReadOnlyList<CardModel> deckCards,
        ISet<int> restoredDeckIndexes)
    {
        var appendedCount = 0;

        for (var index = 0; index < deckCards.Count; index++)
        {
            if (restoredDeckIndexes.Contains(index))
            {
                continue;
            }

            var deckCard = deckCards[index];
            var restoredCard = combatState.CloneCard(deckCard);
            restoredCard.DeckVersion = deckCard;
            drawPile.AddInternal(restoredCard, silent: true);
            appendedCount++;
        }

        return appendedCount;
    }

    private static PileType GetPersistedPlayPileDestination(CardModel card)
    {
        if (card.IsDupe || card.Type == CardType.Power)
        {
            return PileType.None;
        }

        if (card.ExhaustOnNextPlay || card.Keywords.Contains(CardKeyword.Exhaust))
        {
            return PileType.Exhaust;
        }

        return PileType.Discard;
    }

    private bool ShouldCreatePermanentDeckVersion(CardModel card)
    {
        if (card.Owner != Owner || card.DeckVersion is not null || card.IsClone || card.IsDupe)
        {
            return false;
        }

        return card.Type is CardType.Attack or CardType.Skill or CardType.Power;
    }

    private FrozenHandSnapshotData? DeserializeSnapshot()
    {
        try
        {
            return JsonSerializer.Deserialize<FrozenHandSnapshotData>(FrozenHandStoredSnapshotJson, SnapshotJsonOptions);
        }
        catch (Exception ex)
        {
            Log.Warn($"[FrozenHand] Failed to deserialize stored pile snapshot: {ex}");
            return null;
        }
    }
}
