using Prosequor.Data;

namespace Prosequor.Xp;

/// <summary>Bucket-aware XP award / drain orchestration on <see cref="PlayerProgressState"/>.</summary>
public static class XpAwardService
{
    public static void ApplyDrain(PlayerProgressState state, double nowHours)
    {
        foreach (SkillProgressState skill in state.Skills.Values)
        {
            float fill = skill.Fill;
            double drain = skill.LastDrainTotalHours;
            XpBucketFormulas.DrainMeter(
                ref fill,
                skill.LastAccrualTotalHours,
                ref drain,
                nowHours);
            skill.Fill = fill;
            skill.LastDrainTotalHours = drain;
        }
    }

    /// <summary>
    /// Skill award: drain → mult from skill sat → accrue/flush → fill skill meter with raw.
    /// Returns lifetime skill XP to commit (0 if held in accrued).
    /// </summary>
    public static float AwardSkill(PlayerProgressState state, string skillId, float raw, double nowHours)
    {
        if (raw <= 0f || string.IsNullOrWhiteSpace(skillId))
        {
            return 0f;
        }

        ApplyDrain(state, nowHours);

        SkillProgressState skill = state.GetOrCreateSkill(skillId);
        if (skill.CachedCap <= 0f)
        {
            XpBucketFormulas.RefreshSkillCap(skill);
        }

        float skillAccrued = skill.Accrued;
        float skillFill = skill.Fill;
        double skillAccrual = skill.LastAccrualTotalHours;
        float granted = XpBucketFormulas.AccrueAcrossSatTiers(
            ref skillAccrued,
            ref skillFill,
            ref skillAccrual,
            skill.CachedCap,
            raw,
            nowHours);
        skill.Accrued = skillAccrued;
        skill.Fill = skillFill;
        skill.LastAccrualTotalHours = skillAccrual;
        return granted;
    }

    /// <summary>
    /// GrantAndFill: bump skill fill by <paramref name="raw"/> without accruing or sat-scaling.
    /// Updates last-accrual so drain grace matches Earn.
    /// </summary>
    public static void FillSkillMeters(PlayerProgressState state, string skillId, float raw, double nowHours)
    {
        if (raw <= 0f || string.IsNullOrWhiteSpace(skillId))
        {
            return;
        }

        ApplyDrain(state, nowHours);

        SkillProgressState skill = state.GetOrCreateSkill(skillId);
        skill.Fill += raw;
        skill.LastAccrualTotalHours = nowHours;
    }
}
