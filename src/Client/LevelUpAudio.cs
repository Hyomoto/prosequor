using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace Prosequor.Client;

/// <summary>
/// Clip gain for skill and player level-up stingers. 0–100, default 50.
/// Passed straight to <c>Gui.PlaySound</c>.
/// </summary>
public static class LevelUpAudio
{
    public const string ConfigFileName = "prosequor/client.json";
    public const string VolumeProperty = "LevelUpVolume";
    public const int DefaultPercent = 50;
    public const int MinPercent = 0;
    public const int MaxPercent = 100;

    public static int ClampPercent(int percent) => Math.Clamp(percent, MinPercent, MaxPercent);

    public static float ToGain(int percent) => ClampPercent(percent) / 100f;

    /// <summary>
    /// Reads <see cref="VolumeProperty"/>. Missing or unreadable values become
    /// <see cref="DefaultPercent"/>. Out-of-range numbers are clamped.
    /// Returns true when <paramref name="root"/> was changed.
    /// </summary>
    public static bool Normalize(JObject root, out int percent)
    {
        JToken? token = root[VolumeProperty];
        if (token == null || token.Type == JTokenType.Null)
        {
            percent = DefaultPercent;
            root[VolumeProperty] = percent;
            return true;
        }

        if (token.Type is JTokenType.Integer or JTokenType.Float)
        {
            double value = token.Value<double>();
            if (double.IsFinite(value))
            {
                percent = ClampPercent((int)Math.Round(value, MidpointRounding.AwayFromZero));
                if (token.Type == JTokenType.Integer && token.Value<long>() == percent)
                {
                    return false;
                }

                root[VolumeProperty] = percent;
                return true;
            }
        }

        percent = DefaultPercent;
        root[VolumeProperty] = percent;
        return true;
    }

    /// <summary>Gain for <c>Gui.PlaySound</c>. Creates the client file when it is missing.</summary>
    public static float ReadGain(ICoreAPI api)
    {
        try
        {
            JsonObject? loaded = api.LoadModConfig(ConfigFileName);
            if (loaded != null && loaded.Token is not JObject)
            {
                api.Logger.Warning(
                    "[prosequor] {0} is not a JSON object. Using level-up volume {1}.",
                    ConfigFileName,
                    DefaultPercent);
                return ToGain(DefaultPercent);
            }

            JObject root = loaded?.Token as JObject ?? new JObject();
            bool dirty = Normalize(root, out int percent);
            if (loaded == null)
            {
                api.StoreModConfig(new ClientAudioConfig { LevelUpVolume = percent }, ConfigFileName);
            }
            else if (dirty)
            {
                api.StoreModConfig(new JsonObject(root), ConfigFileName);
            }

            return ToGain(percent);
        }
        catch (Exception ex)
        {
            api.Logger.Warning(
                "[prosequor] Could not read {0}: {1}",
                ConfigFileName,
                ex.Message);
            return ToGain(DefaultPercent);
        }
    }
}

/// <summary>Shape of <c>ModConfig/prosequor/client.json</c>.</summary>
public sealed class ClientAudioConfig
{
    public int LevelUpVolume { get; set; } = LevelUpAudio.DefaultPercent;
}
