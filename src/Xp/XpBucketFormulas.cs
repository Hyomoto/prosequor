using Prosequor.Data;

namespace Prosequor.Xp;

/// <summary>Pure dual-bucket capacity / saturation / drain formulas.</summary>
public static class XpBucketFormulas
{
    public const float MinAward = 0.1f;
    public const float DrainPerGameSecond = 0.01f;
    public const double DrainGraceHours = 1.0;
    public const int MaxSatExponent = 20;
    public const double GameSecondsPerHour = 3600.0;

    /// <summary>Base skill bucket capacity. Optional multiplier reserved for future profession/nodes.</summary>
    public static float SkillCap(int skillLevel, float multiplier = 1f)
    {
        float baseCap = 25f + 2f * Math.Max(0, skillLevel);
        return Math.Max(1f, baseCap * Math.Max(0f, multiplier));
    }

    /// <summary>Base player bucket capacity. Optional multiplier reserved for future buffs.</summary>
    public static float PlayerCap(int playerLevel, float multiplier = 1f)
    {
        float baseCap = 50f * Math.Max(XpCurves.PlayerMinLevel, playerLevel);
        return Math.Max(1f, baseCap * Math.Max(0f, multiplier));
    }

    public static int SatLevel(float fill, float cachedCap)
    {
        if (cachedCap <= 0f || fill <= 0f)
        {
            return 0;
        }

        int level = (int)Math.Floor(fill / cachedCap);
        return Math.Clamp(level, 0, MaxSatExponent);
    }

    public static float Multiplier(int skillSat, int playerSat)
    {
        int exp = Math.Clamp(skillSat + playerSat, 0, MaxSatExponent);
        return exp == 0 ? 1f : MathF.Pow(2f, -exp);
    }

    public static float Multiplier(float skillFill, float skillCap, float playerFill, float playerCap) =>
        Multiplier(SatLevel(skillFill, skillCap), SatLevel(playerFill, playerCap));

    /// <summary>
    /// Player grace refreshes only when this award actually earns, or when both meters are
    /// still unsaturated (MinAward holds on a fresh meter). Compressed dust ticks do not.
    /// </summary>
    public static bool RefreshesPlayerGrace(float granted, int skillSat, int playerSat) =>
        granted > 0f || (skillSat <= 0 && playerSat <= 0);

    public static void RefreshSkillCap(SkillProgressState skill, float multiplier = 1f) =>
        skill.CachedCap = SkillCap(skill.Level, multiplier);

    public static void RefreshPlayerCap(PlayerProgressState state, float multiplier = 1f) =>
        state.PlayerCachedCap = PlayerCap(state.PlayerLevel, multiplier);

    public static void RefreshAllCaps(PlayerProgressState state)
    {
        RefreshPlayerCap(state);
        foreach (SkillProgressState skill in state.Skills.Values)
        {
            RefreshSkillCap(skill);
        }
    }

    /// <summary>
    /// Drains <paramref name="fill"/> using calendar time after the idle grace past last accrual.
    /// Updates <paramref name="lastDrainTotalHours"/> to <paramref name="nowHours"/>.
    /// </summary>
    public static void DrainMeter(
        ref float fill,
        double lastAccrualTotalHours,
        ref double lastDrainTotalHours,
        double nowHours)
    {
        if (nowHours < lastDrainTotalHours)
        {
            // Clock went backwards; resync without draining.
            lastDrainTotalHours = nowHours;
            return;
        }

        double drainStart = lastAccrualTotalHours + DrainGraceHours;
        if (nowHours < drainStart)
        {
            lastDrainTotalHours = nowHours;
            return;
        }

        double from = Math.Max(lastDrainTotalHours, drainStart);
        if (nowHours <= from)
        {
            lastDrainTotalHours = nowHours;
            return;
        }

        double gameSeconds = (nowHours - from) * GameSecondsPerHour;
        fill = Math.Max(0f, fill - (float)(DrainPerGameSecond * gameSeconds));
        lastDrainTotalHours = nowHours;
    }

