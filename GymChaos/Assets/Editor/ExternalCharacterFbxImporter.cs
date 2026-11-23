using System;
using UnityEditor;

public sealed class ExternalCharacterFbxImporter : AssetPostprocessor
{
    private const string CharacterRoot = "Assets/Resources/Characters/";
    private const string EquipmentRoot = "Assets/Assets/";

    public override uint GetVersion()
    {
        return 6;
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
        // The remade player resolves its Female/Upper/Lower/Soft keys at runtime (PlayerAppearance).
        bool remakePlayer = assetPath.EndsWith("Remake/player_authored.fbx", StringComparison.OrdinalIgnoreCase);
        importer.importBlendShapes = remakePlayer;
        if (remakePlayer) importer.importBlendShapeNormals = ModelImporterNormals.Calculate;
        importer.importCameras = false;
        importer.importLights = false;
        importer.importAnimation = true;
        importer.animationType = ModelImporterAnimationType.Generic;
        importer.avatarSetup = ModelImporterAvatarSetup.NoAvatar;
        // Drops constant and near-linear keys (most of 72 bones x 3 curves per
        // clip); the tolerances stay far below what the foot/grip checks see.
        importer.animationCompression = ModelImporterAnimationCompression.KeyframeReduction;
        importer.animationRotationError = 0.1f;
        importer.animationPositionError = 0.1f;
        importer.animationScaleError = 0.1f;
        importer.optimizeGameObjects = false;
        importer.preserveHierarchy = true;
        importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
        importer.materialLocation = ModelImporterMaterialLocation.InPrefab;
    }

    // Clip loop flags are set here, not in OnPreprocessModel: only at this
    // point does defaultClipAnimations list the takes of the file being
    // imported. Read earlier it can still hold the previous import's takes,
    // so a re-exported FBX with a new take (deadlift) imported its walk/run
    // with or without loop pose depending on import order.
    private void OnPreprocessAnimation()
    {
        if (!assetPath.StartsWith(CharacterRoot, StringComparison.OrdinalIgnoreCase) ||
            !assetPath.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        ModelImporter importer = (ModelImporter)assetImporter;
        ModelImporterClipAnimation[] clips = importer.defaultClipAnimations;
        for (int i = 0; i < clips.Length; i++)
        {
            bool run = clips[i].name.IndexOf("run", StringComparison.OrdinalIgnoreCase) >= 0;
            bool walk = clips[i].name.IndexOf("walk", StringComparison.OrdinalIgnoreCase) >= 0;
            bool idle = clips[i].name.IndexOf("idle", StringComparison.OrdinalIgnoreCase) >= 0;
            bool fly = clips[i].name.IndexOf("fly", StringComparison.OrdinalIgnoreCase) >= 0;
            bool celebration = clips[i].name.IndexOf("celebration", StringComparison.OrdinalIgnoreCase) >= 0;
            // Jolly Dog's sky drop holds its falling clip until the landing.
            bool falling = clips[i].name.IndexOf("falling", StringComparison.OrdinalIgnoreCase) >= 0;
            bool loop = run || walk || idle || fly || celebration || falling;
            clips[i].loopTime = loop;
            clips[i].loopPose = loop;
        }
        if (clips.Length > 0)
        {
            importer.clipAnimations = clips;
        }
    }
}
