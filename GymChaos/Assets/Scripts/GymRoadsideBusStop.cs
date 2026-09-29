using UnityEngine;

/// <summary>
/// Builds a single temporary curbside stop on the visitor road and loads the
/// authored Davie bus. The stop sits on the long straight, leaving the two
/// moving traffic lanes and the city-building corner route unobstructed.
/// </summary>
public static class GymRoadsideBusStop
{
    private const string RootName = "Davie Roadside Bus Stop (Runtime)";
    private const string DavieBusAsset =
        "BodyBuilders/vehicles/Davie_Bus.glb";
    private const float DavieBusTargetLength = 10.4f;
    private const float DavieBusEstimatedHalfWidth = 1.45f;
    public const float BusBayStartOffset = 18.0f;
    public const float BusBayEndInset = 7.0f;
    // The outer bay fence continues the parking north wall line.
    public const float BusBayDepth = GymOutdoorBuilder.NorthBoundaryOffsetFromRoadEdge;
    private const float MinimumBayExtraLength = 2.0f;
    private const float MinimumRoadWidth = 7.5f;
    private const float FenceClearance = 0.45f;
    private const float MinimumLaneClearance = 0.15f;
    private const float MinimumCornerClearance = 5.5f;
    private static GameObject davieBusPreviewRoot;
    private static bool dynamicDavieBusActive;

    public static bool IsBuilt { get; private set; }
    public static bool IsDavieBusReady { get; private set; }
    public static Bounds DavieBusBounds { get; private set; }
    public static Vector3 DavieBusCenterPoint { get; private set; }
    public static Vector3 DavieBusPassengerPoint { get; private set; }
    public static Vector3 DavieBusFrontApproachPoint { get; private set; }
    public static Vector3 DavieBusPedestrianExitPoint { get; private set; }
    public static Vector3 DavieBusArrivalApproachPoint { get; private set; }
    public static Vector3 DavieBusDepartureApproachPoint { get; private set; }
    public static Vector3 DavieBusTurnaroundCenterPoint { get; private set; }
    public static Vector3 DavieBusTurnaroundStartPoint { get; private set; }
    public static Vector3 DavieBusReturnLanePoint { get; private set; }
    public static Vector3 DavieBusReturnRoadTurnPoint { get; private set; }
    public static Vector3 DavieBusReturnRoadPoint { get; private set; }
    public static float DavieBusTurnaroundRadiusX { get; private set; }
    public static float DavieBusTurnaroundRadiusZ { get; private set; }
    public static Vector3 DavieBusBayEntryApproachPoint { get; private set; }
    public static Vector3 DavieBusBayEntryPoint { get; private set; }
    public static Vector3 DavieBusBayParkingTurnPoint { get; private set; }
    public static float BusBayStartX { get; private set; }
    public static float BusBayEndX { get; private set; }
    public static float BusBayRoadEdgeZ { get; private set; }
    public static float BusBayOuterZ { get; private set; }
    public static float BusBayLengthForVerification => BusBayEndX - BusBayStartX;
    public static bool HasClearPedestrianGateForVerification()
    {
        if (!IsBuilt) return false;
        Vector3 gatePoint = new Vector3(
            BusBayStartX, DavieBusPassengerPoint.y + 0.95f,
            DavieBusPassengerPoint.z);
        Collider[] nearby = Physics.OverlapSphere(
            gatePoint, 0.30f, ~0, QueryTriggerInteraction.Ignore);
        for (int index = 0; index < nearby.Length; index++)
        {
            Collider collider = nearby[index];
            if (collider == null) continue;
            string name = collider.name.ToLowerInvariant();
            if (name.Contains("bus stop") &&
                (name.Contains("fence") || name.Contains("wall"))) return false;
        }
        return true;
    }
    public static Quaternion DavieBusRotation { get; private set; }

    public static void SetDavieDynamicBusActive(bool active)
    {
        dynamicDavieBusActive = active;
        if (davieBusPreviewRoot != null)
        {
            davieBusPreviewRoot.SetActive(!active);
        }
    }

