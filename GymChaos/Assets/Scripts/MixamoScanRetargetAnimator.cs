using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Drives an enemy from the animation clips baked into that enemy's own
/// authored Blender FBX.  The class keeps the gameplay-facing state API used
/// by EnemyFighter, but it no longer creates a hidden source rig or copies
/// rotations from a shared skeleton at runtime.
/// </summary>
[DefaultExecutionOrder(900)]
public sealed class MixamoScanRetargetAnimator : MonoBehaviour
{
    public const string CanonicalSquatSourcePath =
        "Assets/BodyBuilders/enemies/anims/squat.fbx";
    private const float BasePunchDuration = 0.72f;
    // Goku punches at 2x speed: the same clip plays in half the time.
    private float PunchDuration =>
        configuredIdentity == BodybuilderIdentity.Goku ? BasePunchDuration * 0.5f : BasePunchDuration;
    private const float AuthoredTransitionDuration = 0.12f;

    public enum VehiclePose
    {
        None,
        Driving,
        Exit
    }

    public enum MotionState
    {
        Uninitialized,
        Idle,
        Running,
        Punching,
        GunDrawing,
        GunShooting,
        Flying,
        Celebration,
        Downed
    }

    private BodybuilderEnemyVisual.Rig rig;
    private Transform modelRoot;
    private AnimationClip walkingClip;
    private AnimationClip runClip;
    private AnimationClip punchClip;
    private AnimationClip idleClip;
    private AnimationClip flyClip;
    private AnimationClip celebrationClip;
    private AnimationClip authoredWalkingClip;
    private AnimationClip authoredRunningClip;
    private AnimationClip authoredPunchClip;
    private AnimationClip authoredFlyingClip;
    private AnimationClip authoredSquatClip;
    private AnimationClip authoredDeadliftClip;
    // Driven visitors sit in their car (car_driving loops) and climb out of
    // the driver door (car_exit, played backwards to get in). Both keep the
    // clip's own root motion; GymVehicleDriverSeat places the actor root and
    // adds a seat offset so the hips stay on the seat.
    private AnimationClip authoredCarDrivingClip;
    private AnimationClip authoredCarExitClip;
    // Goku on his cloud: held nimbus_stance pose, soles planted on the cloud.
    private AnimationClip nimbusStanceClip;
    private Func<Vector3, float> nimbusSurface;
    public bool HasNimbusStanceClip => nimbusStanceClip != null;
    public bool IsNimbusStanceActive => nimbusSurface != null && nimbusStanceClip != null;
    public float NimbusLeftSoleGapForVerification { get; private set; }
    public float NimbusRightSoleGapForVerification { get; private set; }
    private VehiclePose vehiclePose;
    private float vehicleExitPhase;
    private float vehicleDriveTime;
    private Vector3 vehicleRootOffset;
    // Uniform scale of a seated driver (the giant bodybuilders fold into
    // 1.25x cars); 1 once out of the car.
    private float vehicleScale = 1f;
    private float vehicleSampleScale = -1f;
    // The workout pose plays the squat unless a deadlift set selected its own
    // clip. The deadlift phase is a clamped position in the clip (the set
    // runs it forward and back), never a loop.
    private bool deadliftWorkoutClip;
    private AnimationClip authoredGunDrawingClip;
    private AnimationClip authoredGunShootingClip;
    private AnimationClip[] idleVariants = new AnimationClip[0];
    private AnimationClip[] celebrationVariants = new AnimationClip[0];
    private readonly Dictionary<Transform, Quaternion> restRotations =
        new Dictionary<Transform, Quaternion>();
    private readonly Dictionary<Transform, Vector3> restPositions =
        new Dictionary<Transform, Vector3>();
    private readonly Dictionary<Transform, Vector3> restScales =
        new Dictionary<Transform, Vector3>();
    private Vector3 modelBaseLocalPosition;
    private Quaternion modelBaseLocalRotation;
    private Vector3 modelBaseLocalScale;
    private EnemyFighter owningFighter;
    private int treadmillGroundingSamples;
    private float treadmillMinimumRootLocalY;
    private float treadmillMaximumRootLocalY;
    private bool moving;
    private bool useRunningClip;
    private bool flying;
    private bool downed;
    private float speed01;
    private float runTime;
    private float idleTime;
    private float flightTime;
    private float attackTime = -1f;
    private AnimationClip gunActionClip;
    private float gunActionTime = -1f;
    private bool gunActionReverse;
    private MotionState gunActionState;
    private bool punchContactSent;
    private bool celebrating;
    private bool workoutPoseLocked;
    private float workoutPoseTime;
    private bool workoutPosePhaseDriven;
    private float workoutPosePhase;
    private Vector3 punchDirection = Vector3.forward;
    private Vector3 punchTargetPosition;
    private bool hasPunchTarget;
    private MotionState lastMotionState = MotionState.Uninitialized;
    private BodybuilderIdentity configuredIdentity;
    private int variantSeed;
    private int idleVariantCursor = -1;
    private int celebrationVariantCursor = -1;
    private string lastAnimationMarker;
    private MotionState lastAnimationMarkerState = MotionState.Uninitialized;
    private AnimationClip lastAnimationMarkerClip;
    private string lastAnimationMarkerBranch;
    private bool hasAnimationMarker;
    private int authoredLoadedClipCount;
    private string authoredLoadedClipNames = string.Empty;
    private bool configured;
    private readonly AuthoredPoseTransition poseTransition = new AuthoredPoseTransition();
    private AnimationClip lastSampledClip;
    private string lastSampledBranch;
    private bool hasSampledPose;
    private int ExpectedAuthoredClipCount =>
        configuredIdentity == BodybuilderIdentity.Mark ? 3 :
        configuredIdentity == BodybuilderIdentity.Policeman ? 7 : 11;

    public bool HasRunClip => runClip != null;
    public bool HasWalkingClip => walkingClip != null;
    public bool HasPunchClip => punchClip != null;
    public bool HasIdleClip => idleClip != null;
    public bool HasProceduralIdle => false;
    public bool HasFlightPose => rig != null && rig.RightUpperArm != null &&
        rig.RightForearm != null && rig.RightHand != null;
    public bool HasFlyClip => flyClip != null;
    public bool HasCelebrationClip => celebrationClip != null;
    public bool HasAuthoredWalkingClip => authoredWalkingClip != null;
    public bool HasAuthoredRunningClip => authoredRunningClip != null;
    public bool HasAuthoredPunchClip => authoredPunchClip != null;
    public bool HasAuthoredFlyingClip => authoredFlyingClip != null;
    public bool HasAuthoredSquatClip => authoredSquatClip != null;
    public AnimationClip AuthoredSquatClipForVerification => authoredSquatClip;
    public bool HasAuthoredDeadliftClip => authoredDeadliftClip != null;
    public AnimationClip AuthoredDeadliftClip => authoredDeadliftClip;
    public bool IsDeadliftWorkoutClip => deadliftWorkoutClip;
    public bool HasVehicleClips => authoredCarDrivingClip != null && authoredCarExitClip != null;
    public float CarExitClipLength => authoredCarExitClip != null ? authoredCarExitClip.length : 0f;
    public VehiclePose CurrentVehiclePose => vehiclePose;
    public BodybuilderEnemyVisual.Rig BodyRig => rig;
    public bool HasGunDrawingClip => authoredGunDrawingClip != null;
    public bool HasGunShootingClip => authoredGunShootingClip != null;
    public bool IsGunActionPlaying => gunActionTime >= 0f;
    public bool IsGunActionComplete => !IsGunActionPlaying;
    public bool HasAuthoredAnimationSetup =>
        authoredLoadedClipCount == ExpectedAuthoredClipCount;
    public Transform RuntimeModelRoot => modelRoot;
    public int AuthoredIdleClipCount => CountAvailable(idleVariants);
    public int AuthoredCelebrationClipCount => CountAvailable(celebrationVariants);
    public string AuthoredAnimationResourcePath => ResourcePath(configuredIdentity);
    public bool RuntimeModelRootIsAuthoredInstance =>
        modelRoot != null && modelRoot.IsChildOf(transform);
    public string CurrentAnimationClipName => GetClipNameForMarker(lastAnimationMarker);
    public bool IsPunchComplete => attackTime >= PunchDuration;
    public MotionState CurrentState => lastMotionState;
    public MotionState LastMotionState => lastMotionState;
    public bool IsWorkoutPoseLocked => workoutPoseLocked;
    public bool IsUsingRunningClip => useRunningClip;
    public bool HasGroundedSkeletonRoot => rig != null && rig.Root != null;
    public int TreadmillGroundingSamples => treadmillGroundingSamples;
    public float TreadmillRootBounce => treadmillGroundingSamples > 0
        ? treadmillMaximumRootLocalY - treadmillMinimumRootLocalY
        : float.PositiveInfinity;
    public float GroundedRootVerticalDrift
    {
        get
        {
            if (rig == null || rig.Root == null || rig.Root.parent == null ||
                !restPositions.TryGetValue(
                    rig.Root, out Vector3 restRootLocalPosition))
            {
                return float.PositiveInfinity;
            }

            // The grounded root is the bind position plus the clip's
            // constant ground offset.
            Vector3 restRootWorldPosition =
                rig.Root.parent.TransformPoint(restRootLocalPosition) +
                (modelRoot != null ? modelRoot.up : Vector3.up) * appliedGroundOffset;
            return Mathf.Abs(Vector3.Dot(
                rig.Root.position - restRootWorldPosition, Vector3.up));
        }
    }

    public void ApplyGroundedRootCorrection(Vector3 worldUp, float worldDelta)
    {
        if (rig == null || rig.Root == null ||
            worldUp.sqrMagnitude < 0.0001f ||
            Mathf.Abs(worldDelta) < 0.00001f)
        {
            return;
        }

        ApplyRootWorldCorrection(worldUp.normalized * worldDelta);
    }

