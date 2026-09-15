namespace Prosequor.Client;

/// <summary>
/// Qualitative bands for a blended multiplier (or any ratio around a known average).
/// Neutral is omitted. Floor and ceiling clamp to Great. Shared so later skill
/// "current bonus" UI can use the same 1/3–2/3 voice without a one-off scale.
/// </summary>
public static class QualitativeStatScale
{
    public const float DefaultFloor = 0.4f;
    public const float DefaultNeutral = 1f;
    public const float DefaultCeiling = 2f;

    const float Epsilon = 0.0001f;

    public readonly record struct Band(bool Positive, QualitativeStatTier Tier);

    /// <summary>
    /// Classify <paramref name="value"/>. Returns false at neutral (caller omits the line).
    /// Non-finite values are omitted. Values outside floor/ceiling still classify as Great.
    /// </summary>
    public static bool TryClassify(
        float value,
        bool higherIsBetter,
        out Band band,
        float floor = DefaultFloor,
        float neutral = DefaultNeutral,
        float ceiling = DefaultCeiling)
    {
        band = default;
        if (!float.IsFinite(value) || !float.IsFinite(floor) || !float.IsFinite(neutral) || !float.IsFinite(ceiling))
        {
            return false;
        }

        if (NearlyEqual(value, neutral))
        {
            return false;
        }

        bool above = value > neutral;
        bool positive = higherIsBetter ? above : !above;
        float clamped = Math.Clamp(value, floor, ceiling);
        band = new Band(positive, ResolveTier(clamped, neutral, floor, ceiling));
        return true;
    }

    static QualitativeStatTier ResolveTier(float clamped, float neutral, float floor, float ceiling)
    {
        float span;
        float distance;
        if (clamped >= neutral)
        {
            span = ceiling - neutral;
            distance = clamped - neutral;
        }
        else
        {
            span = neutral - floor;
            distance = neutral - clamped;
        }

        if (span < Epsilon)
        {
            return QualitativeStatTier.Great;
        }

        float t = Math.Clamp(distance / span, 0f, 1f);
        if (t < 1f / 3f)
        {
            return QualitativeStatTier.Slight;
        }

        if (t < 2f / 3f)
        {
            return QualitativeStatTier.Moderate;
        }

        return QualitativeStatTier.Great;
    }

    static bool NearlyEqual(float a, float b) => Math.Abs(a - b) < Epsilon;
}

public enum QualitativeStatTier
{
    Slight,
    Moderate,
    Great
}
