using Newtonsoft.Json.Linq;
using Prosequor.Data;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace Prosequor.Progress;

/// <summary>
/// Server leveling knobs in <c>ModConfig/prosequor/server.json</c>.
/// Defaults match the shipped curves and level-up schedules.
/// </summary>
public sealed class ServerLevelingConfig
{
    public const string ConfigFileName = "prosequor/server.json";
    public const string SkillPointRuleId = "prosequor:earn-skill-point";
    public const string AttributeRuleId = "prosequor:earn-attribute";
    public const string SpecializationRuleId = "prosequor:earn-specialization-point";

    public const string SkillLevelsPerPointProperty = "SkillLevelsPerPoint";
    public const string SkillPointEveryProperty = "SkillPointEvery";
    public const string AttributePointLevelsProperty = "AttributePointLevels";
    public const string SpecializationPointLevelsProperty = "SpecializationPointLevels";
    public const string MaxPlayerLevelProperty = "MaxPlayerLevel";
    public const string XpScaleProperty = "XpScale";

    public const int DefaultSkillLevelsPerPoint = UnlockPointPolicy.SkillLevelsPerPoint;
    public const int DefaultSkillPointEvery = 1;
    public const int DefaultMaxPlayerLevel = XpCurves.PlayerMaxLevel;
    public const float DefaultXpScale = 1f;

    public const int MinSkillLevelsPerPoint = 0;
    public const int MaxSkillLevelsPerPoint = 1000;
    public const int MinSkillPointEvery = 1;
    public const int MaxSkillPointEvery = 1000;
    public const int MinPlayerLevel = XpCurves.PlayerMinLevel;
    public const int MaxPlayerLevelCap = 1000;
    public const float MinXpScale = 0f;
    public const float MaxXpScale = 1000f;

    static readonly int[] DefaultAttributeLevels =
    [
        5, 10, 15, 19, 23, 27, 30, 33, 36, 39, 41, 43, 45, 47, 49, 50
    ];

    static readonly int[] DefaultSpecializationLevels = [10, 20, 30, 40, 50];

    public int SkillLevelsPerPoint { get; set; } = DefaultSkillLevelsPerPoint;
    public int SkillPointEvery { get; set; } = DefaultSkillPointEvery;
    public int[] AttributePointLevels { get; set; } = Copy(DefaultAttributeLevels);
    public int[] SpecializationPointLevels { get; set; } = Copy(DefaultSpecializationLevels);
    public int MaxPlayerLevel { get; set; } = DefaultMaxPlayerLevel;
    public float XpScale { get; set; } = DefaultXpScale;

    public static ServerLevelingConfig CreateDefault() => new()
    {
        SkillLevelsPerPoint = DefaultSkillLevelsPerPoint,
        SkillPointEvery = DefaultSkillPointEvery,
        AttributePointLevels = Copy(DefaultAttributeLevels),
        SpecializationPointLevels = Copy(DefaultSpecializationLevels),
        MaxPlayerLevel = DefaultMaxPlayerLevel,
        XpScale = DefaultXpScale
    };

    public static int[] DefaultAttributePointLevels() => Copy(DefaultAttributeLevels);

    public static int[] DefaultSpecializationPointLevels() => Copy(DefaultSpecializationLevels);

    /// <summary>
    /// Multiplier on lifetime skill XP after buckets have already accounted the raw amount.
    /// <paramref name="scale"/> of 1 leaves <paramref name="committed"/> unchanged.
    /// </summary>
    public static float ScaleEarned(float committed, float scale)
    {
        if (committed <= 0f || scale <= 0f)
        {
            return 0f;
        }

        if (scale == 1f)
        {
            return committed;
        }

        return committed * scale;
    }

    public static int ClampSkillLevelsPerPoint(int value) =>
        Math.Clamp(value, MinSkillLevelsPerPoint, MaxSkillLevelsPerPoint);

    public static int ClampSkillPointEvery(int value) =>
        Math.Clamp(value, MinSkillPointEvery, MaxSkillPointEvery);

    public static int ClampMaxPlayerLevel(int value) =>
        Math.Clamp(value, MinPlayerLevel, MaxPlayerLevelCap);

    public static float ClampXpScale(float value)
    {
        if (!float.IsFinite(value))
        {
            return DefaultXpScale;
        }

        return Math.Clamp(value, MinXpScale, MaxXpScale);
    }

