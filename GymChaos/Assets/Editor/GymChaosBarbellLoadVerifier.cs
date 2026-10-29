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
        Debug.Log("GYMCHAOS_BARBELL_LOAD_OK");
        GymChaosBarbellLoadVerifier.Finish(0);
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
