using System.Collections.Generic;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Runs;

namespace JoinInProgress.Resync;

/// <summary>
/// Host-generated, immutable-by-convention offers for a late player's catch-up walk.
/// The host keeps its copy and validates selections against it; the joining client only renders it.
/// </summary>
public sealed class CatchUpRewardPlan : IPacketSerializable
{
    public List<CatchUpFloorRewardPlan> Floors { get; set; } = new();
    public SerializablePlayerRngSet FinalPlayerRng { get; set; } = new();
    public SerializablePlayerOddsSet FinalPlayerOdds { get; set; } = new();
    public SerializableRelicGrabBag FinalRelicGrabBag { get; set; } = new();

    public void Serialize(PacketWriter writer)
    {
        writer.WriteList(Floors);
        writer.Write(FinalPlayerRng);
        writer.Write(FinalPlayerOdds);
        writer.Write(FinalRelicGrabBag);
    }

    public void Deserialize(PacketReader reader)
    {
        Floors = reader.ReadList<CatchUpFloorRewardPlan>();
        FinalPlayerRng = reader.Read<SerializablePlayerRngSet>();
        FinalPlayerOdds = reader.Read<SerializablePlayerOddsSet>();
        FinalRelicGrabBag = reader.Read<SerializableRelicGrabBag>();
    }
}

public sealed class CatchUpFloorRewardPlan : IPacketSerializable
{
    public int ActIndex { get; set; }
    public int FloorIndex { get; set; }
    public int GlobalFloor { get; set; }
    public MapPointType MapPointType { get; set; }
    public List<CatchUpRoomRewardPlan> Rooms { get; set; } = new();

    public void Serialize(PacketWriter writer)
    {
        writer.WriteInt(ActIndex);
        writer.WriteInt(FloorIndex);
        writer.WriteInt(GlobalFloor);
        writer.WriteEnum(MapPointType);
        writer.WriteList(Rooms);
    }

    public void Deserialize(PacketReader reader)
    {
        ActIndex = reader.ReadInt();
        FloorIndex = reader.ReadInt();
        GlobalFloor = reader.ReadInt();
        MapPointType = reader.ReadEnum<MapPointType>();
        Rooms = reader.ReadList<CatchUpRoomRewardPlan>();
    }
}

public sealed class CatchUpRoomRewardPlan : IPacketSerializable
{
    public RoomType RoomType { get; set; }
    public ModelId? ModelId { get; set; }
    public int Gold { get; set; }
    public List<CatchUpCardRewardGroup> CardRewardGroups { get; set; } = new();
    public List<ModelId> RelicRewards { get; set; } = new();
    public List<ModelId> PotionRewards { get; set; } = new();
    public CatchUpShopRewardPlan? Shop { get; set; }
    public List<CatchUpAncientRelicOffer> AncientRelicChoices { get; set; } = new();
    public int AncientHealPercent { get; set; }

    public void Serialize(PacketWriter writer)
    {
        writer.WriteEnum(RoomType);
        CatchUpPlanPacket.WriteNullableModelId(writer, ModelId);
        writer.WriteInt(Gold);
        writer.WriteList(CardRewardGroups);
        CatchUpPlanPacket.WriteModelIds(writer, RelicRewards);
        CatchUpPlanPacket.WriteModelIds(writer, PotionRewards);
        writer.WriteBool(Shop != null);
        if (Shop != null)
        {
            writer.Write(Shop);
        }
        writer.WriteList(AncientRelicChoices);
        writer.WriteInt(AncientHealPercent);
    }

    public void Deserialize(PacketReader reader)
    {
        RoomType = reader.ReadEnum<RoomType>();
        ModelId = CatchUpPlanPacket.ReadNullableModelId(reader);
        Gold = reader.ReadInt();
        CardRewardGroups = reader.ReadList<CatchUpCardRewardGroup>();
        RelicRewards = CatchUpPlanPacket.ReadModelIds(reader);
        PotionRewards = CatchUpPlanPacket.ReadModelIds(reader);
        Shop = reader.ReadBool() ? reader.Read<CatchUpShopRewardPlan>() : null;
        AncientRelicChoices = reader.ReadList<CatchUpAncientRelicOffer>();
        AncientHealPercent = reader.ReadInt();
    }
}

