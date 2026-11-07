#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// The scene side of a join: a world snapshot from the host sets the clock and
/// breaks the listed glass in the guest's scene, and a snapshot captured from
/// that scene reads back the same. A Join context never owns the world section.
/// </summary>
public static class GymChaosNetWorldApplyVerifier
{
    private const string RequestedKey = "GymChaos.NetWorldApplyVerificationRequested";

    private static double startedAt;
    private static bool finished;
    private static int resultCode = 1;

    [InitializeOnLoadMethod]
    private static void ResumeAfterReload()
    {
        if (SessionState.GetBool(RequestedKey, false)) Hook();
    }

    public static void Run()
    {
        finished = false;
        resultCode = 1;
        GymChaosVerifierExit.Record(resultCode);
        GymChaosVerifierSave.Neutralize();
        SessionState.SetBool(RequestedKey, true);
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        Hook();
        EditorApplication.isPlaying = true;
    }

    private static void Hook() => GymChaosVerifierHooks.Hook(Tick, OnPlayModeStateChanged);

    private static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            GymChaosVerifierClock.BeginFixedStep();
            startedAt = GymChaosVerifierClock.Now;
            return;
        }
        if (state != PlayModeStateChange.EnteredEditMode) return;
        EditorApplication.update -= Tick;
        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        SessionState.EraseBool(RequestedKey);
        if (Application.isBatchMode) GymChaosVerifierExit.Exit(resultCode);
    }

    private static void Tick()
    {
        if (finished || !EditorApplication.isPlaying) return;
        double now = GymChaosVerifierClock.Now;
        if (now - startedAt > 120d) { Finish(1, "scene never became ready"); return; }
        GlassShatterPanel[] panels = UnityEngine.Object.FindObjectsByType<GlassShatterPanel>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        if (GymTimeOfDay.Instance == null || panels.Length == 0 || now - startedAt < 4d) return;
        try
        {
            GlassShatterPanel target = null;
            foreach (GlassShatterPanel panel in panels) if (!panel.IsShattered) { target = panel; break; }
            if (target == null) throw new InvalidOperationException("no intact glass panel in the scene");
            string id = target.StableId;

            GymNetWorldSnapshot host = new GymNetWorldSnapshot
            {
                worldTime = 0.83f, worldDay = 6, brokenPanels = new[] { id },
                fixerIssueIds = Array.Empty<string>(), fixerIssueSince = Array.Empty<float>()
            };
            Require(GymSessionService.ApplyWorldSnapshot(host), "snapshot not applied");
            Require(Mathf.Abs(GymTimeOfDay.Instance.Time01 - 0.83f) < 0.02f && GymTimeOfDay.Instance.CurrentDay == 6,
                $"clock not set: {GymTimeOfDay.Instance.Time01} day {GymTimeOfDay.Instance.CurrentDay}");
            Require(target.IsShattered, "listed glass was not broken");

            GymNetWorldSnapshot back = GymSessionService.CaptureWorldSnapshot();
            Require(Array.IndexOf(back.brokenPanels, id) >= 0 && back.worldDay == 6, "captured snapshot lost the applied state");

            GymSessionService.SetContext(GymSaveContext.Join);
            Require((GymSessionService.AllowedSections & GymSaveSections.World) == 0, "Join owns the world section");
            GymSessionService.SetContext(GymSaveContext.Solo);
            Debug.Log("GYMCHAOS_NET_WORLD_APPLY_OK clock=0.83 day=6 glass=broken roundtrip=ok");
            Finish(0, null);
        }
        catch (Exception exception)
        {
            Finish(1, exception.Message);
        }
    }

    private static void Finish(int code, string error)
    {
        if (error != null) Debug.LogError("GYMCHAOS_NET_WORLD_APPLY_FAILED " + error);
        finished = true;
        resultCode = code;
        GymChaosVerifierExit.Record(code);
        GymChaosVerifierClock.EndFixedStep();
        if (Application.isBatchMode) GymChaosVerifierExit.Exit(code);
        else EditorApplication.isPlaying = false;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
#endif
