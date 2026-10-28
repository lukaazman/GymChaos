#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Live scenario for the 2026-10-12 parking change: a passenger walks from
/// its parked car to the gym while further cars are sent into the car park.
/// While those cars drive in and are far from the passenger, the passenger
/// must keep walking (the old rule froze every passenger for any arriving
/// car near the connector).
/// </summary>
[InitializeOnLoad]
public static class GymChaosParkingTrafficScenarioVerifier
{
    private const string RequestedKey = "GymChaos.ParkingTrafficScenarioRequested";
    private const float FarFromCar = 12f;
    private const double ScenarioSeconds = 240d;
    private const int MaxExtraArrivals = 7;

    private static double startedAt;
    private static double lastSample;
    private static double lastForce;
    private static bool finished;
    private static int resultCode = 1;
    private static int forced;
    private static int farSamples;
    private static int farMoving;
    private static int nearSamples;
    private static readonly Dictionary<GymVisitorAgent, Vector3> lastPositions =
        new Dictionary<GymVisitorAgent, Vector3>();
    private static readonly HashSet<BodybuilderIdentity> forcedIdentities =
        new HashSet<BodybuilderIdentity>();

    static GymChaosParkingTrafficScenarioVerifier()
    {
        if (!SessionState.GetBool(RequestedKey, false)) return;
        Hook();
    }

    [MenuItem("Tools/GymChaos/Run Parking Traffic Scenario Verification")]
    public static void Run()
    {
        finished = false;
        resultCode = 1;
        forced = farSamples = farMoving = nearSamples = 0;
        lastPositions.Clear();
        forcedIdentities.Clear();
        GymChaosVerifierExit.Record(resultCode);
        GymChaosVerifierSave.Neutralize();
        SessionState.SetBool(RequestedKey, true);
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        Hook();
        EditorApplication.isPlaying = true;
    }

    private static void Hook() => GymChaosVerifierHooks.Hook(Tick, OnPlayModeStateChanged);

