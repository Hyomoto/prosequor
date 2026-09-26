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
    public const double CircleBackplateSize = 64;
    public const double CirclePad = 2;
    public const double WatermarkFill = 0.85;
    public const double WatermarkAlpha = 0.12;

    // attribute-backplate.svg viewBox 360: circle at (180,137) r≈122, pill y 232–347.
    const double PlateVb = 360;
    const double PlateCircleCx = 180 / PlateVb;
    const double PlateCircleCy = 137 / PlateVb;
    const double PlateCircleR = 122 / PlateVb;
    const double PlateIconFill = 0.78;
    const double PlateTextLeft = 49 / PlateVb;
    const double PlateTextTop = 248 / PlateVb;
    const double PlateTextWidth = 262 / PlateVb;
    const double PlateTextHeight = 92 / PlateVb;
    const double BackplateFillAlpha = 0.35;

    static readonly double[] CircleBorderColor = [0.72, 0.62, 0.42, 0.95];
    static readonly double[] CircleValueColor = [0.76, 0.64, 0.36, 1];

    static readonly AssetLocation GearTreeLoc =
        new("prosequor", "textures/icons/gear-tree.svg");

    static readonly AssetLocation AttributeBackplateLoc =
        new("prosequor", "textures/icons/gui/attribute-backplate.svg");

    static readonly AssetLocation AttributeBackplateBorderLoc =
        new("prosequor", "textures/icons/gui/attribute-backplate-border.svg");

    public static string ValueKey(string attrId) => "attr-value-" + attrId;

    public static string NameKey(string attrId) => "attr-name-" + attrId;

    public static string TickKey(string attrId) => "attr-tick-" + attrId;

    public static double ListBlockHeight(
        int count,
        double rowHeight = DefaultListRowHeight,
        double rowGap = DefaultListRowGap) =>
        count <= 0 ? 0 : count * (rowHeight + rowGap) - rowGap;

    /// <summary>
    /// Preferred host height for a near-round ring. Callers should clamp to the
    /// Stats panel budget so Character height is not exceeded.
    /// </summary>
    public static double CircleBlockHeight(double width)
    {
        double cluster = CircleBackplateSize;
        double rx = Math.Max(16, width / 2 - CirclePad - cluster / 2);
        double ry = rx * 0.82;
        return 2 * (ry + CirclePad + cluster / 2);
    }

    /// <summary>Smallest host that still clears the plates with <see cref="CirclePad"/>.</summary>
    public static double CircleBlockMinHeight() =>
        2 * (CirclePad + CircleBackplateSize);

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
        ref LoadedTexture? backplate)
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
            .WithColor((double[])CircleValueColor.Clone())
            .WithOrientation(EnumTextOrientation.Center);

        double cluster = CircleBackplateSize;
        double baseRx = Math.Max(16, host.fixedWidth / 2 - CirclePad - cluster / 2);
        double baseRy = Math.Max(16, host.fixedHeight / 2 - CirclePad - cluster / 2);
        double cx = host.fixedX + host.fixedWidth / 2;
        double cy = host.fixedY + host.fixedHeight / 2;

        AttributeRingRelax.Resolve(
            catalog.Count,
            cx,
            cy,
            baseRx,
            baseRy,
            cluster,
            cluster,
            out double[] angles,
            out double rx,
            out double ry);

        EnsureBackplate(capi, cluster, ref backplate);
        LoadedTexture? plate = backplate != null && backplate.TextureId > 0 ? backplate : null;

        for (int i = 0; i < catalog.Count; i++)
        {
            string attrId = catalog[i];
            AttributeRingRelax.ClusterCenter(angles[i], cx, cy, rx, ry, out double midX, out double midY);
            double left = midX - cluster / 2;
            double top = midY - cluster / 2;

            if (plate != null)
            {
                LoadedTexture plateTex = plate;
                composer.AddCustomRender(
                    ElementBounds.Fixed(left, top, cluster, cluster),
                    (_, bounds) =>
                    {
                        capi.Render.Render2DTexturePremultipliedAlpha(
                            plateTex.TextureId,
                            (float)bounds.renderX,
                            (float)bounds.renderY,
                            (float)bounds.OuterWidth,
                            (float)bounds.OuterHeight);
                    });
            }

            double iconSize = cluster * PlateCircleR * 2 * PlateIconFill;
            ElementBounds iconBounds = ElementBounds.Fixed(
                left + cluster * PlateCircleCx - iconSize / 2,
                top + cluster * PlateCircleCy - iconSize / 2,
                iconSize,
                iconSize);
            LoadedTexture? icon = icons.Get(attrId, iconSize);
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

            ElementBounds valueBounds = ElementBounds.Fixed(
                left + cluster * PlateTextLeft,
                top + cluster * PlateTextTop - 13,
                cluster * PlateTextWidth,
                32);
            
            composer.AddDynamicText(
                scoreOf(attrId).ToString(),
                valueFont,
                valueBounds.FlatCopy(),
                ValueKey(attrId));

            ElementBounds hit = ElementBounds
                .Fixed(left - host.fixedX, top - host.fixedY, cluster, cluster)
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

    static void EnsureBackplate(ICoreClientAPI capi, double size, ref LoadedTexture? backplate)
    {
        int px = Math.Max(16, (int)Math.Round(size));
        if (backplate != null && backplate.Width == px && backplate.Height == px)
        {
            return;
        }

        backplate?.Dispose();
        backplate = null;

        IAsset? fillAsset = capi.Assets.TryGet(AttributeBackplateLoc);
        if (fillAsset == null)
        {
            capi.Logger.Warning("[prosequor] Missing attribute backplate {0}.", AttributeBackplateLoc);
            return;
        }

        IAsset? borderAsset = capi.Assets.TryGet(AttributeBackplateBorderLoc);
        if (borderAsset == null)
        {
            capi.Logger.Warning(
                "[prosequor] Missing attribute backplate border {0}.",
                AttributeBackplateBorderLoc);
        }

        ImageSurface surface = new(Format.Argb32, px, px);
        Context ctx = new(surface);
        ImageSurface layer = new(Format.Argb32, px, px);
        try
        {
            capi.Gui.DrawSvg(fillAsset, layer, 0, 0, px, px, ColorUtil.BlackArgb);
            ctx.SetSourceSurface(layer, 0, 0);
            ctx.PaintWithAlpha(BackplateFillAlpha);

            if (borderAsset != null)
            {
                layer.Flush();
                using (Context clear = new(layer))
                {
                    clear.Operator = Operator.Clear;
                    clear.Paint();
                }

                capi.Gui.DrawSvg(borderAsset, layer, 0, 0, px, px, BorderArgb);
                ctx.SetSourceSurface(layer, 0, 0);
                ctx.Paint();
            }

            LoadedTexture texture = new(capi);
            capi.Gui.LoadOrUpdateCairoTexture(surface, linearMag: true, ref texture);
            backplate = texture;
        }
        finally
        {
            ctx.Dispose();
            surface.Dispose();
            layer.Dispose();
        }
    }

    static int BorderArgb =>
        ColorUtil.ColorFromRgba(
            (int)(CircleBorderColor[0] * 255),
            (int)(CircleBorderColor[1] * 255),
            (int)(CircleBorderColor[2] * 255),
            (int)(CircleBorderColor[3] * 255));
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

