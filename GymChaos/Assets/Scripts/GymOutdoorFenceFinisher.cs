using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Final pass over the exterior low walls. Builders place each wall from its
/// own centre line, so neighbouring runs used to overlap by half a wall at
/// corners and leave the outer corner square open. This pass makes every
/// joint seamless: collinear runs butt end to end, an L corner is closed by
/// the run with the free end, and a T joint stops at the face of the wall it
/// meets. It also closes the side strips beside the Protein.com shop so no
/// one can walk around the building.
/// </summary>
public static class GymOutdoorFenceFinisher
{
    private const float Tolerance = 0.03f;
    private const float MaxCollinearGap = 0.3f;

    public static int ResolvedJoints { get; private set; }
    public static int RemainingOverlaps { get; private set; }
    public static int StoreSideLocks { get; private set; }

    private sealed class Wall
    {
        public string name;
        public Transform body;
        public Transform coping;
        public readonly List<Transform> ribs = new List<Transform>();
        public bool alongX;
        public float runMin;
        public float runMax;
        public float crossMin;
        public float crossMax;
        public bool minJoined;
        public bool maxJoined;
        public float CrossCenter => (crossMin + crossMax) * 0.5f;
        public float RunCenter => (runMin + runMax) * 0.5f;
    }

    public static void Finish(Transform exteriorRoot)
    {
        if (exteriorRoot == null)
        {
            return;
        }

        Physics.SyncTransforms();
        StoreSideLocks = BuildStoreSideLocks(exteriorRoot);
        List<Wall> walls = CollectWalls(exteriorRoot);
        int resolved = ButtCollinearRuns(walls);
        MarkJoinedEnds(walls);
        resolved += ResolvePerpendicularJoints(walls);
        for (int i = 0; i < walls.Count; i++)
        {
            Apply(walls[i]);
        }
        Physics.SyncTransforms();
        ResolvedJoints = resolved;
        RemainingOverlaps = CountOverlaps(walls);
        Debug.Log(
            $"GYMCHAOS_FENCE_JOINS_{(RemainingOverlaps == 0 ? "OK" : "FAIL")} " +
            $"walls={walls.Count} resolved={resolved} overlaps={RemainingOverlaps} " +
            $"storeSideLocks={StoreSideLocks}");
    }

    // Low walls that close the strips north and south of the shop, from its
    // front corners to the road wall and to the site's south wall.
    private static int BuildStoreSideLocks(Transform root)
    {
        // The shop GLB loads asynchronously, so use the shell footprint the
        // store builder computed instead of its runtime colliders.
        Bounds shell = GymProteinStoreEnvironment.ShellFootprint;
        Collider roadWall = FindCollider(root, "Visitor Road South Wall Collision");
        Collider siteSouth = FindCollider(root, "Protein Store Perimeter South Collision");
        Transform wallTemplate = FindChild(root, "Protein Store Perimeter South Low Wall");
        Transform copingTemplate = FindChild(root, "Protein Store Perimeter South Coping");
        if (!GymProteinStoreEnvironment.IsBuilt || shell.size.x < 1f || roadWall == null ||
            siteSouth == null || wallTemplate == null || copingTemplate == null)
        {
            Debug.LogWarning("GYMCHAOS_STORE_SIDE_LOCK_SKIPPED reason=missing_reference");
            return 0;
        }

        Transform parent = wallTemplate.parent;
        float thickness = GymOutdoorBuilder.SharedFenceWallThickness;
        // Flush with the facade, directly behind its corner pillars.
        float x = GymProteinStoreEnvironment.FacadeX + thickness * 0.5f;
        int built = 0;
        built += CreateLock(parent, wallTemplate, copingTemplate, "Protein Store Side Lock South",
            x, siteSouth.bounds.max.z, shell.min.z, thickness) ? 1 : 0;
        built += CreateLock(parent, wallTemplate, copingTemplate, "Protein Store Side Lock North",
            x, shell.max.z, roadWall.bounds.min.z, thickness) ? 1 : 0;
        return built;
    }

