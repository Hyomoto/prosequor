using Newtonsoft.Json.Linq;

namespace Prosequor.Ability;

/// <summary>Pure percent-point formulas used by declarative ability effects.</summary>
public static class AbilityFormulas
{
    /// <summary>
    /// Yield bonus as a fractional multiplier addition:
    /// min(cap, base + perLevel * skillLevel) / 100.
    /// </summary>
    public static float YieldBonusFraction(float basePercent, float perSkillLevelPercent, float capPercent, int skillLevel)
    {
        float points = basePercent + perSkillLevelPercent * Math.Max(0, skillLevel);
        if (capPercent > 0f)
        {
            points = Math.Min(capPercent, points);
        }

        return Math.Max(0f, points) * 0.01f;
    }

    /// <summary>Chance percent (0-100) to a unit interval probability.</summary>
    public static float ChanceFraction(float chancePercent) =>
        Math.Clamp(chancePercent, 0f, 100f) * 0.01f;

    /// <summary>
    /// Linear map of <paramref name="score"/> from [fromScore, toScore] onto [fromValue, toValue],
    /// then round with the named mode (<c>ceil</c>, <c>floor</c>, or <c>round</c>).
    /// Scores outside the range are clamped to the endpoints.
    /// When <paramref name="midScore"/> and <paramref name="midValue"/> are both set, lerps in two
    /// segments hinged at the midpoint (score ≤ mid uses from→mid; otherwise mid→to).
    /// </summary>
    public static int AttributeMappedInt(
        int score,
        int fromScore,
        float fromValue,
        int toScore,
        float toValue,
        string roundMode,
        int? midScore = null,
        float? midValue = null)
    {
        float raw = AttributeMappedRaw(
            score,
            fromScore,
            fromValue,
            toScore,
            toValue,
            midScore,
            midValue);
        return roundMode.ToLowerInvariant() switch
        {
            "floor" => (int)Math.Floor(raw),
            "round" => (int)Math.Round(raw, MidpointRounding.AwayFromZero),
            _ => (int)Math.Ceiling(raw)
        };
    }

    /// <summary>
    /// Linear (or hinged) map of <paramref name="score"/> onto float values. No rounding.
    /// </summary>
    public static float AttributeMappedFloat(
        int score,
        int fromScore,
        float fromValue,
        int toScore,
        float toValue,
        int? midScore = null,
        float? midValue = null) =>
        AttributeMappedRaw(
            score,
            fromScore,
            fromValue,
            toScore,
            toValue,
            midScore,
            midValue);

    static float AttributeMappedRaw(
        int score,
        int fromScore,
        float fromValue,
        int toScore,
        float toValue,
        int? midScore,
        float? midValue)
    {
        if (midScore is int midS && midValue is float midV)
        {
            if (score <= midS)
            {
                return LerpSegment(score, fromScore, fromValue, midS, midV);
            }

            return LerpSegment(score, midS, midV, toScore, toValue);
        }

        return LerpSegment(score, fromScore, fromValue, toScore, toValue);
    }

    static float LerpSegment(int score, int fromScore, float fromValue, int toScore, float toValue)
    {
        if (toScore == fromScore)
        {
            return fromValue;
        }

        float t = (score - fromScore) / (float)(toScore - fromScore);
        t = Math.Clamp(t, 0f, 1f);
        return fromValue + t * (toValue - fromValue);
    }

    /// <summary>
    /// Score → value over a <paramref name="from"/> span and <paramref name="to"/> list.
    /// Below the span → 0; above → last <paramref name="to"/> entry.
    /// <paramref name="curve"/> <c>linear</c> (default): even knots through every entry.
    /// <c>ease</c> / <c>bezier</c>: Bernstein with first/last values repeated (smoothstep for two entries).
    /// </summary>
    public static float AttributeCurveFloat(
        int score,
        int fromLow,
        int fromHigh,
        IReadOnlyList<float> to,
        string curve)
    {
        if (to == null || to.Count < 2)
        {
            return 0f;
        }

        if (score < fromLow)
        {
            return 0f;
        }

        if (score > fromHigh)
        {
            return to[to.Count - 1];
        }

        if (fromHigh == fromLow)
        {
            return to[0];
        }

        if (IsEaseCurve(curve))
        {
            float t = (score - fromLow) / (float)(fromHigh - fromLow);
            return BernsteinWithRepeatedEnds(to, t);
        }

        return Xp.AmountTableMath.LerpAmount(to, score, fromLow, fromHigh);
    }

    /// <summary>Same as <see cref="AttributeCurveFloat"/> then named round.</summary>
    public static int AttributeCurveInt(
        int score,
        int fromLow,
        int fromHigh,
        IReadOnlyList<float> to,
        string curve,
        string roundMode)
    {
        float raw = AttributeCurveFloat(score, fromLow, fromHigh, to, curve);
        return roundMode.ToLowerInvariant() switch
        {
            "floor" => (int)Math.Floor(raw),
            "round" => (int)Math.Round(raw, MidpointRounding.AwayFromZero),
            _ => (int)Math.Ceiling(raw)
        };
    }