    /// <summary>
    /// Accrue raw XP after multiplier. Returns amount to commit when accrued crosses MinAward (0 if held).
    /// Always adds <paramref name="raw"/> into fill when raw &gt; 0.
    /// </summary>
    public static float AccrueAndMaybeFlush(
        ref float accrued,
        ref float fill,
        ref double lastAccrualTotalHours,
        float raw,
        float multiplier,
        double nowHours)
    {
        if (raw <= 0f)
        {
            return 0f;
        }

        accrued += raw * multiplier;
        fill += raw;
        lastAccrualTotalHours = nowHours;

        if (accrued < MinAward)
        {
            return 0f;
        }

        float granted = accrued;
        accrued = 0f;
        return granted;
    }

    /// <summary>Raw activity until the next saturation tier boundary (or unbounded at max sat).</summary>
    public static float RoomToNextSatLevel(float fill, float cachedCap)
    {
        if (cachedCap <= 0f)
        {
            return float.MaxValue;
        }

        int sat = SatLevel(fill, cachedCap);
        if (sat >= MaxSatExponent)
        {
            return float.MaxValue;
        }

        float nextBoundary = (sat + 1) * cachedCap;
        float room = nextBoundary - fill;
        return room > 0f ? room : float.MaxValue;
    }

    /// <summary>
    /// Accrues <paramref name="raw"/> in saturation-tier chunks so multiplier changes when fill crosses a cap.
    /// Returns total flushed lifetime XP for this award (0 if held in <paramref name="accrued"/>).
    /// </summary>
    public static float AccrueAcrossSatTiers(
        ref float accrued,
        ref float fill,
        ref double lastAccrualTotalHours,
        float cachedCap,
        float raw,
        double nowHours,
        bool playerSatOnly)
    {
        if (raw <= 0f)
        {
            return 0f;
        }

        float totalGranted = 0f;
        float remaining = raw;
        while (remaining > 0f)
        {
            int sat = SatLevel(fill, cachedCap);
            float mult = playerSatOnly ? Multiplier(0, sat) : Multiplier(sat, 0);
            float room = RoomToNextSatLevel(fill, cachedCap);
            float chunk = Math.Min(remaining, room);
            if (chunk <= 0f)
            {
                chunk = remaining;
            }

            totalGranted += AccrueAndMaybeFlush(
                ref accrued,
                ref fill,
                ref lastAccrualTotalHours,
                chunk,
                mult,
                nowHours);
            remaining -= chunk;
        }

        return totalGranted;
    }

    /// <summary>
    /// Skill accrual with dual-bucket saturation: both meters advance by the same raw chunk per tier.
    /// Player fill is updated here; caller should set player last-accrual only when
    /// <see cref="RefreshesPlayerGrace"/> is true.
    /// </summary>
    public static float AccrueSkillAcrossSatTiers(
        ref float skillAccrued,
        ref float skillFill,
        ref double skillLastAccrualTotalHours,
        ref float playerFill,
        float skillCap,
        float playerCap,
        float raw,
        double nowHours)
    {
        if (raw <= 0f)
        {
            return 0f;
        }

        float totalGranted = 0f;
        float remaining = raw;
        while (remaining > 0f)
        {
            int skillSat = SatLevel(skillFill, skillCap);
            int playerSat = SatLevel(playerFill, playerCap);
            float mult = Multiplier(skillSat, playerSat);
            float skillRoom = RoomToNextSatLevel(skillFill, skillCap);
            float playerRoom = RoomToNextSatLevel(playerFill, playerCap);
            float chunk = Math.Min(remaining, Math.Min(skillRoom, playerRoom));
            if (chunk <= 0f)
            {
                chunk = remaining;
            }

            totalGranted += AccrueAndMaybeFlush(
                ref skillAccrued,
                ref skillFill,
                ref skillLastAccrualTotalHours,
                chunk,
                mult,
                nowHours);
            playerFill += chunk;
            remaining -= chunk;
        }

        return totalGranted;
    }
}
