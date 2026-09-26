using Newtonsoft.Json.Linq;
using Prosequor.Ability.Hooks;

namespace Prosequor.Ability.Actions;

/// <summary>
/// Attribute-score → number map for <c>prosequor:add-mapped-number</c> (and last-stand / …).
/// Preferred params: <c>from</c> (score pair) + <c>to</c> (value list ≥ 2); optional
/// <c>curve</c> <c>linear</c> (default) or <c>ease</c> (<c>bezier</c> accepted as ease).
/// Legacy: <c>fromScore</c>/<c>fromValue</c>/<c>toScore</c>/<c>toValue</c> (+ optional mid hinge).
/// <c>op</c>: <c>add</c> (default) or <c>scale</c>. Optional <c>round</c>.
/// </summary>
public sealed class MappedNumberParams
{
    public string Op { get; init; } = "add";

    /// <summary>True when authored with <c>from</c>/<c>to</c> arrays.</summary>
    public bool UsesCurve { get; init; }

    /// <summary><c>linear</c> (default) or <c>ease</c>; only meaningful when <see cref="UsesCurve"/>.</summary>
    public string Curve { get; init; } = "linear";

    public int FromScore { get; init; }
    public float FromValue { get; init; }
    public int ToScore { get; init; }
    public float ToValue { get; init; }
    public int? MidScore { get; init; }
    public float? MidValue { get; init; }
    public float[]? ToValues { get; init; }
    public string? Round { get; init; }

    /// <summary>Score span start (curve <c>from[0]</c> or legacy <c>fromScore</c>).</summary>
    public int SpanStart => FromScore;

    /// <summary>Score span end (curve <c>from[1]</c> or legacy <c>toScore</c>).</summary>
    public int SpanEnd => ToScore;

    public static bool TryParse(JObject? raw, out MappedNumberParams? parameters, out string error)
    {
        parameters = null;
        if (raw == null)
        {
            error = "params require from and to, or fromScore/fromValue/toScore/toValue.";
            return false;
        }

        string op = (raw.Value<string>("op") ?? "add").Trim().ToLowerInvariant();
        if (op is not ("add" or "scale"))
        {
            error = "op must be 'add' or 'scale' (or omit for add).";
            return false;
        }

        string? round = raw.Value<string>("round")?.Trim();
        if (round != null && round is not ("ceil" or "floor" or "round"))
        {
            error = "round must be ceil, floor, or round.";
            return false;
        }

        if (string.IsNullOrEmpty(round))
        {
            round = null;
        }

        bool hasFromArray = raw["from"] != null;
        bool hasToArray = raw["to"] != null;
        bool hasLegacy = raw["fromScore"] != null
            || raw["fromValue"] != null
            || raw["toScore"] != null
            || raw["toValue"] != null
            || raw["midScore"] != null
            || raw["midValue"] != null;

        if ((hasFromArray || hasToArray) && hasLegacy)
        {
            error = "params cannot mix from/to arrays with fromScore/fromValue/toScore/toValue.";
            return false;
        }

        if (hasFromArray || hasToArray)
        {
            return TryParseCurve(raw, op, round, out parameters, out error);
        }

        if (raw["curve"] != null)
        {
            error = "curve is only valid with from/to arrays.";
            return false;
        }

        return TryParseLegacy(raw, op, round, out parameters, out error);
    }

    static bool TryParseCurve(
        JObject raw,
        string op,
        string? round,
        out MappedNumberParams? parameters,
        out string error)
    {
        parameters = null;
        if (raw["from"] is not JArray fromArr || fromArr.Count != 2)
        {
            error = "from must be a two-number score span.";
            return false;
        }

        if (!TryReadInt(fromArr[0], out int fromLow) || !TryReadInt(fromArr[1], out int fromHigh))
        {
            error = "from entries must be numbers.";
            return false;
        }

        if (raw["to"] is not JArray toArr || toArr.Count < 2)
        {
            error = "to must be a number array with at least two entries.";
            return false;
        }

        float[] toValues = new float[toArr.Count];
        for (int i = 0; i < toArr.Count; i++)
        {
            if (!TryReadNumber(toArr[i], out toValues[i]))
            {
                error = "to entries must be numbers.";
                return false;
            }
        }

        string curveRaw = (raw.Value<string>("curve") ?? "linear").Trim().ToLowerInvariant();
        string curve = curveRaw switch
        {
            "linear" => "linear",
            "ease" or "bezier" => "ease",
            _ => ""
        };
        if (curve.Length == 0)
        {
            error = "curve must be 'linear' or 'ease' (or omit for linear).";
            return false;
        }

        parameters = new MappedNumberParams
        {
            Op = op,
            UsesCurve = true,
            Curve = curve,
            FromScore = fromLow,
            FromValue = toValues[0],
            ToScore = fromHigh,
            ToValue = toValues[toValues.Length - 1],
            ToValues = toValues,
            Round = round
        };
        error = "";
        return true;
    }

