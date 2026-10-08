#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Drives every wheeled vehicle (visitor cars, Arnold's Hummer, Davie's bus
/// and the police car) and checks that only the separated wheel nodes turn,
/// that they roll by distance / radius while driving, and that they stay still
/// while parked. Logs GYMCHAOS_WHEEL_SPIN_OK.
/// </summary>
public static class GymChaosWheelSpinVerifier
{
    private const string RequestedKey = "GymChaos.WheelSpinVerificationRequested";
    private const string PoliceCarAsset = "BodyBuilders/vehicles/Policecar.glb";
    private static readonly BodybuilderIdentity[] Identities =
    {
        BodybuilderIdentity.Cbum, BodybuilderIdentity.Zyzz, BodybuilderIdentity.JayCutler,
        BodybuilderIdentity.Ronnie, BodybuilderIdentity.Arnold, BodybuilderIdentity.Davie
    };

    private static int phase;
    private static int index;
    private static double phaseStarted;
    private static double started;
    private static GymVisitorVehicle vehicle;
    private static GymChaosWheelSpinProbe probe;
    private static GameObject policeCar;
    private static bool policeMoving;
    private static int policeSteps;
    private static bool departed;
    private static float savedMaximumDeltaTime;
    private static readonly StringBuilder summary = new StringBuilder();

    [InitializeOnLoadMethod]
    private static void ResumeAfterReload()
    {
        if (!SessionState.GetBool(RequestedKey, false)) return;
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
        EditorApplication.playModeStateChanged -= PlayModeChanged;
        EditorApplication.playModeStateChanged += PlayModeChanged;
    }

