#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// In-game showcase captures (run with -Graphics):
//  - city_birdseye.png: high oblique view over the whole city and buildings;
//  - goku_nimbus_*.png: Goku in the nimbus stance on his cloud mid-arrival.
// Writes Logs/agent/showcase/.
[InitializeOnLoad]
public static class GymChaosShowcaseShots
{
    private const string RequestedKey = "GymChaos.ShowcaseShotsRequested";
    private static double started;
    private static int stage;
    private static int rideFrames;
    private static bool finished;
    private static int resultCode;
    private static EnemyFighter goku;
    private static string outDir;

    static GymChaosShowcaseShots()
    {
        if (SessionState.GetBool(RequestedKey, false)) Hook();
    }

    [MenuItem("Tools/GymChaos/Capture Showcase Shots")]
    public static void Run()
    {
        SessionState.SetBool(RequestedKey, true);
        GymChaosVerifierSave.Neutralize();
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        Hook();
        EditorApplication.isPlaying = true;
    }

    private static void Hook() => GymChaosVerifierHooks.Hook(Tick, Changed);

    private static void Changed(PlayModeStateChange change)
    {
        if (change == PlayModeStateChange.EnteredPlayMode)
        {
            started = EditorApplication.timeSinceStartup;
            stage = 0;
            rideFrames = 0;
            finished = false;
            resultCode = 1;
            goku = null;
            GymChaosVerifierExit.Record(1);
        }
        if (change != PlayModeStateChange.EnteredEditMode) return;
        EditorApplication.update -= Tick;
        EditorApplication.playModeStateChanged -= Changed;
        SessionState.EraseBool(RequestedKey);
        GymChaosVerifierSave.Restore();
        if (Application.isBatchMode) GymChaosVerifierExit.Exit(resultCode);
    }

    private static void Tick()
    {
        if (finished || !EditorApplication.isPlaying) return;
        try
        {
            double now = EditorApplication.timeSinceStartup;
            if (now - started > 150d) throw new TimeoutException($"stage {stage}");
            outDir = Path.GetFullPath(Path.Combine(Application.dataPath, "../../Logs/agent/showcase"));
            Directory.CreateDirectory(outDir);
            switch (stage)
            {
                case 0:
                    // Let the runtime city, GLBs and textures load.
                    if (now - started < 14d) return;
                    CaptureCity();
                    stage = 1;
                    break;
                case 1:
                    GymVisitorDirector director = UnityEngine.Object.FindAnyObjectByType<GymVisitorDirector>();
                    if (director != null && director.BeginVehicleArrivalForVerification(
                        BodybuilderIdentity.Goku, out goku, out _))
                    {
                        stage = 2;
                    }
                    break;
                case 2:
                    if (goku == null || !goku.IsRidingVehicleForVerification ||
                        goku.VehicleBodyAnimator == null || !goku.VehicleBodyAnimator.IsNimbusStanceActive)
                    {
                        return;
                    }
                    if (++rideFrames < 25) return;
                    CaptureGoku();
                    Debug.Log($"GYMCHAOS_SHOWCASE_OK dir={outDir}");
                    finished = true;
                    resultCode = 0;
                    GymChaosVerifierExit.Record(0);
                    EditorApplication.isPlaying = false;
                    break;
            }
        }
        catch (Exception e)
        {
            Debug.LogError("GYMCHAOS_SHOWCASE_FAILED " + e.Message);
            finished = true;
            EditorApplication.isPlaying = false;
        }
    }

    private static void CaptureCity()
    {
        Bounds city = default;
        bool any = false;
        foreach (Renderer r in UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
        {
            if (!r.enabled || !r.gameObject.activeInHierarchy) continue;
            Vector3 size = r.bounds.size;
            if (size.x > 900f || size.z > 900f || size.y > 400f) continue;   // sky dome, horizon cards
            if (!any) { city = r.bounds; any = true; } else city.Encapsulate(r.bounds);
        }
        float radius = Mathf.Max(city.extents.x, city.extents.z);
        Vector3 center = new Vector3(city.center.x, city.min.y, city.center.z);
        Vector3 offset = new Vector3(-0.55f, 0.95f, -0.75f).normalized * radius * 1.75f;
        // Straight down over the city centre; the 16:9 frame fits the depth.
        float halfDepth = city.extents.z * 1.04f;
        float height = halfDepth / Mathf.Tan(25f * Mathf.Deg2Rad);
        Shot("city_topdown.png", center + Vector3.up * height, center, 50f, 2560, 1440, height + 600f, Vector3.forward);
        Debug.Log($"GYMCHAOS_SHOWCASE_CITY bounds={city}");
    }

    private static void CaptureGoku()
    {
        Time.timeScale = 0f;
        Transform root = goku.transform;
        Vector3 chest = goku.VehicleBodyAnimator.BodyRig.Chest.position;
        Vector3 forward = Vector3.ProjectOnPlane(root.forward, Vector3.up).normalized;
        Vector3 right = Vector3.Cross(Vector3.up, forward);
        Shot("goku_nimbus_front.png", chest + forward * 5.5f + right * 2.2f + Vector3.up * 0.6f, chest - Vector3.up * 0.6f, 45f, 1920, 1080, 2000f);
        Shot("goku_nimbus_side.png", chest - right * 6f + forward * 1.2f + Vector3.up * 0.4f, chest - Vector3.up * 0.6f, 45f, 1920, 1080, 2000f);
        Shot("goku_nimbus_wide.png", chest + forward * 11f - right * 4f + Vector3.up * 3f, chest - Vector3.up * 0.8f, 50f, 1920, 1080, 2000f);
        Time.timeScale = 1f;
    }

    private static void Shot(string file, Vector3 position, Vector3 target, float fov, int width, int height, float far,
        Vector3? up = null)
    {
        GameObject host = new GameObject("Showcase Camera");
        Camera camera = host.AddComponent<Camera>();
        Camera main = Camera.main;
        if (main != null)
        {
            camera.CopyFrom(main);
            camera.targetTexture = null;
        }
        camera.fieldOfView = fov;
        camera.nearClipPlane = 0.05f;
        camera.farClipPlane = far;
        camera.transform.SetPositionAndRotation(position, Quaternion.LookRotation(target - position, up ?? Vector3.up));
        RenderTexture rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
        camera.targetTexture = rt;
        camera.Render();
        RenderTexture previous = RenderTexture.active;
        RenderTexture.active = rt;
        Texture2D image = new Texture2D(width, height, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        image.Apply();
        RenderTexture.active = previous;
        camera.targetTexture = null;
        File.WriteAllBytes(Path.Combine(outDir, file), image.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(image);
        rt.Release();
        UnityEngine.Object.DestroyImmediate(rt);
        UnityEngine.Object.DestroyImmediate(host);
    }
}
#endif
