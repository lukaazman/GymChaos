#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Eye bars without gaps: no face vertex inside a bar's height band may
/// stick out through its curved shell (that showed as holes in Davie's bar).
/// Bakes every bodybuilder's skinned mesh, counts the vertices outside the
/// shell, fails on Davie and logs the rest. With a graphics device it writes
/// a close-up of each bar to Logs/agent/eyebar/.
/// Logs GYMCHAOS_EYEBAR_OK.
/// </summary>
public static class GymChaosEyeBarVerifier
{
    private const string RequestedKey = "GymChaos.EyeBarVerificationRequested";
    // Points closer than this to the shell count as touching, not piercing.
    private const float Tolerance = 0.002f;
    private static double started;

    [InitializeOnLoadMethod]
    private static void ResumeAfterReload()
    {
        if (!SessionState.GetBool(RequestedKey, false)) return;
        Hook();
    }

    public static void Run()
    {
        GymChaosVerifierSave.Neutralize();
        SessionState.SetBool(RequestedKey, true);
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        Hook();
        EditorApplication.isPlaying = true;
    }

    private static void Hook() => GymChaosVerifierHooks.Hook(Tick, Changed);

    private static void Changed(PlayModeStateChange change)
    {
        if (change == PlayModeStateChange.EnteredPlayMode) started = EditorApplication.timeSinceStartup;
        if (change != PlayModeStateChange.EnteredEditMode) return;
        EditorApplication.update -= Tick;
        GymChaosVerifierSave.Restore();
        if (Application.isBatchMode && SessionState.GetBool(RequestedKey, false)) EditorApplication.Exit(1);
    }

    private static void Tick()
    {
        if (!EditorApplication.isPlaying) return;
        double elapsed = EditorApplication.timeSinceStartup - started;
        List<FaceCensorSettings> bars = FindBars();
        bool davieReady = bars.Exists(b => b.name.StartsWith("Davie"));
        if (!davieReady && elapsed < 90d) return;
        if (elapsed < 8d) return;
        int code = 1;
        try
        {
            if (!davieReady) throw new InvalidOperationException("Davie has no eye bar");
            int daviePierce = -1;
            foreach (FaceCensorSettings bar in bars)
            {
                int pierce = CountPiercing(bar, out int band);
                string who = bar.name.Replace(" Black Eye Bar", string.Empty);
                Debug.Log($"GYMCHAOS_EYEBAR_PIERCE identity={who} frontPiercing={pierce} sidePiercing={sidePierce} bandVertices={band}");
                if (who == "Davie") daviePierce = pierce;
                Capture(bar, who);
            }
            if (daviePierce != 0) throw new InvalidOperationException($"Davie's face pokes through the bar ({daviePierce})");
            Debug.Log($"GYMCHAOS_EYEBAR_OK bars={bars.Count}");
            code = 0;
        }
        catch (Exception exception)
        {
            Debug.LogError("GYMCHAOS_EYEBAR_FAILED " + exception.Message);
        }
        SessionState.EraseBool(RequestedKey);
        EditorApplication.update -= Tick;
        GymChaosVerifierSave.Restore();
        if (Application.isBatchMode)
        {
            GymChaosVerifierExit.Record(code);
            EditorApplication.Exit(code);
        }
        else
        {
            EditorApplication.isPlaying = false;
        }
    }

    private static List<FaceCensorSettings> FindBars()
    {
        var result = new List<FaceCensorSettings>();
        foreach (FaceCensorSettings bar in UnityEngine.Object.FindObjectsByType<FaceCensorSettings>(
                     FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (bar != null && bar.GetComponent<MeshRenderer>() != null && bar.GetComponentInParent<EnemyFighter>(true) != null)
                result.Add(bar);
        return result;
    }

    private const float FrontShare = 0.75f;
    private static int sidePierce;

    private static int CountPiercing(FaceCensorSettings bar, out int band)
    {
        band = 0;
        sidePierce = 0;
        EnemyFighter fighter = bar.GetComponentInParent<EnemyFighter>(true);
        SkinnedMeshRenderer body = null;
        foreach (SkinnedMeshRenderer candidate in fighter.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            if (body == null || (candidate.sharedMesh != null && body.sharedMesh != null &&
                                 candidate.sharedMesh.vertexCount > body.sharedMesh.vertexCount))
                body = candidate;
        if (body == null) return -1;
        Mesh baked = new Mesh();
        body.BakeMesh(baked, true);
        Vector3[] vertices = baked.vertices;
        UnityEngine.Object.Destroy(baked);
        Vector2 size = bar.ConfiguredSize;
        float depth = bar.ConfiguredFaceDepth;
        float halfWidth = size.x * 0.5f;
        float arc = Mathf.Sin(Mathf.Clamp(69f, 55f, 82f) * Mathf.Deg2Rad);
        int pierce = 0;
        Transform t = bar.transform;
        foreach (Vector3 v in vertices)
        {
            Vector3 world = body.transform.TransformPoint(v);
            // The bar compensates the head scale, so its local space is metric.
            Vector3 local = t.InverseTransformPoint(world);
            if (Mathf.Abs(local.y) > size.y * 0.5f || local.z <= 0f) continue;
            float nx = local.x / halfWidth;
            float nz = local.z / depth;
            float r = Mathf.Sqrt(nx * nx + nz * nz);
            if (r < 1e-4f || Mathf.Abs(nx / r) > arc) continue;
            band++;
            // Distance past the shell along the ray from the shell centre.
            float outside = (r - 1f) * Mathf.Min(halfWidth, depth);
            if (outside <= Tolerance) continue;
            // The face front (eyes, brow, nose) is what shows as holes; hair
            // curling past the bar's far sides is only counted for the log.
            if (Mathf.Abs(nx / r) < FrontShare) pierce++;
            else sidePierce++;
        }
        return pierce;
    }

    private static void Capture(FaceCensorSettings bar, string who)
    {
        if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) return;
        EnemyFighter fighter = bar.GetComponentInParent<EnemyFighter>(true);
        bool wasActive = fighter.gameObject.activeSelf;
        if (!wasActive) fighter.gameObject.SetActive(true);
        GameObject cameraObject = new GameObject("Eye Bar Capture Camera");
        Camera camera = cameraObject.AddComponent<Camera>();
        camera.fieldOfView = 30f;
        camera.nearClipPlane = 0.02f;
        Vector3 target = bar.transform.position + bar.transform.forward * bar.ConfiguredFaceDepth;
        // From above as well: the player is taller than most visitors.
        foreach (float rise in new[] { 0.04f, 0.3f })
        {
            camera.transform.position = target + bar.transform.forward * 0.55f + Vector3.up * rise;
            camera.transform.LookAt(target);
            RenderTexture rt = new RenderTexture(512, 512, 24);
            camera.targetTexture = rt;
            camera.Render();
            RenderTexture.active = rt;
            Texture2D image = new Texture2D(512, 512, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, 512, 512), 0, 0);
            RenderTexture.active = null;
            string directory = Path.Combine(Directory.GetCurrentDirectory(), "..", "Logs", "agent", "eyebar");
            Directory.CreateDirectory(directory);
            File.WriteAllBytes(Path.Combine(directory, who + (rise > 0.1f ? "_above" : string.Empty) + ".png"),
                image.EncodeToPNG());
            UnityEngine.Object.Destroy(image);
            camera.targetTexture = null;
            rt.Release();
            UnityEngine.Object.Destroy(rt);
        }
        UnityEngine.Object.Destroy(cameraObject);
        if (!wasActive) fighter.gameObject.SetActive(false);
    }
}
#endif
