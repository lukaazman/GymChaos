using UnityEngine;

/// <summary>
/// Coordinates the gym squat rep with each visitor's authored squat clip.
/// The imported per-character animation remains authoritative for the
/// character pose; this component only follows the station bar and exposes
/// the gameplay-facing squat measurements.
/// </summary>
[DefaultExecutionOrder(1100)]
public sealed class SquatWorkoutController : MonoBehaviour
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
    private float hipBackTravelRatio = 0.42f;
    private float maximumHipBackTravel = 0.22f;
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
    private Transform activeArmContactHand;
    private Vector3 activeArmContactTarget;
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
    private float currentGripError = float.PositiveInfinity;
    private float currentLeftGripError = float.PositiveInfinity;
    private float currentRightGripError = float.PositiveInfinity;
    private float currentForearmOutwardError = float.PositiveInfinity;
    private float currentArmCrossingError;
    private float currentElbowOutwardError;
    private float currentUpperArmReferenceError = float.PositiveInfinity;
    private float currentForearmReferenceError = float.PositiveInfinity;
    private float currentArmShapeError = float.PositiveInfinity;
    private float currentLeftHandContactError = float.PositiveInfinity;
    private float currentRightHandContactError = float.PositiveInfinity;
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
    private bool hasPreviousLeftElbowPose;
    private bool hasPreviousRightElbowPose;
    private Vector3 previousLeftElbowDirection;
    private Vector3 previousRightElbowDirection;
    private bool footSoleCalibrationPrepared;
    private bool hasCachedMeshFootSoleCalibration;
    private float cachedLeftFootMeshSoleOffset;
    private float cachedRightFootMeshSoleOffset;

    public bool IsActive => running;
    public bool IsComplete => completed;
    public int Repetitions => repetitions;
    public Transform Traps => chest != null ? chest : hips;
    public float CurrentMotion { get; private set; }
    public float CurrentHipDrop => currentHipDrop;
    public float CurrentKneeBend => currentKneeBend;
    public float KneeBendDifference => currentKneeBendDifference;
    public float LegDepthDifference => currentLegDepthDifference;
    public float GripError => currentGripError;
    public float LeftGripError => currentLeftGripError;
    public float RightGripError => currentRightGripError;
    public float ForearmOutwardError => currentForearmOutwardError;
    public float ArmCrossingError => currentArmCrossingError;
    public float ElbowOutwardError => currentElbowOutwardError;
    public float UpperArmReferenceError => currentUpperArmReferenceError;
    public float ForearmReferenceError => currentForearmReferenceError;
    public float ArmShapeError => currentArmShapeError;
    public float LeftHandContactError => currentLeftHandContactError;
    public float RightHandContactError => currentRightHandContactError;
    public float HandContactError => Mathf.Max(
        currentLeftHandContactError, currentRightHandContactError);
    public float LeftFootSoleError => currentLeftFootSoleError;
    public float RightFootSoleError => currentRightFootSoleError;
    public float FootSoleError => Mathf.Max(
        currentLeftFootSoleError, currentRightFootSoleError);
    public float LeftFootGroundError => currentLeftFootGroundError;
    public float RightFootGroundError => currentRightFootGroundError;
    public float FootGroundError => Mathf.Max(
        currentLeftFootGroundError, currentRightFootGroundError);
    // Independent contact oracle: this uses the calibrated mesh/bind sole
    // offset directly and never uses currentLeftSoleOffsetWorld or the
    // reachability adjustment used by the IK endpoint solver.
    public float LeftFixedFootGroundError => currentLeftFixedFootGroundError;
    public float RightFixedFootGroundError => currentRightFixedFootGroundError;
    public float FixedFootGroundError => Mathf.Max(
        currentLeftFixedFootGroundError, currentRightFixedFootGroundError);
    public float LeftFixedFootGroundSigned =>
        GetFixedFootGroundSigned(leftFoot, baseLeftSoleAnchorLocal,
            leftFootMeshSoleOffset);
    public float RightFixedFootGroundSigned =>
        GetFixedFootGroundSigned(rightFoot, baseRightSoleAnchorLocal,
            rightFootMeshSoleOffset);
    public float LeftMeshSoleOffsetForVerification => leftFootMeshSoleOffset;
    public float RightMeshSoleOffsetForVerification => rightFootMeshSoleOffset;
    public Vector3 LeftFixedSoleAnchorWorldForVerification =>
        leftFoot != null ? leftFoot.TransformPoint(baseLeftSoleAnchorLocal) :
        Vector3.zero;
    public Vector3 RightFixedSoleAnchorWorldForVerification =>
        rightFoot != null ? rightFoot.TransformPoint(baseRightSoleAnchorLocal) :
        Vector3.zero;
    public float LeftFootHorizontalSlip => !basePoseCaptured ? float.PositiveInfinity :
        GetFootPlantHorizontalSlip(leftFoot, baseLeftSoleAnchorLocal, squatLeftSoleTarget);
    public float RightFootHorizontalSlip => !basePoseCaptured ? float.PositiveInfinity :
        GetFootPlantHorizontalSlip(rightFoot, baseRightSoleAnchorLocal, squatRightSoleTarget);
    public float FootRotationError => Mathf.Max(
        currentLeftFootRotationError, currentRightFootRotationError);
    public float BarBodyFollowError => currentBarBodyFollowError;
    public float BarDropFromStart => currentBarDropFromStart;
    public float HandSpread
    {
        get
        {
            if (!HasValidArmRig || owner == null)
            {
                return 0f;
            }

            Vector3 shoulderAxis = Vector3.ProjectOnPlane(owner.transform.right, owner.transform.up);
            if (shoulderAxis.sqrMagnitude < 0.0001f)
            {
                return 0f;
            }

            return Mathf.Abs(Vector3.Dot(
                rightHand.position - leftHand.position,
                shoulderAxis.normalized));
        }
    }
    public float HandSpreadRatio
    {
        get
        {
            if (!HasValidArmRig)
            {
                return 0f;
            }

            return HandSpread / Mathf.Max(0.01f, GetArmSpanReference());
        }
    }
    public bool HasOverhandGrip => HasValidArmRig && owner != null &&
        Vector3.Dot(leftHand.up, -owner.transform.up) > 0.25f &&
        Vector3.Dot(rightHand.up, -owner.transform.up) > 0.25f;
    public Vector3 BarTargetPosition => station != null && station.IsEnemySquatBarAttached
        ? station.EnemySquatBarCenter
        : owner != null ? CalculateBarTargetPosition(owner) : barTargetPosition;
    public Vector3 InitialBarTargetPosition => barTargetPosition;
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
        hasPreviousLeftElbowPose = false;
        hasPreviousRightElbowPose = false;
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
        return true;
    }

    public void Cancel()
    {
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
        currentGripError = float.PositiveInfinity;
        currentLeftGripError = float.PositiveInfinity;
        currentRightGripError = float.PositiveInfinity;
        currentForearmOutwardError = float.PositiveInfinity;
        currentArmCrossingError = 0f;
        currentElbowOutwardError = 0f;
        currentUpperArmReferenceError = float.PositiveInfinity;
        currentForearmReferenceError = float.PositiveInfinity;
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
        hasPreviousLeftElbowPose = false;
        hasPreviousRightElbowPose = false;
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
        float leftSigned = GetSoleAnchorWorld(leftFoot, baseLeftSoleAnchorLocal).y - floorY;
        float rightSigned = GetSoleAnchorWorld(rightFoot, baseRightSoleAnchorLocal).y - floorY;
        float correction = Mathf.Clamp(
            -(leftSigned + rightSigned) * 0.5f,
            -MaxAuthoredSquatGroundCorrection,
            MaxAuthoredSquatGroundCorrection);
        authoredAnimator.ApplyGroundedRootCorrection(up, correction);
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

        Vector3 currentBarTarget = CalculateBarTargetPosition(owner);
        station.SyncEnemySquatBarPose(owner, Traps, currentBarTarget);
        currentBarBodyFollowError = Vector3.Distance(
            station.EnemySquatBarCenter, currentBarTarget);
        currentBarDropFromStart = initialAttachedBarCenter.y -
            station.EnemySquatBarCenter.y;

        // The scene bar is the only object layered onto the authored pose.
        // It follows the current traps/neck target every sampled frame, while
        // the authored hands remain exactly as exported in squat.fbx.
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

    private float GetFootGroundError(Transform foot, float soleOffset)
    {
        if (foot == null || owner == null ||
            float.IsNaN(soleOffset) || float.IsInfinity(soleOffset))
        {
            return float.PositiveInfinity;
        }

        return Mathf.Abs(
            (foot.position.y + soleOffset) - GetFloorY(owner));
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

    private Vector3 EnsureSoleOffsetReachable(
        Transform thigh,
        Vector3 soleTarget,
        Vector3 anchorOffset,
        float upperLegLength,
        float lowerLegLength,
        Vector3 worldUp)
    {
        Vector3 horizontal = Vector3.ProjectOnPlane(anchorOffset, worldUp);
        float supportDepth = Mathf.Max(
            0f, -Vector3.Dot(anchorOffset, worldUp));
        float maximumReach = Mathf.Max(
            0.001f, upperLegLength + lowerLegLength - 0.0005f);

        for (int iteration = 0; iteration < 3; iteration++)
        {
            Vector3 candidate = horizontal - worldUp * supportDepth;
            float targetDistance = Vector3.Distance(
                thigh.position, soleTarget - candidate);
            if (targetDistance <= maximumReach)
            {
                return candidate;
            }

            // Raise the ankle target by extending only the invisible
            // sole-to-ankle support depth. This preserves a planted sole
            // without changing any authored bone length or scale.
            supportDepth += targetDistance - maximumReach;
        }

        return horizontal - worldUp * supportDepth;
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

    private float GetFootPlantHorizontalSlip(
        Transform foot, Vector3 anchorLocal, Vector3 target)
    {
        if (foot == null)
        {
            return float.PositiveInfinity;
        }

        return Vector3.ProjectOnPlane(
            GetSoleAnchorWorld(foot, anchorLocal) - target,
            Vector3.up).magnitude;
    }

    private static float GetFootPlantCandidateCost(
        Transform foot,
        Vector3 anchorLocal,
        Vector3 target,
        Vector3 worldUp)
    {
        Vector3 error = foot.TransformPoint(anchorLocal) - target;
        float groundError = Mathf.Abs(Vector3.Dot(error, worldUp));
        float horizontalError = Vector3.ProjectOnPlane(error, worldUp).magnitude;
        // Keep the two independent planting contracts balanced: a tiny
        // vertical improvement must not be bought with a larger horizontal
        // slip, or the reverse.
        return Mathf.Max(groundError / 0.015f, horizontalError / 0.020f);
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

    private bool ApplyFootPlantIk()
    {
        if (owner == null || !HasValidSquatRig)
        {
            return false;
        }

        Vector3 up = owner.transform.up.sqrMagnitude > 0.0001f
            ? owner.transform.up.normalized
            : Vector3.up;
        Vector3 pole = squatKneePole.sqrMagnitude > 0.0001f
            ? squatKneePole.normalized
            : owner.transform.forward;
        MixamoScanRetargetAnimator authoredAnimator =
            owner.GetComponentInChildren<MixamoScanRetargetAnimator>(true);
        Vector3 rootDelta = owner.transform.position - baseFighterPosition;
        Vector3 horizontalRootDelta = Vector3.ProjectOnPlane(rootDelta, up);
        squatLeftSoleTarget = baseSquatLeftSoleTarget + horizontalRootDelta;
        squatRightSoleTarget = baseSquatRightSoleTarget + horizontalRootDelta;
        float floorY = GetFloorY(owner);
        squatLeftSoleTarget.y = floorY;
        squatRightSoleTarget.y = floorY;

        // Solve to the measured bind-pose sole anchor itself. The previous
        // reachability extension could make the dynamic oracle read planted
        // while the actual mesh sole stayed above the floor.
        leftFoot.rotation = Quaternion.FromToRotation(
            leftFoot.up, up) * leftFoot.rotation;
        rightFoot.rotation = Quaternion.FromToRotation(
            rightFoot.up, up) * rightFoot.rotation;
        Physics.SyncTransforms();
        currentLeftSoleOffsetWorld = leftFoot.TransformVector(
            baseLeftSoleAnchorLocal);
        currentRightSoleOffsetWorld = rightFoot.TransformVector(
            baseRightSoleAnchorLocal);

        // If the authored clip raises the hips farther than its current
        // runtime leg segments can reach, lower only the visible rig root
        // before solving. This keeps the fixed floor contacts reachable
        // without changing a limb scale, bone position, or animation asset.
        float remainingReachCorrection = 0.80f;
        for (int reachIteration = 0;
            reachIteration < 6 && remainingReachCorrection > 0.0005f;
            reachIteration++)
        {
            float leftRuntimeReach = Vector3.Distance(
                leftThigh.position, leftShin.position) +
                Vector3.Distance(leftShin.position, leftFoot.position);
            float rightRuntimeReach = Vector3.Distance(
                rightThigh.position, rightShin.position) +
                Vector3.Distance(rightShin.position, rightFoot.position);
            float leftTargetDistance = Vector3.Distance(
                leftThigh.position,
                squatLeftSoleTarget - currentLeftSoleOffsetWorld);
            float rightTargetDistance = Vector3.Distance(
                rightThigh.position,
                squatRightSoleTarget - currentRightSoleOffsetWorld);
            float leftReachDeficit = Mathf.Max(
                0f, leftTargetDistance - leftRuntimeReach);
            float rightReachDeficit = Mathf.Max(
                0f, rightTargetDistance - rightRuntimeReach);
            if (leftReachDeficit <= 0.0005f &&
                rightReachDeficit <= 0.0005f)
            {
                break;
            }

            float leftCorrectionDistance = leftReachDeficit > 0.0005f
                ? leftReachDeficit + 0.06f
                : 0f;
            float rightCorrectionDistance = rightReachDeficit > 0.0005f
                ? rightReachDeficit + 0.06f
                : 0f;
            Vector3 leftReachDirection = leftTargetDistance > 0.0001f
                ? (squatLeftSoleTarget - currentLeftSoleOffsetWorld -
                    leftThigh.position).normalized
                : Vector3.zero;
            Vector3 rightReachDirection = rightTargetDistance > 0.0001f
                ? (squatRightSoleTarget - currentRightSoleOffsetWorld -
                    rightThigh.position).normalized
                : Vector3.zero;
            Vector3 reachCorrectionVector = Vector3.zero;
            int deficientLegCount = 0;
            if (leftCorrectionDistance > 0f)
            {
                reachCorrectionVector += leftReachDirection *
                    leftCorrectionDistance;
                deficientLegCount++;
            }
            if (rightCorrectionDistance > 0f)
            {
                reachCorrectionVector += rightReachDirection *
                    rightCorrectionDistance;
                deficientLegCount++;
            }
            if (deficientLegCount == 0 ||
                reachCorrectionVector.sqrMagnitude < 0.00000025f)
            {
                break;
            }
            Vector3 averageReachCorrection = reachCorrectionVector /
                deficientLegCount;
            float correctionDistance = Mathf.Min(
                averageReachCorrection.magnitude, remainingReachCorrection);
            Vector3 reachCorrection = averageReachCorrection.normalized *
                correctionDistance;
            ExternalRiggedCharacterVisual externalVisual =
                owner.GetComponent<ExternalRiggedCharacterVisual>();
            Transform visibleRoot = externalVisual != null
                ? externalVisual.RuntimeModelRoot
                : null;
            if (visibleRoot != null)
            {
                visibleRoot.position += reachCorrection;
            }
            if (authoredAnimator.RuntimeModelRoot != visibleRoot)
            {
                authoredAnimator.ApplyRootWorldCorrection(reachCorrection);
            }
            Physics.SyncTransforms();
            remainingReachCorrection -= correctionDistance;
            currentLeftSoleOffsetWorld = leftFoot.TransformVector(
                baseLeftSoleAnchorLocal);
            currentRightSoleOffsetWorld = rightFoot.TransformVector(
                baseRightSoleAnchorLocal);
        }

        bool leftSolved = ApplyFootPlantIkForLeg(
            leftThigh, leftShin, leftFoot,
            squatLeftSoleTarget, currentLeftSoleOffsetWorld,
            baseLeftSoleAnchorLocal, pole, leftUpperLegLength,
            leftLowerLegLength, up);
        bool rightSolved = ApplyFootPlantIkForLeg(
            rightThigh, rightShin, rightFoot,
            squatRightSoleTarget, currentRightSoleOffsetWorld,
            baseRightSoleAnchorLocal, pole, rightUpperLegLength,
            rightLowerLegLength, up);

        ApplyFixedSoleRootCorrection(authoredAnimator, up);

        // A steep authored frame can leave one endpoint just outside the
        // contract after the first grounded root correction. Retry only when
        // it is actually outside the two measured tolerances, and retain the
        // original pose if the retry is worse.
        float beforeRetryCost = Mathf.Max(
            GetFootPlantCandidateCost(
                leftFoot, baseLeftSoleAnchorLocal, squatLeftSoleTarget, up),
            GetFootPlantCandidateCost(
                rightFoot, baseRightSoleAnchorLocal, squatRightSoleTarget, up));
        if (beforeRetryCost > 1f)
        {
            Quaternion savedLeftThigh = leftThigh.localRotation;
            Quaternion savedLeftShin = leftShin.localRotation;
            Quaternion savedLeftFoot = leftFoot.localRotation;
            Quaternion savedRightThigh = rightThigh.localRotation;
            Quaternion savedRightShin = rightShin.localRotation;
            Quaternion savedRightFoot = rightFoot.localRotation;
            ExternalRiggedCharacterVisual retryVisual =
                owner.GetComponent<ExternalRiggedCharacterVisual>();
            Transform retryVisibleRoot = retryVisual != null
                ? retryVisual.RuntimeModelRoot
                : null;
            Vector3 savedVisibleRootPosition = retryVisibleRoot != null
                ? retryVisibleRoot.position
                : Vector3.zero;

            // The first solve can change the current hip/ankle geometry. If
            // a leg is still beyond reach, make one small diagonal root move
            // toward the deficient target before the retry.
            float retryLeftReach = Vector3.Distance(
                leftThigh.position, leftShin.position) +
                Vector3.Distance(leftShin.position, leftFoot.position);
            float retryRightReach = Vector3.Distance(
                rightThigh.position, rightShin.position) +
                Vector3.Distance(rightShin.position, rightFoot.position);
            float retryLeftDistance = Vector3.Distance(
                leftThigh.position,
                squatLeftSoleTarget - currentLeftSoleOffsetWorld);
            float retryRightDistance = Vector3.Distance(
                rightThigh.position,
                squatRightSoleTarget - currentRightSoleOffsetWorld);
            float retryLeftDeficit = Mathf.Max(
                0f, retryLeftDistance - retryLeftReach);
            float retryRightDeficit = Mathf.Max(
                0f, retryRightDistance - retryRightReach);
            Vector3 lateReachCorrection = Vector3.zero;
            int lateDeficientLegCount = 0;
            if (retryLeftDeficit > 0.0005f)
            {
                lateReachCorrection += (
                    squatLeftSoleTarget - currentLeftSoleOffsetWorld -
                    leftThigh.position).normalized * (retryLeftDeficit + 0.04f);
                lateDeficientLegCount++;
            }
            if (retryRightDeficit > 0.0005f)
            {
                lateReachCorrection += (
                    squatRightSoleTarget - currentRightSoleOffsetWorld -
                    rightThigh.position).normalized * (retryRightDeficit + 0.04f);
                lateDeficientLegCount++;
            }
            bool lateRootMoved = lateDeficientLegCount > 0 &&
                lateReachCorrection.sqrMagnitude > 0.00000025f;
            if (lateRootMoved)
            {
                lateReachCorrection /= lateDeficientLegCount;
                lateReachCorrection = Vector3.ClampMagnitude(
                    lateReachCorrection, 0.15f);
                if (retryVisibleRoot != null)
                {
                    retryVisibleRoot.position += lateReachCorrection;
                }
                if (authoredAnimator.RuntimeModelRoot != retryVisibleRoot)
                {
                    authoredAnimator.ApplyRootWorldCorrection(
                        lateReachCorrection);
                }
                Physics.SyncTransforms();
                currentLeftSoleOffsetWorld = leftFoot.TransformVector(
                    baseLeftSoleAnchorLocal);
                currentRightSoleOffsetWorld = rightFoot.TransformVector(
                    baseRightSoleAnchorLocal);
            }
            bool retryLeftSolved = ApplyFootPlantIkForLeg(
                leftThigh, leftShin, leftFoot,
                squatLeftSoleTarget, currentLeftSoleOffsetWorld,
                baseLeftSoleAnchorLocal, pole, leftUpperLegLength,
                leftLowerLegLength, up);
            bool retryRightSolved = ApplyFootPlantIkForLeg(
                rightThigh, rightShin, rightFoot,
                squatRightSoleTarget, currentRightSoleOffsetWorld,
                baseRightSoleAnchorLocal, pole, rightUpperLegLength,
                rightLowerLegLength, up);
            float afterRetryCost = Mathf.Max(
                GetFootPlantCandidateCost(
                    leftFoot, baseLeftSoleAnchorLocal, squatLeftSoleTarget, up),
                GetFootPlantCandidateCost(
                rightFoot, baseRightSoleAnchorLocal, squatRightSoleTarget, up));
            if (lateRootMoved)
            {
                ApplyFixedSoleRootCorrection(authoredAnimator, up);
                afterRetryCost = Mathf.Max(
                    GetFootPlantCandidateCost(
                        leftFoot, baseLeftSoleAnchorLocal, squatLeftSoleTarget, up),
                    GetFootPlantCandidateCost(
                        rightFoot, baseRightSoleAnchorLocal, squatRightSoleTarget, up));
            }
            if (!retryLeftSolved || !retryRightSolved ||
                afterRetryCost > beforeRetryCost)
            {
                leftThigh.localRotation = savedLeftThigh;
                leftShin.localRotation = savedLeftShin;
                leftFoot.localRotation = savedLeftFoot;
                rightThigh.localRotation = savedRightThigh;
                rightShin.localRotation = savedRightShin;
                rightFoot.localRotation = savedRightFoot;
                if (lateRootMoved && retryVisibleRoot != null)
                {
                    retryVisibleRoot.position = savedVisibleRootPosition;
                    Physics.SyncTransforms();
                }
            }
            else
            {
                leftSolved = retryLeftSolved;
                rightSolved = retryRightSolved;
            }
        }
        currentLeftSoleOffsetWorld = leftFoot.TransformVector(
            baseLeftSoleAnchorLocal);
        currentRightSoleOffsetWorld = rightFoot.TransformVector(
            baseRightSoleAnchorLocal);


        currentLeftFootGroundError = GetFootPlantGroundError(
            leftFoot, baseLeftSoleAnchorLocal);
        currentRightFootGroundError = GetFootPlantGroundError(
            rightFoot, baseRightSoleAnchorLocal);
        UpdateFixedFootGroundErrors();
        currentLeftFootSoleError = GetFootSoleError(
            leftFoot, leftToe, leftFootBoneToSoleOffset);
        currentRightFootSoleError = GetFootSoleError(
            rightFoot, rightToe, rightFootBoneToSoleOffset);
        currentLeftFootRotationError = Vector3.Angle(leftFoot.up, up);
        currentRightFootRotationError = Vector3.Angle(rightFoot.up, up);
        return leftSolved && rightSolved;
    }

    private void ApplyFixedSoleRootCorrection(
        MixamoScanRetargetAnimator authoredAnimator, Vector3 worldUp)
    {
        if (authoredAnimator == null || leftFoot == null || rightFoot == null)
        {
            return;
        }

        ExternalRiggedCharacterVisual externalVisual =
            owner.GetComponent<ExternalRiggedCharacterVisual>();
        Transform visibleRoot = externalVisual != null
            ? externalVisual.RuntimeModelRoot : null;
        float leftSigned = GetFixedFootGroundSigned(
            leftFoot, baseLeftSoleAnchorLocal, leftFootMeshSoleOffset);
        float rightSigned = GetFixedFootGroundSigned(
            rightFoot, baseRightSoleAnchorLocal, rightFootMeshSoleOffset);
        if (float.IsNaN(leftSigned) || float.IsNaN(rightSigned))
        {
            return;
        }
        float correction = -((leftSigned + rightSigned) * 0.5f);
        Vector3 leftHorizontalError = Vector3.ProjectOnPlane(
            leftFoot.TransformPoint(baseLeftSoleAnchorLocal) -
            squatLeftSoleTarget, worldUp);
        Vector3 rightHorizontalError = Vector3.ProjectOnPlane(
            rightFoot.TransformPoint(baseRightSoleAnchorLocal) -
            squatRightSoleTarget, worldUp);
        Vector3 horizontalCorrection = -(
            leftHorizontalError + rightHorizontalError) * 0.5f;
        Vector3 totalCorrection = horizontalCorrection +
            worldUp.normalized * correction;
        if (totalCorrection.sqrMagnitude <= 0.00000025f)
        {
            return;
        }

        // Correct only the authored skeleton roots. Foot/leg transforms,
        // scales, and the source squat clip remain untouched; the root shift
        // places the fixed mesh soles on the station floor as a rigid body.
        if (visibleRoot != null)
        {
            visibleRoot.position += totalCorrection;
        }
        if (authoredAnimator.RuntimeModelRoot != visibleRoot)
        {
            authoredAnimator.ApplyRootWorldCorrection(totalCorrection);
        }
        Physics.SyncTransforms();

        // A non-uniform imported model can leave a small horizontal residual
        // after a world-space root move. Correct that residual once, while
        // deliberately leaving the vertical correction single-pass.
        Vector3 correctedLeftHorizontal = Vector3.ProjectOnPlane(
            leftFoot.TransformPoint(baseLeftSoleAnchorLocal) -
            squatLeftSoleTarget, worldUp);
        Vector3 correctedRightHorizontal = Vector3.ProjectOnPlane(
            rightFoot.TransformPoint(baseRightSoleAnchorLocal) -
            squatRightSoleTarget, worldUp);
        Vector3 horizontalResidual = -(
            correctedLeftHorizontal + correctedRightHorizontal) * 0.5f;
        if (horizontalResidual.sqrMagnitude > 0.00000025f)
        {
            if (visibleRoot != null)
            {
                visibleRoot.position += horizontalResidual;
            }
            if (authoredAnimator.RuntimeModelRoot != visibleRoot)
            {
                authoredAnimator.ApplyRootWorldCorrection(horizontalResidual);
            }
            totalCorrection += horizontalResidual;
            Physics.SyncTransforms();
        }

        float finalLeftSigned = GetFixedFootGroundSigned(
            leftFoot, baseLeftSoleAnchorLocal, leftFootMeshSoleOffset);
        float finalRightSigned = GetFixedFootGroundSigned(
            rightFoot, baseRightSoleAnchorLocal, rightFootMeshSoleOffset);
        Vector3 finalLeftHorizontal = Vector3.ProjectOnPlane(
            leftFoot.TransformPoint(baseLeftSoleAnchorLocal) -
            squatLeftSoleTarget, worldUp);
        Vector3 finalRightHorizontal = Vector3.ProjectOnPlane(
            rightFoot.TransformPoint(baseRightSoleAnchorLocal) -
            squatRightSoleTarget, worldUp);
        Debug.Log(
            $"GYMCHAOS_SQUAT_FIXED_SOLE_ROOT_CORRECTION " +
            $"left={finalLeftSigned:0.000} right={finalRightSigned:0.000} " +
            $"delta={totalCorrection} " +
            $"leftHorizontal={finalLeftHorizontal} " +
            $"rightHorizontal={finalRightHorizontal}", this);
    }

    private bool ApplyFootPlantIkForLeg(
        Transform thigh,
        Transform shin,
        Transform foot,
        Vector3 soleTarget,
        Vector3 anchorOffset,
        Vector3 fixedSoleAnchorLocal,
        Vector3 kneePole,
        float upperLegLength,
        float lowerLegLength,
        Vector3 worldUp)
    {
        if (thigh == null || shin == null || foot == null)
        {
            return false;
        }

        // Preserve the authored foot heading while removing only its
        // vertical tilt. The foot is then treated as a rigid endpoint; no
        // position, scale, or limb length is written.
        Quaternion groundedFootRotation =
            Quaternion.FromToRotation(foot.up, worldUp) * foot.rotation;
        bool solved = false;
        foot.rotation = groundedFootRotation;
        anchorOffset = foot.TransformVector(fixedSoleAnchorLocal);
        // Authored clips may contain local bone-position channels. Use the
        // current segment lengths for this solve instead of assuming the
        // idle bind distances, while never writing scale or bone positions.
        float runtimeUpperLegLength = Vector3.Distance(
            thigh.position, shin.position);
        float runtimeLowerLegLength = Vector3.Distance(
            shin.position, foot.position);
        if (runtimeUpperLegLength > 0.001f)
        {
            upperLegLength = runtimeUpperLegLength;
        }
        if (runtimeLowerLegLength > 0.001f)
        {
            lowerLegLength = runtimeLowerLegLength;
        }
        // The imported rigs have non-zero foot pivots and different local
        // bone offsets. Give the endpoint solve enough passes to converge on
        // the fixed mesh sole without translating a foot bone or changing
        // either limb length.
        float bestCost = GetFootPlantCandidateCost(
            foot, fixedSoleAnchorLocal, soleTarget, worldUp);
        Quaternion bestThighRotation = thigh.localRotation;
        Quaternion bestShinRotation = shin.localRotation;
        Quaternion bestFootRotation = foot.localRotation;
        for (int iteration = 0; iteration < 8; iteration++)
        {
            Vector3 desiredAnkle = soleTarget - anchorOffset;
            solved = SolveTwoBoneChain(
                thigh, shin, foot, desiredAnkle, kneePole,
                upperLegLength, lowerLegLength);
            if (!solved)
            {
                return false;
            }

            // Reapply the rigid foot orientation after every leg solve. The
            // next pass corrects the remaining endpoint error without
            // translating the foot bone or changing either limb length.
            foot.rotation = groundedFootRotation;
            anchorOffset = foot.TransformVector(fixedSoleAnchorLocal);

            float cost = GetFootPlantCandidateCost(
                foot, fixedSoleAnchorLocal, soleTarget, worldUp);
            if (cost < bestCost)
            {
                bestCost = cost;
                bestThighRotation = thigh.localRotation;
                bestShinRotation = shin.localRotation;
                bestFootRotation = foot.localRotation;
            }
        }

        // Solving from a different authored pose can overshoot when the
        // target is near the chain's reach limit. Keep the best fixed-anchor
        // result rather than allowing a later pass to worsen contact.
        if (solved)
        {
            thigh.localRotation = bestThighRotation;
            shin.localRotation = bestShinRotation;
            foot.localRotation = bestFootRotation;
        }
        return solved;
    }

    private static bool SolveTwoBoneChain(
        Transform thigh,
        Transform shin,
        Transform foot,
        Vector3 target,
        Vector3 kneePole,
        float upperLegLength,
        float lowerLegLength)
    {
        Vector3 hip = thigh.position;
        Vector3 toTarget = target - hip;
        float targetDistance = toTarget.magnitude;
        if (targetDistance < 0.0001f)
        {
            return false;
        }

        float upper = Mathf.Max(0.001f, upperLegLength);
        float lower = Mathf.Max(0.001f, lowerLegLength);
        float minimumReach = Mathf.Abs(upper - lower) + 0.0005f;
        float maximumReach = upper + lower - 0.0005f;
        float distance = Mathf.Clamp(
            targetDistance, minimumReach, Mathf.Max(minimumReach, maximumReach));
        Vector3 direction = toTarget / targetDistance;
        if (targetDistance > maximumReach)
        {
            distance = maximumReach;
        }
        else if (targetDistance < minimumReach)
        {
            distance = minimumReach;
        }

        Vector3 projectedPole = Vector3.ProjectOnPlane(
            kneePole, direction);
        if (projectedPole.sqrMagnitude < 0.0001f)
        {
            projectedPole = Vector3.ProjectOnPlane(
                shin.position - hip, direction);
        }
        if (projectedPole.sqrMagnitude < 0.0001f)
        {
            projectedPole = Vector3.Cross(direction, Vector3.up);
        }
        if (projectedPole.sqrMagnitude < 0.0001f)
        {
            projectedPole = Vector3.Cross(direction, Vector3.right);
        }
        projectedPole.Normalize();

        float along = (upper * upper - lower * lower +
            distance * distance) / (2f * distance);
        float height = Mathf.Sqrt(
            Mathf.Max(0f, upper * upper - along * along));
        Vector3 desiredKnee = hip + direction * along +
            projectedPole * height;

        Vector3 currentUpper = shin.position - thigh.position;
        if (currentUpper.sqrMagnitude < 0.0001f)
        {
            return false;
        }
        thigh.rotation = Quaternion.FromToRotation(
            currentUpper, desiredKnee - hip) * thigh.rotation;

        Vector3 actualKnee = shin.position;
        Vector3 currentLower = foot.position - actualKnee;
        Vector3 desiredLower = target - actualKnee;
        if (currentLower.sqrMagnitude < 0.0001f ||
            desiredLower.sqrMagnitude < 0.0001f)
        {
            return false;
        }
        shin.rotation = Quaternion.FromToRotation(
            currentLower, desiredLower) * shin.rotation;
        return true;
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

    private void ApplyArmGripPose()
    {
        if (owner == null || station == null || !HasValidArmRig)
        {
            currentGripError = float.PositiveInfinity;
            currentLeftGripError = float.PositiveInfinity;
            currentRightGripError = float.PositiveInfinity;
            currentForearmOutwardError = float.PositiveInfinity;
            currentArmCrossingError = float.PositiveInfinity;
            currentElbowOutwardError = float.PositiveInfinity;
            currentUpperArmReferenceError = float.PositiveInfinity;
            currentForearmReferenceError = float.PositiveInfinity;
            currentArmShapeError = float.PositiveInfinity;
            currentLeftHandContactError = float.PositiveInfinity;
            currentRightHandContactError = float.PositiveInfinity;
            currentLeftFootSoleError = float.PositiveInfinity;
            currentRightFootSoleError = float.PositiveInfinity;
            currentLeftFootGroundError = float.PositiveInfinity;
            currentRightFootGroundError = float.PositiveInfinity;
            return;
        }

        GetGripTargets(
            out Vector3 leftTarget,
            out Vector3 rightTarget,
            out Vector3 leftContactTarget,
            out Vector3 rightContactTarget,
            out Vector3 barAxis);
        float leftSideSign = leftArmSideSign;
        float rightSideSign = rightArmSideSign;

        // The elbows should point down, slightly behind the lifter and out to
        // the sides. The forearms then rise from those elbows out to the
        // wide bar grip. The direction is mirrored in the character's own
        // frame, while every rig still contributes its own captured arm
        // lengths and hand frame. This keeps the upper arms anatomically
        // consistent on differently proportioned imported scans.
        Vector3 sideAxis = Vector3.ProjectOnPlane(owner.transform.right, owner.transform.up).normalized;
        Vector3 leftOutward = sideAxis * leftSideSign;
        Vector3 rightOutward = sideAxis * rightSideSign;
        Vector3 leftPole = GetSquatElbowPole(leftOutward, leftUpperArmLength, leftLowerArmLength);
        Vector3 rightPole = GetSquatElbowPole(rightOutward, rightUpperArmLength, rightLowerArmLength);

        // Solve the wrist from the actual fingertip endpoint, then correct
        // the target once more after the forearm has rotated.  The endpoint
        // is the part that touches the shaft; solving only to the wrist makes
        // the bar pass through the palm on rigs with a different hand scale.
        SolveArmToHandContact(
            leftUpperArm,
            leftForearm,
            leftHand,
            leftTarget,
            leftContactTarget,
            baseLeftHandContactOffsetLocal,
            leftUpperArmLength,
            leftLowerArmLength,
            leftPole,
            leftSideSign,
            baseLeftUpperArmLocalRotation,
            baseLeftForearmLocalRotation,
            baseLeftHandLocalRotation,
            baseLeftHandRelativeToForearm,
            ref leftTarget);
        SolveArmToHandContact(
            rightUpperArm,
            rightForearm,
            rightHand,
            rightTarget,
            rightContactTarget,
            baseRightHandContactOffsetLocal,
            rightUpperArmLength,
            rightLowerArmLength,
            rightPole,
            rightSideSign,
            baseRightUpperArmLocalRotation,
            baseRightForearmLocalRotation,
            baseRightHandLocalRotation,
            baseRightHandRelativeToForearm,
            ref rightTarget);
        currentForearmOutwardError = Mathf.Max(
            GetForearmOutwardError(leftForearm, leftHand),
            GetForearmOutwardError(rightForearm, rightHand));
        currentArmCrossingError = Mathf.Max(
            GetArmCrossingError(leftUpperArm, leftForearm, leftHand, leftSideSign),
            GetArmCrossingError(rightUpperArm, rightForearm, rightHand, rightSideSign));
        currentElbowOutwardError = Mathf.Max(
            GetElbowOutwardError(leftUpperArm, leftForearm, leftSideSign),
            GetElbowOutwardError(rightUpperArm, rightForearm, rightSideSign));
        currentUpperArmReferenceError = Mathf.Max(
            GetUpperArmReferenceError(leftUpperArm, leftForearm, leftSideSign),
            GetUpperArmReferenceError(rightUpperArm, rightForearm, rightSideSign));
        currentForearmReferenceError = Mathf.Max(
            GetForearmReferenceError(leftForearm, leftHand, leftSideSign),
            GetForearmReferenceError(rightForearm, rightHand, rightSideSign));
        currentArmShapeError = Mathf.Max(
            GetArmShapeError(
                leftUpperArm,
                leftForearm,
                leftHand,
                leftSideSign,
                leftUpperArmLength,
                leftLowerArmLength),
            GetArmShapeError(
                rightUpperArm,
                rightForearm,
                rightHand,
                rightSideSign,
                rightUpperArmLength,
                rightLowerArmLength));
        currentLeftGripError = Vector3.Distance(leftHand.position, leftTarget);
        currentRightGripError = Vector3.Distance(rightHand.position, rightTarget);
        currentGripError = Mathf.Max(currentLeftGripError, currentRightGripError);
        currentLeftHandContactError = GetHandContactError(
            leftHand, baseLeftHandContactOffsetLocal, leftContactTarget);
        currentRightHandContactError = GetHandContactError(
            rightHand, baseRightHandContactOffsetLocal, rightContactTarget);
    }

    private void GetGripTargets(
        out Vector3 leftTarget,
        out Vector3 rightTarget,
        out Vector3 leftContactTarget,
        out Vector3 rightContactTarget,
        out Vector3 barAxis)
    {
        Vector3 barCenter = station != null ? station.EnemySquatBarCenter : barTargetPosition;
        if (barCenter == Vector3.zero)
        {
            barCenter = barTargetPosition;
        }

        float armSpan = GetArmSpanReference();
        float shortestReach = Mathf.Min(
            leftUpperArmLength + leftLowerArmLength,
            rightUpperArmLength + rightLowerArmLength);
        // The Mixamo shoulder joints sit close to the chest. A fixed minimum
        // based on that joint distance made every enemy use almost the same
        // absolute grip width and over-stretched the smaller scans. Scale the
        // grip from the actual upper-arm span and the shorter arm chain.
        // The arm solver targets the wrist bone, but the requested squat pose
        // is a fingertip/end-contact pose: the bar must meet the fingers at
        // the inside end of the hand, not pass through the wrist or sit in the
        // middle of a wrapped palm. Subtract each rig's measured hand reach
        // from the available arm length so every imported body keeps a valid,
        // non-stretched chain after moving the wrist outward.
        float contactReach = Mathf.Max(leftHandContactReach, rightHandContactReach);
        float minimumGripOffset = Mathf.Max(0.12f, (shortestReach - contactReach) * 0.38f);
        float maximumGripOffset = Mathf.Max(
            minimumGripOffset,
            // Leave enough arm-chain reach for the elbow to stay inside the
            // fingertip grip. The old cap could make the bar contact narrower
            // than the imported elbow span, forcing both forearms inward.
            (shortestReach - contactReach) * 1.35f);
        float gripOffset = Mathf.Clamp(
            armSpan * GetGripSpanMultiplier(),
            minimumGripOffset,
            maximumGripOffset);
        float overhandHeight = Mathf.Clamp(armSpan * 0.025f, 0.006f, 0.02f);
        barAxis = Vector3.ProjectOnPlane(owner.transform.right, owner.transform.up);
        if (barAxis.sqrMagnitude < 0.0001f)
        {
            barAxis = Vector3.right;
        }
        barAxis.Normalize();
        Vector3 handOffset = owner.transform.up * overhandHeight;
        float leftSideSign = leftArmSideSign;
        float rightSideSign = rightArmSideSign;
        leftContactTarget = barCenter +
            barAxis * leftSideSign * gripOffset + handOffset;
        rightContactTarget = barCenter +
            barAxis * rightSideSign * gripOffset + handOffset;
        // The wrist stays outside the contact point. The fingers remain in
        // their authored relaxed pose and only the measured fingertip endpoint
        // touches the shaft; no finger chain is retargeted around the bar.
        // Start from each imported hand's real endpoint vector.  The arm IK
        // will refine this after it has selected the elbow plane because the
        // endpoint vector rotates with the forearm on every asset.
        Vector3 leftContactOffsetLocal = GetEffectiveHandContactOffsetLocal(
            leftHand, baseLeftHandContactOffsetLocal);
        Vector3 rightContactOffsetLocal = GetEffectiveHandContactOffsetLocal(
            rightHand, baseRightHandContactOffsetLocal);
        leftTarget = leftContactTarget -
            leftHand.TransformVector(leftContactOffsetLocal);
        rightTarget = rightContactTarget -
            rightHand.TransformVector(rightContactOffsetLocal);
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

    private float GetArmSpanReference()
    {
        if (armSpanReference > 0.01f)
        {
            return armSpanReference;
        }

        if (leftUpperArm != null && rightUpperArm != null)
        {
            float span = Vector3.Distance(leftUpperArm.position, rightUpperArm.position);
            if (span > 0.01f)
            {
                return span;
            }
        }

        if (leftShoulder != null && rightShoulder != null)
        {
            return Vector3.Distance(leftShoulder.position, rightShoulder.position);
        }

        return 0.5f;
    }

    private float GetGripSpanMultiplier()
    {
        if (owner == null)
        {
            return 0.58f;
        }

        // A back-squat grip is only moderately wider than the shoulders. Keep
        // the target per-rig because the imported scans have different
        // shoulder widths and arm reach, while the reach cap above prevents
        // stretch. The old 0.90/3.00 values produced an exaggerated spread
        // that no longer matched the reference pose.
        switch (owner.Identity)
        {
            case BodybuilderIdentity.Cbum:
            case BodybuilderIdentity.Arnold:
                return 0.92f;
            case BodybuilderIdentity.Zyzz:
            case BodybuilderIdentity.JayCutler:
                return 0.92f;
            case BodybuilderIdentity.Goku:
                // Goku's stylized scan has a small shoulder-joint span. Keep
                // the hand contact wide enough for the elbows to remain
                // inside the grip without widening the foot stance.
                return 1.05f;
            default:
                return 0.86f;
        }
    }

    private Vector3 GetSquatElbowPole(
        Vector3 outwardDirection, float upperLength, float lowerLength)
    {
        Vector3 up = owner != null ? owner.transform.up : Vector3.up;
        Vector3 forward = owner != null
            ? Vector3.ProjectOnPlane(owner.transform.forward, up)
            : Vector3.forward;
        if (forward.sqrMagnitude < 0.0001f)
        {
            forward = Vector3.forward;
        }
        forward.Normalize();

        float armLength = Mathf.Max(0.1f, upperLength + lowerLength);
        float rearWeight = 0.22f;
        // Keep the pole primarily down rather than wide. The shoulder-to-bar
        // silhouette is a back-squat pose; it must not be achieved by
        // widening the feet or flaring the elbows past the hand contact.
        float outwardWeight = 0.27f;
        float dropWeight = 1.30f;
        if (owner != null)
        {
            switch (owner.Identity)
            {
                case BodybuilderIdentity.Cbum:
                    // Cbum's imported rest shoulders already sit wide; keep
                    // his correction modest so the elbows follow the reference
                    // without over-rotating the broad upper back.
                    rearWeight = 0.24f;
                    outwardWeight = 0.26f;
                    dropWeight = 1.32f;
                    break;
                case BodybuilderIdentity.Arnold:
                    rearWeight = 0.20f;
                    outwardWeight = 0.25f;
                    dropWeight = 1.36f;
                    break;
                case BodybuilderIdentity.Zyzz:
                    // Zyzz's scan has a narrower shoulder bind and was the
                    // rig most likely to pull the elbows toward the chest.
                    // Give it a wider, lower back-squat elbow plane.
                    rearWeight = 0.16f;
                    outwardWeight = 0.29f;
                    dropWeight = 1.42f;
                    break;
                case BodybuilderIdentity.JayCutler:
                    rearWeight = 0.18f;
                    outwardWeight = 0.28f;
                    dropWeight = 1.38f;
                    break;
                case BodybuilderIdentity.Goku:
                    rearWeight = 0.15f;
                    outwardWeight = 0.24f;
                    dropWeight = 1.24f;
                    break;
            }
        }

        // This is an explicit back-squat elbow target, not just a generic
        // pole direction: elbows sit below the bar and outside their own
        // shoulders, while the forearms travel upward and outward to the
        // wide grip. The per-identity weights compensate for each imported
        // scan's shoulder width and bind-pose rotation.
        return (-up * dropWeight - forward * rearWeight +
            outwardDirection.normalized * outwardWeight) * armLength;
    }

    private Vector3 GetSquatArmReferencePole(
        Transform upperArm,
        Vector3 targetHand,
        float upperLength,
        float expectedSideSign)
    {
        if (owner == null || upperArm == null)
        {
            return Vector3.zero;
        }

        Vector3 sideAxis = Vector3.ProjectOnPlane(
            owner.transform.right, owner.transform.up);
        Vector3 forwardAxis = Vector3.ProjectOnPlane(
            owner.transform.forward, owner.transform.up);
        Vector3 handDirection = targetHand - upperArm.position;
        if (sideAxis.sqrMagnitude < 0.0001f ||
            forwardAxis.sqrMagnitude < 0.0001f ||
            handDirection.sqrMagnitude < 0.0001f)
        {
            return Vector3.zero;
        }

        sideAxis.Normalize();
        forwardAxis.Normalize();
        GetSquatArmShapeProfile(
            out float desiredUpperDown,
            out float desiredUpperOutward,
            out _,
            out _);
        Vector3 desiredElbowOffset =
            sideAxis * expectedSideSign * upperLength * desiredUpperOutward -
            owner.transform.up * upperLength * desiredUpperDown -
            forwardAxis * upperLength * 0.16f;
        return Vector3.ProjectOnPlane(
            desiredElbowOffset, handDirection.normalized);
    }

    private void SolveArmWithSideGuard(
        Transform upperArm,
        Transform forearm,
        Transform hand,
        Vector3 targetHand,
        float upperLength,
        float lowerLength,
        Vector3 poleDirection,
        float expectedSideSign,
        Quaternion baseUpperRotation,
        Quaternion baseForearmRotation,
        Quaternion baseHandRotation)
    {
        float bestCost = float.PositiveInfinity;
        Quaternion bestUpperRotation = baseUpperRotation;
        Quaternion bestForearmRotation = baseForearmRotation;
        Quaternion bestHandRotation = baseHandRotation;

        EvaluateArmCandidate(
            upperArm,
            forearm,
            hand,
            targetHand,
            upperLength,
            lowerLength,
            poleDirection,
            expectedSideSign,
            baseUpperRotation,
            baseForearmRotation,
            baseHandRotation,
            ref bestCost,
            ref bestUpperRotation,
            ref bestForearmRotation,
            ref bestHandRotation);
        EvaluateArmCandidate(
            upperArm,
            forearm,
            hand,
            targetHand,
            upperLength,
            lowerLength,
            -poleDirection,
            expectedSideSign,
            baseUpperRotation,
            baseForearmRotation,
            baseHandRotation,
            ref bestCost,
            ref bestUpperRotation,
            ref bestForearmRotation,
            ref bestHandRotation);

        // Give the solver one explicit pole built from the reference
        // down/out upper-arm angles. Generic poles remain as fallbacks for
        // unusual imported axes, but should not win over this silhouette.
        Vector3 referencePole = GetSquatArmReferencePole(
            upperArm, targetHand, upperLength, expectedSideSign);
        if (referencePole.sqrMagnitude > 0.0001f)
        {
            EvaluateArmCandidate(
                upperArm,
                forearm,
                hand,
                targetHand,
                upperLength,
                lowerLength,
                referencePole,
                expectedSideSign,
                baseUpperRotation,
                baseForearmRotation,
                baseHandRotation,
                ref bestCost,
                ref bestUpperRotation,
                ref bestForearmRotation,
                ref bestHandRotation);

            Vector3 handAxis = targetHand - upperArm.position;
            if (handAxis.sqrMagnitude > 0.0001f)
            {
                handAxis.Normalize();
                Vector3 referencePoleDirection = referencePole.normalized;
                // The earlier pole list sampled only a few disconnected
                // planes. Search the full elbow circle at 15-degree steps so
                // the solver can land between the wide and crossed branches.
                for (int sample = 1; sample < 24; sample++)
                {
                    Vector3 sampledPole = Quaternion.AngleAxis(
                        sample * 15f, handAxis) * referencePoleDirection;
                    EvaluateArmCandidate(
                        upperArm,
                        forearm,
                        hand,
                        targetHand,
                        upperLength,
                        lowerLength,
                        sampledPole,
                        expectedSideSign,
                        baseUpperRotation,
                        baseForearmRotation,
                        baseHandRotation,
                        ref bestCost,
                        ref bestUpperRotation,
                        ref bestForearmRotation,
                        ref bestHandRotation);
                }
            }
        }

        // A pole made mostly from the actual shoulder side is a useful
        // fallback for scans whose imported upper-arm axes make the authored
        // rear/down pole collapse into the wrong bend plane.
        Vector3 directOutwardPole = poleDirection;
        if (owner != null)
        {
            Vector3 sideAxis = Vector3.ProjectOnPlane(
                owner.transform.right, owner.transform.up);
            Vector3 forwardAxis = Vector3.ProjectOnPlane(
                owner.transform.forward, owner.transform.up);
            if (sideAxis.sqrMagnitude > 0.0001f)
            {
                sideAxis.Normalize();
                if (forwardAxis.sqrMagnitude < 0.0001f)
                {
                    forwardAxis = Vector3.forward;
                }

                directOutwardPole =
                    sideAxis * expectedSideSign * 2.2f -
                    owner.transform.up * 0.55f -
                    forwardAxis.normalized * 0.12f;
            }
        }

        EvaluateArmCandidate(
            upperArm,
            forearm,
            hand,
            targetHand,
            upperLength,
            lowerLength,
            directOutwardPole,
            expectedSideSign,
            baseUpperRotation,
            baseForearmRotation,
            baseHandRotation,
            ref bestCost,
            ref bestUpperRotation,
            ref bestForearmRotation,
            ref bestHandRotation);
        EvaluateArmCandidate(
            upperArm,
            forearm,
            hand,
            targetHand,
            upperLength,
            lowerLength,
            -directOutwardPole,
            expectedSideSign,
            baseUpperRotation,
            baseForearmRotation,
            baseHandRotation,
            ref bestCost,
            ref bestUpperRotation,
            ref bestForearmRotation,
            ref bestHandRotation);

        // The two imported pole profiles above cover the common broad- and
        // narrow-shoulder scans, but a few rigs need a lower, more vertical
        // elbow plane to keep both the upper arm down/out and the forearm
        // rising out to the wide grip. Search that intermediate plane too so
        // the solver does not settle for the mirrored-looking compromise.
        if (owner != null)
        {
            Vector3 sideAxis = Vector3.ProjectOnPlane(
                owner.transform.right, owner.transform.up);
            Vector3 forwardAxis = Vector3.ProjectOnPlane(
                owner.transform.forward, owner.transform.up);
            if (sideAxis.sqrMagnitude > 0.0001f)
            {
                sideAxis.Normalize();
                if (forwardAxis.sqrMagnitude < 0.0001f)
                {
                    forwardAxis = Vector3.forward;
                }

                Vector3 balancedPole =
                    sideAxis * expectedSideSign * 0.20f -
                    owner.transform.up * 1.50f -
                    forwardAxis.normalized * 0.08f;
                EvaluateArmCandidate(
                    upperArm,
                    forearm,
                    hand,
                    targetHand,
                    upperLength,
                    lowerLength,
                    balancedPole,
                    expectedSideSign,
                    baseUpperRotation,
                    baseForearmRotation,
                    baseHandRotation,
                    ref bestCost,
                    ref bestUpperRotation,
                    ref bestForearmRotation,
                    ref bestHandRotation);
                EvaluateArmCandidate(
                    upperArm,
                    forearm,
                    hand,
                    targetHand,
                    upperLength,
                    lowerLength,
                    -balancedPole,
                    expectedSideSign,
                    baseUpperRotation,
                    baseForearmRotation,
                    baseHandRotation,
                    ref bestCost,
                    ref bestUpperRotation,
                    ref bestForearmRotation,
                    ref bestHandRotation);

                // Keep a second, narrower down/out plane available for
                // stylized scans whose shoulder bind makes the broad pole
                // put the elbow outside the fingertip contact. The reference
                // pose needs the elbow below the shoulder and the forearm
                // rising out to the bar, not an elbow wider than the grip.
                Vector3 narrowDownPole =
                    sideAxis * expectedSideSign * 0.08f -
                    owner.transform.up * 1.65f -
                    forwardAxis.normalized * 0.04f;
                EvaluateArmCandidate(
                    upperArm,
                    forearm,
                    hand,
                    targetHand,
                    upperLength,
                    lowerLength,
                    narrowDownPole,
                    expectedSideSign,
                    baseUpperRotation,
                    baseForearmRotation,
                    baseHandRotation,
                    ref bestCost,
                    ref bestUpperRotation,
                    ref bestForearmRotation,
                    ref bestHandRotation);
                EvaluateArmCandidate(
                    upperArm,
                    forearm,
                    hand,
                    targetHand,
                    upperLength,
                    lowerLength,
                    -narrowDownPole,
                    expectedSideSign,
                    baseUpperRotation,
                    baseForearmRotation,
                    baseHandRotation,
                    ref bestCost,
                    ref bestUpperRotation,
                    ref bestForearmRotation,
                    ref bestHandRotation);

                // A middle down/out plane is important for stylized scans:
                // the broad pole can flare the elbow past the grip, while
                // the narrow pole can fold the forearm inward. Keep the
                // anatomically useful middle solution in the candidate set.
                Vector3 middleDownOutPole =
                    sideAxis * expectedSideSign * 0.95f -
                    owner.transform.up * 1.25f -
                    forwardAxis.normalized * 0.06f;
                EvaluateArmCandidate(
                    upperArm,
                    forearm,
                    hand,
                    targetHand,
                    upperLength,
                    lowerLength,
                    middleDownOutPole,
                    expectedSideSign,
                    baseUpperRotation,
                    baseForearmRotation,
                    baseHandRotation,
                    ref bestCost,
                    ref bestUpperRotation,
                    ref bestForearmRotation,
                    ref bestHandRotation);
                EvaluateArmCandidate(
                    upperArm,
                    forearm,
                    hand,
                    targetHand,
                    upperLength,
                    lowerLength,
                    -middleDownOutPole,
                    expectedSideSign,
                    baseUpperRotation,
                    baseForearmRotation,
                    baseHandRotation,
                    ref bestCost,
                    ref bestUpperRotation,
                    ref bestForearmRotation,
                    ref bestHandRotation);
            }

            Vector3 toHand = targetHand - upperArm.position;
            if (toHand.sqrMagnitude > 0.0001f)
            {
                Vector3 handDirection = toHand.normalized;
                Vector3 orthogonalOutwardPole = Vector3.ProjectOnPlane(
                    sideAxis * expectedSideSign,
                    handDirection);
                Vector3 orthogonalDownPole = Vector3.ProjectOnPlane(
                    -owner.transform.up,
                    handDirection);
                Vector3 orthogonalOutwardDownPole =
                    Vector3.ProjectOnPlane(
                        sideAxis * expectedSideSign * 0.72f -
                        owner.transform.up * 1.10f,
                        handDirection);

                EvaluateArmCandidate(
                    upperArm,
                    forearm,
                    hand,
                    targetHand,
                    upperLength,
                    lowerLength,
                    orthogonalOutwardPole,
                    expectedSideSign,
                    baseUpperRotation,
                    baseForearmRotation,
                    baseHandRotation,
                    ref bestCost,
                    ref bestUpperRotation,
                    ref bestForearmRotation,
                    ref bestHandRotation);
                EvaluateArmCandidate(
                    upperArm,
                    forearm,
                    hand,
                    targetHand,
                    upperLength,
                    lowerLength,
                    -orthogonalOutwardPole,
                    expectedSideSign,
                    baseUpperRotation,
                    baseForearmRotation,
                    baseHandRotation,
                    ref bestCost,
                    ref bestUpperRotation,
                    ref bestForearmRotation,
                    ref bestHandRotation);
                EvaluateArmCandidate(
                    upperArm,
                    forearm,
                    hand,
                    targetHand,
                    upperLength,
                    lowerLength,
                    orthogonalDownPole,
                    expectedSideSign,
                    baseUpperRotation,
                    baseForearmRotation,
                    baseHandRotation,
                    ref bestCost,
                    ref bestUpperRotation,
                    ref bestForearmRotation,
                    ref bestHandRotation);
                EvaluateArmCandidate(
                    upperArm,
                    forearm,
                    hand,
                    targetHand,
                    upperLength,
                    lowerLength,
                    orthogonalOutwardDownPole,
                    expectedSideSign,
                    baseUpperRotation,
                    baseForearmRotation,
                    baseHandRotation,
                    ref bestCost,
                    ref bestUpperRotation,
                    ref bestForearmRotation,
                    ref bestHandRotation);
                EvaluateArmCandidate(
                    upperArm,
                    forearm,
                    hand,
                    targetHand,
                    upperLength,
                    lowerLength,
                    -orthogonalOutwardDownPole,
                    expectedSideSign,
                    baseUpperRotation,
                    baseForearmRotation,
                    baseHandRotation,
                    ref bestCost,
                    ref bestUpperRotation,
                    ref bestForearmRotation,
                    ref bestHandRotation);
            }
        }

        upperArm.localRotation = bestUpperRotation;
        forearm.localRotation = bestForearmRotation;
        hand.localRotation = bestHandRotation;
        Vector3 finalElbowDirection = forearm.position - upperArm.position;
        if (finalElbowDirection.sqrMagnitude > 0.0001f)
        {
            if (upperArm == leftUpperArm)
            {
                previousLeftElbowDirection = finalElbowDirection.normalized;
                hasPreviousLeftElbowPose = true;
            }
            else if (upperArm == rightUpperArm)
            {
                previousRightElbowDirection = finalElbowDirection.normalized;
                hasPreviousRightElbowPose = true;
            }
        }
    }

    private void SolveArmToHandContact(
        Transform upperArm,
        Transform forearm,
        Transform hand,
        Vector3 targetHand,
        Vector3 targetContact,
        Vector3 contactOffsetLocal,
        float upperLength,
        float lowerLength,
        Vector3 poleDirection,
        float expectedSideSign,
        Quaternion baseUpperRotation,
        Quaternion baseForearmRotation,
        Quaternion baseHandRotation,
        Quaternion handRelativeToForearm,
        ref Vector3 solvedTargetHand)
    {
        solvedTargetHand = targetHand;
        activeArmContactHand = hand;
        activeArmContactTarget = targetContact;
        // The endpoint correction is deterministic from the measured
        // fingertip offset. More passes make the imported hand visibly orbit
        // the shaft before settling, so use one plane solve and one final
        // contact correction while keeping the elbow branch stable.
        for (int iteration = 0; iteration < 2; iteration++)
        {
            SolveArmWithSideGuard(
                upperArm,
                forearm,
                hand,
                solvedTargetHand,
                upperLength,
                lowerLength,
                poleDirection,
                expectedSideSign,
                baseUpperRotation,
                baseForearmRotation,
                baseHandRotation);
            AlignHandGripFrame(
                hand,
                forearm,
                handRelativeToForearm,
                contactOffsetLocal);

            Vector3 contactError = targetContact -
                hand.TransformPoint(contactOffsetLocal);
            if (contactError.sqrMagnitude <= 0.000004f)
            {
                break;
            }

            // Rebuild the wrist target from the hand orientation that is
            // actually being rendered. Adding a world-space residual to the
            // old wrist target leaves a rotated imported hand several
            // centimetres short of the shaft and can make the IK choose the
            // mirrored/high-elbow branch. This directly places the calibrated
            // fingertip endpoint on the bar on the next solve.
            solvedTargetHand = targetContact -
                hand.TransformVector(contactOffsetLocal);
        }
        activeArmContactHand = null;
        activeArmContactTarget = Vector3.zero;
    }

    private void EvaluateArmCandidate(
        Transform upperArm,
        Transform forearm,
        Transform hand,
        Vector3 targetHand,
        float upperLength,
        float lowerLength,
        Vector3 poleDirection,
        float expectedSideSign,
        Quaternion baseUpperRotation,
        Quaternion baseForearmRotation,
        Quaternion baseHandRotation,
        ref float bestCost,
        ref Quaternion bestUpperRotation,
        ref Quaternion bestForearmRotation,
        ref Quaternion bestHandRotation)
    {
        upperArm.localRotation = baseUpperRotation;
        forearm.localRotation = baseForearmRotation;
        hand.localRotation = baseHandRotation;
        SolveArm(
            upperArm,
            forearm,
            hand,
            targetHand,
            upperLength,
            lowerLength,
            poleDirection);

        float candidateCost = GetArmReferenceCost(
            upperArm, forearm, hand, expectedSideSign);
        // Score the same final anatomical constraints that are reported by
        // the runtime verifier. This prevents a candidate that looks good
        // using bind-pose hand axes from winning and then crossing after the
        // calibrated fingertip frame is applied.
        candidateCost += GetUpperArmReferenceError(
            upperArm, forearm, expectedSideSign) * 80f;
        candidateCost += GetForearmReferenceError(
            forearm, hand, expectedSideSign) * 80f;
        candidateCost += GetElbowOutwardError(
            upperArm, forearm, expectedSideSign) * 80f;
        candidateCost += GetArmCrossingError(
            upperArm, forearm, hand, expectedSideSign) * 80f;
        // Prefer the reference silhouette itself, not merely a mathematically
        // reachable hand. This keeps the upper arm angled down/out from the
        // shoulder and the forearm rising out to the bar for each imported
        // enemy, preventing the mirrored/crossed compromise from winning.
        float candidateArmShapeError = GetArmShapeError(
            upperArm,
            forearm,
            hand,
            expectedSideSign,
            upperLength,
            lowerLength);
        candidateCost += candidateArmShapeError * 900f;
        candidateCost += GetArmContinuityCost(upperArm, forearm);
        if (candidateCost < bestCost)
        {
            bestCost = candidateCost;
            bestUpperRotation = upperArm.localRotation;
            bestForearmRotation = forearm.localRotation;
            bestHandRotation = hand.localRotation;
        }
    }

    private float GetArmReferenceCost(
        Transform upperArm,
        Transform forearm,
        Transform hand,
        float expectedSideSign)
    {
        if (owner == null || upperArm == null || forearm == null || hand == null)
        {
            return float.PositiveInfinity;
        }

        Vector3 sideAxis = Vector3.ProjectOnPlane(
            owner.transform.right, owner.transform.up);
        if (sideAxis.sqrMagnitude < 0.0001f)
        {
            return float.PositiveInfinity;
        }

        sideAxis.Normalize();
        Vector3 upAxis = owner.transform.up;
        Vector3 elbowOffset = forearm.position - upperArm.position;
        // Evaluate the point that actually touches the shaft. Palm origins
        // differ materially across these imported enemy rigs.
        Vector3 handReferencePosition = activeArmContactHand == hand
            ? activeArmContactTarget
            : GetHandContactPosition(hand);
        Vector3 handOffset = handReferencePosition - upperArm.position;
        Vector3 forearmOffset = handReferencePosition - forearm.position;
        float elbowOutward = Vector3.Dot(elbowOffset, sideAxis) * expectedSideSign;
        float handOutward = Vector3.Dot(handOffset, sideAxis) * expectedSideSign;
        float forearmOutward = Vector3.Dot(forearmOffset, sideAxis) * expectedSideSign;
        float elbowHeight = Vector3.Dot(elbowOffset, upAxis);
        float handHeight = Vector3.Dot(handOffset, upAxis);
        float forearmHeight = Vector3.Dot(forearmOffset, upAxis);

        // An inward elbow is never an acceptable trade for a slightly better
        // vertical angle. Penalize side inversion by a dominant weight, then
        // prefer elbows below the shoulder and hands on the same side.
        float inwardElbow = Mathf.Max(0f, 0.025f - elbowOutward);
        float inwardHand = Mathf.Max(0f, 0.025f - handOutward);
        float elbowAboveShoulder = Mathf.Max(0f, elbowHeight + 0.015f);
        float handAboveElbow = Mathf.Max(0f, elbowHeight - handHeight);
        // The elbow may be outside the shoulder, but it must still stay
        // inside the grip. If it is wider than the fingertip target, the
        // resulting forearm necessarily folds inward (the crossed/deformed
        // look from the reference screenshots).
        float elbowWiderThanGrip = Mathf.Max(
            0f, elbowOutward - handOutward + 0.015f);
        // Do not let a small-shoulder stylized rig solve with a bodybuilding
        // sized elbow flare. Keep the elbow near its own shoulder span; this
        // is what makes the forearm rise to the bar instead of folding back
        // inward even when the hand grip is wide.
        float maximumElbowOutward = owner.Identity == BodybuilderIdentity.Goku
            ? Mathf.Max(0.08f, GetArmSpanReference() * 0.68f)
            : Mathf.Max(0.08f, GetArmSpanReference() * 0.85f);
        float elbowTooWide = Mathf.Max(
            0f, elbowOutward - maximumElbowOutward);
        // In a real back squat the hand grip is wider than the elbow, so the
        // forearm continues outward as it rises to the bar. The old sign here
        // preferred an inward forearm and could select the mirrored/crossed
        // solution for one side of a scan.
        float forearmOutwardError = Mathf.Max(0f, 0.015f - forearmOutward);
        float handBelowElbow = Mathf.Max(0f, 0.03f - forearmHeight);
        return inwardElbow * 100f + inwardHand * 20f +
            elbowWiderThanGrip * 150f +
            elbowTooWide * 150f +
            elbowAboveShoulder * 30f + handAboveElbow * 0.25f +
            forearmOutwardError * 100f + handBelowElbow * 20f;
    }

    private void GetSquatArmShapeProfile(
        out float upperArmDown,
        out float upperArmOutward,
        out float forearmUp,
        out float forearmOutward)
    {
        // These normalized targets change only the limb silhouette. The
        // moderate squat foot stance and the existing per-rig hand span are
        // deliberately left untouched.
        upperArmDown = 0.30f;
        upperArmOutward = 0.48f;
        forearmUp = 0.17f;
        forearmOutward = 0.80f;
        if (owner == null)
        {
            return;
        }

        switch (owner.Identity)
        {
            case BodybuilderIdentity.Cbum:
                upperArmDown = 0.28f;
                upperArmOutward = 0.46f;
                forearmUp = 0.17f;
                // Cbum's wider authored arm span puts the hands far outside
                // the elbows on this rack; the correct forearm direction is
                // therefore much more lateral than the narrow default.
                forearmOutward = 0.94f;
                break;
            case BodybuilderIdentity.Arnold:
                upperArmDown = 0.30f;
                upperArmOutward = 0.45f;
                forearmUp = 0.16f;
                forearmOutward = 0.48f;
                break;
            case BodybuilderIdentity.Zyzz:
                upperArmDown = 0.32f;
                upperArmOutward = 0.49f;
                forearmUp = 0.18f;
                forearmOutward = 1.12f;
                break;
            case BodybuilderIdentity.JayCutler:
                upperArmDown = 0.31f;
                upperArmOutward = 0.48f;
                forearmUp = 0.17f;
                forearmOutward = 0.84f;
                break;
            case BodybuilderIdentity.Goku:
                // Goku's stylized scan places the traps/bar below the
                // imported shoulder pivots. A conventional down-elbow target
                // is unreachable on that skeleton and folds the forearms
                // inward; use its measured shoulder-plane solution instead.
                upperArmDown = 0.02f;
                upperArmOutward = 0.28f;
                forearmUp = 0.02f;
                forearmOutward = 0.85f;
                break;
            case BodybuilderIdentity.Ronnie:
                upperArmDown = 0.28f;
                upperArmOutward = 0.46f;
                forearmUp = 0.16f;
                forearmOutward = 0.80f;
                break;
        }
    }

    private float GetArmShapeError(
        Transform upperArm,
        Transform forearm,
        Transform hand,
        float expectedSideSign,
        float upperLength,
        float lowerLength)
    {
        if (owner == null || upperArm == null || forearm == null || hand == null)
        {
            return float.PositiveInfinity;
        }

        Vector3 sideAxis = Vector3.ProjectOnPlane(
            owner.transform.right, owner.transform.up);
        if (sideAxis.sqrMagnitude < 0.0001f)
        {
            return float.PositiveInfinity;
        }

        sideAxis.Normalize();
        Vector3 upAxis = owner.transform.up;
        Vector3 upperDirection = forearm.position - upperArm.position;
        // The imported wrist pivot is not consistently at the visible hand
        // contact across these FBX scans. Judge the forearm against the
        // actual fingertip/bar endpoint used by the grip solver; this is also
        // the point the player sees when deciding whether the arms rise to
        // the shaft or fold inward.
        Vector3 handReferencePosition = activeArmContactHand == hand
            ? activeArmContactTarget
            : GetHandContactPosition(hand);
        Vector3 forearmDirection = handReferencePosition - forearm.position;
        float measuredUpperLength = Mathf.Max(0.001f, upperDirection.magnitude);
        float measuredLowerLength = Mathf.Max(0.001f, forearmDirection.magnitude);
        // Use captured lengths when available, while still tolerating the
        // small wrist-pivot movement caused by the contact correction.
        float upperDenominator = upperLength > 0.01f
            ? upperLength
            : measuredUpperLength;
        float lowerDenominator = lowerLength > 0.01f
            ? lowerLength
            : measuredLowerLength;
        GetSquatArmShapeProfile(
            out float desiredUpperDown,
            out float desiredUpperOutward,
            out float desiredForearmUp,
            out float desiredForearmOutward);

        float actualUpperDown = Mathf.Max(0f, -Vector3.Dot(
            upperDirection, upAxis) / Mathf.Max(0.001f, upperDenominator));
        float actualUpperOutward = Vector3.Dot(
            upperDirection, sideAxis) * expectedSideSign /
            Mathf.Max(0.001f, upperDenominator);
        float actualForearmUp = Mathf.Max(0f, Vector3.Dot(
            forearmDirection, upAxis) / Mathf.Max(0.001f, lowerDenominator));
        float actualForearmOutward = Vector3.Dot(
            forearmDirection, sideAxis) * expectedSideSign /
            Mathf.Max(0.001f, lowerDenominator);

        // Crossing/inversion is checked separately. This metric describes the
        // visible angle and does not reward a wider foot stance or hand span.
        return Mathf.Abs(actualUpperDown - desiredUpperDown) * 1.25f +
            Mathf.Abs(actualUpperOutward - desiredUpperOutward) * 1.10f +
            Mathf.Abs(actualForearmUp - desiredForearmUp) * 1.15f +
            Mathf.Abs(actualForearmOutward - desiredForearmOutward);
    }

    private Vector3 GetHandContactPosition(Transform hand)
    {
        if (hand == null)
        {
            return Vector3.zero;
        }

        Vector3 contactOffset = hand == leftHand
            ? baseLeftHandContactOffsetLocal
            : hand == rightHand
                ? baseRightHandContactOffsetLocal
                : Vector3.zero;
        contactOffset = GetEffectiveHandContactOffsetLocal(hand, contactOffset);
        return contactOffset.sqrMagnitude > 0.000001f
            ? hand.TransformPoint(contactOffset)
            : hand.position;
    }

    private static Vector3 GetEffectiveHandContactOffsetLocal(
        Transform hand, Vector3 capturedOffset)
    {
        if (hand == null)
        {
            return Vector3.zero;
        }

        if (capturedOffset.sqrMagnitude > 0.000001f)
        {
            return capturedOffset;
        }

        // Some authored hands contain a valid hand bone but collapse the
        // fingertip endpoints onto that pivot. Use a finite local fallback so
        // contact and arm-shape measurements stay valid on that rig.
        return hand.InverseTransformVector(hand.forward * 0.08f);
    }

    private float GetArmContinuityCost(
        Transform upperArm, Transform forearm)
    {
        if (upperArm == null || forearm == null)
        {
            return 0f;
        }

        Vector3 currentDirection = forearm.position - upperArm.position;
        if (currentDirection.sqrMagnitude < 0.0001f)
        {
            return 100f;
        }
        currentDirection.Normalize();

        bool hasPrevious = upperArm == leftUpperArm
            ? hasPreviousLeftElbowPose
            : upperArm == rightUpperArm && hasPreviousRightElbowPose;
        if (!hasPrevious)
        {
            return 0f;
        }

        Vector3 previousDirection = upperArm == leftUpperArm
            ? previousLeftElbowDirection
            : previousRightElbowDirection;
        // A squat changes the elbow direction gradually. Penalize a branch
        // flip strongly enough that the arm cannot cross or snap to the
        // opposite IK solution between the bottom and top of a rep.
        // Reference constraints must win over temporal continuity. A large
        // branch-stickiness weight can keep a rig on the mirrored elbow plane
        // even after the solver has found the anatomically correct down/out
        // pose. Keep enough continuity to prevent frame-to-frame snapping,
        // but never at the cost of crossed or inverted forearms.
        return Vector3.Distance(currentDirection, previousDirection) * 0.5f;
    }

    private float GetArmCrossingError(
        Transform upperArm,
        Transform forearm,
        Transform hand,
        float expectedSideSign)
    {
        if (owner == null || upperArm == null || forearm == null || hand == null ||
            leftUpperArm == null || rightUpperArm == null)
        {
            return 0f;
        }

        Vector3 sideAxis = Vector3.ProjectOnPlane(
            owner.transform.right, owner.transform.up);
        if (sideAxis.sqrMagnitude < 0.0001f)
        {
            return 0f;
        }

        sideAxis.Normalize();
        float forearmSide = Vector3.Dot(
            forearm.position - upperArm.position, sideAxis) * expectedSideSign;
        Vector3 handReferencePosition = activeArmContactHand == hand
            ? activeArmContactTarget
            : GetHandContactPosition(hand);
        float handSide = Vector3.Dot(
            handReferencePosition - upperArm.position,
            sideAxis) * expectedSideSign;
        return Mathf.Max(0f, -Mathf.Min(forearmSide, handSide));
    }

    private float GetElbowOutwardError(
        Transform upperArm, Transform forearm, float expectedSideSign)
    {
        if (owner == null || upperArm == null || forearm == null)
        {
            return float.PositiveInfinity;
        }

        Vector3 sideAxis = Vector3.ProjectOnPlane(
            owner.transform.right, owner.transform.up);
        if (sideAxis.sqrMagnitude < 0.0001f)
        {
            return float.PositiveInfinity;
        }

        sideAxis.Normalize();
        float elbowOutward = Vector3.Dot(
            forearm.position - upperArm.position, sideAxis) * expectedSideSign;
        return Mathf.Max(0f, 0.025f - elbowOutward);
    }

    private float GetUpperArmReferenceError(
        Transform upperArm, Transform forearm, float expectedSideSign)
    {
        if (owner == null || upperArm == null || forearm == null)
        {
            return float.PositiveInfinity;
        }

        Vector3 sideAxis = Vector3.ProjectOnPlane(
            owner.transform.right, owner.transform.up);
        if (sideAxis.sqrMagnitude < 0.0001f)
        {
            return float.PositiveInfinity;
        }

        sideAxis.Normalize();
        float outward = GetArmSideOffset(upperArm, forearm, expectedSideSign);
        float height = GetArmVerticalOffset(upperArm, forearm);
        float allowedUpwardOffset = owner.Identity == BodybuilderIdentity.Goku
            ? 0.10f
            : -0.015f;
        return Mathf.Max(0f, 0.025f - outward) +
            Mathf.Max(0f, height - allowedUpwardOffset);
    }

    private float GetForearmReferenceError(
        Transform forearm, Transform hand, float expectedSideSign)
    {
        if (owner == null || forearm == null || hand == null)
        {
            return float.PositiveInfinity;
        }

        Vector3 sideAxis = Vector3.ProjectOnPlane(
            owner.transform.right, owner.transform.up);
        if (sideAxis.sqrMagnitude < 0.0001f)
        {
            return float.PositiveInfinity;
        }

        sideAxis.Normalize();
        // The forearm chain ends at the wrist/hand origin. Fingertips are
        // checked separately through HandContactError because an open
        // imported bind pose can place the average fingertip a few centimetres
        // inward from the wrist even when the actual forearm is anatomically
        // correct and the tips are touching the bar.
        Vector3 wristOffset = hand.position - forearm.position;
        float outward = Vector3.Dot(wristOffset, sideAxis) * expectedSideSign;
        float height = Vector3.Dot(wristOffset, owner.transform.up);
        // In a back-squat grip the elbow is lower and farther out; the
        // forearm travels upward and outward from the elbow to the wrist.
        float minimumOutward = owner.Identity == BodybuilderIdentity.Goku
            ? 0.005f
            : 0.015f;
        float minimumHeight = owner.Identity == BodybuilderIdentity.Goku
            ? 0.02f
            : 0.03f;
        return Mathf.Max(0f, minimumOutward - outward) +
            Mathf.Max(0f, minimumHeight - height);
    }

    private float GetArmSideOffset(
        Transform origin, Transform endpoint, float expectedSideSign)
    {
        if (owner == null || origin == null || endpoint == null)
        {
            return float.PositiveInfinity;
        }

        Vector3 sideAxis = Vector3.ProjectOnPlane(
            owner.transform.right, owner.transform.up);
        if (sideAxis.sqrMagnitude < 0.0001f)
        {
            return float.PositiveInfinity;
        }

        return Vector3.Dot(
            endpoint.position - origin.position,
            sideAxis.normalized) * expectedSideSign;
    }

    private float GetArmVerticalOffset(Transform origin, Transform endpoint)
    {
        if (owner == null || origin == null || endpoint == null)
        {
            return float.PositiveInfinity;
        }

        return Vector3.Dot(
            endpoint.position - origin.position, owner.transform.up);
    }

    private static void SolveArm(
        Transform upperArm,
        Transform forearm,
        Transform hand,
        Vector3 targetHand,
        float upperLength,
        float lowerLength,
        Vector3 poleDirection)
    {
        if (upperArm == null || forearm == null || hand == null ||
            upperLength <= 0.01f || lowerLength <= 0.01f)
        {
            return;
        }

        Vector3 shoulder = upperArm.position;
        Vector3 toHand = targetHand - shoulder;
        if (toHand.sqrMagnitude < 0.0001f)
        {
            return;
        }

        float distance = Mathf.Clamp(
            toHand.magnitude,
            Mathf.Abs(upperLength - lowerLength) + 0.001f,
            upperLength + lowerLength - 0.001f);
        Vector3 direction = toHand.normalized;
        Vector3 pole = Vector3.ProjectOnPlane(poleDirection, direction);
        if (pole.sqrMagnitude < 0.0001f)
        {
            pole = Vector3.Cross(direction, Vector3.up);
        }
        if (pole.sqrMagnitude < 0.0001f)
        {
            pole = Vector3.right;
        }
        pole.Normalize();

        float along = (upperLength * upperLength + distance * distance -
            lowerLength * lowerLength) / (2f * distance);
        float perpendicular = Mathf.Sqrt(
            Mathf.Max(0f, upperLength * upperLength - along * along));
        Vector3 solvedHand = shoulder + direction * distance;
        Vector3 targetElbow = shoulder + direction * along + pole * perpendicular;

        Vector3 currentUpper = forearm.position - shoulder;
        Vector3 desiredUpper = targetElbow - shoulder;
        if (currentUpper.sqrMagnitude > 0.0001f && desiredUpper.sqrMagnitude > 0.0001f)
        {
            upperArm.rotation = Quaternion.FromToRotation(currentUpper, desiredUpper) *
                upperArm.rotation;
        }

        Vector3 currentLower = hand.position - forearm.position;
        Vector3 desiredLower = solvedHand - forearm.position;
        if (currentLower.sqrMagnitude > 0.0001f && desiredLower.sqrMagnitude > 0.0001f)
        {
            forearm.rotation = Quaternion.FromToRotation(currentLower, desiredLower) *
                forearm.rotation;
        }

    }

    private float GetForearmOutwardError(Transform forearm, Transform hand)
    {
        if (forearm == null || hand == null || owner == null)
        {
            return float.PositiveInfinity;
        }

        Vector3 forearmAxis = hand.position - forearm.position;
        Vector3 currentPalm = Vector3.ProjectOnPlane(hand.up, forearmAxis);
        Vector3 desiredPalm = Vector3.ProjectOnPlane(-owner.transform.up, forearmAxis);
        if (forearmAxis.sqrMagnitude < 0.0001f ||
            currentPalm.sqrMagnitude < 0.0001f ||
            desiredPalm.sqrMagnitude < 0.0001f)
        {
            return float.PositiveInfinity;
        }

        return 1f - Vector3.Dot(currentPalm.normalized, desiredPalm.normalized);
    }

    private void AlignHandGripFrame(
        Transform hand,
        Transform forearm,
        Quaternion handRelativeToForearm,
        Vector3 contactOffsetLocal)
    {
        if (hand == null || forearm == null || owner == null)
        {
            return;
        }

        // Reapply the captured local hand basis. This is deliberately local to
        // the forearm, so each imported enemy keeps its own wrist/bind axes.
        hand.localRotation = handRelativeToForearm;
        Vector3 forearmAxis = hand.position - forearm.position;
        if (forearmAxis.sqrMagnitude < 0.0001f)
        {
            return;
        }

        // Establish the overhand palm frame first. This is deliberately a
        // whole-hand rotation, not a finger curl: the imported finger chains
        // keep their authored open spread and only their tips touch the shaft.
        Vector3 desiredPalm = -owner.transform.up;
        if (Vector3.Dot(hand.up, desiredPalm) < 0.999f)
        {
            hand.rotation = Quaternion.FromToRotation(hand.up, desiredPalm) *
                hand.rotation;
        }

        // Use the remaining wrist twist to aim the measured fingertip toward
        // the target, but rotate around the forearm axis. This preserves the
        // overhand relation and avoids the inward/crossed forearm that can be
        // produced when the hand is rotated around its fingertip vector.
        Vector3 desiredContactOffset = activeArmContactHand == hand
            ? activeArmContactTarget - hand.position
            : Vector3.zero;
        if (desiredContactOffset.sqrMagnitude > 0.000001f)
        {
            Vector3 forearmDirection = forearmAxis.normalized;
            Vector3 desiredContactDirection = desiredContactOffset.normalized;
            Quaternion palmAlignedRotation = hand.rotation;
            Quaternion bestRotation = palmAlignedRotation;
            float bestRollCost = float.PositiveInfinity;
            for (int sample = 0; sample < 18; sample++)
            {
                float roll = -180f + sample * 20f;
                Quaternion candidateRotation = Quaternion.AngleAxis(
                    roll, forearmDirection) * palmAlignedRotation;
                hand.rotation = candidateRotation;

                Vector3 rollForearmAxis = forearm.position - hand.position;
                Vector3 currentPalm = Vector3.ProjectOnPlane(
                    hand.up, rollForearmAxis);
                Vector3 rollDesiredPalm = Vector3.ProjectOnPlane(
                    -owner.transform.up, rollForearmAxis);
                if (rollForearmAxis.sqrMagnitude < 0.000001f ||
                    currentPalm.sqrMagnitude < 0.000001f ||
                    rollDesiredPalm.sqrMagnitude < 0.000001f)
                {
                    continue;
                }

                float forearmPalmCost = 1f - Vector3.Dot(
                    currentPalm.normalized, rollDesiredPalm.normalized);
                Vector3 currentContactDirection = hand.TransformVector(
                    contactOffsetLocal);
                float contactDirectionCost = currentContactDirection.sqrMagnitude >
                    0.000001f
                    ? 1f - Vector3.Dot(
                        currentContactDirection.normalized,
                        desiredContactDirection)
                    : 1f;
                float rollCost = forearmPalmCost * 3f +
                    contactDirectionCost;
                if (rollCost < bestRollCost)
                {
                    bestRollCost = rollCost;
                    bestRotation = candidateRotation;
                }
            }

            hand.rotation = bestRotation;
        }

        // Do not rotate any individual finger child bones. They retain each
        // asset's authored relaxed pose and meet the shaft only at the
        // calibrated fingertip endpoint.
    }

    private float GetHandContactError(
        Transform hand, Vector3 contactOffsetLocal, Vector3 targetContact)
    {
        if (hand == null)
        {
            return float.PositiveInfinity;
        }

        contactOffsetLocal = GetEffectiveHandContactOffsetLocal(
            hand, contactOffsetLocal);
        return Vector3.Distance(
            hand.TransformPoint(contactOffsetLocal), targetContact);
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
        currentGripError = float.PositiveInfinity;
        currentLeftGripError = float.PositiveInfinity;
        currentRightGripError = float.PositiveInfinity;
        currentForearmOutwardError = float.PositiveInfinity;
        currentArmCrossingError = 0f;
        currentElbowOutwardError = 0f;
        currentUpperArmReferenceError = float.PositiveInfinity;
        currentForearmReferenceError = float.PositiveInfinity;
        currentArmShapeError = float.PositiveInfinity;
        currentLeftHandContactError = float.PositiveInfinity;
        currentRightHandContactError = float.PositiveInfinity;
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
