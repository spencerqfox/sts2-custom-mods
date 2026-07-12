using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;
using MegaCrit.Sts2.Core.Multiplayer.Connection;
using MegaCrit.Sts2.Core.Multiplayer.Messages.Lobby;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Unlocks;
using JoinInProgress.Join;
using JoinInProgress.Networking;

namespace JoinInProgress.Patches;

[HarmonyPatch]
internal static class KeepSteamLobbyOpenPatch
{
    private static MethodBase TargetMethod()
    {
        Type steamHost = AccessTools.TypeByName("MegaCrit.Sts2.Core.Multiplayer.Transport.Steam.SteamHost");
        return AccessTools.Method(steamHost, "SetHostIsClosed");
    }

    private static bool Prefix(bool isClosed)
    {
        return !isClosed;
    }
}

[HarmonyPatch(typeof(RunLobby), "OnConnectedToClientAsHost")]
internal static class AllowLateConnectionPatch
{
    private static bool Prefix(RunLobby __instance, ulong playerId)
    {
        var players = (IPlayerCollection)AccessTools.Field(typeof(RunLobby), "_playerCollection").GetValue(__instance)!;
        if (players.GetPlayer(playerId) != null || !LateJoinNetwork.IsSafeCheckpoint())
        {
            return true;
        }

        var netService = (INetGameService)AccessTools.Field(typeof(RunLobby), "_netService").GetValue(__instance)!;
        InitialGameInfoMessage message = InitialGameInfoMessage.Basic();
        message.sessionState = RunSessionState.Running;
        message.gameMode = __instance.GameMode;
        netService.SendMessage(message, playerId);
        return false;
    }
}

[HarmonyPatch(typeof(RunManager), "InitializeRunLobby")]
internal static class RunLobbyInitializedPatch
{
    private static void Postfix(RunManager __instance)
    {
        LateJoinNetwork.Attach(__instance.NetService);
    }
}

[HarmonyPatch(typeof(RunManager), nameof(RunManager.CleanUp))]
internal static class RunCleanupPatch
{
    private static void Prefix()
    {
        LateJoinNetwork.Detach();
        LateJoinHandshake.Reset();
    }
}

[HarmonyPatch(typeof(JoinFlow), "AttemptRejoin")]
internal static class JoinFlowAttemptRejoinPatch
{
    private static bool Prefix(
        JoinFlow __instance,
        NetClientGameService gameService,
        ref Task<ClientRejoinResponseMessage> __result)
    {
        __result = AttemptLateJoin(__instance, gameService);
        return false;
    }

    private static async Task<ClientRejoinResponseMessage> AttemptLateJoin(
        JoinFlow flow,
        NetClientGameService gameService)
    {
        using CancellationTokenSource phaseCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(flow.CancelToken.Token);
        NetErrorInfo? disconnectedInfo = null;
        bool succeeded = false;
        void HandleDisconnected(NetErrorInfo info)
        {
            disconnectedInfo = info;
            phaseCancellation.Cancel();
        }

        TaskCompletionSource<ClientRejoinResponseMessage> rejoinCompletion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        AccessTools.Field(typeof(JoinFlow), "_rejoinCompletion").SetValue(flow, rejoinCompletion);
        gameService.Disconnected += HandleDisconnected;
        try
        {
            UnlockState unlockState = SaveManager.Instance.GenerateUnlockStateFromProgress();
            IReadOnlyList<CharacterModel> characters = unlockState.Characters.ToList();
            CharacterModel? character = await LateJoinCharacterPicker.Pick(
                characters,
                phaseCancellation.Token);
            if (character == null)
            {
                phaseCancellation.Token.ThrowIfCancellationRequested();
                throw new OperationCanceledException("Late join canceled during character selection.");
            }

            TaskCompletionSource<LateJoinReadyMessage> readyCompletion =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
            void HandleReady(LateJoinReadyMessage message, ulong senderId)
            {
                if (senderId == gameService.HostNetId)
                {
                    readyCompletion.TrySetResult(message);
                }
            }

            gameService.RegisterMessageHandler<LateJoinReadyMessage>(HandleReady);
            LateJoinReadyMessage ready;
            try
            {
                gameService.SendMessage(new LateJoinProfileMessage
                {
                    CharacterEntry = character.Id.Entry,
                    UnlockState = unlockState.ToSerializable()
                });
                ready = await readyCompletion.Task.WaitAsync(
                    TimeSpan.FromSeconds(30),
                    phaseCancellation.Token);
            }
            finally
            {
                gameService.UnregisterMessageHandler<LateJoinReadyMessage>(HandleReady);
            }

            if (!ready.Accepted)
            {
                throw new InvalidOperationException(
                    string.IsNullOrWhiteSpace(ready.RejectionReason)
                        ? "The host rejected the late join."
                        : ready.RejectionReason);
            }

            LateJoinHandshake.Ready = ready;
            gameService.SendMessage(default(ClientRejoinRequestMessage));
            ClientRejoinResponseMessage response = await rejoinCompletion.Task.WaitAsync(
                TimeSpan.FromSeconds(30),
                phaseCancellation.Token);
            succeeded = true;
            return response;
        }
        catch (OperationCanceledException) when (disconnectedInfo.HasValue)
        {
            throw new ClientConnectionFailedException(
                $"Disconnected from the host during late join: {disconnectedInfo.Value.GetReason()}",
                disconnectedInfo.Value);
        }
        catch (TimeoutException)
        {
            throw new ClientConnectionFailedException(
                "Late-join handshake timed out.",
                new NetErrorInfo(NetError.HandshakeTimeout, selfInitiated: false));
        }
        finally
        {
            gameService.Disconnected -= HandleDisconnected;
            if (!succeeded)
            {
                LateJoinHandshake.Reset();
            }
        }
    }
}

[HarmonyPatch(typeof(JoinFlow), nameof(JoinFlow.Begin))]
internal static class JoinFlowBeginPatch
{
    private static void Postfix(JoinFlow __instance, ref Task<JoinResult> __result)
    {
        __result = LoadRunningSession(__instance, __result);
    }

    private static async Task<JoinResult> LoadRunningSession(JoinFlow flow, Task<JoinResult> original)
    {
        JoinResult result = await original;
        if (result.sessionState != RunSessionState.Running || result.rejoinResponse == null)
        {
            return result;
        }

        LateJoinReadyMessage ready = LateJoinHandshake.Ready ??
            throw new InvalidOperationException("The late-join handshake completed without synchronization metadata.");
        NetClientGameService netService = flow.NetService ??
            throw new InvalidOperationException("The late-join network service is unavailable.");

        await LateJoinRunLoader.Load(netService, result.rejoinResponse.Value, ready);
        result.sessionState = null;
        LateJoinHandshake.Reset();
        return result;
    }
}

internal static class LateJoinHandshake
{
    public static LateJoinReadyMessage? Ready { get; set; }

    public static void Reset()
    {
        Ready = null;
    }
}
