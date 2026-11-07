using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// "LOADING GAME" overlay right after the world starts from the main menu.
/// The first seconds upload textures, compile shaders and finish runtime GLB
/// loads, which dropped the frame rate under 10 fps. While that happens the
/// player is held in place under a veil (like the death screen); once the
/// loads are done and the frame rate has settled the veil goes and control
/// returns in the same spot and the same world.
/// </summary>
public sealed class GymLoadingScreen : MonoBehaviour
{
    private const float MinimumSeconds = 0.75f;
    private const float MaximumSeconds = 20f;
    // Frames in a row at 30 fps or better before the game counts as loaded.
    private const int SettledFrames = 30;
    private const float SettledFrameSeconds = 1f / 30f;

    private static GymLoadingScreen instance;
    private PlayerMovement player;
    private float elapsed;
    private int settled;

    public static bool IsVisible => instance != null;

    // Batch verifiers keep their startup contract; the loading verifier opts in.
    internal static bool AllowInBatchForVerification;

    public static void Begin(PlayerMovement target)
    {
        if (instance != null || target == null || (Application.isBatchMode && !AllowInBatchForVerification)) return;
        GameObject root = new GameObject("Gym Loading Screen");
        instance = root.AddComponent<GymLoadingScreen>();
        instance.player = target;
        instance.Build();
        target.enabled = false;
        Debug.Log("GYMCHAOS_LOADING_SCREEN_SHOWN", instance);
    }

    private void Build()
    {
        Canvas canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        // Above the HUD and the pause menu: nothing is usable until loaded.
        canvas.sortingOrder = 300;
        CanvasScaler scaler = gameObject.AddComponent<CanvasScaler>();
        GymRuntimeSettings.ConfigureBalancedCanvasScaler(scaler);
        Image veil = GymRuntimeSettings.CreateImage("Loading Veil", transform, PersonaMenuStyle.Veil);
        GymRuntimeSettings.Stretch(veil.rectTransform);
        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        Text title = PersonaMenuStyle.CreateLabel(transform, "Loading Title",
            PersonaMenuStyle.LoadTitleFont(font), "LOADING GAME", 640f, 300f, 900f, 90f, 70,
            PersonaMenuStyle.Ink, TextAnchor.MiddleCenter);
        title.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -1.5f);
    }

    private void Update()
    {
        elapsed += Time.unscaledDeltaTime;
        settled = Time.unscaledDeltaTime <= SettledFrameSeconds ? settled + 1 : 0;
        bool loaded = RuntimeGlbSceneLoader.PendingCount == 0 && settled >= SettledFrames;
        if ((elapsed < MinimumSeconds || !loaded) && elapsed < MaximumSeconds) return;
        if (player != null)
        {
            player.enabled = true;
            player.CaptureCursorForGameplay();
        }
        Debug.Log($"GYMCHAOS_LOADING_SCREEN_DONE seconds={elapsed:F2} settledFrames={settled} timedOut={!loaded}", this);
        instance = null;
        Destroy(gameObject);
    }

    private void OnDestroy()
    {
        if (instance == this) instance = null;
    }
}
