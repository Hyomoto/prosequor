using Cairo;
using Prosequor.Data;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace Prosequor.Client;

/// <summary>Shared attribute list and circle layouts for Stats / create-character.</summary>
public static class AttributeLayout
{
    public const double DefaultListIconSize = 30;
    public const double DefaultListRowHeight = 32;
    public const double DefaultListRowGap = 5;
    public const double DefaultListValueNudgeY = -5;
    public const double CircleIconSize = 32;
    public const double CircleValueWidth = 36;
    public const double CircleValueHeight = 32;
    public const double CircleIconValueGap = 2;
    public const double CirclePad = 6;
    public const double WatermarkFill = 0.85;
    public const double WatermarkAlpha = 0.12;

    static readonly AssetLocation GearTreeLoc =
        new("prosequor", "textures/icons/gear-tree.svg");

    static readonly double[] ArcColor = [0.55, 0.48, 0.36, 0.55];

    public static string ValueKey(string attrId) => "attr-value-" + attrId;

    public static string NameKey(string attrId) => "attr-name-" + attrId;

    public static string TickKey(string attrId) => "attr-tick-" + attrId;

    public static double ListBlockHeight(
        int count,
        double rowHeight = DefaultListRowHeight,
        double rowGap = DefaultListRowGap) =>
        count <= 0 ? 0 : count * (rowHeight + rowGap) - rowGap;

    public static IReadOnlyList<string> ResolveCatalog(ICoreClientAPI? capi)
    {
        IAttributeStatRegistry? stats = capi != null
            ? ProsequorModSystem.For(capi)?.AttributeStats
            : null;
        return stats != null && stats.All.Count > 0
            ? AttributeIds.CatalogIds(stats)
            : AttributeIds.All;
    }

    /// <summary>Vertical rows: icon, name, value inset, optional tick on the name band.</summary>
    public static void ComposeList(
        GuiComposer composer,
        ICoreClientAPI capi,
        AttributeIcons icons,
        IReadOnlyList<string> catalog,
        double x,
        double y,
        double width,
        System.Func<string, int> scoreOf,
        float?[]? ticks = null,
        bool showNames = true,
        bool showTicks = true,
        double iconSize = DefaultListIconSize,
        double rowHeight = DefaultListRowHeight,
        double rowGap = DefaultListRowGap,
        double valueNudgeY = DefaultListValueNudgeY,
        double valueWidth = 36)
    {
        CairoFont nameFont = CairoFont.WhiteSmallText();
        CairoFont valueFont = CairoFont.ButtonText()
            .WithOrientation(EnumTextOrientation.Center);

        double nameX = iconSize + 10;
        double valueColX = width - valueWidth - 8;
        double nameWidth = Math.Max(40, valueColX - nameX - 4);
        double rowY = y;

        for (int i = 0; i < catalog.Count; i++)
        {
            string attrId = catalog[i];
            ElementBounds rowBounds = ElementBounds.Fixed(x, rowY, width, rowHeight);
            composer.AddRoundedInset(rowBounds);

            if (showTicks && ticks != null && i < ticks.Length)
            {
                composer.AddRoundedInsetTick(
                    ElementBounds.Fixed(x + nameX, rowY, nameWidth, rowHeight),
                    TickKey(attrId),
                    ticks[i]);
            }

            ElementBounds iconBounds = ElementBounds.Fixed(
                x + 6,
                rowY + (rowHeight - iconSize) / 2,
                iconSize,
                iconSize);
            ElementBounds nameBounds = ElementBounds.Fixed(
                x + nameX,
                rowY + (rowHeight - 18) / 2,
                nameWidth,
                18);
            ElementBounds valueBounds = ElementBounds.Fixed(
                x + valueColX,
                rowY + (rowHeight - 32) / 2 + valueNudgeY,
                valueWidth,
                32);

            LoadedTexture? icon = icons.Get(attrId, iconSize);
            if (showNames)
            {
                composer.AddDynamicText(
                    Lang.Get("prosequor:attribute-" + attrId),
                    nameFont,
                    nameBounds,
                    NameKey(attrId));
            }

            composer.AddDynamicText(
                scoreOf(attrId).ToString(),
                valueFont,
                valueBounds,
                ValueKey(attrId));

            if (icon != null && icon.TextureId > 0)
            {
                LoadedTexture tex = icon;
                composer.AddCustomRender(iconBounds, (_, bounds) =>
                {
                    capi.Render.Render2DTexturePremultipliedAlpha(
                        tex.TextureId,
                        (float)bounds.renderX,
                        (float)bounds.renderY,
                        (float)bounds.OuterWidth,
                        (float)bounds.OuterHeight);
                });
            }

            rowY += rowHeight + rowGap;
        }
    }

