using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace Prosequor.Xp.Activity;

/// <summary>
/// Fishing effort poll: fishing + liquid target while pole is cast / bobber out.
/// Registered as <see cref="Effort.PollIdFishing"/>.
/// </summary>
public static class EffortFishingEmitter
{
    public static EffortPollResult? TryPoll(IPlayer player, EntityAgent entity)
    {
        if (player == null || entity == null)
        {
            return null;
        }

        ItemSlot? slot = player.InventoryManager?.ActiveHotbarSlot;
        ItemStack? stack = slot?.Itemstack;
        if (stack?.Collectible is not ItemFishingPole || stack.Attributes == null)
        {
            return null;
        }

        long bobberId = stack.Attributes.GetLong("bobberEntityId", 0L);
        long fishingEntityId = stack.Attributes.GetLong("fishingEntityId", 0L);
        if (fishingEntityId == 0L && bobberId == 0L)
        {
            return null;
        }

        return new EffortPollResult(
            [EffortTokenTags.Fishing],
            Target: ResolveLiquidTarget(entity, bobberId),
            Channel: EffortTokenTags.Fishing);
    }

    static string? ResolveLiquidTarget(EntityAgent entity, long bobberId)
    {
        Entity? bobber = bobberId != 0L ? entity.World.GetEntityById(bobberId) : null;
        if (bobber != null)
        {
            BlockPos at = bobber.Pos.AsBlockPos;
            if (TryLiquidAt(entity.World, at, out Block? liquid) && liquid?.Code != null)
            {
                return liquid.Code.ToString();
            }
        }

        BlockPos feet = entity.Pos.AsBlockPos;
        if (TryLiquidAt(entity.World, feet, out Block? underfoot) && underfoot?.Code != null)
        {
            return underfoot.Code.ToString();
        }

        BlockPos below = feet.DownCopy();
        Block? under = entity.World.BlockAccessor.GetBlock(below);
        return under != null && under.Id != 0 ? under.Code?.ToString() : null;
    }

    static bool TryLiquidAt(IWorldAccessor world, BlockPos pos, out Block? liquid)
    {
        liquid = world.BlockAccessor.GetBlock(pos);
        if (liquid != null && liquid.Id != 0 && IsLiquidish(liquid))
        {
            return true;
        }

        liquid = world.BlockAccessor.GetBlock(pos.DownCopy());
        return liquid != null && liquid.Id != 0 && IsLiquidish(liquid);
    }

    static bool IsLiquidish(Block block) =>
        block.IsLiquid()
        || (block.Code?.Path.Contains("water", StringComparison.OrdinalIgnoreCase) ?? false)
        || (block.Code?.Path.Contains("lava", StringComparison.OrdinalIgnoreCase) ?? false);
}
