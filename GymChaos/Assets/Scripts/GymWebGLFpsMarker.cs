using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Logs GYMCHAOS_WEBGL_FPS (average FPS, average and p95 frame time) every
/// 5 s in WebGL development builds, or in any WebGL build opened with ?fps=1.
/// Tools/Invoke-WebGLSmoke.ps1 reads it from the browser console.
/// </summary>
public sealed class GymWebGLFpsMarker : MonoBehaviour
{
    private const float WindowSeconds = 5f;
    private readonly List<float> frameMs = new List<float>(1024);
    private float windowStart;

#if UNITY_WEBGL && !UNITY_EDITOR
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Install()
    {
        if (!Debug.isDebugBuild && !Application.absoluteURL.Contains("fps=1")) return;
        GameObject host = new GameObject("WebGL FPS Marker");
        DontDestroyOnLoad(host);
        host.AddComponent<GymWebGLFpsMarker>();
    }
#endif

    // Shows whether the browser ran on the real GPU or a software fallback.
    private void Start() =>
        Debug.Log($"GYMCHAOS_WEBGL_GPU device={SystemInfo.graphicsDeviceName} api={SystemInfo.graphicsDeviceVersion}");

    private void Update()
    {
        frameMs.Add(Time.unscaledDeltaTime * 1000f);
        if (Time.unscaledTime - windowStart < WindowSeconds) return;
        frameMs.Sort();
        float sum = 0f;
        for (int i = 0; i < frameMs.Count; i++) sum += frameMs[i];
        float averageMs = sum / frameMs.Count;
        float p95Ms = frameMs[Mathf.Min(frameMs.Count - 1, (int)(frameMs.Count * 0.95f))];
        Debug.Log($"GYMCHAOS_WEBGL_FPS avg={1000f / averageMs:F1} avgMs={averageMs:F1} " +
            $"p95Ms={p95Ms:F1} frames={frameMs.Count}");
        frameMs.Clear();
        windowStart = Time.unscaledTime;
    }
}
