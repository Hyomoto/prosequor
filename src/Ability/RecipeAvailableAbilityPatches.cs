using HarmonyLib;
using Vintagestory.API.Common;

namespace Prosequor.Ability;

/// <summary>
/// Gates grid recipes that declare <c>attributes.prosequorUnlock</c> on
/// <c>crafting-interaction</c> / <c>recipe-available</c>.
/// </summary>
[HarmonyPatch(typeof(GridRecipe), nameof(GridRecipe.Matches))]
public static class RecipeAvailableAbilityPatches
{
    [HarmonyPrefix]
    public static bool Prefix(GridRecipe __instance, IPlayer forPlayer, ref bool __result)
    {
        if (MedicineStation.IsRecipeAvailable(forPlayer, __instance))
        {
            return true;
        }

        __result = false;
        return false;
    }
}
