using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Opens the park south of the gym path to the player. The short wall that
/// closed the path's south end between the gym and protein.com is gone; the
/// lawn behind it is closed instead by the gym, the locker room, the store
/// site walls and two new low walls in the shared fence style (south along
/// the locker room line, east continuing the store's east wall). The park
/// round the parking stays outside the fences.
///
/// The open part gets a walkable ground collider, colliders on its trees,
/// bushes, benches and lamps, and the abandoned tent where the homeless guy
/// lives (see <see cref="GymHomelessAgent"/>).
/// </summary>
public static class GymParkAccess
{
    public const string RootName = "Park Access (Runtime)";
    public const string GroundName = "Park Walkable Ground";
    public const string SouthWallName = "Park South Boundary";
    public const string EastWallName = "Park East Boundary";
    public const string GateWestName = "Park Gate West";
    public const string GateEastName = "Park Gate East";
    // Opening left in the old path-end wall line for the park path (the
    // gravel path is 1.9 m wide; a little room either side).
    public const float GateWidth = 2.8f;
    public const string TentName = "Park Abandoned Tent";
    private const string TentAsset = "BodyBuilders/outside/abandoned_tent.glb";
    // The source tent is 1.65 m tall; the game cast stands about 2.1-2.5 m.
    public const float TentScale = 1.3f;
    // Tent with its guy ropes and stakes, as a circle round its centre.
    public const float TentFootprintRadius = 2.9f;
    // Lawn kept free of trees and bushes round the tent.
    public const float TentPlantClearance = 3.6f;
    // Half the groundsheet (2.56 x 2.46 m source) plus a margin: no grass
    // blades grow through the tent floor.
    public const float TentFloorHalf = 1.28f * TentScale + 0.15f;

    private static readonly List<Rect> walkAreas = new List<Rect>();
    private static Transform root;

    public static bool IsBuilt { get; private set; }
    public static float FloorY { get; private set; }
    public static Bounds Area { get; private set; }
    // Middle of the former path wall, where the path meets the park.
    public static Vector3 EntrancePoint { get; private set; }
    public static GameObject Tent { get; private set; }
    public static bool TentReady { get; private set; }
    // Tent placement on the lawn, chosen by the park layout.
    public static Vector3 TentCenter { get; private set; }
    // Unit direction out of the tent door.
    public static Vector3 TentDoorDirection { get; private set; }
    // Sleeping bag (pillow at the back wall) in world space.
    public static Bounds TentBedBounds { get; private set; }
    // The sleeping bag alone (no mat edge, pillow, blanket or clothes).
    public static Bounds TentBagBounds { get; private set; }
    // World-space floor depth of the tent along its door axis, from the
    // centre to the door edge.
    public static float TentDoorDepth { get; private set; }
    public static int PlantColliders { get; private set; }
    public static int PropColliders { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        IsBuilt = false;
        TentReady = false;
        Tent = null;
        root = null;
        walkAreas.Clear();
        PlantColliders = 0;
        PropColliders = 0;
    }

