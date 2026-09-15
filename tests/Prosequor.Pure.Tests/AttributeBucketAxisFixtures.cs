using Prosequor.Data;
using Xunit;

namespace Prosequor.Progress;

/// <summary>Median-centered stats-panel tick math (no world required).</summary>
public static class AttributeBucketAxisFixtures
{
    public static void VerifyAll()
    {
        VerifyAllEqualSitAtCenter();
        VerifySpreadAroundMedian();
        VerifyEvenCountMedian();
        VerifyMaxedExcludedAndHidden();
        VerifyAllMaxedHidden();
        VerifySingleEligibleCentered();
    }

    static void VerifyAllEqualSitAtCenter()
    {
        int[] scores = AllDefault();
        float[] buckets = [0f, 0f, 0f, 0f, 0f];
        float?[] ticks = AttributeBucketAxis.TickFractions(scores, buckets);
        if (!AllNear(ticks, 0.5f))
        {
            Assert.Fail("[prosequor] Attribute bucket axis fixture failed (all-zero center).");
        }

        buckets = [4f, 4f, 4f, 4f, 4f];
        ticks = AttributeBucketAxis.TickFractions(scores, buckets);
        if (!AllNear(ticks, 0.5f))
        {
            Assert.Fail("[prosequor] Attribute bucket axis fixture failed (tied center).");
        }
    }

    static void VerifySpreadAroundMedian()
    {
        // [0, 1, 5, 6, 10] median 5, maxDev 5 → 0, 0.1, 0.5, 0.6, 1
        float?[] ticks = AttributeBucketAxis.TickFractions(
            AllDefault(),
            [0f, 1f, 5f, 6f, 10f]);
        if (!Near(ticks, [0f, 0.1f, 0.5f, 0.6f, 1f]))
        {
            Assert.Fail("[prosequor] Attribute bucket axis fixture failed (median spread).");
        }
    }

    static void VerifyEvenCountMedian()
    {
        // Four eligible after hiding resilience: [0, 2, 8, 10] median 5, maxDev 5
        int[] scores =
        [
            AttributeGrowth.DefaultScore,
            AttributeGrowth.DefaultScore,
            AttributeGrowth.DefaultScore,
            AttributeGrowth.DefaultScore,
            AttributeGrowth.MaxScore
        ];
        float?[] ticks = AttributeBucketAxis.TickFractions(
            scores,
            [0f, 2f, 8f, 10f, 99f]);
        if (!Near(ticks, [0f, 0.2f, 0.8f, 1f, null]))
        {
            Assert.Fail("[prosequor] Attribute bucket axis fixture failed (even-count median).");
        }
    }

    static void VerifyMaxedExcludedAndHidden()
    {
        // Strength maxed with a leftover pile must not warp the race.
        // Eligible [0, 1, 5, 10] median 3, maxDev 7
        int[] scores =
        [
            AttributeGrowth.MaxScore,
            AttributeGrowth.DefaultScore,
            AttributeGrowth.DefaultScore,
            AttributeGrowth.DefaultScore,
            AttributeGrowth.DefaultScore
        ];
        float?[] ticks = AttributeBucketAxis.TickFractions(
            scores,
            [99f, 0f, 1f, 5f, 10f]);
        if (ticks[0] != null
            || !Near(ticks[1], 0.5f + 0.5f * (0f - 3f) / 7f)
            || !Near(ticks[2], 0.5f + 0.5f * (1f - 3f) / 7f)
            || !Near(ticks[3], 0.5f + 0.5f * (5f - 3f) / 7f)
            || !Near(ticks[4], 1f))
        {
            Assert.Fail("[prosequor] Attribute bucket axis fixture failed (maxed excluded).");
        }
    }

    static void VerifyAllMaxedHidden()
    {
        int[] scores =
        [
            AttributeGrowth.MaxScore,
            AttributeGrowth.MaxScore,
            AttributeGrowth.MaxScore,
            AttributeGrowth.MaxScore,
            AttributeGrowth.MaxScore
        ];
        float?[] ticks = AttributeBucketAxis.TickFractions(scores, [1f, 2f, 3f, 4f, 5f]);
        for (int i = 0; i < ticks.Length; i++)
        {
            if (ticks[i] != null)
            {
                Assert.Fail("[prosequor] Attribute bucket axis fixture failed (all-maxed hidden).");
                return;
            }
        }
    }

    static void VerifySingleEligibleCentered()
    {
        int[] scores =
        [
            AttributeGrowth.MaxScore,
            AttributeGrowth.MaxScore,
            AttributeGrowth.DefaultScore,
            AttributeGrowth.MaxScore,
            AttributeGrowth.MaxScore
        ];
        float?[] ticks = AttributeBucketAxis.TickFractions(scores, [9f, 8f, 1.5f, 7f, 6f]);
        if (ticks[0] != null
            || ticks[1] != null
            || !Near(ticks[2], 0.5f)
            || ticks[3] != null
            || ticks[4] != null)
        {
            Assert.Fail("[prosequor] Attribute bucket axis fixture failed (single eligible center).");
        }
    }

    static int[] AllDefault() =>
    [
        AttributeGrowth.DefaultScore,
        AttributeGrowth.DefaultScore,
        AttributeGrowth.DefaultScore,
        AttributeGrowth.DefaultScore,
        AttributeGrowth.DefaultScore
    ];

    static bool AllNear(float?[] ticks, float expected)
    {
        if (ticks.Length != AttributeIds.All.Length)
        {
            return false;
        }

        for (int i = 0; i < ticks.Length; i++)
        {
            if (!Near(ticks[i], expected))
            {
                return false;
            }
        }

        return true;
    }

    static bool Near(float?[] ticks, float?[] expected)
    {
        if (ticks.Length != expected.Length)
        {
            return false;
        }

        for (int i = 0; i < ticks.Length; i++)
        {
            if (!Near(ticks[i], expected[i]))
            {
                return false;
            }
        }

        return true;
    }

    static bool Near(float? actual, float? expected)
    {
        if (actual == null || expected == null)
        {
            return actual == null && expected == null;
        }

        return Math.Abs(actual.Value - expected.Value) < 0.0001f;
    }
}
