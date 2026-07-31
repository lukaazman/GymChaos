using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public sealed class PlayerHandRig : MonoBehaviour
{
    private sealed class BakedRenderProxy
    {
        public SkinnedMeshRenderer source;
        public Mesh mesh;
    }

    private const string PlayerModelResource = "Player/player_mia_rigged";
    private const float MirrorTargetHeight = 2.3f;

    private Camera playerCamera;
    private CharacterController controller;
    private GameObject modelRoot;
    private Transform leftUpperArm;
    private Transform leftForearm;
    private Transform leftHand;
    private Transform rightUpperArm;
    private Transform rightForearm;
    private Transform rightHand;
    private Transform leftThigh;
    private Transform rightThigh;
    private Transform leftCalf;
    private Transform rightCalf;

    private readonly Dictionary<Transform, Quaternion> restRotations = new Dictionary<Transform, Quaternion>();
    private readonly Dictionary<Transform, Vector3> restPositions = new Dictionary<Transform, Vector3>();
    private readonly List<BakedRenderProxy> bakedRenderProxies = new List<BakedRenderProxy>();
    private readonly List<Mesh> runtimeMeshes = new List<Mesh>();
    private AnimationClip jabClip;
    private AnimationClip pushClip;
    private AnimationClip throwClip;
    private AnimationClip runClip;
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
    private Vector3 baseModelLocalPosition;
    private Vector3 baseModelLocalScale;
    private bool isHolding;
    private bool initialized;
    private bool sampledJabClip;
    private bool sampledPushClip;
    private bool sampledThrowClip;
    private bool sampledHeldBarGrip;
    private bool sampledHeldPlateGrip;
    private bool sampledRunClip;

    private readonly Dictionary<Transform, Quaternion> lowerBodyRotations = new Dictionary<Transform, Quaternion>();
    private readonly Dictionary<Transform, Vector3> lowerBodyPositions = new Dictionary<Transform, Vector3>();

    public bool HasRequiredMixamoAttackClips => jabClip != null && pushClip != null && throwClip != null;
    public bool HasSampledAllMixamoAttackClips => sampledJabClip && sampledPushClip && sampledThrowClip;
    public bool HasSampledHeldEquipmentGrips => sampledHeldBarGrip && sampledHeldPlateGrip;
    public bool HasMixamoRunClip => runClip != null;
    public bool HasSampledMixamoRunClip => sampledRunClip;
    public string MixamoAttackClipSummary =>
        $"run={runClip?.name ?? "missing"},jab={jabClip?.name ?? "missing"},push={pushClip?.name ?? "missing"},throw={throwClip?.name ?? "missing"}";

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
        }
        controller = GetComponentInParent<CharacterController>();

        GameObject modelPrefab = Resources.Load<GameObject>(PlayerModelResource);
        if (modelPrefab == null)
        {
            Debug.LogError("Player model is missing at Resources/Player/player_mia_rigged.fbx.");
            return;
        }

        modelRoot = Instantiate(modelPrefab, transform);
        modelRoot.name = "Player Mesh (Mixamo Rig)";
        modelRoot.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);

        Animator[] animators = modelRoot.GetComponentsInChildren<Animator>(true);
        for (int i = 0; i < animators.Length; i++)
        {
            animators[i].enabled = false;
        }

        FitModelToController();
        FindBones();
        CaptureRestRotations();
        LoadAttackClips();
        ConfigureRenderers();
    }

    public void SetHolding(bool holding)
    {
        isHolding = holding;
    }

    public void TriggerPunch(bool useRightHand)
    {
        if (jabClip != null)
        {
            StartAttack(jabClip, 0.46f, 0f, useRightHand);
            return;
        }
        if (useRightHand)
        {
            rightPunchTimer = 0.24f;
        }
        else
        {
            leftPunchTimer = 0.24f;
        }
    }

    public void TriggerShove(float heldGripReach = 0f, float duration = 0.58f)
    {
        if (heldGripReach > 0f)
        {
            TriggerHeldShove(heldGripReach, duration);
            return;
        }
        if (pushClip != null)
        {
            StartAttack(pushClip, duration, heldGripReach);
            return;
        }
        shoveTimer = 0.3f;
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
        if (throwClip != null)
        {
            StartAttack(throwClip, 0.68f, 0f, useRightHand);
            return;
        }
        if (useRightHand)
        {
            rightThrowTimer = 0.32f;
        }
        else
        {
            leftThrowTimer = 0.32f;
        }
    }

    public void Tick(float normalizedMoveAmount, float normalizedCrouchAmount = 0f)
    {
        moveAmount = normalizedMoveAmount;
        crouchAmount = Mathf.Clamp01(normalizedCrouchAmount);
        if (modelRoot != null)
        {
            modelRoot.transform.localPosition = baseModelLocalPosition + Vector3.down * (0.14f * crouchAmount);
            Vector3 compressedScale = baseModelLocalScale;
            compressedScale.y *= Mathf.Lerp(1f, 0.84f, crouchAmount);
            modelRoot.transform.localScale = compressedScale;
        }
        locomotionElapsed += Time.deltaTime * Mathf.Lerp(0.75f, 1.35f, moveAmount);
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
    }

