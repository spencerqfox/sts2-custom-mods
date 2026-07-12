using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.RelicPools;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Multiplayer;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Runs.History;
using MegaCrit.Sts2.Core.Saves.Runs;
using MegaCrit.Sts2.Core.Unlocks;

namespace JoinInProgress.Infrastructure;

internal static class LatePlayerState
{
    public static Player AttachSerializedPlayer(RunState state, SerializablePlayer serializedPlayer)
    {
        Player? existing = state.GetPlayer(serializedPlayer.NetId);
        if (existing != null)
        {
            return existing;
        }

        Player player = Player.FromSerializable(serializedPlayer);
        List<Player> players = (List<Player>)AccessTools.Field(typeof(RunState), "_players").GetValue(state)!;
        players.Add(player);
        player.RunState = state;
        foreach (var card in player.Deck.Cards)
        {
            state.AddCard(card, player);
        }

        RefreshUnlockState(state);
        EnsureHistoryEntries(state, player.NetId);
        ExtendFixedPlayerState(player.NetId);
        return player;
    }

    public static void EnsureHistoryEntries(RunState state, ulong playerId)
    {
        foreach (MapPointHistoryEntry floor in state.MapPointHistory.SelectMany(act => act))
        {
            if (floor.PlayerStats.All(entry => entry.PlayerId != playerId))
            {
                floor.PlayerStats.Add(new PlayerMapPointHistoryEntry { PlayerId = playerId });
            }
        }
    }

    public static void RefreshUnlockState(RunState state)
    {
        AccessTools.Property(typeof(RunState), nameof(RunState.UnlockState))
            .SetValue(state, new UnlockState(state.Players.Select(player => player.UnlockState)));
    }

    public static List<PlayerMapPointHistoryEntry> CaptureHistory(RunState state, ulong playerId)
    {
        return state.MapPointHistory
            .SelectMany(act => act)
            .Select(floor => floor.GetEntry(playerId))
            .ToList();
    }

    public static void ApplyHistory(RunState state, ulong playerId, IReadOnlyList<PlayerMapPointHistoryEntry> history)
    {
        List<MapPointHistoryEntry> floors = state.MapPointHistory.SelectMany(act => act).ToList();
        if (floors.Count != history.Count)
        {
            throw new InvalidOperationException(
                $"Late-join history length mismatch. Local={floors.Count}, remote={history.Count}.");
        }

        for (int i = 0; i < floors.Count; i++)
        {
            floors[i].PlayerStats.RemoveAll(entry => entry.PlayerId == playerId);
            history[i].PlayerId = playerId;
            floors[i].PlayerStats.Add(history[i]);
        }
    }

    public static void ExtendFixedPlayerState(ulong playerId)
    {
        RunManager manager = RunManager.Instance;
        AddActionQueue(manager.ActionQueueSet, playerId);
        AddRewardState(manager.RewardsSetSynchronizer);
        AddListDefault(manager.ActChangeSynchronizer, "_readyPlayers", false);
        AddListDefault(manager.MapSelectionSynchronizer, "_votes", null);
        AddListDefault(manager.HoveredModelTracker, "_hoveredModels", null);
        AddListDefault(manager.PlayerChoiceSynchronizer, "_choiceIds", 0u);
    }

    public static void EnsurePlayerUi(RunState state)
    {
        var container = MegaCrit.Sts2.Core.Nodes.NRun.Instance?.GlobalUi.MultiplayerPlayerContainer;
        if (container == null)
        {
            EnsureMapVoteUi(state);
            return;
        }

        List<NMultiplayerPlayerState> nodes =
            (List<NMultiplayerPlayerState>)AccessTools.Field(typeof(NMultiplayerPlayerStateContainer), "_nodes")
                .GetValue(container)!;

        foreach (Player player in state.Players)
        {
            if (nodes.Any(node => node.Player == player))
            {
                continue;
            }

            NMultiplayerPlayerState node = NMultiplayerPlayerState.Create(player);
            container.AddChild(node);
            nodes.Add(node);
        }

        AccessTools.Method(typeof(NMultiplayerPlayerStateContainer), "UpdatePosition")?.Invoke(container, null);
        AccessTools.Method(typeof(NMultiplayerPlayerStateContainer), "UpdateNavigation")?.Invoke(container, null);
        EnsureMapVoteUi(state);
    }

