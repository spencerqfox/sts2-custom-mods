using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;

namespace NeowCustomMode;

[ModInitializer("Init")]
public static class ModEntry
{
    private static Harmony? _harmony;

    public static void Init()
    {
        Log.Info("[NeowCustomMode] Initializing...");

        _harmony = new Harmony("com.spencerfox.neowcustommode");
        _harmony.PatchAll();

        Log.Info("[NeowCustomMode] Loaded successfully.");
    }
}
