using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Minimal boot screen layered over the generated gym. The gym is built first
/// so the player sees the actual room, windows, daylight and parking backdrop;
/// gameplay actors are spawned only after Play is selected.
/// </summary>
[DefaultExecutionOrder(-20)]
public sealed class GymStartScreen : MonoBehaviour
{
    private static GymStartScreen instance;

    private GymArenaBootstrap bootstrap;
    private PlayerMovement player;
    private CanvasGroup canvasGroup;
    private Volume backgroundVolume;
    private VolumeProfile backgroundVolumeProfile;
    private Button playButton;
    private bool closing;

    private static readonly Color Ink = new Color(0.91f, 0.94f, 0.98f, 1f);
    private static readonly Color MutedInk = new Color(0.61f, 0.69f, 0.78f, 1f);
    private static readonly Color RaisedInk = new Color(0.055f, 0.085f, 0.13f, 0.9f);
    private static readonly Color Accent = new Color(0.98f, 0.34f, 0.13f, 1f);
    private static readonly Color Frame = new Color(0.82f, 0.89f, 0.97f, 0.22f);

    public static bool IsMenuVisible =>
        instance != null && !instance.closing && instance.canvasGroup != null &&
        instance.canvasGroup.blocksRaycasts;

    public static GymStartScreen CreateForScene(GymArenaBootstrap targetBootstrap, PlayerMovement targetPlayer)
    {
        if (instance != null)
        {
            return instance;
        }

        GameObject root = new GameObject("Gym Start Screen");
        instance = root.AddComponent<GymStartScreen>();
        instance.bootstrap = targetBootstrap;
        instance.player = targetPlayer;
        instance.BuildInterface();
        instance.EnterMenuState();
        Debug.Log("GYMCHAOS_START_SCREEN_READY", instance);
        return instance;
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
    }

    private void OnDestroy()
    {
        if (backgroundVolumeProfile != null)
        {
            if (Application.isPlaying)
            {
                Destroy(backgroundVolumeProfile);
            }
            else
            {
                DestroyImmediate(backgroundVolumeProfile);
            }

            backgroundVolumeProfile = null;
        }

        if (instance == this)
        {
            instance = null;
        }
    }

    private void BuildInterface()
    {
        EnsureEventSystem();

        Canvas canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;
        canvas.pixelPerfect = false;

        CanvasScaler scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.55f;
        gameObject.AddComponent<GraphicRaycaster>();
        canvasGroup = gameObject.AddComponent<CanvasGroup>();

        CreateBackgroundTreatment();

        // Unity 6 removed Arial.ttf from the valid built-in runtime font list.
        // LegacyRuntime.ttf is the supported built-in UGUI font for editor,
        // standalone and WebGL player builds.
        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (font == null)
        {
            Debug.LogError("GYMCHAOS_START_SCREEN_FONT_MISSING: LegacyRuntime.ttf could not be loaded.", this);
            return;
        }
        RectTransform rootRect = gameObject.GetComponent<RectTransform>();
        Stretch(rootRect);

        // The room remains the hero image. This overlay only creates the calm,
        // high-contrast moment needed for a readable start screen.
        Image veil = CreateImage("Atmosphere Veil", transform, new Color(0.008f, 0.018f, 0.038f, 0.34f));
        Stretch(veil.rectTransform);
        veil.raycastTarget = false;

        // A restrained cinematic frame gives the menu a designed edge without
        // covering the gym with another card or fake browser-like chrome.
        Image topBand = CreateImage("Cinematic Top Band", transform, new Color(0.004f, 0.01f, 0.022f, 0.26f));
        SetAnchors(topBand.rectTransform, new Vector2(0f, 0.88f), Vector2.one);
        topBand.raycastTarget = false;
        Image bottomBand = CreateImage("Cinematic Bottom Band", transform, new Color(0.004f, 0.01f, 0.022f, 0.3f));
        SetAnchors(bottomBand.rectTransform, Vector2.zero, new Vector2(1f, 0.13f));
        bottomBand.raycastTarget = false;
        CreateFrame(transform);

        CreateText(
            "Boot Context",
            transform,
            font,
            "GYMCHAOS  /  START SCREEN",
            14,
            MutedInk,
            FontStyle.Bold,
            TextAnchor.MiddleCenter,
            new Vector2(0.25f, 0.8f),
            new Vector2(0.75f, 0.85f));
        CreateText(
            "Title",
            transform,
            font,
            "GYMCHAOS",
            116,
            Ink,
            FontStyle.Bold,
            TextAnchor.MiddleCenter,
            new Vector2(0.16f, 0.58f),
            new Vector2(0.84f, 0.76f));
        Image titleRule = CreateImage("Title Accent Rule", transform, Accent);
        SetAnchors(titleRule.rectTransform, new Vector2(0.41f, 0.55f), new Vector2(0.59f, 0.55f), 0f, -1f, 0f, -1f);
        titleRule.raycastTarget = false;
        CreateText(
            "Subtitle",
            transform,
            font,
            "THE FLOOR IS READY",
            16,
            MutedInk,
            FontStyle.Normal,
            TextAnchor.MiddleCenter,
            new Vector2(0.3f, 0.47f),
            new Vector2(0.7f, 0.52f));

        playButton = CreateButton(
            "Play Button",
            transform,
            font,
            "PLAY",
            Accent,
            new Vector2(0.34f, 0.31f),
            new Vector2(0.66f, 0.405f),
            BeginPlay);
        CreateButton(
            "Exit Button",
            transform,
            font,
            "EXIT",
            RaisedInk,
            new Vector2(0.34f, 0.19f),
            new Vector2(0.66f, 0.285f),
            ExitGame);

        CreateText(
            "Footer",
            transform,
            font,
            "SELECT AN OPTION TO ENTER",
            12,
            MutedInk,
            FontStyle.Normal,
            TextAnchor.MiddleCenter,
            new Vector2(0.31f, 0.14f),
            new Vector2(0.69f, 0.18f));

        if (EventSystem.current != null && playButton != null)
        {
            EventSystem.current.SetSelectedGameObject(playButton.gameObject);
        }
    }

