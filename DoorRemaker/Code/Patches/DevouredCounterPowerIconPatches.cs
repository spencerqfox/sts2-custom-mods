using DoorRemaker.Powers;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;

namespace DoorRemaker.Patches;

[HarmonyPatch]
internal static class DevouredCounterPowerIconPatches
{
    [HarmonyPatch(typeof(PowerModel), nameof(PowerModel.PackedIconPath), MethodType.Getter)]
    [HarmonyPrefix]
    private static bool PackedIconPathPrefix(PowerModel __instance, ref string __result)
    {
        if (__instance is not DevouredCounterPower)
        {
            return true;
        }

        __result = DevouredCounterPower.HungerPackedIconPath;
        return false;
    }

    [HarmonyPatch(typeof(PowerModel), "get_BigIconPath")]
    [HarmonyPrefix]
    private static bool BigIconPathPrefix(PowerModel __instance, ref string __result)
    {
        if (__instance is not DevouredCounterPower)
        {
            return true;
        }

        __result = DevouredCounterPower.HungerBigIconPath;
        return false;
    }
}
