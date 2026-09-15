using Newtonsoft.Json.Linq;
using Prosequor.Ability.Hooks;

namespace Prosequor.Ability.Actions;

/// <summary>Compiled number operand for <c>prosequor:number</c> (and nested NumberSpec).</summary>
public abstract class NumberSpec
{
    public abstract float Evaluate(IHookContext context, AbilityRuleSource source);

    public abstract float Apply(float value, IHookContext context, AbilityRuleSource source);

    /// <summary>
    /// Parses <c>{ "op": "add"|"scale"|"set", ... }</c>. Operand is either literal <c>value</c>
    /// or skill-scaled raw amount (<c>base</c>/<c>perSkillLevel</c>/<c>cap</c>) —
    /// same units as <c>value</c> (e.g. 0.15 to add fifteen hundredths, 1 to add one).
    /// <c>cap: 0</c> means uncapped.
    /// Optional <c>ofBase: true</c> (add only): addend is multiplied by
    /// <see cref="IHasBaseValue.BaseValue"/> so later rules do not compound prior multipliers.
    /// <c>op: set</c> replaces the fold value with the operand (ignores prior fold).
    /// When <paramref name="defaultOp"/> is set, <c>op</c> may be omitted (e.g. chance gates).
    /// </summary>
    public static bool TryParse(JToken? raw, out NumberSpec? spec, out string error) =>
        TryParse(raw, defaultOp: null, out spec, out error);

    /// <inheritdoc cref="TryParse(JToken?, out NumberSpec?, out string)"/>
    public static bool TryParse(
        JToken? raw,
        string? defaultOp,
        out NumberSpec? spec,
        out string error)
    {
        spec = null;
        if (raw is not JObject obj)
        {
            error = "number params must be an object with op.";
            return false;
        }

        string? op = obj.Value<string>("op")?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(op))
        {
            op = defaultOp?.Trim().ToLowerInvariant();
        }

        if (op is not ("add" or "scale" or "set"))
        {
            error = string.IsNullOrEmpty(defaultOp)
                ? "op must be 'add', 'scale', or 'set'."
                : $"op must be 'add', 'scale', or 'set' (or omit for default '{defaultOp}').";
            return false;
        }

        bool ofBase = obj.Value<bool?>("ofBase") ?? false;
        if (ofBase && op != "add")
        {
            error = "ofBase is only valid with op 'add'.";
            return false;
        }

        if (!TryParseOperand(obj, out NumberOperand? operand, out error) || operand == null)
        {
            return false;
        }

        spec = op switch
        {
            "add" => new AddNumberSpec(operand, ofBase),
            "scale" => new ScaleNumberSpec(operand),
            _ => new SetNumberSpec(operand)
        };
        error = "";
        return true;
    }

    static bool TryParseOperand(JObject obj, out NumberOperand? operand, out string error)
    {
        operand = null;
        bool hasValue = obj["value"] != null && obj["value"]!.Type != JTokenType.Null;
        bool hasSkill =
            obj["base"] != null
            || obj["perSkillLevel"] != null
            || obj["cap"] != null
            || obj["skill"] != null;

        if (hasValue && hasSkill)
        {
            error = "specify either value or skill-scaled base/perSkillLevel/cap, not both.";
            return false;
        }

        if (hasValue)
        {
            float value = obj.Value<float?>("value") ?? 0f;
            operand = new LiteralOperand(value);
            error = "";
            return true;
        }

        float baseAmt = obj.Value<float?>("base") ?? 0f;
        float perLevel = obj.Value<float?>("perSkillLevel") ?? 0f;
        float cap = obj.Value<float?>("cap") ?? 0f;
        if (cap < 0f)
        {
            error = "cap must be >= 0 (0 = uncapped).";
            return false;
        }

        if (cap > 0f && baseAmt > cap)
        {
            error = $"base {baseAmt} exceeds cap {cap}.";
            return false;
        }

        string? skill = obj.Value<string>("skill")?.Trim();
        if (string.IsNullOrWhiteSpace(skill))
        {
            skill = null;
        }

        operand = new SkillScaledOperand(baseAmt, perLevel, cap, skill);
        error = "";
        return true;
    }
}

abstract class NumberOperand
{
    public abstract float Resolve(IHookContext context, AbilityRuleSource source);
}

sealed class LiteralOperand : NumberOperand
{
    readonly float value;

    public LiteralOperand(float value) => this.value = value;

