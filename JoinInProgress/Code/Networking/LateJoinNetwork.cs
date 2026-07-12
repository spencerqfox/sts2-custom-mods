using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves.Runs;
using MegaCrit.Sts2.Core.Unlocks;
using JoinInProgress.Infrastructure;
using JoinInProgress.Resync;

namespace JoinInProgress.Networking;

internal static class LateJoinNetwork
{
    private sealed class PendingJoin
    {
        public required SerializableRunRngSet RunRng { get; init; }
        public required SerializableRelicGrabBag SharedRelicGrabBag { get; init; }
        public SerializablePlayer? BaselinePlayer { get; set; }
    }

    private static readonly object SyncRoot = new();
    private static readonly Dictionary<ulong, PendingJoin> PendingPlayers = new();
    private static readonly HashSet<ulong> CompletingPlayers = new();
    private static readonly HashSet<ulong> PreparingPlayers = new();
    private static INetGameService? _service;

    public static void Attach(INetGameService service)
    {
        if (ReferenceEquals(_service, service))
        {
            return;
        }

        Detach();
        _service = service;
        service.RegisterMessageHandler<LateJoinProfileMessage>(OnProfileReceived);
        service.RegisterMessageHandler<LateJoinPlayerAddedMessage>(OnPlayerAddedReceived);
        service.RegisterMessageHandler<LateJoinPlayerRemovedMessage>(OnPlayerRemovedReceived);
        service.RegisterMessageHandler<LateJoinCatchUpCompleteMessage>(OnCatchUpCompleteReceived);
        service.RegisterMessageHandler<LateJoinSnapshotMessage>(OnSnapshotReceived);
        if (service is NetHostGameService host)
        {
            host.ClientDisconnected += OnHostClientDisconnected;
        }
    }

    public static void Detach()
    {
        if (_service != null)
        {
            _service.UnregisterMessageHandler<LateJoinProfileMessage>(OnProfileReceived);
            _service.UnregisterMessageHandler<LateJoinPlayerAddedMessage>(OnPlayerAddedReceived);
            _service.UnregisterMessageHandler<LateJoinPlayerRemovedMessage>(OnPlayerRemovedReceived);
            _service.UnregisterMessageHandler<LateJoinCatchUpCompleteMessage>(OnCatchUpCompleteReceived);
            _service.UnregisterMessageHandler<LateJoinSnapshotMessage>(OnSnapshotReceived);
            if (_service is NetHostGameService host)
            {
                host.ClientDisconnected -= OnHostClientDisconnected;
            }
        }

        _service = null;
        lock (SyncRoot)
        {
            PendingPlayers.Clear();
            CompletingPlayers.Clear();
            PreparingPlayers.Clear();
        }
    }

    public static bool IsSafeCheckpoint()
    {
        lock (SyncRoot)
        {
            if (PendingPlayers.Count > 0 || CompletingPlayers.Count > 0)
            {
                return false;
            }
        }

        RunState? state = RunManager.Instance.DebugOnlyGetState();
        NMapScreen? map = NMapScreen.Instance;
        if (state?.CurrentMapPointHistoryEntry == null ||
            map is not { IsOpen: true, IsTravelEnabled: true, IsTraveling: false } ||
            CombatManager.Instance.IsInProgress ||
            !RunManager.Instance.ActionQueueSet.IsEmpty)
        {
            return false;
        }

        return RunManager.Instance.RunLobby?.ConnectedPlayerIds.All(playerId =>
            RunManager.Instance.InputSynchronizer.GetScreenType(playerId) == NetScreenType.Map) == true;
    }

    public static void SetMapTravelEnabled(bool enabled)
    {
        NMapScreen.Instance?.SetTravelEnabled(enabled);
    }

    public static void SendCatchUpComplete(Player player)
    {
        RunState state = (RunState)player.RunState;
        RunManager.Instance.NetService.SendMessage(new LateJoinCatchUpCompleteMessage
        {
            Player = player.ToSerializable(),
            History = LatePlayerState.CaptureHistory(state, player.NetId)
        });
    }

    private static void OnProfileReceived(LateJoinProfileMessage message, ulong senderId)
    {
        if (_service?.Type != NetGameType.Host)
        {
            return;
        }

        RunState? state = RunManager.Instance.DebugOnlyGetState();
        bool isExistingPlayer = state?.GetPlayer(senderId) != null;
        lock (SyncRoot)
        {
            if (!isExistingPlayer &&
                (PreparingPlayers.Count > 0 || PendingPlayers.Count > 0 || CompletingPlayers.Count > 0))
            {
                SendReady(senderId, accepted: false, isLateJoin: false,
                    "Another player is already joining this run.");
                return;
            }
            if (!PreparingPlayers.Add(senderId))
            {
                return;
            }
        }

        TaskHelper.RunSafely(PreparePlayerAndReply(message, senderId));
    }

