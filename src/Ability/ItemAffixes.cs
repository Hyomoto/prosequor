using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;

namespace Prosequor.Ability;

/// <summary>
/// One stamped affix: plain lang key + optional color (composed at tooltip time).
/// When <see cref="Color"/> is null, <see cref="LangKey"/> may already contain VTML.
/// </summary>
public readonly record struct ItemAffixEntry(string Code, string LangKey, string? Color);

/// <summary>
/// Ordered affix bag under <c>prosequorAffixes</c>. Stack tree is the display cache;
/// pedigree blob is the unit of record. Never write type-level <see cref="ItemStack.ItemAttributes"/>.
/// </summary>
public static class ItemAffixes
{
    public const string TreeAttr = "prosequorAffixes";

    /// <summary>Stable stamp code for the leading craft-quality grade affix.</summary>
    public const string QualityCode = "quality";

    const string CountKey = "n";
    const string CodeKey = "code";
    const string LangKey = "lang";
    const string ColorKey = "color";

    public static IReadOnlyList<ItemAffixEntry> GetAll(ItemStack? stack) =>
        stack?.Attributes == null
            ? Array.Empty<ItemAffixEntry>()
            : ReadAll(stack.Attributes.GetTreeAttribute(TreeAttr));

    public static IReadOnlyList<ItemAffixEntry> ReadAll(ITreeAttribute? tree)
    {
        if (tree == null)
        {
            return Array.Empty<ItemAffixEntry>();
        }

        int n = tree.GetInt(CountKey, 0);
        if (n <= 0)
        {
            return Array.Empty<ItemAffixEntry>();
        }

        List<ItemAffixEntry> list = new(n);
        for (int i = 0; i < n; i++)
        {
            ITreeAttribute? entry = tree.GetTreeAttribute(i.ToString());
            if (entry == null)
            {
                continue;
            }

            string code = entry.GetString(CodeKey, "")?.Trim() ?? "";
            string lang = entry.GetString(LangKey, "")?.Trim() ?? "";
            if (code.Length == 0 || lang.Length == 0)
            {
                continue;
            }

            string? color = entry.GetString(ColorKey, null)?.Trim();
            if (string.IsNullOrWhiteSpace(color))
            {
                color = null;
            }

            list.Add(new ItemAffixEntry(code, lang, color));
        }

        return list;
    }

    /// <summary>Replace the bag (empty removes the tree). Does not stamp pedigree.</summary>
    public static void WriteAll(ItemStack? stack, IReadOnlyList<ItemAffixEntry>? affixes)
    {
        if (stack?.Attributes == null)
        {
            return;
        }

        if (affixes == null || affixes.Count == 0)
        {
            if (stack.Attributes.HasAttribute(TreeAttr))
            {
                stack.Attributes.RemoveAttribute(TreeAttr);
            }

            return;
        }

        WriteAll(stack.Attributes.GetOrAddTreeAttribute(TreeAttr), affixes);
    }

    public static void WriteAll(ITreeAttribute tree, IReadOnlyList<ItemAffixEntry> affixes)
    {
        int oldN = tree.GetInt(CountKey, 0);
        for (int i = 0; i < oldN; i++)
        {
            tree.RemoveAttribute(i.ToString());
        }

        int written = 0;
        for (int i = 0; i < affixes.Count; i++)
        {
            ItemAffixEntry e = affixes[i];
            if (string.IsNullOrWhiteSpace(e.Code) || string.IsNullOrWhiteSpace(e.LangKey))
            {
                continue;
            }

            WriteEntry(tree, written, e.Code.Trim(), e.LangKey.Trim(), e.Color);
            written++;
        }

        tree.SetInt(CountKey, written);
    }

