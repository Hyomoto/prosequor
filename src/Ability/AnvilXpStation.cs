using Prosequor.Xp;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.GameContent;

namespace Prosequor.Ability;

/// <summary>
/// High-water correct-voxel count on the anvil work item, keyed by selected smithing recipe.
/// The work item is the workpiece: the mark travels with <see cref="BlockEntityAnvil.WorkItemStack"/>.
/// </summary>
public static class AnvilXpStation
{
    public const string HighWaterAttr = "prosequorVoxelXpHighWater";
    public const string RecipeKeyAttr = "prosequorVoxelXpRecipe";

    public static string? RecipeKeyOf(SmithingRecipe? recipe) =>
        VoxelWorkXp.RecipeKeyOf(recipe?.Name);

    /// <summary>
    /// Record the correct-voxel count before an edit. The first sight of a recipe
    /// adopts that count and pays nothing.
    /// </summary>
    public static void NoteBaseline(BlockEntityAnvil anvil)
    {
        if (!TryMeasure(anvil, out int good, out string? key, out ItemStack work))
        {
            return;
        }

        VoxelWorkXp.Mark current = Read(work);
        VoxelWorkXp.Mark next = VoxelWorkXp.Note(good, key, current);
        Save(anvil, work, current, next);
    }

    /// <summary>
    /// After a voxel mutation, pay newly reached correct voxels and advance the mark.
    /// Returns how many voxels to award (0 when nothing novel).
    /// </summary>
    public static int TakeProgress(BlockEntityAnvil anvil)
    {
        if (!TryMeasure(anvil, out int good, out string? key, out ItemStack work))
        {
            return 0;
        }

        VoxelWorkXp.Mark current = Read(work);
        int paid = VoxelWorkXp.Apply(good, key, current, out VoxelWorkXp.Mark next);
        Save(anvil, work, current, next);
        return paid;
    }

    /// <summary>Award novel correct voxels to the hammering player (server).</summary>
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
        ProsequorModSystem.For(anvil.Api)?.VoxelWorkXp?.NotifyProgress(
            player,
            paid,
            target,
            EventFactBuilder.CodeOf(anvil.Block));
    }

    static bool TryMeasure(
        BlockEntityAnvil? anvil,
        out int good,
        out string? key,
        out ItemStack work)
    {
        good = 0;
        key = null;
        work = null!;
        ItemStack? stack = anvil?.WorkItemStack;
        if (anvil?.Api?.Side != EnumAppSide.Server
            || anvil.Voxels == null
            || anvil.SelectedRecipe == null
            || stack == null)
        {
            return false;
        }

        bool[,,]? want = anvil.recipeVoxels;
        if (want == null)
        {
            return false;
        }

        key = RecipeKeyOf(anvil.SelectedRecipe);
        if (key == null)
        {
            return false;
        }

        work = stack;
        int layers = Math.Min(AnvilVoxelGrid.SizeY, anvil.SelectedRecipe.QuantityLayers);
        good = VoxelWorkXpMath.CountGood(anvil.Voxels, want, layers, AnvilVoxelGrid.Metal);
        return true;
    }

    static VoxelWorkXp.Mark Read(ItemStack work)
    {
        ITreeAttribute? tree = work.Attributes;
        if (tree == null)
        {
            return default;
        }

        string? recipe = tree.GetString(RecipeKeyAttr);
        return new VoxelWorkXp.Mark(
            Math.Max(0, tree.GetInt(HighWaterAttr)),
            string.IsNullOrWhiteSpace(recipe) ? null : recipe.Trim());
    }

    static void Save(
        BlockEntityAnvil anvil,
        ItemStack work,
        VoxelWorkXp.Mark current,
        VoxelWorkXp.Mark next)
    {
        if (current.HighWater == next.HighWater
            && string.Equals(current.RecipeKey, next.RecipeKey, StringComparison.Ordinal))
        {
            return;
        }

        work.Attributes ??= new TreeAttribute();
        work.Attributes.SetInt(HighWaterAttr, next.HighWater);
        if (string.IsNullOrEmpty(next.RecipeKey))
        {
            work.Attributes.RemoveAttribute(RecipeKeyAttr);
        }
        else
        {
            work.Attributes.SetString(RecipeKeyAttr, next.RecipeKey);
        }

        anvil.MarkDirty(redrawOnClient: false);
    }
}
