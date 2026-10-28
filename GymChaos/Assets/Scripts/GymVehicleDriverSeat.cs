using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The driver of a visitor car: sits in the cabin while the car drives
/// (car_driving), climbs out of the driver door after parking (car_exit) and
/// walks along the car to its aisle end, and on departure walks back, gets
/// in (car_exit backwards) and drives off. The actor root stays on a seat
/// anchor; the clip's own root motion moves the body, and a vertical seat
/// offset keeps each rig's hips on the cushion and its head under the roof.
/// Every hand-over (drive/exit/walk) keeps the hips where they were, so no
/// transition snaps.
/// </summary>
[DefaultExecutionOrder(100)]
public sealed class GymVehicleDriverSeat : MonoBehaviour
{
    // car_exit.fbx: 126 frames at 30 fps.
    private const float ExitFrames = 126f;
    private const float DoorOpenStart = 10f / ExitFrames;
    private const float DoorOpenEnd = 34f / ExitFrames;
    private const float DoorCloseStart = 84f / ExitFrames;
    private const float DoorCloseEnd = 100f / ExitFrames;
    // The clip climbs out of a high seat; the seat offset fades while the
    // driver slides out and the first foot reaches the ground.
    private const float RiseStart = 14f / ExitFrames;
    private const float RiseEnd = 44f / ExitFrames;
    // Feet are matched to the walking clip's planted feet over the end.
    private const float GroundMatchStart = 0.62f;
    private const float WalkSpeed = 1.9f;
    private const float WalkTurnDegreesPerSecond = 300f;
    private const float RoofClearance = 0.05f;

    public enum SeatState
    {
        Empty,
        WaitingForCabin,
        Seated,
        Exiting,
        WalkingAway,
        WalkingIn,
        Boarding
    }

    private struct Fit
    {
        public bool Valid;
        // Measured after the rig's per-clip ground offsets were known.
        public bool Grounded;
        public Vector3 AnchorLocal;
        public Vector3 DrivingOffsetLocal;
        public float ExitStartOffsetY;
        public float EndFootLift;
        public float CushionLocalY;
        public float Sink;
        public Vector3 DoorPointLocal;
        public Quaternion DoorRotationLocal;
        public Vector3 LanePointLocal;
        public Vector3 WheelLeftLocal;
        public Vector3 WheelRightLocal;
        public float HeadClearance;
        // Seat-offset fade over the climb (0 seated .. 1 standing), sampled
        // per rig so the crown stays under the roof edge and the feet clear
        // the sill and the road.
        public float[] Rise;
        public int RiseConflicts;
        // Door timing over the climb: fully open while any body part is in
        // the door's swing, shut only once the body is clear of it.
        public float DoorOpenStart, DoorOpenEnd, DoorCloseStart, DoorCloseEnd;
        // Rearward shift (vehicle-local, along the door's length) keeping the
        // climbing body off the inside of the open door leaf.
        public float[] Shift;
        public Vector3 ShiftAxis;
        // Uniform scale while seated, back to 1 over the climb once the
        // hips are outside the door (see ScaleAt).
        public float InCarScale;
        public float ScaleOutStart;
        public float HipsOutPhase;
        public float CrownAboveCushion;
        public float CushionRoom;
        // Foot bone height above the sole (standing, unscaled).
        public float AnkleHeight;
    }

    private GymVisitorVehicle vehicle;
    private GymVehicleCabin cabin;
    private Transform anchor;
    private float groundLocalY;
    private Vector3 aisleEndLocal;
    // The seated body: a visitor, the police officer or the player.
    private IVehicleSeatOccupant driver;
    private Fit fit;
    private string fitOwner;
    private float exitPhase;
    private float clipLength = 4.2f;
    private Action onFinished;
    private Vector3 walkTarget;
    private Quaternion walkFinalRotation;

    // The police officer climbs out faster than a visitor.
    public float PlaybackSpeed { get; set; } = 1f;
    public SeatState State { get; private set; }
    public EnemyFighter Driver => driver as EnemyFighter;
    // A destroyed driver component compares equal to null only through Unity's operator.
    private bool HasDriver => driver is UnityEngine.Object body ? body != null : driver != null;
    public GymVehicleCabin Cabin => cabin;
    public float ExitPhase => exitPhase;
    public float SeatSink => fit.Sink;
    public float HeadClearance => fit.HeadClearance;
    public Vector3 LanePointWorld => fit.Valid ? transform.TransformPoint(fit.LanePointLocal) : transform.position;
    public Vector3 DoorPointWorld => fit.Valid ? transform.TransformPoint(fit.DoorPointLocal) : transform.position;
    private const float SoleBelowFootBone = 0.05f;

    public void Initialize(GymVisitorVehicle owner, GymVehicleCabin cabinParts,
        float groundLocal, Vector3 aisleSideLocal)
    {
        vehicle = owner;
        cabin = cabinParts;
        groundLocalY = groundLocal;
        aisleEndLocal = aisleSideLocal;
        if (anchor == null)
        {
            anchor = new GameObject("Driver Seat Anchor").transform;
            anchor.SetParent(transform, false);
        }
    }

    public static bool SupportsDriver(EnemyFighter fighter) => SupportsDriver((IVehicleSeatOccupant)fighter);

    public static bool SupportsDriver(IVehicleSeatOccupant occupant)
    {
        BodybuilderEnemyVisual.Rig rig = occupant?.BodyRig;
        return rig != null && occupant.HasVehicleClips &&
            rig.Head != null && rig.LeftHand != null && rig.RightHand != null;
    }

    /// <summary>Seats <paramref name="fighter"/> in the driving pose at once.</summary>
    public bool Mount(EnemyFighter fighter) => Mount((IVehicleSeatOccupant)fighter);

