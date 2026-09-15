using System;
using Prosequor.Ability;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.Client;
using Vintagestory.Client.NoObf;

namespace Prosequor.Client.CatEyes;

/// <summary>
/// Cat eyes post-process at AfterFinalComposition.
/// Always-on pipeline: meter → adaptation → response lerp.
/// Capacity comes from <see cref="CatEyesStation"/> (attribute-agnostic); adaptation is scene-driven.
/// </summary>
public sealed class CatEyesRenderer : IRenderer, IDisposable
{
    const int LumaSize = 128;
    const int MidSize = 32;
    const int SmallSize = 8;

    /// <summary>Soft pull of meter toward screen center (0 = corners only).</summary>
    public const float CenterBias = 0.35f;

    /// <summary>Slow ramp when target &gt; adaptation (eyes adjusting to dark).</summary>
    public const float AdaptTimeUpSeconds = 2.0f;

    /// <summary>Fast fall when target &lt; adaptation (look at light → no-op).</summary>
    public const float AdaptTimeDownSeconds = 0.2f;

    /// <summary>
    /// Meter brightness at/above this → target = 0.
    /// Below → target rises toward 1 as meter → 0.
    /// </summary>
    public const float Equilibrium = 0.18f;

    public const float BrightenShadows = 0.8f;
    public const float BrightenMidtones = 0.35f;
    public const float BrightenHighlights = 0.04f;
    public const float DesatAmount = 0.4f;

    readonly ICoreClientAPI capi;
    readonly CatEyesShaders shaders;
    readonly MeshRef quadRef;

    FrameBufferRef? fbScene;
    FrameBufferRef? fbLuma;
    FrameBufferRef? fbMid;
    FrameBufferRef? fbSmall;
    FrameBufferRef? fbMeter;
    FrameBufferRef? fbAdapt;
    FrameBufferRef? fbAdaptLast;

    bool disposed;

    public double RenderOrder => -1.0;
    public int RenderRange => 1;

    public CatEyesRenderer(ICoreClientAPI capi, CatEyesShaders shaders)
    {
        this.capi = capi;
        this.shaders = shaders;
        MeshData quad = QuadMeshUtil.GetCustomQuadModelData(-1f, -1f, 0f, 2f, 2f);
        quad.Rgba = null!;
        quadRef = capi.Render.UploadMesh(quad);
        EnsureMeterFrameBuffers();
    }

    public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
    {
        if (disposed || deltaTime <= 0f)
        {
            return;
        }

        if (shaders.Luma == null || shaders.Reduce == null || shaders.FiveSample == null
            || shaders.Adapt == null || shaders.Copy == null || shaders.CopyColor == null
            || shaders.Apply == null)
        {
            return;
        }

        FrameBufferRef? primary = capi.Render.FrameBuffers?[(int)EnumFrameBuffer.Primary];
        if (primary == null || primary.ColorTextureIds == null || primary.ColorTextureIds.Length == 0
            || primary.Width <= 0 || primary.Height <= 0)
        {
            return;
        }

        if (!EnsureMeterFrameBuffers() || !EnsureSceneFrameBuffer(primary.Width, primary.Height))
        {
            return;
        }

        float capacity = ResolveCapacity();

        IShaderProgram? previous = capi.Render.CurrentActiveShader;
        previous?.Stop();

        capi.Render.GLDisableDepthTest();
        capi.Render.GlToggleBlend(false);

        // 1) Snapshot
        DrawTo(fbScene!, shaders.CopyColor, () =>
        {
            shaders.CopyColor!.BindTexture2D("src", primary.ColorTextureIds[0], 0);
        });

        // 2) Luma + mean reduce 128 → 32 → 8
        DrawTo(fbLuma!, shaders.Luma, () =>
        {
            shaders.Luma!.BindTexture2D("primaryScene", fbScene!.ColorTextureIds[0], 0);
        });
        DrawReduce(fbLuma!, fbMid!);
        DrawReduce(fbMid!, fbSmall!);

        // 3) Soft center-biased five-sample meter
        DrawTo(fbMeter!, shaders.FiveSample, () =>
        {
            shaders.FiveSample!.BindTexture2D("src", fbSmall!.ColorTextureIds[0], 0);
            shaders.FiveSample.Uniform("centerBias", CenterBias);
        });

        // 4) Adaptation (scene-driven; independent of capacity)
        float dt = Math.Min(deltaTime, AdaptTimeUpSeconds);
        float alphaUp = 1f - MathF.Exp(-dt / AdaptTimeUpSeconds);
        float alphaDown = 1f - MathF.Exp(-dt / AdaptTimeDownSeconds);
        DrawTo(fbAdapt!, shaders.Adapt, () =>
        {
            shaders.Adapt!.BindTexture2D("sceneMeter", fbMeter!.ColorTextureIds[0], 0);
            shaders.Adapt.BindTexture2D("prevAdapt", fbAdaptLast!.ColorTextureIds[0], 1);
            shaders.Adapt.Uniform("equilibrium", Equilibrium);
            shaders.Adapt.Uniform("alphaUp", alphaUp);
            shaders.Adapt.Uniform("alphaDown", alphaDown);
        });

        DrawTo(fbAdaptLast!, shaders.Copy, () =>
        {
            shaders.Copy!.BindTexture2D("src", fbAdapt!.ColorTextureIds[0], 0);
        });

        // 5) Always apply: identity when t==0 is a byproduct of the response curve
        ScreenManager.Platform!.CurrentFrameBuffer = primary;
        shaders.Apply.Use();
        shaders.Apply.BindTexture2D("primaryScene", fbScene!.ColorTextureIds[0], 0);
        shaders.Apply.BindTexture2D("adaptTex", fbAdapt!.ColorTextureIds[0], 1);
        shaders.Apply.Uniform("capacity", capacity);
        shaders.Apply.Uniform("brightenShadows", BrightenShadows);
        shaders.Apply.Uniform("brightenMidtones", BrightenMidtones);
        shaders.Apply.Uniform("brightenHighlights", BrightenHighlights);
        shaders.Apply.Uniform("desatAmount", DesatAmount);
        capi.Render.RenderMesh(quadRef);
        shaders.Apply.Stop();

        capi.Render.GLEnableDepthTest();
        capi.Render.GlToggleBlend(true, EnumBlendMode.Standard);
        previous?.Use();
    }

