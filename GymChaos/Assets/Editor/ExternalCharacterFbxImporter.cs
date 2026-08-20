using System;
using UnityEditor;

public sealed class ExternalCharacterFbxImporter : AssetPostprocessor
{
    private const string CharacterRoot = "Assets/Resources/Characters/";
    private const string EquipmentRoot = "Assets/Assets/";

    public override uint GetVersion()
    {
        return 4;
    }

    private void OnPreprocessModel()
    {
        bool isCharacter = assetPath.StartsWith(CharacterRoot, StringComparison.OrdinalIgnoreCase);
        bool isEquipment = assetPath.StartsWith(EquipmentRoot, StringComparison.OrdinalIgnoreCase);
        if ((!isCharacter && !isEquipment) ||
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

        // Runtime pickup/static colliders are generated from these meshes.
        // Keep the source geometry readable so WebGL does not emit a
        // MeshCollider collision-data error when the bootstrap attaches them.
        if (!isCharacter)
        {
            return;
        }

        importer.importNormals = ModelImporterNormals.Import;
        importer.importTangents = ModelImporterTangents.None;
        importer.importBlendShapes = false;
        importer.importCameras = false;
        importer.importLights = false;
        importer.importAnimation = true;
        importer.animationType = ModelImporterAnimationType.Generic;
        importer.avatarSetup = ModelImporterAvatarSetup.NoAvatar;
        importer.animationCompression = ModelImporterAnimationCompression.Off;
        importer.animationRotationError = 0.001f;
        importer.animationPositionError = 0.001f;
        importer.animationScaleError = 0.001f;
        importer.optimizeGameObjects = false;
        importer.preserveHierarchy = true;
        importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
        importer.materialLocation = ModelImporterMaterialLocation.InPrefab;

        ModelImporterClipAnimation[] clips = importer.defaultClipAnimations;
        for (int i = 0; i < clips.Length; i++)
        {
            bool run = clips[i].name.IndexOf("run", StringComparison.OrdinalIgnoreCase) >= 0;
            bool idle = clips[i].name.IndexOf("idle", StringComparison.OrdinalIgnoreCase) >= 0;
            bool fly = clips[i].name.IndexOf("fly", StringComparison.OrdinalIgnoreCase) >= 0;
            bool celebration = clips[i].name.IndexOf("celebration", StringComparison.OrdinalIgnoreCase) >= 0;
            clips[i].loopTime = run || idle || fly || celebration;
            clips[i].loopPose = run || idle || fly || celebration;
        }
        if (clips.Length > 0)
        {
            importer.clipAnimations = clips;
        }
    }
}
