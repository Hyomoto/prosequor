using Prosequor.Data;

namespace Prosequor.Progress;

/// <summary>
/// Clears owned tiers on skills outside the access union. Keeps XP and level.
/// Non-hobby tier costs return to global unlock points; hobby costs do not.
/// </summary>
public static class SkillAccessReconcile
{
    /// <summary>
    /// Refund and clear tiers for registered skills that have owned tiers and are not in
    /// <paramref name="union"/>. Returns the number of skills cleared.
    /// </summary>
    public static int RefundLostAccess(
        PlayerProgressState state,
        ISkillRegistry skills,
        IReadOnlySet<string> union)
    {
        if (state?.Skills == null || skills == null || union == null)
        {
            return 0;
        }

        int cleared = 0;
        int pointRefund = 0;

        foreach (KeyValuePair<string, SkillProgressState> kv in state.Skills)
        {
            string skillId = kv.Key;
            SkillProgressState progress = kv.Value;
            if (progress == null || progress.UnlockTiers.Count == 0)
            {
                continue;
            }

            if (union.Contains(skillId))
            {
                continue;
            }

            if (!skills.TryGet(skillId, out SkillDef def) || def == null)
            {
                continue;
            }

            int cost = SumOwnedTierCosts(def, progress);
            progress.UnlockTiers.Clear();
            if (!def.IsHobby && cost > 0)
            {
                pointRefund += cost;
            }

            cleared++;
        }

        if (pointRefund > 0)
        {
            state.UnlockPoints = Math.Max(0, state.UnlockPoints + pointRefund);
        }

        return cleared;
    }

    static int SumOwnedTierCosts(SkillDef skill, SkillProgressState progress)
    {
        if (skill.Tree == null)
        {
            return 0;
        }

        int sum = 0;
        foreach (SkillTreeNodeDef node in skill.Tree.Nodes)
        {
            int owned = progress.GetTier(node.Id);
            for (int t = 1; t <= owned && t <= node.MaxTier; t++)
            {
                int cost = node.TierAt(t).Cost;
                if (cost > 0)
                {
                    sum += cost;
                }
            }
        }

        return sum;
    }
}
