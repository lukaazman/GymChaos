using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Locker outfit selector in the menu and HUD style: an upright ink-rimmed
/// plate with a red title tag, Bebas labels and Persona buttons for every
/// top, bottom and headwear option; tops and bottoms each pick their colour
/// from a row of swatches beside the section label. The worn item keeps the selected plate, locked
/// items are dimmed with a padlock; hovering one names its unlock requirement. Equip rules stay in
/// <see cref="GymExperienceService"/>; this only presents and forwards clicks.
/// </summary>
public sealed class GymLockerMenuUI : MonoBehaviour
{
    private const float PanelWidth = 640f;
    private const float PanelHeight = 776f;
    private const float Padding = 34f;
    private const float ButtonHeight = 56f;
    private const float ButtonGap = 22f;
    private const float RowGap = 18f;
    private const int Columns = 3;

    private static GymLockerMenuUI instance;

    private GymExperienceService service;
    private Canvas canvas;
    private CanvasGroup group;
    private RectTransform panel;
    private Text rankLabel;
    private Text wearingLabel;
    private Text challengeLabel;
    private Button[] topButtons;
    private Button[] shirtButtons;
    private Button[] bottomButtons;
    private Button[] bottomColorButtons;
    private Button[] headwearButtons;
    private Button doneButton;
    private bool shown;
    private System.Func<string> hoveredRequirement;
    private float shownAt;

    public static GymLockerMenuUI Active => instance;
    public bool IsShowingForVerification => shown;
    public Button[] TopButtonsForVerification => topButtons;
    public Button[] ShirtButtonsForVerification => shirtButtons;
    public Button[] BottomButtonsForVerification => bottomButtons;
    public Button[] BottomColorButtonsForVerification => bottomColorButtons;
    public static bool IsSwatchSelectedForVerification(Button swatch) =>
        swatch.transform.Find("Worn").gameObject.activeSelf;
    public Button[] HeadwearButtonsForVerification => headwearButtons;
    public Button DoneButtonForVerification => doneButton;

    public static GymLockerMenuUI Ensure(GymExperienceService owner)
    {
        if (instance == null)
        {
            GameObject root = new GameObject("Gym Locker Menu");
            instance = root.AddComponent<GymLockerMenuUI>();
            instance.service = owner;
            instance.Build();
        }
        instance.service = owner;
        return instance;
    }

