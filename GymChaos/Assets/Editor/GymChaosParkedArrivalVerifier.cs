#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Soak check for visitors arriving by car: several arrivals are forced at
/// once (they contend for the shared parking connector and the doorway), and
/// every visitor who left a vehicle must walk on to the gym. A visitor that
/// stands outside in the arrival state without moving for longer than the
/// stall window is the "idles in front of the car" bug. Also checks the free
/// roam walking pace range of the enemies already inside.
/// </summary>
[InitializeOnLoad]
public static class GymChaosParkedArrivalVerifier
{
    private const string RequestedKey = "GymChaos.ParkedArrivalVerificationRequested";
    private const int ForcedArrivals = 3;
    // Idling by the car is the reported bug; a short door queue further on is
    // normal, but a longer stop there means a deadlock.
    private const double StallWindowSeconds = 10d;
    private const double QueueStallWindowSeconds = 30d;
    private const float NearCarRadius = 4f;
    private const double ScenarioSeconds = 300d;
    private const double LateArrivalGraceSeconds = 45d;

    private sealed class Track
    {
        public GymVisitorAgent agent;
        public Vector3 anchor;
        public double anchorTime;
        public bool entered;
        public bool loggedArrival;
        public double arrivalStarted;
        public bool wasArriving;
        public Vector3 carPosition;
        public bool carPositionSet;
    }

    private static readonly Dictionary<GymVisitorAgent, Track> tracks =
        new Dictionary<GymVisitorAgent, Track>();
    private static double startedAt;
    private static double lastSample;
    private static bool setupComplete;
    private static bool finished;
    private static int forced;
    private static int completedArrivals;
    private static int resultCode;
    private static float roamMin = float.PositiveInfinity;
    private static float roamMax;
    private static int roamSamples;
    private static int roamSpeedChanges;
    private static readonly Dictionary<EnemyFighter, float> lastRoamSpeed =
        new Dictionary<EnemyFighter, float>();
    private static readonly Dictionary<EnemyFighter, float> lastRoamChangeTime =
        new Dictionary<EnemyFighter, float>();

    static GymChaosParkedArrivalVerifier()
    {
        if (!SessionState.GetBool(RequestedKey, false)) return;
        Hook();
    }

    [MenuItem("Tools/GymChaos/Run Parked Arrival Verification")]
    public static void Run()
    {
        tracks.Clear();
        lastRoamSpeed.Clear();
        lastRoamChangeTime.Clear();
        setupComplete = false;
        finished = false;
        forced = 0;
        completedArrivals = 0;
        roamMin = float.PositiveInfinity;
        roamMax = 0f;
        roamSamples = 0;
        roamSpeedChanges = 0;
        resultCode = 1; GymChaosVerifierExit.Record(resultCode);
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
            GymChaosVerifierClock.BeginFixedStep();
            startedAt = GymChaosVerifierClock.Now;
            lastSample = startedAt;
            return;
        }
        if (change != PlayModeStateChange.EnteredEditMode) return;
        GymChaosVerifierClock.EndFixedStep();
        EditorApplication.update -= Tick;
        EditorApplication.playModeStateChanged -= PlayModeChanged;
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
            if (director == null) return;

            if (!setupComplete && elapsed > 3d)
            {
                // Only fresh scheduled arrivals: the fallback reset path of
                // BeginEntryForVerification would restart a visitor already
                // on its way, which is not a real game situation.
                HashSet<BodybuilderIdentity> seen = new HashSet<BodybuilderIdentity>();
                while (forced < ForcedArrivals &&
                    director.BeginEntryForVerification(out EnemyFighter fighter) &&
                    seen.Add(fighter.Identity))
                {
                    forced++;
                    Debug.Log($"GYMCHAOS_PARKED_ARRIVAL_FORCED enemy={fighter.Identity}");
                }
                setupComplete = true;
            }
            if (!setupComplete || now - lastSample < 0.5d) return;

            // A lane-load stall can stretch the gap; only compare close samples.
            SampleRoamSpeeds(now - lastSample <= 0.6d);
            lastSample = now;
            foreach (GymVisitorAgent agent in UnityEngine.Object.FindObjectsByType<GymVisitorAgent>(
                FindObjectsSortMode.None))
            {
                SampleAgent(agent, now);
                if (finished) return;
            }

