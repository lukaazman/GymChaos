using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
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
    private Button playButton;
    private bool closing;

    // Persona red-orange from the reference title and primary button.
    private static readonly Color Accent = new Color(0.93f, 0.2f, 0.09f, 1f);

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

    private Texture2D halftoneTexture;

    private void OnDestroy()
    {
        if (halftoneTexture != null)
        {
            Destroy(halftoneTexture);
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
        // A balanced scaler keeps the centered composition legible on both
        // narrow windows and ultrawide displays without pushing the controls
        // toward an edge when the aspect ratio changes.
        scaler.matchWidthOrHeight = 0.5f;
        gameObject.AddComponent<GraphicRaycaster>();
        canvasGroup = gameObject.AddComponent<CanvasGroup>();


        // Unity 6 removed Arial.ttf from the valid built-in runtime font list.
        // LegacyRuntime.ttf is the supported built-in UGUI font for editor,
        // standalone and WebGL player builds; the settings panel keeps it.
        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (font == null)
        {
            Debug.LogError("GYMCHAOS_START_SCREEN_FONT_MISSING: LegacyRuntime.ttf could not be loaded.", this);
            return;
        }
        // Persona-style condensed display faces (SIL OFL, Resources/Fonts).
        Font titleFont = Resources.Load<Font>("Fonts/Anton-Regular");
        Font buttonFont = Resources.Load<Font>("Fonts/BebasNeue-Regular");
        if (titleFont == null) titleFont = font;
        if (buttonFont == null) buttonFont = font;
        RectTransform rootRect = gameObject.GetComponent<RectTransform>();
        Stretch(rootRect);

        // The authored comic art covers the left of the screen; its
        // transparent right side leaves the live gym view visible.
        CreateBackground();
        Texture2D halftone = CreateHalftoneTexture();
        halftoneTexture = halftone;

        // "GYM" / "CHAOS" ransom-note title: every letter is its own cut-out
        // plate, tilted and offset like the reference layout (1672x941).
        CreateTitleLetter("G", titleFont, 318f, 140f, 168f, 184f, -6f, TitleInk, halftone);
        CreateTitleLetter("Y", titleFont, 478f, 124f, 128f, 172f, 4f, TitleInk, halftone);
        CreateTitleLetter("M", titleFont, 616f, 132f, 146f, 174f, -3f, TitleInk, halftone);
        CreateTitleLetter("C", titleFont, 506f, 282f, 108f, 136f, 7f, Accent, halftone);
        CreateTitleLetter("H", titleFont, 610f, 266f, 102f, 146f, -4f, Accent, halftone);
        CreateTitleLetter("A", titleFont, 712f, 262f, 102f, 152f, 3f, Accent, halftone);
        CreateTitleLetter("O", titleFont, 806f, 250f, 88f, 138f, -5f, Accent, halftone);
        CreateTitleLetter("S", titleFont, 902f, 254f, 118f, 178f, 6f, Accent, halftone);

        playButton = CreateMenuButton("Play Button", buttonFont, "ENTER THE GYM",
            262f, 616f, 440f, 96f, 84, -2f, BeginPlay);
        GameObject options = null;
        Button exitButton = null;
        Button optionsButton = CreateMenuButton("Options Button", buttonFont, "SETTINGS",
            200f, 722f, 310f, 72f, 60, -1f, null);
        exitButton = CreateMenuButton("Exit Button", buttonFont, "EXIT",
            176f, 812f, 262f, 70f, 58, -1f, ExitGame);
        Button[] menuButtons = { playButton, optionsButton, exitButton };
        // While settings are open, Submit must not trigger a menu entry that
        // still holds focus behind the panel.
        void SetMenuButtonsInteractable(bool interactable)
        {
            foreach (Button menuButton in menuButtons) menuButton.interactable = interactable;
        }
        optionsButton.onClick.AddListener(() => {
            SetMenuButtonsInteractable(false);
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
            options.SetActive(true);
        });
        options = GymRuntimeSettings.CreateOptionsPanel(transform, font, () => {
            options.SetActive(false);
            SetMenuButtonsInteractable(true);
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(optionsButton.gameObject);
        });
        options.SetActive(false);

        if (EventSystem.current != null && playButton != null)
        {
            EventSystem.current.SetSelectedGameObject(playButton.gameObject);
        }
    }

    // Reference layout size of the supplied background art.
    private const float ArtWidth = 1672f;
    private const float ArtHeight = 941f;
    private static readonly Color TitleInk = new Color(0.97f, 0.97f, 0.95f, 1f);
    private static readonly Color PlateBlack = new Color(0.035f, 0.035f, 0.045f, 1f);
    private static readonly Color ButtonIdle = new Color(0.055f, 0.07f, 0.12f, 1f);

    private void CreateBackground()
    {
        Texture2D art = Resources.Load<Texture2D>("UI/menu_background");
        GameObject holder = new GameObject("Menu Background", typeof(RectTransform));
        holder.transform.SetParent(transform, false);
        RectTransform holderRect = holder.GetComponent<RectTransform>();
        SetAnchors(holderRect, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
        holderRect.sizeDelta = new Vector2(ArtWidth, ArtHeight);
        // Envelope keeps the art filling the screen at any aspect ratio.
        AspectRatioFitter fitter = holder.AddComponent<AspectRatioFitter>();
        fitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
        fitter.aspectRatio = ArtWidth / ArtHeight;
        if (art != null)
        {
            RawImage image = holder.AddComponent<RawImage>();
            image.texture = art;
            image.raycastTarget = false;
            return;
        }

        // Fallback until the art is imported: a plain navy field on the art's
        // left side so the menu stays readable.
        Debug.LogWarning("GYMCHAOS_START_SCREEN_BACKGROUND_MISSING path=Resources/UI/menu_background", this);
        PersonaShape navy = CreateShape("Navy Field", holder.transform, new Color(0.03f, 0.16f, 0.36f, 1f));
        SetAnchors(navy.rectTransform, Vector2.zero, new Vector2(0.6f, 1f));
        navy.SetCorners(Vector2.zero, Vector2.zero, new Vector2(0.02f, 0f), new Vector2(0.05f, 0f));
    }

    private void CreateTitleLetter(
        string letter, Font font, float centerX, float centerY, float width, float height,
        float rotation, Color ink, Texture2D halftone)
    {
        GameObject root = new GameObject("Title " + letter, typeof(RectTransform));
        root.transform.SetParent(transform, false);
        RectTransform rect = root.GetComponent<RectTransform>();
        PlaceInArt(rect, centerX, centerY, width, height);
        rect.localRotation = Quaternion.Euler(0f, 0f, rotation);

        // Uneven cut: every plate gets its own slightly skewed corners.
        float jitter = (letter[0] % 5 - 2) * 0.018f;
        PersonaShape border = CreateShape("Paper Edge", root.transform, TitleInk);
        Stretch(border.rectTransform);
        border.SetCorners(new Vector2(-0.06f, -0.05f + jitter), new Vector2(-0.04f, 0.05f),
            new Vector2(0.06f, 0.04f - jitter), new Vector2(0.05f, -0.06f));
        PersonaShape plate = CreateShape("Plate", root.transform, PlateBlack);
        Stretch(plate.rectTransform);
        plate.SetCorners(new Vector2(-0.01f, jitter), new Vector2(0.01f, 0f),
            new Vector2(0f, -jitter), new Vector2(0.01f, 0.01f));

        float scale = 1080f / ArtHeight;
        Text text = CreateText("Letter", root.transform, font, letter,
            Mathf.RoundToInt(height * scale * 0.9f), ink, FontStyle.Normal, TextAnchor.MiddleCenter,
            Vector2.zero, Vector2.one);
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        Outline outline = text.gameObject.AddComponent<Outline>();
        outline.effectColor = Color.black;
        outline.effectDistance = new Vector2(3f, -3f);

        // Comic halftone shading across the lower-right of the cut-out.
        PersonaShape dots = CreateShape("Halftone", root.transform, new Color(0f, 0f, 0f, 0.5f));
        Stretch(dots.rectTransform);
        dots.SetCorners(new Vector2(0.45f, 0f), new Vector2(1f, -0.35f), Vector2.zero, Vector2.zero);
        dots.SetPattern(halftone, 7f);
    }

    private Button CreateMenuButton(
        string name, Font font, string label, float centerX, float centerY,
        float width, float height, int fontSize, float rotation,
        UnityEngine.Events.UnityAction onClick)
    {
        GameObject root = new GameObject(name, typeof(RectTransform), typeof(Button));
        root.transform.SetParent(transform, false);
        RectTransform rect = root.GetComponent<RectTransform>();
        PlaceInArt(rect, centerX, centerY, width, height);
        rect.localRotation = Quaternion.Euler(0f, 0f, rotation);

        // Focus backing: a white slab offset behind the red plate.
        PersonaShape backing = CreateShape("Focus Backing", root.transform, Color.white);
        Stretch(backing.rectTransform);
        backing.SetCorners(new Vector2(-0.03f, -0.1f), new Vector2(-0.045f, 0.12f),
            new Vector2(0.015f, 0.08f), new Vector2(0.035f, -0.14f));
        PersonaShape outline = CreateShape("Outline", root.transform, Accent);
        Stretch(outline.rectTransform);
        outline.SetCorners(new Vector2(-0.015f, -0.09f), new Vector2(-0.02f, 0.08f),
            new Vector2(0.075f, 0.1f), new Vector2(0.015f, -0.06f));
        PersonaShape face = CreateShape("Face", root.transform, ButtonIdle);
        Stretch(face.rectTransform);
        face.SetCorners(Vector2.zero, Vector2.zero, new Vector2(0.05f, 0f), Vector2.zero);
        face.raycastTarget = true;

        float scale = 1080f / ArtHeight;
        Text text = CreateText("Label", root.transform, font, label,
            Mathf.RoundToInt(fontSize * scale), Color.white,
            FontStyle.Normal, TextAnchor.MiddleLeft, Vector2.zero, Vector2.one);
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.rectTransform.offsetMin = new Vector2(width * scale * 0.09f, 0f);
        Shadow shadow = text.gameObject.AddComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.8f);
        shadow.effectDistance = new Vector2(3f, -3f);

        Button button = root.GetComponent<Button>();
        button.targetGraphic = face;
        button.transition = Selectable.Transition.None;
        if (onClick != null) button.onClick.AddListener(onClick);
        PersonaMenuButton persona = root.AddComponent<PersonaMenuButton>();
        persona.Configure(face, outline, backing, text, ButtonIdle, Accent, Accent, Color.black);
        return button;
    }

    // Places a rect using pixel coordinates of the 1672x941 reference art
    // (origin top-left) so the overlay lines up with the background image.
    private static void PlaceInArt(RectTransform rect, float centerX, float centerY,
        float width, float height)
    {
        Vector2 anchor = new Vector2(centerX / ArtWidth, 1f - centerY / ArtHeight);
        rect.anchorMin = anchor;
        rect.anchorMax = anchor;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        // The canvas reference is 1920x1080, i.e. the art scaled by 1.148.
        float scale = 1080f / ArtHeight;
        rect.sizeDelta = new Vector2(width * scale, height * scale);
        rect.localScale = Vector3.one;
    }

    private static PersonaShape CreateShape(string name, Transform parent, Color color)
    {
        GameObject shapeObject = new GameObject(name, typeof(RectTransform), typeof(PersonaShape));
        shapeObject.transform.SetParent(parent, false);
        PersonaShape shape = shapeObject.GetComponent<PersonaShape>();
        shape.color = color;
        shape.raycastTarget = false;
        return shape;
    }

    private static Texture2D CreateHalftoneTexture()
    {
        const int size = 32;
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            name = "Menu Halftone",
            wrapMode = TextureWrapMode.Repeat,
            filterMode = FilterMode.Bilinear
        };
        Color32[] pixels = new Color32[size * size];
        float radius = size * 0.3f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float distance = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f),
                    new Vector2(size * 0.5f, size * 0.5f));
                byte alpha = (byte)(Mathf.Clamp01(radius + 0.5f - distance) * 255f);
                pixels[y * size + x] = new Color32(255, 255, 255, alpha);
            }
        }
        texture.SetPixels32(pixels);
        texture.Apply(false, true);
        return texture;
    }

    // The menu art covers the left ~60% of the screen, so turn the boot view
    // left until the receptionist sits in the open right-hand side.
    private const float ReceptionistScreenX = 0.78f;
    private Quaternion gameplayFacing;
    private bool facingOverridden;

    private void FrameReceptionistOnRight()
    {
        Camera view = player.playerCamera;
        EnemyFighter receptionist = null;
        foreach (EnemyFighter fighter in FindObjectsByType<EnemyFighter>(FindObjectsInactive.Exclude))
        {
            if (fighter != null && fighter.Identity == BodybuilderIdentity.Manwithsuit1)
            {
                receptionist = fighter;
                break;
            }
        }
        if (view == null || receptionist == null)
        {
            return;
        }

        Vector3 toReceptionist = Vector3.ProjectOnPlane(
            receptionist.transform.position - view.transform.position, Vector3.up);
        if (toReceptionist.sqrMagnitude < 0.01f)
        {
            return;
        }
        float halfHorizontalFov = Mathf.Atan(
            Mathf.Tan(view.fieldOfView * 0.5f * Mathf.Deg2Rad) * view.aspect);
        float offset = Mathf.Atan((ReceptionistScreenX * 2f - 1f) * Mathf.Tan(halfHorizontalFov)) *
            Mathf.Rad2Deg;
        float targetYaw = Quaternion.LookRotation(toReceptionist).eulerAngles.y - offset;
        float currentYaw = view.transform.eulerAngles.y;
        gameplayFacing = player.transform.rotation;
        facingOverridden = true;
        player.transform.rotation =
            Quaternion.Euler(0f, Mathf.DeltaAngle(currentYaw, targetYaw), 0f) * player.transform.rotation;
        Rigidbody playerBody = player.GetComponent<Rigidbody>();
        if (playerBody != null) playerBody.rotation = player.transform.rotation;
        Debug.Log($"GYMCHAOS_START_SCREEN_FRAMING receptionist={receptionist.name} turnDegrees=" +
            $"{Mathf.DeltaAngle(currentYaw, targetYaw):F1}", this);
    }

    private void RestoreGameplayFacing()
    {
        if (facingOverridden && player != null)
        {
            player.transform.rotation = gameplayFacing;
            Rigidbody body = player.GetComponent<Rigidbody>();
            if (body != null) body.rotation = gameplayFacing;
            facingOverridden = false;
        }
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
            FrameReceptionistOnRight();
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

        RestoreGameplayFacing();
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
