using System.Runtime.CompilerServices;
using Prosequor.Xp;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.GameContent;

namespace Prosequor.Ability;

/// <summary>
/// High-water good-voxel count on an anvil work piece, keyed by selected smithing recipe.
/// Survives chunk save/load so undo-redo cannot re-farm XP after a reload.
/// </summary>
public static class AnvilXpStation
{
    public const string HighWaterAttr = "prosequorAnvilXpHighWater";
    public const string RecipeKeyAttr = "prosequorAnvilXpRecipe";

    static readonly ConditionalWeakTable<BlockEntity, Box> boxes = new();

    sealed class Box
    {
        public int HighWater;
        public string? RecipeKey;
    }

    public static string? RecipeKeyOf(SmithingRecipe? recipe)
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
    public static int TakeProgress(BlockEntityAnvil anvil)
    {
        if (anvil?.Api?.Side != EnumAppSide.Server
            || anvil.Voxels == null
            || anvil.SelectedRecipe == null)
        {
            return 0;
        }

        string? currentKey = RecipeKeyOf(anvil.SelectedRecipe);
        if (currentKey == null)
        {
            return 0;
        }

        bool[,,]? want = anvil.recipeVoxels;
        if (want == null)
        {
            return 0;
        }

        int layers = Math.Min(AnvilVoxelGrid.SizeY, anvil.SelectedRecipe.QuantityLayers);
        int good = ClayFormXpMath.CountGood(anvil.Voxels, want, layers, AnvilVoxelGrid.Metal);

        Box box = boxes.GetOrCreateValue(anvil);
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
            anvil.MarkDirty(redrawOnClient: false);
        }

        return paid;
    }

    /// <summary>Award novel good voxels to the hammering player (server).</summary>
    public static void TryAwardProgress(BlockEntityAnvil anvil, IPlayer? player)
    {
        if (anvil?.Api?.Side != EnumAppSide.Server || player == null)
        {
            return;
        }

        int paid = TakeProgress(anvil);
        if (paid <= 0)
        {
            return;
        }

        string? target = anvil.SelectedRecipe?.Output?.Code?.ToString()
            ?? anvil.SelectedRecipe?.Output?.ResolvedItemstack?.Collectible?.Code?.ToString();
        ProsequorModSystem.For(anvil.Api)?.ClayFormXp?.NotifyProgress(player, paid, target);
    }

    public static void WriteToTree(BlockEntityAnvil anvil, ITreeAttribute tree)
    {
        if (!boxes.TryGetValue(anvil, out Box? box) || box == null)
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

    public static void ReadFromTree(BlockEntityAnvil anvil, ITreeAttribute tree)
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

        Box box = boxes.GetOrCreateValue(anvil);
        box.HighWater = hasWater ? Math.Max(0, tree.GetInt(HighWaterAttr)) : 0;
        box.RecipeKey = hasKey ? tree.GetString(RecipeKeyAttr) : null;
        if (string.IsNullOrWhiteSpace(box.RecipeKey))
        {
            box.RecipeKey = null;
        }
    }
}
