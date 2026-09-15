using System.Text;
using Cairo;
using Prosequor.Data;
using Prosequor.Network;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;

namespace Prosequor.Client;

/// <summary>Queues level-up HUD packets and drives animation timing.</summary>
public sealed class LevelUpHudController
{
    public const float FadeInRowDuration = 0.3f;
    public const float TitleStartDelay = 0.25f;
    public const float TitleCharDelay = 0.025f;
    public const float TitleCharFade = 0.12f;
    public const float BarStartDelay = 0.85f;
    public const float BarAnimDuration = 0.6f;
    public const float LevelFlashDuration = 0.25f;
    public const float HoldDuration = 1.5f;
    public const float FadeOutDuration = 0.4f;

    static readonly double[] DimLabelColor = { 0.55, 0.55, 0.55, 1.0 };
    const double TextStrokeWidth = 1.0;
    const float LevelUpSoundVolume = 1f;

    static readonly AssetLocation SkillUpSound = new(ProsequorModSystem.ModId, "sounds/skill_up.ogg");
    static readonly AssetLocation PlayerLevelUpSound = new(ProsequorModSystem.ModId, "sounds/level_up.ogg");

    readonly ICoreClientAPI capi;
    readonly Queue<LevelUpHudPacket> pending = new();
    LevelUpHudPacket? active;
    float timer;
    string headline = "";
    string attributeLine = "";
    LoadedTexture? labelTexture;
    LoadedTexture? levelTexture;
    CairoFont? headlineFont;
    CairoFont? rowFont;
    int renderedPlayerLevel = int.MinValue;
    readonly List<StaggeredChar> headlineChars = new();
    readonly List<StaggeredChar> attributeChars = new();

    public bool HasActive => active != null;

    public LevelUpHudController(ICoreClientAPI capi)
    {
        this.capi = capi;
    }

    public void Enqueue(LevelUpHudPacket packet)
    {
        pending.Enqueue(packet);
        if (active == null)
        {
            StartNext();
        }
    }

    public void Tick(float deltaTime)
    {
        if (active == null)
        {
            return;
        }

        timer += deltaTime;
        float total = TotalDuration(active);
        if (timer >= total)
        {
            DisposeTextures();
            active = null;
            timer = 0f;
            StartNext();
        }
    }

    public void Draw(ICoreClientAPI capi, double centerX, double topY, double scale)
    {
        if (active == null)
        {
            return;
        }

        EnsureTextures(capi);
        float masterAlpha = MasterAlpha(active);
        if (masterAlpha <= 0.001f)
        {
            return;
        }

        double rowAlpha = RowAlpha() * masterAlpha;
        if (rowAlpha <= 0.001f && timer < TitleStartDelay)
        {
            return;
        }

        const double panelWidth = 420;
        const double headlineHeight = 28;
        const double rowHeight = 22;
        const double attrHeight = 20;
        const double gap = 10;
        double w = panelWidth * scale;
        double x = centerX - w / 2;
        double headlineY = topY;

        if (active.SkillLeveledUp || !string.IsNullOrWhiteSpace(headline))
        {
            DrawStaggeredLine(
                capi,
                headlineChars,
                x,
                headlineY,
                w,
                headlineHeight * scale,
                masterAlpha,
                scale,
                CharAlpha);
        }

        double rowY = headlineY + (headlineChars.Count > 0 ? headlineHeight * scale + gap * scale : 0);
        DrawProgressRow(capi, x, rowY, w, rowHeight * scale, rowAlpha, scale);

        if (attributeChars.Count > 0)
        {
            double attrY = rowY + rowHeight * scale + gap * scale;
            DrawStaggeredLine(
                capi,
                attributeChars,
                x,
                attrY,
                w,
                attrHeight * scale,
                masterAlpha,
                scale,
                AttributeCharAlpha);
        }
    }

    void DrawStaggeredLine(
        ICoreClientAPI capi,
        List<StaggeredChar> chars,
        double x,
        double y,
        double width,
        double height,
        float masterAlpha,
        double scale,
        System.Func<int, float> charAlpha)
    {
        if (chars.Count == 0)
        {
            return;
        }

        StaggeredChar first = chars[0];
        if (first.Texture == null)
        {
            return;
        }

        double totalWidth = (chars[^1].OffsetX + chars[^1].Texture!.Width) * scale;
        double startX = x + (width - totalWidth) / 2;
        double drawY = y + (height - first.Texture.Height * scale) / 2;

        for (int i = 0; i < chars.Count; i++)
        {
            float opacity = charAlpha(i) * masterAlpha;
            if (opacity <= 0.001f)
            {
                break;
            }

            StaggeredChar ch = chars[i];
            if (ch.Texture == null || ch.Texture.TextureId <= 0)
            {
                continue;
            }

            float charX = (float)(startX + ch.OffsetX * scale);
            float charW = (float)(ch.Texture.Width * scale);
            float charH = (float)(ch.Texture.Height * scale);
            DrawFadedTexture(capi, ch.Texture, charX, (float)drawY, charW, charH, opacity);
        }
    }

