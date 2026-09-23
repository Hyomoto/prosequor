using Prosequor.Ability;
using Prosequor.Ability.Actions;
using Prosequor.Ability.Hooks;
using Prosequor.Data;
using Prosequor.Player;
using Vintagestory.API.Config;

namespace Prosequor.Client;

enum AttributeEffectTier
{
    Slight,
    Moderate,
    Great
}

enum AttributeEffectSign
{
    Positive,
    Negative
}

enum AttributeEffectDisplayKind
{
    Tiered,
    Binary
}

/// <summary>
/// Resolves currently active attribute rules into qualitative, colored trait-tab lines.
/// Inactive / neutral effects are omitted.
/// </summary>
public static class AttributeEffectDescription
{
    public const string PositiveColor = "#84ff84";
    public const string NegativeColor = "#ff8484";

    /// <summary>
    /// Per-rule display metadata. <see cref="RuleDisplay.HigherIsBetter"/>: mapped value above
    /// neutral is a benefit.
    /// </summary>
    static readonly Dictionary<string, RuleDisplay> Displays = new(StringComparer.OrdinalIgnoreCase)
    {
        ["prosequor:str-armor-walk"] = new(false, AttributeEffectDisplayKind.Tiered),
        ["prosequor:str-melee-damage"] = new(true, AttributeEffectDisplayKind.Tiered),
        ["prosequor:str-basic-slots"] = new(true, AttributeEffectDisplayKind.Tiered),
        ["prosequor:per-cat-eyes-capacity"] = new(true, AttributeEffectDisplayKind.Binary),
        ["prosequor:per-ranged-speed"] = new(
            true,
            AttributeEffectDisplayKind.Tiered,
            BlendedStatDescription.RangedWeaponsSpeed),
        ["prosequor:per-ranged-acc"] = new(true, AttributeEffectDisplayKind.Tiered),
        ["prosequor:con-health"] = new(true, AttributeEffectDisplayKind.Tiered),
        ["prosequor:con-satiety"] = new(true, AttributeEffectDisplayKind.Tiered),
        ["prosequor:con-hunger-delay"] = new(true, AttributeEffectDisplayKind.Tiered),
        ["prosequor:inc-animal-threat"] = new(false, AttributeEffectDisplayKind.Tiered),
        ["prosequor:inc-whole-vessel-loot"] = new(true, AttributeEffectDisplayKind.Tiered),
        ["prosequor:inc-crit-chance"] = new(true, AttributeEffectDisplayKind.Tiered),
        ["prosequor:res-frost-damage"] = new(false, AttributeEffectDisplayKind.Tiered),
        ["prosequor:res-last-stand"] = new(true, AttributeEffectDisplayKind.Binary),
        ["prosequor:res-fall-factor"] = new(false, AttributeEffectDisplayKind.Tiered),
        ["prosequor:res-fall-threshold"] = new(true, AttributeEffectDisplayKind.Tiered)
    };

    public static IEnumerable<(string Text, bool Positive)> EnumerateActiveLines(
        IAttributeStatRegistry registry,
        IPlayerProgress? progress,
        IReadOnlySet<string>? emittedBlendedStats = null)
    {
        foreach (AttributeStatDef def in registry.All)
        {
            int score = progress?.GetAttribute(def.Id) ?? AttributeGrowth.DefaultScore;
            foreach (AbilityRule rule in def.Rules)
            {
                if (IsCoveredByEmittedBlendedStat(rule.RuleId, emittedBlendedStats))
                {
                    continue;
                }

                if (!TryDescribe(rule, score, out string? text, out bool positive) || text == null)
                {
                    continue;
                }

                yield return (text, positive);
            }
        }
    }

    /// <summary>
    /// Attribute curves that describe the same entity stat as a blended line would disagree
    /// with gear / temperature / riding. Skip the curve only while the blended catalog emits.
    /// </summary>
    static bool IsCoveredByEmittedBlendedStat(string ruleId, IReadOnlySet<string>? emittedBlendedStats)
    {
        if (emittedBlendedStats == null || emittedBlendedStats.Count == 0)
        {
            return false;
        }

        if (!Displays.TryGetValue(ruleId, out RuleDisplay display) || display.CoversEntityStat == null)
        {
            return false;
        }

        return emittedBlendedStats.Contains(display.CoversEntityStat);
    }

    public static string ColorWrap(string text, bool positive) =>
        $"<font color=\"{(positive ? PositiveColor : NegativeColor)}\">{text}</font>";

