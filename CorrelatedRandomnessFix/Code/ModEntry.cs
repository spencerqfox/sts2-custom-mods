using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;

namespace CorrelatedRandomnessFix;

[ModInitializer("Init")]
public static class ModEntry
{
    private static Harmony? _harmony;

    public static void Init()
    {
        Log.Info("[CorrelatedRandomnessFix] Initializing...");

        _harmony = new Harmony("com.spencerfox.correlatedrandomnessfix");
        _harmony.PatchAll();

        Log.Info("[CorrelatedRandomnessFix] Loaded successfully.");
    }
}
