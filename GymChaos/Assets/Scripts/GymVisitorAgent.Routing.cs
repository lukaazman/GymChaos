using System;
using System.Collections.Generic;
using UnityEngine;

public sealed partial class GymVisitorAgent
{
	public bool BeginProteinStoreVisit(GymDoorway door, float dwellSeconds = 3.2f)
	{
		//IL_004c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0052: Unknown result type (might be due to invalid IL or missing references)
		//IL_0057: Unknown result type (might be due to invalid IL or missing references)
		//IL_005c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0061: Unknown result type (might be due to invalid IL or missing references)
		//IL_0066: Unknown result type (might be due to invalid IL or missing references)
		//IL_007d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0084: Unknown result type (might be due to invalid IL or missing references)
		//IL_008e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0093: Unknown result type (might be due to invalid IL or missing references)
		//IL_0098: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ae: Unknown result type (might be due to invalid IL or missing references)
		//IL_0075: Unknown result type (might be due to invalid IL or missing references)
		//IL_007a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0137: Unknown result type (might be due to invalid IL or missing references)
		if ((UnityEngine.Object)(object)fighter == (UnityEngine.Object)null || (UnityEngine.Object)(object)door == (UnityEngine.Object)null || state != VisitorState.FreeRoaming || !IsInsideGym || fighter.IsDead || fighter.IsOnTreadmill || !GymOutdoorBuilder.HasProteinStoreRoute)
		{
			return false;
		}
		Vector3 inward = Vector3.ProjectOnPlane(door.InteriorPoint - door.ExteriorPoint, Vector3.up);
		if (inward.sqrMagnitude < 0.01f)
		{
			inward = Vector3.back;
		}
		storeReturnRoomTarget = door.InteriorPoint + inward.normalized * 3.6f;
		storeReturnRoomTarget.y = ((Component)fighter).transform.position.y;
		GymBackRoomBuilder.HideBenchBagsForVisitor(fighter.Identity);
		BeginExit(door);
		if (state != VisitorState.ExitingDoor)
		{
			return false;
		}
		storeVisitActive = true;
		storeVisitReturning = false;
		storeVisitDwellSeconds = Mathf.Max(0.8f, dwellSeconds);
		storeVisitDwellUntil = 0f;
		storeVisitWaypoints = null;
		storeVisitWaypointIndex = 0;
		Debug.Log((object)($"GYMCHAOS_VISITOR_STORE_REQUESTED enemy={fighter.Identity} " + $"target={GymOutdoorBuilder.ProteinStoreFrontClearPoint} " + $"dwell={storeVisitDwellSeconds:F1}"), (UnityEngine.Object)(object)this);
		return true;
	}
	private static int FindInitialRouteWaypoint(Vector3[] route, Vector3 start, float handoffRadius)
	{
		//IL_0012: Unknown result type (might be due to invalid IL or missing references)
		//IL_0017: Unknown result type (might be due to invalid IL or missing references)
		//IL_0018: Unknown result type (might be due to invalid IL or missing references)
		//IL_001d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0022: Unknown result type (might be due to invalid IL or missing references)
		//IL_0027: Unknown result type (might be due to invalid IL or missing references)
		int index;
		for (index = 0; index + 1 < route.Length; index++)
		{
			Vector3 val = Vector3.ProjectOnPlane(route[index] - start, Vector3.up);
			if (!(val.sqrMagnitude <= handoffRadius * handoffRadius))
			{
				break;
			}
		}
		return index;
	}
	private Vector3[] BuildProteinStoreWaypoints(bool returning)
	{
		//IL_0026: Unknown result type (might be due to invalid IL or missing references)
		//IL_0031: Unknown result type (might be due to invalid IL or missing references)
		//IL_0036: Unknown result type (might be due to invalid IL or missing references)
		//IL_0037: Unknown result type (might be due to invalid IL or missing references)
		//IL_003c: Unknown result type (might be due to invalid IL or missing references)
		//IL_003d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0042: Unknown result type (might be due to invalid IL or missing references)
		//IL_0043: Unknown result type (might be due to invalid IL or missing references)
		//IL_0048: Unknown result type (might be due to invalid IL or missing references)
		//IL_004a: Unknown result type (might be due to invalid IL or missing references)
		//IL_004f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0051: Unknown result type (might be due to invalid IL or missing references)
		//IL_0056: Unknown result type (might be due to invalid IL or missing references)
		//IL_006f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0066: Unknown result type (might be due to invalid IL or missing references)
		//IL_0074: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f3: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f4: Unknown result type (might be due to invalid IL or missing references)
		//IL_00fb: Unknown result type (might be due to invalid IL or missing references)
		//IL_00fc: Unknown result type (might be due to invalid IL or missing references)
		//IL_0103: Unknown result type (might be due to invalid IL or missing references)
		//IL_0104: Unknown result type (might be due to invalid IL or missing references)
		//IL_010b: Unknown result type (might be due to invalid IL or missing references)
		//IL_010d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0114: Unknown result type (might be due to invalid IL or missing references)
		//IL_0116: Unknown result type (might be due to invalid IL or missing references)
		//IL_011d: Unknown result type (might be due to invalid IL or missing references)
		//IL_011f: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b9: Unknown result type (might be due to invalid IL or missing references)
		//IL_00bb: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c2: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c4: Unknown result type (might be due to invalid IL or missing references)
		//IL_00cb: Unknown result type (might be due to invalid IL or missing references)
		//IL_00cc: Unknown result type (might be due to invalid IL or missing references)
		//IL_00d3: Unknown result type (might be due to invalid IL or missing references)
		//IL_00d4: Unknown result type (might be due to invalid IL or missing references)
		//IL_00db: Unknown result type (might be due to invalid IL or missing references)
		//IL_00dc: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e3: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e5: Unknown result type (might be due to invalid IL or missing references)
		float y = (((UnityEngine.Object)(object)fighter != (UnityEngine.Object)null) ? ((Component)fighter).transform.position.y : roomTarget.y);
		Vector3 approach = GymOutdoorBuilder.ProteinStoreVisitApproachPoint;
		Vector3 front = GymOutdoorBuilder.ProteinStoreFrontClearPoint;
		Vector3 gymPath = GymOutdoorBuilder.ProteinStoreGymPathClearPoint;
		Vector3 exterior = (((UnityEngine.Object)(object)doorway != (UnityEngine.Object)null) ? doorway.ExteriorPoint : gymPath);
		approach.y = y;
		front.y = y;
		gymPath.y = y;
		exterior.y = y;
		// Straight through the fenced entry walkway: the old dog-leg through
		// the yard south of the shop is closed off.
		if (returning)
		{
			return (Vector3[])(object)new Vector3[3] { approach, gymPath, exterior };
		}
		return (Vector3[])(object)new Vector3[3] { gymPath, approach, front };
	}
	private bool MoveAlongAuthoredExteriorRoute(Vector3 target, float speed, Vector3? nextWaypoint = null, float requestedCompletionRadius = -1f)
	{
		//IL_0006: Unknown result type (might be due to invalid IL or missing references)
		return fighter.MoveVisitorAlongExteriorRoute(target, speed, nextWaypoint, requestedCompletionRadius);
	}
	private bool MoveAlongAuthoredExteriorRoute(Vector3 target, float speed, Vector3? nextWaypoint, float requestedCompletionRadius, bool allowWaypointPlaneCrossing, bool stopAtDestination)
	{
		//IL_0006: Unknown result type (might be due to invalid IL or missing references)
		return fighter.MoveVisitorAlongExteriorRoute(target, speed, nextWaypoint, requestedCompletionRadius, allowWaypointPlaneCrossing, stopAtDestination);
	}
	private bool IsExternalRouteSegmentClear(Vector3 start, Vector3 end)
	{
		Vector3 segment = Vector3.ProjectOnPlane(end - start, Vector3.up);
		float distance = segment.magnitude;
		if (distance < 0.2f) return true;
		return fighter.IsVisitorExternalPathClearFrom(
			start, segment / distance, distance);
	}
	private static Vector3 ResolveSafeExteriorLane(Vector3 authoredPoint, float y, float routeVariation = 0.5f)
	{
		//IL_0000: Unknown result type (might be due to invalid IL or missing references)
		//IL_0025: Unknown result type (might be due to invalid IL or missing references)
		//IL_002a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0036: Unknown result type (might be due to invalid IL or missing references)
		//IL_00cc: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ab: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b0: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b4: Unknown result type (might be due to invalid IL or missing references)
		//IL_0048: Unknown result type (might be due to invalid IL or missing references)
		//IL_004d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0051: Unknown result type (might be due to invalid IL or missing references)
		//IL_0065: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f8: Unknown result type (might be due to invalid IL or missing references)
		//IL_00fe: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e0: Unknown result type (might be due to invalid IL or missing references)
		//IL_007d: Unknown result type (might be due to invalid IL or missing references)
		float safeX = authoredPoint.x;
		GameObject northWall = GameObject.Find("North Wall Lower");
		Collider wallCollider = (((UnityEngine.Object)(object)northWall != (UnityEngine.Object)null) ? northWall.GetComponent<Collider>() : null);
		Bounds accessible = GymOutdoorBuilder.AccessibleBounds;
		Bounds bounds;
		if ((UnityEngine.Object)(object)wallCollider != (UnityEngine.Object)null && accessible.size.x > 2f)
		{
			bounds = wallCollider.bounds;
			float minLaneX = bounds.max.x + 1.15f;
			float maxLaneX = accessible.max.x - 1.1f;
			safeX = ((maxLaneX > minLaneX) ? Mathf.Lerp(minLaneX, maxLaneX, Mathf.Clamp01(routeVariation)) : Mathf.Clamp(authoredPoint.x, maxLaneX, minLaneX));
		}
		else
		{
			if ((UnityEngine.Object)(object)wallCollider != (UnityEngine.Object)null)
			{
				float num = safeX;
				bounds = wallCollider.bounds;
				safeX = Mathf.Max(num, bounds.max.x + 1.15f);
			}
			if (accessible.size.x > 2f)
			{
				safeX = Mathf.Min(safeX, accessible.max.x - 1.1f);
			}
		}
		return new Vector3(safeX, y, authoredPoint.z);
	}
	private Vector3[] BuildIndoorVisitorRoute(Vector3 destination)
	{
		List<Vector3> route = new List<Vector3>();
		bool built = fighter != null && fighter.TryBuildVisitorRoute(destination, route);
		if (!built || route.Count == 0)
		{
			route.Clear();
			route.Add(destination);
		}
		return route.ToArray();
	}
	private void PlanEntryRoomRoute()
	{
		Vector3 destination = storeVisitActive && storeVisitReturning
			? storeReturnRoomTarget
			: roomTarget;
		entryRoomWaypoints = BuildIndoorVisitorRoute(destination);
		entryRoomWaypointIndex = 0;
		travelTarget = entryRoomWaypoints[0];
		Debug.Log(
			$"GYMCHAOS_VISITOR_ENTRY_ROOM_ROUTE enemy={fighter?.Identity} " +
			$"storeReturn={storeVisitActive && storeVisitReturning} " +
			$"destination={destination} points={entryRoomWaypoints.Length}", this);
	}
	private bool TryBuildPlayerBypassRoute(Vector3 destination, out Vector3[] route)
	{
		//IL_0019: Unknown result type (might be due to invalid IL or missing references)
		//IL_001e: Unknown result type (might be due to invalid IL or missing references)
		//IL_001f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0020: Unknown result type (might be due to invalid IL or missing references)
		//IL_0021: Unknown result type (might be due to invalid IL or missing references)
		//IL_0026: Unknown result type (might be due to invalid IL or missing references)
		//IL_002b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0030: Unknown result type (might be due to invalid IL or missing references)
		//IL_0043: Unknown result type (might be due to invalid IL or missing references)
		//IL_0048: Unknown result type (might be due to invalid IL or missing references)
		//IL_0049: Unknown result type (might be due to invalid IL or missing references)
		//IL_004e: Unknown result type (might be due to invalid IL or missing references)
		//IL_004f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0054: Unknown result type (might be due to invalid IL or missing references)
		//IL_0082: Unknown result type (might be due to invalid IL or missing references)
		//IL_0087: Unknown result type (might be due to invalid IL or missing references)
		//IL_0257: Unknown result type (might be due to invalid IL or missing references)
		//IL_0258: Unknown result type (might be due to invalid IL or missing references)
		//IL_025a: Unknown result type (might be due to invalid IL or missing references)
		//IL_025f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0264: Unknown result type (might be due to invalid IL or missing references)
		//IL_0269: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c6: Unknown result type (might be due to invalid IL or missing references)
		//IL_00cb: Unknown result type (might be due to invalid IL or missing references)
		//IL_015a: Unknown result type (might be due to invalid IL or missing references)
		//IL_015f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0163: Unknown result type (might be due to invalid IL or missing references)
		//IL_016e: Unknown result type (might be due to invalid IL or missing references)
		//IL_016f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0171: Unknown result type (might be due to invalid IL or missing references)
		//IL_0173: Unknown result type (might be due to invalid IL or missing references)
		//IL_0174: Unknown result type (might be due to invalid IL or missing references)
		//IL_0179: Unknown result type (might be due to invalid IL or missing references)
		//IL_017e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0183: Unknown result type (might be due to invalid IL or missing references)
		//IL_0199: Unknown result type (might be due to invalid IL or missing references)
		//IL_019a: Unknown result type (might be due to invalid IL or missing references)
		//IL_019e: Unknown result type (might be due to invalid IL or missing references)
		//IL_01a3: Unknown result type (might be due to invalid IL or missing references)
		//IL_01a8: Unknown result type (might be due to invalid IL or missing references)
		//IL_01aa: Unknown result type (might be due to invalid IL or missing references)
		//IL_01ac: Unknown result type (might be due to invalid IL or missing references)
		//IL_01ae: Unknown result type (might be due to invalid IL or missing references)
		//IL_01b3: Unknown result type (might be due to invalid IL or missing references)
		//IL_01b8: Unknown result type (might be due to invalid IL or missing references)
		//IL_01bd: Unknown result type (might be due to invalid IL or missing references)
		//IL_03ec: Unknown result type (might be due to invalid IL or missing references)
		//IL_03f7: Unknown result type (might be due to invalid IL or missing references)
		//IL_03fc: Unknown result type (might be due to invalid IL or missing references)
		//IL_0401: Unknown result type (might be due to invalid IL or missing references)
		//IL_0406: Unknown result type (might be due to invalid IL or missing references)
		//IL_040b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0210: Unknown result type (might be due to invalid IL or missing references)
		//IL_0212: Unknown result type (might be due to invalid IL or missing references)
		//IL_058d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0593: Unknown result type (might be due to invalid IL or missing references)
		//IL_05a3: Unknown result type (might be due to invalid IL or missing references)
		//IL_0425: Unknown result type (might be due to invalid IL or missing references)
		//IL_042a: Unknown result type (might be due to invalid IL or missing references)
		//IL_042c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0431: Unknown result type (might be due to invalid IL or missing references)
		//IL_0435: Unknown result type (might be due to invalid IL or missing references)
		//IL_043a: Unknown result type (might be due to invalid IL or missing references)
		//IL_030e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0310: Unknown result type (might be due to invalid IL or missing references)
		//IL_0319: Unknown result type (might be due to invalid IL or missing references)
		//IL_031e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0323: Unknown result type (might be due to invalid IL or missing references)
		//IL_0329: Unknown result type (might be due to invalid IL or missing references)
		//IL_032e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0333: Unknown result type (might be due to invalid IL or missing references)
		//IL_0337: Unknown result type (might be due to invalid IL or missing references)
		//IL_0342: Unknown result type (might be due to invalid IL or missing references)
		//IL_0344: Unknown result type (might be due to invalid IL or missing references)
		//IL_0345: Unknown result type (might be due to invalid IL or missing references)
		//IL_034a: Unknown result type (might be due to invalid IL or missing references)
		//IL_034f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0354: Unknown result type (might be due to invalid IL or missing references)
		//IL_0365: Unknown result type (might be due to invalid IL or missing references)
		//IL_036e: Unknown result type (might be due to invalid IL or missing references)
		//IL_036f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0371: Unknown result type (might be due to invalid IL or missing references)
		//IL_037c: Unknown result type (might be due to invalid IL or missing references)
		//IL_037e: Unknown result type (might be due to invalid IL or missing references)
		//IL_037f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0393: Unknown result type (might be due to invalid IL or missing references)
		//IL_0395: Unknown result type (might be due to invalid IL or missing references)
		//IL_039c: Unknown result type (might be due to invalid IL or missing references)
		//IL_039d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0492: Unknown result type (might be due to invalid IL or missing references)
		//IL_0497: Unknown result type (might be due to invalid IL or missing references)
		//IL_04a1: Unknown result type (might be due to invalid IL or missing references)
		//IL_04a6: Unknown result type (might be due to invalid IL or missing references)
		//IL_04ab: Unknown result type (might be due to invalid IL or missing references)
		//IL_04b2: Unknown result type (might be due to invalid IL or missing references)
		//IL_04b7: Unknown result type (might be due to invalid IL or missing references)
		//IL_04bc: Unknown result type (might be due to invalid IL or missing references)
		//IL_04c0: Unknown result type (might be due to invalid IL or missing references)
		//IL_04cb: Unknown result type (might be due to invalid IL or missing references)
		//IL_04cd: Unknown result type (might be due to invalid IL or missing references)
		//IL_04ce: Unknown result type (might be due to invalid IL or missing references)
		//IL_04d3: Unknown result type (might be due to invalid IL or missing references)
		//IL_04d8: Unknown result type (might be due to invalid IL or missing references)
		//IL_04dd: Unknown result type (might be due to invalid IL or missing references)
		//IL_04ee: Unknown result type (might be due to invalid IL or missing references)
		//IL_04f7: Unknown result type (might be due to invalid IL or missing references)
		//IL_04f8: Unknown result type (might be due to invalid IL or missing references)
		//IL_04fa: Unknown result type (might be due to invalid IL or missing references)
		//IL_0505: Unknown result type (might be due to invalid IL or missing references)
		//IL_0507: Unknown result type (might be due to invalid IL or missing references)
		//IL_0508: Unknown result type (might be due to invalid IL or missing references)
		//IL_051c: Unknown result type (might be due to invalid IL or missing references)
		//IL_051e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0525: Unknown result type (might be due to invalid IL or missing references)
		//IL_0526: Unknown result type (might be due to invalid IL or missing references)
		route = null;
		if ((UnityEngine.Object)(object)fighter == (UnityEngine.Object)null)
		{
			return false;
		}
		Vector3 current = fighter.VisitorPhysicsPosition;
		Vector3 toDestination = Vector3.ProjectOnPlane(destination - current, Vector3.up);
		if (toDestination.sqrMagnitude < 0.25f)
		{
			return false;
		}
		Vector3 travelDirection = toDestination.normalized;
		Vector3 lateral = Vector3.Cross(Vector3.up, travelDirection);
		if (lateral.sqrMagnitude < 0.001f)
		{
			return false;
		}
		lateral.Normalize();
		string blockerName = fighter.LastVisitorRouteBlocker ?? string.Empty;
		Vector3 blockerPosition = Vector3.zero;
		float blockerRadius = 0.48f;
		bool foundBlocker = false;
		Vector3 val;
		if (blockerName.IndexOf("Player", StringComparison.OrdinalIgnoreCase) >= 0)
		{
			PlayerMovement player = UnityEngine.Object.FindFirstObjectByType<PlayerMovement>();
			if ((UnityEngine.Object)(object)player == (UnityEngine.Object)null || player.IsDead)
			{
				return false;
			}
			blockerPosition = ((Component)player).transform.position;
			CharacterController playerController = ((Component)player).GetComponent<CharacterController>();
			if ((UnityEngine.Object)(object)playerController != (UnityEngine.Object)null)
			{
				blockerRadius = Mathf.Max(playerController.radius, 0.36f);
			}
			foundBlocker = true;
		}
		else if (blockerName.IndexOf("hitbox", StringComparison.OrdinalIgnoreCase) >= 0)
		{
			EnemyFighter[] array = UnityEngine.Object.FindObjectsByType<EnemyFighter>((FindObjectsInactive)1, (FindObjectsSortMode)0);
			float bestScore = float.PositiveInfinity;
			EnemyFighter[] array2 = array;
			foreach (EnemyFighter candidate in array2)
			{
				if ((UnityEngine.Object)(object)candidate == (UnityEngine.Object)null || (UnityEngine.Object)(object)candidate == (UnityEngine.Object)(object)fighter || candidate.IsDead)
				{
					continue;
				}
				Vector3 candidatePosition = candidate.VisitorPhysicsPosition;
				candidatePosition.y = current.y;
				Vector3 segment = toDestination;
				float lineT = Mathf.Clamp01(Vector3.Dot(Vector3.ProjectOnPlane(candidatePosition - current, Vector3.up), segment) / segment.sqrMagnitude);
				Vector3 closest = current + segment * lineT;
				val = Vector3.ProjectOnPlane(candidatePosition - closest, Vector3.up);
				float lineDistance = val.magnitude;
				float blockingDistance = EnemyFighter.GetBodyRadiusForIdentity(fighter.Identity) + EnemyFighter.GetBodyRadiusForIdentity(candidate.Identity) + 0.55f;
				if (!(lineDistance > blockingDistance + 1.5f))
				{
					float score = lineDistance + lineT * 0.05f;
					if (score < bestScore)
					{
						bestScore = score;
						blockerPosition = candidatePosition;
						blockerRadius = EnemyFighter.GetBodyRadiusForIdentity(candidate.Identity);
						foundBlocker = true;
					}
				}
			}
		}
		if (!foundBlocker && blockerName.IndexOf("Player", StringComparison.OrdinalIgnoreCase) < 0 &&
			blockerName.IndexOf("hitbox", StringComparison.OrdinalIgnoreCase) < 0)
		{
			// The movement probe can reset its diagnostic string to "none"
			// after a physics tick even while the player is still the live
			// blocker at the doorway. Refresh the blocker from the scene before
			// giving up on a no-teleport character bypass.
			PlayerMovement nearbyPlayer = UnityEngine.Object.FindFirstObjectByType<PlayerMovement>();
			if ((UnityEngine.Object)(object)nearbyPlayer != (UnityEngine.Object)null && !nearbyPlayer.IsDead)
			{
				Vector3 playerPosition = ((Component)nearbyPlayer).transform.position;
				playerPosition.y = current.y;
				Vector3 playerOffset = Vector3.ProjectOnPlane(
					playerPosition - current, Vector3.up);
				float lineT = Mathf.Clamp01(Vector3.Dot(playerOffset, toDestination) /
					toDestination.sqrMagnitude);
				Vector3 closest = current + toDestination * lineT;
				float lineDistance = Vector3.ProjectOnPlane(
					playerPosition - closest, Vector3.up).magnitude;
				CharacterController playerController =
					((Component)nearbyPlayer).GetComponent<CharacterController>();
				float livePlayerRadius =
					(UnityEngine.Object)(object)playerController != (UnityEngine.Object)null
						? Mathf.Max(playerController.radius, 0.36f)
						: 0.48f;
				float blockingDistance =
					EnemyFighter.GetBodyRadiusForIdentity(fighter.Identity) +
					livePlayerRadius + 0.55f;
				float currentDistance = playerOffset.magnitude;
				if ((lineDistance <= blockingDistance + 1.5f &&
					lineT > -0.25f && lineT < 1.25f) ||
					currentDistance <= blockingDistance + 0.8f)
				{
					blockerName = "Player owner=Player";
					blockerPosition = playerPosition;
					blockerRadius = livePlayerRadius;
					foundBlocker = true;
				}
			}
		}
		if (!foundBlocker)
		{
			return false;
		}
		float clearance = EnemyFighter.GetBodyRadiusForIdentity(fighter.Identity) + blockerRadius + 0.55f;
		float currentSide = Vector3.Dot(Vector3.ProjectOnPlane(current - blockerPosition, Vector3.up), lateral);
		int preferredSide = ((!(Mathf.Abs(currentSide) > 0.1f)) ? (((doorwayExitRecoveryCount & 1) == 0) ? 1 : (-1)) : ((currentSide >= 0f) ? 1 : (-1)));
		float[] lateralDistances = new float[5]
		{
			clearance,
			clearance + 0.6f,
			clearance + 1.2f,
			clearance + 1.8f,
			clearance + 2.5f
		};
		float[] forwardOffsets = new float[8]
		{
			0.15f, -0.25f, 0.8f, -0.85f,
			1.35f, -1.4f, 2.05f, -2.0f
		};
		float segmentClearance = Mathf.Max(0.2f, clearance - 0.08f);
		for (int forwardIndex = 0; forwardIndex < forwardOffsets.Length; forwardIndex++)
		{
			for (int lateralIndex = 0; lateralIndex < lateralDistances.Length; lateralIndex++)
			{
				for (int sideIndex = 0; sideIndex < 2; sideIndex++)
				{
					float side = ((sideIndex == 0) ? preferredSide : (-preferredSide));
					Vector3 candidate2 = blockerPosition + lateral * (side * lateralDistances[lateralIndex]) + travelDirection * forwardOffsets[forwardIndex];
					candidate2.y = current.y;
					val = Vector3.ProjectOnPlane(candidate2 - current, Vector3.up);
					if (!(val.sqrMagnitude < 0.36f) && IsCharacterBypassPointClear(candidate2) && IsVisitorStaticRouteSegmentClear(current, candidate2) && IsVisitorStaticRouteSegmentClear(candidate2, destination) && IsPlayerEgressSegmentClear(current, candidate2, blockerPosition, segmentClearance) && IsPlanarSegmentClearOfPlayer(candidate2, destination, blockerPosition, segmentClearance))
					{
						route = (Vector3[])(object)new Vector3[2] { candidate2, destination };
						return true;
					}
				}
			}
		}
		if ((UnityEngine.Object)(object)doorway != (UnityEngine.Object)null)
		{
			Vector3 doorwayInward = Vector3.ProjectOnPlane(doorway.InteriorPoint - doorway.ExteriorPoint, Vector3.up);
			if (doorwayInward.sqrMagnitude > 0.01f)
			{
				doorwayInward.Normalize();
				val = Vector3.Cross(Vector3.up, doorwayInward);
				Vector3 doorwayLateral = val.normalized;
				float[] doorwaySideOffsets = new float[4] { 2.1f, 2.8f, 3.5f, 4.2f };
				float[] doorwayDepthOffsets = new float[4] { 0.15f, -0.65f, 0.85f, -1.35f };
				for (int j = 0; j < 2; j++)
				{
					float side2 = ((j == 0) ? 1f : (-1f));
					for (int sideOffsetIndex = 0; sideOffsetIndex < doorwaySideOffsets.Length; sideOffsetIndex++)
					{
						for (int depthIndex = 0; depthIndex < doorwayDepthOffsets.Length; depthIndex++)
						{
							Vector3 candidate3 = doorway.InteriorPoint + doorwayLateral * (side2 * doorwaySideOffsets[sideOffsetIndex]) + doorwayInward * doorwayDepthOffsets[depthIndex];
							candidate3.y = current.y;
							val = Vector3.ProjectOnPlane(candidate3 - current, Vector3.up);
							if (!(val.sqrMagnitude < 0.36f) && IsCharacterBypassPointClear(candidate3) && IsVisitorStaticRouteSegmentClear(current, candidate3) && IsVisitorStaticRouteSegmentClear(candidate3, destination) && IsPlayerEgressSegmentClear(current, candidate3, blockerPosition, segmentClearance) && IsPlanarSegmentClearOfPlayer(candidate3, destination, blockerPosition, segmentClearance))
							{
								route = (Vector3[])(object)new Vector3[2] { candidate3, destination };
								return true;
							}
						}
					}
				}
			}
		}
		if (blockerName.IndexOf("Player", StringComparison.OrdinalIgnoreCase) >= 0)
		{
			Debug.LogWarning((object)($"GYMCHAOS_VISITOR_PLAYER_BYPASS_UNAVAILABLE enemy={fighter.Identity} " + $"current={current} destination={destination} " + $"player={blockerPosition} clearance={clearance:0.00}"), (UnityEngine.Object)(object)this);
		}
		return false;
	}
	private bool IsVisitorStaticRouteSegmentClear(Vector3 start, Vector3 end)
	{
		Vector3 delta = Vector3.ProjectOnPlane(end - start, Vector3.up);
		float distance = delta.magnitude;
		if (distance < 0.05f || (UnityEngine.Object)(object)fighter == (UnityEngine.Object)null)
		{
			return true;
		}

		Vector3 direction = delta / distance;
		Vector3 lower = start + Vector3.up * EnemyFighter.VisitorProbeLower;
		Vector3 upper = start + Vector3.up * EnemyFighter.VisitorProbeUpper;
		int count = Physics.CapsuleCastNonAlloc(
			lower, upper, EnemyFighter.GetBodyRadiusForIdentity(fighter.Identity),
			direction, doorwayRouteHits, distance + 0.06f, ~0,
			QueryTriggerInteraction.Ignore);
		for (int i = 0; i < count; i++)
		{
			Collider hit = doorwayRouteHits[i].collider;
			if (hit == null || hit.transform == transform ||
				hit.transform.IsChildOf(transform))
			{
				continue;
			}
			if (hit.GetComponentInParent<EnemyFighter>() != null ||
				hit.GetComponentInParent<PlayerMovement>() != null ||
				hit.GetComponentInParent<GymDoorway>() != null)
			{
				continue;
			}
			if (HasRoomFloorInHierarchy(hit.transform) ||
				IsWalkableFloorSurface(hit) ||
				hit.name == "Player Road Access Blocker" ||
				hit.name == "Exterior Courtyard Foundation" ||
				(doorwayWallCollisionsIgnored &&
					IsDoorwayWallColliderForRouting(hit)))
			{
				continue;
			}
			return false;
		}

		return true;
	}
	private bool IsCharacterBypassPointClear(Vector3 point)
	{
		//IL_0000: Unknown result type (might be due to invalid IL or missing references)
		//IL_0001: Unknown result type (might be due to invalid IL or missing references)
		//IL_000b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0010: Unknown result type (might be due to invalid IL or missing references)
		//IL_0015: Unknown result type (might be due to invalid IL or missing references)
		//IL_0016: Unknown result type (might be due to invalid IL or missing references)
		//IL_0020: Unknown result type (might be due to invalid IL or missing references)
		//IL_0025: Unknown result type (might be due to invalid IL or missing references)
		//IL_002a: Unknown result type (might be due to invalid IL or missing references)
		//IL_002b: Unknown result type (might be due to invalid IL or missing references)
		Vector3 val = point + Vector3.up * 0.59125006f;
		Vector3 upper = point + Vector3.up * 1.9887501f;
		int count = Physics.OverlapCapsuleNonAlloc(val, upper, 0.6235f, doorwayPointHits, -1, (QueryTriggerInteraction)1);
		for (int i = 0; i < count; i++)
		{
			Collider hit = doorwayPointHits[i];
			if (!((UnityEngine.Object)(object)hit == (UnityEngine.Object)null) && !((UnityEngine.Object)(object)((Component)hit).transform == (UnityEngine.Object)(object)((Component)this).transform) && !((Component)hit).transform.IsChildOf(((Component)this).transform) && !((UnityEngine.Object)(object)((Component)hit).GetComponentInParent<EnemyFighter>() != (UnityEngine.Object)null) && !((UnityEngine.Object)(object)((Component)hit).GetComponentInParent<PlayerMovement>() != (UnityEngine.Object)null) && !((UnityEngine.Object)(object)((Component)hit).GetComponentInParent<GymDoorway>() != (UnityEngine.Object)null) && !HasRoomFloorInHierarchy(((Component)hit).transform) && !IsWalkableFloorSurface(hit) && (!doorwayWallCollisionsIgnored || !IsDoorwayWallColliderForRouting(hit)) && !(((UnityEngine.Object)hit).name == "Player Road Access Blocker") && !(((UnityEngine.Object)hit).name == "Exterior Courtyard Foundation"))
			{
				return false;
			}
		}
		return true;
	}
	private static bool IsPlanarSegmentClearOfPlayer(Vector3 start, Vector3 end, Vector3 playerPosition, float radius)
	{
		//IL_0000: Unknown result type (might be due to invalid IL or missing references)
		//IL_0001: Unknown result type (might be due to invalid IL or missing references)
		//IL_0002: Unknown result type (might be due to invalid IL or missing references)
		//IL_0007: Unknown result type (might be due to invalid IL or missing references)
		//IL_000c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0011: Unknown result type (might be due to invalid IL or missing references)
		//IL_0024: Unknown result type (might be due to invalid IL or missing references)
		//IL_0025: Unknown result type (might be due to invalid IL or missing references)
		//IL_0026: Unknown result type (might be due to invalid IL or missing references)
		//IL_002b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0030: Unknown result type (might be due to invalid IL or missing references)
		//IL_0035: Unknown result type (might be due to invalid IL or missing references)
		//IL_0043: Unknown result type (might be due to invalid IL or missing references)
		//IL_0044: Unknown result type (might be due to invalid IL or missing references)
		//IL_0046: Unknown result type (might be due to invalid IL or missing references)
		//IL_004b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0050: Unknown result type (might be due to invalid IL or missing references)
		//IL_0051: Unknown result type (might be due to invalid IL or missing references)
		//IL_0056: Unknown result type (might be due to invalid IL or missing references)
		//IL_005b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0060: Unknown result type (might be due to invalid IL or missing references)
		Vector3 segment = Vector3.ProjectOnPlane(end - start, Vector3.up);
		float lengthSquared = segment.sqrMagnitude;
		if (lengthSquared < 0.0001f)
		{
			return true;
		}
		float t = Mathf.Clamp01(Vector3.Dot(Vector3.ProjectOnPlane(playerPosition - start, Vector3.up), segment) / lengthSquared);
		Vector3 val = Vector3.ProjectOnPlane(start + segment * t - playerPosition, Vector3.up);
		return val.sqrMagnitude >= radius * radius;
	}
	private static bool IsPlayerEgressSegmentClear(Vector3 start, Vector3 end, Vector3 playerPosition, float radius)
	{
		//IL_0000: Unknown result type (might be due to invalid IL or missing references)
		//IL_0001: Unknown result type (might be due to invalid IL or missing references)
		//IL_0002: Unknown result type (might be due to invalid IL or missing references)
		//IL_0007: Unknown result type (might be due to invalid IL or missing references)
		//IL_000c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0011: Unknown result type (might be due to invalid IL or missing references)
		//IL_0026: Unknown result type (might be due to invalid IL or missing references)
		//IL_0027: Unknown result type (might be due to invalid IL or missing references)
		//IL_0028: Unknown result type (might be due to invalid IL or missing references)
		//IL_002d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0032: Unknown result type (might be due to invalid IL or missing references)
		//IL_0037: Unknown result type (might be due to invalid IL or missing references)
		//IL_001c: Unknown result type (might be due to invalid IL or missing references)
		//IL_001d: Unknown result type (might be due to invalid IL or missing references)
		//IL_001e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0058: Unknown result type (might be due to invalid IL or missing references)
		//IL_005f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0069: Unknown result type (might be due to invalid IL or missing references)
		//IL_006a: Unknown result type (might be due to invalid IL or missing references)
		//IL_006b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0070: Unknown result type (might be due to invalid IL or missing references)
		//IL_0075: Unknown result type (might be due to invalid IL or missing references)
		//IL_007a: Unknown result type (might be due to invalid IL or missing references)
		Vector3 startOffset = Vector3.ProjectOnPlane(start - playerPosition, Vector3.up);
		if (startOffset.magnitude >= radius)
		{
			return IsPlanarSegmentClearOfPlayer(start, end, playerPosition, radius);
		}
		Vector3 egress = Vector3.ProjectOnPlane(end - start, Vector3.up);
		if (startOffset.sqrMagnitude < 0.0001f || egress.sqrMagnitude < 0.0001f)
		{
			return false;
		}
		float num = Vector3.Dot(egress.normalized, startOffset.normalized);
		Vector3 val = Vector3.ProjectOnPlane(end - playerPosition, Vector3.up);
		float endSeparation = val.magnitude;
		if (num > 0.25f)
		{
			return endSeparation > startOffset.magnitude + 0.18f;
		}
		return false;
	}
	private static bool TryGetRoomFloorBounds(out Bounds bounds)
	{
		//IL_0032: Unknown result type (might be due to invalid IL or missing references)
		//IL_0037: Unknown result type (might be due to invalid IL or missing references)
		//IL_003d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0028: Unknown result type (might be due to invalid IL or missing references)
		//IL_004f: Unknown result type (might be due to invalid IL or missing references)
		GameObject floor = GameObject.Find("Rubber Floor");
		Renderer renderer = (((UnityEngine.Object)(object)floor != (UnityEngine.Object)null) ? floor.GetComponent<Renderer>() : null);
		if ((UnityEngine.Object)(object)renderer == (UnityEngine.Object)null)
		{
			bounds = default(Bounds);
			return false;
		}
		bounds = renderer.bounds;
		if (bounds.size.x > 2f)
		{
			return bounds.size.z > 2f;
		}
		return false;
	}
	private static bool HasRoomFloorInHierarchy(Transform target)
	{
		Transform current = target;
		while ((UnityEngine.Object)(object)current != (UnityEngine.Object)null)
		{
			string lowerName = ((UnityEngine.Object)current).name.ToLowerInvariant();
			if (lowerName.Contains("rubber floor") || lowerName == "plane" || lowerName.StartsWith("plane("))
			{
				return true;
			}
			current = current.parent;
		}
		return false;
	}
	private static bool IsWalkableFloorSurface(Collider collider)
	{
		if ((UnityEngine.Object)(object)collider == (UnityEngine.Object)null)
		{
			return false;
		}
		Transform current = ((Component)collider).transform;
		while ((UnityEngine.Object)(object)current != (UnityEngine.Object)null)
		{
			string lowerName = ((UnityEngine.Object)current).name.ToLowerInvariant();
			if (lowerName.Contains("mat") || lowerName.Contains("carpet") || lowerName.Contains("rug"))
			{
				return true;
			}
			current = current.parent;
		}
		return false;
	}
}
