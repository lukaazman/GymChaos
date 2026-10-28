using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Coordinates the gym squat rep with each visitor's authored squat clip.
/// The imported per-character animation remains authoritative for the
/// character pose; this component only follows the station bar and exposes
/// the gameplay-facing squat measurements.
/// </summary>
[DefaultExecutionOrder(1100)]
public sealed partial class SquatWorkoutController : MonoBehaviour
{
    private EnemyFighter owner;
    private GymExerciseStation station;
    private Transform hips;
    private Transform spine;
    private Transform chest;
    private Transform neck;
    private Transform leftShoulder;
    private Transform leftUpperArm;
    private Transform leftForearm;
    private Transform leftHand;
    private Transform leftIndexTip;
    private Transform leftMiddleTip;
    private Transform leftRingTip;
    private Transform leftPinkyTip;
    private Transform rightShoulder;
    private Transform rightUpperArm;
    private Transform rightForearm;
    private Transform rightHand;
    private Transform rightIndexTip;
    private Transform rightMiddleTip;
    private Transform rightRingTip;
    private Transform rightPinkyTip;
    private Transform leftThigh;
    private Transform leftShin;
    private Transform leftFoot;
    private Transform leftToe;
    private Transform rightThigh;
    private Transform rightShin;
    private Transform rightFoot;
    private Transform rightToe;
    private Quaternion baseHipsLocalRotation;
    private Vector3 baseHipsLocalPosition;
    private Vector3 baseHipsWorldPosition;
    private Quaternion baseHipsWorldRotation;
    private Quaternion baseSpineWorldRotation;
    private Quaternion baseChestWorldRotation;
    private Quaternion baseSpineLocalRotation;
    private Quaternion baseChestLocalRotation;
    private Quaternion baseLeftShoulderLocalRotation;
    private Quaternion baseLeftUpperArmLocalRotation;
    private Quaternion baseLeftForearmLocalRotation;
    private Quaternion baseLeftHandLocalRotation;
    private Quaternion baseRightShoulderLocalRotation;
    private Quaternion baseRightUpperArmLocalRotation;
    private Quaternion baseRightForearmLocalRotation;
    private Quaternion baseRightHandLocalRotation;
    private Vector3 baseLeftHipPosition;
    private Vector3 baseRightHipPosition;
    private Vector3 baseLeftThighLocalPosition;
    private Vector3 baseRightThighLocalPosition;
    private Vector3 squatLeftThighOffsetFromHips;
    private Vector3 squatRightThighOffsetFromHips;
    private Vector3 baseLeftKneePosition;
    private Vector3 baseRightKneePosition;
    private Vector3 baseLeftFootPosition;
    private Vector3 baseRightFootPosition;
    private Vector3 baseLeftFootLocalPosition;
    private Vector3 baseRightFootLocalPosition;
    private float leftFootRootOffsetY;
    private float rightFootRootOffsetY;
    private float leftFootBoneToSoleOffset;
    private float rightFootBoneToSoleOffset;
    private float leftFootMeshSoleOffset;
    private float rightFootMeshSoleOffset;
    private Vector3 baseLeftSoleAnchorLocal;
    private Vector3 baseRightSoleAnchorLocal;
    private Vector3 currentLeftSoleOffsetWorld;
    private Vector3 currentRightSoleOffsetWorld;
    private bool hasMeshFootSoleCalibration;
    private Vector3 squatKneePole;
    private Vector3 squatLeftFootTarget;
    private Vector3 squatRightFootTarget;
    private Vector3 squatLeftSoleTarget;
    private Vector3 squatRightSoleTarget;
    private Vector3 baseSquatLeftSoleTarget;
    private Vector3 baseSquatRightSoleTarget;
    private Vector3 baseFighterPosition;
    private bool authoredSquatTargetsCaptured;
    private float leftLegSideSign = -1f;
    private float rightLegSideSign = 1f;
    private float leftArmSideSign = -1f;
    private float rightArmSideSign = 1f;
    private Vector3 baseLeftArmAnchorPosition;
    private Vector3 baseRightArmAnchorPosition;
    private Vector3 baseLeftElbowPosition;
    private Vector3 baseRightElbowPosition;
    private Vector3 baseLeftHandPosition;
    private Vector3 baseRightHandPosition;
    private Quaternion baseLeftThighRotation;
    private Quaternion baseLeftThighLocalRotation;
    private Quaternion baseRightThighRotation;
    private Quaternion baseRightThighLocalRotation;
    private Quaternion baseLeftShinRotation;
    private Quaternion baseLeftShinLocalRotation;
    private Quaternion baseRightShinRotation;
    private Quaternion baseRightShinLocalRotation;
    private Quaternion baseLeftFootRotation;
    private Quaternion baseLeftFootLocalRotation;
    private Quaternion baseLeftToeLocalRotation;
    private Quaternion baseRightFootRotation;
    private Quaternion baseRightFootLocalRotation;
    private Quaternion baseRightToeLocalRotation;
    private float leftUpperLegLength;
    private float rightUpperLegLength;
    private float leftLowerLegLength;
    private float rightLowerLegLength;
    private float leftUpperArmLength;
    private float rightUpperArmLength;
    private float leftLowerArmLength;
    private float rightLowerArmLength;
    private Quaternion baseLeftHandRelativeToForearm;
    private Quaternion baseRightHandRelativeToForearm;
    private Vector3 baseLeftHandContactOffsetLocal;
    private Vector3 baseRightHandContactOffsetLocal;
    private float leftHandContactReach;
    private float rightHandContactReach;
    private float armSpanReference;
    private bool basePoseCaptured;
    private float elapsed;
    private float repDuration;
    private int repetitions;
    private bool running;
    private bool completed;
    private bool initialPoseHoldPending;
    private bool warnedMissingBones;
    private float maxHipDrop;
    private float currentHipDrop;
    private float currentKneeBend;
    private float currentKneeBendDifference;
    private float currentLegDepthDifference;
    private float currentLeftFootSoleError = float.PositiveInfinity;
    private float currentRightFootSoleError = float.PositiveInfinity;
    private float currentLeftFootGroundError = float.PositiveInfinity;
    private float currentRightFootGroundError = float.PositiveInfinity;
    private float currentLeftFixedFootGroundError = float.PositiveInfinity;
    private float currentRightFixedFootGroundError = float.PositiveInfinity;
    private float currentLeftFootRotationError = float.PositiveInfinity;
    private float currentRightFootRotationError = float.PositiveInfinity;
    private Vector3 barTargetPosition;
    private Vector3 initialAttachedBarCenter;
    private float currentBarBodyFollowError = float.PositiveInfinity;
    private float currentBarDropFromStart;
    private bool poseMetricLogged;
    private bool workoutGroundingApplied;
    private bool footSoleCalibrationPrepared;
    private bool hasCachedMeshFootSoleCalibration;
    private float cachedLeftFootMeshSoleOffset;
    private float cachedRightFootMeshSoleOffset;

    public bool IsActive => running;
    public bool IsComplete => completed;
    public Transform Traps => chest != null ? chest : hips;
    internal Transform LeftHandBoneForVerification => leftHand;
    internal Transform RightHandBoneForVerification => rightHand;
    internal Transform NeckBoneForVerification => neck;
    public float CurrentMotion { get; private set; }
    public float CurrentHipDrop => currentHipDrop;
    public float CurrentKneeBend => currentKneeBend;
    public float FootSoleError => Mathf.Max(
        currentLeftFootSoleError, currentRightFootSoleError);
    public float FootGroundError => Mathf.Max(
        currentLeftFootGroundError, currentRightFootGroundError);
    public float FootRotationError => Mathf.Max(
        currentLeftFootRotationError, currentRightFootRotationError);
    public float BarBodyFollowError => currentBarBodyFollowError;
    public float BarDropFromStart => currentBarDropFromStart;
    public Vector3 BarTargetPosition => station != null && station.IsEnemySquatBarAttached
        ? station.EnemySquatBarCenter
        : owner != null ? CalculateBarTargetPosition(owner) : barTargetPosition;
    public bool HasValidSquatRig
    {
        get
        {
            EnsureBonesResolved();
            return hips != null && spine != null && chest != null &&
                HasValidLegChain(leftThigh, leftShin, leftFoot) &&
                HasValidLegChain(rightThigh, rightShin, rightFoot);
        }
    }
    public bool HasValidArmRig
    {
        get
        {
            EnsureBonesResolved();
            return leftShoulder != null && leftUpperArm != null &&
                leftForearm != null && leftHand != null && rightShoulder != null &&
                rightUpperArm != null && rightForearm != null && rightHand != null;
        }
    }
    public bool HasFingerContactRig
    {
        get
        {
            EnsureBonesResolved();
            return leftHand != null && rightHand != null &&
                leftIndexTip != null && leftMiddleTip != null &&
                leftRingTip != null && leftPinkyTip != null &&
                rightIndexTip != null && rightMiddleTip != null &&
                rightRingTip != null && rightPinkyTip != null;
        }
    }
    public float FootPlantError
    {
        get
        {
            if (!basePoseCaptured || leftFoot == null || rightFoot == null)
            {
                return float.PositiveInfinity;
            }

            return Mathf.Max(
                Vector3.Distance(GetSoleAnchorWorld(leftFoot, baseLeftSoleAnchorLocal), squatLeftSoleTarget),
                Vector3.Distance(GetSoleAnchorWorld(rightFoot, baseRightSoleAnchorLocal), squatRightSoleTarget));
        }
    }

    public bool Begin(
        GymExerciseStation targetStation,
        EnemyFighter fighter,
        int targetRepetitions,
        float durationPerRep)
    {
        if (running || targetStation == null || fighter == null || fighter.IsDead)
        {
            return false;
        }

        ResolveBones();
        if (!HasValidSquatRig || !HasValidArmRig || Traps == null)
        {
            if (!warnedMissingBones)
            {
                warnedMissingBones = true;
                Debug.LogWarning(
                    $"Squat workout cannot start for {fighter.Identity}: full squat rig " +
                    "(legs, traps and both arm chains) is missing.",
                    this);
            }
            return false;
        }

        // GymVisitorAgent normally locks this pose during the physics-side
        // arrival handoff. Keep this fallback for direct callers, but the
        // locked-state guard makes it a no-op during the normal entry frame.
        fighter.PrepareVisitorWorkoutPose();
        // A direct verifier or a vehicle handoff can move the Rigidbody after
        // the imported child was initially fitted. Reground that child once
        // before capturing any world-space foot anchors.
        Physics.SyncTransforms();
        fighter.GetComponent<ExternalRiggedCharacterVisual>()?.RegroundAfterRootSnap();
        Physics.SyncTransforms();
        CaptureBasePose(fighter);
        ResetAuthoredBarAndGrip();
        barTargetPosition = CalculateBarTargetPosition(fighter);
        if (!targetStation.TryBeginEnemySquat(fighter, Traps, barTargetPosition))
        {
            RestoreBasePose();
            fighter.ReleaseVisitorWorkoutPose();
            return false;
        }

        owner = fighter;
        station = targetStation;
        initialAttachedBarCenter = targetStation.EnemySquatBarCenter;
        currentBarBodyFollowError = 0f;
        currentBarDropFromStart = 0f;
        elapsed = 0f;
        repDuration = Mathf.Clamp(durationPerRep, 0.55f, 2.2f);
        repetitions = Mathf.Clamp(targetRepetitions, 6, 12);
        running = true;
        completed = false;
        CurrentMotion = 0f;
        initialPoseHoldPending = true;
        poseMetricLogged = false;
        workoutGroundingApplied = false;
        currentLeftSoleOffsetWorld = Vector3.zero;
        currentRightSoleOffsetWorld = Vector3.zero;
        authoredSquatTargetsCaptured = false;
        authoredGroundCorrectionMeasured = false;
        authoredGroundMeasureFrames = 0;
        authoredGroundCorrection = 0f;
        return true;
    }

    public void Cancel()
    {
        if (deadliftMode)
        {
            CancelDeadlift();
            return;
        }
        if (station != null && owner != null)
        {
            station.EndEnemySquat(owner);
        }

        RestoreBasePose();
        owner?.ReleaseVisitorWorkoutPose();
        running = false;
        completed = false;
        initialPoseHoldPending = false;
        owner = null;
        station = null;
        elapsed = 0f;
        CurrentMotion = 0f;
        currentHipDrop = 0f;
        currentKneeBend = 0f;
        currentKneeBendDifference = 0f;
        currentLegDepthDifference = 0f;
        currentLeftFootSoleError = float.PositiveInfinity;
        currentRightFootSoleError = float.PositiveInfinity;
        currentLeftFootGroundError = float.PositiveInfinity;
        currentRightFootGroundError = float.PositiveInfinity;
        currentLeftFixedFootGroundError = float.PositiveInfinity;
        currentRightFixedFootGroundError = float.PositiveInfinity;
        barTargetPosition = Vector3.zero;
        initialAttachedBarCenter = Vector3.zero;
        currentBarBodyFollowError = float.PositiveInfinity;
        currentBarDropFromStart = 0f;
        poseMetricLogged = false;
        workoutGroundingApplied = false;
        currentLeftSoleOffsetWorld = Vector3.zero;
        currentRightSoleOffsetWorld = Vector3.zero;
        authoredSquatTargetsCaptured = false;
        ResetAuthoredBarAndGrip();
    }

