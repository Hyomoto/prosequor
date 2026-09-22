using Newtonsoft.Json.Linq;
using Prosequor.Ability.Hooks;
using Vintagestory.API.Common;

namespace Prosequor.Ability.Actions;

/// <summary>Float number fold for mounted phases.</summary>
public sealed class NumberMountedAction : AbilityActionHandler<MountedContext, float, NumberSpec>
{
    readonly PhaseId phase;

    public NumberMountedAction(PhaseId phase) => this.phase = phase;

    public override ActionId Id => ActionIds.Number;
    public override HookId Hook => HookIds.EntityInteraction;
    public override VerbId Verb => VerbIds.Mounted;
    public override PhaseId Phase => phase;

    protected override bool TryParse(JObject? raw, out NumberSpec? parameters, out string error) =>
        NumberSpec.TryParse(raw, out parameters, out error);

    protected override float Apply(
        MountedContext context,
        float value,
        NumberSpec parameters,
        AbilityRuleSource source) =>
        parameters.Apply(value, context, source);
}

/// <summary>Float number fold for repair add-durability.</summary>
public sealed class NumberRepairAddDurabilityAction
    : AbilityActionHandler<RepairContext, float, NumberSpec>
{
    public override ActionId Id => ActionIds.Number;
    public override HookId Hook => HookIds.ItemInteraction;
    public override VerbId Verb => VerbIds.Repair;
    public override PhaseId Phase => HookIds.AddDurability;

    protected override bool TryParse(JObject? raw, out NumberSpec? parameters, out string error) =>
        NumberSpec.TryParse(raw, out parameters, out error);

    protected override float Apply(
        RepairContext context,
        float value,
        NumberSpec parameters,
        AbilityRuleSource source) =>
        parameters.Apply(value, context, source);
}

/// <summary>Float number fold for clay-form auto-finish / place-conservation.</summary>
public sealed class NumberClayFormAction : AbilityActionHandler<VoxelWorkContext, float, NumberSpec>
{
    readonly PhaseId phase;

    public NumberClayFormAction(PhaseId phase) => this.phase = phase;

    public override ActionId Id => ActionIds.Number;
    public override HookId Hook => HookIds.ItemInteraction;
    public override VerbId Verb => VerbIds.ClayForm;
    public override PhaseId Phase => phase;

    protected override bool TryParse(JObject? raw, out NumberSpec? parameters, out string error) =>
        NumberSpec.TryParse(raw, out parameters, out error);

    protected override float Apply(
        VoxelWorkContext context,
        float value,
        NumberSpec parameters,
        AbilityRuleSource source) =>
        parameters.Apply(value, context, source);
}

/// <summary>Float number fold for fertilize absorb rate.</summary>
public sealed class NumberFertilizeAction
    : AbilityActionHandler<FertilizerAbsorbContext, float, NumberSpec>
{
    public override ActionId Id => ActionIds.Number;
    public override HookId Hook => HookIds.BlockInteraction;
    public override VerbId Verb => VerbIds.Fertilize;
    public override PhaseId Phase => HookIds.Default;

    protected override bool TryParse(JObject? raw, out NumberSpec? parameters, out string error) =>
        NumberSpec.TryParse(raw, out parameters, out error);

    protected override float Apply(
        FertilizerAbsorbContext context,
        float value,
        NumberSpec parameters,
        AbilityRuleSource source) =>
        parameters.Apply(value, context, source);
}

/// <summary>
/// Crop climate-window expand fraction fold (NumberSpec params; dedicated action id).
/// </summary>
public sealed class AdjustPlantClimateValueAction
    : AbilityActionHandler<PlantCropClimateContext, float, NumberSpec>
{
    public override ActionId Id => ActionIds.AdjustPlantClimateValue;
    public override HookId Hook => HookIds.BlockInteraction;
    public override VerbId Verb => VerbIds.PlantCrop;
    public override PhaseId Phase => HookIds.Default;

    protected override bool TryParse(JObject? raw, out NumberSpec? parameters, out string error) =>
        NumberSpec.TryParse(raw, out parameters, out error);

    protected override float Apply(
        PlantCropClimateContext context,
        float value,
        NumberSpec parameters,
        AbilityRuleSource source) =>
        parameters.Apply(value, context, source);
}

