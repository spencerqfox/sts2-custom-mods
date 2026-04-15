using FrozenHand.Relics;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Players;

namespace FrozenHand.Patches;

[HarmonyPatch(typeof(Player), nameof(Player.PopulateCombatState))]
public static class PlayerPopulateCombatStatePatch
{
    [HarmonyPrefix]
    private static bool Prefix(Player __instance, CombatState state)
    {
        if (__instance.GetRelic<FrozenHandRelic>() is { } frozenHandRelic &&
            frozenHandRelic.TryPopulateCombatStateFromStoredPiles(__instance, state))
        {
            return false;
        }

        return true;
    }
}
