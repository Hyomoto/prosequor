namespace Prosequor.Data;

/// <summary>
/// Attribute score defaults and soft-reset bucket grant logic.
/// Pure helpers for runtime and in-process fixtures.
/// Schema ≥ 6: <see cref="PlayerProgressState.Attributes"/> holds growth deltas.
/// </summary>
public static class AttributeGrowth
{
    public const int DefaultScore = 10;
    public const int MaxScore = 18;

    /// <summary>
    /// Deposit skill <paramref name="scores"/> into attribute buckets, multiplied by
    /// <paramref name="levelsGained"/>. Call before player XP / attribute grants on skill level-up.
    /// </summary>
    public static void AddScores(
        PlayerProgressState state,
        IReadOnlyList<AttributeScoreEntry> scores,
        int levelsGained,
        IReadOnlyList<string>? catalog = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (levelsGained <= 0 || scores == null || scores.Count == 0)
        {
            return;
        }

        IReadOnlyList<string> ids = catalog ?? AttributeIds.All;
        PlayerProgressState.EnsureAttributeEntries(state, ids);
        foreach (AttributeScoreEntry entry in scores)
        {
            if (string.IsNullOrEmpty(entry.Id) || entry.Value == 0f)
            {
                continue;
            }

            float amount = entry.Value * levelsGained;
            if (amount == 0f)
            {
                continue;
            }

            if (!state.AttributeBuckets.TryGetValue(entry.Id, out float current))
            {
                current = 0f;
            }

            state.AttributeBuckets[entry.Id] = Math.Max(0f, current + amount);
        }
    }

    /// <summary>
    /// Among attributes below <see cref="MaxScore"/> effective, pick the winner (max bucket,
    /// then max effective, then RNG), increment its <b>delta</b> by 1, and reset only that bucket.
    /// </summary>
    /// <param name="baselines">
    /// Per-attribute class/trait baseline. Null → <see cref="DefaultScore"/> for every id.
    /// </param>
    /// <returns>The attribute id that received the point, or null if none can grow.</returns>
    public static string? TryGrow(
        PlayerProgressState state,
        Random random,
        IReadOnlyList<string>? catalog = null,
        IReadOnlyDictionary<string, int>? baselines = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(random);
        IReadOnlyList<string> ids = catalog ?? AttributeIds.All;
        if (state.Schema < PlayerProgressState.AttributeDeltaSchema)
        {
            state.Schema = PlayerProgressState.AttributeDeltaSchema;
        }

        PlayerProgressState.EnsureAttributeEntries(state, ids);

        int Baseline(string id) =>
            baselines != null && baselines.TryGetValue(id, out int b) ? b : DefaultScore;

        int EffectiveOf(string id) =>
            AttributeScoreMath.Effective(Baseline(id), state.Attributes[id]);

        List<string> eligible = new(ids.Count);
        foreach (string id in ids)
        {
            if (EffectiveOf(id) < MaxScore)
            {
                eligible.Add(id);
            }
        }

        if (eligible.Count == 0)
        {
            return null;
        }

        float maxBucket = float.NegativeInfinity;
        foreach (string id in eligible)
        {
            float bucket = state.AttributeBuckets[id];
            if (bucket > maxBucket)
            {
                maxBucket = bucket;
            }
        }

        List<string> byBucket = new(eligible.Count);
        foreach (string id in eligible)
        {
            if (state.AttributeBuckets[id] == maxBucket)
            {
                byBucket.Add(id);
            }
        }

        int maxScore = int.MinValue;
        foreach (string id in byBucket)
        {
            int score = EffectiveOf(id);
            if (score > maxScore)
            {
                maxScore = score;
            }
        }

        List<string> candidates = new(byBucket.Count);
        foreach (string id in byBucket)
        {
            if (EffectiveOf(id) == maxScore)
            {
                candidates.Add(id);
            }
        }

        string winner = candidates.Count == 1
            ? candidates[0]
            : candidates[random.Next(candidates.Count)];

        state.Attributes[winner] = state.Attributes[winner] + 1;
        state.AttributeBuckets[winner] = 0f;
        return winner;
    }
}
