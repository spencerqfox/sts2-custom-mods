using HarmonyLib;
using HeavyEnchantment.Enchantments;
using MegaCrit.Sts2.Core.Models;

namespace HeavyEnchantment.Patches;

[HarmonyPatch(typeof(EnchantmentModel), nameof(EnchantmentModel.IconPath), MethodType.Getter)]
internal static class HeavyIconPatch
{
    [HarmonyPrefix]
    private static bool Prefix(EnchantmentModel __instance, ref string __result)
    {
        if (__instance is not Heavy)
        {
            return true;
        }

        __result = Heavy.PerfectFitIconPath;
        return false;
    }
}
