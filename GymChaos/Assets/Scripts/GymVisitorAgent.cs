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

    public enum VisitorState
    {
        Dormant,
        FreeRoaming,
        EnteringDoor,
        EnteringRoom,
        ApproachingWorkout,
        Squatting,
        ExitingDoor,
        LeavingGym
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
        state == VisitorState.EnteringRoom || state == VisitorState.ExitingDoor ||
        state == VisitorState.LeavingGym;
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
    public bool CanDeactivate => HasCompletedDoorExit ||
        (!hasSuccessfulEntry && !enteredGym && leftGym && state == VisitorState.Dormant);
    public bool IsEntryPending => state == VisitorState.EnteringDoor ||
        state == VisitorState.EnteringRoom;
    public int CompletedWorkoutVersion => completedWorkoutVersion;
    public EnemyFighter Fighter => fighter;

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
        ReleaseDoorOpenRequest();
        EndWorkoutStationRelease();
        ReleasePendingStationApproach();
        state = VisitorState.FreeRoaming;
        enteredGym = true;
        leftGym = false;
        hasSuccessfulEntry = true;
        completedDoorExit = false;
        doorway = GymDoorway.Instance;
        hasDoorwayClearPoint = false;
        pendingStation = null;
        squatStartPending = false;
        entryRoomClearPointPending = false;
        exitRoomClearPointPending = false;
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
        entryRoomClearPointPending = false;
        exitRoomClearPointPending = false;
        ResetDoorwayExitTracking();
        postWorkoutFreeRoamUntil = 0f;
        roomTravelStalledSeconds = 0f;
        lastRoomTravelDistance = float.PositiveInfinity;
        fighter.StopVisitorMovement();
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

        doorway = door;
        hasDoorwayClearPoint = false;
        HoldDoorOpenRequest();
        pendingStation = null;
        entryRoomClearPointPending = false;
        exitRoomClearPointPending = true;
        travelTarget = GetDoorwayClearPoint();
        ResetDoorwayExitTracking();
        state = VisitorState.ExitingDoor;
        fighter.StopVisitorMovement();
    }

    public void AbortEntryAndReturnThroughDoor(GymDoorway door)
    {
        if (fighter == null || door == null || !IsEntryPending)
        {
            return;
        }

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

        switch (state)
        {
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
                        // segment. This avoids routing the capsule through the
                        // desk when the visitor is leaving the gym.
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

        // This is only a last-resort handoff after a real stalled physics
        // route. The target is always one of the authored clear/interior/
        // exterior doorway points, so the visitor cannot remain trapped at
        // the threshold indefinitely.
        fighter.SetVisitorSpawnPose(
            travelTarget,
            Quaternion.LookRotation(direction.normalized, Vector3.up));
        fighter.StopVisitorMovement();
        Debug.LogWarning(
            $"GYMCHAOS_VISITOR_EXIT_ROUTE_FALLBACK enemy={fighter.Identity} " +
            $"state={state} target={travelTarget}",
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
