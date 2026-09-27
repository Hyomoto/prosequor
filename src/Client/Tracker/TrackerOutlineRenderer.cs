using HarmonyLib;
using Prosequor.Ability;
using Prosequor.Xp;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.Client;
using Vintagestory.Client.NoObf;
using Vintagestory.GameContent;

namespace Prosequor.Client.Tracker;

/// <summary>
/// Marks one animal near the cursor, then draws its skinned silhouette through the world.
/// The mask is filled at <see cref="EnumRenderStage.AfterOIT"/> and the edge is blended at
/// <see cref="EnumRenderStage.AfterFinalComposition"/>, before the HUD.
/// </summary>
public sealed class TrackerOutlineRenderer : IRenderer, IDisposable
{
    const int JointCapacity = 64;
    const int FloatsPerJoint = 16;
    const string SkillId = "hunting";
    const string NodeId = "tracker";

    readonly ICoreClientAPI capi;
    readonly TrackMark mark = new();
    readonly float[] jointUpload = new float[JointCapacity * FloatsPerJoint];
    readonly MeshRef quadRef;
    readonly List<Sighting> sightings = new();
    TrackAim.AimSample[] aimSamples = [];

    public long? MarkedId => mark.MarkedId;

    IShaderProgram? maskShader;
    IShaderProgram? edgeShader;
    UBORef? joints;
    FrameBufferRef? maskFb;
    bool maskDrawn;
    bool disposed;

    public double RenderOrder => 1.0;

    public int RenderRange => 1;

    public TrackerOutlineRenderer(ICoreClientAPI capi)
    {
        this.capi = capi;
        MeshData quad = QuadMeshUtil.GetCustomQuadModelData(-1f, -1f, 0f, 2f, 2f);
        quad.Rgba = null!;
        quadRef = capi.Render.UploadMesh(quad);
        capi.Event.ReloadShader += LoadShaders;
        LoadShaders();
        capi.Event.RegisterRenderer(this, EnumRenderStage.AfterOIT, "prosequor-tracker-mask");
        capi.Event.RegisterRenderer(this, EnumRenderStage.AfterFinalComposition, "prosequor-tracker-edge");
    }

    public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
    {
        if (disposed)
        {
            return;
        }

        if (stage == EnumRenderStage.AfterOIT)
        {
            TickMark(deltaTime);
            maskDrawn = TryDrawMask(deltaTime);
            return;
        }

        if (stage == EnumRenderStage.AfterFinalComposition && maskDrawn)
        {
            DrawEdge();
        }
    }

    void TickMark(float deltaTime)
    {
        IClientWorldAccessor? world = capi.World;
        IPlayer? player = world?.Player;
        EntityPlayer? self = player?.Entity;
        if (world == null || player == null || self == null)
        {
            mark.Clear();
            return;
        }

        IPlayerProgress? progress = ProsequorModSystem.GetProgress(player);
        bool hasUnlock = (progress?.GetUnlockTier(SkillId, NodeId) ?? 0) > 0;
        float range = PlayerInteractionStation.ResolveTrackRange(player);
        float focus = PlayerInteractionStation.ResolveTrackFocusSeconds(player);
        float loseAngle = PlayerInteractionStation.ResolveTrackLoseAngle(player);

        Vec3d origin = self.CameraPos;
        Vec3f rawLook = self.Pos.GetViewVector();
        float lookLen = MathF.Sqrt((rawLook.X * rawLook.X) + (rawLook.Y * rawLook.Y) + (rawLook.Z * rawLook.Z));
        Vec3f look = lookLen > 0.0001f
            ? new Vec3f(rawLook.X / lookLen, rawLook.Y / lookLen, rawLook.Z / lookLen)
            : rawLook;
        long? aimed = hasUnlock && range > 0f
            ? FindAimedLiving(self, origin, look, range, mark.CandidateId)
            : null;

        bool present = false;
        float distance = 0f;
        float angle = 0f;
        if (mark.MarkedId is long markedId)
        {
            Entity? marked = world.GetEntityById(markedId);
            if (marked != null && marked.Pos.Dimension == self.Pos.Dimension)
            {
                present = true;
                Measure(marked, origin, look, out distance, out angle);
            }
        }

        mark.Tick(
            deltaTime,
            hasUnlock,
            aimed,
            present,
            distance,
            angle,
            range,
            focus,
            loseAngle);
    }