    private static async Task PreparePlayerAndReply(LateJoinProfileMessage message, ulong senderId)
    {
        PendingJoin? transaction = null;
        try
        {
            RunState? state = RunManager.Instance.DebugOnlyGetState();
            if (state == null || _service == null)
            {
                SendReady(senderId, accepted: false, isLateJoin: false, "The host has no active run.");
                return;
            }

            Player? player = state.GetPlayer(senderId);
            bool isLateJoin = false;

            if (player == null)
            {
                if (state.Players.Count >= 4)
                {
                    SendReady(senderId, accepted: false, isLateJoin: false, "The run already has four players.");
                    return;
                }
                if (!IsSafeCheckpoint())
                {
                    SendReady(senderId, accepted: false, isLateJoin: false,
                        "Late joins are accepted only while the party is waiting on the map.");
                    return;
                }

                CharacterModel? character = ModelDb.AllCharacters.FirstOrDefault(candidate =>
                    string.Equals(candidate.Id.Entry, message.CharacterEntry, StringComparison.OrdinalIgnoreCase));
                if (character == null)
                {
                    SendReady(senderId, accepted: false, isLateJoin: false, "The selected character is unavailable.");
                    return;
                }
                if (character.StartingRelics.Any(relic => relic.HasUponPickupEffect))
                {
                    SendReady(senderId, accepted: false, isLateJoin: false,
                        "That character has a starting relic whose pickup choices cannot be synchronized safely.");
                    return;
                }

                await PreloadManager.LoadRunAssets(new[] { character });
                if (!IsHostPeerConnected(senderId))
                {
                    return;
                }

                transaction = new PendingJoin
                {
                    RunRng = state.Rng.ToSerializable(),
                    SharedRelicGrabBag = state.SharedRelicGrabBag.ToSerializable()
                };
                lock (SyncRoot)
                {
                    PendingPlayers.Add(senderId, transaction);
                }
                SetMapTravelEnabled(enabled: false);

                UnlockState unlockState = UnlockState.FromSerializable(message.UnlockState);
                player = Player.CreateForNewRun(character, unlockState, senderId);
                state.AddPlayerDebug(player, -1);
                state.Rng.LoadFromSerializable(transaction.RunRng);
                player.InitializeSeed(state.Rng.StringSeed);
                LatePlayerState.InitializeRelicProgression(state, player);
                Player reference = state.Players.First(candidate => candidate.NetId != senderId);
                LatePlayerState.AlignProgressionWithReference(player, reference);
                LatePlayerState.RefreshUnlockState(state);
                LatePlayerState.EnsureHistoryEntries(state, senderId);
                LatePlayerState.ExtendFixedPlayerState(senderId);
                LatePlayerState.EnsurePlayerUi(state);
                SendPlayerAddedToExistingPeers(player, senderId);

                foreach (RelicModel relic in player.Relics)
                {
                    await relic.AfterObtained();
                    if (!IsPending(senderId))
                    {
                        return;
                    }
                }

                SendPlayerAddedToExistingPeers(player, senderId);
                lock (SyncRoot)
                {
                    if (!PendingPlayers.TryGetValue(senderId, out PendingJoin? current) ||
                        !ReferenceEquals(current, transaction))
                    {
                        return;
                    }
                    current.BaselinePlayer = player.ToSerializable();
                }
                isLateJoin = true;
                Log.Info($"[JoinInProgress] Added late player {senderId} as {character.Id.Entry}.");
            }

            SendReady(senderId, accepted: true, isLateJoin, string.Empty);
        }
        catch (Exception exception)
        {
            Log.Error($"[JoinInProgress] Failed to prepare late player {senderId}: {exception}");
            if (transaction != null)
            {
                AbortPendingJoin(senderId, transaction);
            }
            if (IsHostPeerConnected(senderId))
            {
                SendReady(senderId, accepted: false, isLateJoin: false, exception.Message);
            }
        }
        finally
        {
            lock (SyncRoot)
            {
                PreparingPlayers.Remove(senderId);
            }
        }
    }

    private static bool IsPending(ulong playerId)
    {
        lock (SyncRoot)
        {
            return PendingPlayers.ContainsKey(playerId);
        }
    }

    private static bool IsHostPeerConnected(ulong playerId)
    {
        return _service is NetHostGameService host &&
               host.ConnectedPeers.Any(peer => peer.peerId == playerId);
    }

