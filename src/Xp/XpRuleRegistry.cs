using Prosequor.Data;
using Vintagestory.API.Common;

namespace Prosequor.Xp;

public interface IXpRuleRegistry
{
    IReadOnlyList<XpRule> All { get; }
    IReadOnlyList<XpRule> ByActivity(string activity);
    IReadOnlyList<XpRule> ByActivityRate(string activity);
    IReadOnlyList<XpRule> ByActivityAmount(string activity);
}

public class XpRuleRegistry : IXpRuleRegistry
{
    readonly List<XpRule> ordered = new();
    readonly Dictionary<string, List<XpRule>> byActivity = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, List<XpRule>> byActivityRate = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, List<XpRule>> byActivityAmount = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, XpRule> byId = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<XpRule> All => ordered;

    public IReadOnlyList<XpRule> ByActivity(string activity)
    {
        if (byActivity.TryGetValue(activity, out List<XpRule>? list))
        {
            return list;
        }

        return Array.Empty<XpRule>();
    }

    public IReadOnlyList<XpRule> ByActivityRate(string activity)
    {
        if (byActivityRate.TryGetValue(activity, out List<XpRule>? list))
        {
            return list;
        }

        return Array.Empty<XpRule>();
    }

    public IReadOnlyList<XpRule> ByActivityAmount(string activity)
    {
        if (byActivityAmount.TryGetValue(activity, out List<XpRule>? list))
        {
            return list;
        }

        return Array.Empty<XpRule>();
    }

    public void LoadFromSkills(ISkillRegistry skills) => LoadFromSkills(api: null, skills);

    /// <summary>
    /// Flattens compiled <see cref="SkillDef.XpRules"/> from registered skills.
    /// Duplicate rule ids last-win with a warning. Disabled skills never appear here.
    /// </summary>
    public void LoadFromSkills(ICoreAPI? api, ISkillRegistry skills)
    {
        ordered.Clear();
        byActivity.Clear();
        byActivityRate.Clear();
        byActivityAmount.Clear();
        byId.Clear();

        foreach (SkillDef skill in skills.All.OrderBy(s => s.Id, StringComparer.OrdinalIgnoreCase))
        {
            foreach (XpRule rule in skill.XpRules)
            {
                if (byId.TryGetValue(rule.Id, out XpRule? existing))
                {
                    api?.Logger.Warning(
                        "[prosequor] XP rule '{0}' redefined by skill '{1}'; last definition wins.",
                        rule.Id,
                        skill.Id);
                    Remove(existing);
                }

                byId[rule.Id] = rule;
                ordered.Add(rule);
                AddToActivityBucket(byActivity, rule.Activity, rule);
                if (rule.IsRateRule)
                {
                    AddToActivityBucket(byActivityRate, rule.Activity, rule);
                }
                else
                {
                    AddToActivityBucket(byActivityAmount, rule.Activity, rule);
                }
            }
        }

        api?.Logger.Notification("[prosequor] Registered {0} XP rule(s).", ordered.Count);
        VerifySplitIntegrity(api);
    }

    void VerifySplitIntegrity(ICoreAPI? api)
    {
        if (ordered.Count == 0)
        {
            return;
        }

        int rateCount = 0;
        foreach (List<XpRule> list in byActivityRate.Values)
        {
            rateCount += list.Count;
        }

        int amountCount = 0;
        foreach (List<XpRule> list in byActivityAmount.Values)
        {
            amountCount += list.Count;
        }

        if (rateCount + amountCount != ordered.Count)
        {
            api?.Logger.Error(
                "[prosequor] XP rule registry split failed (rate={0}, amount={1}, total={2}).",
                rateCount,
                amountCount,
                ordered.Count);
            return;
        }

        foreach (XpRule rule in ordered)
        {
            long expected = XpRuleMatcher.Score(
                rule.Criteria,
                rule.Priority,
                rule.SourceOrder);
            if (rule.MatchScore != expected)
            {
                api?.Logger.Error(
                    "[prosequor] XP rule MatchScore failed for '{0}', got {1}, expected {2}.",
                    rule.Id,
                    rule.MatchScore,
                    expected);
                return;
            }
        }
    }

    static void AddToActivityBucket(Dictionary<string, List<XpRule>> map, string activity, XpRule rule)
    {
        if (!map.TryGetValue(activity, out List<XpRule>? bucket))
        {
            bucket = new List<XpRule>();
            map[activity] = bucket;
        }

        bucket.Add(rule);
    }

    void Remove(XpRule rule)
    {
        ordered.Remove(rule);
        byId.Remove(rule.Id);
        RemoveFromBucket(byActivity, rule);
        if (rule.IsRateRule)
        {
            RemoveFromBucket(byActivityRate, rule);
        }
        else
        {
            RemoveFromBucket(byActivityAmount, rule);
        }
    }

    static void RemoveFromBucket(Dictionary<string, List<XpRule>> map, XpRule rule)
    {
        if (map.TryGetValue(rule.Activity, out List<XpRule>? bucket))
        {
            bucket.Remove(rule);
            if (bucket.Count == 0)
            {
                map.Remove(rule.Activity);
            }
        }
    }

    /// <summary>Bare activity ids become <c>game:...</c>.</summary>
    public static string NormalizeActivity(string activity)
    {
        if (string.IsNullOrWhiteSpace(activity))
        {
            return activity;
        }

        return activity.Contains(':') ? activity : "game:" + activity;
    }
}
