using Prosequor.Ability.Hooks;
using Prosequor.Xp;
using Newtonsoft.Json.Linq;

namespace Prosequor.Data;

/// <summary>Registered skill definition (content or code).</summary>
public class SkillDef
{
    public string Id { get; set; } = "";

    /// <summary>Lang key under domain prosequor (e.g. skill-digging).</summary>
    public string NameLang { get; set; } = "";

    /// <summary>Optional list-hover description lang key (e.g. skilldesc-digging).</summary>
    public string DescriptionLang { get; set; } = "";

    /// <summary>
    /// Unresolved skill-list <c>descriptionParams</c> (selectors / sum expressions).
    /// Resolved at hover with progress so owned additive unlocks can contribute.
    /// </summary>
    public IReadOnlyList<JToken> DescriptionParamSpecs { get; set; } = Array.Empty<JToken>();

    /// <summary>Root effect param snapshots for skill-list description selectors.</summary>
    public IReadOnlyList<AbilityEffectJson> RootEffectSnapshots { get; set; } =
        Array.Empty<AbilityEffectJson>();

    /// <summary>Path under the prosequor domain, e.g. textures/icons/digging-skill.svg.</summary>
    public string Icon { get; set; } = "";

    /// <summary>
    /// Derived skill contract (hobby / specialization / minor / passive).
    /// Set at compile from JSON <c>hobby</c> and tree shape.
    /// </summary>
    public SkillKind Kind { get; set; } = SkillKind.Minor;

    /// <summary>True when <see cref="Kind"/> is <see cref="SkillKind.Hobby"/>.</summary>
    public bool IsHobby => Kind == SkillKind.Hobby;

    /// <summary>
    /// When true, omitted from the shared class base set (trait <c>skills</c> can still add it).
    /// Defaults false so every skill stays class-accessible unless authored otherwise.
    /// </summary>
    public bool IsOptional { get; set; }

    /// <summary>Level cap derived from <see cref="Kind"/> (not authored in JSON).</summary>
    public int MaxLevel { get; set; } = XpCurves.SkillMaxLevel;

    /// <summary>
    /// Flattened active rule index for this skill (root effects + all tree-tier rules).
    /// Ownership filtering happens in the pipeline.
    /// </summary>
    public List<AbilityRule> Rules { get; set; } = new();

    /// <summary>Compiled skill tree, or null when the skill has none / failed validation.</summary>
    public SkillTreeDef? Tree { get; set; }

    /// <summary>Compiled XP rules owned by this skill (inferred at compile; no JSON <c>skill</c> field).</summary>
    public IReadOnlyList<XpRule> XpRules { get; set; } = Array.Empty<XpRule>();

    /// <summary>
    /// Per skill-level growth credit deposited into attribute buckets on skill level-up
    /// (before player XP / attribute grants).
    /// </summary>
    public IReadOnlyList<AttributeScoreEntry> AttributeScores { get; set; } =
        Array.Empty<AttributeScoreEntry>();
}

/// <summary>Compiled skill → attribute-bucket fill entry.</summary>
public readonly record struct AttributeScoreEntry(string Id, float Value);

/// <summary>
/// JSON shape for one skill file under <c>config/prosequor/skills/*.json</c>
/// (any mod domain). Each file is a single object, not an array.
/// </summary>
public class SkillDefJson
{
    public string id { get; set; } = "";
    public string nameLang { get; set; } = "";
    public string descriptionLang { get; set; } = "";
    public JToken[]? descriptionParams { get; set; }
    public string icon { get; set; } = "";

    /// <summary>
    /// Ignored. Caps are derived from skill kind. Presence still deserializes so we can warn.
    /// </summary>
    public int? maxLevel { get; set; }

    /// <summary>When true, this skill is a hobby (cap 20, local unlock points).</summary>
    public bool hobby { get; set; }

    /// <summary>
    /// When true, not in the shared class base set. Trait <c>skills</c> can still grant it.
    /// </summary>
    public bool optional { get; set; }

    public AttributeScoreJson[]? attributeScores { get; set; }
    public XpRuleJson[]? xpRules { get; set; }
    public AbilityEffectJson[]? effects { get; set; }
    public SkillTreeJson? tree { get; set; }
}

/// <summary>JSON shape for one <c>attributeScores</c> entry on a skill root.</summary>
public class AttributeScoreJson
{
    public string id { get; set; } = "";
    public float value { get; set; }
}
