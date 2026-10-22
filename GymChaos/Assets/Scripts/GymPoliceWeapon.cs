using System.Collections.Generic;
using UnityEngine;

/// <summary>Attaches the authored Glock17 prop to the policeman's right hand.</summary>
// Runs after the rig retarget (900) and visual (1000) so the anchor copies
// the final hand pose of the frame.
[DefaultExecutionOrder(1100)]
public sealed class GymPoliceWeapon : MonoBehaviour
{
    private const string GlockAsset = "BodyBuilders/items/glock17.glb";
    private const float GlockScale = 0.35f;
    // Real Glock 17 length is 0.186 m; the officer is 1.36x a 1.82 m man,
    // so the same gun in his hand is about 0.25 m (the earlier 0.27 m gun
    // looked oversized against the fist).
    public const float GlockBaseWorldLength = 0.235f;
    public const float GlockScaleMultiplier = 1.07f;
    public const float GlockWorldLength = GlockBaseWorldLength * GlockScaleMultiplier;
    public const float BulletDamage = 16f;

    private EnemyFighter owner;
    private MixamoScanRetargetAnimator animator;
    private Transform rightHand;
    private Transform weaponRoot;
    private Transform muzzle;
    private bool attachRequested;
    private bool loadFailed;
    private bool visible;

    public bool IsReady => weaponRoot != null && muzzle != null;
    public bool IsVisible => visible && weaponRoot != null &&
        weaponRoot.gameObject.activeInHierarchy;
    public Transform WeaponRoot => weaponRoot;
    public Transform Muzzle => muzzle;
    public int ShotCount { get; private set; }
    public bool IsAttachedToRightHand =>
        weaponRoot != null && rightHand != null &&
        handAnchor != null && weaponRoot.IsChildOf(handAnchor);
    // Uniformly scaled follower of the right hand bone. Parenting the prop
    // directly under the Rigify hand inherits its non-uniform scale, which
    // skews and splits the prop's child meshes.
    private Transform handAnchor;
    public Vector3 LastShotOrigin { get; private set; }
    public Vector3 LastShotDirection { get; private set; }
    public float BulletDamageForVerification => BulletDamage;
    public Vector3 HeldBarrelDirectionForVerification =>
        weaponRoot != null
            ? weaponRoot.TransformDirection(Vector3.right).normalized
            : Vector3.zero;
    // Hand-to-prop-body direction; the body must sit in front of the grip.
    public Vector3 BodyDirectionForVerification =>
        weaponRoot != null && rightHand != null && TryGetLocalBounds(out Bounds local)
            ? (weaponRoot.TransformPoint(local.center) - rightHand.position).normalized
            : Vector3.zero;

    public void Configure(EnemyFighter fighter)
    {
        owner = fighter != null ? fighter : GetComponent<EnemyFighter>();
        animator = GetComponent<MixamoScanRetargetAnimator>();
        if (animator == null)
        {
            animator = GetComponentInChildren<MixamoScanRetargetAnimator>(true);
        }
    }

    private void Start()
    {
        if (owner == null)
        {
            owner = GetComponent<EnemyFighter>();
        }
        TryAttach();
    }

    private void LateUpdate()
    {
        if (handAnchor != null && rightHand != null)
        {
            handAnchor.SetPositionAndRotation(rightHand.position, rightHand.rotation);
        }
        CurlFingersAroundGrip();
        if (IsVisible)
        {
            HoldInPalm();
        }
        else if (thumbBase != null && Quaternion.Angle(thumbBase.localRotation, thumbWrittenLocal) <= 0.01f)
        {
            // Gun away: give the thumb back to the animation.
            thumbBase.localRotation = thumbBaseLocal;
        }
    }

