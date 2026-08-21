using UnityEngine;

[RequireComponent(typeof(BoxCollider))]
[RequireComponent(typeof(Rigidbody))]
public sealed class GlassShatterPanel : MonoBehaviour
{
    [SerializeField] private int minimumColumns = 8;
    [SerializeField] private int minimumRows = 8;
    [SerializeField] private float fragmentLifetime = 28f;

    private Renderer panelRenderer;
    private Collider panelCollider;
    private Rigidbody panelBody;
    private bool hasShattered;

    public bool IsShattered => hasShattered;

    private void Awake()
    {
        panelRenderer = GetComponent<Renderer>();
        if (panelRenderer == null)
        {
            panelRenderer = GetComponentInChildren<Renderer>(true);
        }

        panelCollider = GetComponent<Collider>();
        panelBody = GetComponent<Rigidbody>();
        // A kinematic body makes collision delivery reliable for a static
        // runtime panel while keeping the original panel fixed in the wall.
        panelBody.isKinematic = true;
        panelBody.useGravity = false;
        panelBody.interpolation = RigidbodyInterpolation.None;
        panelBody.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (hasShattered || collision == null)
        {
            return;
        }

        PickupItem item = collision.rigidbody != null
            ? collision.rigidbody.GetComponentInParent<PickupItem>()
            : null;
        if (item == null && collision.collider != null)
        {
            item = collision.collider.GetComponentInParent<PickupItem>();
        }

        // A dropped or merely bumped weight must not break the panel. The
        // thrown flag is set only by PlayerMovement.ThrowHeldItem().
        if (item == null || !item.IsThrowableWeapon || !item.WasThrownRecently)
        {
            return;
        }

        float impactSpeed = collision.relativeVelocity.magnitude;
        float minimumImpactSpeed = item.ItemType == WeightType.Barbell ||
            item.ItemType == WeightType.EzBar ? 0.8f : 2.5f;
        if (impactSpeed < minimumImpactSpeed || !item.TryConsumeThrownHit())
        {
            return;
        }

        ContactPoint contact = collision.contactCount > 0 ? collision.GetContact(0) : default;
        Vector3 point = collision.contactCount > 0 ? contact.point : transform.position;
        Vector3 normal = collision.contactCount > 0 ? contact.normal : -collision.relativeVelocity.normalized;
        Shatter(point, normal, collision.relativeVelocity, impactSpeed, item.ImpactMultiplier);
    }

    public bool ShatterFromPlayerImpact(
        PickupItem item, Vector3 impactPoint, Vector3 impactNormal, Vector3 shoveImpulse)
    {
        if (hasShattered || item == null || !item.IsThrowableWeapon)
        {
            return false;
        }

        // A held shove is an explicitly authenticated player action rather
        // than a generic collision. Convert the shove impulse into a bounded
        // impact speed for the same heavy shard burst used by throws.
        float impactSpeed = Mathf.Clamp(
            shoveImpulse.magnitude / Mathf.Max(1f, item.BaseMass) * 2.5f,
            4f,
            22f);
        Shatter(impactPoint, impactNormal, shoveImpulse, impactSpeed, item.ImpactMultiplier);
        return true;
    }

