using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// New game / load game flow layered on the boot menu. One explicit state
/// decides which page is visible, so overlays cannot stack and a session can
/// only be started once. Highlighting a class never commits it; only Confirm
/// creates a character.
/// </summary>
public sealed class GymSessionMenu : MonoBehaviour
{
    public enum State { Closed, SessionChoice, ClassSelect, SaveList, SavePreview, Error, Starting }

    private const int SavesPerPage = 5;
    private static readonly string[] StatLabels =
    {
        "BODYWEIGHT", "COMPOUND LIFTS", "CARDIO", "SPRINT SPEED", "STAMINA", "SPRINT ECONOMY"
    };

    private Font bodyFont;
    private Font titleFont;
    private Action<bool> setMainMenuInteractable;
    private Action restoreMainFocus;
    private Action sessionReady;

    private GameObject choicePage;
    private GameObject classPage;
    private GameObject listPage;
    private GameObject previewPage;
    private GameObject errorPage;

    private Button loadGameButton;
    private Button newGameButton;
    private Text choiceNote;

    private readonly List<PersonaMenuButton> classCards = new List<PersonaMenuButton>();
    private readonly List<Button> classCardButtons = new List<Button>();
    private Button confirmClassButton;
    private RawImage classArt;
    private Text className;
    private Text classDescription;
    private Text classStrength;
    private Text classWeakness;
    private readonly PersonaShape[] statFills = new PersonaShape[6];
    private readonly Text[] statValues = new Text[6];
    private int highlightedClass;

    private readonly List<GameObject> listEntries = new List<GameObject>();
    private Text listEmpty;
    private Button listBackButton;
    private Button listNextButton;
    private List<GymSaveListEntry> saves = new List<GymSaveListEntry>();
    private int listPageIndex;
    private int unreadableSaves;

    private RawImage previewArt;
    private Text previewTitle;
    private Text previewBody;
    private Button previewLoadButton;
    private GymSaveListEntry previewEntry;

    private Text errorText;
    private Button errorBackButton;
    private State errorReturnState = State.SessionChoice;

    private State state = State.Closed;
    private float inputLockedUntil;

    public State Current => state;
    public string HighlightedClassId => GymClassCatalog.All[highlightedClass].id;
    public int ValidSaveCount => saves.Count;
    public string ChoiceNoteText => choiceNote != null ? choiceNote.text : string.Empty;
    public bool LoadGameOffered => loadGameButton != null && loadGameButton.gameObject.activeSelf;
    public GameObject FocusedObject =>
        EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;

    public static GymSessionMenu Create(
        Transform canvasRoot, Font fallbackFont, Action<bool> setMainMenuInteractable,
        Action restoreMainFocus, Action sessionReady)
    {
        GameObject root = new GameObject("Session Menu", typeof(RectTransform));
        root.transform.SetParent(canvasRoot, false);
        Stretch(root.GetComponent<RectTransform>());
        GymSessionMenu menu = root.AddComponent<GymSessionMenu>();
        menu.bodyFont = PersonaMenuStyle.LoadButtonFont(fallbackFont);
        menu.titleFont = PersonaMenuStyle.LoadTitleFont(fallbackFont);
        menu.setMainMenuInteractable = setMainMenuInteractable;
        menu.restoreMainFocus = restoreMainFocus;
        menu.sessionReady = sessionReady;
        menu.Build();
        menu.Show(State.Closed);
        return menu;
    }

    public void Open()
    {
        if (state != State.Closed)
        {
            return;
        }
        RefreshSaves();
        Show(State.SessionChoice);
    }

    // ---- actions (buttons and verifiers call the same entry points) ----

    public void ChooseNewGame()
    {
        if (!Accepts(State.SessionChoice)) return;
        highlightedClass = IndexOfClass(GymClassCatalog.BaselineId);
        Show(State.ClassSelect);
    }

    public void ChooseLoadGame()
    {
        if (!Accepts(State.SessionChoice) || saves.Count == 0) return;
        listPageIndex = 0;
        Show(State.SaveList);
    }

