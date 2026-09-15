using Prosequor.Ability.Hooks;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;

namespace Prosequor.Data;

/// <summary>One purchasable rank of a node. Tier 1 is the first purchase.</summary>
public class SkillTreeTierDef
{
    public string DescriptionLang { get; set; } = "";
    public IReadOnlyList<object> DescriptionArgs { get; set; } = Array.Empty<object>();
    public int Cost { get; set; } = 1;
    public int MinSkillLevel { get; set; }

    /// <summary>Gameplay rules granted while this is the player's owned rank.</summary>
    public IReadOnlyList<AbilityRule> Rules { get; set; } = Array.Empty<AbilityRule>();

    /// <summary>
    /// Effect param snapshots for skill-list description selectors (owned-tier lookup).
    /// </summary>
    public IReadOnlyList<AbilityEffectJson> EffectSnapshots { get; set; } =
        Array.Empty<AbilityEffectJson>();

    /// <summary>
    /// Optional live totals for the owned-rank tooltip. Empty means no bracket.
    /// </summary>
    public IReadOnlyList<TotalParamSpec> TotalParams { get; set; } = Array.Empty<TotalParamSpec>();
}

/// <summary>
/// Tooltip-time evaluation of one effect's number operand. <see cref="Target"/> is the
/// tier effect index; <see cref="Format"/> is <c>percent</c> or <c>fractionPercent</c>.
/// </summary>
public sealed class TotalParamSpec
{
    public int Target { get; init; }
    public required string Format { get; init; }
}

/// <summary>
/// A numeric description value evaluated while skill assets compile. Formatting
/// remains client-localized and is applied when the containing lang key renders.
/// </summary>
public sealed class FormattedDescriptionArg
{
    public required decimal Value { get; init; }
    public required string Format { get; init; }
}

/// <summary>Compiled skill-tree node. Coordinates are abstract layers/orders; UI maps them to pixels.</summary>
public class SkillTreeNodeDef
{
    public string Id { get; set; } = "";
    public string NameLang { get; set; } = "";
    public string Icon { get; set; } = "";

    /// <summary>Zero-based position in the authored nodes array; used as the layout hint.</summary>
    public int DeclarationOrder { get; set; }

    /// <summary>Ranks in purchase order; always holds at least one entry.</summary>
    public IReadOnlyList<SkillTreeTierDef> Tiers { get; set; } = new[] { new SkillTreeTierDef() };

    public int MaxTier => Tiers.Count;

    /// <summary>1-based tier lookup, clamped to the declared range.</summary>
    public SkillTreeTierDef TierAt(int tier) => Tiers[Math.Clamp(tier, 1, Tiers.Count) - 1];

    /// <summary>
    /// Prerequisite groups: OR within each group, AND across groups.
    /// Flat authored <c>["a","b"]</c> becomes two singleton groups.
    /// </summary>
    public IReadOnlyList<RequireGroup> RequireGroups { get; set; } = Array.Empty<RequireGroup>();

    /// <summary>Union of every alternative across <see cref="RequireGroups"/>.</summary>
    public IReadOnlyList<string> AllRequirementIds { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Same-skill node ids that cannot be owned together with this node.
    /// Compiler symmetrizes one-sided declarations.
    /// </summary>
    public IReadOnlyList<string> Excludes { get; set; } = Array.Empty<string>();

    public bool IsRoot => RequireGroups.Count == 0;

    /// <summary>
    /// When true, purchasing this node consumes one specialization slot
    /// (one slot per 10 player levels, shared across all skills).
    /// </summary>
    public bool IsSpecialization { get; set; }

    /// <summary>
    /// For dependents: row distance from the deepest parent. 1 = next row (default),
    /// 0 = same row (horizontal link). Roots ignore this.
    /// </summary>
    public int LayerOffset { get; set; } = 1;

    /// <summary>
    /// Soft horizontal preference when choosing among legal columns.
    /// Null keeps barycenter preference.
    /// </summary>
    public int? ColumnBias { get; set; }

    /// <summary>
    /// When false, the shake-out pass will not select this node as an upward pull
    /// candidate. Parentage can still move it when a parent is compacted.
    /// </summary>
    public bool Compact { get; set; } = true;

    /// <summary>Longest dependency depth; roots are 0.</summary>
    public int Layer { get; set; }

    /// <summary>Stable left-to-right index within <see cref="Layer"/>.</summary>
    public int Order { get; set; }

    /// <summary>
    /// Signed column on the constrained render grid. Downward edges move by -1, 0, or +1
    /// per row; same-layer edges are horizontal (non-zero column delta).
    /// </summary>
    public int GridColumn { get; set; }

    public IReadOnlyList<SkillTreeNodeDef> Parents { get; set; } = Array.Empty<SkillTreeNodeDef>();
    public IReadOnlyList<SkillTreeNodeDef> Children { get; set; } = Array.Empty<SkillTreeNodeDef>();
}

/// <summary>Compiled DAG for one skill. Null on SkillDef means no tree.</summary>
public class SkillTreeDef
{
    public IReadOnlyList<SkillTreeNodeDef> Nodes { get; set; } = Array.Empty<SkillTreeNodeDef>();
    public IReadOnlyDictionary<string, SkillTreeNodeDef> ById { get; set; } =
        new Dictionary<string, SkillTreeNodeDef>(StringComparer.OrdinalIgnoreCase);

    public int MaxLayer { get; set; }
    public int MaxOrder { get; set; }

    /// <summary>Max column span across all layers after grid placement.</summary>
    public int ColumnSpan { get; set; }