    private void Shatter(
        Vector3 impactPoint,
        Vector3 impactNormal,
        Vector3 impactVelocity,
        float impactSpeed,
        float impactMultiplier)
    {
        if (hasShattered)
        {
            return;
        }

        hasShattered = true;
        Vector3 shatterSoundPosition = panelRenderer != null
            ? panelRenderer.bounds.center
            : transform.position;
        GymAudio.Play(GymSoundEffect.GlassShatter, shatterSoundPosition, 1f);
        Vector3 panelCenter = panelRenderer != null ? panelRenderer.bounds.center : transform.position;
        Vector3 panelRight = transform.right.normalized;
        Vector3 panelUp = transform.up.normalized;
        Vector3 panelNormal = transform.forward.normalized;
        Vector3 worldScale = transform.lossyScale;
        float panelWidth = Mathf.Max(0.1f, Mathf.Abs(worldScale.x));
        float panelHeight = Mathf.Max(0.1f, Mathf.Abs(worldScale.y));
        float panelThickness = Mathf.Max(0.018f, Mathf.Abs(worldScale.z));

        int columns = Mathf.Clamp(
            Mathf.CeilToInt(panelWidth * 2.2f), minimumColumns, 14);
        int rows = Mathf.Clamp(
            Mathf.CeilToInt(panelHeight * 2.2f), minimumRows, 14);
        Material shardMaterial = panelRenderer != null ? panelRenderer.sharedMaterial : null;

        GameObject fragmentRootObject = new GameObject($"{name} - shattered fragments");
        Transform fragmentRoot = fragmentRootObject.transform;
        float totalMass = panelWidth * panelHeight * panelThickness * 22f;
        float panelArea = Mathf.Max(0.01f, panelWidth * panelHeight);
        Vector3 safeImpactNormal = impactNormal.sqrMagnitude > 0.0001f
            ? impactNormal.normalized
            : panelNormal;
        Vector3 safeImpactVelocity = impactVelocity.sqrMagnitude > 0.0001f
            ? impactVelocity.normalized
            : safeImpactNormal;
        float impactStrength = Mathf.Clamp(
            impactSpeed * (0.16f + Mathf.Clamp(impactMultiplier, 1f, 3f) * 0.025f),
            3f,
            15f);

        // Use a local random stream so the irregular break pattern does not
        // disturb gameplay's global Random state. Uneven edge spacing makes
        // shard sizes vary, while corner jitter and triangle splits create
        // visibly different silhouettes instead of a grid of equal cubes.
        System.Random geometryRandom = new System.Random(
            (name.GetHashCode() * 397) ^
            Mathf.RoundToInt(panelWidth * 1000f) ^
            Mathf.RoundToInt(panelHeight * 1000f));
        float[] xEdges = BuildVariableEdges(panelWidth, columns, geometryRandom);
        float[] yEdges = BuildVariableEdges(panelHeight, rows, geometryRandom);
        int shardCount = 0;
        for (int row = 0; row < rows; row++)
        {
            for (int column = 0; column < columns; column++)
            {
                float left = xEdges[column];
                float right = xEdges[column + 1];
                float bottom = yEdges[row];
                float top = yEdges[row + 1];
                float cellWidth = right - left;
                float cellHeight = top - bottom;
                Vector2 bottomLeft = JitterCorner(
                    left, bottom, left, right, bottom, top, cellWidth, cellHeight, geometryRandom);
                Vector2 bottomRight = JitterCorner(
                    right, bottom, left, right, bottom, top, cellWidth, cellHeight, geometryRandom);
                Vector2 topRight = JitterCorner(
                    right, top, left, right, bottom, top, cellWidth, cellHeight, geometryRandom);
                Vector2 topLeft = JitterCorner(
                    left, top, left, right, bottom, top, cellWidth, cellHeight, geometryRandom);

                int cellIndex = row * columns + column;
                bool splitIntoTriangles = cellIndex % 4 == 1;
                if (splitIntoTriangles)
                {
                    bool reverseDiagonal = ((row + column) & 1) == 0;
                    Vector2[] firstTriangle = reverseDiagonal
                        ? new[] { bottomLeft, bottomRight, topRight }
                        : new[] { bottomLeft, bottomRight, topLeft };
                    Vector2[] secondTriangle = reverseDiagonal
                        ? new[] { bottomLeft, topRight, topLeft }
                        : new[] { bottomRight, topRight, topLeft };
                    CreateGlassShard(
                        name, fragmentRoot, panelCenter, transform.rotation,
                        panelNormal, panelRight, panelUp, firstTriangle,
                        panelThickness, panelArea, totalMass, shardMaterial,
                        impactPoint, safeImpactNormal, safeImpactVelocity,
                        impactStrength, fragmentLifetime, geometryRandom);
                    CreateGlassShard(
                        name, fragmentRoot, panelCenter, transform.rotation,
                        panelNormal, panelRight, panelUp, secondTriangle,
                        panelThickness, panelArea, totalMass, shardMaterial,
                        impactPoint, safeImpactNormal, safeImpactVelocity,
                        impactStrength, fragmentLifetime, geometryRandom);
                    shardCount += 2;
                }
                else
                {
                    CreateGlassShard(
                        name, fragmentRoot, panelCenter, transform.rotation,
                        panelNormal, panelRight, panelUp,
                        new[] { bottomLeft, bottomRight, topRight, topLeft },
                        panelThickness, panelArea, totalMass, shardMaterial,
                        impactPoint, safeImpactNormal, safeImpactVelocity,
                        impactStrength, fragmentLifetime, geometryRandom);
                    shardCount++;
                }
            }
        }

        if (panelRenderer != null)
        {
            panelRenderer.enabled = false;
        }
        if (panelCollider != null)
        {
            panelCollider.enabled = false;
        }
        if (panelBody != null)
        {
            panelBody.detectCollisions = false;
        }

        Debug.Log(
            $"GYMCHAOS_GLASS_SHATTER panel={name} shards={shardCount} " +
            $"impactSpeed={impactSpeed:0.0}", this);
        Object.Destroy(gameObject);
    }

