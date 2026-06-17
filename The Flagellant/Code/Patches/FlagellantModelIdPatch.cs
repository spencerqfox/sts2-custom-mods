using System;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using TheFlagellant.Characters;

namespace TheFlagellant.Patches;

[HarmonyPatch(typeof(ModelDb), nameof(ModelDb.GetEntry))]
internal static class FlagellantModelIdPatch
{
    private const string Prefix = "THEFLAGELLANT-";

    [HarmonyPostfix]
    private static void Postfix(Type type, ref string __result)
    {
        if (type == typeof(FlagellantCharacter))
        {
            __result = "THE_FLAGELLANT";
            return;
        }

        if (typeof(AbstractModel).IsAssignableFrom(type) &&
            type.Namespace?.StartsWith("TheFlagellant.", StringComparison.Ordinal) == true)
        {
            __result = Prefix + __result;
        }
    }
}
