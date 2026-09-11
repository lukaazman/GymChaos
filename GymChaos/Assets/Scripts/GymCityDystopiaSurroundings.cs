using UnityEngine;

/// <summary>
/// Places the authored city_dystopia Blender export outside the unchanged gym
/// shell, parking lot and vehicle route.  The copies form a four-sided ring
/// plus corner coverage so the player never sees an empty horizon beyond the
/// existing perimeter walls.
/// </summary>
public static class GymCityDystopiaSurroundings
{
    private const string RootName = "City Dystopia Surroundings (Runtime)";
    private const string AssetPath = "BodyBuilders/outside/city_dystopia_backdrop.glb";

    // Conservative source bounds of the Blender city backdrop before runtime
    // scale.  They are intentionally slightly larger than the exported mesh
    // so the placement remains outside every fence/wall even if the exporter
    // or GLB loader changes a small amount of padding.
    // Bounds of the exported city after removing the presentation ground and
    // reflection strips.  The layout is intentionally asymmetric in depth:
    // the near city row is only about 18 m from the exported origin while the
    // far row reaches about 93 m.  Using a symmetric half-bound left a large
    // empty blue horizon between the gym fence and the first building row.
    private const float SourceNearWidth = 98.5f;
    private const float SourceFarWidth = 100.8f;
    private const float SourceNearDepth = 18.2f;
    private const float SourceFarDepth = 93.4f;
    private const float SideScale = 0.54f;
    private const float CornerScale = 0.42f;
    private const float PerimeterGap = 1.15f;
    private const float CityGroundDepth = 54f;
    private const float CityGroundGap = 0.45f;

    public static bool IsBuilt { get; private set; }
    public static Bounds ProtectedBounds { get; private set; }
    public static int ExpectedPlacements { get; private set; }
    public static int ReadyPlacements { get; private set; }
    public static int LoadFailures { get; private set; }
    public static int HorizontalOverlaps { get; private set; }

