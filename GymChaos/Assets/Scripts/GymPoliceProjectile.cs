using UnityEngine;

/// <summary>
/// Fast police projectile drawn with the authored bullet.glb cartridge, tip
/// first along its flight. It uses a swept sphere instead of a trigger so a
/// short-lived bullet cannot tunnel through the target or the gym props.
/// </summary>
public sealed class GymPoliceProjectile : MonoBehaviour
{
    private const float Radius = 0.026f;
    private const float Speed = 34f;
    private const float LifeTime = 2.2f;

    private EnemyFighter owner;
    private Transform target;
    private Vector3 velocity;
    private float damage;
    private float expiresAt;
    private readonly RaycastHit[] hits = new RaycastHit[16];

    public static int CreatedCount { get; private set; }
    public static int ResolvedCollisionCount { get; private set; }
    public static int ActiveCount { get; private set; }
    public static string LastCollisionKind { get; private set; } = "none";

    // Authored 9x19 mm cartridge (decimated runtime copy, tip on local +Y).
    private const string BulletAsset = "BodyBuilders/items/bullet.glb";
    private const float CartridgeLength = 0.0297f;
    private const float RealGlockLength = 0.186f;
    // Same scale-up as the officer's oversized Glock, so the round matches the gun.
    public const float BulletWorldLength =
        CartridgeLength * GymPoliceWeapon.GlockWorldLength / RealGlockLength;

    private static Transform template;
    // Root of the in-flight template request; destroyed with the scene on reload.
    private static GameObject pendingRequest;
    // A missing or broken asset is reported once, not on every shot.
    private static bool templateLoadFailed;

    public static int GlbVisualCount { get; private set; }
    public static float LastVisualWorldLength { get; private set; }
    public static float LastVisualTipAlignment { get; private set; }
    public static float LastSpawnMuzzleDistance { get; private set; } = -1f;
    public static bool IsTemplateReady => template != null;

    /// <summary>Loads the bullet model once; every shot clones the hidden template.</summary>
    public static void Preload()
    {
        if (template != null || pendingRequest != null || templateLoadFailed)
        {
            return;
        }
        pendingRequest = RuntimeGlbSceneLoader.Request(
            BulletAsset, null, new Vector3(0f, -500f, 0f), Quaternion.identity, Vector3.one,
            "Police Bullet Template", 0, onLoaded: BuildTemplate);
    }

    private static void BuildTemplate(GameObject loaded)
    {
        pendingRequest = null;
        if (loaded == null)
        {
            templateLoadFailed = true;
            Debug.LogError($"GYMCHAOS_POLICE_BULLET_LOAD_FAILED path={BulletAsset}");
            return;
        }

        // Pivot: centred on the mesh, tip along +Z so LookRotation(velocity)
        // points the round where it flies.
        GameObject pivot = new GameObject("Police Bullet Template");
        pivot.SetActive(false);
        Transform model = loaded.transform;
        model.SetParent(pivot.transform, false);
        model.localPosition = Vector3.zero;
        model.localRotation = Quaternion.FromToRotation(Vector3.up, Vector3.forward);
        model.localScale = Vector3.one;
        if (!TryGetBounds(pivot.transform, out Bounds bounds) || bounds.size.z <= 0.0001f)
        {
            templateLoadFailed = true;
            Debug.LogError("GYMCHAOS_POLICE_BULLET_LOAD_FAILED reason=empty-bounds");
            Object.Destroy(pivot);
            return;
        }
        float scale = BulletWorldLength / bounds.size.z;
        model.localScale = Vector3.one * scale;
        model.localPosition = -bounds.center * scale;
        foreach (Renderer renderer in pivot.GetComponentsInChildren<Renderer>(true))
        {
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }
        template = pivot.transform;
    }

