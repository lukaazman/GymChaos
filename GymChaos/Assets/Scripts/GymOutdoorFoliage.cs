using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Lays out the unbuilt land around the gym, the protein store and the
/// parking as a designed city park ("central park" look) instead of a random
/// scatter. Everything here is outside the fences and purely visual.
///
/// Layout comes from the land itself. A 0.5 m mask marks built cells (any
/// visible collider surface, wall or blocker) and a distance field gives
/// every free cell its distance to the nearest built edge or to the city
/// ring. From that field:
/// - a promenade loop follows the 6 m contour of every wide lawn, and the
///   ridge (medial axis) of the field gives the cross paths and the single
///   path down narrow strips, so paths always curve with the land;
/// - avenue trees line both sides of the paths, groves fill deep interiors,
///   specimen trees stand on open lawns, and a low hedge runs along every
///   fence so the planting never stops abruptly at a wall;
/// - benches and lamp posts sit beside the paths;
/// - lawns get real grass: thin double-sided blades in per-chunk meshes;
/// - one baked ground texture paints lawn (mowing stripes), gravel paths
///   with edging, mulch under hedges and tree shade, and fades the park edge
///   into the city paving colour, which the city ground ring also uses, so
///   the park and the tower bases meet without a seam or a step.
/// Plants and props have no colliders and cast no shadows.
/// </summary>
public static class GymOutdoorFoliage
{
    public const string RootName = "Outdoor Foliage (Runtime)";
    public const string GroundName = "Outdoor Planting Ground";
    public const string DetailsRootName = "Outdoor Park Details (Runtime)";
    private const string AssetFolder = "BodyBuilders/outside/foliage/";
    private const int Seed = 4127;
    private const float Cell = 0.5f;
    // Park ground top, just below every built surface (floorY) and the
    // courtyard foundation (floorY - 0.02).
    public const float GroundTopOffset = -0.05f;
    // The city ground ring starts this far outside the planting bounds.
    private const float CityRingGap = 0.45f;
    private const float PromenadeLevel = 6f;
    private const float PathHalfWidth = 0.95f;
    private const float PixelsPerMetre = 10f;
    private const int MaxTextureSize = 1024;
    private const float GrassChunkSize = 8f;
    private const float GrassTuftsPerSquareMetre = 5f;

    // The paving colour of the city ring around the towers. The park ground
    // fades into it at its outer edge.
    public static readonly Color CityPavingColor = new Color(0.075f, 0.085f, 0.09f);

    private sealed class Species
    {
        public string File;
        public bool Tree;
        public float MinScale;
        public float MaxScale;
        // Radius around the stem that must stay clear of walls/blockers, in
        // world metres at scale 1 of the placed object.
        public float Clearance;

        public Species(string file, bool tree, float minScale, float maxScale, float clearance)
        {
            File = file;
            Tree = tree;
            MinScale = minScale;
            MaxScale = maxScale;
            Clearance = clearance;
        }
    }

    // Source meshes are about 1 m (custom ones about 1.5-2.2 m).
    private static readonly Species[] Bushes =
    {
        new Species("bush1_lod.glb", false, 1.1f, 1.9f, 0.8f),
        new Species("bush2_lod.glb", false, 1.2f, 2.0f, 0.8f),
        new Species("bush3_custom.glb", false, 0.7f, 1.2f, 0.9f),
    };

    private static readonly Species[] Trees =
    {
        new Species("tree1_lod.glb", true, 5.2f, 7.2f, 2.2f),
        new Species("tree2_lod.glb", true, 6.4f, 8.8f, 1.2f),
        new Species("tree3_custom.glb", true, 2.5f, 3.4f, 1.8f),
    };

    public static bool IsBuilt { get; private set; }
    public static int RequestedPlants { get; private set; }
    public static int LoadedPlants { get; private set; }
    public static int FailedPlants { get; private set; }
    public static int TreeCount { get; private set; }
    public static int BushCount { get; private set; }
    public static int PathCellCount { get; private set; }
    public static int GrassBladeCount { get; private set; }
    public static int BenchCount { get; private set; }
    public static int LampCount { get; private set; }
    public static Bounds PlantingBounds { get; private set; }
    public static Material GroundMaterial { get; private set; }
    // Baked colour at the park's outer edge (the texture is not readable).
    public static Color EdgeColorForVerification { get; private set; }
    public static readonly List<Vector3> PlantPositions = new List<Vector3>();
    private static readonly Collider[] ClearanceHits = new Collider[4];
    private static readonly RaycastHit[] RayHits = new RaycastHit[16];

    // Grid state for one build.
    private static int nx;
    private static int nz;
    private static float originX;
    private static float originZ;
    private static bool[] built;
    private static float[] distance;
    private static float[] edgeDistance;
    private static float[] pathDistance;
    private static float[] componentMax;