    public override float Resolve(IHookContext context, AbilityRuleSource source) => value;
}

sealed class SkillScaledOperand : NumberOperand
{
    readonly float baseAmt;
    readonly float perLevel;
    readonly float cap;
    readonly string? skillOverride;

    public SkillScaledOperand(float baseAmt, float perLevel, float cap, string? skillOverride)
    {
        this.baseAmt = baseAmt;
        this.perLevel = perLevel;
        this.cap = cap;
        this.skillOverride = skillOverride;
    }

    public override float Resolve(IHookContext context, AbilityRuleSource source)
    {
        string skillId = skillOverride ?? source.SkillId;
        int level = context.Progress?.GetSkillLevel(skillId) ?? 0;
        float amount = baseAmt + perLevel * Math.Max(0, level);
        if (cap > 0f)
        {
            amount = Math.Min(cap, amount);
        }

        return amount;
    }
}

sealed class AddNumberSpec : NumberSpec
{
    readonly NumberOperand operand;
    readonly bool ofBase;

    public AddNumberSpec(NumberOperand operand, bool ofBase)
    {
        this.operand = operand;
        this.ofBase = ofBase;
    }

    public override float Evaluate(IHookContext context, AbilityRuleSource source) =>
        operand.Resolve(context, source);

    public override float Apply(float value, IHookContext context, AbilityRuleSource source)
    {
        float amount = operand.Resolve(context, source);
        if (ofBase)
        {
            float baseValue = context is IHasBaseValue hasBase ? hasBase.BaseValue : 0f;
            amount *= baseValue;
        }

        return value + amount;
    }
}

sealed class ScaleNumberSpec : NumberSpec
{
    readonly NumberOperand operand;

    public ScaleNumberSpec(NumberOperand operand) => this.operand = operand;

    public override float Evaluate(IHookContext context, AbilityRuleSource source) =>
        operand.Resolve(context, source);

    public override float Apply(float value, IHookContext context, AbilityRuleSource source) =>
        value * (1f + operand.Resolve(context, source));
}

sealed class SetNumberSpec : NumberSpec
{
    readonly NumberOperand operand;

    public SetNumberSpec(NumberOperand operand) => this.operand = operand;

    public override float Evaluate(IHookContext context, AbilityRuleSource source) =>
        operand.Resolve(context, source);

    public override float Apply(float value, IHookContext context, AbilityRuleSource source)
    {
        _ = value;
        return operand.Resolve(context, source);
    }
}

/// <summary>Float→float number fold for mutate-drops quantity.</summary>
public sealed class NumberDropsQuantityAction
    : AbilityActionHandler<DropsContext, float, NumberSpec>
{
    public override ActionId Id => ActionIds.Number;
    public override HookId Hook => HookIds.BlockInteraction;
    public override VerbId Verb => VerbIds.MutateDrops;
    public override PhaseId Phase => HookIds.Quantity;

    protected override bool TryParse(JObject? raw, out NumberSpec? parameters, out string error) =>
        NumberSpec.TryParse(raw, out parameters, out error);

    protected override float Apply(
        DropsContext context,
        float value,
        NumberSpec parameters,
        AbilityRuleSource source) =>
        parameters.Apply(value, context, source);
}

/// <summary>Float→float number fold for mutate-output quantity (seed = recipe base count).</summary>
public sealed class NumberCraftQuantityAction
    : AbilityActionHandler<CraftMutateOutputContext, float, NumberSpec>
{
    public override ActionId Id => ActionIds.Number;
    public override HookId Hook => HookIds.CraftingInteraction;
    public override VerbId Verb => VerbIds.MutateOutput;
    public override PhaseId Phase => HookIds.Quantity;

    protected override bool TryParse(JObject? raw, out NumberSpec? parameters, out string error) =>
        NumberSpec.TryParse(raw, out parameters, out error);

    protected override float Apply(
        CraftMutateOutputContext context,
        float value,
        NumberSpec parameters,
        AbilityRuleSource source) =>
        parameters.Apply(value, context, source);
}

/// <summary>Float number fold for apply-quality knob phases (base / window / rolls / bonus).</summary>
public sealed class NumberApplyQualityAction
    : AbilityActionHandler<CraftMutateOutputContext, float, NumberSpec>
{
    readonly PhaseId phase;

    public NumberApplyQualityAction(PhaseId phase) => this.phase = phase;

    public override ActionId Id => ActionIds.Number;
    public override HookId Hook => HookIds.CraftingInteraction;
    public override VerbId Verb => VerbIds.ApplyQuality;
    public override PhaseId Phase => phase;

    protected override bool TryParse(JObject? raw, out NumberSpec? parameters, out string error) =>
        NumberSpec.TryParse(raw, out parameters, out error);

    protected override float Apply(
        CraftMutateOutputContext context,
        float value,
        NumberSpec parameters,
        AbilityRuleSource source) =>
        parameters.Apply(value, context, source);
}

