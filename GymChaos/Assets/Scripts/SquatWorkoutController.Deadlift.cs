using UnityEngine;

/// <summary>
/// Member deadlift set on the freeweights platform. Each rep plays the rig's
/// own authored pull (snatch.fbx retargeted per character) forward at normal
/// speed, runs the lockout in slow motion so it reads as a pause, then plays
/// the pull in reverse to put the bar back on the mats. The loaded bar
/// follows the hands; both hands are solved onto the shaft with the shared
/// overhand grip, and the bar never goes below its settled pose, so the
/// plates cannot sink into the platform.
/// </summary>
public sealed partial class SquatWorkoutController
{
    // The lockout (the last part of the pull) plays slowed down: the pause.
    private const float DeadliftSlowPhase = 0.88f;
    private const float DeadliftSlowMotionSpeed = 0.14f;
    // Set-up time with the bar on the mats before each pull.
    private const float DeadliftFloorSeconds = 0.4f;
    private const float DeadliftPlanStep = 0.0125f;
    private const float DeadliftMaxGroundLift = 0.35f;
    private const float DeadliftMaxStartPhase = 0.6f;
    // Gap kept between the shaft and the legs/torso along the whole pull.
    // Planned from sampled poses; the live IK pose drifts about 1.5 cm from
    // them, so 12 mm still let the shaft touch a shin on some reps.
    private const float DeadliftBodyClearance = 0.02f;
    private const float DeadliftMaxBodyPush = 0.26f;
    private const int DeadliftPushSamples = 24;

    private bool deadliftMode;
    private MixamoScanRetargetAnimator deadliftAnimator;
    private float deadliftStartPhase;
    private float deadliftClipLength;
    private float deadliftPhase;
    private float deadliftRepTime;
    private int deadliftRepsTarget;
    private int deadliftRepsDone;
    private float deadliftGroundLift;
    private bool deadliftStartMeasured;
    private Vector3 deadliftStartMid;
    private bool deadliftMetricsLogged;
    private int deadliftGroundFrames;
    private bool deadliftGroundMeasured;
    private Vector3 deadliftStancePosition;
    private Quaternion deadliftStanceRotation;
    private Mesh deadliftClearanceMesh;
    private readonly float[] deadliftPushTable = new float[DeadliftPushSamples + 1];

    public bool IsDeadliftActive => running && deadliftMode;
    public float DeadliftPhase => deadliftPhase;
    public float DeadliftStartPhase => deadliftStartPhase;
    public int DeadliftRepsDone => deadliftRepsDone;
    public int DeadliftRepsTarget => deadliftRepsTarget;
    public float DeadliftGroundLift => deadliftGroundLift;
    public float DeadliftStanceStepBack { get; private set; }
    public string DeadliftSegment { get; private set; } = string.Empty;
    public Vector3 DeadliftBarTarget { get; private set; }
    // Where the bar sits in each hand while deadlifting (see HookHollowLocal).
    private Vector3 DeadliftHollowLeft => UsesRigidFistGrip || HookGripDisabledForVerification ? leftGripHollowLocal : HookHollowLocal(true, Vector3.down);
    private Vector3 DeadliftHollowRight => UsesRigidFistGrip || HookGripDisabledForVerification ? rightGripHollowLocal : HookHollowLocal(false, Vector3.down);

    public float DeadliftLeftGripError { get; private set; } = float.PositiveInfinity;
    public float DeadliftRightGripError { get; private set; } = float.PositiveInfinity;
    public float DeadliftRepSeconds => DeadliftFloorSeconds + PullSeconds + PauseSeconds + LowerSeconds;

    private float PullSeconds => Mathf.Max(0.05f, (DeadliftSlowPhase - deadliftStartPhase) * deadliftClipLength);
    private float PauseSeconds => (1f - DeadliftSlowPhase) * deadliftClipLength / DeadliftSlowMotionSpeed;
    private float LowerSeconds => Mathf.Max(0.05f, (1f - deadliftStartPhase) * deadliftClipLength);

