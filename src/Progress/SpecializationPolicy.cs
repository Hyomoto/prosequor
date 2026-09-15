using Prosequor.Data;

namespace Prosequor.Progress;

/// <summary>
/// Derived specialization capacity from compiled level-up rules
/// (<see cref="LevelUpActionKind.EarnSpecializationPoint"/>).
/// Owned specializations are counted across every registered skill tree.
/// </summary>
public static class SpecializationPolicy
{
    public static int AllowedSlots(int playerLevel, IReadOnlyList<LevelUpRuleDef>? rules) =>
        LevelUpRules.SpecializationSlots(rules ?? Array.Empty<LevelUpRuleDef>(), playerLevel);

    public static int OwnedCount(IPlayerProgress progress, ISkillRegistry registry)
    {
        int count = 0;
        foreach (SkillDef skill in registry.All)
        {
            if (skill.Tree == null)
            {
                continue;
            }

            foreach (SkillTreeNodeDef node in skill.Tree.Nodes)
            {
                if (node.IsSpecialization && progress.GetUnlockTier(skill.Id, node.Id) > 0)
                {
                    count++;
                }
            }
        }

        return count;
    }

    public static bool HasAvailableSlot(
        IPlayerProgress progress,
        ISkillRegistry registry,
        IReadOnlyList<LevelUpRuleDef>? levelUpRules) =>
        OwnedCount(progress, registry) < AllowedSlots(progress.PlayerLevel, levelUpRules);
}
