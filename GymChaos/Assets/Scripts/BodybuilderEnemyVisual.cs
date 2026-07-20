using System;
using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

public enum BodybuilderIdentity
{
    Arnold,
    Cbum,
    Zyzz,
    Ronnie,
    Manwithsuit1
}

public sealed class BodybuilderEnemyVisual : MonoBehaviour
{
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
        public Transform Head;
        public Transform LeftUpperArm;
        public Transform LeftForearm;
        public Transform RightUpperArm;
        public Transform RightForearm;
        public Transform LeftThigh;
        public Transform LeftShin;
        public Transform RightThigh;
        public Transform RightShin;
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
            LeftHand, RightHand
        };
    }

    public static BodybuilderEnemyVisual Build(GameObject enemy, BodybuilderIdentity identity)
    {
        BodybuilderEnemyVisual visual = enemy.AddComponent<BodybuilderEnemyVisual>();
        visual.StartCoroutine(visual.BuildVisual(identity, false));
        return visual;
    }

    public static BodybuilderEnemyVisual BuildNeutralNpc(GameObject npc, BodybuilderIdentity identity)
    {
        BodybuilderEnemyVisual visual = npc.AddComponent<BodybuilderEnemyVisual>();
        visual.StartCoroutine(visual.BuildVisual(identity, true));
        return visual;
    }

    private IEnumerator BuildVisual(BodybuilderIdentity identity, bool neutralNpc)
    {
        // Stagger the large scans so parsing and mesh upload do not land in one frame.
        int delayFrames = (int)identity * 2 + 1;
        for (int i = 0; i < delayFrames; i++)
        {
            yield return null;
        }

        string fileName = identity.ToString().ToLowerInvariant() + ".glb";
        string path = Path.Combine(Application.streamingAssetsPath, "BodyBuilders", fileName);
        if (!File.Exists(path))
        {
            Debug.LogError($"Bodybuilder model is missing: {path}", this);
            yield break;
        }

        if (!TryReadGlb(File.ReadAllBytes(path), out GltfRoot gltf, out byte[] binary))
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

        if (neutralNpc)
        {
            CreateNameLabel(renderObject.transform, renderer, identity, bounds.size.y * 0.13f);
            ManWithSuitIdleAnimator animator = gameObject.AddComponent<ManWithSuitIdleAnimator>();
            animator.Configure(rig);
        }
        else
        {
            CreateFaceCensorAndName(
                renderObject.transform, positions, bounds, profile,
                rig.Head, renderer, identity);
            BodybuilderEnemyAnimator animator = gameObject.AddComponent<BodybuilderEnemyAnimator>();
            animator.Configure(identity, rig);
        }
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
            default:
                return new RigProfile(
                    new Vector2(-0.10f, 0.70f), new Vector2(-0.14f, 0.55f), new Vector2(-0.13f, 0.40f),
                    new Vector2(0.10f, 0.70f), new Vector2(0.13f, 0.58f), new Vector2(0.07f, 0.43f),
                    new Vector2(-0.045f, 0.49f), new Vector2(-0.04f, 0.25f), new Vector2(0.00f, 0.02f),
                    new Vector2(0.045f, 0.49f), new Vector2(0.06f, 0.25f), new Vector2(0.075f, 0.02f),
                    0.79f, 0.08f, 0.10f, 0.87f, -0.085f, 0.08f,
                    0f, -0.025f, -0.08f, 0f, 0.08f, 0.15f);
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

        rig.LeftUpperArm = CreateBone("Left Upper Arm", rig.Chest, visualRoot, ToModelPoint(bounds, profile.LeftShoulder));
        rig.LeftForearm = CreateBone("Left Forearm", rig.LeftUpperArm, visualRoot, ToModelPoint(bounds, profile.LeftElbow, profile.LeftArmZ * 0.55f));
        rig.RightUpperArm = CreateBone("Right Upper Arm", rig.Chest, visualRoot, ToModelPoint(bounds, profile.RightShoulder));
        rig.RightForearm = CreateBone("Right Forearm", rig.RightUpperArm, visualRoot, ToModelPoint(bounds, profile.RightElbow, profile.RightArmZ * 0.55f));

        rig.LeftThigh = CreateBone("Left Thigh", rig.Hips, visualRoot, ToModelPoint(bounds, profile.LeftHip, profile.LeftHipZ));
        rig.LeftShin = CreateBone("Left Shin", rig.LeftThigh, visualRoot, ToModelPoint(bounds, profile.LeftKnee, profile.LeftKneeZ));
        rig.RightThigh = CreateBone("Right Thigh", rig.Hips, visualRoot, ToModelPoint(bounds, profile.RightHip, profile.RightHipZ));
        rig.RightShin = CreateBone("Right Shin", rig.RightThigh, visualRoot, ToModelPoint(bounds, profile.RightKnee, profile.RightKneeZ));
        rig.LeftHandPosition = ToModelPoint(bounds, profile.LeftHand, profile.LeftArmZ);
        rig.RightHandPosition = ToModelPoint(bounds, profile.RightHand, profile.RightArmZ);
        rig.LeftHand = CreateBone(
            "Left Hand", rig.LeftForearm, visualRoot,
            Vector3.Lerp(ToModelPoint(bounds, profile.LeftElbow, profile.LeftArmZ * 0.55f), rig.LeftHandPosition, 0.72f));
        rig.RightHand = CreateBone(
            "Right Hand", rig.RightForearm, visualRoot,
            Vector3.Lerp(ToModelPoint(bounds, profile.RightElbow, profile.RightArmZ * 0.55f), rig.RightHandPosition, 0.72f));
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
            bool suitRightDistal = identity == BodybuilderIdentity.Manwithsuit1 &&
                vertex.x > bounds.center.x + height * 0.135f &&
                normalizedY > 0.52f && normalizedY < 0.73f &&
                Vector3.Distance(vertex, rightHand3) < height * 0.18f;

            if (normalizedY >= profile.NeckY && Mathf.Abs(vertex.x - bounds.center.x) <= profile.HeadHalfWidth * height)
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
            else if (identity == BodybuilderIdentity.Zyzz && useLeftArm)
            {
                weights[i] = ZyzzArmWeight(
                    vertex, leftShoulder3, leftElbow3, leftHand3, 5, 6, height);
            }
            else if (identity == BodybuilderIdentity.Zyzz && useRightArm)
            {
                weights[i] = ZyzzArmWeight(
                    vertex, rightShoulder3, rightElbow3, rightHand3, 7, 8, height);
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
                if (useLeftArm)
                {
                    weights[i] = SegmentWeight(vertex, leftShoulder3, leftElbow3, leftHand3, 5, 6);
                }
                else if (useRightArm)
                {
                    weights[i] = SegmentWeight(vertex, rightShoulder3, rightElbow3, rightHand3, 7, 8);
                }
                else if (normalizedY < 0.62f)
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
            material.SetTexture("_BaseMap", texture);
            material.mainTexture = texture;
        }

        material.SetColor("_BaseColor", Color.white);
        return material;
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
        Transform head,
        SkinnedMeshRenderer bodyRenderer, BodybuilderIdentity identity)
    {
        float bodyHeight = bounds.size.y;
        GameObject censorObject = new GameObject(identity + " Black Eye Bar");
        FaceCensorSettings censor = censorObject.AddComponent<FaceCensorSettings>();
        censor.Configure(
            GetFaceCensorProfile(identity, vertices, bounds, rigProfile, visualRoot, head),
            head, identity.GetHashCode() + 31);

        EnemyFighter fighter = visualRoot.GetComponentInParent<EnemyFighter>();
        if (fighter != null && fighter.IsDead)
        {
            censor.SetDead(true);
        }

        CreateNameLabel(visualRoot, bodyRenderer, identity, bodyHeight * 0.13f);
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
