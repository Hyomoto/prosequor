using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace Prosequor.Ability;

/// <summary>
/// Liquid portions never keep a mixed Frozen bag. Merge always averages mods by
/// litres (<see cref="ItemStack.StackSize"/>); prestige (maker + affix bag) survives
/// only when both sides match.
/// </summary>
public static class ProsequorLiquidPedigree
{
    /// <summary>
    /// True for a containable liquid portion (juice, wine, …), not the jug/bucket host.
    /// </summary>
    public static bool IsPortion(ItemStack? stack)
    {
        if (stack?.Collectible == null)
        {
            return false;
        }

        WaterTightContainableProps? props = BlockLiquidContainerBase.GetContainableProps(stack);
        return props != null && props.Containable;
    }

    /// <summary>
    /// Prestige identity: same maker (both null counts) and the same ordered affix bag.
    /// Mods and contributors are not prestige.
    /// </summary>
    public static bool SamePrestige(ProsequorBlob a, ProsequorBlob b)
    {
        if (!string.Equals(a.MakerUid, b.MakerUid, StringComparison.Ordinal))
        {
            return false;
        }

        return AffixesEqual(a.Affixes, b.Affixes);
    }

    /// <summary>
    /// Weighted average of mod factors. Missing key is 1.0. Weights are litres
    /// (<c>StackSize</c>). Degenerate weights fall back to the non-empty side.
    /// </summary>
    public static IReadOnlyList<ProsequorBlob.ModFactor> AverageMods(
        IReadOnlyList<ProsequorBlob.ModFactor> sinkMods,
        int sinkQty,
        IReadOnlyList<ProsequorBlob.ModFactor> srcMods,
        int srcQty)
    {
        if (sinkQty <= 0 && srcQty <= 0)
        {
            return Array.Empty<ProsequorBlob.ModFactor>();
        }

        if (sinkQty <= 0)
        {
            return NormalizeAverage(srcMods);
        }

        if (srcQty <= 0)
        {
            return NormalizeAverage(sinkMods);
        }

        Dictionary<string, float> sinkMap = ToModMap(sinkMods);
        Dictionary<string, float> srcMap = ToModMap(srcMods);
        HashSet<string> keys = new(StringComparer.OrdinalIgnoreCase);
        foreach (string key in sinkMap.Keys)
        {
            keys.Add(key);
        }

        foreach (string key in srcMap.Keys)
        {
            keys.Add(key);
        }

        if (keys.Count == 0)
        {
            return Array.Empty<ProsequorBlob.ModFactor>();
        }

        float total = sinkQty + srcQty;
        List<ProsequorBlob.ModFactor> list = new(keys.Count);
        foreach (string key in keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase))
        {
            float sinkFactor = sinkMap.TryGetValue(key, out float sf) ? sf : 1f;
            float srcFactor = srcMap.TryGetValue(key, out float rf) ? rf : 1f;
            float averaged = (sinkQty * sinkFactor + srcQty * srcFactor) / total;
            if (!float.IsFinite(averaged) || averaged <= 0f)
            {
                continue;
            }

            list.Add(new ProsequorBlob.ModFactor(key, averaged));
        }

