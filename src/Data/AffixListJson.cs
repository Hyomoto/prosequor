namespace Prosequor.Data;

/// <summary>JSON shape for one entry in <c>config/prosequor/affixes.json</c>.</summary>
public class AffixListEntryJson
{
    public string code { get; set; } = "";
    public string lang { get; set; } = "";
    public string? color { get; set; }
}

/// <summary>JSON shape for one named affix list under <c>config/prosequor/affixes.json</c>.</summary>
public class AffixListJson
{
    public string id { get; set; } = "";
    public AffixListEntryJson[]? entries { get; set; }
}

/// <summary>Resolved affix stamp definition (lang key + optional color).</summary>
public sealed class AffixListEntryDef
{
    public required string Code { get; init; }
    public required string Lang { get; init; }
    public string? Color { get; init; }
}

/// <summary>Resolved ordered affix list used by <c>prosequor:add-affix</c> <c>{ list, item }</c>.</summary>
public sealed class AffixListDef
{
    public required string Id { get; init; }
    public required IReadOnlyList<AffixListEntryDef> Entries { get; init; }
}
