using System.Globalization;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;

namespace Prosequor.Client;

/// <summary>
/// Hover stats band for tools/weapons:
/// melee → tier | damage | range | durability;
/// bow → tier | damage | accuracy | durability;
/// durable tool without melee/mining/bow → durability only.
/// Strips matching vanilla GetHeldItemInfo lines from hover text by localized template
/// (values are wildcards), not by reconstructing formatted strings.
/// </summary>
public static class WeaponToolTooltipStatsBand
{
    enum StatsLayout
    {
        None,
        Melee,
        Bow,
        DurabilityOnly
    }

    static readonly AssetLocation DamageIcon = new("prosequor", "textures/icons/gui/tooltip-damage.svg");
    static readonly AssetLocation RangeIcon = new("prosequor", "textures/icons/gui/tooltip-range.svg");
    static readonly AssetLocation AccuracyIcon = new("prosequor", "textures/icons/gui/tooltip-accuracy.svg");

    /// <summary>Warm tint for damage icon (RGBA).</summary>
    const int DamageTintArgb = unchecked((int)0xFFE07060);

    /// <summary>Cool tint for range / accuracy icons (RGBA).</summary>
    const int CoolTintArgb = unchecked((int)0xFF85A9C4);

    const string DurabilityKey = "Durability: {0} / {1}";
    const string ToolTierKey = "Tool Tier: {0}";
    const string AttackPowerDamageKey = "Attack power: {0} damage";
    const string AttackPowerHpKey = "Attack power: -{0} hp";
    const string AttackTierKey = "Attack tier: {0}";
    const string AttackRangeKey = "Attack range: {0} m";
    const string BowPiercingKey = "bow-piercingdamage";
    const string BowAccuracyKey = "bow-accuracybonus";

    static string? cachedLocale;
    static readonly Dictionary<StatsLayout, string[]> CachedTemplates = new();

    public static bool IsEligible(ItemSlot? slot) => ResolveLayout(slot) != StatsLayout.None;

    public static ItemTooltipStatsBandRequest? TryProvide(ItemSlot slot)
    {
        StatsLayout layout = ResolveLayout(slot);
        if (layout == StatsLayout.None)
        {
            return null;
        }

        ItemStack stack = slot.Itemstack!;
        return layout switch
        {
            StatsLayout.Bow => new ItemTooltipStatsBandRequest(BuildBowCells(stack)),
            StatsLayout.DurabilityOnly => new ItemTooltipStatsBandRequest(BuildDurabilityOnlyCells(stack)),
            _ => new ItemTooltipStatsBandRequest(BuildMeleeCells(stack))
        };
    }

    /// <summary>
    /// Removes vanilla lines that the active band layout replaces, by matching localized
    /// lang templates. Leaves mining speed and other description content alone.
    /// </summary>
    public static string StripHoverLines(string desc, ItemSlot? slot)
    {
        StatsLayout layout = ResolveLayout(slot);
        if (string.IsNullOrEmpty(desc) || layout == StatsLayout.None)
        {
            return desc;
        }

        return HoverStatLineStripper.Strip(desc, TemplatesFor(layout));
    }

    static StatsLayout ResolveLayout(ItemSlot? slot)
    {
        ItemStack? stack = slot?.Itemstack;
        if (stack?.Collectible == null)
        {
            return StatsLayout.None;
        }

        CollectibleObject col = stack.Collectible;
        if (col.GetMaxDurability(stack) <= 1)
        {
            return StatsLayout.None;
        }

        if (IsBowLayout(col))
        {
            return StatsLayout.Bow;
        }

        bool hasMining = col.MiningSpeed != null && col.MiningSpeed.Count > 0;
        bool hasAttack = col.GetAttackPower(stack) > 0.5f;
        if (hasMining || hasAttack)
        {
            return StatsLayout.Melee;
        }

        // Conservative: EnumTool and/or creative "tools" tab (covers firestarter etc. without armor).
        if (IsToolGated(col))
        {
            return StatsLayout.DurabilityOnly;
        }

        return StatsLayout.None;
    }