    public void ConsumeCompletion()
    {
        completed = false;
    }

    private void Awake()
    {
        ResolveBones();
        PrepareFootSoleCalibration();
    }

    private void Update()
    {
        if (!running)
        {
            return;
        }
        if (deadliftMode)
        {
            UpdateDeadlift();
            return;
        }

        if (owner == null || owner.IsDead || station == null)
        {
            Cancel();
            return;
        }

        if (initialPoseHoldPending)
        {
            // Begin() can run from FixedUpdate before this component's
            // Update. Keep the first render frame at the exact authored
            // zero-motion pose instead of advancing the rep immediately.
            initialPoseHoldPending = false;
            CurrentMotion = 0f;
            SetAuthoredWorkoutPhase(0f);
            station.TickEnemySquat(owner, 0f);
            return;
        }

        elapsed += Time.deltaTime;
        float totalDuration = repDuration * repetitions;
        float normalized = Mathf.Clamp01(elapsed / Mathf.Max(0.01f, totalDuration));
        float cycle = Mathf.Repeat(elapsed, repDuration) / repDuration;
        float motion = Mathf.Sin(cycle * Mathf.PI);
        CurrentMotion = motion;
        SetAuthoredWorkoutPhase(cycle);
        if (!station.TickEnemySquat(owner, motion))
        {
            Cancel();
            return;
        }

        if (normalized >= 1f)
        {
            string stationName = station.EquipmentName;
            station.EndEnemySquat(owner);
            RestoreBasePose();
            owner.ReleaseVisitorWorkoutPose();
            running = false;
            completed = true;
            initialPoseHoldPending = false;
            Debug.Log(
                $"GYMCHAOS_SQUAT_COMPLETE enemy={owner.Identity} station={stationName} reps={repetitions}",
                this);
        }
    }

    private void LateUpdate()
    {
        if (!running)
        {
            return;
        }
        if (deadliftMode)
        {
            LateUpdateDeadlift();
            return;
        }

        float cycle = Mathf.Repeat(elapsed, repDuration) / Mathf.Max(0.01f, repDuration);
        float motion = Mathf.Sin(cycle * Mathf.PI);
        CurrentMotion = motion;
        SetAuthoredWorkoutPhase(cycle);
        if (!workoutGroundingApplied)
        {
            owner.GetComponent<ExternalRiggedCharacterVisual>()?.RegroundAfterRootSnap();
            workoutGroundingApplied = true;
        }
        ApplySquatPose(motion);
    }

    private void SetAuthoredWorkoutPhase(float normalizedPhase)
    {
        if (owner == null)
        {
            return;
        }

        MixamoScanRetargetAnimator authoredAnimator =
            owner.GetComponentInChildren<MixamoScanRetargetAnimator>(true);
        authoredAnimator?.SetWorkoutPosePhase(normalizedPhase);
    }

    private void ApplySquatPose(float motion)
    {
        if (!basePoseCaptured)
        {
            return;
        }

        if (TryApplyAuthoredSquatPose())
        {
            return;
        }

        // The old generic lower-body IK fallback lengthened legs and wrote
        // foot/ankle transforms. Keep the no-clip path root-only as well:
        // authored enemy assets and their limb transforms are never modified.
        ApplyRootOnlySquatFallback();
    }

    // The contact-baked clip keeps both soles on one plane but its pelvis
    // height can sit a few centimetres off the runtime support plane. Shift
    // only the visual skeleton root by the mean sole error; limbs untouched.
    private const float MaxAuthoredSquatGroundCorrection = 0.08f;

    private void ApplyAuthoredSquatGroundCorrection(
        MixamoScanRetargetAnimator authoredAnimator, Vector3 up)
    {
        if (authoredAnimator == null || leftFoot == null || rightFoot == null)
        {
            return;
        }
        float floorY = GetFloorY(owner);
        // The bind-pose sole anchors misjudge the per-character rigs by about
        // 13 cm (with the wrong sign), which lifted the squatting body about
        // 6 cm off the floor. Measure the rendered skin once per set instead:
        // the authored clip keeps both soles on one plane for the whole rep,
        // so one root offset grounds it.
        // The first frames of a set can still show the station-snap pose;
        // measure once the authored clip drives the skin.
        if (!authoredGroundCorrectionMeasured &&
            ++authoredGroundMeasureFrames >= AuthoredGroundMeasureFrame)
        {
            authoredGroundCorrectionMeasured = true;
            authoredGroundCorrection = TryMeasureRenderedSoleY(out float soleY)
                ? Mathf.Clamp(floorY - soleY,
                    -MaxAuthoredSquatGroundCorrection, MaxAuthoredSquatGroundCorrection)
                : 0f;
            Debug.Log(
                $"GYMCHAOS_SQUAT_GROUND_MEASURED enemy={owner.Identity} " +
                $"floor={floorY:0.000} correction={authoredGroundCorrection:0.000}", this);
        }
        authoredAnimator.ApplyGroundedRootCorrection(up, authoredGroundCorrection);
    }

    private const int AuthoredGroundMeasureFrame = 4;
    private bool authoredGroundCorrectionMeasured;
    private int authoredGroundMeasureFrames;
    private float authoredGroundCorrection;
    private static Mesh soleMeasureMesh;

    private bool TryMeasureRenderedSoleY(out float soleY)
    {
        soleY = 0f;
        SkinnedMeshRenderer body = null;
        foreach (SkinnedMeshRenderer candidate in
            owner.GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            if (candidate.sharedMesh != null && (body == null ||
                candidate.sharedMesh.vertexCount > body.sharedMesh.vertexCount))
            {
                body = candidate;
            }
        }
        if (body == null)
        {
            return false;
        }
        if (soleMeasureMesh == null)
        {
            soleMeasureMesh = new Mesh { name = "Squat sole measure" };
        }
        // Unscaled bake: vertices relative to the renderer position/rotation.
        body.BakeMesh(soleMeasureMesh, false);
        Vector3[] vertices = soleMeasureMesh.vertices;
        if (vertices.Length == 0)
        {
            return false;
        }
        Matrix4x4 toWorld = Matrix4x4.TRS(
            body.transform.position, body.transform.rotation, Vector3.one);
        float lowest = float.PositiveInfinity;
        for (int i = 0; i < vertices.Length; i++)
        {
            lowest = Mathf.Min(lowest, toWorld.MultiplyPoint3x4(vertices[i]).y);
        }
        soleY = lowest;
        return true;
    }

    private void ApplyRootOnlySquatFallback()
    {
        if (owner == null || station == null)
        {
            return;
        }

        MixamoScanRetargetAnimator authoredAnimator =
            owner.GetComponentInChildren<MixamoScanRetargetAnimator>(true);
        if (authoredAnimator != null && leftFoot != null && rightFoot != null)
        {
            float leftSignedGroundError =
                (leftFoot.position.y + leftFootMeshSoleOffset) - GetFloorY(owner);
            float rightSignedGroundError =
                (rightFoot.position.y + rightFootMeshSoleOffset) - GetFloorY(owner);
            float rootGroundCorrection = Mathf.Clamp(
                -Mathf.Max(leftSignedGroundError, rightSignedGroundError),
                -0.25f,
                0.25f);
            authoredAnimator.ApplyGroundedRootCorrection(
                owner.transform.up, rootGroundCorrection);
        }

        currentHipDrop = 0f;
        currentKneeBend = 0f;
        currentKneeBendDifference = 0f;
        currentLegDepthDifference = 0f;
        currentLeftFootRotationError = 0f;
        currentRightFootRotationError = 0f;
        currentLeftFootSoleError = GetFootSoleError(
            leftFoot, leftToe, leftFootBoneToSoleOffset);
        currentRightFootSoleError = GetFootSoleError(
            rightFoot, rightToe, rightFootBoneToSoleOffset);
        currentLeftFootGroundError = GetFootPlantGroundError(
            leftFoot, baseLeftSoleAnchorLocal);
        currentRightFootGroundError = GetFootPlantGroundError(
            rightFoot, baseRightSoleAnchorLocal);
        UpdateFixedFootGroundErrors();

        Vector3 currentBarTarget = CalculateBarTargetPosition(owner);
        station.SyncEnemySquatBarPose(owner, Traps, currentBarTarget);
        currentBarBodyFollowError = Vector3.Distance(
            station.EnemySquatBarCenter, currentBarTarget);
        currentBarDropFromStart = initialAttachedBarCenter.y -
            station.EnemySquatBarCenter.y;
    }
    private bool TryApplyAuthoredSquatPose()
    {
        if (owner == null || station == null)
        {
            return false;
        }

        MixamoScanRetargetAnimator authoredAnimator =
            owner.GetComponentInChildren<MixamoScanRetargetAnimator>(true);
        if (authoredAnimator == null || !authoredAnimator.IsWorkoutPoseLocked ||
            !authoredAnimator.HasAuthoredSquatClip)
        {
            return false;
        }

        // The authored squat already contains each character's own hip,
        // spine, leg, foot and hand motion. Do not overwrite that evaluated
        // pose with the old generic world-space IK solver; it was calibrated
        // against a shared skeleton and visibly changed height/shape on the
        // per-character rigs. Only derive measurements and attach the scene
        // bar to the current authored traps position here.
        Vector3 up = owner.transform.up;
        // Keep the authored skeleton and enemy assets untouched. If the
        // exported squat pose lifts both soles, move only the visual
        // skeleton root down to the support plane; never lengthen a leg or
        // translate either foot/ankle bone.
        if (!authoredSquatTargetsCaptured)
        {
            CaptureAuthoredSquatTargets();
        }
        // The per-character squat.fbx is one complete authored repetition.
        // Keep its hip, torso, leg, foot and hand transforms untouched; the
        // old generic correction solvers were calibrated against a shared
        // skeleton and changed the source animation on differently rigged
        // enemies.
        ApplyAuthoredSquatGroundCorrection(authoredAnimator, up);
        currentHipDrop = Mathf.Max(
            0f, Vector3.Dot(baseHipsWorldPosition - hips.position, up));
        float leftKneeBend = GetKneeBend(leftThigh, leftShin, leftFoot);
        float rightKneeBend = GetKneeBend(rightThigh, rightShin, rightFoot);
        currentKneeBend = (leftKneeBend + rightKneeBend) * 0.5f;
        currentKneeBendDifference = Mathf.Abs(leftKneeBend - rightKneeBend);
        Vector3 depthAxis = Vector3.ProjectOnPlane(
            owner.transform.forward, up);
        if (depthAxis.sqrMagnitude < 0.0001f)
        {
            depthAxis = Vector3.forward;
        }
        depthAxis.Normalize();
        currentLegDepthDifference = Mathf.Abs(
            Vector3.Dot(leftShin.position - leftThigh.position, depthAxis) -
            Vector3.Dot(rightShin.position - rightThigh.position, depthAxis));

        currentLeftFootSoleError = GetFootSoleError(
            leftFoot, leftToe, leftFootBoneToSoleOffset);
        currentRightFootSoleError = GetFootSoleError(
            rightFoot, rightToe, rightFootBoneToSoleOffset);
        currentLeftFootGroundError = GetFootPlantGroundError(
            leftFoot, baseLeftSoleAnchorLocal);
        currentRightFootGroundError = GetFootPlantGroundError(
            rightFoot, baseRightSoleAnchorLocal);
        UpdateFixedFootGroundErrors();
        currentLeftFootRotationError = Quaternion.Angle(
            leftFoot.rotation, baseLeftFootRotation);
        currentRightFootRotationError = Quaternion.Angle(
            rightFoot.rotation, baseRightFootRotation);

        // The bar rests on the rendered upper back (measured from the baked
        // skin, so thick traps do not swallow it), and the hands close over
        // it with an overhand grip. Both are layered onto the authored pose.
        Vector3 currentBarTarget = GetAuthoredBarRestTarget(
            CalculateBarTargetPosition(owner));
        station.SyncEnemySquatBarPose(owner, Traps, currentBarTarget);
        currentBarBodyFollowError = Vector3.Distance(
            station.EnemySquatBarCenter, currentBarTarget);
        currentBarDropFromStart = initialAttachedBarCenter.y -
            station.EnemySquatBarCenter.y;
        ApplyAuthoredOverhandGrip(currentBarTarget);

        if (!poseMetricLogged && CurrentMotion > 0.82f)
        {
            poseMetricLogged = true;
            Debug.Log(
                $"GYMCHAOS_SQUAT_AUTHORED_METRICS enemy={owner.Identity} " +
                $"clip={authoredAnimator.CurrentAnimationClipName} " +
                "mode=authored-clip-only " +
                $"footPositionError={FootPlantError:0.000} " +
                $"soleError={FootSoleError:0.000} " +
                $"groundError={FootGroundError:0.000} " +
                $"footRotation={FootRotationError:0.0} " +
                $"hipDrop={currentHipDrop:0.000} " +
                $"kneeBend={currentKneeBend:0.0} " +
                $"kneeDelta={currentKneeBendDifference:0.0} " +
                $"barFollow={currentBarBodyFollowError:0.000} " +
                $"barDrop={currentBarDropFromStart:0.000}",
                this);
        }
        return true;
    }

