using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Cheap shadow casters for dense static scans (visitor cars are ~145k
/// triangles each, drawn again in both shadow cascades). The visible mesh
/// stops casting; a vertex-clustered copy (a few thousand triangles, same
/// silhouette at shadow-map resolution) casts in its place with
/// ShadowCastingMode.ShadowsOnly. Proxies are children of the source
/// renderer, so they follow moving and spinning parts (wheels, parked cars).
/// </summary>
public static class GymShadowProxy
{
    public const string ProxyName = "Shadow Proxy";
    // Meshes below this keep their own shadow.
    public const int MinimumTriangles = 12000;

    private static readonly Dictionary<(Mesh, int), Mesh> proxies = new Dictionary<(Mesh, int), Mesh>();
    private static Material shadowMaterial;
    public static int TotalProxyTriangles { get; private set; }
    public static int TotalSourceTriangles { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        proxies.Clear();
        shadowMaterial = null;
        TotalProxyTriangles = 0;
        TotalSourceTriangles = 0;
        DisabledSmallCasters = 0;
    }

    /// <summary>
    /// Replaces the shadow of every dense shadow-casting MeshRenderer under
    /// <paramref name="root"/>. <paramref name="cellsAcross"/> is the cluster
    /// grid resolution along the mesh's largest side.
    /// </summary>
    public static int Apply(GameObject root, int cellsAcross = 48)
    {
        if (root == null) return 0;
        int applied = 0;
        MeshRenderer[] renderers = root.GetComponentsInChildren<MeshRenderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            MeshRenderer renderer = renderers[i];
            if (renderer == null || renderer.name == ProxyName ||
                renderer.shadowCastingMode != ShadowCastingMode.On) continue;
            MeshFilter filter = renderer.GetComponent<MeshFilter>();
            Mesh mesh = filter != null ? filter.sharedMesh : null;
            if (mesh == null || !mesh.isReadable || TriangleCount(mesh) < MinimumTriangles) continue;
            Mesh proxy = GetProxy(mesh, cellsAcross);
            if (proxy == null) continue;

            GameObject proxyObject = new GameObject(ProxyName);
            proxyObject.layer = renderer.gameObject.layer;
            proxyObject.transform.SetParent(renderer.transform, false);
            proxyObject.AddComponent<MeshFilter>().sharedMesh = proxy;
            MeshRenderer proxyRenderer = proxyObject.AddComponent<MeshRenderer>();
            proxyRenderer.sharedMaterial = GetMaterial();
            proxyRenderer.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
            proxyRenderer.receiveShadows = false;
            proxyRenderer.lightProbeUsage = LightProbeUsage.Off;
            proxyRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            applied++;
        }
        return applied;
    }

    // Props smaller than this (plates, dumbbells, bottles, small parts) cast
    // shadows a few cm wide that the 2048 shadow map barely resolves, yet
    // each one is another caster in both cascades.
    public const float SmallCasterSize = 0.3f;
    public static int DisabledSmallCasters { get; private set; }

    /// <summary>Stops tiny static props from casting shadows (one pass at build).</summary>
    public static int DisableSmallCasters()
    {
        int disabled = 0;
        MeshRenderer[] renderers = Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None);
        for (int i = 0; i < renderers.Length; i++)
        {
            MeshRenderer renderer = renderers[i];
            if (renderer == null || renderer.shadowCastingMode != ShadowCastingMode.On) continue;
            Vector3 size = renderer.bounds.size;
            if (Mathf.Max(size.x, Mathf.Max(size.y, size.z)) >= SmallCasterSize) continue;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            disabled++;
        }
        DisabledSmallCasters += disabled;
        return disabled;
    }

    private static int TriangleCount(Mesh mesh)
    {
        long count = 0;
        for (int s = 0; s < mesh.subMeshCount; s++) count += mesh.GetIndexCount(s);
        return (int)(count / 3);
    }

    private static Material GetMaterial()
    {
        if (shadowMaterial != null) return shadowMaterial;
        Shader shader = GymMaterialLibrary.UnlitShader ??
            GymMaterialLibrary.LitShader;
        shadowMaterial = new Material(shader) { name = "Shadow Proxy", hideFlags = HideFlags.DontSave };
        return shadowMaterial;
    }

    public static Mesh GetProxy(Mesh source, int cellsAcross)
    {
        if (proxies.TryGetValue((source, cellsAcross), out Mesh cached)) return cached;
        Mesh proxy = Build(source, cellsAcross);
        proxies[(source, cellsAcross)] = proxy;
        return proxy;
    }

    /// <summary>Vertex clustering: vertices snap to one representative per
    /// grid cell (their average); triangles that collapse are dropped.</summary>
    private static Mesh Build(Mesh source, int cellsAcross)
    {
        Vector3[] vertices = source.vertices;
        Bounds bounds = source.bounds;
        float largest = Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z));
        if (largest <= 0f || vertices.Length == 0) return null;
        float cell = largest / Mathf.Max(4, cellsAcross);
        Vector3 origin = bounds.min;

        Dictionary<Vector3Int, int> clusterOf = new Dictionary<Vector3Int, int>(vertices.Length / 4);
        int[] vertexCluster = new int[vertices.Length];
        List<Vector3> sum = new List<Vector3>();
        List<int> count = new List<int>();
        for (int i = 0; i < vertices.Length; i++)
        {
            Vector3 p = vertices[i] - origin;
            Vector3Int key = new Vector3Int((int)(p.x / cell), (int)(p.y / cell), (int)(p.z / cell));
            if (!clusterOf.TryGetValue(key, out int c))
            {
                c = sum.Count;
                clusterOf.Add(key, c);
                sum.Add(Vector3.zero);
                count.Add(0);
            }
            vertexCluster[i] = c;
            sum[c] += vertices[i];
            count[c]++;
        }

        HashSet<(int, int, int)> seen = new HashSet<(int, int, int)>();
        List<int> triangles = new List<int>();
        for (int s = 0; s < source.subMeshCount; s++)
        {
            if (source.GetTopology(s) != MeshTopology.Triangles) continue;
            int[] indices = source.GetTriangles(s);
            for (int t = 0; t < indices.Length; t += 3)
            {
                int a = vertexCluster[indices[t]];
                int b = vertexCluster[indices[t + 1]];
                int c = vertexCluster[indices[t + 2]];
                if (a == b || b == c || a == c) continue;
                // One copy per cluster triangle, either winding (shadows
                // are rendered without back-face dependence here).
                int lo = Mathf.Min(a, Mathf.Min(b, c));
                int hi = Mathf.Max(a, Mathf.Max(b, c));
                int mid = a + b + c - lo - hi;
                if (!seen.Add((lo, mid, hi))) continue;
                triangles.Add(a); triangles.Add(b); triangles.Add(c);
            }
        }
        if (triangles.Count == 0) return null;

        Vector3[] positions = new Vector3[sum.Count];
        for (int i = 0; i < positions.Length; i++) positions[i] = sum[i] / Mathf.Max(1, count[i]);
        Mesh proxy = new Mesh { name = source.name + " Shadow Proxy" };
        proxy.indexFormat = positions.Length > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
        proxy.vertices = positions;
        proxy.SetTriangles(triangles, 0);
        proxy.RecalculateNormals();
        proxy.RecalculateBounds();
        proxy.UploadMeshData(true);
        TotalSourceTriangles += TriangleCount(source);
        TotalProxyTriangles += triangles.Count / 3;
        return proxy;
    }
}
