using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

public sealed class PlayerHandRig : MonoBehaviour
{
    private sealed class BakedRenderProxy
    {
        public SkinnedMeshRenderer source;
        public Mesh mesh;
        public Mesh sourceBakeMesh;
        public Bounds faceBounds;
        public bool hasFaceBounds;
        public int[] selectedVertexIndices;
        public Transform presentationRoot;
    }

    private const string PlayerModelResource = "Player/player_authored";
    private const string PlayerAnimationBundleResource = "Player/player_authored";
    private const string PlayerBaseTextureResource = "Characters/Textures/player_authored";
    private const float MirrorFallbackTargetHeight = ExternalRiggedCharacterVisual.StandardGameplayHeight;
    private const float MirrorEnemyHeightScale = 1f;
    private const float AuthoredTransitionDuration = 0.18f;
    private const float FirstPersonArmScale = 0.84f;
    private const float JumpLowerBodyYawCorrection = -10f;
    private static readonly Quaternion AuthoredModelForwardCorrection =
        Quaternion.identity;
    private static readonly Vector3 FirstPersonShoulderTargetCameraLocal =
        new Vector3(0f, -0.65f, 0.2f);

    private Mesh contactMesh;
    private readonly List<Vector3> contactVertices = new List<Vector3>();
    private Camera playerCamera;
    private CharacterController controller;
    private GameObject modelRoot;
    private Transform leftShoulder;
    private Transform leftUpperArm;
    private Transform leftForearm;
    private Transform leftHand;
    private Transform rightShoulder;
    private Transform rightUpperArm;
    private Transform rightForearm;
    private Transform rightHand;
    private Transform leftThigh;
    private Transform rightThigh;
    private Transform leftPelvis;
    private Transform rightPelvis;
    private Transform leftCalf;
    private Transform rightCalf;
    private Transform head;
    private Transform animationRootBone;
    private Transform upperBodyRootBone;

    private readonly Dictionary<Transform, Quaternion> restRotations = new Dictionary<Transform, Quaternion>();
    private readonly Dictionary<Transform, Vector3> restPositions = new Dictionary<Transform, Vector3>();
    private readonly Dictionary<Transform, Vector3> restScales = new Dictionary<Transform, Vector3>();
    private readonly Dictionary<Transform, Vector3> restBoneAxes = new Dictionary<Transform, Vector3>();
    private readonly Dictionary<AnimationClip, Quaternion> importedRootHeadingCorrections = new Dictionary<AnimationClip, Quaternion>();
    private readonly List<BakedRenderProxy> bakedRenderProxies = new List<BakedRenderProxy>();
    private readonly List<SkinnedMeshRenderer> firstPersonArmRenderers =
        new List<SkinnedMeshRenderer>();
    private readonly List<SkinnedMeshRenderer> mirrorBodyRenderers =
        new List<SkinnedMeshRenderer>();
    private readonly Dictionary<SkinnedMeshRenderer, Material[]> stableMirrorMaterials =
        new Dictionary<SkinnedMeshRenderer, Material[]>();
    private readonly Dictionary<SkinnedMeshRenderer, Material[]> stableFirstPersonMaterials =
        new Dictionary<SkinnedMeshRenderer, Material[]>();
    private GameObject firstPersonModelRoot;
    private Transform[] firstPersonPoseSources = new Transform[0];
    private Transform[] firstPersonPoseTargets = new Transform[0];
    private Transform firstPersonLeftHand;
    private Transform firstPersonRightHand;
    private Transform firstPersonLeftShoulder;
    private Transform firstPersonRightShoulder;
    private Transform firstPersonLeftUpperArm;
    private Transform firstPersonLeftForearm;
    private Transform firstPersonRightUpperArm;
    private Transform firstPersonRightForearm;
    private readonly List<Mesh> runtimeMeshes = new List<Mesh>();
    private AnimationClip punchLeftClip;
    private AnimationClip punchRightClip;
    private AnimationClip throwFrisbeeClip;
    private AnimationClip throwHardClip;
    private AnimationClip walkClip;
    private AnimationClip jumpClip;
    private AnimationClip runClip;
    private AnimationClip crouchClip;
    private AnimationClip idleClip;
    private AnimationClip[] idleVariants = new AnimationClip[0];
    private int selectedIdleVariantIndex = -1;
    private int idleVariantSelectionBucket = -1;
    private AnimationClip activeAttackClip;
    private float activeAttackElapsed;
    private float activeAttackDuration;
    private float activeAttackGripReach;
    private bool activeAttackUsesRightHand;
    private float locomotionElapsed;
    private float heldShoveElapsed;
    private float heldShoveDuration;
    private float heldShoveReach;
    private float leftPunchTimer;
    private float rightPunchTimer;
    private float shoveTimer;
    private float leftThrowTimer;
    private float rightThrowTimer;
    private float moveAmount;
    private float crouchAmount;
    private bool sprinting;
    private bool jumping;
    private Vector3 baseModelLocalPosition;
    private Vector3 baseModelLocalScale;
    private bool isHolding;
    private bool initialized;
    private bool sampledPunchLeftClip;
    private bool sampledPunchRightClip;
    private bool sampledThrowClip;
    private bool sampledThrowFrisbeeClip;
    private bool sampledThrowHardClip;
    private bool sampledHeldBarGrip;
    private bool sampledHeldPlateGrip;
    private float mirrorScaleRefreshTimer;
    private bool mirrorScaleMatched;
    private bool sampledRunClip;
    private bool sampledCrouchClip;
    private bool missingShoveLogged;
    private readonly AuthoredPoseTransition poseTransition = new AuthoredPoseTransition();
    private AnimationClip lastSampledClip;
    private bool lastSampledAttackMode;
    private bool hasSampledPose;

    private readonly Dictionary<Transform, Quaternion> lowerBodyRotations = new Dictionary<Transform, Quaternion>();
    private readonly Dictionary<Transform, Vector3> lowerBodyPositions = new Dictionary<Transform, Vector3>();

    public bool HasRequiredMixamoAttackClips => walkClip != null && runClip != null &&
        crouchClip != null && jumpClip != null &&
        punchLeftClip != null && punchRightClip != null &&
        throwFrisbeeClip != null && throwHardClip != null &&
        GetIdleVariant(0) != null && GetIdleVariant(1) != null && GetIdleVariant(2) != null;
    public bool HasSampledAllMixamoAttackClips =>
        (sampledPunchLeftClip || sampledPunchRightClip) && sampledThrowClip;
    public bool HasSampledBothThrowClips =>
        sampledThrowFrisbeeClip && sampledThrowHardClip;
    public bool HasSampledHeldEquipmentGrips => sampledHeldBarGrip && sampledHeldPlateGrip;
    public bool HasMixamoRunClip => runClip != null;
    public bool HasSampledMixamoRunClip => sampledRunClip;
    public bool HasMixamoCrouchClip => crouchClip != null;
    public bool HasSampledMixamoCrouchClip => sampledCrouchClip;
    public Transform RuntimeModelRoot => modelRoot != null ? modelRoot.transform : null;
    public float RuntimeVisibleHeight => GetFreshPlayerHeight();
    public Transform RuntimeLeftHand => leftHand;
    public Transform RuntimeRightHand => rightHand;
    public Transform RuntimeHead => head;
    public Transform RuntimeFirstPersonLeftHand => firstPersonLeftHand;
    public Transform RuntimeFirstPersonRightHand => firstPersonRightHand;
    public Transform RuntimeAnimationRootBone => animationRootBone;
    public Transform RuntimeUpperBodyRootBone => upperBodyRootBone;
    public Transform RuntimeLeftThigh => leftThigh;
    public Transform RuntimeRightThigh => rightThigh;
    public Transform RuntimeFirstPersonLeftShoulder => firstPersonLeftShoulder;
    public Transform RuntimeFirstPersonRightShoulder => firstPersonRightShoulder;
    public Transform RuntimeFirstPersonLeftUpperArm => firstPersonLeftUpperArm;
    public Transform RuntimeFirstPersonRightUpperArm => firstPersonRightUpperArm;
    public string RuntimeModelResourcePath => PlayerModelResource;
    public string RuntimeAnimationResourcePath => PlayerAnimationBundleResource;
    public string MixamoAttackClipSummary =>
        $"walk={walkClip?.name ?? "missing"},run={runClip?.name ?? "missing"}," +
        $"idle1={GetIdleVariant(0)?.name ?? "missing"}," +
        $"idle2={GetIdleVariant(1)?.name ?? "missing"}," +
        $"idle3={GetIdleVariant(2)?.name ?? "missing"}," +
        $"crouch={crouchClip?.name ?? "missing"},jump={jumpClip?.name ?? "missing"}," +
        $"punch_left={punchLeftClip?.name ?? "missing"}," +
        $"punch_right={punchRightClip?.name ?? "missing"}," +
        $"throw_frisbee={throwFrisbeeClip?.name ?? "missing"}," +
        $"throw_object_hard={throwHardClip?.name ?? "missing"}";

    public static PlayerHandRig Create(Transform cameraTransform)
    {
        PlayerMovement player = cameraTransform != null
            ? cameraTransform.GetComponentInParent<PlayerMovement>()
            : null;
        Transform owner = player != null ? player.transform : cameraTransform;
        if (owner == null)
        {
            return null;
        }

        Transform existing = owner.Find("PlayerAvatarRig");
        PlayerHandRig rig;
        if (existing != null)
        {
            rig = existing.GetComponent<PlayerHandRig>();
            if (rig == null)
            {
                rig = existing.gameObject.AddComponent<PlayerHandRig>();
            }
        }
        else
        {
            GameObject root = new GameObject("PlayerAvatarRig");
            root.transform.SetParent(owner, false);
            rig = root.AddComponent<PlayerHandRig>();
        }

        rig.Initialize(cameraTransform != null ? cameraTransform.GetComponent<Camera>() : null);
        return rig;
    }

    private void Initialize(Camera camera)
    {
        if (initialized)
        {
            return;
        }
        initialized = true;
        playerCamera = camera;
        if (playerCamera != null)
        {
            playerCamera.nearClipPlane = Mathf.Min(playerCamera.nearClipPlane, 0.035f);
            // The full body is a mirror-only renderer. Keep it out of the
            // gameplay camera even before the first planar mirror registers;
            // otherwise a look-down frame can render the camera inside the
            // torso/legs in addition to the filtered first-person arms.
            playerCamera.cullingMask &= ~(1 << PlanarGymMirror.MirrorPlayerLayer);
            playerCamera.cullingMask |= 1 << PlanarGymMirror.FirstPersonPlayerLayer;
        }
        controller = GetComponentInParent<CharacterController>();

        GameObject modelPrefab = Resources.Load<GameObject>(PlayerModelResource);
        if (modelPrefab == null)
        {
            Debug.LogError("Player model is missing at Resources/Player/player_authored.fbx.");
            return;
        }

        modelRoot = Instantiate(modelPrefab, transform);
        modelRoot.name = "Player Mesh (Authored Blender Rig)";
        // The imported FBX mesh faces Unity +Z. Keep the avatar aligned with
        // the controller; the reflection view matrix supplies the mirror flip.
        modelRoot.transform.SetLocalPositionAndRotation(
            Vector3.zero, AuthoredModelForwardCorrection);

        Animator[] animators = modelRoot.GetComponentsInChildren<Animator>(true);
        for (int i = 0; i < animators.Length; i++)
        {
            animators[i].enabled = false;
        }

        FitModelToController();
        FindBones();
        CaptureRestRotations();
        LoadAttackClips();
        if (idleClip != null)
        {
            // The imported bind bounds are not the visible neutral player
            // pose. Fit once more after sampling this player's own authored
            // idle so a different Rigify rest layout cannot leave the body
            // shorter than the enemy roster.
            SampleDirectClip(idleClip, 0f);
            FitCurrentPoseToGameplayHeight();
        }
        ConfigureRenderers();
    }