    // Authored squat: bar rest on the upper back and overhand grip ----------
    // Wrists sit slightly behind the bar so the fingers wrap over its top.
    private const float GripWristBackTilt = 0.35f;
    private const float BarRestAboveShoulderJoints = 0.02f;
    private const float BarRestMaxDeepPush = 0.08f;
    // Skin farther than this from its own bone is hair or loose clothing
    // (Goku's mane, hoods), not the back the bar rests on.
    private const float BarSupportMaxBoneDistance = 0.30f;
    private bool barRestCalibrated;
    private bool barRestDeepCalibrated;
    private Vector3 barRestLocal;
    private Vector3 barRestDeepLocal;
    private Mesh barRestBakeMesh;
    private bool gripCalibrated;
    private bool gripGeometryCached;
    private bool gripWidthMeasured;
    private float gripHalfWidth;
    private Vector3 leftGripHollowLocal;
    private Vector3 rightGripHollowLocal;
    private Vector3 leftKnuckleAxisLocal;
    private Vector3 rightKnuckleAxisLocal;
    private Vector3 leftHandLengthLocal;
    private Vector3 rightHandLengthLocal;
    public float BarRestCorrection { get; private set; }
    internal static bool BarRestDiagnostics;

    private void OnDestroy()
    {
        if (barRestBakeMesh != null)
        {
            Destroy(barRestBakeMesh);
            barRestBakeMesh = null;
        }
        if (deadliftClearanceMesh != null)
        {
            Destroy(deadliftClearanceMesh);
            deadliftClearanceMesh = null;
        }
    }

    private void ResetAuthoredBarAndGrip()
    {
        barRestCalibrated = false;
        barRestDeepCalibrated = false;
        barRestLocal = Vector3.zero;
        // gripGeometryCached, the hollows and wrapFingers stay: they describe
        // this fighter's hands, not the set. The grip width is re-measured.
        gripCalibrated = false;
        gripWidthMeasured = false;
        BarRestCorrection = 0f;
    }

    // The bone-based target sits between the traps and neck bones; on thick
    // scans that point is inside the body. Slide the bar straight back (or
    // forward, if it floats) until the shaft just touches the rendered skin.
    // Measured once standing and once at the bottom of the first rep, then
    // kept in chest space so it rides with the torso through every rep.
    private Vector3 GetAuthoredBarRestTarget(Vector3 boneTarget)
    {
        Transform anchor = Traps;
        if (anchor == null || owner == null)
        {
            return boneTarget;
        }
        if (!barRestCalibrated)
        {
            // Rest the shaft on the upper back across the rear delts, at the
            // height of the shoulder joints: below the traps and well clear
            // of the neck.
            if (leftUpperArm != null && rightUpperArm != null)
            {
                Vector3 torsoUp = GetTorsoUp(GetBarAxis());
                Vector3 joints = (leftUpperArm.position + rightUpperArm.position) * 0.5f;
                boneTarget += torsoUp * (Vector3.Dot(joints - boneTarget, torsoUp) + BarRestAboveShoulderJoints);
            }
            barRestLocal = anchor.InverseTransformPoint(SolveBarRest(boneTarget));
            barRestCalibrated = true;
            BarRestCorrection = Vector3.Distance(
                boneTarget, anchor.TransformPoint(barRestLocal));
        }
        else if (!barRestDeepCalibrated && CurrentMotion > 0.85f)
        {
            // The upper back changes shape a little between standing and the
            // bottom; measure the bottom too and blend by squat depth. A jump
            // larger than BarRestMaxDeepPush is hair or cloth, not the back.
            Vector3 current = anchor.TransformPoint(barRestLocal);
            Vector3 backDirection = GetBarBackDirection();
            float push = Mathf.Clamp(Vector3.Dot(SolveBarRest(current) - current, backDirection),
                -BarRestMaxDeepPush, BarRestMaxDeepPush);
            barRestDeepLocal = anchor.InverseTransformPoint(current + backDirection * push);
            barRestDeepCalibrated = true;
        }
        if (barRestDeepCalibrated)
        {
            return anchor.TransformPoint(Vector3.Lerp(
                barRestLocal, barRestDeepLocal, Mathf.Clamp01(CurrentMotion / 0.9f)));
        }
        return anchor.TransformPoint(barRestLocal);
    }

    // Goku's mane is skinned to the shoulders and hangs over the upper back;
    // the bar goes through it onto the body, so his support skin must stay
    // closer to its bones than the other rigs' thick traps.
    internal float GetBarSupportMaxBoneDistance()
    {
        return owner != null && owner.Identity == BodybuilderIdentity.Goku
            ? 0.17f
            : BarSupportMaxBoneDistance;
    }

    private Vector3 GetBarAxis()
    {
        if (deadliftMode && station != null)
        {
            return station.DeadliftBarAxis;
        }
        Vector3 axis = Vector3.ProjectOnPlane(owner.transform.right, Vector3.up);
        return axis.sqrMagnitude > 0.0001f ? axis.normalized : Vector3.right;
    }

    private Vector3 GetTorsoUp(Vector3 axis)
    {
        Vector3 up = neck != null && chest != null
            ? neck.position - chest.position
            : owner.transform.up;
        up = Vector3.ProjectOnPlane(up, axis);
        return up.sqrMagnitude > 0.0001f ? up.normalized : Vector3.up;
    }

    private Vector3 GetBarBackDirection()
    {
        Vector3 axis = GetBarAxis();
        Vector3 back = Vector3.Cross(axis, GetTorsoUp(axis));
        if (Vector3.Dot(back, owner.transform.forward) > 0f)
        {
            back = -back;
        }
        return back.normalized;
    }

    private Vector3 SolveBarRest(Vector3 start)
    {
        Vector3 axis = GetBarAxis();
        Vector3 torsoUp = GetTorsoUp(axis);
        Vector3 back = GetBarBackDirection();
        float radius = (station != null ? station.SquatBarShaftRadius : 0.02f) + 0.002f;
        float span = leftUpperArm != null && rightUpperArm != null
            ? Mathf.Abs(Vector3.Dot(leftUpperArm.position - rightUpperArm.position, axis)) * 0.5f * 0.85f
            : 0.18f;
        float maxBoneDistance = GetBarSupportMaxBoneDistance();
        float required = float.NegativeInfinity;
        string blocker = "none";
        barRestBakeMesh ??= new Mesh();
        SkinnedMeshRenderer[] skins = owner.GetComponentsInChildren<SkinnedMeshRenderer>();
        for (int s = 0; s < skins.Length; s++)
        {
            SkinnedMeshRenderer skin = skins[s];
            if (skin == null || !skin.enabled || skin.sharedMesh == null)
            {
                continue;
            }
            Vector3[] vertices = GymSkinSampler.BakeWorld(skin, barRestBakeMesh);
            int[] dominant = GymSkinSampler.GetDominantBones(skin.sharedMesh);
            Transform[] bones = skin.bones;
            for (int i = 0; i < vertices.Length; i++)
            {
                // Arms reach the bar from outside; the head and hair sit in
                // front of it. Only the neck, shoulders and back support it.
                int bone = i < dominant.Length ? dominant[i] : -1;
                Transform boneTransform = bone >= 0 && bone < bones.Length ? bones[bone] : null;
                if (GymSkinSampler.IsInChain(boneTransform, leftUpperArm) ||
                    GymSkinSampler.IsInChain(boneTransform, rightUpperArm) ||
                    (neck != null && boneTransform != null && boneTransform != neck &&
                        boneTransform.IsChildOf(neck)))
                {
                    continue;
                }
                Vector3 offset = vertices[i] - start;
                if (Mathf.Abs(Vector3.Dot(offset, axis)) > span ||
                    (boneTransform != null &&
                        (vertices[i] - boneTransform.position).sqrMagnitude >
                        maxBoneDistance * maxBoneDistance))
                {
                    continue;
                }
                float h = Vector3.Dot(offset, torsoUp);
                if (Mathf.Abs(h) >= radius)
                {
                    continue;
                }
                float f = Vector3.Dot(offset, back);
                float need = f + Mathf.Sqrt(radius * radius - h * h);
                if (need > required)
                {
                    required = need;
                    if (BarRestDiagnostics)
                    {
                        blocker = $"{skin.name}/{(boneTransform != null ? boneTransform.name : "none")}";
                    }
                }
            }
        }
        if (BarRestDiagnostics)
        {
            Debug.Log($"GYMCHAOS_SQUAT_BAR_REST enemy={owner.Identity} push={required:0.000} " +
                $"blocker={blocker} motion={CurrentMotion:0.00}");
        }
        return float.IsNegativeInfinity(required) ? start : start + back * required;
    }

    // Overhand grip: the hollow of each hand's curled fingers goes onto the
    // bar axis, knuckles along the bar, wrist below and behind it. The arm
    // reaches it with a two-bone solve that keeps the authored elbow plane;
    // fingers keep their authored curl.
    private void ApplyAuthoredOverhandGrip(Vector3 barCenter, Vector3? handUpOverride = null)
    {
        if (owner == null || !HasValidArmRig)
        {
            return;
        }
        Vector3 axis = GetBarAxis();
        if (!EnsureGripCalibrated())
        {
            return;
        }
        if (gripHalfWidth <= 0f || !gripWidthMeasured)
        {
            // Keep each rig's own authored grip width, but outside the
            // shoulders and inside the bar sleeves.
            float authored = (
                Mathf.Abs(Vector3.Dot(leftHand.TransformPoint(leftGripHollowLocal) - barCenter, axis)) +
                Mathf.Abs(Vector3.Dot(rightHand.TransformPoint(rightGripHollowLocal) - barCenter, axis))) * 0.5f;
            float shoulderHalf = Mathf.Abs(
                Vector3.Dot(leftUpperArm.position - rightUpperArm.position, axis)) * 0.5f;
            gripHalfWidth = Mathf.Clamp(authored, shoulderHalf + 0.05f, shoulderHalf + 0.32f);
            gripWidthMeasured = true;
        }

        Vector3 torsoUp = GetTorsoUp(axis);
        Vector3 handUp = handUpOverride ??
            (torsoUp + GetBarBackDirection() * GripWristBackTilt).normalized;
        float leftSign = Vector3.Dot(leftUpperArm.position - barCenter, axis) < 0f ? -1f : 1f;
        // Deadlift (hands hanging): every member holds the bar in a real
        // overhand hook. The shaft sits in the crease where the fingers
        // leave the palm, on the palm side (towards the body), and the
        // fingers curl under and around it. Authored hollows only fit rigs
        // whose clip already curls the fingers; straight-fingered rigs (Jay
        // Cutler, Arnold) had the bar running through the palm.
        bool hookGrip = handUpOverride.HasValue && !UsesRigidFistGrip && !HookGripDisabledForVerification;
        Vector3 leftHollow = hookGrip ? HookHollowLocal(true, handUp) : leftGripHollowLocal;
        Vector3 rightHollow = hookGrip ? HookHollowLocal(false, handUp) : rightGripHollowLocal;
        SolveHandOnBar(leftUpperArm, leftForearm, leftHand,
            barCenter + axis * (leftSign * gripHalfWidth), axis * leftSign, handUp,
            leftHollow, leftKnuckleAxisLocal, leftHandLengthLocal);
        SolveHandOnBar(rightUpperArm, rightForearm, rightHand,
            barCenter - axis * (leftSign * gripHalfWidth), -axis * leftSign, handUp,
            rightHollow, rightKnuckleAxisLocal, rightHandLengthLocal);
        if (wrapFingers || hookGrip)
        {
            float wrapRadius = CurrentBarShaftRadius + (hookGrip ? HookFingerHalfThickness : FingerHalfThickness);
            WrapFingerAroundBar(leftIndexTip, barCenter, axis, wrapRadius, hookGrip ? HookMinFingerFlex : 0f);
            WrapFingerAroundBar(leftMiddleTip, barCenter, axis, wrapRadius, hookGrip ? HookMinFingerFlex : 0f);
            WrapFingerAroundBar(leftRingTip, barCenter, axis, wrapRadius, hookGrip ? HookMinFingerFlex : 0f);
            WrapFingerAroundBar(leftPinkyTip, barCenter, axis, wrapRadius, hookGrip ? HookMinFingerFlex : 0f);
            WrapFingerAroundBar(rightIndexTip, barCenter, axis, wrapRadius, hookGrip ? HookMinFingerFlex : 0f);
            WrapFingerAroundBar(rightMiddleTip, barCenter, axis, wrapRadius, hookGrip ? HookMinFingerFlex : 0f);
            WrapFingerAroundBar(rightRingTip, barCenter, axis, wrapRadius, hookGrip ? HookMinFingerFlex : 0f);
            WrapFingerAroundBar(rightPinkyTip, barCenter, axis, wrapRadius, hookGrip ? HookMinFingerFlex : 0f);
            if (hookGrip)
            {
                // The thumb closes round the shaft from the other side.
                WrapFingerAroundBar(FindThumbTip(leftHand, ref leftThumbTip), barCenter, axis, wrapRadius, hookGrip ? HookMinFingerFlex : 0f);
                WrapFingerAroundBar(FindThumbTip(rightHand, ref rightThumbTip), barCenter, axis, wrapRadius, hookGrip ? HookMinFingerFlex : 0f);
            }
        }
    }