/// <summary>Int number fold for scythe multi-break quantity (truncate like legacy).</summary>
public sealed class NumberScytheMultiBreakQuantityAction
    : AbilityActionHandler<ScytheMultiBreakContext, int, NumberSpec>
{
    public override ActionId Id => ActionIds.Number;
    public override HookId Hook => HookIds.BlockInteraction;
    public override VerbId Verb => VerbIds.ScytheMultibreak;
    public override PhaseId Phase => HookIds.Quantity;

    protected override bool TryParse(JObject? raw, out NumberSpec? parameters, out string error) =>
        NumberSpec.TryParse(raw, out parameters, out error);

    protected override int Apply(
        ScytheMultiBreakContext context,
        int value,
        NumberSpec parameters,
        AbilityRuleSource source) =>
        (int)parameters.Apply(value, context, source);
}

/// <summary>Int number fold for trough-fill quantity (truncate).</summary>
public sealed class NumberTroughFillQuantityAction
    : AbilityActionHandler<TroughFillContext, int, NumberSpec>
{
    public override ActionId Id => ActionIds.Number;
    public override HookId Hook => HookIds.BlockInteraction;
    public override VerbId Verb => VerbIds.TroughFill;
    public override PhaseId Phase => HookIds.Quantity;

    protected override bool TryParse(JObject? raw, out NumberSpec? parameters, out string error) =>
        NumberSpec.TryParse(raw, out parameters, out error);

    protected override int Apply(
        TroughFillContext context,
        int value,
        NumberSpec parameters,
        AbilityRuleSource source) =>
        (int)parameters.Apply(value, context, source);
}

/// <summary>Float number fold for skep angry-bee spawn chance.</summary>
public sealed class NumberSpawnBeesChanceAction
    : AbilityActionHandler<SkepBeeSpawnContext, float, NumberSpec>
{
    public override ActionId Id => ActionIds.Number;
    public override HookId Hook => HookIds.BlockInteraction;
    public override VerbId Verb => VerbIds.SpawnBeesChance;
    public override PhaseId Phase => HookIds.Default;

    protected override bool TryParse(JObject? raw, out NumberSpec? parameters, out string error) =>
        NumberSpec.TryParse(raw, out parameters, out error);

    protected override float Apply(
        SkepBeeSpawnContext context,
        float value,
        NumberSpec parameters,
        AbilityRuleSource source) =>
        parameters.Apply(value, context, source);
}

/// <summary>Float number fold for skep right-click harvest break chance.</summary>
public sealed class NumberHarvestSkepBreakChanceAction
    : AbilityActionHandler<SkepHarvestContext, float, NumberSpec>
{
    public override ActionId Id => ActionIds.Number;
    public override HookId Hook => HookIds.BlockInteraction;
    public override VerbId Verb => VerbIds.HarvestSkep;
    public override PhaseId Phase => HookIds.RightClickHarvestBreakChance;

    protected override bool TryParse(JObject? raw, out NumberSpec? parameters, out string error) =>
        NumberSpec.TryParse(raw, out parameters, out error);

    protected override float Apply(
        SkepHarvestContext context,
        float value,
        NumberSpec parameters,
        AbilityRuleSource source) =>
        parameters.Apply(value, context, source);
}

/// <summary>Float number fold for bloomery right-click harvest break chance.</summary>
public sealed class NumberHarvestBloomeryBreakChanceAction
    : AbilityActionHandler<BloomeryHarvestContext, float, NumberSpec>
{
    public override ActionId Id => ActionIds.Number;
    public override HookId Hook => HookIds.BlockInteraction;
    public override VerbId Verb => VerbIds.HarvestBloomery;
    public override PhaseId Phase => HookIds.RightClickHarvestBreakChance;

    protected override bool TryParse(JObject? raw, out NumberSpec? parameters, out string error) =>
        NumberSpec.TryParse(raw, out parameters, out error);

    protected override float Apply(
        BloomeryHarvestContext context,
        float value,
        NumberSpec parameters,
        AbilityRuleSource source) =>
        parameters.Apply(value, context, source);
}

/// <summary>Float number fold for plant / cutting / seek-bobber success chance.</summary>
public sealed class NumberSuccessChanceAction
    : AbilityActionHandler<SuccessChanceContext, float, NumberSpec>
{
    readonly VerbId verb;

    public NumberSuccessChanceAction(VerbId verb) => this.verb = verb;

    public override ActionId Id => ActionIds.Number;
    public override HookId Hook => HookIds.BlockInteraction;
    public override VerbId Verb => verb;
    public override PhaseId Phase => HookIds.Default;

    protected override bool TryParse(JObject? raw, out NumberSpec? parameters, out string error) =>
        NumberSpec.TryParse(raw, out parameters, out error);

    protected override float Apply(
        SuccessChanceContext context,
        float value,
        NumberSpec parameters,
        AbilityRuleSource source) =>
        parameters.Apply(value, context, source);
}