    public void ApplyRootWorldCorrection(Vector3 worldDelta)
    {
        if (rig == null || rig.Root == null ||
            worldDelta.sqrMagnitude < 0.00000001f)
        {
            return;
        }

        rig.Root.position += worldDelta;
    }
    public bool Configure(
        BodybuilderIdentity identity, BodybuilderEnemyVisual.Rig bodyRig)
    {
        return Configure(identity, bodyRig, ResolveModelRoot(bodyRig));
    }

    public bool Configure(
        BodybuilderIdentity identity,
        BodybuilderEnemyVisual.Rig bodyRig,
        Transform authoredRoot)
    {
        configuredIdentity = identity;
        variantSeed = StableVariantSeed(identity);
        rig = bodyRig;
        modelRoot = authoredRoot != null ? authoredRoot : ResolveModelRoot(bodyRig);
        owningFighter = GetComponentInParent<EnemyFighter>();
        lastAnimationMarker = null;
        hasAnimationMarker = false;
        lastAnimationMarkerClip = null;
        lastAnimationMarkerBranch = null;

        string resourcePath = ResourcePath(identity);
        bool policeSet = identity == BodybuilderIdentity.Policeman;
        // The policeman has a deliberately small authored set: running,
        // punch_combo, celebration variants and the two firearm actions.
        // Do not load the ordinary enemy-only walking/flying/squat/idles for
        // this role; running is used as its locomotion fallback.
        authoredWalkingClip = policeSet
            ? null : LoadAuthoredClip(resourcePath, "walking");
        authoredRunningClip = LoadAuthoredClip(resourcePath, "running");
        authoredPunchClip = LoadAuthoredClip(resourcePath, "punch_combo");
        authoredFlyingClip = policeSet
            ? null : LoadAuthoredClip(resourcePath, "flying");
        authoredSquatClip = policeSet
            ? null : LoadAuthoredClip(resourcePath, "squat");
        authoredDeadliftClip = policeSet
            ? null : LoadAuthoredClip(resourcePath, "deadlift");
        authoredCarDrivingClip = LoadAuthoredClip(resourcePath, "vehicle_driving");
        authoredCarExitClip = LoadAuthoredClip(resourcePath, "vehicle_exit");
        nimbusStanceClip = LoadAuthoredClip(resourcePath, "nimbus_stance");
        vehiclePose = VehiclePose.None;
        authoredGunDrawingClip = policeSet
            ? LoadAuthoredClip(resourcePath, "gun_drawing") : null;
        authoredGunShootingClip = policeSet
            ? LoadAuthoredClip(resourcePath, "gun_shooting") : null;
        idleVariants = policeSet
            ? new AnimationClip[0]
            : CompactVariants(
                LoadAuthoredClip(resourcePath, "idle1"),
                LoadAuthoredClip(resourcePath, "idle2"),
                LoadAuthoredClip(resourcePath, "idle3"));
        celebrationVariants = CompactVariants(
            LoadAuthoredClip(resourcePath, "celebration1"),
            LoadAuthoredClip(resourcePath, "celebration2"),
            LoadAuthoredClip(resourcePath, "celebration3"));
        authoredLoadedClipNames = BuildAuthoredClipNameList();
        authoredLoadedClipCount = CountLoadedAuthoredClips();

        walkingClip = policeSet ? authoredRunningClip : authoredWalkingClip;
        runClip = authoredRunningClip;
        punchClip = authoredPunchClip;
        flyClip = authoredFlyingClip;
        idleClip = null;
        celebrationClip = null;
        SelectInitialIdleVariant();
        SelectCelebrationVariant();

        Debug.Log(
            $"GYMCHAOS_ENEMY_AUTHORED_CLIP_INVENTORY identity={identity} " +
            $"path={resourcePath} loadedCount={authoredLoadedClipCount} " +
            $"loadedNames={authoredLoadedClipNames} " +
            $"walking={ClipName(authoredWalkingClip)} " +
            $"running={ClipName(authoredRunningClip)} " +
            $"idle1={ClipName(GetVariant(idleVariants, 0))} " +
            $"idle2={ClipName(GetVariant(idleVariants, 1))} " +
            $"idle3={ClipName(GetVariant(idleVariants, 2))} " +
            $"flying={ClipName(authoredFlyingClip)} " +
            $"squat={ClipName(authoredSquatClip)} " +
            $"deadlift={ClipName(authoredDeadliftClip)} " +
            $"punch_combo={ClipName(authoredPunchClip)} " +
            $"celebration1={ClipName(GetVariant(celebrationVariants, 0))} " +
            $"celebration2={ClipName(GetVariant(celebrationVariants, 1))} " +
            $"celebration3={ClipName(GetVariant(celebrationVariants, 2))} " +
            $"selectedIdle={ClipName(idleClip)} " +
            $"selectedCelebration={ClipName(celebrationClip)}",
            this);

        if (modelRoot == null || bodyRig == null || !HasAuthoredAnimationSetup)
        {
            Debug.LogError(
                $"GYMCHAOS_ENEMY_AUTHORED_CLIP_SETUP_FAILED identity={identity} " +
                $"modelRoot={modelRoot != null} bodyRig={bodyRig != null} " +
                $"loadedCount={authoredLoadedClipCount}/{ExpectedAuthoredClipCount} " +
                $"requiredPath=Assets/Resources/{resourcePath}.fbx", this);
            return false;
        }

        CaptureRestPose();
        configured = true;
        clipGroundOffsets.Clear();
        groundOffsetsMeasured = false;
        groundOffsetsLogged = false;
        RestoreVisibleRestPose();
        if (idleClip != null)
        {
            SampleDirect(idleClip, 0f);
        }
        poseTransition.Cancel();
        lastSampledClip = idleClip;
        lastSampledBranch = "idle";
        hasSampledPose = idleClip != null;
        Debug.Log(
            $"GYMCHAOS_DIRECT_AUTHORED_ANIMATION_OK identity={identity} " +
            $"model={modelRoot.name} clips={authoredLoadedClipCount} " +
            $"idle={ClipName(idleClip)} walking={ClipName(walkingClip)} " +
            $"run={ClipName(runClip)} punch={ClipName(punchClip)} " +
            $"flight={ClipName(flyClip)} squat={ClipName(authoredSquatClip)} " +
            $"gunDrawing={ClipName(authoredGunDrawingClip)} " +
            $"gunShooting={ClipName(authoredGunShootingClip)} " +
            $"celebration={ClipName(celebrationClip)}",
            this);
        return true;
    }

    public void CaptureFittedModelTransform()
    {
        if (modelRoot == null)
        {
            return;
        }
        modelBaseLocalPosition = modelRoot.localPosition;
        modelBaseLocalRotation = modelRoot.localRotation;
        modelBaseLocalScale = modelRoot.localScale;
        if (configured && idleClip != null)
        {
            SampleDirect(idleClip, 0f);
        }
    }

    public void SetConversationActive(bool active)
    {
        // Conversation uses the same authored idle as the rest of the game.
        // Keep this API for dialogue code without injecting a second
        // procedural skeleton pose over the exported character animation.
    }

    public void SetMoving(
        bool shouldMove, float normalizedSpeed = 1f,
        bool shouldUseRunningClip = false)
    {
        if (attackTime >= PunchDuration)
        {
            attackTime = -1f;
            punchContactSent = false;
        }

        bool enteringIdle = !shouldMove &&
            (moving || lastMotionState != MotionState.Idle);
        // Intent selects the authored clip. Roaming may reach full walk speed
        // and must still remain Walking; chase/anger explicitly selects Run.
        bool nextRunningClip = shouldMove && shouldUseRunningClip;
        bool enteringOrChangingLocomotion = shouldMove &&
            (!moving || useRunningClip != nextRunningClip);
        if (enteringOrChangingLocomotion)
        {
            runTime = 0f;
        }
        if (enteringIdle)
        {
            idleTime = 0f;
        }
        moving = shouldMove;
        speed01 = shouldMove ? Mathf.Clamp01(normalizedSpeed) : 0f;
        useRunningClip = nextRunningClip;
        celebrating = false;
        if (enteringIdle)
        {
            SelectNextIdleVariant();
        }
        lastMotionState = shouldMove ? MotionState.Running : MotionState.Idle;
    }

    public void PrepareForWorkoutPose()
    {
        if (!configured || workoutPoseLocked)
        {
            return;
        }

        moving = false;
        flying = false;
        celebrating = false;
        attackTime = -1f;
        punchContactSent = false;
        lastMotionState = MotionState.Idle;
        workoutPoseLocked = true;
        workoutPoseTime = 0f;
        workoutPosePhaseDriven = false;
        workoutPosePhase = 0f;
        RestoreVisibleRestPose();
    }

    public void SetWorkoutPosePhase(float normalizedPhase)
    {
        if (!configured || !workoutPoseLocked)
        {
            return;
        }

        workoutPosePhase = deadliftWorkoutClip
            ? Mathf.Clamp01(normalizedPhase)
            : Mathf.Repeat(normalizedPhase, 1f);
        workoutPosePhaseDriven = true;
    }

    /// <summary>
    /// Goku stands on his cloud in the nimbus stance; <paramref name="surface"/>
    /// gives the cloud's top height under a world point (NaN off the cloud).
    /// Null ends the stance.
    /// </summary>
    public bool SetNimbusStance(Func<Vector3, float> surface)
    {
        if (surface != null && nimbusStanceClip == null)
        {
            return false;
        }
        nimbusSurface = surface;
        nimbusSoleOffsets = Vector2.zero;
        if (surface != null)
        {
            moving = false;
            flying = false;
            celebrating = false;
            attackTime = -1f;
            punchContactSent = false;
        }
        return true;
    }

