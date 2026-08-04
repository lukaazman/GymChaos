using System;
using UnityEditor;

public sealed class ExternalCharacterFbxImporter : AssetPostprocessor
{
    private const string CharacterRoot = "Assets/Resources/Characters/";

    public override uint GetVersion()
    {
        return 2;
    }

    private void OnPreprocessModel()
    {
        if (!assetPath.StartsWith(CharacterRoot, StringComparison.OrdinalIgnoreCase) ||
            !assetPath.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        ModelImporter importer = (ModelImporter)assetImporter;
        importer.isReadable = true;
        importer.meshCompression = ModelImporterMeshCompression.Off;
        importer.weldVertices = false;
        importer.optimizeMeshPolygons = false;
        importer.optimizeMeshVertices = false;
        importer.importNormals = ModelImporterNormals.Import;
        importer.importTangents = ModelImporterTangents.None;
        importer.importBlendShapes = false;
        importer.importCameras = false;
        importer.importLights = false;
        importer.importAnimation = true;
        importer.animationType = ModelImporterAnimationType.Generic;
        importer.avatarSetup = ModelImporterAvatarSetup.NoAvatar;
        importer.optimizeGameObjects = false;
        importer.preserveHierarchy = true;
        importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
        importer.materialLocation = ModelImporterMaterialLocation.InPrefab;

        ModelImporterClipAnimation[] clips = importer.defaultClipAnimations;
        for (int i = 0; i < clips.Length; i++)
        {
            bool run = clips[i].name.IndexOf("run", StringComparison.OrdinalIgnoreCase) >= 0;
            clips[i].loopTime = run;
            clips[i].loopPose = run;
        }
        if (clips.Length > 0)
        {
            importer.clipAnimations = clips;
        }
    }
}
