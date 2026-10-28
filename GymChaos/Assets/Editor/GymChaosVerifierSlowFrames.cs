#if UNITY_EDITOR
using System;
using System.Threading;
using UnityEditor;

/// <summary>
/// CI runners (no GPU, shared CPU) run play mode at a few frames per second,
/// so each frame advances several fixed steps. GYMCHAOS_VERIFIER_SLOW_FRAME_MS
/// sleeps that long on every Editor update during play mode, which reproduces
/// CI-sized frames in a local verifier run.
/// </summary>
[InitializeOnLoad]
public static class GymChaosVerifierSlowFrames
{
    static GymChaosVerifierSlowFrames()
    {
        if (!int.TryParse(Environment.GetEnvironmentVariable("GYMCHAOS_VERIFIER_SLOW_FRAME_MS"),
                out int milliseconds) || milliseconds <= 0)
        {
            return;
        }
        UnityEngine.Debug.Log($"GYMCHAOS_VERIFIER_SLOW_FRAMES ms={milliseconds}");
        EditorApplication.update += () =>
        {
            if (EditorApplication.isPlaying) Thread.Sleep(milliseconds);
        };
    }
}
#endif
