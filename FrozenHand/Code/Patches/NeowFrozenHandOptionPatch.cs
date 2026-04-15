using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using FrozenHand.Relics;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Events;

namespace FrozenHand.Patches;

[HarmonyPatch(typeof(Neow), "GenerateInitialOptions")]
public static class NeowFrozenHandOptionPatch
{
    private const string FrozenHandTextKey = "NEOW.pages.INITIAL.options.FROZEN_HAND";

    private static readonly MethodInfo? AncientDoneMethod =
        AccessTools.Method(typeof(AncientEventModel), "Done");

    [HarmonyPostfix]
    private static void Postfix(Neow __instance, ref IReadOnlyList<EventOption> __result)
    {
        var owner = __instance.Owner;
        if (owner is null || owner.RunState.Modifiers.Count > 0 || owner.GetRelic<FrozenHandRelic>() is not null)
        {
            return;
        }

        if (__result.Any(static option => option.Relic is FrozenHandRelic))
        {
            return;
        }

        var relic = (FrozenHandRelic)ModelDb.Relic<FrozenHandRelic>().ToMutable();
        relic.Owner = owner;

        var options = __result.ToList();
        options.Add(EventOption.FromRelic(relic, __instance, () => OnChosen(__instance, relic), FrozenHandTextKey));
        __result = options;
    }

    private static async Task OnChosen(AncientEventModel ancient, FrozenHandRelic relic)
    {
        await RelicCmd.Obtain(relic, relic.Owner);
        AncientDoneMethod?.Invoke(ancient, null);
    }
}
