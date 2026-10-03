using Prosequor.Data;

namespace Prosequor.Progress;

/// <summary>
/// Specialization capacity from the progression table.
/// Owned specializations are counted across every registered skill tree.
/// </summary>
public static class SpecializationPolicy
{
    public static int AllowedSlots(int playerLevel, LevelSet levels) =>
        playerLevel < XpCurves.PlayerMinLevel ? 0 : levels.CountAtMost(playerLevel);

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
        LevelSet levels) =>
        OwnedCount(progress, registry) < AllowedSlots(progress.PlayerLevel, levels);
}