    static bool IsEaseCurve(string? curve) =>
        string.Equals(curve, "ease", StringComparison.OrdinalIgnoreCase)
        || string.Equals(curve, "bezier", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Bernstein polynomial after repeating the first and last authored values.
    /// Two entries → cubic smoothstep controls.
    /// </summary>
    public static float BernsteinWithRepeatedEnds(IReadOnlyList<float> authored, float t)
    {
        if (authored == null || authored.Count == 0)
        {
            return 0f;
        }

        if (authored.Count == 1)
        {
            return authored[0];
        }

        t = Math.Clamp(t, 0f, 1f);
        int nAuthored = authored.Count;
        // Controls: first, first, …middle…, last, last → length nAuthored + 2
        int n = nAuthored + 1; // degree
        float[] controls = new float[nAuthored + 2];
        controls[0] = authored[0];
        controls[1] = authored[0];
        for (int i = 1; i < nAuthored - 1; i++)
        {
            controls[i + 1] = authored[i];
        }

        controls[nAuthored] = authored[nAuthored - 1];
        controls[nAuthored + 1] = authored[nAuthored - 1];

        float sum = 0f;
        float oneMinus = 1f - t;
        for (int i = 0; i <= n; i++)
        {
            sum += Binomial(n, i) * Pow(oneMinus, n - i) * Pow(t, i) * controls[i];
        }

        return sum;
    }

    static float Binomial(int n, int k)
    {
        if (k < 0 || k > n)
        {
            return 0f;
        }

        if (k == 0 || k == n)
        {
            return 1f;
        }

        if (k > n - k)
        {
            k = n - k;
        }

        long result = 1;
        for (int i = 1; i <= k; i++)
        {
            result = result * (n - k + i) / i;
        }

        return result;
    }

    static float Pow(float baseValue, int exp)
    {
        if (exp == 0)
        {
            return 1f;
        }

        float result = 1f;
        for (int i = 0; i < exp; i++)
        {
            result *= baseValue;
        }

        return result;
    }

    /// <summary>Rounds expected quantity with a fractional chance of +1.</summary>
    public static int StochasticRound(double expected, Random rand)
    {
        if (expected <= 0)
        {
            return 0;
        }

        int whole = (int)Math.Floor(expected);
        if (rand.NextDouble() < expected - whole)
        {
            whole++;
        }

        return whole;
    }

    /// <summary>
    /// Inclusive integer range from JSON: omit/null → defaults, number/<c>"2"</c> → fixed,
    /// <c>"2-5"</c> → inclusive min-max. Both ends must be &gt;= <paramref name="minAllowed"/>.
    /// </summary>
    /// <param name="specified">True when the token was present and non-empty.</param>
    public static bool TryParseIntRange(
        JToken? token,
        int defaultMin,
        int defaultMax,
        int minAllowed,
        string fieldName,
        out int min,
        out int max,
        out bool specified,
        out string error)
    {
        min = defaultMin;
        max = defaultMax;
        specified = false;
        error = "";

        if (token == null || token.Type == JTokenType.Null || token.Type == JTokenType.Undefined)
        {
            return true;
        }

        if (token.Type is JTokenType.Integer or JTokenType.Float)
        {
            int n = token.Value<int>();
            if (n < minAllowed)
            {
                error = $"{fieldName} must be >= {minAllowed}.";
                return false;
            }

            specified = true;
            min = max = n;
            return true;
        }

        if (token.Type != JTokenType.String)
        {
            error = $"{fieldName} must be a number or range string (e.g. 2 or \"2-5\").";
            return false;
        }

        string text = (token.Value<string>() ?? "").Trim();
        if (text.Length == 0)
        {
            return true;
        }

        specified = true;
        int dash = text.IndexOf('-');
        if (dash < 0)
        {
            if (!int.TryParse(text, out int n) || n < minAllowed)
            {
                error = $"{fieldName} must be an integer >= {minAllowed}.";
                return false;
            }

            min = max = n;
            return true;
        }

        string left = text[..dash].Trim();
        string right = text[(dash + 1)..].Trim();
        if (!int.TryParse(left, out int lo)
            || !int.TryParse(right, out int hi)
            || lo < minAllowed
            || hi < lo)
        {
            error = $"{fieldName} range must be \"min-max\" with {minAllowed} <= min <= max.";
            return false;
        }

        min = lo;
        max = hi;
        return true;
    }

    /// <summary>
    /// Stack quantity shorthand: omit → 1, number/<c>"2"</c> → fixed, <c>"2-5"</c> → inclusive.
    /// </summary>
    public static bool TryParseQuantityRange(JToken? token, out int min, out int max, out string error) =>
        TryParseIntRange(token, 1, 1, 1, "quantity", out min, out max, out _, out error);

    /// <summary>Same as <see cref="TryParseQuantityRange"/> but reports whether the token was set.</summary>
    public static bool TryParseQuantityRange(
        JToken? token,
        out int min,
        out int max,
        out bool specified,
        out string error) =>
        TryParseIntRange(token, 1, 1, 1, "quantity", out min, out max, out specified, out error);

    public static int RollIntRange(int min, int max, Random rand)
    {
        if (min >= max)
        {
            return Math.Max(0, min);
        }

        return rand.Next(min, max + 1);
    }

    public static int RollQuantityRange(int min, int max, Random rand) => RollIntRange(min, max, rand);
}
