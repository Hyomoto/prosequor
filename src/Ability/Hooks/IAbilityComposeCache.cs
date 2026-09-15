namespace Prosequor.Ability.Hooks;

/// <summary>Per-player compose memo exposed to <see cref="AbilityPipeline"/> and fixtures.</summary>
public interface IAbilityComposeCache
{
    ComposeMemo ComposeMemo { get; }

    void BumpProgressRevision();

    /// <summary>
    /// True when this progress owns an active-rule cache (empty bucket is still a hit).
    /// </summary>
    bool TryGetActiveRules(
        HookId hook,
        VerbId verb,
        PhaseId phase,
        out IReadOnlyList<AbilityRule> rules);
}
