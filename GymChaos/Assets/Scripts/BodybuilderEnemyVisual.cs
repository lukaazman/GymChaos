using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.Rendering;

public enum BodybuilderIdentity
{
    Arnold,
    Cbum,
    Zyzz,
    Ronnie,
    Manwithsuit1,
    JayCutler,
    Goku
}

public sealed class BodybuilderEnemyVisual : MonoBehaviour
{
    // Shared downward correction for the imported eye-line anchor. Individual
    // asset offsets below remain relative to this common baseline.
    private const float ImportedEyeLineBaseOffset = 0.06f;

    private const uint GlbMagic = 0x46546C67;
    private const uint JsonChunk = 0x4E4F534A;
    private const uint BinaryChunk = 0x004E4942;

    private readonly struct RigProfile
    {
        public readonly Vector2 LeftShoulder;
        public readonly Vector2 LeftElbow;
        public readonly Vector2 LeftHand;
        public readonly Vector2 RightShoulder;
        public readonly Vector2 RightElbow;
        public readonly Vector2 RightHand;
        public readonly Vector2 LeftHip;
        public readonly Vector2 LeftKnee;
        public readonly Vector2 LeftFoot;
        public readonly Vector2 RightHip;
        public readonly Vector2 RightKnee;
        public readonly Vector2 RightFoot;
        public readonly float NeckY;
        public readonly float HeadHalfWidth;
        public readonly float ArmRadius;
        public readonly float FaceY;
        public readonly float LeftArmZ;
        public readonly float RightArmZ;
        public readonly float LeftHipZ;
        public readonly float LeftKneeZ;
        public readonly float LeftFootZ;
        public readonly float RightHipZ;
        public readonly float RightKneeZ;
        public readonly float RightFootZ;

        public RigProfile(
            Vector2 leftShoulder, Vector2 leftElbow, Vector2 leftHand,
            Vector2 rightShoulder, Vector2 rightElbow, Vector2 rightHand,
            Vector2 leftHip, Vector2 leftKnee, Vector2 leftFoot,
            Vector2 rightHip, Vector2 rightKnee, Vector2 rightFoot,
            float neckY, float headHalfWidth, float armRadius, float faceY,
            float leftArmZ = 0f, float rightArmZ = 0f,
            float leftHipZ = 0f, float leftKneeZ = 0f, float leftFootZ = 0f,
            float rightHipZ = 0f, float rightKneeZ = 0f, float rightFootZ = 0f)
        {
            LeftShoulder = leftShoulder;
            LeftElbow = leftElbow;
            LeftHand = leftHand;
            RightShoulder = rightShoulder;
            RightElbow = rightElbow;
            RightHand = rightHand;
            LeftHip = leftHip;
            LeftKnee = leftKnee;
            LeftFoot = leftFoot;
            RightHip = rightHip;
            RightKnee = rightKnee;
            RightFoot = rightFoot;
            NeckY = neckY;
            HeadHalfWidth = headHalfWidth;
            ArmRadius = armRadius;
            FaceY = faceY;
            LeftArmZ = leftArmZ;
            RightArmZ = rightArmZ;
            LeftHipZ = leftHipZ;
            LeftKneeZ = leftKneeZ;
            LeftFootZ = leftFootZ;
            RightHipZ = rightHipZ;
            RightKneeZ = rightKneeZ;
            RightFootZ = rightFootZ;
        }
    }

    [Serializable]
    private sealed class GltfRoot
    {
        public GltfBufferView[] bufferViews;
        public GltfAccessor[] accessors;
        public GltfMesh[] meshes;
        public GltfImage[] images;
    }

    [Serializable]
    private sealed class GltfBufferView
    {
        public int byteOffset;
        public int byteLength;
        public int byteStride;
    }

    [Serializable]
    private sealed class GltfAccessor
    {
        public int bufferView;
        public int byteOffset;
        public int componentType;
        public int count;
        public string type;
    }

    [Serializable]
    private sealed class GltfMesh
    {
        public GltfPrimitive[] primitives;
    }

    [Serializable]
    private sealed class GltfPrimitive
    {
        public GltfAttributes attributes;
        public int indices;
    }

    [Serializable]
    private sealed class GltfAttributes
    {
        public int POSITION;
        public int NORMAL;
        public int TEXCOORD_0;
    }

    [Serializable]
    private sealed class GltfImage
    {
        public int bufferView;
        public string mimeType;
    }

    public sealed class Rig
    {
        public Transform Root;
        public Transform Hips;
        public Transform Spine;
        public Transform Chest;
        public Transform Neck;
        public Transform Head;
        public Transform LeftShoulder;
        public Transform LeftUpperArm;
        public Transform LeftForearm;
        public Transform RightShoulder;
        public Transform RightUpperArm;
        public Transform RightForearm;
        public Transform LeftThigh;
        public Transform LeftShin;
        public Transform LeftFoot;
        public Transform RightThigh;
        public Transform RightShin;
        public Transform RightFoot;
        public Transform LeftHand;
        public Transform RightHand;
        public Vector3 LeftHandPosition;
        public Vector3 RightHandPosition;
        public Vector3 LeftFootPosition;
        public Vector3 RightFootPosition;

        public Transform[] All => new[]
        {
            Root, Hips, Spine, Chest, Head,
            LeftUpperArm, LeftForearm, RightUpperArm, RightForearm,
            LeftThigh, LeftShin, RightThigh, RightShin,
            LeftHand, RightHand, LeftShoulder, RightShoulder
        };
    }

    public static BodybuilderEnemyVisual Build(GameObject enemy, BodybuilderIdentity identity)
    {
        // The final FBX is the per-enemy T-pose scan with its fitted Mixamo
        // skeleton and authored UV/material layout.  Keep that hierarchy as
        // the visible body; the legacy procedural GLB loader below is only a
        // recovery fallback when an FBX is genuinely unavailable.
        if (ExternalRiggedCharacterVisual.TryBuild(enemy, identity))
        {
            return null;
        }

        BodybuilderEnemyVisual visual = enemy.AddComponent<BodybuilderEnemyVisual>();
        // Recovery-only path for a missing/invalid final FBX.
        visual.StartCoroutine(visual.BuildVisual(identity, false));
        return visual;
    }

    public static BodybuilderEnemyVisual BuildNeutralNpc(GameObject npc, BodybuilderIdentity identity)
    {
        BodybuilderEnemyVisual visual = npc.AddComponent<BodybuilderEnemyVisual>();
        visual.StartCoroutine(visual.BuildVisual(identity, true));
        return visual;
    }

    public static void ConfigureImportedVisual(
        Transform visualRoot, SkinnedMeshRenderer bodyRenderer, Rig rig,
        BodybuilderIdentity identity, bool neutralNpc)
    {
        if (visualRoot == null || bodyRenderer == null || rig == null || rig.Head == null)
        {
            return;
        }

        Mesh baked = new Mesh { name = identity + " imported visual setup" };
        // Request vertices without the renderer scale, then apply the
        // renderer hierarchy exactly once while converting them into the
        // visible model-root space. The imported armature has a bind scale of
        // about 0.02; baking that scale and TransformPoint-ing it again makes
        // the eye sample drift roughly a metre above the head.
        bodyRenderer.BakeMesh(baked, false);
        Vector3[] bakedVertices = baked.vertices;
        Vector3[] vertices = new Vector3[bakedVertices.Length];
        Bounds bounds = default;
        for (int i = 0; i < bakedVertices.Length; i++)
        {
            Vector3 world = bodyRenderer.transform.TransformPoint(bakedVertices[i]);
            vertices[i] = visualRoot.InverseTransformPoint(world);
            if (i == 0)
            {
                bounds = new Bounds(vertices[i], Vector3.zero);
            }
            else
            {
                bounds.Encapsulate(vertices[i]);
            }
        }
        Destroy(baked);

        RigProfile profile = GetRigProfile(identity);
        LogImportedHeadGeometry(identity, bodyRenderer, rig.Head, visualRoot, vertices);
        FaceCensorProfile importedProfile = GetImportedFaceCensorProfile(
            identity, vertices, bounds, profile, visualRoot, rig.Head,
            bodyRenderer.bounds.size.y);
        if (neutralNpc)
        {
            CreateDeathMarkersAndName(
                visualRoot, vertices, bounds, profile, rig.Head, bodyRenderer, identity,
                importedProfile);
        }
        else
        {
            CreateFaceCensorAndName(
                visualRoot, vertices, bounds, profile, rig.Head, bodyRenderer, identity,
                importedProfile);
        }
    }

