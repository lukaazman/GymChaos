using System.Collections.Generic;
using UnityEngine;

/// Scan atlases pack UV islands right next to islands of another colour (a
/// navy suit face beside the white shirt). Bilinear filtering at an island
/// border reads the neighbour texel and shows a light fleck on dark cloth.
/// This returns a copy of the mesh whose island-border UVs are pulled up to
/// one texel inward. Positions, normals, skin weights and bind poses are the
/// same; the texture shifts by less than a texel. Same rule as
/// Tools/inset_uv_islands.py for the GLB assets.
public static class ScanUvInset
{
    public const float DefaultTexels = 1.0f;
    private static readonly Dictionary<Mesh, Mesh> Inset = new Dictionary<Mesh, Mesh>();

    public static Mesh Apply(Mesh source, int textureSize, float texels = DefaultTexels)
    {
        if (source == null || !source.isReadable || textureSize <= 0)
        {
            return source;
        }
        if (Inset.TryGetValue(source, out Mesh cached) && cached != null)
        {
            return cached;
        }

        Vector2[] uv = source.uv;
        if (uv == null || uv.Length != source.vertexCount)
        {
            return source;
        }
        var timer = System.Diagnostics.Stopwatch.StartNew();
        int[] triangles = source.triangles;
        // Border edges are the ones used by one triangle only. Sort packed
        // edge keys instead of hashing: hashing 400k edges stalled the
        // single-threaded WebGL player for seconds.
        long[] keys = new long[triangles.Length];
        for (int t = 0; t + 2 < triangles.Length; t += 3)
        {
            for (int k = 0; k < 3; k++)
            {
                int a = triangles[t + k];
                int b = triangles[t + (k + 1) % 3];
                keys[t + k] = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
            }
        }
        System.Array.Sort(keys);
        bool[] border = new bool[uv.Length];
        for (int i = 0; i < keys.Length;)
        {
            int j = i + 1;
            while (j < keys.Length && keys[j] == keys[i])
            {
                j++;
            }
            if (j - i == 1)
            {
                border[(int)(keys[i] >> 32)] = true;
                border[(int)(keys[i] & 0xffffffff)] = true;
            }
            i = j;
        }

        Vector2[] pull = new Vector2[uv.Length];
        int[] hits = new int[uv.Length];
        for (int t = 0; t + 2 < triangles.Length; t += 3)
        {
            Vector2 centroid = (uv[triangles[t]] + uv[triangles[t + 1]] + uv[triangles[t + 2]]) / 3f;
            for (int k = 0; k < 3; k++)
            {
                int v = triangles[t + k];
                pull[v] += centroid - uv[v];
                hits[v]++;
            }
        }
        float step = texels / textureSize;
        Vector2[] moved = (Vector2[])uv.Clone();
        for (int i = 0; i < uv.Length; i++)
        {
            if (!border[i] || hits[i] == 0)
            {
                continue;
            }
            Vector2 direction = pull[i] / hits[i];
            float length = direction.magnitude;
            if (length < 1e-9f)
            {
                continue;
            }
            moved[i] = uv[i] + direction / length * Mathf.Min(step, length * 0.3f);
        }

        Mesh copy = Object.Instantiate(source);
        copy.name = source.name;
        copy.uv = moved;
        Inset[source] = copy;
        Debug.Log($"GYMCHAOS_SCAN_UV_INSET mesh={source.name} vertices={uv.Length} ms={timer.ElapsedMilliseconds}");
        return copy;
    }

