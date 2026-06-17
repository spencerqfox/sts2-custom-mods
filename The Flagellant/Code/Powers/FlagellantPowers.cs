using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Combat.History.Entries;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.Extensions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;
using TheFlagellant.Cards;

namespace TheFlagellant.Powers;

public sealed class ResiliencePower : PowerModel
{
    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override bool TryModifyPowerAmountReceived(
        PowerModel canonicalPower,
        Creature target,
        decimal amount,
        Creature? applier,
        out decimal modifiedAmount)
    {
        // Block self-inflicted debuffs (applier == Owner) and curse-inflicted debuffs
        // (curses apply with a null applier, e.g. Doubt/Shame). Enemy-applied debuffs
        // use the enemy creature as the applier, so they remain unblocked.
        if (target != Owner || (applier != Owner && applier != null) ||
            canonicalPower.GetTypeForAmount(amount) != PowerType.Debuff ||
            !canonicalPower.IsVisible)
        {
            modifiedAmount = amount;
            return false;
        }

        modifiedAmount = 0m;
        return true;
    }

    public override async Task AfterModifyingPowerAmountReceived(PowerModel power)
    {
        await PowerCmd.Decrement(this);
    }
}

public sealed class NextTurnResiliencePower : PowerModel
{
    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override async Task AfterEnergyReset(Player player)
    {
        if (player == Owner.Player)
        {
            await PowerCmd.Apply<ResiliencePower>(Owner, Amount, Owner, null);
            await PowerCmd.Remove(this);
        }
    }
}

public sealed class LoseEnergyNextTurnPower : PowerModel
{
    public override PowerType Type => PowerType.Debuff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override async Task AfterEnergyReset(Player player)
    {
        if (player == Owner.Player)
        {
            await PlayerCmd.LoseEnergy(Amount, player);
            await PowerCmd.Remove(this);
        }
    }
}

public sealed class FervorPower : PowerModel
{
    // Typed as a Debuff so that self-inflicted-debuff prevention (Resilience, Exaltation)
    // blocks this tracker at application time, leaving the Strength/Dexterity permanent and
    // never showing the "lose at end of turn" marker. The +Str/+Dex applied by the Fervor
    // card are separate Buff applications and are unaffected.
    public override PowerType Type => PowerType.Debuff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override async Task AfterTurnEnd(PlayerChoiceContext choiceContext, CombatSide side)
    {
        if (side == Owner.Side)
        {
            Flash();
            await PowerCmd.Remove(this);
            await PowerCmd.Apply<StrengthPower>(Owner, -Amount, Owner, null);
            await PowerCmd.Apply<DexterityPower>(Owner, -Amount, Owner, null);
        }
    }
}

public sealed class GritPower : PowerModel
{
    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override async Task AfterPowerAmountChanged(PowerModel power, decimal amount, Creature? applier, CardModel? cardSource)
    {
        if (power is PenancePower && power.Owner == Owner && amount > 0m)
        {
            await CreatureCmd.GainBlock(Owner, Amount, ValueProp.Unpowered, null);
        }
    }

    public override async Task AfterTurnEnd(PlayerChoiceContext choiceContext, CombatSide side)
    {
        if (side == Owner.Side)
        {
            await PowerCmd.Remove(this);
        }
    }
}

public sealed class DeliriumPower : PowerModel
{
    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override decimal ModifyDamageMultiplicative(Creature? target, decimal amount, ValueProp props, Creature? dealer, CardModel? cardSource)
    {
        if (dealer != Owner || cardSource?.Type != CardType.Attack || !props.IsPoweredAttack())
        {
            return 1m;
        }

        return Amount;
    }

    public override async Task AfterTurnEnd(PlayerChoiceContext choiceContext, CombatSide side)
    {
        if (side == Owner.Side)
        {
            await PowerCmd.Remove(this);
        }
    }
}

public sealed class CondescendPower : PowerModel
{
    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Single;

