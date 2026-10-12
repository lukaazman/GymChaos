#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// Runs a verifier from a neutral progression save. A negative-reputation
/// save in the project's PlayerPrefs auto-aggros every visitor, which blocks
/// roaming, arrivals and departures, so results would depend on whatever the
/// last play session left behind. The original save is kept in
/// project-scoped verifier prefs (it survives the play-mode domain reload)
/// and restored when the verifier exits.
///
/// Batch verifier runs (-executeMethod GymChaos*Verifier.*) are neutralised
/// once per Unity process at the first domain load, before the verifier
/// itself runs, so verifiers that write their own test save still can.
/// </summary>
[InitializeOnLoad]
public static class GymChaosVerifierSave
{
    private const string ProgressionSaveKey = "GymChaos.Progression.v1";
    private const string BackupKey = "GymChaos.VerifierSave.Backup";
    private const string BackupPresentKey = "GymChaos.VerifierSave.BackupPresent";
    private const string ProcessNeutralizedKey = "GymChaos.VerifierSave.ProcessNeutralized";

    static GymChaosVerifierSave()
    {
        EditorApplication.playModeStateChanged -= PlayModeChanged;
        EditorApplication.playModeStateChanged += PlayModeChanged;
        EditorApplication.quitting -= Restore;
        EditorApplication.quitting += Restore;
        if (Application.isBatchMode && IsVerifierCommandLine() &&
            !SessionState.GetBool(ProcessNeutralizedKey, false))
        {
            SessionState.SetBool(ProcessNeutralizedKey, true);
            Neutralize();
        }
        else if (!Application.isBatchMode && !EditorApplication.isPlayingOrWillChangePlaymode)
        {
            // A killed batch run never reached its restore. Put the player's
            // save back as soon as an interactive Editor loads.
            Restore();
        }
    }

    private static bool IsVerifierCommandLine()
    {
        string[] arguments = System.Environment.GetCommandLineArgs();
        for (int i = 0; i + 1 < arguments.Length; i++)
        {
            if (string.Equals(arguments[i], "-executeMethod", System.StringComparison.OrdinalIgnoreCase) &&
                arguments[i + 1].StartsWith("GymChaos", System.StringComparison.Ordinal) &&
                arguments[i + 1].Contains("Verifier"))
            {
                return true;
            }
        }
        return false;
    }

    public static void Neutralize()
    {
        if (!GymChaosVerifierPrefs.HasKey(BackupPresentKey))
        {
            GymChaosVerifierPrefs.SetBool(BackupPresentKey, PlayerPrefs.HasKey(ProgressionSaveKey));
            GymChaosVerifierPrefs.SetString(BackupKey, PlayerPrefs.GetString(ProgressionSaveKey, string.Empty));
        }
        PlayerPrefs.DeleteKey(ProgressionSaveKey);
        PlayerPrefs.Save();
    }

    public static void Restore()
    {
        if (!GymChaosVerifierPrefs.HasKey(BackupPresentKey))
        {
            return;
        }
        if (GymChaosVerifierPrefs.GetBool(BackupPresentKey, false))
        {
            PlayerPrefs.SetString(ProgressionSaveKey, GymChaosVerifierPrefs.GetString(BackupKey, string.Empty));
        }
        else
        {
            PlayerPrefs.DeleteKey(ProgressionSaveKey);
        }
        PlayerPrefs.Save();
        GymChaosVerifierPrefs.DeleteKey(BackupKey);
        GymChaosVerifierPrefs.DeleteKey(BackupPresentKey);
    }

    private static void PlayModeChanged(PlayModeStateChange change)
    {
        // Batch runs restore on exit; a verifier may enter play mode again.
        if (change == PlayModeStateChange.EnteredEditMode && !Application.isBatchMode)
        {
            Restore();
        }
    }
}
#endif
