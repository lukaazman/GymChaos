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
    private static readonly Color MutedInk = new Color(0.61f, 0.69f, 0.78f, 1f);
    private static readonly Color Surface = new Color(0.035f, 0.065f, 0.1f, 0.98f);
    private static readonly Color Control = new Color(0.07f, 0.105f, 0.15f, 1f);
    private static readonly Color Accent = new Color(0.98f, 0.34f, 0.13f, 1f);
    private static readonly Color Rule = new Color(0.82f, 0.89f, 0.97f, 0.22f);

    public static GameObject CreateOptionsPanel(
        Transform parent, Font font, UnityAction backAction)
    {
        ApplyPersistedSettings();
        Font actualFont = font != null
            ? font
            : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        GameObject panel = new GameObject("Options Panel", typeof(RectTransform));
        panel.transform.SetParent(parent, false);
        Stretch(panel.GetComponent<RectTransform>());
        Image surface = CreateImage("Options Surface", panel.transform, Surface);
        Stretch(surface.rectTransform);

        Image rule = CreateImage("Options Top Rule", panel.transform, Accent);
        SetAnchors(rule.rectTransform, new Vector2(0.18f, 0.895f),
            new Vector2(0.82f, 0.895f), 0f, -1f, 0f, -1f);
        rule.raycastTarget = false;
        CreateText("Options Heading", panel.transform, actualFont, "OPTIONS", 34,
            Ink, FontStyle.Bold, TextAnchor.MiddleCenter,
            new Vector2(0.18f, 0.91f), new Vector2(0.82f, 0.97f));

        CreateText("Display Section", panel.transform, actualFont, "DISPLAY", 13,
            Accent, FontStyle.Bold, TextAnchor.MiddleLeft,
            new Vector2(0.18f, 0.77f), new Vector2(0.82f, 0.81f));
        CreateRowLabel(panel.transform, actualFont, "RESOLUTION", 0.68f);
        CreateRowLabel(panel.transform, actualFont, "WINDOW MODE", 0.59f);

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

        CreateDropdown("Resolution Dropdown", panel.transform, actualFont,
            resolutionLabels, FindResolutionIndex(resolutions),
            new Vector2(0.47f, 0.68f), new Vector2(0.82f, 0.75f), index =>
            {
                selectedResolution = resolutions[Mathf.Clamp(index, 0, resolutions.Length - 1)];
                ApplyDisplay(selectedResolution, selectedMode);
            });
        CreateDropdown("Window Mode Dropdown", panel.transform, actualFont,
            modeLabels, GetWindowModeIndex(modes, selectedMode),
            new Vector2(0.47f, 0.59f), new Vector2(0.82f, 0.66f), index =>
            {
                selectedMode = modes[Mathf.Clamp(index, 0, modes.Length - 1)];
                ApplyDisplay(selectedResolution, selectedMode);
            });

        CreateText("Graphics Section", panel.transform, actualFont, "GRAPHICS", 13,
            Accent, FontStyle.Bold, TextAnchor.MiddleLeft,
            new Vector2(0.18f, 0.51f), new Vector2(0.82f, 0.55f));
        CreateRowLabel(panel.transform, actualFont, "TEXTURE", 0.42f);
        CreateRowLabel(panel.transform, actualFont, "POST FX", 0.33f);
        CreateRowLabel(panel.transform, actualFont, "ANTI-ALIASING", 0.24f);

        string[] textureLabels = { "FULL", "HALF", "QUARTER", "LOW" };
        CreateDropdown("Texture Quality Dropdown", panel.transform, actualFont,
            textureLabels,
            Mathf.Clamp(PlayerPrefs.GetInt(TextureQualityKey,
                QualitySettings.globalTextureMipmapLimit), 0, textureLabels.Length - 1),
            new Vector2(0.47f, 0.42f), new Vector2(0.82f, 0.49f), ApplyTextureQuality);

        bool postEnabled = PlayerPrefs.GetInt(PostProcessingKey, 1) != 0;
        Text postValue = CreateText("Post FX Value", panel.transform, actualFont,
            postEnabled ? "ON" : "OFF", 16, Ink, FontStyle.Bold,
            TextAnchor.MiddleRight, new Vector2(0.78f, 0.33f), new Vector2(0.84f, 0.40f));
        Toggle postToggle = CreateToggle("Post FX Toggle", panel.transform, postEnabled,
            new Vector2(0.70f, 0.34f), new Vector2(0.76f, 0.39f));
        postToggle.onValueChanged.AddListener(value =>
        {
            postValue.text = value ? "ON" : "OFF";
            ApplyPostProcessing(value);
        });

        string[] aaLabels = { "OFF", "FXAA", "SMAA", "TAA" };
        CreateDropdown("Anti Aliasing Dropdown", panel.transform, actualFont, aaLabels,
            Mathf.Clamp(GetStoredAntiAliasing(), 0, aaLabels.Length - 1),
            new Vector2(0.47f, 0.24f), new Vector2(0.82f, 0.31f), ApplyAntiAliasing);

        CreateText("Audio Section", panel.transform, actualFont, "AUDIO", 13,
            Accent, FontStyle.Bold, TextAnchor.MiddleLeft,
            new Vector2(0.18f, 0.16f), new Vector2(0.82f, 0.20f));
        CreateRowLabel(panel.transform, actualFont, "MASTER", 0.08f);
        Text volumeValue = CreateText("Master Value", panel.transform, actualFont,
            FormatVolume(AudioListener.volume), 16, Ink, FontStyle.Bold,
            TextAnchor.MiddleRight, new Vector2(0.78f, 0.08f), new Vector2(0.84f, 0.15f));
        CreateSlider("Master Volume Slider", panel.transform, AudioListener.volume,
            new Vector2(0.47f, 0.09f), new Vector2(0.76f, 0.14f), value =>
            {
                volumeValue.text = FormatVolume(value);
                ApplyMasterVolume(value);
            });

        CreateButton("Options Back Button", panel.transform, actualFont, "BACK", Control,
            new Vector2(0.40f, 0.015f), new Vector2(0.60f, 0.065f), backAction);
        panel.SetActive(false);
        return panel;
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
        CreateText(label + " Label", parent, font, label, 15, MutedInk, FontStyle.Bold,
            TextAnchor.MiddleLeft, new Vector2(0.18f, y), new Vector2(0.45f, y + 0.07f));
        Image rule = CreateImage(label + " Rule", parent, Rule);
        SetAnchors(rule.rectTransform, new Vector2(0.18f, y), new Vector2(0.45f, y),
            0f, -1f, 0f, -1f);
        rule.raycastTarget = false;
    }

    private static Dropdown CreateDropdown(string name, Transform parent, Font font,
        string[] options, int initialValue, Vector2 anchorMin, Vector2 anchorMax,
        UnityAction<int> onChanged)
    {
        GameObject dropdownObject = new GameObject(
            name, typeof(RectTransform), typeof(Image), typeof(Dropdown));
        dropdownObject.transform.SetParent(parent, false);
        SetAnchors(dropdownObject.GetComponent<RectTransform>(), anchorMin, anchorMax);
        Image background = dropdownObject.GetComponent<Image>();
        background.color = Control;
        Dropdown dropdown = dropdownObject.GetComponent<Dropdown>();
        dropdown.targetGraphic = background;
        dropdown.transition = Selectable.Transition.ColorTint;
        dropdown.colors = CreateColorBlock(Control);

        Text caption = CreateText("Caption", dropdownObject.transform, font, "", 16, Ink,
            FontStyle.Bold, TextAnchor.MiddleLeft, new Vector2(0.06f, 0f), new Vector2(0.84f, 1f));
        Text arrow = CreateText("Arrow", dropdownObject.transform, font, "v", 16, Accent,
            FontStyle.Bold, TextAnchor.MiddleCenter, new Vector2(0.86f, 0f), new Vector2(0.98f, 1f));
        arrow.raycastTarget = false;

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
        templateRect.anchoredPosition = Vector2.zero;
        templateObject.GetComponent<Image>().color = Control;
        ScrollRect scroll = templateObject.GetComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;

        GameObject viewportObject = new GameObject(
            "Viewport", typeof(RectTransform), typeof(Image), typeof(Mask));
        viewportObject.transform.SetParent(templateObject.transform, false);
        RectTransform viewport = viewportObject.GetComponent<RectTransform>();
        Stretch(viewport);
        viewportObject.GetComponent<Image>().color = Control;
        viewportObject.GetComponent<Mask>().showMaskGraphic = true;
        scroll.viewport = viewport;

        GameObject contentObject = new GameObject(
            "Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        contentObject.transform.SetParent(viewportObject.transform, false);
        RectTransform content = contentObject.GetComponent<RectTransform>();
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = new Vector2(1f, 1f);
        content.pivot = new Vector2(0.5f, 1f);
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
        itemImage.color = Control;
        itemObject.GetComponent<LayoutElement>().preferredHeight = 44f;
        Toggle itemToggle = itemObject.GetComponent<Toggle>();
        itemToggle.targetGraphic = itemImage;
        itemToggle.colors = CreateColorBlock(Control);
        Text itemText = CreateText("Item Label", itemObject.transform, font, "", 16, Ink,
            FontStyle.Bold, TextAnchor.MiddleLeft, new Vector2(0.06f, 0f), new Vector2(0.94f, 1f));
        itemText.raycastTarget = false;

        templateObject.SetActive(false);
        return templateRect;
    }

    private static Toggle CreateToggle(string name, Transform parent, bool initialValue,
        Vector2 anchorMin, Vector2 anchorMax)
    {
        GameObject toggleObject = new GameObject(
            name, typeof(RectTransform), typeof(Image), typeof(Toggle));
        toggleObject.transform.SetParent(parent, false);
        SetAnchors(toggleObject.GetComponent<RectTransform>(), anchorMin, anchorMax);
        Image background = toggleObject.GetComponent<Image>();
        background.color = Control;
        Toggle toggle = toggleObject.GetComponent<Toggle>();
        toggle.targetGraphic = background;
        toggle.transition = Selectable.Transition.ColorTint;
        toggle.colors = CreateColorBlock(Control);
        Image indicator = CreateImage("Toggle Indicator", toggleObject.transform, Accent);
        SetAnchors(indicator.rectTransform, new Vector2(0.22f, 0.22f), new Vector2(0.78f, 0.78f));
        indicator.raycastTarget = false;
        toggle.graphic = indicator;
        toggle.isOn = initialValue;
        return toggle;
    }

    private static Slider CreateSlider(string name, Transform parent, float initialValue,
        Vector2 anchorMin, Vector2 anchorMax, UnityAction<float> onChanged)
    {
        GameObject sliderObject = new GameObject(
            name, typeof(RectTransform), typeof(Image), typeof(Slider));
        sliderObject.transform.SetParent(parent, false);
        SetAnchors(sliderObject.GetComponent<RectTransform>(), anchorMin, anchorMax);
        sliderObject.GetComponent<Image>().color = Control;
        Slider slider = sliderObject.GetComponent<Slider>();
        slider.minValue = 0f;
        slider.maxValue = 1f;
        slider.value = Mathf.Clamp01(initialValue);
        slider.direction = Slider.Direction.LeftToRight;
        slider.transition = Selectable.Transition.ColorTint;
        slider.colors = CreateColorBlock(Control);

        GameObject fillAreaObject = new GameObject("Fill Area", typeof(RectTransform));
        fillAreaObject.transform.SetParent(sliderObject.transform, false);
        RectTransform fillArea = fillAreaObject.GetComponent<RectTransform>();
        SetAnchors(fillArea, new Vector2(0.04f, 0.25f), new Vector2(0.96f, 0.75f));
        GameObject fillObject = new GameObject("Fill", typeof(RectTransform), typeof(Image));
        fillObject.transform.SetParent(fillAreaObject.transform, false);
        RectTransform fill = fillObject.GetComponent<RectTransform>();
        Stretch(fill);
        fillObject.GetComponent<Image>().color = Accent;

        GameObject handleObject = new GameObject("Handle", typeof(RectTransform), typeof(Image));
        handleObject.transform.SetParent(sliderObject.transform, false);
        RectTransform handle = handleObject.GetComponent<RectTransform>();
        SetAnchors(handle, new Vector2(0f, 0.05f), new Vector2(0f, 0.95f), -7f, 0f, 7f, 0f);
        Image handleImage = handleObject.GetComponent<Image>();
        handleImage.color = Ink;
        slider.fillRect = fill;
        slider.handleRect = handle;
        slider.targetGraphic = handleImage;
        slider.onValueChanged.AddListener(onChanged);
        return slider;
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
