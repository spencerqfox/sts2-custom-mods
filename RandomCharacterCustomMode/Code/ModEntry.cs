using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;

namespace RandomCharacterCustomMode;

[ModInitializer("Init")]
public static class ModEntry
{
    private static Harmony? _harmony;

    public static void Init()
    {
        Log.Info("[RandomCharacterCustomMode] Initializing...");

        _harmony = new Harmony("com.spencerfox.randomcharactercustommode");
        _harmony.PatchAll();

        Log.Info("[RandomCharacterCustomMode] Loaded successfully.");
    }
}