    private static float[] BuildVariableEdges(
        float size, int divisions, System.Random random)
    {
        float[] edges = new float[divisions + 1];
        float[] weights = new float[divisions];
        float totalWeight = 0f;
        for (int i = 0; i < divisions; i++)
        {
            weights[i] = 0.68f + (float)random.NextDouble() * 0.64f;
            totalWeight += weights[i];
        }

        edges[0] = -size * 0.5f;
        float accumulated = edges[0];
        for (int i = 0; i < divisions; i++)
        {
            accumulated += size * weights[i] / totalWeight;
            edges[i + 1] = i == divisions - 1 ? size * 0.5f : accumulated;
        }
        return edges;
    }

    private static Vector2 JitterCorner(
        float x,
        float y,
        float left,
        float right,
        float bottom,
        float top,
        float cellWidth,
        float cellHeight,
        System.Random random)
    {
        float xJitter = ((float)random.NextDouble() * 2f - 1f) * cellWidth * 0.16f;
        float yJitter = ((float)random.NextDouble() * 2f - 1f) * cellHeight * 0.16f;
        float xMargin = cellWidth * 0.035f;
        float yMargin = cellHeight * 0.035f;
        return new Vector2(
            Mathf.Clamp(x + xJitter, left + xMargin, right - xMargin),
            Mathf.Clamp(y + yJitter, bottom + yMargin, top - yMargin));
    }

