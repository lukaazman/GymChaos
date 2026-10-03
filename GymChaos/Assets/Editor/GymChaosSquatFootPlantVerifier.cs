using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class GymChaosSquatFootPlantVerifier
{
    private const string RequestedKey = "GymChaos.SquatFootPlantVerificationRequested";
    private const string CanonicalSquatAssetPath =
        "Assets/BodyBuilders/enemies/anims/squat.fbx";
    private static double startTime;
    private static double squatStartTime;
    private static bool requested;
    private static bool workoutStarted;
    private static bool finished;
    private static int resultCode;
    private static int sampleCount;
    private static float worstGroundError;
    private static float worstFixedGroundError;
    private static float worstHorizontalSlip;
    private static float lowestMotion;
    private static bool canonicalClipVerified;
    private static string canonicalClipDetails;
    private static EnemyFighter fighter;
    private static SquatWorkoutController squat;
    private static readonly List<Transform> legBones = new List<Transform>();
    private static readonly List<Vector3> legScales = new List<Vector3>();

    static GymChaosSquatFootPlantVerifier()
    {
        if (!SessionState.GetBool(RequestedKey, false))
        {
            return;
        }

        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        EditorApplication.delayCall += ResumeAfterDomainReload;
    }

    [MenuItem("Tools/GymChaos/Run Squat Foot Plant Verification")]
    public static void Run()
    {
        ResetState();
        SessionState.SetBool(RequestedKey, true);
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        EditorApplication.isPlaying = true;
    }

    private static void ResetState()
    {
        startTime = 0d;
        squatStartTime = 0d;
        requested = false;
        workoutStarted = false;
        finished = false;
        resultCode = 1; GymChaosVerifierExit.Record(resultCode);
        sampleCount = 0;
        worstGroundError = 0f;
        worstFixedGroundError = 0f;
        worstHorizontalSlip = 0f;
        lowestMotion = 0f;
        canonicalClipVerified = false;
        canonicalClipDetails = "none";
        fighter = null;
        squat = null;
        legBones.Clear();
        legScales.Clear();
    }

    private static void ResumeAfterDomainReload()
    {
        if (!EditorApplication.isPlaying)
        {
            return;
        }

        startTime = EditorApplication.timeSinceStartup;
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
    }

    private static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            Time.timeScale = 1f;
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
        GymVisitorDirector director =
            UnityEngine.Object.FindAnyObjectByType<GymVisitorDirector>();
        if (director == null)
        {
            if (elapsed > 25d)
            {
                Fail("director_not_ready");
            }
            return;
        }

        if (!requested && elapsed > 2d)
        {
            director.SuspendVisitorSimulationForVerification();
            EnemyFighter[] fighters = UnityEngine.Object.FindObjectsByType<EnemyFighter>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int index = 0; index < fighters.Length; index++)
            {
                EnemyFighter candidate = fighters[index];
                if (candidate == null || candidate.IsDead ||
                    candidate.Identity != BodybuilderIdentity.JayCutler)
                {
                    continue;
                }

                SquatWorkoutController candidateSquat =
                    candidate.GetComponent<SquatWorkoutController>();
                if (candidateSquat == null || !candidateSquat.HasValidSquatRig ||
                    !candidateSquat.HasValidArmRig)
                {
                    continue;
                }

                GymExerciseStation station =
                    FindVerificationSquatStation(candidate.transform.position, 60f);
                GymVisitorAgent agent = candidate.GetComponent<GymVisitorAgent>();
                if (station == null || agent == null)
                {
                    continue;
                }

                if (!TryVerifyCanonicalSquatClip(
                        candidate, out string sourceDetails))
                {
                    Fail("canonical_squat_link_failed " + sourceDetails);
                    return;
                }
                canonicalClipVerified = true;
                canonicalClipDetails = sourceDetails;

                // Start the production squat controller directly after snapping
                // to the authored station pose. This verifier is meant to
                // isolate the foot solver; routing through the visitor approach
                // state makes the result depend on unrelated NavMesh/corridor
                // timing and can leave the test waiting forever before a squat
                // ever starts.
                candidate.SetVisitorSpawnPose(
                    station.EnemyPosition, station.EnemyRotation, true);
                // SetVisitorSpawnPose writes the Rigidbody first; mirror the
                // authored position onto the Transform before synchronizing so
                // Begin() cannot capture the pre-snap visual hierarchy.
                candidate.transform.SetPositionAndRotation(
                    station.EnemyPosition, station.EnemyRotation);
                // Begin() captures the bind foot anchors immediately. Flush
                // the Rigidbody snap first so those anchors cannot come from
                // the visitor's pre-verification world position.
                Physics.SyncTransforms();
                if (!candidateSquat.Begin(station, candidate, 6, 0.65f))
                {
                    continue;
                }

                fighter = candidate;
                squat = candidateSquat;
                CaptureLegTransformContract(candidate);
                requested = true;
                Debug.Log(
                    $"GYMCHAOS_SQUAT_FOOT_PLANT_TEST_STARTED enemy={candidate.Identity} " +
                    $"station={station.EquipmentName}");
                break;
            }

            if (!requested && elapsed > 12d)
            {
                Fail("no_valid_squat_candidate");
            }
        }

        if (!requested || fighter == null || squat == null)
        {
            return;
        }

        if (squat.IsActive)
        {
            // Begin() is called from the editor update loop, while the
            // controller evaluates the imported pose in LateUpdate. Do not
            // treat the controller's intentional pre-evaluation Infinity as
            // a foot-placement failure.
            float currentGroundError = squat.FootGroundError;
            if (float.IsNaN(currentGroundError) ||
                float.IsInfinity(currentGroundError))
            {
                return;
            }

            if (!workoutStarted)
            {
                workoutStarted = true;
                squatStartTime = EditorApplication.timeSinceStartup;
                lowestMotion = squat.CurrentMotion;
            }

            sampleCount++;
            lowestMotion = Mathf.Min(lowestMotion, squat.CurrentMotion);
            // Diagnostic mode: GYMCHAOS_SQUAT_CAPTURE=1 renders the deepest
            // point of the rep instead of judging the metrics.
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GYMCHAOS_SQUAT_CAPTURE")))
            {
                if (squat.CurrentMotion > 0.85f)
                {
                    CaptureSquatViews();
                    Debug.Log($"GYMCHAOS_SQUAT_CAPTURE_OK motion={squat.CurrentMotion:0.00} " +
                        $"ground={squat.FootGroundError:0.000} hipDrop={squat.CurrentHipDrop:0.000}");
                    finished = true;
                    resultCode = 0; GymChaosVerifierExit.Record(resultCode);
                    EditorApplication.isPlaying = false;
                }
                return;
            }
            worstGroundError = Mathf.Max(
                worstGroundError, squat.FootGroundError);
            worstFixedGroundError = Mathf.Max(
                worstFixedGroundError, squat.FixedFootGroundError);
            worstHorizontalSlip = Mathf.Max(
                worstHorizontalSlip,
                squat.LeftFootHorizontalSlip,
                squat.RightFootHorizontalSlip);

            if (worstGroundError > 0.015f)
            {
                Fail(
                    $"ground_error={worstGroundError:0.000} " +
                    $"left={squat.LeftFootGroundError:0.000} " +
                    $"right={squat.RightFootGroundError:0.000} " +
                    $"leftSlip={squat.LeftFootHorizontalSlip:0.000} " +
                    $"rightSlip={squat.RightFootHorizontalSlip:0.000}");
                return;
            }
            if (float.IsNaN(worstFixedGroundError) ||
                float.IsInfinity(worstFixedGroundError) ||
                worstFixedGroundError > 0.045f)
            {
                Fail(
                    $"fixed_ground_error={worstFixedGroundError:0.000} " +
                    $"left={squat.LeftFixedFootGroundError:0.000} " +
                    $"right={squat.RightFixedFootGroundError:0.000} " +
                    $"leftSigned={squat.LeftFixedFootGroundSigned:0.000} " +
                    $"rightSigned={squat.RightFixedFootGroundSigned:0.000} " +
                    $"leftOffset={squat.LeftMeshSoleOffsetForVerification:0.000} " +
                    $"rightOffset={squat.RightMeshSoleOffsetForVerification:0.000} " +
                    $"leftAnchor={squat.LeftFixedSoleAnchorWorldForVerification} " +
                    $"rightAnchor={squat.RightFixedSoleAnchorWorldForVerification}");
                return;
            }
            if (worstHorizontalSlip > 0.02f)
            {
                Fail(
                    $"horizontal_slip={worstHorizontalSlip:0.000} " +
                    $"leftSlip={squat.LeftFootHorizontalSlip:0.000} " +
                    $"rightSlip={squat.RightFootHorizontalSlip:0.000}");
                return;
            }
            for (int index = 0; index < legBones.Count; index++)
            {
                if (legBones[index] == null ||
                    Vector3.Distance(legBones[index].localScale, legScales[index]) > 0.0001f)
                {
                    Fail("leg_scale_changed");
                    return;
                }
            }
        }
        else if (workoutStarted &&
            EditorApplication.timeSinceStartup - squatStartTime > 4.8d)
        {
            if (!squat.IsComplete || sampleCount < 1)
            {
                Fail(
                    $"incomplete_cycle samples={sampleCount} lowestMotion={lowestMotion:0.000}");
                return;
            }

            finished = true;
            resultCode = 0; GymChaosVerifierExit.Record(resultCode);
            Debug.Log(
                $"GYMCHAOS_SQUAT_FOOT_PLANT_OK enemy={fighter.Identity} " +
                $"samples={sampleCount} maxGround={worstGroundError:0.000} " +
                $"maxFixedGround={worstFixedGroundError:0.000} " +
                $"maxSlip={worstHorizontalSlip:0.000} " +
                $"lowestMotion={lowestMotion:0.000} " +
                $"sourceLink={canonicalClipVerified} " +
                $"{canonicalClipDetails}");
            EditorApplication.isPlaying = false;
        }

        if (elapsed > 35d)
        {
            Fail(
                $"timeout requested={requested} started={workoutStarted} " +
                $"samples={sampleCount}");
        }
    }

    private static GymExerciseStation FindVerificationSquatStation(
        Vector3 position, float maxDistance)
    {
        GymExerciseStation preferred = null;
        float preferredDistance = maxDistance;
        GymExerciseStation[] stations = UnityEngine.Object.FindObjectsByType<
            GymExerciseStation>(FindObjectsSortMode.None);
        for (int i = 0; i < stations.Length; i++)
        {
            GymExerciseStation station = stations[i];
            if (station == null || !station.IsSquat || station.IsOccupied ||
                station.EquipmentName.IndexOf(
                    "smith", StringComparison.OrdinalIgnoreCase) < 0)
            {
                continue;
            }

            Vector3 offset = station.EnemyPosition - position;
            offset.y = 0f;
            float distance = offset.magnitude;
            if (distance < preferredDistance)
            {
                preferred = station;
                preferredDistance = distance;
            }
        }

        return preferred ?? GymExerciseStation.FindClosestSquat(position, maxDistance);
    }

    private static void CaptureLegTransformContract(EnemyFighter target)
    {
        Transform[] transforms = target.GetComponentsInChildren<Transform>(true);
        for (int index = 0; index < transforms.Length; index++)
        {
            Transform candidate = transforms[index];
            string lowerName = candidate.name.ToLowerInvariant();
            if (!lowerName.Contains("thigh") && !lowerName.Contains("upleg") &&
                !lowerName.Contains("shin") && !lowerName.Contains("lowerleg") &&
                !lowerName.Contains("foot"))
            {
                continue;
            }

            legBones.Add(candidate);
            legScales.Add(candidate.localScale);
        }
    }

    private static bool TryVerifyCanonicalSquatClip(
        EnemyFighter candidate, out string details)
    {
        MixamoScanRetargetAnimator animator =
            candidate != null
                ? candidate.GetComponent<MixamoScanRetargetAnimator>()
                : null;
        AnimationClip runtimeClip = animator != null
            ? animator.AuthoredSquatClipForVerification
            : null;
        string projectRoot = Directory.GetParent(Application.dataPath).FullName;
        string workspaceRoot = Directory.GetParent(projectRoot).FullName;
        string canonicalPath = Path.Combine(
            workspaceRoot, CanonicalSquatAssetPath.Replace('/', Path.DirectorySeparatorChar));
        string validationPath = Path.Combine(
            workspaceRoot,
            ".unlazy",
            "gymchaos-legacy-goal-20260924",
            "squat-source-validation.json");
        if (runtimeClip == null || !File.Exists(canonicalPath) ||
            !File.Exists(validationPath))
        {
            details = $"runtime={ClipName(runtimeClip)} " +
                $"canonicalFile={File.Exists(canonicalPath)} " +
                $"sourceValidation={File.Exists(validationPath)}";
            return false;
        }

        int runtimeBindings = AnimationUtility.GetCurveBindings(runtimeClip).Length;
        int runtimeKeys = CountCurveKeys(runtimeClip);
        string sourceValidation = File.ReadAllText(validationPath);
        string canonicalSha256 = ComputeSha256(canonicalPath);
        string runtimeAssetPath = AssetDatabase.GetAssetPath(runtimeClip);
        string runtimeDiskPath = Path.Combine(
            projectRoot,
            runtimeAssetPath.Replace('/', Path.DirectorySeparatorChar));
        string runtimeFileSha256 = File.Exists(runtimeDiskPath)
            ? ComputeSha256(runtimeDiskPath)
            : string.Empty;
        AssetImporter runtimeImporter = string.IsNullOrEmpty(runtimeAssetPath)
            ? null
            : AssetImporter.GetAtPath(runtimeAssetPath);
        string runtimeUserData = runtimeImporter != null
            ? runtimeImporter.userData
            : string.Empty;
        bool canonicalHashRecorded = Regex.IsMatch(
            sourceValidation,
            "\\\"source_sha256\\\"\\s*:\\s*\\\"" + canonicalSha256 + "\\\"",
            RegexOptions.IgnoreCase);
        bool runtimeAssetPathRecorded = string.Equals(
            runtimeAssetPath,
            "Assets/Resources/Characters/Enemies/jaycutler_authored.fbx",
            StringComparison.OrdinalIgnoreCase);
        bool runtimeSourcePathRecorded = runtimeUserData.IndexOf(
            "gymchaosCanonicalSquatSourcePath=" + CanonicalSquatAssetPath,
            StringComparison.OrdinalIgnoreCase) >= 0;
        bool runtimeSourceHashRecorded = runtimeUserData.IndexOf(
            "gymchaosCanonicalSquatSourceSha256=" + canonicalSha256,
            StringComparison.OrdinalIgnoreCase) >= 0;
        bool runtimeClipRecorded = runtimeUserData.IndexOf(
            "gymchaosCanonicalSquatRuntimeClip=" + runtimeClip.name,
            StringComparison.OrdinalIgnoreCase) >= 0;
        bool sourcePassed = Regex.IsMatch(
            sourceValidation, "\\\"pass\\\"\\s*:\\s*true",
            RegexOptions.IgnoreCase);
        bool sourceArmatureOnly = Regex.IsMatch(
            sourceValidation, "\\\"armature_count\\\"\\s*:\\s*1") &&
            Regex.IsMatch(sourceValidation, "\\\"mesh_count\\\"\\s*:\\s*0");
        bool sourceFrameRange = Regex.IsMatch(
            sourceValidation,
            "\\\"action_frame_ranges\\\"\\s*:\\s*\\[\\s*\\[\\s*1(?:\\.0+)?\\s*,\\s*69(?:\\.0+)?",
            RegexOptions.IgnoreCase);
        bool sourceScaleStable = Regex.IsMatch(
            sourceValidation,
            "\\\"non_identity_scale_paths\\\"\\s*:\\s*\\[\\s*\\]");
        bool runtimeName = string.Equals(
            runtimeClip.name, "squat", StringComparison.OrdinalIgnoreCase) ||
            runtimeClip.name.EndsWith("|squat", StringComparison.OrdinalIgnoreCase) ||
            runtimeClip.name.EndsWith("_squat", StringComparison.OrdinalIgnoreCase);
        bool runtimeDuration = runtimeClip.length >= 2.0f;
        bool runtimeCurveData = runtimeBindings > 0 && runtimeKeys > 0;
        string recordedRuntimeSha256 = ExtractJsonString(
            sourceValidation, "runtime_sha256");
        bool runtimeRoundTripPass = Regex.IsMatch(
            sourceValidation,
            "\"runtime_comparison\"\\s*:\\s*\\{.*?\"pass\"\\s*:\\s*true",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        bool runtimeComparisonMethod = Regex.IsMatch(
            sourceValidation,
            "\"comparison_method\"\\s*:\\s*\"Blender FBX round-trip world rotation delta\"",
            RegexOptions.IgnoreCase);
        bool runtimeComparisonPairs = Regex.IsMatch(
            sourceValidation,
            "\"matched_bone_pairs\"\\s*:\\s*20");
        bool runtimeComparisonFrames = Regex.IsMatch(
            sourceValidation,
            "\"compared_frames\"\\s*:\\s*69");
        bool runtimeComparisonCount = Regex.IsMatch(
            sourceValidation,
            "\"comparison_count\"\\s*:\\s*1380");
        float maxRotationDeltaDegrees = ExtractJsonFloat(
            sourceValidation, "max_rotation_delta_degrees");
        // Upper body must match the canonical clip; Tools/squat_contact_bake.py
        // may re-solve the leg chains within a bounded angle.
        float legRotationDeltaDegrees = ExtractJsonFloat(
            sourceValidation, "leg_max_rotation_delta_degrees");
        bool runtimeComparisonDelta = maxRotationDeltaDegrees >= 0f &&
            maxRotationDeltaDegrees <= 0.25f &&
            legRotationDeltaDegrees >= 0f && legRotationDeltaDegrees <= 25f;
        bool runtimeRoundTripHash = string.Equals(
            recordedRuntimeSha256,
            runtimeFileSha256,
            StringComparison.OrdinalIgnoreCase);
        bool runtimeRoundTrip = runtimeRoundTripPass && runtimeRoundTripHash &&
            runtimeComparisonMethod && runtimeComparisonPairs &&
            runtimeComparisonFrames && runtimeComparisonCount &&
            runtimeComparisonDelta;
        details = $"runtime={runtimeClip.name} " +
            $"resource={animator?.AuthoredAnimationResourcePath} " +
            $"canonical={MixamoScanRetargetAnimator.CanonicalSquatSourcePath} " +
            $"canonicalSha256={canonicalSha256} " +
            $"runtimeAsset={runtimeAssetPath} runtimeManifest=" +
            $"{runtimeAssetPathRecorded && runtimeSourcePathRecorded && runtimeSourceHashRecorded && runtimeClipRecorded} " +
            $"runtimeFileSha256={runtimeFileSha256} runtimeRoundTrip=" +
            $"{runtimeRoundTrip} maxRotationDelta={maxRotationDeltaDegrees:0.000000} " +
            $"sourcePass={sourcePassed} armatureOnly={sourceArmatureOnly} " +
            $"frames1to69={sourceFrameRange} scaleStable={sourceScaleStable} " +
            $"length={runtimeClip.length:0.000} frameRate={runtimeClip.frameRate:0.00} " +
            $"bindings={runtimeBindings} keys={runtimeKeys}";
        if (!canonicalHashRecorded || !runtimeAssetPathRecorded ||
            !runtimeSourcePathRecorded || !runtimeSourceHashRecorded ||
            !runtimeClipRecorded || !sourcePassed || !sourceArmatureOnly ||
            !sourceFrameRange ||
            !sourceScaleStable || !runtimeName || !runtimeDuration ||
            !runtimeCurveData || !runtimeRoundTrip ||
            !runtimeRoundTripPass || !runtimeRoundTripHash ||
            !runtimeComparisonMethod ||
            !runtimeComparisonPairs || !runtimeComparisonFrames ||
            !runtimeComparisonCount || !runtimeComparisonDelta)
        {
            return false;
        }

        Debug.Log("GYMCHAOS_SQUAT_SOURCE_LINK_OK " + details, candidate);
        return true;
    }

    private static string ComputeSha256(string path)
    {
        using (FileStream stream = File.OpenRead(path))
        using (SHA256 sha256 = SHA256.Create())
        {
            byte[] digest = sha256.ComputeHash(stream);
            return BitConverter.ToString(digest).Replace("-", string.Empty);
        }
    }

    private static string ExtractJsonString(string json, string propertyName)
    {
        Match match = Regex.Match(
            json,
            "\"" + Regex.Escape(propertyName) + "\"\\s*:\\s*\"([^\"]+)\"",
            RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value : string.Empty;
    }

    private static float ExtractJsonFloat(string json, string propertyName)
    {
        string value = ExtractJsonNumber(json, propertyName);
        return float.TryParse(
            value,
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out float parsed)
            ? parsed
            : -1f;
    }

    private static string ExtractJsonNumber(string json, string propertyName)
    {
        Match match = Regex.Match(
            json,
            "\"" + Regex.Escape(propertyName) + "\"\\s*:\\s*(-?[0-9]+(?:\\.[0-9]+)?(?:[eE][+-]?[0-9]+)?)",
            RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value : string.Empty;
    }

    private static int CountCurveKeys(AnimationClip clip)
    {
        int keys = 0;
        EditorCurveBinding[] bindings = AnimationUtility.GetCurveBindings(clip);
        for (int index = 0; index < bindings.Length; index++)
        {
            AnimationCurve curve = AnimationUtility.GetEditorCurve(
                clip, bindings[index]);
            keys += curve != null ? curve.length : 0;
        }

        EditorCurveBinding[] objectBindings =
            AnimationUtility.GetObjectReferenceCurveBindings(clip);
        for (int index = 0; index < objectBindings.Length; index++)
        {
            ObjectReferenceKeyframe[] curve =
                AnimationUtility.GetObjectReferenceCurve(
                    clip, objectBindings[index]);
            keys += curve != null ? curve.Length : 0;
        }
        return keys;
    }

    private static string ClipName(AnimationClip clip)
    {
        return clip == null ? "missing" : clip.name;
    }

    // Graphics runs only: saves front and side renders of the squatting
    // fighter to Logs/verify/squat-<view>.png for visual inspection.
    private static void CaptureSquatViews()
    {
        if (fighter == null ||
            SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
        {
            return;
        }
        Transform subject = fighter.transform;
        Vector3 focus = subject.position + Vector3.up * 1.2f;
        string directory = System.IO.Path.Combine(
            Directory.GetParent(Application.dataPath).Parent.FullName, "Logs", "verify");
        foreach ((string view, Vector3 offset) in new[]
                 {
                     ("front", subject.forward * 3.2f),
                     ("side", subject.right * 3.2f)
                 })
        {
            GameObject host = new GameObject("Squat Capture Camera");
            Camera camera = host.AddComponent<Camera>();
            camera.fieldOfView = 55f;
            camera.nearClipPlane = 0.05f;
            Vector3 eye = focus + offset + Vector3.up * 0.3f;
            camera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(focus - eye));
            RenderTexture target = new RenderTexture(640, 720, 24);
            camera.targetTexture = target;
            camera.Render();
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = target;
            Texture2D image = new Texture2D(640, 720, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, 640, 720), 0, 0);
            image.Apply();
            RenderTexture.active = previous;
            camera.targetTexture = null;
            File.WriteAllBytes(System.IO.Path.Combine(directory, $"squat-{view}.png"), image.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(image);
            target.Release();
            UnityEngine.Object.DestroyImmediate(target);
            UnityEngine.Object.DestroyImmediate(host);
        }
    }

    private static void Fail(string reason)
    {
        if (finished)
        {
            return;
        }
        CaptureSquatViews();

        finished = true;
        resultCode = 1; GymChaosVerifierExit.Record(resultCode);
        Debug.LogError(
            $"GYMCHAOS_SQUAT_FOOT_PLANT_FAILED reason={reason} " +
            $"samples={sampleCount} maxGround={worstGroundError:0.000} " +
            $"maxFixedGround={worstFixedGroundError:0.000} " +
            $"maxSlip={worstHorizontalSlip:0.000} lowestMotion={lowestMotion:0.000}");
        EditorApplication.isPlaying = false;
    }
}

