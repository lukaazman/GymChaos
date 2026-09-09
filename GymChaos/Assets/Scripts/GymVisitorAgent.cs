using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Per-enemy bridge between the visitor director and EnemyFighter's normal
/// roaming. It owns only door travel and scheduled squat travel; free roaming
/// is handed back to EnemyFighter so existing behavior remains intact.
/// </summary>
[DefaultExecutionOrder(1050)]
public sealed class GymVisitorAgent : MonoBehaviour
{
    private const float WorkoutApproachStallTimeout = 2.4f;
    private const float WorkoutApproachTimeout = 18f;
    private const float FailedWorkoutFreeRoamSeconds = 3f;
    private const float WorkoutReleaseStallTimeout = 2.4f;
    private const float WorkoutReleaseTimeout = 8f;
    private const float WorkoutReleaseDistance = 3.35f;
    private const float DoorwayClearance = 1.35f;
    private const float DoorwayBodyRadius = 0.58f;
    private const float DoorwayExitStallTimeout = 4f;
    private const float VehicleRouteStallTimeout = 2.4f;
    private const float VehicleYieldSideStep = 3.2f;
    private const float VehicleYieldBackwardOffset = 0.6f;
    private const float VehicleYieldSpeed = 3.2f;
    private const float VehicleYieldHoldSeconds = 0.25f;
    private const float VehicleYieldTimeout = 4.5f;

    public enum VisitorState
    {
        Dormant,
        FreeRoaming,
        ApproachingGymFromVehicle,
        EnteringDoor,
        EnteringRoom,
        ApproachingWorkout,
        Squatting,
        ExitingDoor,
        LeavingGym,
        ApproachingVehicle
    }

    private EnemyFighter fighter;
    private GymDoorway doorway;
    private GymExerciseStation pendingStation;
    private SquatWorkoutController squatController;
    private VisitorState state = VisitorState.Dormant;
    private Vector3 travelTarget;
    private Vector3 roomTarget;
    private int pendingRepetitions;
    private float pendingRepDuration;
    private bool enteredGym;
    private bool leftGym;
    private bool hasSuccessfulEntry;
    private bool completedDoorExit;
    private bool reachedVehicle;
    private int completedVehicleApproaches;
    private Vector3 finalVehicleTarget;
    private Vector3[] vehicleEntryWaypoints;
    private int vehicleEntryWaypointIndex;
    private Vector3[] vehicleExitWaypoints;
    private int vehicleExitWaypointIndex;
    private Vector3 vehicleEntrySpawnPoint;
    private bool vehicleEntrySpawnPending;
    private float vehicleBoardingRadius = 0.55f;
    private bool vehicleTurnPending;
    private bool vehicleEntryPending;
    private bool vehicleAislePending;
    private Vector3 vehicleAisleTarget;
    private bool vehicleDetourActive;
    private Vector3 vehicleDetourResumeTarget;
    private float vehicleRouteStalledSeconds;
    private float lastVehicleRouteDistance = float.PositiveInfinity;
    private int vehicleRerouteAttempt;
    private int vehicleRouteDetourCount;
    private GymVisitorVehicle routeVehicleWithIgnoredCollision;
    private GymVisitorVehicle yieldingToVehicle;
    private Vector3 vehicleYieldPoint;
    private float vehicleYieldStartedAt;
    private float vehicleYieldReachedAt = -1f;
    private int vehicleYieldCount;
    private bool entryRoomClearPointPending;
    private bool exitRoomClearPointPending;
    private bool doorOpenRequestHeld;
    private Vector3 doorwayClearPoint;
    private bool hasDoorwayClearPoint;
    private int completedWorkoutVersion;
    private float postWorkoutFreeRoamUntil;
    private float roomTravelStalledSeconds;
    private float lastRoomTravelDistance = float.PositiveInfinity;
    private float doorwayExitStalledSeconds;
    private float lastDoorwayExitDistance = float.PositiveInfinity;
    private int doorwayExitRecoveryCount;
    private float workoutApproachStartedAt;
    private float workoutApproachStalledSeconds;
    private float lastWorkoutApproachDistance = float.PositiveInfinity;
    private bool squatStartPending;
    private GymExerciseStation workoutReleaseStation;
    private Vector3 workoutReleaseTarget;
    private float workoutReleaseStartedAt;
    private float workoutReleaseStalledSeconds;
    private float lastWorkoutReleaseDistance = float.PositiveInfinity;
    private bool applicationQuitting;
    private readonly List<GymExerciseStation> attemptedWorkoutStations =
        new List<GymExerciseStation>(3);
    private readonly Collider[] doorwayPointHits = new Collider[32];

    public VisitorState State => state;
    public bool IsInsideGym => enteredGym && !leftGym && state != VisitorState.Dormant &&
        state != VisitorState.ExitingDoor && state != VisitorState.LeavingGym;
    public bool IsTraveling => state == VisitorState.EnteringDoor ||
        state == VisitorState.ApproachingGymFromVehicle ||
        state == VisitorState.EnteringRoom || state == VisitorState.ExitingDoor ||
        state == VisitorState.LeavingGym || state == VisitorState.ApproachingVehicle;
    public bool IsBusy => IsTraveling || state == VisitorState.ApproachingWorkout ||
        state == VisitorState.Squatting || IsPostWorkoutFreeRoam ||
        workoutReleaseStation != null ||
        (fighter != null && fighter.IsOnTreadmill);
    public bool IsPostWorkoutFreeRoam => state == VisitorState.FreeRoaming &&
        Time.time < postWorkoutFreeRoamUntil;
    public bool IsSquatLifecycleActive => pendingStation != null ||
        state == VisitorState.ApproachingWorkout || state == VisitorState.Squatting ||
        workoutReleaseStation != null ||
        (squatController != null && squatController.IsActive);
    public bool IsWorkoutActive => state == VisitorState.Squatting ||
        (squatController != null && squatController.IsActive);
    public bool HasEnteredGym => enteredGym;
    public bool HasLeftGym => leftGym;
    public bool HasCompletedDoorExit => completedDoorExit && !enteredGym && leftGym &&
        state == VisitorState.Dormant;
    public bool HasReachedVehicle => reachedVehicle && state == VisitorState.Dormant;
    public int CompletedVehicleApproaches => completedVehicleApproaches;
    public int DoorwayExitRecoveryCountForVerification => doorwayExitRecoveryCount;
    public bool IsUsingSharedParkingConnector =>
        state == VisitorState.ApproachingVehicle &&
        vehicleExitWaypoints != null && vehicleExitWaypointIndex < 4;
    public bool IsYieldingToVehicle => yieldingToVehicle != null;
    public bool CanDeactivate => HasCompletedDoorExit ||
        (!hasSuccessfulEntry && !enteredGym && leftGym && state == VisitorState.Dormant);
    public bool IsEntryPending => state == VisitorState.EnteringDoor ||
        state == VisitorState.EnteringRoom || state == VisitorState.ApproachingGymFromVehicle;
    public int CompletedWorkoutVersion => completedWorkoutVersion;
    public EnemyFighter Fighter => fighter;
    public static bool IsVehicleApproachReserved => false;
#if UNITY_EDITOR
    public Vector3 TravelTargetForVerification => travelTarget;
    public int VehicleEntryWaypointForVerification => vehicleEntryWaypointIndex;
    public int VehicleRouteDetourCountForVerification => vehicleRouteDetourCount;
    public int VehicleYieldCountForVerification => vehicleYieldCount;
    public string VehicleStageForVerification =>
        $"entryWaypoint={vehicleEntryWaypointIndex},exitWaypoint={vehicleExitWaypointIndex}";

    public void PrepareVehicleYieldVerification()
    {
        CancelVehicleYield();
        state = VisitorState.Dormant;
        enteredGym = false;
        leftGym = true;
        hasSuccessfulEntry = false;
        completedDoorExit = false;
        doorway = GymDoorway.Instance;
    }
#endif

    public void Configure(EnemyFighter owner)
    {
        fighter = owner != null ? owner : GetComponent<EnemyFighter>();
        if (squatController == null)
        {
            squatController = GetComponent<SquatWorkoutController>();
        }
        if (squatController == null)
        {
            squatController = gameObject.AddComponent<SquatWorkoutController>();
        }
    }