    private static bool CreateLock(Transform parent, Transform wallTemplate,
        Transform copingTemplate, string name, float x, float minZ, float maxZ, float thickness)
    {
        float length = maxZ - minZ;
        if (length < 0.2f)
        {
            return false;
        }

        float centerZ = (minZ + maxZ) * 0.5f;
        Bounds wallBounds = wallTemplate.GetComponent<Renderer>().bounds;
        Bounds copingBounds = copingTemplate.GetComponent<Renderer>().bounds;
        GameObject wall = Object.Instantiate(wallTemplate.gameObject, parent);
        wall.name = name + " Low Wall";
        wall.transform.position = new Vector3(x, wallBounds.center.y, centerZ);
        wall.transform.localScale = new Vector3(thickness, wallBounds.size.y, length);
        GameObject coping = Object.Instantiate(copingTemplate.gameObject, parent);
        coping.name = name + " Coping";
        coping.transform.position = new Vector3(x, copingBounds.center.y, centerZ);
        coping.transform.localScale = new Vector3(thickness, copingBounds.size.y, length);

        GameObject collision = new GameObject(name + " Collision");
        collision.transform.SetParent(parent, true);
        collision.transform.position = new Vector3(x,
            wallBounds.min.y + GymOutdoorBuilder.SharedFenceCollisionHeight * 0.5f, centerZ);
        BoxCollider collider = collision.AddComponent<BoxCollider>();
        collider.size = new Vector3(thickness, GymOutdoorBuilder.SharedFenceCollisionHeight, length);
        return true;
    }

    private static List<Wall> CollectWalls(Transform root)
    {
        List<Wall> walls = new List<Wall>();
        Dictionary<string, Transform> byName = new Dictionary<string, Transform>();
        Transform[] nodes = root.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < nodes.Length; i++)
        {
            if (!byName.ContainsKey(nodes[i].name)) byName[nodes[i].name] = nodes[i];
        }

