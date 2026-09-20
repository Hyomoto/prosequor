using Prosequor.Ability;
using Vintagestory.API.Common;

namespace Prosequor.Xp;

/// <summary>
/// GameReady catalog of field-crop growth-day min/max. Amount tables on
/// <c>pay: lifetime</c> normalize against this span.
/// </summary>
public sealed class CropLifetimeCatalog
{
    public readonly record struct Range(float Min, float Max, int BlockCount);

    readonly Range? span;

    CropLifetimeCatalog(Range? span)
    {
        this.span = span;
    }

    public int BlockCount => span?.BlockCount ?? 0;

    public float Min => span?.Min ?? 0f;

    public float Max => span?.Max ?? 0f;

    public static CropLifetimeCatalog Build(ICoreAPI api)
    {
        if (api?.World?.Blocks == null)
        {
            return new CropLifetimeCatalog(null);
        }

        float daysPerMonth = (float)api.World.Calendar.DaysPerMonth;
        float min = float.MaxValue;
        float max = float.MinValue;
        int count = 0;

        foreach (Block block in api.World.Blocks)
        {
            if (block == null || block.Id == 0 || !AbilityBootstrap.IsCropBlock(block))
            {
                continue;
            }

            BlockCropProperties? props = block.CropProps;
            float days = CropLifetimeMath.TotalGrowthDays(props, daysPerMonth);
            if (days <= 0f)
            {
                continue;
            }

            min = Math.Min(min, days);
            max = Math.Max(max, days);
            count++;
        }

        if (count <= 0)
        {
            return new CropLifetimeCatalog(null);
        }

        return new CropLifetimeCatalog(new Range(min, max, count));
    }

    public bool TryGetRange(out float min, out float max)
    {
        if (span == null || span.Value.BlockCount <= 0)
        {
            min = 0f;
            max = 0f;
            return false;
        }

        min = span.Value.Min;
        max = span.Value.Max;
        return true;
    }

    public Range? TryGet() => span;
}
