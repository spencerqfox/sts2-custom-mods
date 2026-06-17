using HarmonyLib;
using Godot;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using TheFlagellant.Potions;

namespace TheFlagellant.Patches;

[HarmonyPatch]
internal static class FlagellantPotionAssetPatch
{
    private static readonly string FallbackImagePath = ImageHelper.GetImagePath("atlases/potion_atlas.sprites/fire_potion.tres");
    private static readonly string FallbackOutlinePath = ImageHelper.GetImagePath("atlases/potion_outline_atlas.sprites/fire_potion.tres");

    [HarmonyPatch(typeof(PotionModel), nameof(PotionModel.ImagePath), MethodType.Getter)]
    [HarmonyPrefix]
    private static bool ImagePathPrefix(PotionModel __instance, ref string __result)
    {
        if (!IsFlagellantPotion(__instance))
        {
            return true;
        }

        __result = FallbackImagePath;
        return false;
    }

    [HarmonyPatch(typeof(PotionModel), nameof(PotionModel.Image), MethodType.Getter)]
    [HarmonyPrefix]
    private static bool ImagePrefix(PotionModel __instance, ref Texture2D __result)
    {
        if (!IsFlagellantPotion(__instance))
        {
            return true;
        }

        __result = ResourceLoader.Load<Texture2D>(FallbackImagePath, null, ResourceLoader.CacheMode.Reuse);
        return false;
    }

    [HarmonyPatch(typeof(PotionModel), nameof(PotionModel.OutlinePath), MethodType.Getter)]
    [HarmonyPrefix]
    private static bool OutlinePathPrefix(PotionModel __instance, ref string? __result)
    {
        if (!IsFlagellantPotion(__instance))
        {
            return true;
        }

        __result = ResourceLoader.Exists(FallbackOutlinePath) ? FallbackOutlinePath : null;
        return false;
    }

    [HarmonyPatch(typeof(PotionModel), nameof(PotionModel.Outline), MethodType.Getter)]
    [HarmonyPrefix]
    private static bool OutlinePrefix(PotionModel __instance, ref Texture2D? __result)
    {
        if (!IsFlagellantPotion(__instance))
        {
            return true;
        }

        __result = ResourceLoader.Exists(FallbackOutlinePath)
            ? ResourceLoader.Load<Texture2D>(FallbackOutlinePath, null, ResourceLoader.CacheMode.Reuse)
            : null;
        return false;
    }

    private static bool IsFlagellantPotion(PotionModel potion)
    {
        return potion is HolyWater or VialOfGall or BottledSin;
    }
}