    public static void Build(
        Transform parent,
        Bounds roomFloor,
        float floorY,
        float parkingMinX,
        float parkingMaxX,
        float parkingCenterZ,
        float doorZ,
        float pathCenterX,
        float pathSouthZ,
        float pathNorthZ,
        float outerPathX,
        float roadStartX,
        float roadEndX,
        Vector3 roadExit,
        float pathWidth,
        float roadWidth)
    {
        if (parent == null || GameObject.Find(RootName) != null)
        {
            return;
        }

        float parkingMinZ = parkingCenterZ - 9f - 1.25f;
        float parkingMaxZ = parkingCenterZ + 9f + 1.25f;
        float protectedMinX = Mathf.Min(
            roomFloor.min.x - 1.1f,
            parkingMinX - 1.25f,
            pathCenterX - pathWidth * 0.5f - 1.1f,
            roadStartX - 1.1f);
        float protectedMaxX = Mathf.Max(
            roomFloor.max.x + 1.1f,
            parkingMaxX + 1.25f,
            outerPathX + 1.1f,
            roadEndX + 4.2f);
        float protectedMinZ = Mathf.Min(
            roomFloor.min.z - 1.1f,
            parkingMinZ,
            pathSouthZ - 1.1f,
            parkingCenterZ - roadWidth * 0.5f - 1.1f);
        float protectedMaxZ = Mathf.Max(
            roomFloor.max.z + 1.1f,
            parkingMaxZ,
            pathNorthZ + 1.1f,
            roadExit.z + 3.2f,
            parkingCenterZ + roadWidth * 0.5f + 1.1f);

        bool lockerRoomIncluded = false;
        if (GymBackRoomBuilder.TryGetRoomBounds(out Bounds lockerRoomBounds))
        {
            const float lockerRoomClearance = 1.1f;
            protectedMinX = Mathf.Min(protectedMinX, lockerRoomBounds.min.x - lockerRoomClearance);
            protectedMaxX = Mathf.Max(protectedMaxX, lockerRoomBounds.max.x + lockerRoomClearance);
            protectedMinZ = Mathf.Min(protectedMinZ, lockerRoomBounds.min.z - lockerRoomClearance);
            protectedMaxZ = Mathf.Max(protectedMaxZ, lockerRoomBounds.max.z + lockerRoomClearance);
            lockerRoomIncluded = true;
        }

        Bounds protectedBounds = new Bounds(
            new Vector3(
                (protectedMinX + protectedMaxX) * 0.5f,
                floorY + 2.6f,
                (protectedMinZ + protectedMaxZ) * 0.5f),
            new Vector3(
                protectedMaxX - protectedMinX,
                5.2f,
                protectedMaxZ - protectedMinZ));

        IsBuilt = true;
        ProtectedBounds = protectedBounds;
        ExpectedPlacements = 0;
        ReadyPlacements = 0;
        LoadFailures = 0;
        HorizontalOverlaps = 0;

        GameObject root = new GameObject(RootName);
        root.transform.SetParent(parent, true);
        root.transform.position = new Vector3(0f, 0f, 0f);
        CreateCityGroundRing(root.transform, protectedBounds, floorY);
        CityDystopiaBuildTracker tracker = new CityDystopiaBuildTracker(
            protectedBounds, floorY);

        Vector3 center = protectedBounds.center;
        PlaceCity(
            root.transform,
            tracker,
            "City Dystopia North",
            new Vector3(
                center.x,
                floorY,
                protectedMaxZ + SourceNearDepth * SideScale + PerimeterGap),
            Quaternion.Euler(0f, 180f, 0f),
            SideScale,
            "north");
        PlaceCity(
            root.transform,
            tracker,
            "City Dystopia South",
            new Vector3(
                center.x,
                floorY,
                protectedMinZ - SourceNearDepth * SideScale - PerimeterGap),
            Quaternion.identity,
            SideScale,
            "south");
        PlaceCity(
            root.transform,
            tracker,
            "City Dystopia East",
            new Vector3(
                protectedMaxX + SourceFarDepth * SideScale + PerimeterGap,
                floorY,
                center.z),
            Quaternion.Euler(0f, 90f, 0f),
            SideScale,
            "east");
        PlaceCity(
            root.transform,
            tracker,
            "City Dystopia West",
            new Vector3(
                protectedMinX - SourceFarDepth * SideScale - PerimeterGap,
                floorY,
                center.z),
            Quaternion.Euler(0f, -90f, 0f),
            SideScale,
            "west");

        PlaceCorner(root.transform, tracker, "SW", protectedMinX, protectedMinZ,
            floorY, -1f, -1f, SourceNearWidth, SourceNearDepth, Quaternion.identity);
        PlaceCorner(root.transform, tracker, "SE", protectedMaxX, protectedMinZ,
            floorY, 1f, -1f, SourceFarDepth, SourceFarWidth, Quaternion.Euler(0f, 90f, 0f));
        PlaceCorner(root.transform, tracker, "NW", protectedMinX, protectedMaxZ,
            floorY, -1f, 1f, SourceFarDepth, SourceFarWidth, Quaternion.Euler(0f, -90f, 0f));
        PlaceCorner(root.transform, tracker, "NE", protectedMaxX, protectedMaxZ,
            floorY, 1f, 1f, SourceNearWidth, SourceNearDepth, Quaternion.Euler(0f, 180f, 0f));

        ExpectedPlacements = tracker.Expected;

        Debug.Log(


            $"GYMCHAOS_CITY_DYSTOPIA_RING_REQUESTED placements={tracker.Expected} " +
            $"protectedMin=({protectedMinX:F2},{protectedMinZ:F2}) " +
            $"protectedMax=({protectedMaxX:F2},{protectedMaxZ:F2}) " +
            $"lockerRoomIncluded={(lockerRoomIncluded ? 1 : 0)} " +
            $"gap={PerimeterGap:F2} source={AssetPath}",
            root);
    }

    private static void PlaceCorner(
        Transform parent,
        CityDystopiaBuildTracker tracker,
        string suffix,
        float protectedX,
        float protectedZ,
        float floorY,
        float xSign,
        float zSign,
        float xExtent,
        float zExtent,
        Quaternion rotation)
    {
        PlaceCity(
            parent,
            tracker,
            "City Dystopia Corner " + suffix,
            new Vector3(
                protectedX + xSign * (xExtent * CornerScale + PerimeterGap),
                floorY,
                protectedZ + zSign * (zExtent * CornerScale + PerimeterGap)),
            rotation,
            CornerScale,
            "corner_" + suffix.ToLowerInvariant());
    }

    private static void CreateCityGroundRing(
        Transform parent,
        Bounds protectedBounds,
        float floorY)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit") ??
            Shader.Find("Standard");
        if (shader == null)
        {
            return;
        }

        Material material = new Material(shader)
        {
            name = "City Dystopia Ground Infill",
            color = new Color(0.012f, 0.031f, 0.036f, 1f)
        };
        if (material.HasProperty("_BaseColor"))
        {
            material.SetColor("_BaseColor", material.color);
        }
        if (material.HasProperty("_Metallic"))
        {
            material.SetFloat("_Metallic", 0.18f);
        }
        if (material.HasProperty("_Smoothness"))
        {
            material.SetFloat("_Smoothness", 0.52f);
        }

