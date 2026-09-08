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
    private const float PunchDuration = 0.72f;
    private const float AuthoredTransitionDuration = 0.12f;

    public enum MotionState
    {
        Uninitialized,
        Idle,
        Running,
        Punching,
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
    private float groundingOffsetY;
    private float groundingOffsetVelocity;
    private bool groundingOffsetInitialized;
    private bool moving;
    private bool useRunningClip;
    private bool flying;
    private bool downed;
    private float speed01;
    private float runTime;
    private float idleTime;
    private float flightTime;
    private float attackTime = -1f;
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
    public bool HasAuthoredAnimationSetup => authoredLoadedClipCount == 11;
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
        lastAnimationMarker = null;
        hasAnimationMarker = false;
        lastAnimationMarkerClip = null;
        lastAnimationMarkerBranch = null;

        string resourcePath = ResourcePath(identity);
        authoredWalkingClip = LoadAuthoredClip(resourcePath, "walking");
        authoredRunningClip = LoadAuthoredClip(resourcePath, "running");
        authoredPunchClip = LoadAuthoredClip(resourcePath, "punch_combo");
        authoredFlyingClip = LoadAuthoredClip(resourcePath, "flying");
        authoredSquatClip = LoadAuthoredClip(resourcePath, "squat");
        idleVariants = CompactVariants(
            LoadAuthoredClip(resourcePath, "idle1"),
            LoadAuthoredClip(resourcePath, "idle2"),
            LoadAuthoredClip(resourcePath, "idle3"));
        celebrationVariants = CompactVariants(
            LoadAuthoredClip(resourcePath, "celebration1"),
            LoadAuthoredClip(resourcePath, "celebration2"),
            LoadAuthoredClip(resourcePath, "celebration3"));
        authoredLoadedClipNames = BuildAuthoredClipNameList();
        authoredLoadedClipCount = CountLoadedAuthoredClips();

        walkingClip = authoredWalkingClip;
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
                $"loadedCount={authoredLoadedClipCount}/11 " +
                $"requiredPath=Assets/Resources/{resourcePath}.fbx", this);
            return false;
        }

        CaptureRestPose();
        configured = true;
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
        groundingOffsetY = 0f;
        groundingOffsetVelocity = 0f;
        groundingOffsetInitialized = false;
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

        workoutPosePhase = Mathf.Repeat(normalizedPhase, 1f);
        workoutPosePhaseDriven = true;
    }

    public void ReleaseWorkoutPose()
    {
        workoutPoseLocked = false;
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
        if (attackTime < 0f || punchContactSent || punchClip == null ||
            attackTime / PunchDuration < 0.54f)
        {
            return false;
        }
        punchContactSent = true;
        return true;
    }

    public void SetDowned(bool isDowned)
    {
        downed = isDowned;
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

    private void LateUpdate()
    {
        if (!configured || modelRoot == null)
        {
            return;
        }

        if (workoutPoseLocked)
        {
            lastMotionState = MotionState.Idle;
            PreparePoseTransition(authoredSquatClip, "workout");
            if (authoredSquatClip != null)
            {
                if (workoutPosePhaseDriven)
                {
                    float duration = Mathf.Max(0.01f, authoredSquatClip.length - 0.001f);
                    workoutPoseTime = workoutPosePhase * duration;
                }
                else
                {
                    workoutPoseTime += Time.deltaTime;
                }
                SampleDirectLoop(authoredSquatClip, workoutPoseTime);
                EmitAnimationMarker(MotionState.Idle, authoredSquatClip, "workout");
            }
            else
            {
                PreparePoseTransition(null, "animation-disabled");
                RestoreVisibleRestPose();
                EmitAnimationMarker(MotionState.Idle, null, "animation-disabled");
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
            return;
        }

        lastMotionState = MotionState.Idle;
        PreparePoseTransition(idleClip, "idle");
        idleTime += Time.deltaTime;
        EmitAnimationMarker(MotionState.Idle, idleClip, "idle");
        SampleDirectLoop(idleClip, idleTime);
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
        clip.SampleAnimation(modelRoot.gameObject, Mathf.Max(0f, sampleTime));
        // Blender's Rigify bake writes evaluated deform-bone scale channels
        // even though this retarget is rotation/translation driven. Restore
        // each character's own imported rest scales so those bake artifacts
        // cannot stretch fingers or change the visible body height.
        RestoreAnimatedScales();
        // Root motion belongs to the EnemyFighter/Rigidbody, never to the
        // imported child model. Keep the full authored squat root translation
        // because squat.fbx is one complete standing -> squat -> standing rep;
        // strip root translation from locomotion/attack/idle clips so those
        // clips cannot change the character's world height or floor contact.
        if (!ReferenceEquals(clip, authoredSquatClip) && rig != null &&
            rig.Root != null && restPositions.TryGetValue(
                rig.Root, out Vector3 rootPosition))
        {
            // Horizontal motion belongs to EnemyFighter/Rigidbody. Keep only
            // the authored vertical pelvis curve: it is smooth clip data and
            // keeps the planted foot on the floor without moving the entire
            // FBX model root from animated renderer bounds every frame.
            Vector3 sampledRootPosition = rig.Root.localPosition;
            rig.Root.localPosition = new Vector3(
                rootPosition.x, sampledRootPosition.y, rootPosition.z);
        }
        modelRoot.localPosition = modelBaseLocalPosition;
        modelRoot.localRotation = modelBaseLocalRotation;
        modelRoot.localScale = modelBaseLocalScale;
        GroundSkeletonSmoothly();
        return true;
    }

    private void GroundSkeletonSmoothly()
    {
        if (modelRoot == null || rig == null || rig.Root == null || flying || downed)
        {
            return;
        }
        Renderer[] renderers = modelRoot.GetComponentsInChildren<Renderer>(false);
        if (renderers.Length == 0)
        {
            return;
        }
        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
        {
            bounds.Encapsulate(renderers[i].bounds);
        }
        float targetOffset = Mathf.Clamp(
            transform.position.y - bounds.min.y, -0.45f, 0.45f);
        if (!groundingOffsetInitialized)
        {
            groundingOffsetY = targetOffset;
            groundingOffsetVelocity = 0f;
            groundingOffsetInitialized = true;
        }
        else
        {
            groundingOffsetY = Mathf.SmoothDamp(
                groundingOffsetY, targetOffset, ref groundingOffsetVelocity,
                0.085f, 3.5f, Mathf.Max(Time.deltaTime, 1f / 120f));
        }
        // Move the authored skeleton, never the fitted FBX/model root. The
        // correction therefore follows a damped continuous curve while the
        // Rigidbody and visible model transform remain perfectly stable.
        rig.Root.position += Vector3.up * groundingOffsetY;
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
        poseTransition.Apply(Time.deltaTime);
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
        poseTransition.Apply(Time.deltaTime);
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
            default: return string.Empty;
        }
    }
}
