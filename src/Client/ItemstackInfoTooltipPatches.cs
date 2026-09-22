using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Reflection;
using Cairo;
using HarmonyLib;
using Prosequor.Ability;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace Prosequor.Client;

/// <summary>
/// Hover chrome: larger Lora title + affixes in a top two-column band with the preview
/// upper-right; optional stats band; full-width GetDescription body below.
/// </summary>
[HarmonyPatch(typeof(GuiElementItemstackInfo))]
public static class ItemstackInfoTooltipPatches
{
    const double PreviewScale = 0.75;
    /// <summary>Vanilla-style plate chrome around the 3D preview.</summary>
    const double PreviewPad = 40;
    /// <summary>Scale applied to <see cref="GuiElementItemstackInfo"/> outer bounds padding (~25% tighter).</summary>
    const double OuterPaddingScale = 0.75;
    /// <summary>Unscaled inset from the content edges (top / left / right) for text and preview.</summary>
    const double ContentInset = 8;
    /// <summary>Gap between the title column and the left edge of the preview plate.</summary>
    const double NamePreviewGap = 8;
    /// <summary>Extra unscaled width so title layout does not wrap at MaxLineWidth equality.</summary>
    const double TitleWrapSlack = 6;
    const double BottomPad = 8;
    /// <summary>Extra space between the top (title/preview) band and GetDescription body (or stats band).</summary>
    const double DescBandGap = 8;
    /// <summary>Unscaled size of an optional icon painted after the item name.</summary>
    const double TitleIconUnscaled = 18;
    /// <summary>Gap between the first title line and a trailing title icon.</summary>
    const double TitleIconGap = 6;
    /// <summary>Muted subtitle color for trailing parenthetical name parts.</summary>
    const string ParentheticalColor = OwnerCredit.MutedColor;
    /// <summary>Wide enough that the first title measure never wraps.</summary>
    const double UnconstrainedMeasureWidth = 10000;

    static readonly ConditionalWeakTable<GuiElementItemstackInfo, TooltipStatsHost> StatsHosts = new();
    static StyledSvgIconCache? sharedTooltipIcons;

    sealed class TooltipStatsHost
    {
        public required GuiElementTooltipStatsBand Band { get; init; }
        public int RecomposeGen;
    }

    static readonly MethodInfo GenContextMethod =
        AccessTools.Method(typeof(GuiElement), "genContext", [typeof(ImageSurface)])
        ?? throw new MissingMethodException(nameof(GuiElement), "genContext");

    static StyledSvgIconCache TooltipIcons(ICoreClientAPI capi) =>
        sharedTooltipIcons ??= new StyledSvgIconCache(capi);

    /// <summary>Dispose the shared SVG cache (call from ModSystem.Dispose).</summary>
    public static void DisposeIcons()
    {
        sharedTooltipIcons?.Dispose();
        sharedTooltipIcons = null;
    }

    static double PreviewSize => GuiElementItemstackInfo.ItemStackSize * PreviewScale;

    static double PlateUnscaled => PreviewSize + PreviewPad;

    /// <summary>Preview plate + right inset + gap after the name.</summary>
    static double TopGutter => PlateUnscaled + ContentInset + NamePreviewGap;

    [HarmonyPostfix]
    [HarmonyPatch(MethodType.Constructor, typeof(ICoreClientAPI), typeof(ElementBounds), typeof(InfoTextDelegate))]
    public static void CtorPostfix(GuiElementItemstackInfo __instance, ElementBounds bounds, ICoreClientAPI capi)
    {
        Traverse t = Traverse.Create(__instance);

        // ItemstackComponentBase uses WithFixedPadding(6 + 4*GUIScale); tighten the outer chrome only.
        bounds.fixedPaddingX *= OuterPaddingScale;
        bounds.fixedPaddingY *= OuterPaddingScale;

        CairoFont titleFont = CairoFont.WhiteSmallText()
            .WithFont(GuiStyle.DecorativeFontName)
            .WithFontSize((float)GuiStyle.SmallishFontSize)
            .WithWeight(FontWeight.Bold);
        t.Field<CairoFont>("titleFont").Value = titleFont;

        double provisionalTop = ContentInset + PlateUnscaled;
        ElementBounds textBounds = bounds.CopyOnlySize();
        ClearCopiedPadding(textBounds);
        ElementBounds descBounds = textBounds.CopyOffsetedSibling(0, provisionalTop, 0, 0);
        ClearCopiedPadding(descBounds);
        descBounds.WithParent(bounds);
        textBounds.WithParent(bounds);

        __instance.descriptionElement.Bounds = descBounds;
        __instance.titleElement.Bounds = textBounds;

        ElementBounds statsBounds = ElementBounds.Fixed(0, provisionalTop, 10, 0);
        ClearCopiedPadding(statsBounds);
        statsBounds.WithParent(bounds);
        GuiElementTooltipStatsBand statsElement = new(capi, statsBounds, TooltipIcons(capi));
        StatsHosts.Add(__instance, new TooltipStatsHost { Band = statsElement });

        __instance.onRenderStack = () => RenderPreviewRight(__instance);
    }