    private float CurrentBarShaftRadius => station == null ? 0.02f
        : deadliftMode ? station.DeadliftShaftRadius : station.SquatBarShaftRadius;

    public bool BeginDeadlift(GymExerciseStation targetStation, EnemyFighter fighter, int targetRepetitions)
    {
        if (running || targetStation == null || !targetStation.IsDeadlift ||
            fighter == null || fighter.IsDead)
        {
            return false;
        }

        ResolveBones();
        MixamoScanRetargetAnimator animator = fighter.GetComponentInChildren<MixamoScanRetargetAnimator>(true);
        if (!HasValidArmRig || animator == null || !animator.HasAuthoredDeadliftClip)
        {
            if (!warnedMissingBones)
            {
                warnedMissingBones = true;
                Debug.LogWarning(
                    $"Deadlift cannot start for {fighter.Identity}: arm rig or authored deadlift clip missing.",
                    this);
            }
            return false;
        }
        if (!targetStation.TryReserveEnemyDeadlift(fighter))
        {
            return false;
        }

        owner = fighter;
        station = targetStation;
        deadliftMode = true;
        deadliftAnimator = animator;
        fighter.PrepareVisitorWorkoutPose();
        animator.SetDeadliftWorkoutClip(true);
        deadliftClipLength = Mathf.Max(0.1f, animator.AuthoredDeadliftClip.length);
        ResetAuthoredBarAndGrip();

        Vector3 startPosition = fighter.transform.position;
        Quaternion startRotation = fighter.transform.rotation;
        if (!PlanDeadliftStance())
        {
            CancelDeadlift();
            // Planning snapped the body onto the platform; put it back.
            fighter.SetVisitorSpawnPose(startPosition, startRotation);
            return false;
        }

        deadliftRepsTarget = Mathf.Clamp(targetRepetitions, 3, 6);
        deadliftRepsDone = 0;
        deadliftRepTime = 0f;
        deadliftPhase = deadliftStartPhase;
        deadliftStartMeasured = false;
        deadliftMetricsLogged = false;
        deadliftGroundFrames = 0;
        deadliftGroundMeasured = false;
        DeadliftSegment = "floor";
        CurrentMotion = 0f;
        running = true;
        completed = false;
        animator.SetWorkoutPosePhase(deadliftPhase);
        Debug.Log(
            $"GYMCHAOS_DEADLIFT_MEMBER_BEGIN enemy={fighter.Identity} station={station.EquipmentName} " +
            $"reps={deadliftRepsTarget} startPhase={deadliftStartPhase:0.000} " +
            $"groundLift={deadliftGroundLift:0.000} repSeconds={DeadliftRepSeconds:0.00}",
            this);
        return true;
    }

