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
    private Button saveButton;
    private Button saveAndReturnButton;
    private Button retryButton;
    private Text saveStatus;
    private Text failureMessage;
    private GameObject failurePage;
    private bool savingForReturn;

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

        if (instance.savingForReturn)
        {
            return;
        }

        if ((instance.optionsPanel != null && instance.optionsPanel.activeSelf) ||
            (instance.failurePage != null && instance.failurePage.activeSelf))
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
        // No title and no art: the frozen game stays visible under a navy
        // veil, and the buttons sit exactly where the start menu has them.
        Image veil = GymRuntimeSettings.CreateImage("Pause Veil", pausePage.transform,
            new Color(0.012f, 0.05f, 0.13f, 0.62f));
        GymRuntimeSettings.Stretch(veil.rectTransform);

        Font buttonFont = PersonaMenuStyle.LoadButtonFont(font);
        resumeButton = PersonaMenuStyle.CreatePrimaryButton(
            pausePage.transform, "Pause Play Button", buttonFont, "RESUME", Resume);
        PersonaMenuStyle.CreateSecondaryButton(
            pausePage.transform, "Pause Options Button", buttonFont, "SETTINGS", ShowOptions);
        PersonaMenuStyle.CreateTertiaryButton(
            pausePage.transform, "Pause Exit Button", buttonFont, "EXIT", Exit);
        // Character saves sit above the shared three-button stack so RESUME
        // keeps the exact start-menu position and default focus.
        saveButton = PersonaMenuStyle.CreateButton(
            pausePage.transform, "Pause Save Button", buttonFont, "SAVE GAME",
            214f, 420f, 330f, 68f, 56, -1.5f, SaveGame);
        saveAndReturnButton = PersonaMenuStyle.CreateButton(
            pausePage.transform, "Pause Save Return Button", buttonFont, "SAVE & RETURN TO MENU",
            300f, 508f, 510f, 68f, 52, -1.5f, SaveAndReturn);
        saveStatus = PersonaMenuStyle.CreateLabel(
            pausePage.transform, "Pause Save Status", buttonFont, string.Empty,
            640f, 420f, 460f, 60f, 34, PersonaMenuStyle.Gold, TextAnchor.MiddleLeft);

        failurePage = new GameObject("Save Failed Page", typeof(RectTransform));
        failurePage.transform.SetParent(transform, false);
        GymRuntimeSettings.Stretch(failurePage.GetComponent<RectTransform>());
        Image failureVeil = GymRuntimeSettings.CreateImage("Save Failed Veil", failurePage.transform,
            new Color(0.012f, 0.05f, 0.13f, 0.78f));
        GymRuntimeSettings.Stretch(failureVeil.rectTransform);
        PersonaMenuStyle.CreatePanel(failurePage.transform, "Panel", 836f, 440f, 720f, 430f, -1.5f,
            new Color(0.035f, 0.05f, 0.1f, 1f), PersonaMenuStyle.Accent);
        Text failureTitle = PersonaMenuStyle.CreateLabel(failurePage.transform, "Title",
            PersonaMenuStyle.LoadTitleFont(font), "SAVE FAILED", 836f, 280f, 640f, 70f, 56,
            PersonaMenuStyle.Accent, TextAnchor.MiddleLeft);
        failureTitle.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -1.5f);
        failureMessage = PersonaMenuStyle.CreateLabel(failurePage.transform, "Message", buttonFont,
            string.Empty, 836f, 372f, 640f, 100f, 30, PersonaMenuStyle.Ink, TextAnchor.UpperLeft);
        retryButton = PersonaMenuStyle.CreateButton(failurePage.transform, "Save Retry Button", buttonFont,
            "RETRY SAVE", 690f, 470f, 330f, 64f, 50, -1.5f, SaveAndReturn);
        PersonaMenuStyle.CreateButton(failurePage.transform, "Save Leave Button", buttonFont,
            "LEAVE WITHOUT SAVING", 790f, 552f, 520f, 64f, 50, -1.5f, LeaveWithoutSaving);
        PersonaMenuStyle.CreateButton(failurePage.transform, "Save Cancel Button", buttonFont,
            "BACK", 640f, 630f, 220f, 58f, 44, -1.5f, ShowPausePage);
        failurePage.SetActive(false);

        optionsPanel = GymRuntimeSettings.CreateOptionsPanel(transform, font, ShowPausePage);
        GymSessionService.SaveStatusChanged += HandleSaveStatus;
    }

    private void HandleSaveStatus(GymSaveStatus status, string message)
    {
        if (saveStatus == null)
        {
            return;
        }
        saveStatus.text = status == GymSaveStatus.Idle ? string.Empty : message.ToUpperInvariant();
        saveStatus.color = status == GymSaveStatus.Failed ? PersonaMenuStyle.Accent : PersonaMenuStyle.Gold;
    }

    private void SaveGame()
    {
        if (savingForReturn || !GymSessionService.HasActive)
        {
            return;
        }
        GymSessionService.RequestSave("manual", true);
    }

    // Leaving waits for the write: success returns to the menu, failure asks.
    private void SaveAndReturn()
    {
        if (savingForReturn || !GymSessionService.HasActive)
        {
            return;
        }
        savingForReturn = true;
        failurePage.SetActive(false);
        pausePage.SetActive(true);
        SetSessionButtonsInteractable(false);
        GymSessionService.RequestSave("return-to-menu", true, (succeeded, message) =>
        {
            savingForReturn = false;
            if (this == null)
            {
                return;
            }
            if (succeeded)
            {
                GymSessionService.ReturnToMenu();
                return;
            }
            SetSessionButtonsInteractable(true);
            failureMessage.text = message + "\nYour last successful save is intact.";
            pausePage.SetActive(false);
            failurePage.SetActive(true);
            Select(retryButton);
        });
    }

    private void LeaveWithoutSaving()
    {
        if (savingForReturn)
        {
            return;
        }
        GymSessionService.ReturnToMenu();
    }

    private void SetSessionButtonsInteractable(bool interactable)
    {
        foreach (Button button in pausePage.GetComponentsInChildren<Button>(true))
        {
            button.interactable = interactable;
        }
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
        failurePage.SetActive(false);
        bool hasCharacter = GymSessionService.HasActive;
        saveButton.gameObject.SetActive(hasCharacter);
        saveAndReturnButton.gameObject.SetActive(hasCharacter);
        saveStatus.gameObject.SetActive(hasCharacter);
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
        if (!GymSessionService.HasActive)
        {
            GymRuntimeSettings.ExitGame();
            return;
        }
        if (savingForReturn)
        {
            return;
        }
        // Explicit quit saves first; the quit callback remains a fallback only.
        savingForReturn = true;
        SetSessionButtonsInteractable(false);
        GymSessionService.RequestSave("exit", true, (succeeded, message) =>
        {
            savingForReturn = false;
            GymRuntimeSettings.ExitGame();
        });
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
        if (failurePage != null) failurePage.SetActive(false);
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
        GymSessionService.SaveStatusChanged -= HandleSaveStatus;
        Time.timeScale = 1f;
        if (instance == this)
        {
            instance = null;
        }
    }
}