    public static void Build(
        Transform parent,
        float floorY,
        Bounds plantingBounds,
        Material grassMaterial)
    {
        if (parent == null || parent.Find(RootName) != null ||
            plantingBounds.size.x < 1f || plantingBounds.size.z < 1f)
        {
            return;
        }

        IsBuilt = true;
        RequestedPlants = 0;
        LoadedPlants = 0;
        FailedPlants = 0;
        TreeCount = 0;
        BushCount = 0;
        BenchCount = 0;
        LampCount = 0;
        GrassBladeCount = 0;
        PlantPositions.Clear();
        PlantingBounds = plantingBounds;

        GameObject root = new GameObject(RootName);
        root.transform.SetParent(parent, false);
        GameObject details = new GameObject(DetailsRootName);
        details.transform.SetParent(parent, false);

        BuildFields(plantingBounds, floorY);
        System.Random random = new System.Random(Seed);
        List<Vector4> treeShade = PlacePlanting(root.transform, floorY, random);
        PlaceBenchesAndLamps(details.transform, floorY);
        CreateGround(parent, root.transform, floorY, plantingBounds, grassMaterial, treeShade);
        CreateGrass(details.transform, floorY, random);

        Debug.Log(
            $"GYMCHAOS_OUTDOOR_FOLIAGE_REQUESTED plants={RequestedPlants} " +
            $"trees={TreeCount} bushes={BushCount} bounds={plantingBounds} " +
            $"pathCells={PathCellCount} grassBlades={GrassBladeCount} " +
            $"benches={BenchCount} lamps={LampCount}",
            root);
        if (RequestedPlants == 0)
        {
            Debug.Log("GYMCHAOS_OUTDOOR_FOLIAGE_OK plants=0");
        }

        built = null;
        distance = null;
        edgeDistance = null;
        pathDistance = null;
        componentMax = null;
    }

    // ---- fields ----

    private static void BuildFields(Bounds bounds, float floorY)
    {
        originX = bounds.min.x;
        originZ = bounds.min.z;
        nx = Mathf.Max(2, Mathf.CeilToInt(bounds.size.x / Cell));
        nz = Mathf.Max(2, Mathf.CeilToInt(bounds.size.z / Cell));
        int count = nx * nz;
        built = new bool[count];
        distance = new float[count];
        edgeDistance = new float[count];
        pathDistance = new float[count];
        componentMax = new float[count];

        for (int z = 0; z < nz; z++)
        {
            for (int x = 0; x < nx; x++)
            {
                int index = z * nx + x;
                Vector3 spot = CellCenter(x, z, floorY);
                float edge = Mathf.Min(
                    Mathf.Min(spot.x - bounds.min.x, bounds.max.x - spot.x),
                    Mathf.Min(spot.z - bounds.min.z, bounds.max.z - spot.z));
                edgeDistance[index] = Mathf.Max(0f, edge);
                built[index] = !IsUnbuilt(spot, floorY) || IsBlocked(spot, floorY);
                distance[index] = built[index] ? 0f : edgeDistance[index];
            }
        }
        Chamfer(distance);

        // Per free component: its deepest point decides which paths fit.
        int[] label = new int[count];
        List<float> maxima = new List<float> { 0f };
        Queue<int> queue = new Queue<int>();
        for (int i = 0; i < count; i++)
        {
            if (built[i] || label[i] != 0)
            {
                continue;
            }
            int id = maxima.Count;
            float deepest = 0f;
            label[i] = id;
            queue.Enqueue(i);
            while (queue.Count > 0)
            {
                int current = queue.Dequeue();
                deepest = Mathf.Max(deepest, distance[current]);
                int cx = current % nx;
                int cz = current / nx;
                TryVisit(cx - 1, cz, id, label, queue);
                TryVisit(cx + 1, cz, id, label, queue);
                TryVisit(cx, cz - 1, id, label, queue);
                TryVisit(cx, cz + 1, id, label, queue);
            }
            maxima.Add(deepest);
        }
        for (int i = 0; i < count; i++)
        {
            componentMax[i] = label[i] > 0 ? maxima[label[i]] : 0f;
        }

        // Path centre lines: the promenade contour on wide lawns plus the
        // ridge of the distance field (cross paths, strip centres). The
        // ridge is found on a smoothed copy so chamfer steps do not sprout
        // short spurs.
        float[] smooth = BoxBlur(BoxBlur(distance));
        const float far = 1e6f;
        PathCellCount = 0;
        for (int z = 0; z < nz; z++)
        {
            for (int x = 0; x < nx; x++)
            {
                int index = z * nx + x;
                pathDistance[index] = far;
                if (built[index])
                {
                    continue;
                }
                float d = distance[index];
                bool promenade = componentMax[index] >= PromenadeLevel + 2f &&
                    Mathf.Abs(d - PromenadeLevel) <= Cell * 0.5f;
                bool ridge = d >= 3f && IsRidge(smooth, x, z);
                if (promenade || ridge)
                {
                    pathDistance[index] = 0f;
                    PathCellCount++;
                }
            }
        }
        Chamfer(pathDistance);
    }

    private static void TryVisit(int x, int z, int id, int[] label, Queue<int> queue)
    {
        if (x < 0 || z < 0 || x >= nx || z >= nz)
        {
            return;
        }
        int index = z * nx + x;
        if (!built[index] && label[index] == 0)
        {
            label[index] = id;
            queue.Enqueue(index);
        }
    }

    // Local maximum across x or z (strict on one side, so flat bands that
    // only depend on the other axis are not ridges).
    private static bool IsRidge(float[] field, int x, int z)
    {
        const float strict = 0.015f;
        float d = field[z * nx + x];
        if (x > 0 && x < nx - 1)
        {
            float left = field[z * nx + x - 1];
            float right = field[z * nx + x + 1];
            if ((d > left + strict && d >= right) || (d >= left && d > right + strict))
            {
                return true;
            }
        }
        if (z > 0 && z < nz - 1)
        {
            float down = field[(z - 1) * nx + x];
            float up = field[(z + 1) * nx + x];
            if ((d > down + strict && d >= up) || (d >= down && d > up + strict))
            {
                return true;
            }
        }
        return false;
    }

