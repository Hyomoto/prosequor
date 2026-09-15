using System.Globalization;
using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;

namespace Prosequor.Client;

/// <summary>
/// Hover stats band for tools/weapons:
/// melee → tier | damage | range | durability;
/// bow → tier | damage | accuracy | durability;
/// durable tool without melee/mining/bow → durability only.
/// Strips matching vanilla GetHeldItemInfo lines from hover text only.
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
    /// Removes vanilla lines that the active band layout replaces.
    /// Leaves mining speed and other description content alone.
    /// </summary>
    public static string StripHoverLines(string desc, ItemSlot? slot)
    {
        StatsLayout layout = ResolveLayout(slot);
        if (string.IsNullOrEmpty(desc) || layout == StatsLayout.None)
        {
            return desc;
        }

        HashSet<string> drop = BuildStripLines(slot!.Itemstack!, layout);
        if (drop.Count == 0)
        {
            return desc;
        }

        string[] lines = desc.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var kept = new List<string>(lines.Length);
        foreach (string line in lines)
        {
            string trimmed = line.TrimEnd();
            if (drop.Contains(trimmed) || drop.Contains(line))
            {
                continue;
            }

            kept.Add(line);
        }

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
                "Tier " + col.ToolTier.ToString(CultureInfo.InvariantCulture),
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
                "Tier " + tier.ToString(CultureInfo.InvariantCulture),
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

    static HashSet<string> BuildStripLines(ItemStack stack, StatsLayout layout)
    {
        CollectibleObject col = stack.Collectible;
        var drop = new HashSet<string>(StringComparer.Ordinal);

        int maxDura = col.GetMaxDurability(stack);
        if (maxDura > 1)
        {
            AddLine(drop, Lang.Get(
                "Durability: {0} / {1}",
                col.GetRemainingDurability(stack),
                maxDura));
        }

        if (layout == StatsLayout.DurabilityOnly)
        {
            return drop;
        }

        if (layout == StatsLayout.Bow)
        {
            JsonObject? attrs = col.Attributes;
            if (attrs != null)
            {
                float damage = attrs["damage"].AsFloat(0f);
                if (damage != 0f)
                {
                    AddLine(drop, Lang.Get("bow-piercingdamage", damage));
                }

                float acc = attrs["statModifier"]["rangedWeaponsAcc"].AsFloat(0f);
                // Vanilla only appends when non-zero; still strip the formatted line if present.
                if (acc != 0f)
                {
                    AddLine(drop, Lang.Get(
                        "bow-accuracybonus",
                        acc > 0f ? "+" : "",
                        (int)(100f * acc)));
                }
            }

            return drop;
        }

        // Melee
        if (col.MiningSpeed != null && col.MiningSpeed.Count > 0)
        {
            AddLine(drop, Lang.Get("Tool Tier: {0}", col.ToolTier));
        }

        float power = col.GetAttackPower(stack);
        if (power > 0.5f)
        {
            string powerFmt = power.ToString("0.#", CultureInfo.InvariantCulture);
            AddLine(drop, Lang.Get("Attack power: -{0} hp", powerFmt));
            AddLine(drop, Lang.Get("Attack power: {0} damage", powerFmt));
            AddLine(drop, Lang.Get("Attack tier: {0}", col.ToolTier));
        }

        float range = col.GetAttackRange(stack);
        if (range > GlobalConstants.DefaultAttackRange)
        {
            string rangeFmt = range.ToString("0.#", CultureInfo.InvariantCulture);
            AddLine(drop, Lang.Get("Attack range: {0} m", rangeFmt));
        }

        return drop;
    }

    static void AddLine(HashSet<string> drop, string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return;
        }

        drop.Add(line.TrimEnd());
    }
}
