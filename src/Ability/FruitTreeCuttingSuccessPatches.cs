using System.Text;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.GameContent;

namespace Prosequor.Ability;

/// <summary>
/// Place stamps absolute establish chance; TryGrow/GetBlockInfo own Cutting when stamped
/// (Prefix early-out — original body skipped for that invocation only).
/// </summary>
[HarmonyPatch(typeof(BlockFruitTreeBranch), nameof(BlockFruitTreeBranch.TryPlaceBlock))]
public static class BlockFruitTreeBranchTryPlaceBlockSuccessPatch
{
    [HarmonyPostfix]
    public static void Postfix(
        BlockFruitTreeBranch __instance,
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
            is not BlockEntityFruitTreeBranch be)
        {
            return;
        }

        FruitTreeCuttingSuccess.TryStampOnPlace(world, byPlayer, be, __instance);
        ProsequorBlockPedigreeStation.StampPlanter(be, byPlayer.PlayerUID);
    }
}



[HarmonyPatch(typeof(FruitTreeGrowingBranchBH), "TryGrow")]
public static class FruitTreeGrowingBranchBHTryGrowSuccessPatch
{
    [HarmonyPrefix]
    public static bool Prefix(FruitTreeGrowingBranchBH __instance)
    {
        if (__instance.Blockentity is not BlockEntityFruitTreeBranch ownBe
            || ownBe.PartType != EnumTreePartType.Cutting
            || !FruitTreeCuttingSuccess.TryGetEstablishChance(ownBe, out _))
        {
            return true;
        }

        FruitTreeCuttingSuccess.EstablishCutting(__instance);
        return false;
    }
}

[HarmonyPatch(typeof(FruitTreeGrowingBranchBH), nameof(FruitTreeGrowingBranchBH.GetBlockInfo))]
public static class FruitTreeGrowingBranchBHGetBlockInfoSuccessPatch
{
    [HarmonyPrefix]
    public static bool Prefix(
        FruitTreeGrowingBranchBH __instance,
        StringBuilder dsc)
    {
        if (__instance.Blockentity is not BlockEntityFruitTreeBranch ownBe
            || ownBe.PartType != EnumTreePartType.Cutting
            || !FruitTreeCuttingSuccess.TryGetEstablishChance(ownBe, out _))
        {
            return true;
        }

        FruitTreeCuttingSuccess.AppendCuttingInfo(ownBe, dsc);
        return false;
    }
}
