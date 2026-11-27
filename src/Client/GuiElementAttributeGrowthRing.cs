using Cairo;
using Vintagestory.API.Client;

namespace Prosequor.Client;

/// <summary>
/// Live-stats-only polygon of attribute bucket axis fractions inside the attribute wheel.
/// Vertices follow the same ring angles as the plates; null (maxed) sits on the neutral ellipse.
/// </summary>
public class GuiElementAttributeGrowthRing : GuiElement
{
    static readonly double[] NeutralStroke = [0.72, 0.62, 0.42, 0.35];
    static readonly double[] PolyFill = [0.55, 0.48, 0.36, 0.18];
    static readonly double[] PolyStroke = [0.72, 0.62, 0.42, 0.70];

    readonly int vertexCount;
    float?[] fractions;
    int texId;
    bool hasDrawable;

    public GuiElementAttributeGrowthRing(
        ICoreClientAPI capi,
        ElementBounds bounds,
        int vertexCount)
        : base(capi, bounds)
    {
        this.vertexCount = Math.Max(0, vertexCount);
        fractions = new float?[this.vertexCount];
        for (int i = 0; i < this.vertexCount; i++)
        {
            fractions[i] = 0.5f;
        }
    }

    public override void ComposeElements(Context ctx, ImageSurface surface)
    {
        Bounds.CalcWorldBounds();
        Redraw();
    }

    public void SetFractions(float?[] next)
    {
        ArgumentNullException.ThrowIfNull(next);
        if (FractionsEqual(fractions, next))
        {
            return;
        }

        float?[] copy = new float?[vertexCount];
        for (int i = 0; i < vertexCount; i++)
        {
            copy[i] = i < next.Length ? ClampFraction(next[i]) : 0.5f;
        }

        fractions = copy;
        Redraw();
    }

    public void Redraw()
    {
        if (Bounds.OuterWidthInt <= 0 || Bounds.OuterHeightInt <= 0 || vertexCount <= 0)
        {
            hasDrawable = false;
            return;
        }

        ImageSurface surface = new(Format.Argb32, Bounds.OuterWidthInt, Bounds.OuterHeightInt);
        Context ctx = new(surface);
        try
        {
            hasDrawable = TryDraw(ctx, Bounds.OuterWidth, Bounds.OuterHeight);
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
        if (!hasDrawable || texId <= 0)
        {
            return;
        }

        api.Render.Render2DTexture(texId, Bounds);
    }

    bool TryDraw(Context ctx, double width, double height)
    {
        bool anyRace = false;
        for (int i = 0; i < vertexCount; i++)
        {
            if (fractions[i] != null)
            {
                anyRace = true;
                break;
            }
        }

        if (!anyRace)
        {
            return false;
        }

        double cluster = scaled(AttributeLayout.CircleBackplateSize);
        double pad = scaled(AttributeLayout.CirclePad);
        double cx = width * 0.5;
        double cy = height * 0.5;
        double baseRx = Math.Max(scaled(16), width * 0.5 - pad - cluster * 0.5);
        double baseRy = Math.Max(scaled(16), height * 0.5 - pad - cluster * 0.5);

        AttributeRingRelax.Resolve(
            vertexCount,
            cx,
            cy,
            baseRx,
            baseRy,
            cluster,
            cluster,
            out double[] angles,
            out double rx,
            out double ry);

        double maxRx = Math.Max(1, rx - cluster * 0.5);
        double maxRy = Math.Max(1, ry - cluster * 0.5);
        double neutralRx = maxRx * 0.5;
        double neutralRy = maxRy * 0.5;

        DrawEllipse(ctx, cx, cy, neutralRx, neutralRy, NeutralStroke, lineWidth: 1);

        ctx.NewPath();
        for (int i = 0; i < vertexCount; i++)
        {
            float t = fractions[i] ?? 0.5f;
            double vx = cx + maxRx * t * Math.Cos(angles[i]);
            double vy = cy + maxRy * t * Math.Sin(angles[i]);
            if (i == 0)
            {
                ctx.MoveTo(vx, vy);
            }
            else
            {
                ctx.LineTo(vx, vy);
            }
        }

        ctx.ClosePath();
        ctx.SetSourceRGBA(PolyFill);
        ctx.FillPreserve();
        ctx.SetSourceRGBA(PolyStroke);
        ctx.LineWidth = scaled(1.5);
        ctx.Stroke();
        return true;
    }

    static void DrawEllipse(
        Context ctx,
        double cx,
        double cy,
        double rx,
        double ry,
        double[] color,
        double lineWidth)
    {
        if (rx <= 0 || ry <= 0)
        {
            return;
        }

        ctx.Save();
        ctx.Translate(cx, cy);
        ctx.Scale(rx, ry);
        ctx.NewPath();
        ctx.Arc(0, 0, 1, 0, Math.PI * 2);
        ctx.Restore();
        ctx.SetSourceRGBA(color);
        ctx.LineWidth = scaled(lineWidth);
        ctx.Stroke();
    }

    static float? ClampFraction(float? value) =>
        value == null ? null : Math.Clamp(value.Value, 0f, 1f);

    static bool FractionsEqual(float?[] a, float?[] b)
    {
        if (a.Length != b.Length)
        {
            return false;
        }

        for (int i = 0; i < a.Length; i++)
        {
            float? av = a[i];
            float? bv = b[i];
            if (av == null || bv == null)
            {
                if (av != bv)
                {
                    return false;
                }

                continue;
            }

            if (Math.Abs(av.Value - bv.Value) >= 0.0001f)
            {
                return false;
            }
        }

        return true;
    }
}

public static class GuiElementAttributeGrowthRingHelpers
{
    public static GuiComposer AddAttributeGrowthRing(
        this GuiComposer composer,
        GuiElementAttributeGrowthRing element,
        string key)
    {
        composer.AddInteractiveElement(element, key);
        return composer;
    }

    public static GuiElementAttributeGrowthRing? GetAttributeGrowthRing(
        this GuiComposer composer,
        string key)
    {
        return composer.GetElement(key) as GuiElementAttributeGrowthRing;
    }
}