    // The held clip keeps its soles on the clip ground. Lower the whole body
    // so the soles meet the cloud on average, then bend each leg so its sole
    // rests exactly on the cloud under it (the cloud top is uneven).
    private void PlantFeetOnSurface(Func<Vector3, float> surface)
    {
        if (rig == null || rig.Root == null || rig.LeftThigh == null || rig.LeftShin == null ||
            rig.LeftFoot == null || rig.RightThigh == null || rig.RightShin == null || rig.RightFoot == null)
        {
            return;
        }
        // Ankle above the boot sole, measured once on the baked skin of
        // this pose (boots and Goku's scale make any proportion guess float).
        if (nimbusSoleOffsets.x <= 0f)
        {
            nimbusSoleOffsets = MeasureSoleOffsets();
        }
        float leftSole = nimbusSoleOffsets.x;
        float rightSole = nimbusSoleOffsets.y;
        float leftTarget = surface(rig.LeftFoot.position) + leftSole;
        float rightTarget = surface(rig.RightFoot.position) + rightSole;
        if (float.IsNaN(leftTarget) || float.IsNaN(rightTarget))
        {
            NimbusLeftSoleGapForVerification = float.NaN;
            NimbusRightSoleGapForVerification = float.NaN;
            return;
        }
        float shift = ((leftTarget - rig.LeftFoot.position.y) + (rightTarget - rig.RightFoot.position.y)) * 0.5f;
        rig.Root.position += Vector3.up * shift;
        SolveLegToHeight(rig.LeftThigh, rig.LeftShin, rig.LeftFoot, leftTarget);
        SolveLegToHeight(rig.RightThigh, rig.RightShin, rig.RightFoot, rightTarget);
        NimbusLeftSoleGapForVerification = rig.LeftFoot.position.y - leftTarget;
        NimbusRightSoleGapForVerification = rig.RightFoot.position.y - rightTarget;
    }

    private Vector2 nimbusSoleOffsets;
    private Mesh soleScratch;

    private Vector2 MeasureSoleOffsets()
    {
        float fallback = 0.196f * Vector3.Distance(rig.LeftThigh.position, rig.LeftShin.position);
        SkinnedMeshRenderer skin = null;
        foreach (SkinnedMeshRenderer candidate in GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (candidate.sharedMesh != null && (skin == null || candidate.sharedMesh.vertexCount > skin.sharedMesh.vertexCount))
            {
                skin = candidate;
            }
        }
        if (skin == null)
        {
            return new Vector2(fallback, fallback);
        }
        if (soleScratch == null) soleScratch = new Mesh { name = "Nimbus Sole Bake" };
        Vector3[] world = GymSkinSampler.BakeWorld(skin, soleScratch);
        float Lowest(Transform foot)
        {
            float lowest = float.PositiveInfinity;
            for (int i = 0; i < world.Length; i += 2)
            {
                Vector3 p = world[i];
                float dx = p.x - foot.position.x, dz = p.z - foot.position.z;
                if (dx * dx + dz * dz < 0.09f && p.y < foot.position.y + 0.1f && p.y < lowest) lowest = p.y;
            }
            return float.IsInfinity(lowest) ? fallback : Mathf.Clamp(foot.position.y - lowest, 0.02f, 0.4f);
        }
        return new Vector2(Lowest(rig.LeftFoot), Lowest(rig.RightFoot));
    }

    // Two-bone leg: moves the ankle straight up/down to targetY, keeping the
    // knee in its current bend plane and the foot's world rotation.
    private static void SolveLegToHeight(Transform thigh, Transform shin, Transform foot, float targetY)
    {
        Quaternion footRotation = foot.rotation;
        Vector3 hip = thigh.position;
        Vector3 knee = shin.position;
        Vector3 ankle = foot.position;
        Vector3 target = new Vector3(ankle.x, targetY, ankle.z);
        float upper = Vector3.Distance(hip, knee);
        float lower = Vector3.Distance(knee, ankle);
        Vector3 toTarget = target - hip;
        float reach = Mathf.Clamp(toTarget.magnitude, Mathf.Abs(upper - lower) + 0.001f, upper + lower - 0.001f);
        Vector3 axis = toTarget.normalized;
        // Bend direction: the current knee offset from the hip-ankle line.
        Vector3 bend = Vector3.ProjectOnPlane(knee - hip, axis);
        if (bend.sqrMagnitude < 1e-6f)
        {
            bend = Vector3.ProjectOnPlane(thigh.forward, axis);
        }
        bend.Normalize();
        float along = (upper * upper - lower * lower + reach * reach) / (2f * reach);
        float height = Mathf.Sqrt(Mathf.Max(0f, upper * upper - along * along));
        Vector3 newKnee = hip + axis * along + bend * height;
        thigh.rotation = Quaternion.FromToRotation(knee - hip, newKnee - hip) * thigh.rotation;
        Vector3 currentAnkle = foot.position;
        shin.rotation = Quaternion.FromToRotation(currentAnkle - shin.position, hip + axis * reach - shin.position) * shin.rotation;
        foot.rotation = footRotation;
    }

    /// <summary>
    /// Holds the car pose: Driving loops car_driving; Exit samples car_exit
    /// at <paramref name="exitPhase"/> (0 seated, 1 standing outside).
    /// <paramref name="rootWorldOffset"/> moves the sampled skeleton root.
    /// </summary>
    public bool SetVehiclePose(VehiclePose pose, float exitPhase, Vector3 rootWorldOffset,
        float scale = 1f)
    {
        vehicleScale = pose == VehiclePose.None ? 1f : Mathf.Clamp(scale, 0.5f, 1f);
        if (pose != VehiclePose.None && !HasVehicleClips)
        {
            return false;
        }
        if (pose == VehiclePose.Driving && vehiclePose != VehiclePose.Driving)
        {
            vehicleDriveTime = 0f;
        }
        vehiclePose = pose;
        vehicleExitPhase = Mathf.Clamp01(exitPhase);
        vehicleRootOffset = rootWorldOffset;
        if (pose != VehiclePose.None)
        {
            moving = false;
            flying = false;
            celebrating = false;
            attackTime = -1f;
            punchContactSent = false;
        }
        return true;
    }

    /// <summary>
    /// Writes a car pose right now (no seat offset) so the seat can measure
    /// this rig's own hips, hands and head. The next LateUpdate samples the
    /// selected pose again.
    /// </summary>
    public bool SampleVehiclePoseNow(VehiclePose pose, float normalized, float scale = 1f)
    {
        if (!configured || !HasVehicleClips || pose == VehiclePose.None)
        {
            return false;
        }
        AnimationClip clip = pose == VehiclePose.Driving ? authoredCarDrivingClip : authoredCarExitClip;
        float duration = Mathf.Max(0.01f, clip.length - 0.001f);
        vehicleSampleScale = scale;
        bool sampled = SampleDirect(clip, Mathf.Clamp01(normalized) * duration);
        vehicleSampleScale = -1f;
        return sampled;
    }

    /// <summary>
    /// Writes the first walking frame (pinned, grounded root) right now and
    /// returns false when there is no walking clip.
    /// </summary>
    public bool SampleWalkingPoseNow()
    {
        if (!configured || walkingClip == null)
        {
            return false;
        }
        return SampleDirect(walkingClip, 0f);
    }

    /// <summary>Selects the deadlift (true) or squat clip for the locked workout pose.</summary>
    public bool SetDeadliftWorkoutClip(bool deadlift)
    {
        if (deadlift && authoredDeadliftClip == null)
        {
            return false;
        }
        deadliftWorkoutClip = deadlift;
        return true;
    }

    /// <summary>
    /// Writes the deadlift pose at <paramref name="normalized"/> right now, so
    /// a set can measure this rig's own hands before it starts. The next
    /// LateUpdate samples the workout pose again.
    /// </summary>
    public bool SampleDeadliftPoseNow(float normalized)
    {
        if (!configured || authoredDeadliftClip == null)
        {
            return false;
        }
        float duration = Mathf.Max(0.01f, authoredDeadliftClip.length - 0.001f);
        return SampleDirect(authoredDeadliftClip, Mathf.Clamp01(normalized) * duration);
    }

    // Per clip, the vertical shift that puts its planted feet (lowest foot
    // skin over the whole cycle) on the level the model was fitted to: the
    // bind pose, because the hidden actor is fitted before any clip can be
    // evaluated. With the skeleton root pinned, each Mixamo clip plants its
    // feet at its own height; without this the walk or the run of some rigs
    // sank several centimetres into the floor or the treadmill belt.
    private readonly Dictionary<AnimationClip, float> clipGroundOffsets =
        new Dictionary<AnimationClip, float>();
    private const int GroundSamplesPerFrame = 16;
    private const int MaxGroundSamplesPerClip = 64;
    // Measured over the first visible frames: the clip sampler cannot
    // evaluate the rig while the prepared actor is still hidden.
    private bool groundOffsetsMeasured;
    private bool groundOffsetsLogged;
    private readonly List<AnimationClip> groundQueue = new List<AnimationClip>();
    private AnimationClip groundClip;
    private int groundSample;
    private int groundSamples;
    private float groundClipMin;
    private float groundLevel = float.NaN;
    private SkinnedMeshRenderer groundBody;
    // Foot skin, skinned by hand: a full BakeMesh of these 100k-vertex scans
    // per sample stalled the first frames of every visitor.
    private Vector3[] groundFootVertices;
    private BoneWeight[] groundFootWeights;
    private Matrix4x4[] groundBoneMatrices;
    private bool[] groundBoneUsed;
    private Transform[] groundBones;
    private Matrix4x4[] groundBindposes;
    private Transform[] groundSnapshotBones;
    private Vector3[] groundSnapshotPositions;
    private Quaternion[] groundSnapshotRotations;
    private Vector3[] groundSnapshotScales;
    private string groundDetails = string.Empty;
    // Ground offset of the clip sampled last; part of the grounded root.
    private float appliedGroundOffset;
    public float AppliedGroundOffsetForVerification => appliedGroundOffset;
    public bool GroundOffsetsReadyForVerification => groundOffsetsLogged;
    public float SkeletonRootHeightForVerification =>
        rig != null && rig.Root != null && modelRoot != null
            ? Vector3.Dot(rig.Root.position - modelRoot.position, modelRoot.up) : float.NaN;

