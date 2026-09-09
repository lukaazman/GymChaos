using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.Rendering;

public sealed class GymMountedWeightMarker : MonoBehaviour
{
    public float MassOverride { get; private set; }

    public void SetMassOverride(float mass)
    {
        MassOverride = Mathf.Max(0f, mass);
    }
}

public sealed class GymDeadliftStationMarker : MonoBehaviour
{
    private readonly List<Collider> stationColliders = new List<Collider>();

    public void RegisterCollider(Collider collider)
    {
        if (collider != null && !stationColliders.Contains(collider))
        {
            stationColliders.Add(collider);
        }
    }

    public void RegisterHierarchyColliders(Transform root)
    {
        if (root == null)
        {
            return;
        }

        Collider[] colliders = root.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < colliders.Length; i++)
        {
            RegisterCollider(colliders[i]);
        }
    }

    public bool ContainsCollider(Collider collider)
    {
        return collider != null && stationColliders.Contains(collider);
    }

    public bool ContainsObject(Transform target)
    {
        if (target == null)
        {
            return false;
        }

        if (target == transform || target.IsChildOf(transform))
        {
            return true;
        }

        for (int i = 0; i < stationColliders.Count; i++)
        {
            Collider stationCollider = stationColliders[i];
            if (stationCollider == null)
            {
                continue;
            }

            Transform colliderRoot = stationCollider.transform;
            if (target == colliderRoot || target.IsChildOf(colliderRoot) ||
                colliderRoot.IsChildOf(target))
            {
                return true;
            }
        }

        return false;
    }

    public bool AreEnemyCollisionsIgnored(EnemyFighter enemy)
    {
        if (enemy == null)
        {
            return false;
        }

        Collider[] enemyColliders = enemy.GetComponentsInChildren<Collider>(true);
        bool comparedAny = false;
        for (int enemyIndex = 0; enemyIndex < enemyColliders.Length; enemyIndex++)
        {
            Collider enemyCollider = enemyColliders[enemyIndex];
            if (enemyCollider == null || !enemyCollider.enabled ||
                !enemyCollider.gameObject.activeInHierarchy)
            {
                continue;
            }

            for (int stationIndex = 0; stationIndex < stationColliders.Count; stationIndex++)
            {
                Collider stationCollider = stationColliders[stationIndex];
                if (stationCollider == null || !stationCollider.enabled ||
                    !stationCollider.gameObject.activeInHierarchy)
                {
                    continue;
                }

                comparedAny = true;
                if (!Physics.GetIgnoreCollision(enemyCollider, stationCollider))
                {
                    return false;
                }
            }
        }

        return comparedAny;
    }

    public void IgnoreEnemy(EnemyFighter enemy, bool ignore)
    {
        if (enemy == null)
        {
            return;
        }

        Collider[] enemyColliders = enemy.GetComponentsInChildren<Collider>(true);
        for (int enemyIndex = 0; enemyIndex < enemyColliders.Length; enemyIndex++)
        {
            Collider enemyCollider = enemyColliders[enemyIndex];
            if (enemyCollider == null || !enemyCollider.enabled ||
                !enemyCollider.gameObject.activeInHierarchy)
            {
                continue;
            }

            for (int stationIndex = 0; stationIndex < stationColliders.Count; stationIndex++)
            {
                Collider stationCollider = stationColliders[stationIndex];
                if (stationCollider != null && stationCollider.enabled &&
                    stationCollider.gameObject.activeInHierarchy)
                {
                    Physics.IgnoreCollision(enemyCollider, stationCollider, ignore);
                }
            }
        }
    }

    public bool TryGetEscapePoint(
        Vector3 worldPosition, float margin, out Vector3 escapePoint)
    {
        escapePoint = worldPosition;
        if (!TryGetLocalFootprint(out Bounds localFootprint))
        {
            return false;
        }

        Vector3 localPosition = transform.InverseTransformPoint(worldPosition);
        bool inside = localPosition.x >= localFootprint.min.x &&
            localPosition.x <= localFootprint.max.x &&
            localPosition.z >= localFootprint.min.z &&
            localPosition.z <= localFootprint.max.z;
        if (!inside)
        {
            return false;
        }

        float left = localPosition.x - localFootprint.min.x;
        float right = localFootprint.max.x - localPosition.x;
        float front = localPosition.z - localFootprint.min.z;
        float back = localFootprint.max.z - localPosition.z;
        float nearest = Mathf.Min(Mathf.Min(left, right), Mathf.Min(front, back));
        float safeMargin = Mathf.Max(0.75f, margin);
        if (nearest == left)
        {
            localPosition.x = localFootprint.min.x - safeMargin;
        }
        else if (nearest == right)
        {
            localPosition.x = localFootprint.max.x + safeMargin;
        }
        else if (nearest == front)
        {
            localPosition.z = localFootprint.min.z - safeMargin;
        }
        else
        {
            localPosition.z = localFootprint.max.z + safeMargin;
        }

        localPosition.y = transform.InverseTransformPoint(worldPosition).y;
        escapePoint = transform.TransformPoint(localPosition);
        escapePoint.y = worldPosition.y;
        return true;
    }

    public bool TryGetEscapePointForEnemy(
        EnemyFighter enemy, float margin, out Vector3 escapePoint)
    {
        escapePoint = enemy != null ? enemy.transform.position : Vector3.zero;
        if (enemy == null || !TryGetLocalFootprint(out Bounds localFootprint))
        {
            return false;
        }

        Collider[] enemyColliders = enemy.GetComponentsInChildren<Collider>(true);
        Bounds enemyFootprint = default;
        bool hasEnemyFootprint = false;
        for (int i = 0; i < enemyColliders.Length; i++)
        {
            Collider collider = enemyColliders[i];
            if (collider == null || !collider.enabled ||
                !collider.gameObject.activeInHierarchy)
            {
                continue;
            }

            Bounds worldBounds = collider.bounds;
            Vector3 min = worldBounds.min;
            Vector3 max = worldBounds.max;
            for (int x = 0; x <= 1; x++)
            for (int y = 0; y <= 1; y++)
            for (int z = 0; z <= 1; z++)
            {
                Vector3 local = transform.InverseTransformPoint(new Vector3(
                    x == 0 ? min.x : max.x,
                    y == 0 ? min.y : max.y,
                    z == 0 ? min.z : max.z));
                if (!hasEnemyFootprint)
                {
                    enemyFootprint = new Bounds(local, Vector3.zero);
                    hasEnemyFootprint = true;
                }
                else
                {
                    enemyFootprint.Encapsulate(local);
                }
            }
        }

        if (!hasEnemyFootprint ||
            enemyFootprint.max.x < localFootprint.min.x ||
            enemyFootprint.min.x > localFootprint.max.x ||
            enemyFootprint.max.z < localFootprint.min.z ||
            enemyFootprint.min.z > localFootprint.max.z)
        {
            return false;
        }

        float safeMargin = Mathf.Max(0.45f, margin);
        Vector3 currentCenter = enemyFootprint.center;
        Vector3 leftTarget = currentCenter;
        leftTarget.x = localFootprint.min.x - safeMargin - enemyFootprint.extents.x;
        Vector3 rightTarget = currentCenter;
        rightTarget.x = localFootprint.max.x + safeMargin + enemyFootprint.extents.x;
        Vector3 frontTarget = currentCenter;
        frontTarget.z = localFootprint.min.z - safeMargin - enemyFootprint.extents.z;
        Vector3 backTarget = currentCenter;
        backTarget.z = localFootprint.max.z + safeMargin + enemyFootprint.extents.z;

        Vector3 target = leftTarget;
        float bestDistance = (leftTarget - currentCenter).sqrMagnitude;
        Vector3[] candidates = { rightTarget, frontTarget, backTarget };
        for (int i = 0; i < candidates.Length; i++)
        {
            float distance = (candidates[i] - currentCenter).sqrMagnitude;
            if (distance < bestDistance)
            {
                target = candidates[i];
                bestDistance = distance;
            }
        }

        Vector3 currentWorldCenter = transform.TransformPoint(currentCenter);
        Vector3 targetWorldCenter = transform.TransformPoint(target);
        Vector3 delta = targetWorldCenter - currentWorldCenter;
        escapePoint = enemy.transform.position + delta;
        escapePoint.y = enemy.transform.position.y;
        return true;
    }

    private bool TryGetLocalFootprint(out Bounds localFootprint)
    {
        localFootprint = default;
        bool hasFootprint = false;
        for (int i = 0; i < stationColliders.Count; i++)
        {
            Collider collider = stationColliders[i];
            if (collider == null || !collider.enabled ||
                !collider.gameObject.activeInHierarchy ||
                (collider.transform != transform &&
                 !collider.transform.IsChildOf(transform)))
            {
                continue;
            }

            Bounds worldBounds = collider.bounds;
            Vector3 min = worldBounds.min;
            Vector3 max = worldBounds.max;
            for (int x = 0; x <= 1; x++)
            for (int y = 0; y <= 1; y++)
            for (int z = 0; z <= 1; z++)
            {
                Vector3 local = transform.InverseTransformPoint(new Vector3(
                    x == 0 ? min.x : max.x,
                    y == 0 ? min.y : max.y,
                    z == 0 ? min.z : max.z));
                if (!hasFootprint)
                {
                    localFootprint = new Bounds(local, Vector3.zero);
                    hasFootprint = true;
                }
                else
                {
                    localFootprint.Encapsulate(local);
                }
            }
        }

        return hasFootprint;
    }
}

/// <summary>
/// Loads the small GLB props from StreamingAssets and lays them out against
/// the runtime-built gym. The source files live in Assets/BodyBuilders/items;
/// StreamingAssets is the runtime copy used by the game's existing GLB path.
/// </summary>
public sealed class GymLooseItemSpawner : MonoBehaviour
{
    private const string RootName = "Gym Loose Items (Runtime)";
    private const string InteriorRootName = "Gym Interior (Runtime)";
    private const uint GlbMagic = 0x46546C67;
    private const uint JsonChunk = 0x4E4F534A;
    private const uint BinaryChunk = 0x004E4942;
    private const float CurrentFoamRollerScale = 0.81f;
    private const float CurrentMedicineBallScale = 0.95f;
    private const float FoamRollerScale = CurrentFoamRollerScale * 0.75f;
    private const float MedicineBallScale = CurrentMedicineBallScale * 1.25f;
    private const float MedicineBallMass = 0.8f;
    private const float MedicineBallEntryClearance = 1.15f;
    private const float MedicineBallFloorHalfExtent = 0.24f;
    private const int MedicineBallCandidateGridResolution = 7;
    private const float MedicineBallSquatCorridorDepth = 1.75f;
    private const float MedicineBallRearClearance = 0.65f;
    private const float DeadliftPlatformThickness = 0.12f;
    private const float DeadliftLoadedPlateThickness = 0.12f;
    private const float DeadliftLoadedPlateClearance = 0.002f;

    private static GymLooseItemSpawner instance;

    private readonly Dictionary<string, ItemAsset> assetCache =
        new Dictionary<string, ItemAsset>(StringComparer.OrdinalIgnoreCase);

    private Transform itemRoot;
    private Bounds floorBounds;
    private bool hasFloorBounds;
    private Bounds yogaSurfaceBounds;
    private bool hasYogaSurfaceBounds;
    private readonly List<Bounds> yogaSurfaceItemBounds = new List<Bounds>();
    private GymDeadliftStationMarker deadliftStationMarker;

