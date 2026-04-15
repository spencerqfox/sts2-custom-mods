using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;

namespace FogOfWar;

[ModInitializer("Init")]
public static class ModEntry
{
    private static Harmony? _harmony;

    public static void Init()
    {
        Log.Info("[FogOfWar] Initializing...");

        _harmony = new Harmony("com.spencerfox.fogofwar");
        _harmony.PatchAll();

        Log.Info("[FogOfWar] Loaded successfully.");
    }
}
