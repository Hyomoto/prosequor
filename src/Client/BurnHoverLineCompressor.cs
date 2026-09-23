using Vintagestory.API.Config;

namespace Prosequor.Client;

/// <summary>
/// Collapses consecutive vanilla burn temp + duration hover lines into one sentence.
/// </summary>
public static class BurnHoverLineCompressor
{
    const string BurnTemperatureKey = "Burn temperature: {0}°C";
    const string BurnDurationKey = "Burn duration: {0}s";
    const string CompressedKey = "prosequor:tooltip-burns-for";
    const string CompressedFallback = "Burns for {0}s at {1}°C";

    static string? cachedLocale;
    static string? cachedTempTemplate;
    static string? cachedDurationTemplate;

    /// <summary>
    /// When both burn lines are present, replace them with a single localized line at the
    /// earlier of the two positions. Leaves other lines (smelt, flavor, etc.) alone.
    /// </summary>
    public static string Compress(string? desc)
    {
        if (string.IsNullOrEmpty(desc))
        {
            return desc ?? "";
        }

        EnsureTemplates(out string tempTemplate, out string durationTemplate);

        string[] lines = desc.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        int tempIdx = -1;
        int durationIdx = -1;
        string? temperature = null;
        string? duration = null;

        for (int i = 0; i < lines.Length; i++)
        {
            string plain = HoverStatLineStripper.StripVtml(lines[i].TrimEnd());
            if (tempIdx < 0
                && HoverStatLineStripper.TryExtract(plain, tempTemplate, out string[] tempCaps)
                && tempCaps.Length > 0
                && !string.IsNullOrWhiteSpace(tempCaps[0]))
            {
                tempIdx = i;
                temperature = tempCaps[0].Trim();
                continue;
            }

            if (durationIdx < 0
                && HoverStatLineStripper.TryExtract(plain, durationTemplate, out string[] durCaps)
                && durCaps.Length > 0
                && !string.IsNullOrWhiteSpace(durCaps[0]))
            {
                durationIdx = i;
                duration = durCaps[0].Trim();
            }
        }

        if (tempIdx < 0 || durationIdx < 0 || temperature == null || duration == null)
        {
            return desc;
        }

        string combined = FormatCombined(duration, temperature);
        int insertAt = Math.Min(tempIdx, durationIdx);
        var kept = new List<string>(lines.Length);
        for (int i = 0; i < lines.Length; i++)
        {
            if (i == tempIdx || i == durationIdx)
            {
                if (i == insertAt)
                {
                    kept.Add(combined);
                }

                continue;
            }

            kept.Add(lines[i]);
        }

        return HoverStatLineStripper.JoinCollapsingBlanks(kept);
    }

    static string FormatCombined(string duration, string temperature)
    {
        try
        {
            string localized = Lang.Get(CompressedKey, duration, temperature);
            if (!string.IsNullOrWhiteSpace(localized)
                && !string.Equals(localized, CompressedKey, StringComparison.Ordinal))
            {
                return localized;
            }
        }
        catch
        {
            // Pure fixtures / missing lang table.
        }

        return string.Format(
            System.Globalization.CultureInfo.InvariantCulture,
            CompressedFallback,
            duration,
            temperature);
    }

    static void EnsureTemplates(out string tempTemplate, out string durationTemplate)
    {
        string locale;
        try
        {
            locale = Lang.CurrentLocale ?? "en";
        }
        catch
        {
            locale = "en";
        }

        if (!string.Equals(cachedLocale, locale, StringComparison.Ordinal)
            || cachedTempTemplate == null
            || cachedDurationTemplate == null)
        {
            cachedLocale = locale;
            cachedTempTemplate = ResolveTemplate(BurnTemperatureKey);
            cachedDurationTemplate = ResolveTemplate(BurnDurationKey);
        }

        tempTemplate = cachedTempTemplate;
        durationTemplate = cachedDurationTemplate;
    }

    static string ResolveTemplate(string key)
    {
        try
        {
            string? unformatted = Lang.GetUnformatted(key);
            if (!string.IsNullOrWhiteSpace(unformatted)
                && !string.Equals(unformatted, key, StringComparison.Ordinal))
            {
                return unformatted.TrimEnd();
            }

            if (!string.IsNullOrWhiteSpace(unformatted) && unformatted.IndexOf('{') >= 0)
            {
                return unformatted.TrimEnd();
            }

            if (!string.IsNullOrWhiteSpace(unformatted))
            {
                return unformatted.TrimEnd();
            }
        }
        catch
        {
            // Pure fixtures may lack a loaded lang table.
        }

        return key;
    }
}
