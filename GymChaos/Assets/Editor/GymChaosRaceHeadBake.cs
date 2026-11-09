#if UNITY_EDITOR
using System.Collections;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// One-off bake (run with -Graphics): starts a chaos race so every racer is
/// seated, then writes each racer's headshot to Resources/UI/RaceHeads. The
/// committed PNGs are what the standings board shows afterwards.
/// </summary>
public static class GymChaosRaceHeadBake
{
    private const string RequestedKey = "GymChaos.RaceHeadBakeRequested";
    private static IEnumerator routine;

    [InitializeOnLoadMethod]
    private static void ResumeAfterReload()
    {
        if (SessionState.GetBool(RequestedKey, false)) Hook();
    }

    [MenuItem("Tools/GymChaos/Bake Race Heads")]
    public static void Run()
    {
        GymChaosVerifierSave.Neutralize();
        SessionState.SetBool(RequestedKey, true);
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        Hook();
        EditorApplication.isPlaying = true;
    }

    private static void Hook() => GymChaosVerifierHooks.Hook(Tick, Changed);

    private static void Changed(PlayModeStateChange change)
    {
        if (change == PlayModeStateChange.EnteredPlayMode) routine = Scenario();
        if (change != PlayModeStateChange.EnteredEditMode) return;
        EditorApplication.update -= Tick;
        GymChaosVerifierSave.Restore();
        if (Application.isBatchMode && SessionState.GetBool(RequestedKey, false)) EditorApplication.Exit(1);
    }

    private static void Tick()
    {
        if (!EditorApplication.isPlaying || routine == null) return;
        try
        {
            if (routine.MoveNext()) return;
        }
        catch (System.Exception exception)
        {
            Finish(1, "exception " + exception);
            return;
        }
        Finish(0, "baked");
    }

    private static IEnumerator Scenario()
    {
        float started = Time.realtimeSinceStartup;
        PlayerMovement player = null;
        while (player == null || !GymRaceCityCarver.CityReady() || GymRaceSign.Instance == null || GymRaceDirector.Instance == null)
        {
            if (Time.realtimeSinceStartup - started > 160f) throw new System.Exception("scene not ready");
            player = Object.FindFirstObjectByType<PlayerMovement>();
            yield return null;
        }
        GymRaceHeadshot.LiveOnly = true;
        GymRaceDirector director = GymRaceDirector.Instance;
        float limit = Time.time + 90f;
        while (director.BlockReasonForVerification != null)
        {
            if (Time.time > limit) throw new System.Exception("race blocked: " + director.BlockReasonForVerification);
            yield return null;
        }
        if (!director.StartRaceForVerification(player, true, GymRaceDirector.RaceMode.Chaos)) throw new System.Exception("race did not start");
        while (director.CurrentPhase != GymRaceDirector.Phase.Countdown)
        {
            if (Time.realtimeSinceStartup - started > 400f) throw new System.Exception("countdown never started");
            yield return null;
        }
        string folder = Path.Combine(Application.dataPath, "Resources", "UI", "RaceHeads");
        Directory.CreateDirectory(folder);
        int written = 0;
        foreach (GymRaceRacer racer in director.Racers)
        {
            // The player keeps a live portrait of their own character.
            if (racer.IsPlayer || racer.Headshot == null) continue;
            File.WriteAllBytes(Path.Combine(folder, GymRaceHeadshot.FileKey(racer.DisplayName) + ".png"), racer.Headshot.EncodeToPNG());
            written++;
        }
        if (written < 7) throw new System.Exception($"only {written}/7 headshots rendered");
    }

    private static void Finish(int code, string message)
    {
        Debug.Log($"GYMCHAOS_RACE_HEAD_BAKE_{(code == 0 ? "OK" : "FAILED")} {message}");
        SessionState.SetBool(RequestedKey, false);
        routine = null;
        EditorApplication.update -= Tick;
        GymChaosVerifierSave.Restore();
        if (Application.isBatchMode) EditorApplication.Exit(code);
        else EditorApplication.isPlaying = false;
    }
}
#endif
