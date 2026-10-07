using Prosequor.Ability;
using Prosequor.Ability.Hooks;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace Prosequor.Xp;

/// <summary>
/// Startup catalog of smithing recipes for <c>smithing-formed</c> collection membership
/// and voxels-per-unit lookup. Keys match <see cref="AnvilXpStation.RecipeKeyOf"/>.
/// </summary>
public sealed class SmithingRecipeCatalog : IVoxelPerUnitCatalog
{
    readonly HashSet<string> outputCodes;
    readonly Dictionary<string, int> voxelsPerUnit;
    readonly int minVoxelsPerUnit;
    readonly int maxVoxelsPerUnit;

    public IReadOnlyCollection<string> OutputCodes => outputCodes;

    public int RecipeCount { get; }

    public int MinVoxelsPerUnit => minVoxelsPerUnit;

    public int MaxVoxelsPerUnit => maxVoxelsPerUnit;

    SmithingRecipeCatalog(
        HashSet<string> outputCodes,
        Dictionary<string, int> voxelsPerUnit,
        int recipeCount,
        int minVoxelsPerUnit,
        int maxVoxelsPerUnit)
    {
        this.outputCodes = outputCodes;
        this.voxelsPerUnit = voxelsPerUnit;
        RecipeCount = recipeCount;
        this.minVoxelsPerUnit = minVoxelsPerUnit;
        this.maxVoxelsPerUnit = maxVoxelsPerUnit;
    }

    public static SmithingRecipeCatalog Build(ICoreAPI api)
    {
        HashSet<string> outputs = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, int> perUnit = new(StringComparer.OrdinalIgnoreCase);
        int recipes = 0;
        int min = int.MaxValue;
        int max = 0;

        List<SmithingRecipe>? list = api?.GetSmithingRecipes();
        if (list == null)
        {
            return new SmithingRecipeCatalog(outputs, perUnit, 0, 0, 0);
        }

        foreach (SmithingRecipe recipe in list)
        {
            recipes++;
            RecordOutputCodes(recipe.Output, outputs);
            string? key = AnvilXpStation.RecipeKeyOf(recipe);
            if (key == null || perUnit.ContainsKey(key))
            {
                continue;
            }

            int layers = Math.Min(6, Math.Max(0, recipe.QuantityLayers));
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

        return new SmithingRecipeCatalog(outputs, perUnit, recipes, min, max);
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

    /// <summary>Fill builtin <c>smithing-formed</c> collection from recipe outputs.</summary>
    public void FillSmithingFormedCollection(CollectionIndex index)
    {
        const string id = "smithing-formed";
        index.EnsureKey(id);
        foreach (string code in outputCodes)
        {
            index.AddCode(id, code);
        }
    }

    static void RecordOutputCodes(JsonItemStack? output, HashSet<string> outputs)
    {
        if (output == null)
        {
            return;
        }

        AddOutputCode(outputs, output.Code?.ToString());
        AddOutputCode(outputs, output.ResolvedItemstack?.Collectible?.Code?.ToString());
    }

    static void AddOutputCode(HashSet<string> outputs, string? code)
    {
        if (!string.IsNullOrWhiteSpace(code))
        {
            outputs.Add(code.Trim());
        }
    }
}
