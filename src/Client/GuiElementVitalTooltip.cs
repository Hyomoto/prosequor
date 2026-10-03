using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace Prosequor.Client;

/// <summary>
/// Follow-mouse tooltip for Stats panel vital cards (health, satiety, temperature).
/// Calls <see cref="StatsVitalHover.Lines"/> while hovered; empty result draws nothing.
/// </summary>
public class GuiElementVitalTooltip : GuiElement
{
    public const double MouseOffsetX = 16;
    public const double MouseOffsetY = -4;

    readonly ICoreClientAPI capi;
    readonly List<VitalHoverRow> rows = new();
    readonly CairoFont valueFont = CairoFont.WhiteSmallText()
        .WithStroke(ColorUtil.BlackArgbDouble, 0.75);
    readonly TextBackground background = new()
    {
        FillColor = GuiStyle.DialogStrongBgColor,
        Padding = 5,
        BorderWidth = 2
    };

    ElementBounds? clipBounds;
    string? hoveredSlot;
    string? cachedText;
    LoadedTexture? valueTexture;

    public GuiElementVitalTooltip(ICoreClientAPI capi, ElementBounds bounds)
        : base(capi, bounds)
    {
        this.capi = capi;
    }

    public void SetClipBounds(ElementBounds clip) => clipBounds = clip;

    public void SetRows(IReadOnlyList<VitalHoverRow> next)
    {
        rows.Clear();
        rows.AddRange(next);
        ClearHover();
    }

    public override void RenderInteractiveElements(float deltaTime)
    {
        UpdateHover();
        if (hoveredSlot == null || valueTexture == null || valueTexture.TextureId <= 0)
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

        string? nextSlot = null;
        foreach (VitalHoverRow row in rows)
        {
            row.Bounds.CalcWorldBounds();
            if (row.Bounds.PointInside(mx, my))
            {
                nextSlot = row.Slot;
                break;
            }
        }

        if (nextSlot == null)
        {
            ClearHover();
            return;
        }

        if (!string.Equals(hoveredSlot, nextSlot, StringComparison.OrdinalIgnoreCase))
        {
            hoveredSlot = nextSlot;
            cachedText = null;
            valueTexture?.Dispose();
            valueTexture = null;
        }

        EnsureTexture(nextSlot);
    }

    void EnsureTexture(string slot)
    {
        string? text = FormatLines(StatsVitalHover.Lines(slot));
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

    /// <summary>Drop null/whitespace lines; join the rest with newlines. Null when none remain.</summary>
    internal static string? FormatLines(IReadOnlyList<string>? lines)
    {
        if (lines == null || lines.Count == 0)
        {
            return null;
        }

        StringBuilder? sb = null;
        for (int i = 0; i < lines.Count; i++)
        {
            string? line = lines[i];
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            sb ??= new StringBuilder();
            if (sb.Length > 0)
            {
                sb.Append('\n');
            }

            sb.Append(line.Trim());
        }

        return sb?.Length > 0 ? sb.ToString() : null;
    }

    void ClearHover()
    {
        hoveredSlot = null;
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

public readonly struct VitalHoverRow
{
    public VitalHoverRow(string slot, ElementBounds bounds)
    {
        Slot = slot;
        Bounds = bounds;
    }

    public string Slot { get; }
    public ElementBounds Bounds { get; }
}

public static class GuiElementVitalTooltipHelpers
{
    public static GuiComposer AddVitalTooltip(
        this GuiComposer composer,
        GuiElementVitalTooltip element,
        string key)
    {
        composer.AddInteractiveElement(element, key);
        return composer;
    }
}
