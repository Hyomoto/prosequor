using Prosequor.Ability.Hooks;

namespace Prosequor.Data;

/// <summary>Registered attribute-stat definition (content or code).</summary>
public sealed class AttributeStatDef
{
    public string Id { get; set; } = "";

    /// <summary>Lang key for the display name (default <c>attribute-{id}</c>).</summary>
    public string NameLang { get; set; } = "";

    /// <summary>Lang key for the tooltip flavor quote (default <c>attribute-flavor-{id}</c>).</summary>
    public string DescriptionLang { get; set; } = "";

    /// <summary>Texture path (default <c>textures/icons/{id}-attribute.svg</c>).</summary>
    public string Icon { get; set; } = "";

    /// <summary>Flattened compiled rules for this attribute.</summary>
    public List<AbilityRule> Rules { get; set; } = new();
}

/// <summary>Asset shape for <c>config/prosequor/stats/*.json</c>.</summary>
public sealed class AttributeStatDefJson
{
    public string? id { get; set; }
    public string? nameLang { get; set; }
    public string? descriptionLang { get; set; }
    public string? icon { get; set; }
    public AttributeStatRuleJson[]? rules { get; set; }
}

/// <summary>
/// One attribute-stat rule row. Same hook/phase/action envelope as skill effects,
/// plus optional score gates.
/// </summary>
public sealed class AttributeStatRuleJson
{
    public string? id { get; set; }
    public int? minScore { get; set; }
    public int? maxScore { get; set; }
    public string? hook { get; set; }
    public string? verb { get; set; }
    public string? phase { get; set; }
    public string? action { get; set; }
    public AbilityWhenJson? when { get; set; }
    public Newtonsoft.Json.Linq.JObject? @params { get; set; }
    public int? priority { get; set; }
}