    float ResolveCapacity()
    {
        IPlayer? player = capi.World?.Player;
        if (player == null)
        {
            return 0f;
        }

        return CatEyesStation.ResolveCapacity(player);
    }

    void DrawReduce(FrameBufferRef src, FrameBufferRef dest)
    {
        float step = 1f / (4f * Math.Max(dest.Width, 1));
        DrawTo(dest, shaders.Reduce!, () =>
        {
            shaders.Reduce!.BindTexture2D("src", src.ColorTextureIds[0], 0);
            shaders.Reduce.Uniform("texelSize", new Vec2f(step, step));
        });
    }

    void DrawTo(FrameBufferRef dest, IShaderProgram shader, Action bind)
    {
        ScreenManager.Platform!.CurrentFrameBuffer = dest;
        shader.Use();
        bind();
        capi.Render.RenderMesh(quadRef);
        shader.Stop();
    }

    bool EnsureMeterFrameBuffers()
    {
        if (ScreenManager.Platform == null)
        {
            return false;
        }

        fbLuma ??= CreateColorFb("prosequor-cateyes-luma", LumaSize, LumaSize);
        fbMid ??= CreateColorFb("prosequor-cateyes-mid", MidSize, MidSize);
        fbSmall ??= CreateColorFb("prosequor-cateyes-small", SmallSize, SmallSize);
        fbMeter ??= CreateColorFb("prosequor-cateyes-meter", 1, 1);
        fbAdapt ??= CreateColorFb("prosequor-cateyes-adapt", 1, 1);
        fbAdaptLast ??= CreateColorFb("prosequor-cateyes-adapt-last", 1, 1);
        return fbLuma != null && fbMid != null && fbSmall != null
            && fbMeter != null && fbAdapt != null && fbAdaptLast != null;
    }

    bool EnsureSceneFrameBuffer(int width, int height)
    {
        if (fbScene != null && fbScene.Width == width && fbScene.Height == height)
        {
            return true;
        }

        DestroyFb(ref fbScene);
        fbScene = CreateColorFb("prosequor-cateyes-scene", width, height);
        return fbScene != null;
    }

    static FrameBufferRef? CreateColorFb(string name, int width, int height)
    {
        if (ScreenManager.Platform == null)
        {
            return null;
        }

        var attrs = new FramebufferAttrs(name, width, height)
        {
            Attachments =
            [
                new FramebufferAttrsAttachment
                {
                    AttachmentType = EnumFramebufferAttachment.ColorAttachment0,
                    Texture = new RawTexture
                    {
                        Width = width,
                        Height = height,
                        PixelFormat = EnumTexturePixelFormat.Rgba,
                        PixelInternalFormat = EnumTextureInternalFormat.Rgba8,
                        MinFilter = EnumTextureFilter.Linear,
                        MagFilter = EnumTextureFilter.Linear,
                        WrapS = EnumTextureWrap.ClampToEdge,
                        WrapT = EnumTextureWrap.ClampToEdge
                    }
                }
            ]
        };
        return ScreenManager.Platform.CreateFramebuffer(attrs);
    }

    static void DestroyFb(ref FrameBufferRef? fb)
    {
        if (fb == null)
        {
            return;
        }

        ScreenManager.Platform?.DisposeFrameBuffer(fb, disposeTextures: true);
        fb = null;
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        capi.Render.DeleteMesh(quadRef);
        DestroyFb(ref fbScene);
        DestroyFb(ref fbLuma);
        DestroyFb(ref fbMid);
        DestroyFb(ref fbSmall);
        DestroyFb(ref fbMeter);
        DestroyFb(ref fbAdapt);
        DestroyFb(ref fbAdaptLast);
    }
}
