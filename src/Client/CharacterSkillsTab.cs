using System.Collections.Generic;
using System.Reflection;
using Cairo;
using HarmonyLib;
using Prosequor.Data;
using Prosequor.Network;
using Prosequor.Player;
using Prosequor.Progress;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace Prosequor.Client;

/// <summary>
/// Vanilla C-menu Skills tab: skill list, and in-tab skill-tree detail with Back.
/// </summary>
public class CharacterSkillsTab
{
    const string PlayerBarKey = "prosequor-playerbar";
    const string ScrollbarKey = "prosequor-skillscroll";
    const string HeaderLevelKey = "prosequor-header-level";
    const string PointsKey = "prosequor-points";
    const string DetailPointsKey = "prosequor-detail-points";
    const string DetailLevelKey = "prosequor-detail-level";

    const double FallbackWidth = 385;
    const double FallbackHeight = 344;

    const double TopMargin = 24;
    const double Gap = 6;
    const double HeaderHeight = 22;
    const double FooterHeight = 20;
    const double LineHeight = 2;
    const double RowHeight = 26;
    const double SectionHeaderHeight = 28;
    const double BarHeight = 16;
    const double IconSize = 18;
    const double CollapseIconSize = 14;
    const double BackButtonWidth = 70;

    static readonly double[] SpecializationBarColor = ColorUtil.Hex2Doubles("#E8C95B");
    static readonly double[] MinorBarColor = ColorUtil.Hex2Doubles("#79B9D1");
    static readonly double[] HobbyBarColor = ColorUtil.Hex2Doubles("#B99AD6");
    static readonly double[] PassiveBarColor = ColorUtil.Hex2Doubles("#91C77B");
    const double LevelColumnWidth = 30;
    const double RowPadding = 4;
    const double ScrollbarWidth = 20;
    const int TreeTooltipWidth = 360;

    const string TreeViewportKey = "prosequor-tree-viewport";
    const string TreeHoverKey = "prosequor-tree-hover";
    const string ListTooltipKey = "prosequor-list-tooltip";
    const string ListBarTooltipKey = "prosequor-list-bar-tooltip";

    static readonly AssetLocation SectionExpandedLoc =
        new("prosequor", "textures/gui/section-expanded.png");
    static readonly AssetLocation SectionCollapsedLoc =
        new("prosequor", "textures/gui/section-collapsed.png");
    static readonly AssetLocation SkillUnlockSound =
        new(ProsequorModSystem.ModId, "sounds/skill-unlock.ogg");
    static readonly AssetLocation SpecializationUnlockSound =
        new(ProsequorModSystem.ModId, "sounds/specialization-unlock.ogg");
    static readonly AssetLocation TabSelectSound =
        new(ProsequorModSystem.ModId, "sounds/tab-select.ogg");
    static readonly AssetLocation SubSelectSound =
        new(ProsequorModSystem.ModId, "sounds/sub-select.ogg");
    static readonly AssetLocation SubRetreatSound =
        new(ProsequorModSystem.ModId, "sounds/sub-retreat.ogg");
    static readonly AssetLocation SkillLockedSound =
        new(ProsequorModSystem.ModId, "sounds/skill-locked.ogg");
    const float UnlockSoundVolume = 1f;

    readonly ICoreClientAPI capi;
    readonly ProgressNetwork network;

    GuiDialogCharacterBase? dlg;
    int tabIndex = -1;
    float scrollY;
    ElementBounds? contentBounds;
    GuiComposer? activeComposer;
    EntityBehaviorProgress? observedProgress;
    readonly List<string> composedSkillIds = new();
    readonly Dictionary<string, LoadedTexture> iconTextures = new(StringComparer.OrdinalIgnoreCase);
    LoadedTexture? sectionHeaderTexture;
    int sectionHeaderTexW;
    int sectionHeaderTexH;
    readonly Dictionary<SkillMenuSectionKind, bool> sectionCollapsed = new()
    {
        [SkillMenuSectionKind.Specializations] = false,
        [SkillMenuSectionKind.MinorSkills] = false,
        [SkillMenuSectionKind.HobbySkills] = false,
        [SkillMenuSectionKind.Passives] = false
    };

    int sectionExpandedTex;
    int sectionCollapsedTex;
    bool sectionTexturesResolved;

    string? selectedSkillId;
    double tabWidth = FallbackWidth;
    double tabHeight = FallbackHeight;

