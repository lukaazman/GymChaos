using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Gameplay HUD in the shared menu palette and type. Nothing here reacts to
/// the pointer, so every element is an upright rectangle: health and stamina meters
/// with a delayed trailing bar, the progression and goal plates, a bounded
/// prioritized notice queue and the save indicator. Text and geometry are
/// rewritten only when the underlying value changes.
/// </summary>
[DefaultExecutionOrder(50)]
public sealed class GymHud : MonoBehaviour
{
    public const int MaxVisibleNotices = 3;
    public const int MaxQueuedNotices = 6;
    private const float TrailDelaySeconds = 0.35f;
    private const float TrailSpeed = 0.6f;
    private const float SaveIndicatorSeconds = 2.2f;

    private sealed class Meter
    {
        public RectTransform fill;
        public RectTransform trail;
        public PersonaShape fillShape;
        public Text value;
        public Text state;
        public float shown = -1f;
        public float trailValue = 1f;
        public float trailHoldUntil;
        public int shownCurrent = int.MinValue;
        public int shownMaximum = int.MinValue;
        public Color baseColor;
    }

    private sealed class Notice
    {
        public string text;
        public int priority;
        public int count;
        public float expiresAt;
        public float seconds;
    }

    private static GymHud instance;

    private PlayerMovement player;
    private GymExperienceService progression;
    private CanvasGroup rootGroup;
    private GameObject vitalsRoot;
    private GameObject progressRoot;
    private Meter health;
    private Meter stamina;
    private RawImage classArt;
    private Text levelBadge;
    private Text membersLabel;
    private Text heldLabel;
    private Text levelLabel;
    private Text statsLabel;
    private Text reputationLabel;
    private RectTransform experienceFill;
    private readonly RectTransform[] goalFills = new RectTransform[3];
    private readonly PersonaShape[] goalShapes = new PersonaShape[3];
    private readonly Text[] goalCounts = new Text[3];
    private Text dayLabel;
    private readonly Text[] noticeLabels = new Text[MaxVisibleNotices];
    private readonly GameObject[] noticeRoots = new GameObject[MaxVisibleNotices];
    private readonly PersonaShape[] noticeRims = new PersonaShape[MaxVisibleNotices];
    private readonly List<Notice> notices = new List<Notice>();
    private bool noticesDirty;
    private GameObject saveRoot;
    private Text saveLabel;
    private CanvasGroup saveGroup;
    private float saveHideAt;

    private int shownLevel = int.MinValue;
    private int shownExperience = int.MinValue;
    private int shownStats = int.MinValue;
    private int shownReputation = int.MinValue;
    private int shownMembers = int.MinValue;
    private int shownDay = int.MinValue;
    private string shownHeld;
    private string shownClass;
    private readonly int[] shownGoalProgress = { int.MinValue, int.MinValue, int.MinValue };
    private readonly bool[] shownGoalDone = new bool[3];
    private bool shownExhausted;
    private bool shownSprinting;
    private bool visible = true;

    public static bool IsActive => instance != null;
    public static GymHud Active => instance;
    public int QueuedNoticeCount => notices.Count;
    public string TopNoticeText => notices.Count > 0 ? notices[0].text : string.Empty;
    public float StaminaMaximumShown => stamina != null ? stamina.shownMaximum : 0f;
    public float HealthFillShown => health != null ? health.shown : 0f;
    public string SaveIndicatorText => saveLabel != null ? saveLabel.text : string.Empty;

    public static GymHud CreateForScene(PlayerMovement targetPlayer)
    {
        if (instance != null)
        {
            instance.Bind(targetPlayer);
            return instance;
        }
        GameObject root = new GameObject("Gym HUD");
        instance = root.AddComponent<GymHud>();
        instance.Build();
        instance.Bind(targetPlayer);
        Debug.Log("GYMCHAOS_HUD_READY", instance);
        return instance;
    }

