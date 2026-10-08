using System;
using System.Collections.Generic;
using UnityEngine;

public sealed partial class GymVisitorAgent
{
	public void PrepareVehicleYieldVerification()
	{
		externalVehicleRouteVerification = true;
		CancelVehicleYield();
		state = VisitorState.Dormant;
		enteredGym = false;
		leftGym = true;
		hasSuccessfulEntry = false;
		completedDoorExit = false;
		doorway = GymDoorway.Instance;
	}
	public void BeginEntryFromVehicle(GymDoorway door, Vector3 destinationInside, Vector3 vehicleSpawnPoint, Vector3 parkingAislePoint, GymVisitorVehicle arrivalVehicleForEntry)
	{
		returningToVehicleAfterEntryAbort = false;
		doorwayExitClearanceReleased = false;
		doorwayEntryYieldingForExit = false;
		//IL_0002: Unknown result type (might be due to invalid IL or missing references)
		//IL_0048: Unknown result type (might be due to invalid IL or missing references)
		//IL_0053: Unknown result type (might be due to invalid IL or missing references)
		//IL_0066: Unknown result type (might be due to invalid IL or missing references)
		//IL_0067: Unknown result type (might be due to invalid IL or missing references)
		//IL_006c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0071: Unknown result type (might be due to invalid IL or missing references)
		//IL_0076: Unknown result type (might be due to invalid IL or missing references)
		//IL_007b: Unknown result type (might be due to invalid IL or missing references)
		//IL_00aa: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b1: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b6: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b9: Unknown result type (might be due to invalid IL or missing references)
		//IL_00be: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c1: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c2: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c5: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ca: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f5: Unknown result type (might be due to invalid IL or missing references)
		//IL_00fc: Unknown result type (might be due to invalid IL or missing references)
		//IL_0102: Unknown result type (might be due to invalid IL or missing references)
		//IL_0107: Unknown result type (might be due to invalid IL or missing references)
		//IL_0115: Unknown result type (might be due to invalid IL or missing references)
		//IL_011d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0130: Unknown result type (might be due to invalid IL or missing references)
		//IL_0137: Unknown result type (might be due to invalid IL or missing references)
		//IL_013c: Unknown result type (might be due to invalid IL or missing references)
		//IL_013e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0143: Unknown result type (might be due to invalid IL or missing references)
		//IL_0155: Unknown result type (might be due to invalid IL or missing references)
		//IL_015a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0166: Unknown result type (might be due to invalid IL or missing references)
		//IL_016e: Unknown result type (might be due to invalid IL or missing references)
		//IL_017c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0184: Unknown result type (might be due to invalid IL or missing references)
		//IL_0193: Unknown result type (might be due to invalid IL or missing references)
		//IL_019d: Unknown result type (might be due to invalid IL or missing references)
		//IL_01bc: Unknown result type (might be due to invalid IL or missing references)
		//IL_01c6: Unknown result type (might be due to invalid IL or missing references)
		//IL_01e3: Unknown result type (might be due to invalid IL or missing references)
		//IL_01f4: Unknown result type (might be due to invalid IL or missing references)
		//IL_00d3: Unknown result type (might be due to invalid IL or missing references)
		//IL_00d8: Unknown result type (might be due to invalid IL or missing references)
		//IL_00dd: Unknown result type (might be due to invalid IL or missing references)
		//IL_00df: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e6: Unknown result type (might be due to invalid IL or missing references)
		//IL_00eb: Unknown result type (might be due to invalid IL or missing references)
		//IL_020e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0213: Unknown result type (might be due to invalid IL or missing references)
		//IL_0215: Unknown result type (might be due to invalid IL or missing references)
		//IL_021a: Unknown result type (might be due to invalid IL or missing references)
		//IL_021c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0221: Unknown result type (might be due to invalid IL or missing references)
		//IL_0223: Unknown result type (might be due to invalid IL or missing references)
		//IL_0228: Unknown result type (might be due to invalid IL or missing references)
		//IL_022a: Unknown result type (might be due to invalid IL or missing references)
		//IL_022f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0231: Unknown result type (might be due to invalid IL or missing references)
		//IL_0236: Unknown result type (might be due to invalid IL or missing references)
		//IL_0238: Unknown result type (might be due to invalid IL or missing references)
		//IL_023d: Unknown result type (might be due to invalid IL or missing references)
		//IL_023f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0244: Unknown result type (might be due to invalid IL or missing references)
		//IL_0246: Unknown result type (might be due to invalid IL or missing references)
		//IL_024b: Unknown result type (might be due to invalid IL or missing references)
		//IL_029b: Unknown result type (might be due to invalid IL or missing references)
		//IL_02db: Unknown result type (might be due to invalid IL or missing references)
		//IL_02e9: Unknown result type (might be due to invalid IL or missing references)
		//IL_02f5: Unknown result type (might be due to invalid IL or missing references)
		//IL_0306: Unknown result type (might be due to invalid IL or missing references)
		//IL_0315: Unknown result type (might be due to invalid IL or missing references)
		//IL_0329: Unknown result type (might be due to invalid IL or missing references)
		//IL_0338: Unknown result type (might be due to invalid IL or missing references)
		//IL_034c: Unknown result type (might be due to invalid IL or missing references)
		//IL_035b: Unknown result type (might be due to invalid IL or missing references)
		//IL_036f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0387: Unknown result type (might be due to invalid IL or missing references)
		//IL_0391: Unknown result type (might be due to invalid IL or missing references)
		//IL_03a5: Unknown result type (might be due to invalid IL or missing references)
		//IL_03c3: Unknown result type (might be due to invalid IL or missing references)
		//IL_03cd: Unknown result type (might be due to invalid IL or missing references)
		//IL_03e7: Unknown result type (might be due to invalid IL or missing references)
		//IL_0405: Unknown result type (might be due to invalid IL or missing references)
		//IL_040f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0429: Unknown result type (might be due to invalid IL or missing references)
		//IL_0441: Unknown result type (might be due to invalid IL or missing references)
		//IL_044b: Unknown result type (might be due to invalid IL or missing references)
		//IL_045f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0477: Unknown result type (might be due to invalid IL or missing references)
		//IL_0481: Unknown result type (might be due to invalid IL or missing references)
		//IL_0495: Unknown result type (might be due to invalid IL or missing references)
		//IL_04ad: Unknown result type (might be due to invalid IL or missing references)
		//IL_04b7: Unknown result type (might be due to invalid IL or missing references)
		//IL_04cb: Unknown result type (might be due to invalid IL or missing references)
		//IL_04e3: Unknown result type (might be due to invalid IL or missing references)
		//IL_04ed: Unknown result type (might be due to invalid IL or missing references)
		//IL_0501: Unknown result type (might be due to invalid IL or missing references)
		//IL_0519: Unknown result type (might be due to invalid IL or missing references)
		//IL_0523: Unknown result type (might be due to invalid IL or missing references)
		//IL_0531: Unknown result type (might be due to invalid IL or missing references)
		//IL_0543: Unknown result type (might be due to invalid IL or missing references)
		//IL_054d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0561: Unknown result type (might be due to invalid IL or missing references)
		//IL_0579: Unknown result type (might be due to invalid IL or missing references)
		//IL_0583: Unknown result type (might be due to invalid IL or missing references)
		//IL_0597: Unknown result type (might be due to invalid IL or missing references)
		//IL_05af: Unknown result type (might be due to invalid IL or missing references)
		//IL_05b9: Unknown result type (might be due to invalid IL or missing references)
		//IL_05cd: Unknown result type (might be due to invalid IL or missing references)
		//IL_05e5: Unknown result type (might be due to invalid IL or missing references)
		//IL_05ef: Unknown result type (might be due to invalid IL or missing references)
		//IL_05fd: Unknown result type (might be due to invalid IL or missing references)
		//IL_0615: Unknown result type (might be due to invalid IL or missing references)
		//IL_061f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0633: Unknown result type (might be due to invalid IL or missing references)
		//IL_064b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0655: Unknown result type (might be due to invalid IL or missing references)
		//IL_0669: Unknown result type (might be due to invalid IL or missing references)
		//IL_0681: Unknown result type (might be due to invalid IL or missing references)
		//IL_068b: Unknown result type (might be due to invalid IL or missing references)
		//IL_069f: Unknown result type (might be due to invalid IL or missing references)
		//IL_06b7: Unknown result type (might be due to invalid IL or missing references)
		//IL_06c1: Unknown result type (might be due to invalid IL or missing references)
		//IL_06d5: Unknown result type (might be due to invalid IL or missing references)
		//IL_06ed: Unknown result type (might be due to invalid IL or missing references)
		//IL_06f7: Unknown result type (might be due to invalid IL or missing references)
		//IL_070b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0723: Unknown result type (might be due to invalid IL or missing references)
		//IL_072d: Unknown result type (might be due to invalid IL or missing references)
		//IL_073b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0753: Unknown result type (might be due to invalid IL or missing references)
		//IL_075d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0771: Unknown result type (might be due to invalid IL or missing references)
		//IL_0789: Unknown result type (might be due to invalid IL or missing references)
		//IL_0793: Unknown result type (might be due to invalid IL or missing references)
		//IL_07a7: Unknown result type (might be due to invalid IL or missing references)
		//IL_07bf: Unknown result type (might be due to invalid IL or missing references)
		//IL_07c9: Unknown result type (might be due to invalid IL or missing references)
		//IL_07d7: Unknown result type (might be due to invalid IL or missing references)
		//IL_07ef: Unknown result type (might be due to invalid IL or missing references)
		//IL_07f9: Unknown result type (might be due to invalid IL or missing references)
		//IL_080d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0825: Unknown result type (might be due to invalid IL or missing references)
		//IL_082f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0bd1: Unknown result type (might be due to invalid IL or missing references)
		//IL_0bd3: Unknown result type (might be due to invalid IL or missing references)
		//IL_0bda: Unknown result type (might be due to invalid IL or missing references)
		//IL_0be5: Unknown result type (might be due to invalid IL or missing references)
		//IL_0bef: Unknown result type (might be due to invalid IL or missing references)
		//IL_0bf4: Unknown result type (might be due to invalid IL or missing references)
		//IL_0bfb: Unknown result type (might be due to invalid IL or missing references)
		//IL_0bfc: Unknown result type (might be due to invalid IL or missing references)
		//IL_0c03: Unknown result type (might be due to invalid IL or missing references)
		//IL_0c05: Unknown result type (might be due to invalid IL or missing references)
		//IL_0c12: Unknown result type (might be due to invalid IL or missing references)
		//IL_0c23: Unknown result type (might be due to invalid IL or missing references)
		//IL_0c2d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0c32: Unknown result type (might be due to invalid IL or missing references)
		//IL_0b3d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0b3f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0b46: Unknown result type (might be due to invalid IL or missing references)
		//IL_0b48: Unknown result type (might be due to invalid IL or missing references)
		//IL_0b4f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0b51: Unknown result type (might be due to invalid IL or missing references)
		//IL_0b58: Unknown result type (might be due to invalid IL or missing references)
		//IL_0b5a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0b61: Unknown result type (might be due to invalid IL or missing references)
		//IL_0b63: Unknown result type (might be due to invalid IL or missing references)
		//IL_0b6a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0b75: Unknown result type (might be due to invalid IL or missing references)
		//IL_0b7f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0b84: Unknown result type (might be due to invalid IL or missing references)
		//IL_0b8b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0b8c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0b93: Unknown result type (might be due to invalid IL or missing references)
		//IL_0b95: Unknown result type (might be due to invalid IL or missing references)
		//IL_0ba2: Unknown result type (might be due to invalid IL or missing references)
		//IL_0bb3: Unknown result type (might be due to invalid IL or missing references)
		//IL_0bbd: Unknown result type (might be due to invalid IL or missing references)
		//IL_0bc2: Unknown result type (might be due to invalid IL or missing references)
		//IL_09d0: Unknown result type (might be due to invalid IL or missing references)
		//IL_09d2: Unknown result type (might be due to invalid IL or missing references)
		//IL_09d9: Unknown result type (might be due to invalid IL or missing references)
		//IL_09db: Unknown result type (might be due to invalid IL or missing references)
		//IL_09e2: Unknown result type (might be due to invalid IL or missing references)
		//IL_09e4: Unknown result type (might be due to invalid IL or missing references)
		//IL_09eb: Unknown result type (might be due to invalid IL or missing references)
		//IL_09ed: Unknown result type (might be due to invalid IL or missing references)
		//IL_09f4: Unknown result type (might be due to invalid IL or missing references)
		//IL_09f6: Unknown result type (might be due to invalid IL or missing references)
		//IL_09fd: Unknown result type (might be due to invalid IL or missing references)
		//IL_09ff: Unknown result type (might be due to invalid IL or missing references)
		//IL_0a06: Unknown result type (might be due to invalid IL or missing references)
		//IL_0a08: Unknown result type (might be due to invalid IL or missing references)
		//IL_0a0f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0a11: Unknown result type (might be due to invalid IL or missing references)
		//IL_0a18: Unknown result type (might be due to invalid IL or missing references)
		//IL_0a1a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0a22: Unknown result type (might be due to invalid IL or missing references)
		//IL_0a24: Unknown result type (might be due to invalid IL or missing references)
		//IL_0a2c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0a2e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0a36: Unknown result type (might be due to invalid IL or missing references)
		//IL_0a38: Unknown result type (might be due to invalid IL or missing references)
		//IL_0a40: Unknown result type (might be due to invalid IL or missing references)
		//IL_0a42: Unknown result type (might be due to invalid IL or missing references)
		//IL_0a4a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0a4c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0a54: Unknown result type (might be due to invalid IL or missing references)
		//IL_0a56: Unknown result type (might be due to invalid IL or missing references)
		//IL_0a5e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0a60: Unknown result type (might be due to invalid IL or missing references)
		//IL_0a68: Unknown result type (might be due to invalid IL or missing references)
		//IL_0a6a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0a72: Unknown result type (might be due to invalid IL or missing references)
		//IL_0a74: Unknown result type (might be due to invalid IL or missing references)
		//IL_0a7c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0a7e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0a86: Unknown result type (might be due to invalid IL or missing references)
		//IL_0a88: Unknown result type (might be due to invalid IL or missing references)
		//IL_0a90: Unknown result type (might be due to invalid IL or missing references)
		//IL_0a92: Unknown result type (might be due to invalid IL or missing references)
		//IL_0a9a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0a9c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0aa4: Unknown result type (might be due to invalid IL or missing references)
		//IL_0aa6: Unknown result type (might be due to invalid IL or missing references)
		//IL_0aae: Unknown result type (might be due to invalid IL or missing references)
		//IL_0ab0: Unknown result type (might be due to invalid IL or missing references)
		//IL_0ab8: Unknown result type (might be due to invalid IL or missing references)
		//IL_0aba: Unknown result type (might be due to invalid IL or missing references)
		//IL_0ac2: Unknown result type (might be due to invalid IL or missing references)
		//IL_0ac4: Unknown result type (might be due to invalid IL or missing references)
		//IL_0acc: Unknown result type (might be due to invalid IL or missing references)
		//IL_0ace: Unknown result type (might be due to invalid IL or missing references)
		//IL_0ad6: Unknown result type (might be due to invalid IL or missing references)
		//IL_0ad8: Unknown result type (might be due to invalid IL or missing references)
		//IL_0ae0: Unknown result type (might be due to invalid IL or missing references)
		//IL_0ae2: Unknown result type (might be due to invalid IL or missing references)
		//IL_0aea: Unknown result type (might be due to invalid IL or missing references)
		//IL_0aec: Unknown result type (might be due to invalid IL or missing references)
		//IL_0af4: Unknown result type (might be due to invalid IL or missing references)
		//IL_0af6: Unknown result type (might be due to invalid IL or missing references)
		//IL_0afe: Unknown result type (might be due to invalid IL or missing references)
		//IL_0b00: Unknown result type (might be due to invalid IL or missing references)
		//IL_0b08: Unknown result type (might be due to invalid IL or missing references)
		//IL_0b0a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0b12: Unknown result type (might be due to invalid IL or missing references)
		//IL_0b14: Unknown result type (might be due to invalid IL or missing references)
		//IL_0b1c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0b1e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0851: Unknown result type (might be due to invalid IL or missing references)
		//IL_0853: Unknown result type (might be due to invalid IL or missing references)
		//IL_085a: Unknown result type (might be due to invalid IL or missing references)
		//IL_085c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0863: Unknown result type (might be due to invalid IL or missing references)
		//IL_0865: Unknown result type (might be due to invalid IL or missing references)
		//IL_086c: Unknown result type (might be due to invalid IL or missing references)
		//IL_086e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0875: Unknown result type (might be due to invalid IL or missing references)
		//IL_0877: Unknown result type (might be due to invalid IL or missing references)
		//IL_087e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0880: Unknown result type (might be due to invalid IL or missing references)
		//IL_0887: Unknown result type (might be due to invalid IL or missing references)
		//IL_0889: Unknown result type (might be due to invalid IL or missing references)
		//IL_0890: Unknown result type (might be due to invalid IL or missing references)
		//IL_0892: Unknown result type (might be due to invalid IL or missing references)
		//IL_0899: Unknown result type (might be due to invalid IL or missing references)
		//IL_089b: Unknown result type (might be due to invalid IL or missing references)
		//IL_08a3: Unknown result type (might be due to invalid IL or missing references)
		//IL_08a5: Unknown result type (might be due to invalid IL or missing references)
		//IL_08ad: Unknown result type (might be due to invalid IL or missing references)
		//IL_08af: Unknown result type (might be due to invalid IL or missing references)
		//IL_08b7: Unknown result type (might be due to invalid IL or missing references)
		//IL_08b9: Unknown result type (might be due to invalid IL or missing references)
		//IL_08c1: Unknown result type (might be due to invalid IL or missing references)
		//IL_08c3: Unknown result type (might be due to invalid IL or missing references)
		//IL_08cb: Unknown result type (might be due to invalid IL or missing references)
		//IL_08cd: Unknown result type (might be due to invalid IL or missing references)
		//IL_08d5: Unknown result type (might be due to invalid IL or missing references)
		//IL_08d7: Unknown result type (might be due to invalid IL or missing references)
		//IL_08df: Unknown result type (might be due to invalid IL or missing references)
		//IL_08e1: Unknown result type (might be due to invalid IL or missing references)
		//IL_08e9: Unknown result type (might be due to invalid IL or missing references)
		//IL_08eb: Unknown result type (might be due to invalid IL or missing references)
		//IL_08f3: Unknown result type (might be due to invalid IL or missing references)
		//IL_08f5: Unknown result type (might be due to invalid IL or missing references)
		//IL_08fd: Unknown result type (might be due to invalid IL or missing references)
		//IL_08ff: Unknown result type (might be due to invalid IL or missing references)
		//IL_0907: Unknown result type (might be due to invalid IL or missing references)
		//IL_0909: Unknown result type (might be due to invalid IL or missing references)
		//IL_0911: Unknown result type (might be due to invalid IL or missing references)
		//IL_0913: Unknown result type (might be due to invalid IL or missing references)
		//IL_091b: Unknown result type (might be due to invalid IL or missing references)
		//IL_091d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0925: Unknown result type (might be due to invalid IL or missing references)
		//IL_0927: Unknown result type (might be due to invalid IL or missing references)
		//IL_092f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0931: Unknown result type (might be due to invalid IL or missing references)
		//IL_0939: Unknown result type (might be due to invalid IL or missing references)
		//IL_093b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0943: Unknown result type (might be due to invalid IL or missing references)
		//IL_0945: Unknown result type (might be due to invalid IL or missing references)
		//IL_094d: Unknown result type (might be due to invalid IL or missing references)
		//IL_094f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0957: Unknown result type (might be due to invalid IL or missing references)
		//IL_0959: Unknown result type (might be due to invalid IL or missing references)
		//IL_0961: Unknown result type (might be due to invalid IL or missing references)
		//IL_0963: Unknown result type (might be due to invalid IL or missing references)
		//IL_096b: Unknown result type (might be due to invalid IL or missing references)
		//IL_096d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0975: Unknown result type (might be due to invalid IL or missing references)
		//IL_0977: Unknown result type (might be due to invalid IL or missing references)
		//IL_097f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0981: Unknown result type (might be due to invalid IL or missing references)
		//IL_0989: Unknown result type (might be due to invalid IL or missing references)
		//IL_098b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0993: Unknown result type (might be due to invalid IL or missing references)
		//IL_0995: Unknown result type (might be due to invalid IL or missing references)
		//IL_099d: Unknown result type (might be due to invalid IL or missing references)
		//IL_099f: Unknown result type (might be due to invalid IL or missing references)
		//IL_09a7: Unknown result type (might be due to invalid IL or missing references)
		//IL_09a9: Unknown result type (might be due to invalid IL or missing references)
		//IL_09b1: Unknown result type (might be due to invalid IL or missing references)
		//IL_09b3: Unknown result type (might be due to invalid IL or missing references)
		//IL_09bb: Unknown result type (might be due to invalid IL or missing references)
		//IL_09bd: Unknown result type (might be due to invalid IL or missing references)
		//IL_0c44: Unknown result type (might be due to invalid IL or missing references)
		//IL_0c65: Unknown result type (might be due to invalid IL or missing references)
		//IL_0c6a: Unknown result type (might be due to invalid IL or missing references)
		BeginEntry(door, destinationInside);
		arrivalVehicle = arrivalVehicleForEntry;
		if (!((UnityEngine.Object)(object)fighter == (UnityEngine.Object)null) && !((UnityEngine.Object)(object)doorway == (UnityEngine.Object)null))
		{
			vehicleRouteDetourCount = 0;
			AllowVisitorThroughPlayerRoadBlocker();
			RestoreVisitorVehicleCollisions();
			Debug.Log(
				$"GYMCHAOS_VISITOR_ENTRY_BOUNDARY_EXCEPTION enemy={fighter.Identity} enabled=1",
				this);
			float y = ((Component)fighter).transform.position.y;
			float laneVariation = GetVehicleRouteVariation(vehicleSpawnPoint.x);
			int num;
			if (GymRoadsideBusStop.IsBuilt)
			{
				Vector3 val = Vector3.ProjectOnPlane(vehicleSpawnPoint - GymRoadsideBusStop.DavieBusPassengerPoint, Vector3.up);
				num = ((val.sqrMagnitude < 0.25f) ? 1 : 0);
			}
			else
			{
				num = 0;
			}
			bool isDavieBusDropoff = (byte)num != 0;
			vehicleEntryUsesDavieBusGate = isDavieBusDropoff;
			// The store is only reached through its west entry walkway; arrivals
			// walk the direct parking-to-door route, never round the shop.
			vehicleEntryUsesProteinStoreRoute = false;
			Vector3 genericSafeTurn = ResolveSafeExteriorLane(GymOutdoorBuilder.VisitorParkingTurnPoint, y, laneVariation);
			Vector3 exteriorClear = GetExteriorDoorClearPoint(y);
			genericSafeTurn = KeepLaneOutsideDoorWall(genericSafeTurn, exteriorClear, y);
			Vector3 safeTurn = genericSafeTurn;
			vehicleEntrySpawnPoint = new Vector3(vehicleSpawnPoint.x, y, vehicleSpawnPoint.z);
			vehicleEntrySpawnPending = true;
			Vector3 aisleApproach = default(Vector3);
			aisleApproach = new Vector3(parkingAislePoint.x, y, parkingAislePoint.z);
			// Bind the visitor's collision exception to the vehicle that actually
			// delivered this visitor. Nearest-vehicle selection is unstable when
			// several bays are occupied and can leave the active route blocked by
			// a neighbouring parked car.
			IgnoreRouteVehicleCollision(
				vehicleSpawnPoint,
				isDavieBusDropoff,
				arrivalVehicleForEntry);
			Vector3 busFrontApproach = GymRoadsideBusStop.DavieBusFrontApproachPoint;
			Vector3 parkingNorthGate = GymOutdoorBuilder.VisitorParkingNorthGatePoint;
			parkingNorthGate.y = y;
			busFrontApproach.y = y;
			Vector3 busPedestrianExit = GymRoadsideBusStop.DavieBusPedestrianExitPoint;
			busPedestrianExit.y = y;
			Vector3 busPedestrianRoadCrossing = default(Vector3);
			busPedestrianRoadCrossing = new Vector3(busPedestrianExit.x, y, parkingNorthGate.z);
			Vector3 parkingNorthLane = default(Vector3);
			parkingNorthLane = new Vector3(parkingNorthGate.x, y, GymOutdoorBuilder.VisitorParkingEntryPoint.z);
			float aisleExitDirection = Mathf.Sign(GymOutdoorBuilder.VisitorParkingEntryPoint.x - aisleApproach.x);
			aisleApproach.x += aisleExitDirection * Mathf.Min(5.5f, Mathf.Abs(GymOutdoorBuilder.VisitorParkingEntryPoint.x - aisleApproach.x));
			Vector3 doorwayExterior = default(Vector3);
			doorwayExterior = new Vector3(doorway.ExteriorPoint.x, y, doorway.ExteriorPoint.z);
			vehicleEntryWaypoints = (Vector3[])(object)(isDavieBusDropoff ? new Vector3[9]
			{
				busFrontApproach,
				busPedestrianExit,
				busPedestrianRoadCrossing,
				parkingNorthGate,
				parkingNorthLane,
				new Vector3(GymOutdoorBuilder.VisitorParkingEntryPoint.x, y, GymOutdoorBuilder.VisitorParkingEntryPoint.z),
				safeTurn,
				GetExteriorDoorQueuePoint(y),
				new Vector3(doorway.ExteriorPoint.x, y, doorway.ExteriorPoint.z)
			} : new Vector3[5]
			{
				aisleApproach,
				new Vector3(GymOutdoorBuilder.VisitorParkingEntryPoint.x, y, GymOutdoorBuilder.VisitorParkingEntryPoint.z),
				safeTurn,
				GetExteriorDoorQueuePoint(y),
				new Vector3(doorway.ExteriorPoint.x, y, doorway.ExteriorPoint.z)
			});
			vehicleEntryWaypointIndex = FindInitialRouteWaypoint(vehicleEntryWaypoints, vehicleEntrySpawnPoint, 2.2f);
			travelTarget = vehicleEntryWaypoints[vehicleEntryWaypointIndex];
			vehicleDetourActive = false;
			vehicleDetourResumeTarget = Vector3.zero;
			vehicleRouteStalledSeconds = 0f;
			vehicleRerouteAttempt = 0;
			Vector3 entryDistanceVector = Vector3.ProjectOnPlane(
				travelTarget - fighter.VisitorPhysicsPosition, Vector3.up);
			lastVehicleRouteDistance = entryDistanceVector.magnitude;
			state = VisitorState.ApproachingGymFromVehicle;
		}
	}
	public bool BeginVehicleApproach(Vector3 vehiclePoint, float boardingRadius = 0.55f)
	{
		//IL_004d: Unknown result type (might be due to invalid IL or missing references)
		//IL_004e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0076: Unknown result type (might be due to invalid IL or missing references)
		//IL_008e: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b6: Unknown result type (might be due to invalid IL or missing references)
		//IL_00bb: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c0: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c5: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ca: Unknown result type (might be due to invalid IL or missing references)
		//IL_00cf: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f1: Unknown result type (might be due to invalid IL or missing references)
		//IL_0101: Unknown result type (might be due to invalid IL or missing references)
		//IL_010c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0111: Unknown result type (might be due to invalid IL or missing references)
		//IL_0129: Unknown result type (might be due to invalid IL or missing references)
		//IL_0133: Unknown result type (might be due to invalid IL or missing references)
		//IL_0138: Unknown result type (might be due to invalid IL or missing references)
		//IL_013c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0146: Unknown result type (might be due to invalid IL or missing references)
		//IL_014b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0150: Unknown result type (might be due to invalid IL or missing references)
		//IL_0155: Unknown result type (might be due to invalid IL or missing references)
		//IL_0156: Unknown result type (might be due to invalid IL or missing references)
		//IL_015b: Unknown result type (might be due to invalid IL or missing references)
		//IL_016a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0186: Unknown result type (might be due to invalid IL or missing references)
		//IL_01bc: Unknown result type (might be due to invalid IL or missing references)
		//IL_01c6: Unknown result type (might be due to invalid IL or missing references)
		//IL_01ae: Unknown result type (might be due to invalid IL or missing references)
		//IL_01cb: Unknown result type (might be due to invalid IL or missing references)
		//IL_01ce: Unknown result type (might be due to invalid IL or missing references)
		//IL_01cf: Unknown result type (might be due to invalid IL or missing references)
		//IL_01dc: Unknown result type (might be due to invalid IL or missing references)
		//IL_01e6: Unknown result type (might be due to invalid IL or missing references)
		//IL_01eb: Unknown result type (might be due to invalid IL or missing references)
		//IL_0233: Unknown result type (might be due to invalid IL or missing references)
		//IL_0248: Unknown result type (might be due to invalid IL or missing references)
		//IL_0252: Unknown result type (might be due to invalid IL or missing references)
		//IL_026a: Unknown result type (might be due to invalid IL or missing references)
		//IL_026c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0273: Unknown result type (might be due to invalid IL or missing references)
		//IL_0274: Unknown result type (might be due to invalid IL or missing references)
		//IL_027b: Unknown result type (might be due to invalid IL or missing references)
		//IL_027d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0285: Unknown result type (might be due to invalid IL or missing references)
		//IL_028a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0291: Unknown result type (might be due to invalid IL or missing references)
		//IL_0293: Unknown result type (might be due to invalid IL or missing references)
		//IL_029a: Unknown result type (might be due to invalid IL or missing references)
		//IL_029b: Unknown result type (might be due to invalid IL or missing references)
		//IL_02a3: Unknown result type (might be due to invalid IL or missing references)
		//IL_02a8: Unknown result type (might be due to invalid IL or missing references)
		//IL_01f6: Unknown result type (might be due to invalid IL or missing references)
		//IL_01fb: Unknown result type (might be due to invalid IL or missing references)
		//IL_0200: Unknown result type (might be due to invalid IL or missing references)
		//IL_0202: Unknown result type (might be due to invalid IL or missing references)
		//IL_0209: Unknown result type (might be due to invalid IL or missing references)
		//IL_020e: Unknown result type (might be due to invalid IL or missing references)
		//IL_021c: Unknown result type (might be due to invalid IL or missing references)
		//IL_02d0: Unknown result type (might be due to invalid IL or missing references)
		//IL_02d5: Unknown result type (might be due to invalid IL or missing references)
		//IL_02d7: Unknown result type (might be due to invalid IL or missing references)
		//IL_02dc: Unknown result type (might be due to invalid IL or missing references)
		//IL_02eb: Unknown result type (might be due to invalid IL or missing references)
		//IL_0307: Unknown result type (might be due to invalid IL or missing references)
		//IL_0318: Unknown result type (might be due to invalid IL or missing references)
		//IL_032d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0337: Unknown result type (might be due to invalid IL or missing references)
		//IL_0ec3: Unknown result type (might be due to invalid IL or missing references)
		//IL_0ee4: Unknown result type (might be due to invalid IL or missing references)
		//IL_0ee9: Unknown result type (might be due to invalid IL or missing references)
		//IL_0f17: Unknown result type (might be due to invalid IL or missing references)
		//IL_0f27: Unknown result type (might be due to invalid IL or missing references)
		//IL_0f2c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0f31: Unknown result type (might be due to invalid IL or missing references)
		//IL_0f36: Unknown result type (might be due to invalid IL or missing references)
		//IL_0f3b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0f87: Unknown result type (might be due to invalid IL or missing references)
		//IL_039c: Unknown result type (might be due to invalid IL or missing references)
		//IL_039e: Unknown result type (might be due to invalid IL or missing references)
		//IL_03a5: Unknown result type (might be due to invalid IL or missing references)
		//IL_03a7: Unknown result type (might be due to invalid IL or missing references)
		//IL_03ae: Unknown result type (might be due to invalid IL or missing references)
		//IL_03b0: Unknown result type (might be due to invalid IL or missing references)
		//IL_03b7: Unknown result type (might be due to invalid IL or missing references)
		//IL_03b8: Unknown result type (might be due to invalid IL or missing references)
		//IL_03bf: Unknown result type (might be due to invalid IL or missing references)
		//IL_03c1: Unknown result type (might be due to invalid IL or missing references)
		//IL_03c9: Unknown result type (might be due to invalid IL or missing references)
		//IL_03ce: Unknown result type (might be due to invalid IL or missing references)
		//IL_03d6: Unknown result type (might be due to invalid IL or missing references)
		//IL_03db: Unknown result type (might be due to invalid IL or missing references)
		//IL_0357: Unknown result type (might be due to invalid IL or missing references)
		//IL_0359: Unknown result type (might be due to invalid IL or missing references)
		//IL_0360: Unknown result type (might be due to invalid IL or missing references)
		//IL_0362: Unknown result type (might be due to invalid IL or missing references)
		//IL_0369: Unknown result type (might be due to invalid IL or missing references)
		//IL_036a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0371: Unknown result type (might be due to invalid IL or missing references)
		//IL_0373: Unknown result type (might be due to invalid IL or missing references)
		//IL_037b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0380: Unknown result type (might be due to invalid IL or missing references)
		//IL_0388: Unknown result type (might be due to invalid IL or missing references)
		//IL_038d: Unknown result type (might be due to invalid IL or missing references)
		//IL_03fb: Unknown result type (might be due to invalid IL or missing references)
		//IL_0400: Unknown result type (might be due to invalid IL or missing references)
		//IL_0402: Unknown result type (might be due to invalid IL or missing references)
		//IL_0407: Unknown result type (might be due to invalid IL or missing references)
		//IL_0409: Unknown result type (might be due to invalid IL or missing references)
		//IL_040e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0410: Unknown result type (might be due to invalid IL or missing references)
		//IL_0415: Unknown result type (might be due to invalid IL or missing references)
		//IL_0417: Unknown result type (might be due to invalid IL or missing references)
		//IL_041c: Unknown result type (might be due to invalid IL or missing references)
		//IL_041e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0423: Unknown result type (might be due to invalid IL or missing references)
		//IL_0425: Unknown result type (might be due to invalid IL or missing references)
		//IL_042a: Unknown result type (might be due to invalid IL or missing references)
		//IL_042c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0431: Unknown result type (might be due to invalid IL or missing references)
		//IL_0433: Unknown result type (might be due to invalid IL or missing references)
		//IL_0438: Unknown result type (might be due to invalid IL or missing references)
		//IL_0447: Unknown result type (might be due to invalid IL or missing references)
		//IL_0463: Unknown result type (might be due to invalid IL or missing references)
		//IL_047f: Unknown result type (might be due to invalid IL or missing references)
		//IL_049b: Unknown result type (might be due to invalid IL or missing references)
		//IL_04b7: Unknown result type (might be due to invalid IL or missing references)
		//IL_04d3: Unknown result type (might be due to invalid IL or missing references)
		//IL_04ef: Unknown result type (might be due to invalid IL or missing references)
		//IL_050b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0520: Unknown result type (might be due to invalid IL or missing references)
		//IL_0560: Unknown result type (might be due to invalid IL or missing references)
		//IL_056e: Unknown result type (might be due to invalid IL or missing references)
		//IL_057a: Unknown result type (might be due to invalid IL or missing references)
		//IL_058b: Unknown result type (might be due to invalid IL or missing references)
		//IL_059a: Unknown result type (might be due to invalid IL or missing references)
		//IL_05ae: Unknown result type (might be due to invalid IL or missing references)
		//IL_05bd: Unknown result type (might be due to invalid IL or missing references)
		//IL_05d1: Unknown result type (might be due to invalid IL or missing references)
		//IL_05e0: Unknown result type (might be due to invalid IL or missing references)
		//IL_05f4: Unknown result type (might be due to invalid IL or missing references)
		//IL_060c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0616: Unknown result type (might be due to invalid IL or missing references)
		//IL_062a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0648: Unknown result type (might be due to invalid IL or missing references)
		//IL_0652: Unknown result type (might be due to invalid IL or missing references)
		//IL_066c: Unknown result type (might be due to invalid IL or missing references)
		//IL_068a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0694: Unknown result type (might be due to invalid IL or missing references)
		//IL_06ae: Unknown result type (might be due to invalid IL or missing references)
		//IL_06c6: Unknown result type (might be due to invalid IL or missing references)
		//IL_06d0: Unknown result type (might be due to invalid IL or missing references)
		//IL_06e4: Unknown result type (might be due to invalid IL or missing references)
		//IL_06fc: Unknown result type (might be due to invalid IL or missing references)
		//IL_0706: Unknown result type (might be due to invalid IL or missing references)
		//IL_071a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0732: Unknown result type (might be due to invalid IL or missing references)
		//IL_073c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0750: Unknown result type (might be due to invalid IL or missing references)
		//IL_0768: Unknown result type (might be due to invalid IL or missing references)
		//IL_0772: Unknown result type (might be due to invalid IL or missing references)
		//IL_0786: Unknown result type (might be due to invalid IL or missing references)
		//IL_079e: Unknown result type (might be due to invalid IL or missing references)
		//IL_07a8: Unknown result type (might be due to invalid IL or missing references)
		//IL_07b6: Unknown result type (might be due to invalid IL or missing references)
		//IL_07c8: Unknown result type (might be due to invalid IL or missing references)
		//IL_07d2: Unknown result type (might be due to invalid IL or missing references)
		//IL_07e6: Unknown result type (might be due to invalid IL or missing references)
		//IL_07fe: Unknown result type (might be due to invalid IL or missing references)
		//IL_0808: Unknown result type (might be due to invalid IL or missing references)
		//IL_081c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0834: Unknown result type (might be due to invalid IL or missing references)
		//IL_083e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0852: Unknown result type (might be due to invalid IL or missing references)
		//IL_086a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0874: Unknown result type (might be due to invalid IL or missing references)
		//IL_0882: Unknown result type (might be due to invalid IL or missing references)
		//IL_089a: Unknown result type (might be due to invalid IL or missing references)
		//IL_08a4: Unknown result type (might be due to invalid IL or missing references)
		//IL_08b8: Unknown result type (might be due to invalid IL or missing references)
		//IL_08d0: Unknown result type (might be due to invalid IL or missing references)
		//IL_08da: Unknown result type (might be due to invalid IL or missing references)
		//IL_08ee: Unknown result type (might be due to invalid IL or missing references)
		//IL_0906: Unknown result type (might be due to invalid IL or missing references)
		//IL_0910: Unknown result type (might be due to invalid IL or missing references)
		//IL_0924: Unknown result type (might be due to invalid IL or missing references)
		//IL_093c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0946: Unknown result type (might be due to invalid IL or missing references)
		//IL_095a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0972: Unknown result type (might be due to invalid IL or missing references)
		//IL_097c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0990: Unknown result type (might be due to invalid IL or missing references)
		//IL_09a8: Unknown result type (might be due to invalid IL or missing references)
		//IL_09b2: Unknown result type (might be due to invalid IL or missing references)
		//IL_09c0: Unknown result type (might be due to invalid IL or missing references)
		//IL_09d8: Unknown result type (might be due to invalid IL or missing references)
		//IL_09e2: Unknown result type (might be due to invalid IL or missing references)
		//IL_09f6: Unknown result type (might be due to invalid IL or missing references)
		//IL_0a0e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0a18: Unknown result type (might be due to invalid IL or missing references)
		//IL_0a2c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0a44: Unknown result type (might be due to invalid IL or missing references)
		//IL_0a4e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0a5c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0a74: Unknown result type (might be due to invalid IL or missing references)
		//IL_0a7e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0a92: Unknown result type (might be due to invalid IL or missing references)
		//IL_0aaa: Unknown result type (might be due to invalid IL or missing references)
		//IL_0ab4: Unknown result type (might be due to invalid IL or missing references)
		//IL_0ad3: Unknown result type (might be due to invalid IL or missing references)
		//IL_0e4e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0e50: Unknown result type (might be due to invalid IL or missing references)
		//IL_0e57: Unknown result type (might be due to invalid IL or missing references)
		//IL_0e58: Unknown result type (might be due to invalid IL or missing references)
		//IL_0e5f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0e74: Unknown result type (might be due to invalid IL or missing references)
		//IL_0e7e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0e88: Unknown result type (might be due to invalid IL or missing references)
		//IL_0e8d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0e95: Unknown result type (might be due to invalid IL or missing references)
		//IL_0e9a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0ea2: Unknown result type (might be due to invalid IL or missing references)
		//IL_0ea7: Unknown result type (might be due to invalid IL or missing references)
		//IL_0dd5: Unknown result type (might be due to invalid IL or missing references)
		//IL_0dd7: Unknown result type (might be due to invalid IL or missing references)
		//IL_0dde: Unknown result type (might be due to invalid IL or missing references)
		//IL_0ddf: Unknown result type (might be due to invalid IL or missing references)
		//IL_0de6: Unknown result type (might be due to invalid IL or missing references)
		//IL_0dfb: Unknown result type (might be due to invalid IL or missing references)
		//IL_0e05: Unknown result type (might be due to invalid IL or missing references)
		//IL_0e0f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0e14: Unknown result type (might be due to invalid IL or missing references)
		//IL_0e1c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0e21: Unknown result type (might be due to invalid IL or missing references)
		//IL_0e28: Unknown result type (might be due to invalid IL or missing references)
		//IL_0e2a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0e31: Unknown result type (might be due to invalid IL or missing references)
		//IL_0e32: Unknown result type (might be due to invalid IL or missing references)
		//IL_0e3a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0e3f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0c64: Unknown result type (might be due to invalid IL or missing references)
		//IL_0c66: Unknown result type (might be due to invalid IL or missing references)
		//IL_0c6d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0c6f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0c76: Unknown result type (might be due to invalid IL or missing references)
		//IL_0c78: Unknown result type (might be due to invalid IL or missing references)
		//IL_0c7f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0c81: Unknown result type (might be due to invalid IL or missing references)
		//IL_0c88: Unknown result type (might be due to invalid IL or missing references)
		//IL_0c8a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0c91: Unknown result type (might be due to invalid IL or missing references)
		//IL_0c93: Unknown result type (might be due to invalid IL or missing references)
		//IL_0c9a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0c9c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0ca3: Unknown result type (might be due to invalid IL or missing references)
		//IL_0ca5: Unknown result type (might be due to invalid IL or missing references)
		//IL_0cac: Unknown result type (might be due to invalid IL or missing references)
		//IL_0cae: Unknown result type (might be due to invalid IL or missing references)
		//IL_0cb6: Unknown result type (might be due to invalid IL or missing references)
		//IL_0cb8: Unknown result type (might be due to invalid IL or missing references)
		//IL_0cc0: Unknown result type (might be due to invalid IL or missing references)
		//IL_0cc2: Unknown result type (might be due to invalid IL or missing references)
		//IL_0cca: Unknown result type (might be due to invalid IL or missing references)
		//IL_0ccc: Unknown result type (might be due to invalid IL or missing references)
		//IL_0cd4: Unknown result type (might be due to invalid IL or missing references)
		//IL_0cd6: Unknown result type (might be due to invalid IL or missing references)
		//IL_0cde: Unknown result type (might be due to invalid IL or missing references)
		//IL_0ce0: Unknown result type (might be due to invalid IL or missing references)
		//IL_0ce8: Unknown result type (might be due to invalid IL or missing references)
		//IL_0cea: Unknown result type (might be due to invalid IL or missing references)
		//IL_0cf2: Unknown result type (might be due to invalid IL or missing references)
		//IL_0cf4: Unknown result type (might be due to invalid IL or missing references)
		//IL_0cfc: Unknown result type (might be due to invalid IL or missing references)
		//IL_0cfe: Unknown result type (might be due to invalid IL or missing references)
		//IL_0d06: Unknown result type (might be due to invalid IL or missing references)
		//IL_0d08: Unknown result type (might be due to invalid IL or missing references)
		//IL_0d10: Unknown result type (might be due to invalid IL or missing references)
		//IL_0d12: Unknown result type (might be due to invalid IL or missing references)
		//IL_0d1a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0d1c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0d24: Unknown result type (might be due to invalid IL or missing references)
		//IL_0d26: Unknown result type (might be due to invalid IL or missing references)
		//IL_0d2e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0d30: Unknown result type (might be due to invalid IL or missing references)
		//IL_0d38: Unknown result type (might be due to invalid IL or missing references)
		//IL_0d3a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0d42: Unknown result type (might be due to invalid IL or missing references)
		//IL_0d44: Unknown result type (might be due to invalid IL or missing references)
		//IL_0d4c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0d4e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0d56: Unknown result type (might be due to invalid IL or missing references)
		//IL_0d58: Unknown result type (might be due to invalid IL or missing references)
		//IL_0d60: Unknown result type (might be due to invalid IL or missing references)
		//IL_0d62: Unknown result type (might be due to invalid IL or missing references)
		//IL_0d6a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0d6c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0d74: Unknown result type (might be due to invalid IL or missing references)
		//IL_0d76: Unknown result type (might be due to invalid IL or missing references)
		//IL_0d7e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0d80: Unknown result type (might be due to invalid IL or missing references)
		//IL_0d88: Unknown result type (might be due to invalid IL or missing references)
		//IL_0d8a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0d92: Unknown result type (might be due to invalid IL or missing references)
		//IL_0d94: Unknown result type (might be due to invalid IL or missing references)
		//IL_0d9c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0d9e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0da7: Unknown result type (might be due to invalid IL or missing references)
		//IL_0dac: Unknown result type (might be due to invalid IL or missing references)
		//IL_0db5: Unknown result type (might be due to invalid IL or missing references)
		//IL_0dba: Unknown result type (might be due to invalid IL or missing references)
		//IL_0af2: Unknown result type (might be due to invalid IL or missing references)
		//IL_0af4: Unknown result type (might be due to invalid IL or missing references)
		//IL_0afb: Unknown result type (might be due to invalid IL or missing references)
		//IL_0afd: Unknown result type (might be due to invalid IL or missing references)
		//IL_0b04: Unknown result type (might be due to invalid IL or missing references)
		//IL_0b06: Unknown result type (might be due to invalid IL or missing references)
		//IL_0b0d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0b0f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0b16: Unknown result type (might be due to invalid IL or missing references)
		//IL_0b18: Unknown result type (might be due to invalid IL or missing references)
		//IL_0b1f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0b21: Unknown result type (might be due to invalid IL or missing references)
		//IL_0b28: Unknown result type (might be due to invalid IL or missing references)
		//IL_0b2a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0b31: Unknown result type (might be due to invalid IL or missing references)
		//IL_0b33: Unknown result type (might be due to invalid IL or missing references)
		//IL_0b3a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0b3c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0b44: Unknown result type (might be due to invalid IL or missing references)
		//IL_0b46: Unknown result type (might be due to invalid IL or missing references)
		//IL_0b4e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0b50: Unknown result type (might be due to invalid IL or missing references)
		//IL_0b58: Unknown result type (might be due to invalid IL or missing references)
		//IL_0b5a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0b62: Unknown result type (might be due to invalid IL or missing references)
		//IL_0b64: Unknown result type (might be due to invalid IL or missing references)
		//IL_0b6c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0b6e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0b76: Unknown result type (might be due to invalid IL or missing references)
		//IL_0b78: Unknown result type (might be due to invalid IL or missing references)
		//IL_0b80: Unknown result type (might be due to invalid IL or missing references)
		//IL_0b82: Unknown result type (might be due to invalid IL or missing references)
		//IL_0b8a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0b8c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0b94: Unknown result type (might be due to invalid IL or missing references)
		//IL_0b96: Unknown result type (might be due to invalid IL or missing references)
		//IL_0b9e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0ba0: Unknown result type (might be due to invalid IL or missing references)
		//IL_0ba8: Unknown result type (might be due to invalid IL or missing references)
		//IL_0baa: Unknown result type (might be due to invalid IL or missing references)
		//IL_0bb2: Unknown result type (might be due to invalid IL or missing references)
		//IL_0bb4: Unknown result type (might be due to invalid IL or missing references)
		//IL_0bbc: Unknown result type (might be due to invalid IL or missing references)
		//IL_0bbe: Unknown result type (might be due to invalid IL or missing references)
		//IL_0bc6: Unknown result type (might be due to invalid IL or missing references)
		//IL_0bc8: Unknown result type (might be due to invalid IL or missing references)
		//IL_0bd0: Unknown result type (might be due to invalid IL or missing references)
		//IL_0bd2: Unknown result type (might be due to invalid IL or missing references)
		//IL_0bda: Unknown result type (might be due to invalid IL or missing references)
		//IL_0bdc: Unknown result type (might be due to invalid IL or missing references)
		//IL_0be4: Unknown result type (might be due to invalid IL or missing references)
		//IL_0be6: Unknown result type (might be due to invalid IL or missing references)
		//IL_0bee: Unknown result type (might be due to invalid IL or missing references)
		//IL_0bf0: Unknown result type (might be due to invalid IL or missing references)
		//IL_0bf8: Unknown result type (might be due to invalid IL or missing references)
		//IL_0bfa: Unknown result type (might be due to invalid IL or missing references)
		//IL_0c02: Unknown result type (might be due to invalid IL or missing references)
		//IL_0c04: Unknown result type (might be due to invalid IL or missing references)
		//IL_0c0c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0c0e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0c16: Unknown result type (might be due to invalid IL or missing references)
		//IL_0c18: Unknown result type (might be due to invalid IL or missing references)
		//IL_0c20: Unknown result type (might be due to invalid IL or missing references)
		//IL_0c22: Unknown result type (might be due to invalid IL or missing references)
		//IL_0c2b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0c30: Unknown result type (might be due to invalid IL or missing references)
		//IL_0c38: Unknown result type (might be due to invalid IL or missing references)
		//IL_0c3a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0c42: Unknown result type (might be due to invalid IL or missing references)
		//IL_0c43: Unknown result type (might be due to invalid IL or missing references)
		//IL_0c4c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0c51: Unknown result type (might be due to invalid IL or missing references)
		if ((UnityEngine.Object)(object)fighter == (UnityEngine.Object)null || !CanDeactivate)
		{
			return false;
		}
		if ((UnityEngine.Object)(object)activeVehicleApproachAgent != (UnityEngine.Object)null &&
			(UnityEngine.Object)(object)activeVehicleApproachAgent != (UnityEngine.Object)(object)this &&
			((Behaviour)activeVehicleApproachAgent).isActiveAndEnabled &&
			activeVehicleApproachAgent.IsUsingSharedParkingConnector)
		{
			return false;
		}
		if (IsOtherInboundOnSharedConnector(this))
		{
			return false;
		}
		if ((UnityEngine.Object)(object)activeVehicleApproachAgent == (UnityEngine.Object)null ||
			!((Behaviour)activeVehicleApproachAgent).isActiveAndEnabled ||
			!activeVehicleApproachAgent.IsUsingSharedParkingConnector)
		{
			activeVehicleApproachAgent = null;
		}
		activeVehicleApproachAgent = this;
		CancelVehicleYield();
		finalVehicleTarget = vehiclePoint;
		vehicleBoardingRadius = Mathf.Clamp(boardingRadius, 0.35f, 2.2f);
		AllowVisitorThroughPlayerRoadBlocker();
		RestoreVisitorVehicleCollisions();
		IgnoreRouteVehicleCollision(
			vehiclePoint,
			arrivalVehicle != null && arrivalVehicle.IsBus,
			arrivalVehicle);
		finalVehicleTarget.y = ((Component)fighter).transform.position.y;
		float laneVariation = GetVehicleRouteVariation(finalVehicleTarget.x);
		Vector3 val;
		int num;
		if (GymRoadsideBusStop.IsBuilt)
		{
			val = Vector3.ProjectOnPlane(finalVehicleTarget - GymRoadsideBusStop.DavieBusPassengerPoint, Vector3.up);
			num = ((val.sqrMagnitude < 0.25f) ? 1 : 0);
		}
		else
		{
			num = 0;
		}
		bool isDavieBusPickup = (byte)num != 0;
		vehicleExitUsesDavieBusGate = isDavieBusPickup;
		vehicleExitUsesProteinStoreRoute = false;
		Vector3 safeTurn = ResolveSafeExteriorLane(GymOutdoorBuilder.VisitorParkingTurnPoint, ((Component)fighter).transform.position.y, laneVariation);
		float x = finalVehicleTarget.x;
		float y = ((Component)fighter).transform.position.y;
		Bounds parkingBounds = GymOutdoorBuilder.ParkingBounds;
		vehicleAisleTarget = new Vector3(x, y, parkingBounds.center.z);
		Vector3 busFrontApproach = GymRoadsideBusStop.DavieBusFrontApproachPoint;
		Vector3 parkingNorthGate = GymOutdoorBuilder.VisitorParkingNorthGatePoint;
		parkingNorthGate.y = ((Component)fighter).transform.position.y;
		busFrontApproach.y = ((Component)fighter).transform.position.y;
		Vector3 exteriorClear = (((UnityEngine.Object)(object)doorway != (UnityEngine.Object)null) ? GetExteriorDoorClearPoint(fighter.VisitorPhysicsPosition.y) : ((Component)fighter).transform.position);
		safeTurn = KeepLaneOutsideDoorWall(safeTurn, exteriorClear, ((Component)fighter).transform.position.y);
		if (isDavieBusPickup)
		{
			Vector3 parkingEntry = default(Vector3);
			parkingEntry = new Vector3(GymOutdoorBuilder.VisitorParkingEntryPoint.x, ((Component)fighter).transform.position.y, GymOutdoorBuilder.VisitorParkingEntryPoint.z);
			vehicleExitWaypoints = (Vector3[])(object)new Vector3[7] { exteriorClear, safeTurn, parkingEntry, vehicleAisleTarget, parkingNorthGate, busFrontApproach, finalVehicleTarget };
		}
		else if ((UnityEngine.Object)(object)doorway == (UnityEngine.Object)null || externalVehicleRouteVerification)
		{
			Vector3 arrivalTurn = GymOutdoorBuilder.VehicleArrivalRoadTurnPoint;
			Vector3 arrivalJunction = GymOutdoorBuilder.VehicleArrivalRoadJunctionPoint;
			arrivalTurn.y = ((Component)fighter).transform.position.y;
			arrivalJunction.y = ((Component)fighter).transform.position.y;
			Vector3 parkingEntry2 = default(Vector3);
			parkingEntry2 = new Vector3(GymOutdoorBuilder.VisitorParkingEntryPoint.x, ((Component)fighter).transform.position.y, GymOutdoorBuilder.VisitorParkingEntryPoint.z);
			Vector3 parkingConnector = new Vector3(
				parkingEntry2.x - 0.9f,
				parkingEntry2.y,
				GymOutdoorBuilder.ParkingBounds.center.z -
					GymOutdoorBuilder.VehicleRoadWidthForVerification * 0.5f + 1.15f);
			if (externalVehicleRouteVerification &&
				GymOutdoorBuilder.HasProteinStoreRoute)
			{
				// The south-east parking boundary opens at the road edge.
				// Keep the preceding handoff above its capsule-clearance
				// line before crossing into the parking connector.
				safeTurn.z = Mathf.Max(safeTurn.z, parkingConnector.z + 0.85f);
				safeTurn.y = parkingEntry2.y;
			}
			vehicleExitWaypoints = (Vector3[])(object)(externalVehicleRouteVerification ? new Vector3[7] { arrivalTurn, arrivalJunction, safeTurn, parkingConnector, parkingEntry2, vehicleAisleTarget, finalVehicleTarget } : new Vector3[7] { exteriorClear, arrivalTurn, arrivalJunction, safeTurn, parkingEntry2, vehicleAisleTarget, finalVehicleTarget });
		}
		else
		{
			vehicleExitWaypoints = (Vector3[])(object)(isDavieBusPickup ? new Vector3[7]
			{
				exteriorClear,
				safeTurn,
				new Vector3(GymOutdoorBuilder.VisitorParkingEntryPoint.x, ((Component)fighter).transform.position.y, GymOutdoorBuilder.VisitorParkingEntryPoint.z),
				vehicleAisleTarget,
				parkingNorthGate,
				busFrontApproach,
				finalVehicleTarget
			} : new Vector3[5]
			{
				exteriorClear,
				safeTurn,
				new Vector3(GymOutdoorBuilder.VisitorParkingEntryPoint.x, ((Component)fighter).transform.position.y, GymOutdoorBuilder.VisitorParkingEntryPoint.z),
				vehicleAisleTarget,
				finalVehicleTarget
			});
		}
		vehicleExitWaypointIndex = FindResumeRouteWaypoint(vehicleExitWaypoints, ((Component)fighter).transform.position, FindInitialRouteWaypoint(vehicleExitWaypoints, ((Component)fighter).transform.position, 2.2f), 8f);
		travelTarget = vehicleExitWaypoints[vehicleExitWaypointIndex];
		vehicleTurnPending = true;
		vehicleEntryPending = true;
		vehicleAislePending = true;
		vehicleDetourActive = false;
		vehicleRouteStalledSeconds = 0f;
		val = Vector3.ProjectOnPlane(travelTarget - ((Component)fighter).transform.position, Vector3.up);
		lastVehicleRouteDistance = val.magnitude;
		vehicleRerouteAttempt = 0;
		vehicleRouteDetourCount = 0;
		reachedVehicle = false;
		state = VisitorState.ApproachingVehicle;
		fighter.StopVisitorMovement();
		Debug.Log((object)$"GYMCHAOS_VISITOR_WALK_TO_VEHICLE enemy={fighter.Identity} target={travelTarget}", (UnityEngine.Object)(object)this);
		return true;
	}
	public bool RequestVehicleYield(GymVisitorVehicle vehicle, Vector3 vehicleDirection)
	{
		//IL_007d: Unknown result type (might be due to invalid IL or missing references)
		//IL_009a: Unknown result type (might be due to invalid IL or missing references)
		//IL_009b: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a0: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a5: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ba: Unknown result type (might be due to invalid IL or missing references)
		//IL_00bf: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c4: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c9: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e1: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e6: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e7: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ec: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f0: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f5: Unknown result type (might be due to invalid IL or missing references)
		//IL_0101: Unknown result type (might be due to invalid IL or missing references)
		//IL_0106: Unknown result type (might be due to invalid IL or missing references)
		//IL_0107: Unknown result type (might be due to invalid IL or missing references)
		//IL_010e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0113: Unknown result type (might be due to invalid IL or missing references)
		//IL_0118: Unknown result type (might be due to invalid IL or missing references)
		//IL_011d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0122: Unknown result type (might be due to invalid IL or missing references)
		//IL_0165: Unknown result type (might be due to invalid IL or missing references)
		//IL_0166: Unknown result type (might be due to invalid IL or missing references)
		//IL_016f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0174: Unknown result type (might be due to invalid IL or missing references)
		//IL_0179: Unknown result type (might be due to invalid IL or missing references)
		//IL_017f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0184: Unknown result type (might be due to invalid IL or missing references)
		//IL_0189: Unknown result type (might be due to invalid IL or missing references)
		//IL_018c: Unknown result type (might be due to invalid IL or missing references)
		//IL_018e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0194: Unknown result type (might be due to invalid IL or missing references)
		//IL_0199: Unknown result type (might be due to invalid IL or missing references)
		//IL_019b: Unknown result type (might be due to invalid IL or missing references)
		//IL_019d: Unknown result type (might be due to invalid IL or missing references)
		//IL_019e: Unknown result type (might be due to invalid IL or missing references)
		//IL_01a3: Unknown result type (might be due to invalid IL or missing references)
		//IL_01a8: Unknown result type (might be due to invalid IL or missing references)
		//IL_01ad: Unknown result type (might be due to invalid IL or missing references)
		//IL_01c6: Unknown result type (might be due to invalid IL or missing references)
		//IL_01ec: Unknown result type (might be due to invalid IL or missing references)
		//IL_01ee: Unknown result type (might be due to invalid IL or missing references)
		//IL_023d: Unknown result type (might be due to invalid IL or missing references)
		if ((UnityEngine.Object)(object)vehicle == (UnityEngine.Object)null || (UnityEngine.Object)(object)fighter == (UnityEngine.Object)null || fighter.IsDead || (UnityEngine.Object)(object)yieldingToVehicle != (UnityEngine.Object)null)
		{
			return (UnityEngine.Object)(object)yieldingToVehicle == (UnityEngine.Object)(object)vehicle;
		}
		bool num = state == VisitorState.ApproachingGymFromVehicle || state == VisitorState.ExitingDoor || state == VisitorState.LeavingGym || state == VisitorState.ApproachingVehicle;
		bool isOutsideFreeRoam = state == VisitorState.FreeRoaming && GymOutdoorBuilder.IsPlayerOutsideGym(((Component)fighter).transform.position);
		if ((!num && !isOutsideFreeRoam) || !vehicle.IsDriving)
		{
			return false;
		}
		Vector3 direction = Vector3.ProjectOnPlane(vehicleDirection, Vector3.up);
		if (direction.sqrMagnitude < 0.01f)
		{
			direction = Vector3.ProjectOnPlane(((Component)vehicle).transform.forward, Vector3.up);
		}
		if (direction.sqrMagnitude < 0.01f)
		{
			return false;
		}
		direction.Normalize();
		Vector3 val = Vector3.Cross(Vector3.up, direction);
		Vector3 right = val.normalized;
		Vector3 current = ((Component)fighter).transform.position;
		float signedSide = Vector3.Dot(Vector3.ProjectOnPlane(current - ((Component)vehicle).transform.position, Vector3.up), right);
		float preferredSide = ((Mathf.Abs(signedSide) > 0.2f) ? Mathf.Sign(signedSide) : 1f);
		for (int attempt = 0; attempt < 2; attempt++)
		{
			float side = preferredSide * ((attempt == 0) ? 1f : (-1f));
			Vector3 candidate = current + right * (side * 3.2f) - direction * 0.6f;
			candidate = ClampVehicleYieldPoint(candidate, current.y);
			Vector3 step = Vector3.ProjectOnPlane(candidate - current, Vector3.up);
			if (!(step.sqrMagnitude < 1f) && fighter.IsVisitorExternalPathClear(step, step.magnitude))
			{
				fighter.StopVisitorMovement();
				yieldingToVehicle = vehicle;
				vehicleYieldPoint = candidate;
				vehicleYieldStartedAt = Time.time;
				vehicleYieldReachedAt = -1f;
				vehicleYieldCount++;
				Debug.Log((object)($"GYMCHAOS_VISITOR_VEHICLE_YIELD_REQUESTED enemy={fighter.Identity} " + $"vehicle={((UnityEngine.Object)vehicle).name} point={vehicleYieldPoint} " + $"attempt={attempt + 1}"), (UnityEngine.Object)(object)this);
				return true;
			}
		}
		return false;
	}
	private Vector3 ClampVehicleYieldPoint(Vector3 point, float y)
	{
		//IL_0008: Unknown result type (might be due to invalid IL or missing references)
		point.y = y;
		return point;
	}
	private void TickVehicleYield()
	{
		//IL_004a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0093: Unknown result type (might be due to invalid IL or missing references)
		//IL_00dc: Unknown result type (might be due to invalid IL or missing references)
		GymVisitorVehicle vehicle = yieldingToVehicle;
		if ((UnityEngine.Object)(object)vehicle == (UnityEngine.Object)null || (UnityEngine.Object)(object)fighter == (UnityEngine.Object)null)
		{
			ClearVehicleYield(log: false);
			return;
		}
		if (!vehicle.IsDriving || !((Component)vehicle).gameObject.activeInHierarchy)
		{
			ClearVehicleYield(log: true);
			return;
		}
		bool reached = fighter.MoveVisitorAlongExteriorRoute(vehicleYieldPoint, 3.2f);
		if (Time.time - vehicleYieldStartedAt >= 8f)
		{
			Debug.LogWarning((object)($"GYMCHAOS_VISITOR_VEHICLE_YIELD_TIMEOUT enemy={fighter.Identity} " + $"vehicle={((UnityEngine.Object)vehicle).name} point={vehicleYieldPoint}"), (UnityEngine.Object)(object)this);
			ClearVehicleYield(log: true);
		}
		else if (reached)
		{
			if (vehicleYieldReachedAt < 0f)
			{
				vehicleYieldReachedAt = Time.time;
			}
			bool num = vehicle.IsPedestrianClearForYield(((Component)fighter).transform.position);
			bool holdExpired = Time.time - vehicleYieldReachedAt >= 0.25f;
			if (num && holdExpired)
			{
				ClearVehicleYield(log: true);
			}
		}
	}
	private void ClearVehicleYield(bool log)
	{
		//IL_0088: Unknown result type (might be due to invalid IL or missing references)
		GymVisitorVehicle vehicle = yieldingToVehicle;
		yieldingToVehicle = null;
		vehicleYieldReachedAt = -1f;
		if (state == VisitorState.ApproachingVehicle)
		{
			ResetVehicleRouteProgress();
		}
		if ((UnityEngine.Object)(object)fighter != (UnityEngine.Object)null)
		{
			fighter.StopVisitorMovement();
		}
		if (log && (UnityEngine.Object)(object)vehicle != (UnityEngine.Object)null)
		{
			Debug.Log((object)($"GYMCHAOS_VISITOR_VEHICLE_YIELD_RELEASED enemy={fighter?.Identity} " + $"vehicle={((UnityEngine.Object)vehicle).name} point={vehicleYieldPoint}"), (UnityEngine.Object)(object)this);
		}
	}
	private void CancelVehicleYield()
	{
		yieldingToVehicle = null;
		vehicleYieldReachedAt = -1f;
	}
	private static float GetVehicleRouteVariation(float vehicleX)
	{
		//IL_0000: Unknown result type (might be due to invalid IL or missing references)
		//IL_0005: Unknown result type (might be due to invalid IL or missing references)
		//IL_0008: Unknown result type (might be due to invalid IL or missing references)
		//IL_0021: Unknown result type (might be due to invalid IL or missing references)
		//IL_002d: Unknown result type (might be due to invalid IL or missing references)
		Bounds parking = GymOutdoorBuilder.ParkingBounds;
		if (parking.size.x <= 1f)
		{
			return 0.5f;
		}
		float bayPosition = Mathf.InverseLerp(parking.min.x, parking.max.x, vehicleX);
		return Mathf.Lerp(0.24f, 0.76f, bayPosition);
	}
	private bool TryAcquireSharedParkingConnectorReservation()
	{
		if (!IsUsingSharedParkingConnector)
		{
			return true;
		}

		// Passengers walking from their cars queue for the door at the door
		// queue window (see TryAcquireDoorwayEntrySlot), not in the car park:
		// a door owner still far up its route left them idling by their cars.
		bool isInboundToGym = state == VisitorState.ReturningFromProteinStore;
		if (isInboundToGym &&
			activeDoorwayEntryAgent != null &&
			activeDoorwayEntryAgent != this &&
			activeDoorwayEntryAgent.isActiveAndEnabled &&
			activeDoorwayEntryAgent.IsDoorwayEntryAreaOccupied())
		{
			return false;
		}

		bool walkingFromCar = state == VisitorState.ApproachingGymFromVehicle;
		if ((UnityEngine.Object)(object)activeVehicleApproachAgent != (UnityEngine.Object)null &&
			(UnityEngine.Object)(object)activeVehicleApproachAgent != (UnityEngine.Object)(object)this &&
			((Behaviour)activeVehicleApproachAgent).isActiveAndEnabled &&
			activeVehicleApproachAgent.IsUsingSharedParkingConnector)
		{
			// Passengers walking from their cars to the gym all move the same
			// way and cannot deadlock each other; they share the connector
			// instead of idling one by one in front of their cars. A store
			// trip only crosses near the door, where the doorway queue already
			// orders people. Only a visitor walking back to a car (the
			// opposite direction along the whole connector) makes them wait.
			return walkingFromCar &&
				(activeVehicleApproachAgent.state != VisitorState.ApproachingVehicle ||
				 IsBlockingConnectorOwner(activeVehicleApproachAgent));
		}
		if (!walkingFromCar && IsOtherInboundOnSharedConnector(this))
		{
			return false;
		}

		if ((UnityEngine.Object)(object)activeVehicleApproachAgent == (UnityEngine.Object)null ||
			!((Behaviour)activeVehicleApproachAgent).isActiveAndEnabled ||
			!activeVehicleApproachAgent.IsUsingSharedParkingConnector)
		{
			activeVehicleApproachAgent = null;
		}

		activeVehicleApproachAgent = this;
		return true;
	}
	// Face to face on the connector: the owner walking back to its car has
	// stopped for this visitor. Waiting here as well would deadlock both, so
	// this visitor keeps walking and passes the owner.
	private bool IsBlockingConnectorOwner(GymVisitorAgent owner)
	{
		if (owner == null || owner.fighter == null || fighter == null)
		{
			return false;
		}
		string blocker = owner.fighter.LastVisitorRouteBlocker;
		return !string.IsNullOrEmpty(blocker) &&
			blocker.IndexOf("owner=" + fighter.Identity, StringComparison.Ordinal) >= 0;
	}
	// Only an inbound walker close enough to meet this visitor on the
	// connector makes it wait; one far away (or still queuing by its car)
	// would otherwise hold every departure indefinitely.
	private const float InboundMeetDistance = 8f;
	private static bool IsOtherInboundOnSharedConnector(GymVisitorAgent self)
	{
		for (int i = 0; i < activeAgents.Count; i++)
		{
			GymVisitorAgent other = activeAgents[i];
			if (other != null && other != self && other.isActiveAndEnabled &&
				other.state == VisitorState.ApproachingGymFromVehicle &&
				other.IsUsingSharedParkingConnector &&
				(self == null || self.fighter == null || other.fighter == null ||
				 Vector3.ProjectOnPlane(other.fighter.VisitorPhysicsPosition -
					self.fighter.VisitorPhysicsPosition, Vector3.up).magnitude < InboundMeetDistance) &&
				(self == null || !self.IsBlockingConnectorOwner(other)))
			{
				return true;
			}
		}
		return false;
	}
	public void ReleaseVehicleApproachReservation()
	{
		if ((UnityEngine.Object)(object)activeVehicleApproachAgent == (UnityEngine.Object)(object)this)
		{
			activeVehicleApproachAgent = null;
		}
		else if ((UnityEngine.Object)(object)activeVehicleApproachAgent == (UnityEngine.Object)null || !((Behaviour)activeVehicleApproachAgent).isActiveAndEnabled)
		{
			activeVehicleApproachAgent = null;
		}
		ReleaseDoorwayExitSlot();
	}
	private void RestoreVisitorVehicleCollisions()
	{
		if ((UnityEngine.Object)(object)fighter == (UnityEngine.Object)null)
		{
			return;
		}
		Collider[] visitorColliders = ((Component)fighter).GetComponentsInChildren<Collider>(true);
		GymVisitorVehicle[] vehicles = UnityEngine.Object.FindObjectsByType<GymVisitorVehicle>((FindObjectsInactive)1, (FindObjectsSortMode)0);
		for (int v = 0; v < vehicles.Length; v++)
		{
			if ((UnityEngine.Object)(object)vehicles[v] == (UnityEngine.Object)null)
			{
				continue;
			}
			Collider[] vehicleColliders = ((Component)vehicles[v]).GetComponentsInChildren<Collider>(true);
			for (int i = 0; i < visitorColliders.Length; i++)
			{
				if ((UnityEngine.Object)(object)visitorColliders[i] == (UnityEngine.Object)null)
				{
					continue;
				}
				for (int c = 0; c < vehicleColliders.Length; c++)
				{
					if ((UnityEngine.Object)(object)vehicleColliders[c] != (UnityEngine.Object)null)
					{
						Physics.IgnoreCollision(visitorColliders[i], vehicleColliders[c], false);
					}
				}
			}
		}
	}
	private void IgnoreRouteVehicleCollision(
		Vector3 referencePoint,
		bool allowBus = false,
		GymVisitorVehicle preferredVehicle = null)
	{
		//IL_001b: Unknown result type (might be due to invalid IL or missing references)
		//IL_001c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0021: Unknown result type (might be due to invalid IL or missing references)
		//IL_0026: Unknown result type (might be due to invalid IL or missing references)
		//IL_0044: Unknown result type (might be due to invalid IL or missing references)
		//IL_0049: Unknown result type (might be due to invalid IL or missing references)
		//IL_004e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0053: Unknown result type (might be due to invalid IL or missing references)
		//IL_0054: Unknown result type (might be due to invalid IL or missing references)
		//IL_0059: Unknown result type (might be due to invalid IL or missing references)
		RestoreRouteVehicleCollision();
		RestoreDoorwayWallCollisions();
		GymVisitorVehicle[] array = UnityEngine.Object.FindObjectsByType<GymVisitorVehicle>((FindObjectsInactive)1, (FindObjectsSortMode)0);
		GymVisitorVehicle nearest = preferredVehicle;
		float nearestDistance = float.PositiveInfinity;
		if ((UnityEngine.Object)(object)nearest == (UnityEngine.Object)null)
		{
			Vector3 planarReference = Vector3.ProjectOnPlane(referencePoint, Vector3.up);
			GymVisitorVehicle[] array2 = array;
			foreach (GymVisitorVehicle candidate in array2)
			{
				if (!((UnityEngine.Object)(object)candidate == (UnityEngine.Object)null))
				{
					Vector3 val = Vector3.ProjectOnPlane(((Component)candidate).transform.position, Vector3.up) - planarReference;
					float distance = val.sqrMagnitude;
					if (distance < nearestDistance)
					{
						nearestDistance = distance;
						nearest = candidate;
					}
				}
			}
		}
		if (!((UnityEngine.Object)(object)nearest == (UnityEngine.Object)null) && (!nearest.IsBus || allowBus || !GymRoadsideBusStop.IsBuilt))
		{
			routeVehicleWithIgnoredCollision = nearest;
			SetRouteVehicleCollisionIgnored(ignored: true);
		}
	}
	private void RestoreRouteVehicleCollision()
	{
		if (!((UnityEngine.Object)(object)routeVehicleWithIgnoredCollision == (UnityEngine.Object)null))
		{
			SetRouteVehicleCollisionIgnored(ignored: false);
			routeVehicleWithIgnoredCollision = null;
		}
	}
	private void SetRouteVehicleCollisionIgnored(bool ignored)
	{
		if ((UnityEngine.Object)(object)fighter == (UnityEngine.Object)null || (UnityEngine.Object)(object)routeVehicleWithIgnoredCollision == (UnityEngine.Object)null)
		{
			return;
		}
		Collider[] componentsInChildren = ((Component)fighter).GetComponentsInChildren<Collider>(true);
		Collider[] vehicleColliders = ((Component)routeVehicleWithIgnoredCollision).GetComponentsInChildren<Collider>(true);
		Collider[] array = componentsInChildren;
		foreach (Collider visitorCollider in array)
		{
			if ((UnityEngine.Object)(object)visitorCollider == (UnityEngine.Object)null)
			{
				continue;
			}
			Collider[] array2 = vehicleColliders;
			foreach (Collider vehicleCollider in array2)
			{
				if ((UnityEngine.Object)(object)vehicleCollider != (UnityEngine.Object)null)
				{
					Physics.IgnoreCollision(visitorCollider, vehicleCollider, ignored);
				}
			}
		}
	}
	private void CompleteVehicleApproach()
	{
		bool returnedAfterEntryAbort = returningToVehicleAfterEntryAbort;
		returningToVehicleAfterEntryAbort = false;
		RestoreVisitorPlayerBoundaryCollisions();
		RestoreRouteVehicleCollision();
		RestoreDoorwayWallCollisions();
		ReleaseVehicleApproachReservation();
		state = VisitorState.Dormant;
		fighter.StopVisitorMovement();
		if (returnedAfterEntryAbort)
		{
			reachedVehicle = false;
			enteredGym = false;
			leftGym = true;
			hasSuccessfulEntry = false;
			completedDoorExit = true;
			pendingStation = null;
			ReleaseDoorOpenRequest();
			Debug.Log(
				$"GYMCHAOS_VISITOR_ENTRY_ABORT_RETURNED enemy={fighter.Identity} point={fighter.VisitorPhysicsPosition}",
				this);
			return;
		}

		reachedVehicle = true;
		completedVehicleApproaches++;
		Debug.Log((object)$"GYMCHAOS_VISITOR_REACHED_VEHICLE enemy={fighter.Identity}", (UnityEngine.Object)(object)this);
	}
	private void ResetVehicleRouteProgress()
	{
		//IL_0022: Unknown result type (might be due to invalid IL or missing references)
		//IL_0032: Unknown result type (might be due to invalid IL or missing references)
		//IL_0037: Unknown result type (might be due to invalid IL or missing references)
		//IL_003c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0041: Unknown result type (might be due to invalid IL or missing references)
		//IL_0046: Unknown result type (might be due to invalid IL or missing references)
		vehicleRouteStalledSeconds = 0f;
		float num;
		if (!((UnityEngine.Object)(object)fighter != (UnityEngine.Object)null))
		{
			num = float.PositiveInfinity;
		}
		else
		{
			Vector3 val = Vector3.ProjectOnPlane(travelTarget - ((Component)fighter).transform.position, Vector3.up);
			num = val.magnitude;
		}
		lastVehicleRouteDistance = num;
	}
	private void ClearVehicleRouteDetour()
	{
		vehicleDetourActive = false;
		vehicleDetourWaypoints = null;
		vehicleDetourWaypointIndex = 0;
	}
	private void TryRerouteStalledVehicleApproach()
	{
		//IL_0010: Unknown result type (might be due to invalid IL or missing references)
		//IL_001b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0020: Unknown result type (might be due to invalid IL or missing references)
		//IL_0025: Unknown result type (might be due to invalid IL or missing references)
		//IL_002a: Unknown result type (might be due to invalid IL or missing references)
		//IL_002f: Unknown result type (might be due to invalid IL or missing references)
		//IL_008b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0083: Unknown result type (might be due to invalid IL or missing references)
		//IL_0090: Unknown result type (might be due to invalid IL or missing references)
		//IL_0092: Unknown result type (might be due to invalid IL or missing references)
		//IL_00fb: Unknown result type (might be due to invalid IL or missing references)
		//IL_00fc: Unknown result type (might be due to invalid IL or missing references)
		//IL_0125: Unknown result type (might be due to invalid IL or missing references)
		//IL_0126: Unknown result type (might be due to invalid IL or missing references)
		//IL_015b: Unknown result type (might be due to invalid IL or missing references)
		//IL_016b: Unknown result type (might be due to invalid IL or missing references)
		//IL_00d8: Unknown result type (might be due to invalid IL or missing references)
		if ((UnityEngine.Object)(object)fighter == (UnityEngine.Object)null)
		{
			return;
		}
		Vector3 val = Vector3.ProjectOnPlane(travelTarget - fighter.VisitorPhysicsPosition, Vector3.up);
		float distance = val.magnitude;
		if (distance < lastVehicleRouteDistance - 0.012f)
		{
			lastVehicleRouteDistance = distance;
			vehicleRouteStalledSeconds = 0f;
			return;
		}
		vehicleRouteStalledSeconds += Time.fixedDeltaTime;
		if (!(vehicleRouteStalledSeconds < 2.4f))
		{
			Vector3 resumeTarget = (vehicleDetourActive ? vehicleDetourResumeTarget : travelTarget);
			if (!TryBuildCollisionCheckedVehicleDetour(resumeTarget, out Vector3[] detourWaypoints))
			{
				vehicleRouteStalledSeconds = 0f;
				lastVehicleRouteDistance = distance;
				fighter.StopVisitorMovement();
				GymVisitorAgent reservationOwner = activeVehicleApproachAgent;
				string ownerSnapshot = reservationOwner != null && reservationOwner.fighter != null
					? $"{reservationOwner.fighter.Identity}:{reservationOwner.state}@{reservationOwner.fighter.VisitorPhysicsPosition} target={reservationOwner.travelTarget} exit={reservationOwner.vehicleExitWaypointIndex} store={reservationOwner.storeVisitWaypointIndex}"
					: "none";
				Debug.LogWarning(
					$"GYMCHAOS_VISITOR_VEHICLE_ROUTE_QUEUE_WAITING enemy={fighter.Identity} " +
					$"state={state} position={fighter.VisitorPhysicsPosition} target={travelTarget} " +
					$"entry={vehicleEntryWaypointIndex} exit={vehicleExitWaypointIndex} " +
					$"owner={ownerSnapshot} resume={resumeTarget} blocker={fighter.LastVisitorRouteBlocker}",
					this);
			}
			else
			{
				vehicleDetourResumeTarget = resumeTarget;
				vehicleDetourWaypoints = detourWaypoints;
				vehicleDetourWaypointIndex = 0;
				vehicleDetourActive = true;
				vehicleRerouteAttempt++;
				vehicleRouteDetourCount++;
				travelTarget = detourWaypoints[0];
				ResetVehicleRouteProgress();
				Debug.LogWarning((object)($"GYMCHAOS_VISITOR_VEHICLE_ROUTE_REROUTE enemy={fighter.Identity} " + $"attempt={vehicleRerouteAttempt} detour={detourWaypoints[0]} waypoints={detourWaypoints.Length} " + $"resume={resumeTarget} blocker={fighter.LastVisitorRouteBlocker}"), (UnityEngine.Object)(object)this);
			}
		}
	}
	private bool IsStaticCollisionCheckedVehicleRouteClear(Vector3 start, Vector3[] waypoints)
	{
		if (waypoints == null || waypoints.Length == 0 ||
			(UnityEngine.Object)(object)fighter == (UnityEngine.Object)null)
		{
			return false;
		}

		Vector3 from = start;
		foreach (Vector3 authoredTarget in waypoints)
		{
			Vector3 target = new Vector3(authoredTarget.x, from.y, authoredTarget.z);
			Vector3 segment = Vector3.ProjectOnPlane(target - from, Vector3.up);
			float distance = segment.magnitude;
			if (distance > 0.2f &&
				!fighter.IsVisitorExternalPathClearFrom(
					from, segment / distance, distance, ignoreDynamicBlockers: true))
			{
				return false;
			}
			from = target;
		}

		return true;
	}
	private Vector3? GetVehicleRouteDetourLookAhead()
	{
		if (!vehicleDetourActive || vehicleDetourWaypoints == null ||
			vehicleDetourWaypointIndex + 1 >= vehicleDetourWaypoints.Length)
		{
			return vehicleDetourResumeTarget;
		}
		return vehicleDetourWaypoints[vehicleDetourWaypointIndex + 1];
	}
	private void AdvanceVehicleRouteDetour()
	{
		if (!vehicleDetourActive || vehicleDetourWaypoints == null)
		{
			vehicleDetourActive = false;
			travelTarget = vehicleDetourResumeTarget;
			vehicleDetourWaypoints = null;
			vehicleDetourWaypointIndex = 0;
			ResetVehicleRouteProgress();
			return;
		}

		vehicleDetourWaypointIndex++;
		if (vehicleDetourWaypointIndex < vehicleDetourWaypoints.Length)
		{
			travelTarget = vehicleDetourWaypoints[vehicleDetourWaypointIndex];
			ResetVehicleRouteProgress();
			return;
		}

		vehicleDetourActive = false;
		vehicleDetourWaypoints = null;
		vehicleDetourWaypointIndex = 0;
		travelTarget = vehicleDetourResumeTarget;
		ResetVehicleRouteProgress();
	}
	private EnemyFighter ResolveVehicleRouteBlocker()
	{
		if (fighter == null)
		{
			return null;
		}

		string blocker = fighter.LastVisitorRouteBlocker ?? string.Empty;
		int ownerStart = blocker.LastIndexOf("owner=", StringComparison.OrdinalIgnoreCase);
		if (ownerStart < 0)
		{
			return null;
		}
		string ownerName = blocker.Substring(ownerStart + "owner=".Length).Trim();
		int separator = ownerName.IndexOfAny(new[] { ' ', ',', ';' });
		if (separator >= 0)
		{
			ownerName = ownerName.Substring(0, separator);
		}
		if (!Enum.TryParse(ownerName, true, out BodybuilderIdentity ownerIdentity))
		{
			return null;
		}

		IReadOnlyList<EnemyFighter> fighters = EnemyFighter.RegisteredFighters;
		for (int i = 0; i < fighters.Count; i++)
		{
			EnemyFighter candidate = fighters[i];
			if (candidate != null && candidate != fighter &&
				candidate.isActiveAndEnabled && candidate.Identity == ownerIdentity)
			{
				return candidate;
			}
		}
		return null;
	}
	private bool IsVehicleDetourRouteClear(Vector3 start, Vector3[] waypoints, Vector3 end)
	{
		Vector3 from = start;
		for (int i = 0; i <= waypoints.Length; i++)
		{
			Vector3 to = i < waypoints.Length ? waypoints[i] : end;
			if (!IsExternalRouteSegmentClear(from, to))
			{
				return false;
			}
			from = to;
		}
		return true;
	}
	private bool TryBuildCollisionCheckedVehicleDetour(
		Vector3 resumeTarget, out Vector3[] detourWaypoints)
	{
		detourWaypoints = null;
		if (fighter == null)
		{
			return false;
		}

		Vector3 current = fighter.VisitorPhysicsPosition;
		Vector3 towardResume = Vector3.ProjectOnPlane(resumeTarget - current, Vector3.up);
		float routeDistance = towardResume.magnitude;
		if (routeDistance < 0.01f)
		{
			return false;
		}
		towardResume.Normalize();
		Vector3 lateral = Vector3.Cross(Vector3.up, towardResume).normalized;
		Bounds accessible = GymOutdoorBuilder.AccessibleBounds;
		float actorRadius = EnemyFighter.GetBodyRadiusForIdentity(fighter.Identity);
		float boundsMargin = actorRadius + 0.45f;

		EnemyFighter blocker = ResolveVehicleRouteBlocker();
		if (blocker != null)
		{
			Vector3 blockerPosition = blocker.VisitorPhysicsPosition;
			blockerPosition.y = current.y;
			Vector3 toBlocker = Vector3.ProjectOnPlane(blockerPosition - current, Vector3.up);
			float blockerAlongRoute = Vector3.Dot(toBlocker, towardResume);
			float blockerRadius = EnemyFighter.GetBodyRadiusForIdentity(blocker.Identity);
			float lateralClearance = actorRadius + blockerRadius + 0.65f;
			float longitudinalClearance = actorRadius + blockerRadius + 0.75f;
			if (blockerAlongRoute >= -0.15f &&
				blockerAlongRoute + longitudinalClearance < routeDistance)
			{
				for (int sideIndex = 0; sideIndex < 2; sideIndex++)
				{
					float sideSign = sideIndex == 0 ? 1f : -1f;
					for (int widen = 0; widen < 3; widen++)
					{
						float sideOffset = lateralClearance + widen * 0.55f;
						Vector3 passSide = blockerPosition + lateral * sideSign * sideOffset;
						Vector3 passAhead = passSide + towardResume * longitudinalClearance;
						ClampVehicleDetourToAccessibleBounds(ref passSide, accessible, boundsMargin);
						ClampVehicleDetourToAccessibleBounds(ref passAhead, accessible, boundsMargin);
						float actualSideClearance = Mathf.Abs(Vector3.Dot(
							Vector3.ProjectOnPlane(passSide - blockerPosition, Vector3.up), lateral));
						if (actualSideClearance < lateralClearance - 0.1f)
						{
							continue;
						}

						Vector3[] candidate = { passSide, passAhead };
						if (Vector3.Distance(current, passSide) <= 0.35f ||
							Vector3.Distance(passSide, passAhead) <= 0.35f ||
							!IsVehicleDetourRouteClear(current, candidate, resumeTarget))
						{
							continue;
						}

						detourWaypoints = candidate;
						return true;
					}
				}
			}
		}

		float[] lateralOffsets =
		{
			-1.0f, 1.0f, -1.55f, 1.55f, -2.1f, 2.1f,
			-2.6f, 2.6f, -3.1f, 3.1f, -3.6f, 3.6f
		};
		float[] forwardOffsets =
		{
			0.35f, 0.35f, 0.45f, 0.45f, 0.55f, 0.55f,
			0.65f, 0.65f, 0.75f, 0.75f, 0.85f, 0.85f
		};
		for (int i = 0; i < lateralOffsets.Length; i++)
		{
			Vector3 candidate = current + lateral * lateralOffsets[i] +
				towardResume * forwardOffsets[i];
			ClampVehicleDetourToAccessibleBounds(ref candidate, accessible, boundsMargin);
			Vector3 step = Vector3.ProjectOnPlane(candidate - current, Vector3.up);
			if (step.magnitude < 0.35f || step.magnitude > 4.4f)
			{
				continue;
			}

			Vector3[] singleStep = { candidate };
			if (IsVehicleDetourRouteClear(current, singleStep, resumeTarget))
			{
				detourWaypoints = singleStep;
				return true;
			}
		}
		return false;
	}
	private static void ClampVehicleDetourToAccessibleBounds(
		ref Vector3 point, Bounds accessible, float margin)
	{
		if (accessible.size.x <= 3f || accessible.size.z <= 3f)
		{
			return;
		}
		point.x = Mathf.Clamp(point.x, accessible.min.x + margin, accessible.max.x - margin);
		point.z = Mathf.Clamp(point.z, accessible.min.z + margin, accessible.max.z - margin);
	}
}
