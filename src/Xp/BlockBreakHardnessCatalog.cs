using Prosequor.Ability;
using Vintagestory.API.Common;

namespace Prosequor.Xp;

/// <summary>
/// GameReady catalog of block <see cref="Block.Resistance"/> min/max per break domain
/// (dig / mine / chop). Amount tables on <c>prosequor:deed</c> + <c>broken</c> normalize against these ranges.
/// </summary>
public sealed class BlockBreakHardnessCatalog
{
    public const string DomainDig = BlockBreakClassification.TokenDig;
    public const string DomainMine = BlockBreakClassification.TokenMine;
    public const string DomainChop = BlockBreakClassification.TokenChop;

    readonly Dictionary<string, DomainRange> byDomain;

    public readonly record struct DomainRange(float Min, float Max, int BlockCount);

    BlockBreakHardnessCatalog(Dictionary<string, DomainRange> byDomain)
    {
        this.byDomain = byDomain;
    }

    public int DomainCount => byDomain.Count;

    public static BlockBreakHardnessCatalog Build(ICoreAPI api)
    {
        Dictionary<string, Acc> acc = new(StringComparer.OrdinalIgnoreCase);
        if (api?.World?.Blocks == null)
        {
            return new BlockBreakHardnessCatalog(new Dictionary<string, DomainRange>(StringComparer.OrdinalIgnoreCase));
        }

        foreach (Block block in api.World.Blocks)
        {
            if (block == null || block.Id == 0)
            {
                continue;
            }

            string? domain = BlockBreakClassification.ClassifyToken(block);
            if (domain is not (DomainDig or DomainMine or DomainChop))
            {
                continue;
            }

            float resistance = block.Resistance;
            if (resistance <= 0f)
            {
                continue;
            }

            if (!acc.TryGetValue(domain, out Acc cur))
            {
                acc[domain] = new Acc(resistance, resistance, 1);
                continue;
            }

            acc[domain] = new Acc(
                Math.Min(cur.Min, resistance),
                Math.Max(cur.Max, resistance),
                cur.Count + 1);
        }

        Dictionary<string, DomainRange> map = new(StringComparer.OrdinalIgnoreCase);
        foreach (KeyValuePair<string, Acc> kv in acc)
        {
            map[kv.Key] = new DomainRange(kv.Value.Min, kv.Value.Max, kv.Value.Count);
        }

        return new BlockBreakHardnessCatalog(map);
    }

    public bool TryGetRange(string? domain, out float min, out float max)
    {
        min = 0f;
        max = 0f;
        if (string.IsNullOrWhiteSpace(domain)
            || !byDomain.TryGetValue(domain, out DomainRange range)
            || range.BlockCount <= 0)
        {
            return false;
        }

        min = range.Min;
        max = range.Max;
        return true;
    }

    public DomainRange? TryGet(string? domain) =>
        !string.IsNullOrWhiteSpace(domain) && byDomain.TryGetValue(domain, out DomainRange range)
            ? range
            : null;

    readonly record struct Acc(float Min, float Max, int Count);
}
