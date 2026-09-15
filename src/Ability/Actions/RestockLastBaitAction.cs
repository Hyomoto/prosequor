using Newtonsoft.Json.Linq;
using Prosequor.Ability.Hooks;
using Vintagestory.API.Common;

namespace Prosequor.Ability.Actions;

public sealed class RestockLastBaitParams
{
    /// <summary>Units to take from inventory matching the consumed bait.</summary>
    public int Amount { get; init; } = 1;
}

/// <summary>
/// After bait consume: take matching bait from the player's inventories and queue it
/// on <see cref="ConsumeBaitContext.RestockedBait"/>. Does not substitute a different type.
/// </summary>
public sealed class RestockLastBaitOnBaitAction
    : AbilityActionHandler<ConsumeBaitContext, int, RestockLastBaitParams>
{
    public override ActionId Id => ActionIds.RestockLastBait;
    public override HookId Hook => HookIds.ItemInteraction;
    public override VerbId Verb => VerbIds.ConsumeBait;
    public override PhaseId Phase => HookIds.Restock;

    protected override bool TryParse(
        JObject? raw,
        out RestockLastBaitParams? parameters,
        out string error)
    {
        parameters = null;
        int amount = raw?.Value<int?>("amount") ?? 1;
        if (amount < 1)
        {
            error = "amount must be >= 1.";
            return false;
        }

        parameters = new RestockLastBaitParams { Amount = amount };
        error = "";
        return true;
    }

    protected override int Apply(
        ConsumeBaitContext context,
        int value,
        RestockLastBaitParams parameters,
        AbilityRuleSource source)
    {
        _ = source;
        if (context.Player == null
            || context.ConsumedBait?.Collectible == null
            || context.RestockedBait != null
            || parameters.Amount < 1)
        {
            return value;
        }

        int took = ConsumeBaitStation.TakeMatching(
            context.Player,
            context.ConsumedBait,
            parameters.Amount,
            out ItemStack? taken);
        if (took <= 0 || taken == null)
        {
            return value;
        }

        taken.ResolveBlockOrItem(context.World);
        context.RestockedBait = taken;
        return value + took;
    }
}
