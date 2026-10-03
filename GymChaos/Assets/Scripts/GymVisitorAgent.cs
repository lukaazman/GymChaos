using System;
using System.Collections.Generic;
using UnityEngine;

[DefaultExecutionOrder(1050)]
public sealed partial class GymVisitorAgent : MonoBehaviour
{
	public enum VisitorState
	{
		Dormant,
		FreeRoaming,
		ApproachingGymFromVehicle,
		EnteringDoor,
		EnteringRoom,
		ApproachingWorkout,
		ReleasingWorkout,
		ApproachingLockerRoom,
		LockerRoomVisit,
		VisitingProteinStore,
		ProteinStoreDwell,
		ReturningFromProteinStore,
		Squatting,
		ExitingDoor,
		LeavingGym,
		ApproachingVehicle
	}

	private const float WorkoutApproachStallTimeout = 2.4f;

	private const float WorkoutApproachTimeout = 18f;

	private const float FailedWorkoutFreeRoamSeconds = 3f;

	private const float WorkoutReleaseStallTimeout = 2.4f;

	private const float WorkoutReleaseTimeout = 8f;

	private const float WorkoutReleaseDistance = 3.35f;

	private const float DoorwayClearance = 1.35f;

	private const float DoorwayBodyRadius = 0.6235f;

	private const float DoorwayExitStallTimeout = 0.75f;

	private const float VehicleRouteStallTimeout = 2.4f;

	private const float VehicleYieldSideStep = 3.2f;

	private const float VehicleYieldBackwardOffset = 0.6f;

	private const float VehicleYieldSpeed = 3.2f;

	private const float VehicleYieldHoldSeconds = 0.25f;

	private const float VehicleYieldTimeout = 8f;

	private const int MaxVehicleRouteReroutes = 4;

	private static GymVisitorAgent activeVehicleApproachAgent;
	private static readonly List<GymVisitorAgent> activeAgents =
		new List<GymVisitorAgent>();
	public static IReadOnlyList<GymVisitorAgent> ActiveAgents => activeAgents;

	private bool returningToVehicleAfterEntryAbort;

	private static GymVisitorAgent activeDoorwayExitAgent;

	private static GymVisitorAgent activeDoorwayEntryAgent;

	private EnemyFighter fighter;

	private GymDoorway doorway;

	private GymExerciseStation pendingStation;

	private SquatWorkoutController squatController;

	private VisitorState state;

	private Vector3 travelTarget;

	private Vector3 roomTarget;

	private Vector3 lockerVisitTarget;

	private Vector3[] lockerVisitWaypoints;

	private int lockerVisitWaypointIndex;

	private Vector3[] exitRoomWaypoints;

	private int exitRoomWaypointIndex;

	private float lockerVisitDwellUntil;

	private float lockerVisitDwellSeconds;

	private string lockerVisitLabel;

	private bool lockerVisitReturning;

	private bool storeVisitActive;

	private bool storeVisitReturning;

	private float storeVisitDwellSeconds;

	private float storeVisitDwellUntil;

	private Vector3 storeReturnRoomTarget;

	private Vector3[] storeVisitWaypoints;

	private int storeVisitWaypointIndex;

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

	private bool vehicleEntryUsesDavieBusGate;

	private bool vehicleEntryUsesProteinStoreRoute;

	private Vector3[] vehicleExitWaypoints;

	private int vehicleExitWaypointIndex;

	private bool vehicleExitUsesDavieBusGate;

	private bool vehicleExitUsesProteinStoreRoute;

	private Vector3 vehicleEntrySpawnPoint;

	private bool vehicleEntrySpawnPending;

	private float vehicleBoardingRadius = 0.55f;

	private bool vehicleTurnPending;

	private bool vehicleEntryPending;

	private bool vehicleAislePending;

	private Vector3 vehicleAisleTarget;

	private bool vehicleDetourActive;

	private Vector3[] vehicleDetourWaypoints;

	private int vehicleDetourWaypointIndex;

	private Vector3 vehicleDetourResumeTarget;

	private float vehicleRouteStalledSeconds;

	private float nextVehicleArrivalWaitLogTime;

	private bool doorwayEntryYieldingForExit;
	private bool doorwayExitPriorityLogIssued;

	private bool doorwayExitClearanceReleased;

	private bool deadReservationCleanupComplete;

	private float lastVehicleRouteDistance = float.PositiveInfinity;

	private int vehicleRerouteAttempt;

	private int vehicleRouteDetourCount;

	private GymVisitorVehicle routeVehicleWithIgnoredCollision;

	private GymVisitorVehicle arrivalVehicle;

	private GymVisitorVehicle yieldingToVehicle;

	private Vector3 vehicleYieldPoint;

	private float vehicleYieldStartedAt;

	private float vehicleYieldReachedAt = -1f;

	private int vehicleYieldCount;

	private bool entryRoomClearPointPending;
	private Vector3[] entryRoomWaypoints;
	private int entryRoomWaypointIndex;
	private int entryRoomRecoveryCount;

	private bool exitRoomClearPointPending;

	private bool doorOpenRequestHeld;

	private bool doorwayWallCollisionsIgnored;

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

	private Vector3[] workoutReleaseWaypoints;

	private int workoutReleaseWaypointIndex;

	private bool workoutReleaseProgressLogged;

	private bool applicationQuitting;

	private bool externalVehicleRouteVerification;

#if UNITY_EDITOR
	private bool verificationDepartureQueueHold;
#endif

	private readonly List<GymExerciseStation> attemptedWorkoutStations = new List<GymExerciseStation>(3);

	private readonly Collider[] doorwayPointHits = (Collider[])(object)new Collider[32];

	private readonly RaycastHit[] doorwayRouteHits = new RaycastHit[32];

	public VisitorState State => state;

	public bool IsDoorwayWallCollisionIgnored => doorwayWallCollisionsIgnored;

	public bool IsInsideGym
	{
		get
		{
			if (enteredGym && !leftGym && !storeVisitActive && state != VisitorState.Dormant && state != VisitorState.ExitingDoor)
			{
				return state != VisitorState.LeavingGym;
			}
			return false;
		}
	}

	public bool IsTraveling
	{
		get
		{
			if (state != VisitorState.EnteringDoor && state != VisitorState.ApproachingGymFromVehicle && state != VisitorState.EnteringRoom && state != VisitorState.ExitingDoor && state != VisitorState.LeavingGym && state != VisitorState.ApproachingVehicle && state != VisitorState.ReleasingWorkout && state != VisitorState.ApproachingLockerRoom && state != VisitorState.VisitingProteinStore)
			{
				return state == VisitorState.ReturningFromProteinStore;
			}
			return true;
		}
	}

	public bool IsBusy
	{
		get
		{
			if (!IsTraveling && state != VisitorState.ApproachingWorkout && state != VisitorState.LockerRoomVisit && state != VisitorState.ProteinStoreDwell && state != VisitorState.Squatting && !IsPostWorkoutFreeRoam && !((UnityEngine.Object)(object)workoutReleaseStation != (UnityEngine.Object)null))
			{
				if ((UnityEngine.Object)(object)fighter != (UnityEngine.Object)null)
				{
					return fighter.IsOnTreadmill;
				}
				return false;
			}
			return true;
		}
	}

	public bool IsPostWorkoutFreeRoam
	{
		get
		{
			if (state == VisitorState.FreeRoaming)
			{
				return Time.time < postWorkoutFreeRoamUntil;
			}
			return false;
		}
	}

	public bool IsSquatLifecycleActive
	{
		get
		{
			if (!((UnityEngine.Object)(object)pendingStation != (UnityEngine.Object)null) && state != VisitorState.ApproachingWorkout && state != VisitorState.Squatting && !((UnityEngine.Object)(object)workoutReleaseStation != (UnityEngine.Object)null))
			{
				if ((UnityEngine.Object)(object)squatController != (UnityEngine.Object)null)
				{
					return squatController.IsActive;
				}
				return false;
			}
			return true;
		}
	}

	public bool IsWorkoutActive
	{
		get
		{
			if (state != VisitorState.Squatting)
			{
				if ((UnityEngine.Object)(object)squatController != (UnityEngine.Object)null)
				{
					return squatController.IsActive;
				}
				return false;
			}
			return true;
		}
	}

	public bool HasEnteredGym => enteredGym;

	public bool HasLeftGym => leftGym;

	// The director uses this runtime-safe signal to serialize doorway traffic
	// even when an indoor visitor is still physically occupying the narrow
	// connector while its state is FreeRoaming. This is intentionally a
	// clearance query, not a collision-ignore switch.
	public bool IsDoorwayTraversalAreaOccupied =>
		state != VisitorState.Dormant && IsPhysicallyInDoorwayTraversalArea();

	public bool HasCompletedDoorExit
	{
		get
		{
			if (completedDoorExit && !enteredGym && leftGym)
			{
				return state == VisitorState.Dormant;
			}
			return false;
		}
	}

	public bool HasReachedVehicle
	{
		get
		{
			if (reachedVehicle)
			{
				return state == VisitorState.Dormant;
			}
			return false;
		}
	}

	public int CompletedVehicleApproaches => completedVehicleApproaches;

