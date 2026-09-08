using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Persistent runtime pause surface. It is created only for normal gameplay,
/// so batch-mode verifiers keep their existing startup contract.
/// </summary>
[DefaultExecutionOrder(-10)]
public sealed class GymPauseMenu : MonoBehaviour
{
    private static GymPauseMenu instance;
    private PlayerMovement player;
    private CanvasGroup canvasGroup;
    private GameObject pausePage;
    private GameObject optionsPanel;
    private Button resumeButton;

    public static bool IsVisible =>
        instance != null && instance.canvasGroup != null && instance.canvasGroup.blocksRaycasts;

    public static GymPauseMenu CreateForScene(PlayerMovement targetPlayer)
    {
        if (instance != null)
        {
            instance.player = targetPlayer;
            return instance;
        }
        GameObject root = new GameObject("Gym Pause Menu");
        instance = root.AddComponent<GymPauseMenu>();
        instance.player = targetPlayer;
        instance.BuildInterface();
        instance.HideMenu();
        Debug.Log("GYMCHAOS_PAUSE_MENU_READY", instance);
        return instance;
    }

    public static void Open(PlayerMovement source)
    {
        if (instance == null || IsVisible || GymStartScreen.IsMenuVisible)
        {
            return;
        }
        instance.player = source;
        instance.ShowPausePage();
    }

    internal static void HandlePauseInput()
    {
        if (instance == null || !IsVisible || !ReadPauseToggle())
        {
            return;
        }

        if (instance.optionsPanel != null && instance.optionsPanel.activeSelf)
        {
            instance.ShowPausePage();
        }
        else
        {
            instance.Resume();
        }
    }

    private void BuildInterface()
    {
        GymRuntimeSettings.EnsureEventSystem();
        Canvas canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 200;
        CanvasScaler scaler = gameObject.AddComponent<CanvasScaler>();
        GymRuntimeSettings.ConfigureBalancedCanvasScaler(scaler);
        gameObject.AddComponent<GraphicRaycaster>();
        canvasGroup = gameObject.AddComponent<CanvasGroup>();

        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        pausePage = new GameObject("Pause Page", typeof(RectTransform));
        pausePage.transform.SetParent(transform, false);
        GymRuntimeSettings.Stretch(pausePage.GetComponent<RectTransform>());
        Image veil = GymRuntimeSettings.CreateImage("Pause Veil", pausePage.transform,
            new Color(0.008f, 0.018f, 0.038f, 0.94f));
        GymRuntimeSettings.Stretch(veil.rectTransform);
        Image rule = GymRuntimeSettings.CreateImage("Pause Rule", pausePage.transform,
            new Color(0.98f, 0.34f, 0.13f, 1f));
        GymRuntimeSettings.SetAnchors(rule.rectTransform,
            new Vector2(0.36f, 0.68f), new Vector2(0.64f, 0.68f), 0f, -1f, 0f, -1f);
        rule.raycastTarget = false;
        GymRuntimeSettings.CreateText("Pause Heading", pausePage.transform, font,
            "PAUSED", 48, new Color(0.91f, 0.94f, 0.98f, 1f), FontStyle.Bold,
            TextAnchor.MiddleCenter, new Vector2(0.2f, 0.70f), new Vector2(0.8f, 0.80f));

        resumeButton = GymRuntimeSettings.CreateButton("Pause Play Button", pausePage.transform,
            font, "PLAY", new Color(0.98f, 0.34f, 0.13f, 1f),
            new Vector2(0.34f, 0.50f), new Vector2(0.66f, 0.59f), Resume);
        GymRuntimeSettings.CreateButton("Pause Options Button", pausePage.transform,
            font, "OPTIONS", new Color(0.07f, 0.105f, 0.15f, 1f),
            new Vector2(0.34f, 0.38f), new Vector2(0.66f, 0.47f), ShowOptions);
        GymRuntimeSettings.CreateButton("Pause Exit Button", pausePage.transform,
            font, "EXIT", new Color(0.07f, 0.105f, 0.15f, 1f),
            new Vector2(0.34f, 0.26f), new Vector2(0.66f, 0.35f), Exit);
        optionsPanel = GymRuntimeSettings.CreateOptionsPanel(transform, font, ShowPausePage);
    }

    private void ShowPausePage()
    {
        Time.timeScale = 0f;
        if (player != null)
        {
            player.SetCursorCaptured(false);
        }
        pausePage.SetActive(true);
        optionsPanel.SetActive(false);
        SetMenuVisible(true);
        Select(resumeButton);
    }

    private void ShowOptions()
    {
        Time.timeScale = 0f;
        pausePage.SetActive(false);
        optionsPanel.SetActive(true);
        SetMenuVisible(true);
        Select(optionsPanel.GetComponentInChildren<Selectable>(true));
    }

    private void Resume()
    {
        Time.timeScale = 1f;
        HideMenu();
        if (player != null)
        {
            player.CaptureCursorForGameplay();
        }
    }

    private void Exit()
    {
        GymRuntimeSettings.ExitGame();
    }

    private void HideMenu()
    {
        if (canvasGroup == null)
        {
            return;
        }
        canvasGroup.alpha = 0f;
        canvasGroup.interactable = false;
        canvasGroup.blocksRaycasts = false;
        if (pausePage != null) pausePage.SetActive(false);
        if (optionsPanel != null) optionsPanel.SetActive(false);
    }

    private void SetMenuVisible(bool visible)
    {
        canvasGroup.alpha = visible ? 1f : 0f;
        canvasGroup.interactable = visible;
        canvasGroup.blocksRaycasts = visible;
        if (visible)
        {
            Cursor.lockState = CursorLockMode.None;
        }
        Cursor.visible = visible;
    }

    private static void Select(Selectable selectable)
    {
        if (EventSystem.current != null && selectable != null)
        {
            EventSystem.current.SetSelectedGameObject(selectable.gameObject);
        }
    }

    private static bool ReadPauseToggle()
    {
#if ENABLE_INPUT_SYSTEM
        return UnityEngine.InputSystem.Keyboard.current != null &&
            UnityEngine.InputSystem.Keyboard.current.escapeKey.wasPressedThisFrame;
#else
        return Input.GetKeyDown(KeyCode.Escape);
#endif
    }

    private void OnDestroy()
    {
        Time.timeScale = 1f;
        if (instance == this)
        {
            instance = null;
        }
    }
}