            if (elapsed >= ScenarioSeconds)
            {
                Finish();
            }
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            Fail(exception.Message);
        }
    }

    private static void SampleAgent(GymVisitorAgent agent, double now)
    {
        if (agent == null || agent.Fighter == null || !agent.isActiveAndEnabled) return;
        EnemyFighter fighter = agent.Fighter;
        if (!tracks.TryGetValue(agent, out Track track))
        {
            track = new Track { agent = agent, anchor = fighter.VisitorPhysicsPosition, anchorTime = now };
            tracks.Add(agent, track);
        }

        bool arriving = agent.State == GymVisitorAgent.VisitorState.ApproachingGymFromVehicle;
        if (!arriving)
        {
            track.wasArriving = false;
            if (agent.State == GymVisitorAgent.VisitorState.EnteringDoor ||
                agent.State == GymVisitorAgent.VisitorState.EnteringRoom)
            {
                if (!track.entered && track.loggedArrival)
                {
                    completedArrivals++;
                    Debug.Log($"GYMCHAOS_PARKED_ARRIVAL_REACHED_DOOR enemy={fighter.Identity}");
                }
                track.entered = true;
            }
            track.anchor = fighter.VisitorPhysicsPosition;
            track.anchorTime = now;
            return;
        }

        if (!track.wasArriving)
        {
            // A new arrival (first visit or a later return by car).
            track.arrivalStarted = now;
            track.entered = false;
            track.carPositionSet = false;
            track.anchor = fighter.VisitorPhysicsPosition;
            track.anchorTime = now;
        }
        track.wasArriving = true;
        track.loggedArrival = true;
        Vector3 position = fighter.VisitorPhysicsPosition;
        if (Vector3.ProjectOnPlane(position - track.anchor, Vector3.up).magnitude > 0.6f)
        {
            track.anchor = position;
            track.anchorTime = now;
            return;
        }

        if (!track.carPositionSet)
        {
            string vehicleName = fighter.Identity + " Visitor Vehicle";
            foreach (GymVisitorVehicle vehicle in UnityEngine.Object.FindObjectsByType<GymVisitorVehicle>(
                FindObjectsSortMode.None))
            {
                if (vehicle.name == vehicleName && vehicle.IsParked)
                {
                    track.carPosition = vehicle.PassengerPoint;
                    track.carPositionSet = true;
                }
            }
        }
        bool nearCar = track.carPositionSet && Vector3.ProjectOnPlane(position - track.carPosition, Vector3.up).magnitude <
            NearCarRadius;
        if (now - track.anchorTime > (nearCar ? StallWindowSeconds : QueueStallWindowSeconds))
        {
            foreach (GymVisitorAgent other in UnityEngine.Object.FindObjectsByType<GymVisitorAgent>(
                FindObjectsSortMode.None))
            {
                if (other == null || other.Fighter == null || !other.isActiveAndEnabled) continue;
                Debug.Log(
                    $"GYMCHAOS_PARKED_ARRIVAL_AGENT enemy={other.Fighter.Identity} state={other.State} " +
                    $"position={other.Fighter.VisitorPhysicsPosition} target={other.TravelTargetForVerification} " +
                    $"stage={other.VehicleStageForVerification} connector={other.IsUsingSharedParkingConnector} " +
                    $"blocker={other.Fighter.LastVisitorRouteBlocker} waitingCorridor={other.IsWaitingForSharedCorridor} " +
                    $"waitOwner={other.DoorwayRouteWaitOwnerForVerification} " +
                    $"entryYield={other.IsDoorwayEntryYieldingForVerification} " +
                    $"exitClear={other.IsDoorwayExitClearForVerification}");
            }
            foreach (GymVisitorVehicle vehicle in UnityEngine.Object.FindObjectsByType<GymVisitorVehicle>(
                FindObjectsSortMode.None))
            {
                Debug.Log(
                    $"GYMCHAOS_PARKED_ARRIVAL_VEHICLE name={vehicle.name} driving={vehicle.IsDriving} " +
                    $"parked={vehicle.IsParked} position={vehicle.transform.position} " +
                    $"yielding={vehicle.IsYieldingToPedestrian} speed={vehicle.CurrentDriveSpeedForVerification:F2} " +
                    $"blocker={vehicle.LastTrafficBlockerForVerification}");
            }
            Fail(
                $"enemy={fighter.Identity} idled outside for {now - track.anchorTime:F1}s nearCar={nearCar} " +
                $"state={agent.State} position={position} " +
                $"target={agent.TravelTargetForVerification} " +
                $"stage={agent.VehicleStageForVerification} " +
                $"detour={agent.VehicleRouteDetourActiveForVerification} " +
                $"stalled={agent.VehicleRouteStalledSecondsForVerification:F1} " +
                $"blocker={fighter.LastVisitorRouteBlocker} " +
                $"reserved={GymVisitorAgent.IsVehicleApproachReserved} " +
                $"connector={agent.IsUsingSharedParkingConnector}");
        }
    }

    private static void SampleRoamSpeeds(bool compareWithPrevious)
    {
        foreach (EnemyFighter fighter in UnityEngine.Object.FindObjectsByType<EnemyFighter>(
            FindObjectsSortMode.None))
        {
            if (fighter == null || fighter.IsDead || fighter.IsAggressive) continue;
            float speed = fighter.RoamSpeedForVerification;
            if (speed <= 0f) continue;
            if (speed < fighter.RoamSpeedMinForVerification - 0.001f ||
                speed > fighter.RoamSpeedMaxForVerification + 0.001f)
            {
                Fail($"roam speed {speed:F3} outside [{fighter.RoamSpeedMinForVerification:F3}, " +
                    $"{fighter.RoamSpeedMaxForVerification:F3}] enemy={fighter.Identity}");
                return;
            }
            roamMin = Mathf.Min(roamMin, speed);
            roamMax = Mathf.Max(roamMax, speed);
            roamSamples++;
            // A visitor reset re-rolls the pace at once and moves the next
            // change time; only eased drift within one interval is compared.
            float changeTime = fighter.NextRoamSpeedChangeTimeForVerification;
            bool sameInterval = lastRoamChangeTime.TryGetValue(fighter, out float previousChange) &&
                Mathf.Approximately(previousChange, changeTime);
            lastRoamChangeTime[fighter] = changeTime;
            if (compareWithPrevious && sameInterval &&
                lastRoamSpeed.TryGetValue(fighter, out float previous) &&
                Mathf.Abs(previous - speed) > 0.0001f)
            {
                roamSpeedChanges++;
                // Eased drift: two samples 0.5 s apart may differ only a little.
                if (Mathf.Abs(previous - speed) > 0.3f * 0.5f + 0.02f)
                {
                    Fail($"roam speed jumped {previous:F3}->{speed:F3} enemy={fighter.Identity}");
                    return;
                }
            }
            lastRoamSpeed[fighter] = speed;
        }
    }

    private static void Finish()
    {
        // Completed arrivals over the whole run, plus any arrival still open:
        // one that began just before the end may still be walking.
        int entered = completedArrivals;
        int arrivals = completedArrivals;
        double now = GymChaosVerifierClock.Now;
        foreach (Track track in tracks.Values)
        {
            if (track.loggedArrival && !track.entered &&
                now - track.arrivalStarted >= LateArrivalGraceSeconds)
            {
                arrivals++;
            }
        }
        if (forced < 1 || arrivals < 1 || entered < arrivals)
        {
            Fail($"forced={forced} arrivals={arrivals} entered={entered}");
            return;
        }
        if (roamSamples == 0 || roamSpeedChanges == 0)
        {
            Fail($"roam speed never varied samples={roamSamples} changes={roamSpeedChanges}");
            return;
        }
        finished = true;
        resultCode = 0; GymChaosVerifierExit.Record(resultCode);
        Debug.Log(
            $"GYMCHAOS_PARKED_ARRIVAL_OK forced={forced} arrivals={arrivals} entered={entered} " +
            $"roamMin={roamMin:F2} roamMax={roamMax:F2} roamChanges={roamSpeedChanges}");
        EditorApplication.isPlaying = false;
    }

    private static void Fail(string reason)
    {
        if (finished) return;
        finished = true;
        resultCode = 1; GymChaosVerifierExit.Record(resultCode);
        Debug.LogError($"GYMCHAOS_PARKED_ARRIVAL_FAILED {reason}");
        EditorApplication.isPlaying = false;
    }
}
#endif
