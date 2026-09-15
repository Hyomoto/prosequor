using System.Collections.Generic;
using System.Reflection;
using Cairo;
using HarmonyLib;
using Prosequor.Data;
using Prosequor.Player;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace Prosequor.Client;

/// <summary>
/// Replaces the vanilla Character "Stats" side panel with attributes, vital cards, and nutrition.
/// </summary>
public class CharacterStatsPanel
{
    const double PanelInnerWidth = 250;
    const double AttrIconSize = 30;
    const double AttrRowHeight = 32;
    const double AttrRowGap = 5;
    const double AttrValueNudgeY = -5;
    const double ContentTop = 26;
    const double CardHeight = 60;
    const double TempCardHeight = 52;
    const double CardGap = 8;
    const double SectionGap = 5;
    const double CardPadX = 6;
    const double CardValueOffsetY = -2;
    const double CardLabelOffsetY = 38;
    const double NutritionHeaderHeight = 18;
    const double NutritionIconSize = 24;
    const double NutritionRowHeight = 24;
    const double NutritionRowGap = 2;
    const double NutritionBarHeight = 12;
    const double NutritionBarWidth = 120;
    const double NutritionIconGap = 6;
    const double NutritionTextHeight = 14;
    const float ComfortHighBase = 50f;

    // Vanilla bar keys and Dairy lang stay so ComposeExtraGuis injectors can
    // find the last nutrition row the same way they do on the Essentials panel.
    static readonly NutritionRowDef[] NutritionRows =
    [
        new("Freeza", "fruitBar", "fruit"),
        new("Vegita", "vegetableBar", "vegetable"),
        new("Krillin", "grainBar", "grain"),
        new("Cell", "proteinBar", "protein"),
        new("Dairy", "dairyBar", "dairy")
    ];

    static readonly double[] ComfortLowColor = [0.55, 0.75, 0.90, 1];
    static readonly double[] ComfortHighColor = [0.88, 0.65, 0.30, 1];

    readonly ICoreClientAPI capi;
    readonly AttributeIcons attributeIcons;
    readonly Dictionary<string, LoadedTexture> iconTextures = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, AssetLocation> nutritionIconLocations = new(StringComparer.OrdinalIgnoreCase)
    {
        ["fruit"] = new("prosequor", "textures/icons/fruit-nutrition.svg"),
        ["vegetable"] = new("prosequor", "textures/icons/vegetable-nutrition.svg"),
        ["grain"] = new("prosequor", "textures/icons/grain-nutrition.svg"),
        ["protein"] = new("prosequor", "textures/icons/protein-nutrition.svg"),
        ["dairy"] = new("prosequor", "textures/icons/dairy-nutrition.svg")
    };

    static readonly FieldInfo? StaticElementsField =
        AccessTools.Field(typeof(GuiComposer), "staticElements");
    static readonly FieldInfo? InteractiveElementsField =
        AccessTools.Field(typeof(GuiComposer), "interactiveElements");

    static readonly HashSet<string> OwnedBarKeys = new(StringComparer.Ordinal);

    static CharacterStatsPanel? live;

    GuiDialogCharacterBase? dlg;
    bool listenersRegistered;
    bool started;
    ElementBounds? panelBgBounds;
    ElementBounds? panelDialogBounds;
    double panelInnerHeight;
    double nutritionNextRowY;
    bool nutritionSectionPresent;

    static CharacterStatsPanel()
    {
        foreach (NutritionRowDef row in NutritionRows)
        {
            OwnedBarKeys.Add(row.BarKey);
        }
    }

    public CharacterStatsPanel(ICoreClientAPI capi)
    {
        this.capi = capi;
        attributeIcons = new AttributeIcons(capi);
    }

    public void Start()
    {
        if (started)
        {
            return;
        }

        dlg = capi.Gui.LoadedGuis.Find(g => g is GuiDialogCharacterBase) as GuiDialogCharacterBase;
        if (dlg == null)
        {
            capi.Logger.Warning("[prosequor] GuiDialogCharacterBase not found; Stats panel skipped.");
            return;
        }

        started = true;
        live = this;
        dlg.OnOpened += OnOpened;
        dlg.OnClosed += OnClosed;
        dlg.TabClicked += OnTabClicked;
        // Last subscriber: adopt extra bars after everyone else has written.
        dlg.ComposeExtraGuis += AdoptInjectedNutrition;
    }