    private static void LogImportedHeadGeometry(
        BodybuilderIdentity identity, SkinnedMeshRenderer bodyRenderer,
        Transform head, Transform visualRoot, Vector3[] vertices)
    {
        if (bodyRenderer.sharedMesh == null || bodyRenderer.bones == null)
        {
            return;
        }

        int headBoneIndex = -1;
        for (int i = 0; i < bodyRenderer.bones.Length; i++)
        {
            if (bodyRenderer.bones[i] == head)
            {
                headBoneIndex = i;
                break;
            }
        }
        if (headBoneIndex < 0)
        {
            return;
        }

        BoneWeight[] weights = bodyRenderer.sharedMesh.boneWeights;
        bool hasBounds = false;
        Bounds headBounds = default;
        int weightedVertices = 0;
        for (int i = 0; i < vertices.Length && i < weights.Length; i++)
        {
            BoneWeight weight = weights[i];
            float headWeight = 0f;
            if (weight.boneIndex0 == headBoneIndex) headWeight = Mathf.Max(headWeight, weight.weight0);
            if (weight.boneIndex1 == headBoneIndex) headWeight = Mathf.Max(headWeight, weight.weight1);
            if (weight.boneIndex2 == headBoneIndex) headWeight = Mathf.Max(headWeight, weight.weight2);
            if (weight.boneIndex3 == headBoneIndex) headWeight = Mathf.Max(headWeight, weight.weight3);
            if (headWeight < 0.25f)
            {
                continue;
            }

            weightedVertices++;
            if (!hasBounds)
            {
                headBounds = new Bounds(vertices[i], Vector3.zero);
                hasBounds = true;
            }
            else
            {
                headBounds.Encapsulate(vertices[i]);
            }
        }

        if (hasBounds)
        {
            Debug.Log(
                $"FACE_HEAD_GEOMETRY_DEBUG identity={identity} headLocal=" +
                $"{visualRoot.InverseTransformPoint(head.position)} weightedVertices={weightedVertices} " +
                $"headMeshBounds={headBounds.min}/{headBounds.max} center={headBounds.center}");
        }
    }

    private IEnumerator BuildVisual(BodybuilderIdentity identity, bool neutralNpc)
    {
        // Stagger the large scans so parsing and mesh upload do not land in one frame.
        int delayFrames = (int)identity * 2 + 1;
        for (int i = 0; i < delayFrames; i++)
        {
            yield return null;
        }

        string fileName = GetModelFileName(identity);
        string path = JoinStreamingAssetsPath("BodyBuilders/" + fileName);
        byte[] glbBytes;
        using (UnityWebRequest request = UnityWebRequest.Get(path))
        {
            request.downloadHandler = new DownloadHandlerBuffer();
            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"Bodybuilder model is missing or could not be loaded: {path}\n{request.error}", this);
                yield break;
            }