    public static bool Build(
        Transform parent,
        float floorY,
        float roadStartX,
        float roadTurnX,
        float roadCenterZ,
        float roadWidth,
        float arrivalLaneOffset,
        Vector3 arrivalRoadSpawnPoint,
        Material boundaryMaterial,
        Material boundaryTrimMaterial,
        Material boundaryRibMaterial)
    {
        if (parent == null)
        {
            return false;
        }

        Transform existing = parent.Find(RootName);
        if (existing != null)
        {
            IsBuilt = true;
            return true;
        }

        float bayStartX = roadStartX + BusBayStartOffset;
        float bayEndX = roadTurnX - BusBayEndInset;
        float bayLength = bayEndX - bayStartX;
        bool hasRoadEnvelope = roadWidth >= MinimumRoadWidth;
        bool hasStraightLength = bayLength >=
            DavieBusTargetLength + MinimumBayExtraLength;
        if (!hasRoadEnvelope || !hasStraightLength)
        {
            Debug.LogError(
                $"GYMCHAOS_BUS_STOP_SECTION_FAIL roadWidth={roadWidth:F2} " +
                $"bayLength={bayLength:F2} minimumRoadWidth={MinimumRoadWidth:F2}");
            return false;
        }

        GameObject root = new GameObject(RootName);
        root.transform.SetParent(parent, true);
        root.transform.position = Vector3.zero;

        Material yellow = CreateMaterial(
            "Sprayed Yellow Bus Stop Road Paint",
            new Color(0.96f, 0.61f, 0.025f),
            0.0f,
            0.08f);

        float roadNorthEdgeZ = roadCenterZ + roadWidth * 0.5f;
        float bayOuterZ = roadNorthEdgeZ + BusBayDepth;
        float busCenterZ = roadNorthEdgeZ + FenceClearance +
            DavieBusEstimatedHalfWidth;
        float platformInnerZ = busCenterZ +
            DavieBusEstimatedHalfWidth + 0.25f;
        float passengerZ = Mathf.Lerp(platformInnerZ, bayOuterZ, 0.42f);
        float markingY = floorY + 0.026f;
        BusBayStartX = bayStartX;
        BusBayEndX = bayEndX;
        BusBayRoadEdgeZ = roadNorthEdgeZ;
        CreateBusBayOuterFence(
            root.transform,
            floorY,
            bayStartX,
            bayEndX,
            bayOuterZ,
            boundaryMaterial, boundaryTrimMaterial, boundaryRibMaterial);
        // No side return fences: the paved pockets west and east of the bay
        // stay open to it, so the bay, pockets and road read as one area
        // closed only by the continuous north boundary line.
        BusBayOuterZ = bayOuterZ;

        CreateMarkingBox(
            "Bus Bay Sprayed Yellow Entry Line",
            root.transform,
            new Vector3((bayStartX + bayEndX) * 0.5f,
                markingY, roadNorthEdgeZ + 0.08f),
            new Vector3(bayLength, 0.032f, 0.12f),
            Quaternion.identity,
            yellow);
        CreateMarkingBox(
            "Bus Bay West End Bar",
            root.transform,
            new Vector3(bayStartX, markingY,
                (roadNorthEdgeZ + bayOuterZ) * 0.5f),
            new Vector3(0.12f, 0.032f, BusBayDepth),
            Quaternion.identity,
            yellow);
        CreateMarkingBox(
            "Bus Bay East End Bar",
            root.transform,
            new Vector3(bayEndX, markingY,
                (roadNorthEdgeZ + bayOuterZ) * 0.5f),
            new Vector3(0.12f, 0.032f, BusBayDepth),
            Quaternion.identity,
            yellow);

        const float dashLength = 2.35f;
        const float dashGap = 1.15f;
        int dashCount = Mathf.Max(
            1,
            Mathf.FloorToInt((bayLength - 1f + dashGap) /
                (dashLength + dashGap)));
        float dashedSpan = dashCount * dashLength +
            (dashCount - 1) * dashGap;
        float dashX = (bayStartX + bayEndX - dashedSpan) * 0.5f +
            dashLength * 0.5f;
        for (int i = 0; i < dashCount; i++)
        {
            CreateMarkingBox(
                "Bus Bay Inner Yellow Dash " + (i + 1),
                root.transform,
                new Vector3(
                    dashX + i * (dashLength + dashGap),
                    markingY,
                    bayOuterZ - 0.10f),
                new Vector3(dashLength, 0.032f, 0.12f),
                Quaternion.identity,
                yellow);
        }

        CreateRoadStroke(
            "Bus Bay Entry Taper",
            root.transform,
            new Vector3(bayStartX + 0.35f, markingY, roadNorthEdgeZ + 0.08f),
            new Vector3(bayStartX + 2.15f, markingY,
                bayOuterZ - 0.12f),
            0.12f,
            yellow);
        CreateRoadStroke(
            "Bus Bay Exit Taper",
            root.transform,
            new Vector3(bayEndX - 2.15f, markingY, bayOuterZ - 0.12f),
            new Vector3(bayEndX - 0.35f, markingY, roadNorthEdgeZ + 0.08f),
            0.12f,
            yellow);

        float legendCenterX = bayStartX + 5.0f;
        CreateRoadLegend(
            root.transform,
            legendCenterX,
            roadNorthEdgeZ + BusBayDepth * 0.5f,
            markingY + 0.004f,
            yellow);

        // Parked 3.3 m inside the bay's east end: a forward pull-in then keeps
        // the bus tail clear of the road wall beside the bay mouth.
        float busCenterX = bayEndX - DavieBusTargetLength * 0.5f - 3.3f;
        DavieBusCenterPoint = new Vector3(busCenterX, floorY, busCenterZ);
        DavieBusPassengerPoint = new Vector3(
            busCenterX - DavieBusTargetLength * 0.5f + 1.75f,
            floorY,
            passengerZ);
        DavieBusFrontApproachPoint = new Vector3(
            busCenterX - DavieBusTargetLength * 0.5f - 1.10f,
            floorY,
            passengerZ);
        DavieBusPedestrianExitPoint = new Vector3(
            bayStartX - 1.05f, floorY, passengerZ);
        // Arrival uses the north/right-hand lane for the inbound approach.
        // After the U-turn the bus must return on the opposite south/right
        // lane, never reuse the inbound lane.
        float returnLaneZ = roadCenterZ - Mathf.Abs(arrivalLaneOffset);
        DavieBusBayEntryApproachPoint = new Vector3(
            bayEndX + 2.0f, floorY, roadCenterZ + arrivalLaneOffset);
        DavieBusBayEntryPoint = new Vector3(
            bayEndX - 0.8f, floorY, busCenterZ);
        DavieBusBayParkingTurnPoint = new Vector3(
            bayEndX - 0.8f, floorY, busCenterZ);
        DavieBusArrivalApproachPoint = DavieBusBayEntryApproachPoint;
        float turnaroundCenterX = bayStartX + 10.5f;
        float turnaroundCenterZ = (busCenterZ + returnLaneZ) * 0.5f;
        DavieBusTurnaroundRadiusX = 6.5f;
        DavieBusTurnaroundRadiusZ =
            Mathf.Abs(busCenterZ - returnLaneZ) * 0.5f;
        DavieBusTurnaroundCenterPoint = new Vector3(
            turnaroundCenterX, floorY, turnaroundCenterZ);
        DavieBusTurnaroundStartPoint = new Vector3(
            turnaroundCenterX, floorY, busCenterZ);
        DavieBusDepartureApproachPoint = DavieBusTurnaroundStartPoint;
        DavieBusReturnLanePoint = new Vector3(
            turnaroundCenterX, floorY, returnLaneZ);
        DavieBusReturnRoadTurnPoint = new Vector3(
            roadTurnX - arrivalLaneOffset, floorY, returnLaneZ);
        DavieBusReturnRoadPoint = new Vector3(
            arrivalRoadSpawnPoint.x, floorY, returnLaneZ);
        DavieBusRotation = Quaternion.LookRotation(Vector3.left, Vector3.up);
        davieBusPreviewRoot = RuntimeGlbSceneLoader.Request(
            DavieBusAsset,
            root.transform,
            DavieBusCenterPoint,
            DavieBusRotation,
            Vector3.one * DavieBusTargetLength,
            "Davie Bus - Temporary Roadside Stop",
            root.layer,
            true,
            floorY - GymVisitorVehicle.TyreContactSink,
            loaded => FinalizeDavieBus(
                loaded,
                roadCenterZ,
                roadNorthEdgeZ,
                bayOuterZ,
                bayStartX,
                bayEndX,
                roadTurnX,
                roadWidth,
                arrivalLaneOffset));

        IsBuilt = true;
        Debug.Log(
            $"GYMCHAOS_BUS_STOP_SECTION_OK roadWidth={roadWidth:F2} " +
            $"lanes=2 bayLength={bayLength:F2} bayDepth={BusBayDepth:F2} " +
            $"bayStart={bayStartX:F2} bayEnd={bayEndX:F2} " +
            $"markings=yellow-real-road source={DavieBusAsset}",
            root);
        return true;
    }

