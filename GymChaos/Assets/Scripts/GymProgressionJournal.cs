using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The progression journal, drawn with the same Persona sheet as the settings
/// page and the boot menu: a navy plate with a red edge, red section headers
/// with slanted rules, Anton headings and Bebas labels. Level and experience,
/// the four stat trees, today's three goals, the gym-day streak, the race
/// ladder and what the tutorial has next, plus a live player model in the
/// clothes the player wears (drag to turn, wheel to zoom). TAB or I opens it
/// during normal play; while the tutorial runs it also carries the SKIP button
/// and the completion card.
/// </summary>
public sealed class GymProgressionJournal : MonoBehaviour
{
    private const float PanelWidth = 1240f;
    private const float PanelHeight = 760f;
    private const float Padding = 40f;
    private const float ColumnLeft = Padding;
    private const float ColumnLeftWidth = 360f;
    private const float ColumnPreview = 444f;
    private const float ColumnPreviewWidth = 420f;
    private const float ColumnRight = 902f;
    private const float ColumnRightWidth = 298f;

    // Opaque, exactly like the settings sheet: at 0.98 the accent plate behind
    // bleeds through as a red cast in linear space.
    private static readonly Color Sheet = new Color(0.03f, 0.036f, 0.058f, 1f);
    private static readonly Color BarBack = new Color(1f, 1f, 1f, 0.14f);

    private GymExperienceService service;
    private PlayerMovement player;
    private GameObject builtRoot;
    private GameObject veil;
    private GameObject journalPanel;
    private GameObject completionPanel;
    private GymJournalPreview preview;
    private Text subtitle;
    private Text levelLabel;
    private Text xpText;
    private RectTransform xpFill;
    private readonly Text[] statLabels = new Text[4];
    private readonly RectTransform[] statFills = new RectTransform[4];
    private readonly Text[] statValues = new Text[4];
    private readonly Text[] goalLabels = new Text[3];
    private readonly RectTransform[] goalFills = new RectTransform[3];
    private readonly Text[] goalValues = new Text[3];
    private Text raceTitle;
    private Text raceLine;
    private Text nextLabel;
    private Button skipButton;
    private float shownXp;
    private float fillBlend;
    private bool pausedGame;
    private float previousTimeScale = 1f;

    public static GymProgressionJournal Instance { get; private set; }
    public static bool IsOpen { get; private set; }
    public static bool WasSeen { get; private set; }
    public static bool CompletionPending { get; set; }
    public static string TeaserPending { get; set; } = string.Empty;

    /// <summary>Verifier access: opens the window without a key press.</summary>
    public void OpenForVerification() => Open();

    /// <summary>Verifier access: closes the window without a key press.</summary>
    public void CloseForVerification() => Close();

    /// <summary>Verifier access to the model view and its numbers.</summary>
    public GymJournalPreview Preview => preview;
    public bool PanelShown => journalPanel != null && journalPanel.activeSelf;
    public Button SkipButtonForVerification => skipButton;
    public bool CompletionShown => completionPanel != null && completionPanel.activeSelf;
    public string StatValueText(int index) =>
        index >= 0 && index < statValues.Length && statValues[index] != null
            ? statValues[index].text : string.Empty;

    public static GymProgressionJournal EnsureCreated(PlayerMovement targetPlayer)
    {
        if (Instance != null)
        {
            if (targetPlayer != null)
            {
                Instance.player = targetPlayer;
            }
            return Instance;
        }
        GameObject root = new GameObject("Gym Progression Journal");
        Instance = root.AddComponent<GymProgressionJournal>();
        Instance.player = targetPlayer;
        return Instance;
    }

    private GymProgressionState State => service != null ? service.State : null;