    /// <summary>
    /// Content size of the Skills tab as last composed by the character dialog. The layout
    /// editor mirrors the derived viewport size so previews crop exactly like the real panel.
    /// Stays at the fallback until the tab has been composed once this session.
    /// </summary>
    public static double TabWidth { get; private set; } = FallbackWidth;

    public static double TabHeight { get; private set; } = FallbackHeight;

    public static double TreeAreaTop => TopMargin - 4 + 22 + Gap;

    public static double TreeAreaHeight => Math.Max(80, TabHeight - TreeAreaTop - 8);

    public static double TreeViewportWidth => TabWidth - 8;

    public static double TreeViewportHeight => TreeAreaHeight - 8;

    string detailSignature = "";
    bool recomposePending;
    MethodInfo? recomposeMethod;
    bool recomposeMethodResolved;
    bool recomposeWarned;
    GuiElementSkillTreeViewport? activeViewport;

    public CharacterSkillsTab(ICoreClientAPI capi, ProgressNetwork network)
    {
        this.capi = capi;
        this.network = network;
        network.UnlockResultReceived += OnUnlockResult;
    }

    public void Start()
    {
        dlg = capi.Gui.LoadedGuis.Find(g => g is GuiDialogCharacterBase) as GuiDialogCharacterBase;
        if (dlg == null)
        {
            capi.Logger.Warning("[prosequor] GuiDialogCharacterBase not found; Skills tab skipped.");
            return;
        }

        tabIndex = dlg.Tabs.Count;
        dlg.Tabs.Add(new GuiTab
        {
            Name = Lang.Get("prosequor:charactertab-skills"),
            DataInt = tabIndex
        });
        dlg.RenderTabHandlers.Add(Compose);
        dlg.TabClicked += OnTabClicked;
    }

    /// <summary>
    /// Selects the Skills tab on the open character dialog (used by the skill-waiting HUD).
    /// Prefers vanilla <c>onTabClicked</c> so TabClicked listeners hide equipment extras.
    /// When <paramref name="openSkillId"/> is set, opens that skill's tree after the tab switch.
    /// </summary>
    public bool TrySelectTab(string? openSkillId = null)
    {
        if (dlg == null || tabIndex < 0)
        {
            return false;
        }

        MethodInfo? onTabClicked = AccessTools.Method(dlg.GetType(), "onTabClicked", [typeof(int)]);
        if (onTabClicked != null)
        {
            onTabClicked.Invoke(dlg, [tabIndex]);
            TryOpenSkillDetail(openSkillId);
            return true;
        }

        FieldInfo? curTabField = AccessTools.Field(dlg.GetType(), "curTab");
        if (curTabField == null)
        {
            return false;
        }

        curTabField.SetValue(dlg, tabIndex);
        if (!recomposeMethodResolved)
        {
            recomposeMethod = FindRecomposeMethod(dlg.GetType());
            recomposeMethodResolved = true;
        }

        recomposeMethod?.Invoke(dlg, null);
        TryOpenSkillDetail(openSkillId);
        return recomposeMethod != null;
    }

    void TryOpenSkillDetail(string? skillId)
    {
        if (string.IsNullOrWhiteSpace(skillId))
        {
            return;
        }

        OnSkillClicked(skillId);
    }

    public void Dispose()
    {
        network.UnlockResultReceived -= OnUnlockResult;
        ObserveProgress(null);
        activeComposer = null;
        DisposeIcons();

        if (dlg != null)
        {
            dlg.TabClicked -= OnTabClicked;
        }
    }

    void OnTabClicked(int index)
    {
        capi.Gui.PlaySound(TabSelectSound, randomizePitch: false, UnlockSoundVolume);

        // GuiDialogCharacter clears its composer immediately after this event.
        activeComposer = null;
        activeViewport = null;
        selectedSkillId = null;
        scrollY = 0f;
    }

    void Compose(GuiComposer compo)
    {
        IPlayerProgress? progress = capi.World.Player.Entity.GetBehavior<EntityBehaviorProgress>();
        ISkillRegistry? registry = ProsequorModSystem.For(capi)?.Registry;
        ObserveProgress(progress as EntityBehaviorProgress);
        activeComposer = compo;
        activeViewport = null;
        composedSkillIds.Clear();

        CaptureTabBounds(compo);

        if (selectedSkillId != null)
        {
            ComposeDetail(compo, progress, registry, selectedSkillId);
        }
        else
        {
            ComposeList(compo, progress, registry);
        }
    }

