using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Runtime settings used by the pause menu. Values are applied immediately
/// through Unity APIs and persisted under an explicit GymChaos UI namespace.
/// </summary>
public static class GymRuntimeSettings
{
    private const string ResolutionWidthKey = "GymChaos.UI.ResolutionWidth.v1";
    private const string ResolutionHeightKey = "GymChaos.UI.ResolutionHeight.v1";
    private const string WindowModeKey = "GymChaos.UI.WindowMode.v1";
    private const string TextureQualityKey = "GymChaos.UI.TextureQuality.v1";
    private const string PostProcessingKey = "GymChaos.UI.PostProcessing.v1";
    private const string AntiAliasingKey = "GymChaos.UI.AntiAliasing.v1";
    private const string MasterVolumeKey = "GymChaos.UI.MasterVolume.v1";

    private static readonly Color Ink = new Color(0.91f, 0.94f, 0.98f, 1f);
    private static readonly Color Accent = new Color(0.98f, 0.34f, 0.13f, 1f);

    internal static void ConfigureBalancedCanvasScaler(CanvasScaler scaler)
    {
        if (scaler == null)
        {
            return;
        }

        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;
    }
    public static GameObject CreateOptionsPanel(
        Transform parent, Font font, UnityAction backAction)
    {
        ApplyPersistedSettings();
        Font actualFont = font != null
            ? font
            : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        // Same faces as the start and pause menus: Anton for the heading,
        // Bebas Neue for every label and control.
        Font titleFont = PersonaMenuStyle.LoadTitleFont(actualFont);
        Font labelFont = PersonaMenuStyle.LoadButtonFont(actualFont);

        GameObject panel = new GameObject("Options Panel", typeof(RectTransform));
        panel.transform.SetParent(parent, false);
        Stretch(panel.GetComponent<RectTransform>());
        // Navy veil like the pause page, then one slanted Persona plate that
        // holds the whole settings sheet.
        Image veil = CreateImage("Options Surface", panel.transform, OptionsVeil);
        Stretch(veil.rectTransform);
        CreateSettingsSheet(panel.transform, titleFont);

        CreateSectionHeader(panel.transform, labelFont, "Display Section", "DISPLAY", 0.765f);
        CreateRowLabel(panel.transform, labelFont, "RESOLUTION", RowY(0));
        CreateRowLabel(panel.transform, labelFont, "WINDOW MODE", RowY(1));

        Resolution[] resolutions = GetResolutionOptions();
        string[] resolutionLabels = GetResolutionLabels(resolutions);
        Resolution selectedResolution = resolutions[FindResolutionIndex(resolutions)];
        FullScreenMode[] modes =
        {
            FullScreenMode.Windowed,
            FullScreenMode.FullScreenWindow,
            FullScreenMode.ExclusiveFullScreen
        };
        string[] modeLabels = { "WINDOWED", "BORDERLESS", "FULLSCREEN" };
        FullScreenMode selectedMode = GetStoredWindowMode(modes);

        CreateDropdown("Resolution Dropdown", panel.transform, labelFont,
            resolutionLabels, FindResolutionIndex(resolutions), RowY(0), index =>
            {
                selectedResolution = resolutions[Mathf.Clamp(index, 0, resolutions.Length - 1)];
                ApplyDisplay(selectedResolution, selectedMode);
            });
        CreateDropdown("Window Mode Dropdown", panel.transform, labelFont,
            modeLabels, GetWindowModeIndex(modes, selectedMode), RowY(1), index =>
            {
                selectedMode = modes[Mathf.Clamp(index, 0, modes.Length - 1)];
                ApplyDisplay(selectedResolution, selectedMode);
            });

        CreateSectionHeader(panel.transform, labelFont, "Graphics Section", "GRAPHICS", 0.545f);
        CreateRowLabel(panel.transform, labelFont, "TEXTURE", RowY(2));
        CreateRowLabel(panel.transform, labelFont, "POST FX", RowY(3));
        CreateRowLabel(panel.transform, labelFont, "ANTI-ALIASING", RowY(4));

        string[] textureLabels = { "FULL", "HALF", "QUARTER", "LOW" };
        CreateDropdown("Texture Quality Dropdown", panel.transform, labelFont,
            textureLabels,
            Mathf.Clamp(PlayerPrefs.GetInt(TextureQualityKey,
                QualitySettings.globalTextureMipmapLimit), 0, textureLabels.Length - 1),
            RowY(2), ApplyTextureQuality);

        bool postEnabled = PlayerPrefs.GetInt(PostProcessingKey, 1) != 0;
        Text postValue = CreateText("Post FX Value", panel.transform, labelFont,
            postEnabled ? "ON" : "OFF", 26, Ink, FontStyle.Normal,
            TextAnchor.MiddleRight, new Vector2(ValueMinX, RowY(3)),
            new Vector2(ControlMaxX, RowY(3) + RowHeight));
        Toggle postToggle = CreateToggle("Post FX Toggle", panel.transform, postEnabled,
            new Vector2(ControlMinX, RowY(3) + 0.008f),
            new Vector2(ControlMinX + 0.04f, RowY(3) + RowHeight - 0.008f));
        postToggle.onValueChanged.AddListener(value =>
        {
            postValue.text = value ? "ON" : "OFF";
            ApplyPostProcessing(value);
        });

        string[] aaLabels = { "OFF", "FXAA", "SMAA", "TAA" };
        CreateDropdown("Anti Aliasing Dropdown", panel.transform, labelFont, aaLabels,
            Mathf.Clamp(GetStoredAntiAliasing(), 0, aaLabels.Length - 1),
            RowY(4), ApplyAntiAliasing);

        CreateSectionHeader(panel.transform, labelFont, "Audio Section", "AUDIO", 0.25f);
        CreateRowLabel(panel.transform, labelFont, "MASTER", RowY(5));
        Text volumeValue = CreateText("Master Value", panel.transform, labelFont,
            FormatVolume(AudioListener.volume), 26, Ink, FontStyle.Normal,
            TextAnchor.MiddleRight, new Vector2(ValueMinX, RowY(5)),
            new Vector2(ControlMaxX, RowY(5) + RowHeight));
        CreateSlider("Master Volume Slider", panel.transform, AudioListener.volume,
            new Vector2(ControlMinX, RowY(5) + 0.012f),
            new Vector2(ValueMinX - 0.008f, RowY(5) + RowHeight - 0.012f), value =>
            {
                volumeValue.text = FormatVolume(value);
                ApplyMasterVolume(value);
            });

        // Downscaled copy of the menu buttons, centred under the sheet.
        Button back = PersonaMenuStyle.CreateButton(panel.transform, "Options Back Button",
            labelFont, "BACK", PersonaMenuStyle.ArtWidth * 0.5f,
            PersonaMenuStyle.ArtHeight * 0.875f, 190f, 52f, 44, -1f, backAction);
        PersonaMenuButton backPersona = back.GetComponent<PersonaMenuButton>();
        if (backPersona != null) backPersona.SetFocusMotion(1.06f, 2f);
        panel.SetActive(false);
        return panel;
    }

