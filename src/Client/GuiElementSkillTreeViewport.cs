using System.Collections.Generic;
using System.Text;
using Cairo;
using Prosequor.Data;
using Prosequor.Player;
using Prosequor.Progress;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
namespace Prosequor.Client;
/// <summary>
/// Clipped, custom-drawn skill-tree canvas: diamond nodes, textured links, drag-pan, wheel scroll.
/// </summary>
public class GuiElementSkillTreeViewport : GuiElement
{
    public const double NodeDisplaySize = 72;
    public const double IconInset = 14;
    public const double GridStep = 80;
    /// <summary>How far the corner tier badge may stick past the diamond edge.</summary>
    public const double TierLabelHeight = 16;
    /// <summary>Pull tier badge toward the diamond corner (fraction of node size).</summary>
    public const double TierLabelCornerInsetFraction = 0.10;
    public const double WatermarkFill = 0.8;
    public const double WatermarkAlpha = 0.12;
    public const double Overscroll = 24;
    public const float PanLerp = 14f;
    public const double DragThreshold = 4;
    public const float WheelScroll = 48f;
    public const double IconShadowAlpha = StyledSvgIconCache.ShadowAlpha;
    public const double IconShadowOffsetFraction = StyledSvgIconCache.ShadowOffsetFraction;
    public const int FitScaleDenominator = 240;
    public const int FitScaleNumeratorStep = 6;
    public const double FitHorizontalPadding = 16;
    public const double FitVerticalPadding = 16;
    static readonly AssetLocation DiamondOnLoc = new("prosequor", "textures/gui/diamond-on.png");
    static readonly AssetLocation DiamondOffLoc = new("prosequor", "textures/gui/diamond-off.png");
    static readonly AssetLocation DiamondSpecialOnLoc =
        new("prosequor", "textures/gui/diamond-special-on.png");
    static readonly AssetLocation DiamondSpecialOffLoc =
        new("prosequor", "textures/gui/diamond-special-off.png");
    static readonly AssetLocation VerticalOnLoc = new("prosequor", "textures/gui/vertical-on.png");
    static readonly AssetLocation VerticalOffLoc = new("prosequor", "textures/gui/vertical-off.png");
    static readonly AssetLocation HorizontalOnLoc = new("prosequor", "textures/gui/horizontal-on.png");
    static readonly AssetLocation HorizontalOffLoc = new("prosequor", "textures/gui/horizontal-off.png");
    static readonly AssetLocation DiagonalLeftOnLoc = new("prosequor", "textures/gui/diagonal-left-on.png");
    static readonly AssetLocation DiagonalLeftOffLoc = new("prosequor", "textures/gui/diagonal-left-off.png");
    static readonly AssetLocation DiagonalRightOnLoc = new("prosequor", "textures/gui/diagonal-right-on.png");
    static readonly AssetLocation DiagonalRightOffLoc = new("prosequor", "textures/gui/diagonal-right-off.png");
    static readonly AssetLocation DiamondHighlightLoc = new("prosequor", "textures/gui/diamond-highlight.png");
    static readonly AssetLocation FallbackIconLoc = new("prosequor", "textures/icons/generic.svg");
    readonly ICoreClientAPI capi;
    readonly StyledSvgIconCache nodeIconCache;
    readonly Dictionary<string, LoadedTexture> tierLabels = new(StringComparer.Ordinal);
    readonly List<NodeVisual> nodes = new();
    readonly List<LinkVisual> links = new();
    readonly Dictionary<string, NodeVisual> nodesById = new(StringComparer.OrdinalIgnoreCase);
    // Branch art comes from the engine texture cache, so these ids are shared and must never be disposed here.
    int diamondOn;
    int diamondOff;
    int diamondSpecialOn;
    int diamondSpecialOff;
    int verticalOn;
    int verticalOff;
    int horizontalOn;
    int horizontalOff;
    int diagonalLeftOn;
    int diagonalLeftOff;
    int diagonalRightOn;
    int diagonalRightOff;
    int diamondHighlight;
    SkillDef? skill;
    AssetLocation? watermarkIcon;
    LoadedTexture? watermark;
    int watermarkSize;
    double graphMinX;
    double graphMinY;
    double graphMaxX;
    double graphMaxY;
    double treeFitScale = 1;
    double panX;
    double panY;
    double targetPanX;
    double targetPanY;
    bool centered;
    bool mouseDown;
    bool dragging;
    double dragStartX;
    double dragStartY;
    double panAtDragStartX;
    double panAtDragStartY;
    string? pressedNodeId;
    string? hoveredNodeId;
    string hoverTooltip = "";
    public Action<string>? OnNodeActivated;
    public Action? OnLockedNodeClicked;
    public Action<string?, string>? OnHoverChanged;
    public override bool Focusable => true;
    public GuiElementSkillTreeViewport(ICoreClientAPI capi, ElementBounds bounds) : base(capi, bounds)
    {
        this.capi = capi;
        nodeIconCache = new StyledSvgIconCache(capi);
    }
    /// <summary>Skill icon drawn faintly behind the graph. Null clears it.</summary>
    public void SetWatermark(AssetLocation? icon)
    {
        watermarkIcon = icon;
        watermark?.Dispose();
        watermark = null;
        watermarkSize = 0;
    }
    public void SetTree(SkillDef skillDef, IPlayerProgress? progress)
    {
        skill = skillDef;
        RebuildLayout(progress);
        centered = false;
        hoveredNodeId = null;
        hoverTooltip = "";
        OnHoverChanged?.Invoke(null, "");
    }
    public void RefreshProgress(IPlayerProgress? progress)
    {
        if (skill == null)
        {
            return;
        }
        ISkillRegistry? registry = ProsequorModSystem.For(capi)?.Registry;
        IReadOnlyList<LevelUpRuleDef>? levelUpRules = ProsequorModSystem.For(capi)?.LevelUps.Rules;
        foreach (NodeVisual node in nodes)
        {
            node.State = ResolveState(skill, progress, registry, levelUpRules, node.Def, out int tier);
            node.Tier = tier;
            node.Label = EnsureTierLabel(tier, node.Def.MaxTier);
            node.Tooltip = BuildTooltip(skill, node.Def, tier, node.State, progress, registry, levelUpRules);
        }
        foreach (LinkVisual link in links)
        {
            link.Lit = progress?.HasUnlock(skill.Id, link.ParentId) == true;
        }
        if (hoveredNodeId != null && nodesById.TryGetValue(hoveredNodeId, out NodeVisual? hovered))
        {
            hoverTooltip = hovered.Tooltip;
            OnHoverChanged?.Invoke(hoveredNodeId, hoverTooltip);
        }
    }
    public override void ComposeElements(Context ctx, ImageSurface surface)
    {
        Bounds.CalcWorldBounds();
        LoadBranchTextures();
        EnsureWatermark();
    }
    public override void RenderInteractiveElements(float deltaTime)
    {
        if (skill?.Tree == null || nodes.Count == 0)
        {
            return;
        }
        EnsureCentered();
        AnimatePan(deltaTime);
        UpdateHover();
        api.Render.PushScissor(Bounds);
        if (watermark != null && watermark.TextureId > 0)
        {
            api.Render.Render2DTexturePremultipliedAlpha(
                watermark.TextureId,
                Bounds.renderX + (Bounds.InnerWidth - watermark.Width) / 2,
                Bounds.renderY + (Bounds.InnerHeight - watermark.Height) / 2,
                watermark.Width,
                watermark.Height,
                50);
        }
        foreach (LinkVisual link in links)
        {
            DrawLink(link);
        }
        foreach (NodeVisual node in nodes)
        {
            DrawNode(node);
        }
        api.Render.PopScissor();
    }
    public override void OnMouseDownOnElement(ICoreClientAPI api, MouseEvent args)
    {
        if (!IsPositionInside(api.Input.MouseX, api.Input.MouseY) || args.Button != EnumMouseButton.Left)
        {
            return;
        }
        mouseDown = true;
        dragging = false;
        dragStartX = api.Input.MouseX;
        dragStartY = api.Input.MouseY;
        panAtDragStartX = targetPanX;
        panAtDragStartY = targetPanY;
        pressedNodeId = HitTestNode(api.Input.MouseX, api.Input.MouseY);
        args.Handled = true;
    }
    public override void OnMouseMove(ICoreClientAPI api, MouseEvent args)
    {
        if (!mouseDown)
        {
            return;
        }
        double dx = api.Input.MouseX - dragStartX;
        double dy = api.Input.MouseY - dragStartY;
        if (!dragging && (Math.Abs(dx) >= DragThreshold || Math.Abs(dy) >= DragThreshold))
        {
            dragging = true;
            pressedNodeId = null;
        }
        if (dragging)
        {
            targetPanX = panAtDragStartX + dx;
            targetPanY = panAtDragStartY + dy;
            ClampPan();
            args.Handled = true;
        }
    }
    public override void OnMouseUp(ICoreClientAPI api, MouseEvent args)
    {
        if (!mouseDown || args.Button != EnumMouseButton.Left)
        {
            return;
        }
        mouseDown = false;
        if (!dragging && pressedNodeId != null)
        {
            string nodeId = pressedNodeId;
            if (nodesById.TryGetValue(nodeId, out NodeVisual? node))
            {
                if (node.State == NodeVisualState.Eligible)
                {
                    OnNodeActivated?.Invoke(nodeId);
                }
                else if (node.State == NodeVisualState.Locked)
                {
                    OnLockedNodeClicked?.Invoke();
                }
            }
        }
        dragging = false;
        pressedNodeId = null;
        args.Handled = true;
    }
    public override void OnMouseWheel(ICoreClientAPI api, MouseWheelEventArgs args)
    {
        if (!IsPositionInside(api.Input.MouseX, api.Input.MouseY))
        {
            return;
        }
        targetPanY += args.deltaPrecise * scaled(WheelScroll);
        ClampPan();
        args.SetHandled(true);
    }
    public override void Dispose()
    {
        base.Dispose();
        // Only textures this element created are disposed; branch art belongs to the engine cache.
        nodeIconCache.Dispose();
        foreach (LoadedTexture texture in tierLabels.Values)
        {
            texture.Dispose();
        }
        watermark?.Dispose();
        watermark = null;
        tierLabels.Clear();
    }
    void RebuildLayout(IPlayerProgress? progress)
    {
        nodes.Clear();
        links.Clear();
        nodesById.Clear();
        if (skill?.Tree == null)
        {
            return;
        }
        SkillTreeDef tree = skill.Tree;
        graphMinX = double.MaxValue;
        graphMinY = double.MaxValue;
        graphMaxX = double.MinValue;
        graphMaxY = double.MinValue;
        foreach (SkillTreeNodeDef node in tree.Nodes)
        {
            double x = node.GridColumn * GridStep;
            double y = node.Layer * GridStep;
            graphMinX = Math.Min(graphMinX, x - NodeDisplaySize / 2);
            graphMinY = Math.Min(graphMinY, y - NodeDisplaySize / 2);
            // Tier badge sits on the lower-right corner and can overhang past the diamond.
            graphMaxX = Math.Max(graphMaxX, x + NodeDisplaySize / 2 + TierLabelHeight);
            graphMaxY = Math.Max(graphMaxY, y + NodeDisplaySize / 2 + TierLabelHeight / 2);
        }

        UpdateTreeFitScale();

        ISkillRegistry? registry = ProsequorModSystem.For(capi)?.Registry;
        IReadOnlyList<LevelUpRuleDef>? levelUpRules = ProsequorModSystem.For(capi)?.LevelUps.Rules;
        foreach (SkillTreeNodeDef node in tree.Nodes)
        {
            double x = node.GridColumn * GridStep;
            double y = node.Layer * GridStep;
            NodeVisual visual = new()
            {
                Def = node,
                WorldX = x,
                WorldY = y,
                State = ResolveState(skill, progress, registry, levelUpRules, node, out int tier),
                Tier = tier,
                Icon = EnsureNodeIcon(node),
                Label = EnsureTierLabel(tier, node.MaxTier)
            };
            visual.Tooltip = BuildTooltip(skill, node, tier, visual.State, progress, registry, levelUpRules);
            nodes.Add(visual);
            nodesById[node.Id] = visual;
        }
        foreach (SkillTreeNodeDef node in tree.Nodes)
        {
            if (!nodesById.TryGetValue(node.Id, out NodeVisual? child))
            {
                continue;
            }
            foreach (SkillTreeNodeDef parent in node.Parents)
            {
                if (!nodesById.TryGetValue(parent.Id, out NodeVisual? parentVisual))
                {
                    continue;
                }
                links.Add(new LinkVisual
                {
                    ParentId = parent.Id,
                    ChildId = node.Id,
                    X0 = parentVisual.WorldX,
                    Y0 = parentVisual.WorldY,
                    X1 = child.WorldX,
                    Y1 = child.WorldY,
                    Lit = progress?.HasUnlock(skill.Id, parent.Id) == true
                });
            }
        }
    }

