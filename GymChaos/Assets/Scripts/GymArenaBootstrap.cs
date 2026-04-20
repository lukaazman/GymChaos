using System.Collections.Generic;
using UnityEngine;

public class GymArenaBootstrap : MonoBehaviour
{
    private static readonly string[] StaticNameBlocks =
    {
        "player",
        "main camera",
        "carryanchor",
        "playerhandrig",
        "gym fighter"
    };

    private static GymArenaBootstrap instance;

    private readonly Dictionary<Transform, WeightType> weightCandidates = new Dictionary<Transform, WeightType>();
    private readonly HashSet<Transform> configuredPickupRoots = new HashSet<Transform>();

    private PlayerMovement player;

    public static void EnsureExists(PlayerMovement player)
    {
        if (instance != null)
        {
            return;
        }

        GameObject bootstrapObject = new GameObject("GymArenaBootstrap");
        instance = bootstrapObject.AddComponent<GymArenaBootstrap>();
        instance.Initialize(player);
    }

    public void EnsureSceneColliders()
    {
        Renderer[] renderers = FindObjectsByType<Renderer>(FindObjectsSortMode.None);
        HashSet<Transform> processedRenderers = new HashSet<Transform>();

        foreach (Renderer renderer in renderers)
        {
            if (renderer == null || !renderer.enabled)
            {
                continue;
            }

            Transform target = renderer.transform;
            if (processedRenderers.Contains(target) || ShouldIgnoreRenderer(target))
            {
                continue;
            }

            processedRenderers.Add(target);

            Transform pickupRoot = FindPickupRoot(target);
            if (pickupRoot != null)
            {
                if (configuredPickupRoots.Add(pickupRoot))
                {
                    WeightType weightType = TryGetWeightType(pickupRoot.name);
                    weightCandidates[pickupRoot] = weightType;
                    EnsurePickupRootGameplay(pickupRoot.gameObject, weightType);
                }

                continue;
            }

            EnsureStaticCollider(target.gameObject, renderer);
        }
    }

    public void EnsureWeightGameplayObjects()
    {
        foreach (KeyValuePair<Transform, WeightType> entry in weightCandidates)
        {
            if (entry.Key == null)
            {
                continue;
            }

            GameObject item = entry.Key.gameObject;
            Rigidbody body = item.GetComponent<Rigidbody>();
            if (body == null)
            {
                body = item.AddComponent<Rigidbody>();
            }

            Collider[] colliders = item.GetComponentsInChildren<Collider>(true);
            if (colliders == null || colliders.Length == 0)
            {
                continue;
            }

            for (int i = 0; i < colliders.Length; i++)
            {
                MeshCollider meshCollider = colliders[i] as MeshCollider;
                if (meshCollider != null)
                {
                    meshCollider.convex = true;
                }
            }

            PickupItem pickup = item.GetComponent<PickupItem>();
            if (pickup == null)
            {
                pickup = item.AddComponent<PickupItem>();
            }

            pickup.Configure(body, entry.Value, colliders);
        }
    }

    private void Initialize(PlayerMovement targetPlayer)
    {
        player = targetPlayer;
        EnsureSceneColliders();
        EnsureWeightGameplayObjects();
        SpawnEnemies();
    }

    private bool ShouldIgnoreRenderer(Transform target)
    {
        string lowerName = target.name.ToLowerInvariant();
        for (int i = 0; i < StaticNameBlocks.Length; i++)
        {
            if (lowerName.Contains(StaticNameBlocks[i]))
            {
                return true;
            }
        }

        if (target.GetComponentInParent<PlayerMovement>() != null || target.GetComponentInParent<EnemyFighter>() != null)
        {
            return true;
        }

        return target.GetComponent<ParticleSystemRenderer>() != null;
    }

    private Transform FindPickupRoot(Transform target)
    {
        Transform current = target;
        while (current != null)
        {
            if (TryGetWeightType(current.name) != WeightType.None)
            {
                return current;
            }

            if (ShouldIgnoreRenderer(current))
            {
                return null;
            }

            current = current.parent;
        }

        return null;
    }

    private void EnsureStaticCollider(GameObject target, Renderer renderer)
    {
        if (target.GetComponent<Collider>() != null)
        {
            return;
        }

        MeshFilter meshFilter = target.GetComponent<MeshFilter>();
        if (meshFilter != null && meshFilter.sharedMesh != null)
        {
            MeshCollider meshCollider = target.AddComponent<MeshCollider>();
            meshCollider.sharedMesh = meshFilter.sharedMesh;
            meshCollider.convex = false;
            return;
        }

        BoxCollider boxCollider = target.AddComponent<BoxCollider>();
        ApplyRendererBoundsToBox(target.transform, renderer, boxCollider);
    }