    // Settings layout, in fractions of the 1920x1080 reference canvas.
    private const float SheetMinX = 0.3f;
    private const float SheetMaxX = 0.7f;
    private const float LabelMinX = 0.335f;
    private const float ControlMinX = 0.49f;
    private const float ControlMaxX = 0.665f;
    private const float ValueMinX = 0.61f;
    private const float RowHeight = 0.058f;
    private static readonly float[] RowBottoms = { 0.69f, 0.615f, 0.47f, 0.395f, 0.32f, 0.175f };
    private static readonly Color OptionsVeil = new Color(0.012f, 0.05f, 0.13f, 0.72f);
    private static readonly Color SheetColor = new Color(0.03f, 0.036f, 0.058f, 1f);

    // Two display rows, three graphics rows, one audio row.
    private static float RowY(int row)
    {
        return RowBottoms[Mathf.Clamp(row, 0, RowBottoms.Length - 1)];
    }

    private static void CreateSettingsSheet(Transform parent, Font titleFont)
    {
        GameObject sheet = new GameObject("Options Sheet", typeof(RectTransform));
        sheet.transform.SetParent(parent, false);
        RectTransform sheetRect = sheet.GetComponent<RectTransform>();
        SetAnchors(sheetRect, new Vector2(SheetMinX, 0.085f), new Vector2(SheetMaxX, 0.905f));
        sheetRect.localRotation = Quaternion.Euler(0f, 0f, -0.8f);
        PersonaShape edge = PersonaMenuStyle.CreateShape("Sheet Accent Edge", sheet.transform,
            PersonaMenuStyle.Accent);
        Stretch(edge.rectTransform);
        edge.SetCorners(new Vector2(-0.012f, -0.012f), new Vector2(-0.02f, 0.01f),
            new Vector2(0.03f, 0.018f), new Vector2(0.012f, -0.02f));
        PersonaShape plate = PersonaMenuStyle.CreateShape("Sheet Plate", sheet.transform, SheetColor);
        Stretch(plate.rectTransform);
        plate.SetCorners(Vector2.zero, Vector2.zero, new Vector2(0.018f, 0f), Vector2.zero);

        // Title: a red slanted plate with the Anton heading, like the title
        // cut-outs of the start menu but at a fraction of their size.
        GameObject title = new GameObject("Options Heading Plate", typeof(RectTransform));
        title.transform.SetParent(parent, false);
        RectTransform titleRect = title.GetComponent<RectTransform>();
        SetAnchors(titleRect, new Vector2(0.315f, 0.84f), new Vector2(0.47f, 0.93f));
        titleRect.localRotation = Quaternion.Euler(0f, 0f, 2.5f);
        PersonaShape titleBacking = PersonaMenuStyle.CreateShape("Heading Backing", title.transform,
            Color.white);
        Stretch(titleBacking.rectTransform);
        titleBacking.SetCorners(new Vector2(-0.04f, -0.12f), new Vector2(-0.03f, 0.1f),
            new Vector2(0.06f, 0.12f), new Vector2(0.04f, -0.1f));
        PersonaShape titlePlate = PersonaMenuStyle.CreateShape("Heading Plate", title.transform,
            PersonaMenuStyle.Accent);
        Stretch(titlePlate.rectTransform);
        titlePlate.SetCorners(Vector2.zero, new Vector2(0.01f, 0f), new Vector2(0.04f, 0f),
            new Vector2(0.02f, 0f));
        Text heading = CreateText("Options Heading", title.transform, titleFont, "SETTINGS", 56,
            Color.white, FontStyle.Normal, TextAnchor.MiddleCenter, Vector2.zero, Vector2.one);
        Shadow shadow = heading.gameObject.AddComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.85f);
        shadow.effectDistance = new Vector2(3f, -3f);
    }

    private static void CreateSectionHeader(Transform parent, Font font, string name,
        string label, float y)
    {
        CreateText(name, parent, font, label, 30, PersonaMenuStyle.Accent, FontStyle.Normal,
            TextAnchor.MiddleLeft, new Vector2(LabelMinX, y), new Vector2(ControlMaxX, y + 0.04f));
        PersonaShape rule = PersonaMenuStyle.CreateShape(name + " Rule", parent,
            new Color(PersonaMenuStyle.Accent.r, PersonaMenuStyle.Accent.g,
                PersonaMenuStyle.Accent.b, 0.55f));
        SetAnchors(rule.rectTransform, new Vector2(LabelMinX + 0.07f, y + 0.017f),
            new Vector2(ControlMaxX, y + 0.021f));
        rule.SetSlant(0.004f);
    }

    public static void ApplyPersistedSettings()
    {
        FullScreenMode[] modes =
        {
            FullScreenMode.Windowed,
            FullScreenMode.FullScreenWindow,
            FullScreenMode.ExclusiveFullScreen
        };
        if (PlayerPrefs.HasKey(ResolutionWidthKey) && PlayerPrefs.HasKey(ResolutionHeightKey))
        {
            Screen.SetResolution(
                Mathf.Max(320, PlayerPrefs.GetInt(ResolutionWidthKey)),
                Mathf.Max(240, PlayerPrefs.GetInt(ResolutionHeightKey)),
                GetStoredWindowMode(modes));
        }

        ApplyTextureQuality(PlayerPrefs.GetInt(
            TextureQualityKey, QualitySettings.globalTextureMipmapLimit));
        ApplyPostProcessing(PlayerPrefs.GetInt(PostProcessingKey, 1) != 0);
        ApplyAntiAliasing(PlayerPrefs.GetInt(AntiAliasingKey, GetStoredAntiAliasing()));
        ApplyMasterVolume(PlayerPrefs.GetFloat(MasterVolumeKey, AudioListener.volume));
    }

    public static void ExitGame()
    {
        Time.timeScale = 1f;
        Debug.Log("GYMCHAOS_EXIT_REQUESTED");
        if (Application.platform == RuntimePlatform.WebGLPlayer)
        {
            Debug.Log("GYMCHAOS_WEBGL_RETURN_TO_START");
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
            return;
        }

#if UNITY_EDITOR
        EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    internal static Button CreateButton(string name, Transform parent, Font font,
        string label, Color normalColor, Vector2 anchorMin, Vector2 anchorMax,
        UnityAction onClick)
    {
        GameObject buttonObject = new GameObject(
            name, typeof(RectTransform), typeof(Image), typeof(Button));
        buttonObject.transform.SetParent(parent, false);
        RectTransform rect = buttonObject.GetComponent<RectTransform>();
        SetAnchors(rect, anchorMin, anchorMax);
        Image image = buttonObject.GetComponent<Image>();
        image.color = normalColor;
        Image edge = CreateImage("Accent Edge", buttonObject.transform, Accent);
        SetAnchors(edge.rectTransform, Vector2.zero, new Vector2(0.018f, 1f));
        edge.raycastTarget = false;

        Button button = buttonObject.GetComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(onClick);
        button.transition = Selectable.Transition.ColorTint;
        button.colors = CreateColorBlock(normalColor);
        Text text = CreateText("Label", buttonObject.transform, font, label, 22, Ink,
            FontStyle.Bold, TextAnchor.MiddleCenter, Vector2.zero, Vector2.one);
        text.raycastTarget = false;
        return button;
    }

    internal static Text CreateText(string name, Transform parent, Font font,
        string content, int fontSize, Color color, FontStyle style, TextAnchor alignment,
        Vector2 anchorMin, Vector2 anchorMax)
    {
        GameObject textObject = new GameObject(name, typeof(RectTransform), typeof(Text));
        textObject.transform.SetParent(parent, false);
        SetAnchors(textObject.GetComponent<RectTransform>(), anchorMin, anchorMax);
        Text text = textObject.GetComponent<Text>();
        text.font = font;
        text.text = content;
        text.fontSize = fontSize;
        text.fontStyle = style;
        text.color = color;
        text.alignment = alignment;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.raycastTarget = false;
        return text;
    }

    internal static Image CreateImage(string name, Transform parent, Color color)
    {
        GameObject imageObject = new GameObject(name, typeof(RectTransform), typeof(Image));
        imageObject.transform.SetParent(parent, false);
        Image image = imageObject.GetComponent<Image>();
        image.color = color;
        return image;
    }

    internal static void EnsureEventSystem()
    {
        EventSystem eventSystem = EventSystem.current;
        if (eventSystem == null)
        {
            GameObject eventObject = new GameObject("Gym UI Event System");
            eventSystem = eventObject.AddComponent<EventSystem>();
            eventObject.AddComponent<StandaloneInputModule>();
        }
        else if (eventSystem.GetComponent<BaseInputModule>() == null)
        {
            eventSystem.gameObject.AddComponent<StandaloneInputModule>();
        }
    }

    internal static void Stretch(RectTransform rect)
    {
        SetAnchors(rect, Vector2.zero, Vector2.one);
    }

    internal static void SetAnchors(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax,
        float left = 0f, float bottom = 0f, float right = 0f, float top = 0f)
    {
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = new Vector2(left, bottom);
        rect.offsetMax = new Vector2(-right, -top);
    }

    private static void CreateRowLabel(Transform parent, Font font, string label, float y)
    {
        CreateText(label + " Label", parent, font, label, 27, Ink, FontStyle.Normal,
            TextAnchor.MiddleLeft, new Vector2(LabelMinX, y),
            new Vector2(ControlMinX - 0.01f, y + RowHeight));
    }

    // Slanted control plate shared by dropdowns, the toggle and the slider:
    // idle navy face with red outline, red face and white slab on focus.
    private static PersonaShape CreateControlPlate(GameObject root)
    {
        PersonaShape backing = PersonaMenuStyle.CreateShape("Focus Backing", root.transform, Color.white);
        Stretch(backing.rectTransform);
        backing.SetCorners(new Vector2(-0.03f, -0.16f), new Vector2(-0.035f, 0.16f),
            new Vector2(0.055f, 0.16f), new Vector2(0.035f, -0.16f));
        PersonaShape outline = PersonaMenuStyle.CreateShape("Outline", root.transform,
            PersonaMenuStyle.Accent);
        Stretch(outline.rectTransform);
        outline.SetCorners(new Vector2(-0.012f, -0.08f), new Vector2(-0.012f, 0.08f),
            new Vector2(0.035f, 0.08f), new Vector2(0.015f, -0.08f));
        PersonaShape face = PersonaMenuStyle.CreateShape("Face", root.transform,
            PersonaMenuStyle.ButtonIdle);
        Stretch(face.rectTransform);
        face.SetCorners(Vector2.zero, Vector2.zero, new Vector2(0.02f, 0f), Vector2.zero);
        face.raycastTarget = true;
        PersonaMenuButton persona = root.AddComponent<PersonaMenuButton>();
        persona.Configure(face, outline, backing, null, PersonaMenuStyle.ButtonIdle,
            PersonaMenuStyle.Accent, PersonaMenuStyle.Accent, Color.black);
        persona.SetFocusMotion(1.035f, 1f);
        return face;
    }

    private static Dropdown CreateDropdown(string name, Transform parent, Font font,
        string[] options, int initialValue, float rowY, UnityAction<int> onChanged)
    {
        GameObject dropdownObject = new GameObject(
            name, typeof(RectTransform), typeof(Dropdown));
        dropdownObject.transform.SetParent(parent, false);
        SetAnchors(dropdownObject.GetComponent<RectTransform>(),
            new Vector2(ControlMinX, rowY + 0.006f),
            new Vector2(ControlMaxX, rowY + RowHeight - 0.006f));
        PersonaShape face = CreateControlPlate(dropdownObject);
        Dropdown dropdown = dropdownObject.GetComponent<Dropdown>();
        dropdown.targetGraphic = face;
        dropdown.transition = Selectable.Transition.None;

        Text caption = CreateText("Caption", dropdownObject.transform, font, "", 26, Color.white,
            FontStyle.Normal, TextAnchor.MiddleLeft, new Vector2(0.06f, 0f), new Vector2(0.84f, 1f));
        PersonaShape arrow = PersonaMenuStyle.CreateShape("Arrow", dropdownObject.transform, Color.white);
        SetAnchors(arrow.rectTransform, new Vector2(0.86f, 0.36f), new Vector2(0.93f, 0.64f));
        // Collapse the two bottom corners to the middle: a down-pointing wedge.
        arrow.SetCorners(new Vector2(0.5f, 0f), Vector2.zero, Vector2.zero, new Vector2(-0.5f, 0f));

        RectTransform template = CreateDropdownTemplate(dropdownObject.transform, font);
        dropdown.template = template;
        dropdown.captionText = caption;
        dropdown.itemText = template.GetComponentInChildren<Text>(true);
        for (int i = 0; i < options.Length; i++)
        {
            dropdown.options.Add(new Dropdown.OptionData(options[i]));
        }
        dropdown.value = Mathf.Clamp(initialValue, 0, Mathf.Max(0, options.Length - 1));
        dropdown.RefreshShownValue();
        dropdown.onValueChanged.AddListener(onChanged);
        return dropdown;
    }

    private static RectTransform CreateDropdownTemplate(Transform parent, Font font)
    {
        GameObject templateObject = new GameObject(
            "Template", typeof(RectTransform), typeof(Image), typeof(ScrollRect));
        templateObject.transform.SetParent(parent, false);
        RectTransform templateRect = templateObject.GetComponent<RectTransform>();
        templateRect.anchorMin = new Vector2(0f, 0f);
        templateRect.anchorMax = new Vector2(1f, 0f);
        templateRect.pivot = new Vector2(0.5f, 1f);
        templateRect.sizeDelta = new Vector2(0f, 220f);
        templateRect.anchoredPosition = new Vector2(0f, -6f);
        templateObject.GetComponent<Image>().color = PersonaMenuStyle.Accent;
        ScrollRect scroll = templateObject.GetComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;

        GameObject viewportObject = new GameObject(
            "Viewport", typeof(RectTransform), typeof(Image), typeof(Mask));
        viewportObject.transform.SetParent(templateObject.transform, false);
        RectTransform viewport = viewportObject.GetComponent<RectTransform>();
        SetAnchors(viewport, Vector2.zero, Vector2.one, 3f, 3f, 3f, 3f);
        viewportObject.GetComponent<Image>().color = SheetColor;
        viewportObject.GetComponent<Mask>().showMaskGraphic = true;
        scroll.viewport = viewport;

        GameObject contentObject = new GameObject(
            "Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        contentObject.transform.SetParent(viewportObject.transform, false);
        RectTransform content = contentObject.GetComponent<RectTransform>();
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = new Vector2(1f, 1f);
        content.pivot = new Vector2(0.5f, 1f);
        // A new RectTransform defaults to a 100x100 sizeDelta; with stretched
        // anchors that makes the list 100 px wider than the masked viewport
        // and the mask clips the first letters of every option.
        content.sizeDelta = Vector2.zero;
        content.anchoredPosition = Vector2.zero;
        VerticalLayoutGroup layout = contentObject.GetComponent<VerticalLayoutGroup>();
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        ContentSizeFitter fitter = contentObject.GetComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        scroll.content = content;

        GameObject itemObject = new GameObject(
            "Item", typeof(RectTransform), typeof(Image), typeof(Toggle), typeof(LayoutElement));
        itemObject.transform.SetParent(contentObject.transform, false);
        itemObject.GetComponent<RectTransform>().sizeDelta = new Vector2(0f, 44f);
        Image itemImage = itemObject.GetComponent<Image>();
        itemImage.color = Color.white;
        itemObject.GetComponent<LayoutElement>().preferredHeight = 44f;
        Toggle itemToggle = itemObject.GetComponent<Toggle>();
        itemToggle.targetGraphic = itemImage;
        itemToggle.transition = Selectable.Transition.ColorTint;
        itemToggle.colors = CreateListColorBlock();
        Text itemText = CreateText("Item Label", itemObject.transform, font, "", 26, Color.white,
            FontStyle.Normal, TextAnchor.MiddleLeft, new Vector2(0.06f, 0f), new Vector2(0.94f, 1f));
        itemText.raycastTarget = false;

        templateObject.SetActive(false);
        return templateRect;
    }

    private static Toggle CreateToggle(string name, Transform parent, bool initialValue,
        Vector2 anchorMin, Vector2 anchorMax)
    {
        GameObject toggleObject = new GameObject(name, typeof(RectTransform), typeof(Toggle));
        toggleObject.transform.SetParent(parent, false);
        SetAnchors(toggleObject.GetComponent<RectTransform>(), anchorMin, anchorMax);
        PersonaShape face = CreateControlPlate(toggleObject);
        Toggle toggle = toggleObject.GetComponent<Toggle>();
        toggle.targetGraphic = face;
        toggle.transition = Selectable.Transition.None;
        PersonaShape indicator = PersonaMenuStyle.CreateShape("Toggle Indicator",
            toggleObject.transform, Color.white);
        SetAnchors(indicator.rectTransform, new Vector2(0.24f, 0.24f), new Vector2(0.76f, 0.76f));
        indicator.SetSlant(0.12f);
        toggle.graphic = indicator;
        toggle.isOn = initialValue;
        return toggle;
    }

    private static Slider CreateSlider(string name, Transform parent, float initialValue,
        Vector2 anchorMin, Vector2 anchorMax, UnityAction<float> onChanged)
    {
        GameObject sliderObject = new GameObject(name, typeof(RectTransform), typeof(Slider));
        sliderObject.transform.SetParent(parent, false);
        SetAnchors(sliderObject.GetComponent<RectTransform>(), anchorMin, anchorMax);
        CreateControlPlate(sliderObject);
        Slider slider = sliderObject.GetComponent<Slider>();
        slider.minValue = 0f;
        slider.maxValue = 1f;
        slider.value = Mathf.Clamp01(initialValue);
        slider.direction = Slider.Direction.LeftToRight;
        slider.transition = Selectable.Transition.None;

        GameObject fillAreaObject = new GameObject("Fill Area", typeof(RectTransform));
        fillAreaObject.transform.SetParent(sliderObject.transform, false);
        RectTransform fillArea = fillAreaObject.GetComponent<RectTransform>();
        SetAnchors(fillArea, new Vector2(0.03f, 0.3f), new Vector2(0.97f, 0.7f));
        PersonaShape fill = PersonaMenuStyle.CreateShape("Fill", fillAreaObject.transform, Color.white);
        Stretch(fill.rectTransform);
        fill.SetSlant(0.01f);

        GameObject handleArea = new GameObject("Handle Slide Area", typeof(RectTransform));
        handleArea.transform.SetParent(sliderObject.transform, false);
        SetAnchors(handleArea.GetComponent<RectTransform>(), new Vector2(0.03f, 0f),
            new Vector2(0.97f, 1f));
        PersonaShape handle = PersonaMenuStyle.CreateShape("Handle", handleArea.transform, Color.white);
        SetAnchors(handle.rectTransform, new Vector2(0f, -0.12f), new Vector2(0f, 1.12f), -6f, 0f, -6f, 0f);
        handle.SetSlant(0.35f);
        handle.raycastTarget = true;
        slider.fillRect = fill.rectTransform;
        slider.handleRect = handle.rectTransform;
        slider.targetGraphic = handle;
        slider.onValueChanged.AddListener(onChanged);
        return slider;
    }

    // Dropdown list rows: navy idle, red when highlighted or selected.
    private static ColorBlock CreateListColorBlock()
    {
        return new ColorBlock
        {
            normalColor = PersonaMenuStyle.ButtonIdle,
            highlightedColor = PersonaMenuStyle.Accent,
            pressedColor = Color.Lerp(PersonaMenuStyle.Accent, Color.black, 0.25f),
            selectedColor = PersonaMenuStyle.Accent,
            disabledColor = new Color(0.055f, 0.07f, 0.12f, 0.35f),
            colorMultiplier = 1f,
            fadeDuration = 0.08f
        };
    }

    private static ColorBlock CreateColorBlock(Color normal)
    {
        return new ColorBlock
        {
            normalColor = normal,
            highlightedColor = Color.Lerp(normal, Ink, 0.18f),
            pressedColor = Color.Lerp(normal, new Color(0.01f, 0.02f, 0.04f, 1f), 0.25f),
            selectedColor = Color.Lerp(normal, Ink, 0.13f),
            disabledColor = new Color(normal.r, normal.g, normal.b, 0.35f),
            colorMultiplier = 1f,
            fadeDuration = 0.1f
        };
    }

    private static Resolution[] GetResolutionOptions()
    {
        Resolution[] available = Screen.resolutions;
        List<Resolution> unique = new List<Resolution>();
        for (int i = 0; i < available.Length; i++)
        {
            bool duplicate = false;
            for (int j = 0; j < unique.Count; j++)
            {
                if (unique[j].width == available[i].width && unique[j].height == available[i].height)
                {
                    duplicate = true;
                    break;
                }
            }
            if (!duplicate && available[i].width >= 320 && available[i].height >= 240)
            {
                unique.Add(available[i]);
            }
        }
        if (unique.Count == 0)
        {
            unique.Add(new Resolution
            {
                width = Mathf.Max(320, Screen.width),
                height = Mathf.Max(240, Screen.height)
            });
        }
        return unique.ToArray();
    }

    private static string[] GetResolutionLabels(Resolution[] resolutions)
    {
        string[] labels = new string[resolutions.Length];
        for (int i = 0; i < resolutions.Length; i++)
        {
            labels[i] = resolutions[i].width + " x " + resolutions[i].height;
        }
        return labels;
    }

    private static int FindResolutionIndex(Resolution[] resolutions)
    {
        int width = PlayerPrefs.GetInt(ResolutionWidthKey, Screen.width);
        int height = PlayerPrefs.GetInt(ResolutionHeightKey, Screen.height);
        for (int i = 0; i < resolutions.Length; i++)
        {
            if (resolutions[i].width == width && resolutions[i].height == height)
            {
                return i;
            }
        }
        return Mathf.Clamp(resolutions.Length - 1, 0, resolutions.Length - 1);
    }

    private static FullScreenMode GetStoredWindowMode(FullScreenMode[] modes)
    {
        int stored = PlayerPrefs.GetInt(WindowModeKey, (int)Screen.fullScreenMode);
        for (int i = 0; i < modes.Length; i++)
        {
            if ((int)modes[i] == stored)
            {
                return modes[i];
            }
        }
        return Screen.fullScreenMode == FullScreenMode.Windowed
            ? FullScreenMode.Windowed
            : FullScreenMode.FullScreenWindow;
    }

    private static int GetWindowModeIndex(FullScreenMode[] modes, FullScreenMode selected)
    {
        for (int i = 0; i < modes.Length; i++)
        {
            if (modes[i] == selected)
            {
                return i;
            }
        }
        return 0;
    }

    private static void ApplyDisplay(Resolution resolution, FullScreenMode mode)
    {
        PlayerPrefs.SetInt(ResolutionWidthKey, resolution.width);
        PlayerPrefs.SetInt(ResolutionHeightKey, resolution.height);
        PlayerPrefs.SetInt(WindowModeKey, (int)mode);
        PlayerPrefs.Save();
        Screen.SetResolution(resolution.width, resolution.height, mode);
    }

    private static void ApplyTextureQuality(int value)
    {
        int clamped = Mathf.Clamp(value, 0, 3);
        QualitySettings.globalTextureMipmapLimit = clamped;
        PlayerPrefs.SetInt(TextureQualityKey, clamped);
        PlayerPrefs.Save();
    }

    private static void ApplyPostProcessing(bool enabled)
    {
        PlayerPrefs.SetInt(PostProcessingKey, enabled ? 1 : 0);
        PlayerPrefs.Save();
        Volume[] volumes = Object.FindObjectsByType<Volume>(FindObjectsSortMode.None);
        for (int i = 0; i < volumes.Length; i++)
        {
            if (volumes[i] != null)
            {
                volumes[i].enabled = enabled;
            }
        }
        Camera[] cameras = Object.FindObjectsByType<Camera>(FindObjectsSortMode.None);
        for (int i = 0; i < cameras.Length; i++)
        {
            if (cameras[i] != null && cameras[i].TryGetComponent(out UniversalAdditionalCameraData data))
            {
                data.renderPostProcessing = enabled;
            }
        }
    }

    private static int GetStoredAntiAliasing()
    {
        if (PlayerPrefs.HasKey(AntiAliasingKey))
        {
            return Mathf.Clamp(PlayerPrefs.GetInt(AntiAliasingKey), 0, 3);
        }
        Camera[] cameras = Object.FindObjectsByType<Camera>(FindObjectsSortMode.None);
        for (int i = 0; i < cameras.Length; i++)
        {
            if (cameras[i] == null || !cameras[i].TryGetComponent(out UniversalAdditionalCameraData data))
            {
                continue;
            }
            switch (data.antialiasing)
            {
                case AntialiasingMode.FastApproximateAntialiasing: return 1;
                case AntialiasingMode.SubpixelMorphologicalAntiAliasing: return 2;
                case AntialiasingMode.TemporalAntiAliasing: return 3;
                default: return 0;
            }
        }
        return 0;
    }

    private static void ApplyAntiAliasing(int value)
    {
        int clamped = Mathf.Clamp(value, 0, 3);
        PlayerPrefs.SetInt(AntiAliasingKey, clamped);
        PlayerPrefs.Save();
        AntialiasingMode mode = clamped switch
        {
            1 => AntialiasingMode.FastApproximateAntialiasing,
            2 => AntialiasingMode.SubpixelMorphologicalAntiAliasing,
            3 => AntialiasingMode.TemporalAntiAliasing,
            _ => AntialiasingMode.None
        };
        Camera[] cameras = Object.FindObjectsByType<Camera>(FindObjectsSortMode.None);
        for (int i = 0; i < cameras.Length; i++)
        {
            if (cameras[i] != null && cameras[i].TryGetComponent(out UniversalAdditionalCameraData data))
            {
                data.antialiasing = mode;
                data.antialiasingQuality = AntialiasingQuality.High;
            }
        }
    }

    private static void ApplyMasterVolume(float value)
    {
        float clamped = Mathf.Clamp01(value);
        AudioListener.volume = clamped;
        PlayerPrefs.SetFloat(MasterVolumeKey, clamped);
        PlayerPrefs.Save();
    }

    private static string FormatVolume(float value)
    {
        return Mathf.RoundToInt(Mathf.Clamp01(value) * 100f) + "%";
    }
}