    private void CreateBackgroundTreatment()
    {
        // URP keeps this treatment behind the ScreenSpaceOverlay canvas, so the
        // gym is softened while the title and controls stay crisp and readable.
        backgroundVolume = gameObject.AddComponent<Volume>();
        backgroundVolume.isGlobal = true;
        backgroundVolume.priority = 100f;
        backgroundVolume.weight = 1f;
        backgroundVolumeProfile = ScriptableObject.CreateInstance<VolumeProfile>();

        DepthOfField depthOfField = backgroundVolumeProfile.Add<DepthOfField>(true);
        depthOfField.active = true;
        depthOfField.mode.Override(DepthOfFieldMode.Gaussian);
        depthOfField.gaussianStart.Override(0f);
        depthOfField.gaussianEnd.Override(100f);
        depthOfField.gaussianMaxRadius.Override(0.7f);
        backgroundVolume.sharedProfile = backgroundVolumeProfile;
    }

    private static void CreateFrame(Transform parent)
    {
        const float left = 0.065f;
        const float right = 0.935f;
        const float bottom = 0.13f;
        const float top = 0.88f;

        CreateFrameLine("Frame Top", parent, new Vector2(left, top), new Vector2(right, top), Frame);
        CreateFrameLine("Frame Bottom", parent, new Vector2(left, bottom), new Vector2(right, bottom), Frame);
        CreateFrameLine("Frame Left", parent, new Vector2(left, bottom), new Vector2(left, top), Frame);
        CreateFrameLine("Frame Right", parent, new Vector2(right, bottom), new Vector2(right, top), Frame);

        Color cornerAccent = new Color(Accent.r, Accent.g, Accent.b, 0.82f);
        CreateFrameLine("Frame Accent Top Left", parent, new Vector2(left, top), new Vector2(left + 0.035f, top), cornerAccent);
        CreateFrameLine("Frame Accent Bottom Right", parent, new Vector2(right - 0.035f, bottom), new Vector2(right, bottom), cornerAccent);
    }

    private static void CreateFrameLine(string name, Transform parent, Vector2 from, Vector2 to, Color color)
    {
        Image line = CreateImage(name, parent, color);
        bool vertical = Mathf.Approximately(from.x, to.x);
        bool horizontal = Mathf.Approximately(from.y, to.y);
        SetAnchors(
            line.rectTransform,
            from,
            to,
            vertical ? -1f : 0f,
            horizontal ? -1f : 0f,
            vertical ? -1f : 0f,
            horizontal ? -1f : 0f);
        line.raycastTarget = false;
    }

    private void EnterMenuState()
    {
        if (player != null)
        {
            Transform rig = player.transform.Find("PlayerAvatarRig");
            if (rig != null)
            {
                rig.gameObject.SetActive(false);
            }

            player.enabled = false;
        }

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        if (canvasGroup != null)
        {
            canvasGroup.alpha = 1f;
            canvasGroup.interactable = true;
            canvasGroup.blocksRaycasts = true;
        }
    }

    private void BeginPlay()
    {
        if (closing)
        {
            return;
        }

        closing = true;
        Debug.Log("GYMCHAOS_START_SCREEN_PLAY", this);
        if (canvasGroup != null)
        {
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;
        }

        if (bootstrap != null)
        {
            bootstrap.BeginGameplay();
        }
        else if (player != null)
        {
            player.enabled = true;
        }

        if (player != null)
        {
            player.CaptureCursorForGameplay();
        }

        StartCoroutine(FadeOutAndClose());
    }

