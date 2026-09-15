using Cairo;
using Vintagestory.API.Client;

namespace Prosequor.Client;

/// <summary>
/// Dynamic vertical tick inside a rounded inset, redrawn when its 0..1 axis position changes.
/// A null fraction hides the tick (maxed attribute).
/// </summary>
public class GuiElementRoundedInsetTick : GuiElement
{
    readonly double radius;
    readonly double strokeWidth;
    readonly double[] tickColor;

    int texId;
    float? fraction = 0.5f;

    public float? Fraction => fraction;

    public GuiElementRoundedInsetTick(
        ICoreClientAPI capi,
        ElementBounds bounds,
        float? fraction = 0.5f,
        double radius = GuiElementRoundedInset.DefaultRadius,
        double strokeWidth = GuiElementRoundedInset.DefaultStrokeWidth,
        double[]? tickColor = null)
        : base(capi, bounds)
    {
        this.radius = radius;
        this.strokeWidth = strokeWidth;
        this.tickColor = tickColor ?? GuiElementRoundedInset.DefaultTickColor;
        this.fraction = ClampFraction(fraction);
    }

    public override void ComposeElements(Context ctx, ImageSurface surface)
    {
        Bounds.CalcWorldBounds();
        Redraw();
    }

    public void SetTick(float? value)
    {
        float? clamped = ClampFraction(value);
        if (NullableEquals(clamped, fraction))
        {
            return;
        }

        fraction = clamped;
        Redraw();
    }

    public void Redraw()
    {
        if (Bounds.OuterWidthInt <= 0 || Bounds.OuterHeightInt <= 0)
        {
            return;
        }

        ImageSurface surface = new(Format.Argb32, Bounds.OuterWidthInt, Bounds.OuterHeightInt);
        Context ctx = new(surface);
        try
        {
            if (fraction != null
                && RoundedInsetDraw.TryGetGeometry(
                    Bounds,
                    radius,
                    strokeWidth,
                    localCoordinates: true,
                    out RoundedInsetGeometry geo))
            {
                RoundedInsetDraw.DrawTick(ctx, geo, fraction.Value, tickColor);
            }

            generateTexture(surface, ref texId);
        }
        finally
        {
            ctx.Dispose();
            surface.Dispose();
        }
    }

    public override void RenderInteractiveElements(float deltaTime)
    {
        if (texId <= 0)
        {
            return;
        }

        api.Render.Render2DTexture(texId, Bounds);
    }

    static float? ClampFraction(float? value) =>
        value == null ? null : Math.Clamp(value.Value, 0f, 1f);

    static bool NullableEquals(float? a, float? b)
    {
        if (a == null || b == null)
        {
            return a == null && b == null;
        }

        return Math.Abs(a.Value - b.Value) < 0.0001f;
    }
}

public static class GuiElementRoundedInsetTickHelpers
{
    public static GuiComposer AddRoundedInsetTick(
        this GuiComposer composer,
        ElementBounds bounds,
        string key,
        float? fraction = 0.5f,
        double radius = GuiElementRoundedInset.DefaultRadius,
        double strokeWidth = GuiElementRoundedInset.DefaultStrokeWidth,
        double[]? tickColor = null)
    {
        if (!composer.Composed)
        {
            composer.AddInteractiveElement(
                new GuiElementRoundedInsetTick(
                    composer.Api,
                    bounds,
                    fraction,
                    radius,
                    strokeWidth,
                    tickColor),
                key);
        }

        return composer;
    }

    public static GuiElementRoundedInsetTick? GetRoundedInsetTick(this GuiComposer composer, string key)
    {
        return composer.GetElement(key) as GuiElementRoundedInsetTick;
    }
}