    public override decimal ModifyDamageMultiplicative(Creature? target, decimal amount, ValueProp props, Creature? dealer, CardModel? cardSource)
    {
        if (target != Owner || dealer == null || dealer.Side == Owner.Side ||
            dealer.GetPower<PenancePower>() == null || !props.IsPoweredAttack())
        {
            return 1m;
        }

        return 0.75m;
    }

    public override async Task AfterTurnEnd(PlayerChoiceContext choiceContext, CombatSide side)
    {
        if (side != Owner.Side)
        {
            await PowerCmd.Remove(this);
        }
    }
}

public sealed class ExaltationPower : PowerModel
{
    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Single;

    public override bool TryModifyPowerAmountReceived(
        PowerModel canonicalPower,
        Creature target,
        decimal amount,
        Creature? applier,
        out decimal modifiedAmount)
    {
        if (target == Owner && amount > 0m && canonicalPower.GetTypeForAmount(amount) == PowerType.Debuff)
        {
            modifiedAmount = 0m;
            return true;
        }

        modifiedAmount = amount;
        return false;
    }

    public override async Task AfterTurnEnd(PlayerChoiceContext choiceContext, CombatSide side)
    {
        if (side == Owner.Side)
        {
            await PowerCmd.Remove(this);
        }
    }
}

public sealed class RetributionPower : PowerModel
{
    private int _createdCurseCount;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DamageVar(10m, ValueProp.Unpowered)
    };

    public override async Task AfterCardGeneratedForCombat(CardModel card, bool addedByPlayer)
    {
        if (!addedByPlayer || card.Owner != Owner.Player || !FlagellantCardHelpers.IsCurseLike(card, Owner.Player))
        {
            return;
        }

        await AfterFlagellantCurseCreated(card);
    }

    public async Task AfterFlagellantCurseCreated(CardModel card)
    {
        _createdCurseCount++;
        int threshold = Math.Max(1, Amount);
        if (_createdCurseCount < threshold)
        {
            return;
        }

        while (_createdCurseCount >= threshold)
        {
            _createdCurseCount -= threshold;
            Flash();
            await FlagellantCardHelpers.DamageAllEnemies(Owner.Player, DynamicVars.Damage.BaseValue, ValueProp.Unpowered, null, this);
        }
    }
}

public sealed class WeakGripPower : PowerModel
{
    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Single;

    public override async Task AfterPlayerTurnStart(PlayerChoiceContext choiceContext, Player player)
    {
        if (player != Owner.Player)
        {
            return;
        }

        CardModel clumsy = Owner.CombatState.CreateCard<Clumsy>(player);
        CardCmd.PreviewCardPileAdd(await CardPileCmd.AddGeneratedCardToCombat(clumsy, PileType.Hand, addedByPlayer: true));
    }
}

public sealed class PremonitionPower : PowerModel
{
    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override async Task AfterCardDrawn(PlayerChoiceContext choiceContext, CardModel card, bool fromHandDraw)
    {
        if (card.Owner != Owner.Player || !FlagellantCardHelpers.IsCurseLike(card, Owner.Player))
        {
            return;
        }

        int cursesDrawnThisTurn = CombatManager.Instance.History.Entries.OfType<CardDrawnEntry>()
            .Count(e => e.HappenedThisTurn(Owner.CombatState) && e.Actor == Owner && FlagellantCardHelpers.IsCurseLike(e.Card, Owner.Player));
        if (cursesDrawnThisTurn == 1)
        {
            Flash();
            await CardPileCmd.Draw(choiceContext, Amount, Owner.Player);
        }
    }
}

public sealed class SharedSufferingPower : PowerModel
{
    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Single;

    public override async Task AfterPowerAmountChanged(PowerModel power, decimal amount, Creature? applier, CardModel? cardSource)
    {
        if (power.Owner != Owner || amount <= 0m || power.TypeForCurrentAmount != PowerType.Debuff ||
            cardSource == null || !FlagellantCardHelpers.IsCurseLike(cardSource, Owner.Player))
        {
            return;
        }

        foreach (Creature enemy in Owner.CombatState.HittableEnemies)
        {
            PowerModel copy = ModelDb.GetById<PowerModel>(power.Id).ToMutable();
            await PowerCmd.Apply(copy, enemy, amount, Owner, null);
        }
    }
}

