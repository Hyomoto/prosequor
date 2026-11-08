using Prosequor.Ability.Actions;
using Prosequor.Ability.Hooks;
using Prosequor.Data;
using Prosequor.Player;
using Vintagestory.API.Config;

namespace Prosequor.Client;

public enum AttributeEffectUnit
{
    /// <summary>Mapped number is a signed percent offset (<c>+9%</c>).</summary>
    SignedPercent,

    /// <summary>Mapped number is an absolute percent of baseline (<c>125%</c>).</summary>
    AbsolutePercent,

    /// <summary>Mapped number is a signed flat delta (<c>+5</c>).</summary>
    FlatSigned,

    /// <summary>Mapped number is an unsigned count (<c>10</c>).</summary>
    FlatCount,

    /// <summary>Mapped multiplier relative to 1, shown as signed percent (<c>+10%</c>).</summary>
    MultiplierPercent,

    /// <summary>Mapped multiplier shown as <c>×2.5</c>.</summary>
    MultiplierFactor,

    /// <summary>Gated phrase with no amount.</summary>
    Binary
}

/// <summary>
/// Resolves currently active attribute rules into colored plate-tooltip lines with raw amounts.
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
        ["prosequor:str-armor-walk"] = new(false, AttributeEffectUnit.AbsolutePercent),
        ["prosequor:str-melee-damage"] = new(true, AttributeEffectUnit.AbsolutePercent),
        ["prosequor:str-basic-slots"] = new(true, AttributeEffectUnit.FlatSigned),
        ["prosequor:str-break-speed"] = new(true, AttributeEffectUnit.MultiplierPercent),
        ["prosequor:per-cat-eyes-capacity"] = new(true, AttributeEffectUnit.Binary),
        ["prosequor:per-ranged-speed"] = new(true, AttributeEffectUnit.AbsolutePercent),
        ["prosequor:per-ranged-acc"] = new(true, AttributeEffectUnit.AbsolutePercent),
        ["prosequor:per-ranged-damage"] = new(true, AttributeEffectUnit.SignedPercent),
        ["prosequor:per-ranged-distance"] = new(true, AttributeEffectUnit.SignedPercent),
        ["prosequor:con-health"] = new(true, AttributeEffectUnit.FlatSigned),
        ["prosequor:con-satiety"] = new(true, AttributeEffectUnit.FlatSigned),
        ["prosequor:con-hunger-delay"] = new(true, AttributeEffectUnit.SignedPercent),
        ["prosequor:con-walk-speed"] = new(true, AttributeEffectUnit.SignedPercent),
        ["prosequor:inc-animal-threat"] = new(false, AttributeEffectUnit.AbsolutePercent),
        ["prosequor:inc-whole-vessel-loot"] = new(true, AttributeEffectUnit.SignedPercent),
        ["prosequor:inc-crit-chance"] = new(true, AttributeEffectUnit.SignedPercent),
        ["prosequor:inc-mechanicals-damage"] = new(true, AttributeEffectUnit.SignedPercent),
        ["prosequor:res-frost-damage"] = new(false, AttributeEffectUnit.MultiplierFactor),
        ["prosequor:res-last-stand"] = new(true, AttributeEffectUnit.FlatCount),
        ["prosequor:res-fall-factor"] = new(false, AttributeEffectUnit.AbsolutePercent),
        ["prosequor:res-fall-threshold"] = new(true, AttributeEffectUnit.AbsolutePercent),
        ["prosequor:res-hunger-rate"] = new(false, AttributeEffectUnit.SignedPercent)
    };

    public static IEnumerable<(string Text, bool Positive)> EnumerateActiveLines(
        IAttributeStatRegistry registry,
        IPlayerProgress? progress)
    {
        foreach (AttributeStatDef def in registry.All)
        {
            foreach ((string text, bool positive) in EnumerateActiveLinesFor(def, progress))
            {
                yield return (text, positive);
            }
        }
    }

    /// <summary>Active lines for a single attribute id at an explicit score.</summary>
    public static IEnumerable<(string Text, bool Positive)> EnumerateActiveLinesFor(
        IAttributeStatRegistry registry,
        string attributeId,
        int score)
    {
        if (!registry.TryGet(attributeId, out AttributeStatDef def))
        {
            yield break;
        }

        foreach ((string text, bool positive) in EnumerateActiveLinesFor(def, score))
        {
            yield return (text, positive);
        }
    }

    /// <summary>Active lines for a single attribute id.</summary>
    public static IEnumerable<(string Text, bool Positive)> EnumerateActiveLinesFor(
        IAttributeStatRegistry registry,
        string attributeId,
        IPlayerProgress? progress)
    {
        int score = progress?.GetAttribute(attributeId) ?? AttributeGrowth.DefaultScore;
        foreach ((string text, bool positive) in EnumerateActiveLinesFor(
                     registry, attributeId, score))
        {
            yield return (text, positive);
        }
    }

    static IEnumerable<(string Text, bool Positive)> EnumerateActiveLinesFor(
        AttributeStatDef def,
        IPlayerProgress? progress)
    {
        int score = progress?.GetAttribute(def.Id) ?? AttributeGrowth.DefaultScore;
        return EnumerateActiveLinesFor(def, score);
    }

    static IEnumerable<(string Text, bool Positive)> EnumerateActiveLinesFor(
        AttributeStatDef def,
        int score)
    {
        foreach (AbilityRule rule in def.Rules)
        {
            if (!TryDescribe(rule, score, out string? text, out bool positive) || text == null)
            {
                continue;
            }

            yield return (text, positive);
        }
    }

    public static string ColorWrap(string text, bool positive) =>
        $"<font color=\"{(positive ? PositiveColor : NegativeColor)}\">{text}</font>";

    /// <summary>Formats a mapped amount for tooltip insertion.</summary>
    public static string FormatAmount(AttributeEffectUnit unit, float current)
    {
        return unit switch
        {
            AttributeEffectUnit.SignedPercent => FormatSignedPercent(current),
            AttributeEffectUnit.AbsolutePercent => FormatAbsolutePercent(current),
            AttributeEffectUnit.FlatSigned => FormatFlatSigned(current),
            AttributeEffectUnit.FlatCount => FormatFlatCount(current),
            AttributeEffectUnit.MultiplierPercent => FormatSignedPercent((current - 1f) * 100f),
            AttributeEffectUnit.MultiplierFactor => FormatMultiplierFactor(current),
            _ => ""
        };
    }

    public static string FormatSignedPercent(float percent)
    {
        int n = (int)Math.Round(percent);
        return n > 0 ? $"+{n}%" : $"{n}%";
    }

    public static string FormatAbsolutePercent(float percent) =>
        (int)Math.Round(percent) + "%";

    public static string FormatFlatSigned(float value)
    {
        int n = (int)Math.Round(value);
        return n > 0 ? $"+{n}" : n.ToString();
    }

    public static string FormatFlatCount(float value) =>
        ((int)Math.Round(value)).ToString();

    public static string FormatMultiplierFactor(float factor)
    {
        string body = Math.Abs(factor - Math.Round(factor)) < 0.05f
            ? ((int)Math.Round(factor)).ToString()
            : factor.ToString("0.##");
        return "×" + body;
    }

    /// <summary>
    /// True when the tooltip amount would read as no change after rounding
    /// (e.g. flat health <c>0</c>, absolute percent matching neutral).
    /// </summary>
    public static bool IsDisplayNeutral(AttributeEffectUnit unit, float current, float neutral)
    {
        return unit switch
        {
            AttributeEffectUnit.SignedPercent => (int)Math.Round(current) == 0,
            AttributeEffectUnit.FlatSigned => (int)Math.Round(current) == 0,
            AttributeEffectUnit.FlatCount => (int)Math.Round(current) == 0,
            AttributeEffectUnit.MultiplierPercent =>
                (int)Math.Round((current - 1f) * 100f) == 0,
            AttributeEffectUnit.AbsolutePercent =>
                (int)Math.Round(current) == (int)Math.Round(neutral),
            AttributeEffectUnit.MultiplierFactor =>
                string.Equals(
                    FormatMultiplierFactor(current),
                    FormatMultiplierFactor(neutral),
                    StringComparison.Ordinal),
            AttributeEffectUnit.Binary => NearlyEqual(current, neutral),
            _ => NearlyEqual(current, neutral)
        };
    }

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

        if (rule.Parameters is not MappedNumberParams mapped)
        {
            return false;
        }

        // Curve starts above default score: inactive until span start (mapped clamp would lie).
        if (mapped.SpanStart > AttributeGrowth.DefaultScore && score < mapped.SpanStart)
        {
            return false;
        }

        float current = mapped.Evaluate(score);
        float neutral = ResolveNeutral(mapped);

        if (display.Unit == AttributeEffectUnit.Binary)
        {
            if (rule.Source.MinAttributeScore > AttributeGrowth.DefaultScore
                || mapped.SpanStart > AttributeGrowth.DefaultScore)
            {
                positive = true;
                text = LangLine(rule.RuleId, amount: null);
                return text != null;
            }

            if (IsDisplayNeutral(display.Unit, current, neutral))
            {
                return false;
            }

            positive = IsPositive(display.HigherIsBetter, current, neutral);
            text = LangLine(rule.RuleId, amount: null);
            return text != null;
        }

        if (IsDisplayNeutral(display.Unit, current, neutral))
        {
            return false;
        }

        positive = IsPositive(display.HigherIsBetter, current, neutral);
        string amount = FormatAmount(display.Unit, current);
        text = LangLine(rule.RuleId, amount);
        return text != null;
    }

    static float ResolveNeutral(MappedNumberParams mapped)
    {
        // Legacy hinged curves: hinge value is the authored neutral.
        if (!mapped.UsesCurve && mapped.MidScore is int && mapped.MidValue is float midV)
        {
            return midV;
        }

        // Additive / unlock curves that only begin above default: no-effect baseline is 0.
        if (mapped.SpanStart > AttributeGrowth.DefaultScore)
        {
            return 0f;
        }

        return mapped.Evaluate(AttributeGrowth.DefaultScore);
    }

    static bool IsPositive(bool higherIsBetter, float current, float neutral)
    {
        bool higher = current > neutral;
        return higherIsBetter ? higher : !higher;
    }

    static string? LangLine(string ruleId, string? amount)
    {
        string key = $"prosequor:attrdesc-{ShortRuleId(ruleId)}";
        string? text = amount == null
            ? Lang.GetIfExists(key)
            : Lang.Get(key, amount);

        if (string.IsNullOrWhiteSpace(text) || text == key)
        {
            return null;
        }

        // Lang.Get returns the key when missing even with args; also reject unsubstituted templates.
        if (amount != null && text.Contains("{0}", StringComparison.Ordinal))
        {
            return null;
        }

        return text;
    }

    static string ShortRuleId(string ruleId)
    {
        const string prefix = "prosequor:";
        return ruleId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? ruleId[prefix.Length..]
            : ruleId;
    }

    static bool NearlyEqual(float a, float b) => Math.Abs(a - b) < 0.0001f;

    readonly record struct RuleDisplay(bool HigherIsBetter, AttributeEffectUnit Unit);
}