    [HarmonyPostfix]
    [HarmonyPatch(nameof(GuiElementItemstackInfo.Dispose))]
    public static void DisposePostfix(GuiElementItemstackInfo __instance)
    {
        if (TryGetHost(__instance, out var host))
        {
            host.RecomposeGen++;
            host.Band.Dispose();
        }
    }

    [HarmonyPrefix]
    [HarmonyPatch(nameof(GuiElementItemstackInfo.AsyncRecompose))]
    public static bool AsyncRecomposePrefix(GuiElementItemstackInfo __instance)
    {
        TryGetHost(__instance, out TooltipStatsHost? host);
        if (host != null)
        {
            host.RecomposeGen++;
        }

        int gen = host?.RecomposeGen ?? 0;
        ItemSlot? curSlot = __instance.curSlot;
        if (curSlot?.Itemstack == null)
        {
            host?.Band.SetCells(null);
            return false;
        }

        Traverse t = Traverse.Create(__instance);
        ICoreClientAPI api = t.Field<ICoreClientAPI>("api").Value
            ?? throw new InvalidOperationException("GuiElementItemstackInfo.api is null");
        CairoFont titleFont = t.Field<CairoFont>("titleFont").Value;
        CairoFont descFont = __instance.Font;
        double maxWidth = t.Field<double>("maxWidth").Value;
        InfoTextDelegate onRequireInfoText = t.Field<InfoTextDelegate>("OnRequireInfoText").Value;

        __instance.Dirty = true;

        // face+size on each affix span: nested <font color> would otherwise drop face and
        // inherit the decorative (Lora) title CairoFont.
        string? headerAffixes = ItemAffixes.FormatHeaderNames(
            curSlot.Itemstack,
            GuiStyle.StandardFontName,
            ((int)GuiStyle.DetailFontSize).ToString());
        string stackName = curSlot.GetStackName()?.Trim() ?? "";
        SplitTitleParts(stackName, out string mainName, out string? parenthetical);
        string title = FormatTooltipTitle(mainName, parenthetical, headerAffixes);
        string desc = TrimDescription(onRequireInfoText(curSlot));
        if (ArmorTooltipStatsBand.IsEligible(curSlot))
        {
            desc = ArmorTooltipStatsBand.StripHoverLines(desc, curSlot);
            desc = TrimDescription(desc);
        }
        else if (ClothingTooltipStatsBand.IsEligible(curSlot))
        {
            desc = ClothingTooltipStatsBand.StripHoverLines(desc, curSlot);
            desc = TrimDescription(desc);
        }
        else if (WeaponToolTooltipStatsBand.IsEligible(curSlot))
        {
            desc = WeaponToolTooltipStatsBand.StripHoverLines(desc, curSlot);
            desc = TrimDescription(desc);
        }

        desc = ItemAffixes.AppendQualityFooter(desc, curSlot.Itemstack);
        desc = OwnerCredit.AppendForStack(desc, api.World, curSlot.Itemstack);

        bool hasStatsBand = ItemTooltipStatsBand.TryResolve(curSlot, out ItemTooltipStatsBandRequest statsRequest);
        TooltipTitleIcon titleIcon = hasStatsBand ? statsRequest.TitleIcon : default;
        GuiElementTooltipStatsBand? statsElement = host?.Band;

        ClearCopiedPadding(__instance.titleElement.Bounds);
        ClearCopiedPadding(__instance.descriptionElement.Bounds);
        if (statsElement != null)
        {
            ClearCopiedPadding(statsElement.Bounds);
        }

        // Unconstrained title measure → true single-line MaxLineWidth (avoids premature wrap).
        __instance.titleElement.Bounds.fixedX = ContentInset;
        __instance.titleElement.Bounds.fixedY = ContentInset;
        __instance.titleElement.Bounds.fixedWidth = UnconstrainedMeasureWidth;

        __instance.descriptionElement.Bounds.fixedX = ContentInset;
        __instance.descriptionElement.Bounds.fixedY = ContentInset + PlateUnscaled;
        __instance.descriptionElement.Bounds.fixedWidth = Math.Max(40, maxWidth - ContentInset * 2);
        __instance.descriptionElement.Bounds.CalcWorldBounds();
        __instance.titleElement.Bounds.CalcWorldBounds();

        __instance.titleElement.SetNewTextWithoutRecompose(title, titleFont, null, true);
        __instance.descriptionElement.SetNewTextWithoutRecompose(desc, descFont, null, true);

        if (statsElement != null)
        {
            statsElement.SetCells(hasStatsBand ? statsRequest.Cells : null);
        }

        RecalcZoneBounds(
            __instance,
            maxWidth,
            statsElement,
            titleFont,
            mainName,
            titleIcon);
        __instance.Bounds.CalcWorldBounds();

        ElementBounds textBounds = __instance.Bounds.CopyOnlySize();
        textBounds.CalcWorldBounds();

        double previewSize = PreviewSize;
        double plateUnscaled = PlateUnscaled;
        double[] titleColor = titleFont.Color ?? [1, 1, 1, 1];

        TyronThreadPool.QueueTask(() =>
        {
            if (host != null && gen != host.RecomposeGen)
            {
                return;
            }

            ImageSurface surface = new(Format.Argb32, __instance.Bounds.OuterWidthInt, __instance.Bounds.OuterHeightInt);
            Context ctx = (Context)GenContextMethod.Invoke(__instance, [surface])!;

            ctx.SetSourceRGBA(0, 0, 0, 0);
            ctx.Paint();

            double[] backTint = GuiStyle.DialogStrongBgColor;
            ctx.SetSourceRGBA(backTint[0], backTint[1], backTint[2], backTint[3]);
            GuiElement.RoundRectangle(
                ctx,
                textBounds.bgDrawX,
                textBounds.bgDrawY,
                textBounds.OuterWidthInt,
                textBounds.OuterHeightInt,
                GuiStyle.DialogBGRadius);
            ctx.FillPreserve();

            ctx.SetSourceRGBA(
                GuiStyle.DialogLightBgColor[0] * 1.4,
                GuiStyle.DialogStrongBgColor[1] * 1.4,
                GuiStyle.DialogStrongBgColor[2] * 1.4,
                1);
            ctx.LineWidth = 3 * 1.75;
            ctx.StrokePreserve();
            surface.BlurFull(8.2);

            ctx.SetSourceRGBA(backTint[0] / 2, backTint[1] / 2, backTint[2] / 2, backTint[3]);
            ctx.Stroke();

            int w = (int)(GuiElement.scaled(previewSize) + GuiElement.scaled(PreviewPad));
            int h = (int)(GuiElement.scaled(previewSize) + GuiElement.scaled(PreviewPad));

            ImageSurface shSurface = new(Format.Argb32, w, h);
            Context shCtx = (Context)GenContextMethod.Invoke(__instance, [shSurface])!;

            shCtx.SetSourceRGBA(GuiStyle.DialogSlotBackColor);
            GuiElement.RoundRectangle(shCtx, 0, 0, w, h, 0);
            shCtx.FillPreserve();

            shCtx.SetSourceRGBA(GuiStyle.DialogSlotFrontColor);
            shCtx.LineWidth = 5;
            shCtx.Stroke();
            shSurface.BlurFull(7);
            shSurface.BlurFull(7);
            shSurface.BlurFull(7);
            __instance.EmbossRoundRectangleElement(shCtx, 0, 0, w, h, true);

            // drawX is already past padding; place against the inner right edge, then inset.
            int plateX = (int)(textBounds.drawX + textBounds.InnerWidth - w - GuiElement.scaled(ContentInset));
            int plateY = (int)(textBounds.drawY + GuiElement.scaled(ContentInset));
            ctx.SetSourceSurface(shSurface, plateX, plateY);
            ctx.Rectangle(plateX, plateY, w, h);
            ctx.Fill();

            shCtx.Dispose();
            shSurface.Dispose();

            api.Event.EnqueueMainThreadTask(() =>
            {
                if (host != null && gen != host.RecomposeGen)
                {
                    ctx.Dispose();
                    surface.Dispose();
                    return;
                }

                // Paint the stats row onto the chrome surface after blur so it stays sharp,
                // in the same generation that clears Dirty. No separate GPU overlay.
                if (statsElement != null && statsElement.HasCells)
                {
                    double bandX = textBounds.drawX + GuiElement.scaled(statsElement.Bounds.fixedX);
                    double bandY = textBounds.drawY + GuiElement.scaled(statsElement.Bounds.fixedY);
                    double bandW = GuiElement.scaled(statsElement.Bounds.fixedWidth);
                    double bandH = GuiElement.scaled(statsElement.Bounds.fixedHeight);
                    if (!statsElement.Paint(ctx, bandX, bandY, bandW, bandH))
                    {
                        ctx.Dispose();
                        surface.Dispose();
                        // Keep Dirty: a hidden tooltip is better than a visible empty hole.
                        return;
                    }
                }

                if (!titleIcon.IsEmpty)
                {
                    PaintTitleIcon(api, ctx, textBounds, titleFont, mainName, titleIcon, titleColor);
                }

                __instance.titleElement.Compose(false);
                __instance.descriptionElement.Compose(false);

                LoadedTexture texture = __instance.texture;
                api.Gui.LoadOrUpdateCairoTexture(surface, true, ref texture);
                __instance.texture = texture;

                ctx.Dispose();
                surface.Dispose();

                ElementBounds scissorBounds = ElementBounds
                    .Fixed(
                        __instance.Bounds.fixedWidth - plateUnscaled - ContentInset,
                        ContentInset,
                        plateUnscaled,
                        plateUnscaled)
                    .WithParent(__instance.Bounds);
                scissorBounds.CalcWorldBounds();
                t.Field<ElementBounds>("scissorBounds").Value = scissorBounds;

                __instance.Dirty = false;
            }, "genstackinfotexture");
        }, nameof(ItemstackInfoTooltipPatches));

        return false;
    }