    // The gun clips leave the right hand open. While the gun is out, curl the
    // four fingers around the grip; the thumb stays as animated.
    private static readonly string[] CurledFingers = { "index", "middle", "ring", "pinky" };
    private static readonly float[] CurlDegrees = { 55f, 70f, 45f };
    private const float CurlSign = 1f;
    private Transform[] curlBones;
    private Quaternion[] curlBaseLocal;
    private Quaternion[] curlWrittenLocal;
    private Transform curlIndexBase;
    private Transform curlPinkyBase;
    private Transform curlMiddleTip;
    private Transform thumbBase;
    private Transform thumbTip;
    private Quaternion thumbBaseLocal;
    private Quaternion thumbWrittenLocal;

    private void CurlFingersAroundGrip()
    {
        if (rightHand == null)
        {
            return;
        }
        if (curlBones == null)
        {
            CacheCurlBones();
        }
        if (curlBones.Length == 0 || curlIndexBase == null || curlPinkyBase == null)
        {
            return;
        }

        bool curl = IsVisible;
        Vector3 axis = (curlPinkyBase.position - curlIndexBase.position).normalized;
        for (int i = 0; i < curlBones.Length; i++)
        {
            Transform bone = curlBones[i];
            // The clips may not key finger bones. Only adopt a new base pose
            // when something other than this curl wrote the bone.
            if (Quaternion.Angle(bone.localRotation, curlWrittenLocal[i]) > 0.01f)
            {
                curlBaseLocal[i] = bone.localRotation;
            }
            bone.localRotation = curlBaseLocal[i];
            if (curl && axis.sqrMagnitude > 0.5f)
            {
                bone.rotation = Quaternion.AngleAxis(
                    CurlSign * CurlDegrees[i % CurlDegrees.Length], axis) * bone.rotation;
            }
            curlWrittenLocal[i] = bone.localRotation;
        }
    }

    // Bones ordered finger by finger, knuckle to tip, so each rotation
    // carries the later segments with it.
    private void CacheCurlBones()
    {
        List<Transform> bones = new List<Transform>();
        Transform[] all = rightHand.GetComponentsInChildren<Transform>(true);
        foreach (string finger in CurledFingers)
        {
            for (int segment = 1; segment <= CurlDegrees.Length; segment++)
            {
                string suffix = $"f_{finger}.0{segment}";
                foreach (Transform child in all)
                {
                    if (child.name.StartsWith("DEF-") && child.name.Contains(suffix))
                    {
                        bones.Add(child);
                        if (segment == 1 && finger == "index") curlIndexBase = child;
                        if (segment == 1 && finger == "pinky") curlPinkyBase = child;
                        if (segment == 3 && finger == "middle") curlMiddleTip = child;
                        break;
                    }
                }
            }
        }
        if (bones.Count != CurledFingers.Length * CurlDegrees.Length)
        {
            bones.Clear();
        }
        foreach (Transform child in all)
        {
            if (!child.name.StartsWith("DEF-")) continue;
            if (child.name.Contains("thumb.01")) thumbBase = child;
            if (child.name.Contains("thumb.03")) thumbTip = child;
        }
        if (thumbBase != null)
        {
            thumbBaseLocal = thumbBase.localRotation;
            thumbWrittenLocal = thumbBase.localRotation;
        }
        curlBones = bones.ToArray();
        curlBaseLocal = new Quaternion[curlBones.Length];
        curlWrittenLocal = new Quaternion[curlBones.Length];
        for (int i = 0; i < curlBones.Length; i++)
        {
            curlBaseLocal[i] = curlBones[i].localRotation;
            curlWrittenLocal[i] = curlBones[i].localRotation;
        }
    }

    // The authored draw/shoot clips do not rotate the hand into a pistol
    // grip, so a fixed bone-relative offset points the barrel down and back.
    // Each frame: barrel follows the forearm and locks onto the target once
    // the arm points at it, the grip sits in the palm and the slide on top.
    private const float AimLockStartDot = 0.35f;
    private const float AimLockFullDot = 0.8f;
    // Grip anchor in prop bounds, measured from the rear/bottom corner.
    private const float GripRearFraction = 0.2f;
    private const float GripHeightFraction = 0.5f;
    // Palm centre between wrist (0) and the finger knuckles (1).
    private const float PalmKnuckleBlend = 0.6f;
    private Transform[] fingerBases;
    private Transform rightForearm;