public sealed class CatchUpCardRewardGroup : IPacketSerializable
{
    public List<SerializableCard> Cards { get; set; } = new();

    public void Serialize(PacketWriter writer)
    {
        writer.WriteList(Cards);
    }

    public void Deserialize(PacketReader reader)
    {
        Cards = reader.ReadList<SerializableCard>();
    }
}

public sealed class CatchUpShopRewardPlan : IPacketSerializable
{
    public List<CatchUpShopCardOffer> Cards { get; set; } = new();
    public List<CatchUpShopModelOffer> Relics { get; set; } = new();
    public List<CatchUpShopModelOffer> Potions { get; set; } = new();

    public void Serialize(PacketWriter writer)
    {
        writer.WriteList(Cards);
        writer.WriteList(Relics);
        writer.WriteList(Potions);
    }

    public void Deserialize(PacketReader reader)
    {
        Cards = reader.ReadList<CatchUpShopCardOffer>();
        Relics = reader.ReadList<CatchUpShopModelOffer>();
        Potions = reader.ReadList<CatchUpShopModelOffer>();
    }
}

public sealed class CatchUpShopCardOffer : IPacketSerializable
{
    public SerializableCard Card { get; set; } = new();
    public int Cost { get; set; }
    public bool IsColorless { get; set; }
    public bool IsOnSale { get; set; }

    public void Serialize(PacketWriter writer)
    {
        writer.Write(Card);
        writer.WriteInt(Cost);
        writer.WriteBool(IsColorless);
        writer.WriteBool(IsOnSale);
    }

    public void Deserialize(PacketReader reader)
    {
        Card = reader.Read<SerializableCard>();
        Cost = reader.ReadInt();
        IsColorless = reader.ReadBool();
        IsOnSale = reader.ReadBool();
    }
}

public sealed class CatchUpShopModelOffer : IPacketSerializable
{
    public ModelId ModelId { get; set; } = null!;
    public int Cost { get; set; }

    public void Serialize(PacketWriter writer)
    {
        writer.WriteFullModelId(ModelId);
        writer.WriteInt(Cost);
    }

    public void Deserialize(PacketReader reader)
    {
        ModelId = reader.ReadFullModelId();
        Cost = reader.ReadInt();
    }
}

public sealed class CatchUpAncientRelicOffer : IPacketSerializable
{
    public SerializableRelic Relic { get; set; } = new();
    public string OptionTextKey { get; set; } = string.Empty;

    public void Serialize(PacketWriter writer)
    {
        writer.Write(Relic);
        writer.WriteString(OptionTextKey);
    }

    public void Deserialize(PacketReader reader)
    {
        Relic = reader.Read<SerializableRelic>();
        OptionTextKey = reader.ReadString();
    }
}

internal static class CatchUpPlanPacket
{
    public static void WriteNullableModelId(PacketWriter writer, ModelId? modelId)
    {
        writer.WriteBool(modelId != null);
        if (modelId != null)
        {
            writer.WriteFullModelId(modelId);
        }
    }

    public static ModelId? ReadNullableModelId(PacketReader reader)
    {
        return reader.ReadBool() ? reader.ReadFullModelId() : null;
    }

    public static void WriteModelIds(PacketWriter writer, IReadOnlyList<ModelId> ids)
    {
        writer.WriteInt(ids.Count);
        foreach (ModelId id in ids)
        {
            writer.WriteFullModelId(id);
        }
    }

    public static List<ModelId> ReadModelIds(PacketReader reader)
    {
        int count = reader.ReadInt();
        List<ModelId> ids = new(count);
        for (int i = 0; i < count; i++)
        {
            ids.Add(reader.ReadFullModelId());
        }
        return ids;
    }
}
