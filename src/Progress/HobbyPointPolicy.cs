using Prosequor.Data;

namespace Prosequor.Progress;

/// <summary>
/// Derived unlock points for hobby skills: entitled from tree cost × level / cap,
/// spendable = entitled − owned tier costs. No stored currency.
/// </summary>
public static class HobbyPointPolicy
{
    /// <summary>Sum of every compiled tier cost on the skill tree (0 when no tree).</summary>
    public static int TotalTierCost(SkillDef skill)
    {
        if (skill.Tree == null)
        {
            return 0;
        }

        int sum = 0;
        foreach (SkillTreeNodeDef node in skill.Tree.Nodes)
        {
            foreach (SkillTreeTierDef tier in node.Tiers)
            {
                if (tier.Cost > 0)
                {
                    sum += tier.Cost;
                }
            }
        }

        return sum;
    }

    /// <summary>
    /// Points the player is entitled to at <paramref name="skillLevel"/> for this hobby.
    /// Uses ceiling so a non-empty tree grants a point at level 1.
    /// </summary>
    public static int Entitled(SkillDef skill, int skillLevel)
    {
        if (!skill.IsHobby || skill.MaxLevel <= 0)
        {
            return 0;
        }

        int total = TotalTierCost(skill);
        if (total <= 0 || skillLevel <= 0)
        {
            return 0;
        }

        int level = Math.Min(skillLevel, skill.MaxLevel);
        return (int)Math.Ceiling(total * (double)level / skill.MaxLevel);
    }

    /// <summary>Sum of costs for owned tiers on this skill.</summary>
    public static int Spent(SkillDef skill, IPlayerProgress progress)
    {
        if (skill.Tree == null)
        {
            return 0;
        }

        int spent = 0;
        foreach (SkillTreeNodeDef node in skill.Tree.Nodes)
        {
            int owned = progress.GetUnlockTier(skill.Id, node.Id);
            for (int t = 1; t <= owned && t <= node.MaxTier; t++)
            {
                int cost = node.TierAt(t).Cost;
                if (cost > 0)
                {
                    spent += cost;
                }
            }
        }

        return spent;
    }

    /// <summary>Unspent hobby points: max(0, entitled − spent).</summary>
    public static int Spendable(SkillDef skill, IPlayerProgress progress)
    {
        if (!skill.IsHobby)
        {
            return 0;
        }

        int entitled = Entitled(skill, progress.GetSkillLevel(skill.Id));
        int spent = Spent(skill, progress);
        return Math.Max(0, entitled - spent);
    }
}
