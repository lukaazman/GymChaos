#if UNITY_EDITOR
using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Diagnostic: dumps world bounds of every exterior renderer and collider to
/// Logs/agent/layout.json so a top-down plan can be plotted outside Unity.
/// </summary>
[InitializeOnLoad]
public static class GymChaosLayoutDump
{
    private const string RequestedKey = "GymChaos.LayoutDumpRequested";
    private static double started;
    private static double builtAt = -1d;
    private static int resultCode = 1;
    private static bool movedOutside;

    static GymChaosLayoutDump()
    {
        if (SessionState.GetBool(RequestedKey, false)) Hook();
    }

    [MenuItem("Tools/GymChaos/Dump Exterior Layout")]
    public static void Run()
    {
        SessionState.SetBool(RequestedKey, true);
        started = EditorApplication.timeSinceStartup;
        builtAt = -1d;
        movedOutside = false;
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        Hook();
        EditorApplication.isPlaying = true;
    }

    private static void Hook()
    {
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
        EditorApplication.playModeStateChanged -= PlayModeChanged;
        EditorApplication.playModeStateChanged += PlayModeChanged;
    }

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
            if (!GymOutdoorBuilder.IsBuilt || !GymProteinStoreEnvironment.IsLoaded)
            {
                if (now - started > 60d) throw new TimeoutException("Exterior did not build.");
                return;
            }
            // Let runtime GLBs (bus stop, streetlights, city) finish loading.
            if (builtAt < 0d) builtAt = now;
            if (now - builtAt < 6d) return;
            // Exterior-only fences render only while the player is outside.
            PlayerMovement player = UnityEngine.Object.FindFirstObjectByType<PlayerMovement>();
            if (!movedOutside && player != null)
            {
                movedOutside = true;
                Rigidbody body = player.GetComponent<Rigidbody>();
                Vector3 outside = new Vector3(168.25f, -0.05f, 22f);
                if (body != null) { body.position = outside; body.linearVelocity = Vector3.zero; }
                player.transform.position = outside;
                // Optional time of day for the renders (0..1, 0.5 = noon).
                string timeOfDay = Environment.GetEnvironmentVariable("GYMCHAOS_LAYOUT_TIME");
                if (!string.IsNullOrEmpty(timeOfDay) && GymTimeOfDay.Instance != null)
                {
                    GymTimeOfDay.Instance.SetTimeForVerification(
                        float.Parse(timeOfDay, CultureInfo.InvariantCulture));
                }
                return;
            }
            if (now - builtAt < 8d) return;