public sealed class SympathyPower : PowerModel
{
    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override async Task AfterPowerAmountChanged(PowerModel power, decimal amount, Creature? applier, CardModel? cardSource)
    {
        if (power is PenancePower && power.Owner == Owner && applier == Owner && amount > 0m)
        {
            Flash();
            await FlagellantCardHelpers.DamageAllEnemies(Owner.Player, Amount * amount, ValueProp.Unpowered, null, this);
        }
    }
}

public sealed class DisciplinePower : PowerModel
{
    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override async Task AfterPlayerTurnStart(PlayerChoiceContext choiceContext, Player player)
    {
        if (player == Owner.Player)
        {
            await PowerCmd.Apply<PenancePower>(Owner, Amount, Owner, null);
        }
    }
}

public sealed class LucidityPower : PowerModel
{
    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override async Task AfterPowerAmountChanged(PowerModel power, decimal amount, Creature? applier, CardModel? cardSource)
    {
        if (power is PenancePower && applier == Owner && amount > 0m)
        {
            Flash();
            await CardPileCmd.Draw(new BlockingPlayerChoiceContext(), Amount, Owner.Player);
        }
    }
}

public sealed class DefiancePower : PowerModel
{
    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override async Task AfterPowerAmountChanged(PowerModel power, decimal amount, Creature? applier, CardModel? cardSource)
    {
        if (power is ResiliencePower && power.Owner == Owner && amount > 0m)
        {
            Flash();
            Creature? enemy = Owner.Player.RunState.Rng.CombatTargets.NextItem(Owner.CombatState.HittableEnemies);
            if (enemy != null)
            {
                await CreatureCmd.Damage(new BlockingPlayerChoiceContext(), enemy, Amount, ValueProp.Unpowered, Owner, null);
            }
        }
    }
}

public sealed class ConvictionPower : PowerModel
{
    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Single;

    public override async Task AfterPowerAmountChanged(PowerModel power, decimal amount, Creature? applier, CardModel? cardSource)
    {
        if (power is ResiliencePower && power.Owner == Owner && amount > 0m)
        {
            Flash();
            await PlayerCmd.GainEnergy(1m, Owner.Player);
        }
    }
}

public sealed class CompulsionPower : PowerModel
{
    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Single;

    public override async Task BeforeTurnEnd(PlayerChoiceContext choiceContext, CombatSide side)
    {
        if (side != Owner.Side)
        {
            return;
        }

        List<CardModel> skills = PileType.Hand.GetPile(Owner.Player).Cards
            .Where(c => c.Type == CardType.Skill && !c.Keywords.Contains(CardKeyword.Unplayable))
            .ToList();
        CardModel? skill = skills.Count == 0 ? null : Owner.Player.RunState.Rng.CombatCardSelection.NextItem(skills);
        if (skill == null)
        {
            return;
        }

        Flash();
        await CardCmd.AutoPlay(choiceContext, skill, null);
    }
}

public sealed class FortitudePower : PowerModel
{
    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override async Task AfterCardDrawn(PlayerChoiceContext choiceContext, CardModel card, bool fromHandDraw)
    {
        if (card.Owner != Owner.Player || !FlagellantCardHelpers.IsCurseLike(card, Owner.Player))
        {
            return;
        }

        int cursesDrawnThisTurn = CombatManager.Instance.History.Entries.OfType<CardDrawnEntry>()
            .Count(e => e.HappenedThisTurn(Owner.CombatState) && e.Actor == Owner && FlagellantCardHelpers.IsCurseLike(e.Card, Owner.Player));
        if (cursesDrawnThisTurn == 1)
        {
            Flash();
            await PowerCmd.Apply<ResiliencePower>(Owner, Amount, Owner, null);
        }
    }
}

