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

    // Long scenario verifiers (walks to cars, store trips, bus rides) run the
    // simulation faster than real time. Physics keeps its fixed step; only
    // fewer rendered/Update frames are spent per simulated second.
    public const float ScenarioTimeScale = 2f;

    // Game seconds at the scenario's 1x pace, for verifiers that run at
    // ScenarioTimeScale. Use game time for deadlines, never wall clock: a slow
    // runner caps each frame at Time.maximumDeltaTime, so wall-clock budgets
    // ran out while the simulation was still on its way.
    public static double ScenarioNow => Time.timeAsDouble / ScenarioTimeScale;

    public static void BeginFixedStep() => Time.captureFramerate = FrameRate;

    public static void EndFixedStep() => Time.captureFramerate = 0;

    public static double Now => EditorApplication.isPlaying && Time.captureFramerate > 0
        ? Time.frameCount / (double)Time.captureFramerate
        : EditorApplication.timeSinceStartup;
}
#endif