    public bool Mount(IVehicleSeatOccupant occupant)
    {
        if (!Prepare(occupant)) return false;
        exitPhase = 0f;
        driver.BeginVehicleSeat(anchor);
        ApplySeat(true);
        State = SeatState.Seated;
        Debug.Log(
            $"GYMCHAOS_VEHICLE_DRIVER_SEATED identity={occupant.OccupantName} " +
            $"sink={fit.Sink:F3} headClearance={fit.HeadClearance:F3} " +
            $"cushion={fit.CushionLocalY:F3} door={fit.DoorPointLocal} lane={fit.LanePointLocal}",
            this);
        return true;
    }

    /// <summary>Climbs out and walks to the aisle end of the car; then <paramref name="standing"/>.</summary>
    public bool BeginExit(Action standing)
    {
        if (State != SeatState.Seated || !HasDriver) return false;
        onFinished = standing;
        RefreshGroundedFit();
        exitPhase = 0f;
        State = SeatState.Exiting;
        Debug.Log($"GYMCHAOS_VEHICLE_DRIVER_EXIT_STARTED identity={driver.OccupantName}", this);
        return true;
    }

    /// <summary>Walks from the aisle end to the door, gets in; then <paramref name="seated"/>.</summary>
    public bool BeginBoarding(EnemyFighter fighter, Action seated) =>
        BeginBoarding((IVehicleSeatOccupant)fighter, seated);

    public bool BeginBoarding(IVehicleSeatOccupant occupant, Action seated)
    {
        if (!Prepare(occupant)) return false;
        onFinished = seated;
        // Ride a free anchor from where the visitor stands to the door.
        Transform root = occupant.Root;
        anchor.SetPositionAndRotation(root.position, root.rotation);
        driver.BeginVehicleSeat(anchor);
        driver.SetVehiclePose(MixamoScanRetargetAnimator.VehiclePose.None, 0f, Vector3.zero);
        walkTarget = DoorPointWorld;
        walkFinalRotation = transform.rotation * fit.DoorRotationLocal;
        State = SeatState.WalkingIn;
        Debug.Log($"GYMCHAOS_VEHICLE_DRIVER_BOARDING_STARTED identity={occupant.OccupantName}", this);
        return true;
    }

    /// <summary>The car has gone: forget the driver (it is hidden by its director).</summary>
    public void Release()
    {
        if (HasDriver)
        {
            driver.SetVehiclePose(MixamoScanRetargetAnimator.VehiclePose.None, 0f, Vector3.zero);
            if (driver.IsInVehicleSeatMode)
            {
                driver.EndVehicleSeat(driver.Root.position, driver.Root.rotation);
            }
        }
        driver = null;
        onFinished = null;
        State = SeatState.Empty;
        lastDoorOpen = 0f;
        cabin?.SetDoorOpen01(0f);
    }

    /// <summary>
    /// The driver was attacked: it jumps out at once and stands at the door to
    /// fight. The pending seated/standing callback is dropped.
    /// </summary>
    public void EjectDriverForCombat()
    {
        IVehicleSeatOccupant standing = driver;
        if (!HasDriver)
        {
            return;
        }
        standing.SetVehiclePose(MixamoScanRetargetAnimator.VehiclePose.None, 0f, Vector3.zero);
        // Beside the door, from the seat anchor: while the car drives, its
        // body moves away from this (parking) root, so DoorPointWorld lags.
        Transform seat = anchor != null ? anchor : standing.Root;
        float side = fit.Valid ? Mathf.Sign((fit.DoorPointLocal - fit.AnchorLocal).x) : 1f;
        if (side == 0f) side = 1f;
        Vector3 point = seat.position + seat.right * side * 1.4f;
        Quaternion rotation = Quaternion.LookRotation(seat.right * side, Vector3.up);
        driver = null;
        onFinished = null;
        State = SeatState.Empty;
        lastDoorOpen = 0f;
        cabin?.SetDoorOpen01(0f);
        if (standing.IsInVehicleSeatMode)
        {
            standing.EndVehicleSeat(point, Quaternion.Euler(0f, rotation.eulerAngles.y, 0f));
        }
        Debug.Log($"GYMCHAOS_VEHICLE_DRIVER_EJECTED identity={standing.OccupantName} point={point}", this);
    }

    /// <summary>Measures this rig's seat fit (door and lane points) without seating it.</summary>
    public bool EnsureFit(EnemyFighter fighter) => EnsureFit((IVehicleSeatOccupant)fighter);

    public bool EnsureFit(IVehicleSeatOccupant occupant)
    {
        if (occupant == null || cabin == null || !cabin.IsReady || !SupportsDriver(occupant)) return false;
        if (fit.Valid && fitOwner == occupant.OccupantName) return true;
        if (!occupant.Root.gameObject.activeInHierarchy) return false;
        fit = Measure(occupant);
        fitOwner = occupant.OccupantName;
        return fit.Valid;
    }

    // The standing (walking) pose plants its feet with a per-clip ground
    // offset that is only measured once the rig has been visible; a fit
    // taken before that would make the hand-over to walking jump.
    private void RefreshGroundedFit()
    {
        if (fit.Valid && !fit.Grounded && HasDriver && driver.GroundOffsetsReady)
        {
            fit = Measure(driver);
        }
    }

    private bool Prepare(IVehicleSeatOccupant occupant)
    {
        if (occupant == null || cabin == null || !cabin.IsReady || !SupportsDriver(occupant))
        {
            return false;
        }
        driver = occupant;
        clipLength = Mathf.Max(1f, occupant.CarExitClipLength);
        if (!fit.Valid || fitOwner != occupant.OccupantName)
        {
            fit = Measure(occupant);
            fitOwner = occupant.OccupantName;
        }
        else
        {
            RefreshGroundedFit();
        }
        if (!fit.Valid)
        {
            driver = null;
            return false;
        }
        cabin.SetSeatCushionHeight(fit.CushionLocalY);
        cabin.FitSteeringWheel(fit.WheelLeftLocal, fit.WheelRightLocal, 0f);
        return true;
    }

