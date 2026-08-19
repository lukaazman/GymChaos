using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Per-enemy bridge between the visitor director and EnemyFighter's normal
/// roaming. It owns only door travel and scheduled squat travel; free roaming
/// is handed back to EnemyFighter so existing behavior remains intact.
/// </summary>
public sealed class GymVisitorAgent : MonoBehaviour
{
    private const float WorkoutApproachStallTimeout = 2.4f;
    private const float WorkoutApproachTimeout = 18f;
    private const float FailedWorkoutFreeRoamSeconds = 3f;
    private const float WorkoutReleaseStallTimeout = 2.4f;
    private const float WorkoutReleaseTimeout = 8f;
    private const float WorkoutReleaseDistance = 3.35f;

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
    private int completedWorkoutVersion;
    private float postWorkoutFreeRoamUntil;
    private float roomTravelStalledSeconds;
    private float lastRoomTravelDistance = float.PositiveInfinity;
    private float workoutApproachStartedAt;
    private float workoutApproachStalledSeconds;
    private float lastWorkoutApproachDistance = float.PositiveInfinity;
    private float workoutBeginNotBefore;
    private GymExerciseStation workoutReleaseStation;
    private Vector3 workoutReleaseTarget;
    private float workoutReleaseStartedAt;
    private float workoutReleaseStalledSeconds;
    private float lastWorkoutReleaseDistance = float.PositiveInfinity;
    private bool applicationQuitting;
    private readonly List<GymExerciseStation> attemptedWorkoutStations =
        new List<GymExerciseStation>(3);

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
        EndWorkoutStationRelease();
        ReleasePendingStationApproach();
        state = VisitorState.FreeRoaming;
        enteredGym = true;
        leftGym = false;
        hasSuccessfulEntry = true;
        completedDoorExit = false;
        doorway = GymDoorway.Instance;
        pendingStation = null;
        postWorkoutFreeRoamUntil = 0f;
        roomTravelStalledSeconds = 0f;
        lastRoomTravelDistance = float.PositiveInfinity;
        if (fighter != null)
        {
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
        doorway.RequestOpen();
        roomTarget = GetDoorwayRoomStagingTarget(destinationInside);
        travelTarget = doorway.InteriorPoint;
        state = VisitorState.EnteringDoor;
        enteredGym = false;
        leftGym = false;
        hasSuccessfulEntry = false;
        completedDoorExit = false;
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
        attemptedWorkoutStations.Clear();
        pendingRepetitions = Mathf.Clamp(repetitions, 6, 12);
        pendingRepDuration = Mathf.Clamp(repDuration, 0.55f, 2.2f);
        workoutBeginNotBefore = 0f;
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
        doorway.RequestOpen();
        pendingStation = null;
        travelTarget = doorway.InteriorPoint;
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
        doorway.RequestOpen();
        pendingStation = null;
        enteredGym = false;
        leftGym = false;
        hasSuccessfulEntry = false;
        completedDoorExit = false;
        // Route even a stalled incoming visitor through the same authored
        // interior and exterior points. It must never disappear at the
        // doorway because a navigation timeout fired.
        travelTarget = doorway.InteriorPoint;
        state = VisitorState.ExitingDoor;
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

        doorway?.ReleaseOpen();
        state = VisitorState.Dormant;
        enteredGym = false;
        leftGym = true;
        hasSuccessfulEntry = false;
        completedDoorExit = false;
        pendingStation = null;
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
        postWorkoutFreeRoamUntil = 0f;
        if (fighter != null)
        {
            fighter.StopVisitorMovement();
        }
    }