    public void HighlightClass(string classId)
    {
        if (state != State.ClassSelect) return;
        int index = IndexOfClass(classId);
        if (index < 0 || index == highlightedClass) return;
        highlightedClass = index;
        RefreshClassDetails();
    }

    public void ConfirmClass()
    {
        if (!Accepts(State.ClassSelect)) return;
        GymSessionService.BeginNew(HighlightedClassId);
        StartSession();
    }

    public void SelectSave(string slotId)
    {
        if (!Accepts(State.SaveList)) return;
        previewEntry = saves.Find(entry => entry.SlotId == slotId);
        if (previewEntry == null) return;
        Show(State.SavePreview);
    }

    public void ConfirmLoad()
    {
        if (!Accepts(State.SavePreview) || previewEntry == null) return;
        // Validate again at the moment of loading; the file may have changed.
        if (!GymSessionService.BeginLoad(previewEntry.SlotId, out string error))
        {
            ShowError(error, State.SaveList);
            return;
        }
        StartSession();
    }

    public void Back()
    {
        if (Time.unscaledTime < inputLockedUntil) return;
        switch (state)
        {
            case State.SessionChoice:
                Show(State.Closed);
                break;
            case State.ClassSelect:
            case State.SaveList:
                RefreshSaves();
                Show(State.SessionChoice);
                break;
            case State.SavePreview:
                Show(State.SaveList);
                break;
            case State.Error:
                RefreshSaves();
                Show(errorReturnState == State.SaveList && saves.Count == 0
                    ? State.SessionChoice : errorReturnState);
                break;
        }
    }

    private void StartSession()
    {
        state = State.Starting;
        inputLockedUntil = float.PositiveInfinity;
        SetPagesActive(null);
        try
        {
            sessionReady?.Invoke();
        }
        catch (Exception exception)
        {
            // Keep the player in a usable menu instead of a half-started world.
            Debug.LogException(exception);
            inputLockedUntil = 0f;
            ShowError("The session could not be started. " + exception.Message, State.SessionChoice);
        }
    }

    private bool Accepts(State expected)
    {
        return state == expected && Time.unscaledTime >= inputLockedUntil;
    }

    private void ShowError(string message, State returnTo)
    {
        errorText.text = string.IsNullOrEmpty(message) ? "Something went wrong." : message;
        errorReturnState = returnTo;
        Show(State.Error);
    }

    private void RefreshSaves()
    {
        try
        {
            saves = GymSessionService.ListValidSaves(out unreadableSaves);
        }
        catch (Exception exception) when (exception is System.IO.IOException ||
            exception is UnauthorizedAccessException || exception is InvalidOperationException)
        {
            saves = new List<GymSaveListEntry>();
            unreadableSaves = 0;
            Debug.LogWarning("Character saves could not be listed: " + exception.Message);
        }
    }

    private void Show(State next)
    {
        state = next;
        inputLockedUntil = Time.unscaledTime + PersonaMenuStyle.TransitionSeconds;
        GameObject page =
            next == State.SessionChoice ? choicePage :
            next == State.ClassSelect ? classPage :
            next == State.SaveList ? listPage :
            next == State.SavePreview ? previewPage :
            next == State.Error ? errorPage : null;
        SetPagesActive(page);
        setMainMenuInteractable?.Invoke(next == State.Closed);

        switch (next)
        {
            case State.Closed:
                restoreMainFocus?.Invoke();
                break;
            case State.SessionChoice:
                bool hasSaves = saves.Count > 0;
                loadGameButton.gameObject.SetActive(hasSaves);
                choiceNote.text = unreadableSaves > 0
                    ? unreadableSaves + (unreadableSaves == 1
                        ? " SAVE COULD NOT BE READ AND IS NOT LISTED"
                        : " SAVES COULD NOT BE READ AND ARE NOT LISTED")
                    : string.Empty;
                Focus(hasSaves ? loadGameButton : newGameButton);
                break;
            case State.ClassSelect:
                RefreshClassDetails();
                Focus(classCardButtons[highlightedClass]);
                break;
            case State.SaveList:
                RebuildSaveList();
                break;
            case State.SavePreview:
                RefreshPreview();
                Focus(previewLoadButton);
                break;
            case State.Error:
                Focus(errorBackButton);
                break;
        }
        Debug.Log("GYMCHAOS_SESSION_MENU state=" + next);
    }

