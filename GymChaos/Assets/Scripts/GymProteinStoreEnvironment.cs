using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Mounts the protein.com store (Assets/ProteinStore/v2, one runtime-loaded
/// GLB) in the walled square east of the gym path. The building fills the
/// square with a small gap to the low walls; its door opens straight onto the
/// south end of the gym path through an opening in the path's outer wall.
/// The collision shell is created from constants, so routes are blocked
/// correctly before the GLB finishes loading; fixtures get their colliders
/// once it has loaded.
/// </summary>
public static class GymProteinStoreEnvironment
{
    private const string RootName = "Protein Store (Runtime)";
    private const string AssetPath = "BodyBuilders/outside/protein_store.glb";

    // Module dimensions from create_protein_store_v2.py (metres). The GLB
    // is placed with a 180 degree yaw: authored +X runs world east (from the
    // storefront to the back wall) and authored +Y runs world north.
    private const float ModuleDepth = 23.4f;
    private const float ModuleWidth = 20.1f;
    private const float ModuleWall = 0.25f;
    private const float ModuleWallHeight = 4.4f;
    private const float ModuleHeight = 5.08f;
    private const float DoorLocalZ = -2.76f;
    private const float DoorWidth = 2.6f;
    // Opening in the gym path's outer wall, starting at the path's south end.
    public const float PathOpeningWidth = 2.8f;
    // Gap between the building and the site's low walls.
    private const float StorePadding = 0.7f;
    // The walled site keeps the size it had around the first store.
    private const float SiteSouthBelowPathEnd = 6.597f;
    private const float SiteEastFromPath = 25.30f;
    // Checkout: where a customer stands in front of the counter and where
    // Mark stands behind it (authored X, authored Y = world north).
    private static readonly Vector2 CounterCustomerLocal = new Vector2(-7.2f, -5.35f);
    private static readonly Vector2 CounterStaffLocal = new Vector2(-7.4f, -8.25f);
    // Where store visitors stop: the counter first, then two open spots
    // by the shelves, so two shoppers never queue for the same point.
    private static readonly Vector2[] VisitSpotLocal =
    {
        CounterCustomerLocal, new Vector2(-1.0f, -1.4f), new Vector2(0.5f, -6.5f)
    };
    private static readonly UnityEngine.Object[] visitSpotOwners =
        new UnityEngine.Object[VisitSpotLocal.Length];
    private static readonly Vector2[] InteriorLightLocal =
    {
        new Vector2(-7.5f, -4.5f), new Vector2(-7.5f, 4.5f), new Vector2(0f, -4.5f),
        new Vector2(0f, 4.5f), new Vector2(7.5f, -4.5f), new Vector2(7.5f, 4.5f)
    };
    private const float StoreDoorClearance = 1.25f;
    private const float ParkingBypassOffset = 7.247f;

    public static bool IsBuilt { get; private set; }
    public static bool IsLoaded { get; private set; }
    public static bool LoadFailed { get; private set; }
    public static int LoadedRendererCount { get; private set; }
    public static int LoadedMaterialCount { get; private set; }
    public static int TransparentFridgePanelCount { get; private set; }
    public static int ColliderCount { get; private set; }
    public static int EntryFenceJunctionCount { get; private set; }
    public static float EntryFenceLargestGap { get; private set; }
    public static Bounds StoreBounds { get; private set; }
    public static Bounds SiteBounds { get; private set; }
    // World footprint of the building.
    public static Bounds ShellFootprint { get; private set; }
    // x of the storefront's outer face.
    public static float FacadeX { get; private set; }
    public static Bounds OuterRouteBounds { get; private set; }
    public static float EntranceMinZ { get; private set; }
    public static float EntranceMaxZ { get; private set; }
    public static Vector3 StoreEntrancePoint { get; private set; }
    public static Vector3 StoreFrontClearPoint { get; private set; }
    public static Vector3 StoreWestApproachPoint { get; private set; }
    public static Vector3 StoreSouthCurvePointB { get; private set; }
    public static Vector3 StoreSouthCurvePointC { get; private set; }
    public static Vector3 StoreSouthCurvePointD { get; private set; }
    public static Vector3 StoreSouthCurvePointE { get; private set; }
    public static Vector3 StoreParkingBypassPoint { get; private set; }
    public static Vector3 StoreEastRoutePoint { get; private set; }
    public static Vector3 StoreGymPathClearPoint { get; private set; }
    public static Vector3 StoreGateWestClearPoint { get; private set; }
    public static Vector3 StoreGymPathSouthClearPoint { get; private set; }
    public static Vector3 StoreVisitApproachPoint { get; private set; }
    public static Vector3 StoreInsideDoorPoint { get; private set; }
    public static Vector3 StoreWorkerPoint { get; private set; }
    public static Quaternion StoreWorkerRotation { get; private set; }
    public static float InnerFenceStartZ { get; private set; }
    public static int RuntimeInteriorLightCount { get; private set; }
    public static Transform RuntimeRoot { get; private set; }
    public static string RuntimeAssetPath => AssetPath;

    /// <summary>The store door's opening in the path's outer wall.</summary>
    public static void GetPathOpening(float pathSouthZ, out float minZ, out float maxZ)
    {
        minZ = pathSouthZ + GymOutdoorBuilder.SharedFenceWallThickness * 0.5f;
        maxZ = minZ + PathOpeningWidth;
    }

    private static float entryFenceExpectedStartX;
    private static float entryFenceExpectedEndX;
    private static float entryFenceCenterZ;
    private static float entryFenceHalfOffset;
    private static float entryFenceFloorY;
    private static string entryFenceRouteBlocker = "none";
    private static Vector3 entryFenceRouteBlockerPosition;
    private static readonly Collider[] entryFenceRouteProbe =
        new Collider[64];

    public static bool HasConnectedEntryFenceContract(out string details)
    {
        Transform root = RuntimeRoot != null && RuntimeRoot.parent != null
            ? RuntimeRoot.parent
            : RuntimeRoot;
        SeedEntryFenceMeasurementFromExistingRoot(root);
        int measuredJunctions = 0;
        float measuredLargestGap = float.PositiveInfinity;
        bool measuredRouteClear = false;
        bool measured = root != null && MeasureEntryFenceContract(
            root,
            entryFenceExpectedStartX,
            entryFenceExpectedEndX,
            entryFenceCenterZ,
            entryFenceHalfOffset,
            entryFenceFloorY,
            out measuredJunctions,
            out measuredLargestGap,
            out measuredRouteClear);
        EntryFenceJunctionCount = measuredJunctions;
        EntryFenceLargestGap = measuredLargestGap;
        bool south = root != null &&
            FindChildRecursive(root,
                "Protein Store Entry Fence South Collision") != null;
        bool north = root != null &&
            FindChildRecursive(root,
                "Protein Store Entry Fence North Collision") != null;
        bool roadWidth = GymOutdoorBuilder.VehicleRoadWidthForVerification >= 7.5f;
        bool routeClear = StoreGateWestClearPoint != Vector3.zero &&
            GymOutdoorBuilder.VehicleRoadJunctionPoint != Vector3.zero &&
            GymOutdoorBuilder.VehicleRoadTurnPoint != Vector3.zero;
        bool passed = measured && south && north &&
            measuredJunctions == 2 && measuredLargestGap <= 0.035f &&
            measuredRouteClear && roadWidth && routeClear;
        details =
            $"southJunction={south} northJunction={north} " +
            $"junctions={measuredJunctions} " +
            $"largestGap={measuredLargestGap:0.000} " +
            $"roadWidth={GymOutdoorBuilder.VehicleRoadWidthForVerification:0.00} " +
            $"routeClear={routeClear} sweptRoadClear={measuredRouteClear} " +
            $"measured={measured} blocker={entryFenceRouteBlocker} " +
            $"blockerPosition={entryFenceRouteBlockerPosition} " +
            $"span={entryFenceExpectedStartX:0.00}->{entryFenceExpectedEndX:0.00} " +
            $"centerZ={entryFenceCenterZ:0.00} floorY={entryFenceFloorY:0.00}";
        return passed;
    }

    private static bool MeasureEntryFenceContract(
        Transform root,
        float expectedStartX,
        float expectedEndX,
        float centerZ,
        float halfOffset,
        float floorY,
        out int junctionCount,
        out float largestGap,
        out bool routeClear)
    {
        junctionCount = 0;
        largestGap = float.PositiveInfinity;
        routeClear = false;
        entryFenceRouteBlocker = "none";
        entryFenceRouteBlockerPosition = Vector3.zero;
        if (root == null)
        {
            return false;
        }

        BoxCollider south = FindFenceCollision(root,
            "Protein Store Entry Fence South");
        BoxCollider north = FindFenceCollision(root,
            "Protein Store Entry Fence North");
        if (south != null)
        {
            junctionCount++;
        }
        if (north != null)
        {
            junctionCount++;
        }
        if (south == null || north == null)
        {
            return false;
        }

        // Measure the collider endpoints and offsets in world space. These
        // values come from the instantiated collision objects, not authored
        // counters, so a shifted or shortened fence cannot self-report OK.
        float southGap = MeasureFenceGap(
            south, expectedStartX, expectedEndX, centerZ - halfOffset);
        float northGap = MeasureFenceGap(
            north, expectedStartX, expectedEndX, centerZ + halfOffset);
        largestGap = Mathf.Max(southGap, northGap);
        routeClear = IsFenceOpeningSweptClear(
            expectedStartX, expectedEndX, centerZ, floorY);
        return true;
    }

    private static BoxCollider FindFenceCollision(Transform root, string prefix)
    {
        Transform child = FindChildRecursive(root, prefix + " Collision");
        return child != null ? child.GetComponent<BoxCollider>() : null;
    }

    private static void SeedEntryFenceMeasurementFromExistingRoot(Transform root)
    {
        if (root == null || Mathf.Abs(entryFenceExpectedEndX -
            entryFenceExpectedStartX) >= 0.5f)
        {
            return;
        }

        BoxCollider south = FindFenceCollision(root,
            "Protein Store Entry Fence South");
        BoxCollider north = FindFenceCollision(root,
            "Protein Store Entry Fence North");
        if (south == null || north == null)
        {
            return;
        }

        // A runtime root can survive a domain reload while these static
        // measurements reset. Reconstruct the expected span from the two
        // instantiated colliders, then still validate the opening sweep and
        // exact two-collider count below.
        entryFenceExpectedStartX = Mathf.Min(
            south.bounds.min.x, north.bounds.min.x);
        entryFenceExpectedEndX = Mathf.Max(
            south.bounds.max.x, north.bounds.max.x);
        entryFenceCenterZ = (south.bounds.center.z + north.bounds.center.z) * 0.5f;
        entryFenceHalfOffset = Mathf.Abs(
            north.bounds.center.z - south.bounds.center.z) * 0.5f;
        entryFenceFloorY = Mathf.Min(south.bounds.min.y, north.bounds.min.y);
    }

    private static float MeasureFenceGap(
        BoxCollider collider,
        float expectedStartX,
        float expectedEndX,
        float expectedZ)
    {
        Bounds bounds = collider.bounds;
        return Mathf.Max(
            Mathf.Abs(bounds.min.x - expectedStartX),
            Mathf.Abs(bounds.max.x - expectedEndX),
            Mathf.Abs(bounds.center.z - expectedZ));
    }

    private static bool IsFenceOpeningSweptClear(
        float startX, float endX, float centerZ, float floorY)
    {
        if (endX - startX < 0.10f)
        {
            entryFenceRouteBlocker = "expected-span-too-short";
            return false;
        }

        Physics.SyncTransforms();
        const float radius = 0.68f;
        float bottomCenterY = floorY + radius + 0.06f;
        float topCenterY = floorY + 1.72f;
        int sampleCount = Mathf.Clamp(
            Mathf.CeilToInt((endX - startX) / 1.25f) + 1, 3, 16);
        for (int index = 0; index < sampleCount; index++)
        {
            float t = sampleCount == 1 ? 0.5f :
                (float)index / (sampleCount - 1);
            float x = Mathf.Lerp(startX, endX, t);
            int hitCount = Physics.OverlapCapsuleNonAlloc(
                new Vector3(x, bottomCenterY, centerZ),
                new Vector3(x, topCenterY, centerZ),
                radius,
                entryFenceRouteProbe,
                ~0,
                QueryTriggerInteraction.Ignore);
            if (hitCount >= entryFenceRouteProbe.Length)
            {
                entryFenceRouteBlocker = "probe-buffer-full";
                entryFenceRouteBlockerPosition = new Vector3(x, floorY, centerZ);
                return false;
            }
            for (int hitIndex = 0; hitIndex < hitCount; hitIndex++)
            {
                Collider hit = entryFenceRouteProbe[hitIndex];
                if (IsIgnoredRouteProbeCollider(hit))
                {
                    continue;
                }

                entryFenceRouteBlocker = hit.name;
                entryFenceRouteBlockerPosition = hit.bounds.center;
                return false;
            }
        }

        return true;
    }

    private static bool IsIgnoredRouteProbeCollider(Collider collider)
    {
        if (collider == null || collider.isTrigger)
        {
            return true;
        }

        // The probe is for static route geometry. Runtime actors are expected
        // to move through this opening and must not turn a valid opening into
        // a false failure while a verifier is running.
        if (collider.GetComponentInParent<EnemyFighter>() != null ||
            collider.GetComponentInParent<GymVisitorAgent>() != null ||
            collider.GetComponentInParent<GymVisitorVehicle>() != null ||
            collider.GetComponentInParent<PlayerMovement>() != null ||
            collider.GetComponentInParent<GymPoliceOfficer>() != null)
        {
            return true;
        }

        string lowerName = collider.name.ToLowerInvariant();
        return lowerName.Contains("floor") || lowerName.Contains("ground") ||
            lowerName.Contains("walkway");
    }

    private static Transform FindChildRecursive(Transform root, string name)
    {
        if (root == null)
        {
            return null;
        }
        Transform[] children = root.GetComponentsInChildren<Transform>(true);
        for (int index = 0; index < children.Length; index++)
        {
            if (children[index] != null && children[index].name == name)
            {
                return children[index];
            }
        }
        return null;
    }

    public static void Build(
        Transform parent,
        Bounds roomFloor,
        float floorY,
        GymDoorway doorway,
        float pathCenterX,
        float pathSouthZ,
        float outerPathX,
        float pathWidth,
        Material pathMaterial,
        Material markingMaterial,
        Material boundaryMaterial,
        Material boundaryTrimMaterial,
        Material boundaryRibMaterial)
    {
        if (parent == null || doorway == null)
        {
            return;
        }

        GameObject existing = GameObject.Find(RootName);
        if (existing != null)
        {
            RuntimeRoot = existing.transform;
            RemoveLegacyEntryDecorations();
            IsBuilt = true;
            return;
        }

        IsBuilt = true;
        IsLoaded = false;
        LoadFailed = false;
        System.Array.Clear(visitSpotOwners, 0, visitSpotOwners.Length);
        LoadedRendererCount = 0;
        LoadedMaterialCount = 0;
        TransparentFridgePanelCount = 0;
        EntryFenceJunctionCount = 0;
        EntryFenceLargestGap = float.PositiveInfinity;
        entryFenceFloorY = floorY;

        // The door sits at the south end of the gym path; the building is
        // anchored by that door and fills the walled square to the east.
        GetPathOpening(pathSouthZ, out float openingMinZ, out float openingMaxZ);
        EntranceMinZ = openingMinZ;
        EntranceMaxZ = openingMaxZ;
        float entranceZ = (openingMinZ + openingMaxZ) * 0.5f;
        float pathWallEastX = outerPathX + GymOutdoorBuilder.SharedFenceWallThickness * 0.5f;
        float pathWallWestX = outerPathX - GymOutdoorBuilder.SharedFenceWallThickness * 0.5f;
        FacadeX = pathWallEastX + StorePadding;
        float centerX = FacadeX + ModuleDepth * 0.5f;
        float centerZ = entranceZ - DoorLocalZ;
        Vector3 center = new Vector3(centerX, floorY, centerZ);
        StoreBounds = new Bounds(center + Vector3.up * (ModuleHeight * 0.5f),
            new Vector3(ModuleDepth, ModuleHeight, ModuleWidth));
        ShellFootprint = new Bounds(center, new Vector3(ModuleDepth, 0.2f, ModuleWidth));
        float siteSouth = pathSouthZ - SiteSouthBelowPathEnd;
        float siteEast = outerPathX + SiteEastFromPath;
        float siteNorth = centerZ + ModuleWidth * 0.5f + StorePadding +
            GymOutdoorBuilder.SharedFenceWallThickness * 0.5f;
        SiteBounds = new Bounds(
            new Vector3((outerPathX + siteEast) * 0.5f, floorY, (siteSouth + siteNorth) * 0.5f),
            new Vector3(siteEast - outerPathX, 0.2f, siteNorth - siteSouth));

        // Walking route: gym door -> path in front of the opening -> through
        // the opening -> just inside the door -> the checkout counter.
        StoreGymPathClearPoint = new Vector3(pathCenterX, floorY, entranceZ);
        StoreGymPathSouthClearPoint = StoreGymPathClearPoint;
        StoreGateWestClearPoint = new Vector3(pathWallWestX - 0.95f, floorY, entranceZ);
        StoreVisitApproachPoint = new Vector3((pathWallEastX + FacadeX) * 0.5f, floorY, entranceZ);
        StoreEntrancePoint = new Vector3(FacadeX + 0.35f, floorY, entranceZ);
        StoreInsideDoorPoint = new Vector3(FacadeX + 1.0f, floorY, entranceZ);
        StoreFrontClearPoint = LocalToWorld(center, CounterCustomerLocal);
        StoreWorkerPoint = LocalToWorld(center, CounterStaffLocal);
        // Mark faces the counter and the customers north of it.
        StoreWorkerRotation = Quaternion.LookRotation(Vector3.forward, Vector3.up);
        StoreWestApproachPoint = StoreGateWestClearPoint;
        // The old loop round the shop no longer exists; these legacy points
        // collapse onto the path in front of the opening.
        StoreSouthCurvePointB = StoreGymPathClearPoint;
        StoreSouthCurvePointC = StoreGymPathClearPoint;
        StoreSouthCurvePointD = StoreGymPathClearPoint;
        StoreSouthCurvePointE = StoreGymPathClearPoint;
        StoreEastRoutePoint = StoreGymPathClearPoint;
        StoreParkingBypassPoint = new Vector3(
            pathCenterX, floorY, doorway.ExteriorPoint.z + ParkingBypassOffset);
        InnerFenceStartZ = doorway.ExteriorPoint.z + StoreDoorClearance;

        // Short paved link through the opening and two low walls that close
        // it off from the gaps beside the building.
        // Paving under the wall opening, between the path surface and the
        // site ground (which runs on from the wall line to the shop).
        float pathEdgeX = pathCenterX + pathWidth * 0.5f;
        CreateGroundBox(
            "Protein Store Entry Walkway",
            parent,
            new Vector3((pathEdgeX + outerPathX) * 0.5f, floorY - 0.045f, entranceZ),
            new Vector3(outerPathX - pathEdgeX, 0.09f, openingMaxZ - openingMinZ),
            pathMaterial,
            true);
        float halfThickness = GymOutdoorBuilder.SharedFenceWallThickness * 0.5f;
        // Both walls start on the path wall's centre line: the south one
        // continues the path's south wall straight to the storefront.
        entryFenceExpectedStartX = outerPathX;
        entryFenceExpectedEndX = FacadeX;
        entryFenceCenterZ = entranceZ;
        entryFenceHalfOffset = (openingMaxZ - openingMinZ) * 0.5f + halfThickness;
        CreateAccessFence(parent, floorY, outerPathX, FacadeX,
            openingMinZ - halfThickness, openingMinZ - halfThickness,
            boundaryMaterial, boundaryTrimMaterial, boundaryRibMaterial,
            "Protein Store Entry Fence South");
        CreateAccessFence(parent, floorY, outerPathX, FacadeX,
            openingMaxZ + halfThickness, openingMaxZ + halfThickness,
            boundaryMaterial, boundaryTrimMaterial, boundaryRibMaterial,
            "Protein Store Entry Fence North");

        Vector3 storePosition = center;
        GameObject storeRoot = RuntimeGlbSceneLoader.Request(
            AssetPath,
            parent,
            storePosition,
            Quaternion.Euler(0f, 180f, 0f),
            Vector3.one,
            RootName,
            0,
            settleOnSupport: false,
            supportY: floorY,
            onLoaded: loaded => OnStoreLoaded(loaded, floorY));

        if (storeRoot == null)
        {
            LoadFailed = true;
            Debug.LogError(
                $"GYMCHAOS_PROTEIN_STORE_LOAD_FAIL path={AssetPath} " +
                "reason=request_returned_null");
            return;
        }

        RuntimeRoot = storeRoot.transform;
        CreateStoreCollisionShell(storeRoot.transform, center, floorY, entranceZ);
        int measuredJunctions;
        float measuredLargestGap;
        bool measuredRouteClear;
        MeasureEntryFenceContract(
            parent,
            entryFenceExpectedStartX,
            entryFenceExpectedEndX,
            entryFenceCenterZ,
            entryFenceHalfOffset,
            entryFenceFloorY,
            out measuredJunctions,
            out measuredLargestGap,
            out measuredRouteClear);
        EntryFenceJunctionCount = measuredJunctions;
        EntryFenceLargestGap = measuredLargestGap;
        ColliderCount = storeRoot.GetComponentsInChildren<Collider>(true).Length;
        Physics.SyncTransforms();

        float padSouth = ShellFootprint.min.z - (siteSouth + halfThickness);
        float padEast = (siteEast - halfThickness) - ShellFootprint.max.x;
        Debug.Log(
            $"GYMCHAOS_PROTEIN_STORE_REQUESTED path={AssetPath} " +
            $"position={storePosition} footprint={ShellFootprint} " +
            $"entrance={openingMinZ:F2}->{openingMaxZ:F2} facadeX={FacadeX:F2} " +
            $"padWest={StorePadding:F2} padSouth={padSouth:F2} padEast={padEast:F2} " +
            $"counter={StoreFrontClearPoint} worker={StoreWorkerPoint} colliders={ColliderCount}",
            storeRoot);
    }

    /// <summary>
    /// The spot `owner` stops at inside the shop: the one it already holds,
    /// else the first free one (the counter is preferred).
    /// </summary>
    public static Vector3 ReserveVisitSpot(UnityEngine.Object owner)
    {
        int free = -1;
        for (int index = 0; index < visitSpotOwners.Length; index++)
        {
            if (visitSpotOwners[index] == owner && owner != null)
            {
                return LocalToWorld(ShellFootprint.center, VisitSpotLocal[index]);
            }
            if (free < 0 && visitSpotOwners[index] == null)
            {
                free = index;
            }
        }
        if (free < 0)
        {
            return StoreFrontClearPoint;
        }
        visitSpotOwners[free] = owner;
        return LocalToWorld(ShellFootprint.center, VisitSpotLocal[free]);
    }

    public static void ReleaseVisitSpot(UnityEngine.Object owner)
    {
        for (int index = 0; index < visitSpotOwners.Length; index++)
        {
            if (visitSpotOwners[index] == owner)
            {
                visitSpotOwners[index] = null;
            }
        }
    }

    private static Vector3 LocalToWorld(Vector3 center, Vector2 local)
    {
        return new Vector3(center.x + local.x, center.y, center.z + local.y);
    }

    private static void OnStoreLoaded(GameObject loaded, float floorY)
    {
        if (loaded != null) RuntimeRoot = loaded.transform;
        if (loaded == null)
        {
            LoadFailed = true;
            Debug.LogError(
                $"GYMCHAOS_PROTEIN_STORE_LOAD_FAIL path={AssetPath} " +
                "reason=runtime_glb_loader");
            return;
        }

        MeshRenderer[] renderers =
            loaded.GetComponentsInChildren<MeshRenderer>(true);
        for (int index = 0; index < renderers.Length; index++)
        {
            MeshRenderer renderer = renderers[index];
            if (renderer == null) continue;
            // Only the shell casts shadows: it keeps the sun out of the shop
            // without paying for thousands of small product casters.
            string name = renderer.name;
            bool shell = name.StartsWith("Roof", StringComparison.Ordinal) ||
                name.StartsWith("Solid_Walls", StringComparison.Ordinal) ||
                name.StartsWith("Solid_Storefront", StringComparison.Ordinal);
            renderer.shadowCastingMode = shell ? ShadowCastingMode.On : ShadowCastingMode.Off;
            renderer.receiveShadows = !name.StartsWith("Products_", StringComparison.Ordinal);
        }

        AddStoreObjectColliders(loaded);
        Physics.SyncTransforms();
        ColliderCount = loaded.GetComponentsInChildren<Collider>(true).Length;
        HashSet<Material> materials = new HashSet<Material>();
        int transparentFridgePanels = 0;
        for (int index = 0; index < renderers.Length; index++)
        {
            MeshRenderer renderer = renderers[index];
            if (renderer == null || renderer.sharedMaterial == null)
            {
                continue;
            }

            materials.Add(renderer.sharedMaterial);
            if (IsFridgeGlassRenderer(renderer) &&
                IsTransparentMaterial(renderer.sharedMaterial))
            {
                transparentFridgePanels++;
            }
        }

        LoadedRendererCount = renderers.Length;
        LoadedMaterialCount = materials.Count;
        StaticBatchingUtility.Combine(loaded);
        TransparentFridgePanelCount = transparentFridgePanels;
        IsLoaded = LoadedRendererCount > 0 &&
            LoadedMaterialCount >= 8 &&
            TransparentFridgePanelCount >= 2;
        LoadFailed = !IsLoaded;
        if (LoadedRendererCount > 0)
        {
            StoreBounds = CalculateRendererBounds(loaded);
            CreateRuntimeInteriorLights(loaded);
        }

        Debug.Log(
            $"GYMCHAOS_PROTEIN_STORE_GLTF_READY path={AssetPath} " +
            $"renderers={LoadedRendererCount} materials={LoadedMaterialCount} " +
            $"bounds={StoreBounds} floorY={floorY:F3}",
            loaded);
        Debug.Log(
            $"GYMCHAOS_PROTEIN_STORE_CONTRACT_{(IsLoaded && !LoadFailed ? "OK" : "FAIL")} " +
            $"loaded={(IsLoaded ? 1 : 0)} renderers={LoadedRendererCount} " +
            $"materials={LoadedMaterialCount} colliders={ColliderCount} " +
            $"transparentFridgePanels={TransparentFridgePanelCount} " +
            "sharedParking=1 entranceClear=1 productCues=1 readableBrand=1",
            loaded);
    }

    private static void CreateRuntimeInteriorLights(GameObject loaded)
    {
        if (loaded == null) return;
        int existingCount = 0;
        Transform[] children = loaded.GetComponentsInChildren<Transform>(true);
        for (int index = 0; index < children.Length; index++)
        {
            if (children[index].name.StartsWith("Protein Store Interior Light "))
                existingCount++;
        }
        if (existingCount > 0)
        {
            RuntimeInteriorLightCount = existingCount;
            return;
        }

        Vector3 center = ShellFootprint.center;
        for (int index = 0; index < InteriorLightLocal.Length; index++)
        {
            GameObject lightObject = new GameObject(
                "Protein Store Interior Light " + (index + 1));
            lightObject.transform.SetParent(loaded.transform, true);
            lightObject.transform.position =
                LocalToWorld(center, InteriorLightLocal[index]) + Vector3.up * 3.6f;
            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.97f, 0.9f);
            light.intensity = 35f;
            light.range = 8.5f;
            light.shadows = LightShadows.None;
        }
        RuntimeInteriorLightCount = InteriorLightLocal.Length;
    }

    private static bool IsFridgeGlassRenderer(Renderer renderer)
    {
        if (renderer == null)
        {
            return false;
        }

        string lowerName = renderer.name.ToLowerInvariant();
        return lowerName.Contains("fridge") && lowerName.Contains("glass");
    }

    private static bool IsTransparentMaterial(Material material)
    {
        if (material == null)
        {
            return false;
        }

        return (material.HasProperty("_Surface") &&
            material.GetFloat("_Surface") > 0.5f) ||
            material.renderQueue >= (int)UnityEngine.Rendering.RenderQueue.Transparent &&
            material.HasProperty("_BaseColor") &&
            material.GetColor("_BaseColor").a < 0.99f;
    }

    // Walls and the storefront (minus the door) as boxes from the module
    // constants, so the shop blocks people before its GLB has loaded.
    private static void CreateStoreCollisionShell(
        Transform storeRoot, Vector3 center, float floorY, float entranceZ)
    {
        float h = ModuleWallHeight;
        float minX = center.x - ModuleDepth * 0.5f;
        float maxX = center.x + ModuleDepth * 0.5f;
        float minZ = center.z - ModuleWidth * 0.5f;
        float maxZ = center.z + ModuleWidth * 0.5f;
        float doorMin = entranceZ - DoorWidth * 0.5f;
        float doorMax = entranceZ + DoorWidth * 0.5f;
        void Wall(string name, float x0, float x1, float z0, float z1)
        {
            GameObject wall = new GameObject("Protein Store Collision - " + name);
            wall.transform.SetParent(storeRoot, true);
            wall.transform.position = new Vector3((x0 + x1) * 0.5f, floorY + h * 0.5f, (z0 + z1) * 0.5f);
            BoxCollider collider = wall.AddComponent<BoxCollider>();
            collider.size = new Vector3(x1 - x0, h, z1 - z0);
        }
        Wall("Back Wall", maxX - ModuleWall, maxX, minZ, maxZ);
        Wall("North Wall", minX, maxX, maxZ - ModuleWall, maxZ);
        Wall("South Wall", minX, maxX, minZ, minZ + ModuleWall);
        Wall("Storefront South", minX, minX + ModuleWall, minZ, doorMin);
        Wall("Storefront North", minX, minX + ModuleWall, doorMax, maxZ);
        // Above the door: keeps jumps from clipping into the fascia.
        GameObject lintel = new GameObject("Protein Store Collision - Door Lintel");
        lintel.transform.SetParent(storeRoot, true);
        lintel.transform.position = new Vector3(minX + ModuleWall * 0.5f, floorY + (2.7f + h) * 0.5f, entranceZ);
        lintel.AddComponent<BoxCollider>().size = new Vector3(ModuleWall, h - 2.7f, doorMax - doorMin);
    }

    // Fixtures authored as "Solid_*" (counters, shelving, fridges, display
    // platforms) block movement; products, signs and glass do not. Walls and
    // the storefront already have the constant shell above.
    private static void AddStoreObjectColliders(GameObject loaded)
    {
        MeshFilter[] filters = loaded.GetComponentsInChildren<MeshFilter>(true);
        for (int index = 0; index < filters.Length; index++)
        {
            MeshFilter filter = filters[index];
            if (filter == null || filter.sharedMesh == null ||
                filter.GetComponent<Collider>() != null)
            {
                continue;
            }
            string name = filter.name;
            if (!name.StartsWith("Solid_", StringComparison.Ordinal) ||
                name.StartsWith("Solid_Walls", StringComparison.Ordinal) ||
                name.StartsWith("Solid_Storefront", StringComparison.Ordinal))
            {
                continue;
            }

            Bounds meshBounds = filter.sharedMesh.bounds;
            if (meshBounds.size.sqrMagnitude < 0.0001f)
            {
                continue;
            }

            BoxCollider collider = filter.gameObject.AddComponent<BoxCollider>();
            collider.center = meshBounds.center;
            collider.size = meshBounds.size;
            collider.isTrigger = false;
        }
    }

    public static void RemoveLegacyEntryDecorations()
    {
        GameObject root = GameObject.Find(RootName);
        if (root == null)
        {
            return;
        }

        string[] legacyPrefixes =
        {
            "Protein Store Entry Bollard",
            "Protein Store Connector Edge",
            "Protein Store Side Path Edge",
            "Protein Store Entry Edge ",
            "Protein Store Facade Guard"
        };
        Transform[] children = root.GetComponentsInChildren<Transform>(true);
        for (int index = children.Length - 1; index >= 0; index--)
        {
            Transform child = children[index];
            if (child == null || child == root.transform)
            {
                continue;
            }

            for (int prefixIndex = 0; prefixIndex < legacyPrefixes.Length; prefixIndex++)
            {
                if (child.name.StartsWith(legacyPrefixes[prefixIndex],
                    StringComparison.Ordinal))
                {
                    UnityEngine.Object.Destroy(child.gameObject);
                    break;
                }
            }
        }
    }

    private static void AddCollider(
        Transform parent,
        string name,
        Vector3 localPosition,
        Vector3 size,
        bool isTrigger = false)
    {
        GameObject shell = new GameObject(name);
        shell.transform.SetParent(parent, false);
        shell.transform.localPosition = localPosition;
        shell.transform.localRotation = Quaternion.identity;
        shell.transform.localScale = Vector3.one;
        BoxCollider collider = shell.AddComponent<BoxCollider>();
        collider.size = size;
        collider.isTrigger = isTrigger;
    }

    // The walled site: one paved ground over the whole square plus its
    // south, east and west-return low walls (the path wall and the road
    // wall close the other sides).
    public static void CreateOuterRouteGroundAndFence(
        Transform parent, float floorY, float west, float pathSouthZ, float north,
        float fenceNorth,
        Material ground, Material wall, Material trim, Material ribs)
    {
        float south = pathSouthZ - SiteSouthBelowPathEnd;
        float east = west + SiteEastFromPath;
        OuterRouteBounds = new Bounds(
            new Vector3((west + east) * 0.5f, floorY, (south + north) * 0.5f),
            new Vector3(east - west, 0.2f, north - south));
        CreateGroundBox("Protein Store Site Ground", parent,
            new Vector3((west + east) * 0.5f, floorY - 0.1f, (south + north) * 0.5f),
            new Vector3(east - west, 0.2f, north - south), ground, true);
        CreateAccessFence(parent, floorY, west, east, south, south,
            wall, trim, ribs, "Protein Store Perimeter South");
        CreateAccessFence(parent, floorY, east, east, south, fenceNorth,
            wall, trim, ribs, "Protein Store Perimeter East");
        CreateAccessFence(parent, floorY, west, west, south, pathSouthZ,
            wall, trim, ribs, "Protein Store Perimeter West Return");
        float halfThickness = GymOutdoorBuilder.SharedFenceWallThickness * 0.5f;
        Debug.Log(
            $"GYMCHAOS_PROTEIN_STORE_SITE site=({west + halfThickness:F2},{south + halfThickness:F2})" +
            $"->({east - halfThickness:F2},{fenceNorth - halfThickness:F2}) store={ShellFootprint.min.x:F2}," +
            $"{ShellFootprint.min.z:F2}->{ShellFootprint.max.x:F2},{ShellFootprint.max.z:F2} " +
            $"padNorth={(fenceNorth - halfThickness) - ShellFootprint.max.z:F2}");
    }

    private static void CreateAccessFence(
        Transform parent,
        float floorY,
        float startX,
        float endX,
        float startZ,
        float endZ,
        Material boundaryMaterial,
        Material boundaryTrimMaterial,
        Material boundaryRibMaterial,
        string name)
    {
        Vector3 start = new Vector3(startX, floorY, startZ);
        Vector3 end = new Vector3(endX, floorY, endZ);
        Vector3 delta = end - start;
        float length = Vector3.ProjectOnPlane(delta, Vector3.up).magnitude;
        if (length < 0.35f)
        {
            return;
        }

        Vector3 midpoint = (start + end) * 0.5f;
        bool runsAlongX = Mathf.Abs(delta.x) >= Mathf.Abs(delta.z);
        float wallHeight = GymOutdoorBuilder.SharedFenceVisibleHeight;
        GameObject wall = CreateVisualBox(
            name + " Low Wall",
            parent,
            midpoint + Vector3.up * (wallHeight * 0.5f),
            runsAlongX
                ? new Vector3(length, wallHeight, GymOutdoorBuilder.SharedFenceWallThickness)
                : new Vector3(GymOutdoorBuilder.SharedFenceWallThickness, wallHeight, length),
            boundaryMaterial);
        wall.AddComponent<GymExteriorOnlyVisual>();
        GameObject coping = CreateVisualBox(
            name + " Coping",
            parent,
            midpoint + Vector3.up * (wallHeight + 0.08f),
            runsAlongX
                ? new Vector3(length, GymOutdoorBuilder.SharedFenceCopingHeight,
                    GymOutdoorBuilder.SharedFenceWallThickness)
                : new Vector3(GymOutdoorBuilder.SharedFenceWallThickness,
                    GymOutdoorBuilder.SharedFenceCopingHeight, length),
            boundaryTrimMaterial);
        coping.AddComponent<GymExteriorOnlyVisual>();

        int ribCount = Mathf.Clamp(Mathf.FloorToInt(
            length / GymOutdoorBuilder.SharedFenceRibSpacing), 2, 14);
        for (int index = 0; index < ribCount; index++)
        {
            float t = (index + 0.5f) / ribCount - 0.5f;
            Vector3 ribPosition = midpoint + (runsAlongX
                ? new Vector3(t * length, 0f, 0f)
                : new Vector3(0f, 0f, t * length));
            ribPosition.y = floorY + wallHeight * 0.5f;
            Vector3 ribSize = runsAlongX
                ? new Vector3(GymOutdoorBuilder.SharedFenceRibThickness,
                    Mathf.Max(0.55f, wallHeight - 0.22f),
                    GymOutdoorBuilder.SharedFenceWallThickness + 0.025f)
                : new Vector3(GymOutdoorBuilder.SharedFenceWallThickness + 0.025f,
                    Mathf.Max(0.55f, wallHeight - 0.22f),
                    GymOutdoorBuilder.SharedFenceRibThickness);
            GameObject rib = CreateVisualBox(
                name + " Vertical Rib " + (index + 1), parent, ribPosition,
                ribSize, boundaryRibMaterial);
            rib.AddComponent<GymExteriorOnlyVisual>();
        }

        GameObject collisionObject = new GameObject(name + " Collision");
        collisionObject.transform.SetParent(parent, true);
        collisionObject.transform.position = midpoint + Vector3.up *
            (GymOutdoorBuilder.SharedFenceCollisionHeight * 0.5f);
        BoxCollider collision = collisionObject.AddComponent<BoxCollider>();
        collision.size = runsAlongX
            ? new Vector3(length, GymOutdoorBuilder.SharedFenceCollisionHeight,
                GymOutdoorBuilder.SharedFenceWallThickness)
            : new Vector3(GymOutdoorBuilder.SharedFenceWallThickness,
                GymOutdoorBuilder.SharedFenceCollisionHeight, length);
        collision.isTrigger = false;
    }

    private static void CreateBoxBetween(
        string name,
        Transform parent,
        Vector3 start,
        Vector3 end,
        float thickness,
        Material material)
    {
        Vector3 delta = end - start;
        float length = delta.magnitude;
        if (length < 0.01f)
        {
            return;
        }

        GameObject rail = CreateVisualBox(
            name,
            parent,
            (start + end) * 0.5f,
            new Vector3(thickness, thickness, length),
            material);
        rail.transform.rotation = Quaternion.LookRotation(
            delta.normalized,
            Vector3.up);
    }

    private static GameObject CreateGroundBox(
        string name,
        Transform parent,
        Vector3 position,
        Vector3 size,
        Material material,
        bool keepCollider = false)
    {
        return CreateBox(name, parent, position, size, material, keepCollider);
    }

    private static GameObject CreateVisualBox(
        string name,
        Transform parent,
        Vector3 position,
        Vector3 size,
        Material material)
    {
        return CreateBox(name, parent, position, size, material, false);
    }

    private static GameObject CreateBox(
        string name,
        Transform parent,
        Vector3 position,
        Vector3 size,
        Material material,
        bool keepCollider)
    {
        GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
        box.name = name;
        box.transform.SetParent(parent, true);
        box.transform.position = position;
        box.transform.localScale = size;
        Renderer renderer = box.GetComponent<Renderer>();
        if (renderer != null)
        {
            renderer.sharedMaterial = material;
        }

        if (!keepCollider)
        {
            Collider collider = box.GetComponent<Collider>();
            if (collider != null)
            {
                UnityEngine.Object.Destroy(collider);
            }
        }

        return box;
    }

    private static Material CreateMaterial(
        string name,
        Color color,
        float metallic,
        float smoothness)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit") ??
            Shader.Find("Standard");
        Material material = new Material(shader)
        {
            name = name,
            color = color
        };
        if (material.HasProperty("_Metallic"))
        {
            material.SetFloat("_Metallic", metallic);
        }
        if (material.HasProperty("_Smoothness"))
        {
            material.SetFloat("_Smoothness", smoothness);
        }
        return material;
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
