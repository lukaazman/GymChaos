using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// Checks that the reception NPC (manwithsuit1) keeps a closed skin while his
/// right forearm and the held towel shake: no suit edge around the shoulder
/// and arm may tear open or collapse, and the hand must still move.
/// With -Graphics it also writes shoulder close-ups to Logs/agent/receptionist.
[InitializeOnLoad]
public static class GymChaosReceptionistSkinVerifier
{
    private const string RequestedKey = "GymChaos.ReceptionistSkinVerificationRequested";
    private const double SampleSeconds = 3.5d;
    private const float TearStretch = 1.45f;
    private const float CollapseStretch = 0.55f;
    // Hand travel measured with the original (tearing) weights was 0.165 of
    // the mesh height; the fixed skin must keep at least 80% of that shake.
    private const float OriginalHandTravel = 0.165f;
    private const float MinimumHandTravel = OriginalHandTravel * 0.8f;

    private static double startTime;
    private static double sampleStart;
    private static bool finished;
    private static int resultCode;
    private static SkinnedMeshRenderer body;
    private static Mesh baked;
    private static Vector3[] rest;
    private static int[] edgeA;
    private static int[] edgeB;
    private static float[] restLength;
    private static bool[] armEdge;
    private static bool[] handVertex;
    private static float[] maxRatio;
    private static float[] minRatio;
    private static Vector3[] firstHandPose;
    private static float handTravel;
    private static int frames;
    private static int captures;
    private static int motionFrames;
    private static int sweepViews;
    private static int sweepHoles;
    private static int worstHoleArea;
    private const int SweepCount = 72;
    private const int MaxTearPx = 600;
    private const int MinTearPx = 5;

    static GymChaosReceptionistSkinVerifier()
    {
        if (!SessionState.GetBool(RequestedKey, false))
        {
            return;
        }
        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        EditorApplication.delayCall += () =>
        {
            if (EditorApplication.isPlaying)
            {
                startTime = EditorApplication.timeSinceStartup;
                EditorApplication.update -= Tick;
                EditorApplication.update += Tick;
            }
        };
    }

    [MenuItem("Tools/GymChaos/Run Receptionist Skin Verification")]
    public static void Run()
    {
        finished = false;
        resultCode = 1;
        GymChaosVerifierExit.Record(resultCode);
        body = null;
        rest = null;
        frames = 0;
        captures = 0;
        motionFrames = 0;
        sweepViews = 0;
        sweepHoles = 0;
        worstHoleArea = 0;
        handTravel = 0f;
        SessionState.SetBool(RequestedKey, true);
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        EditorApplication.isPlaying = true;
    }