    static bool TryDescribe(AbilityRule rule, int score, out string? text, out bool positive)
    {
        text = null;
        positive = true;

        if (!Displays.TryGetValue(rule.RuleId, out RuleDisplay display))
        {
            return false;
        }

        if (score < rule.Source.MinAttributeScore)
        {
            return false;
        }

        if (rule.Source.MaxAttributeScore is int max && score > max)
        {
            return false;
        }

        if (!TryReadParams(rule, out int fromScore, out float fromValue, out int toScore, out float toValue,
                out int? midScore, out float? midValue, out string? round))
        {
            return false;
        }

        // Curve starts above default score: inactive until fromScore (mapped clamp would lie).
        if (fromScore > AttributeGrowth.DefaultScore && score < fromScore)
        {
            return false;
        }

        if (!TryMap(score, fromScore, fromValue, toScore, toValue, midScore, midValue, round, out float current))
        {
            return false;
        }

        float neutral = ResolveNeutral(
            fromScore,
            fromValue,
            toScore,
            toValue,
            midScore,
            midValue,
            round);

        if (display.Kind == AttributeEffectDisplayKind.Binary)
        {
            // Gated posters: once past minScore / fromScore, show a single phrase.
            if (rule.Source.MinAttributeScore > AttributeGrowth.DefaultScore
                || fromScore > AttributeGrowth.DefaultScore)
            {
                positive = true;
                text = LangLine(rule.RuleId, AttributeEffectSign.Positive, null);
                return text != null;
            }

            if (NearlyEqual(current, neutral))
            {
                return false;
            }

            positive = IsPositive(display.HigherIsBetter, current, neutral);
            text = LangLine(
                rule.RuleId,
                positive ? AttributeEffectSign.Positive : AttributeEffectSign.Negative,
                null);
            return text != null;
        }

        if (NearlyEqual(current, neutral))
        {
            return false;
        }

        positive = IsPositive(display.HigherIsBetter, current, neutral);
        AttributeEffectTier tier = ResolveTier(
            current,
            neutral,
            fromScore,
            fromValue,
            toScore,
            toValue,
            midScore,
            midValue,
            round);
        text = LangLine(
            rule.RuleId,
            positive ? AttributeEffectSign.Positive : AttributeEffectSign.Negative,
            tier);
        return text != null;
    }

    static float ResolveNeutral(
        int fromScore,
        float fromValue,
        int toScore,
        float toValue,
        int? midScore,
        float? midValue,
        string? round)
    {
        if (midScore is int midS && midValue is float midV)
        {
            return midV;
        }

        // Additive / unlock curves that only begin above default: no-effect baseline is 0.
        if (fromScore > AttributeGrowth.DefaultScore)
        {
            return 0f;
        }

        TryMap(
            AttributeGrowth.DefaultScore,
            fromScore,
            fromValue,
            toScore,
            toValue,
            midScore,
            midValue,
            round,
            out float atDefault);
        return atDefault;
    }

    static bool IsPositive(bool higherIsBetter, float current, float neutral)
    {
        bool higher = current > neutral;
        return higherIsBetter ? higher : !higher;
    }

    static AttributeEffectTier ResolveTier(
        float current,
        float neutral,
        int fromScore,
        float fromValue,
        int toScore,
        float toValue,
        int? midScore,
        float? midValue,
        string? round)
    {
        float endpoint;
        if (current > neutral)
        {
            TryMap(toScore, fromScore, fromValue, toScore, toValue, midScore, midValue, round, out endpoint);
        }
        else
        {
            TryMap(fromScore, fromScore, fromValue, toScore, toValue, midScore, midValue, round, out endpoint);
        }

        float span = Math.Abs(endpoint - neutral);
        if (span < 0.0001f)
        {
            return AttributeEffectTier.Slight;
        }

        float t = Math.Clamp(Math.Abs(current - neutral) / span, 0f, 1f);
        if (t < 1f / 3f)
        {
            return AttributeEffectTier.Slight;
        }

        if (t < 2f / 3f)
        {
            return AttributeEffectTier.Moderate;
        }

        return AttributeEffectTier.Great;
    }

    static bool TryReadParams(
        AbilityRule rule,
        out int fromScore,
        out float fromValue,
        out int toScore,
        out float toValue,
        out int? midScore,
        out float? midValue,
        out string? round)
    {
        fromScore = 0;
        fromValue = 0;
        toScore = 0;
        toValue = 0;
        midScore = null;
        midValue = null;
        round = null;

        switch (rule.Parameters)
        {
            case MappedNumberParams mapped:
                fromScore = mapped.FromScore;
                fromValue = mapped.FromValue;
                toScore = mapped.ToScore;
                toValue = mapped.ToValue;
                midScore = mapped.MidScore;
                midValue = mapped.MidValue;
                round = mapped.Round;
                return true;

            default:
                return false;
        }
    }

    static bool TryMap(
        int score,
        int fromScore,
        float fromValue,
        int toScore,
        float toValue,
        int? midScore,
        float? midValue,
        string? round,
        out float value)
    {
        if (round != null)
        {
            value = AbilityFormulas.AttributeMappedInt(
                score,
                fromScore,
                fromValue,
                toScore,
                toValue,
                round,
                midScore,
                midValue);
            return true;
        }

        value = AbilityFormulas.AttributeMappedFloat(
            score,
            fromScore,
            fromValue,
            toScore,
            toValue,
            midScore,
            midValue);
        return true;
    }

    static string? LangLine(string ruleId, AttributeEffectSign sign, AttributeEffectTier? tier)
    {
        string shortId = ShortRuleId(ruleId);
        string polarity = sign == AttributeEffectSign.Positive ? "pos" : "neg";
        string key = tier == null
            ? $"prosequor:attrdesc-{shortId}-{polarity}"
            : $"prosequor:attrdesc-{shortId}-{polarity}-{tier.Value.ToString().ToLowerInvariant()}";

        string? text = Lang.GetIfExists(key);
        return string.IsNullOrWhiteSpace(text) || text == key ? null : text;
    }

    static string ShortRuleId(string ruleId)
    {
        const string prefix = "prosequor:";
        return ruleId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? ruleId[prefix.Length..]
            : ruleId;
    }

    static bool NearlyEqual(float a, float b) => Math.Abs(a - b) < 0.0001f;

    readonly record struct RuleDisplay(
        bool HigherIsBetter,
        AttributeEffectDisplayKind Kind,
        string? CoversEntityStat = null);
}