	public int DoorwayExitRecoveryCountForVerification => doorwayExitRecoveryCount;

#if UNITY_EDITOR
	public bool IsDoorwayEntryYieldingForVerification => doorwayEntryYieldingForExit;
	public bool IsDoorwayExitClearForVerification => IsDoorwayExitAreaClearForReservation();
	public bool DoorwayExitClearanceReleasedForVerification => doorwayExitClearanceReleased;
	public float DoorwayExitClearanceMetersForVerification => GetDoorwayOutwardClearanceMeters();

	public void SetDepartureQueueHoldForVerification(bool hold)
	{
		verificationDepartureQueueHold = hold;
		if (hold && state == VisitorState.FreeRoaming)
		{
			fighter?.StopVisitorMovement();
		}
	}

	public void ResetForEntryVerification()
	{
		CancelForCombat();
		state = VisitorState.Dormant;
		enteredGym = false;
		leftGym = true;
		hasSuccessfulEntry = false;
		completedDoorExit = false;
		reachedVehicle = false;
		doorway = GymDoorway.Instance;
		hasDoorwayClearPoint = false;
		entryRoomWaypoints = null;
		exitRoomWaypoints = null;
		entryRoomWaypointIndex = 0;
						exitRoomWaypointIndex = 0;
		doorwayExitRecoveryCount = 0;
		fighter?.ClearVisitorStuckRecovery();
		deadReservationCleanupComplete = false;
		externalVehicleRouteVerification = false;
		ResetDoorwayExitTracking();
	}
	public bool IsAtDoorwayEntryQueueForVerification
	{
		get
		{
			if (state != VisitorState.ApproachingGymFromVehicle ||
				vehicleEntryWaypoints == null || vehicleEntryWaypoints.Length < 2 ||
				vehicleEntryWaypointIndex != vehicleEntryWaypoints.Length - 2 || fighter == null)
			{
				return false;
			}
			return Vector3.ProjectOnPlane(
				fighter.VisitorPhysicsPosition - travelTarget, Vector3.up).magnitude <= 0.60f;
		}
	}

	public string DoorwayRouteWaitOwnerForVerification
	{
		get
		{
            if (state != VisitorState.ApproachingGymFromVehicle &&
                state != VisitorState.EnteringDoor && state != VisitorState.EnteringRoom)
            {
                return "none";
            }
			GymVisitorAgent owner = activeVehicleApproachAgent;
			if (owner != null && owner != this && owner.isActiveAndEnabled &&
				owner.IsUsingSharedParkingConnector && IsNearDoorwayConnector(owner))
			{
				return $"shared-connector:{owner.fighter?.Identity}";
			}
			owner = activeDoorwayExitAgent;
			if (owner != null && owner != this && owner.isActiveAndEnabled &&
				!owner.IsDoorwayExitAreaClearForReservation())
			{
				return $"door-exit:{owner.fighter?.Identity}";
			}
			owner = activeDoorwayEntryAgent;
			if (owner != null && owner != this && owner.isActiveAndEnabled &&
				owner.IsDoorwayEntryAreaOccupied())
			{
				return $"door-entry:{owner.fighter?.Identity}";
			}
			return "none";
		}
	}
#endif

	public bool IsUsingSharedParkingConnector
	{
		get
		{
			if (returningToVehicleAfterEntryAbort && state == VisitorState.ApproachingVehicle)
			{
				return vehicleExitWaypoints != null && vehicleExitWaypointIndex < vehicleExitWaypoints.Length;
			}
			if (state == VisitorState.ApproachingGymFromVehicle && vehicleEntryWaypoints != null)
			{
				int sharedConnectorWaypointCount = (vehicleEntryUsesProteinStoreRoute ? 32 : (vehicleEntryUsesDavieBusGate ? 6 : 4));
				return vehicleEntryWaypointIndex < sharedConnectorWaypointCount;
			}
			if (state == VisitorState.ApproachingVehicle && vehicleExitWaypoints != null)
			{
				return vehicleExitWaypointIndex < vehicleExitWaypoints.Length;
			}
			if (storeVisitActive &&
				(state == VisitorState.VisitingProteinStore || state == VisitorState.ReturningFromProteinStore) &&
				storeVisitWaypoints != null)
			{
				return storeVisitWaypointIndex < storeVisitWaypoints.Length;
			}
			return false;
		}
	}

	public bool IsWaitingForSharedParkingConnector
	{
		get
		{
			if (!IsUsingSharedParkingConnector ||
				(UnityEngine.Object)(object)activeVehicleApproachAgent == (UnityEngine.Object)null ||
				(UnityEngine.Object)(object)activeVehicleApproachAgent == (UnityEngine.Object)(object)this ||
				!((Behaviour)activeVehicleApproachAgent).isActiveAndEnabled)
			{
				return false;
			}
			return activeVehicleApproachAgent.IsUsingSharedParkingConnector;
		}
	}

	public bool IsYieldingToVehicle => (UnityEngine.Object)(object)yieldingToVehicle != (UnityEngine.Object)null;

	public bool CanDeactivate
	{
		get
		{
			if (!HasCompletedDoorExit)
			{
				if (!hasSuccessfulEntry && !enteredGym && leftGym)
				{
					return state == VisitorState.Dormant;
				}
				return false;
			}
			return true;
		}
	}

	public bool IsStoreVisitActive => storeVisitActive;

	public bool IsEntryPending
	{
		get
		{
			if (state != VisitorState.EnteringDoor && state != VisitorState.EnteringRoom)
			{
				return state == VisitorState.ApproachingGymFromVehicle;
			}
			return true;
		}
	}

	public bool IsWaitingForSharedCorridor
	{
		get
		{
			if (state != VisitorState.ApproachingGymFromVehicle && state != VisitorState.EnteringDoor && state != VisitorState.EnteringRoom)
			{
				return false;
			}
		if (state == VisitorState.ApproachingGymFromVehicle && GymVisitorVehicle.HasActiveParkingArrivalExcept(arrivalVehicle))
		{
			// Keep the director timeout from treating an authored vehicle-arrival queue as a failed route.
			return true;
		}
		GymVisitorAgent connectorOwner = activeVehicleApproachAgent;
		if (state == VisitorState.ApproachingGymFromVehicle &&
			connectorOwner != null && connectorOwner != this &&
			connectorOwner.isActiveAndEnabled &&
			connectorOwner.IsUsingSharedParkingConnector &&
			IsNearDoorwayConnector(connectorOwner))
		{
			return true;
		}
		GymVisitorAgent entryOwner = activeDoorwayEntryAgent;
		if ((state == VisitorState.ApproachingGymFromVehicle ||
			state == VisitorState.EnteringDoor || state == VisitorState.EnteringRoom) &&
			entryOwner != null && entryOwner != this && entryOwner.isActiveAndEnabled &&
			entryOwner.IsDoorwayEntryAreaOccupied())
		{
			return true;
		}
		GymVisitorAgent exitAgent = activeDoorwayExitAgent;
			if ((UnityEngine.Object)(object)exitAgent != (UnityEngine.Object)null && (UnityEngine.Object)(object)exitAgent != (UnityEngine.Object)(object)this && ((Behaviour)exitAgent).isActiveAndEnabled && !exitAgent.IsDoorwayExitAreaClearForReservation())
			{
				return true;
			}
			if (state == VisitorState.ApproachingGymFromVehicle && IsAtDoorwayEntryQueueWindow() && (UnityEngine.Object)(object)fighter != (UnityEngine.Object)null)
			{
				if ((fighter.LastVisitorRouteBlocker ?? string.Empty).IndexOf("hitbox", StringComparison.OrdinalIgnoreCase) < 0)
				{
					return (fighter.LastVisitorRouteBlocker ?? string.Empty).IndexOf("Player", StringComparison.OrdinalIgnoreCase) >= 0;
				}
				return true;
			}
			return false;
		}
	}

	public int CompletedWorkoutVersion => completedWorkoutVersion;

	public GymExerciseStation WorkoutStationForVerification => pendingStation;

	public EnemyFighter Fighter => fighter;

	public static bool IsVehicleApproachReserved
	{
		get
		{
			if ((UnityEngine.Object)(object)activeVehicleApproachAgent == (UnityEngine.Object)null || !((Behaviour)activeVehicleApproachAgent).isActiveAndEnabled)
			{
				activeVehicleApproachAgent = null;
				return false;
			}
			return true;
		}
	}

	public Vector3 TravelTargetForVerification => travelTarget;

	public int VehicleEntryWaypointForVerification => vehicleEntryWaypointIndex;

	public int VehicleExitWaypointForVerification => vehicleExitWaypointIndex;

	public int VehicleRouteDetourCountForVerification => vehicleRouteDetourCount;
	public float VehicleRouteStalledSecondsForVerification => vehicleRouteStalledSeconds;
	public bool VehicleRouteDetourActiveForVerification => vehicleDetourActive;

	public int VehicleRouteDetourWaypointCountForVerification =>
		vehicleDetourWaypoints != null ? vehicleDetourWaypoints.Length : 0;

	public int VehicleRouteDetourWaypointIndexForVerification =>
		vehicleDetourWaypointIndex;

	public Vector3[] VehicleRouteDetourWaypointsForVerification =>
		vehicleDetourWaypoints != null ? (Vector3[])vehicleDetourWaypoints.Clone() : Array.Empty<Vector3>();

	public Vector3 VehicleRouteDetourResumeTargetForVerification =>
		vehicleDetourResumeTarget;

	public int VehicleYieldCountForVerification => vehicleYieldCount;