    public void MarkInitialInside()
    {
        CancelVehicleYield();
        ReleaseDoorOpenRequest();
        EndWorkoutStationRelease();
        ReleasePendingStationApproach();
        state = VisitorState.FreeRoaming;
        enteredGym = true;
        leftGym = false;
        hasSuccessfulEntry = true;
        completedDoorExit = false;
        reachedVehicle = false;
        doorway = GymDoorway.Instance;
        hasDoorwayClearPoint = false;
        pendingStation = null;
        squatStartPending = false;
        entryRoomClearPointPending = false;
        exitRoomClearPointPending = false;
        doorwayExitRecoveryCount = 0;
        ResetDoorwayExitTracking();
        postWorkoutFreeRoamUntil = 0f;
        roomTravelStalledSeconds = 0f;
        lastRoomTravelDistance = float.PositiveInfinity;
        if (fighter != null)
        {
            fighter.ReleaseVisitorWorkoutPose();
            fighter.RestoreVisitorPoseInterpolation();
            fighter.StopVisitorMovement();
        }
    }

    public void BeginEntry(GymDoorway door, Vector3 destinationInside)
    {
        if (fighter == null || door == null)
        {
            return;
        }

        CancelVehicleYield();
        RestoreRouteVehicleCollision();
        EndWorkoutStationRelease();
        doorway = door;
        hasDoorwayClearPoint = false;
        HoldDoorOpenRequest();
        roomTarget = GetDoorwayRoomStagingTarget(destinationInside);
        travelTarget = doorway.InteriorPoint;
        state = VisitorState.EnteringDoor;
        enteredGym = false;
        leftGym = false;
        hasSuccessfulEntry = false;
        completedDoorExit = false;
        reachedVehicle = false;
        entryRoomClearPointPending = false;
        exitRoomClearPointPending = false;
        doorwayExitRecoveryCount = 0;
        ResetDoorwayExitTracking();
        postWorkoutFreeRoamUntil = 0f;
        roomTravelStalledSeconds = 0f;
        lastRoomTravelDistance = float.PositiveInfinity;
        fighter.StopVisitorMovement();
    }

    public void BeginEntryFromVehicle(
        GymDoorway door, Vector3 destinationInside,
        Vector3 vehicleSpawnPoint, Vector3 parkingAislePoint)
    {
        BeginEntry(door, destinationInside);
        if (fighter == null || doorway == null) return;
        vehicleRouteDetourCount = 0;
        RestoreVisitorVehicleCollisions();
        IgnoreRouteVehicleCollision(vehicleSpawnPoint);

        float y = fighter.transform.position.y;
        // Use the center of the authored connector for both directions. A
        // random side on every visit made the pedestrian route depend on a
        // lateral detour rather than the shortest door-to-parking corridor,
        // and amplified oscillation when another visitor was nearby.
        float laneVariation = GetVehicleRouteVariation(vehicleSpawnPoint.x);
        Vector3 safeTurn = ResolveSafeExteriorLane(
            GymOutdoorBuilder.VisitorParkingTurnPoint, y, laneVariation);
        Vector3 exteriorClear = GetExteriorDoorClearPoint(y);
        safeTurn = KeepLaneOutsideDoorWall(safeTurn, exteriorClear, y);
        vehicleEntrySpawnPoint = new Vector3(
            vehicleSpawnPoint.x, y, vehicleSpawnPoint.z);
        vehicleEntrySpawnPending = true;
        Vector3 aisleApproach = new Vector3(
            parkingAislePoint.x, y, parkingAislePoint.z);
        float aisleExitDirection = Mathf.Sign(
            GymOutdoorBuilder.VisitorParkingEntryPoint.x - aisleApproach.x);
        aisleApproach.x += aisleExitDirection * Mathf.Min(
            5.5f, Mathf.Abs(GymOutdoorBuilder.VisitorParkingEntryPoint.x -
                aisleApproach.x));
        vehicleEntryWaypoints = new[]
        {
            aisleApproach,
            new Vector3(GymOutdoorBuilder.VisitorParkingEntryPoint.x, y,
                GymOutdoorBuilder.VisitorParkingEntryPoint.z),
            safeTurn,
            exteriorClear,
            new Vector3(doorway.ExteriorPoint.x, y, doorway.ExteriorPoint.z)
        };
        vehicleEntryWaypointIndex = FindInitialRouteWaypoint(
            vehicleEntryWaypoints, vehicleEntrySpawnPoint, 2.2f);
        travelTarget = vehicleEntryWaypoints[vehicleEntryWaypointIndex];
        state = VisitorState.ApproachingGymFromVehicle;
    }

    public bool BeginWorkoutApproach(
        GymExerciseStation station,
        int repetitions,
        float repDuration)
    {
        if (fighter == null || station == null || state != VisitorState.FreeRoaming ||
            fighter.IsDead || fighter.IsOnTreadmill)
        {
            return false;
        }

        pendingStation = station;
        squatStartPending = false;
        attemptedWorkoutStations.Clear();
        pendingRepetitions = Mathf.Clamp(repetitions, 6, 12);
        pendingRepDuration = Mathf.Clamp(repDuration, 0.55f, 2.2f);
        if (station.IsSquat)
        {
            if (!station.TryReserveEnemySquatApproach(fighter))
            {
                pendingStation = null;
                return false;
            }
            attemptedWorkoutStations.Add(station);

            // Squat stations are cages/racks. The visitor must physically
            // cross the rack footprint and stop at the authored squat pose,
            // not wait in the aisle in front of it.
            travelTarget = station.EnemyPosition;
        }
        else
        {
            Vector3 approachDirection = Vector3.ProjectOnPlane(
                station.EnemyRotation * Vector3.back, Vector3.up);
            if (approachDirection.sqrMagnitude < 0.01f)
            {
                approachDirection = Vector3.back;
            }
            travelTarget = station.EnemyPosition + approachDirection.normalized * 2.15f;
        }
        travelTarget.y = fighter.transform.position.y;
        workoutApproachStartedAt = Time.time;
        workoutApproachStalledSeconds = 0f;
        lastWorkoutApproachDistance = Vector3.ProjectOnPlane(
            travelTarget - fighter.transform.position, Vector3.up).magnitude;
        state = VisitorState.ApproachingWorkout;
        fighter.StopVisitorMovement();
        return true;
    }

    public void BeginExit(GymDoorway door)
    {
        if (fighter == null || door == null || !IsInsideGym ||
            IsWorkoutActive || IsPostWorkoutFreeRoam)
        {
            return;
        }

        CancelVehicleYield();
        doorway = door;
        hasDoorwayClearPoint = false;
        HoldDoorOpenRequest();
        // Initial visitors can already be inside the gym and therefore never
        // passed through BeginEntryFromVehicle. Their exit still uses the
        // same authored exterior route and normal perimeter collisions.
        pendingStation = null;
        entryRoomClearPointPending = false;
        exitRoomClearPointPending = true;
        travelTarget = GetDoorwayClearPoint();
        doorwayExitRecoveryCount = 0;
        ResetDoorwayExitTracking();
        state = VisitorState.ExitingDoor;
        fighter.StopVisitorMovement();
    }

    public bool BeginVehicleApproach(Vector3 vehiclePoint, float boardingRadius = 0.55f)
    {
        if (fighter == null || !CanDeactivate)
        {
            return false;
        }
        CancelVehicleYield();
        // Pedestrians may take their independently varied exterior paths at
        // the same time. Vehicle serialization remains in GymVisitorVehicle;
        // reserving this entire walk made later visitors freeze by the door.

        finalVehicleTarget = vehiclePoint;
        vehicleBoardingRadius = Mathf.Clamp(boardingRadius, 0.35f, 2.2f);
        AllowVisitorThroughPlayerRoadBlocker();
        RestoreVisitorVehicleCollisions();
        IgnoreRouteVehicleCollision(vehiclePoint);
        finalVehicleTarget.y = fighter.transform.position.y;
        // Arrival and departure must share the same stable connector lane.
        // Keep the route deterministic so a visitor never alternates sides
        // of the parking connector while resolving a temporary blocker.
        float laneVariation = GetVehicleRouteVariation(finalVehicleTarget.x);
        Vector3 safeTurn = ResolveSafeExteriorLane(
            GymOutdoorBuilder.VisitorParkingTurnPoint,
            fighter.transform.position.y, laneVariation);
        vehicleAisleTarget = new Vector3(
            finalVehicleTarget.x,
            fighter.transform.position.y,
            GymOutdoorBuilder.ParkingBounds.center.z);
        Vector3 exteriorClear = doorway != null
            ? GetExteriorDoorClearPoint(fighter.transform.position.y)
            : fighter.transform.position;
        safeTurn = KeepLaneOutsideDoorWall(
            safeTurn, exteriorClear, fighter.transform.position.y);
        vehicleExitWaypoints = new[]
        {
            exteriorClear,
            safeTurn,
            new Vector3(GymOutdoorBuilder.VisitorParkingEntryPoint.x,
                fighter.transform.position.y,
                GymOutdoorBuilder.VisitorParkingEntryPoint.z),
            vehicleAisleTarget,
            finalVehicleTarget
        };
        vehicleExitWaypointIndex = FindInitialRouteWaypoint(
            vehicleExitWaypoints, fighter.transform.position, 2.2f);
        travelTarget = vehicleExitWaypoints[vehicleExitWaypointIndex];
        vehicleTurnPending = true;
        vehicleEntryPending = true;
        vehicleAislePending = true;
        vehicleDetourActive = false;
        vehicleRouteStalledSeconds = 0f;
        lastVehicleRouteDistance = Vector3.ProjectOnPlane(
            travelTarget - fighter.transform.position, Vector3.up).magnitude;
        vehicleRerouteAttempt = 0;
        vehicleRouteDetourCount = 0;
        reachedVehicle = false;
        state = VisitorState.ApproachingVehicle;
        fighter.StopVisitorMovement();
        Debug.Log($"GYMCHAOS_VISITOR_WALK_TO_VEHICLE enemy={fighter.Identity} target={travelTarget}", this);
        return true;
    }

