using Prosequor.Xp;

namespace Prosequor.Ability;

/// <summary>
/// Pure craft-quality kernel: roll window → integer ranks → attribute lerp / affix band.
/// </summary>
public static class QualityMath
{
    /// <summary>Added to owning skill level for the <c>quality-base</c> fold seed.</summary>
    public const float BaseSeedOffset = -200f;

    /// <summary>Default <c>quality-window</c> fold seed.</summary>
    public const float WindowSeed = 200f;

    /// <summary>Default <c>quality-rolls</c> fold seed.</summary>
    public const float RollsSeed = 1f;

    /// <summary>Default <c>quality-bonus</c> fold seed.</summary>
    public const float BonusSeed = 0f;

    /// <summary>Inclusive floor after roll + bonuses before dividing into ranks.</summary>
    public const float RawMin = 0f;

    /// <summary>Inclusive ceiling after roll + bonuses before dividing into ranks.</summary>
    public const float RawMax = 300f;

    /// <summary>Integer divisor that collapses raw quality into ranks.</summary>
    public const float RankDivisor = 10f;

    /// <summary>Inclusive floor of the rank domain used for lerp / affix banding.</summary>
    public const float RankMin = 0f;

    /// <summary>Inclusive ceiling of the rank domain (300 / 10).</summary>
    public const float RankMax = 30f;

    /// <summary>
    /// Sample <paramref name="rolls"/> times in <c>[base, base + window]</c> and return the max.
    /// Degenerate window (&lt;= 0) yields <paramref name="qualityBase"/> with no spread.
    /// </summary>
    public static float RollRaw(float qualityBase, float qualityWindow, int rolls, Random rand)
    {
        int n = Math.Max(1, rolls);
        if (qualityWindow <= 0f || rand == null)
        {
            return qualityBase;
        }

        float best = qualityBase;
        for (int i = 0; i < n; i++)
        {
            float sample = qualityBase + (float)(rand.NextDouble() * qualityWindow);
            if (sample > best)
            {
                best = sample;
            }
        }

        return best;
    }

    /// <summary>
    /// Clamp <c>raw + ruleBonus + globalBonus</c> onto <c>[RawMin, RawMax]</c> (the mean used for grade banding).
    /// </summary>
    public static float ClampMean(float raw, float ruleBonus, float globalBonus)
    {
        float sum = raw + ruleBonus + globalBonus;
        if (sum < RawMin)
        {
            return RawMin;
        }

        if (sum > RawMax)
        {
            return RawMax;
        }

        return sum;
    }

    /// <summary>
    /// <c>floor(clamp(raw + ruleBonus + globalBonus, 0, 300) / 10)</c>.
    /// </summary>
    public static int ToPoints(float raw, float ruleBonus, float globalBonus) =>
        (int)Math.Floor(ClampMean(raw, ruleBonus, globalBonus) / RankDivisor);

    /// <summary>
    /// Band mean onto a quality-grade affix list: <c>mean / RawMax</c> → nearest index.
    /// </summary>
    public static int GradeIndex(int count, float mean) =>
        AmountTableMath.PickIndex(count, mean, RawMin, RawMax);

    /// <summary>
    /// Piecewise-linear interpolate <paramref name="table"/> across ranks
    /// <c>[0, RankMax]</c>. Same curve as XP measure grants.
    /// </summary>
    public static float LerpTable(IReadOnlyList<float> table, int points) =>
        AmountTableMath.LerpAmount(table, points, RankMin, RankMax);

    /// <summary>
    /// Nearest index of <paramref name="points"/> onto an affix list of
    /// <paramref name="count"/> entries over <c>[0, 30]</c>. Affix lists stay discrete;
    /// attribute and XP amount tables lerp.
    /// </summary>
    public static int AffixIndex(int count, int points) =>
        AmountTableMath.PickIndex(count, points, RankMin, RankMax);

    /// <summary>
    /// Band <paramref name="points"/> onto the inclusive slice
    /// <c>[affixFrom, affixTo]</c> of a list with <paramref name="count"/> entries.
    /// Degenerate / out-of-range bounds clamp to a valid non-empty slice.
    /// </summary>
    public static int AffixIndexInRange(int count, int points, int affixFrom, int affixTo)
    {
        if (count <= 0)
        {
            return 0;
        }

        int from = affixFrom;
        int to = affixTo;
        if (from > to)
        {
            (from, to) = (to, from);
        }

        if (from < 0)
        {
            from = 0;
        }

        if (to >= count)
        {
            to = count - 1;
        }

        if (from > to)
        {
            return 0;
        }

        int slice = to - from + 1;
        return from + AffixIndex(slice, points);
    }

    /// <summary>Owning-skill seed for the <c>quality-base</c> fold.</summary>
    public static float BaseSeed(int skillLevel) =>
        BaseSeedOffset + Math.Max(0, skillLevel);

    /// <summary>Whole rolls from a folded <c>quality-rolls</c> value.</summary>
    public static int ResolveRollCount(float qualityRolls) =>
        Math.Max(1, (int)Math.Round(qualityRolls, MidpointRounding.AwayFromZero));
}
