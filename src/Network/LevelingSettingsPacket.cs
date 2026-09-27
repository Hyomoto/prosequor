using ProtoBuf;

namespace Prosequor.Network;

/// <summary>Server → client player cap and specialization level list.</summary>
[ProtoContract]
public class LevelingSettingsPacket
{
    [ProtoMember(1)]
    public int MaxPlayerLevel { get; set; } = 50;

    [ProtoMember(2)]
    public int[] SpecializationPointLevels { get; set; } = [];
}
