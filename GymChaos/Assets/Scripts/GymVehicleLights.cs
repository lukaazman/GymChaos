using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Head and tail lights for a car after dark. The lamps are the car's own
/// lamp geometry made to glow: named lamp parts where the model has them
/// (the player buggy), otherwise the body triangles whose texture shows the
/// lamps (bright glass at the front, red lenses at the back). One forward
/// spot lights the road. Attach it to the transform whose forward is the
/// car's driving direction (visitor car root, police car root, race car
/// holder). The bus and Goku's cloud do not get lights. Lamps are on only
/// while it is dark and the car is in use (driving, or racing): a parked car
/// stays dark. The glow is added light on the lens, not paint over it.
/// </summary>
public sealed class GymVehicleLights : MonoBehaviour
{
    // Daylight below which lamps switch on, and the margin back to off, so
    // dusk never flickers them.
    public const float OnBelowDaylight = 0.3f;
    private const float OffAboveDaylight = 0.36f;
    private const float CheckInterval = 0.25f;
    private const float BuildRetrySeconds = 0.4f;
    // Front and back slices of the body (fraction of its length) that may hold lamps.
    private const float EndSlice = 0.2f;
    private const int MinLampTriangles = 6;

    private static readonly Color HeadColor = new Color(1f, 0.88f, 0.66f);
    private static readonly Color TailColor = new Color(1f, 0.1f, 0.05f);
    private static readonly Color TailTextureTint = new Color(1f, 0.35f, 0.3f);
    private const float HeadIntensity = 0.8f;
    private const float TailIntensity = 1.1f;
    private const float TailTextureIntensity = 1.5f;
    private static readonly List<GymVehicleLights> all = new List<GymVehicleLights>();
    private static readonly Dictionary<(Mesh, Texture, int, string), LampMeshes> lampCache = new Dictionary<(Mesh, Texture, int, string), LampMeshes>();
    private static readonly Dictionary<(Texture, Color, float), Material> glowMaterials = new Dictionary<(Texture, Color, float), Material>();
    private static Shader glowShader;

    /// <summary>
    /// A lamp on the body, read off the lamp atlas (each car alone, straight
    /// on): |lateral| from the centre (0) to the widest point (1), height
    /// from the bottom (0) to the top (1) of the body mesh. Mirrored left and
    /// right. Plain lamps glow in a flat colour because their texture is dark.
    /// </summary>
    private struct LampRegion
    {
        public bool Front;
        public float LateralMin, LateralMax, HeightMin, HeightMax;
        public bool Plain;

        public LampRegion(bool front, float lateralMin, float lateralMax, float heightMin, float heightMax, bool plain)
        {
            Front = front;
            LateralMin = lateralMin;
            LateralMax = lateralMax;
            HeightMin = heightMin;
            HeightMax = heightMax;
            Plain = plain;
        }

        public bool Contains(float lateral, float height) =>
            lateral >= LateralMin && lateral <= LateralMax && height >= HeightMin && height <= HeightMax;
    }

    // Measured per car from Logs/agent/race/atlas_*.png (GymChaosRaceCaptureVerifier).
    private static readonly Dictionary<string, LampRegion[]> Regions = new Dictionary<string, LampRegion[]>
    {
        ["zyzz"] = new[]
        {
            new LampRegion(true, 0.54f, 0.80f, 0.30f, 0.49f, true),
            new LampRegion(false, 0.72f, 0.82f, 0.30f, 0.60f, true),
        },
        ["cbum"] = new[]
        {
            new LampRegion(true, 0.61f, 0.79f, 0.31f, 0.44f, true),
            new LampRegion(false, 0.55f, 0.93f, 0.42f, 0.57f, true),
        },
        ["jaycutler"] = new[]
        {
            new LampRegion(true, 0.47f, 0.77f, 0.33f, 0.46f, true),
            new LampRegion(false, 0.58f, 0.80f, 0.46f, 0.59f, false),
        },
        ["arnold"] = new[]
        {
            new LampRegion(true, 0.26f, 0.46f, 0.36f, 0.48f, true),
            new LampRegion(false, 0.55f, 0.79f, 0.35f, 0.64f, false),
        },
        ["ronnie"] = new[]
        {
            new LampRegion(true, 0.47f, 0.77f, 0.29f, 0.40f, true),
            new LampRegion(false, 0.38f, 0.76f, 0.32f, 0.46f, false),
        },
        ["police"] = new[]
        {
            new LampRegion(true, 0.47f, 0.76f, 0.29f, 0.40f, true),
            new LampRegion(false, 0.38f, 0.76f, 0.32f, 0.46f, false),
        },
    };

    private sealed class LampMeshes
    {
        public Mesh Head;
        public Mesh Tail;
        // No lamp in the texture: the lamp-corner surface glows in a flat colour.
        public bool HeadPlain;
        public bool TailPlain;
    }