    private static void SendReady(ulong senderId, bool accepted, bool isLateJoin, string reason)
    {
        if (_service == null)
        {
            return;
        }

        List<ulong> connected = RunManager.Instance.RunLobby?.ConnectedPlayerIds.ToList() ?? new List<ulong>();
        if (!connected.Contains(senderId))
        {
            connected.Add(senderId);
        }

        _service.SendMessage(new LateJoinReadyMessage
        {
            Accepted = accepted,
            IsLateJoin = isLateJoin,
            RejectionReason = reason,
            ConnectedPlayerIds = connected,
            NextActionId = RunManager.Instance.ActionQueueSet.NextActionId,
            NextHookId = RunManager.Instance.ActionQueueSynchronizer.NextHookId,
            NextChecksumId = RunManager.Instance.ChecksumTracker.NextId,
            MapGenerationCount = RunManager.Instance.MapSelectionSynchronizer.MapGenerationCount
        }, senderId);
    }

    private static void SendPlayerAddedToExistingPeers(Player player, ulong joiningPlayerId)
    {
        if (_service is not NetHostGameService host)
        {
            return;
        }

        RunState state = (RunState)player.RunState;
        LateJoinPlayerAddedMessage message = new()
        {
            Player = player.ToSerializable(),
            RunRng = state.Rng.ToSerializable(),
            SharedRelicGrabBag = state.SharedRelicGrabBag.ToSerializable()
        };
        foreach (NetClientData peer in host.ConnectedPeers)
        {
            if (peer.readyForBroadcasting && peer.peerId != joiningPlayerId)
            {
                host.SendMessage(message, peer.peerId);
            }
        }
    }

    private static void OnPlayerAddedReceived(LateJoinPlayerAddedMessage message, ulong senderId)
    {
        if (_service?.Type != NetGameType.Client)
        {
            return;
        }

        TaskHelper.RunSafely(AddRemotePlayer(message));
    }

    private static async Task AddRemotePlayer(LateJoinPlayerAddedMessage message)
    {
        RunState? state = RunManager.Instance.DebugOnlyGetState();
        if (state == null)
        {
            return;
        }

        state.Rng.LoadFromSerializable(message.RunRng);
        state.SharedRelicGrabBag.LoadFromSerializable(message.SharedRelicGrabBag);
        Player? existing = state.GetPlayer(message.Player.NetId);
        Player player = existing ?? LatePlayerState.AttachSerializedPlayer(state, message.Player);
        if (existing != null)
        {
            existing.SyncWithSerializedPlayer(message.Player);
        }
        await PreloadManager.LoadRunAssets(new[] { player.Character });
        LatePlayerState.EnsurePlayerUi(state);
        SetMapTravelEnabled(enabled: false);
        Log.Info($"[JoinInProgress] Attached remote late player {player.NetId}.");
    }

    private static void OnPlayerRemovedReceived(LateJoinPlayerRemovedMessage message, ulong senderId)
    {
        if (_service?.Type != NetGameType.Client)
        {
            return;
        }

        RunState? state = RunManager.Instance.DebugOnlyGetState();
        if (state != null)
        {
            state.Rng.LoadFromSerializable(message.RunRng);
            state.SharedRelicGrabBag.LoadFromSerializable(message.SharedRelicGrabBag);
            LatePlayerState.RemovePlayer(state, message.PlayerId);
        }
        SetMapTravelEnabled(enabled: true);
        CatchUpScreen.NotifyAborted(message.PlayerId);
    }

    private static void OnHostClientDisconnected(ulong playerId, NetErrorInfo info)
    {
        PendingJoin? transaction;
        lock (SyncRoot)
        {
            PendingPlayers.TryGetValue(playerId, out transaction);
        }
        if (transaction != null)
        {
            Log.Info($"[JoinInProgress] Rolling back disconnected late player {playerId}.");
            AbortPendingJoin(playerId, transaction);
        }
    }

    private static void OnCatchUpCompleteReceived(LateJoinCatchUpCompleteMessage message, ulong senderId)
    {
        if (_service?.Type != NetGameType.Host || message.Player.NetId != senderId)
        {
            return;
        }

        PendingJoin transaction;
        lock (SyncRoot)
        {
            if (!PendingPlayers.TryGetValue(senderId, out transaction!) ||
                transaction.BaselinePlayer == null ||
                !CompletingPlayers.Add(senderId))
            {
                return;
            }
            PendingPlayers.Remove(senderId);
        }

        AcceptCompletedCatchUp(message, senderId, transaction);
    }

