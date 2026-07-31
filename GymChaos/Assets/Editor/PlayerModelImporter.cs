using UnityEditor;

public sealed class PlayerModelImporter : AssetPostprocessor
{
    private void OnPreprocessModel()
    {
        if (assetPath.StartsWith("Assets/Resources/Player/Animations/", System.StringComparison.OrdinalIgnoreCase))
        {
            ModelImporter animationImporter = (ModelImporter)assetImporter;
            animationImporter.importAnimation = true;
            animationImporter.animationType = ModelImporterAnimationType.Generic;
            animationImporter.avatarSetup = ModelImporterAvatarSetup.NoAvatar;
            animationImporter.materialImportMode = ModelImporterMaterialImportMode.None;
            animationImporter.importBlendShapes = false;
            animationImporter.importCameras = false;
            animationImporter.importLights = false;
            return;
        }

        if (!assetPath.EndsWith("Resources/Player/player_mia_rigged.fbx", System.StringComparison.OrdinalIgnoreCase))
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
        importer.animationType = ModelImporterAnimationType.Human;
        importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
    }
}