        return list;
    }

    /// <summary>
    /// Mash → spirit: keep maker / other affixes / mods, drop the <c>Quality:</c> grade
    /// and <c>qualityRank</c> (rank is spent as a quality-base addend at the condenser).
    /// </summary>
    public static ProsequorBlob ForDistillate(ProsequorBlob source)
    {
        if (!source.HasPersistable)
        {
            return ProsequorBlob.Empty;
        }

        return source
            .WithAffixes(ItemAffixes.WithoutQuality(source.Affixes))
            .WithQualityRank(0);
    }

    /// <summary>
    /// Mash rank spent as a quality-base addend (0 when unset).
    /// </summary>
    public static float QualityBaseBonus(int mashRank) => mashRank > 0 ? mashRank : 0;

    /// <summary>
    /// Distilled output: no <c>Quality:</c> grade, no leftover <c>qualityRank</c>.
    /// Intoxication / value affixes and mods stay.
    /// </summary>
    public static void StripQualityAndRank(ItemStack? stack)
    {
        if (stack?.Attributes == null)
        {
            return;
        }

        ItemAffixes.WriteAll(stack, ItemAffixes.WithoutQuality(ItemAffixes.GetAll(stack)));
        ProsequorStackPedigree.StampSurfaceFromStack(stack);
        ProsequorStackPedigree.StampQualityRank(stack, 0);
    }

    /// <summary>
    /// Dilute two portion blobs. Always averages mods. Prestige match keeps maker and
    /// affixes (and sink contributors / payload); miss strips maker, affixes, and
    /// contributors.
    /// </summary>
    public static ProsequorBlob MergeBlobs(
        ProsequorBlob sink,
        int sinkQty,
        ProsequorBlob src,
        int srcQty)
    {
        if (sinkQty <= 0 && srcQty <= 0)
        {
            return ProsequorBlob.Empty;
        }

        if (sinkQty <= 0)
        {
            return src;
        }

        if (srcQty <= 0)
        {
            return sink;
        }

        IReadOnlyList<ProsequorBlob.ModFactor> mods = AverageMods(sink.Mods, sinkQty, src.Mods, srcQty);
        int qualityRank = AverageQualityRank(sink.QualityRank, sinkQty, src.QualityRank, srcQty);
        if (SamePrestige(sink, src))
        {
            return new ProsequorBlob(
                sink.MakerUid,
                sink.Contributors,
                sink.Recipe,
                sink.FriendlinessReadyAtTotalHours,
                sink.AnvilSplits,
                sink.Affixes,
                mods,
                qualityRank);
        }

        return new ProsequorBlob(
            makerUid: null,
            contributors: null,
            recipe: null,
            friendlinessReadyAtTotalHours: 0,
            anvilSplits: 0,
            affixes: null,
            mods: mods,
            qualityRank);
    }

    /// <summary>
    /// Weighted average of quality ranks. Missing rank is 0. Degenerate weights fall
    /// back to the non-empty side. Rounded away from zero.
    /// </summary>
    public static int AverageQualityRank(int sinkRank, int sinkQty, int srcRank, int srcQty)
    {
        if (sinkQty <= 0 && srcQty <= 0)
        {
            return 0;
        }

        if (sinkQty <= 0)
        {
            return ClampRank(srcRank);
        }

        if (srcQty <= 0)
        {
            return ClampRank(sinkRank);
        }

        float avg = ((sinkQty * sinkRank) + (srcQty * srcRank)) / (float)(sinkQty + srcQty);
        if (!float.IsFinite(avg))
        {
            return 0;
        }

        return ClampRank((int)Math.Round(avg, MidpointRounding.AwayFromZero));
    }

    static int ClampRank(int rank) => rank > 0 ? rank : 0;

    /// <summary>
    /// Fold mixed Frozen groups into one blob via successive <see cref="MergeBlobs"/>.
    /// </summary>
    public static ProsequorBlob CollapseGroups(IReadOnlyList<ProsequorStackPedigree.FrozenGroup> groups)
    {
        if (groups == null || groups.Count == 0)
        {
            return ProsequorBlob.Empty;
        }

        ProsequorBlob blob = ProsequorBlob.Empty;
        int qty = 0;
        for (int i = 0; i < groups.Count; i++)
        {
            ProsequorStackPedigree.FrozenGroup g = groups[i];
            if (g.Qty <= 0)
            {
                continue;
            }

            if (qty <= 0)
            {
                blob = g.Blob;
                qty = g.Qty;
                continue;
            }

            blob = MergeBlobs(blob, qty, g.Blob, g.Qty);
            qty += g.Qty;
        }

        return blob;
    }

    /// <summary>
    /// Collapse a liquid portion to a single blob covering <see cref="ItemStack.StackSize"/>.
    /// No-op when already homogeneous / anonymous.
    /// </summary>
    public static void EnsureHomogeneous(ItemStack? stack)
    {
        if (stack?.Attributes == null || stack.StackSize <= 0 || !IsPortion(stack))
        {
            return;
        }

        ProsequorStackPedigree.AbsorbLegacySurface(stack);

        if (ProsequorStackPedigree.IsHomogeneous(stack)
            && !ProsequorStackPedigree.HasFrozen(stack))
        {
            // Live size-1 or empty pedigree.
            if (ProsequorStackPedigree.IsLive(stack) && stack.StackSize == 1)
            {
                return;
            }
        }

        if (ProsequorStackPedigree.IsHomogeneous(stack)
            && ProsequorStackPedigree.HasFrozen(stack)
            && ProsequorStackPedigree.TryGetPrimaryBlob(stack, out ProsequorBlob sole)
            && ProsequorStackPedigree.TotalQty(ProsequorStackPedigree.ReadFrozenGroups(stack))
                == stack.StackSize)
        {
            // Single Frozen group already covering the whole stack.
            WriteFullBlob(stack, sole);
            return;
        }

        IReadOnlyList<ProsequorStackPedigree.FrozenGroup> groups =
            ProsequorStackPedigree.ReadFrozenGroups(stack);
        if (ProsequorStackPedigree.IsLive(stack)
            && ProsequorStackPedigree.TryGetPrimaryBlob(stack, out ProsequorBlob live))
        {
            // Live is one unit; expand / collapse against StackSize.
            if (stack.StackSize == 1)
            {
                return;
            }

            // Live + multi size without Frozen: shortfall. Keep the one unit as full stack.
            WriteFullBlob(stack, live);
            return;
        }

        if (groups.Count == 0)
        {
            return;
        }

        ProsequorBlob collapsed = CollapseGroups(groups);
        WriteFullBlob(stack, collapsed);
    }

    /// <summary>
    /// After litres were added onto an occupied sink portion. <paramref name="sinkQtyBefore"/>
    /// is the sink size prior to the add; <paramref name="movedQty"/> is how many units arrived.
    /// Empty sink (first fill) is not a merge — leave the incoming clone alone.
    /// </summary>
    public static void ApplyMerge(
        ItemStack? sinkPortion,
        ProsequorBlob sourceBlob,
        int sinkQtyBefore,
        int movedQty)
    {
        if (sinkPortion?.Attributes == null || movedQty <= 0 || sinkQtyBefore <= 0)
        {
            return;
        }

        ProsequorStackPedigree.AbsorbLegacySurface(sinkPortion);
        EnsureHomogeneous(sinkPortion);

        ProsequorBlob sinkBlob = ProsequorBlob.Empty;
        _ = ProsequorStackPedigree.TryGetPrimaryBlob(sinkPortion, out sinkBlob);

        ProsequorBlob merged = MergeBlobs(sinkBlob, sinkQtyBefore, sourceBlob, movedQty);
        WriteFullBlob(sinkPortion, merged);
    }

    /// <summary>
    /// Write <paramref name="blob"/> as the sole pedigree covering the whole stack
    /// (Live when size 1, else one Frozen group × StackSize).
    /// </summary>
    public static void WriteFullBlob(ItemStack? stack, ProsequorBlob blob)
    {
        if (stack?.Attributes == null || stack.StackSize <= 0)
        {
            return;
        }

        if (!blob.HasPersistable)
        {
            ProsequorStackPedigree.ClearAll(stack);
            ItemAffixes.WriteAll(stack, Array.Empty<ItemAffixEntry>());
            CraftAttributeMods.WriteAll(stack, Array.Empty<ProsequorBlob.ModFactor>());
            return;
        }

        ProsequorStackPedigree.ClearAll(stack);
        if (stack.StackSize == 1)
        {
            blob.WriteTo(stack.Attributes.GetOrAddTreeAttribute(ProsequorStackPedigree.LiveAttr));
        }
        else
        {
            ProsequorStackPedigree.WriteFrozenGroups(
                stack,
                new[] { new ProsequorStackPedigree.FrozenGroup(blob, stack.StackSize) });
        }

        // WriteFrozenGroups already materializes; Live path needs an explicit project.
        if (stack.StackSize == 1)
        {
            ItemAffixes.WriteAll(stack, blob.Affixes);
            CraftAttributeMods.WriteAll(stack, blob.Mods);
        }
    }

    static bool AffixesEqual(
        IReadOnlyList<ItemAffixEntry> a,
        IReadOnlyList<ItemAffixEntry> b)
    {
        if (a.Count != b.Count)
        {
            return false;
        }

        for (int i = 0; i < a.Count; i++)
        {
            ItemAffixEntry left = a[i];
            ItemAffixEntry right = b[i];
            if (!string.Equals(left.Code, right.Code, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(left.LangKey, right.LangKey, StringComparison.Ordinal)
                || !string.Equals(left.Color, right.Color, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    static Dictionary<string, float> ToModMap(IReadOnlyList<ProsequorBlob.ModFactor> mods)
    {
        Dictionary<string, float> map = new(StringComparer.OrdinalIgnoreCase);
        if (mods == null)
        {
            return map;
        }

        for (int i = 0; i < mods.Count; i++)
        {
            ProsequorBlob.ModFactor mod = mods[i];
            if (string.IsNullOrWhiteSpace(mod.Key)
                || !float.IsFinite(mod.Factor)
                || mod.Factor <= 0f)
            {
                continue;
            }

            map[mod.Key.Trim()] = mod.Factor;
        }

        return map;
    }

    static IReadOnlyList<ProsequorBlob.ModFactor> NormalizeAverage(
        IReadOnlyList<ProsequorBlob.ModFactor> mods)
    {
        if (mods == null || mods.Count == 0)
        {
            return Array.Empty<ProsequorBlob.ModFactor>();
        }

        Dictionary<string, float> map = ToModMap(mods);
        if (map.Count == 0)
        {
            return Array.Empty<ProsequorBlob.ModFactor>();
        }

        List<ProsequorBlob.ModFactor> list = new(map.Count);
        foreach (KeyValuePair<string, float> pair in map.OrderBy(
                     p => p.Key,
                     StringComparer.OrdinalIgnoreCase))
        {
            list.Add(new ProsequorBlob.ModFactor(pair.Key, pair.Value));
        }

        return list;
    }
}
