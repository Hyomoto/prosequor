using Cairo;
using Prosequor.Data;
using Prosequor.Player;
using Vintagestory.API.Client;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace Prosequor.Client;

/// <summary>
/// Follow-mouse attribute card: icon, name, and active effect lines. Clamped on-screen.
/// </summary>
public class GuiElementAttributeTooltip : GuiElement
{
    public const double MinCardWidth = 200;
    public const double IconSize = 48;
    public const double Pad = 10;
    public const double Gap = 6;
    public const double MouseOffset = 18;

    readonly ICoreClientAPI capi;
    readonly AttributeIcons icons;
    readonly List<AttributeHoverRow> rows = new();
    ElementBounds? clipBounds;
    IPlayerProgress? progress;
    IAttributeStatRegistry? stats;

    string? hoveredId;
    int hoveredScore = int.MinValue;
    LoadedTexture? nameTexture;
    readonly List<LoadedTexture> lineTextures = new();
    LoadedTexture? cardBackground;
    int cardBgW;
    int cardBgH;
    double cardWidth = MinCardWidth;
    double cardHeight;

    public GuiElementAttributeTooltip(
        ICoreClientAPI capi,
        ElementBounds bounds,
        AttributeIcons icons)
        : base(capi, bounds)
    {
        this.capi = capi;
        this.icons = icons;
    }

    public void SetClipBounds(ElementBounds clip) => clipBounds = clip;

    public void SetProgress(IPlayerProgress? next) => progress = next;

    public void SetStats(IAttributeStatRegistry? next) => stats = next;

    public void SetRows(IReadOnlyList<AttributeHoverRow> next)
    {
        rows.Clear();
        rows.AddRange(next);
        ClearHover();
    }

    public override void RenderInteractiveElements(float deltaTime)
    {
        UpdateHover();
        if (hoveredId == null)
        {
            return;
        }

        bool scissorWasOn = api.Render.ScissorStack.Count > 0;
        if (scissorWasOn)
        {
            api.Render.GlScissorFlag(false);
        }

        try
        {
            RenderCard();
        }
        finally
        {
            if (scissorWasOn)
            {
                api.Render.GlScissorFlag(true);
            }
        }
    }

    void RenderCard()
    {
        string attrId = hoveredId!;
        EnsureTextures(attrId);
        LoadedTexture? icon = icons.Get(attrId, IconSize);

        double width = cardWidth;
        double height = cardHeight;
        double x = capi.Input.MouseX + MouseOffset;
        double y = capi.Input.MouseY + MouseOffset;
        double screenW = capi.Render.FrameWidth;
        double screenH = capi.Render.FrameHeight;
        if (x + width > screenW - 4)
        {
            x = capi.Input.MouseX - width - 8;
        }

        if (y + height > screenH - 4)
        {
            y = screenH - height - 4;
        }

        if (x < 4)
        {
            x = 4;
        }

        if (y < 4)
        {
            y = 4;
        }

        EnsureCardBackground((int)width, (int)height);
        if (cardBackground != null && cardBackground.TextureId > 0)
        {
            api.Render.Render2DTexturePremultipliedAlpha(
                cardBackground.TextureId, x, y, width, height, 90);
        }

        double ix = x + (width - IconSize) / 2;
        double iy = y + Pad;
        if (icon != null && icon.TextureId > 0)
        {
            api.Render.Render2DTexturePremultipliedAlpha(
                icon.TextureId, ix, iy, IconSize, IconSize, 92);
        }

        double textY = iy + IconSize + Gap;
        if (nameTexture != null && nameTexture.TextureId > 0)
        {
            api.Render.Render2DTexturePremultipliedAlpha(
                nameTexture.TextureId,
                x + (width - nameTexture.Width) / 2,
                textY,
                nameTexture.Width,
                nameTexture.Height,
                92);
            textY += nameTexture.Height + 4;
        }

        foreach (LoadedTexture line in lineTextures)
        {
            if (line.TextureId <= 0)
            {
                continue;
            }

            api.Render.Render2DTexturePremultipliedAlpha(
                line.TextureId,
                x + Pad,
                textY,
                line.Width,
                line.Height,
                92);
            textY += line.Height + 2;
        }
    }

