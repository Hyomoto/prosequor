using Cairo;
using Prosequor.Network;
using Prosequor.Player;
using Prosequor.Progress;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace Prosequor.Client;

/// <summary>
/// Observes unlock points, arms the skill-waiting reminder, and draws the hotbar-right icon.
/// </summary>
public sealed class SkillWaitingHudController : IDisposable
{
    public const float BreathePeriodSeconds = 2.4f;
    public const double GapAfterHotbarUnscaled = 16;
    public const double KeyDiscFraction = 0.38;
    public const int StatusTickMs = 100;
    public const float DrawZBg = 60f;
    public const float DrawZEmblem = 61f;
    public const float DrawZKeyDisc = 62f;
    public const float DrawZKeyText = 63f;

    static readonly AssetLocation BgLoc =
        new(ProsequorModSystem.ModId, "textures/icons/gui/skill-waiting-bg.svg");
    static readonly AssetLocation EmblemLoc =
        new(ProsequorModSystem.ModId, "textures/icons/gui/skill-waiting.svg");

    static readonly Vec4f NeutralTint = StyledSvgIconCache.TintFromArgb(unchecked((int)0xFFE0E8F2));
    static readonly Vec4f GoldTint = StyledSvgIconCache.TintFromArgb(unchecked((int)0xFFE8C95B));
    static readonly Vec4f BlackTint = new(0f, 0f, 0f, 1f);

    readonly ICoreClientAPI capi;
    readonly ProgressNetwork network;
    readonly SkillWaitingHintState hint = new();
    readonly System.Func<string?, bool> trySelectSkillsTab;
    readonly System.Func<bool> isHudElementOpen;

    GuiDialogCharacterBase? characterDlg;
    EntityBehaviorProgress? observedProgress;
    bool started;
    long statusListenerId;
    string? lastStatusSignature;
    bool menuWatchSeeded;
    bool wasMenuOpen;
    int dismissCount;

    LoadedTexture? bgTexture;
    LoadedTexture? emblemTexture;
    LoadedTexture? keyTexture;
    int bakedPx;
    string? bakedKeyLabel;
    float breathePhase;
    CairoFont? keyFont;

    public SkillWaitingHudController(
        ICoreClientAPI capi,
        ProgressNetwork network,
        System.Func<string?, bool> trySelectSkillsTab,
        System.Func<bool> isHudElementOpen)
    {
        this.capi = capi;
        this.network = network;
        this.trySelectSkillsTab = trySelectSkillsTab;
        this.isHudElementOpen = isHudElementOpen;
    }

    /// <summary>
    /// Begins unlock-point observation immediately. Character dialog may be attached later
    /// via <see cref="AttachCharacterDialog"/> once GUIs exist (BlockTexturesLoaded).
    /// </summary>
    public void Start()
    {
        if (started)
        {
            return;
        }

        started = true;
        // Observe on tick — do not force-publish here; the network channel is not Connected yet
        // during StartClientSide.
        statusListenerId = capi.Event.RegisterGameTickListener(OnStatusTick, StatusTickMs);
        network.LevelUpHudReceived += OnLevelUpHud;
    }

    public void AttachCharacterDialog(GuiDialogCharacterBase? dlg)
    {
        if (ReferenceEquals(characterDlg, dlg))
        {
            return;
        }

        characterDlg = dlg;
        // Re-seed closed→open edge detection when the dialog instance changes.
        menuWatchSeeded = false;
    }

    /// <summary>One-line client dump for <c>/prosequor skillhint</c>.</summary>
    public string FormatStatusDump()
    {
        ObserveProgress();
        SkillWaitingHudStatusPacket p = BuildStatusPacket();
        string layout = TryGetHotbarBounds(out _) ? "hotbar-bounds" : "fallback";
        return
            $"armed={p.Armed} shouldDraw={p.ShouldDraw} pts={p.UnlockPoints} synced={p.HasSyncedMirror} " +
            $"seen={hint.Seen} lastPts={hint.LastPoints} skill={hint.SkillHint ?? "-"} dismisses={dismissCount} " +
            $"menu={p.MenuOpen} started={p.HudStarted} hudOpen={p.HudElementOpen} " +
            $"bgOk={p.BgTextureOk} emblemOk={p.EmblemTextureOk} layout={layout} " +
            $"frame={p.FrameWidth}x{p.FrameHeight} draw=({p.DrawX:0},{p.DrawY:0}) size={p.IconSize:0}";
    }