    void CaptureTabBounds(GuiComposer compo)
    {
        ElementBounds parent = compo.CurParentBounds;
        tabWidth = parent.fixedWidth > 50 ? parent.fixedWidth : FallbackWidth;
        tabHeight = parent.fixedHeight > 50 ? parent.fixedHeight : FallbackHeight;
        TabWidth = tabWidth;
        TabHeight = tabHeight;
    }

    void ComposeList(GuiComposer compo, IPlayerProgress? progress, ISkillRegistry? registry)
    {
        double width = tabWidth;
        double height = tabHeight;

        CairoFont headerFont = CairoFont.WhiteSmallText();
        CairoFont sectionFont = CairoFont.WhiteSmallText()
            .WithOrientation(EnumTextOrientation.Center);
        CairoFont rowFont = CairoFont.WhiteDetailText();
        CairoFont levelFont = CairoFont.WhiteDetailText().WithOrientation(EnumTextOrientation.Right);

        int playerLevel = progress?.PlayerLevel ?? XpCurves.PlayerMinLevel;
        int points = progress?.UnlockPoints ?? 0;

        double levelLabelWidth = 76;
        compo.AddDynamicText(Lang.Get("prosequor:prosequor-header-level", playerLevel), headerFont,
            ElementBounds.Fixed(0, TopMargin, levelLabelWidth, HeaderHeight), HeaderLevelKey);

        double playerBarX = levelLabelWidth + Gap;
        ElementBounds playerBarBounds =
            ElementBounds.Fixed(playerBarX, TopMargin + 3, width - playerBarX, BarHeight);
        compo.AddStatbar(playerBarBounds, GuiStyle.XPBarColor, PlayerBarKey);
        SetBar(compo.GetStatbar(PlayerBarKey), progress, null);

        double topLineY = TopMargin + HeaderHeight + Gap;
        double footerY = height - FooterHeight - 2;
        double bottomLineY = footerY - Gap - LineHeight;
        double listTop = topLineY + LineHeight + Gap;
        double listHeight = Math.Max(60, bottomLineY - Gap - listTop);

        compo
            .AddInset(ElementBounds.Fixed(0, topLineY, width, LineHeight), 2)
            .AddInset(ElementBounds.Fixed(0, bottomLineY, width, LineHeight), 2)
            .AddDynamicText(Lang.Get("prosequor:prosequor-header-points", points), rowFont,
                ElementBounds.Fixed(0, footerY, width, FooterHeight), PointsKey);

        SkillMenuIndex menu = registry?.MenuIndex ?? SkillMenuIndex.Empty;
        EnsureSectionTextures();

        double contentHeight = 4;
        foreach (SkillMenuSection section in menu.Sections)
        {
            contentHeight += SectionHeaderHeight;
            if (!IsCollapsed(section.Kind))
            {
                contentHeight += section.Skills.Count * RowHeight;
            }
        }

        bool scrolls = contentHeight > listHeight + 1;
        double listWidth = scrolls ? width - ScrollbarWidth - 4 : width;
        if (!scrolls)
        {
            scrollY = 0f;
        }

        ElementBounds clipBounds = ElementBounds.Fixed(0, listTop, listWidth, listHeight);
        contentBounds = ElementBounds
            .Fixed(0, -scrollY, listWidth, Math.Max(listHeight, contentHeight))
            .WithParent(clipBounds);

        compo.BeginClip(clipBounds);

        double textStartX = RowPadding + IconSize + Gap;
        double nameWidth = Math.Max(80, listWidth * 0.40 - IconSize);
        double levelX = textStartX + nameWidth + Gap;
        double barX = levelX + LevelColumnWidth + Gap;
        double barWidth = Math.Max(40, listWidth - barX - RowPadding * 2);

        List<SkillListHoverRow> hoverRows = new();
        List<SkillBarHoverRow> barHoverRows = new();
        double y = 2;
        foreach (SkillMenuSection section in menu.Sections)
        {
            bool collapsed = IsCollapsed(section.Kind);
            SkillMenuSectionKind kind = section.Kind;

            ElementBounds headerBounds = ElementBounds
                .Fixed(RowPadding, y, listWidth - RowPadding * 2, SectionHeaderHeight - 4)
                .WithParent(contentBounds);
            ElementBounds headerClick = ElementBounds
                .Fixed(0, y, listWidth, SectionHeaderHeight - 2)
                .WithParent(contentBounds);

            // Insets are Cairo-baked at compose time and do not follow scroll; draw a live texture.
            LoadedTexture? headerBg = EnsureSectionHeaderTexture(
                (int)(listWidth - RowPadding * 2),
                (int)(SectionHeaderHeight - 4));
            if (headerBg != null && headerBg.TextureId > 0)
            {
                LoadedTexture bg = headerBg;
                compo.AddCustomRender(headerBounds, (_, bounds) =>
                {
                    capi.Render.Render2DTexturePremultipliedAlpha(
                        bg.TextureId,
                        (float)bounds.renderX,
                        (float)bounds.renderY,
                        (float)bounds.OuterWidth,
                        (float)bounds.OuterHeight);
                });
            }
            compo.AddButton("", () => OnSectionHeaderClicked(kind), headerClick, EnumButtonStyle.None);

            string title = Lang.Get(section.TitleLang);
            double iconSlot = CollapseIconSize + Gap;
            ElementBounds titleBounds = ElementBounds
                .Fixed(RowPadding + iconSlot, y + 4, listWidth - RowPadding * 2 - iconSlot * 2, 18)
                .WithParent(contentBounds);
            compo.AddDynamicText(title, sectionFont, titleBounds);

            int collapseTex = collapsed ? sectionCollapsedTex : sectionExpandedTex;
            if (collapseTex > 0)
            {
                ElementBounds iconBounds = ElementBounds
                    .Fixed(
                        RowPadding + 4,
                        y + (SectionHeaderHeight - CollapseIconSize) / 2,
                        CollapseIconSize,
                        CollapseIconSize)
                    .WithParent(contentBounds);
                int texId = collapseTex;
                compo.AddCustomRender(iconBounds, (_, bounds) =>
                {
                    capi.Render.Render2DTexturePremultipliedAlpha(
                        texId,
                        (float)bounds.renderX,
                        (float)bounds.renderY,
                        (float)bounds.OuterWidth,
                        (float)bounds.OuterHeight);
                });
            }
            else
            {
                ElementBounds chevronBounds = ElementBounds
                    .Fixed(RowPadding + 2, y + 4, CollapseIconSize + 4, 18)
                    .WithParent(contentBounds);
                compo.AddDynamicText(collapsed ? "▶" : "▼", rowFont, chevronBounds);
            }

            y += SectionHeaderHeight;
            if (collapsed)
            {
                continue;
            }

            foreach (SkillDef def in section.Skills)
            {
                string skillId = def.Id;
                composedSkillIds.Add(skillId);
                double rowY = y;

                ElementBounds clickBounds = ElementBounds
                    .Fixed(0, rowY, listWidth, RowHeight - 2).WithParent(contentBounds);
                ElementBounds iconBounds = ElementBounds
                    .Fixed(RowPadding, rowY + (RowHeight - IconSize) / 2, IconSize, IconSize)
                    .WithParent(contentBounds);
                ElementBounds nameBounds = ElementBounds
                    .Fixed(textStartX, rowY + 3, nameWidth, 20).WithParent(contentBounds);
                ElementBounds levelBounds = ElementBounds
                    .Fixed(levelX, rowY + 3, LevelColumnWidth, 20).WithParent(contentBounds);
                ElementBounds barBounds = ElementBounds
                    .Fixed(barX, rowY + 5, barWidth, BarHeight).WithParent(contentBounds);
                ElementBounds descHoverBounds = ElementBounds
                    .Fixed(RowPadding, rowY, levelX - RowPadding, RowHeight - 2)
                    .WithParent(contentBounds);

                string barKey = "prosequor-skillbar-" + skillId;
                string levelKey = "prosequor-skilllevel-" + skillId;

                LoadedTexture? icon = EnsureIconTexture(def, IconSize);
                hoverRows.Add(new SkillListHoverRow(def, descHoverBounds));
                barHoverRows.Add(new SkillBarHoverRow(skillId, barBounds));

                compo
                    .AddButton("", () => OnSkillClicked(skillId), clickBounds, EnumButtonStyle.None)
                    .AddDynamicText(SkillRegistry.DisplayName(def), rowFont, nameBounds)
                    .AddDynamicText(
                        progress?.GetSkillLevel(skillId).ToString() ?? "0",
                        levelFont,
                        levelBounds,
                        levelKey)
                    .AddStatbar(barBounds, SkillBarColor(kind), barKey);

                if (icon != null && icon.TextureId > 0)
                {
                    LoadedTexture tex = icon;
                    compo.AddCustomRender(iconBounds, (_, bounds) =>
                    {
                        capi.Render.Render2DTexturePremultipliedAlpha(
                            tex.TextureId,
                            (float)bounds.renderX,
                            (float)bounds.renderY,
                            (float)bounds.OuterWidth,
                            (float)bounds.OuterHeight);
                    });
                }

                SetBar(compo.GetStatbar(barKey), progress, skillId);
                y += RowHeight;
            }
        }

        compo.EndClip();

        ElementBounds tipHost = ElementBounds.Fixed(0, listTop, listWidth, listHeight);
        GuiElementSkillListTooltip listTip = new(capi, tipHost, EnsureIconTexture);
        listTip.SetClipBounds(clipBounds);
        listTip.SetProgress(progress);
        listTip.SetRows(hoverRows);
        compo.AddSkillListTooltip(listTip, ListTooltipKey);

        GuiElementSkillBarTooltip barTip = new(capi, tipHost, id => FormatSkillBarHover(progress, id));
        barTip.SetClipBounds(clipBounds);
        barTip.SetRows(barHoverRows);
        compo.AddSkillBarTooltip(barTip, ListBarTooltipKey);

        if (scrolls)
        {
            ElementBounds scrollBounds =
                ElementBounds.Fixed(width - ScrollbarWidth, listTop, ScrollbarWidth, listHeight);
            compo.AddVerticalScrollbar(OnScroll, scrollBounds, ScrollbarKey);
            GuiElementScrollbar? sb = compo.GetScrollbar(ScrollbarKey);
            sb?.SetHeights((float)listHeight, (float)contentHeight);
            sb?.SetScrollbarPosition((int)scrollY);
        }
    }

