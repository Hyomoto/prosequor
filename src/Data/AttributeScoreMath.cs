namespace Prosequor.Data;

/// <summary>
/// Effective attribute score = clamp(baseline + growth delta).
/// Schema ≥ 6 stores deltas in <see cref="PlayerProgressState.Attributes"/>.
/// </summary>
public static class AttributeScoreMath
{
    public static int Effective(int baseline, int delta) =>
        Math.Clamp(baseline + delta, 0, AttributeGrowth.MaxScore);

    public static int DeltaFromEffective(int baseline, int effective) =>
        Math.Clamp(effective, 0, AttributeGrowth.MaxScore) - baseline;

    public static int ReadDelta(
        PlayerProgressState state,
        string id,
        IReadOnlyList<string>? catalog = null)
    {
        string? canonical = AttributeIds.Canonicalize(id, catalog ?? AttributeIds.All);
        if (canonical == null)
        {
            return 0;
        }

        return state.Attributes.TryGetValue(canonical, out int delta) ? delta : 0;
    }

    /// <summary>
    /// Convert absolute scores to growth deltas using <paramref name="baselines"/>.
    /// Missing baseline keys use <see cref="AttributeGrowth.DefaultScore"/>.
    /// </summary>
    public static void ConvertAbsoluteToDeltas(
        PlayerProgressState state,
        IReadOnlyDictionary<string, int>? baselines,
        IReadOnlyList<string>? catalog = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        IReadOnlyList<string> ids = catalog ?? AttributeIds.All;
        foreach (string id in ids)
        {
            int absolute = state.Attributes.TryGetValue(id, out int stored)
                ? stored
                : AttributeGrowth.DefaultScore;
            int baseline = baselines != null && baselines.TryGetValue(id, out int b)
                ? b
                : AttributeGrowth.DefaultScore;
            state.Attributes[id] = absolute - baseline;
        }

        foreach (string id in ids)
        {
            if (!state.AttributeBuckets.ContainsKey(id))
            {
                state.AttributeBuckets[id] = 0f;
            }
        }

        state.Schema = PlayerProgressState.CurrentSchema;
    }

    public static bool NeedsAbsoluteToDeltaMigration(PlayerProgressState state) =>
        state.Schema > 0 && state.Schema < PlayerProgressState.AttributeDeltaSchema;
}
