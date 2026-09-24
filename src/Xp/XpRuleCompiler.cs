using Newtonsoft.Json.Linq;
using Prosequor.Ability;
using Prosequor.Ability.Hooks;
using Prosequor.Data;
using Prosequor.Xp.Activity;
using Vintagestory.API.Common;

namespace Prosequor.Xp;

/// <summary>Compiles skill-embedded <see cref="XpRuleJson"/> rows into runtime <see cref="XpRule"/>s.</summary>
public static class XpRuleCompiler
{
    public static bool TryCompile(
        XpRuleJson row,
        string skillId,
        int sourceOrder,
        CollectionIndex collections,
        out XpRule rule,
        out string error)
    {
        rule = null!;
        if (row == null
            || string.IsNullOrWhiteSpace(row.id)
            || row.when == null
            || string.IsNullOrWhiteSpace(row.when.activity))
        {
            error = "missing id or when.activity";
            return false;
        }

        if (!row.TryParseAmount(out float amountScalar, out float[]? amountTable, out string? amountError))
        {
            error = amountError ?? "invalid amount";
            return false;
        }

        bool hasAmount = amountScalar > 0f || (amountTable != null && amountTable.Length > 0);
        bool hasRate = row.rate > 0f;
        if (hasAmount == hasRate)
        {
            error = "set exactly one of amount or rate (> 0)";
            return false;
        }

        if (!row.TryParsePay(out XpPayChannel pay, out string? payError))
        {
            error = payError ?? "invalid pay";
            return false;
        }

        if (hasRate && (row.pay != null && row.pay.Type != JTokenType.Null))
        {
            error = "pay is only valid on amount rules";
            return false;
        }

        if (hasRate)
        {
            pay = XpPayChannel.Flat;
        }

        if (!TryCompileFilter(row.include, "include", collections, out XpQuantityExclude include, out string includeError))
        {
            error = includeError;
            return false;
        }

        if (!TryCompileFilter(row.exclude, "exclude", collections, out XpQuantityExclude exclude, out string excludeError))
        {
            error = excludeError;
            return false;
        }

        if (hasRate && !include.IsEmpty)
        {
            error = "include is only valid on amount rules";
            return false;
        }

        if (hasRate && !exclude.IsEmpty)
        {
            error = "exclude is only valid on amount rules";
            return false;
        }

        if (hasAmount
            && !include.IsEmpty
            && !XpPayChannels.UsesQuantity(pay)
            && !XpPayChannels.UsesIngredients(pay))
        {
            error = "include requires pay quantity or ingredients";
            return false;
        }

        if (hasAmount && !exclude.IsEmpty && !XpPayChannels.UsesQuantity(pay))
        {
            error = "exclude requires pay quantity";
            return false;
        }

        if (!row.TryParsePayee(out XpPayee payeeMode, out string? payeeError))
        {
            error = payeeError ?? "invalid payee";
            return false;
        }

        if (hasRate && !string.IsNullOrWhiteSpace(row.payee))
        {
            error = "payee is only valid on amount rules";
            return false;
        }

        if (hasRate)
        {
            payeeMode = XpPayee.User;
        }

        string id = row.id.Trim();
        string activity = XpRuleRegistry.NormalizeActivity(row.when.activity.Trim());
        List<TagCriterion> criteria = new();
        foreach (string? raw in row.when.tags ?? Array.Empty<string>())
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

        rule = new XpRule
        {
            Id = id,
            Activity = activity,
            SkillId = skillId,
            Amount = hasAmount ? amountScalar : 0f,
            AmountTable = hasAmount ? amountTable : null,
            Rate = hasRate ? row.rate : 0f,
            Pay = hasAmount ? pay : XpPayChannel.Flat,
            Include = hasAmount ? include : XpQuantityExclude.Empty,
            Exclude = hasAmount ? exclude : XpQuantityExclude.Empty,
            Payee = hasAmount ? payeeMode : XpPayee.User,
            Criteria = criteria,
            Priority = row.priority,
            SourceOrder = sourceOrder,
            MatchScore = XpRuleMatcher.Score(criteria, row.priority, sourceOrder)
        };
        error = "";
        return true;
    }

    static bool TryCompileFilter(
        string[]? raw,
        string field,
        CollectionIndex collections,
        out XpQuantityExclude filter,
        out string error)
    {
        filter = XpQuantityExclude.Empty;
        error = "";
        if (raw == null || raw.Length == 0)
        {
            return true;
        }

        List<string> exact = new();
        List<string> collectionsOut = new();
        foreach (string? entry in raw)
        {
            if (!XpQuantityExclude.TryParseEntry(entry, collections, exact, collectionsOut, out error))
            {
                error = error.Replace("filter", field, StringComparison.Ordinal);
                filter = XpQuantityExclude.Empty;
                return false;
            }
        }

        filter = new XpQuantityExclude(exact, collectionsOut);
        return true;
    }

    public static List<XpRule> CompileAll(
        XpRuleJson[]? rows,
        string skillId,
        CollectionIndex collections,
        ref int sourceOrder,
        Action<string> warn)
    {
        List<XpRule> compiled = new();
        if (rows == null || rows.Length == 0)
        {
            return compiled;
        }

        Dictionary<string, XpRule> byId = new(StringComparer.OrdinalIgnoreCase);
        foreach (XpRuleJson row in rows)
        {
            sourceOrder++;
            if (!TryCompile(row, skillId, sourceOrder, collections, out XpRule rule, out string error))
            {
                warn(
                    $"[prosequor] Skipping XP rule on skill '{skillId}' (order {sourceOrder}): {error}.");
                continue;
            }

            byId[rule.Id] = rule;

            if (rule.IsRateRule
                && string.Equals(rule.Activity, Effort.Activity, StringComparison.OrdinalIgnoreCase)
                && rule.Criteria.Count == 0)
            {
                warn(
                    $"[prosequor] XP rate rule '{rule.Id}' on skill '{skillId}' uses {Effort.Activity} with no when.tags — it matches every effort fact.");
            }

            if (rule.IsAmountRule
                && string.Equals(rule.Activity, Deed.Activity, StringComparison.OrdinalIgnoreCase)
                && rule.Criteria.Count == 0)
            {
                warn(
                    $"[prosequor] XP amount rule '{rule.Id}' on skill '{skillId}' uses {Deed.Activity} with no when.tags — it matches every deed.");
            }

            if (rule.IsAmountRule
                && rule.AmountTable != null
                && rule.AmountTable.Count > 0
                && !XpPayChannels.UsesMeasure(rule.Pay))
            {
                warn(
                    $"[prosequor] XP amount rule '{rule.Id}' on skill '{skillId}' has an amount table without a measure pay channel (resistance / effort / voxels / ingredients / lifetime) — missing metric always uses amount[0].");
            }
        }

        compiled.AddRange(byId.Values);
        return compiled;
    }
}