    public void CancelForCombat()
    {
        if (squatController != null && squatController.IsActive)
        {
            squatController.Cancel();
        }

        EndWorkoutStationRelease();

        if (fighter != null)
        {
            fighter.StopVisitorMovement();
        }

        ReleasePendingStationApproach();
        state = enteredGym ? VisitorState.FreeRoaming : VisitorState.Dormant;
        pendingStation = null;
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
                    doorway?.ReleaseOpen();
                    state = VisitorState.EnteringRoom;
                    travelTarget = roomTarget;
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
                    state = VisitorState.FreeRoaming;
                    enteredGym = true;
                    leftGym = false;
                    hasSuccessfulEntry = true;
                    completedDoorExit = false;
                    roomTravelStalledSeconds = 0f;
                    lastRoomTravelDistance = 0f;
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
                            travelTarget = doorway.InteriorPoint +
                                inward.normalized * 3.6f;
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
                    // MoveVisitorTo stops the physics body immediately, but
                    // the retarget animator applies the idle pose on its next
                    // render update. Capture the squat base pose only after
                    // that settle frame; otherwise a visitor who arrived
                    // during Run can freeze a run-frame arm/leg pose as the
                    // IK reference and occasionally choose the crossed-arm
                    // branch.
                    if (workoutBeginNotBefore <= 0f)
                    {
                        fighter.SetVisitorSpawnPose(
                            pendingStation.EnemyPosition,
                            pendingStation.EnemyRotation);
                        fighter.StopVisitorMovement();
                        workoutBeginNotBefore = Time.time + 0.12f;
                        return true;
                    }

                    if (Time.time < workoutBeginNotBefore)
                    {
                        fighter.StopVisitorMovement();
                        return true;
                    }

                    // The collision-probe mover leaves the fighter facing
                    // the last travel segment. Snap only at the authored
                    // squat pose so every cage visitor faces the mirrors,
                    // independent of which side they approached from.
                    fighter.SetVisitorSpawnPose(
                        pendingStation.EnemyPosition,
                        pendingStation.EnemyRotation);
                    Vector3 actualForward = Vector3.ProjectOnPlane(
                        fighter.transform.forward, Vector3.up).normalized;
                    Vector3 stationForward = Vector3.ProjectOnPlane(
                        pendingStation.EnemyRotation * Vector3.forward,
                        Vector3.up).normalized;
                    Debug.Log(
                        $"GYMCHAOS_SQUAT_ORIENTATION enemy={fighter.Identity} " +
                        $"station={pendingStation.EquipmentName} " +
                        $"facingDot={Vector3.Dot(actualForward, stationForward):0.000}",
                        this);

                    if (squatController == null || !squatController.Begin(
                        pendingStation, fighter, pendingRepetitions, pendingRepDuration))
                    {
                        if (!TrySwitchToAlternativeSquatStation())
                        {
                            CancelWorkoutApproach("workout_begin_failed");
                        }
                    }
                    else
                    {
                        state = VisitorState.Squatting;
                        workoutBeginNotBefore = 0f;
                        ResetWorkoutApproachTracking();
                    }
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
                if (fighter.MoveVisitorTo(travelTarget, 2.2f, true))
                {
                    state = VisitorState.LeavingGym;
                    travelTarget = doorway != null ? doorway.ExteriorPoint : travelTarget;
                }
                return true;

            case VisitorState.LeavingGym:
                if (fighter.MoveVisitorTo(travelTarget, 2.2f, true))
                {
                    doorway?.ReleaseOpen();
                    state = VisitorState.Dormant;
                    enteredGym = false;
                    leftGym = true;
                    completedDoorExit = true;
                    pendingStation = null;
                    fighter.StopVisitorMovement();
                    Debug.Log($"GYMCHAOS_VISITOR_EXITED enemy={fighter.Identity}", this);
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
        workoutBeginNotBefore = 0f;
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
            workoutBeginNotBefore = 0f;
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

        // Confirm entry through a short authored segment inside the room.
        // A random point across the gym is not a reliable navigation target
        // for the lightweight collision-probe mover and could leave an enemy
        // circling at the threshold indefinitely. Once this point is reached,
        // the normal free-roam planner takes over and can choose any room
        // interest without creating a second enemy instance.
        Vector3 stagingTarget = doorway.InteriorPoint + inward.normalized * 3.6f;
        stagingTarget.y = requestedTarget.y;
        return stagingTarget;
    }

    private void OnDisable()
    {
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
