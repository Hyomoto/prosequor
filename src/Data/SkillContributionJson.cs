using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Prosequor.Xp;

namespace Prosequor.Data;

/// <summary>
/// Skill-tree graft from <c>config/prosequor/contributions/*.json</c>.
/// Each file is a JSON array of these objects (any number of files / nodes).
/// Contributed node ids must be namespaced as <c>assetDomain:localId</c>.
/// </summary>
public class SkillContributionJson
{
    public string skill { get; set; } = "";

    /// <summary>
    /// Either the string <c>"all"</c> (remove the entire skill) or an array of node ids to strip.
    /// </summary>
    [JsonProperty("disable")]
    public JToken? disable { get; set; }

    public SkillTreeNodeJson[]? nodes { get; set; }

    /// <summary>XP rules to merge into the target skill (last-win by rule id within that skill).</summary>
    public XpRuleJson[]? xpRules { get; set; }

    /// <summary>
    /// Optional player level-up rule grafts. Handled by <see cref="LevelUpRegistry"/>;
    /// skill merge ignores entries that only set this field.
    /// </summary>
    public LevelUpContributionJson? levelUps { get; set; }

    /// <summary>
    /// Engine-style mod gates. The whole entry (skill grafts and <see cref="levelUps"/>)
    /// is skipped unless every clause is met. Same shape as JSON-patch <c>dependsOn</c>.
    /// </summary>
    public ContributionModDependence[]? dependsOn { get; set; }

    /// <summary>
    /// Parses <see cref="disable"/> into whole-skill removal or a node-id list.
    /// Returns false when the token is present but not a recognized shape.
    /// </summary>
    public bool TryParseDisable(out bool disableAll, out string[] nodeIds, out string? error)
    {
        disableAll = false;
        nodeIds = Array.Empty<string>();
        error = null;
        if (disable == null || disable.Type == JTokenType.Null)
        {
            return true;
        }

        if (disable.Type == JTokenType.String)
        {
            string value = disable.Value<string>()?.Trim() ?? "";
            if (string.Equals(value, "all", StringComparison.OrdinalIgnoreCase))
            {
                disableAll = true;
                return true;
            }

            error = $"disable string must be \"all\" (got '{value}').";
            return false;
        }

        if (disable.Type == JTokenType.Array)
        {
            List<string> ids = new();
            foreach (JToken item in disable.Children())
            {
                if (item.Type != JTokenType.String)
                {
                    error = "disable array entries must be strings (node ids).";
                    return false;
                }

                string id = item.Value<string>()?.Trim() ?? "";
                if (id.Length > 0)
                {
                    ids.Add(id);
                }
            }

            nodeIds = ids.ToArray();
            return true;
        }

        error = "disable must be \"all\" or an array of node ids.";
        return false;
    }
}
