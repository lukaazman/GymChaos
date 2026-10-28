using System;
using System.Collections.Generic;
using UnityEngine;

public sealed partial class GymVisitorAgent
{
	public bool AllowsPlayerContactEgress(PlayerMovement player, Vector3 direction)
	{
		//IL_0033: Unknown result type (might be due to invalid IL or missing references)
		//IL_003e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0043: Unknown result type (might be due to invalid IL or missing references)
		//IL_0048: Unknown result type (might be due to invalid IL or missing references)
		//IL_004d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0052: Unknown result type (might be due to invalid IL or missing references)
		//IL_0053: Unknown result type (might be due to invalid IL or missing references)
		//IL_0054: Unknown result type (might be due to invalid IL or missing references)
		//IL_0059: Unknown result type (might be due to invalid IL or missing references)
		//IL_005e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0086: Unknown result type (might be due to invalid IL or missing references)
		//IL_008d: Unknown result type (might be due to invalid IL or missing references)
		if ((UnityEngine.Object)(object)fighter == (UnityEngine.Object)null || (UnityEngine.Object)(object)player == (UnityEngine.Object)null || (state != VisitorState.ExitingDoor && state != VisitorState.LeavingGym))
		{
			return false;
		}
		Vector3 away = Vector3.ProjectOnPlane(fighter.VisitorPhysicsPosition - ((Component)player).transform.position, Vector3.up);
		Vector3 planarDirection = Vector3.ProjectOnPlane(direction, Vector3.up);
		if (away.sqrMagnitude < 0.0001f || planarDirection.sqrMagnitude < 0.0001f)
		{
			return false;
		}
		float magnitude = away.magnitude;
		float alignment = Vector3.Dot(planarDirection.normalized, away.normalized);
		if (magnitude < 2.35f)
		{
			return alignment > 0.32f;
		}
		return false;
	}
	private void AllowVisitorThroughPlayerRoadBlocker()
	{
		SetVisitorPlayerBoundaryCollisionIgnored(ignored: true);
	}
	private void RestoreVisitorPlayerBoundaryCollisions()
	{
		SetVisitorPlayerBoundaryCollisionIgnored(ignored: false);
	}
	private static readonly string[] PlayerRouteBoundaryNames = { "Player Road Access Blocker", "Outdoor Boundary - Path Outer" };
	private void SetVisitorPlayerBoundaryCollisionIgnored(bool ignored)
	{
		if ((UnityEngine.Object)(object)fighter == (UnityEngine.Object)null)
		{
			return;
		}
		Collider[] array = GymSceneLookup.FindCollidersNamed(PlayerRouteBoundaryNames);
		Collider[] visitorColliders = ((Component)fighter).GetComponentsInChildren<Collider>(true);
		foreach (Collider boundary in array)
		{
			for (int j = 0; j < visitorColliders.Length; j++)
			{
				if ((UnityEngine.Object)(object)visitorColliders[j] != (UnityEngine.Object)null)
				{
					Physics.IgnoreCollision(visitorColliders[j], boundary, ignored);
				}
			}
		}
	}
}
