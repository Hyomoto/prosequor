using Newtonsoft.Json.Linq;

namespace Prosequor.Data;

/// <summary>
/// Sparse <c>ModConfig/prosequor/server.json</c>: a preset id plus only values that differ from it.
/// </summary>
public static class ServerProgressionConfig
{
    public const string ConfigFileName = "prosequor/server.json";
    public const string KeyPreset = "preset";
    public const string KeyMaxPlayerLevel = "maxPlayerLevel";
    public const string KeySkillPointsPerPlayerLevel = "skillPointsPerPlayerLevel";
    public const string KeySkillPointsPerSkillLevel = "skillPointsPerSkillLevel";
    public const string KeySpecializationLevels = "specializationLevels";
    public const string KeyXpGain = "xpGain";

    public static readonly string[] OverlayKeys =
    [
        KeyMaxPlayerLevel,
        KeySkillPointsPerPlayerLevel,
        KeySkillPointsPerSkillLevel,
        KeySpecializationLevels,
        KeyXpGain
    ];

    /// <summary>
    /// Rewrites <paramref name="root"/> to <c>preset</c> plus overrides that differ from that preset.
    /// A missing preset id is <see cref="ProgressionProfile.MultiplayerPresetId"/>.
    /// Returns true when <paramref name="root"/> changed.
    /// </summary>
    public static bool Normalize(
        JObject root,
        IReadOnlyDictionary<string, ProgressionProfile> presets,
        ProgressionProfile baseline,
        out ProgressionProfile resolved,
        IList<string> warnings)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(presets);
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(warnings);

        string requested = ReadPresetId(root);
        bool known = TryResolvePreset(presets, requested, baseline, out ProgressionProfile fromPreset, out string canonicalId);
        if (!known)
        {
            warnings.Add(
                $"Unknown preset '{requested}'. Using the baseline table; other keys still apply.");
        }

        List<JProperty> kept = new();
        int playerCap = fromPreset.MaxPlayerLevel;
        int? maxOverride = null;
        LevelSet? playerPoints = null;
        LevelSet? skillPoints = null;
        LevelSet? specs = null;
        float? xpGain = null;

        foreach (string key in OverlayKeys)
        {
            if (!TryGetProperty(root, key, out JProperty? prop) || prop?.Value == null
                || prop.Value.Type == JTokenType.Null)
            {
                continue;
            }

            if (!TryParseOverlay(key, prop.Value, playerCap, out int? max, out LevelSet? levels, out float? gain, out string? error))
            {
                warnings.Add(error ?? $"Ignoring {key}.");
                continue;
            }

            if (max.HasValue)
            {
                playerCap = max.Value;
            }

            if (MatchesPreset(key, max, levels, gain, fromPreset))
            {
                continue;
            }

            kept.Add(new JProperty(key, prop.Value.DeepClone()));
            maxOverride = max ?? maxOverride;
            if (key == KeySkillPointsPerPlayerLevel)
            {
                playerPoints = levels;
            }
            else if (key == KeySkillPointsPerSkillLevel)
            {
                skillPoints = levels;
            }
            else if (key == KeySpecializationLevels)
            {
                specs = levels;
            }

            xpGain = gain ?? xpGain;
        }

        List<JProperty> unknown = new();
        foreach (JProperty prop in root.Properties())
        {
            if (prop.Name.Equals(KeyPreset, StringComparison.OrdinalIgnoreCase) || IsOverlayKey(prop.Name))
            {
                continue;
            }

            unknown.Add(new JProperty(prop.Name, prop.Value.DeepClone()));
        }

        resolved = fromPreset.With(
            presetId: canonicalId,
            maxPlayerLevel: maxOverride,
            skillPointsPerPlayerLevel: playerPoints,
            skillPointsPerSkillLevel: skillPoints,
            specializationLevels: specs,
            xpGain: xpGain);

        JObject desired = new()
        {
            [KeyPreset] = canonicalId
        };
        foreach (JProperty prop in kept)
        {
            desired.Add(prop);
        }

        foreach (JProperty prop in unknown)
        {
            desired.Add(prop);
        }

        if (JToken.DeepEquals(root, desired))
        {
            return false;
        }

        root.RemoveAll();
        foreach (JProperty prop in desired.Properties())
        {
            root.Add(prop.Name, prop.Value);
        }