    void ComposeDetail(GuiComposer compo, IPlayerProgress? progress, ISkillRegistry? registry, string skillId)
    {
        double width = tabWidth;

        if (registry == null || !registry.TryGet(skillId, out SkillDef def))
        {
            selectedSkillId = null;
            ComposeList(compo, progress, registry);
            return;
        }

        CairoFont detailFont = CairoFont.WhiteDetailText();

        int skillLevel = progress?.GetSkillLevel(skillId) ?? 0;
        int points = DetailPointsValue(def, progress);
        detailSignature = DetailSignature(skillId, progress);

        // Single header row: level on the left, Back centered, points on the right.
        double headerY = TopMargin - 4;
        double sideWidth = Math.Max(60, (width - BackButtonWidth) / 2 - Gap);
        compo.AddSmallButton(
            Lang.Get("prosequor:skilltree-back"),
            OnBackClicked,
            ElementBounds.Fixed((width - BackButtonWidth) / 2, headerY, BackButtonWidth, 22));
        compo.AddDynamicText(
            Lang.Get("prosequor:skilltree-level", skillLevel),
            detailFont,
            ElementBounds.Fixed(0, headerY + 3, sideWidth, 18),
            DetailLevelKey);
        compo.AddDynamicText(
            Lang.Get("prosequor:skilltree-points", points),
            detailFont.Clone().WithOrientation(EnumTextOrientation.Right),
            ElementBounds.Fixed(width - sideWidth, headerY + 3, sideWidth, 18),
            DetailPointsKey);

        double treeTop = TreeAreaTop;
        double treeHeight = TreeAreaHeight;
        ElementBounds treeArea = ElementBounds.Fixed(0, treeTop, width, treeHeight);

        if (def.Tree == null || def.Tree.Nodes.Count == 0)
        {
            compo.AddDynamicText(
                Lang.Get("prosequor:skilltree-no-tree"),
                detailFont.Clone().WithOrientation(EnumTextOrientation.Center),
                ElementBounds.Fixed(10, treeTop + treeHeight / 2 - 10, width - 20, 24));
            return;
        }

        compo.AddInset(treeArea, 2, 0.85f);

        ElementBounds viewportBounds =
            ElementBounds.Fixed(4, treeTop + 4, TreeViewportWidth, TreeViewportHeight);

        GuiElementSkillTreeViewport viewport = new(capi, viewportBounds);
        viewport.SetWatermark(SkillRegistry.IconLocation(def));
        viewport.SetTree(def, progress);
        viewport.OnNodeActivated = nodeId => network.RequestUnlock(skillId, nodeId);
        viewport.OnLockedNodeClicked = () =>
            capi.Gui.PlaySound(SkillLockedSound, randomizePitch: false, UnlockSoundVolume);
        viewport.OnHoverChanged = (nodeId, tip) =>
        {
            GuiElementHoverText? hover = activeComposer?.GetHoverText(TreeHoverKey);
            if (hover == null)
            {
                return;
            }

            if (string.IsNullOrEmpty(nodeId))
            {
                hover.SetVisible(false);
                return;
            }

            hover.SetNewText(tip);
            hover.SetVisible(true);
        };

        compo.AddSkillTreeViewport(viewport, TreeViewportKey);
        activeViewport = viewport;

        ElementBounds tipBounds = ElementBounds.Fixed(0, treeTop, 1, 1);
        compo.AddHoverText(
            "",
            CairoFont.WhiteDetailText(),
            TreeTooltipWidth,
            tipBounds,
            TreeHoverKey);
        GuiElementHoverText? tip = compo.GetHoverText(TreeHoverKey);
        tip?.SetAutoDisplay(false);
        tip?.SetFollowMouse(true);
        tip?.SetVisible(false);
    }

