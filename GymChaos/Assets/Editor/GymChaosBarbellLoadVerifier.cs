#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Every barbell station (flat, incline, squat, preacher, deadlift) at its
/// heaviest load: the bar lies level and across the player (a flat bench bar
/// is not slanted), and plates for the selected weight sit on both sleeves,
/// the same count per side, outside the grip.
/// </summary>
[InitializeOnLoad]
public static class GymChaosBarbellLoadVerifier
{
    private const string Key = "GymChaos.BarbellLoadVerificationRequested";
    private static bool launched;

    static GymChaosBarbellLoadVerifier()
    {
        GymChaosVerifierHooks.Hook(Tick, OnPlayModeChanged);
    }

    [MenuItem("Tools/GymChaos/Run Barbell Load Verification")]
    public static void Run()
    {
        launched = false;
        GymChaosVerifierExit.Record(1);
        GymChaosVerifierSave.Neutralize();
        SessionState.SetBool(Key, true);
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        EditorApplication.isPlaying = true;
    }

    private static void OnPlayModeChanged(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredEditMode || !SessionState.GetBool(Key, false)) return;
        SessionState.EraseBool(Key);
        if (Application.isBatchMode) GymChaosVerifierExit.Exit(1);
    }

    private static void Tick()
    {
        if (!SessionState.GetBool(Key, false) || !EditorApplication.isPlaying || launched) return;
        PlayerMovement player = UnityEngine.Object.FindFirstObjectByType<PlayerMovement>();
        if (player == null || UnityEngine.Object.FindFirstObjectByType<GymExerciseStation>() == null) return;
        launched = true;
        new GameObject("Barbell load verifier").AddComponent<GymChaosBarbellLoadVerifierRunner>();
    }

    public static void Finish(int code)
    {
        GymChaosVerifierExit.Record(code);
        EditorApplication.isPlaying = false;
    }
}