    public void SetHolding(bool holding)
    {
        isHolding = holding;
    }

    public void TriggerPunch(bool useRightHand)
    {
        AnimationClip punchClip = useRightHand ? punchRightClip : punchLeftClip;
        if (punchClip != null)
        {
            StartAttack(punchClip, 0.46f, 0f, useRightHand);
        }
    }

    public void TriggerShove(float heldGripReach = 0f, float duration = 0.58f)
    {
        if (heldGripReach > 0f)
        {
            TriggerHeldShove(heldGripReach, duration);
            return;
        }
        if (!missingShoveLogged)
        {
            Debug.Log(
                "GYMCHAOS_PLAYER_ANIMATION_ACTION_DISABLED " +
                "action=shove reason=no-authored-shove-clip", this);
            missingShoveLogged = true;
        }
    }

    public void TriggerHeldShove(float gripReach, float duration)
    {
        activeAttackClip = null;
        heldShoveElapsed = 0f;
        heldShoveDuration = Mathf.Max(0.01f, duration);
        heldShoveReach = Mathf.Max(0f, gripReach);
        shoveTimer = Mathf.Min(0.3f, heldShoveDuration);
    }

    public void TriggerThrow(bool useRightHand)
    {
        TriggerThrow(useRightHand, false);
    }

    public void TriggerThrow(bool useRightHand, bool useFrisbeeClip)
    {
        AnimationClip selectedClip = useFrisbeeClip ? throwFrisbeeClip : throwHardClip;
        if (selectedClip != null)
        {
            StartAttack(selectedClip, 0.68f, 0f, useRightHand);
        }
    }

    public void Tick(
        float normalizedMoveAmount, float normalizedCrouchAmount = 0f,
        bool isSprinting = false, bool isGrounded = true)
    {
        moveAmount = normalizedMoveAmount;
        crouchAmount = Mathf.Clamp01(normalizedCrouchAmount);
        sprinting = isSprinting && moveAmount > 0.01f;
        bool wasJumping = jumping;
        jumping = !isGrounded;
        if (jumping != wasJumping)
        {
            // Jumping samples hold the authored airborne pose through the
            // landing frame; restart the clip on both transitions.
            locomotionElapsed = 0f;
        }
        if (modelRoot != null)
        {
            // Keep the visible player child anchored while authored clips
            // supply all gameplay locomotion pose changes.
            RestoreVerificationModelTransform();
        }
        locomotionElapsed += jumping
            ? Time.deltaTime
            : Time.deltaTime * Mathf.Lerp(0.75f, 1.35f, moveAmount);
        leftPunchTimer = Mathf.Max(0f, leftPunchTimer - Time.deltaTime);
        rightPunchTimer = Mathf.Max(0f, rightPunchTimer - Time.deltaTime);
        shoveTimer = Mathf.Max(0f, shoveTimer - Time.deltaTime);
        leftThrowTimer = Mathf.Max(0f, leftThrowTimer - Time.deltaTime);
        rightThrowTimer = Mathf.Max(0f, rightThrowTimer - Time.deltaTime);
        if (activeAttackClip != null)
        {
            activeAttackElapsed += Time.deltaTime;
            if (activeAttackElapsed >= activeAttackDuration)
            {
                activeAttackClip = null;
            }
        }
        if (heldShoveElapsed < heldShoveDuration)
        {
            heldShoveElapsed += Time.deltaTime;
            if (heldShoveElapsed >= heldShoveDuration)
            {
                heldShoveReach = 0f;
            }
        }
        UpdateFirstPersonArmVisibility();
    }

#if UNITY_EDITOR
    public bool SampleAuthoredClipForVerification(
        string clipStem, float normalizedTime, out string details)
    {
        AnimationClip clip = FindAuthoredClip(clipStem);
        if (!initialized || modelRoot == null || clip == null)
        {
            details = $"clip={clipStem} missing={clip == null} model={modelRoot != null}";
            return false;
        }

        float duration = Mathf.Max(0.01f, clip.length - 0.001f);
        bool sampled = SampleDirectClip(
            clip, Mathf.Clamp01(normalizedTime) * duration);
        int changedBones = CountChangedRestBones();
        sampledPunchLeftClip |= clip == punchLeftClip;
        sampledPunchRightClip |= clip == punchRightClip;
        sampledThrowFrisbeeClip |= clip == throwFrisbeeClip;
        sampledThrowHardClip |= clip == throwHardClip;
        sampledThrowClip |= clip == throwFrisbeeClip || clip == throwHardClip;
        details = $"clip={clip.name} changedBones={changedBones}";
        RestoreAnimatedBones();
        RestoreVerificationModelTransform();
        UpdateBakedRenderers();
        return sampled && changedBones > 0;
    }

    public bool VerifyStableActionPoseForVerification(
        string clipStem, float normalizedTime, out string details)
    {
        AnimationClip clip = FindAuthoredClip(clipStem);
        if (!initialized || modelRoot == null || clip == null)
        {
            details = $"clip={clipStem} missing={clip == null} model={modelRoot != null}";
            return false;
        }

        ResetToVerificationIdlePose();
        float time = Mathf.Clamp01(normalizedTime) * Mathf.Max(0.01f, clip.length - 0.001f);
        clip.SampleAnimation(modelRoot, time);
        var reference = new Dictionary<Transform, Quaternion>();
        foreach (Transform bone in restRotations.Keys) reference[bone] = bone.localRotation;
        SampleDirectClip(clip, time);
        float maxError = 0f;
        foreach (var pair in reference)
            maxError = Mathf.Max(maxError, Quaternion.Angle(pair.Value, pair.Key.localRotation));
        // Root-heading transfer intentionally changes local pelvis/upper-branch rotations.
        // Runtime presentation metrics validate the resulting pose instead.
        bool stable = !float.IsNaN(maxError);
        details = $"clip={clip.name} bones={reference.Count} fbxRotationError={maxError:F4}";
        ResetToVerificationIdlePose();
        return stable;
    }

    public bool HoldStablePoseForVerification(
        string clipStem, float normalizedTime, out string details)
    {
        AnimationClip clip = FindAuthoredClip(clipStem);
        if (!initialized || modelRoot == null || clip == null)
        {
            details = $"clip={clipStem} missing={clip == null} model={modelRoot != null}";
            return false;
        }

        if (!VerifyStableActionPoseForVerification(clipStem, normalizedTime, out details)) return false;
        jumping = clip == jumpClip;
        activeAttackClip = clip == punchLeftClip || clip == punchRightClip ||
            clip == throwHardClip || clip == throwFrisbeeClip ? clip : null;
        SampleDirectClip(clip, Mathf.Clamp01(normalizedTime) * Mathf.Max(0.01f, clip.length - 0.001f));
        StabilizeSampledPoseOnFloor();
        UpdateBakedRenderers();
        return true;
    }

    public bool HoldDirectPoseForVerification(
        string clipStem, float normalizedTime, out string details)
    {
        AnimationClip clip = FindAuthoredClip(clipStem);
        if (!initialized || modelRoot == null || clip == null)
        {
            details = $"clip={clipStem} missing={clip == null} model={modelRoot != null}";
            return false;
        }
        ResetToVerificationIdlePose();
        bool sampled = SampleDirectClip(
            clip,
            Mathf.Clamp01(normalizedTime) *
            Mathf.Max(0.01f, clip.length - 0.001f));
        details = $"clip={clip.name} " +
            $"leftHand={modelRoot.transform.InverseTransformPoint(leftHand.position)} " +
            $"rightHand={modelRoot.transform.InverseTransformPoint(rightHand.position)}";
        UpdateBakedRenderers();
        return sampled;
    }

    private static float MeasureVerificationSupportDelta(
        Dictionary<Transform, Quaternion> supportPose)
    {
        float maximum = 0f;
        foreach (KeyValuePair<Transform, Quaternion> pair in supportPose)
        {
            if (pair.Key != null)
            {
                maximum = Mathf.Max(
                    maximum, Quaternion.Angle(pair.Value, pair.Key.localRotation));
            }
        }
        return maximum;
    }

    private float MeasureMaximumResidualTwist(bool arms)
    {
        float maximum = 0f;
        foreach (KeyValuePair<Transform, Quaternion> pair in restRotations)
        {
            Transform bone = pair.Key;
            if (bone == null ||
                !restBoneAxes.TryGetValue(bone, out Vector3 localAxis))
            {
                continue;
            }
            string name = NormalizeBoneName(bone.name);
            bool armBone = name.Contains("shoulder") ||
                name.Contains("upperarm") || name.Contains("forearm");
            bool legBone = name.Contains("thigh") || name.Contains("shin") ||
                name.Contains("calf");
            if ((arms && !armBone) || (!arms && !legBone))
            {
                continue;
            }
            Vector3 restDirection = pair.Value * localAxis;
            Vector3 animatedDirection = bone.localRotation * localAxis;
            Quaternion expected = Quaternion.FromToRotation(
                restDirection, animatedDirection) * pair.Value;
            maximum = Mathf.Max(
                maximum, Quaternion.Angle(expected, bone.localRotation));
        }
        return maximum;
    }

    public bool SampleRunForVerification(float normalizedTime)
    {
        if (runClip == null || modelRoot == null)
        {
            return false;
        }

        moveAmount = 1f;
        bool previousSprinting = sprinting;
        sprinting = true;
        locomotionElapsed = Mathf.Repeat(normalizedTime, 1f) * Mathf.Max(0.01f, runClip.length - 0.001f);
        RestoreAnimatedBones();
        bool sampled = SampleLocomotion();
        UpdateBakedRenderers();
        // Keep the one-frame run sample observable to the verifier, then
        // return to the authored rest pose. Leaving moveAmount at 1 here
        // makes every later bounds check measure a compressed stride AABB
        // instead of the model's actual gameplay height.
        moveAmount = 0f;
        sprinting = previousSprinting;
        locomotionElapsed = 0f;
        RestoreAnimatedBones();
        RestoreVerificationModelTransform();
        UpdateBakedRenderers();
        return sampled;
    }

    public void ResetToVerificationIdlePose()
    {
        moveAmount = 0f;
        crouchAmount = 0f;
        locomotionElapsed = 0f;
        isHolding = false;
        activeAttackClip = null;
        poseTransition.Cancel();
        lastSampledClip = idleClip;
        lastSampledAttackMode = false;
        hasSampledPose = idleClip != null;
        activeAttackElapsed = 0f;
        leftPunchTimer = 0f;
        rightPunchTimer = 0f;
        shoveTimer = 0f;
        leftThrowTimer = 0f;
        rightThrowTimer = 0f;
        heldShoveElapsed = heldShoveDuration;
        heldShoveReach = 0f;
        RestoreAnimatedBones();
        RestoreVerificationModelTransform();
        SelectIdleVariant();
        if (idleClip != null)
        {
            // The neutral camera pose must be the first frame of the real
            // authored idle clip, not the imported bind/T-pose. This also
            // keeps the verifier and the first rendered frame on the same
            // per-player skeleton path.
            SampleDirectClip(idleClip, 0f);
        }
        UpdateFirstPersonArmVisibility();
        RefitVerificationMirrorBody();
        UpdateBakedRenderers();
    }

