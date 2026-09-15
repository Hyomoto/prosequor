using Cairo;
using Prosequor.Data;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace Prosequor.Client;

/// <summary>
/// Follow-mouse skill list card: large icon, name, and description. Clamped on-screen.
/// </summary>
public class GuiElementSkillListTooltip : GuiElement
{
    public const double MinCardWidth = 240;
    public const double IconSize = 64;
    public const double Pad = 10;
    public const double Gap = 8;
    public const double MouseOffset = 18;
    const int DescWrapChars = 36;

    public delegate LoadedTexture? IconLoader(SkillDef skill, double size);

    readonly ICoreClientAPI capi;
    readonly IconLoader iconLoader;
    readonly List<SkillListHoverRow> rows = new();
    ElementBounds? clipBounds;
    IPlayerProgress? progress;

    SkillDef? hovered;
    string? hoveredId;
    string? hoveredProgressKey;
    LoadedTexture? nameTexture;
    LoadedTexture? descTexture;
    LoadedTexture? cardBackground;
    int cardBgW;
    int cardBgH;
    double cardWidth;
    double cardHeight;

    public GuiElementSkillListTooltip(
        ICoreClientAPI capi,
        ElementBounds bounds,
        IconLoader iconLoader)
        : base(capi, bounds)
    {
        this.capi = capi;
        this.iconLoader = iconLoader;
    }

    public void SetClipBounds(ElementBounds clip) => clipBounds = clip;

    public void SetProgress(IPlayerProgress? next) => progress = next;

    public void SetRows(IReadOnlyList<SkillListHoverRow> next)
    {
        rows.Clear();
        rows.AddRange(next);
        ClearHover();
    }

    public override void RenderInteractiveElements(float deltaTime)
    {
        UpdateHover();
        if (hovered == null)
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
        SkillDef skill = hovered!;
        EnsureTextures(skill);
        LoadedTexture? icon = iconLoader(skill, IconSize);

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

        if (descTexture != null && descTexture.TextureId > 0)
        {
            api.Render.Render2DTexturePremultipliedAlpha(
                descTexture.TextureId,
                x + (width - descTexture.Width) / 2,
                textY,
                descTexture.Width,
                descTexture.Height,
                92);
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

        SkillDef? next = null;
        foreach (SkillListHoverRow row in rows)
        {
            row.Bounds.CalcWorldBounds();
            if (row.Bounds.PointInside(mx, my))
            {
                next = row.Skill;
                break;
            }
        }

        if (next == null)
        {
            ClearHover();
            return;
        }

        string progressKey = SkillDescriptionResolver.ProgressFingerprint(next, progress);
        if (!string.Equals(hoveredId, next.Id, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(hoveredProgressKey, progressKey, StringComparison.Ordinal))
        {
            hovered = next;
            hoveredId = next.Id;
            hoveredProgressKey = progressKey;
            DisposeTextTextures();
        }
        else
        {
            hovered = next;
        }
    }

    void EnsureTextures(SkillDef skill)
    {
        if (nameTexture != null)
        {
            return;
        }

        CairoFont nameFont = CairoFont.WhiteSmallText()
            .WithFontSize(16)
            .WithOrientation(EnumTextOrientation.Center);
        CairoFont descFont = CairoFont.WhiteDetailText()
            .WithOrientation(EnumTextOrientation.Left);

        string name = SkillRegistry.DisplayName(skill);
        string desc = SkillRegistry.Description(skill, progress);
        nameTexture = capi.Gui.TextTexture.GenTextTexture(name, nameFont);

        if (!string.IsNullOrWhiteSpace(desc))
        {
            string wrapped = WrapText(desc, DescWrapChars);
            descTexture = capi.Gui.TextTexture.GenTextTexture(wrapped, descFont);
        }

        double textW = 0;
        if (nameTexture != null)
        {
            textW = Math.Max(textW, nameTexture.Width);
        }

        if (descTexture != null)
        {
            textW = Math.Max(textW, descTexture.Width);
        }

        cardWidth = Math.Max(MinCardWidth, Math.Max(IconSize + Pad * 2, textW + Pad * 2));

        double h = Pad + IconSize + Gap;
        if (nameTexture != null)
        {
            h += nameTexture.Height + 4;
        }

        if (descTexture != null)
        {
            h += descTexture.Height;
        }

        h += Pad;
        cardHeight = h;
    }

    void ClearHover()
    {
        if (hovered == null)
        {
            return;
        }

        hovered = null;
        hoveredId = null;
        hoveredProgressKey = null;
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
        descTexture?.Dispose();
        nameTexture = null;
        descTexture = null;
        cardWidth = MinCardWidth;
        cardHeight = 0;
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

    public override void Dispose()
    {
        DisposeTextTextures();
        cardBackground?.Dispose();
        cardBackground = null;
        base.Dispose();
    }
}

public readonly struct SkillListHoverRow
{
    public SkillListHoverRow(SkillDef skill, ElementBounds bounds)
    {
        Skill = skill;
        Bounds = bounds;
    }

    public SkillDef Skill { get; }
    public ElementBounds Bounds { get; }
}

public static class GuiElementSkillListTooltipHelpers
{
    public static GuiComposer AddSkillListTooltip(
        this GuiComposer composer,
        GuiElementSkillListTooltip element,
        string key)
    {
        composer.AddInteractiveElement(element, key);
        return composer;
    }
}