    // Locates the hollow of each hand's curled fingers once per fighter.
    private bool EnsureGripCalibrated()
    {
        if (!gripCalibrated && gripGeometryCached)
        {
            gripCalibrated = true;
        }
        else if (!gripCalibrated)
        {
            gripCalibrated =
                CaptureHandGrip(leftHand, leftIndexTip, leftMiddleTip, leftRingTip, leftPinkyTip,
                    out leftGripHollowLocal, out leftKnuckleAxisLocal, out leftHandLengthLocal) &&
                CaptureHandGrip(rightHand, rightIndexTip, rightMiddleTip, rightRingTip, rightPinkyTip,
                    out rightGripHollowLocal, out rightKnuckleAxisLocal, out rightHandLengthLocal);
            if (!gripCalibrated)
            {
                return false;
            }
            float barRadius = CurrentBarShaftRadius;
            // Goku's fists are skinned rigidly to the hand bone: the fingers
            // cannot open around the bar, so the shaft runs through the
            // middle of the closed fist (fingers overlapping the bar).
            UsesRigidFistGrip = owner.Identity == BodybuilderIdentity.Goku;
            if (UsesRigidFistGrip)
            {
                CaptureRigidFistCentres();
            }
            else
            {
                RefineGripHollows(barRadius);
                // Hands whose authored fingers do not enclose a bar-sized
                // hollow are wrapped around the shaft every frame.
                wrapFingers = LeftGripClearance < 0f || RightGripClearance < 0f ||
                    LeftGripClearance > barRadius * 1.6f || RightGripClearance > barRadius * 1.6f;
            }
            // The hand shape is the rig's own; measure it once per fighter.
            gripGeometryCached = true;
        }
        return gripCalibrated;
    }

    // An open authored hand (Goku) has no hollow to close over the bar.
    // Bend each phalanx about the bar axis so its far end lies on the shaft
    // surface: the fingers then wrap over the bar instead of floating.
    private const float FingerHalfThickness = 0.011f;
    private bool wrapFingers;
    public bool UsesRigidFistGrip { get; private set; }

    private static void WrapFingerAroundBar(
        Transform tip, Vector3 barCenter, Vector3 barAxis, float wrapRadius, float minFlex = 0f)
    {
        if (tip == null || tip.parent == null || tip.parent.parent == null)
        {
            return;
        }
        for (int j = 0; j < 3; j++)
        {
            Transform joint = j == 0 ? tip.parent.parent : j == 1 ? tip.parent : tip;
            Vector3 pivot = joint.position;
            Vector3 end = j == 0 ? tip.parent.position
                : j == 1 ? tip.position
                : tip.position + (tip.position - tip.parent.position) * 0.8f;
            float bestAngle = 0f;
            float bestScore = float.PositiveInfinity;
            // Second pass only when nothing fits: let the phalanx touch the
            // shaft at a slant rather than leave the finger straight.
            for (int pass = 0; pass < 2 && bestScore > 0.012f; pass++)
            {
                for (float angle = -100f; angle <= 100.1f; angle += 2.5f)
                {
                    Vector3 rotated = pivot + Quaternion.AngleAxis(angle, barAxis) * (end - pivot);
                    // Hook grip: the middle and last phalanx bend at least
                    // minFlex from the one before, so no finger runs straight.
                    if (j > 0 && minFlex > 0f &&
                        Vector3.Angle(pivot - joint.parent.position, rotated - pivot) < minFlex)
                    {
                        continue;
                    }
                    // A phalanx lies on the shaft, never across it: reject a
                    // solution whose middle cuts into the bar.
                    Vector3 middle = (pivot + rotated) * 0.5f - barCenter;
                    if (pass == 0 &&
                        (middle - barAxis * Vector3.Dot(middle, barAxis)).magnitude < wrapRadius * 0.85f)
                    {
                        continue;
                    }
                    Vector3 offset = rotated - barCenter;
                    float distance = (offset - barAxis * Vector3.Dot(offset, barAxis)).magnitude;
                    // Never end inside the shaft.
                    float score = Mathf.Abs(distance - wrapRadius) + Mathf.Abs(angle) * 0.00004f +
                        Mathf.Max(0f, wrapRadius - distance) * 4f;
                    if (score < bestScore)
                    {
                        bestScore = score;
                        bestAngle = angle;
                    }
                }
            }
            joint.rotation = Quaternion.AngleAxis(bestAngle, barAxis) * joint.rotation;
        }
    }

    public float LeftGripClearance { get; private set; }
    public float RightGripClearance { get; private set; }

    // A rigid fist: its finger bones do not match the sculpted fingers, so
    // take the middle of the distal (finger) part of the hand skin instead.
    private void CaptureRigidFistCentres()
    {
        CollectHandSkin(out List<Vector3> left, out List<Vector3> right);
        leftGripHollowLocal = FistCentreLocal(leftHand, left, leftGripHollowLocal);
        rightGripHollowLocal = FistCentreLocal(rightHand, right, rightGripHollowLocal);
    }

    private static Vector3 FistCentreLocal(Transform hand, List<Vector3> skin, Vector3 fallback)
    {
        if (hand == null || skin.Count < 30)
        {
            return fallback;
        }
        Vector3 centroid = Vector3.zero;
        for (int i = 0; i < skin.Count; i++) centroid += skin[i];
        centroid /= skin.Count;
        Vector3 length = centroid - hand.position;
        if (length.sqrMagnitude < 1e-6f)
        {
            return fallback;
        }
        length.Normalize();
        float reach = 0f;
        for (int i = 0; i < skin.Count; i++)
            reach = Mathf.Max(reach, Vector3.Dot(skin[i] - hand.position, length));
        Vector3 sum = Vector3.zero;
        int count = 0;
        for (int i = 0; i < skin.Count; i++)
        {
            if (Vector3.Dot(skin[i] - hand.position, length) < reach * 0.45f) continue;
            sum += skin[i];
            count++;
        }
        return count > 0 ? hand.InverseTransformPoint(sum / count) : fallback;
    }

    private void CollectHandSkin(out List<Vector3> left, out List<Vector3> right)
    {
        barRestBakeMesh ??= new Mesh();
        left = new List<Vector3>(2048);
        right = new List<Vector3>(2048);
        SkinnedMeshRenderer[] skins = owner.GetComponentsInChildren<SkinnedMeshRenderer>();
        for (int s = 0; s < skins.Length; s++)
        {
            SkinnedMeshRenderer skin = skins[s];
            if (skin == null || !skin.enabled || skin.sharedMesh == null)
            {
                continue;
            }
            Vector3[] vertices = GymSkinSampler.BakeWorld(skin, barRestBakeMesh);
            int[] dominant = GymSkinSampler.GetDominantBones(skin.sharedMesh);
            Transform[] bones = skin.bones;
            bool[] inLeft = GymSkinSampler.ChainMask(bones, leftHand);
            bool[] inRight = GymSkinSampler.ChainMask(bones, rightHand);
            for (int i = 0; i < vertices.Length && i < dominant.Length; i++)
            {
                int bone = dominant[i];
                if (bone < 0 || bone >= bones.Length)
                {
                    continue;
                }
                if (inLeft[bone])
                {
                    left.Add(vertices[i]);
                }
                else if (inRight[bone])
                {
                    right.Add(vertices[i]);
                }
            }
        }
    }

    // The joint average only approximates the hollow of the curled fingers.
    // Search the hand's cross-section (perpendicular to the knuckle line) on
    // the baked skin for the point the fingers wrap around: enclosed by hand
    // skin on most sides and about one bar radius from the nearest skin.
    private void RefineGripHollows(float barRadius)
    {
        CollectHandSkin(out List<Vector3> left, out List<Vector3> right);
        LeftGripClearance = RefineGripHollow(leftHand, left, leftKnuckleAxisLocal,
            barRadius, ref leftGripHollowLocal);
        RightGripClearance = RefineGripHollow(rightHand, right, rightKnuckleAxisLocal,
            barRadius, ref rightGripHollowLocal);
    }

    private static float RefineGripHollow(
        Transform hand, List<Vector3> skin, Vector3 knuckleAxisLocal, float barRadius,
        ref Vector3 hollowLocal)
    {
        if (hand == null || skin.Count < 30)
        {
            return -1f;
        }
        Vector3 axis = hand.TransformDirection(knuckleAxisLocal).normalized;
        Vector3 origin = hand.TransformPoint(hollowLocal);
        Vector3 e1 = Vector3.Cross(axis, Mathf.Abs(Vector3.Dot(axis, Vector3.up)) < 0.9f
            ? Vector3.up : Vector3.forward).normalized;
        Vector3 e2 = Vector3.Cross(axis, e1).normalized;
        // Only the finger span along the knuckle line can hold the bar.
        float minAlong = float.PositiveInfinity;
        float maxAlong = float.NegativeInfinity;
        Vector2[] points = new Vector2[skin.Count];
        float[] along = new float[skin.Count];
        for (int i = 0; i < skin.Count; i++)
        {
            Vector3 offset = skin[i] - origin;
            along[i] = Vector3.Dot(offset, axis);
            points[i] = new Vector2(Vector3.Dot(offset, e1), Vector3.Dot(offset, e2));
            minAlong = Mathf.Min(minAlong, along[i]);
            maxAlong = Mathf.Max(maxAlong, along[i]);
        }
        float centre = (minAlong + maxAlong) * 0.5f;
        float halfSpan = (maxAlong - minAlong) * 0.3f;
        const int Sectors = 12;
        float bestScore = float.PositiveInfinity;
        Vector2 best = Vector2.zero;
        float bestClearance = -1f;
        bool[] covered = new bool[Sectors];
        // Points of the central span, bucketed so each candidate centre reads
        // its neighbourhood instead of every point. The nearest-point minimum
        // and the sector set do not depend on visit order, so the result is
        // the same as scanning all points.
        PointBuckets buckets = new PointBuckets(points, along, centre, halfSpan, 0.008f);
        for (float x = -0.05f; x <= 0.0501f; x += 0.004f)
        {
            for (float y = -0.05f; y <= 0.0501f; y += 0.004f)
            {
                Vector2 c = new Vector2(x, y);
                float clearance = buckets.Nearest(c);
                if (float.IsPositiveInfinity(clearance)) continue;
                System.Array.Clear(covered, 0, Sectors);
                // Coverage above 7 sectors does not change the score.
                int coverage = buckets.CountSectors(c, clearance + 0.03f, covered, Sectors, 7);
                // Wrapped on at least half the circle, snug around the bar.
                float score = Mathf.Abs(clearance - barRadius) * 100f +
                    Mathf.Max(0, 7 - coverage) * 10f + c.magnitude * 5f;
                if (score < bestScore)
                {
                    bestScore = score;
                    best = c;
                    bestClearance = clearance;
                }
            }
        }
        Vector3 world = origin + e1 * best.x + e2 * best.y + axis * centre;
        hollowLocal = hand.InverseTransformPoint(world);
        return bestClearance;
    }

    // Uniform 2D grid over the hand cross-section points used by RefineGripHollow.
    private sealed class PointBuckets
    {
        // Bucket assignment rounds; a point may sit up to this far outside its bucket.
        private const float Slack = 1e-4f;
        private readonly Vector2[] sorted;
        private readonly int[] start;
        private readonly int columns;
        private readonly int rows;
        private readonly float minX;
        private readonly float minY;
        private readonly float size;

        public PointBuckets(Vector2[] points, float[] along, float centre, float halfSpan, float bucketSize)
        {
            size = bucketSize;
            List<Vector2> kept = new List<Vector2>(points.Length);
            minX = float.PositiveInfinity;
            minY = float.PositiveInfinity;
            float maxX = float.NegativeInfinity;
            float maxY = float.NegativeInfinity;
            for (int i = 0; i < points.Length; i++)
            {
                // Same test as the original scan: skip only when clearly outside the span.
                if (Mathf.Abs(along[i] - centre) > halfSpan) continue;
                Vector2 p = points[i];
                // Candidate centres lie within 5 cm of the hand: a vertex a metre off
                // (or NaN from a broken bake) can never be nearest, and must not size the grid.
                if (!(Mathf.Abs(p.x) < 1f && Mathf.Abs(p.y) < 1f)) continue;
                kept.Add(p);
                minX = Mathf.Min(minX, p.x);
                minY = Mathf.Min(minY, p.y);
                maxX = Mathf.Max(maxX, p.x);
                maxY = Mathf.Max(maxY, p.y);
            }
            if (kept.Count == 0)
            {
                sorted = System.Array.Empty<Vector2>();
                start = new int[1];
                return;
            }
            columns = Mathf.FloorToInt((maxX - minX) / size) + 1;
            rows = Mathf.FloorToInt((maxY - minY) / size) + 1;
            start = new int[columns * rows + 1];
            int[] cellOf = new int[kept.Count];
            for (int i = 0; i < kept.Count; i++)
            {
                cellOf[i] = Cell(kept[i]);
                start[cellOf[i] + 1]++;
            }
            for (int i = 0; i < columns * rows; i++) start[i + 1] += start[i];
            int[] fill = (int[])start.Clone();
            sorted = new Vector2[kept.Count];
            for (int i = 0; i < kept.Count; i++) sorted[fill[cellOf[i]]++] = kept[i];
        }

