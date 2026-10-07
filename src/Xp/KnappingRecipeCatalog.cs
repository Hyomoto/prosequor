using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace Prosequor.Xp;

/// <summary>
/// Startup catalog of knapping recipes for voxels-per-unit lookup.
/// Keys match <see cref="VoxelWorkXp.RecipeKeyOf"/> on the recipe name.
/// One layer. Does not fill a collection.
/// </summary>
public sealed class KnappingRecipeCatalog : IVoxelPerUnitCatalog
{
    readonly Dictionary<string, int> voxelsPerUnit;
    readonly int minVoxelsPerUnit;
    readonly int maxVoxelsPerUnit;

    public int RecipeCount => voxelsPerUnit.Count;

    public int MinVoxelsPerUnit => minVoxelsPerUnit;

    public int MaxVoxelsPerUnit => maxVoxelsPerUnit;

    KnappingRecipeCatalog(
        Dictionary<string, int> voxelsPerUnit,
        int minVoxelsPerUnit,
        int maxVoxelsPerUnit)
    {
        this.voxelsPerUnit = voxelsPerUnit;
        this.minVoxelsPerUnit = minVoxelsPerUnit;
        this.maxVoxelsPerUnit = maxVoxelsPerUnit;
    }

    public static string? RecipeKeyOf(KnappingRecipe? recipe) =>
        VoxelWorkXp.RecipeKeyOf(recipe?.Name);

    public static KnappingRecipeCatalog Build(ICoreAPI api)
    {
        Dictionary<string, int> perUnit = new(StringComparer.OrdinalIgnoreCase);
        int min = int.MaxValue;
        int max = 0;

        List<KnappingRecipe>? list = api?.GetKnappingRecipes();
        if (list == null)
        {
            return new KnappingRecipeCatalog(perUnit, 0, 0);
        }

        foreach (KnappingRecipe recipe in list)
        {
            string? key = RecipeKeyOf(recipe);
            if (key == null || perUnit.ContainsKey(key))
            {
                continue;
            }

            int layers = Math.Min(1, Math.Max(0, recipe.QuantityLayers));
            int voxels = ClayFormingRecipeCatalog.VoxelsPerUnit(recipe.Voxels, layers, recipe.Output?.StackSize ?? 1);
            perUnit[key] = voxels;
            if (voxels < min)
            {
                min = voxels;
            }

            if (voxels > max)
            {
                max = voxels;
            }
        }

        if (perUnit.Count == 0)
        {
            min = 0;
            max = 0;
        }

        return new KnappingRecipeCatalog(perUnit, min, max);
    }

    public bool TryGetVoxelsPerUnit(string? recipeKey, out int voxels)
    {
        voxels = 0;
        if (string.IsNullOrWhiteSpace(recipeKey))
        {
            return false;
        }

        return voxelsPerUnit.TryGetValue(recipeKey.Trim(), out voxels);
    }
}