    private void SetPagesActive(GameObject visible)
    {
        choicePage.SetActive(visible == choicePage);
        classPage.SetActive(visible == classPage);
        listPage.SetActive(visible == listPage);
        previewPage.SetActive(visible == previewPage);
        errorPage.SetActive(visible == errorPage);
    }

    private void Update()
    {
        if (state == State.Closed || state == State.Starting)
        {
            return;
        }
        if (ReadCancelPressed())
        {
            GymAudio.Play2D(GymSoundEffect.UiBack, 0.55f);
            Back();
            return;
        }
        // A mouse click on empty space clears uGUI focus; restore it so
        // keyboard and controller navigation never dead-ends.
        if (EventSystem.current != null && EventSystem.current.currentSelectedGameObject == null &&
            ReadNavigationPressed())
        {
            switch (state)
            {
                case State.SessionChoice:
                    Focus(loadGameButton.gameObject.activeSelf ? loadGameButton : newGameButton);
                    break;
                case State.ClassSelect: Focus(classCardButtons[highlightedClass]); break;
                case State.SaveList: Focus(listBackButton); break;
                case State.SavePreview: Focus(previewLoadButton); break;
                case State.Error: Focus(errorBackButton); break;
            }
        }
    }

    private static bool ReadCancelPressed()
    {
#if ENABLE_INPUT_SYSTEM
        var keyboard = UnityEngine.InputSystem.Keyboard.current;
        var gamepad = UnityEngine.InputSystem.Gamepad.current;
        return (keyboard != null && keyboard.escapeKey.wasPressedThisFrame) ||
            (gamepad != null && gamepad.buttonEast.wasPressedThisFrame);
#else
        return Input.GetKeyDown(KeyCode.Escape);
#endif
    }

    private static bool ReadNavigationPressed()
    {
#if ENABLE_INPUT_SYSTEM
        var keyboard = UnityEngine.InputSystem.Keyboard.current;
        var gamepad = UnityEngine.InputSystem.Gamepad.current;
        return (keyboard != null && (keyboard.upArrowKey.wasPressedThisFrame ||
                keyboard.downArrowKey.wasPressedThisFrame || keyboard.leftArrowKey.wasPressedThisFrame ||
                keyboard.rightArrowKey.wasPressedThisFrame || keyboard.tabKey.wasPressedThisFrame ||
                keyboard.wKey.wasPressedThisFrame || keyboard.sKey.wasPressedThisFrame ||
                keyboard.aKey.wasPressedThisFrame || keyboard.dKey.wasPressedThisFrame)) ||
            (gamepad != null && (gamepad.dpad.ReadValue().sqrMagnitude > 0.2f ||
                gamepad.leftStick.ReadValue().sqrMagnitude > 0.2f));
#else
        return Mathf.Abs(Input.GetAxisRaw("Horizontal")) > 0.2f || Mathf.Abs(Input.GetAxisRaw("Vertical")) > 0.2f;
#endif
    }

    // ---- construction ----

    private void Build()
    {
        BuildChoicePage();
        BuildClassPage();
        BuildListPage();
        BuildPreviewPage();
        BuildErrorPage();
    }