    static bool TryParseLegacy(
        JObject raw,
        string op,
        string? round,
        out MappedNumberParams? parameters,
        out string error)
    {
        parameters = null;
        int? fromScore = raw.Value<int?>("fromScore");
        float? fromValue = raw.Value<float?>("fromValue");
        int? toScore = raw.Value<int?>("toScore");
        float? toValue = raw.Value<float?>("toValue");
        if (fromScore == null || fromValue == null || toScore == null || toValue == null)
        {
            error = "params require from and to, or fromScore, fromValue, toScore, toValue.";
            return false;
        }

        int? midScore = raw.Value<int?>("midScore");
        float? midValue = raw.Value<float?>("midValue");
        if (midScore.HasValue != midValue.HasValue)
        {
            error = "midScore and midValue must both be set or both omitted.";
            return false;
        }

        parameters = new MappedNumberParams
        {
            Op = op,
            UsesCurve = false,
            FromScore = fromScore.Value,
            FromValue = fromValue.Value,
            ToScore = toScore.Value,
            ToValue = toValue.Value,
            MidScore = midScore,
            MidValue = midValue,
            Round = round
        };
        error = "";
        return true;
    }

    static bool TryReadInt(JToken? token, out int value)
    {
        value = 0;
        if (token == null)
        {
            return false;
        }

        if (token.Type == JTokenType.Integer)
        {
            value = token.Value<int>();
            return true;
        }

        if (token.Type == JTokenType.Float)
        {
            float f = token.Value<float>();
            if (!float.IsFinite(f))
            {
                return false;
            }

            value = (int)f;
            return true;
        }

        return false;
    }

    static bool TryReadNumber(JToken? token, out float value)
    {
        value = 0f;
        if (token == null)
        {
            return false;
        }

        if (token.Type is JTokenType.Integer or JTokenType.Float)
        {
            value = token.Value<float>();
            return float.IsFinite(value);
        }

        return false;
    }

    public float Evaluate(int score)
    {
        if (UsesCurve && ToValues != null)
        {
            if (Round != null)
            {
                return AbilityFormulas.AttributeCurveInt(
                    score,
                    FromScore,
                    ToScore,
                    ToValues,
                    Curve,
                    Round);
            }

            return AbilityFormulas.AttributeCurveFloat(
                score,
                FromScore,
                ToScore,
                ToValues,
                Curve);
        }

        if (Round != null)
        {
            return AbilityFormulas.AttributeMappedInt(
                score,
                FromScore,
                FromValue,
                ToScore,
                ToValue,
                Round,
                MidScore,
                MidValue);
        }

        return AbilityFormulas.AttributeMappedFloat(
            score,
            FromScore,
            FromValue,
            ToScore,
            ToValue,
            MidScore,
            MidValue);
    }

    public int EvaluateInt(int score)
    {
        if (UsesCurve && ToValues != null)
        {
            return AbilityFormulas.AttributeCurveInt(
                score,
                FromScore,
                ToScore,
                ToValues,
                Curve,
                Round ?? "ceil");
        }

        return AbilityFormulas.AttributeMappedInt(
            score,
            FromScore,
            FromValue,
            ToScore,
            ToValue,
            Round ?? "ceil",
            MidScore,
            MidValue);
    }

    public float ApplyTo(float value, int score)
    {
        float mapped = Evaluate(score);
        return Op == "scale" ? value * mapped : value + mapped;
    }

    public int ApplyTo(int value, int score)
    {
        if (Op == "scale")
        {
            return (int)(value * Evaluate(score));
        }

        return value + EvaluateInt(score);
    }
}

/// <summary>
/// Mapped number on a player-interaction int fold (stat verbs / <c>default</c>).
/// </summary>
public sealed class AddMappedNumberIntAction
    : AbilityActionHandler<PlayerInteractionContext, int, MappedNumberParams>
{
    readonly VerbId verb;

    public AddMappedNumberIntAction(VerbId verb) => this.verb = verb;

    public override ActionId Id => ActionIds.AddMappedNumber;
    public override HookId Hook => HookIds.PlayerInteraction;
    public override VerbId Verb => verb;
    public override PhaseId Phase => HookIds.Default;

    protected override bool TryParse(JObject? raw, out MappedNumberParams? parameters, out string error) =>
        MappedNumberParams.TryParse(raw, out parameters, out error);

    protected override int Apply(
        PlayerInteractionContext context,
        int value,
        MappedNumberParams parameters,
        AbilityRuleSource source)
    {
        if (string.IsNullOrWhiteSpace(source.AttributeId))
        {
            return value;
        }

        int score = context.Progress?.GetAttribute(source.AttributeId) ?? 0;
        return parameters.ApplyTo(value, score);
    }
}

