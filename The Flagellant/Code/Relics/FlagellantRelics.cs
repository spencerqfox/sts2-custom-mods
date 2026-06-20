using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves.Runs;
using MegaCrit.Sts2.Core.ValueProps;
using TheFlagellant.Cards;
using TheFlagellant.Powers;

namespace TheFlagellant.Relics;

public sealed class HallowedRosary : RelicModel
{
    public override RelicRarity Rarity => RelicRarity.Ancient;

    protected override string IconBaseName => "burning_blood";

    public override bool IsAllowed(IRunState runState) => false;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new PowerVar<ResiliencePower>(3m)
    };

    public override async Task BeforeCombatStart()
    {
        Flash();
        await PowerCmd.Apply<ResiliencePower>(new ThrowingPlayerChoiceContext(), Owner.Creature, DynamicVars["ResiliencePower"].BaseValue, Owner.Creature, null);
    }
}

public sealed class DuVuDoll : RelicModel
{
    public override RelicRarity Rarity => RelicRarity.Rare;

    protected override string IconBaseName => "darkstone_periapt";

    public override async Task BeforeCombatStart()
    {
        int curses = FlagellantCardHelpers.CountCurseLike(PileType.Deck.GetPile(Owner).Cards, Owner);
        if (curses > 0)
        {
            Flash();
            await PowerCmd.Apply<StrengthPower>(new ThrowingPlayerChoiceContext(), Owner.Creature, curses, Owner.Creature, null);
        }
    }
}

public sealed class DarkstonePeriapt : RelicModel
{
    public override RelicRarity Rarity => RelicRarity.Uncommon;

    protected override string IconBaseName => "darkstone_periapt";

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new MaxHpVar(6m)
    };

    public override async Task AfterCardChangedPiles(CardModel card, PileType oldPileType, AbstractModel? source)
    {
        if (card.Owner == Owner && card.Pile?.Type == PileType.Deck && FlagellantCardHelpers.IsCurseLike(card, Owner))
        {
            Flash();
            await CreatureCmd.GainMaxHp(Owner.Creature, DynamicVars.MaxHp.BaseValue);
        }
    }
}

public sealed class ScarletLetter : RelicModel
{
    public override RelicRarity Rarity => RelicRarity.Shop;

    protected override string IconBaseName => "darkstone_periapt";
}

public sealed class QuiltedVestment : RelicModel
{
    private int _resilienceGainedThisCombat;

    public override RelicRarity Rarity => RelicRarity.Uncommon;

    public override bool ShowCounter => true;

    public override int DisplayAmount => _resilienceGainedThisCombat;

    protected override string IconBaseName => "anchor";

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DynamicVar("ResilienceThreshold", 10m),
        new BlockVar(10m, ValueProp.Unpowered)
    };

    [SavedProperty]
    public int ResilienceTowardBlock
    {
        get
        {
            return _resilienceGainedThisCombat;
        }
        set
        {
            AssertMutable();
            _resilienceGainedThisCombat = value;
            UpdateDisplay();
        }
    }

    private void UpdateDisplay()
    {
        int threshold = DynamicVars["ResilienceThreshold"].IntValue;
        Status = ((_resilienceGainedThisCombat == threshold - 1) ? RelicStatus.Active : RelicStatus.Normal);
        InvokeDisplayAmountChanged();
    }

    public override Task BeforeCombatStart()
    {
        ResilienceTowardBlock = 0;
        return Task.CompletedTask;
    }

    public override async Task AfterPowerAmountChanged(PlayerChoiceContext choiceContext, PowerModel power, decimal amount, Creature? applier, CardModel? cardSource)
    {
        if (power is not ResiliencePower || power.Owner != Owner.Creature || amount <= 0m)
        {
            return;
        }

        int total = _resilienceGainedThisCombat + (int)amount;
        int threshold = DynamicVars["ResilienceThreshold"].IntValue;
        while (total >= threshold)
        {
            total -= threshold;
            Flash();
            await CreatureCmd.GainBlock(Owner.Creature, DynamicVars.Block.BaseValue, ValueProp.Unpowered, null);
        }

        ResilienceTowardBlock = total;
    }
}

public sealed class InquisitorsSeal : RelicModel
{
    public override RelicRarity Rarity => RelicRarity.Common;

    protected override string IconBaseName => "paper_phrog";

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new PowerVar<PenancePower>(1m)
    };

    public override async Task BeforeCombatStart()
    {
        Flash();
        await PowerCmd.Apply<PenancePower>(new ThrowingPlayerChoiceContext(), Owner.Creature.CombatState.HittableEnemies, DynamicVars["PenancePower"].BaseValue, Owner.Creature, null);
    }
}

public sealed class BloodiedBandages : RelicModel
{
    public override RelicRarity Rarity => RelicRarity.Uncommon;

    protected override string IconBaseName => "anchor";

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new BlockVar(3m, ValueProp.Unpowered)
    };

    public override async Task AfterPowerAmountChanged(PlayerChoiceContext choiceContext, PowerModel power, decimal amount, Creature? applier, CardModel? cardSource)
    {
        if (power is PenancePower && applier == Owner.Creature && amount > 0m)
        {
            Flash();
            await CreatureCmd.GainBlock(Owner.Creature, DynamicVars.Block.BaseValue, ValueProp.Unpowered, null);
        }
    }
}

public sealed class Stigmata : RelicModel
{
    private bool _triggeredThisCombat;

    public override RelicRarity Rarity => RelicRarity.Uncommon;

    protected override string IconBaseName => "burning_blood";

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new PowerVar<ResiliencePower>(2m)
    };

    public override Task BeforeCombatStart()
    {
        _triggeredThisCombat = false;
        return Task.CompletedTask;
    }

    public override async Task AfterDamageReceived(PlayerChoiceContext choiceContext, Creature target, DamageResult result, ValueProp props, Creature? dealer, CardModel? cardSource)
    {
        if (_triggeredThisCombat || target != Owner.Creature || result.UnblockedDamage <= 0)
        {
            return;
        }

        _triggeredThisCombat = true;
        Flash();
        await PowerCmd.Apply<ResiliencePower>(choiceContext, Owner.Creature, DynamicVars["ResiliencePower"].BaseValue, Owner.Creature, null);
    }
}
