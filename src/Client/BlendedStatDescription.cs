using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;

namespace Prosequor.Client;

/// <summary>
/// Resolves net <c>Entity.Stats.GetBlended</c> multipliers into qualitative trait-tab lines.
/// Neutral (100%) is omitted. Adding a row is one catalog entry plus six lang keys.
/// </summary>
public static class BlendedStatDescription
{
    public const string WalkSpeed = "walkspeed";
    public const string HealingEffectiveness = "healingeffectivness";
    public const string HungerRate = "hungerrate";
    public const string RangedWeaponsSpeed = "rangedWeaponsSpeed";

    readonly record struct StatSpec(
        string EntityStat,
        string LangId,
        bool HigherIsBetter,
        float Floor = QualitativeStatScale.DefaultFloor,
        float Ceiling = QualitativeStatScale.DefaultCeiling);

    static readonly StatSpec[] Catalog =
    [
        new(WalkSpeed, "walkspeed", true),
        new(HealingEffectiveness, "healingeffectivness", true),
        new(HungerRate, "hungerrate", false),
        new(RangedWeaponsSpeed, "ranged-charge-speed", true)
    ];

    public static bool CoversEntityStat(string? entityStat)
    {
        if (string.IsNullOrWhiteSpace(entityStat))
        {
            return false;
        }

        foreach (StatSpec spec in Catalog)
        {
            if (string.Equals(spec.EntityStat, entityStat, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public static bool TryGetHigherIsBetter(string entityStat, out bool higherIsBetter)
    {
        if (TryGetSpec(entityStat, out StatSpec spec))
        {
            higherIsBetter = spec.HigherIsBetter;
            return true;
        }

        higherIsBetter = false;
        return false;
    }

    public static IEnumerable<(string Text, bool Positive)> EnumerateActiveLines(
        Entity? entity,
        ISet<string>? emittedEntityStats = null)
    {
        if (entity?.Stats == null)
        {
            yield break;
        }

        foreach (StatSpec spec in Catalog)
        {
            float blended = entity.Stats.GetBlended(spec.EntityStat);
            if (!QualitativeStatScale.TryClassify(
                    blended,
                    spec.HigherIsBetter,
                    out QualitativeStatScale.Band band,
                    spec.Floor,
                    QualitativeStatScale.DefaultNeutral,
                    spec.Ceiling))
            {
                continue;
            }

            string? text = LangLine(spec.LangId, band.Positive, band.Tier);
            if (text == null)
            {
                continue;
            }

            emittedEntityStats?.Add(spec.EntityStat);
            yield return (text, band.Positive);
        }
    }

    static bool TryGetSpec(string entityStat, out StatSpec spec)
    {
        foreach (StatSpec candidate in Catalog)
        {
            if (string.Equals(candidate.EntityStat, entityStat, StringComparison.OrdinalIgnoreCase))
            {
                spec = candidate;
                return true;
            }
        }

        spec = default;
        return false;
    }

    static string? LangLine(string langId, bool positive, QualitativeStatTier tier)
    {
        string polarity = positive ? "pos" : "neg";
        string key = $"prosequor:statdesc-{langId}-{polarity}-{tier.ToString().ToLowerInvariant()}";
        string? text = Lang.GetIfExists(key);
        return string.IsNullOrWhiteSpace(text) || text == key ? null : text;
    }
}