    private static void FinalizeDavieBus(
        GameObject loaded,
        float roadCenterZ,
        float roadNorthEdgeZ,
        float bayOuterZ,
        float bayStartX,
        float bayEndX,
        float roadTurnX,
        float roadWidth,
        float arrivalLaneOffset)
    {
        if (loaded == null)
        {
            IsDavieBusReady = false;
            Debug.LogError(
                $"GYMCHAOS_DAVIE_BUS_FAIL source={DavieBusAsset} reason=load");
            return;
        }

        Renderer[] renderers = loaded.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
        {
            IsDavieBusReady = false;
            Debug.LogError(
                $"GYMCHAOS_DAVIE_BUS_FAIL source={DavieBusAsset} reason=no-renderers",
                loaded);
            return;
        }

        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
        {
            bounds.Encapsulate(renderers[i].bounds);
        }

        BoxCollider busCollider = null;
        if (TryGetOrientedLocalBounds(loaded, loaded.transform,
            out Bounds localBounds))
        {
            busCollider = loaded.AddComponent<BoxCollider>();
            busCollider.center = localBounds.center;
            busCollider.size = localBounds.size;
        }

        float bayOuterGap = bayOuterZ - bounds.max.z;
        float laneGap = bounds.min.z - roadNorthEdgeZ;
        float bayStartGap = bounds.min.x - bayStartX;
        float bayEndGap = bayEndX - bounds.max.x;
        float cornerGap = roadTurnX - bounds.max.x;
        bool contractPassed = roadWidth >= MinimumRoadWidth &&
            bayOuterGap >= 0.12f &&
            laneGap >= MinimumLaneClearance &&
            bayStartGap >= 0.12f &&
            bayEndGap >= 0.12f &&
            cornerGap >= MinimumCornerClearance &&
            bounds.size.x >= 9.5f &&
            bounds.size.x <= 11.5f;

        DavieBusBounds = bounds;
        IsDavieBusReady = contractPassed;
        davieBusPreviewRoot = loaded;
        loaded.SetActive(!dynamicDavieBusActive);
        Debug.Log(
            $"GYMCHAOS_DAVIE_BUS_{(contractPassed ? "OK" : "FAIL")} " +
            $"source={DavieBusAsset} bounds={bounds} " +
            $"bayOuterGap={bayOuterGap:F2} laneGap={laneGap:F2} " +
            $"bayStartGap={bayStartGap:F2} bayEndGap={bayEndGap:F2} " +
            $"cornerGap={cornerGap:F2} collider={(busCollider != null ? 1 : 0)}",
            loaded);
    }