    private static void CreateGlassShard(
        string panelName,
        Transform fragmentRoot,
        Vector3 panelCenter,
        Quaternion panelRotation,
        Vector3 panelNormal,
        Vector3 panelRight,
        Vector3 panelUp,
        Vector2[] polygon,
        float panelThickness,
        float panelArea,
        float totalMass,
        Material shardMaterial,
        Vector3 impactPoint,
        Vector3 impactNormal,
        Vector3 impactVelocity,
        float impactStrength,
        float lifetime,
        System.Random random)
    {
        if (polygon == null || polygon.Length < 3)
        {
            return;
        }

        Vector2 localCenter = Vector2.zero;
        for (int i = 0; i < polygon.Length; i++)
        {
            localCenter += polygon[i];
        }
        localCenter /= polygon.Length;
        float area = Mathf.Max(0.001f, PolygonArea(polygon));
        Vector3 shardCenter = panelCenter +
            panelRight * localCenter.x + panelUp * localCenter.y;

        GameObject shard = new GameObject(
            $"{panelName} shard {fragmentRoot.childCount:000}");
        shard.transform.SetPositionAndRotation(shardCenter, panelRotation);
        shard.transform.SetParent(fragmentRoot, true);

        Mesh mesh = new Mesh
        {
            name = $"{panelName} irregular shard mesh"
        };
        int pointCount = polygon.Length;
        Vector3[] vertices = new Vector3[pointCount * 2];
        float halfThickness = panelThickness * 0.575f;
        for (int i = 0; i < pointCount; i++)
        {
            Vector2 point = polygon[i] - localCenter;
            vertices[i] = new Vector3(point.x, point.y, halfThickness);
            vertices[pointCount + i] = new Vector3(point.x, point.y, -halfThickness);
        }

        int triangleCount = (pointCount - 2) * 2 + pointCount * 2;
        int[] triangles = new int[triangleCount * 3];
        int triangleIndex = 0;
        for (int i = 1; i < pointCount - 1; i++)
        {
            triangles[triangleIndex++] = 0;
            triangles[triangleIndex++] = i;
            triangles[triangleIndex++] = i + 1;
            triangles[triangleIndex++] = pointCount;
            triangles[triangleIndex++] = pointCount + i + 1;
            triangles[triangleIndex++] = pointCount + i;
        }
        for (int i = 0; i < pointCount; i++)
        {
            int next = (i + 1) % pointCount;
            triangles[triangleIndex++] = i;
            triangles[triangleIndex++] = next;
            triangles[triangleIndex++] = pointCount + next;
            triangles[triangleIndex++] = i;
            triangles[triangleIndex++] = pointCount + next;
            triangles[triangleIndex++] = pointCount + i;
        }
        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.RecalculateBounds();
        mesh.RecalculateNormals();

        MeshFilter meshFilter = shard.AddComponent<MeshFilter>();
        meshFilter.sharedMesh = mesh;
        MeshRenderer shardRenderer = shard.AddComponent<MeshRenderer>();
        if (shardMaterial != null)
        {
            shardRenderer.sharedMaterial = shardMaterial;
        }
        MeshCollider shardCollider = shard.AddComponent<MeshCollider>();
        shardCollider.sharedMesh = mesh;
        shardCollider.convex = true;

        Rigidbody shardBody = shard.AddComponent<Rigidbody>();
        float shardMass = Mathf.Clamp(
            totalMass * area / panelArea *
                Mathf.Lerp(0.84f, 1.16f, (float)random.NextDouble()),
            0.045f,
            0.48f);
        shardBody.mass = shardMass;
        shardBody.linearDamping = 0.12f;
        shardBody.angularDamping = 0.08f;
        shardBody.interpolation = RigidbodyInterpolation.Interpolate;
        shardBody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        shardBody.maxAngularVelocity = 28f;

        Vector3 fromImpact = shardCenter - impactPoint;
        Vector3 planeScatter = Vector3.ProjectOnPlane(fromImpact, panelNormal);
        if (planeScatter.sqrMagnitude < 0.0001f)
        {
            planeScatter = panelRight * ((fragmentRoot.childCount & 1) == 0 ? -1f : 1f) +
                panelUp * ((fragmentRoot.childCount & 2) == 0 ? -0.35f : 0.35f);
        }

        Vector3 shardDirection = (
            impactNormal * 0.5f +
            impactVelocity * 0.18f +
            planeScatter.normalized * 0.78f +
            Vector3.up * 0.16f +
            Random.insideUnitSphere * 0.2f).normalized;
        float shardImpulse = impactStrength * Random.Range(0.72f, 1.3f);
        shardBody.AddForce(
            shardDirection * shardImpulse + Vector3.down * (shardMass * 0.35f),
            ForceMode.Impulse);
        shardBody.AddTorque(
            Random.onUnitSphere * impactStrength * Random.Range(0.35f, 0.9f),
            ForceMode.Impulse);
        Object.Destroy(shard, lifetime + Random.Range(-3f, 4f));
        Object.Destroy(mesh, lifetime + 5f);
    }

    private static float PolygonArea(Vector2[] polygon)
    {
        float area = 0f;
        for (int i = 0; i < polygon.Length; i++)
        {
            Vector2 current = polygon[i];
            Vector2 next = polygon[(i + 1) % polygon.Length];
            area += current.x * next.y - next.x * current.y;
        }
        return Mathf.Abs(area) * 0.5f;
    }
}