    public float GetClipGroundOffsetForVerification(AnimationClip clip) =>
        clip != null && clipGroundOffsets.TryGetValue(clip, out float offset) ? offset : 0f;

    private void BeginGroundOffsetMeasurement()
    {
        groundOffsetsMeasured = true;
        clipGroundOffsets.Clear();
        groundQueue.Clear();
        groundClip = null;
        groundDetails = string.Empty;
        groundBody = FindBodyRenderer();
        Mesh mesh = groundBody != null ? groundBody.sharedMesh : null;
        if (mesh == null || !mesh.isReadable || rig == null ||
            rig.LeftFoot == null || rig.RightFoot == null)
        {
            groundOffsetsLogged = true;
            return;
        }
        // Only the skin the feet carry: toes and soles.
        int[] dominant = GymSkinSampler.GetDominantBones(mesh);
        groundBones = groundBody.bones;
        groundBindposes = mesh.bindposes;
        Vector3[] vertices = mesh.vertices;
        BoneWeight[] weights = mesh.boneWeights;
        List<Vector3> footVertices = new List<Vector3>();
        List<BoneWeight> footWeights = new List<BoneWeight>();
        groundBoneUsed = new bool[groundBones.Length];
        for (int i = 0; i < dominant.Length && i < vertices.Length && i < weights.Length; i++)
        {
            Transform bone = dominant[i] >= 0 && dominant[i] < groundBones.Length ? groundBones[dominant[i]] : null;
            if (!GymSkinSampler.IsInChain(bone, rig.LeftFoot) && !GymSkinSampler.IsInChain(bone, rig.RightFoot))
            {
                continue;
            }
            BoneWeight weight = weights[i];
            footVertices.Add(vertices[i]);
            footWeights.Add(weight);
            MarkGroundBone(weight.boneIndex0, weight.weight0);
            MarkGroundBone(weight.boneIndex1, weight.weight1);
            MarkGroundBone(weight.boneIndex2, weight.weight2);
            MarkGroundBone(weight.boneIndex3, weight.weight3);
        }
        if (footVertices.Count == 0)
        {
            groundOffsetsLogged = true;
            return;
        }
        groundFootVertices = footVertices.ToArray();
        groundFootWeights = footWeights.ToArray();
        groundBoneMatrices = new Matrix4x4[groundBones.Length];

        SnapshotGroundPose();
        RestoreVisibleRestPose();
        groundLevel = MeasureSoleNow();
        RestoreGroundPose();
        AnimationClip[] grounded =
        {
            authoredWalkingClip, authoredRunningClip,
            GetVariant(idleVariants, 0), GetVariant(idleVariants, 1), GetVariant(idleVariants, 2),
            authoredPunchClip,
            GetVariant(celebrationVariants, 0), GetVariant(celebrationVariants, 1),
            GetVariant(celebrationVariants, 2)
        };
        foreach (AnimationClip clip in grounded)
        {
            if (clip != null && !groundQueue.Contains(clip))
            {
                groundQueue.Add(clip);
            }
        }
    }

    private void MarkGroundBone(int index, float weight)
    {
        if (weight > 0f && index >= 0 && index < groundBoneUsed.Length)
        {
            groundBoneUsed[index] = true;
        }
    }

    // A few samples per frame so the one-time measurement never spikes. The
    // visible pose is restored afterwards, so a pose transition starting this
    // frame never blends from a measurement pose.
    private void TickGroundOffsetMeasurement()
    {
        SnapshotGroundPose();
        float restoreGroundOffset = appliedGroundOffset;
        int budget = GroundSamplesPerFrame;
        while (budget-- > 0)
        {
            if (groundClip == null)
            {
                if (groundQueue.Count == 0)
                {
                    if (!groundOffsetsLogged)
                    {
                        groundOffsetsLogged = true;
                        Debug.Log($"GYMCHAOS_CLIP_GROUND_OFFSETS identity={configuredIdentity} " +
                            $"restSole={groundLevel:0.000}{groundDetails}", this);
                    }
                    break;
                }
                groundClip = groundQueue[0];
                groundQueue.RemoveAt(0);
                groundSample = 0;
                // Two samples per authored frame, capped for the long idles.
                groundSamples = Mathf.Clamp(
                    Mathf.CeilToInt(groundClip.length * groundClip.frameRate * 2f), 8, MaxGroundSamplesPerClip);
                groundClipMin = float.PositiveInfinity;
            }
            float duration = Mathf.Max(0.01f, groundClip.length - 0.001f);
            SampleDirect(groundClip, duration * groundSample / groundSamples);
            groundClipMin = Mathf.Min(groundClipMin, MeasureSoleNow());
            if (++groundSample < groundSamples)
            {
                continue;
            }
            // A correction is a pose height mismatch, never a teleport.
            float offset = Mathf.Clamp(groundLevel - groundClipMin, -0.3f, 0.3f);
            clipGroundOffsets[groundClip] = offset;
            groundDetails += $" {ClipName(groundClip)}={offset:0.000}" +
                (Mathf.Abs(groundLevel - groundClipMin) > 0.3f ? $"(raw={groundLevel - groundClipMin:0.000})" : "");
            groundClip = null;
        }
        RestoreGroundPose();
        appliedGroundOffset = restoreGroundOffset;
    }

    private void SnapshotGroundPose()
    {
        if (groundSnapshotBones == null)
        {
            groundSnapshotBones = modelRoot.GetComponentsInChildren<Transform>(true);
            groundSnapshotPositions = new Vector3[groundSnapshotBones.Length];
            groundSnapshotRotations = new Quaternion[groundSnapshotBones.Length];
            groundSnapshotScales = new Vector3[groundSnapshotBones.Length];
        }
        for (int i = 0; i < groundSnapshotBones.Length; i++)
        {
            Transform bone = groundSnapshotBones[i];
            groundSnapshotPositions[i] = bone.localPosition;
            groundSnapshotRotations[i] = bone.localRotation;
            groundSnapshotScales[i] = bone.localScale;
        }
    }

    private void RestoreGroundPose()
    {
        for (int i = 0; i < groundSnapshotBones.Length; i++)
        {
            Transform bone = groundSnapshotBones[i];
            if (bone == null)
            {
                continue;
            }
            bone.localPosition = groundSnapshotPositions[i];
            bone.localRotation = groundSnapshotRotations[i];
            bone.localScale = groundSnapshotScales[i];
        }
    }

    // Lowest foot skin above the model root, skinned by hand from the foot
    // vertices only. An offset SampleDirect already applied is removed so
    // every clip is measured on the same pinned root.
    private float MeasureSoleNow()
    {
        for (int b = 0; b < groundBones.Length; b++)
        {
            if (groundBoneUsed[b] && groundBones[b] != null)
            {
                groundBoneMatrices[b] = groundBones[b].localToWorldMatrix * groundBindposes[b];
            }
        }
        Vector3 up = modelRoot.up;
        Vector3 origin = modelRoot.position;
        float lowest = float.PositiveInfinity;
        for (int i = 0; i < groundFootVertices.Length; i++)
        {
            Vector3 v = groundFootVertices[i];
            BoneWeight w = groundFootWeights[i];
            Vector3 world =
                groundBoneMatrices[w.boneIndex0].MultiplyPoint3x4(v) * w.weight0 +
                (w.weight1 > 0f ? groundBoneMatrices[w.boneIndex1].MultiplyPoint3x4(v) * w.weight1 : Vector3.zero) +
                (w.weight2 > 0f ? groundBoneMatrices[w.boneIndex2].MultiplyPoint3x4(v) * w.weight2 : Vector3.zero) +
                (w.weight3 > 0f ? groundBoneMatrices[w.boneIndex3].MultiplyPoint3x4(v) * w.weight3 : Vector3.zero);
            lowest = Mathf.Min(lowest, Vector3.Dot(world - origin, up));
        }
        return lowest - appliedGroundOffset;
    }

    private SkinnedMeshRenderer FindBodyRenderer()
    {
        SkinnedMeshRenderer body = null;
        foreach (SkinnedMeshRenderer candidate in modelRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (candidate.sharedMesh != null &&
                (body == null || candidate.sharedMesh.vertexCount > body.sharedMesh.vertexCount))
            {
                body = candidate;
            }
        }
        return body;
    }

    private AnimationClip WorkoutClip => deadliftWorkoutClip ? authoredDeadliftClip : authoredSquatClip;

    public void ReleaseWorkoutPose()
    {
        workoutPoseLocked = false;
        deadliftWorkoutClip = false;
        workoutPosePhaseDriven = false;
        if (lastMotionState == MotionState.Uninitialized)
        {
            lastMotionState = MotionState.Idle;
        }
    }

    public void SetFlying(bool shouldFly)
    {
        if (shouldFly && attackTime >= 0f)
        {
            attackTime = -1f;
            punchContactSent = false;
        }
        if (shouldFly && !flying)
        {
            flightTime = 0f;
        }
        flying = shouldFly;
        celebrating = false;
        lastMotionState = shouldFly ? MotionState.Flying : MotionState.Idle;
        if (flying)
        {
            moving = false;
        }
    }