    private void Update()
    {
        if (service == null)
        {
            service = GymExperienceService.Active;
        }
        if (player == null && service != null)
        {
            player = service.Player;
        }
        if (service == null || player == null)
        {
            return;
        }

        if (GymRaceDirector.IsRaceActive && IsOpen)
        {
            Close();
        }
        if (IsOpen)
        {
            // The page owns the pointer: keep it free and visible over every
            // part of the window, including the tutorial card and the SKIP
            // button, so drag, zoom and the buttons all work.
            if (Cursor.lockState != CursorLockMode.None || !Cursor.visible)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
        }

        bool blocked = GymPauseMenu.IsVisible || GymStartScreen.IsMenuVisible;
        if (!blocked && (Input.GetKeyDown(KeyCode.Tab) || Input.GetKeyDown(KeyCode.I)))
        {
            if (IsOpen)
            {
                Close();
            }
            else
            {
                Open();
            }
        }
        if (IsOpen && (Input.GetKeyDown(KeyCode.Escape) || GymPauseMenu.IsVisible))
        {
            Close();
        }
        if (CompletionPending && builtRoot == null)
        {
            // The lesson can finish without the journal ever being opened: the
            // completion card is its own sheet and still has to appear.
            EnsureBuilt();
        }
        if (builtRoot != null)
        {
            Refresh();
        }
    }

    private void OnDestroy()
    {
        ResumeGame();
        if (Instance == this)
        {
            Instance = null;
            IsOpen = false;
            CompletionPending = false;
        }
    }

    private void Open()
    {
        if (State == null)
        {
            return;
        }
        IsOpen = true;
        WasSeen = true;
        EnsureBuilt();
        player.SetCursorCaptured(false);
        PauseGame();
        Debug.Log("GYMCHAOS_JOURNAL_OPEN", this);
        Refresh();
    }

    private void Close()
    {
        if (!IsOpen)
        {
            return;
        }
        IsOpen = false;
        if (builtRoot != null) builtRoot.SetActive(false);
        if (preview != null) preview.Hide();
        ResumeGame();
        if (player != null)
        {
            player.SetCursorCaptured(true);
        }
    }

    /// <summary>
    /// Reading the journal stops the gym: the player must not walk, swing or get
    /// hit while the page is open. A network room keeps its clock so the two
    /// clients do not drift apart.
    /// </summary>
    private void PauseGame()
    {
        if (pausedGame || GymNetSession.InRoom)
        {
            return;
        }
        previousTimeScale = Time.timeScale;
        Time.timeScale = 0f;
        pausedGame = true;
    }

    private void ResumeGame()
    {
        if (!pausedGame)
        {
            return;
        }
        Time.timeScale = previousTimeScale > 0f ? previousTimeScale : 1f;
        pausedGame = false;
    }

    // ------------------------------------------------------------------ build

    private void EnsureBuilt()
    {
        if (builtRoot != null)
        {
            builtRoot.SetActive(true);
            return;
        }
        GymRuntimeSettings.EnsureEventSystem();
        builtRoot = new GameObject("Journal Canvas", typeof(RectTransform));
        builtRoot.transform.SetParent(transform, false);
        Canvas canvas = builtRoot.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 60;
        CanvasScaler scaler = builtRoot.AddComponent<CanvasScaler>();
        GymRuntimeSettings.ConfigureBalancedCanvasScaler(scaler);
        scaler.matchWidthOrHeight = 1f;
        // Without this the page is a picture: no pointer events reach the SKIP
        // button, so it neither pops nor clicks while the gym is paused.
        builtRoot.AddComponent<GraphicRaycaster>();
        RectTransform canvasRect = (RectTransform)builtRoot.transform;

        Font titleFont = PersonaMenuStyle.LoadTitleFont(PersonaMenuStyle.BuiltinFont);
        Font labelFont = PersonaMenuStyle.LoadButtonFont(PersonaMenuStyle.BuiltinFont);

        // Navy veil like the settings page; it also swallows clicks aimed at the
        // game behind the window.
        veil = new GameObject("Journal Veil", typeof(RectTransform), typeof(Image));
        veil.transform.SetParent(canvasRect, false);
        Image veilImage = veil.GetComponent<Image>();
        veilImage.color = new Color(0.012f, 0.05f, 0.13f, 0.82f);
        veilImage.raycastTarget = true;
        Stretch((RectTransform)veil.transform);

        journalPanel = CreateSheet(canvasRect, "Journal Sheet", PanelWidth, PanelHeight);
        RectTransform panel = (RectTransform)journalPanel.transform;
        CreateHeading(panel, titleFont, "PROGRESSION");
        subtitle = Label("Subtitle", panel, labelFont, string.Empty, 30, PersonaMenuStyle.Ink,
            TextAnchor.MiddleLeft, Padding + 360f, 34f, 700f, 40f);
        BuildExperience(panel, labelFont);
        BuildStats(panel, titleFont, labelFont);
        BuildPreview(panel, labelFont);
        BuildGoals(panel, titleFont, labelFont);
        BuildRace(panel, titleFont, labelFont);

        completionPanel = CreateCompletion(canvasRect, titleFont, labelFont);
        completionPanel.SetActive(false);
        builtRoot.SetActive(true);
    }

