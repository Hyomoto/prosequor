using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace Prosequor.Client.CatEyes;

/// <summary>
/// Client lifecycle for Perception cat eyes: shaders + AfterFinalComposition renderer.
/// Always-on meter → adaptation → response; capacity from CatEyesStation (pipeline).
/// </summary>
public sealed class CatEyesController : IDisposable
{
    readonly ICoreClientAPI capi;
    readonly CatEyesShaders shaders;
    CatEyesRenderer? renderer;
    bool disposed;

    public CatEyesController(ICoreClientAPI capi)
    {
        this.capi = capi;
        shaders = new CatEyesShaders(capi);
        capi.Event.ReloadShader += LoadShaders;
        LoadShaders();
        renderer = new CatEyesRenderer(capi, shaders);
        capi.Event.RegisterRenderer(renderer, EnumRenderStage.AfterFinalComposition);
    }

    bool LoadShaders()
    {
        bool ok = shaders.Load();
        if (!ok)
        {
            capi.Logger.Error("[{0}] Cat eyes shaders failed to load.", ProsequorModSystem.ModId);
        }

        return ok;
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        capi.Event.ReloadShader -= LoadShaders;

        if (renderer != null)
        {
            capi.Event.UnregisterRenderer(renderer, EnumRenderStage.AfterFinalComposition);
            renderer.Dispose();
            renderer = null;
        }
    }
}
