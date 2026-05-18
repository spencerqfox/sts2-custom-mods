using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using NeowCustomMode.Modifiers;

namespace NeowCustomMode.Patches;

[HarmonyPatch(typeof(ModelDb), "get_GoodModifiers")]
public static class GoodModifiersPatch
{
    public static void Postfix(ref IReadOnlyList<ModifierModel> __result)
    {
        if (__result.Any(static modifier => modifier is NeowBonus))
        {
            return;
        }

        List<ModifierModel> modifiers = __result.ToList();
        modifiers.Add(ModelDb.Modifier<NeowBonus>());
        __result = modifiers;
    }
}
