namespace Prosequor.Xp;

/// <summary>
/// Shared amount-table lookup. Normalize a metric onto [min, max], then either snap to the
/// nearest knot (<see cref="PickIndex"/>, affix lists) or piecewise-lerp between knots
/// (<see cref="LerpAmount"/>, XP measure grants and quality attribute tables).
/// </summary>
public static class AmountTableMath
{
    /// <summary>Craft ingredient measure floor (inclusive).</summary>
    public const float IngredientsMin = 1f;

    /// <summary>Craft ingredient measure ceiling; values above clamp here.</summary>
    public const float IngredientsMax = 40f;

    /// <summary>
    /// Normalize <paramref name="value"/> onto [min, max], then pick nearest index among
    /// <paramref name="count"/> slots. Missing/degenerate → index 0.
    /// </summary>
    public static int PickIndex(int count, float value, float min, float max)
    {
        if (count <= 0)
        {
            return 0;
        }

        if (count == 1)
        {
            return 0;
        }

        if (value <= 0f || max <= min)
        {
            return 0;
        }

        float t = (value - min) / (max - min);
        if (t < 0f)
        {
            t = 0f;
        }
        else if (t > 1f)
        {
            t = 1f;
        }

        int idx = (int)Math.Round(t * (count - 1), MidpointRounding.AwayFromZero);
        if (idx < 0)
        {
            return 0;
        }

        if (idx >= count)
        {
            return count - 1;
        }

        return idx;
    }

    /// <summary>
    /// Map <paramref name="value"/> onto an amount table using catalog min/max.
    /// Normalize to [0,1], then pick nearest index among N values. Missing/degenerate → index 0.
    /// Affix lists. XP measure grants use <see cref="LerpAmount"/>.
    /// </summary>
    public static float PickAmount(
        IReadOnlyList<float> table,
        float value,
        float min,
        float max)
    {
        if (table == null || table.Count == 0)
        {
            return 0f;
        }

        int idx = PickIndex(table.Count, value, min, max);
        return table[idx];
    }

    /// <summary>
    /// Piecewise-linear interpolate <paramref name="table"/> across [min, max].
    /// Knot count is free (2+, or 1 = constant). Empty → 0. Values outside the domain
    /// clamp to the end knots. Degenerate domain (max &lt;= min) → first knot.
    /// The domain max always lands on the last knot.
    /// </summary>
    public static float LerpAmount(
        IReadOnlyList<float>? table,
        float value,
        float min,
        float max)
    {
        if (table == null || table.Count == 0)
        {
            return 0f;
        }

        if (table.Count == 1 || max <= min)
        {
            return table[0];
        }

        float p = value;
        if (p < min)
        {
            p = min;
        }
        else if (p > max)
        {
            p = max;
        }

        float t = (p - min) / (max - min);
        float scaled = t * (table.Count - 1);
        int lo = (int)Math.Floor(scaled);
        int hi = lo + 1;
        if (lo < 0)
        {
            lo = 0;
        }

        if (hi >= table.Count)
        {
            return table[table.Count - 1];
        }

        float frac = scaled - lo;
        return table[lo] + (table[hi] - table[lo]) * frac;
    }

    /// <summary>
    /// Scalar amount, or lerped table lookup when <paramref name="amountTable"/> is set.
    /// XP measure channels (resistance, voxels, ingredients, lifetime) use this path.
    /// </summary>
    public static float ResolveAmount(
        float scalarAmount,
        IReadOnlyList<float>? amountTable,
        float value,
        float min,
        float max)
    {
        if (amountTable != null && amountTable.Count > 0)
        {
            return LerpAmount(amountTable, value, min, max);
        }

        return scalarAmount > 0f ? scalarAmount : 0f;
    }
}
