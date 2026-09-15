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
    /// <see cref="SkillLevelsPerPoint"/> (e.g. 0→40 with interval 20 awards 2).
    /// </summary>
    public static int PointsForSkillLevelGain(int before, int after)
    {
        if (after <= before || SkillLevelsPerPoint <= 0)
        {
            return 0;
        }

        return after / SkillLevelsPerPoint - before / SkillLevelsPerPoint;
    }
}