public sealed class PerseverancePower : PowerModel
{
    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override async Task AfterPlayerTurnStart(PlayerChoiceContext choiceContext, Player player)
    {
        if (player == Owner.Player)
        {
            await PowerCmd.Apply<ResiliencePower>(Owner, Amount, Owner, null);
        }
    }
}

public sealed class ManifestationPower : PowerModel
{
    // Net Strength/Dexterity this power has applied to the Owner. While Manifestation is active the
    // player's Strength and Dexterity are FULLY OVERRIDDEN to equal Resilience: on every change we
    // push each stat's TOTAL to the Resilience target (swallowing external sources such as Vajra's
    // +Strength so Strength and Dexterity never drift apart from Resilience). We track exactly what
    // we contributed so AfterRemoved restores whatever external Strength/Dexterity the creature had.
    private int _strContributed;
    private int _dexContributed;

    // Reentrancy guard. SyncStatsToResilience applies Strength/Dexterity, which themselves fire
    // AfterPowerAmountChanged on every listener (including this one). We only react to Resilience, but
    // the guard keeps a single logical sync from re-entering itself and keeps the side-effect ordering
    // deterministic across multiplayer clients. The old code applied Strength/Dexterity non-silently
    // and reentrantly from inside this hook during a Resilience auto-consume, which is the multiplayer
    // desync/crash vector the base game avoids (see PossessStrengthPower/TemporaryStrengthPower).
    private bool _syncing;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    private int Resilience => Owner.GetPower<ResiliencePower>()?.Amount ?? 0;

    public override async Task AfterApplied(Creature? applier, CardModel? cardSource)
    {
        await SyncStatsToResilience();
    }

    public override async Task AfterPowerAmountChanged(PowerModel power, decimal amount, Creature? applier, CardModel? cardSource)
    {
        if (power is ResiliencePower && power.Owner == Owner)
        {
            await SyncStatsToResilience();
        }
    }

    public override async Task AfterRemoved(Creature oldOwner)
    {
        // Back out only what this power added, so the creature returns to its non-Manifestation
        // Strength/Dexterity. Applied silently with a null applier to match how the contributions
        // were applied.
        int str = _strContributed;
        int dex = _dexContributed;
        _strContributed = 0;
        _dexContributed = 0;
        if (str != 0)
        {
            await PowerCmd.Apply<StrengthPower>(oldOwner, -str, null, null, silent: true);
        }

        if (dex != 0)
        {
            await PowerCmd.Apply<DexterityPower>(oldOwner, -dex, null, null, silent: true);
        }
    }

    private async Task SyncStatsToResilience()
    {
        if (_syncing)
        {
            return;
        }

        // Full override: push each stat's TOTAL to equal Resilience, then record how much of that
        // total is ours so removal can be undone cleanly regardless of external sources.
        int target = Resilience;
        int strDelta = target - (Owner.GetPower<StrengthPower>()?.Amount ?? 0);
        int dexDelta = target - (Owner.GetPower<DexterityPower>()?.Amount ?? 0);
        if (strDelta == 0 && dexDelta == 0)
        {
            return;
        }

        _syncing = true;
        try
        {
            _strContributed += strDelta;
            _dexContributed += dexDelta;
            if (strDelta != 0)
            {
                await PowerCmd.Apply<StrengthPower>(Owner, strDelta, null, null, silent: true);
            }

            if (dexDelta != 0)
            {
                await PowerCmd.Apply<DexterityPower>(Owner, dexDelta, null, null, silent: true);
            }
        }
        finally
        {
            _syncing = false;
        }
    }
}

public sealed class ConsumptionPower : PowerModel
{
    // When applied from an upgraded card, the effect triggers at the START of the enemy turn
    // (shaving Penance damage off before the enemy acts) instead of at the end.
    private bool _atTurnStart;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Single;

