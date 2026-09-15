using Prosequor.Xp;
using Xunit;

namespace Prosequor.Pure.Tests;

/// <summary>Pure fixtures for shared amount-table math and block-break hardness bands.</summary>
public static class AmountTableFixtures
{
    public static void VerifyAll()
    {
        VerifyFloatBands();
        VerifyPickIndex();
        VerifyLerp();
        VerifyClayDelegation();
        VerifyMissingMetricUsesFirst();
        VerifyScalarResolve();
    }

    static void VerifyFloatBands()
    {
        float[] table = [1f, 5f, 10f];
        // Dig-like: min 1.2, max 4 (clay). Soil 1.8 → low; gravel 2.4 → mid; clay 4 → high.
        if (AmountTableMath.PickAmount(table, 1.8f, 1.2f, 4f) != 1f
            || AmountTableMath.PickAmount(table, 2.4f, 1.2f, 4f) != 5f
            || AmountTableMath.PickAmount(table, 4f, 1.2f, 4f) != 10f)
        {
            Assert.Fail("[prosequor] Resistance amount table band pick failed.");
        }
    }

    static void VerifyPickIndex()
    {
        float[] table = [1f, 5f, 10f];
        if (AmountTableMath.PickIndex(3, 1.8f, 1.2f, 4f) != 0
            || AmountTableMath.PickIndex(3, 2.4f, 1.2f, 4f) != 1
            || AmountTableMath.PickIndex(3, 4f, 1.2f, 4f) != 2
            || AmountTableMath.PickAmount(table, 2.4f, 1.2f, 4f)
                != table[AmountTableMath.PickIndex(3, 2.4f, 1.2f, 4f)])
        {
            Assert.Fail("[prosequor] PickIndex should match PickAmount bands.");
        }
    }

    static void VerifyLerp()
    {
        float[] table = [0.1f, 16f];
        // Domain 1–40, 16 ingredients: t = 15/39, two knots → 0.1 + 15.9 * (15/39).
        float at16 = AmountTableMath.LerpAmount(table, 16f, 1f, 40f);
        float want = 0.1f + 15.9f * (15f / 39f);
        if (Math.Abs(at16 - want) > 0.0001f
            || Math.Abs(AmountTableMath.LerpAmount(table, 1f, 1f, 40f) - 0.1f) > 0.0001f
            || Math.Abs(AmountTableMath.LerpAmount(table, 40f, 1f, 40f) - 16f) > 0.0001f
            || Math.Abs(AmountTableMath.ResolveAmount(3f, table, 16f, 1f, 40f) - want) > 0.0001f)
        {
            Assert.Fail($"[prosequor] Amount table lerp failed: at16={at16} want={want}.");
        }
    }

    static void VerifyClayDelegation()
    {
        float[] table = [1f, 5f, 10f];
        if (ClayFireXpMath.PickAmount(table, 10, 10, 100) != 1f
            || ClayFireXpMath.PickAmount(table, 55, 10, 100) != 5f
            || ClayFireXpMath.PickAmount(table, 100, 10, 100) != 10f)
        {
            Assert.Fail("[prosequor] ClayFireXpMath should delegate to AmountTableMath.");
        }
    }

    static void VerifyMissingMetricUsesFirst()
    {
        float[] table = [1f, 5f, 10f];
        if (AmountTableMath.PickAmount(table, 0f, 1.2f, 4f) != 1f
            || AmountTableMath.ResolveAmount(3f, table, 0f, 1.2f, 4f) != 1f)
        {
            Assert.Fail("[prosequor] Missing hardness should pick table[0].");
        }
    }

    static void VerifyScalarResolve()
    {
        if (AmountTableMath.ResolveAmount(2f, null, 9f, 1f, 10f) != 2f)
        {
            Assert.Fail("[prosequor] Scalar resolve should ignore hardness.");
        }
    }
}