    public bool RequestVehicleYield(
        GymVisitorVehicle vehicle, Vector3 vehicleDirection)
    {
        if (vehicle == null || fighter == null || fighter.IsDead ||
            yieldingToVehicle != null)
        {
            return yieldingToVehicle == vehicle;
        }

        bool isExteriorTravel = state == VisitorState.ApproachingGymFromVehicle ||
            state == VisitorState.ExitingDoor ||
            state == VisitorState.LeavingGym ||
            state == VisitorState.ApproachingVehicle;
        bool isOutsideFreeRoam = state == VisitorState.FreeRoaming &&
            GymOutdoorBuilder.IsPlayerOutsideGym(fighter.transform.position);
        if ((!isExteriorTravel && !isOutsideFreeRoam) || !vehicle.IsDriving)
        {
            return false;
        }

        Vector3 direction = Vector3.ProjectOnPlane(vehicleDirection, Vector3.up);
        if (direction.sqrMagnitude < 0.01f)
        {
            direction = Vector3.ProjectOnPlane(vehicle.transform.forward, Vector3.up);
        }
        if (direction.sqrMagnitude < 0.01f)
        {
            return false;
        }

        direction.Normalize();
        Vector3 right = Vector3.Cross(Vector3.up, direction).normalized;
        Vector3 current = fighter.transform.position;
        Vector3 relative = Vector3.ProjectOnPlane(
            current - vehicle.transform.position, Vector3.up);
        float signedSide = Vector3.Dot(relative, right);
        float preferredSide = Mathf.Abs(signedSide) > 0.2f
            ? Mathf.Sign(signedSide)
            : 1f;

        for (int attempt = 0; attempt < 2; attempt++)
        {
            float side = preferredSide * (attempt == 0 ? 1f : -1f);
            Vector3 candidate = current + right * (side * VehicleYieldSideStep) -
                direction * VehicleYieldBackwardOffset;
            candidate = ClampVehicleYieldPoint(candidate, current.y);
            Vector3 step = Vector3.ProjectOnPlane(candidate - current, Vector3.up);
            if (step.sqrMagnitude < 1f ||
                !fighter.IsVisitorExternalPathClear(
                    step, Mathf.Min(step.magnitude, 1.55f)))
            {
                continue;
            }

            yieldingToVehicle = vehicle;
            vehicleYieldPoint = candidate;
            vehicleYieldStartedAt = Time.time;
            vehicleYieldReachedAt = -1f;
            vehicleYieldCount++;
            Debug.Log(
                $"GYMCHAOS_VISITOR_VEHICLE_YIELD_REQUESTED enemy={fighter.Identity} " +
                $"vehicle={vehicle.name} point={vehicleYieldPoint} " +
                $"attempt={attempt + 1}", this);
            return true;
        }

        return false;
    }

    private Vector3 ClampVehicleYieldPoint(Vector3 point, float y)
    {
        Bounds accessible = GymOutdoorBuilder.AccessibleBounds;
        if (accessible.size.x > 3f && accessible.size.z > 3f)
        {
            const float boundaryPadding = 1.25f;
            point.x = Mathf.Clamp(
                point.x, accessible.min.x + boundaryPadding,
                accessible.max.x - boundaryPadding);
            point.z = Mathf.Clamp(
                point.z, accessible.min.z + boundaryPadding,
                accessible.max.z - boundaryPadding);
        }

        point.y = y;
        return point;
    }

    private void TickVehicleYield()
    {
        GymVisitorVehicle vehicle = yieldingToVehicle;
        if (vehicle == null || fighter == null)
        {
            ClearVehicleYield(false);
            return;
        }

        if (!vehicle.IsDriving || !vehicle.gameObject.activeInHierarchy)
        {
            ClearVehicleYield(true);
            return;
        }

        if (fighter.MoveVisitorAlongExteriorRoute(
                vehicleYieldPoint, VehicleYieldSpeed))
        {
            if (vehicleYieldReachedAt < 0f)
            {
                vehicleYieldReachedAt = Time.time;
            }

            bool vehicleCanPass = vehicle.IsPedestrianClearForYield(
                fighter.transform.position);
            bool holdExpired = Time.time - vehicleYieldReachedAt >=
                VehicleYieldHoldSeconds;
            bool timeout = Time.time - vehicleYieldStartedAt >= VehicleYieldTimeout;
            if ((vehicleCanPass && holdExpired) || timeout)
            {
                ClearVehicleYield(true);
            }
        }
    }

    private void ClearVehicleYield(bool log)
    {
        GymVisitorVehicle vehicle = yieldingToVehicle;
        yieldingToVehicle = null;
        vehicleYieldReachedAt = -1f;
        if (state == VisitorState.ApproachingVehicle)
        {
            ResetVehicleRouteProgress();
        }

        if (fighter != null)
        {
            fighter.StopVisitorMovement();
        }

        if (log && vehicle != null)
        {
            Debug.Log(
                $"GYMCHAOS_VISITOR_VEHICLE_YIELD_RELEASED enemy={fighter?.Identity} " +
                $"vehicle={vehicle.name} point={vehicleYieldPoint}", this);
        }
    }

    private void CancelVehicleYield()
    {
        yieldingToVehicle = null;
        vehicleYieldReachedAt = -1f;
    }

    private Vector3 GetExteriorDoorClearPoint(float y)
    {
        Vector3 exterior = doorway.ExteriorPoint;
        Vector3 outward = Vector3.ProjectOnPlane(
            doorway.ExteriorPoint - doorway.InteriorPoint, Vector3.up);
        if (outward.sqrMagnitude < 0.01f)
        {
            outward = Vector3.right;
        }
        exterior += outward.normalized * DoorwayClearance;
        exterior.y = y;
        return exterior;
    }

    private Vector3 KeepLaneOutsideDoorWall(
        Vector3 lane, Vector3 exteriorClear, float y)
    {
        if (doorway == null)
        {
            lane.y = y;
            return lane;
        }
        Vector3 outward = Vector3.ProjectOnPlane(
            doorway.ExteriorPoint - doorway.InteriorPoint, Vector3.up).normalized;
        if (outward.sqrMagnitude < 0.01f) outward = Vector3.right;
        float missingClearance = Vector3.Dot(exteriorClear - lane, outward);
        if (missingClearance > 0f) lane += outward * missingClearance;
        lane.y = y;
        return lane;
    }

    private static float GetVehicleRouteVariation(float vehicleX)
    {
        Bounds parking = GymOutdoorBuilder.ParkingBounds;
        if (parking.size.x <= 1f) return 0.5f;
        float bayPosition = Mathf.InverseLerp(
            parking.min.x, parking.max.x, vehicleX);
        return Mathf.Lerp(0.24f, 0.76f, bayPosition);
    }

    private static int FindInitialRouteWaypoint(
        Vector3[] route, Vector3 start, float handoffRadius)
    {
        int index = 0;
        while (index + 1 < route.Length &&
               Vector3.ProjectOnPlane(route[index] - start, Vector3.up)
                   .sqrMagnitude <= handoffRadius * handoffRadius)
        {
            index++;
        }
        return index;
    }

    public void ReleaseVehicleApproachReservation()
    {
        // Kept as a compatibility hook for director call sites. Pedestrian
        // approaches no longer own a global lock.
    }

    private void OnDestroy()
    {
        RestoreRouteVehicleCollision();
        ReleaseVehicleApproachReservation();
    }

