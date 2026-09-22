using Cairo;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace Prosequor.Client;

/// <summary>
/// Weighted-column stats strip for item tooltips. Each cell sits in its own rounded inset;
/// content is centered in the column. Painted onto the tooltip chrome Cairo surface
/// (same generation that clears <c>Dirty</c>), not as a GPU overlay.
/// </summary>
public class GuiElementTooltipStatsBand : GuiElement
{
    public const double IconUnscaled = 16;
    public const double IconTextGap = 4;
    public const double VerticalPad = 4;
    /// <summary>Unscaled padding inside each column inset (keeps text off the stroke).</summary>
    public const double HorizontalPad = 8;
    /// <summary>Unscaled gap between adjacent column insets.</summary>
    public const double ColumnGap = 4;
    public const double InsetRadius = 6;

    static readonly double[] InsetStrokeColor = [0.72, 0.62, 0.42, 0.95];

    static bool loggedCellCap;

    readonly ICoreClientAPI capi;
    readonly StyledSvgIconCache icons;
    readonly List<TooltipStatCell> cells = new();
    CairoFont textFont;

    public GuiElementTooltipStatsBand(ICoreClientAPI capi, ElementBounds bounds, StyledSvgIconCache icons)
        : base(capi, bounds)
    {
        this.capi = capi;
        this.icons = icons;
        textFont = CairoFont.WhiteDetailText();
    }

    /// <summary>True when the band has at least one cell to paint.</summary>
    public bool HasCells => cells.Count > 0;

    /// <summary>Unscaled height reserved for the band given current cells (0 when empty).</summary>
    public double PreferredUnscaledHeight
    {
        get
        {
            if (cells.Count == 0)
            {
                return 0;
            }

            double line = textFont.GetFontExtents().Height / RuntimeEnv.GUIScale;
            double icon = IconUnscaled;
            return Math.Max(line, icon) + VerticalPad * 2;
        }
    }

    /// <summary>
    /// Unscaled band width needed so every weighted column fits its icon/text cluster
    /// (plus inset padding). Used to widen short-name tooltips.
    /// </summary>
    public double PreferredUnscaledMinWidth
    {
        get
        {
            if (cells.Count == 0)
            {
                return 0;
            }

            int weightSum = 0;
            for (int i = 0; i < cells.Count; i++)
            {
                weightSum += Math.Max(1, cells[i].Weight);
            }

            if (weightSum <= 0)
            {
                return 0;
            }

            // Weighted share: colW_i = usable * w_i / sum. Require colW_i >= content_i for all i
            // ⇒ usable >= max_i (content_i * sum / w_i).
            double maxUsable = 0;
            for (int i = 0; i < cells.Count; i++)
            {
                int w = Math.Max(1, cells[i].Weight);
                double content = MeasureCellContentUnscaled(cells[i]) + HorizontalPad * 2;
                maxUsable = Math.Max(maxUsable, content * weightSum / w);
            }

            return maxUsable + ColumnGap * (cells.Count - 1);
        }
    }

    double MeasureCellContentUnscaled(TooltipStatCell cell)
    {
        double textW = 0;
        if (!string.IsNullOrEmpty(cell.Text))
        {
            textW = textFont.GetTextExtents(cell.Text).Width / RuntimeEnv.GUIScale;
        }

        bool showIcon = cell.Icon != null;
        if (showIcon && textW > 0)
        {
            return IconUnscaled + IconTextGap + textW;
        }

        if (showIcon)
        {
            return IconUnscaled;
        }

        return textW;
    }

    public void SetCells(IReadOnlyList<TooltipStatCell>? next)
    {
        cells.Clear();
        if (next == null || next.Count == 0)
        {
            Bounds.fixedHeight = 0;
            return;
        }

        int take = Math.Min(next.Count, ItemTooltipStatsBand.MaxCells);
        if (next.Count > ItemTooltipStatsBand.MaxCells && !loggedCellCap)
        {
            loggedCellCap = true;
            capi.Logger.Warning(
                "[prosequor] Tooltip stats band capped at {0} cells (got {1}).",
                ItemTooltipStatsBand.MaxCells,
                next.Count);
        }

        for (int i = 0; i < take; i++)
        {
            cells.Add(next[i]);
        }
    }