    /// <summary>
    /// Fills missing or unreadable properties with defaults and drops level-list entries
    /// outside <c>1..MaxPlayerLevel</c>. Returns true when <paramref name="root"/> changed.
    /// </summary>
    public static bool Normalize(JObject root, out ServerLevelingConfig settings)
    {
        ArgumentNullException.ThrowIfNull(root);
        bool dirty = false;
        int skillLevels = ReadInt(
            root,
            SkillLevelsPerPointProperty,
            DefaultSkillLevelsPerPoint,
            ClampSkillLevelsPerPoint,
            ref dirty);
        int skillEvery = ReadInt(
            root,
            SkillPointEveryProperty,
            DefaultSkillPointEvery,
            ClampSkillPointEvery,
            ref dirty);
        int maxPlayer = ReadInt(
            root,
            MaxPlayerLevelProperty,
            DefaultMaxPlayerLevel,
            ClampMaxPlayerLevel,
            ref dirty);
        float scale = ReadScale(root, ref dirty);
        int[] attributes = ReadLevels(
            root,
            AttributePointLevelsProperty,
            DefaultAttributeLevels,
            maxPlayer,
            ref dirty);
        int[] specializations = ReadLevels(
            root,
            SpecializationPointLevelsProperty,
            DefaultSpecializationLevels,
            maxPlayer,
            ref dirty);

        settings = new ServerLevelingConfig
        {
            SkillLevelsPerPoint = skillLevels,
            SkillPointEvery = skillEvery,
            AttributePointLevels = attributes,
            SpecializationPointLevels = specializations,
            MaxPlayerLevel = maxPlayer,
            XpScale = scale
        };
        return dirty;
    }

    /// <summary>Reads the server file, creating it when missing. Unreadable files use defaults.</summary>
    public static ServerLevelingConfig Load(ICoreAPI api)
    {
        try
        {
            JsonObject? loaded = api.LoadModConfig(ConfigFileName);
            if (loaded != null && loaded.Token is not JObject)
            {
                api.Logger.Warning(
                    "[prosequor] {0} is not a JSON object. Using default leveling.",
                    ConfigFileName);
                return CreateDefault();
            }

            JObject root = loaded?.Token as JObject ?? new JObject();
            bool dirty = Normalize(root, out ServerLevelingConfig settings);
            if (loaded == null)
            {
                api.StoreModConfig(settings, ConfigFileName);
            }
            else if (dirty)
            {
                api.StoreModConfig(new JsonObject(root), ConfigFileName);
            }

            return settings;
        }
        catch (Exception ex)
        {
            api.Logger.Warning(
                "[prosequor] Could not read {0}: {1}",
                ConfigFileName,
                ex.Message);
            return CreateDefault();
        }
    }

    /// <summary>
    /// Copy of <paramref name="rules"/> with the built-in skill-point, attribute, and
    /// specialization schedules replaced. Other rules are copied through.
    /// </summary>
    public static List<LevelUpRuleDef> Overlay(
        IReadOnlyList<LevelUpRuleDef> rules,
        ServerLevelingConfig settings,
        out List<string> missing,
        bool specializationOnly = false)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(settings);
        missing = new List<string>();
        bool foundSkill = false;
        bool foundAttribute = false;
        bool foundSpecialization = false;
        List<LevelUpRuleDef> next = new(rules.Count);
        foreach (LevelUpRuleDef rule in rules)
        {
            if (!specializationOnly && SameId(rule.Id, SkillPointRuleId))
            {
                next.Add(rule.WithSchedule(settings.SkillPointEvery, levels: null));
                foundSkill = true;
                continue;
            }

            if (!specializationOnly && SameId(rule.Id, AttributeRuleId))
            {
                next.Add(rule.WithSchedule(every: null, ToSet(settings.AttributePointLevels)));
                foundAttribute = true;
                continue;
            }

            if (SameId(rule.Id, SpecializationRuleId))
            {
                next.Add(rule.WithSchedule(every: null, ToSet(settings.SpecializationPointLevels)));
                foundSpecialization = true;
                continue;
            }

            next.Add(rule);
        }

        if (!specializationOnly && !foundSkill)
        {
            missing.Add(SkillPointRuleId);
        }

        if (!specializationOnly && !foundAttribute)
        {
            missing.Add(AttributeRuleId);
        }

        if (!foundSpecialization)
        {
            missing.Add(SpecializationRuleId);
        }