    private enum ColliderKind
    {
        Sphere,
        CapsuleX,
        CapsuleY,
        Box
    }

    private sealed class ItemSpec
    {
        public string FileName;
        public string DisplayName;
        public WeightType ItemType;
        public float Scale;
        public Vector3 Position;
        public Vector3 EulerAngles;
        public float SupportY;
        public bool SettleOnSupport;
        public bool Pickable;
        public ColliderKind Collider;
        public bool PlaceOnYogaSurface;
    }

    private sealed class ItemAsset
    {
        public Mesh Mesh;
        public Material Material;
        public Bounds LocalBounds;
    }

    private sealed class SquatLaneLayout
    {
        public string Name;
        public Bounds Bounds;
        public Transform Anchor;
        public Vector3 EntryDirection;
    }

    private sealed class ShelfLayout
    {
        public Vector3 Center;
        public float TopY;
    }

    private sealed class DeadliftLayout
    {
        public Vector3 Center;
        public Quaternion Rotation;
        public GymDeadliftStationMarker Marker;
        public bool Created;
    }

    public static void IgnoreDeadliftStationForEnemy(EnemyFighter enemy)
    {
        EnsureDeadliftStationCollisionIgnore(enemy);
    }

    public static bool EnsureDeadliftStationCollisionIgnore(EnemyFighter enemy)
    {
        if (instance == null || enemy == null)
        {
            return false;
        }

        instance.RegisterDeadliftStationCollidersInternal();
        if (instance.deadliftStationMarker == null)
        {
            return false;
        }

        instance.deadliftStationMarker.IgnoreEnemy(enemy, true);
        return instance.deadliftStationMarker.AreEnemyCollisionsIgnored(enemy);
    }

    public static bool TryGetDeadliftEscapePoint(
        Vector3 worldPosition, float margin, out Vector3 escapePoint)
    {
        escapePoint = worldPosition;
        return instance != null && instance.deadliftStationMarker != null &&
            instance.deadliftStationMarker.TryGetEscapePoint(
                worldPosition, margin, out escapePoint);
    }

    public static bool TryGetDeadliftEscapePointForEnemy(
        EnemyFighter enemy, float margin, out Vector3 escapePoint)
    {
        escapePoint = enemy != null ? enemy.transform.position : Vector3.zero;
        return instance != null && instance.deadliftStationMarker != null &&
            instance.deadliftStationMarker.TryGetEscapePointForEnemy(
                enemy, margin, out escapePoint);
    }

    public static bool IsDeadliftStationCollider(Collider collider)
    {
        return instance != null && instance.deadliftStationMarker != null &&
            instance.deadliftStationMarker.ContainsCollider(collider);
    }

    public static bool IsDeadliftStationObject(Transform target)
    {
        return instance != null && instance.deadliftStationMarker != null &&
            instance.deadliftStationMarker.ContainsObject(target);
    }

    public static void RegisterDeadliftStationColliders()
    {
        instance?.RegisterDeadliftStationCollidersInternal();
    }

#pragma warning disable 0649
    [Serializable]
    private sealed class GltfRoot
    {
        public GltfBufferView[] bufferViews;
        public GltfAccessor[] accessors;
        public GltfMesh[] meshes;
        public GltfImage[] images;
    }

    [Serializable]
    private sealed class GltfBufferView
    {
        public int byteOffset;
        public int byteLength;
        public int byteStride;
    }

    [Serializable]
    private sealed class GltfAccessor
    {
        public int bufferView = -1;
        public int byteOffset;
        public int componentType;
        public int count;
    }

    [Serializable]
    private sealed class GltfMesh
    {
        public GltfPrimitive[] primitives;
    }

    [Serializable]
    private sealed class GltfPrimitive
    {
        public GltfAttributes attributes;
        public int indices = -1;
        public int material;
    }

    [Serializable]
    private sealed class GltfAttributes
    {
        public int POSITION = -1;
        public int NORMAL = -1;
        public int TEXCOORD_0 = -1;
    }

    [Serializable]
    private sealed class GltfImage
    {
        public int bufferView = -1;
        public string mimeType;
    }
#pragma warning restore 0649

    public static GymLooseItemSpawner CreateForScene(PlayerMovement player)
    {
        if (instance != null)
        {
            return instance;
        }

        GameObject existing = GameObject.Find(RootName);
        if (existing != null)
        {
            instance = existing.GetComponent<GymLooseItemSpawner>();
            if (instance == null)
            {
                instance = existing.AddComponent<GymLooseItemSpawner>();
            }

            if (instance.itemRoot == null)
            {
                instance.itemRoot = existing.transform;
            }
            instance.deadliftStationMarker = existing.GetComponentInChildren<
                GymDeadliftStationMarker>(true);

            return instance;
        }

        GameObject rootObject = new GameObject(RootName);
        instance = rootObject.AddComponent<GymLooseItemSpawner>();
        instance.itemRoot = rootObject.transform;
        instance.BuildLayout(player);
        return instance;
    }

    private void Awake()
    {
        if (instance == null)
        {
            instance = this;
        }

        if (itemRoot == null)
        {
            itemRoot = transform;
        }
    }

    private void Start()
    {
        if (deadliftStationMarker == null)
        {
            deadliftStationMarker = GetComponentInChildren<GymDeadliftStationMarker>(true);
        }

        // Reapply the exemption after the bootstrap has finished spawning the
        // roster. This covers scene-authored enemies and imported rigs whose
        // child colliders are attached after their EnemyFighter component.
        if (deadliftStationMarker == null)
        {
            return;
        }

        RegisterDeadliftStationCollidersInternal();

        EnemyFighter[] enemies = FindObjectsByType<EnemyFighter>(
            FindObjectsSortMode.None);
        for (int i = 0; i < enemies.Length; i++)
        {
            deadliftStationMarker.IgnoreEnemy(enemies[i], true);
        }
    }

    private void RegisterDeadliftStationCollidersInternal()
    {
        if (deadliftStationMarker == null)
        {
            deadliftStationMarker = GetComponentInChildren<GymDeadliftStationMarker>(true);
        }

        if (deadliftStationMarker == null)
        {
            return;
        }

        // EnsureSceneColliders can add a convex collider to a generated mesh
        // visual after the station's own factory colliders were registered.
        // Register the complete station hierarchy and every generated loose
        // plate so enemy collision exemptions cover those runtime additions.
        deadliftStationMarker.RegisterHierarchyColliders(
            deadliftStationMarker.transform);

        GameObject loadedBar = GameObject.Find("Barbell DeadliftStation Loaded");
        if (loadedBar != null)
        {
            deadliftStationMarker.RegisterHierarchyColliders(loadedBar.transform);
        }

        Transform[] transforms = itemRoot != null
            ? itemRoot.GetComponentsInChildren<Transform>(true)
            : new Transform[0];
        for (int i = 0; i < transforms.Length; i++)
        {
            Transform target = transforms[i];
            if (target == null ||
                target.name.IndexOf("Freeweight Loose", System.StringComparison.OrdinalIgnoreCase) < 0)
            {
                continue;
            }

            deadliftStationMarker.RegisterHierarchyColliders(target);
        }
    }

    private void OnDestroy()
    {
        if (instance == this)
        {
            instance = null;
        }
    }

    private void BuildLayout(PlayerMovement player)
    {
        hasFloorBounds = TryFindFloorBounds(out floorBounds);
        if (!hasFloorBounds)
        {
            Vector3 fallbackCenter = player != null ? player.transform.position : Vector3.zero;
            floorBounds = new Bounds(fallbackCenter, new Vector3(42f, 0.24f, 42f));
            hasFloorBounds = true;
            Debug.LogWarning("GYMCHAOS_LOOSE_ITEMS_FLOOR_FALLBACK using a 42m runtime layout", this);
        }

        ShelfLayout shelf = CreatePaperTowelShelf();
        DeadliftLayout deadlift = CreateDeadliftSection(shelf);
        List<ItemSpec> specs = CreateItemSpecs(shelf, deadlift);
        StartCoroutine(LoadAndSpawnItems(specs));

        Debug.Log(
            $"GYMCHAOS_LOOSE_ITEMS_LAYOUT floorCenter={floorBounds.center} " +
            $"floorSize={floorBounds.size.x:F1}x{floorBounds.size.z:F1} " +
            $"paperShelf={shelf.Center} yogaSurface={yogaSurfaceBounds.center} " +
            $"yogaSize={yogaSurfaceBounds.size.x:F2}x{yogaSurfaceBounds.size.z:F2} " +
            $"yogaSides=rolledOut:{specs[0].Position} halfRolled:{specs[1].Position} " +
            $"itemSpecs={specs.Count}",
            this);
    }

    private ShelfLayout CreatePaperTowelShelf()
    {
        float floorY = floorBounds.max.y;
        float southWallZ = floorBounds.center.z - floorBounds.size.z * 0.5f;
        float shelfX = floorBounds.center.x - Mathf.Min(8.5f, floorBounds.size.x * 0.25f);
        float shelfZ = southWallZ + 0.52f;
        float shelfY = floorY + 2.15f;
        Vector3 shelfCenter = new Vector3(shelfX, shelfY, shelfZ);

        Material shelfMaterial = CreateSolidMaterial(
            "Loose item shelf material", new Color(0.055f, 0.065f, 0.08f), 0.62f, 0.48f);
        CreateShelfBox("Paper towel shelf", shelfCenter, new Vector3(2.35f, 0.14f, 0.68f), shelfMaterial);
        CreateShelfBox(
            "Paper towel shelf back rail",
            new Vector3(shelfX, shelfY + 0.3f, southWallZ + 0.22f),
            new Vector3(2.35f, 0.62f, 0.12f),
            shelfMaterial);

        for (int i = -1; i <= 1; i += 2)
        {
            CreateShelfBox(
                "Paper towel shelf bracket",
                new Vector3(shelfX + i * 0.78f, shelfY - 0.27f, shelfZ - 0.18f),
                new Vector3(0.12f, 0.54f, 0.12f),
                shelfMaterial);
        }

        return new ShelfLayout { Center = shelfCenter, TopY = shelfY + 0.07f };
    }

    private GameObject CreateShelfBox(string name, Vector3 position, Vector3 size, Material material)
    {
        GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
        box.name = name;
        box.transform.SetParent(itemRoot, true);
        box.transform.position = position;
        box.transform.localScale = size;
        Renderer renderer = box.GetComponent<Renderer>();
        if (renderer != null)
        {
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.On;
            renderer.receiveShadows = true;
        }

        return box;
    }

