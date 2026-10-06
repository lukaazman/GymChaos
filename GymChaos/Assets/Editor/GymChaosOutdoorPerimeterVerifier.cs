#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class GymChaosOutdoorPerimeterVerifier
{
    private const string RequestedKey =
        "GymChaos.OutdoorPerimeterVerificationRequested";
    private static double started;
    private static bool finished;
    private static int resultCode;

    private struct SurfaceRegion
    {
        public string Name;
        public Bounds Bounds;
    }

    static GymChaosOutdoorPerimeterVerifier()
    {
        if (SessionState.GetBool(RequestedKey, false)) Hook();
    }

    [MenuItem("Tools/GymChaos/Run Outdoor Perimeter Verification")]
    public static void Run()
    {
        SessionState.SetBool(RequestedKey, true);
        started = EditorApplication.timeSinceStartup;
        finished = false;
        resultCode = 1; GymChaosVerifierExit.Record(resultCode);
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        Hook();
        EditorApplication.isPlaying = true;
    }

    private static void Hook()
    {
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
        EditorApplication.playModeStateChanged -= PlayModeChanged;
        EditorApplication.playModeStateChanged += PlayModeChanged;
    }

    private static void PlayModeChanged(PlayModeStateChange change)
    {
        if (change == PlayModeStateChange.EnteredPlayMode)
        {
            started = EditorApplication.timeSinceStartup;
            Time.timeScale = 1f;
        }
        if (change != PlayModeStateChange.EnteredEditMode) return;
        EditorApplication.update -= Tick;
        EditorApplication.playModeStateChanged -= PlayModeChanged;
        SessionState.EraseBool(RequestedKey);
        if (Application.isBatchMode) GymChaosVerifierExit.Exit(resultCode);
    }

    private static void Tick()
    {
        if (finished || !EditorApplication.isPlaying) return;
        try
        {
            double elapsed = EditorApplication.timeSinceStartup - started;
            if (!GymOutdoorBuilder.IsBuilt ||
                !GymProteinStoreEnvironment.IsLoaded ||
                GymOutdoorFoliage.LoadedPlants + GymOutdoorFoliage.FailedPlants <
                    GymOutdoorFoliage.RequestedPlants)
            {
                if (elapsed > 45d)
                    throw new TimeoutException("Outdoor builders did not settle.");
                return;
            }

            Physics.SyncTransforms();
            GameObject root = GameObject.Find("Gym Exterior (Runtime)");
            if (root == null)
                throw new InvalidOperationException("Outdoor runtime root is missing.");

            int overlaps = CountCoplanarSurfaceOverlaps(root, out string overlapDetails);
            bool fence = GymProteinStoreEnvironment.HasConnectedEntryFenceContract(
                out string fenceDetails);
            bool boundaries = HasRequiredBoundaryColliders(root.transform,
                out string boundaryDetails);
            float seamGap = MeasureParkingRoadSeam(root.transform);
            int busPocketHoles = CountBusPocketGroundHoles(out string holeDetails);
            int roadHoles = CountRoadGroundHoles(out string roadHoleDetails);
            busPocketHoles += roadHoles;
            holeDetails += " road:" + roadHoleDetails;
            float storeWallGap = MeasureStoreRoadWallGap(root.transform);
            bool pocketWalls = HasBusPocketWalls(root.transform, out string pocketWallDetails);
            if (busPocketHoles != 0 || storeWallGap > 0.01f || !pocketWalls)
            {
                throw new InvalidOperationException(
                    $"Outdoor gap contract failed: busPocketHoles={busPocketHoles} " +
                    $"holes={holeDetails} storeWallGap={storeWallGap:F3} " +
                    $"pocketWalls={pocketWalls} {pocketWallDetails}.");
            }
            Debug.Log(
                $"GYMCHAOS_OUTDOOR_GAPS_OK busPocketHoles=0 storeWallGap={storeWallGap:F3} " +
                $"{pocketWallDetails}", root);
            if (overlaps != 0 || !fence || !boundaries || seamGap > 0.012f)
            {
                throw new InvalidOperationException(
                    $"Outdoor perimeter contract failed: overlaps={overlaps} " +
                    $"overlapDetails={overlapDetails} seamGap={seamGap:F3} " +
                    $"fence={fence} fenceDetails={fenceDetails} " +
                    $"boundaries={boundaries} boundaryDetails={boundaryDetails}.");
            }

            if (!HasSeamlessGround(root.transform, out string groundDetails))
            {
                throw new InvalidOperationException(
                    $"Outdoor seamless ground contract failed: {groundDetails}.");
            }
            Debug.Log($"GYMCHAOS_OUTDOOR_SEAMLESS_GROUND_OK {groundDetails}", root);

            if (!HasEqualTrafficLanes(root.transform, out string laneDetails))
            {
                throw new InvalidOperationException(
                    $"Traffic lane width contract failed: {laneDetails}.");
            }
            Debug.Log($"GYMCHAOS_OUTDOOR_EQUAL_LANES_OK {laneDetails}", root);

            if (!HasPlantedSurroundings(root.transform, out string foliageDetails))
            {
                throw new InvalidOperationException(
                    $"Outdoor foliage contract failed: {foliageDetails}.");
            }
            Debug.Log($"GYMCHAOS_OUTDOOR_FOLIAGE_CONTRACT_OK {foliageDetails}", root);

            Debug.Log(
                $"GYMCHAOS_OUTDOOR_PERIMETER_OK overlaps={overlaps} " +
                $"seamGap={seamGap:F3} fence={fence} boundaries={boundaries} " +
                $"{fenceDetails} {boundaryDetails}", root);
            Finish(0);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            Finish(1);
        }
    }

    // Every walkable exterior surface must render as one slab: one shared
    // material, the same top height, no visible vertical side strips, and no
    // raised parking curbs splitting the lot from the courtyard.
    private static bool HasSeamlessGround(Transform root, out string details)
    {
        string[] names =
        {
            "Exterior Courtyard Foundation",
            "Mini Parking Lot",
            "Path from Gym Door",
            "Visitor Vehicle Road",
            "Protein Store Entry Walkway",
            "Protein Store Platform",
            "Protein Store Route South",
            "Protein Store Route North",
            "Protein Store Route East",
            "Protein Store Route West South",
            "Protein Store Route West North"
        };
        Material shared = null;
        float topMin = float.PositiveInfinity;
        float topMax = float.NegativeInfinity;
        int sideTriangles = 0;
        for (int i = 0; i < names.Length; i++)
        {
            Transform node = FindRecursive(root, names[i]);
            MeshRenderer renderer = node != null ? node.GetComponent<MeshRenderer>() : null;
            MeshFilter filter = node != null ? node.GetComponent<MeshFilter>() : null;
            if (renderer == null || filter == null || filter.sharedMesh == null)
            {
                details = $"missing={names[i]}";
                return false;
            }
            foreach (Material material in renderer.sharedMaterials)
            {
                if (shared == null) shared = material;
                if (material != shared)
                {
                    details = $"materialMismatch={names[i]}:{(material != null ? material.name : "null")}";
                    return false;
                }
            }
            Mesh mesh = filter.sharedMesh;
            Vector3[] vertices = mesh.vertices;
            int[] triangles = mesh.triangles;
            for (int t = 0; t + 2 < triangles.Length; t += 3)
            {
                Vector3 a = node.TransformPoint(vertices[triangles[t]]);
                Vector3 b = node.TransformPoint(vertices[triangles[t + 1]]);
                Vector3 c = node.TransformPoint(vertices[triangles[t + 2]]);
                Vector3 normal = Vector3.Cross(b - a, c - a).normalized;
                if (Mathf.Abs(normal.y) < 0.5f) sideTriangles++;
            }
            // The foundation intentionally sits a hair below the named
            // surfaces to avoid z-fighting; it is not part of the top band.
            if (i > 0)
            {
                topMin = Mathf.Min(topMin, renderer.bounds.max.y);
                topMax = Mathf.Max(topMax, renderer.bounds.max.y);
            }
        }
        bool curbs = FindRecursive(root, "Parking North Curb") != null ||
            FindRecursive(root, "Parking West Curb") != null ||
            FindRecursive(root, "Parking South Curb") != null;
        float topSpread = topMax - topMin;
        details = $"surfaces={names.Length} material={(shared != null ? shared.name : "null")} " +
            $"topSpread={topSpread:F4} sideTriangles={sideTriangles} curbs={curbs} " +
            $"unified={GymExteriorGroundUnifier.UnifiedSurfaceCount}";
        return shared != null && topSpread <= 0.002f && sideTriangles == 0 && !curbs;
    }

    // The visible carriageway runs from the road's north edge (the bus bay is
    // a separate pull-off) to the south wall face. The centre line and both
    // traffic lanes must split that width evenly.
    private static bool HasEqualTrafficLanes(Transform root, out string details)
    {
        Transform line = FindRecursive(root, "Road Center Line");
        Transform southWall = FindRecursive(root, "Visitor Road South Wall");
        Renderer wallRenderer = southWall != null ? southWall.GetComponent<Renderer>() : null;
        if (line == null || wallRenderer == null)
        {
            details = $"line={line != null} southWall={wallRenderer != null}";
            return false;
        }
        float northEdge = GymOutdoorBuilder.ParkingBounds.center.z +
            GymOutdoorBuilder.VehicleRoadWidthForVerification * 0.5f;
        float southFace = wallRenderer.bounds.max.z;
        float centre = GymOutdoorBuilder.TrafficLaneCenterZ;
        float northLane = northEdge - centre;
        float southLane = centre - southFace;
        float arrivalFromCentre = GymOutdoorBuilder.VehicleArrivalRoadTurnPoint.z - centre;
        float departureFromCentre = centre - GymOutdoorBuilder.VehicleDepartureRoadTurnPoint.z;
        details = $"northLane={northLane:F2} southLane={southLane:F2} " +
            $"lineZ={line.position.z:F2} centre={centre:F2} " +
            $"arrival={arrivalFromCentre:F2} departure={departureFromCentre:F2}";
        return Mathf.Abs(northLane - southLane) <= 0.35f &&
            Mathf.Abs(line.position.z - centre) <= 0.01f &&
            Mathf.Abs(arrivalFromCentre - departureFromCentre) <= 0.01f &&
            Mathf.Abs(arrivalFromCentre - northLane * 0.5f) <= 0.35f;
    }

    // Plants fill only unbuilt land inside the city ring: every plant is on
    // the planting ground (no visible built surface below), within the ring
    // envelope, collider-free, textured and non-metallic, with mixed species.
    private static bool HasPlantedSurroundings(Transform root, out string details)
    {
        Transform foliage = FindRecursive(root, GymOutdoorFoliage.RootName);
        Transform ground = FindRecursive(root, GymOutdoorFoliage.GroundName);
        bool fakePlanter = FindRecursive(root, "Park Landscape Planter") != null;
        bool fakeShrub = FindRecursive(root, "Park Landscape Shrub") != null;
        if (foliage == null || ground == null || fakePlanter || fakeShrub)
        {
            details = $"foliage={foliage != null} ground={ground != null} " +
                $"fakePlanter={fakePlanter} fakeShrub={fakeShrub}";
            return false;
        }

        Bounds envelope = GymOutdoorFoliage.PlantingBounds;
        HashSet<string> species = new HashSet<string>();
        int onBuilt = 0;
        int outside = 0;
        int colliders = 0;
        int untextured = 0;
        int metallic = 0;
        for (int i = 0; i < foliage.childCount; i++)
        {
            Transform plant = foliage.GetChild(i);
            if (plant.name == GymOutdoorFoliage.GroundName)
            {
                continue;
            }
            species.Add(plant.name);
            Vector3 p = plant.position;
            if (p.x < envelope.min.x || p.x > envelope.max.x ||
                p.z < envelope.min.z || p.z > envelope.max.z)
            {
                outside++;
            }
            foreach (RaycastHit hit in Physics.RaycastAll(
                new Vector3(p.x, p.y + 40f, p.z), Vector3.down, 42f,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                Renderer below = hit.collider.GetComponent<Renderer>();
                if (below != null && below.enabled &&
                    !hit.collider.name.StartsWith("Plane", StringComparison.OrdinalIgnoreCase))
                {
                    onBuilt++;
                    break;
                }
            }
            colliders += plant.GetComponentsInChildren<Collider>(true).Length;
            foreach (MeshRenderer renderer in plant.GetComponentsInChildren<MeshRenderer>(true))
            {
                Material material = renderer.sharedMaterial;
                if (material == null || material.mainTexture == null) untextured++;
                else if (material.HasProperty("_Metallic") && material.GetFloat("_Metallic") > 0.05f) metallic++;
            }
        }
        details = $"plants={GymOutdoorFoliage.RequestedPlants} loaded={GymOutdoorFoliage.LoadedPlants} " +
            $"failed={GymOutdoorFoliage.FailedPlants} trees={GymOutdoorFoliage.TreeCount} " +
            $"bushes={GymOutdoorFoliage.BushCount} species={species.Count} onBuilt={onBuilt} " +
            $"outside={outside} colliders={colliders} untextured={untextured} metallic={metallic}";
        return GymOutdoorFoliage.RequestedPlants >= 60 &&
            GymOutdoorFoliage.FailedPlants == 0 &&
            GymOutdoorFoliage.TreeCount >= 15 &&
            GymOutdoorFoliage.BushCount >= 30 &&
            species.Count >= 6 &&
            onBuilt == 0 && outside == 0 && colliders == 0 &&
            untextured == 0 && metallic == 0;
    }

    private static int CountCoplanarSurfaceOverlaps(
        GameObject root, out string details)
    {
        string[] names =
        {
            "Mini Parking Lot",
            "Path from Gym Door",
            "Visitor Vehicle Road",
            "Protein Store Entry Walkway",
            "Protein Store Platform",
            "Protein Store Route South",
            "Protein Store Route North",
            "Protein Store Route East",
            "Protein Store Route West South",
            "Protein Store Route West North"
        };
        List<SurfaceRegion> regions = new List<SurfaceRegion>();
        for (int i = 0; i < names.Length; i++)
        {
            Transform node = FindRecursive(root.transform, names[i]);
            Renderer renderer = node != null ? node.GetComponent<Renderer>() : null;
            if (renderer == null || !renderer.enabled)
                throw new InvalidOperationException($"Surface missing: {names[i]}.");
            AppendRegions(renderer, names[i], regions);
        }

        int overlaps = 0;
        details = string.Empty;
        for (int a = 0; a < regions.Count; a++)
        {
            for (int b = a + 1; b < regions.Count; b++)
            {
                Bounds first = regions[a].Bounds;
                Bounds second = regions[b].Bounds;
                float overlapX = Mathf.Min(first.max.x, second.max.x) -
                    Mathf.Max(first.min.x, second.min.x);
                float overlapZ = Mathf.Min(first.max.z, second.max.z) -
                    Mathf.Max(first.min.z, second.min.z);
                float topDelta = Mathf.Abs(first.max.y - second.max.y);
                if (overlapX <= 0.012f || overlapZ <= 0.012f || topDelta > 0.003f)
                    continue;
                overlaps++;
                if (overlaps <= 8)
                    details += $"[{regions[a].Name}/{regions[b].Name} " +
                        $"x={overlapX:F2} z={overlapZ:F2}]";
            }
        }
        return overlaps;
    }

    private static void AppendRegions(
        Renderer renderer, string name, List<SurfaceRegion> regions)
    {
        MeshFilter filter = renderer.GetComponent<MeshFilter>();
        Mesh mesh = filter != null ? filter.sharedMesh : null;
        if (mesh == null || !mesh.isReadable || mesh.subMeshCount <= 1)
        {
            regions.Add(new SurfaceRegion { Name = name, Bounds = renderer.bounds });
            return;
        }
        Vector3[] vertices = mesh.vertices;
        for (int sub = 0; sub < mesh.subMeshCount; sub++)
        {
            int[] indices = mesh.GetIndices(sub);
            if (indices.Length == 0) continue;
            Bounds bounds = new Bounds(
                renderer.transform.TransformPoint(vertices[indices[0]]),
                Vector3.zero);
            for (int i = 1; i < indices.Length; i++)
                bounds.Encapsulate(renderer.transform.TransformPoint(vertices[indices[i]]));
            regions.Add(new SurfaceRegion { Name = name, Bounds = bounds });
        }
    }

    private static bool HasRequiredBoundaryColliders(
        Transform root, out string details)
    {
        string[] names =
        {
            "Outdoor Boundary - Parking West",
            "Outdoor Boundary - Parking East South",
            "Outdoor Boundary - Parking East North",
            "Outdoor Boundary - Parking North Extension",
            "Outdoor Boundary - Path Outer South",
            "Outdoor Boundary - Path Outer Middle",
            "Outdoor Boundary - Path Outer North",
            "Outdoor Boundary - Path South",
            "Visitor Road Corner West Wall Collision",
            "Visitor Road Corner East Wall Collision",
            "Protein Store Perimeter South Collision",
            "Protein Store Perimeter East Collision",
            "Protein Store Perimeter West Return Collision"
        };
        int found = 0;
        for (int i = 0; i < names.Length; i++)
        {
            Transform node = FindRecursive(root, names[i]);
            if (node != null && node.GetComponent<Collider>() != null) found++;
        }
        details = $"colliders={found}/{names.Length}";
        return found == names.Length;
    }

    private static float MeasureParkingRoadSeam(Transform root)
    {
        Renderer parking = FindRecursive(root, "Mini Parking Lot")?.GetComponent<Renderer>();
        Renderer road = FindRecursive(root, "Visitor Vehicle Road")?.GetComponent<Renderer>();
        if (parking == null || road == null) return float.PositiveInfinity;
        List<SurfaceRegion> regions = new List<SurfaceRegion>();
        AppendRegions(road, road.name, regions);
        float seam = float.PositiveInfinity;
        foreach (SurfaceRegion region in regions)
        {
            Bounds bounds = region.Bounds;
            if (bounds.min.z > parking.bounds.center.z ||
                bounds.max.z < parking.bounds.center.z) continue;
            float gap = Mathf.Abs(bounds.min.x - parking.bounds.max.x);
            float height = Mathf.Abs(bounds.max.y - parking.bounds.max.y);
            seam = Mathf.Min(seam, Mathf.Max(gap, height));
        }
        return seam;
    }

    // Downward rays over the whole bus pull-off row (road wall line to the
    // bay's outer fence, parking courtyard edge to the road corner wall).
    private static int CountBusPocketGroundHoles(out string details)
    {
        details = string.Empty;
        float floorY = GymDoorway.Instance != null ? GymDoorway.Instance.ExteriorPoint.y : -0.15f;
        float minX = GymRoadsideBusStop.BusBayStartX - 6.0f;
        float maxX = GymRoadsideBusStop.BusBayEndX + 2.4f;
        float minZ = GymRoadsideBusStop.BusBayRoadEdgeZ + 0.3f;
        float maxZ = GymRoadsideBusStop.BusBayOuterZ - 0.3f;
        int holes = 0;
        for (float x = minX; x <= maxX; x += 0.5f)
        {
            for (float z = minZ; z <= maxZ; z += 0.5f)
            {
                Vector3 origin = new Vector3(x, floorY + 3f, z);
                RaycastHit[] hits = Physics.RaycastAll(origin, Vector3.down, 6f,
                    Physics.AllLayers, QueryTriggerInteraction.Ignore);
                bool ground = false;
                foreach (RaycastHit hit in hits)
                {
                    if (Mathf.Abs(hit.point.y - floorY) <= 0.05f &&
                        hit.collider.GetComponentInParent<EnemyFighter>() == null)
                    {
                        ground = true;
                        break;
                    }
                }
                if (ground) continue;
                holes++;
                if (holes <= 6) details += $"({x:F1},{z:F1})";
            }
        }
        return holes;
    }

    // Ground under the whole walled road: the east-west leg between the
    // road walls and the north-south arrival leg up to the spawn.
    private static int CountRoadGroundHoles(out string details)
    {
        details = string.Empty;
        Vector3 turn = GymOutdoorBuilder.VehicleRoadTurnPoint;
        float halfWidth = GymOutdoorBuilder.VehicleRoadWidthForVerification * 0.5f;
        Rect[] regions =
        {
            // x, z, width, depth
            new Rect(171.8f, 20.6f, turn.x + halfWidth - 0.4f - 171.8f,
                GymRoadsideBusStop.BusBayRoadEdgeZ - 0.3f - 20.6f),
            new Rect(turn.x - halfWidth + 0.4f, 20.6f, halfWidth * 2f - 0.8f,
                GymOutdoorBuilder.VehicleArrivalRoadSpawnPoint.z - 20.6f)
        };
        int holes = 0;
        int regionStart = 0;
        Vector2 holeMin = new Vector2(float.MaxValue, float.MaxValue);
        Vector2 holeMax = new Vector2(float.MinValue, float.MinValue);
        foreach (Rect region in regions)
        {
            for (float x = region.xMin; x <= region.xMax; x += 0.5f)
            {
                for (float z = region.yMin; z <= region.yMax; z += 0.5f)
                {
                    RaycastHit[] hits = Physics.RaycastAll(new Vector3(x, 4f, z), Vector3.down, 6f,
                        Physics.AllLayers, QueryTriggerInteraction.Ignore);
                    bool ground = false;
                    foreach (RaycastHit hit in hits)
                    {
                        if (hit.normal.y > 0.9f && hit.point.y > -0.6f && hit.point.y < 0.2f &&
                            hit.collider.GetComponentInParent<EnemyFighter>() == null)
                        {
                            ground = true;
                            break;
                        }
                    }
                    if (ground) continue;
                    holes++;
                    holeMin = Vector2.Min(holeMin, new Vector2(x, z));
                    holeMax = Vector2.Max(holeMax, new Vector2(x, z));
                }
            }
            if (holes > regionStart)
                details += $"[region{System.Array.IndexOf(regions, region)} n={holes - regionStart} " +
                    $"x={holeMin.x:F1}..{holeMax.x:F1} z={holeMin.y:F1}..{holeMax.y:F1}]";
            regionStart = holes;
            holeMin = new Vector2(float.MaxValue, float.MaxValue);
            holeMax = new Vector2(float.MinValue, float.MinValue);
        }
        return holes;
    }

    // The road's south wall is one continuous run from the gym path to the
    // road corner (the old store gates are closed). Report the largest
    // uncovered stretch between the path's outer wall and the corner wall.
    private static float MeasureStoreRoadWallGap(Transform root)
    {
        Collider wall = FindRecursive(root, "Visitor Road South Wall Collision")?.GetComponent<Collider>();
        Collider path = FindRecursive(root, "Outdoor Boundary - Path Outer Middle")?.GetComponent<Collider>();
        Collider corner = FindRecursive(root, "Visitor Road Corner East Wall Collision")?.GetComponent<Collider>();
        if (wall == null || path == null || corner == null) return float.PositiveInfinity;
        float westGap = Mathf.Max(0f, wall.bounds.min.x - path.bounds.max.x);
        float eastGap = Mathf.Max(0f, corner.bounds.min.x - wall.bounds.max.x);
        return Mathf.Max(westGap, eastGap);
    }

    private static bool HasBusPocketWalls(Transform root, out string details)
    {
        Collider west = FindRecursive(root, "Bus Bay West Pocket North Wall Collision")?.GetComponent<Collider>();
        Collider east = FindRecursive(root, "Bus Bay East Pocket North Wall Collision")?.GetComponent<Collider>();
        Collider parkingNorth = FindRecursive(root, "Outdoor Boundary - Parking North Extension")?.GetComponent<Collider>();
        Collider corner = FindRecursive(root, "Visitor Road Corner West Wall Collision")?.GetComponent<Collider>();
        bool ok = west != null && east != null && parkingNorth != null && corner != null &&
            west.bounds.min.x <= parkingNorth.bounds.max.x + 0.01f &&
            west.bounds.max.x >= GymRoadsideBusStop.BusBayStartX &&
            east.bounds.min.x <= GymRoadsideBusStop.BusBayEndX &&
            east.bounds.max.x >= corner.bounds.min.x - 0.01f;
        details = $"westWall={(west != null ? west.bounds.min.x.ToString("F2") + ".." + west.bounds.max.x.ToString("F2") : "missing")} " +
            $"eastWall={(east != null ? east.bounds.min.x.ToString("F2") + ".." + east.bounds.max.x.ToString("F2") : "missing")}";
        return ok;
    }

    private static Transform FindRecursive(Transform root, string name)
    {
        if (root == null) return null;
        Transform[] nodes = root.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < nodes.Length; i++)
            if (nodes[i].name == name) return nodes[i];
        return null;
    }

    private static void Finish(int code)
    {
        finished = true;
        resultCode = code; GymChaosVerifierExit.Record(resultCode);
        SessionState.EraseBool(RequestedKey);
        EditorApplication.update -= Tick;
        EditorApplication.isPlaying = false;
    }
}
#endif
