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
    // looked oversized against the fist). 1.3x: the scan's fingers are about
    // 4 cm thick and the knuckles 9.7 cm wide, so at real proportions the
    // index cannot sit on the trigger with all three fingers on the handle.
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
        grip?.Update(weaponRoot, IsVisible);
    }

    // Hands, fingers and the gun's place in the fist (see GymPistolGrip).
    private GymPistolGrip grip;

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

    public Vector3 PalmPointForVerification => grip != null ? grip.PalmPoint : Vector3.zero;

#if UNITY_EDITOR
    /// <summary>Verifier: grip measures of the current pose (skin baked now).</summary>
    internal bool TryMeasureGripForVerification(out GymPistolGrip.Measures measures)
    {
        measures = default;
        if (grip == null || weaponRoot == null || !grip.IsSolved) return false;
        measures = grip.Measure(weaponRoot);
        return true;
    }

    internal float SupportWeightForVerification => grip != null ? grip.SupportWeight : 0f;
    internal bool GripUsedBakeForVerification => grip != null && grip.UsedBake;
    internal string ExportGripForVerification() => grip?.ExportBaked();

    internal float BarrelYawFromFacingForVerification =>
        weaponRoot != null ? Vector3.Angle(Vector3.ProjectOnPlane(weaponRoot.right, Vector3.up), Vector3.ProjectOnPlane(transform.forward, Vector3.up)) : -1f;

    internal float SlideTiltFromUpForVerification =>
        weaponRoot != null ? Vector3.Angle(Vector3.ProjectOnPlane(weaponRoot.up, weaponRoot.right), Vector3.ProjectOnPlane(Vector3.up, weaponRoot.right)) : -1f;

    internal float GunWorldLengthForVerification => MeasureWorldLength();
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
        ExternalRiggedCharacterVisual rigged = GetComponent<ExternalRiggedCharacterVisual>();
        if (rigged != null && rigged.RuntimeRig != null)
        {
            SkinnedMeshRenderer body = null;
            foreach (SkinnedMeshRenderer candidate in GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (candidate.sharedMesh != null && (body == null || candidate.sharedMesh.vertexCount > body.sharedMesh.vertexCount))
                    body = candidate;
            }
            grip = new GymPistolGrip(transform, rigged.RuntimeRig, body);
        }

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
