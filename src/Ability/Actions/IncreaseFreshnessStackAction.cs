using Newtonsoft.Json.Linq;
using Prosequor.Ability.Hooks;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace Prosequor.Ability.Actions;

/// <summary>
/// Per-stack freshness projector: reads perish fresh-hours, runs a NumberSpec, writes back.
/// </summary>
public sealed class IncreaseFreshnessStackAction
    : AbilityActionHandler<DropsContext, ItemStack, NumberSpec>
{
    public override ActionId Id => ActionIds.IncreaseFreshness;
    public override HookId Hook => HookIds.BlockInteraction;
    public override VerbId Verb => VerbIds.MutateDrops;
    public override PhaseId Phase => HookIds.Stack;

    protected override bool TryParse(JObject? raw, out NumberSpec? parameters, out string error) =>
        NumberSpec.TryParse(raw, out parameters, out error);

    protected override ItemStack Apply(
        DropsContext context,
        ItemStack value,
        NumberSpec parameters,
        AbilityRuleSource source)
    {
        if (value == null || !ItemFreshnessApplicator.TryGetFreshHours(context.World, value, out float hours))
        {
            return value!;
        }

        float next = parameters.Apply(hours, context, source);
        if (next <= 0f || Math.Abs(next - hours) < 0.00001f)
        {
            return value;
        }

        ItemFreshnessApplicator.TrySetFreshHours(context.World, value, next);
        return value;
    }
}

/// <summary>Item-interaction mutate-drops stack freshness (e.g. fishing Fresh Fish).</summary>
public sealed class IncreaseFreshnessItemStackAction
    : AbilityActionHandler<DropsContext, ItemStack, NumberSpec>
{
    public override ActionId Id => ActionIds.IncreaseFreshness;
    public override HookId Hook => HookIds.ItemInteraction;
    public override VerbId Verb => VerbIds.MutateDrops;
    public override PhaseId Phase => HookIds.Stack;

    protected override bool TryParse(JObject? raw, out NumberSpec? parameters, out string error) =>
        NumberSpec.TryParse(raw, out parameters, out error);

    protected override ItemStack Apply(
        DropsContext context,
        ItemStack value,
        NumberSpec parameters,
        AbilityRuleSource source)
    {
        if (value == null || !ItemFreshnessApplicator.TryGetFreshHours(context.World, value, out float hours))
        {
            return value!;
        }

        float next = parameters.Apply(hours, context, source);
        if (next <= 0f || Math.Abs(next - hours) < 0.00001f)
        {
            return value;
        }

        ItemFreshnessApplicator.TrySetFreshHours(context.World, value, next);
        return value;
    }
}
