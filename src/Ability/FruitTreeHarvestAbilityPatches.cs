using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace Prosequor.Ability;

/// <summary>
/// Orchardist quantity rules for fruit-tree interact harvest and ripe break drops.
/// </summary>
[HarmonyPatch(typeof(BlockEntityFruitTreePart))]
public static class FruitTreeHarvestAbilityPatches
{
    [HarmonyPrefix]
    [HarmonyPatch(nameof(BlockEntityFruitTreePart.OnBlockInteractStop))]
    public static void InteractStopPrefix(
        BlockEntityFruitTreePart __instance,
        IPlayer byPlayer,
        BlockSelection blockSel)
    {
        Begin(__instance, byPlayer, blockSel?.Position);
    }

    [HarmonyPostfix]
    [HarmonyPatch(nameof(BlockEntityFruitTreePart.OnBlockInteractStop))]
    public static void InteractStopPostfix() => DropHarvestScope.Pop();

    [HarmonyPrefix]
    [HarmonyPatch(nameof(BlockEntityFruitTreePart.OnBlockBroken))]
    public static void BrokenPrefix(BlockEntityFruitTreePart __instance, IPlayer byPlayer)
    {
        Begin(__instance, byPlayer, __instance.Pos);
    }

    [HarmonyPostfix]
    [HarmonyPatch(nameof(BlockEntityFruitTreePart.OnBlockBroken))]
    public static void BrokenPostfix() => DropHarvestScope.Pop();

    static void Begin(BlockEntityFruitTreePart part, IPlayer? byPlayer, BlockPos? pos)
    {
        IWorldAccessor? world = part?.Api?.World;
        Block? block = part?.Block;
        if (world == null || byPlayer == null || block == null || pos == null)
        {
            return;
        }

        AbilityAction fact = DropsFactBuilder.ForHarvest(byPlayer, block, pos);
        DropHarvestScope.Begin(byPlayer, fact, block, pos);
    }
}
