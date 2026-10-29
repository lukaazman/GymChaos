using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Removes the city towers that stand on the race road and puts them back
/// after the race. The city is eight copies of one joined GLB mesh, so the
/// carver assigns every triangle to a building footprint exported from the
/// Blender source (<c>Tools/export_city_building_footprints.py</c>) and swaps
/// each copy's renderers to a mesh without the blocked buildings.
/// </summary>
public static class GymRaceCityCarver
{
    private const string CityRootName = "City Dystopia Surroundings (Runtime)";
    private const string ContentName = "City GLB Content";
    private const string FootprintResource = "Race/city_dystopia_buildings";
    // Facade sheen and window panes sit slightly proud of the mass they
    // belong to; pad footprints (mesh-space metres, before copy scale).
    private const float FootprintPadding = 0.9f;
    // Plan-view reach for geometry outside every padded box (any height).
    private const float LooseOwnerPadding = 2.5f;
    // Street and pavement at the building foot never count as loose trim.
    private const float GroundSkip = 0.3f;
    // World metres beyond the road edge that a building must keep clear.
    public const float BuildingClearance = 1.5f;
    // Safety net: any leftover triangle this close to the road edge goes too.
    private const float TriangleClearance = 0.75f;
    private const int TrianglesPerSlice = 60000;

    [Serializable]
    private sealed class FootprintFile
    {
        public string space;
        public FootprintEntry[] buildings;
    }

    [Serializable]
    private sealed class FootprintEntry
    {
        public string name;
        public float[] min;
        public float[] max;
        public int polygons;
    }

    private struct Footprint
    {
        public Vector3 Min;
        public Vector3 Max;
    }

    private sealed class MeshTriangles
    {
        public int[][] SubMeshTriangles;
        // Per sub-mesh, per triangle: owning footprint or -1.
        public int[][] Owners;
        // Per sub-mesh, per triangle: padded-box owner before island voting.
        public int[][] DirectOwners;
        // Per sub-mesh, per triangle: centroid in content-local space.
        public Vector3[][] Centroids;
    }

    private sealed class CarvedRenderer
    {
        public MeshFilter Filter;
        public Mesh Original;
        public Mesh Carved;
    }

    private static Footprint[] footprints;
    private static readonly Dictionary<Mesh, MeshTriangles> meshCache = new Dictionary<Mesh, MeshTriangles>();
    private static readonly Dictionary<MeshFilter, CarvedRenderer> carvedCache = new Dictionary<MeshFilter, CarvedRenderer>();
    private static readonly List<CarvedRenderer> applied = new List<CarvedRenderer>();
    private static readonly List<Vector3> removedBuildingCenters = new List<Vector3>();
    private static readonly List<Vector3> removedBuildingSizes = new List<Vector3>();
    private static readonly Dictionary<Transform, bool[]> blockedByContent = new Dictionary<Transform, bool[]>();

    public static bool IsCarved => applied.Count > 0;
    public static bool IsBusy { get; private set; }
    public static int FootprintCount => footprints != null ? footprints.Length : 0;
    public static int RemovedBuildingCount { get; private set; }
    public static int RemovedTriangleCount { get; private set; }
    public static int RemovedLooseTriangleCount { get; private set; }
    public static int CityCopyCount { get; private set; }
    public static IReadOnlyList<Vector3> RemovedBuildingCenters => removedBuildingCenters;

    /// <summary>All city renderers of the runtime ring (empty until the GLB copies load).</summary>
    public static List<MeshFilter> FindCityFilters(out List<Transform> contents)
    {
        List<MeshFilter> filters = new List<MeshFilter>();
        contents = new List<Transform>();
        GameObject root = GameObject.Find(CityRootName);
        if (root == null)
        {
            return filters;
        }
        foreach (Transform copy in root.transform)
        {
            Transform content = copy.Find(ContentName);
            if (content == null)
            {
                continue;
            }
            contents.Add(content);
            filters.AddRange(content.GetComponentsInChildren<MeshFilter>(true));
        }
        return filters;
    }

