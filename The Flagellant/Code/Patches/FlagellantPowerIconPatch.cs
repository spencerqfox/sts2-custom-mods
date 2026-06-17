using HarmonyLib;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using TheFlagellant.Powers;

namespace TheFlagellant.Patches;

[HarmonyPatch]
internal static class FlagellantPowerIconPatch
{
    // Powers that ship with custom art use a flat PNG for both the small in-combat icon and the
    // big tooltip icon (a PNG loads as a Texture2D just like the atlas .tres the game normally uses).
    // Every other Flagellant power still falls back to the Ironclad's Strength icon.
    private const string StrengthPackedFallback = "atlases/power_atlas.sprites/strength_power.tres";
    private const string StrengthBigFallback = "powers/strength_power.png";

    [HarmonyPatch(typeof(PowerModel), nameof(PowerModel.PackedIconPath), MethodType.Getter)]
    [HarmonyPrefix]
    private static bool PackedIconPathPrefix(PowerModel __instance, ref string __result)
    {
        if (!IsFlagellantPower(__instance))
        {
            return true;
        }

        __result = ImageHelper.GetImagePath(CustomIconInnerPath(__instance) ?? StrengthPackedFallback);
        return false;
    }

    [HarmonyPatch(typeof(PowerModel), nameof(PowerModel.ResolvedBigIconPath), MethodType.Getter)]
    [HarmonyPrefix]
    private static bool ResolvedBigIconPathPrefix(PowerModel __instance, ref string __result)
    {
        if (!IsFlagellantPower(__instance))
        {
            return true;
        }

        __result = ImageHelper.GetImagePath(CustomIconInnerPath(__instance) ?? StrengthBigFallback);
        return false;
    }

    private static string? CustomIconInnerPath(PowerModel power)
    {
        return power switch
        {
            PenancePower => "powers/penance.png",
            ResiliencePower => "powers/resilience.png",
            _ => null
        };
    }

    private static bool IsFlagellantPower(PowerModel power)
    {
        return power.GetType().Namespace == "TheFlagellant.Powers";
    }
}
