using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Cloth for the Oversized and Onyx tops: a skinned shell copied from the
/// player body's shirt area (Resources/Player/Outfits/*_shell, written by
/// Tools/build_player_tops.py). The copy is welded, Taubin-smoothed (hard for
/// Oversized so it hangs off the muscles, lightly for Onyx so it hides the
/// sculpted tank-top ridges), straightened along
/// its hem/cuffs/collar (carried slightly past the painted edge) and pushed
/// out along smoothed normals by the mask's inflation. It shares the body's bones, bind poses and UVs, so it follows
/// every animation and takes the same outfit texture.
/// Tools/render_player_tops.py runs the same steps for the Blender preview.
/// </summary>
public static class PlayerTopShell
{
    /// <summary>One garment cut: its mask and how much the copy is smoothed.</summary>
    public sealed class Style
    {
        public string Name;
        public string MaskResource;
        public int SmoothIterations;
        public float Lambda;
        public float EdgeOverhang;
        // Drape: implicit smoothing strength (edge-length units squared) and
        // the radius over which the cloth rests on the highest body points.
        public float DrapeSmoothing;
        public float DrapeRadius;
        internal Color32[] MaskPixels;
        internal int MaskWidth;
        internal int MaskHeight;
        internal bool MaskLoaded;
    }

    // Loose cloth: smoothed until no muscle or tank-top relief is left, so
    // it hangs off the chest, shoulders and back like a boxy tee.
    public static readonly Style Oversized = new Style
    {
        Name = ShellObjectName, MaskResource = "Player/Outfits/oversized_shell",
        SmoothIterations = 12, Lambda = 0.6f, EdgeOverhang = 0.009f,
        DrapeSmoothing = 260f, DrapeRadius = 0.05f
    };
    // Compression knit: a few mm off the skin, smoothed just enough to hide
    // the sculpted tank-top neckline and hem ridges in the scan.
    public static readonly Style Onyx = new Style
    {
        Name = OnyxObjectName, MaskResource = "Player/Outfits/onyx_shell",
        SmoothIterations = 24, Lambda = 0.5f, EdgeOverhang = 0.005f,
        // Light fairing: removes centimetre ridges (the tank top's sculpted
        // neckline) and keeps the larger muscle shapes.
        DrapeSmoothing = 30f
    };

    public const string ShellObjectName = "Oversized Top Shell";
    public const string OnyxObjectName = "Onyx Top Shell";
    // Mask G = 1 means this fraction of the rest mesh height (build_player_tops.py INFLATE_RANGE).
    private const float InflateRange = 0.04f;
    // Mean triangle edge of the height-1 rest mesh: converts the mask's
    // height fractions to this mesh's units whatever its import scale/pose.
    private const float RestMeanEdge = 0.0038112f;
    private const int NormalPasses = 6;
    private const int MinimumTriangles = 2000;
    // Rings of vertices next to an open edge that are relaxed into a strip.
    private const int EdgeRelaxRings = 4;
    // Rings over which the drape lift fades in from an open edge.
    private const int DrapeEdgeRings = 12;

    private static readonly Dictionary<(Mesh, Style), Mesh> shells = new Dictionary<(Mesh, Style), Mesh>();

    public static int CachedShellCount => shells.Count;

