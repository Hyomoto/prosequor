using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace Prosequor.Ability;

/// <summary>
/// When a meal leaves a pot, bowl, or crock, keep the potter and drop the cook.
/// Covers serve-empty, last bite, rot, and a wash in water.
/// </summary>
public static class MealVesselEmptyPatches
{
    public sealed class EmptyState
    {
        public ItemStack? Before;
        public string? Maker;
        public bool HadMeal;
        public int BlockId;
        public BlockPos? Pos;
    }

    static void NoteStack(ItemStack? stack, out EmptyState state)
    {
        state = new EmptyState
        {
            Before = stack,
            Maker = CraftAttribution.TryGetMakerUid(stack),
            HadMeal = MealHostCredit.StillHasMeal(stack)
        };
    }

    static void FinishStack(EmptyState? state, ItemStack? after)
    {
        if (state == null || !state.HadMeal)
        {
            return;
        }

        MealHostCredit.AfterMealGone(state.Before, after, state.Maker);
    }

    [HarmonyPatch(typeof(BlockCookedContainerBase), "SetServingsMaybeEmpty")]
    public static class SetServingsMaybeEmptyPatch
    {
        [HarmonyPrefix]
        public static void Prefix(ItemSlot potslot, out EmptyState __state) =>
            NoteStack(potslot?.Itemstack, out __state);

        [HarmonyPostfix]
        public static void Postfix(ItemSlot potslot, EmptyState __state) =>
            FinishStack(__state, potslot?.Itemstack);
    }

    [HarmonyPatch(typeof(BlockMeal), nameof(BlockMeal.OnHeldInteractStop))]
    public static class MealEatEmptyPatch
    {
        [HarmonyPrefix]
        public static void Prefix(ItemSlot slot, out EmptyState __state) =>
            NoteStack(slot?.Itemstack, out __state);

        [HarmonyPostfix]
        public static void Postfix(ItemSlot slot, EmptyState __state) =>
            FinishStack(__state, slot?.Itemstack);
    }

    [HarmonyPatch(typeof(BlockMeal), nameof(BlockMeal.OnBlockInteractStop))]
    public static class PlacedMealEatEmptyPatch
    {
        [HarmonyPrefix]
        public static void Prefix(IWorldAccessor world, BlockSelection blockSel, out EmptyState __state)
        {
            __state = new EmptyState();
            if (world?.Side != EnumAppSide.Server || blockSel?.Position == null)
            {
                return;
            }

            __state.Pos = blockSel.Position.Copy();
            __state.BlockId = world.BlockAccessor.GetBlock(blockSel.Position).BlockId;
            BlockEntity? be = world.BlockAccessor.GetBlockEntity(blockSel.Position);
            if (ProsequorBlockPedigreeStation.TryGetBlob(be, out ProsequorBlob blob))
            {
                __state.Maker = blob.MakerUid;
                __state.HadMeal = true;
            }
        }

        [HarmonyPostfix]
        public static void Postfix(IWorldAccessor world, EmptyState __state)
        {
            if (!__state.HadMeal || __state.Pos == null || world?.Side != EnumAppSide.Server)
            {
                return;
            }

            Block block = world.BlockAccessor.GetBlock(__state.Pos);
            if (block == null || block.BlockId == __state.BlockId)
            {
                return;
            }

            MealHostCredit.RetainBeMaker(world.BlockAccessor.GetBlockEntity(__state.Pos), __state.Maker);
        }
    }

    [HarmonyPatch(typeof(BlockMeal), nameof(BlockMeal.UpdateAndGetTransitionStates))]
    public static class MealRotEmptyPatch
    {
        [HarmonyPrefix]
        public static void Prefix(ItemSlot inslot, out EmptyState __state) =>
            NoteStack(inslot?.Itemstack, out __state);

        [HarmonyPostfix]
        public static void Postfix(ItemSlot inslot, EmptyState __state) =>
            FinishStack(__state, inslot?.Itemstack);
    }

    [HarmonyPatch(typeof(BlockCookedContainer), nameof(BlockCookedContainer.UpdateAndGetTransitionStates))]
    public static class CookedPotRotEmptyPatch
    {
        [HarmonyPrefix]
        public static void Prefix(ItemSlot inslot, out EmptyState __state) =>
            NoteStack(inslot?.Itemstack, out __state);

        [HarmonyPostfix]
        public static void Postfix(ItemSlot inslot, EmptyState __state) =>
            FinishStack(__state, inslot?.Itemstack);
    }

    [HarmonyPatch(typeof(BlockCrock), nameof(BlockCrock.UpdateAndGetTransitionStates))]
    public static class CrockRotEmptyPatch
    {
        [HarmonyPrefix]
        public static void Prefix(ItemSlot inslot, out EmptyState __state) =>
            NoteStack(inslot?.Itemstack, out __state);

        [HarmonyPostfix]
        public static void Postfix(ItemSlot inslot, EmptyState __state) =>
            FinishStack(__state, inslot?.Itemstack);
    }

    [HarmonyPatch(typeof(BlockCookedContainer), nameof(BlockCookedContainer.OnGroundIdle))]
    public static class CookedPotWashPatch
    {
        [HarmonyPrefix]
        public static void Prefix(EntityItem entityItem, out EmptyState __state) =>
            NoteStack(entityItem?.Itemstack, out __state);

        [HarmonyPostfix]
        public static void Postfix(EntityItem entityItem, EmptyState __state) =>
            FinishStack(__state, entityItem?.Itemstack);
    }

    [HarmonyPatch(typeof(BlockMeal), nameof(BlockMeal.OnGroundIdle))]
    public static class MealWashPatch
    {
        [HarmonyPrefix]
        public static void Prefix(EntityItem entityItem, out EmptyState __state) =>
            NoteStack(entityItem?.Itemstack, out __state);

        [HarmonyPostfix]
        public static void Postfix(EntityItem entityItem, EmptyState __state) =>
            FinishStack(__state, entityItem?.Itemstack);
    }

    [HarmonyPatch(typeof(BlockCrock), nameof(BlockCrock.OnGroundIdle))]
    public static class CrockWashPatch
    {
        [HarmonyPrefix]
        public static void Prefix(EntityItem entityItem, out EmptyState __state) =>
            NoteStack(entityItem?.Itemstack, out __state);

        [HarmonyPostfix]
        public static void Postfix(EntityItem entityItem, EmptyState __state) =>
            FinishStack(__state, entityItem?.Itemstack);
    }
}
