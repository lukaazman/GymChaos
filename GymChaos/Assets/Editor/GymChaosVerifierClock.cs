#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// Batch verifiers step play mode at a fixed frame rate so a slow machine
/// simulates the same as a fast one. Timeouts then count simulated frames
/// instead of wall-clock seconds.
/// </summary>
public static class GymChaosVerifierClock
{
    public const int FrameRate = 60;

    public static void BeginFixedStep() => Time.captureFramerate = FrameRate;

    public static void EndFixedStep() => Time.captureFramerate = 0;

    public static double Now => EditorApplication.isPlaying && Time.captureFramerate > 0
        ? Time.frameCount / (double)Time.captureFramerate
        : EditorApplication.timeSinceStartup;
}
#endif
