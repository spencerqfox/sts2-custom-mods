using System;
using System.Linq;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;
using MegaCrit.Sts2.Core.Multiplayer.Messages.Lobby;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Runs;
using JoinInProgress.Networking;
using JoinInProgress.Resync;

namespace JoinInProgress.Join;

internal static class LateJoinRunLoader
{
    public static async Task Load(
        NetClientGameService netService,
        ClientRejoinResponseMessage response,
        LateJoinReadyMessage ready)
    {
        SerializableRun save = response.serializableRun;
        SerializablePlayer localPlayer = save.Players.First(player => player.NetId == netService.NetId);
        LoadRunListener listener = new();
        ClientLoadJoinResponseMessage loadMessage = new()
        {
            serializableRun = save,
            playersAlreadyConnected = ready.ConnectedPlayerIds
        };
        LoadRunLobby lobby = new(netService, listener, loadMessage);

        try
        {
            ModelId characterId = localPlayer.CharacterId ??
                throw new InvalidOperationException("The joining player has no character ID.");
            CharacterModel character = ModelDb.GetById<CharacterModel>(characterId);
            NGame game = NGame.Instance ?? throw new InvalidOperationException("The game root is unavailable.");
            SfxCmd.Play(character.CharacterTransitionSfx);
            await game.Transition.FadeOut(
                0.8f,
                character.CharacterSelectTransitionPath);

            RunState state = RunState.FromSerializable(save);
            await RunManager.Instance.SetUpSavedMultiplayer(state, lobby);
            game.ReactionContainer.InitializeNetworking(netService);
            await PreloadManager.LoadRunAssets(state.Players.Select(player => player.Character));
            await PreloadManager.LoadActAssets(state.Act);
            RunManager.Instance.Launch();
            game.RootSceneContainer.SetCurrentScene(NRun.Create(state));
            await RunManager.Instance.GenerateMap();
            ApplySynchronizationCounters(response, ready);
            ReplaceConnectedPlayerIds(ready);
            await RunManager.Instance.LoadIntoLatestMapCoord(new MapRoom());
            ApplyTransientPlayerState(response.combatState, state);
            if (RunManager.Instance.MapDrawingsToLoad != null)
            {
                (NRun.Instance ?? throw new InvalidOperationException("The run scene was not created."))
                    .GlobalUi.MapScreen.Drawings.LoadDrawings(RunManager.Instance.MapDrawingsToLoad);
                RunManager.Instance.MapDrawingsToLoad = null;
            }
            lobby.CleanUp(disconnectSession: false);
            await game.Transition.FadeIn();

            if (ready.IsLateJoin)
            {
                LateJoinNetwork.SetMapTravelEnabled(enabled: false);
                CatchUpScreen.Show(state, state.GetPlayer(netService.NetId)!);
            }

            Log.Info(ready.IsLateJoin
                ? "[JoinInProgress] Loaded late joiner at the party map checkpoint."
                : "[JoinInProgress] Restored an existing player into the running session.");
        }
        catch
        {
            lobby.CleanUp(disconnectSession: true, NetError.InternalError);
            throw;
        }
    }

    private static void ReplaceConnectedPlayerIds(LateJoinReadyMessage ready)
    {
        RunLobby lobby = RunManager.Instance.RunLobby ??
            throw new InvalidOperationException("The running lobby was not initialized.");
        var connected = (System.Collections.Generic.HashSet<ulong>)AccessTools
            .Field(typeof(RunLobby), "_connectedPlayerIds")
            .GetValue(lobby)!;
        connected.Clear();
        foreach (ulong playerId in ready.ConnectedPlayerIds)
        {
            connected.Add(playerId);
        }
    }

    private static void ApplySynchronizationCounters(
        ClientRejoinResponseMessage response,
        LateJoinReadyMessage ready)
    {
        RunManager manager = RunManager.Instance;
        manager.ActionQueueSet.FastForwardNextActionId(ready.NextActionId);
        manager.ActionQueueSynchronizer.FastForwardHookId(ready.NextHookId);
        manager.ChecksumTracker.LoadReplayChecksums(null!, ready.NextChecksumId);
        AccessTools.Property(typeof(MapSelectionSynchronizer), nameof(MapSelectionSynchronizer.MapGenerationCount))
            .SetValue(manager.MapSelectionSynchronizer, ready.MapGenerationCount);

        if (response.combatState == null)
        {
            return;
        }

        manager.PlayerChoiceSynchronizer.FastForwardChoiceIds(response.combatState.nextChoiceIds);
        manager.RewardsSetSynchronizer.FastForwardRewardIds(response.combatState.nextRewardIds);
    }

    private static void ApplyTransientPlayerState(NetFullCombatState? combatState, RunState state)
    {
        if (combatState == null)
        {
            return;
        }

        foreach (NetFullCombatState.PlayerState snapshot in combatState.Players)
        {
            Player? player = state.GetPlayer(snapshot.playerId);
            if (player == null || snapshot.turnNumber <= 0)
            {
                continue;
            }

            if (player.PlayerCombatState == null)
            {
                player.ResetCombatState();
                player.PlayerCombatState!.AfterCombatEnd();
            }

            PlayerCombatState playerState = player.PlayerCombatState!;
            AccessTools.Field(typeof(PlayerCombatState), "<TurnNumber>k__BackingField")
                .SetValue(playerState, snapshot.turnNumber);
            AccessTools.Field(typeof(PlayerCombatState), "_phase").SetValue(playerState, snapshot.phase);
            AccessTools.Field(typeof(PlayerCombatState), "_energy").SetValue(playerState, snapshot.energy);
            AccessTools.Field(typeof(PlayerCombatState), "_stars").SetValue(playerState, snapshot.stars);
        }
    }

    private sealed class LoadRunListener : ILoadRunLobbyListener
    {
        public void PlayerConnected(ulong playerId)
        {
        }

        public void RemotePlayerDisconnected(ulong playerId)
        {
        }

        public Task<bool> ShouldAllowRunToBegin()
        {
            return Task.FromResult(true);
        }

        public void BeginRun()
        {
        }

        public void PlayerReadyChanged(ulong playerId)
        {
        }

        public void LocalPlayerDisconnected(NetErrorInfo info)
        {
        }
    }
}
