namespace Prosequor.Data;

/// <summary>Persisted progress for one skill.</summary>
public class SkillProgressState
{
    public int Level { get; set; }
    public float Xp { get; set; }

    /// <summary>Node id to owned tier count. A missing entry means tier 0 (not unlocked).</summary>
    public Dictionary<string, int> UnlockTiers { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Schema 1 stored flat unlock codes; folded into <see cref="UnlockTiers"/> on load.</summary>
    public List<string>? Unlocks { get; set; }

    /// <summary>XP waiting to cross <see cref="Xp.XpBucketFormulas.MinAward"/> before commit.</summary>
    public float Accrued { get; set; }

    /// <summary>Saturation fill meter (pre-penalty raw XP accumulated).</summary>
    public float Fill { get; set; }

    /// <summary>Cached skill bucket capacity for current level.</summary>
    public float CachedCap { get; set; }

    /// <summary>World calendar TotalHours of last XP accrual into this skill.</summary>
    public double LastAccrualTotalHours { get; set; }

    /// <summary>World calendar TotalHours through which drain for this skill has been applied.</summary>
    public double LastDrainTotalHours { get; set; }

    public int GetTier(string nodeId) =>
        !string.IsNullOrWhiteSpace(nodeId) && UnlockTiers.TryGetValue(nodeId, out int tier) ? tier : 0;

    public void SetTier(string nodeId, int tier)
    {
        if (tier <= 0)
        {
            UnlockTiers.Remove(nodeId);
            return;
        }

        UnlockTiers[nodeId] = tier;
    }

    public void MigrateLegacyUnlocks()
    {
        if (Unlocks == null)
        {
            return;
        }

        foreach (string code in Unlocks)
        {
            if (!string.IsNullOrWhiteSpace(code) && GetTier(code) < 1)
            {
                UnlockTiers[code.Trim()] = 1;
            }
        }

        Unlocks = null;
    }

    /// <summary>
    /// Rewrite stored unlock ids to current node ids. One pass over the snapshot so a
    /// forestry <c>lumberjack</c> / <c>forester</c> pair does not chain.
    /// </summary>
    public void RemapUnlockIds(string skillId)
    {
        if (Unlocks != null)
        {
            for (int i = 0; i < Unlocks.Count; i++)
            {
                if (UnlockIdRemap.TryMap(skillId, Unlocks[i], out string mapped))
                {
                    Unlocks[i] = mapped;
                }
            }
        }

        if (UnlockTiers.Count == 0)
        {
            return;
        }

        List<(string Old, string New, int Tier)> moves = new();
        foreach (KeyValuePair<string, int> kv in UnlockTiers)
        {
            if (UnlockIdRemap.TryMap(skillId, kv.Key, out string mapped))
            {
                moves.Add((kv.Key, mapped, kv.Value));
            }
        }

        if (moves.Count == 0)
        {
            return;
        }

        foreach ((string oldId, _, _) in moves)
        {
            UnlockTiers.Remove(oldId);
        }

        foreach ((_, string newId, int tier) in moves)
        {
            int existing = GetTier(newId);
            SetTier(newId, Math.Max(existing, tier));
        }
    }
}

/// <summary>Full player progress blob (ModData + mirror source).</summary>
public class PlayerProgressState
{
    /// <summary>Schema 5 remaps unlock node ids once (see <see cref="UnlockIdRemap"/>).</summary>
    public const int CurrentSchema = 5;

    public int Schema { get; set; } = CurrentSchema;
    public int PlayerLevel { get; set; } = XpCurves.PlayerMinLevel;
    public float PlayerXp { get; set; }
    public int UnlockPoints { get; set; }
    public Dictionary<string, SkillProgressState> Skills { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Attribute scores (Strength, Perception, …). Missing keys are filled on load.</summary>
    public Dictionary<string, int> Attributes { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Growth credit per attribute. Soft-reset: only the winning bucket is zeroed on a grant.
    /// Mirrored for the stats-panel ticks.
    /// </summary>
    public Dictionary<string, float> AttributeBuckets { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Player-track accrued XP pending MinAward flush.</summary>
    public float PlayerAccrued { get; set; }

    /// <summary>Player saturation fill meter.</summary>
    public float PlayerFill { get; set; }

    /// <summary>Cached player bucket capacity for current player level.</summary>
    public float PlayerCachedCap { get; set; }

    /// <summary>World calendar TotalHours of last XP accrual that filled the player bucket.</summary>
    public double PlayerLastAccrualTotalHours { get; set; }

    /// <summary>World calendar TotalHours through which player-bucket drain has been applied.</summary>
    public double PlayerLastDrainTotalHours { get; set; }

    public static PlayerProgressState CreateNew(ISkillRegistry registry)
    {
        PlayerProgressState state = new()
        {
            Schema = CurrentSchema,
            PlayerLevel = XpCurves.PlayerMinLevel,
            PlayerXp = 0f,
            UnlockPoints = 0
        };

        EnsureSkillEntries(state, registry);
        EnsureAttributeEntries(state);
        state.ReconcileLevelsFromXp();
        return state;
    }

    /// <summary>Ensure every registered skill has a progress row (do not wipe existing XP).</summary>
    public static void EnsureSkillEntries(PlayerProgressState state, ISkillRegistry registry)
    {
        foreach (SkillDef def in registry.All)
        {
            if (!state.Skills.ContainsKey(def.Id))
            {
                state.Skills[def.Id] = new SkillProgressState();
            }
        }
    }

    /// <summary>
    /// Ensure every known attribute has a score and bucket entry.
    /// Does not wipe existing values; missing keys get defaults.
    /// </summary>
    public static void EnsureAttributeEntries(PlayerProgressState state)
    {
        NormalizeAttributeDictionary(state.Attributes, AttributeGrowth.DefaultScore);
        NormalizeAttributeDictionary(state.AttributeBuckets, 0f);
    }

    static void NormalizeAttributeDictionary<T>(Dictionary<string, T> dict, T defaultValue)
    {
        foreach (string id in AttributeIds.All)
        {
            if (!dict.ContainsKey(id))
            {
                dict[id] = defaultValue;
            }
        }
    }

    public int GetAttribute(string id)
    {
        string? canonical = AttributeIds.Canonicalize(id);
        if (canonical == null)
        {
            return AttributeGrowth.DefaultScore;
        }

        return Attributes.TryGetValue(canonical, out int score)
            ? score
            : AttributeGrowth.DefaultScore;
    }

    public float GetAttributeBucket(string id)
    {
        string? canonical = AttributeIds.Canonicalize(id);
        if (canonical == null)
        {
            return 0f;
        }

        return AttributeBuckets.TryGetValue(canonical, out float fill) ? fill : 0f;
    }

    /// <summary>XP is truth; recompute cached levels from lifetime XP.</summary>
    public void ReconcileLevelsFromXp()
    {
        PlayerLevel = XpCurves.PlayerLevelFromLifetimeXp(PlayerXp);
        foreach (KeyValuePair<string, SkillProgressState> kv in Skills)
        {
            kv.Value.Level = XpCurves.SkillLevelFromLifetimeXp(kv.Value.Xp);
        }
    }

    public SkillProgressState GetOrCreateSkill(string skillId)
    {
        if (!Skills.TryGetValue(skillId, out SkillProgressState? skill) || skill == null)
        {
            skill = new SkillProgressState();
            Skills[skillId] = skill;
        }

        return skill;
    }
}
