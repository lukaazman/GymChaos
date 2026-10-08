using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Builds the small physical space behind the main gym. It stays procedural so
/// it follows the same runtime scene path as the existing gym walls and floor.
/// </summary>
public static class GymBackRoomBuilder
{
    private const string RootName = "Gym Back Area (Runtime)";
    private const float BackWidth = 17.5f;
    private const float BackDepth = 13f;
    private const float BackHeight = 4.4f;
    private const float DoorWidth = 2.8f;
    private const string WoodenBenchAsset = "BodyBuilders/items/wooden_bench.glb";
    private const string ToiletAsset = "BodyBuilders/items/toilet.glb";
    private const string SinkAsset = "BodyBuilders/items/sink.glb";
    private const string ColorBagAsset = "BodyBuilders/items/color_bag.glb";
    private const string BlackBagAsset = "BodyBuilders/items/black_bag.glb";
    // The authored bench is imported at 2.7x and its runtime top is about
    // 0.51 m above the room floor.  Bags are settled onto that surface, not
    // onto an assumed standing-height marker.
    private const float BenchSeatSupportHeight = 0.52f;
    private const float BenchBagScale = 0.78f;
    private const float BenchOffsetX = -4.15f;
    private const float BenchOffsetZ = 2.65f;
    public const float PrepInteractionRange = 3.6f;
    public const float BenchPrepInteractionRange = 3.9f;
    private static Bounds roomBounds;
    private static bool roomBoundsReady;
    private static float roomFloorY;
    private static Vector3 roomCenter;
    private static Vector3 lockerVisitPoint;
    private static Quaternion lockerVisitRotation = Quaternion.identity;
    private const int LockerVisitSlotCount = 4;
    private static readonly bool[] lockerSlotReserved = new bool[LockerVisitSlotCount];
    private static readonly BodybuilderIdentity[] lockerSlotOwners = new BodybuilderIdentity[LockerVisitSlotCount];
    private static readonly System.Collections.Generic.List<GameObject>[] benchBags =
    {
        new System.Collections.Generic.List<GameObject>(2),
        new System.Collections.Generic.List<GameObject>(2)
    };
    private static readonly int[] visibleBagCounts = new int[2];
    private static readonly int[] visibleBagStartIndices = new int[2];
    private static readonly System.Collections.Generic.Dictionary<BodybuilderIdentity, int> bagOwnerBench =
        new System.Collections.Generic.Dictionary<BodybuilderIdentity, int>();
    private static readonly GameObject[] loadedBenches = new GameObject[2];
    private static readonly System.Collections.Generic.HashSet<BodybuilderIdentity> activeBagVisitors =
        new System.Collections.Generic.HashSet<BodybuilderIdentity>();
    private static int authoredLockerPropRequests;
    private static int authoredLockerPropsLoaded;

    public static bool HasAuthoredLockerProps =>
        authoredLockerPropRequests > 0 &&
        authoredLockerPropsLoaded >= authoredLockerPropRequests;
    public static int VisibleBenchBagCount
    {
        get
        {
            int visible = 0;
            for (int bench = 0; bench < benchBags.Length; bench++)
            {
                for (int i = 0; i < benchBags[bench].Count; i++)
                {
                    if (benchBags[bench][i] != null && benchBags[bench][i].activeSelf) visible++;
                }
            }
            return visible;
        }
    }
    public static int GetVisibleBagCountForBench(int benchIndex)
    {
        if (benchIndex < 0 || benchIndex >= benchBags.Length) return 0;
        int visible = 0;
        for (int i = 0; i < benchBags[benchIndex].Count; i++)
        {
            if (benchBags[benchIndex][i] != null && benchBags[benchIndex][i].activeSelf) visible++;
        }
        return visible;
    }
    public static bool HasActiveBagVisitor => activeBagVisitors.Count > 0;
    public static BodybuilderIdentity ActiveBagVisitor
    {
        get
        {
            foreach (BodybuilderIdentity identity in activeBagVisitors)
            {
                return identity;
            }
            return BodybuilderIdentity.Mark;
        }
    }

    public static bool TryGetRoomBounds(out Bounds bounds)
    {
        if (!roomBoundsReady)
        {
            GameObject existingRoot = GameObject.Find(RootName);
            if (existingRoot != null)
            {
                RebuildBoundsFromExistingRoot(existingRoot);
            }
        }

        bounds = roomBounds;
        return roomBoundsReady;
    }

    public static bool IsInsideRoom(Vector3 position)
    {
        if (!roomBoundsReady)
        {
            GameObject existingRoot = GameObject.Find(RootName);
            if (existingRoot != null)
            {
                RebuildBoundsFromExistingRoot(existingRoot);
            }
            if (!roomBoundsReady)
            {
                return false;
            }
        }

        return position.x >= roomBounds.min.x && position.x <= roomBounds.max.x &&
            position.z >= roomBounds.min.z && position.z <= roomBounds.max.z &&
            position.y >= roomBounds.min.y - 1f && position.y <= roomBounds.max.y + 1f;
    }