    private GameObject CreatePage(string name, bool fullScreenVeil)
    {
        GameObject page = new GameObject(name, typeof(RectTransform));
        page.transform.SetParent(transform, false);
        Stretch(page.GetComponent<RectTransform>());
        if (fullScreenVeil)
        {
            // Opaque enough to read text, thin enough to keep the menu art and gym visible.
            PersonaShape veil = PersonaMenuStyle.CreateShape("Veil", page.transform,
                new Color(0.012f, 0.035f, 0.09f, 1f));
            Stretch(veil.rectTransform);
            veil.raycastTarget = true;
            PersonaShape slash = PersonaMenuStyle.CreateShape("Accent Slash", page.transform, PersonaMenuStyle.Accent);
            RectTransform slashRect = slash.rectTransform;
            slashRect.anchorMin = new Vector2(0.56f, 0f);
            slashRect.anchorMax = new Vector2(1f, 1f);
            slashRect.offsetMin = Vector2.zero;
            slashRect.offsetMax = Vector2.zero;
            slash.SetCorners(new Vector2(0.22f, 0f), new Vector2(0f, 0f), Vector2.zero, Vector2.zero);
            PersonaShape slashInner = PersonaMenuStyle.CreateShape("Accent Slash Inner", page.transform,
                new Color(0.035f, 0.05f, 0.1f, 1f));
            RectTransform innerRect = slashInner.rectTransform;
            innerRect.anchorMin = new Vector2(0.575f, 0f);
            innerRect.anchorMax = new Vector2(1f, 1f);
            innerRect.offsetMin = Vector2.zero;
            innerRect.offsetMax = Vector2.zero;
            slashInner.SetCorners(new Vector2(0.225f, 0f), new Vector2(0f, 0f), Vector2.zero, Vector2.zero);
        }
        return page;
    }