    void UpdateTreeFitScale()
    {
        double graphWidth = graphMaxX - graphMinX;
        double graphHeight = graphMaxY - graphMinY;
        // Prefer Outer* — same space ClampPan / EnsureCentered use after CalcWorldBounds.
        double viewW = Bounds.OuterWidth;
        double viewH = Bounds.OuterHeight;
        if (graphWidth <= 0 || graphHeight <= 0 || viewW <= 1 || viewH <= 1)
        {
            treeFitScale = 1;
            return;
        }

        double availableW = viewW - scaled(FitHorizontalPadding);
        double availableH = viewH - scaled(FitVerticalPadding);
        if (availableW <= 0 || availableH <= 0)
        {
            treeFitScale = 1;
            return;
        }

        // Graph AABB is in unscaled world units; viewport is already GUI-scaled pixels.
        // treeScale(w) = scaled(w * fit), so fit <= available / scaled(graph).
        double scaledGraphW = scaled(graphWidth);
        double scaledGraphH = scaled(graphHeight);
        if (scaledGraphW <= 0 || scaledGraphH <= 0)
        {
            treeFitScale = 1;
            return;
        }

        // Uniform scale: take the tighter axis so both dimensions prefer to fit.
        // Snap down on the discrete k/240 grid (GUI 1 / 1.5 / 2 friendly); if still too
        // large at the floor step, pan/scroll remains available on that axis.
        double rawFit = Math.Min(1, Math.Min(availableW / scaledGraphW, availableH / scaledGraphH));
        treeFitScale = SnapFitScaleDown(rawFit);
    }

