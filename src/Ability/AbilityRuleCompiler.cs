using Newtonsoft.Json.Linq;
using Prosequor.Ability.Hooks;
using Prosequor.Data;

namespace Prosequor.Ability;

/// <summary>Compiles explicit hook/verb/phase/action JSON into bound AbilityRule instances.</summary>
public static class AbilityRuleCompiler
{
    public static List<AbilityRule>? CompileEffects(
        string skillId,
        string? nodeId,
        int? tier,
        AbilityEffectJson[]? rows,
        IHookRegistry hooks,
        IAbilityActionRegistry actions,
        CollectionIndex collections,
        List<string> errors,
        ref int sourceOrder)
    {
        List<AbilityRule> rules = new();
        if (rows == null || rows.Length == 0)
        {
            return rules;
        }

        bool ok = true;
        for (int i = 0; i < rows.Length; i++)
        {
            AbilityEffectJson row = rows[i];
            string where = nodeId == null
                ? $"Skill '{skillId}' root effect {i + 1}"
                : $"Skill '{skillId}' node '{nodeId}' tier {tier} effect {i + 1}";

            if (row.replicate.HasValue)
            {
                errors.Add(
                    $"{where}: replicate must reference an effect from the immediately previous tier.");
                ok = false;
                continue;
            }

            if (!string.IsNullOrWhiteSpace(row.type))
            {
                errors.Add(
                    $"{where}: legacy effect type '{row.type}' is no longer supported; use hook/verb/phase/action.");
                ok = false;
                continue;
            }

            if (string.IsNullOrWhiteSpace(row.hook) || string.IsNullOrWhiteSpace(row.action))
            {
                errors.Add($"{where}: hook and action are required.");
                ok = false;
                continue;
            }

            if (string.IsNullOrWhiteSpace(row.verb))
            {
                errors.Add($"{where}: verb is required.");
                ok = false;
                continue;
            }

            if (!string.IsNullOrWhiteSpace(row.when?.verb))
            {
                errors.Add($"{where}: when.verb is removed; use top-level verb and when.tags for classification.");
                ok = false;
                continue;
            }

            HookId hookId = HookId.Normalize(row.hook.Trim());
            VerbId verbId = VerbId.Normalize(row.verb);
            PhaseId phaseId = string.IsNullOrWhiteSpace(row.phase)
                ? HookIds.Default
                : PhaseId.Normalize(row.phase);
            ActionId actionId = ActionId.Normalize(row.action);

            if (!hooks.TryGet(hookId, out HookRegistration hook))
            {
                errors.Add($"{where}: unknown hook '{hookId}'.");
                ok = false;
                continue;
            }

            if (!hook.Phases.TryGetValue((verbId, phaseId), out _))
            {
                errors.Add($"{where}: unknown verb '{verbId}' phase '{phaseId}' on hook '{hookId}'.");
                ok = false;
                continue;
            }

            if (!actions.TryGet(actionId, hookId, verbId, phaseId, out IAbilityActionHandler handler))
            {
                errors.Add($"{where}: unknown action '{actionId}' for {hookId}/{verbId}/{phaseId}.");
                ok = false;
                continue;
            }

            if (handler.Hook != hookId || handler.Verb != verbId || handler.Phase != phaseId)
            {
                errors.Add(
                    $"{where}: action '{actionId}' is registered for {handler.Hook}/{handler.Verb}/{handler.Phase}, not {hookId}/{verbId}/{phaseId}.");
                ok = false;
                continue;
            }

            if (!TryCompileWhen(
                    where,
                    row.when,
                    collections,
                    out AbilityWhenFilter? filter,
                    out string whenError))
            {
                errors.Add($"{where}: {whenError}");
                ok = false;
                continue;
            }

            if (!handler.TryParseParams(row.@params, out object parameters, out string paramError))
            {
                errors.Add($"{where}: {paramError}");
                ok = false;
                continue;
            }

            if (parameters is Actions.DropTableParams dropTable
                && !string.IsNullOrWhiteSpace(dropTable.Table)
                && !collections.Exists(dropTable.Table))
            {
                errors.Add($"{where}: unknown drop table/collection '{dropTable.Table}'.");
                ok = false;
                continue;
            }

            if (parameters is Actions.HasUnlockParams hasUnlock)
            {
                string unlockSkill = hasUnlock.SkillId ?? skillId;
                // Node existence is validated against compiled skill trees after all skills load;
                // here we only require a non-empty node id (already parsed).
                if (string.IsNullOrWhiteSpace(hasUnlock.NodeId))
                {
                    errors.Add($"{where}: has-unlock requires unlock node id.");
                    ok = false;
                    continue;
                }

                _ = unlockSkill;
            }

            if (!ValidateStackCollectionParams(where, row.@params, collections, errors))
            {
                ok = false;
                continue;
            }

            sourceOrder++;
            rules.Add(new AbilityRule
            {
                RuleId = $"{skillId}:{nodeId ?? "root"}:{tier ?? 0}:{i}",
                Hook = hookId,
                Verb = verbId,
                Phase = phaseId,
                Action = actionId,
                When = filter!,
                Parameters = parameters,
                Source = new AbilityRuleSource
                {
                    SkillId = skillId,
                    NodeId = nodeId,
                    Tier = tier
                },
                Priority = row.priority ?? 0,
                SourceOrder = sourceOrder
            });
        }

        return ok ? rules : null;
    }

    public static bool TryCompileWhen(
        string where,
        AbilityWhenJson? when,
        CollectionIndex collections,
        out AbilityWhenFilter? filter,
        out string error)
    {
        filter = null;

        if (!string.IsNullOrWhiteSpace(when?.verb))
        {
            error = "when.verb is removed; use top-level verb and when.tags for classification.";
            return false;
        }

        List<TagCriterion> criteria = new();
        string[] rawTags = when?.tags ?? Array.Empty<string>();
        foreach (string? raw in rawTags)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            if (!TagCriterionParser.TryParse(raw, collections, out TagCriterion? criterion, out string parseError)
                || criterion == null)
            {
                error = parseError;
                return false;
            }

            criteria.Add(criterion);
        }

        filter = new AbilityWhenFilter
        {
            Criteria = criteria,
            Collections = collections
        };
        error = "";
        return true;
    }

    static bool ValidateStackCollectionParams(
        string where,
        JObject? raw,
        CollectionIndex collections,
        List<string> errors)
    {
        if (raw == null)
        {
            return true;
        }

        bool ok = true;
        foreach (string key in new[] { "matchTag", "outputTag", "match", "table" })
        {
            JToken? token = raw[key];
            if (token == null || token.Type == JTokenType.Null || token.Type != JTokenType.String)
            {
                // Quality rules use table as a number array; drop-table actions use a string id.
                continue;
            }

            string? value = token.Value<string>()?.Trim();
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            string id = NormalizeCollectionParam(value);
            if (!collections.Exists(id))
            {
                errors.Add($"{where}: unknown collection '{value}' for {key}.");
                ok = false;
            }
        }

        return ok;
    }

    /// <summary>Allows authors to write <c>&lt;stone&gt;</c> or bare <c>stone</c> / pool ids.</summary>
    public static string NormalizeCollectionParam(string value)
    {
        string trimmed = value.Trim();
        if (trimmed.Length >= 3 && trimmed[0] == '<' && trimmed[^1] == '>')
        {
            return trimmed[1..^1].Trim();
        }

        return trimmed;
    }
}
