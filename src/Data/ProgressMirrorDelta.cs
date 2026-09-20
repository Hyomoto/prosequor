namespace Prosequor.Data;

/// <summary>
/// Describes which public WatchedAttributes paths and owner-channel fields to patch
/// after a visible progress mutation.
/// </summary>
public sealed class VisibleProgressChange
{
    public string? SkillId { get; init; }
    public bool MirrorPlayerTrack { get; init; }
    public bool MirrorAttributeScores { get; init; }
    public bool MirrorAttributeBuckets { get; init; }
    public bool MirrorSkillTrack { get; init; }
    public bool MirrorUnlockTiers { get; init; }
    public string? UnlockNodeId { get; init; }

    /// <summary>
    /// When true, flush public WA and owner channel immediately.
    /// When false, coalesce with other pending XP/bucket updates (~150ms).
    /// </summary>
    public bool Immediate { get; init; }

    public static VisibleProgressChange PlayerTrack(bool immediate = true) =>
        new() { MirrorPlayerTrack = true, Immediate = immediate };

    public static VisibleProgressChange AttributeScores() =>
        new() { MirrorAttributeScores = true, Immediate = true };

    public static VisibleProgressChange AttributeBuckets() =>
        new() { MirrorAttributeBuckets = true, Immediate = false };

    public static VisibleProgressChange SkillTrack(string skillId) =>
        new() { SkillId = skillId, MirrorSkillTrack = true, Immediate = false };

    public static VisibleProgressChange SkillAndPlayer(string skillId) =>
        new()
        {
            SkillId = skillId,
            MirrorSkillTrack = true,
            MirrorPlayerTrack = true,
            MirrorAttributeScores = true,
            MirrorAttributeBuckets = true,
            Immediate = true
        };

    public static VisibleProgressChange Unlock(string skillId, string nodeId) =>
        new()
        {
            SkillId = skillId,
            MirrorUnlockTiers = true,
            UnlockNodeId = nodeId,
            MirrorPlayerTrack = true,
            Immediate = true
        };
}

/// <summary>Accumulates dirty progress units until a flush (immediate or coalesced).</summary>
public sealed class PendingProgressFlush
{
    public bool PlayerTrack { get; private set; }
    public bool AttributeScores { get; private set; }
    public bool AttributeBuckets { get; private set; }
    public bool Immediate { get; private set; }
    public HashSet<string> SkillTracks { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<(string SkillId, string NodeId)> Unlocks { get; } = new();

    public bool Any =>
        PlayerTrack
        || AttributeScores
        || AttributeBuckets
        || SkillTracks.Count > 0
        || Unlocks.Count > 0;

    public void Accumulate(VisibleProgressChange change)
    {
        if (change.MirrorPlayerTrack)
        {
            PlayerTrack = true;
        }

        if (change.MirrorAttributeScores)
        {
            AttributeScores = true;
        }

        if (change.MirrorAttributeBuckets)
        {
            AttributeBuckets = true;
        }

        if (change.MirrorSkillTrack && !string.IsNullOrWhiteSpace(change.SkillId))
        {
            SkillTracks.Add(change.SkillId);
        }

        if (change.MirrorUnlockTiers
            && !string.IsNullOrWhiteSpace(change.SkillId)
            && !string.IsNullOrWhiteSpace(change.UnlockNodeId))
        {
            Unlocks.Add((change.SkillId, change.UnlockNodeId));
        }

        if (change.Immediate)
        {
            Immediate = true;
        }
    }

    public void Clear()
    {
        PlayerTrack = false;
        AttributeScores = false;
        AttributeBuckets = false;
        Immediate = false;
        SkillTracks.Clear();
        Unlocks.Clear();
    }

    public VisibleProgressChange ToPublicChange()
    {
        // Public WA only: unlocks + attribute scores. Channel carries the rest.
        if (Unlocks.Count == 0 && !AttributeScores)
        {
            return new VisibleProgressChange();
        }

        if (Unlocks.Count == 1 && !AttributeScores)
        {
            (string skillId, string nodeId) = Unlocks[0];
            return new VisibleProgressChange
            {
                SkillId = skillId,
                UnlockNodeId = nodeId,
                MirrorUnlockTiers = true,
                Immediate = true
            };
        }

        return new VisibleProgressChange
        {
            MirrorUnlockTiers = Unlocks.Count > 0,
            MirrorAttributeScores = AttributeScores,
            // Multi-unlock: ApplyVisibleMirror rewrites the whole unlock tree.
            SkillId = Unlocks.Count == 1 ? Unlocks[0].SkillId : null,
            UnlockNodeId = Unlocks.Count == 1 ? Unlocks[0].NodeId : null,
            Immediate = true
        };
    }
}