    // 3 x 3 box blur over free cells; built cells stay 0.
    private static float[] BoxBlur(float[] field)
    {
        float[] result = new float[field.Length];
        for (int z = 0; z < nz; z++)
        {
            for (int x = 0; x < nx; x++)
            {
                int index = z * nx + x;
                if (built[index])
                {
                    continue;
                }
                float sum = 0f;
                int count = 0;
                for (int dz = -1; dz <= 1; dz++)
                {
                    int sz = z + dz;
                    if (sz < 0 || sz >= nz) continue;
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int sx = x + dx;
                        if (sx < 0 || sx >= nx) continue;
                        sum += field[sz * nx + sx];
                        count++;
                    }
                }
                result[index] = sum / count;
            }
        }
        return result;
    }

    // Two-pass 3-4 chamfer distance in metres (values seeded in place).
    private static void Chamfer(float[] field)
    {
        float straight = Cell;
        float diagonal = Cell * 1.41421356f;
        for (int z = 0; z < nz; z++)
        {
            for (int x = 0; x < nx; x++)
            {
                int i = z * nx + x;
                float v = field[i];
                if (x > 0) v = Mathf.Min(v, field[i - 1] + straight);
                if (z > 0)
                {
                    v = Mathf.Min(v, field[i - nx] + straight);
                    if (x > 0) v = Mathf.Min(v, field[i - nx - 1] + diagonal);
                    if (x < nx - 1) v = Mathf.Min(v, field[i - nx + 1] + diagonal);
                }
                field[i] = v;
            }
        }
        for (int z = nz - 1; z >= 0; z--)
        {
            for (int x = nx - 1; x >= 0; x--)
            {
                int i = z * nx + x;
                float v = field[i];
                if (x < nx - 1) v = Mathf.Min(v, field[i + 1] + straight);
                if (z < nz - 1)
                {
                    v = Mathf.Min(v, field[i + nx] + straight);
                    if (x < nx - 1) v = Mathf.Min(v, field[i + nx + 1] + diagonal);
                    if (x > 0) v = Mathf.Min(v, field[i + nx - 1] + diagonal);
                }
                field[i] = v;
            }
        }
    }

    private static Vector3 CellCenter(int x, int z, float floorY)
    {
        return new Vector3(originX + (x + 0.5f) * Cell, floorY, originZ + (z + 0.5f) * Cell);
    }

    // Bilinear field sample at a world position.
    private static float Sample(float[] field, float worldX, float worldZ)
    {
        float fx = (worldX - originX) / Cell - 0.5f;
        float fz = (worldZ - originZ) / Cell - 0.5f;
        int x0 = Mathf.Clamp(Mathf.FloorToInt(fx), 0, nx - 1);
        int z0 = Mathf.Clamp(Mathf.FloorToInt(fz), 0, nz - 1);
        int x1 = Mathf.Min(x0 + 1, nx - 1);
        int z1 = Mathf.Min(z0 + 1, nz - 1);
        float tx = Mathf.Clamp01(fx - x0);
        float tz = Mathf.Clamp01(fz - z0);
        float a = Mathf.Lerp(field[z0 * nx + x0], field[z0 * nx + x1], tx);
        float b = Mathf.Lerp(field[z1 * nx + x0], field[z1 * nx + x1], tx);
        return Mathf.Lerp(a, b, tz);
    }

    private static bool IsBuiltAt(float worldX, float worldZ)
    {
        int x = Mathf.FloorToInt((worldX - originX) / Cell);
        int z = Mathf.FloorToInt((worldZ - originZ) / Cell);
        if (x < 0 || z < 0 || x >= nx || z >= nz)
        {
            return true;
        }
        return built[z * nx + x];
    }

    // ---- planting ----

    private struct Candidate
    {
        public Vector3 Position;
        public float Score;
    }

    private static List<Vector4> PlacePlanting(Transform root, float floorY, System.Random random)
    {
        List<Vector4> shade = new List<Vector4>();
        List<Vector3> trees = new List<Vector3>();
        List<Vector3> bushes = new List<Vector3>();
        List<Candidate> avenue = new List<Candidate>();
        List<Candidate> grove = new List<Candidate>();
        List<Candidate> specimen = new List<Candidate>();
        List<Candidate> hedge = new List<Candidate>();
        List<Candidate> beds = new List<Candidate>();

        for (int z = 0; z < nz; z++)
        {
            for (int x = 0; x < nx; x++)
            {
                int index = z * nx + x;
                if (built[index])
                {
                    continue;
                }
                Vector3 spot = CellCenter(x, z, floorY);
                float d = distance[index];
                float p = pathDistance[index];
                float noise = Mathf.PerlinNoise(spot.x * 0.06f + 31.7f, spot.z * 0.06f + 4.1f);
                float jitter = (float)random.NextDouble();
                if (p >= 2.4f && p <= 2.9f && d >= 2.2f)
                {
                    avenue.Add(new Candidate { Position = spot, Score = jitter });
                }
                else if (p >= 6f && d >= 4f && noise > 0.52f)
                {
                    grove.Add(new Candidate { Position = spot, Score = noise + jitter * 0.2f });
                }
                else if (p >= 7f && d >= 5f && noise < 0.32f)
                {
                    specimen.Add(new Candidate { Position = spot, Score = jitter });
                }
                if (d >= 0.9f && d <= 1.6f && p >= 2.2f)
                {
                    hedge.Add(new Candidate { Position = spot, Score = jitter });
                }
                else if (p >= 1.7f && p <= 2.1f && d >= 2f && noise > 0.6f)
                {
                    beds.Add(new Candidate { Position = spot, Score = noise + jitter * 0.3f });
                }
            }
        }

        // Ordered placement: avenues first (they define the park), then
        // groves, specimens, hedges and path-side beds.
        PlaceSet(root, avenue, Trees, 7.5f, trees, bushes, shade, random, floorY, 70, true);
        PlaceSet(root, grove, Trees, 4.6f, trees, bushes, shade, random, floorY, 45, true);
        PlaceSet(root, specimen, Trees, 13f, trees, bushes, shade, random, floorY, 12, true);
        PlaceSet(root, hedge, Bushes, 2.6f, trees, bushes, shade, random, floorY, 110, false);
        PlaceSet(root, beds, Bushes, 3.6f, trees, bushes, shade, random, floorY, 30, false);
        return shade;
    }

    private static void PlaceSet(
        Transform root,
        List<Candidate> candidates,
        Species[] pool,
        float spacing,
        List<Vector3> trees,
        List<Vector3> bushes,
        List<Vector4> shade,
        System.Random random,
        float floorY,
        int limit,
        bool tree)
    {
        candidates.Sort((a, b) => b.Score.CompareTo(a.Score));
        int placed = 0;
        float spacingSquared = spacing * spacing;
        // Trees keep their crown spacing to other trees; bushes keep their own
        // spacing and stay out of tree trunks.
        for (int i = 0; i < candidates.Count && placed < limit; i++)
        {
            Vector3 spot = candidates[i].Position;
            if (TooClose(spot, tree ? trees : bushes, spacingSquared) ||
                TooClose(spot, tree ? bushes : trees, tree ? 1.2f * 1.2f : 1.6f * 1.6f))
            {
                continue;
            }

            Species species = pool[random.Next(pool.Length)];
            float scale = Range(random, species.MinScale, species.MaxScale);
            float clearance = tree ? species.Clearance : species.Clearance * scale * 0.5f;
            if (!IsClear(spot, floorY, clearance, tree ? 4f : 1.4f))
            {
                continue;
            }

            Plant(root, species, spot, scale, Range(random, 0f, 360f), floorY);
            (tree ? trees : bushes).Add(spot);
            if (tree)
            {
                shade.Add(new Vector4(spot.x, spot.z, 2.6f + scale * 0.12f, 0f));
            }
            placed++;
        }
    }

    private static bool TooClose(Vector3 spot, List<Vector3> others, float minSquared)
    {
        for (int i = 0; i < others.Count; i++)
        {
            float dx = others[i].x - spot.x;
            float dz = others[i].z - spot.z;
            if (dx * dx + dz * dz < minSquared)
            {
                return true;
            }
        }
        return false;
    }

    // ---- benches and lamps ----

    private static void PlaceBenchesAndLamps(Transform root, float floorY)
    {
        List<CombineInstance> wood = new List<CombineInstance>();
        List<CombineInstance> metal = new List<CombineInstance>();
        List<CombineInstance> glow = new List<CombineInstance>();
        Mesh cube = PrimitiveMesh(PrimitiveType.Cube);
        Mesh sphere = PrimitiveMesh(PrimitiveType.Sphere);
        List<Vector3> placedProps = new List<Vector3>();
        bool bench = true;
        for (int z = 1; z < nz - 1; z++)
        {
            for (int x = 1; x < nx - 1; x++)
            {
                int index = z * nx + x;
                if (built[index])
                {
                    continue;
                }
                float p = pathDistance[index];
                if (p < 1.25f || p > 1.6f || distance[index] < 2.4f)
                {
                    continue;
                }
                Vector3 spot = CellCenter(x, z, floorY);
                if (TooClose(spot, placedProps, 12f * 12f) ||
                    TooClose(spot, PlantPositions, 1.6f * 1.6f) ||
                    !IsClear(spot, floorY, 1f, 2f))
                {
                    continue;
                }
                // Face the path: towards decreasing path distance.
                Vector3 toPath = new Vector3(
                    pathDistance[index - 1] - pathDistance[index + 1], 0f,
                    pathDistance[index - nx] - pathDistance[index + nx]);
                if (toPath.sqrMagnitude < 1e-4f)
                {
                    continue;
                }
                Quaternion facing = Quaternion.LookRotation(toPath.normalized, Vector3.up);
                Vector3 baseY = new Vector3(spot.x, floorY + GroundTopOffset, spot.z);
                if (bench)
                {
                    AddBench(wood, metal, cube, baseY, facing);
                    BenchCount++;
                }
                else
                {
                    AddLamp(metal, glow, cube, sphere, baseY);
                    LampCount++;
                }
                bench = !bench;
                placedProps.Add(spot);
            }
        }

        CreateCombined(root, "Park Benches Wood", wood,
            CreateLitMaterial("Park bench wood", new Color(0.32f, 0.2f, 0.11f), 0f, 0.3f));
        CreateCombined(root, "Park Benches And Lamps Metal", metal,
            CreateLitMaterial("Park cast iron", new Color(0.05f, 0.06f, 0.06f), 0.6f, 0.4f));
        Material glowMaterial = CreateLitMaterial(
            "Park lamp glow", new Color(1f, 0.86f, 0.6f), 0f, 0.6f);
        glowMaterial.EnableKeyword("_EMISSION");
        glowMaterial.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        if (glowMaterial.HasProperty("_EmissionColor"))
        {
            glowMaterial.SetColor("_EmissionColor", new Color(2.4f, 1.8f, 1.05f));
        }
        CreateCombined(root, "Park Lamp Globes", glow, glowMaterial);
    }

    private static void AddBench(
        List<CombineInstance> wood, List<CombineInstance> metal, Mesh cube,
        Vector3 basePosition, Quaternion facing)
    {
        // Facing = the direction a seated person looks (towards the path).
        AddBox(wood, cube, basePosition, facing, new Vector3(0f, 0.46f, 0f), new Vector3(1.7f, 0.06f, 0.46f));
        AddBox(wood, cube, basePosition, facing, new Vector3(0f, 0.78f, -0.24f), new Vector3(1.7f, 0.34f, 0.05f));
        for (int side = -1; side <= 1; side += 2)
        {
            AddBox(metal, cube, basePosition, facing, new Vector3(side * 0.74f, 0.23f, 0f), new Vector3(0.06f, 0.46f, 0.44f));
            AddBox(metal, cube, basePosition, facing, new Vector3(side * 0.74f, 0.66f, -0.24f), new Vector3(0.06f, 0.46f, 0.06f));
        }
    }

    private static void AddLamp(
        List<CombineInstance> metal, List<CombineInstance> glow, Mesh cube, Mesh sphere,
        Vector3 basePosition)
    {
        AddBox(metal, cube, basePosition, Quaternion.identity, new Vector3(0f, 0.15f, 0f), new Vector3(0.3f, 0.3f, 0.3f));
        AddBox(metal, cube, basePosition, Quaternion.identity, new Vector3(0f, 1.8f, 0f), new Vector3(0.09f, 3.3f, 0.09f));
        AddBox(metal, cube, basePosition, Quaternion.identity, new Vector3(0f, 3.48f, 0f), new Vector3(0.26f, 0.06f, 0.26f));
        glow.Add(new CombineInstance
        {
            mesh = sphere,
            transform = Matrix4x4.TRS(basePosition + new Vector3(0f, 3.72f, 0f),
                Quaternion.identity, Vector3.one * 0.42f)
        });
    }

    private static void AddBox(
        List<CombineInstance> list, Mesh cube, Vector3 basePosition, Quaternion rotation,
        Vector3 localCenter, Vector3 size)
    {
        list.Add(new CombineInstance
        {
            mesh = cube,
            transform = Matrix4x4.TRS(basePosition + rotation * localCenter, rotation, size)
        });
    }

    private static void CreateCombined(
        Transform root, string name, List<CombineInstance> parts, Material material)
    {
        if (parts.Count == 0)
        {
            return;
        }
        Mesh mesh = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
        mesh.CombineMeshes(parts.ToArray(), true, true);
        mesh.RecalculateBounds();
        GameObject holder = new GameObject(name);
        holder.transform.SetParent(root, false);
        holder.AddComponent<MeshFilter>().sharedMesh = mesh;
        MeshRenderer renderer = holder.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.lightProbeUsage = LightProbeUsage.Off;
        renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
    }

    private static Mesh PrimitiveMesh(PrimitiveType type)
    {
        GameObject temporary = GameObject.CreatePrimitive(type);
        Mesh mesh = temporary.GetComponent<MeshFilter>().sharedMesh;
        // Immediate: its collider must not linger for the clearance queries.
        Object.DestroyImmediate(temporary);
        return mesh;
    }

    // ---- ground ----

    private static void CreateGround(
        Transform outdoorRoot,
        Transform root,
        float floorY,
        Bounds bounds,
        Material grassMaterial,
        List<Vector4> treeShade)
    {
        if (grassMaterial == null)
        {
            return;
        }

        Texture2D texture = BakeGroundTexture(bounds, treeShade);
        // Lit ground; the material keeps its name ("Outdoor park ground").
        grassMaterial.SetFloat("_Smoothness", 0.08f);
        grassMaterial.SetFloat("_Metallic", 0f);
        if (grassMaterial.HasProperty("_BaseMap"))
        {
            grassMaterial.SetTexture("_BaseMap", texture);
            grassMaterial.SetTextureScale("_BaseMap", Vector2.one);
            grassMaterial.SetTextureOffset("_BaseMap", Vector2.zero);
        }
        if (grassMaterial.HasProperty("_MainTex"))
        {
            grassMaterial.SetTexture("_MainTex", texture);
            grassMaterial.SetTextureScale("_MainTex", Vector2.one);
        }
        if (grassMaterial.HasProperty("_BaseColor"))
        {
            grassMaterial.SetColor("_BaseColor", Color.white);
        }
        GroundMaterial = grassMaterial;
        GymVegetationNightTint.Register(grassMaterial, texture);

        // One flat quad out to the city ring, whose top it matches exactly.
        float y = floorY + GroundTopOffset;
        float minX = bounds.min.x - CityRingGap;
        float maxX = bounds.max.x + CityRingGap;
        float minZ = bounds.min.z - CityRingGap;
        float maxZ = bounds.max.z + CityRingGap;
        Mesh mesh = new Mesh { name = GroundName + " Mesh" };
        Vector3[] vertices =
        {
            new Vector3(minX, y, minZ), new Vector3(minX, y, maxZ),
            new Vector3(maxX, y, maxZ), new Vector3(maxX, y, minZ)
        };
        mesh.vertices = vertices;
        Vector2[] uvs = new Vector2[4];
        for (int i = 0; i < 4; i++)
        {
            uvs[i] = ParkUv(bounds, vertices[i]);
        }
        mesh.uv = uvs;
        mesh.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
        mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
        mesh.RecalculateBounds();

        GameObject ground = new GameObject(GroundName);
        ground.transform.SetParent(root, false);
        ground.AddComponent<MeshFilter>().sharedMesh = mesh;
        MeshRenderer renderer = ground.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = grassMaterial;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        // Lit like the city ring it fades into (which takes no shadows).
        renderer.receiveShadows = false;
        renderer.lightProbeUsage = LightProbeUsage.Off;

        // The thin strip behind the parking wall shares the park ground and
        // sits at the same height, so it adds no step.
        Transform strip = FindChild(outdoorRoot, "Parking Park Landscape");
        if (strip != null)
        {
            MeshRenderer stripRenderer = strip.GetComponent<MeshRenderer>();
            MeshFilter stripFilter = strip.GetComponent<MeshFilter>();
            if (stripRenderer != null && stripFilter != null)
            {
                Vector3 position = strip.position;
                position.y = y - strip.lossyScale.y * 0.5f;
                strip.position = position;
                stripRenderer.sharedMaterial = grassMaterial;
                Mesh stripMesh = Object.Instantiate(stripFilter.sharedMesh);
                Vector3[] stripVertices = stripMesh.vertices;
                Vector2[] stripUvs = new Vector2[stripVertices.Length];
                for (int i = 0; i < stripVertices.Length; i++)
                {
                    stripUvs[i] = ParkUv(bounds, strip.TransformPoint(stripVertices[i]));
                }
                stripMesh.uv = stripUvs;
                stripFilter.sharedMesh = stripMesh;
            }
        }
    }

    private static Vector2 ParkUv(Bounds bounds, Vector3 world)
    {
        return new Vector2(
            (world.x - bounds.min.x) / bounds.size.x,
            (world.z - bounds.min.z) / bounds.size.z);
    }

    private static Texture2D BakeGroundTexture(Bounds bounds, List<Vector4> treeShade)
    {
        int width = Mathf.Clamp(Mathf.CeilToInt(bounds.size.x * PixelsPerMetre), 64, MaxTextureSize);
        int height = Mathf.Clamp(Mathf.CeilToInt(bounds.size.z * PixelsPerMetre), 64, MaxTextureSize);
        Texture2D texture = new Texture2D(width, height, TextureFormat.RGBA32, true, false)
        {
            name = "Park ground baked texture",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Trilinear,
            anisoLevel = 4
        };
        // Tree shade stamped once on the 0.5 m grid, then sampled smoothly.
        float[] shadeField = new float[nx * nz];
        for (int i = 0; i < treeShade.Count; i++)
        {
            Vector4 tree = treeShade[i];
            int reach = Mathf.CeilToInt(tree.z / Cell);
            int tx = Mathf.FloorToInt((tree.x - originX) / Cell);
            int tz = Mathf.FloorToInt((tree.y - originZ) / Cell);
            for (int z = Mathf.Max(0, tz - reach); z <= Mathf.Min(nz - 1, tz + reach); z++)
            {
                for (int x = Mathf.Max(0, tx - reach); x <= Mathf.Min(nx - 1, tx + reach); x++)
                {
                    float dx = originX + (x + 0.5f) * Cell - tree.x;
                    float dz = originZ + (z + 0.5f) * Cell - tree.y;
                    float r = Mathf.Sqrt(dx * dx + dz * dz) / tree.z;
                    if (r < 1f)
                    {
                        int index = z * nx + x;
                        shadeField[index] = Mathf.Max(shadeField[index], 1f - r);
                    }
                }
            }
        }

        Color32[] pixels = new Color32[width * height];
        Color lawnDark = new Color(0.13f, 0.32f, 0.07f);
        Color lawnLight = new Color(0.23f, 0.45f, 0.11f);
        Color gravel = new Color(0.44f, 0.41f, 0.35f);
        Color edging = new Color(0.2f, 0.2f, 0.19f);
        Color mulch = new Color(0.11f, 0.08f, 0.055f);
        float stepX = bounds.size.x / width;
        float stepZ = bounds.size.z / height;
        for (int py = 0; py < height; py++)
        {
            float wz = bounds.min.z + (py + 0.5f) * stepZ;
            for (int px = 0; px < width; px++)
            {
                float wx = bounds.min.x + (px + 0.5f) * stepX;
                float d = Sample(distance, wx, wz);
                float p = Sample(pathDistance, wx, wz);
                float edge = Sample(edgeDistance, wx, wz);
                float grain = Hash01(px, py) - 0.5f;
                float patch = Mathf.PerlinNoise(wx * 0.11f + 7.3f, wz * 0.11f + 2.9f);
                // Lawn with broad mowing stripes.
                float stripe = Mathf.Sin(wx * Mathf.PI / 2.2f) > 0f ? 0.06f : -0.04f;
                Color color = Color.Lerp(lawnDark, lawnLight, patch * 0.8f + 0.1f + stripe);
                color *= 1f + grain * 0.18f;

                // A thin mulch line at the hedge bases only: narrow strips
                // along walls stay lawn so their plants sit in grass.
                float mulchAmount = 0.6f * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.35f, 0.9f, d)));
                color = Color.Lerp(color, mulch * (1f + grain * 0.4f), mulchAmount);

                // Gravel paths with a darker stone edging.
                if (p < PathHalfWidth + 0.25f)
                {
                    float pathAmount = 1f - Mathf.SmoothStep(0f, 1f,
                        Mathf.InverseLerp(PathHalfWidth - 0.08f, PathHalfWidth + 0.04f, p));
                    float edgeAmount = 1f - Mathf.Abs(p - PathHalfWidth) / 0.16f;
                    Color path = gravel * (1f + grain * 0.35f);
                    color = Color.Lerp(color, path, pathAmount);
                    color = Color.Lerp(color, edging, Mathf.Clamp01(edgeAmount) * 0.85f);
                }

                // Soft shade under each tree crown.
                color *= 1f - 0.28f * Sample(shadeField, wx, wz);

                // Fade into the city paving at the outer edge.
                float toCity = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.1f, 1.4f, edge));
                color = Color.Lerp(color, CityPavingColor, toCity);
                if (px == 0 && py == height / 2)
                {
                    EdgeColorForVerification = color;
                }
                pixels[py * width + px] = new Color32(
                    (byte)(Mathf.Clamp01(color.r) * 255f),
                    (byte)(Mathf.Clamp01(color.g) * 255f),
                    (byte)(Mathf.Clamp01(color.b) * 255f),
                    255);
            }
        }
        texture.SetPixels32(pixels);
        texture.Apply(true, true);
        return texture;
    }

    // ---- grass ----

    private static void CreateGrass(Transform root, float floorY, System.Random random)
    {
        Material material = CreateGrassMaterial();
        int chunksX = Mathf.CeilToInt(nx * Cell / GrassChunkSize);
        int chunksZ = Mathf.CeilToInt(nz * Cell / GrassChunkSize);
        float baseY = floorY + GroundTopOffset - 0.01f;
        List<Vector3> vertices = new List<Vector3>();
        List<Vector3> normals = new List<Vector3>();
        List<Vector2> uvs = new List<Vector2>();
        List<int> triangles = new List<int>();
        float tuftsPerCell = GrassTuftsPerSquareMetre * Cell * Cell;
        for (int cz = 0; cz < chunksZ; cz++)
        {
            for (int cx = 0; cx < chunksX; cx++)
            {
                vertices.Clear();
                normals.Clear();
                uvs.Clear();
                triangles.Clear();
                int cellsPerChunk = Mathf.RoundToInt(GrassChunkSize / Cell);
                for (int z = cz * cellsPerChunk; z < Mathf.Min(nz, (cz + 1) * cellsPerChunk); z++)
                {
                    for (int x = cx * cellsPerChunk; x < Mathf.Min(nx, (cx + 1) * cellsPerChunk); x++)
                    {
                        int index = z * nx + x;
                        if (built[index] || distance[index] < 0.35f ||
                            pathDistance[index] < PathHalfWidth + 0.2f ||
                            edgeDistance[index] < 0.9f)
                        {
                            continue;
                        }
                        // Thinner at hedge bases and wall strips, denser on open lawn.
                        float density = tuftsPerCell * Mathf.Lerp(
                            0.45f, 1f, Mathf.InverseLerp(0.35f, 2.4f, distance[index]));
                        int tufts = Mathf.FloorToInt(density + (float)random.NextDouble());
                        for (int t = 0; t < tufts; t++)
                        {
                            float wx = originX + (x + (float)random.NextDouble()) * Cell;
                            float wz = originZ + (z + (float)random.NextDouble()) * Cell;
                            if (IsBuiltAt(wx, wz))
                            {
                                continue;
                            }
                            AddTuft(vertices, normals, uvs, triangles, new Vector3(wx, baseY, wz), random);
                        }
                    }
                }
                if (vertices.Count == 0)
                {
                    continue;
                }
                Mesh mesh = new Mesh { name = $"Park Grass {cx},{cz}" };
                if (vertices.Count > 65000)
                {
                    mesh.indexFormat = IndexFormat.UInt32;
                }
                mesh.SetVertices(vertices);
                mesh.SetNormals(normals);
                mesh.SetUVs(0, uvs);
                mesh.SetTriangles(triangles, 0);
                mesh.RecalculateBounds();
                // Static: drop the CPU copy once it is on the GPU.
                mesh.UploadMeshData(true);
                GameObject chunk = new GameObject(mesh.name);
                chunk.transform.SetParent(root, false);
                chunk.AddComponent<MeshFilter>().sharedMesh = mesh;
                MeshRenderer renderer = chunk.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = true;
                renderer.lightProbeUsage = LightProbeUsage.Off;
                renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            }
        }
    }

    // Three thin blades fanned from one root. UV.x picks a shade column of
    // the grass gradient, UV.y runs root (dark) to tip (light). Normals point
    // up so the lawn lights evenly from every side.
    private static void AddTuft(
        List<Vector3> vertices, List<Vector3> normals, List<Vector2> uvs, List<int> triangles,
        Vector3 root, System.Random random)
    {
        float shade = (float)random.NextDouble();
        float baseYaw = (float)random.NextDouble() * Mathf.PI * 2f;
        for (int blade = 0; blade < 3; blade++)
        {
            float yaw = baseYaw + blade * 2.1f;
            Vector3 side = new Vector3(Mathf.Cos(yaw), 0f, Mathf.Sin(yaw));
            Vector3 lean = new Vector3(-side.z, 0f, side.x) * Range(random, -0.09f, 0.09f) +
                side * Range(random, -0.05f, 0.05f);
            float height = Range(random, 0.16f, 0.34f);
            float halfWidth = Range(random, 0.018f, 0.03f);
            Vector3 offset = side * Range(random, 0f, 0.06f);
            int start = vertices.Count;
            vertices.Add(root + offset - side * halfWidth);
            vertices.Add(root + offset + side * halfWidth);
            vertices.Add(root + offset + lean + Vector3.up * height);
            normals.Add(Vector3.up);
            normals.Add(Vector3.up);
            normals.Add(Vector3.up);
            uvs.Add(new Vector2(shade, 0f));
            uvs.Add(new Vector2(shade, 0f));
            uvs.Add(new Vector2(shade, 1f));
            triangles.Add(start);
            triangles.Add(start + 2);
            triangles.Add(start + 1);
            GrassBladeCount++;
        }
    }

    private static Material CreateGrassMaterial()
    {
        const int columns = 8;
        const int rows = 16;
        Texture2D gradient = new Texture2D(columns, rows, TextureFormat.RGBA32, false, false)
        {
            name = "Park grass blade gradient",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear
        };
        Color root = new Color(0.09f, 0.22f, 0.05f);
        for (int x = 0; x < columns; x++)
        {
            float t = x / (float)(columns - 1);
            Color tip = Color.Lerp(new Color(0.28f, 0.52f, 0.11f), new Color(0.42f, 0.6f, 0.16f), t);
            for (int y = 0; y < rows; y++)
            {
                float v = y / (float)(rows - 1);
                gradient.SetPixel(x, y, Color.Lerp(root, tip, Mathf.Pow(v, 0.8f)));
            }
        }
        gradient.Apply(false, true);
        Material material = CreateLitMaterial("Park grass blades", Color.white, 0f, 0.15f);
        material.SetTexture("_BaseMap", gradient);
        // Blades are single triangles: render both faces.
        if (material.HasProperty("_Cull"))
        {
            material.SetFloat("_Cull", (float)CullMode.Off);
        }
        material.doubleSidedGI = true;
        material.enableInstancing = true;
        GymVegetationNightTint.Register(material, gradient);
        return material;
    }

    private static Material CreateLitMaterial(string name, Color color, float metallic, float smoothness)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        Material material = new Material(shader) { name = name };
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
        material.color = color;
        material.SetFloat("_Metallic", metallic);
        material.SetFloat("_Smoothness", smoothness);
        return material;
    }

    // ---- shared placement helpers ----

    private static bool IsUnbuilt(Vector3 spot, float floorY)
    {
        // Any visible collider straight below (floor, road, route, gym,
        // store) means the spot is built or walkable. The source scene Plane
        // (hidden by GymArenaBootstrap) does not count.
        int hitCount = Physics.RaycastNonAlloc(
            new Vector3(spot.x, floorY + 40f, spot.z),
            Vector3.down,
            RayHits,
            42f,
            Physics.DefaultRaycastLayers,
            QueryTriggerInteraction.Ignore);
        for (int i = 0; i < hitCount; i++)
        {
            Collider collider = RayHits[i].collider;
            if (collider.name.StartsWith("Plane", System.StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            Renderer renderer = collider.GetComponent<Renderer>();
            if (renderer == null || !renderer.enabled)
            {
                continue;
            }
            return false;
        }
        return true;
    }

    // Walls and invisible blockers standing on the cell.
    private static bool IsBlocked(Vector3 spot, float floorY)
    {
        return Physics.CheckBox(
            new Vector3(spot.x, floorY + 0.9f, spot.z),
            new Vector3(Cell * 0.5f, 0.5f, Cell * 0.5f),
            Quaternion.identity,
            Physics.DefaultRaycastLayers,
            QueryTriggerInteraction.Ignore);
    }

    private static bool IsClear(Vector3 spot, float floorY, float radius, float height)
    {
        int count = Physics.OverlapBoxNonAlloc(
            new Vector3(spot.x, floorY + 0.35f + height * 0.5f, spot.z),
            new Vector3(radius, height * 0.5f, radius),
            ClearanceHits,
            Quaternion.identity,
            Physics.DefaultRaycastLayers,
            QueryTriggerInteraction.Ignore);
        return count == 0;
    }

    private static void Plant(
        Transform root,
        Species species,
        Vector3 position,
        float scale,
        float yaw,
        float floorY)
    {
        RequestedPlants++;
        if (species.Tree) TreeCount++;
        else BushCount++;
        PlantPositions.Add(position);
        RuntimeGlbSceneLoader.Request(
            AssetFolder + species.File,
            root,
            position,
            Quaternion.Euler(0f, yaw, 0f),
            Vector3.one * scale,
            (species.Tree ? "Outdoor Tree " : "Outdoor Bush ") +
                species.File.Replace(".glb", string.Empty),
            0,
            settleOnSupport: true,
            supportY: floorY + GroundTopOffset,
            onLoaded: OnPlantLoaded);
    }

    private static void OnPlantLoaded(GameObject plant)
    {
        if (plant == null)
        {
            FailedPlants++;
            Debug.LogError("GYMCHAOS_OUTDOOR_FOLIAGE_LOAD_FAIL");
            return;
        }

        LoadedPlants++;
        MeshRenderer[] renderers = plant.GetComponentsInChildren<MeshRenderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            MeshRenderer renderer = renderers[i];
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = true;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }
        Collider[] colliders = plant.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < colliders.Length; i++)
        {
            Object.Destroy(colliders[i]);
        }

        if (LoadedPlants + FailedPlants == RequestedPlants)
        {
            Debug.Log(
                $"GYMCHAOS_OUTDOOR_FOLIAGE_{(FailedPlants == 0 ? "OK" : "FAIL")} " +
                $"plants={RequestedPlants} loaded={LoadedPlants} failed={FailedPlants} " +
                $"trees={TreeCount} bushes={BushCount}");
        }
    }

    private static Transform FindChild(Transform root, string name)
    {
        if (root.name == name)
        {
            return root;
        }
        for (int i = 0; i < root.childCount; i++)
        {
            Transform found = FindChild(root.GetChild(i), name);
            if (found != null)
            {
                return found;
            }
        }
        return null;
    }

    private static float Hash01(int x, int y)
    {
        unchecked
        {
            uint value = (uint)(Seed ^ (x * 374761393) ^ (y * 668265263));
            value = (value ^ (value >> 13)) * 1274126177u;
            value ^= value >> 16;
            return (value & 0xffffu) / 65535f;
        }
    }

    private static float Range(System.Random random, float min, float max)
    {
        return min + (float)random.NextDouble() * (max - min);
    }
}
