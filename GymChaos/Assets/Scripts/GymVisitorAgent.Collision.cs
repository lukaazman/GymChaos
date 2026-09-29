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
	private void SetVisitorPlayerBoundaryCollisionIgnored(bool ignored)
	{
		if ((UnityEngine.Object)(object)fighter == (UnityEngine.Object)null)
		{
			return;
		}
		Collider[] array = UnityEngine.Object.FindObjectsByType<Collider>((FindObjectsInactive)1, (FindObjectsSortMode)0);
		Collider[] visitorColliders = ((Component)fighter).GetComponentsInChildren<Collider>(true);
		Collider[] array2 = array;
		foreach (Collider boundary in array2)
		{
			if (!IsPlayerRouteBoundaryCollider(boundary))
			{
				continue;
			}
			for (int j = 0; j < visitorColliders.Length; j++)
			{
				if ((UnityEngine.Object)(object)visitorColliders[j] != (UnityEngine.Object)null)
				{
					Physics.IgnoreCollision(visitorColliders[j], boundary, ignored);
				}
			}
		}
	}
	private static bool IsPlayerRouteBoundaryCollider(Collider collider)
	{
		if ((UnityEngine.Object)(object)collider == (UnityEngine.Object)null)
		{
			return false;
		}
		string name = ((UnityEngine.Object)((Component)collider).gameObject).name;
		if (!(name == "Player Road Access Blocker"))
		{
			return name == "Outdoor Boundary - Path Outer";
		}
		return true;
	}
}
