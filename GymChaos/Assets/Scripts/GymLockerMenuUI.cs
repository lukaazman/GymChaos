using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Locker outfit selector in the menu and HUD style: an upright ink-rimmed
/// plate with a red title tag, Bebas labels and Persona buttons for every
/// shirt and headwear option. The worn item keeps the selected plate, locked
/// items are dimmed and show their unlock rank. Equip rules stay in
/// <see cref="GymExperienceService"/>; this only presents and forwards clicks.
/// </summary>
public sealed class GymLockerMenuUI : MonoBehaviour
{
    private const float PanelWidth = 640f;
    private const float PanelHeight = 660f;
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
    private Button[] shirtButtons;
    private Button[] headwearButtons;
    private Button doneButton;
    private bool shown;
    private float shownAt;

    public static GymLockerMenuUI Active => instance;
    public bool IsShowingForVerification => shown;
    public Button[] ShirtButtonsForVerification => shirtButtons;
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
        PersonaMenuStyle.CreateLayoutLabel(panel, "Shirt Section", body, "SHIRT",
            new Vector2(Padding, 100f), new Vector2(300f, 32f), 30, PersonaMenuStyle.Gold, TextAnchor.MiddleLeft);
        shirtButtons = new Button[GymExperienceService.LockerShirts.Count];
        for (int i = 0; i < shirtButtons.Length; i++)
        {
            GymShirtColor shirt = GymExperienceService.LockerShirts[i];
            shirtButtons[i] = PersonaMenuStyle.CreateLayoutButton(panel, "Shirt " + shirt, body, string.Empty,
                Cell(i, 142f, buttonWidth), new Vector2(buttonWidth, ButtonHeight), 32,
                () => { service?.EquipShirt(shirt); Refresh(); });
        }

        PersonaMenuStyle.CreateLayoutLabel(panel, "Headwear Section", body, "HEADWEAR",
            new Vector2(Padding, 296f), new Vector2(300f, 32f), 30, PersonaMenuStyle.Gold, TextAnchor.MiddleLeft);
        headwearButtons = new Button[GymExperienceService.LockerHeadwear.Count];
        for (int i = 0; i < headwearButtons.Length; i++)
        {
            GymHeadwear item = GymExperienceService.LockerHeadwear[i];
            headwearButtons[i] = PersonaMenuStyle.CreateLayoutButton(panel, "Headwear " + item, body, string.Empty,
                Cell(i, 338f, buttonWidth), new Vector2(buttonWidth, ButtonHeight), 32,
                () => { service?.EquipHeadwear(item); Refresh(); });
        }

        wearingLabel = PersonaMenuStyle.CreateLayoutLabel(panel, "Wearing", body, string.Empty,
            new Vector2(Padding, 494f), new Vector2(PanelWidth - Padding * 2f, 30f), 28,
            PersonaMenuStyle.Ink, TextAnchor.MiddleLeft);
        challengeLabel = PersonaMenuStyle.CreateLayoutLabel(panel, "Mastery Challenge", body, string.Empty,
            new Vector2(Padding, 528f), new Vector2(PanelWidth - Padding * 2f, 30f), 24,
            PersonaMenuStyle.Muted, TextAnchor.MiddleLeft);
        doneButton = PersonaMenuStyle.CreateLayoutButton(panel, "Done", body, "DONE   ESC",
            new Vector2(PanelWidth - Padding - 230f, PanelHeight - Padding - 58f), new Vector2(210f, 58f), 34,
            () => service?.CloseLockerMenuFromUi());

        SetShown(false);
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
        if (ReadEscape())
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
                Button focus = shirtButtons.Length > 0 ? shirtButtons[0] : doneButton;
                for (int i = 0; i < shirtButtons.Length; i++)
                {
                    if (service.IsShirtWorn(GymExperienceService.LockerShirts[i])) focus = shirtButtons[i];
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
        challengeLabel.text = service.MasteryChallengeLabel.ToUpperInvariant();
        for (int i = 0; i < shirtButtons.Length; i++)
        {
            GymShirtColor shirt = GymExperienceService.LockerShirts[i];
            Configure(shirtButtons[i], service.GetLockerShirtLabel(shirt),
                service.IsCosmeticUnlocked(shirt), service.IsShirtWorn(shirt));
        }
        for (int i = 0; i < headwearButtons.Length; i++)
        {
            GymHeadwear item = GymExperienceService.LockerHeadwear[i];
            Configure(headwearButtons[i], service.GetLockerHeadwearLabel(item),
                service.IsCosmeticUnlocked(item), service.IsHeadwearWorn(item));
        }
    }

    private static void Configure(Button button, string label, bool unlocked, bool worn)
    {
        button.interactable = unlocked;
        button.GetComponentInChildren<Text>().text = label;
        PersonaMenuStyle.FitLabelToButton(button, 32);
        button.GetComponent<PersonaMenuButton>().SetSelected(worn);
    }

    private static bool ReadEscape()
    {
#if ENABLE_INPUT_SYSTEM
        return Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame;
#else
        return Input.GetKeyDown(KeyCode.Escape);
#endif
    }

    private void OnDestroy()
    {
        if (instance == this)
        {
            instance = null;
        }
    }
}
