using Newtonsoft.Json.Linq;
using Prosequor.Ability.Hooks;
using Vintagestory.API.Common;

namespace Prosequor.Ability.Actions;

/// <summary>Which list entry <c>replace-from-drop-table</c> overwrites.</summary>
public enum DropTableReplaceRule
{
    First,
    Last,
    Random
}

/// <summary>
/// Shared params for append/replace-from-drop-table.
/// Omit <see cref="Table"/> to use ambient <see cref="DropsContext.DropTable"/>;
/// set it to a pools.json / collection id for a named weighted table.
/// </summary>
public sealed class DropTableParams
{
    /// <summary>Optional named table id; null means ambient context table.</summary>
    public string? Table { get; init; }

    public int RollsMin { get; init; } = 1;
    public int RollsMax { get; init; } = 1;

    public int QuantityMin { get; init; } = 1;
    public int QuantityMax { get; init; } = 1;

    /// <summary>True when <c>params.quantity</c> was authored (not omitted).</summary>
    public bool QuantitySpecified { get; init; }

    /// <summary>Replace target selection; ignored by append. Default <see cref="DropTableReplaceRule.First"/>.</summary>
    public DropTableReplaceRule Rule { get; init; } = DropTableReplaceRule.First;
}

/// <summary>Parses and resolves ambient vs named drop tables.</summary>
public static class DropTableResolve
{
    public static bool TryParse(JObject? raw, out DropTableParams? parameters, out string error)
    {
        parameters = null;
        if (!AbilityFormulas.TryParseIntRange(
                raw?["rolls"],
                defaultMin: 1,
                defaultMax: 1,
                minAllowed: 1,
                fieldName: "rolls",
                out int rollsMin,
                out int rollsMax,
                out _,
                out error))
        {
            return false;
        }

        if (!AbilityFormulas.TryParseQuantityRange(
                raw?["quantity"],
                out int quantityMin,
                out int quantityMax,
                out bool quantitySpecified,
                out error))
        {
            return false;
        }

        if (!TryParseReplaceRule(raw?["rule"], out DropTableReplaceRule rule, out error))
        {
            return false;
        }

        string? table = raw?.Value<string>("table")?.Trim();
        if (string.IsNullOrWhiteSpace(table))
        {
            table = null;
        }
        else
        {
            table = AbilityRuleCompiler.NormalizeCollectionParam(table);
        }

        parameters = new DropTableParams
        {
            Table = table,
            RollsMin = rollsMin,
            RollsMax = rollsMax,
            QuantityMin = quantityMin,
            QuantityMax = quantityMax,
            QuantitySpecified = quantitySpecified,
            Rule = rule
        };
        error = "";
        return true;
    }

    /// <summary>
    /// Named <paramref name="parameters"/>.Table → <see cref="PoolDropTable"/>;
    /// otherwise ambient <see cref="DropsContext.DropTable"/>.
    /// </summary>
    public static IDropTable? TryGet(DropsContext context, DropTableParams parameters)
    {
        if (!string.IsNullOrWhiteSpace(parameters.Table))
        {
            return new PoolDropTable(context, parameters.Table);
        }

        return context.DropTable;
    }

    /// <summary>
    /// One table roll with quantity applied for named tables or when <c>quantity</c> was set.
    /// Ambient rolls without <c>quantity</c> keep the table's stack size.
    /// </summary>
    public static ItemStack? TryRollStack(DropsContext context, DropTableParams parameters)
    {
        IDropTable? table = TryGet(context, parameters);
        ItemStack? stack = table?.TryRollOne();
        if (stack == null)
        {
            return null;
        }

        if (parameters.QuantitySpecified || !string.IsNullOrWhiteSpace(parameters.Table))
        {
            int amount = AbilityFormulas.RollIntRange(
                parameters.QuantityMin,
                parameters.QuantityMax,
                context.World.Rand);
            if (amount <= 0)
            {
                return null;
            }

            stack.StackSize = amount;
        }

        return stack;
    }

    public static int PickReplaceIndex(DropTableReplaceRule rule, int count, Random rand)
    {
        if (count <= 1)
        {
            return 0;
        }

        return rule switch
        {
            DropTableReplaceRule.Last => count - 1,
            DropTableReplaceRule.Random => rand.Next(count),
            _ => 0
        };
    }

    static bool TryParseReplaceRule(JToken? token, out DropTableReplaceRule rule, out string error)
    {
        rule = DropTableReplaceRule.First;
        error = "";
        if (token == null || token.Type == JTokenType.Null || token.Type == JTokenType.Undefined)
        {
            return true;
        }

        if (token.Type != JTokenType.String)
        {
            error = "rule must be \"first\", \"last\", or \"random\".";
            return false;
        }

        string text = (token.Value<string>() ?? "").Trim();
        if (text.Length == 0)
        {
            return true;
        }

        if (text.Equals("first", StringComparison.OrdinalIgnoreCase))
        {
            rule = DropTableReplaceRule.First;
            return true;
        }

        if (text.Equals("last", StringComparison.OrdinalIgnoreCase))
        {
            rule = DropTableReplaceRule.Last;
            return true;
        }

        if (text.Equals("random", StringComparison.OrdinalIgnoreCase))
        {
            rule = DropTableReplaceRule.Random;
            return true;
        }

        error = "rule must be \"first\", \"last\", or \"random\".";
        return false;
    }
}

/// <summary>Weighted pools.json / collection pick as an <see cref="IDropTable"/>.</summary>
public sealed class PoolDropTable : IDropTable
{
    readonly DropsContext context;
    readonly string tableId;

    public PoolDropTable(DropsContext context, string tableId)
    {
        this.context = context;
        this.tableId = tableId;
    }

    public ItemStack? TryRollOne()
    {
        Item? pick = context.Tags.PickFromPool(
            context.World,
            tableId,
            context.World.Rand,
            context.PoolExcludeItemIds);
        return pick == null ? null : new ItemStack(pick, 1);
    }
}