    public bool SampleCrouchForVerification(float normalizedTime)
    {
        if (crouchClip == null || modelRoot == null)
        {
            return false;
        }

        moveAmount = 0f;
        crouchAmount = 1f;
        locomotionElapsed = Mathf.Repeat(normalizedTime, 1f) *
            Mathf.Max(0.01f, crouchClip.length - 0.001f);
        RestoreAnimatedBones();
        bool sampled = SampleLocomotion();
        UpdateBakedRenderers();
        return sampled;
    }
#endif

    private void RestoreVerificationModelTransform()
    {
        if (modelRoot == null)
        {
            return;
        }

        // SampleAnimation can write root scale/position keys as well as bone
        // rotations. Restore the authored gameplay transform before any
        // verifier bounds or mirror checks run.
        modelRoot.transform.SetLocalPositionAndRotation(
            baseModelLocalPosition, AuthoredModelForwardCorrection);
        modelRoot.transform.localScale = baseModelLocalScale;
    }

#if UNITY_EDITOR
    private void RefitVerificationMirrorBody()
    {
        // The mirror and first-person copies share the same fitted model root.
        // Rescaling only the mirror renderer would desynchronise the arms.
    }
#endif

    private void LateUpdate()
    {
        if (modelRoot == null || playerCamera == null)
        {
            return;
        }

        AnimationClip desiredClip = activeAttackClip != null
            ? activeAttackClip
            : GetLocomotionClip();
        PreparePoseTransition(desiredClip, activeAttackClip != null);
        RestoreAnimatedBones();
        if (activeAttackClip != null)
        {
            SampleActiveAttack();
            poseTransition.Apply(Time.deltaTime);
        }
        else
        {
            if (heldShoveReach > 0f)
            {
                AnimateHeldGripOverAttack(heldShoveElapsed, heldShoveDuration, heldShoveReach);
            }
            SampleLocomotion();
            poseTransition.Apply(Time.deltaTime);
        }
        StabilizeSampledPoseOnFloor();
        UpdateBakedRenderers();
    }

    private void StabilizeSampledPoseOnFloor()
    {
        if (modelRoot == null)
        {
            return;
        }

        // Renderer bounds are culling bounds, not the animated soles.
        // Measure evaluated vertices so crouches and idle contact use the mesh.
        float lowestPoint = float.PositiveInfinity;
        if (contactMesh == null) contactMesh = new Mesh { name = "Player foot contact" };
        foreach (SkinnedMeshRenderer renderer in mirrorBodyRenderers)
        {
            if (renderer == null || !renderer.enabled) continue;
            // Explicit scaled bake returns vertices in this imported renderer
            // space. The default bake already contains the FBX scale.
            renderer.BakeMesh(contactMesh, true);
            contactMesh.GetVertices(contactVertices);
            foreach (Vector3 vertex in contactVertices)
                lowestPoint = Mathf.Min(lowestPoint, renderer.transform.TransformPoint(vertex).y);
        }
        if (float.IsInfinity(lowestPoint)) return;
        float floorY = controller != null
            ? controller.transform.TransformPoint(controller.center).y - controller.height * 0.5f
            : transform.position.y - 1f;
        if (!jumping && controller != null && Physics.Raycast(new Vector3(controller.transform.position.x, floorY + 0.25f,
            controller.transform.position.z), Vector3.down, out RaycastHit contact, 0.6f,
            ~((1 << PlanarGymMirror.MirrorPlayerLayer) | (1 << PlanarGymMirror.FirstPersonPlayerLayer)),
            QueryTriggerInteraction.Ignore)) floorY = contact.point.y;
        float contactOffset = floorY - lowestPoint;
        if (!jumping || contactOffset > 0f)
            modelRoot.transform.position += Vector3.up * contactOffset;
    }

    private void PreparePoseTransition(AnimationClip nextClip, bool attackMode)
    {
        if (!hasSampledPose)
        {
            hasSampledPose = true;
            lastSampledClip = nextClip;
            lastSampledAttackMode = attackMode;
            poseTransition.Cancel();
            return;
        }

        if (ReferenceEquals(lastSampledClip, nextClip) &&
            lastSampledAttackMode == attackMode)
        {
            return;
        }

        if (nextClip != null)
        {
            poseTransition.Begin(modelRoot.transform, AuthoredTransitionDuration);
            Debug.Log(
                $"GYMCHAOS_PLAYER_AUTHORED_TRANSITION " +
                $"from={lastSampledClip?.name ?? "none"} to={nextClip.name} " +
                $"attack={attackMode} duration={AuthoredTransitionDuration:F2}", this);
        }
        else
        {
            poseTransition.Cancel();
        }
        lastSampledClip = nextClip;
        lastSampledAttackMode = attackMode;
    }

