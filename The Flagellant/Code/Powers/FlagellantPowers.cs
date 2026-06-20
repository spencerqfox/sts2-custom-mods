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
        if (target == Owner && ManifestationPower.IsInternalStatAdjustment(canonicalPower, target, amount))
        {
            modifiedAmount = amount;
            return false;
        }

        // Block self-inflicted debuffs (applier == Owner) and curse-inflicted debuffs
        // (curses apply with a null applier, e.g. Doubt/Shame). Knowledge Demon choice
        // cards also use the owner as applier, but are boss debuffs and should land.
        if (target != Owner || IsKnowledgeDemonChoicePower(canonicalPower) || (applier != Owner && applier != null) ||
            canonicalPower.GetTypeForAmount(amount) != PowerType.Debuff ||
            !canonicalPower.IsVisible)
        {
            modifiedAmount = amount;
            return false;
        }

        modifiedAmount = 0m;
        return true;
    }

    private static bool IsKnowledgeDemonChoicePower(PowerModel power)
    {
        return power is DisintegrationPower or MindRotPower or SlothPower or WasteAwayPower;
    }

    public override async Task AfterModifyingPowerAmountReceived(PowerModel power)
    {
        await PowerCmd.Decrement(this);
    }

    public override async Task AfterPowerAmountChanged(PlayerChoiceContext choiceContext, PowerModel power, decimal amount, Creature? applier, CardModel? cardSource)
    {
        if (power == this && amount < 0m)
        {
            await PowerCmd.Apply<LostResilienceThisTurnPower>(choiceContext, Owner, -amount, null, null, silent: true);
        }
    }
}

public sealed class LostResilienceThisTurnPower : PowerModel
{
    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    protected override bool IsVisibleInternal => false;

    public override async Task AfterSideTurnEnd(PlayerChoiceContext choiceContext, CombatSide side, IEnumerable<Creature> participants)
    {
        await PowerCmd.Remove(this);
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
            await PowerCmd.Apply<ResiliencePower>(new ThrowingPlayerChoiceContext(), Owner, Amount, Owner, null);
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

    public override async Task AfterSideTurnEnd(PlayerChoiceContext choiceContext, CombatSide side, IEnumerable<Creature> participants)
    {
        if (side == Owner.Side)
        {
            Flash();
            await PowerCmd.Remove(this);
            await PowerCmd.Apply<StrengthPower>(choiceContext, Owner, -Amount, Owner, null);
            await PowerCmd.Apply<DexterityPower>(choiceContext, Owner, -Amount, Owner, null);
        }
    }
}

public sealed class GritPower : PowerModel
{
    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override async Task AfterPowerAmountChanged(PlayerChoiceContext choiceContext, PowerModel power, decimal amount, Creature? applier, CardModel? cardSource)
    {
        if (power is PenancePower && power.Owner == Owner && amount > 0m)
        {
            await CreatureCmd.GainBlock(Owner, Amount * amount, ValueProp.Unpowered, null);
        }
    }

    public override async Task AfterSideTurnEnd(PlayerChoiceContext choiceContext, CombatSide side, IEnumerable<Creature> participants)
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

    public override async Task AfterSideTurnEnd(PlayerChoiceContext choiceContext, CombatSide side, IEnumerable<Creature> participants)
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

    public override async Task AfterSideTurnEnd(PlayerChoiceContext choiceContext, CombatSide side, IEnumerable<Creature> participants)
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

    public override async Task AfterSideTurnEnd(PlayerChoiceContext choiceContext, CombatSide side, IEnumerable<Creature> participants)
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

    public override async Task AfterCardGeneratedForCombat(CardModel card, Player creator)
    {
        if (creator != Owner.Player || card.Owner != Owner.Player || !FlagellantCardHelpers.IsCurseLike(card, Owner.Player))
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
        CardCmd.PreviewCardPileAdd(await CardPileCmd.AddGeneratedCardToCombat(clumsy, PileType.Hand, Owner.Player));
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

    public override async Task AfterPowerAmountChanged(PlayerChoiceContext choiceContext, PowerModel power, decimal amount, Creature? applier, CardModel? cardSource)
    {
        if (power.Owner != Owner || amount <= 0m || power.TypeForCurrentAmount != PowerType.Debuff ||
            cardSource == null || !FlagellantCardHelpers.IsCurseLike(cardSource, Owner.Player))
        {
            return;
        }

        foreach (Creature enemy in Owner.CombatState.HittableEnemies)
        {
            PowerModel copy = ModelDb.GetById<PowerModel>(power.Id).ToMutable();
            await PowerCmd.Apply(choiceContext, copy, enemy, amount, Owner, null);
        }
    }
}

public sealed class SympathyPower : PowerModel
{
    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override async Task AfterPowerAmountChanged(PlayerChoiceContext choiceContext, PowerModel power, decimal amount, Creature? applier, CardModel? cardSource)
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
            await PowerCmd.Apply<PenancePower>(choiceContext, Owner, Amount, Owner, null);
        }
    }
}

public sealed class LucidityPower : PowerModel
{
    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override async Task AfterPowerAmountChanged(PlayerChoiceContext choiceContext, PowerModel power, decimal amount, Creature? applier, CardModel? cardSource)
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

