using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Modal controls for the local playlist and the free SoundCloud Widget flow.
/// </summary>
public sealed class GymRadioSoundCloudPopup : MonoBehaviour
{
    private static GymRadioSoundCloudPopup activePopup;
    private GymRadio radio;
    private CanvasGroup canvasGroup;
    private GameObject page;
    private Text sourceText;
    private Text statusText;
    private Text localLabel;
    private Button musicButton;
    private Text musicLabel;
    private Button localButton;
    private Button openSoundCloudButton;
    private Button loadPlaylistButton;
    private Button playPauseButton;
    private Button nextButton;
    private Text playPauseLabel;
    private InputField playlistUrlInput;
    private Font font;
    private bool built;
    private float previousTimeScale = 1f;
    private CursorLockMode previousCursorLockState = CursorLockMode.None;
    private bool previousCursorVisible = true;
    private bool cursorWasCaptured;
    private bool popupStateActive;

    public bool IsVisible => canvasGroup != null && canvasGroup.blocksRaycasts;
    public static bool IsAnyVisible => activePopup != null && activePopup.IsVisible;

    public static GymRadioSoundCloudPopup CreateFor(GymRadio owner)
    {
        if (owner == null)
        {
            return null;
        }

        // Reuse the existing owner popup. Rebuilding the Canvas on every
        // interaction creates a second full-screen veil/page for one radio;
        // both surfaces then remain visible until Unity processes Destroy,
        // which is the overlapping radio-menu bug.
        if (activePopup != null && activePopup.radio == owner)
        {
            activePopup.BuildInterface();
            return activePopup;
        }
        GymRadioSoundCloudPopup[] ownerPopups =
            FindObjectsByType<GymRadioSoundCloudPopup>(
                FindObjectsInactive.Include);
        for (int i = 0; i < ownerPopups.Length; i++)
        {
            GymRadioSoundCloudPopup existing = ownerPopups[i];
            if (existing != null && existing.radio == owner)
            {
                activePopup = existing;
                existing.BuildInterface();
                return existing;
            }
        }

        // There is only ever one modal radio surface. A stale popup can be
        // left hidden after a scene/UI rebuild; remove that UI object without
        // touching the owner or its physical model.
        if (activePopup != null && activePopup.radio != owner)
        {
            GymRadioSoundCloudPopup stalePopup = activePopup;
            stalePopup.Close();
            Destroy(stalePopup.gameObject);
        }

        // Keep popup lifecycle independent from the physical radio/model.
        GameObject popupObject = new GameObject("Radio SoundCloud Popup");
        GymRadioSoundCloudPopup popup = popupObject.AddComponent<GymRadioSoundCloudPopup>();
        popup.radio = owner;
        popup.BuildInterface();
        popup.HideImmediate();
        return popup;
    }

    public void Open()
    {
        if (radio == null || !radio.CanInteractWithPlayer)
        {
            return;
        }

        BuildInterface();

        if (activePopup != null && activePopup != this)
        {
            GymRadioSoundCloudPopup stalePopup = activePopup;
            stalePopup.Close();
            Destroy(stalePopup.gameObject);
        }
        activePopup = this;
        transform.SetAsLastSibling();

        if (popupStateActive)
        {
            SetVisible(true);
            RefreshFromRadio();
            Select(localButton != null ? localButton : closeButton);
            return;
        }

        previousTimeScale = Time.timeScale;
        previousCursorLockState = Cursor.lockState;
        previousCursorVisible = Cursor.visible;
        PlayerMovement player = FindAnyObjectByType<PlayerMovement>();
        cursorWasCaptured = player != null && player.CursorCaptured;
        popupStateActive = true;

        Time.timeScale = 0f;
        player?.SetCursorCaptured(false);
        SetVisible(true);
        RefreshFromRadio();
        Select(localButton != null ? localButton : closeButton);
    }

    public void Close()
    {
        if (!popupStateActive && !IsVisible)
        {
            if (activePopup == this)
            {
                activePopup = null;
            }
            return;
        }

        if (IsVisible)
        {
            SetVisible(false);
        }

        RestorePopupState();
        if (activePopup == this)
        {
            activePopup = null;
        }
    }

