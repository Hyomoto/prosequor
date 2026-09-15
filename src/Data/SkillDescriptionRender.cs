using System.Globalization;
using System.Text;
using Vintagestory.API.Config;

namespace Prosequor.Data;

/// <summary>Formats skill / unlock description args for list (plain) and tree (VTML) tooltips.</summary>
public static class SkillDescriptionRender
{
    public static string RenderLang(string descriptionLang, IReadOnlyList<object> args, bool vtml)
    {
        if (string.IsNullOrWhiteSpace(descriptionLang))
        {
            return "";
        }

        object[] argArray = args.ToArray();
        Dictionary<string, string> replacements = new();
        for (int i = 0; i < argArray.Length; i++)
        {
            if (argArray[i] is not FormattedDescriptionArg formatted)
            {
                continue;
            }

            string marker = $"\uE000{i}\uE001";
            argArray[i] = marker;
            replacements[marker] = vtml
                ? FormatPercentVtml(formatted)
                : FormatPercentPlain(formatted);
        }

        string rendered = LangKey(descriptionLang, argArray);
        if (vtml)
        {
            rendered = EscapeVtml(rendered);
        }

        foreach (KeyValuePair<string, string> replacement in replacements)
        {
            rendered = rendered.Replace(replacement.Key, replacement.Value, StringComparison.Ordinal);
        }

        return rendered;
    }

    public static string FormatPercentPlain(FormattedDescriptionArg arg)
    {
        decimal value = PercentPoints(arg);
        return DisplayMagnitude(value).ToString("0.##", CultureInfo.CurrentCulture) + "%";
    }

    public static string FormatPercentVtml(FormattedDescriptionArg arg)
    {
        decimal value = PercentPoints(arg);
        string number = DisplayMagnitude(value).ToString("0.##", CultureInfo.CurrentCulture);
        string key = value < 0m ? "format-percent-negative" : "format-percent";
        return LangKey(key, EscapeVtml(number));
    }

    /// <summary>Percent points before sign. Negative values drop the minus and use the red wrap.</summary>
    static decimal PercentPoints(FormattedDescriptionArg arg) =>
        arg.Format == "fractionPercent" ? arg.Value * 100m : arg.Value;

    static decimal DisplayMagnitude(decimal value) => value < 0m ? -value : value;

    public static string EscapeVtml(string text) =>
        text.Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal);

    public static string LangKey(string key, params object[] args)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return "";
        }

        string full = key.Contains(':') ? key : "prosequor:" + key;
        string translated = args.Length == 0 ? Lang.Get(full) : Lang.Get(full, args);
        return string.IsNullOrEmpty(translated) || string.Equals(translated, full, StringComparison.Ordinal)
            ? key
            : translated;
    }
}
