using HarmonyLib;
using Prosequor.Xp.Adapters;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace Prosequor.Ability;

/// <summary>
/// Watering-can interact step: farming effort + water care credit on the aimed tile,
/// and Field Expertise NxN extra farmland watering (always full unlocked size).
/// </summary>
[HarmonyPatch(typeof(BlockWateringCan), nameof(BlockWateringCan.OnHeldInteractStep))]
public static class BlockWateringCanFieldAreaPatch
{
    const string PrevSecondsAttr = "prosequorWateringPrevSeconds";

    [HarmonyPrefix]
    public static void Prefix(
        ItemSlot slot,
        EntityAgent byEntity,
        BlockSelection blockSel,
        out float __state)
    {
        __state = -1f;
        if (blockSel == null
            || slot?.Itemstack == null
            || byEntity is not EntityPlayer entityPlayer
            || entityPlayer.Player == null
            || byEntity.World?.Side != EnumAppSide.Server
            || WateringXp.IsRefilling(slot))
        {
            return;
        }

        if (WateringXp.TryResolveFarmland(byEntity.World, blockSel, out BlockEntityFarmland? farmland)
            && farmland != null)
        {
            __state = farmland.MoistureLevel;
        }
    }

    [HarmonyPostfix]
    public static void Postfix(
        bool __result,
        float secondsUsed,
        ItemSlot slot,
        EntityAgent byEntity,
        BlockSelection blockSel,
        float __state)
    {
        if (!__result || blockSel == null || slot?.Itemstack == null)
        {
            return;
        }

        if (byEntity is not EntityPlayer entityPlayer || entityPlayer.Player == null)
        {
            return;
        }

        if (byEntity.World.Side != EnumAppSide.Server || WateringXp.IsRefilling(slot))
        {
            return;
        }

        IPlayer player = entityPlayer.Player;
        IWorldAccessor world = byEntity.World;
        if (!WateringXp.TryResolveFarmland(world, blockSel, out BlockEntityFarmland? farmland)
            || farmland == null)
        {
            return;
        }

        if (__state >= 0f)
        {
            WateringXp.CreditPour(player, farmland, __state, emitEffort: true);
        }

        int size = FieldWorkStation.Run(player);
        if (size <= 1)
        {
            return;
        }

        float prev = slot.Itemstack.TempAttributes.GetFloat(PrevSecondsAttr, 0f);
        if (secondsUsed < prev)
        {
            prev = 0f;
        }

        slot.Itemstack.TempAttributes.SetFloat(PrevSecondsAttr, secondsUsed);
        float dt = secondsUsed - prev;
        if (dt <= 0f)
        {
            return;
        }

        Block block = world.BlockAccessor.GetBlock(blockSel.Position);
        int x = blockSel.Position.X;
        int y = blockSel.Position.Y;
        int z = blockSel.Position.Z;
        int xOff = 0;
        int zOff = 0;
        if (size % 2 == 0)
        {
            if (x - byEntity.Pos.X >= 0.0)
            {
                xOff = 1;
            }

            if (z - byEntity.Pos.Z >= 0.0)
            {
                zOff = 1;
            }
        }

        x = x - size / 2 + xOff;
        z = z - size / 2 + zOff;

        for (int ix = x; ix < x + size; ix++)
        {
            for (int iz = z; iz < z + size; iz++)
            {
                if (ix == blockSel.Position.X && iz == blockSel.Position.Z)
                {
                    continue;
                }

                BlockPos pos = new(ix, y, iz, blockSel.Position.dimension);
                if (block.CollisionBoxes == null || block.CollisionBoxes.Length == 0)
                {
                    Block fluidOrCover = world.BlockAccessor.GetBlock(blockSel.Position, 2);
                    if ((fluidOrCover.CollisionBoxes == null || fluidOrCover.CollisionBoxes.Length == 0)
                        && !fluidOrCover.IsLiquid())
                    {
                        pos = pos.DownCopy();
                    }
                }

                if (world.BlockAccessor.GetBlockEntity(pos) is BlockEntityFarmland extra)
                {
                    float before = extra.MoistureLevel;
                    extra.WaterFarmland(dt, waterNeightbours: true);
                    WateringXp.CreditPour(player, extra, before, emitEffort: false);
                }
            }
        }
    }
}