    public static bool Contains(Vector3 point, float margin = 0f)
    {
        for (int i = 0; i < walkAreas.Count; i++)
        {
            Rect area = walkAreas[i];
            if (point.x >= area.xMin + margin && point.x <= area.xMax - margin &&
                point.z >= area.yMin + margin && point.z <= area.yMax - margin)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Builds the boundary walls and the walkable ground. Runs before the
    /// fence finisher (so the new runs join the store walls seamlessly) and
    /// before the park layout (so paths and hedges follow the new walls).
    /// </summary>
    public static void BuildBoundary(
        Transform parent,
        float floorY,
        float gymEastX,
        float pathSouthZ,
        Material wall,
        Material trim,
        Material ribs)
    {
        IsBuilt = false;
        TentReady = false;
        Tent = null;
        walkAreas.Clear();
        PlantColliders = 0;
        PropColliders = 0;
        if (parent == null || !GymProteinStoreEnvironment.IsBuilt)
        {
            Debug.LogWarning("GYMCHAOS_PARK_ACCESS_FAIL reason=no-store-site");
            return;
        }

        Bounds site = GymProteinStoreEnvironment.OuterRouteBounds;
        float storeEastX = site.max.x;
        float storeSouthZ = site.min.z;
        if (!TryGetWallBounds("Locker Room East Wall", out Bounds lockerEast) ||
            !TryGetWallBounds("Locker Room South Wall", out Bounds lockerSouth) ||
            !TryGetWallBounds("South Wall East of Locker Room", out Bounds gymSouth))
        {
            Debug.LogWarning("GYMCHAOS_PARK_ACCESS_FAIL reason=no-gym-walls");
            return;
        }

        float lockerEastX = lockerEast.max.x;
        float gymSouthZ = gymSouth.min.z;
        float southZ = lockerSouth.center.z;
        FloorY = floorY;

        GameObject rootObject = new GameObject(RootName);
        rootObject.transform.SetParent(parent, false);
        root = rootObject.transform;

        // South wall on the locker room's south line, east wall on the
        // store's east wall line. Both meet the existing walls end to end.
        GymProteinStoreEnvironment.CreateAccessFence(
            parent, floorY, lockerEastX, storeEastX, southZ, southZ,
            wall, trim, ribs, SouthWallName);
        GymProteinStoreEnvironment.CreateAccessFence(
            parent, floorY, storeEastX, storeEastX, southZ, storeSouthZ,
            wall, trim, ribs, EastWallName);

        float half = GymOutdoorBuilder.SharedFenceWallThickness * 0.5f;
        // Lawn east of the gym and south of the store, then the strip south
        // of the gym beside the locker room.
        walkAreas.Add(Rect.MinMaxRect(gymEastX, southZ + half, storeEastX - half, pathSouthZ));
        walkAreas.Add(Rect.MinMaxRect(lockerEastX, southZ + half, gymEastX, gymSouthZ));
        Bounds area = new Bounds(
            new Vector3((lockerEastX + storeEastX) * 0.5f, floorY, (southZ + pathSouthZ) * 0.5f),
            new Vector3(storeEastX - lockerEastX, 0.2f, pathSouthZ - southZ));
        Area = area;
        EntrancePoint = new Vector3(
            (gymEastX + site.min.x) * 0.5f, floorY, pathSouthZ - 1.2f);

        // The asphalt path used to end in a straight edge on the lawn. Close
        // that line again with low walls on both sides and leave only a
        // gate in the middle, where the park's gravel path starts.
        float gateMin = EntrancePoint.x - GateWidth * 0.5f;
        float gateMax = EntrancePoint.x + GateWidth * 0.5f;
        GymProteinStoreEnvironment.CreateAccessFence(
            parent, floorY, gymEastX, gateMin, pathSouthZ, pathSouthZ,
            wall, trim, ribs, GateWestName);
        GymProteinStoreEnvironment.CreateAccessFence(
            parent, floorY, gateMax, site.min.x, pathSouthZ, pathSouthZ,
            wall, trim, ribs, GateEastName);
        GymOutdoorFoliage.RequestPathEntrance(
            new Vector3(EntrancePoint.x, floorY, pathSouthZ), Vector3.back, GateWidth);

        // One invisible walkable slab just under the park ground surface.
        // It has no renderer, so the park layout still treats it as lawn.
        float groundTop = floorY + GymOutdoorFoliage.GroundTopOffset;
        for (int i = 0; i < walkAreas.Count; i++)
        {
            Rect r = walkAreas[i];
            GameObject ground = new GameObject(GroundName + " " + (i + 1));
            ground.transform.SetParent(root, false);
            ground.transform.position = new Vector3(r.center.x, groundTop - 0.25f, r.center.y);
            BoxCollider box = ground.AddComponent<BoxCollider>();
            // Overlap the neighbouring floors a little so no seam opens.
            box.size = new Vector3(r.width + 0.6f, 0.5f, r.height + 0.6f);
        }

        GymOutdoorFoliage.RequestReservedSpot(
            walkAreas, EntrancePoint, TentFootprintRadius, TentPlantClearance, TentFloorHalf);
        IsBuilt = true;
        Debug.Log(
            $"GYMCHAOS_PARK_ACCESS_BUILT area=({lockerEastX:F2},{southZ:F2})->({storeEastX:F2},{pathSouthZ:F2}) " +
            $"gymEast={gymEastX:F2} gymSouth={gymSouthZ:F2} storeSouth={storeSouthZ:F2} " +
            $"entrance={EntrancePoint}");
    }

    /// <summary>Places the tent on the reserved lawn spot once the park is laid out.</summary>
    public static void PlaceTent()
    {
        if (!IsBuilt || root == null)
        {
            return;
        }
        if (!GymOutdoorFoliage.HasReservedSpot)
        {
            Debug.LogWarning("GYMCHAOS_PARK_TENT_FAIL reason=no-lawn-spot");
            return;
        }

        TentCenter = GymOutdoorFoliage.ReservedSpot;
        Vector3 door = GymOutdoorFoliage.ReservedFacing;
        // Square to the park grid, like the paths and walls round it.
        TentDoorDirection = Mathf.Abs(door.x) > Mathf.Abs(door.z)
            ? new Vector3(Mathf.Sign(door.x), 0f, 0f)
            : new Vector3(0f, 0f, door.z >= 0f ? 1f : -1f);
        float groundTop = FloorY + GymOutdoorFoliage.GroundTopOffset;
        Tent = RuntimeGlbSceneLoader.Request(
            TentAsset,
            root,
            new Vector3(TentCenter.x, groundTop, TentCenter.z),
            Quaternion.LookRotation(TentDoorDirection, Vector3.up),
            Vector3.one * TentScale,
            TentName,
            0,
            settleOnSupport: false,
            onLoaded: OnTentLoaded);
    }

    private static void OnTentLoaded(GameObject tent)
    {
        if (tent == null || root == null || tent.transform.parent != root)
        {
            Debug.LogError("GYMCHAOS_PARK_TENT_FAIL reason=load");
            return;
        }
        Tent = tent;

        // The loader splits every node into one object per material
        // ("Tent_Canvas Primitive 2"); group them back under their node.
        string[] partNames =
        {
            "Tent_Bedding", "Tent_Canvas", "Tent_Floor", "Tent_Poles", "Tent_Props",
            "Tent_Rocks", "Tent_Ropes", "Tent_Stakes", "Tent_Tape"
        };
        GroupParts(tent.transform, partNames);
        Transform bedding = FindChild(tent.transform, "Tent_Bedding");
        Transform props = FindChild(tent.transform, "Tent_Props");
        Transform floor = FindChild(tent.transform, "Tent_Floor");
        Transform canvas = FindChild(tent.transform, "Tent_Canvas");
        if (bedding == null || props == null || floor == null || canvas == null)
        {
            Debug.LogError("GYMCHAOS_PARK_TENT_FAIL reason=parts");
            return;
        }

        // The doormat (in Tent_Props) lies outside the door, so the props
        // reach furthest out on the door side. Turn the tent so that side
        // faces the chosen direction (towards the nearest path).
        Bounds floorBounds = RendererBounds(floor);
        Vector3 propsOffset = RendererBounds(props).center - floorBounds.center;
        propsOffset.y = 0f;
        Vector3 localProps = tent.transform.InverseTransformDirection(propsOffset);
        Vector3 localDoor = Mathf.Abs(localProps.x) > Mathf.Abs(localProps.z)
            ? new Vector3(Mathf.Sign(localProps.x), 0f, 0f)
            : new Vector3(0f, 0f, Mathf.Sign(localProps.z));
        Vector3 worldDoor = tent.transform.TransformDirection(localDoor);
        worldDoor.y = 0f;
        Quaternion turn = Quaternion.FromToRotation(worldDoor.normalized, TentDoorDirection);
        Vector3 pivot = floorBounds.center;
        tent.transform.rotation = turn * tent.transform.rotation;
        tent.transform.position = pivot + turn * (tent.transform.position - pivot);
        // Centre the floor on the reserved spot and rest it on the lawn.
        floorBounds = RendererBounds(floor);
        Vector3 shift = TentCenter - floorBounds.center;
        shift.y = FloorY + GymOutdoorFoliage.GroundTopOffset + 0.005f - floorBounds.min.y;
        tent.transform.position += shift;

        // The sleeping bag keeps its authored layout: pillow at the back
        // wall, foot end at the door.
        TentBedBounds = RendererBounds(bedding);
        TentBagBounds = TentBedBounds;
        foreach (Renderer part in bedding.GetComponentsInChildren<Renderer>(true))
        {
            Material material = part.sharedMaterial;
            if (material != null && material.name.IndexOf("sleeping_bag", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                TentBagBounds = part.bounds;
                break;
            }
        }
        floorBounds = RendererBounds(floor);
        TentDoorDepth = Mathf.Abs(Vector3.Dot(floorBounds.extents, AbsAxis(TentDoorDirection)));

        // Solid canvas (the door stays open), rocks and props.
        AddMeshCollider(canvas);
        AddMeshCollider(FindChild(tent.transform, "Tent_Rocks"));
        Renderer[] renderers = tent.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            renderers[i].shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderers[i].lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            renderers[i].reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        }
        Physics.SyncTransforms();
        TentReady = true;
        Debug.Log(
            $"GYMCHAOS_PARK_TENT_OK center={TentCenter} door={TentDoorDirection} " +
            $"bed={TentBedBounds.center} bedSize={TentBedBounds.size} bag={TentBagBounds.center} " +
            $"bagSize={TentBagBounds.size} doorDepth={TentDoorDepth:F2} " +
            $"renderers={renderers.Length}");
    }

    /// <summary>Gives a loaded park plant inside the open area a trunk/body collider.</summary>
    public static void AddPlantCollider(GameObject plant, bool tree)
    {
        if (plant == null || !IsBuilt || !Contains(plant.transform.position, -1f))
        {
            return;
        }
        Bounds bounds = RendererBounds(plant.transform);
        if (bounds.size.sqrMagnitude < 1e-4f)
        {
            return;
        }
        GameObject shell = new GameObject(tree ? "Park Tree Trunk Collision" : "Park Bush Collision");
        shell.transform.SetParent(plant.transform, true);
        float bottom = FloorY + GymOutdoorFoliage.GroundTopOffset;
        float height = tree ? 3.2f : Mathf.Max(0.5f, bounds.max.y - bottom);
        float radius = tree
            ? 0.32f
            : Mathf.Clamp(Mathf.Min(bounds.extents.x, bounds.extents.z) * 0.6f, 0.25f, 0.9f);
        shell.transform.position = new Vector3(
            plant.transform.position.x, bottom + height * 0.5f, plant.transform.position.z);
        shell.transform.rotation = Quaternion.identity;
        Vector3 lossy = shell.transform.lossyScale;
        CapsuleCollider capsule = shell.AddComponent<CapsuleCollider>();
        float sx = Mathf.Max(0.0001f, Mathf.Max(lossy.x, lossy.z));
        float sy = Mathf.Max(0.0001f, lossy.y);
        capsule.radius = radius / sx;
        capsule.height = Mathf.Max(height, radius * 2f) / sy;
        capsule.direction = 1;
        PlantColliders++;
    }

    /// <summary>Solid seat/back for a bench or a pole for a lamp inside the open area.</summary>
    public static void AddPropCollider(Transform parent, Vector3 basePosition, Quaternion facing, bool bench)
    {
        if (parent == null || !IsBuilt || !Contains(basePosition, -1f))
        {
            return;
        }
        float s = bench ? GymOutdoorFoliage.BenchScale : 1f;
        GameObject shell = new GameObject(bench ? "Park Bench Collision" : "Park Lamp Collision");
        shell.transform.SetParent(parent, false);
        shell.transform.SetPositionAndRotation(basePosition, facing);
        if (bench)
        {
            BoxCollider seat = shell.AddComponent<BoxCollider>();
            seat.center = new Vector3(0f, 0.25f, 0f) * s;
            seat.size = new Vector3(1.7f, 0.5f, 0.46f) * s;
            BoxCollider back = shell.AddComponent<BoxCollider>();
            back.center = new Vector3(0f, 0.65f, -0.24f) * s;
            back.size = new Vector3(1.7f, 0.6f, 0.06f) * s;
        }
        else
        {
            BoxCollider pole = shell.AddComponent<BoxCollider>();
            pole.center = new Vector3(0f, 1.8f, 0f);
            pole.size = new Vector3(0.3f, 3.6f, 0.3f);
        }
        PropColliders++;
    }

    private static void GroupParts(Transform tent, string[] partNames)
    {
        MeshRenderer[] renderers = tent.GetComponentsInChildren<MeshRenderer>(true);
        Dictionary<string, Transform> groups = new Dictionary<string, Transform>();
        for (int i = 0; i < renderers.Length; i++)
        {
            Transform part = renderers[i].transform;
            for (int n = 0; n < partNames.Length; n++)
            {
                string name = partNames[n];
                if (part.name != name && !part.name.StartsWith(name + " ", System.StringComparison.Ordinal))
                {
                    continue;
                }
                if (!groups.TryGetValue(name, out Transform group))
                {
                    group = new GameObject(name).transform;
                    group.SetParent(part.parent, false);
                    groups[name] = group;
                }
                part.SetParent(group, true);
                break;
            }
        }
    }

    private static void AddMeshCollider(Transform part)
    {
        if (part == null)
        {
            return;
        }
        MeshFilter[] filters = part.GetComponentsInChildren<MeshFilter>(true);
        for (int i = 0; i < filters.Length; i++)
        {
            if (filters[i].sharedMesh == null)
            {
                continue;
            }
            MeshCollider collider = filters[i].gameObject.AddComponent<MeshCollider>();
            collider.sharedMesh = filters[i].sharedMesh;
        }
    }

    private static bool TryGetWallBounds(string name, out Bounds bounds)
    {
        bounds = default;
        GameObject wall = GameObject.Find(name);
        if (wall == null)
        {
            return false;
        }
        Collider collider = wall.GetComponent<Collider>();
        Renderer renderer = wall.GetComponent<Renderer>();
        if (collider != null)
        {
            bounds = collider.bounds;
            return true;
        }
        if (renderer != null)
        {
            bounds = renderer.bounds;
            return true;
        }
        return false;
    }

    private static Bounds RendererBounds(Transform node)
    {
        Renderer[] renderers = node.GetComponentsInChildren<Renderer>(true);
        Bounds bounds = new Bounds(node.position, Vector3.zero);
        bool any = false;
        for (int i = 0; i < renderers.Length; i++)
        {
            if (!any)
            {
                bounds = renderers[i].bounds;
                any = true;
            }
            else
            {
                bounds.Encapsulate(renderers[i].bounds);
            }
        }
        return bounds;
    }

    private static Vector3 AbsAxis(Vector3 direction)
    {
        return new Vector3(Mathf.Abs(direction.x), Mathf.Abs(direction.y), Mathf.Abs(direction.z));
    }

    private static Transform FindChild(Transform node, string name)
    {
        if (node == null)
        {
            return null;
        }
        if (node.name == name)
        {
            return node;
        }
        for (int i = 0; i < node.childCount; i++)
        {
            Transform found = FindChild(node.GetChild(i), name);
            if (found != null)
            {
                return found;
            }
        }
        return null;
    }
}