    // Samples this rig's own car poses (in actor-local space, which shares
    // its axes with the car while seated) and derives where its root must
    // sit, how far the seat drops to clear the roof, and where it stands
    // once out.
    private const float ScaleOutLength = 0.22f;
    private const float MaxSinkUnscaled = 0.06f;
    private const float MinimumInCarScale = 0.8f;

    // Unscaled first; a rig that only fits by sinking far below the seat
    // or that cannot clear the roof edge and the sill together folds
    // (uniformly scaled) while seated and grows back while it stands up
    // outside the door.
    private Fit Measure(IVehicleSeatOccupant fighter)
    {
        Fit fitted = MeasureAt(fighter, 1f, 1f);
        if (!fitted.Valid) return fitted;
        float scale = 1f;
        if (fitted.Sink > MaxSinkUnscaled && fitted.CrownAboveCushion > 0.01f)
        {
            scale = Mathf.Clamp((fitted.CushionRoom + MaxSinkUnscaled) / fitted.CrownAboveCushion,
                MinimumInCarScale, 1f);
        }
        if (scale >= 0.999f && fitted.RiseConflicts <= 3) return fitted;
        float outStart = Mathf.Min(0.75f, fitted.HipsOutPhase);
        // Keep the largest scale with the fewest roof/sill conflicts.
        Fit best = fitted;
        for (int attempt = 0; attempt < 5; attempt++)
        {
            if (scale >= 0.999f) scale = 0.95f;
            Fit scaled = MeasureAt(fighter, scale, outStart);
            if (!scaled.Valid) break;
            if (scaled.RiseConflicts < best.RiseConflicts ||
                (best.Sink > MaxSinkUnscaled && scaled.Sink <= MaxSinkUnscaled &&
                 scaled.RiseConflicts <= best.RiseConflicts + 2))
            {
                best = scaled;
            }
            if (scaled.RiseConflicts <= 3 || scale <= MinimumInCarScale + 0.001f) break;
            scale = Mathf.Max(MinimumInCarScale, scale - 0.05f);
        }
        if (!ReferenceEquals(best.Rise, fitted.Rise) || best.InCarScale != fitted.InCarScale)
        {
            // Re-run the chosen scale so the plan scratch (shift, door) matches it.
            best = MeasureAt(fighter, best.InCarScale, best.ScaleOutStart);
        }
        return best;
    }

    private static float ScaleFor(float inCar, float outStart, float t) =>
        Mathf.Lerp(inCar, 1f, Smooth(outStart, outStart + ScaleOutLength, t));

    private float ScaleAt(float t) => fit.InCarScale <= 0f ? 1f : ScaleFor(fit.InCarScale, fit.ScaleOutStart, t);

