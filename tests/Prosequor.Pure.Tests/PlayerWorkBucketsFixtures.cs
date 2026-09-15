using Prosequor.Xp.Activity;
using Xunit;

namespace Prosequor.Xp;

/// <summary>Player work-bucket roster / rotation fixtures (no world required).</summary>
public static class PlayerWorkBucketsFixtures
{
    public static void VerifyAll()
    {
        VerifyFortyPlayersFivePerBucket();
        VerifyAddLandsOnSmallest();
        VerifyRemoveRebalances();
        VerifyRotationVisitsEveryBucketIncludingEmpty();
        VerifyDuplicateAddAndUnknownRemoveAreNoOps();
        VerifyDrainEveryFourthGeneration();
        VerifyFlushStaggerDoesNotStarveOddIndices();
    }

    static void VerifyFortyPlayersFivePerBucket()
    {
        PlayerWorkBuckets buckets = new();
        for (int i = 0; i < 40; i++)
        {
            buckets.Add($"p{i:D2}");
        }

        if (buckets.Count != 40)
        {
            Assert.Fail("[prosequor] PlayerWorkBuckets fixture failed (40-player count).");
        }

        for (int b = 0; b < PlayerWorkBuckets.BucketCount; b++)
        {
            if (buckets.BucketSize(b) != 5)
            {
                Assert.Fail("[prosequor] PlayerWorkBuckets fixture failed (40 players → 5 per bucket).");
            }
        }
    }

    static void VerifyAddLandsOnSmallest()
    {
        PlayerWorkBuckets buckets = new();
        buckets.Add("a");
        buckets.Add("b");
        buckets.Add("c");
        // Fill prefers lowest index when tied: 0, then 1, then 2.
        if (buckets.BucketOf("a") != 0
            || buckets.BucketOf("b") != 1
            || buckets.BucketOf("c") != 2)
        {
            Assert.Fail("[prosequor] PlayerWorkBuckets fixture failed (add lands on smallest / lowest index).");
        }

        // Buckets 3–7 are still empty (size 0), so the next add must take bucket 3, not revisit 0.
        buckets.Add("d");
        if (buckets.BucketOf("d") != 3 || buckets.BucketSize(3) != 1 || buckets.BucketSize(0) != 1)
        {
            Assert.Fail("[prosequor] PlayerWorkBuckets fixture failed (fourth add lands on next empty bucket).");
        }

        // After filling all eight once, the ninth add returns to bucket 0.
        for (int i = 4; i < PlayerWorkBuckets.BucketCount; i++)
        {
            buckets.Add($"e{i}");
        }

        buckets.Add("ninth");
        if (buckets.BucketOf("ninth") != 0 || buckets.BucketSize(0) != 2)
        {
            Assert.Fail("[prosequor] PlayerWorkBuckets fixture failed (ninth add returns to bucket 0).");
        }
    }

    static void VerifyRemoveRebalances()
    {
        PlayerWorkBuckets buckets = new();
        for (int i = 0; i < 9; i++)
        {
            buckets.Add($"u{i}");
        }

        // 9 players → sizes 2,1,1,1,1,1,1,1 (bucket 0 has 2).
        if (buckets.BucketSize(0) != 2)
        {
            Assert.Fail("[prosequor] PlayerWorkBuckets fixture failed (9-player initial spread).");
        }

        // Remove a singleton so max-min would be 2 without rebalance.
        string singleton = FirstInBucket(buckets, 1);
        buckets.Remove(singleton);

        int min = int.MaxValue;
        int max = 0;
        for (int b = 0; b < PlayerWorkBuckets.BucketCount; b++)
        {
            int size = buckets.BucketSize(b);
            if (size < min)
            {
                min = size;
            }

            if (size > max)
            {
                max = size;
            }
        }

        if (max - min > 1 || buckets.Count != 8 || buckets.Contains(singleton))
        {
            Assert.Fail("[prosequor] PlayerWorkBuckets fixture failed (remove then rebalance).");
        }
    }

    static void VerifyRotationVisitsEveryBucketIncludingEmpty()
    {
        PlayerWorkBuckets buckets = new();
        buckets.Add("only");
        // Only bucket 0 has a player; empty buckets must still consume a tick slot.
        int[] sizesSeen = new int[PlayerWorkBuckets.BucketCount];
        for (int i = 0; i < PlayerWorkBuckets.BucketCount; i++)
        {
            sizesSeen[buckets.Cursor] = buckets.Current().Count;
            buckets.Advance();
        }

        if (sizesSeen[0] != 1)
        {
            Assert.Fail("[prosequor] PlayerWorkBuckets fixture failed (bucket 0 should be visited with 1 uid).");
        }

        for (int b = 1; b < PlayerWorkBuckets.BucketCount; b++)
        {
            if (sizesSeen[b] != 0)
            {
                Assert.Fail("[prosequor] PlayerWorkBuckets fixture failed (empty buckets must still be visited).");
            }
        }

        if (buckets.Cursor != 0)
        {
            Assert.Fail("[prosequor] PlayerWorkBuckets fixture failed (8 advances return cursor to 0).");
        }
    }

    static void VerifyDuplicateAddAndUnknownRemoveAreNoOps()
    {
        PlayerWorkBuckets buckets = new();
        buckets.Add("x");
        buckets.Add("x");
        if (buckets.Count != 1 || buckets.BucketSize(0) != 1)
        {
            Assert.Fail("[prosequor] PlayerWorkBuckets fixture failed (duplicate add must be no-op).");
        }

        buckets.Remove("missing");
        if (buckets.Count != 1 || !buckets.Contains("x"))
        {
            Assert.Fail("[prosequor] PlayerWorkBuckets fixture failed (unknown remove must be no-op).");
        }
    }

    static void VerifyDrainEveryFourthGeneration()
    {
        for (int gen = 0; gen < 16; gen++)
        {
            bool drain = PlayerWorkBuckets.ShouldDrain(gen);
            bool expected = (gen & PlayerWorkBuckets.DrainVisitMask) == 0;
            if (drain != expected || drain != (gen % 4 == 0))
            {
                Assert.Fail($"[prosequor] PlayerWorkBuckets fixture failed (drain at gen {gen}).");
            }
        }
    }

    static void VerifyFlushStaggerDoesNotStarveOddIndices()
    {
        const int players = 12;
        int[] hits = new int[players];
        int period = PlayerWorkBuckets.FlushVisitMask + 1;
        for (int gen = 0; gen < period; gen++)
        {
            int due = 0;
            for (int i = 0; i < players; i++)
            {
                if (!PlayerWorkBuckets.ShouldFlush(gen, i))
                {
                    continue;
                }

                due++;
                hits[i]++;
            }

            if (due > 1)
            {
                Assert.Fail($"[prosequor] PlayerWorkBuckets fixture failed (more than one flush at gen {gen}).");
            }
        }

        for (int i = 0; i < players; i++)
        {
            if (hits[i] != 1)
            {
                Assert.Fail($"[prosequor] PlayerWorkBuckets fixture failed (index {i} flushed {hits[i]} times in {period} gens).");
            }
        }
    }

    static string FirstInBucket(PlayerWorkBuckets buckets, int index)
    {
        // Rebuild by probing known adds — fixtures only use sequential uids.
        for (int i = 0; i < 64; i++)
        {
            string uid = $"u{i}";
            if (buckets.BucketOf(uid) == index)
            {
                return uid;
            }
        }

        Assert.Fail($"[prosequor] PlayerWorkBuckets fixture failed (no uid in bucket {index}).");
        return "";
    }
}
