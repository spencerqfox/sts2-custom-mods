using System.Collections.Generic;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Multiplayer.Transport;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves.Runs;
using MegaCrit.Sts2.Core.Unlocks;

namespace JoinInProgress.Networking;

public sealed class LateJoinProfileMessage : INetMessage, IPacketSerializable
{
    public bool ShouldBroadcast => false;
    public bool ShouldBuffer => true;
    public NetTransferMode Mode => NetTransferMode.Reliable;
    public LogLevel LogLevel => LogLevel.Info;

    public string CharacterEntry { get; set; } = string.Empty;
    public SerializableUnlockState UnlockState { get; set; } = new();

    public void Serialize(PacketWriter writer)
    {
        writer.WriteString(CharacterEntry);
        writer.Write(UnlockState);
    }

    public void Deserialize(PacketReader reader)
    {
        CharacterEntry = reader.ReadString();
        UnlockState = reader.Read<SerializableUnlockState>();
    }
}

public sealed class LateJoinReadyMessage : INetMessage, IPacketSerializable
{
    public bool ShouldBroadcast => false;
    public bool ShouldBuffer => true;
    public NetTransferMode Mode => NetTransferMode.Reliable;
    public LogLevel LogLevel => LogLevel.Info;

    public bool Accepted { get; set; }
    public bool IsLateJoin { get; set; }
    public string RejectionReason { get; set; } = string.Empty;
    public List<ulong> ConnectedPlayerIds { get; set; } = new();
    public uint NextActionId { get; set; }
    public uint NextHookId { get; set; }
    public uint NextChecksumId { get; set; }
    public int MapGenerationCount { get; set; }

    public void Serialize(PacketWriter writer)
    {
        writer.WriteBool(Accepted);
        writer.WriteBool(IsLateJoin);
        writer.WriteString(RejectionReason);
        writer.WriteInt(ConnectedPlayerIds.Count, 6);
        foreach (ulong playerId in ConnectedPlayerIds)
        {
            writer.WriteULong(playerId);
        }
        writer.WriteUInt(NextActionId);
        writer.WriteUInt(NextHookId);
        writer.WriteUInt(NextChecksumId);
        writer.WriteInt(MapGenerationCount);
    }

    public void Deserialize(PacketReader reader)
    {
        Accepted = reader.ReadBool();
        IsLateJoin = reader.ReadBool();
        RejectionReason = reader.ReadString();
        ConnectedPlayerIds = new List<ulong>();
        int count = reader.ReadInt(6);
        for (int i = 0; i < count; i++)
        {
            ConnectedPlayerIds.Add(reader.ReadULong());
        }
        NextActionId = reader.ReadUInt();
        NextHookId = reader.ReadUInt();
        NextChecksumId = reader.ReadUInt();
        MapGenerationCount = reader.ReadInt();
    }
}

public sealed class LateJoinPlayerAddedMessage : INetMessage, IPacketSerializable
{
    public bool ShouldBroadcast => false;
    public bool ShouldBuffer => true;
    public NetTransferMode Mode => NetTransferMode.Reliable;
    public LogLevel LogLevel => LogLevel.Info;

    public SerializablePlayer Player { get; set; } = new();
    public SerializableRunRngSet RunRng { get; set; } = new();
    public SerializableRelicGrabBag SharedRelicGrabBag { get; set; } = new();

    public void Serialize(PacketWriter writer)
    {
        writer.Write(Player);
        writer.Write(RunRng);
        writer.Write(SharedRelicGrabBag);
    }

    public void Deserialize(PacketReader reader)
    {
        Player = reader.Read<SerializablePlayer>();
        RunRng = reader.Read<SerializableRunRngSet>();
        SharedRelicGrabBag = reader.Read<SerializableRelicGrabBag>();
    }
}

public sealed class LateJoinCatchUpCompleteMessage : INetMessage, IPacketSerializable
{
    public bool ShouldBroadcast => false;
    public bool ShouldBuffer => true;
    public NetTransferMode Mode => NetTransferMode.Reliable;
    public LogLevel LogLevel => LogLevel.Info;

    public SerializablePlayer Player { get; set; } = new();
    public List<PlayerMapPointHistoryEntry> History { get; set; } = new();

    public void Serialize(PacketWriter writer)
    {
        writer.Write(Player);
        writer.WriteList(History);
    }

    public void Deserialize(PacketReader reader)
    {
        Player = reader.Read<SerializablePlayer>();
        History = reader.ReadList<PlayerMapPointHistoryEntry>();
    }
}

public sealed class LateJoinPlayerRemovedMessage : INetMessage, IPacketSerializable
{
    public bool ShouldBroadcast => false;
    public bool ShouldBuffer => true;
    public NetTransferMode Mode => NetTransferMode.Reliable;
    public LogLevel LogLevel => LogLevel.Info;

    public ulong PlayerId { get; set; }
    public SerializableRunRngSet RunRng { get; set; } = new();
    public SerializableRelicGrabBag SharedRelicGrabBag { get; set; } = new();

    public void Serialize(PacketWriter writer)
    {
        writer.WriteULong(PlayerId);
        writer.Write(RunRng);
        writer.Write(SharedRelicGrabBag);
    }

    public void Deserialize(PacketReader reader)
    {
        PlayerId = reader.ReadULong();
        RunRng = reader.Read<SerializableRunRngSet>();
        SharedRelicGrabBag = reader.Read<SerializableRelicGrabBag>();
    }
}

public sealed class LateJoinSnapshotMessage : INetMessage, IPacketSerializable
{
    public bool ShouldBroadcast => false;
    public bool ShouldBuffer => true;
    public NetTransferMode Mode => NetTransferMode.Reliable;
    public LogLevel LogLevel => LogLevel.Info;

    public SerializablePlayer Player { get; set; } = new();
    public List<PlayerMapPointHistoryEntry> History { get; set; } = new();
    public SerializableRunRngSet RunRng { get; set; } = new();
    public SerializableRelicGrabBag SharedRelicGrabBag { get; set; } = new();

    public void Serialize(PacketWriter writer)
    {
        writer.Write(Player);
        writer.WriteList(History);
        writer.Write(RunRng);
        writer.Write(SharedRelicGrabBag);
    }

    public void Deserialize(PacketReader reader)
    {
        Player = reader.Read<SerializablePlayer>();
        History = reader.ReadList<PlayerMapPointHistoryEntry>();
        RunRng = reader.Read<SerializableRunRngSet>();
        SharedRelicGrabBag = reader.Read<SerializableRelicGrabBag>();
    }
}
