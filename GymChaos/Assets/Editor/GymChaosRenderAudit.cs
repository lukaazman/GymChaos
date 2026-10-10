#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Diagnostic dump of what the built world asks the GPU to draw: triangles
/// and shadow casters grouped by top-level object, skinned meshes, realtime
/// lights, planar mirrors and the active quality settings. Logs
/// GYMCHAOS_RENDER_AUDIT lines and GYMCHAOS_RENDER_AUDIT_OK.
/// </summary>
public static class GymChaosRenderAudit
{
    private const string RequestedKey = "GymChaos.RenderAuditRequested";
    private static double startedAt;

    [InitializeOnLoadMethod]
    private static void Resume()
    {
        if (!SessionState.GetBool(RequestedKey, false)) return;
        Hook();
    }

    public static void Run()
    {
        SessionState.SetBool(RequestedKey, true);
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        Hook();
        EditorApplication.isPlaying = true;
    }

    private static void Hook()
    {
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
        EditorApplication.playModeStateChanged -= Changed;
        EditorApplication.playModeStateChanged += Changed;
    }

    private static void Changed(PlayModeStateChange change)
    {
        if (change == PlayModeStateChange.EnteredPlayMode) startedAt = EditorApplication.timeSinceStartup;
        if (change != PlayModeStateChange.EnteredEditMode) return;
        EditorApplication.update -= Tick;
        SessionState.EraseBool(RequestedKey);
        if (Application.isBatchMode) EditorApplication.Exit(0);
    }

    private static void Tick()
    {
        if (!EditorApplication.isPlaying || startedAt <= 0d) return;
        if (EditorApplication.timeSinceStartup - startedAt < 12d) return;
        EditorApplication.update -= Tick;
        try { Dump(); }
        catch (Exception exception) { Debug.LogException(exception); }
        Debug.Log("GYMCHAOS_RENDER_AUDIT_OK");
        EditorApplication.isPlaying = false;
    }

    private sealed class Group
    {
        public long triangles;
        public long shadowTriangles;
        public int renderers;
        public int shadowCasters;
        public int skinned;
        public int materials;
    }

    private static long Triangles(Mesh mesh)
    {
        if (mesh == null) return 0;
        long count = 0;
        for (int i = 0; i < mesh.subMeshCount; i++) count += mesh.GetIndexCount(i) / 3;
        return count;
    }

    private static string GroupKey(Transform t)
    {
        // Two levels deep keeps "World/Outdoor/..." from collapsing into one row.
        Transform root = t.root;
        Transform child = t;
        while (child.parent != null && child.parent != root) child = child.parent;
        return child == root ? root.name : root.name + "/" + child.name;
    }

    private static void Dump()
    {
        var groups = new Dictionary<string, Group>();
        var meshes = new Dictionary<string, long>();
        long total = 0, shadowTotal = 0;
        int casters = 0, rendererCount = 0;
        foreach (Renderer renderer in UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
        {
            if (!renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
            Mesh mesh = renderer is SkinnedMeshRenderer skinned ? skinned.sharedMesh
                : renderer.TryGetComponent(out MeshFilter filter) ? filter.sharedMesh : null;
            if (mesh == null) continue;
            long tris = Triangles(mesh);
            if (renderer.isPartOfStaticBatch)
            {
                // A static batch shares one combined mesh; count only this
                // renderer's submesh range.
                tris = 0;
                int first = renderer is MeshRenderer meshRenderer ? meshRenderer.subMeshStartIndex : 0;
                int count = renderer.sharedMaterials.Length;
                for (int s = first; s < first + count && s < mesh.subMeshCount; s++)
                    tris += mesh.GetIndexCount(s) / 3;
            }
            string key = GroupKey(renderer.transform);
            if (!groups.TryGetValue(key, out Group group)) groups[key] = group = new Group();
            group.triangles += tris;
            group.renderers++;
            group.materials += renderer.sharedMaterials.Length;
            if (renderer is SkinnedMeshRenderer) group.skinned++;
            bool castsShadow = renderer.shadowCastingMode != ShadowCastingMode.Off;
            if (castsShadow)
            {
                group.shadowCasters++;
                group.shadowTriangles += tris;
                casters++;
                shadowTotal += tris;
            }
            total += tris;
            rendererCount++;
            string meshKey = mesh.name + (mesh.lodCount > 1 ? " [lods=" + mesh.lodCount + "]" : "") +
                (renderer is SkinnedMeshRenderer ? " [skinned]" : "") +
                (castsShadow ? " [shadow]" : "");
            meshes.TryGetValue(meshKey, out long sum);
            meshes[meshKey] = sum + tris;
        }
        Debug.Log($"GYMCHAOS_RENDER_AUDIT total renderers={rendererCount} tris={total} " +
            $"shadowCasters={casters} shadowTris={shadowTotal}");
        foreach (var pair in groups.OrderByDescending(p => p.Value.triangles).Take(30))
            Debug.Log($"GYMCHAOS_RENDER_AUDIT group={pair.Key} tris={pair.Value.triangles} " +
                $"renderers={pair.Value.renderers} materials={pair.Value.materials} skinned={pair.Value.skinned} " +
                $"casters={pair.Value.shadowCasters} shadowTris={pair.Value.shadowTriangles}");
        foreach (var pair in meshes.OrderByDescending(p => p.Value).Take(30))
            Debug.Log($"GYMCHAOS_RENDER_AUDIT mesh={pair.Key} tris={pair.Value}");

        foreach (Light light in UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
        {
            if (!light.enabled || !light.gameObject.activeInHierarchy) continue;
            Debug.Log($"GYMCHAOS_RENDER_AUDIT light={light.name} type={light.type} shadows={light.shadows} " +
                $"range={light.range:F1} mode={light.lightmapBakeType}");
        }
        foreach (Camera camera in UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsSortMode.None))
            Debug.Log($"GYMCHAOS_RENDER_AUDIT camera={camera.name} enabled={camera.enabled} " +
                $"target={(camera.targetTexture != null ? camera.targetTexture.width + "x" + camera.targetTexture.height : "screen")} " +
                $"far={camera.farClipPlane:F0} mask={camera.cullingMask}");

        RenderPipelineAsset pipeline = GraphicsSettings.currentRenderPipeline;
        Debug.Log($"GYMCHAOS_RENDER_AUDIT quality={QualitySettings.names[QualitySettings.GetQualityLevel()]} " +
            $"pipeline={(pipeline != null ? pipeline.name : "none")} vSync={QualitySettings.vSyncCount} " +
            $"targetFrameRate={Application.targetFrameRate} shadowDistance={QualitySettings.shadowDistance} " +
            $"lodBias={QualitySettings.lodBias} antiAliasing={QualitySettings.antiAliasing}");
        if (pipeline != null)
        {
            var type = pipeline.GetType();
            foreach (string name in new[] { "shadowDistance", "shadowCascadeCount", "renderScale",
                         "msaaSampleCount", "mainLightShadowmapResolution", "additionalLightsShadowmapResolution",
                         "supportsAdditionalLightShadows", "maxAdditionalLightsCount", "supportsHDR",
                         "supportsCameraDepthTexture", "supportsCameraOpaqueTexture", "softShadowQuality" })
            {
                var property = type.GetProperty(name);
                if (property != null)
                    Debug.Log($"GYMCHAOS_RENDER_AUDIT urp {name}={property.GetValue(pipeline)}");
            }
        }
    }
}
#endif