    private void Build()
    {
        GymRuntimeSettings.EnsureEventSystem();
        canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 60;
        GymRuntimeSettings.ConfigureBalancedCanvasScaler(gameObject.AddComponent<CanvasScaler>());
        gameObject.AddComponent<GraphicRaycaster>();
        group = gameObject.AddComponent<CanvasGroup>();

        Font fallback = PersonaMenuStyle.BuiltinFont;
        Font body = PersonaMenuStyle.LoadButtonFont(fallback);
        Font title = PersonaMenuStyle.LoadTitleFont(fallback);

        GameObject panelObject = new GameObject("Locker Panel", typeof(RectTransform));
        panelObject.transform.SetParent(transform, false);
        panel = panelObject.GetComponent<RectTransform>();
        panel.anchorMin = panel.anchorMax = new Vector2(0f, 0.5f);
        panel.pivot = new Vector2(0f, 0.5f);
        panel.anchoredPosition = new Vector2(56f, 0f);
        panel.sizeDelta = new Vector2(PanelWidth, PanelHeight);
        PersonaMenuStyle.AddUprightPlate(panel, PersonaMenuStyle.Ink, PersonaMenuStyle.PlateBlack);

        GameObject tagObject = new GameObject("Title Tag", typeof(RectTransform));
        tagObject.transform.SetParent(panel, false);
        RectTransform titleTag = tagObject.GetComponent<RectTransform>();
        titleTag.anchorMin = titleTag.anchorMax = new Vector2(0f, 1f);
        titleTag.pivot = new Vector2(0f, 0.5f);
        titleTag.anchoredPosition = new Vector2(Padding - 6f, 0f);
        titleTag.sizeDelta = new Vector2(200f, 60f);
        PersonaMenuStyle.AddUprightPlate(titleTag, PersonaMenuStyle.Ink, PersonaMenuStyle.Accent);
        Text titleText = PersonaMenuStyle.CreateLayoutLabel(titleTag, "Title", title, "OUTFIT",
            new Vector2(20f, 0f), new Vector2(170f, 60f), 38, PersonaMenuStyle.Ink, TextAnchor.MiddleLeft);
        titleText.horizontalOverflow = HorizontalWrapMode.Overflow;

        rankLabel = PersonaMenuStyle.CreateLayoutLabel(panel, "Rank", body, string.Empty,
            new Vector2(Padding, 48f), new Vector2(PanelWidth - Padding * 2f, 34f), 30,
            PersonaMenuStyle.Muted, TextAnchor.MiddleLeft);

        float buttonWidth = (PanelWidth - Padding * 2f - ButtonGap * (Columns - 1) - 24f) / Columns;
        PersonaMenuStyle.CreateLayoutLabel(panel, "Top Section", body, "TOP",
            new Vector2(Padding, 100f), new Vector2(300f, 32f), 30, PersonaMenuStyle.Gold, TextAnchor.MiddleLeft);
        topButtons = new Button[GymExperienceService.LockerTops.Count];
        for (int i = 0; i < topButtons.Length; i++)
        {
            GymTopStyle top = GymExperienceService.LockerTops[i];
            topButtons[i] = PersonaMenuStyle.CreateLayoutButton(panel, "Top " + top, body, string.Empty,
                Cell(i, 142f, buttonWidth), new Vector2(buttonWidth, ButtonHeight), 32,
                () => { service?.EquipTop(top); Refresh(); });
            AddLockHover(topButtons[i], () => service != null ? service.GetLockerTopRequirement(top) : string.Empty);
        }

        // The swatches beside TOP colour whichever top is worn.
        shirtButtons = BuildSwatches("Shirt", 100f,
            shirt => { service?.EquipShirt(shirt); Refresh(); });

        PersonaMenuStyle.CreateLayoutLabel(panel, "Bottom Section", body, "BOTTOMS",
            new Vector2(Padding, 216f), new Vector2(300f, 32f), 30, PersonaMenuStyle.Gold, TextAnchor.MiddleLeft);
        bottomButtons = new Button[GymExperienceService.LockerBottoms.Length];
        for (int i = 0; i < bottomButtons.Length; i++)
        {
            GymBottomStyle bottom = GymExperienceService.LockerBottoms[i];
            bottomButtons[i] = PersonaMenuStyle.CreateLayoutButton(panel, "Bottom " + bottom, body, string.Empty,
                Cell(i, 258f, buttonWidth), new Vector2(buttonWidth, ButtonHeight), 32,
                () => { service?.EquipBottom(bottom); Refresh(); });
        }
        bottomColorButtons = BuildSwatches("Bottom Color", 216f,
            color => { service?.EquipBottomColor(color); Refresh(); });

        PersonaMenuStyle.CreateLayoutLabel(panel, "Headwear Section", body, "HEADWEAR",
            new Vector2(Padding, 334f), new Vector2(300f, 32f), 30, PersonaMenuStyle.Gold, TextAnchor.MiddleLeft);
        headwearButtons = new Button[GymExperienceService.LockerHeadwear.Count];
        for (int i = 0; i < headwearButtons.Length; i++)
        {
            GymHeadwear item = GymExperienceService.LockerHeadwear[i];
            headwearButtons[i] = PersonaMenuStyle.CreateLayoutButton(panel, "Headwear " + item, body, string.Empty,
                Cell(i, 376f, buttonWidth), new Vector2(buttonWidth, ButtonHeight), 32,
                () => { service?.EquipHeadwear(item); Refresh(); });
            AddLockHover(headwearButtons[i], () => service != null ? service.GetLockerHeadwearRequirement(item) : string.Empty);
        }

        wearingLabel = PersonaMenuStyle.CreateLayoutLabel(panel, "Wearing", body, string.Empty,
            new Vector2(Padding, 610f), new Vector2(PanelWidth - Padding * 2f, 30f), 28,
            PersonaMenuStyle.Ink, TextAnchor.MiddleLeft);
        challengeLabel = PersonaMenuStyle.CreateLayoutLabel(panel, "Mastery Challenge", body, string.Empty,
            new Vector2(Padding, 644f), new Vector2(PanelWidth - Padding * 2f, 30f), 24,
            PersonaMenuStyle.Muted, TextAnchor.MiddleLeft);
        doneButton = PersonaMenuStyle.CreateLayoutButton(panel, "Done", body, "DONE   ESC",
            new Vector2(PanelWidth - Padding - 230f, PanelHeight - Padding - 58f), new Vector2(210f, 58f), 34,
            () => service?.CloseLockerMenuFromUi());

        SetShown(false);
    }

    private const float SwatchSize = 34f;
    private const float SwatchGap = 12f;