    static void RecalcZoneBounds(
        GuiElementItemstackInfo el,
        double maxWidth,
        GuiElementTooltipStatsBand? statsElement,
        CairoFont titleFont,
        string mainName,
        TooltipTitleIcon titleIcon)
    {
        el.descriptionElement.BeforeCalcBounds();
        el.titleElement.BeforeCalcBounds();

        // MaxLineWidth is scaled pixels; convert back to unscaled fixed coords.
        double naturalTitle = el.titleElement.MaxLineWidth / RuntimeEnv.GUIScale;
        double naturalDesc = el.descriptionElement.MaxLineWidth / RuntimeEnv.GUIScale;

        // Include a trailing title icon in the name-line budget when present.
        double titleBudget = naturalTitle + TitleWrapSlack;
        if (!titleIcon.IsEmpty && !string.IsNullOrEmpty(mainName))
        {
            double nameUnscaled = titleFont.GetTextExtents(mainName).Width / RuntimeEnv.GUIScale;
            double withIcon = nameUnscaled + TitleIconUnscaled + TitleIconGap;
            titleBudget = Math.Max(titleBudget, withIcon + TitleWrapSlack);
        }

        double currentWidth = Math.Max(
            titleBudget + TopGutter + ContentInset,
            naturalDesc + ContentInset * 2);

        // Short names (e.g. "Gold cleaver") must still fit the weighted stats columns.
        if (statsElement != null && statsElement.HasCells)
        {
            double statsMinBody = statsElement.PreferredUnscaledMinWidth;
            if (statsMinBody > 0)
            {
                currentWidth = Math.Max(currentWidth, statsMinBody + ContentInset * 2);
            }
        }

        currentWidth = Math.Min(Math.Max(currentWidth, TopGutter + ContentInset + 80), maxWidth);

        double titleColWidth = Math.Max(40, currentWidth - TopGutter - ContentInset);
        double bodyWidth = Math.Max(40, currentWidth - ContentInset * 2);

        el.Bounds.fixedWidth = currentWidth;

        el.titleElement.Bounds.fixedX = ContentInset;
        el.titleElement.Bounds.fixedY = ContentInset;
        el.titleElement.Bounds.fixedWidth = titleColWidth;
        el.titleElement.Bounds.CalcWorldBounds();
        el.titleElement.BeforeCalcBounds();

        double headerHeight = Math.Max(
            el.titleElement.TotalHeight / RuntimeEnv.GUIScale,
            el.titleElement.Bounds.fixedHeight);
        headerHeight = Math.Max(headerHeight, 1);

        double previewBand = ContentInset + PlateUnscaled;
        double headerBottom = Math.Max(headerHeight + ContentInset, previewBand);
        double afterHeader = headerBottom + DescBandGap;

        el.titleElement.Bounds.fixedHeight = headerHeight;

        // Hole only when the band actually has cells — PreferredUnscaledHeight is 0 when empty.
        double statsHeight = 0;
        if (statsElement != null && statsElement.HasCells)
        {
            statsHeight = statsElement.PreferredUnscaledHeight;
            statsElement.Bounds.fixedX = ContentInset;
            statsElement.Bounds.fixedY = afterHeader;
            statsElement.Bounds.fixedWidth = bodyWidth;
            statsElement.Bounds.fixedHeight = statsHeight;
            statsElement.Bounds.CalcWorldBounds();
        }
        else if (statsElement != null)
        {
            statsElement.SetCells(null);
            statsElement.Bounds.fixedX = ContentInset;
            statsElement.Bounds.fixedY = afterHeader;
            statsElement.Bounds.fixedWidth = bodyWidth;
            statsElement.Bounds.fixedHeight = 0;
            statsElement.Bounds.CalcWorldBounds();
        }

        double descY = afterHeader + statsHeight + (statsHeight > 0 ? DescBandGap : 0);
        el.descriptionElement.Bounds.fixedX = ContentInset;
        el.descriptionElement.Bounds.fixedY = descY;
        el.descriptionElement.Bounds.fixedWidth = bodyWidth;
        el.descriptionElement.Bounds.CalcWorldBounds();
        el.descriptionElement.BeforeCalcBounds();

        double descHeight = Math.Max(
            el.descriptionElement.TotalHeight / RuntimeEnv.GUIScale,
            el.descriptionElement.Bounds.fixedHeight);
        el.descriptionElement.Bounds.fixedHeight = descHeight;
        el.Bounds.fixedHeight = BottomPad + descY + descHeight;
    }