    bool TryDrawMask(float deltaTime)
    {
        if (mark.MarkedId is not long markedId || maskShader == null || joints == null)
        {
            return false;
        }

        Entity? entity = capi.World.GetEntityById(markedId);
        if (entity?.Properties?.Client?.Renderer is not EntityShapeRenderer shape)
        {
            return false;
        }

        MultiTextureMeshRef? meshes = Traverse.Create(shape)
            .Field<MultiTextureMeshRef>("meshRefOpaque")
            .Value;
        if (meshes == null || meshes.Disposed || meshes.meshrefs == null || meshes.meshrefs.Length == 0)
        {
            return false;
        }

        FrameBufferRef? primary = capi.Render.FrameBuffers?[(int)EnumFrameBuffer.Primary];
        if (primary == null || primary.Width <= 0 || primary.Height <= 0)
        {
            return false;
        }

        if (!EnsureMask(primary.Width, primary.Height) || maskFb == null)
        {
            return false;
        }

        shape.loadModelMatrix(entity, deltaTime, isShadowPass: false);
        UploadJoints(entity);

        FrameBufferRef? previousFb = capi.Render.CurrentFrameBuffer;
        IShaderProgram? previous = capi.Render.CurrentActiveShader;
        previous?.Stop();

        capi.Render.CurrentFrameBuffer = maskFb;
        capi.Render.ClearFrameBuffer(maskFb, [0f, 0f, 0f, 0f], clearDepthBuffer: false, clearColorBuffers: true);
        capi.Render.GLDisableDepthTest();
        capi.Render.GlDisableCullFace();
        capi.Render.GlToggleBlend(false);

        maskShader.Use();
        maskShader.UniformMatrix("projectionMatrix", capi.Render.CurrentProjectionMatrix);
        maskShader.UniformMatrix("viewMatrix", capi.Render.CameraMatrixOriginf);
        maskShader.UniformMatrix("modelMatrix", shape.ModelMat);
        joints.Update(jointUpload, 0, jointUpload.Length * sizeof(float));
        joints.Bind();

        foreach (MeshRef mesh in meshes.meshrefs)
        {
            if (mesh != null && mesh.Initialized && !mesh.Disposed)
            {
                capi.Render.RenderMesh(mesh);
            }
        }

        joints.Unbind();
        maskShader.Stop();
        capi.Render.GlEnableCullFace();
        capi.Render.GLEnableDepthTest();
        capi.Render.GlToggleBlend(true, EnumBlendMode.Standard);
        capi.Render.CurrentFrameBuffer = previousFb;
        previous?.Use();
        return true;
    }

    void DrawEdge()
    {
        if (edgeShader == null || maskFb?.ColorTextureIds == null || maskFb.ColorTextureIds.Length == 0)
        {
            return;
        }

        FrameBufferRef? primary = capi.Render.FrameBuffers?[(int)EnumFrameBuffer.Primary];
        if (primary == null || ScreenManager.Platform == null)
        {
            return;
        }

        IShaderProgram? previous = capi.Render.CurrentActiveShader;
        previous?.Stop();
        capi.Render.GLDisableDepthTest();
        capi.Render.GlToggleBlend(true, EnumBlendMode.Standard);

        ScreenManager.Platform.CurrentFrameBuffer = primary;
        edgeShader.Use();
        edgeShader.BindTexture2D("mask", maskFb.ColorTextureIds[0], 0);
        edgeShader.Uniform("texelSize", new Vec2f(1f / maskFb.Width, 1f / maskFb.Height));
        capi.Render.RenderMesh(quadRef);
        edgeShader.Stop();

        capi.Render.GLEnableDepthTest();
        previous?.Use();
    }

    void UploadJoints(Entity entity)
    {
        FillIdentity(jointUpload);
        if (entity.AnimManager?.Animator is AnimatorBase animator
            && animator.TransformationMatrices is float[] matrices
            && matrices.Length > 0)
        {
            int count = Math.Min(matrices.Length, jointUpload.Length);
            Array.Copy(matrices, jointUpload, count);
        }
    }

