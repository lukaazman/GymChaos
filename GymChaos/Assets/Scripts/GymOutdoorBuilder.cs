using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Builds a complete, closed exterior courtyard that is actually reachable
/// from the gym. The parking sits in front of the window view, while a filled
/// dog-leg path connects it to the existing black visitor door on the east
/// wall. Solid collision shells and visible architectural perimeter walls are
/// deliberately taller than the player's jump arc so the exterior cannot
/// become a fall/glitch route.
/// </summary>
public static class GymOutdoorBuilder
{
    private const string RootName = "Gym Exterior (Runtime)";
    private const float ParkingDepth = 14f;
    private const float PathWidth = 4.4f;
    private const float BoundaryHeight = 5.2f;
    private const float EntranceFenceStartOffset = 3.25f;
    private const float EntranceFenceEndInset = 0.2f;
    private const float InnerBoundaryWallOffset = 0.42f;
    private const float ParkingSurfaceOffset = 0.01f;
    private const float ParkingLightBaseHeight = 0.22f;
    private const float ParkingLightPoleHeight = 5.4f;

    public static bool IsBuilt { get; private set; }
    public static Bounds ParkingBounds { get; private set; }
    public static Bounds AccessibleBounds { get; private set; }

    public static bool IsPlayerOutsideGym(Vector3 position)
    {
        GameObject floorObject = GameObject.Find("Rubber Floor");
        Renderer floorRenderer = floorObject != null
            ? floorObject.GetComponent<Renderer>()
            : null;
        if (floorRenderer == null)
        {
            return false;
        }

        Bounds roomFloor = floorRenderer.bounds;
        const float exteriorClearance = 0.55f;
        return position.x < roomFloor.min.x - exteriorClearance ||
            position.x > roomFloor.max.x + exteriorClearance ||
            position.z < roomFloor.min.z - exteriorClearance ||
            position.z > roomFloor.max.z + exteriorClearance;
    }