    // Measures this rig on its own clip: where its curled fingers are, at
    // which point of the pull they reach the height of the settled bar (the
    // set starts and ends there, so the arms never stretch down to it), and
    // where the body must stand for the hands to be over the shaft.
    private bool PlanDeadliftStance()
    {
        Quaternion facing = station.EnemyRotation;
        SnapDeadliftStance(owner.transform.position, facing);

        if (!deadliftAnimator.SampleDeadliftPoseNow(0.5f) || !EnsureGripCalibrated())
        {
            return false;
        }

        Debug.Log(
            $"GYMCHAOS_DEADLIFT_MEMBER_GRIP_CALIBRATED enemy={owner.Identity} " +
            $"hollowL={Vector3.Distance(leftHand.position, leftHand.TransformPoint(DeadliftHollowLeft)):0.000} " +
            $"hollowR={Vector3.Distance(rightHand.position, rightHand.TransformPoint(DeadliftHollowRight)):0.000} " +
            $"clearL={LeftGripClearance:0.000} clearR={RightGripClearance:0.000} wrap={wrapFingers} " +
            $"armL={Vector3.Distance(leftUpperArm.position, leftForearm.position) + Vector3.Distance(leftForearm.position, leftHand.position):0.000} " +
            $"armR={Vector3.Distance(rightUpperArm.position, rightForearm.position) + Vector3.Distance(rightForearm.position, rightHand.position):0.000}",
            this);
        // Soles at lockout (feet are baked fixed for the whole clip).
        deadliftAnimator.SampleDeadliftPoseNow(1f);
        if (!TryMeasureRenderedSoleY(out float soleY))
        {
            return false;
        }
        float soleOffset = soleY - owner.transform.position.y;

        Vector3 rest = station.DeadliftBarRestPosition;
        float barHeight = rest.y - station.DeadliftPlatformTopY;
        float start = -1f;
        float previousPhase = 0f;
        float previousHeight = float.NegativeInfinity;
        for (float phase = 0f; phase <= 1.0001f; phase += DeadliftPlanStep)
        {
            deadliftAnimator.SampleDeadliftPoseNow(phase);
            float height = GetGripMid().y - soleY;
            if (height >= barHeight)
            {
                start = previousHeight > float.NegativeInfinity && height > previousHeight
                    ? Mathf.Lerp(previousPhase, phase, Mathf.InverseLerp(previousHeight, height, barHeight))
                    : phase;
                break;
            }
            previousPhase = phase;
            previousHeight = height;
        }
        if (start < 0f)
        {
            Debug.LogWarning(
                $"GYMCHAOS_DEADLIFT_MEMBER_PLAN_FAILED enemy={owner.Identity} barHeight={barHeight:0.000} " +
                $"lockoutGripHeight={previousHeight:0.000}", this);
            return false;
        }
        deadliftStartPhase = Mathf.Min(start, DeadliftMaxStartPhase);

        // Stand where the grip at the start of the pull is over the shaft.
        deadliftAnimator.SampleDeadliftPoseNow(deadliftStartPhase);
        Vector3 offset = rest - GetGripMid();
        offset.y = 0f;
        SnapDeadliftStance(owner.transform.position + offset, facing);

        // The fighter root stays on the gym floor (the platform colliders are
        // ignored for members); lift the visible skeleton onto the mats.
        float soleLift = station.DeadliftPlatformTopY - (owner.transform.position.y + soleOffset);
        deadliftGroundLift = Mathf.Clamp(soleLift, -0.05f, DeadliftMaxGroundLift);

        // On this rig's own hand path the shaft can cut through the shins and
        // thighs. Step back just far enough that the settled bar clears them,
        // and carry the bar that much further forward wherever the pull still
        // needs it; the arms reach forward to keep the grip.
        // The first floor frames settle the soles exactly on the mats (up to
        // 0.2 m past the clamped lift); measure the legs where they end up.
        PlanDeadliftBodyClearance(Mathf.Clamp(soleLift, -0.25f, DeadliftMaxGroundLift + 0.2f));
        DeadliftStanceStepBack = deadliftPushTable[0];
        Vector3 forward = station.DeadliftEnemyFacing;
        SnapDeadliftStance(owner.transform.position - forward * DeadliftStanceStepBack, facing);

        deadliftStancePosition = owner.transform.position;
        deadliftStanceRotation = facing;
        return true;
    }

