using ProtoBuf;
using Prosequor.Progress;

namespace Prosequor.Network;

[ProtoContract]
public class UnlockNodeRequestPacket
{
    [ProtoMember(1)]
    public string SkillId { get; set; } = "";

    [ProtoMember(2)]
    public string NodeId { get; set; } = "";
}

[ProtoContract]
public class UnlockNodeResultPacket
{
    [ProtoMember(1)]
    public string SkillId { get; set; } = "";

    [ProtoMember(2)]
    public string NodeId { get; set; } = "";

    [ProtoMember(3)]
    public UnlockPurchaseStatus Status { get; set; }
}
