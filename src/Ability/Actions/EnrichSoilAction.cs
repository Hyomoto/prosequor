using Newtonsoft.Json.Linq;
using Prosequor.Ability.Hooks;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace Prosequor.Ability.Actions;

public sealed class EnrichSoilParams
{
    public float MaxFertility { get; init; }
}

/// <summary>
/// Enriches soil/dirt stacks in a drop list using a farmland nutrient snapshot on
/// <see cref="DropsContext"/> (XSkills Recycler parity). Unrelated stacks pass through.
/// </summary>
public sealed class EnrichSoilAction
    : AbilityActionHandler<DropsContext, IReadOnlyList<ItemStack>, EnrichSoilParams>
{
    public override ActionId Id => ActionIds.EnrichSoil;
    public override HookId Hook => HookIds.BlockInteraction;
    public override VerbId Verb => VerbIds.MutateDrops;
    public override PhaseId Phase => HookIds.Stacks;

    protected override bool TryParse(
        JObject? raw,
        out EnrichSoilParams? parameters,
        out string error)
    {
        parameters = null;
        float maxFertility = raw?.Value<float?>("maxFertility") ?? 0f;
        if (maxFertility < 0f)
        {
            error = "maxFertility must be >= 0.";
            return false;
        }

        parameters = new EnrichSoilParams { MaxFertility = maxFertility };
        error = "";
        return true;
    }

    protected override IReadOnlyList<ItemStack> Apply(
        DropsContext context,
        IReadOnlyList<ItemStack> value,
        EnrichSoilParams parameters,
        AbilityRuleSource source)
    {
        if (value.Count == 0)
        {
            return value;
        }

        Block? soil = TryResolveSoilBlock(
            context.World,
            context.FarmlandNutrients,
            context.FarmlandOriginalFertility,
            parameters.MaxFertility);
        if (soil == null)
        {
            return value;
        }

        List<ItemStack> next = new(value.Count);
        bool changed = false;
        foreach (ItemStack? current in value)
        {
            if (current?.Collectible?.Code == null)
            {
                if (current != null)
                {
                    next.Add(current);
                }

                continue;
            }

            if (!IsSoilOrDirt(current.Collectible))
            {
                next.Add(current);
                continue;
            }

            changed = true;
            next.Add(new ItemStack(soil, current.StackSize));
        }

        return changed ? next : value;
    }

    internal static bool IsSoilOrDirt(CollectibleObject collectible)
    {
        string? part = collectible.Code?.FirstCodePart();
        return string.Equals(part, "soil", StringComparison.OrdinalIgnoreCase)
            || string.Equals(part, "dirt", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Resolves <c>soil-{fertility}-none</c> from nutrient / original-fertility arrays
    /// and a max-fertility ceiling.
    /// </summary>
    internal static Block? TryResolveSoilBlock(
        IWorldAccessor? world,
        float[]? nutrients,
        int[]? originalFertility,
        float maxFertility)
    {
        if (world == null
            || nutrients == null
            || nutrients.Length == 0
            || originalFertility == null
            || originalFertility.Length == 0)
        {
            return null;
        }

        string? fertilityKey = TryResolveFertilityKey(nutrients, originalFertility, maxFertility);
        if (fertilityKey == null)
        {
            return null;
        }

        return world.GetBlock(new AssetLocation("game", "soil-" + fertilityKey + "-none"));
    }

    /// <summary>
    /// Picks the nearest fertility tier at or below the nutrient/maxFertility ceiling.
    /// </summary>
    internal static string? TryResolveFertilityKey(
        float[] nutrients,
        int[] originalFertility,
        float maxFertility)
    {
        if (nutrients.Length == 0 || originalFertility.Length == 0)
        {
            return null;
        }

        float originalMin = originalFertility.Min();
        float ceiling = Math.Max(maxFertility, originalMin);
        float target = Math.Min(nutrients.Min(), ceiling);

        string fertilityKey = "verylow";
        float bestDistance = float.MaxValue;
        bool found = false;
        foreach (KeyValuePair<string, float> entry in BlockEntitySoilNutrition.Fertilities)
        {
            if (entry.Value > ceiling + 0.1f)
            {
                continue;
            }

            float distance = Math.Abs(entry.Value - target);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                fertilityKey = entry.Key;
                found = true;
            }
        }

        return found ? fertilityKey : "verylow";
    }
}
