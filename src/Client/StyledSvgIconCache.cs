using Cairo;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace Prosequor.Client;

/// <summary>
/// Bakes SVG icons as alpha masks with drop shadow + cool-grey diagonal gradient
/// (same recipe as skill-tree node icons). Tint is applied at blit / Cairo paint, not bake.
/// </summary>
public sealed class StyledSvgIconCache : IDisposable
{
    public const double ShadowAlpha = 0.55;
    public const double ShadowOffsetFraction = 0.04;

    readonly ICoreClientAPI capi;
    readonly Dictionary<string, LoadedTexture> textures = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, ImageSurface> surfaces = new(StringComparer.OrdinalIgnoreCase);

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

        ImageSurface? surface = GetOrBakeSurface(loc, px, fallback);
        if (surface == null)
        {
            return null;
        }

        LoadedTexture texture = new(capi);
        capi.Gui.LoadOrUpdateCairoTexture(surface, linearMag: true, ref texture);
        textures[cacheKey] = texture;
        return texture;
    }

    /// <summary>
    /// Paint a styled SVG onto a Cairo context at the given pixel rect.
    /// Optional ARGB tint multiplies the baked grey gradient (same idea as GPU blit tint).
    /// </summary>
    public bool TryPaint(
        Context ctx,
        AssetLocation loc,
        double x,
        double y,
        double size,
        int? tintArgb = null,
        AssetLocation? fallback = null)
    {
        int px = Math.Max(8, (int)Math.Ceiling(size));
        ImageSurface? surface = GetOrBakeSurface(loc, px, fallback);
        if (surface == null)
        {
            return false;
        }

        ctx.Save();
        try
        {
            double scale = size / px;
            ctx.Translate(x, y);
            if (Math.Abs(scale - 1.0) > 0.001)
            {
                ctx.Scale(scale, scale);
            }

            ctx.SetSourceSurface(surface, 0, 0);
            ctx.Paint();

            if (tintArgb is int argb)
            {
                Vec4f tint = TintFromArgb(argb);
                ctx.Operator = Operator.Multiply;
                ctx.SetSourceRGBA(tint.R, tint.G, tint.B, tint.A);
                ctx.Rectangle(0, 0, px, px);
                ctx.Fill();
                ctx.Operator = Operator.Over;
            }
        }
        finally
        {
            ctx.Restore();
        }

        return true;
    }

    ImageSurface? GetOrBakeSurface(AssetLocation loc, int px, AssetLocation? fallback)
    {
        string cacheKey = loc.ToString() + "@" + px;
        if (surfaces.TryGetValue(cacheKey, out ImageSurface? existing))
        {
            return existing;
        }

        ImageSurface? baked = BakeSurface(loc, px, fallback);
        if (baked != null)
        {
            surfaces[cacheKey] = baked;
        }

        return baked;
    }

    ImageSurface? BakeSurface(AssetLocation loc, int px, AssetLocation? fallback)
    {
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

            return surface;
        }
        catch
        {
            surface.Dispose();
            throw;
        }
        finally
        {
            ctx.Dispose();
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

        foreach (ImageSurface surface in surfaces.Values)
        {
            surface.Dispose();
        }

        surfaces.Clear();
    }
}