        return true;
    }

    public static bool TryParseOverlay(
        string key,
        JToken token,
        int playerCap,
        out int? maxPlayerLevel,
        out LevelSet? levels,
        out float? xpGain,
        out string? error)
    {
        maxPlayerLevel = null;
        levels = null;
        xpGain = null;
        error = null;

        if (key == KeyMaxPlayerLevel)
        {
            if (!TryReadInt(token, out int max) || max < XpCurves.PlayerMinLevel)
            {
                error = "maxPlayerLevel must be a whole number of at least 1.";
                return false;
            }

            maxPlayerLevel = max;
            return true;
        }

        if (key == KeyXpGain)
        {
            if (!TryReadFloat(token, out float gain) || gain < 0f || float.IsNaN(gain) || float.IsInfinity(gain))
            {
                error = "xpGain must be a number of at least 0.";
                return false;
            }

            xpGain = gain;
            return true;
        }

        if (key is KeySkillPointsPerPlayerLevel or KeySkillPointsPerSkillLevel or KeySpecializationLevels)
        {
            if (token.Type != JTokenType.String)
            {
                error = $"{key} must be a string such as \"1..50\" or \"20,40\".";
                return false;
            }

            string text = token.Value<string>() ?? "";
            int openEnd = key == KeySkillPointsPerSkillLevel ? XpCurves.SkillMaxLevel : playerCap;
            if (!LevelSet.TryParse(text, openEnd, out LevelSet set, out string? parseError))
            {
                error = $"{key}: {parseError}";
                return false;
            }

            levels = set;
            return true;
        }

        error = $"Unknown progression key '{key}'.";
        return false;
    }

    static bool MatchesPreset(
        string key,
        int? max,
        LevelSet? levels,
        float? xpGain,
        ProgressionProfile preset)
    {
        if (key == KeyMaxPlayerLevel)
        {
            return max == preset.MaxPlayerLevel;
        }

        if (key == KeyXpGain)
        {
            return xpGain.HasValue && Math.Abs(xpGain.Value - preset.XpGain) <= 0.0001f;
        }

        if (key == KeySkillPointsPerPlayerLevel)
        {
            return levels.HasValue && levels.Value == preset.SkillPointsPerPlayerLevel;
        }

        if (key == KeySkillPointsPerSkillLevel)
        {
            return levels.HasValue && levels.Value == preset.SkillPointsPerSkillLevel;
        }

        if (key == KeySpecializationLevels)
        {
            return levels.HasValue && levels.Value == preset.SpecializationLevels;
        }

        return false;
    }

    static string ReadPresetId(JObject root)
    {
        if (!TryGetProperty(root, KeyPreset, out JProperty? prop) || prop?.Value.Type != JTokenType.String)
        {
            return ProgressionProfile.MultiplayerPresetId;
        }

        string id = prop.Value.Value<string>()?.Trim() ?? "";
        return id.Length == 0 ? ProgressionProfile.MultiplayerPresetId : id;
    }

    static bool TryResolvePreset(
        IReadOnlyDictionary<string, ProgressionProfile> presets,
        string requested,
        ProgressionProfile baseline,
        out ProgressionProfile profile,
        out string canonicalId)
    {
        if (presets.TryGetValue(requested, out ProgressionProfile? found) && found != null)
        {
            profile = found;
            canonicalId = found.PresetId.Length > 0 ? found.PresetId : requested;
            return true;
        }

        profile = baseline;
        canonicalId = requested;
        return false;
    }

    static bool IsOverlayKey(string name)
    {
        foreach (string key in OverlayKeys)
        {
            if (name.Equals(key, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    static bool TryGetProperty(JObject root, string name, out JProperty? prop)
    {
        foreach (JProperty candidate in root.Properties())
        {
            if (candidate.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                prop = candidate;
                return true;
            }
        }

        prop = null;
        return false;
    }

    static bool TryReadInt(JToken token, out int value)
    {
        value = 0;
        if (token.Type == JTokenType.Integer)
        {
            value = token.Value<int>();
            return true;
        }

        if (token.Type == JTokenType.Float)
        {
            double number = token.Value<double>();
            if (double.IsFinite(number) && Math.Abs(number - Math.Round(number)) < 0.0001d)
            {
                value = (int)Math.Round(number);
                return true;
            }
        }

        return false;
    }

    static bool TryReadFloat(JToken token, out float value)
    {
        value = 0f;
        if (token.Type is JTokenType.Integer or JTokenType.Float)
        {
            double number = token.Value<double>();
            if (double.IsFinite(number))
            {
                value = (float)number;
                return true;
            }
        }

        return false;
    }
}
