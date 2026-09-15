using ProtoBuf;

namespace Prosequor.Network;

/// <summary>Client → server compiled-content fingerprint after the channel connects.</summary>
[ProtoContract]
public class ContentFingerprintPacket
{
    [ProtoMember(1)]
    public int Schema { get; set; }

    [ProtoMember(2)]
    public string Hash { get; set; } = "";
}

/// <summary>Server → client: authored content does not match. Diagnostic only; join continues.</summary>
[ProtoContract]
public class ContentFingerprintMismatchPacket
{
    [ProtoMember(1)]
    public int ServerSchema { get; set; }

    [ProtoMember(2)]
    public string ServerHash { get; set; } = "";

    [ProtoMember(3)]
    public int ClientSchema { get; set; }

    [ProtoMember(4)]
    public string ClientHash { get; set; } = "";
}
