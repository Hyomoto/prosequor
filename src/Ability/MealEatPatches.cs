using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace Prosequor.Ability;

/// <summary>Scopes the eaten meal stack for pedigree eat mods.</summary>
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
}
