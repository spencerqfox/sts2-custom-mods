using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;
using TheFlagellant.Relics;

namespace TheFlagellant.Patches;

[HarmonyPatch(typeof(TouchOfOrobas), "GetUpgradedStarterRelic")]
internal static class TouchOfOrobasStarterRelicPatch
{
    [HarmonyPrefix]
    private static bool GetUpgradedStarterRelicPrefix(RelicModel starterRelic, ref RelicModel? __result)
    {
        if (starterRelic is not FlagellantStarterRelic)
        {
            return true;
        }

        __result = ModelDb.Relic<HallowedRosary>();
        return false;
    }
}
