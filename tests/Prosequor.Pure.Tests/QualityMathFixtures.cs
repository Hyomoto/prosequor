using Prosequor.Ability;
using Prosequor.Xp;
using Xunit;

namespace Prosequor.Pure.Tests;

/// <summary>Pure fixtures for craft-quality ranks, lerp, and affix banding.</summary>
public static class QualityMathFixtures
{
    public static void VerifyAll()
    {
        VerifyToPointsClampAndSkip();
        VerifyGradeIndex();
        VerifyNegativeBonus();
        VerifyRollDegenerateWindow();
        VerifyRollTakesMax();
        VerifyLerpTable();
        VerifyAffixIndexMatchesPickIndex();
        VerifyBaseSeedAndRollCount();
    }

    static void VerifyToPointsClampAndSkip()
    {
        if (QualityMath.ToPoints(-50f, 0f, 0f) != 0
            || QualityMath.ToPoints(9.9f, 0f, 0f) != 0
            || QualityMath.ToPoints(10f, 0f, 0f) != 1
            || QualityMath.ToPoints(299f, 0f, 0f) != 29
            || QualityMath.ToPoints(300f, 0f, 0f) != 30
            || QualityMath.ToPoints(600f, 0f, 0f) != 30)
        {
            Assert.Fail("[prosequor] Quality ToPoints clamp / rank failed.");
        }
    }

    static void VerifyGradeIndex()
    {
        // mean/300 → 5-rung table (Nice … Masterful).
        if (QualityMath.GradeIndex(5, 0f) != 0
            || QualityMath.GradeIndex(5, 1f) != 0
            || QualityMath.GradeIndex(5, 75f) != 1
            || QualityMath.GradeIndex(5, 150f) != 2
            || QualityMath.GradeIndex(5, 225f) != 3
            || QualityMath.GradeIndex(5, 300f) != 4
            || Math.Abs(QualityMath.ClampMean(400f, 0f, 0f) - 300f) > 0.0001f)
        {
            Assert.Fail("[prosequor] Quality grade index / ClampMean failed.");
        }
    }

    static void VerifyNegativeBonus()
    {
        // Raw 100 → 10 points; rule −50 → 5 points; global −60 → skip (0).
        if (QualityMath.ToPoints(100f, -50f, 0f) != 5
            || QualityMath.ToPoints(100f, -50f, -60f) != 0)
        {
            Assert.Fail("[prosequor] Quality negative bonus failed.");
        }
    }

    static void VerifyRollDegenerateWindow()
    {
        Random rand = new(1);
        if (Math.Abs(QualityMath.RollRaw(40f, 0f, 5, rand) - 40f) > 0.0001f
            || Math.Abs(QualityMath.RollRaw(40f, -10f, 5, rand) - 40f) > 0.0001f)
        {
            Assert.Fail("[prosequor] Quality degenerate window should return base.");
        }
    }

    static void VerifyRollTakesMax()
    {
        // Fixed sequence: 0.1, 0.9, 0.2 → samples 10+20=30, 10+180=190, 10+40=50 → max 190.
        SequenceRandom rand = new([0.1, 0.9, 0.2]);
        float got = QualityMath.RollRaw(10f, 200f, 3, rand);
        if (Math.Abs(got - 190f) > 0.0001f)
        {
            Assert.Fail(string.Format(
                "[prosequor] Quality RollRaw max failed. got={0} want=190.",
                got));
        }
    }

