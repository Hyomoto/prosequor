using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Prosequor.Data;

/// <summary>Root shape for <c>config/prosequor/level-ups.json</c> (and <c>level-ups/*.json</c>).</summary>
public sealed class LevelUpFileJson
{
    public LevelUpRuleJson[]? rules { get; set; }
}

/// <summary>One player level-up grant rule.</summary>
public sealed class LevelUpRuleJson
{
    public string? id { get; set; }

    /// <summary>Fire when reached level L satisfies <c>L % every == 0</c>. Mutually exclusive with <see cref="levels"/>.</summary>
    public int? every { get; set; }

    /// <summary>Fire when the reached level is in this list. Mutually exclusive with <see cref="every"/>.</summary>
    public int[]? levels { get; set; }

    public string? action { get; set; }

    [JsonProperty("params")]
    public JObject? @params { get; set; }

    public int? priority { get; set; }
}

/// <summary>Contribution graft for level-up rules (<c>levelUps</c> on a contribution entry).</summary>
public sealed class LevelUpContributionJson
{
    /// <summary>
    /// Either the string <c>"all"</c> (clear every rule) or an array of rule ids to strip first.
    /// </summary>
    [JsonProperty("disable")]
    public JToken? disable { get; set; }

    public LevelUpRuleJson[]? rules { get; set; }

    /// <summary>
    /// Parses <see cref="disable"/> into whole-list removal or a rule-id list.
    /// Returns false when the token is present but not a recognized shape.
    /// </summary>
    public bool TryParseDisable(out bool disableAll, out string[] ruleIds, out string? error)
    {
        disableAll = false;
        ruleIds = Array.Empty<string>();
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
                    error = "disable array entries must be strings (rule ids).";
                    return false;
                }

                string id = item.Value<string>()?.Trim() ?? "";
                if (id.Length > 0)
                {
                    ids.Add(id);
                }
            }

            ruleIds = ids.ToArray();
            return true;
        }

        error = "disable must be \"all\" or an array of rule ids.";
        return false;
    }
}