    private Fit MeasureAt(IVehicleSeatOccupant fighter, float inCarScale, float scaleOutStart)
    {
        Fit result = default;
        result.InCarScale = inCarScale;
        result.ScaleOutStart = scaleOutStart;
        BodybuilderEnemyVisual.Rig rig = fighter.BodyRig;
        Transform hips = rig.Hips != null ? rig.Hips : rig.Root;
        Transform actor = fighter.Root;
        Vector3 Local(Transform bone) => actor.InverseTransformPoint(bone.position);
        float Foot() => Mathf.Min(Local(rig.LeftFoot != null ? rig.LeftFoot : hips).y,
            Local(rig.RightFoot != null ? rig.RightFoot : hips).y);

        if (!fighter.SampleWalkingPoseNow()) return result;
        Vector3 hipsWalk = Local(hips);
        float footWalk = Foot();
        result.AnkleHeight = Mathf.Max(0.04f, footWalk);
        float headWalk = Local(rig.Head).y - footWalk;
        // Head bone sits about 87% up a standing body; its crown 15% above it.
        float height = Mathf.Max(1f, headWalk / 0.87f);
        float crownAboveHead = height - headWalk;
        float hipsAboveCushion = 0.055f * height * inCarScale;

        fighter.SampleVehiclePoseNow(MixamoScanRetargetAnimator.VehiclePose.Exit, 0f,
            ScaleFor(inCarScale, scaleOutStart, 0f));
        Vector3 hipsExit0 = Local(hips);
        Quaternion hipsExit0Rotation = Quaternion.Inverse(actor.rotation) * hips.rotation;
        fighter.SampleVehiclePoseNow(MixamoScanRetargetAnimator.VehiclePose.Exit, 1f);
        Vector3 hipsExit1 = Local(hips);
        Quaternion hipsExit1Rotation = Quaternion.Inverse(actor.rotation) * hips.rotation;
        float footExit1 = Foot();

        Vector3 hipsDrive = Vector3.zero, leftDrive = Vector3.zero, rightDrive = Vector3.zero;
        float headDrive = 0f;
        float footDrive = 0f;
        float headBoneDrive = 0f;
        const int samples = 6;
        for (int i = 0; i < samples; i++)
        {
            fighter.SampleVehiclePoseNow(MixamoScanRetargetAnimator.VehiclePose.Driving, i / (float)samples,
                inCarScale);
            hipsDrive += Local(hips) / samples;
            leftDrive += Local(rig.LeftHand) / samples;
            rightDrive += Local(rig.RightHand) / samples;
            footDrive += Foot() / samples;
            headBoneDrive += Local(rig.Head).y / samples;
            headDrive = Mathf.Max(headDrive, Local(rig.Head).y + crownAboveHead);
            if (i == 0)
            {
                // Hair and caps rise well above the estimate: take the real
                // crown from the baked skin round the head.
                headDrive = Mathf.Max(headDrive, fighter.BakedCrown(0.4f));
            }
        }

        cabin.TryGetMarker("Seat", out Vector3 seat);
        cabin.TryGetMarker("Roof", out Vector3 roof);
        cabin.TryGetMarker("Floor", out Vector3 floor);
        float crownAboveHips = headDrive - hipsDrive.y;
        result.CrownAboveCushion = crownAboveHips + hipsAboveCushion;
        result.CushionRoom = roof.y - RoofClearance - seat.y;
        float hipsY = seat.y + hipsAboveCushion;
        float maximumHipsY = roof.y - RoofClearance - crownAboveHips;
        result.Sink = Mathf.Max(0f, hipsY - maximumHipsY);
        hipsY -= result.Sink;
        // In a tall car (the Hummer) the seated feet must reach the cabin
        // floor, not dangle out under the body; raise the seat while the
        // roof allows it.
        // The foot bone sits above the sole: keep the soles on the floor.
        float feetOnFloorHipsY = floor.y + (hipsDrive.y - footDrive) + SoleBelowFootBone;
        if (hipsY < feetOnFloorHipsY) hipsY = Mathf.Min(feetOnFloorHipsY, maximumHipsY);
        result.HeadClearance = roof.y - (hipsY + crownAboveHips);
        result.CushionLocalY = Mathf.Max(floor.y + 0.06f, hipsY - hipsAboveCushion);
        Vector3 hipsTarget = new Vector3(seat.x, hipsY, seat.z);

        result.AnchorLocal = new Vector3(hipsTarget.x - hipsExit0.x, groundLocalY, hipsTarget.z - hipsExit0.z);
        result.ExitStartOffsetY = hipsTarget.y - (groundLocalY + hipsExit0.y);
        result.DrivingOffsetLocal = hipsTarget - (result.AnchorLocal + hipsDrive);
        result.EndFootLift = footWalk - footExit1;
        cabin.TryGetMarker("Hinge", out Vector3 hinge);
        result.Rise = PlanRise(fighter, rig, actor, result, roof.y, cabin.DoorSillLocalY, hinge.x, headDrive - headBoneDrive,
            out result.RiseConflicts);
        result.Shift = planShift;
        result.ShiftAxis = planShiftAxis;
        result.HipsOutPhase = Mathf.Min(1f, hipsOut);
        result.DoorOpenStart = doorOpenStart;
        result.DoorOpenEnd = doorOpenEnd;
        result.DoorCloseStart = doorCloseStart;
        result.DoorCloseEnd = doorCloseEnd;

        Quaternion turn = hipsExit1Rotation * Quaternion.Inverse(hipsExit0Rotation);
        Vector3 facing = Vector3.ProjectOnPlane(turn * Vector3.forward, Vector3.up);
        result.DoorRotationLocal = facing.sqrMagnitude > 1e-4f
            ? Quaternion.LookRotation(facing.normalized, Vector3.up) : Quaternion.identity;
        Vector3 endHips = result.AnchorLocal + hipsExit1;
        Vector3 walkHips = result.DoorRotationLocal * new Vector3(hipsWalk.x, 0f, hipsWalk.z);
        result.DoorPointLocal = new Vector3(endHips.x - walkHips.x, groundLocalY, endHips.z - walkHips.z);
        // The walk starts where the shifted climb ends.
        result.DoorPointLocal += planShiftAxis * planShift[RiseSamples];
        result.LanePointLocal = new Vector3(result.DoorPointLocal.x, groundLocalY, aisleEndLocal.z);

        Vector3 seatedShift = result.AnchorLocal + result.DrivingOffsetLocal;
        result.WheelLeftLocal = seatedShift + leftDrive;
        result.WheelRightLocal = seatedShift + rightDrive;
        result.Valid = true;
        result.Grounded = fighter.GroundOffsetsReady;
        GymLog.Diag(
            $"GYMCHAOS_VEHICLE_DRIVER_FIT identity={fighter.OccupantName} height={height:F2} " +
            $"sink={result.Sink:F3} headClearance={result.HeadClearance:F3} " +
            $"exitOffset={result.ExitStartOffsetY:F3} endFootLift={result.EndFootLift:F3} " +
            $"doorYaw={result.DoorRotationLocal.eulerAngles.y:F0} door={result.DoorPointLocal} " +
            $"riseConflicts={result.RiseConflicts} shiftEnd={result.Shift[RiseSamples]:F2} " +
            $"inCarScale={result.InCarScale:F2} scaleOut={result.ScaleOutStart:F2} " +
            $"doorTiming={result.DoorOpenStart:F2}-{result.DoorOpenEnd:F2}/{result.DoorCloseStart:F2}-{result.DoorCloseEnd:F2}",
            this);
        return result;
    }

    private static Mesh crownScratch;

    // Highest baked skin vertex (actor-local y) within `radius` of the head
    // bone horizontally, at or above it. The largest skinned mesh under
    // `skinOwner` is the body.
    internal static float BakedSkinCrown(Transform actor, Component skinOwner, Transform head, float radius)
    {
        SkinnedMeshRenderer skin = null;
        foreach (SkinnedMeshRenderer candidate in skinOwner.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (candidate.sharedMesh != null && (skin == null ||
                candidate.sharedMesh.vertexCount > skin.sharedMesh.vertexCount))
            {
                skin = candidate;
            }
        }
        Vector3 headLocal = actor.InverseTransformPoint(head.position);
        if (skin == null) return headLocal.y;
        if (crownScratch == null) crownScratch = new Mesh { name = "Driver Crown Bake" };
        Vector3[] vertices = GymSkinSampler.BakeWorld(skin, crownScratch);
        float top = headLocal.y;
        float radiusSquared = radius * radius;
        for (int i = 0; i < vertices.Length; i++)
        {
            Vector3 v = actor.InverseTransformPoint(vertices[i]);
            float dx = v.x - headLocal.x, dz = v.z - headLocal.z;
            if (v.y > top && dx * dx + dz * dz < radiusSquared) top = v.y;
        }
        return top;
    }

    private void ApplySeat(bool driving)
    {
        anchor.localPosition = fit.AnchorLocal;
        anchor.localRotation = Quaternion.identity;
        if (driving)
        {
            driver.SetVehiclePose(MixamoScanRetargetAnimator.VehiclePose.Driving, 0f,
                transform.TransformVector(fit.DrivingOffsetLocal), fit.InCarScale);
        }
    }

    private const int RiseSamples = 64;

