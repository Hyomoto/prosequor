using Prosequor.Xp;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace Prosequor.Ability;

/// <summary>
/// High-water correct-voxel count on a clay form, keyed by selected recipe.
/// The form block is the workpiece, so the mark lives on its chunk pedigree.
/// </summary>
public static class ClayFormXpStation
{
    public const string HighWaterAttr = "prosequorClayXpHighWater";
    public const string RecipeKeyAttr = "prosequorClayXpRecipe";

    public static string? RecipeKeyOf(ClayFormingRecipe? recipe) =>
        VoxelWorkXp.RecipeKeyOf(recipe?.Name);

    /// <summary>
    /// Record the correct-voxel count before an edit. The first sight of a recipe
    /// adopts that count and pays nothing.
    /// </summary>
    public static void NoteBaseline(BlockEntityClayForm form)
    {
        if (!TryMeasure(form, out int good, out string? key))
        {
            return;
        }

        VoxelWorkXp.Mark current = Read(form);
        VoxelWorkXp.Mark next = VoxelWorkXp.Note(good, key, current);
        Save(form, current, next);
    }

    /// <summary>
    /// After a voxel mutation, pay newly reached correct voxels and advance the mark.
    /// Returns how many voxels to award (0 when nothing novel).
    /// </summary>
    public static int TakeProgress(BlockEntityClayForm form)
    {
        if (!TryMeasure(form, out int good, out string? key))
        {
            return 0;
        }

        VoxelWorkXp.Mark current = Read(form);
        int paid = VoxelWorkXp.Apply(good, key, current, out VoxelWorkXp.Mark next);
        Save(form, current, next);
        return paid;
    }

    /// <summary>Scenario / test stamp of high-water state onto the pedigree host.</summary>
    public static void Stamp(BlockEntityClayForm? form, int highWater, string? recipeKey)
    {
        if (form == null)
        {
            return;
        }

        ProsequorBlockPedigreeStation.Mutate(form, box =>
        {
            box.ClayHighWater = Math.Max(0, highWater);
            box.ClayRecipeKey = string.IsNullOrWhiteSpace(recipeKey) ? null : recipeKey.Trim();
        });
    }

    static bool TryMeasure(BlockEntityClayForm? form, out int good, out string? key)
    {
        good = 0;
        key = null;
        if (form?.Api?.Side != EnumAppSide.Server
            || form.SelectedRecipe == null
            || form.Voxels == null)
        {
            return false;
        }

        key = RecipeKeyOf(form.SelectedRecipe);
        if (key == null)
        {
            return false;
        }

        int layers = Math.Min(16, form.SelectedRecipe.QuantityLayers);
        good = VoxelWorkXpMath.CountGood(form.Voxels, form.SelectedRecipe.Voxels, layers);
        return true;
    }

    static VoxelWorkXp.Mark Read(BlockEntityClayForm form)
    {
        if (!ProsequorBlockPedigreeStation.TryGetBox(form, out ProsequorChunkPedigree.Box box))
        {
            return default;
        }

        return new VoxelWorkXp.Mark(box.ClayHighWater, box.ClayRecipeKey);
    }

    static void Save(BlockEntityClayForm form, VoxelWorkXp.Mark current, VoxelWorkXp.Mark next)
    {
        if (current.HighWater == next.HighWater
            && string.Equals(current.RecipeKey, next.RecipeKey, StringComparison.Ordinal))
        {
            return;
        }

        ProsequorBlockPedigreeStation.Mutate(form, box =>
        {
            box.ClayHighWater = next.HighWater;
            box.ClayRecipeKey = next.RecipeKey;
        });
    }
}
