using System.Globalization;
using Newtonsoft.Json.Linq;

namespace Prosequor.Data;

/// <summary>
/// Skill-list descriptionParams: compile-time validation and progress-aware coefficient resolve.
/// Does not run the ability pipeline — sums authored params given ownership.
/// </summary>
public static class SkillDescriptionResolver
{
    /// <summary>Deep-clones effect rows for description-only param lookup.</summary>
    public static AbilityEffectJson[] SnapshotEffects(IReadOnlyList<AbilityEffectJson>? effects)
    {
        if (effects == null || effects.Count == 0)
        {
            return Array.Empty<AbilityEffectJson>();
        }

        AbilityEffectJson[] copy = new AbilityEffectJson[effects.Count];
        for (int i = 0; i < effects.Count; i++)
        {
            AbilityEffectJson src = effects[i];
            copy[i] = new AbilityEffectJson
            {
                hook = src.hook,
                verb = src.verb,
                phase = src.phase,
                action = src.action,
                when = src.when,
                priority = src.priority,
                replicate = src.replicate,
                @params = src.@params == null ? null : (JObject)src.@params.DeepClone()
            };
        }

        return copy;
    }

    /// <summary>
    /// Validates skill-root descriptionParams. Node scopes must exist on at least one tier.
    /// </summary>
    public static bool TryValidate(
        string skillId,
        IReadOnlyList<JToken> declarations,
        IReadOnlyList<AbilityEffectJson> rootEffects,
        SkillTreeDef? tree,
        List<string> errors)
    {
        if (declarations.Count == 0)
        {
            return true;
        }

        bool valid = true;
        foreach (JToken declaration in declarations)
        {
            if (declaration.Type == JTokenType.String)
            {
                string selector = declaration.Value<string>()?.Trim() ?? "";
                if (!TryValidateSelector(skillId, selector, rootEffects, tree, allowMissing: false, errors))
                {
                    valid = false;
                }

                continue;
            }

            if (declaration is not JObject expression)
            {
                errors.Add(
                    $"Skill '{skillId}': descriptionParams entries must be selectors or expression objects.");
                valid = false;
                continue;
            }

            string format = expression.Value<string>("format")?.Trim() ?? "";
            if (format.Length > 0 && format is not ("percent" or "fractionPercent"))
            {
                errors.Add($"Skill '{skillId}': unknown description format '{format}'.");
                valid = false;
            }

            JArray? sum = expression["sum"] as JArray;
            if (sum == null || sum.Count == 0)
            {
                errors.Add($"Skill '{skillId}': description expression requires a non-empty sum array.");
                valid = false;
                continue;
            }

            foreach (JToken operand in sum)
            {
                string selector = operand.Value<string>()?.Trim() ?? "";
                if (!TryValidateSelector(skillId, selector, rootEffects, tree, allowMissing: true, errors))
                {
                    valid = false;
                }
            }
        }

        return valid;
    }

    /// <summary>
    /// Resolves skill-list description args for the current ownership snapshot.
    /// Unowned node operands contribute 0 inside sums.
    /// </summary>
    public static IReadOnlyList<object> Resolve(
        SkillDef skill,
        IPlayerProgress? progress)
    {
        IReadOnlyList<JToken> declarations = skill.DescriptionParamSpecs;
        if (declarations.Count == 0)
        {
            return Array.Empty<object>();
        }

        List<object> args = new(declarations.Count);
        foreach (JToken declaration in declarations)
        {
            if (declaration.Type == JTokenType.String)
            {
                string selector = declaration.Value<string>()?.Trim() ?? "";
                if (TryResolveSelector(
                        skill,
                        progress,
                        selector,
                        allowMissingNode: false,
                        out JToken? value)
                    && value != null)
                {
                    args.Add(ToDescriptionScalar(value));
                }
                else
                {
                    args.Add("");
                }

                continue;
            }

            if (declaration is not JObject expression)
            {
                args.Add("");
                continue;
            }

            string format = expression.Value<string>("format")?.Trim() ?? "";
            JArray? sum = expression["sum"] as JArray;
            if (sum == null || sum.Count == 0)
            {
                args.Add(0m);
                continue;
            }

            decimal total = 0m;
            foreach (JToken operand in sum)
            {
                string selector = operand.Value<string>()?.Trim() ?? "";
                if (!TryResolveSelector(
                        skill,
                        progress,
                        selector,
                        allowMissingNode: true,
                        out JToken? value)
                    || value == null)
                {
                    continue;
                }

                if (TryToDecimal(value, out decimal number))
                {
                    total += number;
                }
            }

            if (format is "fractionPercent" or "percent")
            {
                args.Add(new FormattedDescriptionArg { Value = total, Format = format });
            }
            else
            {
                args.Add(total);
            }
        }

        return args;
    }