        return next;
    }

    /// <summary>Install <see cref="Overlay"/> on <paramref name="registry"/>. Missing ids are warned and skipped.</summary>
    public void ApplyTo(LevelUpRegistry registry, Action<string>? warn, bool specializationOnly = false)
    {
        ArgumentNullException.ThrowIfNull(registry);
        List<LevelUpRuleDef> next = Overlay(registry.Rules, this, out List<string> missing, specializationOnly);
        registry.Install(next);
        if (warn == null)
        {
            return;
        }

        foreach (string id in missing)
        {
            warn($"[prosequor] Server leveling skipped '{id}': no level-up rule with that id.");
        }
    }

    /// <summary>Client copy: player cap and specialization list from the server, other knobs unchanged.</summary>
    public ServerLevelingConfig WithClientSync(int maxPlayerLevel, IReadOnlyList<int>? specializationLevels)
    {
        int cap = ClampMaxPlayerLevel(maxPlayerLevel);
        return new ServerLevelingConfig
        {
            SkillLevelsPerPoint = SkillLevelsPerPoint,
            SkillPointEvery = SkillPointEvery,
            AttributePointLevels = Copy(AttributePointLevels),
            SpecializationPointLevels = FilterLevels(specializationLevels, cap),
            MaxPlayerLevel = cap,
            XpScale = XpScale
        };
    }

    static int ReadInt(
        JObject root,
        string property,
        int fallback,
        System.Func<int, int> clamp,
        ref bool dirty)
    {
        JToken? token = root[property];
        if (token == null || token.Type == JTokenType.Null)
        {
            int filled = clamp(fallback);
            root[property] = filled;
            dirty = true;
            return filled;
        }

        if (token.Type is JTokenType.Integer or JTokenType.Float)
        {
            double raw = token.Value<double>();
            if (double.IsFinite(raw))
            {
                int value = clamp((int)Math.Round(raw, MidpointRounding.AwayFromZero));
                if (token.Type == JTokenType.Integer && token.Value<long>() == value)
                {
                    return value;
                }

                root[property] = value;
                dirty = true;
                return value;
            }
        }

        int reset = clamp(fallback);
        root[property] = reset;
        dirty = true;
        return reset;
    }

    static float ReadScale(JObject root, ref bool dirty)
    {
        JToken? token = root[XpScaleProperty];
        if (token == null || token.Type == JTokenType.Null)
        {
            root[XpScaleProperty] = DefaultXpScale;
            dirty = true;
            return DefaultXpScale;
        }

        if (token.Type is JTokenType.Integer or JTokenType.Float)
        {
            double raw = token.Value<double>();
            if (double.IsFinite(raw))
            {
                float value = ClampXpScale((float)raw);
                if (token.Type == JTokenType.Integer && value == Math.Truncate(value) && token.Value<long>() == (long)value)
                {
                    return value;
                }

                if (token.Type == JTokenType.Float && Math.Abs(token.Value<double>() - value) < 0.0000001d)
                {
                    return value;
                }

                root[XpScaleProperty] = value;
                dirty = true;
                return value;
            }
        }

        root[XpScaleProperty] = DefaultXpScale;
        dirty = true;
        return DefaultXpScale;
    }

    static int[] ReadLevels(JObject root, string property, int[] fallback, int maxPlayerLevel, ref bool dirty)
    {
        JToken? token = root[property];
        int[] defaults = FilterLevels(fallback, maxPlayerLevel);
        if (token == null || token.Type == JTokenType.Null)
        {
            root[property] = new JArray(defaults);
            dirty = true;
            return defaults;
        }

        if (token is not JArray array)
        {
            root[property] = new JArray(defaults);
            dirty = true;
            return defaults;
        }

        List<int> parsed = new(array.Count);
        bool dropped = false;
        foreach (JToken entry in array)
        {
            if (entry.Type is not (JTokenType.Integer or JTokenType.Float))
            {
                dropped = true;
                continue;
            }

            double raw = entry.Value<double>();
            if (!double.IsFinite(raw))
            {
                dropped = true;
                continue;
            }

            int level = (int)Math.Round(raw, MidpointRounding.AwayFromZero);
            if (entry.Type != JTokenType.Integer || entry.Value<long>() != level)
            {
                dropped = true;
            }

            parsed.Add(level);
        }

        int[] levels = FilterLevels(parsed, maxPlayerLevel);
        if (dropped || !SameLevels(parsed, levels) || !SameToken(array, levels))
        {
            root[property] = new JArray(levels);
            dirty = true;
        }

        return levels;
    }

    public static int[] FilterLevels(IReadOnlyList<int>? levels, int maxPlayerLevel)
    {
        if (levels == null || levels.Count == 0)
        {
            return Array.Empty<int>();
        }

        int cap = ClampMaxPlayerLevel(maxPlayerLevel);
        HashSet<int> unique = new();
        foreach (int level in levels)
        {
            if (level >= MinPlayerLevel && level <= cap)
            {
                unique.Add(level);
            }
        }

        int[] sorted = new int[unique.Count];
        unique.CopyTo(sorted);
        Array.Sort(sorted);
        return sorted;
    }

    static bool SameLevels(List<int> parsed, int[] filtered)
    {
        if (parsed.Count != filtered.Length)
        {
            return false;
        }

        for (int i = 0; i < filtered.Length; i++)
        {
            if (parsed[i] != filtered[i])
            {
                return false;
            }
        }

        return true;
    }

    static bool SameToken(JArray array, int[] levels)
    {
        if (array.Count != levels.Length)
        {
            return false;
        }

        for (int i = 0; i < levels.Length; i++)
        {
            JToken entry = array[i];
            if (entry.Type != JTokenType.Integer || entry.Value<long>() != levels[i])
            {
                return false;
            }
        }

        return true;
    }

    static HashSet<int> ToSet(int[]? levels)
    {
        HashSet<int> set = new();
        if (levels == null)
        {
            return set;
        }

        foreach (int level in levels)
        {
            set.Add(level);
        }

        return set;
    }

    static bool SameId(string? id, string expected) =>
        string.Equals(id, expected, StringComparison.OrdinalIgnoreCase);

    static int[] Copy(int[] source)
    {
        int[] copy = new int[source.Length];
        Array.Copy(source, copy, source.Length);
        return copy;
    }
}