    /// Inset plus the sliver fix below. Thin triangles along UV seams whose
    /// UVs point into a light island (skin, shirt) show as 1 px orange or
    /// white lines on dark cloth; their texels belong to that light island,
    /// so the texture cannot be repainted. Each such isolated light triangle
    /// inside dark cloth gets its own three vertices (same position, normal
    /// and skin weights) with the UV of its darkest 3D neighbour, so it reads
    /// the cloth colour. Neighbouring triangles keep their vertices.
    public static Mesh Apply(Mesh source, Texture texture)
    {
        Mesh inset = Apply(source, TextureSize(texture));
        if (inset == null || inset == source || texture == null || Slivered.Contains(inset))
        {
            return inset;
        }
        Slivered.Add(inset);
        if (inset.blendShapeCount > 0)
        {
            return inset;
        }
        Color32[] pixels = ReadPixels(texture, out int width, out int height);
        if (pixels == null)
        {
            return inset;
        }
        var timer = System.Diagnostics.Stopwatch.StartNew();
        int fixedCount = FixSlivers(inset, pixels, width, height);
        Debug.Log($"GYMCHAOS_SCAN_SLIVER_FIX mesh={inset.name} triangles={fixedCount} ms={timer.ElapsedMilliseconds}");
        return inset;
    }

    private static readonly HashSet<Mesh> Slivered = new HashSet<Mesh>();