    private void PlanDeadliftBodyClearance(float lift)
    {
        Vector3 rest = station.DeadliftBarRestPosition;
        Vector3 forward = station.DeadliftEnemyFacing;
        deadliftAnimator.SampleDeadliftPoseNow(deadliftStartPhase);
        Vector3 startMid = GetGripMid();
        float[] raw = new float[DeadliftPushSamples + 1];
        string worstBlocker = "none";
        float worst = 0f;
        for (int i = 0; i <= DeadliftPushSamples; i++)
        {
            float phase = Mathf.Lerp(deadliftStartPhase, 1f, i / (float)DeadliftPushSamples);
            deadliftAnimator.SampleDeadliftPoseNow(phase);
            Vector3 mid = GetGripMid();
            Vector3 bar = rest +
                forward * Vector3.Dot(mid - startMid, forward) +
                Vector3.up * Mathf.Max(0f, mid.y - startMid.y);
            raw[i] = Mathf.Min(DeadliftMaxBodyPush,
                MeasureDeadliftBodyPush(bar, lift, out _, out string blocker));
            if (raw[i] > worst)
            {
                worst = raw[i];
                worstBlocker = blocker;
            }
        }
        // Between samples the body moves on: keep the larger neighbour.
        for (int i = 0; i <= DeadliftPushSamples; i++)
        {
            deadliftPushTable[i] = Mathf.Max(raw[i],
                Mathf.Max(raw[Mathf.Max(0, i - 1)], raw[Mathf.Min(DeadliftPushSamples, i + 1)]));
        }
        Debug.Log(
            $"GYMCHAOS_DEADLIFT_MEMBER_CLEARANCE enemy={owner.Identity} stepBack={deadliftPushTable[0]:0.000} " +
            $"maxPush={worst:0.000} blocker={worstBlocker}", this);
    }

    // Extra forward reach of the bar at this phase, relative to the start.
    private float GetDeadliftBarPush(float phase)
    {
        float u = Mathf.InverseLerp(deadliftStartPhase, 1f, phase) * DeadliftPushSamples;
        int i = Mathf.Clamp(Mathf.FloorToInt(u), 0, DeadliftPushSamples - 1);
        float push = Mathf.Lerp(deadliftPushTable[i], deadliftPushTable[i + 1], u - i);
        return push - deadliftPushTable[0];
    }

    // The visitor snap writes the Rigidbody; the transform only follows on
    // the next physics step. The plan samples the rig right now, so move
    // the transform too.
    private void SnapDeadliftStance(Vector3 position, Quaternion rotation)
    {
        owner.SetVisitorSpawnPose(position, rotation, true);
        Rigidbody body = owner.GetComponent<Rigidbody>();
        owner.transform.SetPositionAndRotation(body != null ? body.position : position, rotation);
        Physics.SyncTransforms();
        owner.GetComponent<ExternalRiggedCharacterVisual>()?.RegroundAfterRootSnap();
    }

    // Nothing may turn or push the lifter off the planned stance mid-set.
    private void HoldDeadliftStance()
    {
        Rigidbody body = owner.GetComponent<Rigidbody>();
        Vector3 position = body != null ? body.position : owner.transform.position;
        Quaternion rotation = body != null ? body.rotation : owner.transform.rotation;
        if ((position - deadliftStancePosition).sqrMagnitude < 0.0001f &&
            Quaternion.Angle(rotation, deadliftStanceRotation) < 0.5f)
        {
            return;
        }
        if (body != null)
        {
            body.position = deadliftStancePosition;
            body.rotation = deadliftStanceRotation;
            if (!body.isKinematic)
            {
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }
        }
        owner.transform.SetPositionAndRotation(deadliftStancePosition, deadliftStanceRotation);
    }

    private Vector3 GetGripMid()
    {
        return (leftHand.TransformPoint(DeadliftHollowLeft) +
            rightHand.TransformPoint(DeadliftHollowRight)) * 0.5f;
    }

