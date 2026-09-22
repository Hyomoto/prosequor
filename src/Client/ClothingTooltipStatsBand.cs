using System.Globalization;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace Prosequor.Client;

/// <summary>
/// Hover stats band for clothing: live warmth (equip contribution) | condition rem/max.
/// Clothing category becomes a title icon (character-menu IconUtil name), not a body line.
/// Strips matching vanilla wearable lines by localized template.
/// </summary>
public static class ClothingTooltipStatsBand
{
    static readonly AssetLocation WarmthIcon = new("prosequor", "textures/icons/tailoring/warm-garments.svg");

    /// <summary>Warm tint when live warmth is helpful.</summary>
    const int WarmTintArgb = unchecked((int)0xFFE07060);

    /// <summary>Cool tint when live warmth is harmful (negative).</summary>
    const int CoolTintArgb = unchecked((int)0xFF85A9C4);

    /// <summary>Matches vanilla tooltip green for positive warmth.</summary>
    const string WarmthPositiveHex = "#84ff84";

    /// <summary>Matches vanilla tooltip red for near-zero / cold warmth display.</summary>
    const string WarmthNegativeHex = "#ff8484";

    /// <summary>Vanilla treats warmth below this as the red branch.</summary>
    const float WarmthPositiveThreshold = 0.05f;

    const string ClothingCategoryKey = "Clothing Category: {0}";
    const string ClothingCategoryUnknownKey = "Clothing Category: Unknown";
    /// <summary>Lang key is a bare marker; strip template becomes <c>{marker} {0}</c>.</summary>
    const string ConditionMarkerKey = "Condition:";
    const string MaxWarmthKey = "clothing-maxwarmth";

    static string? cachedLocale;
    static string[]? cachedTemplates;

    public static bool IsEligible(ItemSlot? slot)
    {
        if (slot?.Itemstack?.Collectible == null)
        {
            return false;
        }

        IWearableStatsSupplier? stats =
            slot.Itemstack.Collectible.GetCollectibleInterface<IWearableStatsSupplier>();
        if (stats == null || stats.IsArmorType(slot))
        {
            return false;
        }

        return stats.GetMaxWarmth(slot) != 0f;
    }

    public static ItemTooltipStatsBandRequest? TryProvide(ItemSlot slot)
    {
        if (!IsEligible(slot))
        {
            return null;
        }

        IWearable? wearable = slot.Itemstack!.Collectible.GetCollectibleInterface<IWearable>();
        IWearableStatsSupplier? stats =
            slot.Itemstack.Collectible.GetCollectibleInterface<IWearableStatsSupplier>();
        if (wearable == null || stats == null)
        {
            return null;
        }

        // GetWarmth ensures condition exists on the stack when the game would.
        float warmth = wearable.GetWarmth(slot);
        float condition = slot.Itemstack.Attributes.GetFloat("condition", 1f);
        int rem = (int)(condition * 100f);
        rem = Math.Clamp(rem, 0, 100);

        string warmthText = FormatWarmth(warmth);
        string? warmthColor = WarmthTextColor(warmth);
        int? warmthTint = warmth < 0f ? CoolTintArgb : WarmTintArgb;

        TooltipStatCell[] cells =
        [
            new TooltipStatCell(
                warmthText,
                WarmthIcon,
                TooltipStatIconSide.Leading,
                Weight: 1,
                TintArgb: warmthTint,
                TextColorHex: warmthColor),
            new TooltipStatCell(
                rem.ToString(CultureInfo.InvariantCulture) + "/100",
                Icon: null,
                IconSide: TooltipStatIconSide.None,
                Weight: 3)
        ];

        return new ItemTooltipStatsBandRequest(cells, ResolveTitleIcon(stats.GetDressType(slot)));
    }

    /// <summary>
    /// Removes vanilla clothing lines the band (and title icon) replace.
    /// Leaves flavor and unrelated body text alone.
    /// </summary>
    public static string StripHoverLines(string desc, ItemSlot? slot)
    {
        if (string.IsNullOrEmpty(desc) || !IsEligible(slot))
        {
            return desc;
        }

        return HoverStatLineStripper.Strip(desc, Templates());
    }

