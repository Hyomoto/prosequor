using HarmonyLib;
using Prosequor.Xp;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace Prosequor.Ability;

/// <summary>
/// Knapping completion. No per-voxel pulse. The output stack leaves through
/// <see cref="VoxelFinishScope"/> so <c>pay: voxels</c> can read the recipe.
/// </summary>
[HarmonyPatch(typeof(BlockEntityKnappingSurface), nameof(BlockEntityKnappingSurface.CheckIfFinished))]
public static class KnappingCheckIfFinishedPatch
{
    [HarmonyPrefix]
    public static void Prefix(BlockEntityKnappingSurface __instance, IPlayer byPlayer)
    {
        if (byPlayer == null || __instance?.Api?.Side != EnumAppSide.Server)
        {
            return;
        }

        VoxelFinishScope.BeginGive(
            byPlayer,
            KnappingRecipeCatalog.RecipeKeyOf(__instance.SelectedRecipe),
            __instance.Block);
    }

    [HarmonyFinalizer]
    public static void Finalizer() => VoxelFinishScope.End();
}
