using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class GymChaosDeadliftPathVerifier
{
    private const string RequestedKey = "GymChaos.DeadliftPathVerificationRequested";
    private static readonly Collider[] overlapBuffer = new Collider[64];
    private static readonly List<Vector3> route = new List<Vector3>();
    private static double startedAt;
    private static double lastProgressAt;
    private static bool routeStarted;
    private static bool finished;
    private static int resultCode = 1;
    private static EnemyFighter fighter;
    private static GymDeadliftStationMarker station;
    private static Vector3 destination;
    private static Vector3 furthestPosition;
    private static float floorY;
    private static float clearance;
    private static int escapeBaseline;
    private static Vector3 lastObservedPosition;
    private static float maximumFrameStep;
    private static bool egressPhaseStarted;
    private static bool egressClearanceExited;
    private static Vector3 egressStartPosition;
    private static Vector3 egressDirection;
    private static float furthestEgressProgress;

    static GymChaosDeadliftPathVerifier()
    {
        if (!SessionState.GetBool(RequestedKey, false))
        {
            return;
        }

        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        EditorApplication.delayCall += ResumeAfterDomainReload;
    }

    [MenuItem("Tools/GymChaos/Run Deadlift Path Verification")]
    public static void Run()
    {
        ResetState();
        SessionState.SetBool(RequestedKey, true);
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        EditorApplication.isPlaying = true;
    }

    private static void ResetState()
    {
        startedAt = 0d;
        lastProgressAt = 0d;
        routeStarted = false;
        finished = false;
        resultCode = 1; GymChaosVerifierExit.Record(resultCode);
        fighter = null;
        station = null;
        destination = Vector3.zero;
        furthestPosition = Vector3.zero;
        floorY = 0f;
        clearance = 0f;
        escapeBaseline = 0;
        lastObservedPosition = Vector3.zero;
        maximumFrameStep = 0f;
        egressPhaseStarted = false;
        egressClearanceExited = false;
        egressStartPosition = Vector3.zero;
        egressDirection = Vector3.zero;
        furthestEgressProgress = 0f;
        route.Clear();
    }

    private static void ResumeAfterDomainReload()
    {
        if (!EditorApplication.isPlaying)
        {
            return;
        }

        startedAt = EditorApplication.timeSinceStartup;
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
    }

    private static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            Time.timeScale = 1f;
            startedAt = EditorApplication.timeSinceStartup;
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
        }
        else if (state == PlayModeStateChange.EnteredEditMode)
        {
            EditorApplication.update -= Tick;
            SessionState.EraseBool(RequestedKey);
            if (Application.isBatchMode)
            {
                GymChaosVerifierExit.Exit(resultCode);
            }
        }
    }

    private static void Tick()
    {
        if (finished || !EditorApplication.isPlaying)
        {
            return;
        }

        double elapsed = EditorApplication.timeSinceStartup - startedAt;
        if (!routeStarted)
        {
            if (elapsed < 2d)
            {
                return;
            }

            if (!TryStartRoute())
            {
                return;
            }
        }

        if (fighter == null || station == null)
        {
            Fail("route_actor_or_station_lost");
            return;
        }

        Vector3 position = fighter.VisitorPhysicsPosition;
        float frameStep = Vector3.Distance(lastObservedPosition, position);
        maximumFrameStep = Mathf.Max(maximumFrameStep, frameStep);
        if (frameStep > 1.5f)
        {
            Fail($"discontinuous_step distance={frameStep:0.00} from={lastObservedPosition} to={position}");
            return;
        }
        lastObservedPosition = position;

        bool insideClearance = station.IsInsideNavigationClearance(position, clearance);
        if (!egressPhaseStarted && insideClearance)
        {
            Fail($"entered_deadlift_clearance position={position}");
            return;
        }
        if (egressPhaseStarted)
        {
            float egressProgress = Vector3.Dot(
                Vector3.ProjectOnPlane(position - egressStartPosition, Vector3.up),
                egressDirection);
            if (egressProgress < furthestEgressProgress - 0.18f)
            {
                Fail(
                    $"egress_progress_reversed progress={egressProgress:0.00} " +
                    $"furthest={furthestEgressProgress:0.00} position={position}");
                return;
            }
            furthestEgressProgress = Mathf.Max(furthestEgressProgress, egressProgress);
            if (!insideClearance)
            {
                egressClearanceExited = true;
            }
            else if (egressClearanceExited)
            {
                Fail($"egress_reentered_deadlift_clearance position={position}");
                return;
            }
        }

        if (Mathf.Abs(position.y - floorY) > 0.22f)
        {
            Fail($"not_grounded position={position} floorY={floorY:0.000}");
            return;
        }
        int escapeCount = fighter.DeadliftEscapeCountForVerification - escapeBaseline;
        int allowedEscapeCount = egressPhaseStarted ? 1 : 0;
        if (escapeCount > allowedEscapeCount)
        {
            Fail($"platform_escape_repeated count={escapeCount} allowed={allowedEscapeCount}");
            return;
        }

        float progress = Vector3.ProjectOnPlane(
            position - furthestPosition, Vector3.up).magnitude;
        if (progress > 0.18f)
        {
            furthestPosition = position;
            lastProgressAt = EditorApplication.timeSinceStartup;
        }

        float remaining = Vector3.ProjectOnPlane(
            destination - position, Vector3.up).magnitude;
        if (!insideClearance && remaining <= 0.92f)
        {
            if (!egressPhaseStarted)
            {
                if (!TryStartEgressPhase())
                {
                    return;
                }
                return;
            }

            finished = true;
            resultCode = 0; GymChaosVerifierExit.Record(resultCode);
            Debug.Log(
                $"GYMCHAOS_DEADLIFT_PATH_OK enemy={fighter.Identity} " +
                $"remaining={remaining:0.00} routePoints={route.Count} " +
                $"groundY={position.y:0.000} crossingEscapes=0 " +
                $"egressReroutes={fighter.DeadliftEscapeCountForVerification - escapeBaseline} "+
                $"clearance={clearance:0.00} egress=True " +
                $"egressProgress={furthestEgressProgress:0.00} " +
                $"maxFrameStep={maximumFrameStep:0.00}");
            EditorApplication.isPlaying = false;
            return;
        }

        if (EditorApplication.timeSinceStartup - lastProgressAt > 4.2d)
        {
            Fail(
                $"stalled remaining={remaining:0.00} position={position} " +
                $"routeRemaining={fighter.CurrentRoamRouteRemaining} " +
                $"blocker={fighter.LastVisitorRouteBlocker}");
            return;
        }

        if (elapsed > 30d)
        {
            Fail(
                $"timeout remaining={remaining:0.00} position={position} " +
                $"routeRemaining={fighter.CurrentRoamRouteRemaining}");
        }
    }
    private static bool TryStartRoute()
    {
        GymVisitorDirector director =
            Object.FindAnyObjectByType<GymVisitorDirector>();
        if (director == null)
        {
            if (EditorApplication.timeSinceStartup - startedAt > 24d)
            {
                Fail("director_not_ready");
            }
            return false;
        }

        director.SuspendVisitorSimulationForVerification();
        EnemyFighter[] fighters = Object.FindObjectsByType<EnemyFighter>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int index = 0; index < fighters.Length; index++)
        {
            EnemyFighter candidate = fighters[index];
            if (candidate != null && candidate.Identity == BodybuilderIdentity.Goku &&
                candidate.gameObject.activeInHierarchy && !candidate.IsDead)
            {
                fighter = candidate;
                break;
            }
        }

        station = Object.FindAnyObjectByType<GymDeadliftStationMarker>();
        GameObject floorObject = GameObject.Find("Rubber Floor");
        Renderer floorRenderer = floorObject != null
            ? floorObject.GetComponent<Renderer>()
            : null;
        if (fighter == null || station == null || floorRenderer == null)
        {
            if (EditorApplication.timeSinceStartup - startedAt > 15d)
            {
                Fail(
                    $"scene_contract_missing fighter={fighter != null} " +
                    $"station={station != null} floor={floorRenderer != null}");
            }
            return false;
        }

        for (int index = 0; index < fighters.Length; index++)
        {
            if (fighters[index] != null && fighters[index] != fighter)
            {
                fighters[index].gameObject.SetActive(false);
            }
        }

        PlayerMovement[] players = Object.FindObjectsByType<PlayerMovement>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        int isolatedPlayers = 0;
        for (int index = 0; index < players.Length; index++)
        {
            if (players[index] != null)
            {
                Debug.Log(
                    $"GYMCHAOS_DEADLIFT_PATH_PLAYER name={players[index].name} " +
                    $"position={players[index].transform.position}");
                players[index].transform.position += Vector3.up * 1000f;
                players[index].gameObject.SetActive(false);
                isolatedPlayers++;
            }
        }
        Physics.SyncTransforms();

        Debug.Log($"GYMCHAOS_DEADLIFT_PATH_PLAYERS_ISOLATED count={isolatedPlayers}");

        Bounds floor = floorRenderer.bounds;
        floorY = floor.max.y;
        clearance = EnemyFighter.GetBodyRadiusForIdentity(fighter.Identity) + 0.18f;
        if (!TryChooseEndpoints(floor, out Vector3 start, out destination))
        {
            Fail("no_platform_crossing_segment_inside_gym");
            return false;
        }

        escapeBaseline = fighter.DeadliftEscapeCountForVerification;
        fighter.SetVisitorSpawnPose(
            start, Quaternion.LookRotation(destination - start, Vector3.up), true);
        if (!fighter.TryBuildVisitorRoute(destination, route))
        {
            Fail("production_route_builder_returned_false");
            return false;
        }

        Vector3 previous = start;
        for (int index = 0; index < route.Count; index++)
        {
            if (station.DoesSegmentCrossNavigationClearance(
                    previous, route[index], clearance))
            {
                Fail(
                    $"route_crosses_deadlift_clearance segment={index} " +
                    $"from={previous} to={route[index]} points={route.Count}");
                return false;
            }
            previous = route[index];
        }
        if (station.DoesSegmentCrossNavigationClearance(
                previous, destination, clearance))
        {
            Fail(
                $"final_segment_crosses_deadlift_clearance from={previous} " +
                $"to={destination} points={route.Count}");
            return false;
        }

        if (!fighter.QueueRoamDestinationForVerification(destination))
        {
            Fail("production_roam_route_queue_failed");
            return false;
        }

        routeStarted = true;
        lastObservedPosition = start;
        maximumFrameStep = 0f;
        furthestPosition = start;
        lastProgressAt = EditorApplication.timeSinceStartup;
        Debug.Log(
            $"GYMCHAOS_DEADLIFT_PATH_TEST_STARTED enemy={fighter.Identity} " +
            $"routePoints={route.Count} start={start} target={destination} " +
            $"clearance={clearance:0.00} platformEscapesBefore={escapeBaseline}");
        return true;
    }

    private static bool TryStartEgressPhase()
    {
        if (!station.TryGetNavigationFootprint(out Bounds footprint))
        {
            Fail("egress_footprint_missing");
            return false;
        }

        Vector3 scale = station.transform.lossyScale;
        float localClearanceX = clearance / Mathf.Max(0.001f, Mathf.Abs(scale.x));
        Vector3 localStart = footprint.center;
        localStart.x = -(footprint.extents.x + localClearanceX * 0.95f);
        Vector3 localTarget = footprint.center;
        localTarget.x = -(footprint.extents.x + localClearanceX +
            1.55f / Mathf.Max(0.001f, Mathf.Abs(scale.x)));
        egressStartPosition = station.transform.TransformPoint(localStart);
        destination = station.transform.TransformPoint(localTarget);
        egressStartPosition.y = floorY;
        destination.y = floorY;

        if (!station.IsInsideNavigationClearance(egressStartPosition, clearance) ||
            station.IsInsideNavigationClearance(destination, clearance) ||
            !IsProbePointClear(destination))
        {
            Fail(
                $"egress_test_endpoints_invalid startInside=" +
                $"{station.IsInsideNavigationClearance(egressStartPosition, clearance)} " +
                $"targetInside={station.IsInsideNavigationClearance(destination, clearance)} " +
                $"target={destination}");
            return false;
        }

        egressDirection = Vector3.ProjectOnPlane(
            destination - egressStartPosition, Vector3.up).normalized;
        egressPhaseStarted = true;
        egressClearanceExited = false;
        furthestEgressProgress = 0f;
        fighter.SetVisitorSpawnPose(
            egressStartPosition,
            Quaternion.LookRotation(egressDirection, Vector3.up), true);
        escapeBaseline = fighter.DeadliftEscapeCountForVerification;

        if (!fighter.TryBuildVisitorRoute(destination, route))
        {
            Fail("egress_production_route_builder_returned_false");
            return false;
        }

        Vector3 previous = egressStartPosition;
        bool initialEgressSegmentValidated = false;
        for (int index = 0; index < route.Count; index++)
        {
            if (station.DoesSegmentCrossNavigationClearance(
                    previous, route[index], clearance))
            {
                if (index != 0 || !IsEgressSegment(previous, route[index]))
                {
                    Fail(
                        $"egress_route_crosses_deadlift_clearance segment={index} " +
                        $"from={previous} to={route[index]} points={route.Count}");
                    return false;
                }
                initialEgressSegmentValidated = true;
            }
            previous = route[index];
        }

        if (station.DoesSegmentCrossNavigationClearance(
                previous, destination, clearance))
        {
            if (route.Count != 0 || !IsEgressSegment(previous, destination))
            {
                Fail(
                    $"egress_final_segment_crosses_clearance from={previous} " +
                    $"to={destination} points={route.Count}");
                return false;
            }
            initialEgressSegmentValidated = true;
        }
        if (!initialEgressSegmentValidated)
        {
            Fail("egress_route_did_not_cross_clearance_boundary");
            return false;
        }

        if (!fighter.QueueRoamDestinationForVerification(destination))
        {
            Fail("egress_production_roam_route_queue_failed");
            return false;
        }

        lastObservedPosition = egressStartPosition;
        furthestPosition = egressStartPosition;
        lastProgressAt = EditorApplication.timeSinceStartup;
        Debug.Log(
            $"GYMCHAOS_DEADLIFT_EGRESS_STARTED enemy={fighter.Identity} " +
            $"start={egressStartPosition} target={destination} " +
            $"routePoints={route.Count} direction={egressDirection}");
        return true;
    }

    private static bool IsEgressSegment(Vector3 from, Vector3 to)
    {
        Vector3 movement = Vector3.ProjectOnPlane(to - from, Vector3.up);
        return !station.IsInsideNavigationClearance(to, clearance) &&
            movement.sqrMagnitude > 0.01f &&
            Vector3.Dot(movement.normalized, egressDirection) >= 0.35f &&
            Vector3.Dot(movement, egressDirection) > 0.05f;
    }
    private static bool TryChooseEndpoints(
        Bounds floor, out Vector3 start, out Vector3 target)
    {
        start = default;
        target = default;
        if (!station.TryGetNavigationFootprint(out Bounds footprint))
        {
            return false;
        }

        Vector3 localCenter = footprint.center;
        Vector3[] localAxes = { Vector3.right, Vector3.forward };
        for (int axisIndex = 0; axisIndex < localAxes.Length; axisIndex++)
        {
            Vector3 axis = localAxes[axisIndex];
            float axisExtent = axisIndex == 0
                ? footprint.extents.x
                : footprint.extents.z;
            for (int marginIndex = 0; marginIndex < 7; marginIndex++)
            {
                float bodyMargin = clearance + 2.2f + marginIndex * 1.2f;
                float halfSpan = axisExtent + bodyMargin;
                Vector3 localStart = localCenter - axis * halfSpan;
                Vector3 localTarget = localCenter + axis * halfSpan;
                Vector3 worldStart = station.transform.TransformPoint(localStart);
                Vector3 worldTarget = station.transform.TransformPoint(localTarget);
                worldStart.y = floorY;
                worldTarget.y = floorY;

                bool startInsideFloor = InsideFloor(
                    floor, worldStart, clearance * 0.55f);
                bool targetInsideFloor = InsideFloor(
                    floor, worldTarget, clearance * 0.55f);
                bool startInsideClearance =
                    station.IsInsideNavigationClearance(worldStart, clearance);
                bool targetInsideClearance =
                    station.IsInsideNavigationClearance(worldTarget, clearance);
                bool startClear = IsProbePointClear(worldStart);
                bool targetClear = IsProbePointClear(worldTarget);
                Debug.Log(
                    $"GYMCHAOS_DEADLIFT_PATH_CANDIDATE axis={axisIndex} " +
                    $"margin={bodyMargin:0.0} footprint={footprint} " +
                    $"start={worldStart} target={worldTarget} " +
                    $"startFloor={startInsideFloor} targetFloor={targetInsideFloor} " +
                    $"startInClearance={startInsideClearance} " +
                    $"targetInClearance={targetInsideClearance} " +
                    $"startClear={startClear} targetClear={targetClear}");

                if (!startInsideFloor || !targetInsideFloor ||
                    startInsideClearance || targetInsideClearance ||
                    !startClear || !targetClear)
                {
                    continue;
                }

                start = worldStart;
                target = worldTarget;
                return true;
            }
        }

        return false;
    }
    private static bool InsideFloor(Bounds floor, Vector3 point, float margin)
    {
        return point.x >= floor.min.x + margin &&
            point.x <= floor.max.x - margin &&
            point.z >= floor.min.z + margin &&
            point.z <= floor.max.z - margin;
    }

    private static bool IsProbePointClear(Vector3 point)
    {
        Vector3 lower = point + Vector3.up * 0.26f;
        Vector3 upper = point + Vector3.up * 1.3f;
        int count = Physics.OverlapCapsuleNonAlloc(
            lower, upper, clearance * 0.72f, overlapBuffer,
            ~0, QueryTriggerInteraction.Ignore);
        for (int index = 0; index < count; index++)
        {
            Collider hit = overlapBuffer[index];
            if (hit == null)
            {
                continue;
            }
            if (hit.GetComponentInParent<EnemyFighter>() != null ||
                hit.GetComponentInParent<PlayerMovement>() != null ||
                hit.name == "Rubber Floor" ||
                hit.GetComponentInParent<GymDeadliftStationMarker>() != null)
            {
                continue;
            }
            return false;
        }
        return true;
    }

    private static void Fail(string reason)
    {
        if (finished)
        {
            return;
        }

        finished = true;
        resultCode = 1; GymChaosVerifierExit.Record(resultCode);
        Debug.LogError($"GYMCHAOS_DEADLIFT_PATH_FAILED reason={reason}");
        EditorApplication.isPlaying = false;
    }
}