    public override Task AfterApplied(Creature? applier, CardModel? cardSource)
    {
        if (cardSource?.IsUpgraded == true)
        {
            _atTurnStart = true;
        }

        return Task.CompletedTask;
    }

    public override async Task AfterSideTurnStart(CombatSide side, CombatState combatState)
    {
        if (_atTurnStart && side == CombatSide.Enemy)
        {
            await DamageEnemiesByPenance(new ThrowingPlayerChoiceContext());
        }
    }

    public override async Task AfterTurnEnd(PlayerChoiceContext choiceContext, CombatSide side)
    {
        if (!_atTurnStart && side == CombatSide.Enemy)
        {
            await DamageEnemiesByPenance(choiceContext);
        }
    }

    private async Task DamageEnemiesByPenance(PlayerChoiceContext choiceContext)
    {
        foreach (Creature enemy in Owner.CombatState.HittableEnemies)
        {
            PenancePower? penance = enemy.GetPower<PenancePower>();
            if (penance != null)
            {
                await CreatureCmd.Damage(choiceContext, enemy, penance.Amount, ValueProp.Unpowered, Owner, null);
            }
        }
    }
}

public sealed class TranscendentFormPower : PowerModel
{
    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override async Task AfterDamageGiven(PlayerChoiceContext choiceContext, Creature? dealer, DamageResult result, ValueProp props, Creature target, CardModel? cardSource)
    {
        if (dealer == Owner && cardSource?.Type == CardType.Attack && result.UnblockedDamage > 0)
        {
            await PowerCmd.Apply<PenancePower>(target, result.UnblockedDamage * Amount, Owner, cardSource);
        }
    }
}

public sealed class TenderizePower : PowerModel
{
    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override decimal ModifyDamageMultiplicative(Creature? target, decimal amount, ValueProp props, Creature? dealer, CardModel? cardSource)
    {
        if (dealer != Owner || target == null || target.GetPower<PenancePower>() == null || !props.IsPoweredAttack())
        {
            return 1m;
        }

        return 1m + (0.25m * Amount);
    }
}

public sealed class AnathemaPower : PowerModel
{
    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Single;
}

public sealed class MartyrPower : PowerModel
{
    private PowerModel? _redirectedPower;
    private decimal _redirectedAmount;
    private Creature? _redirectedApplier;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Single;

    public override bool TryModifyPowerAmountReceived(
        PowerModel canonicalPower,
        Creature target,
        decimal amount,
        Creature? applier,
        out decimal modifiedAmount)
    {
        // Only redirect debuffs an external attacker inflicts on an ally. Ignore an ally's
        // self-inflicted debuffs (applier == target, e.g. gaining their own Penance) and
        // curse-inflicted debuffs (null applier) -- those are the ally's own business and are
        // handled by their Resilience. Without this guard, a single self-penance application on
        // ally B is seen by BOTH B's Resilience and this Martyr in the same Hook pass, so B's
        // Resilience is consumed AND the Martyr owner still gains the Penance.
        if (target == Owner || target.Side != Owner.Side || !target.IsPlayer || amount <= 0m ||
            applier == target || applier == null ||
            canonicalPower.GetTypeForAmount(amount) != PowerType.Debuff)
        {
            modifiedAmount = amount;
            return false;
        }

        _redirectedPower = ModelDb.GetById<PowerModel>(canonicalPower.Id).ToMutable();
        _redirectedAmount = amount;
        _redirectedApplier = applier;
        modifiedAmount = 0m;
        return true;
    }

    public override async Task AfterModifyingPowerAmountReceived(PowerModel power)
    {
        if (_redirectedPower == null)
        {
            return;
        }

        PowerModel redirectedPower = _redirectedPower;
        decimal redirectedAmount = _redirectedAmount;
        Creature? redirectedApplier = _redirectedApplier;
        _redirectedPower = null;
        _redirectedAmount = 0m;
        _redirectedApplier = null;
        await PowerCmd.Apply(redirectedPower, Owner, redirectedAmount, redirectedApplier, null);
    }
}