    // One colour square per shirt colour on a section's label row, right
    // aligned; a white frame marks the worn colour, locked ones are dimmed.
    private Button[] BuildSwatches(string name, float rowTop, System.Action<GymShirtColor> equip)
    {
        int count = GymExperienceService.LockerShirts.Count;
        Button[] swatches = new Button[count];
        float right = PanelWidth - Padding - 12f;
        for (int i = 0; i < count; i++)
        {
            GymShirtColor color = GymExperienceService.LockerShirts[i];
            GameObject root = new GameObject(name + " " + color, typeof(RectTransform), typeof(Image), typeof(Button));
            root.transform.SetParent(panel, false);
            RectTransform rect = root.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(right - (count - i) * (SwatchSize + SwatchGap) + SwatchGap, -rowTop + 1f);
            rect.sizeDelta = new Vector2(SwatchSize, SwatchSize);
            root.GetComponent<Image>().color = PersonaMenuStyle.Ink;
            Image worn = SwatchPart(rect, "Worn", -4f, Color.white);
            worn.transform.SetAsFirstSibling();
            Image fill = SwatchPart(rect, "Colour", 3f, PlayerCosmeticLoadout.GetSwatchColor(color));
            Button button = root.GetComponent<Button>();
            button.targetGraphic = fill;
            button.transition = Selectable.Transition.None;
            button.onClick.AddListener(() => equip(color));
            swatches[i] = button;
            AddLockHover(button, () => service != null ? service.GetLockerShirtRequirement(color) : string.Empty);
        }
        return swatches;
    }

    private static Image SwatchPart(RectTransform parent, string name, float inset, Color color)
    {
        GameObject part = new GameObject(name, typeof(RectTransform), typeof(Image));
        part.transform.SetParent(parent, false);
        RectTransform rect = part.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(inset, inset);
        rect.offsetMax = new Vector2(-inset, -inset);
        Image image = part.GetComponent<Image>();
        image.color = color;
        image.raycastTarget = name == "Colour";
        return image;
    }

    private static void ConfigureSwatch(Button swatch, bool unlocked, bool worn)
    {
        swatch.interactable = unlocked;
        swatch.transform.Find("Worn").gameObject.SetActive(worn);
        Image fill = swatch.transform.Find("Colour").GetComponent<Image>();
        Color c = fill.color;
        c.a = unlocked ? 1f : 0.3f;
        fill.color = c;
        // The dimmed square already says locked; the hover hint names the unlock.
        Transform padlock = swatch.transform.Find("Padlock");
        if (padlock != null) padlock.gameObject.SetActive(false);
    }

    private static Vector2 Cell(int index, float top, float width)
    {
        int column = index % Columns;
        int row = index / Columns;
        return new Vector2(Padding + 12f + column * (width + ButtonGap), top + row * (ButtonHeight + RowGap));
    }

    private void LateUpdate()
    {
        bool open = service != null && service.IsLockerMenuOpen &&
            !GymStartScreen.IsMenuVisible && !GymPauseMenu.IsVisible;
        if (open != shown)
        {
            SetShown(open);
        }
        if (!shown)
        {
            return;
        }

        float reveal = Mathf.Clamp01((Time.unscaledTime - shownAt) / PersonaMenuStyle.TransitionSeconds);
        group.alpha = reveal;
        panel.anchoredPosition = new Vector2(56f - (1f - reveal) * 40f, 0f);

        // LateUpdate runs after PlayerMovement, so this ESC can never also open
        // the pause menu in the same frame.
        if (GymShared.ReadEscape())
        {
            GymAudio.Play2D(GymSoundEffect.UiBack, 0.55f);
            service.CloseLockerMenuFromUi();
            SetShown(false);
        }
    }

    private void SetShown(bool value)
    {
        shown = value;
        group.alpha = 0f;
        group.blocksRaycasts = value;
        group.interactable = value;
        canvas.enabled = value;
        if (value)
        {
            shownAt = Time.unscaledTime;
            Refresh();
            if (EventSystem.current != null)
            {
                Button focus = topButtons.Length > 0 ? topButtons[0] : doneButton;
                for (int i = 0; i < topButtons.Length; i++)
                {
                    if (service.IsTopWorn(GymExperienceService.LockerTops[i])) focus = topButtons[i];
                }
                EventSystem.current.SetSelectedGameObject(focus.gameObject);
            }
        }
        else if (EventSystem.current != null && EventSystem.current.currentSelectedGameObject != null &&
            EventSystem.current.currentSelectedGameObject.transform.IsChildOf(transform))
        {
            EventSystem.current.SetSelectedGameObject(null);
        }
    }