    public static bool CityReady()
    {
        return GymCityDystopiaSurroundings.IsBuilt &&
            GymCityDystopiaSurroundings.ExpectedPlacements > 0 &&
            GymCityDystopiaSurroundings.ReadyPlacements >= GymCityDystopiaSurroundings.ExpectedPlacements;
    }

    /// <summary>Carves synchronously (verifiers, or when the race must start now).</summary>
    public static void CarveNow()
    {
        IEnumerator routine = CarveRoutine(int.MaxValue);
        while (routine.MoveNext())
        {
        }
    }

    /// <summary>Carves over several frames, yielding after roughly <paramref name="trianglesPerFrame"/> triangles.</summary>
    public static IEnumerator CarveRoutine(int trianglesPerFrame = TrianglesPerSlice)
    {
        if (IsCarved || IsBusy)
        {
            yield break;
        }
        if (!LoadFootprints())
        {
            Debug.LogError("GYMCHAOS_RACE_CARVE_FAIL reason=footprints-missing resource=" + FootprintResource);
            yield break;
        }

        IsBusy = true;
        try
        {
        removedBuildingCenters.Clear();
        removedBuildingSizes.Clear();
        blockedByContent.Clear();
        RemovedBuildingCount = 0;
        RemovedTriangleCount = 0;
        RemovedLooseTriangleCount = 0;
        GymRaceCorridorMask buildingMask = GymRaceCorridorMask.Build(BuildingClearance);
        GymRaceCorridorMask triangleMask = GymRaceCorridorMask.Build(TriangleClearance, 0.5f);

        List<MeshFilter> filters = FindCityFilters(out List<Transform> contents);
        CityCopyCount = contents.Count;
        int budget = 0;
        foreach (Transform content in contents)
        {
            bool[] blocked = FindBlockedBuildings(content, buildingMask);
            blockedByContent[content] = blocked;
            MeshFilter[] copyFilters = content.GetComponentsInChildren<MeshFilter>(true);
            for (int i = 0; i < copyFilters.Length; i++)
            {
                MeshFilter filter = copyFilters[i];
                if (filter == null || filter.sharedMesh == null)
                {
                    continue;
                }
                if (carvedCache.TryGetValue(filter, out CarvedRenderer cached))
                {
                    Apply(cached);
                    continue;
                }

                Mesh original = filter.sharedMesh;
                MeshTriangles data = GetTriangles(original, filter.transform, content);
                budget += CountTriangles(data);
                Mesh carved = BuildCarved(original, data, blocked, content, triangleMask);
                if (carved != null)
                {
#if !UNITY_EDITOR
                    // Keep only the GPU copy: readable carved copies of every
                    // city renderer grew the WebGL heap by ~145 MB at race start.
                    // Editor verifiers read the carved triangles, so they keep it.
                    carved.UploadMeshData(true);
#endif
                    CarvedRenderer entry = new CarvedRenderer
                    {
                        Filter = filter,
                        Original = original,
                        Carved = carved
                    };
                    carvedCache[filter] = entry;
                    Apply(entry);
                }
                if (budget >= trianglesPerFrame)
                {
                    budget = 0;
                    yield return null;
                }
            }
        }
        }
        finally
        {
            // An aborted carve must not block every later race.
            IsBusy = false;
        }
        Debug.Log(
            $"GYMCHAOS_RACE_CARVED copies={CityCopyCount} renderers={applied.Count} " +
            $"buildings={RemovedBuildingCount} triangles={RemovedTriangleCount} " +
            $"loose={RemovedLooseTriangleCount} footprints={FootprintCount}");
    }