    /// <summary>
    /// Paint a title-colored icon immediately after the first name line.
    /// SVG assets use <see cref="StyledSvgIconCache"/>; clothing uses IconUtil built-ins.
    /// A missing asset is a soft miss — does not keep Dirty.
    /// </summary>
    static void PaintTitleIcon(
        ICoreClientAPI api,
        Context ctx,
        ElementBounds textBounds,
        CairoFont titleFont,
        string mainName,
        TooltipTitleIcon titleIcon,
        double[] titleColor)
    {
        if (string.IsNullOrEmpty(mainName) || titleIcon.IsEmpty)
        {
            return;
        }

        double nameW = titleFont.GetTextExtents(mainName).Width;
        FontExtents fontExtents = titleFont.GetFontExtents();
        double iconPx = GuiElement.scaled(TitleIconUnscaled);
        double gapPx = GuiElement.scaled(TitleIconGap);
        double originX = textBounds.drawX + GuiElement.scaled(ContentInset);
        double originY = textBounds.drawY + GuiElement.scaled(ContentInset);
        double iconX = originX + nameW + gapPx;
        double iconY = originY + (fontExtents.Height - iconPx) / 2;

        double r = titleColor.Length > 0 ? titleColor[0] : 1;
        double g = titleColor.Length > 1 ? titleColor[1] : 1;
        double b = titleColor.Length > 2 ? titleColor[2] : 1;
        double a = titleColor.Length > 3 ? titleColor[3] : 1;
        double[] rgba = [r, g, b, a];

        if (titleIcon.Svg != null)
        {
            sharedTooltipIcons?.TryPaintFlat(ctx, titleIcon.Svg, iconX, iconY, iconPx, r, g, b, a);
            return;
        }

        if (!string.IsNullOrEmpty(titleIcon.BuiltIn))
        {
            api.Gui.Icons.DrawIcon(ctx, titleIcon.BuiltIn, iconX, iconY, iconPx, iconPx, rgba);
        }
    }

