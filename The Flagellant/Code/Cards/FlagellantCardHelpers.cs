using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Combat.History.Entries;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.Extensions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.CardPools;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;
using TheFlagellant.Powers;
using TheFlagellant.Relics;

namespace TheFlagellant.Cards;

internal static class FlagellantCardHelpers
{
    private readonly record struct PermanentCurseCreation(Player Owner, CombatState CombatState, int RoundNumber, CombatSide CurrentSide);

    private static readonly List<PermanentCurseCreation> PermanentCurseCreations = new();

    public static bool IsCurseLike(CardModel card, Player owner)
    {
        if (card.Type == CardType.Curse)
        {
            return true;
        }

        if (card.Type == CardType.Status && owner.GetRelic<ScarletLetter>() != null)
        {
            return true;
        }

        return card.Type == CardType.Attack && owner.Creature.GetPower<AnathemaPower>() != null;
    }

    public static int CountCurseLike(IEnumerable<CardModel> cards, Player owner)
    {
        return cards.Count(card => IsCurseLike(card, owner));
    }

    public static IEnumerable<CardModel> CombatCards(Player owner)
    {
        if (owner.PlayerCombatState == null)
        {
            return Enumerable.Empty<CardModel>();
        }

        return owner.PlayerCombatState.AllPiles.SelectMany(pile => pile.Cards).Distinct();
    }

    public static int CountCurseLikeAnywhere(Player owner)
    {
        IEnumerable<CardModel> cards = owner.PlayerCombatState == null
            ? PileType.Deck.GetPile(owner).Cards
            : CombatCards(owner);
        return CountCurseLike(cards, owner);
    }

    public static int CountCurseLikeInHand(Player owner)
    {
        return CountCurseLike(PileType.Hand.GetPile(owner).Cards, owner);
    }

    public static IReadOnlyList<Creature> Enemies(CardModel card)
    {
        return card.CombatState?.HittableEnemies.ToList() ?? new List<Creature>();
    }

    public static Creature? FirstEnemy(CardModel card)
    {
        return Enemies(card).FirstOrDefault();
    }

    public static async Task DamageAllEnemies(Player owner, decimal amount, ValueProp props, CardModel? cardSource, AbstractModel? source)
    {
        CombatState? combatState = owner.Creature.CombatState;
        if (combatState == null)
        {
            return;
        }

        await CreatureCmd.Damage(new BlockingPlayerChoiceContext(), combatState.HittableEnemies, amount, props, owner.Creature, cardSource);
    }

    public static async Task DealAttack(CardModel card, PlayerChoiceContext choiceContext, Creature target, decimal amount, string hitFx = "vfx/vfx_attack_slash")
    {
        await DamageCmd.Attack(amount)
            .FromCard(card)
            .Targeting(target)
            .WithHitFx(hitFx)
            .Execute(choiceContext);
    }

    public static async Task DealAttackAll(CardModel card, PlayerChoiceContext choiceContext, decimal amount, string hitFx = "vfx/vfx_attack_slash")
    {
        await DamageCmd.Attack(amount)
            .FromCard(card)
            .TargetingAllOpponents(card.CombatState)
            .WithHitFx(hitFx)
            .Execute(choiceContext);
    }

    public static async Task ApplyPenance(CardModel source, Creature target, decimal amount)
    {
        await PowerCmd.Apply<PenancePower>(target, amount, source.Owner.Creature, source);
    }

    public static async Task GainPenance(CardModel source, decimal amount)
    {
        await PowerCmd.Apply<PenancePower>(source.Owner.Creature, amount, source.Owner.Creature, source);
    }

    public static async Task GainResilience(CardModel source, decimal amount)
    {
        await PowerCmd.Apply<ResiliencePower>(source.Owner.Creature, amount, source.Owner.Creature, source);
    }

    public static int PenanceOn(Creature creature)
    {
        return creature.GetPower<PenancePower>()?.Amount ?? 0;
    }

    public static int ResilienceOn(Creature creature)
    {
        return creature.GetPower<ResiliencePower>()?.Amount ?? 0;
    }

    public static async Task<CardModel?> AddCurseToCombat(CardModel source, PileType pileType)
    {
        CardModel? curse = CreateCurse(source);
        if (curse == null)
        {
            return null;
        }

        CardCmd.PreviewCardPileAdd(await CardPileCmd.AddGeneratedCardToCombat(curse, pileType, addedByPlayer: true));
        return curse;
    }

    public static async Task<CardModel?> AddCurseToDeck(Player owner)
    {
        CardModel? curse = CreateDeckCurse(owner);
        if (curse == null)
        {
            return null;
        }

        CardPileAddResult result = await CardPileCmd.Add(curse, PileType.Deck);
        CardCmd.PreviewCardPileAdd(result);
        if (result.success)
        {
            await RecordPermanentCurseCreated(owner, curse);
        }

        return curse;
    }

