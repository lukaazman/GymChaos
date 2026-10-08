#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Runs the authored squat for every enemy that has one and measures the
/// rendered (baked) skin against the attached bar:
///  - back: the bar rests on the upper back, it neither sinks into the body
///    nor floats behind it;
///  - hands: each hand touches the bar (no visible gap) and its fingers wrap
///    over the top of the shaft (overhand grip).
/// Needs -Graphics (skinned poses are not evaluated without a device).
/// Renders front/back/side/hand views per enemy to Logs/verify/squat-grip/.
/// Logs GYMCHAOS_SQUAT_BAR_GRIP_OK.
/// </summary>
public static class GymChaosSquatBarGripVerifier
{
    private const string RequestedKey = "GymChaos.SquatBarGripVerificationRequested";
    // Bar sinks no deeper than this into the skin, and the closest skin is
    // no farther than BackGapLimit from the shaft surface.
    private const float BackPenetrationLimit = 0.012f;
    private const float BackGapLimit = 0.02f;
    private const float HandGapLimit = 0.012f;
    private const float HandPenetrationLimit = 0.02f;

    private static readonly List<EnemyFighter> queue = new List<EnemyFighter>();
    private static EnemyFighter fighter;
    private static SquatWorkoutController squat;
    private static GymExerciseStation station;
    private static int phase;
    private static double started;
    private static double phaseStarted;
    private static bool deepMeasured;
    private static bool topMeasured;
    private static bool sawDeep;
    private static readonly StringBuilder summary = new StringBuilder();
    private static readonly List<string> failures = new List<string>();

    [InitializeOnLoadMethod]
    private static void ResumeAfterReload()
    {
        if (!SessionState.GetBool(RequestedKey, false)) return;
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
        EditorApplication.playModeStateChanged -= PlayModeChanged;
        EditorApplication.playModeStateChanged += PlayModeChanged;
    }

