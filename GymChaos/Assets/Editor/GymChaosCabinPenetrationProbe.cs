#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Tests a driver's baked skin against the car's outer skin (body shell,
/// glass and driver door as mesh colliders on a spare layer, enabled only
/// while querying). Seated: a segment from inside the cabin to each skin
/// vertex must not cross the shell, so no body part shows outside the car
/// or through the glass. Climbing in or out: a vertex moving between two
/// samples must not cross the shell; only the open door aperture and the
/// window holes have no faces, so passing through roof, pillars, sill or
/// the door itself is caught.
/// </summary>
public sealed class GymChaosCabinPenetrationProbe
{
    private const int ProbeLayer = 30;
    private const int VertexStride = 5;

    private readonly List<(MeshCollider collider, Transform source)> parts =
        new List<(MeshCollider, Transform)>();
    private readonly Transform vehicle;
    private readonly List<(Transform part, Bounds bounds)> hidden = new List<(Transform, Bounds)>();

    private bool IsHidden(Vector3 world)
    {
        foreach ((Transform part, Bounds bounds) in hidden)
            if (part != null && bounds.Contains(part.InverseTransformPoint(world))) return true;
        return false;
    }
    private readonly SkinnedMeshRenderer skin;
    private readonly Mesh scratch = new Mesh { name = "Cabin Probe Bake" };
    private Vector3[] previousLocal;

    public int SeatedOutside { get; private set; }
    public int SeatedSamples { get; private set; }
    public float SeatedDepth { get; private set; }
    public int Crossings { get; private set; }
    public int MotionSamples { get; private set; }
    public int WorstSampleCrossings { get; private set; }
    public int SampledVertices { get; private set; }
    public string WorstCrossing { get; private set; } = "";
    private readonly SortedDictionary<string, int> crossingBuckets = new SortedDictionary<string, int>();
    private readonly SortedDictionary<string, int> outsideBuckets = new SortedDictionary<string, int>();

    public string Histogram()
    {
        var parts = new List<string>();
        foreach (var pair in crossingBuckets) parts.Add($"{pair.Key}:{pair.Value}");
        var outside = new List<string>();
        foreach (var pair in outsideBuckets) outside.Add($"{pair.Key}:{pair.Value}");
        return $"crossings[{string.Join(",", parts)}] outside[{string.Join(",", outside)}]";
    }

    public GymChaosCabinPenetrationProbe(Transform vehicleRoot, EnemyFighter driver)
    {
        vehicle = vehicleRoot;
        foreach (SkinnedMeshRenderer candidate in driver.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (candidate.sharedMesh != null && (skin == null ||
                candidate.sharedMesh.vertexCount > skin.sharedMesh.vertexCount))
            {
                skin = candidate;
            }
        }
        foreach (MeshFilter filter in vehicleRoot.GetComponentsInChildren<MeshFilter>(true))
        {
            string name = GymVehicleCabin.PartName(filter.name);
            if (name.StartsWith("Cabin__Underbody", System.StringComparison.Ordinal))
            {
                // Feet in the dark footwell tub under the floor are hidden.
                Bounds b = filter.sharedMesh.bounds;
                hidden.Add((filter.transform, b));
                continue;
            }
            bool shell = name == "geometry_0" || name == "Door_Driver__Shell" ||
                name == "Door_Driver__Glass" || name == "Glass__Body";
            if (!shell || filter.sharedMesh == null) continue;
            GameObject copy = new GameObject("Cabin Probe " + name) { layer = ProbeLayer };
            MeshCollider collider = copy.AddComponent<MeshCollider>();
            collider.sharedMesh = filter.sharedMesh;
            collider.enabled = false;
            parts.Add((collider, filter.transform));
        }
    }

    public int ShellPartCount => parts.Count;

    private void Enable(bool on)
    {
        foreach ((MeshCollider collider, Transform source) in parts)
        {
            if (on)
            {
                collider.transform.SetPositionAndRotation(source.position, source.rotation);
                collider.transform.localScale = source.lossyScale;
            }
            collider.enabled = on;
        }
        Physics.SyncTransforms();
    }

    private Vector3[] Bake() => skin != null ? GymSkinSampler.BakeWorld(skin, scratch) : new Vector3[0];

    public void CheckSeated(Vector3 cabinInside)
    {
        Vector3[] vertices = Bake();
        Enable(true);
        int mask = 1 << ProbeLayer;
        for (int i = 0; i < vertices.Length; i += VertexStride)
        {
            SeatedSamples++;
            if (IsHidden(vertices[i])) continue;
            if (Physics.Linecast(cabinInside, vertices[i], out RaycastHit hit, mask,
                QueryTriggerInteraction.Collide))
            {
                SeatedOutside++;
                SeatedDepth = Mathf.Max(SeatedDepth, Vector3.Distance(hit.point, vertices[i]));
                Vector3 local = vehicle.InverseTransformPoint(vertices[i]);
                string key = $"{GymVehicleCabin.PartName(hit.collider.name).Replace("Cabin Probe ", "")}" +
                    $"@y{Mathf.Round(local.y * 5f) / 5f:F1}";
                outsideBuckets[key] = outsideBuckets.TryGetValue(key, out int n) ? n + 1 : 1;
            }
        }
        Enable(false);
    }

    public void ResetMotion() => previousLocal = null;

    public void TrackMotion(string phase)
    {
        Vector3[] vertices = Bake();
        if (vertices.Length == 0) return;
        if (previousLocal == null || previousLocal.Length != vertices.Length)
        {
            previousLocal = new Vector3[vertices.Length];
            for (int i = 0; i < vertices.Length; i += VertexStride)
                previousLocal[i] = vehicle.InverseTransformPoint(vertices[i]);
            return;
        }
        Enable(true);
        int mask = 1 << ProbeLayer;
        int before = Crossings;
        MotionSamples++;
        SampledVertices = (vertices.Length + VertexStride - 1) / VertexStride;
        for (int i = 0; i < vertices.Length; i += VertexStride)
        {
            Vector3 from = vehicle.TransformPoint(previousLocal[i]);
            if (Physics.Linecast(from, vertices[i], out RaycastHit hit, mask,
                QueryTriggerInteraction.Collide))
            {
                Crossings++;
                string bucket = phase.Length > 0 ? phase.Substring(0, Mathf.Min(phase.Length, phase.IndexOf('@') + 4)) : phase;
                bucket += "/" + hit.collider.name.Replace("Cabin Probe ", "");
                Vector3 hitLocal = vehicle.InverseTransformPoint(hit.point);
                bucket += $"@y{Mathf.Round(hitLocal.y * 5f) / 5f:F1}";
                crossingBuckets[bucket] = crossingBuckets.TryGetValue(bucket, out int c) ? c + 1 : 1;
                if (Crossings <= 1 || Random.value < 0.02f)
                {
                    WorstCrossing = $"{phase} part={hit.collider.name} " +
                        $"at={vehicle.InverseTransformPoint(hit.point)}";
                }
            }
            previousLocal[i] = vehicle.InverseTransformPoint(vertices[i]);
        }
        WorstSampleCrossings = Mathf.Max(WorstSampleCrossings, Crossings - before);
        Enable(false);
    }

    public void Dispose()
    {
        foreach ((MeshCollider collider, Transform _) in parts)
            if (collider != null) Object.Destroy(collider.gameObject);
        parts.Clear();
        Object.Destroy(scratch);
    }
}
#endif
