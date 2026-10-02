using Prosequor.Data;

namespace Prosequor.Progress;

public enum UnlockPurchaseStatus
{
    Ok,
    UnknownSkill,
    NoTree,
    UnknownNode,
    AlreadyUnlocked,
    MissingPrerequisite,
    BlockedByExclusive,
    SkillLevelTooLow,
    NotEnoughPoints,
    SpecializationLimitReached
}

/// <summary>Pure eligibility checks for declared skill-tree nodes.</summary>
public static class SkillTreeEligibility
{
    public static UnlockPurchaseStatus Evaluate(
        SkillDef? skill,
        IPlayerProgress? progress,
        string nodeId,
        out SkillTreeNodeDef? node) =>
        Evaluate(skill, progress, null, default, nodeId, out node, out _, requirePoints: true);

    public static UnlockPurchaseStatus Evaluate(
        SkillDef? skill,
        IPlayerProgress? progress,
        ISkillRegistry? registry,
        string nodeId,
        out SkillTreeNodeDef? node) =>
        Evaluate(skill, progress, registry, default, nodeId, out node, out _, requirePoints: true);

    public static UnlockPurchaseStatus Evaluate(
        SkillDef? skill,
        IPlayerProgress? progress,
        ISkillRegistry? registry,
        LevelSet specializationLevels,
        string nodeId,
        out SkillTreeNodeDef? node) =>
        Evaluate(skill, progress, registry, specializationLevels, nodeId, out node, out _, requirePoints: true);

    /// <summary>
    /// Checks the next unpurchased tier. <paramref name="tier"/> is the 1-based tier the player would buy.
    /// </summary>
    public static UnlockPurchaseStatus Evaluate(
        SkillDef? skill,
        IPlayerProgress? progress,
        string nodeId,
        out SkillTreeNodeDef? node,
        out int tier) =>
        Evaluate(skill, progress, null, default, nodeId, out node, out tier, requirePoints: true);

    public static UnlockPurchaseStatus Evaluate(
        SkillDef? skill,
        IPlayerProgress? progress,
        ISkillRegistry? registry,
        string nodeId,
        out SkillTreeNodeDef? node,
        out int tier) =>
        Evaluate(skill, progress, registry, default, nodeId, out node, out tier, requirePoints: true);

    public static UnlockPurchaseStatus Evaluate(
        SkillDef? skill,
        IPlayerProgress? progress,
        ISkillRegistry? registry,
        LevelSet specializationLevels,
        string nodeId,
        out SkillTreeNodeDef? node,
        out int tier) =>
        Evaluate(skill, progress, registry, specializationLevels, nodeId, out node, out tier, requirePoints: true);

    /// <param name="requirePoints">
    /// When false, skips unlock-point / hobby-spendable checks (admin <c>GrantUnlock</c>).
    /// </param>
    public static UnlockPurchaseStatus Evaluate(
        SkillDef? skill,
        IPlayerProgress? progress,
        ISkillRegistry? registry,
        string nodeId,
        out SkillTreeNodeDef? node,
        out int tier,
        bool requirePoints) =>
        Evaluate(skill, progress, registry, default, nodeId, out node, out tier, requirePoints);

    /// <param name="requirePoints">
    /// When false, skips unlock-point / hobby-spendable checks (admin <c>GrantUnlock</c>).
    /// </param>
    /// <param name="specializationLevels">
    /// Player levels that grant a specialization slot. Empty means no slots.
    /// </param>
    public static UnlockPurchaseStatus Evaluate(
        SkillDef? skill,
        IPlayerProgress? progress,
        ISkillRegistry? registry,
        LevelSet specializationLevels,
        string nodeId,
        out SkillTreeNodeDef? node,
        out int tier,
        bool requirePoints)
    {
        node = null;
        tier = 0;
        if (skill == null)
        {
            return UnlockPurchaseStatus.UnknownSkill;
        }

        if (skill.Tree == null)
        {
            return UnlockPurchaseStatus.NoTree;
        }

        if (!skill.Tree.TryGet(nodeId, out SkillTreeNodeDef found))
        {
            return UnlockPurchaseStatus.UnknownNode;
        }

        node = found;
        if (progress == null)
        {
            return UnlockPurchaseStatus.UnknownSkill;
        }

        int owned = progress.GetUnlockTier(skill.Id, found.Id);
        if (owned >= found.MaxTier)
        {
            tier = found.MaxTier;
            return UnlockPurchaseStatus.AlreadyUnlocked;
        }

        tier = owned + 1;
        foreach (string excl in found.Excludes)
        {
            if (progress.HasUnlock(skill.Id, excl))
            {
                return UnlockPurchaseStatus.BlockedByExclusive;
            }
        }

        foreach (RequireGroup group in found.RequireGroups)
        {
            bool any = false;
            foreach (string req in group.Alternatives)
            {
                if (progress.HasUnlock(skill.Id, req))
                {
                    any = true;
                    break;
                }
            }

            if (!any)
            {
                return UnlockPurchaseStatus.MissingPrerequisite;
            }
        }

        SkillTreeTierDef next = found.TierAt(tier);
        if (progress.GetSkillLevel(skill.Id) < next.MinSkillLevel)
        {
            return UnlockPurchaseStatus.SkillLevelTooLow;
        }

        if (requirePoints && next.Cost > 0)
        {
            if (skill.IsHobby)
            {
                if (HobbyPointPolicy.Spendable(skill, progress) < next.Cost)
                {
                    return UnlockPurchaseStatus.NotEnoughPoints;
                }
            }
            else if (progress.UnlockPoints < next.Cost)
            {
                return UnlockPurchaseStatus.NotEnoughPoints;
            }
        }

        if (found.IsSpecialization)
        {
            if (registry == null)
            {
                return UnlockPurchaseStatus.SpecializationLimitReached;
            }

            if (!SpecializationPolicy.HasAvailableSlot(progress, registry, specializationLevels))
            {
                return UnlockPurchaseStatus.SpecializationLimitReached;
            }
        }

        return UnlockPurchaseStatus.Ok;
    }

    public static bool IsEligible(
        SkillDef? skill,
        IPlayerProgress? progress,
        string nodeId) =>
        Evaluate(skill, progress, null, default, nodeId, out _) == UnlockPurchaseStatus.Ok;

    public static bool IsEligible(
        SkillDef? skill,
        IPlayerProgress? progress,
        ISkillRegistry? registry,
        string nodeId) =>
        Evaluate(skill, progress, registry, default, nodeId, out _) == UnlockPurchaseStatus.Ok;

    public static bool IsEligible(
        SkillDef? skill,
        IPlayerProgress? progress,
        ISkillRegistry? registry,
        LevelSet specializationLevels,
        string nodeId) =>
        Evaluate(skill, progress, registry, specializationLevels, nodeId, out _) == UnlockPurchaseStatus.Ok;
}
