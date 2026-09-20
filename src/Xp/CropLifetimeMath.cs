using Vintagestory.API.Common;

namespace Prosequor.Xp;

/// <summary>
/// Pure crop growth-duration math matching vanilla
/// <c>BlockEntityFarmland.GetHoursForNextStage</c> before stage split and speed rolls.
/// </summary>
public static class CropLifetimeMath
{
    /// <summary>
    /// Total calendar days for a full plant. Legacy <paramref name="totalGrowthDays"/>
    /// (when &gt; 0) is treated as days-on-a-12-day-month and rescaled to
    /// <paramref name="daysPerMonth"/>; otherwise months × days-per-month.
    /// </summary>
    public static float TotalGrowthDays(float totalGrowthDays, float totalGrowthMonths, float daysPerMonth)
    {
        if (daysPerMonth <= 0f)
        {
            return 0f;
        }

        if (totalGrowthDays > 0f)
        {
            return (totalGrowthDays / 12f) * daysPerMonth;
        }

        if (totalGrowthMonths <= 0f)
        {
            return 0f;
        }

        return totalGrowthMonths * daysPerMonth;
    }

    /// <summary>Vanilla <see cref="BlockCropProperties"/> → total calendar days.</summary>
    public static float TotalGrowthDays(BlockCropProperties? props, float daysPerMonth)
    {
        if (props == null)
        {
            return 0f;
        }

        return TotalGrowthDays(props.TotalGrowthDays, props.TotalGrowthMonths, daysPerMonth);
    }

    /// <summary>Stage count for lifetime split; missing props → 0 (grant path uses max(1, stages)).</summary>
    public static int GrowthStages(BlockCropProperties? props) =>
        props == null ? 0 : Math.Max(0, props.GrowthStages);
}