    /// <summary>Fingerprint of ownership that affects this skill's description resolve.</summary>
    public static string ProgressFingerprint(SkillDef skill, IPlayerProgress? progress)
    {
        if (progress == null || skill.Tree == null || skill.DescriptionParamSpecs.Count == 0)
        {
            return "0";
        }

        List<string> parts = new();
        foreach (SkillTreeNodeDef node in skill.Tree.Nodes)
        {
            int tier = progress.GetUnlockTier(skill.Id, node.Id);
            if (tier > 0)
            {
                parts.Add(node.Id + "=" + tier);
            }
        }

        parts.Sort(StringComparer.OrdinalIgnoreCase);
        return string.Join(";", parts);
    }

    static bool TryValidateSelector(
        string skillId,
        string selector,
        IReadOnlyList<AbilityEffectJson> rootEffects,
        SkillTreeDef? tree,
        bool allowMissing,
        List<string> errors)
    {
        if (selector.Length == 0)
        {
            errors.Add($"Skill '{skillId}': descriptionParams contains a blank selector.");
            return false;
        }

        if (!TryParseSelector(
                selector,
                out string? nodeId,
                out string scopedSelector,
                out string? parseError))
        {
            errors.Add($"Skill '{skillId}': {parseError}");
            return false;
        }

        IReadOnlyList<AbilityEffectJson> source;
        if (nodeId != null)
        {
            if (tree == null || !tree.TryGet(nodeId, out SkillTreeNodeDef node))
            {
                errors.Add(
                    $"Skill '{skillId}': description param '{selector}' references unknown node '{nodeId}'.");
                return false;
            }

            if (!TryFindPathOnAnyTier(node, scopedSelector, out _))
            {
                if (allowMissing)
                {
                    return true;
                }

                errors.Add(
                    $"Skill '{skillId}': description param '{selector}' was not found on any tier of '{nodeId}'.");
                return false;
            }

            return true;
        }

        source = rootEffects;
        if (TryMatchParam(source, scopedSelector, out _))
        {
            return true;
        }

        if (allowMissing && source.Count == 0)
        {
            return true;
        }

        errors.Add(
            $"Skill '{skillId}': description param '{selector}' was not found on root effects.");
        return false;
    }

    static bool TryResolveSelector(
        SkillDef skill,
        IPlayerProgress? progress,
        string selector,
        bool allowMissingNode,
        out JToken? value)
    {
        value = null;
        if (selector.Length == 0
            || !TryParseSelector(selector, out string? nodeId, out string scopedSelector, out _))
        {
            return false;
        }

        IReadOnlyList<AbilityEffectJson> source;
        if (nodeId != null)
        {
            if (skill.Tree == null || !skill.Tree.TryGet(nodeId, out SkillTreeNodeDef node))
            {
                return allowMissingNode;
            }

            int owned = progress?.GetUnlockTier(skill.Id, nodeId) ?? 0;
            if (owned <= 0)
            {
                return allowMissingNode;
            }

            source = node.TierAt(owned).EffectSnapshots;
            if (!TryMatchParam(source, scopedSelector, out value))
            {
                return allowMissingNode;
            }

            return true;
        }

        source = skill.RootEffectSnapshots;
        return TryMatchParam(source, scopedSelector, out value);
    }

