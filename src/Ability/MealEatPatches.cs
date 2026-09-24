using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace Prosequor.Ability;

/// <summary>Scopes cooked food stacks for pedigree eat mods (meals and spit roasts).</summary>
public static class MealEatPatches
{
    [HarmonyPatch(typeof(BlockMeal), nameof(BlockMeal.Consume))]
    public static class BlockMealConsumeScopePatch
    {
        [HarmonyPrefix]
        public static void Prefix(ItemSlot inSlot) =>
            MealEatScope.Begin(inSlot?.Itemstack);

        [HarmonyFinalizer]
        public static void Finalizer() => MealEatScope.End();
    }

    [HarmonyPatch(typeof(CollectibleObject), "tryEatStop")]
    public static class CollectibleTryEatStopScopePatch
    {
        [HarmonyPrefix]
        public static void Prefix(ItemSlot slot) =>
            MealEatScope.Begin(slot?.Itemstack);

        [HarmonyFinalizer]
        public static void Finalizer() => MealEatScope.End();
    }
}
