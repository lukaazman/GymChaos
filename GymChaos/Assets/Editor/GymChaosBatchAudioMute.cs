using System;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Batch runs (Tools/Invoke-UnityCheck.ps1 sets GYMCHAOS_MUTE_AUDIO=1)
/// must not play music, sirens or engine sounds through the user's speakers.
/// The editor master mute silences every AudioSource without touching
/// AudioListener.volume or playback state, so verifiers that check audio
/// settings or sources still see their real values.
///
/// The user's own mute setting is written to a marker file before muting and
/// restored on quit. If a batch process is killed before it can restore, the
/// next normal Editor start finds the marker and restores it then.
/// </summary>
[InitializeOnLoad]
public static class GymChaosBatchAudioMute
{
    private const string Variable = "GYMCHAOS_MUTE_AUDIO";

    private static string MarkerPath => Path.Combine(
        Directory.GetParent(Application.dataPath).FullName, "Library", "GymChaosBatchAudioMute.previous");

    static GymChaosBatchAudioMute()
    {
        if (!IsRequested())
        {
            HealAfterKilledBatchRun();
            return;
        }

        // A stale marker already holds the user's value; never overwrite it with "muted".
        if (!File.Exists(MarkerPath))
        {
            File.WriteAllText(MarkerPath, EditorUtility.audioMasterMute ? "1" : "0");
        }
        EditorUtility.audioMasterMute = true;
        EditorApplication.playModeStateChanged -= KeepMuted;
        EditorApplication.playModeStateChanged += KeepMuted;
        EditorApplication.quitting -= Restore;
        EditorApplication.quitting += Restore;
    }

    public static bool IsRequested() =>
        string.Equals(Environment.GetEnvironmentVariable(Variable), "1", StringComparison.Ordinal);

    private static void KeepMuted(PlayModeStateChange change)
    {
        EditorUtility.audioMasterMute = true;
    }

    private static void Restore()
    {
        if (!File.Exists(MarkerPath))
        {
            return;
        }
        EditorUtility.audioMasterMute = File.ReadAllText(MarkerPath).Trim() == "1";
        File.Delete(MarkerPath);
    }

    private static void HealAfterKilledBatchRun()
    {
        if (!Application.isBatchMode && File.Exists(MarkerPath))
        {
            Restore();
            Debug.Log("GymChaos: restored the Editor audio mute setting left by an interrupted batch run.");
        }
    }
}