    public void RefreshFromRadio()
    {
        if (!built || radio == null)
        {
            return;
        }

        if (!radio.CanInteractWithPlayer)
        {
            Close();
            return;
        }

        sourceText.text = radio.IsSoundCloudMode
            ? "SOURCE  //  SOUNDCLOUD"
            : "SOURCE  //  LOCAL PLAYLIST";
        statusText.text = radio.GetSoundCloudUiStatus();
        statusText.color = radio.IsSoundCloudError
            ? new Color(1f, 0.43f, 0.31f, 1f)
            : new Color(0.61f, 0.69f, 0.78f, 1f);
        localLabel.text = radio.IsLocalMusicEnabled
            ? "LOCAL PLAYLIST  //  ON"
            : "LOCAL PLAYLIST  //  OFF";
        musicLabel.text = radio.IsMusicEnabled ? "TURN MUSIC OFF" : "TURN MUSIC ON";
        playPauseLabel.text = radio.IsSoundCloudWidgetPlaying
            ? "PAUSE"
            : "PLAY";
        if (playlistUrlInput != null && !playlistUrlInput.isFocused &&
            !string.Equals(playlistUrlInput.text, radio.SoundCloudPlaylistUrl,
                System.StringComparison.Ordinal))
        {
            playlistUrlInput.text = radio.SoundCloudPlaylistUrl;
        }
        openSoundCloudButton.interactable = true;
        loadPlaylistButton.interactable = true;
        playPauseButton.interactable = true;
        nextButton.interactable = true;
    }

