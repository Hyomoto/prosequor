using Newtonsoft.Json.Linq;
using Prosequor.Ability.Hooks;
using Prosequor.Player;

namespace Prosequor.Ability;

/// <summary>Compiled nested action reference resolved at skill compile time.</summary>
public sealed class NestedActionRef
{
    public required ActionId Action { get; init; }
    public required object Parameters { get; init; }
    public required IAbilityActionHandler Handler { get; init; }
}

/// <summary>Parse and run nested ability actions.</summary>
public static class AbilityNestedActions
{
    /// <summary>
    /// Parses a nested action that must match <paramref name="expectedHook"/> /
    /// <paramref name="expectedVerb"/> / <paramref name="expectedPhase"/>.
    /// </summary>
    public static bool TryParseSamePhase(
        JToken? raw,
        IAbilityActionRegistry actions,
        HookId expectedHook,
        VerbId expectedVerb,
        PhaseId expectedPhase,
        out NestedActionRef? nested,
        out string error)
    {
        nested = null;
        if (!TryReadActionObject(raw, out ActionId actionId, out JObject? childParams, out error))
        {
            return false;
        }

        if (!actions.TryGet(actionId, expectedHook, expectedVerb, expectedPhase, out IAbilityActionHandler handler))
        {
            error =
                $"nested action '{actionId}' is not registered for {expectedHook}/{expectedVerb}/{expectedPhase}.";
            return false;
        }

        if (!handler.TryParseParams(childParams, out object parameters, out string paramError))
        {
            error = paramError;
            return false;
        }

        nested = new NestedActionRef
        {
            Action = actionId,
            Parameters = parameters,
            Handler = handler
        };
        error = "";
        return true;
    }

    /// <summary>
    /// Parses a nested float producer (ValueType float), preferring the caller's
    /// quantity phase, then mutate-drops quantity, then plant success defaults.
    /// </summary>
    public static bool TryParseFloatProducer(
        JToken? raw,
        IAbilityActionRegistry actions,
        HookId preferredHook,
        VerbId preferredVerb,
        out NestedActionRef? nested,
        out string error)
    {
        nested = null;
        if (!TryReadActionObject(raw, out ActionId actionId, out JObject? childParams, out error))
        {
            return false;
        }

        if (!TryResolveFloatHandler(actions, actionId, preferredHook, preferredVerb, out IAbilityActionHandler handler))
        {
            error = $"nested chance action '{actionId}' is not a registered float producer.";
            return false;
        }

        if (!handler.TryParseParams(childParams, out object parameters, out string paramError))
        {
            error = paramError;
            return false;
        }

        nested = new NestedActionRef
        {
            Action = actionId,
            Parameters = parameters,
            Handler = handler
        };
        error = "";
        return true;
    }

    static bool TryResolveFloatHandler(
        IAbilityActionRegistry actions,
        ActionId actionId,
        HookId preferredHook,
        VerbId preferredVerb,
        out IAbilityActionHandler handler)
    {
        if (actions.TryGet(actionId, preferredHook, preferredVerb, HookIds.Quantity, out handler)
            && handler.ValueType == typeof(float))
        {
            return true;
        }

        if (actions.TryGet(
                actionId,
                HookIds.BlockInteraction,
                VerbIds.MutateDrops,
                HookIds.Quantity,
                out handler)
            && handler.ValueType == typeof(float))
        {
            return true;
        }

        foreach (VerbId verb in new[]
                 {
                     VerbIds.PlantSapling,
                     VerbIds.PlantBushCutting,
                     VerbIds.EstablishCutting,
                     VerbIds.SeekBobber
                 })
        {
            if (actions.TryGet(actionId, HookIds.BlockInteraction, verb, HookIds.Default, out handler)
                && handler.ValueType == typeof(float))
            {
                return true;
            }
        }

        handler = null!;
        return false;
    }

