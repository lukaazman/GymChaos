using System.Collections.Generic;
using UnityEngine;

// A visitor or the police officer in a car seat: the seat drives this
// fighter's own retarget animator and its skin-sized hitbox capsules.
public partial class EnemyFighter : IVehicleSeatOccupant
{
    private EnemyMeshHitboxRig seatHitboxes;

    Transform IVehicleSeatOccupant.Root => transform;
    string IVehicleSeatOccupant.OccupantName => identity.ToString();
    bool IVehicleSeatOccupant.IsDead => isDead;
    bool IVehicleSeatOccupant.IsInVehicleSeatMode => visitorVehicleSeated;
    bool IVehicleSeatOccupant.HasVehicleClips => VehicleBodyAnimator != null && VehicleBodyAnimator.HasVehicleClips;
    float IVehicleSeatOccupant.CarExitClipLength => VehicleBodyAnimator != null ? VehicleBodyAnimator.CarExitClipLength : 0f;
    BodybuilderEnemyVisual.Rig IVehicleSeatOccupant.BodyRig => VehicleBodyAnimator?.BodyRig;
    bool IVehicleSeatOccupant.GroundOffsetsReady =>
        VehicleBodyAnimator != null && VehicleBodyAnimator.GroundOffsetsReadyForVerification;
    MixamoScanRetargetAnimator.VehiclePose IVehicleSeatOccupant.CurrentVehiclePose =>
        VehicleBodyAnimator != null ? VehicleBodyAnimator.CurrentVehiclePose : MixamoScanRetargetAnimator.VehiclePose.None;

    void IVehicleSeatOccupant.BeginVehicleSeat(Transform anchor) => BeginVisitorVehicleSeat(anchor);
    void IVehicleSeatOccupant.EndVehicleSeat(Vector3 groundPosition, Quaternion rotation) =>
        EndVisitorVehicleSeat(groundPosition, rotation);

    bool IVehicleSeatOccupant.SetVehiclePose(MixamoScanRetargetAnimator.VehiclePose pose, float exitPhase,
        Vector3 rootWorldOffset, float scale) =>
        VehicleBodyAnimator != null && VehicleBodyAnimator.SetVehiclePose(pose, exitPhase, rootWorldOffset, scale);

    bool IVehicleSeatOccupant.SampleVehiclePoseNow(MixamoScanRetargetAnimator.VehiclePose pose, float normalized,
        float scale) =>
        VehicleBodyAnimator != null && VehicleBodyAnimator.SampleVehiclePoseNow(pose, normalized, scale);

    bool IVehicleSeatOccupant.SampleWalkingPoseNow() =>
        VehicleBodyAnimator != null && VehicleBodyAnimator.SampleWalkingPoseNow();

    void IVehicleSeatOccupant.SetMoving(bool moving, float normalizedSpeed) =>
        VehicleBodyAnimator?.SetMoving(moving, normalizedSpeed, false);

    float IVehicleSeatOccupant.BakedCrown(float radius)
    {
        Transform head = VehicleBodyAnimator?.BodyRig?.Head;
        return head != null ? GymVehicleDriverSeat.BakedSkinCrown(transform, this, head, radius) : 0f;
    }

    void IVehicleSeatOccupant.GetBodyVolumes(List<(Vector3 a, Vector3 b, float radius)> into)
    {
        if (seatHitboxes == null) seatHitboxes = GetComponent<EnemyMeshHitboxRig>();
        if (seatHitboxes != null) seatHitboxes.GetBodyVolumes(into);
        else into.Clear();
    }
}
