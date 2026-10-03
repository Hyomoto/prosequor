using Prosequor.Ability;
using Prosequor.Xp.Activity;
using Vintagestory.API.Common;

namespace Prosequor.Xp.Adapters;

/// <summary>
/// Finished pan: <c>panned</c> deed, quantity = stacks handed out after mutate-drops.
/// A miss (no stacks) does not emit. Sift XP stays on effort.
/// </summary>
public static class PanningXp
{
    public static void NotifyComplete(
        ICoreAPI? api,
        IPlayer? player,
        Block? pan,
        string? fromBlockCode,
        IReadOnlyList<ItemStack>? stacks)
    {
        if (api == null
            || api.Side != EnumAppSide.Server
            || player?.PlayerUID == null
            || stacks == null
            || stacks.Count == 0)
        {
            return;
        }

        List<Deed.QuantityUnit> units = new();
        int total = 0;
        for (int i = 0; i < stacks.Count; i++)
        {
            ItemStack? stack = stacks[i];
            string? code = EventFactBuilder.CodeOf(stack);
            if (string.IsNullOrWhiteSpace(code) || stack!.StackSize <= 0)
            {
                continue;
            }

            units.Add(new Deed.QuantityUnit(code, stack.StackSize));
            total += stack.StackSize;
        }

        if (total <= 0)
        {
            return;
        }

        string? caller = EventFactBuilder.CodeOf(pan);
        api.Logger.VerboseDebug(
            "[prosequor] deed panned {0} caller={1} units={2} by {3}",
            fromBlockCode,
            caller,
            total,
            player.PlayerName);

        Deed.Emit(
            api,
            player.PlayerUID,
            DeedToken.Panned,
            caller: caller,
            target: fromBlockCode,
            outputs: units);
    }
}
