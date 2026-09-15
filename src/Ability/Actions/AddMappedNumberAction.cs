using Newtonsoft.Json.Linq;
using Prosequor.Ability.Hooks;

namespace Prosequor.Ability.Actions;

/// <summary>
/// Attribute-score → number map for <c>prosequor:add-mapped-number</c> (and last-stand / …).
/// <c>op</c>: <c>add</c> (default) or <c>scale</c> (<c>value * mapped</c>; author fractions, no hidden /100).
/// Optional <c>round</c> (<c>ceil</c>|<c>floor</c>|<c>round</c>) for whole mapped values; omit for fractional.
/// </summary>
public sealed class MappedNumberParams
{
    public string Op { get; init; } = "add";
    public int FromScore { get; init; }
    public float FromValue { get; init; }
    public int ToScore { get; init; }
    public float ToValue { get; init; }
    public int? MidScore { get; init; }
    public float? MidValue { get; init; }
    public string? Round { get; init; }

    public static bool TryParse(JObject? raw, out MappedNumberParams? parameters, out string error)
    {
        parameters = null;
        if (raw == null)
        {
            error = "params require fromScore, fromValue, toScore, toValue.";
            return false;
        }

        string op = (raw.Value<string>("op") ?? "add").Trim().ToLowerInvariant();
        if (op is not ("add" or "scale"))
        {
            error = "op must be 'add' or 'scale' (or omit for add).";
            return false;
        }

        int? fromScore = raw.Value<int?>("fromScore");
        float? fromValue = raw.Value<float?>("fromValue");
        int? toScore = raw.Value<int?>("toScore");
        float? toValue = raw.Value<float?>("toValue");
        if (fromScore == null || fromValue == null || toScore == null || toValue == null)
        {
            error = "params require fromScore, fromValue, toScore, toValue.";
            return false;
        }

        int? midScore = raw.Value<int?>("midScore");
        float? midValue = raw.Value<float?>("midValue");
        if (midScore.HasValue != midValue.HasValue)
        {
            error = "midScore and midValue must both be set or both omitted.";
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

        parameters = new MappedNumberParams
        {
            Op = op,
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

    public float Evaluate(int score)
    {
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

    public int EvaluateInt(int score) =>
        AbilityFormulas.AttributeMappedInt(
            score,
            FromScore,
            FromValue,
            ToScore,
            ToValue,
            Round ?? "ceil",
            MidScore,
            MidValue);

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