    public static bool HasCurseCreatedThisTurn(Player owner)
    {
        CombatState? combatState = owner.Creature.CombatState;
        if (combatState == null)
        {
            return false;
        }

        PermanentCurseCreations.RemoveAll(record => record.CombatState != combatState);
        return CombatManager.Instance.History.Entries.OfType<CardGeneratedEntry>()
            .Any(entry => entry.HappenedThisTurn(combatState) &&
                entry.GeneratedByPlayer &&
                entry.Card.Owner == owner &&
                IsCurseLike(entry.Card, owner)) ||
            PermanentCurseCreations.Any(record =>
                record.Owner == owner &&
                record.CombatState == combatState &&
                record.RoundNumber == combatState.RoundNumber &&
                record.CurrentSide == combatState.CurrentSide);
    }

    private static async Task RecordPermanentCurseCreated(Player owner, CardModel curse)
    {
        if (!IsCurseLike(curse, owner))
        {
            return;
        }

        CombatState? combatState = owner.Creature.CombatState;
        if (combatState == null)
        {
            return;
        }

        PermanentCurseCreations.Add(new PermanentCurseCreation(owner, combatState, combatState.RoundNumber, combatState.CurrentSide));
        foreach (RetributionPower retribution in owner.Creature.Powers.OfType<RetributionPower>().ToList())
        {
            await retribution.AfterFlagellantCurseCreated(curse);
        }
    }

    private static CardModel? CreateCurse(CardModel source)
    {
        CardModel? canonical = FirstCurse(source.Owner);
        return canonical == null ? null : source.CombatState?.CreateCard(canonical, source.Owner);
    }

    private static CardModel? CreateDeckCurse(Player owner)
    {
        CardModel? canonical = FirstCurse(owner);
        return canonical == null ? null : owner.RunState.CreateCard(canonical, owner);
    }

    private static CardModel? FirstCurse(Player owner)
    {
        List<CardModel> curses = ModelDb.CardPool<CurseCardPool>()
            .GetUnlockedCards(owner.UnlockState, owner.RunState.CardMultiplayerConstraint)
            .Where(c => c.CanBeGeneratedByModifiers)
            .ToList();
        return owner.RunState.Rng.CombatCardGeneration.NextItem(curses);
    }

    public static async Task<IEnumerable<CardModel>> SelectCursesFromHand(CardModel source, PlayerChoiceContext context, int min, int max, LocString prompt)
    {
        return await CardSelectCmd.FromHand(
            context,
            source.Owner,
            new CardSelectorPrefs(prompt, min, max) { Cancelable = min == 0 },
            card => IsCurseLike(card, source.Owner),
            source);
    }

    public static async Task<int> ExhaustSelectedCurses(CardModel source, PlayerChoiceContext context, int min, int max)
    {
        List<CardModel> selected = (await SelectCursesFromHand(source, context, min, max, CardSelectorPrefs.ExhaustSelectionPrompt)).ToList();
        foreach (CardModel card in selected)
        {
            await CardCmd.Exhaust(context, card);
        }

        return selected.Count;
    }

    public static async Task<int> DiscardSelectedCurses(CardModel source, PlayerChoiceContext context, int min, int max)
    {
        List<CardModel> selected = (await SelectCursesFromHand(source, context, min, max, CardSelectorPrefs.DiscardSelectionPrompt)).ToList();
        await CardCmd.Discard(context, selected);
        return selected.Count;
    }

    public static int CountUniqueDebuffs(Creature creature)
    {
        return creature.Powers.Count(power => power.TypeForCurrentAmount == PowerType.Debuff && power.Amount > 0);
    }

    public static int CountDebuffsAppliedThisTurn(CombatState? combatState)
    {
        if (combatState == null)
        {
            return 0;
        }

        return CombatManager.Instance.History.Entries.OfType<PowerReceivedEntry>()
            .Count(entry => entry.HappenedThisTurn(combatState) &&
                entry.Amount > 0m &&
                entry.Power.GetTypeForAmount(entry.Amount) == PowerType.Debuff);
    }

    public static int CountResilienceLostThisTurn(Creature creature)
    {
        CombatState? combatState = creature.CombatState;
        if (combatState == null)
        {
            return 0;
        }

        return (int)Math.Abs(CombatManager.Instance.History.Entries.OfType<PowerReceivedEntry>()
            .Where(entry => entry.HappenedThisTurn(combatState) &&
                entry.Actor == creature &&
                entry.Power is ResiliencePower &&
                entry.Amount < 0m)
            .Sum(entry => entry.Amount));
    }

    public static bool WasAppliedByPlayer(PowerModel power, Player player)
    {
        return CombatManager.Instance.History.Entries.OfType<PowerReceivedEntry>()
            .Any(entry => entry.Actor == power.Owner &&
                entry.Power.GetType() == power.GetType() &&
                entry.Applier == player.Creature &&
                entry.Amount > 0m);
    }

    public static async Task ApplyRandomDebuff(CardModel source, Creature target, decimal amount)
    {
        int index = source.Owner.RunState.Rng.CombatCardSelection.NextInt(4);
        switch (index)
        {
            case 0:
                await PowerCmd.Apply<WeakPower>(target, amount, source.Owner.Creature, source);
                break;
            case 1:
                await PowerCmd.Apply<FrailPower>(target, amount, source.Owner.Creature, source);
                break;
            case 2:
                await PowerCmd.Apply<VulnerablePower>(target, amount, source.Owner.Creature, source);
                break;
            default:
                await PowerCmd.Apply<PenancePower>(target, amount, source.Owner.Creature, source);
                break;
        }
    }
}