    static bool TryParseSelector(
        string selector,
        out string? nodeId,
        out string scopedSelector,
        out string? error)
    {
        nodeId = null;
        scopedSelector = selector;
        error = null;

        int scopeDot = selector.IndexOf('.');
        if (scopeDot <= 0)
        {
            return true;
        }

        string scope = selector[..scopeDot];
        if (scope.Equals("root", StringComparison.OrdinalIgnoreCase)
            || scope.Equals("current", StringComparison.OrdinalIgnoreCase))
        {
            scopedSelector = selector[(scopeDot + 1)..];
            return true;
        }

        if (scope.Equals("prev", StringComparison.OrdinalIgnoreCase))
        {
            error = "description param scope 'prev' is only valid on unlock tiers.";
            return false;
        }

        if (int.TryParse(scope, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
        {
            // Bare "0.perLevel" — effect index, not a node id.
            return true;
        }

        nodeId = scope;
        scopedSelector = selector[(scopeDot + 1)..];
        if (scopedSelector.Length == 0)
        {
            error = $"description param '{selector}' is missing an effect path after node id.";
            return false;
        }

        return true;
    }

    static bool TryFindPathOnAnyTier(
        SkillTreeNodeDef node,
        string scopedSelector,
        out JToken? value)
    {
        value = null;
        foreach (SkillTreeTierDef tier in node.Tiers)
        {
            if (TryMatchParam(tier.EffectSnapshots, scopedSelector, out value))
            {
                return true;
            }
        }

        return false;
    }

    static bool TryMatchParam(
        IReadOnlyList<AbilityEffectJson> source,
        string scopedSelector,
        out JToken? value)
    {
        value = null;
        int effectIndex = -1;
        string paramPath = scopedSelector;
        int dot = scopedSelector.IndexOf('.');
        if (dot > 0 && int.TryParse(scopedSelector[..dot], out int parsedIndex))
        {
            effectIndex = parsedIndex;
            paramPath = scopedSelector[(dot + 1)..];
        }

        List<JToken> matches = new();
        if (effectIndex >= 0)
        {
            if (effectIndex < source.Count
                && TryGetParamPath(source[effectIndex].@params, paramPath, out JToken? token))
            {
                matches.Add(token);
            }
        }
        else
        {
            foreach (AbilityEffectJson effect in source)
            {
                if (TryGetParamPath(effect.@params, paramPath, out JToken? token))
                {
                    matches.Add(token);
                }
            }
        }

        if (matches.Count != 1)
        {
            return false;
        }

        value = matches[0];
        return true;
    }

    internal static bool TryGetParamPath(JObject? parameters, string path, out JToken token)
    {
        token = null!;
        if (parameters == null || string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        string[] parts = path.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            return false;
        }

        JToken? current = parameters;
        foreach (string part in parts)
        {
            if (current is not JObject obj
                || !obj.TryGetValue(part, StringComparison.OrdinalIgnoreCase, out JToken? next)
                || next == null)
            {
                return false;
            }

            current = next;
        }

        token = current;
        return true;
    }

    internal static object ToDescriptionScalar(JToken value) =>
        value is JValue scalar
            ? scalar.Value ?? ""
            : value.ToString(Newtonsoft.Json.Formatting.None);

    internal static bool TryToDecimal(JToken value, out decimal number)
    {
        if (value is JValue scalar && scalar.Value != null)
        {
            try
            {
                number = Convert.ToDecimal(scalar.Value, CultureInfo.InvariantCulture);
                return true;
            }
            catch (Exception ex) when (
                ex is FormatException
                or InvalidCastException
                or OverflowException)
            {
                // Handled below.
            }
        }

        number = 0m;
        return false;
    }
}
