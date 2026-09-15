using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;

namespace Prosequor.Ability;

/// <summary>
/// Stack pedigree under <c>prosequorLive</c> (mutable, size 1) and <c>prosequorFrozen</c>
/// (immutable coalesced bag of blob×qty totaling StackSize).
/// </summary>
public static class ProsequorStackPedigree
{
    public const string LiveAttr = "prosequorLive";
    public const string FrozenAttr = "prosequorFrozen";
    public const string GroupCountKey = "n";
    public const string QtyKey = "qty";
    public const string HashKey = "hash";
    public const string BlobKey = "blob";

    public readonly record struct FrozenGroup(ProsequorBlob Blob, int Qty);

    public static bool HasLive(ItemStack? stack) =>
        stack?.Attributes?.GetTreeAttribute(LiveAttr) != null;

    public static bool HasFrozen(ItemStack? stack)
    {
        ITreeAttribute? tree = stack?.Attributes?.GetTreeAttribute(FrozenAttr);
        return tree != null && tree.GetInt(GroupCountKey, 0) > 0;
    }

    public static bool IsLive(ItemStack? stack) => HasLive(stack) && !HasFrozen(stack);

    /// <summary>0 or 1 distinct Frozen blob groups (or Live / empty).</summary>
    public static bool IsHomogeneous(ItemStack? stack)
    {
        if (stack == null)
        {
            return true;
        }

        if (IsLive(stack))
        {
            return true;
        }

        IReadOnlyList<FrozenGroup> groups = ReadFrozenGroups(stack);
        int distinct = 0;
        foreach (FrozenGroup g in groups)
        {
            if (g.Qty > 0 && g.Blob.HasPersistable)
            {
                distinct++;
                if (distinct > 1)
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>
    /// Live blob, else the first non-empty Frozen unit's blob, else null when anonymous/missing.
    /// </summary>
    public static bool TryGetPrimaryBlob(ItemStack? stack, out ProsequorBlob blob)
    {
        blob = ProsequorBlob.Empty;
        if (stack?.Attributes == null)
        {
            return false;
        }

        ITreeAttribute? live = stack.Attributes.GetTreeAttribute(LiveAttr);
        if (live != null)
        {
            blob = ProsequorBlob.ReadFrom(live);
            return blob.HasPersistable;
        }

        foreach (FrozenGroup g in ReadFrozenGroups(stack))
        {
            if (g.Qty <= 0)
            {
                continue;
            }

            blob = g.Blob;
            return blob.HasPersistable;
        }

        return false;
    }

    public static void StampMaker(ItemStack? stack, string? makerUid)
    {
        if (stack?.Attributes == null || string.IsNullOrWhiteSpace(makerUid))
        {
            return;
        }

        MutateAllUnits(stack, b => b.WithMaker(makerUid));
    }

    /// <summary>Clear maker only. Contributors, quality, and mods stay.</summary>
    public static void ClearMaker(ItemStack? stack)
    {
        if (stack?.Attributes == null)
        {
            return;
        }

        MutateAllUnits(stack, b => b.WithMaker(null));
    }

    public static void AddContributor(ItemStack? stack, string? contributorUid, int amount = 1)
    {
        if (stack?.Attributes == null || string.IsNullOrWhiteSpace(contributorUid) || amount <= 0)
        {
            return;
        }

        MutateAllUnits(stack, b => b.WithContributor(contributorUid, amount));
    }

    public static void StampRecipe(ItemStack? stack, string? recipeKey)
    {
        if (stack?.Attributes == null || string.IsNullOrWhiteSpace(recipeKey))
        {
            return;
        }

        MutateAllUnits(stack, b => b.WithRecipe(recipeKey));
    }

    /// <summary>
    /// Authored craft quality rank. 0 clears. Homogeneous / Live stacks only —
    /// mixed bags keep per-unit ranks.
    /// </summary>
    public static void StampQualityRank(ItemStack? stack, int rank)
    {
        if (stack?.Attributes == null)
        {
            return;
        }

        MutateAllUnits(stack, b => b.WithQualityRank(rank));
    }

    /// <summary>Primary blob quality rank, or 0 when unset / anonymous.</summary>
    public static bool TryGetQualityRank(ItemStack? stack, out int rank)
    {
        rank = 0;
        if (!TryGetPrimaryBlob(stack, out ProsequorBlob blob) || blob.QualityRank <= 0)
        {
            return false;
        }

        rank = blob.QualityRank;
        return true;
    }

    /// <summary>
    /// Recipe receipt from Live or the first Frozen unit (works even when attribution-anonymous).
    /// </summary>
    public static bool TryGetRecipeKey(ItemStack? stack, out string? recipeKey)
    {
        recipeKey = null;
        if (stack?.Attributes == null)
        {
            return false;
        }

        ITreeAttribute? live = stack.Attributes.GetTreeAttribute(LiveAttr);
        if (live != null)
        {
            string? fromLive = ProsequorBlob.ReadFrom(live).Recipe;
            if (!string.IsNullOrEmpty(fromLive))
            {
                recipeKey = fromLive;
                return true;
            }
        }

        foreach (FrozenGroup g in ReadFrozenGroups(stack))
        {
            if (g.Qty <= 0 || string.IsNullOrEmpty(g.Blob.Recipe))
            {
                continue;
            }

            recipeKey = g.Blob.Recipe;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Clamp attributed qty to StackSize (peel when bag &gt; size). Shortfall is allowed:
    /// untracked units need no Empty fillers. Live on size &gt; 1 freezes as one unit.
    /// </summary>
    public static void EnsureFrozenMatchesStackSize(ItemStack? stack)
    {
        if (stack?.Attributes == null)
        {
            return;
        }

        int size = Math.Max(0, stack.StackSize);
        if (size <= 0)
        {
            ClearAll(stack);
            return;
        }

        if (HasLive(stack) && size == 1)
        {
            ClearFrozen(stack);
            return;
        }

        // Live is one unit; size &gt; 1 means shortfall, not blob×StackSize.
        if (HasLive(stack) && size > 1)
        {
            FreezeLive(stack);
        }

        List<FrozenGroup> groups = new(ReadFrozenGroups(stack));
        int total = TotalQty(groups);
        if (total > size)
        {
            groups = PeelMinFragment(groups, size, out _);
            WriteFrozenGroups(stack, Coalesce(groups));
        }

        if (size == 1)
        {
            PromoteToLive(stack);
        }
        else
        {
            ClearLive(stack);
        }
    }

    /// <summary>
    /// Craft / intentional yield: expand primary blob qty up to StackSize (duplicate attribution).
    /// No-ops when anonymous or missing pedigree.
    /// </summary>
    public static void DuplicatePrimaryToMatchStackSize(ItemStack? stack)
    {
        if (stack?.Attributes == null || stack.StackSize <= 0)
        {
            return;
        }

        if (!TryGetPrimaryBlob(stack, out ProsequorBlob primary) || !primary.HasPersistable)
        {
            return;
        }

        int size = stack.StackSize;
        if (size == 1)
        {
            ClearFrozen(stack);
            primary.WriteTo(stack.Attributes.GetOrAddTreeAttribute(LiveAttr));
            MaterializePrimarySurface(stack);
            return;
        }

        ClearLive(stack);
        WriteFrozenGroups(stack, new[] { new FrozenGroup(primary, size) });
    }

    /// <summary>
    /// After vanilla moved <paramref name="movedQty"/> from source onto sink.
    /// Prefer <paramref name="sourceBagBefore"/> from a Harmony prefix — source Itemstack
    /// may be null after a full drain.
    /// </summary>
    public static void MergeAfterMove(ItemStack? sink, ItemStack? source, int movedQty) =>
        MergeAfterMove(sink, source, movedQty, sourceBagBefore: null, sourceHadLive: false, sourceLiveBefore: null, sourceSizeBefore: -1);

    public static void MergeAfterMove(
        ItemStack? sink,
        ItemStack? source,
        int movedQty,
        IReadOnlyList<FrozenGroup>? sourceBagBefore,
        bool sourceHadLive,
        ProsequorBlob? sourceLiveBefore,
        int sourceSizeBefore)
    {
        if (sink?.Attributes == null || movedQty <= 0)
        {
            return;
        }

        AbsorbLegacySurface(sink);

        List<FrozenGroup> preMoveBag = BuildPreMoveSourceBag(
            source,
            sourceBagBefore,
            sourceHadLive,
            sourceLiveBefore,
            sourceSizeBefore,
            movedQty);

        // Liquids reduce instead of appending a Frozen bag.
        if (ProsequorLiquidPedigree.IsPortion(sink) || ProsequorLiquidPedigree.IsPortion(source))
        {
            MergeLiquidAfterMove(sink, source, movedQty, preMoveBag);
            return;
        }

        HashSet<string> preferred = CollectPreferredHashes(sink);

        if (preMoveBag.Count > 0 || sourceHadLive || (sourceBagBefore != null && sourceBagBefore.Count > 0))
        {
            // Clamp over-attributed bags only; shortfall means some moved units are untracked.
            int remain = Math.Max(0, source?.StackSize ?? 0);
            int needTotal = remain + movedQty;
            int total = TotalQty(preMoveBag);
            if (total > needTotal)
            {
                preMoveBag = PeelMinFragment(preMoveBag, needTotal, out _);
            }

            List<FrozenGroup> peeled = PeelPreferMatch(preMoveBag, movedQty, preferred, out List<FrozenGroup> leftover);
            ApplyMergePeel(sink, source, peeled, leftover);
            return;
        }

        // No source pedigree — moved units are untracked; do not invent Empty fillers.
        if (HasLive(sink) && sink.StackSize > 1)
        {
            FreezeLive(sink);
        }

        EnsureFrozenMatchesStackSize(sink);
    }

    static void MergeLiquidAfterMove(
        ItemStack sink,
        ItemStack? source,
        int movedQty,
        List<FrozenGroup> preMoveBag)
    {
        int sinkQtyBefore = Math.Max(0, sink.StackSize - movedQty);
        ProsequorBlob sourceBlob = ProsequorLiquidPedigree.CollapseGroups(preMoveBag);

        if (sinkQtyBefore <= 0)
        {
            ProsequorLiquidPedigree.WriteFullBlob(sink, sourceBlob);
        }
        else
        {
            ProsequorLiquidPedigree.ApplyMerge(sink, sourceBlob, sinkQtyBefore, movedQty);
        }

        if (source?.Attributes != null && source.StackSize > 0)
        {
            ProsequorLiquidPedigree.WriteFullBlob(source, sourceBlob);
        }
        else if (source?.Attributes != null)
        {
            ClearAll(source);
        }
    }

    static List<FrozenGroup> BuildPreMoveSourceBag(
        ItemStack? sourceAfter,
        IReadOnlyList<FrozenGroup>? sourceBagBefore,
        bool sourceHadLive,
        ProsequorBlob? sourceLiveBefore,
        int sourceSizeBefore,
        int movedQty)
    {
        if (sourceBagBefore != null && sourceBagBefore.Count > 0)
        {
            return new List<FrozenGroup>(sourceBagBefore);
        }

        if (sourceHadLive && sourceLiveBefore != null && sourceLiveBefore.HasPersistable)
        {
            int preSize = sourceSizeBefore > 0
                ? sourceSizeBefore
                : Math.Max(0, sourceAfter?.StackSize ?? 0) + movedQty;
            return new List<FrozenGroup> { new(sourceLiveBefore, Math.Max(1, preSize)) };
        }

        if (sourceAfter?.Attributes == null)
        {
            return new List<FrozenGroup>();
        }

        if (HasLive(sourceAfter))
        {
            ProsequorBlob liveBlob = ProsequorBlob.ReadFrom(sourceAfter.Attributes.GetTreeAttribute(LiveAttr));
            int remain = Math.Max(0, sourceAfter.StackSize);
            return new List<FrozenGroup> { new(liveBlob, remain + movedQty) };
        }

        return new List<FrozenGroup>(ReadFrozenGroups(sourceAfter));
    }

    static void ApplyMergePeel(
        ItemStack sink,
        ItemStack? source,
        List<FrozenGroup> peeled,
        List<FrozenGroup> leftover)
    {
        bool sinkHadPedigree = HasLive(sink) || HasFrozen(sink);
        if (HasLive(sink))
        {
            FreezeLive(sink);
        }

        List<FrozenGroup> sinkGroups = new(ReadFrozenGroups(sink));
        bool peeledMeaningful = false;
        foreach (FrozenGroup g in peeled)
        {
            if (g.Qty > 0 && g.Blob.HasPersistable)
            {
                peeledMeaningful = true;
                break;
            }
        }

        if (peeledMeaningful || sinkHadPedigree)
        {
            sinkGroups.AddRange(peeled);
            WriteFrozenGroups(sink, Coalesce(sinkGroups));
            SyncRootForSize(sink);
            EnsureFrozenMatchesStackSize(sink);
        }

        if (source?.Attributes != null)
        {
            WriteFrozenGroups(source, Coalesce(leftover));
            SyncRootForSize(source);
            EnsureFrozenMatchesStackSize(source);
        }
    }

    /// <summary>
    /// After <see cref="ItemSlot.TakeOut"/>: <paramref name="moved"/> is the returned stack;
    /// <paramref name="source"/> is what remains in the slot (may be null/empty).
    /// Prefix must snapshot source bag before TakeOut; pass that via <paramref name="sourceBagBefore"/>.
    /// </summary>
    public static void AfterTakeOut(
        ItemStack? sourceAfter,
        ItemStack? moved,
        IReadOnlyList<FrozenGroup>? sourceBagBefore,
        bool sourceHadLive,
        ProsequorBlob? sourceLiveBefore)
    {
        if (moved?.Attributes == null || moved.StackSize <= 0)
        {
            return;
        }

        int movedQty = moved.StackSize;
        List<FrozenGroup> before = sourceBagBefore == null
            ? new List<FrozenGroup>()
            : new List<FrozenGroup>(sourceBagBefore);

        if (sourceHadLive && sourceLiveBefore != null && sourceLiveBefore.HasPersistable)
        {
            // Entire size-1 live item moved (or was the only unit).
            before = new List<FrozenGroup> { new(sourceLiveBefore, Math.Max(movedQty, 1)) };
        }
        else if (TotalQty(before) == 0)
        {
            // Anonymous / no pedigree — nothing to copy.
            ClearAll(moved);
            if (sourceAfter?.Attributes != null)
            {
                ClearAll(sourceAfter);
            }

            return;
        }

        // Liquids stay one blob on both sides of a scoop.
        if (ProsequorLiquidPedigree.IsPortion(moved)
            || ProsequorLiquidPedigree.IsPortion(sourceAfter))
        {
            ProsequorBlob one = ProsequorLiquidPedigree.CollapseGroups(before);
            ProsequorLiquidPedigree.WriteFullBlob(moved, one);
            if (sourceAfter?.Attributes != null && sourceAfter.StackSize > 0)
            {
                ProsequorLiquidPedigree.WriteFullBlob(sourceAfter, one);
            }
            else if (sourceAfter?.Attributes != null)
            {
                ClearAll(sourceAfter);
            }

            return;
        }

        List<FrozenGroup> movedGroups = PeelMinFragment(before, movedQty, out List<FrozenGroup> leftover);
        WriteFrozenGroups(moved, Coalesce(movedGroups));
        SyncRootForSize(moved);

        if (sourceAfter?.Attributes != null && sourceAfter.StackSize > 0)
        {
            WriteFrozenGroups(sourceAfter, Coalesce(leftover));
            SyncRootForSize(sourceAfter);
            EnsureFrozenMatchesStackSize(sourceAfter);
        }
    }

    public static void FreezeLive(ItemStack? stack)
    {
        if (stack?.Attributes == null)
        {
            return;
        }

        ITreeAttribute? live = stack.Attributes.GetTreeAttribute(LiveAttr);
        if (live == null)
        {
            return;
        }

        ProsequorBlob blob = ProsequorBlob.ReadFrom(live);
        // Live always names one unit — never expand to StackSize here.
        List<FrozenGroup> groups = new(ReadFrozenGroups(stack))
        {
            new FrozenGroup(blob, 1)
        };
        WriteFrozenGroups(stack, Coalesce(groups));
        ClearLive(stack);
    }

    /// <summary>
    /// Write one unit's blob onto <paramref name="stack"/> as Live (size 1).
    /// When StackSize &gt; 1, stores Frozen blob×1 (shortfall allowed) — does not fill the bag.
    /// Use <see cref="DuplicatePrimaryToMatchStackSize"/> when extras should inherit attribution.
    /// </summary>
    public static void ApplyUnitBlob(ItemStack? stack, ProsequorBlob blob)
    {
        if (stack?.Attributes == null || !blob.HasPersistable || stack.StackSize <= 0)
        {
            return;
        }

        ClearAll(stack);
        if (stack.StackSize == 1)
        {
            blob.WriteTo(stack.Attributes.GetOrAddTreeAttribute(LiveAttr));
            MaterializePrimarySurface(stack);
            return;
        }

        WriteFrozenGroups(stack, new[] { new FrozenGroup(blob, 1) });
        ClearLive(stack);
        MaterializePrimarySurface(stack);
    }

    public static void PromoteToLive(ItemStack? stack)
    {
        if (stack?.Attributes == null || stack.StackSize != 1)
        {
            return;
        }

        IReadOnlyList<FrozenGroup> groups = ReadFrozenGroups(stack);
        ProsequorBlob blob = ProsequorBlob.Empty;
        foreach (FrozenGroup g in groups)
        {
            if (g.Qty > 0)
            {
                blob = g.Blob;
                break;
            }
        }

        ClearFrozen(stack);
        if (!blob.HasPersistable)
        {
            ClearLive(stack);
            return;
        }

        ITreeAttribute live = stack.Attributes.GetOrAddTreeAttribute(LiveAttr);
        blob.WriteTo(live);
        MaterializePrimarySurface(stack);
    }

    public static IReadOnlyList<FrozenGroup> ReadFrozenGroups(ItemStack? stack)
    {
        ITreeAttribute? tree = stack?.Attributes?.GetTreeAttribute(FrozenAttr);
        if (tree == null)
        {
            return Array.Empty<FrozenGroup>();
        }

        int n = tree.GetInt(GroupCountKey, 0);
        if (n <= 0)
        {
            return Array.Empty<FrozenGroup>();
        }

        List<FrozenGroup> list = new(n);
        for (int i = 0; i < n; i++)
        {
            ITreeAttribute? entry = tree.GetTreeAttribute(i.ToString());
            if (entry == null)
            {
                continue;
            }

            int qty = entry.GetInt(QtyKey, 0);
            if (qty <= 0)
            {
                continue;
            }

            ITreeAttribute? blobTree = entry.GetTreeAttribute(BlobKey);
            ProsequorBlob blob = ProsequorBlob.ReadFrom(blobTree);
            list.Add(new FrozenGroup(blob, qty));
        }

        return list;
    }

    public static void WriteFrozenGroups(ItemStack? stack, IReadOnlyList<FrozenGroup> groups)
    {
        if (stack?.Attributes == null)
        {
            return;
        }

        if (groups == null || groups.Count == 0 || TotalQty(groups) <= 0)
        {
            ClearFrozen(stack);
            return;
        }

        ITreeAttribute tree = stack.Attributes.GetOrAddTreeAttribute(FrozenAttr);
        int oldN = tree.GetInt(GroupCountKey, 0);
        for (int i = 0; i < oldN; i++)
        {
            tree.RemoveAttribute(i.ToString());
        }

        int written = 0;
        for (int i = 0; i < groups.Count; i++)
        {
            FrozenGroup g = groups[i];
            if (g.Qty <= 0)
            {
                continue;
            }

            ITreeAttribute entry = tree.GetOrAddTreeAttribute(written.ToString());
            entry.SetInt(QtyKey, g.Qty);
            entry.SetString(HashKey, g.Blob.ContentHash);
            ITreeAttribute blobTree = entry.GetOrAddTreeAttribute(BlobKey);
            g.Blob.WriteTo(blobTree);
            written++;
        }

        tree.SetInt(GroupCountKey, written);
        if (written == 0)
        {
            ClearFrozen(stack);
            return;
        }

        MaterializePrimarySurface(stack);
    }

    /// <summary>Coalesce adjacent equal hashes; also merge non-adjacent equals into first.</summary>
    public static List<FrozenGroup> Coalesce(IReadOnlyList<FrozenGroup> groups)
    {
        if (groups == null || groups.Count == 0)
        {
            return new List<FrozenGroup>();
        }

        Dictionary<string, int> qtyByHash = new(StringComparer.Ordinal);
        Dictionary<string, ProsequorBlob> blobByHash = new(StringComparer.Ordinal);
        List<string> order = new();

        foreach (FrozenGroup g in groups)
        {
            if (g.Qty <= 0)
            {
                continue;
            }

            string hash = g.Blob.ContentHash;
            if (!qtyByHash.ContainsKey(hash))
            {
                order.Add(hash);
                blobByHash[hash] = g.Blob;
                qtyByHash[hash] = 0;
            }

            qtyByHash[hash] += g.Qty;
        }

        List<FrozenGroup> result = new(order.Count);
        foreach (string hash in order)
        {
            result.Add(new FrozenGroup(blobByHash[hash], qtyByHash[hash]));
        }

        return result;
    }

    public static List<FrozenGroup> TakeFromFront(
        IReadOnlyList<FrozenGroup> groups,
        int qty,
        out List<FrozenGroup> leftover)
    {
        leftover = new List<FrozenGroup>();
        List<FrozenGroup> taken = new();
        if (qty <= 0 || groups == null || groups.Count == 0)
        {
            if (groups != null)
            {
                leftover.AddRange(groups);
            }

            return taken;
        }

        int need = qty;
        for (int i = 0; i < groups.Count; i++)
        {
            FrozenGroup g = groups[i];
            if (g.Qty <= 0)
            {
                continue;
            }

            if (need <= 0)
            {
                leftover.Add(g);
                continue;
            }

            if (g.Qty <= need)
            {
                taken.Add(g);
                need -= g.Qty;
            }
            else
            {
                taken.Add(new FrozenGroup(g.Blob, need));
                leftover.Add(new FrozenGroup(g.Blob, g.Qty - need));
                need = 0;
            }
        }

        return taken;
    }

    /// <summary>
    /// Peel for merges: prefer units whose content hash is in <paramref name="preferredHashes"/>,
    /// largest groups first; then fill from the rest (largest first, split only as needed).
    /// </summary>
    public static List<FrozenGroup> PeelPreferMatch(
        IReadOnlyList<FrozenGroup> groups,
        int qty,
        IReadOnlyCollection<string>? preferredHashes,
        out List<FrozenGroup> leftover)
    {
        leftover = new List<FrozenGroup>();
        List<FrozenGroup> taken = new();
        if (qty <= 0 || groups == null || groups.Count == 0)
        {
            if (groups != null)
            {
                leftover.AddRange(groups);
            }

            return taken;
        }

        HashSet<string> preferred = preferredHashes == null || preferredHashes.Count == 0
            ? new HashSet<string>(StringComparer.Ordinal)
            : new HashSet<string>(preferredHashes, StringComparer.Ordinal);

        // Mutable working copy.
        List<(ProsequorBlob Blob, int Qty)> work = new(groups.Count);
        foreach (FrozenGroup g in groups)
        {
            if (g.Qty > 0)
            {
                work.Add((g.Blob, g.Qty));
            }
        }

        int need = qty;
        need = TakeFromWork(work, need, taken, preferMatch: true, preferred);
        if (need > 0)
        {
            need = TakeFromWork(work, need, taken, preferMatch: false, preferred);
        }

        foreach ((ProsequorBlob blob, int q) in work)
        {
            if (q > 0)
            {
                leftover.Add(new FrozenGroup(blob, q));
            }
        }

        return Coalesce(taken);
    }

    /// <summary>
    /// Peel for splits: take whole groups that fit (largest fitting first), then split one group.
    /// </summary>
    public static List<FrozenGroup> PeelMinFragment(
        IReadOnlyList<FrozenGroup> groups,
        int qty,
        out List<FrozenGroup> leftover)
    {
        leftover = new List<FrozenGroup>();
        List<FrozenGroup> taken = new();
        if (qty <= 0 || groups == null || groups.Count == 0)
        {
            if (groups != null)
            {
                leftover.AddRange(groups);
            }

            return taken;
        }

        List<(ProsequorBlob Blob, int Qty)> work = new(groups.Count);
        foreach (FrozenGroup g in groups)
        {
            if (g.Qty > 0)
            {
                work.Add((g.Blob, g.Qty));
            }
        }

        int need = qty;
        while (need > 0)
        {
            int bestIdx = -1;
            int bestQty = -1;
            for (int i = 0; i < work.Count; i++)
            {
                int q = work[i].Qty;
                if (q <= 0 || q > need)
                {
                    continue;
                }

                if (q > bestQty)
                {
                    bestQty = q;
                    bestIdx = i;
                }
            }

            if (bestIdx < 0)
            {
                break;
            }

            taken.Add(new FrozenGroup(work[bestIdx].Blob, work[bestIdx].Qty));
            need -= work[bestIdx].Qty;
            work[bestIdx] = (work[bestIdx].Blob, 0);
        }

        if (need > 0)
        {
            int bestIdx = -1;
            int bestQty = -1;
            for (int i = 0; i < work.Count; i++)
            {
                int q = work[i].Qty;
                if (q <= 0)
                {
                    continue;
                }

                if (q > bestQty)
                {
                    bestQty = q;
                    bestIdx = i;
                }
            }

            if (bestIdx >= 0)
            {
                int take = Math.Min(need, work[bestIdx].Qty);
                taken.Add(new FrozenGroup(work[bestIdx].Blob, take));
                work[bestIdx] = (work[bestIdx].Blob, work[bestIdx].Qty - take);
                need -= take;
            }
        }

        foreach ((ProsequorBlob blob, int q) in work)
        {
            if (q > 0)
            {
                leftover.Add(new FrozenGroup(blob, q));
            }
        }

        return Coalesce(taken);
    }

    public static HashSet<string> CollectPreferredHashes(ItemStack? stack)
    {
        HashSet<string> set = new(StringComparer.Ordinal);
        if (stack?.Attributes == null)
        {
            return set;
        }

        ITreeAttribute? live = stack.Attributes.GetTreeAttribute(LiveAttr);
        if (live != null)
        {
            ProsequorBlob blob = ProsequorBlob.ReadFrom(live);
            if (blob.HasPersistable)
            {
                set.Add(blob.ContentHash);
            }
        }

        foreach (FrozenGroup g in ReadFrozenGroups(stack))
        {
            if (g.Qty > 0 && g.Blob.HasPersistable)
            {
                set.Add(g.Blob.ContentHash);
            }
        }

        return set;
    }

    static int TakeFromWork(
        List<(ProsequorBlob Blob, int Qty)> work,
        int need,
        List<FrozenGroup> taken,
        bool preferMatch,
        HashSet<string> preferred)
    {
        while (need > 0)
        {
            int bestIdx = -1;
            int bestQty = -1;
            for (int i = 0; i < work.Count; i++)
            {
                int q = work[i].Qty;
                if (q <= 0)
                {
                    continue;
                }

                bool isMatch = preferred.Contains(work[i].Blob.ContentHash);
                if (preferMatch != isMatch)
                {
                    continue;
                }

                if (q > bestQty)
                {
                    bestQty = q;
                    bestIdx = i;
                }
            }

            if (bestIdx < 0)
            {
                break;
            }

            int take = Math.Min(need, work[bestIdx].Qty);
            taken.Add(new FrozenGroup(work[bestIdx].Blob, take));
            work[bestIdx] = (work[bestIdx].Blob, work[bestIdx].Qty - take);
            need -= take;
        }

        return need;
    }

    public static int TotalQty(IReadOnlyList<FrozenGroup> groups)
    {
        int total = 0;
        if (groups == null)
        {
            return 0;
        }

        foreach (FrozenGroup g in groups)
        {
            total += Math.Max(0, g.Qty);
        }

        return total;
    }

    public static void ClearAll(ItemStack? stack)
    {
        ClearLive(stack);
        ClearFrozen(stack);
    }

    public static void ClearLive(ItemStack? stack)
    {
        if (stack?.Attributes == null || !stack.Attributes.HasAttribute(LiveAttr))
        {
            return;
        }

        stack.Attributes.RemoveAttribute(LiveAttr);
    }

    public static void ClearFrozen(ItemStack? stack)
    {
        if (stack?.Attributes == null || !stack.Attributes.HasAttribute(FrozenAttr))
        {
            return;
        }

        stack.Attributes.RemoveAttribute(FrozenAttr);
    }

    /// <summary>
    /// Pedigree must not block vanilla stack merges — bags exist so heterogeneous units can share a stack.
    /// Appends Live/Frozen plus craft surface trees to
    /// <see cref="GlobalConstants.IgnoredStackAttributes"/> once.
    /// </summary>
    public static void EnsurePedigreeIgnoredForMerge()
    {
        AppendIgnoredStackAttributes(
            LiveAttr,
            FrozenAttr,
            ItemAffixes.TreeAttr,
            CraftAttributeMods.TreeAttr);
    }

    static void AppendIgnoredStackAttributes(params string[] keys)
    {
        string[]? current = GlobalConstants.IgnoredStackAttributes;
        if (current == null || current.Length == 0)
        {
            GlobalConstants.IgnoredStackAttributes = keys;
            return;
        }

        List<string> next = new(current.Length + keys.Length);
        next.AddRange(current);
        bool added = false;
        for (int i = 0; i < keys.Length; i++)
        {
            string key = keys[i];
            bool found = false;
            for (int j = 0; j < current.Length; j++)
            {
                if (string.Equals(current[j], key, StringComparison.OrdinalIgnoreCase))
                {
                    found = true;
                    break;
                }
            }

            if (found)
            {
                continue;
            }

            next.Add(key);
            added = true;
        }

        if (added)
        {
            GlobalConstants.IgnoredStackAttributes = next.ToArray();
        }
    }

    /// <summary>
    /// Copy the stack's affix/mod trees onto every unit (craft stamp). Homogeneous
    /// and Live stacks only — mixed bags keep per-unit surface.
    /// </summary>
    public static void StampSurfaceFromStack(ItemStack? stack)
    {
        if (stack?.Attributes == null)
        {
            return;
        }

        IReadOnlyList<ItemAffixEntry> affixes = ItemAffixes.GetAll(stack);
        IReadOnlyList<ProsequorBlob.ModFactor> mods = CraftAttributeMods.GetAll(stack);
        if (affixes.Count == 0 && mods.Count == 0 && !HasLive(stack) && !HasFrozen(stack))
        {
            return;
        }

        if (HasFrozen(stack) && !IsHomogeneous(stack) && !IsLive(stack))
        {
            return;
        }

        MutateAllUnits(stack, b => b.WithAffixes(affixes).WithMods(mods));
    }

    /// <summary>
    /// Legacy stacks: fold stack-level affixes/mods into units that have none.
    /// </summary>
    public static void AbsorbLegacySurface(ItemStack? stack)
    {
        if (stack?.Attributes == null)
        {
            return;
        }

        IReadOnlyList<ItemAffixEntry> affixes = ItemAffixes.GetAll(stack);
        IReadOnlyList<ProsequorBlob.ModFactor> mods = CraftAttributeMods.GetAll(stack);
        if (affixes.Count == 0 && mods.Count == 0)
        {
            return;
        }

        if (!HasLive(stack) && !HasFrozen(stack))
        {
            MutateAllUnits(stack, b => b.WithAffixes(affixes).WithMods(mods));
            return;
        }

        bool changed = false;
        if (IsLive(stack))
        {
            ProsequorBlob blob = ProsequorBlob.ReadFrom(stack.Attributes.GetTreeAttribute(LiveAttr));
            ProsequorBlob next = AdoptSurface(blob, affixes, mods);
            if (next.ContentHash != blob.ContentHash || next.HasSurface != blob.HasSurface)
            {
                next.WriteTo(stack.Attributes.GetOrAddTreeAttribute(LiveAttr));
                changed = true;
            }
        }
        else
        {
            List<FrozenGroup> groups = new(ReadFrozenGroups(stack));
            for (int i = 0; i < groups.Count; i++)
            {
                FrozenGroup g = groups[i];
                ProsequorBlob next = AdoptSurface(g.Blob, affixes, mods);
                if (next.ContentHash == g.Blob.ContentHash && next.HasSurface == g.Blob.HasSurface)
                {
                    continue;
                }

                groups[i] = new FrozenGroup(next, g.Qty);
                changed = true;
            }

            if (changed)
            {
                WriteFrozenGroups(stack, Coalesce(groups));
            }
        }

        if (changed)
        {
            MaterializePrimarySurface(stack);
        }
    }

    static ProsequorBlob AdoptSurface(
        ProsequorBlob blob,
        IReadOnlyList<ItemAffixEntry> affixes,
        IReadOnlyList<ProsequorBlob.ModFactor> mods)
    {
        ProsequorBlob next = blob;
        if (next.Affixes.Count == 0 && affixes.Count > 0)
        {
            next = next.WithAffixes(affixes);
        }

        if (next.Mods.Count == 0 && mods.Count > 0)
        {
            next = next.WithMods(mods);
        }

        return next;
    }

    static void MaterializePrimarySurface(ItemStack stack)
    {
        if (!TryGetPrimaryBlob(stack, out ProsequorBlob blob))
        {
            return;
        }

        ItemAffixes.WriteAll(stack, blob.Affixes);
        CraftAttributeMods.WriteAll(stack, blob.Mods);
    }

    /// <summary>Snapshot for TakeOut prefix (Frozen groups + optional Live).</summary>
    public static void SnapshotForTakeOut(
        ItemStack? stack,
        out List<FrozenGroup> frozen,
        out bool hadLive,
        out ProsequorBlob? liveBlob)
    {
        frozen = new List<FrozenGroup>();
        hadLive = false;
        liveBlob = null;
        if (stack?.Attributes == null)
        {
            return;
        }

        AbsorbLegacySurface(stack);

        ITreeAttribute? live = stack.Attributes.GetTreeAttribute(LiveAttr);
        if (live != null)
        {
            hadLive = true;
            liveBlob = ProsequorBlob.ReadFrom(live);
        }

        frozen.AddRange(ReadFrozenGroups(stack));
        if (hadLive && frozen.Count == 0 && liveBlob != null)
        {
            frozen.Add(new FrozenGroup(liveBlob, Math.Max(1, stack.StackSize)));
        }
    }

    static void MutateAllUnits(ItemStack stack, System.Func<ProsequorBlob, ProsequorBlob> mutate)
    {
        int size = Math.Max(1, stack.StackSize);
        if (size == 1)
        {
            ClearFrozen(stack);
            ProsequorBlob current = ProsequorBlob.Empty;
            ITreeAttribute? live = stack.Attributes.GetTreeAttribute(LiveAttr);
            if (live != null)
            {
                current = ProsequorBlob.ReadFrom(live);
            }

            ProsequorBlob next = mutate(current);
            if (!next.HasPersistable)
            {
                ClearLive(stack);
                return;
            }

            next.WriteTo(stack.Attributes.GetOrAddTreeAttribute(LiveAttr));
            MaterializePrimarySurface(stack);
            return;
        }

        // Multi: expand to per-unit, mutate each, re-coalesce.
        if (HasLive(stack))
        {
            FreezeLive(stack);
        }

        List<FrozenGroup> groups = new(ReadFrozenGroups(stack));
        int total = TotalQty(groups);
        if (total < size)
        {
            groups.Add(new FrozenGroup(ProsequorBlob.Empty, size - total));
        }
        else if (total > size)
        {
            groups = PeelMinFragment(groups, size, out _);
        }

        List<FrozenGroup> expanded = new(size);
        foreach (FrozenGroup g in groups)
        {
            for (int i = 0; i < g.Qty; i++)
            {
                expanded.Add(new FrozenGroup(mutate(g.Blob), 1));
            }
        }

        WriteFrozenGroups(stack, Coalesce(expanded));
        ClearLive(stack);
        MaterializePrimarySurface(stack);
    }

    static void SyncRootForSize(ItemStack stack)
    {
        if (stack.StackSize == 1)
        {
            PromoteToLive(stack);
        }
        else
        {
            ClearLive(stack);
        }
    }
}
