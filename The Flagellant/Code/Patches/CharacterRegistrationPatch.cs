using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using TheFlagellant.Characters;

namespace TheFlagellant.Patches;

[HarmonyPatch(typeof(ModelDb), nameof(ModelDb.AllCharacters), MethodType.Getter)]
internal static class CharacterRegistrationPatch
{
    [HarmonyPostfix]
    private static void Postfix(ref IEnumerable<CharacterModel> __result)
    {
        if (!ModelDb.Contains(typeof(FlagellantCharacter)))
        {
            return;
        }

        if (__result.Any(static character => character is FlagellantCharacter))
        {
            return;
        }

        __result = __result.Concat(new CharacterModel[]
        {
            ModelDb.Character<FlagellantCharacter>()
        });
    }
}
