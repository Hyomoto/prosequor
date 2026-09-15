using Prosequor.Ability;
using Prosequor.Xp.Activity;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace Prosequor.Xp.Adapters;

/// <summary>
/// Watering-can hold: emit <c>interacting</c> and stamp water care credit only when
/// the pour raises the farmland moisture budget (one full pour per growth stage).
/// </summary>
public static class WateringXp
{
    public const string RefilledAttr = "refilled";

    /// <summary>
    /// Credit moisture gained since <paramref name="moistureBefore"/>. Care credit (and
    /// optional effort) only when the budget actually rose.
    /// </summary>
    public static float CreditPour(
        IPlayer? player,
        BlockEntityFarmland? farmland,
        float moistureBefore,
        bool emitEffort)
    {
        if (player?.PlayerUID == null || farmland == null)
        {
            return 0f;
        }

        float credited = ProsequorBlockPedigreeStation.TryAddWaterCredit(
            farmland,
            farmland.MoistureLevel - moistureBefore);
        if (credited <= 0f)
        {
            return 0f;
        }

        ProsequorBlockPedigreeStation.TryAddCareCredit(
            farmland,
            player.PlayerUID,
            FarmlandCareKind.Water);
        if (emitEffort)
        {
            EmitIfWateringFarmland(player, farmland);
        }

        return credited;
    }

    /// <summary>
    /// After a credited pour: water care credit only. Callers must already have raised the budget.
    /// </summary>
    public static void OnWatered(IPlayer? player, BlockEntityFarmland? farmland)
    {
        if (player?.PlayerUID == null || farmland == null)
        {
            return;
        }

        ProsequorBlockPedigreeStation.TryAddCareCredit(
            farmland,
            player.PlayerUID,
            FarmlandCareKind.Water);
    }

    /// <summary>Watcher pulse while the can is pouring onto farmland (not refilling).</summary>
    public static void EmitIfWateringFarmland(IPlayer? player, BlockEntityFarmland? farmland)
    {
        if (player == null || farmland == null)
        {
            return;
        }

        Effort.Emit(
            player,
            EffortToken.Interacting,
            target: EventFactBuilder.CodeOf(farmland.Block),
            channel: EffortTokenTags.Interacting);
    }

    /// <summary>
    /// Vanilla watering-can aim: farmland at the selection, or the block below when
    /// the aimed block has no collision (crop / air cover).
    /// </summary>
    public static bool TryResolveFarmland(
        IWorldAccessor? world,
        BlockSelection? blockSel,
        out BlockEntityFarmland? farmland)
    {
        farmland = null;
        if (world?.BlockAccessor == null || blockSel?.Position == null)
        {
            return false;
        }

        BlockPos pos = ResolveWateringPos(world, blockSel);
        if (world.BlockAccessor.GetBlockEntity(pos) is not BlockEntityFarmland be)
        {
            return false;
        }

        farmland = be;
        return true;
    }

    public static bool IsRefilling(ItemSlot? slot) =>
        slot?.Itemstack != null && slot.Itemstack.TempAttributes.GetInt(RefilledAttr) > 0;

    static BlockPos ResolveWateringPos(IWorldAccessor world, BlockSelection blockSel)
    {
        BlockPos pos = blockSel.Position;
        Block target = world.BlockAccessor.GetBlock(pos);
        if (target.CollisionBoxes != null && target.CollisionBoxes.Length > 0)
        {
            return pos;
        }

        Block fluidOrCover = world.BlockAccessor.GetBlock(pos, BlockLayersAccess.Fluid);
        if ((fluidOrCover.CollisionBoxes == null || fluidOrCover.CollisionBoxes.Length == 0)
            && !fluidOrCover.IsLiquid())
        {
            return pos.DownCopy();
        }

        return pos;
    }
}
