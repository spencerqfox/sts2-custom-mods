using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;

namespace DoorRemaker;

[ModInitializer("Init")]
public static class ModEntry
{
    private static Harmony? _harmony;

    public static void Init()
    {
        Log.Info("[DoorRemaker] Initializing...");

        _harmony = new Harmony("com.spencerfox.doorremaker");
        _harmony.PatchAll();

        Log.Info("[DoorRemaker] Loaded successfully.");
    }
}