    public static void Build(PlayerMovement player)
    {
        GameObject existingRoot = GameObject.Find(RootName);
        if (existingRoot != null)
        {
            IsBuilt = true;
            return;
        }

        GameObject floorObject = GameObject.Find("Rubber Floor");
        Renderer floorRenderer = floorObject != null ? floorObject.GetComponent<Renderer>() : null;
        GymDoorway doorway = GymDoorway.Instance != null
            ? GymDoorway.Instance
            : Object.FindAnyObjectByType<GymDoorway>();
        if (floorRenderer == null || doorway == null)
        {
            Debug.LogWarning("Gym exterior could not be built because the room floor or visitor door is missing.");
            return;
        }

        Bounds roomFloor = floorRenderer.bounds;
        float floorY = roomFloor.max.y;
        float roomEast = roomFloor.max.x;
        float roomNorth = roomFloor.max.z;
        float parkingWidth = Mathf.Clamp(roomFloor.size.x * 0.7f, 22f, 38f);
        float parkingCenterZ = roomNorth + ParkingDepth * 0.5f + 1f;
        float parkingMinX = roomFloor.center.x - parkingWidth * 0.5f;
        float parkingMaxX = roomFloor.center.x + parkingWidth * 0.5f;
        float doorZ = doorway.ExteriorPoint.z;
        float pathCenterX = roomEast + 4.5f;
        float pathSouthZ = doorZ - 3.15f;
        float pathNorthZ = parkingCenterZ + ParkingDepth * 0.5f + 0.55f;
        float pathLength = Mathf.Max(0.5f, parkingCenterZ - doorZ);
        float outerPathX = pathCenterX + PathWidth * 0.5f + 0.55f;
        // Keep the inner guard flush with the building's exterior face. The
        // old path-derived position left a wide walkable gap beside the wall.
        float innerPathX = roomEast + InnerBoundaryWallOffset;
        float courtyardMinX = parkingMinX - 1.25f;
        float courtyardMaxX = outerPathX + 0.75f;
        float courtyardMinZ = pathSouthZ - 0.75f;
        float courtyardMaxZ = pathNorthZ + 0.75f;

        GameObject root = new GameObject(RootName);
        Material courtyardMaterial = CreateMaterial(
            "Exterior courtyard foundation",
            new Color(0.045f, 0.09f, 0.14f),
            0.05f,
            0.3f);
        Material asphalt = CreateMaterial(
            "Outdoor parking asphalt",
            new Color(0.045f, 0.065f, 0.1f),
            0.08f,
            0.36f);
        Material landscape = CreateMaterial(
            "Outdoor park ground",
            new Color(0.045f, 0.13f, 0.16f),
            0f,
            0.2f);
        Material pathMaterial = CreateMaterial(
            "Outdoor concrete path",
            new Color(0.13f, 0.2f, 0.28f),
            0.02f,
            0.32f);
        Material curbMaterial = CreateMaterial(
            "Outdoor curb",
            new Color(0.16f, 0.22f, 0.29f),
            0.1f,
            0.4f);
        Material boundaryMaterial = CreateMaterial(
            "Exterior boundary wall",
            new Color(0.07f, 0.13f, 0.21f),
            0.35f,
            0.42f);
        Material boundaryTrimMaterial = CreateMaterial(
            "Exterior boundary coping",
            new Color(0.16f, 0.29f, 0.42f),
            0.55f,
            0.5f);
        SetEmission(boundaryTrimMaterial, new Color(0.025f, 0.08f, 0.16f));
        Material boundaryRibMaterial = CreateMaterial(
            "Exterior boundary ribs",
            new Color(0.02f, 0.05f, 0.09f),
            0.75f,
            0.3f);
        Material planterMaterial = CreateMaterial(
            "Exterior planter concrete",
            new Color(0.1f, 0.19f, 0.26f),
            0.12f,
            0.34f);
        Material foliageMaterial = CreateMaterial(
            "Exterior cool foliage",
            new Color(0.035f, 0.18f, 0.16f),
            0f,
            0.22f);
        Material signMaterial = CreateMaterial(
            "Parking wayfinding sign",
            new Color(0.025f, 0.18f, 0.48f),
            0.05f,
            0.28f);
        SetEmission(signMaterial, new Color(0.03f, 0.14f, 0.45f));
        Material markingMaterial = CreateMaterial(
            "Parking line paint",
            new Color(0.96f, 0.98f, 0.94f),
            0.02f,
            0.42f);
        SetEmission(markingMaterial, new Color(1.15f, 1.25f, 1.05f));
        Material lampMaterial = CreateMaterial(
            "Parking light pole",
            new Color(0.035f, 0.045f, 0.055f),
            0.72f,
            0.28f);
        Material lampFixtureMaterial = CreateMaterial(
            "Parking light fixture",
            new Color(0.62f, 0.82f, 1f),
            0.02f,
            0.16f);
        SetEmission(lampFixtureMaterial, new Color(1.4f, 4.2f, 9f));

        CreateBox(
            "Exterior Courtyard Foundation",
            root.transform,
            new Vector3(
                (courtyardMinX + courtyardMaxX) * 0.5f,
                floorY - 0.13f,
                (courtyardMinZ + courtyardMaxZ) * 0.5f),
            new Vector3(
                courtyardMaxX - courtyardMinX,
                0.24f,
                courtyardMaxZ - courtyardMinZ),
            courtyardMaterial,
            true);

        CreateBox(
            "Mini Parking Lot",
            root.transform,
            new Vector3(roomFloor.center.x, floorY - 0.11f, parkingCenterZ),
            new Vector3(parkingWidth, 0.24f, ParkingDepth),
            asphalt,
            true);
        CreateBox(
            "Path from Gym Door",
            root.transform,
            new Vector3(pathCenterX, floorY - 0.11f, doorZ + pathLength * 0.5f),
            new Vector3(PathWidth, 0.24f, pathLength + 0.8f),
            pathMaterial,
            true);

        float horizontalMinX = parkingMaxX - 0.9f;
        float horizontalMaxX = outerPathX;
        CreateBox(
            "Parking Path Turn",
            root.transform,
            new Vector3((horizontalMinX + horizontalMaxX) * 0.5f, floorY - 0.11f, parkingCenterZ),
            new Vector3(horizontalMaxX - horizontalMinX, 0.24f, PathWidth),
            pathMaterial,
            true);
        CreateBox(
            "Black Door Landing",
            root.transform,
            new Vector3((roomEast + outerPathX) * 0.5f, floorY - 0.11f, doorZ),
            new Vector3(outerPathX - roomEast + 0.8f, 0.24f, 6.2f),
            pathMaterial,
            true);

        CreateParkingMarkings(root.transform, floorY, parkingMinX, parkingMaxX, parkingCenterZ, markingMaterial);
        CreatePathEdge(root.transform, floorY, pathCenterX, doorZ, parkingCenterZ, markingMaterial);
        CreateParkingCurb(root.transform, floorY, parkingMinX, parkingMaxX, parkingCenterZ, curbMaterial);
        CreateParkingDetails(
            root.transform,
            floorY,
            parkingMinX,
            parkingMaxX,
            parkingCenterZ,
            lampMaterial,
            lampFixtureMaterial,
            curbMaterial);
        CreateParkingSiteDetails(
            root.transform,
            floorY,
            parkingMinX,
            parkingMaxX,
            parkingCenterZ,
            pathCenterX,
            doorZ,
            planterMaterial,
            foliageMaterial,
            signMaterial,
            markingMaterial,
            lampMaterial);

        // Keep the landscape behind the lot as a thin ground strip. The old
        // implementation used a tall horizon cube here, which blocked the
        // parking view and read as a giant black wall from inside the gym.
        CreateBox(
            "Parking Park Landscape",
            root.transform,
            new Vector3(roomFloor.center.x,
                floorY - 0.06f,
                parkingCenterZ + ParkingDepth * 0.5f + 2.7f),
            new Vector3(parkingWidth + 8f, 0.12f, 4.8f),
            landscape,
            false);

        // Parking perimeter. The east side is split around the path opening;
        // all other edges are continuous and high enough to stop a jump-over.
        CreateBoundary(
            "Outdoor Boundary - Parking North",
            root.transform,
            new Vector3(roomFloor.center.x, floorY + BoundaryHeight * 0.5f, parkingCenterZ + ParkingDepth * 0.5f + 0.55f),
            new Vector3(parkingWidth + 1.1f, BoundaryHeight, 0.5f));
        CreateBoundary(
            "Outdoor Boundary - Parking South",
            root.transform,
            new Vector3(roomFloor.center.x, floorY + BoundaryHeight * 0.5f, parkingCenterZ - ParkingDepth * 0.5f - 0.55f),
            new Vector3(parkingWidth + 1.1f, BoundaryHeight, 0.5f));
        CreateBoundary(
            "Outdoor Boundary - Parking West",
            root.transform,
            new Vector3(parkingMinX - 0.55f, floorY + BoundaryHeight * 0.5f, parkingCenterZ),
            new Vector3(0.5f, BoundaryHeight, ParkingDepth + 1.1f));

        float parkingEastBoundaryX = parkingMaxX + 0.55f;
        float parkingMinZ = parkingCenterZ - ParkingDepth * 0.5f - 0.55f;
        float parkingMaxZ = parkingCenterZ + ParkingDepth * 0.5f + 0.55f;
        float northBoundaryExtensionLength = outerPathX - parkingEastBoundaryX;
        if (northBoundaryExtensionLength > 0.4f)
        {
            CreateBoundary(
                "Outdoor Boundary - Parking North Extension",
                root.transform,
                new Vector3(parkingEastBoundaryX + northBoundaryExtensionLength * 0.5f,
                    floorY + BoundaryHeight * 0.5f, parkingMaxZ),
                new Vector3(northBoundaryExtensionLength, BoundaryHeight, 0.5f));
        }

        float openingHalfWidth = PathWidth * 0.5f + 0.35f;
        float innerStartZ = doorZ + EntranceFenceStartOffset;
        float innerEndZ = parkingCenterZ - openingHalfWidth - EntranceFenceEndInset;
        float eastSouthLength = parkingCenterZ - openingHalfWidth - parkingMinZ;
        if (eastSouthLength > 0.4f)
        {
            CreateBoundary(
                "Outdoor Boundary - Parking East South",
                root.transform,
                new Vector3(parkingEastBoundaryX, floorY + BoundaryHeight * 0.5f,
                    parkingMinZ + eastSouthLength * 0.5f),
                new Vector3(0.5f, BoundaryHeight, eastSouthLength));
        }

        float eastNorthLength = parkingMaxZ - (parkingCenterZ + openingHalfWidth);
        if (eastNorthLength > 0.4f)
        {
            CreateBoundary(
                "Outdoor Boundary - Parking East North",
                root.transform,
                new Vector3(parkingEastBoundaryX, floorY + BoundaryHeight * 0.5f,
                    parkingCenterZ + openingHalfWidth + eastNorthLength * 0.5f),
                new Vector3(0.5f, BoundaryHeight, eastNorthLength));
        }

        // The path uses the building wall as its inside edge. These two
        // colliders close the exposed side and leave a deliberate opening at
        // the door landing.
        CreateBoundary(
            "Outdoor Boundary - Path Outer",
            root.transform,
            new Vector3(outerPathX, floorY + BoundaryHeight * 0.5f,
                (pathSouthZ + pathNorthZ) * 0.5f),
            new Vector3(0.5f, BoundaryHeight, pathNorthZ - pathSouthZ));

        if (innerEndZ > innerStartZ)
        {
            CreateBoundary(
                "Outdoor Boundary - Path Inner",
                root.transform,
                new Vector3(innerPathX, floorY + BoundaryHeight * 0.5f,
                    (innerStartZ + innerEndZ) * 0.5f),
                new Vector3(0.5f, BoundaryHeight, innerEndZ - innerStartZ));
        }

        CreateBoundary(
            "Outdoor Boundary - Path South",
            root.transform,
            new Vector3((roomEast + outerPathX) * 0.5f, floorY + BoundaryHeight * 0.5f, pathSouthZ),
            new Vector3(outerPathX - roomEast + 0.8f, BoundaryHeight, 0.5f));

        CreateBoundaryVisuals(
            root.transform,
            floorY,
            parkingMinX,
            parkingMaxX,
            parkingCenterZ,
            roomEast,
            outerPathX,
            pathSouthZ,
            pathNorthZ,
            parkingMinZ,
            parkingMaxZ,
            parkingEastBoundaryX,
            innerPathX,
            innerStartZ,
            innerEndZ,
            boundaryMaterial,
            boundaryTrimMaterial,
            boundaryRibMaterial);
        ValidateCourtyardSurface(
            root,
            floorY,
            courtyardMinX,
            courtyardMaxX,
            courtyardMinZ,
            courtyardMaxZ);

        ParkingBounds = new Bounds(
            new Vector3(roomFloor.center.x, floorY, parkingCenterZ),
            new Vector3(parkingWidth, BoundaryHeight, ParkingDepth));
        AccessibleBounds = new Bounds(
            new Vector3((courtyardMinX + courtyardMaxX) * 0.5f, floorY, (courtyardMinZ + courtyardMaxZ) * 0.5f),
            new Vector3(courtyardMaxX - courtyardMinX, BoundaryHeight, courtyardMaxZ - courtyardMinZ));
        IsBuilt = true;

        Debug.Log(
            $"GYMCHAOS_OUTDOOR_OK parkingCenter={ParkingBounds.center} " +
            $"parkingSize={ParkingBounds.size} door={doorway.DoorCenter} " +
            $"pathWidth={PathWidth:F2} boundaryHeight={BoundaryHeight:F2} " +
            "parkingLines=white parkingLights=4 vehicles=0 " +
            "courtyard=filled visibleShell=1",
            root);
    }