    public bool BeginGunDrawing()
    {
        return BeginGunAction(authoredGunDrawingClip, MotionState.GunDrawing);
    }

    public bool BeginGunShooting()
    {
        return BeginGunAction(authoredGunShootingClip, MotionState.GunShooting);
    }

    /// <summary>Puts the gun away: the draw clip played backwards.</summary>
    public bool BeginGunHolstering()
    {
        bool started = BeginGunAction(authoredGunDrawingClip, MotionState.GunDrawing);
        gunActionReverse = started;
        return started;
    }

    public void EndCelebration()
    {
        celebrating = false;
        if (lastMotionState == MotionState.Celebration)
        {
            lastMotionState = MotionState.Idle;
        }
    }

    private bool BeginGunAction(AnimationClip clip, MotionState state)
    {
        if (!configured || configuredIdentity != BodybuilderIdentity.Policeman || clip == null)
        {
            return false;
        }

        moving = false;
        flying = false;
        celebrating = false;
        attackTime = -1f;
        punchContactSent = false;
        gunActionClip = clip;
        gunActionState = state;
        gunActionTime = 0f;
        gunActionReverse = false;
        lastMotionState = state;
        return true;
    }
    public void CancelPunch()
    {
        attackTime = -1f;
        punchContactSent = false;
        hasPunchTarget = false;
        if (lastMotionState == MotionState.Punching)
        {
            lastMotionState = flying ? MotionState.Flying :
                moving ? MotionState.Running : MotionState.Idle;
        }
    }

    public void TriggerAttack()
    {
        attackTime = 0f;
        punchContactSent = false;
        celebrating = false;
        hasPunchTarget = false;
        lastMotionState = MotionState.Punching;
        punchDirection = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
        if (punchDirection.sqrMagnitude < 0.001f)
        {
            punchDirection = Vector3.forward;
        }
    }

    public void SetPunchDirection(Vector3 direction)
    {
        Vector3 planar = Vector3.ProjectOnPlane(direction, Vector3.up);
        if (planar.sqrMagnitude > 0.0001f)
        {
            punchDirection = planar.normalized;
        }
    }

    public void SetPunchTarget(Vector3 worldPosition)
    {
        punchTargetPosition = worldPosition;
        hasPunchTarget = true;
    }

    public void TriggerCelebration()
    {
        if (!downed)
        {
            celebrating = true;
            flying = false;
            moving = false;
            attackTime = -1f;
            lastMotionState = MotionState.Celebration;
        }
    }

    public bool TryConsumePunchContact(out Transform leftHand, out Transform rightHand)
    {
        leftHand = rig != null ? rig.LeftHand : null;
        rightHand = rig != null ? rig.RightHand : null;
        if (attackTime < 0f || punchContactSent || punchClip == null)
        {
            return false;
        }
        // Test the hand every frame across the extension instead of on one
        // sampled frame: a single frame at low frame rate or a target that
        // stepped a few centimetres missed most visibly connecting punches.
        float normalized = attackTime / PunchDuration;
        if (normalized < PunchContactWindowStart)
        {
            return false;
        }
        if (normalized > PunchContactWindowEnd)
        {
            punchContactSent = true;
            return false;
        }
        return true;
    }

    public const float PunchContactWindowStart = 0.42f;
    public const float PunchContactWindowEnd = 0.74f;

    /// <summary>Ends the contact window after a landed hit (one hit per punch).</summary>
    public void MarkPunchContactLanded()
    {
        punchContactSent = true;
    }

    public void SetDowned(bool isDowned)
    {
        downed = isDowned;
        if (downed)
        {
            gunActionTime = -1f;
            gunActionClip = null;
        }
        if (downed)
        {
            moving = false;
            flying = false;
            celebrating = false;
            attackTime = -1f;
            lastMotionState = MotionState.Downed;
        }
        else if (lastMotionState == MotionState.Downed)
        {
            lastMotionState = MotionState.Idle;
        }
    }

#if UNITY_EDITOR
    // Holds a gun clip at a fixed phase; pair with Time.timeScale = 0.
    public bool HoldGunActionForVerification(bool drawing, float normalizedTime)
    {
        AnimationClip clip = drawing ? authoredGunDrawingClip : authoredGunShootingClip;
        if (clip == null)
        {
            return false;
        }
        gunActionClip = clip;
        gunActionState = drawing ? MotionState.GunDrawing : MotionState.GunShooting;
        gunActionTime = Mathf.Clamp01(normalizedTime) * Mathf.Max(0.01f, clip.length - 0.001f);
        // A frozen clock never advances the blend, so show the clip directly.
        poseTransition.Cancel();
        hasSampledPose = true;
        lastSampledClip = clip;
        lastSampledBranch = drawing ? "gun-drawing" : "gun-shooting";
        return true;
    }

    public bool SampleAuthoredClipForVerification(
        string clipStem, float normalizedTime, out string details)
    {
        AnimationClip clip = FindAuthoredClip(clipStem);
        if (!configured || modelRoot == null || clip == null)
        {
            details = $"clip={clipStem} missing={clip == null} model={modelRoot != null}";
            return false;
        }

        float duration = Mathf.Max(0.01f, clip.length - 0.001f);
        bool sampled = SampleDirect(
            clip, Mathf.Clamp01(normalizedTime) * duration);
        int changedBones = CountChangedRestBones();
        float rootDelta = 0f;
        if (rig != null && rig.Root != null &&
            restPositions.TryGetValue(rig.Root, out Vector3 restRootPosition))
        {
            rootDelta = Vector3.Distance(rig.Root.localPosition, restRootPosition);
        }
        details =
            $"clip={clip.name} changedBones={changedBones} rootDelta={rootDelta:F4}";
        RestoreVisibleRestPose();
        return sampled && changedBones > 0;
    }

    public bool SampleAllAuthoredClipsForVerification(out string details)
    {
        string[] clipStems =
        {
            "walking", "running", "punch_combo", "flying", "squat",
            "idle1", "idle2", "idle3", "celebration1", "celebration2",
            "celebration3"
        };
        List<string> samples = new List<string>(clipStems.Length);
        bool valid = true;
        for (int i = 0; i < clipStems.Length; i++)
        {
            bool sampled = SampleAuthoredClipForVerification(
                clipStems[i], 0.5f, out string sampleDetails);
            valid &= sampled;
            samples.Add(sampleDetails);
        }
        details = string.Join(";", samples.ToArray());
        return valid;
    }

    public bool SampleAuthoredClipRangeForVerification(
        string clipStem, out string details)
    {
        AnimationClip clip = FindAuthoredClip(clipStem);
        if (!configured || modelRoot == null || clip == null)
        {
            details = $"clip={clipStem} missing={clip == null} model={modelRoot != null}";
            return false;
        }

        float duration = Mathf.Max(0.01f, clip.length - 0.001f);
        bool sampledStart = SampleDirect(clip, 0f);
        Dictionary<Transform, AuthoredPoseSample> startPose = CaptureAuthoredPose();
        Vector3 startRoot = GetAuthoredRootLocalPosition();
        int startChangedBones = CountChangedRestBones();

        bool sampledMiddle = SampleDirect(clip, duration * 0.5f);
        Dictionary<Transform, AuthoredPoseSample> middlePose = CaptureAuthoredPose();
        Vector3 middleRoot = GetAuthoredRootLocalPosition();
        int middleChangedBones = CountChangedRestBones();

        // Sample the actual imported clip endpoint. The runtime loop still
        // subtracts a tiny epsilon to avoid crossing the clip boundary, but
        // the authored source's final standing frame is the authoritative
        // endpoint for this structural rep check.
        bool sampledEnd = SampleDirect(clip, clip.length);
        Dictionary<Transform, AuthoredPoseSample> endPose = CaptureAuthoredPose();
        Vector3 endRoot = GetAuthoredRootLocalPosition();
        int endChangedBones = CountChangedRestBones();

        string endpointDeltaBone;
        string middleDeltaBone;
        float endpointPoseDelta = GetAuthoredPoseDelta(
            startPose, endPose, out endpointDeltaBone);
        float middlePoseDelta = GetAuthoredPoseDelta(
            startPose, middlePose, out middleDeltaBone);
        float endpointRootDelta = Vector3.Distance(startRoot, endRoot);
        float middleRootTravel = Vector3.Distance(startRoot, middleRoot);
        bool isSquat = string.Equals(clipStem, "squat", StringComparison.OrdinalIgnoreCase);
        bool valid = sampledStart && sampledMiddle && sampledEnd && isSquat &&
            clip.length > 0.01f && middleChangedBones > 0 && middlePoseDelta > 0.01f &&
            endpointPoseDelta < 0.12f && endpointRootDelta < 0.12f;
        details =
            $"clip={clip.name} length={clip.length:F3} " +
            $"startChanged={startChangedBones} middleChanged={middleChangedBones} " +
            $"endChanged={endChangedBones} middlePoseDelta={middlePoseDelta:F4} " +
            $"endpointPoseDelta={endpointPoseDelta:F4} " +
            $"endpointDeltaBone={endpointDeltaBone} middleDeltaBone={middleDeltaBone} " +
            $"endpointRootDelta={endpointRootDelta:F4} " +
            $"middleRootTravel={middleRootTravel:F4}";
        RestoreVisibleRestPose();
        return valid;
    }

    private struct AuthoredPoseSample
    {
        public Vector3 localPosition;
        public Quaternion localRotation;
        public Vector3 localScale;
    }

