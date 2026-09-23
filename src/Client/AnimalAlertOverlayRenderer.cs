using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace Prosequor.Client;

/// <summary>
/// Debug overlay: alert / threat bars above animals that currently sense the player.
/// Fill = alert 0–100. Color: calm (cyan), awake (amber), committed (red).
/// A thin secondary fill shows current threat.
/// </summary>
public sealed class AnimalAlertOverlayRenderer : IRenderer, IDisposable
{
    const float BarWidth = 60f;
    const float BarHeight = 8f;
    const float ThreatHeight = 3f;
    const float MaxDistance = 48f;

    readonly ICoreClientAPI capi;
    readonly MeshRef outlineRef;
    readonly MeshRef fillRef;
    readonly Matrixf mv = new();
    bool enabled = true;

    public double RenderOrder => 0.95;

    public int RenderRange => 50;

    public bool Enabled
    {
        get => enabled;
        set => enabled = value;
    }

    public AnimalAlertOverlayRenderer(ICoreClientAPI api)
    {
        capi = api;
        outlineRef = api.Render.UploadMesh(LineMeshUtil.GetRectangle(-1));
        fillRef = api.Render.UploadMesh(QuadMeshUtil.GetQuad());
        api.Event.RegisterRenderer(this, EnumRenderStage.Ortho, "prosequor-alert");
    }

    public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
    {
        if (!enabled || capi.World?.Player?.Entity == null)
        {
            return;
        }

        EntityPlayer self = capi.World.Player.Entity;
        IShaderProgram? shader = capi.Render.CurrentActiveShader;
        if (shader == null)
        {
            return;
        }

        IEnumerable<Entity> entities = capi.World.LoadedEntities.Values;
        foreach (Entity entity in entities)
        {
            if (entity == null
                || !entity.Alive
                || entity.EntityId == self.EntityId
                || entity.Pos.Dimension != self.Pos.Dimension)
            {
                continue;
            }

            if (!Ability.AnimalAlertService.TryReadOverlay(
                    entity,
                    out int alert,
                    out int threat,
                    out bool awake,
                    out bool committed))
            {
                continue;
            }

            if (self.Pos.SquareDistanceTo(entity.Pos) > MaxDistance * MaxDistance)
            {
                continue;
            }

            DrawBar(shader, self, entity, alert, threat, awake, committed);
        }
    }

    void DrawBar(
        IShaderProgram shader,
        EntityPlayer viewer,
        Entity entity,
        int alert,
        int threat,
        bool awake,
        bool committed)
    {
        IRenderAPI render = capi.Render;
        Vec3d head = AboveHead(entity, viewer);
        Vec3d screen = MatrixToolsd.Project(
            head,
            render.PerspectiveProjectionMat,
            render.PerspectiveViewMat,
            render.FrameWidth,
            render.FrameHeight);
        if (screen.Z < 0.0)
        {
            return;
        }

        float scale = RuntimeEnv.GUIScale;
        float w = BarWidth * scale;
        float h = BarHeight * scale;
        float th = ThreatHeight * scale;
        float cx = (float)screen.X;
        float cy = render.FrameHeight - (float)screen.Y;

        Vec4f outline = new(1f, 1f, 1f, 0.85f);
        Vec4f fill = committed
            ? new Vec4f(0.95f, 0.2f, 0.15f, 0.9f)
            : awake
                ? new Vec4f(0.95f, 0.7f, 0.15f, 0.9f)
                : new Vec4f(0.25f, 0.85f, 0.95f, 0.85f);
        Vec4f threatFill = new(1f, 1f, 1f, 0.55f);

        DrawRect(shader, outline, cx - w / 2f, cy - h - th - 2f * scale, w, h, outline: true);
        float alertW = w * (alert / 100f);
        if (alertW > 0.5f)
        {
            DrawRect(shader, fill, cx - w / 2f, cy - h - th - 2f * scale, alertW, h, outline: false);
        }

        DrawRect(shader, outline, cx - w / 2f, cy - th, w, th, outline: true);
        float threatW = w * (threat / 100f);
        if (threatW > 0.5f)
        {
            DrawRect(shader, threatFill, cx - w / 2f, cy - th, threatW, th, outline: false);
        }
    }

    void DrawRect(
        IShaderProgram shader,
        Vec4f rgba,
        float x,
        float y,
        float width,
        float height,
        bool outline)
    {
        IRenderAPI render = capi.Render;
        shader.Uniform("rgbaIn", rgba);
        shader.Uniform("extraGlow", 0);
        shader.Uniform("applyColor", 0);
        shader.Uniform("tex2d", 0);
        shader.Uniform("noTexture", 1f);

        // ProgressBarRenderer style: translate to top-left, scale to size, center the unit mesh.
        mv.Set(render.CurrentModelviewMatrix)
            .Translate(x, y, 50f)
            .Scale(width, height, 0f)
            .Translate(0.5f, 0.5f, 0f)
            .Scale(0.5f, 0.5f, 0f);
        shader.UniformMatrix("projectionMatrix", render.CurrentProjectionMatrix);
        shader.UniformMatrix("modelViewMatrix", mv.Values);
        render.RenderMesh(outline ? outlineRef : fillRef);
    }

    static Vec3d AboveHead(Entity entity, EntityPlayer viewer)
    {
        if (entity.Properties?.Client?.Renderer is EntityShapeRenderer shape)
        {
            return shape.getAboveHeadPosition(viewer);
        }

        return new Vec3d(
            entity.Pos.X,
            entity.Pos.Y + entity.SelectionBox.Y2 + 0.35,
            entity.Pos.Z);
    }

    public void Dispose()
    {
        capi.Event.UnregisterRenderer(this, EnumRenderStage.Ortho);
        if (outlineRef != null)
        {
            capi.Render.DeleteMesh(outlineRef);
        }

        if (fillRef != null)
        {
            capi.Render.DeleteMesh(fillRef);
        }
    }
}