    // Mesh bounds in the pivot's space; works while the template is inactive.
    private static bool TryGetBounds(Transform root, out Bounds bounds)
    {
        bounds = default;
        bool found = false;
        foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>(true))
        {
            if (filter.sharedMesh == null) continue;
            Matrix4x4 toRoot = root.worldToLocalMatrix * filter.transform.localToWorldMatrix;
            Bounds local = filter.sharedMesh.bounds;
            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 point = toRoot.MultiplyPoint3x4(new Vector3(
                    (corner & 1) == 0 ? local.min.x : local.max.x,
                    (corner & 2) == 0 ? local.min.y : local.max.y,
                    (corner & 4) == 0 ? local.min.z : local.max.z));
                if (!found) { bounds = new Bounds(point, Vector3.zero); found = true; }
                else bounds.Encapsulate(point);
            }
        }
        return found;
    }

    public static GymPoliceProjectile Create(
        EnemyFighter source, Transform projectileTarget, Vector3 origin,
        Vector3 direction, float projectileDamage)
    {
        Preload();
        Vector3 heading = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.forward;
        // The round leaves the muzzle: its base starts at the barrel tip.
        Vector3 start = origin + heading * (BulletWorldLength * 0.5f);
        GameObject bulletObject = new GameObject("Police Glock17 Bullet");
        bulletObject.transform.SetPositionAndRotation(start, Quaternion.LookRotation(heading));
        bulletObject.layer = source != null ? source.gameObject.layer : 0;

        GymPoliceProjectile projectile = bulletObject.AddComponent<GymPoliceProjectile>();
        if (template != null)
        {
            Transform model = Object.Instantiate(template.GetChild(0), bulletObject.transform, false);
            model.name = "Bullet Model";
            GlbVisualCount++;
            LastVisualWorldLength = MeasureWorldLength(bulletObject.transform);
            LastVisualTipAlignment = Vector3.Dot(
                model.TransformDirection(Vector3.up).normalized, heading);
        }
        LastSpawnMuzzleDistance = Vector3.Distance(
            start - heading * (BulletWorldLength * 0.5f), origin);
        projectile.owner = source;
        projectile.target = projectileTarget;
        projectile.velocity = heading * Speed;
        projectile.damage = Mathf.Max(0.1f, projectileDamage);
        projectile.expiresAt = Time.time + LifeTime;
        CreatedCount++;
        ActiveCount++;
        Debug.Log(
            $"GYMCHAOS_POLICE_PROJECTILE_CREATED count={CreatedCount} " +
            $"speed={Speed:F1} radius={Radius:F3} model={(template != null ? "bullet.glb" : "pending")} " +
            $"length={LastVisualWorldLength:F3} tipDot={LastVisualTipAlignment:F3}", projectile);
        return projectile;
    }

    private static float MeasureWorldLength(Transform root)
    {
        if (!TryGetBounds(root, out Bounds bounds)) return 0f;
        return bounds.size.z * root.lossyScale.z;
    }

    private void Update()
    {
        if (Time.time >= expiresAt)
        {
            Destroy(gameObject);
            return;
        }

        Vector3 start = transform.position;
        Vector3 delta = velocity * Time.deltaTime;
        float distance = delta.magnitude;
        if (distance <= 0.0001f)
        {
            return;
        }

        int hitCount = Physics.SphereCastNonAlloc(
            start, Radius, delta.normalized, hits, distance,
            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        RaycastHit? nearest = null;
        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit hit = hits[i];
            if (hit.collider == null || IsOwnedCollider(hit.collider))
            {
                continue;
            }

            if (!nearest.HasValue || hit.distance < nearest.Value.distance)
            {
                nearest = hit;
            }
        }

        if (nearest.HasValue)
        {
            ResolveHit(nearest.Value);
            Destroy(gameObject);
            return;
        }

        transform.position = start + delta;
    }

    private bool IsOwnedCollider(Collider collider)
    {
        return owner != null &&
            (collider.transform == owner.transform ||
             collider.transform.IsChildOf(owner.transform));
    }

    private void ResolveHit(RaycastHit hit)
    {
        ResolvedCollisionCount++;
        EnemyFighter fighter = hit.collider.GetComponentInParent<EnemyFighter>();
        if (fighter != null && fighter != owner && !fighter.IsDead)
        {
            Vector3 impulse = velocity.normalized * 1.5f + Vector3.up * 0.08f;
            LastCollisionKind = "enemy";
            fighter.TakeMeleeHit(impulse, damage, 0.08f);
            Debug.Log(
                $"GYMCHAOS_POLICE_PROJECTILE_HIT kind=enemy target={fighter.Identity} " +
                $"collisions={ResolvedCollisionCount}", this);
            return;
        }

        PlayerMovement player = hit.collider.GetComponentInParent<PlayerMovement>();
        if (player != null && !player.IsDead)
        {
            player.ReceiveEnemyPunch(
                damage,
                velocity.normalized * 0.35f + Vector3.up * 0.04f,
                owner);
            if (player.IsDead)
            {
                owner?.CelebratePlayerKill();
            }
            LastCollisionKind = "player";
            Debug.Log(
                $"GYMCHAOS_POLICE_PROJECTILE_HIT kind=player " +
                $"dead={player.IsDead} collisions={ResolvedCollisionCount}", this);
            return;
        }

        // The target can move between the shot and impact. A miss still stops
        // at the first solid surface, which prevents bullets from traversing
        // the whole building or vehicle lanes.
        LastCollisionKind = "environment";
        Debug.Log(
            $"GYMCHAOS_POLICE_PROJECTILE_HIT kind=environment " +
            $"collider={hit.collider.name} collisions={ResolvedCollisionCount}", this);
        _ = target;
    }

    private void OnDestroy()
    {
        ActiveCount = Mathf.Max(0, ActiveCount - 1);
    }
}
