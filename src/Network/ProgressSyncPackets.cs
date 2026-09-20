using ProtoBuf;

namespace Prosequor.Network;

/// <summary>Full private progress payload for the owning client (join / resync).</summary>
[ProtoContract]
public class ProgressSnapshotPacket
{
    [ProtoMember(1)]
    public int Seq { get; set; }

    [ProtoMember(2)]
    public int PlayerLevel { get; set; }

    [ProtoMember(3)]
    public float PlayerXp { get; set; }

    [ProtoMember(4)]
    public int UnlockPoints { get; set; }

    [ProtoMember(5)]
    public List<ProgressSkillTrackDto> Skills { get; set; } = new();

    [ProtoMember(6)]
    public List<ProgressAttributeBucketDto> AttributeBuckets { get; set; } = new();
}

/// <summary>Sparse private progress update for the owning client.</summary>
[ProtoContract]
public class ProgressDeltaPacket
{
    [ProtoMember(1)]
    public int Seq { get; set; }

    [ProtoMember(2)]
    public bool HasPlayerTrack { get; set; }

    [ProtoMember(3)]
    public int PlayerLevel { get; set; }

    [ProtoMember(4)]
    public float PlayerXp { get; set; }

    [ProtoMember(5)]
    public int UnlockPoints { get; set; }

    [ProtoMember(6)]
    public List<ProgressSkillTrackDto> Skills { get; set; } = new();

    [ProtoMember(7)]
    public bool HasAttributeBuckets { get; set; }

    [ProtoMember(8)]
    public List<ProgressAttributeBucketDto> AttributeBuckets { get; set; } = new();
}

/// <summary>Client asks the server for a fresh snapshot after a sequence gap.</summary>
[ProtoContract]
public class ProgressResyncRequestPacket
{
    [ProtoMember(1)]
    public int LastSeq { get; set; }
}

[ProtoContract]
public class ProgressSkillTrackDto
{
    [ProtoMember(1)]
    public string SkillId { get; set; } = "";

    [ProtoMember(2)]
    public int Level { get; set; }

    [ProtoMember(3)]
    public float Xp { get; set; }
}

[ProtoContract]
public class ProgressAttributeBucketDto
{
    [ProtoMember(1)]
    public string Id { get; set; } = "";

    [ProtoMember(2)]
    public float Fill { get; set; }
}
