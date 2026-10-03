using Newtonsoft.Json.Linq;

namespace Prosequor.Data;

/// <summary>
/// <c>ModConfig/prosequor/server.json</c>: every progression key, plus the preset those values were last aligned to.
/// A preset change rewrites values that still match the previous preset and keeps values that differ.
/// </summary>
public static class ServerProgressionConfig
{
    public const string ConfigFileName = "prosequor/server.json";
    public const string KeyPreset = "preset";
    public const string KeyAppliedPreset = "appliedPreset";
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
    /// Writes every progression key. Values that still match <c>appliedPreset</c> follow the selected preset.
    /// A missing <c>appliedPreset</c> treats keys already in the file as written for the selected preset.
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
        bool known = TryResolvePreset(presets, requested, baseline, out ProgressionProfile selected, out string canonicalId);
        if (!known)
        {
            warnings.Add(
                $"Unknown preset '{requested}'. Using the baseline table; other keys still apply.");
        }

        ProgressionProfile previous = selected;
        if (TryReadId(root, KeyAppliedPreset, out string appliedId))
        {
            TryResolvePreset(presets, appliedId, baseline, out previous, out _);
        }

        List<JProperty> unknown = new();
        foreach (JProperty prop in root.Properties())
        {
            if (prop.Name.Equals(KeyPreset, StringComparison.OrdinalIgnoreCase)
                || prop.Name.Equals(KeyAppliedPreset, StringComparison.OrdinalIgnoreCase)
                || IsOverlayKey(prop.Name))
            {
                continue;
            }

            unknown.Add(new JProperty(prop.Name, prop.Value.DeepClone()));
        }

        int playerCap = selected.MaxPlayerLevel;
        JToken maxToken = AlignInt(
            root,
            KeyMaxPlayerLevel,
            previous.MaxPlayerLevel,
            selected.MaxPlayerLevel,
            playerCap,
            warnings,
            out int resolvedMax);
        playerCap = resolvedMax;

        JToken playerPointsToken = AlignLevels(
            root,
            KeySkillPointsPerPlayerLevel,
            previous.SkillPointsPerPlayerLevel,
            selected.SkillPointsPerPlayerLevel,
            playerCap,
            warnings,
            out LevelSet playerPoints);
        JToken skillPointsToken = AlignLevels(
            root,
            KeySkillPointsPerSkillLevel,
            previous.SkillPointsPerSkillLevel,
            selected.SkillPointsPerSkillLevel,
            playerCap,
            warnings,
            out LevelSet skillPoints);
        JToken specsToken = AlignLevels(
            root,
            KeySpecializationLevels,
            previous.SpecializationLevels,
            selected.SpecializationLevels,
            playerCap,
            warnings,
            out LevelSet specs);
        JToken xpToken = AlignFloat(
            root,
            KeyXpGain,
            previous.XpGain,
            selected.XpGain,
            playerCap,
            warnings,
            out float xpGain);

        resolved = selected.With(
            presetId: canonicalId,
            maxPlayerLevel: resolvedMax,
            skillPointsPerPlayerLevel: playerPoints,
            skillPointsPerSkillLevel: skillPoints,
            specializationLevels: specs,
            xpGain: xpGain);

        JObject desired = new()
        {
            [KeyPreset] = canonicalId,
            [KeyMaxPlayerLevel] = maxToken,
            [KeySkillPointsPerPlayerLevel] = playerPointsToken,
            [KeySkillPointsPerSkillLevel] = skillPointsToken,
            [KeySpecializationLevels] = specsToken,
            [KeyXpGain] = xpToken,
            [KeyAppliedPreset] = canonicalId
        };
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

    static JToken AlignInt(
        JObject root,
        string key,
        int previous,
        int selected,
        int playerCap,
        IList<string> warnings,
        out int resolved)
    {
        if (!TryReadStored(root, key, playerCap, warnings, out JToken userToken, out int? max, out _, out _)
            || !max.HasValue
            || max.Value == previous)
        {
            resolved = selected;
            return new JValue(selected);
        }

        resolved = max.Value;
        return userToken;
    }

    static JToken AlignLevels(
        JObject root,
        string key,
        LevelSet previous,
        LevelSet selected,
        int playerCap,
        IList<string> warnings,
        out LevelSet resolved)
    {
        if (!TryReadStored(root, key, playerCap, warnings, out JToken userToken, out _, out LevelSet? levels, out _)
            || !levels.HasValue
            || levels.Value == previous)
        {
            resolved = selected;
            return new JValue(selected.Format());
        }

        resolved = levels.Value;
        return userToken;
    }

    static JToken AlignFloat(
        JObject root,
        string key,
        float previous,
        float selected,
        int playerCap,
        IList<string> warnings,
        out float resolved)
    {
        if (!TryReadStored(root, key, playerCap, warnings, out JToken userToken, out _, out _, out float? gain)
            || !gain.HasValue
            || Math.Abs(gain.Value - previous) <= 0.0001f)
        {
            resolved = selected;
            return NumberToken(selected);
        }

        resolved = gain.Value;
        return userToken;
    }

    static bool TryReadStored(
        JObject root,
        string key,
        int playerCap,
        IList<string> warnings,
        out JToken userToken,
        out int? max,
        out LevelSet? levels,
        out float? gain)
    {
        userToken = JValue.CreateNull();
        max = null;
        levels = null;
        gain = null;
        if (!TryGetProperty(root, key, out JProperty? prop) || prop?.Value == null
            || prop.Value.Type == JTokenType.Null)
        {
            return false;
        }

        if (!TryParseOverlay(key, prop.Value, playerCap, out max, out levels, out gain, out string? error))
        {
            warnings.Add(error ?? $"Ignoring {key}.");
            return false;
        }

        userToken = prop.Value.DeepClone();
        return true;
    }

    static JToken NumberToken(float value)
    {
        double rounded = Math.Round(value);
        if (Math.Abs(value - rounded) <= 0.0001d
            && rounded >= int.MinValue
            && rounded <= int.MaxValue)
        {
            return new JValue((int)rounded);
        }

        return new JValue(value);
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

    static bool TryReadId(JObject root, string key, out string id)
    {
        id = "";
        if (!TryGetProperty(root, key, out JProperty? prop) || prop?.Value.Type != JTokenType.String)
        {
            return false;
        }

        id = prop.Value.Value<string>()?.Trim() ?? "";
        return id.Length > 0;
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
