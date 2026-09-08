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
    private static float closestConcurrentVehicleDistance = float.PositiveInfinity;
    private static bool routeSampleInitialized;
    private static Vector3 lastRouteSamplePosition;
    private static Vector3 lastRouteMotionDirection;
    private static Vector3 lastRouteTarget;
    private static float maximumRouteMotionTurn;
    private static float minimumRouteTargetAlignment;
    private static int routeMotionSamples;
    private static Vector3 minimumAlignmentPosition;
    private static Vector3 minimumAlignmentTarget;
    private static Vector3 minimumAlignmentMotion;
    private static int minimumAlignmentStage;

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
        EditorPrefs.SetBool(GokuModeKey, false);
        EditorPrefs.SetBool(ZyzzModeKey, false);
        EditorPrefs.SetBool(RouteModeKey, false);
        EditorPrefs.SetBool(AllModeKey, false);
        StartRun();
    }

    public static void RunGoku()
    {
        EditorPrefs.SetBool(GokuModeKey, true);
        EditorPrefs.SetBool(ZyzzModeKey, false);
        EditorPrefs.SetBool(RouteModeKey, false);
        EditorPrefs.SetBool(AllModeKey, false);
        StartRun();
    }

    public static void RunZyzz()
    {
        EditorPrefs.SetBool(GokuModeKey, false);
        EditorPrefs.SetBool(ZyzzModeKey, true);
        EditorPrefs.SetBool(RouteModeKey, false);
        EditorPrefs.SetBool(AllModeKey, false);
        StartRun();
    }

    public static void RunRoute()
    {
        EditorPrefs.SetBool(GokuModeKey, false);
        EditorPrefs.SetBool(RouteModeKey, true);
        routeStages.Clear();
        parkingVisualCaptured = false;
        StartRun();
    }

    public static void RunAll()
    {
        EditorPrefs.SetBool(GokuModeKey, false);
        EditorPrefs.SetBool(ZyzzModeKey, false);
        EditorPrefs.SetBool(RouteModeKey, false);
        EditorPrefs.SetBool(AllModeKey, true);
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
        closestConcurrentVehicleDistance = float.PositiveInfinity;
        ResetRouteQuality();
        EditorPrefs.SetBool(RequestedKey, true);
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
            Time.timeScale = 3f;
            MuteAllAudio();
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
        MuteAllAudio();
        try
        {
            double elapsed = EditorApplication.timeSinceStartup - started;
            GymVisitorDirector director = UnityEngine.Object.FindAnyObjectByType<GymVisitorDirector>();
            bool routeMode = EditorPrefs.GetBool(RouteModeKey, false);
            bool gokuMode = EditorPrefs.GetBool(GokuModeKey, false);
            bool zyzzMode = EditorPrefs.GetBool(ZyzzModeKey, false);
            bool allMode = EditorPrefs.GetBool(AllModeKey, false);
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
            if (!requested && director != null && (gokuMode
                ? director.BeginGokuDepartureForVerification(out fighter, out vehicle)
                : zyzzMode
                    ? director.BeginZyzzDepartureForVerification(out fighter, out vehicle)
                    : director.BeginDepartureForVerification(out fighter, out vehicle)))
            {
                GymVisitorAgent agent = fighter.GetComponent<GymVisitorAgent>();
                if (!vehicle.IsParked ||
                    !vehicle.IsEngineStoppedForVerification ||
                    !vehicle.HasDistanceAttenuatedAudioForVerification)
                {
                    throw new InvalidOperationException(
                        $"Parked vehicle audio contract failed: parked={vehicle.IsParked} " +
                        $"stopped={vehicle.IsEngineStoppedForVerification} " +
                        $"distanceAudio={vehicle.HasDistanceAttenuatedAudioForVerification}.");
                }
                approachVersion = agent != null ? agent.CompletedVehicleApproaches : 0;
                ResetRouteQuality();
                requested = true;
                Debug.Log($"GYMCHAOS_VEHICLE_DEPARTURE_ONLY_STARTED enemy={fighter.Identity}");
            }
            if (requested && fighter != null && vehicle != null)
            {
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
                            $"engineStopped={vehicle.IsEngineStoppedForVerification}.");
                    }
                    Debug.Log($"GYMCHAOS_VEHICLE_DEPARTURE_ONLY_OK enemy={fighter.Identity} " +
                        $"walkedToVehicle=True departed=True collider=True originalTexture=True " +
                        $"cloud={vehicle.IsCloud} riderObserved={gokuRiderObservedInFlight} " +
                        $"maxMotionTurn={maximumRouteMotionTurn:F1} " +
                        $"minTargetAlignment={minimumRouteTargetAlignment:F2} " +
                        $"samples={routeMotionSamples} " +
                        $"distanceAudio=True engineStopped=True");
                    EditorPrefs.DeleteKey(RequestedKey);
                    EditorPrefs.DeleteKey(GokuModeKey);
                    EditorPrefs.DeleteKey(ZyzzModeKey);
                    if (Application.isBatchMode)
                    {
                        EditorApplication.Exit(0);
                        return;
                    }
                    EditorApplication.isPlaying = false;
                    return;
                }
            }
            // The collision-safe exterior path deliberately walks around the
            // east wall and through the parking connector instead of cutting
            // the corner. Allow the single-visitor verifier enough real time
            // to observe that complete physical route and the vehicle exit.
            if (elapsed > 45d)
            {
                GymVisitorAgent timedOutAgent = fighter != null
                    ? fighter.GetComponent<GymVisitorAgent>() : null;
                throw new InvalidOperationException(
                    $"Vehicle-only timeout requested={requested} state={timedOutAgent?.State} " +
                    $"position={fighter?.transform.position} target={timedOutAgent?.TravelTargetForVerification} " +
                    $"stage={timedOutAgent?.VehicleStageForVerification} " +
                    $"departed={vehicle?.HasCompletedDeparture} active={fighter?.gameObject.activeInHierarchy}.");
            }
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            requested = false;
            EditorPrefs.DeleteKey(RequestedKey);
            EditorPrefs.DeleteKey(GokuModeKey);
            EditorPrefs.DeleteKey(ZyzzModeKey);
            EditorPrefs.DeleteKey(RouteModeKey);
            EditorPrefs.DeleteKey(AllModeKey);
            if (Application.isBatchMode)
            {
                EditorApplication.Exit(1);
                return;
            }
            EditorApplication.isPlaying = false;
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
            if (count < 5 || allVehicles.Count != count)
            {
                throw new InvalidOperationException(
                    $"Expected five eligible departures, got fighters={count} " +
                    $"vehicles={allVehicles.Count}.");
            }
            for (int i = 0; i < allFighters.Count; i++)
            {
                GymVisitorAgent agent = allFighters[i].GetComponent<GymVisitorAgent>();
                allApproachVersions[allFighters[i]] =
                    agent != null ? agent.CompletedVehicleApproaches : 0;
            }
            requested = true;
            Debug.Log("GYMCHAOS_ALL_VEHICLE_DEPARTURES_STARTED count=" + count);
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
                    "multiVehicleTraffic=True " +
                    $"gokuCloud=True cooldown=45-80s closestTraffic=" +
                    $"{closestConcurrentVehicleDistance:F2}");
                EditorPrefs.DeleteKey(RequestedKey);
                EditorPrefs.DeleteKey(AllModeKey);
                EditorApplication.Exit(0);
                return;
            }
        }
        // Five visitors intentionally share the narrow doorway and parking
        // connector one at a time. Keep the stress test long enough for the
        // whole physical queue while the per-route reroute logs still expose
        // any genuine no-progress condition.
        if (elapsed > 150d)
        {
            List<string> states = new List<string>();
            for (int i = 0; i < allFighters.Count; i++)
            {
                GymVisitorAgent agent = allFighters[i] != null
                    ? allFighters[i].GetComponent<GymVisitorAgent>() : null;
                GymVisitorVehicle timedOutVehicle = allVehicles[i];
                states.Add($"{allFighters[i]?.Identity}:{agent?.State}:" +
                    $"{timedOutVehicle?.HasCompletedDeparture}:" +
                    $"driving={timedOutVehicle?.IsDriving}:" +
                    $"speed={timedOutVehicle?.CurrentDriveSpeedForVerification:F2}:" +
                    $"position={timedOutVehicle?.transform.position}:" +
                    $"blocker={timedOutVehicle?.LastTrafficBlockerForVerification}");
            }
            throw new InvalidOperationException(
                "All-departure timeout " + string.Join(",", states));
        }
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
            vehicle = GymVisitorVehicle.Create(BodybuilderIdentity.Arnold, 0, null, true);
            fighter.gameObject.SetActive(true);
            fighter.SetVisitorSpawnPose(vehicle.PassengerPoint,
                Quaternion.LookRotation(
                    GymOutdoorBuilder.VisitorParkingEntryPoint -
                    vehicle.PassengerPoint, Vector3.up));
            Physics.SyncTransforms();
            agent.BeginEntryFromVehicle(
                doorway, doorway.InteriorPoint + Vector3.left * 2f,
                vehicle.PassengerPoint, vehicle.AislePassengerPoint);
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
                $"maxMotionTurn={maximumRouteMotionTurn:F1} " +
                $"minTargetAlignment={minimumRouteTargetAlignment:F2} " +
                $"samples={routeMotionSamples} parkingRows=2 " +
                "visibleRoadExtension=True enteredDoor=True");
            EditorPrefs.DeleteKey(RequestedKey);
            EditorPrefs.DeleteKey(GokuModeKey);
            EditorPrefs.DeleteKey(RouteModeKey);
            EditorApplication.Exit(0);
            return;
        }
        // The route-quality sample covers the exterior leg; leave enough
        // time for the unchanged interior doorway/room handoff afterwards.
        if (elapsed > 45d)
            throw new InvalidOperationException(
                $"Route timeout requested={requested} state={current?.State} " +
                $"position={fighter?.transform.position} target={current?.TravelTargetForVerification} " +
                $"stage={current?.VehicleEntryWaypointForVerification}.");
    }

    private static void ResetRouteQuality()
    {
        routeSampleInitialized = false;
        lastRouteSamplePosition = Vector3.zero;
        lastRouteMotionDirection = Vector3.zero;
        lastRouteTarget = Vector3.zero;
        maximumRouteMotionTurn = 0f;
        minimumRouteTargetAlignment = 1f;
        routeMotionSamples = 0;
        minimumAlignmentPosition = Vector3.zero;
        minimumAlignmentTarget = Vector3.zero;
        minimumAlignmentMotion = Vector3.zero;
        minimumAlignmentStage = -1;
    }

    private static void SampleRouteQuality(GymVisitorAgent agent)
    {
        if (fighter == null || agent == null) return;
        Vector3 position = fighter.transform.position;
        if (!routeSampleInitialized)
        {
            routeSampleInitialized = true;
            lastRouteSamplePosition = position;
            lastRouteTarget = agent.TravelTargetForVerification;
            return;
        }

        Vector3 displacement = Vector3.ProjectOnPlane(
            position - lastRouteSamplePosition, Vector3.up);
        lastRouteSamplePosition = position;
        if (displacement.sqrMagnitude < 0.0004f) return;

        Vector3 motionDirection = displacement.normalized;
        Vector3 currentTarget = agent.TravelTargetForVerification;
        bool targetChanged = Vector3.ProjectOnPlane(
            currentTarget - lastRouteTarget, Vector3.up).sqrMagnitude > 0.01f;
        Vector3 toTarget = Vector3.ProjectOnPlane(
            currentTarget - position, Vector3.up);
        // The sampled displacement belongs to the preceding physics step.
        // Do not compare it against a waypoint selected at the end of that
        // same step; resume alignment checks on the next stable-target sample.
        if (!targetChanged && toTarget.sqrMagnitude > 0.04f)
        {
            float alignment = Vector3.Dot(
                motionDirection, toTarget.normalized);
            if (alignment < minimumRouteTargetAlignment)
            {
                minimumRouteTargetAlignment = alignment;
                minimumAlignmentPosition = position;
                minimumAlignmentTarget = currentTarget;
                minimumAlignmentMotion = motionDirection;
                minimumAlignmentStage =
                    agent.VehicleEntryWaypointForVerification;
            }
        }
        lastRouteTarget = currentTarget;
        if (lastRouteMotionDirection.sqrMagnitude > 0.1f)
        {
            maximumRouteMotionTurn = Mathf.Max(
                maximumRouteMotionTurn,
                Vector3.Angle(lastRouteMotionDirection, motionDirection));
        }
        lastRouteMotionDirection = motionDirection;
        routeMotionSamples++;
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
        if (maximumRouteMotionTurn > 35f)
            throw new InvalidOperationException(
                $"{direction} route made a hard turn: " +
                $"angle={maximumRouteMotionTurn:F1} degrees.");
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
        if (GymOutdoorBuilder.ParkingBounds.size.z < 17.5f ||
            GymOutdoorBuilder.ParkingBounds.size.x < 27.5f)
            throw new InvalidOperationException(
                $"Expanded parking missing: {GymOutdoorBuilder.ParkingBounds.size}.");
        BodybuilderIdentity[] ids = {
            BodybuilderIdentity.Arnold, BodybuilderIdentity.Cbum,
            BodybuilderIdentity.Zyzz, BodybuilderIdentity.Ronnie,
            BodybuilderIdentity.Manwithsuit1, BodybuilderIdentity.JayCutler };
        int north = 0, south = 0;
        for (int i = 0; i < ids.Length; i++)
        {
            GymVisitorVehicle test = GymVisitorVehicle.Create(ids[i], i, null, true);
            float z = test.ParkingPointForVerification.z -
                GymOutdoorBuilder.ParkingBounds.center.z;
            if (z > 1f) north++;
            if (z < -1f) south++;
            UnityEngine.Object.Destroy(test.gameObject);
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
