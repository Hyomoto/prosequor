using Prosequor.Xp;
using Prosequor.Xp.Activity;
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

    struct HammerMaterials
    {
        public int Slag;
        public int Metal;
        public bool Armed;
    }

    [ThreadStatic]
    static HammerMaterials hammerMaterials;

    /// <summary>Slag and metal counts before a split or a Knapster voxel edit.</summary>
    public static void ArmHammerMaterials(BlockEntityAnvil? anvil)
    {
        hammerMaterials = default;
        byte[,,]? voxels = anvil?.Voxels;
        if (voxels == null)
        {
            return;
        }

        hammerMaterials = new HammerMaterials
        {
            Armed = true,
            Slag = AnvilVoxelGrid.CountMaterial(voxels, AnvilVoxelGrid.Slag),
            Metal = AnvilVoxelGrid.CountMaterial(voxels, AnvilVoxelGrid.Metal),
        };
    }

    public static void DisarmHammerMaterials() => hammerMaterials = default;

    /// <summary>
    /// One <c>hammer-slag</c> deed per slag voxel cleared since
    /// <see cref="ArmHammerMaterials"/>. When <paramref name="splits"/> is set, one
    /// <c>hammer-split</c> deed per metal voxel removed.
    /// </summary>
    public static void EmitHammerMaterials(BlockEntityAnvil? anvil, IPlayer? player, bool splits)
    {
        HammerMaterials before = hammerMaterials;
        hammerMaterials = default;
        if (!before.Armed || anvil?.Voxels == null)
        {
            return;
        }

        int slag = Math.Max(0, before.Slag - AnvilVoxelGrid.CountMaterial(anvil.Voxels, AnvilVoxelGrid.Slag));
        int metal = splits
            ? Math.Max(0, before.Metal - AnvilVoxelGrid.CountMaterial(anvil.Voxels, AnvilVoxelGrid.Metal))
            : 0;
        EmitHammer(anvil, player, DeedToken.HammerSlag, slag);
        EmitHammer(anvil, player, DeedToken.HammerSplit, metal);
    }

    /// <summary>One <c>hammer-slag</c> deed per voxel a heavy hit just cleared.</summary>
    public static void EmitHammerSlag(BlockEntityAnvil? anvil, IPlayer? player, int count) =>
        EmitHammer(anvil, player, DeedToken.HammerSlag, count);

    static void EmitHammer(BlockEntityAnvil? anvil, IPlayer? player, DeedToken token, int count)
    {
        if (count <= 0 || anvil?.Api?.Side != EnumAppSide.Server || player == null)
        {
            return;
        }

        string? caller = EventFactBuilder.CodeOf(anvil.Block);
        if (string.IsNullOrWhiteSpace(caller))
        {
            return;
        }

        string? target = anvil.SelectedRecipe?.Output?.Code?.ToString()
            ?? anvil.SelectedRecipe?.Output?.ResolvedItemstack?.Collectible?.Code?.ToString();
        for (int i = 0; i < count; i++)
        {
            Deed.Emit(anvil.Api, player.PlayerUID, token, caller: caller, target: target);
        }
    }

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
