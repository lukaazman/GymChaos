using System;
using System.Collections.Generic;
using UnityEngine;

public sealed partial class GymVisitorAgent
{
	public bool BeginWorkoutApproach(GymExerciseStation station, int repetitions, float repDuration)
	{
		//IL_00b4: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b9: Unknown result type (might be due to invalid IL or missing references)
		//IL_00be: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c3: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c8: Unknown result type (might be due to invalid IL or missing references)
		//IL_00cd: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e4: Unknown result type (might be due to invalid IL or missing references)
		//IL_00eb: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f5: Unknown result type (might be due to invalid IL or missing references)
		//IL_00fa: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ff: Unknown result type (might be due to invalid IL or missing references)
		//IL_00dc: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e1: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a7: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ac: Unknown result type (might be due to invalid IL or missing references)
		//IL_0115: Unknown result type (might be due to invalid IL or missing references)
		//IL_013c: Unknown result type (might be due to invalid IL or missing references)
		//IL_014c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0151: Unknown result type (might be due to invalid IL or missing references)
		//IL_0156: Unknown result type (might be due to invalid IL or missing references)
		//IL_015b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0160: Unknown result type (might be due to invalid IL or missing references)
		if ((UnityEngine.Object)(object)fighter == (UnityEngine.Object)null || (UnityEngine.Object)(object)station == (UnityEngine.Object)null || state != VisitorState.FreeRoaming || fighter.IsDead || fighter.IsOnTreadmill)
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
			travelTarget = station.EnemyPosition;
		}
		else
		{
			Vector3 approachDirection = Vector3.ProjectOnPlane(station.EnemyRotation * Vector3.back, Vector3.up);
			if (approachDirection.sqrMagnitude < 0.01f)
			{
				approachDirection = Vector3.back;
			}
			travelTarget = station.EnemyPosition + approachDirection.normalized * 2.15f;
		}
		travelTarget.y = ((Component)fighter).transform.position.y;
		workoutApproachStartedAt = Time.time;
		workoutApproachStalledSeconds = 0f;
		Vector3 val = Vector3.ProjectOnPlane(travelTarget - ((Component)fighter).transform.position, Vector3.up);
		lastWorkoutApproachDistance = val.magnitude;
		state = VisitorState.ApproachingWorkout;
		fighter.StopVisitorMovement();
		return true;
	}
	private bool BeginWorkoutStationRelease(GymExerciseStation station)
	{
		//IL_0028: Unknown result type (might be due to invalid IL or missing references)
		//IL_002d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0032: Unknown result type (might be due to invalid IL or missing references)
		//IL_0037: Unknown result type (might be due to invalid IL or missing references)
		//IL_003c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0041: Unknown result type (might be due to invalid IL or missing references)
		//IL_005e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0063: Unknown result type (might be due to invalid IL or missing references)
		//IL_0071: Unknown result type (might be due to invalid IL or missing references)
		//IL_0080: Unknown result type (might be due to invalid IL or missing references)
		//IL_0081: Unknown result type (might be due to invalid IL or missing references)
		//IL_0091: Unknown result type (might be due to invalid IL or missing references)
		//IL_0096: Unknown result type (might be due to invalid IL or missing references)
		//IL_009b: Unknown result type (might be due to invalid IL or missing references)
		//IL_009c: Unknown result type (might be due to invalid IL or missing references)
		//IL_009d: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a3: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a8: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ad: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b0: Unknown result type (might be due to invalid IL or missing references)
		//IL_00bd: Unknown result type (might be due to invalid IL or missing references)
		//IL_00d8: Unknown result type (might be due to invalid IL or missing references)
		//IL_00d9: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e0: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e1: Unknown result type (might be due to invalid IL or missing references)
		//IL_00fa: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ff: Unknown result type (might be due to invalid IL or missing references)
		//IL_0123: Unknown result type (might be due to invalid IL or missing references)
		//IL_0133: Unknown result type (might be due to invalid IL or missing references)
		//IL_0138: Unknown result type (might be due to invalid IL or missing references)
		//IL_013d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0142: Unknown result type (might be due to invalid IL or missing references)
		//IL_0147: Unknown result type (might be due to invalid IL or missing references)
		//IL_017b: Unknown result type (might be due to invalid IL or missing references)
		//IL_018f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0050: Unknown result type (might be due to invalid IL or missing references)
		//IL_0055: Unknown result type (might be due to invalid IL or missing references)
		if ((UnityEngine.Object)(object)fighter == (UnityEngine.Object)null || (UnityEngine.Object)(object)station == (UnityEngine.Object)null || !station.BeginEnemySquatRelease(fighter))
		{
			return false;
		}
		Vector3 releaseDirection = Vector3.ProjectOnPlane(station.EnemyRotation * Vector3.back, Vector3.up);
		if (releaseDirection.sqrMagnitude < 0.001f)
		{
			releaseDirection = Vector3.back;
		}
		releaseDirection.Normalize();
		Vector3 releaseOrigin = station.EnemyPosition;
		releaseOrigin.y = ((Component)fighter).transform.position.y;
		Vector3 firstClearPoint = releaseOrigin + releaseDirection * Mathf.Min(1.55f, 1.675f);
		Vector3 safePoint = releaseOrigin + releaseDirection * 3.35f;
		firstClearPoint.y = releaseOrigin.y;
		safePoint.y = releaseOrigin.y;
		workoutReleaseStation = station;
		workoutReleaseWaypoints = (Vector3[])(object)new Vector3[2] { firstClearPoint, safePoint };
		workoutReleaseWaypointIndex = 0;
		workoutReleaseTarget = workoutReleaseWaypoints[0];
		workoutReleaseStartedAt = Time.time;
		workoutReleaseStalledSeconds = 0f;
		workoutReleaseProgressLogged = false;
		Vector3 val = Vector3.ProjectOnPlane(workoutReleaseTarget - ((Component)fighter).transform.position, Vector3.up);
		lastWorkoutReleaseDistance = val.magnitude;
		Debug.Log((object)($"GYMCHAOS_SQUAT_RELEASE_WALK_STARTED enemy={fighter.Identity} " + $"station={station.EquipmentName} target={workoutReleaseTarget} " + $"safePoint={safePoint}"), (UnityEngine.Object)(object)this);
		return true;
	}
	private void TickWorkoutStationRelease()
	{
		//IL_0008: Unknown result type (might be due to invalid IL or missing references)
		//IL_0018: Unknown result type (might be due to invalid IL or missing references)
		//IL_001d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0022: Unknown result type (might be due to invalid IL or missing references)
		//IL_0027: Unknown result type (might be due to invalid IL or missing references)
		//IL_002c: Unknown result type (might be due to invalid IL or missing references)
		//IL_00de: Unknown result type (might be due to invalid IL or missing references)
		//IL_0088: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a0: Unknown result type (might be due to invalid IL or missing references)
		//IL_012c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0131: Unknown result type (might be due to invalid IL or missing references)
		//IL_0155: Unknown result type (might be due to invalid IL or missing references)
		//IL_0165: Unknown result type (might be due to invalid IL or missing references)
		//IL_016a: Unknown result type (might be due to invalid IL or missing references)
		//IL_016f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0174: Unknown result type (might be due to invalid IL or missing references)
		//IL_0179: Unknown result type (might be due to invalid IL or missing references)
		//IL_01f4: Unknown result type (might be due to invalid IL or missing references)
		//IL_01fa: Unknown result type (might be due to invalid IL or missing references)
		//IL_01ff: Unknown result type (might be due to invalid IL or missing references)
		//IL_0204: Unknown result type (might be due to invalid IL or missing references)
		//IL_0209: Unknown result type (might be due to invalid IL or missing references)
		//IL_020e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0237: Unknown result type (might be due to invalid IL or missing references)
		//IL_023d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0242: Unknown result type (might be due to invalid IL or missing references)
		//IL_0247: Unknown result type (might be due to invalid IL or missing references)
		//IL_024c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0251: Unknown result type (might be due to invalid IL or missing references)
		//IL_02ad: Unknown result type (might be due to invalid IL or missing references)
		//IL_02b2: Unknown result type (might be due to invalid IL or missing references)
		//IL_02b9: Unknown result type (might be due to invalid IL or missing references)
		//IL_02be: Unknown result type (might be due to invalid IL or missing references)
		//IL_02c3: Unknown result type (might be due to invalid IL or missing references)
		//IL_02d2: Unknown result type (might be due to invalid IL or missing references)
		//IL_02ea: Unknown result type (might be due to invalid IL or missing references)
		//IL_02ec: Unknown result type (might be due to invalid IL or missing references)
		//IL_02fe: Unknown result type (might be due to invalid IL or missing references)
		//IL_0300: Unknown result type (might be due to invalid IL or missing references)
		//IL_0324: Unknown result type (might be due to invalid IL or missing references)
		//IL_0334: Unknown result type (might be due to invalid IL or missing references)
		//IL_0339: Unknown result type (might be due to invalid IL or missing references)
		//IL_033e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0343: Unknown result type (might be due to invalid IL or missing references)
		//IL_0348: Unknown result type (might be due to invalid IL or missing references)
		//IL_0272: Unknown result type (might be due to invalid IL or missing references)
		//IL_038c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0299: Unknown result type (might be due to invalid IL or missing references)
		//IL_0289: Unknown result type (might be due to invalid IL or missing references)
		//IL_028f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0294: Unknown result type (might be due to invalid IL or missing references)
		GymExerciseStation station = workoutReleaseStation;
		Vector3 val = Vector3.ProjectOnPlane(workoutReleaseTarget - ((Component)fighter).transform.position, Vector3.up);
		float releaseDistance = val.magnitude;
		if (!workoutReleaseProgressLogged)
		{
			workoutReleaseProgressLogged = true;
			Debug.Log((object)($"GYMCHAOS_SQUAT_RELEASE_WALK_TICK enemy={fighter.Identity} " + $"station={station?.EquipmentName} position={((Component)fighter).transform.position} " + $"target={workoutReleaseTarget} distance={releaseDistance:0.00} " + "blocker=" + fighter.LastVisitorRouteBlocker), (UnityEngine.Object)(object)this);
		}
		if (fighter.MoveVisitorTo(workoutReleaseTarget, 2.1f, allowOutsideRoom: false, station))
		{
			if (workoutReleaseWaypoints != null && workoutReleaseWaypointIndex + 1 < workoutReleaseWaypoints.Length)
			{
				workoutReleaseWaypointIndex++;
				workoutReleaseTarget = workoutReleaseWaypoints[workoutReleaseWaypointIndex];
				workoutReleaseStartedAt = Time.time;
				workoutReleaseStalledSeconds = 0f;
				workoutReleaseProgressLogged = false;
				val = Vector3.ProjectOnPlane(workoutReleaseTarget - ((Component)fighter).transform.position, Vector3.up);
				lastWorkoutReleaseDistance = val.magnitude;
			}
			else
			{
				CompleteWorkoutStationRelease("arrived");
			}
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
		if (!(workoutReleaseStalledSeconds > 2.4f) && !(Time.time - workoutReleaseStartedAt > 8f))
		{
			return;
		}
		val = Vector3.ProjectOnPlane(((Component)fighter).transform.position - station.EnemyPosition, Vector3.up);
		float distanceFromStation = val.magnitude;
		if (!(distanceFromStation >= 1.05f) || !TryCompleteWorkoutReleaseAtCurrentPoint(station, distanceFromStation))
		{
			Vector3 rerouteDirection = Vector3.ProjectOnPlane(((Component)fighter).transform.position - station.EnemyPosition, Vector3.up);
			if (rerouteDirection.sqrMagnitude < 0.01f)
			{
				rerouteDirection = ((workoutReleaseWaypoints != null && workoutReleaseWaypoints.Length != 0) ? (workoutReleaseWaypoints[workoutReleaseWaypoints.Length - 1] - station.EnemyPosition) : Vector3.back);
			}
			rerouteDirection.Normalize();
			Vector3 rerouteTarget = ((Component)fighter).transform.position + rerouteDirection * 1.4f;
			rerouteTarget.y = ((Component)fighter).transform.position.y;
			workoutReleaseWaypoints = (Vector3[])(object)new Vector3[1] { rerouteTarget };
			workoutReleaseWaypointIndex = 0;
			workoutReleaseTarget = rerouteTarget;
			workoutReleaseStartedAt = Time.time;
			workoutReleaseStalledSeconds = 0f;
			workoutReleaseProgressLogged = false;
			val = Vector3.ProjectOnPlane(workoutReleaseTarget - ((Component)fighter).transform.position, Vector3.up);
			lastWorkoutReleaseDistance = val.magnitude;
			Debug.LogWarning((object)($"GYMCHAOS_SQUAT_RELEASE_WALK_REROUTE enemy={fighter.Identity} " + $"station={station?.EquipmentName} target={workoutReleaseTarget} " + $"distanceFromStation={distanceFromStation:0.00} " + "blocker=" + fighter.LastVisitorRouteBlocker), (UnityEngine.Object)(object)this);
		}
	}
	private bool TryCompleteWorkoutReleaseAtCurrentPoint(GymExerciseStation station, float distanceFromStation)
	{
		//IL_0027: Unknown result type (might be due to invalid IL or missing references)
		//IL_002d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0032: Unknown result type (might be due to invalid IL or missing references)
		if (distanceFromStation < 1.05f || (UnityEngine.Object)(object)fighter == (UnityEngine.Object)null || !fighter.IsVisitorExternalPathClear(((Component)fighter).transform.position - station.EnemyPosition, 0.25f))
		{
			return false;
		}
		CompleteWorkoutStationRelease("safe-point");
		return true;
	}
	private void CompleteWorkoutStationRelease(string reason)
	{
		//IL_0027: Unknown result type (might be due to invalid IL or missing references)
		//IL_0015: Unknown result type (might be due to invalid IL or missing references)
		//IL_002c: Unknown result type (might be due to invalid IL or missing references)
		//IL_004b: Unknown result type (might be due to invalid IL or missing references)
		//IL_004d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0052: Unknown result type (might be due to invalid IL or missing references)
		//IL_0057: Unknown result type (might be due to invalid IL or missing references)
		//IL_005c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0061: Unknown result type (might be due to invalid IL or missing references)
		GymExerciseStation station = workoutReleaseStation;
		Vector3 completionPosition = (((UnityEngine.Object)(object)fighter != (UnityEngine.Object)null) ? ((Component)fighter).transform.position : Vector3.zero);
		float num;
		if (!((UnityEngine.Object)(object)station != (UnityEngine.Object)null) || !((UnityEngine.Object)(object)fighter != (UnityEngine.Object)null))
		{
			num = 0f;
		}
		else
		{
			Vector3 val = Vector3.ProjectOnPlane(completionPosition - station.EnemyPosition, Vector3.up);
			num = val.magnitude;
		}
		float distanceFromStation = num;
		EndWorkoutStationRelease();
		state = VisitorState.FreeRoaming;
		postWorkoutFreeRoamUntil = Time.time + 2.25f;
		fighter?.ResumeVisitorRoaming();
		completedWorkoutVersion++;
		Debug.Log((object)($"GYMCHAOS_SQUAT_RELEASE_WALK_COMPLETE enemy={fighter?.Identity} " + "station=" + station?.EquipmentName + " reason=" + reason + " " + $"distance={distanceFromStation:0.00} " + $"version={completedWorkoutVersion} state={state} " + $"barOnRack={(UnityEngine.Object)(object)station != (UnityEngine.Object)null && station.IsSquatBarOnRack} " + "GYMCHAOS_SQUAT_WORKOUT_LIFECYCLE_OK"), (UnityEngine.Object)(object)this);
	}
	private void EndWorkoutStationRelease()
	{
		//IL_0043: Unknown result type (might be due to invalid IL or missing references)
		//IL_0048: Unknown result type (might be due to invalid IL or missing references)
		if ((UnityEngine.Object)(object)workoutReleaseStation != (UnityEngine.Object)null && (UnityEngine.Object)(object)fighter != (UnityEngine.Object)null)
		{
			workoutReleaseStation.EndEnemySquatRelease(fighter);
		}
		workoutReleaseStation = null;
		workoutReleaseWaypoints = null;
		workoutReleaseWaypointIndex = 0;
		workoutReleaseTarget = Vector3.zero;
		workoutReleaseStartedAt = 0f;
		workoutReleaseStalledSeconds = 0f;
		workoutReleaseProgressLogged = false;
		lastWorkoutReleaseDistance = float.PositiveInfinity;
	}
	private void ReleasePendingStationApproach()
	{
		if ((UnityEngine.Object)(object)pendingStation != (UnityEngine.Object)null && pendingStation.IsSquat && (UnityEngine.Object)(object)fighter != (UnityEngine.Object)null)
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
		//IL_0049: Unknown result type (might be due to invalid IL or missing references)
		//IL_008d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0092: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a8: Unknown result type (might be due to invalid IL or missing references)
		//IL_00cf: Unknown result type (might be due to invalid IL or missing references)
		//IL_00df: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e4: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e9: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ee: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f3: Unknown result type (might be due to invalid IL or missing references)
		if ((UnityEngine.Object)(object)fighter == (UnityEngine.Object)null || (UnityEngine.Object)(object)pendingStation == (UnityEngine.Object)null || !pendingStation.IsSquat)
		{
			return false;
		}
		GymExerciseStation previousStation = pendingStation;
		previousStation.CancelEnemySquatApproach(fighter);
		GymExerciseStation alternative;
		do
		{
			alternative = GymExerciseStation.FindClosestSquat(((Component)fighter).transform.position, 60f, attemptedWorkoutStations);
			if ((UnityEngine.Object)(object)alternative == (UnityEngine.Object)null)
			{
				return false;
			}
			attemptedWorkoutStations.Add(alternative);
		}
		while (!alternative.TryReserveEnemySquatApproach(fighter));
		pendingStation = alternative;
		travelTarget = alternative.EnemyPosition;
		travelTarget.y = ((Component)fighter).transform.position.y;
		workoutApproachStartedAt = Time.time;
		workoutApproachStalledSeconds = 0f;
		Vector3 val = Vector3.ProjectOnPlane(travelTarget - ((Component)fighter).transform.position, Vector3.up);
		lastWorkoutApproachDistance = val.magnitude;
		Debug.LogWarning((object)($"GYMCHAOS_SQUAT_STATION_FALLBACK enemy={fighter.Identity} " + "from=" + previousStation.EquipmentName + " to=" + alternative.EquipmentName), (UnityEngine.Object)(object)this);
		return true;
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
		postWorkoutFreeRoamUntil = Time.time + 3f;
		ResetWorkoutApproachTracking();
		fighter?.ResumeVisitorRoaming();
		Debug.LogWarning((object)($"GYMCHAOS_SQUAT_APPROACH_CANCELLED enemy={fighter?.Identity} " + "station=" + canceledStation?.EquipmentName + " reason=" + reason + " " + $"freeRoamUntil={postWorkoutFreeRoamUntil:0.00}"), (UnityEngine.Object)(object)this);
	}
}