    private GameObject CreateSheet(RectTransform parent, string name, float width, float height)
    {
        GameObject sheet = new GameObject(name, typeof(RectTransform));
        sheet.transform.SetParent(parent, false);
        RectTransform rect = (RectTransform)sheet.transform;
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(width, height);
        rect.anchoredPosition = Vector2.zero;
        rect.localRotation = Quaternion.Euler(0f, 0f, -0.8f);

        PersonaShape edge = PersonaMenuStyle.CreateShape("Sheet Accent Edge", rect, PersonaMenuStyle.Accent);
        Stretch(edge.rectTransform);
        edge.SetCorners(new Vector2(-0.012f, -0.012f), new Vector2(-0.02f, 0.01f),
            new Vector2(0.03f, 0.018f), new Vector2(0.012f, -0.02f));
        PersonaShape plate = PersonaMenuStyle.CreateShape("Sheet Plate", rect, Sheet);
        Stretch(plate.rectTransform);
        plate.SetCorners(Vector2.zero, Vector2.zero, new Vector2(0.018f, 0f), Vector2.zero);
        plate.raycastTarget = true;
        return sheet;
    }

    private void CreateHeading(RectTransform panel, Font titleFont, string heading)
    {
        GameObject title = new GameObject("Heading Plate", typeof(RectTransform));
        title.transform.SetParent(panel, false);
        RectTransform rect = (RectTransform)title.transform;
        Place(rect, Padding - 8f, 20f, 336f, 88f);
        rect.localRotation = Quaternion.Euler(0f, 0f, 2.2f);

        PersonaShape backing = PersonaMenuStyle.CreateShape("Heading Backing", rect, Color.white);
        Stretch(backing.rectTransform);
        backing.SetCorners(new Vector2(-0.04f, -0.12f), new Vector2(-0.03f, 0.1f),
            new Vector2(0.06f, 0.12f), new Vector2(0.04f, -0.1f));
        PersonaShape plate = PersonaMenuStyle.CreateShape("Heading Plate", rect, PersonaMenuStyle.Accent);
        Stretch(plate.rectTransform);
        plate.SetCorners(Vector2.zero, new Vector2(0.01f, 0f), new Vector2(0.04f, 0f),
            new Vector2(0.02f, 0f));

        Text text = Label("Heading", rect, titleFont, heading, 52, Color.white,
            TextAnchor.MiddleCenter, 0f, 0f, 0f, 0f);
        text.rectTransform.anchorMin = Vector2.zero;
        text.rectTransform.anchorMax = Vector2.one;
        text.rectTransform.offsetMin = Vector2.zero;
        text.rectTransform.offsetMax = Vector2.zero;
        Shadow shadow = text.gameObject.AddComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.85f);
        shadow.effectDistance = new Vector2(3f, -3f);
    }

    private void BuildExperience(RectTransform panel, Font labelFont)
    {
        levelLabel = Label("Level", panel, labelFont, "LEVEL 1", 30, PersonaMenuStyle.Gold,
            TextAnchor.MiddleLeft, Padding, 130f, 150f, 32f);
        xpText = Label("Xp", panel, labelFont, string.Empty, 26, PersonaMenuStyle.Muted,
            TextAnchor.MiddleRight, 640f, 130f, 560f, 32f);
        RectTransform back = Bar("Xp Bar", panel, Padding, 168f, PanelWidth - Padding * 2f, 16f);
        xpFill = Fill(back, PersonaMenuStyle.Gold);
    }

    private void BuildStats(RectTransform panel, Font titleFont, Font labelFont)
    {
        SectionHeader(panel, labelFont, "Stat Trees Header", "STAT TREES", ColumnLeft, 220f,
            ColumnLeftWidth);
        string[] names = { "STRENGTH", "ENDURANCE", "TECHNIQUE", "REPUTATION" };
        for (int i = 0; i < names.Length; i++)
        {
            float y = 268f + i * 66f;
            statLabels[i] = Label("Stat " + names[i], panel, labelFont, names[i], 30,
                PersonaMenuStyle.Ink, TextAnchor.MiddleLeft, ColumnLeft, y, 168f, 34f);
            RectTransform back = Bar("Stat Bar " + names[i], panel, ColumnLeft + 172f, y + 12f,
                120f, 12f);
            statFills[i] = Fill(back, PersonaMenuStyle.Gold);
            statValues[i] = Label("Stat Value " + names[i], panel, titleFont, "0", 40,
                PersonaMenuStyle.Gold, TextAnchor.MiddleRight, ColumnLeft + 296f, y, 64f, 36f);
        }

        // Next lesson and the tutorial's own escape hatch, under the trees.
        SectionHeader(panel, labelFont, "Next Lesson Header", "NEXT LESSON", ColumnLeft, 558f,
            ColumnLeftWidth);
        nextLabel = Label("Next Lesson", panel, labelFont, string.Empty, 26, PersonaMenuStyle.Ink,
            TextAnchor.UpperLeft, ColumnLeft, 602f, ColumnLeftWidth, 74f);
        nextLabel.horizontalOverflow = HorizontalWrapMode.Wrap;
        nextLabel.resizeTextForBestFit = true;
        nextLabel.resizeTextMinSize = 16;
        nextLabel.resizeTextMaxSize = 26;
        skipButton = PersonaMenuStyle.CreateLayoutButton(panel, "Skip Tutorial Button", labelFont,
            "SKIP TUTORIAL", new Vector2(ColumnLeft, 676f), new Vector2(250f, 52f), 26,
            SkipTutorial);
        skipButton.gameObject.SetActive(false);
    }

    private void BuildPreview(RectTransform panel, Font labelFont)
    {
        GameObject frame = new GameObject("Preview Frame", typeof(RectTransform));
        frame.transform.SetParent(panel, false);
        RectTransform rect = (RectTransform)frame.transform;
        Place(rect, ColumnPreview, 220f, ColumnPreviewWidth, 468f);

        // No card, no outline: the view paints the sheet's own navy, so any
        // limb the idle clip swings wide overlaps the page instead of being
        // cut by a frame edge.
        GameObject view = new GameObject("Preview View", typeof(RectTransform), typeof(RawImage));
        view.transform.SetParent(rect, false);
        RectTransform viewRect = (RectTransform)view.transform;
        viewRect.anchorMin = Vector2.zero;
        viewRect.anchorMax = Vector2.one;
        viewRect.offsetMin = Vector2.zero;
        viewRect.offsetMax = Vector2.zero;
        RawImage image = view.GetComponent<RawImage>();
        image.raycastTarget = true;
        image.color = Color.white;

        preview = GymJournalPreview.Create(rect);
        preview.SetViewRect(viewRect);
        image.texture = preview.Target;

    }

    private void BuildGoals(RectTransform panel, Font titleFont, Font labelFont)
    {
        SectionHeader(panel, labelFont, "Goals Header", "TODAY'S GOALS", ColumnRight, 220f,
            ColumnRightWidth);
        for (int i = 0; i < 3; i++)
        {
            float y = 266f + i * 96f;
            goalLabels[i] = Label("Goal " + i, panel, labelFont, string.Empty, 26,
                PersonaMenuStyle.Ink, TextAnchor.UpperLeft, ColumnRight, y, ColumnRightWidth - 56f, 56f);
            goalLabels[i].horizontalOverflow = HorizontalWrapMode.Wrap;
            goalLabels[i].resizeTextForBestFit = true;
            goalLabels[i].resizeTextMinSize = 16;
            goalLabels[i].resizeTextMaxSize = 26;
            goalValues[i] = Label("Goal Value " + i, panel, titleFont, "0", 34,
                PersonaMenuStyle.Gold, TextAnchor.MiddleRight, ColumnRight + ColumnRightWidth - 60f,
                y - 4f, 60f, 34f);
            RectTransform back = Bar("Goal Bar " + i, panel, ColumnRight, y + 62f,
                ColumnRightWidth, 10f);
            goalFills[i] = Fill(back, PersonaMenuStyle.Accent);
        }
    }

    private void BuildRace(RectTransform panel, Font titleFont, Font labelFont)
    {
        SectionHeader(panel, labelFont, "Race Header", "RACE LADDER", ColumnRight, 560f,
            ColumnRightWidth);
        raceTitle = Label("Race Difficulty", panel, titleFont, string.Empty, 34,
            PersonaMenuStyle.Gold, TextAnchor.MiddleLeft, ColumnRight, 600f, ColumnRightWidth, 36f);
        raceLine = Label("Race Line", panel, labelFont, string.Empty, 24, PersonaMenuStyle.Ink,
            TextAnchor.UpperLeft, ColumnRight, 640f, ColumnRightWidth, 60f);
        raceLine.horizontalOverflow = HorizontalWrapMode.Wrap;
    }

    private GameObject CreateCompletion(RectTransform parent, Font titleFont, Font labelFont)
    {
        GameObject sheet = CreateSheet(parent, "Completion Sheet", 880f, 420f);
        RectTransform panel = (RectTransform)sheet.transform;

        GameObject title = new GameObject("Completion Heading", typeof(RectTransform));
        title.transform.SetParent(panel, false);
        RectTransform rect = (RectTransform)title.transform;
        Place(rect, 62f, 34f, 400f, 76f);
        rect.localRotation = Quaternion.Euler(0f, 0f, 2f);
        PersonaShape backing = PersonaMenuStyle.CreateShape("Heading Backing", rect, Color.white);
        Stretch(backing.rectTransform);
        backing.SetCorners(new Vector2(-0.04f, -0.12f), new Vector2(-0.03f, 0.1f),
            new Vector2(0.06f, 0.12f), new Vector2(0.04f, -0.1f));
        PersonaShape plate = PersonaMenuStyle.CreateShape("Heading Plate", rect, PersonaMenuStyle.Accent);
        Stretch(plate.rectTransform);
        Text text = Label("Heading", rect, titleFont, "TUTORIAL COMPLETE", 42, Color.white,
            TextAnchor.MiddleCenter, 0f, 0f, 0f, 0f);
        text.rectTransform.anchorMin = Vector2.zero;
        text.rectTransform.anchorMax = Vector2.one;
        text.rectTransform.offsetMin = Vector2.zero;
        text.rectTransform.offsetMax = Vector2.zero;

        Text body = Label("Completion Body", panel, labelFont,
            "You have seen the floor, the locker, a set, the counter, the journal and a race.\n\n" +
            "TODAY'S GOALS are in the journal: three of them, every gym day.\n" +
            "NEXT UNLOCK:  higher-level gear in the locker and a harder race.\n" +
            "The race sign now offers every difficulty you have opened.",
            26, PersonaMenuStyle.Ink, TextAnchor.UpperLeft, 62f, 128f, 756f, 170f);
        body.horizontalOverflow = HorizontalWrapMode.Wrap;
        body.resizeTextForBestFit = true;
        body.resizeTextMinSize = 18;
        body.resizeTextMaxSize = 26;

        PersonaMenuStyle.CreateLayoutButton(panel, "Completion Train Button", labelFont,
            "LET'S TRAIN", new Vector2(62f, 330f), new Vector2(240f, 56f), 28,
            () => { CompletionPending = false; });
        PersonaMenuStyle.CreateLayoutButton(panel, "Completion Journal Button", labelFont,
            "OPEN JOURNAL", new Vector2(320f, 330f), new Vector2(250f, 56f), 28,
            () => { CompletionPending = false; Open(); });
        return sheet;
    }

    // ---------------------------------------------------------------- refresh

    private void Refresh()
    {
        GymProgressionState state = State;
        if (state == null) return;

        bool completion = CompletionPending;
        bool journal = IsOpen && !completion;
        bool visible = completion || journal;
        if (builtRoot != null && builtRoot.activeSelf != visible)
        {
            builtRoot.SetActive(visible);
        }
        if (!visible)
        {
            if (journalPanel != null && journalPanel.activeSelf) journalPanel.SetActive(false);
            if (completionPanel != null && completionPanel.activeSelf) completionPanel.SetActive(false);
            if (preview != null) preview.Hide();
            return;
        }
        if (completionPanel != null && completionPanel.activeSelf != completion)
        {
            completionPanel.SetActive(completion);
        }
        if (journalPanel != null && journalPanel.activeSelf != journal)
        {
            journalPanel.SetActive(journal);
        }
        if (veil != null && veil.activeSelf != visible)
        {
            veil.SetActive(visible);
        }
        if (!journal)
        {
            if (preview != null) preview.Hide();
            return;
        }

        subtitle.text = "DAY " + state.gymDay + "   ·   LEVEL " + state.level +
            "   ·   REPUTATION " + state.reputation + "   ·   STAMPS " + state.loyaltyStamps;
        int next = Mathf.Max(1, service.ExperienceToNextLevel);
        levelLabel.text = "LEVEL " + state.level;
        shownXp = Mathf.Lerp(shownXp, state.experience, 0.18f);
        fillBlend = Mathf.Lerp(fillBlend, Mathf.Clamp01(shownXp / next), 0.25f);
        SetFill(xpFill, fillBlend);
        xpText.text = Mathf.RoundToInt(state.experience) + " / " + next + " XP   —   " +
            Mathf.Max(0f, next - state.experience).ToString("0") + " TO LEVEL " + (state.level + 1);

        GymStat[] stats = { GymStat.Strength, GymStat.Endurance, GymStat.Technique, GymStat.Reputation };
        for (int i = 0; i < stats.Length; i++)
        {
            int rank = service.GetStatRank(stats[i]);
            // A rank, not a fraction of the ladder: the number is what the
            // player has, the bar is how full the tree is.
            statValues[i].text = rank.ToString();
            SetFill(statFills[i], Mathf.Clamp01(rank / (float)GymExperienceService.MaxStatRank));
        }

        for (int i = 0; i < 3; i++)
        {
            int target = Mathf.Max(1, service.DailyGoalTarget(i));
            int progress = state.dailyProgress != null && i < state.dailyProgress.Length
                ? state.dailyProgress[i] : 0;
            bool done = state.dailyCompleted != null && i < state.dailyCompleted.Length &&
                state.dailyCompleted[i];
            goalLabels[i].text = (done ? "DONE  " : string.Empty) + service.DailyGoalLabel(i);
            goalValues[i].text = Mathf.Min(progress, target).ToString();
            SetFill(goalFills[i], Mathf.Clamp01(progress / (float)target));
        }

        raceTitle.text = GymRaceDirector.DifficultyNames[
            Mathf.Clamp(state.raceDifficultyUnlocked, 0, 2)].ToUpperInvariant();
        string bestLap = state.raceBestLapMs > 0
            ? (state.raceBestLapMs / 1000f).ToString("0.00") + " S" : "—";
        raceLine.text = "STARTS " + state.raceStarts + "   ·   WINS " + state.raceWins +
            "\nBEST LAP  " + bestLap;

        string teaser = !string.IsNullOrEmpty(TeaserPending)
            ? TeaserPending
            : GymTutorialDirector.NextTeaser(state);
        bool tutorial = !string.IsNullOrEmpty(teaser);
        skipButton.gameObject.SetActive(tutorial);
        nextLabel.text = tutorial ? teaser : "Level " + (state.level + 1) +
            " opens new gear and a harder race.";

        if (preview != null)
        {
            string classId = GymSessionService.ActiveClass != null
                ? GymSessionService.ActiveClass.id : null;
            preview.Show(classId, state);
        }
    }

    private void SkipTutorial()
    {
        GymTutorialDirector.SkipAll(State);
        GymHud.Active?.PushNotice("Tutorial skipped.", 2.5f, 0);
        Close();
    }

    // ------------------------------------------------------------------ pieces

    private void SectionHeader(RectTransform parent, Font font, string name, string content,
        float x, float y, float width)
    {
        Label(name, parent, font, content, 30, PersonaMenuStyle.Accent, TextAnchor.MiddleLeft,
            x, y, width - 84f, 40f);
        PersonaShape rule = PersonaMenuStyle.CreateShape(name + " Rule", parent,
            new Color(PersonaMenuStyle.Accent.r, PersonaMenuStyle.Accent.g,
                PersonaMenuStyle.Accent.b, 0.55f));
        RectTransform rect = rule.rectTransform;
        Place(rect, x + 150f, y + 18f, Mathf.Max(40f, width - 150f), 4f);
        rule.SetSlant(0.35f);
    }

    private RectTransform Bar(string name, RectTransform parent, float x, float y,
        float width, float height)
    {
        GameObject back = new GameObject(name, typeof(RectTransform));
        back.transform.SetParent(parent, false);
        RectTransform rect = (RectTransform)back.transform;
        Place(rect, x, y, width, height);
        PersonaShape shape = PersonaMenuStyle.CreateShape("Back", rect, BarBack);
        Stretch(shape.rectTransform);
        shape.SetSlant(0.02f);
        return rect;
    }

    private RectTransform Fill(RectTransform bar, Color color)
    {
        GameObject fill = new GameObject("Fill", typeof(RectTransform));
        fill.transform.SetParent(bar, false);
        RectTransform rect = (RectTransform)fill.transform;
        rect.anchorMin = new Vector2(0f, 0f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = new Vector2(0f, 0f);
        PersonaShape shape = PersonaMenuStyle.CreateShape("Fill Shape", rect, color);
        Stretch(shape.rectTransform);
        shape.SetSlant(0.12f);
        return rect;
    }

    private static void SetFill(RectTransform fill, float fraction)
    {
        if (fill == null) return;
        RectTransform parent = fill.parent as RectTransform;
        float width = parent != null ? parent.rect.width : 0f;
        fill.sizeDelta = new Vector2(Mathf.Max(0f, width * Mathf.Clamp01(fraction)), 0f);
    }

    private static Text Label(string name, RectTransform parent, Font font, string content,
        int size, Color color, TextAnchor alignment, float x, float y, float width, float height)
    {
        GameObject textObject = new GameObject(name, typeof(RectTransform), typeof(Text));
        textObject.transform.SetParent(parent, false);
        Text text = textObject.GetComponent<Text>();
        text.font = font;
        text.text = content;
        text.fontSize = size;
        text.color = color;
        text.alignment = alignment;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.raycastTarget = false;
        if (width > 0f || height > 0f)
        {
            Place(text.rectTransform, x, y, width, height);
        }
        return text;
    }

    // Top-left placement inside a panel: fixed pixel insets in canvas units.
    private static void Place(RectTransform rect, float x, float y, float width, float height)
    {
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(x, -y);
        rect.sizeDelta = new Vector2(width, height);
        rect.localScale = Vector3.one;
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