    private float ShiftAt(float t)
    {
        if (fit.Shift == null) return 0f;
        float x = Mathf.Clamp01(t) * RiseSamples;
        int i = Mathf.Min(RiseSamples - 1, (int)x);
        return Mathf.Lerp(fit.Shift[i], fit.Shift[i + 1], x - i);
    }

    private float RiseAt(float t)
    {
        if (fit.Rise == null) return Smooth(RiseStart, RiseEnd, t);
        float x = Mathf.Clamp01(t) * RiseSamples;
        int i = Mathf.Min(RiseSamples - 1, (int)x);
        return Mathf.Lerp(fit.Rise[i], fit.Rise[i + 1], x - i);
    }

    // Per climb sample: the default fade, limited so that while the head is
    // still inside the door aperture its crown stays under the roof edge,
    // and while a foot crosses the threshold (or is outside) it stays above
    // the sill (or the road). Head clearance wins a conflict.
    private float[] PlanRise(IVehicleSeatOccupant occupant, BodybuilderEnemyVisual.Rig rig, Transform actor, Fit result,
        float roofY, float sillY, float doorX, float crownAboveHeadBone, out int conflicts)
    {
        conflicts = 0;
        float firstInSwing = 2f, lastInSwing = -1f;
        hipsOut = 2f;
        float hingeZ = 0f;
        if (cabin.TryGetMarker("Hinge", out Vector3 hingeLocal)) hingeZ = hingeLocal.z;
        float reach = cabin.DoorReach;
        float side = Mathf.Sign(doorX);
        float doorOuter = Mathf.Abs(doorX);
        float e = result.ExitStartOffsetY;
        float[] rise = new float[RiseSamples + 1];
        // Open door leaf in the car's x/z plane.
        float along = Mathf.Sign(reach == 0f ? 1f : reach);
        Vector2 hinge2 = new Vector2(doorX, hingeZ);
        float open = cabin.DoorOpenSign * GymVehicleCabin.DoorOpenDegrees * Mathf.Deg2Rad;
        Vector2 leaf0 = new Vector2(0f, Mathf.Abs(reach) * along);
        Vector2 leaf = new Vector2(leaf0.x * Mathf.Cos(open) + leaf0.y * Mathf.Sin(open),
            -leaf0.x * Mathf.Sin(open) + leaf0.y * Mathf.Cos(open));
        Vector2 leafDir = leaf.normalized;
        Vector2 rear = new Vector2(0f, along);
        Vector2 leafNormal = new Vector2(-leafDir.y, leafDir.x);
        if (Vector2.Dot(leafNormal, rear) < 0f) leafNormal = -leafNormal;
        float normalAlongRear = Mathf.Max(0.2f, Vector2.Dot(leafNormal, rear));
        float pillar = hingeZ + reach;
        float[] shiftNeed = new float[RiseSamples + 1];
        float[] shiftLimit = new float[RiseSamples + 1];
        Transform[] shiftBones = { rig.Head, rig.Chest, rig.Hips, rig.LeftHand, rig.RightHand,
            rig.LeftForearm, rig.RightForearm, rig.LeftShin, rig.RightShin, rig.LeftFoot, rig.RightFoot };
        occupant.GetBodyVolumes(planVolumes);
        bool hitboxes = planVolumes.Count > 0;
        // Bone points stand for thick limbs: bodybuilder forearms, fists, calves.
        float[] radii = { 0.18f, 0.34f, 0.3f, 0.17f, 0.17f, 0.2f, 0.2f, 0.17f, 0.17f, 0.15f, 0.15f };
        for (int k = 0; k <= RiseSamples; k++)
        {
            float t = k / (float)RiseSamples;
            occupant.SampleVehiclePoseNow(MixamoScanRetargetAnimator.VehiclePose.Exit, t,
                ScaleFor(result.InCarScale, result.ScaleOutStart, t));
            float g = result.EndFootLift * Smooth(GroundMatchStart, 1f, t);
            if (rig.Hips != null && hipsOut > 1f)
            {
                Vector3 hipsNow = actor.InverseTransformPoint(rig.Hips.position) + result.AnchorLocal;
                if (side * hipsNow.x > doorOuter + 0.25f) hipsOut = t;
            }
            float rMin = 0f, rMax = 1f;
            // O(r) = e * (1 - r) + g; bound O <= upper or O >= lower.
            void Upper(float bound)
            {
                if (Mathf.Abs(e) < 1e-4f) return;
                float r = 1f - (bound - g) / e;
                if (e < 0f) rMax = Mathf.Min(rMax, r); else rMin = Mathf.Max(rMin, r);
            }
            void Lower(float bound)
            {
                if (Mathf.Abs(e) < 1e-4f) return;
                float r = 1f - (bound - g) / e;
                if (e < 0f) rMin = Mathf.Max(rMin, r); else rMax = Mathf.Min(rMax, r);
            }
            Vector3 head = actor.InverseTransformPoint(rig.Head.position) + result.AnchorLocal;
            // Inside the cabin and under the roof rail above the door.
            if (side * head.x < doorOuter + 0.12f)
            {
                Upper(roofY - 0.03f - (head.y + crownAboveHeadBone));
            }
            foreach (Transform foot in new[] { rig.LeftFoot, rig.RightFoot })
            {
                if (foot == null) continue;
                Vector3 f = actor.InverseTransformPoint(foot.position) + result.AnchorLocal;
                float lateral = side * f.x;
                if (lateral > doorOuter + 0.05f) Lower(groundLocalY + 0.06f - f.y);
                else if (lateral > doorOuter - 0.3f) Lower(sillY + 0.04f - f.y);
            }
            // The skin, not only the bones: soles below the ankle, thick
            // calves and thighs over the sill, shoulders and fists under the
            // roof rail. Volumes are the skin-sized hitbox capsules.
            float bodyScale = ScaleFor(result.InCarScale, result.ScaleOutStart, t);
            if (hitboxes)
            {
                occupant.GetBodyVolumes(planVolumes);
                for (int vi = 0; vi < planVolumes.Count; vi++)
                {
                    (Vector3 wa, Vector3 wb, float wr) = planVolumes[vi];
                    float r = wr * bodyScale;
                    for (int si = 0; si < 3; si++)
                    {
                        Vector3 p = actor.InverseTransformPoint(Vector3.Lerp(wa, wb, si * 0.5f)) + result.AnchorLocal;
                        float lateral = side * p.x;
                        if (p.y - r < sillY + 0.35f)
                        {
                            // Low body: over the sill while crossing the
                            // threshold, above the road once outside.
                            if (lateral - r > doorOuter + 0.05f) Lower(groundLocalY + 0.02f - (p.y - r));
                            else if (lateral + r > doorOuter - 0.12f) Lower(sillY + 0.03f - (p.y - r));
                        }
                        else if (lateral - r < doorOuter + 0.12f)
                        {
                            // Upper body still in the aperture: under the roof rail.
                            Upper(roofY - 0.03f - (p.y + r));
                        }
                    }
                }
            }
            // Door swing: outside the closed door plane, along its length.
            foreach (Transform bone in new[] { rig.Head, rig.Chest, rig.Hips, rig.LeftHand, rig.RightHand,
                rig.LeftForearm, rig.RightForearm, rig.LeftShin, rig.RightShin, rig.LeftFoot, rig.RightFoot })
            {
                if (bone == null) continue;
                Vector3 b = actor.InverseTransformPoint(bone.position) + result.AnchorLocal;
                float alongDoor = (b.z - hingeZ) * Mathf.Sign(reach);
                bool inSwing = side * b.x + 0.2f > doorOuter &&
                    side * b.x < doorOuter + Mathf.Abs(reach) + 0.25f &&
                    alongDoor > -0.25f && alongDoor < Mathf.Abs(reach) + 0.25f;
                if (inSwing)
                {
                    firstInSwing = Mathf.Min(firstInSwing, t);
                    lastInSwing = Mathf.Max(lastInSwing, t);
                }
            }
            if (hitboxes)
            {
                for (int vi = 0; vi < planVolumes.Count; vi++)
                {
                    (Vector3 wa, Vector3 wb, float wr) = planVolumes[vi];
                    float r = wr * bodyScale;
                    for (int si = 0; si < 3; si++)
                    {
                        Vector3 p3 = actor.InverseTransformPoint(Vector3.Lerp(wa, wb, si * 0.5f)) + result.AnchorLocal;
                        Vector2 p = new Vector2(p3.x, p3.z);
                        if (side * p.x <= doorOuter - 0.1f) continue;
                        float alongLeaf = Vector2.Dot(p - hinge2, leafDir);
                        if (alongLeaf > -r && alongLeaf < leaf.magnitude + r + 0.1f)
                        {
                            float distance = Vector2.Dot(p - hinge2, leafNormal);
                            shiftNeed[k] = Mathf.Max(shiftNeed[k], (r + 0.06f - distance) / normalAlongRear);
                        }
                    }
                }
            }
            shiftLimit[k] = 0.85f;
            for (int bi = 0; bi < shiftBones.Length; bi++)
            {
                if (shiftBones[bi] == null) continue;
                Vector3 b3 = actor.InverseTransformPoint(shiftBones[bi].position) + result.AnchorLocal;
                Vector2 b = new Vector2(b3.x, b3.z);
                float lateral = side * b.x;
                if (lateral > doorOuter - 0.1f)
                {
                    float alongLeaf = Vector2.Dot(b - hinge2, leafDir);
                    if (alongLeaf > -radii[bi] && alongLeaf < leaf.magnitude + radii[bi] + 0.1f)
                    {
                        float distance = Vector2.Dot(b - hinge2, leafNormal);
                        shiftNeed[k] = Mathf.Max(shiftNeed[k],
                            (radii[bi] + 0.07f - distance) / normalAlongRear);
                    }
                }
                if (lateral > doorOuter - 0.55f && lateral < doorOuter + 0.05f)
                {
                    // Still in the aperture: stay ahead of the rear pillar.
                    shiftLimit[k] = Mathf.Min(shiftLimit[k],
                        (pillar - b3.z) * along - radii[bi] - 0.03f);
                }
            }
            float rDefault = Smooth(RiseStart, RiseEnd, t);
            if (rMin > rMax)
            {
                conflicts++;
                rMin = rMax;
            }
            rise[k] = Mathf.Clamp(Mathf.Clamp(rDefault, rMin, rMax), 0f, 1f);
        }
        rise[RiseSamples] = 1f;
        // Smooth the limited curve so the body never twitches between samples.
        float[] smooth = new float[rise.Length];
        for (int pass = 0; pass < 2; pass++)
        {
            for (int k = 0; k < rise.Length; k++)
            {
                float sum = 0f;
                int n = 0;
                for (int d = -2; d <= 2; d++)
                {
                    int j = Mathf.Clamp(k + d, 0, rise.Length - 1);
                    sum += rise[j];
                    n++;
                }
                smooth[k] = sum / n;
            }
            System.Array.Copy(smooth, rise, rise.Length);
        }
        rise[0] = 0f;
        rise[RiseSamples] = 1f;
        // Shift: never back toward the leaf once moved (running maximum),
        // within the pillar limit, smoothed.
        float[] shift = new float[RiseSamples + 1];
        float running = 0f;
        for (int k = 0; k <= RiseSamples; k++)
        {
            running = Mathf.Max(running, Mathf.Clamp(shiftNeed[k], 0f, 0.85f));
            shift[k] = Mathf.Max(0f, Mathf.Min(running, shiftLimit[k]));
        }
        for (int pass = 0; pass < 2; pass++)
        {
            float[] copy = (float[])shift.Clone();
            for (int k = 1; k < RiseSamples; k++)
                shift[k] = (copy[k - 1] + copy[k] + copy[k + 1]) / 3f;
        }
        shift[0] = 0f;
        planShift = shift;
        planShiftAxis = new Vector3(0f, 0f, along);
        if (lastInSwing < 0f)
        {
            firstInSwing = DoorOpenEnd;
            lastInSwing = DoorCloseStart;
        }
        doorOpenEnd = Mathf.Clamp(Mathf.Min(DoorOpenEnd, firstInSwing - 0.02f), 0.06f, 0.6f);
        doorOpenStart = Mathf.Clamp(Mathf.Min(DoorOpenStart, doorOpenEnd - 0.12f), 0.01f, doorOpenEnd - 0.05f);
        doorCloseStart = Mathf.Clamp(Mathf.Max(DoorCloseStart, lastInSwing + 0.03f), doorOpenEnd + 0.05f, 0.95f);
        doorCloseEnd = Mathf.Min(0.995f, doorCloseStart + (DoorCloseEnd - DoorCloseStart));
        return rise;
    }

