namespace Prosequor.Data;

/// <summary>Asset row for <c>config/prosequor/trait-attributes</c>.</summary>
public sealed class TraitAttributeMappingJson
{
    public string? code { get; set; }
    public Dictionary<string, int>? attributes { get; set; }
    public bool retainTrait { get; set; }
}

/// <summary>Compiled trait → attribute score deltas (empty = crafting gate / flavor-only).</summary>
public sealed class TraitAttributeMapping
{
    public string Code { get; init; } = "";
    public IReadOnlyDictionary<string, int> Attributes { get; init; } =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// When true with score deltas, keep the trait on the class (e.g. <c>HasTrait</c> gates)
    /// and clear only that trait's vanilla Entity.Stats bag.
    /// </summary>
    public bool RetainTrait { get; init; }

    public bool HasScoreDeltas => Attributes.Count > 0;

    /// <summary>Mapped score trait that should be removed from <c>CharacterClass.Traits</c>.</summary>
    public bool ShouldStripFromClass => HasScoreDeltas && !RetainTrait;
}