    public static void Restore()
    {
        for (int i = 0; i < applied.Count; i++)
        {
            CarvedRenderer entry = applied[i];
            if (entry.Filter != null)
            {
                entry.Filter.sharedMesh = entry.Original;
            }
        }
        applied.Clear();
        // Free the carved copies and the per-triangle tables: they are about
        // as large as the city itself and only needed during a race.
        foreach (CarvedRenderer entry in carvedCache.Values)
        {
            if (entry.Carved != null)
            {
                UnityEngine.Object.Destroy(entry.Carved);
            }
        }
        carvedCache.Clear();
        meshCache.Clear();
        blockedByContent.Clear();
        IsBusy = false;
        Debug.Log("GYMCHAOS_RACE_CITY_RESTORED");
    }

    private static void Apply(CarvedRenderer entry)
    {
        if (entry.Filter == null)
        {
            return;
        }
        entry.Filter.sharedMesh = entry.Carved;
        applied.Add(entry);
    }

    private static bool LoadFootprints()
    {
        if (footprints != null)
        {
            return footprints.Length > 0;
        }
        TextAsset text = Resources.Load<TextAsset>(FootprintResource);
        if (text == null)
        {
            footprints = new Footprint[0];
            return false;
        }
        FootprintFile file = JsonUtility.FromJson<FootprintFile>(text.text);
        List<Footprint> list = new List<Footprint>();
        if (file != null && file.buildings != null)
        {
            foreach (FootprintEntry entry in file.buildings)
            {
                if (entry == null || entry.min == null || entry.max == null ||
                    entry.min.Length < 3 || entry.max.Length < 3)
                {
                    continue;
                }
                list.Add(new Footprint
                {
                    Min = new Vector3(entry.min[0], entry.min[1], entry.min[2]),
                    Max = new Vector3(entry.max[0], entry.max[1], entry.max[2])
                });
            }
        }
        footprints = list.ToArray();
        return footprints.Length > 0;
    }

    private static bool[] FindBlockedBuildings(Transform content, GymRaceCorridorMask mask)
    {
        bool[] blocked = new bool[footprints.Length];
        for (int i = 0; i < footprints.Length; i++)
        {
            Footprint footprint = footprints[i];
            // Copies only rotate in 90-degree steps, so the transformed
            // rectangle stays axis aligned in world space.
            Vector3 a = content.TransformPoint(new Vector3(footprint.Min.x, footprint.Min.y, footprint.Min.z));
            Vector3 b = content.TransformPoint(new Vector3(footprint.Max.x, footprint.Min.y, footprint.Max.z));
            float minX = Mathf.Min(a.x, b.x);
            float maxX = Mathf.Max(a.x, b.x);
            float minZ = Mathf.Min(a.z, b.z);
            float maxZ = Mathf.Max(a.z, b.z);
            if (!mask.Overlaps(minX, minZ, maxX, maxZ))
            {
                continue;
            }
            blocked[i] = true;
            RemovedBuildingCount++;
            Vector3 top = content.TransformPoint(new Vector3(footprint.Max.x, footprint.Max.y, footprint.Max.z));
            removedBuildingCenters.Add(new Vector3((minX + maxX) * 0.5f, Mathf.Min(a.y, top.y), (minZ + maxZ) * 0.5f));
            removedBuildingSizes.Add(new Vector3(maxX - minX, Mathf.Abs(top.y - a.y), maxZ - minZ));
        }
        return blocked;
    }

    private static int CountTriangles(MeshTriangles data)
    {
        int count = 0;
        for (int i = 0; i < data.SubMeshTriangles.Length; i++)
        {
            count += data.SubMeshTriangles[i].Length / 3;
        }
        return count;
    }

