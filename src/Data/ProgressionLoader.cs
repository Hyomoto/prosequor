using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace Prosequor.Data;

/// <summary>
/// Loads <c>progression.json</c>, preset files, and the sparse server config.
/// </summary>
public static class ProgressionLoader
{
    public const string BaselinePath = "config/prosequor/progression";
    public const string PresetPath = "config/prosequor/presets/";

    public static ProgressionProfile LoadBaseline(ICoreAPI api)
    {
        List<KeyValuePair<AssetLocation, JObject>> assets = api.Assets
            .GetMany<JObject>(api.Logger, BaselinePath, null)
            .OrderBy(kv => kv.Key.Domain, StringComparer.OrdinalIgnoreCase)
            .ThenBy(kv => kv.Key.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        ProgressionProfile profile = ProgressionProfile.Baseline;
        bool any = false;
        foreach (KeyValuePair<AssetLocation, JObject> kv in assets)
        {
            if (kv.Value == null)
            {
                continue;
            }

            if (!TryReadProfile(kv.Value, ProgressionProfile.Baseline, out ProgressionProfile next, out string? error))
            {
                api.Logger.Warning(
                    "[prosequor] Skipping progression file {0}: {1}",
                    kv.Key,
                    error);
                continue;
            }

            if (any)
            {
                api.Logger.Warning(
                    "[prosequor] Progression table redefined by {0}; last-win.",
                    kv.Key);
            }

            profile = next.With(presetId: ProgressionProfile.MultiplayerPresetId);
            any = true;
        }

        if (!any)
        {
            api.Logger.Warning(
                "[prosequor] No progression.json found. Using the built-in table.");
        }

        return profile;
    }

    public static Dictionary<string, ProgressionProfile> LoadPresets(ICoreAPI api, ProgressionProfile baseline)
    {
        Dictionary<string, ProgressionProfile> presets = new(StringComparer.OrdinalIgnoreCase);

        List<KeyValuePair<AssetLocation, JObject>> assets = api.Assets
            .GetMany<JObject>(api.Logger, PresetPath, null)
            .OrderBy(kv => kv.Key.Domain, StringComparer.OrdinalIgnoreCase)
            .ThenBy(kv => kv.Key.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (KeyValuePair<AssetLocation, JObject> kv in assets)
        {
            if (kv.Value == null)
            {
                continue;
            }

            AddPreset(api, presets, baseline, kv.Value, FileStem(kv.Key.Path), kv.Key.ToString());
        }

        string dir = Path.Combine(api.GetOrCreateDataPath("ModConfig"), "prosequor", "presets");
        if (Directory.Exists(dir))
        {
            foreach (string file in Directory.GetFiles(dir, "*.json").OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                JObject? obj;
                try
                {
                    obj = JObject.Parse(File.ReadAllText(file));
                }
                catch (Exception ex)
                {
                    api.Logger.Warning(
                        "[prosequor] Skipping preset file {0}: {1}",
                        file,
                        ex.Message);
                    continue;
                }

                AddPreset(api, presets, baseline, obj, Path.GetFileNameWithoutExtension(file), file);
            }
        }

        if (!presets.ContainsKey(ProgressionProfile.MultiplayerPresetId))
        {
            presets[ProgressionProfile.MultiplayerPresetId] =
                baseline.With(presetId: ProgressionProfile.MultiplayerPresetId);
        }

        return presets;
    }

    /// <summary>
    /// Reads <c>prosequor/server.json</c>, keeps only overrides that differ from the preset, and stores the file when it changes.
    /// </summary>
    public static ProgressionProfile ApplyServerSelection(
        ICoreAPI api,
        ProgressionProfile baseline,
        IReadOnlyDictionary<string, ProgressionProfile> presets)
    {
        bool missing = false;
        JObject root;
        try
        {
            JsonObject? loaded = api.LoadModConfig(ServerProgressionConfig.ConfigFileName);
            if (loaded == null)
            {
                missing = true;
                root = new JObject();
            }
            else if (loaded.Token is not JObject obj)
            {
                api.Logger.Warning(
                    "[prosequor] {0} is not a JSON object. Replacing it with the multiplayer preset.",
                    ServerProgressionConfig.ConfigFileName);
                missing = true;
                root = new JObject();
            }
            else
            {
                root = obj;
            }
        }
        catch (Exception ex)
        {
            api.Logger.Warning(
                "[prosequor] Could not read {0}: {1}",
                ServerProgressionConfig.ConfigFileName,
                ex.Message);
            return baseline.With(presetId: ProgressionProfile.MultiplayerPresetId);
        }

        List<string> warnings = new();
        bool dirty = ServerProgressionConfig.Normalize(root, presets, baseline, out ProgressionProfile resolved, warnings);
        foreach (string warning in warnings)
        {
            api.Logger.Warning("[prosequor] {0}", warning);
        }

        if (missing || dirty)
        {
            try
            {
                api.StoreModConfig(new JsonObject(root), ServerProgressionConfig.ConfigFileName);
            }
            catch (Exception ex)
            {
                api.Logger.Warning(
                    "[prosequor] Could not write {0}: {1}",
                    ServerProgressionConfig.ConfigFileName,
                    ex.Message);
            }
        }

        api.Logger.Notification(
            "[prosequor] Progression preset {0}, xp gain {1}, player cap {2}.",
            resolved.PresetId,
            resolved.XpGain,
            resolved.MaxPlayerLevel);
        return resolved;
    }

    static void AddPreset(
        ICoreAPI api,
        Dictionary<string, ProgressionProfile> presets,
        ProgressionProfile baseline,
        JObject obj,
        string fileStem,
        string source)
    {
        string id = obj["id"]?.Type == JTokenType.String
            ? obj["id"]!.Value<string>()?.Trim() ?? ""
            : "";
        if (id.Length == 0)
        {
            id = fileStem;
        }

        if (id.Length == 0)
        {
            api.Logger.Warning("[prosequor] Skipping preset {0}: missing id.", source);
            return;
        }

        if (!TryReadProfile(obj, baseline, out ProgressionProfile profile, out string? error))
        {
            api.Logger.Warning("[prosequor] Skipping preset {0}: {1}", source, error);
            return;
        }

        if (presets.ContainsKey(id))
        {
            api.Logger.Warning(
                "[prosequor] Preset '{0}' redefined by {1}; last-win.",
                id,
                source);
        }

        presets[id] = profile.With(presetId: id);
    }

    /// <summary>
    /// Overlay <paramref name="obj"/> onto <paramref name="baseline"/>. Missing keys stay on the baseline.
    /// </summary>
    public static bool TryReadProfile(
        JObject obj,
        ProgressionProfile baseline,
        out ProgressionProfile profile,
        out string? error)
    {
        profile = baseline;
        error = null;
        int playerCap = baseline.MaxPlayerLevel;
        int? max = null;
        LevelSet? playerPoints = null;
        LevelSet? skillPoints = null;
        LevelSet? specs = null;
        float? xpGain = null;

        foreach (string key in ServerProgressionConfig.OverlayKeys)
        {
            JToken? token = obj[key];
            if (token == null || token.Type == JTokenType.Null)
            {
                continue;
            }

            if (!ServerProgressionConfig.TryParseOverlay(key, token, playerCap, out int? parsedMax, out LevelSet? levels, out float? gain, out string? parseError))
            {
                error = parseError;
                return false;
            }

            if (parsedMax.HasValue)
            {
                playerCap = parsedMax.Value;
            }

            max = parsedMax ?? max;
            xpGain = gain ?? xpGain;
            if (key == ServerProgressionConfig.KeySkillPointsPerPlayerLevel)
            {
                playerPoints = levels;
            }
            else if (key == ServerProgressionConfig.KeySkillPointsPerSkillLevel)
            {
                skillPoints = levels;
            }
            else if (key == ServerProgressionConfig.KeySpecializationLevels)
            {
                specs = levels;
            }
        }

        profile = baseline.With(
            maxPlayerLevel: max,
            skillPointsPerPlayerLevel: playerPoints,
            skillPointsPerSkillLevel: skillPoints,
            specializationLevels: specs,
            xpGain: xpGain);
        return true;
    }

    static string FileStem(string path)
    {
        int slash = path.LastIndexOf('/');
        string name = slash >= 0 ? path[(slash + 1)..] : path;
        if (name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            name = name[..^5];
        }

        return name;
    }
}
