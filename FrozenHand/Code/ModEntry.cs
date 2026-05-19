using FrozenHand.Relics;
using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Models.RelicPools;

namespace FrozenHand;

[ModInitializer("Init")]
public static class ModEntry
{
    private static Harmony? _harmony;

    public static void Init()
    {
        Log.Info("[FrozenHand] Initializing...");

        ModHelper.AddModelToPool<SharedRelicPool, FrozenHandRelic>();

        _harmony = new Harmony("com.spencerfox.frozenhand");
        _harmony.PatchAll();

        Log.Info("[FrozenHand] Loaded successfully.");
    }
}