    private static float Smooth(float a, float b, float t)
    {
        float x = Mathf.Clamp01((t - a) / Mathf.Max(1e-4f, b - a));
        return x * x * (3f - 2f * x);
    }

    private float lastDoorOpen;

    // Door SFX: unlatch as it starts to swing out, thunk as it shuts.
    private void SetDoor(float open)
    {
        if (open > 0.02f && lastDoorOpen <= 0.02f)
        {
            GymAudio.Play(GymSoundEffect.CarDoorOpen, cabin.MarkerWorld("Hinge"), 0.8f);
        }
        else if (open <= 0.001f && lastDoorOpen > 0.001f)
        {
            GymAudio.Play(GymSoundEffect.CarDoorClose, cabin.MarkerWorld("Hinge"), 0.9f);
        }
        lastDoorOpen = open;
        cabin.SetDoorOpen01(open);
    }

    private float doorOpenStart, doorOpenEnd, doorCloseStart, doorCloseEnd;
    private float[] planShift;
    private readonly List<(Vector3 a, Vector3 b, float radius)> planVolumes =
        new List<(Vector3, Vector3, float)>();
    private float hipsOut;
    private Vector3 planShiftAxis;

    private float DoorAt(float t) => fit.DoorOpenEnd <= 0f
        ? Smooth(DoorOpenStart, DoorOpenEnd, t) * (1f - Smooth(DoorCloseStart, DoorCloseEnd, t))
        : Smooth(fit.DoorOpenStart, fit.DoorOpenEnd, t) * (1f - Smooth(fit.DoorCloseStart, fit.DoorCloseEnd, t));

