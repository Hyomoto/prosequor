using Newtonsoft.Json.Linq;
using Prosequor.Ability;

namespace Prosequor.Xp;

/// <summary>Runtime XP rule. Event rules use <see cref="Amount"/>; passive vectors use <see cref="Rate"/>.</summary>
public sealed class XpRule
{
    public required string Id { get; init; }
    public required string Activity { get; init; }
    public required string SkillId { get; init; }

    /// <summary>Burst grant for discrete adapters. 0 when this is a rate rule or table-only.</summary>
    public float Amount { get; init; }

    /// <summary>
    /// Optional size-scaled amount table (e.g. clay fire voxels). When set, measure grants
    /// piecewise-lerp the table instead of using <see cref="Amount"/> alone.
    /// </summary>
    public IReadOnlyList<float>? AmountTable { get; init; }

    /// <summary>XP per game-second while the activity matches. 0 when this is an amount rule.</summary>
    public float Rate { get; init; }

    /// <summary>
    /// Which deed emit channel this amount rule reads. Omitted JSON <c>pay</c> is
    /// <see cref="XpPayChannel.Flat"/>.
    /// </summary>
    public XpPayChannel Pay { get; init; }

    /// <summary>
    /// Codes / collections kept for quantity (outputs) or ingredients (inputs).
    /// Empty when unused. Only valid with quantity or ingredients pay.
    /// </summary>
    public XpQuantityExclude Include { get; init; } = XpQuantityExclude.Empty;

    /// <summary>
    /// Codes / collections omitted from the quantity channel. Empty when unused.
    /// Only valid with <see cref="XpPayChannel.Quantity"/>.
    /// </summary>
    public XpQuantityExclude Exclude { get; init; } = XpQuantityExclude.Empty;

    /// <summary>
    /// Who receives the grant. Omitted JSON <c>payee</c> is <see cref="XpPayee.User"/>.
    /// </summary>
    public XpPayee Payee { get; init; }

    public IReadOnlyList<TagCriterion> Criteria { get; init; } = Array.Empty<TagCriterion>();
    public int Priority { get; init; }
    public int SourceOrder { get; init; }

    public long MatchScore { get; init; }

    public bool IsRateRule => Rate > 0f;
    public bool IsAmountRule =>
        Amount > 0f || (AmountTable != null && AmountTable.Count > 0);
}

/// <summary>
/// JSON shape for skill-embedded / contribution <c>xpRules</c> entries.
/// Skill ownership is inferred from the enclosing skill; there is no <c>skill</c> field.
/// <c>amount</c> may be a number or an array of numbers (size table).
/// </summary>
public class XpRuleJson
{
    public string id { get; set; } = "";

    /// <summary>Scalar or array; use <see cref="TryParseAmount"/>.</summary>
    public JToken? amount { get; set; }

    public float rate { get; set; }

    /// <summary>Single channel name; use <see cref="TryParsePay"/>. Null/omit → flat.</summary>
    public JToken? pay { get; set; }

    /// <summary>
    /// Codes / <c>&lt;collections&gt;</c> kept for quantity or ingredients. Null/omit → empty.
    /// </summary>
    public string[]? include { get; set; }

    /// <summary>
    /// Codes / <c>&lt;collections&gt;</c> omitted from quantity. Null/omit → empty.
    /// </summary>
    public string[]? exclude { get; set; }

    /// <summary><c>user</c> / <c>contributor</c> / <c>contributors</c>. Null/omit → user.</summary>
    public string? payee { get; set; }

    public XpRuleWhenJson? when { get; set; }
    public int priority { get; set; }

    public bool TryParsePayee(out XpPayee payeeMode, out string? error)
    {
        payeeMode = XpPayee.User;
        error = null;
        if (string.IsNullOrWhiteSpace(payee))
        {
            return true;
        }

        return XpPayees.TryParse(payee, out payeeMode, out error);
    }

    public bool TryParsePay(out XpPayChannel channels, out string? error)
    {
        channels = XpPayChannel.Flat;
        error = null;
        if (pay == null || pay.Type == JTokenType.Null)
        {
            return true;
        }

        if (pay.Type == JTokenType.String)
        {
            return XpPayChannels.TryParseName(pay.Value<string>(), out channels, out error);
        }

        error = "pay must be a single channel string";
        return false;
    }

    public bool TryParseAmount(out float scalar, out float[]? table, out string? error)
    {
        scalar = 0f;
        table = null;
        error = null;
        if (amount == null || amount.Type == JTokenType.Null)
        {
            return true;
        }

        if (amount.Type == JTokenType.Integer || amount.Type == JTokenType.Float)
        {
            scalar = amount.Value<float>();
            return true;
        }

        if (amount is JArray arr)
        {
            if (arr.Count == 0)
            {
                error = "amount array must not be empty";
                return false;
            }

            List<float> list = new(arr.Count);
            foreach (JToken entry in arr)
            {
                if (entry.Type is not (JTokenType.Integer or JTokenType.Float))
                {
                    error = "amount array entries must be numbers";
                    return false;
                }

                float v = entry.Value<float>();
                if (v < 0f)
                {
                    error = "amount array entries must be >= 0";
                    return false;
                }

                list.Add(v);
            }

            table = list.ToArray();
            scalar = table[0];
            return true;
        }

        error = "amount must be a number or number array";
        return false;
    }
}

public class XpRuleWhenJson
{
    public string activity { get; set; } = "";

    /// <summary>AND-list of role/collection/token criteria.</summary>
    public string[]? tags { get; set; }
}
