using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;

/// <summary>
/// Reads the rendered (baked) skin of a character: world-space vertex
/// positions and each vertex's dominant bone. Unity 6 can import FBX meshes
/// without the legacy BoneWeight array, so the per-vertex weights come from
/// GetBonesPerVertex/GetAllBoneWeights, which are always populated.
/// </summary>
public static class GymSkinSampler
{
    private static readonly Dictionary<Mesh, int[]> DominantBoneCache =
        new Dictionary<Mesh, int[]>();

    public static int[] GetDominantBones(Mesh mesh)
    {
        if (mesh == null)
        {
            return System.Array.Empty<int>();
        }
        if (DominantBoneCache.TryGetValue(mesh, out int[] cached))
        {
            return cached;
        }

        int vertexCount = mesh.vertexCount;
        int[] result = new int[vertexCount];
        NativeArray<byte> perVertex = mesh.GetBonesPerVertex();
        NativeArray<BoneWeight1> weights = mesh.GetAllBoneWeights();
        if (perVertex.Length == vertexCount && weights.Length > 0)
        {
            int cursor = 0;
            for (int i = 0; i < vertexCount; i++)
            {
                int count = perVertex[i];
                int best = -1;
                float bestWeight = -1f;
                for (int j = 0; j < count; j++)
                {
                    BoneWeight1 weight = weights[cursor + j];
                    if (weight.weight > bestWeight)
                    {
                        bestWeight = weight.weight;
                        best = weight.boneIndex;
                    }
                }
                result[i] = best;
                cursor += count;
            }
        }
        else
        {
            for (int i = 0; i < vertexCount; i++)
            {
                result[i] = -1;
            }
        }
        DominantBoneCache[mesh] = result;
        return result;
    }

    /// <summary>Bakes the current pose and returns world-space vertices.</summary>
    public static Vector3[] BakeWorld(SkinnedMeshRenderer skin, Mesh scratch)
    {
        // Unscaled bake: vertices relative to the renderer position/rotation.
        skin.BakeMesh(scratch, false);
        Vector3[] vertices = scratch.vertices;
        Matrix4x4 toWorld = Matrix4x4.TRS(
            skin.transform.position, skin.transform.rotation, Vector3.one);
        // MultiplyPoint3x4 written out: same arithmetic, no per-vertex call.
        float m00 = toWorld.m00, m01 = toWorld.m01, m02 = toWorld.m02, m03 = toWorld.m03;
        float m10 = toWorld.m10, m11 = toWorld.m11, m12 = toWorld.m12, m13 = toWorld.m13;
        float m20 = toWorld.m20, m21 = toWorld.m21, m22 = toWorld.m22, m23 = toWorld.m23;
        for (int i = 0; i < vertices.Length; i++)
        {
            Vector3 p = vertices[i];
            float x = p.x, y = p.y, z = p.z;
            p.x = m00 * x + m01 * y + m02 * z + m03;
            p.y = m10 * x + m11 * y + m12 * z + m13;
            p.z = m20 * x + m21 * y + m22 * z + m23;
            vertices[i] = p;
        }
        return vertices;
    }

    /// <summary>
    /// IsInChain for every entry of <paramref name="bones"/> at once, so vertex
    /// loops test a bool by bone index instead of walking parents per vertex.
    /// </summary>
    public static bool[] ChainMask(Transform[] bones, Transform chainRoot)
    {
        bool[] mask = new bool[bones.Length];
        for (int i = 0; i < bones.Length; i++)
        {
            mask[i] = IsInChain(bones[i], chainRoot);
        }
        return mask;
    }

    public static bool IsInChain(Transform bone, Transform chainRoot)
    {
        return bone != null && chainRoot != null &&
            (bone == chainRoot || bone.IsChildOf(chainRoot));
    }
}