    private void EnsurePickupRootGameplay(GameObject rootObject, WeightType type)
    {
        Renderer[] renderers = rootObject.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null || renderer is ParticleSystemRenderer)
            {
                continue;
            }

            GameObject renderObject = renderer.gameObject;
            if (renderObject.GetComponent<Collider>() != null)
            {
                continue;
            }

            MeshFilter meshFilter = renderObject.GetComponent<MeshFilter>();
            if (meshFilter != null && meshFilter.sharedMesh != null)
            {
                MeshCollider meshCollider = renderObject.AddComponent<MeshCollider>();
                meshCollider.sharedMesh = meshFilter.sharedMesh;
                meshCollider.convex = true;
                continue;
            }

            AddFallbackCollider(renderObject.transform, renderer, type);
        }
    }

    private void AddFallbackCollider(Transform target, Renderer renderer, WeightType type)
    {
        switch (type)
        {
            case WeightType.Barbell:
            case WeightType.EzBar:
            {
                CapsuleCollider capsule = target.gameObject.AddComponent<CapsuleCollider>();
                capsule.direction = 2;
                capsule.height = Mathf.Max(renderer.bounds.size.z, 0.8f);
                capsule.radius = Mathf.Max(renderer.bounds.extents.x, 0.06f);
                capsule.center = target.InverseTransformPoint(renderer.bounds.center);
                break;
            }
            default:
            {
                SphereCollider sphere = target.gameObject.AddComponent<SphereCollider>();
                float maxExtent = Mathf.Max(renderer.bounds.extents.x, Mathf.Max(renderer.bounds.extents.y, renderer.bounds.extents.z));
                sphere.radius = maxExtent * 0.9f;
                sphere.center = target.InverseTransformPoint(renderer.bounds.center);
                break;
            }
        }
    }

    private static void ApplyRendererBoundsToBox(Transform target, Renderer renderer, BoxCollider boxCollider)
    {
        Vector3 localCenter = target.InverseTransformPoint(renderer.bounds.center);
        Vector3 lossy = target.lossyScale;
        boxCollider.center = localCenter;
        boxCollider.size = new Vector3(
            renderer.bounds.size.x / Mathf.Max(Mathf.Abs(lossy.x), 0.0001f),
            renderer.bounds.size.y / Mathf.Max(Mathf.Abs(lossy.y), 0.0001f),
            renderer.bounds.size.z / Mathf.Max(Mathf.Abs(lossy.z), 0.0001f));
    }

    private static WeightType TryGetWeightType(string objectName)
    {
        string lower = objectName.ToLowerInvariant();
        if (lower.StartsWith("barbell"))
        {
            return WeightType.Barbell;
        }

        if (lower.StartsWith("ezbar"))
        {
            return WeightType.EzBar;
        }

        if (lower == "plate" || lower.StartsWith("plate ("))
        {
            return WeightType.Plate;
        }

        if (lower.StartsWith("plate20"))
        {
            return WeightType.Plate20;
        }

        if (lower.StartsWith("plate10"))
        {
            return WeightType.Plate10;
        }

        if (lower.StartsWith("plate5"))
        {
            return WeightType.Plate5;
        }

        return WeightType.None;
    }

    private void SpawnEnemies()
    {
        if (FindObjectsByType<EnemyFighter>(FindObjectsSortMode.None).Length > 0 || player == null)
        {
            return;
        }

        Vector3[] offsets =
        {
            new Vector3(7f, 0f, 3f),
            new Vector3(11f, 0f, -4f),
            new Vector3(15f, 0f, 4f)
        };

        for (int i = 0; i < offsets.Length; i++)
        {
            CreateEnemy(i, player.transform.position + player.transform.right * offsets[i].x + player.transform.forward * offsets[i].z);
        }
    }

    private void CreateEnemy(int index, Vector3 position)
    {
        GameObject enemy = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        enemy.name = $"Gym Fighter {index + 1}";
        enemy.transform.position = position + Vector3.up;
        enemy.transform.localScale = new Vector3(1.1f, 1.1f, 1.1f);

        Rigidbody body = enemy.AddComponent<Rigidbody>();
        body.mass = 85f;
        body.linearDamping = 1.5f;
        body.angularDamping = 0.5f;
        body.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
        body.interpolation = RigidbodyInterpolation.Interpolate;

        EnemyFighter fighter = enemy.AddComponent<EnemyFighter>();
        fighter.SetTarget(player);

        Renderer renderer = enemy.GetComponent<Renderer>();
        if (renderer != null)
        {
            renderer.material.color = index % 2 == 0 ? new Color(0.82f, 0.22f, 0.18f) : new Color(0.92f, 0.82f, 0.24f);
        }
    }
}