    private static Color32[] ReadPixels(Texture texture, out int width, out int height)
    {
        width = texture.width;
        height = texture.height;
        if (texture is Texture2D readable && readable.isReadable)
        {
            return readable.GetPixels32();
        }
        RenderTexture target = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32,
            RenderTextureReadWrite.sRGB);
        RenderTexture previous = RenderTexture.active;
        try
        {
            Graphics.Blit(texture, target);
            RenderTexture.active = target;
            Texture2D copy = new Texture2D(width, height, TextureFormat.RGBA32, false);
            copy.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            copy.Apply(false, false);
            Color32[] pixels = copy.GetPixels32();
            Object.Destroy(copy);
            return pixels;
        }
        finally
        {
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(target);
        }
    }

    private static int FixSlivers(Mesh mesh, Color32[] pixels, int width, int height)
    {
        Vector3[] positions = mesh.vertices;
        Vector2[] uv = mesh.uv;
        int subMeshes = mesh.subMeshCount;
        var triangles = new List<int>();
        var subLength = new int[subMeshes];
        for (int s = 0; s < subMeshes; s++)
        {
            int[] t = mesh.GetTriangles(s);
            triangles.AddRange(t);
            subLength[s] = t.Length;
        }
        int count = triangles.Count / 3;
        if (count == 0)
        {
            return 0;
        }

        // weld by position so neighbours across UV seams are found
        float cell = Mathf.Max(1e-7f, mesh.bounds.size.magnitude * 1e-6f);
        var weldMap = new Dictionary<Vector3Int, int>(positions.Length);
        int[] weld = new int[positions.Length];
        for (int i = 0; i < positions.Length; i++)
        {
            Vector3 p = positions[i];
            var key = new Vector3Int(Mathf.RoundToInt(p.x / cell), Mathf.RoundToInt(p.y / cell), Mathf.RoundToInt(p.z / cell));
            if (!weldMap.TryGetValue(key, out int id))
            {
                id = weldMap.Count;
                weldMap.Add(key, id);
            }
            weld[i] = id;
        }
        long[] edgeKey = new long[count * 3];
        int[] edgeTri = new int[count * 3];
        for (int t = 0; t < count; t++)
        {
            for (int k = 0; k < 3; k++)
            {
                int a = weld[triangles[t * 3 + k]];
                int b = weld[triangles[t * 3 + (k + 1) % 3]];
                edgeKey[t * 3 + k] = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                edgeTri[t * 3 + k] = t;
            }
        }
        System.Array.Sort(edgeKey, edgeTri);
        var neighbours = new List<int>[count];
        for (int i = 0; i + 1 < edgeKey.Length; i++)
        {
            if (edgeKey[i] != edgeKey[i + 1])
            {
                continue;
            }
            int a = edgeTri[i];
            int b = edgeTri[i + 1];
            (neighbours[a] ??= new List<int>(3)).Add(b);
            (neighbours[b] ??= new List<int>(3)).Add(a);
        }

        float[] lum = new float[count];
        float[] uvArea = new float[count];
        Vector2[] centre = new Vector2[count];
        for (int t = 0; t < count; t++)
        {
            Vector2 c = (uv[triangles[t * 3]] + uv[triangles[t * 3 + 1]] + uv[triangles[t * 3 + 2]]) / 3f;
            centre[t] = c;
            Vector2 e1 = uv[triangles[t * 3 + 1]] - uv[triangles[t * 3]];
            Vector2 e2 = uv[triangles[t * 3 + 2]] - uv[triangles[t * 3]];
            uvArea[t] = Mathf.Abs(e1.x * e2.y - e1.y * e2.x) * 0.5f * width * height;
            int x = Mathf.Clamp(Mathf.FloorToInt(c.x * width), 0, width - 1);
            int y = Mathf.Clamp(Mathf.FloorToInt(c.y * height), 0, height - 1);
            Color32 px = pixels[y * width + x];
            lum[t] = (px.r + px.g + px.b) / 3f;
        }

        var sliver = new List<int>();
        var target = new List<Vector2>();
        for (int t = 0; t < count; t++)
        {
            List<int> nn = neighbours[t];
            // true slivers (under half a texel) are fixed from a lower contrast
            bool tiny = uvArea[t] < 0.5f;
            float minLum = tiny ? 40f : 60f;
            float minJump = tiny ? 22f : 40f;
            if (nn == null || nn.Count < 2 || lum[t] < minLum)
            {
                continue;
            }
            // at most one light neighbour (slivers often come in pairs) and
            // at least two dark ones, so real light details are never hit
            float darkest = float.MaxValue;
            int pick = -1;
            int light = 0;
            foreach (int n in nn)
            {
                if (lum[n] >= 50f)
                {
                    light++;
                    continue;
                }
                if (lum[n] < darkest)
                {
                    darkest = lum[n];
                    pick = n;
                }
            }
            if (light > 1 || nn.Count - light < 2 || pick < 0 || lum[t] - darkest < minJump)
            {
                continue;
            }
            sliver.Add(t);
            target.Add(centre[pick]);
        }
        if (sliver.Count == 0)
        {
            return 0;
        }

        int baseCount = positions.Length;
        var newPositions = new List<Vector3>(positions);
        Vector3[] normals = mesh.normals;
        var newNormals = normals.Length == baseCount ? new List<Vector3>(normals) : null;
        Vector4[] tangents = mesh.tangents;
        var newTangents = tangents.Length == baseCount ? new List<Vector4>(tangents) : null;
        Color32[] colors = mesh.colors32;
        var newColors = colors.Length == baseCount ? new List<Color32>(colors) : null;
        BoneWeight[] weights = mesh.boneWeights;
        var newWeights = weights.Length == baseCount ? new List<BoneWeight>(weights) : null;
        var newUv = new List<Vector2>(uv);
        for (int i = 0; i < sliver.Count; i++)
        {
            int t = sliver[i];
            for (int k = 0; k < 3; k++)
            {
                int v = triangles[t * 3 + k];
                newPositions.Add(positions[v]);
                newNormals?.Add(normals[v]);
                newTangents?.Add(tangents[v]);
                newColors?.Add(colors[v]);
                newWeights?.Add(weights[v]);
                newUv.Add(target[i]);
                triangles[t * 3 + k] = newPositions.Count - 1;
            }
        }
        if (newPositions.Count > 65535)
        {
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        }
        mesh.SetVertices(newPositions);
        if (newNormals != null) mesh.SetNormals(newNormals);
        if (newTangents != null) mesh.SetTangents(newTangents);
        if (newColors != null) mesh.SetColors(newColors);
        mesh.SetUVs(0, newUv);
        if (newWeights != null) mesh.boneWeights = newWeights.ToArray();
        int offset = 0;
        for (int s = 0; s < subMeshes; s++)
        {
            mesh.SetTriangles(triangles.GetRange(offset, subLength[s]), s, false);
            offset += subLength[s];
        }
        mesh.RecalculateBounds();
        return sliver.Count;
    }

    public static int TextureSize(Texture texture)
    {
        return texture != null ? Mathf.Max(texture.width, texture.height) : 0;
    }
}
