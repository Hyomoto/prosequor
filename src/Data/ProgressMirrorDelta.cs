namespace Prosequor.Data;

/// <summary>Describes which WatchedAttributes paths to patch after a visible progress mutation.</summary>
public sealed class VisibleProgressChange
{
    public string? SkillId { get; init; }
    public bool MirrorPlayerTrack { get; init; }
    public bool MirrorAttributes { get; init; }
    public bool MirrorSkillTrack { get; init; }
    public bool MirrorUnlockTiers { get; init; }
    public string? UnlockNodeId { get; init; }

    public static VisibleProgressChange PlayerTrack() =>
        new() { MirrorPlayerTrack = true, MirrorAttributes = true };

    public static VisibleProgressChange Attributes() =>
        new() { MirrorAttributes = true };

    public static VisibleProgressChange SkillTrack(string skillId) =>
        new() { SkillId = skillId, MirrorSkillTrack = true };

    public static VisibleProgressChange SkillAndPlayer(string skillId) =>
        new()
        {
            SkillId = skillId,
            MirrorSkillTrack = true,
            MirrorPlayerTrack = true,
            MirrorAttributes = true
        };

    public static VisibleProgressChange Unlock(string skillId, string nodeId) =>
        new()
        {
            SkillId = skillId,
            MirrorUnlockTiers = true,
            UnlockNodeId = nodeId,
            MirrorPlayerTrack = true
        };
}
