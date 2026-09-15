using Newtonsoft.Json.Linq;
using Prosequor.Ability.Hooks;
using Vintagestory.API.Common;

namespace Prosequor.Ability.Actions;

/// <summary>
/// Free bait refund: clone the pre-consume bait onto
/// <see cref="ConsumeBaitContext.RestockedBait"/> without taking from inventory.
/// </summary>
public sealed class RestoreConsumedBaitOnBaitAction
    : AbilityActionHandler<ConsumeBaitContext, int, object>
{
    public override ActionId Id => ActionIds.RestoreConsumedBait;
    public override HookId Hook => HookIds.ItemInteraction;
    public override VerbId Verb => VerbIds.ConsumeBait;
    public override PhaseId Phase => HookIds.Restock;

    protected override bool TryParse(JObject? raw, out object? parameters, out string error)
    {
        parameters = new object();
        error = "";
        return true;
    }

    protected override int Apply(
        ConsumeBaitContext context,
        int value,
        object parameters,
        AbilityRuleSource source)
    {
        _ = source;
        if (context.RestockedBait != null || context.ConsumedBait == null)
        {
            return value;
        }

        ItemStack clone = context.ConsumedBait.Clone();
        if (context.World != null)
        {
            clone.ResolveBlockOrItem(context.World);
        }

        context.RestockedBait = clone;
        return value + Math.Max(1, clone.StackSize);
    }
}
