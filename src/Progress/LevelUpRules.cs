using Prosequor.Data;

namespace Prosequor.Progress;

/// <summary>
/// Pure helpers: match and apply compiled level-up rules across a player level range.
/// </summary>
public static class LevelUpRules
{
    /// <summary>
    /// For each level in (beforeLevel, afterLevel], run every matching rule in priority then
    /// source-order. Returns attribute ids that received a point (one entry per successful grant).
    /// Specialization rules are capacity-only and do not mutate state here.
    /// </summary>
    public static IReadOnlyList<string> Apply(
        PlayerProgressState state,
        IReadOnlyList<LevelUpRuleDef> rules,
        int beforeLevel,
        int afterLevel,
        Random random,
        IReadOnlyList<string>? catalog = null,
        IReadOnlyDictionary<string, int>? attributeBaselines = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(random);

        if (afterLevel <= beforeLevel || rules.Count == 0)
        {
            return Array.Empty<string>();
        }

        List<LevelUpRuleDef> ordered = rules
            .OrderBy(r => r.Priority)
            .ThenBy(r => r.SourceOrder)
            .ToList();

        List<string> winners = new();
        for (int level = beforeLevel + 1; level <= afterLevel; level++)
        {
            foreach (LevelUpRuleDef rule in ordered)
            {
                if (!rule.Matches(level))
                {
                    continue;
                }

                ApplyOne(state, rule, random, winners, catalog, attributeBaselines);
            }
        }

        return winners;
    }

    /// <summary>
    /// Derived specialization slot capacity: sum of matching
    /// <see cref="LevelUpActionKind.EarnSpecializationPoint"/> grants from level 1..playerLevel.
    /// </summary>
    public static int SpecializationSlots(IReadOnlyList<LevelUpRuleDef> rules, int playerLevel)
    {
        ArgumentNullException.ThrowIfNull(rules);
        if (playerLevel < XpCurves.PlayerMinLevel || rules.Count == 0)
        {
            return 0;
        }

        int slots = 0;
        foreach (LevelUpRuleDef rule in rules)
        {
            if (rule.Action != LevelUpActionKind.EarnSpecializationPoint)
            {
                continue;
            }

            for (int level = XpCurves.PlayerMinLevel; level <= playerLevel; level++)
            {
                if (rule.Matches(level))
                {
                    slots += rule.Value;
                }
            }
        }

        return Math.Max(0, slots);
    }

    static void ApplyOne(
        PlayerProgressState state,
        LevelUpRuleDef rule,
        Random random,
        List<string> winners,
        IReadOnlyList<string>? catalog,
        IReadOnlyDictionary<string, int>? attributeBaselines)
    {
        switch (rule.Action)
        {
            case LevelUpActionKind.EarnSkillPoint:
                state.UnlockPoints = Math.Max(0, state.UnlockPoints + rule.Value);
                break;

            case LevelUpActionKind.EarnSpecializationPoint:
                // Capacity is derived; nothing to store.
                break;

            case LevelUpActionKind.EarnAttribute:
                ApplyAttribute(state, rule, random, winners, catalog, attributeBaselines);
                break;
        }
    }

    static void ApplyAttribute(
        PlayerProgressState state,
        LevelUpRuleDef rule,
        Random random,
        List<string> winners,
        IReadOnlyList<string>? catalog,
        IReadOnlyDictionary<string, int>? attributeBaselines)
    {
        IReadOnlyList<string> ids = catalog ?? AttributeIds.All;
        if (state.Schema < PlayerProgressState.AttributeDeltaSchema)
        {
            state.Schema = PlayerProgressState.AttributeDeltaSchema;
        }

        PlayerProgressState.EnsureAttributeEntries(state, ids);
        if (string.Equals(rule.AttributeKey, LevelUpRuleDef.BucketsKey, StringComparison.OrdinalIgnoreCase))
        {
            for (int i = 0; i < rule.Value; i++)
            {
                string? winner = AttributeGrowth.TryGrow(state, random, ids, attributeBaselines);
                if (winner != null)
                {
                    winners.Add(winner);
                }
            }

            return;
        }

        string attrId = rule.AttributeKey!;
        string? canonical = AttributeIds.Canonicalize(attrId, ids);
        if (canonical == null)
        {
            return;
        }

        int baseline = attributeBaselines != null
            && attributeBaselines.TryGetValue(canonical, out int b)
            ? b
            : AttributeGrowth.DefaultScore;
        int delta = state.Attributes.TryGetValue(canonical, out int d) ? d : 0;
        int current = AttributeScoreMath.Effective(baseline, delta);
        int next = Math.Min(AttributeGrowth.MaxScore, current + rule.Value);
        if (next == current)
        {
            return;
        }

        state.Attributes[canonical] = AttributeScoreMath.DeltaFromEffective(baseline, next);
        for (int i = current; i < next; i++)
        {
            winners.Add(canonical);
        }
    }
}