            glbBytes = request.downloadHandler.data;
        }

        if (!TryReadGlb(glbBytes, out GltfRoot gltf, out byte[] binary))
        {
            Debug.LogError($"Could not read bodybuilder model: {fileName}", this);
            yield break;
        }

        yield return null;

        GltfPrimitive primitive = gltf.meshes[0].primitives[0];
        Vector3[] sourcePositions = ReadVector3Accessor(gltf, binary, primitive.attributes.POSITION);
        Vector3[] sourceNormals = ReadVector3Accessor(gltf, binary, primitive.attributes.NORMAL);
        Vector2[] sourceUvs = ReadVector2Accessor(gltf, binary, primitive.attributes.TEXCOORD_0);
        int[] triangles = ReadIndexAccessor(gltf, binary, primitive.indices);

        float sourceMinY = float.PositiveInfinity;
        float sourceMaxY = float.NegativeInfinity;
        for (int i = 0; i < sourcePositions.Length; i++)
        {
            sourceMinY = Mathf.Min(sourceMinY, sourcePositions[i].y);
            sourceMaxY = Mathf.Max(sourceMaxY, sourcePositions[i].y);
        }

        float baseMeshHeight = identity == BodybuilderIdentity.Arnold ? 1.88f : 1.84f;
        float targetMeshHeight = identity == BodybuilderIdentity.Manwithsuit1
            ? 1.82f * 1.125f
            : baseMeshHeight * 1.25f;
        float sourceHeight = Mathf.Max(0.01f, sourceMaxY - sourceMinY);
        float scale = targetMeshHeight / sourceHeight;
        float modelBottom = 0f;
        float yOffset = modelBottom - sourceMinY * scale;
        RigProfile profile = GetRigProfile(identity);

        Vector3[] positions = new Vector3[sourcePositions.Length];
        Vector3[] normals = new Vector3[sourceNormals.Length];
        Vector2[] uvs = new Vector2[sourceUvs.Length];
        Bounds bounds = new Bounds();
        for (int i = 0; i < positions.Length; i++)
        {
            Vector3 source = sourcePositions[i];
            positions[i] = new Vector3(-source.x * scale, source.y * scale + yOffset, source.z * scale);
            normals[i] = new Vector3(-sourceNormals[i].x, sourceNormals[i].y, sourceNormals[i].z).normalized;
            uvs[i] = new Vector2(sourceUvs[i].x, 1f - sourceUvs[i].y);
            if (i == 0)
            {
                bounds = new Bounds(positions[i], Vector3.zero);
            }
            else
            {
                bounds.Encapsulate(positions[i]);
            }
        }

        for (int i = 0; i + 2 < triangles.Length; i += 3)
        {
            (triangles[i], triangles[i + 2]) = (triangles[i + 2], triangles[i]);
        }

        yield return null;

        GameObject renderObject = new GameObject(identity + " Body");
        renderObject.transform.SetParent(transform, false);
        Rig rig = CreateRig(renderObject.transform, bounds, profile);
        BoneWeight[] boneWeights = CreateBoneWeights(positions, triangles, bounds, profile, identity);

        // Keep imported topology intact. Runtime clustering merged UV seams and
        // discarded triangles, producing visible holes in the textured scans.
        Debug.Log(
            $"GYMCHAOS_MESH_SOURCE {identity} triangles={triangles.Length / 3} vertices={positions.Length}",
            this);

        yield return null;

        Mesh mesh = new Mesh
        {
            name = identity + " Runtime Rigged Mesh",
            indexFormat = IndexFormat.UInt32,
            vertices = positions,
            normals = normals,
            uv = uvs,
            triangles = triangles,
            boneWeights = boneWeights
        };

        Transform[] bones = rig.All;
        Matrix4x4[] bindPoses = new Matrix4x4[bones.Length];
        for (int i = 0; i < bones.Length; i++)
        {
            bindPoses[i] = bones[i].worldToLocalMatrix * renderObject.transform.localToWorldMatrix;
        }

        mesh.bindposes = bindPoses;
        mesh.bounds = new Bounds(bounds.center, bounds.size + Vector3.one * 0.45f);

        SkinnedMeshRenderer renderer = renderObject.AddComponent<SkinnedMeshRenderer>();
        renderer.sharedMesh = mesh;
        renderer.bones = bones;
        renderer.rootBone = rig.Root;
        renderer.sharedMaterial = CreateBodyMaterial(gltf, binary, identity);
        renderer.updateWhenOffscreen = false;
        renderer.quality = SkinQuality.Bone4;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.skinnedMotionVectors = false;

        EnemyMeshHitboxRig.Configure(gameObject, rig, renderer);

        if (identity == BodybuilderIdentity.Goku)
        {
            GokuAura aura = gameObject.GetComponent<GokuAura>();
            if (aura == null)
            {
                aura = gameObject.AddComponent<GokuAura>();
            }
            aura.Configure(renderer);
        }

        if (neutralNpc)
        {
            CreateDeathMarkersAndName(
                renderObject.transform, positions, bounds, profile,
                rig.Head, renderer, identity);
            ManWithSuitIdleAnimator animator = gameObject.AddComponent<ManWithSuitIdleAnimator>();
            animator.Configure(rig);
        }
        else
        {
            CreateFaceCensorAndName(
                renderObject.transform, positions, bounds, profile,
                rig.Head, renderer, identity);
            MixamoScanRetargetAnimator mixamoAnimator =
                gameObject.AddComponent<MixamoScanRetargetAnimator>();
            if (!mixamoAnimator.Configure(identity, rig))
            {
                Destroy(mixamoAnimator);
                BodybuilderEnemyAnimator fallbackAnimator =
                    gameObject.AddComponent<BodybuilderEnemyAnimator>();
                fallbackAnimator.Configure(identity, rig);
            }
        }
    }

    private static string JoinStreamingAssetsPath(string relativePath)
    {
        string path = Application.streamingAssetsPath.TrimEnd('/', '\\') + "/" + relativePath;
        if (path.Contains("://"))
        {
            return path;
        }
        return "file:///" + path.Replace('\\', '/').TrimStart('/');
    }

    private static string GetModelFileName(BodybuilderIdentity identity)
    {
        return identity == BodybuilderIdentity.JayCutler
            ? "jay.glb"
            : identity.ToString().ToLowerInvariant() + ".glb";
    }

    private static void SimplifyRuntimeMesh(
        ref Vector3[] positions, ref Vector3[] normals, ref Vector2[] uvs,
        ref BoneWeight[] boneWeights, ref int[] triangles, Bounds bounds, int targetTriangleCount)
    {
        if (triangles.Length / 3 <= targetTriangleCount || positions.Length == 0)
        {
            return;
        }

        float cellSize = Mathf.Max(0.001f, bounds.size.y / 650f);
        Vector3[] bestPositions = positions;
        Vector3[] bestNormals = normals;
        Vector2[] bestUvs = uvs;
        BoneWeight[] bestWeights = boneWeights;
        int[] bestTriangles = triangles;

        for (int attempt = 0; attempt < 8; attempt++)
        {
            ClusterMesh(
                positions, normals, uvs, boneWeights, triangles,
                bounds.min, cellSize,
                out Vector3[] reducedPositions, out Vector3[] reducedNormals,
                out Vector2[] reducedUvs, out BoneWeight[] reducedWeights,
                out int[] reducedTriangles);

            bestPositions = reducedPositions;
            bestNormals = reducedNormals;
            bestUvs = reducedUvs;
            bestWeights = reducedWeights;
            bestTriangles = reducedTriangles;
            if (reducedTriangles.Length / 3 <= targetTriangleCount)
            {
                break;
            }
            cellSize *= 1.35f;
        }

        positions = bestPositions;
        normals = bestNormals;
        uvs = bestUvs;
        boneWeights = bestWeights;
        triangles = bestTriangles;
    }

    private static void ClusterMesh(
        Vector3[] positions, Vector3[] normals, Vector2[] uvs, BoneWeight[] weights,
        int[] triangles, Vector3 minimum, float cellSize,
        out Vector3[] reducedPositions, out Vector3[] reducedNormals,
        out Vector2[] reducedUvs, out BoneWeight[] reducedWeights, out int[] reducedTriangles)
    {
        Dictionary<Vector3Int, int> clusters = new Dictionary<Vector3Int, int>(positions.Length / 2);
        List<Vector3> positionList = new List<Vector3>(positions.Length / 2);
        List<Vector3> normalList = new List<Vector3>(positions.Length / 2);
        List<Vector2> uvList = new List<Vector2>(positions.Length / 2);
        List<BoneWeight> weightList = new List<BoneWeight>(positions.Length / 2);
        int[] remap = new int[positions.Length];

        for (int i = 0; i < positions.Length; i++)
        {
            Vector3 relative = (positions[i] - minimum) / cellSize;
            Vector3Int key = new Vector3Int(
                Mathf.FloorToInt(relative.x),
                Mathf.FloorToInt(relative.y),
                Mathf.FloorToInt(relative.z));
            if (!clusters.TryGetValue(key, out int reducedIndex))
            {
                reducedIndex = positionList.Count;
                clusters.Add(key, reducedIndex);
                positionList.Add(positions[i]);
                normalList.Add(i < normals.Length ? normals[i] : Vector3.up);
                uvList.Add(i < uvs.Length ? uvs[i] : Vector2.zero);
                weightList.Add(i < weights.Length ? weights[i] : default);
            }
            remap[i] = reducedIndex;
        }

        List<int> triangleList = new List<int>(triangles.Length);
        for (int i = 0; i + 2 < triangles.Length; i += 3)
        {
            int a = remap[triangles[i]];
            int b = remap[triangles[i + 1]];
            int c = remap[triangles[i + 2]];
            if (a == b || b == c || c == a)
            {
                continue;
            }
            triangleList.Add(a);
            triangleList.Add(b);
            triangleList.Add(c);
        }

        reducedPositions = positionList.ToArray();
        reducedNormals = normalList.ToArray();
        reducedUvs = uvList.ToArray();
        reducedWeights = weightList.ToArray();
        reducedTriangles = triangleList.ToArray();
    }

    private static RigProfile GetRigProfile(BodybuilderIdentity identity)
    {
        switch (identity)
        {
            case BodybuilderIdentity.Arnold:
                return new RigProfile(
                    new Vector2(-0.13f, 0.72f), new Vector2(-0.27f, 0.82f), new Vector2(-0.20f, 0.91f),
                    new Vector2(0.13f, 0.72f), new Vector2(0.27f, 0.82f), new Vector2(0.20f, 0.91f),
                    new Vector2(-0.055f, 0.49f), new Vector2(-0.055f, 0.25f), new Vector2(-0.055f, 0.02f),
                    new Vector2(0.055f, 0.49f), new Vector2(0.055f, 0.25f), new Vector2(0.055f, 0.02f),
                    0.79f, 0.09f, 0.095f, 0.88f);
            case BodybuilderIdentity.Cbum:
                return new RigProfile(
                    new Vector2(-0.14f, 0.70f), new Vector2(-0.23f, 0.62f), new Vector2(-0.12f, 0.52f),
                    new Vector2(0.14f, 0.70f), new Vector2(0.23f, 0.62f), new Vector2(0.12f, 0.52f),
                    new Vector2(-0.065f, 0.49f), new Vector2(-0.065f, 0.25f), new Vector2(-0.075f, 0.02f),
                    new Vector2(0.065f, 0.49f), new Vector2(0.065f, 0.25f), new Vector2(0.075f, 0.02f),
                    0.79f, 0.085f, 0.095f, 0.88f);
            case BodybuilderIdentity.Zyzz:
                // The generic profile previously put Zyzz's right knee and
                // foot forward of the left side. Keep his proportions, but
                // make the complete leg and arm chains bilateral so the
                // fallback visual uses the same squat stance as the FBX rig.
                return new RigProfile(
                    new Vector2(-0.10f, 0.70f), new Vector2(-0.14f, 0.56f), new Vector2(-0.12f, 0.42f),
                    new Vector2(0.10f, 0.70f), new Vector2(0.14f, 0.56f), new Vector2(0.12f, 0.42f),
                    new Vector2(-0.052f, 0.49f), new Vector2(-0.052f, 0.25f), new Vector2(-0.048f, 0.02f),
                    new Vector2(0.052f, 0.49f), new Vector2(0.052f, 0.25f), new Vector2(0.048f, 0.02f),
                    0.79f, 0.085f, 0.095f, 0.88f);
            case BodybuilderIdentity.Ronnie:
                return new RigProfile(
                    new Vector2(-0.13f, 0.70f), new Vector2(-0.18f, 0.57f), new Vector2(-0.14f, 0.43f),
                    new Vector2(0.13f, 0.70f), new Vector2(0.18f, 0.57f), new Vector2(0.14f, 0.43f),
                    new Vector2(-0.055f, 0.49f), new Vector2(-0.055f, 0.25f), new Vector2(-0.06f, 0.02f),
                    new Vector2(0.055f, 0.49f), new Vector2(0.055f, 0.25f), new Vector2(0.06f, 0.02f),
                    0.79f, 0.09f, 0.095f, 0.88f);
            case BodybuilderIdentity.Manwithsuit1:
                return new RigProfile(
                    new Vector2(-0.105f, 0.70f), new Vector2(-0.12f, 0.57f), new Vector2(-0.09f, 0.65f),
                    new Vector2(0.105f, 0.70f), new Vector2(0.12f, 0.57f), new Vector2(0.09f, 0.65f),
                    new Vector2(-0.052f, 0.49f), new Vector2(-0.052f, 0.25f), new Vector2(-0.055f, 0.02f),
                    new Vector2(0.052f, 0.49f), new Vector2(0.052f, 0.25f), new Vector2(0.055f, 0.02f),
                    0.79f, 0.08f, 0.085f, 0.88f, 0.12f, 0.12f);
            case BodybuilderIdentity.JayCutler:
                return new RigProfile(
                    new Vector2(-0.11f, 0.70f), new Vector2(-0.14f, 0.56f), new Vector2(-0.12f, 0.42f),
                    new Vector2(0.11f, 0.70f), new Vector2(0.14f, 0.56f), new Vector2(0.12f, 0.42f),
                    new Vector2(-0.05f, 0.49f), new Vector2(-0.05f, 0.25f), new Vector2(-0.04f, 0.02f),
                    new Vector2(0.05f, 0.49f), new Vector2(0.05f, 0.25f), new Vector2(0.04f, 0.02f),
                    0.79f, 0.085f, 0.095f, 0.88f);
            case BodybuilderIdentity.Goku:
                return new RigProfile(
                    new Vector2(-0.11f, 0.70f), new Vector2(-0.15f, 0.56f), new Vector2(-0.12f, 0.42f),
                    new Vector2(0.11f, 0.70f), new Vector2(0.15f, 0.56f), new Vector2(0.12f, 0.42f),
                    new Vector2(-0.05f, 0.49f), new Vector2(-0.05f, 0.25f), new Vector2(-0.04f, 0.02f),
                    new Vector2(0.05f, 0.49f), new Vector2(0.05f, 0.25f), new Vector2(0.04f, 0.02f),
                    0.79f, 0.085f, 0.095f, 0.78f);
            default:
                return new RigProfile(
                    new Vector2(-0.10f, 0.70f), new Vector2(-0.14f, 0.56f), new Vector2(-0.12f, 0.42f),
                    new Vector2(0.10f, 0.70f), new Vector2(0.14f, 0.56f), new Vector2(0.12f, 0.42f),
                    new Vector2(-0.05f, 0.49f), new Vector2(-0.05f, 0.25f), new Vector2(-0.045f, 0.02f),
                    new Vector2(0.05f, 0.49f), new Vector2(0.05f, 0.25f), new Vector2(0.045f, 0.02f),
                    0.79f, 0.08f, 0.10f, 0.87f, -0.085f, 0.08f,
                    0f, -0.025f, -0.08f, 0f, 0.025f, 0.08f);
        }
    }

    private static Rig CreateRig(Transform visualRoot, Bounds bounds, RigProfile profile)
    {
        float bottom = bounds.min.y;
        float height = bounds.size.y;
        float z = bounds.center.z;

        Rig rig = new Rig();
        rig.Root = CreateBone("Rig Root", visualRoot, visualRoot, new Vector3(0f, bottom, z));
        rig.Hips = CreateBone("Hips", rig.Root, visualRoot, new Vector3(0f, bottom + height * 0.49f, z));
        rig.Spine = CreateBone("Spine", rig.Hips, visualRoot, new Vector3(0f, bottom + height * 0.61f, z));
        rig.Chest = CreateBone("Chest", rig.Spine, visualRoot, new Vector3(0f, bottom + height * 0.72f, z));
        rig.Head = CreateBone("Head", rig.Chest, visualRoot, ToModelPoint(bounds, new Vector2(0f, 0.84f)));

        // Keep the shoulder joint in the runtime GLB rig. The earlier rig
        // started at upper-arm level, so Mixamo shoulder motion was discarded
        // and the visible punch/run arc was only the distal half of the arm.
        Vector2 leftShoulderRoot = new Vector2(profile.LeftShoulder.x * 0.55f, profile.LeftShoulder.y);
        Vector2 rightShoulderRoot = new Vector2(profile.RightShoulder.x * 0.55f, profile.RightShoulder.y);
        rig.LeftShoulder = CreateBone("Left Shoulder", rig.Chest, visualRoot, ToModelPoint(bounds, leftShoulderRoot));
        rig.LeftUpperArm = CreateBone("Left Upper Arm", rig.LeftShoulder, visualRoot, ToModelPoint(bounds, profile.LeftShoulder));
        rig.LeftForearm = CreateBone("Left Forearm", rig.LeftUpperArm, visualRoot, ToModelPoint(bounds, profile.LeftElbow, profile.LeftArmZ * 0.55f));
        rig.RightShoulder = CreateBone("Right Shoulder", rig.Chest, visualRoot, ToModelPoint(bounds, rightShoulderRoot));
        rig.RightUpperArm = CreateBone("Right Upper Arm", rig.RightShoulder, visualRoot, ToModelPoint(bounds, profile.RightShoulder));
        rig.RightForearm = CreateBone("Right Forearm", rig.RightUpperArm, visualRoot, ToModelPoint(bounds, profile.RightElbow, profile.RightArmZ * 0.55f));

        rig.LeftThigh = CreateBone("Left Thigh", rig.Hips, visualRoot, ToModelPoint(bounds, profile.LeftHip, profile.LeftHipZ));
        rig.LeftShin = CreateBone("Left Shin", rig.LeftThigh, visualRoot, ToModelPoint(bounds, profile.LeftKnee, profile.LeftKneeZ));
        rig.RightThigh = CreateBone("Right Thigh", rig.Hips, visualRoot, ToModelPoint(bounds, profile.RightHip, profile.RightHipZ));
        rig.RightShin = CreateBone("Right Shin", rig.RightThigh, visualRoot, ToModelPoint(bounds, profile.RightKnee, profile.RightKneeZ));
        rig.LeftHandPosition = ToModelPoint(bounds, profile.LeftHand, profile.LeftArmZ);
        rig.RightHandPosition = ToModelPoint(bounds, profile.RightHand, profile.RightArmZ);
        rig.LeftHand = CreateBone(
            "Left Hand", rig.LeftForearm, visualRoot,
            rig.LeftHandPosition);
        rig.RightHand = CreateBone(
            "Right Hand", rig.RightForearm, visualRoot,
            rig.RightHandPosition);
        rig.LeftFootPosition = ToModelPoint(bounds, profile.LeftFoot, profile.LeftFootZ);
        rig.RightFootPosition = ToModelPoint(bounds, profile.RightFoot, profile.RightFootZ);
        return rig;
    }

    private static Vector3 ToModelPoint(Bounds bounds, Vector2 normalizedPoint)
    {
        return ToModelPoint(bounds, normalizedPoint, 0f);
    }

    private static Vector3 ToModelPoint(Bounds bounds, Vector2 normalizedPoint, float normalizedZ)
    {
        return new Vector3(
            bounds.center.x + normalizedPoint.x * bounds.size.y,
            bounds.min.y + normalizedPoint.y * bounds.size.y,
            bounds.center.z + normalizedZ * bounds.size.y);
    }

    private static Transform CreateBone(string name, Transform parent, Transform visualRoot, Vector3 visualLocalPosition)
    {
        Transform bone = new GameObject(name).transform;
        bone.position = visualRoot.TransformPoint(visualLocalPosition);
        bone.rotation = visualRoot.rotation;
        bone.SetParent(parent, true);
        bone.localScale = Vector3.one;
        return bone;
    }

    private static BoneWeight[] CreateBoneWeights(
        Vector3[] vertices, int[] triangles, Bounds bounds,
        RigProfile profile, BodybuilderIdentity identity)
    {
        BoneWeight[] weights = new BoneWeight[vertices.Length];
        float bottom = bounds.min.y;
        float height = Mathf.Max(0.01f, bounds.size.y);
        Vector2 leftShoulder = ToPlanePoint(bounds, profile.LeftShoulder);
        Vector2 leftElbow = ToPlanePoint(bounds, profile.LeftElbow);
        Vector2 leftHand = ToPlanePoint(bounds, profile.LeftHand);
        Vector2 rightShoulder = ToPlanePoint(bounds, profile.RightShoulder);
        Vector2 rightElbow = ToPlanePoint(bounds, profile.RightElbow);
        Vector2 rightHand = ToPlanePoint(bounds, profile.RightHand);
        Vector2 leftHip = ToPlanePoint(bounds, profile.LeftHip);
        Vector2 leftKnee = ToPlanePoint(bounds, profile.LeftKnee);
        Vector2 leftFoot = ToPlanePoint(bounds, profile.LeftFoot);
        Vector2 rightHip = ToPlanePoint(bounds, profile.RightHip);
        Vector2 rightKnee = ToPlanePoint(bounds, profile.RightKnee);
        Vector2 rightFoot = ToPlanePoint(bounds, profile.RightFoot);
        Vector3 leftShoulder3 = ToModelPoint(bounds, profile.LeftShoulder);
        Vector3 leftElbow3 = ToModelPoint(bounds, profile.LeftElbow, profile.LeftArmZ * 0.55f);
        Vector3 leftHand3 = ToModelPoint(bounds, profile.LeftHand, profile.LeftArmZ);
        Vector3 rightShoulder3 = ToModelPoint(bounds, profile.RightShoulder);
        Vector3 rightElbow3 = ToModelPoint(bounds, profile.RightElbow, profile.RightArmZ * 0.55f);
        Vector3 rightHand3 = ToModelPoint(bounds, profile.RightHand, profile.RightArmZ);
        Vector3 leftHip3 = ToModelPoint(bounds, profile.LeftHip, profile.LeftHipZ);
        Vector3 leftKnee3 = ToModelPoint(bounds, profile.LeftKnee, profile.LeftKneeZ);
        Vector3 leftFoot3 = ToModelPoint(bounds, profile.LeftFoot, profile.LeftFootZ);
        Vector3 rightHip3 = ToModelPoint(bounds, profile.RightHip, profile.RightHipZ);
        Vector3 rightKnee3 = ToModelPoint(bounds, profile.RightKnee, profile.RightKneeZ);
        Vector3 rightFoot3 = ToModelPoint(bounds, profile.RightFoot, profile.RightFootZ);
        bool[] gokuHeadComponents = identity == BodybuilderIdentity.Goku
            ? MarkGokuHeadComponents(vertices, triangles, bounds)
            : null;
        for (int i = 0; i < vertices.Length; i++)
        {
            Vector3 vertex = vertices[i];
            float normalizedY = (vertex.y - bottom) / height;
            Vector2 point = new Vector2(vertex.x, vertex.y);
            Vector3 leftReach = identity == BodybuilderIdentity.Manwithsuit1
                ? leftHand3 + (leftHand3 - leftElbow3).normalized * height * 0.16f
                : leftHand3;
            Vector3 rightReach = identity == BodybuilderIdentity.Manwithsuit1
                ? rightHand3 + (rightHand3 - rightElbow3).normalized * height * 0.16f
                : rightHand3;
            float armRadius = profile.ArmRadius * height *
                (identity == BodybuilderIdentity.Manwithsuit1 ? 1.35f : 1f);
            float leftDistance = Mathf.Min(
                DistanceToSegment(vertex, leftShoulder3, leftElbow3),
                DistanceToSegment(vertex, leftElbow3, leftReach));
            float rightDistance = Mathf.Min(
                DistanceToSegment(vertex, rightShoulder3, rightElbow3),
                DistanceToSegment(vertex, rightElbow3, rightReach));
            bool useLeftArm = leftDistance < armRadius && leftDistance <= rightDistance;
            bool useRightArm = rightDistance < armRadius && rightDistance < leftDistance;
            bool gokuCenterLowerBody = identity == BodybuilderIdentity.Goku &&
                normalizedY < 0.46f && Mathf.Abs(vertex.x - bounds.center.x) < height * 0.13f;
            bool suitRightDistal = identity == BodybuilderIdentity.Manwithsuit1 &&
                vertex.x > bounds.center.x + height * 0.135f &&
                normalizedY > 0.52f && normalizedY < 0.73f &&
                Vector3.Distance(vertex, rightHand3) < height * 0.18f;

            if ((gokuHeadComponents != null && gokuHeadComponents[i]) ||
                (identity == BodybuilderIdentity.Goku && normalizedY > 0.78f) ||
                (normalizedY >= profile.NeckY && Mathf.Abs(vertex.x - bounds.center.x) <= profile.HeadHalfWidth * height))
            {
                weights[i] = SingleBone(4);
            }
            else if (normalizedY > 0.43f && normalizedY < 0.53f &&
                     Mathf.Abs(vertex.x - bounds.center.x) < height * 0.16f)
            {
                weights[i] = SingleBone(1);
            }
            else if (identity == BodybuilderIdentity.Manwithsuit1 && useLeftArm)
            {
                weights[i] = ArmWeightWithHand(
                    vertex, leftShoulder3, leftElbow3, leftHand3, 5, 6, 13);
            }
            else if (identity == BodybuilderIdentity.Manwithsuit1 && (useRightArm || suitRightDistal))
            {
                weights[i] = suitRightDistal
                    ? SingleBone(14)
                    : ArmWeightWithHand(vertex, rightShoulder3, rightElbow3, rightHand3, 7, 8, 14);
            }
            else if (useLeftArm)
            {
                weights[i] = ArmWeightWithHand(
                    vertex, leftShoulder3, leftElbow3, leftHand3, 5, 6, 13);
            }
            else if (useRightArm)
            {
                weights[i] = ArmWeightWithHand(
                    vertex, rightShoulder3, rightElbow3, rightHand3, 7, 8, 14);
            }
            else if (gokuCenterLowerBody)
            {
                // Keep the wide gi/pelvis center on the hips instead of
                // averaging it into whichever upper-leg surface is nearest.
                weights[i] = SingleBone(1);
            }
            else if (normalizedY < 0.51f)
            {
                float leftLegDistance = Mathf.Min(
                    DistanceToSegment(vertex, leftHip3, leftKnee3),
                    DistanceToSegment(vertex, leftKnee3, leftFoot3));
                float rightLegDistance = Mathf.Min(
                    DistanceToSegment(vertex, rightHip3, rightKnee3),
                    DistanceToSegment(vertex, rightKnee3, rightFoot3));
                weights[i] = BlendedLegWeight(
                    vertex,
                    leftHip3, leftKnee3, leftFoot3,
                    rightHip3, rightKnee3, rightFoot3,
                    leftLegDistance, rightLegDistance);
            }
            else
            {
                if (normalizedY < 0.62f)
                {
                    weights[i] = SingleBone(1);
                }
                else if (normalizedY < 0.72f)
                {
                    weights[i] = SingleBone(2);
                }
                else
                {
                    weights[i] = SingleBone(3);
                }
            }
        }

        if (identity == BodybuilderIdentity.Manwithsuit1)
        {
            BindSuitRightHandComponents(
                vertices, triangles, bounds, weights,
                rightShoulder3, rightElbow3, rightHand3);
        }
        return weights;
    }

    private static void BindSuitRightHandComponents(
        Vector3[] vertices, int[] triangles, Bounds bounds, BoneWeight[] weights,
        Vector3 rightShoulder, Vector3 rightElbow, Vector3 rightHand)
    {
        int[] parents = new int[vertices.Length];
        int[] counts = new int[vertices.Length];
        Vector3[] sums = new Vector3[vertices.Length];
        Vector3[] minima = new Vector3[vertices.Length];
        Vector3[] maxima = new Vector3[vertices.Length];
        for (int i = 0; i < parents.Length; i++)
        {
            parents[i] = i;
            minima[i] = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
            maxima[i] = new Vector3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);
        }

        for (int i = 0; i + 2 < triangles.Length; i += 3)
        {
            UnionVertices(parents, triangles[i], triangles[i + 1]);
            UnionVertices(parents, triangles[i], triangles[i + 2]);
        }

        for (int i = 0; i < vertices.Length; i++)
        {
            int root = FindVertexRoot(parents, i);
            counts[root]++;
            sums[root] += vertices[i];
            minima[root] = Vector3.Min(minima[root], vertices[i]);
            maxima[root] = Vector3.Max(maxima[root], vertices[i]);
        }

        bool[] bindToArm = new bool[vertices.Length];
        bool[] bindHeldObject = new bool[vertices.Length];
        float height = bounds.size.y;
        for (int i = 0; i < vertices.Length; i++)
        {
            if (counts[i] == 0)
            {
                continue;
            }

            Vector3 center = sums[i] / counts[i];
            float normalizedY = (center.y - bounds.min.y) / height;
            float verticalSpan = maxima[i].y - minima[i].y;
            bindHeldObject[i] = counts[i] <= 2200 &&
                normalizedY > 0.68f && normalizedY < 0.82f &&
                center.z > bounds.center.z + height * 0.12f &&
                verticalSpan < height * 0.16f;
            bindToArm[i] = bindHeldObject[i] ||
                (counts[i] <= 2200 &&
                center.x > bounds.center.x + height * 0.065f &&
                normalizedY > 0.55f && normalizedY < 0.82f &&
                verticalSpan < height * 0.25f);
        }

        for (int i = 0; i < vertices.Length; i++)
        {
            int root = FindVertexRoot(parents, i);
            if (bindToArm[root])
            {
                float normalizedY = (vertices[i].y - bounds.min.y) / height;
                bool distal = vertices[i].x > bounds.center.x + height * 0.075f &&
                    normalizedY > 0.56f && normalizedY < 0.82f;
                weights[i] = bindHeldObject[root] || distal
                    ? SingleBone(14)
                    : ArmWeightWithHand(vertices[i], rightShoulder, rightElbow, rightHand, 7, 8, 14);
            }

        }
    }

    private static bool[] MarkGokuHeadComponents(
        Vector3[] vertices, int[] triangles, Bounds bounds)
    {
        bool[] headVertices = new bool[vertices.Length];
        if (vertices.Length == 0 || triangles.Length < 3)
        {
            return headVertices;
        }

        int[] parents = new int[vertices.Length];
        for (int i = 0; i < parents.Length; i++)
        {
            parents[i] = i;
        }
        for (int i = 0; i + 2 < triangles.Length; i += 3)
        {
            UnionVertices(parents, triangles[i], triangles[i + 1]);
            UnionVertices(parents, triangles[i], triangles[i + 2]);
        }

        Dictionary<int, float> componentMaxY = new Dictionary<int, float>();
        for (int i = 0; i < vertices.Length; i++)
        {
            int root = FindVertexRoot(parents, i);
            if (!componentMaxY.TryGetValue(root, out float maximumY) ||
                vertices[i].y > maximumY)
            {
                componentMaxY[root] = vertices[i].y;
            }
        }

        float headThreshold = bounds.min.y + bounds.size.y * 0.78f;
        for (int i = 0; i < vertices.Length; i++)
        {
            int root = FindVertexRoot(parents, i);
            if (componentMaxY[root] >= headThreshold)
            {
                // Hair spikes and rear hair/gi islands are disconnected from
                // the face. Binding the complete island to Head prevents its
                // lower vertices from being accidentally assigned to an arm
                // just because the island crosses the arm height band.
                headVertices[i] = true;
            }
        }
        return headVertices;
    }

    private static int FindVertexRoot(int[] parents, int index)
    {
        while (parents[index] != index)
        {
            parents[index] = parents[parents[index]];
            index = parents[index];
        }
        return index;
    }

    private static void UnionVertices(int[] parents, int first, int second)
    {
        int firstRoot = FindVertexRoot(parents, first);
        int secondRoot = FindVertexRoot(parents, second);
        if (firstRoot != secondRoot)
        {
            parents[secondRoot] = firstRoot;
        }
    }

    private static BoneWeight BlendedLegWeight(
        Vector3 point,
        Vector3 leftHip, Vector3 leftKnee, Vector3 leftFoot,
        Vector3 rightHip, Vector3 rightKnee, Vector3 rightFoot,
        float leftDistance, float rightDistance)
    {
        BoneWeight left = SegmentWeight(point, leftHip, leftKnee, leftFoot, 9, 10);
        BoneWeight right = SegmentWeight(point, rightHip, rightKnee, rightFoot, 11, 12);
        float distanceTotal = Mathf.Max(0.0001f, leftDistance + rightDistance);
        float leftShare = Mathf.SmoothStep(0f, 1f, rightDistance / distanceTotal);
        float rightShare = 1f - leftShare;
        return new BoneWeight
        {
            boneIndex0 = left.boneIndex0,
            weight0 = left.weight0 * leftShare,
            boneIndex1 = left.boneIndex1,
            weight1 = left.weight1 * leftShare,
            boneIndex2 = right.boneIndex0,
            weight2 = right.weight0 * rightShare,
            boneIndex3 = right.boneIndex1,
            weight3 = right.weight1 * rightShare
        };
    }

    private static BoneWeight ArmWeightWithHand(
        Vector3 point, Vector3 shoulder, Vector3 elbow, Vector3 hand,
        int upperBone, int forearmBone, int handBone)
    {
        float upperDistance = DistanceToSegment(point, shoulder, elbow);
        float lowerDistance = DistanceToSegment(point, elbow, hand);
        float total = Mathf.Max(0.0001f, upperDistance + lowerDistance);
        float upperWeight = Mathf.Clamp01(lowerDistance / total);
        Vector3 forearm = hand - elbow;
        float projection = forearm.sqrMagnitude > 0.000001f
            ? Mathf.Clamp01(Vector3.Dot(point - elbow, forearm) / forearm.sqrMagnitude)
            : 0f;
        float handInfluence = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.54f, 0.88f, projection));
        float lowerWeight = 1f - upperWeight;
        return new BoneWeight
        {
            boneIndex0 = upperBone,
            weight0 = upperWeight,
            boneIndex1 = forearmBone,
            weight1 = lowerWeight * (1f - handInfluence),
            boneIndex2 = handBone,
            weight2 = lowerWeight * handInfluence
        };
    }

    private static BoneWeight ZyzzArmWeight(
        Vector3 point, Vector3 shoulder, Vector3 elbow, Vector3 hand,
        int upperBone, int lowerBone, float height)
    {
        float upperDistance = DistanceToSegment(point, shoulder, elbow);
        float lowerDistance = DistanceToSegment(point, elbow, hand);
        float shoulderInfluence = Mathf.SmoothStep(
            0f, 1f, Vector3.Distance(point, shoulder) / Mathf.Max(0.0001f, height * 0.09f));
        float armInfluence = Mathf.Clamp01(shoulderInfluence);

        float totalDistance = Mathf.Max(0.0001f, upperDistance + lowerDistance);
        float upperShare = Mathf.Clamp01(lowerDistance / totalDistance);
        return new BoneWeight
        {
            boneIndex0 = upperBone,
            weight0 = armInfluence * upperShare,
            boneIndex1 = lowerBone,
            weight1 = armInfluence * (1f - upperShare),
            boneIndex2 = 3,
            weight2 = 1f - armInfluence
        };
    }

    private static Vector2 ToPlanePoint(Bounds bounds, Vector2 normalizedPoint)
    {
        Vector3 point = ToModelPoint(bounds, normalizedPoint);
        return new Vector2(point.x, point.y);
    }

    private static BoneWeight SingleBone(int boneIndex)
    {
        return new BoneWeight { boneIndex0 = boneIndex, weight0 = 1f };
    }

    private static BoneWeight SegmentWeight(Vector2 point, Vector2 start, Vector2 joint, Vector2 end, int firstBone, int secondBone)
    {
        float firstDistance = DistanceToSegment(point, start, joint);
        float secondDistance = DistanceToSegment(point, joint, end);
        float total = Mathf.Max(0.0001f, firstDistance + secondDistance);
        float firstWeight = Mathf.Clamp01(secondDistance / total);
        return new BoneWeight
        {
            boneIndex0 = firstBone,
            weight0 = firstWeight,
            boneIndex1 = secondBone,
            weight1 = 1f - firstWeight
        };
    }

    private static float DistanceToSegment(Vector2 point, Vector2 start, Vector2 end)
    {
        Vector2 segment = end - start;
        float lengthSquared = segment.sqrMagnitude;
        if (lengthSquared < 0.000001f)
        {
            return Vector2.Distance(point, start);
        }

        float t = Mathf.Clamp01(Vector2.Dot(point - start, segment) / lengthSquared);
        return Vector2.Distance(point, start + segment * t);
    }

    private static BoneWeight SegmentWeight(Vector3 point, Vector3 start, Vector3 joint, Vector3 end, int firstBone, int secondBone)
    {
        float firstDistance = DistanceToSegment(point, start, joint);
        float secondDistance = DistanceToSegment(point, joint, end);
        float total = Mathf.Max(0.0001f, firstDistance + secondDistance);
        float firstWeight = Mathf.Clamp01(secondDistance / total);
        return new BoneWeight
        {
            boneIndex0 = firstBone,
            weight0 = firstWeight,
            boneIndex1 = secondBone,
            weight1 = 1f - firstWeight
        };
    }

    private static float DistanceToSegment(Vector3 point, Vector3 start, Vector3 end)
    {
        Vector3 segment = end - start;
        float lengthSquared = segment.sqrMagnitude;
        if (lengthSquared < 0.000001f)
        {
            return Vector3.Distance(point, start);
        }

        float t = Mathf.Clamp01(Vector3.Dot(point - start, segment) / lengthSquared);
        return Vector3.Distance(point, start + segment * t);
    }

    private static Material CreateBodyMaterial(GltfRoot gltf, byte[] binary, BodybuilderIdentity identity)
    {
        Shader shader = Shader.Find("GymChaos/BodybuilderUnlit");
        if (shader == null)
        {
            shader = Shader.Find("Universal Render Pipeline/Unlit");
        }
        if (shader == null)
        {
            shader = Shader.Find("Universal Render Pipeline/Lit");
        }
        if (shader == null)
        {
            Debug.LogError($"No runtime body shader is available for {identity}; keeping the renderer unassigned.");
            return null;
        }

        Material material = new Material(shader) { name = identity + " Body Material" };
        if (gltf.images != null && gltf.images.Length > 0)
        {
            GltfBufferView view = gltf.bufferViews[gltf.images[0].bufferView];
            byte[] imageBytes = new byte[view.byteLength];
            Buffer.BlockCopy(binary, view.byteOffset, imageBytes, 0, view.byteLength);
            Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, true)
            {
                name = identity + " Body Texture",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Repeat
            };
            texture.LoadImage(imageBytes);
            texture = DownscaleTexture(texture, 1024, identity + " Body Texture Low");
            material.SetTexture("_BaseMap", texture);
            material.mainTexture = texture;
        }

        material.SetColor("_BaseColor", Color.white);
        return material;
    }

    private static Texture2D DownscaleTexture(Texture2D source, int maximumSize, string textureName)
    {
        if (source.width <= maximumSize && source.height <= maximumSize)
        {
            source.Apply(true, true);
            return source;
        }

        float scale = maximumSize / (float)Mathf.Max(source.width, source.height);
        int width = Mathf.Max(4, Mathf.RoundToInt(source.width * scale));
        int height = Mathf.Max(4, Mathf.RoundToInt(source.height * scale));
        RenderTexture temporary = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32);
        Graphics.Blit(source, temporary);
        RenderTexture previous = RenderTexture.active;
        RenderTexture.active = temporary;
        Texture2D reduced = new Texture2D(width, height, TextureFormat.RGBA32, true)
        {
            name = textureName,
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Repeat
        };
        reduced.ReadPixels(new Rect(0f, 0f, width, height), 0, 0, false);
        reduced.Apply(true, true);
        RenderTexture.active = previous;
        RenderTexture.ReleaseTemporary(temporary);
        Destroy(source);
        return reduced;
    }

    private static void RemoveCollider(GameObject target)
    {
        Collider collider = target.GetComponent<Collider>();
        if (collider != null)
        {
            DestroyImmediate(collider);
        }
    }

    private static void CreateFaceCensorAndName(
        Transform visualRoot, Vector3[] vertices, Bounds bounds, RigProfile rigProfile,
        Transform head, SkinnedMeshRenderer bodyRenderer, BodybuilderIdentity identity,
        FaceCensorProfile? importedProfile = null)
    {
        float bodyHeight = bodyRenderer.bounds.size.y;
        GameObject censorObject = new GameObject(identity + " Black Eye Bar");
        FaceCensorSettings censor = censorObject.AddComponent<FaceCensorSettings>();
        censor.Configure(
            importedProfile ?? GetFaceCensorProfile(identity, vertices, bounds, rigProfile, visualRoot, head),
            head, identity.GetHashCode() + 31, true);

        EnemyFighter fighter = visualRoot.GetComponentInParent<EnemyFighter>();
        if (fighter != null && fighter.IsDead)
        {
            censor.SetDead(true);
        }

        CreateNameLabel(visualRoot, bodyRenderer, identity, bodyHeight * 0.13f);
    }

    private static void CreateDeathMarkersAndName(
        Transform visualRoot, Vector3[] vertices, Bounds bounds, RigProfile rigProfile,
        Transform head, SkinnedMeshRenderer bodyRenderer, BodybuilderIdentity identity,
        FaceCensorProfile? importedProfile = null)
    {
        FaceCensorProfile markerProfile = importedProfile ?? GetFaceCensorProfile(
            identity, vertices, bounds, rigProfile, visualRoot, head);
        if (identity == BodybuilderIdentity.Manwithsuit1)
        {
            Vector3 profileForwardLocal =
                Quaternion.Euler(markerProfile.LocalEulerAngles) * Vector3.forward;
            Vector3 profileDepthWorld = head.TransformDirection(profileForwardLocal)
                * markerProfile.FaceDepth;
            markerProfile.LocalPosition += new Vector3(
                0f, -bodyRenderer.bounds.size.y * 0.025f, 0f) +
                head.InverseTransformVector(profileDepthWorld);
            markerProfile.FaceDepth = 0f;
        }

        GameObject markerAnchor = new GameObject(identity + " Death Eye Marker Anchor");
        FaceCensorSettings censor = markerAnchor.AddComponent<FaceCensorSettings>();
        censor.Configure(
            markerProfile, head, identity.GetHashCode() + 31, false);

        EnemyFighter fighter = visualRoot.GetComponentInParent<EnemyFighter>();
        if (fighter != null && fighter.IsDead)
        {
            censor.SetDead(true);
        }

        CreateNameLabel(visualRoot, bodyRenderer, identity, bodyRenderer.bounds.size.y * 0.13f);
    }

    private static FaceCensorProfile GetImportedFaceCensorProfile(
        BodybuilderIdentity identity, Vector3[] vertices, Bounds bounds,
        RigProfile rigProfile, Transform visualRoot, Transform head, float worldHeight)
    {
        float localHeight = Mathf.Max(0.1f, bounds.size.y);
        // The normalized full-body FaceY profile belongs to the old
        // procedural rig. The imported FBX has its own authored Head bone;
        // use that asset-space position as the vertical eye anchor so a
        // different scan cannot move the bar up into the forehead.
        Vector3 headLocal = visualRoot.InverseTransformPoint(head.position);
        float importedEyeOffset = 0f;
        switch (identity)
        {
            // These are small asset-specific corrections from the visible
            // FBX face proportions. Keep them local to the imported profile;
            // the old large offsets moved the bars below the face.
            case BodybuilderIdentity.Cbum:
                importedEyeOffset = -0.040f;
                break;
            case BodybuilderIdentity.Zyzz:
                importedEyeOffset = -0.022f;
                break;
            case BodybuilderIdentity.Arnold:
                importedEyeOffset = -0.033f;
                break;
            case BodybuilderIdentity.Ronnie:
                importedEyeOffset = -0.017f;
                break;
            case BodybuilderIdentity.JayCutler:
                importedEyeOffset = -0.025f;
                break;
            case BodybuilderIdentity.Goku:
                importedEyeOffset = -0.020f;
                break;
        }
        float eyeY = headLocal.y + localHeight *
            (ImportedEyeLineBaseOffset + importedEyeOffset);

        float height = Mathf.Max(0.1f, worldHeight);
        Vector2 size;
        float coverage;
        switch (identity)
        {
            case BodybuilderIdentity.Cbum:
                size = new Vector2(height * 0.125f, height * 0.028f);
                coverage = 68f;
                break;
            case BodybuilderIdentity.Zyzz:
                size = new Vector2(height * 0.135f, height * 0.028f);
                coverage = 70f;
                break;
            case BodybuilderIdentity.Ronnie:
                size = new Vector2(height * 0.128f, height * 0.029f);
                coverage = 69f;
                break;
            case BodybuilderIdentity.Goku:
                size = new Vector2(height * 0.165f, height * 0.038f);
                coverage = 72f;
                break;
            default:
                size = new Vector2(height * 0.128f, height * 0.029f);
                coverage = 69f;
                break;
        }

        // Keep the lateral correction in the asset's model space, but sample
        // depth in the actual head frame. This handles identities whose head
        // sits farther forward/back or tilts relative to the body root.
        float faceCenterX = headLocal.x;
        switch (identity)
        {
            case BodybuilderIdentity.Ronnie:
                faceCenterX += localHeight * 0.0095f;
                break;
            case BodybuilderIdentity.Zyzz:
                faceCenterX += 0.001f;
                break;
        }

        Vector3 faceDirectionWorld = head.forward;
        if (faceDirectionWorld.sqrMagnitude < 0.0001f)
        {
            faceDirectionWorld = visualRoot.forward;
        }
        faceDirectionWorld.Normalize();
        if (Vector3.Dot(faceDirectionWorld, visualRoot.forward) < 0f)
        {
            faceDirectionWorld = -faceDirectionWorld;
        }

        Vector3 faceUpWorld = head.up;
        if (faceUpWorld.sqrMagnitude < 0.0001f)
        {
            faceUpWorld = Vector3.up;
        }
        faceUpWorld = Vector3.ProjectOnPlane(faceUpWorld, faceDirectionWorld).normalized;
        if (faceUpWorld.sqrMagnitude < 0.0001f)
        {
            faceUpWorld = Vector3.up;
        }
        Vector3 faceRightWorld = Vector3.Cross(faceUpWorld, faceDirectionWorld).normalized;
        if (faceRightWorld.sqrMagnitude < 0.0001f)
        {
            faceRightWorld = head.right.normalized;
        }
        faceUpWorld = Vector3.Cross(faceDirectionWorld, faceRightWorld).normalized;
        if (Vector3.Dot(faceUpWorld, head.up) < 0f)
        {
            faceRightWorld = -faceRightWorld;
            faceUpWorld = -faceUpWorld;
        }

        Vector3 eyeAnchorLocal = new Vector3(faceCenterX, eyeY, headLocal.z);
        Vector3 eyeAnchorWorld = visualRoot.TransformPoint(eyeAnchorLocal);
        float eyeHalfHeight = Mathf.Max(localHeight * 0.022f, size.y * 0.65f);
        float eyeHalfWidth = Mathf.Max(
            localHeight * rigProfile.HeadHalfWidth * 0.95f, size.x * 0.6f);
        float frontDepth = float.NegativeInfinity;
        float backDepth = float.PositiveInfinity;
        for (int i = 0; i < vertices.Length; i++)
        {
            Vector3 vertexWorld = visualRoot.TransformPoint(vertices[i]);
            Vector3 delta = vertexWorld - eyeAnchorWorld;
            float horizontal = Vector3.Dot(delta, faceRightWorld);
            float vertical = Vector3.Dot(delta, faceUpWorld);
            if (Mathf.Abs(vertical) > eyeHalfHeight ||
                Mathf.Abs(horizontal) > eyeHalfWidth)
            {
                continue;
            }

            float depth = Vector3.Dot(delta, faceDirectionWorld);
            frontDepth = Mathf.Max(frontDepth, depth);
            backDepth = Mathf.Min(backDepth, depth);
        }

        bool sampledFace = !float.IsNegativeInfinity(frontDepth) &&
            !float.IsPositiveInfinity(backDepth);
        if (!sampledFace)
        {
            frontDepth = Vector3.Dot(
                visualRoot.TransformPoint(new Vector3(faceCenterX, eyeY, bounds.max.z)) -
                eyeAnchorWorld,
                faceDirectionWorld);
            backDepth = frontDepth - localHeight * 0.01f;
        }

        Vector3 faceSurfaceWorld = eyeAnchorWorld + faceDirectionWorld * frontDepth;
        float faceSurfaceZ = visualRoot.InverseTransformPoint(faceSurfaceWorld).z;
        float faceSpread = Mathf.Max(0f, frontDepth - backDepth);
        float arcDrop = 1f - Mathf.Cos(Mathf.Clamp(coverage, 55f, 82f) * Mathf.Deg2Rad);
        float faceDepth = Mathf.Clamp(
            faceSpread / Mathf.Max(0.2f, arcDrop),
            height * 0.0015f,
            height * 0.08f);

        // The shell's centre lands exactly on the sampled front boundary;
        // its curved sides follow the asset's measured depth spread instead
        // of floating in front of the face or following a collision box.
        Vector3 barOriginWorld = faceSurfaceWorld - faceDirectionWorld * faceDepth;
        Vector3 faceDirectionLocal = head.InverseTransformDirection(faceDirectionWorld).normalized;
        Vector3 upLocal = head.InverseTransformDirection(faceUpWorld).normalized;
        Quaternion faceRotation = Quaternion.LookRotation(faceDirectionLocal, upLocal);

        Vector3 localPosition = head.InverseTransformPoint(barOriginWorld);
        Debug.Log(
            $"FACE_GEOMETRY_DEBUG identity={identity} root={visualRoot.position} " +
            $"rootScale={visualRoot.lossyScale} rootForward={visualRoot.forward} " +
            $"head={head.position} headForward={head.forward} localBounds={bounds.min}/{bounds.max} " +
            $"eyeLocal={eyeY:F3} surfaceLocalZ={faceSurfaceZ:F3} surfaceWorld={faceSurfaceWorld} " +
            $"frontDepth={frontDepth:F4} backDepth={backDepth:F4} " +
            $"faceDepth={faceDepth:F4} barOrigin={barOriginWorld} profileLocal={localPosition}");
        return new FaceCensorProfile(
            localPosition, faceRotation.eulerAngles, size, faceDepth, coverage, Color.black);
    }

    private static FaceCensorProfile GetFaceCensorProfile(
        BodybuilderIdentity identity, Vector3[] vertices, Bounds bounds,
        RigProfile rigProfile, Transform visualRoot, Transform head)
    {
        float height = bounds.size.y;
        float eyeY = bounds.min.y + height * rigProfile.FaceY;
        switch (identity)
        {
            case BodybuilderIdentity.Cbum:
                eyeY += height * 0.035f;
                break;
            case BodybuilderIdentity.Zyzz:
                eyeY += height * 0.00575f;
                break;
            case BodybuilderIdentity.Ronnie:
                eyeY += height * 0.035f;
                break;
            case BodybuilderIdentity.Goku:
                // Goku's scan uses a lower eye line than the generic head
                // profile; do not apply the default forehead offset.
                break;
            default:
                eyeY += height * 0.05f;
                break;
        }
        float faceSurfaceZ = float.NegativeInfinity;
        float eyeHalfHeight = height * 0.022f;
        float eyeHalfWidth = height * rigProfile.HeadHalfWidth * 0.95f;
        for (int i = 0; i < vertices.Length; i++)
        {
            Vector3 vertex = vertices[i];
            if (Mathf.Abs(vertex.y - eyeY) <= eyeHalfHeight &&
                Mathf.Abs(vertex.x - bounds.center.x) <= eyeHalfWidth)
            {
                faceSurfaceZ = Mathf.Max(faceSurfaceZ, vertex.z);
            }
        }
        if (float.IsNegativeInfinity(faceSurfaceZ))
        {
            faceSurfaceZ = bounds.max.z;
        }

        Vector3 faceCenter = new Vector3(
            bounds.center.x,
            eyeY,
            faceSurfaceZ + 0.0015f);
        Vector3 localPosition = head.InverseTransformPoint(visualRoot.TransformPoint(faceCenter));
        switch (identity)
        {
            case BodybuilderIdentity.Cbum:
                return new FaceCensorProfile(localPosition + new Vector3(0f, 0f, -height * 0.019f), Vector3.zero,
                    new Vector2(height * 0.125f, height * 0.028f), height * 0.019f, 68f, Color.black);
            case BodybuilderIdentity.Zyzz:
                return new FaceCensorProfile(localPosition + new Vector3(0.001f, 0f, -height * 0.017f), new Vector3(0f, -1f, 0f),
                    new Vector2(height * 0.135f, height * 0.028f), height * 0.017f, 70f, Color.black);
            case BodybuilderIdentity.Ronnie:
                // Ronnie's scanned face center sits about 0.95% of body height
                // to model-right of the full-mesh bounds center at eye level.
                return new FaceCensorProfile(localPosition + new Vector3(height * 0.0095f, 0f, -height * 0.02f), Vector3.zero,
                    new Vector2(height * 0.128f, height * 0.029f), height * 0.02f, 69f, Color.black);
            case BodybuilderIdentity.Goku:
                // Goku's scan places the visible eyes lower than the generic
                // head profile. Make his censor wider/taller and lower it onto
                // the eye line without changing the other fighters' bars.
                return new FaceCensorProfile(localPosition + new Vector3(0f, -height * 0.012f, -height * 0.022f), new Vector3(0f, 1f, 0f),
                    new Vector2(height * 0.165f, height * 0.038f), height * 0.022f, 72f, Color.black);
            default:
                return new FaceCensorProfile(localPosition + new Vector3(-0.001f, 0f, -height * 0.02f), new Vector3(0f, 1f, 0f),
                    new Vector2(height * 0.128f, height * 0.029f), height * 0.02f, 69f, Color.black);
        }
    }

    private static void CreateNameLabel(
        Transform visualRoot, SkinnedMeshRenderer bodyRenderer,
        BodybuilderIdentity identity, float heightOffset)
    {
        string displayName = identity == BodybuilderIdentity.Cbum
            ? "CBum"
            : identity == BodybuilderIdentity.Manwithsuit1
                ? "manwithsuit1"
                : identity == BodybuilderIdentity.JayCutler
                    ? "Jay Cutler"
                : identity.ToString();
        ScreenSpaceCharacterLabel label = visualRoot.gameObject.AddComponent<ScreenSpaceCharacterLabel>();
        label.Configure(bodyRenderer, displayName, heightOffset);
    }

    private static bool TryReadGlb(byte[] bytes, out GltfRoot gltf, out byte[] binary)
    {
        gltf = null;
        binary = null;
        if (bytes == null || bytes.Length < 20 || BitConverter.ToUInt32(bytes, 0) != GlbMagic)
        {
            return false;
        }

        int offset = 12;
        string json = null;
        while (offset + 8 <= bytes.Length)
        {
            int length = (int)BitConverter.ToUInt32(bytes, offset);
            uint type = BitConverter.ToUInt32(bytes, offset + 4);
            offset += 8;
            if (length < 0 || offset + length > bytes.Length)
            {
                return false;
            }

            if (type == JsonChunk)
            {
                json = Encoding.UTF8.GetString(bytes, offset, length).TrimEnd('\0', ' ', '\n', '\r', '\t');
            }
            else if (type == BinaryChunk)
            {
                binary = new byte[length];
                Buffer.BlockCopy(bytes, offset, binary, 0, length);
            }
            offset += length;
        }

        if (string.IsNullOrEmpty(json) || binary == null)
        {
            return false;
        }

        gltf = JsonUtility.FromJson<GltfRoot>(json);
        return gltf != null && gltf.meshes != null && gltf.meshes.Length > 0;
    }

    private static Vector3[] ReadVector3Accessor(GltfRoot gltf, byte[] binary, int accessorIndex)
    {
        GltfAccessor accessor = gltf.accessors[accessorIndex];
        GltfBufferView view = gltf.bufferViews[accessor.bufferView];
        int stride = view.byteStride > 0 ? view.byteStride : 12;
        int start = view.byteOffset + accessor.byteOffset;
        Vector3[] result = new Vector3[accessor.count];
        for (int i = 0; i < result.Length; i++)
        {
            int offset = start + i * stride;
            result[i] = new Vector3(BitConverter.ToSingle(binary, offset), BitConverter.ToSingle(binary, offset + 4), BitConverter.ToSingle(binary, offset + 8));
        }
        return result;
    }

    private static Vector2[] ReadVector2Accessor(GltfRoot gltf, byte[] binary, int accessorIndex)
    {
        GltfAccessor accessor = gltf.accessors[accessorIndex];
        GltfBufferView view = gltf.bufferViews[accessor.bufferView];
        int stride = view.byteStride > 0 ? view.byteStride : 8;
        int start = view.byteOffset + accessor.byteOffset;
        Vector2[] result = new Vector2[accessor.count];
        for (int i = 0; i < result.Length; i++)
        {
            int offset = start + i * stride;
            result[i] = new Vector2(BitConverter.ToSingle(binary, offset), BitConverter.ToSingle(binary, offset + 4));
        }
        return result;
    }

    private static int[] ReadIndexAccessor(GltfRoot gltf, byte[] binary, int accessorIndex)
    {
        GltfAccessor accessor = gltf.accessors[accessorIndex];
        GltfBufferView view = gltf.bufferViews[accessor.bufferView];
        int componentSize = accessor.componentType == 5125 ? 4 : accessor.componentType == 5123 ? 2 : 1;
        int stride = view.byteStride > 0 ? view.byteStride : componentSize;
        int start = view.byteOffset + accessor.byteOffset;
        int[] result = new int[accessor.count];
        for (int i = 0; i < result.Length; i++)
        {
            int offset = start + i * stride;
            result[i] = accessor.componentType == 5125
                ? (int)BitConverter.ToUInt32(binary, offset)
                : accessor.componentType == 5123
                    ? BitConverter.ToUInt16(binary, offset)
                    : binary[offset];
        }
        return result;
    }
}