        float y = floorY - 0.08f;
        float depth = CityGroundDepth;
        float outerWidth = protectedBounds.size.x + depth * 2f;
        float outerDepth = protectedBounds.size.z + depth * 2f;
        CreateGroundBox(
            "City Dystopia Ground North",
            parent,
            new Vector3(
                protectedBounds.center.x,
                y,
                protectedBounds.max.z + CityGroundGap + depth * 0.5f),
            new Vector3(outerWidth, 0.16f, depth),
            material);
        CreateGroundBox(
            "City Dystopia Ground South",
            parent,
            new Vector3(
                protectedBounds.center.x,
                y,
                protectedBounds.min.z - CityGroundGap - depth * 0.5f),
            new Vector3(outerWidth, 0.16f, depth),
            material);
        CreateGroundBox(
            "City Dystopia Ground East",
            parent,
            new Vector3(
                protectedBounds.max.x + CityGroundGap + depth * 0.5f,
                y,
                protectedBounds.center.z),
            new Vector3(depth, 0.16f, outerDepth),
            material);
        CreateGroundBox(
            "City Dystopia Ground West",
            parent,
            new Vector3(
                protectedBounds.min.x - CityGroundGap - depth * 0.5f,
                y,
                protectedBounds.center.z),
            new Vector3(depth, 0.16f, outerDepth),
            material);
    }

    private static void CreateGroundBox(
        string name,
        Transform parent,
        Vector3 position,
        Vector3 size,
        Material material)
    {
        GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
        ground.name = name;
        ground.transform.SetParent(parent, true);
        ground.transform.position = position;
        ground.transform.localScale = size;
        Renderer renderer = ground.GetComponent<Renderer>();
        if (renderer != null)
        {
            renderer.sharedMaterial = material;
        }
        Collider collider = ground.GetComponent<Collider>();
        if (collider != null)
        {
            Object.Destroy(collider);
        }
    }
    private static void PlaceCity(
        Transform parent,
        CityDystopiaBuildTracker tracker,
        string objectName,
        Vector3 position,
        Quaternion rotation,
        float scale,
        string side)
    {
        tracker.Expected++;
        RuntimeGlbSceneLoader.Request(
            AssetPath,
            parent,
            position,
            rotation,
            Vector3.one * scale,
            objectName,
            0,
            settleOnSupport: true,
            supportY: tracker.FloorY,
            onLoaded: loaded => tracker.Record(objectName, side, loaded));
    }

    private sealed class CityDystopiaBuildTracker
    {
        private readonly Bounds protectedBounds;
        public readonly float FloorY;
        public int Expected { get; set; }
        private int ready;
        private int failed;
        private int horizontalOverlaps;

        public CityDystopiaBuildTracker(Bounds protectedBounds, float floorY)
        {
            this.protectedBounds = protectedBounds;
            FloorY = floorY;
        }

        public void Record(string objectName, string side, GameObject loaded)
        {
            if (loaded == null)
            {
                failed++;
                ready++;
                ReadyPlacements = ready;
                LoadFailures = failed;
                HorizontalOverlaps = horizontalOverlaps;
                Debug.LogError(
                    $"GYMCHAOS_CITY_DYSTOPIA_LOAD_FAIL object={objectName} " +
                    $"side={side} path={AssetPath}");
                return;
            }

            Bounds cityBounds = CalculateRendererBounds(loaded);
            bool horizontalOverlap =
                cityBounds.min.x < protectedBounds.max.x &&
                cityBounds.max.x > protectedBounds.min.x &&
                cityBounds.min.z < protectedBounds.max.z &&
                cityBounds.max.z > protectedBounds.min.z;
            if (horizontalOverlap)
            {
                horizontalOverlaps++;
            }

            ready++;
            ReadyPlacements = ready;
            LoadFailures = failed;
            HorizontalOverlaps = horizontalOverlaps;
            Debug.Log(
                $"GYMCHAOS_CITY_DYSTOPIA_PLACED object={objectName} side={side} " +
                $"bounds={cityBounds} horizontalOverlap={(horizontalOverlap ? 1 : 0)} " +
                $"renderers={loaded.GetComponentsInChildren<MeshRenderer>(true).Length}",
                loaded);

            if (ready >= Expected)
            {
                Debug.Log(
                    $"GYMCHAOS_CITY_DYSTOPIA_CONTRACT_" +
                    $"{(failed == 0 && horizontalOverlaps == 0 ? "OK" : "FAIL")} " +
                    $"expected={Expected} ready={ready} failed={failed} " +
                    $"horizontalOverlaps={horizontalOverlaps} sides=4 corners=4",
                    loaded);
            }
        }

        private static Bounds CalculateRendererBounds(GameObject target)
        {
            MeshRenderer[] renderers =
                target.GetComponentsInChildren<MeshRenderer>(true);
            if (renderers.Length == 0)
            {
                return new Bounds(target.transform.position, Vector3.zero);
            }

            Bounds bounds = renderers[0].bounds;
            for (int index = 1; index < renderers.Length; index++)
            {
                bounds.Encapsulate(renderers[index].bounds);
            }

            return bounds;
        }
    }
}