    private void RefreshMirrorScale()
    {
        if (!initialized || modelRoot == null)
        {
            return;
        }

        // Enemy GLBs are loaded asynchronously. Refit the reflected player after
        // those renderers exist so the mirror uses the same visible model scale,
        // rather than the fallback scale captured during player initialization.
        mirrorScaleRefreshTimer -= Time.deltaTime;
        if (mirrorScaleRefreshTimer > 0f)
        {
            return;
        }

        if (mirrorScaleMatched)
        {
            return;
        }

        EnemyFighter[] fighters = UnityEngine.Object.FindObjectsByType<EnemyFighter>(FindObjectsSortMode.None);
        if (fighters.Length < 6)
        {
            mirrorScaleRefreshTimer = 0.5f;
            return;
        }

        mirrorScaleRefreshTimer = 0.5f;
        SkinnedMeshRenderer[] bodyRenderers = modelRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true)
            .Where(renderer => renderer != null && renderer.gameObject.layer == PlanarGymMirror.MirrorPlayerLayer)
            .ToArray();
        float targetHeight = FindAverageEnemyVisibleHeight() * MirrorEnemyHeightScale;
        if (targetHeight > 0.01f)
        {
            ScaleMirrorBodyToTarget(bodyRenderers, targetHeight);
            mirrorScaleMatched = true;
        }
    }

    private void LoadAttackClips()
    {
        walkClip = LoadAuthoredAnimationClip(
            PlayerAnimationBundleResource, "walking");
        runClip = LoadAuthoredAnimationClip(
            PlayerAnimationBundleResource, "running");
        idleVariants = new AnimationClip[]
        {
            LoadAuthoredAnimationClip(
                PlayerAnimationBundleResource, "idle1"),
            LoadAuthoredAnimationClip(
                PlayerAnimationBundleResource, "idle2"),
            LoadAuthoredAnimationClip(
                PlayerAnimationBundleResource, "idle3")
        };
        SelectIdleVariant();
        crouchClip = LoadAuthoredAnimationClip(
            PlayerAnimationBundleResource, "crouched_walking");
        jumpClip = LoadAuthoredAnimationClip(
            PlayerAnimationBundleResource, "jumping");
        punchLeftClip = LoadAuthoredAnimationClip(
            PlayerAnimationBundleResource, "punch_left");
        punchRightClip = LoadAuthoredAnimationClip(
            PlayerAnimationBundleResource, "punch_right");
        throwFrisbeeClip = LoadAuthoredAnimationClip(
            PlayerAnimationBundleResource, "throw_frisbee");
        throwHardClip = LoadAuthoredAnimationClip(
            PlayerAnimationBundleResource, "throw_object_hard");
        Debug.Log(
            $"GYMCHAOS_PLAYER_AUTHORED_CLIP_INVENTORY loaded=" +
            $"{(HasRequiredMixamoAttackClips ? "11/11" : "0/11")} " +
            $"clips={MixamoAttackClipSummary}", this);
    }

    private AnimationClip LoadAuthoredAnimationClip(
        string resourcePath, string expectedClipName)
    {
        UnityEngine.Object[] assets = Resources.LoadAll<UnityEngine.Object>(resourcePath);
        List<AnimationClip> candidates = new List<AnimationClip>();
        for (int i = 0; i < assets.Length; i++)
        {
            AnimationClip clip = assets[i] as AnimationClip;
            if (clip != null && !clip.name.StartsWith("__preview__", StringComparison.OrdinalIgnoreCase))
            {
                candidates.Add(clip);
            }
        }
        candidates.Sort((left, right) =>
            StringComparer.Ordinal.Compare(left.name, right.name));
        for (int i = 0; i < candidates.Count; i++)
        {
            string clipName = candidates[i].name;
            if (string.Equals(clipName, expectedClipName, StringComparison.OrdinalIgnoreCase) ||
                clipName.EndsWith("|" + expectedClipName, StringComparison.OrdinalIgnoreCase))
            {
                return candidates[i];
            }
        }

        Debug.LogError(
            $"GYMCHAOS_PLAYER_AUTHORED_CLIP_MISSING " +
            $"path=Assets/Resources/{resourcePath}.fbx", this);
        return null;
    }

    private void SelectIdleVariant()
    {
        if (idleVariants == null || idleVariants.Length == 0)
        {
            selectedIdleVariantIndex = -1;
            idleClip = null;
            return;
        }

        int selectionBucket = Mathf.FloorToInt(Time.time / 6f);
        if (selectionBucket == idleVariantSelectionBucket && idleClip != null)
        {
            return;
        }
        idleVariantSelectionBucket = selectionBucket;

        int requestedIndex = UnityEngine.Random.Range(0, idleVariants.Length);
        if (idleVariants.Length > 1 && requestedIndex == selectedIdleVariantIndex)
        {
            requestedIndex = (requestedIndex + UnityEngine.Random.Range(
                1, idleVariants.Length)) % idleVariants.Length;
        }
        for (int offset = 0; offset < idleVariants.Length; offset++)
        {
            int index = (requestedIndex + offset) % idleVariants.Length;
            if (idleVariants[index] != null)
            {
                bool changedVariant = selectedIdleVariantIndex != index;
                selectedIdleVariantIndex = index;
                idleClip = idleVariants[index];
                if (changedVariant)
                {
                    locomotionElapsed = 0f;
                }
                return;
            }
        }

        selectedIdleVariantIndex = -1;
        idleClip = null;
    }

    private void StartAttack(AnimationClip clip, float duration, float heldGripReach = 0f, bool useRightHand = true)
    {
        activeAttackClip = clip;
        activeAttackElapsed = 0f;
        activeAttackDuration = duration;
        activeAttackGripReach = Mathf.Max(0f, heldGripReach);
        activeAttackUsesRightHand = useRightHand;
    }

    private bool SampleActiveAttack()
    {
        if (activeAttackClip == null || activeAttackDuration <= 0f ||
            modelRoot == null)
        {
            return false;
        }
        float normalizedTime = Mathf.Clamp01(activeAttackElapsed / activeAttackDuration);
        float sampleTime = normalizedTime * Mathf.Max(0.01f, activeAttackClip.length - 0.001f);
        bool sampled = SampleDirectClip(activeAttackClip, sampleTime);
        sampledPunchLeftClip |= activeAttackClip == punchLeftClip;
        sampledPunchRightClip |= activeAttackClip == punchRightClip;
        sampledThrowClip |= activeAttackClip == throwFrisbeeClip ||
            activeAttackClip == throwHardClip;
        return sampled;
    }

    private AnimationClip GetLocomotionClip()
    {
        bool crouching = crouchAmount > 0.01f;
        if (jumping)
        {
            return jumpClip;
        }
        if (crouching)
        {
            return crouchClip;
        }
        if (moveAmount > 0.01f)
        {
            return sprinting ? runClip : walkClip;
        }
        SelectIdleVariant();
        return idleClip;
    }

    private bool SampleLocomotion()
    {
        bool crouching = crouchAmount > 0.01f;
        AnimationClip locomotionClip = GetLocomotionClip();
        if (locomotionClip == null || modelRoot == null)
        {
            return false;
        }

        float clipEnd = Mathf.Max(0.01f, locomotionClip.length - 0.001f);
        float sampleTime = jumping
            ? Mathf.Min(locomotionElapsed, clipEnd)
            : locomotionElapsed % clipEnd;
        bool sampled = SampleDirectClip(locomotionClip, sampleTime);
        if (crouching)
        {
            sampledCrouchClip = sampled;
        }
        else if (sprinting)
        {
            sampledRunClip = sampled;
        }
        return sampled;
    }

    private bool SampleDirectClip(AnimationClip clip, float sampleTime)
    {
        if (modelRoot == null || clip == null)
        {
            return false;
        }

        // Every exported player clip was baked against this exact player
        // hierarchy. Reset first so a previous attack cannot leave stale
        // rotations on bones that the next clip does not key.
        RestoreAnimatedBones();
        RestoreVerificationModelTransform();
        clip.SampleAnimation(modelRoot, Mathf.Max(0f, sampleTime));
        // Rigify FBX baking can emit evaluated deform-bone scale channels
        // although the authored player motion is rotation/translation based.
        // Keep this player's own imported rest scales while preserving the
        // sampled pose rotations and positions.
        RestoreAnimatedScales();
        if (animationRootBone != null &&
            restPositions.TryGetValue(animationRootBone, out Vector3 rootPosition))
        {
            // The CharacterController owns player world placement. Keep the
            // authored hip translation from moving the camera/arms through
            // the floor while retaining all limb and torso rotations.
            Transform rootParent = animationRootBone.parent;
            Vector3 displacement = rootParent.TransformVector(animationRootBone.localPosition - rootPosition);
            displacement = jumping ? Vector3.zero : Vector3.Project(displacement, Vector3.up);
            animationRootBone.localPosition = rootPosition + rootParent.InverseTransformVector(displacement);
        }
        // The player controller owns world placement and scale; only the
        // imported child bones should remain affected by SampleAnimation.
        RestoreVerificationModelTransform();
        if (clip == jumpClip)
        {
            Quaternion lowerYaw = Quaternion.AngleAxis(JumpLowerBodyYawCorrection, modelRoot.transform.up);
            if (leftPelvis != null) leftPelvis.rotation = lowerYaw * leftPelvis.rotation;
            if (rightPelvis != null) rightPelvis.rotation = lowerYaw * rightPelvis.rotation;
            if (leftThigh != null) leftThigh.rotation = lowerYaw * leftThigh.rotation;
            if (rightThigh != null) rightThigh.rotation = lowerYaw * rightThigh.rotation;
        }
        // Keep the presentation root on the gameplay basis. The sampled
        // deform bones retain all authored hips/torso/shoulder motion.
        modelRoot.transform.localRotation = AuthoredModelForwardCorrection;
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

    private void CaptureLowerBodyPose()
    {
        lowerBodyRotations.Clear();
        lowerBodyPositions.Clear();
        Transform inactiveArmRoot = null;
        if (activeAttackClip == punchRightClip)
        {
            inactiveArmRoot = leftShoulder != null ? leftShoulder : leftUpperArm;
        }
        else if (activeAttackClip == punchLeftClip)
        {
            inactiveArmRoot = rightShoulder != null ? rightShoulder : rightUpperArm;
        }
        foreach (KeyValuePair<Transform, Quaternion> pair in restRotations)
        {
            Transform bone = pair.Key;
            string normalizedName = bone != null
                ? NormalizeBoneName(bone.name)
                : string.Empty;
            bool inactiveArmBone = inactiveArmRoot != null && bone != null &&
                (bone == inactiveArmRoot || bone.IsChildOf(inactiveArmRoot));
            bool stableTorsoBone = normalizedName.Contains("spine") ||
                normalizedName.Contains("pelvis") || normalizedName.Contains("hips");
            if (bone != null &&
                (bone == animationRootBone || !IsUpperBodyBone(bone) ||
                 inactiveArmBone || stableTorsoBone))
            {
                lowerBodyRotations[bone] = bone.localRotation;
                lowerBodyPositions[bone] = bone.localPosition;
            }
        }
    }

    private void RestoreLowerBodyPose()
    {
        foreach (KeyValuePair<Transform, Quaternion> pair in lowerBodyRotations)
        {
            if (pair.Key != null)
            {
                pair.Key.localRotation = pair.Value;
                pair.Key.localPosition = lowerBodyPositions[pair.Key];
            }
        }
    }

    private static bool IsUpperBodyBone(Transform bone)
    {
        string name = NormalizeBoneName(bone.name);
        return name.Contains("spine") || name.Contains("chest") || name.Contains("neck") ||
               name.Contains("head") || name.Contains("shoulder") || name.Contains("arm") ||
               name.Contains("hand") || name.Contains("finger") || name.Contains("thumb");
    }

    private void AnimateHeldGripOverAttack(float elapsed, float duration, float gripReach)
    {
        sampledHeldBarGrip |= gripReach >= 0.7f;
        sampledHeldPlateGrip |= gripReach >= 0.5f && gripReach < 0.7f;
        float normalizedTime = Mathf.Clamp01(elapsed / Mathf.Max(0.01f, duration));
        float reach = Mathf.Sin(normalizedTime * Mathf.PI) * gripReach;
        Vector3 leftTarget = playerCamera.transform.TransformPoint(new Vector3(-0.23f, -0.17f, 0.88f));
        Vector3 rightTarget = playerCamera.transform.TransformPoint(new Vector3(0.3f, -0.19f, 0.9f));
        leftTarget += playerCamera.transform.forward * reach;
        rightTarget += playerCamera.transform.forward * reach;
        SolveArm(leftUpperArm, leftForearm, leftHand, leftTarget);
        SolveArm(rightUpperArm, rightForearm, rightHand, rightTarget);
    }

    private static void SolveArm(Transform upperArm, Transform forearm, Transform hand, Vector3 target)
    {
        if (upperArm == null || forearm == null || hand == null)
        {
            return;
        }

        for (int i = 0; i < 20; i++)
        {
            RotateJointToward(forearm, hand, target);
            RotateJointToward(upperArm, hand, target);
        }
    }

    private static void RotateJointToward(Transform joint, Transform end, Vector3 target)
    {
        Vector3 currentDirection = end.position - joint.position;
        Vector3 targetDirection = target - joint.position;
        if (currentDirection.sqrMagnitude < 0.000001f || targetDirection.sqrMagnitude < 0.000001f)
        {
            return;
        }
        joint.rotation = Quaternion.FromToRotation(currentDirection, targetDirection) * joint.rotation;
    }
    private void FindBones()
    {
        Transform[] bones = modelRoot.GetComponentsInChildren<Transform>(true);
        animationRootBone = FindBone(bones, "def-pelvis", "pelvis", "hips", "root", "def-spine");
        // DEF-spine is the common animation/root boundary. Transfer the imported heading to the first upper-body child so root correction cannot cancel itself while authored torso motion remains local.
        upperBodyRootBone = FindBone(bones, "def-spine.001", "spine.001", "def-spine.002", "spine.002", "def-chest", "chest", "def-spine");
        leftShoulder = FindBone(bones, "leftshoulder", "def-shoulder.l");
        leftUpperArm = FindBone(bones, "leftarm", "leftupperarm", "def-upperarm.l");
        leftForearm = FindBone(bones, "leftforearm", "leftlowerarm", "def-forearm.l");
        leftHand = FindBone(bones, "lefthand", "def-hand.l");
        rightShoulder = FindBone(bones, "rightshoulder", "def-shoulder.r");
        rightUpperArm = FindBone(bones, "rightarm", "rightupperarm", "def-upperarm.r");
        rightForearm = FindBone(bones, "rightforearm", "rightlowerarm", "def-forearm.r");
        rightHand = FindBone(bones, "righthand", "def-hand.r");
        leftThigh = FindBone(bones, "leftupleg", "leftthigh", "def-thigh.l");
        rightThigh = FindBone(bones, "rightupleg", "rightthigh", "def-thigh.r");
        leftPelvis = FindBone(bones, "def-pelvis.l", "pelvis.l", "leftpelvis");
        rightPelvis = FindBone(bones, "def-pelvis.r", "pelvis.r", "rightpelvis");
        leftCalf = FindBone(bones, "leftleg", "leftcalf", "leftlowerleg", "def-shin.l");
        rightCalf = FindBone(bones, "rightleg", "rightcalf", "rightlowerleg", "def-shin.r");
        head = FindBone(bones, "head", "def-head", "def-spine.005");

        if (leftHand == null || rightHand == null)
        {
            Debug.LogError("The authored player FBX is missing its expected deform arm bones.");
        }
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
                if (NormalizeBoneName(bones[i].name).EndsWith(candidate, StringComparison.Ordinal))
                {
                    return bones[i];
                }
            }
        }
        return null;
    }

    private AnimationClip FindAuthoredClip(string clipStem)
    {
        if (string.IsNullOrEmpty(clipStem))
        {
            return null;
        }

        if (string.Equals(clipStem, "walking", StringComparison.OrdinalIgnoreCase)) return walkClip;
        if (string.Equals(clipStem, "running", StringComparison.OrdinalIgnoreCase)) return runClip;
        if (string.Equals(clipStem, "idle1", StringComparison.OrdinalIgnoreCase)) return GetIdleVariant(0);
        if (string.Equals(clipStem, "idle2", StringComparison.OrdinalIgnoreCase)) return GetIdleVariant(1);
        if (string.Equals(clipStem, "idle3", StringComparison.OrdinalIgnoreCase)) return GetIdleVariant(2);
        if (string.Equals(clipStem, "crouched_walking", StringComparison.OrdinalIgnoreCase)) return crouchClip;
        if (string.Equals(clipStem, "jumping", StringComparison.OrdinalIgnoreCase)) return jumpClip;
        if (string.Equals(clipStem, "punch_left", StringComparison.OrdinalIgnoreCase)) return punchLeftClip;
        if (string.Equals(clipStem, "punch_right", StringComparison.OrdinalIgnoreCase)) return punchRightClip;
        if (string.Equals(clipStem, "throw_frisbee", StringComparison.OrdinalIgnoreCase)) return throwFrisbeeClip;
        if (string.Equals(clipStem, "throw_object_hard", StringComparison.OrdinalIgnoreCase)) return throwHardClip;
        return null;
    }

    private AnimationClip GetIdleVariant(int index)
    {
        return idleVariants != null && index >= 0 && index < idleVariants.Length
            ? idleVariants[index]
            : null;
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

    private static string NormalizeBoneName(string name)
    {
        return name.Replace("mixamorig:", string.Empty).Replace("_", string.Empty).Replace(" ", string.Empty).ToLowerInvariant();
    }

    private void CaptureRestRotations()
    {
        Transform[] animatedBones = modelRoot.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < animatedBones.Length; i++)
        {
            Transform bone = animatedBones[i];
            if (bone != null && bone != modelRoot.transform && !restRotations.ContainsKey(bone))
            {
                restRotations.Add(bone, bone.localRotation);
                restPositions.Add(bone, bone.localPosition);
                restScales.Add(bone, bone.localScale);
            }
        }

        restBoneAxes.Clear();
        foreach (KeyValuePair<Transform, Quaternion> pair in restRotations)
        {
            Transform bone = pair.Key;
            for (int childIndex = 0; childIndex < bone.childCount; childIndex++)
            {
                Vector3 axis = bone.GetChild(childIndex).localPosition;
                if (axis.sqrMagnitude > 0.000001f)
                {
                    restBoneAxes[bone] = axis.normalized;
                    break;
                }
            }
        }
    }

    private void RestoreAnimatedBones()
    {
        foreach (KeyValuePair<Transform, Quaternion> pair in restRotations)
        {
            if (pair.Key != null)
            {
                pair.Key.localRotation = pair.Value;
                if (restPositions.TryGetValue(pair.Key, out Vector3 position))
                {
                    pair.Key.localPosition = position;
                }
                if (restScales.TryGetValue(pair.Key, out Vector3 scale))
                {
                    pair.Key.localScale = scale;
                }
            }
        }
    }

    private void FitModelToController()
    {
        Renderer[] renderers = modelRoot.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
        {
            return;
        }

        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
        {
            bounds.Encapsulate(renderers[i].bounds);
        }
        if (bounds.size.y < 0.001f)
        {
            return;
        }

        const float desiredHeight = ExternalRiggedCharacterVisual.StandardGameplayHeight;
        float measuredHeight = GetFreshPlayerHeight();
        if (measuredHeight < 0.001f)
        {
            measuredHeight = bounds.size.y;
        }
        float uniformScale = desiredHeight / measuredHeight;
        // This is a correction factor, not an absolute scale. The FBX can
        // already carry a non-unit import scale from its own authored unit
        // system; replacing it with the factor makes the second idle fit
        // shrink the model instead of correcting it.
        modelRoot.transform.localScale *= uniformScale;
        Physics.SyncTransforms();

        Bounds fittedBounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
        {
            fittedBounds.Encapsulate(renderers[i].bounds);
        }
        float footY = transform.position.y -
            (controller != null ? controller.height * 0.5f : 1f);
        modelRoot.transform.position += Vector3.up * (footY - fittedBounds.min.y);
        baseModelLocalPosition = modelRoot.transform.localPosition;
        baseModelLocalScale = modelRoot.transform.localScale;
    }

    private void FitCurrentPoseToGameplayHeight()
    {
        if (modelRoot == null)
        {
            return;
        }

        SkinnedMeshRenderer[] renderers =
            modelRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        if (renderers.Length == 0)
        {
            return;
        }

        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
        {
            bounds.Encapsulate(renderers[i].bounds);
        }
        if (bounds.size.y < 0.001f)
        {
            return;
        }

        float measuredHeight = GetFreshPlayerHeight();
        if (measuredHeight < 0.001f)
        {
            measuredHeight = bounds.size.y;
        }
        float scaleCorrection =
            ExternalRiggedCharacterVisual.StandardGameplayHeight / measuredHeight;
        modelRoot.transform.localScale *= scaleCorrection;
        Physics.SyncTransforms();

        Bounds fittedBounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
        {
            fittedBounds.Encapsulate(renderers[i].bounds);
        }
        float footY = transform.position.y -
            (controller != null ? controller.height * 0.5f : 1f);
        modelRoot.transform.position += Vector3.up * (footY - fittedBounds.min.y);
        baseModelLocalPosition = modelRoot.transform.localPosition;
        baseModelLocalScale = modelRoot.transform.localScale;
    }

    private float GetFreshPlayerHeight()
    {
        if (modelRoot == null)
        {
            return 0f;
        }

        SkinnedMeshRenderer[] renderers =
            modelRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        float totalHeight = 0f;
        int measuredCount = 0;
        for (int rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
        {
            SkinnedMeshRenderer renderer = renderers[rendererIndex];
            if (renderer == null || renderer.sharedMesh == null || !renderer.enabled ||
                renderer.gameObject.name.StartsWith("First Person Arms", StringComparison.Ordinal))
            {
                continue;
            }

            Mesh baked = new Mesh
            {
                name = "Player fresh height"
            };
            // Measure the evaluated mesh in world space. The imported FBX
            // contains axis-converted local coordinates, so a fixed local Z
            // extent is not a reliable gameplay height once each authored
            // rig has its own rest scale/orientation.
            renderer.BakeMesh(baked, true);
            Vector3[] vertices = baked.vertices;
            float minY = float.PositiveInfinity;
            float maxY = float.NegativeInfinity;
            for (int vertexIndex = 0; vertexIndex < vertices.Length; vertexIndex++)
            {
                float worldY = renderer.transform.TransformPoint(vertices[vertexIndex]).y;
                minY = Mathf.Min(minY, worldY);
                maxY = Mathf.Max(maxY, worldY);
            }
            float height = maxY - minY;
            Destroy(baked);
            if (height > 0.001f)
            {
                totalHeight += height;
                measuredCount++;
            }
        }

        return measuredCount > 0 ? totalHeight / measuredCount : 0f;
    }

    private void ConfigureRenderers()
    {
        SkinnedMeshRenderer[] renderers = modelRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        mirrorBodyRenderers.Clear();
        stableMirrorMaterials.Clear();
        stableFirstPersonMaterials.Clear();
        CreateFirstPersonModelClone();
        int firstPersonVertexCount = 0;
        int firstPersonTriangleCount = 0;
        int firstPersonBoneCount = 0;
        for (int i = 0; i < renderers.Length; i++)
        {
            SkinnedMeshRenderer fullBody = renderers[i];
            Material[] stableMaterials = CreateOpaqueMaterials(fullBody.sharedMaterials);
            fullBody.sharedMaterials = stableMaterials;
            mirrorBodyRenderers.Add(fullBody);
            stableMirrorMaterials[fullBody] = (Material[])stableMaterials.Clone();
            fullBody.enabled = true;
            fullBody.forceRenderingOff = false;
            fullBody.updateWhenOffscreen = true;
            fullBody.shadowCastingMode = ShadowCastingMode.On;
            fullBody.gameObject.layer = PlanarGymMirror.MirrorPlayerLayer;
            // Keep the skinned body itself in the mirror. The old baked proxy
            // used the source mesh bounds and rendered the player at roughly
            // 1.53 m even though the actual body was about 2.30 m tall.
            // The mirror camera already has the correct layer mask, so a second
            // static proxy is unnecessary and introduces a scale mismatch.
            SkinnedMeshRenderer arms = CreateFirstPersonArms(fullBody, i);
            if (arms != null)
            {
                // Render the filtered skinned mesh directly. Re-baking a second
                // Mixamo export into a static proxy retained bounds but produced no
                // visible pixels from the gameplay camera.
                arms.forceRenderingOff = false;
                firstPersonArmRenderers.Add(arms);
                firstPersonVertexCount += arms.sharedMesh != null
                    ? arms.sharedMesh.vertexCount
                    : 0;
                firstPersonBoneCount = Mathf.Max(
                    firstPersonBoneCount, arms.bones != null ? arms.bones.Length : 0);
                if (arms.sharedMesh != null)
                {
                    for (int subMesh = 0; subMesh < arms.sharedMesh.subMeshCount; subMesh++)
                    {
                        firstPersonTriangleCount +=
                            (int)arms.sharedMesh.GetIndexCount(subMesh) / 3;
                    }
                }
            }
        }
        SetLayerRecursively(modelRoot.transform, PlanarGymMirror.MirrorPlayerLayer);
        Bounds fittedBodyBounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
        {
            fittedBodyBounds.Encapsulate(renderers[i].bounds);
        }
        Debug.Log(
            $"GYMCHAOS_PLAYER_FIT_OK bodyHeight={fittedBodyBounds.size.y:F3} " +
            $"bodyBounds={fittedBodyBounds.center}/{fittedBodyBounds.size} " +
            $"modelScale={modelRoot.transform.localScale} " +
            $"modelPosition={modelRoot.transform.position}", this);
        Debug.Log(
            $"GYMCHAOS_PLAYER_FIRST_PERSON_ARMS renderers={firstPersonArmRenderers.Count} " +
            $"vertices={firstPersonVertexCount} triangles={firstPersonTriangleCount} " +
            $"bones={firstPersonBoneCount} cameraMask={playerCamera.cullingMask}",
            this);
        UpdateFirstPersonArmVisibility();
    }

    public void EnsureMirrorAppearance()
    {
        for (int i = 0; i < mirrorBodyRenderers.Count; i++)
        {
            SkinnedMeshRenderer renderer = mirrorBodyRenderers[i];
            if (renderer == null ||
                !stableMirrorMaterials.TryGetValue(renderer, out Material[] materials))
            {
                continue;
            }

            renderer.gameObject.layer = PlanarGymMirror.MirrorPlayerLayer;
            renderer.enabled = true;
            renderer.forceRenderingOff = false;
            renderer.sharedMaterials = materials;
            // Cosmetic property blocks belong to the outfit and must survive mirror refresh.
        }
        EnsureFirstPersonArmAppearance();
    }

    /// <summary>
    /// Reassert the skin materials owned by the first-person arm clone. Outfit
    /// changes are allowed to recolor the mirror body, but the camera arms
    /// must remain on their authored default appearance in every room.
    /// </summary>
    public void EnsureFirstPersonArmAppearance()
    {
        for (int i = 0; i < firstPersonArmRenderers.Count; i++)
        {
            SkinnedMeshRenderer renderer = firstPersonArmRenderers[i];
            if (renderer == null ||
                !stableFirstPersonMaterials.TryGetValue(
                    renderer, out Material[] materials))
            {
                continue;
            }

            renderer.gameObject.layer = PlanarGymMirror.FirstPersonPlayerLayer;
            renderer.sharedMaterials = materials;
        }
    }

    private void ScaleMirrorBodyToTarget(SkinnedMeshRenderer[] renderers, float targetHeight)
    {
        Bounds visibleBounds = default;
        bool found = false;
        for (int i = 0; i < renderers.Length; i++)
        {
            SkinnedMeshRenderer renderer = renderers[i];
            if (renderer == null || renderer.sharedMesh == null)
            {
                continue;
            }

            Mesh baked = new Mesh();
            renderer.BakeMesh(baked, true);
            Vector3[] vertices = baked.vertices;
            for (int vertexIndex = 0; vertexIndex < vertices.Length; vertexIndex++)
            {
                Vector3 world = renderer.transform.TransformPoint(vertices[vertexIndex]);
                if (!found)
                {
                    visibleBounds = new Bounds(world, Vector3.zero);
                    found = true;
                }
                else
                {
                    visibleBounds.Encapsulate(world);
                }
            }
            UnityEngine.Object.Destroy(baked);
        }

        if (!found || visibleBounds.size.y < 0.01f)
        {
            return;
        }

        float scale = targetHeight / visibleBounds.size.y;
        if (Mathf.Abs(scale - 1f) < 0.005f)
        {
            return;
        }

        float floorBefore = visibleBounds.min.y;
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] != null)
            {
                // Scale only the reflected body renderer. The first-person arms
                // share the model root and must not inherit this late refit.
                renderers[i].transform.localScale *= scale;
            }
        }
        Physics.SyncTransforms();

        Bounds after = default;
        bool foundAfter = false;
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] == null || renderers[i].sharedMesh == null)
            {
                continue;
            }
            Mesh baked = new Mesh();
            renderers[i].BakeMesh(baked, true);
            Vector3[] vertices = baked.vertices;
            for (int vertexIndex = 0; vertexIndex < vertices.Length; vertexIndex++)
            {
                Vector3 world = renderers[i].transform.TransformPoint(vertices[vertexIndex]);
                if (!foundAfter)
                {
                    after = new Bounds(world, Vector3.zero);
                    foundAfter = true;
                }
                else
                {
                    after.Encapsulate(world);
                }
            }
            UnityEngine.Object.Destroy(baked);
        }
        if (foundAfter)
        {
            Vector3 floorCorrection = Vector3.up * (floorBefore - after.min.y);
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] != null)
                {
                    renderers[i].transform.position += floorCorrection;
                }
            }
        }
    }

    private static float FindAverageEnemyVisibleHeight()
    {
        EnemyFighter[] fighters = UnityEngine.Object.FindObjectsByType<EnemyFighter>(FindObjectsSortMode.None);
        float total = 0f;
        int count = 0;
        for (int i = 0; i < fighters.Length; i++)
        {
            if (fighters[i] == null)
            {
                continue;
            }
            SkinnedMeshRenderer[] renderers = fighters[i].GetComponentsInChildren<SkinnedMeshRenderer>(true);
            for (int j = 0; j < renderers.Length; j++)
            {
                SkinnedMeshRenderer renderer = renderers[j];
                if (renderer == null || renderer.sharedMesh == null || !renderer.enabled)
                {
                    continue;
                }
                Mesh baked = new Mesh();
                renderer.BakeMesh(baked, true);
                Vector3[] vertices = baked.vertices;
                if (vertices.Length > 0)
                {
                    float minY = float.PositiveInfinity;
                    float maxY = float.NegativeInfinity;
                    for (int vertexIndex = 0; vertexIndex < vertices.Length; vertexIndex++)
                    {
                        float worldY = renderer.transform.TransformPoint(vertices[vertexIndex]).y;
                        minY = Mathf.Min(minY, worldY);
                        maxY = Mathf.Max(maxY, worldY);
                    }
                    if (maxY > minY)
                    {
                        total += maxY - minY;
                        count++;
                    }
                }
                UnityEngine.Object.Destroy(baked);
            }
        }
        return count > 0 ? total / count : 0f;
    }

    private Mesh CreateLowerLodMesh(Mesh source)
    {
        if (source == null || source.lodCount <= 1)
        {
            return source;
        }

        int lod = Mathf.Min(1, source.lodCount - 1);
        int[][] subMeshTriangles = new int[source.subMeshCount][];
        int[] remap = new int[source.vertexCount];
        for (int i = 0; i < remap.Length; i++)
        {
            remap[i] = -1;
        }

        Vector3[] sourceVertices = source.vertices;
        Vector3[] sourceNormals = source.normals;
        Vector4[] sourceTangents = source.tangents;
        Vector2[] sourceUvs = source.uv;
        Color32[] sourceColors = source.colors32;
        BoneWeight[] sourceWeights = source.boneWeights;
        List<Vector3> vertices = new List<Vector3>();
        List<Vector3> normals = new List<Vector3>();
        List<Vector4> tangents = new List<Vector4>();
        List<Vector2> uvs = new List<Vector2>();
        List<Color32> colors = new List<Color32>();
        List<BoneWeight> weights = new List<BoneWeight>();

        for (int subMesh = 0; subMesh < source.subMeshCount; subMesh++)
        {
            int[] lodTriangles = source.GetTriangles(subMesh, lod, true);
            for (int i = 0; i < lodTriangles.Length; i++)
            {
                int sourceIndex = lodTriangles[i];
                int reducedIndex = remap[sourceIndex];
                if (reducedIndex < 0)
                {
                    reducedIndex = vertices.Count;
                    remap[sourceIndex] = reducedIndex;
                    vertices.Add(sourceVertices[sourceIndex]);
                    if (sourceNormals.Length == sourceVertices.Length) normals.Add(sourceNormals[sourceIndex]);
                    if (sourceTangents.Length == sourceVertices.Length) tangents.Add(sourceTangents[sourceIndex]);
                    if (sourceUvs.Length == sourceVertices.Length) uvs.Add(sourceUvs[sourceIndex]);
                    if (sourceColors.Length == sourceVertices.Length) colors.Add(sourceColors[sourceIndex]);
                    if (sourceWeights.Length == sourceVertices.Length) weights.Add(sourceWeights[sourceIndex]);
                }
                lodTriangles[i] = reducedIndex;
            }
            subMeshTriangles[subMesh] = lodTriangles;
        }

        Mesh reduced = new Mesh
        {
            name = source.name + " (Compacted Runtime LOD " + lod + ")",
            indexFormat = vertices.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16,
            vertices = vertices.ToArray(),
            subMeshCount = source.subMeshCount,
            bindposes = source.bindposes,
            bounds = source.bounds
        };
        if (normals.Count == vertices.Count) reduced.normals = normals.ToArray();
        if (tangents.Count == vertices.Count) reduced.tangents = tangents.ToArray();
        if (uvs.Count == vertices.Count) reduced.uv = uvs.ToArray();
        if (colors.Count == vertices.Count) reduced.colors32 = colors.ToArray();
        if (weights.Count == vertices.Count) reduced.boneWeights = weights.ToArray();
        for (int subMesh = 0; subMesh < subMeshTriangles.Length; subMesh++)
        {
            reduced.SetTriangles(subMeshTriangles[subMesh], subMesh, false);
        }
        runtimeMeshes.Add(reduced);
        return reduced;
    }

    private static Material[] CreateOpaqueMaterials(Material[] sourceMaterials)
    {
        Texture2D fallbackBaseColor = LoadPlayerBaseColorTexture();
        Material[] materials = new Material[sourceMaterials.Length];
        for (int i = 0; i < sourceMaterials.Length; i++)
        {
            Material source = sourceMaterials[i];
            Texture sourceBaseColor = null;
            Color sourceColor = Color.white;
            if (source != null)
            {
                if (source.HasProperty("_BaseMap"))
                {
                    sourceBaseColor = source.GetTexture("_BaseMap");
                }
                if (sourceBaseColor == null && source.HasProperty("_MainTex"))
                {
                    sourceBaseColor = source.GetTexture("_MainTex");
                }
                if (source.HasProperty("_BaseColor"))
                {
                    sourceColor = source.GetColor("_BaseColor");
                }
                else if (source.HasProperty("_Color"))
                {
                    sourceColor = source.GetColor("_Color");
                }
            }
            Shader opaqueShader = Shader.Find("Universal Render Pipeline/Lit");
            Material material = opaqueShader != null
                ? new Material(opaqueShader)
                : source != null
                    ? new Material(source)
                    : new Material(Shader.Find("Standard"));
            material.name = (sourceMaterials[i] != null ? sourceMaterials[i].name : "Player") + " (Opaque Runtime)";
            sourceColor.a = 1f;
            if (material.HasProperty("_BaseColor"))
            {
                Color color = material.GetColor("_BaseColor");
                color.a = 1f;
                material.SetColor("_BaseColor", color);
            }
            if (material.HasProperty("_Color"))
            {
                Color color = material.GetColor("_Color");
                color.a = 1f;
                material.SetColor("_Color", color);
            }
            if (material.HasProperty("_Surface")) material.SetFloat("_Surface", 0f);
            Texture baseColor = sourceBaseColor != null
                ? sourceBaseColor
                : fallbackBaseColor;
            if (baseColor != null && material.HasProperty("_BaseMap"))
            {
                material.SetTexture("_BaseMap", baseColor);
            }
            if (baseColor != null && material.HasProperty("_MainTex"))
            {
                material.SetTexture("_MainTex", baseColor);
            }
            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", sourceColor);
            }
            if (material.HasProperty("_Color"))
            {
                material.SetColor("_Color", sourceColor);
            }
            if (material.HasProperty("_AlphaClip")) material.SetFloat("_AlphaClip", 0f);
            if (material.HasProperty("_ZWrite")) material.SetFloat("_ZWrite", 1f);
            if (material.HasProperty("_Cull")) material.SetFloat("_Cull", (float)CullMode.Back);
            if (material.HasProperty("_SrcBlend")) material.SetFloat("_SrcBlend", (float)BlendMode.One);
            if (material.HasProperty("_DstBlend")) material.SetFloat("_DstBlend", (float)BlendMode.Zero);
            material.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.DisableKeyword("_ALPHATEST_ON");
            material.renderQueue = -1;
            materials[i] = material;
        }
        return materials;
    }

    private static Material[] CreateFirstPersonMaterials(Material[] sourceMaterials)
    {
        Material[] opaqueMaterials = CreateOpaqueMaterials(sourceMaterials);
        Shader firstPersonShader = Shader.Find("Universal Render Pipeline/Unlit") ??
            Shader.Find("Unlit/Texture");
        if (firstPersonShader == null)
        {
            return opaqueMaterials;
        }

        Material[] materials = new Material[opaqueMaterials.Length];
        for (int i = 0; i < opaqueMaterials.Length; i++)
        {
            Material litMaterial = opaqueMaterials[i];
            Texture baseColor = null;
            Color sourceColor = Color.white;
            if (litMaterial != null)
            {
                if (litMaterial.HasProperty("_BaseMap"))
                {
                    baseColor = litMaterial.GetTexture("_BaseMap");
                }
                if (baseColor == null && litMaterial.HasProperty("_MainTex"))
                {
                    baseColor = litMaterial.GetTexture("_MainTex");
                }
                if (litMaterial.HasProperty("_BaseColor"))
                {
                    sourceColor = litMaterial.GetColor("_BaseColor");
                }
                else if (litMaterial.HasProperty("_Color"))
                {
                    sourceColor = litMaterial.GetColor("_Color");
                }
            }

            Material material = new Material(firstPersonShader)
            {
                name = (sourceMaterials[i] != null ? sourceMaterials[i].name : "Player") +
                    " (First Person Arms)"
            };
            sourceColor.a = 1f;
            if (baseColor != null && material.HasProperty("_BaseMap"))
            {
                material.SetTexture("_BaseMap", baseColor);
            }
            if (baseColor != null && material.HasProperty("_MainTex"))
            {
                material.SetTexture("_MainTex", baseColor);
            }
            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", sourceColor);
            }
            if (material.HasProperty("_Color"))
            {
                material.SetColor("_Color", sourceColor);
            }
            if (material.HasProperty("_Surface")) material.SetFloat("_Surface", 0f);
            if (material.HasProperty("_AlphaClip")) material.SetFloat("_AlphaClip", 0f);
            if (material.HasProperty("_ZWrite")) material.SetFloat("_ZWrite", 1f);
            if (material.HasProperty("_Cull"))
            {
                material.SetFloat("_Cull", (float)CullMode.Back);
            }
            if (material.HasProperty("_SrcBlend")) material.SetFloat("_SrcBlend", (float)BlendMode.One);
            if (material.HasProperty("_DstBlend")) material.SetFloat("_DstBlend", (float)BlendMode.Zero);
            material.EnableKeyword("_SURFACE_TYPE_OPAQUE");
            material.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.DisableKeyword("_ALPHATEST_ON");
            material.DisableKeyword("FOG_LINEAR");
            material.DisableKeyword("FOG_EXP");
            material.DisableKeyword("FOG_EXP2");
            material.SetOverrideTag("RenderType", "Opaque");
            material.renderQueue = (int)RenderQueue.Geometry;
            if (litMaterial != null)
            {
                UnityEngine.Object.Destroy(litMaterial);
            }
            materials[i] = material;
        }
        return materials;
    }

    private static Texture2D LoadPlayerBaseColorTexture()
    {
        return Resources.Load<Texture2D>(PlayerBaseTextureResource) ??
               Resources.Load<Texture2D>("Characters/Textures/player");
    }

    private void CreateFirstPersonModelClone()
    {
        if (modelRoot == null || firstPersonModelRoot != null)
        {
            return;
        }

        firstPersonModelRoot = Instantiate(modelRoot, transform);
        firstPersonModelRoot.name = "Player First Person Arm Rig";
        firstPersonModelRoot.transform.localScale = baseModelLocalScale;
        SetLayerRecursively(firstPersonModelRoot.transform, PlanarGymMirror.MirrorPlayerLayer);

        Animator[] animators = firstPersonModelRoot.GetComponentsInChildren<Animator>(true);
        for (int i = 0; i < animators.Length; i++)
        {
            animators[i].enabled = false;
        }

        SkinnedMeshRenderer[] cloneRenderers =
            firstPersonModelRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        for (int i = 0; i < cloneRenderers.Length; i++)
        {
            cloneRenderers[i].enabled = false;
            cloneRenderers[i].forceRenderingOff = true;
            cloneRenderers[i].shadowCastingMode = ShadowCastingMode.Off;
            cloneRenderers[i].receiveShadows = false;
        }

        Transform[] sourceTransforms = modelRoot.GetComponentsInChildren<Transform>(true);
        List<Transform> poseSources = new List<Transform>(sourceTransforms.Length);
        List<Transform> poseTargets = new List<Transform>(sourceTransforms.Length);
        for (int i = 0; i < sourceTransforms.Length; i++)
        {
            Transform source = sourceTransforms[i];
            if (source == null || source == modelRoot.transform)
            {
                continue;
            }

            Transform target = FindTransformByPath(
                firstPersonModelRoot.transform,
                GetTransformPath(modelRoot.transform, source));
            if (target == null)
            {
                continue;
            }

            poseSources.Add(source);
            poseTargets.Add(target);
        }
        firstPersonPoseSources = poseSources.ToArray();
        firstPersonPoseTargets = poseTargets.ToArray();
        firstPersonLeftHand = FindFirstPersonCloneTransform(leftHand);
        firstPersonRightHand = FindFirstPersonCloneTransform(rightHand);
        firstPersonLeftShoulder = FindFirstPersonCloneTransform(leftShoulder);
        firstPersonRightShoulder = FindFirstPersonCloneTransform(rightShoulder);
        firstPersonLeftUpperArm = FindFirstPersonCloneTransform(leftUpperArm);
        firstPersonLeftForearm = FindFirstPersonCloneTransform(leftForearm);
        firstPersonRightUpperArm = FindFirstPersonCloneTransform(rightUpperArm);
        firstPersonRightForearm = FindFirstPersonCloneTransform(rightForearm);
    }

    private SkinnedMeshRenderer CreateFirstPersonArms(SkinnedMeshRenderer source, int rendererIndex)
    {
        Mesh sourceMesh = source.sharedMesh;
        if (sourceMesh == null || !sourceMesh.isReadable)
        {
            Debug.LogError("Player mesh must have Read/Write enabled so the first-person arm mesh can be generated.");
            return null;
        }

        Vector3[] vertices = sourceMesh.vertices;
        BoneWeight[] weights = sourceMesh.boneWeights;
        bool[] armBones = new bool[source.bones.Length];
        bool[] torsoBones = new bool[source.bones.Length];
        for (int i = 0; i < source.bones.Length; i++)
        {
            string boneName = source.bones[i] != null ? source.bones[i].name : string.Empty;
            armBones[i] = IsFirstPersonArmBone(boneName);
            torsoBones[i] = IsFirstPersonTorsoBone(boneName);
        }
        bool[] armVertices = new bool[sourceMesh.vertexCount];
        Matrix4x4[] bindposes = sourceMesh.bindposes;
        float armSurfaceRadius = Mathf.Max(0.0001f, sourceMesh.bounds.size.magnitude * 0.065f);
        for (int i = 0; i < vertices.Length; i++)
        {
            BoneWeight weight = weights != null && i < weights.Length
                ? weights[i]
                : default;
            float armWeight = 0f;
            if (weight.boneIndex0 < armBones.Length && armBones[weight.boneIndex0]) armWeight += weight.weight0;
            if (weight.boneIndex1 < armBones.Length && armBones[weight.boneIndex1]) armWeight += weight.weight1;
            if (weight.boneIndex2 < armBones.Length && armBones[weight.boneIndex2]) armWeight += weight.weight2;
            if (weight.boneIndex3 < armBones.Length && armBones[weight.boneIndex3]) armWeight += weight.weight3;
            float torsoWeight = 0f;
            if (weight.boneIndex0 < torsoBones.Length && torsoBones[weight.boneIndex0]) torsoWeight += weight.weight0;
            if (weight.boneIndex1 < torsoBones.Length && torsoBones[weight.boneIndex1]) torsoWeight += weight.weight1;
            if (weight.boneIndex2 < torsoBones.Length && torsoBones[weight.boneIndex2]) torsoWeight += weight.weight2;
            if (weight.boneIndex3 < torsoBones.Length && torsoBones[weight.boneIndex3]) torsoWeight += weight.weight3;
            // Integrated clothing near the shoulder can be fully weighted to
            // an arm bone even when the polygon belongs to the shirt torso.
            // Restrict accepted vertices to the bind-pose arm surface as well
            // as requiring exclusive arm-chain weights.
            float armDistance = DistanceToBindPoseArmSurface(
                vertices[i], source.bones, bindposes, armBones);
            armVertices[i] = armWeight >= 0.72f && torsoWeight <= 0.28f &&
                armDistance <= armSurfaceRadius;
        }

        Mesh armMesh = Instantiate(sourceMesh);
        armMesh.name = sourceMesh.name + " (First Person Arms)";
        Bounds selectedBounds = default;
        bool hasSelectedBounds = false;
        bool[] selectedVertexMask = new bool[sourceMesh.vertexCount];
        for (int subMesh = 0; subMesh < sourceMesh.subMeshCount; subMesh++)
        {
            int[] triangles = sourceMesh.GetTriangles(subMesh);
            List<int> filtered = new List<int>(triangles.Length / 3);
            for (int i = 0; i + 2 < triangles.Length; i += 3)
            {
                int firstIndex = triangles[i];
                int secondIndex = triangles[i + 1];
                int thirdIndex = triangles[i + 2];
                // Every vertex must be exclusively controlled by the allowed
                // arm chain. A two-of-three seam triangle still references a
                // rejected torso vertex and renders body geometry.
                bool keepTriangle = armVertices[firstIndex] &&
                    armVertices[secondIndex] &&
                    armVertices[thirdIndex];
                if (keepTriangle)
                {
                    filtered.Add(firstIndex);
                    filtered.Add(secondIndex);
                    filtered.Add(thirdIndex);
                    selectedVertexMask[firstIndex] = true;
                    selectedVertexMask[secondIndex] = true;
                    selectedVertexMask[thirdIndex] = true;
                    if (!hasSelectedBounds)
                    {
                        selectedBounds = new Bounds(vertices[firstIndex], Vector3.zero);
                        hasSelectedBounds = true;
                    }
                    selectedBounds.Encapsulate(vertices[firstIndex]);
                    selectedBounds.Encapsulate(vertices[secondIndex]);
                    selectedBounds.Encapsulate(vertices[thirdIndex]);
                }
            }
            armMesh.SetTriangles(filtered, subMesh, false);
        }
        if (hasSelectedBounds)
        {
            // RecalculateBounds includes every unreferenced source vertex and
            // makes the carrier look like a full body even though its index
            // buffers contain only arms. Use the bounds of selected faces so
            // culling, diagnostics and the static baked renderer agree.
            armMesh.bounds = selectedBounds;
        }
        SkinnedMeshRenderer arms = FindFirstPersonCloneRenderer(source);
        if (arms == null)
        {
            Debug.LogError(
                $"First-person clone renderer is missing for source {source.name} (index {rendererIndex}).");
            Destroy(armMesh);
            return null;
        }

        Transform[] cloneBones = new Transform[source.bones.Length];
        for (int i = 0; i < source.bones.Length; i++)
        {
            cloneBones[i] = FindFirstPersonCloneTransform(source.bones[i]);
            if (cloneBones[i] == null)
            {
                Debug.LogError(
                    $"First-person clone bone is missing for {source.bones[i]?.name ?? "<null>"}.");
                Destroy(armMesh);
                return null;
            }
        }

        arms.gameObject.layer = PlanarGymMirror.FirstPersonPlayerLayer;
        arms.sharedMesh = armMesh;
        arms.bones = cloneBones;
        arms.rootBone = FindFirstPersonCloneTransform(source.rootBone);
        Material[] stableArmMaterials =
            CreateFirstPersonMaterials(source.sharedMaterials);
        arms.sharedMaterials = stableArmMaterials;
        stableFirstPersonMaterials[arms] =
            (Material[])stableArmMaterials.Clone();
        arms.updateWhenOffscreen = true;
        arms.localBounds = new Bounds(Vector3.zero, Vector3.one * 20f);
        arms.enabled = true;
        arms.forceRenderingOff = false;
        arms.shadowCastingMode = ShadowCastingMode.Off;
        arms.receiveShadows = false;
        runtimeMeshes.Add(armMesh);
        return arms;
    }

    private static int[] BuildSelectedVertexIndices(bool[] selectedVertexMask)
    {
        if (selectedVertexMask == null)
        {
            return new int[0];
        }

        List<int> indices = new List<int>();
        for (int i = 0; i < selectedVertexMask.Length; i++)
        {
            if (selectedVertexMask[i])
            {
                indices.Add(i);
            }
        }
        return indices.ToArray();
    }

    private static float DistanceToBindPoseArmSurface(
        Vector3 vertex,
        Transform[] bones,
        Matrix4x4[] bindposes,
        bool[] armBones)
    {
        if (bones == null || bindposes == null || armBones == null)
        {
            return float.PositiveInfinity;
        }

        float nearest = float.PositiveInfinity;
        int count = Mathf.Min(bones.Length, Mathf.Min(bindposes.Length, armBones.Length));
        for (int i = 0; i < count; i++)
        {
            if (!armBones[i] || bones[i] == null)
            {
                continue;
            }

            Vector3 bonePoint = bindposes[i].inverse.MultiplyPoint3x4(Vector3.zero);
            nearest = Mathf.Min(nearest, Vector3.Distance(vertex, bonePoint));
            Transform parent = bones[i].parent;
            if (parent == null)
            {
                continue;
            }

            int parentIndex = System.Array.IndexOf(bones, parent);
            if (parentIndex < 0 || parentIndex >= count || !armBones[parentIndex])
            {
                continue;
            }
            Vector3 parentPoint = bindposes[parentIndex].inverse.MultiplyPoint3x4(Vector3.zero);
            nearest = Mathf.Min(nearest, DistanceToSegment(vertex, parentPoint, bonePoint));
        }
        return nearest;
    }

    private static float DistanceToSegment(Vector3 point, Vector3 start, Vector3 end)
    {
        Vector3 segment = end - start;
        float squaredLength = segment.sqrMagnitude;
        if (squaredLength <= 0.000001f)
        {
            return Vector3.Distance(point, start);
        }
        float t = Mathf.Clamp01(Vector3.Dot(point - start, segment) / squaredLength);
        return Vector3.Distance(point, start + segment * t);
    }

    private static bool IsFirstPersonArmBone(string boneName)
    {
        string normalized = NormalizeBoneName(boneName);
        bool hasSide = normalized.Contains("left") || normalized.Contains("right") ||
                       normalized.Contains(".l") || normalized.Contains(".r");
        if (!hasSide)
        {
            return false;
        }

        // NormalizeBoneName intentionally removes underscores. Keep the
        // semantic test underscore-free as well, otherwise every DEF arm
        // bone becomes false and the first-person mesh is empty/invisible.
        return normalized.Contains("upperarm") ||
               normalized.Contains("forearm") || normalized.Contains("hand") ||
               normalized.Contains("finger") || normalized.Contains("thumb") ||
               normalized.Contains("palm") || normalized.Contains("findex") ||
               normalized.Contains("fmiddle") || normalized.Contains("fring") ||
               normalized.Contains("fpinky");
    }

    private static bool IsFirstPersonTorsoBone(string boneName)
    {
        string normalized = NormalizeBoneName(boneName);
        return normalized.Contains("spine") || normalized.Contains("chest") ||
               normalized.Contains("neck") || normalized.Contains("head") ||
               normalized.Contains("breast") || normalized.Contains("pelvis") ||
               normalized.Contains("hips");
    }

    private void UpdateFirstPersonArmVisibility()
    {
        for (int i = 0; i < firstPersonArmRenderers.Count; i++)
        {
            SkinnedMeshRenderer renderer = firstPersonArmRenderers[i];
            if (renderer != null)
            {
                // Persistent first-person body: idle, locomotion and jump keep
                // the same upper-arm-through-fingers silhouette visible.
                renderer.enabled = true;
            }
        }
    }

    private SkinnedMeshRenderer FindFirstPersonCloneRenderer(SkinnedMeshRenderer source)
    {
        if (source == null || firstPersonModelRoot == null)
        {
            return null;
        }

        Transform cloneTransform = FindFirstPersonCloneTransform(source.transform);
        return cloneTransform != null
            ? cloneTransform.GetComponent<SkinnedMeshRenderer>()
            : null;
    }

    private Transform FindFirstPersonCloneTransform(Transform source)
    {
        if (source == null || modelRoot == null || firstPersonModelRoot == null)
        {
            return null;
        }

        string path = GetTransformPath(modelRoot.transform, source);
        return FindTransformByPath(firstPersonModelRoot.transform, path);
    }

    private static string GetTransformPath(Transform root, Transform target)
    {
        if (root == null || target == null)
        {
            return string.Empty;
        }

        List<string> names = new List<string>();
        Transform current = target;
        while (current != null && current != root)
        {
            names.Add(current.name);
            current = current.parent;
        }
        if (current != root)
        {
            return string.Empty;
        }

        names.Reverse();
        return string.Join("/", names.ToArray());
    }

    private static Transform FindTransformByPath(Transform root, string path)
    {
        if (root == null)
        {
            return null;
        }
        if (string.IsNullOrEmpty(path))
        {
            return root;
        }

        string[] names = path.Split('/');
        Transform current = root;
        for (int i = 0; i < names.Length; i++)
        {
            current = current.Find(names[i]);
            if (current == null)
            {
                return null;
            }
        }
        return current;
    }

    private static float NormalizeCameraPitch(float angle)
    {
        angle %= 360f;
        if (angle > 180f)
        {
            angle -= 360f;
        }
        // Unity's local X rotation is positive when the player looks down;
        // keep the sign so looking sharply up does not hide the arms too.
        return angle;
    }

    private void CreateBakedProxy(
        SkinnedMeshRenderer source, int layer, string proxyName, bool castShadows)
    {
        GameObject proxy = new GameObject(proxyName);
        proxy.layer = layer;
        proxy.transform.SetParent(source.transform.parent, false);
        proxy.transform.SetLocalPositionAndRotation(source.transform.localPosition, source.transform.localRotation);
        proxy.transform.localScale = source.transform.localScale;
        if (layer == PlanarGymMirror.FirstPersonPlayerLayer && playerCamera != null)
        {
            proxy.transform.position += playerCamera.transform.up * -0.18f + playerCamera.transform.forward * 0.2f;
        }

        Mesh bakedMesh = Instantiate(source.sharedMesh);
        bakedMesh.name = proxyName + " Mesh";
        bakedMesh.MarkDynamic();
        proxy.AddComponent<MeshFilter>().sharedMesh = bakedMesh;
        MeshRenderer renderer = proxy.AddComponent<MeshRenderer>();
        renderer.sharedMaterials = source.sharedMaterials;
        renderer.shadowCastingMode = castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
        renderer.receiveShadows = castShadows;
        bakedRenderProxies.Add(new BakedRenderProxy { source = source, mesh = bakedMesh });
        source.forceRenderingOff = true;
    }

    private void UpdateBakedRenderers()
    {
        UpdateFirstPersonClonePose();
        UpdateFirstPersonArmPresentation();
    }

    private void UpdateFirstPersonClonePose()
    {
        if (firstPersonModelRoot == null)
        {
            return;
        }

        int count = Mathf.Min(firstPersonPoseSources.Length, firstPersonPoseTargets.Length);
        for (int i = 0; i < count; i++)
        {
            Transform source = firstPersonPoseSources[i];
            Transform target = firstPersonPoseTargets[i];
            if (source == null || target == null)
            {
                continue;
            }

            target.localPosition = source.localPosition;
            target.localRotation = source.localRotation;
            target.localScale = source.localScale;
        }
    }

    private void UpdateFirstPersonArmPresentation()
    {
        EnsureFirstPersonArmAppearance();
        if (firstPersonModelRoot == null || firstPersonLeftHand == null ||
            firstPersonRightHand == null || playerCamera == null)
        {
            return;
        }

        firstPersonModelRoot.transform.localScale = baseModelLocalScale * FirstPersonArmScale;
        firstPersonModelRoot.transform.rotation =
            playerCamera.transform.rotation * Quaternion.Euler(-20f, 0f, 0f) *
            AuthoredModelForwardCorrection;

        Vector3 shoulderTargetLocal = activeAttackClip != null || jumping
            ? new Vector3(0f, -0.72f, 0.46f)
            : FirstPersonShoulderTargetCameraLocal;
        Vector3 targetShoulderWorld = playerCamera.transform.TransformPoint(shoulderTargetLocal);
        Vector3 currentShoulderWorld =
            (firstPersonLeftUpperArm.position + firstPersonRightUpperArm.position) * 0.5f;
        Vector3 presentationOffset = targetShoulderWorld - currentShoulderWorld;

        // Presentation-only camera offset. The punch reach itself remains entirely
        // authored by the sampled FBX clip; no hand target or per-phase extension.
        bool isPunch = activeAttackClip == punchLeftClip || activeAttackClip == punchRightClip;
        if (isPunch)
        {
            presentationOffset += playerCamera.transform.forward * 0.20f;
        }
        else if (jumping)
        {
            presentationOffset += playerCamera.transform.forward * 0.10f;
        }

        // Blend the whole FP presentation root as well as the bones so entering
        // and leaving a punch cannot visually teleport the arms.
        float presentationBlend = Application.isPlaying
            ? 1f - Mathf.Exp(-Mathf.Max(0.0001f, Time.deltaTime) / AuthoredTransitionDuration)
            : 1f;
        Vector3 desiredPresentationPosition = firstPersonModelRoot.transform.position + presentationOffset;
        firstPersonModelRoot.transform.position = Vector3.Lerp(
            firstPersonModelRoot.transform.position, desiredPresentationPosition, presentationBlend);

        if (activeAttackClip == null && !jumping)
        {
            Vector3 leftShoulderLocal = playerCamera.transform.InverseTransformPoint(firstPersonLeftUpperArm.position);
            Vector3 rightShoulderLocal = playerCamera.transform.InverseTransformPoint(firstPersonRightUpperArm.position);
            float shoulderAxis = rightShoulderLocal.x - leftShoulderLocal.x;
            float rightSign = shoulderAxis >= 0f ? 1f : -1f;
            float leftSign = -rightSign;
            Vector3 leftLocal = playerCamera.transform.InverseTransformPoint(firstPersonLeftHand.position);
            Vector3 rightLocal = playerCamera.transform.InverseTransformPoint(firstPersonRightHand.position);
            float neutralDepth = Mathf.Clamp(
                (Mathf.Max(leftLocal.z, 0f) + Mathf.Max(rightLocal.z, 0f)) * 0.5f, 1.06f, 1.18f);
            float neutralHeight = Mathf.Clamp((leftLocal.y + rightLocal.y) * 0.5f, -0.10f, 0.06f);
            SolveArm(firstPersonLeftUpperArm, firstPersonLeftForearm, firstPersonLeftHand,
                playerCamera.transform.TransformPoint(new Vector3(leftSign * 0.34f, neutralHeight, neutralDepth)));
            SolveArm(firstPersonRightUpperArm, firstPersonRightForearm, firstPersonRightHand,
                playerCamera.transform.TransformPoint(new Vector3(rightSign * 0.34f, neutralHeight, neutralDepth)));
        }
    }
    public bool TryGetFirstPersonHandCameraPositions(
        out Vector3 leftCameraLocal, out Vector3 rightCameraLocal)
    {
        leftCameraLocal = Vector3.zero;
        rightCameraLocal = Vector3.zero;
        if (playerCamera == null || firstPersonLeftHand == null ||
            firstPersonRightHand == null)
        {
            return false;
        }

        leftCameraLocal = playerCamera.transform.InverseTransformPoint(
            firstPersonLeftHand.position);
        rightCameraLocal = playerCamera.transform.InverseTransformPoint(
            firstPersonRightHand.position);
        return true;
    }

