namespace Prosequor.Data;

/// <summary>JSON shape for one entry in <c>config/prosequor/pools.json</c>.</summary>
public class OutputPoolEntryJson
{
    public string code { get; set; } = "";
    public float weight { get; set; } = 1f;
}

/// <summary>JSON shape for one output pool under <c>config/prosequor/pools.json</c>.</summary>
public class OutputPoolJson
{
    public string id { get; set; } = "";
    public OutputPoolEntryJson[]? entries { get; set; }
}

/// <summary>Resolved output-pool entry (item code + pick weight).</summary>
public sealed class OutputPoolEntryDef
{
    public required string Code { get; init; }
    public float Weight { get; init; } = 1f;
}

/// <summary>Resolved output pool used to seed <see cref="Ability.Hooks.CollectionIndex"/> membership.</summary>
public sealed class OutputPoolDef
{
    public required string Id { get; init; }
    public required IReadOnlyList<OutputPoolEntryDef> Entries { get; init; }
}