    /// <summary>
    /// Builds <c>playerstats</c> during Essentials' <c>ComposeStatsGui</c> so later
    /// <c>ComposeExtraGuis</c> handlers can inject into it. Do not rebuild here —
    /// that would wipe those injectors.
    /// </summary>
    internal static void ComposeOwned() => live?.Compose();

    public void Dispose()
    {
        UnregisterListeners();
        attributeIcons.Dispose();
        DisposeIcons();

        if (ReferenceEquals(live, this))
        {
            live = null;
        }

        if (dlg != null)
        {
            dlg.ComposeExtraGuis -= AdoptInjectedNutrition;
            dlg.OnOpened -= OnOpened;
            dlg.OnClosed -= OnClosed;
            dlg.TabClicked -= OnTabClicked;
            dlg = null;
        }

        started = false;
    }

    void OnTabClicked(int tabIndex)
    {
        if (tabIndex != 0)
        {
            UnregisterListeners();
        }
        else if (dlg != null && dlg.IsOpened())
        {
            RegisterListeners();
        }
    }

    void OnOpened()
    {
        RegisterListeners();
    }

    void OnClosed()
    {
        UnregisterListeners();
    }

    void RegisterListeners()
    {
        if (listenersRegistered || dlg == null)
        {
            return;
        }

        EntityPlayer entity = capi.World.Player.Entity;
        entity.WatchedAttributes.RegisterModifiedListener("hunger", UpdateAll);
        entity.WatchedAttributes.RegisterModifiedListener("health", UpdateAll);
        entity.WatchedAttributes.RegisterModifiedListener("stats", UpdateAll);
        entity.WatchedAttributes.RegisterModifiedListener("bodyTemp", UpdateAll);
        entity.WatchedAttributes.RegisterModifiedListener(ProgressStore.AttrTree, UpdateAll);
        listenersRegistered = true;
    }

    void UnregisterListeners()
    {
        if (!listenersRegistered)
        {
            return;
        }

        EntityPlayer entity = capi.World.Player.Entity;
        entity.WatchedAttributes.UnregisterListener(UpdateAll);
        listenersRegistered = false;
    }