    private void BuildChoicePage()
    {
        // Upper-left of the see-through half of the boot art (right of x=1090).
        choicePage = CreatePage("Session Choice", false);
        PersonaMenuStyle.CreatePanel(choicePage.transform, "Panel", 1320f, 262f, 430f, 372f, -2f,
            new Color(0.035f, 0.05f, 0.1f, 1f), PersonaMenuStyle.Accent);
        Text title = PersonaMenuStyle.CreateLabel(choicePage.transform, "Title", titleFont, "SESSION",
            1316f, 122f, 360f, 70f, 58, PersonaMenuStyle.Ink, TextAnchor.MiddleLeft);
        title.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -2f);
        loadGameButton = PersonaMenuStyle.CreateButton(choicePage.transform, "Load Game Button", bodyFont,
            "LOAD GAME", 1312f, 212f, 336f, 72f, 58, -2f, ChooseLoadGame);
        newGameButton = PersonaMenuStyle.CreateButton(choicePage.transform, "New Game Button", bodyFont,
            "NEW GAME", 1316f, 300f, 336f, 72f, 58, -2f, ChooseNewGame);
        PersonaMenuStyle.CreateButton(choicePage.transform, "Session Back Button", bodyFont,
            "BACK", 1268f, 384f, 230f, 58f, 44, -2f, Back);
        choiceNote = PersonaMenuStyle.CreateLabel(choicePage.transform, "Note", bodyFont, string.Empty,
            1322f, 470f, 420f, 40f, 24, PersonaMenuStyle.Gold, TextAnchor.MiddleLeft);
    }

    private void BuildClassPage()
    {
        classPage = CreatePage("Class Select", true);
        Transform parent = classPage.transform;
        RectTransform titlePlate = PersonaMenuStyle.CreatePanel(parent, "Title Plate", 330f, 74f, 580f, 84f, -3f,
            PersonaMenuStyle.PlateBlack, PersonaMenuStyle.Ink);
        Text title = PersonaMenuStyle.CreateLabel(parent, "Title", titleFont, "CHOOSE YOUR CLASS",
            336f, 74f, 540f, 84f, 56, PersonaMenuStyle.Ink, TextAnchor.MiddleCenter);
        title.rectTransform.localRotation = titlePlate.localRotation;

        classArt = CreateArt(parent, "Class Illustration", 1330f, 440f, 640f, 800f);
        className = PersonaMenuStyle.CreateLabel(parent, "Class Name", titleFont, string.Empty,
            470f, 196f, 800f, 110f, 92, PersonaMenuStyle.Accent, TextAnchor.MiddleLeft);
        className.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -2f);
        Outline nameOutline = className.gameObject.AddComponent<Outline>();
        nameOutline.effectColor = Color.black;
        nameOutline.effectDistance = new Vector2(3f, -3f);
        classDescription = PersonaMenuStyle.CreateLabel(parent, "Description", bodyFont, string.Empty,
            470f, 286f, 800f, 70f, 30, PersonaMenuStyle.Ink, TextAnchor.UpperLeft);
        classStrength = PersonaMenuStyle.CreateLabel(parent, "Strength", bodyFont, string.Empty,
            470f, 352f, 800f, 34f, 28, PersonaMenuStyle.Gold, TextAnchor.MiddleLeft);
        classWeakness = PersonaMenuStyle.CreateLabel(parent, "Weakness", bodyFont, string.Empty,
            470f, 390f, 800f, 34f, 28, PersonaMenuStyle.Muted, TextAnchor.MiddleLeft);

        for (int i = 0; i < StatLabels.Length; i++)
        {
            float y = 446f + i * 40f;
            PersonaMenuStyle.CreateLabel(parent, "Stat Label " + i, bodyFont, StatLabels[i],
                180f, y, 220f, 34f, 26, PersonaMenuStyle.Muted, TextAnchor.MiddleLeft);
            PersonaShape track = PersonaMenuStyle.CreateShape("Stat Track " + i, parent,
                new Color(0f, 0f, 0f, 0.55f));
            PersonaMenuStyle.PlaceInArt(track.rectTransform, 520f, y, 400f, 18f);
            track.SetSlant(0.02f);
            // Baseline marker: every class is compared against Bodybuilding (1.00).
            PersonaShape fill = PersonaMenuStyle.CreateShape("Stat Fill " + i, track.transform, PersonaMenuStyle.Accent);
            RectTransform fillRect = fill.rectTransform;
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = new Vector2(0.5f, 1f);
            fillRect.offsetMin = Vector2.zero;
            fillRect.offsetMax = Vector2.zero;
            fill.SetSlant(0.04f);
            statFills[i] = fill;
            PersonaShape marker = PersonaMenuStyle.CreateShape("Baseline " + i, track.transform, PersonaMenuStyle.Ink);
            RectTransform markerRect = marker.rectTransform;
            markerRect.anchorMin = new Vector2(StatFraction(1f) - 0.004f, -0.35f);
            markerRect.anchorMax = new Vector2(StatFraction(1f) + 0.004f, 1.35f);
            markerRect.offsetMin = Vector2.zero;
            markerRect.offsetMax = Vector2.zero;
            statValues[i] = PersonaMenuStyle.CreateLabel(parent, "Stat Value " + i, bodyFont, string.Empty,
                790f, y, 120f, 34f, 26, PersonaMenuStyle.Ink, TextAnchor.MiddleLeft);
        }

        IReadOnlyList<GymClassDefinition> classes = GymClassCatalog.All;
        float[] tilts = { -4f, 3f, -2f, 4f, -3f };
        for (int i = 0; i < classes.Count; i++)
        {
            GymClassDefinition definition = classes[i];
            string id = definition.id;
            Button card = PersonaMenuStyle.CreateButton(parent, "Class Card " + id, bodyFont,
                string.Empty, 128f + i * 182f, 800f, 160f, 196f, 24, tilts[i % tilts.Length],
                () => HighlightClass(id));
            RawImage thumbnail = CreateArt(card.transform, "Thumbnail", 0f, 0f, 1f, 1f);
            RectTransform thumbRect = thumbnail.rectTransform;
            thumbRect.anchorMin = new Vector2(0.04f, 0.2f);
            thumbRect.anchorMax = new Vector2(0.96f, 0.98f);
            thumbRect.offsetMin = Vector2.zero;
            thumbRect.offsetMax = Vector2.zero;
            thumbRect.sizeDelta = Vector2.zero;
            thumbnail.texture = Resources.Load<Texture2D>(definition.artwork);
            thumbnail.enabled = thumbnail.texture != null;
            FitArt(thumbnail);
            Text cardLabel = card.transform.Find("Label").GetComponent<Text>();
            cardLabel.text = definition.displayName.ToUpperInvariant();
            cardLabel.alignment = TextAnchor.LowerCenter;
            cardLabel.rectTransform.offsetMin = new Vector2(0f, 6f);
            cardLabel.resizeTextForBestFit = true;
            cardLabel.resizeTextMinSize = 12;
            cardLabel.resizeTextMaxSize = cardLabel.fontSize;
            cardLabel.transform.SetAsLastSibling();
            PersonaMenuButton persona = card.GetComponent<PersonaMenuButton>();
            persona.SetFocusMotion(1.1f, 2f);
            persona.SetFocusOutlineColor(PersonaMenuStyle.Accent);
            // Focusing a card previews it; only Confirm commits the class.
            persona.Focused += () => HighlightClass(id);
            classCards.Add(persona);
            classCardButtons.Add(card);
        }

        confirmClassButton = PersonaMenuStyle.CreateButton(parent, "Class Confirm Button", bodyFont,
            "CONFIRM", 1210f, 862f, 330f, 84f, 70, -2f, ConfirmClass);
        PersonaMenuStyle.CreateButton(parent, "Class Back Button", bodyFont,
            "BACK", 1540f, 874f, 200f, 60f, 46, -1f, Back);
    }

    private void BuildListPage()
    {
        listPage = CreatePage("Save List", false);
        PersonaMenuStyle.CreatePanel(listPage.transform, "Panel", 1356f, 376f, 560f, 620f, -1.5f,
            new Color(0.035f, 0.05f, 0.1f, 1f), PersonaMenuStyle.Accent);
        Text title = PersonaMenuStyle.CreateLabel(listPage.transform, "Title", titleFont, "LOAD GAME",
            1352f, 112f, 500f, 70f, 56, PersonaMenuStyle.Ink, TextAnchor.MiddleLeft);
        title.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -1.5f);
        listEmpty = PersonaMenuStyle.CreateLabel(listPage.transform, "Empty", bodyFont,
            "NO SAVED CHARACTERS", 1356f, 330f, 500f, 50f, 34, PersonaMenuStyle.Muted, TextAnchor.MiddleLeft);
        listNextButton = PersonaMenuStyle.CreateButton(listPage.transform, "Save List Next Button", bodyFont,
            "MORE", 1500f, 636f, 190f, 54f, 40, -1.5f, NextListPage);
        listBackButton = PersonaMenuStyle.CreateButton(listPage.transform, "Save List Back Button", bodyFont,
            "BACK", 1228f, 640f, 210f, 54f, 40, -1.5f, Back);
    }

    private void BuildPreviewPage()
    {
        previewPage = CreatePage("Save Preview", true);
        Transform parent = previewPage.transform;
        RectTransform titlePlate = PersonaMenuStyle.CreatePanel(parent, "Title Plate", 300f, 74f, 520f, 84f, -3f,
            PersonaMenuStyle.PlateBlack, PersonaMenuStyle.Ink);
        Text title = PersonaMenuStyle.CreateLabel(parent, "Title", titleFont, "LOAD CHARACTER",
            306f, 74f, 480f, 84f, 56, PersonaMenuStyle.Ink, TextAnchor.MiddleCenter);
        title.rectTransform.localRotation = titlePlate.localRotation;
        previewArt = CreateArt(parent, "Preview Illustration", 1330f, 440f, 640f, 800f);
        previewTitle = PersonaMenuStyle.CreateLabel(parent, "Preview Class", titleFont, string.Empty,
            470f, 210f, 800f, 110f, 92, PersonaMenuStyle.Accent, TextAnchor.MiddleLeft);
        previewTitle.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -2f);
        Outline outline = previewTitle.gameObject.AddComponent<Outline>();
        outline.effectColor = Color.black;
        outline.effectDistance = new Vector2(3f, -3f);
        previewBody = PersonaMenuStyle.CreateLabel(parent, "Preview Details", bodyFont, string.Empty,
            470f, 500f, 800f, 420f, 34, PersonaMenuStyle.Ink, TextAnchor.UpperLeft);
        previewBody.lineSpacing = 1.25f;
        previewLoadButton = PersonaMenuStyle.CreateButton(parent, "Preview Load Button", bodyFont,
            "LOAD", 300f, 820f, 330f, 84f, 70, -2f, ConfirmLoad);
        PersonaMenuStyle.CreateButton(parent, "Preview Back Button", bodyFont,
            "BACK", 600f, 828f, 210f, 62f, 48, -1f, Back);
    }

    private void BuildErrorPage()
    {
        errorPage = CreatePage("Session Error", false);
        PersonaMenuStyle.CreatePanel(errorPage.transform, "Panel", 1340f, 290f, 520f, 380f, -2f,
            new Color(0.035f, 0.05f, 0.1f, 1f), PersonaMenuStyle.Accent);
        Text title = PersonaMenuStyle.CreateLabel(errorPage.transform, "Title", titleFont, "COULD NOT LOAD",
            1338f, 150f, 460f, 60f, 46, PersonaMenuStyle.Accent, TextAnchor.MiddleLeft);
        title.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -2f);
        errorText = PersonaMenuStyle.CreateLabel(errorPage.transform, "Message", bodyFont, string.Empty,
            1342f, 280f, 450f, 170f, 30, PersonaMenuStyle.Ink, TextAnchor.UpperLeft);
        errorBackButton = PersonaMenuStyle.CreateButton(errorPage.transform, "Error Back Button", bodyFont,
            "BACK TO SAVES", 1290f, 420f, 330f, 62f, 46, -2f, Back);
    }

    // ---- refresh ----

    private void RefreshClassDetails()
    {
        GymClassDefinition definition = GymClassCatalog.All[highlightedClass];
        for (int i = 0; i < classCards.Count; i++)
        {
            classCards[i].SetSelected(i == highlightedClass);
        }
        className.text = definition.displayName.ToUpperInvariant();
        classDescription.text = definition.description;
        classStrength.text = "+  " + definition.strength;
        classWeakness.text = "-  " + definition.weakness;
        classArt.texture = Resources.Load<Texture2D>(definition.artwork);
        classArt.enabled = classArt.texture != null;
        FitArt(classArt);

        // Sprint economy is shown as distance per stamina, so longer is always better.
        float[] values =
        {
            definition.bodyweight, definition.compound, definition.cardio,
            definition.sprintSpeed, definition.staminaCapacity, 1f / definition.sprintCost
        };
        for (int i = 0; i < values.Length; i++)
        {
            RectTransform fillRect = statFills[i].rectTransform;
            fillRect.anchorMax = new Vector2(StatFraction(values[i]), 1f);
            statFills[i].color = values[i] > 1.001f ? PersonaMenuStyle.Accent :
                values[i] < 0.999f ? PersonaMenuStyle.Muted : PersonaMenuStyle.Ink;
            statValues[i].text = "x" + values[i].ToString("0.00", CultureInfo.InvariantCulture);
        }
    }

    // Bars span multipliers 0.5 .. 1.4 so the 1.00 baseline sits mid-track.
    private static float StatFraction(float multiplier)
    {
        return Mathf.Clamp01((multiplier - 0.5f) / 0.9f);
    }

    private void NextListPage()
    {
        if (!Accepts(State.SaveList)) return;
        int pages = Mathf.Max(1, Mathf.CeilToInt(saves.Count / (float)SavesPerPage));
        listPageIndex = (listPageIndex + 1) % pages;
        RebuildSaveList();
    }

    private void RebuildSaveList()
    {
        foreach (GameObject entry in listEntries)
        {
            if (entry != null) Destroy(entry);
        }
        listEntries.Clear();
        listEmpty.gameObject.SetActive(saves.Count == 0);
        listNextButton.gameObject.SetActive(saves.Count > SavesPerPage);

        Button first = null;
        int start = listPageIndex * SavesPerPage;
        for (int i = start; i < saves.Count && i < start + SavesPerPage; i++)
        {
            GymSaveListEntry entry = saves[i];
            string slot = entry.SlotId;
            Button button = PersonaMenuStyle.CreateButton(listPage.transform, "Save Entry " + (i - start),
                bodyFont, DescribeEntry(entry.Result.Data), 1352f, 196f + (i - start) * 84f, 500f, 64f, 32, -1.5f,
                () => SelectSave(slot));
            button.GetComponent<PersonaMenuButton>().SetFocusMotion(1.04f, 1f);
            listEntries.Add(button.gameObject);
            if (first == null) first = button;
        }
        Focus(first != null ? first : listBackButton);
    }

    private static string DescribeEntry(GymCharacterSave save)
    {
        return ClassName(save.classId).ToUpperInvariant() + "   LV " + save.progression.level +
            "   " + FormatPlaytime(save.playSeconds);
    }

    private void RefreshPreview()
    {
        GymCharacterSave save = previewEntry.Result.Data;
        GymClassCatalog.TryGet(save.classId, out GymClassDefinition definition);
        previewTitle.text = ClassName(save.classId).ToUpperInvariant();
        previewArt.texture = definition != null ? Resources.Load<Texture2D>(definition.artwork) : null;
        previewArt.enabled = previewArt.texture != null;
        FitArt(previewArt);
        GymProgressionState progression = save.progression;
        string saved = DateTime.TryParse(save.savedUtc, CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind, out DateTime time)
            ? time.ToLocalTime().ToString("yyyy-MM-dd  HH:mm", CultureInfo.InvariantCulture)
            : "UNKNOWN";
        previewBody.text =
            "LEVEL " + progression.level + "     XP " + progression.experience + " / " +
                GymExperienceService.GetExperienceToNextLevel(progression.level) + "\n" +
            "STR " + progression.strengthRank + "   END " + progression.enduranceRank +
                "   TECH " + progression.techniqueRank + "   REP RANK " + progression.reputationRank + "\n" +
            "REPUTATION " + progression.reputation.ToString("+#;-#;0", CultureInfo.InvariantCulture) +
                "     MASTERY " + progression.masteryRank + "\n" +
            "GYM DAY " + progression.gymDay + "     OUTFIT " + progression.shirt.ToUpperInvariant() + "\n" +
            "PLAYTIME " + FormatPlaytime(save.playSeconds) + "\n" +
            "LAST SAVED " + saved +
            (previewEntry.Result.RecoveredBackup ? "\nRECOVERED FROM THE PREVIOUS VALID SAVE" : string.Empty);
    }

    private static string ClassName(string classId)
    {
        return GymClassCatalog.TryGet(classId, out GymClassDefinition definition)
            ? definition.displayName : classId;
    }

    private static string FormatPlaytime(double seconds)
    {
        TimeSpan span = TimeSpan.FromSeconds(Math.Max(0d, seconds));
        return ((int)span.TotalHours).ToString("00", CultureInfo.InvariantCulture) + ":" +
            span.Minutes.ToString("00", CultureInfo.InvariantCulture) + ":" +
            span.Seconds.ToString("00", CultureInfo.InvariantCulture);
    }

    private static int IndexOfClass(string classId)
    {
        IReadOnlyList<GymClassDefinition> classes = GymClassCatalog.All;
        for (int i = 0; i < classes.Count; i++)
        {
            if (classes[i].id == classId) return i;
        }
        return -1;
    }

    private static RawImage CreateArt(Transform parent, string name, float centerX, float centerY,
        float width, float height)
    {
        GameObject artObject = new GameObject(name, typeof(RectTransform), typeof(RawImage));
        artObject.transform.SetParent(parent, false);
        RawImage image = artObject.GetComponent<RawImage>();
        PersonaMenuStyle.PlaceInArt(image.rectTransform, centerX, centerY, width, height);
        image.raycastTarget = false;
        image.enabled = false;
        return image;
    }

    // Letterboxes the UV rect so illustrations keep their aspect in any frame.
    private static void FitArt(RawImage image)
    {
        if (image.texture == null) return;
        Rect rect = image.rectTransform.rect;
        if (rect.width <= 0f || rect.height <= 0f) return;
        float frame = rect.width / rect.height;
        float art = image.texture.width / (float)image.texture.height;
        image.uvRect = art > frame
            ? new Rect((1f - frame / art) * 0.5f, 0f, frame / art, 1f)
            : new Rect(0f, 0f, 1f, 1f);
        if (art < frame)
        {
            // Narrow art in a wide frame: shrink the drawn width instead of cropping height.
            float scale = art / frame;
            image.rectTransform.localScale = new Vector3(scale, 1f, 1f);
        }
        else
        {
            image.rectTransform.localScale = Vector3.one;
        }
    }

    private static void Focus(Selectable selectable)
    {
        if (EventSystem.current != null && selectable != null)
        {
            EventSystem.current.SetSelectedGameObject(selectable.gameObject);
        }
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        rect.localScale = Vector3.one;
        rect.localRotation = Quaternion.identity;
    }
}
