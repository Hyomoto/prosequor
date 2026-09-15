using Newtonsoft.Json.Linq;
using Prosequor.Ability.Hooks;

namespace Prosequor.Ability.Actions;

/// <summary>
/// Sets <c>harvest-skep</c> / <c>allow-right-click-harvest</c> to true (1).
/// No params. Compose memo is skipped (allow fold).
/// </summary>
public sealed class SetTrueHarvestSkepAllowAction
    : AbilityActionHandler<SkepHarvestContext, int, object>
{
    public override ActionId Id => ActionIds.SetTrue;
    public override HookId Hook => HookIds.BlockInteraction;
    public override VerbId Verb => VerbIds.HarvestSkep;
    public override PhaseId Phase => HookIds.AllowRightClickHarvest;

    protected override bool TryParse(JObject? raw, out object? parameters, out string error)
    {
        parameters = new object();
        error = "";
        return true;
    }

    protected override int Apply(
        SkepHarvestContext context,
        int value,
        object parameters,
        AbilityRuleSource source) =>
        1;
}

/// <summary>
/// Sets <c>harvest-bloomery</c> / <c>allow-right-click-harvest</c> to true (1).
/// No params. Compose memo is skipped (allow fold).
/// </summary>
public sealed class SetTrueHarvestBloomeryAllowAction
    : AbilityActionHandler<BloomeryHarvestContext, int, object>
{
    public override ActionId Id => ActionIds.SetTrue;
    public override HookId Hook => HookIds.BlockInteraction;
    public override VerbId Verb => VerbIds.HarvestBloomery;
    public override PhaseId Phase => HookIds.AllowRightClickHarvest;

    protected override bool TryParse(JObject? raw, out object? parameters, out string error)
    {
        parameters = new object();
        error = "";
        return true;
    }

    protected override int Apply(
        BloomeryHarvestContext context,
        int value,
        object parameters,
        AbilityRuleSource source) =>
        1;
}