    private static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            startTime = EditorApplication.timeSinceStartup;
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
        }
        else if (state == PlayModeStateChange.EnteredEditMode)
        {
            EditorApplication.update -= Tick;
            SessionState.EraseBool(RequestedKey);
            if (Application.isBatchMode)
            {
                GymChaosVerifierExit.Exit(resultCode);
            }
        }
    }

    private static void Tick()
    {
        if (finished || !EditorApplication.isPlaying)
        {
            return;
        }

        double elapsed = EditorApplication.timeSinceStartup - startTime;
        if (body == null)
        {
            GameObject npc = GameObject.Find("NPC - manwithsuit1");
            body = npc != null ? npc.GetComponentInChildren<SkinnedMeshRenderer>() : null;
            if (body == null || body.sharedMesh == null)
            {
                body = null;
                if (elapsed > 60d)
                {
                    Finish(false, "receptionist_visual_missing");
                }
                return;
            }
            Prepare();
            sampleStart = EditorApplication.timeSinceStartup;
            return;
        }

        Sample();
        double sampled = EditorApplication.timeSinceStartup - sampleStart;
        if (SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null &&
            captures < 3 && sampled > 0.6d + captures * 0.9d)
        {
            Capture(captures++);
        }
        // Consecutive frames from two fixed angles over a shake cycle, so the
        // moving forearm/towel and the closed shoulder can be seen in motion.
        if (SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null &&
            sampled > 3.0d && motionFrames < 16)
        {
            CaptureMotion(motionFrames++);
        }
        // one view per tick, so the 72 views land on different shake phases
        else if (SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null &&
            sampled > 1.0d && sweepViews < SweepCount)
        {
            SweepView(sweepViews++);
        }
        if (sampled >= SampleSeconds && frames >= 20 &&
            ((motionFrames >= 16 && sweepViews >= SweepCount) || SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null))
        {
            Evaluate();
        }
    }

    private static void Prepare()
    {
        Mesh mesh = body.sharedMesh;
        rest = mesh.vertices;
        baked = new Mesh();
        int[] triangles = mesh.triangles;
        BoneWeight[] weights = mesh.boneWeights;
        Transform[] bones = body.bones;
        bool[] armBone = new bool[bones.Length];
        bool[] handBone = new bool[bones.Length];
        for (int i = 0; i < bones.Length; i++)
        {
            string name = bones[i] != null ? bones[i].name : string.Empty;
            armBone[i] = name.StartsWith("Right ", StringComparison.Ordinal) &&
                (name.Contains("Arm") || name.Contains("Hand") || name.Contains("Shoulder"));
            handBone[i] = name == "Right Hand";
        }

        bool[] armVertex = new bool[rest.Length];
        handVertex = new bool[rest.Length];
        for (int i = 0; i < weights.Length; i++)
        {
            BoneWeight w = weights[i];
            armVertex[i] = (w.weight0 > 0f && armBone[w.boneIndex0]) ||
                (w.weight1 > 0f && armBone[w.boneIndex1]) ||
                (w.weight2 > 0f && armBone[w.boneIndex2]) ||
                (w.weight3 > 0f && armBone[w.boneIndex3]);
            handVertex[i] = w.weight0 > 0.5f && handBone[w.boneIndex0];
        }

        Bounds bounds = mesh.bounds;
        float minimum = bounds.size.y * 0.0004f;
        HashSet<long> seen = new HashSet<long>();
        List<int> a = new List<int>();
        List<int> b = new List<int>();
        List<float> length = new List<float>();
        List<bool> arm = new List<bool>();
        for (int t = 0; t + 2 < triangles.Length; t += 3)
        {
            for (int k = 0; k < 3; k++)
            {
                int i0 = triangles[t + k];
                int i1 = triangles[t + (k + 1) % 3];
                int lo = Math.Min(i0, i1);
                int hi = Math.Max(i0, i1);
                if (!seen.Add(((long)lo << 32) | (uint)hi))
                {
                    continue;
                }
                float l = Vector3.Distance(rest[lo], rest[hi]);
                if (l < minimum)
                {
                    continue;
                }
                a.Add(lo);
                b.Add(hi);
                length.Add(l);
                arm.Add(armVertex[lo] || armVertex[hi]);
            }
        }
        edgeA = a.ToArray();
        edgeB = b.ToArray();
        restLength = length.ToArray();
        armEdge = arm.ToArray();
        maxRatio = new float[edgeA.Length];
        minRatio = new float[edgeA.Length];
        for (int i = 0; i < minRatio.Length; i++)
        {
            minRatio[i] = float.MaxValue;
        }
        Debug.Log($"GYMCHAOS_RECEPTIONIST_SKIN_PREPARED vertices={rest.Length} edges={edgeA.Length} " +
            $"armEdges={Count(armEdge)} handVertices={Count(handVertex)} bones={bones.Length}");
    }

    private static void Sample()
    {
        body.BakeMesh(baked);
        Vector3[] now = baked.vertices;
        if (now.Length != rest.Length)
        {
            return;
        }

        // BakeMesh may drop the renderer scale; normalise by the body scale.
        float scale = MedianScale(now);
        for (int i = 0; i < edgeA.Length; i++)
        {
            float ratio = Vector3.Distance(now[edgeA[i]], now[edgeB[i]]) / (restLength[i] * scale);
            if (ratio > maxRatio[i]) maxRatio[i] = ratio;
            if (ratio < minRatio[i]) minRatio[i] = ratio;
        }

        if (firstHandPose == null)
        {
            firstHandPose = (Vector3[])now.Clone();
        }
        else
        {
            for (int i = 0; i < now.Length; i += 7)
            {
                if (handVertex[i])
                {
                    handTravel = Mathf.Max(handTravel, Vector3.Distance(now[i], firstHandPose[i]) / scale);
                }
            }
        }
        frames++;
    }

    private static float MedianScale(Vector3[] now)
    {
        List<float> ratios = new List<float>(256);
        int step = Math.Max(1, edgeA.Length / 256);
        for (int i = 0; i < edgeA.Length; i += step)
        {
            if (!armEdge[i])
            {
                ratios.Add(Vector3.Distance(now[edgeA[i]], now[edgeB[i]]) / restLength[i]);
            }
        }
        ratios.Sort();
        return ratios.Count > 0 ? Mathf.Max(1e-6f, ratios[ratios.Count / 2]) : 1f;
    }

    private static void Evaluate()
    {
        int torn = 0, collapsed = 0, tornBody = 0;
        float worstStretch = 0f, worstCollapse = float.MaxValue;
        for (int i = 0; i < edgeA.Length; i++)
        {
            bool tear = maxRatio[i] > TearStretch;
            bool collapse = minRatio[i] < CollapseStretch;
            if (armEdge[i])
            {
                if (tear) torn++;
                if (collapse) collapsed++;
                worstStretch = Mathf.Max(worstStretch, maxRatio[i]);
                worstCollapse = Mathf.Min(worstCollapse, minRatio[i]);
            }
            else if (tear)
            {
                tornBody++;
            }
        }

        LogWorstEdges();
        string details = $"frames={frames} armEdges={Count(armEdge)} torn={torn} collapsed={collapsed} " +
            $"tornBody={tornBody} worstStretch={worstStretch:F3} worstCollapse={worstCollapse:F3} " +
            $"handTravel={handTravel:F4} handTravelVsOriginal={handTravel / OriginalHandTravel:P0} captures={captures}";
        Texture texture = body.sharedMaterial != null ? body.sharedMaterial.GetTexture("_BaseMap") : null;
        int mips = texture != null ? texture.mipmapCount : -1;
        bool copySupported = (SystemInfo.copyTextureSupport & UnityEngine.Rendering.CopyTextureSupport.Basic) != 0;
        bool mipsLimited = texture != null && (!copySupported || mips <= ScanTextureMips.DefaultLevels);
        details += $" textureMips={mips} mipsLimited={mipsLimited}";
        details += $" sweepViews={sweepViews} shoulderHoles={sweepHoles} worstHolePx={worstHoleArea}";
        bool sweepOk = sweepViews == 0 || sweepHoles == 0;
        bool pass = sweepOk && torn == 0 && collapsed == 0 && tornBody == 0 && handTravel >= MinimumHandTravel && mipsLimited;
        Finish(pass, details);
    }

    private static void LogWorstEdges()
    {
        BoneWeight[] weights = body.sharedMesh.boneWeights;
        Transform[] bones = body.bones;
        Bounds bounds = body.sharedMesh.bounds;
        List<int> order = new List<int>();
        for (int i = 0; i < edgeA.Length; i++)
        {
            if (maxRatio[i] > TearStretch || minRatio[i] < CollapseStretch)
            {
                order.Add(i);
            }
        }
        order.Sort((x, y) => maxRatio[y].CompareTo(maxRatio[x]));
        Dictionary<string, int> pairs = new Dictionary<string, int>();
        foreach (int e in order)
        {
            string key = Describe(weights[edgeA[e]], bones) + " | " + Describe(weights[edgeB[e]], bones);
            pairs[key] = pairs.TryGetValue(key, out int n) ? n + 1 : 1;
        }
        List<KeyValuePair<string, int>> sorted = new List<KeyValuePair<string, int>>(pairs);
        sorted.Sort((x, y) => y.Value.CompareTo(x.Value));
        for (int i = 0; i < Math.Min(12, sorted.Count); i++)
        {
            Debug.Log($"GYMCHAOS_RECEPTIONIST_SKIN_PAIR count={sorted[i].Value} {sorted[i].Key}");
        }
        for (int i = 0; i < Math.Min(8, order.Count); i++)
        {
            int e = order[i];
            Vector3 p = rest[edgeA[e]];
            Debug.Log($"GYMCHAOS_RECEPTIONIST_SKIN_EDGE max={maxRatio[e]:F2} min={minRatio[e]:F2} " +
                $"y={(p.y - bounds.min.y) / bounds.size.y:F3} x={(p.x - bounds.center.x) / bounds.size.y:F3} " +
                $"z={(p.z - bounds.center.z) / bounds.size.y:F3} len={restLength[e] / bounds.size.y:F5} " +
                $"a={Describe(weights[edgeA[e]], bones)} b={Describe(weights[edgeB[e]], bones)}");
        }
        foreach (Transform bone in bones)
        {
            if (bone != null && bone.name.StartsWith("Right", StringComparison.Ordinal))
            {
                Debug.Log($"GYMCHAOS_RECEPTIONIST_SKIN_BONE {bone.name} local={bone.localEulerAngles}");
            }
        }
    }

    private static string Describe(BoneWeight w, Transform[] bones)
    {
        string Name(int index, float weight) => weight > 0.01f && index < bones.Length && bones[index] != null
            ? $"{bones[index].name}:{weight:F2} " : string.Empty;
        return (Name(w.boneIndex0, w.weight0) + Name(w.boneIndex1, w.weight1) +
            Name(w.boneIndex2, w.weight2) + Name(w.boneIndex3, w.weight3)).Trim();
    }

    // Renders only the receptionist on a magenta background from one of 72
    // directions and counts background pixels fully enclosed by his body near
    // the right shoulder: a tear or see-through patch shows up as such a hole.
    private static void SweepView(int index)
    {
        Transform shoulder = FindBone("Right Upper Arm");
        Transform elbow = FindBone("Right Forearm");
        if (shoulder == null || elbow == null)
        {
            return;
        }
        const int Size = 512;
        const int Layer = 31;
        int azimuth = index % 24;
        int elevation = index / 24;
        Transform root = body.transform.root;
        Vector3 forward = Vector3.ProjectOnPlane(root.forward, Vector3.up).normalized;
        Vector3 dir = Quaternion.AngleAxis(azimuth * 15f, Vector3.up) * forward;
        dir = (dir * Mathf.Cos((elevation * 30f - 15f) * Mathf.Deg2Rad) +
            Vector3.up * Mathf.Sin((elevation * 30f - 15f) * Mathf.Deg2Rad)).normalized;
        Vector3 focus = Vector3.Lerp(shoulder.position, elbow.position, 0.4f);
        int oldLayer = body.gameObject.layer;
        RenderTexture target = new RenderTexture(Size, Size, 24);
        GameObject probeObject = new GameObject("Receptionist Sweep Probe");
        Camera probe = probeObject.AddComponent<Camera>();
        probe.nearClipPlane = 0.02f;
        probe.fieldOfView = 40f;
        probe.clearFlags = CameraClearFlags.SolidColor;
        probe.backgroundColor = Color.magenta;
        probe.cullingMask = 1 << Layer;
        try
        {
            body.gameObject.layer = Layer;
            Vector3 position = focus + dir * 1.4f;
            probe.transform.SetPositionAndRotation(position, Quaternion.LookRotation(focus - position, Vector3.up));
            probe.targetTexture = target;
            probe.Render();
            RenderTexture.active = target;
            Texture2D image = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
            image.ReadPixels(new Rect(0f, 0f, Size, Size), 0, 0);
            image.Apply(false, false);
            Color32[] px = image.GetPixels32();
            bool[] bg = new bool[px.Length];
            for (int i = 0; i < px.Length; i++)
            {
                bg[i] = px[i].r > 200 && px[i].g < 60 && px[i].b > 200;
            }
            // background reachable from the border is outside the body
            bool[] outside = new bool[px.Length];
            Queue<int> queue = new Queue<int>();
            for (int i = 0; i < Size; i++)
            {
                foreach (int e in new[] { i, (Size - 1) * Size + i, i * Size, i * Size + Size - 1 })
                {
                    if (bg[e] && !outside[e]) { outside[e] = true; queue.Enqueue(e); }
                }
            }
            while (queue.Count > 0)
            {
                int v = queue.Dequeue();
                int x = v % Size, y = v / Size;
                if (x > 0) Visit(v - 1); if (x < Size - 1) Visit(v + 1);
                if (y > 0) Visit(v - Size); if (y < Size - 1) Visit(v + Size);
            }
            void Visit(int n) { if (bg[n] && !outside[n]) { outside[n] = true; queue.Enqueue(n); } }
            // enclosed background near the shoulder/upper arm on screen
            Vector3 a = probe.WorldToScreenPoint(shoulder.position);
            Vector3 b = probe.WorldToScreenPoint(elbow.position);
            float radius = Mathf.Max(40f, Vector2.Distance(a, b) * 0.6f);
            // Enclosed background components: a tear is a small hole in the
            // surface; the loop between a raised arm and the torso is a big
            // gap and is not a defect.
            int holePx = 0;
            bool[] done = new bool[px.Length];
            for (int i = 0; i < px.Length; i++)
            {
                if (!bg[i] || outside[i] || done[i]) continue;
                List<int> component = new List<int>();
                Queue<int> fill = new Queue<int>();
                fill.Enqueue(i);
                done[i] = true;
                while (fill.Count > 0)
                {
                    int v = fill.Dequeue();
                    component.Add(v);
                    int x = v % Size, y = v / Size;
                    foreach (int n in new[] { x > 0 ? v - 1 : -1, x < Size - 1 ? v + 1 : -1, y > 0 ? v - Size : -1, y < Size - 1 ? v + Size : -1 })
                    {
                        if (n >= 0 && bg[n] && !outside[n] && !done[n]) { done[n] = true; fill.Enqueue(n); }
                    }
                }
                // 1-4 px slits where two separate surfaces overlap on screen are
                // rasterisation gaps, not see-through geometry
                if (component.Count > MaxTearPx || component.Count < MinTearPx) continue;
                foreach (int v in component)
                {
                    if (DistanceToSegment2D(new Vector2(v % Size, v / Size), a, b) < radius) { holePx += component.Count; break; }
                }
            }
            if (holePx > 0)
            {
                sweepHoles++;
                string folder = Path.Combine(Path.GetDirectoryName(Application.dataPath), "..", "Logs", "agent", "receptionist");
                Directory.CreateDirectory(folder);
                File.WriteAllBytes(Path.Combine(folder, $"sweep_hole_{index:00}.png"), image.EncodeToPNG());
            }
            worstHoleArea = Mathf.Max(worstHoleArea, holePx);
            if (true) // every view is kept as evidence
            {
                string folder = Path.Combine(Path.GetDirectoryName(Application.dataPath), "..", "Logs", "agent", "receptionist");
                Directory.CreateDirectory(folder);
                File.WriteAllBytes(Path.Combine(folder, $"sweep_{index:00}.png"), image.EncodeToPNG());
            }
            UnityEngine.Object.Destroy(image);
        }
        finally
        {
            body.gameObject.layer = oldLayer;
            RenderTexture.active = null;
            probe.targetTexture = null;
            UnityEngine.Object.Destroy(probeObject);
            target.Release();
            UnityEngine.Object.Destroy(target);
        }
    }

    private static float DistanceToSegment2D(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float t = ab.sqrMagnitude > 1e-6f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude) : 0f;
        return Vector2.Distance(p, a + ab * t);
    }

    private static void CaptureMotion(int index)
    {
        Transform shoulder = FindBone("Right Upper Arm");
        Transform hand = FindBone("Right Hand");
        if (shoulder == null || hand == null)
        {
            return;
        }
        string folder = Path.Combine(Path.GetDirectoryName(Application.dataPath), "..", "Logs", "agent", "receptionist");
        Directory.CreateDirectory(folder);
        Transform root = body.transform.root;
        Vector3 forward = Vector3.ProjectOnPlane(root.forward, Vector3.up).normalized;
        Vector3 right = Vector3.Cross(Vector3.up, forward);
        // fixed focus at the shoulder so only the arm moves between frames
        Vector3 focus = shoulder.position;
        var views = new[]
        {
            ("side", (-forward + right * 0.5f) * 0.8f + Vector3.up * 0.5f),
            // the wall is right behind him, so look at the back of the shoulder from above
            ("above-back", forward * 0.35f + right * 0.25f + Vector3.up * 1.0f),
        };
        RenderTexture target = new RenderTexture(640, 640, 24);
        GameObject probeObject = new GameObject("Receptionist Motion Probe");
        Camera probe = probeObject.AddComponent<Camera>();
        probe.nearClipPlane = 0.02f;
        probe.fieldOfView = 50f;
        probe.clearFlags = CameraClearFlags.SolidColor;
        probe.backgroundColor = new Color(0.62f, 0.66f, 0.62f);
        try
        {
            foreach (var view in views)
            {
                Vector3 position = focus + view.Item2.normalized * (view.Item1 == "side" ? 1.3f : 1.0f);
                probe.transform.SetPositionAndRotation(position, Quaternion.LookRotation(focus - position, Vector3.up));
                probe.targetTexture = target;
                probe.Render();
                RenderTexture.active = target;
                Texture2D image = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
                image.ReadPixels(new Rect(0f, 0f, target.width, target.height), 0, 0);
                image.Apply(false, false);
                File.WriteAllBytes(Path.Combine(folder, $"motion_{index:00}_{view.Item1}.png"), image.EncodeToPNG());
                UnityEngine.Object.Destroy(image);
            }
            Debug.Log($"GYMCHAOS_RECEPTIONIST_MOTION_FRAME index={index} time={Time.time:F3} hand={hand.position}");
        }
        finally
        {
            RenderTexture.active = null;
            probe.targetTexture = null;
            UnityEngine.Object.Destroy(probeObject);
            target.Release();
            UnityEngine.Object.Destroy(target);
        }
    }

    private static void Capture(int index)
    {
        Transform shoulder = FindBone("Right Upper Arm");
        Transform hand = FindBone("Right Hand");
        if (shoulder == null || hand == null)
        {
            return;
        }

        string folder = Path.Combine(Path.GetDirectoryName(Application.dataPath), "..", "Logs", "agent", "receptionist");
        Directory.CreateDirectory(folder);
        RenderTexture target = new RenderTexture(900, 900, 24);
        GameObject probeObject = new GameObject("Receptionist Skin Probe");
        Camera probe = probeObject.AddComponent<Camera>();
        probe.nearClipPlane = 0.02f;
        probe.fieldOfView = 38f;
        probe.clearFlags = CameraClearFlags.SolidColor;
        probe.backgroundColor = new Color(0.62f, 0.66f, 0.62f);
        Vector3 focus = Vector3.Lerp(shoulder.position, hand.position, 0.3f);
        Transform root = body.transform.root;
        Vector3 forward = Vector3.ProjectOnPlane(root.forward, Vector3.up).normalized;
        Vector3 right = Vector3.Cross(Vector3.up, forward);
        Vector3[] directions =
        {
            forward + Vector3.up * 0.9f,
            (forward + right) * 0.7f + Vector3.up * 0.8f,
            (forward - right) * 0.7f + Vector3.up * 0.8f,
            -forward + Vector3.up * 0.7f,
            (-forward + right) * 0.7f + Vector3.up * 0.6f,
            (-forward - right) * 0.7f + Vector3.up * 0.6f,
        };
        string[] names = { "front-top", "front-right", "front-left", "back-top", "back-right", "back-left" };
        try
        {
            for (int i = 0; i < directions.Length; i++)
            {
                Vector3 position = focus + directions[i].normalized * 1.15f;
                probe.transform.SetPositionAndRotation(position, Quaternion.LookRotation(focus - position, Vector3.up));
                probe.targetTexture = target;
                probe.Render();
                RenderTexture.active = target;
                Texture2D image = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
                image.ReadPixels(new Rect(0f, 0f, target.width, target.height), 0, 0);
                image.Apply(false, false);
                File.WriteAllBytes(Path.Combine(folder, $"shoulder_{index}_{names[i]}.png"), image.EncodeToPNG());
                UnityEngine.Object.Destroy(image);
            }
        }
        finally
        {
            RenderTexture.active = null;
            probe.targetTexture = null;
            UnityEngine.Object.Destroy(probeObject);
            target.Release();
            UnityEngine.Object.Destroy(target);
        }
    }

    private static Transform FindBone(string name)
    {
        foreach (Transform bone in body.bones)
        {
            if (bone != null && bone.name == name)
            {
                return bone;
            }
        }
        return null;
    }

    private static int Count(bool[] values)
    {
        int n = 0;
        foreach (bool v in values)
        {
            if (v) n++;
        }
        return n;
    }

    private static void Finish(bool pass, string details)
    {
        finished = true;
        resultCode = pass ? 0 : 1;
        GymChaosVerifierExit.Record(resultCode);
        if (pass)
        {
            Debug.Log("GYMCHAOS_RECEPTIONIST_SKIN_OK " + details);
        }
        else
        {
            Debug.LogError("GYMCHAOS_RECEPTIONIST_SKIN_FAIL " + details);
        }
        EditorApplication.isPlaying = false;
    }
}
