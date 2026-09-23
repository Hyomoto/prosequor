using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace Prosequor.Ability;

/// <summary>
/// Place → BE tree → pick/break restore for stack pedigree.
/// Explicit targets (not TargetMethods): container BEs call each other, not always
/// <see cref="BlockEntity"/> base, and pick overrides rebuild stacks without attrs.
/// </summary>
public static class ProsequorBlockPedigreePatches
{
    [HarmonyPatch(typeof(Block), nameof(Block.OnBlockPlaced))]
    public static class BlockOnBlockPlacedPedigreePatch
    {
        [HarmonyPostfix]
        public static void Postfix(IWorldAccessor world, BlockPos blockPos, ItemStack byItemStack) =>
            ProsequorBlockPedigreeStation.CaptureAtPos(world, blockPos, byItemStack);
    }

    [HarmonyPatch(typeof(BlockEntity), nameof(BlockEntity.OnBlockPlaced))]
    public static class BlockEntityOnBlockPlacedPedigreePatch
    {
        [HarmonyPostfix]
        public static void Postfix(BlockEntity __instance, ItemStack byItemStack) =>
            ProsequorBlockPedigreeStation.CaptureFromPlacedStack(__instance, byItemStack);
    }

    [HarmonyPatch(typeof(BlockEntityContainer), nameof(BlockEntityContainer.OnBlockPlaced))]
    public static class BlockEntityContainerOnBlockPlacedPedigreePatch
    {
        [HarmonyPostfix]
        public static void Postfix(BlockEntityContainer __instance, ItemStack byItemStack) =>
            ProsequorBlockPedigreeStation.CaptureFromPlacedStack(__instance, byItemStack);
    }

    [HarmonyPatch(typeof(BlockEntity), nameof(BlockEntity.OnBlockRemoved))]
    public static class BlockEntityOnBlockRemovedPedigreePatch
    {
        [HarmonyPostfix]
        public static void Postfix(BlockEntity __instance)
        {
            if (__instance?.Api?.Side != EnumAppSide.Server || __instance.Api.World == null)
            {
                return;
            }

            ProsequorBlockPedigreeStation.ClearAtPos(__instance.Api.World, __instance.Pos);
        }
    }

    [HarmonyPatch(typeof(Block), nameof(Block.GetDrops))]
    public static class BlockGetDropsPedigreePatch
    {
        /// <summary>
        /// Before mutate-drops: stamp the broken block's unit blob onto vanilla drops.
        /// Quantity luck may raise StackSize afterward; attributed qty stays short (no duplicate).
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPriority(Priority.First)]
        public static void Postfix(
            Block __instance,
            IWorldAccessor world,
            BlockPos pos,
            ref ItemStack[] __result) =>
            ProsequorBlockPedigreeStation.ApplyAtPos(world, pos, __instance, __result);
    }

    [HarmonyPatch(typeof(Block), nameof(Block.OnPickBlock))]
    public static class BlockOnPickBlockPedigreePatch
    {
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        public static void Postfix(
            Block __instance,
            IWorldAccessor world,
            BlockPos pos,
            ref ItemStack __result) =>
            ProsequorBlockPedigreeStation.ApplyAtPos(world, pos, __instance, __result);
    }

    [HarmonyPatch(typeof(BlockContainer), nameof(BlockContainer.OnPickBlock))]
    public static class BlockContainerOnPickBlockPedigreePatch
    {
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        public static void Postfix(
            BlockContainer __instance,
            IWorldAccessor world,
            BlockPos pos,
            ref ItemStack __result) =>
            ProsequorBlockPedigreeStation.ApplyAtPos(world, pos, __instance, __result);
    }

    [HarmonyPatch(typeof(BlockCrock), nameof(BlockCrock.OnPickBlock))]
    public static class BlockCrockOnPickBlockPedigreePatch
    {
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        public static void Postfix(
            BlockCrock __instance,
            IWorldAccessor world,
            BlockPos pos,
            ref ItemStack __result) =>
            ProsequorBlockPedigreeStation.ApplyAtPos(world, pos, __instance, __result);
    }

    [HarmonyPatch(typeof(BlockGenericTypedContainer), nameof(BlockGenericTypedContainer.OnPickBlock))]
    public static class BlockGenericTypedContainerOnPickBlockPedigreePatch
    {
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        public static void Postfix(
            BlockGenericTypedContainer __instance,
            IWorldAccessor world,
            BlockPos pos,
            ref ItemStack __result) =>
            ProsequorBlockPedigreeStation.ApplyAtPos(world, pos, __instance, __result);
    }

    [HarmonyPatch(typeof(BlockCookedContainer), nameof(BlockCookedContainer.OnPickBlock))]
    public static class BlockCookedContainerOnPickBlockPedigreePatch
    {
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        public static void Postfix(
            BlockCookedContainer __instance,
            IWorldAccessor world,
            BlockPos pos,
            ref ItemStack __result) =>
            ProsequorBlockPedigreeStation.ApplyAtPos(world, pos, __instance, __result);
    }

    [HarmonyPatch(typeof(BlockMeal), nameof(BlockMeal.OnPickBlock))]
    public static class BlockMealOnPickBlockPedigreePatch
    {
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        public static void Postfix(
            BlockMeal __instance,
            IWorldAccessor world,
            BlockPos pos,
            ref ItemStack __result) =>
            ProsequorBlockPedigreeStation.ApplyAtPos(world, pos, __instance, __result);
    }

    [HarmonyPatch(typeof(BlockEntityCrock), nameof(BlockEntityCrock.OnBlockPlaced))]
    public static class BlockEntityCrockOnBlockPlacedPedigreePatch
    {
        [HarmonyPostfix]
        public static void Postfix(BlockEntityCrock __instance, ItemStack byItemStack) =>
            ProsequorBlockPedigreeStation.CaptureFromPlacedStack(__instance, byItemStack);
    }

    [HarmonyPatch(typeof(BlockEntityCookedContainer), nameof(BlockEntityCookedContainer.OnBlockPlaced))]
    public static class BlockEntityCookedContainerOnBlockPlacedPedigreePatch
    {
        [HarmonyPostfix]
        public static void Postfix(BlockEntityCookedContainer __instance, ItemStack byItemStack) =>
            ProsequorBlockPedigreeStation.CaptureFromPlacedStack(__instance, byItemStack);
    }

    [HarmonyPatch(typeof(BlockEntityMeal), nameof(BlockEntityMeal.OnBlockPlaced))]
    public static class BlockEntityMealOnBlockPlacedPedigreePatch
    {
        [HarmonyPostfix]
        public static void Postfix(BlockEntityMeal __instance, ItemStack byItemStack) =>
            ProsequorBlockPedigreeStation.CaptureFromPlacedStack(__instance, byItemStack);
    }

    [HarmonyPatch(typeof(BlockEntityGenericTypedContainer), nameof(BlockEntityGenericTypedContainer.OnBlockPlaced))]
    public static class BlockEntityGenericTypedContainerOnBlockPlacedPedigreePatch
    {
        [HarmonyPostfix]
        public static void Postfix(BlockEntityGenericTypedContainer __instance, ItemStack byItemStack) =>
            ProsequorBlockPedigreeStation.CaptureFromPlacedStack(__instance, byItemStack);
    }
}
