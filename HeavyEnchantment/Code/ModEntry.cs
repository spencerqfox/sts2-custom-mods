using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;

namespace HeavyEnchantment;

[ModInitializer("Init")]
public static class ModEntry
{
    private static Harmony? _harmony;

    public static void Init()
    {
        Log.Info("[HeavyEnchantment] Initializing...");

        _harmony = new Harmony("com.spencerfox.heavyenchantment");
        _harmony.PatchAll();

        Log.Info("[HeavyEnchantment] Loaded successfully.");
    }
}
