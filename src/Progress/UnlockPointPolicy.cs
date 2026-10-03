using Prosequor.Data;

namespace Prosequor.Progress;

/// <summary>
/// Unlock-point awards from skill levels listed on the progression table.
/// </summary>
public static class UnlockPointPolicy
{
    /// <summary>
    /// Unlock points granted when a skill rises from <paramref name="before"/> to
    /// <paramref name="after"/>. One point per listed level in (before, after].
    /// </summary>
    public static int PointsForSkillLevelGain(int before, int after, LevelSet levels)
    {
        if (after <= before)
        {
            return 0;
        }

        return levels.CountCrossed(before, after);
    }
}
