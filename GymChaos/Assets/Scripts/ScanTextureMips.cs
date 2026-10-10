using System.Collections.Generic;
using UnityEngine;

/// Scan textures (characters, scanned vehicles, wearables) pack thousands of
/// tiny UV islands next to islands of unrelated colour. Every lower mip level
/// averages neighbouring islands, so a black shirt or navy suit picks up skin
/// and grey specks at normal viewing distance (worst from mip 2 down, still
/// visible on flat cloth at mip 1). This keeps only mip 0 of such a texture,
/// the same result as the WebGL path, which has no CopyTexture and uses a
/// mip-less copy.
public static class ScanTextureMips
{
    public const int DefaultLevels = 1;
    // Verifiers set this to exercise the no-CopyTexture (WebGL/GLES) path.
    public static bool ForceFlatCopy;
    private static bool loggedMode;
    private static readonly Dictionary<Texture, Texture2D> Limited = new Dictionary<Texture, Texture2D>();

    /// `ownsSource`: the caller created the texture at runtime and no longer
    /// needs the full chain, so it is destroyed instead of cached.
    public static Texture Limit(Texture texture, int levels = DefaultLevels, bool ownsSource = false)
    {
        Texture2D source = texture as Texture2D;
        if (source == null || source.mipmapCount <= levels)
        {
            return texture;
        }
        bool copy = !ForceFlatCopy &&
            (SystemInfo.copyTextureSupport & UnityEngine.Rendering.CopyTextureSupport.Basic) != 0;
        if (!loggedMode)
        {
            loggedMode = true;
            Debug.Log($"GYMCHAOS_SCAN_TEXTURE_MIPS mode={(copy ? "copy" : "flat")} platform={Application.platform}");
        }
        if (!copy)
        {
            return WithoutMips(source);
        }
        if (!ownsSource && Limited.TryGetValue(source, out Texture2D cached) && cached != null)
        {
            return cached;
        }

        Texture2D result = new Texture2D(source.width, source.height, source.format, levels, !source.isDataSRGB)
        {
            // Same name: verifiers and logs identify the texture by it.
            name = source.name,
            filterMode = source.filterMode,
            wrapMode = source.wrapMode,
            anisoLevel = Mathf.Max(source.anisoLevel, 4)
        };
        for (int mip = 0; mip < levels; mip++)
        {
            Graphics.CopyTexture(source, 0, mip, result, 0, mip);
        }
        if (ownsSource)
        {
            if (Application.isPlaying)
            {
                Object.Destroy(source);
            }
            else
            {
                Object.DestroyImmediate(source);
            }
        }
        else
        {
            Limited[source] = result;
        }
        return result;
    }

    // Platforms without CopyTexture (some WebGL/GLES devices): blit mip 0 into
    // a render texture without mips. It aliases a little at distance but never
    // mixes islands.
    private static Texture WithoutMips(Texture2D source)
    {
        if (Limited.TryGetValue(source, out Texture2D cached) && cached != null)
        {
            return cached;
        }
        if (FlatCopies.TryGetValue(source, out RenderTexture flat) && flat != null && flat.IsCreated())
        {
            return flat;
        }
        flat = new RenderTexture(source.width, source.height, 0, RenderTextureFormat.ARGB32,
            source.isDataSRGB ? RenderTextureReadWrite.sRGB : RenderTextureReadWrite.Linear)
        {
            name = source.name,
            useMipMap = false,
            filterMode = source.filterMode,
            wrapMode = source.wrapMode,
            anisoLevel = Mathf.Max(source.anisoLevel, 4)
        };
        Graphics.Blit(source, flat);
        FlatCopies[source] = flat;
        return flat;
    }

    private static readonly Dictionary<Texture, RenderTexture> FlatCopies = new Dictionary<Texture, RenderTexture>();
}
