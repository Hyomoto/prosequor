using System.Text;
using Cairo;
using Prosequor.Ability;
using Prosequor.Data;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace Prosequor.Client;

/// <summary>
/// Class-tab right pane for create-character: Lora name, flavor box, attributes, traits, body.
/// </summary>
public sealed class CreateCharacterClassTab : IDisposable
{
    public const string ClassNameKey = "className";
    public const string ClassFlavorKey = "classFlavor";
    public const string ClassTraitsKey = "classTraits";
    public const string ClassBodyKey = "classBody";
    public const string TraitsScrollKey = "classTraitsScroll";

    const double AttrIconSize = 24;
    const double AttrRowHeight = 30;
    const double AttrRowGap = 4;
    const double AttrValueNudgeY = -5;
    const double AttrValueWidth = 40;
    const double AttrValueRightPad = 10;
    const double ColGap = 12;
    const double ScrollbarWidth = 16;
    const double FlavorPad = 8;

    static readonly double[] SectionHeaderColor = [0.91, 0.84, 0.64, 1];

    readonly ICoreClientAPI capi;
    readonly AttributeIcons icons;
    ElementBounds? traitsClipBounds;
    ElementBounds? traitsContentBounds;
    float traitsScrollY;
    double traitsContentHeight = 80;

    public CreateCharacterClassTab(ICoreClientAPI capi)
    {
        this.capi = capi;
        icons = new AttributeIcons(capi);
    }

    /// <summary>
    /// Builds the right-hand class UI starting at <paramref name="rightX"/> / <paramref name="topY"/>.
    /// </summary>
    public void Compose(
        GuiComposer composer,
        double rightX,
        double topY,
        double rightWidth,
        double availableHeight,
        Action onPrevClass,
        Action onNextClass)
    {
        traitsScrollY = 0f;

        double arrowW = 35;
        double arrowH = GuiElementPassiveItemSlot.unscaledSlotSize - 4;
        double nameW = 200;
        double selectorW = arrowW + 20 + nameW + 20 + arrowW;
        double selectorX = rightX + Math.Max(0, (rightWidth - selectorW) / 2);

        ElementBounds leftArrow = ElementBounds
            .Fixed(selectorX, topY, arrowW, arrowH)
            .WithFixedPadding(2);
        // Inset first, then text inside it — avoid ForkBoundingParent + post-hoc Y
        // (Lora metrics pushed the label out of the box).
        ElementBounds nameInset = ElementBounds.Fixed(
            leftArrow.fixedX + leftArrow.fixedWidth + 20,
            topY + 2,
            nameW + 8,
            arrowH);
        // Lora sits low in the line box — keep text near the top of the inset with full height.
        ElementBounds nameBounds = ElementBounds.Fixed(
            nameInset.fixedX + 4,
            nameInset.fixedY - 4,
            nameW,
            arrowH);
        ElementBounds rightArrow = ElementBounds
            .Fixed(nameInset.fixedX + nameInset.fixedWidth + 20, topY, arrowW, arrowH)
            .WithFixedPadding(2);

        CairoFont classNameFont = CairoFont.WhiteMediumText()
            .WithFont(GuiStyle.DecorativeFontName)
            .WithOrientation(EnumTextOrientation.Center);

        composer
            .AddIconButton("left", _ => onPrevClass(), leftArrow.FlatCopy())
            .AddInset(nameInset, 2, 0.85f)
            .AddDynamicText("Commoner", classNameFont, nameBounds, ClassNameKey)
            .AddIconButton("right", _ => onNextClass(), rightArrow.FlatCopy());

        double y = topY + arrowH + 14;
        ElementBounds flavorBox = ElementBounds.Fixed(rightX, y, rightWidth, 78);
        composer.AddRoundedInset(flavorBox);
        ElementBounds flavorText = ElementBounds.Fixed(
            rightX + FlavorPad,
            y + FlavorPad,
            rightWidth - FlavorPad * 2,
            78 - FlavorPad * 2);
        composer.AddRichtext("", CairoFont.WhiteDetailText(), flavorText, ClassFlavorKey);

        y += 78 + 12;
        double midHeight = Math.Max(160, availableHeight - (y - topY) - 90);
        double attrColW = Math.Min(230, (rightWidth - ColGap) * 0.48);
        double traitsColW = rightWidth - attrColW - ColGap;

        CairoFont sectionFont = CairoFont.WhiteSmallText()
            .WithWeight(FontWeight.Bold)
            .WithColor((double[])SectionHeaderColor.Clone());

        composer.AddStaticText(
            Lang.Get("prosequor:class-starting-attributes"),
            sectionFont,
            ElementBounds.Fixed(rightX, y, attrColW, 20));
        composer.AddStaticText(
            Lang.Get("prosequor:class-traits"),
            sectionFont,
            ElementBounds.Fixed(rightX + attrColW + ColGap, y, traitsColW, 20));

        y += 22;
        double columnsTop = y;
        double columnsH = midHeight - 22;

        ComposeAttributeColumn(composer, rightX, columnsTop, attrColW);
        ComposeTraitsColumn(
            composer,
            rightX + attrColW + ColGap,
            columnsTop,
            traitsColW,
            columnsH);

        y = columnsTop + columnsH + 10;
        double bodyH = Math.Max(48, availableHeight - (y - topY));
        ElementBounds bodyBounds = ElementBounds.Fixed(rightX, y, rightWidth, bodyH);
        composer.AddRichtext("", CairoFont.WhiteDetailText(), bodyBounds, ClassBodyKey);
    }

