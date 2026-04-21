using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BaseLib.Abstracts;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Afflictions;

namespace DoorRemaker.Powers;

public sealed class DevouredCounterPower : CustomPowerModel
{
    private const int DevouredAfflictionAmount = 1;
    private const string HungerPackedIconPath = "res://images/atlases/power_atlas.sprites/hunger_power.tres";
    private const string HungerBigIconPath = "res://images/powers/hunger_power.png";

    private sealed class Data
    {
        public int RemainingThisTurn;
        public bool ForcedCurrentPlayToExhaust;
    }

    public override PowerType Type => PowerType.Debuff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override int DisplayAmount => GetInternalData<Data>().RemainingThisTurn;

    public bool HasRemainingThisTurn => GetInternalData<Data>().RemainingThisTurn > 0;

    protected override IEnumerable<IHoverTip> ExtraHoverTips => new IHoverTip[]
    {
        HoverTipFactory.FromKeyword(CardKeyword.Exhaust)
    };

    public override string? CustomPackedIconPath => HungerPackedIconPath;

    public override string? CustomBigIconPath => HungerBigIconPath;

    protected override object InitInternalData()
    {
        return new Data();
    }

    public override Task AfterApplied(MegaCrit.Sts2.Core.Entities.Creatures.Creature? applier, CardModel? cardSource)
    {
        ResetRemainingThisTurn();
        return Task.CompletedTask;
    }

    public override async Task AfterSideTurnStart(CombatSide side, CombatState combatState)
    {
        if (side != Owner.Side)
        {
            return;
        }

        ResetRemainingThisTurn();
        await ReapplyDevouredForCurrentTurnAsync();
    }

    public override (PileType, CardPilePosition) ModifyCardPlayResultPileTypeAndPosition(
        CardModel card,
        bool isAutoPlay,
        ResourceInfo resources,
        PileType pileType,
        CardPilePosition position)
    {
        var data = GetInternalData<Data>();
        data.ForcedCurrentPlayToExhaust = false;

        if (!ShouldForceExhaust(card))
        {
            return (pileType, position);
        }

        if (data.RemainingThisTurn <= 0)
        {
            return (pileType, position);
        }

        if (pileType == PileType.Exhaust)
        {
            return (pileType, position);
        }

        data.ForcedCurrentPlayToExhaust = true;
        return (PileType.Exhaust, position);
    }

    public override Task AfterModifyingCardPlayResultPileOrPosition(CardModel card, PileType pileType, CardPilePosition position)
    {
        var data = GetInternalData<Data>();
        if (!data.ForcedCurrentPlayToExhaust)
        {
            return Task.CompletedTask;
        }

        data.ForcedCurrentPlayToExhaust = false;
        data.RemainingThisTurn--;
        Flash();
        InvokeDisplayAmountChanged();
        if (data.RemainingThisTurn <= 0)
        {
            ClearDevouredForCurrentTurn();
        }

        return Task.CompletedTask;
    }

    private void ResetRemainingThisTurn()
    {
        var data = GetInternalData<Data>();
        data.RemainingThisTurn = Amount;
        data.ForcedCurrentPlayToExhaust = false;
        InvokeDisplayAmountChanged();
    }

    private bool ShouldForceExhaust(CardModel card)
    {
        if (card.Owner?.Creature != Owner)
        {
            return false;
        }

        if (card.Type is not (CardType.Attack or CardType.Skill))
        {
            return false;
        }

        return card.Affliction is Devoured;
    }

    private async Task ReapplyDevouredForCurrentTurnAsync()
    {
        var playerCombatState = Owner.Player?.PlayerCombatState;
        if (playerCombatState is null || !HasRemainingThisTurn)
        {
            return;
        }

        foreach (var card in playerCombatState.AllCards.Where(IsEligibleForDevoured).ToList())
        {
            await CardCmd.Afflict<Devoured>(card, DevouredAfflictionAmount);
        }
    }

    private void ClearDevouredForCurrentTurn()
    {
        var playerCombatState = Owner.Player?.PlayerCombatState;
        if (playerCombatState is null)
        {
            return;
        }

        foreach (var card in playerCombatState.AllCards.Where(static card => card.Affliction is Devoured).ToList())
        {
            CardCmd.ClearAffliction(card);
        }
    }

    private static bool IsEligibleForDevoured(CardModel card)
    {
        if (card.Affliction is not null)
        {
            return false;
        }

        return card.Type is CardType.Attack or CardType.Skill;
    }
}
