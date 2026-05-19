using System;
using HarmonyLib;
using HeavyEnchantment.Enchantments;
using MegaCrit.Sts2.Core.Models;

namespace HeavyEnchantment.Patches;

[HarmonyPatch(typeof(ModelDb), nameof(ModelDb.GetEntry))]
internal static class HeavyEnchantmentModelIdPatch
{
    [HarmonyPostfix]
    private static void Postfix(Type type, ref string __result)
    {
        if (type == typeof(Heavy))
        {
            __result = "HEAVYENCHANTMENT-" + __result;
        }
    }
}