    private Vector3 PalmPoint
    {
        get
        {
            if (fingerBases == null && rightHand != null)
            {
                List<Transform> bases = new List<Transform>();
                foreach (Transform child in rightHand.GetComponentsInChildren<Transform>(true))
                {
                    string name = child.name.ToLowerInvariant();
                    if ((name.Contains("index") || name.Contains("middle") || name.Contains("ring")) &&
                        (name.Contains("01") || name.Contains(".1") || name.Contains("1.")) &&
                        !name.Contains("02") && !name.Contains("03"))
                    {
                        bases.Add(child);
                    }
                }
                fingerBases = bases.ToArray();
            }
            if (fingerBases == null || fingerBases.Length == 0)
            {
                return rightHand.position + rightHand.up.normalized * PalmOffset;
            }
            Vector3 knuckles = Vector3.zero;
            foreach (Transform finger in fingerBases)
            {
                knuckles += finger.position;
            }
            return Vector3.Lerp(rightHand.position,
                knuckles / fingerBases.Length, PalmKnuckleBlend);
        }
    }

    // Fighter roots stand on the floor; the player root is the CharacterController
    // centre, so a fixed offset above it would shoot over the player's head.
    private static Vector3 ResolveAimPoint(Transform target)
    {
        EnemyFighter targetFighter = target.GetComponentInParent<EnemyFighter>();
        if (targetFighter != null)
        {
            return targetFighter.transform.position + Vector3.up * 1.18f;
        }
        CharacterController controller = target.GetComponentInParent<CharacterController>();
        if (controller != null)
        {
            return controller.transform.TransformPoint(controller.center) +
                Vector3.up * (controller.height * 0.15f);
        }
        return target.position + Vector3.up * 1.15f;
    }

    // Where the officer shoots: the target's chest, else level ahead.
    private Vector3 TargetAimDirection(Vector3 from)
    {
        Transform target = owner != null ? owner.CurrentTarget : null;
        if (target == null)
        {
            return AimDirection;
        }
        Vector3 aimPoint = ResolveAimPoint(target);
        Vector3 direction = aimPoint - from;
        return direction.sqrMagnitude > 0.0001f ? direction.normalized : AimDirection;
    }