    static double SnapFitScaleDown(double rawFit)
    {
        if (rawFit >= 1)
        {
            return 1;
        }

        int maxK = (int)Math.Floor(rawFit * FitScaleDenominator);
        int k = maxK - (maxK % FitScaleNumeratorStep);
        if (k < FitScaleNumeratorStep)
        {
            k = FitScaleNumeratorStep;
        }

        return k / (double)FitScaleDenominator;
    }

    double treeScale(double world) => scaled(world * treeFitScale);

    void RefreshNodeIconsIfNeeded(double previousFit)
    {
        if (Math.Abs(previousFit - treeFitScale) < 1e-6)
        {
            return;
        }

        foreach (NodeVisual node in nodes)
        {
            node.Icon = EnsureNodeIcon(node.Def);
        }
    }
    void EnsureCentered()
    {
        if (Bounds.OuterWidth <= 1 || Bounds.OuterHeight <= 1)
        {
            return;
        }

        // SetTree often runs during compose before bounds are real (scale stuck at 1).
        // Recompute once bounds exist; safe to call every frame — no-op when unchanged.
        double previousFit = treeFitScale;
        UpdateTreeFitScale();
        RefreshNodeIconsIfNeeded(previousFit);

        if (centered)
        {
            return;
        }

        double graphCx = treeScale((graphMinX + graphMaxX) / 2);
        double graphCy = treeScale((graphMinY + graphMaxY) / 2);
        targetPanX = Bounds.OuterWidth / 2 - graphCx;
        targetPanY = Bounds.OuterHeight / 2 - graphCy;
        ClampPan();
        panX = targetPanX;
        panY = targetPanY;
        centered = true;
    }
    void AnimatePan(float deltaTime)
    {
        float t = Math.Min(1f, deltaTime * PanLerp);
        panX += (targetPanX - panX) * t;
        panY += (targetPanY - panY) * t;
    }
    void ClampPan()
    {
        double viewW = Bounds.OuterWidth;
        double viewH = Bounds.OuterHeight;
        double minX = treeScale(graphMinX);
        double maxX = treeScale(graphMaxX);
        double minY = treeScale(graphMinY);
        double maxY = treeScale(graphMaxY);
        double contentW = Math.Max(viewW, maxX - minX);
        double contentH = Math.Max(viewH, maxY - minY);
        double overscroll = scaled(Overscroll);
        double minPanX = viewW - maxX - overscroll;
        double maxPanX = -minX + overscroll;
        double minPanY = viewH - maxY - overscroll;
        double maxPanY = -minY + overscroll;
        if (contentW <= viewW)
        {
            targetPanX = viewW / 2 - (minX + maxX) / 2;
        }
        else
        {
            targetPanX = GameMath.Clamp(targetPanX, minPanX, maxPanX);
        }
        if (contentH <= viewH)
        {
            targetPanY = viewH / 2 - (minY + maxY) / 2;
        }
        else
        {
            targetPanY = GameMath.Clamp(targetPanY, minPanY, maxPanY);
        }
    }
    void UpdateHover()
    {
        string? hit = IsPositionInside(api.Input.MouseX, api.Input.MouseY)
            ? HitTestNode(api.Input.MouseX, api.Input.MouseY)
            : null;
        if (hit == hoveredNodeId)
        {
            return;
        }
        hoveredNodeId = hit;
        hoverTooltip = "";
        if (hit != null && nodesById.TryGetValue(hit, out NodeVisual? node))
        {
            hoverTooltip = node.Tooltip;
        }
        OnHoverChanged?.Invoke(hoveredNodeId, hoverTooltip);
    }
    string? HitTestNode(int mouseX, int mouseY)
    {
        double localX = mouseX - Bounds.absX;
        double localY = mouseY - Bounds.absY;
        double half = treeScale(NodeDisplaySize) * 0.35;
        double diamondReach = treeScale(NodeDisplaySize) * 0.55;
        string? best = null;
        double bestDist = double.MaxValue;
        foreach (NodeVisual node in nodes)
        {
            double sx = treeScale(node.WorldX) + panX;
            double sy = treeScale(node.WorldY) + panY;
            double dx = localX - sx;
            double dy = localY - sy;
            // Diamond-ish hit: manhattan distance approximates the rotated square.
            double manhattan = Math.Abs(dx) + Math.Abs(dy);
            if (manhattan <= diamondReach || (Math.Abs(dx) <= half && Math.Abs(dy) <= half))
            {
                double dist = dx * dx + dy * dy;
                if (dist < bestDist)
                {
                    bestDist = dist;
                    best = node.Def.Id;
                }
            }
        }
        return best;
    }
    static readonly Vec4f LockedIconTint = new(0.4f, 0.4f, 0.4f, 1f);
    void DrawNode(NodeVisual node)
    {
        double size = treeScale(NodeDisplaySize);
        double x = Bounds.renderX + treeScale(node.WorldX) + panX - size / 2;
        double y = Bounds.renderY + treeScale(node.WorldY) + panY - size / 2;
        int diamond = node.Def.IsSpecialization
            ? node.Tier > 0 ? diamondSpecialOn : diamondSpecialOff
            : node.Tier > 0 ? diamondOn : diamondOff;
        if (diamond > 0)
        {
            api.Render.Render2DTexturePremultipliedAlpha(diamond, x, y, size, size, 52);
        }
        // Highlight sits above the diamond frame but under the skill icon.
        bool highlighted = string.Equals(hoveredNodeId, node.Def.Id, StringComparison.OrdinalIgnoreCase);
        if (highlighted && diamondHighlight > 0)
        {
            api.Render.Render2DTexturePremultipliedAlpha(
                diamondHighlight, x, y, size, size, 53);
        }
        if (node.Icon != null && node.Icon.TextureId > 0)
        {
            double iconSize = treeScale(NodeDisplaySize - IconInset);
            double ix = x + (size - iconSize) / 2;
            double iy = y + (size - iconSize) / 2;
            Vec4f? tint = node.State == NodeVisualState.Locked ? LockedIconTint : null;
            api.Render.Render2DTexturePremultipliedAlpha(
                node.Icon.TextureId, ix, iy, iconSize, iconSize, 54, tint);
        }
        if (node.Label != null && node.Label.TextureId > 0)
        {
            // Lower-right corner: clears short vertical links; diagonals are long enough to stay readable.
            double inset = treeScale(NodeDisplaySize * TierLabelCornerInsetFraction);
            api.Render.Render2DTexturePremultipliedAlpha(
                node.Label.TextureId,
                x + size - node.Label.Width / 2 - inset,
                y + size - node.Label.Height / 2 - inset,
                node.Label.Width,
                node.Label.Height,
                55);
        }
    }
    void DrawLink(LinkVisual link)
    {
        double x = link.X0;
        double y = link.Y0;
        double dx = link.X1 - x;
        double dy = link.Y1 - y;
        // Consume equal X/Y distance first so every sloped section is exactly 45 degrees.
        double diagonal = Math.Min(Math.Abs(dx), Math.Abs(dy));
        if (diagonal > 0.5)
        {
            double nextX = x + Math.Sign(dx) * diagonal;
            double nextY = y + Math.Sign(dy) * diagonal;
            DrawDiagonal(x, y, nextX, nextY, link.Lit);
            x = nextX;
            y = nextY;
        }
        if (Math.Abs(link.X1 - x) > 0.5)
        {
            DrawAxis(x, y, link.X1, y, vertical: false, lit: link.Lit);
            x = link.X1;
        }
        if (Math.Abs(link.Y1 - y) > 0.5)
        {
            DrawAxis(x, y, x, link.Y1, vertical: true, lit: link.Lit);
        }
    }
    void DrawDiagonal(double x0, double y0, double x1, double y1, bool lit)
    {
        double dx = x1 - x0;
        // left-down uses diagonal-left (top-right → bottom-left); right-down uses diagonal-right.
        int tex = dx < 0
            ? (lit ? diagonalLeftOn : diagonalLeftOff)
            : (lit ? diagonalRightOn : diagonalRightOff);
        if (tex <= 0)
        {
            return;
        }
        // The sloped stroke runs corner to corner, so stretching one copy over the whole run would
        // scale its width with the span and outweigh the straight sections. Repeat the sprite at the
        // same size those use instead, overlapping the copies so the tapered ends only surface at the
        // node centers, where the diamonds cover them.
        double span = Math.Abs(dx);
        double tile = Math.Min(NodeDisplaySize, span);
        int tiles = (int)Math.Ceiling(span / tile);
        double step = tiles > 1 ? (span - tile) / (tiles - 1) : 0;
        double left = Math.Min(x0, x1);
        double top = Math.Min(y0, y1);
        for (int i = 0; i < tiles; i++)
        {
            double offset = i * step;
            // Left-leaning runs descend to the left, so their copies walk the opposite way in x.
            double tileX = dx < 0 ? left + span - tile - offset : left + offset;
            DrawWorldTexture(tex, tileX, top + offset, tile, tile);
        }
    }
    void DrawAxis(double x0, double y0, double x1, double y1, bool vertical, bool lit)
    {
        int tex = vertical
            ? (lit ? verticalOn : verticalOff)
            : (lit ? horizontalOn : horizontalOff);
        if (tex <= 0)
        {
            return;
        }
        double length = Math.Abs(vertical ? y1 - y0 : x1 - x0);
        if (length < 0.5)
        {
            return;
        }
        double overlap = NodeDisplaySize;
        if (vertical)
        {
            DrawWorldTexture(
                tex,
                x0 - overlap / 2,
                Math.Min(y0, y1) - overlap / 2,
                overlap,
                length + overlap);
        }
        else
        {
            DrawWorldTexture(
                tex,
                Math.Min(x0, x1) - overlap / 2,
                y0 - overlap / 2,
                length + overlap,
                overlap);
        }
    }
    void DrawWorldTexture(int textureId, double worldX, double worldY, double worldWidth, double worldHeight)
    {
        double x = Bounds.renderX + treeScale(worldX) + panX;
        double y = Bounds.renderY + treeScale(worldY) + panY;
        api.Render.Render2DTexturePremultipliedAlpha(
            textureId,
            x,
            y,
            treeScale(worldWidth),
            treeScale(worldHeight),
            51);
    }
    void LoadBranchTextures()
    {
        diamondOn = LoadTex(DiamondOnLoc);
        diamondOff = LoadTex(DiamondOffLoc);
        diamondSpecialOn = LoadTex(DiamondSpecialOnLoc);
        diamondSpecialOff = LoadTex(DiamondSpecialOffLoc);
        verticalOn = LoadTex(VerticalOnLoc);
        verticalOff = LoadTex(VerticalOffLoc);
        horizontalOn = LoadTex(HorizontalOnLoc);
        horizontalOff = LoadTex(HorizontalOffLoc);
        diagonalLeftOn = LoadTex(DiagonalLeftOnLoc);
        diagonalLeftOff = LoadTex(DiagonalLeftOffLoc);
        diagonalRightOn = LoadTex(DiagonalRightOnLoc);
        diagonalRightOff = LoadTex(DiagonalRightOffLoc);
        diamondHighlight = LoadTex(DiamondHighlightLoc);
    }
    int LoadTex(AssetLocation loc)
    {
        try
        {
            return api.Render.GetOrLoadTexture(loc);
        }
        catch (Exception e)
        {
            capi.Logger.Warning("[prosequor] Failed to load skill-tree texture {0}: {1}", loc, e.Message);
            return 0;
        }
    }
    LoadedTexture? EnsureNodeIcon(SkillTreeNodeDef node)
    {
        AssetLocation loc = ResolveNodeIcon(node);
        int px = Math.Max(8, (int)treeScale(NodeDisplaySize - IconInset));
        return nodeIconCache.Get(loc, px, FallbackIconLoc);
    }
    void EnsureWatermark()
    {
        if (watermarkIcon == null)
        {
            return;
        }
        int px = Math.Max(32, (int)(Math.Min(Bounds.InnerWidth, Bounds.InnerHeight) * WatermarkFill));
        if (watermark != null && watermarkSize == px)
        {
            return;
        }
        IAsset? asset = capi.Assets.TryGet(watermarkIcon);
        if (asset == null)
        {
            capi.Logger.Warning("[prosequor] Missing skill icon {0}; no tree watermark.", watermarkIcon);
            watermarkIcon = null;
            return;
        }
        ImageSurface surface = new(Format.Argb32, px, px);
        Context ctx = new(surface);
        // The icon is drawn opaque, then composited at low alpha so it reads as a backdrop.
        ImageSurface iconSurface = new(Format.Argb32, px, px);
        try
        {
            capi.Gui.DrawSvg(asset, iconSurface, 0, 0, px, px, ColorUtil.WhiteArgb);
            ctx.SetSourceSurface(iconSurface, 0, 0);
            ctx.PaintWithAlpha(WatermarkAlpha);
            LoadedTexture texture = watermark ?? new LoadedTexture(capi);
            capi.Gui.LoadOrUpdateCairoTexture(surface, linearMag: true, ref texture);
            watermark = texture;
            watermarkSize = px;
        }
        finally
        {
            ctx.Dispose();
            surface.Dispose();
            iconSurface.Dispose();
        }
    }
    LoadedTexture? EnsureTierLabel(int tier, int maxTier)
    {
        string text = tier + "/" + maxTier;
        if (tierLabels.TryGetValue(text, out LoadedTexture? existing))
        {
            return existing.TextureId > 0 ? existing : null;
        }
        CairoFont font = CairoFont.WhiteDetailText().WithOrientation(EnumTextOrientation.Center);
        LoadedTexture texture = capi.Gui.TextTexture.GenTextTexture(text, font);
        tierLabels[text] = texture;
        return texture.TextureId > 0 ? texture : null;
    }
    static AssetLocation ResolveNodeIcon(SkillTreeNodeDef node)
    {
        if (string.IsNullOrWhiteSpace(node.Icon))
        {
            return FallbackIconLoc;
        }
        string path = node.Icon.Trim().Replace('\\', '/').TrimStart('/');
        return path.Contains(':') ? new AssetLocation(path) : new AssetLocation("prosequor", path);
    }
    static NodeVisualState ResolveState(
        SkillDef skill,
        IPlayerProgress? progress,
        ISkillRegistry? registry,
        IReadOnlyList<LevelUpRuleDef>? levelUpRules,
        SkillTreeNodeDef node,
        out int tier)
    {
        tier = progress?.GetUnlockTier(skill.Id, node.Id) ?? 0;
        if (tier >= node.MaxTier)
        {
            return NodeVisualState.Unlocked;
        }

        return SkillTreeEligibility.IsEligible(skill, progress, registry, levelUpRules, node.Id)
            ? NodeVisualState.Eligible
            : NodeVisualState.Locked;
    }

