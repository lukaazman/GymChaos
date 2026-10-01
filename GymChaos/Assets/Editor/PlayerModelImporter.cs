using UnityEditor;

public sealed class PlayerModelImporter : AssetPostprocessor
{
    private void OnPreprocessModel()
    {
        bool baseline = assetPath.EndsWith(
            "Resources/Player/player_authored.fbx", System.StringComparison.OrdinalIgnoreCase);
        // Class body variants share the baseline rig and reuse its clips, so
        // they import as mesh + skeleton only with identical model settings.
        bool classVariant = assetPath.Contains("Resources/Player/Classes/player_") &&
            assetPath.EndsWith(".fbx", System.StringComparison.OrdinalIgnoreCase);
        if (!baseline && !classVariant)
        {
            return;
        }

        ModelImporter importer = (ModelImporter)assetImporter;
        importer.isReadable = true;
        importer.importAnimation = baseline;
        importer.importBlendShapes = false;
        importer.importCameras = false;
        importer.importLights = false;
        importer.meshCompression = ModelImporterMeshCompression.Off;
        importer.generateMeshLods = false;
        importer.maximumMeshLod = -1;
        // The source is a Rigify generic hierarchy, not the old Human-avatar
        // player. Its baked curves must stay bound to this exact player rig.
        importer.animationType = ModelImporterAnimationType.Generic;
        importer.avatarSetup = ModelImporterAvatarSetup.NoAvatar;
        importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
    }
}