/// <summary>Float number fold for plant growth-duration multiplier.</summary>
public sealed class NumberGrowthDurationAction
    : AbilityActionHandler<GrowthDurationContext, float, NumberSpec>
{
    readonly VerbId verb;

    public NumberGrowthDurationAction(VerbId verb) => this.verb = verb;

    public override ActionId Id => ActionIds.Number;
    public override HookId Hook => HookIds.BlockInteraction;
    public override VerbId Verb => verb;
    public override PhaseId Phase => HookIds.Growth;

    protected override bool TryParse(JObject? raw, out NumberSpec? parameters, out string error) =>
        NumberSpec.TryParse(raw, out parameters, out error);

    protected override float Apply(
        GrowthDurationContext context,
        float value,
        NumberSpec parameters,
        AbilityRuleSource source) =>
        parameters.Apply(value, context, source);
}

/// <summary>
/// Int number fold for craft-damaged amount with stochastic round after NumberSpec
/// (legacy scale-amount-by-percent behavior).
/// </summary>
public sealed class NumberCraftDamagedAmountAction
    : AbilityActionHandler<ItemDurabilityContext, int, NumberSpec>
{
    public override ActionId Id => ActionIds.Number;
    public override HookId Hook => HookIds.ItemInteraction;
    public override VerbId Verb => VerbIds.CraftDamaged;
    public override PhaseId Phase => HookIds.Amount;

    protected override bool TryParse(JObject? raw, out NumberSpec? parameters, out string error) =>
        NumberSpec.TryParse(raw, out parameters, out error);

    protected override int Apply(
        ItemDurabilityContext context,
        int value,
        NumberSpec parameters,
        AbilityRuleSource source)
    {
        if (value <= 0)
        {
            return value;
        }

        float next = parameters.Apply(value, context, source);
        Random rand = context.World?.Rand ?? new Random(0);
        return AbilityFormulas.StochasticRound(next, rand);
    }
}

/// <summary>Float number fold for animal behavior chance (flee / seek / …).</summary>
public sealed class NumberAnimalBehaviorChanceAction
    : AbilityActionHandler<AnimalBehaviorContext, float, NumberSpec>
{
    readonly VerbId verb;

    public NumberAnimalBehaviorChanceAction(VerbId verb) => this.verb = verb;

    public override ActionId Id => ActionIds.Number;
    public override HookId Hook => HookIds.EntityInteraction;
    public override VerbId Verb => verb;
    public override PhaseId Phase => HookIds.Chance;

    protected override bool TryParse(JObject? raw, out NumberSpec? parameters, out string error) =>
        NumberSpec.TryParse(raw, out parameters, out error);

    protected override float Apply(
        AnimalBehaviorContext context,
        float value,
        NumberSpec parameters,
        AbilityRuleSource source) =>
        parameters.Apply(value, context, source);
}

/// <summary>Int number fold for animal-behavior multiplier (truncate).</summary>
public sealed class NumberAnimalBehaviorMultiplierAction
    : AbilityActionHandler<AnimalBehaviorContext, int, NumberSpec>
{
    readonly VerbId verb;

    public NumberAnimalBehaviorMultiplierAction(VerbId verb) => this.verb = verb;

    public override ActionId Id => ActionIds.Number;
    public override HookId Hook => HookIds.EntityInteraction;
    public override VerbId Verb => verb;
    public override PhaseId Phase => HookIds.Multiplier;

    protected override bool TryParse(JObject? raw, out NumberSpec? parameters, out string error) =>
        NumberSpec.TryParse(raw, out parameters, out error);

    protected override int Apply(
        AnimalBehaviorContext context,
        int value,
        NumberSpec parameters,
        AbilityRuleSource source) =>
        (int)parameters.Apply(value, context, source);
}

/// <summary>Int number fold for field-work size (truncate).</summary>
public sealed class NumberFieldWorkSizeAction
    : AbilityActionHandler<FieldWorkContext, int, NumberSpec>
{
    public override ActionId Id => ActionIds.Number;
    public override HookId Hook => HookIds.BlockInteraction;
    public override VerbId Verb => VerbIds.FieldWork;
    public override PhaseId Phase => HookIds.Size;

    protected override bool TryParse(JObject? raw, out NumberSpec? parameters, out string error) =>
        NumberSpec.TryParse(raw, out parameters, out error);

    protected override int Apply(
        FieldWorkContext context,
        int value,
        NumberSpec parameters,
        AbilityRuleSource source) =>
        (int)parameters.Apply(value, context, source);
}