    private static MeshTriangles GetTriangles(Mesh mesh, Transform part, Transform content)
    {
        if (meshCache.TryGetValue(mesh, out MeshTriangles cached))
        {
            return cached;
        }

        // Every copy shares the mesh and the part's local transform, so the
        // mesh-to-content matrix is the same for all of them.
        Matrix4x4 toContent = content.worldToLocalMatrix * part.localToWorldMatrix;
        Vector3[] vertices = mesh.vertices;
        int subMeshCount = mesh.subMeshCount;
        MeshTriangles data = new MeshTriangles
        {
            SubMeshTriangles = new int[subMeshCount][],
            Owners = new int[subMeshCount][],
            DirectOwners = new int[subMeshCount][],
            Centroids = new Vector3[subMeshCount][]
        };
        FootprintGrid grid = new FootprintGrid(footprints, FootprintPadding, false);
        // Trim, antennas and window panes that stick out past the padded box
        // still belong to the nearest building in plan view.
        FootprintGrid looseGrid = new FootprintGrid(footprints, LooseOwnerPadding, true);
        for (int sub = 0; sub < subMeshCount; sub++)
        {
            int[] triangles = mesh.GetTriangles(sub);
            int triangleCount = triangles.Length / 3;
            int[] owners = new int[triangleCount];
            Vector3[] centroids = new Vector3[triangleCount];
            for (int t = 0; t < triangleCount; t++)
            {
                Vector3 centroid = (vertices[triangles[t * 3]] +
                    vertices[triangles[t * 3 + 1]] +
                    vertices[triangles[t * 3 + 2]]) / 3f;
                centroid = toContent.MultiplyPoint3x4(centroid);
                centroids[t] = centroid;
                int owner = grid.Find(centroid);
                owners[t] = owner >= 0 ? owner : looseGrid.Find(centroid);
            }
            data.SubMeshTriangles[sub] = triangles;
            data.Owners[sub] = owners;
            int[] direct = new int[triangleCount];
            for (int t = 0; t < triangleCount; t++)
            {
                direct[t] = grid.Find(centroids[t]);
            }
            data.DirectOwners[sub] = direct;
            data.Centroids[sub] = centroids;
        }
        AssignIslandOwners(vertices, data);
        meshCache[mesh] = data;
        return data;
    }

    /// <summary>
    /// A connected piece of geometry (welded by position) goes with the
    /// building most of its triangles belong to, so a removed tower never
    /// leaves a facade strip, ledge or window row standing. Pieces wider than
    /// their building (shared street plates) keep per-triangle owners.
    /// </summary>
    private static void AssignIslandOwners(Vector3[] vertices, MeshTriangles data)
    {
        int[] weld = new int[vertices.Length];
        Dictionary<Vector3Int, int> welded = new Dictionary<Vector3Int, int>(vertices.Length);
        for (int v = 0; v < vertices.Length; v++)
        {
            Vector3 p = vertices[v];
            Vector3Int key = new Vector3Int(
                Mathf.RoundToInt(p.x * 100f), Mathf.RoundToInt(p.y * 100f), Mathf.RoundToInt(p.z * 100f));
            if (!welded.TryGetValue(key, out int id))
            {
                id = welded.Count;
                welded[key] = id;
            }
            weld[v] = id;
        }
        int[] parent = new int[welded.Count];
        for (int i = 0; i < parent.Length; i++)
        {
            parent[i] = i;
        }
        int subMeshCount = data.SubMeshTriangles.Length;
        for (int sub = 0; sub < subMeshCount; sub++)
        {
            int[] triangles = data.SubMeshTriangles[sub];
            for (int t = 0; t < triangles.Length; t += 3)
            {
                Union(parent, weld[triangles[t]], weld[triangles[t + 1]]);
                Union(parent, weld[triangles[t]], weld[triangles[t + 2]]);
            }
        }

        // Votes per (island, owner) and the island's plan-view extent.
        Dictionary<long, int> votes = new Dictionary<long, int>();
        Dictionary<int, Vector4> extents = new Dictionary<int, Vector4>();
        int stride = footprints.Length + 1;
        for (int sub = 0; sub < subMeshCount; sub++)
        {
            int[] triangles = data.SubMeshTriangles[sub];
            int[] owners = data.Owners[sub];
            Vector3[] centroids = data.Centroids[sub];
            for (int t = 0; t < owners.Length; t++)
            {
                int island = Find(parent, weld[triangles[t * 3]]);
                long key = (long)island * stride + owners[t] + 1;
                votes.TryGetValue(key, out int count);
                votes[key] = count + 1;
                Vector3 c = centroids[t];
                extents[island] = extents.TryGetValue(island, out Vector4 e)
                    ? new Vector4(Mathf.Min(e.x, c.x), Mathf.Min(e.y, c.z), Mathf.Max(e.z, c.x), Mathf.Max(e.w, c.z))
                    : new Vector4(c.x, c.z, c.x, c.z);
            }
        }
        Dictionary<int, int> islandOwner = new Dictionary<int, int>();
        Dictionary<int, int> islandBest = new Dictionary<int, int>();
        foreach (KeyValuePair<long, int> vote in votes)
        {
            int island = (int)(vote.Key / stride);
            int owner = (int)(vote.Key % stride) - 1;
            if (owner < 0)
            {
                continue;
            }
            if (!islandBest.TryGetValue(island, out int best) || vote.Value > best)
            {
                islandBest[island] = vote.Value;
                islandOwner[island] = owner;
            }
        }
        for (int sub = 0; sub < subMeshCount; sub++)
        {
            int[] triangles = data.SubMeshTriangles[sub];
            int[] owners = data.Owners[sub];
            for (int t = 0; t < owners.Length; t++)
            {
                int island = Find(parent, weld[triangles[t * 3]]);
                if (!islandOwner.TryGetValue(island, out int owner))
                {
                    continue;
                }
                Footprint f = footprints[owner];
                Vector4 e = extents[island];
                if (e.x >= f.Min.x - LooseOwnerPadding && e.y >= f.Min.z - LooseOwnerPadding &&
                    e.z <= f.Max.x + LooseOwnerPadding && e.w <= f.Max.z + LooseOwnerPadding)
                {
                    owners[t] = owner;
                }
            }
        }
    }

