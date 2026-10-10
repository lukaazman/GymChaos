#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using Unity.Profiling;
using UnityEngine;

/// <summary>
/// Frame-time benchmark over representative scenarios. Run with graphics
/// (Invoke-UnityCheck -Graphics): the main camera renders into a render
/// texture every frame so rendering and planar mirrors are included.
/// Writes Logs/verify/perf-&lt;label&gt;.json and logs one GYMCHAOS_PERF line per
/// scenario. Set GYMCHAOS_PERF_LABEL to name the run (baseline, final, ...).
/// </summary>
[InitializeOnLoad]
public static class GymChaosPerformanceVerifier
{
    private const string RequestedKey = "GymChaos.PerformanceVerificationRequested";
    private const float WarmupSeconds = 12f;
    private const float SettleSeconds = 2f;
    private const int SampleFrames = 150;

    private struct Scenario
    {
        public string Name;
        public Func<Vector3> Position;
        public float Yaw;
        // Time of day (0..1); 0.5 is noon, 0.95 is full night.
        public float Time01;
    }

    private static readonly List<float> frameMs = new List<float>(SampleFrames);
    private static Scenario[] scenarios;
    private static int scenarioIndex;
    private static double phaseStarted;
    private static bool sampling;
    private static long allocatedAtStart;
    private static int gcAtStart;
    private static double started;
    private static bool warmedUp;
    private static int resultCode = 1;
    private static RenderTexture target;
    private static readonly StringBuilder json = new StringBuilder();
    // Main-thread cost split per frame (ms), averaged over the samples.
    private static readonly string[] Markers =
    {
        "FixedUpdate.PhysicsFixedUpdate",
        "GymChaos.GlbTextureDecode",
        "GymChaos.GlbMeshBuild",
        "Shader.CreateGPUProgram",
        "PlayerLoop",
        "FixedUpdate.ScriptRunBehaviourFixedUpdate",
        "Update.ScriptRunBehaviourUpdate",
        "PreLateUpdate.ScriptRunBehaviourLateUpdate",
        "PostLateUpdate.FinishFrameRendering",
        "GymChaos.FixedUpdate",
        "GymChaos.TickRoaming",
        "GymChaos.BuildRoamRoute",
        "GymChaos.FindRoamPoint",
        "GymChaos.FindPurposefulRoam",
        "GymChaos.CollectRoamInterests",
        "GymChaos.VisitorDirection",
        "GymChaos.VisitorPathClear",
        "GymChaos.NavSegment",
        "GC.Collect",
        "GymChaos.PunchContact",
        "GymChaos.GroundedRoot",
        "GymChaos.VisitorTick",
        "GymChaos.Late.MixamoScanRetargetAnimator",
        "GymChaos.Late.EnemyMeshHitboxRig",
        "GymChaos.Late.PlayerHandRig",
        "Camera.Render",
        "Gfx.WaitForPresentOnGfxThread"
    };
    // Render counters averaged per frame (raw counts, not time).
    private static readonly string[] Counters =
    {
        "Batches Count", "SetPass Calls Count", "Triangles Count",
        "Shadow Casters Count", "Visible Skinned Meshes Count"
    };
    private static ProfilerRecorder[] counterRecorders;
    private static double[] counterSums;
    private static ProfilerRecorder[] recorders;
    private static double[] markerSums;
    private static int fixedStepsAtStart;

    static GymChaosPerformanceVerifier()
    {
        if (SessionState.GetBool(RequestedKey, false)) Hook();
    }

