using Cairo;
using Vintagestory.API.Client;

namespace Prosequor.Client;

/// <summary>
/// Compose-time rounded rectangle: semi-transparent fill and stroke, baked into the dialog texture.
/// </summary>
public class GuiElementRoundedInset : GuiElement
{
    public const double DefaultRadius = 8;
    public const double DefaultStrokeWidth = 1.5;

    static readonly double[] DefaultStrokeColor = [0.72, 0.62, 0.42, 0.95];
    public static readonly double[] DefaultFillColor = [0, 0, 0, 0.35];
    /// <summary>Muted gold: darker than cream labels, lighter than the inset well.</summary>
    public static readonly double[] DefaultTickColor = [0.55, 0.48, 0.36, 0.70];

    readonly double radius;
    readonly double strokeWidth;
    readonly double[] strokeColor;
    readonly double[] fillColor;

    public GuiElementRoundedInset(
        ICoreClientAPI capi,
        ElementBounds bounds,
        double radius = DefaultRadius,
        double strokeWidth = DefaultStrokeWidth,
        double[]? strokeColor = null,
        double[]? fillColor = null)
        : base(capi, bounds)
    {
        this.radius = radius;
        this.strokeWidth = strokeWidth;
        this.strokeColor = strokeColor ?? DefaultStrokeColor;
        this.fillColor = fillColor ?? DefaultFillColor;
    }

    public override void ComposeElements(Context ctx, ImageSurface surface)
    {
        Bounds.CalcWorldBounds();
        if (!RoundedInsetDraw.TryGetGeometry(Bounds, radius, strokeWidth, localCoordinates: false, out RoundedInsetGeometry geo))
        {
            return;
        }

        RoundedInsetDraw.DrawFill(ctx, geo, fillColor);
        RoundedInsetDraw.DrawStroke(ctx, geo, strokeColor, strokeWidth);
    }
}

/// <summary>Shared inset rect geometry for static compose and dynamic texture bakes.</summary>
internal readonly struct RoundedInsetGeometry
{
    public double X { get; init; }
    public double Y { get; init; }
    public double W { get; init; }
    public double H { get; init; }
    public double R { get; init; }
}

internal static class RoundedInsetDraw
{
    public static bool TryGetGeometry(
        ElementBounds bounds,
        double radius,
        double strokeWidth,
        bool localCoordinates,
        out RoundedInsetGeometry geo)
    {
        double line = GuiElement.scaled(strokeWidth);
        double half = line / 2;
        double originX = localCoordinates ? 0 : bounds.drawX;
        double originY = localCoordinates ? 0 : bounds.drawY;
        double x = originX + half;
        double y = originY + half;
        double w = bounds.InnerWidth - line;
        double h = bounds.InnerHeight - line;
        if (w <= 0 || h <= 0)
        {
            geo = default;
            return false;
        }

        double r = Math.Min(GuiElement.scaled(radius), Math.Min(w, h) / 2);
        geo = new RoundedInsetGeometry { X = x, Y = y, W = w, H = h, R = r };
        return true;
    }

    public static void DrawFill(Context ctx, in RoundedInsetGeometry geo, double[] fillColor)
    {
        GuiElement.RoundRectangle(ctx, geo.X, geo.Y, geo.W, geo.H, geo.R);
        ctx.SetSourceRGBA(fillColor);
        ctx.Fill();
    }

    public static void DrawStroke(Context ctx, in RoundedInsetGeometry geo, double[] strokeColor, double strokeWidth)
    {
        GuiElement.RoundRectangle(ctx, geo.X, geo.Y, geo.W, geo.H, geo.R);
        ctx.SetSourceRGBA(strokeColor);
        ctx.LineWidth = GuiElement.scaled(strokeWidth);
        ctx.Stroke();
    }

    public static void DrawTick(
        Context ctx,
        in RoundedInsetGeometry geo,
        float fraction,
        double[] tickColor)
    {
        float clamped = Math.Clamp(fraction, 0f, 1f);
        double padX = Math.Min(GuiElement.scaled(6), geo.W * 0.2);
        double padY = Math.Min(GuiElement.scaled(4), geo.H * 0.25);
        double x = geo.X + padX + (geo.W - padX * 2) * clamped;
        double y0 = geo.Y + padY;
        double y1 = geo.Y + geo.H - padY;
        if (y1 <= y0)
        {
            return;
        }

        ctx.Save();
        GuiElement.RoundRectangle(ctx, geo.X, geo.Y, geo.W, geo.H, geo.R);
        ctx.Clip();
        ctx.SetSourceRGBA(tickColor);
        ctx.LineWidth = GuiElement.scaled(2);
        ctx.MoveTo(x, y0);
        ctx.LineTo(x, y1);
        ctx.Stroke();
        ctx.Restore();
    }
}

public static class GuiElementRoundedInsetHelpers
{
    /// <summary>
    /// Drop-in style replacement for <c>AddInset</c>: same bounds, rounded fill and stroke.
    /// </summary>
    public static GuiComposer AddRoundedInset(
        this GuiComposer composer,
        ElementBounds bounds,
        double radius = GuiElementRoundedInset.DefaultRadius,
        double strokeWidth = GuiElementRoundedInset.DefaultStrokeWidth,
        double[]? strokeColor = null,
        double[]? fillColor = null)
    {
        if (!composer.Composed)
        {
            composer.AddStaticElement(
                new GuiElementRoundedInset(composer.Api, bounds, radius, strokeWidth, strokeColor, fillColor));
        }

        return composer;
    }
}
