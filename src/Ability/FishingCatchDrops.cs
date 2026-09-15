using Prosequor.Ability.Hooks;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace Prosequor.Ability;

/// <summary>
/// Fishing catch adapter into <see cref="DropsStation"/> (per-stack quantity/stack then stacks).
/// Mutates the give stack in place; extra stacks from list-post rules are flushed after.
/// Runs on item-interaction with a VS-resolved catch <see cref="IDropTable"/>.
/// </summary>
public static class FishingCatchDrops
{
    [ThreadStatic]
    static List<ItemStack>? pendingExtraStacks;

    public static void BeginCatch() => pendingExtraStacks = null;

    public static void ApplyPrefix(IWorldAccessor world, IPlayer player, ItemStack stack)
    {
        pendingExtraStacks = null;
        if (world?.Side != EnumAppSide.Server || player == null || stack == null)
        {
            return;
        }

        if (!FishingCatchFacts.TryCreate(player, stack, out AbilityAction? fact) || fact == null)
        {
            return;
        }

        IDropTable dropTable = new FishingCatchDropTable(stack);
        ItemStack[] stacks = DropsStation.Run(
            world,
            player,
            new[] { stack },
            fact,
            dropTable: dropTable,
            hook: HookIds.ItemInteraction);
        if (stacks.Length == 0 || stacks[0] == null)
        {
            stack.StackSize = 0;
            return;
        }

        if (!ReferenceEquals(stacks[0], stack))
        {
            stack.SetFrom(stacks[0]);
        }

        if (stacks.Length > 1)
        {
            pendingExtraStacks = new List<ItemStack>(stacks.Length - 1);
            for (int i = 1; i < stacks.Length; i++)
            {
                if (stacks[i] != null)
                {
                    pendingExtraStacks.Add(stacks[i]);
                }
            }
        }
    }

    public static void FlushExtraStacks(EntityPlayer entity, IPlayer player)
    {
        if (pendingExtraStacks == null || pendingExtraStacks.Count == 0 || entity?.World == null)
        {
            pendingExtraStacks = null;
            return;
        }

        foreach (ItemStack extra in pendingExtraStacks)
        {
            if (extra == null || extra.StackSize <= 0)
            {
                continue;
            }

            if (!player.InventoryManager.TryGiveItemstack(extra, slotNotifyEffect: true))
            {
                entity.World.SpawnItemEntity(extra, entity.Pos.XYZ);
            }
        }

        pendingExtraStacks = null;
    }
}

/// <summary>
/// Ambient drop table for a fishing catch: re-rolls by cloning the VS-resolved catch stack
/// (no authored pool; no second invisible-stock depletion).
/// </summary>
public sealed class FishingCatchDropTable : IDropTable
{
    readonly ItemStack template;

    public FishingCatchDropTable(ItemStack caught)
    {
        template = caught.Clone();
    }

    public ItemStack? TryRollOne()
    {
        if (template == null || template.StackSize <= 0)
        {
            return null;
        }

        return template.Clone();
    }
}
