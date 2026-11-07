#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// The "LOADING GAME" overlay: shown as gameplay starts, the player held in
/// place (input off, same position) while it is up, then gone with control
/// back in the same spot. Logs GYMCHAOS_LOADING_SCREEN_OK.
/// </summary>
[InitializeOnLoad]
public static class GymChaosLoadingScreenVerifier
{
    private const string RequestedKey = "GymChaos.LoadingScreenVerificationRequested";
    private static double startedAt;
    private static int stage;
    private static bool finished;
    private static int resultCode = 1;
    private static PlayerMovement player;
    private static Vector3 heldAt;
    private static float worstDrift;
    private static int heldFrames;

    static GymChaosLoadingScreenVerifier()
    {
        if (SessionState.GetBool(RequestedKey, false)) Hook();
    }

    public static void Run()
    {
        finished = false;
        resultCode = 1;
        stage = 0;
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
            startedAt = GymChaosVerifierClock.Now;
            return;
        }
        if (state != PlayModeStateChange.EnteredEditMode) return;
        GymLoadingScreen.AllowInBatchForVerification = false;
        EditorApplication.update -= Tick;
        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        SessionState.EraseBool(RequestedKey);
        if (Application.isBatchMode) GymChaosVerifierExit.Exit(resultCode);
    }

    private static void Tick()
    {
        if (finished || !EditorApplication.isPlaying) return;
        double now = GymChaosVerifierClock.Now;
        try
        {
            if (now - startedAt > 120d) throw new TimeoutException($"stage {stage}");
            switch (stage)
            {
                case 0:
                    if (!GymOutdoorBuilder.IsBuilt || now - startedAt < 3d) return;
                    player = UnityEngine.Object.FindFirstObjectByType<PlayerMovement>();
                    if (player == null || !player.enabled) return;
                    GymLoadingScreen.AllowInBatchForVerification = true;
                    GymLoadingScreen.Begin(player);
                    if (!GymLoadingScreen.IsVisible) throw new InvalidOperationException("overlay did not show");
                    if (GameObject.Find("Loading Title") == null) throw new InvalidOperationException("LOADING GAME title missing");
                    heldAt = player.transform.position;
                    stage = 1;
                    break;
                case 1:
                    if (GymLoadingScreen.IsVisible)
                    {
                        if (player.enabled) throw new InvalidOperationException("player has control under the loading screen");
                        worstDrift = Mathf.Max(worstDrift, Vector3.Distance(player.transform.position, heldAt));
                        heldFrames++;
                        return;
                    }
                    if (!player.enabled) throw new InvalidOperationException("control not returned after loading");
                    float drift = Vector3.Distance(player.transform.position, heldAt);
                    if (heldFrames < 5) throw new InvalidOperationException($"overlay only {heldFrames} frames");
                    if (Mathf.Max(worstDrift, drift) > 0.05f)
                        throw new InvalidOperationException($"player moved {Mathf.Max(worstDrift, drift):F3} m while loading");
                    Debug.Log($"GYMCHAOS_LOADING_SCREEN_OK heldFrames={heldFrames} drift={Mathf.Max(worstDrift, drift):F3}");
                    Finish(0);
                    break;
            }
        }
        catch (Exception exception)
        {
            Debug.LogError("GYMCHAOS_LOADING_SCREEN_FAILED " + exception.Message);
            Finish(1);
        }
    }

    private static void Finish(int code)
    {
        finished = true;
        resultCode = code;
        GymChaosVerifierExit.Record(code);
        if (Application.isBatchMode) GymChaosVerifierExit.Exit(code);
        else EditorApplication.isPlaying = false;
    }
}
#endif