    private static void AcceptCompletedCatchUp(
        LateJoinCatchUpCompleteMessage message,
        ulong senderId,
        PendingJoin transaction)
    {
        try
        {
            RunState state = RunManager.Instance.DebugOnlyGetState() ??
                throw new InvalidOperationException("The host run ended during catch-up.");
            Player player = state.GetPlayer(senderId) ??
                throw new InvalidOperationException("The late player is no longer in the host run.");
            if (_service == null || transaction.BaselinePlayer == null)
            {
                throw new InvalidOperationException("The late-join transaction is no longer active.");
            }

            SerializablePlayer sanitized = LateJoinSubmissionValidator.ValidateAndSanitize(
                state,
                player,
                transaction.BaselinePlayer,
                message.Player,
                message.History);
            player.SyncWithSerializedPlayer(sanitized);
            LatePlayerState.ApplyHistory(state, senderId, message.History);
            LatePlayerState.EnsurePlayerUi(state);

            LateJoinSnapshotMessage snapshot = new()
            {
                Player = player.ToSerializable(),
                History = LatePlayerState.CaptureHistory(state, senderId),
                RunRng = state.Rng.ToSerializable(),
                SharedRelicGrabBag = state.SharedRelicGrabBag.ToSerializable()
            };
            _service.SendMessage(snapshot);
            Log.Info($"[JoinInProgress] Catch-up complete for player {senderId}.");
        }
        catch (Exception exception)
        {
            Log.Error($"[JoinInProgress] Rejected catch-up state from {senderId}: {exception}");
            RollBackPlayer(senderId, transaction);
            if (_service is NetHostGameService host && IsHostPeerConnected(senderId))
            {
                host.DisconnectClient(senderId, NetError.InternalError);
            }
        }
        finally
        {
            lock (SyncRoot)
            {
                CompletingPlayers.Remove(senderId);
            }
            UpdateMapTravelLock();
        }
    }

    private static void AbortPendingJoin(ulong playerId, PendingJoin expectedTransaction)
    {
        lock (SyncRoot)
        {
            if (!PendingPlayers.TryGetValue(playerId, out PendingJoin? current) ||
                !ReferenceEquals(current, expectedTransaction))
            {
                return;
            }
            PendingPlayers.Remove(playerId);
        }
        RollBackPlayer(playerId, expectedTransaction);
        UpdateMapTravelLock();
    }

    private static void RollBackPlayer(ulong playerId, PendingJoin transaction)
    {
        RunState? state = RunManager.Instance.DebugOnlyGetState();
        if (state != null)
        {
            state.Rng.LoadFromSerializable(transaction.RunRng);
            state.SharedRelicGrabBag.LoadFromSerializable(transaction.SharedRelicGrabBag);
            LatePlayerState.RemovePlayer(state, playerId);
        }

        if (_service is NetHostGameService host)
        {
            LateJoinPlayerRemovedMessage message = new()
            {
                PlayerId = playerId,
                RunRng = transaction.RunRng,
                SharedRelicGrabBag = transaction.SharedRelicGrabBag
            };
            foreach (NetClientData peer in host.ConnectedPeers.Where(peer => peer.readyForBroadcasting).ToList())
            {
                host.SendMessage(message, peer.peerId);
            }
        }
    }

    private static void UpdateMapTravelLock()
    {
        bool isBusy;
        lock (SyncRoot)
        {
            isBusy = PendingPlayers.Count > 0 || CompletingPlayers.Count > 0;
        }
        SetMapTravelEnabled(enabled: !isBusy);
    }

    private static void OnSnapshotReceived(LateJoinSnapshotMessage message, ulong senderId)
    {
        if (_service?.Type != NetGameType.Client)
        {
            return;
        }
        TaskHelper.RunSafely(ApplySnapshot(message));
    }

    private static async Task ApplySnapshot(LateJoinSnapshotMessage message)
    {
        RunState? state = RunManager.Instance.DebugOnlyGetState();
        Player? player = state?.GetPlayer(message.Player.NetId);
        if (state == null || player == null)
        {
            return;
        }

        state.Rng.LoadFromSerializable(message.RunRng);
        state.SharedRelicGrabBag.LoadFromSerializable(message.SharedRelicGrabBag);
        player.SyncWithSerializedPlayer(message.Player);
        LatePlayerState.ApplyHistory(state, message.Player.NetId, message.History);
        LatePlayerState.EnsurePlayerUi(state);
        await PreloadManager.LoadRunAssets(new[] { player.Character });
        SetMapTravelEnabled(enabled: true);
        CatchUpScreen.NotifySynchronized(message.Player.NetId);
    }
}