    // Barrel along the forearm while the arm travels (draw), blended onto the
    // target once the forearm points roughly at it (aim and shoot).
    private Vector3 HeldBarrelDirection(Vector3 palm)
    {
        Vector3 aim = TargetAimDirection(palm);
        Vector3 forearm = rightForearm != null
            ? rightHand.position - rightForearm.position
            : Vector3.zero;
        if (forearm.sqrMagnitude < 0.000001f)
        {
            return aim;
        }
        forearm.Normalize();
        float lockOn = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(
            AimLockStartDot, AimLockFullDot, Vector3.Dot(forearm, aim)));
        return Vector3.Slerp(forearm, aim, lockOn).normalized;
    }

    public Vector3 AimDirection
    {
        get
        {
            Vector3 aim = owner != null ? owner.transform.forward : transform.forward;
            aim = Vector3.ProjectOnPlane(aim, Vector3.up);
            return aim.sqrMagnitude > 0.0001f ? aim.normalized : Vector3.forward;
        }
    }

    // Pistol grip in the hand's own frame, so the gun follows every twist of
    // the wrist instead of passing through the fingers:
    // - the grip runs from the index knuckle down to the pinky knuckle,
    // - the barrel points from the wrist toward the knuckles,
    // - the grip sits in the palm, a fist's half-thickness to the palm side
    //   (the side the curled fingers close toward).
    private const float GripPalmDepth = 0.035f;
    private const float GripAnchorRear = 0.22f;
    private const float GripAnchorHeight = 0.3f;

    private bool TryGetHandFrame(out Vector3 palm, out Vector3 forward, out Vector3 gripDown, out Vector3 palmSide)
    {
        palm = forward = gripDown = palmSide = Vector3.zero;
        if (rightHand == null || curlIndexBase == null || curlPinkyBase == null)
        {
            return false;
        }
        Vector3 knuckles = (curlIndexBase.position + curlPinkyBase.position) * 0.5f;
        forward = knuckles - rightHand.position;
        if (forward.sqrMagnitude < 1e-6f)
        {
            return false;
        }
        forward.Normalize();
        gripDown = Vector3.ProjectOnPlane(curlPinkyBase.position - curlIndexBase.position, forward);
        if (gripDown.sqrMagnitude < 1e-6f)
        {
            return false;
        }
        gripDown.Normalize();
        palmSide = Vector3.Cross(forward, gripDown);
        // The curled middle finger's tip lies on the palm side.
        if (curlMiddleTip != null &&
            Vector3.Dot(curlMiddleTip.position - knuckles, palmSide) < 0f)
        {
            palmSide = -palmSide;
        }
        float handScale = Vector3.Distance(rightHand.position, knuckles) / 0.1f;
        palm = Vector3.Lerp(rightHand.position, knuckles, PalmKnuckleBlend) +
            palmSide * GripPalmDepth * Mathf.Clamp(handScale, 0.8f, 1.6f);
        return true;
    }

    private void HoldInPalm()
    {
        if (weaponRoot == null || rightHand == null || !weaponRoot.gameObject.activeInHierarchy ||
            !TryGetLocalBounds(out Bounds local))
        {
            return;
        }
        if (!TryGetHandFrame(out Vector3 palm, out Vector3 forward, out Vector3 gripDown, out Vector3 palmSide))
        {
            return;
        }
        // Prop long axis is local +X; LookRotation maps +Z to the barrel, so
        // turn +X onto +Z first. Slide up = away from the pinky.
        weaponRoot.rotation = Quaternion.LookRotation(forward, -gripDown) * Quaternion.Euler(0f, -90f, 0f);
        Vector3 grip = new Vector3(
            local.min.x + local.size.x * GripAnchorRear,
            local.min.y + local.size.y * GripAnchorHeight,
            local.center.z);
        Vector3 fit = gripFitDone ? palmSide * gripFit.x + forward * gripFit.y + gripDown * gripFit.z : Vector3.zero;
        weaponRoot.position += palm + fit - weaponRoot.TransformPoint(grip);
        if (!gripFitDone && curlBones != null && curlBones.Length > 0)
        {
            FitGripToHandSkin(palmSide, forward, gripDown, local);
            fit = palmSide * gripFit.x + forward * gripFit.y + gripDown * gripFit.z;
            weaponRoot.position += fit;
        }
        WrapThumb(forward);
    }

    // Once per officer, with the fingers curled: bake the hand skin and slide
    // the gun (palm side, along the barrel, along the grip) to the smallest
    // offset at which almost no hand skin is inside it. The fist then closes
    // round the grip instead of the gun passing through the palm.
    private bool gripFitDone;
    private Vector3 gripFit;
    public float GripFitInsideShare { get; private set; } = -1f;

    private void FitGripToHandSkin(Vector3 palmSide, Vector3 forward, Vector3 gripDown, Bounds local)
    {
        gripFitDone = true;
        gripFit = Vector3.zero;
        SkinnedMeshRenderer skin = null;
        foreach (SkinnedMeshRenderer candidate in owner.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (candidate.sharedMesh != null && (skin == null || candidate.sharedMesh.vertexCount > skin.sharedMesh.vertexCount))
                skin = candidate;
        }
        if (skin == null || !skin.sharedMesh.isReadable)
        {
            return;
        }
        BoneWeight[] weights = skin.sharedMesh.boneWeights;
        Transform[] bones = skin.bones;
        Mesh scratch = new Mesh();
        Vector3[] world = GymSkinSampler.BakeWorld(skin, scratch);
        Destroy(scratch);
        System.Collections.Generic.List<Vector3> hand = new System.Collections.Generic.List<Vector3>();
        for (int i = 0; i < world.Length && i < weights.Length; i += 2)
        {
            int b = weights[i].boneIndex0;
            if (b >= 0 && b < bones.Length && bones[b] != null && (bones[b] == rightHand || bones[b].IsChildOf(rightHand)))
                hand.Add(weaponRoot.InverseTransformPoint(world[i]));
        }
        if (hand.Count == 0)
        {
            return;
        }
        Bounds inner = new Bounds(local.center, local.size * 0.85f);
        // Skin within ~2 cm of the gun: the fist must still close on it.
        float avgScale = Mathf.Max(1e-4f, (Mathf.Abs(weaponRoot.lossyScale.x) + Mathf.Abs(weaponRoot.lossyScale.y) + Mathf.Abs(weaponRoot.lossyScale.z)) / 3f);
        Bounds shell = new Bounds(local.center, local.size + Vector3.one * (0.04f / avgScale));
        Vector3 localPalm = weaponRoot.InverseTransformDirection(palmSide);
        Vector3 localForward = weaponRoot.InverseTransformDirection(forward);
        Vector3 localDown = weaponRoot.InverseTransformDirection(gripDown);
        Vector3 scale = weaponRoot.lossyScale;
        float best = float.MaxValue;
        int bestInside = hand.Count;
        for (float p = 0f; p <= 0.16f; p += 0.01f)
        {
            for (float f = -0.06f; f <= 0.08f; f += 0.02f)
            {
                for (float d = -0.04f; d <= 0.04f; d += 0.02f)
                {
                    Vector3 shiftWorld = new Vector3(p, f, d);
                    Vector3 shift = localPalm * p + localForward * f + localDown * d;
                    shift = new Vector3(shift.x / Mathf.Max(1e-4f, scale.x), shift.y / Mathf.Max(1e-4f, scale.y), shift.z / Mathf.Max(1e-4f, scale.z));
                    int inside = 0, touching = 0;
                    for (int i = 0; i < hand.Count; i++)
                    {
                        // Moving the gun by +shift moves the skin by -shift in gun space.
                        Vector3 point = hand[i] - shift;
                        if (inner.Contains(point)) inside++;
                        else if (shell.Contains(point)) touching++;
                    }
                    // Few vertices inside, the fist still touching, small move.
                    float touchShare = touching / (float)hand.Count;
                    float score = inside / (float)hand.Count * 10f +
                        Mathf.Max(0f, 0.12f - touchShare) * 4f + shiftWorld.magnitude * 0.5f;
                    if (score < best)
                    {
                        best = score;
                        bestInside = inside;
                        gripFit = shiftWorld;
                    }
                }
            }
        }
        GripFitInsideShare = bestInside / (float)hand.Count;
        Debug.Log($"GYMCHAOS_POLICE_GRIP_FIT palm={gripFit.x:F2} forward={gripFit.y:F2} down={gripFit.z:F2} inside={GripFitInsideShare:P1} handSamples={hand.Count}", this);
    }

    // The clip leaves the thumb sticking up; lay it forward along the side
    // of the slide, as in a thumbs-forward pistol grip.
    private void WrapThumb(Vector3 forward)
    {
        if (thumbBase == null || thumbTip == null)
        {
            return;
        }
        if (Quaternion.Angle(thumbBase.localRotation, thumbWrittenLocal) > 0.01f)
        {
            thumbBaseLocal = thumbBase.localRotation;
        }
        thumbBase.localRotation = thumbBaseLocal;
        Vector3 current = thumbTip.position - thumbBase.position;
        Vector3 goal = forward * current.magnitude;
        if (current.sqrMagnitude > 1e-6f && goal.sqrMagnitude > 1e-6f)
        {
            thumbBase.rotation = Quaternion.FromToRotation(current, goal) * thumbBase.rotation;
        }
        thumbWrittenLocal = thumbBase.localRotation;
    }

    public Vector3 PalmPointForVerification => rightHand != null ? PalmPoint : Vector3.zero;