    static void FillIdentity(float[] mats)
    {
        Array.Clear(mats);
        for (int joint = 0; joint < JointCapacity; joint++)
        {
            int o = joint * FloatsPerJoint;
            mats[o] = 1f;
            mats[o + 5] = 1f;
            mats[o + 10] = 1f;
            mats[o + 15] = 1f;
        }
    }

    long? FindAimedLiving(EntityPlayer self, Vec3d origin, Vec3f look, float searchRange, long? heldId)
    {
        sightings.Clear();
        foreach (Entity entity in capi.World.LoadedEntities.Values)
        {
            if (entity == null
                || entity.EntityId == self.EntityId
                || !entity.Alive
                || entity.Pos.Dimension != self.Pos.Dimension
                || !AnimalWeightCatalog.IsAnimal(entity))
            {
                continue;
            }

            if (TrySight(entity, origin, look, searchRange, out Sighting sight))
            {
                sightings.Add(sight);
            }
        }

        if (sightings.Count == 0)
        {
            return null;
        }

        int heldSight = -1;
        if (heldId is long held)
        {
            for (int i = 0; i < sightings.Count; i++)
            {
                if (sightings[i].Id == held)
                {
                    heldSight = i;
                    break;
                }
            }
        }

        if (aimSamples.Length < sightings.Count)
        {
            aimSamples = new TrackAim.AimSample[Math.Max(sightings.Count, 16)];
        }

        for (int i = 0; i < sightings.Count; i++)
        {
            Sighting sight = sightings[i];
            float separation = heldSight >= 0 && i != heldSight
                ? SeparationDegrees(origin, sight, sightings[heldSight])
                : 0f;
            aimSamples[i] = new TrackAim.AimSample(sight.Id, sight.Angle, separation);
        }

        return TrackAim.Select(heldId, aimSamples.AsSpan(0, sightings.Count));
    }