#if UNITY_EDITOR
    public bool SampleRunForVerification(float normalizedTime)
    {
        if (runClip == null || modelRoot == null)
        {
            return false;
        }

        moveAmount = 1f;
        locomotionElapsed = Mathf.Repeat(normalizedTime, 1f) * Mathf.Max(0.01f, runClip.length - 0.001f);
        RestoreAnimatedBones();
        bool sampled = SampleLocomotion();
        UpdateBakedRenderers();
        return sampled;
    }
#endif

    private void LateUpdate()
    {
        if (modelRoot == null || playerCamera == null)
        {
            return;
        }

        RestoreAnimatedBones();
        bool sampledLocomotion = SampleLocomotion();
        if (activeAttackClip != null)
        {
            CaptureLowerBodyPose();
            SampleActiveAttack();
            RestoreLowerBodyPose();
            KeepMixamoAttackInFirstPersonView();
        }
        else
        {
            if (!sampledLocomotion)
            {
                AnimateLegs();
            }
            AnimateArms();
            if (heldShoveReach > 0f)
            {
                AnimateHeldGripOverAttack(heldShoveElapsed, heldShoveDuration, heldShoveReach);
            }
        }
        UpdateBakedRenderers();
    }

    private void LoadAttackClips()
    {
        runClip = LoadAnimationClip("Player/Animations/Run") ?? LoadAnimationClip("Player/Animations/Walk");
        jabClip = LoadAnimationClip("Player/Animations/Jab");
        pushClip = LoadAnimationClip("Player/Animations/Push");
        throwClip = LoadAnimationClip("Player/Animations/Throw");
    }

    private static AnimationClip LoadAnimationClip(string resourcePath)
    {
        AnimationClip[] clips = Resources.LoadAll<AnimationClip>(resourcePath);
        for (int i = 0; i < clips.Length; i++)
        {
            if (clips[i] != null && !clips[i].name.StartsWith("__preview__", StringComparison.OrdinalIgnoreCase))
            {
                return clips[i];
            }
        }
        return null;
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
        if (activeAttackClip == null || activeAttackDuration <= 0f)
        {
            return false;
        }
        float normalizedTime = Mathf.Clamp01(activeAttackElapsed / activeAttackDuration);
        float sampleTime = normalizedTime * Mathf.Max(0.01f, activeAttackClip.length - 0.001f);
        Vector3 stablePosition = modelRoot.transform.localPosition;
        Quaternion stableRotation = modelRoot.transform.localRotation;
        activeAttackClip.SampleAnimation(modelRoot, sampleTime);
        modelRoot.transform.SetLocalPositionAndRotation(stablePosition, stableRotation);
        sampledJabClip |= activeAttackClip == jabClip;
        sampledPushClip |= activeAttackClip == pushClip;
        sampledThrowClip |= activeAttackClip == throwClip;
        return true;
    }

    private bool SampleLocomotion()
    {
        if (runClip == null || moveAmount <= 0.01f)
        {
            return false;
        }

        Vector3 stablePosition = modelRoot.transform.localPosition;
        Quaternion stableRotation = modelRoot.transform.localRotation;
        float sampleTime = locomotionElapsed % Mathf.Max(0.01f, runClip.length - 0.001f);
        runClip.SampleAnimation(modelRoot, sampleTime);
        modelRoot.transform.SetLocalPositionAndRotation(stablePosition, stableRotation);
        sampledRunClip = true;
        return true;
    }

    private void CaptureLowerBodyPose()
    {
        lowerBodyRotations.Clear();
        lowerBodyPositions.Clear();
        foreach (KeyValuePair<Transform, Quaternion> pair in restRotations)
        {
            Transform bone = pair.Key;
            if (bone != null && !IsUpperBodyBone(bone))
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

    private void KeepMixamoAttackInFirstPersonView()
    {
        float normalizedTime = Mathf.Clamp01(activeAttackElapsed / Mathf.Max(0.01f, activeAttackDuration));
        float reach = Mathf.Sin(normalizedTime * Mathf.PI);
        Vector3 leftTarget = playerCamera.transform.TransformPoint(new Vector3(-0.38f, -0.76f, 0.8f));
        Vector3 rightTarget = playerCamera.transform.TransformPoint(new Vector3(0.42f, -0.78f, 0.8f));

        if (activeAttackClip == pushClip)
        {
            leftTarget += playerCamera.transform.forward * (0.46f * reach);
            rightTarget += playerCamera.transform.forward * (0.46f * reach);
            leftTarget += playerCamera.transform.right * (-0.1f * reach);
            rightTarget += playerCamera.transform.right * (0.1f * reach);
        }
        else
        {
            Vector3 attackOffset = playerCamera.transform.forward *
                ((activeAttackClip == jabClip ? 0.78f : 0.58f) * reach);
            attackOffset += playerCamera.transform.up *
                ((activeAttackClip == throwClip ? 0.05f : activeAttackClip == jabClip ? -0.18f : -0.06f) * reach);
            if (activeAttackUsesRightHand)
            {
                rightTarget += attackOffset;
            }
            else
            {
                leftTarget += attackOffset;
            }
        }

        // Retain the sampled Mixamo torso, wrist, hand and finger motion. Only
        // retarget the upper/forearm chains so the real full-body animation also
        // remains visible from the first-person camera.
        SolveArm(leftUpperArm, leftForearm, leftHand, leftTarget);
        SolveArm(rightUpperArm, rightForearm, rightHand, rightTarget);
    }

    private void AnimateArms()
    {
        float bob = Mathf.Sin(Time.time * (isHolding ? 5f : 8f)) * moveAmount * 0.025f;
        float leftPunch = AttackCurve(leftPunchTimer, 0.24f);
        float rightPunch = AttackCurve(rightPunchTimer, 0.24f);
        float shove = AttackCurve(shoveTimer, 0.3f);
        float leftThrow = AttackCurve(leftThrowTimer, 0.32f);
        float rightThrow = AttackCurve(rightThrowTimer, 0.32f);

        Vector3 leftTarget = playerCamera.transform.TransformPoint(
            isHolding ? new Vector3(-0.27f, -0.52f + bob, 0.88f) : new Vector3(-0.34f, -0.76f + bob, 0.8f));
        Vector3 rightTarget = playerCamera.transform.TransformPoint(
            isHolding ? new Vector3(0.34f, -0.54f - bob, 0.9f) : new Vector3(0.38f, -0.78f - bob, 0.8f));

        leftTarget += playerCamera.transform.forward * (leftPunch * 0.82f + shove * 0.46f + leftThrow * 0.62f);
        rightTarget += playerCamera.transform.forward * (rightPunch * 0.82f + shove * 0.46f + rightThrow * 0.62f);
        leftTarget += playerCamera.transform.right * (-0.12f * shove + 0.08f * leftThrow);
        rightTarget += playerCamera.transform.right * (0.12f * shove - 0.08f * rightThrow);

        SolveArm(leftUpperArm, leftForearm, leftHand, leftTarget);
        SolveArm(rightUpperArm, rightForearm, rightHand, rightTarget);
    }

    private void AnimateLegs()
    {
        if (moveAmount <= 0.01f)
        {
            return;
        }
        float stride = Mathf.Sin(Time.time * Mathf.Lerp(5f, 10f, moveAmount)) * 28f * moveAmount;
        ApplyLocalPitch(leftThigh, stride);
        ApplyLocalPitch(rightThigh, -stride);
        ApplyLocalPitch(leftCalf, Mathf.Max(0f, -stride) * 0.65f);
        ApplyLocalPitch(rightCalf, Mathf.Max(0f, stride) * 0.65f);
    }

    private void ApplyLocalPitch(Transform bone, float degrees)
    {
        if (bone != null && restRotations.TryGetValue(bone, out Quaternion rest))
        {
            bone.localRotation = rest * Quaternion.Euler(degrees, 0f, 0f);
        }
    }

    private static void SolveArm(Transform upperArm, Transform forearm, Transform hand, Vector3 target)
    {
        if (upperArm == null || forearm == null || hand == null)
        {
            return;
        }

        for (int i = 0; i < 5; i++)
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

    private static float AttackCurve(float timer, float duration)
    {
        if (timer <= 0f)
        {
            return 0f;
        }
        float normalized = 1f - timer / duration;
        return Mathf.Sin(normalized * Mathf.PI);
    }

    private void FindBones()
    {
        Transform[] bones = modelRoot.GetComponentsInChildren<Transform>(true);
        leftUpperArm = FindBone(bones, "leftarm", "leftupperarm");
        leftForearm = FindBone(bones, "leftforearm", "leftlowerarm");
        leftHand = FindBone(bones, "lefthand");
        rightUpperArm = FindBone(bones, "rightarm", "rightupperarm");
        rightForearm = FindBone(bones, "rightforearm", "rightlowerarm");
        rightHand = FindBone(bones, "righthand");
        leftThigh = FindBone(bones, "leftupleg", "leftthigh");
        rightThigh = FindBone(bones, "rightupleg", "rightthigh");
        leftCalf = FindBone(bones, "leftleg", "leftcalf", "leftlowerleg");
        rightCalf = FindBone(bones, "rightleg", "rightcalf", "rightlowerleg");

        if (leftHand == null || rightHand == null)
        {
            Debug.LogError("The player FBX is not using the expected Mixamo-compatible arm bone names.");
        }
    }

    private static Transform FindBone(Transform[] bones, params string[] candidates)
    {
        for (int i = 0; i < bones.Length; i++)
        {
            string normalized = NormalizeBoneName(bones[i].name);
            for (int j = 0; j < candidates.Length; j++)
            {
                if (normalized == candidates[j] || normalized.EndsWith(candidates[j], StringComparison.Ordinal))
                {
                    return bones[i];
                }
            }
        }
        return null;
    }

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

        const float desiredHeight = 2.75f;
        float uniformScale = desiredHeight / Mathf.Max(0.001f, bounds.size.y);
        float localMinimumY = transform.InverseTransformPoint(bounds.min).y;
        modelRoot.transform.localScale = new Vector3(uniformScale * 1.1f, uniformScale, uniformScale * 1.05f);
        float footY = controller != null ? -controller.height * 0.5f : -1f;
        modelRoot.transform.localPosition = new Vector3(0f, footY - localMinimumY * uniformScale, 0f);
        baseModelLocalPosition = modelRoot.transform.localPosition;
        baseModelLocalScale = modelRoot.transform.localScale;
    }

    private void ConfigureRenderers()
    {
        SkinnedMeshRenderer[] renderers = modelRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            SkinnedMeshRenderer fullBody = renderers[i];
            fullBody.sharedMaterials = CreateOpaqueMaterials(fullBody.sharedMaterials);
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
            }
        }
        ScaleMirrorBodyToEnemyHeight(renderers);
        SetLayerRecursively(modelRoot.transform, PlanarGymMirror.MirrorPlayerLayer);
    }

    private static void ScaleMirrorBodyToEnemyHeight(SkinnedMeshRenderer[] renderers)
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
            renderer.BakeMesh(baked);
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

        float scale = MirrorTargetHeight / visibleBounds.size.y;
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] != null)
            {
                renderers[i].transform.localScale *= scale;
            }
        }
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
        Texture2D fallbackBaseColor = Resources.Load<Texture2D>(
            "Player/player_mia_rigged.fbm/Image_0");
        Material[] materials = new Material[sourceMaterials.Length];
        for (int i = 0; i < sourceMaterials.Length; i++)
        {
            Material material = sourceMaterials[i] != null
                ? new Material(sourceMaterials[i])
                : new Material(Shader.Find("Universal Render Pipeline/Lit"));
            material.name = (sourceMaterials[i] != null ? sourceMaterials[i].name : "Player") + " (Opaque Runtime)";
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
            if (fallbackBaseColor != null && material.HasProperty("_BaseMap") &&
                material.GetTexture("_BaseMap") == null)
            {
                material.SetTexture("_BaseMap", fallbackBaseColor);
            }
            if (fallbackBaseColor != null && material.HasProperty("_MainTex") &&
                material.GetTexture("_MainTex") == null)
            {
                material.SetTexture("_MainTex", fallbackBaseColor);
            }
            if (material.HasProperty("_AlphaClip")) material.SetFloat("_AlphaClip", 0f);
            if (material.HasProperty("_ZWrite")) material.SetFloat("_ZWrite", 1f);
            if (material.HasProperty("_Cull")) material.SetFloat("_Cull", (float)CullMode.Off);
            if (material.HasProperty("_SrcBlend")) material.SetFloat("_SrcBlend", (float)BlendMode.One);
            if (material.HasProperty("_DstBlend")) material.SetFloat("_DstBlend", (float)BlendMode.Zero);
            material.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.DisableKeyword("_ALPHATEST_ON");
            material.renderQueue = -1;
            materials[i] = material;
        }
        return materials;
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
        for (int i = 0; i < source.bones.Length; i++)
        {
            string boneName = source.bones[i] != null ? NormalizeBoneName(source.bones[i].name) : string.Empty;
            armBones[i] = boneName.Contains("leftarm") || boneName.Contains("leftforearm") ||
                          boneName.Contains("lefthand") || boneName.Contains("rightarm") ||
                          boneName.Contains("rightforearm") || boneName.Contains("righthand");
        }
        bool[] armVertices = new bool[sourceMesh.vertexCount];
        for (int i = 0; i < vertices.Length; i++)
        {
            Vector3 worldVertex = source.transform.TransformPoint(vertices[i]);
            float distance = Mathf.Min(
                DistanceToArm(worldVertex, leftUpperArm, leftForearm, leftHand),
                DistanceToArm(worldVertex, rightUpperArm, rightForearm, rightHand));
            BoneWeight weight = weights[i];
            float armWeight = 0f;
            if (weight.boneIndex0 < armBones.Length && armBones[weight.boneIndex0]) armWeight += weight.weight0;
            if (weight.boneIndex1 < armBones.Length && armBones[weight.boneIndex1]) armWeight += weight.weight1;
            if (weight.boneIndex2 < armBones.Length && armBones[weight.boneIndex2]) armWeight += weight.weight2;
            if (weight.boneIndex3 < armBones.Length && armBones[weight.boneIndex3]) armWeight += weight.weight3;
            // Mixamo exports can slightly change skin-weight normalization between
            // FBX versions. Accept a strong arm influence or a vertex that is
            // geometrically on the animated arm chain; requiring both produced an
            // empty first-person mesh on a freshly auto-rigged character.
            armVertices[i] = armWeight >= 0.25f || distance <= 0.12f;
        }

        Mesh armMesh = Instantiate(sourceMesh);
        armMesh.name = sourceMesh.name + " (First Person Arms)";
        for (int subMesh = 0; subMesh < sourceMesh.subMeshCount; subMesh++)
        {
            int[] triangles = sourceMesh.GetTriangles(subMesh);
            List<int> filtered = new List<int>(triangles.Length / 3);
            for (int i = 0; i + 2 < triangles.Length; i += 3)
            {
                int armVertexCount = (armVertices[triangles[i]] ? 1 : 0) +
                                     (armVertices[triangles[i + 1]] ? 1 : 0) +
                                     (armVertices[triangles[i + 2]] ? 1 : 0);
                // Keep seam triangles touching the arm selection. Requiring every vertex
                // to pass the threshold cut triangular holes through wrists and hands.
                if (armVertexCount >= 1)
                {
                    filtered.Add(triangles[i]);
                    filtered.Add(triangles[i + 1]);
                    filtered.Add(triangles[i + 2]);
                }
            }
            armMesh.SetTriangles(filtered, subMesh, false);
        }
        armMesh.RecalculateBounds();

        GameObject armObject = new GameObject("First Person Arms " + rendererIndex);
        armObject.layer = PlanarGymMirror.FirstPersonPlayerLayer;
        armObject.transform.SetParent(source.transform.parent, false);
        armObject.transform.SetLocalPositionAndRotation(
            source.transform.localPosition + Vector3.down * 0.35f, source.transform.localRotation);
        armObject.transform.localScale = source.transform.localScale;
        SkinnedMeshRenderer arms = armObject.AddComponent<SkinnedMeshRenderer>();
        arms.sharedMesh = armMesh;
        arms.bones = source.bones;
        arms.rootBone = source.rootBone;
        arms.sharedMaterials = source.sharedMaterials;
        arms.updateWhenOffscreen = true;
        arms.localBounds = new Bounds(Vector3.zero, Vector3.one * 20f);
        arms.enabled = true;
        arms.forceRenderingOff = false;
        arms.shadowCastingMode = ShadowCastingMode.Off;
        arms.receiveShadows = false;
        return arms;
    }

    private static float DistanceToArm(
        Vector3 point, Transform upperArm, Transform forearm, Transform hand)
    {
        if (upperArm == null || forearm == null || hand == null)
        {
            return float.PositiveInfinity;
        }
        float upperDistance = DistanceToSegment(point, upperArm.position, forearm.position);
        float lowerDistance = DistanceToSegment(point, forearm.position, hand.position);
        float handDistance = Vector3.Distance(point, hand.position) * 0.8f;
        return Mathf.Min(upperDistance, lowerDistance, handDistance);
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
        for (int i = 0; i < bakedRenderProxies.Count; i++)
        {
            BakedRenderProxy proxy = bakedRenderProxies[i];
            if (proxy.source != null && proxy.mesh != null)
            {
                proxy.source.BakeMesh(proxy.mesh);
            }
        }
    }

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
        }
    }
}
