using Prosequor.Ability;
using Prosequor.Ability.Hooks;
using Prosequor.Data;
using Prosequor.Progress;
using Prosequor.Xp;

namespace Prosequor;

/// <summary>Public read/mutate surface for Layer 2/3.</summary>
public interface IPlayerProgress
{
    /// <summary>Raised after a complete progress mutation has been applied.</summary>
    event Action? Changed;

    int PlayerLevel { get; }
    float PlayerXp { get; }
    int UnlockPoints { get; }

    int GetSkillLevel(string skillId);
    float GetSkillXp(string skillId);
    IReadOnlyList<string> GetUnlocks(string skillId);
    bool HasUnlock(string skillId, string code);

    /// <summary>Owned tier count for a node; 0 when not unlocked.</summary>
    int GetUnlockTier(string skillId, string nodeId);

    /// <summary>Attribute score (default 10 when unknown / unset).</summary>
    int GetAttribute(string id);

    /// <summary>Growth bucket fill for an attribute (0 when unknown).</summary>
    float GetAttributeBucket(string id);

    /// <summary>XP remaining until next player level (0 at cap).</summary>
    float PlayerXpUntilNext { get; }

    /// <summary>In-level player XP and need for the progress bar.</summary>
    void GetPlayerBar(out float intoLevel, out int needForNext, out int level);

    /// <summary>In-level skill XP and need for the progress bar.</summary>
    void GetSkillBar(string skillId, out float intoLevel, out int needForNext, out int level);

    // Server-only mutations
    void AddPlayerXp(float amount, XpAwardMode mode = XpAwardMode.Earn);
    void AddSkillXp(
        string skillId,
        float amount,
        AbilityAction? fact = null,
        XpAwardMode mode = XpAwardMode.Earn);
    void AddUnlockPoints(int amount);
    void SetPlayerLevel(int level);
    void SetSkillLevel(string skillId, int level);

    /// <summary>Add growth credit to an attribute bucket (skill level-up fill / debug command).</summary>
    void AddAttributeBucket(string id, float amount);

    /// <summary>Set a body attribute score (clamped) and refresh attribute effects.</summary>
    void SetAttribute(string id, int score);

    bool GrantUnlock(string skillId, string code, int cost = 1);
    bool RevokeUnlock(string skillId, string code, int refund = 1);

    /// <summary>Server-authoritative purchase of a declared skill-tree node.</summary>
    UnlockPurchaseStatus TryPurchaseNode(string skillId, string nodeId);
}