    static bool IsToolGated(CollectibleObject col)
    {
        if (col.Tool != null)
        {
            return true;
        }

        string[]? tabs = col.CreativeInventoryTabs;
        if (tabs == null)
        {
            return false;
        }

        for (int i = 0; i < tabs.Length; i++)
        {
            if (string.Equals(tabs[i], "tools", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Same attribute keys ItemBow.GetHeldItemInfo reads (mod-friendly; no class check).
    /// </summary>
    static bool IsBowLayout(CollectibleObject col)
    {
        JsonObject? attrs = col.Attributes;
        if (attrs == null)
        {
            return false;
        }

        if (attrs["damage"].AsFloat(0f) != 0f)
        {
            return true;
        }

        JsonObject? statMod = attrs["statModifier"];
        return statMod != null && statMod.KeyExists("rangedWeaponsAcc");
    }

    static TooltipStatCell[] BuildMeleeCells(ItemStack stack)
    {
        CollectibleObject col = stack.Collectible;
        float power = col.GetAttackPower(stack);
        float range = col.GetAttackRange(stack);
        int maxDura = col.GetMaxDurability(stack);
        int remDura = col.GetRemainingDurability(stack);

        return
        [
            new TooltipStatCell(
                FormatTier(col.ToolTier),
                Icon: null,
                IconSide: TooltipStatIconSide.None,
                Weight: 1),
            new TooltipStatCell(
                power.ToString("0.#", CultureInfo.InvariantCulture),
                DamageIcon,
                TooltipStatIconSide.Leading,
                Weight: 1,
                TintArgb: DamageTintArgb),
            new TooltipStatCell(
                range.ToString("0.#", CultureInfo.InvariantCulture),
                RangeIcon,
                TooltipStatIconSide.Leading,
                Weight: 1,
                TintArgb: CoolTintArgb),
            DurabilityCell(remDura, maxDura, weight: 3)
        ];
    }

    static TooltipStatCell[] BuildBowCells(ItemStack stack)
    {
        CollectibleObject col = stack.Collectible;
        JsonObject attrs = col.Attributes!;
        int tier = attrs["damageTier"].AsInt(col.ToolTier);
        float damage = attrs["damage"].AsFloat(0f);
        float acc = attrs["statModifier"]["rangedWeaponsAcc"].AsFloat(0f);
        int maxDura = col.GetMaxDurability(stack);
        int remDura = col.GetRemainingDurability(stack);

        return
        [
            new TooltipStatCell(
                FormatTier(tier),
                Icon: null,
                IconSide: TooltipStatIconSide.None,
                Weight: 1),
            new TooltipStatCell(
                damage.ToString("0.#", CultureInfo.InvariantCulture),
                DamageIcon,
                TooltipStatIconSide.Leading,
                Weight: 1,
                TintArgb: DamageTintArgb),
            new TooltipStatCell(
                FormatAccuracyPercent(acc),
                AccuracyIcon,
                TooltipStatIconSide.Leading,
                Weight: 1,
                TintArgb: CoolTintArgb),
            DurabilityCell(remDura, maxDura, weight: 3)
        ];
    }

    static TooltipStatCell[] BuildDurabilityOnlyCells(ItemStack stack)
    {
        CollectibleObject col = stack.Collectible;
        return
        [
            DurabilityCell(col.GetRemainingDurability(stack), col.GetMaxDurability(stack), weight: 1)
        ];
    }

    static TooltipStatCell DurabilityCell(int rem, int max, int weight) =>
        new(
            rem.ToString(CultureInfo.InvariantCulture) + "/" + max.ToString(CultureInfo.InvariantCulture),
            Icon: null,
            IconSide: TooltipStatIconSide.None,
            Weight: weight);

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

    /// <summary>Compact signed percent for the band (e.g. +20%, -5%, 0%).</summary>
    static string FormatAccuracyPercent(float accFraction)
    {
        int pct = (int)(100f * accFraction);
        if (pct > 0)
        {
            return "+" + pct.ToString(CultureInfo.InvariantCulture) + "%";
        }

        return pct.ToString(CultureInfo.InvariantCulture) + "%";
    }

    static string[] TemplatesFor(StatsLayout layout)
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
            || !CachedTemplates.TryGetValue(layout, out string[]? cached))
        {
            if (!string.Equals(cachedLocale, locale, StringComparison.Ordinal))
            {
                CachedTemplates.Clear();
                cachedLocale = locale;
            }

            cached = BuildTemplates(layout);
            CachedTemplates[layout] = cached;
        }

        return cached;
    }

    static string[] BuildTemplates(StatsLayout layout)
    {
        var list = new List<string>(8) { ResolveTemplate(DurabilityKey) };

        if (layout == StatsLayout.DurabilityOnly)
        {
            return list.ToArray();
        }

        if (layout == StatsLayout.Bow)
        {
            list.Add(ResolveTemplate(BowPiercingKey));
            list.Add(ResolveTemplate(BowAccuracyKey));
            return list.ToArray();
        }

        // Melee — do not include mining speed; it sits between tier and attack in vanilla.
        list.Add(ResolveTemplate(ToolTierKey));
        list.Add(ResolveTemplate(AttackPowerDamageKey));
        list.Add(ResolveTemplate(AttackPowerHpKey));
        list.Add(ResolveTemplate(AttackTierKey));
        list.Add(ResolveTemplate(AttackRangeKey));
        return list.ToArray();
    }

    /// <summary>
    /// Localized unformatted template, falling back to the English key string when the
    /// lang table is unavailable (pure tests).
    /// </summary>
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

            // Some locales keep the English key as the entry id; GetUnformatted may return
            // the key itself when that is also the English source string with placeholders.
            if (!string.IsNullOrWhiteSpace(unformatted) && unformatted.IndexOf('{') >= 0)
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
