using Vintagestory.API.Client;

namespace Prosequor.Client.CatEyes;

/// <summary>
/// In-memory GLSL for Perception cat eyes:
/// snapshot → mean-reduce → five-sample meter → adaptation → identity/lift response.
/// </summary>
public sealed class CatEyesShaders
{
    public const string LumaName = "prosequor-cateyes-luma";
    public const string ReduceName = "prosequor-cateyes-reduce";
    public const string FiveSampleName = "prosequor-cateyes-five-sample";
    public const string AdaptName = "prosequor-cateyes-adapt";
    public const string CopyName = "prosequor-cateyes-copy";
    public const string CopyColorName = "prosequor-cateyes-copy-color";
    public const string ApplyName = "prosequor-cateyes-apply";

    public IShaderProgram? Luma { get; private set; }
    public IShaderProgram? Reduce { get; private set; }
    public IShaderProgram? FiveSample { get; private set; }
    public IShaderProgram? Adapt { get; private set; }
    public IShaderProgram? Copy { get; private set; }
    public IShaderProgram? CopyColor { get; private set; }
    public IShaderProgram? Apply { get; private set; }

    readonly ICoreClientAPI capi;

    public CatEyesShaders(ICoreClientAPI capi)
    {
        this.capi = capi;
    }

    public bool Load()
    {
        Luma = Compile(LumaName, VertexCode, LumaFragmentCode);
        Reduce = Compile(ReduceName, VertexCode, ReduceFragmentCode);
        FiveSample = Compile(FiveSampleName, VertexCode, FiveSampleFragmentCode);
        Adapt = Compile(AdaptName, VertexCode, AdaptFragmentCode);
        Copy = Compile(CopyName, VertexCode, CopyFragmentCode);
        CopyColor = Compile(CopyColorName, VertexCode, CopyColorFragmentCode);
        Apply = Compile(ApplyName, VertexCode, ApplyFragmentCode);
        return Luma != null && Reduce != null && FiveSample != null && Adapt != null
            && Copy != null && CopyColor != null && Apply != null;
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

    public const string VertexCode = @"
#version 330 core
#extension GL_ARB_explicit_attrib_location: enable
layout(location = 0) in vec3 vertex;
out vec2 uv;
void main(void)
{
    gl_Position = vec4(vertex.xy, 0.0, 1.0);
    uv = (vertex.xy + 1.0) / 2.0;
}
";

    public const string LumaFragmentCode = @"
#version 330 core
uniform sampler2D primaryScene;
in vec2 uv;
out vec4 outColor;
void main()
{
    vec3 color = texture(primaryScene, uv).rgb;
    float luma = dot(color, vec3(0.212656, 0.715158, 0.072186));
    outColor = vec4(luma, luma, luma, 1.0);
}
";

    public const string ReduceFragmentCode = @"
#version 330 core
uniform sampler2D src;
uniform vec2 texelSize;
in vec2 uv;
out vec4 outColor;

void main()
{
    float sum = 0.0;
    sum += texture(src, uv + vec2(-1.5, -1.5) * texelSize).r;
    sum += texture(src, uv + vec2(-0.5, -1.5) * texelSize).r;
    sum += texture(src, uv + vec2( 0.5, -1.5) * texelSize).r;
    sum += texture(src, uv + vec2( 1.5, -1.5) * texelSize).r;
    sum += texture(src, uv + vec2(-1.5, -0.5) * texelSize).r;
    sum += texture(src, uv + vec2(-0.5, -0.5) * texelSize).r;
    sum += texture(src, uv + vec2( 0.5, -0.5) * texelSize).r;
    sum += texture(src, uv + vec2( 1.5, -0.5) * texelSize).r;
    sum += texture(src, uv + vec2(-1.5,  0.5) * texelSize).r;
    sum += texture(src, uv + vec2(-0.5,  0.5) * texelSize).r;
    sum += texture(src, uv + vec2( 0.5,  0.5) * texelSize).r;
    sum += texture(src, uv + vec2( 1.5,  0.5) * texelSize).r;
    sum += texture(src, uv + vec2(-1.5,  1.5) * texelSize).r;
    sum += texture(src, uv + vec2(-0.5,  1.5) * texelSize).r;
    sum += texture(src, uv + vec2( 0.5,  1.5) * texelSize).r;
    sum += texture(src, uv + vec2( 1.5,  1.5) * texelSize).r;
    float luma = sum * (1.0 / 16.0);
    outColor = vec4(luma, luma, luma, 1.0);
}
";

    /// <summary>
    /// Soft center-biased meter from 8×8 luma: 4 corners + center → 1×1.
    /// </summary>
    public const string FiveSampleFragmentCode = @"
#version 330 core
uniform sampler2D src;
uniform float centerBias;
in vec2 uv;
out vec4 outColor;

void main()
{
    float halfTexel = 0.5 / 8.0;
    float c00 = texture(src, vec2(halfTexel,       halfTexel)).r;
    float c10 = texture(src, vec2(1.0 - halfTexel, halfTexel)).r;
    float c01 = texture(src, vec2(halfTexel,       1.0 - halfTexel)).r;
    float c11 = texture(src, vec2(1.0 - halfTexel, 1.0 - halfTexel)).r;
    float center = texture(src, vec2(0.5, 0.5)).r;
    float cornerMean = (c00 + c10 + c01 + c11) * 0.25;
    float meter = mix(cornerMean, center, clamp(centerBias, 0.0, 1.0));
    outColor = vec4(meter, meter, meter, 1.0);
}
";

    /// <summary>
    /// 1×1: target from meter brightness, then move adaptation toward target
    /// (slow up into dark, fast down into light).
    /// </summary>
    public const string AdaptFragmentCode = @"
#version 330 core
uniform sampler2D sceneMeter;
uniform sampler2D prevAdapt;
uniform float equilibrium;
uniform float alphaUp;
uniform float alphaDown;
in vec2 uv;
out vec4 outColor;
void main()
{
    float meter = texture(sceneMeter, vec2(0.5, 0.5)).r;
    float adapt = texture(prevAdapt, vec2(0.5, 0.5)).r;
    float target = clamp((equilibrium - meter) / max(equilibrium, 1e-4), 0.0, 1.0);
    float alpha = target > adapt ? clamp(alphaUp, 0.0, 1.0) : clamp(alphaDown, 0.0, 1.0);
    adapt = clamp(adapt + (target - adapt) * alpha, 0.0, 1.0);
    outColor = vec4(adapt, adapt, adapt, 1.0);
}
";

    public const string CopyFragmentCode = @"
#version 330 core
uniform sampler2D src;
in vec2 uv;
out vec4 outColor;
void main()
{
    float v = texture(src, uv).r;
    outColor = vec4(v, v, v, 1.0);
}
";

    public const string CopyColorFragmentCode = @"
#version 330 core
uniform sampler2D src;
in vec2 uv;
out vec4 outColor;
void main()
{
    outColor = texture(src, uv);
}
";

    /// <summary>
    /// Always-on response: lerp(identity, identity+lift, adaptation × capacity).
    /// When t == 0, raw sample — true identity, no gamma round-trip.
    /// </summary>
    public const string ApplyFragmentCode = @"
#version 330 core
uniform sampler2D primaryScene;
uniform sampler2D adaptTex;
uniform float capacity;
uniform float brightenShadows;
uniform float brightenMidtones;
uniform float brightenHighlights;
uniform float desatAmount;
in vec2 uv;
out vec4 outColor;

const vec3 LumCoeff = vec3(0.212656, 0.715158, 0.072186);

float AdaptionDelta(float luma, float mid, float shadows, float highlights)
{
    float midtones = (4.0 * mid - highlights - shadows) * luma * (1.0 - luma);
    return midtones + shadows * (1.0 - luma) + highlights * luma;
}

void main()
{
    vec4 color = texture(primaryScene, uv);
    float adaptation = texture(adaptTex, vec2(0.5, 0.5)).r;
    float t = clamp(adaptation, 0.0, 1.0) * clamp(capacity, 0.0, 1.0);

    if (t <= 0.0)
    {
        outColor = color;
        return;
    }

    color.rgb = pow(abs(color.rgb), vec3(1.0 / 2.2));
    float luma = dot(color.rgb, LumCoeff);
    vec3 chroma = color.rgb - vec3(luma);

    float dark = clamp(luma + AdaptionDelta(luma, brightenMidtones, brightenShadows, brightenHighlights), 0.0, 1.0);
    luma = mix(luma, dark, t);

    float desat = desatAmount * t;
    chroma *= (1.0 - clamp(desat, 0.0, 1.0));
    color.rgb = clamp(vec3(luma) + chroma, 0.0, 1.0);
    color.rgb = pow(abs(color.rgb), vec3(2.2));
    outColor = vec4(color.rgb, 1.0);
}
";
}
