using Prosequor.Ability;
using Prosequor.Ability.Hooks;

namespace Prosequor.Xp;

/// <summary>
/// Specificity scoring against <see cref="XpMatchFact"/>.
/// Identity criteria beat collections; more criteria win; then priority and source order.
/// </summary>
public static class XpRuleMatcher
{
    public static bool Matches(XpRule rule, XpMatchFact fact, CollectionIndex collections)
    {
        if (!string.Equals(rule.Activity, fact.Activity, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        AbilityAction asAction = fact.ToAbilityAction();
        foreach (TagCriterion criterion in rule.Criteria)
        {
            if (!criterion.Matches(asAction, collections))
            {
                return false;
            }
        }

        return true;
    }

    public static long Score(IReadOnlyList<TagCriterion> criteria, int priority, int sourceOrder)
    {
        long score = 0;
        foreach (TagCriterion criterion in criteria)
        {
            score += criterion.SpecificityScore;
        }

        score += priority;
        score += Math.Max(0, 100_000 - sourceOrder);
        return score;
    }

    public static XpRule? PickWinner(
        IEnumerable<XpRule> candidates,
        XpMatchFact fact,
        CollectionIndex collections)
    {
        XpRule? best = null;
        long bestScore = long.MinValue;

        foreach (XpRule rule in candidates)
        {
            if (!Matches(rule, fact, collections))
            {
                continue;
            }

            long score = rule.MatchScore;
            if (best == null || score > bestScore)
            {
                best = rule;
                bestScore = score;
            }
        }

        return best;
    }

    public static bool Matches(XpRule rule, XpAction action, CollectionIndex collections) =>
        Matches(rule, action.ToMatchFact(), collections);

    public static XpRule? PickWinner(
        IEnumerable<XpRule> candidates,
        XpAction action,
        CollectionIndex collections) =>
        PickWinner(candidates, action.ToMatchFact(), collections);
}
