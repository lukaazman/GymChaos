#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
internal static class GymChaosFaceCensorAutoRun
{
    private const string MarkerPath = "../../.tools/run-face-censor-verification";

    static GymChaosFaceCensorAutoRun()
    {
        // The external marker launches the verifier after a clean editor reload.
        EditorApplication.delayCall += TryRun;
    }

    private static void TryRun()
    {
        string marker = Path.GetFullPath(Path.Combine(Application.dataPath, MarkerPath));
        if (!File.Exists(marker))
        {
            return;
        }

        if (EditorApplication.isPlaying)
        {
            return;
        }

        File.Delete(marker);
        GymChaosPlayModeVerifier.Run();
    }
}
#endif
