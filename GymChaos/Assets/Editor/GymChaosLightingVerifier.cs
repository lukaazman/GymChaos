#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Needs -Graphics. Renders eye-height views (post stack on, like the player
/// camera) across the gym floor grid and the locker room, at noon and at night,
/// and checks that every spot is readable (mean luma in band) and that the gym
/// is evenly lit (darkest spot / median spot). Logs per-spot luma, writes
/// Logs/agent/lighting-*.png and GYMCHAOS_LIGHTING_EVEN_OK or _FAIL.
/// </summary>
[InitializeOnLoad]
public static class GymChaosLightingVerifier
{
    private const string RequestedKey = "GymChaos.LightingVerifierRequested";
    private const float MinLuma = 0.2f;
    private const float MaxLuma = 0.6f;
    private const float MinUniformity = 0.7f;
    // Mostly floor and lower walls: local pools and dark patches show up per spot.
    private const float Pitch = 62f;
    private static readonly float[] Times = { 0.5f, 0.02f };
    private static double started;
    private static double builtAt = -1d;
    private static int resultCode = 1;

    static GymChaosLightingVerifier()
    {
        if (SessionState.GetBool(RequestedKey, false)) Hook();
    }

    [MenuItem("Tools/GymChaos/Run Lighting Verification")]
    public static void Run()
    {
        SessionState.SetBool(RequestedKey, true);
        started = EditorApplication.timeSinceStartup;
        builtAt = -1d;
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        Hook();
        EditorApplication.isPlaying = true;
    }

    private static void Hook() => GymChaosVerifierHooks.Hook(Tick, PlayModeChanged);

    private static void PlayModeChanged(PlayModeStateChange change)
    {
        if (change == PlayModeStateChange.EnteredPlayMode)
        {
            started = EditorApplication.timeSinceStartup;
            Application.runInBackground = true;
        }
        if (change != PlayModeStateChange.EnteredEditMode) return;
        EditorApplication.update -= Tick;
        EditorApplication.playModeStateChanged -= PlayModeChanged;
        SessionState.EraseBool(RequestedKey);
        if (Application.isBatchMode) GymChaosVerifierExit.Exit(resultCode);
    }

    private static void Tick()
    {
        if (!EditorApplication.isPlaying) return;
        double now = EditorApplication.timeSinceStartup;
        try
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
                throw new InvalidOperationException("Lighting verifier needs -Graphics.");
            GameObject floor = GameObject.Find("Rubber Floor");
            if (floor == null || !GymBackRoomBuilder.TryGetRoomBounds(out Bounds locker) ||
                GymTimeOfDay.Instance == null)
            {
                if (now - started > 60d) throw new TimeoutException("Interior did not build.");
                return;
            }
            if (builtAt < 0d) builtAt = now;
            if (now - builtAt < 4d) return;

            Bounds gym = floor.GetComponent<Renderer>().bounds;
            var spots = new List<(string name, Vector3 position)>();
            for (int x = 0; x < 3; x++)
                for (int z = 0; z < 3; z++)
                    spots.Add(($"gym{x}{z}", new Vector3(
                        Mathf.Lerp(gym.min.x, gym.max.x, 0.18f + 0.32f * x), gym.max.y + 1.6f,
                        Mathf.Lerp(gym.min.z, gym.max.z, 0.18f + 0.32f * z))));
            spots.Add(("lockerA", new Vector3(locker.center.x - locker.extents.x * 0.4f,
                locker.min.y + 1.6f, locker.center.z)));
            spots.Add(("lockerB", new Vector3(locker.center.x + locker.extents.x * 0.4f,
                locker.min.y + 1.6f, locker.center.z)));

            string directory = Path.Combine(Directory.GetParent(Application.dataPath).Parent.FullName,
                "Logs", "agent");
            Directory.CreateDirectory(directory);
            var failures = new List<string>();
            foreach (float time in Times)
            {
                GymTimeOfDay.Instance.SetTimeForVerification(time);
                var gymLumas = new List<float>();
                foreach (var spot in spots)
                {
                    float luma = 0f;
                    for (int yaw = 0; yaw < 360; yaw += 90)
                    {
                        string png = yaw == 0
                            ? Path.Combine(directory, $"lighting-{spot.name}-{(time > 0.25f ? "day" : "night")}.png")
                            : null;
                        luma += MeanLuma(spot.position, Quaternion.Euler(Pitch, yaw, 0f), png) * 0.25f;
                    }
                    Debug.Log(string.Format(CultureInfo.InvariantCulture,
                        "GYMCHAOS_LIGHTING_SAMPLE time={0:F2} spot={1} luma={2:F3}", time, spot.name, luma));
                    if (luma < MinLuma || luma > MaxLuma)
                        failures.Add($"{spot.name}@{time:F2} luma={luma:F3}");
                    if (spot.name.StartsWith("gym", StringComparison.Ordinal)) gymLumas.Add(luma);
                }
                // Darkest spot against the median: a white tile area (albedo) cannot mask a dark patch.
                gymLumas.Sort();
                float median = gymLumas[gymLumas.Count / 2];
                float uniformity = gymLumas[0] / Mathf.Max(0.001f, median);
                Debug.Log(string.Format(CultureInfo.InvariantCulture,
                    "GYMCHAOS_LIGHTING_UNIFORMITY time={0:F2} min={1:F3} median={2:F3} ratio={3:F3}",
                    time, gymLumas[0], median, uniformity));
                if (uniformity < MinUniformity) failures.Add($"gym uniformity@{time:F2}={uniformity:F3}");
            }
            if (failures.Count > 0)
                throw new InvalidOperationException("GYMCHAOS_LIGHTING_EVEN_FAIL " + string.Join("; ", failures));
            Debug.Log("GYMCHAOS_LIGHTING_EVEN_OK");
            resultCode = 0;
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            resultCode = 1;
        }
        GymChaosVerifierExit.Record(resultCode);
        EditorApplication.update -= Tick;
        EditorApplication.isPlaying = false;
    }

    // Mean Rec.709 luma of the sRGB image a player camera would show from here.
    private static float MeanLuma(Vector3 position, Quaternion rotation, string png)
    {
        const int width = 320;
        const int height = 180;
        GameObject host = new GameObject("Lighting Probe Camera");
        Camera camera = host.AddComponent<Camera>();
        camera.fieldOfView = 70f;
        camera.nearClipPlane = 0.05f;
        camera.farClipPlane = 200f;
        camera.cullingMask &= ~(1 << PlanarGymMirror.MirrorPlayerLayer);
        camera.transform.SetPositionAndRotation(position, rotation);
        camera.GetUniversalAdditionalCameraData().renderPostProcessing = true;
        RenderTexture target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
        camera.targetTexture = target;
        camera.Render();
        RenderTexture previous = RenderTexture.active;
        RenderTexture.active = target;
        Texture2D image = new Texture2D(width, height, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        image.Apply();
        RenderTexture.active = previous;
        camera.targetTexture = null;
        Color32[] pixels = image.GetPixels32();
        double sum = 0d;
        for (int i = 0; i < pixels.Length; i++)
            sum += 0.2126 * pixels[i].r + 0.7152 * pixels[i].g + 0.0722 * pixels[i].b;
        if (png != null) File.WriteAllBytes(png, image.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(image);
        target.Release();
        UnityEngine.Object.DestroyImmediate(target);
        UnityEngine.Object.DestroyImmediate(host);
        return (float)(sum / pixels.Length / 255d);
    }
}
#endif