    void DrawProgressRow(
        ICoreClientAPI capi,
        double x,
        double y,
        double width,
        double height,
        double rowAlpha,
        double scale)
    {
        if (active == null || labelTexture == null || levelTexture == null)
        {
            return;
        }

        float opacity = (float)rowAlpha;
        if (opacity <= 0.001f)
        {
            return;
        }

        double labelW = labelTexture.Width * scale;
        double levelW = levelTexture.Width * scale;
        double barMargin = 8 * scale;
        double barX = x + labelW + barMargin;
        double barW = width - labelW - levelW - barMargin * 2;
        double barH = 4 * scale;
        double barY = y + (height - barH) / 2;
        float labelX = (float)x;
        float labelY = (float)(y + (height - labelTexture.Height * scale) / 2);
        float labelH = (float)(labelTexture.Height * scale);

        DrawFadedTexture(
            capi,
            labelTexture,
            labelX,
            labelY,
            (float)labelW,
            labelH,
            opacity);

        DrawBar(capi, barX, barY, barW, barH, rowAlpha);

        float levelX = (float)(x + width - levelW);
        float levelY = (float)(y + (height - levelTexture.Height * scale) / 2);
        float levelH = (float)(levelTexture.Height * scale);
        DrawFadedTexture(
            capi,
            levelTexture,
            levelX,
            levelY,
            (float)levelW,
            levelH,
            opacity);
    }

    /// <summary>
    /// Premultiplied-alpha fade must scale RGB and A together: (a,a,a,a).
    /// Straight-alpha (1,1,1,a) leaves glyph RGB full so only black shadows appear to fade.
    /// </summary>
    static void DrawFadedTexture(
        ICoreClientAPI capi,
        LoadedTexture texture,
        float x,
        float y,
        float width,
        float height,
        float opacity)
    {
        if (opacity <= 0.001f || texture.TextureId <= 0)
        {
            return;
        }

        capi.Render.Render2DTexturePremultipliedAlpha(
            texture.TextureId,
            x,
            y,
            width,
            height,
            50f,
            new Vec4f(opacity, opacity, opacity, opacity));
    }

    void DrawBar(ICoreClientAPI capi, double x, double y, double w, double h, double rowAlpha)
    {
        if (active == null)
        {
            return;
        }

        float fill = CurrentBarFill();
        bool flash = InLevelFlash();
        double bracketW = 6;
        double innerX = x + bracketW;
        double innerW = Math.Max(1, w - bracketW * 2);
        int trackColor = RgbaColor(38, 38, 38, rowAlpha * 0.9);
        int fillColor = RgbaColor(217, 217, 217, rowAlpha);

        capi.Render.RenderRectangle((float)innerX, (float)y, 50, (float)innerW, (float)h, trackColor);

        if (flash)
        {
            float flashT = FlashT();
            float flashAlpha = (float)rowAlpha * (1f - Math.Abs(flashT * 2f - 1f));
            capi.Render.RenderRectangle(
                (float)innerX,
                (float)y,
                50,
                (float)innerW,
                (float)h,
                RgbaColor(255, 255, 255, flashAlpha));
        }
        else if (fill > 0.001f)
        {
            capi.Render.RenderRectangle(
                (float)innerX,
                (float)y,
                50,
                (float)(innerW * fill),
                (float)h,
                fillColor);
        }

        int bracketColor = RgbaColor(179, 179, 179, rowAlpha);
        DrawBracket(capi, x, y, h, true, bracketColor);
        DrawBracket(capi, x + w - bracketW, y, h, false, bracketColor);
    }

    static void DrawBracket(ICoreClientAPI capi, double x, double y, double h, bool left, int color)
    {
        float x0 = (float)x;
        float y0 = (float)y;
        float x1 = (float)(x + (left ? 4 : 6));
        float y1 = (float)(y + h);
        float thickness = 1f;
        capi.Render.RenderRectangle(x0, y0, 50, thickness, y1 - y0, color);
        capi.Render.RenderRectangle(x0, y0, 50, x1 - x0, thickness, color);
        capi.Render.RenderRectangle(left ? x0 : x1, y1 - thickness, 50, x1 - x0, thickness, color);
    }

    static int RgbaColor(int r, int g, int b, double alpha) =>
        ColorUtil.ColorFromRgba(r, g, b, (int)(255 * GameMath.Clamp(alpha, 0, 1)));

