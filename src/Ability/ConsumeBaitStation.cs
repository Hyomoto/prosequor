using Prosequor.Ability.Hooks;
using Prosequor.Player;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace Prosequor.Ability;

/// <summary>
/// Bait consume station. Adapters snapshot bait before vanilla clears it, then run
/// item-interaction <c>consume-bait</c> / <c>restock</c> (free restore and/or inventory refill).
/// </summary>
public static class ConsumeBaitStation
{
    public const string VerbConsumeBait = "prosequor:consume-bait";

    /// <summary>
    /// After vanilla clears bait: run <c>restock</c>. Matching rules may set
    /// <see cref="ConsumeBaitContext.RestockedBait"/>; this applies it to the bobber.
    /// </summary>
    public static int TryRestock(ConsumeBaitContext context, EntityBobber bobber)
    {
        if (context.World?.Api == null
            || bobber == null
            || bobber.BaitStack != null
            || context.ConsumedBait == null)
        {
            return 0;
        }

        ProsequorModSystem? mod = ProsequorModSystem.For(context.World.Api);
        if (mod?.Pipeline == null)
        {
            return 0;
        }

        int restocked = mod.Pipeline.Run(HookIds.ItemInteraction, VerbIds.ConsumeBait, HookIds.Restock, context, 0);
        ItemStack? bait = context.RestockedBait;
        if (bait == null || bait.StackSize <= 0)
        {
            return restocked;
        }

        if (context.World != null)
        {
            bait.ResolveBlockOrItem(bobber.World);
        }

        bobber.BaitStack = bait;
        bobber.WatchedAttributes.MarkPathDirty("baitStack");
        return restocked;
    }

    public static ConsumeBaitContext BuildContext(
        IWorldAccessor world,
        IPlayer player,
        IPlayerProgress progress,
        ItemStack consumedBait,
        ItemSlot? poleSlot)
    {
        AbilityAction fact = EventFactBuilder.ForPlayer(
            player,
            VerbConsumeBait,
            target: EventFactBuilder.CodeOf(consumedBait.Collectible),
            held: EventFactBuilder.CodeOf(poleSlot?.Itemstack));

        return new ConsumeBaitContext
        {
            World = world,
            Player = player,
            Progress = progress,
            Fact = fact,
            ConsumedBait = consumedBait
        };
    }

    /// <summary>Removes up to <paramref name="amount"/> units matching <paramref name="template"/>.</summary>
    public static int TakeMatching(
        IPlayer player,
        ItemStack template,
        int amount,
        out ItemStack? taken)
    {
        taken = null;
        if (player?.InventoryManager?.Inventories == null
            || template?.Collectible == null
            || amount <= 0)
        {
            return 0;
        }

        int remaining = amount;
        ItemStack? aggregate = null;

        foreach (IInventory inv in player.InventoryManager.Inventories.Values)
        {
            if (inv == null || inv.Count == 0)
            {
                continue;
            }

            if (inv is InventoryBasePlayer playerInv
                && playerInv.Player != null
                && playerInv.Player != player)
            {
                continue;
            }

            for (int i = 0; i < inv.Count && remaining > 0; i++)
            {
                ItemSlot? slot = inv[i];
                ItemStack? stack = slot?.Itemstack;
                if (stack == null || !SameCollectible(stack, template))
                {
                    continue;
                }

                ItemStack piece = slot!.TakeOut(Math.Min(remaining, stack.StackSize));
                slot.MarkDirty();
                if (piece == null || piece.StackSize <= 0)
                {
                    continue;
                }

                remaining -= piece.StackSize;
                if (aggregate == null)
                {
                    aggregate = piece;
                }
                else
                {
                    aggregate.StackSize += piece.StackSize;
                }
            }
        }

        taken = aggregate;
        return amount - remaining;
    }

    public static bool SameCollectible(ItemStack a, ItemStack b) =>
        a.Collectible != null
        && b.Collectible != null
        && a.Collectible.Class == b.Collectible.Class
        && a.Collectible.Id == b.Collectible.Id;
}
