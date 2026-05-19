using System;
using FrozenHand.Relics;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;

namespace FrozenHand.Patches;

[HarmonyPatch(typeof(ModelDb), nameof(ModelDb.GetEntry))]
internal static class FrozenHandModelIdPatch
{
    [HarmonyPostfix]
    private static void Postfix(Type type, ref string __result)
    {
        if (type == typeof(FrozenHandRelic))
        {
            __result = "FROZENHAND-" + __result;
        }
    }
}