            Physics.SyncTransforms();
            StringBuilder json = new StringBuilder("{\"items\":[\n");
            bool first = true;
            foreach (GameObject root in UnityEngine.SceneManagement.SceneManager
                         .GetActiveScene().GetRootGameObjects())
            {
                string rootName = root.name;
                if (rootName.StartsWith("Gym Interior", StringComparison.Ordinal) &&
                    string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GYMCHAOS_LAYOUT_INTERIOR"))) continue;
                foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
                    Append(json, ref first, "r", renderer.name, rootName, renderer.bounds,
                        renderer.enabled && renderer.gameObject.activeInHierarchy);
                foreach (Collider collider in root.GetComponentsInChildren<Collider>(true))
                    Append(json, ref first, collider.isTrigger ? "t" : "c", collider.name,
                        rootName, collider.bounds,
                        collider.enabled && collider.gameObject.activeInHierarchy);
            }
            json.Append("],\n\"points\":{");
            AppendPoint(json, "door", GymDoorway.Instance != null
                ? GymDoorway.Instance.ExteriorPoint : Vector3.zero, true);
            AppendPoint(json, "arrivalSpawn", GymOutdoorBuilder.VehicleArrivalRoadSpawnPoint, false);
            AppendPoint(json, "arrivalTurn", GymOutdoorBuilder.VehicleArrivalRoadTurnPoint, false);
            AppendPoint(json, "arrivalJunction", GymOutdoorBuilder.VehicleArrivalRoadJunctionPoint, false);
            AppendPoint(json, "parkingEntry", GymOutdoorBuilder.VisitorParkingEntryPoint, false);
            AppendPoint(json, "storeEast", GymOutdoorBuilder.ProteinStoreEastRoutePoint, false);
            AppendPoint(json, "storeSouthC", GymOutdoorBuilder.ProteinStoreSouthCurvePointC, false);
            AppendPoint(json, "storeSouthD", GymOutdoorBuilder.ProteinStoreSouthCurvePointD, false);
            AppendPoint(json, "storeEntrance", GymProteinStoreEnvironment.StoreEntrancePoint, false);
            AppendPoint(json, "westApproach", GymOutdoorBuilder.ProteinStoreWestApproachPoint, false);
            AppendPoint(json, "parkingBypass", GymOutdoorBuilder.ProteinStoreParkingBypassPoint, false);
            AppendPoint(json, "gymPathClear", GymOutdoorBuilder.ProteinStoreGymPathClearPoint, false);
            AppendPoint(json, "gymPathSouth", GymOutdoorBuilder.ProteinStoreGymPathSouthClearPoint, false);
            AppendPoint(json, "gateWestClear", GymOutdoorBuilder.ProteinStoreGateWestClearPoint, false);
            AppendPoint(json, "southB", GymOutdoorBuilder.ProteinStoreSouthCurvePointB, false);
            AppendPoint(json, "southE", GymOutdoorBuilder.ProteinStoreSouthCurvePointE, false);
            AppendPoint(json, "storeFront", GymProteinStoreEnvironment.StoreFrontClearPoint, false);
            AppendPoint(json, "storeVisit", GymProteinStoreEnvironment.StoreVisitApproachPoint, false);
            AppendPoint(json, "busExit", GymRoadsideBusStop.DavieBusPedestrianExitPoint, false);
            AppendPoint(json, "parkingNorthGate", GymOutdoorBuilder.VisitorParkingNorthGatePoint, false);
            AppendPoint(json, "roadTurn", GymOutdoorBuilder.VehicleRoadTurnPoint, false);
            json.Append("}}\n");

            string directory = Path.Combine(Directory.GetParent(Application.dataPath).Parent.FullName,
                "Logs", "agent");
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "layout.json");
            File.WriteAllText(path, json.ToString());
            if (SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null)
            {
                CaptureTopDown(Path.Combine(directory, "layout-top.png"),
                    new Vector3(163f, 60f, 20f), 44f);
                CaptureTopDown(Path.Combine(directory, "layout-store.png"),
                    new Vector3(180f, 60f, 20f), 18f);
                CaptureTopDown(Path.Combine(directory, "layout-busstop.png"),
                    new Vector3(190f, 60f, 33f), 14f);
                string views = Environment.GetEnvironmentVariable("GYMCHAOS_LAYOUT_VIEWS");
                if (!string.IsNullOrEmpty(views))
                {
                    // Format: name:x,y,z,yaw,pitch;name2:...
                    foreach (string view in views.Split(';'))
                    {
                        string[] parts = view.Split(':');
                        if (parts.Length != 2) continue;
                        string[] v = parts[1].Split(',');
                        if (v.Length != 5) continue;
                        float F(int i) => float.Parse(v[i], CultureInfo.InvariantCulture);
                        CapturePerspective(Path.Combine(directory, $"view-{parts[0]}.png"),
                            new Vector3(F(0), F(1), F(2)), Quaternion.Euler(F(4), F(3), 0f));
                    }
                }
            }
            Debug.Log($"GYMCHAOS_LAYOUT_DUMP_OK path={path}");
            resultCode = 0; GymChaosVerifierExit.Record(resultCode);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            resultCode = 1; GymChaosVerifierExit.Record(resultCode);
        }
        EditorApplication.update -= Tick;
        EditorApplication.isPlaying = false;
    }

    private static void CaptureTopDown(string path, Vector3 center, float halfSize)
    {
        GameObject host = new GameObject("Layout Capture Camera");
        Camera camera = host.AddComponent<Camera>();
        camera.orthographic = true;
        camera.orthographicSize = halfSize;
        camera.transform.SetPositionAndRotation(center, Quaternion.Euler(90f, 0f, 0f));
        camera.nearClipPlane = 0.3f;
        camera.farClipPlane = 200f;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(1f, 0f, 1f);
        Render(camera, path, 2048, 2048);
        UnityEngine.Object.DestroyImmediate(host);
    }

    private static void CapturePerspective(string path, Vector3 position, Quaternion rotation)
    {
        GameObject host = new GameObject("Layout View Camera");
        Camera camera = host.AddComponent<Camera>();
        camera.fieldOfView = 70f;
        camera.transform.SetPositionAndRotation(position, rotation);
        camera.nearClipPlane = 0.05f;
        camera.farClipPlane = 400f;
        Render(camera, path, 1280, 720);
        UnityEngine.Object.DestroyImmediate(host);
    }

    private static void Render(Camera camera, string path, int width, int height)
    {
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
        File.WriteAllBytes(path, image.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(image);
        target.Release();
        UnityEngine.Object.DestroyImmediate(target);
    }

    private static void Append(StringBuilder json, ref bool first, string kind, string name,
        string root, Bounds bounds, bool active)
    {
        if (!first) json.Append(",\n");
        first = false;
        json.Append("{\"k\":\"").Append(kind).Append("\",\"n\":\"").Append(Escape(name))
            .Append("\",\"root\":\"").Append(Escape(root)).Append("\",\"a\":")
            .Append(active ? "1" : "0").Append(",\"min\":");
        Vec(json, bounds.min);
        json.Append(",\"max\":");
        Vec(json, bounds.max);
        json.Append('}');
    }

    private static void AppendPoint(StringBuilder json, string name, Vector3 point, bool first)
    {
        if (!first) json.Append(',');
        json.Append('"').Append(name).Append("\":");
        Vec(json, point);
    }

    private static void Vec(StringBuilder json, Vector3 v)
    {
        json.Append('[').Append(v.x.ToString("F3", CultureInfo.InvariantCulture)).Append(',')
            .Append(v.y.ToString("F3", CultureInfo.InvariantCulture)).Append(',')
            .Append(v.z.ToString("F3", CultureInfo.InvariantCulture)).Append(']');
    }

    private static string Escape(string value) =>
        value.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
#endif