/// <summary>
/// Mapped number on a player-interaction float fold (e.g. cat-eyes).
/// </summary>
public sealed class AddMappedNumberFloatAction
    : AbilityActionHandler<CatEyesContext, float, MappedNumberParams>
{
    readonly VerbId verb;

    public AddMappedNumberFloatAction(VerbId verb) => this.verb = verb;

    public override ActionId Id => ActionIds.AddMappedNumber;
    public override HookId Hook => HookIds.PlayerInteraction;
    public override VerbId Verb => verb;
    public override PhaseId Phase => HookIds.Default;

    protected override bool TryParse(JObject? raw, out MappedNumberParams? parameters, out string error) =>
        MappedNumberParams.TryParse(raw, out parameters, out error);

    protected override float Apply(
        CatEyesContext context,
        float value,
        MappedNumberParams parameters,
        AbilityRuleSource source)
    {
        if (string.IsNullOrWhiteSpace(source.AttributeId))
        {
            return value;
        }

        int score = context.Progress?.GetAttribute(source.AttributeId) ?? 0;
        return parameters.ApplyTo(value, score);
    }
}

/// <summary>
/// Mapped number on block-interaction / interaction-speed (Strength break-speed curve).
/// </summary>
public sealed class AddMappedNumberInteractionSpeedAction
    : AbilityActionHandler<InteractionSpeedContext, float, MappedNumberParams>
{
    public override ActionId Id => ActionIds.AddMappedNumber;
    public override HookId Hook => HookIds.BlockInteraction;
    public override VerbId Verb => VerbIds.InteractionSpeed;
    public override PhaseId Phase => HookIds.Default;

    protected override bool TryParse(JObject? raw, out MappedNumberParams? parameters, out string error) =>
        MappedNumberParams.TryParse(raw, out parameters, out error);

    protected override float Apply(
        InteractionSpeedContext context,
        float value,
        MappedNumberParams parameters,
        AbilityRuleSource source)
    {
        if (string.IsNullOrWhiteSpace(source.AttributeId))
        {
            return value;
        }

        int score = context.Progress?.GetAttribute(source.AttributeId) ?? 0;
        return parameters.ApplyTo(value, score);
    }
}

/// <summary>Mapped number on on-damage / amount or last-stand.</summary>
public sealed class AddMappedNumberOnDamageAction
    : AbilityActionHandler<TakeDamageContext, float, MappedNumberParams>
{
    readonly PhaseId phase;

    public AddMappedNumberOnDamageAction(PhaseId phase) => this.phase = phase;

    public override ActionId Id => ActionIds.AddMappedNumber;
    public override HookId Hook => HookIds.PlayerInteraction;
    public override VerbId Verb => VerbIds.OnDamage;
    public override PhaseId Phase => phase;

    protected override bool TryParse(JObject? raw, out MappedNumberParams? parameters, out string error) =>
        MappedNumberParams.TryParse(raw, out parameters, out error);

    protected override float Apply(
        TakeDamageContext context,
        float value,
        MappedNumberParams parameters,
        AbilityRuleSource source)
    {
        if (string.IsNullOrWhiteSpace(source.AttributeId))
        {
            return value;
        }

        int score = context.Progress?.GetAttribute(source.AttributeId) ?? 0;
        return parameters.ApplyTo(value, score);
    }
}

/// <summary>Mapped number on animal-flee|seek / response (Inconspicuity).</summary>
public sealed class AddMappedNumberAnimalResponseAction
    : AbilityActionHandler<AnimalBehaviorContext, float, MappedNumberParams>
{
    readonly VerbId verb;

    public AddMappedNumberAnimalResponseAction(VerbId verb) => this.verb = verb;

    public override ActionId Id => ActionIds.AddMappedNumber;
    public override HookId Hook => HookIds.EntityInteraction;
    public override VerbId Verb => verb;
    public override PhaseId Phase => HookIds.Response;

    protected override bool TryParse(JObject? raw, out MappedNumberParams? parameters, out string error) =>
        MappedNumberParams.TryParse(raw, out parameters, out error);

    protected override float Apply(
        AnimalBehaviorContext context,
        float value,
        MappedNumberParams parameters,
        AbilityRuleSource source)
    {
        if (string.IsNullOrWhiteSpace(source.AttributeId))
        {
            return value;
        }

        int score = context.Progress?.GetAttribute(source.AttributeId) ?? 0;
        return parameters.ApplyTo(value, score);
    }
}
