using Prosequor.Data;

namespace Prosequor.Xp;

/// <summary>Bucket-aware XP award / drain orchestration on <see cref="PlayerProgressState"/>.</summary>
public static class XpAwardService
{
    public static void ApplyDrain(PlayerProgressState state, double nowHours)
    {
        float playerFill = state.PlayerFill;
        double playerDrain = state.PlayerLastDrainTotalHours;
        XpBucketFormulas.DrainMeter(
            ref playerFill,
            state.PlayerLastAccrualTotalHours,
            ref playerDrain,
            nowHours);
        state.PlayerFill = playerFill;
        state.PlayerLastDrainTotalHours = playerDrain;

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
    /// Skill award: drain → mult from skill+player sat → accrue/flush → fill both meters with raw.
    /// Player grace refreshes only when the award earns (or both meters are still empty of sat).
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

        if (state.PlayerCachedCap <= 0f)
        {
            XpBucketFormulas.RefreshPlayerCap(state);
        }

        float skillAccrued = skill.Accrued;
        float skillFill = skill.Fill;
        double skillAccrual = skill.LastAccrualTotalHours;
        float playerFill = state.PlayerFill;
        int skillSat = XpBucketFormulas.SatLevel(skillFill, skill.CachedCap);
        int playerSat = XpBucketFormulas.SatLevel(playerFill, state.PlayerCachedCap);
        float granted = XpBucketFormulas.AccrueSkillAcrossSatTiers(
            ref skillAccrued,
            ref skillFill,
            ref skillAccrual,
            ref playerFill,
            skill.CachedCap,
            state.PlayerCachedCap,
            raw,
            nowHours);
        skill.Accrued = skillAccrued;
        skill.Fill = skillFill;
        skill.LastAccrualTotalHours = skillAccrual;
        state.PlayerFill = playerFill;
        if (XpBucketFormulas.RefreshesPlayerGrace(granted, skillSat, playerSat))
        {
            state.PlayerLastAccrualTotalHours = nowHours;
        }

        return granted;
    }

    /// <summary>
    /// Player-only award: drain → mult from player sat only → accrue/flush → fill player meter.
    /// Player grace refreshes only when the award earns (or the meter is still unsaturated).
    /// Returns lifetime player XP to commit (0 if held).
    /// </summary>
    public static float AwardPlayer(PlayerProgressState state, float raw, double nowHours)
    {
        if (raw <= 0f)
        {
            return 0f;
        }

        ApplyDrain(state, nowHours);

        if (state.PlayerCachedCap <= 0f)
        {
            XpBucketFormulas.RefreshPlayerCap(state);
        }

        float accrued = state.PlayerAccrued;
        float fill = state.PlayerFill;
        double accrual = state.PlayerLastAccrualTotalHours;
        int playerSat = XpBucketFormulas.SatLevel(fill, state.PlayerCachedCap);
        float granted = XpBucketFormulas.AccrueAcrossSatTiers(
            ref accrued,
            ref fill,
            ref accrual,
            state.PlayerCachedCap,
            raw,
            nowHours,
            playerSatOnly: true);
        state.PlayerAccrued = accrued;
        state.PlayerFill = fill;
        if (XpBucketFormulas.RefreshesPlayerGrace(granted, skillSat: 0, playerSat))
        {
            state.PlayerLastAccrualTotalHours = accrual;
        }

        return granted;
    }

    /// <summary>
    /// GrantAndFill: bump skill + player fill by <paramref name="raw"/> without accruing or sat-scaling.
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
        state.PlayerFill += raw;
        state.PlayerLastAccrualTotalHours = nowHours;
    }

    /// <summary>
    /// GrantAndFill (player track): bump player fill by <paramref name="raw"/> without accruing or sat-scaling.
    /// </summary>
    public static void FillPlayerMeter(PlayerProgressState state, float raw, double nowHours)
    {
        if (raw <= 0f)
        {
            return;
        }

        ApplyDrain(state, nowHours);
        state.PlayerFill += raw;
        state.PlayerLastAccrualTotalHours = nowHours;
    }
}
