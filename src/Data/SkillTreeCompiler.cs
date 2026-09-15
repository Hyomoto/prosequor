using Prosequor.Ability;
using Prosequor.Ability.Actions;
using Prosequor.Ability.Hooks;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;

namespace Prosequor.Data;

/// <summary>
/// Validates and compiles a skill-tree DAG into layered layout.
/// Pure: no UI/API side effects beyond optional logging via the caller.
/// </summary>
public static class SkillTreeCompiler
{
    public const int BarycentricPasses = 4;

    public sealed class CompileIssue
    {
        public required string Message { get; init; }
        public IReadOnlyList<string> BlamedNodeIds { get; init; } = Array.Empty<string>();
    }

    public sealed class CompileResult
    {
        public SkillTreeDef? Tree { get; set; }
        public List<CompileIssue> Issues { get; } = new();
        public List<string> Errors { get; } = new();
        public List<string> Warnings { get; } = new();
        public bool Success => Tree != null && Errors.Count == 0;

        public void AddError(string message, params string[] blamedNodeIds)
        {
            Issues.Add(new CompileIssue
            {
                Message = message,
                BlamedNodeIds = blamedNodeIds.Length == 0
                    ? Array.Empty<string>()
                    : blamedNodeIds
            });
            Errors.Add(message);
        }
    }

    public static CompileResult Compile(
        string skillId,
        int skillMaxLevel,
        SkillTreeJson? json,
        IHookRegistry hooks,
        IAbilityActionRegistry actions,
        CollectionIndex collections,
        ref int sourceOrder) =>
        Compile(
            skillId,
            skillMaxLevel,
            json,
            Array.Empty<AbilityEffectJson>(),
            hooks,
            actions,
            collections,
            ref sourceOrder);

    public static CompileResult Compile(
        string skillId,
        int skillMaxLevel,
        SkillTreeJson? json,
        IReadOnlyList<AbilityEffectJson> rootEffects,
        IHookRegistry hooks,
        IAbilityActionRegistry actions,
        CollectionIndex collections,
        ref int sourceOrder)
    {
        CompileResult result = new();
        if (json?.nodes == null || json.nodes.Length == 0)
        {
            return result;
        }

        Dictionary<string, SkillTreeNodeDef> byId = new(StringComparer.OrdinalIgnoreCase);
        List<SkillTreeNodeDef> nodes = new();

        foreach (SkillTreeNodeJson row in json.nodes)
        {
            if (string.IsNullOrWhiteSpace(row.id))
            {
                result.AddError($"Skill '{skillId}' tree has a node with blank id.");
                continue;
            }

            string id = row.id.Trim();
            if (byId.ContainsKey(id))
            {
                result.AddError($"Skill '{skillId}' tree has duplicate node id '{id}'.", id);
                continue;
            }

            List<SkillTreeTierDef>? tiers = BuildTiers(
                skillId, id, row, rootEffects, hooks, actions, collections, result, ref sourceOrder);
            if (tiers == null)
            {
                continue;
            }

            bool tiersValid = true;
            for (int t = 0; t < tiers.Count; t++)
            {
                SkillTreeTierDef tier = tiers[t];
                if (tier.Cost < 0)
                {
                    result.AddError(
                        $"Skill '{skillId}' node '{id}' tier {t + 1} has negative cost.",
                        id);
                    tiersValid = false;
                }

                if (tier.MinSkillLevel < 0 || tier.MinSkillLevel > skillMaxLevel)
                {
                    result.AddError(
                        $"Skill '{skillId}' node '{id}' tier {t + 1} minSkillLevel {tier.MinSkillLevel} is outside 0..{skillMaxLevel}.",
                        id);
                    tiersValid = false;
                }

                if (t > 0 && tier.MinSkillLevel < tiers[t - 1].MinSkillLevel)
                {
                    result.Warnings.Add(
                        $"Skill '{skillId}' node '{id}' tier {t + 1} requires a lower skill level than the tier before it.");
                }
            }

            if (!tiersValid)
            {
                continue;
            }

            if (row.specialization && tiers.Count != 1)
            {
                result.AddError(
                    $"Skill '{skillId}' node '{id}' is a specialization and must declare exactly one tier.",
                    id);
                continue;
            }

            List<string> requireErrors = new();
            if (!TryParseRequireGroups(
                    skillId,
                    id,
                    row.requires,
                    out List<RequireGroup> requireGroups,
                    out List<string> allRequirementIds,
                    requireErrors))
            {
                foreach (string err in requireErrors)
                {
                    result.AddError(err, id);
                }

                continue;
            }

            List<string> excludes = new();
            HashSet<string> excludeSeen = new(StringComparer.OrdinalIgnoreCase);
            bool excludesValid = true;
            foreach (string raw in row.excludes ?? Array.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(raw))
                {
                    continue;
                }

                string excl = raw.Trim();
                if (string.Equals(excl, id, StringComparison.OrdinalIgnoreCase))
                {
                    result.AddError(
                        $"Skill '{skillId}' node '{id}' cannot exclude itself.",
                        id);
                    excludesValid = false;
                    continue;
                }

                if (excludeSeen.Add(excl))
                {
                    excludes.Add(excl);
                }
            }

            if (!excludesValid)
            {
                continue;
            }

            int layerOffset = 1;
            int? columnBias = null;
            bool compact = true;
            if (row.layout != null)
            {
                if (row.layout.layerOffset != null)
                {
                    int authored = row.layout.layerOffset.Value;
                    if (authored is not (0 or 1))
                    {
                        result.AddError(
                            $"Skill '{skillId}' node '{id}' layout.layerOffset must be 0 or 1 (got {authored}).",
                            id);
                        continue;
                    }

                    layerOffset = authored;
                }

                if (!string.IsNullOrWhiteSpace(row.layout.columnBias))
                {
                    string bias = row.layout.columnBias.Trim().ToLowerInvariant();
                    if (bias == "left")
                    {
                        columnBias = -1;
                    }
                    else if (bias == "right")
                    {
                        columnBias = 1;
                    }
                    else
                    {
                        result.AddError(
                            $"Skill '{skillId}' node '{id}' layout.columnBias must be 'left' or 'right' (got '{row.layout.columnBias}').",
                            id);
                        continue;
                    }
                }

                if (row.layout.compact != null)
                {
                    compact = row.layout.compact.Value;
                }
            }

            if (requireGroups.Count == 0 && layerOffset != 1 && row.layout?.layerOffset != null)
            {
                result.Warnings.Add(
                    $"Skill '{skillId}' node '{id}' is a root; layout.layerOffset is ignored.");
            }

            if (requireGroups.Count == 0 && columnBias != null)
            {
                result.Warnings.Add(
                    $"Skill '{skillId}' node '{id}' is a root; layout.columnBias is ignored.");
            }

            SkillTreeNodeDef node = new()
            {
                Id = id,
                NameLang = string.IsNullOrWhiteSpace(row.nameLang)
                    ? DefaultUnlockNameLang(skillId, id)
                    : row.nameLang.Trim(),
                Icon = row.icon?.Trim() ?? "",
                DeclarationOrder = nodes.Count,
                Tiers = tiers,
                RequireGroups = requireGroups,
                AllRequirementIds = allRequirementIds,
                Excludes = excludes,
                IsSpecialization = row.specialization,
                LayerOffset = layerOffset,
                ColumnBias = columnBias,
                Compact = compact
            };
            byId[id] = node;
            nodes.Add(node);
        }

