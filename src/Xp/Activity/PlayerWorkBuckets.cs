namespace Prosequor.Xp.Activity;

/// <summary>
/// Spreads online player UIDs across a fixed number of work buckets so a tick listener can
/// process one slice per callback while keeping ~500ms per-player cadence.
/// </summary>
public sealed class PlayerWorkBuckets
{
    public const int BucketCount = 8;
    public const int TargetCycleMs = 500;
    public const int TickMs = TargetCycleMs / BucketCount;

    /// <summary>Drain every 4 slice visits (~2s at the 500ms per-player cycle).</summary>
    public const int DrainVisitMask = 3;

    /// <summary>
    /// Staggered ModData flush period (64 visits ≈ 32s). Combined with the in-slice index
    /// so a bucket of many players does not write them all on one callback.
    /// </summary>
    public const int FlushVisitMask = 63;

    readonly List<string>[] buckets;
    readonly int[] generation;
    readonly Dictionary<string, int> bucketByUid = new(StringComparer.Ordinal);
    int cursor;

    public PlayerWorkBuckets()
    {
        buckets = new List<string>[BucketCount];
        generation = new int[BucketCount];
        for (int i = 0; i < BucketCount; i++)
        {
            buckets[i] = new List<string>();
        }
    }

    public int Cursor => cursor;

    /// <summary>How many times the current slice has already been served. First visit is 0.</summary>
    public int CurrentGeneration => generation[cursor];

    public int Count => bucketByUid.Count;

    /// <summary>Calendar drain is due for everyone in the slice on this generation.</summary>
    public static bool ShouldDrain(int generation) => (generation & DrainVisitMask) == 0;

    /// <summary>
    /// One index in 0..63 is due per generation. Evaluated on every visit, not only drain gens,
    /// or indices that are not multiples of 4 never flush.
    /// </summary>
    public static bool ShouldFlush(int generation, int index) =>
        ((generation + index) & FlushVisitMask) == 0;

    public IReadOnlyList<string> Current() => buckets[cursor];

    /// <summary>Count this visit against the current slice, then rotate the cursor.</summary>
    public void Advance()
    {
        generation[cursor]++;
        cursor = (cursor + 1) % BucketCount;
    }

    public bool Contains(string uid) =>
        !string.IsNullOrEmpty(uid) && bucketByUid.ContainsKey(uid);

    public int BucketOf(string uid) =>
        bucketByUid.TryGetValue(uid, out int index) ? index : -1;

    public int BucketSize(int index) =>
        index >= 0 && index < BucketCount ? buckets[index].Count : 0;

    /// <summary>Place <paramref name="uid"/> in a current smallest bucket (lowest index on ties).</summary>
    public void Add(string uid)
    {
        if (string.IsNullOrEmpty(uid) || bucketByUid.ContainsKey(uid))
        {
            return;
        }

        int target = IndexOfSmallest();
        buckets[target].Add(uid);
        bucketByUid[uid] = target;
    }

    /// <summary>
    /// Drop <paramref name="uid"/> and, if sizes differ by more than one, move one UID from a
    /// largest bucket into a smallest (lexicographically first donor UID).
    /// </summary>
    public void Remove(string uid)
    {
        if (string.IsNullOrEmpty(uid) || !bucketByUid.TryGetValue(uid, out int index))
        {
            return;
        }

        buckets[index].Remove(uid);
        bucketByUid.Remove(uid);
        RebalanceIfNeeded();
    }

    void RebalanceIfNeeded()
    {
        while (true)
        {
            int maxIndex = IndexOfLargest();
            int minIndex = IndexOfSmallest();
            if (buckets[maxIndex].Count - buckets[minIndex].Count <= 1)
            {
                return;
            }

            string moveUid = LexicographicallyFirst(buckets[maxIndex]);
            buckets[maxIndex].Remove(moveUid);
            buckets[minIndex].Add(moveUid);
            bucketByUid[moveUid] = minIndex;
        }
    }

    int IndexOfSmallest()
    {
        int best = 0;
        int bestCount = buckets[0].Count;
        for (int i = 1; i < BucketCount; i++)
        {
            int count = buckets[i].Count;
            if (count < bestCount)
            {
                best = i;
                bestCount = count;
            }
        }

        return best;
    }

    int IndexOfLargest()
    {
        int best = 0;
        int bestCount = buckets[0].Count;
        for (int i = 1; i < BucketCount; i++)
        {
            int count = buckets[i].Count;
            if (count > bestCount)
            {
                best = i;
                bestCount = count;
            }
        }

        return best;
    }

    static string LexicographicallyFirst(List<string> uids)
    {
        string best = uids[0];
        for (int i = 1; i < uids.Count; i++)
        {
            if (string.CompareOrdinal(uids[i], best) < 0)
            {
                best = uids[i];
            }
        }

        return best;
    }
}