        private int Cell(Vector2 p)
        {
            int cx = Mathf.Clamp(Mathf.FloorToInt((p.x - minX) / size), 0, columns - 1);
            int cy = Mathf.Clamp(Mathf.FloorToInt((p.y - minY) / size), 0, rows - 1);
            return cy * columns + cx;
        }

        /// <summary>Distance from <paramref name="c"/> to the nearest point (infinity when none).</summary>
        public float Nearest(Vector2 c)
        {
            float best = float.PositiveInfinity;
            if (sorted.Length == 0) return best;
            int bx = Mathf.FloorToInt((c.x - minX) / size);
            int by = Mathf.FloorToInt((c.y - minY) / size);
            for (int r = 0; ; r++)
            {
                for (int y = by - r; y <= by + r; y++)
                {
                    if (y < 0 || y >= rows) continue;
                    bool edgeRow = y == by - r || y == by + r;
                    for (int x = bx - r; x <= bx + r; x += edgeRow ? 1 : 2 * r)
                    {
                        if (x >= 0 && x < columns)
                        {
                            int cell = y * columns + x;
                            for (int i = start[cell]; i < start[cell + 1]; i++)
                            {
                                // (sorted[i] - c).magnitude written out.
                                float dx = sorted[i].x - c.x;
                                float dy = sorted[i].y - c.y;
                                float d = (float)System.Math.Sqrt(dx * dx + dy * dy);
                                if (d < best) best = d;
                            }
                        }
                        if (r == 0) break;
                    }
                }
                // Points outside rings 0..r are at least r buckets away.
                bool coversAll = bx - r <= 0 && by - r <= 0 && bx + r >= columns - 1 && by + r >= rows - 1;
                if (coversAll || best <= r * size - Slack) return best;
            }
        }

        /// <summary>
        /// Marks the sectors (around <paramref name="c"/>) that hold a point within
        /// <paramref name="reach"/> and returns how many are marked, stopping at <paramref name="enough"/>.
        /// </summary>
        public int CountSectors(Vector2 c, float reach, bool[] covered, int sectors, int enough)
        {
            int coverage = 0;
            if (sorted.Length == 0) return coverage;
            int ring = Mathf.CeilToInt((reach + Slack) / size) + 1;
            int bx = Mathf.FloorToInt((c.x - minX) / size);
            int by = Mathf.FloorToInt((c.y - minY) / size);
            int x0 = Mathf.Max(0, bx - ring);
            int x1 = Mathf.Min(columns - 1, bx + ring);
            int y0 = Mathf.Max(0, by - ring);
            int y1 = Mathf.Min(rows - 1, by + ring);
            for (int y = y0; y <= y1; y++)
            {
                for (int x = x0; x <= x1; x++)
                {
                    int cell = y * columns + x;
                    for (int i = start[cell]; i < start[cell + 1]; i++)
                    {
                        Vector2 d = sorted[i] - c;
                        if (d.sqrMagnitude > reach * reach) continue;
                        int sector = (int)((Mathf.Atan2(d.y, d.x) + Mathf.PI) / (2f * Mathf.PI) * sectors) % sectors;
                        if (covered[sector]) continue;
                        covered[sector] = true;
                        if (++coverage >= enough) return coverage;
                    }
                }
            }
            return coverage;
        }
    }

    private static bool CaptureHandGrip(
        Transform hand, Transform indexTip, Transform middleTip, Transform ringTip,
        Transform pinkyTip, out Vector3 hollowLocal, out Vector3 knuckleAxisLocal,
        out Vector3 lengthLocal)
    {
        hollowLocal = Vector3.zero;
        knuckleAxisLocal = Vector3.right;
        lengthLocal = Vector3.forward;
        Transform[] tips = { indexTip, middleTip, ringTip, pinkyTip };
        Vector3 sum = Vector3.zero;
        Vector3 knuckles = Vector3.zero;
        for (int i = 0; i < tips.Length; i++)
        {
            Transform tip = tips[i];
            if (hand == null || tip == null || tip.parent == null || tip.parent.parent == null)
            {
                return false;
            }
            Transform middle = tip.parent;
            Transform root = middle.parent;
            // The last phalanx has no child; extend it by most of the middle
            // phalanx length to reach the fingertip.
            Vector3 end = tip.position + (tip.position - middle.position) * 0.8f;
            sum += root.position + middle.position + tip.position + end;
            knuckles += root.position;
        }
        // The joints of a curled finger enclose the hollow the bar sits in.
        hollowLocal = hand.InverseTransformPoint(sum / 16f);
        Vector3 knuckleCentre = knuckles / 4f;
        Vector3 across = tips[3].parent.parent.position - tips[0].parent.parent.position;
        knuckleAxisLocal = hand.InverseTransformDirection(across).normalized;
        lengthLocal = hand.InverseTransformDirection(knuckleCentre - hand.position).normalized;
        return across.sqrMagnitude > 1e-6f && lengthLocal.sqrMagnitude > 0.5f;
    }

    // Hook grip on the heavy members' scanned hands: palm skin in front of
    // the knuckle joints and the finger's half thickness around its bones.
    private const float PalmHalfThickness = 0.032f;
    internal const float HookFingerHalfThickness = 0.02f;
    private const float HookMinFingerFlex = 40f;

    // Bar position for a hook grip, in hand space: one bar radius plus the
    // palm's half thickness from the finger roots, on the palm side (the
    // side facing the lifter), slightly towards the fingers.
    private Transform leftThumbTip;
    private Transform rightThumbTip;

