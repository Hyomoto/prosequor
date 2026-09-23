using Prosequor.Xp;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace Prosequor.Ability;

/// <summary>
/// High-water good-voxel count on an anvil work piece, keyed by selected smithing recipe.
/// Survives chunk save/load on <see cref="ProsequorChunkPedigree"/> so undo-redo cannot re-farm XP.
/// </summary>
public static class AnvilXpStation
{
    public const string HighWaterAttr = "prosequorAnvilXpHighWater";
    public const string RecipeKeyAttr = "prosequorAnvilXpRecipe";

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

        if (!ProsequorBlockPedigreeStation.TryGetBox(anvil, out ProsequorChunkPedigree.Box box))
        {
            box = new ProsequorChunkPedigree.Box();
        }

        int paid = ClayFormXpMath.TakeDelta(
            good,
            box.AnvilHighWater,
            box.AnvilRecipeKey,
            currentKey,
            out int newHighWater,
            out string? newKey);

        if (box.AnvilHighWater != newHighWater
            || !string.Equals(box.AnvilRecipeKey, newKey, StringComparison.Ordinal))
        {
            ProsequorBlockPedigreeStation.Mutate(anvil, b =>
            {
                b.AnvilHighWater = newHighWater;
                b.AnvilRecipeKey = newKey;
            });
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

    /// <summary>Scenario / test stamp of high-water state onto the pedigree host.</summary>
    public static void Stamp(BlockEntityAnvil? anvil, int highWater, string? recipeKey)
    {
        if (anvil == null)
        {
            return;
        }

        ProsequorBlockPedigreeStation.Mutate(anvil, box =>
        {
            box.AnvilHighWater = Math.Max(0, highWater);
            box.AnvilRecipeKey = string.IsNullOrWhiteSpace(recipeKey) ? null : recipeKey.Trim();
        });
    }
}