        if (result.Errors.Count > 0)
        {
            return result;
        }

        foreach (SkillTreeNodeDef node in nodes)
        {
            foreach (string req in node.AllRequirementIds)
            {
                if (!byId.ContainsKey(req))
                {
                    result.AddError(
                        $"Skill '{skillId}' node '{node.Id}' requires unknown node '{req}'.",
                        node.Id);
                }
            }

            foreach (string excl in node.Excludes)
            {
                if (!byId.ContainsKey(excl))
                {
                    result.AddError(
                        $"Skill '{skillId}' node '{node.Id}' excludes unknown node '{excl}'.",
                        node.Id);
                }
            }
        }

        if (result.Errors.Count > 0)
        {
            return result;
        }

        SymmetrizeExcludes(skillId, nodes, result.Warnings);

        // Wire parent/child links from the union of requirement alternatives.
        foreach (SkillTreeNodeDef node in nodes)
        {
            List<SkillTreeNodeDef> parents = new();
            foreach (string req in node.AllRequirementIds)
            {
                parents.Add(byId[req]);
            }

            node.Parents = parents;
        }

        foreach (SkillTreeNodeDef node in nodes)
        {
            List<SkillTreeNodeDef> children = nodes
                .Where(n => n.AllRequirementIds.Any(
                    r => string.Equals(r, node.Id, StringComparison.OrdinalIgnoreCase)))
                .ToList();
            node.Children = children;
        }

        if (HasCycle(nodes, out string cycleHint, out string[] cycleNodes))
        {
            result.AddError(
                $"Skill '{skillId}' tree has a dependency cycle ({cycleHint}).",
                cycleNodes);
            return result;
        }

        AssignLayers(nodes);

        foreach (SkillTreeNodeDef parent in nodes)
        {
            List<SkillTreeNodeDef> downwardChildren = parent.Children
                .Where(c => c.Layer == parent.Layer + 1)
                .ToList();
            if (downwardChildren.Count > 3)
            {
                result.AddError(
                    $"Skill '{skillId}' node '{parent.Id}' has {downwardChildren.Count} downward children; the visual grid supports at most 3 (left, center, right).",
                    downwardChildren.Select(c => c.Id).ToArray());
            }

            foreach (SkillTreeNodeDef child in parent.Children)
            {
                if (child.Layer == parent.Layer)
                {
                    if (child.LayerOffset != 0)
                    {
                        result.AddError(
                            $"Skill '{skillId}' edge '{parent.Id}' -> '{child.Id}' is same-layer but layout.layerOffset is not 0.",
                            child.Id);
                    }

                    continue;
                }

                if (child.Layer == parent.Layer + 1)
                {
                    continue;
                }

                result.AddError(
                    $"Skill '{skillId}' edge '{parent.Id}' -> '{child.Id}' skips a visual layer; every prerequisite must be adjacent or same-layer.",
                    child.Id);
            }
        }

        if (result.Errors.Count > 0)
        {
            return result;
        }

        // Warn about nodes unreachable from any root (requires non-empty but still valid via other roots).
        HashSet<string> reachable = new(StringComparer.OrdinalIgnoreCase);
        Queue<SkillTreeNodeDef> q = new();
        foreach (SkillTreeNodeDef root in nodes.Where(n => n.IsRoot))
        {
            q.Enqueue(root);
            reachable.Add(root.Id);
        }

        while (q.Count > 0)
        {
            SkillTreeNodeDef cur = q.Dequeue();
            foreach (SkillTreeNodeDef child in cur.Children)
            {
                if (reachable.Add(child.Id))
                {
                    q.Enqueue(child);
                }
            }
        }

        foreach (SkillTreeNodeDef node in nodes)
        {
            if (!reachable.Contains(node.Id))
            {
                result.Warnings.Add(
                    $"Skill '{skillId}' node '{node.Id}' is disconnected from root nodes.");
            }
        }

        AssignOrders(nodes);
        if (!TryAssignGridColumns(nodes, out string gridError, out string[] gridBlame))
        {
            result.AddError(
                $"Skill '{skillId}' cannot be placed on the visual grid: {gridError}",
                gridBlame);
            return result;
        }

        int maxLayer = nodes.Count == 0 ? 0 : nodes.Max(n => n.Layer);
        int maxOrder = nodes.Count == 0 ? 0 : nodes.Max(n => n.Order);
        int columnSpan = ComputeColumnSpan(nodes);

