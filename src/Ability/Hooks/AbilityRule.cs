using Prosequor.Player;
using Vintagestory.API.Common;

namespace Prosequor.Ability.Hooks;

/// <summary>Typed bag owned by one hook invocation.</summary>
public interface IHookContext
{
    HookId Hook { get; }
    IPlayer? Player { get; }
    IPlayerProgress? Progress { get; }

    /// <summary>Optional match facts (verb / roles / tokens).</summary>
    AbilityAction? Fact { get; }
}

/// <summary>
/// Contexts that expose a frozen pre-rule baseline (e.g. interaction-speed mining rate).
/// Used by <c>prosequor:number</c> <c>ofBase</c> addends.
/// </summary>
public interface IHasBaseValue
{
    float BaseValue { get; }
}

/// <summary>Where a compiled rule came from.</summary>
public sealed class AbilityRuleSource
{
    public required string SkillId { get; init; }
    public string? NodeId { get; init; }

    /// <summary>1-based owned tier for tree rules; null for skill-root baseline rules.</summary>
    public int? Tier { get; init; }

    /// <summary>Attribute id when this rule came from <c>config/prosequor/stats/</c>.</summary>
    public string? AttributeId { get; init; }

    /// <summary>Minimum attribute score for the rule to be active (attribute rules only).</summary>
    public int MinAttributeScore { get; init; }

    /// <summary>Optional maximum attribute score for the rule to be active.</summary>
    public int? MaxAttributeScore { get; init; }

    public bool IsTreeRule => NodeId != null && Tier.HasValue;

    public bool IsAttributeRule => AttributeId != null;
}

/// <summary>Standard when-filter shared by all hooks (tags/tokens only; no when.verb).</summary>
public sealed class AbilityWhenFilter
{
    public IReadOnlyList<TagCriterion> Criteria { get; init; } = Array.Empty<TagCriterion>();

    /// <summary>Optional collection index for criterion matching; null matches nothing that needs it.</summary>
    public CollectionIndex? Collections { get; init; }

    public bool Matches(AbilityAction? fact)
    {
        if (Criteria.Count == 0)
        {
            return true;
        }

        foreach (TagCriterion criterion in Criteria)
        {
            if (!criterion.Matches(fact, Collections))
            {
                return false;
            }
        }

        return true;
    }
}

/// <summary>Compiled content binding: hook + verb + phase + action + params + match.</summary>
public sealed class AbilityRule
{
    public required string RuleId { get; init; }
    public required HookId Hook { get; init; }
    public required VerbId Verb { get; init; }
    public required PhaseId Phase { get; init; }
    public required ActionId Action { get; init; }
    public required AbilityWhenFilter When { get; init; }
    public required object Parameters { get; init; }
    public required AbilityRuleSource Source { get; init; }
    public int Priority { get; init; }
    public int SourceOrder { get; init; }
}
