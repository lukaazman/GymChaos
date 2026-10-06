#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class GymChaosVehicleDepartureVerifier
{
    private const string RequestedKey = "GymChaos.VehicleDepartureVerificationRequested";
    private const string GokuModeKey = "GymChaos.VehicleDepartureVerificationGoku";
    private const string ZyzzModeKey = "GymChaos.VehicleDepartureVerificationZyzz";
    private const string CbumModeKey = "GymChaos.VehicleDepartureVerificationCbum";
    private const string ArnoldModeKey = "GymChaos.VehicleDepartureVerificationArnold";
    private const string RouteModeKey = "GymChaos.VehicleRouteVerificationMode";
    private const string AllModeKey = "GymChaos.VehicleDepartureVerificationAll";
    private static double started;
    private static bool requested;
    private static EnemyFighter fighter;
    private static GymVisitorVehicle vehicle;
    private static int approachVersion;
    private static readonly HashSet<int> routeStages = new HashSet<int>();
    private static readonly List<EnemyFighter> allFighters = new List<EnemyFighter>();
    private static readonly List<GymVisitorVehicle> allVehicles = new List<GymVisitorVehicle>();
    private static readonly Dictionary<EnemyFighter, int> allApproachVersions =
        new Dictionary<EnemyFighter, int>();
    private static bool parkingVisualCaptured;
    private static bool gokuRiderObservedInFlight;
    private static bool arnoldParkedPoseVerified;
    private static float closestConcurrentVehicleDistance = float.PositiveInfinity;
    private static bool concurrentVehiclePairObserved;
    private static bool routeSampleInitialized;
    private static Vector3 lastRouteSamplePosition;
    private static Vector3 lastRouteMotionDirection;
    private static Vector3 lastRouteTarget;
    private static float maximumRouteMotionTurn;
    private static float maximumRouteMotionTurnRate;
    private static float maximumRouteMotionTurnInterval;
    private static float maximumRouteMotionTurnTargetDistance;
    private static double lastRouteMotionSampleTime;
    private static float minimumRouteTargetAlignment;
    private static int routeMotionSamples;
    private static Vector3 minimumAlignmentPosition;
    private static Vector3 minimumAlignmentTarget;
    private static Vector3 minimumAlignmentMotion;
    private static int minimumAlignmentStage;
    private static Vector3 maximumTurnPosition;
    private static Vector3 maximumTurnTarget;
    private static Vector3 maximumTurnFrom;
    private static Vector3 maximumTurnTo;
    private static int maximumTurnStage;
    private static string maximumTurnBlocker;

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
        GymChaosVerifierPrefs.SetBool(GokuModeKey, false);
        GymChaosVerifierPrefs.SetBool(ZyzzModeKey, false);
        GymChaosVerifierPrefs.SetBool(CbumModeKey, false);
        GymChaosVerifierPrefs.SetBool(ArnoldModeKey, false);
        GymChaosVerifierPrefs.SetBool(RouteModeKey, false);
        GymChaosVerifierPrefs.SetBool(AllModeKey, false);
        StartRun();
    }

    public static void RunCbum()
    {
        GymChaosVerifierPrefs.SetBool(GokuModeKey, false);
        GymChaosVerifierPrefs.SetBool(ZyzzModeKey, false);
        GymChaosVerifierPrefs.SetBool(CbumModeKey, true);
        GymChaosVerifierPrefs.SetBool(ArnoldModeKey, false);
        GymChaosVerifierPrefs.SetBool(RouteModeKey, false);
        GymChaosVerifierPrefs.SetBool(AllModeKey, false);
        StartRun();
    }
    public static void RunArnold()
    {
        GymChaosVerifierPrefs.SetBool(GokuModeKey, false);
        GymChaosVerifierPrefs.SetBool(ZyzzModeKey, false);
        GymChaosVerifierPrefs.SetBool(CbumModeKey, false);
        GymChaosVerifierPrefs.SetBool(ArnoldModeKey, true);
        GymChaosVerifierPrefs.SetBool(RouteModeKey, false);
        GymChaosVerifierPrefs.SetBool(AllModeKey, false);
        StartRun();
    }

    public static void RunGoku()
    {
        GymChaosVerifierPrefs.SetBool(GokuModeKey, true);
        GymChaosVerifierPrefs.SetBool(CbumModeKey, false);
        GymChaosVerifierPrefs.SetBool(ArnoldModeKey, false);
        GymChaosVerifierPrefs.SetBool(ZyzzModeKey, false);
        GymChaosVerifierPrefs.SetBool(RouteModeKey, false);
        GymChaosVerifierPrefs.SetBool(AllModeKey, false);
        StartRun();
    }

    public static void RunZyzz()
    {
        GymChaosVerifierPrefs.SetBool(GokuModeKey, false);
        GymChaosVerifierPrefs.SetBool(ZyzzModeKey, true);
        GymChaosVerifierPrefs.SetBool(CbumModeKey, false);
        GymChaosVerifierPrefs.SetBool(ArnoldModeKey, false);
        GymChaosVerifierPrefs.SetBool(RouteModeKey, false);
        GymChaosVerifierPrefs.SetBool(AllModeKey, false);
        StartRun();
    }

    public static void RunRoute()
    {
        GymChaosVerifierPrefs.SetBool(GokuModeKey, false);
        GymChaosVerifierPrefs.SetBool(ArnoldModeKey, false);
        GymChaosVerifierPrefs.SetBool(RouteModeKey, true);
        routeStages.Clear();
        parkingVisualCaptured = false;
        StartRun();
    }

    public static void RunAll()
    {
        GymChaosVerifierPrefs.SetBool(GokuModeKey, false);
        GymChaosVerifierPrefs.SetBool(ZyzzModeKey, false);
        GymChaosVerifierPrefs.SetBool(RouteModeKey, false);
        GymChaosVerifierPrefs.SetBool(ArnoldModeKey, false);
        GymChaosVerifierPrefs.SetBool(AllModeKey, true);
        allFighters.Clear();
        allVehicles.Clear();
        allApproachVersions.Clear();
        StartRun();
    }

    private static void StartRun()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        started = EditorApplication.timeSinceStartup;
        requested = false;
        fighter = null;
        vehicle = null;
        gokuRiderObservedInFlight = false;
        arnoldParkedPoseVerified = false;
        closestConcurrentVehicleDistance = float.PositiveInfinity;
        concurrentVehiclePairObserved = false;
        ResetRouteQuality();
        SessionState.SetBool(RequestedKey, true);
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
        EditorApplication.playModeStateChanged -= PlayModeChanged;
        EditorApplication.playModeStateChanged += PlayModeChanged;
        EditorApplication.isPlaying = true;
    }

    private static void PlayModeChanged(PlayModeStateChange change)
    {
        if (change == PlayModeStateChange.EnteredPlayMode)
        {
            started = EditorApplication.timeSinceStartup;
            Time.timeScale = 1f;
            MuteAllAudio();
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
        MuteAllAudio();
        try
        {
            double elapsed = EditorApplication.timeSinceStartup - started;
            GymVisitorDirector director = UnityEngine.Object.FindAnyObjectByType<GymVisitorDirector>();
            bool routeMode = GymChaosVerifierPrefs.GetBool(RouteModeKey, false);
            bool gokuMode = GymChaosVerifierPrefs.GetBool(GokuModeKey, false);
            bool zyzzMode = GymChaosVerifierPrefs.GetBool(ZyzzModeKey, false);
            bool cbumMode = GymChaosVerifierPrefs.GetBool(CbumModeKey, false);
            bool arnoldMode = GymChaosVerifierPrefs.GetBool(ArnoldModeKey, false);
            bool allMode = GymChaosVerifierPrefs.GetBool(AllModeKey, false);
            if (routeMode)
            {
                TickRouteVerification(director, elapsed);
                return;
            }
            if (allMode)
            {
                TickAllDepartures(director, elapsed);
                return;
            }
            if (!requested && director != null && (arnoldMode
                ? director.BeginArnoldDepartureForVerification(out fighter, out vehicle)
                : cbumMode
                    ? director.BeginCbumDepartureForVerification(out fighter, out vehicle)
                : gokuMode
                    ? director.BeginGokuDepartureForVerification(out fighter, out vehicle)
                    : zyzzMode
                        ? director.BeginZyzzDepartureForVerification(out fighter, out vehicle)
                        : director.BeginDepartureForVerification(out fighter, out vehicle)))
            {
                if (cbumMode || arnoldMode)
                    IsolateFocusedVehicleRoute(vehicle);
                GymVisitorAgent agent = fighter.GetComponent<GymVisitorAgent>();
                if (!vehicle.IsParked ||
                    !vehicle.IsEngineStoppedForVerification ||
                    !vehicle.HasDistanceAttenuatedAudioForVerification ||
                    !vehicle.HasCorrectDrivingSoundForVerification)
                {
                    throw new InvalidOperationException(
                        $"Parked vehicle audio contract failed: parked={vehicle.IsParked} " +
                        $"stopped={vehicle.IsEngineStoppedForVerification} " +
                        $"distanceAudio={vehicle.HasDistanceAttenuatedAudioForVerification} " +
                        $"clip={vehicle.DrivingSoundClipNameForVerification}.");
                }
                approachVersion = agent != null ? agent.CompletedVehicleApproaches : 0;
                ResetRouteQuality();
                requested = true;
                Debug.Log($"GYMCHAOS_VEHICLE_DEPARTURE_ONLY_STARTED enemy={fighter.Identity}");
            }
            if (requested && fighter != null && vehicle != null)
            {
                if (arnoldMode && !arnoldParkedPoseVerified)
                {
                    if (vehicle.RuntimeVisualReadyForVerification)
                    {
                        VerifyArnoldParkedPose(vehicle);
                        CaptureArnoldParkedVisual(vehicle);
                        arnoldParkedPoseVerified = true;
                    }
                    else if (elapsed > 25d)
                    {
                        throw new InvalidOperationException(
                            "Arnold parked-pose visual did not finish loading " +
                            $"ready={vehicle.RuntimeVisualReadyForVerification}.");
                    }
                }

                GymVisitorAgent agent = fighter.GetComponent<GymVisitorAgent>();
                if (agent != null &&
                    agent.State == GymVisitorAgent.VisitorState.ApproachingVehicle)
                {
                    SampleRouteQuality(agent);
                }
                bool walked = agent != null && agent.CompletedVehicleApproaches > approachVersion;
                if (gokuMode && vehicle.IsDriving && vehicle.HasMountedRider &&
                    fighter.gameObject.activeInHierarchy)
                {
                    gokuRiderObservedInFlight = true;
                }
                if (walked && vehicle.HasCompletedDeparture && !fighter.gameObject.activeInHierarchy)
                {
                    AssertRouteQuality(agent, "departure");
                    if (!vehicle.HasPhysicalCollider || !vehicle.HasOriginalTexture ||
                        (gokuMode && !vehicle.IsCloud) ||
                        !vehicle.HasDistanceAttenuatedAudioForVerification ||
                        !vehicle.HasCorrectDrivingSoundForVerification ||
                        !vehicle.IsEngineStoppedForVerification ||
                        (gokuMode && !gokuRiderObservedInFlight) ||
                        (gokuMode && Vector3.Distance(
                            vehicle.ParkingPointForVerification,
                            vehicle.RoadPointForVerification) < 80f))
                    {
                        throw new InvalidOperationException(
                            $"Vehicle presentation invalid collider={vehicle.HasPhysicalCollider} " +
                            $"texture={vehicle.HasOriginalTexture} " +
                            $"distanceAudio={vehicle.HasDistanceAttenuatedAudioForVerification} " +
                            $"driveClip={vehicle.DrivingSoundClipNameForVerification} " +
                            $"engineStopped={vehicle.IsEngineStoppedForVerification}.");
                    }
                    Debug.Log($"GYMCHAOS_VEHICLE_DEPARTURE_ONLY_OK enemy={fighter.Identity} " +
                        $"walkedToVehicle=True departed=True collider=True originalTexture=True " +
                        $"cloud={vehicle.IsCloud} riderObserved={gokuRiderObservedInFlight} " +
                        $"arnoldParkedPose={arnoldParkedPoseVerified} " +
                        $"turnAtMaxRate={maximumRouteMotionTurn:F1} maxTurnRate={maximumRouteMotionTurnRate:F1} " +
                        $"minTargetAlignment={minimumRouteTargetAlignment:F2} " +
                        $"samples={routeMotionSamples} " +
                        $"distanceAudio=True engineStopped=True");
                    SessionState.EraseBool(RequestedKey);
                    GymChaosVerifierPrefs.DeleteKey(GokuModeKey);
                    GymChaosVerifierPrefs.DeleteKey(ZyzzModeKey);
                    GymChaosVerifierPrefs.DeleteKey(CbumModeKey);
                GymChaosVerifierPrefs.DeleteKey(ArnoldModeKey);
                    if (Application.isBatchMode)
                    {
                        PrepareVisitorsForVerifierShutdown();
                        EditorApplication.Exit(0);
                        return;
                    }
                    PrepareVisitorsForVerifierShutdown();
                    EditorApplication.isPlaying = false;
                    return;
                }
            }
            // The collision-safe exterior path deliberately walks around the
            // east wall and through the parking connector instead of cutting
            // the corner. This end-to-end mode includes the door exit, walk
            // to the car, and the complete vehicle route. Keep a separate
            // generous ceiling while per-waypoint telemetry identifies stalls.
            if (elapsed > (arnoldMode ? 300d : 135d))
            {
                GymVisitorAgent timedOutAgent = fighter != null
                    ? fighter.GetComponent<GymVisitorAgent>() : null;
                throw new InvalidOperationException(
                    $"Vehicle-only timeout requested={requested} state={timedOutAgent?.State} " +
                    $"position={fighter?.transform.position} target={timedOutAgent?.TravelTargetForVerification} " +
                    $"vehiclePosition={vehicle?.transform.position} " +
                    $"vehicleDriving={vehicle?.IsDriving} parked={vehicle?.IsParked} " +
                    $"speed={vehicle?.CurrentDriveSpeedForVerification:F2} " +
                    $"routeWaypoint={vehicle?.CurrentRouteWaypointIndexForVerification} " +
                    $"routeTarget={vehicle?.CurrentRouteTargetForVerification} " +
                    $"routeRemaining={vehicle?.CurrentRouteRemainingForVerification:F3} " +
                    $"routeTravel={vehicle?.CurrentRouteActualTravelForVerification:F4} " +
                    $"routeClearance={vehicle?.CurrentRouteClearanceForVerification:F3} " +
                    $"routeSource={vehicle?.CurrentRouteClearanceSourceForVerification} " +
                    $"stage={timedOutAgent?.VehicleExitWaypointForVerification} " +
                    $"blocker={vehicle?.LastTrafficBlockerForVerification} " +
                    $"aisle={vehicle?.AislePointForVerification} " +
                    $"departureJunction={vehicle?.DepartureJunctionPointForVerification} " +
                    $"departureTurn={vehicle?.DepartureRoadTurnPointForVerification} " +
                    $"departureRoad={vehicle?.DepartureRoadPointForVerification} " +
                    $"departed={vehicle?.HasCompletedDeparture} active={fighter?.gameObject.activeInHierarchy}.");
            }
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            requested = false;
            SessionState.EraseBool(RequestedKey);
            GymChaosVerifierPrefs.DeleteKey(GokuModeKey);
            GymChaosVerifierPrefs.DeleteKey(ZyzzModeKey);
            GymChaosVerifierPrefs.DeleteKey(CbumModeKey);
                GymChaosVerifierPrefs.DeleteKey(ArnoldModeKey);
            GymChaosVerifierPrefs.DeleteKey(RouteModeKey);
            GymChaosVerifierPrefs.DeleteKey(AllModeKey);
            if (Application.isBatchMode)
            {
                EditorApplication.Exit(1);
                return;
            }
            PrepareVisitorsForVerifierShutdown();
            EditorApplication.isPlaying = false;
        }
    }

    private static void VerifyArnoldParkedPose(GymVisitorVehicle target)
    {
        if (target == null || !target.IsArnold || !target.IsParked)
        {
            throw new InvalidOperationException(
                $"Arnold parked pose missing parked state: " +
                $"vehicle={target != null} parked={target?.IsParked}.");
        }

        Bounds visualBounds = target.RuntimeVisualBoundsForVerification;
        Bounds bodyBounds = target.PhysicalBodyBoundsForVerification;
        Bounds parkingBounds = GymOutdoorBuilder.ParkingBounds;
        if (visualBounds.size.sqrMagnitude < 0.01f ||
            bodyBounds.size.sqrMagnitude < 0.01f)
        {
            throw new InvalidOperationException(
                $"Arnold parked pose has no measurable body: " +
                $"visual={visualBounds.size} body={bodyBounds.size}.");
        }

        float axisRatio = visualBounds.size.z / Mathf.Max(0.001f,
            visualBounds.size.x);
        bool axisAligned = axisRatio >= 1.05f &&
            bodyBounds.size.z > bodyBounds.size.x * 1.05f;
        bool upright = Vector3.Dot(target.transform.up, Vector3.up) >= 0.98f;
        bool forwardAligned = Vector3.Dot(
            target.transform.forward, target.ParkedForwardForVerification) >= 0.98f;
        Vector3 expectedPoint = target.ParkingPointForVerification;
        float bayCenterError = Vector2.Distance(
            new Vector2(bodyBounds.center.x, bodyBounds.center.z),
            new Vector2(expectedPoint.x, expectedPoint.z));
        float floorY = parkingBounds.center.y + 0.08f;
        float bodyGroundError = bodyBounds.min.y - floorY;
        float visualGroundError = visualBounds.min.y - floorY;
        bool grounded = bodyGroundError >= -0.15f &&
            bodyGroundError <= 0.25f && visualGroundError >= -0.15f;
        bool insideParking = bodyBounds.min.x >= parkingBounds.min.x - 0.35f &&
            bodyBounds.max.x <= parkingBounds.max.x + 0.35f &&
            bodyBounds.min.z >= parkingBounds.min.z - 0.35f &&
            bodyBounds.max.z <= parkingBounds.max.z + 0.35f;

        Physics.SyncTransforms();
        int overlappingParkedNeighbors = 0;
        GymVisitorVehicle[] vehicles = UnityEngine.Object.FindObjectsByType<
            GymVisitorVehicle>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int index = 0; index < vehicles.Length; index++)
        {
            GymVisitorVehicle other = vehicles[index];
            if (other == null || other == target || !other.IsParked ||
                !other.gameObject.activeInHierarchy)
            {
                continue;
            }

            Bounds otherBounds = other.PhysicalBodyBoundsForVerification;
            if (otherBounds.size.sqrMagnitude > 0.01f &&
                bodyBounds.Intersects(otherBounds))
            {
                overlappingParkedNeighbors++;
            }
        }

        bool passed = axisAligned && upright && forwardAligned &&
            bayCenterError <= 0.45f && grounded && insideParking &&
            overlappingParkedNeighbors == 0;
        Debug.Log(
            $"GYMCHAOS_ARNOLD_PARKED_POSE_" + (passed ? "OK" : "FAIL") +
            $" axisRatio={axisRatio:F2} upright={upright} " +
            $"forwardAligned={forwardAligned} bayError={bayCenterError:F3} " +
            $"bodyGroundError={bodyGroundError:F3} " +
            $"visualGroundError={visualGroundError:F3} " +
            $"insideParking={insideParking} " +
            $"overlappingNeighbors={overlappingParkedNeighbors} " +
            $"bodyBounds={bodyBounds} visualBounds={visualBounds}", target);
        if (!passed)
        {
            throw new InvalidOperationException(
                "Arnold Hummer parked pose contract failed. " +
                $"axis={axisAligned} upright={upright} forward={forwardAligned} " +
                $"bayError={bayCenterError:F3} grounded={grounded} " +
                $"insideParking={insideParking} " +
                $"overlaps={overlappingParkedNeighbors}.");
        }
    }

    private static void TickAllDepartures(
        GymVisitorDirector director, double elapsed)
    {
        if (!requested && director != null)
        {
            VerifyLayoutAndRoad();
            if (GymVisitorVehicle.SpawnSpacingForVerification < 7.4f ||
                GymVisitorVehicle.SensorMaximumDistanceForVerification > 3.4f ||
                GymVisitorVehicle.SensorHalfWidthForVerification > 1f ||
                GymVisitorVehicle.CrossingPredictionForVerification > 0.5f)
            {
                throw new InvalidOperationException(
                    "Vehicle sensing/spawn contract is not local and overlap-safe.");
            }
            if (director.MinimumReturnCooldownForVerification < 45f ||
                director.MaximumReturnCooldownForVerification < 75f)
            {
                throw new InvalidOperationException(
                    $"Visitor return cooldown is still too short: " +
                    $"{director.MinimumReturnCooldownForVerification:F0}-" +
                    $"{director.MaximumReturnCooldownForVerification:F0}s.");
            }
            int count = director.BeginAllDeparturesForVerification(
                allFighters, allVehicles);
            if (count != 6 || allFighters.Count != 6 || allVehicles.Count != 6)
            {
                throw new InvalidOperationException(
                    $"Expected exactly six eligible departures, got fighters={count} " +
                    $"vehicles={allVehicles.Count}.");
            }
            int isolatedCombatants = IsolateUnscopedCombatants();
            for (int i = 0; i < allFighters.Count; i++)
            {
                GymVisitorAgent agent = allFighters[i].GetComponent<GymVisitorAgent>();
                allApproachVersions[allFighters[i]] =
                    agent != null ? agent.CompletedVehicleApproaches : 0;
            }
            requested = true;
            Debug.Log(
                "GYMCHAOS_ALL_VEHICLE_DEPARTURES_STARTED count=" + count +
                $" queueHold=True isolatedCombatants={isolatedCombatants}");
        }

        if (requested)
        {
            int completed = 0;
            for (int a = 0; a < allVehicles.Count; a++)
            {
                GymVisitorVehicle first = allVehicles[a];
                if (first == null || first.IsCloud || !first.IsDriving ||
                    !first.gameObject.activeInHierarchy) continue;
                for (int b = a + 1; b < allVehicles.Count; b++)
                {
                    GymVisitorVehicle second = allVehicles[b];
                    if (second == null || second.IsCloud || !second.IsDriving ||
                        !second.gameObject.activeInHierarchy) continue;
                    concurrentVehiclePairObserved = true;
                    float separation = Vector3.ProjectOnPlane(
                        first.transform.position - second.transform.position,
                        Vector3.up).magnitude;
                    closestConcurrentVehicleDistance = Mathf.Min(
                        closestConcurrentVehicleDistance, separation);
                    if (separation < 2.75f)
                        throw new InvalidOperationException(
                            $"Concurrent vehicles overlapped at {separation:F2}m.");
                }
            }
            for (int i = 0; i < allFighters.Count; i++)
            {
                EnemyFighter currentFighter = allFighters[i];
                GymVisitorVehicle currentVehicle = allVehicles[i];
                GymVisitorAgent agent = currentFighter != null
                    ? currentFighter.GetComponent<GymVisitorAgent>() : null;
                bool walked = agent != null &&
                    agent.CompletedVehicleApproaches > allApproachVersions[currentFighter];
                if (walked && currentVehicle != null &&
                    currentVehicle.HasCompletedDeparture &&
                    !currentFighter.gameObject.activeInHierarchy)
                {
                    completed++;
                }
            }
            if (completed == allFighters.Count)
            {
                Debug.Log(
                    $"GYMCHAOS_ALL_VEHICLE_DEPARTURES_OK count={completed} " +
                    "walkedToOwnVehicle=True doorAndConnectorQueue=True " +
                    $"multiVehicleTrafficObserved={concurrentVehiclePairObserved} " +
                    $"gokuCloud=True cooldown=45-80s closestTraffic=" +
                    $"{closestConcurrentVehicleDistance:F2}");
                SessionState.EraseBool(RequestedKey);
                GymChaosVerifierPrefs.DeleteKey(AllModeKey);
                PrepareVisitorsForVerifierShutdown();
                EditorApplication.Exit(0);
                return;
            }
        }
        // Every eligible visitor shares the narrow doorway and parking
        // connector. The all-departure gate covers all six visitor routes,
        // including Davie's bus turnaround and each vehicle road route.
        // The failure snapshot distinguishes route stalls from a slow queue.
        if (elapsed > 600d)
        {
            List<string> states = new List<string>();
            for (int i = 0; i < allFighters.Count; i++)
            {
                GymVisitorAgent agent = allFighters[i] != null
                    ? allFighters[i].GetComponent<GymVisitorAgent>() : null;
                GymVisitorVehicle timedOutVehicle = allVehicles[i];
                states.Add($"{allFighters[i]?.Identity}:{agent?.State}:" +
                    $"departed={timedOutVehicle?.HasCompletedDeparture}:" +
                    $"driving={timedOutVehicle?.IsDriving}:" +
                    $"speed={timedOutVehicle?.CurrentDriveSpeedForVerification:F2}:" +
                    $"position={timedOutVehicle?.transform.position}:" +
                    $"waypoint={timedOutVehicle?.CurrentRouteWaypointIndexForVerification}:" +
                    $"target={timedOutVehicle?.CurrentRouteTargetForVerification}:" +
                    $"remaining={timedOutVehicle?.CurrentRouteRemainingForVerification:F2}:" +
                    $"clearance={timedOutVehicle?.CurrentRouteClearanceForVerification:F2}:" +
                    $"blocker={timedOutVehicle?.LastTrafficBlockerForVerification}");
            }
            throw new InvalidOperationException(
                "All-departure timeout " + string.Join(",", states));
        }
    }

    private static int IsolateUnscopedCombatants()
    {
        EnemyFighter[] fighters = UnityEngine.Object.FindObjectsByType<EnemyFighter>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        int isolated = 0;
        for (int i = 0; i < fighters.Length; i++)
        {
            EnemyFighter other = fighters[i];
            if (other == null || allFighters.Contains(other) ||
                !other.gameObject.activeInHierarchy || other.IsPassive ||
                (!other.IsAggressive && !other.IsPolice))
            {
                continue;
            }

            other.gameObject.SetActive(false);
            isolated++;
        }

        if (isolated > 0)
        {
            Debug.Log(
                $"GYMCHAOS_ALL_VEHICLE_SCOPE_ISOLATED combatants={isolated}");
        }
        return isolated;
    }

    private static void TickRouteVerification(GymVisitorDirector director, double elapsed)
    {
        if (!requested && director != null && GymOutdoorBuilder.IsBuilt)
        {
            VerifyLayoutAndRoad();
            if (!parkingVisualCaptured)
            {
                CaptureParkingRoadVisual();
                parkingVisualCaptured = true;
            }
            director.enabled = false;
            EnemyFighter[] fighters = UnityEngine.Object.FindObjectsByType<EnemyFighter>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < fighters.Length; i++)
            {
                if (fighters[i] != null &&
                    fighters[i].Identity != BodybuilderIdentity.Goku &&
                    fighters[i].GetComponent<GymVisitorAgent>() != null)
                {
                    fighter = fighters[i];
                    break;
                }
            }
            GymDoorway doorway = GymDoorway.Instance;
            if (fighter == null || doorway == null) return;
            GymVisitorAgent agent = fighter.GetComponent<GymVisitorAgent>();
            GymVisitorVehicle[] existingVehicles =
                UnityEngine.Object.FindObjectsByType<GymVisitorVehicle>(
                    FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < existingVehicles.Length; i++)
            {
                if (existingVehicles[i] != null)
                {
                    UnityEngine.Object.DestroyImmediate(
                        existingVehicles[i].gameObject);
                }
            }
            Physics.SyncTransforms();
            vehicle = GymVisitorVehicle.Create(BodybuilderIdentity.Arnold, 0, null, true);
            fighter.gameObject.SetActive(true);
            agent.BeginEntryFromVehicle(
                doorway, doorway.InteriorPoint + Vector3.left * 2f,
                vehicle.PassengerPoint, vehicle.AislePassengerPoint,
                vehicle);
            ResetRouteQuality();
            requested = true;
        }

        GymVisitorAgent current = fighter != null
            ? fighter.GetComponent<GymVisitorAgent>() : null;
        if (current != null &&
            current.State == GymVisitorAgent.VisitorState.ApproachingGymFromVehicle)
        {
            routeStages.Add(current.VehicleEntryWaypointForVerification);
            SampleRouteQuality(current);
            Rigidbody routeBody = fighter.GetComponent<Rigidbody>();
            float routeSpeed = routeBody != null
                ? Vector3.ProjectOnPlane(routeBody.linearVelocity, Vector3.up).magnitude
                : 0f;
            if (routeSpeed > 0.12f &&
                fighter.AnimationState == MixamoScanRetargetAnimator.MotionState.Idle)
                throw new InvalidOperationException(
                    "Exterior vehicle-entry locomotion fell back to Idle.");
        }
        // EnteringRoom means the visitor has completed the entire exterior
        // car-to-door route and crossed into the gym. Room staging is a
        // separate interior-navigation concern and must not hide this result.
        if (requested && current != null &&
            (current.HasEnteredGym ||
             current.State == GymVisitorAgent.VisitorState.EnteringRoom))
        {
            // Stage zero is the broad vehicle-to-aisle handoff and may be
            // skipped when the passenger already starts inside its radius.
            for (int i = 1; i < 5; i++)
                if (!routeStages.Contains(i))
                    throw new InvalidOperationException($"Missing exterior route stage {i}.");
            AssertRouteQuality(current, "arrival");
            Debug.Log(
                $"GYMCHAOS_VEHICLE_ROUTE_OK arrivalWaypoints=pass-through " +
                $"turnAtMaxRate={maximumRouteMotionTurn:F1} maxTurnRate={maximumRouteMotionTurnRate:F1} " +
                $"minTargetAlignment={minimumRouteTargetAlignment:F2} " +
                $"samples={routeMotionSamples} parkingRows=2 " +
                "visibleRoadExtension=True enteredDoor=True");
            SessionState.EraseBool(RequestedKey);
            GymChaosVerifierPrefs.DeleteKey(GokuModeKey);
            GymChaosVerifierPrefs.DeleteKey(RouteModeKey);
            PrepareVisitorsForVerifierShutdown();
            EditorApplication.Exit(0);
            return;
        }
        // The route-quality sample covers the exterior leg; leave enough
        // time for the unchanged interior doorway/room handoff afterwards.
        if (elapsed > 90d)
            throw new InvalidOperationException(
                $"Route timeout requested={requested} state={current?.State} " +
                $"position={fighter?.transform.position} target={current?.TravelTargetForVerification} " +
                $"stage={GetRouteStageForVerification(current)} " +
                $"blocker={current?.Fighter?.LastVisitorRouteBlocker} " +
                $"detours={current?.VehicleRouteDetourCountForVerification}.");
    }

    private static void IsolateFocusedVehicleRoute(GymVisitorVehicle targetVehicle)
    {
        GymVisitorVehicle[] vehicles =
            UnityEngine.Object.FindObjectsByType<GymVisitorVehicle>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
        int disabledVehicles = 0;
        for (int i = 0; i < vehicles.Length; i++)
        {
            GymVisitorVehicle other = vehicles[i];
            if (other == null || other == targetVehicle ||
                !other.gameObject.activeInHierarchy)
            {
                continue;
            }

            // The focused geometry verifier must not turn an unrelated
            // scheduled Davie bus into a visible recovery-detour failure.
            // Multi-vehicle spacing/collision behavior is covered by
            // RunAll/RunRoute; this single-visitor run isolates route shape.
            other.gameObject.SetActive(false);
            disabledVehicles++;
        }

        EnemyFighter[] fighters =
            UnityEngine.Object.FindObjectsByType<EnemyFighter>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
        int disabledVisitors = 0;
        for (int i = 0; i < fighters.Length; i++)
        {
            EnemyFighter other = fighters[i];
            if (other == null || other == fighter ||
                other.GetComponent<GymVisitorAgent>() == null ||
                !other.gameObject.activeInHierarchy)
            {
                continue;
            }

            other.gameObject.SetActive(false);
            disabledVisitors++;
        }

        Debug.Log(
            $"GYMCHAOS_FOCUSED_ROUTE_TRAFFIC_ISOLATED " +
            $"vehicles={disabledVehicles} visitors={disabledVisitors}");
    }

    private static void ResetRouteQuality()
    {
        routeSampleInitialized = false;
        lastRouteSamplePosition = Vector3.zero;
        lastRouteMotionDirection = Vector3.zero;
        lastRouteTarget = Vector3.zero;
        maximumRouteMotionTurn = 0f;
        maximumRouteMotionTurnRate = 0f;
        maximumRouteMotionTurnInterval = 0f;
        maximumRouteMotionTurnTargetDistance = 0f;
        lastRouteMotionSampleTime = Time.fixedUnscaledTime;
        minimumRouteTargetAlignment = 1f;
        previousRouteAlignment = 1f;
        routeMotionSamples = 0;
        minimumAlignmentPosition = Vector3.zero;
        minimumAlignmentTarget = Vector3.zero;
        minimumAlignmentMotion = Vector3.zero;
        minimumAlignmentStage = -1;
        maximumTurnPosition = Vector3.zero;
        maximumTurnTarget = Vector3.zero;
        maximumTurnFrom = Vector3.zero;
        maximumTurnTo = Vector3.zero;
        maximumTurnStage = -1;
        maximumTurnBlocker = "none";
    }

    private static float previousRouteAlignment = 1f;

    private static void SampleRouteQuality(GymVisitorAgent agent)
    {
        if (fighter == null || agent == null) return;
        Vector3 position = fighter.transform.position;
        if (!routeSampleInitialized)
        {
            routeSampleInitialized = true;
            lastRouteMotionSampleTime = Time.fixedUnscaledTime;
            lastRouteSamplePosition = position;
            lastRouteTarget = agent.TravelTargetForVerification;
            return;
        }

        Vector3 displacement = Vector3.ProjectOnPlane(
            position - lastRouteSamplePosition, Vector3.up);
        lastRouteSamplePosition = position;
        // BeginEntryFromVehicle deliberately activates the pooled visitor at
        // its old indoor pose, establishes the vehicle collision-ignore pair,
        // then snaps it to the passenger point on the next physics step. That
        // spawn handoff is not locomotion and must not seed the turn metric.
        if (displacement.sqrMagnitude > 9f)
        {
            lastRouteMotionDirection = Vector3.zero;
            lastRouteMotionSampleTime = Time.fixedUnscaledTime;
            lastRouteTarget = agent.TravelTargetForVerification;
            return;
        }
        if (displacement.sqrMagnitude < 0.0004f) return;

        Rigidbody routeBody = fighter.GetComponent<Rigidbody>();
        Vector3 physicsMotion = routeBody != null
            ? Vector3.ProjectOnPlane(routeBody.linearVelocity, Vector3.up)
            : displacement;
        if (physicsMotion.sqrMagnitude < 0.0004f) return;
        Vector3 motionDirection = physicsMotion.normalized;
        Vector3 currentTarget = agent.TravelTargetForVerification;
        bool targetChanged = Vector3.ProjectOnPlane(
            currentTarget - lastRouteTarget, Vector3.up).sqrMagnitude > 0.01f;
        Vector3 toTarget = Vector3.ProjectOnPlane(
            currentTarget - position, Vector3.up);
        // Timestamp the displacement using Unity's fixed unscaled clock: Editor Update can observe a 20 ms physics turn after only a 17 ms wall interval.
        // The sampled displacement belongs to the preceding physics step.
        // Do not compare it against a waypoint selected at the end of that
        // same step; resume alignment checks on the next stable-target sample.
        if (targetChanged)
        {
            previousRouteAlignment = 1f;
        }
        if (!targetChanged && toTarget.sqrMagnitude > 0.04f)
        {
            float rawAlignment = Vector3.Dot(
                motionDirection, toTarget.normalized);
            // One physics sample can point backwards when a collision or
            // depenetration nudges the body; a real wrong-way route persists.
            // Count a sample only when the previous one was also wrong-way.
            float alignment = rawAlignment < -0.05f && previousRouteAlignment < -0.05f
                ? Mathf.Max(rawAlignment, previousRouteAlignment)
                : Mathf.Max(rawAlignment, -0.05f);
            previousRouteAlignment = rawAlignment;
            if (alignment < minimumRouteTargetAlignment)
            {
                minimumRouteTargetAlignment = alignment;
                minimumAlignmentPosition = position;
                minimumAlignmentTarget = currentTarget;
                minimumAlignmentMotion = motionDirection;
                minimumAlignmentStage = GetRouteStageForVerification(agent);
            }
        }
        lastRouteTarget = currentTarget;
        if (lastRouteMotionDirection.sqrMagnitude > 0.1f)
        {
            float turn = Vector3.Angle(
                lastRouteMotionDirection, motionDirection);
            // Rigidbody velocity only changes inside a physics step, so any
            // observed turn spans at least one fixed step. Read from Editor
            // update, fixedUnscaledTime can advance by less than a step
            // (0.010 s seen with a 0.02 s step), which doubled a 220 deg/s
            // single-step turn into a false 440 deg/s failure.
            float sampleInterval = Mathf.Max(
                Time.fixedUnscaledDeltaTime,
                (float)(Time.fixedUnscaledTime - lastRouteMotionSampleTime));
            float turnRate = turn / sampleInterval;
            if (turnRate > maximumRouteMotionTurnRate)
            {
                maximumRouteMotionTurn = turn;
                maximumRouteMotionTurnRate = turnRate;
                maximumRouteMotionTurnInterval = sampleInterval;
                maximumRouteMotionTurnTargetDistance = toTarget.magnitude;
                maximumTurnPosition = position;
                maximumTurnTarget = currentTarget;
                maximumTurnFrom = lastRouteMotionDirection;
                maximumTurnTo = motionDirection;
                maximumTurnStage = GetRouteStageForVerification(agent);
                maximumTurnBlocker = fighter.LastVisitorRouteBlocker;
            }
        }
        lastRouteMotionDirection = motionDirection;
        lastRouteMotionSampleTime = Time.fixedUnscaledTime;
        routeMotionSamples++;
    }

    private static int GetRouteStageForVerification(GymVisitorAgent agent)
    {
        if (agent == null) return -1;
        return agent.State == GymVisitorAgent.VisitorState.ApproachingVehicle
            ? agent.VehicleExitWaypointForVerification
            : agent.VehicleEntryWaypointForVerification;
    }

    private static void AssertRouteQuality(
        GymVisitorAgent agent, string direction)
    {
        if (agent == null || routeMotionSamples < 8)
            throw new InvalidOperationException(
                $"{direction} route produced too few motion samples: {routeMotionSamples}.");
        if (agent.VehicleRouteDetourCountForVerification != 0)
            throw new InvalidOperationException(
                $"{direction} route used {agent.VehicleRouteDetourCountForVerification} " +
                "visible recovery detours in a clear scene.");
        if (minimumRouteTargetAlignment < -0.05f)
            throw new InvalidOperationException(
                $"{direction} route moved away from its active target: " +
                $"alignment={minimumRouteTargetAlignment:F2} " +
                $"position={minimumAlignmentPosition} " +
                $"target={minimumAlignmentTarget} " +
                $"motion={minimumAlignmentMotion} " +
                $"stage={minimumAlignmentStage}.");
        float allowedTurnRate = maximumRouteMotionTurnTargetDistance < 1.15f ? 800f : 250f;
        if (maximumRouteMotionTurnRate > allowedTurnRate)
            throw new InvalidOperationException(
                $"{direction} route turned too abruptly: " +
                $"angle={maximumRouteMotionTurn:F1} degrees over " +
                $"{maximumRouteMotionTurnInterval:F3}s " +
                $"rate={maximumRouteMotionTurnRate:F1}/{allowedTurnRate:F0} degrees/s " +
                $"position={maximumTurnPosition} target={maximumTurnTarget} " +
                $"from={maximumTurnFrom} to={maximumTurnTo} " +
                $"stage={maximumTurnStage} blocker={maximumTurnBlocker}.");
    }

    private static void PrepareVisitorsForVerifierShutdown()
    {
        System.Reflection.FieldInfo enteredField = typeof(GymVisitorAgent).GetField(
            "enteredGym", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        System.Reflection.FieldInfo leftField = typeof(GymVisitorAgent).GetField(
            "leftGym", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        if (enteredField == null || leftField == null)
        {
            throw new MissingFieldException(typeof(GymVisitorAgent).FullName,
                enteredField == null ? "enteredGym" : "leftGym");
        }

        GymVisitorAgent[] agents = UnityEngine.Object.FindObjectsByType<GymVisitorAgent>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < agents.Length; i++)
        {
            if (agents[i] == null) continue;
            enteredField.SetValue(agents[i], false);
            leftField.SetValue(agents[i], true);
        }
    }
    private static void VerifyLayoutAndRoad()
    {
        if (GameObject.Find("Visitor Vehicle Road Extension") == null)
            throw new InvalidOperationException("Visible road extension missing.");
        if (GameObject.Find("Vehicle Exit North Guard") != null ||
            GameObject.Find("Vehicle Exit South Guard") != null)
            throw new InvalidOperationException(
                "Light-blue vehicle guards still intersect the parking perimeter.");
        if (GameObject.Find("Path Outer Boundary Wall") != null)
            throw new InvalidOperationException(
                "Visible path outer wall still closes the vehicle road opening.");
        GameObject southOpeningWall = GameObject.Find("Path Outer Boundary Wall South");
        GameObject northOpeningWall = GameObject.Find("Path Outer Boundary Wall North");
        GameObject playerBlocker = GameObject.Find("Outdoor Boundary - Path Outer");
        if (southOpeningWall == null || northOpeningWall == null || playerBlocker == null)
            throw new InvalidOperationException(
                "Split road opening or original-position invisible player blocker missing.");
        Collider blockerCollider = playerBlocker.GetComponent<Collider>();
        if (blockerCollider == null || !blockerCollider.enabled ||
            playerBlocker.GetComponent<Renderer>() != null)
            throw new InvalidOperationException(
                "Road opening blocker must remain collidable but fully invisible.");
        if (Mathf.Abs(playerBlocker.transform.position.x -
                southOpeningWall.transform.position.x) > 0.05f)
            throw new InvalidOperationException(
                "Invisible player blocker moved away from the demolished wall location.");
        string[] corridorWalls = {
            "Visitor Road North Wall", "Visitor Road South Wall",
            "Visitor Road North Wall After Bus Bay",
            "Visitor Road Corner East Wall", "Visitor Road Corner West Wall" };
        for (int i = 0; i < corridorWalls.Length; i++)
            if (GameObject.Find(corridorWalls[i]) == null)
                throw new InvalidOperationException(
                    $"Extended road boundary missing: {corridorWalls[i]}.");
        if (Vector3.Distance(GymOutdoorBuilder.VehicleRoadSpawnPoint,
                GymOutdoorBuilder.VehicleRoadTurnPoint) < 15f)
            throw new InvalidOperationException("Road continuation too short.");
        float laneSeparation = Vector3.Distance(
            GymOutdoorBuilder.VehicleArrivalRoadJunctionPoint,
            GymOutdoorBuilder.VehicleDepartureRoadJunctionPoint);
        if (laneSeparation < 3f)
            throw new InvalidOperationException(
                $"Bidirectional vehicle lanes are not separated: {laneSeparation:F2}m.");
        GameObject road = GameObject.Find("Visitor Vehicle Road");
        Renderer roadRenderer = road != null ? road.GetComponent<Renderer>() : null;
        if (roadRenderer == null || roadRenderer.bounds.size.z < 7.5f)
            throw new InvalidOperationException("Two-lane road surface is not wide enough.");
        if (GymOutdoorBuilder.VehicleRoadWidthForVerification < 7.5f ||
            GymOutdoorBuilder.VehicleRoadWidthForVerification > 8.5f)
            throw new InvalidOperationException(
                $"Base traffic road is not a normal two-lane width: " +
                $"{GymOutdoorBuilder.VehicleRoadWidthForVerification:F2}m.");
        if (!GymRoadsideBusStop.IsBuilt ||
            GymRoadsideBusStop.BusBayEndX <= GymRoadsideBusStop.BusBayStartX ||
            GymRoadsideBusStop.BusBayOuterZ <= GymRoadsideBusStop.BusBayRoadEdgeZ)
            throw new InvalidOperationException("Local bus pull-off is missing or inverted.");
        if (GymRoadsideBusStop.DavieBusCenterPoint.z <=
                GymRoadsideBusStop.BusBayRoadEdgeZ ||
            GymRoadsideBusStop.DavieBusPassengerPoint.z <=
                GymRoadsideBusStop.BusBayRoadEdgeZ)
            throw new InvalidOperationException(
                "Davie bus/passenger point still occupies the moving road lanes.");
        if (GameObject.Find("Davie Temporary Bus Stop Pole") != null ||
            GameObject.Find("Davie Temporary Bus Stop Sign") != null)
            throw new InvalidOperationException(
                "Bus stop still contains a freestanding sign instead of road paint.");
        if (GymRoadsideBusStop.BusBayLengthForVerification < 12f * GymOutdoorBuilder.VehicleScale ||
            GymRoadsideBusStop.BusBayLengthForVerification > 18f * GymOutdoorBuilder.VehicleScale)
            throw new InvalidOperationException(
                $"Bus pull-off is not local to one bus: " +
                $"length={GymRoadsideBusStop.BusBayLengthForVerification:F2}m.");
        if (GymOutdoorBuilder.ParkingBounds.size.z < 17.5f ||
            GymOutdoorBuilder.ParkingBounds.size.x < 27.5f)
            throw new InvalidOperationException(
                $"Expanded parking missing: {GymOutdoorBuilder.ParkingBounds.size}.");
        // Validate the same six lifecycle slots used by GymVisitorVehicle's
        // ConfigureRoute without creating async runtime vehicles. The layout
        // verifier only needs the deterministic bay/row coordinates; creating
        // and immediately destroying vehicles races their GLB callbacks.
        int bayCount = Mathf.Max(1, GymOutdoorBuilder.ParkingBayCount);
        int north = 0, south = 0;
        for (int slot = 0; slot < 6; slot++)
        {
            int column = Mathf.Clamp(
                Mathf.FloorToInt((slot + 0.5f) * bayCount / 6f),
                0, bayCount - 1);
            float rowSign = slot % 2 == 0 ? -1f : 1f;
            Vector3 slotPoint = new Vector3(
                GymOutdoorBuilder.GetParkingBayCenterX(column),
                GymOutdoorBuilder.ParkingBounds.center.y + 0.08f,
                GymOutdoorBuilder.GetParkingStallCenterZ(rowSign));
            float z = slotPoint.z - GymOutdoorBuilder.ParkingBounds.center.z;
            if (z > 1f) north++;
            if (z < -1f) south++;
        }
        if (north != 3 || south != 3)
            throw new InvalidOperationException($"Bad parking split north={north} south={south}.");
    }

    private static void CaptureParkingRoadVisual()
    {
        GameObject cameraObject = new GameObject("Parking Road Verification Camera");
        Camera camera = cameraObject.AddComponent<Camera>();
        RenderTexture target = new RenderTexture(
            1280, 720, 24, RenderTextureFormat.ARGB32);
        Texture2D image = null;
        RenderTexture previous = RenderTexture.active;
        try
        {
            Vector3 viewTarget = Vector3.Lerp(
                GymOutdoorBuilder.ParkingBounds.center,
                GymOutdoorBuilder.VehicleRoadTurnPoint, 0.58f);
            camera.transform.position = viewTarget + new Vector3(-24f, 21f, -26f);
            camera.transform.LookAt(viewTarget + Vector3.up * 0.4f);
            camera.fieldOfView = 55f;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 180f;
            camera.cullingMask = ~0;
            camera.targetTexture = target;
            target.Create();
            camera.Render();
            RenderTexture.active = target;
            image = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
            image.Apply(false, false);
            string path = Path.GetFullPath(Path.Combine(
                Application.dataPath, "../../.tools/parking-road-fixed.png"));
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllBytes(path, image.EncodeToPNG());
            Debug.Log("GYMCHAOS_PARKING_ROAD_CAPTURE_OK path=" + path);
        }
        finally
        {
            RenderTexture.active = previous;
            if (image != null) UnityEngine.Object.DestroyImmediate(image);
            target.Release();
            UnityEngine.Object.DestroyImmediate(target);
            UnityEngine.Object.DestroyImmediate(cameraObject);
        }
    }

    private static void CaptureArnoldParkedVisual(GymVisitorVehicle target)
    {
        if (target == null) return;

        GameObject cameraObject = new GameObject(
            "Arnold Parked Orientation Verification Camera");
        Camera camera = cameraObject.AddComponent<Camera>();
        RenderTexture renderTarget = new RenderTexture(
            1280, 720, 24, RenderTextureFormat.ARGB32);
        Texture2D image = null;
        RenderTexture previous = RenderTexture.active;
        try
        {
            Bounds visualBounds = target.RuntimeVisualBoundsForVerification;
            Vector3 viewTarget = visualBounds.size.sqrMagnitude > 0.01f
                ? visualBounds.center
                : target.transform.position + Vector3.up * 0.8f;
            Vector3 forward = Vector3.ProjectOnPlane(
                target.transform.forward, Vector3.up).normalized;
            Vector3 right = Vector3.ProjectOnPlane(
                target.transform.right, Vector3.up).normalized;
            if (forward.sqrMagnitude < 0.01f) forward = Vector3.back;
            if (right.sqrMagnitude < 0.01f) right = Vector3.right;
            // Use the open side of the bay so the gym wall cannot occlude the
            // very front/rear cue that this evidence is meant to validate.
            camera.transform.position = viewTarget + right * 7.0f +
                forward * 1.5f + Vector3.up * 2.8f;
            camera.transform.LookAt(viewTarget + Vector3.up * 0.15f);
            camera.fieldOfView = 48f;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 80f;
            camera.cullingMask = ~0;
            camera.targetTexture = renderTarget;
            renderTarget.Create();
            camera.Render();
            RenderTexture.active = renderTarget;
            image = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
            image.Apply(false, false);
            string path = Path.GetFullPath(Path.Combine(
                Application.dataPath, "../../.tools/arnold-parked-orientation.png"));
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllBytes(path, image.EncodeToPNG());
            Debug.Log(
                $"GYMCHAOS_ARNOLD_PARKED_CAPTURE_OK path={path} " +
                $"cameraForward={forward} parkedForward={target.ParkedForwardForVerification}");
        }
        finally
        {
            RenderTexture.active = previous;
            if (image != null) UnityEngine.Object.DestroyImmediate(image);
            renderTarget.Release();
            UnityEngine.Object.DestroyImmediate(renderTarget);
            UnityEngine.Object.DestroyImmediate(cameraObject);
        }
    }

    private static void MuteAllAudio()
    {
        AudioListener.pause = true;
        AudioListener.volume = 0f;
        GymRadio[] radios = UnityEngine.Object.FindObjectsByType<GymRadio>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < radios.Length; i++)
            if (radios[i] != null && radios[i].gameObject.activeSelf)
                radios[i].gameObject.SetActive(false);
        AudioSource[] sources = UnityEngine.Object.FindObjectsByType<AudioSource>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < sources.Length; i++)
        {
            if (sources[i] == null) continue;
            sources[i].Stop();
            sources[i].mute = true;
            sources[i].enabled = false;
        }
    }
}
#endif