    private static void CreateParkingMarkings(
        Transform parent,
        float floorY,
        float minX,
        float maxX,
        float centerZ,
        Material markingMaterial)
    {
        const float aisleDepth = 4.2f;
        float halfDepth = ParkingDepth * 0.5f;
        float southRowOuterZ = centerZ - halfDepth;
        float southRowAisleZ = centerZ - aisleDepth * 0.5f;
        float northRowAisleZ = centerZ + aisleDepth * 0.5f;
        float northRowOuterZ = centerZ + halfDepth;
        float southRowLineLength = southRowAisleZ - southRowOuterZ - 0.5f;
        float northRowLineLength = northRowOuterZ - northRowAisleZ - 0.5f;
        int bayCount = Mathf.Clamp(Mathf.FloorToInt((maxX - minX) / 4.2f), 4, 9);
        float usableWidth = maxX - minX - 1.4f;
        float bayStep = usableWidth / bayCount;
        float startX = minX + 0.7f;

        for (int i = 0; i <= bayCount; i++)
        {
            CreateBox(
                "Parking South Bay Line",
                parent,
                new Vector3(startX + bayStep * i, floorY + 0.025f,
                    (southRowOuterZ + southRowAisleZ) * 0.5f),
                new Vector3(0.075f, 0.035f, southRowLineLength),
                markingMaterial,
                false);
            CreateBox(
                "Parking North Bay Line",
                parent,
                new Vector3(startX + bayStep * i, floorY + 0.025f,
                    (northRowAisleZ + northRowOuterZ) * 0.5f),
                new Vector3(0.075f, 0.035f, northRowLineLength),
                markingMaterial,
                false);
        }

        CreateBox(
            "Parking South Outer Line",
            parent,
            new Vector3((minX + maxX) * 0.5f, floorY + 0.026f, southRowOuterZ + 0.48f),
            new Vector3(maxX - minX - 1.1f, 0.035f, 0.075f),
            markingMaterial,
            false);
        CreateBox(
            "Parking South Aisle Line",
            parent,
            new Vector3((minX + maxX) * 0.5f, floorY + 0.026f, southRowAisleZ - 0.18f),
            new Vector3(maxX - minX - 1.1f, 0.035f, 0.075f),
            markingMaterial,
            false);
        CreateBox(
            "Parking North Aisle Line",
            parent,
            new Vector3((minX + maxX) * 0.5f, floorY + 0.026f, northRowAisleZ + 0.18f),
            new Vector3(maxX - minX - 1.1f, 0.035f, 0.075f),
            markingMaterial,
            false);
        CreateBox(
            "Parking North Outer Line",
            parent,
            new Vector3((minX + maxX) * 0.5f, floorY + 0.026f, northRowOuterZ - 0.48f),
            new Vector3(maxX - minX - 1.1f, 0.035f, 0.075f),
            markingMaterial,
            false);
    }

    private static void CreatePathEdge(
        Transform parent,
        float floorY,
        float pathCenterX,
        float doorZ,
        float parkingCenterZ,
        Material markingMaterial)
    {
        float offset = PathWidth * 0.5f - 0.22f;
        float pathCenterZ = (doorZ + parkingCenterZ) * 0.5f;
        float pathLength = Mathf.Abs(parkingCenterZ - doorZ) + 0.6f;
        CreateBox(
            "Path Edge Marking",
            parent,
            new Vector3(pathCenterX - offset, floorY + 0.026f, pathCenterZ),
            new Vector3(0.08f, 0.035f, pathLength),
            markingMaterial,
            false);
        CreateBox(
            "Path Edge Marking",
            parent,
            new Vector3(pathCenterX + offset, floorY + 0.026f, pathCenterZ),
            new Vector3(0.08f, 0.035f, pathLength),
            markingMaterial,
            false);
    }