    public static void AlignProgressionWithReference(Player player, Player reference)
    {
        var playerRng = player.PlayerRng.ToSerializable();
        var referenceRng = reference.PlayerRng.ToSerializable();
        foreach (var (rngType, counter) in referenceRng.Counters)
        {
            if (!playerRng.Counters.TryGetValue(rngType, out int current) || counter > current)
            {
                playerRng.Counters[rngType] = counter;
            }
        }
        player.PlayerRng.LoadFromSerializable(playerRng);
        player.PlayerOdds.LoadFromSerializable(reference.PlayerOdds.ToSerializable());

        foreach (var choice in ((RunState)player.RunState).MapPointHistory
                     .SelectMany(act => act)
                     .SelectMany(floor => floor.PlayerStats.First(entry => entry.PlayerId == reference.NetId).RelicChoices))
        {
            var relic = MegaCrit.Sts2.Core.Models.ModelDb.GetByIdOrNull<MegaCrit.Sts2.Core.Models.RelicModel>(choice.choice);
            if (relic != null)
            {
                player.RelicGrabBag.Remove(relic);
            }
        }
        foreach (RelicModel relic in player.Relics)
        {
            player.RelicGrabBag.Remove(ModelDb.GetById<RelicModel>(relic.Id));
        }
    }

    public static void ApplyLocalAuthoritativeSnapshot(Player player, SerializablePlayer snapshot)
    {
        SerializablePlayer local = player.ToSerializable();
        bool relicsMatch = local.Relics.Select(relic => relic.Id)
            .SequenceEqual(snapshot.Relics.Select(relic => relic.Id));
        bool potionsMatch = local.Potions.Select(potion => (potion.Id, potion.SlotIndex))
            .SequenceEqual(snapshot.Potions.Select(potion => (potion.Id, potion.SlotIndex)));
        if (!relicsMatch || !potionsMatch)
        {
            throw new InvalidOperationException("The host snapshot did not match the locally replayed relics and potions.");
        }

        RunState state = (RunState)player.RunState;
        foreach (var card in player.Deck.Cards.ToList())
        {
            player.Deck.RemoveInternal(card);
            state.RemoveCard(card);
        }
        foreach (SerializableCard card in snapshot.Deck)
        {
            player.Deck.AddInternal(state.LoadCard(card, player));
        }

        player.Creature.SetMaxHpInternal(snapshot.MaxHp);
        player.Creature.SetCurrentHpInternal(snapshot.CurrentHp);
        player.MaxEnergy = snapshot.MaxEnergy;
        player.BaseOrbSlotCount = snapshot.BaseOrbSlotCount;
        player.Gold = snapshot.Gold;
        player.PlayerRng.LoadFromSerializable(snapshot.Rng);
        player.PlayerOdds.LoadFromSerializable(snapshot.Odds);
        player.RelicGrabBag.LoadFromSerializable(snapshot.RelicGrabBag);
        player.DiscoveredCards = snapshot.DiscoveredCards.ToList();
        player.DiscoveredEnemies = snapshot.DiscoveredEnemies.ToList();
        player.DiscoveredEpochs = snapshot.DiscoveredEpochs.ToList();
        player.DiscoveredPotions = snapshot.DiscoveredPotions.ToList();
        player.DiscoveredRelics = snapshot.DiscoveredRelics.ToList();
        AccessTools.Property(typeof(Player), nameof(Player.ExtraFields))
            .SetValue(player, ExtraPlayerFields.FromSerializable(snapshot.ExtraFields));
    }

