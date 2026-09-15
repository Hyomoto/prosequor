using Prosequor.Ability;
using Prosequor.Ability.Actions;
using Prosequor.Ability.Hooks;

namespace Prosequor.Data;

/// <summary>Compiles attribute-stat JSON rules into bound <see cref="AbilityRule"/> instances.</summary>
public static class AttributeRuleCompiler
{
    public static List<AbilityRule>? CompileRules(
        string attributeId,
        AttributeStatRuleJson[]? rows,
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
            AttributeStatRuleJson row = rows[i];
            string where = $"Attribute '{attributeId}' rule {i + 1}";
            if (!string.IsNullOrWhiteSpace(row.id))
            {
                where = $"Attribute '{attributeId}' rule '{row.id.Trim()}'";
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

            if (!AbilityRuleCompiler.TryCompileWhen(
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

            int minScore = row.minScore ?? 0;
            if (minScore < 0)
            {
                errors.Add($"{where}: minScore must be >= 0.");
                ok = false;
            }

            if (row.maxScore is int maxScore && maxScore < minScore)
            {
                errors.Add($"{where}: maxScore must be >= minScore.");
                ok = false;
            }

            if (!ok)
            {
                continue;
            }

            if (!handler.TryParseParams(row.@params, out object parameters, out string paramError))
            {
                errors.Add($"{where}: {paramError}");
                ok = false;
                continue;
            }

            if (parameters is DropTableParams dropTable
                && !string.IsNullOrWhiteSpace(dropTable.Table)
                && !collections.Exists(dropTable.Table))
            {
                errors.Add($"{where}: unknown drop table/collection '{dropTable.Table}'.");
                ok = false;
                continue;
            }

            sourceOrder++;
            string ruleId = !string.IsNullOrWhiteSpace(row.id)
                ? row.id.Trim()
                : $"{attributeId}:rule:{i}";

            rules.Add(new AbilityRule
            {
                RuleId = ruleId,
                Hook = hookId,
                Verb = verbId,
                Phase = phaseId,
                Action = actionId,
                When = filter!,
                Parameters = parameters,
                Source = new AbilityRuleSource
                {
                    SkillId = attributeId,
                    AttributeId = attributeId,
                    MinAttributeScore = minScore,
                    MaxAttributeScore = row.maxScore
                },
                Priority = row.priority ?? 0,
                SourceOrder = sourceOrder
            });
        }

        return ok ? rules : null;
    }
}
