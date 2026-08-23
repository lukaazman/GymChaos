using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.Rendering;

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

    private static GymLooseItemSpawner instance;

    private readonly Dictionary<string, ItemAsset> assetCache =
        new Dictionary<string, ItemAsset>(StringComparer.OrdinalIgnoreCase);

    private Transform itemRoot;
    private Bounds floorBounds;
    private bool hasFloorBounds;
    private Bounds yogaSurfaceBounds;
    private bool hasYogaSurfaceBounds;
    private readonly List<Bounds> yogaSurfaceItemBounds = new List<Bounds>();

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

    private sealed class ShelfLayout
    {
        public Vector3 Center;
        public float TopY;
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
        List<ItemSpec> specs = CreateItemSpecs(shelf);
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

    private List<ItemSpec> CreateItemSpecs(ShelfLayout shelf)
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

        Bounds rackAnchor;
        if (!TryFindNamedBounds(
                new[] { "cage", "smithmachine", "squatrack", "powerrack" },
                out rackAnchor))
        {
            rackAnchor = new Bounds(
                floorBounds.center + new Vector3(floorBounds.size.x * 0.18f, 0f, floorBounds.size.z * 0.12f),
                new Vector3(2.2f, 2.5f, 2.2f));
        }

        // The authored extra-large mat is a visual anchor, not a tray. Keep
        // both loose yoga mats on the floor beside it, on the same z band, so
        // they read as a small stretching setup instead of a stack below it.
        float yogaSideOffset = yogaAnchor.extents.x + 1.35f;
        Vector3 rolledOutPosition = ClampToFloor(
            new Vector3(yogaAnchor.center.x - yogaSideOffset, floorY, yogaAnchor.center.z),
            new Vector3(0.58f, 0f, 0.78f));
        Vector3 halfRolledPosition = ClampToFloor(
            new Vector3(yogaAnchor.center.x + yogaSideOffset, floorY, yogaAnchor.center.z),
            new Vector3(0.58f, 0f, 0.68f));
        Vector3 yogaArea = ClampToFloor(
            yogaAnchor.center + Vector3.forward * 0.15f,
            new Vector3(0.5f, 0f, 0.72f));
        Vector3 foamPosition = ClampToFloor(
            yogaArea + Vector3.right * 1.55f,
            new Vector3(0.23f, 0f, 0.58f));

        Vector3 stepPosition = ClampToFloor(
            rackAnchor.center + Vector3.forward * (rackAnchor.extents.z + 0.72f),
            new Vector3(0.62f, 0f, 0.34f));
        Vector3 redBallPosition = ClampToFloor(
            rackAnchor.center + Vector3.right * (rackAnchor.extents.x + 0.95f),
            new Vector3(0.2f, 0f, 0.2f));
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
                1.22f, stepPosition, new Vector3(0f, 0f, 0f), floorY, false, ColliderKind.Box),
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

        if (spec.Pickable)
        {
            Rigidbody body = itemObject.AddComponent<Rigidbody>();
            PickupItem pickup = itemObject.AddComponent<PickupItem>();
            pickup.Configure(body, spec.ItemType, new[] { collider }, true, spec.DisplayName);
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
        bounds = default;
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