    public static void InitializeRelicProgression(RunState state, Player player)
    {
        RunRngSet scratch = new(state.Rng.StringSeed);
        RelicGrabBag shared = new(refreshAllowed: true);
        shared.Populate(
            ModelDb.RelicPool<SharedRelicPool>().GetUnlockedRelics(state.UnlockState),
            scratch.UpFront);

        foreach (Player prior in state.Players)
        {
            if (prior == player)
            {
                break;
            }
            RelicGrabBag throwaway = new();
            throwaway.Populate(prior, scratch.UpFront);
        }

        ((IDictionary)AccessTools.Field(typeof(RelicGrabBag), "_deques").GetValue(player.RelicGrabBag)!).Clear();
        ((IList)AccessTools.Field(typeof(RelicGrabBag), "_mpFallbackDequeue").GetValue(player.RelicGrabBag)!).Clear();
        player.PopulateRelicGrabBagIfNecessary(scratch.UpFront);
    }

    public static void RemovePlayer(RunState state, ulong playerId)
    {
        Player? player = state.GetPlayer(playerId);
        if (player == null)
        {
            return;
        }

        List<Player> players = (List<Player>)AccessTools.Field(typeof(RunState), "_players").GetValue(state)!;
        int playerIndex = players.IndexOf(player);
        if (playerIndex < 0)
        {
            return;
        }

        ClearMapVoteUi(state, player);
        RunManager manager = RunManager.Instance;
        manager.InputSynchronizer.OnPlayerDisconnected(playerId);
        player.DeactivateHooks();
        RemoveActionQueue(manager.ActionQueueSet, playerId);
        RemoveListSlot(manager.RewardsSetSynchronizer, "_rewardStates", playerIndex);
        RemoveListSlot(manager.ActChangeSynchronizer, "_readyPlayers", playerIndex);
        RemoveListSlot(manager.MapSelectionSynchronizer, "_votes", playerIndex);
        RemoveListSlot(manager.HoveredModelTracker, "_hoveredModels", playerIndex);
        RemoveListSlot(manager.PlayerChoiceSynchronizer, "_choiceIds", playerIndex);

        foreach (var card in player.Deck.Cards.ToList())
        {
            state.RemoveCard(card);
        }
        foreach (MapPointHistoryEntry floor in state.MapPointHistory.SelectMany(act => act))
        {
            floor.PlayerStats.RemoveAll(entry => entry.PlayerId == playerId);
        }
        players.RemoveAt(playerIndex);
        RefreshUnlockState(state);
        RemovePlayerUi(player);

        manager.MapSelectionSynchronizer.OnLocationChanged(state.MapLocation);
        NMapScreen.Instance?.RefreshAllMapPointVotes();
    }

    private static void EnsureMapVoteUi(RunState state)
    {
        NMapScreen? map = NMapScreen.Instance;
        if (map == null)
        {
            return;
        }

        IDictionary points = (IDictionary)AccessTools.Field(typeof(NMapScreen), "_mapPointDictionary").GetValue(map)!;
        foreach (NMapPoint point in points.Values)
        {
            List<Player> roster = (List<Player>)AccessTools.Field(typeof(NMultiplayerVoteContainer), "_allPlayers")
                .GetValue(point.VoteContainer)!;
            foreach (Player player in state.Players)
            {
                if (!roster.Contains(player))
                {
                    roster.Add(player);
                }
            }
            point.VoteContainer.RefreshPlayerVotes(animate: false);
        }
    }

    private static void ClearMapVoteUi(RunState state, Player removedPlayer)
    {
        NMapScreen? map = NMapScreen.Instance;
        if (map == null)
        {
            return;
        }

        map.PlayerVoteDictionary.Clear();
        IDictionary points = (IDictionary)AccessTools.Field(typeof(NMapScreen), "_mapPointDictionary").GetValue(map)!;
        foreach (NMapPoint point in points.Values)
        {
            List<Player> roster = (List<Player>)AccessTools.Field(typeof(NMultiplayerVoteContainer), "_allPlayers")
                .GetValue(point.VoteContainer)!;
            point.VoteContainer.RefreshPlayerVotes(animate: false);
            roster.RemoveAll(player => player == removedPlayer || !state.Players.Contains(player));
        }
    }

