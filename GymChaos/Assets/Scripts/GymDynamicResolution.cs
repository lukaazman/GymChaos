using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// WebGL only: drops the URP render scale in 0.1 steps (down to 0.7) while
/// the browser stays under 45 fps and raises it again above 57 fps. Logs
/// GYMCHAOS_RENDER_SCALE on every change.
/// </summary>
public sealed class GymDynamicResolution : MonoBehaviour
{
    private const float WindowSeconds = 2f;
    // Boot and first GLB loads hitch; judge the frame rate after them.
    private const float StartSeconds = 15f;
    private float windowStart;
    private int frames;

#if UNITY_WEBGL && !UNITY_EDITOR
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Install()
    {
        GameObject host = new GameObject("Dynamic Resolution");
        DontDestroyOnLoad(host);
        host.AddComponent<GymDynamicResolution>();
    }
#endif

    public static float NextScale(float scale, float fps)
    {
        float next = fps < 45f ? scale - 0.1f : fps > 57f ? scale + 0.1f : scale;
        return Mathf.Clamp(Mathf.Round(next * 10f) / 10f, 0.7f, 1f);
    }

    private void Update()
    {
        frames++;
        float now = Time.unscaledTime;
        // A hidden tab stops frames; the frame after it would read as low fps.
        if (now < StartSeconds || Time.unscaledDeltaTime > 0.5f)
        {
            windowStart = now;
            frames = 0;
            return;
        }
        if (now - windowStart < WindowSeconds) return;
        float fps = frames / (now - windowStart);
        frames = 0;
        windowStart = now;
        if (!(GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset asset)) return;
        float next = NextScale(asset.renderScale, fps);
        if (Mathf.Approximately(next, asset.renderScale)) return;
        asset.renderScale = next;
        Debug.Log($"GYMCHAOS_RENDER_SCALE scale={next:F1} fps={fps:F0}");
    }
}
