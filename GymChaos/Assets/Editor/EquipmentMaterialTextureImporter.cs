using UnityEditor;

public sealed class EquipmentMaterialTextureImporter : AssetPostprocessor
{
    private const string EquipmentMaterialPath = "/Resources/EquipmentMaterials/";

    private void OnPreprocessTexture()
    {
        string normalizedPath = assetPath.Replace('\\', '/');
        if (!normalizedPath.Contains(EquipmentMaterialPath))
        {
            return;
        }

        TextureImporter importer = (TextureImporter)assetImporter;
        importer.maxTextureSize = 1024;
        importer.mipmapEnabled = true;
        importer.wrapMode = UnityEngine.TextureWrapMode.Repeat;
        importer.textureCompression = TextureImporterCompression.CompressedHQ;

        if (normalizedPath.EndsWith("/normal.png"))
        {
            importer.textureType = TextureImporterType.NormalMap;
            importer.sRGBTexture = false;
        }
        else if (normalizedPath.EndsWith("/metallic-smoothness.png"))
        {
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = false;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
        }
        else
        {
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = true;
        }
    }
}
