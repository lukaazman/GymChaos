using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class GymChaosVehicleTrafficVerifier
{
    private const string RequestedKey = "GymChaos.VehicleTrafficVerificationRequested";
    private static double started;
    private static double phaseStarted;
    private static int phase;
    private static GymVisitorVehicle first;
    private static GymVisitorVehicle second;
    private static GameObject pedestrian;
    private static bool firstDone;
    private static bool secondDone;
    private static bool concurrentObserved;
    private static bool yieldingObserved;
    private static bool hornObserved;
    private static bool stoppedObserved;
    private static bool resumedObserved;
    private static float minimumSeparation;
    private static Vector3 minimumFirstPosition;
    private static Vector3 minimumSecondPosition;

    [InitializeOnLoadMethod]
    private static void ResumeAfterReload()
    {
        if (!EditorPrefs.GetBool(RequestedKey, false)) return;
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
        EditorApplication.playModeStateChanged -= PlayModeChanged;
        EditorApplication.playModeStateChanged += PlayModeChanged;
    }

    public static void Run()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        EditorPrefs.SetBool(RequestedKey, true);
        ResetState();
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
        EditorApplication.playModeStateChanged -= PlayModeChanged;
        EditorApplication.playModeStateChanged += PlayModeChanged;
        Debug.Log("GYMCHAOS_VEHICLE_TRAFFIC_STARTED");
        EditorApplication.isPlaying = true;
    }

    private static void ResetState()
    {
        started = EditorApplication.timeSinceStartup;
        phaseStarted = started;
        phase = 0;
        first = null;
        second = null;
        pedestrian = null;
        firstDone = false;
        secondDone = false;
        concurrentObserved = false;
        yieldingObserved = false;
        hornObserved = false;
        stoppedObserved = false;
        resumedObserved = false;
        minimumSeparation = float.PositiveInfinity;
        minimumFirstPosition = Vector3.zero;
        minimumSecondPosition = Vector3.zero;
    }

    private static void PlayModeChanged(PlayModeStateChange change)
    {
        if (change == PlayModeStateChange.EnteredPlayMode)
        {
            ResetState();
            Time.timeScale = 3f;
            AudioListener.pause = true;
        }
        if (change != PlayModeStateChange.EnteredEditMode) return;
        EditorApplication.update -= Tick;
        EditorApplication.playModeStateChanged -= PlayModeChanged;
        if (Application.isBatchMode && EditorPrefs.GetBool(RequestedKey, false))
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
            director.enabled = false;
            switch (phase)
            {
                case 0: BeginArrivalConvoy(); break;
                case 1: TickArrivalConvoy(); break;
                case 2: BeginBidirectionalTraffic(); break;
                case 3: TickBidirectionalTraffic(); break;
                case 4: BeginPedestrianYield(); break;
                case 5: TickPedestrianYield(); break;
            }
            if (EditorApplication.timeSinceStartup - started > 90d)
                throw new InvalidOperationException("Vehicle traffic verification timed out at phase " + phase + ".");
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            EditorPrefs.DeleteKey(RequestedKey);
            EditorApplication.Exit(1);
        }
    }

    private static void BeginArrivalConvoy()
    {
        DisableSceneVehicles();
        first = GymVisitorVehicle.Create(BodybuilderIdentity.Arnold, 0, null, false);
        second = GymVisitorVehicle.Create(BodybuilderIdentity.Cbum, 2, null, false);
        first.DriveIn(() => firstDone = true);
        second.DriveIn(() => secondDone = true);
        float spawnSeparation = PlanarDistance(first.transform.position, second.transform.position);
        if (spawnSeparation < GymVisitorVehicle.SpawnSpacingForVerification - 0.1f)
            throw new InvalidOperationException($"Arrival vehicles spawned only {spawnSeparation:F2}m apart.");
        phase = 1;
        phaseStarted = EditorApplication.timeSinceStartup;
    }

    private static void TickArrivalConvoy()
    {
        TrackConcurrentTraffic();
        if (firstDone && secondDone)
        {
            if (!concurrentObserved || minimumSeparation < 2.75f)
                throw new InvalidOperationException(
                    $"Arrival convoy failed concurrent={concurrentObserved} minimum={minimumSeparation:F2} " +
                    $"firstAtMinimum={minimumFirstPosition} secondAtMinimum={minimumSecondPosition}.");
            CleanupVehicles();
            phase = 2;
            phaseStarted = EditorApplication.timeSinceStartup;
        }
        else if (EditorApplication.timeSinceStartup - phaseStarted > 28d)
            throw new InvalidOperationException(
                $"Arrival convoy did not park smoothly firstDone={firstDone} " +
                $"firstPos={first?.transform.position} firstSpeed={first?.CurrentDriveSpeedForVerification:F2} " +
                $"firstBlocker={first?.LastTrafficBlockerForVerification} secondDone={secondDone} " +
                $"secondPos={second?.transform.position} secondSpeed={second?.CurrentDriveSpeedForVerification:F2} " +
                $"secondBlocker={second?.LastTrafficBlockerForVerification}.");
    }

    private static void BeginBidirectionalTraffic()
    {
        firstDone = false;
        secondDone = false;
        concurrentObserved = false;
        minimumSeparation = float.PositiveInfinity;
        first = GymVisitorVehicle.Create(BodybuilderIdentity.Zyzz, 1, null, true);
        second = GymVisitorVehicle.Create(BodybuilderIdentity.Arnold, 3, null, false);
        first.DriveOut(() => firstDone = true);
        second.DriveIn(() => secondDone = true);
        phase = 3;
        phaseStarted = EditorApplication.timeSinceStartup;
    }

    private static void TickBidirectionalTraffic()
    {
        TrackConcurrentTraffic();
        if (firstDone && secondDone)
        {
            if (!concurrentObserved)
                throw new InvalidOperationException("Opposite lane vehicles were never moving concurrently.");
            if (minimumSeparation < 2.75f)
                throw new InvalidOperationException(
                    $"Opposite lane vehicles overlapped at {minimumSeparation:F2}m.");
            CleanupVehicles();
            phase = 4;
            phaseStarted = EditorApplication.timeSinceStartup;
        }
        else if (EditorApplication.timeSinceStartup - phaseStarted > 28d)
            throw new InvalidOperationException(
                $"Bidirectional traffic stalled outgoing={firstDone} incoming={secondDone} " +
                $"firstBlocker={first?.LastTrafficBlockerForVerification} " +
                $"secondBlocker={second?.LastTrafficBlockerForVerification}.");
    }

    private static void BeginPedestrianYield()
    {
        firstDone = false;
        yieldingObserved = false;
        hornObserved = false;
        stoppedObserved = false;
        resumedObserved = false;
        first = GymVisitorVehicle.Create(BodybuilderIdentity.Cbum, 4, null, false);
        Vector3 direction = Vector3.ProjectOnPlane(
            first.ArrivalRoadTurnPointForVerification - first.RoadPointForVerification,
            Vector3.up).normalized;
        pedestrian = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        pedestrian.name = "Traffic Verifier Pedestrian";
        pedestrian.transform.position = first.RoadPointForVerification + direction * 4.1f + Vector3.up;
        pedestrian.AddComponent<GymVisitorAgent>();
        Physics.SyncTransforms();
        first.DriveIn(() => firstDone = true);
        phase = 5;
        phaseStarted = EditorApplication.timeSinceStartup;
    }

    private static void TickPedestrianYield()
    {
        yieldingObserved |= first != null && first.IsYieldingToPedestrian;
        hornObserved |= first != null && first.HornPlayCountForVerification > 0;
        stoppedObserved |= yieldingObserved && first != null &&
            first.CurrentDriveSpeedForVerification < 0.12f;
        if (stoppedObserved && pedestrian != null)
        {
            Vector3 direction = Vector3.ProjectOnPlane(
                first.ArrivalRoadTurnPointForVerification - first.RoadPointForVerification,
                Vector3.up).normalized;
            Vector3 right = Vector3.Cross(Vector3.up, direction).normalized;
            pedestrian.transform.position += right * 5f;
            Physics.SyncTransforms();
        }
        resumedObserved |= stoppedObserved && first != null &&
            first.CurrentDriveSpeedForVerification > 2f;
        if (firstDone)
        {
            if (!yieldingObserved || !hornObserved || !stoppedObserved || !resumedObserved)
                throw new InvalidOperationException(
                    $"Pedestrian yield contract failed yield={yieldingObserved} horn={hornObserved} " +
                    $"stopped={stoppedObserved} resumed={resumedObserved}.");
            Debug.Log(
                "GYMCHAOS_VEHICLE_TRAFFIC_OK arrivalConvoy=True spawnSeparated=True " +
                "bidirectional=True oppositeLaneIgnored=True pedestrianYield=True horn=True immediateResume=True");
            CleanupVehicles();
            if (pedestrian != null) UnityEngine.Object.Destroy(pedestrian);
            EditorPrefs.DeleteKey(RequestedKey);
            EditorApplication.Exit(0);
        }
        else if (EditorApplication.timeSinceStartup - phaseStarted > 28d)
            throw new InvalidOperationException(
                $"Pedestrian yield timed out yield={yieldingObserved} horn={hornObserved} " +
                $"stopped={stoppedObserved} resumed={resumedObserved} speed={first?.CurrentDriveSpeedForVerification:F2} " +
                $"blocker={first?.LastTrafficBlockerForVerification}.");
    }

    private static void TrackConcurrentTraffic()
    {
        if (first == null || second == null || !first.IsDriving || !second.IsDriving ||
            !first.gameObject.activeInHierarchy || !second.gameObject.activeInHierarchy) return;
        concurrentObserved = true;
        float separation = PlanarDistance(first.transform.position, second.transform.position);
        if (separation < minimumSeparation)
        {
            minimumSeparation = separation;
            minimumFirstPosition = first.transform.position;
            minimumSecondPosition = second.transform.position;
        }
    }

    private static float PlanarDistance(Vector3 a, Vector3 b)
    {
        return Vector3.ProjectOnPlane(a - b, Vector3.up).magnitude;
    }

    private static void CleanupVehicles()
    {
        if (first != null) UnityEngine.Object.Destroy(first.gameObject);
        if (second != null) UnityEngine.Object.Destroy(second.gameObject);
        first = null;
        second = null;
    }

    private static void DisableSceneVehicles()
    {
        GymVisitorVehicle[] existing = UnityEngine.Object.FindObjectsByType<GymVisitorVehicle>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < existing.Length; i++)
        {
            if (existing[i] != null) existing[i].gameObject.SetActive(false);
        }
    }
}
