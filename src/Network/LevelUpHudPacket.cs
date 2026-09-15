using ProtoBuf;

namespace Prosequor.Network;

/// <summary>Server → client Skyrim-style level-up HUD payload.</summary>
[ProtoContract]
public class LevelUpHudPacket
{
    /// <summary>Null or empty when only the player leveled.</summary>
    [ProtoMember(1)]
    public string SkillId { get; set; } = "";

    [ProtoMember(2)]
    public int SkillLevelBefore { get; set; }

    [ProtoMember(3)]
    public int SkillLevelAfter { get; set; }

    [ProtoMember(4)]
    public int PlayerLevelBefore { get; set; }

    [ProtoMember(5)]
    public int PlayerLevelAfter { get; set; }

    [ProtoMember(6)]
    public float PlayerBarFillBefore { get; set; }

    [ProtoMember(7)]
    public float PlayerBarFillAfter { get; set; }

    [ProtoMember(8)]
    public bool PlayerLeveledUp { get; set; }

    /// <summary>Attribute ids that gained a point during this player level jump (may be empty).</summary>
    [ProtoMember(9)]
    public List<string> AttributeGains { get; set; } = new();

    public bool SkillLeveledUp =>
        !string.IsNullOrWhiteSpace(SkillId) && SkillLevelAfter > SkillLevelBefore;
}