    static TooltipTitleIcon ResolveTitleIcon(EnumCharacterDressType dress) =>
        dress switch
        {
            EnumCharacterDressType.Foot => TooltipTitleIcon.FromBuiltIn("boots"),
            EnumCharacterDressType.Hand => TooltipTitleIcon.FromBuiltIn("gloves"),
            EnumCharacterDressType.Shoulder => TooltipTitleIcon.FromBuiltIn("cape"),
            EnumCharacterDressType.Head => TooltipTitleIcon.FromBuiltIn("hat"),
            EnumCharacterDressType.LowerBody => TooltipTitleIcon.FromBuiltIn("trousers"),
            EnumCharacterDressType.UpperBody => TooltipTitleIcon.FromBuiltIn("shirt"),
            EnumCharacterDressType.UpperBodyOver => TooltipTitleIcon.FromBuiltIn("pullover"),
            EnumCharacterDressType.Neck => TooltipTitleIcon.FromBuiltIn("necklace"),
            EnumCharacterDressType.Arm => TooltipTitleIcon.FromBuiltIn("bracers"),
            EnumCharacterDressType.Waist => TooltipTitleIcon.FromBuiltIn("belt"),
            EnumCharacterDressType.Emblem => TooltipTitleIcon.FromBuiltIn("medal"),
            EnumCharacterDressType.Face => TooltipTitleIcon.FromBuiltIn("mask"),
            _ => default
        };

    static string FormatWarmth(float warmth)
    {
        string body = warmth.ToString("0.#", CultureInfo.InvariantCulture);
        if (warmth > 0f)
        {
            return "+" + body + "°C";
        }

        return body + "°C";
    }

    static string? WarmthTextColor(float warmth)
    {
        if (warmth > WarmthPositiveThreshold)
        {
            return WarmthPositiveHex;
        }

        if (warmth < 0f)
        {
            return WarmthNegativeHex;
        }

        // Near zero (including tiny positives below vanilla's 0.05 threshold): default/white.
        return null;
    }

    static string[] Templates()
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

        if (!string.Equals(cachedLocale, locale, StringComparison.Ordinal) || cachedTemplates == null)
        {
            cachedLocale = locale;
            cachedTemplates =
            [
                ResolveTemplate(ClothingCategoryKey),
                ResolveTemplate(ClothingCategoryUnknownKey),
                ResolveConditionTemplate(),
                ResolveTemplate(MaxWarmthKey)
            ];
        }

        return cachedTemplates;
    }

    static string ResolveConditionTemplate()
    {
        string marker = ConditionMarkerKey;
        try
        {
            string? unformatted = Lang.GetUnformatted(ConditionMarkerKey);
            if (!string.IsNullOrWhiteSpace(unformatted))
            {
                marker = unformatted.TrimEnd();
            }
        }
        catch
        {
            // Pure fixtures may lack a loaded lang table.
        }

        return marker + " {0}";
    }

    static string ResolveTemplate(string key)
    {
        try
        {
            string? unformatted = Lang.GetUnformatted(key);
            if (!string.IsNullOrWhiteSpace(unformatted)
                && !string.Equals(unformatted, key, StringComparison.Ordinal))
            {
                return NormalizeTemplate(unformatted.TrimEnd());
            }

            if (!string.IsNullOrWhiteSpace(unformatted) && unformatted.IndexOf('{') >= 0)
            {
                return NormalizeTemplate(unformatted.TrimEnd());
            }

            if (!string.IsNullOrWhiteSpace(unformatted))
            {
                return NormalizeTemplate(unformatted.TrimEnd());
            }
        }
        catch
        {
            // Pure fixtures may lack a loaded lang table.
        }

        return NormalizeTemplate(key);
    }

    /// <summary>
    /// <c>clothing-maxwarmth</c> unformatted may keep <c>{0:0.#}</c>; collapse to <c>{0}</c> for matching.
    /// </summary>
    static string NormalizeTemplate(string template)
    {
        if (string.Equals(template, MaxWarmthKey, StringComparison.Ordinal))
        {
            return "Max warmth: {0}°C";
        }

        // Collapse format specs inside placeholders: {0:0.#} → {0}
        return System.Text.RegularExpressions.Regex.Replace(
            template,
            @"\{(\d+)[^}]*\}",
            "{$1}");
    }
}