    private static void CreateParkingCurb(
        Transform parent,
        float floorY,
        float minX,
        float maxX,
        float centerZ,
        Material curbMaterial)
    {
        float halfDepth = ParkingDepth * 0.5f;
        CreateBox(
            "Parking North Curb",
            parent,
            new Vector3((minX + maxX) * 0.5f, floorY + 0.08f, centerZ + halfDepth - 0.22f),
            new Vector3(maxX - minX, 0.16f, 0.24f),
            curbMaterial,
            false);
        CreateBox(
            "Parking West Curb",
            parent,
            new Vector3(minX + 0.22f, floorY + 0.08f, centerZ),
            new Vector3(0.24f, 0.16f, ParkingDepth),
            curbMaterial,
            false);
        CreateBox(
            "Parking South Curb",
            parent,
            new Vector3((minX + maxX) * 0.5f, floorY + 0.08f, centerZ - halfDepth + 0.22f),
            new Vector3(maxX - minX, 0.16f, 0.24f),
            curbMaterial,
            false);
    }

    private static void CreateParkingDetails(
        Transform parent,
        float floorY,
        float minX,
        float maxX,
        float centerZ,
        Material lampMaterial,
        Material lampFixtureMaterial,
        Material wheelStopMaterial)
    {
        float halfDepth = ParkingDepth * 0.5f;
        float parkingGroundY = floorY + ParkingSurfaceOffset;
        float poleY = parkingGroundY + ParkingLightBaseHeight + ParkingLightPoleHeight * 0.5f;
        float parkingCenterX = (minX + maxX) * 0.5f;
        float[] poleXs = { minX + 1.55f, maxX - 1.55f };
        float[] poleZs = { centerZ - halfDepth + 0.8f, centerZ + halfDepth - 0.8f };

        for (int xIndex = 0; xIndex < poleXs.Length; xIndex++)
        {
            for (int zIndex = 0; zIndex < poleZs.Length; zIndex++)
            {
                float x = poleXs[xIndex];
                float z = poleZs[zIndex];
                GameObject pole = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                pole.name = "Parking Light Pole";
                pole.transform.SetParent(parent, true);
                pole.transform.position = new Vector3(x, poleY, z);
                pole.transform.localScale = new Vector3(0.09f, ParkingLightPoleHeight * 0.5f, 0.09f);
                Renderer poleRenderer = pole.GetComponent<Renderer>();
                if (poleRenderer != null)
                {
                    poleRenderer.sharedMaterial = lampMaterial;
                }
                Object.Destroy(pole.GetComponent<Collider>());

                CreateCylinder(
                    "Parking Light Pole Base",
                    parent,
                    new Vector3(x, parkingGroundY + ParkingLightBaseHeight * 0.5f, z),
                    new Vector3(0.44f, ParkingLightBaseHeight * 0.5f, 0.44f),
                    lampMaterial);
                // The base must sit on the parking slab, not on an inferred
                // world origin. Keep a visible foot so the pole reads as
                // physically planted in the ground.
                Vector3 fixtureOffset = new Vector3(
                    x < parkingCenterX ? 0.42f : -0.42f,
                    0f,
                    z < centerZ ? 0.22f : -0.22f);
                Vector3 poleTop = new Vector3(
                    x,
                    parkingGroundY + ParkingLightBaseHeight + ParkingLightPoleHeight,
                    z);
                Vector3 fixturePosition = poleTop + fixtureOffset;
                CreateCylinderBetween(
                    "Parking Light Arm",
                    parent,
                    poleTop,
                    fixturePosition,
                    0.055f,
                    lampMaterial);
                CreateBox(
                    "Parking Light Head",
                    parent,
                    fixturePosition + Vector3.up * 0.06f,
                    new Vector3(0.62f, 0.12f, 0.34f),
                    lampFixtureMaterial,
                    false);

                CreateBox(
                    "Parking Light Cap",
                    parent,
                    fixturePosition + Vector3.up * 0.16f,
                    new Vector3(0.74f, 0.06f, 0.4f),
                    lampMaterial,
                    false);

                GameObject lightObject = new GameObject("Parking Light Source");
                lightObject.transform.SetParent(pole.transform, true);
                lightObject.transform.position = fixturePosition;
                Light parkingLight = lightObject.AddComponent<Light>();
                parkingLight.type = LightType.Point;
                parkingLight.color = new Color(0.68f, 0.84f, 1f);
                parkingLight.intensity = 7.5f;
                parkingLight.range = 12f;
                parkingLight.shadows = LightShadows.None;
            }
        }

        int bayCount = Mathf.Clamp(Mathf.FloorToInt((maxX - minX) / 4.2f), 4, 9);
        float usableWidth = maxX - minX - 1.4f;
        float bayStep = usableWidth / bayCount;
        float startX = minX + 0.7f;
        float aisleDepth = 4.2f;
        float southWheelStopZ = centerZ - aisleDepth * 0.5f - 0.75f;
        float northWheelStopZ = centerZ + aisleDepth * 0.5f + 0.75f;
        for (int i = 0; i < bayCount; i++)
        {
            CreateBox(
                "Parking South Wheel Stop",
                parent,
                new Vector3(startX + bayStep * (i + 0.5f), floorY + 0.11f, southWheelStopZ),
                new Vector3(Mathf.Min(1.2f, bayStep * 0.55f), 0.16f, 0.24f),
                wheelStopMaterial,
                false);
            CreateBox(
                "Parking North Wheel Stop",
                parent,
                new Vector3(startX + bayStep * (i + 0.5f), floorY + 0.11f, northWheelStopZ),
                new Vector3(Mathf.Min(1.2f, bayStep * 0.55f), 0.16f, 0.24f),
                wheelStopMaterial,
                false);
        }
    }

