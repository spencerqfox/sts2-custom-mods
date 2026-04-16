using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Random;

namespace FrozenHand.Patches;

[HarmonyPatch]
public static class CardCmdTransformPatch
{
    private static MethodBase TargetMethod() =>
        AccessTools.Method(typeof(CardCmd), nameof(CardCmd.Transform),
            new[] { typeof(IEnumerable<CardTransformation>), typeof(Rng), typeof(CardPreviewStyle) });

    [HarmonyTranspiler]
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var addInternal = AccessTools.Method(typeof(CardPile), nameof(CardPile.AddInternal),
            new[] { typeof(CardModel), typeof(int), typeof(bool) });
        var patchedInstructions = new List<CodeInstruction>(instructions);

        CodeInstruction? indexLoad = null;
        for (var i = 2; i < patchedInstructions.Count; i++)
        {
            if (!patchedInstructions[i].Calls(addInternal))
            {
                continue;
            }

            var arg = patchedInstructions[i - 2];
            if (arg.IsLdloc())
            {
                indexLoad = arg.Clone();
                break;
            }
        }

        if (indexLoad is null)
        {
            Log.Warn("[FrozenHand] CardCmd.Transform transpiler: else-branch index load not found; leaving IL unchanged");
            return patchedInstructions;
        }

        for (var i = 2; i < patchedInstructions.Count; i++)
        {
            if (!patchedInstructions[i].Calls(addInternal))
            {
                continue;
            }

            var arg = patchedInstructions[i - 2];
            if (arg.opcode == OpCodes.Ldc_I4_M1)
            {
                patchedInstructions[i - 2] = indexLoad;
                break;
            }
        }

        return patchedInstructions;
    }
}