    /// <summary>Rebinds after a load: stale notices and cached values are dropped.</summary>
    private void Bind(PlayerMovement targetPlayer)
    {
        Unbind();
        player = targetPlayer;
        progression = GymExperienceService.Active;
        if (progression != null)
        {
            progression.NoticeRaised += PushNotice;
        }
        GymSessionService.SaveStatusChanged += HandleSaveStatus;
        notices.Clear();
        noticesDirty = true;
        shownLevel = shownExperience = shownStats = shownReputation = shownMembers = shownDay = int.MinValue;
        shownHeld = null;
        shownClass = null;
        for (int i = 0; i < shownGoalProgress.Length; i++) shownGoalProgress[i] = int.MinValue;
        health.shown = stamina.shown = -1f;
        health.shownCurrent = stamina.shownCurrent = int.MinValue;
        // Start both meters (and their trails) full so a load never shows a
        // stale trail draining; the first refresh sets the real values.
        foreach (Meter meter in new[] { health, stamina })
        {
            meter.trailValue = 1f;
            meter.trailHoldUntil = 0f;
            SetFraction(meter.trail, 1f);
            SetFraction(meter.fill, 1f);
        }
    }

    private void Unbind()
    {
        if (progression != null)
        {
            progression.NoticeRaised -= PushNotice;
        }
        GymSessionService.SaveStatusChanged -= HandleSaveStatus;
        progression = null;
    }

    private void OnDestroy()
    {
        Unbind();
        if (instance == this)
        {
            instance = null;
        }
    }

    // ---- notices ----

    /// <summary>
    /// Priority 2 (critical) goes first and may evict lower entries; repeated
    /// text merges into one line with a counter instead of stacking.
    /// </summary>
    public void PushNotice(string text, float seconds, int priority)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }
        float now = Time.unscaledTime;
        seconds = Mathf.Clamp(seconds, 0.8f, 6f);
        for (int i = 0; i < notices.Count; i++)
        {
            if (notices[i].text == text)
            {
                notices[i].count++;
                notices[i].expiresAt = now + notices[i].seconds;
                noticesDirty = true;
                return;
            }
        }

        var notice = new Notice
        {
            text = text, priority = priority, count = 1, seconds = seconds, expiresAt = now + seconds
        };
        int insertAt = notices.Count;
        for (int i = 0; i < notices.Count; i++)
        {
            if (priority > notices[i].priority)
            {
                insertAt = i;
                break;
            }
        }
        notices.Insert(insertAt, notice);
        while (notices.Count > MaxQueuedNotices)
        {
            // The list is priority ordered, so the tail is the least important.
            notices.RemoveAt(notices.Count - 1);
        }
        noticesDirty = true;
    }

    private void TickNotices()
    {
        float now = Time.unscaledTime;
        // Only visible lines age; queued ones wait their turn with a full timer.
        for (int i = Mathf.Min(notices.Count, MaxVisibleNotices) - 1; i >= 0; i--)
        {
            if (now >= notices[i].expiresAt)
            {
                notices.RemoveAt(i);
                noticesDirty = true;
            }
        }
        for (int i = MaxVisibleNotices; i < notices.Count; i++)
        {
            notices[i].expiresAt = now + notices[i].seconds;
        }
        if (!noticesDirty)
        {
            return;
        }
        noticesDirty = false;
        for (int i = 0; i < MaxVisibleNotices; i++)
        {
            bool shown = i < notices.Count;
            if (noticeRoots[i].activeSelf != shown) noticeRoots[i].SetActive(shown);
            if (!shown) continue;
            Notice notice = notices[i];
            noticeLabels[i].text = notice.count > 1 ? notice.text + "   x" + notice.count : notice.text;
            noticeRims[i].color = notice.priority >= 2 ? PersonaMenuStyle.Gold :
                notice.priority == 1 ? PersonaMenuStyle.Accent : PersonaMenuStyle.Muted;
        }
    }

    private void HandleSaveStatus(GymSaveStatus status, string message)
    {
        if (saveRoot == null)
        {
            return;
        }
        if (status == GymSaveStatus.Idle)
        {
            saveRoot.SetActive(false);
            return;
        }
        saveRoot.SetActive(true);
        saveGroup.alpha = 1f;
        saveLabel.text = status == GymSaveStatus.Saving ? "SAVING" :
            status == GymSaveStatus.Saved ? "SAVED" : "SAVE FAILED";
        saveLabel.color = status == GymSaveStatus.Failed ? PersonaMenuStyle.Accent : PersonaMenuStyle.Ink;
        saveHideAt = status == GymSaveStatus.Saving
            ? float.PositiveInfinity
            : Time.unscaledTime + SaveIndicatorSeconds;
        if (status == GymSaveStatus.Failed)
        {
            PushNotice(message, 5f, 2);
        }
    }

    // ---- per-frame refresh (change detection only) ----