    private Dictionary<Transform, AuthoredPoseSample> CaptureAuthoredPose()
    {
        Dictionary<Transform, AuthoredPoseSample> pose =
            new Dictionary<Transform, AuthoredPoseSample>(restRotations.Count);
        foreach (KeyValuePair<Transform, Quaternion> pair in restRotations)
        {
            Transform bone = pair.Key;
            if (bone == null)
            {
                continue;
            }

            pose[bone] = new AuthoredPoseSample
            {
                localPosition = bone.localPosition,
                localRotation = bone.localRotation,
                localScale = bone.localScale
            };
        }
        return pose;
    }

    private static float GetAuthoredPoseDelta(
        Dictionary<Transform, AuthoredPoseSample> from,
        Dictionary<Transform, AuthoredPoseSample> to,
        out string maximumBone)
    {
        float maximum = 0f;
        maximumBone = "none";
        foreach (KeyValuePair<Transform, AuthoredPoseSample> pair in from)
        {
            if (!to.TryGetValue(pair.Key, out AuthoredPoseSample target))
            {
                continue;
            }

            AuthoredPoseSample source = pair.Value;
            float positionDelta = Vector3.Distance(
                source.localPosition, target.localPosition);
            if (positionDelta > maximum)
            {
                maximum = positionDelta;
                maximumBone = pair.Key.name + ":position";
            }
            float rotationDelta = Quaternion.Angle(
                source.localRotation, target.localRotation) / 180f;
            if (rotationDelta > maximum)
            {
                maximum = rotationDelta;
                maximumBone = pair.Key.name + ":rotation";
            }
            float scaleDelta = Vector3.Distance(
                source.localScale, target.localScale);
            if (scaleDelta > maximum)
            {
                maximum = scaleDelta;
                maximumBone = pair.Key.name + ":scale";
            }
        }
        return maximum;
    }

    private Vector3 GetAuthoredRootLocalPosition()
    {
        return rig != null && rig.Root != null ? rig.Root.localPosition : Vector3.zero;
    }

    public bool SamplePunchContactForVerification(
        out Vector3 leftHandPosition, out Vector3 rightHandPosition, out string details)
    {
        leftHandPosition = Vector3.zero;
        rightHandPosition = Vector3.zero;
        if (!configured || modelRoot == null || punchClip == null || rig == null ||
            rig.LeftHand == null || rig.RightHand == null)
        {
            details =
                $"configured={configured} model={modelRoot != null} clip={punchClip != null} " +
                $"leftHand={rig?.LeftHand != null} rightHand={rig?.RightHand != null}";
            return false;
        }

        // TryConsumePunchContact fires at >= 0.54. The physics query runs
        // before the following LateUpdate, so the visible hand pose is the
        // preceding frame''s pose. Sample just before that threshold to place
        // the verifier target on the same authored motion path.
        const float contactNormalizedTime = 0.52f;
        SampleDirectNormalized(punchClip, contactNormalizedTime);
        leftHandPosition = rig.LeftHand.position;
        rightHandPosition = rig.RightHand.position;
        details =
            $"clip={punchClip.name} normalized={contactNormalizedTime:F2} " +
            $"left={leftHandPosition} right={rightHandPosition}";
        RestoreVisibleRestPose();
        return true;
    }

    public void ResetToVerificationIdlePose()
    {
        if (!configured || modelRoot == null)
        {
            return;
        }

        moving = false;
        flying = false;
        celebrating = false;
        downed = false;
        attackTime = -1f;
        gunActionTime = -1f;
        gunActionClip = null;
        punchContactSent = false;
        idleTime = 0f;
        runTime = 0f;
        flightTime = 0f;
        RestoreVisibleRestPose();
        poseTransition.Cancel();
        lastSampledClip = idleClip;
        lastSampledBranch = "idle";
        hasSampledPose = idleClip != null;
        if (idleClip != null)
        {
            SampleDirect(idleClip, 0f);
        }
        lastMotionState = MotionState.Idle;
    }
#endif

    private static readonly Unity.Profiling.ProfilerMarker LateUpdateMarker =
        new Unity.Profiling.ProfilerMarker("GymChaos.Late.MixamoScanRetargetAnimator");

    private void LateUpdate()
    {
        using (LateUpdateMarker.Auto()) LateUpdateBody();
    }

    private void LateUpdateBody()
    {
        if (!configured || modelRoot == null)
        {
            return;
        }

        if (modelRoot.gameObject.activeInHierarchy && !groundOffsetsLogged)
        {
            if (!groundOffsetsMeasured)
            {
                BeginGroundOffsetMeasurement();
            }
            TickGroundOffsetMeasurement();
        }

        if (IsNimbusStanceActive)
        {
            lastMotionState = MotionState.Idle;
            PreparePoseTransition(nimbusStanceClip, "nimbus-stance");
            EmitAnimationMarker(MotionState.Idle, nimbusStanceClip, "nimbus-stance");
            SampleDirectNormalized(nimbusStanceClip, 0.5f);
            PlantFeetOnSurface(nimbusSurface);
            return;
        }

        if (vehiclePose != VehiclePose.None)
        {
            lastMotionState = MotionState.Idle;
            bool driving = vehiclePose == VehiclePose.Driving;
            AnimationClip vehicleClip = driving ? authoredCarDrivingClip : authoredCarExitClip;
            string branch = driving ? "car-driving" : "car-exit";
            PreparePoseTransition(vehicleClip, branch);
            EmitAnimationMarker(MotionState.Idle, vehicleClip, branch);
            if (driving)
            {
                vehicleDriveTime += Time.deltaTime;
                SampleDirectLoop(vehicleClip, vehicleDriveTime);
            }
            else
            {
                SampleDirectNormalized(vehicleClip, vehicleExitPhase);
            }
            if (rig != null && rig.Root != null)
            {
                rig.Root.position += vehicleRootOffset;
            }
            return;
        }

        if (workoutPoseLocked)
        {
            lastMotionState = MotionState.Idle;
            AnimationClip workoutClip = WorkoutClip;
            PreparePoseTransition(workoutClip, "workout");
            if (workoutClip != null)
            {
                if (deadliftWorkoutClip)
                {
                    SampleDirectNormalized(workoutClip, workoutPosePhase);
                }
                else
                {
                    if (workoutPosePhaseDriven)
                    {
                        float duration = Mathf.Max(0.01f, workoutClip.length - 0.001f);
                        workoutPoseTime = workoutPosePhase * duration;
                    }
                    else
                    {
                        workoutPoseTime += Time.deltaTime;
                    }
                    SampleDirectLoop(workoutClip, workoutPoseTime);
                }
                EmitAnimationMarker(MotionState.Idle, workoutClip, "workout");
            }
            else
            {
                PreparePoseTransition(null, "animation-disabled");
                RestoreVisibleRestPose();
                EmitAnimationMarker(MotionState.Idle, null, "animation-disabled");
            }
            return;
        }

        if (gunActionTime >= 0f)
        {
            lastMotionState = gunActionState;
            string branch = gunActionState == MotionState.GunDrawing
                ? "gun-drawing" : "gun-shooting";
            PreparePoseTransition(gunActionClip, branch);
            float duration = Mathf.Max(
                0.01f, gunActionClip != null ? gunActionClip.length - 0.001f : 0.01f);
            gunActionTime += Time.deltaTime;
            EmitAnimationMarker(gunActionState, gunActionClip, branch);
            float gunPhase = Mathf.Clamp01(gunActionTime / duration);
            SampleDirectNormalized(gunActionClip, gunActionReverse ? 1f - gunPhase : gunPhase);
            if (gunActionTime >= duration)
            {
                gunActionTime = -1f;
                gunActionClip = null;
            }
            return;
        }
        if (downed)
        {
            PreparePoseTransition(null, "downed");
            RestoreVisibleRestPose();
            lastMotionState = MotionState.Downed;
            EmitAnimationMarker(MotionState.Downed, null, "downed");
            return;
        }

        if (celebrating)
        {
            lastMotionState = MotionState.Celebration;
            PreparePoseTransition(celebrationClip, "celebration");
            EmitAnimationMarker(MotionState.Celebration, celebrationClip, "celebration");
            SampleDirectLoop(celebrationClip, Time.time);
            return;
        }

        if (flying)
        {
            lastMotionState = MotionState.Flying;
            PreparePoseTransition(flyClip, "flying");
            flightTime += Time.deltaTime;
            EmitAnimationMarker(MotionState.Flying, flyClip, "flying");
            SampleDirectLoop(flyClip, flightTime);
            return;
        }

        if (attackTime >= 0f)
        {
            lastMotionState = MotionState.Punching;
            PreparePoseTransition(punchClip, "attack");
            attackTime += Time.deltaTime;
            EmitAnimationMarker(MotionState.Punching, punchClip, "attack");
            float normalized = Mathf.Clamp01(attackTime / PunchDuration);
            SampleDirectNormalized(punchClip, normalized);
            return;
        }

        if (moving)
        {
            lastMotionState = MotionState.Running;
            runTime += Time.deltaTime * Mathf.Lerp(0.85f, 1.35f, speed01);
            AnimationClip locomotion = useRunningClip ? runClip : walkingClip;
            PreparePoseTransition(
                locomotion, useRunningClip ? "running" : "walking");
            EmitAnimationMarker(MotionState.Running, locomotion,
                useRunningClip ? "running" : "walking");
            SampleDirectLoop(locomotion, runTime);
            RecordTreadmillWalkingGrounding();
            return;
        }

        lastMotionState = MotionState.Idle;
        idleTime += Time.deltaTime;
        if (idleClip != null &&
            idleTime >= Mathf.Max(0.01f, idleClip.length - 0.001f))
        {
            // Let idle-only workers and neutral enemies move through their
            // authored idle set without needing a locomotion transition.
            // SelectNextIdleVariant avoids immediately repeating the same
            // clip when two or more variants are available.
            SelectNextIdleVariant();
        }
        PreparePoseTransition(idleClip, "idle");
        EmitAnimationMarker(MotionState.Idle, idleClip, "idle");
        SampleDirectLoop(idleClip, idleTime);
    }