[DefaultExecutionOrder(10000)]
public sealed class GymChaosBarbellLoadVerifierRunner : MonoBehaviour
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;

    private IEnumerator Start()
    {
        yield return new WaitForSeconds(2f);
        try
        {
            Verify();
        }
        catch (Exception exception)
        {
            Debug.LogError("GYMCHAOS_BARBELL_LOAD_FAIL " + exception.Message);
            GymChaosBarbellLoadVerifier.Finish(1);
            yield break;
        }
        var memberFailures = new List<string>();
        yield return VerifyMemberSquats(memberFailures);
        if (memberFailures.Count > 0)
        {
            Debug.LogError("GYMCHAOS_BARBELL_LOAD_FAIL " + string.Join(" | ", memberFailures));
            GymChaosBarbellLoadVerifier.Finish(1);
            yield break;
        }
        Debug.Log("GYMCHAOS_BARBELL_LOAD_OK");
        GymChaosBarbellLoadVerifier.Finish(0);
    }

    // Member squat loads: 20 kg bar only, then one 20 kg plate per side per
    // 40 kg. The body faces square to the rack for the whole rep, and the
    // plates ride on the sleeves from the collar outward.
    private static readonly int[] MemberWeights = { 20, 60, 100, 140, 200 };
    private static readonly int[] MemberPlatesPerSide = { 0, 1, 2, 3, 4 };
    // Root and bar stay square; the authored clips sway the hips a few degrees.
    private const float MemberYawLimit = 3f;
    private const float MemberHipYawLimit = 8f;
    // A plate rides a 2.2 m bar: its hole belongs on the shaft center line
    // (5 mm) with its face square to the bar (about 11 degrees).
    private const float MemberPlateRadialLimit = 0.005f;
    private const float MemberPlateTiltLimit = 0.02f;
    private SquatWorkoutController sampledSquat;
    private GymExerciseStation sampledStation;
    private EnemyFighter sampledFighter;
    private int sampleWeightIndex;
    private int samples;
    private float rootYaw, hipYaw, shoulderYaw, barYaw, deepest, handGap;
    private float radial, deepRadial, plateTilt;
    private Vector3 radialVec;
    private string plateError;
    private Vector3 pinPosition;
    private bool deepCaptured;

    private IEnumerator VerifyMemberSquats(List<string> failures)
    {
        GymVisitorDirector director = FindAnyObjectByType<GymVisitorDirector>();
        if (director != null) director.SuspendVisitorSimulationForVerification();
        var stations = new List<GymExerciseStation>();
        foreach (GymExerciseStation candidate in FindObjectsByType<GymExerciseStation>(FindObjectsSortMode.InstanceID))
        {
            if (candidate.IsSquat && candidate.HasAuthoredSquatBar && !candidate.IsOccupied) stations.Add(candidate);
        }
        var fighters = new List<EnemyFighter>();
        var seen = new HashSet<BodybuilderIdentity>();
        foreach (EnemyFighter candidate in FindObjectsByType<EnemyFighter>(FindObjectsInactive.Include, FindObjectsSortMode.InstanceID))
        {
            SquatWorkoutController controller = candidate.GetComponent<SquatWorkoutController>();
            MixamoScanRetargetAnimator animator = candidate.GetComponentInChildren<MixamoScanRetargetAnimator>(true);
            if (candidate.IsDead || seen.Contains(candidate.Identity) || controller == null ||
                !controller.HasValidSquatRig || !controller.HasValidArmRig ||
                animator == null || !animator.HasAuthoredSquatClip) continue;
            seen.Add(candidate.Identity);
            fighters.Add(candidate);
        }
        if (stations.Count == 0 || fighters.Count == 0)
        {
            failures.Add($"member squat: stations={stations.Count} fighters={fighters.Count}");
            yield break;
        }
        float previousMaxDelta = Time.maximumDeltaTime;
        Time.maximumDeltaTime = 0.05f;
        int runs = Mathf.Max(fighters.Count, MemberWeights.Length);
        for (int run = 0; run < runs; run++)
        {
            EnemyFighter fighter = fighters[run % fighters.Count];
            GymExerciseStation station = stations[run % stations.Count];
            sampleWeightIndex = run % MemberWeights.Length;
            GymExerciseStation.MemberSquatWeightForVerification = MemberWeights[sampleWeightIndex];
            fighter.gameObject.SetActive(true);
            // As GymVisitorAgent does before a set: no roam steering underneath.
            fighter.StopVisitorMovement();
            fighter.SetVisitorSpawnPose(station.EnemyPosition, station.EnemyRotation, true);
            fighter.transform.SetPositionAndRotation(station.EnemyPosition, station.EnemyRotation);
            Physics.SyncTransforms();
            SquatWorkoutController squat = fighter.GetComponent<SquatWorkoutController>();
            string label = $"{fighter.Identity}@{station.EquipmentName} weight={MemberWeights[sampleWeightIndex]}";
            if (!squat.Begin(station, fighter, 6, 2f))
            {
                failures.Add(label + " squat did not start");
                continue;
            }
            sampledStation = station;
            sampledFighter = fighter;
            samples = 0;
            rootYaw = hipYaw = shoulderYaw = barYaw = deepest = handGap = 0f;
            radial = deepRadial = plateTilt = 0f;
            radialVec = Vector3.zero;
            deepCaptured = false;
            plateError = null;
            pinPosition = fighter.transform.position;
            sampledSquat = squat;
            double until = Time.timeAsDouble + 2.4d;
            while (Time.timeAsDouble < until && squat.IsActive) yield return null;
            sampledSquat = null;
            string line = $"{label} samples={samples} deepest={deepest:F2} rootYaw={rootYaw:F1} " +
                $"handGap={handGap:F3} hipYaw={hipYaw:F1} shoulderYaw={shoulderYaw:F1} barYaw={barYaw:F1} " +
                $"radial={radial:F3} deepRadial={deepRadial:F3} tilt={plateTilt:F3} " +
                $"lastRadialVec={radialVec.ToString("F3")} plates={plateError ?? "ok"}";
            Debug.Log("GYMCHAOS_MEMBER_SQUAT_SAMPLE " + line);
            if (samples < 20 || deepest < 0.9f || rootYaw > MemberYawLimit || hipYaw > MemberHipYawLimit ||
                shoulderYaw > MemberHipYawLimit || handGap > 0.16f || barYaw > MemberYawLimit ||
                radial > MemberPlateRadialLimit || deepRadial > MemberPlateRadialLimit ||
                plateTilt > MemberPlateTiltLimit || plateError != null)
            {
                failures.Add(line);
            }
            Vector3 barCenter = station.EnemySquatBarCenter;
            Vector3 front = station.EnemyRotation * Vector3.forward;
            Capture($"member-squat-{MemberWeights[sampleWeightIndex]}kg",
                barCenter - front * 2.4f + station.EnemyRotation * Vector3.right * 1.2f + Vector3.up * 0.1f, barCenter);
            squat.Cancel();
            yield return null;
            Transform leftover = Field<Transform>(station, "loadedPlatesRoot");
            if (leftover != null) failures.Add(label + " plates left on the racked bar");
            // Any other set still in the scene is a leftover from an earlier
            // session: it renders at the old bar pose, which reads as discs
            // floating above the bar being lifted now.
            int sets = 0;
            foreach (Transform candidate in FindObjectsByType<Transform>(FindObjectsSortMode.InstanceID))
            {
                if (candidate.name == "Exercise Loaded Plates") sets++;
            }
            if (sets != 0) failures.Add(label + " loaded plate sets still in the scene=" + sets);
            fighter.gameObject.SetActive(false);
        }
        GymExerciseStation.MemberSquatWeightForVerification = 0;
        Time.maximumDeltaTime = previousMaxDelta;
    }

    // GymVisitorAgent holds a squatting member in its Squatting state; with
    // the visitor simulation suspended, pin the body to the stand instead.
    private void FixedUpdate()
    {
        if (sampledSquat == null) return;
        Rigidbody body = sampledFighter.GetComponent<Rigidbody>();
        if (body == null) return;
        body.linearVelocity = Vector3.zero;
        body.angularVelocity = Vector3.zero;
        body.position = pinPosition;
        body.rotation = sampledStation.EnemyRotation;
    }

    // Folded horizontal angle between two lines (0..90 degrees).

    private static float LineYaw(Vector3 a, Vector3 b)
    {
        a = Vector3.ProjectOnPlane(a, Vector3.up);
        b = Vector3.ProjectOnPlane(b, Vector3.up);
        if (a.sqrMagnitude < 1e-6f || b.sqrMagnitude < 1e-6f) return 90f;
        float angle = Vector3.Angle(a, b);
        return Mathf.Min(angle, 180f - angle);
    }

    private static Transform SquatBone(SquatWorkoutController squat, string name) =>
        (Transform)typeof(SquatWorkoutController).GetField(name, Flags).GetValue(squat);

    // Runs after the squat controller's LateUpdate has posed the rig and bar.
    private void LateUpdate()
    {
        if (sampledSquat == null || !sampledSquat.IsActive || !sampledStation.IsEnemySquatBarAttached) return;
        samples++;
        deepest = Mathf.Max(deepest, sampledSquat.CurrentMotion);
        Quaternion facing = sampledStation.EnemyRotation;
        Vector3 across = facing * Vector3.right;
        float yawNow = Vector3.Angle(
            Vector3.ProjectOnPlane(sampledFighter.transform.forward, Vector3.up), facing * Vector3.forward);
        rootYaw = Mathf.Max(rootYaw, yawNow);
        hipYaw = Mathf.Max(hipYaw, LineYaw(
            SquatBone(sampledSquat, "rightThigh").position - SquatBone(sampledSquat, "leftThigh").position, across));
        shoulderYaw = Mathf.Max(shoulderYaw, LineYaw(
            SquatBone(sampledSquat, "rightUpperArm").position - SquatBone(sampledSquat, "leftUpperArm").position, across));
        Transform bar = Field<Transform>(sampledStation, "sceneBar");
        Transform plates = Field<Transform>(sampledStation, "loadedPlatesRoot");
        Vector3 axis = ((Vector3)typeof(GymExerciseStation).GetMethod("GetBarAxis", BindingFlags.Static | BindingFlags.NonPublic)
            .Invoke(null, new object[] { bar })).normalized;
        barYaw = Mathf.Max(barYaw, LineYaw(axis, across));
        if (!deepCaptured && sampledSquat.CurrentMotion > 0.95f)
        {
            deepCaptured = true;
            Vector3 front = facing * Vector3.forward;
            Capture($"member-squat-{MemberWeights[sampleWeightIndex]}kg-deep",
                sampledStation.EnemySquatBarCenter - front * 1.6f + across * 0.9f + Vector3.up * 0.05f, sampledStation.EnemySquatBarCenter + across * 0.5f);
        }

        // Bar extent along its axis, without the loaded plates.
        float min = float.PositiveInfinity, max = float.NegativeInfinity;
        foreach (Renderer renderer in bar.GetComponentsInChildren<Renderer>(true))
        {
            if (plates != null && renderer.transform.IsChildOf(plates)) continue;
            Bounds b = renderer.bounds;
            float c = Vector3.Dot(b.center, axis);
            float e = Vector3.Dot(b.extents, new Vector3(Mathf.Abs(axis.x), Mathf.Abs(axis.y), Mathf.Abs(axis.z)));
            min = Mathf.Min(min, c - e);
            max = Mathf.Max(max, c + e);
        }
        int expected = MemberPlatesPerSide[sampleWeightIndex];
        float half = (max - min) * 0.5f;
        // Shaft line: the bar's longest renderer.
        Renderer shaft = null;
        foreach (Renderer renderer in bar.GetComponentsInChildren<Renderer>(true))
        {
            if (plates != null && renderer.transform.IsChildOf(plates)) continue;
            if (shaft == null || Mathf.Abs(Vector3.Dot(renderer.bounds.size, axis)) > Mathf.Abs(Vector3.Dot(shaft.bounds.size, axis))) shaft = renderer;
        }
        Vector3 center = shaft.bounds.center;
        center += axis * ((min + max) * 0.5f - Vector3.Dot(center, axis));
        // Overhand grip: both wrist bones stay within a hand length of the shaft.
        foreach (Transform hand in new[] { sampledSquat.LeftHandBoneForVerification, sampledSquat.RightHandBoneForVerification })
        {
            Vector3 toHand = hand.position - center;
            handGap = Mathf.Max(handGap, (toHand - axis * Vector3.Dot(toHand, axis)).magnitude);
        }
        // Shaft center line for the discs. Pick the shaft by its along-bar
        // extent (a disc is two orders of magnitude thinner along the bar, so
        // shear growth of a world AABB cannot swap them) and use the bar's own
        // axis, which the station forces level and across the lifter.
        Vector3 absAxisForShaft = new Vector3(Mathf.Abs(axis.x), Mathf.Abs(axis.y), Mathf.Abs(axis.z));
        Renderer shaftRenderer = null;
        float shaftAlong = 0f;
        foreach (Renderer candidate in bar.GetComponentsInChildren<Renderer>(true))
        {
            if (plates != null && candidate.transform.IsChildOf(plates)) continue;
            float along = Mathf.Abs(Vector3.Dot(candidate.bounds.extents, absAxisForShaft));
            if (along > shaftAlong)
            {
                shaftAlong = along;
                shaftRenderer = candidate;
            }
        }
        Vector3 shaftWorldAxis = axis;
        Vector3 shaftWorldCenter = shaftRenderer != null
            ? shaftRenderer.transform.TransformPoint(shaftRenderer.localBounds.center)
            : center;
        int left = 0, right = 0;
        float innerFace = float.PositiveInfinity, outerFace = 0f;
        if (plates != null)
        {
            foreach (Transform plate in plates)
            {
                Bounds bounds = GymShared.CombinedBounds(plate.GetComponentsInChildren<Renderer>(true));
                float offset = Vector3.Dot(bounds.center - center, axis);
                float thickness = Vector3.Dot(bounds.extents, new Vector3(Mathf.Abs(axis.x), Mathf.Abs(axis.y), Mathf.Abs(axis.z)));
                if (offset < 0f) left++; else right++;
                innerFace = Mathf.Min(innerFace, Mathf.Abs(offset) - thickness);
                outerFace = Mathf.Max(outerFace, Mathf.Abs(offset) + thickness);
                // The disc's drawn centre (not its pivot) has to sit on the
                // shaft center line at every depth of the rep: a template
                // whose mesh is offset from its pivot still has to hang on
                // the shaft visually.
                Vector3 fromShaft = bounds.center - shaftWorldCenter;
                float discRadial = (fromShaft - shaftWorldAxis * Vector3.Dot(fromShaft, shaftWorldAxis)).magnitude;
                radial = Mathf.Max(radial, discRadial);
                if (discRadial >= radial)
                {
                    radialVec = fromShaft;
                }
                if (sampledSquat.CurrentMotion > 0.9f) deepRadial = Mathf.Max(deepRadial, discRadial);
                // The disc's face normal is the thinnest axis of its own
                // geometry; it has to point along the bar or the shaft cannot
                // pass through the hole.
                Bounds discLocal = GymShared.GetBoundsRelativeTo(
                    plate, plate.GetComponentsInChildren<Renderer>(true));
                Vector3 discSize = discLocal.size;
                Vector3 discNormal = plate.TransformDirection(
                    discSize.x <= discSize.y && discSize.x <= discSize.z ? Vector3.right
                    : discSize.y <= discSize.z ? Vector3.up : Vector3.forward).normalized;
                plateTilt = Mathf.Max(plateTilt, 1f - Mathf.Abs(Vector3.Dot(discNormal, shaftWorldAxis)));
            }
        }
        // Sleeve of a 2.2 m bar: from the collar at ~0.6 of the half length
        // to the bar end; the stack starts at the collar, on the shaft line.
        bool placed = expected == 0 || (innerFace > half * 0.52f && innerFace < half * 0.7f &&
            outerFace <= half + 0.005f);
        if (left != expected || right != expected || !placed ||
            radial > MemberPlateRadialLimit || plateTilt > MemberPlateTiltLimit)
        {
            plateError ??= $"expected={expected} left={left} right={right} innerFace={innerFace:F3} " +
                $"outerFace={outerFace:F3} half={half:F3} radial={radial:F3} tilt={plateTilt:F3} " +
                $"motion={sampledSquat.CurrentMotion:F2}";
        }
    }

    // With a graphics device: Logs/agent/<name>.png of the loaded bar.
    private static void Capture(string name, Vector3 from, Vector3 at)
    {
        if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) return;
        var cameraObject = new GameObject("Barbell capture camera");
        Camera camera = cameraObject.AddComponent<Camera>();
        camera.transform.SetPositionAndRotation(from, Quaternion.LookRotation(at - from, Vector3.up));
        camera.fieldOfView = 60f;
        camera.nearClipPlane = 0.05f;
        var target = new RenderTexture(960, 540, 24);
        camera.targetTexture = target;
        camera.Render();
        RenderTexture previous = RenderTexture.active;
        RenderTexture.active = target;
        var picture = new Texture2D(960, 540, TextureFormat.RGB24, false);
        picture.ReadPixels(new Rect(0, 0, 960, 540), 0, 0);
        RenderTexture.active = previous;
        string directory = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "..", "Logs", "agent");
        System.IO.Directory.CreateDirectory(directory);
        System.IO.File.WriteAllBytes(System.IO.Path.Combine(directory, name + ".png"), picture.EncodeToPNG());
        camera.targetTexture = null;
        Destroy(target);
        Destroy(picture);
        Destroy(cameraObject);
    }

    private static T Field<T>(GymExerciseStation station, string name) =>
        (T)typeof(GymExerciseStation).GetField(name, Flags).GetValue(station);

    private void Verify()
    {
        Transform cameraTransform = FindFirstObjectByType<PlayerMovement>().playerCamera.transform;
        MethodInfo update = typeof(GymExerciseStation).GetMethod("UpdateSceneEquipment", Flags);
        MethodInfo barAxis = typeof(GymExerciseStation).GetMethod(
            "GetBarAxis", BindingFlags.Static | BindingFlags.NonPublic);
        MethodInfo platesPerSide = typeof(GymExerciseStation).GetMethod("GetPlatesPerSide", Flags);
        int checkedBars = 0;
        var failures = new List<string>();
        foreach (GymExerciseStation station in FindObjectsByType<GymExerciseStation>(FindObjectsSortMode.None))
        {
            Transform bar = Field<Transform>(station, "sceneBar");
            GymExerciseType type = Field<GymExerciseType>(station, "exerciseType");
            Debug.Log($"GYMCHAOS_BARBELL_STATION type={type} name={station.name} pos={station.transform.position} " +
                $"bar={(bar != null ? bar.name + "@" + bar.position : "none")}");
            if (bar == null) continue;
            if (!station.RequiresWeightSelection)
            {
                failures.Add($"{type}@{station.name} has a bar but no weight selection");
                continue;
            }
            int[] options = station.WeightOptions;
            station.SelectWeight(options[options.Length - 1]);
            station.BeginSession(cameraTransform);
            update.Invoke(station, new object[] { 0f });
            Physics.SyncTransforms();

            Quaternion playerRotation = Field<Quaternion>(station, "playerRotation");
            Vector3 axis = ((Vector3)barAxis.Invoke(null, new object[] { bar })).normalized;
            Vector3 forward = Vector3.ProjectOnPlane(playerRotation * Vector3.forward, Vector3.up).normalized;
            string label = $"{type}@{station.name}";
            float along = Mathf.Abs(Vector3.Dot(axis, forward));
            if (along > 0.08f || Mathf.Abs(axis.y) > 0.05f)
            {
                failures.Add($"{label} bar not across/level: axis={axis} playerForward={forward}");
            }

            int expected = ((List<int>)platesPerSide.Invoke(station, null)).Count;
            Transform plates = Field<Transform>(station, "loadedPlatesRoot");
            Bounds barBounds = GymShared.CombinedBounds(bar.GetComponentsInChildren<Renderer>(true));
            int left = 0, right = 0;
            float innerMost = float.PositiveInfinity, outerMost = 0f;
            float halfLength = Vector3.Dot(barBounds.extents,
                new Vector3(Mathf.Abs(axis.x), Mathf.Abs(axis.y), Mathf.Abs(axis.z)));
            if (plates != null)
            {
                foreach (Transform plate in plates)
                {
                    float offset = Vector3.Dot(plate.position - barBounds.center, axis);
                    if (offset < 0f) left++; else right++;
                    innerMost = Mathf.Min(innerMost, Mathf.Abs(offset));
                    outerMost = Mathf.Max(outerMost, Mathf.Abs(offset));
                }
            }
            string load = $"{label} weight={station.SelectedWeight} expectedPerSide={expected} " +
                $"left={left} right={right} innerMost={innerMost:F2} outerMost={outerMost:F2} barHalf={halfLength:F2}";
            Debug.Log("GYMCHAOS_BARBELL_LOAD_SAMPLE " + load + $" axis={axis} forward={forward}");
            // Grip is inside +-0.45 m; plates sit on the sleeves beyond it,
            // from the collar outward, never past the bar end (the deadlift's
            // 400 kg stack keeps its own authored layout).
            if (expected == 0 || left != expected || right != expected || innerMost < 0.45f ||
                (type != GymExerciseType.Deadlift && outerMost > halfLength - 0.03f))
            {
                failures.Add(load);
            }
            // Player view: the station's camera pose on the player's spot.
            station.GetCameraPose(out Vector3 cameraLocal, out Quaternion cameraLocalRotation);
            Vector3 eye = Field<Vector3>(station, "playerPosition") + playerRotation * cameraLocal;
            Vector3 look = playerRotation * cameraLocalRotation * Vector3.forward;
            if (type == GymExerciseType.FlatBenchPress || type == GymExerciseType.InclineBenchPress)
            {
                // Lying on the bench the bar stays well inside the view at
                // the chest and at lockout, not at the bottom edge.
                float bottomAngle = Vector3.Angle(look, barBounds.center - eye);
                update.Invoke(station, new object[] { 1f });
                Vector3 lockout = GymShared.CombinedBounds(bar.GetComponentsInChildren<Renderer>(true)).center;
                float topAngle = Vector3.Angle(look, lockout - eye);
                update.Invoke(station, new object[] { 0f });
                Debug.Log($"GYMCHAOS_BARBELL_VIEW {label} chestAngle={bottomAngle:F1} lockoutAngle={topAngle:F1}");
                if (bottomAngle > 20f || topAngle > 20f)
                {
                    failures.Add($"{label} bar near the view edge: chest={bottomAngle:F1} lockout={topAngle:F1}");
                }
            }
            Capture($"barbell-{type}", eye, eye + look);
            Capture($"barbell-{type}-load", barBounds.center - forward * 2.6f + Vector3.up * 0.4f, barBounds.center);
            Capture($"barbell-{type}-side", barBounds.center + axis * 3f + Vector3.up * 0.4f, barBounds.center);
            station.EndSession();
            checkedBars++;
        }
        if (checkedBars < 5) failures.Add($"only {checkedBars} barbell stations found");
        if (failures.Count > 0) throw new InvalidOperationException(string.Join(" | ", failures));
        Debug.Log($"GYMCHAOS_BARBELL_LOAD_STATIONS count={checkedBars}");
    }
}
#endif
