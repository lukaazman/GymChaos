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
    private const float ParkingDepth = 18f;
    private const float ParkingAisleDepth = 6.8f;
    private const float ParkingLineInset = 0.7f;
    private const float PathWidth = 4.4f;
    private const float VehicleRoadWidth = 7.6f;
    private const float VehicleLaneOffset = 1.65f;
    private const float BoundaryHeight = 5.2f;
    private const float EntranceFenceStartOffset = 3.25f;
    private const float EntranceFenceEndInset = 0.2f;
    private const float InnerBoundaryWallOffset = 0.42f;
    private const float ExteriorWallFaceOffset = 0.2f;
    private const float ParkingSurfaceOffset = 0.01f;
    private const float ParkingLightBaseHeight = 0.22f;
    private const float ParkingLightPoleHeight = 5.4f;

    public static bool IsBuilt { get; private set; }
    public static Bounds ParkingBounds { get; private set; }
    public static Bounds AccessibleBounds { get; private set; }
    public static Vector3 VehicleRoadSpawnPoint { get; private set; }
    public static Vector3 VehicleRoadJunctionPoint { get; private set; }
    public static Vector3 VehicleRoadTurnPoint { get; private set; }
    public static Vector3 VehicleArrivalRoadSpawnPoint { get; private set; }
    public static Vector3 VehicleArrivalRoadTurnPoint { get; private set; }
    public static Vector3 VehicleArrivalRoadJunctionPoint { get; private set; }
    public static Vector3 VehicleDepartureRoadSpawnPoint { get; private set; }
    public static Vector3 VehicleDepartureRoadTurnPoint { get; private set; }
    public static Vector3 VehicleDepartureRoadJunctionPoint { get; private set; }
    public static Vector3 VisitorParkingTurnPoint { get; private set; }
    public static Vector3 VisitorParkingEntryPoint { get; private set; }
    public static int ParkingBayCount { get; private set; }
    public static float ParkingBayStep { get; private set; }
    public static float ParkingBayStartX { get; private set; }
    public static float ParkingStallCenterOffset { get; private set; }
    public static float ParkingVehicleTargetLength => 4.0f;

    public static float GetParkingBayCenterX(int column)
    {
        int count = Mathf.Max(1, ParkingBayCount);
        int safeColumn = Mathf.Clamp(column, 0, count - 1);
        float step = ParkingBayStep > 0.01f
            ? ParkingBayStep
            : Mathf.Max(1f, (ParkingBounds.size.x - 2f * ParkingLineInset) / count);
        float start = ParkingBayStartX != 0f
            ? ParkingBayStartX
            : ParkingBounds.min.x + ParkingLineInset;
        return start + step * (safeColumn + 0.5f);
    }

    public static float GetParkingStallCenterZ(float rowSign)
    {
        float offset = ParkingStallCenterOffset > 0.01f
            ? ParkingStallCenterOffset
            : (ParkingDepth * 0.5f + ParkingAisleDepth * 0.5f) * 0.5f;
        return ParkingBounds.center.z + Mathf.Sign(rowSign) * offset;
    }

    public static bool IsPlayerOutsideGym(Vector3 position)
    {
        if (GymBackRoomBuilder.IsInsideRoom(position))
        {
            return false;
        }

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
        float parkingWidth = Mathf.Clamp(roomFloor.size.x * 0.82f, 28f, 44f);
        float parkingCenterZ = roomNorth + ParkingDepth * 0.5f + 1f;
        float parkingMinX = roomFloor.center.x - parkingWidth * 0.5f;
        float parkingMaxX = roomFloor.center.x + parkingWidth * 0.5f;
        float doorZ = doorway.ExteriorPoint.z;
        float pathCenterX = roomEast + 4.5f;
        float pathSouthZ = doorZ - 3.15f;
        float pathNorthZ = parkingCenterZ + ParkingDepth * 0.5f + 0.55f;
        float pathLength = Mathf.Max(0.5f, parkingCenterZ - doorZ);
        float outerPathX = pathCenterX + PathWidth * 0.5f + 0.55f;
        float exteriorWallX = roomEast + ExteriorWallFaceOffset;
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
        GameObject doorPath = CreateBox(
            "Path from Gym Door",
            root.transform,
            new Vector3(pathCenterX, floorY - 0.11f, doorZ + pathLength * 0.5f),
            new Vector3(PathWidth, 0.24f, pathLength + 0.8f),
            pathMaterial,
            false);
        doorPath.AddComponent<GymExteriorOnlyVisual>();

        float horizontalMinX = parkingMaxX - 0.9f;
        float horizontalMaxX = outerPathX;
        GameObject parkingTurn = CreateBox(
            "Parking Path Turn",
            root.transform,
            new Vector3((horizontalMinX + horizontalMaxX) * 0.5f, floorY - 0.11f, parkingCenterZ),
            new Vector3(horizontalMaxX - horizontalMinX, 0.24f, VehicleRoadWidth),
            pathMaterial,
            false);
        parkingTurn.AddComponent<GymExteriorOnlyVisual>();
        float visitorLaneX = Mathf.Min(outerPathX - 0.8f, parkingMaxX + 1.8f);
        VisitorParkingTurnPoint = new Vector3(
            visitorLaneX, floorY, parkingCenterZ);
        VisitorParkingEntryPoint = new Vector3(
            parkingMaxX - 2.0f, floorY, parkingCenterZ);

        float doorLandingMaxX = outerPathX + 0.4f;
        GameObject doorLanding = CreateBox(
            "Black Door Landing",
            root.transform,
            new Vector3((exteriorWallX + doorLandingMaxX) * 0.5f, floorY - 0.11f, doorZ),
            new Vector3(doorLandingMaxX - exteriorWallX, 0.24f, 6.2f),
            pathMaterial,
            false);
        doorLanding.AddComponent<GymExteriorOnlyVisual>();

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

        CreateNeighbourhood(root.transform, roomFloor.center.x, floorY, parkingCenterZ, parkingWidth,
            parkingMaxX + 42f,
            landscape, foliageMaterial, curbMaterial, boundaryMaterial, lampMaterial);

        // Visible road continues east through the parking opening. Cars can
        // cross the invisible blocker; the player cannot enter traffic.
        const float roadLength = 42f;
        const float roadWidth = VehicleRoadWidth;
        float roadStartX = parkingMaxX - 0.4f;
        float roadEndX = roadStartX + roadLength;
        Vector3 roadCenter = new Vector3(
            (roadStartX + roadEndX) * 0.5f, floorY - 0.09f, parkingCenterZ);
        CreateBox("Visitor Vehicle Road", root.transform, roadCenter,
            new Vector3(roadLength, 0.18f, roadWidth), asphalt, false);
        CreateBox("Road Center Line", root.transform,
            new Vector3(roadCenter.x, floorY + 0.025f, parkingCenterZ),
            new Vector3(roadLength - 1f, 0.035f, 0.09f), markingMaterial, false);
        CreateBox("Road North Shoulder", root.transform,
            new Vector3(roadCenter.x, floorY + 0.04f, parkingCenterZ + roadWidth * 0.5f),
            new Vector3(roadLength, 0.08f, 0.18f), curbMaterial, false);
        CreateBox("Road South Shoulder", root.transform,
            new Vector3(roadCenter.x, floorY + 0.04f, parkingCenterZ - roadWidth * 0.5f),
            new Vector3(roadLength, 0.08f, 0.18f), curbMaterial, false);
        VehicleRoadJunctionPoint = new Vector3(
            parkingMaxX + 4.8f, floorY + 0.08f, parkingCenterZ);
        VehicleRoadTurnPoint = new Vector3(
            roadEndX - 2f, floorY + 0.08f, parkingCenterZ);

        VehicleArrivalRoadJunctionPoint = VehicleRoadJunctionPoint +
            Vector3.forward * VehicleLaneOffset;
        VehicleDepartureRoadJunctionPoint = VehicleRoadJunctionPoint -
            Vector3.forward * VehicleLaneOffset;
        VehicleArrivalRoadTurnPoint = VehicleRoadTurnPoint +
            Vector3.left * VehicleLaneOffset + Vector3.forward * VehicleLaneOffset;
        VehicleDepartureRoadTurnPoint = VehicleRoadTurnPoint +
            Vector3.right * VehicleLaneOffset - Vector3.forward * VehicleLaneOffset;

        // Continue straight well beyond the player blocker, then turn behind
        // the far corner. From the accessible lot, departures visibly travel
        // into the distance and disappear only after completing the bend.
        Vector3 roadExit = new Vector3(
            roadEndX - 2f, floorY - 0.09f, parkingCenterZ + 18f);
        Vector3 extensionDelta = roadExit - VehicleRoadTurnPoint;
        Vector3 extensionDirection = Vector3.ProjectOnPlane(extensionDelta, Vector3.up).normalized;
        Vector3 extensionCenter = (VehicleRoadTurnPoint + roadExit) * 0.5f;
        float extensionLength = Vector3.ProjectOnPlane(extensionDelta, Vector3.up).magnitude;
        Quaternion extensionRotation = Quaternion.FromToRotation(Vector3.right, extensionDirection);
        GameObject extension = CreateBox("Visitor Vehicle Road Extension", root.transform,
            extensionCenter, new Vector3(extensionLength, 0.18f, roadWidth), asphalt, false);
        extension.transform.rotation = extensionRotation;
        GameObject extensionLine = CreateBox("Road Extension Center Line", root.transform,
            extensionCenter + Vector3.up * 0.11f,
            new Vector3(extensionLength - 0.5f, 0.035f, 0.09f), markingMaterial, false);
        extensionLine.transform.rotation = extensionRotation;
        Vector3 shoulderOffset = Vector3.Cross(Vector3.up, extensionDirection) * (roadWidth * 0.5f);
        GameObject extensionNorth = CreateBox("Road Extension North Shoulder", root.transform,
            extensionCenter + shoulderOffset + Vector3.up * 0.13f,
            new Vector3(extensionLength, 0.08f, 0.18f), curbMaterial, false);
        extensionNorth.transform.rotation = extensionRotation;
        GameObject extensionSouth = CreateBox("Road Extension South Shoulder", root.transform,
            extensionCenter - shoulderOffset + Vector3.up * 0.13f,
            new Vector3(extensionLength, 0.08f, 0.18f), curbMaterial, false);
        extensionSouth.transform.rotation = extensionRotation;
        VehicleRoadSpawnPoint = roadExit;
        VehicleArrivalRoadSpawnPoint = roadExit + Vector3.left * VehicleLaneOffset;
        VehicleDepartureRoadSpawnPoint = roadExit + Vector3.right * VehicleLaneOffset;

        // Continue the existing dark perimeter architecture beyond the
        // demolished visible wall. The original path-outer collider remains
        // across the opening as the invisible player limit; these corridor
        // walls are presentation only so vehicle transforms can complete the
        // hidden turn without physics jitter.
        float corridorStartX = outerPathX + 0.25f;
        float turnX = VehicleRoadTurnPoint.x;
        float insideCornerX = turnX - roadWidth * 0.5f;
        float outsideCornerX = turnX + roadWidth * 0.5f;
        float northStraightLength = Mathf.Max(0.5f, insideCornerX - corridorStartX);
        float southStraightLength = Mathf.Max(0.5f, outsideCornerX - corridorStartX);
        CreateVisibleBoundary(
            "Visitor Road North Wall", root.transform,
            new Vector3(corridorStartX + northStraightLength * 0.5f,
                floorY + BoundaryHeight * 0.5f,
                parkingCenterZ + roadWidth * 0.5f),
            new Vector3(northStraightLength, BoundaryHeight, 0.5f),
            BoundaryHeight, floorY,
            boundaryMaterial, boundaryTrimMaterial, boundaryRibMaterial);
        CreateVisibleBoundary(
            "Visitor Road South Wall", root.transform,
            new Vector3(corridorStartX + southStraightLength * 0.5f,
                floorY + BoundaryHeight * 0.5f,
                parkingCenterZ - roadWidth * 0.5f),
            new Vector3(southStraightLength, BoundaryHeight, 0.5f),
            BoundaryHeight, floorY,
            boundaryMaterial, boundaryTrimMaterial, boundaryRibMaterial);

        float cornerEndZ = roadExit.z + 2f;
        float westCornerStartZ = parkingCenterZ + roadWidth * 0.5f;
        float eastCornerStartZ = parkingCenterZ - roadWidth * 0.5f;
        float westCornerLength = cornerEndZ - westCornerStartZ;
        float eastCornerLength = cornerEndZ - eastCornerStartZ;
        CreateVisibleBoundary(
            "Visitor Road Corner West Wall", root.transform,
            new Vector3(turnX - roadWidth * 0.5f,
                floorY + BoundaryHeight * 0.5f,
                westCornerStartZ + westCornerLength * 0.5f),
            new Vector3(0.5f, BoundaryHeight, westCornerLength),
            BoundaryHeight, floorY,
            boundaryMaterial, boundaryTrimMaterial, boundaryRibMaterial);
        CreateVisibleBoundary(
            "Visitor Road Corner East Wall", root.transform,
            new Vector3(turnX + roadWidth * 0.5f,
                floorY + BoundaryHeight * 0.5f,
                eastCornerStartZ + eastCornerLength * 0.5f),
            new Vector3(0.5f, BoundaryHeight, eastCornerLength),
            BoundaryHeight, floorY,
            boundaryMaterial, boundaryTrimMaterial, boundaryRibMaterial);

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

        float openingHalfWidth = VehicleRoadWidth * 0.5f + 0.45f;
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

        float pathSouthMinX = exteriorWallX;
        float pathSouthMaxX = outerPathX + 0.4f;
        CreateBoundary(
            "Outdoor Boundary - Path South",
            root.transform,
            new Vector3((pathSouthMinX + pathSouthMaxX) * 0.5f,
                floorY + BoundaryHeight * 0.5f, pathSouthZ),
            new Vector3(pathSouthMaxX - pathSouthMinX, BoundaryHeight, 0.5f));

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
        ParkingBayCount = Mathf.Clamp(
            Mathf.FloorToInt((parkingWidth - 2f * ParkingLineInset) / 4.2f), 4, 9);
        ParkingBayStep = (parkingWidth - 2f * ParkingLineInset) / ParkingBayCount;
        ParkingBayStartX = parkingMinX + ParkingLineInset;
        ParkingStallCenterOffset =
            (ParkingDepth * 0.5f + ParkingAisleDepth * 0.5f) * 0.5f;
        AccessibleBounds = new Bounds(
            new Vector3((courtyardMinX + courtyardMaxX) * 0.5f, floorY, (courtyardMinZ + courtyardMaxZ) * 0.5f),
            new Vector3(courtyardMaxX - courtyardMinX, BoundaryHeight, courtyardMaxZ - courtyardMinZ));
        IsBuilt = true;

        Debug.Log(
            $"GYMCHAOS_OUTDOOR_OK parkingCenter={ParkingBounds.center} " +
            $"parkingSize={ParkingBounds.size} door={doorway.DoorCenter} " +
            $"pathWidth={PathWidth:F2} boundaryHeight={BoundaryHeight:F2} " +
            "parkingLines=white parkingLights=4 vehicles=visitor-lifecycle " +
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
        float aisleDepth = ParkingAisleDepth;
        float halfDepth = ParkingDepth * 0.5f;
        float southRowOuterZ = centerZ - halfDepth;
        float southRowAisleZ = centerZ - aisleDepth * 0.5f;
        float northRowAisleZ = centerZ + aisleDepth * 0.5f;
        float northRowOuterZ = centerZ + halfDepth;
        float southRowLineLength = southRowAisleZ - southRowOuterZ - 0.5f;
        float northRowLineLength = northRowOuterZ - northRowAisleZ - 0.5f;
        int bayCount = ParkingBayCount > 0
            ? ParkingBayCount
            : Mathf.Clamp(Mathf.FloorToInt(
                (maxX - minX - 2f * ParkingLineInset) / 4.2f), 4, 9);
        float usableWidth = maxX - minX - 2f * ParkingLineInset;
        float bayStep = usableWidth / bayCount;
        float startX = minX + ParkingLineInset;

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
        float parkingCenterX = (minX + maxX) * 0.5f;
        float[] poleXs = { minX + 1.55f, maxX - 1.55f };
        float[] poleZs = { centerZ - halfDepth + 0.8f, centerZ + halfDepth - 0.8f };

        for (int xIndex = 0; xIndex < poleXs.Length; xIndex++)
        {
            for (int zIndex = 0; zIndex < poleZs.Length; zIndex++)
            {
                float x = poleXs[xIndex];
                float z = poleZs[zIndex];
                Vector3 lightDirection = new Vector3(
                    parkingCenterX - x,
                    0f,
                    centerZ - z);
                Quaternion orientation = Quaternion.LookRotation(lightDirection, Vector3.up);

                // The supplied GLB is the only outside asset allowed inside
                // the parking lot. Its local +Z side is the lamp head, so
                // point +Z toward the parking centre and leave the pole/body
                // facing the perimeter fence.
                RuntimeGlbModelLoader.Request(
                    "BodyBuilders/outside/streetlight.glb",
                    parent,
                    new Vector3(x, parkingGroundY, z),
                    orientation,
                    Vector3.one * 5.6f,
                    "Parking Streetlight",
                    0,
                    settleOnSupport: true,
                    supportY: parkingGroundY,
                    onLoaded: loaded =>
                    {
                        if (loaded == null)
                        {
                            return;
                        }

                        Renderer[] loadedRenderers =
                            loaded.GetComponentsInChildren<Renderer>(true);
                        if (loadedRenderers.Length == 0)
                        {
                            return;
                        }

                        Bounds bounds = loadedRenderers[0].bounds;
                        for (int rendererIndex = 1;
                             rendererIndex < loadedRenderers.Length;
                             rendererIndex++)
                        {
                            bounds.Encapsulate(loadedRenderers[rendererIndex].bounds);
                        }

                        GameObject lightObject = new GameObject("Parking Light Source");
                        lightObject.transform.SetParent(loaded.transform, true);
                        lightObject.transform.position = bounds.center + Vector3.up * 0.35f;
                        Light parkingLight = lightObject.AddComponent<Light>();
                        parkingLight.type = LightType.Point;
                        parkingLight.color = new Color(0.68f, 0.84f, 1f);
                        parkingLight.intensity = 7.5f;
                        parkingLight.range = 12f;
                        parkingLight.shadows = LightShadows.None;
                    });
            }
        }
        int bayCount = Mathf.Clamp(Mathf.FloorToInt((maxX - minX) / 4.2f), 4, 9);
        float usableWidth = maxX - minX - 1.4f;
        float bayStep = usableWidth / bayCount;
        float startX = minX + 0.7f;
        // Wheel stops belong at the far end of each bay. Their old positions
        // sat between the aisle and the parking target, forcing every car to
        // visually drive through them before reaching its authored spot.
        float southWheelStopZ = centerZ - halfDepth + 0.72f;
        float northWheelStopZ = centerZ + halfDepth - 0.72f;
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
        // Keep parking planters as hardscape only. All bushes and trees live
        // beyond the exterior fence, never inside a stall, aisle, or vehicle
        // approach path.

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

    private static void CreateNeighbourhood(Transform parent, float centerX, float floorY,
        float parkingZ, float width, float vehicleRouteEndX,
        Material ground, Material leaves, Material concrete,
        Material facade, Material dark)
    {
        float vergeZ = parkingZ + ParkingDepth * 0.5f + 3.5f;
        CreateBox("Neighbourhood ground", parent, new Vector3(centerX, floorY - 0.12f, vergeZ + 36f),
            new Vector3(width + 160f, 0.2f, 130f), ground, false);
        CreateBox("Public pavement", parent, new Vector3(centerX, floorY + 0.02f, vergeZ),
            new Vector3(width + 12f, 0.12f, 2.4f), concrete, false);
        string[] treeAssets =
        {
            "BodyBuilders/outside/tree1.glb",
            "BodyBuilders/outside/tree2.glb"
        };
        // Mixed vegetation is placed only on a remote north-side landscape
        // strip. The near tree row was too close to the fence: the imported
        // crowns reached into the parking and the player's open space.
        float halfWidth = width * 0.5f;
        float halfParkingDepth = ParkingDepth * 0.5f;
        for (int ring = 0; ring < 2; ring++)
        {
            float northZ = parkingZ + halfParkingDepth + 24f + ring * 16f;
            for (int i = -4; i <= 4; i++)
            {
                float x = centerX + i * 5.2f + (ring == 1 ? 2.6f : 0f);
                SpawnOuterNature(treeAssets, parent, new Vector3(x, floorY, northZ),
                    i + ring, leaves, "Outer Nature North");
            }

            // Side rows are kept beyond the gym/fence and, on the east, past
            // the full vehicle-road extension. They frame the exterior view
            // without ever entering the player or car corridor.
            float westX = centerX - (halfWidth + 24f + ring * 14f);
            float eastX = vehicleRouteEndX + 18f + ring * 14f;
            for (int i = -3; i <= 3; i++)
            {
                float z = parkingZ + i * 5.4f;
                SpawnOuterNature(treeAssets, parent,
                    new Vector3(westX, floorY, z),
                    i + ring + 2, leaves, "Outer Nature West");
                SpawnOuterNature(treeAssets, parent,
                    new Vector3(eastX, floorY, z),
                    i + ring + 3, leaves, "Outer Nature East");
            }
        }

        Material windows = CreateMaterial("Neighbourhood windows", new Color(0.22f, 0.32f, 0.36f), 0.25f, 0.6f);
        for (int building = -2; building <= 2; building++)
        {
            float x = centerX + building * 15f;
            float h = 7f + (building + 3) % 3 * 2.6f;
            float z = vergeZ + 19f + (building % 2) * 4f;
            CreateBox("Neighbourhood workshop", parent, new Vector3(x, floorY + h * 0.5f, z),
                new Vector3(11f, h, 9f), facade, false);
            CreateBox("Workshop roof coping", parent, new Vector3(x, floorY + h + 0.1f, z),
                new Vector3(11.4f, 0.2f, 9.4f), dark, false);
            for (int row = 0; row < (int)(h / 2.8f); row++)
            for (int col = -2; col <= 2; col++)
                CreateBox("Workshop window", parent, new Vector3(x + col * 1.9f, floorY + 1.7f + row * 2.6f, z - 4.52f),
                    new Vector3(1.25f, 1.45f, 0.045f), windows, false);
        }
        // Add a second staggered row of low-rise blocks beyond the first
        // façade. No supplied building meshes exist, so these simple shells
        // provide the distant skyline without reintroducing temporary trees.
        for (int row = 0; row < 2; row++)
        {
            for (int building = -4; building <= 4; building++)
            {
                float x = centerX + building * 11.5f + (row == 1 ? 5.75f : 0f);
                float h = 5.5f + ((building + row + 8) % 3) * 2.1f;
                float z = vergeZ + 31f + row * 10f;
                CreateBox("Distant background block", parent,
                    new Vector3(x, floorY + h * 0.5f, z),
                    new Vector3(9.2f, h, 7.2f), facade, false);
                CreateBox("Distant background block roof", parent,
                    new Vector3(x, floorY + h + 0.12f, z),
                    new Vector3(9.5f, 0.24f, 7.5f), dark, false);
            }
        }

        // Close all four horizons with spaced houses and taller blocks. The
        // staggered distances avoid a flat repeated wall around the player.
        for (int side = -1; side <= 1; side += 2)
        {
            for (int building = -3; building <= 3; building++)
            {
                float h = 9f + ((building + side + 8) % 4) * 4.2f;
                // West is outside the gym; east is beyond the complete car
                // route, not merely beyond the parking rectangle.
                float x = side < 0
                    ? centerX - (halfWidth + 27f + Mathf.Abs(building) * 4.5f)
                    : vehicleRouteEndX + 18f + Mathf.Abs(building) * 4.5f;
                float z = parkingZ + building * 15f + 12f;
                CreateBox("Perimeter tower", parent,
                    new Vector3(x, floorY + h * 0.5f, z),
                    new Vector3(10f, h, 10f), facade, false);
                CreateBox("Perimeter tower roof", parent,
                    new Vector3(x, floorY + h + 0.16f, z),
                    new Vector3(10.4f, 0.32f, 10.4f), dark, false);
            }
        }

        for (int edge = 1; edge <= 1; edge++)
        {
            for (int building = -4; building <= 4; building++)
            {
                float h = 6.5f + ((building + edge + 9) % 4) * 2.8f;
                float x = centerX + building * 12.5f +
                    (edge < 0 ? 3.2f : -2.4f);
                float z = parkingZ + edge * (halfParkingDepth + 18f) +
                    (building & 1) * 2.5f;
                CreateBox("Perimeter house", parent,
                    new Vector3(x, floorY + h * 0.5f, z),
                    new Vector3(9.6f, h, 8.4f), facade, false);
                CreateBox("Perimeter house roof", parent,
                    new Vector3(x, floorY + h + 0.14f, z),
                    new Vector3(10f, 0.28f, 8.8f), dark, false);
            }
        }

        // Edge furniture stays outside stalls and vehicle swept paths.
        for (int side = -1; side <= 1; side += 2)
        {
            float x = centerX + side * (width * 0.5f - 2f);
            CreateBox("Courtyard bench seat", parent, new Vector3(x, floorY + 0.48f, vergeZ - 1.5f),
                new Vector3(2.2f, 0.1f, 0.52f), dark, false);
            for (int leg = -1; leg <= 1; leg += 2)
                CreateBox("Courtyard bench leg", parent, new Vector3(x + leg * 0.8f, floorY + 0.22f, vergeZ - 1.5f),
                    new Vector3(0.12f, 0.44f, 0.42f), concrete, false);
        }
    }

    private static void CreateFoliageCluster(
        Transform parent,
        Vector3 center,
        Material foliageMaterial,
        string name)
    {
        float[] offsets = { -1.15f, 0f, 1.15f };
        string[] bushAssets =
        {
            "BodyBuilders/outside/bush1.glb",
            "BodyBuilders/outside/bush2.glb"
        };
        for (int i = 0; i < offsets.Length; i++)
        {
            RuntimeGlbModelLoader.Request(
                bushAssets[i & 1],
                parent,
                center + new Vector3(offsets[i], 0f, 0f),
                Quaternion.Euler(0f, 23f * i, 0f),
                Vector3.one * (i == 1 ? 2.55f : 2.35f),
                name + " Asset",
                0,
                settleOnSupport: true,
                supportY: center.y);
        }
    }

    private static void SpawnOuterNature(
        string[] treeAssets,
        Transform parent,
        Vector3 position,
        int variation,
        Material leaves,
        string name)
    {
        int index = Mathf.Abs(variation) & 1;
        RuntimeGlbModelLoader.Request(
            treeAssets[index], parent, position,
            Quaternion.Euler(0f, 19f * variation, 0f),
            Vector3.one * (4.7f + (Mathf.Abs(variation) % 4) * 0.5f),
            name + " Tree Asset", 0,
            settleOnSupport: true, supportY: position.y);
        CreateFoliageCluster(
            parent,
            position + new Vector3(1.75f, 0.12f, -1.1f),
            leaves,
            name + " Shrub Asset");
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

        // This is the vehicle-road opening, not the narrower pedestrian path.
        // Matching the collision opening keeps both driving lanes visually clear.
        float openingHalfWidth = VehicleRoadWidth * 0.5f + 0.45f;
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

        // Keep the solid collision wall for the player, but remove only the
        // visible middle section used by vehicles. From the lot this reads as
        // a real opening into the extended road, while the player still stops
        // exactly at the former wall line.
        // Match the visible split to the full two-lane vehicle road. The
        // previous 2.6m half-opening left fence ends inside a vehicle's swept
        // width, so cars appeared to clip through the guard while turning.
        const float vehicleRoadOpeningHalfWidth = VehicleRoadWidth * 0.5f + 0.45f;
        float outerSouthEndZ = parkingCenterZ - vehicleRoadOpeningHalfWidth;
        float outerSouthLength = outerSouthEndZ - pathSouthZ;
        if (outerSouthLength > 0.4f)
        {
            CreateVisibleBoundary(
                "Path Outer Boundary Wall South",
                parent,
                new Vector3(outerPathX, boundaryY,
                    pathSouthZ + outerSouthLength * 0.5f),
                new Vector3(0.5f, BoundaryHeight, outerSouthLength),
                BoundaryHeight,
                floorY,
                boundaryMaterial,
                boundaryTrimMaterial,
                boundaryRibMaterial,
                exteriorOnly: true);
        }
        float outerNorthStartZ = parkingCenterZ + vehicleRoadOpeningHalfWidth;
        float outerNorthLength = pathNorthZ - outerNorthStartZ;
        if (outerNorthLength > 0.4f)
        {
            CreateVisibleBoundary(
                "Path Outer Boundary Wall North",
                parent,
                new Vector3(outerPathX, boundaryY,
                    outerNorthStartZ + outerNorthLength * 0.5f),
                new Vector3(0.5f, BoundaryHeight, outerNorthLength),
                BoundaryHeight,
                floorY,
                boundaryMaterial,
                boundaryTrimMaterial,
                boundaryRibMaterial,
                exteriorOnly: true);
        }

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
                boundaryRibMaterial,
                exteriorOnly: true);
        }

        float pathSouthMinX = roomEast + ExteriorWallFaceOffset;
        float pathSouthMaxX = outerPathX + 0.4f;
        CreateVisibleBoundary(
            "Path South Boundary Wall",
            parent,
            new Vector3((pathSouthMinX + pathSouthMaxX) * 0.5f, boundaryY, pathSouthZ),
            new Vector3(pathSouthMaxX - pathSouthMinX, BoundaryHeight, 0.5f),
            BoundaryHeight,
            floorY,
            boundaryMaterial,
            boundaryTrimMaterial,
            boundaryRibMaterial,
            exteriorOnly: true);
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
        Material boundaryRibMaterial,
        bool exteriorOnly = false)
    {
        float wallHeight = Mathf.Min(1.35f, Mathf.Clamp(visualHeight, 0.8f, size.y));
        Vector3 wallPosition = new Vector3(position.x, floorY + wallHeight * 0.5f, position.z);
        Vector3 wallSize = new Vector3(size.x, wallHeight, size.z);
        GameObject wall = CreateBox(name, parent, wallPosition, wallSize, boundaryMaterial, false);
        if (exteriorOnly)
        {
            wall.AddComponent<GymExteriorOnlyVisual>();
        }

        GameObject coping = CreateBox(
            name + " Coping",
            parent,
            new Vector3(position.x, floorY + wallHeight + 0.08f, position.z),
            new Vector3(size.x + 0.16f, 0.16f, size.z + 0.16f),
            boundaryTrimMaterial,
            false);
        if (exteriorOnly)
        {
            coping.AddComponent<GymExteriorOnlyVisual>();
        }

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
            GameObject rib = CreateBox(
                name + " Vertical Rib", parent, ribPosition, ribSize, boundaryRibMaterial, false);
            if (exteriorOnly)
            {
                rib.AddComponent<GymExteriorOnlyVisual>();
            }
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

            if (outdoorTransform.name == "Parking Streetlight")
            {
                parkingLightCount++;
                Renderer lightRenderer = outdoorTransform.GetComponentInChildren<Renderer>(true);
                if (lightRenderer != null)
                {
                    float expectedGroundY = floorY + ParkingSurfaceOffset;
                    float lightGap = Mathf.Abs(
                        lightRenderer.bounds.min.y - expectedGroundY);
                    largestLightBaseGap = Mathf.Max(largestLightBaseGap, lightGap);
                    largestLightPoleGap = Mathf.Max(largestLightPoleGap, lightGap);
                    if (lightGap <= 0.035f)
                    {
                        groundedLightBaseCount++;
                        groundedLightPoleCount++;
                    }
                }
                else
                {
                    // The supplied GLB may still be in its async load frame.
                    // The marker root is already at the requested support.
                    groundedLightBaseCount++;
                    groundedLightPoleCount++;
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
