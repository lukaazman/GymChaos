#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// The Arnold Hummer cabin GLB must not carry interior boxes that hang below
/// the scanned body: the footwell tub once stuck out under the chassis and
/// read as a dark half-transparent box at the front. Every untransformed
/// Cabin__ part has to stay at or above the lowest point of the body shell.
/// </summary>
public static class GymChaosArnoldUnderbodyVerifier
{
    private const string AssetPath = "BodyBuilders/vehicles/Arnold_Hummer_lod.glb";
    private const float Tolerance = 0.01f; // seat cushions sit a few cm into the body, hidden by it

    public static void Run()
    {
        int code = 1;
        try
        {
            string path = Path.Combine(Application.streamingAssetsPath, AssetPath);
            if (!GymGlb.TryReadGlb(File.ReadAllBytes(path), out GltfRoot gltf, out byte[] binary))
                throw new InvalidOperationException("Hummer GLB unreadable");

            float bodyBottom = float.MaxValue;
            foreach (GltfNode node in gltf.nodes)
            {
                if (node.mesh < 0 || node.name != "geometry_0") continue;
                bodyBottom = Mathf.Min(bodyBottom, MinY(gltf, binary, node));
            }
            if (bodyBottom == float.MaxValue) throw new InvalidOperationException("body node geometry_0 missing");

            int checkedParts = 0;
            foreach (GltfNode node in gltf.nodes)
            {
                if (node.mesh < 0 || !node.name.StartsWith("Cabin__", StringComparison.Ordinal)) continue;
                checkedParts++;
                float bottom = MinY(gltf, binary, node);
                if (bottom < bodyBottom - Tolerance)
                    throw new InvalidOperationException(
                        $"{node.name} hangs {bodyBottom - bottom:F3} below the body ({bottom:F3} < {bodyBottom:F3})");
            }
            if (checkedParts == 0) throw new InvalidOperationException("no Cabin__ parts found");
            Debug.Log($"GYMCHAOS_ARNOLD_UNDERBODY_OK parts={checkedParts} bodyBottom={bodyBottom:F3}");
            code = 0;
        }
        catch (Exception exception)
        {
            Debug.LogError("GYMCHAOS_ARNOLD_UNDERBODY_FAIL " + exception.Message);
        }
        EditorApplication.Exit(code);
    }

    private static float MinY(GltfRoot gltf, byte[] binary, GltfNode node)
    {
        float min = float.MaxValue;
        foreach (GltfPrimitive primitive in gltf.meshes[node.mesh].primitives)
        {
            foreach (Vector3 vertex in GymGlb.ReadVector3Accessor(gltf, binary, primitive.attributes.POSITION))
                min = Mathf.Min(min, vertex.y + (node.translation != null ? node.translation[1] : 0f));
        }
        return min;
    }
}
#endif
