using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;

namespace FriendTrading;

[ModInitializer("Init")]
public static class ModEntry
{
    private static Harmony? _harmony;

    public static void Init()
    {
        Log.Info("[FriendTrading] Initializing...");

        _harmony = new Harmony("com.spencerfox.friendtrading");
        _harmony.PatchAll();

        Log.Info("[FriendTrading] Loaded successfully.");
    }
}