#if UNITY_EDITOR
    /// <summary>
    /// Verifier: share of the right-hand skin (vertices mainly weighted to the
    /// hand and finger bones) inside the gun's box shrunk by 15 %: the gun
    /// passing through the fist instead of sitting in it.
    /// </summary>
    internal float HandSkinInsideGunForVerification(out int handVertices)
    {
        handVertices = 0;
        if (weaponRoot == null || rightHand == null || !TryGetLocalBounds(out Bounds local))
        {
            return -1f;
        }
        SkinnedMeshRenderer skin = null;
        foreach (SkinnedMeshRenderer candidate in owner.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (candidate.sharedMesh != null && (skin == null || candidate.sharedMesh.vertexCount > skin.sharedMesh.vertexCount))
                skin = candidate;
        }
        if (skin == null || !skin.sharedMesh.isReadable) return -1f;
        BoneWeight[] weights = skin.sharedMesh.boneWeights;
        Transform[] bones = skin.bones;
        Mesh scratch = new Mesh();
        Vector3[] world = GymSkinSampler.BakeWorld(skin, scratch);
        Object.Destroy(scratch);
        Bounds inner = new Bounds(local.center, local.size * 0.85f);
        int inside = 0;
        for (int i = 0; i < world.Length && i < weights.Length; i++)
        {
            int b = weights[i].boneIndex0;
            if (b < 0 || b >= bones.Length || bones[b] == null) continue;
            if (bones[b] != rightHand && !bones[b].IsChildOf(rightHand)) continue;
            handVertices++;
            if (inner.Contains(weaponRoot.InverseTransformPoint(world[i]))) inside++;
        }
        return handVertices > 0 ? inside / (float)handVertices : -1f;
    }

    internal float GunWorldLengthForVerification => MeasureWorldLength();
    internal float ThumbToGripForVerification =>
        thumbTip != null && weaponRoot != null && TryGetLocalBounds(out Bounds b)
            ? Vector3.Distance(thumbTip.position, weaponRoot.TransformPoint(b.center)) : -1f;