    static string BuildTooltip(
        SkillDef skill,
        SkillTreeNodeDef node,
        int tier,
        NodeVisualState state,
        IPlayerProgress? progress,
        ISkillRegistry? registry,
        IReadOnlyList<LevelUpRuleDef>? levelUpRules)
    {
        StringBuilder sb = new();
        sb.Append("<font family=\"")
            .Append(EscapeVtml(GuiStyle.DecorativeFontName))
            .Append("\" size=\"20\" weight=\"bold\" color=\"#E6C78A\">")
            .Append(EscapeVtml(LangKey(node.NameLang)))
            .Append("</font><br>");
        if (node.MaxTier > 1)
        {
            sb.Append("<font size=\"14\" color=\"#A89B88\">")
                .Append(EscapeVtml(Lang.Get("prosequor:skilltree-tier", tier, node.MaxTier)))
                .Append("</font><br>");
        }
        //sb.Append("<font color=\"#665845\" opacity=\"0.9\">────────────────────────</font><br>");
        // Purchase chrome always describes the rank you would buy next.
        SkillTreeTierDef next = node.TierAt(Math.Min(node.MaxTier, tier + 1));
        if (tier <= 0)
        {
            AppendDescription(sb, next, labelKey: null, bracket: null);
        }
        else
        {
            SkillTreeTierDef current = node.TierAt(tier);
            AppendDescription(
                sb,
                current,
                "skilltree-current",
                SkillEffectTotal.FormatBracket(current, progress, vtml: true));
            if (tier < node.MaxTier)
            {
                AppendDescription(sb, next, "skilltree-next", bracket: null);
            }
        }
        if (state != NodeVisualState.Unlocked)
        {
            sb.Append("<font color=\"#D8B56A\">-  ")
                .Append(EscapeVtml(Lang.Get("prosequor:skilltree-cost", next.Cost)))
                .Append("</font><br>");
            if (next.MinSkillLevel > 0)
            {
                sb.Append("<font color=\"#85A9C4\">-  ")
                    .Append(EscapeVtml(Lang.Get("prosequor:skilltree-requires-level", next.MinSkillLevel)))
                    .Append("</font><br>");
            }
            if (node.RequireGroups.Count > 0)
            {
                List<string> groupTexts = new();
                foreach (RequireGroup group in node.RequireGroups)
                {
                    List<string> names = new();
                    foreach (string req in group.Alternatives)
                    {
                        if (skill.Tree != null && skill.Tree.TryGet(req, out SkillTreeNodeDef reqNode))
                        {
                            names.Add(LangKey(reqNode.NameLang));
                        }
                        else
                        {
                            names.Add(req);
                        }
                    }

                    string joined = string.Join(" or ", names);
                    groupTexts.Add(names.Count > 1 ? $"({joined})" : joined);
                }

                sb.Append("<font color=\"#85A9C4\">   ")
                    .Append(EscapeVtml(Lang.Get(
                        "prosequor:skilltree-requires-nodes",
                        string.Join(", ", groupTexts))))
                    .Append("</font><br>");
            }

            if (node.Excludes.Count > 0)
            {
                List<string> names = new();
                foreach (string excl in node.Excludes)
                {
                    if (skill.Tree != null && skill.Tree.TryGet(excl, out SkillTreeNodeDef exclNode))
                    {
                        names.Add(LangKey(exclNode.NameLang));
                    }
                    else
                    {
                        names.Add(excl);
                    }
                }

                sb.Append("<font color=\"#C48585\">-  ")
                    .Append(EscapeVtml(Lang.Get(
                        "prosequor:skilltree-incompatible-with",
                        string.Join(", ", names))))
                    .Append("</font><br>");
            }

            if (node.IsSpecialization && progress != null && registry != null)
            {
                int used = SpecializationPolicy.OwnedCount(progress, registry);
                int allowed = SpecializationPolicy.AllowedSlots(progress.PlayerLevel, levelUpRules);
                sb.Append("<font color=\"#85A9C4\">-  ")
                    .Append(EscapeVtml(Lang.Get(
                        "prosequor:skilltree-specialization-slots",
                        used,
                        allowed)))
                    .Append("</font><br>");
            }
        }
        string status = state switch
        {
            NodeVisualState.Unlocked =>
                $"<font weight=\"bold\" color=\"#86B95B\">✓  {EscapeVtml(Lang.Get("prosequor:skilltree-unlocked"))}</font>",
            NodeVisualState.Eligible =>
                $"<font weight=\"bold\" color=\"#E6C78A\">›  {EscapeVtml(Lang.Get("prosequor:skilltree-eligible"))}</font>",
            _ when tier > 0 =>
                $"<font color=\"#928A80\">   {EscapeVtml(Lang.Get("prosequor:skilltree-next-locked"))}</font>",
            _ =>
                $"<font color=\"#928A80\">   {EscapeVtml(Lang.Get("prosequor:skilltree-locked"))}</font>"
        };
        sb.Append(status);
        return sb.ToString();
    }
    static void AppendDescription(StringBuilder sb, SkillTreeTierDef tier, string? labelKey, string? bracket)
    {
        string body = RenderDescription(tier);
        if (string.IsNullOrWhiteSpace(body) && string.IsNullOrEmpty(bracket))
        {
            return;
        }

        if (labelKey != null)
        {
            sb.Append("<font size=\"14\" color=\"#A89B88\">")
                .Append(EscapeVtml(Lang.Get("prosequor:" + labelKey)))
                .Append("</font><br>");
        }

        sb.Append("<font lineheight=\"1.15\" color=\"#E8DFD0\">")
            .Append(body)
            .Append(bracket)
            .Append("</font><br><br>");
    }

