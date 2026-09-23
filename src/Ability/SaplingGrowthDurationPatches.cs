using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.GameContent;

namespace Prosequor.Ability;

/// <summary>
/// Place stamps growth-time multiplier and scales remaining hours; CheckGrow
/// re-scales after Seed→Sapling when stamped.
/// </summary>
[HarmonyPatch(typeof(Block), nameof(Block.DoPlaceBlock))]
public static class BlockDoPlaceBlockSaplingGrowthPatch
{
    [HarmonyPostfix]
    public static void Postfix(
        bool __result,
        IWorldAccessor world,
        IPlayer byPlayer,
        BlockSelection blockSel)
    {
        if (!__result
            || world?.Side != EnumAppSide.Server
            || byPlayer == null
            || blockSel?.Position == null)
        {
            return;
        }

        if (world.BlockAccessor.GetBlockEntity(blockSel.Position)
            is not BlockEntitySapling be)
        {
            return;
        }

        ProsequorBlockPedigreeStation.StampPlanter(be, byPlayer.PlayerUID);
        SaplingGrowthDuration.TryStampOnPlace(world, byPlayer, be);
    }
}



[HarmonyPatch(typeof(BlockEntitySapling), "CheckGrow")]
public static class BlockEntitySaplingCheckGrowGrowthPatch
{
    [HarmonyPrefix]
    public static void Prefix(EnumTreeGrowthStage ___stage, out EnumTreeGrowthStage __state)
    {
        __state = ___stage;
    }

    [HarmonyPostfix]
    public static void Postfix(
        BlockEntitySapling __instance,
        EnumTreeGrowthStage __state,
        EnumTreeGrowthStage ___stage)
    {
        SaplingGrowthDuration.ApplyAfterStageChange(__instance, __state, ___stage);
    }
}