    /// <summary>
    /// Appends an affix. Same <paramref name="code"/> is a no-op (keeps first stamp).
    /// </summary>
    public static bool Add(ItemStack? stack, string code, string langKey, string? color = null)
    {
        if (stack?.Attributes == null
            || string.IsNullOrWhiteSpace(code)
            || string.IsNullOrWhiteSpace(langKey))
        {
            return false;
        }

        string trimmedCode = code.Trim();
        string trimmedLang = langKey.Trim();
        string? trimmedColor = string.IsNullOrWhiteSpace(color) ? null : color.Trim();

        foreach (ItemAffixEntry existing in GetAll(stack))
        {
            if (string.Equals(existing.Code, trimmedCode, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        ITreeAttribute tree = stack.Attributes.GetOrAddTreeAttribute(TreeAttr);
        int n = tree.GetInt(CountKey, 0);
        ITreeAttribute entry = tree.GetOrAddTreeAttribute(n.ToString());
        entry.SetString(CodeKey, trimmedCode);
        entry.SetString(LangKey, trimmedLang);
        if (trimmedColor != null)
        {
            entry.SetString(ColorKey, trimmedColor);
        }

        tree.SetInt(CountKey, n + 1);
        ProsequorStackPedigree.StampSurfaceFromStack(stack);
        return true;
    }

    /// <summary>
    /// Inserts or replaces an affix at the front of the bag (e.g. craft quality grade).
    /// Same <paramref name="code"/> is removed first so only one leading entry exists.
    /// </summary>
    public static bool SetFront(ItemStack? stack, string code, string langKey, string? color = null)
    {
        if (stack?.Attributes == null
            || string.IsNullOrWhiteSpace(code)
            || string.IsNullOrWhiteSpace(langKey))
        {
            return false;
        }

        string trimmedCode = code.Trim();
        string trimmedLang = langKey.Trim();
        string? trimmedColor = string.IsNullOrWhiteSpace(color) ? null : color.Trim();

        List<ItemAffixEntry> rest = new();
        foreach (ItemAffixEntry existing in GetAll(stack))
        {
            if (string.Equals(existing.Code, trimmedCode, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            rest.Add(existing);
        }

        ITreeAttribute tree = stack.Attributes.GetOrAddTreeAttribute(TreeAttr);
        // Clear old numbered entries.
        int oldN = tree.GetInt(CountKey, 0);
        for (int i = 0; i < oldN; i++)
        {
            tree.RemoveAttribute(i.ToString());
        }

        WriteEntry(tree, 0, trimmedCode, trimmedLang, trimmedColor);
        for (int i = 0; i < rest.Count; i++)
        {
            ItemAffixEntry e = rest[i];
            WriteEntry(tree, i + 1, e.Code, e.LangKey, e.Color);
        }

        tree.SetInt(CountKey, rest.Count + 1);
        ProsequorStackPedigree.StampSurfaceFromStack(stack);
        return true;
    }

    static void WriteEntry(
        ITreeAttribute tree,
        int index,
        string code,
        string lang,
        string? color)
    {
        ITreeAttribute entry = tree.GetOrAddTreeAttribute(index.ToString());
        entry.SetString(CodeKey, code);
        entry.SetString(LangKey, lang);
        if (color != null)
        {
            entry.SetString(ColorKey, color);
        }
        else
        {
            entry.RemoveAttribute(ColorKey);
        }
    }

    /// <summary>
    /// Affix bag without the leading craft-quality grade (<see cref="QualityCode"/>).
    /// Distillate keeps other stamps (intoxication / value) but never the <c>Quality:</c> footer.
    /// </summary>
    public static IReadOnlyList<ItemAffixEntry> WithoutQuality(IReadOnlyList<ItemAffixEntry>? affixes)
    {
        if (affixes == null || affixes.Count == 0)
        {
            return Array.Empty<ItemAffixEntry>();
        }

        List<ItemAffixEntry> list = new(affixes.Count);
        for (int i = 0; i < affixes.Count; i++)
        {
            ItemAffixEntry affix = affixes[i];
            if (string.Equals(affix.Code, QualityCode, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            list.Add(affix);
        }

        return list.Count == 0 ? Array.Empty<ItemAffixEntry>() : list;
    }

    /// <summary>
    /// Leading craft-quality grade (<see cref="QualityCode"/>), if stamped.
    /// </summary>
    public static bool TryGetQuality(ItemStack? stack, out ItemAffixEntry entry)
    {
        foreach (ItemAffixEntry affix in GetAll(stack))
        {
            if (string.Equals(affix.Code, QualityCode, StringComparison.OrdinalIgnoreCase))
            {
                entry = affix;
                return true;
            }
        }

        entry = default;
        return false;
    }

    /// <summary>
    /// Comma-joined colored affix names for the tooltip header, or null when empty.
    /// Skips the craft-quality grade (<see cref="QualityCode"/>) — that belongs in the
    /// footer via <see cref="AppendQualityFooter"/>.
    /// Optional <paramref name="fontFace"/> / <paramref name="fontSize"/> are applied on each
    /// name span so nested color tags still override a decorative title font (VTML does not
    /// inherit <c>face</c> across nested <c>&lt;font&gt;</c>).
    /// </summary>
    public static string? FormatHeaderNames(
        ItemStack? stack,
        string? fontFace = null,
        string? fontSize = null)
    {
        IReadOnlyList<ItemAffixEntry> affixes = GetAll(stack);
        if (affixes.Count == 0)
        {
            return null;
        }

        bool wrapFont = !string.IsNullOrWhiteSpace(fontFace)
            || !string.IsNullOrWhiteSpace(fontSize);

        StringBuilder names = new();
        int written = 0;
        for (int i = 0; i < affixes.Count; i++)
        {
            ItemAffixEntry affix = affixes[i];
            if (string.Equals(affix.Code, QualityCode, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (written > 0)
            {
                names.Append(", ");
            }

            string name = ResolveLocalizedName(affix);
            bool hasColor = !string.IsNullOrWhiteSpace(affix.Color);
            if (!hasColor && !wrapFont)
            {
                names.Append(name);
                written++;
                continue;
            }

            names.Append("<font");
            if (!string.IsNullOrWhiteSpace(fontFace))
            {
                names.Append(" face=\"").Append(fontFace).Append('"');
            }

            if (!string.IsNullOrWhiteSpace(fontSize))
            {
                names.Append(" size=\"").Append(fontSize).Append('"');
            }

            if (hasColor)
            {
                names.Append(" color=\"").Append(affix.Color).Append('"');
            }

            names.Append('>').Append(name).Append("</font>");
            written++;
        }

        return written == 0 ? null : names.ToString();
    }

    /// <summary>
    /// Muted italic <c>Quality:</c> label + rarity-colored grade name (footer, above Created By).
    /// </summary>
    public static bool TryFormatQualityFooter(ItemStack? stack, out string styled)
    {
        styled = "";
        if (!TryGetQuality(stack, out ItemAffixEntry quality))
        {
            return false;
        }

        string grade = ResolveLocalizedName(quality);
        if (string.IsNullOrWhiteSpace(grade))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(quality.Color)
            && grade.IndexOf("<font", StringComparison.OrdinalIgnoreCase) < 0)
        {
            grade = "<font color=\"" + quality.Color + "\">"
                + EscapeVtml(grade)
                + "</font>";
        }

        string label = TryGetLang("prosequor:quality-label");
        if (string.IsNullOrWhiteSpace(label) || label == "prosequor:quality-label")
        {
            label = "Quality:";
        }

        styled = "<font color=\"" + OwnerCredit.MutedColor + "\"><i>"
            + EscapeVtml(label.TrimEnd() + " ")
            + "</i></font>"
            + grade;
        return true;
    }

    /// <summary>
    /// Append quality footer once onto a tooltip body. No-op when no quality stamp.
    /// </summary>
    public static string AppendQualityFooter(string? body, ItemStack? stack)
    {
        if (!TryFormatQualityFooter(stack, out string styled))
        {
            return body ?? "";
        }

        if (string.IsNullOrEmpty(body))
        {
            return styled;
        }

        if (body.IndexOf(styled, StringComparison.Ordinal) >= 0)
        {
            return body;
        }

        return body + "\n" + styled;
    }

    /// <summary>
    /// Composes a single labeled tooltip line, or null when the bag is empty
    /// (quality-only bags yield null — quality uses the footer).
    /// </summary>
    public static string? FormatTooltipLine(ItemStack? stack)
    {
        string? names = FormatHeaderNames(stack);
        return names == null ? null : Lang.Get("prosequor:affixes-label", names);
    }

    static string ResolveLocalizedName(ItemAffixEntry affix)
    {
        string name = TryGetLang(affix.LangKey);
        if (string.IsNullOrWhiteSpace(name) || name == affix.LangKey)
        {
            if (!affix.LangKey.Contains(':'))
            {
                string prefixed = TryGetLang("prosequor:" + affix.LangKey);
                if (!string.IsNullOrWhiteSpace(prefixed))
                {
                    name = prefixed;
                }
            }
        }

        return string.IsNullOrWhiteSpace(name) ? affix.LangKey : name;
    }

    static string TryGetLang(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return "";
        }

        try
        {
            return Lang.Get(key);
        }
        catch
        {
            // Pure fixtures / early boot may lack a loaded lang table.
            return key;
        }
    }

    static string EscapeVtml(string text) =>
        text.Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal);
}