    private static bool TryGetOrientedLocalBounds(
        GameObject visual, Transform root, out Bounds bounds)
    {
        bounds = default;
        bool hasBounds = false;
        MeshFilter[] filters = visual.GetComponentsInChildren<MeshFilter>(true);
        for (int filterIndex = 0; filterIndex < filters.Length; filterIndex++)
        {
            MeshFilter filter = filters[filterIndex];
            if (filter == null || filter.sharedMesh == null) continue;
            Bounds source = filter.sharedMesh.bounds;
            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 sourcePoint = new Vector3(
                    (corner & 1) == 0 ? source.min.x : source.max.x,
                    (corner & 2) == 0 ? source.min.y : source.max.y,
                    (corner & 4) == 0 ? source.min.z : source.max.z);
                Vector3 localPoint = root.InverseTransformPoint(
                    filter.transform.TransformPoint(sourcePoint));
                if (!hasBounds)
                {
                    bounds = new Bounds(localPoint, Vector3.zero);
                    hasBounds = true;
                }
                else bounds.Encapsulate(localPoint);
            }
        }
        return hasBounds;
    }
    private static void CreateBusBayOuterFence(
        Transform parent,
        float floorY,
        float startX,
        float endX,
        float outerZ,
        Material boundaryMaterial,
        Material boundaryTrimMaterial,
        Material boundaryRibMaterial)
    {
        CreateBusStopFenceSegment(
            parent, floorY,
            new Vector3(
                startX + GymOutdoorBuilder.SharedFenceWallThickness * 0.5f,
                floorY, outerZ),
            new Vector3(
                endX - GymOutdoorBuilder.SharedFenceWallThickness * 0.5f,
                floorY, outerZ),
            "Bus Stop Outer Fence",
            boundaryMaterial, boundaryTrimMaterial, boundaryRibMaterial);
    }

    private static void CreateBusStopFenceSegment(
        Transform parent,
        float floorY,
        Vector3 start,
        Vector3 end,
        string name,
        Material boundaryMaterial,
        Material boundaryTrimMaterial,
        Material boundaryRibMaterial)
    {
        Vector3 delta = Vector3.ProjectOnPlane(end - start, Vector3.up);
        float length = delta.magnitude;
        if (length < 0.35f)
        {
            return;
        }

        Quaternion rotation = Quaternion.FromToRotation(
            Vector3.right, delta.normalized);
        float wallHeight = GymOutdoorBuilder.SharedFenceVisibleHeight;
        float wallThickness = GymOutdoorBuilder.SharedFenceWallThickness;
        Vector3 center = (start + end) * 0.5f;
        center.y = floorY;
        GameObject wall = CreateMarkingBox(
            name + " Low Wall", parent,
            center + Vector3.up * (wallHeight * 0.5f),
            new Vector3(length, wallHeight, wallThickness),
            rotation, boundaryMaterial);
        wall.AddComponent<GymExteriorOnlyVisual>();
        GameObject coping = CreateMarkingBox(
            name + " Coping", parent,
            center + Vector3.up * (wallHeight + 0.08f),
            new Vector3(length, GymOutdoorBuilder.SharedFenceCopingHeight,
                wallThickness), rotation, boundaryTrimMaterial);
        coping.AddComponent<GymExteriorOnlyVisual>();

        int ribCount = Mathf.Clamp(Mathf.FloorToInt(
            length / GymOutdoorBuilder.SharedFenceRibSpacing), 2, 14);
        for (int index = 0; index < ribCount; index++)
        {
            float t = (index + 0.5f) / ribCount - 0.5f;
            GameObject rib = CreateMarkingBox(
                name + " Vertical Rib " + (index + 1), parent,
                center + rotation * new Vector3(
                    t * length, wallHeight * 0.5f, 0f),
                new Vector3(GymOutdoorBuilder.SharedFenceRibThickness,
                    Mathf.Max(0.55f, wallHeight - 0.22f), wallThickness + 0.025f),
                rotation, boundaryRibMaterial);
            rib.AddComponent<GymExteriorOnlyVisual>();
        }

        GameObject collisionObject = new GameObject(
            "Outdoor Boundary - " + name);
        collisionObject.transform.SetParent(parent, true);
        collisionObject.transform.position = center + Vector3.up *
            (GymOutdoorBuilder.SharedFenceCollisionHeight * 0.5f);
        collisionObject.transform.rotation = rotation;
        BoxCollider collider = collisionObject.AddComponent<BoxCollider>();
        collider.size = new Vector3(length,
            GymOutdoorBuilder.SharedFenceCollisionHeight, wallThickness);
        collider.isTrigger = false;
    }
    private static void CreateRoadLegend(
        Transform parent,
        float centerX,
        float centerZ,
        float y,
        Material material)
    {
        const string text = "BUS STOP";
        const float letterWidth = 0.7f;
        const float letterHeight = 1.15f;
        const float spacing = 0.17f;
        const float spaceWidth = 0.5f;
        const float strokeWidth = 0.1f;

        float totalWidth = 0f;
        for (int i = 0; i < text.Length; i++)
        {
            totalWidth += text[i] == ' ' ? spaceWidth : letterWidth;
            if (i < text.Length - 1)
            {
                totalWidth += spacing;
            }
        }

        float cursorX = centerX - totalWidth * 0.5f;
        int strokeIndex = 0;
        for (int i = 0; i < text.Length; i++)
        {
            char letter = text[i];
            if (letter == ' ')
            {
                cursorX += spaceWidth + spacing;
                continue;
            }

            CreateRoadLetter(
                letter,
                parent,
                cursorX,
                centerZ,
                y,
                letterWidth,
                letterHeight,
                strokeWidth,
                material,
                ref strokeIndex);
            cursorX += letterWidth + spacing;
        }
    }

    private static void CreateRoadLetter(
        char letter,
        Transform parent,
        float leftX,
        float centerZ,
        float y,
        float width,
        float height,
        float strokeWidth,
        Material material,
        ref int strokeIndex)
    {
        float rightX = leftX + width;
        float middleX = leftX + width * 0.5f;
        float bottomZ = centerZ - height * 0.5f;
        float middleZ = centerZ;
        float topZ = centerZ + height * 0.5f;

        if (letter == 'B' || letter == 'P' || letter == 'U')
            AddLegendStroke(parent, leftX, bottomZ, leftX, topZ, y,
                strokeWidth, material, ref strokeIndex);
        if (letter == 'B' || letter == 'O' || letter == 'P' ||
            letter == 'S' || letter == 'T')
            AddLegendStroke(parent, leftX, topZ, rightX, topZ, y,
                strokeWidth, material, ref strokeIndex);
        if (letter == 'B' || letter == 'P' || letter == 'S')
            AddLegendStroke(parent, leftX, middleZ, rightX, middleZ, y,
                strokeWidth, material, ref strokeIndex);
        if (letter == 'B' || letter == 'O' || letter == 'S' || letter == 'U')
            AddLegendStroke(parent, leftX, bottomZ, rightX, bottomZ, y,
                strokeWidth, material, ref strokeIndex);
        if (letter == 'O' || letter == 'U')
            AddLegendStroke(parent, rightX, bottomZ, rightX, topZ, y,
                strokeWidth, material, ref strokeIndex);
        if (letter == 'B' || letter == 'P')
            AddLegendStroke(parent, rightX, middleZ, rightX, topZ, y,
                strokeWidth, material, ref strokeIndex);
        if (letter == 'B')
            AddLegendStroke(parent, rightX, bottomZ, rightX, middleZ, y,
                strokeWidth, material, ref strokeIndex);
        if (letter == 'S')
        {
            AddLegendStroke(parent, leftX, middleZ, leftX, topZ, y,
                strokeWidth, material, ref strokeIndex);
            AddLegendStroke(parent, rightX, bottomZ, rightX, middleZ, y,
                strokeWidth, material, ref strokeIndex);
        }
        if (letter == 'T')
            AddLegendStroke(parent, middleX, bottomZ, middleX, topZ, y,
                strokeWidth, material, ref strokeIndex);
    }

    private static void AddLegendStroke(
        Transform parent,
        float startX,
        float startZ,
        float endX,
        float endZ,
        float y,
        float width,
        Material material,
        ref int strokeIndex)
    {
        strokeIndex++;
        CreateRoadStroke(
            "BUS STOP Road Legend Stroke " + strokeIndex,
            parent,
            new Vector3(startX, y, startZ),
            new Vector3(endX, y, endZ),
            width,
            material);
    }

    private static void CreateRoadStroke(
        string name,
        Transform parent,
        Vector3 start,
        Vector3 end,
        float width,
        Material material)
    {
        Vector3 delta = end - start;
        float length = Vector3.ProjectOnPlane(delta, Vector3.up).magnitude;
        if (length <= 0.001f)
        {
            return;
        }

        CreateMarkingBox(
            name,
            parent,
            (start + end) * 0.5f,
            new Vector3(length, 0.032f, width),
            Quaternion.FromToRotation(Vector3.right, delta.normalized),
            material);
    }

    private static GameObject CreateMarkingBox(
        string name,
        Transform parent,
        Vector3 position,
        Vector3 scale,
        Quaternion rotation,
        Material material)
    {
        GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
        box.name = name;
        box.transform.SetParent(parent, true);
        box.transform.SetPositionAndRotation(position, rotation);
        box.transform.localScale = scale;
        Renderer renderer = box.GetComponent<Renderer>();
        if (renderer != null)
        {
            renderer.sharedMaterial = material;
        }

        Object.Destroy(box.GetComponent<Collider>());
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
        Material material = new Material(shader) { name = name };
        if (material.HasProperty("_BaseColor"))
            material.SetColor("_BaseColor", color);
        material.color = color;
        if (material.HasProperty("_Metallic"))
            material.SetFloat("_Metallic", metallic);
        if (material.HasProperty("_Smoothness"))
            material.SetFloat("_Smoothness", smoothness);
        return material;
    }
}