    // Deepest "thumb" bone under the hand (Rigify DEF-thumb.03.L / Mixamo
    // ...HandThumb3), found once.
    private static Transform FindThumbTip(Transform hand, ref Transform cached)
    {
        if (cached != null && cached.IsChildOf(hand))
        {
            return cached;
        }
        cached = null;
        if (hand == null)
        {
            return null;
        }
        int bestDepth = -1;
        foreach (Transform bone in hand.GetComponentsInChildren<Transform>(true))
        {
            if (bone.name.IndexOf("thumb", System.StringComparison.OrdinalIgnoreCase) < 0 ||
                bone.name.IndexOf("end", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                continue;
            }
            int depth = 0;
            for (Transform t = bone; t != null && t != hand; t = t.parent) depth++;
            if (depth > bestDepth)
            {
                bestDepth = depth;
                cached = bone;
            }
        }
        return cached;
    }

    // Verification only: the old authored-hollow grip, for a baseline.
    internal static bool HookGripDisabledForVerification;

    private EnemyFighter hookPalmOwner;
    private float leftHookPalmSign;
    private float rightHookPalmSign;

    private Vector3 HookHollowLocal(bool left, Vector3 handUp)
    {
        Transform hand = left ? leftHand : rightHand;
        Transform[] tips = left
            ? new[] { leftIndexTip, leftMiddleTip, leftRingTip, leftPinkyTip }
            : new[] { rightIndexTip, rightMiddleTip, rightRingTip, rightPinkyTip };
        Vector3 knuckleAxisLocal = left ? leftKnuckleAxisLocal : rightKnuckleAxisLocal;
        Vector3 lengthLocal = left ? leftHandLengthLocal : rightHandLengthLocal;
        Vector3 fallback = left ? leftGripHollowLocal : rightGripHollowLocal;
        if (hand == null || leftUpperArm == null || rightUpperArm == null)
        {
            return fallback;
        }
        Vector3 roots = Vector3.zero;
        int count = 0;
        for (int i = 0; i < tips.Length; i++)
        {
            if (tips[i] != null && tips[i].parent != null && tips[i].parent.parent != null)
            {
                roots += tips[i].parent.parent.position;
                count++;
            }
        }
        if (count == 0)
        {
            return fallback;
        }
        Vector3 axis = GetBarAxis();
        Vector3 shoulder = left ? leftUpperArm.position : rightUpperArm.position;
        Vector3 middle = (leftUpperArm.position + rightUpperArm.position) * 0.5f;
        Vector3 outward = Vector3.Dot(shoulder - middle, axis) < 0f ? -axis : axis;
        Vector3 rootsLocal = hand.InverseTransformPoint(roots / count);
        Vector3 palmLocal = Vector3.Cross(lengthLocal, knuckleAxisLocal).normalized;
        // The palm side is a property of the hand: decide it once (palm
        // towards the lifter with the arm hanging) so the bar never jumps
        // to the other side of the hand when the solve direction changes.
        if (hookPalmOwner != owner)
        {
            hookPalmOwner = owner;
            leftHookPalmSign = 0f;
            rightHookPalmSign = 0f;
        }
        float sign = left ? leftHookPalmSign : rightHookPalmSign;
        if (sign == 0f)
        {
            Quaternion hanging = HandRotationOnBar(outward, Vector3.down, knuckleAxisLocal, lengthLocal);
            sign = Vector3.Dot(hanging * palmLocal, GetBarBackDirection()) < 0f ? -1f : 1f;
            if (left) leftHookPalmSign = sign; else rightHookPalmSign = sign;
        }
        palmLocal *= sign;
        float scale = Mathf.Max(0.0001f, hand.lossyScale.x);
        float offset = (CurrentBarShaftRadius + PalmHalfThickness) / scale;
        return rootsLocal + palmLocal * offset + lengthLocal * (0.012f / scale);
    }

    private static Quaternion HandRotationOnBar(
        Vector3 outward, Vector3 handUp, Vector3 knuckleAxisLocal, Vector3 lengthLocal)
    {
        Quaternion localFrame = Quaternion.LookRotation(lengthLocal, knuckleAxisLocal);
        Quaternion worldFrame = Quaternion.LookRotation(
            Vector3.ProjectOnPlane(handUp, outward), outward);
        return worldFrame * Quaternion.Inverse(localFrame);
    }

    private static void SolveHandOnBar(
        Transform upper, Transform fore, Transform hand, Vector3 grip, Vector3 outward,
        Vector3 handUp, Vector3 hollowLocal, Vector3 knuckleAxisLocal, Vector3 lengthLocal)
    {
        // Index-to-pinky runs outward along the bar, wrist-to-knuckles points
        // up and slightly back: for a real hand that puts the palm on the back
        // of the bar and the fingers over its top.
        Quaternion localFrame = Quaternion.LookRotation(lengthLocal, knuckleAxisLocal);
        Quaternion worldFrame = Quaternion.LookRotation(
            Vector3.ProjectOnPlane(handUp, outward), outward);
        Quaternion handRotation = worldFrame * Quaternion.Inverse(localFrame);
        Vector3 hollowOffset = handRotation * Vector3.Scale(hollowLocal, hand.lossyScale);
        SolveTwoBoneArm(upper, fore, hand, grip - hollowOffset);
        hand.rotation = handRotation;
    }

    private static void SolveTwoBoneArm(
        Transform upper, Transform fore, Transform end, Vector3 target)
    {
        Vector3 a = upper.position;
        Vector3 b = fore.position;
        Vector3 c = end.position;
        float l1 = Vector3.Distance(a, b);
        float l2 = Vector3.Distance(b, c);
        Vector3 toTarget = target - a;
        if (l1 < 0.001f || l2 < 0.001f || toTarget.sqrMagnitude < 1e-6f)
        {
            return;
        }
        float d = Mathf.Clamp(toTarget.magnitude, Mathf.Abs(l1 - l2) + 0.001f, l1 + l2 - 0.001f);
        Vector3 dir = toTarget.normalized;
        // Bend in the authored elbow plane so every rig keeps its own arm.
        Vector3 pole = Vector3.ProjectOnPlane(b - a, dir);
        if (pole.sqrMagnitude < 1e-6f)
        {
            pole = Vector3.ProjectOnPlane(Vector3.down, dir);
        }
        pole.Normalize();
        float cosA = Mathf.Clamp((l1 * l1 + d * d - l2 * l2) / (2f * l1 * d), -1f, 1f);
        float sinA = Mathf.Sqrt(Mathf.Max(0f, 1f - cosA * cosA));
        Vector3 elbow = a + dir * (l1 * cosA) + pole * (l1 * sinA);
        upper.rotation = Quaternion.FromToRotation(b - a, elbow - a) * upper.rotation;
        Vector3 b2 = fore.position;
        Vector3 c2 = end.position;
        fore.rotation = Quaternion.FromToRotation(c2 - b2, a + dir * d - b2) * fore.rotation;
    }

    private float GetFloorY(EnemyFighter fighter)
    {
        return fighter != null
            ? fighter.transform.position.y + EnemyFighter.GroundedVisualClearance
            : 0f;
    }
    private float GetFootSoleError(
        Transform foot, Transform toe, float expectedFootToSoleOffset)
    {
        if (foot == null || owner == null)
        {
            return float.PositiveInfinity;
        }

        float lowestFootBoneY = GetLowestFootBoneY(foot, toe);
        // Compare the foot/toe relationship against its own imported bind
        // pose. The absolute renderer minimum is affected by the model-child
        // settling pass, while this relative measurement catches the actual
        // failure here: a rotated foot bone changing the shoe's sole shape or
        // tilt during the squat.
        return Mathf.Abs(
            (lowestFootBoneY - foot.position.y) - expectedFootToSoleOffset);
    }

    private Vector3 GetGroundedSoleOffsetWorld(
        Transform foot, Vector3 anchorLocal, Vector3 worldUp)
    {
        if (foot == null)
        {
            return Vector3.zero;
        }

        Vector3 anchorOffset = foot.TransformVector(anchorLocal);
        if (anchorOffset.sqrMagnitude < 0.000001f)
        {
            return Vector3.zero;
        }

        // Imported foot bind axes can point down or forward. Keep the
        // authored horizontal sole offset for stable stance/slip metrics, but
        // make the support-facing depth at least the full anchor distance so
        // an upside-down bind orientation cannot make the ankle target
        // unreachable above the floor.
        if (Vector3.Dot(anchorOffset, worldUp) > 0f)
        {
            anchorOffset = -anchorOffset;
        }

        Vector3 horizontal = Vector3.ProjectOnPlane(anchorOffset, worldUp);
        float supportDepth = anchorOffset.magnitude;
        return horizontal - worldUp * supportDepth;
    }

    private Vector3 GetSoleAnchorWorld(
        Transform foot, Vector3 anchorLocal)
    {
        if (foot == null)
        {
            return Vector3.zero;
        }

        Vector3 up = owner != null && owner.transform.up.sqrMagnitude > 0.0001f
            ? owner.transform.up.normalized
            : Vector3.up;
        Vector3 anchorOffset =
            foot == leftFoot && currentLeftSoleOffsetWorld.sqrMagnitude > 0.000001f
                ? currentLeftSoleOffsetWorld
                : foot == rightFoot && currentRightSoleOffsetWorld.sqrMagnitude > 0.000001f
                    ? currentRightSoleOffsetWorld
                    : GetGroundedSoleOffsetWorld(foot, anchorLocal, up);
        return foot.position + anchorOffset;
    }

    private float GetFootPlantGroundError(
        Transform foot, Vector3 anchorLocal)
    {
        if (foot == null || owner == null)
        {
            return float.PositiveInfinity;
        }

        return Mathf.Abs(
            GetSoleAnchorWorld(foot, anchorLocal).y - GetFloorY(owner));
    }

    private float GetFixedFootGroundError(
        Transform foot, Vector3 fixedSoleAnchorLocal, float fixedSoleOffset)
    {
        if (foot == null || owner == null ||
            float.IsNaN(fixedSoleOffset) || float.IsInfinity(fixedSoleOffset))
        {
            return float.PositiveInfinity;
        }

        Vector3 soleAnchorLocal = fixedSoleAnchorLocal;
        if (soleAnchorLocal.sqrMagnitude < 0.000001f)
        {
            soleAnchorLocal = new Vector3(0f, fixedSoleOffset, 0f);
        }

        return Mathf.Abs(GetFixedFootGroundSigned(
            foot, soleAnchorLocal, fixedSoleOffset));
    }

    private float GetFixedFootGroundSigned(
        Transform foot, Vector3 fixedSoleAnchorLocal, float fixedSoleOffset)
    {
        if (foot == null || owner == null ||
            float.IsNaN(fixedSoleOffset) || float.IsInfinity(fixedSoleOffset))
        {
            return float.NaN;
        }

        Vector3 soleAnchorLocal = fixedSoleAnchorLocal;
        if (soleAnchorLocal.sqrMagnitude < 0.000001f)
        {
            soleAnchorLocal = new Vector3(0f, fixedSoleOffset, 0f);
        }

        // This is the bind-pose mesh sole anchor transformed by the current
        // foot rotation. It intentionally does not use the per-frame IK
        // reach extension, so the contact oracle remains independent of the
        // endpoint solver while still respecting the foot's real orientation.
        return foot.TransformPoint(soleAnchorLocal).y - GetFloorY(owner);
    }

    private void UpdateFixedFootGroundErrors()
    {
        currentLeftFixedFootGroundError = GetFixedFootGroundError(
            leftFoot, baseLeftSoleAnchorLocal, leftFootMeshSoleOffset);
        currentRightFixedFootGroundError = GetFixedFootGroundError(
            rightFoot, baseRightSoleAnchorLocal, rightFootMeshSoleOffset);
    }

    private void CaptureAuthoredSquatTargets()
    {
        if (owner == null || leftFoot == null || rightFoot == null)
        {
            return;
        }

        // Capture after MixamoScanRetargetAnimator has sampled squat.fbx at
        // phase zero. The idle capture in Begin() is only a fallback: the
        // authored squat can start from a different, fully valid foot stance.
        // From this point onward these world-space sole contacts stay fixed
        // for the complete repetition.
        Vector3 up = owner.transform.up.sqrMagnitude > 0.0001f
            ? owner.transform.up.normalized
            : Vector3.up;
        float floorY = GetFloorY(owner);
        Vector3 leftTarget = leftFoot.TransformPoint(baseLeftSoleAnchorLocal);
        Vector3 rightTarget = rightFoot.TransformPoint(baseRightSoleAnchorLocal);
        leftTarget.y = floorY;
        rightTarget.y = floorY;
        baseSquatLeftSoleTarget = leftTarget;
        baseSquatRightSoleTarget = rightTarget;
        squatLeftSoleTarget = leftTarget;
        squatRightSoleTarget = rightTarget;
        authoredSquatTargetsCaptured = true;
    }
    private static float GetLowestFootBoneY(Transform foot, Transform toe)
    {
        if (foot == null)
        {
            return float.PositiveInfinity;
        }

        return toe != null
            ? Mathf.Min(foot.position.y, toe.position.y)
            : foot.position.y;
    }

    private float GetBoneSideSign(Vector3 bonePosition, float fallback)
    {
        // CaptureBasePose runs before Begin assigns the active workout owner.
        // Resolve the fighter from this controller as well, otherwise scans
        // whose imported root is mirrored silently fall back to the wrong
        // left/right sign and produce an inside-out arm/leg pose.
        EnemyFighter sideOwner = owner != null
            ? owner
            : GetComponent<EnemyFighter>();
        if (sideOwner == null)
        {
            return fallback;
        }

        Vector3 sideAxis = Vector3.ProjectOnPlane(
            sideOwner.transform.right, sideOwner.transform.up);
        if (sideAxis.sqrMagnitude < 0.0001f)
        {
            return fallback;
        }

        float signedOffset = Vector3.Dot(
            bonePosition - baseHipsWorldPosition, sideAxis.normalized);
        return Mathf.Abs(signedOffset) > 0.03f ? Mathf.Sign(signedOffset) : fallback;
    }

    private float GetKneeBend(Transform thigh, Transform shin, Transform foot)
    {
        if (thigh == null || shin == null || foot == null)
        {
            return 0f;
        }

        Vector3 towardHip = thigh.position - shin.position;
        Vector3 towardFoot = foot.position - shin.position;
        if (towardHip.sqrMagnitude < 0.0001f || towardFoot.sqrMagnitude < 0.0001f)
        {
            return 0f;
        }

        return 180f - Vector3.Angle(towardHip, towardFoot);
    }

    private Vector3 CalculateBarTargetPosition(EnemyFighter fighter)
    {
        Transform traps = Traps;
        if (fighter == null || traps == null)
        {
            return traps != null ? traps.position : Vector3.zero;
        }

        bool hasShoulders = leftShoulder != null && rightShoulder != null;
        // On the authored DEF hierarchy the chest bone is several centimetres
        // below the upper-traps line. Use the animated shoulder midpoint as
        // the anatomical traps anchor when it exists, and keep chest as the
        // per-rig fallback for skeletons without shoulder helper bones.
        Vector3 trapsPosition = hasShoulders
            ? (leftShoulder.position + rightShoulder.position) * 0.5f
            : traps.position;
        Vector3 up = fighter.transform.up;
        Vector3 neckPosition = neck != null
            ? neck.position
            : trapsPosition + up * 0.12f;
        float trapsToNeck = Vector3.Distance(trapsPosition, neckPosition);
        // The shaft rests on the upper traps, in the short anatomical span
        // between the traps bone and the neck. Sampling these authored bones
        // after every clip frame keeps the bar in place through the complete
        // standing -> squat -> standing movement for every rig.
        Vector3 target = Vector3.Lerp(trapsPosition, neckPosition, 0.24f);
        target -= up * Mathf.Clamp(trapsToNeck * 0.04f, 0.012f, 0.035f);

        // The bar belongs on the upper back, between the neck and shoulder
        // blades. Use each model's shoulder width to scale the rear offset so
        // Cbum, Arnold, Jay and the other body proportions do not share one
        // visibly wrong fixed distance from the neck.
        float shoulderWidth = hasShoulders
            ? Vector3.Distance(leftShoulder.position, rightShoulder.position)
            : trapsToNeck * 3.2f;
        float rearOffset = Mathf.Clamp(shoulderWidth * 0.16f, 0.055f, 0.14f);
        Vector3 horizontalForward = Vector3.ProjectOnPlane(
            fighter.transform.forward, up);
        if (horizontalForward.sqrMagnitude < 0.0001f)
        {
            horizontalForward = Vector3.ProjectOnPlane(Vector3.forward, up);
        }
        target -= horizontalForward.normalized * rearOffset;
        return target;
    }

    private void CaptureBasePose(EnemyFighter fighter)
    {
        baseHipsLocalRotation = hips.localRotation;
        baseHipsLocalPosition = hips.localPosition;
        baseHipsWorldPosition = hips.position;
        baseHipsWorldRotation = hips.rotation;
        baseSpineWorldRotation = spine.rotation;
        baseChestWorldRotation = chest.rotation;
        baseSpineLocalRotation = spine.localRotation;
        baseChestLocalRotation = chest.localRotation;
        baseLeftShoulderLocalRotation = leftShoulder.localRotation;
        baseLeftUpperArmLocalRotation = leftUpperArm.localRotation;
        baseLeftForearmLocalRotation = leftForearm.localRotation;
        baseLeftHandLocalRotation = leftHand.localRotation;
        baseRightShoulderLocalRotation = rightShoulder.localRotation;
        baseRightUpperArmLocalRotation = rightUpperArm.localRotation;
        baseRightForearmLocalRotation = rightForearm.localRotation;
        baseRightHandLocalRotation = rightHand.localRotation;
        baseLeftHipPosition = leftThigh.position;
        baseRightHipPosition = rightThigh.position;
        baseLeftThighLocalPosition = leftThigh.localPosition;
        baseRightThighLocalPosition = rightThigh.localPosition;
        baseLeftKneePosition = leftShin.position;
        baseRightKneePosition = rightShin.position;
        baseLeftFootPosition = leftFoot.position;
        baseRightFootPosition = rightFoot.position;
        baseLeftFootLocalPosition = leftFoot.localPosition;
        baseRightFootLocalPosition = rightFoot.localPosition;
        float floorY = GetFloorY(fighter);
        // Capture each asset's authored foot-to-toe relationship for the
        // deformation check and the mesh-based contact fallback.
        leftFootBoneToSoleOffset =
            GetLowestFootBoneY(leftFoot, leftToe) - baseLeftFootPosition.y;
        rightFootBoneToSoleOffset =
            GetLowestFootBoneY(rightFoot, rightToe) - baseRightFootPosition.y;
        PrepareFootSoleCalibration();
        hasMeshFootSoleCalibration = hasCachedMeshFootSoleCalibration;
        if (hasMeshFootSoleCalibration)
        {
            leftFootMeshSoleOffset = cachedLeftFootMeshSoleOffset;
            rightFootMeshSoleOffset = cachedRightFootMeshSoleOffset;
        }
        else
        {
            leftFootMeshSoleOffset = leftFootBoneToSoleOffset;
            rightFootMeshSoleOffset = rightFootBoneToSoleOffset;
        }
        Transform leftSoleAnchor = leftToe != null &&
            leftToe.position.y < leftFoot.position.y ? leftToe : leftFoot;
        Transform rightSoleAnchor = rightToe != null &&
            rightToe.position.y < rightFoot.position.y ? rightToe : rightFoot;
        baseLeftSoleAnchorLocal =
            leftFoot.InverseTransformPoint(leftSoleAnchor.position);
        baseRightSoleAnchorLocal =
            rightFoot.InverseTransformPoint(rightSoleAnchor.position);
        if (hasMeshFootSoleCalibration)
        {
            // Convert the measured bind-pose mesh contact point into each
            // foot's local frame. Replacing only local Y with a world-space
            // scalar loses the contact point when the authored foot rotates.
            baseLeftSoleAnchorLocal = leftFoot.InverseTransformPoint(
                baseLeftFootPosition + Vector3.up * leftFootMeshSoleOffset);
            baseRightSoleAnchorLocal = rightFoot.InverseTransformPoint(
                baseRightFootPosition + Vector3.up * rightFootMeshSoleOffset);
        }

        // The imported FBX foot pivots can sit well above the visible shoe
        // sole. ExternalRiggedCharacterVisual already measures the renderer
        // ground contact against the rig foot bones; convert that world-space
        // offset into each foot's local anchor so IK targets the shoe, not the
        // ankle pivot. This does not alter the rig, scale or limb lengths.
        ExternalRiggedCharacterVisual externalVisual =
            fighter.GetComponent<ExternalRiggedCharacterVisual>();
        if (!hasMeshFootSoleCalibration && externalVisual != null &&
            externalVisual.HasGroundedFootReference)
        {
            float visualSoleOffset = externalVisual.GroundedFootToMeshOffset;
            if (!float.IsNaN(visualSoleOffset) &&
                !float.IsInfinity(visualSoleOffset) &&
                Mathf.Abs(visualSoleOffset) < 1.0f)
            {
                Vector3 supportDelta = fighter.transform.up.normalized *
                    visualSoleOffset;
                baseLeftSoleAnchorLocal = leftFoot.InverseTransformPoint(
                    baseLeftFootPosition + supportDelta);
                baseRightSoleAnchorLocal = rightFoot.InverseTransformPoint(
                    baseRightFootPosition + supportDelta);
            }
        }

        // The visible sole, not the ankle pivot, is the support point. The
        // idle clip can capture the two feet at slightly different phases,
        // so do not copy that transient stagger into the squat leg targets.
        // Use the per-enemy average sole offset for both ankles: the feet
        // remain planted within the shoe geometry tolerance while the two
        // knees solve from one level support plane and stay symmetrical.
        float commonFootSoleOffset =
            (leftFootMeshSoleOffset + rightFootMeshSoleOffset) * 0.5f;
        leftFootRootOffsetY = -commonFootSoleOffset;
        rightFootRootOffsetY = -commonFootSoleOffset;
        Vector3 sideAxis = Vector3.ProjectOnPlane(
            fighter.transform.right, fighter.transform.up);
        if (sideAxis.sqrMagnitude < 0.0001f)
        {
            sideAxis = Vector3.right;
        }
        sideAxis.Normalize();
        Vector3 forwardAxis = Vector3.ProjectOnPlane(
            fighter.transform.forward, fighter.transform.up);
        if (forwardAxis.sqrMagnitude < 0.0001f)
        {
            forwardAxis = Vector3.forward;
        }
        forwardAxis.Normalize();

        // Preserve the authored bind-pose stance. A scan's foot pivots and
        // mesh soles are not guaranteed to share the same centreline, so
        // synthesizing a symmetric target can create equal-and-opposite
        // horizontal slip on the two fixed sole anchors. The calibrated
        // bind-pose anchors are the stable per-rig contact reference; only
        // their vertical component is replaced by the station floor below.
        leftLegSideSign = GetBoneSideSign(baseLeftFootPosition, -1f);
        rightLegSideSign = GetBoneSideSign(baseRightFootPosition, 1f);
        if (Mathf.Sign(leftLegSideSign) == Mathf.Sign(rightLegSideSign))
        {
            rightLegSideSign = -leftLegSideSign;
        }
        Vector3 hipJointCenter = (baseLeftHipPosition + baseRightHipPosition) * 0.5f;
        Vector3 authoredLeftSole = leftFoot.TransformPoint(
            baseLeftSoleAnchorLocal);
        Vector3 authoredRightSole = rightFoot.TransformPoint(
            baseRightSoleAnchorLocal);
        Vector3 authoredSoleCenter =
            (authoredLeftSole + authoredRightSole) * 0.5f;
        float authoredHalfWidth = Mathf.Abs(Vector3.Dot(
            authoredLeftSole - authoredSoleCenter, sideAxis));
        float hipWidth = Vector3.Distance(
            baseLeftHipPosition, baseRightHipPosition);
        float stanceHalfWidth = Mathf.Clamp(
            authoredHalfWidth * 0.72f,
            0.07f,
            Mathf.Max(0.09f, hipWidth * 0.60f));
        // Keep the authored fore/aft centre as well. It is part of the
        // calibrated sole reference and is more reliable than a universal
        // hip-centred correction for scans with a deep source stance.
        Vector3 groundedSoleCenter = authoredSoleCenter;
        groundedSoleCenter.y = floorY;
        squatLeftSoleTarget = groundedSoleCenter +
            sideAxis * leftLegSideSign * stanceHalfWidth;
        squatRightSoleTarget = groundedSoleCenter +
            sideAxis * rightLegSideSign * stanceHalfWidth;
        squatLeftFootTarget = squatLeftSoleTarget;
        squatRightFootTarget = squatRightSoleTarget;
        squatLeftFootTarget.y = floorY + leftFootRootOffsetY;
        squatRightFootTarget.y = floorY + rightFootRootOffsetY;
        baseSquatLeftSoleTarget = squatLeftSoleTarget;
        baseSquatRightSoleTarget = squatRightSoleTarget;
        baseFighterPosition = fighter.transform.position;

        float leftHipHalfWidth = Mathf.Abs(Vector3.Dot(
            baseLeftHipPosition - hipJointCenter, sideAxis));
        float rightHipHalfWidth = Mathf.Abs(Vector3.Dot(
            baseRightHipPosition - hipJointCenter, sideAxis));
        float hipHalfWidth = Mathf.Max(
            (leftHipHalfWidth + rightHipHalfWidth) * 0.5f, 0.06f);
        Vector3 normalizedLeftHip = hipJointCenter +
            sideAxis * leftLegSideSign * hipHalfWidth;
        Vector3 normalizedRightHip = hipJointCenter +
            sideAxis * rightLegSideSign * hipHalfWidth;
        squatLeftThighOffsetFromHips = Quaternion.Inverse(baseHipsWorldRotation) *
            (normalizedLeftHip - baseHipsWorldPosition);
        squatRightThighOffsetFromHips = Quaternion.Inverse(baseHipsWorldRotation) *
            (normalizedRightHip - baseHipsWorldPosition);

        Vector3 defaultKneePole = forwardAxis;
        Vector3 leftKneeBend = baseLeftKneePosition -
            (baseLeftHipPosition + baseLeftFootPosition) * 0.5f;
        Vector3 rightKneeBend = baseRightKneePosition -
            (baseRightHipPosition + baseRightFootPosition) * 0.5f;
        Vector3 observedKneeBend = Vector3.ProjectOnPlane(
            leftKneeBend + rightKneeBend, fighter.transform.up);
        if (defaultKneePole.sqrMagnitude > 0.0001f &&
            observedKneeBend.sqrMagnitude > 0.0001f &&
            Vector3.Dot(defaultKneePole, observedKneeBend) < 0f)
        {
            defaultKneePole = -defaultKneePole;
        }
        squatKneePole = defaultKneePole.sqrMagnitude > 0.0001f
            ? defaultKneePole.normalized
            : fighter.transform.forward;
        baseLeftThighRotation = leftThigh.rotation;
        baseLeftThighLocalRotation = leftThigh.localRotation;
        baseRightThighRotation = rightThigh.rotation;
        baseRightThighLocalRotation = rightThigh.localRotation;
        baseLeftShinRotation = leftShin.rotation;
        baseLeftShinLocalRotation = leftShin.localRotation;
        baseRightShinRotation = rightShin.rotation;
        baseRightShinLocalRotation = rightShin.localRotation;
        baseLeftFootRotation = leftFoot.rotation;
        baseLeftFootLocalRotation = leftFoot.localRotation;
        baseLeftToeLocalRotation = leftToe != null
            ? leftToe.localRotation
            : Quaternion.identity;
        baseRightFootRotation = rightFoot.rotation;
        baseRightFootLocalRotation = rightFoot.localRotation;
        baseRightToeLocalRotation = rightToe != null
            ? rightToe.localRotation
            : Quaternion.identity;
        leftUpperLegLength = Vector3.Distance(baseLeftHipPosition, baseLeftKneePosition);
        rightUpperLegLength = Vector3.Distance(baseRightHipPosition, baseRightKneePosition);
        leftLowerLegLength = Vector3.Distance(baseLeftKneePosition, baseLeftFootPosition);
        rightLowerLegLength = Vector3.Distance(baseRightKneePosition, baseRightFootPosition);
        float shortestLeg = Mathf.Min(
            leftUpperLegLength + leftLowerLegLength,
            rightUpperLegLength + rightLowerLegLength);
        // A readable squat needs the hips to travel through the legs, not
        // only a small chest lean. Scale from the shorter side so every rig
        // stays within its own reach while reaching a visibly deep position.
        maxHipDrop = Mathf.Clamp(shortestLeg * 0.52f, 0.40f, 0.50f);
        leftUpperArmLength = Vector3.Distance(
            baseLeftArmAnchorPosition = leftUpperArm.position,
            baseLeftElbowPosition = leftForearm.position);
        rightUpperArmLength = Vector3.Distance(
            baseRightArmAnchorPosition = rightUpperArm.position,
            baseRightElbowPosition = rightForearm.position);
        leftLowerArmLength = Vector3.Distance(
            baseLeftElbowPosition,
            baseLeftHandPosition = leftHand.position);
        rightLowerArmLength = Vector3.Distance(
            baseRightElbowPosition,
            baseRightHandPosition = rightHand.position);
        baseLeftHandRelativeToForearm = Quaternion.Inverse(leftForearm.rotation) *
            leftHand.rotation;
        baseRightHandRelativeToForearm = Quaternion.Inverse(rightForearm.rotation) *
            rightHand.rotation;
        baseLeftHandContactOffsetLocal = CaptureHandContactOffset(
            leftHand,
            leftIndexTip,
            leftMiddleTip,
            leftRingTip,
            leftPinkyTip);
        baseRightHandContactOffsetLocal = CaptureHandContactOffset(
            rightHand,
            rightIndexTip,
            rightMiddleTip,
            rightRingTip,
            rightPinkyTip);
        leftHandContactReach = Mathf.Clamp(
            leftHand.TransformVector(baseLeftHandContactOffsetLocal).magnitude,
            0.035f,
            0.24f);
        rightHandContactReach = Mathf.Clamp(
            rightHand.TransformVector(baseRightHandContactOffsetLocal).magnitude,
            0.035f,
            0.24f);
        // The upper-arm roots are the stable span reference across the
        // imported scans. Some stylized shoulder helper joints are nearly
        // coincident with the chest, so using those helper points would make
        // the bar grip collapse even though the actual arm chains are wide.
        armSpanReference = Vector3.Distance(
            baseLeftArmAnchorPosition, baseRightArmAnchorPosition);
        if (fighter.Identity == BodybuilderIdentity.Goku)
        {
            // Goku's upper-arm roots are authored very close to the torso,
            // while the elbow joints show the intended shoulder/arm width.
            // Use a restrained fraction of that elbow span for the grip
            // reference; it corrects the limb relationship without creating
            // the overly wide squat stance the reference does not have.
            armSpanReference = Mathf.Max(
                armSpanReference,
                Vector3.Distance(baseLeftElbowPosition, baseRightElbowPosition) *
                    0.70f);
        }
        leftArmSideSign = GetBoneSideSign(baseLeftArmAnchorPosition, -1f);
        rightArmSideSign = GetBoneSideSign(baseRightArmAnchorPosition, 1f);
        if (Mathf.Sign(leftArmSideSign) == Mathf.Sign(rightArmSideSign))
        {
            rightArmSideSign = -leftArmSideSign;
        }
        basePoseCaptured = true;
    }

    private void PrepareFootSoleCalibration()
    {
        if (footSoleCalibrationPrepared || leftFoot == null || rightFoot == null)
        {
            return;
        }

        // Bake the visible shoe mesh while the visitor/controller is being
        // created, never on the frame that hands the visitor to the squat
        // solver. The result is an offset from each ankle, so it remains
        // valid when the visitor later moves to a rack with a yaw-only pose.
        footSoleCalibrationPrepared = true;
        hasCachedMeshFootSoleCalibration = TryCaptureMeshFootSoleOffsets(
            leftFoot,
            rightFoot,
            out cachedLeftFootMeshSoleOffset,
            out cachedRightFootMeshSoleOffset);
    }

    private bool TryCaptureMeshFootSoleOffsets(
        Transform leftFootBone,
        Transform rightFootBone,
        out float leftOffset,
        out float rightOffset)
    {
        leftOffset = float.NaN;
        rightOffset = float.NaN;
        if (leftFootBone == null || rightFootBone == null)
        {
            return false;
        }

        SkinnedMeshRenderer[] renderers =
            GetComponentsInChildren<SkinnedMeshRenderer>(true);
        SkinnedMeshRenderer renderer = null;
        int largestVertexCount = 0;
        for (int i = 0; i < renderers.Length; i++)
        {
            SkinnedMeshRenderer candidate = renderers[i];
            int vertexCount = candidate != null && candidate.sharedMesh != null
                ? candidate.sharedMesh.vertexCount
                : 0;
            bool candidateVisible = candidate != null && candidate.enabled &&
                !candidate.forceRenderingOff;
            bool currentVisible = renderer != null && renderer.enabled &&
                !renderer.forceRenderingOff;
            // The visible FBX and the hidden Mixamo motion copy can contain
            // the same mesh. Prefer the visible one when their vertex counts
            // match, because its current skin pose is the one that must stay
            // planted while the squat IK is evaluated.
            if (vertexCount > largestVertexCount ||
                (vertexCount == largestVertexCount && candidateVisible &&
                    !currentVisible))
            {
                renderer = candidate;
                largestVertexCount = vertexCount;
            }
        }

        if (renderer == null || renderer.sharedMesh == null ||
            renderer.bones == null || renderer.bones.Length == 0)
        {
            return false;
        }

        // Resolve the roots from the renderer's actual skin binding table.
        // The visible enemy and the hidden Mixamo motion source share bone
        // names, but they are different Transform instances. Comparing
        // against the controller's first hierarchy match can therefore make
        // every weighted foot vertex look unrelated and silently fall back to
        // the ankle/toe pivot, which is precisely how one shoe can float.
        Transform skinnedLeftFoot = FindBone(renderer.bones, "leftfoot");
        Transform skinnedRightFoot = FindBone(renderer.bones, "rightfoot");
        if (skinnedLeftFoot == null || skinnedRightFoot == null)
        {
            return false;
        }

        BoneWeight[] weights = renderer.sharedMesh.boneWeights;
        bool hasLegacyWeights = weights != null && weights.Length > 0;

        Mesh baked = new Mesh { name = "Squat foot contact calibration" };
        try
        {
            renderer.BakeMesh(baked, false);
            Vector3[] vertices = baked.vertices;
            float leftLowest = float.PositiveInfinity;
            float rightLowest = float.PositiveInfinity;
            if (hasLegacyWeights)
            {
                int vertexCount = Mathf.Min(vertices.Length, weights.Length);
                for (int i = 0; i < vertexCount; i++)
                {
                    BoneWeight weight = weights[i];
                    float leftWeight = GetChainWeight(
                        weight, renderer.bones, skinnedLeftFoot);
                    float rightWeight = GetChainWeight(
                        weight, renderer.bones, skinnedRightFoot);
                    Vector3 worldPosition = GetBakedVertexWorldPosition(
                        renderer, vertices[i]);
                    if (leftWeight >= 0.20f && leftWeight > rightWeight + 0.05f)
                    {
                        leftLowest = Mathf.Min(leftLowest, worldPosition.y);
                    }
                    else if (rightWeight >= 0.20f && rightWeight > leftWeight + 0.05f)
                    {
                        rightLowest = Mathf.Min(rightLowest, worldPosition.y);
                    }
                }
            }

            // The imported FBX meshes can expose no legacy BoneWeight array in
            // Unity 6 even though BakeMesh returns the correct visible body.
            // Always measure the actual shoe geometry as a fallback (and use
            // it when both sides are available). A foot-specific radius and
            // height gate excludes shins, shorts and the other shoe while
            // preserving different sole thicknesses for every enemy.
            float geometricLeftLowest = float.PositiveInfinity;
            float geometricRightLowest = float.PositiveInfinity;
            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 worldPosition = GetBakedVertexWorldPosition(
                    renderer, vertices[i]);
                float leftDistance = Vector3.ProjectOnPlane(
                    worldPosition - leftFootBone.position, Vector3.up).magnitude;
                float rightDistance = Vector3.ProjectOnPlane(
                    worldPosition - rightFootBone.position, Vector3.up).magnitude;
                if (leftDistance < 0.44f &&
                    leftDistance + 0.012f < rightDistance &&
                    worldPosition.y <= leftFootBone.position.y + 0.16f)
                {
                    geometricLeftLowest = Mathf.Min(
                        geometricLeftLowest, worldPosition.y);
                }
                if (rightDistance < 0.44f &&
                    rightDistance + 0.012f < leftDistance &&
                    worldPosition.y <= rightFootBone.position.y + 0.16f)
                {
                    geometricRightLowest = Mathf.Min(
                        geometricRightLowest, worldPosition.y);
                }
            }

            if (!float.IsInfinity(geometricLeftLowest) &&
                !float.IsInfinity(geometricRightLowest))
            {
                leftLowest = geometricLeftLowest;
                rightLowest = geometricRightLowest;
            }

            if (float.IsInfinity(leftLowest) || float.IsInfinity(rightLowest))
            {
                return false;
            }

            leftOffset = leftLowest - leftFootBone.position.y;
            rightOffset = rightLowest - rightFootBone.position.y;
            return Mathf.Abs(leftOffset) < 0.45f &&
                Mathf.Abs(rightOffset) < 0.45f;
        }
        finally
        {
            Destroy(baked);
        }
    }