    static bool TryReadActionObject(
        JToken? raw,
        out ActionId actionId,
        out JObject? childParams,
        out string error)
    {
        actionId = default;
        childParams = null;
        if (raw is not JObject obj)
        {
            error = "nested action must be an object with action/params.";
            return false;
        }

        string? actionRaw = obj.Value<string>("action")?.Trim();
        if (string.IsNullOrWhiteSpace(actionRaw))
        {
            error = "nested action requires 'action'.";
            return false;
        }

        actionId = ActionId.Normalize(actionRaw);
        childParams = obj["params"] as JObject;
        error = "";
        return true;
    }

    public static TValue Run<TValue>(
        NestedActionRef nested,
        IHookContext context,
        TValue value,
        AbilityRuleSource source)
    {
        object result = nested.Handler.Apply(context, value!, nested.Parameters, source);
        return (TValue)result;
    }
}

/// <summary>Resolves a probability in [0,1] for chance gates.</summary>
public abstract class ChanceProducer
{
    public abstract float Evaluate(IHookContext context, AbilityRuleSource source);

    public static bool TryParse(
        JToken? raw,
        IAbilityActionRegistry actions,
        HookId preferredHook,
        VerbId preferredVerb,
        IPlayerProgress? progressForValidation,
        out ChanceProducer? producer,
        out string error)
    {
        producer = null;
        if (raw == null || raw.Type == JTokenType.Null)
        {
            error = "chance is required.";
            return false;
        }

        if (raw is JObject obj && obj.Value<string>("action") != null)
        {
            if (!AbilityNestedActions.TryParseFloatProducer(
                    obj,
                    actions,
                    preferredHook,
                    preferredVerb,
                    out NestedActionRef? nested,
                    out error)
                || nested == null)
            {
                return false;
            }

            producer = new NestedFloatChanceProducer(nested);
            return true;
        }

        if (raw is JObject formula)
        {
            if (formula.Value<float?>("percent") is float percent)
            {
                if (percent < 0f || percent > 100f)
                {
                    error = "chance.percent must be between 0 and 100.";
                    return false;
                }

                producer = new FixedPercentChanceProducer(percent);
                error = "";
                return true;
            }

            string? op = formula.Value<string>("op")?.Trim();
            if (!string.IsNullOrEmpty(op)
                && !op.Equals("add", StringComparison.OrdinalIgnoreCase))
            {
                error = "chance NumberSpec only supports op 'add' (or omit op).";
                return false;
            }

            // Chance is add-only: omit op in JSON; unit probability (0.05 = 5%).
            if (!Actions.NumberSpec.TryParse(
                    formula,
                    defaultOp: "add",
                    out Actions.NumberSpec? spec,
                    out error)
                || spec == null)
            {
                return false;
            }

            producer = new NumberSpecChanceProducer(spec);
            return true;
        }

        if (raw.Type is JTokenType.Float or JTokenType.Integer)
        {
            float percent = raw.Value<float>();
            if (percent < 0f || percent > 100f)
            {
                error = "chance percent must be between 0 and 100.";
                return false;
            }

            producer = new FixedPercentChanceProducer(percent);
            error = "";
            return true;
        }

        error = "chance must be a nested action, NumberSpec object, percent object, or percent number.";
        return false;
    }
}

sealed class NestedFloatChanceProducer : ChanceProducer
{
    readonly NestedActionRef nested;

    public NestedFloatChanceProducer(NestedActionRef nested) => this.nested = nested;

    public override float Evaluate(IHookContext context, AbilityRuleSource source)
    {
        float result = AbilityNestedActions.Run(nested, context, 0f, source);
        return Math.Clamp(result, 0f, 1f);
    }
}

sealed class NumberSpecChanceProducer : ChanceProducer
{
    readonly Actions.NumberSpec spec;

    public NumberSpecChanceProducer(Actions.NumberSpec spec) => this.spec = spec;

    public override float Evaluate(IHookContext context, AbilityRuleSource source) =>
        Math.Clamp(spec.Apply(0f, context, source), 0f, 1f);
}

sealed class FixedPercentChanceProducer : ChanceProducer
{
    readonly float percent;

    public FixedPercentChanceProducer(float percent) => this.percent = percent;

    public override float Evaluate(IHookContext context, AbilityRuleSource source) =>
        AbilityFormulas.ChanceFraction(percent);
}
