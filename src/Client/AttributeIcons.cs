using Cairo;
using Prosequor.Data;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace Prosequor.Client;

/// <summary>Shared SVG attribute icons for Stats panel and class selection.</summary>
public sealed class AttributeIcons : IDisposable
{
    readonly ICoreClientAPI capi;
    readonly Dictionary<string, LoadedTexture> textures = new(StringComparer.OrdinalIgnoreCase);

    public AttributeIcons(ICoreClientAPI capi)
    {
        this.capi = capi;
    }

    public LoadedTexture? Get(string attrId, double size)
    {
        AssetLocation? loc = ResolveLocation(attrId);
        if (loc == null)
        {
            return null;
        }

        string cacheKey = loc + "@" + (int)size;
        if (textures.TryGetValue(cacheKey, out LoadedTexture? existing))
        {
            return existing.TextureId > 0 ? existing : null;
        }

        IAsset? asset = capi.Assets.TryGet(loc);
        if (asset == null)
        {
            capi.Logger.Warning("[prosequor] Missing attribute icon {0}.", loc);
            textures[cacheKey] = new LoadedTexture(capi);
            return null;
        }

        int px = Math.Max(8, (int)size);
        ImageSurface surface = new(Format.Argb32, px, px);
        Context ctx = new(surface);
        try
        {
            capi.Gui.DrawSvg(asset, surface, 0, 0, px, px, ColorUtil.WhiteArgb);
            LoadedTexture texture = new(capi);
            capi.Gui.LoadOrUpdateCairoTexture(surface, linearMag: true, ref texture);
            textures[cacheKey] = texture;
            return texture;
        }
        finally
        {
            ctx.Dispose();
            surface.Dispose();
        }
    }

    AssetLocation? ResolveLocation(string attrId)
    {
        if (string.IsNullOrWhiteSpace(attrId))
        {
            return null;
        }

        IAttributeStatRegistry? stats = ProsequorModSystem.For(capi)?.AttributeStats;
        if (stats != null && stats.TryGet(attrId, out AttributeStatDef def))
        {
            return AttributeStatRegistry.IconLocation(def);
        }

        return new AssetLocation("prosequor", AttributeStatRegistry.DefaultIcon(attrId.Trim()));
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