    public static void Build(
        Transform parent, Vector3 mainCenter, float mainWidth, float mainDepth,
        float mainHeight, PlayerMovement player)
    {
        GameObject existingRoot = GameObject.Find(RootName);
        if (existingRoot != null)
        {
            RebuildBoundsFromExistingRoot(existingRoot);
            return;
        }

        float floorY = mainCenter.y;
        float mainSouth = mainCenter.z - mainDepth * 0.5f;
        roomCenter = new Vector3(mainCenter.x, floorY, mainSouth - BackDepth * 0.5f - 0.18f);
        roomFloorY = floorY;
        roomBounds = new Bounds(
            roomCenter + Vector3.up * (BackHeight * 0.5f),
            new Vector3(BackWidth, BackHeight, BackDepth));
        roomBoundsReady = true;
        lockerVisitPoint = GetLockerSlotPosition(0);
        lockerVisitRotation = Quaternion.LookRotation(Vector3.left, Vector3.up);
        GameObject rootObject = new GameObject(RootName);
        rootObject.transform.SetParent(parent, true);

        Material floorMaterial = GymSurfaceMaterialFactory.CreateLockerFloor("Locker room rubber floor", new Color(0.025f, 0.04f, 0.075f), new Vector2(BackWidth, BackDepth));
        Material wallMaterial = CreateMaterial("Locker room wall", new Color(0.22f, 0.25f, 0.29f), 0f, 0.28f);
        Material trimMaterial = CreateMaterial("Locker room trim", new Color(0.035f, 0.045f, 0.06f), 0.35f, 0.64f);
        Material accentMaterial = CreateMaterial("Locker room accent", new Color(0.82f, 0.13f, 0.08f), 0.05f, 0.42f);
        Material tileMaterial = GymSurfaceMaterialFactory.CreateBathroomTile("Bathroom tile", new Color(0.26f, 0.33f, 0.41f));
        Material mirrorMaterial = CreateMaterial("Locker room mirror", new Color(0.58f, 0.68f, 0.76f), 0.9f, 0.95f);
        Material metalMaterial = CreateMaterial("Locker metal", new Color(0.12f, 0.15f, 0.19f), 0.72f, 0.48f);
        SetEmission(accentMaterial, new Color(0.55f, 0.025f, 0.01f));

        CreateBox("Locker Room Floor", rootObject.transform,
            roomCenter + Vector3.down * 0.12f, new Vector3(BackWidth, 0.24f, BackDepth), floorMaterial, true);
        CreateBox("Locker Room Ceiling", rootObject.transform,
            roomCenter + Vector3.up * BackHeight, new Vector3(BackWidth, 0.18f, BackDepth), trimMaterial, true);

        float northZ = roomCenter.z + BackDepth * 0.5f;
        float southZ = roomCenter.z - BackDepth * 0.5f;
        float minX = roomCenter.x - BackWidth * 0.5f;
        float maxX = roomCenter.x + BackWidth * 0.5f;
        float doorMinX = roomCenter.x - DoorWidth * 0.5f;
        float doorMaxX = roomCenter.x + DoorWidth * 0.5f;

        CreateBox("Locker Room West Wall", rootObject.transform,
            new Vector3(minX, floorY + BackHeight * 0.5f, roomCenter.z),
            new Vector3(0.24f, BackHeight, BackDepth), wallMaterial, true);
        CreateBox("Locker Room East Wall", rootObject.transform,
            new Vector3(maxX, floorY + BackHeight * 0.5f, roomCenter.z),
            new Vector3(0.24f, BackHeight, BackDepth), wallMaterial, true);
        CreateBox("Locker Room South Wall", rootObject.transform,
            new Vector3(roomCenter.x, floorY + BackHeight * 0.5f, southZ),
            new Vector3(BackWidth, BackHeight, 0.24f), wallMaterial, true);
        CreateBox("Locker Room North Wall Left", rootObject.transform,
            new Vector3((minX + doorMinX) * 0.5f, floorY + BackHeight * 0.5f, northZ),
            new Vector3(doorMinX - minX, BackHeight, 0.24f), wallMaterial, true);
        CreateBox("Locker Room North Wall Right", rootObject.transform,
            new Vector3((doorMaxX + maxX) * 0.5f, floorY + BackHeight * 0.5f, northZ),
            new Vector3(maxX - doorMaxX, BackHeight, 0.24f), wallMaterial, true);
        CreateBox("Locker Room Door Header", rootObject.transform,
            new Vector3(roomCenter.x, floorY + 3.55f, northZ),
            new Vector3(DoorWidth, BackHeight - 3.55f, 0.24f), wallMaterial, true);

        CreateBox("Locker Room Door Frame Left", rootObject.transform,
            new Vector3(doorMinX, floorY + 1.8f, northZ - 0.08f),
            new Vector3(0.16f, 3.6f, 0.18f), accentMaterial, false);
        CreateBox("Locker Room Door Frame Right", rootObject.transform,
            new Vector3(doorMaxX, floorY + 1.8f, northZ - 0.08f),
            new Vector3(0.16f, 3.6f, 0.18f), accentMaterial, false);
        CreateLockerBays(rootObject.transform, roomCenter, floorY, minX, maxX, metalMaterial, accentMaterial);
        CreateBenches(rootObject.transform, roomCenter, floorY, trimMaterial, accentMaterial);
        CreateBathroom(
            rootObject.transform, roomCenter, floorY, tileMaterial, metalMaterial,
            accentMaterial, mirrorMaterial, player);
        CreateMirror(rootObject.transform, roomCenter, floorY, mirrorMaterial, trimMaterial, player);

        CreateInteractable(rootObject.transform, "Locker Room Prep Point",
            new Vector3(roomCenter.x, floorY + 1.0f, northZ - 1.0f),
            new Vector3(DoorWidth - 0.3f, 2f, 1.8f),
            GymBackRoomInteractionType.Prep, "Get ready", PrepInteractionRange);
        // Each bench is its own prep spot with a wide reach, so the player
        // can get ready anywhere around either bench, not only the door one.
        for (int benchIndex = 0; benchIndex < 2; benchIndex++)
        {
            CreateInteractable(rootObject.transform,
                "Locker Bench Prep Point " + (benchIndex == 0 ? "Left" : "Right"),
                GetBenchCenter(roomCenter, floorY, benchIndex) + Vector3.up * 1.0f,
                new Vector3(3.4f, 2f, 1.8f),
                GymBackRoomInteractionType.Prep, "Get ready", BenchPrepInteractionRange);
        }
        CreateInteractable(rootObject.transform, "Changing Locker",
            new Vector3(minX + 1.5f, floorY + 1.1f, roomCenter.z + 0.4f),
            new Vector3(2.2f, 2.1f, 1.8f),
            GymBackRoomInteractionType.Locker, "Open locker");

        Debug.Log(
            $"GYMCHAOS_BACK_ROOM_OK center={roomCenter} size={BackWidth:F1}x{BackDepth:F1} " +
            $"doorWidth={DoorWidth:F1} bathroom=1 lockerBanks=1 lockers=7 lockerDoors=14 lockerSlots=4 bags=4 " +
            $"benches=2 bathroomMirrors=3 mirror=1",
            rootObject);
    }

    private static void RebuildBoundsFromExistingRoot(GameObject rootObject)
    {
        Renderer[] renderers = rootObject.GetComponentsInChildren<Renderer>(true);
        bool hasBounds = false;
        Bounds bounds = default;
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null)
            {
                continue;
            }

