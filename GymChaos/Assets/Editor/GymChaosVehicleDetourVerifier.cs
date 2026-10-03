#if UNITY_EDITOR
using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class GymChaosVehicleDetourVerifier
{
    private const string RequestedKey = "GymChaos.VehicleTwoPointDetourVerificationRequested";
    private static double startedAt;
    private static bool setupComplete;
    private static bool blockerPlaced;
    private static bool detourObserved;
    private static bool visitorFrozenForStall;
    private static bool movementSampleInitialized;
    private static float maximumMovementSampleStep;
    private static Vector3 lastSampledPosition;
    private static Vector3[] observedDetourWaypoints;
    private static bool[] detourWaypointReached;
    private static EnemyFighter visitor;
    private static EnemyFighter blocker;
    private static GymVisitorAgent visitorAgent;
    private static GymVisitorVehicle visitorVehicle;
    private static Rigidbody visitorBody;
    private static RigidbodyConstraints originalVisitorConstraints;
    private static double stallHoldStartedAt;
    private static float observedStallSeconds;
    private static bool visitorReachedBlockerApproach;
    private static Vector3 routeStart;
    private static Vector3 routeEnd;
    private static Vector3 detourResumeTarget;
    private static float minimumBlockerDistance = float.PositiveInfinity;
    private static bool detourSegmentsClear;
    private static bool movementSegmentsClear;
    private static Vector3 blockedMovementSegmentStart;
    private static Vector3 blockedMovementSegmentEnd;
    private static string blockedMovementSegmentCollider;
    private static string blockedRouteReport;

    static GymChaosVehicleDetourVerifier()
    {
        if (!SessionState.GetBool(RequestedKey, false)) return;
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
        EditorApplication.playModeStateChanged -= PlayModeChanged;
        EditorApplication.playModeStateChanged += PlayModeChanged;
    }

    [MenuItem("Tools/GymChaos/Run Two-Point Visitor Detour Verification")]
    public static void Run()
    {
        ResetState();
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        startedAt = EditorApplication.timeSinceStartup;
        SessionState.SetBool(RequestedKey, true);
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
        EditorApplication.playModeStateChanged -= PlayModeChanged;
        EditorApplication.playModeStateChanged += PlayModeChanged;
        EditorApplication.isPlaying = true;
    }

    private static void ResetState()
    {
        setupComplete = false;
        blockerPlaced = false;
        detourObserved = false;
        visitorFrozenForStall = false;
        movementSampleInitialized = false;
        maximumMovementSampleStep = 0f;
        lastSampledPosition = Vector3.zero;
        observedDetourWaypoints = null;
        detourWaypointReached = null;
        visitor = null;
        blocker = null;
        visitorAgent = null;
        visitorVehicle = null;
        visitorBody = null;
        originalVisitorConstraints = RigidbodyConstraints.None;
        stallHoldStartedAt = 0d;
        observedStallSeconds = 0f;
        visitorReachedBlockerApproach = false;
        routeStart = Vector3.zero;
        routeEnd = Vector3.zero;
        detourResumeTarget = Vector3.zero;
        minimumBlockerDistance = float.PositiveInfinity;
        detourSegmentsClear = false;
        movementSegmentsClear = true;
        blockedMovementSegmentStart = Vector3.zero;
        blockedMovementSegmentEnd = Vector3.zero;
        blockedMovementSegmentCollider = string.Empty;
        blockedRouteReport = string.Empty;
    }

    private static void PlayModeChanged(PlayModeStateChange change)
    {
        if (change == PlayModeStateChange.EnteredPlayMode)
        {
            startedAt = EditorApplication.timeSinceStartup;
            Time.timeScale = 1f;
        }
        else if (change == PlayModeStateChange.EnteredEditMode)
        {
            EditorApplication.update -= Tick;
            EditorApplication.playModeStateChanged -= PlayModeChanged;
            SessionState.EraseBool(RequestedKey);
            if (Application.isBatchMode) EditorApplication.Exit(1);
        }
    }

    private static void Tick()
    {
        if (!EditorApplication.isPlaying) return;
        try
        {
            double elapsed = EditorApplication.timeSinceStartup - startedAt;
            GymVisitorDirector director = UnityEngine.Object.FindAnyObjectByType<GymVisitorDirector>();
            if (!setupComplete && director != null)
            {
                BeginScenario(director);
            }
            if (setupComplete && visitor != null && blocker != null && visitorAgent != null)
            {
                TickScenario();
            }
            if (elapsed > 45d)
            {
                Fail(
                    $"timeout state={visitorAgent?.State} " +
                    $"position={visitor?.VisitorPhysicsPosition} " +
                    $"target={visitorAgent?.TravelTargetForVerification} " +
                    $"blockerPlaced={blockerPlaced} frozen={visitorFrozenForStall} " +
                    $"detourObserved={detourObserved} stallSeconds={observedStallSeconds:F2} " +
                    $"waypoints={visitorAgent?.VehicleRouteDetourWaypointCountForVerification} " +
                    $"blocker={visitor?.LastVisitorRouteBlocker}");
            }
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            Fail(exception.Message);
        }
    }

    private static void BeginScenario(GymVisitorDirector director)
    {
        PlayerMovement player = UnityEngine.Object.FindAnyObjectByType<PlayerMovement>();
        if (player == null ||
            !director.BeginCbumDepartureForVerification(out visitor, out visitorVehicle))
        {
            return;
        }

        visitorAgent = visitor != null ? visitor.GetComponent<GymVisitorAgent>() : null;
        EnemyFighter[] fighters = UnityEngine.Object.FindObjectsByType<EnemyFighter>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < fighters.Length; i++)
        {
            EnemyFighter candidate = fighters[i];
            if (candidate == null || candidate == visitor) continue;
            if (candidate.Identity == BodybuilderIdentity.JayCutler)
            {
                blocker = candidate;
            }
            else if (candidate.gameObject.activeSelf)
            {
                GymVisitorAgent candidateAgent = candidate.GetComponent<GymVisitorAgent>();
                if (candidateAgent != null)
                {
                    SetAgentField(candidateAgent, "enteredGym", false);
                    SetAgentField(candidateAgent, "leftGym", true);
                }
                candidate.gameObject.SetActive(false);
            }
        }
        if (visitorAgent == null || blocker == null || visitorVehicle == null)
        {
            throw new InvalidOperationException(
                "Cbum visitor, JayCutler blocker, or Cbum vehicle was not found.");
        }

        director.StopAllCoroutines();
        director.enabled = false;
        GymVisitorVehicle[] vehicles = UnityEngine.Object.FindObjectsByType<GymVisitorVehicle>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < vehicles.Length; i++)
        {
            if (vehicles[i] != null && vehicles[i] != visitorVehicle)
            {
                vehicles[i].gameObject.SetActive(false);
            }
        }
        Vector3 ownVehiclePosition = visitorVehicle.transform.position;
        visitorVehicle.gameObject.SetActive(false);

        blocker.gameObject.SetActive(true);
        blocker.Configure(
            blocker.Identity, player, blocker.MaxHealth,
            police: false, passive: true, countAsOpponent: false);
        GymVisitorAgent blockerAgent = blocker.GetComponent<GymVisitorAgent>();
        if (blockerAgent != null)
        {
            SetAgentField(blockerAgent, "enteredGym", false);
            SetAgentField(blockerAgent, "leftGym", true);
            blockerAgent.enabled = false;
        }
        Rigidbody blockerBody = blocker.GetComponent<Rigidbody>();
        if (blockerBody != null)
        {
            blockerBody.linearVelocity = Vector3.zero;
            blockerBody.angularVelocity = Vector3.zero;
            blockerBody.isKinematic = true;
        }

        Bounds parking = GymOutdoorBuilder.ParkingBounds;
        float routeReach = Mathf.Min(12f, parking.extents.x - 4f);
        if (parking.size.x < 24f || routeReach < 8f)
        {
            throw new InvalidOperationException($"Parking test lane is too short: {parking}.");
        }
        float floorY = visitor.VisitorPhysicsPosition.y;
        routeStart = new Vector3(parking.center.x - routeReach, floorY, parking.center.z);
        routeEnd = new Vector3(parking.center.x, floorY, parking.center.z);
        Vector3 route = routeEnd - routeStart;
        if (!visitor.IsVisitorExternalPathClearFrom(
                routeStart, route.normalized, route.magnitude))
        {
            throw new InvalidOperationException(
                $"Verification aisle is not clear before placing a blocker: " +
                $"start={routeStart} end={routeEnd} blocker={visitor.LastVisitorRouteBlocker}.");
        }

        visitorBody = visitor.GetComponent<Rigidbody>();
        if (visitorBody == null)
        {
            throw new InvalidOperationException("Cbum visitor has no Rigidbody.");
        }
        originalVisitorConstraints = visitorBody.constraints;
        visitorBody.position = routeStart;
        visitor.transform.position = routeStart;
        Physics.SyncTransforms();
        movementSampleInitialized = true;
        lastSampledPosition = visitor.VisitorPhysicsPosition;
        maximumMovementSampleStep = 0f;
        movementSegmentsClear = true;
        blockedMovementSegmentStart = Vector3.zero;
        blockedMovementSegmentEnd = Vector3.zero;
        blockedMovementSegmentCollider = string.Empty;

        // The first two authored vehicle waypoints intentionally use a broad
        // 6.5 m approach radius. Start this focused detour at waypoint 2 so the
        // verifier exercises precise destination arrival after the detour.
        SetAgentField("vehicleExitWaypoints", new[] { routeStart, routeStart, routeEnd });
        SetAgentField("vehicleExitWaypointIndex", 2);
        SetAgentField("vehicleExitUsesProteinStoreRoute", false);
        SetAgentField("vehicleExitUsesDavieBusGate", false);
        SetAgentField("vehicleDetourActive", false);
        SetAgentField("vehicleDetourWaypoints", null);
        SetAgentField("vehicleDetourWaypointIndex", 0);
        SetAgentField("vehicleRouteDetourCount", 0);
        SetAgentField("travelTarget", routeEnd);
        SetAgentField("state", GymVisitorAgent.VisitorState.ApproachingVehicle);
        SetAgentField("enteredGym", false);
        SetAgentField("leftGym", true);
        SetAgentField("vehicleBoardingRadius", 0.25f);
        SetAgentField("lastVehicleRouteDistance", route.magnitude);
        SetAgentField("vehicleRouteStalledSeconds", 0f);
        visitorFrozenForStall = false;
        stallHoldStartedAt = EditorApplication.timeSinceStartup;

        Vector3 blockerPosition = (routeStart + routeEnd) * 0.5f;
        blockerPosition.y = floorY;
        if (blockerBody != null) blockerBody.position = blockerPosition;
        blocker.transform.position = blockerPosition;
        Physics.SyncTransforms();
        blockerPlaced = true;
        bool directRouteClear = visitor.IsVisitorExternalPathClearFrom(
            routeStart, route.normalized, route.magnitude);
        blockedRouteReport = visitor.LastVisitorRouteBlocker ?? string.Empty;
        if (directRouteClear ||
            blockedRouteReport.IndexOf("owner=JayCutler", StringComparison.OrdinalIgnoreCase) < 0)
        {
            throw new InvalidOperationException(
                $"JayCutler did not block the test route: clear={directRouteClear} " +
                $"blocker={blockedRouteReport}.");
        }

        setupComplete = true;
        Debug.Log(
            $"GYMCHAOS_TWO_POINT_DETOUR_STARTED visitor=Cbum blocker=JayCutler " +
            $"lane={parking} start={routeStart} end={routeEnd} " +
            $"blocker={blockerPosition} unobstructedRoute=True directBlocked=True " +
            $"stallTrigger=natural ownVehicleDisabled=True vehiclePosition={ownVehiclePosition} " +
            $"testBoardingRadius=0.25 exitWaypointIndex=2");
    }

    private static void TickScenario()
    {
        if (!detourObserved &&
            visitorAgent.State != GymVisitorAgent.VisitorState.ApproachingVehicle &&
            visitorAgent.State != GymVisitorAgent.VisitorState.Dormant)
        {
            return;
        }

        Vector3 position = visitor.VisitorPhysicsPosition;
        if (movementSampleInitialized)
        {
            Vector3 movementDelta = Vector3.ProjectOnPlane(
                position - lastSampledPosition, Vector3.up);
            float movementStep = movementDelta.magnitude;
            maximumMovementSampleStep = Mathf.Max(maximumMovementSampleStep, movementStep);
            if (movementSegmentsClear && movementStep > 0.02f &&
                !IsSegmentClear(lastSampledPosition, position))
            {
                movementSegmentsClear = false;
                blockedMovementSegmentStart = lastSampledPosition;
                blockedMovementSegmentEnd = position;
                blockedMovementSegmentCollider = visitor.LastVisitorRouteBlocker;
            }
        }
        lastSampledPosition = position;

        float separation = Vector3.ProjectOnPlane(
            position - blocker.VisitorPhysicsPosition, Vector3.up).magnitude;
        minimumBlockerDistance = Mathf.Min(minimumBlockerDistance, separation);
        float approachClearance = EnemyFighter.GetBodyRadiusForIdentity(visitor.Identity) +
            EnemyFighter.GetBodyRadiusForIdentity(blocker.Identity) + 0.05f;
        visitorReachedBlockerApproach |= separation <= approachClearance + 0.9f;
        observedStallSeconds = Mathf.Max(
            observedStallSeconds, visitorAgent.VehicleRouteStalledSecondsForVerification);

        if (!detourObserved)
        {
            float naturalEndpointDistance = Vector3.ProjectOnPlane(
                routeEnd - position, Vector3.up).magnitude;
            if (visitorAgent.VehicleRouteDetourCountForVerification <= 0 &&
                visitorAgent.State == GymVisitorAgent.VisitorState.Dormant &&
                naturalEndpointDistance <= 0.65f)
            {
                if (!visitorReachedBlockerApproach || !movementSegmentsClear ||
                    maximumMovementSampleStep > 0.65f)
                {
                    Fail($"natural_blocker_bypass_failed approached={visitorReachedBlockerApproach} " +
                        $"segmentsClear={movementSegmentsClear} " +
                        $"minRootSeparation={minimumBlockerDistance:F2} " +
                        $"maxStep={maximumMovementSampleStep:F2} " +
                        $"failedSegment={blockedMovementSegmentStart}->{blockedMovementSegmentEnd} " +
                        $"collider={blockedMovementSegmentCollider}");
                    return;
                }

                Debug.Log(
                    $"GYMCHAOS_VEHICLE_BLOCKER_BYPASS_OK visitor=Cbum blocker=JayCutler " +
                    $"naturalSteering=True detourOccurred=False reachedTarget=True " +
                    $"endpointDistance={naturalEndpointDistance:F2} " +
                    $"minClearance={minimumBlockerDistance:F2} " +
                    $"segmentsClear={movementSegmentsClear} " +
                    $"maxSampleStep={maximumMovementSampleStep:F2} " +
                    $"start={routeStart} end={routeEnd}");
                Finish(0);
                return;
            }

            if (visitorAgent.VehicleRouteDetourCountForVerification <= 0)
            {
                return;
            }

            float elapsedToDetour = (float)(
                EditorApplication.timeSinceStartup - stallHoldStartedAt);
            if (observedStallSeconds < 2.25f)
            {
                Fail(
                    $"detour_triggered_before_natural_stall_timeout " +
                    $"stall={observedStallSeconds:F2}s elapsed={elapsedToDetour:F2}s");
                return;
            }
            if (!visitorReachedBlockerApproach)
            {
                Fail($"detour_triggered_before_visitor_reached_blocker " +
                    $"clearance={minimumBlockerDistance:F2}m");
                return;
            }

            RestoreVisitorConstraints();
            detourObserved = true;
            detourResumeTarget = visitorAgent.VehicleRouteDetourResumeTargetForVerification;
            observedDetourWaypoints = visitorAgent.VehicleRouteDetourWaypointsForVerification;
            if (observedDetourWaypoints.Length < 1 || observedDetourWaypoints.Length > 2)
            {
                Fail(
                    $"unexpected_dynamic_detour_waypoint_count " +
                    $"waypoints={observedDetourWaypoints.Length} blocker={blockedRouteReport}");
                return;
            }

            detourWaypointReached = new bool[observedDetourWaypoints.Length];
            Vector3 segmentStart = position;
            detourSegmentsClear = true;
            for (int i = 0; i < observedDetourWaypoints.Length; i++)
            {
                Vector3 waypoint = observedDetourWaypoints[i];
                if (!IsSegmentClear(segmentStart, waypoint))
                {
                    detourSegmentsClear = false;
                    break;
                }
                segmentStart = waypoint;
            }
            if (detourSegmentsClear)
            {
                detourSegmentsClear = IsSegmentClear(segmentStart, detourResumeTarget);
            }
            if (!detourSegmentsClear)
            {
                Fail("one_or_more_dynamic_detour_segments_are_blocked");
                return;
            }

            Debug.Log(
                $"GYMCHAOS_VEHICLE_DETOUR_STALL_TRIGGERED elapsed={elapsedToDetour:F2}s stall={observedStallSeconds:F2}s " +
                $"natural=True waypointCount={observedDetourWaypoints.Length} " +
                $"blocker={blockedRouteReport}");
            Debug.Log(
                $"GYMCHAOS_VEHICLE_DETOUR_OBSERVED segmentsClear=True " +
                $"waypoints={string.Join("|", observedDetourWaypoints)} " +
                $"resume={detourResumeTarget}");
        }

        bool allDetourWaypointsReached = detourWaypointReached != null;
        for (int i = 0; i < observedDetourWaypoints.Length; i++)
        {
            if (Vector3.ProjectOnPlane(
                    position - observedDetourWaypoints[i], Vector3.up).magnitude <= 0.65f)
            {
                detourWaypointReached[i] = true;
            }
            allDetourWaypointsReached &= detourWaypointReached[i];
        }

        bool detourResumed = detourObserved && allDetourWaypointsReached &&
            !visitorAgent.VehicleRouteDetourActiveForVerification &&
            Vector3.Distance(visitorAgent.TravelTargetForVerification, detourResumeTarget) < 0.15f;
        if (!detourResumed)
        {
            return;
        }

        float endpointDistance = Vector3.ProjectOnPlane(
            routeEnd - position, Vector3.up).magnitude;
        if (endpointDistance > 0.65f)
        {
            return;
        }

        if (maximumMovementSampleStep > 0.65f)
        {
            Fail(
                $"visitor_movement_discontinuous maxSampleStep={maximumMovementSampleStep:F2}m " +
                $"limit=0.65m");
            return;
        }

        if (!detourSegmentsClear || !movementSegmentsClear)
        {
            Fail($"visitor_collision_sweep_failed plannedSegments={detourSegmentsClear} " +
                $"movementSegments={movementSegmentsClear}");
            return;
        }

        Debug.Log(
            $"GYMCHAOS_VEHICLE_DETOUR_OK visitor=Cbum blocker=JayCutler " +
            $"waypoints={observedDetourWaypoints.Length} segmentsClear={detourSegmentsClear} " +
            $"allWaypointsReached={allDetourWaypointsReached} resumed=True " +
            $"reachedTarget=True endpointDistance={endpointDistance:F2} " +
            $"stallSeconds={observedStallSeconds:F2} " +
            $"maxSampleStep={maximumMovementSampleStep:F2} " +
            $"minRootSeparation={minimumBlockerDistance:F2} " +
            $"movementSegmentsClear={movementSegmentsClear} " +
            $"start={routeStart} end={routeEnd}");
        Finish(0);
    }
    private static void RestoreVisitorConstraints()
    {
        if (visitorBody == null || !visitorFrozenForStall)
        {
            return;
        }

        visitorBody.constraints = originalVisitorConstraints;
        if (!visitorBody.isKinematic)
        {
            visitorBody.linearVelocity = Vector3.zero;
            visitorBody.angularVelocity = Vector3.zero;
        }
        visitorFrozenForStall = false;
    }
    private static void SetAgentField(string name, object value)
    {
        SetAgentField(visitorAgent, name, value);
    }

    private static void SetAgentField(GymVisitorAgent agent, string name, object value)
    {
        FieldInfo field = typeof(GymVisitorAgent).GetField(
            name, BindingFlags.Instance | BindingFlags.NonPublic);
        if (field == null)
        {
            throw new MissingFieldException(typeof(GymVisitorAgent).FullName, name);
        }
        field.SetValue(agent, value);
    }

    private static bool IsSegmentClear(Vector3 from, Vector3 to)
    {
        Vector3 segment = Vector3.ProjectOnPlane(to - from, Vector3.up);
        float distance = segment.magnitude;
        return distance <= 0.0001f || visitor.IsVisitorExternalPathClearFrom(
            from, segment / distance, distance);
    }

    private static void Fail(string reason)
    {
        Debug.LogError("GYMCHAOS_VEHICLE_DETOUR_FAILED " + reason);
        Finish(1);
    }

    private static void Finish(int resultCode)
    {
        RestoreVisitorConstraints();
        if (visitorAgent != null)
        {
            try
            {
                SetAgentField("enteredGym", false);
                SetAgentField("leftGym", true);
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"Visitor detour verifier teardown cleanup failed: {exception.Message}");
            }
        }

        SessionState.EraseBool(RequestedKey);
        EditorApplication.update -= Tick;
        EditorApplication.playModeStateChanged -= PlayModeChanged;
        if (Application.isBatchMode)
        {
            GymChaosVerifierExit.Exit(resultCode);
        }
        else
        {
            EditorApplication.isPlaying = false;
        }
    }
}
#endif