    static bool TrySight(
        Entity entity,
        Vec3d origin,
        Vec3f look,
        float searchRange,
        out Sighting sight)
    {
        sight = default;
        if (!WorldBox(entity, out double x1, out double y1, out double z1, out double x2, out double y2, out double z2))
        {
            return false;
        }

        double nearX = Math.Clamp(origin.X, x1, x2);
        double nearY = Math.Clamp(origin.Y, y1, y2);
        double nearZ = Math.Clamp(origin.Z, z1, z2);
        double dx = nearX - origin.X;
        double dy = nearY - origin.Y;
        double dz = nearZ - origin.Z;
        if (Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz)) > searchRange)
        {
            return false;
        }

        double cx = (x1 + x2) * 0.5;
        double cy = (y1 + y2) * 0.5;
        double cz = (z1 + z2) * 0.5;
        float angle = 0f;
        bool onBody = RayAabb(
            origin.X, origin.Y, origin.Z,
            look.X, look.Y, look.Z,
            x1, y1, z1, x2, y2, z2,
            searchRange,
            out _);
        if (!onBody)
        {
            double vx = cx - origin.X;
            double vy = cy - origin.Y;
            double vz = cz - origin.Z;
            double len = Math.Sqrt((vx * vx) + (vy * vy) + (vz * vz));
            if (len < 0.0001)
            {
                angle = 0f;
            }
            else
            {
                double dot = ((look.X * vx) + (look.Y * vy) + (look.Z * vz)) / len;
                dot = Math.Clamp(dot, -1.0, 1.0);
                angle = (float)(Math.Acos(dot) * (180.0 / Math.PI));
            }
        }

        if (angle > TrackAim.ConeDegrees)
        {
            return false;
        }

        sight = new Sighting(entity.EntityId, angle, cx, cy, cz);
        return true;
    }

    static float SeparationDegrees(Vec3d origin, Sighting a, Sighting b)
    {
        double ax = a.X - origin.X;
        double ay = a.Y - origin.Y;
        double az = a.Z - origin.Z;
        double bx = b.X - origin.X;
        double by = b.Y - origin.Y;
        double bz = b.Z - origin.Z;
        double lenA = Math.Sqrt((ax * ax) + (ay * ay) + (az * az));
        double lenB = Math.Sqrt((bx * bx) + (by * by) + (bz * bz));
        if (lenA < 0.0001 || lenB < 0.0001)
        {
            return 0f;
        }

        double dot = ((ax * bx) + (ay * by) + (az * bz)) / (lenA * lenB);
        dot = Math.Clamp(dot, -1.0, 1.0);
        return (float)(Math.Acos(dot) * (180.0 / Math.PI));
    }

    readonly record struct Sighting(long Id, float Angle, double X, double Y, double Z);

    static void Measure(Entity entity, Vec3d origin, Vec3f look, out float distance, out float angleDegrees)
    {
        if (!WorldBox(entity, out double x1, out double y1, out double z1, out double x2, out double y2, out double z2))
        {
            distance = float.MaxValue;
            angleDegrees = 180f;
            return;
        }

        double cx = (x1 + x2) * 0.5;
        double cy = (y1 + y2) * 0.5;
        double cz = (z1 + z2) * 0.5;
        double vx = cx - origin.X;
        double vy = cy - origin.Y;
        double vz = cz - origin.Z;
        double len = Math.Sqrt((vx * vx) + (vy * vy) + (vz * vz));
        double nearX = Math.Clamp(origin.X, x1, x2);
        double nearY = Math.Clamp(origin.Y, y1, y2);
        double nearZ = Math.Clamp(origin.Z, z1, z2);
        double dx = nearX - origin.X;
        double dy = nearY - origin.Y;
        double dz = nearZ - origin.Z;
        distance = (float)Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
        if (len < 0.0001)
        {
            angleDegrees = 0f;
            return;
        }

        double dot = ((look.X * vx) + (look.Y * vy) + (look.Z * vz)) / len;
        dot = Math.Clamp(dot, -1.0, 1.0);
        angleDegrees = (float)(Math.Acos(dot) * (180.0 / Math.PI));
    }

    static bool WorldBox(
        Entity entity,
        out double x1,
        out double y1,
        out double z1,
        out double x2,
        out double y2,
        out double z2)
    {
        Cuboidf? box = entity.SelectionBox ?? entity.CollisionBox;
        if (box == null)
        {
            x1 = y1 = z1 = x2 = y2 = z2 = 0;
            return false;
        }

        x1 = entity.Pos.X + box.X1;
        y1 = entity.Pos.Y + box.Y1;
        z1 = entity.Pos.Z + box.Z1;
        x2 = entity.Pos.X + box.X2;
        y2 = entity.Pos.Y + box.Y2;
        z2 = entity.Pos.Z + box.Z2;
        if (x1 > x2)
        {
            (x1, x2) = (x2, x1);
        }

        if (y1 > y2)
        {
            (y1, y2) = (y2, y1);
        }

        if (z1 > z2)
        {
            (z1, z2) = (z2, z1);
        }

        return true;
    }

    static bool RayAabb(
        double ox, double oy, double oz,
        double dx, double dy, double dz,
        double x1, double y1, double z1,
        double x2, double y2, double z2,
        double maxDistance,
        out double distance)
    {
        double tMin = 0.0;
        double tMax = maxDistance;
        if (!Slab(ox, dx, x1, x2, ref tMin, ref tMax)
            || !Slab(oy, dy, y1, y2, ref tMin, ref tMax)
            || !Slab(oz, dz, z1, z2, ref tMin, ref tMax))
        {
            distance = 0.0;
            return false;
        }

        distance = tMin;
        return true;
    }

    static bool Slab(double origin, double direction, double min, double max, ref double tMin, ref double tMax)
    {
        if (Math.Abs(direction) < 1e-8)
        {
            return origin >= min && origin <= max;
        }

        double inv = 1.0 / direction;
        double t1 = (min - origin) * inv;
        double t2 = (max - origin) * inv;
        if (t1 > t2)
        {
            (t1, t2) = (t2, t1);
        }

        tMin = Math.Max(tMin, t1);
        tMax = Math.Min(tMax, t2);
        return tMin <= tMax;
    }

    bool LoadShaders()
    {
        maskShader = Compile("prosequor-tracker-mask", MaskVertex, MaskFragment);
        edgeShader = Compile("prosequor-tracker-edge", EdgeVertex, EdgeFragment);
        joints?.Dispose();
        joints = null;
        if (maskShader == null)
        {
            return false;
        }

        joints = capi.Render.CreateUBO(maskShader, 0, "Animation", jointUpload.Length * sizeof(float));
        return edgeShader != null && joints != null;
    }

    IShaderProgram? Compile(string name, string vertex, string fragment)
    {
        IShaderProgram prog = capi.Shader.NewShaderProgram();
        prog.VertexShader = capi.Shader.NewShader(EnumShaderType.VertexShader);
        prog.FragmentShader = capi.Shader.NewShader(EnumShaderType.FragmentShader);
        prog.VertexShader.Code = vertex;
        prog.FragmentShader.Code = fragment;
        capi.Shader.RegisterMemoryShaderProgram(name, prog);
        if (!prog.Compile())
        {
            capi.Logger.Error("[{0}] Failed to compile shader '{1}'.", ProsequorModSystem.ModId, name);
            return null;
        }

        return prog;
    }

    bool EnsureMask(int width, int height)
    {
        if (maskFb != null && maskFb.Width == width && maskFb.Height == height)
        {
            return true;
        }

        DestroyMask();
        if (ScreenManager.Platform == null)
        {
            return false;
        }

        var attrs = new FramebufferAttrs("prosequor-tracker-mask", width, height)
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
                        MinFilter = EnumTextureFilter.Nearest,
                        MagFilter = EnumTextureFilter.Nearest,
                        WrapS = EnumTextureWrap.ClampToEdge,
                        WrapT = EnumTextureWrap.ClampToEdge
                    }
                }
            ]
        };
        maskFb = ScreenManager.Platform.CreateFramebuffer(attrs);
        return maskFb != null;
    }

    void DestroyMask()
    {
        if (maskFb == null)
        {
            return;
        }

        ScreenManager.Platform?.DisposeFrameBuffer(maskFb, disposeTextures: true);
        maskFb = null;
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        capi.Event.ReloadShader -= LoadShaders;
        capi.Event.UnregisterRenderer(this, EnumRenderStage.AfterOIT);
        capi.Event.UnregisterRenderer(this, EnumRenderStage.AfterFinalComposition);
        joints?.Dispose();
        joints = null;
        capi.Render.DeleteMesh(quadRef);
        DestroyMask();
    }

    const string MaskVertex = @"
