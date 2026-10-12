#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Dedicated scenario for the 2026-10-12 parking change: while a car drives
/// into the car park, a passenger on foot stops only when that car is close
/// and coming its way. Passengers far away, behind the car or off its lane
/// keep walking instead of all waiting for the one moving car.
/// </summary>
[InitializeOnLoad]
public static class GymChaosParkingYieldVerifier
{
    private const string RequestedKey = "GymChaos.ParkingYieldVerificationRequested";
    private static double startedAt;
    private static bool finished;
    private static int resultCode = 1;

    static GymChaosParkingYieldVerifier()
    {
        if (!SessionState.GetBool(RequestedKey, false)) return;
        Hook();
    }

    [MenuItem("Tools/GymChaos/Run Parking Yield Verification")]
    public static void Run()
    {
        finished = false;
        resultCode = 1;
        GymChaosVerifierExit.Record(resultCode);
        GymChaosVerifierSave.Neutralize();
        SessionState.SetBool(RequestedKey, true);
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        Hook();
        EditorApplication.isPlaying = true;
    }

    private static void Hook()
    {
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }

    private static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            startedAt = EditorApplication.timeSinceStartup;
            return;
        }
        if (state != PlayModeStateChange.EnteredEditMode) return;
        EditorApplication.update -= Tick;
        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        SessionState.EraseBool(RequestedKey);
        if (Application.isBatchMode) GymChaosVerifierExit.Exit(resultCode);
    }

    private static void Tick()
    {
        if (finished || !EditorApplication.isPlaying) return;
        double now = EditorApplication.timeSinceStartup;
        GymVisitorVehicle car = null;
        try
        {
            if (now - startedAt > 90d) throw new TimeoutException("no parked car to probe");
            if (!GymOutdoorBuilder.IsBuilt || now - startedAt < 6d) return;
            foreach (GymVisitorVehicle vehicle in
                UnityEngine.Object.FindObjectsByType<GymVisitorVehicle>(FindObjectsSortMode.None))
            {
                if (vehicle != null && vehicle.gameObject.activeInHierarchy && vehicle.IsParked &&
                    !vehicle.IsCloud && !vehicle.IsBus && !vehicle.IsDriving)
                {
                    car = vehicle;
                    break;
                }
            }
            if (car == null) return;

            car.ForceParkingArrivalForVerification(true);
            Vector3 c = car.transform.position;
            Vector3 f = Vector3.ProjectOnPlane(car.transform.forward, Vector3.up).normalized;
            Vector3 r = Vector3.Cross(Vector3.up, f).normalized;
            var cases = new List<(string name, Vector3 point, bool stop)>
            {
                ("ahead-near", c + f * 5f, true),
                ("ahead-touching", c + f * 2.5f + r * 1f, true),
                ("ahead-far", c + f * 20f, false),
                ("far-across-lot", c + f * 6f + r * 14f, false),
                ("behind", c - f * 5f, false),
                ("off-lane-side", c + f * 3f + r * 6f, false)
            };
            var results = new List<string>();
            foreach ((string name, Vector3 point, bool stop) in cases)
            {
                bool actual = GymVisitorVehicle.HasConflictingParkingArrivalNear(null, point);
                results.Add($"{name}={(actual ? "stop" : "walk")}");
                if (actual != stop)
                    throw new InvalidOperationException(
                        $"{name}: expected {(stop ? "stop" : "walk")}, got {(actual ? "stop" : "walk")}");
            }
            // The passenger's own car never stops it.
            if (GymVisitorVehicle.HasConflictingParkingArrivalNear(car, c + f * 5f))
                throw new InvalidOperationException("own car blocked its passenger");
            car.ForceParkingArrivalForVerification(false);
            if (GymVisitorVehicle.HasConflictingParkingArrivalNear(null, c + f * 5f))
                throw new InvalidOperationException("parked car still blocked after the drive ended");
            Debug.Log($"GYMCHAOS_PARKING_YIELD_OK car={car.name} {string.Join(" ", results)} own=walk parked=walk");
            Finish(0);
        }
        catch (Exception exception)
        {
            if (car != null) car.ForceParkingArrivalForVerification(false);
            Debug.LogError("GYMCHAOS_PARKING_YIELD_FAIL " + exception.Message);
            Finish(1);
        }
    }

    private static void Finish(int code)
    {
        finished = true;
        resultCode = code;
        GymChaosVerifierExit.Record(resultCode);
        SessionState.EraseBool(RequestedKey);
        EditorApplication.isPlaying = false;
    }
}
#endif