    private IEnumerator FadeOutAndClose()
    {
        float elapsed = 0f;
        const float duration = 0.22f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            if (canvasGroup != null)
            {
                canvasGroup.alpha = 1f - Mathf.Clamp01(elapsed / duration);
            }
            yield return null;
        }

        Destroy(gameObject);
    }

    private void ExitGame()
    {
        Debug.Log("GYMCHAOS_EXIT_REQUESTED", this);
        Application.Quit();
#if UNITY_EDITOR
        EditorApplication.isPlaying = false;
#endif
    }

    private static Button CreateButton(
        string name,
        Transform parent,
        Font font,
        string label,
        Color normalColor,
        Vector2 anchorMin,
        Vector2 anchorMax,
        UnityEngine.Events.UnityAction onClick)
    {
        GameObject buttonObject = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
        buttonObject.transform.SetParent(parent, false);
        RectTransform rect = buttonObject.GetComponent<RectTransform>();
        SetAnchors(rect, anchorMin, anchorMax);
        Image image = buttonObject.GetComponent<Image>();
        image.color = normalColor;

        Image accentEdge = CreateImage("Accent Edge", buttonObject.transform, Accent);
        SetAnchors(accentEdge.rectTransform, Vector2.zero, new Vector2(0.014f, 1f));
        accentEdge.raycastTarget = false;

        Button button = buttonObject.GetComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(onClick);
        button.transition = Selectable.Transition.ColorTint;
        button.colors = new ColorBlock
        {
            normalColor = normalColor,
            highlightedColor = Color.Lerp(normalColor, Ink, 0.16f),
            pressedColor = Color.Lerp(normalColor, new Color(0.01f, 0.02f, 0.04f, 1f), 0.2f),
            selectedColor = Color.Lerp(normalColor, Ink, 0.12f),
            disabledColor = new Color(normalColor.r, normalColor.g, normalColor.b, 0.35f),
            colorMultiplier = 1f,
            fadeDuration = 0.12f
        };

        Outline outline = buttonObject.AddComponent<Outline>();
        outline.effectColor = new Color(Accent.r, Accent.g, Accent.b, 0.8f);
        outline.effectDistance = new Vector2(1.5f, 1.5f);

        Text text = CreateText(
            "Label",
            buttonObject.transform,
            font,
            label,
            26,
            Ink,
            FontStyle.Bold,
            TextAnchor.MiddleCenter,
            Vector2.zero,
            Vector2.one);
        text.raycastTarget = false;
        return button;
    }

    private static Text CreateText(
        string name,
        Transform parent,
        Font font,
        string content,
        int fontSize,
        Color color,
        FontStyle style,
        TextAnchor alignment,
        Vector2 anchorMin,
        Vector2 anchorMax)
    {
        GameObject textObject = new GameObject(name, typeof(RectTransform), typeof(Text));
        textObject.transform.SetParent(parent, false);
        RectTransform rect = textObject.GetComponent<RectTransform>();
        SetAnchors(rect, anchorMin, anchorMax);
        Text text = textObject.GetComponent<Text>();
        text.font = font;
        text.text = content;
        text.fontSize = fontSize;
        text.fontStyle = style;
        text.color = color;
        text.alignment = alignment;
        text.lineSpacing = 1.05f;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.raycastTarget = false;
        return text;
    }

    private static Image CreateImage(string name, Transform parent, Color color)
    {
        GameObject imageObject = new GameObject(name, typeof(RectTransform), typeof(Image));
        imageObject.transform.SetParent(parent, false);
        Image image = imageObject.GetComponent<Image>();
        image.color = color;
        return image;
    }

    private static void EnsureEventSystem()
    {
        EventSystem eventSystem = EventSystem.current;
        if (eventSystem == null)
        {
            GameObject eventObject = new GameObject("Gym UI Event System");
            eventSystem = eventObject.AddComponent<EventSystem>();
            eventObject.AddComponent<StandaloneInputModule>();
            return;
        }

        if (eventSystem.GetComponent<BaseInputModule>() == null)
        {
            eventSystem.gameObject.AddComponent<StandaloneInputModule>();
        }
    }

    private static void Stretch(RectTransform rect)
    {
        SetAnchors(rect, Vector2.zero, Vector2.one);
    }

    private static void SetAnchors(
        RectTransform rect,
        Vector2 anchorMin,
        Vector2 anchorMax,
        float left = 0f,
        float bottom = 0f,
        float right = 0f,
        float top = 0f)
    {
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = new Vector2(left, bottom);
        rect.offsetMax = new Vector2(-right, -top);
        rect.localScale = Vector3.one;
        rect.localRotation = Quaternion.identity;
    }
}
