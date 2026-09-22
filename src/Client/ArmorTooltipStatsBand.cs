using System.Globalization;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace Prosequor.Client;

/// <summary>
/// Hover stats band for armor: protection tier | percent protection | flat reduction | durability.
/// Clothing category becomes a title icon (character-menu SVG), not a body line.
/// Strips matching vanilla wearable lines by localized template.
/// </summary>
public static class ArmorTooltipStatsBand
{
    static readonly AssetLocation ProtectionIcon = new("prosequor", "textures/icons/damage-protection.svg");
    static readonly AssetLocation ReductionIcon = new("prosequor", "textures/icons/damage-reduction.svg");

    static readonly AssetLocation ArmorHeadIcon = new("textures/icons/character/armor-helmet.svg");
    static readonly AssetLocation ArmorBodyIcon = new("textures/icons/character/armor-body.svg");
    static readonly AssetLocation ArmorLegsIcon = new("textures/icons/character/armor-legs.svg");

    /// <summary>Warm tint for percent-protection icon (RGBA).</summary>
    const int ProtectionTintArgb = unchecked((int)0xFFE07060);

    /// <summary>Cool tint for flat-reduction icon (RGBA).</summary>
    const int ReductionTintArgb = unchecked((int)0xFF85A9C4);

    const string DurabilityKey = "Durability: {0} / {1}";
    const string ClothingCategoryKey = "Clothing Category: {0}";
    const string ClothingCategoryUnknownKey = "Clothing Category: Unknown";
    const string FlatReductionKey = "Flat damage reduction: {0} hp";
    const string PercentProtectionKey = "Percent protection: {0}%";
    const string ProtectionTierKey = "Protection tier: {0}";

    static string? cachedLocale;
    static string[]? cachedTemplates;

    public static bool IsEligible(ItemSlot? slot) => TryGetProtection(slot, out _);

    public static ItemTooltipStatsBandRequest? TryProvide(ItemSlot slot)
    {
        if (!TryGetProtection(slot, out ProtectionModifiers? mods) || mods == null)
        {
            return null;
        }

        ItemStack stack = slot.Itemstack!;
        CollectibleObject col = stack.Collectible;
        int maxDura = col.GetMaxDurability(stack);
        int remDura = col.GetRemainingDurability(stack);
        int percent = (int)(100f * mods.RelativeProtection);

        TooltipStatCell[] cells =
        [
            new TooltipStatCell(
                FormatTier(mods.ProtectionTier),
                Icon: null,
                IconSide: TooltipStatIconSide.None,
                Weight: 1),
            new TooltipStatCell(
                percent.ToString(CultureInfo.InvariantCulture) + "%",
                ProtectionIcon,
                TooltipStatIconSide.Leading,
                Weight: 1,
                TintArgb: ProtectionTintArgb),
            new TooltipStatCell(
                mods.FlatDamageReduction.ToString("0.#", CultureInfo.InvariantCulture),
                ReductionIcon,
                TooltipStatIconSide.Leading,
                Weight: 1,
                TintArgb: ReductionTintArgb),
            new TooltipStatCell(
                remDura.ToString(CultureInfo.InvariantCulture) + "/" + maxDura.ToString(CultureInfo.InvariantCulture),
                Icon: null,
                IconSide: TooltipStatIconSide.None,
                Weight: 3)
        ];

        return new ItemTooltipStatsBandRequest(cells, ResolveTitleIcon(slot));
    }

    /// <summary>
    /// Removes vanilla wearable lines the band (and title icon) replace.
    /// Leaves healing / hunger / walk speed / high-tier text alone.
    /// </summary>
    public static string StripHoverLines(string desc, ItemSlot? slot)
    {
        if (string.IsNullOrEmpty(desc) || !IsEligible(slot))
        {
            return desc;
        }

        return HoverStatLineStripper.Strip(desc, Templates());
    }

    static bool TryGetProtection(ItemSlot? slot, out ProtectionModifiers? mods)
    {
        mods = null;
        if (slot?.Itemstack?.Collectible == null)
        {
            return false;
        }

        IWearableStatsSupplier? stats =
            slot.Itemstack.Collectible.GetCollectibleInterface<IWearableStatsSupplier>();
        if (stats == null)
        {
            return false;
        }

        mods = stats.GetProtectionModifiers(slot);
        return mods != null;
    }

    static TooltipTitleIcon ResolveTitleIcon(ItemSlot slot)
    {
        IWearableStatsSupplier? stats =
            slot.Itemstack?.Collectible?.GetCollectibleInterface<IWearableStatsSupplier>();
        if (stats == null)
        {
            return default;
        }

        return stats.GetDressType(slot) switch
        {
            EnumCharacterDressType.ArmorHead => TooltipTitleIcon.FromSvg(ArmorHeadIcon),
            EnumCharacterDressType.ArmorBody => TooltipTitleIcon.FromSvg(ArmorBodyIcon),
            EnumCharacterDressType.ArmorLegs => TooltipTitleIcon.FromSvg(ArmorLegsIcon),
            _ => default
        };
    }

    static string FormatTier(int tier)
    {
        try
        {
            string localized = Lang.Get("prosequor:tooltip-tier", tier);
            if (!string.IsNullOrWhiteSpace(localized)
                && !string.Equals(localized, "prosequor:tooltip-tier", StringComparison.Ordinal))
            {
                return localized;
            }
        }
        catch
        {
            // Pure fixtures / missing lang table.
        }

        return "Tier " + tier.ToString(CultureInfo.InvariantCulture);
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
                ResolveTemplate(DurabilityKey),
                ResolveTemplate(ClothingCategoryKey),
                ResolveTemplate(ClothingCategoryUnknownKey),
                ResolveTemplate(FlatReductionKey),
                ResolveTemplate(PercentProtectionKey),
                ResolveTemplate(ProtectionTierKey)
            ];
        }

        return cachedTemplates;
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

            // Exact keys with no placeholders (e.g. Clothing Category: Unknown).
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
