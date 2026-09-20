using Prosequor.Xp;
using Xunit;

namespace Prosequor.Pure.Tests;

/// <summary>Pure fixtures for crop lifetime duration math.</summary>
public static class CropLifetimeFixtures
{
    public static void VerifyAll()
    {
        VerifyMonthsScaleWithDaysPerMonth();
        VerifyLegacyDaysRescaleFromTwelve();
        VerifyZeroProps();
        VerifyGrowthStages();
    }

    static void VerifyMonthsScaleWithDaysPerMonth()
    {
        // 1.03 months × 9 = 9.27; × 30 = 30.9 — ratio cancels in catalog lerp.
        float shortMonth = CropLifetimeMath.TotalGrowthDays(0f, 1.03f, 9f);
        float longMonth = CropLifetimeMath.TotalGrowthDays(0f, 1.03f, 30f);
        if (Math.Abs(shortMonth - 9.27f) > 0.0001f
            || Math.Abs(longMonth - 30.9f) > 0.0001f
            || Math.Abs(longMonth / shortMonth - 30f / 9f) > 0.0001f)
        {
            Assert.Fail("[prosequor] Crop months should scale linearly with DaysPerMonth.");
        }
    }

    static void VerifyLegacyDaysRescaleFromTwelve()
    {
        // 6 days on a 12-day month → 0.5 months → 15 days at 30 dpm.
        float days = CropLifetimeMath.TotalGrowthDays(6f, 0f, 30f);
        if (Math.Abs(days - 15f) > 0.0001f)
        {
            Assert.Fail("[prosequor] Legacy TotalGrowthDays must rescale from a 12-day month.");
        }

        // When days > 0, months are ignored.
        float ignoreMonths = CropLifetimeMath.TotalGrowthDays(6f, 99f, 30f);
        if (Math.Abs(ignoreMonths - 15f) > 0.0001f)
        {
            Assert.Fail("[prosequor] Positive TotalGrowthDays should ignore TotalGrowthMonths.");
        }
    }

    static void VerifyZeroProps()
    {
        if (CropLifetimeMath.TotalGrowthDays(0f, 0f, 9f) != 0f
            || CropLifetimeMath.TotalGrowthDays(1f, 1f, 0f) != 0f
            || CropLifetimeMath.TotalGrowthDays(null, 9f) != 0f)
        {
            Assert.Fail("[prosequor] Missing crop duration should be zero.");
        }
    }

    static void VerifyGrowthStages()
    {
        if (CropLifetimeMath.GrowthStages(null) != 0)
        {
            Assert.Fail("[prosequor] Null crop props should report 0 growth stages.");
        }
    }
}