    private void UpdateDeadlift()
    {
        if (owner == null || owner.IsDead || station == null || station.EnemyOccupant != owner)
        {
            Cancel();
            return;
        }

        HoldDeadliftStance();
        deadliftRepTime += Time.deltaTime;
        float floorEnd = DeadliftFloorSeconds;
        float pullEnd = floorEnd + PullSeconds;
        float pauseEnd = pullEnd + PauseSeconds;
        float repEnd = pauseEnd + LowerSeconds;
        if (deadliftRepTime >= repEnd)
        {
            deadliftRepsDone++;
            deadliftRepTime -= repEnd;
            Debug.Log(
                $"GYMCHAOS_DEADLIFT_MEMBER_REP enemy={owner.Identity} rep={deadliftRepsDone}/{deadliftRepsTarget}",
                this);
            if (deadliftRepsDone >= deadliftRepsTarget)
            {
                FinishDeadlift();
                return;
            }
        }

        float t = deadliftRepTime;
        if (t < floorEnd)
        {
            DeadliftSegment = "floor";
            deadliftPhase = deadliftStartPhase;
        }
        else if (t < pullEnd)
        {
            DeadliftSegment = "pull";
            deadliftPhase = Mathf.Lerp(deadliftStartPhase, DeadliftSlowPhase, (t - floorEnd) / PullSeconds);
        }
        else if (t < pauseEnd)
        {
            DeadliftSegment = "pause";
            deadliftPhase = Mathf.Lerp(DeadliftSlowPhase, 1f, (t - pullEnd) / PauseSeconds);
        }
        else
        {
            DeadliftSegment = "lower";
            deadliftPhase = Mathf.Lerp(1f, deadliftStartPhase, (t - pauseEnd) / LowerSeconds);
        }
        CurrentMotion = Mathf.InverseLerp(deadliftStartPhase, 1f, deadliftPhase);
        deadliftAnimator.SetWorkoutPosePhase(deadliftPhase);
    }

