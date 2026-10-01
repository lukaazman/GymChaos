using UnityEditor;
using UnityEngine;

/// <summary>
/// Class illustrations are UI-only: straight-alpha cut-outs shown at most
/// about 640x800 on screen, so they import at 1024 without mip maps.
/// </summary>
public sealed class GymClassArtImporter : AssetPostprocessor
{
    private void OnPreprocessTexture()
    {
        if (!assetPath.Contains("Resources/Classes/Art/"))
        {
            return;
        }

        TextureImporter importer = (TextureImporter)assetImporter;
        importer.textureType = TextureImporterType.Default;
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = false;
        importer.npotScale = TextureImporterNPOTScale.None;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.filterMode = FilterMode.Bilinear;
        importer.maxTextureSize = 1024;
        importer.textureCompression = TextureImporterCompression.CompressedHQ;
        importer.isReadable = false;
    }
}