    private void AllowVisitorThroughPlayerRoadBlocker()
    {
        GameObject blocker = GameObject.Find("Player Road Access Blocker");
        Collider roadCollider = blocker != null ? blocker.GetComponent<Collider>() : null;
        if (roadCollider == null || fighter == null) return;
        Collider[] visitorColliders = fighter.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < visitorColliders.Length; i++)
        {
            if (visitorColliders[i] != null)
            {
                Physics.IgnoreCollision(visitorColliders[i], roadCollider, true);
            }
        }
    }

    private void RestoreVisitorVehicleCollisions()
    {
        if (fighter == null) return;
        Collider[] visitorColliders = fighter.GetComponentsInChildren<Collider>(true);
        GymVisitorVehicle[] vehicles = FindObjectsByType<GymVisitorVehicle>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int v = 0; v < vehicles.Length; v++)
        {
            if (vehicles[v] == null) continue;
            Collider[] vehicleColliders =
                vehicles[v].GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < visitorColliders.Length; i++)
            {
                if (visitorColliders[i] == null) continue;
                for (int c = 0; c < vehicleColliders.Length; c++)
                {
                    if (vehicleColliders[c] != null)
                        Physics.IgnoreCollision(
                            visitorColliders[i], vehicleColliders[c], false);
                }
            }
        }
    }

    private void IgnoreRouteVehicleCollision(Vector3 referencePoint)
    {
        RestoreRouteVehicleCollision();
        GymVisitorVehicle[] vehicles = FindObjectsByType<GymVisitorVehicle>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        GymVisitorVehicle nearest = null;
        float nearestDistance = float.PositiveInfinity;
        Vector3 planarReference = Vector3.ProjectOnPlane(referencePoint, Vector3.up);
        for (int i = 0; i < vehicles.Length; i++)
        {
            GymVisitorVehicle candidate = vehicles[i];
            if (candidate == null)
            {
                continue;
            }

            Vector3 candidatePlanar = Vector3.ProjectOnPlane(
                candidate.transform.position, Vector3.up);
            float distance = (candidatePlanar - planarReference).sqrMagnitude;
            if (distance < nearestDistance)
            {
                nearestDistance = distance;
                nearest = candidate;
            }
        }

        if (nearest == null)
        {
            return;
        }

        routeVehicleWithIgnoredCollision = nearest;
        SetRouteVehicleCollisionIgnored(true);
    }

    private void RestoreRouteVehicleCollision()
    {
        if (routeVehicleWithIgnoredCollision == null)
        {
            return;
        }

        SetRouteVehicleCollisionIgnored(false);
        routeVehicleWithIgnoredCollision = null;
    }

    private void SetRouteVehicleCollisionIgnored(bool ignored)
    {
        if (fighter == null || routeVehicleWithIgnoredCollision == null)
        {
            return;
        }

        Collider[] visitorColliders = fighter.GetComponentsInChildren<Collider>(true);
        Collider[] vehicleColliders = routeVehicleWithIgnoredCollision
            .GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < visitorColliders.Length; i++)
        {
            Collider visitorCollider = visitorColliders[i];
            if (visitorCollider == null)
            {
                continue;
            }

            for (int c = 0; c < vehicleColliders.Length; c++)
            {
                Collider vehicleCollider = vehicleColliders[c];
                if (vehicleCollider != null)
                {
                    Physics.IgnoreCollision(visitorCollider, vehicleCollider, ignored);
                }
            }
        }
    }

    public void AbortEntryAndReturnThroughDoor(GymDoorway door)
    {
        if (fighter == null || door == null || !IsEntryPending)
        {
            return;
        }

        RestoreRouteVehicleCollision();
        doorway = door;
        hasDoorwayClearPoint = false;
        HoldDoorOpenRequest();
        pendingStation = null;
        enteredGym = false;
        leftGym = false;
        hasSuccessfulEntry = false;
        completedDoorExit = false;
        entryRoomClearPointPending = false;
        // If the visitor has not reached the interior side yet, it is still
        // outside the gym. Finish at the authored exterior handoff instead of
        // asking it to cross the door and immediately cross back again.
        if (state == VisitorState.EnteringDoor)
        {
            exitRoomClearPointPending = false;
            travelTarget = doorway.ExteriorPoint;
            state = VisitorState.LeavingGym;
            ResetDoorwayExitTracking();
        }
        else
        {
            // A stalled visitor already on the room side must leave through
            // the same clear waypoint used by normal exits. It must never
            // disappear at the doorway because a navigation timeout fired.
            exitRoomClearPointPending = true;
            travelTarget = GetDoorwayClearPoint();
            state = VisitorState.ExitingDoor;
            ResetDoorwayExitTracking();
        }
        fighter.StopVisitorMovement();
        Debug.LogWarning(
            $"GYMCHAOS_VISITOR_ENTRY_ABORT_RETURNING enemy={fighter.Identity}",
            this);
    }

    public void CancelPendingVisit()
    {
        if (!IsEntryPending)
        {
            return;
        }

        CancelVehicleYield();
        RestoreRouteVehicleCollision();
        ReleaseDoorOpenRequest();
        state = VisitorState.Dormant;
        enteredGym = false;
        leftGym = true;
        hasSuccessfulEntry = false;
        completedDoorExit = false;
        pendingStation = null;
        entryRoomClearPointPending = false;
        exitRoomClearPointPending = false;
        ResetDoorwayExitTracking();
        postWorkoutFreeRoamUntil = 0f;
        if (fighter != null)
        {
            fighter.StopVisitorMovement();
        }
    }

    public void MarkDormant()
    {
        if (hasSuccessfulEntry && !completedDoorExit)
        {
            Debug.LogError(
                $"GYMCHAOS_VISITOR_DORMANT_REJECTED enemy={fighter?.Identity} " +
                "reason=door_exit_not_completed",
                this);
            return;
        }

        CancelVehicleYield();
        RestoreRouteVehicleCollision();
        ReleaseDoorOpenRequest();
        EndWorkoutStationRelease();
        ReleasePendingStationApproach();
        if (squatController != null && squatController.IsActive)
        {
            squatController.Cancel();
        }
        state = VisitorState.Dormant;
        enteredGym = false;
        leftGym = true;
        completedDoorExit = completedDoorExit || hasSuccessfulEntry;
        pendingStation = null;
        entryRoomClearPointPending = false;
        exitRoomClearPointPending = false;
        ResetDoorwayExitTracking();
        postWorkoutFreeRoamUntil = 0f;
        if (fighter != null)
        {
            fighter.ReleaseVisitorWorkoutPose();
            fighter.RestoreVisitorPoseInterpolation();
            fighter.StopVisitorMovement();
        }
    }

    public void CancelForCombat()
    {
        CancelVehicleYield();
        RestoreRouteVehicleCollision();
        ReleaseDoorOpenRequest();
        squatStartPending = false;
        if (squatController != null && squatController.IsActive)
        {
            squatController.Cancel();
        }

        EndWorkoutStationRelease();

        if (fighter != null)
        {
            fighter.ReleaseVisitorWorkoutPose();
            fighter.RestoreVisitorPoseInterpolation();
            fighter.StopVisitorMovement();
        }

        ReleasePendingStationApproach();
        state = enteredGym ? VisitorState.FreeRoaming : VisitorState.Dormant;
        pendingStation = null;
        entryRoomClearPointPending = false;
        exitRoomClearPointPending = false;
        ResetDoorwayExitTracking();
        postWorkoutFreeRoamUntil = 0f;
    }

    public bool TickPhysics(EnemyFighter owner)
    {
        if (fighter == null)
        {
            fighter = owner;
        }

        if (fighter == null || fighter.IsDead)
        {
            return false;
        }

        if (workoutReleaseStation != null)
        {
            TickWorkoutStationRelease();
            return true;
        }

        if ((state == VisitorState.ExitingDoor ||
             state == VisitorState.LeavingGym ||
             state == VisitorState.ApproachingVehicle) &&
            !fighter.TickVisitorGroundingForMovement())
        {
            return true;
        }

        if (yieldingToVehicle != null)
        {
            TickVehicleYield();
            return true;
        }

        switch (state)
        {
            case VisitorState.ApproachingGymFromVehicle:
                if (vehicleEntrySpawnPending)
                {
                    vehicleEntrySpawnPending = false;
                    fighter.SetVisitorSpawnPose(
                        vehicleEntrySpawnPoint, fighter.transform.rotation, true);
                    Physics.SyncTransforms();
                    return true;
                }
                Vector3? entryLookAhead = vehicleEntryWaypoints != null &&
                    vehicleEntryWaypointIndex + 1 < vehicleEntryWaypoints.Length
                    ? vehicleEntryWaypoints[vehicleEntryWaypointIndex + 1]
                    : (Vector3?)null;
                bool isRoundedExteriorCorner = vehicleEntryWaypointIndex >= 1 &&
                    vehicleEntryWaypointIndex <= 2;
                bool isExteriorDoorPoint = vehicleEntryWaypoints != null &&
                    vehicleEntryWaypointIndex == vehicleEntryWaypoints.Length - 1;
                // The first handoff is the center of the parking aisle. A
                // dismounted capsule can be shifted sideways by the parked
                // vehicle's footprint while still being fully on that broad
                // connector; waiting for the 1.15 m generic radius there can
                // leave the agent circling forever around an already clear
                // waypoint. Keep the door and corner waypoints precise.
                bool isParkingAislePoint = vehicleEntryWaypointIndex == 0;
                bool requiresWallClearanceTurn = vehicleEntryWaypointIndex == 3;
                if (isParkingAislePoint) entryLookAhead = null;
                float entryCompletionRadius = isParkingAislePoint
                    ? 2.2f
                    : isRoundedExteriorCorner
                    ? 2.1f
                    : requiresWallClearanceTurn ? 0.55f
                    : isExteriorDoorPoint ? 0.45f : -1f;
                if (MoveAlongAuthoredExteriorRoute(
                        travelTarget, 2.35f, entryLookAhead,
                        entryCompletionRadius))
                {
                    vehicleEntryWaypointIndex++;
                    if (vehicleEntryWaypoints != null &&
                        vehicleEntryWaypointIndex < vehicleEntryWaypoints.Length)
                    {
                        travelTarget = vehicleEntryWaypoints[vehicleEntryWaypointIndex];
                    }
                    else
                    {
                        RestoreRouteVehicleCollision();
                        fighter.RestoreVisitorPoseInterpolation();
                        state = VisitorState.EnteringDoor;
                        travelTarget = doorway.InteriorPoint;
                    }
                }
                return true;

            case VisitorState.EnteringDoor:
                if (fighter.MoveVisitorTo(travelTarget, 2.2f, true))
                {
                    state = VisitorState.EnteringRoom;
                    Vector3 clearPoint = GetDoorwayClearPoint();
                    entryRoomClearPointPending =
                        Vector3.ProjectOnPlane(
                            clearPoint - fighter.transform.position, Vector3.up).sqrMagnitude > 0.08f;
                    travelTarget = entryRoomClearPointPending
                        ? clearPoint
                        : roomTarget;
                    roomTravelStalledSeconds = 0f;
                    lastRoomTravelDistance = Vector3.ProjectOnPlane(
                        travelTarget - fighter.transform.position, Vector3.up).magnitude;
                    Debug.Log(
                        $"GYMCHAOS_VISITOR_DOOR_CROSSED enemy={fighter.Identity} " +
                        $"roomTarget={travelTarget}",
                        this);
                }
                return true;

            case VisitorState.EnteringRoom:
                float roomDistance = Vector3.ProjectOnPlane(
                    travelTarget - fighter.transform.position, Vector3.up).magnitude;
                if (fighter.MoveVisitorTo(travelTarget, 2.15f, false))
                {
                    if (entryRoomClearPointPending)
                    {
                        // The reception desk sits immediately behind the
                        // doorway. First clear its footprint laterally, then
                        // continue to the authored inside staging point.
                        entryRoomClearPointPending = false;
                        travelTarget = roomTarget;
                        roomTravelStalledSeconds = 0f;
                        lastRoomTravelDistance = Vector3.ProjectOnPlane(
                            travelTarget - fighter.transform.position, Vector3.up).magnitude;
                        return true;
                    }

                    ReleaseDoorOpenRequest();
                    state = VisitorState.FreeRoaming;
                    enteredGym = true;
                    leftGym = false;
                    hasSuccessfulEntry = true;
                    completedDoorExit = false;
                    roomTravelStalledSeconds = 0f;
                    lastRoomTravelDistance = 0f;
                    fighter.ResumeVisitorRoaming();
                    Debug.Log($"GYMCHAOS_VISITOR_ENTERED enemy={fighter.Identity}", this);
                }
                else if (roomDistance < lastRoomTravelDistance - 0.025f)
                {
                    lastRoomTravelDistance = roomDistance;
                    roomTravelStalledSeconds = 0f;
                }
                else
                {
                    roomTravelStalledSeconds += Time.fixedDeltaTime;
                    if (roomTravelStalledSeconds > 2.2f && doorway != null)
                    {
                        Vector3 inward = Vector3.ProjectOnPlane(
                            doorway.InteriorPoint - doorway.ExteriorPoint, Vector3.up);
                        if (inward.sqrMagnitude > 0.01f)
                        {
                            Vector3 fallbackTarget = entryRoomClearPointPending
                                ? GetDoorwayClearPoint()
                                : GetDoorwayClearPoint() + inward.normalized * 3.6f;
                            travelTarget = fallbackTarget;
                            travelTarget.y = fighter.transform.position.y;
                            lastRoomTravelDistance = Vector3.ProjectOnPlane(
                                travelTarget - fighter.transform.position, Vector3.up).magnitude;
                            roomTravelStalledSeconds = 0f;
                            Debug.LogWarning(
                                $"GYMCHAOS_VISITOR_ENTRY_ROUTE_FALLBACK enemy={fighter.Identity} " +
                                $"target={travelTarget}",
                                this);
                        }
                    }
                }
                return true;

            case VisitorState.ApproachingWorkout:
                if (squatStartPending)
                {
                    // The final pose is prepared in FixedUpdate; the actual
                    // bar attach is deferred to this component's LateUpdate
                    // so it lands after retarget animation and before render.
                    return true;
                }

                if (pendingStation == null ||
                    !pendingStation.IsAvailableForEnemy(fighter) ||
                    pendingStation.EnemyOccupant != fighter)
                {
                    CancelWorkoutApproach("station_unavailable");
                    return true;
                }

                float approachDistance = Vector3.ProjectOnPlane(
                    travelTarget - fighter.transform.position, Vector3.up).magnitude;
                if (fighter.MoveVisitorTo(
                    travelTarget,
                    1.9f,
                    false,
                    pendingStation != null && pendingStation.IsSquat ? pendingStation : null))
                {
                    if (pendingStation != null && pendingStation.IsSquat)
                    {
                        // Lock the visible retarget pose before its next
                        // LateUpdate. This prevents a final idle sample from
                        // appearing between the authored arrival and the
                        // attached-bar squat pose.
                        fighter.PrepareVisitorWorkoutPose();
                    }

                    // Prepare the visitor root in this physics callback. The
                    // actual bar attach and squat begin happen in LateUpdate,
                    // after retarget animation has sampled and immediately
                    // before the squat pose is rendered. The previous
                    // two-step settle window left the rack bar visible for
                    // one rendered frame before it was reparented to the
                    // traps, which appeared as a start microstutter.
                    Vector3 authoredPosition = pendingStation.EnemyPosition;
                    bool needsAuthoredPoseSnap =
                        Vector3.Distance(fighter.transform.position, authoredPosition) > 0.012f ||
                        Quaternion.Angle(
                            fighter.transform.rotation,
                            pendingStation.EnemyRotation) > 0.5f;
                    if (needsAuthoredPoseSnap)
                    {
                        fighter.SetVisitorSpawnPose(
                            authoredPosition,
                            pendingStation.EnemyRotation,
                            keepInterpolationDisabled: pendingStation.IsSquat);
                    }
                    else
                    {
                        fighter.StopVisitorMovement();
                    }
                    squatStartPending = true;
                }
                else if (approachDistance < lastWorkoutApproachDistance - 0.025f)
                {
                    lastWorkoutApproachDistance = approachDistance;
                    workoutApproachStalledSeconds = 0f;
                }
                else
                {
                    workoutApproachStalledSeconds += Time.fixedDeltaTime;
                    if (workoutApproachStalledSeconds > WorkoutApproachStallTimeout ||
                        Time.time - workoutApproachStartedAt > WorkoutApproachTimeout)
                    {
                        if (!TrySwitchToAlternativeSquatStation())
                        {
                            CancelWorkoutApproach("approach_stalled");
                        }
                    }
                }
                return true;

            case VisitorState.Squatting:
                fighter.StopVisitorMovement();
                return true;

            case VisitorState.ExitingDoor:
                if (fighter.MoveVisitorTo(travelTarget, 2.2f, false))
                {
                    if (exitRoomClearPointPending)
                    {
                        // Approach the door from the clear side of the
                        // reception desk before taking the straight doorway
                        // segment. This keeps the whole exit physical while
                        // avoiding the desk footprint.
                        exitRoomClearPointPending = false;
                        travelTarget = doorway != null
                            ? doorway.InteriorPoint
                            : travelTarget;
                        ResetDoorwayExitTracking();
                        return true;
                    }

                    state = VisitorState.LeavingGym;
                    travelTarget = doorway != null ? doorway.ExteriorPoint : travelTarget;
                    ResetDoorwayExitTracking();
                }
                else
                {
                    TryRecoverStalledDoorExit();
                }
                return true;

            case VisitorState.LeavingGym:
                // Crossing the narrow frame uses the same precise physical
                // steering as entry. Parking steering retains its previous
                // heading and wide turning radius, which can drive a visitor
                // into a jamb after the lateral approach beside reception.
                if (fighter.MoveVisitorTo(travelTarget, 2.2f, true))
                {
                    ReleaseDoorOpenRequest();
                    state = VisitorState.Dormant;
                    enteredGym = false;
                    leftGym = true;
                    completedDoorExit = true;
                    pendingStation = null;
                    fighter.StopVisitorMovement();
                    Debug.Log($"GYMCHAOS_VISITOR_EXITED enemy={fighter.Identity}", this);
                }
                else
                {
                    TryRecoverStalledDoorExit();
                }
                return true;

            case VisitorState.ApproachingVehicle:
                if (vehicleExitWaypoints == null ||
                    vehicleExitWaypointIndex >= vehicleExitWaypoints.Length)
                {
                    state = VisitorState.Dormant;
                    reachedVehicle = true;
                    completedVehicleApproaches++;
                    fighter.StopVisitorMovement();
                    Debug.Log($"GYMCHAOS_VISITOR_REACHED_VEHICLE enemy={fighter.Identity}", this);
                    return true;
                }
                Vector3? exitLookAhead = vehicleDetourActive
                    ? vehicleDetourResumeTarget
                    : vehicleExitWaypoints != null &&
                        vehicleExitWaypointIndex + 1 < vehicleExitWaypoints.Length
                        ? vehicleExitWaypoints[vehicleExitWaypointIndex + 1]
                        : (Vector3?)null;
                bool isFinalVehicleWaypoint = !vehicleDetourActive &&
                    vehicleExitWaypoints != null &&
                    vehicleExitWaypointIndex == vehicleExitWaypoints.Length - 1;
                bool isExteriorClearWaypoint = !vehicleDetourActive &&
                    vehicleExitWaypointIndex <= 1;
                float exitCompletionRadius = isFinalVehicleWaypoint
                    ? vehicleBoardingRadius
                    : 2.1f;
                if (isExteriorClearWaypoint)
                {
                    exitCompletionRadius = 6.5f;
                }
                if (fighter.MoveVisitorAlongExteriorRoute(
                        travelTarget, 2.35f, exitLookAhead,
                        exitCompletionRadius))
                {
                    if (vehicleDetourActive)
                    {
                        vehicleDetourActive = false;
                        travelTarget = vehicleDetourResumeTarget;
                        ResetVehicleRouteProgress();
                        return true;
                    }
                    vehicleExitWaypointIndex++;
                    if (vehicleExitWaypoints != null &&
                        vehicleExitWaypointIndex < vehicleExitWaypoints.Length)
                    {
                        travelTarget = vehicleExitWaypoints[vehicleExitWaypointIndex];
                        vehicleTurnPending = vehicleExitWaypointIndex < 3;
                        vehicleEntryPending = vehicleExitWaypointIndex < 4;
                        vehicleAislePending = vehicleExitWaypointIndex < 5;
                        ResetVehicleRouteProgress();
                        return true;
                    }
                    vehicleTurnPending = false;
                    vehicleEntryPending = false;
                    vehicleAislePending = false;
                    RestoreRouteVehicleCollision();
                    state = VisitorState.Dormant;
                    reachedVehicle = true;
                    completedVehicleApproaches++;
                    fighter.StopVisitorMovement();
                    Debug.Log($"GYMCHAOS_VISITOR_REACHED_VEHICLE enemy={fighter.Identity}", this);
                }
                else
                {
                    TryRerouteStalledVehicleApproach();
                }
                return true;

            case VisitorState.Dormant:
                // Keep a completed outbound visitor stationary for the tiny
                // handoff window before the director disables this same
                // unique enemy instance. Normal roaming must not pull it
                // back through the room on the next physics tick.
                if (leftGym && !enteredGym)
                {
                    fighter.StopVisitorMovement();
                    return true;
                }
                return false;

            default:
                // Free roaming and Dormant are intentionally handled by the
                // existing EnemyFighter state machine or by the director.
                return false;
        }
    }

    private bool MoveAlongAuthoredExteriorRoute(
        Vector3 target, float speed, Vector3? nextWaypoint = null,
        float requestedCompletionRadius = -1f)
    {
        // Preserve authored exterior waypoints, but move through them with
        // normal Rigidbody locomotion. Per-frame pose snaps zeroed velocity,
        // disabled interpolation and left visible character in Idle.
        return fighter.MoveVisitorAlongExteriorRoute(
            target, speed, nextWaypoint, requestedCompletionRadius);
    }

    private void ResetVehicleRouteProgress()
    {
        vehicleRouteStalledSeconds = 0f;
        lastVehicleRouteDistance = fighter != null
            ? Vector3.ProjectOnPlane(
                travelTarget - fighter.transform.position, Vector3.up).magnitude
            : float.PositiveInfinity;
    }

    private void TryRerouteStalledVehicleApproach()
    {
        if (fighter == null)
        {
            return;
        }

        float distance = Vector3.ProjectOnPlane(
            travelTarget - fighter.transform.position, Vector3.up).magnitude;
        if (distance < lastVehicleRouteDistance - 0.06f)
        {
            lastVehicleRouteDistance = distance;
            vehicleRouteStalledSeconds = 0f;
            return;
        }

        vehicleRouteStalledSeconds += Time.fixedDeltaTime;
        if (vehicleRouteStalledSeconds < VehicleRouteStallTimeout)
        {
            return;
        }

        Vector3 resumeTarget = vehicleDetourActive
            ? vehicleDetourResumeTarget
            : travelTarget;
        Vector3 towardTarget = Vector3.ProjectOnPlane(
            resumeTarget - fighter.transform.position, Vector3.up);
        if (towardTarget.sqrMagnitude < 0.01f)
        {
            travelTarget = resumeTarget;
            vehicleDetourActive = false;
            ResetVehicleRouteProgress();
            return;
        }

        towardTarget.Normalize();
        Vector3 lateral = Vector3.Cross(Vector3.up, towardTarget).normalized;
        float side = (vehicleRerouteAttempt & 1) == 0 ? 1f : -1f;
        float sidePadding = 0.7f + Mathf.Min(vehicleRerouteAttempt, 3) * 0.18f;
        Vector3 detour = fighter.transform.position + towardTarget * 2.7f +
            lateral * (side * sidePadding);
        Bounds accessible = GymOutdoorBuilder.AccessibleBounds;
        if (accessible.size.x > 3f && accessible.size.z > 3f)
        {
            const float wallPadding = 1.25f;
            detour.x = Mathf.Clamp(
                detour.x, accessible.min.x + wallPadding,
                accessible.max.x - wallPadding);
            detour.z = Mathf.Clamp(
                detour.z, accessible.min.z + wallPadding,
                accessible.max.z - wallPadding);
        }
        detour.y = fighter.transform.position.y;

        vehicleDetourResumeTarget = resumeTarget;
        vehicleDetourActive = true;
        vehicleRerouteAttempt++;
        vehicleRouteDetourCount++;
        travelTarget = detour;
        ResetVehicleRouteProgress();
        Debug.LogWarning(
            $"GYMCHAOS_VISITOR_VEHICLE_ROUTE_REROUTE enemy={fighter.Identity} " +
            $"attempt={vehicleRerouteAttempt} detour={detour} resume={resumeTarget} " +
            $"blocker={fighter.LastVisitorRouteBlocker}",
            this);
    }

    private static Vector3 ResolveSafeExteriorLane(
        Vector3 authoredPoint, float y, float routeVariation = 0.5f)
    {
        float safeX = authoredPoint.x;
        GameObject northWall = GameObject.Find("North Wall Lower");
        Collider wallCollider = northWall != null
            ? northWall.GetComponent<Collider>()
            : null;
        Bounds accessible = GymOutdoorBuilder.AccessibleBounds;
        if (wallCollider != null && accessible.size.x > 2f)
        {
            float minLaneX = wallCollider.bounds.max.x + 1.15f;
            float maxLaneX = accessible.max.x - 1.1f;
            safeX = maxLaneX > minLaneX
                ? Mathf.Lerp(minLaneX, maxLaneX, Mathf.Clamp01(routeVariation))
                : Mathf.Clamp(authoredPoint.x, maxLaneX, minLaneX);
        }
        else
        {
            if (wallCollider != null)
                safeX = Mathf.Max(safeX, wallCollider.bounds.max.x + 1.15f);
            if (accessible.size.x > 2f)
                safeX = Mathf.Min(safeX, accessible.max.x - 1.1f);
        }
        return new Vector3(safeX, y, authoredPoint.z);
    }

    private void Update()
    {
        if (state == VisitorState.Squatting &&
            squatController != null && squatController.IsComplete)
        {
            GymExerciseStation releasedStation = pendingStation;
            squatController.ConsumeCompletion();
            pendingStation = null;
            attemptedWorkoutStations.Clear();
            state = VisitorState.FreeRoaming;
            postWorkoutFreeRoamUntil = Time.time + 2.25f;
            if (!BeginWorkoutStationRelease(releasedStation))
            {
                fighter?.ResumeVisitorRoaming();
            }
            completedWorkoutVersion++;
            Debug.Log(
                $"GYMCHAOS_SQUAT_FREE_ROAM enemy={fighter?.Identity} " +
                $"releaseUntil={postWorkoutFreeRoamUntil:0.00}",
                this);
        }
    }

    private void LateUpdate()
    {
        if (!squatStartPending)
        {
            return;
        }

        squatStartPending = false;
        if (fighter == null || fighter.IsDead ||
            state != VisitorState.ApproachingWorkout || pendingStation == null ||
            !pendingStation.IsAvailableForEnemy(fighter) ||
            pendingStation.EnemyOccupant != fighter)
        {
            fighter?.ReleaseVisitorWorkoutPose();
            fighter?.RestoreVisitorPoseInterpolation();
            CancelWorkoutApproach("workout_begin_invalidated");
            return;
        }

        if (squatController == null || !squatController.Begin(
            pendingStation, fighter, pendingRepetitions, pendingRepDuration))
        {
            fighter.ReleaseVisitorWorkoutPose();
            fighter.RestoreVisitorPoseInterpolation();
            if (!TrySwitchToAlternativeSquatStation())
            {
                CancelWorkoutApproach("workout_begin_failed");
            }
            return;
        }

        state = VisitorState.Squatting;
        ResetWorkoutApproachTracking();
    }

    private bool BeginWorkoutStationRelease(GymExerciseStation station)
    {
        if (fighter == null || station == null ||
            !station.BeginEnemySquatRelease(fighter))
        {
            return false;
        }

        Vector3 releaseDirection = Vector3.ProjectOnPlane(
            station.EnemyRotation * Vector3.back, Vector3.up);
        if (releaseDirection.sqrMagnitude < 0.001f)
        {
            releaseDirection = Vector3.back;
        }

        workoutReleaseStation = station;
        workoutReleaseTarget = station.EnemyPosition +
            releaseDirection.normalized * WorkoutReleaseDistance;
        workoutReleaseTarget.y = fighter.transform.position.y;
        workoutReleaseStartedAt = Time.time;
        workoutReleaseStalledSeconds = 0f;
        lastWorkoutReleaseDistance = Vector3.ProjectOnPlane(
            workoutReleaseTarget - fighter.transform.position, Vector3.up).magnitude;
        Debug.Log(
            $"GYMCHAOS_SQUAT_RELEASE_WALK_STARTED enemy={fighter.Identity} " +
            $"station={station.EquipmentName} target={workoutReleaseTarget}",
            this);
        return true;
    }

    private void TickWorkoutStationRelease()
    {
        GymExerciseStation station = workoutReleaseStation;
        float releaseDistance = Vector3.ProjectOnPlane(
            workoutReleaseTarget - fighter.transform.position, Vector3.up).magnitude;
        if (fighter.MoveVisitorTo(workoutReleaseTarget, 2.1f, false, station))
        {
            EndWorkoutStationRelease();
            fighter.ResumeVisitorRoaming();
            Debug.Log(
                $"GYMCHAOS_SQUAT_RELEASE_WALK_COMPLETE enemy={fighter.Identity} " +
                $"station={station?.EquipmentName} distance={releaseDistance:0.00}",
                this);
            return;
        }

        if (releaseDistance < lastWorkoutReleaseDistance - 0.025f)
        {
            lastWorkoutReleaseDistance = releaseDistance;
            workoutReleaseStalledSeconds = 0f;
        }
        else
        {
            workoutReleaseStalledSeconds += Time.fixedDeltaTime;
        }

        if (workoutReleaseStalledSeconds > WorkoutReleaseStallTimeout ||
            Time.time - workoutReleaseStartedAt > WorkoutReleaseTimeout)
        {
            EndWorkoutStationRelease();
            fighter.ResumeVisitorRoaming();
            Debug.LogWarning(
                $"GYMCHAOS_SQUAT_RELEASE_WALK_FALLBACK enemy={fighter.Identity} " +
                $"station={station?.EquipmentName} distance={releaseDistance:0.00}",
                this);
        }
    }

    private void EndWorkoutStationRelease()
    {
        if (workoutReleaseStation != null && fighter != null)
        {
            workoutReleaseStation.EndEnemySquatRelease(fighter);
        }

        workoutReleaseStation = null;
        workoutReleaseTarget = Vector3.zero;
        workoutReleaseStartedAt = 0f;
        workoutReleaseStalledSeconds = 0f;
        lastWorkoutReleaseDistance = float.PositiveInfinity;
    }

    private void ReleasePendingStationApproach()
    {
        if (pendingStation != null && pendingStation.IsSquat && fighter != null)
        {
            pendingStation.CancelEnemySquatApproach(fighter);
        }
    }

    private void ResetWorkoutApproachTracking()
    {
        workoutApproachStartedAt = 0f;
        workoutApproachStalledSeconds = 0f;
        lastWorkoutApproachDistance = float.PositiveInfinity;
        squatStartPending = false;
    }

    private bool TrySwitchToAlternativeSquatStation()
    {
        if (fighter == null || pendingStation == null || !pendingStation.IsSquat)
        {
            return false;
        }

        GymExerciseStation previousStation = pendingStation;
        previousStation.CancelEnemySquatApproach(fighter);

        while (true)
        {
            GymExerciseStation alternative = GymExerciseStation.FindClosestSquat(
                fighter.transform.position, 60f, attemptedWorkoutStations);
            if (alternative == null)
            {
                return false;
            }

            attemptedWorkoutStations.Add(alternative);
            if (!alternative.TryReserveEnemySquatApproach(fighter))
            {
                continue;
            }

            pendingStation = alternative;
            travelTarget = alternative.EnemyPosition;
            travelTarget.y = fighter.transform.position.y;
            workoutApproachStartedAt = Time.time;
            workoutApproachStalledSeconds = 0f;
            lastWorkoutApproachDistance = Vector3.ProjectOnPlane(
                travelTarget - fighter.transform.position, Vector3.up).magnitude;
            Debug.LogWarning(
                $"GYMCHAOS_SQUAT_STATION_FALLBACK enemy={fighter.Identity} " +
                $"from={previousStation.EquipmentName} to={alternative.EquipmentName}",
                this);
            return true;
        }
    }

    private void CancelWorkoutApproach(string reason)
    {
        GymExerciseStation canceledStation = pendingStation;
        ReleasePendingStationApproach();
        fighter?.ReleaseVisitorWorkoutPose();
        fighter?.RestoreVisitorPoseInterpolation();
        pendingStation = null;
        attemptedWorkoutStations.Clear();
        state = VisitorState.FreeRoaming;
        postWorkoutFreeRoamUntil = Time.time + FailedWorkoutFreeRoamSeconds;
        ResetWorkoutApproachTracking();
        fighter?.ResumeVisitorRoaming();
        Debug.LogWarning(
            $"GYMCHAOS_SQUAT_APPROACH_CANCELLED enemy={fighter?.Identity} " +
            $"station={canceledStation?.EquipmentName} reason={reason} " +
            $"freeRoamUntil={postWorkoutFreeRoamUntil:0.00}",
            this);
    }

    private Vector3 GetDoorwayRoomStagingTarget(Vector3 requestedTarget)
    {
        if (doorway == null)
        {
            return requestedTarget;
        }

        Vector3 inward = Vector3.ProjectOnPlane(
            doorway.InteriorPoint - doorway.ExteriorPoint, Vector3.up);
        if (inward.sqrMagnitude < 0.01f)
        {
            return requestedTarget;
        }

        // Confirm entry through a short authored segment inside the room. The
        // door is aligned with reception, so the direct line can pass through
        // the desk. Move to a clear lateral point first, then continue inward
        // on the desk-free side of the doorway.
        Vector3 stagingTarget = GetDoorwayClearPoint() + inward.normalized * 3.6f;
        stagingTarget.y = requestedTarget.y;
        return stagingTarget;
    }

    private Vector3 GetDoorwayClearPoint()
    {
        if (doorway == null)
        {
            return fighter != null ? fighter.transform.position : transform.position;
        }

        if (hasDoorwayClearPoint)
        {
            return doorwayClearPoint;
        }

        Vector3 inside = doorway.InteriorPoint;
        Vector3 inward = Vector3.ProjectOnPlane(
            doorway.InteriorPoint - doorway.ExteriorPoint, Vector3.up);
        if (inward.sqrMagnitude < 0.01f)
        {
            doorwayClearPoint = inside;
            hasDoorwayClearPoint = true;
            return inside;
        }

        inward.Normalize();
        Vector3 lateral = Vector3.Cross(Vector3.up, inward).normalized;
        if (lateral.sqrMagnitude < 0.01f)
        {
            lateral = Vector3.forward;
        }

        Bounds deskBounds;
        bool hasDeskBounds = TryGetReceptionDeskBounds(out deskBounds);
        float deskLateralOffset = hasDeskBounds
            ? Vector3.Dot(deskBounds.center - inside, lateral)
            : 0f;
        float deskLateralExtent = hasDeskBounds
            ? Mathf.Abs(lateral.x) * deskBounds.extents.x +
              Mathf.Abs(lateral.z) * deskBounds.extents.z
            : 0f;
        float lateralOffset = Mathf.Max(
            DoorwayClearance,
            Mathf.Abs(deskLateralOffset) + deskLateralExtent + 0.3f);
        float preferredSide = deskLateralOffset > 0.05f ? -1f : 1f;

        Vector3 first = inside + lateral * (lateralOffset * preferredSide);
        Vector3 second = inside - lateral * (lateralOffset * preferredSide);
        if (IsDoorwayPointClear(first))
        {
            doorwayClearPoint = first;
            hasDoorwayClearPoint = true;
            return first;
        }
        if (IsDoorwayPointClear(second))
        {
            doorwayClearPoint = second;
            hasDoorwayClearPoint = true;
            return second;
        }

        // Keep a deterministic authored fallback even when a player has
        // temporarily parked an object beside reception. The movement probe
        // can then retry the route instead of returning a zero direction at
        // the door forever.
        doorwayClearPoint = first;
        hasDoorwayClearPoint = true;
        return first;
    }

    private bool IsDoorwayPointClear(Vector3 point)
    {
        if (fighter == null)
        {
            return true;
        }

        Vector3 lower = point + Vector3.up * 0.55f;
        Vector3 upper = point + Vector3.up * 1.85f;
        int count = Physics.OverlapCapsuleNonAlloc(
            lower, upper, DoorwayBodyRadius, doorwayPointHits,
            ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            Collider hit = doorwayPointHits[i];
            if (hit == null || hit.transform == transform || hit.transform.IsChildOf(transform))
            {
                continue;
            }
            if (hit.GetComponentInParent<EnemyFighter>() != null ||
                hit.GetComponentInParent<PlayerMovement>() != null ||
                hit.GetComponentInParent<GymDoorway>() != null ||
                HasRoomFloorInHierarchy(hit.transform) ||
                IsWalkableFloorSurface(hit))
            {
                continue;
            }
            return false;
        }

        return true;
    }

    private static bool TryGetReceptionDeskBounds(out Bounds bounds)
    {
        GameObject desk = GameObject.Find("Reception desk");
        Renderer[] renderers = desk != null
            ? desk.GetComponentsInChildren<Renderer>(true)
            : null;
        bool found = false;
        bounds = default;
        if (renderers == null)
        {
            return false;
        }

        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null)
            {
                continue;
            }

            if (!found)
            {
                bounds = renderer.bounds;
                found = true;
            }
            else
            {
                bounds.Encapsulate(renderer.bounds);
            }
        }

        return found;
    }

    private void HoldDoorOpenRequest()
    {
        if (doorway != null && !doorOpenRequestHeld)
        {
            doorway.RequestOpen();
            doorOpenRequestHeld = true;
        }
    }

    private void ReleaseDoorOpenRequest()
    {
        if (doorway != null && doorOpenRequestHeld)
        {
            doorway.ReleaseOpen();
            doorOpenRequestHeld = false;
        }
    }

    private void ResetDoorwayExitTracking()
    {
        doorwayExitStalledSeconds = 0f;
        lastDoorwayExitDistance = float.PositiveInfinity;
    }

    private void TryRecoverStalledDoorExit()
    {
        if (fighter == null || doorway == null)
        {
            return;
        }

        float distance = Vector3.ProjectOnPlane(
            travelTarget - fighter.transform.position, Vector3.up).magnitude;
        if (distance < lastDoorwayExitDistance - 0.025f)
        {
            lastDoorwayExitDistance = distance;
            doorwayExitStalledSeconds = 0f;
            return;
        }

        doorwayExitStalledSeconds += Time.fixedDeltaTime;
        if (doorwayExitStalledSeconds <= DoorwayExitStallTimeout)
        {
            return;
        }

        Vector3 direction = Vector3.ProjectOnPlane(
            travelTarget - fighter.transform.position, Vector3.up);
        if (direction.sqrMagnitude < 0.001f)
        {
            direction = doorway.ExteriorPoint - doorway.InteriorPoint;
        }
        if (direction.sqrMagnitude < 0.001f)
        {
            direction = Vector3.forward;
        }

        // Never resolve a doorway stall by writing the target into the
        // Rigidbody. That hid the underlying route failure as a visible
        // teleport, especially for Goku at the inside edge of the door.
        // Rebuild the route target and let the normal capsule steering cross
        // the passage on a later physics tick.
        doorwayExitRecoveryCount++;
        if (state == VisitorState.ExitingDoor)
        {
            hasDoorwayClearPoint = false;
            exitRoomClearPointPending = true;
            travelTarget = GetDoorwayClearPoint();
        }
        else
        {
            travelTarget = GetExteriorDoorClearPoint(fighter.transform.position.y);
        }
        fighter.StopVisitorMovement();
        Debug.LogWarning(
            $"GYMCHAOS_VISITOR_EXIT_ROUTE_FALLBACK enemy={fighter.Identity} " +
            $"state={state} target={travelTarget} recovery={doorwayExitRecoveryCount} " +
            "mode=reroute_no_teleport",
            this);
        ResetDoorwayExitTracking();
    }

    private static bool HasRoomFloorInHierarchy(Transform target)
    {
        for (Transform current = target; current != null; current = current.parent)
        {
            string lowerName = current.name.ToLowerInvariant();
            if (lowerName.Contains("rubber floor") ||
                lowerName == "plane" || lowerName.StartsWith("plane("))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsWalkableFloorSurface(Collider collider)
    {
        if (collider == null)
        {
            return false;
        }

        for (Transform current = collider.transform; current != null; current = current.parent)
        {
            string lowerName = current.name.ToLowerInvariant();
            if (lowerName.Contains("mat") || lowerName.Contains("carpet") ||
                lowerName.Contains("rug"))
            {
                return true;
            }
        }

        return false;
    }

    private void OnDisable()
    {
        CancelVehicleYield();
        RestoreRouteVehicleCollision();
        ReleaseDoorOpenRequest();
        squatStartPending = false;
        if (Application.isPlaying && !applicationQuitting &&
            enteredGym && !completedDoorExit)
        {
            Debug.LogError(
                $"GYMCHAOS_VISITOR_DISABLED_INSIDE enemy={fighter?.Identity} " +
                $"state={state} reason=external_disable",
                this);
        }

        // A GameObject can be disabled by the director in the same frame that
        // a death/combat transition is observed. EnemyFighter.FixedUpdate is
        // skipped for disabled/dead objects, so do the squat cleanup here as
        // well instead of leaving the rack occupied with a parented bar.
        if (squatController != null && squatController.IsActive)
        {
            squatController.Cancel();
        }
        EndWorkoutStationRelease();
        fighter?.ReleaseVisitorWorkoutPose();
        fighter?.RestoreVisitorPoseInterpolation();
        ReleasePendingStationApproach();
        pendingStation = null;
        attemptedWorkoutStations.Clear();
        postWorkoutFreeRoamUntil = 0f;
        state = VisitorState.Dormant;
    }

    private void OnApplicationQuit()
    {
        applicationQuitting = true;
    }
}