    [MenuItem("Tools/GymChaos/Run Performance Verification")]
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
        EditorApplication.playModeStateChanged -= PlayModeChanged;
        EditorApplication.playModeStateChanged += PlayModeChanged;
    }

    private static void PlayModeChanged(PlayModeStateChange change)
    {
        if (change == PlayModeStateChange.EnteredPlayMode)
        {
            started = EditorApplication.timeSinceStartup;
            Application.runInBackground = true;
            warmedUp = false;
            scenarioIndex = 0;
            sampling = false;
            json.Clear();
            BuildScenarios();
        }
        if (change != PlayModeStateChange.EnteredEditMode) return;
        EditorApplication.update -= Tick;
        EditorApplication.playModeStateChanged -= PlayModeChanged;
        SessionState.EraseBool(RequestedKey);
        if (Application.isBatchMode) GymChaosVerifierExit.Exit(resultCode);
    }

    private static void BuildScenarios()
    {
        scenarios = new[]
        {
            new Scenario { Name = "mainGymMirror", Yaw = -90f, Position = () =>
                GymDoorway.Instance.InteriorPoint + Vector3.left * 8f },
            new Scenario { Name = "lockerMirror", Yaw = 90f, Position = () =>
                GymBackRoomBuilder.TryGetRoomBounds(out Bounds room)
                    ? new Vector3(room.center.x + 1.5f, GymDoorway.Instance.InteriorPoint.y, room.center.z)
                    : GymDoorway.Instance.InteriorPoint },
            new Scenario { Name = "outdoorParking", Yaw = -90f, Position = () =>
                new Vector3(GymOutdoorBuilder.ParkingBounds.max.x - 2f,
                    GymDoorway.Instance.ExteriorPoint.y, GymOutdoorBuilder.ParkingBounds.center.z) },
            new Scenario { Name = "proteinStore", Yaw = 90f, Position = () =>
                GymProteinStoreEnvironment.StoreFrontClearPoint + Vector3.right * 1f },
            new Scenario { Name = "roadBusStop", Yaw = 80f, Position = () =>
                new Vector3(168f, GymDoorway.Instance.ExteriorPoint.y, 26f) },
            new Scenario { Name = "mainGymMirrorNight", Yaw = -90f, Time01 = 0.95f, Position = () =>
                GymDoorway.Instance.InteriorPoint + Vector3.left * 8f },
            new Scenario { Name = "outdoorParkingNight", Yaw = -90f, Time01 = 0.95f, Position = () =>
                new Vector3(GymOutdoorBuilder.ParkingBounds.max.x - 2f,
                    GymDoorway.Instance.ExteriorPoint.y, GymOutdoorBuilder.ParkingBounds.center.z) },
            new Scenario { Name = "proteinStoreNight", Yaw = 90f, Time01 = 0.95f, Position = () =>
                GymProteinStoreEnvironment.StoreFrontClearPoint + Vector3.right * 1f },
            new Scenario { Name = "roadBusStopNight", Yaw = 80f, Time01 = 0.95f, Position = () =>
                new Vector3(168f, GymDoorway.Instance.ExteriorPoint.y, 26f) }
        };
        for (int i = 0; i < scenarios.Length; i++)
        {
            if (scenarios[i].Time01 <= 0f) scenarios[i].Time01 = 0.5f;
        }
    }

    private static void Tick()
    {
        if (!EditorApplication.isPlaying || scenarios == null) return;
        double now = EditorApplication.timeSinceStartup;
        try
        {
            if (!warmedUp)
            {
                if (!GymOutdoorBuilder.IsBuilt || !GymProteinStoreEnvironment.IsLoaded ||
                    GymDoorway.Instance == null)
                {
                    if (now - started > 90d) throw new TimeoutException("World did not build.");
                    return;
                }
                if (now - started < WarmupSeconds) return;
                warmedUp = true;
                AttachRenderTarget();
                BeginScenario(now);
                return;
            }

            if (!sampling)
            {
                if (now - phaseStarted < SettleSeconds) return;
                sampling = true;
                frameMs.Clear();
                StartRecorders();
                fixedStepsAtStart = fixedSteps;
                allocatedAtStart = GC.GetAllocatedBytesForCurrentThread();
                gcAtStart = GC.CollectionCount(0);
                return;
            }

            float frame = Time.unscaledDeltaTime * 1000f;
            frameMs.Add(frame);
            for (int i = 0; i < recorders.Length; i++)
                if (recorders[i].Valid) markerSums[i] += recorders[i].LastValue / 1e6;
            if (frame > SpikeMs) LogSpike(frame);
            for (int i = 0; i < counterRecorders.Length; i++)
                if (counterRecorders[i].Valid) counterSums[i] += counterRecorders[i].LastValue;
            if (frameMs.Count < SampleFrames) return;

            ReportScenario();
            scenarioIndex++;
            if (scenarioIndex >= scenarios.Length)
            {
                WriteReport();
                resultCode = 0; GymChaosVerifierExit.Record(resultCode);
                Finish();
                return;
            }
            BeginScenario(now);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            resultCode = 1; GymChaosVerifierExit.Record(resultCode);
            Finish();
        }
    }

    private const float SpikeMs = 45f;

    // Names the markers that took the time in a hitch frame. LastValue is
    // the previous frame's sample, which is the frame whose delta just ended.
    private static void LogSpike(float frame)
    {
        var parts = new System.Text.StringBuilder();
        for (int i = 0; i < recorders.Length; i++)
        {
            if (!recorders[i].Valid) continue;
            double ms = recorders[i].LastValue / 1e6;
            if (ms >= 2.0) parts.Append(' ').Append(Markers[i]).Append('=').Append(ms.ToString("F1"));
        }
        Debug.Log($"GYMCHAOS_PERF_SPIKE scenario={scenarios[scenarioIndex].Name} frameMs={frame:F1} " +
            $"frame={Time.frameCount}{parts}");
    }

    private static int fixedSteps;
    private sealed class FixedStepCounter : MonoBehaviour
    {
        private void FixedUpdate() => fixedSteps++;
    }

    private static void StartRecorders()
    {
        recorders = new ProfilerRecorder[Markers.Length];
        markerSums = new double[Markers.Length];
        for (int i = 0; i < Markers.Length; i++)
            recorders[i] = ProfilerRecorder.StartNew(ProfilerCategory.Scripts, Markers[i]);
        counterRecorders = new ProfilerRecorder[Counters.Length];
        counterSums = new double[Counters.Length];
        for (int i = 0; i < Counters.Length; i++)
            counterRecorders[i] = ProfilerRecorder.StartNew(ProfilerCategory.Render, Counters[i]);
    }

    private static void AttachRenderTarget()
    {
        PlayerMovement player = UnityEngine.Object.FindFirstObjectByType<PlayerMovement>();
        Camera camera = player != null ? player.playerCamera : Camera.main;
        if (camera == null) throw new InvalidOperationException("No player camera.");
        new GameObject("Perf Fixed Step Counter").AddComponent<FixedStepCounter>();
        target = new RenderTexture(1920, 1080, 24);
        camera.targetTexture = target;
    }

    private static void BeginScenario(double now)
    {
        Scenario scenario = scenarios[scenarioIndex];
        PlayerMovement player = UnityEngine.Object.FindFirstObjectByType<PlayerMovement>();
        Vector3 position = scenario.Position();
        position.y += 0.05f;
        Quaternion rotation = Quaternion.Euler(0f, scenario.Yaw, 0f);
        Rigidbody body = player.GetComponent<Rigidbody>();
        if (body != null)
        {
            body.position = position;
            body.rotation = rotation;
            body.linearVelocity = Vector3.zero;
        }
        player.transform.SetPositionAndRotation(position, rotation);
        GymTimeOfDay.Instance?.SetTimeForVerification(scenario.Time01);
        phaseStarted = now;
        sampling = false;
    }

    private static void ReportScenario()
    {
        List<float> sorted = new List<float>(frameMs);
        sorted.Sort();
        float sum = 0f;
        for (int i = 0; i < sorted.Count; i++) sum += sorted[i];
        float avg = sum / sorted.Count;
        float p50 = sorted[sorted.Count / 2];
        float p95 = sorted[(int)(sorted.Count * 0.95f)];
        float p99 = sorted[(int)(sorted.Count * 0.99f)];
        float max = sorted[sorted.Count - 1];
        long allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedAtStart;
        float kbPerFrame = allocated / 1024f / sorted.Count;
        int collections = GC.CollectionCount(0) - gcAtStart;
        string name = scenarios[scenarioIndex].Name;
        string line = string.Format(CultureInfo.InvariantCulture,
            "scenario={0} avgMs={1:F2} p50Ms={2:F2} p95Ms={3:F2} p99Ms={4:F2} maxMs={5:F2} " +
            "gcKBPerFrame={6:F1} gcCollections={7}",
            name, avg, p50, p95, p99, max, kbPerFrame, collections);
        Debug.Log("GYMCHAOS_PERF " + line);
        StringBuilder split = new StringBuilder();
        for (int i = 0; i < recorders.Length; i++)
        {
            split.Append(Markers[i]).Append('=')
                .Append((markerSums[i] / sorted.Count).ToString("F2", CultureInfo.InvariantCulture))
                .Append(recorders[i].Valid ? "" : "(n/a)").Append(' ');
            recorders[i].Dispose();
        }
        StringBuilder render = new StringBuilder();
        for (int i = 0; i < counterRecorders.Length; i++)
        {
            render.Append(Counters[i].Replace(" Count", "").Replace(" ", "")).Append('=')
                .Append((counterSums[i] / sorted.Count).ToString("F0", CultureInfo.InvariantCulture))
                .Append(counterRecorders[i].Valid ? "" : "(n/a)").Append(' ');
            counterRecorders[i].Dispose();
        }
        Debug.Log($"GYMCHAOS_PERF_RENDER scenario={name} {render}");
        Debug.Log($"GYMCHAOS_PERF_SPLIT scenario={name} fixedStepsPerFrame=" +
            ((fixedSteps - fixedStepsAtStart) / (float)sorted.Count).ToString("F2", CultureInfo.InvariantCulture) +
            " " + split);
        if (json.Length > 0) json.Append(",\n");
        json.Append(string.Format(CultureInfo.InvariantCulture,
            "\"{0}\":{{\"avgMs\":{1:F2},\"p50Ms\":{2:F2},\"p95Ms\":{3:F2},\"p99Ms\":{4:F2}," +
            "\"maxMs\":{5:F2},\"gcKBPerFrame\":{6:F1},\"gcCollections\":{7}}}",
            name, avg, p50, p95, p99, max, kbPerFrame, collections));
    }

    private static void WriteReport()
    {
        string label = Environment.GetEnvironmentVariable("GYMCHAOS_PERF_LABEL");
        if (string.IsNullOrEmpty(label)) label = "latest";
        string directory = Path.Combine(
            Directory.GetParent(Application.dataPath).Parent.FullName, "Logs", "verify");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, $"perf-{label}.json");
        File.WriteAllText(path, "{\n" + json + "\n}\n");
        Debug.Log($"GYMCHAOS_PERFORMANCE_VERIFICATION_OK scenarios={scenarios.Length} " +
            $"frames={SampleFrames} graphics={SystemInfo.graphicsDeviceType} report={path}");
    }

    private static void Finish()
    {
        if (target != null)
        {
            PlayerMovement player = UnityEngine.Object.FindFirstObjectByType<PlayerMovement>();
            if (player != null && player.playerCamera != null) player.playerCamera.targetTexture = null;
            target.Release();
            UnityEngine.Object.DestroyImmediate(target);
            target = null;
        }
        EditorApplication.update -= Tick;
        EditorApplication.isPlaying = false;
    }
}
#endif
