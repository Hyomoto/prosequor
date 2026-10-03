using Cairo;
using Prosequor.Data;
using Prosequor.Player;
using Vintagestory.API.Client;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace Prosequor.Client;

/// <summary>
/// Follow-mouse attribute card: icon, name, flavor quote, and active effect lines.
/// </summary>
public class GuiElementAttributeTooltip : GuiElement
{
    public const double MinCardWidth = 220;
    public const double IconSize = 48;
    public const double Pad = 10;
    public const double Gap = 6;
    public const double MouseOffset = 18;
    public const int QuoteWrapChars = 34;
    public const string QuoteColorHex = "#99c9f9";

    readonly ICoreClientAPI capi;
    readonly AttributeIcons icons;
    readonly List<AttributeHoverRow> rows = new();
    ElementBounds? clipBounds;
    IPlayerProgress? progress;
    IAttributeStatRegistry? stats;
    System.Func<string, int>? scoreOf;

    string? hoveredId;
    int hoveredScore = int.MinValue;
    LoadedTexture? nameTexture;
    LoadedTexture? quoteTexture;
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

    /// <summary>Optional score override (create-character class preview).</summary>
    public void SetScoreOf(System.Func<string, int>? next) => scoreOf = next;

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

        if (quoteTexture != null && quoteTexture.TextureId > 0)
        {
            api.Render.Render2DTexturePremultipliedAlpha(
                quoteTexture.TextureId,
                x + (width - quoteTexture.Width) / 2,
                textY,
                quoteTexture.Width,
                quoteTexture.Height,
                92);
            textY += quoteTexture.Height + Gap;
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
            if (!TryPointInside(clipBounds, mx, my))
            {
                ClearHover();
                return;
            }
        }

        string? next = null;
        foreach (AttributeHoverRow row in rows)
        {
            if (TryPointInside(row.Bounds, mx, my))
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

        int score = ScoreOf(next);
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

    static bool TryPointInside(ElementBounds bounds, int mx, int my)
    {
        try
        {
            bounds.CalcWorldBounds();
            return bounds.PointInside(mx, my);
        }
        catch (NullReferenceException)
        {
            // Bounds not yet in a laid-out parent chain (compose / tab swap).
            return false;
        }
    }

    int ScoreOf(string attrId) =>
        scoreOf?.Invoke(attrId)
        ?? progress?.GetAttribute(attrId)
        ?? AttributeGrowth.DefaultScore;

    string ResolveName(string attrId)
    {
        if (stats != null && stats.TryGet(attrId, out AttributeStatDef def))
        {
            return AttributeStatRegistry.DisplayName(def);
        }

        return Lang.Get("prosequor:attribute-" + attrId);
    }

    string ResolveFlavor(string attrId)
    {
        if (stats != null && stats.TryGet(attrId, out AttributeStatDef def))
        {
            return AttributeStatRegistry.Description(def);
        }

        return Lang.GetIfExists("prosequor:attribute-flavor-" + attrId) ?? "";
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
            ResolveName(attrId),
            nameFont);

        double textW = nameTexture?.Width ?? 0;

        string quote = ResolveFlavor(attrId);
        if (!string.IsNullOrWhiteSpace(quote))
        {
            CairoFont quoteFont = CairoFont.WhiteDetailText()
                .WithColor(ColorUtil.Hex2Doubles(QuoteColorHex))
                .WithSlant(FontSlant.Italic)
                .WithOrientation(EnumTextOrientation.Center);
            quoteTexture = capi.Gui.TextTexture.GenTextTexture(
                WrapText(quote.Trim(), QuoteWrapChars),
                quoteFont);
            if (quoteTexture != null)
            {
                textW = Math.Max(textW, quoteTexture.Width);
            }
        }

        if (stats != null)
        {
            CairoFont lineFont = CairoFont.WhiteDetailText()
                .WithOrientation(EnumTextOrientation.Left);
            foreach ((string text, bool positive) in AttributeEffectDescription.EnumerateActiveLinesFor(
                         stats, attrId, ScoreOf(attrId)))
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

        if (quoteTexture != null)
        {
            h += quoteTexture.Height + Gap;
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
        quoteTexture?.Dispose();
        quoteTexture = null;
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

    static string WrapText(string text, int maxChars)
    {
        if (text.Length <= maxChars)
        {
            return text;
        }

        List<string> lines = new();
        string[] words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        string line = "";
        foreach (string word in words)
        {
            if (line.Length == 0)
            {
                line = word;
                continue;
            }

            if (line.Length + 1 + word.Length <= maxChars)
            {
                line += " " + word;
            }
            else
            {
                lines.Add(line);
                line = word;
            }
        }

        if (line.Length > 0)
        {
            lines.Add(line);
        }

        return string.Join("\n", lines);
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
