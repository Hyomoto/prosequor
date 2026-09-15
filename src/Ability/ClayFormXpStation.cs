using System.Runtime.CompilerServices;
using Prosequor.Xp;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.GameContent;

namespace Prosequor.Ability;

/// <summary>
/// High-water good-voxel count on a clay form, keyed by selected recipe.
/// Survives chunk save/load so undo-redo cannot re-farm XP after a reload.
/// </summary>
public static class ClayFormXpStation
{
    public const string HighWaterAttr = "prosequorClayXpHighWater";
    public const string RecipeKeyAttr = "prosequorClayXpRecipe";

    static readonly ConditionalWeakTable<BlockEntity, Box> boxes = new();

    sealed class Box
    {
        public int HighWater;
        public string? RecipeKey;
    }

    public static string? RecipeKeyOf(ClayFormingRecipe? recipe)
    {
        if (recipe == null)
        {
            return null;
        }

        // RecipeBase.Name is the recipe id (e.g. game:recipes/clayforming/bowl).
        // Color variants from one file share this name; voxel XP keys on it, collection membership does not.
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

        Box box = boxes.GetOrCreateValue(form);
        int paid = ClayFormXpMath.TakeDelta(
            good,
            box.HighWater,
            box.RecipeKey,
            currentKey,
            out int newHighWater,
            out string? newKey);

        if (box.HighWater != newHighWater || !string.Equals(box.RecipeKey, newKey, StringComparison.Ordinal))
        {
            box.HighWater = newHighWater;
            box.RecipeKey = newKey;
            form.MarkDirty(redrawOnClient: false);
        }

        return paid;
    }

    public static void WriteToTree(BlockEntityClayForm form, ITreeAttribute tree)
    {
        if (!boxes.TryGetValue(form, out Box? box) || box == null)
        {
            return;
        }

        if (box.HighWater > 0)
        {
            tree.SetInt(HighWaterAttr, box.HighWater);
        }

        if (!string.IsNullOrEmpty(box.RecipeKey))
        {
            tree.SetString(RecipeKeyAttr, box.RecipeKey);
        }
    }

    public static void ReadFromTree(BlockEntityClayForm form, ITreeAttribute tree)
    {
        if (tree == null)
        {
            return;
        }

        bool hasWater = tree.HasAttribute(HighWaterAttr);
        bool hasKey = tree.HasAttribute(RecipeKeyAttr);
        if (!hasWater && !hasKey)
        {
            return;
        }

        Box box = boxes.GetOrCreateValue(form);
        box.HighWater = hasWater ? Math.Max(0, tree.GetInt(HighWaterAttr)) : 0;
        box.RecipeKey = hasKey ? tree.GetString(RecipeKeyAttr) : null;
        if (string.IsNullOrWhiteSpace(box.RecipeKey))
        {
            box.RecipeKey = null;
        }
    }
}