    public void Tick(float deltaTime)
    {
        ObserveProgress();
        if (hint.Armed)
        {
            breathePhase += deltaTime * (MathF.PI * 2f / BreathePeriodSeconds);
            if (breathePhase > MathF.PI * 2f)
            {
                breathePhase -= MathF.PI * 2f;
            }
        }
    }

    public bool ShouldDraw
    {
        get
        {
            int points = observedProgress?.UnlockPoints ?? 0;
            bool menuOpen = characterDlg?.IsOpened() == true;
            return hint.IsVisible(points, menuOpen);
        }
    }

    public void Draw(float deltaTime)
    {
        if (!ShouldDraw)
        {
            return;
        }

        ComputeLayout(out double x, out double y, out double iconSize);
        EnsureIconTextures((int)Math.Ceiling(iconSize));
        if (bgTexture == null || emblemTexture == null)
        {
            return;
        }

        float breathe = (MathF.Sin(breathePhase) + 1f) * 0.5f;
        Vec4f emblemTint = LerpTint(NeutralTint, GoldTint, breathe);

        capi.Render.Render2DTexturePremultipliedAlpha(
            bgTexture.TextureId, (float)x, (float)y, (float)iconSize, (float)iconSize, DrawZBg, BlackTint);
        capi.Render.Render2DTexturePremultipliedAlpha(
            emblemTexture.TextureId, (float)x, (float)y, (float)iconSize, (float)iconSize, DrawZEmblem, emblemTint);

        string keyLabel = ResolveKeyLabel();
        EnsureKeyTexture(keyLabel);
        if (keyTexture == null || keyTexture.TextureId <= 0)
        {
            return;
        }

        double discSize = iconSize * KeyDiscFraction;
        double discX = x + (iconSize - discSize) / 2.0;
        double discY = y + iconSize - discSize * 0.72;

        if (bgTexture.TextureId > 0)
        {
            capi.Render.Render2DTexturePremultipliedAlpha(
                bgTexture.TextureId,
                (float)discX,
                (float)discY,
                (float)discSize,
                (float)discSize,
                DrawZKeyDisc,
                BlackTint);
        }

        double textW = keyTexture.Width;
        double textH = keyTexture.Height;
        double textX = discX + (discSize - textW) / 2.0;
        double textY = discY + (discSize - textH) / 2.0 - 1;
        capi.Render.Render2DTexturePremultipliedAlpha(
            keyTexture.TextureId, (float)textX, (float)textY, (float)textW, (float)textH, DrawZKeyText);
    }

    void OnStatusTick(float dt)
    {
        ObserveProgress();
        WatchCharacterMenuEdge();
        PublishStatusIfChanged();
    }

    /// <summary>
    /// Disarm only on a real closed→open transition. Seeding after the first points observe
    /// avoids GUI-bootstrap <c>OnOpened</c> noise that was clearing the login arm.
    /// </summary>
    void WatchCharacterMenuEdge()
    {
        if (characterDlg == null || !hint.Seen)
        {
            return;
        }

        bool open = characterDlg.IsOpened();
        if (!menuWatchSeeded)
        {
            wasMenuOpen = open;
            menuWatchSeeded = true;
            return;
        }

        if (open && !wasMenuOpen)
        {
            dismissCount++;
            if (hint.ConsumeOpen(out string? openSkillId))
            {
                trySelectSkillsTab(openSkillId);
                PublishStatusIfChanged(force: true);
            }
        }

        wasMenuOpen = open;
    }

    void OnLevelUpHud(LevelUpHudPacket packet)
    {
        if (!packet.SkillLeveledUp)
        {
            return;
        }

        hint.NoteSkillLevelUp(packet.SkillId);
        PublishStatusIfChanged();
    }