        for (int i = 0; i < nodes.Length; i++)
        {
            Transform node = nodes[i];
            string name = node.name;
            string baseName = name.EndsWith(" Low Wall")
                ? name.Substring(0, name.Length - " Low Wall".Length)
                : name;
            if (!byName.TryGetValue(baseName + " Coping", out Transform coping) ||
                coping.parent != node.parent || name.EndsWith(" Coping"))
            {
                continue;
            }

            Renderer renderer = node.GetComponent<Renderer>();
            if (renderer == null || !IsAxisAligned(node) || !IsAxisAligned(coping))
            {
                continue;
            }

            Bounds bounds = renderer.bounds;
            Wall wall = new Wall
            {
                name = name,
                body = node,
                coping = coping,
                alongX = bounds.size.x >= bounds.size.z
            };
            wall.runMin = wall.alongX ? bounds.min.x : bounds.min.z;
            wall.runMax = wall.alongX ? bounds.max.x : bounds.max.z;
            wall.crossMin = wall.alongX ? bounds.min.z : bounds.min.x;
            wall.crossMax = wall.alongX ? bounds.max.z : bounds.max.x;
            if (wall.crossMax - wall.crossMin > 1.2f)
            {
                continue;
            }
            for (int r = 0; r < node.parent.childCount; r++)
            {
                Transform sibling = node.parent.GetChild(r);
                if (sibling.name.StartsWith(baseName + " Vertical Rib"))
                {
                    wall.ribs.Add(sibling);
                }
            }
            walls.Add(wall);
        }
        return walls;
    }

    private static int ButtCollinearRuns(List<Wall> walls)
    {
        int resolved = 0;
        for (int a = 0; a < walls.Count; a++)
        {
            for (int b = a + 1; b < walls.Count; b++)
            {
                Wall first = walls[a];
                Wall second = walls[b];
                if (first.alongX != second.alongX ||
                    Mathf.Abs(first.CrossCenter - second.CrossCenter) > 0.12f)
                {
                    continue;
                }

                Wall low = first.runMin <= second.runMin ? first : second;
                Wall high = low == first ? second : first;
                float overlap = low.runMax - high.runMin;
                // Overlapping runs, or runs that stop just short of each
                // other (up to half a wall), meet at one seam.
                if (Mathf.Abs(overlap) <= 0.001f || overlap < -MaxCollinearGap ||
                    high.runMax <= low.runMax)
                {
                    continue;
                }

                float seam = (low.runMax + high.runMin) * 0.5f;
                low.runMax = seam;
                high.runMin = seam;
                // Collinear runs share one line: take the first run's band.
                high.crossMin = low.crossMin;
                high.crossMax = low.crossMax;
                resolved++;
            }
        }
        return resolved;
    }

    private static void MarkJoinedEnds(List<Wall> walls)
    {
        for (int a = 0; a < walls.Count; a++)
        {
            for (int b = 0; b < walls.Count; b++)
            {
                if (a == b) continue;
                Wall wall = walls[a];
                Wall other = walls[b];
                if (wall.alongX != other.alongX ||
                    Mathf.Abs(wall.CrossCenter - other.CrossCenter) > 0.12f)
                {
                    continue;
                }
                if (Mathf.Abs(wall.runMax - other.runMin) <= Tolerance) wall.maxJoined = true;
                if (Mathf.Abs(wall.runMin - other.runMax) <= Tolerance) wall.minJoined = true;
            }
        }
    }

    private static int ResolvePerpendicularJoints(List<Wall> walls)
    {
        int resolved = 0;
        for (int a = 0; a < walls.Count; a++)
        {
            for (int b = 0; b < walls.Count; b++)
            {
                Wall along = walls[a];
                Wall across = walls[b];
                if (!along.alongX || across.alongX)
                {
                    continue;
                }
                if (ResolvePair(along, across))
                {
                    resolved++;
                }
            }
        }
        return resolved;
    }

    // Both walls are expressed in the same frame: "run" of one is the
    // "cross" of the other.
    private static bool ResolvePair(Wall a, Wall b)
    {
        int aEnd = NearEnd(a, b);
        int bEnd = NearEnd(b, a);
        bool aPassesB = a.runMin <= b.crossMin + Tolerance && a.runMax >= b.crossMax - Tolerance;
        bool bPassesA = b.runMin <= a.crossMin + Tolerance && b.runMax >= a.crossMax - Tolerance;
        if (aEnd != 0 && bEnd != 0)
        {
            bool aFree = !IsJoined(a, aEnd);
            bool bFree = !IsJoined(b, bEnd);
            if (aFree && bFree)
            {
                return SetEnd(a, aEnd, FarFace(b, a)) | SetEnd(b, bEnd, NearFace(a, b));
            }
            if (bFree)
            {
                return SetEnd(b, bEnd, NearFace(a, b));
            }
            if (aFree)
            {
                return SetEnd(a, aEnd, NearFace(b, a));
            }
            return false;
        }
        if (bEnd != 0 && aPassesB)
        {
            return SetEnd(b, bEnd, NearFace(a, b));
        }
        if (aEnd != 0 && bPassesA)
        {
            return SetEnd(a, aEnd, NearFace(b, a));
        }
        return false;
    }

    // +1 or -1 when wall's max/min end lies on the other wall's band and the
    // wall actually reaches the other wall's run; 0 otherwise.
    private static int NearEnd(Wall wall, Wall other)
    {
        bool bandTouches = wall.crossMax >= other.runMin - Tolerance &&
            wall.crossMin <= other.runMax + Tolerance;
        if (!bandTouches)
        {
            return 0;
        }
        if (wall.runMax >= other.crossMin - Tolerance && wall.runMax <= other.crossMax + Tolerance &&
            wall.runMin < other.crossMin - Tolerance)
        {
            return 1;
        }
        if (wall.runMin >= other.crossMin - Tolerance && wall.runMin <= other.crossMax + Tolerance &&
            wall.runMax > other.crossMax + Tolerance)
        {
            return -1;
        }
        return 0;
    }

    private static bool IsJoined(Wall wall, int end)
    {
        return end > 0 ? wall.maxJoined : wall.minJoined;
    }

    // Face of `target` that faces `from`'s body.
    private static float NearFace(Wall target, Wall from)
    {
        return from.RunCenter < target.CrossCenter ? target.crossMin : target.crossMax;
    }

    private static float FarFace(Wall target, Wall from)
    {
        return from.RunCenter < target.CrossCenter ? target.crossMax : target.crossMin;
    }

    private static bool SetEnd(Wall wall, int end, float value)
    {
        float current = end > 0 ? wall.runMax : wall.runMin;
        if (Mathf.Abs(current - value) <= 0.001f)
        {
            return false;
        }
        if (end > 0) wall.runMax = value;
        else wall.runMin = value;
        return true;
    }

    private static void Apply(Wall wall)
    {
        ApplyRun(wall.body, wall);
        ApplyRun(wall.coping, wall);
        for (int i = 0; i < wall.ribs.Count; i++)
        {
            Transform rib = wall.ribs[i];
            if (rib == null) continue;
            float position = wall.alongX ? rib.position.x : rib.position.z;
            if (position < wall.runMin + 0.05f || position > wall.runMax - 0.05f)
            {
                rib.gameObject.SetActive(false);
            }
        }
    }

    private static void ApplyRun(Transform part, Wall wall)
    {
        if (part == null) return;
        float length = Mathf.Max(0.05f, wall.runMax - wall.runMin);
        float thickness = wall.crossMax - wall.crossMin;
        Vector3 position = part.position;
        Vector3 scale = part.localScale;
        Vector3 lossy = part.lossyScale;
        Vector3 parentScale = new Vector3(
            Mathf.Approximately(scale.x, 0f) ? 1f : lossy.x / scale.x,
            Mathf.Approximately(scale.y, 0f) ? 1f : lossy.y / scale.y,
            Mathf.Approximately(scale.z, 0f) ? 1f : lossy.z / scale.z);
        if (wall.alongX)
        {
            position.x = wall.RunCenter;
            position.z = wall.CrossCenter;
            scale.x = length / parentScale.x;
            scale.z = thickness / parentScale.z;
        }
        else
        {
            position.z = wall.RunCenter;
            position.x = wall.CrossCenter;
            scale.z = length / parentScale.z;
            scale.x = thickness / parentScale.x;
        }
        part.position = position;
        part.localScale = scale;
    }

    private static int CountOverlaps(List<Wall> walls)
    {
        int overlaps = 0;
        for (int a = 0; a < walls.Count; a++)
        {
            for (int b = a + 1; b < walls.Count; b++)
            {
                Bounds first = walls[a].body.GetComponent<Renderer>().bounds;
                Bounds second = walls[b].body.GetComponent<Renderer>().bounds;
                float x = Mathf.Min(first.max.x, second.max.x) - Mathf.Max(first.min.x, second.min.x);
                float z = Mathf.Min(first.max.z, second.max.z) - Mathf.Max(first.min.z, second.min.z);
                if (x > Tolerance && z > Tolerance)
                {
                    overlaps++;
                    Debug.LogWarning(
                        $"GYMCHAOS_FENCE_JOIN_OVERLAP first={walls[a].name} second={walls[b].name} " +
                        $"size={x:F2}x{z:F2}");
                }
            }
        }
        return overlaps;
    }

    private static bool IsAxisAligned(Transform transform)
    {
        Vector3 forward = transform.forward;
        return Mathf.Abs(forward.y) < 0.01f &&
            (Mathf.Abs(forward.x) < 0.01f || Mathf.Abs(forward.z) < 0.01f);
    }

    private static Transform FindChild(Transform root, string name)
    {
        Transform[] nodes = root.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < nodes.Length; i++)
        {
            if (nodes[i].name == name) return nodes[i];
        }
        return null;
    }

    private static Collider FindCollider(Transform root, string name)
    {
        Transform node = FindChild(root, name);
        return node != null ? node.GetComponent<Collider>() : null;
    }
}