    private void RecordTreadmillWalkingGrounding()
    {
        if (owningFighter == null)
        {
            owningFighter = GetComponentInParent<EnemyFighter>();
        }
        GymExerciseStation station = owningFighter != null
            ? owningFighter.CurrentTreadmill
            : null;
        bool settledTreadmill = station != null &&
            rig != null && rig.Root != null &&
            Mathf.Abs(owningFighter.transform.position.y - station.EnemyPosition.y) <= 0.02f;
        if (!settledTreadmill)
        {
            treadmillGroundingSamples = 0;
            treadmillMinimumRootLocalY = float.PositiveInfinity;
            treadmillMaximumRootLocalY = float.NegativeInfinity;
            return;
        }

        if (modelRoot == null)
        {
            return;
        }

        float rootLocalY = owningFighter.transform
            .InverseTransformPoint(rig.Root.position).y;

        treadmillMinimumRootLocalY = Mathf.Min(
            treadmillMinimumRootLocalY, rootLocalY);
        treadmillMaximumRootLocalY = Mathf.Max(
            treadmillMaximumRootLocalY, rootLocalY);
        treadmillGroundingSamples++;
    }

    private void PreparePoseTransition(AnimationClip nextClip, string branch)
    {
        if (!hasSampledPose)
        {
            hasSampledPose = true;
            lastSampledClip = nextClip;
            lastSampledBranch = branch;
            poseTransition.Cancel();
            return;
        }

        if (ReferenceEquals(lastSampledClip, nextClip) &&
            string.Equals(lastSampledBranch, branch, StringComparison.Ordinal))
        {
            return;
        }

        // The authored squat is the grounded contact source itself. Blending
        // local positions from a locomotion clip into it can import a source
        // rig root offset for one frame (some scans carry a large walking
        // translation channel). Start the squat clip directly so the solver
        // sees a coherent authored phase-zero pose; limb scales and clip data
        // remain untouched.
        if (ReferenceEquals(nextClip, authoredSquatClip) ||
            ReferenceEquals(nextClip, authoredDeadliftClip))
        {
            poseTransition.Cancel();
            lastSampledClip = nextClip;
            lastSampledBranch = branch;
            return;
        }

        if (nextClip != null)
        {
            poseTransition.Begin(modelRoot, AuthoredTransitionDuration);
            Debug.Log(
                $"GYMCHAOS_ENEMY_AUTHORED_TRANSITION identity={configuredIdentity} " +
                $"from={ClipName(lastSampledClip)} to={ClipName(nextClip)} " +
                $"branch={branch} duration={AuthoredTransitionDuration:F2}", this);
        }
        else
        {
            poseTransition.Cancel();
        }
        lastSampledClip = nextClip;
        lastSampledBranch = branch;
    }