    float CurrentBarFill()
    {
        if (active == null)
        {
            return 0f;
        }

        float t = BarT();
        if (!active.PlayerLeveledUp)
        {
            return Lerp(active.PlayerBarFillBefore, active.PlayerBarFillAfter, t);
        }

        if (t < 1f)
        {
            return Lerp(active.PlayerBarFillBefore, 1f, t);
        }

        if (InLevelFlash())
        {
            return 1f;
        }

        float afterT = AfterFlashT();
        return Lerp(0f, active.PlayerBarFillAfter, afterT);
    }

    bool InLevelFlash() =>
        active?.PlayerLeveledUp == true
        && timer >= BarStartDelay + BarAnimDuration
        && timer < BarStartDelay + BarAnimDuration + LevelFlashDuration;

    float FlashT()
    {
        float start = BarStartDelay + BarAnimDuration;
        return GameMath.Clamp((timer - start) / LevelFlashDuration, 0f, 1f);
    }

    float AfterFlashT()
    {
        float start = BarStartDelay + BarAnimDuration + LevelFlashDuration;
        return GameMath.Clamp((timer - start) / 0.2f, 0f, 1f);
    }

    float BarT() =>
        active == null
            ? 0f
            : SmoothStep(GameMath.Clamp((timer - BarStartDelay) / BarAnimDuration, 0f, 1f));

    float RowAlpha() => SmoothStep(GameMath.Clamp(timer / FadeInRowDuration, 0f, 1f));

    float AttributeLineStart()
    {
        if (active == null || active.AttributeGains == null || active.AttributeGains.Count == 0)
        {
            return float.MaxValue;
        }

        return active.PlayerLeveledUp
            ? BarStartDelay + BarAnimDuration
            : 0f;
    }

    float AttributeCharAlpha(int index)
    {
        float start = AttributeLineStart() + index * TitleCharDelay;
        return SmoothStep(GameMath.Clamp((timer - start) / TitleCharFade, 0f, 1f));
    }

    float TitleAlpha()
    {
        if (active == null || !active.SkillLeveledUp)
        {
            return SmoothStep(GameMath.Clamp((timer - TitleStartDelay) / 0.35f, 0f, 1f));
        }

        return 1f;
    }

    float CharAlpha(int index)
    {
        float start = TitleStartDelay + index * TitleCharDelay;
        return SmoothStep(GameMath.Clamp((timer - start) / TitleCharFade, 0f, 1f));
    }

    float MasterAlpha(LevelUpHudPacket packet)
    {
        float total = TotalDuration(packet);
        float fadeStart = total - FadeOutDuration;
        if (timer < fadeStart)
        {
            return 1f;
        }

        return 1f - SmoothStep(GameMath.Clamp((timer - fadeStart) / FadeOutDuration, 0f, 1f));
    }

    static float TotalDuration(LevelUpHudPacket packet)
    {
        float end = BarStartDelay + BarAnimDuration + HoldDuration + FadeOutDuration;
        if (packet.PlayerLeveledUp)
        {
            end += LevelFlashDuration + 0.2f;
        }

        if (packet.AttributeGains is { Count: > 0 })
        {
            float attrStart = packet.PlayerLeveledUp
                ? BarStartDelay + BarAnimDuration
                : 0f;
            int charCount = Math.Max(1, BuildAttributeLine(packet.AttributeGains).Length);
            float attrStagger = charCount * TitleCharDelay + TitleCharFade;
            end = Math.Max(end, attrStart + attrStagger + HoldDuration + FadeOutDuration);
        }

        if (packet.SkillLeveledUp)
        {
            end = Math.Max(end, TitleStartDelay + 0.6f + HoldDuration + FadeOutDuration);
        }

        return end;
    }

    void StartNext()
    {
        DisposeTextures();
        if (!pending.TryDequeue(out LevelUpHudPacket? packet))
        {
            return;
        }

        active = packet;
        timer = 0f;
        renderedPlayerLevel = int.MinValue;
        PlayLevelUpSound(packet);
    }

    void PlayLevelUpSound(LevelUpHudPacket packet)
    {
        AssetLocation sound = packet.PlayerLeveledUp ? PlayerLevelUpSound : SkillUpSound;
        capi.Gui.PlaySound(sound, randomizePitch: false, LevelUpSoundVolume);
    }