#if UNITY_EDITOR
    // Verifier-only cost probe for the per-frame refresh.
    public static long ProfiledTicks;
    public static long ProfiledBytes;
    public static int ProfiledFrames;

    private void LateUpdate()
    {
        long bytes = System.GC.GetAllocatedBytesForCurrentThread();
        long ticks = System.Diagnostics.Stopwatch.GetTimestamp();
        Refresh();
        ProfiledTicks += System.Diagnostics.Stopwatch.GetTimestamp() - ticks;
        ProfiledBytes += System.GC.GetAllocatedBytesForCurrentThread() - bytes;
        ProfiledFrames++;
    }
#else
    private void LateUpdate()
    {
        Refresh();
    }
#endif

    private void Refresh()
    {
        if (progression != GymExperienceService.Active && GymExperienceService.Active != null)
        {
            // The progression service appeared (or was replaced) after binding.
            if (progression != null) progression.NoticeRaised -= PushNotice;
            progression = GymExperienceService.Active;
            progression.NoticeRaised += PushNotice;
        }
        bool shouldShow = player != null && GymArenaBootstrap.IsGameplayStarted &&
            !GymStartScreen.IsMenuVisible && !GymPauseMenu.IsVisible &&
            !GymDialogueDirector.IsDialogueActive &&
            (progression == null || !progression.IsLockerMenuOpen);
        if (shouldShow != visible)
        {
            visible = shouldShow;
            rootGroup.alpha = visible ? 1f : 0f;
        }

        if (saveRoot.activeSelf && Time.unscaledTime >= saveHideAt)
        {
            saveGroup.alpha = Mathf.MoveTowards(saveGroup.alpha, 0f, Time.unscaledDeltaTime * 3f);
            if (saveGroup.alpha <= 0f) saveRoot.SetActive(false);
        }
        TickNotices();
        if (!visible)
        {
            return;
        }

        bool vitals = player.HudShowsVitals;
        if (vitalsRoot.activeSelf != vitals) vitalsRoot.SetActive(vitals);
        if (vitals)
        {
            float capacity = progression != null ? progression.GetSprintCapacity() : 100f;
            UpdateMeter(health, player.CurrentHealth, player.MaxHealth);
            UpdateMeter(stamina, player.SprintEnergy, capacity);
            RefreshSprintState();
            RefreshIdentity();
        }

        bool showProgress = progression != null && progression.State != null;
        if (progressRoot.activeSelf != showProgress) progressRoot.SetActive(showProgress);
        if (showProgress)
        {
            RefreshProgress();
        }
    }

    private static void UpdateMeter(Meter meter, float current, float maximum)
    {
        // Zero, negative and non-finite maxima must never divide or overflow the track.
        float safeMaximum = float.IsNaN(maximum) || float.IsInfinity(maximum) || maximum <= 0f ? 0f : maximum;
        float safeCurrent = float.IsNaN(current) || float.IsInfinity(current) ? 0f : current;
        float fraction = safeMaximum > 0f ? Mathf.Clamp01(safeCurrent / safeMaximum) : 0f;
        if (Mathf.Abs(fraction - meter.shown) > 0.0005f)
        {
            if (meter.shown >= 0f && fraction < meter.shown)
            {
                // Hold the pale trail at the old value, then let it catch up.
                meter.trailHoldUntil = Time.unscaledTime + TrailDelaySeconds;
            }
            else if (fraction > meter.trailValue)
            {
                meter.trailValue = fraction;
                SetFraction(meter.trail, fraction);
            }
            meter.shown = fraction;
            SetFraction(meter.fill, fraction);
        }
        if (meter.trailValue > meter.shown && Time.unscaledTime >= meter.trailHoldUntil)
        {
            meter.trailValue = Mathf.MoveTowards(meter.trailValue, meter.shown, TrailSpeed * Time.unscaledDeltaTime);
            SetFraction(meter.trail, meter.trailValue);
        }

        int shownCurrent = Mathf.CeilToInt(Mathf.Clamp(safeCurrent, 0f, safeMaximum));
        int shownMaximum = Mathf.CeilToInt(safeMaximum);
        if (shownCurrent != meter.shownCurrent || shownMaximum != meter.shownMaximum)
        {
            meter.shownCurrent = shownCurrent;
            meter.shownMaximum = shownMaximum;
            meter.value.text = shownCurrent.ToString(CultureInfo.InvariantCulture) + " / " +
                shownMaximum.ToString(CultureInfo.InvariantCulture);
        }
    }

    private static void SetFraction(RectTransform rect, float fraction)
    {
        rect.anchorMax = new Vector2(Mathf.Clamp01(fraction), 1f);
    }

    // Sprint feedback mirrors PlayerMovement's own state; no sprint rules live here.
    private void RefreshSprintState()
    {
        bool exhausted = player.IsSprintExhausted;
        bool sprinting = player.IsSprinting;
        if (exhausted == shownExhausted && sprinting == shownSprinting)
        {
            return;
        }
        shownExhausted = exhausted;
        shownSprinting = sprinting;
        stamina.state.text = SprintStateText(exhausted, sprinting);
        stamina.state.color = PersonaMenuStyle.Accent;
        stamina.fillShape.color = exhausted ? PersonaMenuStyle.Muted : stamina.baseColor;
    }

    /// <summary>Only exhaustion gets a line; plain sprinting shows no popup.</summary>
    public static string SprintStateText(bool exhausted, bool sprinting)
    {
        return exhausted ? "EXHAUSTED  -  RELEASE TO RECOVER" : string.Empty;
    }

    private void RefreshIdentity()
    {
        string classId = GymSessionService.ActiveClass.id;
        if (classId != shownClass)
        {
            shownClass = classId;
            // The class picture replaces the old name plate; square crop of the 4:5 art.
            classArt.texture = Resources.Load<Texture2D>(GymSessionService.ActiveClass.artwork);
            classArt.enabled = classArt.texture != null;
            classArt.uvRect = new Rect(0.06f, 0.15f, 0.88f, 0.704f);
        }
        int members = GymMemberRoster.GetDisplayCount(player);
        if (members != shownMembers)
        {
            shownMembers = members;
            membersLabel.text = "MEMBERS  " + members.ToString("00", CultureInfo.InvariantCulture);
        }
        string held = player.HeldItemDisplayName;
        if (held != shownHeld)
        {
            shownHeld = held;
            heldLabel.text = string.IsNullOrEmpty(held) ? string.Empty : "HELD  " + held.ToUpperInvariant();
        }
    }

    private void RefreshProgress()
    {
        GymProgressionState state = progression.State;
        if (state.level != shownLevel || state.experience != shownExperience)
        {
            shownLevel = state.level;
            shownExperience = state.experience;
            int toNext = Mathf.Max(1, GymExperienceService.GetExperienceToNextLevel(state.level));
            levelLabel.text = "LEVEL " + state.level + "    XP " + state.experience + " / " + toNext;
            levelBadge.text = state.level.ToString(CultureInfo.InvariantCulture);
            SetFraction(experienceFill, state.experience / (float)toNext);
        }
        int stats = state.strengthRank | (state.enduranceRank << 5) | (state.techniqueRank << 10);
        if (stats != shownStats)
        {
            shownStats = stats;
            statsLabel.text = "STR " + state.strengthRank + "   END " + state.enduranceRank +
                "   TECH " + state.techniqueRank;
        }
        int reputation = state.reputation * 1000 + state.masteryRank;
        if (reputation != shownReputation)
        {
            shownReputation = reputation;
            reputationLabel.text = "REP " + state.reputation.ToString("+#;-#;0", CultureInfo.InvariantCulture) +
                "   MASTERY " + state.masteryRank;
        }
        if (state.gymDay != shownDay)
        {
            shownDay = state.gymDay;
            dayLabel.text = "DAY " + state.gymDay + "   GOALS";
        }
        for (int i = 0; i < goalFills.Length && i < progression.DailyGoalCount; i++)
        {
            int value = state.dailyProgress[i];
            bool done = state.dailyCompleted[i];
            if (value == shownGoalProgress[i] && done == shownGoalDone[i]) continue;
            shownGoalProgress[i] = value;
            shownGoalDone[i] = done;
            int target = Mathf.Max(1, progression.GetDailyGoalTarget(i));
            SetFraction(goalFills[i], value / (float)target);
            goalShapes[i].color = done ? PersonaMenuStyle.Gold : PersonaMenuStyle.Accent;
            goalCounts[i].text = done ? "DONE" : value + " / " + target;
        }
    }

    // ---- construction ----

    private const float Border = 3f;

    private void Build()
    {
        Canvas canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        // Below the start (100) and pause (200) menus.
        canvas.sortingOrder = 50;
        CanvasScaler scaler = gameObject.AddComponent<CanvasScaler>();
        GymRuntimeSettings.ConfigureBalancedCanvasScaler(scaler);
        rootGroup = gameObject.AddComponent<CanvasGroup>();
        // The HUD never takes input, so gameplay clicks pass straight through.
        rootGroup.interactable = false;
        rootGroup.blocksRaycasts = false;

        Font fallback = PersonaMenuStyle.BuiltinFont;
        Font body = PersonaMenuStyle.LoadButtonFont(fallback);
        Font title = PersonaMenuStyle.LoadTitleFont(fallback);

        vitalsRoot = Corner("Vitals", new Vector2(0f, 1f), new Vector2(36f, -30f), new Vector2(600f, 150f));
        BuildVitals(vitalsRoot.transform, body, title);
        progressRoot = Corner("Progress", new Vector2(1f, 1f), new Vector2(-36f, -30f), new Vector2(400f, 250f));
        BuildProgress(progressRoot.transform, body);
        GameObject noticeRoot = Corner("Notices", new Vector2(0.5f, 0f), new Vector2(0f, 190f), new Vector2(760f, 160f));
        BuildNotices(noticeRoot.transform, body);
        saveRoot = Corner("Save Indicator", new Vector2(1f, 0f), new Vector2(-36f, 34f), new Vector2(230f, 46f));
        BuildSaveIndicator(saveRoot.transform, body);
        saveRoot.SetActive(false);
    }

    // Corner-anchored blocks keep every element inside the safe area at any aspect ratio.
    private GameObject Corner(string name, Vector2 anchor, Vector2 offset, Vector2 size)
    {
        GameObject block = new GameObject(name, typeof(RectTransform));
        block.transform.SetParent(transform, false);
        RectTransform rect = block.GetComponent<RectTransform>();
        rect.anchorMin = anchor;
        rect.anchorMax = anchor;
        rect.pivot = anchor;
        rect.anchoredPosition = offset;
        rect.sizeDelta = size;
        return block;
    }

    // Level box centre relative to the picture's bottom-right corner, and the
    // left edge of the HP / stamina / members column, clear of that box.
    public static readonly Vector2 LevelBoxOffset = new Vector2(9f, -7f);
    public const float StatsLeft = 166f;

    private void BuildVitals(Transform parent, Font body, Font title)
    {
        // Class picture in a white-rimmed square, level in a small box on its corner.
        RectTransform emblem = Box(parent, "Class Picture", new Vector2(0f, 0f), new Vector2(112f, 112f));
        Panel(emblem, PersonaMenuStyle.Ink, PersonaMenuStyle.PlateBlack);
        GameObject artObject = new GameObject("Art", typeof(RectTransform), typeof(RawImage));
        artObject.transform.SetParent(emblem, false);
        classArt = artObject.GetComponent<RawImage>();
        Fill(classArt.rectTransform);
        classArt.raycastTarget = false;
        classArt.enabled = false;

        RectTransform levelBox = Box(emblem, "Level Box", Vector2.zero, new Vector2(42f, 34f));
        levelBox.anchorMin = levelBox.anchorMax = new Vector2(1f, 0f);
        levelBox.pivot = new Vector2(0.5f, 0.5f);
        // Half as far over the picture's border as the original (-6, 6) corner inset.
        levelBox.anchoredPosition = new Vector2(LevelBoxOffset.x, LevelBoxOffset.y);
        Panel(levelBox, PersonaMenuStyle.Ink, PersonaMenuStyle.Accent);
        levelBadge = Label(levelBox, "Level", title, "1", 22, PersonaMenuStyle.Ink, TextAnchor.MiddleCenter,
            Vector2.zero, Vector2.one);

        health = BuildMeter(parent, body, "HP", new Vector2(StatsLeft, -6f), 300f, PersonaMenuStyle.Accent);
        stamina = BuildMeter(parent, body, "STAMINA", new Vector2(StatsLeft, -48f), 300f, PersonaMenuStyle.Gold);

        RectTransform info = Box(parent, "Info", new Vector2(StatsLeft, -90f), new Vector2(420f, 26f));
        membersLabel = Label(info, "Members", body, string.Empty, 22, PersonaMenuStyle.Muted, TextAnchor.MiddleLeft,
            Vector2.zero, new Vector2(0.38f, 1f));
        heldLabel = Label(info, "Held", body, string.Empty, 22, PersonaMenuStyle.Gold, TextAnchor.MiddleLeft,
            new Vector2(0.38f, 0f), Vector2.one);
    }

    private Meter BuildMeter(Transform parent, Font font, string label, Vector2 position, float width, Color color)
    {
        const float labelWidth = 96f;
        var meter = new Meter { baseColor = color };
        RectTransform row = Box(parent, label + " Meter", position, new Vector2(labelWidth + width + 130f, 34f));
        Text caption = Label(row, "Label", font, label, 24, color, TextAnchor.MiddleLeft,
            Vector2.zero, new Vector2(0f, 1f));
        caption.rectTransform.pivot = new Vector2(0f, 0.5f);
        caption.rectTransform.sizeDelta = new Vector2(labelWidth, 0f);

        GameObject trackObject = new GameObject("Track", typeof(RectTransform));
        trackObject.transform.SetParent(row, false);
        RectTransform track = trackObject.GetComponent<RectTransform>();
        track.anchorMin = new Vector2(0f, 0.5f);
        track.anchorMax = new Vector2(0f, 0.5f);
        track.pivot = new Vector2(0f, 0.5f);
        track.anchoredPosition = new Vector2(labelWidth, 3f);
        track.sizeDelta = new Vector2(width, 16f);
        Panel(track, PersonaMenuStyle.Ink, PersonaMenuStyle.PlateBlack);
        PersonaShape trail = PersonaMenuStyle.CreateShape("Trail", track, new Color(1f, 1f, 1f, 0.75f));
        Fill(trail.rectTransform);
        PersonaShape fill = PersonaMenuStyle.CreateShape("Fill", track, color);
        Fill(fill.rectTransform);
        meter.trail = trail.rectTransform;
        meter.fill = fill.rectTransform;
        meter.fillShape = fill;

        meter.value = Label(row, "Value", font, string.Empty, 22, PersonaMenuStyle.Ink, TextAnchor.MiddleLeft,
            Vector2.zero, new Vector2(0f, 1f));
        meter.value.rectTransform.pivot = new Vector2(0f, 0.5f);
        meter.value.rectTransform.anchoredPosition = new Vector2(labelWidth + width + 14f, 3f);
        meter.value.rectTransform.sizeDelta = new Vector2(116f, 0f);
        // State line sits under the track, inside the row, so rows never collide.
        meter.state = Label(row, "State", font, string.Empty, 15, PersonaMenuStyle.Gold, TextAnchor.LowerLeft,
            Vector2.zero, new Vector2(0f, 0f));
        meter.state.rectTransform.pivot = new Vector2(0f, 0f);
        meter.state.rectTransform.anchoredPosition = new Vector2(labelWidth, -5f);
        meter.state.rectTransform.sizeDelta = new Vector2(width, 14f);
        return meter;
    }

    private void BuildProgress(Transform parent, Font body)
    {
        const float levelHeight = 112f;
        const float goalHeight = 110f;
        const float gap = 14f;
        RectTransform plate = Box(parent, "Level Plate", Vector2.zero, new Vector2(380f, levelHeight), true);
        Panel(plate, PersonaMenuStyle.Accent, new Color(0.035f, 0.05f, 0.1f, 1f));
        levelLabel = Label(plate, "Level", body, string.Empty, 26, PersonaMenuStyle.Gold, TextAnchor.MiddleLeft,
            new Vector2(0.05f, 0.68f), new Vector2(0.97f, 0.96f));
        experienceFill = Bar(plate, "Experience", new Vector2(0.05f, 0.56f), new Vector2(0.95f, 0.64f),
            PersonaMenuStyle.Gold, out _);
        statsLabel = Label(plate, "Stats", body, string.Empty, 22, PersonaMenuStyle.Ink, TextAnchor.MiddleLeft,
            new Vector2(0.05f, 0.27f), new Vector2(0.97f, 0.52f));
        reputationLabel = Label(plate, "Reputation", body, string.Empty, 22, PersonaMenuStyle.Muted,
            TextAnchor.MiddleLeft, new Vector2(0.05f, 0.04f), new Vector2(0.97f, 0.28f));

        // Stacked with a fixed gap: the two plates can never overlap.
        RectTransform goals = Box(parent, "Goal Plate", new Vector2(0f, -(levelHeight + gap)),
            new Vector2(380f, goalHeight), true);
        Panel(goals, PersonaMenuStyle.Ink, new Color(0.035f, 0.05f, 0.1f, 1f));
        dayLabel = Label(goals, "Day", body, string.Empty, 22, PersonaMenuStyle.Gold, TextAnchor.MiddleLeft,
            new Vector2(0.05f, 0.74f), new Vector2(0.97f, 0.98f));
        string[] shortLabels = { "REPS", "SOCIAL", "CHAOS" };
        for (int i = 0; i < 3; i++)
        {
            float top = 0.7f - i * 0.22f;
            Label(goals, "Goal " + i, body, shortLabels[i], 18, PersonaMenuStyle.Muted, TextAnchor.MiddleLeft,
                new Vector2(0.05f, top - 0.2f), new Vector2(0.24f, top));
            goalFills[i] = Bar(goals, "Goal Bar " + i, new Vector2(0.25f, top - 0.14f), new Vector2(0.76f, top - 0.06f),
                PersonaMenuStyle.Accent, out goalShapes[i]);
            goalCounts[i] = Label(goals, "Goal Count " + i, body, string.Empty, 18, PersonaMenuStyle.Ink,
                TextAnchor.MiddleRight, new Vector2(0.78f, top - 0.2f), new Vector2(0.95f, top));
        }
    }

    private void BuildNotices(Transform parent, Font body)
    {
        for (int i = 0; i < MaxVisibleNotices; i++)
        {
            RectTransform line = Box(parent, "Notice " + i, new Vector2(0f, -i * 52f), new Vector2(720f, 42f),
                false, true);
            noticeRims[i] = Panel(line, PersonaMenuStyle.Accent, new Color(0.035f, 0.05f, 0.1f, 1f));
            noticeLabels[i] = Label(line, "Text", body, string.Empty, 26, PersonaMenuStyle.Ink,
                TextAnchor.MiddleCenter, new Vector2(0.03f, 0f), new Vector2(0.97f, 1f));
            noticeLabels[i].horizontalOverflow = HorizontalWrapMode.Wrap;
            noticeLabels[i].resizeTextForBestFit = true;
            noticeLabels[i].resizeTextMinSize = 14;
            noticeLabels[i].resizeTextMaxSize = 26;
            noticeRoots[i] = line.gameObject;
            line.gameObject.SetActive(false);
        }
    }

    private void BuildSaveIndicator(Transform parent, Font body)
    {
        saveGroup = parent.gameObject.AddComponent<CanvasGroup>();
        RectTransform plate = Box(parent, "Plate", Vector2.zero, new Vector2(230f, 46f), true);
        plate.anchorMin = plate.anchorMax = plate.pivot = new Vector2(1f, 0f);
        plate.anchoredPosition = Vector2.zero;
        Panel(plate, PersonaMenuStyle.Accent, PersonaMenuStyle.PlateBlack);
        saveLabel = Label(plate, "Text", body, string.Empty, 26, PersonaMenuStyle.Ink, TextAnchor.MiddleCenter,
            Vector2.zero, Vector2.one);
    }

    private static RectTransform Box(Transform parent, string name, Vector2 topLeftOffset, Vector2 size,
        bool rightAligned = false, bool centered = false)
    {
        GameObject box = new GameObject(name, typeof(RectTransform));
        box.transform.SetParent(parent, false);
        RectTransform rect = box.GetComponent<RectTransform>();
        Vector2 anchor = centered ? new Vector2(0.5f, 1f) : rightAligned ? new Vector2(1f, 1f) : new Vector2(0f, 1f);
        rect.anchorMin = anchor;
        rect.anchorMax = anchor;
        rect.pivot = anchor;
        rect.anchoredPosition = topLeftOffset;
        rect.sizeDelta = size;
        return rect;
    }

    /// <summary>Upright plate: a border-coloured rectangle with the face inset inside it.</summary>
    private static PersonaShape Panel(Transform parent, Color border, Color face)
    {
        PersonaShape rim = PersonaMenuStyle.CreateShape("Rim", parent, border);
        RectTransform rimRect = rim.rectTransform;
        rimRect.anchorMin = Vector2.zero;
        rimRect.anchorMax = Vector2.one;
        rimRect.offsetMin = new Vector2(-Border, -Border);
        rimRect.offsetMax = new Vector2(Border, Border);
        PersonaShape plate = PersonaMenuStyle.CreateShape("Face", parent, face);
        Fill(plate.rectTransform);
        return rim;
    }

    private static RectTransform Bar(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax,
        Color color, out PersonaShape fillShape)
    {
        PersonaShape track = PersonaMenuStyle.CreateShape(name, parent, new Color(0f, 0f, 0f, 0.6f));
        RectTransform trackRect = track.rectTransform;
        trackRect.anchorMin = anchorMin;
        trackRect.anchorMax = anchorMax;
        trackRect.offsetMin = Vector2.zero;
        trackRect.offsetMax = Vector2.zero;
        fillShape = PersonaMenuStyle.CreateShape("Fill", track.transform, color);
        Fill(fillShape.rectTransform);
        fillShape.rectTransform.anchorMax = new Vector2(0f, 1f);
        return fillShape.rectTransform;
    }

    private static Text Label(Transform parent, string name, Font font, string content, int size, Color color,
        TextAnchor alignment, Vector2 anchorMin, Vector2 anchorMax)
    {
        GameObject textObject = new GameObject(name, typeof(RectTransform), typeof(Text));
        textObject.transform.SetParent(parent, false);
        Text text = textObject.GetComponent<Text>();
        RectTransform rect = text.rectTransform;
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        text.font = font;
        text.text = content;
        text.fontSize = size;
        text.color = color;
        text.alignment = alignment;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.raycastTarget = false;
        Shadow shadow = textObject.AddComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.85f);
        shadow.effectDistance = new Vector2(2f, -2f);
        return text;
    }

    private static void Fill(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

#if UNITY_EDITOR
    /// <summary>Verifier access: every HUD rectangle, to assert upright and non-overlapping layout.</summary>
    public RectTransform[] LayoutRectsForVerification =>
        GetComponentsInChildren<RectTransform>(true);
    public RawImage ClassPictureForVerification => classArt;
    public string LevelBadgeForVerification => levelBadge != null ? levelBadge.text : string.Empty;
#endif
}