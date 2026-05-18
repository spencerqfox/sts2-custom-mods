using System.Collections.Generic;
using BaseLib.Abstracts;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;

namespace HeavyEnchantment.Enchantments;

public sealed class Heavy : CustomEnchantmentModel
{
    private const string PerfectFitIconPath = "res://images/enchantments/perfect_fit.png";

    public override bool HasExtraCardText => true;

    public override bool ShouldStartAtBottomOfDrawPile => true;

    protected override string? CustomIconPath => PerfectFitIconPath;

    public override void ModifyShuffleOrder(Player player, List<CardModel> cards, bool isInitialShuffle)
    {
        if (isInitialShuffle || !cards.Contains(Card))
        {
            return;
        }

        cards.Remove(Card);
        cards.Add(Card);
    }
}
