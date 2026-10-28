#if UNITY_EDITOR
using System;
using UnityEditor;

/// <summary>
/// Subscribes a verifier's editor tick and play-mode handler exactly once
/// (unsubscribe first, so a domain reload or a second Run never doubles them).
/// </summary>
public static class GymChaosVerifierHooks
{
    public static void Hook(EditorApplication.CallbackFunction tick, Action<PlayModeStateChange> playModeChanged)
    {
        EditorApplication.update -= tick;
        EditorApplication.update += tick;
        EditorApplication.playModeStateChanged -= playModeChanged;
        EditorApplication.playModeStateChanged += playModeChanged;
    }
}
#endif