    private static void CreateParkingSiteDetails(
        Transform parent,
        float floorY,
        float minX,
        float maxX,
        float centerZ,
        float pathCenterX,
        float doorZ,
        Material planterMaterial,
        Material foliageMaterial,
        Material signMaterial,
        Material markingMaterial,
        Material metalMaterial)
    {
        float halfDepth = ParkingDepth * 0.5f;
        float planterZ = centerZ + halfDepth - 0.85f;
        CreateBox(
            "Parking North Planter West",
            parent,
            new Vector3(minX + 2.4f, floorY + 0.36f, planterZ),
            new Vector3(4.2f, 0.72f, 0.9f),
            planterMaterial,
            true);
        CreateBox(
            "Parking North Planter East",
            parent,
            new Vector3(maxX - 2.4f, floorY + 0.36f, planterZ),
            new Vector3(4.2f, 0.72f, 0.9f),
            planterMaterial,
            true);
        CreateFoliageCluster(
            parent,
            new Vector3(minX + 2.4f, floorY + 0.78f, planterZ),
            foliageMaterial,
            "Parking Planter West Foliage");
        CreateFoliageCluster(
            parent,
            new Vector3(maxX - 2.4f, floorY + 0.78f, planterZ),
            foliageMaterial,
            "Parking Planter East Foliage");

        int dashCount = Mathf.Clamp(Mathf.FloorToInt((maxX - minX - 5f) / 5f), 3, 8);
        float dashStep = (maxX - minX - 4f) / dashCount;
        for (int i = 0; i < dashCount; i++)
        {
            CreateBox(
                "Parking Aisle Dash",
                parent,
                new Vector3(minX + 2f + dashStep * (i + 0.5f), floorY + 0.03f, centerZ),
                new Vector3(Mathf.Min(2.2f, dashStep * 0.55f), 0.035f, 0.09f),
                markingMaterial,
                false);
        }
        CreateGroundArrow(
            parent,
            new Vector3(pathCenterX, floorY + 0.05f, centerZ),
            Vector3.left,
            2.4f,
            0.78f,
            markingMaterial,
            "Parking Aisle Direction Arrow");

        CreateParkingSign(
            parent,
            new Vector3(pathCenterX - 0.95f, floorY, centerZ - 2.5f),
            signMaterial,
            markingMaterial);
        CreateDrainGrate(
            parent,
            new Vector3(pathCenterX, floorY + 0.025f, doorZ + 1.85f),
            metalMaterial,
            "Gym Door Drain Grate");

        float bollardZ = doorZ - 2.45f;
        CreateBollard(
            parent,
            new Vector3(pathCenterX - 1.55f, floorY, bollardZ),
            metalMaterial,
            "Door Approach Bollard Left");
        CreateBollard(
            parent,
            new Vector3(pathCenterX + 1.55f, floorY, bollardZ),
            metalMaterial,
            "Door Approach Bollard Right");
    }

