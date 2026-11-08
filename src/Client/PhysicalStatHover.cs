using Vintagestory.API.Client;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;

namespace Prosequor.Client;

/// <summary>
/// Blended Physical percents for Stats panel tooltips (health, satiety, attribute-ring hole).
/// Same rounding as vanilla <c>ComposeStatsGui</c>.
/// </summary>
public static class PhysicalStatHover
{
    public const string Health = "health";
    public const string Satiety = "satiety";
    public const string Ring = "ring";

    public const string StatHealingEffectiveness = "healingeffectivness";
    public const string StatHungerRate = "hungerrate";
    public const string StatWalkSpeed = "walkspeed";
    public const string StatRangedAccuracy = "rangedWeaponsAcc";
    public const string StatRangedChargeSpeed = "rangedWeaponsSpeed";

    /// <summary>Vanilla panel rounding: <c>(int)Math.Round(100f * blended) + "%"</c>.</summary>
    public static string FormatPercent(float blended) =>
        (int)Math.Round(100f * blended) + "%";

    /// <summary>One tooltip line: label, then the percent.</summary>
    public static string FormatLine(string label, float blended) =>
        label + ": " + FormatPercent(blended);

    /// <summary>
    /// Entity-stat ids each hover slot prints, in display order.
    /// Empty for unknown slots (temperature is not a hover target).
    /// </summary>
    public static IReadOnlyList<string> StatIdsFor(string slot)
    {
        if (string.Equals(slot, Health, StringComparison.OrdinalIgnoreCase))
        {
            return [StatHealingEffectiveness];
        }

        if (string.Equals(slot, Satiety, StringComparison.OrdinalIgnoreCase))
        {
            return [StatHungerRate];
        }

        if (string.Equals(slot, Ring, StringComparison.OrdinalIgnoreCase))
        {
            return [StatWalkSpeed, StatRangedAccuracy, StatRangedChargeSpeed];
        }

        return [];
    }

    public static List<string> Lines(ICoreClientAPI capi, string slot)
    {
        Entity? entity = capi.World?.Player?.Entity;
        if (entity?.Stats == null)
        {
            return [];
        }

        return Lines(entity, slot);
    }

    public static List<string> Lines(Entity entity, string slot)
    {
        if (entity.Stats == null)
        {
            return [];
        }

        IReadOnlyList<string> ids = StatIdsFor(slot);
        if (ids.Count == 0)
        {
            return [];
        }

        List<string> lines = new(ids.Count);
        foreach (string id in ids)
        {
            lines.Add(FormatLine(LabelFor(id), entity.Stats.GetBlended(id)));
        }

        return lines;
    }

    static string LabelFor(string entityStat) =>
        entityStat switch
        {
            StatHealingEffectiveness => Lang.Get("Healing effectivness"),
            StatHungerRate => Lang.Get("Hunger rate"),
            StatWalkSpeed => Lang.Get("Walk speed"),
            StatRangedAccuracy => Lang.Get("Ranged Accuracy"),
            StatRangedChargeSpeed => Lang.Get("Ranged Charge Speed"),
            _ => entityStat
        };
}