    private static int Find(int[] parent, int i)
    {
        while (parent[i] != i)
        {
            parent[i] = parent[parent[i]];
            i = parent[i];
        }
        return i;
    }

    private static void Union(int[] parent, int a, int b)
    {
        a = Find(parent, a);
        b = Find(parent, b);
        if (a != b)
        {
            parent[a] = b;
        }
    }

    private static Mesh BuildCarved(
        Mesh original,
        MeshTriangles data,
        bool[] blocked,
        Transform content,
        GymRaceCorridorMask triangleMask)
    {
        Matrix4x4 toWorld = content.localToWorldMatrix;
        int subMeshCount = data.SubMeshTriangles.Length;
        List<int> removedIds = new List<int>();
        for (int i = 0; i < blocked.Length; i++)
        {
            if (blocked[i]) removedIds.Add(i);
        }
        List<int>[] kept = new List<int>[subMeshCount];
        int removed = 0;
        for (int sub = 0; sub < subMeshCount; sub++)
        {
            int[] triangles = data.SubMeshTriangles[sub];
            int[] owners = data.Owners[sub];
            Vector3[] centroids = data.Centroids[sub];
            List<int> list = new List<int>(triangles.Length);
            for (int t = 0; t < owners.Length; t++)
            {
                int owner = owners[t];
                bool drop = owner >= 0 && blocked[owner];
                if (!drop)
                {
                    // Inside a removed tower's box and claimed by no kept
                    // neighbour: a leftover sliver of that tower.
                    int direct = data.DirectOwners[sub][t];
                    if ((direct < 0 || blocked[direct]) && removedIds.Count > 0 && InsideBlocked(centroids[t], removedIds))
                    {
                        drop = true;
                    }
                }
                if (!drop)
                {
                    Vector3 world = toWorld.MultiplyPoint3x4(centroids[t]);
                    if (triangleMask.Contains(world.x, world.z))
                    {
                        drop = true;
                        RemovedLooseTriangleCount++;
                    }
                }
                if (drop)
                {
                    removed++;
                    continue;
                }
                list.Add(triangles[t * 3]);
                list.Add(triangles[t * 3 + 1]);
                list.Add(triangles[t * 3 + 2]);
            }
            kept[sub] = list;
        }
        if (removed == 0)
        {
            return null;
        }

        RemovedTriangleCount += removed;
        Mesh carved = new Mesh
        {
            name = original.name + " (race carved)",
            indexFormat = original.indexFormat
        };
        carved.vertices = original.vertices;
        carved.normals = original.normals;
        carved.uv = original.uv;
        if (original.tangents != null && original.tangents.Length == original.vertexCount)
        {
            carved.tangents = original.tangents;
        }
        if (original.colors != null && original.colors.Length == original.vertexCount)
        {
            carved.colors = original.colors;
        }
        carved.subMeshCount = subMeshCount;
        for (int sub = 0; sub < subMeshCount; sub++)
        {
            carved.SetTriangles(kept[sub], sub, false);
        }
        carved.bounds = original.bounds;
        return carved;
    }