    private static void RemovePlayerUi(Player player)
    {
        var container = MegaCrit.Sts2.Core.Nodes.NRun.Instance?.GlobalUi.MultiplayerPlayerContainer;
        if (container == null)
        {
            return;
        }

        List<NMultiplayerPlayerState> nodes =
            (List<NMultiplayerPlayerState>)AccessTools.Field(typeof(NMultiplayerPlayerStateContainer), "_nodes")
                .GetValue(container)!;
        foreach (NMultiplayerPlayerState node in nodes.Where(node => node.Player == player).ToList())
        {
            nodes.Remove(node);
            node.QueueFree();
        }
        AccessTools.Method(typeof(NMultiplayerPlayerStateContainer), "UpdatePosition")?.Invoke(container, null);
        AccessTools.Method(typeof(NMultiplayerPlayerStateContainer), "UpdateNavigation")?.Invoke(container, null);
    }

    private static void AddActionQueue(ActionQueueSet queueSet, ulong playerId)
    {
        FieldInfoRef field = new(typeof(ActionQueueSet), "_actionQueues");
        IList queues = field.GetList(queueSet);
        Type queueType = AccessTools.Inner(typeof(ActionQueueSet), "ActionQueue")!;
        var ownerField = AccessTools.Field(queueType, "ownerId");
        foreach (object queue in queues)
        {
            if ((ulong)ownerField.GetValue(queue)! == playerId)
            {
                return;
            }
        }

        object newQueue = Activator.CreateInstance(queueType, nonPublic: true)!;
        ownerField.SetValue(newQueue, playerId);
        queues.Add(newQueue);
    }

    private static void AddRewardState(RewardsSetSynchronizer synchronizer)
    {
        IList states = (IList)AccessTools.Field(typeof(RewardsSetSynchronizer), "_rewardStates")
            .GetValue(synchronizer)!;
        RunState? state = RunManager.Instance.DebugOnlyGetState();
        if (state == null || states.Count >= state.Players.Count)
        {
            return;
        }

        Type playerStateType = AccessTools.Inner(typeof(RewardsSetSynchronizer), "PlayerRewardState")!;
        Type rewardsSetStateType = AccessTools.Inner(typeof(RewardsSetSynchronizer), "RewardsSetState")!;
        Type bufferedMessageType = AccessTools.Inner(typeof(RewardsSetSynchronizer), "BufferedMessage")!;
        object playerState = Activator.CreateInstance(playerStateType, nonPublic: true)!;
        object rewardsStack = Activator.CreateInstance(typeof(List<>).MakeGenericType(rewardsSetStateType))!;
        object bufferedMessages = Activator.CreateInstance(typeof(List<>).MakeGenericType(bufferedMessageType))!;
        AccessTools.Field(playerStateType, "rewardsStack").SetValue(playerState, rewardsStack);
        AccessTools.Field(playerStateType, "bufferedMessages").SetValue(playerState, bufferedMessages);
        states.Add(playerState);
    }

    private static void AddListDefault(object owner, string fieldName, object? value)
    {
        IList list = (IList)AccessTools.Field(owner.GetType(), fieldName).GetValue(owner)!;
        RunState? state = RunManager.Instance.DebugOnlyGetState();
        if (state != null && list.Count < state.Players.Count)
        {
            list.Add(value);
        }
    }

    private static void RemoveActionQueue(ActionQueueSet queueSet, ulong playerId)
    {
        IList queues = (IList)AccessTools.Field(typeof(ActionQueueSet), "_actionQueues").GetValue(queueSet)!;
        Type queueType = AccessTools.Inner(typeof(ActionQueueSet), "ActionQueue")!;
        var ownerField = AccessTools.Field(queueType, "ownerId");
        for (int i = queues.Count - 1; i >= 0; i--)
        {
            if ((ulong)ownerField.GetValue(queues[i])! == playerId)
            {
                queues.RemoveAt(i);
            }
        }
    }

    private static void RemoveListSlot(object owner, string fieldName, int index)
    {
        IList list = (IList)AccessTools.Field(owner.GetType(), fieldName).GetValue(owner)!;
        if (index >= 0 && index < list.Count)
        {
            list.RemoveAt(index);
        }
    }

    private readonly struct FieldInfoRef
    {
        private readonly System.Reflection.FieldInfo _field;

        public FieldInfoRef(Type type, string name)
        {
            _field = AccessTools.Field(type, name);
        }

        public IList GetList(object instance)
        {
            return (IList)_field.GetValue(instance)!;
        }
    }
}
