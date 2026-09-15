using Prosequor.Data;
using Vintagestory.API.Common;

namespace Prosequor.Ability.Hooks;

/// <summary>
/// Runs owned matching rules for one typed hook/verb/phase.
/// Lower priority first, then source order. Each action receives the previous output.
/// </summary>
public sealed class AbilityPipeline
{
    readonly IAbilityActionRegistry actions;

    public AbilityRuleIndex RuleIndex { get; }

    public AbilityPipeline(IAbilityActionRegistry actions, ISkillRegistry skills)
        : this(actions, skills, attributeStats: null)
    {
    }

    public AbilityPipeline(
        IAbilityActionRegistry actions,
        ISkillRegistry skills,
        IAttributeStatRegistry? attributeStats)
    {
        this.actions = actions;
        RuleIndex = AbilityRuleIndex.Build(skills, attributeStats);
    }

    public TValue Run<TContext, TValue>(
        HookId hook,
        VerbId verb,
        PhaseId phase,
        TContext context,
        TValue value)
        where TContext : class, IHookContext
    {
        if (context.Progress == null)
        {
            return value;
        }

        IReadOnlyList<AbilityRule> candidates;
        bool filterActiveInline;
        if (context.Progress is IAbilityComposeCache cache
            && cache.TryGetActiveRules(hook, verb, phase, out IReadOnlyList<AbilityRule> active))
        {
            candidates = active;
            filterActiveInline = false;
        }
        else
        {
            candidates = RuleIndex.Get(hook, verb, phase);
            filterActiveInline = true;
        }

        if (candidates.Count == 0)
        {
            return value;
        }

        AbilityAction? fact = context.Fact;
        IPlayerProgress progress = context.Progress;
        FactFingerprint fingerprint = FactFingerprint.From(fact);
        long baseBits = GetBaseBits(value);

        if (TryReadMemo(hook, verb, phase, fingerprint, baseBits, context.Progress, candidates, out TValue cached))
        {
            return cached;
        }

        object current = value!;
        foreach (AbilityRule rule in candidates)
        {
            if (filterActiveInline && !IsActive(rule, progress))
            {
                continue;
            }

            if (!rule.When.Matches(fact))
            {
                continue;
            }

            if (!actions.TryGet(rule.Action, hook, verb, phase, out IAbilityActionHandler handler))
            {
                continue;
            }

            current = handler.Apply(context, current, rule.Parameters, rule.Source);
        }

        TValue result = (TValue)current;
        StoreMemo(hook, verb, phase, fingerprint, baseBits, context.Progress, candidates, result);
        return result;
    }

    static bool TryReadMemo<TValue>(
        HookId hook,
        VerbId verb,
        PhaseId phase,
        FactFingerprint fingerprint,
        long baseBits,
        IPlayerProgress progress,
        IReadOnlyList<AbilityRule> candidates,
        out TValue value)
    {
        value = default!;
        if (!IsMemoEligible<TValue>(hook, candidates))
        {
            return false;
        }

        if (progress is not IAbilityComposeCache cache)
        {
            return false;
        }

        return cache.ComposeMemo.TryGet(hook, verb, phase, fingerprint, baseBits, out value);
    }

    static void StoreMemo<TValue>(
        HookId hook,
        VerbId verb,
        PhaseId phase,
        FactFingerprint fingerprint,
        long baseBits,
        IPlayerProgress progress,
        IReadOnlyList<AbilityRule> candidates,
        TValue value)
    {
        if (!IsMemoEligible<TValue>(hook, candidates))
        {
            return;
        }

        if (progress is not IAbilityComposeCache cache)
        {
            return;
        }

        cache.ComposeMemo.Store(hook, verb, phase, fingerprint, baseBits, value);
    }

    static bool IsMemoEligible<TValue>(HookId hook, IReadOnlyList<AbilityRule> candidates)
    {
        if (hook == HookIds.PlayerInteraction)
        {
            // on-damage stays uncached (player-interaction verb; damage facts vary per hit).
            foreach (AbilityRule rule in candidates)
            {
                if (rule.Verb.Equals(VerbIds.OnDamage))
                {
                    return false;
                }
            }
        }

        if (typeof(TValue) != typeof(float) && typeof(TValue) != typeof(int))
        {
            return false;
        }

        foreach (AbilityRule rule in candidates)
        {
            if (rule.Action == ActionIds.Chance
                || rule.Action == ActionIds.HasUnlock
                || rule.Action == ActionIds.RefundIngredients
                || rule.Action == ActionIds.RestockLastBait
                || rule.Action == ActionIds.RestoreConsumedBait
                || rule.Action == ActionIds.UpgradeOreGrade
                || rule.Action == ActionIds.AllowAnimalPet
                || rule.Action == ActionIds.AllowMountedRideWithoutSaddle
                || rule.Action == ActionIds.SetTrue
                || rule.Action == ActionIds.AddFriendliness)
            {
                return false;
            }
        }

        return true;
    }

    static long GetBaseBits<TValue>(TValue value) =>
        value switch
        {
            float f => ComposeMemoBaseBits.From(f),
            int i => ComposeMemoBaseBits.From(i),
            _ => 0L
        };

    internal static bool IsActive(AbilityRule rule, IPlayerProgress progress)
    {
        if (rule.Source.IsAttributeRule)
        {
            int score = progress.GetAttribute(rule.Source.AttributeId!);
            if (score < rule.Source.MinAttributeScore)
            {
                return false;
            }

            if (rule.Source.MaxAttributeScore is int max && score > max)
            {
                return false;
            }

            return true;
        }

        if (!rule.Source.IsTreeRule)
        {
            return true;
        }

        int owned = progress.GetUnlockTier(rule.Source.SkillId, rule.Source.NodeId!);
        return owned > 0 && owned == rule.Source.Tier;
    }
}
