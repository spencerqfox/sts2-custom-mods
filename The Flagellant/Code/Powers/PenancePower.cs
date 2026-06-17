using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;

namespace TheFlagellant.Powers;

public sealed class PenancePower : PowerModel
{
    private const string DamageMultiplierKey = "DamageMultiplier";
    private const string BlockMultiplierKey = "BlockMultiplier";

    public override PowerType Type => PowerType.Debuff;

    public override PowerStackType StackType => PowerStackType.Counter;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DynamicVar(DamageMultiplierKey, 0.75m),
        new DynamicVar(BlockMultiplierKey, 0.75m)
    };

    public override decimal ModifyDamageMultiplicative(
        MegaCrit.Sts2.Core.Entities.Creatures.Creature? target,
        decimal amount,
        ValueProp props,
        MegaCrit.Sts2.Core.Entities.Creatures.Creature? dealer,
        CardModel? cardSource)
    {
        if (dealer != Owner || !props.IsPoweredAttack())
        {
            return 1m;
        }

        return Owner.GetPower<DeliriumPower>() == null ? DynamicVars[DamageMultiplierKey].BaseValue : 0.5m;
    }

    public override decimal ModifyBlockMultiplicative(
        MegaCrit.Sts2.Core.Entities.Creatures.Creature target,
        decimal block,
        ValueProp props,
        CardModel? cardSource,
        MegaCrit.Sts2.Core.Entities.Cards.CardPlay? cardPlay)
    {
        if (target != Owner || !props.IsPoweredCardOrMonsterMoveBlock())
        {
            return 1m;
        }

        return Owner.GetPower<DeliriumPower>() == null ? DynamicVars[BlockMultiplierKey].BaseValue : 0.5m;
    }

    public override async Task AfterTurnEnd(PlayerChoiceContext choiceContext, CombatSide side)
    {
        if (side == Owner.Side)
        {
            Flash();
            await PowerCmd.Decrement(this);
        }
    }
}