/// <summary>Int number fold for clay-form assist-radius (truncate).</summary>
public sealed class NumberClayFormAssistRadiusAction
    : AbilityActionHandler<VoxelWorkContext, int, NumberSpec>
{
    public override ActionId Id => ActionIds.Number;
    public override HookId Hook => HookIds.ItemInteraction;
    public override VerbId Verb => VerbIds.ClayForm;
    public override PhaseId Phase => HookIds.AssistRadius;

    protected override bool TryParse(JObject? raw, out NumberSpec? parameters, out string error) =>
        NumberSpec.TryParse(raw, out parameters, out error);

    protected override int Apply(
        VoxelWorkContext context,
        int value,
        NumberSpec parameters,
        AbilityRuleSource source) =>
        (int)parameters.Apply(value, context, source);
}

/// <summary>Int number fold for anvil-heavy-hit slag-radius / assist-radius / move-count.</summary>
public sealed class NumberAnvilHeavyHitAction
    : AbilityActionHandler<VoxelWorkContext, int, NumberSpec>
{
    readonly PhaseId phase;

    public NumberAnvilHeavyHitAction(PhaseId phase) => this.phase = phase;

    public override ActionId Id => ActionIds.Number;
    public override HookId Hook => HookIds.ItemInteraction;
    public override VerbId Verb => VerbIds.AnvilHeavyHit;
    public override PhaseId Phase => phase;

    protected override bool TryParse(JObject? raw, out NumberSpec? parameters, out string error) =>
        NumberSpec.TryParse(raw, out parameters, out error);

    protected override int Apply(
        VoxelWorkContext context,
        int value,
        NumberSpec parameters,
        AbilityRuleSource source) =>
        (int)parameters.Apply(value, context, source);
}

/// <summary>Int number fold for anvil-split bits-refund.</summary>
public sealed class NumberAnvilSplitAction
    : AbilityActionHandler<VoxelWorkContext, int, NumberSpec>
{
    public override ActionId Id => ActionIds.Number;
    public override HookId Hook => HookIds.ItemInteraction;
    public override VerbId Verb => VerbIds.AnvilSplit;
    public override PhaseId Phase => HookIds.BitsRefund;

    protected override bool TryParse(JObject? raw, out NumberSpec? parameters, out string error) =>
        NumberSpec.TryParse(raw, out parameters, out error);

    protected override int Apply(
        VoxelWorkContext context,
        int value,
        NumberSpec parameters,
        AbilityRuleSource source) =>
        (int)parameters.Apply(value, context, source);
}

/// <summary>Float number fold for anvil-strike decay-shrink.</summary>
public sealed class NumberAnvilStrikeDecayShrinkAction
    : AbilityActionHandler<VoxelWorkContext, float, NumberSpec>
{
    public override ActionId Id => ActionIds.Number;
    public override HookId Hook => HookIds.ItemInteraction;
    public override VerbId Verb => VerbIds.AnvilStrike;
    public override PhaseId Phase => HookIds.DecayShrink;

    protected override bool TryParse(JObject? raw, out NumberSpec? parameters, out string error) =>
        NumberSpec.TryParse(raw, out parameters, out error);

    protected override float Apply(
        VoxelWorkContext context,
        float value,
        NumberSpec parameters,
        AbilityRuleSource source) =>
        (float)parameters.Apply(value, context, source);
}

/// <summary>Int number fold for voxel-copy / voxel-refill default (truncate).</summary>
public sealed class NumberVoxelWorkDefaultAction
    : AbilityActionHandler<VoxelWorkContext, int, NumberSpec>
{
    readonly VerbId verb;

    public NumberVoxelWorkDefaultAction(VerbId verb) => this.verb = verb;

    public override ActionId Id => ActionIds.Number;
    public override HookId Hook => HookIds.ItemInteraction;
    public override VerbId Verb => verb;
    public override PhaseId Phase => HookIds.Default;

    protected override bool TryParse(JObject? raw, out NumberSpec? parameters, out string error) =>
        NumberSpec.TryParse(raw, out parameters, out error);

    protected override int Apply(
        VoxelWorkContext context,
        int value,
        NumberSpec parameters,
        AbilityRuleSource source) =>
        (int)parameters.Apply(value, context, source);
}