    private static float GetChainWeight(
        BoneWeight weight,
        Transform[] bones,
        Transform chainRoot)
    {
        float total = 0f;
        total += GetBoneWeight(weight.boneIndex0, weight.weight0, bones, chainRoot);
        total += GetBoneWeight(weight.boneIndex1, weight.weight1, bones, chainRoot);
        total += GetBoneWeight(weight.boneIndex2, weight.weight2, bones, chainRoot);
        total += GetBoneWeight(weight.boneIndex3, weight.weight3, bones, chainRoot);
        return total;
    }

    private static Vector3 GetBakedVertexWorldPosition(
        SkinnedMeshRenderer renderer, Vector3 bakedVertex)
    {
        if (renderer == null)
        {
            return bakedVertex;
        }

        // BakeMesh(..., false) returns vertices without the renderer scale.
        // Apply only the world translation and rotation here; TransformPoint
        // would apply the imported armature scale for a second time and put
        // the shoe geometry far below the actual visible mesh.
        return renderer.transform.position +
            renderer.transform.rotation * bakedVertex;
    }

    private static float GetBoneWeight(
        int boneIndex,
        float weight,
        Transform[] bones,
        Transform chainRoot)
    {
        if (weight <= 0f || bones == null ||
            boneIndex < 0 || boneIndex >= bones.Length ||
            bones[boneIndex] == null || chainRoot == null)
        {
            return 0f;
        }

        Transform bone = bones[boneIndex];
        return bone == chainRoot || bone.IsChildOf(chainRoot) ? weight : 0f;
    }

