using Cairo;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace Prosequor.Client;

/// <summary>
/// Bakes SVG icons as alpha masks with drop shadow + cool-grey diagonal gradient
/// (same recipe as skill-tree node icons). Tint is applied at blit, not bake.
/// </summary>
public sealed class StyledSvgIconCache : IDisposable
{
    public const double ShadowAlpha = 0.55;
    public const double ShadowOffsetFraction = 0.04;

    readonly ICoreClientAPI capi;
    readonly Dictionary<string, LoadedTexture> textures = new(StringComparer.OrdinalIgnoreCase);

    public StyledSvgIconCache(ICoreClientAPI capi)
    {
        this.capi = capi;
    }

    /// <summary>
    /// Returns a cached styled texture for <paramref name="loc"/> at <paramref name="px"/> pixels,
    /// or null if the asset (and optional fallback) cannot be loaded.
    /// </summary>
    public LoadedTexture? Get(AssetLocation loc, int px, AssetLocation? fallback = null)
    {
        px = Math.Max(8, px);
        string cacheKey = loc.ToString() + "@" + px;
        if (textures.TryGetValue(cacheKey, out LoadedTexture? existing))
        {
            return existing.TextureId > 0 ? existing : null;
        }

        IAsset? asset = capi.Assets.TryGet(loc);
        if (asset == null && fallback != null && fallback != loc)
        {
            capi.Logger.Warning("[prosequor] Missing styled SVG {0}; trying fallback.", loc);
            asset = capi.Assets.TryGet(fallback);
        }

        if (asset == null)
        {
            return null;
        }

        ImageSurface surface = new(Format.Argb32, px, px);
        Context ctx = new(surface);
        ImageSurface mask = new(Format.Argb32, px, px);
        try
        {
            int shadowOffset = Math.Max(1, (int)Math.Round(px * ShadowOffsetFraction));
            int iconSize = px - shadowOffset;

            capi.Gui.DrawSvg(asset, mask, 0, 0, iconSize, iconSize, ColorUtil.WhiteArgb);

            ctx.SetSourceRGBA(0, 0, 0, ShadowAlpha);
            ctx.MaskSurface(mask, shadowOffset, shadowOffset);

            using LinearGradient gradient = new(0, px, px, 0);
            gradient.AddColorStop(0, new Color(0.68, 0.72, 0.78, 1));
            gradient.AddColorStop(0.55, new Color(0.88, 0.91, 0.95, 1));
            gradient.AddColorStop(1, new Color(1, 1, 1, 1));
            ctx.SetSource(gradient);
            ctx.MaskSurface(mask, 0, 0);

            LoadedTexture texture = new(capi);
            capi.Gui.LoadOrUpdateCairoTexture(surface, linearMag: true, ref texture);
            textures[cacheKey] = texture;
            return texture;
        }
        finally
        {
            ctx.Dispose();
            surface.Dispose();
            mask.Dispose();
        }
    }

    public void Render(
        LoadedTexture texture,
        double x,
        double y,
        double size,
        double z,
        Vec4f? tint = null)
    {
        if (texture.TextureId <= 0)
        {
            return;
        }

        capi.Render.Render2DTexturePremultipliedAlpha(
            texture.TextureId, (float)x, (float)y, (float)size, (float)size, (float)z, tint);
    }

    public static Vec4f TintFromArgb(int argb)
    {
        float a = ((argb >> 24) & 0xff) / 255f;
        float r = ((argb >> 16) & 0xff) / 255f;
        float g = ((argb >> 8) & 0xff) / 255f;
        float b = (argb & 0xff) / 255f;
        if (a <= 0f)
        {
            a = 1f;
        }

        return new Vec4f(r, g, b, a);
    }

    public void Dispose()
    {
        foreach (LoadedTexture texture in textures.Values)
        {
            texture.Dispose();
        }

        textures.Clear();
    }
}