            if (!hasBounds)
            {
                bounds = renderer.bounds;
                hasBounds = true;
            }
            else
            {
                bounds.Encapsulate(renderer.bounds);
            }
        }

        if (hasBounds)
        {
            roomBounds = bounds;
            Renderer floorRenderer = null;
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] != null && renderers[i].name == "Locker Room Floor")
                {
                    floorRenderer = renderers[i];
                    break;
                }
            }

            roomFloorY = floorRenderer != null
                ? floorRenderer.bounds.max.y
                : bounds.min.y + 0.24f;
            roomCenter = new Vector3(
                floorRenderer != null ? floorRenderer.bounds.center.x : bounds.center.x,
                roomFloorY,
                floorRenderer != null ? floorRenderer.bounds.center.z : bounds.center.z);
            roomBoundsReady = true;
        }
    }

    public static bool TryGetLockerPreviewPose(
        out Vector3 playerPosition, out Quaternion playerRotation)
    {
        playerPosition = default;
        playerRotation = Quaternion.identity;
        if (!roomBoundsReady)
        {
            GameObject existingRoot = GameObject.Find(RootName);
            if (existingRoot != null)
            {
                RebuildBoundsFromExistingRoot(existingRoot);
            }
            if (!roomBoundsReady)
            {
                return false;
            }
        }

        float mirrorZ = roomCenter.z - BackDepth * 0.5f + 0.18f;
        playerPosition = new Vector3(roomCenter.x, roomFloorY + 1.05f, mirrorZ + 2.25f);
        playerRotation = Quaternion.LookRotation(Vector3.back, Vector3.up);
        return true;
    }

    public static bool TryGetLockerVisitPose(
        out Vector3 visitorPosition, out Quaternion visitorRotation)
    {
        visitorPosition = lockerVisitPoint;
        visitorRotation = lockerVisitRotation;
        if (!roomBoundsReady)
        {
            GameObject existingRoot = GameObject.Find(RootName);
            if (existingRoot != null)
            {
                RebuildBoundsFromExistingRoot(existingRoot);
            }
        }

        if (!roomBoundsReady)
        {
            visitorPosition = default;
            visitorRotation = Quaternion.identity;
            return false;
        }

        if (visitorPosition == Vector3.zero)
        {
            visitorPosition = new Vector3(
                roomCenter.x - 5.65f,
                roomFloorY,
                roomCenter.z + 0.90f);
            visitorRotation = Quaternion.LookRotation(Vector3.left, Vector3.up);
        }
        return true;
    }

    public static bool TryGetLockerVisitPose(
        BodybuilderIdentity identity,
        out Vector3 visitorPosition,
        out Quaternion visitorRotation)
    {
        visitorPosition = default;
        visitorRotation = Quaternion.identity;
        Vector3 ignoredPosition;
        Quaternion ignoredRotation;
        if (!TryGetLockerVisitPose(out ignoredPosition, out ignoredRotation))
        {
            return false;
        }
        if (!TryReserveLockerSlot(identity, out int slot))
        {
            return false;
        }

        visitorPosition = GetLockerSlotPosition(slot);
        visitorRotation = Quaternion.LookRotation(Vector3.left, Vector3.up);
        return true;
    }

    // Clear gap between the two bathroom divider segments.
    public static float BathroomPassageWidthForVerification
    {
        get
        {
            GameObject root = GameObject.Find(RootName);
            if (root == null ||
                !TryGetNamedBounds(root.transform, "Bathroom divider south", out Bounds south) ||
                !TryGetNamedBounds(root.transform, "Bathroom divider north", out Bounds north))
            {
                return 0f;
            }
            return north.min.z - south.max.z;
        }
    }

    public static bool HasRequiredLockerLayoutForVerification(out string details)
    {
        GameObject root = GameObject.Find(RootName);
        if (root == null)
        {
            details = "root=missing";
            return false;
        }
        Transform[] nodes = root.GetComponentsInChildren<Transform>(true);
        int lockers = 0;
        int lockerDoors = 0;
        int benches = 0;
        int bathroomMirrors = 0;
        int sinks = 0;
        int toilets = 0;
        int bagObjects = 0;
        for (int index = 0; index < nodes.Length; index++)
        {
            string name = nodes[index].name;
            if (name == "Locker cabinet") lockers++;
            if (name == "Inset locker door") lockerDoors++;
            if (name.StartsWith("Authored wooden locker bench ")) benches++;
            if (name.StartsWith("Bathroom mirror ") && !name.Contains("frame")) bathroomMirrors++;
            if (name.StartsWith("Authored sink ")) sinks++;
            if (name == "Authored toilet") toilets++;
            if (name.EndsWith("color bag") || name.EndsWith("black bag")) bagObjects++;
        }
        TryGetRoomBounds(out Bounds bounds);
        bool dimensions = bounds.size.x >= 17.4f && bounds.size.z >= 12.9f;
        bool centerClear = root.transform.Find("Central Locker Bank Header") == null;
        for (int index = 0; index < nodes.Length && centerClear; index++)
        {
            Transform node = nodes[index];
            if (node.name == "Locker cabinet" &&
                Mathf.Abs(node.position.x - roomCenter.x) < 1.2f)
            {
                centerClear = false;
            }
        }
        bool partition = TryGetNamedBounds(
            root.transform, "Bathroom divider south", out Bounds partitionSouth) &&
            TryGetNamedBounds(
                root.transform, "Bathroom divider north", out Bounds partitionNorth) &&
            partitionSouth.size.y >= BackHeight - 0.1f &&
            partitionNorth.size.y >= BackHeight - 0.1f &&
            // Full-run divider except the one entry passage (at most 2.3 m).
            partitionSouth.size.z + partitionNorth.size.z >= BackDepth - 2.3f;
        bool fixturesWallFlush = AreBathroomFixturesWallFlush(root.transform);
        bool separateStall =
            TryGetNamedBounds(root.transform, "Bathroom stall west wall", out Bounds _) &&
            TryGetNamedBounds(root.transform, "Bathroom stall south wall left", out Bounds _) &&
            TryGetNamedBounds(root.transform, "Bathroom stall south wall right", out Bounds _);
        bool fixturesGrounded = AreNamedPropsGrounded(
            root.transform, "Authored sink ", "Authored toilet");
        PlanarGymMirror[] mirrors =
            root.GetComponentsInChildren<PlanarGymMirror>(true);
        bool mirrorView = mirrors.Length >= 2;
        for (int index = 0; index < mirrors.Length && mirrorView; index++)
        {
            mirrorView &= mirrors[index] != null &&
                mirrors[index].ReflectionIncludesPlayerLayer;
        }
        bool bagsGrounded = AreBagsGrounded(root.transform);
        bool passed = dimensions && lockers >= 7 && lockerDoors >= 14 &&
            benches == 2 &&
            bathroomMirrors >= 3 && sinks >= 3 && toilets >= 1 &&
            root.transform.Find("Locker Room Ceiling") != null &&
            centerClear && partition && fixturesWallFlush && separateStall &&
            fixturesGrounded && mirrorView &&
            bagObjects >= 4 && bagsGrounded;
        details = $"size={bounds.size} lockers={lockers} doors={lockerDoors} " +
            $"benches={benches} " +
            $"bathroomMirrors={bathroomMirrors} sinks={sinks} toilets={toilets} " +
            $"centerClear={centerClear} partition={partition} " +
            $"fixturesWallFlush={fixturesWallFlush} separateStall={separateStall} " +
            $"fixturesGrounded={fixturesGrounded} mirrorView={mirrorView} " +
            $"bagObjects={bagObjects} bagsGrounded={bagsGrounded}";
        return passed;
    }

    private static bool AreBathroomFixturesWallFlush(Transform root)
    {
        float eastWallInsideX = roomCenter.x + BackWidth * 0.5f - 0.12f;
        Transform[] nodes = root.GetComponentsInChildren<Transform>(true);
        int sinks = 0;
        int mirrors = 0;
        bool soapShelf = false;
        for (int index = 0; index < nodes.Length; index++)
        {
            Transform node = nodes[index];
            if (node.name.StartsWith("Authored sink "))
            {
                sinks++;
                if (eastWallInsideX - node.position.x > 1.15f)
                {
                    return false;
                }
            }
            else if (node.name.StartsWith("Bathroom mirror ") &&
                !node.name.Contains("frame"))
            {
                mirrors++;
                if (Mathf.Abs(node.position.x - eastWallInsideX) > 0.18f)
                {
                    return false;
                }
            }
            else if (node.name == "Bathroom soap shelf")
            {
                soapShelf = Mathf.Abs(node.position.x - eastWallInsideX) <= 0.75f;
            }
        }
        return sinks >= 3 && mirrors >= 3 && soapShelf;
    }

    private static bool TryGetNamedBounds(
        Transform root, string name, out Bounds bounds)
    {
        Transform target = root != null ? root.Find(name) : null;
        Renderer[] renderers = target != null
            ? target.GetComponentsInChildren<Renderer>(true)
            : new Renderer[0];
        if (renderers.Length == 0)
        {
            bounds = default;
            return false;
        }

        bounds = renderers[0].bounds;
        for (int index = 1; index < renderers.Length; index++)
        {
            bounds.Encapsulate(renderers[index].bounds);
        }
        return true;
    }

    private static bool AreNamedPropsGrounded(
        Transform root, string sinkPrefix, string toiletName)
    {
        Transform[] nodes = root.GetComponentsInChildren<Transform>(true);
        int fixtureCount = 0;
        for (int index = 0; index < nodes.Length; index++)
        {
            Transform node = nodes[index];
            if (node == null ||
                (!node.name.StartsWith(sinkPrefix) && node.name != toiletName))
            {
                continue;
            }

            if (!TryGetNamedBounds(root, node.name, out Bounds bounds) ||
                Mathf.Abs(bounds.min.y - roomFloorY) > 0.06f)
            {
                return false;
            }
            fixtureCount++;
        }

        return fixtureCount >= 4;
    }

    private static bool AreBagsGrounded(Transform root)
    {
        Transform[] nodes = root.GetComponentsInChildren<Transform>(true);
        bool leftBenchReady = TryGetNamedBounds(
            root, "Authored wooden locker bench Left", out Bounds leftBench);
        bool rightBenchReady = TryGetNamedBounds(
            root, "Authored wooden locker bench Right", out Bounds rightBench);
        int bagCount = 0;
        for (int index = 0; index < nodes.Length; index++)
        {
            Transform node = nodes[index];
            if (node == null ||
                (!node.name.EndsWith("color bag") &&
                 !node.name.EndsWith("black bag")))
            {
                continue;
            }

            Renderer[] renderers = node.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                return false;
            }
            Bounds bounds = renderers[0].bounds;
            for (int rendererIndex = 1;
                rendererIndex < renderers.Length; rendererIndex++)
            {
                bounds.Encapsulate(renderers[rendererIndex].bounds);
            }
            bool dimensions = bounds.size.x >= 0.10f &&
                bounds.size.y >= 0.10f && bounds.size.z >= 0.10f &&
                bounds.size.x <= 1.60f && bounds.size.y <= 1.60f &&
                bounds.size.z <= 1.60f;
            Bounds bench = node.name.StartsWith("Left ")
                ? leftBench : rightBench;
            bool horizontalSupport = (node.name.StartsWith("Left ")
                    ? leftBenchReady : rightBenchReady) &&
                bounds.max.x >= bench.min.x - 0.25f &&
                bounds.min.x <= bench.max.x + 0.25f &&
                bounds.max.z >= bench.min.z - 0.25f &&
                bounds.min.z <= bench.max.z + 0.25f;
            GameObject benchObject = loadedBenches[node.name.StartsWith("Left ") ? 0 : 1];
            float seatSurface = benchObject != null
                ? GetSeatSurfaceY(benchObject, bounds, bench.max.y)
                : bench.max.y;
            bool verticalSupport = Mathf.Abs(bounds.min.y - (seatSurface + 0.005f)) <= 0.02f;
            if (!dimensions || !horizontalSupport || !verticalSupport)
            {
                return false;
            }
            bagCount++;
        }

        return bagCount >= 4;
    }

    public static int LockerSlotCapacity => LockerVisitSlotCount;

    public static int ReservedLockerSlotCount
    {
        get
        {
            int count = 0;
            for (int index = 0; index < lockerSlotReserved.Length; index++)
            {
                if (lockerSlotReserved[index])
                {
                    count++;
                }
            }
            return count;
        }
    }

    public static bool TryReserveLockerSlot(
        BodybuilderIdentity identity, out int slot)
    {
        for (int index = 0; index < lockerSlotReserved.Length; index++)
        {
            if (lockerSlotReserved[index] &&
                lockerSlotOwners[index] == identity)
            {
                slot = index;
                return true;
            }
        }
        for (int index = 0; index < lockerSlotReserved.Length; index++)
        {
            if (!lockerSlotReserved[index])
            {
                lockerSlotReserved[index] = true;
                lockerSlotOwners[index] = identity;
                slot = index;
                Debug.Log(
                    $"GYMCHAOS_LOCKER_SLOT_RESERVED identity={identity} " +
                    $"slot={slot} reserved={ReservedLockerSlotCount}");
                return true;
            }
        }
        slot = -1;
        return false;
    }

    public static bool HasLockerSlot(BodybuilderIdentity identity)
    {
        for (int index = 0; index < lockerSlotReserved.Length; index++)
        {
            if (lockerSlotReserved[index] && lockerSlotOwners[index] == identity)
            {
                return true;
            }
        }
        return false;
    }

    public static bool IsBagVisitor(BodybuilderIdentity identity) =>
        activeBagVisitors.Contains(identity);

    public static void ReleaseLockerSlot(BodybuilderIdentity identity)
    {
        for (int index = 0; index < lockerSlotReserved.Length; index++)
        {
            if (lockerSlotReserved[index] &&
                lockerSlotOwners[index] == identity)
            {
                lockerSlotReserved[index] = false;
                Debug.Log(
                    $"GYMCHAOS_LOCKER_SLOT_RELEASED identity={identity} " +
                    $"slot={index} reserved={ReservedLockerSlotCount}");
                return;
            }
        }
    }

    private static Vector3 GetLockerSlotPosition(int slot)
    {
        int safeSlot = Mathf.Clamp(slot, 0, LockerVisitSlotCount - 1);
        float xOffset = -6.35f;
        float zOffset = (safeSlot - 1.5f) * 1.55f;
        return new Vector3(roomCenter.x + xOffset, roomFloorY, roomCenter.z + zOffset);
    }

    public static bool TryGetLockerVisitRoute(
        Vector3 destination,
        out Vector3[] route,
        out Quaternion visitorRotation)
    {
        route = null;
        visitorRotation = lockerVisitRotation;
        if (!roomBoundsReady)
        {
            GameObject existingRoot = GameObject.Find(RootName);
            if (existingRoot != null)
            {
                RebuildBoundsFromExistingRoot(existingRoot);
            }
        }

        if (!roomBoundsReady)
        {
            return false;
        }

        float northWallZ = roomCenter.z + BackDepth * 0.5f;
        float floorY = roomFloorY;
        Vector3 gymSideOfDoor = new Vector3(
            roomCenter.x, floorY, northWallZ + 1.25f);
        Vector3 roomSideOfDoor = new Vector3(
            roomCenter.x, floorY, northWallZ - 1.25f);
        destination.y = floorY;
        route = new[]
        {
            gymSideOfDoor,
            roomSideOfDoor,
            destination
        };
        visitorRotation = lockerVisitRotation;
        Debug.Log(
            $"GYMCHAOS_LOCKER_ROUTE_OK points={route.Length} " +
            $"gymSide={gymSideOfDoor} roomSide={roomSideOfDoor} target={destination}");
        return true;
    }

    public static void ShowBenchBagsForVisitor(BodybuilderIdentity identity)
    {
        if (!activeBagVisitors.Add(identity)) return;
        if (Random.value > 0.15f) return;
        for (int bench = 0; bench < 2; bench++)
        {
            if (visibleBagCounts[bench] != 0) continue;
            bagOwnerBench.Add(identity, bench);
            visibleBagCounts[bench] = 1;
            visibleBagStartIndices[bench] = Random.Range(0, 2);
            RefreshBenchBagVisibility(bench);
            break;
        }
        RefreshBenchBagVisibility(0);
        RefreshBenchBagVisibility(1);
        Debug.Log($"GYMCHAOS_LOCKER_BAGS_VISIBLE identity={identity} left={visibleBagCounts[0]} right={visibleBagCounts[1]} owners={activeBagVisitors.Count}");
    }

    // Members leave their gym bag on a locker-room bench (one per bench, so
    // at most two) and collect it before they go home.
    public const int MaxMemberBags = 2;

    public static bool TryPlaceMemberBag(BodybuilderIdentity identity)
    {
        if (bagOwnerBench.ContainsKey(identity))
        {
            return true;
        }
        if (bagOwnerBench.Count >= MaxMemberBags)
        {
            return false;
        }
        for (int bench = 0; bench < visibleBagCounts.Length; bench++)
        {
            if (visibleBagCounts[bench] != 0) continue;
            activeBagVisitors.Add(identity);
            bagOwnerBench.Add(identity, bench);
            visibleBagCounts[bench] = 1;
            visibleBagStartIndices[bench] = Random.Range(0, 2);
            RefreshBenchBagVisibility(0);
            RefreshBenchBagVisibility(1);
            Debug.Log(
                $"GYMCHAOS_LOCKER_MEMBER_BAG_PLACED identity={identity} bench={bench} " +
                $"bags={bagOwnerBench.Count}");
            return true;
        }
        return false;
    }

    public static bool HasMemberBag(BodybuilderIdentity identity) =>
        bagOwnerBench.ContainsKey(identity);

    public static int MemberBagCount => bagOwnerBench.Count;

    public static void HideBenchBagsForVisitor(BodybuilderIdentity identity)
    {
        if (!activeBagVisitors.Remove(identity))
        {
            return;
        }

        if (bagOwnerBench.TryGetValue(identity, out int bench))
        {
            bagOwnerBench.Remove(identity);
            visibleBagCounts[bench] = 0;
            RefreshBenchBagVisibility(bench);
            Debug.Log($"GYMCHAOS_LOCKER_MEMBER_BAG_TAKEN identity={identity} bench={bench}");
        }
        // Other owners' bags stay; the visibility gate needs a refresh once
        // the last owner is gone.
        RefreshBenchBagVisibility(0);
        RefreshBenchBagVisibility(1);
    }

    public static void HideBenchBags()
    {
        bool hadVisibleBags = VisibleBenchBagCount > 0 || activeBagVisitors.Count > 0;
        for (int bench = 0; bench < benchBags.Length; bench++)
        {
            visibleBagCounts[bench] = 0;
            visibleBagStartIndices[bench] = 0;
            RefreshBenchBagVisibility(bench);
        }
        activeBagVisitors.Clear();
        bagOwnerBench.Clear();
        if (hadVisibleBags) Debug.Log("GYMCHAOS_LOCKER_BAGS_HIDDEN");
    }

    private static void CreateLockerBays(
        Transform parent, Vector3 center, float floorY, float minX, float maxX,
        Material metal, Material accent)
    {
        CreateModernLockerBank(parent, center.z, floorY, minX + 0.58f, -1, 7, 1.28f, metal, accent);
    }

    public static void CreateModernLockerBank(
        Transform parent,
        float centerZ,
        float floorY,
        float lockerX,
        int side,
        int count,
        float spacing,
        Material metal,
        Material accent)
    {
        side = side < 0 ? -1 : 1;
        count = Mathf.Max(1, count);
        Material door = CreateMaterial(
            "Locker enamel", new Color(0.19f, 0.24f, 0.25f), 0.45f, 0.32f);
        Material recess = CreateMaterial(
            "Locker vents", new Color(0.022f, 0.029f, 0.03f), 0.1f, 0.2f);
        float firstOffset = (count - 1) * 0.5f;
        for (int i = 0; i < count; i++)
        {
            float z = centerZ + (i - firstOffset) * spacing;
            float front = lockerX - side * 0.37f;
            CreateBox("Locker cabinet", parent,
                new Vector3(lockerX, floorY + 1.15f, z),
                new Vector3(0.7f, 2.3f, 1.16f), metal, true);
            for (int bay = -1; bay <= 1; bay += 2)
            {
                float doorZ = z + bay * 0.285f;
                CreateBox("Inset locker door", parent,
                    new Vector3(front, floorY + 1.2f, doorZ),
                    new Vector3(0.035f, 2.08f, 0.545f), door, false);
                CreateBox("Locker pull handle", parent,
                    new Vector3(front - side * 0.055f,
                        floorY + 1.2f, doorZ + 0.17f),
                    new Vector3(0.06f, 0.18f, 0.035f), accent, false);
                CreateBox("Locker number plate", parent,
                    new Vector3(front - side * 0.023f,
                        floorY + 1.85f, doorZ),
                    new Vector3(0.015f, 0.09f, 0.13f), metal, false);
                for (int vent = 0; vent < 3; vent++)
                {
                    CreateBox("Locker ventilation slot", parent,
                        new Vector3(front - side * 0.025f,
                            floorY + 0.34f + vent * 0.055f, doorZ),
                        new Vector3(0.014f, 0.016f, 0.29f), recess, false);
                }
            }
        }
    }

    private static void CreateBenches(
        Transform parent, Vector3 center, float floorY, Material trim, Material accent)
    {
        _ = trim;
        _ = accent;
        float benchX = center.x + BenchOffsetX;
        for (int benchIndex = 0; benchIndex < 2; benchIndex++)
        {
            int slot = benchIndex;
            benchBags[slot].Clear();
            loadedBenches[slot] = null;
            float z = GetBenchCenter(center, floorY, benchIndex).z;
            string label = benchIndex == 0 ? "Left" : "Right";
            RequestLockerProp(WoodenBenchAsset, parent, new Vector3(benchX, floorY, z),
                Quaternion.identity, Vector3.one * 2.7f, "Authored wooden locker bench " + label,
                new Vector3(1f, 0.38f, 0.27f), settleOnSupport: true, addCollider: true,
                onLoaded: loaded =>
                {
                    loadedBenches[slot] = loaded;
                    RefreshBenchBagVisibility(slot);
                });
            CreateBenchBag(parent, benchIndex, new Vector3(benchX - 0.52f, floorY + 1.34f, z), ColorBagAsset, label + " color bag");
            CreateBenchBag(parent, benchIndex, new Vector3(benchX + 0.52f, floorY + 1.34f, z), BlackBagAsset, label + " black bag");
        }
        HideBenchBags();
    }

    internal static Vector3 GetBenchCenter(Vector3 center, float floorY, int benchIndex)
    {
        return new Vector3(center.x + BenchOffsetX, floorY,
            center.z + (benchIndex == 0 ? -BenchOffsetZ : BenchOffsetZ));
    }

    private static void CreateBenchBag(Transform parent, int benchIndex, Vector3 position, string assetPath, string objectName)
    {
        RequestLockerProp(assetPath, parent, position, Quaternion.Euler(0f, 90f, 0f),
            Vector3.one * BenchBagScale, objectName, Vector3.zero,
            settleOnSupport: true, addCollider: false,
            supportY: roomFloorY + BenchSeatSupportHeight,
            onLoaded: loaded =>
            {
                benchBags[benchIndex].Add(loaded);
                RefreshBenchBagVisibility(benchIndex);
            });
    }

    private static void RefreshBenchBagVisibility(int benchIndex)
    {
        if (benchIndex < 0 || benchIndex >= benchBags.Length) return;
        int visibleCount = activeBagVisitors.Count > 0 ? visibleBagCounts[benchIndex] : 0;
        int selectedSingleBag = visibleBagStartIndices[benchIndex];
        for (int index = 0; index < benchBags[benchIndex].Count; index++)
        {
            GameObject bag = benchBags[benchIndex][index];
            bool visible = visibleCount >= 2 ||
                (visibleCount == 1 && index == selectedSingleBag);
            if (bag == null)
            {
                continue;
            }
            bool show = visible && loadedBenches[benchIndex] != null;
            if (loadedBenches[benchIndex] != null)
            {
                // Measure only while active: an inactive renderer does not
                // report its real bounds, and the old refresh moved hidden
                // bags by that stale measurement on every call.
                bag.SetActive(true);
                RestBagOnSeat(bag, loadedBenches[benchIndex]);
            }
            bag.SetActive(show);
        }
    }

    private static void RestBagOnSeat(GameObject bag, GameObject bench)
    {
        if (!TryGetRendererBounds(bench, out Bounds seat) ||
            !TryGetRendererBounds(bag, out Bounds bounds))
        {
            return;
        }

        // Keep the authored left/right slot along the bench and only snap the
        // bag's base onto the seat top, centered across the seat depth.
        float depthShift = seat.center.z - bounds.center.z;
        Bounds footprint = bounds;
        footprint.center += new Vector3(0f, 0f, depthShift);
        Vector3 offset = new Vector3(
            0f,
            GetSeatSurfaceY(bench, footprint, seat.max.y) + 0.005f - bounds.min.y,
            depthShift);
        bag.transform.position += offset;
    }

    // The authored bench mesh has a stray raised spike at one end, so its
    // renderer bounds top sits ~0.24 m above the slats. Read the slat surface
    // from the vertices directly under the bag, trimming isolated outliers.
    private static float GetSeatSurfaceY(GameObject bench, Bounds footprint, float fallback)
    {
        List<float> heights = new List<float>();
        foreach (MeshFilter filter in bench.GetComponentsInChildren<MeshFilter>(true))
        {
            Mesh mesh = filter.sharedMesh;
            if (mesh == null || !mesh.isReadable) continue;
            Matrix4x4 matrix = filter.transform.localToWorldMatrix;
            Vector3[] vertices = mesh.vertices;
            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 world = matrix.MultiplyPoint3x4(vertices[i]);
                if (world.x >= footprint.min.x && world.x <= footprint.max.x &&
                    world.z >= footprint.min.z && world.z <= footprint.max.z)
                {
                    heights.Add(world.y);
                }
            }
        }
        if (heights.Count < 8)
        {
            return fallback;
        }
        heights.Sort();
        return heights[Mathf.Min(heights.Count - 1, Mathf.FloorToInt(heights.Count * 0.995f))];
    }

    private static bool TryGetRendererBounds(GameObject root, out Bounds bounds)
    {
        bounds = default;
        bool found = false;
        foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(false))
        {
            if (!renderer.enabled) continue;
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

    internal static float MaxVisibleBenchBagSeatGapForVerification()
    {
        float worst = 0f;
        for (int bench = 0; bench < benchBags.Length; bench++)
        {
            if (loadedBenches[bench] == null ||
                !TryGetRendererBounds(loadedBenches[bench], out Bounds seat))
            {
                continue;
            }
            foreach (GameObject bag in benchBags[bench])
            {
                if (bag == null || !bag.activeInHierarchy ||
                    !TryGetRendererBounds(bag, out Bounds bounds))
                {
                    continue;
                }
                worst = Mathf.Max(worst, Mathf.Abs(bounds.min.y -
                    GetSeatSurfaceY(loadedBenches[bench], bounds, seat.max.y)));
            }
        }
        return worst;
    }

    private static GameObject RequestLockerProp(
        string assetPath,
        Transform parent,
        Vector3 position,
        Quaternion rotation,
        Vector3 scale,
        string objectName,
        Vector3 colliderSize,
        bool settleOnSupport,
        bool addCollider,
        float supportY = float.NaN,
        System.Action<GameObject> onLoaded = null)
    {
        authoredLockerPropRequests++;
        return RuntimeGlbSceneLoader.Request(
            assetPath,
            parent,
            position,
            rotation,
            scale,
            objectName,
            0,
            settleOnSupport,
            float.IsNaN(supportY) ? roomFloorY : supportY,
            loaded =>
            {
                if (loaded == null)
                {
                    Debug.LogError(
                        $"GYMCHAOS_LOCKER_PROP_LOAD_FAIL path={assetPath} " +
                        $"object={objectName}", parent);
                    return;
                }

                if (addCollider && colliderSize.sqrMagnitude > 0.001f)
                {
                    BoxCollider collider = loaded.AddComponent<BoxCollider>();
                    collider.size = colliderSize;
                    collider.isTrigger = false;
                }

                authoredLockerPropsLoaded++;
                onLoaded?.Invoke(loaded);
                Debug.Log(
                    $"GYMCHAOS_LOCKER_PROP_READY path={assetPath} " +
                    $"object={objectName} loaded={authoredLockerPropsLoaded}/" +
                    $"{authoredLockerPropRequests}", loaded);
            });
    }
    private static void CreateBathroom(
        Transform parent, Vector3 center, float floorY,
        Material tile, Material metal, Material accent, Material mirror,
        PlayerMovement player)
    {
        float dividerX = center.x + 3.0f;
        float southZ = center.z - BackDepth * 0.5f;
        float northZ = center.z + BackDepth * 0.5f;
        float doorwayCenterZ = center.z + 1.65f;
        const float doorwayWidth = 2.0f;
        float doorwayMinZ = doorwayCenterZ - doorwayWidth * 0.5f;
        float doorwayMaxZ = doorwayCenterZ + doorwayWidth * 0.5f;
        float southLength = doorwayMinZ - southZ;
        float northLength = northZ - doorwayMaxZ;
        CreateBox("Bathroom divider south", parent,
            new Vector3(
                dividerX, floorY + BackHeight * 0.5f,
                southZ + southLength * 0.5f),
            new Vector3(0.18f, BackHeight, southLength), tile, true);
        CreateBox("Bathroom divider north", parent,
            new Vector3(
                dividerX, floorY + BackHeight * 0.5f,
                doorwayMaxZ + northLength * 0.5f),
            new Vector3(0.18f, BackHeight, northLength), tile, true);

        float eastWallInsideX = center.x + BackWidth * 0.5f - 0.12f;

        for (int i = -1; i <= 1; i++)
        {
            float z = center.z - 1.9f + i * 1.45f;
            RequestLockerProp(
                SinkAsset,
                parent,
                new Vector3(eastWallInsideX - 0.78f, floorY + 1.02f, z),
                Quaternion.Euler(0f, -90f, 0f),
                Vector3.one,
                "Authored sink " + (i + 2),
                new Vector3(0.76f, 1f, 0.59f),
                settleOnSupport: true,
                addCollider: true);
        }

        float stallWestX = center.x + 5.55f;
        float stallSouthZ = center.z + 2.15f;
        CreateBox("Bathroom stall west wall", parent,
            new Vector3(
                stallWestX, floorY + BackHeight * 0.5f,
                (stallSouthZ + northZ) * 0.5f),
            new Vector3(0.18f, BackHeight, northZ - stallSouthZ), tile, true);
        float stallDoorMinX = center.x + 5.72f;
        float stallDoorMaxX = center.x + 6.92f;
        float stallEastX = eastWallInsideX;
        float stallLeftLength = stallDoorMinX - stallWestX;
        float stallRightLength = stallEastX - stallDoorMaxX;
        CreateBox("Bathroom stall south wall left", parent,
            new Vector3(
                stallWestX + stallLeftLength * 0.5f,
                floorY + BackHeight * 0.5f, stallSouthZ),
            new Vector3(stallLeftLength, BackHeight, 0.18f), tile, true);
        CreateBox("Bathroom stall south wall right", parent,
            new Vector3(
                stallDoorMaxX + stallRightLength * 0.5f,
                floorY + BackHeight * 0.5f, stallSouthZ),
            new Vector3(stallRightLength, BackHeight, 0.18f), tile, true);
        RequestLockerProp(
            ToiletAsset,
            parent,
            new Vector3(eastWallInsideX - 1.0f, floorY, center.z + 4.25f),
            Quaternion.Euler(0f, -90f, 0f),
            Vector3.one,
            "Authored toilet",
            new Vector3(0.56f, 1f, 0.83f),
            settleOnSupport: true,
            addCollider: true);

        CreateBox("Bathroom soap shelf", parent,
            new Vector3(eastWallInsideX - 0.34f,
                floorY + 1.18f, center.z - 1.9f),
            new Vector3(0.58f, 0.07f, 5.05f), metal, false);
        CreateBox("Bathroom towel shelf", parent,
            new Vector3(eastWallInsideX - 0.34f,
                floorY + 1.48f, center.z - 4.65f),
            new Vector3(0.58f, 0.05f, 0.72f), metal, false);
        for (int towel = 0; towel < 3; towel++)
        {
            CreateBox("Folded towel", parent,
                new Vector3(eastWallInsideX - 0.36f,
                    floorY + 1.54f + towel * 0.06f, center.z - 4.65f),
                new Vector3(0.42f, 0.055f, 0.55f), accent, false);
        }

        CreateBathroomMirrors(
            parent, center, floorY, eastWallInsideX, mirror, metal, player);
        CreateInteractable(parent, "Bathroom Cooldown",
            new Vector3(center.x + 4.15f, floorY + 1.0f, center.z - 0.85f),
            new Vector3(1.2f, 2f, 1.4f),
            GymBackRoomInteractionType.Bathroom, "Use bathroom");
    }
    private static void CreateBathroomMirrors(
        Transform parent, Vector3 center, float floorY, float eastWallInsideX,
        Material mirror, Material frame, PlayerMovement player)
    {
        // Three low-cost reflective panels sit above the three sinks. They are
        // intentionally separate from the large changing-room mirror so the
        // bathroom reads as its own sub-space without stealing attention.
        Renderer[] renderers = new Renderer[3];
        for (int i = -1; i <= 1; i++)
        {
            float z = center.z - 1.9f + i * 1.45f;
            Vector3 panelCenter = new Vector3(
                eastWallInsideX - 0.03f, floorY + 2.05f, z);
            GameObject panel = CreateBox("Bathroom mirror " + (i + 2), parent,
                panelCenter, new Vector3(0.055f, 1.55f, 0.92f), mirror, false);
            renderers[i + 1] = panel.GetComponent<Renderer>();
            CreateBox("Bathroom mirror frame top " + (i + 2), parent,
                panelCenter + Vector3.up * 0.83f,
                new Vector3(0.07f, 0.08f, 1.02f), frame, false);
            CreateBox("Bathroom mirror frame bottom " + (i + 2), parent,
                panelCenter - Vector3.up * 0.83f,
                new Vector3(0.07f, 0.08f, 1.02f), frame, false);
        }
        if (player != null && player.playerCamera != null)
        {
            PlanarGymMirror.Create(
                parent,
                player.playerCamera,
                renderers,
                new Vector3(eastWallInsideX - 0.06f,
                    floorY + 2.05f, center.z - 1.9f),
                Vector3.left);
        }
    }
    private static void CreateMirror(
        Transform parent, Vector3 center, float floorY,
        Material mirror, Material trim, PlayerMovement player)
    {
        Vector3 panelCenter = new Vector3(center.x, floorY + 2.35f, center.z - BackDepth * 0.5f + 0.18f);
        GameObject panel = CreateBox("Locker room mirror panel", parent, panelCenter,
            new Vector3(4.0f, 3.8f, 0.055f), mirror, true);
        panel.transform.rotation = Quaternion.identity;
        panel.AddComponent<GlassShatterPanel>();
        CreateBox("Locker room mirror top frame", parent,
            panelCenter + Vector3.up * 1.98f, new Vector3(4.2f, 0.1f, 0.1f), trim, false);
        CreateBox("Locker room mirror bottom frame", parent,
            panelCenter - Vector3.up * 1.98f, new Vector3(4.2f, 0.1f, 0.1f), trim, false);

        Renderer renderer = panel.GetComponent<Renderer>();
        if (player != null && player.playerCamera != null && renderer != null)
        {
            PlanarGymMirror.Create(parent, player.playerCamera, new[] { renderer },
                panelCenter + Vector3.forward * 0.03f, Vector3.forward);
        }
    }

    private static GymBackRoomInteractable CreateInteractable(
        Transform parent, string name, Vector3 position, Vector3 size,
        GymBackRoomInteractionType type, string displayName, float interactionRange = 0f)
    {
        GameObject objectRoot = new GameObject(name);
        objectRoot.transform.SetParent(parent, true);
        objectRoot.transform.position = position;
        BoxCollider trigger = objectRoot.AddComponent<BoxCollider>();
        trigger.size = size;
        trigger.isTrigger = true;
        GymBackRoomInteractable interactable = objectRoot.AddComponent<GymBackRoomInteractable>();
        interactable.Configure(type, displayName, interactionRange);
        return interactable;
    }

    private static Material CreateMaterial(string name, Color color, float metallic, float smoothness)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        Material material = new Material(shader)
        {
            name = name,
            color = color
        };
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
        string name, Transform parent, Vector3 position, Vector3 scale,
        Material material, bool keepCollider)
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