/// <summary>
/// Int number fold for block-damaged / item-damage amount (truncate).
/// Craft-damaged uses <see cref="NumberCraftDamagedAmountAction"/> (stochastic round).
/// </summary>
public sealed class NumberItemDurabilityAmountAction
    : AbilityActionHandler<ItemDurabilityContext, int, NumberSpec>
{
    readonly VerbId verb;

    public NumberItemDurabilityAmountAction(VerbId verb) => this.verb = verb;

    public override ActionId Id => ActionIds.Number;
    public override HookId Hook => HookIds.ItemInteraction;
    public override VerbId Verb => verb;
    public override PhaseId Phase => HookIds.Amount;

    protected override bool TryParse(JObject? raw, out NumberSpec? parameters, out string error) =>
        NumberSpec.TryParse(raw, out parameters, out error);

    protected override int Apply(
        ItemDurabilityContext context,
        int value,
        NumberSpec parameters,
        AbilityRuleSource source) =>
        (int)parameters.Apply(value, context, source);
}

/// <summary>
/// Float number fold for progress / skill-xp / amount.
/// Own-skill only: Digging rules cannot boost Forestry XP.
/// </summary>
public sealed class NumberSkillXpAmountAction
    : AbilityActionHandler<SkillXpContext, float, NumberSpec>
{
    public override ActionId Id => ActionIds.Number;
    public override HookId Hook => HookIds.Progress;
    public override VerbId Verb => VerbIds.SkillXp;
    public override PhaseId Phase => HookIds.Amount;

    protected override bool TryParse(JObject? raw, out NumberSpec? parameters, out string error) =>
        NumberSpec.TryParse(raw, out parameters, out error);

    protected override float Apply(
        SkillXpContext context,
        float value,
        NumberSpec parameters,
        AbilityRuleSource source)
    {
        if (!string.Equals(context.SkillId, source.SkillId, StringComparison.OrdinalIgnoreCase))
        {
            return value;
        }

        return parameters.Apply(value, context, source);
    }
}

/// <summary>
/// Float number fold for progress / skill-bucket / cap.
/// Own-skill only (same isolation as skill-xp).
/// </summary>
public sealed class NumberSkillBucketCapAction
    : AbilityActionHandler<SkillBucketCapContext, float, NumberSpec>
{
    public override ActionId Id => ActionIds.Number;
    public override HookId Hook => HookIds.Progress;
    public override VerbId Verb => VerbIds.SkillBucket;
    public override PhaseId Phase => HookIds.Cap;

    protected override bool TryParse(JObject? raw, out NumberSpec? parameters, out string error) =>
        NumberSpec.TryParse(raw, out parameters, out error);

    protected override float Apply(
        SkillBucketCapContext context,
        float value,
        NumberSpec parameters,
        AbilityRuleSource source)
    {
        if (!string.Equals(context.SkillId, source.SkillId, StringComparison.OrdinalIgnoreCase))
        {
            return value;
        }

        return parameters.Apply(value, context, source);
    }
}

/// <summary>
/// Int number fold for player-interaction stat verbs (temporal recover/drain).
/// Rounds so fractional <c>perSkillLevel</c> (e.g. drain −0.6) lands on a whole percent.
/// </summary>
public sealed class NumberPlayerInteractionIntAction
    : AbilityActionHandler<PlayerInteractionContext, int, NumberSpec>
{
    readonly VerbId verb;

    public NumberPlayerInteractionIntAction(VerbId verb) => this.verb = verb;

    public override ActionId Id => ActionIds.Number;
    public override HookId Hook => HookIds.PlayerInteraction;
    public override VerbId Verb => verb;
    public override PhaseId Phase => HookIds.Default;

    protected override bool TryParse(JObject? raw, out NumberSpec? parameters, out string error) =>
        NumberSpec.TryParse(raw, out parameters, out error);

    protected override int Apply(
        PlayerInteractionContext context,
        int value,
        NumberSpec parameters,
        AbilityRuleSource source) =>
        (int)Math.Round(parameters.Apply(value, context, source), MidpointRounding.AwayFromZero);
}