    string DetailSignature(string skillId, IPlayerProgress? progress)
    {
        if (progress == null)
        {
            return "";
        }

        int tiers = 0;
        int detailPoints = progress.UnlockPoints;
        ISkillRegistry? registry = ProsequorModSystem.For(capi)?.Registry;
        if (registry != null && registry.TryGet(skillId, out SkillDef def) && def.Tree != null)
        {
            foreach (SkillTreeNodeDef node in def.Tree.Nodes)
            {
                tiers += progress.GetUnlockTier(skillId, node.Id);
            }

            detailPoints = DetailPointsValue(def, progress);
        }

        return detailPoints + "|" + progress.GetSkillLevel(skillId) + "|" + tiers;
    }

    static int DetailPointsValue(SkillDef def, IPlayerProgress? progress)
    {
        if (progress == null)
        {
            return 0;
        }

        return def.IsHobby
            ? HobbyPointPolicy.Spendable(def, progress)
            : progress.UnlockPoints;
    }

    bool OnSkillClicked(string skillId)
    {
        ISkillRegistry? registry = ProsequorModSystem.For(capi)?.Registry;
        if (registry != null
            && registry.TryGet(skillId, out SkillDef def)
            && SkillMenuIndex.IsPassive(def))
        {
            return true;
        }

        capi.Gui.PlaySound(SubSelectSound, randomizePitch: false, UnlockSoundVolume);
        selectedSkillId = skillId;
        scrollY = 0f;
        RequestRecompose();
        return true;
    }