    private DeadliftLayout CreateDeadliftSection(ShelfLayout shelf)
    {
        float floorY = floorBounds.max.y;
        Bounds cableBounds;
        bool hasCable = TryFindNamedBounds(
            new[] { "cablemachinedual", "cablemachine" }, out cableBounds);

        Vector3 shelfFloorCenter = new Vector3(shelf.Center.x, floorY, shelf.Center.z);
        Vector3 stationCenter;
        if (hasCable)
        {
            Vector3 cableFloorCenter = cableBounds.center;
            cableFloorCenter.y = floorY;
            Vector3 between = shelfFloorCenter - cableFloorCenter;
            stationCenter = between.sqrMagnitude > 0.04f
                ? Vector3.Lerp(cableFloorCenter, shelfFloorCenter, 0.56f)
                : shelfFloorCenter + Vector3.forward * 2.2f;
        }
        else
        {
            stationCenter = shelfFloorCenter + Vector3.forward * 2.2f;
        }

        stationCenter = ClampToFloor(stationCenter, new Vector3(2.65f, 0f, 1.85f));
        Vector3 mirrorDirection = GetMirrorDirection(stationCenter);
        Quaternion stationRotation = Quaternion.LookRotation(-mirrorDirection, Vector3.up);

        GameObject stationObject = new GameObject("Freeweights Deadlift Station");
        stationObject.transform.SetParent(itemRoot, true);
        stationObject.transform.SetPositionAndRotation(stationCenter, stationRotation);
        deadliftStationMarker = stationObject.AddComponent<GymDeadliftStationMarker>();

        Material flooringMaterial = FindTemplateMaterial(
            new[] { "flooringmats", "matt" });
        if (flooringMaterial == null)
        {
            flooringMaterial = CreateSolidMaterial(
                "Deadlift Flooring Mats material",
                new Color(0.055f, 0.065f, 0.078f), 0.08f, 0.38f);
        }

        const int columns = 4;
        const int rows = 2;
        const float tileWidth = 1.14f;
        const float tileDepth = 1.28f;
        for (int row = 0; row < rows; row++)
        {
            for (int column = 0; column < columns; column++)
            {
                Vector3 localPosition = new Vector3(
                    (column - (columns - 1) * 0.5f) * tileWidth,
                    0.06f,
                    (row - (rows - 1) * 0.5f) * tileDepth);
                GameObject flooringMat = CreateStaticBox(
                    $"Deadlift Flooring Mat {row * columns + column + 1}",
                    stationObject.transform,
                    localPosition,
                    new Vector3(tileWidth - 0.025f, 0.12f, tileDepth - 0.025f),
                    flooringMaterial);
                deadliftStationMarker.RegisterCollider(flooringMat.GetComponent<Collider>());
            }
        }

        CreateLoadedDeadliftBarbell(
            stationCenter, stationRotation, deadliftStationMarker);

        int[] loosePlateWeights = { 20, 10, 5, 20, 10, 5, 10 };
        Vector3[] loosePlatePositions =
        {
            new Vector3(-1.55f, 0f, 1.78f),
            new Vector3(-0.63f, 0f, 1.62f),
            new Vector3(0.35f, 0f, 1.84f),
            new Vector3(1.28f, 0f, 1.64f),
            new Vector3(-1.15f, 0f, 2.24f),
            new Vector3(0.12f, 0f, 2.28f),
            new Vector3(1.08f, 0f, 2.18f)
        };
        for (int i = 0; i < loosePlateWeights.Length; i++)
        {
            CreateLooseDeadliftPlate(
                stationCenter,
                stationRotation,
                loosePlateWeights[i],
                loosePlatePositions[i],
                (i % 2 == 0 ? -18f : 23f) + i * 7f,
                floorY,
                deadliftStationMarker);
        }

        Debug.Log(
            $"GYMCHAOS_DEADLIFT_STATION_OK center={stationCenter} " +
            $"cableFound={hasCable} shelf={shelf.Center} " +
            "platform=4x2 flooringMats loadedBar=20kgEachSide loosePlates=7 " +
            "loadedPlates=outerSleeveInnerPin mountedRigidbodies floorSettled=rendererBounds " +
            "pickup=E throw=LMB facing=mirrors",
            stationObject);
        return new DeadliftLayout
        {
            Center = stationCenter,
            Rotation = stationRotation,
            Marker = deadliftStationMarker,
            Created = true
        };
    }

    private static Vector3 GetMirrorDirection(Vector3 fromPosition)
    {
        GameObject mirrorObject = GameObject.Find("Mirror panel");
        if (mirrorObject != null)
        {
            Renderer renderer = mirrorObject.GetComponent<Renderer>();
            if (renderer != null)
            {
                Vector3 direction = Vector3.ProjectOnPlane(
                    renderer.bounds.center - fromPosition, Vector3.up);
                if (direction.sqrMagnitude > 0.04f)
                {
                    return direction.normalized;
                }
            }
        }

        // GymInteriorBuilder's mirror wall is the west wall; keep the station
        // facing it even during a headless/bootstrap layout where the mirror
        // renderer has not been registered yet.
        return Vector3.left;
    }

    private void CreateLoadedDeadliftBarbell(
        Vector3 stationCenter, Quaternion stationRotation,
        GymDeadliftStationMarker stationMarker)
    {
        GameObject barObject = new GameObject("Barbell DeadliftStation Loaded");
        barObject.transform.SetParent(itemRoot, true);
        // Start high enough for the authored visual, then settle from its
        // actual renderer bounds after the plate pair exists. This avoids a
        // mesh-specific diameter/scale guess putting the largest plate
        // through the platform.
        barObject.transform.SetPositionAndRotation(
            stationCenter + Vector3.up * 0.44f, stationRotation);

        Transform barVisual = CreateNormalizedSceneVisual(
            "barbell", barObject.transform, Vector3.zero,
            GymExerciseStation.DeadliftBarVisualMajorSize, true,
            "Deadlift Barbell Visual");
        if (barVisual == null)
        {
            CreateFallbackBarVisual(barObject.transform);
        }

        float loadedPlateCenter = GetDeadliftLoadedPlateCenter(
            barObject.transform, GymExerciseStation.DeadliftLoadedPlateCenter);
        // Use the same authored bar/plate ratio as the incline reference. The
        // 20 kg pair is the starting load and stays together on each side.
        for (int side = -1; side <= 1; side += 2)
        {
            CreateLoadedDeadliftPlate(
                barObject.transform, stationMarker, side,
                loadedPlateCenter);
        }

        Debug.Log(
            $"GYMCHAOS_DEADLIFT_LOADING_PIN center={loadedPlateCenter:F3} " +
            $"fallback={GymExerciseStation.DeadliftLoadedPlateCenter:F3}",
            barObject);

        BoxCollider collider = barObject.AddComponent<BoxCollider>();
        collider.center = Vector3.zero;
        // The plate disks have their own colliders. Keep the bar collider to
        // the shaft instead of filling the entire plate diameter; otherwise
        // a picked-up plate is deeply intersecting the bar and cannot slide
        // off when the loaded bar is carried or tilted.
        collider.size = new Vector3(4.45f, 0.14f, 0.14f);
        collider.sharedMaterial = CreateDeadliftPhysicsMaterial(WeightType.Barbell);
        stationMarker?.RegisterCollider(collider);

        Rigidbody body = barObject.AddComponent<Rigidbody>();
        GymMountedWeightMarker mountedMarker = barObject.AddComponent<GymMountedWeightMarker>();
        mountedMarker.SetMassOverride(60f);
        PickupItem pickup = barObject.AddComponent<PickupItem>();
        pickup.Configure(body, WeightType.Barbell, new[] { collider }, true,
            "Loaded deadlift barbell", 60f);
        if (!body.isKinematic)
        {
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }
        body.useGravity = false;
        body.isKinematic = true;
        Physics.SyncTransforms();
        SettleLoadedDeadliftBarbell(barObject, stationCenter);
    }

    private static void CreateLoadedDeadliftPlate(
        Transform barRoot, GymDeadliftStationMarker stationMarker,
        int side, float localCenterX)
    {
        GameObject plateObject = new GameObject(
            $"Plate20 Deadlift Loaded Plate {side}");
        plateObject.transform.SetParent(barRoot, false);
        plateObject.transform.localPosition = new Vector3(localCenterX * side, 0f, 0f);
        plateObject.transform.localRotation = Quaternion.identity;

        Transform visual = CreateNormalizedSceneVisual(
            "plate20", plateObject.transform, Vector3.zero, 0.56f, false,
            $"Deadlift Loaded Plate Visual {side}");
        if (visual == null)
        {
            CreateFallbackPlateVisual(
                plateObject.transform, Vector3.zero, 0.56f, true);
        }

        BoxCollider collider = plateObject.AddComponent<BoxCollider>();
        collider.center = Vector3.zero;
        collider.size = new Vector3(
            DeadliftLoadedPlateThickness, 0.56f, 0.56f);
        collider.sharedMaterial = CreateDeadliftPhysicsMaterial(WeightType.Plate20);
        stationMarker?.RegisterCollider(collider);

        Rigidbody body = plateObject.AddComponent<Rigidbody>();
        PickupItem pickup = plateObject.AddComponent<PickupItem>();
        pickup.Configure(body, WeightType.Plate20, new[] { collider }, true,
            "20kg loaded deadlift plate");
        // Mounted plates are real rigidbodies, but stay fixed with the bar
        // until the bar pickup explicitly detaches them. After detachment,
        // PickupItem restores gravity and the plate can slide down the shaft.
        if (!body.isKinematic)
        {
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }
        body.useGravity = false;
        body.isKinematic = true;
    }

    private static float GetDeadliftLoadedPlateCenter(
        Transform barRoot, float fallback)
    {
        if (barRoot == null)
        {
            return fallback;
        }

        Renderer[] renderers = barRoot.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
        {
            return fallback;
        }

        return GymExerciseStation.GetSceneMatchedLoadedPlateCenter(
            barRoot, fallback);
    }

    private static void SettleLoadedDeadliftBarbell(
        GameObject barObject, Vector3 stationCenter)
    {
        if (barObject == null)
        {
            return;
        }

        Physics.SyncTransforms();
        Renderer[] renderers = barObject.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
        {
            return;
        }

        Bounds occupied = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
        {
            if (renderers[i] != null)
            {
                occupied.Encapsulate(renderers[i].bounds);
            }
        }

        // Keep the physical assembly clear as well as the visible largest
        // plate. The bar root collider is slightly wider than the shaft, so
        // renderer-only settling can still leave its lower edge inside the
        // flooring mat.
        Collider[] colliders = barObject.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < colliders.Length; i++)
        {
            if (colliders[i] != null && colliders[i].enabled)
            {
                occupied.Encapsulate(colliders[i].bounds);
            }
        }

        float platformTopY = stationCenter.y + DeadliftPlatformThickness;
        float requiredLift = platformTopY + DeadliftLoadedPlateClearance - occupied.min.y;
        if (Mathf.Abs(requiredLift) > 0.0001f)
        {
            barObject.transform.position += Vector3.up * requiredLift;
        }

        Rigidbody[] bodies = barObject.GetComponentsInChildren<Rigidbody>(true);
        for (int i = 0; i < bodies.Length; i++)
        {
            if (bodies[i] == null)
            {
                continue;
            }

            bodies[i].position = bodies[i].transform.position;
            if (!bodies[i].isKinematic)
            {
                bodies[i].linearVelocity = Vector3.zero;
                bodies[i].angularVelocity = Vector3.zero;
            }
        }
        Physics.SyncTransforms();