    public static void Run()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        SessionState.SetBool(RequestedKey, true);
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
        EditorApplication.playModeStateChanged -= PlayModeChanged;
        EditorApplication.playModeStateChanged += PlayModeChanged;
        Debug.Log("GYMCHAOS_WHEEL_SPIN_STARTED");
        EditorApplication.isPlaying = true;
    }

    private static void PlayModeChanged(PlayModeStateChange change)
    {
        if (change == PlayModeStateChange.EnteredPlayMode)
        {
            phase = 0;
            index = 0;
            started = EditorApplication.timeSinceStartup;
            phaseStarted = started;
            summary.Clear();
            savedMaximumDeltaTime = Time.maximumDeltaTime;
            // Keep each sampled frame short so a wheel never turns more than
            // half a revolution between two probe samples.
            Time.maximumDeltaTime = 0.05f;
            AudioListener.pause = true;
        }
        if (change != PlayModeStateChange.EnteredEditMode) return;
        EditorApplication.update -= Tick;
        EditorApplication.playModeStateChanged -= PlayModeChanged;
        if (Application.isBatchMode && SessionState.GetBool(RequestedKey, false))
            EditorApplication.Exit(1);
    }

    private static void Tick()
    {
        if (!EditorApplication.isPlaying) return;
        AudioListener.pause = true;
        try
        {
            GymVisitorDirector director = UnityEngine.Object.FindAnyObjectByType<GymVisitorDirector>();
            if (director == null || !GymOutdoorBuilder.IsBuilt) return;
            if (EditorApplication.timeSinceStartup - started > 240d)
                throw new InvalidOperationException($"Wheel spin verification timed out phase={phase} index={index}.");
            if (probe != null && probe.gameObject != null) probe.Sample();
            switch (phase)
            {
                case 0: Begin(director); break;
                case 1: TickVisitorLoad(); break;
                case 2: TickVisitorParked(); break;
                case 3: TickVisitorDriving(); break;
                case 4: BeginPolice(); break;
                case 5: TickPolice(); break;
            }
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            Debug.LogError("GYMCHAOS_WHEEL_SPIN_FAILED " + exception.Message);
            Finish(1);
        }
    }

    private static void Begin(GymVisitorDirector director)
    {
        if (EditorApplication.timeSinceStartup - started < 2d) return;
        director.enabled = false;
        foreach (GymVisitorVehicle existing in UnityEngine.Object.FindObjectsByType<GymVisitorVehicle>(
                     FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (existing != null) existing.gameObject.SetActive(false);
        }
        CreateVisitor();
    }

    private static void CreateVisitor()
    {
        BodybuilderIdentity identity = Identities[index];
        vehicle = GymVisitorVehicle.Create(identity, index % 5, null, true);
        if (vehicle == null) throw new InvalidOperationException($"{identity} vehicle could not be created.");
        vehicle.gameObject.SetActive(true);
        departed = false;
        phase = 1;
        phaseStarted = EditorApplication.timeSinceStartup;
    }

    private static void TickVisitorLoad()
    {
        GymVehicleWheelSpinner spinner = vehicle.GetComponent<GymVehicleWheelSpinner>();
        if (spinner == null)
            throw new InvalidOperationException($"{vehicle.name} has no wheel spinner.");
        if (!vehicle.RuntimeVisualReadyForVerification || spinner.WheelCount == 0)
        {
            if (EditorApplication.timeSinceStartup - phaseStarted > 30d)
                throw new InvalidOperationException(
                    $"{vehicle.name} wheels did not load ready={vehicle.RuntimeVisualReadyForVerification} wheels={spinner.WheelCount}.");
            return;
        }
        probe = new GymChaosWheelSpinProbe(vehicle.gameObject);
        probe.Capture();
        probe.Recording = true;
        probe.Driving = false;
        phase = 2;
        phaseStarted = EditorApplication.timeSinceStartup;
    }

    private static void TickVisitorParked()
    {
        if (EditorApplication.timeSinceStartup - phaseStarted < 1.0d) return;
        if (!vehicle.IsParked)
            throw new InvalidOperationException($"{vehicle.name} was expected to start parked.");
        probe.Driving = true;
        vehicle.DriveOut(() => departed = true);
        phase = 3;
        phaseStarted = EditorApplication.timeSinceStartup;
    }

    private static void TickVisitorDriving()
    {
        bool enoughDistance = probe.RolledDistance > 12f;
        bool timedOut = EditorApplication.timeSinceStartup - phaseStarted > 25d;
        if (!enoughDistance && !departed && !timedOut) return;
        probe.Recording = false;
        Check(vehicle.name, probe, vehicle.GetComponent<GymVehicleWheelSpinner>().WheelCount);
        Capture(vehicle.gameObject, vehicle.name);
        UnityEngine.Object.Destroy(vehicle.gameObject);
        vehicle = null;
        index++;
        if (index < Identities.Length)
        {
            CreateVisitor();
        }
        else
        {
            phase = 4;
            phaseStarted = EditorApplication.timeSinceStartup;
        }
    }

    private static void BeginPolice()
    {
        GymVisitorDirector director = UnityEngine.Object.FindAnyObjectByType<GymVisitorDirector>();
        Vector3 start = GymOutdoorBuilder.VehicleRoadTurnPoint + Vector3.up * 0.05f;
        policeCar = RuntimeGlbSceneLoader.Request(
            PoliceCarAsset, director.transform, start, Quaternion.LookRotation(Vector3.back),
            Vector3.one * 4.6f, "Wheel Spin Police Car", 0, settleOnSupport: false,
            supportY: start.y, onLoaded: null);
        if (policeCar == null) throw new InvalidOperationException("Police car request failed.");
        policeMoving = false;
        policeSteps = 0;
        GymVehicleWheelSpinner.Attach(policeCar, () => policeMoving);
        phase = 5;
        phaseStarted = EditorApplication.timeSinceStartup;
    }

    private static void TickPolice()
    {
        GymVehicleWheelSpinner spinner = policeCar.GetComponent<GymVehicleWheelSpinner>();
        if (probe == null || probe.gameObject != policeCar)
        {
            if (spinner.WheelCount == 0)
            {
                if (EditorApplication.timeSinceStartup - phaseStarted > 30d)
                    throw new InvalidOperationException("Police car wheels did not load.");
                return;
            }
            probe = new GymChaosWheelSpinProbe(policeCar);
            probe.Capture();
            probe.Recording = true;
            probe.Driving = false;
            phaseStarted = EditorApplication.timeSinceStartup;
        }
        double elapsed = EditorApplication.timeSinceStartup - phaseStarted;
        // Parked for one second while being nudged (as a settling car would
        // be): the wheels must not turn. Then drive forward by script.
        if (elapsed < 1.0d)
        {
            policeCar.transform.position += policeCar.transform.forward * 0.01f;
            return;
        }
        policeMoving = true;
        probe.Driving = true;
        policeCar.transform.position += policeCar.transform.forward * 0.12f;
        policeSteps++;
        if (policeSteps < 80) return;
        probe.Recording = false;
        Check("Police Car", probe, spinner.WheelCount);
        Capture(policeCar, "Police Car");
        Debug.Log("GYMCHAOS_WHEEL_SPIN_OK " + summary.ToString().Trim());
        Finish(0);
    }

    private static void Check(string label, GymChaosWheelSpinProbe p, int spinnerWheels)
    {
        float ratio = p.ExpectedDegrees > 1f ? p.MeasuredDegrees / p.ExpectedDegrees : 0f;
        string line =
            $"{label.Replace(' ', '_')}:wheels={p.WheelCount} rolled={p.RolledDistance:F2} " +
            $"ratio={ratio:F3} parkedDeg={p.ParkedDegrees:F3} bodyTurn={p.MaxBodyTurn:F3} " +
            $"axleErr={p.MaxWheelAxleError:F3} boundsGrowth={p.MaxBoundsGrowth:F3}";
        Debug.Log("GYMCHAOS_WHEEL_SPIN_VEHICLE " + line);
        if (p.WheelCount < 4 || spinnerWheels != p.WheelCount)
            throw new InvalidOperationException($"{label} wheel count {p.WheelCount} spinner={spinnerWheels}. {line}");
        if (p.RolledDistance < 3f)
            throw new InvalidOperationException($"{label} did not drive far enough. {line}");
        if (ratio < 0.92f || ratio > 1.08f)
            throw new InvalidOperationException($"{label} wheel spin does not match distance/radius. {line}");
        if (p.ParkedDegrees > 0.05f)
            throw new InvalidOperationException($"{label} wheels turned while parked. {line}");
        if (p.MaxBodyTurn > 0.05f)
            throw new InvalidOperationException($"{label} a non-wheel part rotated. {line}");
        if (p.MaxBoundsGrowth > 0.01f)
            throw new InvalidOperationException($"{label} turned wheel bounds grow past the tyre. {line}");
        if (p.MaxWheelAxleError > 0.1f)
            throw new InvalidOperationException($"{label} wheel axle is not the vehicle's lateral axis. {line}");
        summary.Append(line).Append(' ');
    }

    // With -Graphics: side and three-quarter views of the vehicle right
    // after driving (wheels at an arbitrary turn) for visual review.
    private static void Capture(GameObject target, string label)
    {
        if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null || target == null)
            return;
        Renderer[] renderers = target.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return;
        Bounds bounds = renderers[0].bounds;
        foreach (Renderer r in renderers) bounds.Encapsulate(r.bounds);
        string directory = System.IO.Path.Combine(
            System.IO.Directory.GetParent(Application.dataPath).Parent.FullName, "Logs", "verify", "wheel-spin");
        System.IO.Directory.CreateDirectory(directory);
        const int Layer = 31;
        var layers = new Dictionary<Transform, int>();
        foreach (Transform t in target.GetComponentsInChildren<Transform>(true))
        {
            layers[t] = t.gameObject.layer;
            t.gameObject.layer = Layer;
        }
        Transform root = target.transform;
        float size = Mathf.Max(bounds.size.x, bounds.size.z);
        (string, Vector3)[] views =
        {
            ("side", root.right * size * 0.9f + Vector3.up * size * 0.08f),
            ("quarter", (root.right - root.forward).normalized * size * 0.85f + Vector3.up * size * 0.2f)
        };
        foreach ((string view, Vector3 offset) in views)
        {
            GameObject host = new GameObject("Wheel Spin Capture Camera");
            Camera camera = host.AddComponent<Camera>();
            camera.cullingMask = 1 << Layer;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.55f, 0.58f, 0.62f);
            camera.fieldOfView = 45f;
            Vector3 eye = bounds.center + offset;
            camera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(bounds.center - eye));
            RenderTexture rt = new RenderTexture(640, 400, 24);
            camera.targetTexture = rt;
            camera.Render();
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = rt;
            Texture2D image = new Texture2D(640, 400, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, 640, 400), 0, 0);
            image.Apply();
            RenderTexture.active = previous;
            camera.targetTexture = null;
            System.IO.File.WriteAllBytes(System.IO.Path.Combine(directory,
                $"{label.Replace(' ', '_')}-{view}.png"), image.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(image);
            rt.Release();
            UnityEngine.Object.DestroyImmediate(rt);
            UnityEngine.Object.DestroyImmediate(host);
        }
        foreach (KeyValuePair<Transform, int> entry in layers)
            if (entry.Key != null) entry.Key.gameObject.layer = entry.Value;
    }

    private static void Finish(int code)
    {
        Time.maximumDeltaTime = savedMaximumDeltaTime;
        SessionState.EraseBool(RequestedKey);
        EditorApplication.update -= Tick;
        if (Application.isBatchMode)
        {
            EditorApplication.Exit(code);
        }
        else
        {
            EditorApplication.isPlaying = false;
        }
    }
}
#endif