    bool OnSectionHeaderClicked(SkillMenuSectionKind kind)
    {
        sectionCollapsed[kind] = !IsCollapsed(kind);
        scrollY = 0f;
        RequestRecompose();
        return true;
    }

    bool IsCollapsed(SkillMenuSectionKind kind) =>
        sectionCollapsed.TryGetValue(kind, out bool collapsed) && collapsed;

    void EnsureSectionTextures()
    {
        if (sectionTexturesResolved)
        {
            return;
        }

        sectionTexturesResolved = true;
        sectionExpandedTex = TryLoadGuiTexture(SectionExpandedLoc);
        sectionCollapsedTex = TryLoadGuiTexture(SectionCollapsedLoc);
    }

    int TryLoadGuiTexture(AssetLocation loc)
    {
        if (capi.Assets.TryGet(loc) == null)
        {
            return 0;
        }

        try
        {
            return capi.Render.GetOrLoadTexture(loc);
        }
        catch
        {
            return 0;
        }
    }

    bool OnBackClicked()
    {
        capi.Gui.PlaySound(SubRetreatSound, randomizePitch: false, UnlockSoundVolume);
        selectedSkillId = null;
        scrollY = 0f;
        RequestRecompose();
        return true;
    }

    /// <summary>
    /// The dialog owns the composer (title bar, tabs, background), so switching views means asking it
    /// to recompose. Deferred by a frame because click handlers run while the composer iterates its
    /// elements, and recomposing disposes them.
    /// </summary>
    void RequestRecompose()
    {
        if (recomposePending)
        {
            return;
        }

        recomposePending = true;
        capi.Event.EnqueueMainThreadTask(Recompose, "prosequor-skills-recompose");
    }