/// <summary>
/// Float number fold for player-interaction locomotion verbs (sprint / swim / sneak speed).
/// Seed is 0; the station treats the fold as a bonus fraction.
/// </summary>
public sealed class NumberPlayerInteractionFloatAction
    : AbilityActionHandler<PlayerInteractionContext, float, NumberSpec>
{
    readonly VerbId verb;

    public NumberPlayerInteractionFloatAction(VerbId verb) => this.verb = verb;

    public override ActionId Id => ActionIds.Number;
    public override HookId Hook => HookIds.PlayerInteraction;
    public override VerbId Verb => verb;
    public override PhaseId Phase => HookIds.Default;

    protected override bool TryParse(JObject? raw, out NumberSpec? parameters, out string error) =>
        NumberSpec.TryParse(raw, out parameters, out error);

    protected override float Apply(
        PlayerInteractionContext context,
        float value,
        NumberSpec parameters,
        AbilityRuleSource source) =>
        parameters.Apply(value, context, source);
}

/// <summary>Float number fold for reinforce / strength (seed = material strength).</summary>
public sealed class NumberReinforceStrengthAction
    : AbilityActionHandler<ReinforceContext, float, NumberSpec>
{
    public override ActionId Id => ActionIds.Number;
    public override HookId Hook => HookIds.ItemInteraction;
    public override VerbId Verb => VerbIds.Reinforce;
    public override PhaseId Phase => HookIds.Strength;

    protected override bool TryParse(JObject? raw, out NumberSpec? parameters, out string error) =>
        NumberSpec.TryParse(raw, out parameters, out error);

    protected override float Apply(
        ReinforceContext context,
        float value,
        NumberSpec parameters,
        AbilityRuleSource source) =>
        parameters.Apply(value, context, source);
}

/// <summary>Float number fold for heat-structure-damage / skip (seed 0).</summary>
public sealed class NumberHeatStructureSkipAction
    : AbilityActionHandler<HeatStructureDamageContext, float, NumberSpec>
{
    public override ActionId Id => ActionIds.Number;
    public override HookId Hook => HookIds.BlockInteraction;
    public override VerbId Verb => VerbIds.HeatStructureDamage;
    public override PhaseId Phase => HookIds.Skip;

    protected override bool TryParse(JObject? raw, out NumberSpec? parameters, out string error) =>
        NumberSpec.TryParse(raw, out parameters, out error);

    protected override float Apply(
        HeatStructureDamageContext context,
        float value,
        NumberSpec parameters,
        AbilityRuleSource source) =>
        parameters.Apply(value, context, source);
}

/// <summary>Float number fold for bleed-out / rate (seed 1).</summary>
public sealed class NumberBleedOutRateAction
    : AbilityActionHandler<PlayerInteractionContext, float, NumberSpec>
{
    public override ActionId Id => ActionIds.Number;
    public override HookId Hook => HookIds.PlayerInteraction;
    public override VerbId Verb => VerbIds.BleedOut;
    public override PhaseId Phase => HookIds.Rate;

    protected override bool TryParse(JObject? raw, out NumberSpec? parameters, out string error) =>
        NumberSpec.TryParse(raw, out parameters, out error);

    protected override float Apply(
        PlayerInteractionContext context,
        float value,
        NumberSpec parameters,
        AbilityRuleSource source) =>
        parameters.Apply(value, context, source);
}

/// <summary>Float number fold for tend / health or application-rate.</summary>
public sealed class NumberTendAction : AbilityActionHandler<TendContext, float, NumberSpec>
{
    readonly PhaseId phase;

    public NumberTendAction(PhaseId phase) => this.phase = phase;

    public override ActionId Id => ActionIds.Number;
    public override HookId Hook => HookIds.ItemInteraction;
    public override VerbId Verb => VerbIds.Tend;
    public override PhaseId Phase => phase;

    protected override bool TryParse(JObject? raw, out NumberSpec? parameters, out string error) =>
        NumberSpec.TryParse(raw, out parameters, out error);

    protected override float Apply(
        TendContext context,
        float value,
        NumberSpec parameters,
        AbilityRuleSource source) =>
        parameters.Apply(value, context, source);
}

/// <summary>Float number fold for revive / health or duration.</summary>
public sealed class NumberReviveAction : AbilityActionHandler<ReviveContext, float, NumberSpec>
{
    readonly PhaseId phase;

    public NumberReviveAction(PhaseId phase) => this.phase = phase;

    public override ActionId Id => ActionIds.Number;
    public override HookId Hook => HookIds.ItemInteraction;
    public override VerbId Verb => VerbIds.Revive;
    public override PhaseId Phase => phase;

    protected override bool TryParse(JObject? raw, out NumberSpec? parameters, out string error) =>
        NumberSpec.TryParse(raw, out parameters, out error);

    protected override float Apply(
        ReviveContext context,
        float value,
        NumberSpec parameters,
        AbilityRuleSource source) =>
        parameters.Apply(value, context, source);
}
