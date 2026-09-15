using Prosequor.Player;

namespace Prosequor.Ability.Hooks;

/// <summary>Per-player subset of <see cref="AbilityRuleIndex"/> containing only owned/active rules.</summary>
public sealed class ActiveAbilityRuleCache
{
    readonly Dictionary<(HookId Hook, VerbId Verb, PhaseId Phase), AbilityRule[]> buckets = new();

    public static ActiveAbilityRuleCache Rebuild(AbilityRuleIndex index, IPlayerProgress progress)
    {
        ActiveAbilityRuleCache cache = new();
        foreach (KeyValuePair<(HookId Hook, VerbId Verb, PhaseId Phase), IReadOnlyList<AbilityRule>> kv in index.AllBuckets)
        {
            List<AbilityRule> active = new();
            foreach (AbilityRule rule in kv.Value)
            {
                if (AbilityPipeline.IsActive(rule, progress))
                {
                    active.Add(rule);
                }
            }

            if (active.Count > 0)
            {
                cache.buckets[kv.Key] = active.ToArray();
            }
        }

        return cache;
    }

    public bool TryGet(HookId hook, VerbId verb, PhaseId phase, out IReadOnlyList<AbilityRule> rules)
    {
        if (buckets.TryGetValue((hook, verb, phase), out AbilityRule[]? found))
        {
            rules = found;
            return true;
        }

        rules = Array.Empty<AbilityRule>();
        return false;
    }
}