    void Compose()
    {
        if (dlg == null)
        {
            return;
        }

        var composers = dlg.Composers;
        if (composers["playercharacter"] == null || composers["environment"] == null)
        {
            return;
        }

        ElementBounds leftDlgBounds = composers["playercharacter"].Bounds;
        ElementBounds botDlgBounds = composers["environment"].Bounds;
        EntityPlayer entity = capi.World.Player.Entity;
        IPlayerProgress? progress = entity.GetBehavior<EntityBehaviorProgress>();

        double envOffset = botDlgBounds.InnerHeight / RuntimeEnv.GUIScale + 10;
        double attrsBlock =
            AttributeIds.All.Length * (AttrRowHeight + AttrRowGap) - AttrRowGap;
        double nutritionBlock = NutritionHeaderHeight + NutritionRowGap
            + NutritionRows.Length * NutritionRowHeight
            + (NutritionRows.Length - 1) * NutritionRowGap;
        double contentHeight =
            ContentTop
            + attrsBlock
            + SectionGap
            + CardHeight
            + SectionGap
            + TempCardHeight
            + SectionGap
            + nutritionBlock;
        double panelHeight = Math.Max(
            contentHeight,
            leftDlgBounds.InnerHeight / RuntimeEnv.GUIScale - GuiStyle.ElementToDialogPadding - 20 + envOffset);
        ElementBounds bgBounds = ElementBounds
            .Fixed(0, 0, PanelInnerWidth, panelHeight)
            .WithFixedPadding(GuiStyle.ElementToDialogPadding);

        ElementBounds dialogBounds = bgBounds
            .ForkBoundingParent()
            .WithAlignment(EnumDialogArea.LeftMiddle)
            .WithFixedAlignmentOffset(
                (leftDlgBounds.renderX + leftDlgBounds.OuterWidth + 10) / RuntimeEnv.GUIScale,
                envOffset / 2);

        CairoFont nameFont = CairoFont.WhiteSmallText();
        CairoFont attrValueFont = CairoFont.ButtonText()
            .WithOrientation(EnumTextOrientation.Center);
        CairoFont cardValueFont = new CairoFont()
        {
            Color = (double[])GuiStyle.DialogDefaultTextColor.Clone(),
            Fontname = GuiStyle.DecorativeFontName,
            FontWeight = FontWeight.Bold,
            UnscaledFontsize = GuiStyle.SubNormalFontSize,
            Orientation = EnumTextOrientation.Center
        };
        CairoFont healthValueFont = cardValueFont.Clone().WithColor((double[])GuiStyle.HealthBarColor.Clone());
        CairoFont satietyValueFont = cardValueFont.Clone().WithColor((double[])GuiStyle.FoodBarColor.Clone());
        CairoFont cardLabelFont = CairoFont.WhiteSmallText()
            .WithOrientation(EnumTextOrientation.Center);
        CairoFont comfortLowFont = CairoFont.WhiteDetailText()
            .WithFont(GuiStyle.DecorativeFontName)
            .WithColor((double[])ComfortLowColor.Clone())
            .WithOrientation(EnumTextOrientation.Left);
        CairoFont comfortHighFont = CairoFont.WhiteDetailText()
            .WithFont(GuiStyle.DecorativeFontName)
            .WithColor((double[])ComfortHighColor.Clone())
            .WithOrientation(EnumTextOrientation.Right);
        CairoFont bodyTempFont = CairoFont.WhiteSmallishText(GuiStyle.DecorativeFontName)
            .WithWeight(FontWeight.Bold)
            .WithOrientation(EnumTextOrientation.Center);
        CairoFont nutritionHeaderFont = CairoFont.WhiteSmallText().WithWeight(FontWeight.Bold);
        CairoFont nutritionLabelFont = CairoFont.WhiteDetailText();

        GetHealthSat(out float? health, out float? saturation);
        GetComfortRange(entity, out float comfortLow, out float comfortHigh);
        string bodyTempText = FormatBodyTemp(entity.WatchedAttributes.GetTreeAttribute("bodyTemp"));
        ITreeAttribute? hungerTree = entity.WatchedAttributes.GetTreeAttribute("hunger");

        GuiComposer composer = capi.Gui
            .CreateCompo("playerstats", dialogBounds)
            .AddShadedDialogBG(bgBounds, true)
            .AddDialogTitleBar(Lang.Get("Stats"), () => dlg.OnTitleBarClose())
            .BeginChildElements(bgBounds);

        double y = ContentTop;
        double valueColX = PanelInnerWidth - 44;
        double nameX = AttrIconSize + CardGap + 4;
        double nameWidth = valueColX - nameX - 4;
        float?[] ticks = ReadTickFractions(progress);

        for (int i = 0; i < AttributeIds.All.Length; i++)
        {
            string attrId = AttributeIds.All[i];
            ElementBounds rowBounds = ElementBounds.Fixed(0, y, PanelInnerWidth, AttrRowHeight);
            composer.AddRoundedInset(rowBounds);
            composer.AddRoundedInsetTick(
                ElementBounds.Fixed(nameX, y, nameWidth, AttrRowHeight),
                AttrTickKey(attrId),
                ticks[i]);

            ElementBounds iconBounds = ElementBounds.Fixed(
                6,
                y + (AttrRowHeight - AttrIconSize) / 2,
                AttrIconSize,
                AttrIconSize);
            ElementBounds nameBounds = ElementBounds.Fixed(
                nameX,
                y + (AttrRowHeight - 18) / 2,
                nameWidth,
                18);
            ElementBounds valueBounds = ElementBounds.Fixed(
                valueColX,
                y + (AttrRowHeight - 32) / 2 + AttrValueNudgeY,
                36,
                32);

            int score = progress?.GetAttribute(attrId) ?? AttributeGrowth.DefaultScore;
            LoadedTexture? icon = EnsureAttributeIcon(attrId, AttrIconSize);

            composer.AddDynamicText(
                Lang.Get("prosequor:attribute-" + attrId),
                nameFont,
                nameBounds,
                AttrNameKey(attrId));
            composer.AddDynamicText(
                score.ToString(),
                attrValueFont,
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

            y += AttrRowHeight + AttrRowGap;
        }

        double halfW = (PanelInnerWidth - CardGap) / 2;
        ElementBounds healthCard = ElementBounds.Fixed(0, y, halfW, CardHeight);
        ElementBounds satietyCard = ElementBounds.Fixed(halfW + CardGap, y, halfW, CardHeight);
        composer.AddRoundedInset(healthCard);
        composer.AddRoundedInset(satietyCard);

        double valueTop = y + CardValueOffsetY;
        double labelTop = y + CardLabelOffsetY;
        composer
            .AddDynamicText(
                health == null ? "-" : FormatHealth(health.Value),
                healthValueFont,
                ElementBounds.Fixed(CardPadX, valueTop, halfW - CardPadX * 2, 32),
                "health")
            .AddStaticText(
                Lang.Get("prosequor:stats-health"),
                cardLabelFont,
                ElementBounds.Fixed(CardPadX, labelTop, halfW - CardPadX * 2, 18))
            .AddDynamicText(
                saturation == null ? "-" : ((int)saturation.Value).ToString(),
                satietyValueFont,
                ElementBounds.Fixed(halfW + CardGap + CardPadX, valueTop, halfW - CardPadX * 2, 32),
                "satiety")
            .AddStaticText(
                Lang.Get("prosequor:stats-satiety"),
                cardLabelFont,
                ElementBounds.Fixed(halfW + CardGap + CardPadX, labelTop, halfW - CardPadX * 2, 18));

        y += CardHeight + SectionGap;
        ElementBounds tempCard = ElementBounds.Fixed(0, y, PanelInnerWidth, TempCardHeight);
        composer.AddRoundedInset(tempCard);

        double sideW = 60;
        composer
            .AddDynamicText(
                FormatTemp(comfortLow),
                comfortLowFont,
                ElementBounds.Fixed(10, y + 6, sideW, 22),
                "comfortlow")
            .AddDynamicText(
                FormatTemp(comfortHigh),
                comfortHighFont,
                ElementBounds.Fixed(PanelInnerWidth - sideW - 10, y + 6, sideW, 22),
                "comforthigh")
            .AddDynamicText(
                bodyTempText,
                bodyTempFont,
                ElementBounds.Fixed(sideW, y + CardValueOffsetY, PanelInnerWidth - sideW * 2, 26),
                "bodytemp")
            .AddStaticText(
                Lang.Get("prosequor:stats-temperature"),
                cardLabelFont,
                ElementBounds.Fixed(0, y + CardLabelOffsetY - 10, PanelInnerWidth, 18));

        y += TempCardHeight + SectionGap;
        nutritionSectionPresent = false;
        nutritionNextRowY = y;

        if (hungerTree != null)
        {
            double labelX = NutritionIconSize + NutritionIconGap;
            double labelWidth = PanelInnerWidth - NutritionBarWidth - labelX - NutritionIconGap;
            double barX = PanelInnerWidth - NutritionBarWidth;
            double labelYOff = (NutritionRowHeight - NutritionTextHeight) / 2;
            double barYOff = (NutritionRowHeight - NutritionBarHeight) / 2;

            composer.AddStaticText(
                Lang.Get("playerinfo-nutrition"),
                nutritionHeaderFont,
                ElementBounds.Fixed(0, y, 200, NutritionHeaderHeight));
            y += NutritionHeaderHeight + NutritionRowGap;

            for (int i = 0; i < NutritionRows.Length; i++)
            {
                NutritionRowDef row = NutritionRows[i];
                ElementBounds iconBounds = ElementBounds.Fixed(0, y, NutritionIconSize, NutritionIconSize);
                LoadedTexture? icon = EnsureNutritionIcon(row.IconId, NutritionIconSize);
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

                composer.AddStaticText(
                    Lang.Get("playerinfo-nutrition-" + row.LangSuffix),
                    nutritionLabelFont,
                    ElementBounds.Fixed(labelX, y + labelYOff, labelWidth, NutritionTextHeight));
                composer.AddStatbar(
                    ElementBounds.Fixed(barX, y + barYOff, NutritionBarWidth, NutritionBarHeight),
                    GuiStyle.FoodBarColor,
                    row.BarKey);

                y += NutritionRowHeight;
                if (i < NutritionRows.Length - 1)
                {
                    y += NutritionRowGap;
                }
            }

            nutritionSectionPresent = true;
            nutritionNextRowY = y + NutritionRowGap;
        }

        panelBgBounds = bgBounds;
        panelDialogBounds = dialogBounds;
        panelInnerHeight = panelHeight;

        composers["playerstats"] = composer
            .EndChildElements()
            .Compose();

        UpdateAll();
    }

    void UpdateAll()
    {
        if (dlg == null || !dlg.IsOpened())
        {
            return;
        }

        GuiComposer? composer = dlg.Composers["playerstats"];
        if (composer == null)
        {
            return;
        }

        EntityPlayer entity = capi.World.Player.Entity;
        IPlayerProgress? progress = entity.GetBehavior<EntityBehaviorProgress>();

        float?[] ticks = ReadTickFractions(progress);
        for (int i = 0; i < AttributeIds.All.Length; i++)
        {
            string attrId = AttributeIds.All[i];
            int score = progress?.GetAttribute(attrId) ?? AttributeGrowth.DefaultScore;
            composer.GetDynamicText(AttrValueKey(attrId))?.SetNewText(score.ToString());
            composer.GetRoundedInsetTick(AttrTickKey(attrId))?.SetTick(ticks[i]);
        }

        GetHealthSat(out float? health, out float? saturation);
        if (health != null)
        {
            composer.GetDynamicText("health")?.SetNewText(FormatHealth(health.Value));
        }

        if (saturation != null)
        {
            composer.GetDynamicText("satiety")?.SetNewText(((int)saturation.Value).ToString());
        }

        GetComfortRange(entity, out float comfortLow, out float comfortHigh);
        composer.GetDynamicText("comfortlow")?.SetNewText(FormatTemp(comfortLow));
        composer.GetDynamicText("comforthigh")?.SetNewText(FormatTemp(comfortHigh));
        composer.GetDynamicText("bodytemp")
            ?.SetNewText(FormatBodyTemp(entity.WatchedAttributes.GetTreeAttribute("bodyTemp")));

        ITreeAttribute? hungerTree = entity.WatchedAttributes.GetTreeAttribute("hunger");
        if (hungerTree == null)
        {
            return;
        }

        float maxSaturation = hungerTree.GetFloat("maxsaturation");
        if (maxSaturation <= 0)
        {
            maxSaturation = 1;
        }

        float interval = maxSaturation / 10f;
        SetNutritionBar(composer, "fruitBar", hungerTree.GetFloat("fruitLevel"), maxSaturation, interval);
        SetNutritionBar(composer, "vegetableBar", hungerTree.GetFloat("vegetableLevel"), maxSaturation, interval);
        SetNutritionBar(composer, "grainBar", hungerTree.GetFloat("grainLevel"), maxSaturation, interval);
        SetNutritionBar(composer, "proteinBar", hungerTree.GetFloat("proteinLevel"), maxSaturation, interval);
        SetNutritionBar(composer, "dairyBar", hungerTree.GetFloat("dairyLevel"), maxSaturation, interval);
    }

    static void SetNutritionBar(
        GuiComposer composer,
        string key,
        float level,
        float maxSaturation,
        float interval)
    {
        GuiElementStatbar? bar = composer.GetStatbar(key);
        if (bar == null)
        {
            return;
        }

        bar.SetLineInterval(interval);
        bar.SetValues(level, 0, maxSaturation);
        bar.ShowValueOnHover = true;
    }

    void GetHealthSat(out float? health, out float? saturation)
    {
        health = null;
        saturation = null;

        ITreeAttribute? healthTree = capi.World.Player.Entity.WatchedAttributes.GetTreeAttribute("health");
        if (healthTree != null)
        {
            health = healthTree.TryGetFloat("maxhealth");
            if (health != null)
            {
                health = (float)Math.Round(health.Value, 1);
            }
        }

        ITreeAttribute? hungerTree = capi.World.Player.Entity.WatchedAttributes.GetTreeAttribute("hunger");
        if (hungerTree != null)
        {
            float? sat = hungerTree.TryGetFloat("maxsaturation");
            if (sat != null)
            {
                saturation = (int)sat.Value;
            }
        }
    }

    void GetComfortRange(EntityPlayer entity, out float comfortLow, out float comfortHigh)
    {
        float resistance = StringUtil.ToFloat(
            capi.World.Config.GetString("bodyTemperatureResistance"),
            0f);
        float clothingWarmth = SumClothingWarmth(entity);
        comfortLow = resistance - clothingWarmth;
        comfortHigh = ComfortHighBase - clothingWarmth;
    }

    static float SumClothingWarmth(EntityPlayer entity)
    {
        float warmth = 0f;
        InventoryBase? inventory = entity.GetBehavior<EntityBehaviorPlayerInventory>()?.Inventory;
        if (inventory == null)
        {
            return warmth;
        }

        foreach (ItemSlot slot in inventory)
        {
            if (slot.Empty || slot.Itemstack == null)
            {
                continue;
            }

            IWearable? wearable = slot.Itemstack.Collectible.GetCollectibleInterface<IWearable>();
            IWearableStatsSupplier? stats = slot.Itemstack.Collectible.GetCollectibleInterface<IWearableStatsSupplier>();
            if (wearable == null || stats == null || stats.IsArmorType(slot))
            {
                continue;
            }

            warmth += wearable.GetWarmth(slot);
        }

        return warmth;
    }

    static string FormatBodyTemp(ITreeAttribute? tempTree)
    {
        if (tempTree == null)
        {
            return "-";
        }

        float baseTemp = tempTree.GetFloat("bodytemp");
        // Match vanilla: damp display when warm from fire so it stays near ~37–38°C.
        if (baseTemp > 37f)
        {
            baseTemp = 37f + (baseTemp - 37f) / 10f;
        }

        return string.Format("{0:0.#}°C", baseTemp);
    }

    static string FormatTemp(float temp) => string.Format("{0:0.#}°C", temp);

    static string FormatHealth(float health)
    {
        if (Math.Abs(health - Math.Round(health)) < 0.05f)
        {
            return ((int)Math.Round(health)).ToString();
        }

        return health.ToString("0.#");
    }

    static float?[] ReadTickFractions(IPlayerProgress? progress)
    {
        int[] scores = new int[AttributeIds.All.Length];
        float[] buckets = new float[AttributeIds.All.Length];
        for (int i = 0; i < AttributeIds.All.Length; i++)
        {
            string id = AttributeIds.All[i];
            scores[i] = progress?.GetAttribute(id) ?? AttributeGrowth.DefaultScore;
            buckets[i] = progress?.GetAttributeBucket(id) ?? 0f;
        }

        return AttributeBucketAxis.TickFractions(scores, buckets);
    }

    /// <summary>
    /// After later <c>ComposeExtraGuis</c> subscribers write extra nutrition bars
    /// (Gourmand-style), restyle those rows to our list metrics and grow the panel.
    /// Does not rebuild the composer.
    /// </summary>
    void AdoptInjectedNutrition()
    {
        if (dlg == null || !nutritionSectionPresent || panelBgBounds == null || panelDialogBounds == null)
        {
            return;
        }

        GuiComposer? composer = dlg.Composers["playerstats"];
        if (composer == null)
        {
            return;
        }

        List<GuiElementStatbar> bars = new();
        List<GuiElementStaticText> texts = new();
        CollectInjectedNutrition(composer, bars, texts);
        if (bars.Count == 0 && texts.Count == 0)
        {
            return;
        }

        bars.Sort(CompareElementY);
        texts.Sort(CompareElementY);

        double labelX = NutritionIconSize + NutritionIconGap;
        double labelWidth = PanelInnerWidth - NutritionBarWidth - labelX - NutritionIconGap;
        double barX = PanelInnerWidth - NutritionBarWidth;
        double labelYOff = (NutritionRowHeight - NutritionTextHeight) / 2;
        double barYOff = (NutritionRowHeight - NutritionBarHeight) / 2;
        CairoFont labelFont = CairoFont.WhiteDetailText();
        double y = nutritionNextRowY;
        int rows = Math.Max(bars.Count, texts.Count);

        for (int i = 0; i < rows; i++)
        {
            if (i < texts.Count)
            {
                GuiElementStaticText text = texts[i];
                text.Font = labelFont;
                text.Bounds.fixedX = labelX;
                text.Bounds.fixedY = y + labelYOff;
                text.Bounds.fixedWidth = labelWidth;
                text.Bounds.fixedHeight = NutritionTextHeight;
                text.Bounds.CalcWorldBounds();
            }

            if (i < bars.Count)
            {
                GuiElementStatbar bar = bars[i];
                bar.Bounds.fixedX = barX;
                bar.Bounds.fixedY = y + barYOff;
                bar.Bounds.fixedWidth = NutritionBarWidth;
                bar.Bounds.fixedHeight = NutritionBarHeight;
                bar.Bounds.CalcWorldBounds();
            }

            y += NutritionRowHeight + NutritionRowGap;
        }

        double usedBottom = y - NutritionRowGap;
        if (usedBottom > panelInnerHeight)
        {
            double grow = usedBottom - panelInnerHeight;
            panelBgBounds.fixedHeight += grow;
            panelDialogBounds.fixedHeight += grow;
            panelInnerHeight += grow;
            panelBgBounds.CalcWorldBounds();
            panelDialogBounds.CalcWorldBounds();
        }

        composer.Composed = false;
        composer.Compose();
    }

    void CollectInjectedNutrition(
        GuiComposer composer,
        List<GuiElementStatbar> bars,
        List<GuiElementStaticText> texts)
    {
        HashSet<string> ownedTexts = OwnedNutritionTexts();
        double textFloorY = nutritionNextRowY - NutritionRowHeight;
        ConsiderElements(StaticElementsField?.GetValue(composer), bars, texts, ownedTexts, textFloorY);
        ConsiderElements(InteractiveElementsField?.GetValue(composer), bars, texts, ownedTexts, textFloorY);
    }

    static void ConsiderElements(
        object? dictObj,
        List<GuiElementStatbar> bars,
        List<GuiElementStaticText> texts,
        HashSet<string> ownedTexts,
        double textFloorY)
    {
        if (dictObj is not Dictionary<string, GuiElement> dict)
        {
            return;
        }

        foreach (KeyValuePair<string, GuiElement> kv in dict)
        {
            if (kv.Value is GuiElementStatbar bar && !OwnedBarKeys.Contains(kv.Key))
            {
                if (!bars.Contains(bar))
                {
                    bars.Add(bar);
                }
            }
            else if (kv.Value is GuiElementStaticText text
                && !ownedTexts.Contains(text.Text)
                && text.Bounds.fixedY >= textFloorY
                && !texts.Contains(text))
            {
                texts.Add(text);
            }
        }
    }

    HashSet<string> OwnedNutritionTexts()
    {
        HashSet<string> owned = new(StringComparer.Ordinal)
        {
            Lang.Get("Stats"),
            Lang.Get("playerinfo-nutrition"),
            Lang.Get("prosequor:stats-health"),
            Lang.Get("prosequor:stats-satiety"),
            Lang.Get("prosequor:stats-temperature")
        };

        foreach (NutritionRowDef row in NutritionRows)
        {
            owned.Add(Lang.Get("playerinfo-nutrition-" + row.LangSuffix));
        }

        return owned;
    }

    static int CompareElementY(GuiElement left, GuiElement right)
    {
        return left.Bounds.fixedY.CompareTo(right.Bounds.fixedY);
    }

    static string AttrValueKey(string attrId) => "attr-value-" + attrId;

    static string AttrNameKey(string attrId) => "attr-name-" + attrId;

    static string AttrTickKey(string attrId) => "attr-tick-" + attrId;

    LoadedTexture? EnsureAttributeIcon(string attrId, double size) =>
        attributeIcons.Get(attrId, size);

    LoadedTexture? EnsureNutritionIcon(string iconId, double size)
    {
        if (!nutritionIconLocations.TryGetValue(iconId, out AssetLocation? loc) || loc == null)
        {
            return null;
        }

        return EnsureIcon(loc, "nutrition:" + iconId + "@" + (int)size, size);
    }

    LoadedTexture? EnsureIcon(AssetLocation loc, string cacheKey, double size)
    {
        if (iconTextures.TryGetValue(cacheKey, out LoadedTexture? existing))
        {
            return existing.TextureId > 0 ? existing : null;
        }

        IAsset? asset = capi.Assets.TryGet(loc);
        if (asset == null)
        {
            capi.Logger.Warning("[prosequor] Missing icon {0}.", loc);
            return null;
        }

        int px = Math.Max(8, (int)size);
        ImageSurface surface = new(Format.Argb32, px, px);
        Context ctx = new(surface);
        try
        {
            capi.Gui.DrawSvg(asset, surface, 0, 0, px, px, ColorUtil.WhiteArgb);
            LoadedTexture texture = new(capi);
            capi.Gui.LoadOrUpdateCairoTexture(surface, linearMag: true, ref texture);
            iconTextures[cacheKey] = texture;
            return texture;
        }
        finally
        {
            ctx.Dispose();
            surface.Dispose();
        }
    }

    void DisposeIcons()
    {
        foreach (LoadedTexture texture in iconTextures.Values)
        {
            texture.Dispose();
        }

        iconTextures.Clear();
    }

    readonly struct NutritionRowDef(string langSuffix, string barKey, string iconId)
    {
        public string LangSuffix { get; } = langSuffix;
        public string BarKey { get; } = barKey;
        public string IconId { get; } = iconId;
    }
}