/// <summary>Float→float number fold for mutate-process quantity (seed = output StackSize).</summary>
public sealed class NumberProcessQuantityAction
    : AbilityActionHandler<MutateProcessContext, float, NumberSpec>
{
    public override ActionId Id => ActionIds.Number;
    public override HookId Hook => HookIds.BlockInteraction;
    public override VerbId Verb => VerbIds.MutateProcess;
    public override PhaseId Phase => HookIds.Quantity;

    protected override bool TryParse(JObject? raw, out NumberSpec? parameters, out string error) =>
        NumberSpec.TryParse(raw, out parameters, out error);

    protected override float Apply(
        MutateProcessContext context,
        float value,
        NumberSpec parameters,
        AbilityRuleSource source) =>
        parameters.Apply(value, context, source);
}

/// <summary>Float→float number fold for interaction-speed (seed = mining rate or 1).</summary>
public sealed class NumberInteractionSpeedAction
    : AbilityActionHandler<InteractionSpeedContext, float, NumberSpec>
{
    public override ActionId Id => ActionIds.Number;
    public override HookId Hook => HookIds.BlockInteraction;
    public override VerbId Verb => VerbIds.InteractionSpeed;
    public override PhaseId Phase => HookIds.Default;

    protected override bool TryParse(JObject? raw, out NumberSpec? parameters, out string error) =>
        NumberSpec.TryParse(raw, out parameters, out error);

    protected override float Apply(
        InteractionSpeedContext context,
        float value,
        NumberSpec parameters,
        AbilityRuleSource source) =>
        parameters.Apply(value, context, source);
}

/// <summary>Float→float number fold for entity-interaction mutate-drops quantity (butcher).</summary>
public sealed class NumberEntityMutateDropsQuantityAction
    : AbilityActionHandler<DropsContext, float, NumberSpec>
{
    public override ActionId Id => ActionIds.Number;
    public override HookId Hook => HookIds.EntityInteraction;
    public override VerbId Verb => VerbIds.MutateDrops;
    public override PhaseId Phase => HookIds.Quantity;

    protected override bool TryParse(JObject? raw, out NumberSpec? parameters, out string error) =>
        NumberSpec.TryParse(raw, out parameters, out error);

    protected override float Apply(
        DropsContext context,
        float value,
        NumberSpec parameters,
        AbilityRuleSource source) =>
        parameters.Apply(value, context, source);
}

/// <summary>Float→float number fold for item-interaction mutate-drops quantity (e.g. panning).</summary>
public sealed class NumberItemMutateDropsQuantityAction
    : AbilityActionHandler<DropsContext, float, NumberSpec>
{
    public override ActionId Id => ActionIds.Number;
    public override HookId Hook => HookIds.ItemInteraction;
    public override VerbId Verb => VerbIds.MutateDrops;
    public override PhaseId Phase => HookIds.Quantity;

    protected override bool TryParse(JObject? raw, out NumberSpec? parameters, out string error) =>
        NumberSpec.TryParse(raw, out parameters, out error);

    protected override float Apply(
        DropsContext context,
        float value,
        NumberSpec parameters,
        AbilityRuleSource source) =>
        parameters.Apply(value, context, source);
}

/// <summary>Float→float number fold for item-interaction interaction-speed (held-tool use).</summary>
public sealed class NumberItemInteractionSpeedAction
    : AbilityActionHandler<InteractionSpeedContext, float, NumberSpec>
{
    public override ActionId Id => ActionIds.Number;
    public override HookId Hook => HookIds.ItemInteraction;
    public override VerbId Verb => VerbIds.InteractionSpeed;
    public override PhaseId Phase => HookIds.Default;

    protected override bool TryParse(JObject? raw, out NumberSpec? parameters, out string error) =>
        NumberSpec.TryParse(raw, out parameters, out error);

    protected override float Apply(
        InteractionSpeedContext context,
        float value,
        NumberSpec parameters,
        AbilityRuleSource source) =>
        parameters.Apply(value, context, source);
}