    private void CaptureRestPose()
    {
        restRotations.Clear();
        restPositions.Clear();
        restScales.Clear();
        modelBaseLocalPosition = modelRoot.localPosition;
        modelBaseLocalRotation = modelRoot.localRotation;
        modelBaseLocalScale = modelRoot.localScale;
        Transform[] transforms = modelRoot.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < transforms.Length; i++)
        {
            Transform current = transforms[i];
            if (current == modelRoot)
            {
                continue;
            }
            restRotations[current] = current.localRotation;
            restPositions[current] = current.localPosition;
            restScales[current] = current.localScale;
        }
    }

    private void RestoreVisibleRestPose()
    {
        if (modelRoot == null)
        {
            return;
        }

        foreach (KeyValuePair<Transform, Quaternion> pair in restRotations)
        {
            if (pair.Key != null)
            {
                pair.Key.localRotation = pair.Value;
            }
        }
        foreach (KeyValuePair<Transform, Vector3> pair in restPositions)
        {
            if (pair.Key != null)
            {
                pair.Key.localPosition = pair.Value;
            }
        }
        foreach (KeyValuePair<Transform, Vector3> pair in restScales)
        {
            if (pair.Key != null)
            {
                pair.Key.localScale = pair.Value;
            }
        }
        modelRoot.localPosition = modelBaseLocalPosition;
        modelRoot.localRotation = modelBaseLocalRotation;
        modelRoot.localScale = modelBaseLocalScale;
    }

    private bool SampleDirect(AnimationClip clip, float sampleTime)
    {
        if (modelRoot == null || clip == null)
        {
            RestoreVisibleRestPose();
            return false;
        }

        RestoreVisibleRestPose();
        GymClipSampler.Sample(clip, modelRoot.gameObject, Mathf.Max(0f, sampleTime));
        // Blender's Rigify bake writes evaluated deform-bone scale channels
        // even though this retarget is rotation/translation driven. Restore
        // each character's own imported rest scales so those bake artifacts
        // cannot stretch fingers or change the visible body height.
        RestoreAnimatedScales();
        // The firearm clips were exported from the policeman's source rig as
        // separate actions. Their translation channels contain source-armature
        // offsets (including a large vertical offset) rather than meaningful
        // gameplay root motion. Keep their authored rotations, but restore the
        // policeman bind translations so the hand and attached Glock stay on
        // the grounded actor.
        bool policeFirearmClip =
            configuredIdentity == BodybuilderIdentity.Policeman &&
            (ReferenceEquals(clip, authoredGunDrawingClip) ||
             ReferenceEquals(clip, authoredGunShootingClip));
        if (policeFirearmClip)
        {
            foreach (KeyValuePair<Transform, Vector3> pair in restPositions)
            {
                if (pair.Key != null)
                {
                    pair.Key.localPosition = pair.Value;
                }
            }
        }

        // Root motion belongs to the EnemyFighter/Rigidbody, never to the
        // imported child model. The squat clip is a pose sequence, not a
        // locomotion path: keep its skeleton root on the authored grounded
        // bind position so per-foot IK has one stable support plane. Flying
        // remains the only intentional airborne root-motion exception.
        bool preserveAuthoredRootMotion =
            ReferenceEquals(clip, flyClip) || ReferenceEquals(clip, authoredSquatClip) ||
            ReferenceEquals(clip, authoredDeadliftClip) ||
            ReferenceEquals(clip, authoredCarDrivingClip) ||
            ReferenceEquals(clip, authoredCarExitClip);
        appliedGroundOffset = 0f;
        if (!preserveAuthoredRootMotion && rig != null &&
            rig.Root != null && restPositions.TryGetValue(
                rig.Root, out Vector3 rootPosition))
        {
            rig.Root.localPosition = rootPosition;
            // The pinned root plants each clip's feet at a different height
            // (the walk of some rigs sank ~3 cm below the run and idle). One
            // constant per clip moves its planted feet onto the fitted floor.
            if (!policeFirearmClip && modelRoot != null &&
                clipGroundOffsets.TryGetValue(clip, out float groundOffset))
            {
                appliedGroundOffset = groundOffset;
                rig.Root.position += modelRoot.up * groundOffset;
            }
        }
        modelRoot.localPosition = modelBaseLocalPosition;
        modelRoot.localRotation = modelBaseLocalRotation;
        modelRoot.localScale = modelBaseLocalScale;
        if (ReferenceEquals(clip, authoredCarDrivingClip) || ReferenceEquals(clip, authoredCarExitClip))
        {
            modelRoot.localScale = modelBaseLocalScale *
                (vehicleSampleScale > 0f ? vehicleSampleScale : vehicleScale);
        }
        return true;
    }

    private void RestoreAnimatedScales()
    {
        foreach (KeyValuePair<Transform, Vector3> pair in restScales)
        {
            if (pair.Key != null)
            {
                pair.Key.localScale = pair.Value;
            }
        }
    }

    private void SampleDirectLoop(AnimationClip clip, float elapsed)
    {
        if (clip == null)
        {
            poseTransition.Cancel();
            RestoreVisibleRestPose();
            return;
        }
        float duration = Mathf.Max(0.01f, clip.length - 0.001f);
        SampleDirect(clip, elapsed % duration);
        ApplyPoseTransitionPreservingGroundedRoot(clip);
    }

    private void SampleDirectNormalized(AnimationClip clip, float normalized)
    {
        if (clip == null)
        {
            poseTransition.Cancel();
            RestoreVisibleRestPose();
            return;
        }
        float duration = Mathf.Max(0.01f, clip.length - 0.001f);
        SampleDirect(clip, Mathf.Clamp01(normalized) * duration);
        ApplyPoseTransitionPreservingGroundedRoot(clip);
    }

    private void ApplyPoseTransitionPreservingGroundedRoot(AnimationClip clip)
    {
        bool preserveSampledRoot = rig != null && rig.Root != null &&
            !ReferenceEquals(clip, flyClip);
        Vector3 sampledRootPosition = preserveSampledRoot
            ? rig.Root.position
            : Vector3.zero;

        poseTransition.Apply(Time.deltaTime);

        if (preserveSampledRoot)
        {
            // AuthoredPoseTransition blends every child localPosition, which
            // includes this skeleton root. Let it blend the body pose, then
            // keep the normalized sampled root authoritative so a previous
            // clip's incompatible export origin cannot lift the model or
            // erase a squat floor correction between sampled frames.
            rig.Root.position = sampledRootPosition;
        }
    }

    private static Transform ResolveModelRoot(BodybuilderEnemyVisual.Rig bodyRig)
    {
        Transform current = bodyRig != null ? bodyRig.Root : null;
        if (current == null)
        {
            return null;
        }
        while (current.parent != null && current.parent.parent != null)
        {
            current = current.parent;
        }
        return current;
    }

    private static AnimationClip[] CompactVariants(params AnimationClip[] candidates)
    {
        List<AnimationClip> variants = new List<AnimationClip>();
        for (int i = 0; i < candidates.Length; i++)
        {
            if (candidates[i] != null)
            {
                variants.Add(candidates[i]);
            }
        }
        return variants.ToArray();
    }

    private static AnimationClip LoadAuthoredClip(string resourcePath, string fileStem)
    {
        if (string.IsNullOrEmpty(resourcePath))
        {
            return null;
        }

        UnityEngine.Object[] subAssets = Resources.LoadAll<UnityEngine.Object>(resourcePath);
        List<AnimationClip> candidates = new List<AnimationClip>();
        for (int i = 0; i < subAssets.Length; i++)
        {
            AnimationClip clip = subAssets[i] as AnimationClip;
            if (clip == null || clip.name.StartsWith("__preview__", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            candidates.Add(clip);
        }

        candidates.Sort((left, right) =>
            StringComparer.Ordinal.Compare(left.name, right.name));
        for (int i = 0; i < candidates.Count; i++)
        {
            string name = candidates[i].name;
            if (string.Equals(name, fileStem, StringComparison.OrdinalIgnoreCase) ||
                name.EndsWith("|" + fileStem, StringComparison.OrdinalIgnoreCase) ||
                name.EndsWith("_" + fileStem, StringComparison.OrdinalIgnoreCase))
            {
                return candidates[i];
            }
        }

        return null;
    }

    private int CountLoadedAuthoredClips()
    {
        int count = 0;
        AnimationClip[] clips = AuthoredClipSet();
        for (int i = 0; i < clips.Length; i++)
        {
            if (clips[i] != null)
            {
                count++;
            }
        }
        return count;
    }

    private AnimationClip FindAuthoredClip(string clipStem)
    {
        if (string.IsNullOrEmpty(clipStem))
        {
            return null;
        }

        if (string.Equals(clipStem, "walking", StringComparison.OrdinalIgnoreCase)) return authoredWalkingClip;
        if (string.Equals(clipStem, "running", StringComparison.OrdinalIgnoreCase)) return authoredRunningClip;
        if (string.Equals(clipStem, "punch_combo", StringComparison.OrdinalIgnoreCase)) return authoredPunchClip;
        if (string.Equals(clipStem, "flying", StringComparison.OrdinalIgnoreCase)) return authoredFlyingClip;
        if (string.Equals(clipStem, "squat", StringComparison.OrdinalIgnoreCase)) return authoredSquatClip;
        if (string.Equals(clipStem, "deadlift", StringComparison.OrdinalIgnoreCase)) return authoredDeadliftClip;
        if (string.Equals(clipStem, "gun_drawing", StringComparison.OrdinalIgnoreCase)) return authoredGunDrawingClip;
        if (string.Equals(clipStem, "gun_shooting", StringComparison.OrdinalIgnoreCase)) return authoredGunShootingClip;
        if (string.Equals(clipStem, "idle1", StringComparison.OrdinalIgnoreCase)) return GetVariant(idleVariants, 0);
        if (string.Equals(clipStem, "idle2", StringComparison.OrdinalIgnoreCase)) return GetVariant(idleVariants, 1);
        if (string.Equals(clipStem, "idle3", StringComparison.OrdinalIgnoreCase)) return GetVariant(idleVariants, 2);
        if (string.Equals(clipStem, "celebration1", StringComparison.OrdinalIgnoreCase)) return GetVariant(celebrationVariants, 0);
        if (string.Equals(clipStem, "celebration2", StringComparison.OrdinalIgnoreCase)) return GetVariant(celebrationVariants, 1);
        if (string.Equals(clipStem, "celebration3", StringComparison.OrdinalIgnoreCase)) return GetVariant(celebrationVariants, 2);
        return null;
    }

#if UNITY_EDITOR
    private int CountChangedRestBones()
    {
        int changed = 0;
        foreach (KeyValuePair<Transform, Quaternion> pair in restRotations)
        {
            Transform bone = pair.Key;
            if (bone == null)
            {
                continue;
            }

            bool rotationChanged = Quaternion.Angle(pair.Value, bone.localRotation) > 0.25f;
            bool positionChanged = restPositions.TryGetValue(bone, out Vector3 restPosition) &&
                Vector3.Distance(restPosition, bone.localPosition) > 0.0005f;
            bool scaleChanged = restScales.TryGetValue(bone, out Vector3 restScale) &&
                Vector3.Distance(restScale, bone.localScale) > 0.0005f;
            if (rotationChanged || positionChanged || scaleChanged)
            {
                changed++;
            }
        }
        return changed;
    }
#endif

    private string BuildAuthoredClipNameList()
    {
        List<string> names = new List<string>();
        AnimationClip[] clips = AuthoredClipSet();
        for (int i = 0; i < clips.Length; i++)
        {
            if (clips[i] != null && !names.Contains(clips[i].name))
            {
                names.Add(clips[i].name);
            }
        }
        return names.Count == 0 ? "none" : string.Join(",", names.ToArray());
    }

    private AnimationClip[] AuthoredClipSet()
    {
        List<AnimationClip> clips = new List<AnimationClip>
        {
            authoredWalkingClip,
            authoredRunningClip,
            authoredPunchClip,
            authoredFlyingClip,
            authoredSquatClip
        };
        if (configuredIdentity == BodybuilderIdentity.Policeman)
        {
            clips.Add(authoredGunDrawingClip);
            clips.Add(authoredGunShootingClip);
        }
        clips.AddRange(idleVariants);
        clips.AddRange(celebrationVariants);
        return clips.ToArray();
    }

    private void SelectInitialIdleVariant()
    {
        idleVariantCursor = -1;
        SelectNextIdleVariant();
    }

    private void SelectNextIdleVariant()
    {
        int count = CountAvailable(idleVariants);
        if (count == 0)
        {
            idleTime = 0f;
            return;
        }
        int next = UnityEngine.Random.Range(0, count);
        if (count > 1 && next == idleVariantCursor)
        {
            next = (next + UnityEngine.Random.Range(1, count)) % count;
        }
        idleVariantCursor = next;
        idleClip = GetVariant(idleVariants, idleVariantCursor);
        idleTime = 0f;
    }

    private void SelectCelebrationVariant()
    {
        int count = CountAvailable(celebrationVariants);
        if (count == 0)
        {
            celebrationVariantCursor = -1;
            celebrationClip = null;
            return;
        }
        int next = UnityEngine.Random.Range(0, count);
        if (count > 1 && next == celebrationVariantCursor)
        {
            next = (next + UnityEngine.Random.Range(1, count)) % count;
        }
        celebrationVariantCursor = next;
        celebrationClip = GetVariant(celebrationVariants, celebrationVariantCursor);
    }

    private static int CountAvailable(AnimationClip[] variants)
    {
        return variants == null ? 0 : variants.Length;
    }

    private static AnimationClip GetVariant(AnimationClip[] variants, int index)
    {
        return variants != null && index >= 0 && index < variants.Length
            ? variants[index]
            : null;
    }

    private int StableVariantSeed(BodybuilderIdentity identity)
    {
        unchecked
        {
            int hash = 17;
            string identityName = identity.ToString();
            for (int i = 0; i < identityName.Length; i++)
            {
                hash = hash * 31 + identityName[i];
            }
            return hash;
        }
    }

    private static int PositiveModulo(int value, int modulus)
    {
        if (modulus <= 0)
        {
            return 0;
        }
        int remainder = value % modulus;
        return remainder < 0 ? remainder + modulus : remainder;
    }

    private void EmitAnimationMarker(
        MotionState state, AnimationClip clip, string branch)
    {
        if (hasAnimationMarker &&
            lastAnimationMarkerState == state &&
            ReferenceEquals(lastAnimationMarkerClip, clip) &&
            string.Equals(lastAnimationMarkerBranch, branch, StringComparison.Ordinal))
        {
            return;
        }

        hasAnimationMarker = true;
        lastAnimationMarkerState = state;
        lastAnimationMarkerClip = clip;
        lastAnimationMarkerBranch = branch;
        string marker = $"{state}|{branch}|{ClipName(clip)}";
        lastAnimationMarker = marker;
        Debug.Log(
            $"GYMCHAOS_ENEMY_ANIMATION_STATE identity={configuredIdentity} " +
            $"state={state} branch={branch} clip={ClipName(clip)} " +
            $"speed01={speed01:F2}", this);
    }

    private static string ClipName(AnimationClip clip)
    {
        return clip == null ? "missing" : clip.name;
    }

    private static string GetClipNameForMarker(string marker)
    {
        if (string.IsNullOrEmpty(marker))
        {
            return string.Empty;
        }
        int separator = marker.LastIndexOf('|');
        return separator >= 0 ? marker.Substring(separator + 1) : marker;
    }

    private static string ResourcePath(BodybuilderIdentity identity)
    {
        switch (identity)
        {
            case BodybuilderIdentity.Arnold: return "Characters/Enemies/arnold_authored";
            case BodybuilderIdentity.Cbum: return "Characters/Enemies/cbum_authored";
            case BodybuilderIdentity.Zyzz: return "Characters/Enemies/zyzz_authored";
            case BodybuilderIdentity.Ronnie: return "Characters/Enemies/ronnie_authored";
            case BodybuilderIdentity.JayCutler: return "Characters/Enemies/jaycutler_authored";
            case BodybuilderIdentity.Goku: return "Characters/Enemies/goku_authored";
            case BodybuilderIdentity.Mark: return "Characters/Enemies/mark_authored";
            case BodybuilderIdentity.Davie: return "Characters/Enemies/davie_authored";
            case BodybuilderIdentity.Policeman: return "Characters/Enemies/policeman_authored";
            default: return string.Empty;
        }
    }
}
