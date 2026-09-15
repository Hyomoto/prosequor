using Newtonsoft.Json.Linq;
using Prosequor.Ability.Hooks;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace Prosequor.Ability.Actions;

public sealed class ReplaceWithVariantParams
{
    public required string Variant { get; init; }
}

/// <summary>
/// Swaps each stack for a random replacement from <see cref="CollectibleVariantTable"/>
/// under <c>{firstCodePart}-{variant}</c>. Gate with a nested <c>prosequor:chance</c>
/// <c>onSuccess</c> on the same stacks phase.
/// </summary>
public sealed class ReplaceWithVariantAction
    : AbilityActionHandler<MutateProcessContext, IReadOnlyList<ItemStack>, ReplaceWithVariantParams>
{
    public override ActionId Id => ActionIds.ReplaceWithVariant;
    public override HookId Hook => HookIds.BlockInteraction;
    public override VerbId Verb => VerbIds.MutateProcess;
    public override PhaseId Phase => HookIds.Stacks;

    protected override bool TryParse(
        JObject? raw,
        out ReplaceWithVariantParams? parameters,
        out string error)
    {
        parameters = null;
        string? variant = raw?.Value<string>("variant")?.Trim();
        if (string.IsNullOrWhiteSpace(variant))
        {
            error = "variant is required.";
            return false;
        }

        parameters = new ReplaceWithVariantParams { Variant = variant };
        error = "";
        return true;
    }

    protected override IReadOnlyList<ItemStack> Apply(
        MutateProcessContext context,
        IReadOnlyList<ItemStack> value,
        ReplaceWithVariantParams parameters,
        AbilityRuleSource source)
    {
        if (value.Count == 0)
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

            string key = CollectibleVariantTable.Key(
                current.Collectible.Code.FirstCodePart(),
                parameters.Variant);
            if (!context.Variants.TryPick(
                    key,
                    current.Collectible,
                    context.World.Rand,
                    out CollectibleObject pick))
            {
                next.Add(current);
                continue;
            }

            changed = true;
            next.Add(BuildReplacement(current, pick, context.Player));
        }

        return changed ? next : value;
    }

    static ItemStack BuildReplacement(ItemStack current, CollectibleObject pick, IPlayer? player)
    {
        ItemStack next = new(pick, current.StackSize);
        string? defaultType = pick.Attributes?["defaultType"].AsString();
        if (defaultType != null)
        {
            next.Attributes.SetString("type", defaultType);
        }

        ITreeAttribute? temp = current.Attributes.GetTreeAttribute("temperature");
        if (temp != null)
        {
            float temperature = temp.GetFloat("temperature");
            double lastUpdate = temp.GetDouble("temperatureLastUpdate");
            if (temperature > 50f)
            {
                ITreeAttribute dest = next.Attributes.GetOrAddTreeAttribute("temperature");
                dest.SetFloat("temperature", temperature);
                dest.SetDouble("temperatureLastUpdate", lastUpdate);
            }
        }

        CraftAttribution.StampMaker(next, player);
        return next;
    }
}