    static string RenderDescription(SkillTreeTierDef tier) =>
        SkillDescriptionRender.RenderLang(tier.DescriptionLang, tier.DescriptionArgs, vtml: true);

    static string EscapeVtml(string text) => SkillDescriptionRender.EscapeVtml(text);

    static string LangKey(string key, params object[] args) =>
        SkillDescriptionRender.LangKey(key, args);
    enum NodeVisualState
    {
        Locked,
        Eligible,
        Unlocked
    }
    sealed class NodeVisual
    {
        public SkillTreeNodeDef Def { get; set; } = null!;
        public double WorldX { get; set; }
        public double WorldY { get; set; }
        public NodeVisualState State { get; set; }
        public int Tier { get; set; }
        public string Tooltip { get; set; } = "";
        public LoadedTexture? Icon { get; set; }
        public LoadedTexture? Label { get; set; }
    }
    sealed class LinkVisual
    {
        public string ParentId { get; set; } = "";
        public string ChildId { get; set; } = "";
        public double X0 { get; set; }
        public double Y0 { get; set; }
        public double X1 { get; set; }
        public double Y1 { get; set; }
        public bool Lit { get; set; }
    }
}
public static class GuiElementSkillTreeViewportHelpers
{
    public static GuiComposer AddSkillTreeViewport(
        this GuiComposer composer,
        GuiElementSkillTreeViewport element,
        string key)
    {
        if (!composer.Composed)
        {
            composer.AddInteractiveElement(element, key);
        }
        return composer;
    }
}