    /// <summary>
    /// Drop trailing whitespace / blank lines so they do not inflate tooltip height.
    /// </summary>
    static string TrimDescription(string? desc)
    {
        if (string.IsNullOrEmpty(desc))
        {
            return "";
        }

        string trimmed = desc.TrimEnd();
        // Collapse any leftover trailing blank lines after mixed \r\n / spaces.
        while (trimmed.EndsWith("\n", StringComparison.Ordinal)
            || trimmed.EndsWith("\r", StringComparison.Ordinal))
        {
            trimmed = trimmed.TrimEnd();
        }

        return trimmed;
    }

    /// <summary>
    /// Main name in title font; trailing <c>(…)</c> as a smaller subtitle; then affixes.
    /// </summary>
    static string FormatTooltipTitle(string main, string? parenthetical, string? headerAffixes)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append(EscapeTitleVtml(main));

        if (!string.IsNullOrEmpty(parenthetical))
        {
            sb.Append("\n<font face=\"")
                .Append(GuiStyle.StandardFontName)
                .Append("\" size=\"")
                .Append(((int)GuiStyle.DetailFontSize).ToString())
                .Append("\" color=\"")
                .Append(ParentheticalColor)
                .Append("\">")
                .Append(EscapeTitleVtml(parenthetical))
                .Append("</font>");
        }

