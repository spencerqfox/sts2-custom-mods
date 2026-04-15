using System.Collections.Generic;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Saves.Runs;

namespace FrozenHand.Relics;

internal sealed class FrozenHandSnapshotData
{
    public int Version { get; set; } = 3;

    public List<FrozenHandCardSnapshot> Hand { get; set; } = [];

    public List<FrozenHandCardSnapshot> Draw { get; set; } = [];

    public List<FrozenHandCardSnapshot> Discard { get; set; } = [];

    public List<FrozenHandCardSnapshot> Exhaust { get; set; } = [];
}

internal sealed class FrozenHandCardSnapshot
{
    public int? DeckIndex { get; set; }

    public int? DeckUpgradeLevelAtSave { get; set; }

    public SerializableCard Card { get; set; } = new();
}

internal static class FrozenHandCardStateReflection
{
    public static FrozenHandCardSnapshot CreateCardSnapshot(
        CardModel card,
        IReadOnlyDictionary<CardModel, int> deckIndexLookup)
    {
        return new FrozenHandCardSnapshot
        {
            DeckIndex = GetDeckIndex(card, deckIndexLookup),
            DeckUpgradeLevelAtSave = GetDeckUpgradeLevelAtSave(card, deckIndexLookup),
            Card = card.ToSerializable()
        };
    }

    public static void ApplyPermanentDeckUpgradeDelta(
        CardModel restoredCard,
        CardModel currentDeckCard,
        FrozenHandCardSnapshot snapshot,
        int snapshotVersion)
    {
        if (snapshotVersion < 2 || snapshot.DeckUpgradeLevelAtSave is not int savedDeckUpgradeLevel)
        {
            return;
        }

        var upgradeDelta = currentDeckCard.CurrentUpgradeLevel - savedDeckUpgradeLevel;
        for (var index = 0; index < upgradeDelta && restoredCard.IsUpgradable; index++)
        {
            restoredCard.UpgradeInternal();
            restoredCard.FinalizeUpgradeInternal();
        }
    }

    private static int? GetDeckIndex(CardModel card, IReadOnlyDictionary<CardModel, int> deckIndexLookup)
    {
        if (card.DeckVersion is null)
        {
            return null;
        }

        return deckIndexLookup.TryGetValue(card.DeckVersion, out var deckIndex) ? deckIndex : null;
    }

    private static int? GetDeckUpgradeLevelAtSave(CardModel card, IReadOnlyDictionary<CardModel, int> deckIndexLookup)
    {
        return GetDeckIndex(card, deckIndexLookup).HasValue ? card.DeckVersion?.CurrentUpgradeLevel : null;
    }
}