    /// <summary>
    /// Circular icon+score clusters over a gear-tree watermark. Returns hover hit regions.
    /// </summary>
    public static IReadOnlyList<AttributeHoverRow> ComposeCircle(
        GuiComposer composer,
        ICoreClientAPI capi,
        AttributeIcons icons,
        IReadOnlyList<string> catalog,
        ElementBounds host,
        System.Func<string, int> scoreOf,
        float?[] ticks,
        ref LoadedTexture? watermark,
        ref LoadedTexture? arcTexture)
    {
        List<AttributeHoverRow> hoverRows = new(catalog.Count);
        if (catalog.Count == 0)
        {
            return hoverRows;
        }

        EnsureWatermark(capi, host, ref watermark);
        if (watermark != null && watermark.TextureId > 0)
        {
            LoadedTexture mark = watermark;
            composer.AddCustomRender(host.FlatCopy(), (_, bounds) =>
            {
                double size = Math.Min(bounds.OuterWidth, bounds.OuterHeight) * WatermarkFill;
                double ox = bounds.renderX + (bounds.OuterWidth - size) / 2;
                double oy = bounds.renderY + (bounds.OuterHeight - size) / 2;
                capi.Render.Render2DTexturePremultipliedAlpha(
                    mark.TextureId,
                    (float)ox,
                    (float)oy,
                    (float)size,
                    (float)size);
            });
        }

        CairoFont valueFont = CairoFont.ButtonText()
            .WithFont(GuiStyle.DecorativeFontName)
            .WithOrientation(EnumTextOrientation.Center);

        double clusterH = CircleIconSize + CircleIconValueGap + CircleValueHeight;
        double clusterW = Math.Max(CircleIconSize, CircleValueWidth);
        double rx = Math.Max(16, host.fixedWidth / 2 - CirclePad - clusterW / 2);
        double ry = Math.Max(16, host.fixedHeight / 2 - CirclePad - clusterH / 2);
        double cx = host.fixedX + host.fixedWidth / 2;
        double cy = host.fixedY + host.fixedHeight / 2;
        
        for (int i = 0; i < catalog.Count; i++)
        {
            string attrId = catalog[i];
            double angle = -Math.PI / 2 + i * (2 * Math.PI / catalog.Count);
            double midX = cx + rx * Math.Cos(angle);
            double midY = cy + ry * Math.Sin(angle);
            double left = midX - clusterW / 2;
            double top = midY - clusterH / 2;

            ElementBounds iconBounds = ElementBounds.Fixed(
                left + (clusterW - CircleIconSize) / 2,
                top,
                CircleIconSize,
                CircleIconSize);
            ElementBounds shapeBounds = ElementBounds.Fixed(
                left + (clusterW - CircleValueWidth) / 2,
                top + CircleIconSize + CircleIconValueGap,
                CircleValueWidth,
                CircleValueHeight);
            ElementBounds valueInset = ElementBounds.Fixed(
                left + (clusterW - CircleValueWidth) / 2,
                top + CircleIconSize + CircleIconValueGap + DefaultListValueNudgeY,
                CircleValueWidth,
                CircleValueHeight);

            composer.AddRoundedInset(shapeBounds);
            float? tick = i < ticks.Length ? ticks[i] : null;
            composer.AddRoundedInsetTick(valueInset.FlatCopy(), TickKey(attrId), tick);

            composer.AddDynamicText(
                scoreOf(attrId).ToString(),
                valueFont,
                valueInset.FlatCopy(),
                ValueKey(attrId));

            LoadedTexture? icon = icons.Get(attrId, CircleIconSize);
            if (icon != null && icon.TextureId > 0)
            {
                LoadedTexture tex = icon;
                composer.AddCustomRender(iconBounds, (_, bounds) =>
                {
                    capi.Render.Render2DTexturePremultipliedAlpha(
                        tex.TextureId,
                        (float)bounds.renderX,
                        (float)bounds.renderY,
                        (float)bounds.OuterWidth,
                        (float)bounds.OuterHeight);
                });
            }

            ElementBounds hit = ElementBounds
                .Fixed(left - host.fixedX, top - host.fixedY, clusterW, clusterH)
                .WithParent(host);
            hoverRows.Add(new AttributeHoverRow(attrId, hit));
        }

        return hoverRows;
    }

    static void EnsureWatermark(
        ICoreClientAPI capi,
        ElementBounds host,
        ref LoadedTexture? watermark)
    {
        int px = Math.Max(
            32,
            (int)(Math.Min(host.fixedWidth, host.fixedHeight) * WatermarkFill));
        if (watermark != null && watermark.Width == px && watermark.Height == px)
        {
            return;
        }

        watermark?.Dispose();
        watermark = null;

        IAsset? asset = capi.Assets.TryGet(GearTreeLoc);
        if (asset == null)
        {
            capi.Logger.Warning("[prosequor] Missing attribute watermark {0}.", GearTreeLoc);
            return;
        }

        ImageSurface surface = new(Format.Argb32, px, px);
        Context ctx = new(surface);
        ImageSurface iconSurface = new(Format.Argb32, px, px);
        try
        {
            capi.Gui.DrawSvg(asset, iconSurface, 0, 0, px, px, ColorUtil.WhiteArgb);
            ctx.SetSourceSurface(iconSurface, 0, 0);
            ctx.PaintWithAlpha(WatermarkAlpha);
            LoadedTexture texture = watermark ?? new LoadedTexture(capi);
            capi.Gui.LoadOrUpdateCairoTexture(surface, linearMag: true, ref texture);
            watermark = texture;
        }
        finally
        {
            ctx.Dispose();
            surface.Dispose();
            iconSurface.Dispose();
        }
    }
}

public readonly struct AttributeHoverRow
{
    public AttributeHoverRow(string attributeId, ElementBounds bounds)
    {
        AttributeId = attributeId;
        Bounds = bounds;
    }

    public string AttributeId { get; }
    public ElementBounds Bounds { get; }
}