    void Recompose()
    {
        recomposePending = false;
        if (dlg == null || !dlg.IsOpened() || activeComposer == null)
        {
            return;
        }

        if (!recomposeMethodResolved)
        {
            recomposeMethod = FindRecomposeMethod(dlg.GetType());
            recomposeMethodResolved = true;
        }

        if (recomposeMethod == null)
        {
            if (!recomposeWarned)
            {
                recomposeWarned = true;
                capi.Logger.Warning(
                    "[prosequor] {0} has no ComposeGuis(); skill tree navigation cannot refresh the dialog.",
                    dlg.GetType().Name);
            }

            return;
        }

        recomposeMethod.Invoke(dlg, null);
    }

    static MethodInfo? FindRecomposeMethod(Type type)
    {
        for (Type? t = type; t != null; t = t.BaseType)
        {
            MethodInfo? found = t.GetMethod(
                "ComposeGuis",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
                null,
                Type.EmptyTypes,
                null);
            if (found != null)
            {
                return found;
            }
        }

        return null;
    }

    void OnUnlockResult(UnlockNodeResultPacket packet)
    {
        if (packet.Status == UnlockPurchaseStatus.Ok)
        {
            // WatchedAttributes mirror will raise Changed and refresh the open detail view.
            PlayUnlockSound(packet.SkillId, packet.NodeId);
            return;
        }

        string key = packet.Status switch
        {
            UnlockPurchaseStatus.AlreadyUnlocked => "prosequor:unlock-failed-already",
            UnlockPurchaseStatus.MissingPrerequisite => "prosequor:unlock-failed-prereq",
            UnlockPurchaseStatus.BlockedByExclusive => "prosequor:unlock-failed-exclusive",
            UnlockPurchaseStatus.SkillLevelTooLow => "prosequor:unlock-failed-level",
            UnlockPurchaseStatus.NotEnoughPoints => "prosequor:unlock-failed-points",
            UnlockPurchaseStatus.SpecializationLimitReached => "prosequor:unlock-failed-specialization",
            UnlockPurchaseStatus.UnknownNode or UnlockPurchaseStatus.NoTree or UnlockPurchaseStatus.UnknownSkill
                => "prosequor:unlock-failed-unknown",
            _ => "prosequor:unlock-failed-generic"
        };
        capi.TriggerIngameError(this, "prosequor-unlock", Lang.Get(key));
    }

    void PlayUnlockSound(string skillId, string nodeId)
    {
        AssetLocation sound = SkillUnlockSound;
        ISkillRegistry? registry = ProsequorModSystem.For(capi)?.Registry;
        if (registry != null
            && registry.TryGet(skillId, out SkillDef skill)
            && skill.Tree != null
            && skill.Tree.TryGet(nodeId, out SkillTreeNodeDef node)
            && node.IsSpecialization)
        {
            sound = SpecializationUnlockSound;
        }

        capi.Gui.PlaySound(sound, randomizePitch: false, UnlockSoundVolume);
    }