    private void Refresh()
    {
        if (service == null)
        {
            return;
        }
        rankLabel.text = service.LockerRankLine;
        wearingLabel.text = service.LockerWearingLine;
        RefreshHint();
        for (int i = 0; i < topButtons.Length; i++)
        {
            GymTopStyle top = GymExperienceService.LockerTops[i];
            Configure(topButtons[i], service.GetLockerTopLabel(top),
                service.IsCosmeticUnlocked(top), service.IsTopWorn(top));
        }
        for (int i = 0; i < shirtButtons.Length; i++)
        {
            GymShirtColor shirt = GymExperienceService.LockerShirts[i];
            ConfigureSwatch(shirtButtons[i], service.IsCosmeticUnlocked(shirt), service.IsShirtWorn(shirt));
            ConfigureSwatch(bottomColorButtons[i], service.IsCosmeticUnlocked(shirt), service.IsBottomColorWorn(shirt));
        }
        for (int i = 0; i < bottomButtons.Length; i++)
        {
            GymBottomStyle bottom = GymExperienceService.LockerBottoms[i];
            Configure(bottomButtons[i], PlayerCosmeticLoadout.GetBottomDisplayName(bottom), true,
                service.IsBottomWorn(bottom));
        }
        for (int i = 0; i < headwearButtons.Length; i++)
        {
            GymHeadwear item = GymExperienceService.LockerHeadwear[i];
            Configure(headwearButtons[i], service.GetLockerHeadwearLabel(item),
                service.IsCosmeticUnlocked(item), service.IsHeadwearWorn(item));
        }
    }

    private void AddLockHover(Button button, System.Func<string> requirement)
    {
        LockHover hover = button.gameObject.AddComponent<LockHover>();
        hover.Changed = inside =>
        {
            hoveredRequirement = inside ? requirement : null;
            RefreshHint();
        };
        BuildPadlock(button.transform);
    }

    // The hint row shows the hovered item's unlock requirement, otherwise the
    // mastery challenge once one exists; it never shows a bare "locked".
    private void RefreshHint()
    {
        if (service == null) return;
        string requirement = hoveredRequirement != null ? hoveredRequirement() : string.Empty;
        if (!string.IsNullOrEmpty(requirement))
        {
            challengeLabel.text = requirement;
            challengeLabel.color = PersonaMenuStyle.Gold;
            return;
        }
        string challenge = service.MasteryChallengeLabel;
        challengeLabel.text = challenge == GymExperienceService.GetMasteryChallengeLabel(0)
            ? string.Empty : challenge.ToUpperInvariant();
        challengeLabel.color = PersonaMenuStyle.Muted;
    }

    // Small drawn padlock at the right edge of a button; shown only while locked.
    private static void BuildPadlock(Transform button)
    {
        GameObject root = new GameObject("Padlock", typeof(RectTransform));
        root.transform.SetParent(button, false);
        RectTransform rect = root.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(1f, 0.5f);
        rect.pivot = new Vector2(1f, 0.5f);
        rect.anchoredPosition = new Vector2(-10f, -1f);
        rect.sizeDelta = new Vector2(14f, 18f);
        PadlockPart(rect, 0f, 0f, 14f, 10f);   // body
        PadlockPart(rect, 2f, 9f, 2.5f, 6f);   // shackle left
        PadlockPart(rect, 9.5f, 9f, 2.5f, 6f); // shackle right
        PadlockPart(rect, 2f, 14f, 10f, 2.5f); // shackle top
        root.SetActive(false);
    }

    private static void PadlockPart(RectTransform parent, float x, float y, float width, float height)
    {
        GameObject part = new GameObject("Part", typeof(RectTransform), typeof(Image));
        part.transform.SetParent(parent, false);
        RectTransform rect = part.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.zero;
        rect.anchoredPosition = new Vector2(x, y);
        rect.sizeDelta = new Vector2(width, height);
        Image image = part.GetComponent<Image>();
        image.color = PersonaMenuStyle.Muted;
        image.raycastTarget = false;
    }

    private static void Configure(Button button, string label, bool unlocked, bool worn)
    {
        button.interactable = unlocked;
        Transform padlock = button.transform.Find("Padlock");
        if (padlock != null) padlock.gameObject.SetActive(!unlocked);
        button.GetComponentInChildren<Text>().text = label;
        PersonaMenuStyle.FitLabelToButton(button, 32);
        button.GetComponent<PersonaMenuButton>().SetSelected(worn);
    }

    // Disabled buttons still receive pointer events; this only reports hover
    // so locked plates keep their look while the hint row explains the unlock.
    private sealed class LockHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        public System.Action<bool> Changed;
        public void OnPointerEnter(PointerEventData eventData) => Changed?.Invoke(true);
        public void OnPointerExit(PointerEventData eventData) => Changed?.Invoke(false);
        private void OnDisable() => Changed?.Invoke(false);
    }

    private void OnDestroy()
    {
        if (instance == this)
        {
            instance = null;
        }
    }
}