    public static bool IsShellObject(string name) => name == ShellObjectName || name == OnyxObjectName;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        shells.Clear();
        pending.Clear();
        foreach (Style style in new[] { Oversized, Onyx })
        {
            style.MaskPixels = null;
            style.MaskLoaded = false;
        }
    }

    /// <summary>Shell for the body mesh (cached), or null when the mesh has no shirt area.</summary>
    public static Mesh GetShell(Mesh body, Style style)
    {
        if (body == null) return null;
        if (shells.TryGetValue((body, style), out Mesh cached)) return cached;
        if (pending.TryGetValue((body, style), out System.Threading.Tasks.Task<ShellData> task))
        {
            if (!task.IsCompleted) return null;
            pending.Remove((body, style));
            ShellData data = null;
            if (task.IsFaulted) Debug.LogError("GYMCHAOS_TOP_SHELL_FAIL " + task.Exception?.GetBaseException().Message);
            else data = task.Result;
            Mesh built = null;
            try
            {
                built = Finish(data, body, style);
            }
            catch (System.Exception exception)
            {
                Debug.LogError("GYMCHAOS_TOP_SHELL_FAIL " + exception.Message);
            }
            shells[(body, style)] = built;
            return built;
        }
        if (!LoadMask(style) || !body.isReadable)
        {
            if (!body.isReadable) Debug.LogError("GYMCHAOS_TOP_SHELL_FAIL body mesh is not readable");
            shells[(body, style)] = null;
            return null;
        }
        Vector3[] vertices = body.vertices;
        Vector2[] uvs = body.uv;
        int[] triangles = body.triangles;
        BoneWeight[] weights = body.boneWeights;
#if UNITY_WEBGL && !UNITY_EDITOR
        // No worker threads on WebGL: build in place.
        Mesh direct = Finish(Compute(vertices, uvs, triangles, weights, style), body, style);
        shells[(body, style)] = direct;
        return direct;
#else
        pending[(body, style)] = System.Threading.Tasks.Task.Run(
            () => Compute(vertices, uvs, triangles, weights, style));
        return null;
#endif
    }

    /// <summary>True once the style's shell for this body is built (or known
    /// to be impossible); starts the build otherwise.</summary>
    public static bool IsSettled(Mesh body, Style style)
    {
        if (body == null) return true;
        GetShell(body, style);
        return shells.ContainsKey((body, style));
    }

    /// <summary>Starts building both shells in the background.</summary>
    public static void Prewarm(Mesh body)
    {
        GetShell(body, Oversized);
        GetShell(body, Onyx);
    }

    /// <summary>Adds (or refreshes) the style's shell renderer beside the body
    /// renderer and hides the other style's shell.</summary>
    public static SkinnedMeshRenderer Attach(SkinnedMeshRenderer body, Style style, Texture texture, Color bandColor)
    {
        if (body == null) return null;
        Detach(body, style == Oversized ? Onyx : Oversized);
        Mesh shell = GetShell(body.sharedMesh, style);
        if (shell == null) return null;
        Transform parent = body.transform.parent != null ? body.transform.parent : body.transform;
        Transform existing = parent.Find(style.Name);
        SkinnedMeshRenderer renderer = existing != null ? existing.GetComponent<SkinnedMeshRenderer>() : null;
        if (renderer == null)
        {
            GameObject shellObject = new GameObject(style.Name);
            shellObject.transform.SetParent(parent, false);
            shellObject.transform.SetLocalPositionAndRotation(body.transform.localPosition, body.transform.localRotation);
            shellObject.transform.localScale = body.transform.localScale;
            shellObject.layer = body.gameObject.layer;
            renderer = shellObject.AddComponent<SkinnedMeshRenderer>();
        }
        renderer.gameObject.SetActive(true);
        renderer.sharedMesh = shell;
        renderer.bones = body.bones;
        renderer.rootBone = body.rootBone;
        renderer.updateWhenOffscreen = body.updateWhenOffscreen;
        renderer.quality = body.quality;
        renderer.shadowCastingMode = body.shadowCastingMode;
        renderer.receiveShadows = body.receiveShadows;
        Bounds bounds = body.localBounds;
        bounds.Expand(bounds.size.magnitude * 0.05f);
        renderer.localBounds = bounds;

        Material source = body.sharedMaterial;
        Material[] materials = renderer.sharedMaterials;
        if (materials.Length != 2 || materials[0] == null || source == null || materials[0].shader != source.shader)
        {
            if (source == null) return renderer;
            materials = new[] { new Material(source), new Material(source) };
            materials[0].name = style.Name;
            materials[1].name = style.Name + " Band";
            // Edge strips can fold over on the scan's zig-zag cut; drawn from
            // both sides a fold reads as cloth, not as a hole.
            if (materials[1].HasProperty("_Cull")) materials[1].SetFloat("_Cull", (float)CullMode.Off);
            renderer.sharedMaterials = materials;
        }
        if (texture != null)
        {
            if (materials[0].HasProperty("_BaseMap")) materials[0].SetTexture("_BaseMap", texture);
            if (materials[0].HasProperty("_MainTex")) materials[0].SetTexture("_MainTex", texture);
        }
        if (materials[1].HasProperty("_BaseMap")) materials[1].SetTexture("_BaseMap", null);
        if (materials[1].HasProperty("_MainTex")) materials[1].SetTexture("_MainTex", null);
        if (materials[1].HasProperty("_BaseColor")) materials[1].SetColor("_BaseColor", bandColor);
        materials[1].color = bandColor;
        return renderer;
    }

    /// <summary>Hides a shell (both styles when none is given).</summary>
    public static void Detach(SkinnedMeshRenderer body, Style style = null)
    {
        if (body == null) return;
        Transform parent = body.transform.parent != null ? body.transform.parent : body.transform;
        foreach (Style each in new[] { Oversized, Onyx })
        {
            if (style != null && each != style) continue;
            Transform existing = parent.Find(each.Name);
            if (existing != null) existing.gameObject.SetActive(false);
        }
    }

    private static bool LoadMask(Style style)
    {
        if (style.MaskLoaded) return style.MaskPixels != null;
        style.MaskLoaded = true;
        TextAsset asset = Resources.Load<TextAsset>(style.MaskResource);
        if (asset == null)
        {
            Debug.LogError("GYMCHAOS_TOP_SHELL_FAIL mask missing at Resources/" + style.MaskResource);
            return false;
        }
        Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
        if (!texture.LoadImage(asset.bytes, false))
        {
            Object.Destroy(texture);
            return false;
        }
        style.MaskPixels = texture.GetPixels32();
        style.MaskWidth = texture.width;
        style.MaskHeight = texture.height;
        Object.Destroy(texture);
        Resources.UnloadAsset(asset);
        return true;
    }

    private static Color32 Sample(Style style, Vector2 uv)
    {
        int x = Mathf.Clamp(Mathf.RoundToInt(uv.x * (style.MaskWidth - 1)), 0, style.MaskWidth - 1);
        int y = Mathf.Clamp(Mathf.RoundToInt(uv.y * (style.MaskHeight - 1)), 0, style.MaskHeight - 1);
        return style.MaskPixels[y * style.MaskWidth + x];
    }

    private sealed class ShellData
    {
        public Vector3[] Vertices;
        public Vector3[] Normals;
        public Vector2[] Uv;
        public BoneWeight[] Weights;
        public List<int> Fabric;
        public List<int> Band;
        public string Log;
    }

    /// <summary>
    /// Copies the body's arrays on the main thread and runs the smoothing on
    /// a worker thread (0.1-0.6 s): equipping a top never freezes a frame.
    /// The shell renderer appears once the mesh is ready.
    /// </summary>
    private static readonly Dictionary<(Mesh, Style), System.Threading.Tasks.Task<ShellData>> pending =
        new Dictionary<(Mesh, Style), System.Threading.Tasks.Task<ShellData>>();

    private static Mesh Finish(ShellData data, Mesh body, Style style)
    {
        if (data == null) return null;
        Mesh shell = new Mesh { name = body.name + " " + style.Name };
        shell.indexFormat = data.Vertices.Length > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
        shell.vertices = data.Vertices;
        shell.normals = data.Normals;
        shell.uv = data.Uv;
        shell.subMeshCount = 2;
        shell.SetTriangles(data.Fabric, 0);
        shell.SetTriangles(data.Band, 1);
        if (data.Weights != null) shell.boneWeights = data.Weights;
        shell.bindposes = body.bindposes;
        shell.RecalculateTangents();
        shell.RecalculateBounds();
        shell.UploadMeshData(false);
        Debug.Log($"GYMCHAOS_TOP_SHELL_BUILT style={style.Name} source={body.name} {data.Log}");
        return shell;
    }

    private static ShellData Compute(Vector3[] vertices, Vector2[] uvs, int[] triangles,
        BoneWeight[] weights, Style style)
    {
        System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
        string laps = string.Empty;
        if (uvs == null || uvs.Length != vertices.Length) return null;

        // Mesh scale from the mean edge length.
        double edgeSum = 0d;
        for (int t = 0; t < triangles.Length; t += 3)
        {
            edgeSum += (vertices[triangles[t]] - vertices[triangles[t + 1]]).magnitude +
                (vertices[triangles[t + 1]] - vertices[triangles[t + 2]]).magnitude +
                (vertices[triangles[t + 2]] - vertices[triangles[t]]).magnitude;
        }
        float scale = (float)(edgeSum / triangles.Length) / RestMeanEdge;

        // Mask values are read at triangle centres and averaged per vertex:
        // a vertex UV on an island border can land on a texel grown from a
        // neighbouring island (another body part), the centre never does.
        int vertexCount = vertices.Length;
        Vector4[] maskSum = new Vector4[vertexCount];
        int[] maskCount = new int[vertexCount];
        for (int t = 0; t < triangles.Length; t += 3)
        {
            int a = triangles[t], b = triangles[t + 1], c = triangles[t + 2];
            Color32 m = Sample(style, (uvs[a] + uvs[b] + uvs[c]) / 3f);
            Vector4 v = new Vector4(m.r, m.g, m.b, m.a);
            maskSum[a] += v; maskSum[b] += v; maskSum[c] += v;
            maskCount[a]++; maskCount[b]++; maskCount[c]++;
        }
        bool[] inMask = new bool[vertexCount];
        bool[] painted = new bool[vertexCount];
        float[] inflate = new float[vertexCount];
        float[] edgeDistance = new float[vertexCount];
        for (int i = 0; i < vertexCount; i++)
        {
            if (maskCount[i] == 0)
            {
                edgeDistance[i] = -EdgeRange;
                continue;
            }
            Vector4 m = maskSum[i] / maskCount[i];
            inMask[i] = m.x >= 128f;
            painted[i] = m.z >= 192f;
            // In rest-height units: the smoothing below runs on a height-1
            // copy (Unity's normalize() zeroes vectors under 1e-5, and the
            // imported scan is only ~0.01 units tall).
            inflate[i] = m.y / 255f * InflateRange;
            edgeDistance[i] = (m.w - 128f) / 127f * EdgeRange;
        }

        ClipAtEdges(edgeDistance, ref vertices, ref uvs, ref weights, ref triangles,
            ref inMask, ref painted, ref inflate, scale);

        // Keep triangles whose three corners are in the mask; remap vertices.
        List<int> kept = new List<int>(triangles.Length / 2);
        int[] remap = new int[vertices.Length];
        for (int i = 0; i < remap.Length; i++) remap[i] = -1;
        List<int> sourceIndex = new List<int>();
        for (int t = 0; t < triangles.Length; t += 3)
        {
            int a = triangles[t], b = triangles[t + 1], c = triangles[t + 2];
            if (!inMask[a] || !inMask[b] || !inMask[c]) continue;
            for (int k = 0; k < 3; k++)
            {
                int v = triangles[t + k];
                if (remap[v] < 0)
                {
                    remap[v] = sourceIndex.Count;
                    sourceIndex.Add(v);
                }
                kept.Add(remap[v]);
            }
        }
        if (kept.Count / 3 < MinimumTriangles) return null;

        laps += "weld=" + watch.ElapsedMilliseconds + " ";
        // Weld by position (the scan is split along its UV seams).
        int count = sourceIndex.Count;
        int[] weld = new int[count];
        Dictionary<Vector3Int, int> keyIndex = new Dictionary<Vector3Int, int>(count);
        float quantum = 1e-5f;
        float toRest = 1f / Mathf.Max(scale, 1e-9f);
        List<Vector3> welded = new List<Vector3>(count);
        List<float> weldedInflate = new List<float>(count);
        for (int i = 0; i < count; i++)
        {
            Vector3 p = vertices[sourceIndex[i]] * toRest;
            Vector3Int key = new Vector3Int(Mathf.RoundToInt(p.x / quantum),
                Mathf.RoundToInt(p.y / quantum), Mathf.RoundToInt(p.z / quantum));
            if (!keyIndex.TryGetValue(key, out int w))
            {
                w = welded.Count;
                keyIndex.Add(key, w);
                welded.Add(p);
                weldedInflate.Add(0f);
            }
            weld[i] = w;
            weldedInflate[w] = Mathf.Max(weldedInflate[w], inflate[sourceIndex[i]]);
        }
        int n = welded.Count;
        Dictionary<long, int> edgeUse = new Dictionary<long, int>(kept.Count, EdgeKeyComparer.Instance);
        long EdgeKey(int a, int b) => a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
        void CountEdges()
        {
            edgeUse.Clear();
            for (int t = 0; t < kept.Count; t += 3)
            {
                for (int k = 0; k < 3; k++)
                {
                    long key = EdgeKey(weld[kept[t + k]], weld[kept[t + (k + 1) % 3]]);
                    edgeUse.TryGetValue(key, out int uses);
                    edgeUse[key] = uses + 1;
                }
            }
        }
        laps += "prune=" + watch.ElapsedMilliseconds + " ";
        // Keep the garment itself: atlas neighbours of the shirt islands can
        // pull in stray triangles from elsewhere (feet, hands).
        {
            int[] parent = new int[n];
            for (int i = 0; i < n; i++) parent[i] = i;
            int Find(int a)
            {
                while (parent[a] != a) { parent[a] = parent[parent[a]]; a = parent[a]; }
                return a;
            }
            for (int t = 0; t < kept.Count; t += 3)
            {
                int a = Find(weld[kept[t]]);
                int b = Find(weld[kept[t + 1]]);
                int c = Find(weld[kept[t + 2]]);
                parent[b] = a;
                parent[Find(c)] = Find(a);
            }
            Dictionary<int, int> size = new Dictionary<int, int>();
            for (int t = 0; t < kept.Count; t += 3)
            {
                int root = Find(weld[kept[t]]);
                size.TryGetValue(root, out int value);
                size[root] = value + 1;
            }
            int largest = 0;
            foreach (int value in size.Values) largest = Mathf.Max(largest, value);
            List<int> filtered = new List<int>(kept.Count);
            for (int t = 0; t < kept.Count; t += 3)
            {
                if (size[Find(weld[kept[t]])] * 20 < largest) continue;
                filtered.Add(kept[t]); filtered.Add(kept[t + 1]); filtered.Add(kept[t + 2]);
            }
            kept = filtered;
        }

        // Drop teeth: edge triangles hanging on by one edge (2+ open edges)
        // make a ragged hem.
        // (Edges are cut smoothly by ClipAtEdges now; pruning corner
        // triangles there would only notch the cut.)
        for (int pass = 0; pass < 0; pass++)
        {
            CountEdges();
            List<int> pruned = new List<int>(kept.Count);
            for (int t = 0; t < kept.Count; t += 3)
            {
                int open = 0;
                for (int k = 0; k < 3; k++)
                {
                    if (edgeUse[EdgeKey(weld[kept[t + k]], weld[kept[t + (k + 1) % 3]])] == 1) open++;
                }
                if (open >= 2) continue;
                pruned.Add(kept[t]); pruned.Add(kept[t + 1]); pruned.Add(kept[t + 2]);
            }
            bool done = pruned.Count == kept.Count;
            kept = pruned;
            if (done) break;
        }
        CountEdges();
        int triCount = kept.Count / 3;
        int[] wt = new int[kept.Count];
        for (int i = 0; i < kept.Count; i++) wt[i] = weld[kept[i]];

        // Boundary = edges used by one triangle.
        List<int>[] neighbours = new List<int>[n];
        List<int>[] boundaryNeighbours = new List<int>[n];
        for (int i = 0; i < n; i++) neighbours[i] = new List<int>(6);
        bool[] boundary = new bool[n];
        foreach (KeyValuePair<long, int> pair in edgeUse)
        {
            int a = (int)(pair.Key >> 32), b = (int)(pair.Key & 0xffffffff);
            neighbours[a].Add(b);
            neighbours[b].Add(a);
            if (pair.Value == 1)
            {
                boundary[a] = boundary[b] = true;
                (boundaryNeighbours[a] ??= new List<int>(2)).Add(b);
                (boundaryNeighbours[b] ??= new List<int>(2)).Add(a);
            }
        }

        laps += "adjacency=" + watch.ElapsedMilliseconds + " ";
        Vector3[] pos = welded.ToArray();
        Vector3[] original = welded.ToArray();
        Vector3[] delta = new Vector3[n];
        // Taubin pass band: mu just past -lambda keeps the volume.
        float mu = -(style.Lambda + 0.03f);
        for (int iteration = 0; iteration < style.SmoothIterations; iteration++)
        {
            Smooth(pos, delta, neighbours, boundaryNeighbours, boundary, style.Lambda);
            Smooth(pos, delta, neighbours, boundaryNeighbours, boundary, mu);
        }

        laps += "taubin=" + watch.ElapsedMilliseconds + " ";
        if (style.DrapeSmoothing > 0f)
        {
            ImplicitSmooth(pos, neighbours, style.DrapeSmoothing);
        }
        laps += "implicit=" + watch.ElapsedMilliseconds + " ";
        Vector3[] normals = SmoothedNormals(pos, wt, triCount, neighbours, delta);
        if (style.DrapeRadius > 0f)
        {
            Drape(pos, original, normals, neighbours, wt, triCount, delta, style.DrapeRadius, boundary);
            normals = SmoothedNormals(pos, wt, triCount, neighbours, delta);
        }
        laps += "drape=" + watch.ElapsedMilliseconds + " ";
        // Open edges reach a little past the painted area, so the body's
        // jagged colour edge stays hidden under the hem, cuffs and collar.
        // The outward direction is averaged along the edge loop first: per
        // vertex it is noisy on the scan's jagged cut and made spikes.
        Vector3[] outward = new Vector3[n];
        for (int i = 0; i < n; i++)
        {
            if (!boundary[i]) continue;
            Vector3 centre = Vector3.zero;
            int interior = 0;
            List<int> list = neighbours[i];
            for (int k = 0; k < list.Count; k++)
            {
                if (boundary[list[k]]) continue;
                centre += pos[list[k]];
                interior++;
            }
            if (interior == 0) continue;
            Vector3 direction = pos[i] - centre / interior;
            direction -= normals[i] * Vector3.Dot(direction, normals[i]);
            outward[i] = direction.normalized;
        }
        for (int pass = 0; pass < 12; pass++)
        {
            for (int i = 0; i < n; i++)
            {
                List<int> list = boundary[i] ? boundaryNeighbours[i] : null;
                if (list == null) { delta[i] = outward[i]; continue; }
                Vector3 sum = outward[i];
                for (int k = 0; k < list.Count; k++) sum += outward[list[k]];
                delta[i] = sum;
            }
            for (int i = 0; i < n; i++) outward[i] = delta[i].normalized;
        }
        for (int i = 0; i < n; i++)
        {
            if (boundary[i]) pos[i] += outward[i] * style.EdgeOverhang;
        }
        // The drape and the overhang move neighbouring edge vertices by
        // different amounts: straighten the edges along their own loops.
        for (int iteration = 0; iteration < 40; iteration++)
        {
            SmoothBoundary(pos, delta, boundaryNeighbours, boundary, 0.5f);
            SmoothBoundary(pos, delta, boundaryNeighbours, boundary, -0.53f);
        }
        // The scan's zig-zag cut leaves folded slivers just inside the edge:
        // relax the first few rings (edge held) into an even strip.
        int[] ring = new int[n];
        List<int> frontier = new List<int>();
        for (int i = 0; i < n; i++)
        {
            ring[i] = boundary[i] ? 0 : int.MaxValue;
            if (boundary[i]) frontier.Add(i);
        }
        for (int depth = 1; depth <= EdgeRelaxRings && frontier.Count > 0; depth++)
        {
            List<int> next = new List<int>();
            for (int f = 0; f < frontier.Count; f++)
            {
                List<int> list = neighbours[frontier[f]];
                for (int k = 0; k < list.Count; k++)
                {
                    if (ring[list[k]] != int.MaxValue) continue;
                    ring[list[k]] = depth;
                    next.Add(list[k]);
                }
            }
            frontier = next;
        }
        for (int iteration = 0; iteration < 30; iteration++)
        {
            for (int i = 0; i < n; i++)
            {
                delta[i] = Vector3.zero;
                if (ring[i] == 0 || ring[i] > EdgeRelaxRings) continue;
                List<int> list = neighbours[i];
                if (list.Count == 0) continue;
                Vector3 sum = Vector3.zero;
                for (int k = 0; k < list.Count; k++) sum += pos[list[k]];
                delta[i] = sum / list.Count - pos[i];
            }
            for (int i = 0; i < n; i++) pos[i] += delta[i] * 0.5f;
        }

        double inflateSum = 0d, moveSum = 0d;
        for (int i = 0; i < n; i++)
        {
            float amount = weldedInflate[i];
            inflateSum += amount;
            Vector3 p = pos[i] + normals[i] * amount;
            float gap = Vector3.Dot(p - original[i], normals[i]);
            if (gap < amount * 0.5f) p += normals[i] * (amount * 0.5f - gap);
            pos[i] = p;
            moveSum += (p - original[i]).magnitude;
        }

        // Folds: where drape lifts neighbours by very different amounts a
        // triangle can flip and read as a grey hole. Relax the corners of
        // flipped triangles until none remain (a few passes).
        int folded = 0;
        for (int pass = 0; pass < 6; pass++)
        {
            bool[] fold = new bool[n];
            int count0 = 0;
            for (int t = 0; t < triCount; t++)
            {
                int a = wt[t * 3], b = wt[t * 3 + 1], c = wt[t * 3 + 2];
                Vector3 face = Vector3.Cross(pos[b] - pos[a], pos[c] - pos[a]);
                if (Vector3.Dot(face, normals[a] + normals[b] + normals[c]) >= 0f) continue;
                fold[a] = fold[b] = fold[c] = true;
                count0++;
            }
            if (pass == 0) folded = count0;
            if (count0 == 0) break;
            // Grow the region one ring so the fix blends in.
            bool[] region = (bool[])fold.Clone();
            for (int i = 0; i < n; i++)
            {
                if (!fold[i]) continue;
                List<int> list = neighbours[i];
                for (int k = 0; k < list.Count; k++) region[list[k]] = true;
            }
            for (int iteration = 0; iteration < 8; iteration++)
            {
                for (int i = 0; i < n; i++)
                {
                    delta[i] = Vector3.zero;
                    if (!region[i] || boundary[i]) continue;
                    List<int> list = neighbours[i];
                    if (list.Count == 0) continue;
                    Vector3 sum = Vector3.zero;
                    for (int k = 0; k < list.Count; k++) sum += pos[list[k]];
                    delta[i] = sum / list.Count - pos[i];
                }
                for (int i = 0; i < n; i++) pos[i] += delta[i] * 0.6f;
            }
        }
        laps += "folds=" + folded + " ";

        // Compact: pruned teeth and stray parts leave unreferenced vertices.
        {
            int[] compact = new int[count];
            for (int i = 0; i < count; i++) compact[i] = -1;
            List<int> usedSource = new List<int>(count);
            List<int> usedWeld = new List<int>(count);
            for (int k = 0; k < kept.Count; k++)
            {
                int v = kept[k];
                if (compact[v] < 0)
                {
                    compact[v] = usedSource.Count;
                    usedSource.Add(sourceIndex[v]);
                    usedWeld.Add(weld[v]);
                }
                kept[k] = compact[v];
            }
            sourceIndex = usedSource;
            weld = usedWeld.ToArray();
            count = usedSource.Count;
        }

        Vector3[] outVertices = new Vector3[count];
        Vector3[] outNormals = new Vector3[count];
        Vector2[] outUv = new Vector2[count];
        BoneWeight[] outWeights = weights != null && weights.Length == vertices.Length ? new BoneWeight[count] : null;
        for (int i = 0; i < count; i++)
        {
            int source = sourceIndex[i];
            outVertices[i] = pos[weld[i]] * scale;
            outNormals[i] = normals[weld[i]];
            outUv[i] = uvs[source];
            if (outWeights != null) outWeights[i] = weights[source];
        }

        laps += "finish=" + watch.ElapsedMilliseconds + " ";
        // Triangles touching an unpainted texel form the hem/cuff/collar
        // band: submesh 1, drawn in the plain shirt colour (the atlas around
        // them is not painted, see build_player_tops.py PAINT_MARGIN).
        List<int> fabric = new List<int>(kept.Count);
        List<int> band = new List<int>(kept.Count / 8);
        for (int t = 0; t < kept.Count; t += 3)
        {
            bool edge = !painted[sourceIndex[kept[t]]] || !painted[sourceIndex[kept[t + 1]]] ||
                !painted[sourceIndex[kept[t + 2]]];
            List<int> target = edge ? band : fabric;
            target.Add(kept[t]); target.Add(kept[t + 1]); target.Add(kept[t + 2]);
        }
        return new ShellData
        {
            Vertices = outVertices, Normals = outNormals, Uv = outUv, Weights = outWeights,
            Fabric = fabric, Band = band,
            Log = $"vertices={count} triangles={triCount} welded={n} scale={scale:F4} " +
                $"meanInflate={inflateSum / n:F5} meanMove={moveSum / n:F5} ms={watch.ElapsedMilliseconds} {laps}"
        };
    }

    // Mask A: signed distance to the garment's open edges (build_player_tops.py EDGE_RANGE).
    private const float EdgeRange = 0.03f;

    /// <summary>
    /// Cuts the candidate triangles along the zero line of the mask's edge
    /// distance, so hem, cuffs and collar are smooth curves instead of the
    /// scan's zig-zag triangle borders. Split points are shared per mesh
    /// edge, and the distance is averaged per position so copies of a
    /// vertex on both sides of a UV seam cut at the same point.
    /// </summary>
    private static void ClipAtEdges(float[] sampledEdge, ref Vector3[] vertices, ref Vector2[] uvs,
        ref BoneWeight[] weights, ref int[] triangles, ref bool[] inMask, ref bool[] painted,
        ref float[] inflate, float scale)
    {
        // Locals: ref parameters cannot be captured by the Split function.
        Vector3[] sourceVertices = vertices;
        Vector2[] sourceUvs = uvs;
        BoneWeight[] sourceWeights = weights;
        float[] sourceInflate = inflate;
        bool[] sourcePainted = painted;
        int count = sourceVertices.Length;
        float[] edge = new float[count];
        Dictionary<Vector3Int, Vector2> byPosition = new Dictionary<Vector3Int, Vector2>(count);
        Vector3Int[] keys = new Vector3Int[count];
        float quantum = 1e-5f * scale;
        for (int i = 0; i < count; i++)
        {
            edge[i] = sampledEdge[i];
            Vector3 p = sourceVertices[i];
            keys[i] = new Vector3Int(Mathf.RoundToInt(p.x / quantum), Mathf.RoundToInt(p.y / quantum),
                Mathf.RoundToInt(p.z / quantum));
            byPosition.TryGetValue(keys[i], out Vector2 acc);
            byPosition[keys[i]] = new Vector2(acc.x + edge[i], acc.y + 1f);
        }
        for (int i = 0; i < count; i++)
        {
            Vector2 acc = byPosition[keys[i]];
            edge[i] = acc.x / acc.y;
        }

        bool hasWeights = sourceWeights != null && sourceWeights.Length == count;
        List<Vector3> outVertices = new List<Vector3>(sourceVertices);
        List<Vector2> outUv = new List<Vector2>(sourceUvs);
        List<BoneWeight> outWeights = hasWeights ? new List<BoneWeight>(sourceWeights) : null;
        List<bool> outPainted = new List<bool>(painted);
        List<float> outInflate = new List<float>(sourceInflate);
        List<int> outTriangles = new List<int>(triangles.Length / 3);
        Dictionary<long, int> splits = new Dictionary<long, int>(EdgeKeyComparer.Instance);
        int Split(int inside, int outside)
        {
            long key = ((long)inside << 32) | (uint)outside;
            if (splits.TryGetValue(key, out int index)) return index;
            float t = edge[inside] / (edge[inside] - edge[outside]);
            index = outVertices.Count;
            outVertices.Add(Vector3.Lerp(sourceVertices[inside], sourceVertices[outside], t));
            outUv.Add(Vector2.Lerp(sourceUvs[inside], sourceUvs[outside], t));
            if (hasWeights) outWeights.Add(sourceWeights[inside]);
            outPainted.Add(sourcePainted[inside]);
            outInflate.Add(Mathf.Lerp(sourceInflate[inside], sourceInflate[outside], t));
            splits[key] = index;
            return index;
        }

        int[] polygon = new int[4];
        for (int t = 0; t < triangles.Length; t += 3)
        {
            int a = triangles[t], b = triangles[t + 1], c = triangles[t + 2];
            if (!inMask[a] && !inMask[b] && !inMask[c]) continue;
            bool ia = edge[a] >= 0f, ib = edge[b] >= 0f, ic = edge[c] >= 0f;
            if (!ia && !ib && !ic) continue;
            if (ia && ib && ic)
            {
                outTriangles.Add(a); outTriangles.Add(b); outTriangles.Add(c);
                continue;
            }
            // Sutherland-Hodgman against edge >= 0, keeping the winding.
            int[] corners = { a, b, c };
            int size = 0;
            for (int k = 0; k < 3; k++)
            {
                int current = corners[k];
                int next = corners[(k + 1) % 3];
                bool currentIn = edge[current] >= 0f;
                bool nextIn = edge[next] >= 0f;
                if (currentIn) polygon[size++] = current;
                if (currentIn != nextIn)
                {
                    polygon[size++] = currentIn ? Split(current, next) : Split(next, current);
                }
            }
            for (int k = 1; k + 1 < size; k++)
            {
                outTriangles.Add(polygon[0]); outTriangles.Add(polygon[k]); outTriangles.Add(polygon[k + 1]);
            }
        }

        vertices = outVertices.ToArray();
        uvs = outUv.ToArray();
        if (hasWeights) weights = outWeights.ToArray();
        painted = outPainted.ToArray();
        inflate = outInflate.ToArray();
        triangles = outTriangles.ToArray();
        inMask = new bool[vertices.Length];
        for (int i = 0; i < inMask.Length; i++) inMask[i] = true;
    }

    /// <summary>long.GetHashCode is lo ^ hi: edges between nearby vertex
    /// indices all hash near zero. Mix the key instead.</summary>
    private sealed class EdgeKeyComparer : IEqualityComparer<long>
    {
        public static readonly EdgeKeyComparer Instance = new EdgeKeyComparer();
        public bool Equals(long a, long b) => a == b;
        public int GetHashCode(long key) => (int)(((ulong)key * 0x9E3779B97F4A7C15UL) >> 32);
    }

    private static Vector3[] SmoothedNormals(Vector3[] pos, int[] wt, int triCount,
        List<int>[] neighbours, Vector3[] scratch)
    {
        int n = pos.Length;
        Vector3[] normals = new Vector3[n];
        for (int t = 0; t < triCount; t++)
        {
            int a = wt[t * 3], b = wt[t * 3 + 1], c = wt[t * 3 + 2];
            Vector3 face = Vector3.Cross(pos[b] - pos[a], pos[c] - pos[a]);
            normals[a] += face; normals[b] += face; normals[c] += face;
        }
        for (int i = 0; i < n; i++) normals[i] = normals[i].normalized;
        for (int pass = 0; pass < NormalPasses; pass++)
        {
            for (int i = 0; i < n; i++)
            {
                Vector3 sum = Vector3.zero;
                List<int> list = neighbours[i];
                for (int k = 0; k < list.Count; k++) sum += normals[list[k]];
                scratch[i] = list.Count > 0 ? sum / list.Count : normals[i];
            }
            for (int i = 0; i < n; i++) normals[i] = scratch[i].normalized;
        }
        return normals;
    }

    /// <summary>
    /// Implicit fairing: solves (D + t (D - A)) x = D x0 by conjugate
    /// gradients, which removes muscle-sized relief in a few dozen steps
    /// where explicit smoothing would need thousands on this dense scan.
    /// </summary>
    private static void ImplicitSmooth(Vector3[] pos, List<int>[] neighbours, float t)
    {
        int n = pos.Length;
        float[] degree = new float[n];
        for (int i = 0; i < n; i++) degree[i] = Mathf.Max(1, neighbours[i].Count);
        Vector3[] b = new Vector3[n];
        for (int i = 0; i < n; i++) b[i] = pos[i] * degree[i];
        Vector3[] x = (Vector3[])pos.Clone();
        Vector3[] r = new Vector3[n];
        Vector3[] d = new Vector3[n];
        Vector3[] q = new Vector3[n];
        void Apply(Vector3[] v, Vector3[] result)
        {
            for (int i = 0; i < n; i++)
            {
                Vector3 sum = Vector3.zero;
                List<int> list = neighbours[i];
                for (int k = 0; k < list.Count; k++) sum += v[list[k]];
                result[i] = v[i] * (degree[i] * (1f + t)) - sum * t;
            }
        }
        Apply(x, q);
        for (int i = 0; i < n; i++) { r[i] = b[i] - q[i]; d[i] = r[i]; }
        // Three independent systems (x, y, z) solved together per component.
        Vector3 rr = Dot(r, r);
        for (int iteration = 0; iteration < 90; iteration++)
        {
            Apply(d, q);
            Vector3 dq = Dot(d, q);
            Vector3 alpha = new Vector3(Div(rr.x, dq.x), Div(rr.y, dq.y), Div(rr.z, dq.z));
            for (int i = 0; i < n; i++)
            {
                x[i] += Vector3.Scale(alpha, d[i]);
                r[i] -= Vector3.Scale(alpha, q[i]);
            }
            Vector3 next = Dot(r, r);
            Vector3 beta = new Vector3(Div(next.x, rr.x), Div(next.y, rr.y), Div(next.z, rr.z));
            for (int i = 0; i < n; i++) d[i] = r[i] + Vector3.Scale(beta, d[i]);
            rr = next;
        }
        for (int i = 0; i < n; i++) pos[i] = x[i];
    }

    private static Vector3 Dot(Vector3[] a, Vector3[] b)
    {
        double x = 0d, y = 0d, z = 0d;
        for (int i = 0; i < a.Length; i++)
        {
            x += a[i].x * b[i].x; y += a[i].y * b[i].y; z += a[i].z * b[i].z;
        }
        return new Vector3((float)x, (float)y, (float)z);
    }

    private static float Div(float a, float b) => Mathf.Abs(b) > 1e-30f ? a / b : 0f;

    /// <summary>
    /// Moves the smooth surface out (or in) along its normal until it rests
    /// on the highest body points within <paramref name="radius"/>: the
    /// cloth spans the hollows between muscles instead of tracing them.
    /// Only body points facing the same way count, so a sleeve never
    /// measures the other side of the arm.
    /// </summary>
    private static void Drape(Vector3[] pos, Vector3[] original, Vector3[] normals, List<int>[] neighbours,
        int[] wt, int triCount, Vector3[] scratch, float radius, bool[] boundary)
    {
        int n = pos.Length;
        Vector3[] bodyNormals = SmoothedNormals(original, wt, triCount, neighbours, scratch);
        float cell = radius;
        Dictionary<Vector3Int, List<int>> grid = new Dictionary<Vector3Int, List<int>>();
        // Every third body point is plenty at drape scale (5 cm vs 4 mm
        // spacing) and keeps the search fast.
        for (int i = 0; i < n; i += 3)
        {
            Vector3Int key = Vector3Int.FloorToInt(original[i] / cell);
            if (!grid.TryGetValue(key, out List<int> list)) grid[key] = list = new List<int>();
            list.Add(i);
        }
        float[] lift = new float[n];
        float lateralLimit = radius * radius;
        for (int i = 0; i < n; i++)
        {
            Vector3 normal = normals[i];
            Vector3Int centre = Vector3Int.FloorToInt(original[i] / cell);
            float best = Vector3.Dot(original[i] - pos[i], normal);
            for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
            for (int dz = -1; dz <= 1; dz++)
            {
                if (!grid.TryGetValue(centre + new Vector3Int(dx, dy, dz), out List<int> list)) continue;
                for (int k = 0; k < list.Count; k++)
                {
                    int j = list[k];
                    if (Vector3.Dot(bodyNormals[j], normal) < 0.35f) continue;
                    Vector3 offset = original[j] - pos[i];
                    float along = Vector3.Dot(offset, normal);
                    if (along <= best) continue;
                    if ((offset - normal * along).sqrMagnitude > lateralLimit) continue;
                    best = along;
                }
            }
            lift[i] = Mathf.Clamp(best, -radius * 0.5f, radius);
        }
        // Soften the lift so the cloth bends gently between resting points.
        float[] next = new float[n];
        for (int pass = 0; pass < 6; pass++)
        {
            for (int i = 0; i < n; i++)
            {
                List<int> list = neighbours[i];
                float sum = lift[i];
                for (int k = 0; k < list.Count; k++) sum += lift[list[k]];
                next[i] = Mathf.Max(sum / (list.Count + 1), lift[i] * 0.8f);
            }
            float[] swap = lift; lift = next; next = swap;
        }
        // Near the open edges the cloth falls back onto the body instead of
        // resting on far-off high points: the collar otherwise stood up in
        // flaps over the trapezius.
        int[] ring = new int[n];
        List<int> frontier = new List<int>();
        for (int i = 0; i < n; i++)
        {
            ring[i] = boundary[i] ? 0 : DrapeEdgeRings;
            if (boundary[i]) frontier.Add(i);
        }
        for (int depth = 1; depth < DrapeEdgeRings && frontier.Count > 0; depth++)
        {
            List<int> nextFrontier = new List<int>();
            for (int f = 0; f < frontier.Count; f++)
            {
                List<int> list = neighbours[frontier[f]];
                for (int k = 0; k < list.Count; k++)
                {
                    if (ring[list[k]] <= depth) continue;
                    ring[list[k]] = depth;
                    nextFrontier.Add(list[k]);
                }
            }
            frontier = nextFrontier;
        }
        for (int i = 0; i < n; i++)
        {
            float amount = lift[i] > 0f ? lift[i] * ring[i] / (float)DrapeEdgeRings : lift[i];
            pos[i] += normals[i] * amount;
        }
    }

    private static void SmoothBoundary(Vector3[] pos, Vector3[] delta, List<int>[] boundaryNeighbours,
        bool[] boundary, float factor)
    {
        for (int i = 0; i < pos.Length; i++)
        {
            List<int> list = boundary[i] ? boundaryNeighbours[i] : null;
            if (list == null || list.Count == 0)
            {
                delta[i] = Vector3.zero;
                continue;
            }
            Vector3 sum = Vector3.zero;
            for (int k = 0; k < list.Count; k++) sum += pos[list[k]];
            delta[i] = sum / list.Count - pos[i];
        }
        for (int i = 0; i < pos.Length; i++) pos[i] += delta[i] * factor;
    }

    private static void Smooth(Vector3[] pos, Vector3[] delta, List<int>[] neighbours,
        List<int>[] boundaryNeighbours, bool[] boundary, float factor)
    {
        for (int i = 0; i < pos.Length; i++)
        {
            List<int> list = boundary[i] ? boundaryNeighbours[i] : neighbours[i];
            if (list == null || list.Count == 0)
            {
                delta[i] = Vector3.zero;
                continue;
            }
            Vector3 sum = Vector3.zero;
            for (int k = 0; k < list.Count; k++) sum += pos[list[k]];
            delta[i] = sum / list.Count - pos[i];
        }
        for (int i = 0; i < pos.Length; i++) pos[i] += delta[i] * factor;
    }
}
