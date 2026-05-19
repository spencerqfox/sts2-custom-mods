using System.Collections.Generic;
using System.Linq;
using FriendTrading.RestSite;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.RestSite;
using MegaCrit.Sts2.Core.Multiplayer.Game;

namespace FriendTrading.Patches;

[HarmonyPatch(typeof(RestSiteOption), nameof(RestSiteOption.Generate))]
internal static class RestSiteOptionGeneratePatch
{
    [HarmonyPostfix]
    private static void Postfix(Player player, List<RestSiteOption> __result)
    {
        if (player.RunState.Players.Count < 2 || !player.Creature.IsAlive)
        {
            return;
        }

        if (player.Deck.Cards.Any(FriendCardTradeRestSiteOption.CanTradeCard))
        {
            __result.Add(new FriendCardTradeRestSiteOption(player));
        }

        if (player.Relics.Any(FriendRelicTradeRestSiteOption.CanTradeRelic))
        {
            __result.Add(new FriendRelicTradeRestSiteOption(player));
        }
    }
}

[HarmonyPatch(typeof(RestSiteSynchronizer), nameof(RestSiteSynchronizer.BeginRestSite))]
internal static class RestSiteSynchronizerBeginRestSitePatch
{
    private static readonly HashSet<RestSiteSynchronizer> SubscribedSynchronizers = new();

    [HarmonyPrefix]
    private static void Prefix()
    {
        FriendTradeCoordinator.Reset();
    }

    [HarmonyPostfix]
    private static void Postfix(RestSiteSynchronizer __instance)
    {
        if (SubscribedSynchronizers.Add(__instance))
        {
            __instance.AfterPlayerOptionChosen += FriendTradeCoordinator.OnRestSiteOptionChosen;
        }
    }
}
