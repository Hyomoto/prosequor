using Prosequor.Ability;
using Prosequor.Ability.Hooks;
using Prosequor.Data;
using Prosequor.Progress;
using Prosequor.Xp;

namespace Prosequor.Player;

/// <summary>
/// Read-only <see cref="IPlayerProgress"/> over a stolen or preloaded state.
/// Mutations throw; the live entity reloads ModData on join.
/// </summary>
public sealed class ParkedPlayerProgress : IPlayerProgress, IAbilityComposeCache
{
    readonly PlayerProgressState state;
    readonly ComposeMemo composeMemo = new();
    readonly ActiveAbilityRuleCache? abilityCache;
    readonly ISkillRegistry? registry;

    public ParkedPlayerProgress(
        PlayerProgressState state,
        AbilityRuleIndex? ruleIndex = null,
        ISkillRegistry? registry = null)
    {
        this.state = state ?? throw new ArgumentNullException(nameof(state));
        this.registry = registry;
        if (ruleIndex != null)
        {
            abilityCache = ActiveAbilityRuleCache.Rebuild(ruleIndex, this);
        }
    }

    public event Action? Changed
    {
        add { }
        remove { }
    }

    public PlayerProgressState State => state;

    public ComposeMemo ComposeMemo => composeMemo;

    public int PlayerLevel => state.PlayerLevel;
    public float PlayerXp => state.PlayerXp;
    public int UnlockPoints => state.UnlockPoints;

    public float PlayerXpUntilNext =>
        XpCurves.XpUntilNextPlayerLevel(state.PlayerXp, state.PlayerLevel);

    public void BumpProgressRevision() => composeMemo.BumpProgressRevision();

    public bool TryGetActiveRules(
        HookId hook,
        VerbId verb,
        PhaseId phase,
        out IReadOnlyList<AbilityRule> rules)
    {
        if (abilityCache == null)
        {
            rules = Array.Empty<AbilityRule>();
            return false;
        }

        if (abilityCache.TryGet(hook, verb, phase, out rules))
        {
            return true;
        }

        rules = Array.Empty<AbilityRule>();
        return true;
    }

    public int GetSkillLevel(string skillId) =>
        state.Skills.TryGetValue(skillId, out SkillProgressState? s) ? s.Level : 0;

    public float GetSkillXp(string skillId) =>
        state.Skills.TryGetValue(skillId, out SkillProgressState? s) ? s.Xp : 0f;

    public IReadOnlyList<string> GetUnlocks(string skillId)
    {
        if (!state.Skills.TryGetValue(skillId, out SkillProgressState? s))
        {
            return Array.Empty<string>();
        }

        return s.UnlockTiers
            .Where(kv => kv.Value > 0)
            .Select(kv => kv.Key)
            .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public bool HasUnlock(string skillId, string code) => GetUnlockTier(skillId, code) > 0;

    public int GetUnlockTier(string skillId, string nodeId)
    {
        if (!state.Skills.TryGetValue(skillId, out SkillProgressState? s))
        {
            return 0;
        }

        return s.GetTier(nodeId);
    }

    public int GetAttribute(string id) => state.GetAttribute(id);

    public float GetAttributeBucket(string id) => state.GetAttributeBucket(id);

    public void GetPlayerBar(out float intoLevel, out int needForNext, out int level)
    {
        level = state.PlayerLevel;
        intoLevel = XpCurves.InLevelPlayerXp(state.PlayerXp, level);
        needForNext = XpCurves.XpToNextPlayerLevel(level);
        if (needForNext <= 0)
        {
            intoLevel = 1f;
            needForNext = 1;
        }
        else if (intoLevel > needForNext)
        {
            intoLevel = needForNext;
        }
    }

    public void GetSkillBar(string skillId, out float intoLevel, out int needForNext, out int level)
    {
        if (!state.Skills.TryGetValue(skillId, out SkillProgressState? skill))
        {
            level = 0;
            intoLevel = 0f;
            needForNext = Math.Max(1, XpCurves.XpToNextSkillLevel(0));
            return;
        }

        level = skill.Level;
        int max = XpCurves.SkillMaxLevel;
        if (registry != null && registry.TryGet(skillId, out SkillDef def) && def.MaxLevel > 0)
        {
            max = Math.Min(XpCurves.SkillMaxLevel, def.MaxLevel);
        }

        XpCurves.SkillBar(skill.Xp, level, max, out intoLevel, out needForNext);
    }

    public void AddPlayerXp(float amount, XpAwardMode mode = XpAwardMode.Earn) =>
        throw ParkedReadOnly();

    public void AddSkillXp(
        string skillId,
        float amount,
        AbilityAction? fact = null,
        XpAwardMode mode = XpAwardMode.Earn) =>
        throw ParkedReadOnly();

    public void AddUnlockPoints(int amount) => throw ParkedReadOnly();

    public void SetPlayerLevel(int level) => throw ParkedReadOnly();

    public void SetSkillLevel(string skillId, int level) => throw ParkedReadOnly();

    public void AddAttributeBucket(string id, float amount) => throw ParkedReadOnly();

    public void SetAttribute(string id, int score) => throw ParkedReadOnly();

    public bool GrantUnlock(string skillId, string code, int cost = 1) =>
        throw ParkedReadOnly();

    public bool RevokeUnlock(string skillId, string code, int refund = 1) =>
        throw ParkedReadOnly();

    public UnlockPurchaseStatus TryPurchaseNode(string skillId, string nodeId) =>
        throw ParkedReadOnly();

    static InvalidOperationException ParkedReadOnly() =>
        new("Parked progress is read-only.");
}