    private static void CreateFoliageCluster(
        Transform parent,
        Vector3 center,
        Material foliageMaterial,
        string name)
    {
        float[] offsets = { -1.15f, 0f, 1.15f };
        for (int i = 0; i < offsets.Length; i++)
        {
            GameObject shrub = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            shrub.name = name + " Shrub";
            shrub.transform.SetParent(parent, true);
            shrub.transform.position = center + new Vector3(offsets[i], 0.25f + (i % 2) * 0.12f, 0f);
            shrub.transform.localScale = new Vector3(0.48f, 0.55f + (i % 2) * 0.12f, 0.48f);
            Renderer renderer = shrub.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.sharedMaterial = foliageMaterial;
            }
            Object.Destroy(shrub.GetComponent<Collider>());
        }
    }

    private static void CreateParkingSign(
        Transform parent,
        Vector3 basePosition,
        Material signMaterial,
        Material letteringMaterial)
    {
        GameObject signRoot = new GameObject("Parking Sign - Exterior Only");
        signRoot.transform.SetParent(parent, true);
        signRoot.transform.position = basePosition;

        CreateCylinder(
            "Parking Sign Post",
            signRoot.transform,
            basePosition + Vector3.up * 1.05f,
            new Vector3(0.06f, 1.05f, 0.06f),
            signMaterial);
        CreateBox(
            "Parking Sign Face",
            signRoot.transform,
            basePosition + Vector3.up * 2.2f,
            new Vector3(1.05f, 0.82f, 0.08f),
            signMaterial,
            false);
        CreateBox(
            "Parking Sign Border",
            signRoot.transform,
            basePosition + new Vector3(0f, 2.2f, -0.045f),
            new Vector3(0.82f, 0.58f, 0.025f),
            letteringMaterial,
            false);

        GameObject labelObject = new GameObject("Parking Sign Letter P");
        labelObject.transform.SetParent(signRoot.transform, true);
        labelObject.transform.position = basePosition + new Vector3(0f, 2.2f, -0.055f);
        labelObject.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
        TextMesh label = labelObject.AddComponent<TextMesh>();
        Font legacyFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (legacyFont != null)
        {
            label.font = legacyFont;
        }
        label.text = "P";
        label.anchor = TextAnchor.MiddleCenter;
        label.alignment = TextAlignment.Center;
        label.fontSize = 96;
        label.characterSize = 0.62f;
        label.color = Color.white;

        signRoot.AddComponent<GymExteriorOnlyVisual>();
    }

    private static void CreateDrainGrate(
        Transform parent,
        Vector3 center,
        Material metalMaterial,
        string name)
    {
        CreateBox(
            name,
            parent,
            center,
            new Vector3(PathWidth - 0.7f, 0.035f, 0.62f),
            metalMaterial,
            false);
        for (int i = -3; i <= 3; i++)
        {
            CreateBox(
                name + " Bar",
                parent,
                center + new Vector3(i * 0.38f, 0.025f, 0f),
                new Vector3(0.06f, 0.055f, 0.7f),
                metalMaterial,
                false);
        }
    }

    private static void CreateBollard(
        Transform parent,
        Vector3 basePosition,
        Material material,
        string name)
    {
        CreateCylinder(
            name + " Base",
            parent,
            basePosition + Vector3.up * 0.08f,
            new Vector3(0.24f, 0.08f, 0.24f),
            material);
        CreateCylinder(
            name,
            parent,
            basePosition + Vector3.up * 0.62f,
            new Vector3(0.1f, 0.54f, 0.1f),
            material);
    }

    private static void CreateGroundArrow(
        Transform parent,
        Vector3 position,
        Vector3 direction,
        float length,
        float width,
        Material material,
        string name)
    {
        GameObject arrow = new GameObject(name);
        arrow.transform.SetParent(parent, true);
        arrow.transform.position = position;
        arrow.transform.rotation = Quaternion.LookRotation(direction, Vector3.up);
        MeshFilter filter = arrow.AddComponent<MeshFilter>();
        MeshRenderer renderer = arrow.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        float halfWidth = width * 0.5f;
        float stemWidth = width * 0.18f;
        float halfLength = length * 0.5f;
        Vector3[] vertices =
        {
            new Vector3(-stemWidth, 0f, -halfLength),
            new Vector3(stemWidth, 0f, -halfLength),
            new Vector3(stemWidth, 0f, 0.03f),
            new Vector3(halfWidth, 0f, 0.03f),
            new Vector3(0f, 0f, halfLength),
            new Vector3(-halfWidth, 0f, 0.03f),
            new Vector3(-stemWidth, 0f, 0.03f)
        };
        filter.sharedMesh = CreateFlatPolygonMesh(vertices, name + " Mesh");
    }

    private static Mesh CreateFlatPolygonMesh(Vector3[] vertices, string name)
    {
        Mesh mesh = new Mesh { name = name };
        mesh.vertices = vertices;
        int[] triangles = new int[(vertices.Length - 2) * 3];
        for (int i = 0; i < vertices.Length - 2; i++)
        {
            triangles[i * 3] = 0;
            triangles[i * 3 + 1] = i + 2;
            triangles[i * 3 + 2] = i + 1;
        }
        mesh.triangles = triangles;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    private static void CreateBoundaryVisuals(
        Transform parent,
        float floorY,
        float parkingMinX,
        float parkingMaxX,
        float parkingCenterZ,
        float roomEast,
        float outerPathX,
        float pathSouthZ,
        float pathNorthZ,
        float parkingMinZ,
        float parkingMaxZ,
        float parkingEastBoundaryX,
        float innerPathX,
        float innerStartZ,
        float innerEndZ,
        Material boundaryMaterial,
        Material boundaryTrimMaterial,
        Material boundaryRibMaterial)
    {
        float boundaryY = floorY + BoundaryHeight * 0.5f;
        float parkingCenterX = (parkingMinX + parkingMaxX) * 0.5f;
        CreateVisibleBoundary(
            "Parking North Boundary Wall",
            parent,
            new Vector3(parkingCenterX, boundaryY, parkingMaxZ),
            new Vector3(parkingMaxX - parkingMinX + 1.1f, BoundaryHeight, 0.5f),
            BoundaryHeight,
            floorY,
            boundaryMaterial,
            boundaryTrimMaterial,
            boundaryRibMaterial);
        CreateVisibleBoundary(
            "Parking South Boundary Guard",
            parent,
            new Vector3(parkingCenterX, boundaryY, parkingMinZ),
            new Vector3(parkingMaxX - parkingMinX + 1.1f, BoundaryHeight, 0.5f),
            1.35f,
            floorY,
            boundaryMaterial,
            boundaryTrimMaterial,
            boundaryRibMaterial);
        CreateVisibleBoundary(
            "Parking West Boundary Wall",
            parent,
            new Vector3(parkingMinX - 0.55f, boundaryY, parkingCenterZ),
            new Vector3(0.5f, BoundaryHeight, ParkingDepth + 1.1f),
            BoundaryHeight,
            floorY,
            boundaryMaterial,
            boundaryTrimMaterial,
            boundaryRibMaterial);

        float northExtensionLength = outerPathX - parkingEastBoundaryX;
        if (northExtensionLength > 0.4f)
        {
            CreateVisibleBoundary(
                "Parking North Extension Wall",
                parent,
                new Vector3(parkingEastBoundaryX + northExtensionLength * 0.5f,
                    boundaryY, parkingMaxZ),
                new Vector3(northExtensionLength, BoundaryHeight, 0.5f),
                BoundaryHeight,
                floorY,
                boundaryMaterial,
                boundaryTrimMaterial,
                boundaryRibMaterial);
        }

        float openingHalfWidth = PathWidth * 0.5f + 0.35f;
        float eastSouthLength = parkingCenterZ - openingHalfWidth - parkingMinZ;
        if (eastSouthLength > 0.4f)
        {
            CreateVisibleBoundary(
                "Parking East South Wall",
                parent,
                new Vector3(parkingEastBoundaryX, boundaryY,
                    parkingMinZ + eastSouthLength * 0.5f),
                new Vector3(0.5f, BoundaryHeight, eastSouthLength),
                BoundaryHeight,
                floorY,
                boundaryMaterial,
                boundaryTrimMaterial,
                boundaryRibMaterial);
        }

        float eastNorthLength = parkingMaxZ - (parkingCenterZ + openingHalfWidth);
        if (eastNorthLength > 0.4f)
        {
            CreateVisibleBoundary(
                "Parking East North Wall",
                parent,
                new Vector3(parkingEastBoundaryX, boundaryY,
                    parkingCenterZ + openingHalfWidth + eastNorthLength * 0.5f),
                new Vector3(0.5f, BoundaryHeight, eastNorthLength),
                BoundaryHeight,
                floorY,
                boundaryMaterial,
                boundaryTrimMaterial,
                boundaryRibMaterial);
        }

        CreateVisibleBoundary(
            "Path Outer Boundary Wall",
            parent,
            new Vector3(outerPathX, boundaryY, (pathSouthZ + pathNorthZ) * 0.5f),
            new Vector3(0.5f, BoundaryHeight, pathNorthZ - pathSouthZ),
            BoundaryHeight,
            floorY,
            boundaryMaterial,
            boundaryTrimMaterial,
            boundaryRibMaterial);

        if (innerEndZ > innerStartZ)
        {
            CreateVisibleBoundary(
                "Path Inner Boundary Guard",
                parent,
                new Vector3(innerPathX, boundaryY, (innerStartZ + innerEndZ) * 0.5f),
                new Vector3(0.5f, BoundaryHeight, innerEndZ - innerStartZ),
                2.3f,
                floorY,
                boundaryMaterial,
                boundaryTrimMaterial,
                boundaryRibMaterial);
        }

        CreateVisibleBoundary(
            "Path South Boundary Wall",
            parent,
            new Vector3((roomEast + outerPathX) * 0.5f, boundaryY, pathSouthZ),
            new Vector3(outerPathX - roomEast + 0.8f, BoundaryHeight, 0.5f),
            BoundaryHeight,
            floorY,
            boundaryMaterial,
            boundaryTrimMaterial,
            boundaryRibMaterial);
    }

    private static void CreateVisibleBoundary(
        string name,
        Transform parent,
        Vector3 position,
        Vector3 size,
        float visualHeight,
        float floorY,
        Material boundaryMaterial,
        Material boundaryTrimMaterial,
        Material boundaryRibMaterial)
    {
        float wallHeight = Mathf.Clamp(visualHeight, 0.8f, size.y);
        Vector3 wallPosition = new Vector3(position.x, floorY + wallHeight * 0.5f, position.z);
        Vector3 wallSize = new Vector3(size.x, wallHeight, size.z);
        CreateBox(name, parent, wallPosition, wallSize, boundaryMaterial, false);

        CreateBox(
            name + " Coping",
            parent,
            new Vector3(position.x, floorY + wallHeight + 0.08f, position.z),
            new Vector3(size.x + 0.16f, 0.16f, size.z + 0.16f),
            boundaryTrimMaterial,
            false);

        bool runsAlongX = size.x >= size.z;
        float runLength = runsAlongX ? size.x : size.z;
        int ribCount = Mathf.Clamp(Mathf.FloorToInt(runLength / 2.8f), 2, 14);
        for (int i = 0; i < ribCount; i++)
        {
            float t = (i + 0.5f) / ribCount - 0.5f;
            Vector3 ribPosition = position + (runsAlongX
                ? new Vector3(t * runLength, 0f, 0f)
                : new Vector3(0f, 0f, t * runLength));
            Vector3 ribSize = runsAlongX
                ? new Vector3(0.11f, Mathf.Max(0.55f, wallHeight - 0.22f), size.z + 0.025f)
                : new Vector3(size.x + 0.025f, Mathf.Max(0.55f, wallHeight - 0.22f), 0.11f);
            ribPosition.y = floorY + wallHeight * 0.5f;
            CreateBox(name + " Vertical Rib", parent, ribPosition, ribSize, boundaryRibMaterial, false);
        }
    }

    private static void ValidateCourtyardSurface(
        GameObject root,
        float floorY,
        float minX,
        float maxX,
        float minZ,
        float maxZ)
    {
        Physics.SyncTransforms();
        const int xSamples = 7;
        const int zSamples = 9;
        int sampleCount = 0;
        int missingSamples = 0;
        float xInset = Mathf.Min(0.75f, (maxX - minX) * 0.12f);
        float zInset = Mathf.Min(0.75f, (maxZ - minZ) * 0.12f);
        for (int xIndex = 0; xIndex < xSamples; xIndex++)
        {
            float x = Mathf.Lerp(minX + xInset, maxX - xInset,
                xIndex / (float)(xSamples - 1));
            for (int zIndex = 0; zIndex < zSamples; zIndex++)
            {
                float z = Mathf.Lerp(minZ + zInset, maxZ - zInset,
                    zIndex / (float)(zSamples - 1));
                sampleCount++;
                Ray ray = new Ray(new Vector3(x, floorY + 4.5f, z), Vector3.down);
                RaycastHit[] hits = Physics.RaycastAll(ray, 8f);
                bool foundCourtyardSurface = false;
                for (int hitIndex = 0; hitIndex < hits.Length; hitIndex++)
                {
                    Collider hitCollider = hits[hitIndex].collider;
                    if (hitCollider != null && hitCollider.transform.IsChildOf(root.transform))
                    {
                        foundCourtyardSurface = true;
                        break;
                    }
                }
                if (!foundCourtyardSurface)
                {
                    missingSamples++;
                }
            }
        }

        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        int visibleBoundaryRenderers = 0;
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] != null && renderers[i].name.Contains("Boundary"))
            {
                visibleBoundaryRenderers++;
            }
        }

        Transform[] outdoorTransforms = root.GetComponentsInChildren<Transform>(true);
        int parkingLightCount = 0;
        int groundedLightBaseCount = 0;
        int groundedLightPoleCount = 0;
        float largestLightBaseGap = 0f;
        float largestLightPoleGap = 0f;
        int parkingSignGateCount = 0;
        for (int i = 0; i < outdoorTransforms.Length; i++)
        {
            Transform outdoorTransform = outdoorTransforms[i];
            if (outdoorTransform == null)
            {
                continue;
            }

            if (outdoorTransform.name == "Parking Light Pole")
            {
                parkingLightCount++;
                Renderer poleRenderer = outdoorTransform.GetComponent<Renderer>();
                if (poleRenderer != null)
                {
                    float expectedPoleBottom = floorY + ParkingSurfaceOffset +
                        ParkingLightBaseHeight;
                    float poleGap = Mathf.Abs(
                        poleRenderer.bounds.min.y - expectedPoleBottom);
                    largestLightPoleGap = Mathf.Max(largestLightPoleGap, poleGap);
                    if (poleGap <= 0.035f)
                    {
                        groundedLightPoleCount++;
                    }
                }
            }

            if (outdoorTransform.name == "Parking Light Pole Base")
            {
                Renderer baseRenderer = outdoorTransform.GetComponent<Renderer>();
                if (baseRenderer != null)
                {
                    float expectedGroundY = floorY + ParkingSurfaceOffset;
                    float baseGap = Mathf.Abs(
                        baseRenderer.bounds.min.y - expectedGroundY);
                    largestLightBaseGap = Mathf.Max(largestLightBaseGap, baseGap);
                    if (baseGap <= 0.035f)
                    {
                        groundedLightBaseCount++;
                    }
                }
            }

            if (outdoorTransform.name == "Parking Sign - Exterior Only" &&
                outdoorTransform.GetComponent<GymExteriorOnlyVisual>() != null)
            {
                parkingSignGateCount++;
            }
        }

        Light[] allLights = Object.FindObjectsByType<Light>(FindObjectsSortMode.None);
        int activeDirectionalLights = 0;
        for (int i = 0; i < allLights.Length; i++)
        {
            if (allLights[i] != null && allLights[i].enabled &&
                allLights[i].type == LightType.Directional)
            {
                activeDirectionalLights++;
            }
        }

        bool duplicateLandingRemoved = GameObject.Find("Visitor Door Exterior Landing") == null;
        Transform arrowTransform = root.transform.Find("Parking Aisle Direction Arrow");
        MeshFilter arrowFilter = arrowTransform != null
            ? arrowTransform.GetComponent<MeshFilter>()
            : null;
        bool arrowContractPassed = arrowFilter != null && arrowFilter.sharedMesh != null &&
            arrowFilter.sharedMesh.vertexCount == 7;

        Transform innerVisual = root.transform.Find("Path Inner Boundary Guard");
        Transform innerCollider = root.transform.Find("Outdoor Boundary - Path Inner");
        Renderer innerVisualRenderer = innerVisual != null
            ? innerVisual.GetComponent<Renderer>()
            : null;
        BoxCollider innerColliderComponent = innerCollider != null
            ? innerCollider.GetComponent<BoxCollider>()
            : null;
        bool entranceFenceContractPassed = innerVisualRenderer != null &&
            innerColliderComponent != null &&
            Mathf.Abs(innerVisualRenderer.bounds.center.x - innerColliderComponent.bounds.center.x) <= 0.035f &&
            Mathf.Abs(innerVisualRenderer.bounds.center.z - innerColliderComponent.bounds.center.z) <= 0.035f &&
            Mathf.Abs(innerVisualRenderer.bounds.size.z - innerColliderComponent.bounds.size.z) <= 0.035f;

        Debug.Log(
            $"GYMCHAOS_OUTDOOR_SURFACE_OK samples={sampleCount} " +
            $"surfaceHoles={missingSamples} visibleBoundaryRenderers={visibleBoundaryRenderers}",
            root);
        bool contractPassed = missingSamples == 0 &&
            parkingLightCount == 4 &&
            groundedLightBaseCount == parkingLightCount &&
            groundedLightPoleCount == parkingLightCount &&
            largestLightBaseGap <= 0.035f &&
            largestLightPoleGap <= 0.035f &&
            parkingSignGateCount == 1 &&
            arrowContractPassed &&
            entranceFenceContractPassed &&
            activeDirectionalLights <= 1 &&
            duplicateLandingRemoved;
        Debug.Log(
            $"GYMCHAOS_OUTDOOR_CONTRACT_{(contractPassed ? "OK" : "FAIL")} " +
            $"lights={parkingLightCount} groundedBases={groundedLightBaseCount} " +
            $"groundedPoles={groundedLightPoleCount} maxBaseGap={largestLightBaseGap:F3} " +
            $"maxPoleGap={largestLightPoleGap:F3} signGate={parkingSignGateCount} " +
            $"arrow={arrowContractPassed} entranceFence={entranceFenceContractPassed} " +
            $"activeDirectional={activeDirectionalLights} " +
            $"duplicateLandingRemoved={duplicateLandingRemoved}",
            root);
    }

    private static void CreateBoundary(
        string name,
        Transform parent,
        Vector3 position,
        Vector3 size)
    {
        GameObject boundary = new GameObject(name);
        boundary.transform.SetParent(parent, true);
        boundary.transform.position = position;
        BoxCollider collider = boundary.AddComponent<BoxCollider>();
        collider.size = size;
        collider.isTrigger = false;
    }

    private static GameObject CreateCylinder(
        string name,
        Transform parent,
        Vector3 position,
        Vector3 scale,
        Material material)
    {
        GameObject cylinder = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        cylinder.name = name;
        cylinder.transform.SetParent(parent, true);
        cylinder.transform.position = position;
        cylinder.transform.localScale = scale;
        Renderer renderer = cylinder.GetComponent<Renderer>();
        if (renderer != null)
        {
            renderer.sharedMaterial = material;
        }
        Object.Destroy(cylinder.GetComponent<Collider>());
        return cylinder;
    }

    private static GameObject CreateCylinderBetween(
        string name,
        Transform parent,
        Vector3 start,
        Vector3 end,
        float radius,
        Material material)
    {
        Vector3 delta = end - start;
        GameObject cylinder = CreateCylinder(
            name,
            parent,
            (start + end) * 0.5f,
            new Vector3(radius, delta.magnitude * 0.5f, radius),
            material);
        if (delta.sqrMagnitude > 0.0001f)
        {
            cylinder.transform.rotation = Quaternion.FromToRotation(Vector3.up, delta.normalized);
        }
        return cylinder;
    }

    private static Material CreateMaterial(string name, Color color, float metallic, float smoothness)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
        {
            shader = Shader.Find("Standard");
        }

        Material material = new Material(shader);
        material.name = name;
        material.color = color;
        material.SetFloat("_Metallic", metallic);
        material.SetFloat("_Smoothness", smoothness);
        return material;
    }

    private static void SetEmission(Material material, Color emission)
    {
        material.EnableKeyword("_EMISSION");
        material.SetColor("_EmissionColor", emission);
    }

    private static GameObject CreateBox(
        string name,
        Transform parent,
        Vector3 position,
        Vector3 scale,
        Material material,
        bool keepCollider)
    {
        GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
        box.name = name;
        box.transform.SetParent(parent, true);
        box.transform.position = position;
        box.transform.localScale = scale;
        Renderer renderer = box.GetComponent<Renderer>();
        if (renderer != null)
        {
            renderer.sharedMaterial = material;
        }

        if (!keepCollider)
        {
            Object.Destroy(box.GetComponent<Collider>());
        }

        return box;
    }
}