    /// <summary>
    /// Paint the band onto the tooltip chrome surface at the given pixel origin and size.
    /// Uses the caller-supplied size (from <c>RecalcZoneBounds</c>), not <see cref="ElementBounds.InnerWidth"/>.
    /// Returns false when cells exist but painting cannot complete — caller must keep Dirty.
    /// </summary>
    public bool Paint(Context ctx, double originX, double originY, double bandW, double bandH)
    {
        if (cells.Count == 0)
        {
            return true;
        }

        if (bandW <= 1 || bandH <= 1)
        {
            return false;
        }

        if (!TryGetColumnLayout(bandW, out int weightSum, out double gapPx))
        {
            return false;
        }

        double stroke = GuiElement.scaled(GuiElementRoundedInset.DefaultStrokeWidth);
        double half = stroke / 2;
        double usable = bandW - gapPx * (cells.Count - 1);
        double iconPx = GuiElement.scaled(IconUnscaled);
        double textGap = GuiElement.scaled(IconTextGap);
        double xCursor = originX;
        double yCenter = originY + bandH / 2;

        for (int i = 0; i < cells.Count; i++)
        {
            TooltipStatCell cell = cells[i];
            int w = Math.Max(1, cell.Weight);
            double colW = usable * w / weightSum;
            double boxW = colW - stroke;
            double boxH = bandH - stroke;
            if (boxW > 0 && boxH > 0)
            {
                double r = Math.Min(GuiElement.scaled(InsetRadius), Math.Min(boxW, boxH) / 2);
                var geo = new RoundedInsetGeometry
                {
                    X = xCursor + half,
                    Y = originY + half,
                    W = boxW,
                    H = boxH,
                    R = r
                };
                RoundedInsetDraw.DrawFill(ctx, geo, GuiElementRoundedInset.DefaultFillColor);
                RoundedInsetDraw.DrawStroke(
                    ctx, geo, InsetStrokeColor, GuiElementRoundedInset.DefaultStrokeWidth);
            }

            bool showIcon = cell.Icon != null;
            bool showText = !string.IsNullOrEmpty(cell.Text);
            double textW = 0;
            double textH = 0;
            FontExtents fontExtents = default;
            if (showText)
            {
                TextExtents extents = textFont.GetTextExtents(cell.Text!);
                textW = extents.Width;
                fontExtents = textFont.GetFontExtents();
                textH = fontExtents.Height;
            }

            double iconDraw = showIcon ? iconPx : 0;
            double clusterW = 0;
            if (showIcon && showText)
            {
                clusterW = iconDraw + textGap + textW;
            }
            else if (showIcon)
            {
                clusterW = iconDraw;
            }
            else if (showText)
            {
                clusterW = textW;
            }

            double clusterX = xCursor + (colW - clusterW) / 2;
            bool iconLeading = cell.IconSide != TooltipStatIconSide.Trailing;

            if (showIcon && showText)
            {
                if (iconLeading)
                {
                    icons.TryPaint(ctx, cell.Icon!, clusterX, yCenter - iconDraw / 2, iconDraw, cell.TintArgb);
                    DrawText(ctx, cell, clusterX + iconDraw + textGap, yCenter, textH, fontExtents);
                }
                else
                {
                    DrawText(ctx, cell, clusterX, yCenter, textH, fontExtents);
                    icons.TryPaint(
                        ctx,
                        cell.Icon!,
                        clusterX + textW + textGap,
                        yCenter - iconDraw / 2,
                        iconDraw,
                        cell.TintArgb);
                }
            }
            else if (showIcon)
            {
                icons.TryPaint(ctx, cell.Icon!, clusterX, yCenter - iconDraw / 2, iconDraw, cell.TintArgb);
            }
            else if (showText)
            {
                DrawText(ctx, cell, clusterX, yCenter, textH, fontExtents);
            }

            xCursor += colW + gapPx;
        }

        return true;
    }

    void DrawText(Context ctx, TooltipStatCell cell, double x, double yCenter, double textH, FontExtents fontExtents)
    {
        CairoFont font = textFont.Clone();
        if (!string.IsNullOrWhiteSpace(cell.TextColorHex))
        {
            try
            {
                font.Color = ColorUtil.Hex2Doubles(cell.TextColorHex);
            }
            catch
            {
                // Keep default dialog text color when hex is malformed.
            }
        }

        font.SetupContext(ctx);
        double baseline = yCenter - textH / 2 + fontExtents.Ascent;
        ctx.MoveTo(x, baseline);
        ctx.ShowText(cell.Text!);
    }

    bool TryGetColumnLayout(double bandW, out int weightSum, out double gapPx)
    {
        weightSum = 0;
        gapPx = GuiElement.scaled(ColumnGap);
        for (int i = 0; i < cells.Count; i++)
        {
            weightSum += Math.Max(1, cells[i].Weight);
        }

        if (weightSum <= 0 || bandW <= gapPx * (cells.Count - 1))
        {
            return false;
        }

        return true;
    }

    public override void ComposeElements(Context ctx, ImageSurface surface)
    {
    }

    public override void Dispose()
    {
        cells.Clear();
        base.Dispose();
    }
}