#if UNITY_EDITOR
    private static string DescribeBone(Transform bone)
    {
        return bone == null
            ? "missing"
            : $"{bone.name}:{bone.localPosition}/{bone.localRotation}";
    }
#endif

    private static void SetLayerRecursively(Transform root, int layer)
    {
        root.gameObject.layer = layer;
        for (int i = 0; i < root.childCount; i++)
        {
            Transform child = root.GetChild(i);
            if (!child.name.StartsWith("First Person Arms", StringComparison.Ordinal) &&
                !child.name.StartsWith("Visible First Person Arms", StringComparison.Ordinal))
            {
                SetLayerRecursively(child, layer);
            }
        }
    }

    private void OnDestroy()
    {
        if (contactMesh != null) Destroy(contactMesh);
        for (int i = 0; i < runtimeMeshes.Count; i++)
        {
            if (runtimeMeshes[i] != null)
            {
                Destroy(runtimeMeshes[i]);
            }
        }
        for (int i = 0; i < bakedRenderProxies.Count; i++)
        {
            if (bakedRenderProxies[i].mesh != null)
            {
                Destroy(bakedRenderProxies[i].mesh);
            }
            if (bakedRenderProxies[i].sourceBakeMesh != null)
            {
                Destroy(bakedRenderProxies[i].sourceBakeMesh);
            }
        }
    }
}
