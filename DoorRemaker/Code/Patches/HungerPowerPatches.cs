using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DoorRemaker.Powers;
using DoorRemaker.State;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Afflictions;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.Models.Powers;

namespace DoorRemaker.Patches;

[HarmonyPatch(typeof(HungerPower))]
public static class HungerPowerPatches
{
    [HarmonyPatch(nameof(HungerPower.AfterApplied))]
    [HarmonyPrefix]
    private static bool AfterAppliedPrefix(
        HungerPower __instance,
        Creature? applier,
        CardModel? cardSource,
        ref Task __result)
    {
        if (!IsDoormakerHunger(__instance))
        {
            return true;
        }

        __result = ApplyDoorRemakerHungerAsync(__instance, applier, cardSource);
        return false;
    }

    [HarmonyPatch(nameof(HungerPower.AfterCardEnteredCombat))]
    [HarmonyPrefix]
    private static bool AfterCardEnteredCombatPrefix(HungerPower __instance, CardModel card, ref Task __result)
    {
        if (!IsDoormakerHunger(__instance))
        {
            return true;
        }

        __result = ApplyDevouredIfEligibleAsync(__instance, card);
        return false;
    }

    [HarmonyPatch(nameof(HungerPower.AfterRemoved))]
    [HarmonyPrefix]
    private static bool AfterRemovedPrefix(HungerPower __instance, Creature oldOwner, ref Task __result)
    {
        if (!IsDoormakerHunger(__instance))
        {
            return true;
        }

        __result = RemoveDoorRemakerHungerAsync(oldOwner);
        return false;
    }

    private static async Task ApplyDoorRemakerHungerAsync(
        HungerPower hungerPower,
        Creature? applier,
        CardModel? cardSource)
    {
        var combatState = hungerPower.CombatState;
        var state = HungerCombatStateStore.ForCombat(combatState);
        state.HungerApplications++;

        var quota = (state.HungerApplications * 2) - 1;
        foreach (var playerCreature in GetAffectedPlayerCreatures(hungerPower.Owner))
        {
            var player = playerCreature.Player;
            if (player?.PlayerCombatState is null)
            {
                continue;
            }

            foreach (var card in player.PlayerCombatState.AllCards.ToList())
            {
                await ApplyDevouredIfEligibleAsync(hungerPower, card);
            }

            await PowerCmd.Apply<DevouredCounterPower>(
                playerCreature,
                quota,
                applier ?? hungerPower.Owner,
                cardSource);
        }
    }

    private static async Task RemoveDoorRemakerHungerAsync(Creature oldOwner)
    {
        if (oldOwner.CombatState is null)
        {
            return;
        }

        foreach (var playerCreature in oldOwner.CombatState.Allies.Where(static creature => creature.IsPlayer).ToList())
        {
            var player = playerCreature.Player;
            if (player?.PlayerCombatState is null)
            {
                continue;
            }

            var afflictedCards = player.PlayerCombatState.AllCards
                .Where(static card => card.Affliction is Devoured)
                .ToList();

            foreach (var card in afflictedCards)
            {
                CardCmd.ClearAffliction(card);
            }

            await PowerCmd.Remove<DevouredCounterPower>(playerCreature);
        }
    }

    private static async Task ApplyDevouredIfEligibleAsync(HungerPower hungerPower, CardModel card)
    {
        if (!IsEligibleForDoorRemakerDevoured(card))
        {
            return;
        }

        var counterPower = card.Owner?.Creature?.GetPower<DevouredCounterPower>();
        if (counterPower is not null && !counterPower.HasRemainingThisTurn)
        {
            return;
        }

        await CardCmd.Afflict<Devoured>(card, hungerPower.Amount);
    }

    private static IEnumerable<Creature> GetAffectedPlayerCreatures(Creature owner)
    {
        return owner.CombatState?.Allies.Where(static creature => creature.IsPlayer)
            ?? Enumerable.Empty<Creature>();
    }

    private static bool IsDoormakerHunger(HungerPower hungerPower)
    {
        return hungerPower.Owner.Monster is Doormaker;
    }

    private static bool IsEligibleForDoorRemakerDevoured(CardModel card)
    {
        if (card.Owner?.PlayerCombatState is null)
        {
            return false;
        }

        if (card.Affliction is not null)
        {
            return false;
        }

        return card.Type is CardType.Attack or CardType.Skill;
    }
}