        if (!string.IsNullOrEmpty(headerAffixes))
        {
            // Separators inherit this face; each colored name already carries face+size.
            sb.Append("\n<font face=\"")
                .Append(GuiStyle.StandardFontName)
                .Append("\" size=\"")
                .Append(((int)GuiStyle.DetailFontSize).ToString())
                .Append("\">")
                .Append(headerAffixes)
                .Append("</font>");
        }

        return sb.ToString();
    }

    /// <summary>
    /// Splits a trailing <c>Name (variant)</c> into main + parenthetical (parens kept).
    /// </summary>
    static void SplitTitleParts(string name, out string main, out string? parenthetical)
    {
        main = name;
        parenthetical = null;
        if (TrySplitTrailingParenthetical(name, out string head, out string paren))
        {
            main = head;
            parenthetical = paren;
        }
    }

    /// <summary>
    /// Splits a trailing <c>Name (variant)</c> into main + parenthetical (parens kept).
    /// </summary>
    static bool TrySplitTrailingParenthetical(string name, out string main, out string parenthetical)
    {
        main = name;
        parenthetical = "";

        int close = name.LastIndexOf(')');
        if (close != name.Length - 1)
        {
            return false;
        }

        int open = name.LastIndexOf('(');
        if (open <= 0)
        {
            return false;
        }

        // Require a space (or start boundary) before '(' so we don't split "Item(x)".
        if (!char.IsWhiteSpace(name[open - 1]))
        {
            return false;
        }

        string head = name[..open].TrimEnd();
        string paren = name[open..(close + 1)].Trim();
        if (head.Length == 0 || paren.Length < 3)
        {
            return false;
        }

        main = head;
        parenthetical = paren;
        return true;
    }

    static string EscapeTitleVtml(string text) =>
        text.Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal);

    static void RenderPreviewRight(GuiElementItemstackInfo el)
    {
        if (el.curSlot?.Itemstack == null)
        {
            return;
        }

        Traverse t = Traverse.Create(el);
        ICoreClientAPI api = t.Field<ICoreClientAPI>("api").Value;
        double previewSize = PreviewSize;
        double plate = PlateUnscaled;
        double x = el.Bounds.renderX
            + el.Bounds.absPaddingX
            + el.Bounds.InnerWidth
            - GuiElement.scaled(ContentInset + plate / 2);
        double y = el.Bounds.renderY
            + el.Bounds.absPaddingY
            + GuiElement.scaled(ContentInset + plate / 2);

        api.Render.RenderItemstackToGui(
            el.curSlot,
            (int)x,
            (int)y,
            1000 + GuiElement.scaled(GuiElementPassiveItemSlot.unscaledItemSize) * 2,
            (float)GuiElement.scaled(previewSize),
            ColorUtil.WhiteArgb,
            true,
            true,
            false);
    }

    /// <summary>
    /// <see cref="ElementBounds.CopyOnlySize"/> copies parent padding; that double-insets
    /// title/desc against the tooltip padding and makes the preview look flush-right.
    /// </summary>
    static void ClearCopiedPadding(ElementBounds bounds)
    {
        bounds.fixedPaddingX = 0;
        bounds.fixedPaddingY = 0;
    }

    static bool TryGetHost(GuiElementItemstackInfo el, [NotNullWhen(true)] out TooltipStatsHost? host) =>
        StatsHosts.TryGetValue(el, out host);
}
