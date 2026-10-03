#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class GymChaosMirrorTemporalVerifier
{
    private const string RequestedKey =
        "GymChaos.MirrorTemporalVerificationRequested";
    private static double started;
    private static int lastFrame;
    private static int mirrorIndex;
    private static int phaseFrame;
    private static int maxRefreshGap;
    private static bool finished;
    private static int resultCode;
    private static PlanarGymMirror[] mirrors;

    static GymChaosMirrorTemporalVerifier()
    {
        if (SessionState.GetBool(RequestedKey, false))
        {
            Hook();
        }
    }

    [MenuItem("Tools/GymChaos/Run Mirror Temporal Verification")]
    public static void Run()
    {
        SessionState.SetBool(RequestedKey, true);
        ResetState();
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        Hook();
        EditorApplication.isPlaying = true;
    }

    private static void Hook()
    {
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
        EditorApplication.playModeStateChanged -= PlayModeChanged;
        EditorApplication.playModeStateChanged += PlayModeChanged;
    }

    private static void ResetState()
    {
        started = EditorApplication.timeSinceStartup;
        lastFrame = -1;
        mirrorIndex = 0;
        phaseFrame = 0;
        maxRefreshGap = 0;
        finished = false;
        resultCode = 1; GymChaosVerifierExit.Record(resultCode);
        mirrors = null;
    }

    private static void PlayModeChanged(PlayModeStateChange change)
    {
        if (change == PlayModeStateChange.EnteredPlayMode)
        {
            started = EditorApplication.timeSinceStartup;
            Time.timeScale = 1f;
        }
        if (change != PlayModeStateChange.EnteredEditMode)
        {
            return;
        }
        EditorApplication.update -= Tick;
        EditorApplication.playModeStateChanged -= PlayModeChanged;
        SessionState.EraseBool(RequestedKey);
        if (Application.isBatchMode)
        {
            GymChaosVerifierExit.Exit(resultCode);
        }
    }

    private static void Tick()
    {
        if (finished || !EditorApplication.isPlaying)
        {
            return;
        }
        try
        {
            if (EditorApplication.timeSinceStartup - started > 120d)
            {
                throw new InvalidOperationException(
                    "Mirror temporal verification timed out.");
            }
            if (Time.frameCount == lastFrame)
            {
                return;
            }
            lastFrame = Time.frameCount;

            if (mirrors == null || mirrors.Length < 3)
            {
                mirrors = UnityEngine.Object.FindObjectsByType<PlanarGymMirror>(
                    FindObjectsInactive.Exclude, FindObjectsSortMode.None);
                if (mirrors.Length < 3)
                {
                    return;
                }
            }
            if (mirrorIndex >= mirrors.Length)
            {
                if (maxRefreshGap > 1)
                {
                    throw new InvalidOperationException(
                        $"Visible mirror refresh gap exceeded one frame: " +
                        $"maxGap={maxRefreshGap} mirrors={mirrors.Length}.");
                }
                Debug.Log(
                    $"GYMCHAOS_MIRROR_TEMPORAL_OK mirrors={mirrors.Length} " +
                    $"sampleFrames={mirrors.Length * 100} maxRefreshGap={maxRefreshGap} actualRender=True");
                Finish(0);
                return;
            }

            PlanarGymMirror mirror = mirrors[mirrorIndex];
            Camera camera = mirror != null
                ? mirror.SourceCameraForVerification
                : null;
            if (mirror == null || camera == null)
            {
                throw new InvalidOperationException(
                    $"Mirror {mirrorIndex} is missing its source camera.");
            }

            Vector3 point = mirror.PlanePointForVerification;
            Vector3 normal = mirror.PlaneNormal;
            Vector3 tangent = Vector3.Cross(Vector3.up, normal).normalized;
            Vector3 viewPosition = point + normal * (3.2f + Mathf.Sin(phaseFrame * 0.07f) * 0.5f)
                + tangent * Mathf.Sin(phaseFrame * 0.11f);
            camera.transform.SetPositionAndRotation(
                viewPosition,
                Quaternion.LookRotation(point - viewPosition, Vector3.up));
            phaseFrame++;
            if (phaseFrame > 4)
            {
                int gap = Time.frameCount - mirror.LastRenderedFrameForVerification;
                maxRefreshGap = Mathf.Max(maxRefreshGap, gap);
            }
            if (phaseFrame >= 104)
            {
                Debug.Log(
                    $"GYMCHAOS_MIRROR_TEMPORAL_SAMPLE name={mirror.name} " +
                    $"lastRefresh={mirror.LastRefreshFrameForVerification} " +
                    $"frame={Time.frameCount} maxGap={maxRefreshGap}",
                    mirror);
                mirrorIndex++;
                phaseFrame = 0;
            }
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            Finish(1);
        }
    }

    private static void Finish(int code)
    {
        if (finished)
        {
            return;
        }
        finished = true;
        resultCode = code; GymChaosVerifierExit.Record(resultCode);
        SessionState.EraseBool(RequestedKey);
        EditorApplication.update -= Tick;
        EditorApplication.isPlaying = false;
    }
}
#endif
