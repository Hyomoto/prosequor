using Prosequor.Data;
using Vintagestory.API.Client;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace Prosequor.Client;

/// <summary>
/// Follow-mouse XP bar readout for skill list rows. Rendered outside the list clip
/// so it is not scissored like the built-in statbar hover value.
/// </summary>
public class GuiElementSkillBarTooltip : GuiElement
{
    public const double MouseOffsetX = 16;
    public const double MouseOffsetY = -4;

    public delegate string? BarTextProvider(string skillId);

    readonly ICoreClientAPI capi;
    readonly BarTextProvider barTextProvider;
    readonly List<SkillBarHoverRow> rows = new();
    readonly CairoFont valueFont = CairoFont.WhiteSmallText()
        .WithStroke(ColorUtil.BlackArgbDouble, 0.75);
    readonly TextBackground background = new()
    {
        FillColor = GuiStyle.DialogStrongBgColor,
        Padding = 5,
        BorderWidth = 2
    };

    ElementBounds? clipBounds;
    string? hoveredId;
    string? cachedText;
    LoadedTexture? valueTexture;

    public GuiElementSkillBarTooltip(
        ICoreClientAPI capi,
        ElementBounds bounds,
        BarTextProvider barTextProvider)
        : base(capi, bounds)
    {
        this.capi = capi;
        this.barTextProvider = barTextProvider;
    }

    public void SetClipBounds(ElementBounds clip) => clipBounds = clip;

    public void SetRows(IReadOnlyList<SkillBarHoverRow> next)
    {
        rows.Clear();
        rows.AddRange(next);
        ClearHover();
    }

    public override void RenderInteractiveElements(float deltaTime)
    {
        UpdateHover();
        if (hoveredId == null || valueTexture == null || valueTexture.TextureId <= 0)
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
            double width = valueTexture.Width;
            double height = valueTexture.Height;
            double x = capi.Input.MouseX + MouseOffsetX;
            double y = capi.Input.MouseY + height + MouseOffsetY;
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

            api.Render.RenderTexture(
                valueTexture.TextureId,
                x,
                y,
                width,
                height,
                2000);
        }
        finally
        {
            if (scissorWasOn)
            {
                api.Render.GlScissorFlag(true);
            }
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

        string? nextId = null;
        foreach (SkillBarHoverRow row in rows)
        {
            row.Bounds.CalcWorldBounds();
            if (row.Bounds.PointInside(mx, my))
            {
                nextId = row.SkillId;
                break;
            }
        }

        if (nextId == null)
        {
            ClearHover();
            return;
        }

        if (!string.Equals(hoveredId, nextId, StringComparison.OrdinalIgnoreCase))
        {
            hoveredId = nextId;
            cachedText = null;
            valueTexture?.Dispose();
            valueTexture = null;
        }

        EnsureTexture(nextId);
    }

    void EnsureTexture(string skillId)
    {
        string? text = barTextProvider(skillId);
        if (string.IsNullOrEmpty(text))
        {
            ClearHover();
            return;
        }

        if (string.Equals(cachedText, text, StringComparison.Ordinal) && valueTexture != null)
        {
            return;
        }

        cachedText = text;
        valueTexture ??= new LoadedTexture(capi);
        capi.Gui.TextTexture.GenOrUpdateTextTexture(text, valueFont, ref valueTexture, background);
    }

    void ClearHover()
    {
        hoveredId = null;
        cachedText = null;
        valueTexture?.Dispose();
        valueTexture = null;
    }

    public override void Dispose()
    {
        valueTexture?.Dispose();
        valueTexture = null;
        base.Dispose();
    }
}

public readonly struct SkillBarHoverRow
{
    public SkillBarHoverRow(string skillId, ElementBounds bounds)
    {
        SkillId = skillId;
        Bounds = bounds;
    }

    public string SkillId { get; }
    public ElementBounds Bounds { get; }
}

public static class GuiElementSkillBarTooltipHelpers
{
    public static GuiComposer AddSkillBarTooltip(
        this GuiComposer composer,
        GuiElementSkillBarTooltip element,
        string key)
    {
        composer.AddInteractiveElement(element, key);
        return composer;
    }
}