    public static void Run()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        SessionState.SetBool(RequestedKey, true);
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
        EditorApplication.playModeStateChanged -= PlayModeChanged;
        EditorApplication.playModeStateChanged += PlayModeChanged;
        EditorApplication.isPlaying = true;
    }

    private static void PlayModeChanged(PlayModeStateChange change)
    {
        if (change == PlayModeStateChange.EnteredPlayMode)
        {
            started = EditorApplication.timeSinceStartup;
            phase = 0;
            queue.Clear();
            summary.Clear();
            failures.Clear();
            Time.timeScale = 1f;
            AudioListener.pause = true;
            SquatWorkoutController.BarRestDiagnostics = true;
        }
        if (change != PlayModeStateChange.EnteredEditMode) return;
        EditorApplication.update -= Tick;
        EditorApplication.playModeStateChanged -= PlayModeChanged;
        if (Application.isBatchMode && SessionState.GetBool(RequestedKey, false))
            EditorApplication.Exit(1);
    }

    private static void Tick()
    {
        if (!EditorApplication.isPlaying) return;
        AudioListener.pause = true;
        try
        {
            double elapsed = EditorApplication.timeSinceStartup - started;
            if (elapsed > 300d) throw new InvalidOperationException($"timed out phase={phase} fighter={fighter?.Identity}");
            GymVisitorDirector director = UnityEngine.Object.FindAnyObjectByType<GymVisitorDirector>();
            if (director == null || elapsed < 2d) return;
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
                throw new InvalidOperationException("requires_graphics_device run_with=-Graphics");
            if (phase == 0)
            {
                director.SuspendVisitorSimulationForVerification();
                BuildQueue();
                if (queue.Count == 0) throw new InvalidOperationException("no squat candidates");
                phase = 1;
            }
            if (phase == 1)
            {
                if (queue.Count == 0)
                {
                    Finish();
                    return;
                }
                StartNext();
                return;
            }
            if (phase == 2) TickSquat();
            if (phase == 3 && EditorApplication.timeSinceStartup - phaseStarted > 1.5d) phase = 1;
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            Debug.LogError("GYMCHAOS_SQUAT_BAR_GRIP_FAILED " + exception.Message);
            Exit(1);
        }
    }

    private static void BuildQueue()
    {
        HashSet<BodybuilderIdentity> seen = new HashSet<BodybuilderIdentity>();
        string only = Environment.GetEnvironmentVariable("GYMCHAOS_SQUAT_GRIP_ONLY");
        foreach (EnemyFighter candidate in UnityEngine.Object.FindObjectsByType<EnemyFighter>(
                     FindObjectsInactive.Include, FindObjectsSortMode.InstanceID))
        {
            if (candidate == null || candidate.IsDead || seen.Contains(candidate.Identity)) continue;
            if (!string.IsNullOrEmpty(only) &&
                only.IndexOf(candidate.Identity.ToString(), StringComparison.OrdinalIgnoreCase) < 0) continue;
            SquatWorkoutController controller = candidate.GetComponent<SquatWorkoutController>();
            MixamoScanRetargetAnimator animator =
                candidate.GetComponentInChildren<MixamoScanRetargetAnimator>(true);
            if (controller == null || !controller.HasValidSquatRig || !controller.HasValidArmRig ||
                animator == null || !animator.HasAuthoredSquatClip) continue;
            seen.Add(candidate.Identity);
            queue.Add(candidate);
        }
    }

    private static void StartNext()
    {
        fighter = queue[0];
        queue.RemoveAt(0);
        squat = fighter.GetComponent<SquatWorkoutController>();
        station = null;
        foreach (GymExerciseStation candidate in UnityEngine.Object.FindObjectsByType<GymExerciseStation>(FindObjectsSortMode.None))
        {
            if (candidate == null || !candidate.IsSquat || candidate.IsOccupied ||
                !candidate.HasAuthoredSquatBar) continue;
            bool smith = candidate.EquipmentName.IndexOf("smith", StringComparison.OrdinalIgnoreCase) >= 0;
            if (station == null || !smith) station = candidate;
            if (!smith) break;
        }
        if (station == null) throw new InvalidOperationException("no free squat rack");
        fighter.gameObject.SetActive(true);
        Vector3 stand = station.EnemyPosition;
        stand.y = FindSupportY(stand);
        fighter.SetVisitorSpawnPose(stand, station.EnemyRotation, true);
        fighter.transform.SetPositionAndRotation(stand, station.EnemyRotation);
        Physics.SyncTransforms();
        if (!squat.Begin(station, fighter, 40, 2.0f))
            throw new InvalidOperationException($"{fighter.Identity} squat did not start");
        foreach (SkinnedMeshRenderer skin in fighter.GetComponentsInChildren<SkinnedMeshRenderer>())
            skin.updateWhenOffscreen = true;
        deepMeasured = false;
        topMeasured = false;
        sawDeep = false;
        phase = 2;
        phaseStarted = EditorApplication.timeSinceStartup;
    }

    private static void TickSquat()
    {
        if (!squat.IsActive)
        {
            if (EditorApplication.timeSinceStartup - phaseStarted > 15d)
                throw new InvalidOperationException($"{fighter.Identity} squat ended early");
            return;
        }
        if (EditorApplication.timeSinceStartup - phaseStarted < 0.6d) return;
        float motion = squat.CurrentMotion;
        if (!deepMeasured && motion > 0.88f)
        {
            Measure("deep");
            Capture("deep");
            deepMeasured = true;
            sawDeep = true;
        }
        else if (sawDeep && !topMeasured && motion < 0.08f)
        {
            Measure("top");
            Capture("top");
            topMeasured = true;
        }
        if ((deepMeasured && topMeasured) || EditorApplication.timeSinceStartup - phaseStarted > 30d)
        {
            if (!deepMeasured || !topMeasured)
                failures.Add($"{fighter.Identity}:not_sampled deep={deepMeasured} top={topMeasured}");
            squat.Cancel();
            fighter.gameObject.SetActive(false);
            phase = 3;
            phaseStarted = EditorApplication.timeSinceStartup;
        }
    }

    private static void Measure(string label)
    {
        Transform barRoot = FindAttachedBar(fighter.transform);
        if (barRoot == null) throw new InvalidOperationException($"{fighter.Identity} has no attached bar");
        GetShaft(barRoot, out Vector3 barCenter, out Vector3 axis, out float barRadius);
        Transform leftHand = squat.LeftHandBoneForVerification;
        Transform rightHand = squat.RightHandBoneForVerification;
        Transform neck = squat.NeckBoneForVerification;
        Transform leftArm = leftHand != null && leftHand.parent != null ? FindArmRoot(leftHand) : null;
        Transform rightArm = rightHand != null && rightHand.parent != null ? FindArmRoot(rightHand) : null;
        Vector3 torsoUp = Vector3.ProjectOnPlane(
            neck != null ? neck.position - squat.Traps.position : fighter.transform.up, axis).normalized;
        Vector3 back = Vector3.Cross(axis, torsoUp).normalized;
        if (Vector3.Dot(back, fighter.transform.forward) > 0f) back = -back;
        float span = leftArm != null && rightArm != null
            ? Mathf.Abs(Vector3.Dot(leftArm.position - rightArm.position, axis)) * 0.5f * 0.85f : 0.18f;

        // Back: how far the bar would have to slide back to just touch the
        // skin (positive = sunk into the body, negative = floating behind).
        float sink = float.NegativeInfinity;
        float[] handMin = { float.PositiveInfinity, float.PositiveInfinity };
        int[] fingersOver = { 0, 0 };
        int[] handVerts = { 0, 0 };
        Mesh scratch = new Mesh();
        foreach (SkinnedMeshRenderer skin in fighter.GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            if (!skin.enabled || skin.sharedMesh == null) continue;
            Vector3[] vertices = GymSkinSampler.BakeWorld(skin, scratch);
            int[] dominant = GymSkinSampler.GetDominantBones(skin.sharedMesh);
            Transform[] bones = skin.bones;
            for (int i = 0; i < vertices.Length; i++)
            {
                int bone = i < dominant.Length ? dominant[i] : -1;
                Transform t = bone >= 0 && bone < bones.Length ? bones[bone] : null;
                Vector3 offset = vertices[i] - barCenter;
                float along = Vector3.Dot(offset, axis);
                Vector3 radial = offset - axis * along;
                float distance = radial.magnitude;
                int side = GymSkinSampler.IsInChain(t, leftHand) ? 0 : GymSkinSampler.IsInChain(t, rightHand) ? 1 : -1;
                if (side >= 0)
                {
                    handVerts[side]++;
                    handMin[side] = Mathf.Min(handMin[side], distance - barRadius);
                    // Fingers over the bar: hand skin within 3 cm of the shaft
                    // and above its axis (on top of the bar).
                    if (distance < barRadius + 0.03f && Vector3.Dot(radial, Vector3.up) > barRadius * 0.6f)
                        fingersOver[side]++;
                    continue;
                }
                if (GymSkinSampler.IsInChain(t, leftArm) || GymSkinSampler.IsInChain(t, rightArm) ||
                    (neck != null && t != null && t != neck && t.IsChildOf(neck)) ||
                    Mathf.Abs(along) > span) continue;
                // Hair and loose cloth far from their bone may overlap the bar.
                if (t != null && (vertices[i] - t.position).magnitude > squat.GetBarSupportMaxBoneDistance()) continue;
                float h = Vector3.Dot(offset, torsoUp);
                if (Mathf.Abs(h) >= barRadius) continue;
                sink = Mathf.Max(sink, Vector3.Dot(offset, back) + Mathf.Sqrt(barRadius * barRadius - h * h));
            }
        }
        UnityEngine.Object.Destroy(scratch);
        float backGap = float.IsNegativeInfinity(sink) ? float.PositiveInfinity : -sink;

        string line =
            $"{fighter.Identity}:{label} back={backGap:0.000} handL={handMin[0]:0.000} handR={handMin[1]:0.000} " +
            $"overL={fingersOver[0]} overR={fingersOver[1]} barR={barRadius:0.000} rest={squat.BarRestCorrection:0.000} " +
            $"clearL={squat.LeftGripClearance:0.000} clearR={squat.RightGripClearance:0.000}";
        Debug.Log("GYMCHAOS_SQUAT_BAR_GRIP_SAMPLE " + line);
        summary.Append(line).Append(' ');
        if (backGap < -BackPenetrationLimit) failures.Add(line + " bar_inside_back");
        if (backGap > BackGapLimit) failures.Add(line + " bar_floats_behind_back");
        for (int side = 0; side < 2; side++)
        {
            string name = side == 0 ? "left" : "right";
            if (handVerts[side] == 0) failures.Add(line + $" {name}_hand_not_skinned");
            if (handMin[side] > HandGapLimit) failures.Add(line + $" {name}_hand_gap");
            // A rigid fist (Goku) holds the bar through its closed fingers.
            if (!squat.UsesRigidFistGrip && handMin[side] < -HandPenetrationLimit)
                failures.Add(line + $" {name}_hand_inside_bar");
            if (fingersOver[side] < 10) failures.Add(line + $" {name}_fingers_not_over_bar");
        }
    }

    // Upper arm = the hand's grandparent chain root two levels above the
    // forearm (hand -> forearm(.001) -> forearm -> upper arm ...).
    private static Transform FindArmRoot(Transform hand)
    {
        Transform t = hand;
        while (t.parent != null)
        {
            string name = t.parent.name.ToLowerInvariant();
            if (name.Contains("shoulder") || name.Contains("clavicle") || name.Contains("spine") ||
                name.Contains("chest")) return t;
            t = t.parent;
        }
        return null;
    }

    private static Transform FindAttachedBar(Transform root)
    {
        foreach (GymExerciseStation candidate in UnityEngine.Object.FindObjectsByType<GymExerciseStation>(FindObjectsSortMode.None))
        {
            if (candidate == station)
            {
                Transform bar = candidate.SceneBarForVerification;
                if (bar != null && bar.IsChildOf(root)) return bar;
            }
        }
        return null;
    }

    private static void GetShaft(Transform bar, out Vector3 center, out Vector3 axis, out float radius)
    {
        Renderer longest = null;
        float longestLength = 0f;
        Vector3 longestAxis = Vector3.right;
        float longestRadius = 0.015f;
        foreach (Renderer renderer in bar.GetComponentsInChildren<Renderer>(true))
        {
            MeshFilter filter = renderer.GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null) continue;
            Vector3 size = Vector3.Scale(filter.sharedMesh.bounds.size, renderer.transform.lossyScale);
            size = new Vector3(Mathf.Abs(size.x), Mathf.Abs(size.y), Mathf.Abs(size.z));
            int longAxis = size.x >= size.y && size.x >= size.z ? 0 : size.y >= size.z ? 1 : 2;
            float length = size[longAxis];
            if (length <= longestLength) continue;
            longest = renderer;
            longestLength = length;
            Vector3 local = longAxis == 0 ? Vector3.right : longAxis == 1 ? Vector3.up : Vector3.forward;
            longestAxis = renderer.transform.TransformDirection(local).normalized;
            float a = size[(longAxis + 1) % 3];
            float b = size[(longAxis + 2) % 3];
            longestRadius = Mathf.Min(a, b) * 0.5f;
        }
        if (longest == null) throw new InvalidOperationException("bar has no shaft renderer");
        center = longest.bounds.center;
        axis = longestAxis;
        radius = longestRadius;
    }

    private static void Capture(string label)
    {
        string directory = Path.Combine(
            Directory.GetParent(Application.dataPath).Parent.FullName, "Logs", "verify", "squat-grip");
        Directory.CreateDirectory(directory);
        Transform subject = fighter.transform;
        Transform barRoot = FindAttachedBar(subject);
        Vector3 barCenter = subject.position + Vector3.up * 1.3f;
        if (barRoot != null) GetShaft(barRoot, out barCenter, out _, out _);
        Transform leftHand = squat.LeftHandBoneForVerification;
        var views = new List<(string, Vector3, Vector3, float)>
        {
            ("front", barCenter, subject.forward * 2.4f + Vector3.up * 0.25f, 50f),
            ("back", barCenter, -subject.forward * 2.0f + Vector3.up * 0.45f, 50f),
            ("side", barCenter, subject.right * 2.2f + Vector3.up * 0.15f, 50f),
            ("hand", leftHand != null ? leftHand.position : barCenter,
                -subject.forward * 0.55f + subject.right * -0.35f + Vector3.up * 0.25f, 40f),
            ("handfront", leftHand != null ? leftHand.position : barCenter,
                subject.forward * 0.5f + subject.right * -0.45f + Vector3.up * 0.15f, 40f),
            ("top", barCenter, Vector3.up * 1.6f - subject.forward * 0.25f, 50f),
            // Close three-quarter view from behind, between the plates, on
            // the line where the shaft meets the upper back.
            ("contact", barCenter, -subject.forward * 0.9f + subject.right * 0.35f + Vector3.up * 0.1f, 45f)
        };
        // Render only the fighter and its bar, so the rack posts never hide
        // the contact.
        const int CaptureLayer = 31;
        Dictionary<Transform, int> layers = new Dictionary<Transform, int>();
        foreach (Transform t in subject.GetComponentsInChildren<Transform>(true))
        {
            layers[t] = t.gameObject.layer;
            t.gameObject.layer = CaptureLayer;
        }
        foreach ((string view, Vector3 focus, Vector3 offset, float fov) in views)
        {
            GameObject host = new GameObject("Squat Grip Capture Camera");
            Camera camera = host.AddComponent<Camera>();
            camera.cullingMask = 1 << CaptureLayer;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.55f, 0.58f, 0.62f);
            camera.fieldOfView = fov;
            camera.nearClipPlane = 0.02f;
            Vector3 eye = focus + offset;
            camera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(focus - eye));
            RenderTexture target = new RenderTexture(560, 560, 24);
            camera.targetTexture = target;
            camera.Render();
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = target;
            Texture2D image = new Texture2D(560, 560, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, 560, 560), 0, 0);
            image.Apply();
            RenderTexture.active = previous;
            camera.targetTexture = null;
            File.WriteAllBytes(Path.Combine(directory, $"{fighter.Identity}-{label}-{view}.png"), image.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(image);
            target.Release();
            UnityEngine.Object.DestroyImmediate(target);
            UnityEngine.Object.DestroyImmediate(host);
        }
        foreach (KeyValuePair<Transform, int> entry in layers)
            if (entry.Key != null) entry.Key.gameObject.layer = entry.Value;
    }

    private static float FindSupportY(Vector3 point)
    {
        float support = point.y;
        float best = float.NegativeInfinity;
        foreach (RaycastHit hit in Physics.RaycastAll(point + Vector3.up * 1.5f, Vector3.down, 4f,
                     Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
        {
            if (hit.collider.GetComponentInParent<EnemyFighter>() != null ||
                hit.collider.GetComponentInParent<PlayerMovement>() != null ||
                hit.normal.y < 0.8f || hit.point.y > point.y + 0.05f) continue;
            if (hit.point.y > best)
            {
                best = hit.point.y;
                support = hit.point.y;
            }
        }
        return support;
    }

    private static void Finish()
    {
        bool captureOnly = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GYMCHAOS_SQUAT_GRIP_CAPTURE"));
        if (failures.Count > 0 && !captureOnly)
        {
            foreach (string failure in failures) Debug.LogError("GYMCHAOS_SQUAT_BAR_GRIP_FAILED " + failure);
            Exit(1);
            return;
        }
        Debug.Log((captureOnly ? "GYMCHAOS_SQUAT_BAR_GRIP_CAPTURED " : "GYMCHAOS_SQUAT_BAR_GRIP_OK ") +
                  summary.ToString().Trim() + (failures.Count > 0 ? " failures=" + failures.Count : ""));
        Exit(0);
    }

    private static void Exit(int code)
    {
        SessionState.EraseBool(RequestedKey);
        EditorApplication.update -= Tick;
        if (Application.isBatchMode) EditorApplication.Exit(code);
        else EditorApplication.isPlaying = false;
    }
}
#endif
