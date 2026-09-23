using Prosequor.Xp;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace Prosequor.Ability;

/// <summary>
/// High-water good-voxel count on a clay form, keyed by selected recipe.
/// Survives chunk save/load on <see cref="ProsequorChunkPedigree"/> so undo-redo cannot re-farm XP.
/// </summary>
public static class ClayFormXpStation
{
    public const string HighWaterAttr = "prosequorClayXpHighWater";
    public const string RecipeKeyAttr = "prosequorClayXpRecipe";

    public static string? RecipeKeyOf(ClayFormingRecipe? recipe)
    {
        if (recipe == null)
        {
            return null;
        }

        AssetLocation? name = recipe.Name;
        if (name == null)
        {
            return null;
        }

        string key = name.ToShortString();
        return string.IsNullOrWhiteSpace(key) ? null : key;
    }

    /// <summary>
    /// After a voxel mutation, pay newly reached good voxels and advance the mark.
    /// Returns how many voxels to award (0 when nothing novel).
    /// </summary>
    public static int TakeProgress(BlockEntityClayForm form)
    {
        if (form?.Api?.Side != EnumAppSide.Server || form.SelectedRecipe == null)
        {
            return 0;
        }

        string? currentKey = RecipeKeyOf(form.SelectedRecipe);
        if (currentKey == null)
        {
            return 0;
        }

        int layers = Math.Min(16, form.SelectedRecipe.QuantityLayers);
        int good = ClayFormXpMath.CountGood(form.Voxels, form.SelectedRecipe.Voxels, layers);

        if (!ProsequorBlockPedigreeStation.TryGetBox(form, out ProsequorChunkPedigree.Box box))
        {
            box = new ProsequorChunkPedigree.Box();
        }

        int paid = ClayFormXpMath.TakeDelta(
            good,
            box.ClayHighWater,
            box.ClayRecipeKey,
            currentKey,
            out int newHighWater,
            out string? newKey);

        if (box.ClayHighWater != newHighWater
            || !string.Equals(box.ClayRecipeKey, newKey, StringComparison.Ordinal))
        {
            ProsequorBlockPedigreeStation.Mutate(form, b =>
            {
                b.ClayHighWater = newHighWater;
                b.ClayRecipeKey = newKey;
            });
        }

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
}
