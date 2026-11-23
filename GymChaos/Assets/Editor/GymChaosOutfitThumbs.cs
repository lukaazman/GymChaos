#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Bakes the outfit screen's garment pictures from the game's own pipeline: the same
/// body, ScanUvInset, PlayerTopShell styles, fabrics and colours the player wears in
/// game, shown without the body. Tank top = only the cloth triangles of the scan.
/// Needs a graphics device (-Graphics). Output: Assets/Resources/UI/Outfit/{top,bottom}_{style}_{colour}.png.
/// </summary>
public static class GymChaosOutfitThumbs
{
    private const int Size = 256;
    private const int Layer = 30;
    private const string OutDir = "Assets/Resources/UI/Outfit/";
    private static IEnumerator routine;
    private static float rollDegrees;
    private static double deadline;

    public static void Run()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        routine = Bake();
        EditorApplication.update += Tick;
    }

    private static void Tick()
    {
        try
        {
            if (routine.MoveNext()) return;
            Debug.Log("GYMCHAOS_OUTFIT_THUMBS_OK");
            Finish(0);
        }
        catch (Exception exception)
        {
            Debug.LogError("GYMCHAOS_OUTFIT_THUMBS_FAILED " + exception);
            Finish(1);
        }
    }

    private static void Finish(int code)
    {
        EditorApplication.update -= Tick;
        AssetDatabase.Refresh();
        EditorApplication.Exit(code);
    }

    private static IEnumerator Bake()
    {
        Directory.CreateDirectory(OutDir);
        GameObject lightObject = new GameObject("Thumb Light");
        Light key = lightObject.AddComponent<Light>();
        key.type = LightType.Directional;
        key.intensity = 1.1f;
        key.color = new Color(1f, 0.97f, 0.93f);
        lightObject.transform.rotation = Quaternion.Euler(30f, 150f, 0f);
        GameObject fillObject = new GameObject("Thumb Fill");
        Light fill = fillObject.AddComponent<Light>();
        fill.type = LightType.Directional;
        fill.intensity = 0.5f;
        fill.color = new Color(0.85f, 0.9f, 1f);
        fillObject.transform.rotation = Quaternion.Euler(20f, 330f, 0f);
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.55f, 0.57f, 0.6f);
        RenderSettings.fog = false;

        foreach (GymShirtColor colour in Enum.GetValues(typeof(GymShirtColor)))
        {
            foreach (GymTopStyle top in Enum.GetValues(typeof(GymTopStyle)))
            {
                GameObject model = NewModel(out SkinnedMeshRenderer body);
                IEnumerator dress = top == GymTopStyle.TankTop ? Tank(body, colour) : Shell(body, top, colour);
                while (dress.MoveNext()) yield return null;
                Save(model, "top_" + top.ToString().ToLowerInvariant() + "_" + colour.ToString().ToLowerInvariant());
                UnityEngine.Object.DestroyImmediate(model);
            }
            foreach (GymBottomStyle bottom in Enum.GetValues(typeof(GymBottomStyle)))
            {
                GameObject model = NewModel(out SkinnedMeshRenderer body);
                IEnumerator dress = Bottoms(body, bottom, colour);
                while (dress.MoveNext()) yield return null;
                Save(model, "bottom_" + bottom.ToString().ToLowerInvariant() + "_" + colour.ToString().ToLowerInvariant());
                UnityEngine.Object.DestroyImmediate(model);
            }
        }
        UnityEngine.Object.DestroyImmediate(lightObject);
        UnityEngine.Object.DestroyImmediate(fillObject);
    }

    private static GameObject NewModel(out SkinnedMeshRenderer body)
    {
        GameObject prefab = Resources.Load<GameObject>(PlayerHandRig.DefaultModelResource);
        if (prefab == null) throw new InvalidOperationException("no player model");
        GameObject model = UnityEngine.Object.Instantiate(prefab);
        model.transform.SetPositionAndRotation(new Vector3(0f, 0f, 0f), Quaternion.Euler(0f, 180f, 0f));
        foreach (Transform part in model.GetComponentsInChildren<Transform>(true)) part.gameObject.layer = Layer;
        foreach (Animator animator in model.GetComponentsInChildren<Animator>(true)) animator.enabled = false;
        // The prefab rests in a crouch; the game and the journal pose it with the idle clip.
        foreach (UnityEngine.Object asset in Resources.LoadAll<UnityEngine.Object>(PlayerHandRig.DefaultModelResource))
        {
            AnimationClip clip = asset as AnimationClip;
            if (clip == null || !(clip.name == "idle1" || clip.name.EndsWith("|idle1", StringComparison.OrdinalIgnoreCase))) continue;
            GymClipSampler.Sample(clip, model, MostSymmetricTime(clip, model));
            MirrorPose(model);
            break;
        }
        body = null;
        foreach (SkinnedMeshRenderer renderer in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (body == null || renderer.sharedMesh.vertexCount > body.sharedMesh.vertexCount) body = renderer;
        }
        return model;
    }

    // The idle clip sways; take the frame whose left and right bones mirror each other best.
    private static float MostSymmetricTime(AnimationClip clip, GameObject model)
    {
        List<(Transform, Transform)> pairs = new List<(Transform, Transform)>();
        Transform[] all = model.GetComponentsInChildren<Transform>(true);
        Dictionary<string, Transform> byName = new Dictionary<string, Transform>();
        foreach (Transform t in all) byName[t.name] = t;
        foreach (Transform t in all)
        {
            string other = null;
            if (t.name.Contains("Left")) other = t.name.Replace("Left", "Right");
            else if (t.name.EndsWith(".L")) other = t.name.Substring(0, t.name.Length - 2) + ".R";
            else if (t.name.EndsWith("_L")) other = t.name.Substring(0, t.name.Length - 2) + "_R";
            if (other != null && byName.TryGetValue(other, out Transform right)) pairs.Add((t, right));
        }
        Transform hips = null;
        foreach (Transform t in all) if (t.name.ToLowerInvariant().Contains("hips") || t.name.ToLowerInvariant() == "root") { hips = t; break; }
        float bestTime = 0f, best = float.MaxValue;
        int steps = 48;
        for (int i = 0; i < steps; i++)
        {
            float time = clip.length * i / steps;
            GymClipSampler.Sample(clip, model, time);
            Vector3 centre = hips != null ? hips.position : model.transform.position;
            float cost = 0f;
            foreach ((Transform left, Transform right) in pairs)
            {
                Vector3 l = model.transform.InverseTransformPoint(left.position) - model.transform.InverseTransformPoint(centre);
                Vector3 r = model.transform.InverseTransformPoint(right.position) - model.transform.InverseTransformPoint(centre);
                cost += Mathf.Abs(l.x + r.x) + Mathf.Abs(l.y - r.y) + Mathf.Abs(l.z - r.z);
            }
            if (cost < best) { best = cost; bestTime = time; }
        }
        Debug.Log("GYMCHAOS_THUMB_SYMMETRY pairs=" + pairs.Count + " time=" + bestTime + " cost=" + best);
        return bestTime;
    }

    // Make the pose left/right symmetric: the right side copies the mirrored left rotations, and the
    // spine chain keeps only its forward/back pitch (no sway or twist).
    private static void MirrorPose(GameObject model)
    {
        Dictionary<string, Transform> byName = new Dictionary<string, Transform>();
        foreach (Transform t in model.GetComponentsInChildren<Transform>(true)) byName[t.name] = t;
        foreach (KeyValuePair<string, Transform> pair in byName)
        {
            string n = pair.Key;
            if (n.EndsWith(".L") || n.Contains(".L."))
            {
                int at = n.LastIndexOf(".L");
                string other = n.Substring(0, at) + ".R" + n.Substring(at + 2);
                if (!byName.TryGetValue(other, out Transform right)) continue;
                Quaternion q = pair.Value.localRotation;
                right.localRotation = new Quaternion(q.x, -q.y, -q.z, q.w);
            }
            else if (n.StartsWith("DEF-spine") || n == "rig.001")
            {
                Quaternion q = pair.Value.localRotation;
                Quaternion flat = new Quaternion(q.x, 0f, 0f, q.w);
                if (n != "rig.001") pair.Value.localRotation = flat.x == 0f && flat.w == 0f ? Quaternion.identity : Quaternion.Normalize(flat);
            }
        }
    }

    private static Texture2D Load(string path) => Resources.Load<Texture2D>(path);

    private static void Skin(SkinnedMeshRenderer body, Texture texture)
    {
        foreach (SkinnedMeshRenderer renderer in body.transform.root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (PlayerTopShell.IsShellObject(renderer.name)) continue;
            Material material = renderer.material;
            if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", texture);
            if (material.HasProperty("_MainTex")) material.SetTexture("_MainTex", texture);
            renderer.sharedMaterial = material;
        }
    }

    private static IEnumerator Shell(SkinnedMeshRenderer body, GymTopStyle top, GymShirtColor colour)
    {
        Texture outfit = Load(PlayerCosmeticLoadout.GetOutfitTexturePath(top, colour));
        body.sharedMesh = ScanUvInset.Apply(body.sharedMesh, outfit);
        PlayerTopShell.Style style = top == GymTopStyle.Oversized ? PlayerTopShell.Oversized : PlayerTopShell.Onyx;
        Color band = top == GymTopStyle.Oversized ? PlayerCosmeticLoadout.GetShirtColor(colour) : new Color(0.03f, 0.03f, 0.034f);
        SkinnedMeshRenderer shell = null;
        deadline = EditorApplication.timeSinceStartup + 60d;
        while ((shell = PlayerTopShell.Attach(body, style, outfit, band)) == null)
        {
            if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException("top shell " + top);
            yield return null;
        }
        body.enabled = false;
        foreach (SkinnedMeshRenderer other in body.transform.root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            if (other != shell) other.enabled = false;
    }

    private static IEnumerator Bottoms(SkinnedMeshRenderer body, GymBottomStyle bottom, GymShirtColor colour)
    {
        Texture outfit = Load(PlayerCosmeticLoadout.GetOutfitTexturePath(GymTopStyle.TankTop, GymShirtColor.Black));
        body.sharedMesh = ScanUvInset.Apply(body.sharedMesh, outfit);
        PlayerTopShell.Style style = bottom == GymBottomStyle.Pants ? PlayerTopShell.Pants : PlayerTopShell.Shorts;
        Texture2D fabric = Load(bottom == GymBottomStyle.Pants
            ? PlayerCosmeticLoadout.PantsFabricTexture : PlayerCosmeticLoadout.BottomsFabricTexture);
        Color color = PlayerCosmeticLoadout.GetShirtColor(colour);
        Color tint = color / PlayerTopShell.BottomsFabricLevel;
        tint.a = 1f;
        SkinnedMeshRenderer shell = null;
        deadline = EditorApplication.timeSinceStartup + 60d;
        while ((shell = PlayerTopShell.Attach(body, style, fabric != null ? ScanTextureMips.Limit(fabric) : null,
                   color * 0.85f, tint)) == null)
        {
            if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException("bottoms " + bottom);
            yield return null;
        }
        foreach (SkinnedMeshRenderer other in body.transform.root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            if (other != shell) other.enabled = false;
    }

    // The tank top is painted on the scan: keep only the triangles where the coloured
    // shirt texture differs from the bare-skin body texture.
    private static IEnumerator Tank(SkinnedMeshRenderer body, GymShirtColor colour)
    {
        Texture2D shirt = Load(PlayerCosmeticLoadout.GetOutfitTexturePath(GymTopStyle.TankTop, colour));
        Texture2D skin = Load(PlayerCosmeticLoadout.UnderShellBodyTexture);
        Color[] shirtPixels = Readable(shirt, out int width, out int height);
        Color[] skinPixels = Readable(skin, out int skinWidth, out int skinHeight);
        Mesh mesh = UnityEngine.Object.Instantiate(body.sharedMesh);
        Vector2[] uv = mesh.uv;
        Func<Vector2, bool> cloth = point =>
        {
            Color a = Sample(shirtPixels, width, height, point);
            Color b = Sample(skinPixels, skinWidth, skinHeight, point);
            return Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g) + Mathf.Abs(a.b - b.b) > 0.12f;
        };
        List<List<int>> keptBySub = new List<List<int>>();
        for (int sub = 0; sub < mesh.subMeshCount; sub++)
        {
            int[] triangles = mesh.GetTriangles(sub);
            List<int> kept = new List<int>();
            for (int i = 0; i + 2 < triangles.Length; i += 3)
            {
                Vector2 a = uv[triangles[i]], b = uv[triangles[i + 1]], c = uv[triangles[i + 2]];
                int hits = (cloth(a) ? 1 : 0) + (cloth(b) ? 1 : 0) + (cloth(c) ? 1 : 0) + (cloth((a + b + c) / 3f) ? 1 : 0);
                if (hits >= 3) { kept.Add(triangles[i]); kept.Add(triangles[i + 1]); kept.Add(triangles[i + 2]); }
            }
            keptBySub.Add(kept);
        }
        KeepLargestPiece(mesh, keptBySub);
        for (int sub = 0; sub < mesh.subMeshCount; sub++) mesh.SetTriangles(keptBySub[sub], sub);
        body.sharedMesh = mesh;
        Skin(body, shirt);
        yield return null;
    }

    // Stray cloth-coloured specks elsewhere on the scan: keep the biggest welded piece only.
    private static void KeepLargestPiece(Mesh mesh, List<List<int>> keptBySub)
    {
        Vector3[] vertices = mesh.vertices;
        Dictionary<Vector3Int, int> weld = new Dictionary<Vector3Int, int>();
        int[] parent = new int[vertices.Length];
        int[] id = new int[vertices.Length];
        for (int i = 0; i < vertices.Length; i++)
        {
            parent[i] = i;
            Vector3Int key = Vector3Int.RoundToInt(vertices[i] * 10000f);
            if (weld.TryGetValue(key, out int other)) parent[i] = other; else weld[key] = i;
        }
        Func<int, int> find = null;
        find = x => parent[x] == x ? x : (parent[x] = find(parent[x]));
        foreach (List<int> kept in keptBySub)
            for (int i = 0; i + 2 < kept.Count; i += 3)
            {
                int a = find(kept[i]), b = find(kept[i + 1]), c = find(kept[i + 2]);
                parent[b] = a;
                parent[find(c)] = find(a);
            }
        Dictionary<int, int> size = new Dictionary<int, int>();
        foreach (List<int> kept in keptBySub)
            for (int i = 0; i < kept.Count; i += 3)
            {
                int root = find(kept[i]);
                size[root] = size.TryGetValue(root, out int n) ? n + 1 : 1;
            }
        int best = -1, bestSize = 0;
        foreach (KeyValuePair<int, int> pair in size) if (pair.Value > bestSize) { best = pair.Key; bestSize = pair.Value; }
        foreach (List<int> kept in keptBySub)
        {
            List<int> filtered = new List<int>();
            for (int i = 0; i + 2 < kept.Count; i += 3)
                if (find(kept[i]) == best) { filtered.Add(kept[i]); filtered.Add(kept[i + 1]); filtered.Add(kept[i + 2]); }
            kept.Clear();
            kept.AddRange(filtered);
        }
    }

    private static Color[] Readable(Texture2D texture, out int width, out int height)
    {
        width = texture.width;
        height = texture.height;
        RenderTexture target = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        Graphics.Blit(texture, target);
        RenderTexture previous = RenderTexture.active;
        RenderTexture.active = target;
        Texture2D copy = new Texture2D(width, height, TextureFormat.RGBA32, false);
        copy.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        copy.Apply();
        RenderTexture.active = previous;
        RenderTexture.ReleaseTemporary(target);
        Color[] pixels = copy.GetPixels();
        UnityEngine.Object.DestroyImmediate(copy);
        return pixels;
    }

    private static Color Sample(Color[] pixels, int width, int height, Vector2 uv)
    {
        int x = Mathf.Clamp(Mathf.FloorToInt(uv.x * width), 0, width - 1);
        int y = Mathf.Clamp(Mathf.FloorToInt(uv.y * height), 0, height - 1);
        return pixels[y * width + x];
    }

    // Direction between the matching left/right bones whose name has one of the tokens.
    private static bool PairAxis(GameObject model, string[] tokens, out Vector3 axis)
    {
        axis = Vector3.right;
        Transform[] all = model.GetComponentsInChildren<Transform>(true);
        Dictionary<string, Transform> byName = new Dictionary<string, Transform>();
        foreach (Transform t in all) byName[t.name] = t;
        foreach (Transform t in all)
        {
            string lower = t.name.ToLowerInvariant();
            bool match = false;
            foreach (string token in tokens) if (lower.Contains(token)) match = true;
            if (!match) continue;
            string other = null;
            if (t.name.Contains("Left")) other = t.name.Replace("Left", "Right");
            else if (t.name.EndsWith(".L")) other = t.name.Substring(0, t.name.Length - 2) + ".R";
            else if (t.name.EndsWith("_L")) other = t.name.Substring(0, t.name.Length - 2) + "_R";
            if (other != null && byName.TryGetValue(other, out Transform right))
            {
                axis = right.position - t.position;
                return true;
            }
        }
        return false;
    }

    private static void Save(GameObject model, string name)
    {
        Bounds bounds = default;
        bool any = false;
        foreach (SkinnedMeshRenderer renderer in model.GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            if (!renderer.enabled) continue;
            renderer.updateWhenOffscreen = true;
            // Skinned bounds cover unused vertices too: take the vertices the triangles use.
            Mesh baked = new Mesh();
            renderer.BakeMesh(baked, false);
            Vector3[] vertices = baked.vertices;
            foreach (int index in baked.triangles)
            {
                Vector3 world = renderer.transform.position + renderer.transform.rotation * vertices[index];
                if (!any) { bounds = new Bounds(world, Vector3.zero); any = true; }
                else bounds.Encapsulate(world);
            }
            UnityEngine.Object.DestroyImmediate(baked);
        }
        if (!any) throw new InvalidOperationException("nothing to render for " + name);
        GameObject cameraObject = new GameObject("Thumb Camera");
        Camera camera = cameraObject.AddComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0f, 0f, 0f, 0f);
        camera.cullingMask = 1 << Layer;
        camera.orthographic = true;
        camera.orthographicSize = Mathf.Max(bounds.extents.y, bounds.extents.x) * 1.12f;
        camera.nearClipPlane = 0.01f;
        float distance = bounds.size.magnitude * 2f;
        camera.farClipPlane = distance * 3f;
        Vector3 forward = model.transform.forward;
        // Face the garment square to the body: view along the body's own front axis, up along hips-to-head,
        // so the idle clip's lean and sway never show.
        Vector3 up = Vector3.up;

        bool top = name.StartsWith("top_");
        Transform hips = null, head = null;
        foreach (Transform t in model.GetComponentsInChildren<Transform>(true))
        {
            string lower = t.name.ToLowerInvariant();
            if (hips == null && (lower == "def-spine" || lower.EndsWith("hips"))) hips = t;
            if (head == null && lower == "def-spine.005" || lower.EndsWith("head")) head = t;
        }
        if (hips != null && head != null &&
            PairAxis(model, top ? new[] { "shoulder", "clavicle" } : new[] { "upleg", "thigh", "upper_leg" }, out Vector3 axis))
        {
            Vector3 u = (head.position - hips.position).normalized;
            if (!top)
            {
                // Bottoms hang from the legs, not the spine: up runs from the feet to the thighs.
                Transform footL = null, footR = null, thighL = null, thighR = null;
                foreach (Transform t in model.GetComponentsInChildren<Transform>(true))
                {
                    if (t.name == "DEF-foot.L") footL = t;
                    else if (t.name == "DEF-foot.R") footR = t;
                    else if (t.name == "DEF-thigh.L") thighL = t;
                    else if (t.name == "DEF-thigh.R") thighR = t;
                }
                if (footL != null && footR != null && thighL != null && thighR != null)
                    u = ((thighL.position + thighR.position) * 0.5f - (footL.position + footR.position) * 0.5f).normalized;
            }
            Vector3 r = (axis - Vector3.Dot(axis, u) * u).normalized;
            Vector3 f = Vector3.Cross(r, u).normalized;
            if (Vector3.Dot(f, forward) < 0f) f = -f;
            forward = f;
            up = u;
        }
        rollDegrees = 0f;
        cameraObject.transform.position = bounds.center + forward * distance;
        cameraObject.transform.rotation = Quaternion.LookRotation(-forward, up);
        cameraObject.transform.Rotate(0f, 0f, rollDegrees, Space.Self);
        RenderTexture target = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 4 };
        camera.targetTexture = target;
        camera.Render();
        RenderTexture previous = RenderTexture.active;
        RenderTexture.active = target;
        Texture2D picture = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
        picture.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
        picture.Apply();
        RenderTexture.active = previous;
        File.WriteAllBytes(OutDir + name + ".png", picture.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(picture);
        camera.targetTexture = null;
        target.Release();
        UnityEngine.Object.DestroyImmediate(target);
        UnityEngine.Object.DestroyImmediate(cameraObject);
    }
}
#endif
