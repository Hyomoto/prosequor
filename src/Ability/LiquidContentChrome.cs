using System.Text;
using Vintagestory.API.Common;

namespace Prosequor.Ability;

/// <summary>
/// Tooltip / block-info chrome for a liquid portion inside a vessel. Vanilla only prints
/// <c>{0} litres of {1}</c> from the collectible code; affixes and Created By live on the
/// portion stack and must be appended next to that line.
/// </summary>
public static class LiquidContentChrome
{
    /// <summary>
    /// Affix names, quality footer, and Created By for <paramref name="portion"/>.
    /// Empty when the portion has no pedigree chrome.
    /// </summary>
    public static bool TryFormat(
        IWorldAccessor? world,
        ItemStack? portion,
        out string chrome)
    {
        chrome = "";
        if (portion?.Attributes == null)
        {
            return false;
        }

        StringBuilder sb = new();
        string? names = ItemAffixes.FormatHeaderNames(portion);
        if (!string.IsNullOrEmpty(names))
        {
            sb.AppendLine(names);
        }

        if (ItemAffixes.TryFormatQualityFooter(portion, out string quality))
        {
            sb.AppendLine(quality);
        }

        if (ProsequorStackPedigree.IsHomogeneous(portion)
            && OwnerCredit.TryFormatStyled(
                world,
                CraftAttribution.TryGetMakerUid(portion),
                OwnerCredit.TryGetCreditLang(portion) ?? OwnerCredit.CreatedByLang,
                out string credit))
        {
            sb.AppendLine(credit);
        }

        chrome = sb.ToString().TrimEnd();
        return chrome.Length > 0;
    }

    /// <summary>
    /// Insert <paramref name="chrome"/> after <paramref name="lines"/> newlines starting at
    /// <paramref name="from"/> (so it sits under the litres line, before perishable text).
    /// </summary>
    public static void InsertAfterNthLine(
        StringBuilder? target,
        int from,
        int lines,
        string? chrome)
    {
        if (target == null || string.IsNullOrEmpty(chrome))
        {
            return;
        }

        string existing = target.ToString();
        if (existing.IndexOf(chrome, StringComparison.Ordinal) >= 0)
        {
            return;
        }

        target.Insert(FindAfterNthLine(existing, from, lines), WithTrailingNewline(chrome));
    }

    /// <inheritdoc cref="InsertAfterNthLine(StringBuilder?, int, int, string?)"/>
    public static void InsertAfterNthLine(
        ref string info,
        int from,
        int lines,
        string? chrome)
    {
        if (string.IsNullOrEmpty(chrome))
        {
            return;
        }

        if (string.IsNullOrEmpty(info))
        {
            info = chrome;
            return;
        }

        if (info.IndexOf(chrome, StringComparison.Ordinal) >= 0)
        {
            return;
        }

        info = info.Insert(FindAfterNthLine(info, from, lines), WithTrailingNewline(chrome));
    }

    static string WithTrailingNewline(string chrome) =>
        chrome.EndsWith('\n') ? chrome : chrome + "\n";

    static int FindAfterNthLine(string text, int from, int lines)
    {
        int pos = Math.Clamp(from, 0, text.Length);
        int remaining = Math.Max(1, lines);
        while (remaining > 0 && pos < text.Length)
        {
            int nl = text.IndexOf('\n', pos);
            if (nl < 0)
            {
                return text.Length;
            }

            pos = nl + 1;
            remaining--;
        }

        return pos;
    }
}
