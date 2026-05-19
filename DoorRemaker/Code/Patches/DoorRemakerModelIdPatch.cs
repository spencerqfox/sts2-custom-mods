using System;
using DoorRemaker.Powers;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;

namespace DoorRemaker.Patches;

[HarmonyPatch(typeof(ModelDb), nameof(ModelDb.GetEntry))]
internal static class DoorRemakerModelIdPatch
{
    [HarmonyPostfix]
    private static void Postfix(Type type, ref string __result)
    {
        if (type == typeof(DevouredCounterPower))
        {
            __result = "DOORREMAKER-" + __result;
        }
    }
}