#endif
    public Vector3 WeaponCenterForVerification =>
        weaponRoot != null && TryGetLocalBounds(out Bounds local)
            ? weaponRoot.TransformPoint(local.center)
            : Vector3.zero;
    public Vector3 WeaponUpForVerification =>
        weaponRoot != null ? weaponRoot.up : Vector3.zero;

    private void Update()
    {
        if (!IsReady && !loadFailed)
        {
            TryAttach();
        }
    }

    private void TryAttach()
    {
        if (attachRequested || owner == null)
        {
            return;
        }

        ExternalRiggedCharacterVisual visual =
            GetComponent<ExternalRiggedCharacterVisual>();
        rightHand = visual != null && visual.RuntimeRig != null
            ? visual.RuntimeRig.RightHand : null;
        rightForearm = visual != null && visual.RuntimeRig != null
            ? visual.RuntimeRig.RightForearm : null;
        if (rightHand == null)
        {
            return;
        }

        attachRequested = true;
        handAnchor = new GameObject("Policeman Glock Hand Anchor").transform;
        handAnchor.SetParent(transform, false);
        handAnchor.SetPositionAndRotation(rightHand.position, rightHand.rotation);
        handAnchor.localScale = Vector3.one;
        RuntimeGlbSceneLoader.Request(
            GlockAsset,
            handAnchor,
            rightHand.position,
            rightHand.rotation,
            Vector3.one * GlockScale,
            "Policeman Glock17",
            EnemyFighter.EnemyCollisionLayer,
            settleOnSupport: false,
            supportY: 0f,
            onLoaded: HandleLoaded);
    }

    private void HandleLoaded(GameObject root)
    {
        if (root == null)
        {
            loadFailed = true;
            Debug.LogError(
                $"GYMCHAOS_POLICE_WEAPON_LOAD_FAILED path={GlockAsset}", this);
            return;
        }

        weaponRoot = root.transform;
        // Rigify hand parents can carry a large non-uniform scale. A small local
        // offset is therefore magnified into world space; anchor the authored prop
        // at the actual hand and let its local rotation define the grip.
        weaponRoot.localPosition = Vector3.zero;
        // The authored prop's long/muzzle axis is local +X. Rotate only the
        // child into the hand's forward grip axis; never rotate the officer.
        weaponRoot.localRotation = Quaternion.Euler(0f, 90f, 0f);
        weaponRoot.localScale = Vector3.one * GlockScale;
        FitToWorldLength();
        PlaceInPalm();
        visible = false;
        weaponRoot.gameObject.SetActive(false);
        muzzle = CreateMuzzle();
        GymPoliceProjectile.Preload();

        Debug.Log(
            $"GYMCHAOS_POLICE_WEAPON_READY asset={GlockAsset} " +
            $"rightHand={rightHand.name} muzzle={muzzle.name} " +
            $"attached={IsAttachedToRightHand} " +
            $"handPos={rightHand.position} weaponPos={weaponRoot.position} " +
            $"ownerPos={owner.transform.position}", this);
    }

    // The hand bone origin is the wrist, so a prop centered on it pokes back
    // up the forearm. Move the prop body into the palm and slightly ahead of
    // the fist along the barrel.
    private const float PalmOffset = 0.12f;

    private void PlaceInPalm()
    {
        if (rightHand == null || !TryGetLocalBounds(out Bounds local))
        {
            return;
        }
        Vector3 barrel = weaponRoot.TransformDirection(Vector3.right).normalized;
        Vector3 palm = rightHand.position + rightHand.up.normalized * PalmOffset;
        Vector3 target = palm + barrel * (GlockWorldLength * 0.4f);
        weaponRoot.position += target - weaponRoot.TransformPoint(local.center);
    }

    // The hand bone's lossy scale differs per rig export, so a fixed local
    // scale can blow the prop up to scene size. Size it by its world length.
    private void FitToWorldLength()
    {
        float length = MeasureWorldLength();
        if (length <= 0.0001f)
        {
            return;
        }
        weaponRoot.localScale *= GlockWorldLength / length;
    }

    private float MeasureWorldLength()
    {
        if (!TryGetLocalBounds(out Bounds bounds))
        {
            return 0f;
        }
        Vector3 size = Vector3.Scale(bounds.size, weaponRoot.lossyScale);
        return Mathf.Max(Mathf.Abs(size.x), Mathf.Abs(size.y), Mathf.Abs(size.z));
    }

    // Prop mesh bounds in weaponRoot local space.
    private bool TryGetLocalBounds(out Bounds bounds)
    {
        bounds = default;
        if (weaponRoot == null)
        {
            return false;
        }
        Renderer[] renderers = weaponRoot.GetComponentsInChildren<Renderer>(true);
        bool found = false;
        for (int i = 0; i < renderers.Length; i++)
        {
            Mesh mesh = renderers[i] is SkinnedMeshRenderer skinned
                ? skinned.sharedMesh
                : renderers[i].GetComponent<MeshFilter>()?.sharedMesh;
            if (mesh == null)
            {
                continue;
            }
            // Mesh bounds work while the prop is hidden; Renderer.bounds does not.
            Matrix4x4 toRoot = weaponRoot.worldToLocalMatrix *
                renderers[i].transform.localToWorldMatrix;
            Bounds local = mesh.bounds;
            Vector3 min = local.min;
            Vector3 max = local.max;
            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 point = toRoot.MultiplyPoint3x4(new Vector3(
                    (corner & 1) == 0 ? min.x : max.x,
                    (corner & 2) == 0 ? min.y : max.y,
                    (corner & 4) == 0 ? min.z : max.z));
                if (!found)
                {
                    bounds = new Bounds(point, Vector3.zero);
                    found = true;
                }
                else
                {
                    bounds.Encapsulate(point);
                }
            }
        }
        return found;
    }

    public float WorldLengthForVerification => MeasureWorldLength();
    public Vector3 WeaponPositionForVerification =>
        weaponRoot != null ? weaponRoot.position : Vector3.zero;
    public Vector3 HandPositionForVerification =>
        rightHand != null ? rightHand.position : Vector3.zero;

    public void SetVisible(bool shouldBeVisible)
    {
        visible = shouldBeVisible;
        if (weaponRoot != null)
        {
            weaponRoot.gameObject.SetActive(shouldBeVisible);
        }
    }

    public void AimAt(Transform target)
    {
        if (!IsReady || target == null)
        {
            return;
        }

        Vector3 aimPoint = ResolveAimPoint(target);

        Vector3 direction = aimPoint - weaponRoot.position;
        Vector3 barrel = weaponRoot.TransformDirection(Vector3.right);
        if (direction.sqrMagnitude < 0.0001f ||
            barrel.sqrMagnitude < 0.0001f)
        {
            return;
        }

        weaponRoot.rotation = Quaternion.FromToRotation(
            barrel.normalized, direction.normalized) * weaponRoot.rotation;
    }

    public bool FireAt(Transform target)
    {
        if (!IsReady || owner == null || owner.IsDead || target == null)
        {
            return false;
        }

        SetVisible(true);
        AimAt(target);
        Vector3 origin = muzzle.position;
        Vector3 aimPoint = ResolveAimPoint(target);

        Vector3 direction = Vector3.ProjectOnPlane(aimPoint - origin, Vector3.up);
        direction.y = aimPoint.y - origin.y;
        if (direction.sqrMagnitude < 0.0001f)
        {
            direction = transform.forward;
        }

        GymPoliceProjectile.Create(owner, target, origin, direction, BulletDamage);
        ShotCount++;
        LastShotOrigin = origin;
        LastShotDirection = direction.normalized;
        Debug.Log(
            $"GYMCHAOS_POLICE_SHOT target={target.name} origin={origin} " +
            $"damage={BulletDamage:F1} shot={ShotCount} " +
            $"attached={IsAttachedToRightHand}", this);
        return true;
    }

    // The scanned prop has no muzzle node, so the muzzle is measured from the
    // mesh: the centre of the frontmost slice of vertices along the barrel
    // (+X), i.e. the slide face around the bore.
    private Transform CreateMuzzle()
    {
        Vector3 sum = Vector3.zero;
        int count = 0;
        if (TryGetLocalBounds(out Bounds local))
        {
            float threshold = local.max.x - local.size.x * 0.02f;
            foreach (MeshFilter filter in weaponRoot.GetComponentsInChildren<MeshFilter>(true))
            {
                Mesh mesh = filter.sharedMesh;
                if (mesh == null || !mesh.isReadable) continue;
                Matrix4x4 toRoot = weaponRoot.worldToLocalMatrix * filter.transform.localToWorldMatrix;
                Vector3[] vertices = mesh.vertices;
                for (int i = 0; i < vertices.Length; i++)
                {
                    Vector3 point = toRoot.MultiplyPoint3x4(vertices[i]);
                    if (point.x >= threshold)
                    {
                        sum += point;
                        count++;
                    }
                }
            }
        }
        if (count == 0)
        {
            Debug.LogWarning("GYMCHAOS_POLICE_MUZZLE_FALLBACK reason=no-readable-vertices", this);
        }
        Transform muzzlePoint = new GameObject("Glock Muzzle").transform;
        muzzlePoint.SetParent(weaponRoot, false);
        muzzlePoint.localPosition = count > 0
            ? new Vector3(local.max.x, sum.y / count, sum.z / count)
            : Vector3.zero;
        muzzlePoint.localRotation = Quaternion.identity;
        return muzzlePoint;
    }

    public Vector3 MuzzleLocalForVerification => muzzle != null ? muzzle.localPosition : Vector3.zero;
}