    private const float RemnantShrink = 0.3f;

    private static bool InsideBlocked(Vector3 c, List<int> removedIds)
    {
        for (int r = 0; r < removedIds.Count; r++)
        {
            Footprint f = footprints[removedIds[r]];
            if (c.x >= f.Min.x + RemnantShrink && c.x <= f.Max.x - RemnantShrink &&
                c.z >= f.Min.z + RemnantShrink && c.z <= f.Max.z - RemnantShrink &&
                c.y >= f.Min.y + GroundSkip && c.y <= f.Max.y)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>Remaining city triangles whose centroid lies on the road band (verification).</summary>
    public static int CountTrianglesOnRoad(float margin)
    {
        GymRaceCorridorMask mask = GymRaceCorridorMask.Build(margin, 0.5f);
        List<MeshFilter> filters = FindCityFilters(out _);
        int count = 0;
        foreach (MeshFilter filter in filters)
        {
            Mesh mesh = filter.sharedMesh;
            if (mesh == null)
            {
                continue;
            }
            Vector3[] vertices = mesh.vertices;
            Matrix4x4 toWorld = filter.transform.localToWorldMatrix;
            for (int sub = 0; sub < mesh.subMeshCount; sub++)
            {
                int[] triangles = mesh.GetTriangles(sub);
                for (int t = 0; t < triangles.Length; t += 3)
                {
                    Vector3 world = toWorld.MultiplyPoint3x4(
                        (vertices[triangles[t]] + vertices[triangles[t + 1]] + vertices[triangles[t + 2]]) / 3f);
                    if (mask.Contains(world.x, world.z))
                    {
                        count++;
                    }
                }
            }
        }
        return count;
    }

    /// <summary>
    /// Visible triangles still standing inside a removed building's box and
    /// not claimed by a kept neighbour (verification: should be zero).
    /// </summary>
    public static int CountRemnantTriangles()
    {
        if (footprints == null || footprints.Length == 0)
        {
            return 0;
        }
        FootprintGrid grid = new FootprintGrid(footprints, FootprintPadding, false);
        const float shrink = RemnantShrink;
        int count = 0;
        FindCityFilters(out List<Transform> contents);
        foreach (Transform content in contents)
        {
            if (!blockedByContent.TryGetValue(content, out bool[] blocked))
            {
                continue;
            }
            List<int> removed = new List<int>();
            for (int i = 0; i < blocked.Length; i++)
            {
                if (blocked[i]) removed.Add(i);
            }
            if (removed.Count == 0)
            {
                continue;
            }
            foreach (MeshFilter filter in content.GetComponentsInChildren<MeshFilter>(true))
            {
                Mesh mesh = filter.sharedMesh;
                Renderer renderer = filter.GetComponent<Renderer>();
                if (mesh == null || renderer == null || !renderer.enabled)
                {
                    continue;
                }
                Vector3[] vertices = mesh.vertices;
                Matrix4x4 toContent = content.worldToLocalMatrix * filter.transform.localToWorldMatrix;
                for (int sub = 0; sub < mesh.subMeshCount; sub++)
                {
                    int[] triangles = mesh.GetTriangles(sub);
                    for (int t = 0; t < triangles.Length; t += 3)
                    {
                        Vector3 c = toContent.MultiplyPoint3x4(
                            (vertices[triangles[t]] + vertices[triangles[t + 1]] + vertices[triangles[t + 2]]) / 3f);
                        for (int r = 0; r < removed.Count; r++)
                        {
                            Footprint f = footprints[removed[r]];
                            if (c.x < f.Min.x + shrink || c.x > f.Max.x - shrink ||
                                c.z < f.Min.z + shrink || c.z > f.Max.z - shrink ||
                                c.y < f.Min.y + GroundSkip || c.y > f.Max.y)
                            {
                                continue;
                            }
                            int owner = grid.Find(c);
                            if (owner < 0 || blocked[owner])
                            {
                                count++;
                            }
                            break;
                        }
                    }
                }
            }
        }
        return count;
    }

    /// <summary>Total city triangle count across all copies (verification).</summary>
    public static long CountCityTriangles()
    {
        long count = 0;
        foreach (MeshFilter filter in FindCityFilters(out _))
        {
            Mesh mesh = filter.sharedMesh;
            if (mesh == null)
            {
                continue;
            }
            for (int sub = 0; sub < mesh.subMeshCount; sub++)
            {
                count += mesh.GetIndexCount(sub) / 3;
            }
        }
        return count;
    }

    private sealed class FootprintGrid
    {
        private const float Cell = 4f;
        private readonly Footprint[] padded;
        private readonly Dictionary<long, List<int>> cells = new Dictionary<long, List<int>>();

        private readonly bool ignoreHeight;
        private readonly float padding;

        public FootprintGrid(Footprint[] source, float padding, bool ignoreHeight)
        {
            this.ignoreHeight = ignoreHeight;
            this.padding = padding;
            padded = new Footprint[source.Length];
            for (int i = 0; i < source.Length; i++)
            {
                Footprint f = source[i];
                f.Min -= new Vector3(padding, padding, padding);
                f.Max += new Vector3(padding, padding, padding);
                padded[i] = f;
                int x0 = Mathf.FloorToInt(f.Min.x / Cell);
                int x1 = Mathf.FloorToInt(f.Max.x / Cell);
                int z0 = Mathf.FloorToInt(f.Min.z / Cell);
                int z1 = Mathf.FloorToInt(f.Max.z / Cell);
                for (int z = z0; z <= z1; z++)
                {
                    for (int x = x0; x <= x1; x++)
                    {
                        long key = Key(x, z);
                        if (!cells.TryGetValue(key, out List<int> list))
                        {
                            list = new List<int>(2);
                            cells[key] = list;
                        }
                        list.Add(i);
                    }
                }
            }
        }

        private static long Key(int x, int z)
        {
            return ((long)x << 32) ^ (uint)z;
        }

        public int Find(Vector3 point)
        {
            if (!cells.TryGetValue(Key(Mathf.FloorToInt(point.x / Cell), Mathf.FloorToInt(point.z / Cell)), out List<int> list))
            {
                return -1;
            }
            // Smallest containing footprint wins so a tall tower's shell does
            // not claim the low neighbour tucked against it.
            int best = -1;
            float bestArea = float.MaxValue;
            for (int i = 0; i < list.Count; i++)
            {
                Footprint f = padded[list[i]];
                if (point.x < f.Min.x || point.x > f.Max.x ||
                    point.z < f.Min.z || point.z > f.Max.z ||
                    (ignoreHeight ? point.y < f.Min.y + padding + GroundSkip : point.y < f.Min.y || point.y > f.Max.y))
                {
                    continue;
                }
                float area = (f.Max.x - f.Min.x) * (f.Max.z - f.Min.z);
                if (area < bestArea)
                {
                    bestArea = area;
                    best = list[i];
                }
            }
            return best;
        }
    }
}
