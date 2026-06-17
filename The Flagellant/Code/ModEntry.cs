using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;

namespace TheFlagellant;

[ModInitializer("Init")]
public static class ModEntry
{
    private static Harmony? _harmony;

    public static void Init()
    {
        Log.Info("[TheFlagellant] Initializing...");

        _harmony = new Harmony("com.spencerfox.theflagellant");
        _harmony.PatchAll();

        Log.Info("[TheFlagellant] Loaded successfully.");
    }
}