    static void VerifyLerpTable()
    {
        float[] three = [0f, 0.05f, 0.1f];
        if (Math.Abs(QualityMath.LerpTable(three, 0) - 0f) > 0.0001f
            || Math.Abs(QualityMath.LerpTable(three, 15) - 0.05f) > 0.0001f
            || Math.Abs(QualityMath.LerpTable(three, 30) - 0.1f) > 0.0001f
            || Math.Abs(QualityMath.LerpTable(three, 12) - 0.04f) > 0.0001f
            || Math.Abs(QualityMath.LerpTable([0.2f], 40) - 0.2f) > 0.0001f
            || Math.Abs(QualityMath.LerpTable(Array.Empty<float>(), 10)) > 0.0001f)
        {
            Assert.Fail("[prosequor] Quality LerpTable failed (3-knot / edge).");
        }

        // Two knots: rank 0 → first, rank 30 → last, mid → halfway.
        float[] two = [0f, 0.2f];
        if (Math.Abs(QualityMath.LerpTable(two, 0) - 0f) > 0.0001f
            || Math.Abs(QualityMath.LerpTable(two, 30) - 0.2f) > 0.0001f
            || Math.Abs(QualityMath.LerpTable(two, 15) - 0.1f) > 0.0001f)
        {
            Assert.Fail("[prosequor] Quality LerpTable failed (2-knot).");
        }

        // Four knots evenly spaced on [0, 30]: indices at 0 / 10 / 20 / 30.
        float[] four = [1f, 5f, 6f, 12f];
        if (Math.Abs(QualityMath.LerpTable(four, 0) - 1f) > 0.0001f
            || Math.Abs(QualityMath.LerpTable(four, 10) - 5f) > 0.0001f
            || Math.Abs(QualityMath.LerpTable(four, 20) - 6f) > 0.0001f
            || Math.Abs(QualityMath.LerpTable(four, 30) - 12f) > 0.0001f
            || Math.Abs(QualityMath.LerpTable(four, 5) - 3f) > 0.0001f)
        {
            Assert.Fail("[prosequor] Quality LerpTable failed (4-knot).");
        }
    }

    static void VerifyAffixIndexMatchesPickIndex()
    {
        for (int points = 0; points <= 30; points += 5)
        {
            int want = AmountTableMath.PickIndex(3, points, QualityMath.RankMin, QualityMath.RankMax);
            int got = QualityMath.AffixIndex(3, points);
            if (got != want)
            {
                Assert.Fail(string.Format(
                    "[prosequor] Affix index drift at points={0}: got={1} want={2}.",
                    points,
                    got,
                    want));
            }
        }

        if (QualityMath.AffixIndex(3, 0) != 0
            || QualityMath.AffixIndex(3, 15) != 1
            || QualityMath.AffixIndex(3, 30) != 2)
        {
            Assert.Fail("[prosequor] Affix index knot mapping failed.");
        }

        // Slice [0,3] of 5: points 0 → 0, points 30 → 3. Slice [1,4]: 0 → 1, 30 → 4.
        if (QualityMath.AffixIndexInRange(5, 0, 0, 3) != 0
            || QualityMath.AffixIndexInRange(5, 30, 0, 3) != 3
            || QualityMath.AffixIndexInRange(5, 0, 1, 4) != 1
            || QualityMath.AffixIndexInRange(5, 30, 1, 4) != 4
            || QualityMath.AffixIndexInRange(5, 15, 0, 3) != QualityMath.AffixIndex(4, 15)
            || QualityMath.AffixIndexInRange(5, 15, 1, 4) != 1 + QualityMath.AffixIndex(4, 15))
        {
            Assert.Fail("[prosequor] AffixIndexInRange slice mapping failed.");
        }
    }

    static void VerifyBaseSeedAndRollCount()
    {
        if (Math.Abs(QualityMath.BaseSeed(0) - (-200f)) > 0.0001f
            || Math.Abs(QualityMath.BaseSeed(50) - (-150f)) > 0.0001f
            || QualityMath.ResolveRollCount(0.4f) != 1
            || QualityMath.ResolveRollCount(1.4f) != 1
            || QualityMath.ResolveRollCount(1.5f) != 2
            || QualityMath.ResolveRollCount(3.2f) != 3)
        {
            Assert.Fail("[prosequor] Quality base seed / roll count failed.");
        }
    }

    sealed class SequenceRandom : Random
    {
        readonly double[] values;
        int index;

        public SequenceRandom(double[] values) => this.values = values;

        public override double NextDouble()
        {
            if (index >= values.Length)
            {
                return 0.0;
            }

            return values[index++];
        }
    }
}