    private void BuildInterface()
    {
        if (built)
        {
            return;
        }

        GymRuntimeSettings.EnsureEventSystem();
        font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        Canvas canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 180;
        CanvasScaler scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.55f;
        gameObject.AddComponent<GraphicRaycaster>();
        canvasGroup = gameObject.AddComponent<CanvasGroup>();

        page = new GameObject("Radio SoundCloud Page", typeof(RectTransform));
        page.transform.SetParent(transform, false);
        GymRuntimeSettings.Stretch(page.GetComponent<RectTransform>());
        Image veil = GymRuntimeSettings.CreateImage(
            "Radio Popup Veil", page.transform,
            new Color(0.008f, 0.018f, 0.038f, 0.90f));
        GymRuntimeSettings.Stretch(veil.rectTransform);

        GameObject surface = new GameObject(
            "Radio Popup Surface", typeof(RectTransform));
        surface.transform.SetParent(page.transform, false);
        RectTransform surfaceRect = surface.GetComponent<RectTransform>();
        GymRuntimeSettings.SetAnchors(
            surfaceRect, new Vector2(0.23f, 0.08f), new Vector2(0.77f, 0.92f));
        Image surfaceImage = GymRuntimeSettings.CreateImage(
            "Radio Popup Surface Ink", surface.transform,
            new Color(0.035f, 0.065f, 0.10f, 0.99f));
        GymRuntimeSettings.Stretch(surfaceImage.rectTransform);

        Image rule = GymRuntimeSettings.CreateImage(
            "Radio Popup Rule", surface.transform,
            new Color(0.98f, 0.34f, 0.13f, 1f));
        GymRuntimeSettings.SetAnchors(
            rule.rectTransform, new Vector2(0.12f, 0.92f),
            new Vector2(0.88f, 0.92f), 0f, -1f, 0f, -1f);
        rule.raycastTarget = false;

        GymRuntimeSettings.CreateText(
            "Radio Popup Heading", surface.transform, font,
            "RADIO", 28,
            new Color(0.91f, 0.94f, 0.98f, 1f), FontStyle.Bold,
            TextAnchor.MiddleCenter, new Vector2(0.10f, 0.84f),
            new Vector2(0.90f, 0.91f));
        sourceText = GymRuntimeSettings.CreateText(
            "Radio Popup Source", surface.transform, font,
            "SOURCE  //  LOCAL PLAYLIST", 14,
            new Color(0.98f, 0.34f, 0.13f, 1f), FontStyle.Bold,
            TextAnchor.MiddleCenter, new Vector2(0.10f, 0.77f),
            new Vector2(0.90f, 0.83f));
        statusText = GymRuntimeSettings.CreateText(
            "Radio Popup Status", surface.transform, font,
            "Paste a SoundCloud playlist URL", 14,
            new Color(0.61f, 0.69f, 0.78f, 1f), FontStyle.Normal,
            TextAnchor.MiddleCenter, new Vector2(0.08f, 0.66f),
            new Vector2(0.92f, 0.75f));

        GymRuntimeSettings.CreateText(
            "Radio Playlist URL Heading", surface.transform, font,
            "SOUNDCLOUD PLAYLIST LINK", 12,
            new Color(0.98f, 0.34f, 0.13f, 1f), FontStyle.Bold,
            TextAnchor.MiddleLeft, new Vector2(0.12f, 0.59f),
            new Vector2(0.88f, 0.64f));

        GameObject inputObject = new GameObject(
            "Radio SoundCloud URL Input",
            typeof(RectTransform), typeof(Image), typeof(InputField));
        inputObject.transform.SetParent(surface.transform, false);
        GymRuntimeSettings.SetAnchors(
            inputObject.GetComponent<RectTransform>(),
            new Vector2(0.12f, 0.51f), new Vector2(0.88f, 0.58f));
        Image inputImage = inputObject.GetComponent<Image>();
        inputImage.color = new Color(0.015f, 0.03f, 0.055f, 1f);
        playlistUrlInput = inputObject.GetComponent<InputField>();
        playlistUrlInput.lineType = InputField.LineType.SingleLine;
        playlistUrlInput.characterLimit = 512;

        Text placeholder = GymRuntimeSettings.CreateText(
            "Radio SoundCloud URL Placeholder", inputObject.transform, font,
            "Paste playlist, share or short link", 13,
            new Color(0.43f, 0.50f, 0.60f, 1f), FontStyle.Italic,
            TextAnchor.MiddleLeft, new Vector2(0.025f, 0f),
            new Vector2(0.975f, 1f));
        Text inputText = GymRuntimeSettings.CreateText(
            "Radio SoundCloud URL Text", inputObject.transform, font,
            string.Empty, 13, new Color(0.91f, 0.94f, 0.98f, 1f),
            FontStyle.Normal, TextAnchor.MiddleLeft,
            new Vector2(0.025f, 0f), new Vector2(0.975f, 1f));
        inputText.supportRichText = false;
        playlistUrlInput.placeholder = placeholder;
        playlistUrlInput.textComponent = inputText;

        openSoundCloudButton = GymRuntimeSettings.CreateButton(
            "Radio Open SoundCloud Button", surface.transform, font,
            "BROWSE", new Color(0.07f, 0.105f, 0.15f, 1f),
            new Vector2(0.12f, 0.42f), new Vector2(0.49f, 0.48f),
            OpenSoundCloudFromUi);
        loadPlaylistButton = GymRuntimeSettings.CreateButton(
            "Radio Load Playlist Button", surface.transform, font,
            "CONNECT", new Color(0.98f, 0.34f, 0.13f, 1f),
            new Vector2(0.51f, 0.42f), new Vector2(0.88f, 0.48f),
            LoadSoundCloudPlaylistFromUi);
        playPauseButton = GymRuntimeSettings.CreateButton(
            "Radio Widget Play Pause Button", surface.transform, font,
            "PLAY", new Color(0.98f, 0.34f, 0.13f, 1f),
            new Vector2(0.12f, 0.34f), new Vector2(0.49f, 0.40f),
            ToggleSoundCloudPlaybackFromUi);
        playPauseLabel = playPauseButton.GetComponentInChildren<Text>(true);
        nextButton = GymRuntimeSettings.CreateButton(
            "Radio Widget Next Button", surface.transform, font,
            "NEXT", new Color(0.07f, 0.105f, 0.15f, 1f),
            new Vector2(0.51f, 0.34f), new Vector2(0.88f, 0.40f),
            NextSoundCloudTrackFromUi);

        localButton = GymRuntimeSettings.CreateButton(
            "Radio Local Button", surface.transform, font,
            "LOCAL PLAYLIST  //  ON",
            new Color(0.07f, 0.105f, 0.15f, 1f),
            new Vector2(0.14f, 0.25f), new Vector2(0.86f, 0.31f),
            ToggleLocalPlaybackFromUi);
        localLabel = localButton.GetComponentInChildren<Text>(true);
        musicButton = GymRuntimeSettings.CreateButton(
            "Radio Music Toggle Button", surface.transform, font,
            "TURN MUSIC OFF", new Color(0.98f, 0.34f, 0.13f, 1f),
            new Vector2(0.14f, 0.16f), new Vector2(0.86f, 0.22f),
            ToggleMusicPlaybackFromUi);
        musicLabel = musicButton.GetComponentInChildren<Text>(true);
        closeButton = GymRuntimeSettings.CreateButton(
            "Radio Close Button", surface.transform, font, "CLOSE",
            new Color(0.07f, 0.105f, 0.15f, 1f),
            new Vector2(0.36f, 0.065f), new Vector2(0.64f, 0.125f), Close);
        built = true;
    }

