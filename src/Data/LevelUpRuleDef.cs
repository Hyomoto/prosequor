namespace Prosequor.Data;

/// <summary>Compiled player level-up grant action kinds.</summary>
public enum LevelUpActionKind
{
    EarnSkillPoint,
    EarnSpecializationPoint,
    EarnAttribute
}

/// <summary>Compiled level-up rule ready for matching and application.</summary>
public sealed class LevelUpRuleDef
{
    public required string Id { get; init; }

    /// <summary>When set, fires when reached level L satisfies <c>L % Every == 0</c>.</summary>
    public int? Every { get; init; }

    /// <summary>When set, fires when the reached level is in this set.</summary>
    public IReadOnlySet<int>? Levels { get; init; }

    public LevelUpActionKind Action { get; init; }

    /// <summary>Grant amount (default 1). For attributes: TryGrow times or score delta.</summary>
    public int Value { get; init; } = 1;

    /// <summary>
    /// For <see cref="LevelUpActionKind.EarnAttribute"/>: known attribute id, or
    /// <see cref="BucketsKey"/> to run soft-reset bucket growth.
    /// </summary>
    public string? AttributeKey { get; init; }

    public int Priority { get; init; }

    /// <summary>Stable registration order for tie-breaks after priority.</summary>
    public int SourceOrder { get; init; }

    public const string BucketsKey = "buckets";

    public bool Matches(int reachedLevel)
    {
        if (Every is int every && every > 0)
        {
            return reachedLevel % every == 0;
        }

        return Levels != null && Levels.Contains(reachedLevel);
    }
}
