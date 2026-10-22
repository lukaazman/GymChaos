using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Whoever sits in a car seat (<see cref="GymVehicleDriverSeat"/>): the actor
/// root the seat parks on its anchor, the body rig it measures, the two car
/// clips (vehicle_driving, vehicle_exit) and the walk it hands over to.
/// EnemyFighter (visitors, the police officer) and PlayerVehicleOccupant (the
/// player in the race buggy) implement it.
/// </summary>
public interface IVehicleSeatOccupant
{
    /// <summary>Actor root: on the ground under the body, facing the body's +Z.</summary>
    Transform Root { get; }
    /// <summary>Name for logs; the seat keeps one fit per name.</summary>
    string OccupantName { get; }
    bool IsDead { get; }
    bool IsInVehicleSeatMode { get; }
    bool HasVehicleClips { get; }
    float CarExitClipLength { get; }
    BodybuilderEnemyVisual.Rig BodyRig { get; }
    /// <summary>True once the walking pose's ground offsets are final (see the seat's grounded fit).</summary>
    bool GroundOffsetsReady { get; }
    MixamoScanRetargetAnimator.VehiclePose CurrentVehiclePose { get; }

    /// <summary>The root follows <paramref name="anchor"/>, physics off, until <see cref="EndVehicleSeat"/>.</summary>
    void BeginVehicleSeat(Transform anchor);
    void EndVehicleSeat(Vector3 groundPosition, Quaternion rotation);
    /// <summary>Holds a car pose (see MixamoScanRetargetAnimator.SetVehiclePose).</summary>
    bool SetVehiclePose(MixamoScanRetargetAnimator.VehiclePose pose, float exitPhase,
        Vector3 rootWorldOffset, float scale = 1f);
    /// <summary>Writes a car pose right now, without seat offset, for measuring.</summary>
    bool SampleVehiclePoseNow(MixamoScanRetargetAnimator.VehiclePose pose, float normalized, float scale = 1f);
    /// <summary>Writes the first walking frame (grounded root) right now.</summary>
    bool SampleWalkingPoseNow();
    void SetMoving(bool moving, float normalizedSpeed);
    /// <summary>Highest baked skin point (Root-local y) within <paramref name="radius"/> of the head bone.</summary>
    float BakedCrown(float radius);
    /// <summary>The body as skin-sized capsules (world a, b, radius); cleared, empty when unknown.</summary>
    void GetBodyVolumes(List<(Vector3 a, Vector3 b, float radius)> into);
}
