namespace Prosequor.Progress;

/// <summary>
/// Unlock-point awards from skill level milestones: one point every
/// <see cref="SkillLevelsPerPoint"/> skill levels gained.
/// </summary>
public static class UnlockPointPolicy
{
    public const int SkillLevelsPerPoint = 20;

    /// <summary>
    /// Unlock points granted when a skill rises from <paramref name="before"/> to
    /// <paramref name="after"/>. Counts each crossed multiple of
    /// <paramref name="levelsPerPoint"/> (e.g. 0→40 with interval 20 awards 2).
    /// </summary>
    public static int PointsForSkillLevelGain(int before, int after, int levelsPerPoint = SkillLevelsPerPoint)
    {
        if (after <= before || levelsPerPoint <= 0)
        {
            return 0;
        }

        return after / levelsPerPoint - before / levelsPerPoint;
    }
}