    private string modelKey;
    private Transform lampRoot;
    private readonly List<GameObject> lampObjects = new List<GameObject>();

    private bool built;
    private bool lit;
    private float nextCheck;

    public static IReadOnlyList<GymVehicleLights> All => all;
    /// <summary>The textured body mesh the lamps were cut from (null for named lamp parts).</summary>
    public MeshFilter BodyFilter { get; private set; }
    public bool IsBuilt => built;
    public bool IsLit => lit && built;
    /// <summary>Verification: glowing lamp pieces taken from the car's own model.</summary>
    public int LampPieceCount => lampObjects.Count;
    /// <summary>Lamps only while this is true (driving); null means always (race cars).</summary>
    public System.Func<bool> ActiveWhen { get; set; }
    private bool InUse => ActiveWhen == null || ActiveWhen();

    /// <param name="modelKey">Car model for its measured lamp regions ("cbum", "police", ...).</param>
    /// <param name="activeWhen">Lamps only while this holds (e.g. not parked); null for always.</param>
    public static GymVehicleLights Attach(Transform drivingFrame, string modelKey = null, System.Func<bool> activeWhen = null)
    {
        if (drivingFrame == null)
        {
            return null;
        }
        GymVehicleLights lights = drivingFrame.GetComponent<GymVehicleLights>();
        if (lights == null)
        {
            lights = drivingFrame.gameObject.AddComponent<GymVehicleLights>();
        }
        if (modelKey != null)
        {
            lights.modelKey = modelKey.ToLowerInvariant();
        }
        lights.ActiveWhen = activeWhen;
        return lights;
    }

    /// <summary>True while it is dark enough for car lamps.</summary>
    public static bool IsDark(bool currentlyLit)
    {
        GymTimeOfDay clock = GymTimeOfDay.Instance;
        if (clock == null)
        {
            return false;
        }
        float daylight = clock.Daylight01;
        return currentlyLit ? daylight < OffAboveDaylight : daylight < OnBelowDaylight;
    }

    private void OnEnable()
    {
        all.Add(this);
        nextCheck = 0f;
    }

    private void OnDisable()
    {
        all.Remove(this);
    }

    private void Update()
    {
        if (Time.unscaledTime < nextCheck)
        {
            return;
        }
        if (!built)
        {
            // Models load asynchronously: retry until the body has renderers.
            nextCheck = Time.unscaledTime + BuildRetrySeconds;
            built = TryBuild();
            if (!built)
            {
                return;
            }
        }
        nextCheck = Time.unscaledTime + CheckInterval;
        SetLit(IsDark(lit) && InUse);
    }

    /// <summary>Verification: apply the current clock now instead of on the next check.</summary>
    public void RefreshNow()
    {
        if (!built)
        {
            built = TryBuild();
        }
        if (built)
        {
            SetLit(IsDark(lit) && InUse);
        }
    }

    private void SetLit(bool value)
    {
        if (lit == value && built && lampRoot != null && lampRoot.gameObject.activeSelf == value)
        {
            return;
        }
        lit = value;
        if (lampRoot != null)
        {
            lampRoot.gameObject.SetActive(value);
        }
        foreach (GameObject lamp in lampObjects)
        {
            if (lamp != null) lamp.SetActive(value);
        }
    }

    private bool TryBuild()
    {
        if (!TryMeasureBody(out Bounds body))
        {
            return false;
        }

        // Unscaled frame for the road spot: some car roots are scaled.
        lampRoot = new GameObject("Vehicle Lights").transform;
        lampRoot.SetParent(transform, false);
        Vector3 scale = transform.lossyScale;
        lampRoot.localScale = new Vector3(
            1f / Mathf.Max(scale.x, 1e-4f), 1f / Mathf.Max(scale.y, 1e-4f), 1f / Mathf.Max(scale.z, 1e-4f));
        if (!UseNamedLampParts())
        {
            UseTexturedLamps();
        }
        // One road beam out of each head lamp.
        for (int side = -1; side <= 1; side += 2)
        {
            Vector3 lampCentre = HeadLampCentre(side, body);
            GameObject beam = new GameObject(side < 0 ? "Head Beam Left" : "Head Beam Right");
            beam.transform.SetParent(lampRoot, false);
            beam.transform.localPosition = lampCentre + Vector3.forward * 0.08f;
            beam.transform.localRotation = Quaternion.Euler(10f, side * 4f, 0f);
            Light beamLight = beam.AddComponent<Light>();
            beamLight.type = LightType.Spot;
            beamLight.color = new Color(1f, 0.93f, 0.78f);
            beamLight.range = 20f;
            beamLight.spotAngle = 58f;
            beamLight.innerSpotAngle = 30f;
            beamLight.intensity = 4f;
            beamLight.shadows = LightShadows.None;
        }
        lit = true;
        SetLit(false);
        return true;
    }

