using UnityEngine;

/// <summary>
/// Small, fast police projectile. It uses a swept sphere instead of a trigger
/// so a short-lived bullet cannot tunnel through the target or the gym props.
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

    public static GymPoliceProjectile Create(
        EnemyFighter source, Transform projectileTarget, Vector3 origin,
        Vector3 direction, float projectileDamage)
    {
        GameObject bulletObject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        bulletObject.name = "Police Glock17 Bullet";
        bulletObject.transform.position = origin;
        bulletObject.transform.localScale = Vector3.one * (Radius * 2f);
        bulletObject.layer = source != null
            ? source.gameObject.layer
            : 0;

        Collider collider = bulletObject.GetComponent<Collider>();
        if (collider != null)
        {
            Object.Destroy(collider);
        }

        Renderer renderer = bulletObject.GetComponent<Renderer>();
        if (renderer != null)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            Material material = new Material(shader)
            {
                name = "Police Bullet Tracer Material"
            };
            material.color = new Color(1f, 0.84f, 0.24f, 1f);
            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", material.color);
            }
            if (material.HasProperty("_EmissionColor"))
            {
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", new Color(1f, 0.4f, 0.02f, 1f));
            }
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        GymPoliceProjectile projectile = bulletObject.AddComponent<GymPoliceProjectile>();
        projectile.owner = source;
        projectile.target = projectileTarget;
        projectile.velocity = direction.sqrMagnitude > 0.0001f
            ? direction.normalized * Speed
            : Vector3.forward * Speed;
        projectile.damage = Mathf.Max(0.1f, projectileDamage);
        projectile.expiresAt = Time.time + LifeTime;
        CreatedCount++;
        ActiveCount++;
        Debug.Log(
            $"GYMCHAOS_POLICE_PROJECTILE_CREATED count={CreatedCount} " +
            $"speed={Speed:F1} radius={Radius:F3}", projectile);
        return projectile;
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