    void ObserveProgress()
    {
        EntityBehaviorProgress? progress =
            capi.World?.Player?.Entity?.GetBehavior<EntityBehaviorProgress>();
        if (!ReferenceEquals(observedProgress, progress))
        {
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

        // Wait for WatchedAttributes hydrate — a pre-mirror UnlockPoints of 0 must not
        // become the session's first-seen reading.
        if (observedProgress is { HasSyncedMirror: true })
        {
            hint.Observe(observedProgress.UnlockPoints);
        }
    }

    void OnProgressChanged()
    {
        if (observedProgress is { HasSyncedMirror: true })
        {
            hint.Observe(observedProgress.UnlockPoints);
        }

        PublishStatusIfChanged();
    }

    void PublishStatusIfChanged(bool force = false)
    {
        SkillWaitingHudStatusPacket packet = BuildStatusPacket();
        string signature =
            $"{packet.Armed}|{packet.ShouldDraw}|{packet.HasSyncedMirror}|{packet.UnlockPoints}|" +
            $"{packet.MenuOpen}|{packet.BgTextureOk}|{packet.EmblemTextureOk}|{packet.HudStarted}|" +
            $"{packet.HudElementOpen}|{packet.FrameWidth}x{packet.FrameHeight}|" +
            $"{packet.DrawX:0}|{packet.DrawY:0}|{packet.IconSize:0}";

        if (!force && string.Equals(signature, lastStatusSignature, StringComparison.Ordinal))
        {
            return;
        }

        lastStatusSignature = signature;
        network.SendSkillWaitingStatus(packet);
    }

    SkillWaitingHudStatusPacket BuildStatusPacket()
    {
        bool menuOpen = characterDlg?.IsOpened() == true;
        int points = observedProgress?.UnlockPoints ?? 0;
        bool synced = observedProgress?.HasSyncedMirror == true;
        bool shouldDraw = hint.IsVisible(points, menuOpen);

        ComputeLayout(out double x, out double y, out double iconSize);
        if (shouldDraw || hint.Armed)
        {
            EnsureIconTextures((int)Math.Ceiling(iconSize));
        }

        return new SkillWaitingHudStatusPacket
        {
            Armed = hint.Armed,
            ShouldDraw = shouldDraw,
            HasSyncedMirror = synced,
            UnlockPoints = points,
            MenuOpen = menuOpen,
            BgTextureOk = bgTexture is { TextureId: > 0 },
            EmblemTextureOk = emblemTexture is { TextureId: > 0 },
            DrawX = (float)x,
            DrawY = (float)y,
            IconSize = (float)iconSize,
            FrameWidth = capi.Render.FrameWidth,
            FrameHeight = capi.Render.FrameHeight,
            HudStarted = started,
            HudElementOpen = isHudElementOpen()
        };
    }

    void ComputeLayout(out double x, out double y, out double iconSize)
    {
        iconSize = GuiElement.scaled(GuiElementPassiveItemSlot.unscaledSlotSize);
        double gap = GuiElement.scaled(GapAfterHotbarUnscaled);
        double margin = GuiElement.scaled(4);

        if (TryGetHotbarBounds(out ElementBounds hotbar))
        {
            hotbar.CalcWorldBounds();
            x = hotbar.renderX + hotbar.OuterWidth + gap;
            y = hotbar.renderY + (hotbar.OuterHeight - iconSize) * 0.5;
        }
        else
        {
            // Fallback before HudHotbar has composed (rare): vanilla dialog is fixedWidth 850.
            double hotbarW = GuiElement.scaled(850);
            x = capi.Render.FrameWidth / 2.0 + hotbarW / 2.0 + gap;
            y = capi.Render.FrameHeight - GuiElement.scaled(80) * 0.5 - iconSize * 0.5;
        }

        x = GameMath.Clamp(x, margin, Math.Max(margin, capi.Render.FrameWidth - iconSize - margin));
        y = GameMath.Clamp(y, margin, Math.Max(margin, capi.Render.FrameHeight - iconSize - margin));
    }

    bool TryGetHotbarBounds(out ElementBounds bounds)
    {
        bounds = null!;
        foreach (GuiDialog gui in capi.Gui.LoadedGuis)
        {
            if (!string.Equals(gui.GetType().Name, "HudHotbar", StringComparison.Ordinal))
            {
                continue;
            }

            if (!gui.Composers.ContainsKey("hotbar"))
            {
                return false;
            }

            bounds = gui.Composers["hotbar"].Bounds;
            return bounds != null;
        }

        return false;
    }

    void EnsureIconTextures(int px)
    {
        px = Math.Max(16, px);
        if (bgTexture != null && emblemTexture != null && bakedPx == px)
        {
            return;
        }

        DisposeIconTextures();
        bakedPx = px;
        bgTexture = BakeSvgMask(BgLoc, px);
        emblemTexture = BakeSvgMask(EmblemLoc, px);
    }

    LoadedTexture? BakeSvgMask(AssetLocation loc, int px)
    {
        IAsset? asset = capi.Assets.TryGet(loc);
        if (asset == null)
        {
            capi.Logger.Warning("[prosequor] Missing skill-waiting icon {0}.", loc);
            return null;
        }

        ImageSurface surface = new(Format.Argb32, px, px);
        Context ctx = new(surface);
        try
        {
            capi.Gui.DrawSvg(asset, surface, 0, 0, px, px, ColorUtil.WhiteArgb);
            LoadedTexture texture = new(capi);
            capi.Gui.LoadOrUpdateCairoTexture(surface, linearMag: true, ref texture);
            return texture.TextureId > 0 ? texture : null;
        }
        finally
        {
            ctx.Dispose();
            surface.Dispose();
        }
    }

    string ResolveKeyLabel()
    {
        string code = characterDlg?.ToggleKeyCombinationCode ?? "character";
        HotKey? hotkey = capi.Input.GetHotKeyByCode(code);
        if (hotkey?.CurrentMapping != null)
        {
            string primary = hotkey.CurrentMapping.PrimaryAsString();
            if (!string.IsNullOrWhiteSpace(primary))
            {
                return primary.Trim().ToUpperInvariant();
            }

            string full = hotkey.CurrentMapping.ToString();
            if (!string.IsNullOrWhiteSpace(full))
            {
                return full.Trim().ToUpperInvariant();
            }
        }

        return "C";
    }

    void EnsureKeyTexture(string label)
    {
        if (keyTexture != null && string.Equals(bakedKeyLabel, label, StringComparison.Ordinal))
        {
            return;
        }

        keyTexture?.Dispose();
        keyTexture = null;
        bakedKeyLabel = label;

        keyFont ??= CairoFont.WhiteSmallText()
            .WithFont(GuiStyle.DecorativeFontName)
            .WithFontSize(18)
            .WithWeight(FontWeight.Bold)
            .WithColor(new[] { 1.0, 1.0, 1.0, 1.0 });

        keyTexture = capi.Gui.TextTexture.GenTextTexture(label, keyFont);
    }

    static Vec4f LerpTint(Vec4f a, Vec4f b, float t)
    {
        t = GameMath.Clamp(t, 0f, 1f);
        return new Vec4f(
            a.R + (b.R - a.R) * t,
            a.G + (b.G - a.G) * t,
            a.B + (b.B - a.B) * t,
            a.A + (b.A - a.A) * t);
    }

    void DisposeIconTextures()
    {
        bgTexture?.Dispose();
        bgTexture = null;
        emblemTexture?.Dispose();
        emblemTexture = null;
        bakedPx = 0;
    }

    public void Dispose()
    {
        if (statusListenerId != 0)
        {
            capi.Event.UnregisterGameTickListener(statusListenerId);
            statusListenerId = 0;
        }

        network.LevelUpHudReceived -= OnLevelUpHud;

        if (characterDlg != null)
        {
            characterDlg = null;
        }

        menuWatchSeeded = false;
        wasMenuOpen = false;
        dismissCount = 0;

        if (observedProgress != null)
        {
            observedProgress.Changed -= OnProgressChanged;
            observedProgress = null;
        }

        DisposeIconTextures();
        keyTexture?.Dispose();
        keyTexture = null;
        bakedKeyLabel = null;
        lastStatusSignature = null;
        started = false;
    }
}
