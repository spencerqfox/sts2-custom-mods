using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;

namespace JoinInProgress;

[ModInitializer("Init")]
public static class ModEntry
{
    private static Harmony? _harmony;

    public static void Init()
    {
        Log.Info("[JoinInProgress] Initializing...");

        _harmony = new Harmony("com.spencerfox.joininprogress");
        _harmony.PatchAll();

        Log.Info("[JoinInProgress] Loaded successfully.");
    }
}
