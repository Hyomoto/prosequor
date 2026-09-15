namespace Prosequor.Data;

/// <summary>
/// Player 1–50 and skill XP curves. Skill level caps are derived from
/// <see cref="SkillKind"/> (hobby 20 / minor and passive 50 / specialization 100).
/// The curve formula itself still uses <see cref="SkillMaxLevel"/> as its plateau.
/// </summary>
public static class XpCurves
{
    public const int PlayerMinLevel = 1;
    public const int PlayerMaxLevel = 50;
    public const int SkillMinLevel = 0;
    public const int HobbyMaxLevel = 20;
    public const int MinorMaxLevel = 50;
    public const int SkillMaxLevel = 100;

    /// <summary>XP needed to go from <paramref name="level"/> to level+1. 0 at cap.</summary>
    public static int XpToNextPlayerLevel(int level)
    {
        if (level < PlayerMinLevel || level >= PlayerMaxLevel)
        {
            return 0;
        }

        int L = Math.Min(level, 17);
        int B = (L == 1 || L == 17 || (L > 22 && L % 2 == 1)) ? 1 : 0;
        return 39 * (L * L) + B;
    }

    /// <summary>XP needed to go from <paramref name="level"/> to level+1. 0 at the curve plateau.</summary>
    public static int XpToNextSkillLevel(int level) =>
        XpToNextSkillLevel(level, SkillMaxLevel);

    /// <summary>
    /// XP needed to go from <paramref name="level"/> to level+1. 0 at
    /// <paramref name="maxLevel"/> (hobby 20 / minor and passive 50 / specialization 100).
    /// </summary>
    public static int XpToNextSkillLevel(int level, int maxLevel)
    {
        int cap = maxLevel <= 0 ? SkillMaxLevel : Math.Min(maxLevel, SkillMaxLevel);
        if (level < SkillMinLevel || level >= cap)
        {
            return 0;
        }

        int L = Math.Min(level, 40);
        return 15 + (L * L) / 3;
    }

    /// <summary>
    /// Stat-bar values for a skill. At the skill's cap the bar is a flat full fill (1 / 1).
    /// </summary>
    public static void SkillBar(float lifetimeXp, int level, int maxLevel, out float intoLevel, out int needForNext)
    {
        needForNext = XpToNextSkillLevel(level, maxLevel);
        if (needForNext <= 0)
        {
            intoLevel = 1f;
            needForNext = 1;
            return;
        }

        intoLevel = InLevelSkillXp(lifetimeXp, level);
        if (intoLevel > needForNext)
        {
            intoLevel = needForNext;
        }
    }

    /// <summary>Lifetime XP required to be at exactly <paramref name="level"/> (start of that level).</summary>
    public static float LifetimeXpForPlayerLevel(int level)
    {
        level = Math.Clamp(level, PlayerMinLevel, PlayerMaxLevel);
        float sum = 0f;
        for (int i = PlayerMinLevel; i < level; i++)
        {
            sum += XpToNextPlayerLevel(i);
        }

        return sum;
    }

    /// <summary>Lifetime XP required to be at exactly <paramref name="level"/> (start of that level).</summary>
    public static float LifetimeXpForSkillLevel(int level)
    {
        level = Math.Clamp(level, SkillMinLevel, SkillMaxLevel);
        float sum = 0f;
        for (int i = SkillMinLevel; i < level; i++)
        {
            sum += XpToNextSkillLevel(i);
        }

        return sum;
    }

    public static int PlayerLevelFromLifetimeXp(float lifetimeXp)
    {
        if (lifetimeXp <= 0f)
        {
            return PlayerMinLevel;
        }

        int level = PlayerMinLevel;
        float remaining = lifetimeXp;
        while (level < PlayerMaxLevel)
        {
            int need = XpToNextPlayerLevel(level);
            if (remaining < need)
            {
                break;
            }

            remaining -= need;
            level++;
        }

        return level;
    }

    public static int SkillLevelFromLifetimeXp(float lifetimeXp)
    {
        if (lifetimeXp <= 0f)
        {
            return SkillMinLevel;
        }

        int level = SkillMinLevel;
        float remaining = lifetimeXp;
        while (level < SkillMaxLevel)
        {
            int need = XpToNextSkillLevel(level);
            if (remaining < need)
            {
                break;
            }

            remaining -= need;
            level++;
        }

        return level;
    }

    public static float InLevelPlayerXp(float lifetimeXp, int level)
    {
        return Math.Max(0f, lifetimeXp - LifetimeXpForPlayerLevel(level));
    }

    public static float InLevelSkillXp(float lifetimeXp, int level)
    {
        return Math.Max(0f, lifetimeXp - LifetimeXpForSkillLevel(level));
    }

    public static float XpUntilNextPlayerLevel(float lifetimeXp, int level)
    {
        int need = XpToNextPlayerLevel(level);
        if (need <= 0)
        {
            return 0f;
        }

        float into = InLevelPlayerXp(lifetimeXp, level);
        return Math.Max(0f, need - into);
    }

    public static float XpUntilNextSkillLevel(float lifetimeXp, int level)
    {
        int need = XpToNextSkillLevel(level);
        if (need <= 0)
        {
            return 0f;
        }

        float into = InLevelSkillXp(lifetimeXp, level);
        return Math.Max(0f, need - into);
    }

    /// <summary>
    /// Player XP granted when a skill rises from <paramref name="before"/> to <paramref name="after"/>.
    /// Each completed skill tier contributes <see cref="XpToNextSkillLevel"/> for that tier
    /// (e.g. 0→3 awards 15+15+16).
    /// </summary>
    public static int PlayerXpForSkillLevelGain(int before, int after)
    {
        if (after <= before)
        {
            return 0;
        }

        int sum = 0;
        for (int level = before; level < after; level++)
        {
            sum += XpToNextSkillLevel(level);
        }

        return sum;
    }
}