    /// <summary>World-space horizontal extent at scale 1: (ColumnSpan - 1) * grid step + node size.</summary>
    public double LayoutWidth { get; set; }

    public bool TryGet(string nodeId, out SkillTreeNodeDef node)
    {
        if (ById.TryGetValue(nodeId, out SkillTreeNodeDef? found) && found != null)
        {
            node = found;
            return true;
        }

        node = null!;
        return false;
    }
}

public class SkillTreeJson
{
    public SkillTreeNodeJson[]? nodes { get; set; }
}

public class SkillTreeNodeJson
{
    public string id { get; set; } = "";
    public string nameLang { get; set; } = "";

    /// <summary>Default description key for every tier unless a tier supplies its own.</summary>
    public string descriptionLang { get; set; } = "";

    /// <summary>
    /// Default positional args for <see cref="descriptionLang"/>. Entries may be
    /// param-selector strings or startup-evaluated sum/format objects. A tier may
    /// override with its own list; omitted means inherit this one.
    /// </summary>
    public JToken[]? descriptionParams { get; set; }

    /// <summary>
    /// Optional live-total specs for the owned-rank tooltip. A tier may override;
    /// omitted means inherit this list. Empty / omitted means no bracket.
    /// </summary>
    public JToken[]? totalParams { get; set; }
    public string icon { get; set; } = "";
    public int cost { get; set; } = 1;
    public int minSkillLevel { get; set; }

    /// <summary>When true, this node spends a shared specialization slot.</summary>
    public bool specialization { get; set; }

    /// <summary>Optional layout hints for the visual grid solver.</summary>
    public SkillTreeNodeLayoutJson? layout { get; set; }

    /// <summary>Optional ranks. Omitted fields fall back to the node-level values.</summary>
    public SkillTreeTierJson[]? tiers { get; set; }

    /// <summary>
    /// Prerequisites. Each entry is either a node id string (AND term) or a nested
    /// array of node ids (OR group). Flat <c>["a","b"]</c> means a AND b.
    /// </summary>
    public JToken[]? requires { get; set; }

    /// <summary>Same-skill node ids that become unselectable once this node is owned.</summary>
    public string[]? excludes { get; set; }

    /// <summary>
    /// Contribution-only: existing node id this row replaces. Dependencies on the old id
    /// are rewired to this node's id before compile.
    /// </summary>
    public string? replaces { get; set; }
}

/// <summary>One OR-group of prerequisite node ids.</summary>
public sealed class RequireGroup
{
    public required IReadOnlyList<string> Alternatives { get; init; }
}

/// <summary>Helpers for authored/fixture <c>requires</c> JToken arrays.</summary>
public static class SkillRequireJson
{
    public static JToken[] Ids(params string[] ids) =>
        ids.Select(id => (JToken)id).ToArray();

    public static JToken[] Mixed(params object[] parts)
    {
        JToken[] tokens = new JToken[parts.Length];
        for (int i = 0; i < parts.Length; i++)
        {
            tokens[i] = parts[i] switch
            {
                string id => id,
                string[] alts => new JArray(alts.Cast<object>().ToArray()),
                JToken token => token,
                _ => throw new ArgumentException(
                    $"Unsupported requires entry type '{parts[i]?.GetType().Name ?? "null"}'.",
                    nameof(parts))
            };
        }

        return tokens;
    }
}

/// <summary>Optional visual-grid hints. Omitted fields keep default inference.</summary>
public class SkillTreeNodeLayoutJson
{
    /// <summary>
    /// Row distance from the deepest parent for dependent nodes. Allowed values: 0 (same row,
    /// horizontal link) or 1 (next row, default). Roots ignore this.
    /// </summary>
    public int? layerOffset { get; set; }

    /// <summary>
    /// Soft horizontal preference among legal columns. Allowed values: left, right.
    /// Omitted keeps the default barycenter preference (between parents when merging).
    /// </summary>
    public string? columnBias { get; set; }

    /// <summary>
    /// When false, the node is not selected for upward shake-out pulls.
    /// Omitted / true keeps default compaction.
    /// </summary>
    public bool? compact { get; set; }
}

public class SkillTreeTierJson
{
    /// <summary>Optional override of the node-level description key.</summary>
    public string? descriptionLang { get; set; }

    /// <summary>Optional override of the node-level descriptionParams list.</summary>
    public JToken[]? descriptionParams { get; set; }

    /// <summary>Optional override of the node-level totalParams list.</summary>
    public JToken[]? totalParams { get; set; }
    public int? cost { get; set; }
    public int? minSkillLevel { get; set; }
    public AbilityEffectJson[]? effects { get; set; }
}

/// <summary>Explicit hook/phase/action rule envelope (no legacy type shorthand).</summary>
public class AbilityEffectJson
{
    /// <summary>Legacy field — presence is a compile error.</summary>
    public string? type { get; set; }

    /// <summary>
    /// Zero-based effect index copied from the immediately previous tier.
    /// Fields present on this row overlay the copy; params are merged by key.
    /// </summary>
    public int? replicate { get; set; }

    public string? hook { get; set; }
    public string? verb { get; set; }
    public string? phase { get; set; }
    public string? action { get; set; }
    public AbilityWhenJson? when { get; set; }
    public Newtonsoft.Json.Linq.JObject? @params { get; set; }
    public int? priority { get; set; }
}

public class AbilityWhenJson
{
    /// <summary>Legacy field — presence is a compile error.</summary>
    public string? verb { get; set; }

    /// <summary>AND-list of role/collection/token criteria (see TagCriterionParser).</summary>
    public string[]? tags { get; set; }
}
