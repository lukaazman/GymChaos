using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Mounts the authored protein.com mall module in the existing exterior gap.
/// The store is kept as one runtime-loaded GLB so its Blender materials remain
/// visible in the Unity scene, while a small explicit collision shell keeps
/// the open entrance and the shared parking route usable.
/// </summary>
public static class GymProteinStoreEnvironment
{
    private const string RootName = "Protein Store (Runtime)";
    private const string AssetPath =
        "BodyBuilders/outside/protein_store_lowpoly.glb";

    // These are the exported GLB bounds in the loader's Unity-facing axes.
    // The facade stays proportional to the gym while the depth is fitted to
    // the narrow storefront strip beside the shared path.
    private const float SourceWidth = 12.0f;
    private const float SourceDepth = 10.81f;
    private const float SourceHeight = 6.08f;
    private const float ShellWidth = 12.0f;
    private const float ShellDepth = 8.30f;
    private const float ShellHeight = 5.50f;
    private const float StoreScale = 0.86f * EnemyFighter.GameplayScale;
    private const float StoreDepthScale = 1.08f * EnemyFighter.GameplayScale;
    // The exported storefront front is the authored Blender FRONT_Y value.
    // -6.65 was the full decorative bounds minimum, so using it here put the
    // checkout worker behind the rear wall instead of in the staff gap.
    private const float CheckoutWorkerAuthoredX = 3.04f;
    // Keep Mark behind the checkout, but leave enough front clearance for his
    // scaled capsule and enough rear clearance that the wall does not hide
    // his body. The previous 1.05 m inset placed him almost inside the wall.
    private const float CheckoutWorkerBackWallInset = 3.0f;
    private const float PlatformWidth = 16.0f;
    private const float PlatformDepth = 15.0f;
    private const float ConnectorWidth = 4.40f;
    private const float StoreBackClearance = 0.42f;
    private const float StorePathClearance = 0.42f;
    private const float StoreSouthClearance = 0.32f;
    private const float StoreDoorClearance = 1.25f;
    private const float RouteClearance = 1.70f;

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
    public static Bounds OuterRouteBounds { get; private set; }
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
    public static Vector3 StoreWorkerPoint { get; private set; }
    public static Quaternion StoreWorkerRotation { get; private set; }
    public static float InnerFenceStartZ { get; private set; }
    public static int RuntimeInteriorLightCount { get; private set; }
    public static Transform RuntimeRoot { get; private set; }
    public static string RuntimeAssetPath => AssetPath;

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
                "Protein Store Entry Fence South Junction Collision") != null;
        bool north = root != null &&
            FindChildRecursive(root,
                "Protein Store Entry Fence North Junction Collision") != null;
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
            "Protein Store Entry Fence South Junction");
        BoxCollider north = FindFenceCollision(root,
            "Protein Store Entry Fence North Junction");
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
            "Protein Store Entry Fence South Junction");
        BoxCollider north = FindFenceCollision(root,
            "Protein Store Entry Fence North Junction");
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
        LoadedRendererCount = 0;
        LoadedMaterialCount = 0;
        TransparentFridgePanelCount = 0;
        EntryFenceJunctionCount = 0;
        EntryFenceLargestGap = float.PositiveInfinity;
        entryFenceExpectedStartX = 0f;
        entryFenceExpectedEndX = 0f;
        entryFenceCenterZ = 0f;
        entryFenceHalfOffset = 0f;
        entryFenceFloorY = floorY;

        // Keep the whole authored module in the storefront strip between the
        // gym wall and the existing shared pedestrian path. The frontage and
        // height stay mall-readable; only the depth is compressed so the path
        // never runs through the shop.
        float worldDepth = SourceDepth * StoreDepthScale;
        float worldWidth = SourceWidth * StoreScale;
        float worldHeight = SourceHeight * StoreScale;
        float platformMinX = outerPathX + 4.50f;
        float centerX = platformMinX + 0.50f + worldDepth * 0.5f;
        float frontX = platformMinX + 0.50f;
        // Keep the gym door's complete wall-to-interior corridor open. The
        // storefront starts farther along the same path, while still using
        // the existing inner guard as its north-side continuation.
        float centerZ = doorway.ExteriorPoint.z;

        StoreBounds = new Bounds(
            new Vector3(centerX, floorY + worldHeight * 0.5f, centerZ),
            new Vector3(worldDepth, worldHeight, worldWidth));
        SiteBounds = new Bounds(new Vector3(platformMinX + PlatformWidth * 0.5f, floorY, centerZ),
            new Vector3(PlatformWidth, 0.20f, PlatformDepth));
        StoreEntrancePoint = new Vector3(frontX + 0.05f, floorY, centerZ);
        StoreFrontClearPoint = new Vector3(
            frontX + RouteClearance, floorY, centerZ);
        StoreWestApproachPoint = doorway.ExteriorPoint;

        // The shop is a physical obstacle beside the shared north/south path.
        // Route traffic around its full footprint with a rectangular dog-leg;
        // the reverse sequence is used for arrivals and departures, so cars
        // and pedestrians never need a backwards teleport or a wall shortcut.
        float storeRouteZ = Mathf.Max(
            StoreBounds.extents.z + RouteClearance,
            ShellDepth * StoreDepthScale * 0.5f + RouteClearance);
        float westRouteX = pathCenterX;
        float storeWestRouteX = StoreBounds.min.x - RouteClearance;
        float storeEastRouteX = StoreBounds.max.x + RouteClearance + 2.55f;
        StoreSouthCurvePointE = new Vector3(
            storeWestRouteX, floorY, centerZ - storeRouteZ);
        StoreSouthCurvePointD = new Vector3(
            storeWestRouteX, floorY, centerZ - storeRouteZ);
        StoreSouthCurvePointC = new Vector3(
            storeEastRouteX, floorY, centerZ - storeRouteZ);
        StoreSouthCurvePointB = new Vector3(
            storeEastRouteX, floorY, centerZ + storeRouteZ);
        StoreWestApproachPoint = new Vector3(
            storeWestRouteX, floorY, centerZ + storeRouteZ);
        StoreParkingBypassPoint = new Vector3(
            westRouteX, floorY, centerZ + storeRouteZ);
        StoreEastRoutePoint = new Vector3(
            storeEastRouteX, floorY, centerZ + storeRouteZ);
        // Cross the shared path through its authored outer-fence opening.
        // This keeps the visitor capsule clear of the south wall endpoint.
        StoreGymPathSouthClearPoint = new Vector3(
            outerPathX + 2.20f,
            floorY,
            centerZ - (ConnectorWidth * 0.5f +
                GymOutdoorBuilder.SharedFenceWallThickness * 0.1f) - 1.35f);
        StoreGymPathClearPoint = new Vector3(
            outerPathX + 2.20f, floorY, centerZ);
        StoreVisitApproachPoint = new Vector3(
            storeWestRouteX, floorY, centerZ);
        // Anchor Mark against the rear wall rather than to the checkout mesh
        // origin. The imported counter is made from several primitives, so
        // its authored centre placed the scaled capsule inside the counter.
        float workerWorldX =
            StoreBounds.max.x - CheckoutWorkerBackWallInset;
        float workerWorldZ = centerZ - CheckoutWorkerAuthoredX * StoreScale;
        StoreWorkerPoint = new Vector3(
            workerWorldX,
            floorY,
            workerWorldZ);
        StoreWorkerRotation = Quaternion.LookRotation(Vector3.left, Vector3.up);
        // The shop replaces the inner guard for its own frontage; resume the
        // existing inner fence immediately after the shop's north edge.
        InnerFenceStartZ = doorway.ExteriorPoint.z + StoreDoorClearance;

        CreateAccessGround(
            parent,
            pathCenterX + pathWidth * 0.5f,
            floorY,
            frontX,
            centerZ,
            centerZ,
            pathCenterX,
            pathWidth,
            pathMaterial,
            markingMaterial);
        float connectorFenceHalfOffset = ConnectorWidth * 0.5f +
            GymOutdoorBuilder.SharedFenceWallThickness * 0.1f;
        // Keep a physical gate between the shared path and storefront.
        // The rails remain collision-aware; the opening lets visitors cross
        // the outer path without entering the shop facade.
        float entryFenceStartX = pathCenterX + pathWidth * 0.5f;
        float entryFenceGateCenterX = outerPathX + 2.20f;
        const float entryFenceGateHalfWidth = 2.20f;
        entryFenceExpectedStartX = entryFenceStartX;
        entryFenceExpectedEndX = outerPathX;
        entryFenceCenterZ = centerZ;
        entryFenceHalfOffset = connectorFenceHalfOffset;
        // Keep the visitor capsule inside the authored gate opening before it
        // turns west toward the gym door. The point is derived from the same
        // fence geometry below, so a future gate resize keeps the route safe.
        float gateVisitorClearance = Mathf.Max(
            0.82f,
            EnemyFighter.GetBodyRadiusForIdentity(BodybuilderIdentity.Cbum) + 0.18f);
        StoreGateWestClearPoint = new Vector3(
            entryFenceGateCenterX - entryFenceGateHalfWidth + gateVisitorClearance,
            floorY,
            centerZ);
        CreateAccessFence(parent, floorY, entryFenceStartX,
            entryFenceGateCenterX - entryFenceGateHalfWidth,
            centerZ - connectorFenceHalfOffset,
            centerZ - connectorFenceHalfOffset,
            boundaryMaterial, boundaryTrimMaterial, boundaryRibMaterial,
            "Protein Store Entry Fence South West");
        CreateAccessFence(parent, floorY,
            entryFenceGateCenterX + entryFenceGateHalfWidth, platformMinX,
            centerZ - connectorFenceHalfOffset,
            centerZ - connectorFenceHalfOffset,
            boundaryMaterial, boundaryTrimMaterial, boundaryRibMaterial,
            "Protein Store Entry Fence South East");
        CreateAccessFence(parent, floorY, pathCenterX + pathWidth * 0.5f, platformMinX,
            centerZ + connectorFenceHalfOffset, centerZ + connectorFenceHalfOffset,
            boundaryMaterial, boundaryTrimMaterial, boundaryRibMaterial,
            "Protein Store Entry Fence North");
        // The shared path's outer wall is offset 0.55 m beyond the path
        // edge. Close that small endpoint gap with short physical/visible
        // returns. They meet the store gate's two side rails but stay on the
        // walkway edges, so the intended center road and store opening remain
        // open.
        CreateAccessFence(parent, floorY,
            entryFenceStartX, outerPathX,
            centerZ - connectorFenceHalfOffset,
            centerZ - connectorFenceHalfOffset,
            boundaryMaterial, boundaryTrimMaterial, boundaryRibMaterial,
            "Protein Store Entry Fence South Junction");
        CreateAccessFence(parent, floorY,
            entryFenceStartX, outerPathX,
            centerZ + connectorFenceHalfOffset,
            centerZ + connectorFenceHalfOffset,
            boundaryMaterial, boundaryTrimMaterial, boundaryRibMaterial,
            "Protein Store Entry Fence North Junction");
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

        Vector3 storePosition = new Vector3(centerX, floorY, centerZ);
        GameObject storeRoot = RuntimeGlbSceneLoader.Request(
            AssetPath,
            parent,
            storePosition,
            Quaternion.Euler(0f, -90f, 0f),
            new Vector3(StoreScale, StoreScale, StoreDepthScale),
            RootName,
            0,
            settleOnSupport: true,
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
        CreateStoreCollisionShell(storeRoot.transform);
        ColliderCount = storeRoot.GetComponentsInChildren<Collider>(true).Length;
        Physics.SyncTransforms();

        Debug.Log(
            $"GYMCHAOS_PROTEIN_STORE_REQUESTED path={AssetPath} " +
            $"position={storePosition} scale=({StoreScale:F2},{StoreScale:F2},{StoreDepthScale:F2}) " +
            $"expectedBounds={StoreBounds} entrance={StoreEntrancePoint} " +
            $"parkingBypass={StoreParkingBypassPoint} colliders={ColliderCount} " +
            $"sharedParking=1 materialSource=embedded_glb",
            storeRoot);
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
            if (renderers[index] == null) continue;
            renderers[index].shadowCastingMode = ShadowCastingMode.Off;
            renderers[index].receiveShadows = false;
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

        Vector3 center = StoreBounds.center;
        float ceilingClearance = Mathf.Min(0.55f, StoreBounds.extents.y * 0.18f);
        for (int index = 0; index < 2; index++)
        {
            GameObject lightObject = new GameObject(
                "Protein Store Interior Light " + (index + 1));
            lightObject.transform.SetParent(loaded.transform, true);
            lightObject.transform.position = center + Vector3.up *
                (StoreBounds.extents.y - ceilingClearance) +
                Vector3.forward * (index == 0 ? -1.7f : 1.7f);
            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.96f, 0.87f);
            // Toned down from 180/6.5 m: less glare on the glossy shelves and
            // fewer lit pixels per light.
            light.intensity = 120f;
            light.range = 5.5f;
            light.shadows = LightShadows.None;
        }
        RuntimeInteriorLightCount = 2;
    }

    private static bool IsFridgeGlassRenderer(Renderer renderer)
    {
        if (renderer == null)
        {
            return false;
        }

        string lowerName = renderer.name.ToLowerInvariant();
        return (lowerName.Contains("coldfridge") ||
            lowerName.Contains("energycooler") ||
            lowerName.Contains("fridge") ||
            lowerName.Contains("cooler")) &&
            lowerName.Contains("glass");
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
    private static void CreateStoreCollisionShell(Transform storeRoot)
    {
        // The front remains intentionally open. Side/back shell colliders
        // protect the mall module without sealing the entrance or its ramp.
        AddCollider(
            storeRoot,
            "Protein Store Collision - Floor",
            new Vector3(0f, -0.08f, 0f),
            new Vector3(ShellWidth, 0.16f, ShellDepth),
            isTrigger: true);
        AddCollider(
            storeRoot,
            "Protein Store Collision - Back Wall",
            new Vector3(0f, ShellHeight * 0.5f,
                -ShellDepth * 0.5f + 0.12f),
            new Vector3(ShellWidth, ShellHeight, 0.24f));
        AddCollider(
            storeRoot,
            "Protein Store Collision - Left Wall",
            new Vector3(-ShellWidth * 0.5f + 0.11f, ShellHeight * 0.5f, 0f),
            new Vector3(0.22f, ShellHeight, ShellDepth));
        AddCollider(
            storeRoot,
            "Protein Store Collision - Right Wall",
            new Vector3(ShellWidth * 0.5f - 0.11f, ShellHeight * 0.5f, 0f),
            new Vector3(0.22f, ShellHeight, ShellDepth));
    }

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

            Renderer renderer = filter.GetComponent<Renderer>();
            if (renderer == null)
            {
                continue;
            }

            Vector3 worldSize = renderer.bounds.size;
            float horizontal = Mathf.Max(worldSize.x, worldSize.z);
            float horizontalMin = Mathf.Min(worldSize.x, worldSize.z);
            string lowerName = filter.name.ToLowerInvariant();
            bool decoration =
                lowerName.Contains("light") ||
                lowerName.Contains("logo") ||
                lowerName.Contains("sign") ||
                lowerName.Contains("label") ||
                lowerName.Contains("text") ||
                lowerName.Contains("ceiling") ||
                lowerName.Contains("roof") ||
                lowerName.Contains("window") ||
                lowerName.Contains("glass");
            bool architecturalSurface =
                lowerName.Contains("wall") ||
                lowerName.Contains("floor");
            bool fixture =
                lowerName.Contains("counter") ||
                lowerName.Contains("checkout") ||
                lowerName.Contains("shelf") ||
                lowerName.Contains("rack") ||
                lowerName.Contains("display") ||
                lowerName.Contains("fridge") ||
                lowerName.Contains("freezer") ||
                lowerName.Contains("cabinet") ||
                lowerName.Contains("register") ||
                lowerName.Contains("desk") ||
                lowerName.Contains("table") ||
                lowerName.Contains("bench") ||
                lowerName.Contains("stand") ||
                lowerName.Contains("case") ||
                lowerName.Contains("pillar") ||
                lowerName.Contains("column");
            bool substantialObject =
                horizontal >= 0.70f &&
                horizontalMin >= 0.16f &&
                worldSize.y >= 0.20f &&
                horizontal <= 5.0f;
            if (decoration || architecturalSurface ||
                (!fixture && !substantialObject))
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
            "Protein Store Entry Fence South ",
            "Protein Store Entry Fence North ",
            "Protein Store Entry Bollard",
            "Protein Store Connector Edge",
            "Protein Store Side Path Edge",
            "Protein Store Entry Edge "
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
        EnsureEntryLowWalls(root);
    }

    private static void EnsureEntryLowWalls(GameObject root)
    {
        if (root == null ||
            (root.transform.Find("Protein Store Entry Fence South West Low Wall") != null &&
             root.transform.Find("Protein Store Entry Fence South East Low Wall") != null))
        {
            return;
        }

        Transform walkwayTransform = root.transform.Find("Protein Store Entry Walkway");
        Renderer walkway = walkwayTransform != null
            ? walkwayTransform.GetComponent<Renderer>()
            : null;
        if (walkway == null)
        {
            return;
        }

        Material wall = CreateMaterial(
            "Exterior boundary wall", new Color(0.07f, 0.13f, 0.21f),
            0.35f, 0.42f);
        Material coping = CreateMaterial(
            "Exterior boundary coping", new Color(0.16f, 0.29f, 0.42f),
            0.55f, 0.5f);
        Material ribs = CreateMaterial(
            "Exterior boundary ribs", new Color(0.02f, 0.05f, 0.09f),
            0.75f, 0.3f);
        Bounds bounds = walkway.bounds;
        float halfWidth = ConnectorWidth * 0.5f +
            GymOutdoorBuilder.SharedFenceWallThickness * 0.1f;
        float gateCenterX = bounds.min.x + 2.20f;
        const float gateHalfWidth = 1.35f;
        CreateAccessFence(root.transform, bounds.max.y, bounds.min.x,
            gateCenterX - gateHalfWidth,
            bounds.center.z - halfWidth, bounds.center.z - halfWidth,
            wall, coping, ribs, "Protein Store Entry Fence South West");
        CreateAccessFence(root.transform, bounds.max.y,
            gateCenterX + gateHalfWidth, bounds.max.x,
            bounds.center.z - halfWidth, bounds.center.z - halfWidth,
            wall, coping, ribs, "Protein Store Entry Fence South East");
        CreateAccessFence(root.transform, bounds.max.y, bounds.min.x, bounds.max.x,
            bounds.center.z + halfWidth, bounds.center.z + halfWidth,
            wall, coping, ribs, "Protein Store Entry Fence North");
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

    public static void CreateOuterRouteGroundAndFence(
        Transform parent, float floorY, float west, float pathSouthZ, float north,
        float fenceNorth,
        Material ground, Material wall, Material trim, Material ribs)
    {
        float south = StoreSouthCurvePointD.z - 2.5f;
        float east = StoreEastRoutePoint.x + 3.5f;
        OuterRouteBounds = new Bounds(
            new Vector3((west + east) * 0.5f, floorY, (south + north) * 0.5f),
            new Vector3(east - west, 0.2f, north - south));
        void Surface(string name, float x0, float x1, float z0, float z1)
        {
            if (x1 <= x0 || z1 <= z0) return;
            CreateGroundBox(name, parent,
                new Vector3((x0 + x1) * 0.5f, floorY - 0.1f, (z0 + z1) * 0.5f),
                new Vector3(x1 - x0, 0.2f, z1 - z0), ground, true);
        }
        Surface("Protein Store Route South", west, east, south, SiteBounds.min.z);
        Surface("Protein Store Route North", west, east, SiteBounds.max.z, north);
        Surface("Protein Store Route East", SiteBounds.max.x, east,
            SiteBounds.min.z, SiteBounds.max.z);
        float entryHalfWidth = Mathf.Min(Mathf.Max(2.4f, 4.4f - 0.25f), ConnectorWidth) * 0.5f;
        Surface("Protein Store Route West South", west, SiteBounds.min.x,
            SiteBounds.min.z, SiteBounds.center.z - entryHalfWidth);
        Surface("Protein Store Route West North", west, SiteBounds.min.x,
            SiteBounds.center.z + entryHalfWidth, SiteBounds.max.z);
        CreateAccessFence(parent, floorY, west, east, south, south,
            wall, trim, ribs, "Protein Store Perimeter South");
        CreateAccessFence(parent, floorY, east, east, south, fenceNorth,
            wall, trim, ribs, "Protein Store Perimeter East");
        CreateAccessFence(parent, floorY, west, west, south, pathSouthZ,
            wall, trim, ribs, "Protein Store Perimeter West Return");
    }

    private static void CreateAccessGround(
        Transform parent,
        float roomEast,
        float floorY,
        float frontX,
        float centerZ,
        float bypassZ,
        float pathCenterX,
        float pathWidth,
        Material pathMaterial,
        Material markingMaterial)
    {
        float sharedPathLeftX = pathCenterX - pathWidth * 0.5f;
        float entryMinX = roomEast;
        float entryMaxX = SiteBounds.min.x;
        float entryWidth = Mathf.Max(0.5f, entryMaxX - entryMinX);
        float entryZWidth = Mathf.Min(
            Mathf.Max(2.4f, pathWidth - 0.25f),
            ConnectorWidth);
        CreateGroundBox(
            "Protein Store Entry Walkway",
            parent,
            new Vector3(
                (entryMinX + entryMaxX) * 0.5f,
                floorY - 0.045f,
                centerZ),
            new Vector3(entryWidth, 0.09f, entryZWidth),
            pathMaterial,
            true);

        CreateBox("Protein Store Platform", parent,
            new Vector3(SiteBounds.center.x, floorY - 0.10f, SiteBounds.center.z),
            new Vector3(PlatformWidth, 0.20f, PlatformDepth), pathMaterial, true);

        // Keep the connector visually plain. The side rails provide the only
        // entrance detailing, matching the surrounding parking fence language.
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

    private static void CreateEntranceBollards(
        Transform parent,
        float floorY,
        float frontX,
        float centerZ,
        Material material)
    {
        for (int side = -1; side <= 1; side += 2)
        {
            CreateVisualBox(
                "Protein Store Entry Bollard",
                parent,
                new Vector3(frontX + 0.24f, floorY + 0.35f,
                    centerZ + side * 1.45f),
                new Vector3(0.18f, 0.70f, 0.18f),
                material);
        }
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