    void EnsureTextures(ICoreClientAPI capi)
    {
        if (active == null)
        {
            return;
        }

        headlineFont ??= CairoFont.WhiteSmallText()
            .WithFontSize(20)
            .WithWeight(FontWeight.Bold)
            .WithStroke(ColorUtil.BlackArgbDouble, TextStrokeWidth);
        rowFont ??= CairoFont.WhiteDetailText()
            .WithFontSize(14)
            .WithStroke(ColorUtil.BlackArgbDouble, TextStrokeWidth);

        int playerLevel = active.PlayerLevelAfter;
        if (active.PlayerLeveledUp
            && timer < BarStartDelay + BarAnimDuration + LevelFlashDuration)
        {
            playerLevel = active.PlayerLevelBefore;
        }

        if (headlineChars.Count == 0)
        {
            headline = BuildHeadline(capi, active);
            BuildStaggeredChars(capi, headline, headlineFont, headlineChars);
        }

        labelTexture ??= capi.Gui.TextTexture.GenTextTexture(
            Lang.Get("prosequor:levelup-hud-progress-label"),
            rowFont.Clone().WithColor(DimLabelColor));

        if (attributeChars.Count == 0 && active.AttributeGains is { Count: > 0 })
        {
            attributeLine = BuildAttributeLine(active.AttributeGains);
            if (!string.IsNullOrEmpty(attributeLine) && rowFont != null)
            {
                BuildStaggeredChars(capi, attributeLine, rowFont, attributeChars);
            }
        }

        if (levelTexture == null || renderedPlayerLevel != playerLevel)
        {
            levelTexture?.Dispose();
            renderedPlayerLevel = playerLevel;
            levelTexture = capi.Gui.TextTexture.GenTextTexture(
                playerLevel.ToString(),
                rowFont);
        }
    }

    void BuildStaggeredChars(
        ICoreClientAPI capi,
        string text,
        CairoFont font,
        List<StaggeredChar> dest)
    {
        DisposeStaggeredChars(dest);
        if (text.Length == 0)
        {
            return;
        }

        double cursorX = 0;
        for (int i = 0; i < text.Length; i++)
        {
            string ch = text[i].ToString();
            LoadedTexture texture = capi.Gui.TextTexture.GenTextTexture(ch, font);
            dest.Add(new StaggeredChar
            {
                Texture = texture,
                OffsetX = cursorX
            });
            cursorX += font.GetTextExtents(ch).Width;
        }
    }

    static string BuildHeadline(ICoreClientAPI capi, LevelUpHudPacket packet)
    {
        if (packet.SkillLeveledUp)
        {
            string skillName = packet.SkillId;
            ISkillRegistry? registry = ProsequorModSystem.For(capi)?.Registry;
            if (registry != null && registry.TryGet(packet.SkillId, out SkillDef def))
            {
                skillName = SkillRegistry.DisplayName(def);
            }

            return Lang.Get(
                    "prosequor:levelup-hud-skill",
                    skillName.ToUpperInvariant(),
                    packet.SkillLevelAfter)
                .ToUpperInvariant();
        }

        return Lang.Get("prosequor:levelup-hud-player-only").ToUpperInvariant();
    }

    static string BuildAttributeLine(IReadOnlyList<string> attributeIds)
    {
        if (attributeIds.Count == 0)
        {
            return "";
        }

        List<string> names = new(attributeIds.Count);
        foreach (string id in attributeIds)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                continue;
            }

            names.Add(Lang.Get("prosequor:attribute-" + id.Trim()));
        }

        if (names.Count == 0)
        {
            return "";
        }

        if (names.Count == 1)
        {
            return Lang.Get("prosequor:levelup-hud-attribute", names[0]);
        }

        return Lang.Get("prosequor:levelup-hud-attribute-multi", JoinNames(names));
    }

    static string JoinNames(IReadOnlyList<string> names)
    {
        if (names.Count == 2)
        {
            return names[0] + " and " + names[1];
        }

        StringBuilder sb = new();
        for (int i = 0; i < names.Count; i++)
        {
            if (i > 0)
            {
                sb.Append(i == names.Count - 1 ? " and " : ", ");
            }

            sb.Append(names[i]);
        }

        return sb.ToString();
    }

    void DisposeTextures()
    {
        DisposeStaggeredChars(headlineChars);
        DisposeStaggeredChars(attributeChars);
        labelTexture?.Dispose();
        levelTexture?.Dispose();
        labelTexture = null;
        levelTexture = null;
        headline = "";
        attributeLine = "";
        renderedPlayerLevel = int.MinValue;
    }

    static void DisposeStaggeredChars(List<StaggeredChar> chars)
    {
        foreach (StaggeredChar ch in chars)
        {
            ch.Texture?.Dispose();
        }

        chars.Clear();
    }

    public void Dispose()
    {
        pending.Clear();
        active = null;
        DisposeTextures();
    }

    static float SmoothStep(float t) => t * t * (3f - 2f * t);

    static float Lerp(float a, float b, float t) => a + (b - a) * t;

    sealed class StaggeredChar
    {
        public LoadedTexture? Texture { get; init; }
        public double OffsetX { get; init; }
    }
}