    private static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            GymChaosVerifierClock.BeginFixedStep();
            startedAt = GymChaosVerifierClock.Now;
            lastSample = startedAt;
            lastForce = double.NegativeInfinity;
            return;
        }
        if (state != PlayModeStateChange.EnteredEditMode) return;
        GymChaosVerifierClock.EndFixedStep();
        EditorApplication.update -= Tick;
        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        SessionState.EraseBool(RequestedKey);
        if (Application.isBatchMode) GymChaosVerifierExit.Exit(resultCode);
    }

    private static void Tick()
    {
        if (finished || !EditorApplication.isPlaying) return;
        try
        {
            double now = GymChaosVerifierClock.Now;
            double elapsed = now - startedAt;
            GymVisitorDirector director = UnityEngine.Object.FindAnyObjectByType<GymVisitorDirector>();
            if (director == null || elapsed < 3d) return;

            GymVisitorAgent[] agents =
                UnityEngine.Object.FindObjectsByType<GymVisitorAgent>(FindObjectsSortMode.None);
            bool someoneWalking = false;
            BodybuilderIdentity walker = default;
            foreach (GymVisitorAgent agent in agents)
            {
                if (IsWalkingFromCar(agent))
                {
                    someoneWalking = true;
                    walker = agent.Fighter.Identity;
                }
            }

            // First arrival, then a new car each time a passenger is on foot,
            // so a car drives in while somebody walks across the car park.
            bool wantArrival = (forced == 0 && double.IsNegativeInfinity(lastForce)) ||
                (someoneWalking && now - lastForce > 5d);
            if (wantArrival && forced < MaxExtraArrivals + 1)
            {
                lastForce = now;
                // Never restart the passenger who is walking: that is the
                // one being measured.
                bool started = someoneWalking
                    ? director.BeginEntryForVerification(walker, out EnemyFighter fighter)
                    : director.BeginEntryForVerification(out fighter);
                if (started)
                {
                    forced++;
                    forcedIdentities.Add(fighter.Identity);
                    Debug.Log($"GYMCHAOS_PARKING_SCENARIO_FORCED enemy={fighter.Identity} " +
                              $"walking={(someoneWalking ? walker.ToString() : "none")}");
                }
                // No scheduled visitor free right now: try again shortly.
            }

            if (now - lastSample < 0.5d) return;
            // Lane load can stretch the sample gap; judge walking by speed.
            float gap = (float)(now - lastSample);
            lastSample = now;
            foreach (GymVisitorAgent agent in agents)
            {
                Sample(agent, gap);
            }

            if (farSamples >= 6 || elapsed >= ScenarioSeconds)
            {
                Finish();
            }
        }
        catch (Exception exception)
        {
            Fail(exception.Message);
        }
    }

    private static bool IsWalkingFromCar(GymVisitorAgent agent) =>
        agent != null && agent.isActiveAndEnabled && agent.Fighter != null &&
        agent.Fighter.gameObject.activeInHierarchy &&
        agent.State == GymVisitorAgent.VisitorState.ApproachingGymFromVehicle;

    private static void Sample(GymVisitorAgent agent, float gap)
    {
        if (!IsWalkingFromCar(agent))
        {
            if (agent != null) lastPositions.Remove(agent);
            return;
        }
        Vector3 position = agent.Fighter.VisitorPhysicsPosition;
        bool hadPrevious = lastPositions.TryGetValue(agent, out Vector3 previous);
        lastPositions[agent] = position;
        if (!hadPrevious || gap > 2f) return;
        float moved = Vector3.ProjectOnPlane(position - previous, Vector3.up).magnitude;
        if (moved > 6f) return; // spawn placement, not walking

        string ownCar = agent.Fighter.Identity + " Visitor Vehicle";
        float nearest = float.MaxValue;
        foreach (GymVisitorVehicle vehicle in
            UnityEngine.Object.FindObjectsByType<GymVisitorVehicle>(FindObjectsSortMode.None))
        {
            if (vehicle == null || vehicle.name == ownCar ||
                !vehicle.IsDrivingIntoParkingForVerification) continue;
            nearest = Mathf.Min(nearest,
                Vector3.ProjectOnPlane(vehicle.transform.position - position, Vector3.up).magnitude);
        }
        if (nearest == float.MaxValue) return;
        if (nearest >= FarFromCar)
        {
            farSamples++;
            if (moved / Mathf.Max(0.1f, gap) > 0.25f) farMoving++;
            Debug.Log($"GYMCHAOS_PARKING_SCENARIO_SAMPLE enemy={agent.Fighter.Identity} " +
                      $"carDistance={nearest:F1} moved={moved:F2}");
        }
        else
        {
            nearSamples++;
        }
    }

    private static void Finish()
    {
        if (farSamples < 2)
        {
            Fail($"no passenger walked while another car drove in far away: far={farSamples} " +
                 $"near={nearSamples} forced={forced}");
            return;
        }
        if (farMoving * 4 < farSamples * 3)
        {
            Fail($"passengers far from the arriving car stood still: moving={farMoving}/{farSamples}");
            return;
        }
        finished = true;
        resultCode = 0;
        GymChaosVerifierExit.Record(resultCode);
        Debug.Log($"GYMCHAOS_PARKING_SCENARIO_OK forced={forced} farSamples={farSamples} " +
                  $"farMoving={farMoving} nearSamples={nearSamples}");
        EditorApplication.isPlaying = false;
    }

    private static void Fail(string reason)
    {
        if (finished) return;
        finished = true;
        resultCode = 1;
        GymChaosVerifierExit.Record(resultCode);
        Debug.LogError("GYMCHAOS_PARKING_SCENARIO_FAIL " + reason);
        EditorApplication.isPlaying = false;
    }
}
#endif
