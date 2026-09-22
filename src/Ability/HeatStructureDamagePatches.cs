using System;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace Prosequor.Ability;

/// <summary>
/// Replaces beehive-kiln / stone-coffin heat-damage walks so Construction can skip
/// fireclay and refractory brick damage without double-rolling resist.
/// </summary>
[HarmonyPatch(typeof(MultiblockStructure), nameof(MultiblockStructure.WalkMatchingBlocks))]
public static class HeatStructureDamagePatches
{
    [HarmonyPrefix]
    public static void Prefix(
        IWorldAccessor world,
        BlockPos centerPos,
        ref Action<Block, BlockPos>? onBlock)
    {
        if (world?.Side != EnumAppSide.Server || centerPos == null || onBlock == null)
        {
            return;
        }

        BlockEntity? be = world.BlockAccessor.GetBlockEntity(centerPos);
        if (be is not BlockEntityBeeHiveKiln and not BlockEntityStoneCoffin)
        {
            return;
        }

        IPlayer? player = HeatStructureDamageStation.ResolveOwnerAtCenter(world, centerPos);
        Action? onDamaged = null;
        if (be is BlockEntityBeeHiveKiln kiln)
        {
            onDamaged = () =>
            {
                kiln.StructureComplete = false;
                kiln.MarkDirty(redrawOnClient: false);
            };
        }

        onBlock = (block, pos) =>
            HeatStructureDamageStation.ApplyHeatDamage(player, world, block, pos, onDamaged);
    }
}
