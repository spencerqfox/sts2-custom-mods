using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Saves.Managers;
using TheFlagellant.Characters;

namespace TheFlagellant.Patches;

[HarmonyPatch]
internal static class FlagellantProgressPatches
{
    [HarmonyPatch(typeof(ProgressSaveManager), "ObtainCharUnlockEpoch")]
    [HarmonyPrefix]
    private static bool ObtainCharUnlockEpochPrefix(Player localPlayer)
    {
        return !IsFlagellant(localPlayer);
    }

    [HarmonyPatch(typeof(ProgressSaveManager), "CheckFifteenBossesDefeatedEpoch")]
    [HarmonyPrefix]
    private static bool CheckFifteenBossesDefeatedEpochPrefix(Player localPlayer)
    {
        return !IsFlagellant(localPlayer);
    }

    [HarmonyPatch(typeof(ProgressSaveManager), "CheckFifteenElitesDefeatedEpoch")]
    [HarmonyPrefix]
    private static bool CheckFifteenElitesDefeatedEpochPrefix(Player localPlayer)
    {
        return !IsFlagellant(localPlayer);
    }

    private static bool IsFlagellant(Player player)
    {
        return player.Character is FlagellantCharacter;
    }
}
