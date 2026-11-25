using Prosequor.Data;

namespace Prosequor.Progress;

/// <summary>
/// Clears every owned unlock on one skill. Keeps XP and level.
/// Non-hobby authored tier costs return to global unlock points; hobby costs do not.
/// Class starting nodes are restored to tier 1 without refunding that rank.
/// </summary>
public static class SkillTreeReset
{
    public readonly struct Result
    {
        public int NodesCleared { get; init; }
        public int PointsRefunded { get; init; }
        public IReadOnlyList<string> ClearedNodeIds { get; init; }
    }

    /// <summary>
    /// Wipe owned tiers on <paramref name="skill"/>, refund spent costs, and restore
    /// <paramref name="startingNodeIds"/> to tier 1. Idempotent when already clear
    /// (aside from re-stamping starting nodes that are missing).
    /// </summary>
    public static Result ClearTree(
        PlayerProgressState state,
        SkillDef skill,
        IReadOnlyCollection<string>? startingNodeIds = null)
    {
        if (state?.Skills == null || skill == null)
        {
            return new Result
            {
                NodesCleared = 0,
                PointsRefunded = 0,
                ClearedNodeIds = Array.Empty<string>()
            };
        }

        HashSet<string> keep = BuildStartingSet(skill, startingNodeIds);
        if (!state.Skills.TryGetValue(skill.Id, out SkillProgressState? progress) || progress == null)
        {
            progress = state.GetOrCreateSkill(skill.Id);
        }

        List<string> clearedIds = new();
        int pointRefund = 0;

        foreach (KeyValuePair<string, int> kv in progress.UnlockTiers.ToList())
        {
            string nodeId = kv.Key;
            int owned = kv.Value;
            if (owned <= 0)
            {
                progress.UnlockTiers.Remove(nodeId);
                continue;
            }

            clearedIds.Add(nodeId);
            pointRefund += RefundableCost(skill, nodeId, owned, keep);
            progress.UnlockTiers.Remove(nodeId);
        }

        foreach (string nodeId in keep)
        {
            if (progress.GetTier(nodeId) < 1)
            {
                if (!clearedIds.Contains(nodeId, StringComparer.OrdinalIgnoreCase))
                {
                    clearedIds.Add(nodeId);
                }

                progress.SetTier(nodeId, 1);
            }
        }

        int appliedRefund = 0;
        if (!skill.IsHobby && pointRefund > 0)
        {
            state.UnlockPoints = Math.Max(0, state.UnlockPoints + pointRefund);
            appliedRefund = pointRefund;
        }

        return new Result
        {
            NodesCleared = clearedIds.Count,
            PointsRefunded = appliedRefund,
            ClearedNodeIds = clearedIds
        };
    }

    static HashSet<string> BuildStartingSet(SkillDef skill, IReadOnlyCollection<string>? startingNodeIds)
    {
        HashSet<string> keep = new(StringComparer.OrdinalIgnoreCase);
        if (startingNodeIds == null || skill.Tree == null)
        {
            return keep;
        }

        foreach (string raw in startingNodeIds)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            if (skill.Tree.TryGet(raw.Trim(), out SkillTreeNodeDef node))
            {
                keep.Add(node.Id);
            }
        }

        return keep;
    }

    static int RefundableCost(
        SkillDef skill,
        string nodeId,
        int owned,
        HashSet<string> startingNodes)
    {
        if (skill.Tree == null)
        {
            return Math.Max(0, owned);
        }

        if (!skill.Tree.TryGet(nodeId, out SkillTreeNodeDef node))
        {
            return 0;
        }

        bool keepTier1 = startingNodes.Contains(node.Id);
        int sum = 0;
        for (int t = 1; t <= owned && t <= node.MaxTier; t++)
        {
            if (keepTier1 && t == 1)
            {
                continue;
            }

            int cost = node.TierAt(t).Cost;
            if (cost > 0)
            {
                sum += cost;
            }
        }

        return sum;
    }
}