/// <summary>
/// Gates a world-space exterior prop by the player's side of the gym shell.
/// This prevents transparent/legacy text rendering from leaking an outdoor
/// marker through the gym while keeping the same marker available in the
/// reachable courtyard.
/// </summary>
[DefaultExecutionOrder(100)]
public sealed class GymExteriorOnlyVisual : MonoBehaviour
{
    private Renderer[] gatedRenderers;
    private PlayerMovement player;
    private bool visibilityApplied;
    private bool visible;

    private void Awake()
    {
        gatedRenderers = GetComponentsInChildren<Renderer>(true);
        player = Object.FindAnyObjectByType<PlayerMovement>();
        ApplyVisibility(false);
    }

    private void LateUpdate()
    {
        if (player == null)
        {
            player = Object.FindAnyObjectByType<PlayerMovement>();
        }

        bool shouldBeVisible = player != null &&
            GymOutdoorBuilder.IsPlayerOutsideGym(player.transform.position);
        ApplyVisibility(shouldBeVisible);
    }

    private void ApplyVisibility(bool shouldBeVisible)
    {
        if (visibilityApplied && visible == shouldBeVisible)
        {
            return;
        }

        if (gatedRenderers != null)
        {
            for (int i = 0; i < gatedRenderers.Length; i++)
            {
                if (gatedRenderers[i] != null)
                {
                    gatedRenderers[i].enabled = shouldBeVisible;
                }
            }
        }

        visible = shouldBeVisible;
        visibilityApplied = true;
    }
}
