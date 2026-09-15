using System.Diagnostics.CodeAnalysis;
using Cairo;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace Prosequor.Client;

/// <summary>
/// Weighted-column stats strip for item tooltips. Each cell sits in its own rounded inset;
/// content is centered in the column. Icons use <see cref="StyledSvgIconCache"/>.
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
    static bool loggedDisposedMesh;

    readonly ICoreClientAPI capi;
    readonly StyledSvgIconCache icons;
    readonly List<TooltipStatCell> cells = new();
    readonly List<LoadedTexture?> textTextures = new();

    CairoFont textFont;
    LoadedTexture? columnBackground;
    /// <summary>
    /// False after <see cref="SetCells"/> / <see cref="Dispose"/> until <see cref="Compose"/>
    /// finishes uploading GPU textures. The hover tooltip is double-buffered and
    /// recomposed asynchronously; drawing between those points crashes the client.
    /// </summary>
    bool ready;

    public GuiElementTooltipStatsBand(ICoreClientAPI capi, ElementBounds bounds, StyledSvgIconCache icons)
        : base(capi, bounds)
    {
        this.capi = capi;
        this.icons = icons;
        textFont = CairoFont.WhiteDetailText();
    }

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
        // Keep the last composed textures until Compose() replaces them so a
        // stray draw cannot blit a disposed GL object. ready=false skips draw.
        ready = false;
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

    public void Compose()
    {
        ready = false;
        DisposeTextTextures();
        DisposeColumnBackground();
        if (cells.Count == 0)
        {
            return;
        }

        int iconPx = Math.Max(8, (int)GuiElement.scaled(IconUnscaled));
        for (int i = 0; i < cells.Count; i++)
        {
            TooltipStatCell cell = cells[i];
            if (cell.Icon != null)
            {
                icons.Get(cell.Icon, iconPx);
            }

            if (string.IsNullOrEmpty(cell.Text))
            {
                textTextures.Add(null);
                continue;
            }

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

            LoadedTexture texture = capi.Gui.TextTexture.GenTextTexture(cell.Text, font);
            textTextures.Add(texture);
        }

        BakeColumnBackground();
        ready = true;
    }

    public override void ComposeElements(Context ctx, ImageSurface surface)
    {
    }

    public override void RenderInteractiveElements(float deltaTime)
    {
        if (!ready || cells.Count == 0 || Bounds.fixedHeight <= 0)
        {
            return;
        }

        try
        {
            RenderBand();
        }
        catch (ArgumentException ex)
        {
            ready = false;
            if (!loggedDisposedMesh)
            {
                loggedDisposedMesh = true;
                capi.Logger.Warning(
                    "[prosequor] Tooltip stats band skipped after a disposed-mesh blit. {0}",
                    ex.Message);
            }
        }
    }

    void RenderBand()
    {
        Bounds.CalcWorldBounds();
        double bandW = Bounds.InnerWidth;
        double bandH = Bounds.InnerHeight;
        if (bandW <= 0 || bandH <= 0)
        {
            return;
        }

        double originX = Bounds.renderX + Bounds.absPaddingX;
        double originY = Bounds.renderY + Bounds.absPaddingY;

        LoadedTexture? bg = columnBackground;
        if (IsLive(bg))
        {
            api.Render.Render2DTexturePremultipliedAlpha(
                bg.TextureId,
                (float)originX,
                (float)originY,
                (float)bandW,
                (float)bandH,
                1001f);
        }

        if (!TryGetColumnLayout(bandW, out int weightSum, out double gapPx))
        {
            return;
        }

        double iconPx = GuiElement.scaled(IconUnscaled);
        double textGap = GuiElement.scaled(IconTextGap);
        double xCursor = originX;
        double yCenter = originY + bandH / 2;

        for (int i = 0; i < cells.Count; i++)
        {
            TooltipStatCell cell = cells[i];
            int w = Math.Max(1, cell.Weight);
            double colW = (bandW - gapPx * (cells.Count - 1)) * w / weightSum;

            LoadedTexture? textTex = i < textTextures.Count ? textTextures[i] : null;
            LoadedTexture? iconTex = null;
            if (cell.Icon != null)
            {
                iconTex = icons.Get(cell.Icon, Math.Max(8, (int)iconPx));
            }

            bool showIcon = IsLive(iconTex);
            bool showText = IsLive(textTex);
            double textW = showText ? textTex!.Width : 0;
            double textH = showText ? textTex!.Height : 0;
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
                    DrawIcon(iconTex!, clusterX, yCenter - iconDraw / 2, iconDraw, cell.TintArgb);
                    api.Render.Render2DTexturePremultipliedAlpha(
                        textTex!.TextureId,
                        clusterX + iconDraw + textGap,
                        yCenter - textH / 2,
                        textW,
                        textH,
                        1002);
                }
                else
                {
                    api.Render.Render2DTexturePremultipliedAlpha(
                        textTex!.TextureId,
                        clusterX,
                        yCenter - textH / 2,
                        textW,
                        textH,
                        1002);
                    DrawIcon(iconTex!, clusterX + textW + textGap, yCenter - iconDraw / 2, iconDraw, cell.TintArgb);
                }
            }
            else if (showIcon)
            {
                DrawIcon(iconTex!, clusterX, yCenter - iconDraw / 2, iconDraw, cell.TintArgb);
            }
            else if (showText)
            {
                api.Render.Render2DTexturePremultipliedAlpha(
                    textTex!.TextureId,
                    clusterX,
                    yCenter - textH / 2,
                    textW,
                    textH,
                    1002);
            }

            xCursor += colW + gapPx;
        }
    }

    void BakeColumnBackground()
    {
        Bounds.CalcWorldBounds();
        int pxW = Math.Max(1, (int)Math.Ceiling(Bounds.InnerWidth));
        int pxH = Math.Max(1, (int)Math.Ceiling(Bounds.InnerHeight));
        if (pxW <= 1 || pxH <= 1 || cells.Count == 0)
        {
            return;
        }

        if (!TryGetColumnLayout(pxW, out int weightSum, out double gapPx))
        {
            return;
        }

        double stroke = GuiElement.scaled(GuiElementRoundedInset.DefaultStrokeWidth);
        double half = stroke / 2;
        double usable = pxW - gapPx * (cells.Count - 1);

        ImageSurface surface = new(Format.Argb32, pxW, pxH);
        Context ctx = new(surface);
        try
        {
            ctx.SetSourceRGBA(0, 0, 0, 0);
            ctx.Paint();

            double xCursor = 0;
            for (int i = 0; i < cells.Count; i++)
            {
                int w = Math.Max(1, cells[i].Weight);
                double colW = usable * w / weightSum;
                double boxW = colW - stroke;
                double boxH = pxH - stroke;
                if (boxW > 0 && boxH > 0)
                {
                    double r = Math.Min(GuiElement.scaled(InsetRadius), Math.Min(boxW, boxH) / 2);
                    var geo = new RoundedInsetGeometry
                    {
                        X = xCursor + half,
                        Y = half,
                        W = boxW,
                        H = boxH,
                        R = r
                    };
                    RoundedInsetDraw.DrawFill(ctx, geo, GuiElementRoundedInset.DefaultFillColor);
                    RoundedInsetDraw.DrawStroke(
                        ctx, geo, InsetStrokeColor, GuiElementRoundedInset.DefaultStrokeWidth);
                }

                xCursor += colW + gapPx;
            }

            LoadedTexture texture = new(capi);
            capi.Gui.LoadOrUpdateCairoTexture(surface, linearMag: true, ref texture);
            columnBackground = texture;
        }
        finally
        {
            ctx.Dispose();
            surface.Dispose();
        }
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

    void DrawIcon(LoadedTexture iconTex, double x, double y, double size, int? tintArgb)
    {
        Vec4f? tint = tintArgb is int argb ? StyledSvgIconCache.TintFromArgb(argb) : null;
        icons.Render(iconTex, x, y, size, 1002, tint);
    }

    static bool IsLive([NotNullWhen(true)] LoadedTexture? texture) =>
        texture is { TextureId: > 0, Disposed: false };

    void DisposeTextTextures()
    {
        foreach (LoadedTexture? texture in textTextures)
        {
            texture?.Dispose();
        }

        textTextures.Clear();
    }

    void DisposeColumnBackground()
    {
        columnBackground?.Dispose();
        columnBackground = null;
    }

    public override void Dispose()
    {
        ready = false;
        DisposeTextTextures();
        DisposeColumnBackground();
        base.Dispose();
    }
}
