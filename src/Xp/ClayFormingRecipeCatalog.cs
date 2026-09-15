using Prosequor.Ability.Hooks;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace Prosequor.Xp;

/// <summary>
/// Startup catalog of clayforming recipes: voxel counts and output stack sizes for
/// fire-XP amount-table lookup. Keys match <see cref="Ability.ClayFormXpStation.RecipeKeyOf"/>.
/// The voxel table dedups by recipe name (bowl.json blue/fire/red share one Name);
/// every variant's <c>Output.Code</c> and resolved collectible still join <c>clay-formed</c>.
/// </summary>
public sealed class ClayFormingRecipeCatalog
{
    readonly Dictionary<string, RecipeInfo> byKey;
    readonly int minVoxelsPerUnit;
    readonly int maxVoxelsPerUnit;

    public readonly record struct RecipeInfo(
        int TotalVoxels,
        int OutputCount,
        int VoxelsPerUnit,
        string? OutputCode);

    public int MinVoxelsPerUnit => minVoxelsPerUnit;
    public int MaxVoxelsPerUnit => maxVoxelsPerUnit;
    public int RecipeCount => byKey.Count;

    readonly HashSet<string> outputCodes;

    public IReadOnlyCollection<string> OutputCodes => outputCodes;

    ClayFormingRecipeCatalog(
        Dictionary<string, RecipeInfo> byKey,
        HashSet<string> outputCodes,
        int minVoxelsPerUnit,
        int maxVoxelsPerUnit)
    {
        this.byKey = byKey;
        this.outputCodes = outputCodes;
        this.minVoxelsPerUnit = minVoxelsPerUnit;
        this.maxVoxelsPerUnit = maxVoxelsPerUnit;
    }

    public static ClayFormingRecipeCatalog Build(ICoreAPI api)
    {
        Dictionary<string, RecipeInfo> map = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> outputs = new(StringComparer.OrdinalIgnoreCase);
        int min = int.MaxValue;
        int max = 0;

        List<ClayFormingRecipe>? recipes = api?.GetClayformingRecipes();
        if (recipes == null)
        {
            return new ClayFormingRecipeCatalog(map, outputs, 0, 0);
        }

        foreach (ClayFormingRecipe recipe in recipes)
        {
            string? outputCode = RecordOutputCodes(recipe.Output, outputs);
            string? key = Ability.ClayFormXpStation.RecipeKeyOf(recipe);
            if (key == null || map.ContainsKey(key))
            {
                continue;
            }

            int layers = Math.Min(16, Math.Max(0, recipe.QuantityLayers));
            int total = CountWantedVoxels(recipe.Voxels, layers);
            int outputCount = Math.Max(1, recipe.Output?.StackSize ?? 1);
            int perUnit = Math.Max(1, (int)Math.Ceiling(total / (double)outputCount));

            map[key] = new RecipeInfo(total, outputCount, perUnit, outputCode);
            if (perUnit < min)
            {
                min = perUnit;
            }

            if (perUnit > max)
            {
                max = perUnit;
            }
        }

        if (map.Count == 0)
        {
            min = 0;
            max = 0;
        }

        return new ClayFormingRecipeCatalog(map, outputs, min, max);
    }

    /// <summary>Fill builtin <c>clay-formed</c> collection from recipe outputs.</summary>
    public void FillClayFormedCollection(CollectionIndex index)
    {
        const string id = "clay-formed";
        index.EnsureKey(id);
        foreach (string code in outputCodes)
        {
            index.AddCode(id, code);
        }
    }

    public bool TryGet(string? recipeKey, out RecipeInfo info)
    {
        info = default;
        if (string.IsNullOrWhiteSpace(recipeKey))
        {
            return false;
        }

        return byKey.TryGetValue(recipeKey.Trim(), out info);
    }

    /// <summary>
    /// Adds the recipe output asset code and the resolved collectible code (they can differ
    /// before/after variant resolve). Returns <c>Output.Code</c> for the voxel-table row.
    /// </summary>
    static string? RecordOutputCodes(JsonItemStack? output, HashSet<string> outputs)
    {
        if (output == null)
        {
            return null;
        }

        string? outputCode = output.Code?.ToString();
        AddOutputCode(outputs, outputCode);
        AddOutputCode(outputs, output.ResolvedItemstack?.Collectible?.Code?.ToString());
        return string.IsNullOrWhiteSpace(outputCode) ? null : outputCode;
    }

    static void AddOutputCode(HashSet<string> outputs, string? code)
    {
        if (!string.IsNullOrWhiteSpace(code))
        {
            outputs.Add(code.Trim());
        }
    }

    public static int CountWantedVoxels(bool[,,]? want, int layers)
    {
        if (want == null || layers <= 0)
        {
            return 0;
        }

        int xSize = want.GetLength(0);
        int ySize = Math.Min(want.GetLength(1), layers);
        int zSize = want.GetLength(2);
        int count = 0;
        for (int x = 0; x < xSize; x++)
        {
            for (int y = 0; y < ySize; y++)
            {
                for (int z = 0; z < zSize; z++)
                {
                    if (want[x, y, z])
                    {
                        count++;
                    }
                }
            }
        }

        return count;
    }
}