    /// <summary>Centre of the left (-1) or right (1) head lamp in the lamp frame.</summary>
    private Vector3 HeadLampCentre(int side, Bounds body)
    {
        Vector3 sum = Vector3.zero;
        int count = 0;
        foreach (GameObject lamp in lampObjects)
        {
            MeshFilter filter = lamp != null && lamp.name.EndsWith("Head") ? lamp.GetComponent<MeshFilter>() : null;
            if (filter == null || filter.sharedMesh == null)
            {
                continue;
            }
            Matrix4x4 toFrame = lampRoot.worldToLocalMatrix * lamp.transform.localToWorldMatrix;
            Vector3[] vertices = filter.sharedMesh.vertices;
            // Lens copies keep the whole vertex list: only used vertices count.
            foreach (int index in filter.sharedMesh.triangles)
            {
                Vector3 point = toFrame.MultiplyPoint3x4(vertices[index]);
                if (Mathf.Sign(point.x - body.center.x) == side)
                {
                    sum += point;
                    count++;
                }
            }
        }
        return count > 0
            ? sum / count
            : new Vector3(body.center.x + side * body.extents.x * 0.62f, body.min.y + body.size.y * 0.35f, body.max.z);
    }

    private IEnumerable<Renderer> BodyRenderers()
    {
        foreach (Renderer renderer in GetComponentsInChildren<Renderer>(false))
        {
            // Drivers, effects and our own lamps are not the body.
            if (!renderer.enabled || renderer is SkinnedMeshRenderer || renderer is ParticleSystemRenderer ||
                renderer is TrailRenderer || renderer is LineRenderer ||
                (lampRoot != null && renderer.transform.IsChildOf(lampRoot)) ||
                renderer.name.StartsWith("Vehicle Lamp") ||
                renderer.GetComponentInParent<EnemyFighter>() != null ||
                renderer.GetComponentInParent<PlayerMovement>() != null)
            {
                continue;
            }
            yield return renderer;
        }
    }

    // ---------------------------------------------------------- named parts