    void UpdateHover()
    {
        int mx = capi.Input.MouseX;
        int my = capi.Input.MouseY;
        if (clipBounds != null)
        {
            clipBounds.CalcWorldBounds();
            if (!clipBounds.PointInside(mx, my))
            {
                ClearHover();
                return;
            }
        }

        string? next = null;
        foreach (AttributeHoverRow row in rows)
        {
            row.Bounds.CalcWorldBounds();
            if (row.Bounds.PointInside(mx, my))
            {
                next = row.AttributeId;
                break;
            }
        }

        if (next == null)
        {
            ClearHover();
            return;
        }

        int score = progress?.GetAttribute(next) ?? AttributeGrowth.DefaultScore;
        if (!string.Equals(hoveredId, next, StringComparison.OrdinalIgnoreCase)
            || hoveredScore != score)
        {
            hoveredId = next;
            hoveredScore = score;
            DisposeTextTextures();
        }
        else
        {
            hoveredId = next;
        }
    }

    void EnsureTextures(string attrId)
    {
        if (nameTexture != null)
        {
            return;
        }

        CairoFont nameFont = CairoFont.WhiteSmallText()
            .WithFontSize(16)
            .WithOrientation(EnumTextOrientation.Center);
        nameTexture = capi.Gui.TextTexture.GenTextTexture(
            Lang.Get("prosequor:attribute-" + attrId),
            nameFont);

        double textW = nameTexture?.Width ?? 0;
        if (stats != null)
        {
            CairoFont lineFont = CairoFont.WhiteDetailText()
                .WithOrientation(EnumTextOrientation.Left);
            foreach ((string text, bool positive) in AttributeEffectDescription.EnumerateActiveLinesFor(
                         stats, attrId, progress))
            {
                double[] color = positive
                    ? [0.52, 1, 0.52, 1]
                    : [1, 0.52, 0.52, 1];
                CairoFont colored = lineFont.Clone().WithColor(color);
                LoadedTexture line = capi.Gui.TextTexture.GenTextTexture(text, colored);
                lineTextures.Add(line);
                textW = Math.Max(textW, line.Width);
            }
        }

        cardWidth = Math.Max(MinCardWidth, Math.Max(IconSize + Pad * 2, textW + Pad * 2));

        double h = Pad + IconSize + Gap;
        if (nameTexture != null)
        {
            h += nameTexture.Height + 4;
        }

        foreach (LoadedTexture line in lineTextures)
        {
            h += line.Height + 2;
        }

        h += Pad;
        cardHeight = h;
    }

    void ClearHover()
    {
        if (hoveredId == null)
        {
            return;
        }

        hoveredId = null;
        hoveredScore = int.MinValue;
        DisposeTextTextures();
    }

    void EnsureCardBackground(int width, int height)
    {
        width = Math.Max(8, width);
        height = Math.Max(8, height);
        if (cardBackground != null && cardBgW == width && cardBgH == height)
        {
            return;
        }

        cardBackground?.Dispose();
        cardBackground = null;
        cardBgW = width;
        cardBgH = height;

        ImageSurface surface = new(Format.Argb32, width, height);
        Context ctx = new(surface);
        try
        {
            ctx.SetSourceRGBA(0.10, 0.08, 0.06, 0.94);
            ctx.Rectangle(0, 0, width, height);
            ctx.Fill();
            ctx.SetSourceRGBA(0.72, 0.62, 0.42, 0.95);
            ctx.LineWidth = 2;
            ctx.Rectangle(1, 1, width - 2, height - 2);
            ctx.Stroke();

            LoadedTexture texture = new(capi);
            capi.Gui.LoadOrUpdateCairoTexture(surface, linearMag: false, ref texture);
            cardBackground = texture;
        }
        finally
        {
            ctx.Dispose();
            surface.Dispose();
        }
    }

    void DisposeTextTextures()
    {
        nameTexture?.Dispose();
        nameTexture = null;
        foreach (LoadedTexture line in lineTextures)
        {
            line.Dispose();
        }

        lineTextures.Clear();
        cardWidth = MinCardWidth;
        cardHeight = 0;
    }

    public override void Dispose()
    {
        DisposeTextTextures();
        cardBackground?.Dispose();
        cardBackground = null;
        base.Dispose();
    }
}

public static class GuiElementAttributeTooltipHelpers
{
    public static GuiComposer AddAttributeTooltip(
        this GuiComposer composer,
        GuiElementAttributeTooltip element,
        string key)
    {
        composer.AddInteractiveElement(element, key);
        return composer;
    }
}