    LoadedTexture? EnsureIconTexture(SkillDef def, double size)
    {
        string cacheKey = def.Id + "@" + (int)size;
        if (iconTextures.TryGetValue(cacheKey, out LoadedTexture? existing))
        {
            return existing.TextureId > 0 ? existing : null;
        }

        AssetLocation? loc = SkillRegistry.IconLocation(def);
        if (loc == null)
        {
            return null;
        }

        IAsset? asset = capi.Assets.TryGet(loc);
        if (asset == null)
        {
            capi.Logger.Warning("[prosequor] Missing skill icon {0} for '{1}'.", loc, def.Id);
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

    LoadedTexture? EnsureSectionHeaderTexture(int width, int height)
    {
        width = Math.Max(8, width);
        height = Math.Max(8, height);
        if (sectionHeaderTexture != null
            && sectionHeaderTexW == width
            && sectionHeaderTexH == height
            && sectionHeaderTexture.TextureId > 0)
        {
            return sectionHeaderTexture;
        }

        sectionHeaderTexture?.Dispose();
        sectionHeaderTexture = null;
        sectionHeaderTexW = width;
        sectionHeaderTexH = height;

        ImageSurface surface = new(Format.Argb32, width, height);
        Context ctx = new(surface);
        try
        {
            ctx.SetSourceRGBA(0.16, 0.13, 0.10, 0.85);
            ctx.Rectangle(0, 0, width, height);
            ctx.Fill();
            ctx.SetSourceRGBA(0.65, 0.55, 0.38, 0.9);
            ctx.LineWidth = 1.5;
            ctx.Rectangle(0.75, 0.75, width - 1.5, height - 1.5);
            ctx.Stroke();

            LoadedTexture texture = new(capi);
            capi.Gui.LoadOrUpdateCairoTexture(surface, linearMag: false, ref texture);
            sectionHeaderTexture = texture;
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
        sectionHeaderTexture?.Dispose();
        sectionHeaderTexture = null;
    }

    void ObserveProgress(EntityBehaviorProgress? progress)
    {
        if (ReferenceEquals(observedProgress, progress))
        {
            return;
        }

        if (observedProgress != null)
        {
            observedProgress.Changed -= OnProgressChanged;
        }

        observedProgress = progress;
        if (observedProgress != null)
        {
            observedProgress.Changed += OnProgressChanged;
        }
    }

    void OnProgressChanged()
    {
        GuiComposer? compo = activeComposer;
        EntityBehaviorProgress? progress = observedProgress;
        if (compo == null || progress == null)
        {
            return;
        }

        if (selectedSkillId != null)
        {
            string signature = DetailSignature(selectedSkillId, progress);
            if (signature != detailSignature)
            {
                detailSignature = signature;
                activeViewport?.RefreshProgress(progress);
                compo.GetDynamicText(DetailLevelKey)?.SetNewText(
                    Lang.Get("prosequor:skilltree-level", progress.GetSkillLevel(selectedSkillId)));
                int detailPoints = progress.UnlockPoints;
                ISkillRegistry? registry = ProsequorModSystem.For(capi)?.Registry;
                if (registry != null && registry.TryGet(selectedSkillId, out SkillDef selectedDef))
                {
                    detailPoints = DetailPointsValue(selectedDef, progress);
                }

                compo.GetDynamicText(DetailPointsKey)?.SetNewText(
                    Lang.Get("prosequor:skilltree-points", detailPoints));
            }

            return;
        }

        compo.GetDynamicText(HeaderLevelKey)?.SetNewText(
            Lang.Get("prosequor:prosequor-header-level", progress.PlayerLevel));
        compo.GetDynamicText(PointsKey)?.SetNewText(
            Lang.Get("prosequor:prosequor-header-points", progress.UnlockPoints));
        SetBar(compo.GetStatbar(PlayerBarKey), progress, null);

        foreach (string skillId in composedSkillIds)
        {
            compo.GetDynamicText("prosequor-skilllevel-" + skillId)
                ?.SetNewText(progress.GetSkillLevel(skillId).ToString());
            SetBar(compo.GetStatbar("prosequor-skillbar-" + skillId), progress, skillId);
        }
    }

    static double[] SkillBarColor(SkillMenuSectionKind kind) => kind switch
    {
        SkillMenuSectionKind.Specializations => SpecializationBarColor,
        SkillMenuSectionKind.HobbySkills => HobbyBarColor,
        SkillMenuSectionKind.Passives => PassiveBarColor,
        _ => MinorBarColor
    };

    static void SetBar(GuiElementStatbar? bar, IPlayerProgress? progress, string? skillId)
    {
        if (bar == null)
        {
            return;
        }

        float into = 1f;
        int need = 1;
        if (progress != null)
        {
            if (skillId == null)
            {
                progress.GetPlayerBar(out into, out need, out _);
            }
            else
            {
                progress.GetSkillBar(skillId, out into, out need, out _);
            }
        }

        if (need <= 0)
        {
            need = 1;
            into = 1f;
        }

        bar.SetValues(into, 0, need);
        bar.SetLineInterval(Math.Max(1f, need / 5f));
        bar.ShowValueOnHover = skillId == null;
    }

    static string? FormatSkillBarHover(IPlayerProgress? progress, string skillId)
    {
        if (progress == null)
        {
            return null;
        }

        progress.GetSkillBar(skillId, out float into, out int need, out _);
        if (need <= 0)
        {
            need = 1;
            into = 1f;
        }

        return $"{Math.Round(into, 1)} / {need}";
    }

    void OnScroll(float value)
    {
        scrollY = value;
        if (contentBounds == null)
        {
            return;
        }

        contentBounds.fixedY = -value;
        contentBounds.MarkDirtyRecursive();
        contentBounds.CalcWorldBounds();
    }
}