    private Vector3 ExitOffsetWorld(float t)
    {
        // At t=0 the hips sit exactly where the driving pose holds them.
        Vector3 local = Vector3.up * (fit.ExitStartOffsetY * (1f - RiseAt(t)) +
            fit.EndFootLift * Smooth(GroundMatchStart, 1f, t)) + fit.ShiftAxis * ShiftAt(t);
        return transform.TransformVector(local);
    }

    private void Update()
    {
        if (!HasDriver)
        {
            // The driver was destroyed mid-sequence: finish so the owner
            // (director, police) never waits on it.
            if (State != SeatState.Empty && State != SeatState.WaitingForCabin)
            {
                Action done = onFinished;
                driver = null;
                onFinished = null;
                State = SeatState.Empty;
                lastDoorOpen = 0f;
                cabin?.SetDoorOpen01(0f);
                done?.Invoke();
            }
            return;
        }
        if (driver.IsDead)
        {
            Release();
            return;
        }
        float step = Time.deltaTime * PlaybackSpeed / clipLength;
        switch (State)
        {
            case SeatState.Seated:
                ApplySeat(true);
                break;
            case SeatState.Exiting:
                exitPhase = Mathf.Min(1f, exitPhase + step);
                anchor.localPosition = fit.AnchorLocal;
                anchor.localRotation = Quaternion.identity;
                driver.SetVehiclePose(MixamoScanRetargetAnimator.VehiclePose.Exit, exitPhase,
                    ExitOffsetWorld(exitPhase), ScaleAt(exitPhase));
                SetDoor(DoorAt(exitPhase));
                if (exitPhase >= 1f)
                {
                    // Stand on the walking root exactly where the clip ended.
                    anchor.SetPositionAndRotation(DoorPointWorld, transform.rotation * fit.DoorRotationLocal);
                    driver.SetVehiclePose(MixamoScanRetargetAnimator.VehiclePose.None, 0f, Vector3.zero);
                    cabin.SetDoorOpen01(0f);
                    walkTarget = LanePointWorld;
                    walkFinalRotation = Quaternion.identity;
                    State = SeatState.WalkingAway;
                    Debug.Log($"GYMCHAOS_VEHICLE_DRIVER_EXITED identity={driver.OccupantName} " +
                        $"door={DoorPointWorld}", this);
                }
                break;
            case SeatState.WalkingAway:
                if (Walk(walkTarget, false))
                {
                    IVehicleSeatOccupant standing = driver;
                    Action done = onFinished;
                    standing.SetMoving(false, 1f);
                    standing.EndVehicleSeat(anchor.position, anchor.rotation);
                    driver = null;
                    onFinished = null;
                    State = SeatState.Empty;
                    Debug.Log($"GYMCHAOS_VEHICLE_DRIVER_STANDING identity={standing.OccupantName} " +
                        $"point={standing.Root.position}", this);
                    done?.Invoke();
                }
                break;
            case SeatState.WalkingIn:
                if (Walk(walkTarget, true))
                {
                    driver.SetMoving(false, 1f);
                    exitPhase = 1f;
                    anchor.localPosition = fit.AnchorLocal;
                    anchor.localRotation = Quaternion.identity;
                    driver.SetVehiclePose(MixamoScanRetargetAnimator.VehiclePose.Exit, 1f,
                        ExitOffsetWorld(1f), 1f);
                    State = SeatState.Boarding;
                }
                break;
            case SeatState.Boarding:
                exitPhase = Mathf.Max(0f, exitPhase - step);
                driver.SetVehiclePose(MixamoScanRetargetAnimator.VehiclePose.Exit, exitPhase,
                    ExitOffsetWorld(exitPhase), ScaleAt(exitPhase));
                SetDoor(DoorAt(exitPhase));
                if (exitPhase <= 0f)
                {
                    cabin.SetDoorOpen01(0f);
                    ApplySeat(true);
                    State = SeatState.Seated;
                    Action done = onFinished;
                    onFinished = null;
                    Debug.Log($"GYMCHAOS_VEHICLE_DRIVER_BOARDED identity={driver.OccupantName}", this);
                    done?.Invoke();
                }
                break;
        }
    }