    void ComposeAttributeColumn(GuiComposer composer, double x, double y, double width)
    {
        CairoFont nameFont = CairoFont.WhiteSmallText();
        CairoFont valueFont = CairoFont.ButtonText()
            .WithOrientation(EnumTextOrientation.Center);

        double nameX = AttrIconSize + 10;
        double valueColX = width - AttrValueWidth - AttrValueRightPad;
        double nameWidth = Math.Max(40, valueColX - nameX - 4);
        double rowY = y;
        string defaultScore = AttributeGrowth.DefaultScore.ToString();

        foreach (string attrId in AttributeIds.All)
        {
            ElementBounds rowBounds = ElementBounds.Fixed(x, rowY, width, AttrRowHeight);
            composer.AddRoundedInset(rowBounds);

            ElementBounds iconBounds = ElementBounds.Fixed(
                x + 6,
                rowY + (AttrRowHeight - AttrIconSize) / 2,
                AttrIconSize,
                AttrIconSize);
            ElementBounds nameBounds = ElementBounds.Fixed(
                x + nameX,
                rowY + (AttrRowHeight - 18) / 2,
                nameWidth,
                18);
            ElementBounds valueBounds = ElementBounds.Fixed(
                x + valueColX,
                rowY + (AttrRowHeight - 32) / 2 + AttrValueNudgeY,
                AttrValueWidth,
                32);

            LoadedTexture? icon = icons.Get(attrId, AttrIconSize);
            composer.AddStaticText(Lang.Get("prosequor:attribute-" + attrId), nameFont, nameBounds);
            composer.AddDynamicText(
                defaultScore,
                valueFont,
                valueBounds,
                AttrValueKey(attrId));

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

            rowY += AttrRowHeight + AttrRowGap;
        }
    }

    void ComposeTraitsColumn(
        GuiComposer composer,
        double x,
        double y,
        double width,
        double height)
    {
        ElementBounds box = ElementBounds.Fixed(x, y, width, height);
        composer.AddRoundedInset(box);

        bool scrolls = true;
        double listW = scrolls ? width - ScrollbarWidth - 8 : width - 12;
        traitsClipBounds = ElementBounds.Fixed(x + 6, y + 6, listW, height - 12);
        traitsContentBounds = ElementBounds
            .Fixed(0, 0, listW, Math.Max(height - 12, 40))
            .WithParent(traitsClipBounds);
        ElementBounds scrollBounds = ElementBounds.Fixed(
            x + width - ScrollbarWidth - 4,
            y + 6,
            ScrollbarWidth,
            height - 12);

        composer.BeginClip(traitsClipBounds);
        composer.AddRichtext(
            "",
            CairoFont.WhiteDetailText(),
            traitsContentBounds,
            ClassTraitsKey);
        composer.EndClip();
        composer.AddVerticalScrollbar(OnTraitsScroll, scrollBounds, TraitsScrollKey);
    }

    void OnTraitsScroll(float value)
    {
        traitsScrollY = value;
        if (traitsContentBounds != null)
        {
            traitsContentBounds.fixedY = -traitsScrollY;
            traitsContentBounds.CalcWorldBounds();
        }
    }

    public void Refresh(GuiComposer composer, CharacterClass characterClass, CharacterSystem modSys)
    {
        composer.GetDynamicText(ClassNameKey)
            ?.SetNewText(Lang.Get("characterclass-" + characterClass.Code));

        SplitCharacterDesc(characterClass.Code, out string flavor, out string body);
        composer.GetRichtext(ClassFlavorKey)?.SetNewText(flavor, CairoFont.WhiteDetailText().WithOrientation(EnumTextOrientation.Center));
        composer.GetRichtext(ClassBodyKey)?.SetNewText(body, CairoFont.WhiteDetailText());

        Dictionary<string, int> scores = ResolveScores(characterClass.Code);
        foreach (string attrId in AttributeIds.All)
        {
            int score = scores.TryGetValue(attrId, out int s) ? s : AttributeGrowth.DefaultScore;
            composer.GetDynamicText(AttrValueKey(attrId))?.SetNewText(score.ToString());
        }

        string traitsHtml = BuildTraitsHtml(characterClass, modSys);
        GuiElementRichtext? traits = composer.GetRichtext(ClassTraitsKey);
        traits?.SetNewText(traitsHtml, CairoFont.WhiteDetailText());

        traitsContentHeight = traits?.Bounds.fixedHeight ?? 40;
        if (traitsContentBounds != null && traitsClipBounds != null)
        {
            traitsContentBounds.fixedHeight = Math.Max(traitsClipBounds.fixedHeight, traitsContentHeight);
            traitsContentBounds.fixedY = 0;
            traitsScrollY = 0f;
            traitsContentBounds.CalcWorldBounds();
        }

        GuiElementScrollbar? scrollbar = composer.GetScrollbar(TraitsScrollKey);
        if (scrollbar != null && traitsClipBounds != null)
        {
            scrollbar.SetHeights(
                (float)traitsClipBounds.fixedHeight,
                (float)Math.Max(traitsClipBounds.fixedHeight, traitsContentHeight));
            scrollbar.CurrentYPosition = 0;
        }
    }

