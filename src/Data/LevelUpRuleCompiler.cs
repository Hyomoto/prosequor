using Newtonsoft.Json.Linq;

namespace Prosequor.Data;

/// <summary>Validates and compiles <see cref="LevelUpRuleJson"/> rows into <see cref="LevelUpRuleDef"/>.</summary>
public static class LevelUpRuleCompiler
{
    public const string ActionEarnSkillPoint = "prosequor:earn-skill-point";
    public const string ActionEarnSpecializationPoint = "prosequor:earn-specialization-point";
    public const string ActionEarnAttribute = "prosequor:earn-attribute";

    /// <summary>
    /// Compiles one rule. Returns null and appends to <paramref name="errors"/> on failure.
    /// </summary>
    public static LevelUpRuleDef? Compile(
        LevelUpRuleJson row,
        ref int sourceOrder,
        List<string> errors,
        IReadOnlyList<string>? catalog = null)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(errors);
        IReadOnlyList<string> attributeCatalog = catalog ?? AttributeIds.All;

        string id = row.id?.Trim() ?? "";
        if (id.Length == 0)
        {
            errors.Add("level-up rule missing id.");
            return null;
        }

        bool hasEvery = row.every.HasValue;
        bool hasLevels = row.levels is { Length: > 0 };
        if (hasEvery == hasLevels)
        {
            errors.Add($"level-up rule '{id}' must set exactly one of every or levels.");
            return null;
        }

        int? every = null;
        HashSet<int>? levels = null;
        if (hasEvery)
        {
            int n = row.every!.Value;
            if (n <= 0)
            {
                errors.Add($"level-up rule '{id}' every must be > 0.");
                return null;
            }

            every = n;
        }
        else
        {
            levels = new HashSet<int>();
            foreach (int level in row.levels!)
            {
                if (level < XpCurves.PlayerMinLevel || level > XpCurves.PlayerMaxLevel)
                {
                    errors.Add(
                        $"level-up rule '{id}' level {level} is outside player range "
                        + $"{XpCurves.PlayerMinLevel}–{XpCurves.PlayerMaxLevel}.");
                    return null;
                }

                levels.Add(level);
            }

            if (levels.Count == 0)
            {
                errors.Add($"level-up rule '{id}' levels list is empty.");
                return null;
            }
        }

        string actionRaw = row.action?.Trim() ?? "";
        if (actionRaw.Length == 0)
        {
            errors.Add($"level-up rule '{id}' missing action.");
            return null;
        }

        if (!actionRaw.Contains(':'))
        {
            actionRaw = "prosequor:" + actionRaw;
        }

        if (!TryParseAction(actionRaw, out LevelUpActionKind kind))
        {
            errors.Add($"level-up rule '{id}' unknown action '{row.action}'.");
            return null;
        }

        int value = 1;
        string? attributeKey = null;
        if (row.@params != null)
        {
            if (row.@params.TryGetValue("value", StringComparison.OrdinalIgnoreCase, out JToken? valueTok)
                && valueTok.Type != JTokenType.Null)
            {
                if (valueTok.Type != JTokenType.Integer && valueTok.Type != JTokenType.Float)
                {
                    errors.Add($"level-up rule '{id}' params.value must be a number.");
                    return null;
                }

                value = valueTok.Value<int>();
                if (value <= 0)
                {
                    errors.Add($"level-up rule '{id}' params.value must be > 0.");
                    return null;
                }
            }

            if (row.@params.TryGetValue("key", StringComparison.OrdinalIgnoreCase, out JToken? keyTok)
                && keyTok.Type != JTokenType.Null)
            {
                if (keyTok.Type != JTokenType.String)
                {
                    errors.Add($"level-up rule '{id}' params.key must be a string.");
                    return null;
                }

                attributeKey = keyTok.Value<string>()?.Trim();
            }
        }

        if (kind == LevelUpActionKind.EarnAttribute)
        {
            if (string.IsNullOrWhiteSpace(attributeKey))
            {
                errors.Add($"level-up rule '{id}' earn-attribute requires params.key.");
                return null;
            }

            if (!string.Equals(attributeKey, LevelUpRuleDef.BucketsKey, StringComparison.OrdinalIgnoreCase))
            {
                string? canonical = AttributeIds.Canonicalize(attributeKey, attributeCatalog);
                if (canonical == null)
                {
                    errors.Add(
                        $"level-up rule '{id}' unknown attribute key '{attributeKey}' "
                        + $"(expected {LevelUpRuleDef.BucketsKey} or a loaded attribute id).");
                    return null;
                }

                attributeKey = canonical;
            }
            else
            {
                attributeKey = LevelUpRuleDef.BucketsKey;
            }
        }
        else if (!string.IsNullOrWhiteSpace(attributeKey))
        {
            errors.Add($"level-up rule '{id}' action does not accept params.key.");
            return null;
        }

        int order = sourceOrder++;
        return new LevelUpRuleDef
        {
            Id = id,
            Every = every,
            Levels = levels,
            Action = kind,
            Value = value,
            AttributeKey = attributeKey,
            Priority = row.priority ?? 0,
            SourceOrder = order
        };
    }

    static bool TryParseAction(string action, out LevelUpActionKind kind)
    {
        if (string.Equals(action, ActionEarnSkillPoint, StringComparison.OrdinalIgnoreCase))
        {
            kind = LevelUpActionKind.EarnSkillPoint;
            return true;
        }

        if (string.Equals(action, ActionEarnSpecializationPoint, StringComparison.OrdinalIgnoreCase))
        {
            kind = LevelUpActionKind.EarnSpecializationPoint;
            return true;
        }

        if (string.Equals(action, ActionEarnAttribute, StringComparison.OrdinalIgnoreCase))
        {
            kind = LevelUpActionKind.EarnAttribute;
            return true;
        }

        kind = default;
        return false;
    }
}
