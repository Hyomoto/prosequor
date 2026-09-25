using Prosequor.Ability;
using Prosequor.Data;
using Prosequor.Player;
using Prosequor.Progress;
using Prosequor.Xp;

namespace Prosequor.Pure.Tests;

/// <summary>Minimal progress stub for action Apply tests (skill levels + attribute scores).</summary>
public sealed class ActionTestProgress : IPlayerProgress
{
    readonly Dictionary<string, int> skillLevels = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, int> attributes = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, int> unlockTiers = new(StringComparer.OrdinalIgnoreCase);

    public event Action? Changed;

    public int PlayerLevel => 1;
    public float PlayerXp => 0;
    public int UnlockPoints => 0;
    public float PlayerXpUntilNext => 0;

    public void SetSkillLevel(string skillId, int level)
    {
        skillLevels[skillId] = level;
        Changed?.Invoke();
    }

    public void SetAttribute(string id, int score)
    {
        attributes[id] = score;
        Changed?.Invoke();
    }

    public void SetUnlockTier(string skillId, string nodeId, int tier)
    {
        unlockTiers[$"{skillId}:{nodeId}"] = tier;
        Changed?.Invoke();
    }

    public int GetSkillLevel(string skillId) =>
        skillLevels.TryGetValue(skillId, out int level) ? level : 0;

    public int GetAttribute(string id) =>
        attributes.TryGetValue(id, out int score) ? score : AttributeGrowth.DefaultScore;

    public float GetSkillXp(string skillId) => 0;
    public IReadOnlyList<string> GetUnlocks(string skillId) => Array.Empty<string>();
    public bool HasUnlock(string skillId, string code) => GetUnlockTier(skillId, code) > 0;

    public int GetUnlockTier(string skillId, string nodeId) =>
        unlockTiers.TryGetValue($"{skillId}:{nodeId}", out int tier) ? tier : 0;
    public float GetAttributeBucket(string id) => 0f;

    public bool HasSkillAccess(string skillId) => true;

    public void GetPlayerBar(out float intoLevel, out int needForNext, out int level)
    {
        intoLevel = 0;
        needForNext = 1;
        level = PlayerLevel;
    }

    public void GetSkillBar(string skillId, out float intoLevel, out int needForNext, out int level)
    {
        intoLevel = 0;
        needForNext = 1;
        level = GetSkillLevel(skillId);
    }

    public void AddPlayerXp(float amount, XpAwardMode mode = XpAwardMode.Earn) { }
    public void AddSkillXp(
        string skillId,
        float amount,
        AbilityAction? fact = null,
        XpAwardMode mode = XpAwardMode.Earn) { }
    public void AddUnlockPoints(int amount) { }
    public void SetPlayerLevel(int level) { }
    public void AddAttributeBucket(string id, float amount) { }
    public bool GrantUnlock(string skillId, string code, int cost = 1) => false;
    public bool RevokeUnlock(string skillId, string code, int refund = 1) => false;
    public UnlockPurchaseStatus TryPurchaseNode(string skillId, string nodeId) =>
        UnlockPurchaseStatus.UnknownNode;
}