    Dictionary<string, int> ResolveScores(string classCode)
    {
        ITraitAttributeRegistry? registry = ProsequorModSystem.For(capi)?.TraitAttributes;
        if (registry != null
            && registry.ClassStartingScores.TryGetValue(classCode, out Dictionary<string, int>? cached)
            && cached != null)
        {
            return cached;
        }

        Dictionary<string, int> fallback = new(StringComparer.OrdinalIgnoreCase);
        foreach (string id in AttributeIds.All)
        {
            fallback[id] = AttributeGrowth.DefaultScore;
        }

        return fallback;
    }

    public static void SplitCharacterDesc(string classCode, out string flavor, out string body)
    {
        SplitCharacterDescText(Lang.Get("characterdesc-" + classCode), out flavor, out body);
    }

    /// <summary>
    /// Splits a class description into flavor (quote) and body.
    /// Strips trailing whitespace / <c>&lt;br&gt;</c> spacers (vanilla trait-list padding),
    /// then cuts on the last remaining <c>&lt;br&gt;&lt;br&gt;</c>, else the last <c>&lt;br&gt;</c>.
    /// </summary>
    public static void SplitCharacterDescText(string? full, out string flavor, out string body)
    {
        string text = TrimTrailingBreaks(full ?? "");
        if (text.Length == 0)
        {
            flavor = "";
            body = "";
            return;
        }

        const string para = "<br><br>";
        const string br = "<br>";
        int paraIdx = text.LastIndexOf(para, StringComparison.OrdinalIgnoreCase);
        if (paraIdx >= 0)
        {
            flavor = text[..paraIdx].Trim();
            body = text[(paraIdx + para.Length)..].Trim();
            return;
        }

        int brIdx = text.LastIndexOf(br, StringComparison.OrdinalIgnoreCase);
        if (brIdx >= 0)
        {
            flavor = text[..brIdx].Trim();
            body = text[(brIdx + br.Length)..].Trim();
            return;
        }

        flavor = text;
        body = "";
    }

    /// <summary>Removes trailing whitespace and trailing <c>&lt;br&gt;</c> tags (case-insensitive).</summary>
    public static string TrimTrailingBreaks(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "";
        }

        string text = value;
        while (true)
        {
            text = text.TrimEnd();
            if (text.EndsWith("<br>", StringComparison.OrdinalIgnoreCase))
            {
                text = text[..^4];
                continue;
            }

            return text;
        }
    }

    public static string BuildTraitsHtml(CharacterClass characterClass, CharacterSystem modSys)
    {
        string[] traits = characterClass.Traits ?? Array.Empty<string>();
        if (traits.Length == 0)
        {
            return Lang.Get("No positive or negative traits");
        }

        StringBuilder sb = new();
        StringBuilder attrs = new();
        foreach (Trait item in traits
            .Select(code => modSys.TraitsByCode.TryGetValue(code, out Trait? t) ? t : null)
            .Where(t => t != null)
            .Cast<Trait>()
            .OrderBy(t => (int)t.Type))
        {
            attrs.Clear();
            foreach (KeyValuePair<string, double> attribute in item.Attributes)
            {
                if (attrs.Length > 0)
                {
                    attrs.Append(", ");
                }

                attrs.Append(
                    Lang.Get(
                        string.Format(
                            GlobalConstants.DefaultCultureInfo,
                            "charattribute-{0}-{1}",
                            attribute.Key,
                            attribute.Value)));
            }

            if (attrs.Length > 0)
            {
                sb.AppendLine(
                    Lang.Get(
                        "traitwithattributes",
                        Lang.Get("trait-" + item.Code),
                        attrs.ToString()));
                continue;
            }

            string? desc = Lang.GetIfExists("traitdesc-" + item.Code);
            if (desc != null)
            {
                sb.AppendLine(
                    Lang.Get(
                        "traitwithattributes",
                        Lang.Get("trait-" + item.Code),
                        desc));
            }
            else
            {
                sb.AppendLine(Lang.Get("trait-" + item.Code));
            }
        }

        return sb.Length > 0 ? sb.ToString() : Lang.Get("No positive or negative traits");
    }

    static string AttrValueKey(string attrId) => "class-attr-value-" + attrId;

    public void Dispose() => icons.Dispose();
}