    private Button closeButton;

    private void SetVisible(bool visible)
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

    private void ToggleLocalPlaybackFromUi()
    {
        if (radio == null)
        {
            return;
        }
        radio.ToggleLocalPlayback();
        LogPhysicalRadioAction("local-playlist");
    }

    private void ToggleMusicPlaybackFromUi()
    {
        if (radio == null)
        {
            return;
        }
        radio.ToggleMusicPlayback();
        LogPhysicalRadioAction("music");
    }

    private void OpenSoundCloudFromUi()
    {
        if (radio == null)
        {
            return;
        }
        radio.OpenSoundCloudSite();
        LogPhysicalRadioAction("soundcloud-open");
    }

    private void LoadSoundCloudPlaylistFromUi()
    {
        if (radio == null)
        {
            return;
        }
        radio.LoadSoundCloudPlaylistUrl(
            playlistUrlInput != null ? playlistUrlInput.text : string.Empty);
        LogPhysicalRadioAction("soundcloud-load");
    }

    private void ToggleSoundCloudPlaybackFromUi()
    {
        if (radio == null)
        {
            return;
        }
        radio.ToggleSoundCloudWidgetPlayback();
        LogPhysicalRadioAction("soundcloud-play-pause");
    }

    private void NextSoundCloudTrackFromUi()
    {
        if (radio == null)
        {
            return;
        }
        radio.NextSoundCloudWidgetTrack();
        LogPhysicalRadioAction("soundcloud-next");
    }

    private void LogPhysicalRadioAction(string action)
    {
        if (radio == null)
        {
            return;
        }

        bool modelAlive = radio.HasPhysicalModel;
        Debug.Log(
            $"GYMCHAOS_RADIO_POPUP_ACTION action={action} " +
            $"physicalRadioAlive={radio != null} modelAlive={modelAlive}", this);
        if (!modelAlive)
        {
            Debug.LogError(
                $"GYMCHAOS_RADIO_MODEL_LOST_DURING_POPUP action={action}", this);
        }
    }

    private void HideImmediate()
    {
        if (canvasGroup == null)
        {
            return;
        }
        canvasGroup.alpha = 0f;
        canvasGroup.interactable = false;
        canvasGroup.blocksRaycasts = false;
    }

    private static void Select(Selectable selectable)
    {
        if (EventSystem.current != null && selectable != null)
        {
            EventSystem.current.SetSelectedGameObject(selectable.gameObject);
        }
    }

    private void Update()
    {
        if (!IsVisible)
        {
            return;
        }

        if (radio == null || !radio.CanInteractWithPlayer)
        {
            Close();
            return;
        }

        // Keep the modal surface authoritative even if another update path
        // requested cursor capture in the same frame as a UI click.
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        if (ReadEscapePressed())
        {
            Close();
        }
    }

    private static bool ReadEscapePressed()
    {
#if ENABLE_INPUT_SYSTEM
        return Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame;
#else
        return Input.GetKeyDown(KeyCode.Escape);
#endif
    }

    private void RestorePopupState()
    {
        if (!popupStateActive)
        {
            return;
        }

        // Mark restored before invoking player callbacks so Close/OnDestroy
        // cannot apply the snapshot twice during teardown.
        popupStateActive = false;
        Time.timeScale = previousTimeScale;

        PlayerMovement player = FindAnyObjectByType<PlayerMovement>();
        if (cursorWasCaptured && player != null)
        {
            player.CaptureCursorForGameplay();
            return;
        }

        Cursor.lockState = previousCursorLockState;
        Cursor.visible = previousCursorVisible;
    }

    private void OnDestroy()
    {
        RestorePopupState();
        if (activePopup == this)
        {
            activePopup = null;
        }
    }
}