    // Walks the free anchor to `target`; when `face` the last steps turn to
    // walkFinalRotation. True on arrival.
    private bool Walk(Vector3 target, bool face)
    {
        Vector3 position = anchor.position;
        Vector3 to = Vector3.ProjectOnPlane(target - position, Vector3.up);
        float distance = to.magnitude;
        float stepLength = WalkSpeed * Mathf.Max(1f, PlaybackSpeed * 0.8f) * Time.deltaTime;
        Quaternion rotation = anchor.rotation;
        if (distance > 0.02f)
        {
            Quaternion heading = Quaternion.LookRotation(to / distance, Vector3.up);
            if (face && distance < 0.45f)
            {
                heading = Quaternion.Slerp(walkFinalRotation, heading, distance / 0.45f);
            }
            rotation = Quaternion.RotateTowards(rotation, heading,
                WalkTurnDegreesPerSecond * Time.deltaTime);
            // Turn before striding off when the heading is far off.
            float alignment = Mathf.Clamp01(Vector3.Dot(rotation * Vector3.forward, to / distance));
            position += to / distance * Mathf.Min(distance, stepLength * Mathf.Lerp(0.35f, 1f, alignment));
            driver.SetMoving(true, 0.45f);
        }
        position.y = target.y;
        bool arrived = distance <= 0.02f;
        if (arrived && face)
        {
            rotation = Quaternion.RotateTowards(rotation, walkFinalRotation,
                WalkTurnDegreesPerSecond * Time.deltaTime);
            arrived = Quaternion.Angle(rotation, walkFinalRotation) < 1f;
            driver.SetMoving(!arrived, 0.2f);
            if (arrived) position = target;
        }
        anchor.SetPositionAndRotation(position, rotation);
        return arrived;
    }

    private void LateUpdate()
    {
        if (cabin == null || !HasDriver) return;
        if (State == SeatState.Seated || State == SeatState.Exiting || State == SeatState.Boarding)
        {
            KeepFeetAboveFloor();
        }
        // The rim follows the hands' steering motion while seated.
        if (State != SeatState.Seated) return;
        BodybuilderEnemyVisual.Rig rig = driver.BodyRig;
        if (rig?.LeftHand == null || rig.RightHand == null) return;
        Vector3 left = transform.InverseTransformPoint(rig.LeftHand.position);
        Vector3 right = transform.InverseTransformPoint(rig.RightHand.position);
        float angle = cabin.WheelAngleFor(left, right) - cabin.WheelAngleFor(fit.WheelLeftLocal, fit.WheelRightLocal);
        cabin.FitSteeringWheel(fit.WheelLeftLocal, fit.WheelRightLocal, -angle);
    }

    // The clip's legs hang down from a normal seat; folded into a low car
    // they would push through its floor and sill. Swing each whole leg up
    // about the hip (knee bend kept) until the foot rests on the cabin floor
    // inside, clears the sill over the threshold and stays on the road out.
    private void KeepFeetAboveFloor()
    {
        BodybuilderEnemyVisual.Rig rig = driver.BodyRig;
        if (rig == null || !cabin.TryGetMarker("Floor", out Vector3 floor) ||
            !cabin.TryGetMarker("Hinge", out Vector3 hinge)) return;
        float scale = State == SeatState.Seated ? fit.InCarScale : ScaleAt(exitPhase);
        float ankle = fit.AnkleHeight * scale;
        float side = Mathf.Sign(hinge.x);
        float outer = Mathf.Abs(hinge.x);
        Vector3 nose = cabin.TryGetMarker("Wheel", out Vector3 wheel) && cabin.TryGetMarker("Seat", out Vector3 seatPoint)
            ? Vector3.ProjectOnPlane(wheel - seatPoint, Vector3.up).normalized : Vector3.forward;
        LiftLeg(rig.LeftThigh, rig.LeftFoot);
        LiftLeg(rig.RightThigh, rig.RightFoot);

        void LiftLeg(Transform thigh, Transform foot)
        {
            if (thigh == null || foot == null) return;
            Vector3 footLocal = transform.InverseTransformPoint(foot.position);
            float lateral = side * footLocal.x;
            float inside = floor.y + 0.05f + ankle;
            float sill = cabin.DoorSillLocalY + 0.04f + ankle;
            float road = groundLocalY + ankle;
            float minimum = lateral < outer - 0.25f
                ? Mathf.Lerp(inside, sill, Mathf.InverseLerp(outer - 0.45f, outer - 0.25f, lateral))
                : Mathf.Lerp(sill, road, Mathf.InverseLerp(outer + 0.05f, outer + 0.3f, lateral));
            if (footLocal.y >= minimum) return;
            Vector3 hipLocal = transform.InverseTransformPoint(thigh.position);
            Vector3 reach = footLocal - hipLocal;
            float length = reach.magnitude;
            if (length < 0.05f) return;
            float drop = Mathf.Clamp(minimum - hipLocal.y, -0.98f * length, 0.5f * length);
            Vector3 flat = new Vector3(reach.x, 0f, reach.z);
            Vector3 heading = flat.sqrMagnitude > 1e-4f ? flat.normalized : nose;
            Vector3 target = heading * Mathf.Sqrt(Mathf.Max(0f, length * length - drop * drop)) +
                Vector3.up * drop;
            thigh.rotation = Quaternion.FromToRotation(
                transform.TransformDirection(reach), transform.TransformDirection(target)) * thigh.rotation;
        }
    }
}
