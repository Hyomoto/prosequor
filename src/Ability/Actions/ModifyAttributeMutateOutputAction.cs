using Newtonsoft.Json.Linq;
using Prosequor.Ability.Hooks;
using Vintagestory.API.Common;

namespace Prosequor.Ability.Actions;

public sealed class ModifyAttributeParams
{
    public required string Key { get; init; }
    public required NumberSpec Spec { get; init; }
}

/// <summary>
/// ItemStack projector for craft attribute factors: reads the stamped factor for
/// <c>key</c>, runs a <see cref="NumberSpec"/>, writes the absolute result.
/// </summary>
public sealed class ModifyAttributeMutateOutputAction
    : AbilityActionHandler<CraftMutateOutputContext, ItemStack, ModifyAttributeParams>
{
    public override ActionId Id => ActionIds.ModifyAttribute;
    public override HookId Hook => HookIds.CraftingInteraction;
    public override VerbId Verb => VerbIds.MutateOutput;
    public override PhaseId Phase => HookIds.Attributes;

    protected override bool TryParse(JObject? raw, out ModifyAttributeParams? parameters, out string error)
    {
        parameters = null;
        string? key = raw?.Value<string>("key")?.Trim();
        if (string.IsNullOrWhiteSpace(key))
        {
            error = "key is required.";
            return false;
        }

        if (!NumberSpec.TryParse(raw, out NumberSpec? spec, out error) || spec == null)
        {
            return false;
        }

        parameters = new ModifyAttributeParams { Key = key, Spec = spec };
        error = "";
        return true;
    }

    protected override ItemStack Apply(
        CraftMutateOutputContext context,
        ItemStack value,
        ModifyAttributeParams parameters,
        AbilityRuleSource source)
    {
        if (value == null)
        {
            return value!;
        }

        float current = CraftAttributeMods.GetFactor(value, parameters.Key);
        float next = parameters.Spec.Apply(current, context, source);
        AbilityBootstrap.AttributeMutators.Set(value, parameters.Key, next, context.World);
        return value;
    }
}
