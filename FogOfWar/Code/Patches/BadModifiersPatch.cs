using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using FogOfWarModifier = global::FogOfWar.Modifiers.FogOfWar;

namespace FogOfWar.Patches;

[HarmonyPatch(typeof(ModelDb), nameof(ModelDb.BadModifiers), MethodType.Getter)]
public static class BadModifiersPatch
{
    public static void Postfix(ref IReadOnlyList<ModifierModel> __result)
    {
        if (__result.Any(static modifier => modifier is FogOfWarModifier))
        {
            return;
        }

        List<ModifierModel> modifiers = __result.ToList();
        modifiers.Add(ModelDb.Modifier<FogOfWarModifier>());
        __result = modifiers;
    }
}
