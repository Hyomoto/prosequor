using Prosequor.Ability;
using Prosequor.Ability.Hooks;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace Prosequor.Xp;

/// <summary>
/// Startup catalog of smithing recipes for <c>smithing-formed</c> collection membership.
/// Keys match <see cref="AnvilXpStation.RecipeKeyOf"/>.
/// </summary>
public sealed class SmithingRecipeCatalog
{
    readonly HashSet<string> outputCodes;

    public IReadOnlyCollection<string> OutputCodes => outputCodes;

    public int RecipeCount { get; }

    SmithingRecipeCatalog(HashSet<string> outputCodes, int recipeCount)
    {
        this.outputCodes = outputCodes;
        RecipeCount = recipeCount;
    }

    public static SmithingRecipeCatalog Build(ICoreAPI api)
    {
        HashSet<string> outputs = new(StringComparer.OrdinalIgnoreCase);
        int recipes = 0;

        List<SmithingRecipe>? list = api?.GetSmithingRecipes();
        if (list == null)
        {
            return new SmithingRecipeCatalog(outputs, 0);
        }

        foreach (SmithingRecipe recipe in list)
        {
            recipes++;
            RecordOutputCodes(recipe.Output, outputs);
        }

        return new SmithingRecipeCatalog(outputs, recipes);
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
