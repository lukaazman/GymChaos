using System;
using System.Collections.Generic;
using UnityEngine;

public sealed partial class GymVisitorAgent
{
	private Vector3 GetExteriorDoorClearPoint(float y)
	{
		//IL_0006: Unknown result type (might be due to invalid IL or missing references)
		//IL_000b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0012: Unknown result type (might be due to invalid IL or missing references)
		//IL_001d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0022: Unknown result type (might be due to invalid IL or missing references)
		//IL_0027: Unknown result type (might be due to invalid IL or missing references)
		//IL_002c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0031: Unknown result type (might be due to invalid IL or missing references)
		//IL_0046: Unknown result type (might be due to invalid IL or missing references)
		//IL_0049: Unknown result type (might be due to invalid IL or missing references)
		//IL_0053: Unknown result type (might be due to invalid IL or missing references)
		//IL_0058: Unknown result type (might be due to invalid IL or missing references)
		//IL_005d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0066: Unknown result type (might be due to invalid IL or missing references)
		//IL_0040: Unknown result type (might be due to invalid IL or missing references)
		//IL_0045: Unknown result type (might be due to invalid IL or missing references)
		Vector3 exterior = doorway.ExteriorPoint;
		Vector3 outward = Vector3.ProjectOnPlane(doorway.ExteriorPoint - doorway.InteriorPoint, Vector3.up);
		if (outward.sqrMagnitude < 0.01f)
		{
			outward = Vector3.right;
		}
		exterior += outward.normalized * 1.35f;
		exterior.y = y;
		return exterior;
	}
	private Vector3 GetExteriorDoorQueuePoint(float y)
	{
		Vector3 clear = GetExteriorDoorClearPoint(y);
		if (doorway == null)
		{
			clear += Vector3.forward * 1.85f;
			clear.y = y;
			return clear;
		}

		Vector3 outward = Vector3.ProjectOnPlane(doorway.ExteriorPoint - doorway.InteriorPoint, Vector3.up);
		if (outward.sqrMagnitude < 0.01f)
		{
			outward = Vector3.right;
		}
		outward.Normalize();
		Vector3 side = Vector3.Cross(outward, Vector3.up).normalized;
		Vector3 towardParking = Vector3.ProjectOnPlane(GymOutdoorBuilder.VisitorParkingTurnPoint - clear, Vector3.up);
		if (towardParking.sqrMagnitude > 0.01f && Vector3.Dot(side, towardParking) < 0f)
		{
			side = -side;
		}

		Vector3 queuePoint = clear + side * 1.85f;
		queuePoint.y = y;
		return queuePoint;
	}
	private Vector3[] BuildExteriorDoorDepartureWaypoints(float y)
	{
		//IL_0034: Unknown result type (might be due to invalid IL or missing references)
		//IL_003f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0044: Unknown result type (might be due to invalid IL or missing references)
		//IL_0049: Unknown result type (might be due to invalid IL or missing references)
		//IL_004e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0053: Unknown result type (might be due to invalid IL or missing references)
		//IL_000f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0014: Unknown result type (might be due to invalid IL or missing references)
		//IL_0026: Unknown result type (might be due to invalid IL or missing references)
		//IL_0028: Unknown result type (might be due to invalid IL or missing references)
		//IL_0071: Unknown result type (might be due to invalid IL or missing references)
		//IL_0076: Unknown result type (might be due to invalid IL or missing references)
		//IL_0062: Unknown result type (might be due to invalid IL or missing references)
		//IL_0067: Unknown result type (might be due to invalid IL or missing references)
		//IL_008e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0085: Unknown result type (might be due to invalid IL or missing references)
		//IL_0093: Unknown result type (might be due to invalid IL or missing references)
		//IL_009c: Unknown result type (might be due to invalid IL or missing references)
		//IL_009d: Unknown result type (might be due to invalid IL or missing references)
		//IL_009e: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a3: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a8: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ad: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ae: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b5: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b7: Unknown result type (might be due to invalid IL or missing references)
		//IL_00bc: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c1: Unknown result type (might be due to invalid IL or missing references)
		//IL_0110: Unknown result type (might be due to invalid IL or missing references)
		//IL_0111: Unknown result type (might be due to invalid IL or missing references)
		//IL_0117: Unknown result type (might be due to invalid IL or missing references)
		//IL_011c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0121: Unknown result type (might be due to invalid IL or missing references)
		//IL_012c: Unknown result type (might be due to invalid IL or missing references)
		//IL_012e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0130: Unknown result type (might be due to invalid IL or missing references)
		//IL_0135: Unknown result type (might be due to invalid IL or missing references)
		//IL_013f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0140: Unknown result type (might be due to invalid IL or missing references)
		//IL_0147: Unknown result type (might be due to invalid IL or missing references)
		//IL_0149: Unknown result type (might be due to invalid IL or missing references)
		//IL_00d9: Unknown result type (might be due to invalid IL or missing references)
		//IL_00da: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e0: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e5: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ea: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f5: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f7: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f9: Unknown result type (might be due to invalid IL or missing references)
		//IL_00fe: Unknown result type (might be due to invalid IL or missing references)
		//IL_0108: Unknown result type (might be due to invalid IL or missing references)
		//IL_010a: Unknown result type (might be due to invalid IL or missing references)
		if ((UnityEngine.Object)(object)doorway == (UnityEngine.Object)null)
		{
			Vector3 fallback = travelTarget;
			fallback.y = y;
			return (Vector3[])(object)new Vector3[1] { fallback };
		}
		Vector3 outward = Vector3.ProjectOnPlane(doorway.ExteriorPoint - doorway.InteriorPoint, Vector3.up);
		if (outward.sqrMagnitude < 0.01f)
		{
			outward = Vector3.right;
		}
		outward.Normalize();
		Vector3 clear = GetExteriorDoorClearPoint(y);
		Vector3 current = (((UnityEngine.Object)(object)fighter != (UnityEngine.Object)null) ? fighter.VisitorPhysicsPosition : clear);
		current.y = y;
		Vector3 val = Vector3.ProjectOnPlane(current - clear, Vector3.up);
		float outwardProgress = Vector3.Dot(val, outward);
		Vector3 lateralOffset = val - outward * outwardProgress;
		if (outwardProgress > 0.15f && lateralOffset.magnitude <= 1.65f)
		{
			Vector3 forward = current + outward * 1.1f;
			forward.y = y;
			forward = KeepLaneOutsideDoorWall(forward, clear, y);
			return (Vector3[])(object)new Vector3[1] { forward };
		}
		Vector3 outside = clear + outward * 0.9f;
		outside.y = y;
		outside = KeepLaneOutsideDoorWall(outside, clear, y);
		return (Vector3[])(object)new Vector3[2] { clear, outside };
	}
	private Vector3 KeepLaneOutsideDoorWall(Vector3 lane, Vector3 exteriorClear, float y)
	{
		//IL_001e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0029: Unknown result type (might be due to invalid IL or missing references)
		//IL_002e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0033: Unknown result type (might be due to invalid IL or missing references)
		//IL_0038: Unknown result type (might be due to invalid IL or missing references)
		//IL_003d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0040: Unknown result type (might be due to invalid IL or missing references)
		//IL_0045: Unknown result type (might be due to invalid IL or missing references)
		//IL_0016: Unknown result type (might be due to invalid IL or missing references)
		//IL_005a: Unknown result type (might be due to invalid IL or missing references)
		//IL_005b: Unknown result type (might be due to invalid IL or missing references)
		//IL_005c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0061: Unknown result type (might be due to invalid IL or missing references)
		//IL_0054: Unknown result type (might be due to invalid IL or missing references)
		//IL_0059: Unknown result type (might be due to invalid IL or missing references)
		//IL_0087: Unknown result type (might be due to invalid IL or missing references)
		//IL_0070: Unknown result type (might be due to invalid IL or missing references)
		//IL_0071: Unknown result type (might be due to invalid IL or missing references)
		//IL_0073: Unknown result type (might be due to invalid IL or missing references)
		//IL_0078: Unknown result type (might be due to invalid IL or missing references)
		//IL_007d: Unknown result type (might be due to invalid IL or missing references)
		if ((UnityEngine.Object)(object)doorway == (UnityEngine.Object)null)
		{
			lane.y = y;
			return lane;
		}
		Vector3 val = Vector3.ProjectOnPlane(doorway.ExteriorPoint - doorway.InteriorPoint, Vector3.up);
		Vector3 outward = val.normalized;
		if (outward.sqrMagnitude < 0.01f)
		{
			outward = Vector3.right;
		}
		float missingClearance = Vector3.Dot(exteriorClear - lane, outward);
		if (missingClearance > 0f)
		{
			lane += outward * missingClearance;
		}
		lane.y = y;
		return lane;
	}
	private float GetDoorwayOutwardClearanceMeters()
    {
        if ((UnityEngine.Object)(object)doorway == (UnityEngine.Object)null ||
            (UnityEngine.Object)(object)fighter == (UnityEngine.Object)null)
        {
            return float.PositiveInfinity;
        }

        Vector3 outward = Vector3.ProjectOnPlane(
            doorway.ExteriorPoint - doorway.InteriorPoint, Vector3.up);
        if (outward.sqrMagnitude < 0.01f)
        {
            return float.PositiveInfinity;
        }

        Vector3 fromExterior = Vector3.ProjectOnPlane(
            fighter.VisitorPhysicsPosition - doorway.ExteriorPoint, Vector3.up);
        return Vector3.Dot(fromExterior, outward.normalized);
    }
	private bool IsPhysicallyInDoorwayTraversalArea()
	{
		if ((UnityEngine.Object)(object)doorway == (UnityEngine.Object)null ||
			(UnityEngine.Object)(object)fighter == (UnityEngine.Object)null)
		{
			return false;
		}

		Vector3 interior = doorway.InteriorPoint;
		Vector3 exterior = doorway.ExteriorPoint;
		Vector3 axis = Vector3.ProjectOnPlane(exterior - interior, Vector3.up);
		float length = axis.magnitude;
		if (length < 0.01f)
		{
			return false;
		}

		axis /= length;
		Vector3 fromInterior = Vector3.ProjectOnPlane(
			fighter.VisitorPhysicsPosition - interior, Vector3.up);
		float along = Vector3.Dot(fromInterior, axis);
		Vector3 lateral = fromInterior - axis * along;
		return along >= -1.5f && along <= length + 1.5f &&
			lateral.magnitude <= DoorwayBodyRadius * 1.65f;
	}
    private const float DoorwayExitReservationRadius = 6f;
    private bool doorwayExitReservationLatched;