    private static bool IsHeadName(string name) =>
        name.IndexOf("headlight", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
        name.IndexOf("head_light", System.StringComparison.OrdinalIgnoreCase) >= 0;

    private static bool IsTailName(string name) =>
        name.IndexOf("taillight", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
        name.IndexOf("tail_light", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
        name.IndexOf("brake", System.StringComparison.OrdinalIgnoreCase) >= 0;

    /// <summary>Models with lamp nodes or lamp materials glow exactly there.</summary>
    private bool UseNamedLampParts()
    {
        List<Renderer> renderers = new List<Renderer>(BodyRenderers());
        foreach (Renderer renderer in renderers)
        {
            MeshFilter filter = renderer.GetComponent<MeshFilter>();
            Mesh mesh = filter != null ? filter.sharedMesh : null;
            if (mesh == null || !mesh.isReadable)
            {
                continue;
            }
            Material[] materials = renderer.sharedMaterials;
            // Lamp materials mark the lens exactly (a lamp node may also hold
            // its chrome rim); the node name counts only without them.
            bool namedMaterials = false;
            foreach (Material material in materials)
            {
                namedMaterials |= material != null && (IsHeadName(material.name) || IsTailName(material.name));
            }
            for (int i = 0; i < materials.Length && i < mesh.subMeshCount; i++)
            {
                string materialName = materials[i] != null ? materials[i].name : string.Empty;
                bool head = namedMaterials ? IsHeadName(materialName) : IsHeadName(renderer.name);
                bool tail = namedMaterials ? IsTailName(materialName) : IsTailName(renderer.name);
                if (!head && !tail)
                {
                    continue;
                }
                Mesh lens = new Mesh { name = renderer.name + " Lens Glow", indexFormat = mesh.indexFormat };
                lens.vertices = mesh.vertices;
                lens.normals = mesh.normals;
                lens.uv = mesh.uv;
                lens.SetTriangles(mesh.GetTriangles(i), 0);
                // Authored lenses: full glow everywhere (vertex alpha 1).
                Color[] white = new Color[mesh.vertexCount];
                for (int w = 0; w < white.Length; w++) white[w] = Color.white;
                lens.colors = white;
                lens.RecalculateBounds();
                lampObjects.Add(LampOverlay(head ? "Vehicle Lamp Head" : "Vehicle Lamp Tail", renderer.transform, lens,
                    head ? GlowMaterial(null, HeadColor, HeadIntensity) : GlowMaterial(null, TailColor, TailIntensity)));
            }
        }
        return lampObjects.Count > 0;
    }

    // ------------------------------------------------------- textured lamps

    /// <summary>
    /// Scanned cars are one textured body: copy the triangles whose texels
    /// are lamp glass (front) or red lenses (back) into a glowing overlay that
    /// sits on the body itself.
    /// </summary>
    private void UseTexturedLamps()
    {
        MeshFilter bodyFilter = null;
        int mostVertices = 0;
        foreach (Renderer renderer in BodyRenderers())
        {
            MeshFilter filter = renderer.GetComponent<MeshFilter>();
            string name = renderer.name;
            if (filter == null || filter.sharedMesh == null || !filter.sharedMesh.isReadable ||
                name.StartsWith("Wheel") || name.StartsWith("Cabin") || name.StartsWith("Door") ||
                name.StartsWith("Glass") || name.StartsWith("Marker") || name.StartsWith("Steering") ||
                name.StartsWith("Driver"))
            {
                continue;
            }
            if (filter.sharedMesh.vertexCount > mostVertices)
            {
                mostVertices = filter.sharedMesh.vertexCount;
                bodyFilter = filter;
            }
        }
        if (bodyFilter == null)
        {
            return;
        }
        BodyFilter = bodyFilter;
        MeshRenderer bodyRenderer = bodyFilter.GetComponent<MeshRenderer>();
        Material bodyMaterial = bodyRenderer != null ? bodyRenderer.sharedMaterial : null;
        Texture texture = bodyMaterial != null ? bodyMaterial.mainTexture : null;
        if (texture == null)
        {
            return;
        }

        Transform part = bodyFilter.transform;
        Vector3 forward = part.InverseTransformDirection(transform.forward).normalized;
        Vector3 up = part.InverseTransformDirection(Vector3.up).normalized;
        var key = (bodyFilter.sharedMesh, texture,
            Mathf.RoundToInt(Vector3.SignedAngle(Vector3.forward, forward, Vector3.up)), modelKey ?? string.Empty);
        if (!lampCache.TryGetValue(key, out LampMeshes meshes))
        {
            float lift = 0.003f / Mathf.Max(part.lossyScale.x, 1e-4f);
            meshes = modelKey != null && Regions.TryGetValue(modelKey, out LampRegion[] regions)
                ? RegionLamps(bodyFilter.sharedMesh, regions, forward, up, lift)
                : ExtractLamps(bodyFilter.sharedMesh, texture, forward, up, lift);
            lampCache[key] = meshes;
        }
        if (meshes == null)
        {
            return;
        }
        if (meshes.Head != null)
        {
            lampObjects.Add(LampOverlay("Vehicle Lamp Head", part, meshes.Head,
                GlowMaterial(texture, HeadColor, HeadIntensity, meshes.HeadPlain ? 2f : 1f)));
        }
        if (meshes.Tail != null)
        {
            lampObjects.Add(LampOverlay("Vehicle Lamp Tail", part, meshes.Tail,
                meshes.TailPlain ? GlowMaterial(texture, TailColor, TailIntensity, 2f) : GlowMaterial(texture, TailTextureTint, TailTextureIntensity, 1f)));
        }
    }

    private static GameObject LampOverlay(string name, Transform part, Mesh mesh, Material material)
    {
        GameObject lamp = new GameObject(name);
        lamp.transform.SetParent(part, false);
        lamp.AddComponent<MeshFilter>().sharedMesh = mesh;
        MeshRenderer renderer = lamp.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.lightProbeUsage = LightProbeUsage.Off;
        renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        lamp.SetActive(false);
        return lamp;
    }

    /// <summary>Body triangles on the nose or tail inside the car's measured lamp regions.</summary>
    private static LampMeshes RegionLamps(Mesh mesh, LampRegion[] regions, Vector3 forward, Vector3 up, float lift)
    {
        Vector3[] vertices = mesh.vertices;
        Vector2[] uvs = mesh.uv;
        Vector3[] normals = mesh.normals;
        int[] triangles = mesh.triangles;
        if (uvs == null || uvs.Length != vertices.Length)
        {
            uvs = new Vector2[vertices.Length];
        }
        Vector3 side = Vector3.Cross(up, forward).normalized;
        float minAlong = float.MaxValue, maxAlong = float.MinValue, minUp = float.MaxValue, maxUp = float.MinValue, maxSide = 0f;
        foreach (Vector3 v in vertices)
        {
            float along = Vector3.Dot(v, forward);
            float height = Vector3.Dot(v, up);
            minAlong = Mathf.Min(minAlong, along);
            maxAlong = Mathf.Max(maxAlong, along);
            minUp = Mathf.Min(minUp, height);
            maxUp = Mathf.Max(maxUp, height);
            maxSide = Mathf.Max(maxSide, Mathf.Abs(Vector3.Dot(v, side)));
        }
        float length = maxAlong - minAlong;
        float tall = Mathf.Max(maxUp - minUp, 1e-4f);
        List<int> head = new List<int>();
        List<int> tail = new List<int>();
        bool headPlain = false;
        bool tailPlain = false;
        bool hasNormals = normals != null && normals.Length == vertices.Length;
        for (int t = 0; t < triangles.Length; t += 3)
        {
            Vector3 a = vertices[triangles[t]];
            Vector3 b = vertices[triangles[t + 1]];
            Vector3 c = vertices[triangles[t + 2]];
            Vector3 centre = (a + b + c) / 3f;
            Vector3 normal = hasNormals
                ? (normals[triangles[t]] + normals[triangles[t + 1]] + normals[triangles[t + 2]]).normalized
                : Vector3.Cross(b - a, c - a).normalized;
            float along = Vector3.Dot(centre, forward);
            float facing = Vector3.Dot(normal, forward);
            bool nose = along > maxAlong - length * 0.25f && facing > 0.2f;
            bool rear = along < minAlong + length * 0.25f && facing < -0.2f;
            if (!nose && !rear)
            {
                continue;
            }
            float lateral = Mathf.Abs(Vector3.Dot(centre, side)) / Mathf.Max(maxSide, 1e-4f);
            float height01 = (Vector3.Dot(centre, up) - minUp) / tall;
            foreach (LampRegion region in regions)
            {
                if (region.Front != nose || !region.Contains(lateral, height01))
                {
                    continue;
                }
                if (nose)
                {
                    head.Add(t);
                    headPlain |= region.Plain;
                }
                else
                {
                    tail.Add(t);
                    tailPlain |= region.Plain;
                }
                break;
            }
        }
        return new LampMeshes
        {
            HeadPlain = headPlain,
            TailPlain = tailPlain,
            Head = head.Count > 0 ? BuildOverlay("Head Lamps", vertices, uvs, normals, triangles, head, forward, lift) : null,
            Tail = tail.Count > 0 ? BuildOverlay("Tail Lamps", vertices, uvs, normals, triangles, tail, -forward, lift) : null
        };
    }

    private static LampMeshes ExtractLamps(Mesh mesh, Texture texture, Vector3 forward, Vector3 up, float lift)
    {
        Color32[] texels = ReadTexels(texture, out int width, out int height);
        if (texels == null)
        {
            return null;
        }
        Vector3[] vertices = mesh.vertices;
        Vector2[] uvs = mesh.uv;
        Vector3[] normals = mesh.normals;
        int[] triangles = mesh.triangles;
        if (uvs == null || uvs.Length != vertices.Length)
        {
            return null;
        }
        Vector3 side = Vector3.Cross(up, forward).normalized;

        float minAlong = float.MaxValue, maxAlong = float.MinValue;
        float minUp = float.MaxValue, maxUp = float.MinValue;
        float maxSide = 0f;
        foreach (Vector3 v in vertices)
        {
            float along = Vector3.Dot(v, forward);
            float height01 = Vector3.Dot(v, up);
            minAlong = Mathf.Min(minAlong, along);
            maxAlong = Mathf.Max(maxAlong, along);
            minUp = Mathf.Min(minUp, height01);
            maxUp = Mathf.Max(maxUp, height01);
            maxSide = Mathf.Max(maxSide, Mathf.Abs(Vector3.Dot(v, side)));
        }
        float length = maxAlong - minAlong;
        float tall = maxUp - minUp;

        // Lamp-height corners of the nose and tail, for cars whose texture
        // shows no recognisable lamp.
        List<int> frontCorners = new List<int>();
        List<int> rearCorners = new List<int>();
        List<int> frontCandidates = new List<int>();
        List<float> frontLuma = new List<float>();
        List<Vector2> frontPlace = new List<Vector2>();
        List<int> rearReds = new List<int>();
        List<float> rearRedLuma = new List<float>();
        List<Vector2> rearPlace = new List<Vector2>();
        int rearSideCount = 0;
        int sampled = 0;
        List<int> dimReds = new List<int>();
        List<float> dimRedness = new List<float>();
        List<Vector2> dimPlace = new List<Vector2>();
        int redSampled = 0;
        for (int t = 0; t < triangles.Length; t += 3)
        {
            Vector3 a = vertices[triangles[t]];
            Vector3 b = vertices[triangles[t + 1]];
            Vector3 c = vertices[triangles[t + 2]];
            Vector3 centre = (a + b + c) / 3f;
            Vector3 normal = normals != null && normals.Length == vertices.Length
                ? (normals[triangles[t]] + normals[triangles[t + 1]] + normals[triangles[t + 2]]).normalized
                : Vector3.Cross(b - a, c - a).normalized;
            float along = Vector3.Dot(centre, forward);
            float facing = Vector3.Dot(normal, forward);
            float height01 = (Vector3.Dot(centre, up) - minUp) / Mathf.Max(tall, 1e-4f);
            float lateral = Mathf.Abs(Vector3.Dot(centre, side)) / Mathf.Max(maxSide, 1e-4f);
            // Number plates sit low in the middle: never a lamp.
            if (lateral < 0.25f && height01 < 0.45f)
            {
                continue;
            }
            bool cornerBand = lateral > 0.55f && lateral < 0.95f && height01 > 0.36f && height01 < 0.52f;
            if (cornerBand && along > maxAlong - length * 0.08f && facing > 0.5f) frontCorners.Add(t);
            if (cornerBand && along < minAlong + length * 0.08f && facing < -0.5f) rearCorners.Add(t);
            Vector2 uv = (uvs[triangles[t]] + uvs[triangles[t + 1]] + uvs[triangles[t + 2]]) / 3f;
            Color32 texel = Sample(texels, width, height, uv);
            float r = texel.r / 255f, g = texel.g / 255f, bl = texel.b / 255f;
            sampled++;
            if (r > 0.3f && r > g * 1.45f && r > bl * 1.45f) redSampled++;
            if (along > maxAlong - length * EndSlice && facing > 0.25f &&
                lateral > 0.3f && height01 > 0.28f && height01 < 0.53f)
            {
                // Head lamps sit at the sides between bumper and hood line;
                // chrome bumpers and hood edges run across the middle.
                float max = Mathf.Max(r, Mathf.Max(g, bl));
                float min = Mathf.Min(r, Mathf.Min(g, bl));
                float saturation = max > 0f ? (max - min) / max : 0f;
                if (saturation < 0.5f)
                {
                    frontCandidates.Add(t);
                    frontLuma.Add(0.299f * r + 0.587f * g + 0.114f * bl);
                    frontPlace.Add(new Vector2(Vector3.Dot(centre, side) / Mathf.Max(maxSide, 1e-4f), height01));
                }
            }
            else if (along < minAlong + length * EndSlice && facing < -0.25f &&
                lateral > 0.3f && height01 > 0.25f && height01 < 0.8f)
            {
                rearSideCount++;
                if (r > 0.15f && r > g * 1.2f && r > bl * 1.2f)
                {
                    // Dark red lenses (unlit in the texture), used if no bright ones exist.
                    dimReds.Add(t);
                    dimRedness.Add(r - Mathf.Max(g, bl));
                    dimPlace.Add(new Vector2(Vector3.Dot(centre, side) / Mathf.Max(maxSide, 1e-4f), height01));
                }
                if (r > 0.3f && r > g * 1.45f && r > bl * 1.45f)
                {
                    rearReds.Add(t);
                    rearRedLuma.Add(r);
                    rearPlace.Add(new Vector2(Vector3.Dot(centre, side) / Mathf.Max(maxSide, 1e-4f), height01));
                }
            }
        }

        // Head lamps are the brightest glass at each side of the nose: one
        // compact blob per side, not a strip.
        List<int> head = new List<int>();
        if (frontLuma.Count > 0)
        {
            List<float> sorted = new List<float>(frontLuma);
            sorted.Sort();
            float threshold = Mathf.Max(0.3f, sorted[Mathf.Clamp(Mathf.RoundToInt(sorted.Count * 0.8f), 0, sorted.Count - 1)]);
            List<int> bright = new List<int>();
            List<Vector2> places = new List<Vector2>();
            for (int i = 0; i < frontCandidates.Count; i++)
            {
                if (frontLuma[i] >= threshold)
                {
                    bright.Add(frontCandidates[i]);
                    places.Add(frontPlace[i]);
                }
            }
            head.AddRange(LampBlob(bright, places, -1));
            head.AddRange(LampBlob(bright, places, 1));
        }

        // Tail lamps: red lenses at each side of the tail. On a red car the
        // paint is red too: then only the brightest reds are lenses.
        List<int> tailPicked = new List<int>();
        List<Vector2> tailPlaces = new List<Vector2>();
        float redCut = 0f;
        bool redPaint = redSampled > sampled * 0.15f || rearReds.Count > rearSideCount * 0.4f;
        if (redPaint && rearRedLuma.Count > 0)
        {
            List<float> sortedRed = new List<float>(rearRedLuma);
            sortedRed.Sort();
            redCut = Mathf.Max(0.55f, sortedRed[Mathf.Clamp(Mathf.RoundToInt(sortedRed.Count * 0.85f), 0, sortedRed.Count - 1)]);
        }
        for (int i = 0; i < rearReds.Count; i++)
        {
            if (rearRedLuma[i] >= redCut)
            {
                tailPicked.Add(rearReds[i]);
                tailPlaces.Add(rearPlace[i]);
            }
        }
        List<int> tail = new List<int>();
        tail.AddRange(LampBlob(tailPicked, tailPlaces, -1));
        tail.AddRange(LampBlob(tailPicked, tailPlaces, 1));
        if (tail.Count < MinLampTriangles && !redPaint && dimRedness.Count > 0)
        {
            List<float> sortedDim = new List<float>(dimRedness);
            sortedDim.Sort();
            float dimCut = sortedDim[Mathf.Clamp(Mathf.RoundToInt(sortedDim.Count * 0.7f), 0, sortedDim.Count - 1)];
            List<int> dimPicked = new List<int>();
            List<Vector2> dimPlaces = new List<Vector2>();
            for (int i = 0; i < dimReds.Count; i++)
            {
                if (dimRedness[i] >= dimCut)
                {
                    dimPicked.Add(dimReds[i]);
                    dimPlaces.Add(dimPlace[i]);
                }
            }
            tail.Clear();
            tail.AddRange(LampBlob(dimPicked, dimPlaces, -1));
            tail.AddRange(LampBlob(dimPicked, dimPlaces, 1));
        }

        bool headPlain = head.Count < MinLampTriangles;
        bool tailPlain = tail.Count < MinLampTriangles;
        if (headPlain) head = frontCorners;
        if (tailPlain) tail = rearCorners;

        return new LampMeshes
        {
            HeadPlain = headPlain,
            TailPlain = tailPlain,
            Head = head.Count >= MinLampTriangles ? BuildOverlay("Head Lamps", vertices, uvs, normals, triangles, head, forward, lift) : null,
            Tail = tail.Count >= MinLampTriangles ? BuildOverlay("Tail Lamps", vertices, uvs, normals, triangles, tail, -forward, lift) : null
        };
    }

    private const int BlobGrid = 32;

    /// <summary>Largest compact cluster of lamp triangles on one side (-1 left, 1 right) of the face.</summary>
    private static List<int> LampBlob(List<int> triangles, List<Vector2> places, int sideSign)
    {
        Dictionary<int, List<int>> cells = new Dictionary<int, List<int>>();
        for (int i = 0; i < triangles.Count; i++)
        {
            if (Mathf.Sign(places[i].x) != sideSign)
            {
                continue;
            }
            int cx = Mathf.Clamp(Mathf.FloorToInt((places[i].x * 0.5f + 0.5f) * BlobGrid), 0, BlobGrid - 1);
            int cy = Mathf.Clamp(Mathf.FloorToInt(places[i].y * BlobGrid), 0, BlobGrid - 1);
            int key = cy * BlobGrid + cx;
            if (!cells.TryGetValue(key, out List<int> list))
            {
                list = new List<int>();
                cells[key] = list;
            }
            list.Add(triangles[i]);
        }
        List<int> best = new List<int>();
        HashSet<int> seen = new HashSet<int>();
        foreach (int start in cells.Keys)
        {
            if (!seen.Add(start))
            {
                continue;
            }
            List<int> blob = new List<int>();
            int minX = BlobGrid, maxX = -1, minY = BlobGrid, maxY = -1;
            Queue<int> open = new Queue<int>();
            open.Enqueue(start);
            while (open.Count > 0)
            {
                int cell = open.Dequeue();
                blob.AddRange(cells[cell]);
                int x = cell % BlobGrid, y = cell / BlobGrid;
                minX = Mathf.Min(minX, x); maxX = Mathf.Max(maxX, x);
                minY = Mathf.Min(minY, y); maxY = Mathf.Max(maxY, y);
                for (int dy = -1; dy <= 1; dy++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int nx = x + dx, ny = y + dy;
                        int next = ny * BlobGrid + nx;
                        if (nx >= 0 && nx < BlobGrid && ny >= 0 && ny < BlobGrid &&
                            cells.ContainsKey(next) && seen.Add(next))
                        {
                            open.Enqueue(next);
                        }
                    }
                }
            }
            // A lamp is roughly as tall as wide; trim strips are long and thin.
            bool compact = maxY > minY && maxX - minX + 1 <= (maxY - minY + 1) * 2.5f + 1;
            if (compact && blob.Count > best.Count)
            {
                best = blob;
            }
        }
        return best;
    }

    private static Mesh BuildOverlay(string name, Vector3[] vertices, Vector2[] uvs, Vector3[] normals,
        int[] triangles, List<int> picked, Vector3 outward, float lift)
    {
        Dictionary<int, int> remap = new Dictionary<int, int>();
        List<Vector3> outVertices = new List<Vector3>();
        List<Vector2> outUvs = new List<Vector2>();
        List<int> outTriangles = new List<int>();
        List<Vector3> outNormals = new List<Vector3>();
        bool hasNormals = normals != null && normals.Length == vertices.Length;
        // Patch rim: vertices on an edge only one picked triangle uses fade to 0.
        Dictionary<long, int> edgeUse = new Dictionary<long, int>();
        foreach (int t in picked)
        {
            for (int k = 0; k < 3; k++)
            {
                long edge = EdgeKey(triangles[t + k], triangles[t + (k + 1) % 3]);
                edgeUse.TryGetValue(edge, out int used);
                edgeUse[edge] = used + 1;
            }
        }
        HashSet<int> rim = new HashSet<int>();
        foreach (KeyValuePair<long, int> edge in edgeUse)
        {
            if (edge.Value == 1)
            {
                rim.Add((int)(edge.Key >> 32));
                rim.Add((int)(edge.Key & 0xffffffff));
            }
        }
        List<Color> outColors = new List<Color>();
        foreach (int t in picked)
        {
            for (int k = 0; k < 3; k++)
            {
                int index = triangles[t + k];
                if (!remap.TryGetValue(index, out int mapped))
                {
                    mapped = outVertices.Count;
                    remap[index] = mapped;
                    // Lifted a hair off the body so the glow never z-fights it.
                    Vector3 push = hasNormals ? normals[index] : outward;
                    outVertices.Add(vertices[index] + push.normalized * lift);
                    outUvs.Add(uvs[index]);
                    outNormals.Add(push.normalized);
                    outColors.Add(new Color(1f, 1f, 1f, rim.Contains(index) ? 0f : 1f));
                }
                outTriangles.Add(mapped);
            }
        }
        Mesh mesh = new Mesh { name = name };
        if (outVertices.Count > 65000)
        {
            mesh.indexFormat = IndexFormat.UInt32;
        }
        mesh.SetVertices(outVertices);
        mesh.SetUVs(0, outUvs);
        mesh.SetNormals(outNormals);
        mesh.SetColors(outColors);
        mesh.SetTriangles(outTriangles, 0);
        mesh.RecalculateBounds();
        return mesh;
    }

    private static long EdgeKey(int a, int b)
    {
        int low = Mathf.Min(a, b);
        int high = Mathf.Max(a, b);
        return ((long)low << 32) | (uint)high;
    }

    private static Color32 Sample(Color32[] texels, int width, int height, Vector2 uv)
    {
        int x = Mathf.Clamp(Mathf.FloorToInt(Mathf.Repeat(uv.x, 1f) * width), 0, width - 1);
        int y = Mathf.Clamp(Mathf.FloorToInt(Mathf.Repeat(uv.y, 1f) * height), 0, height - 1);
        return texels[y * width + x];
    }

    /// <summary>CPU copy of a (usually unreadable) texture, at most 512 px.</summary>
    private static Color32[] ReadTexels(Texture texture, out int width, out int height)
    {
        width = Mathf.Min(512, texture.width);
        height = Mathf.Min(512, texture.height);
        if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
        {
            return null;
        }
        RenderTexture target = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        RenderTexture previous = RenderTexture.active;
        Texture2D copy = new Texture2D(width, height, TextureFormat.RGBA32, false);
        try
        {
            Graphics.Blit(texture, target);
            RenderTexture.active = target;
            copy.ReadPixels(new Rect(0, 0, width, height), 0, 0, false);
            copy.Apply(false);
            return copy.GetPixels32();
        }
        finally
        {
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(target);
            Destroy(copy);
        }
    }

    /// <summary>Additive lamp glow (optionally tinted by the lens texture).</summary>
    /// <param name="textureMode">0 flat colour, 1 tinted by the lens texture, 2 lens texture as detail.</param>
    private static Material GlowMaterial(Texture texture, Color color, float intensity, float textureMode = 1f)
    {
        if (texture == null) textureMode = 0f;
        var key = (texture, color, intensity + textureMode * 100f);
        if (glowMaterials.TryGetValue(key, out Material cached) && cached != null)
        {
            return cached;
        }
        if (glowShader == null)
        {
            glowShader = Resources.Load<Shader>("GymVehicleLampGlow") ?? Shader.Find("GymChaos/GymVehicleLampGlow");
        }
        Material material = new Material(glowShader) { name = "Vehicle Lamp Glow" };
        material.SetColor("_Color", color);
        material.SetFloat("_Intensity", intensity);
        material.SetFloat("_UseTexture", textureMode);
        if (texture != null)
        {
            material.SetTexture("_MainTex", texture);
        }
        glowMaterials[key] = material;
        return material;
    }

    /// <summary>Car body bounds in the driving frame, in metres (frame space without its scale).</summary>
    private bool TryMeasureBody(out Bounds body)
    {
        body = default;
        bool any = false;
        Matrix4x4 toFrame = Matrix4x4.TRS(transform.position, transform.rotation, Vector3.one).inverse;
        foreach (Renderer renderer in BodyRenderers())
        {
            Bounds world = renderer.bounds;
            if (world.size.sqrMagnitude < 1e-4f)
            {
                continue;
            }
            Vector3 min = world.min;
            Vector3 max = world.max;
            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 point = toFrame.MultiplyPoint3x4(new Vector3(
                    (corner & 1) == 0 ? min.x : max.x,
                    (corner & 2) == 0 ? min.y : max.y,
                    (corner & 4) == 0 ? min.z : max.z));
                if (!any)
                {
                    body = new Bounds(point, Vector3.zero);
                    any = true;
                }
                else
                {
                    body.Encapsulate(point);
                }
            }
        }
        // A car is at least a couple of metres long; anything smaller is a
        // placeholder before the model arrives.
        return any && body.size.z > 1.5f;
    }
}