        Debug.Log(
            $"GYMCHAOS_DEADLIFT_BAR_SETTLED platformTopY={platformTopY:F3} " +
            $"assemblyMinY={occupied.min.y + requiredLift:F3} " +
            $"lift={requiredLift:F3}", barObject);
    }

    private void CreateLooseDeadliftPlate(
        Vector3 stationCenter, Quaternion stationRotation, int weight,
        Vector3 localPosition, float tilt, float floorY,
        GymDeadliftStationMarker stationMarker)
    {
        WeightType itemType = weight >= 20
            ? WeightType.Plate20
            : weight >= 10 ? WeightType.Plate10 : WeightType.Plate5;
        string assetPrefix = weight >= 20
            ? "plate20"
            : weight >= 10 ? "plate10" : "plate5";
        float diameter = weight >= 20 ? 0.56f : weight >= 10 ? 0.48f : 0.4f;

        GameObject plateObject = new GameObject(
            $"Plate{weight} Freeweight Loose");
        plateObject.transform.SetParent(itemRoot, true);
        Vector3 worldPosition = stationCenter +
            stationRotation * new Vector3(localPosition.x, 0.08f, localPosition.z);
        plateObject.transform.SetPositionAndRotation(
            new Vector3(worldPosition.x, floorY + 0.08f, worldPosition.z),
            stationRotation * Quaternion.Euler(tilt * 0.35f, tilt, 0f));

        Transform visual = CreateNormalizedSceneVisual(
            assetPrefix, plateObject.transform, Vector3.zero, diameter, false,
            $"Deadlift Loose Plate {weight}kg");
        if (visual == null)
        {
            CreateFallbackPlateVisual(
                plateObject.transform, Vector3.zero, diameter, false);
        }
        else
        {
            // The normalized plate visual is aligned with its thin dimension
            // on local X for a loaded bar. Rotate that visual onto the floor
            // for the loose, dropped plates without changing their pickup
            // root or collider orientation.
            visual.localRotation = Quaternion.Euler(0f, 0f, -90f) *
                visual.localRotation;
        }

        BoxCollider collider = plateObject.AddComponent<BoxCollider>();
        collider.center = Vector3.zero;
        collider.size = new Vector3(diameter, 0.14f, diameter);
        collider.sharedMaterial = CreateDeadliftPhysicsMaterial(itemType);
        stationMarker?.RegisterCollider(collider);

        Rigidbody body = plateObject.AddComponent<Rigidbody>();
        PickupItem pickup = plateObject.AddComponent<PickupItem>();
        pickup.Configure(body, itemType, new[] { collider }, true,
            $"{weight}kg deadlift plate");
        if (!body.isKinematic)
        {
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }
        SettleDeadliftPlateOnFloor(plateObject, collider, floorY);
    }

    private static void SettleDeadliftPlateOnFloor(
        GameObject plateObject, Collider collider, float floorY)
    {
        if (plateObject == null || collider == null)
        {
            return;
        }

        Physics.SyncTransforms();
        Bounds occupied = collider.bounds;
        Renderer[] renderers = plateObject.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] != null)
            {
                occupied.Encapsulate(renderers[i].bounds);
            }
        }

        // Use the actual rotated renderer bounds, not the unrotated diameter
        // guess. This keeps the 20 kg plate just above the floor even when a
        // loose plate is tilted, without letting its mesh clip through it.
        float clearance = 0.008f;
        plateObject.transform.position += Vector3.up *
            (floorY + clearance - occupied.min.y);
        Physics.SyncTransforms();

        Rigidbody body = plateObject.GetComponent<Rigidbody>();
        if (body != null)
        {
            body.position = plateObject.transform.position;
            if (!body.isKinematic)
            {
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }
        }
    }

    private static GameObject CreateStaticBox(
        string name, Transform parent, Vector3 localPosition,
        Vector3 size, Material material)
    {
        GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
        box.name = name;
        box.transform.SetParent(parent, false);
        box.transform.localPosition = localPosition;
        box.transform.localRotation = Quaternion.identity;
        box.transform.localScale = size;
        Renderer renderer = box.GetComponent<Renderer>();
        if (renderer != null)
        {
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.On;
            renderer.receiveShadows = true;
        }
        return box;
    }

    private static Material FindTemplateMaterial(string[] keywords)
    {
        Transform[] transforms = UnityEngine.Object.FindObjectsByType<Transform>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < transforms.Length; i++)
        {
            Transform candidate = transforms[i];
            if (candidate == null)
            {
                continue;
            }

            string normalized = Normalize(candidate.name);
            bool matches = false;
            for (int keywordIndex = 0; keywordIndex < keywords.Length; keywordIndex++)
            {
                if (normalized.Contains(Normalize(keywords[keywordIndex])))
                {
                    matches = true;
                    break;
                }
            }
            if (!matches)
            {
                continue;
            }

            Renderer[] renderers = candidate.GetComponentsInChildren<Renderer>(true);
            for (int rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
            {
                if (renderers[rendererIndex] != null &&
                    renderers[rendererIndex].sharedMaterial != null)
                {
                    return renderers[rendererIndex].sharedMaterial;
                }
            }
        }

        return null;
    }

    private static PhysicsMaterial CreateDeadliftPhysicsMaterial(WeightType itemType)
    {
        bool plate = itemType == WeightType.Plate || itemType == WeightType.Plate5 ||
            itemType == WeightType.Plate10 || itemType == WeightType.Plate20;
        return new PhysicsMaterial("Deadlift freeweight physics")
        {
            dynamicFriction = plate ? 0.68f : 0.54f,
            staticFriction = plate ? 0.78f : 0.62f,
            bounciness = 0.03f,
            frictionCombine = PhysicsMaterialCombine.Average,
            bounceCombine = PhysicsMaterialCombine.Minimum
        };
    }

    private static Transform CreateNormalizedSceneVisual(
        string assetPrefix, Transform parent, Vector3 localPosition,
        float desiredMajorSize, bool alignLongestToX, string visualName)
    {
        Transform template = FindSceneTemplate(assetPrefix);
        if (template == null)
        {
            return null;
        }

        GameObject wrapperObject = new GameObject(visualName);
        Transform wrapper = wrapperObject.transform;
        wrapper.SetParent(parent, false);
        wrapper.localPosition = localPosition;

        GameObject clone = UnityEngine.Object.Instantiate(template.gameObject);
        clone.name = "Deadlift Visual Mesh " + assetPrefix;
        clone.SetActive(true);
        clone.transform.SetParent(wrapper, false);
        clone.transform.localPosition = Vector3.zero;
        clone.transform.localRotation = Quaternion.identity;
        clone.transform.localScale = Vector3.one;
        StripPhysics(clone);

        Renderer[] renderers = clone.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
        {
            UnityEngine.Object.Destroy(wrapperObject);
            return null;
        }

        Bounds localBounds = GetBoundsRelativeTo(wrapper, renderers);
        clone.transform.localPosition -= localBounds.center;
        Vector3 size = localBounds.size;
        Quaternion alignment = Quaternion.identity;
        if (alignLongestToX)
        {
            if (size.y >= size.x && size.y >= size.z)
            {
                alignment = Quaternion.Euler(0f, 0f, -90f);
            }
            else if (size.z >= size.x && size.z >= size.y)
            {
                alignment = Quaternion.Euler(0f, 90f, 0f);
            }
        }
        else if (size.y <= size.x && size.y <= size.z)
        {
            alignment = Quaternion.Euler(0f, 0f, -90f);
        }
        else if (size.z <= size.x && size.z <= size.y)
        {
            alignment = Quaternion.Euler(0f, 90f, 0f);
        }

        wrapper.localRotation = alignment;
        float major = Mathf.Max(size.x, Mathf.Max(size.y, size.z));
        wrapper.localScale = Vector3.one *
            (desiredMajorSize / Mathf.Max(major, 0.0001f));
        return wrapper;
    }

    private static Transform FindSceneTemplate(string prefix)
    {
        Transform[] transforms = UnityEngine.Object.FindObjectsByType<Transform>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        string expected = Normalize(prefix);
        if (expected == "barbell")
        {
            Transform inclineReference =
                GymExerciseStation.FindInclineReferenceBarTemplate();
            if (inclineReference != null)
            {
                return inclineReference;
            }
        }

        for (int i = 0; i < transforms.Length; i++)
        {
            Transform candidate = transforms[i];
            if (candidate == null || candidate.name.Contains("Asset Clone") ||
                candidate.GetComponentInParent<PlayerMovement>() != null)
            {
                continue;
            }

            bool underRuntimeItems = candidate == instance?.itemRoot ||
                (instance?.itemRoot != null && candidate.IsChildOf(instance.itemRoot));
            if (underRuntimeItems)
            {
                continue;
            }

            if (Normalize(candidate.name).StartsWith(expected) &&
                candidate.GetComponentInChildren<Renderer>(true) != null)
            {
                return candidate;
            }
        }

        return null;
    }

    private static void CreateFallbackBarVisual(Transform parent)
    {
        GameObject bar = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        bar.name = "Deadlift Fallback Bar";
        bar.transform.SetParent(parent, false);
        bar.transform.localPosition = Vector3.zero;
        bar.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
        bar.transform.localScale = new Vector3(0.055f, 2.05f, 0.055f);
        UnityEngine.Object.Destroy(bar.GetComponent<Collider>());
        Renderer renderer = bar.GetComponent<Renderer>();
        if (renderer != null)
        {
            renderer.sharedMaterial = CreateSolidMaterial(
                "Deadlift fallback bar material", new Color(0.38f, 0.42f, 0.47f),
                0.82f, 0.58f);
        }
    }

    private static void CreateFallbackPlateVisual(
        Transform parent, Vector3 localPosition, float diameter, bool vertical)
    {
        GameObject plate = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        plate.name = "Deadlift Fallback Plate";
        plate.transform.SetParent(parent, false);
        plate.transform.localPosition = localPosition;
        plate.transform.localRotation = vertical
            ? Quaternion.Euler(0f, 0f, 90f)
            : Quaternion.identity;
        plate.transform.localScale = new Vector3(diameter * 0.5f, 0.06f, diameter * 0.5f);
        UnityEngine.Object.Destroy(plate.GetComponent<Collider>());
        Renderer renderer = plate.GetComponent<Renderer>();
        if (renderer != null)
        {
            renderer.sharedMaterial = CreateSolidMaterial(
                "Deadlift fallback plate material", new Color(0.06f, 0.07f, 0.085f),
                0.7f, 0.38f);
        }
    }

    private static void StripPhysics(GameObject clone)
    {
        Collider[] colliders = clone.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < colliders.Length; i++)
        {
            colliders[i].enabled = false;
            UnityEngine.Object.Destroy(colliders[i]);
        }

        Rigidbody[] bodies = clone.GetComponentsInChildren<Rigidbody>(true);
        for (int i = 0; i < bodies.Length; i++)
        {
            if (!bodies[i].isKinematic)
            {
                bodies[i].linearVelocity = Vector3.zero;
                bodies[i].angularVelocity = Vector3.zero;
            }
            bodies[i].useGravity = false;
            bodies[i].isKinematic = true;
            bodies[i].detectCollisions = false;
        }

        PickupItem[] pickups = clone.GetComponentsInChildren<PickupItem>(true);
        for (int i = 0; i < pickups.Length; i++)
        {
            pickups[i].enabled = false;
        }
    }

    private static Bounds GetBoundsRelativeTo(Transform relativeTo, Renderer[] renderers)
    {
        Bounds result = new Bounds(
            relativeTo.InverseTransformPoint(renderers[0].bounds.center), Vector3.zero);
        for (int i = 0; i < renderers.Length; i++)
        {
            Bounds bounds = renderers[i].bounds;
            Vector3 min = bounds.min;
            Vector3 max = bounds.max;
            for (int x = 0; x <= 1; x++)
            for (int y = 0; y <= 1; y++)
            for (int z = 0; z <= 1; z++)
            {
                result.Encapsulate(relativeTo.InverseTransformPoint(new Vector3(
                    x == 0 ? min.x : max.x,
                    y == 0 ? min.y : max.y,
                    z == 0 ? min.z : max.z)));
            }
        }

        return result;
    }

    private List<ItemSpec> CreateItemSpecs(ShelfLayout shelf, DeadliftLayout deadlift)
    {
        float floorY = floorBounds.max.y;
        Bounds yogaAnchor;
        if (!TryFindLargestNamedBounds(new[] { "matt", "yogamat", "lungesmatt" }, out yogaAnchor))
        {
            yogaAnchor = new Bounds(
                new Vector3(
                    floorBounds.center.x - floorBounds.size.x * 0.16f,
                    floorY - 0.06f,
                    floorBounds.center.z - floorBounds.size.z * 0.08f),
                new Vector3(2.2f, 0.12f, 2.4f));
        }
        yogaSurfaceBounds = yogaAnchor;
        hasYogaSurfaceBounds = yogaSurfaceBounds.size.x > 0.1f && yogaSurfaceBounds.size.z > 0.1f;
        float yogaSurfaceY = yogaSurfaceBounds.max.y + 0.006f;

        List<SquatLaneLayout> squatLanes = FindSquatLaneLayouts();
        if (squatLanes.Count == 0)
        {
            Bounds fallbackBounds = new Bounds(
                floorBounds.center + new Vector3(
                    floorBounds.size.x * 0.18f, 0f, floorBounds.size.z * 0.12f),
                new Vector3(2.2f, 2.5f, 2.2f));
            Vector3 fallbackDirection = Vector3.ProjectOnPlane(
                floorBounds.center - fallbackBounds.center, Vector3.up);
            if (fallbackDirection.sqrMagnitude < 0.01f)
            {
                fallbackDirection = Vector3.right;
            }
            squatLanes.Add(new SquatLaneLayout
            {
                Name = "Fallback squat lane",
                Bounds = fallbackBounds,
                EntryDirection = fallbackDirection.normalized
            });
        }

        SquatLaneLayout primarySquatLane = squatLanes[0];
        Bounds rackAnchor = primarySquatLane.Bounds;
        // The authored extra-large mat is a visual anchor, not a tray. Keep
        // both loose yoga mats on the floor beside it, on the same z band, so
        // they read as a small stretching setup instead of a stack below it.
        float yogaSideOffset = yogaAnchor.extents.x + 1.35f;
        Vector3 rolledOutPosition = ClampToFloor(
            new Vector3(yogaAnchor.center.x - yogaSideOffset, floorY, yogaAnchor.center.z),
            new Vector3(0.58f, 0f, 0.78f));
        Vector3 halfRolledWorldPosition = new Vector3(
            yogaAnchor.center.x + yogaSideOffset, floorY, yogaAnchor.center.z);
        if (deadlift != null && deadlift.Created)
        {
            // Keep both loose mats out of the calisthenics footprint and make
            // the full-length mat the left-side companion of the deadlift
            // platform. The half-rolled mat stays on the opposite side so the
            // two props do not overlap or become a single floor pile.
            rolledOutPosition = ClampToFloor(
                deadlift.Center + deadlift.Rotation * new Vector3(-2.72f, 0f, 0.18f),
                new Vector3(0.58f, 0f, 0.78f));
            halfRolledWorldPosition = deadlift.Center +
                deadlift.Rotation * new Vector3(2.72f, 0f, 0.18f);
            halfRolledWorldPosition.y = floorY;
        }
        Vector3 halfRolledPosition = ClampToFloor(
            halfRolledWorldPosition, new Vector3(0.58f, 0f, 0.68f));
        Vector3 yogaArea = ClampToFloor(
            yogaAnchor.center + Vector3.forward * 0.15f,
            new Vector3(0.5f, 0f, 0.72f));
        Vector3 foamPosition = ClampToFloor(
            yogaArea + Vector3.right * 1.55f,
            new Vector3(0.23f, 0f, 0.58f));

        Vector3 stepPosition = ClampToFloor(
            rackAnchor.center + Vector3.forward * (rackAnchor.extents.z + 0.72f),
            new Vector3(0.62f, 0f, 0.34f));
        Vector3 redBallPosition = GetSafeMedicineBallPosition(squatLanes);
        Vector3 blueBallPosition = ClampToFloor(
            yogaArea + Vector3.forward * 1.55f,
            new Vector3(0.2f, 0f, 0.2f));

        List<ItemSpec> specs = new List<ItemSpec>
        {
            CreateSpec(
                "yoga_roll_rolledout.glb", "Rolled-out yoga mat", WeightType.YogaMat,
                1.28f, rolledOutPosition, new Vector3(0f, 0f, 0f), floorY, false, ColliderKind.Box),
            CreateSpec(
                "yoga_roll_halfrolled.glb", "Half-rolled yoga mat", WeightType.YogaMat,
                1.22f, halfRolledPosition, new Vector3(0f, 0f, 0f), floorY, false, ColliderKind.Box),
            CreateSpec(
                "foam_roller.glb", "Foam roller", WeightType.FoamRoller,
                FoamRollerScale, foamPosition, new Vector3(0f, 90f, 0f), yogaSurfaceY, true, ColliderKind.CapsuleX,
                true),
            CreateSpec(
                "step_platform.glb", "Step platform", WeightType.StepPlatform,
                1.22f, stepPosition, new Vector3(0f, 0f, 0f), floorY, true, ColliderKind.Box),
            CreateSpec(
                "red_ball.glb", "Red medicine ball", WeightType.Ball,
                MedicineBallScale, redBallPosition, Vector3.zero, floorY, true, ColliderKind.Sphere),
            CreateSpec(
                "blue_ball.glb", "Blue medicine ball", WeightType.Ball,
                MedicineBallScale, blueBallPosition, Vector3.zero, floorY, true, ColliderKind.Sphere),
            CreateSpec(
                "paper_towel.glb", "Paper towel roll", WeightType.PaperTowel,
                0.44f, shelf.Center + new Vector3(-0.54f, 0f, 0f), Vector3.zero,
                shelf.TopY, true, ColliderKind.CapsuleY),
            CreateSpec(
                "paper_towel.glb", "Paper towel roll (right)", WeightType.PaperTowel,
                0.44f, shelf.Center + new Vector3(0.54f, 0f, 0f), Vector3.zero,
                shelf.TopY, true, ColliderKind.CapsuleY)
        };

        return specs;
    }

    private List<SquatLaneLayout> FindSquatLaneLayouts()
    {
        string[] keywords = { "cage", "smithmachine", "squatrack", "powerrack" };
        Transform[] transforms = FindObjectsByType<Transform>();
        List<SquatLaneLayout> lanes = new List<SquatLaneLayout>();
        for (int i = 0; i < transforms.Length; i++)
        {
            Transform candidate = transforms[i];
            if (candidate == null || ShouldSkipAnchor(candidate))
            {
                continue;
            }

            string normalizedName = Normalize(candidate.name);
            bool matches = false;
            for (int keywordIndex = 0; keywordIndex < keywords.Length; keywordIndex++)
            {
                if (normalizedName.Contains(Normalize(keywords[keywordIndex])))
                {
                    matches = true;
                    break;
                }
            }
            if (!matches || !TryGetRendererBounds(candidate, out Bounds bounds))
            {
                continue;
            }

            bool duplicate = false;
            for (int laneIndex = 0; laneIndex < lanes.Count; laneIndex++)
            {
                Transform existingAnchor = lanes[laneIndex].Anchor;
                if (existingAnchor == candidate ||
                    (existingAnchor != null && candidate.IsChildOf(existingAnchor)) ||
                    (existingAnchor != null && existingAnchor.IsChildOf(candidate)))
                {
                    duplicate = true;
                    break;
                }
            }
            if (duplicate)
            {
                continue;
            }

            Vector3 entryDirection = Vector3.ProjectOnPlane(
                candidate.forward, Vector3.up);
            if (entryDirection.sqrMagnitude < 0.01f)
            {
                entryDirection = Vector3.ProjectOnPlane(
                    floorBounds.center - bounds.center, Vector3.up);
            }
            if (entryDirection.sqrMagnitude < 0.01f)
            {
                entryDirection = Vector3.right;
            }

            lanes.Add(new SquatLaneLayout
            {
                Name = candidate.name,
                Bounds = bounds,
                Anchor = candidate,
                EntryDirection = entryDirection.normalized
            });
        }

        lanes.Sort((left, right) =>
        {
            int distanceComparison = (left.Bounds.center - floorBounds.center).sqrMagnitude
                .CompareTo((right.Bounds.center - floorBounds.center).sqrMagnitude);
            return distanceComparison != 0
                ? distanceComparison
                : string.Compare(left.Name, right.Name, StringComparison.Ordinal);
        });
        return lanes;
    }

    private Vector3 GetSafeMedicineBallPosition(List<SquatLaneLayout> squatLanes)
    {
        Vector3 halfExtents = new Vector3(
            MedicineBallFloorHalfExtent, 0f, MedicineBallFloorHalfExtent);
        float minX = floorBounds.min.x + 0.42f + halfExtents.x;
        float maxX = floorBounds.max.x - 0.42f - halfExtents.x;
        float minZ = floorBounds.min.z + 0.42f + halfExtents.z;
        float maxZ = floorBounds.max.z - 0.42f - halfExtents.z;
        Vector3 best = ClampToFloor(floorBounds.center, halfExtents);
        float bestScore = float.NegativeInfinity;
        float bestClearance = float.NegativeInfinity;
        int gridResolution = Mathf.Max(3, MedicineBallCandidateGridResolution);

        for (int x = 0; x < gridResolution; x++)
        {
            float xT = x / (float)(gridResolution - 1);
            for (int z = 0; z < gridResolution; z++)
            {
                float zT = z / (float)(gridResolution - 1);
                Vector3 candidate = new Vector3(
                    Mathf.Lerp(minX, maxX, xT), floorBounds.max.y,
                    Mathf.Lerp(minZ, maxZ, zT));
                float minimumClearance = GetMinimumSquatLaneClearance(
                    candidate, halfExtents, squatLanes);
                float wallClearance = Mathf.Min(
                    candidate.x - floorBounds.min.x,
                    floorBounds.max.x - candidate.x,
                    candidate.z - floorBounds.min.z,
                    floorBounds.max.z - candidate.z);
                float score = minimumClearance >= 0f
                    ? 10000f + minimumClearance * 100f + wallClearance * 0.1f
                    : minimumClearance * 100f;
                if (score > bestScore)
                {
                    best = candidate;
                    bestScore = score;
                    bestClearance = minimumClearance;
                }
            }
        }

        StringBuilder laneEvidence = new StringBuilder();
        for (int i = 0; i < squatLanes.Count; i++)
        {
            SquatLaneLayout lane = squatLanes[i];
            if (i > 0)
            {
                laneEvidence.Append('|');
            }
            laneEvidence.Append(lane.Name)
                .Append(" center=").Append(lane.Bounds.center)
                .Append(" size=").Append(lane.Bounds.size.x.ToString("0.00"))
                .Append('x').Append(lane.Bounds.size.z.ToString("0.00"))
                .Append(" entry=").Append(lane.EntryDirection);
        }

        Debug.Log(
            $"GYMCHAOS_MEDICINE_BALL_LAYOUT red={best} " +
            $"squatLanes={squatLanes.Count} minClearance={bestClearance:0.00} " +
            $"entryClearance={MedicineBallEntryClearance:0.00} " +
            $"corridorDepth={MedicineBallSquatCorridorDepth:0.00} " +
            $"floorBounds={floorBounds.size.x:0.00}x{floorBounds.size.z:0.00} " +
            $"laneData={laneEvidence} " +
            $"physics=dynamic mass={MedicineBallMass:0.00} constraints=None " +
            "pickup=PickupItem impact=shared-flow",
            this);
        return best;
    }

    private static float GetMinimumSquatLaneClearance(
        Vector3 position, Vector3 halfExtents, List<SquatLaneLayout> squatLanes)
    {
        float minimumClearance = float.PositiveInfinity;
        for (int i = 0; i < squatLanes.Count; i++)
        {
            float clearance = GetSquatLaneClearance(
                position, halfExtents, squatLanes[i]);
            minimumClearance = Mathf.Min(minimumClearance, clearance);
        }
        return minimumClearance;
    }

    private static float GetSquatLaneClearance(
        Vector3 position, Vector3 halfExtents, SquatLaneLayout lane)
    {
        Vector3 entryDirection = lane.EntryDirection;
        Vector3 lateralDirection = Vector3.Cross(
            Vector3.up, entryDirection).normalized;
        Vector3 relative = position - lane.Bounds.center;
        relative.y = 0f;
        float lateralDistance = Mathf.Abs(
            Vector3.Dot(relative, lateralDirection));
        float longitudinalDistance = Vector3.Dot(relative, entryDirection);
        float lateralExtent = GetBoundsPlanarExtent(lane.Bounds, lateralDirection) +
            GetPlanarExtent(halfExtents, lateralDirection) +
            MedicineBallEntryClearance;
        float frontExtent = GetBoundsPlanarExtent(lane.Bounds, entryDirection) +
            GetPlanarExtent(halfExtents, entryDirection) +
            MedicineBallEntryClearance + MedicineBallSquatCorridorDepth;
        float rearExtent = GetBoundsPlanarExtent(lane.Bounds, entryDirection) +
            GetPlanarExtent(halfExtents, entryDirection) +
            MedicineBallEntryClearance + MedicineBallRearClearance;
        float outsideLateral = lateralDistance - lateralExtent;
        float outsideLongitudinal = longitudinalDistance < -rearExtent
            ? -rearExtent - longitudinalDistance
            : longitudinalDistance > frontExtent
                ? longitudinalDistance - frontExtent
                : 0f;
        if (outsideLateral >= 0f || outsideLongitudinal > 0f)
        {
            return Mathf.Sqrt(
                Mathf.Max(0f, outsideLateral) * Mathf.Max(0f, outsideLateral) +
                outsideLongitudinal * outsideLongitudinal);
        }

        float lateralPenetration = lateralExtent - lateralDistance;
        float longitudinalPenetration = Mathf.Min(
            frontExtent - longitudinalDistance,
            longitudinalDistance + rearExtent);
        return -Mathf.Min(lateralPenetration, longitudinalPenetration);
    }

    private static float GetPlanarExtent(Vector3 halfExtents, Vector3 direction)
    {
        return Mathf.Abs(direction.x) * halfExtents.x +
            Mathf.Abs(direction.z) * halfExtents.z;
    }

    private static float GetBoundsPlanarExtent(Bounds bounds, Vector3 direction)
    {
        return Mathf.Abs(direction.x) * bounds.extents.x +
            Mathf.Abs(direction.z) * bounds.extents.z;
    }

    private ItemSpec CreateSpec(
        string fileName, string displayName, WeightType itemType, float scale,
        Vector3 position, Vector3 eulerAngles, float supportY, bool pickable,
        ColliderKind collider, bool placeOnYogaSurface = false)
    {
        return new ItemSpec
        {
            FileName = fileName,
            DisplayName = displayName,
            ItemType = itemType,
            Scale = scale,
            Position = position,
            EulerAngles = eulerAngles,
            SupportY = supportY,
            SettleOnSupport = true,
            Pickable = pickable,
            Collider = collider,
            PlaceOnYogaSurface = placeOnYogaSurface
        };
    }

    private Vector3 ClampToFloor(Vector3 position, Vector3 approximateHalfExtents)
    {
        float margin = 0.42f;
        float minX = floorBounds.min.x + margin + approximateHalfExtents.x;
        float maxX = floorBounds.max.x - margin - approximateHalfExtents.x;
        float minZ = floorBounds.min.z + margin + approximateHalfExtents.z;
        float maxZ = floorBounds.max.z - margin - approximateHalfExtents.z;
        position.x = Mathf.Clamp(position.x, minX, maxX);
        position.z = Mathf.Clamp(position.z, minZ, maxZ);
        position.y = floorBounds.max.y;
        return position;
    }

    private IEnumerator LoadAndSpawnItems(List<ItemSpec> specs)
    {
        int spawned = 0;
        for (int i = 0; i < specs.Count; i++)
        {
            ItemSpec spec = specs[i];
            if (!assetCache.TryGetValue(spec.FileName, out ItemAsset asset))
            {
                yield return StartCoroutine(LoadAsset(spec.FileName));
                assetCache.TryGetValue(spec.FileName, out asset);
            }

            if (asset == null)
            {
                continue;
            }

            CreateLooseItem(spec, asset);
            spawned++;
            yield return null;
        }

        Debug.Log(
            $"GYMCHAOS_LOOSE_ITEMS_OK spawned={spawned}/{specs.Count} " +
            "collisions=primitive pickup=E throw=LMB " +
            $"scales=foam{FoamRollerScale:0.####} balls{MedicineBallScale:0.####} " +
            "source=BodyBuilders/items",
            this);
    }

    private IEnumerator LoadAsset(string fileName)
    {
        string relativePath = "BodyBuilders/items/" + fileName;
        byte[] glbBytes = null;
        string localPath = Path.Combine(
            Application.dataPath,
            "StreamingAssets",
            "BodyBuilders",
            "items",
            fileName);
        bool localFileExists = File.Exists(localPath);
        Debug.Log(
            $"GYMCHAOS_LOOSE_ITEM_SOURCE file={fileName} localExists={localFileExists} " +
            $"path={localPath}",
            this);

        // On local Windows/Editor installs, reading the file directly avoids a
        // file:// UnityWebRequest waiting indefinitely when the GameView is not
        // focused. Keep the request path for packaged platforms whose
        // StreamingAssets are inside an archive or mounted URL.
        if (localFileExists)
        {
            try
            {
                glbBytes = File.ReadAllBytes(localPath);
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    $"GYMCHAOS_LOOSE_ITEM_LOAD_ERROR file={fileName} path={localPath} " +
                    $"error={exception.Message}",
                    this);
                assetCache[fileName] = null;
                yield break;
            }
        }
        else
        {
            string path = JoinStreamingAssetsPath(relativePath);
            using (UnityWebRequest request = UnityWebRequest.Get(path))
            {
                request.downloadHandler = new DownloadHandlerBuffer();
                yield return request.SendWebRequest();

                if (request.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogError(
                        $"GYMCHAOS_LOOSE_ITEM_LOAD_ERROR file={fileName} path={path} " +
                        $"error={request.error}",
                        this);
                    assetCache[fileName] = null;
                    yield break;
                }

                glbBytes = request.downloadHandler.data;
            }
        }

        if (!TryReadGlb(glbBytes, out GltfRoot gltf, out byte[] binary))
        {
            Debug.LogError($"GYMCHAOS_LOOSE_ITEM_PARSE_ERROR file={fileName}", this);
            assetCache[fileName] = null;
            yield break;
        }

        ItemAsset asset = CreateAsset(fileName, gltf, binary);
        assetCache[fileName] = asset;
        yield return null;
    }

    private void CreateLooseItem(ItemSpec spec, ItemAsset asset)
    {
        GameObject itemObject = new GameObject("Loose Item - " + spec.DisplayName);
        itemObject.transform.SetParent(itemRoot, true);
        Vector3 itemPosition = spec.Position;
        if (spec.SettleOnSupport)
        {
            itemPosition.y = spec.SupportY - asset.LocalBounds.min.y * spec.Scale + 0.008f;
        }

        itemObject.transform.SetPositionAndRotation(
            itemPosition,
            Quaternion.Euler(spec.EulerAngles));
        itemObject.transform.localScale = Vector3.one * spec.Scale;

        MeshFilter meshFilter = itemObject.AddComponent<MeshFilter>();
        meshFilter.sharedMesh = asset.Mesh;
        MeshRenderer renderer = itemObject.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = asset.Material;
        renderer.shadowCastingMode = ShadowCastingMode.On;
        renderer.receiveShadows = true;

        if (spec.PlaceOnYogaSurface && hasYogaSurfaceBounds)
        {
            PositionOnYogaSurface(itemObject, renderer, spec);
        }
        else if (spec.SettleOnSupport)
        {
            SettleRendererOnSupport(itemObject, renderer, spec.SupportY);
        }

        Collider collider = AddCollider(itemObject, asset.LocalBounds, spec);
        collider.sharedMaterial = CreatePhysicsMaterial(spec);
        if (spec.ItemType == WeightType.Ball && spec.SettleOnSupport)
        {
            // The sphere uses the largest mesh extent, which can reach lower
            // than the rendered ball. Settle the actual collider so it never
            // starts intersecting the floor and receives a depenetration kick.
            float colliderLift = spec.SupportY + 0.008f - collider.bounds.min.y;
            if (colliderLift > 0f)
            {
                itemObject.transform.position += Vector3.up * colliderLift;
                Physics.SyncTransforms();
            }
        }

        if (spec.Pickable)
        {
            Rigidbody body = itemObject.AddComponent<Rigidbody>();
            PickupItem pickup = itemObject.AddComponent<PickupItem>();
            float massOverride = spec.ItemType == WeightType.Ball
                ? MedicineBallMass
                : -1f;
            pickup.Configure(
                body, spec.ItemType, new[] { collider }, true,
                spec.DisplayName, massOverride);
            if (spec.ItemType == WeightType.Ball)
            {
                ConfigureMedicineBallPhysics(body);
                StartCoroutine(SettleMedicineBallAfterSpawn(body, collider, spec.SupportY));
            }
        }
    }

    private void PositionOnYogaSurface(GameObject itemObject, Renderer renderer, ItemSpec spec)
    {
        float surfaceY = yogaSurfaceBounds.max.y + 0.006f;
        float xExtent = Mathf.Max(0.1f, yogaSurfaceBounds.extents.x);
        float zExtent = Mathf.Max(0.1f, yogaSurfaceBounds.extents.z);
        bool isFoamRoller = spec.ItemType == WeightType.FoamRoller;
        Vector3 center = yogaSurfaceBounds.center;
        Vector3[] candidates = isFoamRoller
            ? new[]
            {
                center + new Vector3(xExtent * 0.34f, 0f, -zExtent * 0.22f),
                center + new Vector3(xExtent * 0.36f, 0f, zExtent * 0.25f),
                center + new Vector3(0f, 0f, -zExtent * 0.36f)
            }
            : new[]
            {
                center + new Vector3(-xExtent * 0.34f, 0f, zExtent * 0.22f),
                center + new Vector3(-xExtent * 0.36f, 0f, -zExtent * 0.25f),
                center + new Vector3(0f, 0f, zExtent * 0.34f)
            };

        bool placed = false;
        for (int i = 0; i < candidates.Length; i++)
        {
            if (!TryPlaceYogaCandidate(itemObject, renderer, candidates[i], surfaceY))
            {
                continue;
            }

            placed = true;
            break;
        }

        if (!placed)
        {
            // The authored mat can have a different size after a scene edit.
            // Search a small deterministic grid before accepting a fallback;
            // this keeps the two props separated without relying on guessed
            // hard-coded offsets.
            float minX = yogaSurfaceBounds.min.x;
            float maxX = yogaSurfaceBounds.max.x;
            float minZ = yogaSurfaceBounds.min.z;
            float maxZ = yogaSurfaceBounds.max.z;
            for (int x = 0; x <= 6 && !placed; x++)
            {
                for (int z = 0; z <= 6; z++)
                {
                    Vector3 candidate = new Vector3(
                        Mathf.Lerp(minX, maxX, x / 6f),
                        surfaceY,
                        Mathf.Lerp(minZ, maxZ, z / 6f));
                    if (TryPlaceYogaCandidate(itemObject, renderer, candidate, surfaceY))
                    {
                        placed = true;
                        break;
                    }
                }
            }
        }

        if (!placed)
        {
            Vector3 fallback = new Vector3(center.x, surfaceY, center.z);
            itemObject.transform.position = fallback;
            SettleRendererOnSupport(itemObject, renderer, surfaceY);
            Debug.LogWarning(
                $"GYMCHAOS_LOOSE_ITEM_SURFACE_FALLBACK item={spec.DisplayName} " +
                $"surface={yogaSurfaceBounds.size.x:F2}x{yogaSurfaceBounds.size.z:F2}",
                itemObject);
        }

        yogaSurfaceItemBounds.Add(renderer.bounds);
    }

    private bool TryPlaceYogaCandidate(
        GameObject itemObject, Renderer renderer, Vector3 candidate, float surfaceY)
    {
        itemObject.transform.position = new Vector3(candidate.x, surfaceY, candidate.z);
        SettleRendererOnSupport(itemObject, renderer, surfaceY);
        Bounds candidateBounds = renderer.bounds;
        return IsInsideYogaSurface(candidateBounds) &&
            !IntersectsYogaSurfaceItem(candidateBounds);
    }

    private void SettleRendererOnSupport(GameObject itemObject, Renderer renderer, float supportY)
    {
        if (itemObject == null || renderer == null)
        {
            return;
        }

        itemObject.transform.position += Vector3.up * (supportY - renderer.bounds.min.y + 0.008f);
    }

    private bool IsInsideYogaSurface(Bounds itemBounds)
    {
        const float edgePadding = 0.025f;
        return itemBounds.min.x >= yogaSurfaceBounds.min.x + edgePadding &&
            itemBounds.max.x <= yogaSurfaceBounds.max.x - edgePadding &&
            itemBounds.min.z >= yogaSurfaceBounds.min.z + edgePadding &&
            itemBounds.max.z <= yogaSurfaceBounds.max.z - edgePadding;
    }

    private bool IntersectsYogaSurfaceItem(Bounds candidate)
    {
        const float separation = 0.035f;
        for (int i = 0; i < yogaSurfaceItemBounds.Count; i++)
        {
            Bounds existing = yogaSurfaceItemBounds[i];
            if (candidate.min.x < existing.max.x + separation &&
                candidate.max.x > existing.min.x - separation &&
                candidate.min.z < existing.max.z + separation &&
                candidate.max.z > existing.min.z - separation)
            {
                return true;
            }
        }

        return false;
    }

    private Collider AddCollider(GameObject itemObject, Bounds bounds, ItemSpec spec)
    {
        Collider collider;
        switch (spec.Collider)
        {
            case ColliderKind.Sphere:
            {
                SphereCollider sphere = itemObject.AddComponent<SphereCollider>();
                sphere.center = bounds.center;
                sphere.radius = Mathf.Max(bounds.extents.x, Mathf.Max(bounds.extents.y, bounds.extents.z));
                collider = sphere;
                break;
            }
            case ColliderKind.CapsuleX:
            {
                CapsuleCollider capsule = itemObject.AddComponent<CapsuleCollider>();
                capsule.direction = 0;
                capsule.center = bounds.center;
                capsule.radius = Mathf.Max(bounds.extents.y, bounds.extents.z);
                capsule.height = Mathf.Max(bounds.size.x, capsule.radius * 2f);
                collider = capsule;
                break;
            }
            case ColliderKind.CapsuleY:
            {
                CapsuleCollider capsule = itemObject.AddComponent<CapsuleCollider>();
                capsule.direction = 1;
                capsule.center = bounds.center;
                capsule.radius = Mathf.Max(bounds.extents.x, bounds.extents.z);
                capsule.height = Mathf.Max(bounds.size.y, capsule.radius * 2f);
                collider = capsule;
                break;
            }
            default:
            {
                BoxCollider box = itemObject.AddComponent<BoxCollider>();
                box.center = bounds.center;
                box.size = bounds.size;
                collider = box;
                break;
            }
        }

        return collider;
    }

    private static void ConfigureMedicineBallPhysics(Rigidbody body)
    {
        if (body == null)
        {
            return;
        }

        body.mass = MedicineBallMass;
        body.isKinematic = false;
        body.useGravity = true;
        body.constraints = RigidbodyConstraints.None;
        body.linearDamping = 0.35f;
        body.angularDamping = 0.55f;
        body.sleepThreshold = 0.02f;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        body.linearVelocity = Vector3.zero;
        body.angularVelocity = Vector3.zero;
        Physics.SyncTransforms();
        body.Sleep();
    }

    private static IEnumerator SettleMedicineBallAfterSpawn(
        Rigidbody body, Collider collider, float supportY)
    {
        for (int i = 0; i < 4; i++)
        {
            yield return new WaitForFixedUpdate();
        }
        if (body == null || collider == null)
        {
            yield break;
        }

        float physicalSupportY = supportY;
        Vector3 rayOrigin = collider.bounds.center + Vector3.up * 0.25f;
        RaycastHit[] supportHits = Physics.RaycastAll(
            rayOrigin, Vector3.down, 5f, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < supportHits.Length; i++)
        {
            Collider hitCollider = supportHits[i].collider;
            if (hitCollider == null ||
                hitCollider.transform == body.transform ||
                hitCollider.transform.IsChildOf(body.transform) ||
                body.transform.IsChildOf(hitCollider.transform))
            {
                continue;
            }
            if (supportHits[i].point.y <= collider.bounds.center.y + 0.05f)
            {
                physicalSupportY = Mathf.Max(
                    physicalSupportY, supportHits[i].point.y);
            }
        }
        float correction = physicalSupportY + 0.008f - collider.bounds.min.y;
        body.position += Vector3.up * correction;
        body.linearVelocity = Vector3.zero;
        body.angularVelocity = Vector3.zero;
        Physics.SyncTransforms();
        body.Sleep();
    }

    private PhysicsMaterial CreatePhysicsMaterial(ItemSpec spec)
    {
        PhysicsMaterial material = new PhysicsMaterial("Loose item physics - " + spec.DisplayName)
        {
            dynamicFriction = spec.ItemType == WeightType.Ball ? 0.25f : 0.52f,
            staticFriction = spec.ItemType == WeightType.Ball ? 0.35f : 0.62f,
            bounciness = spec.ItemType == WeightType.Ball ? 0.42f : 0.08f,
            frictionCombine = PhysicsMaterialCombine.Average,
            bounceCombine = PhysicsMaterialCombine.Maximum
        };
        return material;
    }

    private static bool TryReadGlb(byte[] bytes, out GltfRoot gltf, out byte[] binary)
    {
        gltf = null;
        binary = null;
        if (bytes == null || bytes.Length < 20 || BitConverter.ToUInt32(bytes, 0) != GlbMagic)
        {
            return false;
        }

        int offset = 12;
        string json = null;
        while (offset + 8 <= bytes.Length)
        {
            int length = (int)BitConverter.ToUInt32(bytes, offset);
            uint type = BitConverter.ToUInt32(bytes, offset + 4);
            offset += 8;
            if (length < 0 || offset + length > bytes.Length)
            {
                return false;
            }

            if (type == JsonChunk)
            {
                json = Encoding.UTF8.GetString(bytes, offset, length)
                    .TrimEnd('\0', ' ', '\n', '\r', '\t');
            }
            else if (type == BinaryChunk)
            {
                binary = new byte[length];
                Buffer.BlockCopy(bytes, offset, binary, 0, length);
            }

            offset += length;
        }

        if (string.IsNullOrEmpty(json) || binary == null)
        {
            return false;
        }

        gltf = JsonUtility.FromJson<GltfRoot>(json);
        return gltf != null && gltf.meshes != null && gltf.meshes.Length > 0;
    }

    private static ItemAsset CreateAsset(string fileName, GltfRoot gltf, byte[] binary)
    {
        if (gltf.meshes[0].primitives == null || gltf.meshes[0].primitives.Length == 0)
        {
            Debug.LogError($"Loose item GLB has no primitive: {fileName}");
            return null;
        }

        GltfPrimitive primitive = gltf.meshes[0].primitives[0];
        if (primitive.attributes == null || primitive.attributes.POSITION < 0)
        {
            Debug.LogError($"Loose item GLB has no POSITION accessor: {fileName}");
            return null;
        }

        Vector3[] sourcePositions = ReadVector3Accessor(gltf, binary, primitive.attributes.POSITION);
        Vector3[] sourceNormals = ReadVector3Accessor(gltf, binary, primitive.attributes.NORMAL);
        Vector2[] sourceUvs = ReadVector2Accessor(gltf, binary, primitive.attributes.TEXCOORD_0);
        int[] triangles = ReadIndexAccessor(gltf, binary, primitive.indices);
        if (sourcePositions.Length == 0 || triangles.Length < 3)
        {
            Debug.LogError($"Loose item GLB has empty geometry: {fileName}");
            return null;
        }

        Vector3[] positions = new Vector3[sourcePositions.Length];
        Vector3[] normals = sourceNormals.Length == sourcePositions.Length
            ? new Vector3[sourceNormals.Length]
            : null;
        Vector2[] uvs = sourceUvs.Length == sourcePositions.Length
            ? new Vector2[sourceUvs.Length]
            : null;
        for (int i = 0; i < sourcePositions.Length; i++)
        {
            Vector3 source = sourcePositions[i];
            positions[i] = new Vector3(-source.x, source.y, source.z);
            if (normals != null)
            {
                Vector3 normal = sourceNormals[i];
                normals[i] = new Vector3(-normal.x, normal.y, normal.z).normalized;
            }

            if (uvs != null)
            {
                uvs[i] = new Vector2(sourceUvs[i].x, 1f - sourceUvs[i].y);
            }
        }

        for (int i = 0; i + 2 < triangles.Length; i += 3)
        {
            int swap = triangles[i];
            triangles[i] = triangles[i + 2];
            triangles[i + 2] = swap;
        }

        Mesh mesh = new Mesh
        {
            name = "Loose item mesh - " + fileName,
            indexFormat = IndexFormat.UInt32,
            vertices = positions,
            triangles = triangles
        };
        if (normals != null)
        {
            mesh.normals = normals;
        }
        else
        {
            mesh.RecalculateNormals();
        }

        if (uvs != null)
        {
            mesh.uv = uvs;
        }

        mesh.RecalculateBounds();
        Material material = CreateItemMaterial(fileName, gltf, binary);
        return new ItemAsset { Mesh = mesh, Material = material, LocalBounds = mesh.bounds };
    }

    private static Material CreateItemMaterial(string fileName, GltfRoot gltf, byte[] binary)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        if (shader == null)
        {
            shader = Shader.Find("Universal Render Pipeline/Unlit");
        }

        if (shader == null)
        {
            Debug.LogError($"No shader available for loose item: {fileName}");
            return null;
        }

        Material material = new Material(shader)
        {
            name = "Loose item material - " + fileName,
            enableInstancing = true
        };
        material.SetColor("_BaseColor", Color.white);
        material.SetColor("_Color", Color.white);
        if (material.HasProperty("_Metallic"))
        {
            material.SetFloat("_Metallic", 0.08f);
        }
        if (material.HasProperty("_Smoothness"))
        {
            material.SetFloat("_Smoothness", 0.48f);
        }

        if (gltf.images == null || gltf.images.Length == 0)
        {
            return material;
        }

        GltfImage image = gltf.images[0];
        if (image == null || image.bufferView < 0 || image.bufferView >= gltf.bufferViews.Length)
        {
            return material;
        }

        GltfBufferView view = gltf.bufferViews[image.bufferView];
        if (view == null || view.byteOffset < 0 || view.byteOffset + view.byteLength > binary.Length)
        {
            return material;
        }

        byte[] imageBytes = new byte[view.byteLength];
        Buffer.BlockCopy(binary, view.byteOffset, imageBytes, 0, view.byteLength);
        Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, true)
        {
            name = "Loose item texture - " + fileName,
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Repeat
        };
        if (texture.LoadImage(imageBytes, false))
        {
            material.SetTexture("_BaseMap", texture);
            material.SetTexture("_MainTex", texture);
            material.mainTexture = texture;
        }
        else
        {
            DestroyRuntimeObject(texture);
        }

        return material;
    }

    private static void DestroyRuntimeObject(UnityEngine.Object target)
    {
        if (target == null)
        {
            return;
        }

        if (Application.isPlaying)
        {
            Destroy(target);
        }
        else
        {
            DestroyImmediate(target);
        }
    }

    private static Vector3[] ReadVector3Accessor(GltfRoot gltf, byte[] binary, int accessorIndex)
    {
        if (accessorIndex < 0 || accessorIndex >= gltf.accessors.Length)
        {
            return Array.Empty<Vector3>();
        }

        GltfAccessor accessor = gltf.accessors[accessorIndex];
        GltfBufferView view = gltf.bufferViews[accessor.bufferView];
        int stride = view.byteStride > 0 ? view.byteStride : 12;
        int start = view.byteOffset + accessor.byteOffset;
        Vector3[] result = new Vector3[accessor.count];
        for (int i = 0; i < result.Length; i++)
        {
            int offset = start + i * stride;
            result[i] = new Vector3(
                BitConverter.ToSingle(binary, offset),
                BitConverter.ToSingle(binary, offset + 4),
                BitConverter.ToSingle(binary, offset + 8));
        }

        return result;
    }

    private static Vector2[] ReadVector2Accessor(GltfRoot gltf, byte[] binary, int accessorIndex)
    {
        if (accessorIndex < 0 || accessorIndex >= gltf.accessors.Length)
        {
            return Array.Empty<Vector2>();
        }

        GltfAccessor accessor = gltf.accessors[accessorIndex];
        GltfBufferView view = gltf.bufferViews[accessor.bufferView];
        int stride = view.byteStride > 0 ? view.byteStride : 8;
        int start = view.byteOffset + accessor.byteOffset;
        Vector2[] result = new Vector2[accessor.count];
        for (int i = 0; i < result.Length; i++)
        {
            int offset = start + i * stride;
            result[i] = new Vector2(
                BitConverter.ToSingle(binary, offset),
                BitConverter.ToSingle(binary, offset + 4));
        }

        return result;
    }

    private static int[] ReadIndexAccessor(GltfRoot gltf, byte[] binary, int accessorIndex)
    {
        if (accessorIndex < 0 || accessorIndex >= gltf.accessors.Length)
        {
            return Array.Empty<int>();
        }

        GltfAccessor accessor = gltf.accessors[accessorIndex];
        GltfBufferView view = gltf.bufferViews[accessor.bufferView];
        int componentSize = accessor.componentType == 5125 ? 4 : accessor.componentType == 5123 ? 2 : 1;
        int stride = view.byteStride > 0 ? view.byteStride : componentSize;
        int start = view.byteOffset + accessor.byteOffset;
        int[] result = new int[accessor.count];
        for (int i = 0; i < result.Length; i++)
        {
            int offset = start + i * stride;
            result[i] = accessor.componentType == 5125
                ? (int)BitConverter.ToUInt32(binary, offset)
                : accessor.componentType == 5123
                    ? BitConverter.ToUInt16(binary, offset)
                    : binary[offset];
        }

        return result;
    }

    private bool TryFindFloorBounds(out Bounds bounds)
    {
        bounds = default;
        GameObject interior = GameObject.Find(InteriorRootName);
        if (interior == null)
        {
            return false;
        }

        Transform floor = FindChildByName(interior.transform, "Rubber Floor");
        if (floor != null)
        {
            BoxCollider floorCollider = floor.GetComponent<BoxCollider>();
            if (floorCollider != null && floorCollider.enabled)
            {
                bounds = floorCollider.bounds;
                return bounds.size.x > 1f && bounds.size.z > 1f;
            }

            Renderer floorRenderer = floor.GetComponent<Renderer>();
            if (floorRenderer != null)
            {
                bounds = floorRenderer.bounds;
                return bounds.size.x > 1f && bounds.size.z > 1f;
            }
        }

        Renderer[] renderers = interior.GetComponentsInChildren<Renderer>(true);
        bool hasBounds = false;
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null || renderer.name != "Rubber Floor")
            {
                continue;
            }

            bounds = renderer.bounds;
            hasBounds = true;
            break;
        }

        return hasBounds && bounds.size.x > 1f && bounds.size.z > 1f;
    }

    private bool TryFindNamedBounds(string[] keywords, out Bounds bounds)
    {
        return TryFindNamedBounds(keywords, out bounds, out _);
    }

    private bool TryFindNamedBounds(
        string[] keywords, out Bounds bounds, out Transform anchor)
    {
        bounds = default;
        anchor = null;
        Transform[] transforms = FindObjectsByType<Transform>();
        bool found = false;
        float bestScore = float.PositiveInfinity;
        for (int i = 0; i < transforms.Length; i++)
        {
            Transform candidate = transforms[i];
            if (candidate == null || ShouldSkipAnchor(candidate))
            {
                continue;
            }

            string normalizedName = Normalize(candidate.name);
            bool matches = false;
            for (int keywordIndex = 0; keywordIndex < keywords.Length; keywordIndex++)
            {
                if (normalizedName.Contains(Normalize(keywords[keywordIndex])))
                {
                    matches = true;
                    break;
                }
            }

            if (!matches || !TryGetRendererBounds(candidate, out Bounds candidateBounds))
            {
                continue;
            }

            float score = (candidateBounds.center - floorBounds.center).sqrMagnitude;
            if (!found || score < bestScore)
            {
                found = true;
                bestScore = score;
                bounds = candidateBounds;
                anchor = candidate;
            }
        }

        return found;
    }

    private bool TryFindLargestNamedBounds(string[] keywords, out Bounds bounds)
    {
        bounds = default;
        Transform[] transforms = FindObjectsByType<Transform>();
        bool found = false;
        float largestArea = 0f;
        for (int i = 0; i < transforms.Length; i++)
        {
            Transform candidate = transforms[i];
            if (candidate == null || ShouldSkipAnchor(candidate))
            {
                continue;
            }

            string normalizedName = Normalize(candidate.name);
            bool matches = false;
            for (int keywordIndex = 0; keywordIndex < keywords.Length; keywordIndex++)
            {
                if (normalizedName.Contains(Normalize(keywords[keywordIndex])))
                {
                    matches = true;
                    break;
                }
            }

            if (!matches || !TryGetRendererBounds(candidate, out Bounds candidateBounds))
            {
                continue;
            }

            float area = candidateBounds.size.x * candidateBounds.size.z;
            if (!found || area > largestArea)
            {
                found = true;
                largestArea = area;
                bounds = candidateBounds;
            }
        }

        return found;
    }

    private bool ShouldSkipAnchor(Transform candidate)
    {
        if (candidate == itemRoot || candidate.IsChildOf(itemRoot))
        {
            return true;
        }

        if (candidate.GetComponentInParent<PlayerMovement>() != null ||
            candidate.GetComponentInParent<EnemyFighter>() != null)
        {
            return true;
        }

        for (Transform current = candidate; current != null; current = current.parent)
        {
            string normalized = Normalize(current.name);
            if (normalized.Contains("gyminteriorruntime") ||
                normalized.Contains("gymbackarearuntime") ||
                normalized.Contains("gymexteriorruntime"))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryGetRendererBounds(Transform target, out Bounds bounds)
    {
        bounds = default;
        Renderer[] renderers = target.GetComponentsInChildren<Renderer>(true);
        bool found = false;
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null || !renderer.enabled || renderer is ParticleSystemRenderer)
            {
                continue;
            }

            if (!found)
            {
                bounds = renderer.bounds;
                found = true;
            }
            else
            {
                bounds.Encapsulate(renderer.bounds);
            }
        }

        return found;
    }

    private static Transform FindChildByName(Transform parent, string targetName)
    {
        Transform[] children = parent.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < children.Length; i++)
        {
            if (children[i] != null && children[i].name == targetName)
            {
                return children[i];
            }
        }

        return null;
    }

    private static string Normalize(string value)
    {
        return value.ToLowerInvariant()
            .Replace(" ", string.Empty)
            .Replace("_", string.Empty)
            .Replace("-", string.Empty)
            .Replace("(", string.Empty)
            .Replace(")", string.Empty);
    }

    private static Material CreateSolidMaterial(
        string name, Color color, float metallic, float smoothness)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        Material material = new Material(shader)
        {
            name = name,
            color = color
        };
        material.SetColor("_BaseColor", color);
        material.SetColor("_Color", color);
        material.SetFloat("_Metallic", metallic);
        material.SetFloat("_Smoothness", smoothness);
        return material;
    }

    private static string JoinStreamingAssetsPath(string relativePath)
    {
        string path = Application.streamingAssetsPath.TrimEnd('/', '\\') + "/" + relativePath;
        if (path.Contains("://"))
        {
            return path;
        }

        return "file:///" + path.Replace('\\', '/').TrimStart('/');
    }
}