    // Runs after the animator sampled this frame's deadlift pose.
    private void LateUpdateDeadlift()
    {
        if (owner == null || station == null || deadliftAnimator == null)
        {
            return;
        }

        deadliftAnimator.SetWorkoutPosePhase(deadliftPhase);
        deadliftAnimator.ApplyGroundedRootCorrection(Vector3.up, deadliftGroundLift);
        if (!deadliftGroundMeasured && ++deadliftGroundFrames >= AuthoredGroundMeasureFrame &&
            DeadliftSegment == "floor")
        {
            // The planned lift assumed the root on the gym floor; measure the
            // rendered soles once the clip drives the skin and settle them
            // exactly on the mats.
            deadliftGroundMeasured = true;
            if (TryMeasureRenderedSoleY(out float soleY))
            {
                float correction = Mathf.Clamp(station.DeadliftPlatformTopY - soleY, -0.2f, 0.2f);
                deadliftGroundLift += correction;
                deadliftAnimator.ApplyGroundedRootCorrection(Vector3.up, correction);
                Debug.Log(
                    $"GYMCHAOS_DEADLIFT_MEMBER_GROUND enemy={owner.Identity} correction={correction:0.000} " +
                    $"lift={deadliftGroundLift:0.000}", this);
            }
        }
        Vector3 mid = GetGripMid();
        if (!deadliftStartMeasured || DeadliftSegment == "floor")
        {
            // The bar lies on the mats while the lifter sets up: anchor the
            // hand path to this pose every rep.
            deadliftStartMid = mid;
            deadliftStartMeasured = true;
        }

        // The bar rides the authored hand path: forward/back with the hands,
        // up as far as they rise above the start. At the start and the end of
        // every rep this is exactly the settled pose on the mats.
        Vector3 facing = station.DeadliftEnemyFacing;
        Vector3 bar = station.DeadliftBarRestPosition +
            facing * (Vector3.Dot(mid - deadliftStartMid, facing) + GetDeadliftBarPush(deadliftPhase)) +
            Vector3.up * Mathf.Max(0f, mid.y - deadliftStartMid.y);
        station.SyncEnemyDeadliftBar(owner, bar);
        bar = station.DeadliftBar != null ? station.DeadliftBar.position : bar;
        DeadliftBarTarget = bar;

        Vector3 axis = GetBarAxis();
        SolveDeadliftGrip(bar);
        // Near lockout straight arms can fall a few centimetres short of the
        // hand path; then the bar rides where the shorter arm actually
        // reaches (never below its settled pose) and the grip is solved once
        // more. The other hand reached further, so it still makes it.
        Vector3 leftResidual = leftHand.TransformPoint(DeadliftHollowLeft) - bar;
        Vector3 rightResidual = rightHand.TransformPoint(DeadliftHollowRight) - bar;
        leftResidual -= axis * Vector3.Dot(leftResidual, axis);
        rightResidual -= axis * Vector3.Dot(rightResidual, axis);
        bool leftShort = leftResidual.sqrMagnitude >= rightResidual.sqrMagnitude;
        Vector3 residual = leftShort ? leftResidual : rightResidual;
        if (residual.sqrMagnitude > 0.005f * 0.005f)
        {
            // Pulling the bar back toward the body would put it into the
            // shins or thighs again; ride it up by the same reach instead.
            if (Vector3.Dot(residual, facing) < 0f)
            {
                Vector3 shoulder = leftShort ? leftUpperArm.position : rightUpperArm.position;
                Vector3 toBar = bar - shoulder;
                toBar -= axis * Vector3.Dot(toBar, axis);
                toBar = toBar.sqrMagnitude > 1e-6f ? toBar.normalized : Vector3.down;
                float shortfall = Mathf.Max(0f, -Vector3.Dot(residual, toBar));
                residual = Vector3.up * (shortfall / Mathf.Max(0.5f, -toBar.y));
            }
            station.SyncEnemyDeadliftBar(owner, bar + residual);
            bar = station.DeadliftBar != null ? station.DeadliftBar.position : bar + residual;
            DeadliftBarTarget = bar;
            SolveDeadliftGrip(bar);
        }

        DeadliftLeftGripError = DistanceToLine(leftHand.TransformPoint(DeadliftHollowLeft), bar, axis);
        DeadliftRightGripError = DistanceToLine(rightHand.TransformPoint(DeadliftHollowRight), bar, axis);

        if (!deadliftMetricsLogged && DeadliftSegment == "pause")
        {
            deadliftMetricsLogged = true;
            Debug.Log(
                $"GYMCHAOS_DEADLIFT_MEMBER_METRICS enemy={owner.Identity} " +
                $"barRise={bar.y - station.DeadliftBarRestPosition.y:0.000} " +
                $"gripL={DeadliftLeftGripError:0.000} gripR={DeadliftRightGripError:0.000} " +
                $"halfWidth={gripHalfWidth:0.000} wrap={wrapFingers} rigidFist={UsesRigidFistGrip} " +
                $"ownerFwd={owner.transform.forward} facing={station.DeadliftEnemyFacing} pos={owner.transform.position} bar={bar} axis={axis} shL={leftUpperArm.position} shR={rightUpperArm.position} " +
                $"wristL={leftHand.position} wristR={rightHand.position} " +
                $"hollowL={leftHand.TransformPoint(DeadliftHollowLeft)} hollowR={rightHand.TransformPoint(DeadliftHollowRight)} " +
                $"scaleL={leftHand.lossyScale} chainL={leftForearm.IsChildOf(leftUpperArm)}/{leftHand.IsChildOf(leftForearm)}",
                this);
        }
    }

    private void SolveDeadliftGrip(Vector3 bar)
    {
        Vector3 shoulders = (leftUpperArm.position + rightUpperArm.position) * 0.5f;
        Vector3 handDown = bar - shoulders;
        ApplyAuthoredOverhandGrip(bar, handDown.sqrMagnitude > 1e-6f ? handDown.normalized : Vector3.down);
    }

