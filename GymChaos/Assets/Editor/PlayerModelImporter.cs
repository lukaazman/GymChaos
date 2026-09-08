using UnityEditor;

public sealed class PlayerModelImporter : AssetPostprocessor
{
    private void OnPreprocessModel()
    {
        if (!assetPath.EndsWith("Resources/Player/player_authored.fbx", System.StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        ModelImporter importer = (ModelImporter)assetImporter;
        importer.isReadable = true;
        importer.importAnimation = true;
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
