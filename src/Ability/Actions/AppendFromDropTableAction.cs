using Newtonsoft.Json.Linq;
using Prosequor.Ability.Hooks;
using Vintagestory.API.Common;

namespace Prosequor.Ability.Actions;

/// <summary>
/// Appends rolled stacks from ambient <see cref="DropsContext.DropTable"/> or a named
/// <c>params.table</c> pool. <c>rolls</c> controls how many times; <c>quantity</c> the stack size.
/// No-ops when the table is null; skips individual miss rolls.
/// </summary>
public abstract class AppendFromDropTableActionBase
    : AbilityActionHandler<DropsContext, IReadOnlyList<ItemStack>, DropTableParams>
{
    public override ActionId Id => ActionIds.AppendFromDropTable;
    public override VerbId Verb => VerbIds.MutateDrops;
    public override PhaseId Phase => HookIds.Stacks;

    protected override bool TryParse(JObject? raw, out DropTableParams? parameters, out string error) =>
        DropTableResolve.TryParse(raw, out parameters, out error);

    protected override IReadOnlyList<ItemStack> Apply(
        DropsContext context,
        IReadOnlyList<ItemStack> value,
        DropTableParams parameters,
        AbilityRuleSource source)
    {
        _ = source;
        if (DropTableResolve.TryGet(context, parameters) == null)
        {
            return value;
        }

        int rolls = AbilityFormulas.RollIntRange(
            parameters.RollsMin,
            parameters.RollsMax,
            context.World.Rand);
        if (rolls <= 0)
        {
            return value;
        }

        List<ItemStack> next = new(value.Count + rolls);
        next.AddRange(value);
        for (int i = 0; i < rolls; i++)
        {
            ItemStack? extra = DropTableResolve.TryRollStack(context, parameters);
            if (extra != null)
            {
                next.Add(extra);
            }
        }

        return next;
    }
}

/// <summary>Block-interaction mutate-drops stacks.</summary>
public sealed class AppendFromDropTableBlockAction : AppendFromDropTableActionBase
{
    public override HookId Hook => HookIds.BlockInteraction;
}

/// <summary>Item-interaction mutate-drops stacks.</summary>
public sealed class AppendFromDropTableItemAction : AppendFromDropTableActionBase
{
    public override HookId Hook => HookIds.ItemInteraction;
}

/// <summary>
/// Replaces list entries with rolls from ambient or named drop table.
/// <c>rule</c> selects which entry (<c>first</c> / <c>last</c> / <c>random</c>); <c>rolls</c>
/// repeats. No-ops on empty list or missing table; skips miss rolls.
/// </summary>
public abstract class ReplaceFromDropTableActionBase
    : AbilityActionHandler<DropsContext, IReadOnlyList<ItemStack>, DropTableParams>
{
    public override ActionId Id => ActionIds.ReplaceFromDropTable;
    public override VerbId Verb => VerbIds.MutateDrops;
    public override PhaseId Phase => HookIds.Stacks;

    protected override bool TryParse(JObject? raw, out DropTableParams? parameters, out string error) =>
        DropTableResolve.TryParse(raw, out parameters, out error);

    protected override IReadOnlyList<ItemStack> Apply(
        DropsContext context,
        IReadOnlyList<ItemStack> value,
        DropTableParams parameters,
        AbilityRuleSource source)
    {
        _ = source;
        if (value.Count == 0 || DropTableResolve.TryGet(context, parameters) == null)
        {
            return value;
        }

        int rolls = AbilityFormulas.RollIntRange(
            parameters.RollsMin,
            parameters.RollsMax,
            context.World.Rand);
        if (rolls <= 0)
        {
            return value;
        }

        List<ItemStack> next = new(value);
        for (int i = 0; i < rolls; i++)
        {
            if (next.Count == 0)
            {
                break;
            }

            ItemStack? rolled = DropTableResolve.TryRollStack(context, parameters);
            if (rolled == null)
            {
                continue;
            }

            int index = DropTableResolve.PickReplaceIndex(
                parameters.Rule,
                next.Count,
                context.World.Rand);
            next[index] = rolled;
        }

        return next;
    }
}

/// <summary>Block-interaction mutate-drops stacks replace.</summary>
public sealed class ReplaceFromDropTableBlockAction : ReplaceFromDropTableActionBase
{
    public override HookId Hook => HookIds.BlockInteraction;
}

/// <summary>Item-interaction mutate-drops stacks replace.</summary>
public sealed class ReplaceFromDropTableItemAction : ReplaceFromDropTableActionBase
{
    public override HookId Hook => HookIds.ItemInteraction;
}