    public override async Task AfterPowerAmountChanged(PlayerChoiceContext choiceContext, PowerModel power, decimal amount, Creature? applier, CardModel? cardSource)
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

    public override PowerStackType StackType => PowerStackType.Counter;

    public override async Task AfterPowerAmountChanged(PlayerChoiceContext choiceContext, PowerModel power, decimal amount, Creature? applier, CardModel? cardSource)
    {
        if (power is ResiliencePower && power.Owner == Owner && amount > 0m)
        {
            Flash();
            await PlayerCmd.GainEnergy(Amount, Owner.Player);
        }
    }
}

public sealed class CompulsionPower : PowerModel
{
    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override async Task BeforeSideTurnEnd(PlayerChoiceContext choiceContext, CombatSide side, IEnumerable<Creature> participants)
    {
        if (side != Owner.Side)
        {
            return;
        }

        for (int i = 0; i < Amount; i++)
        {
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
            await PowerCmd.Apply<ResiliencePower>(choiceContext, Owner, Amount, Owner, null);
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
            await PowerCmd.Apply<ResiliencePower>(choiceContext, Owner, Amount, Owner, null);
        }
    }
}

public sealed class ManifestationPower : PowerModel
{
    private static readonly Dictionary<Creature, int> InternalStatAdjustmentDepths = new();

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

    internal static bool IsInternalStatAdjustment(PowerModel canonicalPower, Creature target, decimal amount)
    {
        if (amount >= 0m || (canonicalPower is not StrengthPower && canonicalPower is not DexterityPower))
        {
            return false;
        }

        return InternalStatAdjustmentDepths.ContainsKey(target);
    }

    public override async Task AfterApplied(Creature? applier, CardModel? cardSource)
    {
        await SyncStatsToResilience(new ThrowingPlayerChoiceContext());
    }

    public override async Task AfterPowerAmountChanged(PlayerChoiceContext choiceContext, PowerModel power, decimal amount, Creature? applier, CardModel? cardSource)
    {
        if (power is ResiliencePower && power.Owner == Owner)
        {
            await SyncStatsToResilience(choiceContext);
        }
    }

    public override async Task AfterRemoved(Creature oldOwner)
    {
        // Back out only what this power added, so the creature returns to its non-Manifestation
        // Strength/Dexterity. These internal stat changes bypass Resilience's self-debuff shield.
        int str = _strContributed;
        int dex = _dexContributed;
        _strContributed = 0;
        _dexContributed = 0;
        if (str != 0)
        {
            await ApplyInternalStatAdjustment<StrengthPower>(new ThrowingPlayerChoiceContext(), oldOwner, -str);
        }

        if (dex != 0)
        {
            await ApplyInternalStatAdjustment<DexterityPower>(new ThrowingPlayerChoiceContext(), oldOwner, -dex);
        }
    }

    private async Task SyncStatsToResilience(PlayerChoiceContext choiceContext)
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
                await ApplyInternalStatAdjustment<StrengthPower>(choiceContext, Owner, strDelta);
            }

            if (dexDelta != 0)
            {
                await ApplyInternalStatAdjustment<DexterityPower>(choiceContext, Owner, dexDelta);
            }
        }
        finally
        {
            _syncing = false;
        }
    }

    private static async Task ApplyInternalStatAdjustment<TPower>(PlayerChoiceContext choiceContext, Creature target, decimal amount)
        where TPower : PowerModel, new()
    {
        BeginInternalStatAdjustment(target);
        try
        {
            await PowerCmd.Apply<TPower>(choiceContext, target, amount, null, null, silent: true);
        }
        finally
        {
            EndInternalStatAdjustment(target);
        }
    }

    private static void BeginInternalStatAdjustment(Creature target)
    {
        InternalStatAdjustmentDepths.TryGetValue(target, out int depth);
        InternalStatAdjustmentDepths[target] = depth + 1;
    }

    private static void EndInternalStatAdjustment(Creature target)
    {
        int depth = InternalStatAdjustmentDepths[target] - 1;
        if (depth <= 0)
        {
            InternalStatAdjustmentDepths.Remove(target);
            return;
        }

        InternalStatAdjustmentDepths[target] = depth;
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

    public override async Task AfterSideTurnStart(CombatSide side, IReadOnlyList<Creature> participants, ICombatState combatState)
    {
        if (_atTurnStart && side == CombatSide.Enemy)
        {
            await DamageEnemiesByPenance(new ThrowingPlayerChoiceContext());
        }
    }

    public override async Task AfterSideTurnEnd(PlayerChoiceContext choiceContext, CombatSide side, IEnumerable<Creature> participants)
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

    public override decimal ModifyPowerAmountGivenAdditive(
        PowerModel power,
        Creature giver,
        decimal amount,
        Creature? target,
        CardModel? cardSource)
    {
        if (giver != Owner || amount <= 0m)
        {
            return 0m;
        }

        PowerType powerType = power.GetTypeForAmount(amount);
        return powerType is PowerType.Buff or PowerType.Debuff ? Amount : 0m;
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
        await PowerCmd.Apply(new ThrowingPlayerChoiceContext(), redirectedPower, Owner, redirectedAmount, redirectedApplier, null);
    }
}
