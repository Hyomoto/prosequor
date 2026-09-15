namespace Prosequor.Data;

/// <summary>
/// Median-centered 0..1 tick positions for the stats-panel attribute buckets.
/// </summary>
public static class AttributeBucketAxis
{
    const float TieEpsilon = 1e-5f;

    /// <summary>
    /// One fraction per <see cref="AttributeIds.All"/> entry, in that order.
    /// Null means the attribute is at <see cref="AttributeGrowth.MaxScore"/> and out of the race.
    /// Center (0.5) is the median of eligible bucket fills; extremes are the current leader/lagger.
    /// </summary>
    public static float?[] TickFractions(ReadOnlySpan<int> scores, ReadOnlySpan<float> buckets)
    {
        float?[] dest = new float?[AttributeIds.All.Length];
        WriteTickFractions(scores, buckets, dest);
        return dest;
    }

    public static void WriteTickFractions(
        ReadOnlySpan<int> scores,
        ReadOnlySpan<float> buckets,
        float?[] dest)
    {
        ArgumentNullException.ThrowIfNull(dest);
        int n = AttributeIds.All.Length;
        if (scores.Length != n || buckets.Length != n || dest.Length < n)
        {
            throw new ArgumentException("[prosequor] Attribute bucket axis length mismatch.");
        }

        List<float> eligible = new(n);
        for (int i = 0; i < n; i++)
        {
            if (scores[i] < AttributeGrowth.MaxScore)
            {
                eligible.Add(buckets[i]);
            }
        }

        if (eligible.Count == 0)
        {
            for (int i = 0; i < n; i++)
            {
                dest[i] = null;
            }

            return;
        }

        float median = Median(eligible);
        float maxDev = 0f;
        for (int i = 0; i < eligible.Count; i++)
        {
            maxDev = Math.Max(maxDev, Math.Abs(eligible[i] - median));
        }

        for (int i = 0; i < n; i++)
        {
            if (scores[i] >= AttributeGrowth.MaxScore)
            {
                dest[i] = null;
                continue;
            }

            if (maxDev <= TieEpsilon)
            {
                dest[i] = 0.5f;
                continue;
            }

            dest[i] = Math.Clamp(0.5f + 0.5f * (buckets[i] - median) / maxDev, 0f, 1f);
        }
    }

    static float Median(List<float> values)
    {
        values.Sort();
        int count = values.Count;
        if ((count & 1) == 1)
        {
            return values[count / 2];
        }

        return (values[count / 2 - 1] + values[count / 2]) * 0.5f;
    }
}