    /// <summary>
    /// Deepest the bar shaft currently cuts into the lifter's legs or torso
    /// between the hands (0 when it clears them).
    /// </summary>
    // Hook grip check: how much of the hand skin the shaft cuts into, and how
    // far the fingertips are from lying on the shaft (curled round it, not
    // straight). Goku's rigid fist is exempt.
    public string DeadliftHandBlocker { get; private set; } = "none";
    public float DeadliftHandDeepDepth { get; private set; }
    // Smallest proximal-to-distal phalanx angle over the eight fingers.
    public float DeadliftMinFingerCurl { get; private set; } = 180f;
    public string DeadliftStraightestFinger { get; private set; } = "none";

    public float MeasureDeadliftHandPenetration(out float fingertipGap)
    {
        fingertipGap = 0f;
        if (!IsDeadliftActive || station.DeadliftBar == null || UsesRigidFistGrip)
        {
            return 0f;
        }
        Vector3 bar = station.DeadliftBar.position;
        Vector3 axis = station.DeadliftBarAxis;
        float shaft = station.DeadliftShaftRadius;
        float depth = 0f;
        float minCurl = 180f;
        int handSamples = 0;
        int handInside = 0;
        deadliftClearanceMesh ??= new Mesh { name = "Deadlift body clearance" };
        foreach (SkinnedMeshRenderer skin in owner.GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            if (skin == null || !skin.enabled || skin.sharedMesh == null)
            {
                continue;
            }
            Vector3[] vertices = GymSkinSampler.BakeWorld(skin, deadliftClearanceMesh);
            int[] dominant = GymSkinSampler.GetDominantBones(skin.sharedMesh);
            Transform[] bones = skin.bones;
            for (int i = 0; i < vertices.Length && i < dominant.Length; i++)
            {
                int bone = dominant[i];
                Transform t = bone >= 0 && bone < bones.Length ? bones[bone] : null;
                // The palm (hand and metacarpal bones), not the fingers: a
                // bar through the palm is the visible fault; curled fingers
                // only graze the shaft inside the closed fist.
                if (t == null || (t != leftHand && t != rightHand &&
                    t.name.IndexOf("palm", System.StringComparison.OrdinalIgnoreCase) < 0) ||
                    (!GymSkinSampler.IsInChain(t, leftHand) && !GymSkinSampler.IsInChain(t, rightHand)))
                {
                    continue;
                }
                Vector3 offset = vertices[i] - bar;
                float distance = (offset - axis * Vector3.Dot(offset, axis)).magnitude;
                handSamples++;
                if (shaft - distance > 0.006f)
                {
                    handInside++;
                }
                if (shaft - distance > depth)
                {
                    depth = shaft - distance;
                    DeadliftHandBlocker = t != null ? t.name : "none";
                }
            }
        }
        Transform[] tips =
        {
            leftIndexTip, leftMiddleTip, leftRingTip, leftPinkyTip,
            rightIndexTip, rightMiddleTip, rightRingTip, rightPinkyTip
        };
        for (int i = 0; i < tips.Length; i++)
        {
            Transform tip = tips[i];
            if (tip == null || tip.parent == null)
            {
                continue;
            }
            Vector3 end = tip.position + (tip.position - tip.parent.position) * 0.8f;
            Vector3 offset = end - bar;
            float distance = (offset - axis * Vector3.Dot(offset, axis)).magnitude;
            fingertipGap = Mathf.Max(fingertipGap, distance - (shaft + HookFingerHalfThickness));
            if (tip.parent.parent != null)
            {
                // Curl: proximal phalanx against the distal one.
                float curl = Vector3.Angle(tip.parent.position - tip.parent.parent.position, end - tip.position);
                if (curl < minCurl)
                {
                    minCurl = curl;
                    DeadliftStraightestFinger = tip.name;
                }
            }
        }
        DeadliftMinFingerCurl = minCurl;
        DeadliftHandDeepDepth = depth;
        // Share of the hand skin more than 6 mm inside the shaft.
        return handSamples > 0 ? (float)handInside / handSamples : 0f;
    }