    private void RestoreBasePose()
    {
        if (!basePoseCaptured)
        {
            return;
        }

        // The authored animation controller owns the skeleton. Do not write
        // captured limb transforms back into the enemy mesh on release; the
        // next animator sample restores its own idle pose and root.
        currentHipDrop = 0f;
        currentKneeBend = 0f;
        currentKneeBendDifference = 0f;
        currentLegDepthDifference = 0f;
        currentLeftFootSoleError = float.PositiveInfinity;
        currentRightFootSoleError = float.PositiveInfinity;
        currentLeftFootGroundError = float.PositiveInfinity;
        currentRightFootGroundError = float.PositiveInfinity;
        currentLeftFootRotationError = float.PositiveInfinity;
        currentRightFootRotationError = float.PositiveInfinity;
        currentBarBodyFollowError = float.PositiveInfinity;
        currentBarDropFromStart = 0f;
        basePoseCaptured = false;
    }
    private void ResolveBones()
    {
        Transform[] bones = GetComponentsInChildren<Transform>(true);
        // The authored exports use a clean DEF hierarchy, but every source
        // character reaches it from a different Rigify skeleton. Resolve
        // exact DEF names first and only then fall back to Mixamo aliases;
        // suffix-first matching would bind DEF-spine to both hips and spine.
        hips = FindBone(bones, "hips", "pelvis", "def-spine");
        spine = FindBone(bones, "spine", "def-spine.001");
        chest = FindBone(bones, "spine2", "spine1", "chest", "def-spine.003");
        neck = FindBone(bones, "neck", "def-spine.004");
        leftShoulder = FindBone(bones, "leftshoulder", "def-shoulder.l");
        leftUpperArm = FindBone(
            bones, "leftarm", "leftupperarm", "leftupper", "def-upper_arm.l");
        leftForearm = FindBone(
            bones, "leftforearm", "leftlowerarm", "def-forearm.l");
        leftHand = FindBone(bones, "lefthand", "def-hand.l");
        leftIndexTip = FindBone(
            bones, "lefthandindex3", "def-f_index.03.l");
        leftMiddleTip = FindBone(
            bones, "lefthandmiddle3", "def-f_middle.03.l");
        leftRingTip = FindBone(
            bones, "lefthandring3", "def-f_ring.03.l");
        leftPinkyTip = FindBone(
            bones, "lefthandpinky3", "def-f_pinky.03.l");
        rightShoulder = FindBone(bones, "rightshoulder", "def-shoulder.r");
        rightUpperArm = FindBone(
            bones, "rightarm", "rightupperarm", "rightupper", "def-upper_arm.r");
        rightForearm = FindBone(
            bones, "rightforearm", "rightlowerarm", "def-forearm.r");
        rightHand = FindBone(bones, "righthand", "def-hand.r");
        rightIndexTip = FindBone(
            bones, "righthandindex3", "def-f_index.03.r");
        rightMiddleTip = FindBone(
            bones, "righthandmiddle3", "def-f_middle.03.r");
        rightRingTip = FindBone(
            bones, "righthandring3", "def-f_ring.03.r");
        rightPinkyTip = FindBone(
            bones, "righthandpinky3", "def-f_pinky.03.r");
        leftThigh = FindBone(bones, "leftupleg", "leftthigh", "def-thigh.l");
        leftShin = FindBone(
            bones, "leftleg", "leftshin", "leftlowerleg", "def-shin.l");
        leftFoot = FindBone(bones, "leftfoot", "def-foot.l");
        leftToe = FindBone(bones, "lefttoebase", "lefttoe", "def-toe.l");
        if (leftToe == leftFoot ||
            (leftToe != null && leftFoot != null && !leftToe.IsChildOf(leftFoot)))
        {
            leftToe = null;
        }
        rightThigh = FindBone(bones, "rightupleg", "rightthigh", "def-thigh.r");
        rightShin = FindBone(
            bones, "rightleg", "rightshin", "rightlowerleg", "def-shin.r");
        rightFoot = FindBone(bones, "rightfoot", "def-foot.r");
        rightToe = FindBone(bones, "righttoebase", "righttoe", "def-toe.r");
        if (rightToe == rightFoot ||
            (rightToe != null && rightFoot != null && !rightToe.IsChildOf(rightFoot)))
        {
            rightToe = null;
        }
    }

    private void EnsureBonesResolved()
    {
        if (hips == null || leftThigh == null || leftShin == null ||
            leftFoot == null || rightThigh == null || rightShin == null ||
            rightFoot == null || leftHand == null || rightHand == null)
        {
            ResolveBones();
        }
    }

    private static bool HasValidLegChain(
        Transform thigh, Transform shin, Transform foot)
    {
        return thigh != null && shin != null && foot != null &&
            shin != thigh && foot != shin && foot != thigh &&
            shin.IsChildOf(thigh) && foot.IsChildOf(shin);
    }

    private static Transform FindBone(Transform[] bones, params string[] candidates)
    {
        for (int candidateIndex = 0; candidateIndex < candidates.Length; candidateIndex++)
        {
            string candidate = NormalizeBoneName(candidates[candidateIndex]);
            for (int i = 0; i < bones.Length; i++)
            {
                if (NormalizeBoneName(bones[i].name) == candidate)
                {
                    return bones[i];
                }
            }
        }

        for (int candidateIndex = 0; candidateIndex < candidates.Length; candidateIndex++)
        {
            string candidate = NormalizeBoneName(candidates[candidateIndex]);
            for (int i = 0; i < bones.Length; i++)
            {
                string normalized = NormalizeBoneName(bones[i].name);
                if (normalized.EndsWith(candidate))
                {
                    return bones[i];
                }
            }
        }

        return null;
    }

    private static Vector3 CaptureHandContactOffset(
        Transform hand, params Transform[] fingertipBones)
    {
        if (hand == null)
        {
            return Vector3.forward * 0.08f;
        }

        Vector3 average = Vector3.zero;
        int count = 0;
        for (int i = 0; i < fingertipBones.Length; i++)
        {
            Transform fingertip = fingertipBones[i];
            if (fingertip == null || fingertip == hand || !fingertip.IsChildOf(hand))
            {
                continue;
            }

            average += fingertip.position;
            count++;
        }

        if (count > 0)
        {
            average /= count;
            Vector3 offset = hand.InverseTransformPoint(average);
            if (offset.sqrMagnitude > 0.000001f)
            {
                return offset;
            }
        }

        // All current enemy FBXs expose Mixamo finger chains. Keep a safe
        // deterministic fallback for an authored/custom scan without them.
        // Build it in world units first, then convert to this bone's local
        // scale so TransformPoint/TransformVector preserve the 8 cm contact
        // reach on scaled imported models.
        return hand.InverseTransformVector(hand.forward * 0.08f);
    }

    private static string NormalizeBoneName(string value)
    {
        return value.ToLowerInvariant()
            .Replace("mixamorig:", string.Empty)
            .Replace("mixamorig", string.Empty)
            .Replace(" ", string.Empty)
            .Replace("_", string.Empty)
            .Replace("-", string.Empty);
    }

    private void OnDisable()
    {
        if (running)
        {
            Cancel();
        }
    }
}