#version 330 core
#extension GL_ARB_explicit_attrib_location: enable
layout(location = 0) in vec3 vertexPositionIn;
layout(location = 5) in int jointId;
layout(std140) uniform Animation {
    mat4 values[64];
} ElementTransforms;
uniform mat4 projectionMatrix;
uniform mat4 viewMatrix;
uniform mat4 modelMatrix;
void main()
{
    int id = clamp(jointId, 0, 63);
    vec4 worldPos = modelMatrix * ElementTransforms.values[id] * vec4(vertexPositionIn, 1.0);
    gl_Position = projectionMatrix * viewMatrix * worldPos;
}
";

    const string MaskFragment = @"
#version 330 core
out vec4 outColor;
void main()
{
    outColor = vec4(1.0, 1.0, 1.0, 1.0);
}
";

    const string EdgeVertex = @"
#version 330 core
#extension GL_ARB_explicit_attrib_location: enable
layout(location = 0) in vec3 vertex;
out vec2 uv;
void main()
{
    gl_Position = vec4(vertex.xy, 0.0, 1.0);
    uv = (vertex.xy + 1.0) / 2.0;
}
";

    const string EdgeFragment = @"
#version 330 core
uniform sampler2D mask;
uniform vec2 texelSize;
in vec2 uv;
out vec4 outColor;
void main()
{
    float center = texture(mask, uv).r;
    float around = 0.0;
    around = max(around, texture(mask, uv + vec2(texelSize.x, 0.0)).r);
    around = max(around, texture(mask, uv - vec2(texelSize.x, 0.0)).r);
    around = max(around, texture(mask, uv + vec2(0.0, texelSize.y)).r);
    around = max(around, texture(mask, uv - vec2(0.0, texelSize.y)).r);
    float edge = abs(step(0.5, center) - step(0.5, around));
    if (edge < 0.5)
    {
        discard;
    }
    outColor = vec4(1.0, 0.78, 0.35, 0.95);
}
";
}