    private bool IsDoorwayExitAreaClearForReservation()
    {
        // Keep right-of-way while the visitor is inside the doorway. Use signed
        // outward progress so an indoor position cannot look clear just because
        // it is far from the exterior point. Latch clearance for this departure
        // to prevent the shared reservation from flapping along the parking route.
        if (state == VisitorState.ExitingDoor)
        {
            // Only the doorway itself is shared. A visitor still walking to it
            // from deep inside the gym (or boxed in on the way) used to hold
            // the reservation for the whole walk, so one stuck visitor kept
            // every other departure waiting forever.
            // Once inside the radius the hold latches for this departure, so a
            // holder near the edge cannot flap between held and clear.
            if (!doorwayExitReservationLatched && doorway != null && fighter != null &&
                Vector3.ProjectOnPlane(fighter.VisitorPhysicsPosition - doorway.InteriorPoint, Vector3.up)
                    .sqrMagnitude <= DoorwayExitReservationRadius * DoorwayExitReservationRadius)
            {
                doorwayExitReservationLatched = true;
            }
            return !doorwayExitReservationLatched;
        }

        float outwardClearance = GetDoorwayOutwardClearanceMeters();
        if (state == VisitorState.LeavingGym && outwardClearance >= 0.85f)
        {
            doorwayExitClearanceReleased = true;
        }
        return doorwayExitClearanceReleased || outwardClearance >= 0.85f;
    }
    private bool IsAtDoorwayEntryQueueWindow()
	{
		int queueWindowSize = (vehicleEntryUsesProteinStoreRoute ? 4 : 2);
		if (vehicleEntryWaypoints != null)
		{
			return vehicleEntryWaypointIndex >= Mathf.Max(0, vehicleEntryWaypoints.Length - queueWindowSize);
		}
		return false;
	}
	private bool IsDoorwayEntryAreaOccupied()
	{
		if (state == VisitorState.EnteringDoor)
		{
			return true;
		}

		if (state == VisitorState.EnteringRoom && doorway != null && fighter != null)
		{
			Vector3 fromDoor = Vector3.ProjectOnPlane(
				fighter.VisitorPhysicsPosition - doorway.InteriorPoint, Vector3.up);
			return fromDoor.sqrMagnitude < 2.2f * 2.2f;
		}

		// Reserve the narrow shared connector only when an arriving visitor is
		// close enough to enter. Once the capsule clears the doorway, the next
		// visitor may proceed without waiting for the first visitor's workout.
		// The last route points can still be far up the path (the bus gate
		// route); only a visitor actually near the door occupies it, so one
		// stalled further away no longer holds everyone else in the queue.
		return state == VisitorState.ApproachingGymFromVehicle && IsAtDoorwayEntryQueueWindow() &&
			IsNearDoorwayExterior(DoorwayEntryOccupiedRadius);
	}
	private const float DoorwayEntryOccupiedRadius = 6f;
	private bool IsNearDoorwayExterior(float radius)
	{
		if (doorway == null || fighter == null)
		{
			return true;
		}
		return Vector3.ProjectOnPlane(fighter.VisitorPhysicsPosition - doorway.ExteriorPoint, Vector3.up)
			.sqrMagnitude <= radius * radius;
	}
	private bool TryAcquireDoorwayEntrySlot()
	{
		// Keep the short doorway segment clear, while allowing an inbound
		// visitor to enter once a connector owner is physically elsewhere on
		// the route. The exterior movement probe still handles later crossings.
		if ((UnityEngine.Object)(object)activeVehicleApproachAgent != (UnityEngine.Object)null &&
			(UnityEngine.Object)(object)activeVehicleApproachAgent != (UnityEngine.Object)(object)this &&
			((Behaviour)activeVehicleApproachAgent).isActiveAndEnabled &&
			activeVehicleApproachAgent.IsUsingSharedParkingConnector &&
			IsNearDoorwayConnector(activeVehicleApproachAgent) &&
			!IsBlockingConnectorOwner(activeVehicleApproachAgent))
		{
			return false;
		}
		if ((UnityEngine.Object)(object)activeDoorwayExitAgent != (UnityEngine.Object)null && (UnityEngine.Object)(object)activeDoorwayExitAgent != (UnityEngine.Object)(object)this && ((Behaviour)activeDoorwayExitAgent).isActiveAndEnabled && !activeDoorwayExitAgent.IsDoorwayExitAreaClearForReservation())
		{
			return false;
		}
		if ((UnityEngine.Object)(object)activeDoorwayEntryAgent != (UnityEngine.Object)null && (UnityEngine.Object)(object)activeDoorwayEntryAgent != (UnityEngine.Object)(object)this && (!((Behaviour)activeDoorwayEntryAgent).isActiveAndEnabled || !activeDoorwayEntryAgent.IsDoorwayEntryAreaOccupied()))
		{
			activeDoorwayEntryAgent = null;
		}
		if ((UnityEngine.Object)(object)activeDoorwayEntryAgent == (UnityEngine.Object)null)
		{
			activeDoorwayEntryAgent = this;
		}
		return (UnityEngine.Object)(object)activeDoorwayEntryAgent == (UnityEngine.Object)(object)this;
	}
	private bool IsNearDoorwayConnector(GymVisitorAgent candidate)
	{
		if (candidate == null || candidate.fighter == null || doorway == null)
		{
			return candidate != null;
		}

		Vector3 interior = doorway.InteriorPoint;
		Vector3 exterior = doorway.ExteriorPoint;
		Vector3 outward = Vector3.ProjectOnPlane(exterior - interior, Vector3.up);
		if (outward.sqrMagnitude < 0.001f)
		{
			return true;
		}
		outward.Normalize();

		// Include the authored queue approach outside the doorway, so an actor
		// waiting on the same narrow segment still receives right-of-way.
		Vector3 start = exterior + outward * DoorwayClearance;
		Vector3 end = interior;
		start.y = 0f;
		end.y = 0f;
		Vector3 position = candidate.fighter.VisitorPhysicsPosition;
		position.y = 0f;
		Vector3 segment = end - start;
		float segmentLengthSquared = segment.sqrMagnitude;
		float along = segmentLengthSquared > 0.001f
			? Mathf.Clamp01(Vector3.Dot(position - start, segment) / segmentLengthSquared)
			: 0f;
		Vector3 closest = start + segment * along;
		float combinedBodyRadius = DoorwayBodyRadius * 2f + 0.1f;
		return (position - closest).sqrMagnitude <= combinedBodyRadius * combinedBodyRadius;
	}
	private void ReleaseDoorwayEntrySlot()
	{
		if ((UnityEngine.Object)(object)activeDoorwayEntryAgent == (UnityEngine.Object)(object)this)
		{
			activeDoorwayEntryAgent = null;
		}
		else if ((UnityEngine.Object)(object)activeDoorwayEntryAgent == (UnityEngine.Object)null || !((Behaviour)activeDoorwayEntryAgent).isActiveAndEnabled || !activeDoorwayEntryAgent.IsDoorwayEntryAreaOccupied())
		{
			activeDoorwayEntryAgent = null;
		}
	}
	private bool TryAcquireDoorwayExitSlot()
	{
		// Once the actor physically crosses the outward doorway plane, release
		// its right-of-way for the rest of this departure.
		if (state == VisitorState.LeavingGym && IsDoorwayExitAreaClearForReservation())
		{
			ReleaseDoorwayExitSlot();
			return true;
		}

		GymVisitorAgent entryToYield = null;
		if ((UnityEngine.Object)(object)activeDoorwayEntryAgent != (UnityEngine.Object)null &&
			(UnityEngine.Object)(object)activeDoorwayEntryAgent != (UnityEngine.Object)(object)this &&
			((Behaviour)activeDoorwayEntryAgent).isActiveAndEnabled &&
			activeDoorwayEntryAgent.IsDoorwayEntryAreaOccupied())
		{
			// An incoming visitor waiting in the exterior queue yields to a
			// departure. Visitors already inside keep the shared doorway.
			if (activeDoorwayEntryAgent.state != VisitorState.ApproachingGymFromVehicle)
			{
				return false;
			}
			entryToYield = activeDoorwayEntryAgent;
		}
		if ((UnityEngine.Object)(object)activeDoorwayExitAgent != (UnityEngine.Object)null &&
			(UnityEngine.Object)(object)activeDoorwayExitAgent != (UnityEngine.Object)(object)this &&
			((Behaviour)activeDoorwayExitAgent).isActiveAndEnabled &&
			!activeDoorwayExitAgent.IsDoorwayExitAreaClearForReservation())
		{
			return false;
		}
		if ((UnityEngine.Object)(object)activeDoorwayExitAgent == (UnityEngine.Object)null ||
			!((Behaviour)activeDoorwayExitAgent).isActiveAndEnabled ||
			activeDoorwayExitAgent.IsDoorwayExitAreaClearForReservation())
		{
			activeDoorwayExitAgent = this;
		}
		bool acquired = (UnityEngine.Object)(object)activeDoorwayExitAgent == (UnityEngine.Object)(object)this;
		if (acquired && entryToYield != null)
		{
			if (!entryToYield.doorwayEntryYieldingForExit &&
				!doorwayExitPriorityLogIssued)
			{
				Debug.Log(
					$"GYMCHAOS_VISITOR_DOOR_EXIT_PRIORITY_GRANTED enemy={fighter?.Identity} " +
					$"yielding={entryToYield.fighter?.Identity}", this);
				doorwayExitPriorityLogIssued = true;
			}
			entryToYield.BeginDoorwayEntryYieldForExit();
		}
		return acquired;
	}
	private void BeginDoorwayEntryYieldForExit()
	{
		if (doorwayEntryYieldingForExit || state != VisitorState.ApproachingGymFromVehicle ||
			vehicleEntryWaypoints == null || vehicleEntryWaypoints.Length < 2)
		{
			return;
		}

		int queueIndex = vehicleEntryWaypoints.Length - 2;
		if (vehicleEntryWaypointIndex > queueIndex)
		{
			vehicleEntryWaypointIndex = queueIndex;
			travelTarget = vehicleEntryWaypoints[queueIndex];
			vehicleDetourActive = false;
			vehicleDetourResumeTarget = Vector3.zero;
			ResetVehicleRouteProgress();
		}
		doorwayEntryYieldingForExit = true;
		Debug.Log(
			$"GYMCHAOS_VISITOR_DOOR_ENTRY_YIELD enemy={fighter?.Identity} target={travelTarget}",
			this);
	}
	private bool TickDoorwayEntryYieldForExit()
	{
		if (!doorwayEntryYieldingForExit)
		{
			return false;
		}

		GymVisitorAgent exitAgent = activeDoorwayExitAgent;
		if ((UnityEngine.Object)(object)exitAgent == (UnityEngine.Object)null ||
			!exitAgent.isActiveAndEnabled || exitAgent.IsDoorwayExitAreaClearForReservation())
		{
			doorwayEntryYieldingForExit = false;
			Debug.Log($"GYMCHAOS_VISITOR_DOOR_ENTRY_RESUME enemy={fighter?.Identity}", this);
			return false;
		}

		if (state != VisitorState.ApproachingGymFromVehicle || fighter == null || fighter.IsDead)
		{
			doorwayEntryYieldingForExit = false;
			return false;
		}

		fighter.MoveVisitorAlongExteriorRoute(
			travelTarget,
			2.2f,
			null,
			0.42f,
			allowWaypointPlaneCrossing: false,
			stopAtDestination: true);
		return true;
	}
	private void ReleaseDoorwayExitSlot()
	{
		if ((UnityEngine.Object)(object)activeDoorwayExitAgent == (UnityEngine.Object)(object)this)
		{
			activeDoorwayExitAgent = null;
		}
		else if ((UnityEngine.Object)(object)activeDoorwayExitAgent == (UnityEngine.Object)null || !((Behaviour)activeDoorwayExitAgent).isActiveAndEnabled)
		{
			activeDoorwayExitAgent = null;
		}
	}
	public void AbortEntryAndReturnThroughDoor(GymDoorway door)
	{
		if (fighter == null || door == null || !IsEntryPending) return;
		bool returnToVehicle = state == VisitorState.ApproachingGymFromVehicle &&
			vehicleEntryWaypoints != null && vehicleEntryWaypoints.Length > 0;
		Vector3[] returnVehicleWaypoints = null;
		if (returnToVehicle)
		{
			List<Vector3> reverseRoute = new List<Vector3>(vehicleEntryWaypoints.Length + 1);
			int lastCompletedWaypoint = Mathf.Min(vehicleEntryWaypointIndex - 1, vehicleEntryWaypoints.Length - 1);
			for (int i = lastCompletedWaypoint; i >= 0; i--)
			{
				reverseRoute.Add(vehicleEntryWaypoints[i]);
			}
			if (reverseRoute.Count == 0 ||
				Vector3.ProjectOnPlane(reverseRoute[reverseRoute.Count - 1] - vehicleEntrySpawnPoint, Vector3.up).sqrMagnitude > 0.04f)
			{
				reverseRoute.Add(vehicleEntrySpawnPoint);
			}
			returnVehicleWaypoints = reverseRoute.ToArray();
		}

		if (!returnToVehicle)
		{
			RestoreRouteVehicleCollision();
			RestoreVisitorPlayerBoundaryCollisions();
		}
		RestoreDoorwayWallCollisions();
		GymBackRoomBuilder.HideBenchBagsForVisitor(fighter.Identity);
		doorway = door;
		hasDoorwayClearPoint = false;
		if (returnToVehicle)
		{
			ReleaseDoorOpenRequest();
		}
		else
		{
			HoldDoorOpenRequest();
			SetDoorwayWallCollisionsIgnored(ignored: true);
		}
		pendingStation = null;
		enteredGym = false;
		leftGym = false;
		hasSuccessfulEntry = false;
		completedDoorExit = false;
		entryRoomClearPointPending = false;
		entryRoomWaypoints = null;
		entryRoomWaypointIndex = 0;
		returningToVehicleAfterEntryAbort = returnToVehicle;
		if (state == VisitorState.EnteringDoor)
		{
							exitRoomClearPointPending = false;
			travelTarget = doorway.ExteriorPoint;
			state = VisitorState.LeavingGym;
		}
		else if (returnToVehicle)
		{
							exitRoomClearPointPending = false;
			exitRoomWaypoints = null;
							exitRoomWaypointIndex = 0;
			vehicleExitWaypoints = returnVehicleWaypoints;
			vehicleExitWaypointIndex = 0;
			vehicleExitUsesProteinStoreRoute = false;
			vehicleExitUsesDavieBusGate = false;
			vehicleBoardingRadius = 0.65f;
			travelTarget = vehicleExitWaypoints[0];
			state = VisitorState.ApproachingVehicle;
		}
		else
		{
							exitRoomClearPointPending = false;
			exitRoomWaypoints = BuildIndoorVisitorRoute(doorway.InteriorPoint);
							exitRoomWaypointIndex = 0;
							travelTarget = exitRoomWaypoints[0];
			doorwayExitReservationLatched = false;
			state = VisitorState.ExitingDoor;
		}
		fighter.StopVisitorMovement();
		ResetDoorwayExitTracking();
		Debug.LogWarning(
			$"GYMCHAOS_VISITOR_ENTRY_ABORT_RETURNING enemy={fighter.Identity} route={(returnToVehicle ? "vehicle" : "door")}",
			this);
	}
	private Vector3[] BuildIndoorDoorExitRoute()
	{
		if (doorway == null || fighter == null)
		{
			return BuildIndoorVisitorRoute(
				doorway != null ? doorway.InteriorPoint : travelTarget);
		}

		Vector3 clearPoint = GetDoorwayClearPoint();
		Vector3[] route = BuildIndoorVisitorRoute(clearPoint);
		List<Vector3> waypoints = new List<Vector3>(route);
		if (waypoints.Count == 0 ||
			Vector3.ProjectOnPlane(
				waypoints[waypoints.Count - 1] - clearPoint, Vector3.up).magnitude > 0.5f)
		{
			waypoints.Add(clearPoint);
		}

		Vector3 last = waypoints[waypoints.Count - 1];
		if (Vector3.ProjectOnPlane(
			last - doorway.InteriorPoint, Vector3.up).magnitude > 0.5f)
		{
			waypoints.Add(doorway.InteriorPoint);
		}

		Debug.Log(
			$"GYMCHAOS_VISITOR_DOOR_EXIT_APPROACH enemy={fighter.Identity} " +
			$"clearPoint={clearPoint} interior={doorway.InteriorPoint} " +
			$"points={waypoints.Count} finalSegmentClear=" +
			$"{IsVisitorStaticRouteSegmentClear(clearPoint, doorway.InteriorPoint)}", this);
		return waypoints.ToArray();
	}
	private Vector3 GetDoorwayRoomStagingTarget(Vector3 requestedTarget)
	{
		//IL_0016: Unknown result type (might be due to invalid IL or missing references)
		//IL_0021: Unknown result type (might be due to invalid IL or missing references)
		//IL_0026: Unknown result type (might be due to invalid IL or missing references)
		//IL_002b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0030: Unknown result type (might be due to invalid IL or missing references)
		//IL_0035: Unknown result type (might be due to invalid IL or missing references)
		//IL_000e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0047: Unknown result type (might be due to invalid IL or missing references)
		//IL_004e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0058: Unknown result type (might be due to invalid IL or missing references)
		//IL_005d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0062: Unknown result type (might be due to invalid IL or missing references)
		//IL_0065: Unknown result type (might be due to invalid IL or missing references)
		//IL_0070: Unknown result type (might be due to invalid IL or missing references)
		//IL_0044: Unknown result type (might be due to invalid IL or missing references)
		if ((UnityEngine.Object)(object)doorway == (UnityEngine.Object)null)
		{
			return requestedTarget;
		}
		Vector3 inward = Vector3.ProjectOnPlane(doorway.InteriorPoint - doorway.ExteriorPoint, Vector3.up);
		if (inward.sqrMagnitude < 0.01f)
		{
			return requestedTarget;
		}
		Vector3 stagingTarget = GetDoorwayClearPoint() + inward.normalized * 3.6f;
		stagingTarget.y = requestedTarget.y;
		return stagingTarget;
	}
	private Vector3 GetDoorwayClearPoint()
	{
		//IL_004e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0053: Unknown result type (might be due to invalid IL or missing references)
		//IL_005a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0065: Unknown result type (might be due to invalid IL or missing references)
		//IL_006a: Unknown result type (might be due to invalid IL or missing references)
		//IL_006f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0074: Unknown result type (might be due to invalid IL or missing references)
		//IL_0079: Unknown result type (might be due to invalid IL or missing references)
		//IL_0042: Unknown result type (might be due to invalid IL or missing references)
		//IL_0033: Unknown result type (might be due to invalid IL or missing references)
		//IL_0022: Unknown result type (might be due to invalid IL or missing references)
		//IL_009f: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a4: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a5: Unknown result type (might be due to invalid IL or missing references)
		//IL_00aa: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ae: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b3: Unknown result type (might be due to invalid IL or missing references)
		//IL_0089: Unknown result type (might be due to invalid IL or missing references)
		//IL_008a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0096: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c2: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c7: Unknown result type (might be due to invalid IL or missing references)
		//IL_00db: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e0: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e1: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e6: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f7: Unknown result type (might be due to invalid IL or missing references)
		//IL_0104: Unknown result type (might be due to invalid IL or missing references)
		//IL_010f: Unknown result type (might be due to invalid IL or missing references)
		//IL_011c: Unknown result type (might be due to invalid IL or missing references)
		//IL_015d: Unknown result type (might be due to invalid IL or missing references)
		//IL_015e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0164: Unknown result type (might be due to invalid IL or missing references)
		//IL_0169: Unknown result type (might be due to invalid IL or missing references)
		//IL_016e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0170: Unknown result type (might be due to invalid IL or missing references)
		//IL_0171: Unknown result type (might be due to invalid IL or missing references)
		//IL_0177: Unknown result type (might be due to invalid IL or missing references)
		//IL_017c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0181: Unknown result type (might be due to invalid IL or missing references)
		//IL_0184: Unknown result type (might be due to invalid IL or missing references)
		//IL_01a0: Unknown result type (might be due to invalid IL or missing references)
		//IL_018e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0190: Unknown result type (might be due to invalid IL or missing references)
		//IL_019c: Unknown result type (might be due to invalid IL or missing references)
		//IL_01bc: Unknown result type (might be due to invalid IL or missing references)
		//IL_01be: Unknown result type (might be due to invalid IL or missing references)
		//IL_01ca: Unknown result type (might be due to invalid IL or missing references)
		//IL_01aa: Unknown result type (might be due to invalid IL or missing references)
		//IL_01ac: Unknown result type (might be due to invalid IL or missing references)
		//IL_01b8: Unknown result type (might be due to invalid IL or missing references)
		if ((UnityEngine.Object)(object)doorway == (UnityEngine.Object)null)
		{
			if (!((UnityEngine.Object)(object)fighter != (UnityEngine.Object)null))
			{
				return ((Component)this).transform.position;
			}
			return ((Component)fighter).transform.position;
		}
		if (hasDoorwayClearPoint)
		{
			return doorwayClearPoint;
		}
		Vector3 inside = doorway.InteriorPoint;
		Vector3 inward = Vector3.ProjectOnPlane(doorway.InteriorPoint - doorway.ExteriorPoint, Vector3.up);
		if (inward.sqrMagnitude < 0.01f)
		{
			doorwayClearPoint = inside;
			hasDoorwayClearPoint = true;
			return inside;
		}
		inward.Normalize();
		Vector3 val = Vector3.Cross(Vector3.up, inward);
		Vector3 lateral = val.normalized;
		if (lateral.sqrMagnitude < 0.01f)
		{
			lateral = Vector3.forward;
		}
		Bounds deskBounds;
		bool num = TryGetReceptionDeskBounds(out deskBounds);
		float deskLateralOffset = (num ? Vector3.Dot(deskBounds.center - inside, lateral) : 0f);
		float deskLateralExtent = (num ? (Mathf.Abs(lateral.x) * deskBounds.extents.x + Mathf.Abs(lateral.z) * deskBounds.extents.z) : 0f);
		float lateralOffset = Mathf.Max(1.35f, Mathf.Abs(deskLateralOffset) + deskLateralExtent + 0.3f);
		float preferredSide = ((deskLateralOffset > 0.05f) ? (-1f) : 1f);
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
		doorwayClearPoint = first;
		hasDoorwayClearPoint = true;
		return first;
	}
	private bool IsDoorwayPointClear(Vector3 point)
	{
		//IL_0010: Unknown result type (might be due to invalid IL or missing references)
		//IL_0011: Unknown result type (might be due to invalid IL or missing references)
		//IL_001b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0020: Unknown result type (might be due to invalid IL or missing references)
		//IL_0025: Unknown result type (might be due to invalid IL or missing references)
		//IL_0026: Unknown result type (might be due to invalid IL or missing references)
		//IL_0030: Unknown result type (might be due to invalid IL or missing references)
		//IL_0035: Unknown result type (might be due to invalid IL or missing references)
		//IL_003a: Unknown result type (might be due to invalid IL or missing references)
		//IL_003b: Unknown result type (might be due to invalid IL or missing references)
		if ((UnityEngine.Object)(object)fighter == (UnityEngine.Object)null)
		{
			return true;
		}
		Vector3 val = point + Vector3.up * 0.59125006f;
		Vector3 upper = point + Vector3.up * 1.9887501f;
		int count = Physics.OverlapCapsuleNonAlloc(val, upper, 0.6235f, doorwayPointHits, -1, (QueryTriggerInteraction)1);
		for (int i = 0; i < count; i++)
		{
			Collider hit = doorwayPointHits[i];
			if (!((UnityEngine.Object)(object)hit == (UnityEngine.Object)null) && !((UnityEngine.Object)(object)((Component)hit).transform == (UnityEngine.Object)(object)((Component)this).transform) && !((Component)hit).transform.IsChildOf(((Component)this).transform) && !((UnityEngine.Object)(object)((Component)hit).GetComponentInParent<GymDoorway>() != (UnityEngine.Object)null) && !HasRoomFloorInHierarchy(((Component)hit).transform) && !IsWalkableFloorSurface(hit))
			{
				if (!((UnityEngine.Object)(object)((Component)hit).GetComponentInParent<EnemyFighter>() != (UnityEngine.Object)null))
				{
					_ = (UnityEngine.Object)(object)((Component)hit).GetComponentInParent<PlayerMovement>() != (UnityEngine.Object)null;
				}
				return false;
			}
		}
		return true;
	}
	private static bool TryGetReceptionDeskBounds(out Bounds bounds)
	{
		//IL_0022: Unknown result type (might be due to invalid IL or missing references)
		//IL_005b: Unknown result type (might be due to invalid IL or missing references)
		//IL_004a: Unknown result type (might be due to invalid IL or missing references)
		//IL_004f: Unknown result type (might be due to invalid IL or missing references)
		GameObject desk = GameObject.Find("Reception desk");
		Renderer[] renderers = (((UnityEngine.Object)(object)desk != (UnityEngine.Object)null) ? desk.GetComponentsInChildren<Renderer>(true) : null);
		bool found = false;
		bounds = default(Bounds);
		if (renderers == null)
		{
			return false;
		}
		Renderer[] array = renderers;
		foreach (Renderer renderer in array)
		{
			if (!((UnityEngine.Object)(object)renderer == (UnityEngine.Object)null))
			{
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
		}
		return found;
	}
	private void SetDoorwayWallCollisionsIgnored(bool ignored)
	{
		if ((UnityEngine.Object)(object)fighter == (UnityEngine.Object)null)
		{
			doorwayWallCollisionsIgnored = false;
			return;
		}
		Collider[] visitorColliders = ((Component)fighter).GetComponentsInChildren<Collider>(true);
		Collider[] array = UnityEngine.Object.FindObjectsByType<Collider>((FindObjectsInactive)1, (FindObjectsSortMode)0);
		foreach (Collider wall in array)
		{
			if (!IsDoorwayWallColliderForRouting(wall))
			{
				continue;
			}
			Collider[] array2 = visitorColliders;
			foreach (Collider visitor in array2)
			{
				if ((UnityEngine.Object)(object)visitor != (UnityEngine.Object)null)
				{
					Physics.IgnoreCollision(visitor, wall, ignored);
				}
			}
		}
		doorwayWallCollisionsIgnored = ignored;
	}
	private void RestoreDoorwayWallCollisions()
	{
		if (doorwayWallCollisionsIgnored)
		{
			SetDoorwayWallCollisionsIgnored(ignored: false);
		}
	}
	public static bool IsDoorwayWallColliderForRouting(Collider collider)
	{
		if ((UnityEngine.Object)(object)collider == (UnityEngine.Object)null)
		{
			return false;
		}
		string name = ((UnityEngine.Object)((Component)collider).gameObject).name;
		if (!(name == "East Wall South") && !(name == "East Wall North"))
		{
			return name == "East Wall Above Visitor Door";
		}
		return true;
	}
	private void HoldDoorOpenRequest()
	{
		if ((UnityEngine.Object)(object)doorway != (UnityEngine.Object)null && !doorOpenRequestHeld)
		{
			doorway.RequestOpen();
			doorOpenRequestHeld = true;
		}
	}
	private void ReleaseDoorOpenRequest()
	{
		if ((UnityEngine.Object)(object)doorway != (UnityEngine.Object)null && doorOpenRequestHeld)
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
		//IL_001e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0029: Unknown result type (might be due to invalid IL or missing references)
		//IL_002e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0033: Unknown result type (might be due to invalid IL or missing references)
		//IL_0038: Unknown result type (might be due to invalid IL or missing references)
		//IL_003d: Unknown result type (might be due to invalid IL or missing references)
		//IL_008a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0095: Unknown result type (might be due to invalid IL or missing references)
		//IL_009a: Unknown result type (might be due to invalid IL or missing references)
		//IL_009f: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a4: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a9: Unknown result type (might be due to invalid IL or missing references)
		//IL_00be: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c9: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ce: Unknown result type (might be due to invalid IL or missing references)
		//IL_00d3: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e2: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e7: Unknown result type (might be due to invalid IL or missing references)
		//IL_0139: Unknown result type (might be due to invalid IL or missing references)
		//IL_0131: Unknown result type (might be due to invalid IL or missing references)
		//IL_013e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0144: Unknown result type (might be due to invalid IL or missing references)
		//IL_019c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0226: Unknown result type (might be due to invalid IL or missing references)
		//IL_022b: Unknown result type (might be due to invalid IL or missing references)
		//IL_039a: Unknown result type (might be due to invalid IL or missing references)
		//IL_035b: Unknown result type (might be due to invalid IL or missing references)
		//IL_027f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0233: Unknown result type (might be due to invalid IL or missing references)
		//IL_03de: Unknown result type (might be due to invalid IL or missing references)
		//IL_03c6: Unknown result type (might be due to invalid IL or missing references)
		//IL_03d0: Unknown result type (might be due to invalid IL or missing references)
		//IL_0380: Unknown result type (might be due to invalid IL or missing references)
		//IL_0385: Unknown result type (might be due to invalid IL or missing references)
		//IL_025c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0261: Unknown result type (might be due to invalid IL or missing references)
		//IL_03e3: Unknown result type (might be due to invalid IL or missing references)
		//IL_0429: Unknown result type (might be due to invalid IL or missing references)
		//IL_0449: Unknown result type (might be due to invalid IL or missing references)
		//IL_02e9: Unknown result type (might be due to invalid IL or missing references)
		//IL_02db: Unknown result type (might be due to invalid IL or missing references)
		//IL_02b3: Unknown result type (might be due to invalid IL or missing references)
		//IL_02b8: Unknown result type (might be due to invalid IL or missing references)
		//IL_02ee: Unknown result type (might be due to invalid IL or missing references)
		//IL_02fd: Unknown result type (might be due to invalid IL or missing references)
		//IL_0302: Unknown result type (might be due to invalid IL or missing references)
		if ((UnityEngine.Object)(object)fighter == (UnityEngine.Object)null || (UnityEngine.Object)(object)doorway == (UnityEngine.Object)null)
		{
			return;
		}
		Vector3 val = Vector3.ProjectOnPlane(travelTarget - fighter.VisitorPhysicsPosition, Vector3.up);
		float distance = val.magnitude;
		if (distance < lastDoorwayExitDistance - 0.025f)
		{
			lastDoorwayExitDistance = distance;
			doorwayExitStalledSeconds = 0f;
			return;
		}
		doorwayExitStalledSeconds += Time.fixedDeltaTime;
		if (doorwayExitStalledSeconds <= 0.75f)
		{
			return;
		}
		Vector3 direction = Vector3.ProjectOnPlane(travelTarget - fighter.VisitorPhysicsPosition, Vector3.up);
		if (direction.sqrMagnitude < 0.001f)
		{
			direction = doorway.ExteriorPoint - doorway.InteriorPoint;
		}
		if (direction.sqrMagnitude < 0.001f)
		{
			direction = Vector3.forward;
		}
		string stalledRouteBlocker = fighter.LastVisitorRouteBlocker ?? string.Empty;
		bool stalledDynamicCharacterBlocker =
			stalledRouteBlocker.IndexOf("Player", StringComparison.OrdinalIgnoreCase) >= 0 ||
			stalledRouteBlocker.IndexOf("hitbox", StringComparison.OrdinalIgnoreCase) >= 0;
		Vector3[] stalledCharacterBypassRoute = null;
		bool hasStalledCharacterBypassRoute = false;
		// ExitingDoor is the narrowest part of the route. Refresh the local
		// character bypass even when the last movement probe has already
		// overwritten its diagnostic blocker with "none".
		if (stalledDynamicCharacterBlocker || state == VisitorState.ExitingDoor)
		{
			Vector3 bypassDestination = state == VisitorState.ExitingDoor && doorway != null
				? doorway.InteriorPoint
				: travelTarget;
			if (TryBuildPlayerBypassRoute(
				bypassDestination, out stalledCharacterBypassRoute))
			{
				hasStalledCharacterBypassRoute = true;
			}
			else if (stalledDynamicCharacterBlocker)
			{
				fighter.StopVisitorMovement();
				lastDoorwayExitDistance = distance;
				doorwayExitStalledSeconds = 0f;
				Debug.Log((object)($"GYMCHAOS_VISITOR_EXIT_WAITING_FOR_CHARACTER enemy={fighter.Identity} " + $"state={state} position={fighter.VisitorPhysicsPosition} " + $"blocker={stalledRouteBlocker} distance={distance:0.00}"), (UnityEngine.Object)(object)this);
				return;
			}
		}
		doorwayExitRecoveryCount++;
		fighter.SetVisitorPushesLooseItems(doorwayExitRecoveryCount >= 3);
		if (doorwayExitRecoveryCount >= 4)
		{
			fighter.TryVisitorCrowdPassRouteBlocker();
		}
		string recoveryMode = "reroute_no_teleport";
		if (state == VisitorState.ExitingDoor)
		{
			string routeBlocker = fighter.LastVisitorRouteBlocker ?? string.Empty;
			bool num = routeBlocker.IndexOf("Player", StringComparison.OrdinalIgnoreCase) >= 0 || routeBlocker.IndexOf("hitbox", StringComparison.OrdinalIgnoreCase) >= 0;
			Vector3 exitClearPoint = doorway.InteriorPoint;
			Vector3[] characterBypassRoute = null;
			if (hasStalledCharacterBypassRoute)
			{
				characterBypassRoute = stalledCharacterBypassRoute;
				exitRoomClearPointPending = false;
				exitRoomWaypoints = characterBypassRoute;
				exitRoomWaypointIndex = 0;
				travelTarget = exitRoomWaypoints[0];
				recoveryMode = "character_bypass_no_teleport";
			}
			else if (num && TryBuildPlayerBypassRoute(exitClearPoint, out characterBypassRoute))
			{
				exitRoomClearPointPending = false;
				exitRoomWaypoints = characterBypassRoute;
				exitRoomWaypointIndex = 0;
				travelTarget = exitRoomWaypoints[0];
								recoveryMode = "character_bypass_no_teleport";
			}
			else
			{
				Vector3[] reroute = BuildIndoorDoorExitRoute();
				if (reroute.Length > 0)
				{
					exitRoomWaypoints = reroute;
					exitRoomWaypointIndex = 0;
					exitRoomClearPointPending = false;
					travelTarget = exitRoomWaypoints[0];
				}
				else
				{
					hasDoorwayClearPoint = false;
					exitRoomClearPointPending = true;
					travelTarget = ((doorwayExitRecoveryCount > 1) ? GetDoorwayExitRecoveryPoint(doorwayExitRecoveryCount) : GetDoorwayClearPoint());
					exitRoomWaypoints = (Vector3[])(object)new Vector3[1] { travelTarget };
					exitRoomWaypointIndex = 0;
				}
			}
		}
		else
		{
			string routeBlocker2 = fighter.LastVisitorRouteBlocker ?? string.Empty;
			bool num2 = routeBlocker2.IndexOf("Player", StringComparison.OrdinalIgnoreCase) >= 0 || routeBlocker2.IndexOf("hitbox", StringComparison.OrdinalIgnoreCase) >= 0;
			Vector3[] characterBypassRoute2 = null;
			if (num2 && TryBuildPlayerBypassRoute(travelTarget, out characterBypassRoute2))
			{
				exitRoomWaypoints = characterBypassRoute2;
				exitRoomWaypointIndex = 0;
				travelTarget = exitRoomWaypoints[0];
								recoveryMode = "character_bypass_no_teleport";
			}
			else
			{
				exitRoomWaypoints = BuildExteriorDoorDepartureWaypoints(fighter.VisitorPhysicsPosition.y);
				exitRoomWaypointIndex = 0;
				travelTarget = ((exitRoomWaypoints.Length != 0) ? exitRoomWaypoints[0] : GetExteriorDoorClearPoint(fighter.VisitorPhysicsPosition.y));
				recoveryMode = "exterior_clear_route_no_teleport";
			}
		}
		fighter.StopVisitorMovement();
		Debug.LogWarning((object)($"GYMCHAOS_VISITOR_EXIT_ROUTE_FALLBACK enemy={fighter.Identity} " + $"state={state} position={fighter.VisitorPhysicsPosition} " + $"blocker={fighter.LastVisitorRouteBlocker} target={travelTarget} " + $"recovery={doorwayExitRecoveryCount} mode={recoveryMode}"), (UnityEngine.Object)(object)this);
		ResetDoorwayExitTracking();
	}
	private Vector3 GetDoorwayExitRecoveryPoint(int recovery)
	{
		//IL_0001: Unknown result type (might be due to invalid IL or missing references)
		//IL_0006: Unknown result type (might be due to invalid IL or missing references)
		//IL_000d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0018: Unknown result type (might be due to invalid IL or missing references)
		//IL_001d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0022: Unknown result type (might be due to invalid IL or missing references)
		//IL_0027: Unknown result type (might be due to invalid IL or missing references)
		//IL_002c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0048: Unknown result type (might be due to invalid IL or missing references)
		//IL_004d: Unknown result type (might be due to invalid IL or missing references)
		//IL_004e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0053: Unknown result type (might be due to invalid IL or missing references)
		//IL_0057: Unknown result type (might be due to invalid IL or missing references)
		//IL_005c: Unknown result type (might be due to invalid IL or missing references)
		//IL_003b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0040: Unknown result type (might be due to invalid IL or missing references)
		//IL_006b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0070: Unknown result type (might be due to invalid IL or missing references)
		//IL_009d: Unknown result type (might be due to invalid IL or missing references)
		//IL_009e: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a4: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a9: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ae: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b4: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b9: Unknown result type (might be due to invalid IL or missing references)
		//IL_00be: Unknown result type (might be due to invalid IL or missing references)
		//IL_00cd: Unknown result type (might be due to invalid IL or missing references)
		//IL_00dd: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e9: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ea: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f0: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f5: Unknown result type (might be due to invalid IL or missing references)
		//IL_00fa: Unknown result type (might be due to invalid IL or missing references)
		//IL_0100: Unknown result type (might be due to invalid IL or missing references)
		//IL_0105: Unknown result type (might be due to invalid IL or missing references)
		//IL_010a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0119: Unknown result type (might be due to invalid IL or missing references)
		//IL_0129: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e6: Unknown result type (might be due to invalid IL or missing references)
		//IL_0135: Unknown result type (might be due to invalid IL or missing references)
		//IL_0132: Unknown result type (might be due to invalid IL or missing references)
		Vector3 basePoint = GetDoorwayClearPoint();
		Vector3 inward = Vector3.ProjectOnPlane(doorway.InteriorPoint - doorway.ExteriorPoint, Vector3.up);
		if (inward.sqrMagnitude < 0.01f)
		{
			inward = Vector3.right;
		}
		inward.Normalize();
		Vector3 val = Vector3.Cross(Vector3.up, inward);
		Vector3 lateral = val.normalized;
		if (lateral.sqrMagnitude < 0.01f)
		{
			lateral = Vector3.forward;
		}
		int ring = Mathf.Clamp((recovery - 2) / 2 + 1, 1, 4);
		float side = (((recovery & 1) == 0) ? 1f : (-1f));
		float lateralStep = 0.55f * (float)ring;
		Vector3 first = basePoint + inward * 0.85f + lateral * (side * lateralStep);
		first.y = ((Component)fighter).transform.position.y;
		if (IsDoorwayPointClear(first))
		{
			return first;
		}
		Vector3 second = basePoint + inward * 1.15f - lateral * (side * lateralStep);
		second.y = ((Component)fighter).transform.position.y;
		if (!IsDoorwayPointClear(second))
		{
			return first;
		}
		return second;
	}
}
