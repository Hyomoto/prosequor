using System.Text;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace Prosequor.Ability;

/// <summary>
/// Meal tooltips and placed info: cook credit sits with the serving, before Nutrition Facts.
/// </summary>
public static class MealTooltipCreditPatches
{
    [HarmonyPatch(typeof(BlockMeal), nameof(BlockMeal.GetHeldItemInfo))]
    public static class HeldBowlInfoPatch
    {
        [HarmonyPostfix]
        public static void Postfix(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world) =>
            MealTooltipCredit.InsertBeforeNutritionFacts(dsc, world, inSlot?.Itemstack);
    }

    [HarmonyPatch(typeof(BlockCookedContainer), nameof(BlockCookedContainer.GetHeldItemInfo))]
    public static class HeldPotInfoPatch
    {
        [HarmonyPostfix]
        public static void Postfix(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world) =>
            MealTooltipCredit.InsertBeforeNutritionFacts(dsc, world, inSlot?.Itemstack);
    }

    [HarmonyPatch(typeof(BlockCrock), nameof(BlockCrock.GetHeldItemInfo))]
    public static class HeldCrockInfoPatch
    {
        [HarmonyPostfix]
        public static void Postfix(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world) =>
            MealTooltipCredit.InsertBeforeNutritionFacts(dsc, world, inSlot?.Itemstack);
    }

    [HarmonyPatch(typeof(BlockCrock), nameof(BlockCrock.GetPlacedBlockInfo))]
    public static class PlacedCrockInfoPatch
    {
        [HarmonyPostfix]
        public static void Postfix(IWorldAccessor world, BlockPos pos, ref string __result)
        {
            if (world == null || pos == null)
            {
                return;
            }

            if (world.BlockAccessor.GetBlockEntity(pos) is not BlockEntity be
                || !ProsequorBlockPedigreeStation.TryGetBlob(be, out ProsequorBlob blob))
            {
                return;
            }

            MealTooltipCredit.InsertBeforeNutritionFacts(ref __result, world, blob);
        }
    }

    [HarmonyPatch(typeof(BlockEntityCookedContainer), nameof(BlockEntityCookedContainer.GetBlockInfo))]
    public static class PlacedPotInfoPatch
    {
        [HarmonyPostfix]
        public static void Postfix(BlockEntityCookedContainer __instance, StringBuilder dsc)
        {
            if (!ProsequorBlockPedigreeStation.TryGetBlob(__instance, out ProsequorBlob blob))
            {
                return;
            }

            MealTooltipCredit.InsertBeforeNutritionFacts(dsc, __instance?.Api?.World, blob);
        }
    }

    [HarmonyPatch(typeof(BlockEntityMeal), nameof(BlockEntityMeal.GetBlockInfo))]
    public static class PlacedBowlInfoPatch
    {
        [HarmonyPostfix]
        public static void Postfix(BlockEntityMeal __instance, StringBuilder dsc)
        {
            if (!ProsequorBlockPedigreeStation.TryGetBlob(__instance, out ProsequorBlob blob))
            {
                return;
            }

            MealTooltipCredit.InsertBeforeNutritionFacts(dsc, __instance?.Api?.World, blob);
        }
    }
}