    public float MeasureDeadliftBodyPenetration(out string blocker)
    {
        blocker = "none";
        if (!IsDeadliftActive || station.DeadliftBar == null)
        {
            return 0f;
        }
        MeasureDeadliftBodyPush(station.DeadliftBar.position, 0f, out float penetration, out blocker);
        return penetration;
    }

    // Against the bar at `bar` (skin raised by `lift`): how far forward the
    // bar must move to clear every leg and torso vertex between the hands by
    // the clearance, and how deep the shaft cuts into them right now. Arms
    // reach the bar from outside and are skipped.
    private float MeasureDeadliftBodyPush(Vector3 bar, float lift, out float penetration, out string blocker)
    {
        penetration = 0f;
        blocker = "none";
        Vector3 axis = station.DeadliftBarAxis;
        Vector3 forward = station.DeadliftEnemyFacing;
        Vector3 across = Vector3.Cross(axis, forward).normalized;
        float shaft = station.DeadliftShaftRadius;
        float radius = shaft + DeadliftBodyClearance;
        float span = Mathf.Abs(Vector3.Dot(leftUpperArm.position - rightUpperArm.position, axis)) * 0.5f;
        float push = 0f;
        deadliftClearanceMesh ??= new Mesh { name = "Deadlift body clearance" };
        foreach (SkinnedMeshRenderer skin in owner.GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            if (skin == null || !skin.enabled || skin.sharedMesh == null)
            {
                continue;
            }
            Vector3[] vertices = GymSkinSampler.BakeWorld(skin, deadliftClearanceMesh);
            int[] dominant = GymSkinSampler.GetDominantBones(skin.sharedMesh);
            Transform[] bones = skin.bones;
            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 offset = vertices[i] + Vector3.up * lift - bar;
                float along = Vector3.Dot(offset, axis);
                if (Mathf.Abs(along) > span)
                {
                    continue;
                }
                float h = Vector3.Dot(offset, across);
                if (Mathf.Abs(h) >= radius)
                {
                    continue;
                }
                int bone = i < dominant.Length ? dominant[i] : -1;
                Transform boneTransform = bone >= 0 && bone < bones.Length ? bones[bone] : null;
                if (GymSkinSampler.IsInChain(boneTransform, leftUpperArm) ||
                    GymSkinSampler.IsInChain(boneTransform, rightUpperArm))
                {
                    continue;
                }
                float f = Vector3.Dot(offset, forward);
                float need = f + Mathf.Sqrt(radius * radius - h * h);
                if (need > push)
                {
                    push = need;
                    blocker = boneTransform != null ? boneTransform.name : skin.name;
                }
                float depth = shaft - Mathf.Sqrt(f * f + h * h);
                penetration = Mathf.Max(penetration, depth);
            }
        }
        return push;
    }

    private static float DistanceToLine(Vector3 point, Vector3 origin, Vector3 axis)
    {
        Vector3 offset = point - origin;
        return (offset - axis * Vector3.Dot(offset, axis)).magnitude;
    }

    private void FinishDeadlift()
    {
        string stationName = station.EquipmentName;
        EnemyFighter lifter = owner;
        int reps = deadliftRepsDone;
        EndDeadliftState();
        completed = true;
        Debug.Log(
            $"GYMCHAOS_DEADLIFT_MEMBER_COMPLETE enemy={lifter.Identity} station={stationName} reps={reps}",
            this);
    }

    private void CancelDeadlift()
    {
        EndDeadliftState();
        completed = false;
    }

    private void EndDeadliftState()
    {
        if (station != null && owner != null)
        {
            station.EndEnemyDeadlift(owner);
        }
        owner?.ReleaseVisitorWorkoutPose();
        running = false;
        deadliftMode = false;
        deadliftAnimator = null;
        owner = null;
        station = null;
        CurrentMotion = 0f;
        DeadliftSegment = string.Empty;
        ResetAuthoredBarAndGrip();
    }
}