	public string VehicleStageForVerification => $"entryWaypoint={vehicleEntryWaypointIndex},exitWaypoint={vehicleExitWaypointIndex}";

	public void Configure(EnemyFighter owner)
	{
		fighter = (((UnityEngine.Object)(object)owner != (UnityEngine.Object)null) ? owner : ((Component)this).GetComponent<EnemyFighter>());
		if ((UnityEngine.Object)(object)squatController == (UnityEngine.Object)null)
		{
			squatController = ((Component)this).GetComponent<SquatWorkoutController>();
		}
		if ((UnityEngine.Object)(object)squatController == (UnityEngine.Object)null)
		{
			squatController = ((Component)this).gameObject.AddComponent<SquatWorkoutController>();
		}
	}

	public void MarkInitialInside()
	{
		arrivalVehicle = null;
		ResetDestinationVisitState();
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
		fighter?.ClearVisitorStuckRecovery();
		ResetDoorwayExitTracking();
		postWorkoutFreeRoamUntil = 0f;
		roomTravelStalledSeconds = 0f;
		lastRoomTravelDistance = float.PositiveInfinity;
		if ((UnityEngine.Object)(object)fighter != (UnityEngine.Object)null)
		{
			fighter.ReleaseVisitorWorkoutPose();
			fighter.RestoreVisitorPoseInterpolation();
			fighter.StopVisitorMovement();
		}
	}

	public void BeginEntry(GymDoorway door, Vector3 destinationInside)
	{
		arrivalVehicle = null;
		//IL_006e: Unknown result type (might be due to invalid IL or missing references)
		//IL_006f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0074: Unknown result type (might be due to invalid IL or missing references)
		//IL_0080: Unknown result type (might be due to invalid IL or missing references)
		//IL_0085: Unknown result type (might be due to invalid IL or missing references)
		ResetDestinationVisitState();
		if (!((UnityEngine.Object)(object)fighter == (UnityEngine.Object)null) && !((UnityEngine.Object)(object)door == (UnityEngine.Object)null))
		{
			CancelVehicleYield();
			RestoreRouteVehicleCollision();
			RestoreDoorwayWallCollisions();
			RestoreVisitorPlayerBoundaryCollisions();
			EndWorkoutStationRelease();
			GymBackRoomBuilder.HideBenchBagsForVisitor(fighter.Identity);
			doorway = door;
			hasDoorwayClearPoint = false;
			HoldDoorOpenRequest();
			SetDoorwayWallCollisionsIgnored(ignored: true);
			roomTarget = destinationInside;
			travelTarget = doorway.InteriorPoint;
			state = VisitorState.EnteringDoor;
			enteredGym = false;
			leftGym = false;
			hasSuccessfulEntry = false;
			completedDoorExit = false;
			reachedVehicle = false;
			entryRoomClearPointPending = false;
			entryRoomWaypoints = null;
			entryRoomWaypointIndex = 0;
			entryRoomRecoveryCount = 0;
							exitRoomClearPointPending = false;
			doorwayExitRecoveryCount = 0;
			fighter?.ClearVisitorStuckRecovery();
			ResetDoorwayExitTracking();
			postWorkoutFreeRoamUntil = 0f;
			roomTravelStalledSeconds = 0f;
			lastRoomTravelDistance = float.PositiveInfinity;
			fighter.StopVisitorMovement();
		}
	}

	private bool BeginLockerRoomReturn()
	{
		//IL_0011: Unknown result type (might be due to invalid IL or missing references)
		//IL_0058: Unknown result type (might be due to invalid IL or missing references)
		//IL_005d: Unknown result type (might be due to invalid IL or missing references)
		//IL_006b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0081: Unknown result type (might be due to invalid IL or missing references)
		//IL_0082: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ac: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b1: Unknown result type (might be due to invalid IL or missing references)
		//IL_0105: Unknown result type (might be due to invalid IL or missing references)
		if ((UnityEngine.Object)(object)fighter == (UnityEngine.Object)null)
		{
			return false;
		}
		if (!GymBackRoomBuilder.TryGetLockerVisitRoute(lockerVisitTarget, out var fullRoute, out var _) || fullRoute == null || fullRoute.Length < 2)
		{
			GymBackRoomBuilder.ReleaseLockerSlot(fighter.Identity);
			return false;
		}
		lockerVisitWaypoints = (Vector3[])(object)new Vector3[fullRoute.Length - 1];
		for (int index = 0; index < lockerVisitWaypoints.Length; index++)
		{
			Vector3 point = fullRoute[fullRoute.Length - 2 - index];
			point.y = ((Component)fighter).transform.position.y;
			lockerVisitWaypoints[index] = point;
		}
		lockerVisitWaypointIndex = 0;
		lockerVisitReturning = true;
		travelTarget = lockerVisitWaypoints[0];
		GymBackRoomBuilder.HideBenchBagsForVisitor(fighter.Identity);
		state = VisitorState.ApproachingLockerRoom;
		fighter.StopVisitorMovement();
		Debug.Log((object)($"GYMCHAOS_VISITOR_LOCKER_EXIT_REQUESTED enemy={fighter.Identity} " + $"points={lockerVisitWaypoints.Length} target={travelTarget}"), (UnityEngine.Object)(object)this);
		return true;
	}

	public bool BeginLockerRoomVisit(Vector3 destination, float dwellSeconds = 2.8f, string label = "locker room")
	{
		//IL_003b: Unknown result type (might be due to invalid IL or missing references)
		//IL_006c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0071: Unknown result type (might be due to invalid IL or missing references)
		//IL_0077: Unknown result type (might be due to invalid IL or missing references)
		//IL_0078: Unknown result type (might be due to invalid IL or missing references)
		//IL_008e: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b8: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e7: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ec: Unknown result type (might be due to invalid IL or missing references)
		//IL_015d: Unknown result type (might be due to invalid IL or missing references)
		if ((UnityEngine.Object)(object)fighter == (UnityEngine.Object)null || state != VisitorState.FreeRoaming || !IsInsideGym || fighter.IsDead || fighter.IsOnTreadmill)
		{
			return false;
		}
		if (!GymBackRoomBuilder.TryGetLockerVisitRoute(destination, out lockerVisitWaypoints, out var _))
		{
			GymBackRoomBuilder.ReleaseLockerSlot(fighter.Identity);
			return false;
		}
		lockerVisitWaypointIndex = 0;
		travelTarget = lockerVisitWaypoints[0];
		lockerVisitTarget = destination;
		lockerVisitTarget.y = ((Component)fighter).transform.position.y;
		for (int i = 0; i < lockerVisitWaypoints.Length; i++)
		{
			lockerVisitWaypoints[i].y = ((Component)fighter).transform.position.y;
		}
		lockerVisitTarget = lockerVisitWaypoints[lockerVisitWaypoints.Length - 1];
		lockerVisitDwellUntil = 0f;
		lockerVisitDwellSeconds = Mathf.Max(0.5f, dwellSeconds);
		lockerVisitLabel = label;
		lockerVisitReturning = false;
		state = VisitorState.ApproachingLockerRoom;
		fighter.StopVisitorMovement();
		GymBackRoomBuilder.ShowBenchBagsForVisitor(fighter.Identity);
		Debug.Log((object)($"GYMCHAOS_VISITOR_LOCKER_REQUESTED enemy={fighter.Identity} " + $"target={lockerVisitTarget} routePoints={lockerVisitWaypoints.Length} " + $"dwell={dwellSeconds:F1}"), (UnityEngine.Object)(object)this);
		lockerVisitDwellUntil = 0f;
		return true;
	}

	public void BeginExit(GymDoorway door)
	{
		if (fighter == null || door == null || !IsInsideGym || IsWorkoutActive || IsPostWorkoutFreeRoam)
		{
			return;
		}

		CancelVehicleYield();
#if UNITY_EDITOR
		verificationDepartureQueueHold = false;
#endif
		doorwayExitPriorityLogIssued = false;
		doorwayExitClearanceReleased = false;
		ReleaseDoorwayEntrySlot();
		GymBackRoomBuilder.HideBenchBagsForVisitor(fighter.Identity);
		doorway = door;
		hasDoorwayClearPoint = false;
		HoldDoorOpenRequest();
		SetDoorwayWallCollisionsIgnored(ignored: true);
		pendingStation = null;
		entryRoomClearPointPending = false;
		entryRoomWaypoints = null;
		entryRoomWaypointIndex = 0;
						exitRoomClearPointPending = false;
		exitRoomWaypoints = BuildIndoorDoorExitRoute();
						exitRoomWaypointIndex = 0;
						travelTarget = exitRoomWaypoints[0];
		Debug.Log($"GYMCHAOS_VISITOR_EXIT_ROUTE_PLANNED enemy={fighter.Identity} points={exitRoomWaypoints.Length} target={doorway.InteriorPoint}", this);
		doorwayExitRecoveryCount = 0;
		fighter?.ClearVisitorStuckRecovery();
		ResetDoorwayExitTracking();
		doorwayExitReservationLatched = false;
		state = VisitorState.ExitingDoor;
		fighter.StopVisitorMovement();
	}