        result.Tree = new SkillTreeDef
        {
            Nodes = nodes.OrderBy(n => n.Layer).ThenBy(n => n.Order).ThenBy(n => n.Id, StringComparer.OrdinalIgnoreCase).ToList(),
            ById = byId,
            MaxLayer = maxLayer,
            MaxOrder = maxOrder,
            ColumnSpan = columnSpan,
            LayoutWidth = ComputeLayoutWidth(columnSpan)
        };
        return result;
    }

    public const double LayoutGridStep = 80;
    public const double LayoutNodeSize = 72;

    static int ComputeColumnSpan(List<SkillTreeNodeDef> nodes)
    {
        if (nodes.Count == 0)
        {
            return 0;
        }

        return nodes
            .GroupBy(n => n.Layer)
            .Select(g => g.Max(n => n.GridColumn) - g.Min(n => n.GridColumn) + 1)
            .Max();
    }

    static double ComputeLayoutWidth(int columnSpan) =>
        columnSpan <= 0 ? 0 : (columnSpan - 1) * LayoutGridStep + LayoutNodeSize;

    static List<SkillTreeTierDef>? BuildTiers(
        string skillId,
        string nodeId,
        SkillTreeNodeJson row,
        IReadOnlyList<AbilityEffectJson> rootEffects,
        IHookRegistry hooks,
        IAbilityActionRegistry actions,
        CollectionIndex collections,
        CompileResult result,
        ref int sourceOrder)
    {
        string description = row.descriptionLang?.Trim() ?? "";
        if (row.tiers == null || row.tiers.Length == 0)
        {
            if (row.descriptionParams is { Length: > 0 })
            {
                result.AddError(
                    $"Skill '{skillId}' node '{nodeId}': descriptionParams requires tier effects.",
                    nodeId);
                return null;
            }

            if (row.totalParams is { Length: > 0 })
            {
                result.AddError(
                    $"Skill '{skillId}' node '{nodeId}': totalParams requires tier effects.",
                    nodeId);
                return null;
            }

            return new List<SkillTreeTierDef>
            {
                new()
                {
                    DescriptionLang = description,
                    Cost = row.cost,
                    MinSkillLevel = row.minSkillLevel
                }
            };
        }

        List<SkillTreeTierDef> tiers = new();
        AbilityEffectJson[] previousEffects = Array.Empty<AbilityEffectJson>();
        for (int i = 0; i < row.tiers.Length; i++)
        {
            SkillTreeTierJson tierRow = row.tiers[i];
            List<string> localErrors = new();
            AbilityEffectJson[]? effects = ResolveReplicatedEffects(
                skillId,
                nodeId,
                i,
                tierRow.effects,
                previousEffects,
                localErrors);
            if (effects == null)
            {
                foreach (string err in localErrors)
                {
                    result.AddError(err, nodeId);
                }

                return null;
            }

            localErrors.Clear();
            List<AbilityRule>? rules = AbilityRuleCompiler.CompileEffects(
                skillId,
                nodeId,
                i + 1,
                effects,
                hooks,
                actions,
                collections,
                localErrors,
                ref sourceOrder);
            if (rules == null)
            {
                foreach (string err in localErrors)
                {
                    result.AddError(err, nodeId);
                }

                return null;
            }

            localErrors.Clear();
            JToken[] totalParams = tierRow.totalParams ?? row.totalParams ?? [];
            IReadOnlyList<TotalParamSpec>? totalSpecs = ResolveTotalParams(
                skillId,
                nodeId,
                i,
                totalParams,
                rules,
                localErrors);
            if (totalSpecs == null)
            {
                foreach (string err in localErrors)
                {
                    result.AddError(err, nodeId);
                }

                return null;
            }

            localErrors.Clear();
            JToken[] descriptionParams = tierRow.descriptionParams ?? row.descriptionParams ?? [];
            IReadOnlyList<object>? descriptionArgs = ResolveDescriptionArgs(
                skillId,
                nodeId,
                i,
                descriptionParams,
                rootEffects,
                previousEffects,
                effects,
                localErrors);
            if (descriptionArgs == null)
            {
                foreach (string err in localErrors)
                {
                    result.AddError(err, nodeId);
                }

                return null;
            }

            tiers.Add(new SkillTreeTierDef
            {
                DescriptionLang = string.IsNullOrWhiteSpace(tierRow.descriptionLang)
                    ? description
                    : tierRow.descriptionLang.Trim(),
                DescriptionArgs = descriptionArgs,
                Cost = tierRow.cost ?? row.cost,
                MinSkillLevel = tierRow.minSkillLevel ?? row.minSkillLevel,
                Rules = rules,
                EffectSnapshots = SkillDescriptionResolver.SnapshotEffects(effects),
                TotalParams = totalSpecs
            });
            previousEffects = effects;
        }

        return tiers;
    }

    static IReadOnlyList<TotalParamSpec>? ResolveTotalParams(
        string skillId,
        string nodeId,
        int tierIndex,
        IReadOnlyList<JToken> declarations,
        IReadOnlyList<AbilityRule> rules,
        List<string> errors)
    {
        if (declarations.Count == 0)
        {
            return Array.Empty<TotalParamSpec>();
        }

        List<TotalParamSpec> specs = new(declarations.Count);
        bool valid = true;
        foreach (JToken declaration in declarations)
        {
            if (declaration is not JObject expression)
            {
                errors.Add(
                    $"Skill '{skillId}' node '{nodeId}' tier {tierIndex + 1}: totalParams entries must be objects with target and format.");
                valid = false;
                continue;
            }

            JToken? targetToken = expression["target"];
            if (targetToken == null || targetToken.Type != JTokenType.Integer)
            {
                errors.Add(
                    $"Skill '{skillId}' node '{nodeId}' tier {tierIndex + 1}: totalParams target must be an effect index.");
                valid = false;
                continue;
            }

            int target = targetToken.Value<int>();
            if (target < 0 || target >= rules.Count)
            {
                errors.Add(
                    $"Skill '{skillId}' node '{nodeId}' tier {tierIndex + 1}: totalParams target {target} is outside this tier's effects.");
                valid = false;
                continue;
            }

            string format = expression.Value<string>("format")?.Trim() ?? "";
            if (format is not ("percent" or "fractionPercent"))
            {
                errors.Add(
                    $"Skill '{skillId}' node '{nodeId}' tier {tierIndex + 1}: totalParams format must be percent or fractionPercent.");
                valid = false;
                continue;
            }

            if (rules[target].Parameters is not NumberSpec)
            {
                errors.Add(
                    $"Skill '{skillId}' node '{nodeId}' tier {tierIndex + 1}: totalParams target {target} is not a number effect.");
                valid = false;
                continue;
            }

            specs.Add(new TotalParamSpec { Target = target, Format = format });
        }

        return valid ? specs : null;
    }

    static IReadOnlyList<object>? ResolveDescriptionArgs(
        string skillId,
        string nodeId,
        int tierIndex,
        IReadOnlyList<JToken> declarations,
        IReadOnlyList<AbilityEffectJson> rootEffects,
        IReadOnlyList<AbilityEffectJson> previousEffects,
        IReadOnlyList<AbilityEffectJson> effects,
        List<string> errors)
    {
        List<object> args = new(declarations.Count);
        bool valid = true;
        foreach (JToken declaration in declarations)
        {
            if (declaration.Type == JTokenType.String)
            {
                string selector = declaration.Value<string>()?.Trim() ?? "";
                if (!TryResolveDescriptionValue(
                        skillId,
                        nodeId,
                        tierIndex,
                        selector,
                        rootEffects,
                        previousEffects,
                        effects,
                        allowMissingPrevious: false,
                        errors,
                        out JToken? value))
                {
                    valid = false;
                    continue;
                }

                args.Add(ToDescriptionScalar(value!));
                continue;
            }

            if (declaration is not JObject expression)
            {
                errors.Add(
                    $"Skill '{skillId}' node '{nodeId}' tier {tierIndex + 1}: descriptionParams entries must be selectors or expression objects.");
                valid = false;
                continue;
            }

            string format = expression.Value<string>("format")?.Trim() ?? "";
            JArray? sum = expression["sum"] as JArray;
            if (sum == null || sum.Count == 0)
            {
                errors.Add(
                    $"Skill '{skillId}' node '{nodeId}' tier {tierIndex + 1}: description expression requires a non-empty sum array.");
                valid = false;
                continue;
            }

            decimal total = 0m;
            bool expressionValid = true;
            foreach (JToken operand in sum)
            {
                string selector = operand.Value<string>()?.Trim() ?? "";
                if (!TryResolveDescriptionValue(
                        skillId,
                        nodeId,
                        tierIndex,
                        selector,
                        rootEffects,
                        previousEffects,
                        effects,
                        allowMissingPrevious: true,
                        errors,
                        out JToken? value))
                {
                    expressionValid = false;
                    continue;
                }

                if (value == null)
                {
                    continue;
                }

                if (!TryToDecimal(value, out decimal number))
                {
                    errors.Add(
                        $"Skill '{skillId}' node '{nodeId}' tier {tierIndex + 1}: description sum operand '{selector}' is not numeric.");
                    expressionValid = false;
                    continue;
                }

                total += number;
            }

            if (!expressionValid)
            {
                valid = false;
                continue;
            }

            if (format.Length == 0)
            {
                args.Add(total);
            }
            else if (format is "fractionPercent" or "percent")
            {
                args.Add(new FormattedDescriptionArg { Value = total, Format = format });
            }
            else
            {
                errors.Add(
                    $"Skill '{skillId}' node '{nodeId}' tier {tierIndex + 1}: unknown description format '{format}'.");
                valid = false;
            }
        }

        return valid ? args : null;
    }

    static bool TryResolveDescriptionValue(
        string skillId,
        string nodeId,
        int tierIndex,
        string selector,
        IReadOnlyList<AbilityEffectJson> rootEffects,
        IReadOnlyList<AbilityEffectJson> previousEffects,
        IReadOnlyList<AbilityEffectJson> currentEffects,
        bool allowMissingPrevious,
        List<string> errors,
        out JToken? value)
    {
        value = null;
        if (selector.Length == 0)
        {
            errors.Add(
                $"Skill '{skillId}' node '{nodeId}' tier {tierIndex + 1}: descriptionParams contains a blank selector.");
            return false;
        }

        IReadOnlyList<AbilityEffectJson> source = currentEffects;
        string scopedSelector = selector;
        bool previousScope = false;
        int scopeDot = selector.IndexOf('.');
        if (scopeDot > 0)
        {
            string scope = selector[..scopeDot];
            if (scope.Equals("root", StringComparison.OrdinalIgnoreCase))
            {
                source = rootEffects;
                scopedSelector = selector[(scopeDot + 1)..];
            }
            else if (scope.Equals("prev", StringComparison.OrdinalIgnoreCase))
            {
                source = previousEffects;
                scopedSelector = selector[(scopeDot + 1)..];
                previousScope = true;
            }
            else if (scope.Equals("current", StringComparison.OrdinalIgnoreCase))
            {
                scopedSelector = selector[(scopeDot + 1)..];
            }
        }

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

        if (matches.Count == 1)
        {
            value = matches[0];
            return true;
        }

        if (matches.Count == 0
            && previousScope
            && allowMissingPrevious
            && previousEffects.Count == 0)
        {
            return true;
        }

        string reason = matches.Count == 0 ? "was not found" : "is ambiguous";
        errors.Add(
            $"Skill '{skillId}' node '{nodeId}' tier {tierIndex + 1}: description param '{selector}' {reason}; use '<scope>.<effect-index>.<param>' when needed.");
        return false;
    }

    /// <summary>
    /// Resolves a dotted path into effect params (e.g. chance.base, chance.params.base).
    /// </summary>
    static bool TryGetParamPath(JObject? parameters, string path, out JToken token)
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

    static object ToDescriptionScalar(JToken value) =>
        value is JValue scalar
            ? scalar.Value ?? ""
            : value.ToString(Newtonsoft.Json.Formatting.None);

    static bool TryToDecimal(JToken value, out decimal number)
    {
        if (value is JValue scalar && scalar.Value != null)
        {
            try
            {
                number = Convert.ToDecimal(
                    scalar.Value,
                    System.Globalization.CultureInfo.InvariantCulture);
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

    static AbilityEffectJson[]? ResolveReplicatedEffects(
        string skillId,
        string nodeId,
        int tierIndex,
        AbilityEffectJson[]? effects,
        AbilityEffectJson[] previousEffects,
        List<string> errors)
    {
        if (effects == null || effects.Length == 0)
        {
            return Array.Empty<AbilityEffectJson>();
        }

        AbilityEffectJson[] resolved = new AbilityEffectJson[effects.Length];
        bool valid = true;
        for (int effectIndex = 0; effectIndex < effects.Length; effectIndex++)
        {
            AbilityEffectJson effect = effects[effectIndex];
            if (!effect.replicate.HasValue)
            {
                resolved[effectIndex] = CloneEffect(effect);
                continue;
            }

            int sourceIndex = effect.replicate.Value;
            if (tierIndex == 0)
            {
                errors.Add(
                    $"Skill '{skillId}' node '{nodeId}' tier 1 effect {effectIndex + 1}: replicate cannot be used because there is no previous tier.");
                valid = false;
                continue;
            }

            if (sourceIndex < 0 || sourceIndex >= previousEffects.Length)
            {
                errors.Add(
                    $"Skill '{skillId}' node '{nodeId}' tier {tierIndex + 1} effect {effectIndex + 1}: replicate index {sourceIndex} is invalid; the previous tier has {previousEffects.Length} effect(s).");
                valid = false;
                continue;
            }

            resolved[effectIndex] = OverlayEffect(previousEffects[sourceIndex], effect);
        }

        return valid ? resolved : null;
    }

    static AbilityEffectJson OverlayEffect(AbilityEffectJson inherited, AbilityEffectJson overlay)
    {
        Newtonsoft.Json.Linq.JObject? parameters =
            inherited.@params == null
                ? null
                : (Newtonsoft.Json.Linq.JObject)inherited.@params.DeepClone();
        if (overlay.@params != null)
        {
            parameters ??= new Newtonsoft.Json.Linq.JObject();
            parameters.Merge(
                overlay.@params,
                new Newtonsoft.Json.Linq.JsonMergeSettings
                {
                    MergeArrayHandling = Newtonsoft.Json.Linq.MergeArrayHandling.Replace
                });
        }

        return new AbilityEffectJson
        {
            type = overlay.type ?? inherited.type,
            hook = overlay.hook ?? inherited.hook,
            verb = overlay.verb ?? inherited.verb,
            phase = overlay.phase ?? inherited.phase,
            action = overlay.action ?? inherited.action,
            when = overlay.when == null
                ? CloneWhen(inherited.when)
                : OverlayWhen(inherited.when, overlay.when),
            @params = parameters,
            priority = overlay.priority ?? inherited.priority
        };
    }

    static AbilityEffectJson CloneEffect(AbilityEffectJson effect) =>
        new()
        {
            type = effect.type,
            hook = effect.hook,
            verb = effect.verb,
            phase = effect.phase,
            action = effect.action,
            when = CloneWhen(effect.when),
            @params = effect.@params == null
                ? null
                : (Newtonsoft.Json.Linq.JObject)effect.@params.DeepClone(),
            priority = effect.priority
        };

    static AbilityWhenJson? OverlayWhen(AbilityWhenJson? inherited, AbilityWhenJson overlay) =>
        new()
        {
            tags = overlay.tags ?? inherited?.tags?.ToArray()
        };

    static AbilityWhenJson? CloneWhen(AbilityWhenJson? when) =>
        when == null
            ? null
            : new AbilityWhenJson
            {
                tags = when.tags?.ToArray()
            };

    internal static bool TryParseRequireGroups(
        string skillId,
        string nodeId,
        JToken[]? rawRequires,
        out List<RequireGroup> groups,
        out List<string> allIds,
        List<string> errors)
    {
        groups = new();
        allIds = new();
        HashSet<string> seenIds = new(StringComparer.OrdinalIgnoreCase);
        if (rawRequires == null || rawRequires.Length == 0)
        {
            return true;
        }

        bool ok = true;
        for (int i = 0; i < rawRequires.Length; i++)
        {
            JToken token = rawRequires[i];
            List<string> alternatives = new();
            if (token == null || token.Type == JTokenType.Null)
            {
                errors.Add(
                    $"Skill '{skillId}' node '{nodeId}' requires entry {i + 1} is null.");
                ok = false;
                continue;
            }

            if (token.Type == JTokenType.String)
            {
                string id = token.Value<string>()?.Trim() ?? "";
                if (id.Length == 0)
                {
                    continue;
                }

                alternatives.Add(id);
            }
            else if (token is JArray array)
            {
                if (array.Count == 0)
                {
                    errors.Add(
                        $"Skill '{skillId}' node '{nodeId}' requires entry {i + 1} is an empty OR group.");
                    ok = false;
                    continue;
                }

                foreach (JToken alt in array)
                {
                    if (alt == null || alt.Type == JTokenType.Null)
                    {
                        errors.Add(
                            $"Skill '{skillId}' node '{nodeId}' requires entry {i + 1} contains a null alternative.");
                        ok = false;
                        continue;
                    }

                    if (alt.Type != JTokenType.String)
                    {
                        errors.Add(
                            $"Skill '{skillId}' node '{nodeId}' requires entry {i + 1} alternatives must be node id strings.");
                        ok = false;
                        continue;
                    }

                    string id = alt.Value<string>()?.Trim() ?? "";
                    if (id.Length == 0)
                    {
                        continue;
                    }

                    if (!alternatives.Contains(id, StringComparer.OrdinalIgnoreCase))
                    {
                        alternatives.Add(id);
                    }
                }

                if (alternatives.Count == 0)
                {
                    errors.Add(
                        $"Skill '{skillId}' node '{nodeId}' requires entry {i + 1} is an empty OR group.");
                    ok = false;
                    continue;
                }
            }
            else
            {
                errors.Add(
                    $"Skill '{skillId}' node '{nodeId}' requires entry {i + 1} must be a node id or an array of node ids.");
                ok = false;
                continue;
            }

            foreach (string alt in alternatives)
            {
                if (string.Equals(alt, nodeId, StringComparison.OrdinalIgnoreCase))
                {
                    errors.Add(
                        $"Skill '{skillId}' node '{nodeId}' cannot require itself.");
                    ok = false;
                }

                if (seenIds.Add(alt))
                {
                    allIds.Add(alt);
                }
            }

            groups.Add(new RequireGroup { Alternatives = alternatives });
        }

        return ok;
    }

    static void SymmetrizeExcludes(
        string skillId,
        List<SkillTreeNodeDef> nodes,
        List<string> warnings)
    {
        Dictionary<string, HashSet<string>> map = new(StringComparer.OrdinalIgnoreCase);
        foreach (SkillTreeNodeDef node in nodes)
        {
            HashSet<string> set = new(node.Excludes, StringComparer.OrdinalIgnoreCase);
            map[node.Id] = set;
        }

        foreach (SkillTreeNodeDef node in nodes)
        {
            foreach (string excl in node.Excludes.ToArray())
            {
                if (!map.TryGetValue(excl, out HashSet<string>? peer))
                {
                    continue;
                }

                if (peer.Add(node.Id))
                {
                    warnings.Add(
                        $"Skill '{skillId}' node '{excl}' excludes '{node.Id}' was added to symmetrize '{node.Id}' excludes '{excl}'.");
                }
            }
        }

        foreach (SkillTreeNodeDef node in nodes)
        {
            node.Excludes = map[node.Id].OrderBy(id => id, StringComparer.OrdinalIgnoreCase).ToArray();
        }
    }

    /// <summary>
    /// Target rows with this many nodes or more will not initiate another upward pull.
    /// Cascaded descendants may still push a row past this count.
    /// </summary>
    const int ShakeOutPullThreshold = 4;

    static void AssignLayers(List<SkillTreeNodeDef> nodes)
    {
        SeedLayers(nodes);
        ShakeOutLayers(nodes);
        DenseRenumberLayers(nodes);
    }

    static void SeedLayers(List<SkillTreeNodeDef> nodes)
    {
        // Parentage wins: dependents default to the row after their deepest parent (layerOffset 1).
        // layout.layerOffset 0 keeps them on the parent's row for a horizontal link.
        // minSkillLevel only bands root / disconnected nodes (no parents), so independent
        // unlocks at different levels can still share the canvas without inventing fake edges.
        Dictionary<int, int> rootLevelBands = nodes
            .Where(n => n.Parents.Count == 0)
            .Select(n => n.Tiers.Min(t => t.MinSkillLevel))
            .Distinct()
            .OrderBy(level => level)
            .Select((level, index) => new { level, index })
            .ToDictionary(pair => pair.level, pair => pair.index);

        List<SkillTreeNodeDef> topo = TopologicalSort(nodes);
        foreach (SkillTreeNodeDef node in topo)
        {
            if (node.Parents.Count > 0)
            {
                node.Layer = node.Parents.Max(p => p.Layer) + node.LayerOffset;
                continue;
            }

            int minimumLevel = node.Tiers.Min(t => t.MinSkillLevel);
            node.Layer = rootLevelBands.TryGetValue(minimumLevel, out int band) ? band : 0;
        }
    }

    static void ShakeOutLayers(List<SkillTreeNodeDef> nodes)
    {
        if (nodes.Count == 0)
        {
            return;
        }

        bool progressed;
        do
        {
            progressed = false;
            int maxLayer = nodes.Max(n => n.Layer);
            for (int target = 0; target < maxLayer; target++)
            {
                while (true)
                {
                    int occupied = nodes.Count(n => n.Layer == target);
                    if (occupied >= ShakeOutPullThreshold)
                    {
                        break;
                    }

                    SkillTreeNodeDef? candidate = nodes
                        .Where(n => n.Layer > target && n.Compact && CanPullToLayer(n, target, nodes))
                        .OrderBy(n => n.DeclarationOrder)
                        .ThenBy(n => n.Id, StringComparer.OrdinalIgnoreCase)
                        .FirstOrDefault();
                    if (candidate == null)
                    {
                        break;
                    }

                    PullNodeToLayer(candidate, target, nodes);
                    progressed = true;
                    maxLayer = nodes.Max(n => n.Layer);
                }
            }
        }
        while (progressed);
    }

    static bool CanPullToLayer(SkillTreeNodeDef node, int target, List<SkillTreeNodeDef> nodes)
    {
        if (target < 0 || target >= node.Layer)
        {
            return false;
        }

        // Trial placement: move this node, then recompute dependents from parentage.
        Dictionary<string, int> trial = nodes.ToDictionary(
            n => n.Id,
            n => n.Layer,
            StringComparer.OrdinalIgnoreCase);
        trial[node.Id] = target;
        RecomputeDependentLayers(trial, nodes);

        if (!TrialLayersValid(trial, nodes))
        {
            return false;
        }

        // A pull must actually raise this node (dependents may cascade with it).
        return trial[node.Id] == target && target < node.Layer;
    }

    static void PullNodeToLayer(SkillTreeNodeDef node, int target, List<SkillTreeNodeDef> nodes)
    {
        Dictionary<string, int> trial = nodes.ToDictionary(
            n => n.Id,
            n => n.Layer,
            StringComparer.OrdinalIgnoreCase);
        trial[node.Id] = target;
        RecomputeDependentLayers(trial, nodes);
        foreach (SkillTreeNodeDef n in nodes)
        {
            n.Layer = trial[n.Id];
        }
    }

    static void RecomputeDependentLayers(Dictionary<string, int> layers, List<SkillTreeNodeDef> nodes)
    {
        foreach (SkillTreeNodeDef node in TopologicalSort(nodes))
        {
            if (node.Parents.Count == 0)
            {
                continue;
            }

            layers[node.Id] = node.Parents.Max(p => layers[p.Id]) + node.LayerOffset;
        }
    }

    static bool TrialLayersValid(Dictionary<string, int> layers, List<SkillTreeNodeDef> nodes)
    {
        foreach (SkillTreeNodeDef node in nodes)
        {
            foreach (SkillTreeNodeDef parent in node.Parents)
            {
                int delta = layers[node.Id] - layers[parent.Id];
                if (delta is not (0 or 1))
                {
                    return false;
                }

                if (delta == 0 && node.LayerOffset != 0)
                {
                    return false;
                }
            }
        }

        foreach (SkillTreeNodeDef parent in nodes)
        {
            int downward = parent.Children.Count(c => layers[c.Id] == layers[parent.Id] + 1);
            if (downward > 3)
            {
                return false;
            }
        }

        return true;
    }

    static void DenseRenumberLayers(List<SkillTreeNodeDef> nodes)
    {
        if (nodes.Count == 0)
        {
            return;
        }

        List<int> used = nodes.Select(n => n.Layer).Distinct().OrderBy(layer => layer).ToList();
        Dictionary<int, int> map = used
            .Select((layer, index) => new { layer, index })
            .ToDictionary(pair => pair.layer, pair => pair.index);
        foreach (SkillTreeNodeDef node in nodes)
        {
            node.Layer = map[node.Layer];
        }
    }

    static void AssignOrders(List<SkillTreeNodeDef> nodes)
    {
        Dictionary<int, List<SkillTreeNodeDef>> byLayer = nodes
            .GroupBy(n => n.Layer)
            .ToDictionary(g => g.Key, g => g.OrderBy(n => n.DeclarationOrder).ToList());

        // Declaration order is the author-provided horizontal layout hint.
        // Keep it authoritative within each layer; automatic column placement may choose
        // exact columns but must not silently reorder authored nodes.
        foreach (List<SkillTreeNodeDef> layerNodes in byLayer.Values)
        {
            for (int i = 0; i < layerNodes.Count; i++)
            {
                layerNodes[i].Order = i;
            }
        }
    }

    static bool TryAssignGridColumns(
        List<SkillTreeNodeDef> nodes,
        out string error,
        out string[] blamedNodeIds)
    {
        error = "";
        blamedNodeIds = Array.Empty<string>();
        if (nodes.Count == 0)
        {
            return true;
        }

        Dictionary<int, List<SkillTreeNodeDef>> byLayer = nodes
            .GroupBy(n => n.Layer)
            .ToDictionary(g => g.Key, g => g.OrderBy(n => n.Order).ThenBy(n => n.Id, StringComparer.OrdinalIgnoreCase).ToList());

        int maxLayer = byLayer.Keys.Max();
        for (int layer = 0; layer <= maxLayer; layer++)
        {
            if (!byLayer.TryGetValue(layer, out List<SkillTreeNodeDef>? layerNodes))
            {
                continue;
            }

            // Downward dependents first (parents already placed on earlier layers), then roots,
            // then same-layer dependents that sit beside a parent on this row.
            HashSet<int> occupied = new();
            // Placement walks left to right, so sequence dependents by where their parents
            // already sit. Declaration order only breaks ties; using it as the primary key
            // would reject valid trees whose authored array order differs from parent order.
            List<SkillTreeNodeDef> downward = layerNodes
                .Where(n => n.Parents.Count > 0 && n.Parents.All(p => p.Layer < layer))
                .OrderBy(n => n.Parents.Average(p => p.GridColumn))
                .ThenBy(n => n.Order)
                .ToList();
            List<SkillTreeNodeDef> sameLayer = layerNodes
                .Where(n => n.Parents.Count > 0 && n.Parents.Any(p => p.Layer == layer))
                .ToList();

            if (!TryAssignDependentColumns(
                    downward, 0, occupied, out string? downwardError, out string[]? downwardBlame))
            {
                error = $"layer {layer}: {downwardError}";
                blamedNodeIds = downwardBlame ?? downward.Select(n => n.Id).ToArray();
                return false;
            }

            PlaceIndependentColumns(layerNodes, occupied);
            if (!TryPlaceSameLayerDependents(
                    sameLayer, occupied, out string? sameLayerError, out string[]? sameBlame))
            {
                error = $"layer {layer}: {sameLayerError}";
                blamedNodeIds = sameBlame ?? sameLayer.Select(n => n.Id).ToArray();
                return false;
            }
        }

        return true;
    }

    static bool TryAssignDependentColumns(
        List<SkillTreeNodeDef> layerNodes,
        int index,
        HashSet<int> occupied,
        out string? error,
        out string[]? blamedNodeIds)
    {
        error = null;
        blamedNodeIds = null;
        if (layerNodes.Count == 0)
        {
            return true;
        }

        if (index >= layerNodes.Count)
        {
            if (ParentsHaveValidDownwardSlots(layerNodes))
            {
                return true;
            }

            error =
                $"downward children [{FormatNodeList(layerNodes)}] leave a parent without a legal left/center/right slot.";
            blamedNodeIds = layerNodes.Select(n => n.Id).ToArray();
            return false;
        }

        SkillTreeNodeDef node = layerNodes[index];
        int minimum = node.Parents.Max(p => p.GridColumn - 1);
        int maximum = node.Parents.Min(p => p.GridColumn + 1);
        string parents = FormatParents(node);
        if (minimum > maximum)
        {
            error =
                $"node '{node.Id}' has no legal column — parents are more than 2 columns apart ({parents}).";
            blamedNodeIds = [node.Id];
            return false;
        }

        double barycenter = node.Parents.Average(p => p.GridColumn);
        IEnumerable<int> range = Enumerable.Range(minimum, maximum - minimum + 1);
        IEnumerable<int> candidates = node.ColumnBias switch
        {
            < 0 => range.OrderBy(column => column),
            > 0 => range.OrderByDescending(column => column),
            _ => range
                .OrderBy(column => Math.Abs(column - barycenter))
                .ThenBy(column => column)
        };

        List<int> tried = new();
        foreach (int column in candidates)
        {
            // Dependents on a layer stay in the sequence order they were placed in, so
            // edges never cross.
            if (index > 0 && column <= layerNodes[index - 1].GridColumn)
            {
                continue;
            }

            if (!occupied.Add(column))
            {
                continue;
            }

            tried.Add(column);
            node.GridColumn = column;
            if (TryAssignDependentColumns(layerNodes, index + 1, occupied, out error, out blamedNodeIds))
            {
                return true;
            }

            occupied.Remove(column);
        }

        string previous = index > 0
            ? $" must sit right of '{layerNodes[index - 1].Id}'@C{layerNodes[index - 1].GridColumn};"
            : "";
        string triedText = tried.Count == 0 ? "none" : string.Join(", ", tried);
        error =
            $"could not place '{node.Id}' (parents {parents}; legal columns {minimum}..{maximum};{previous} tried [{triedText}]). "
            + $"Remaining downward: [{FormatNodeList(layerNodes.Skip(index))}].";
        blamedNodeIds = layerNodes.Skip(index).Select(n => n.Id).ToArray();
        if (blamedNodeIds.Length == 0)
        {
            blamedNodeIds = [node.Id];
        }

        return false;
    }

    /// <summary>
    /// Nodes without prerequisites are packed contiguously in declaration order.
    /// Dependent placement supplies any needed diagonal spread on the following layer.
    /// </summary>
    static void PlaceIndependentColumns(List<SkillTreeNodeDef> layerNodes, HashSet<int> occupied)
    {
        List<SkillTreeNodeDef> free = layerNodes.Where(n => n.Parents.Count == 0).ToList();
        if (free.Count == 0)
        {
            return;
        }

        int mid = (free.Count - 1) / 2;
        for (int i = 0; i < free.Count; i++)
        {
            int column = i - mid;
            while (!occupied.Add(column))
            {
                column++;
            }

            free[i].GridColumn = column;
        }
    }

    /// <summary>
    /// Places same-row dependents beside a same-layer parent. Default prefers the parent's
    /// right, then left, then outward. columnBias left/right swaps the first preference.
    /// </summary>
    static bool TryPlaceSameLayerDependents(
        List<SkillTreeNodeDef> sameLayer,
        HashSet<int> occupied,
        out string? error,
        out string[]? blamedNodeIds)
    {
        error = null;
        blamedNodeIds = null;
        foreach (SkillTreeNodeDef node in sameLayer)
        {
            SkillTreeNodeDef? parent = node.Parents.FirstOrDefault(p => p.Layer == node.Layer);
            if (parent == null)
            {
                error =
                    $"same-layer node '{node.Id}' has no parent on layer {node.Layer} ({FormatParents(node)}).";
                blamedNodeIds = [node.Id];
                return false;
            }

            bool preferLeft = node.ColumnBias < 0;
            bool placed = false;
            for (int distance = 1; distance <= sameLayer.Count + 2; distance++)
            {
                int[] sides = preferLeft
                    ? [parent.GridColumn - distance, parent.GridColumn + distance]
                    : [parent.GridColumn + distance, parent.GridColumn - distance];

                foreach (int column in sides)
                {
                    if (!occupied.Add(column))
                    {
                        continue;
                    }

                    node.GridColumn = column;
                    placed = true;
                    break;
                }

                if (placed)
                {
                    break;
                }
            }

            if (!placed)
            {
                error =
                    $"could not place same-layer node '{node.Id}' beside '{parent.Id}'@C{parent.GridColumn}; occupied [{string.Join(", ", occupied.OrderBy(c => c))}].";
                blamedNodeIds = [node.Id];
                return false;
            }
        }

        return true;
    }

    static string FormatParents(SkillTreeNodeDef node) =>
        string.Join(", ", node.Parents.Select(p => $"{p.Id}@L{p.Layer}C{p.GridColumn}"));

    static string FormatNodeList(IEnumerable<SkillTreeNodeDef> nodes) =>
        string.Join(", ", nodes.Select(n => n.Id));

    static bool ParentsHaveValidDownwardSlots(List<SkillTreeNodeDef> layerNodes)
    {
        if (layerNodes.Count == 0)
        {
            return true;
        }

        int childLayer = layerNodes[0].Layer;
        foreach (SkillTreeNodeDef parent in layerNodes.SelectMany(n => n.Parents).Distinct())
        {
            int[] offsets = parent.Children
                .Where(child => child.Layer == childLayer)
                .Select(child => child.GridColumn - parent.GridColumn)
                .OrderBy(offset => offset)
                .ToArray();

            if (offsets.Any(offset => offset < -1 || offset > 1)
                || offsets.Distinct().Count() != offsets.Length)
            {
                return false;
            }

            // Distinct offsets in [-1, 1] are all valid vertical/45-degree edges.
            // Do not require a two-way fork to straddle its parent: {0, 1} and
            // {-1, 0} are valid and allow compact many-to-one layouts.
        }

        return true;
    }

    static List<SkillTreeNodeDef> TopologicalSort(List<SkillTreeNodeDef> nodes)
    {
        Dictionary<string, int> indegree = new(StringComparer.OrdinalIgnoreCase);
        foreach (SkillTreeNodeDef node in nodes)
        {
            indegree[node.Id] = node.Parents.Count;
        }

        SortedSet<string> ready = new(StringComparer.OrdinalIgnoreCase);
        foreach (SkillTreeNodeDef node in nodes)
        {
            if (indegree[node.Id] == 0)
            {
                ready.Add(node.Id);
            }
        }

        Dictionary<string, SkillTreeNodeDef> byId = nodes.ToDictionary(
            n => n.Id,
            StringComparer.OrdinalIgnoreCase);
        List<SkillTreeNodeDef> ordered = new();
        while (ready.Count > 0)
        {
            string id = ready.Min!;
            ready.Remove(id);
            SkillTreeNodeDef node = byId[id];
            ordered.Add(node);
            foreach (SkillTreeNodeDef child in node.Children)
            {
                indegree[child.Id]--;
                if (indegree[child.Id] == 0)
                {
                    ready.Add(child.Id);
                }
            }
        }

        return ordered;
    }

    static bool HasCycle(List<SkillTreeNodeDef> nodes, out string hint, out string[] cycleNodes)
    {
        Dictionary<string, int> state = new(StringComparer.OrdinalIgnoreCase);
        foreach (SkillTreeNodeDef node in nodes)
        {
            state[node.Id] = 0;
        }

        Stack<string> path = new();
        foreach (SkillTreeNodeDef node in nodes.OrderBy(n => n.Id, StringComparer.OrdinalIgnoreCase))
        {
            if (state[node.Id] != 0)
            {
                continue;
            }

            if (DfsCycle(node, state, path, out hint, out cycleNodes))
            {
                return true;
            }
        }

        hint = "";
        cycleNodes = Array.Empty<string>();
        return false;
    }

    static bool DfsCycle(
        SkillTreeNodeDef node,
        Dictionary<string, int> state,
        Stack<string> path,
        out string hint,
        out string[] cycleNodes)
    {
        state[node.Id] = 1;
        path.Push(node.Id);
        foreach (SkillTreeNodeDef child in node.Children)
        {
            if (state[child.Id] == 1)
            {
                List<string> cycle = path.Reverse().Append(child.Id).ToList();
                hint = string.Join(" -> ", cycle);
                // Unique nodes in the cycle path (exclude the repeated closing id for blame LIFO).
                cycleNodes = cycle.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                return true;
            }

            if (state[child.Id] == 0 && DfsCycle(child, state, path, out hint, out cycleNodes))
            {
                return true;
            }
        }

        path.Pop();
        state[node.Id] = 2;
        hint = "";
        cycleNodes = Array.Empty<string>();
        return false;
    }

    /// <summary>
    /// Omitted unlock <c>nameLang</c>: <c>unlock-{skillLocal}-{nodeLocal}</c>, prefixed with
    /// the node domain (else skill domain) so <c>Lang.Get</c> resolves in the authoring mod.
    /// </summary>
    internal static string DefaultUnlockNameLang(string skillId, string nodeId)
    {
        SplitId(skillId, out string? skillDomain, out string skillLocal);
        SplitId(nodeId, out string? nodeDomain, out string nodeLocal);
        string path = "unlock-" + skillLocal + "-" + nodeLocal;
        string? domain = nodeDomain ?? skillDomain;
        return domain == null ? path : domain + ":" + path;
    }

    /// <summary>
    /// Omitted skill <c>nameLang</c>: <c>skill-{local}</c>, prefixed with the skill domain
    /// when the skill id is namespaced.
    /// </summary>
    internal static string DefaultSkillNameLang(string skillId)
    {
        SplitId(skillId, out string? domain, out string local);
        string path = "skill-" + local;
        return domain == null ? path : domain + ":" + path;
    }

    static void SplitId(string id, out string? domain, out string local)
    {
        int colon = id.IndexOf(':');
        if (colon <= 0 || colon >= id.Length - 1)
        {
            domain = null;
            local = id;
            return;
        }

        domain = id[..colon];
        local = id[(colon + 1)..].Replace(':', '-');
    }
}
