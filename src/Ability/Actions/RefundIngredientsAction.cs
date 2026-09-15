using Newtonsoft.Json.Linq;
using Prosequor.Ability.Hooks;
using Vintagestory.API.Common;

namespace Prosequor.Ability.Actions;

public sealed class RefundIngredientsParams
{
    /// <summary>Collection id of ingredients to restock (<c>thread</c>, not <c>&lt;thread&gt;</c>).</summary>
    public required string Match { get; init; }

    /// <summary>Maximum units to refund.</summary>
    public int Amount { get; init; }

    /// <summary>Minimum units that must stay consumed.</summary>
    public int Retain { get; init; }
}

/// <summary>
/// Restocks craft-grid ingredients matching <c>params.match</c> after consume.
/// <c>toReturn = min(amount, max(0, consumed - retain))</c>.
/// </summary>
public sealed class RefundIngredientsOnCraftAction
    : AbilityActionHandler<CraftMutateOutputContext, int, RefundIngredientsParams>
{
    public override ActionId Id => ActionIds.RefundIngredients;
    public override HookId Hook => HookIds.CraftingInteraction;
    public override VerbId Verb => VerbIds.MutateOutput;
    public override PhaseId Phase => HookIds.Refund;

    protected override bool TryParse(
        JObject? raw,
        out RefundIngredientsParams? parameters,
        out string error)
    {
        parameters = null;
        string? matchRaw = raw?.Value<string>("match")?.Trim();
        if (string.IsNullOrWhiteSpace(matchRaw))
        {
            error = "match is required.";
            return false;
        }

        string match = AbilityRuleCompiler.NormalizeCollectionParam(matchRaw);
        if (string.IsNullOrWhiteSpace(match))
        {
            error = "match is required.";
            return false;
        }

        int amount = raw?.Value<int?>("amount") ?? int.MinValue;
        if (amount < 0)
        {
            error = "amount must be >= 0.";
            return false;
        }

        int retain = raw?.Value<int?>("retain") ?? 0;
        if (retain < 0)
        {
            error = "retain must be >= 0.";
            return false;
        }

        parameters = new RefundIngredientsParams
        {
            Match = match,
            Amount = amount,
            Retain = retain
        };
        error = "";
        return true;
    }

    protected override int Apply(
        CraftMutateOutputContext context,
        int value,
        RefundIngredientsParams parameters,
        AbilityRuleSource source)
    {
        _ = source;
        if (context.Player == null
            || context.CraftInventory == null
            || context.IngredientSnapshot == null
            || context.IngredientSnapshot.Count == 0
            || context.Collections == null
            || parameters.Amount <= 0)
        {
            return value;
        }

        int before = CraftMutateOutputStation.CountMatchingUnits(
            context.IngredientSnapshot,
            context.Collections,
            parameters.Match);
        int after = CraftMutateOutputStation.CountMatchingUnitsInGrid(
            context.CraftInventory,
            context.Collections,
            parameters.Match);
        int consumed = before - after;
        int toReturn = CraftMutateOutputStation.ComputeRefund(parameters.Amount, parameters.Retain, consumed);
        if (toReturn <= 0)
        {
            return value;
        }

        ItemStack? template = CraftMutateOutputStation.FindFirstMatching(
            context.IngredientSnapshot,
            context.Collections,
            parameters.Match);
        if (template?.Collectible == null)
        {
            return value;
        }

        CraftMutateOutputStation.RestockMatching(
            context.Player,
            context.CraftInventory,
            template,
            toReturn);
        return value + toReturn;
    }
}