	private void ReleaseDeadVisitorReservations()
	{
		if (deadReservationCleanupComplete)
		{
			return;
		}
		deadReservationCleanupComplete = true;
		doorwayEntryYieldingForExit = false;
		ReleaseDoorwayEntrySlot();
		ReleaseDoorwayExitSlot();
		ReleaseVehicleApproachReservation();
		CancelVehicleYield();
		RestoreRouteVehicleCollision();
		RestoreDoorwayWallCollisions();
		ReleaseDoorOpenRequest();
		if (fighter != null)
		{
			GymBackRoomBuilder.ReleaseLockerSlot(fighter.Identity);
			GymBackRoomBuilder.HideBenchBagsForVisitor(fighter.Identity);
		}
	}

	private void OnDestroy()
	{
		activeAgents.Remove(this);
		RestoreRouteVehicleCollision();
		RestoreDoorwayWallCollisions();
		RestoreVisitorPlayerBoundaryCollisions();
		ReleaseVehicleApproachReservation();
		ReleaseDoorwayEntrySlot();
		ReleaseDoorwayExitSlot();
	}

	public void CancelPendingVisit()
	{
		ResetDestinationVisitState();
		if (IsEntryPending)
		{
			CancelVehicleYield();
			RestoreRouteVehicleCollision();
			RestoreDoorwayWallCollisions();
			RestoreVisitorPlayerBoundaryCollisions();
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
			if ((UnityEngine.Object)(object)fighter != (UnityEngine.Object)null)
			{
				fighter.StopVisitorMovement();
			}
		}
	}

	public void MarkDormant()
	{
		arrivalVehicle = null;
		ResetDestinationVisitState();
		if (hasSuccessfulEntry && !completedDoorExit)
		{
			Debug.LogError((object)($"GYMCHAOS_VISITOR_DORMANT_REJECTED enemy={fighter?.Identity} " + "reason=door_exit_not_completed"), (UnityEngine.Object)(object)this);
			return;
		}
		CancelVehicleYield();
		RestoreRouteVehicleCollision();
		RestoreDoorwayWallCollisions();
		RestoreVisitorPlayerBoundaryCollisions();
		ReleaseDoorOpenRequest();
		EndWorkoutStationRelease();
		ReleasePendingStationApproach();
		if ((UnityEngine.Object)(object)squatController != (UnityEngine.Object)null && squatController.IsActive)
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
		if ((UnityEngine.Object)(object)fighter != (UnityEngine.Object)null)
		{
			fighter.ReleaseVisitorWorkoutPose();
			fighter.RestoreVisitorPoseInterpolation();
			fighter.StopVisitorMovement();
		}
	}

	public void CancelForCombat()
	{
		ResetDestinationVisitState();
		CancelVehicleYield();
		RestoreRouteVehicleCollision();
		RestoreDoorwayWallCollisions();
		RestoreVisitorPlayerBoundaryCollisions();
		ReleaseDoorOpenRequest();
		squatStartPending = false;
		if ((UnityEngine.Object)(object)squatController != (UnityEngine.Object)null && squatController.IsActive)
		{
			squatController.Cancel();
		}
		EndWorkoutStationRelease();
		if ((UnityEngine.Object)(object)fighter != (UnityEngine.Object)null)
		{
			fighter.ReleaseVisitorWorkoutPose();
			fighter.RestoreVisitorPoseInterpolation();
			fighter.StopVisitorMovement();
		}
		ReleasePendingStationApproach();
		state = (enteredGym ? VisitorState.FreeRoaming : VisitorState.Dormant);
		pendingStation = null;
		entryRoomClearPointPending = false;
						exitRoomClearPointPending = false;
		ResetDoorwayExitTracking();
		postWorkoutFreeRoamUntil = 0f;
	}

