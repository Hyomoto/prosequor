using Vintagestory.API.Common;

namespace Prosequor.Client;

public enum TooltipStatIconSide
{
    None,
    Leading,
    Trailing
}

/// <summary>
/// One column in the tooltip stats band. Weight shares the row width; content is centered in the column.
/// </summary>
public readonly record struct TooltipStatCell(
    string? Text,
    AssetLocation? Icon,
    TooltipStatIconSide IconSide,
    int Weight = 1,
    int? TintArgb = null,
    string? TextColorHex = null);

/// <summary>
/// Opt-in middle band between the title/preview header and <c>GetDescription</c> body.
/// Providers supply weighted cells; layout owns column widths and centering.
/// </summary>
public readonly record struct ItemTooltipStatsBandRequest(IReadOnlyList<TooltipStatCell> Cells);

/// <summary>
/// Registry for tooltip stats-band providers. First non-null request with at least one cell wins.
/// </summary>
public static class ItemTooltipStatsBand
{
    public const int MaxCells = 6;

    /// <summary>Default unscaled band height when measuring before compose (icon + pad).</summary>
    public const double DefaultRowHeight = 22;

    public delegate ItemTooltipStatsBandRequest? Provider(ItemSlot slot);

    static readonly object Gate = new();
    static readonly List<Provider> Providers = new();

    public static void Register(Provider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        lock (Gate)
        {
            Providers.Add(provider);
        }
    }

    public static void ClearProviders()
    {
        lock (Gate)
        {
            Providers.Clear();
        }
    }

    public static bool TryResolve(ItemSlot? slot, out ItemTooltipStatsBandRequest request)
    {
        request = default;
        if (slot?.Itemstack == null)
        {
            return false;
        }

        lock (Gate)
        {
            foreach (Provider provider in Providers)
            {
                ItemTooltipStatsBandRequest? resolved = provider(slot);
                if (resolved is not { } band || band.Cells == null || band.Cells.Count == 0)
                {
                    continue;
                }

                request = band;
                return true;
            }
        }

        return false;
    }
}
