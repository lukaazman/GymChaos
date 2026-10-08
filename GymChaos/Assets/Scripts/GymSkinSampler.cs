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
        for (int i = 0; i < vertices.Length; i++)
        {
            vertices[i] = toWorld.MultiplyPoint3x4(vertices[i]);
        }
        return vertices;
    }

    public static bool IsInChain(Transform bone, Transform chainRoot)
    {
        return bone != null && chainRoot != null &&
            (bone == chainRoot || bone.IsChildOf(chainRoot));
    }
}