	public bool TickPhysics(EnemyFighter owner)
	{
		//IL_0428: Unknown result type (might be due to invalid IL or missing references)
		//IL_051d: Unknown result type (might be due to invalid IL or missing references)
		//IL_052d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0532: Unknown result type (might be due to invalid IL or missing references)
		//IL_0537: Unknown result type (might be due to invalid IL or missing references)
		//IL_053c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0541: Unknown result type (might be due to invalid IL or missing references)
		//IL_0553: Unknown result type (might be due to invalid IL or missing references)
		//IL_0c99: Unknown result type (might be due to invalid IL or missing references)
		//IL_0e0d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0e17: Unknown result type (might be due to invalid IL or missing references)
		//IL_0e22: Unknown result type (might be due to invalid IL or missing references)
		//IL_0e27: Unknown result type (might be due to invalid IL or missing references)
		//IL_0e2c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0e31: Unknown result type (might be due to invalid IL or missing references)
		//IL_0e36: Unknown result type (might be due to invalid IL or missing references)
		//IL_0184: Unknown result type (might be due to invalid IL or missing references)
		//IL_0194: Unknown result type (might be due to invalid IL or missing references)
		//IL_044b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0450: Unknown result type (might be due to invalid IL or missing references)
		//IL_0453: Unknown result type (might be due to invalid IL or missing references)
		//IL_0460: Unknown result type (might be due to invalid IL or missing references)
		//IL_0465: Unknown result type (might be due to invalid IL or missing references)
		//IL_046a: Unknown result type (might be due to invalid IL or missing references)
		//IL_046f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0474: Unknown result type (might be due to invalid IL or missing references)
		//IL_1155: Unknown result type (might be due to invalid IL or missing references)
		//IL_115a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0e5e: Unknown result type (might be due to invalid IL or missing references)
		//IL_049a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0493: Unknown result type (might be due to invalid IL or missing references)
		//IL_0579: Unknown result type (might be due to invalid IL or missing references)
		//IL_057e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0590: Unknown result type (might be due to invalid IL or missing references)
		//IL_05a0: Unknown result type (might be due to invalid IL or missing references)
		//IL_05a5: Unknown result type (might be due to invalid IL or missing references)
		//IL_05aa: Unknown result type (might be due to invalid IL or missing references)
		//IL_05af: Unknown result type (might be due to invalid IL or missing references)
		//IL_05b4: Unknown result type (might be due to invalid IL or missing references)
		//IL_08f0: Unknown result type (might be due to invalid IL or missing references)
		//IL_11c3: Unknown result type (might be due to invalid IL or missing references)
		//IL_11b6: Unknown result type (might be due to invalid IL or missing references)
		//IL_049c: Unknown result type (might be due to invalid IL or missing references)
		//IL_04ae: Unknown result type (might be due to invalid IL or missing references)
		//IL_04be: Unknown result type (might be due to invalid IL or missing references)
		//IL_04c3: Unknown result type (might be due to invalid IL or missing references)
		//IL_04c8: Unknown result type (might be due to invalid IL or missing references)
		//IL_04cd: Unknown result type (might be due to invalid IL or missing references)
		//IL_04d2: Unknown result type (might be due to invalid IL or missing references)
		//IL_0500: Unknown result type (might be due to invalid IL or missing references)
		//IL_103b: Unknown result type (might be due to invalid IL or missing references)
		//IL_11c8: Unknown result type (might be due to invalid IL or missing references)
		//IL_11fb: Unknown result type (might be due to invalid IL or missing references)
		//IL_0d51: Unknown result type (might be due to invalid IL or missing references)
		//IL_0ce3: Unknown result type (might be due to invalid IL or missing references)
		//IL_0ce8: Unknown result type (might be due to invalid IL or missing references)
		//IL_1378: Unknown result type (might be due to invalid IL or missing references)
		//IL_0204: Unknown result type (might be due to invalid IL or missing references)
		//IL_0744: Unknown result type (might be due to invalid IL or missing references)
		//IL_074f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0754: Unknown result type (might be due to invalid IL or missing references)
		//IL_0759: Unknown result type (might be due to invalid IL or missing references)
		//IL_075e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0763: Unknown result type (might be due to invalid IL or missing references)
		//IL_0af9: Unknown result type (might be due to invalid IL or missing references)
		//IL_0b09: Unknown result type (might be due to invalid IL or missing references)
		//IL_0b0e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0b13: Unknown result type (might be due to invalid IL or missing references)
		//IL_0b18: Unknown result type (might be due to invalid IL or missing references)
		//IL_0b1d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0b2f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0930: Unknown result type (might be due to invalid IL or missing references)
		//IL_0935: Unknown result type (might be due to invalid IL or missing references)
		//IL_104e: Unknown result type (might be due to invalid IL or missing references)
		//IL_120e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0da0: Unknown result type (might be due to invalid IL or missing references)
		//IL_0d2f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0d22: Unknown result type (might be due to invalid IL or missing references)
		//IL_09e1: Unknown result type (might be due to invalid IL or missing references)
		//IL_0da5: Unknown result type (might be due to invalid IL or missing references)
		//IL_0ddc: Unknown result type (might be due to invalid IL or missing references)
		//IL_0d92: Unknown result type (might be due to invalid IL or missing references)
		//IL_0d85: Unknown result type (might be due to invalid IL or missing references)
		//IL_0d34: Unknown result type (might be due to invalid IL or missing references)
		//IL_136b: Unknown result type (might be due to invalid IL or missing references)
		//IL_079d: Unknown result type (might be due to invalid IL or missing references)
		//IL_077f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0786: Unknown result type (might be due to invalid IL or missing references)
		//IL_0790: Unknown result type (might be due to invalid IL or missing references)
		//IL_0795: Unknown result type (might be due to invalid IL or missing references)
		//IL_10f6: Unknown result type (might be due to invalid IL or missing references)
		//IL_109f: Unknown result type (might be due to invalid IL or missing references)
		//IL_10a4: Unknown result type (might be due to invalid IL or missing references)
		//IL_125f: Unknown result type (might be due to invalid IL or missing references)
		//IL_1264: Unknown result type (might be due to invalid IL or missing references)
		//IL_0f2f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0f34: Unknown result type (might be due to invalid IL or missing references)
		//IL_07a2: Unknown result type (might be due to invalid IL or missing references)
		//IL_07a5: Unknown result type (might be due to invalid IL or missing references)
		//IL_07a7: Unknown result type (might be due to invalid IL or missing references)
		//IL_07bd: Unknown result type (might be due to invalid IL or missing references)
		//IL_07ce: Unknown result type (might be due to invalid IL or missing references)
		//IL_07de: Unknown result type (might be due to invalid IL or missing references)
		//IL_07e3: Unknown result type (might be due to invalid IL or missing references)
		//IL_07e8: Unknown result type (might be due to invalid IL or missing references)
		//IL_07ed: Unknown result type (might be due to invalid IL or missing references)
		//IL_07f2: Unknown result type (might be due to invalid IL or missing references)
		//IL_082b: Unknown result type (might be due to invalid IL or missing references)
		//IL_1295: Unknown result type (might be due to invalid IL or missing references)
		//IL_1288: Unknown result type (might be due to invalid IL or missing references)
		//IL_0ec2: Unknown result type (might be due to invalid IL or missing references)
		//IL_0ec7: Unknown result type (might be due to invalid IL or missing references)
		//IL_0b94: Unknown result type (might be due to invalid IL or missing references)
		//IL_0b99: Unknown result type (might be due to invalid IL or missing references)
		//IL_0ba6: Unknown result type (might be due to invalid IL or missing references)
		//IL_0bab: Unknown result type (might be due to invalid IL or missing references)
		//IL_129a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0be6: Unknown result type (might be due to invalid IL or missing references)
		//IL_0bee: Unknown result type (might be due to invalid IL or missing references)
		//IL_0bc4: Unknown result type (might be due to invalid IL or missing references)
		//IL_0bcf: Unknown result type (might be due to invalid IL or missing references)
		//IL_1466: Unknown result type (might be due to invalid IL or missing references)
		//IL_14af: Unknown result type (might be due to invalid IL or missing references)
		//IL_14b4: Unknown result type (might be due to invalid IL or missing references)
		//IL_0382: Unknown result type (might be due to invalid IL or missing references)
		//IL_14f4: Unknown result type (might be due to invalid IL or missing references)
		//IL_14f9: Unknown result type (might be due to invalid IL or missing references)
		//IL_0415: Unknown result type (might be due to invalid IL or missing references)
		//IL_041a: Unknown result type (might be due to invalid IL or missing references)
		//IL_03de: Unknown result type (might be due to invalid IL or missing references)
		//IL_03e3: Unknown result type (might be due to invalid IL or missing references)
		if ((UnityEngine.Object)(object)fighter == (UnityEngine.Object)null)
		{
			fighter = owner;
		}
		if ((UnityEngine.Object)(object)fighter == (UnityEngine.Object)null)
		{
			return false;
		}
		if (fighter.IsDead)
		{
			ReleaseDeadVisitorReservations();
			return false;
		}
#if UNITY_EDITOR
		if (verificationDepartureQueueHold && state == VisitorState.FreeRoaming)
		{
			fighter.StopVisitorMovement();
			return true;
		}
#endif
		if (TickDoorwayEntryYieldForExit())
		{
			return true;
		}
		if (state == VisitorState.ApproachingGymFromVehicle && GymVisitorVehicle.HasActiveParkingArrivalExcept(arrivalVehicle))
		{
			// A vehicle arrival owns the shared parking connector until its
			// complete body reaches the bay. Keep the passenger at the
			// authored staging point so the vehicle can finish without a
			// pedestrian/bumper deadlock; the passenger resumes normally
			// after the arrival reservation is released.
			fighter.StopVisitorMovement();
			if (Time.time >= nextVehicleArrivalWaitLogTime)
			{
				Debug.Log(
					$"GYMCHAOS_VISITOR_WAITING_FOR_ARRIVAL_TRAFFIC enemy={fighter.Identity}",
					this);
				nextVehicleArrivalWaitLogTime = Time.time + 2.5f;
			}
			return true;
		}
		if (IsUsingSharedParkingConnector && !TryAcquireSharedParkingConnectorReservation())
		{
			fighter.StopVisitorMovement();
			return true;
		}
		bool needsEntryReservation = state != VisitorState.ApproachingGymFromVehicle ||
			IsAtDoorwayEntryQueueWindow();
		if (state == VisitorState.ApproachingGymFromVehicle &&
			!vehicleEntrySpawnPending && needsEntryReservation &&
			!TryAcquireDoorwayEntrySlot())
		{
			fighter.StopVisitorMovement();
			return true;
		}
		if (state == VisitorState.ApproachingGymFromVehicle &&
			!needsEntryReservation &&
			(UnityEngine.Object)(object)activeDoorwayEntryAgent ==
				(UnityEngine.Object)(object)this)
		{
			ReleaseDoorwayEntrySlot();
		}
		if ((state == VisitorState.EnteringDoor || state == VisitorState.EnteringRoom) && !TryAcquireDoorwayEntrySlot())
		{
			fighter.StopVisitorMovement();
			return true;
		}
		if (state == VisitorState.ExitingDoor || state == VisitorState.LeavingGym)
		{
			if (!TryAcquireDoorwayExitSlot())
			{
				fighter.StopVisitorMovement();
				return true;
			}
		}
		else if ((UnityEngine.Object)(object)activeDoorwayExitAgent == (UnityEngine.Object)(object)this && IsDoorwayExitAreaClearForReservation())
		{
			ReleaseDoorwayExitSlot();
		}
		if ((UnityEngine.Object)(object)workoutReleaseStation != (UnityEngine.Object)null)
		{
			TickWorkoutStationRelease();
			return true;
		}
		if ((state == VisitorState.ExitingDoor || state == VisitorState.LeavingGym || state == VisitorState.ApproachingVehicle) && !fighter.TickVisitorGroundingForMovement())
		{
			return true;
		}
		if ((UnityEngine.Object)(object)yieldingToVehicle != (UnityEngine.Object)null)
		{
			TickVehicleYield();
			return true;
		}
		Vector3 val;
		switch (state)
		{
		case VisitorState.ApproachingGymFromVehicle:
		{
			if (vehicleEntrySpawnPending)
			{
				vehicleEntrySpawnPending = false;
				fighter.SetVisitorSpawnPose(vehicleEntrySpawnPoint, ((Component)fighter).transform.rotation, keepInterpolationDisabled: true);
				Physics.SyncTransforms();
				return true;
			}
			if (vehicleEntryWaypointIndex >= 2 && (UnityEngine.Object)(object)routeVehicleWithIgnoredCollision != (UnityEngine.Object)null && routeVehicleWithIgnoredCollision.IsBus)
			{
				RestoreRouteVehicleCollision();
			}
			bool entryDetourActive = vehicleDetourActive;
			Vector3? entryLookAhead = entryDetourActive
				? GetVehicleRouteDetourLookAhead()
				: ((vehicleEntryWaypoints != null && vehicleEntryWaypointIndex + 1 < vehicleEntryWaypoints.Length) ? new Vector3?(vehicleEntryWaypoints[vehicleEntryWaypointIndex + 1]) : ((Vector3?)null));
			bool isStorePrecisePoint = !entryDetourActive && vehicleEntryUsesProteinStoreRoute && vehicleEntryWaypoints != null && vehicleEntryWaypointIndex < vehicleEntryWaypoints.Length - 1 && (vehicleEntryUsesDavieBusGate ? (vehicleEntryWaypointIndex >= 5) : (vehicleEntryWaypointIndex >= 3));
			bool isDavieGatePoint = !entryDetourActive && vehicleEntryUsesDavieBusGate && vehicleEntryWaypointIndex >= 1 && vehicleEntryWaypointIndex <= 4;
			bool isRoundedExteriorCorner = !entryDetourActive && vehicleEntryWaypointIndex >= 1 && vehicleEntryWaypointIndex <= 2;
			bool isExteriorDoorPoint = !entryDetourActive && vehicleEntryWaypoints != null && vehicleEntryWaypointIndex == vehicleEntryWaypoints.Length - 1;
			bool num4 = !entryDetourActive && vehicleEntryWaypointIndex == 0;
			bool requiresWallClearanceTurn = !entryDetourActive && !vehicleEntryUsesProteinStoreRoute && !vehicleEntryUsesDavieBusGate && vehicleEntryWaypointIndex == 3;
			bool isStoreGateHandoffPoint = !entryDetourActive && vehicleEntryUsesProteinStoreRoute && !vehicleEntryUsesDavieBusGate && vehicleEntryWaypointIndex >= 31 && vehicleEntryWaypointIndex <= 32;
			if (num4)
			{
				entryLookAhead = null;
			}
			float entryCompletionRadius = entryDetourActive
				? 0.42f
				: (num4 ? 0.9f : (isStoreGateHandoffPoint ? 1.1f : ((!(isStorePrecisePoint || isDavieGatePoint)) ? (isRoundedExteriorCorner ? 0.75f : (requiresWallClearanceTurn ? 0.55f : (isExteriorDoorPoint ? 0.45f : 0.75f))) : (isStorePrecisePoint ? 0.22f : 0.35f))));
			bool stopAtEntryWaypoint = entryDetourActive || isExteriorDoorPoint || (!isStorePrecisePoint && !isDavieGatePoint && !entryLookAhead.HasValue);
			if (MoveAlongAuthoredExteriorRoute(travelTarget, 2.35f, entryLookAhead, entryCompletionRadius, !vehicleEntryUsesProteinStoreRoute && !isStorePrecisePoint && !isDavieGatePoint, stopAtEntryWaypoint))
			{
				if (entryDetourActive)
				{
					AdvanceVehicleRouteDetour();
					return true;
				}
				vehicleEntryWaypointIndex++;
				if (vehicleEntryWaypoints != null && vehicleEntryWaypointIndex < vehicleEntryWaypoints.Length)
				{
					travelTarget = vehicleEntryWaypoints[vehicleEntryWaypointIndex];
				}
				else
				{
					RestoreRouteVehicleCollision();
					RestoreDoorwayWallCollisions();
					RestoreVisitorPlayerBoundaryCollisions();
					fighter.RestoreVisitorPoseInterpolation();
					state = VisitorState.EnteringDoor;
					travelTarget = doorway.InteriorPoint;
				}
			}
			else
			{
				TryRerouteStalledVehicleApproach();
			}
			return true;
		}
		case VisitorState.EnteringDoor:
			if (fighter.MoveVisitorTo(travelTarget, 2.2f, allowOutsideRoom: true))
			{
				RestoreDoorwayWallCollisions();
				state = VisitorState.EnteringRoom;
				entryRoomRecoveryCount = 0;
				PlanEntryRoomRoute();
				roomTravelStalledSeconds = 0f;
				val = Vector3.ProjectOnPlane(travelTarget - fighter.VisitorPhysicsPosition, Vector3.up);
				lastRoomTravelDistance = val.magnitude;
				Debug.Log($"GYMCHAOS_VISITOR_DOOR_CROSSED enemy={fighter.Identity} roomTarget={roomTarget} routePoints={entryRoomWaypoints.Length}", this);
			}
			return true;
		case VisitorState.EnteringRoom:
		{
			val = Vector3.ProjectOnPlane(travelTarget - fighter.VisitorPhysicsPosition, Vector3.up);
			float roomDistance = val.magnitude;
			if (fighter.MoveVisitorTo(travelTarget, 2.15f, allowOutsideRoom: false))
			{
				if (entryRoomWaypoints != null && entryRoomWaypointIndex + 1 < entryRoomWaypoints.Length)
				{
					entryRoomWaypointIndex++;
					travelTarget = entryRoomWaypoints[entryRoomWaypointIndex];
					roomTravelStalledSeconds = 0f;
					val = Vector3.ProjectOnPlane(travelTarget - fighter.VisitorPhysicsPosition, Vector3.up);
					lastRoomTravelDistance = val.magnitude;
					return true;
				}

				entryRoomWaypoints = null;
				entryRoomWaypointIndex = 0;
				if (storeVisitActive && storeVisitReturning)
				{
					ReleaseVehicleApproachReservation();
					ReleaseDoorwayEntrySlot();
					ReleaseDoorwayExitSlot();
					ReleaseDoorOpenRequest();
					state = VisitorState.FreeRoaming;
					enteredGym = true;
					leftGym = false;
					hasSuccessfulEntry = true;
					completedDoorExit = false;
					storeVisitActive = false;
					storeVisitReturning = false;
					storeVisitWaypoints = null;
					storeVisitWaypointIndex = 0;
					roomTravelStalledSeconds = 0f;
					lastRoomTravelDistance = 0f;
					fighter.ResumeVisitorRoaming();
					Debug.Log($"GYMCHAOS_VISITOR_STORE_RETURNED enemy={fighter.Identity}", this);
					return true;
				}

				ReleaseDoorwayEntrySlot();
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
				if (roomTravelStalledSeconds > 2.2f && doorway != null &&
					entryRoomRecoveryCount >= 3 &&
					!GymOutdoorBuilder.IsPlayerOutsideGym(fighter.VisitorPhysicsPosition) &&
					Vector3.ProjectOnPlane(fighter.VisitorPhysicsPosition - doorway.InteriorPoint, Vector3.up).magnitude > 1.5f)
				{
					// The visitor is already inside; its first room point is only
					// a roaming goal. When other members keep standing on it,
					// finish the entry here instead of replanning until the
					// entry times out and the visitor walks back to its vehicle.
					entryRoomWaypoints = null;
					entryRoomWaypointIndex = 0;
					travelTarget = fighter.VisitorPhysicsPosition;
					roomTravelStalledSeconds = 0f;
					Debug.Log($"GYMCHAOS_VISITOR_ENTRY_ACCEPTED_INSIDE enemy={fighter.Identity} attempts={entryRoomRecoveryCount} blocker={fighter.LastVisitorRouteBlocker}", this);
				}
				else if (roomTravelStalledSeconds > 2.2f && doorway != null)
				{
					entryRoomRecoveryCount++;
					PlanEntryRoomRoute();
					val = Vector3.ProjectOnPlane(travelTarget - fighter.VisitorPhysicsPosition, Vector3.up);
					lastRoomTravelDistance = val.magnitude;
					roomTravelStalledSeconds = 0f;
					Debug.LogWarning($"GYMCHAOS_VISITOR_ENTRY_ROUTE_REPLANNED enemy={fighter.Identity} attempts={entryRoomRecoveryCount} target={roomTarget} routePoints={entryRoomWaypoints.Length} blocker={fighter.LastVisitorRouteBlocker}", this);
				}
			}
			return true;
		}
		case VisitorState.ApproachingLockerRoom:
			if (lockerVisitWaypoints == null || lockerVisitWaypointIndex >= lockerVisitWaypoints.Length)
			{
				if (lockerVisitReturning)
				{
					lockerVisitReturning = false;
					GymBackRoomBuilder.ReleaseLockerSlot(fighter.Identity);
					state = VisitorState.FreeRoaming;
					fighter.ResumeVisitorRoaming();
					Debug.Log((object)$"GYMCHAOS_VISITOR_LOCKER_EXITED enemy={fighter.Identity}", (UnityEngine.Object)(object)this);
					return true;
				}
				lockerVisitWaypoints = null;
				lockerVisitWaypointIndex = 0;
				lockerVisitDwellUntil = Time.time + lockerVisitDwellSeconds;
				state = VisitorState.LockerRoomVisit;
				fighter.StopVisitorMovement();
				return true;
			}
			if (fighter.MoveVisitorTo(travelTarget, 1.8f, allowOutsideRoom: true))
			{
				lockerVisitWaypointIndex++;
				if (lockerVisitWaypointIndex < lockerVisitWaypoints.Length)
				{
					travelTarget = lockerVisitWaypoints[lockerVisitWaypointIndex];
					return true;
				}
				bool num3 = lockerVisitReturning;
				lockerVisitWaypoints = null;
				lockerVisitWaypointIndex = 0;
				if (num3)
				{
					lockerVisitReturning = false;
					GymBackRoomBuilder.ReleaseLockerSlot(fighter.Identity);
					state = VisitorState.FreeRoaming;
					fighter.ResumeVisitorRoaming();
					Debug.Log((object)$"GYMCHAOS_VISITOR_LOCKER_EXITED enemy={fighter.Identity}", (UnityEngine.Object)(object)this);
					return true;
				}
				lockerVisitDwellUntil = Time.time + lockerVisitDwellSeconds;
				state = VisitorState.LockerRoomVisit;
				fighter.StopVisitorMovement();
				Debug.Log((object)($"GYMCHAOS_VISITOR_LOCKER_ARRIVED enemy={fighter.Identity} " + $"target={lockerVisitTarget}"), (UnityEngine.Object)(object)this);
				return true;
			}
			return true;
		case VisitorState.LockerRoomVisit:
			fighter.StopVisitorMovement();
			if (Time.time >= lockerVisitDwellUntil)
			{
				lockerVisitDwellUntil = 0f;
				Debug.Log((object)($"GYMCHAOS_VISITOR_LOCKER_COMPLETE enemy={fighter.Identity} " + "label=" + lockerVisitLabel), (UnityEngine.Object)(object)this);
				if (!BeginLockerRoomReturn())
				{
					lockerVisitReturning = false;
					GymBackRoomBuilder.ReleaseLockerSlot(fighter.Identity);
					state = VisitorState.FreeRoaming;
					fighter.ResumeVisitorRoaming();
					Debug.LogWarning((object)$"GYMCHAOS_VISITOR_LOCKER_EXIT_FALLBACK enemy={fighter.Identity}", (UnityEngine.Object)(object)this);
				}
			}
			return true;
		case VisitorState.ApproachingWorkout:
		{
			if (squatStartPending)
			{
				return true;
			}
			if ((UnityEngine.Object)(object)pendingStation == (UnityEngine.Object)null || !pendingStation.IsAvailableForEnemy(fighter) || (UnityEngine.Object)(object)pendingStation.EnemyOccupant != (UnityEngine.Object)(object)fighter)
			{
				CancelWorkoutApproach("station_unavailable");
				return true;
			}
			val = Vector3.ProjectOnPlane(travelTarget - ((Component)fighter).transform.position, Vector3.up);
			float approachDistance = val.magnitude;
			if (fighter.MoveVisitorTo(travelTarget, 1.9f, allowOutsideRoom: false, ((UnityEngine.Object)(object)pendingStation != (UnityEngine.Object)null && pendingStation.IsSquat) ? pendingStation : null))
			{
				if ((UnityEngine.Object)(object)pendingStation != (UnityEngine.Object)null && pendingStation.IsSquat)
				{
					fighter.PrepareVisitorWorkoutPose();
				}
				Vector3 authoredPosition = pendingStation.EnemyPosition;
				if (Vector3.Distance(((Component)fighter).transform.position, authoredPosition) > 0.012f || Quaternion.Angle(((Component)fighter).transform.rotation, pendingStation.EnemyRotation) > 0.5f)
				{
					fighter.SetVisitorSpawnPose(authoredPosition, pendingStation.EnemyRotation, pendingStation.IsSquat);
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
				if ((workoutApproachStalledSeconds > 2.4f || Time.time - workoutApproachStartedAt > 18f) && !TrySwitchToAlternativeSquatStation())
				{
					CancelWorkoutApproach("approach_stalled");
				}
			}
			return true;
		}
		case VisitorState.Squatting:
			fighter.StopVisitorMovement();
			return true;
		case VisitorState.ExitingDoor:
			if (fighter.MoveVisitorTo(travelTarget, 2.2f, allowOutsideRoom: false, targetStation: null, requestedArrivalRadius: -1f, allowStaticCollisionEgress: true))
			{
				if (exitRoomWaypoints != null && exitRoomWaypointIndex + 1 < exitRoomWaypoints.Length)
				{
					exitRoomWaypointIndex++;
					travelTarget = exitRoomWaypoints[exitRoomWaypointIndex];
					ResetDoorwayExitTracking();
					return true;
				}
				exitRoomWaypoints = null;
				exitRoomWaypointIndex = 0;
				if (exitRoomClearPointPending)
				{
					exitRoomClearPointPending = false;
					travelTarget = (((UnityEngine.Object)(object)doorway != (UnityEngine.Object)null) ? doorway.InteriorPoint : travelTarget);
					ResetDoorwayExitTracking();
					return true;
				}
				state = VisitorState.LeavingGym;
				exitRoomWaypoints = BuildExteriorDoorDepartureWaypoints(fighter.VisitorPhysicsPosition.y);
				exitRoomWaypointIndex = 0;
				travelTarget = ((exitRoomWaypoints.Length != 0) ? exitRoomWaypoints[0] : (((UnityEngine.Object)(object)doorway != (UnityEngine.Object)null) ? doorway.ExteriorPoint : travelTarget));
				Debug.Log((object)("GYMCHAOS_VISITOR_EXIT_EXTERIOR_ROUTE_PLANNED " + $"enemy={fighter.Identity} points={exitRoomWaypoints.Length} " + $"target={travelTarget}"), (UnityEngine.Object)(object)this);
				ResetDoorwayExitTracking();
			}
			else
			{
				TryRecoverStalledDoorExit();
			}
			return true;
		case VisitorState.LeavingGym:
		{
			val = Vector3.ProjectOnPlane(GetExteriorDoorClearPoint(fighter.VisitorPhysicsPosition.y) - fighter.VisitorPhysicsPosition, Vector3.up);
			bool reachedExteriorClear = IsDoorwayExitAreaClearForReservation();
			if (reachedExteriorClear || fighter.MoveVisitorTo(travelTarget, 2.2f, allowOutsideRoom: true, null, 0.675f))
			{
				if (reachedExteriorClear)
				{
					fighter.StopVisitorMovement();
				}
				if (!reachedExteriorClear && exitRoomWaypoints != null && exitRoomWaypointIndex + 1 < exitRoomWaypoints.Length)
				{
					exitRoomWaypointIndex++;
					travelTarget = exitRoomWaypoints[exitRoomWaypointIndex];
					ResetDoorwayExitTracking();
					return true;
				}
				RestoreDoorwayWallCollisions();
				exitRoomWaypoints = null;
				exitRoomWaypointIndex = 0;
				if (storeVisitActive)
				{
					ReleaseDoorOpenRequest();
					leftGym = true;
					completedDoorExit = false;
					storeVisitReturning = false;
					storeVisitWaypoints = BuildProteinStoreWaypoints(returning: false);
					storeVisitWaypointIndex = 0;
					state = VisitorState.VisitingProteinStore;
					travelTarget = storeVisitWaypoints[0];
					ClearVehicleRouteDetour();
					ResetVehicleRouteProgress();
					fighter.StopVisitorMovement();
					Debug.Log((object)$"GYMCHAOS_VISITOR_STORE_EXITED_GYM enemy={fighter.Identity}", (UnityEngine.Object)(object)this);
					return true;
				}
				ReleaseDoorwayExitSlot();
				ReleaseDoorOpenRequest();
				state = VisitorState.Dormant;
				enteredGym = false;
				leftGym = true;
				completedDoorExit = true;
				pendingStation = null;
				fighter.StopVisitorMovement();
				Debug.Log((object)$"GYMCHAOS_VISITOR_EXITED enemy={fighter.Identity}", (UnityEngine.Object)(object)this);
			}
			else
			{
				TryRecoverStalledDoorExit();
			}
			return true;
		}
		case VisitorState.VisitingProteinStore:
		{
			if (storeVisitWaypoints == null || storeVisitWaypointIndex >= storeVisitWaypoints.Length)
			{
				state = VisitorState.ProteinStoreDwell;
				storeVisitDwellUntil = Time.time + storeVisitDwellSeconds;
				ReleaseVehicleApproachReservation();
				fighter.StopVisitorMovement();
				return true;
			}
			Vector3? storeLookAhead = vehicleDetourActive ? GetVehicleRouteDetourLookAhead() : ((storeVisitWaypointIndex + 1 < storeVisitWaypoints.Length) ? new Vector3?(storeVisitWaypoints[storeVisitWaypointIndex + 1]) : ((Vector3?)null));
			if (fighter.MoveVisitorAlongExteriorRoute(travelTarget, 2.2f, storeLookAhead, 0.55f, allowWaypointPlaneCrossing: false, !storeLookAhead.HasValue))
			{
				if (vehicleDetourActive)
				{
					AdvanceVehicleRouteDetour();
					return true;
				}
				storeVisitWaypointIndex++;
				if (storeVisitWaypointIndex < storeVisitWaypoints.Length)
				{
					travelTarget = storeVisitWaypoints[storeVisitWaypointIndex];
					ResetVehicleRouteProgress();
				}
				else
				{
					state = VisitorState.ProteinStoreDwell;
					storeVisitDwellUntil = Time.time + storeVisitDwellSeconds;
					ReleaseVehicleApproachReservation();
					fighter.StopVisitorMovement();
					Debug.Log((object)($"GYMCHAOS_VISITOR_STORE_ARRIVED enemy={fighter.Identity} " + $"target={travelTarget}"), (UnityEngine.Object)(object)this);
				}
			}
			else
			{
				TryRerouteStalledVehicleApproach();
			}
			return true;
		}
		case VisitorState.ProteinStoreDwell:
			fighter.StopVisitorMovement();
			if (Time.time >= storeVisitDwellUntil)
			{
				storeVisitReturning = true;
				storeVisitWaypoints = BuildProteinStoreWaypoints(returning: true);
				storeVisitWaypointIndex = 0;
				state = VisitorState.ReturningFromProteinStore;
				travelTarget = storeVisitWaypoints[0];
				ClearVehicleRouteDetour();
				ResetVehicleRouteProgress();
				Debug.Log((object)$"GYMCHAOS_VISITOR_STORE_LEAVING enemy={fighter.Identity}", (UnityEngine.Object)(object)this);
			}
			return true;
		case VisitorState.ReturningFromProteinStore:
		{
			if (storeVisitWaypoints == null || storeVisitWaypointIndex >= storeVisitWaypoints.Length)
			{
				state = VisitorState.EnteringDoor;
				travelTarget = (((UnityEngine.Object)(object)doorway != (UnityEngine.Object)null) ? doorway.InteriorPoint : travelTarget);
				return true;
			}
			Vector3? returnLookAhead = vehicleDetourActive ? GetVehicleRouteDetourLookAhead() : ((storeVisitWaypointIndex + 1 < storeVisitWaypoints.Length) ? new Vector3?(storeVisitWaypoints[storeVisitWaypointIndex + 1]) : ((Vector3?)null));
			if (fighter.MoveVisitorAlongExteriorRoute(travelTarget, 2.2f, returnLookAhead, 0.55f, allowWaypointPlaneCrossing: false, !returnLookAhead.HasValue))
			{
				if (vehicleDetourActive)
				{
					AdvanceVehicleRouteDetour();
					return true;
				}
				storeVisitWaypointIndex++;
				if (storeVisitWaypointIndex < storeVisitWaypoints.Length)
				{
					travelTarget = storeVisitWaypoints[storeVisitWaypointIndex];
					ResetVehicleRouteProgress();
				}
				else
				{
					ReleaseVehicleApproachReservation();
					state = VisitorState.EnteringDoor;
					travelTarget = (((UnityEngine.Object)(object)doorway != (UnityEngine.Object)null) ? doorway.InteriorPoint : travelTarget);
					entryRoomClearPointPending = true;
					ResetDoorwayExitTracking();
					Debug.Log((object)$"GYMCHAOS_VISITOR_STORE_RETURN_DOOR enemy={fighter.Identity}", (UnityEngine.Object)(object)this);
				}
			}
			else
			{
				TryRerouteStalledVehicleApproach();
			}
			return true;
		}
		case VisitorState.ApproachingVehicle:
		{
			if (vehicleExitWaypoints == null || vehicleExitWaypointIndex >= vehicleExitWaypoints.Length)
			{
				CompleteVehicleApproach();
				return true;
			}
			Vector3? exitLookAhead = (vehicleDetourActive ? GetVehicleRouteDetourLookAhead() : ((vehicleExitWaypoints != null && vehicleExitWaypointIndex + 1 < vehicleExitWaypoints.Length) ? new Vector3?(vehicleExitWaypoints[vehicleExitWaypointIndex + 1]) : ((Vector3?)null)));
			bool isFinalVehicleWaypoint = !vehicleDetourActive && vehicleExitWaypoints != null && vehicleExitWaypointIndex == vehicleExitWaypoints.Length - 1;
			bool num = !returningToVehicleAfterEntryAbort && !externalVehicleRouteVerification && !vehicleDetourActive && (vehicleExitUsesProteinStoreRoute ? (vehicleExitWaypointIndex == 0) : (vehicleExitWaypointIndex <= 1));
			bool num2 = !returningToVehicleAfterEntryAbort && vehicleExitUsesProteinStoreRoute && !vehicleDetourActive && vehicleExitWaypointIndex >= 1 && vehicleExitWaypointIndex <= 30;
			bool isStoreParkingTurnPoint = !returningToVehicleAfterEntryAbort && vehicleExitUsesProteinStoreRoute && !vehicleDetourActive && vehicleExitWaypointIndex == 31;
			bool isStoreConnectorPoint = num2 || isStoreParkingTurnPoint;
			bool canUseStoreCornerLookAhead = false;
			float exitCompletionRadius = returningToVehicleAfterEntryAbort
				? (isFinalVehicleWaypoint ? vehicleBoardingRadius : 0.65f)
				: (isFinalVehicleWaypoint ? vehicleBoardingRadius : (isStoreParkingTurnPoint ? 0.7f : (isStoreConnectorPoint ? 0.35f : 0.9f)));
			if (num)
			{
				exitCompletionRadius = 6.5f;
			}
			// The isolated traffic verifier uses a straight road-lane handoff.
			// Keep the capsule in the authored lane until the waypoint is reached;
			// blending the next corner can steer it into the physical wall.
			bool allowVehicleCornerLookAhead = !vehicleDetourActive &&
				!returningToVehicleAfterEntryAbort &&
				!externalVehicleRouteVerification &&
				(!isStoreConnectorPoint || canUseStoreCornerLookAhead);
			if (fighter.MoveVisitorAlongExteriorRoute(travelTarget, 2.35f, exitLookAhead, exitCompletionRadius, allowVehicleCornerLookAhead, returningToVehicleAfterEntryAbort || isFinalVehicleWaypoint || (!isStoreConnectorPoint && !exitLookAhead.HasValue)))
			{
				if (vehicleDetourActive)
				{
					AdvanceVehicleRouteDetour();
					return true;
				}
				vehicleExitWaypointIndex++;
				if (vehicleExitWaypoints != null && vehicleExitWaypointIndex < vehicleExitWaypoints.Length)
				{
					travelTarget = vehicleExitWaypoints[vehicleExitWaypointIndex];
					vehicleTurnPending = vehicleExitWaypointIndex < 4;
					vehicleEntryPending = vehicleExitWaypointIndex < 5;
					vehicleAislePending = vehicleExitWaypointIndex < 6;
					ResetVehicleRouteProgress();
					return true;
				}
				vehicleTurnPending = false;
				vehicleEntryPending = false;
				vehicleAislePending = false;
				CompleteVehicleApproach();
			}
			else
			{
				TryRerouteStalledVehicleApproach();
			}
			return true;
		}
		case VisitorState.Dormant:
			if (leftGym && !enteredGym)
			{
				fighter.StopVisitorMovement();
				return true;
			}
			return false;
		default:
			return false;
		}
	}

	private void ResetDestinationVisitState()
	{
		doorwayEntryYieldingForExit = false;
		deadReservationCleanupComplete = false;
		returningToVehicleAfterEntryAbort = false;
		ReleaseVehicleApproachReservation();
		ReleaseDoorwayEntrySlot();
		ReleaseDoorwayExitSlot();
		if ((UnityEngine.Object)(object)fighter != (UnityEngine.Object)null)
		{
			GymBackRoomBuilder.ReleaseLockerSlot(fighter.Identity);
			GymBackRoomBuilder.HideBenchBagsForVisitor(fighter.Identity);
		}
		storeVisitActive = false;
		storeVisitReturning = false;
		vehicleEntryUsesDavieBusGate = false;
		vehicleEntryUsesProteinStoreRoute = false;
		vehicleExitUsesDavieBusGate = false;
		vehicleExitUsesProteinStoreRoute = false;
		ClearVehicleRouteDetour();
		storeVisitDwellUntil = 0f;
		storeVisitWaypoints = null;
		storeVisitWaypointIndex = 0;
		lockerVisitWaypoints = null;
		lockerVisitWaypointIndex = 0;
		lockerVisitReturning = false;
		exitRoomWaypoints = null;
						exitRoomWaypointIndex = 0;
		entryRoomWaypoints = null;
		entryRoomWaypointIndex = 0;
		entryRoomRecoveryCount = 0;
		lockerVisitDwellUntil = 0f;
		lockerVisitLabel = null;
	}

	private void Update()
	{
		if (state == VisitorState.Squatting && (UnityEngine.Object)(object)squatController != (UnityEngine.Object)null && squatController.IsComplete)
		{
			GymExerciseStation releasedStation = pendingStation;
			squatController.ConsumeCompletion();
			pendingStation = null;
			attemptedWorkoutStations.Clear();
			if (BeginWorkoutStationRelease(releasedStation))
			{
				state = VisitorState.ReleasingWorkout;
				Debug.Log((object)($"GYMCHAOS_SQUAT_RELEASE_STATE enemy={fighter?.Identity} " + $"state={state} station={releasedStation?.EquipmentName}"), (UnityEngine.Object)(object)this);
				return;
			}
			state = VisitorState.FreeRoaming;
			postWorkoutFreeRoamUntil = Time.time + 2.25f;
			fighter?.ResumeVisitorRoaming();
			completedWorkoutVersion++;
			Debug.LogWarning((object)($"GYMCHAOS_SQUAT_WORKOUT_LIFECYCLE_OK enemy={fighter?.Identity} " + "station=" + releasedStation?.EquipmentName + " release=not-needed " + $"version={completedWorkoutVersion} state={state}"), (UnityEngine.Object)(object)this);
		}
	}

	private void LateUpdate()
	{
		if (!squatStartPending)
		{
			return;
		}
		squatStartPending = false;
		if ((UnityEngine.Object)(object)fighter == (UnityEngine.Object)null || fighter.IsDead || state != VisitorState.ApproachingWorkout || (UnityEngine.Object)(object)pendingStation == (UnityEngine.Object)null || !pendingStation.IsAvailableForEnemy(fighter) || (UnityEngine.Object)(object)pendingStation.EnemyOccupant != (UnityEngine.Object)(object)fighter)
		{
			fighter?.ReleaseVisitorWorkoutPose();
			fighter?.RestoreVisitorPoseInterpolation();
			CancelWorkoutApproach("workout_begin_invalidated");
		}
		else if ((UnityEngine.Object)(object)squatController == (UnityEngine.Object)null || !squatController.Begin(pendingStation, fighter, pendingRepetitions, pendingRepDuration))
		{
			fighter.ReleaseVisitorWorkoutPose();
			fighter.RestoreVisitorPoseInterpolation();
			if (!TrySwitchToAlternativeSquatStation())
			{
				CancelWorkoutApproach("workout_begin_failed");
			}
		}
		else
		{
			state = VisitorState.Squatting;
			ResetWorkoutApproachTracking();
		}
	}

	private void OnDisable()
	{
		activeAgents.Remove(this);
		if ((UnityEngine.Object)(object)fighter != (UnityEngine.Object)null)
		{
			GymBackRoomBuilder.ReleaseLockerSlot(fighter.Identity);
			GymBackRoomBuilder.HideBenchBagsForVisitor(fighter.Identity);
		}
		ReleaseDoorwayExitSlot();
		ReleaseDoorwayEntrySlot();
		CancelVehicleYield();
		RestoreRouteVehicleCollision();
		RestoreDoorwayWallCollisions();
		ReleaseDoorOpenRequest();
		squatStartPending = false;
		if (Application.isPlaying && !applicationQuitting && enteredGym && !completedDoorExit)
		{
			Debug.LogError((object)($"GYMCHAOS_VISITOR_DISABLED_INSIDE enemy={fighter?.Identity} " + $"state={state} reason=external_disable"), (UnityEngine.Object)(object)this);
		}
		if ((UnityEngine.Object)(object)squatController != (UnityEngine.Object)null && squatController.IsActive)
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

	private void OnEnable()
	{
		if (!activeAgents.Contains(this))
		{
			activeAgents.Add(this);
		}
	}

	private void OnApplicationQuit()
	{
		applicationQuitting = true;
	}
}
