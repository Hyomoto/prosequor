using System.Text;
using System.Text.RegularExpressions;

namespace Prosequor.Client;

/// <summary>
/// Drops hover description lines that match localized lang templates (values are wildcards).
/// Used to absorb vanilla <c>GetHeldItemInfo</c> sentences whose facts now live in the stats band.
/// </summary>
public static class HoverStatLineStripper
{
    static readonly Regex PlaceholderRegex = new(@"\{(\d+)\}", RegexOptions.Compiled);
    static readonly Regex VtmlTagRegex = new(@"</?[^>]+>", RegexOptions.Compiled);

    /// <summary>
    /// Remove lines that match any of <paramref name="templates"/>. Templates use
    /// <c>{0}</c>/<c>{1}</c> wildcards; static infixes (e.g. <c> / </c>) must still appear.
    /// </summary>
    public static string Strip(string? desc, IReadOnlyList<string>? templates)
    {
        if (string.IsNullOrEmpty(desc) || templates == null || templates.Count == 0)
        {
            return desc ?? "";
        }

        List<Regex> matchers = new(templates.Count);
        for (int i = 0; i < templates.Count; i++)
        {
            string? template = templates[i];
            if (string.IsNullOrWhiteSpace(template))
            {
                continue;
            }

            matchers.Add(Compile(template.TrimEnd(), capturing: false));
        }

        if (matchers.Count == 0)
        {
            return desc;
        }

        string[] lines = desc.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var kept = new List<string>(lines.Length);
        foreach (string line in lines)
        {
            string plain = StripVtml(line.TrimEnd());
            if (MatchesAny(plain, matchers))
            {
                continue;
            }

            kept.Add(line);
        }

        return JoinCollapsingBlanks(kept);
    }

    /// <summary>True when <paramref name="line"/> matches a single unformatted lang template.</summary>
    public static bool MatchesTemplate(string? line, string? template)
    {
        if (string.IsNullOrEmpty(line) || string.IsNullOrWhiteSpace(template))
        {
            return false;
        }

        return Compile(template.TrimEnd(), capturing: false).IsMatch(StripVtml(line.TrimEnd()));
    }

    /// <summary>
    /// Match <paramref name="line"/> against a template and return placeholder captures in appearance order.
    /// </summary>
    public static bool TryExtract(string? line, string? template, out string[] values)
    {
        values = Array.Empty<string>();
        if (string.IsNullOrEmpty(line) || string.IsNullOrWhiteSpace(template))
        {
            return false;
        }

        Match match = Compile(template.TrimEnd(), capturing: true).Match(StripVtml(line.TrimEnd()));
        if (!match.Success || match.Groups.Count <= 1)
        {
            return false;
        }

        values = new string[match.Groups.Count - 1];
        for (int i = 1; i < match.Groups.Count; i++)
        {
            values[i - 1] = match.Groups[i].Value;
        }

        return true;
    }

    public static string StripVtml(string text)
    {
        if (string.IsNullOrEmpty(text) || text.IndexOf('<') < 0)
        {
            return text;
        }

        return VtmlTagRegex.Replace(text, "");
    }

    /// <summary>Join kept lines, collapsing runs of blank lines to a single blank.</summary>
    internal static string JoinCollapsingBlanks(List<string> kept)
    {
        var sb = new StringBuilder();
        bool pendingBlank = false;
        for (int i = 0; i < kept.Count; i++)
        {
            string line = kept[i];
            bool blank = string.IsNullOrWhiteSpace(line);
            if (blank)
            {
                pendingBlank = sb.Length > 0;
                continue;
            }

            if (pendingBlank)
            {
                sb.Append('\n');
                pendingBlank = false;
            }

            if (sb.Length > 0)
            {
                sb.Append('\n');
            }

            sb.Append(line);
        }

        return sb.ToString();
    }

    static bool MatchesAny(string plain, List<Regex> matchers)
    {
        for (int i = 0; i < matchers.Count; i++)
        {
            if (matchers[i].IsMatch(plain))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Turn a lang template into an anchored regex: static text is literal, <c>{n}</c> is a wildcard.
    /// When <paramref name="capturing"/> is true, each placeholder becomes a capture group.
    /// </summary>
    internal static Regex Compile(string template, bool capturing = false)
    {
        string wildcard = capturing ? "(.*?)" : ".*?";
        var sb = new StringBuilder("^");
        int cursor = 0;
        foreach (Match match in PlaceholderRegex.Matches(template))
        {
            sb.Append(Regex.Escape(template[cursor..match.Index]));
            sb.Append(wildcard);
            cursor = match.Index + match.Length;
        }

        sb.Append(Regex.Escape(template[cursor..]));
        sb.Append('$');
        return new Regex(sb.ToString(), RegexOptions.CultureInvariant | RegexOptions.Singleline);
    }
}
