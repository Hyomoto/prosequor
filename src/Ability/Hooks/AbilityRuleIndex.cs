using Prosequor.Data;

namespace Prosequor.Ability.Hooks;

/// <summary>Global hook+verb+phase buckets of compiled rules, pre-sorted by priority then source order.</summary>
public sealed class AbilityRuleIndex
{
    readonly Dictionary<(HookId Hook, VerbId Verb, PhaseId Phase), AbilityRule[]> buckets = new();

    public static AbilityRuleIndex Build(ISkillRegistry skills) =>
        Build(skills, attributeStats: null);

    public static AbilityRuleIndex Build(ISkillRegistry skills, IAttributeStatRegistry? attributeStats)
    {
        Dictionary<(HookId, VerbId, PhaseId), List<AbilityRule>> building = new();
        foreach (SkillDef skill in skills.All)
        {
            foreach (AbilityRule rule in skill.Rules)
            {
                Add(building, rule);
            }
        }

        if (attributeStats != null)
        {
            foreach (AttributeStatDef attr in attributeStats.All)
            {
                foreach (AbilityRule rule in attr.Rules)
                {
                    Add(building, rule);
                }
            }
        }

        AbilityRuleIndex index = new();
        foreach (KeyValuePair<(HookId, VerbId, PhaseId), List<AbilityRule>> kv in building)
        {
            kv.Value.Sort(CompareExecutionOrder);
            index.buckets[kv.Key] = kv.Value.ToArray();
        }

        return index;
    }

    static void Add(
        Dictionary<(HookId, VerbId, PhaseId), List<AbilityRule>> building,
        AbilityRule rule)
    {
        (HookId, VerbId, PhaseId) key = (rule.Hook, rule.Verb, rule.Phase);
        if (!building.TryGetValue(key, out List<AbilityRule>? list))
        {
            list = new List<AbilityRule>();
            building[key] = list;
        }

        list.Add(rule);
    }

    public IReadOnlyList<AbilityRule> Get(HookId hook, VerbId verb, PhaseId phase)
    {
        if (buckets.TryGetValue((hook, verb, phase), out AbilityRule[]? rules))
        {
            return rules;
        }

        return Array.Empty<AbilityRule>();
    }

    public IEnumerable<KeyValuePair<(HookId Hook, VerbId Verb, PhaseId Phase), IReadOnlyList<AbilityRule>>> AllBuckets
    {
        get
        {
            foreach (KeyValuePair<(HookId, VerbId, PhaseId), AbilityRule[]> kv in buckets)
            {
                yield return new KeyValuePair<(HookId Hook, VerbId Verb, PhaseId Phase), IReadOnlyList<AbilityRule>>(
                    kv.Key,
                    kv.Value);
            }
        }
    }

    public int TotalRuleCount
    {
        get
        {
            int count = 0;
            foreach (AbilityRule[] rules in buckets.Values)
            {
                count += rules.Length;
            }

            return count;
        }
    }

    internal static int CompareExecutionOrder(AbilityRule a, AbilityRule b)
    {
        int cmp = a.Priority.CompareTo(b.Priority);
        return cmp != 0 ? cmp : a.SourceOrder.CompareTo(b.SourceOrder);
    }
}
