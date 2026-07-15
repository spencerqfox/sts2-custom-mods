using System.Collections.Generic;
using System.Linq;
using FriendTrading.RestSite;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.RestSite;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Nodes.Rooms;

namespace FriendTrading.Patches;

[HarmonyPatch(typeof(RestSiteOption), nameof(RestSiteOption.Generate))]
internal static class RestSiteOptionGeneratePatch
{
    [HarmonyPostfix]
    private static void Postfix(Player player, List<RestSiteOption> __result)
    {
        if (player.RunState.Players.Count < 2)
        {
            return;
        }

        if (player.Creature.IsAlive && player.Deck.Cards.Any(FriendCardTradeRestSiteOption.CanTradeCard))
        {
            __result.Add(new FriendCardTradeRestSiteOption(player));
        }

        if (player.Creature.IsAlive && player.Relics.Any(FriendRelicTradeRestSiteOption.CanTradeRelic))
        {
            __result.Add(new FriendRelicTradeRestSiteOption(player));
        }

        FriendTradeCoordinator.SyncAvailabilityFromOptions(player, __result);
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
        FriendTradeCoordinator.SubscribeToChoiceResults();

        if (SubscribedSynchronizers.Add(__instance))
        {
            __instance.BeforePlayerOptionChosen += FriendTradeCoordinator.OnRestSiteOptionStarted;
            __instance.AfterPlayerOptionChosen += FriendTradeCoordinator.OnRestSiteOptionFinished;
        }
    }
}

[HarmonyPatch(typeof(NRestSiteRoom), "OnProceedButtonReleased")]
internal static class RestSiteRoomProceedPatch
{
    [HarmonyPrefix]
    private static void Prefix()
    {
        FriendTradeCoordinator.CancelLocalPendingOffers();
    }
}

[HarmonyPatch(typeof(NRestSiteRoom), nameof(NRestSiteRoom._ExitTree))]
internal static class RestSiteRoomExitPatch
{
    [HarmonyPrefix]
    private static void Prefix()
    {
        FriendTradeCoordinator.CancelAllPendingOffers();
    }
}
