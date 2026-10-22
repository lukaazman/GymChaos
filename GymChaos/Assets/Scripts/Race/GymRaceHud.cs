using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Race HUD in the game's Persona style: place, lap and timers top left,
/// standings under them, minimap top right, speed and boost meter bottom
/// right, countdown and banners in the centre, speed lines while boosting,
/// and the results board at the end.
/// </summary>
public sealed class GymRaceHud : MonoBehaviour
{
    public struct ResultRow
    {
        public int Place;
        public string Name;
        public Color Color;
        public bool IsPlayer;
        public float Time;
        public float BestLap;
        public bool Finished;
    }

    private const float StandingRowHeight = 46f;

    private Font body;
    private Font title;
    private CanvasGroup group;
    private Text placeText;
    private Text placeOfText;
    private Text lapText;
    private Text timeText;
    private Text lapTimesText;
    private Text speedText;
    private Text boostHint;
    private RectTransform boostFill;
    private Image boostFillImage;
    private RectTransform minimapRect;
    private GymRaceMinimapGraphic minimap;
    private readonly List<RectTransform> minimapDots = new List<RectTransform>();
    private readonly List<Image> minimapDotImages = new List<Image>();
    private readonly List<GymRaceRacer> shownOrder = new List<GymRaceRacer>();
    private float nextTextRefresh;
    private int shownLapCount = -1;
    private int shownSpeed = -1;
    private readonly List<Text> standingRows = new List<Text>();
    private readonly List<Text> standingPlaces = new List<Text>();
    private readonly List<RawImage> standingHeads = new List<RawImage>();
    private Text centerText;
    private Text bannerText;
    private Text hintText;
    private GymRaceSpeedLines speedLines;
    private RectTransform resultsRoot;
    private Text resultsTitle;
    private Text resultsBody;
    private Text resultsXp;
    private RectTransform choiceRoot;
    private readonly List<Button> choiceButtons = new List<Button>();
    private int choiceIndex;
    private float centerUntil;
    private float bannerUntil;
    private float placePop;
    private int lastPlace = -1;

    public static GymRaceHud Create()
    {
        GameObject go = new GameObject("Race HUD (Runtime)");
        GymRaceHud hud = go.AddComponent<GymRaceHud>();
        hud.Build();
        return hud;
    }

    private void Build()
    {
        Canvas canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        // Above the gameplay HUD (50), below the pause menu (200).
        canvas.sortingOrder = 60;
        CanvasScaler scaler = gameObject.AddComponent<CanvasScaler>();
        GymRuntimeSettings.ConfigureBalancedCanvasScaler(scaler);
        group = gameObject.AddComponent<CanvasGroup>();
        group.interactable = false;
        group.blocksRaycasts = false;
        gameObject.AddComponent<GraphicRaycaster>();

        Font fallback = PersonaMenuStyle.BuiltinFont;
        body = PersonaMenuStyle.LoadButtonFont(fallback);
        title = PersonaMenuStyle.LoadTitleFont(fallback);

        speedLines = Stretch(new GameObject("Speed Lines", typeof(RectTransform), typeof(CanvasRenderer)), transform).gameObject.AddComponent<GymRaceSpeedLines>();
        speedLines.raycastTarget = false;

        RectTransform topLeft = Corner("Place", new Vector2(0f, 1f), new Vector2(40f, -26f), new Vector2(520f, 420f));
        placeText = Label(topLeft, "Place", title, "6TH", new Vector2(0f, 0f), new Vector2(260f, 130f), 116, PersonaMenuStyle.Ink, TextAnchor.UpperLeft);
        placeOfText = Label(topLeft, "Of", body, "/ 6", new Vector2(205f, -62f), new Vector2(120f, 60f), 44, PersonaMenuStyle.Muted, TextAnchor.UpperLeft);
        lapText = Label(topLeft, "Lap", title, "LAP 1/3", new Vector2(0f, -128f), new Vector2(360f, 60f), 46, PersonaMenuStyle.Gold, TextAnchor.UpperLeft);
        timeText = Label(topLeft, "Time", body, "0:00.00", new Vector2(0f, -180f), new Vector2(360f, 44f), 34, PersonaMenuStyle.Ink, TextAnchor.UpperLeft);
        lapTimesText = Label(topLeft, "Laps", body, string.Empty, new Vector2(0f, -220f), new Vector2(360f, 120f), 24, PersonaMenuStyle.Muted, TextAnchor.UpperLeft);

        // Standings: one black bar per racer with the place, a head portrait
        // (transparent, no backdrop) and the name to its right.
        RectTransform standings = Corner("Standings", new Vector2(0f, 0.5f), new Vector2(40f, -40f), new Vector2(330f, 300f));
        for (int i = 0; i < GymRaceTrackLayout.RacerCount; i++)
        {
            Image bar = new GameObject("Bar " + i, typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            RectTransform barRect = bar.rectTransform;
            barRect.SetParent(standings, false);
            barRect.anchorMin = barRect.anchorMax = new Vector2(0f, 1f);
            barRect.pivot = new Vector2(0f, 1f);
            barRect.anchoredPosition = new Vector2(0f, -i * StandingRowHeight);
            barRect.sizeDelta = new Vector2(310f, StandingRowHeight - 4f);
            bar.color = new Color(0f, 0f, 0f, 0.78f);
            bar.raycastTarget = false;

            standingPlaces.Add(Label(barRect, "Place", title, string.Empty, new Vector2(8f, -2f), new Vector2(30f, 40f), 30, PersonaMenuStyle.Muted, TextAnchor.UpperLeft));
            RawImage head = new GameObject("Head", typeof(RectTransform), typeof(RawImage)).GetComponent<RawImage>();
            RectTransform headRect = head.rectTransform;
            headRect.SetParent(barRect, false);
            headRect.anchorMin = headRect.anchorMax = new Vector2(0f, 0.5f);
            headRect.pivot = new Vector2(0f, 0.5f);
            headRect.anchoredPosition = new Vector2(36f, 0f);
            headRect.sizeDelta = new Vector2(StandingRowHeight + 2f, StandingRowHeight + 2f);
            head.raycastTarget = false;
            standingHeads.Add(head);
            standingRows.Add(Label(barRect, "Name", body, string.Empty, new Vector2(36f + StandingRowHeight + 10f, -4f), new Vector2(220f, 40f), 30, PersonaMenuStyle.Ink, TextAnchor.UpperLeft));
        }

        minimapRect = Corner("Minimap", new Vector2(1f, 1f), new Vector2(-34f, -26f), new Vector2(300f, 300f));
        Image mapBack = minimapRect.gameObject.AddComponent<Image>();
        mapBack.color = new Color(0.01f, 0.03f, 0.08f, 0.55f);
        mapBack.raycastTarget = false;
        minimap = Stretch(new GameObject("Track", typeof(RectTransform), typeof(CanvasRenderer)), minimapRect).gameObject.AddComponent<GymRaceMinimapGraphic>();
        minimap.raycastTarget = false;

        // Number, then its unit on its own line below, then the boost bar:
        // the title font's tall digits used to run into the KM/H label.
        RectTransform bottomRight = Corner("Speed", new Vector2(1f, 0f), new Vector2(-40f, 36f), new Vector2(420f, 200f));
        speedText = Label(bottomRight, "Speed", title, "0", new Vector2(0f, 0f), new Vector2(420f, 100f), 84, PersonaMenuStyle.Ink, TextAnchor.UpperRight);
        Label(bottomRight, "Unit", body, "KM/H", new Vector2(0f, -112f), new Vector2(420f, 30f), 24, PersonaMenuStyle.Muted, TextAnchor.UpperRight);
        RectTransform boostBack = new GameObject("Boost Back", typeof(RectTransform), typeof(Image)).GetComponent<RectTransform>();
        boostBack.SetParent(bottomRight, false);
        boostBack.anchorMin = boostBack.anchorMax = new Vector2(1f, 1f);
        boostBack.pivot = new Vector2(1f, 1f);
        boostBack.anchoredPosition = new Vector2(0f, -150f);
        boostBack.sizeDelta = new Vector2(320f, 22f);
        boostBack.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.6f);
        boostBack.GetComponent<Image>().raycastTarget = false;
        boostFill = new GameObject("Boost Fill", typeof(RectTransform), typeof(Image)).GetComponent<RectTransform>();
        boostFill.SetParent(boostBack, false);
        boostFill.anchorMin = new Vector2(0f, 0f);
        boostFill.anchorMax = new Vector2(0f, 1f);
        boostFill.pivot = new Vector2(0f, 0.5f);
        boostFill.anchoredPosition = Vector2.zero;
        boostFillImage = boostFill.GetComponent<Image>();
        boostFillImage.raycastTarget = false;
        boostHint = Label(bottomRight, "Boost Hint", body, "BOOST", new Vector2(-330f, -142f), new Vector2(240f, 34f), 26, PersonaMenuStyle.Muted, TextAnchor.UpperRight);

        RectTransform center = Corner("Center", new Vector2(0.5f, 0.5f), new Vector2(0f, 120f), new Vector2(900f, 300f));
        centerText = Label(center, "Countdown", title, string.Empty, new Vector2(-450f, 0f), new Vector2(900f, 240f), 210, PersonaMenuStyle.Ink, TextAnchor.MiddleCenter);
        RectTransform banner = Corner("Banner", new Vector2(0.5f, 0.5f), new Vector2(0f, -40f), new Vector2(900f, 120f));
        bannerText = Label(banner, "Banner", title, string.Empty, new Vector2(-450f, 0f), new Vector2(900f, 110f), 78, PersonaMenuStyle.Gold, TextAnchor.MiddleCenter);
        RectTransform hint = Corner("Hint", new Vector2(0.5f, 0f), new Vector2(0f, 40f), new Vector2(1400f, 50f));
        hintText = Label(hint, "Controls", body, string.Empty, new Vector2(-700f, 0f), new Vector2(1400f, 44f), 28, PersonaMenuStyle.Muted, TextAnchor.MiddleCenter);

        resultsRoot = PersonaMenuStyle.CreatePanel(transform, "Results", 960f, 520f, 1020f, 640f, -1.5f, new Color(0.02f, 0.05f, 0.12f, 0.92f), PersonaMenuStyle.Accent);
        resultsTitle = PersonaMenuStyle.CreateLabel(resultsRoot, "Results Title", title, "RESULTS", 510f, 70f, 900f, 90f, 72, PersonaMenuStyle.Ink, TextAnchor.MiddleLeft);
        resultsBody = PersonaMenuStyle.CreateLabel(resultsRoot, "Results Body", body, string.Empty, 470f, 300f, 820f, 340f, 34, PersonaMenuStyle.Ink, TextAnchor.UpperLeft);
        resultsXp = PersonaMenuStyle.CreateLabel(resultsRoot, "Results XP", body, string.Empty, 510f, 560f, 900f, 90f, 34, PersonaMenuStyle.Gold, TextAnchor.UpperLeft);
        // Art-space placement is relative to the screen, not the panel: pin
        // the three labels inside the plate instead.
        FitInside(resultsTitle.rectTransform, 0.06f, 0.80f, 0.94f, 0.95f);
        FitInside(resultsBody.rectTransform, 0.06f, 0.20f, 0.94f, 0.78f);
        FitInside(resultsXp.rectTransform, 0.06f, 0.05f, 0.94f, 0.18f);
        resultsRoot.gameObject.SetActive(false);

        // End-of-race choice under the results board.
        choiceRoot = Stretch(new GameObject("Race Choice", typeof(RectTransform)), transform);
        choiceButtons.Add(PersonaMenuStyle.CreateButton(choiceRoot, "Back To Gym", body, "BACK TO GYM", 735f, 884f, 360f, 70f, 40, -1f, () => ChoiceMade = Choice.BackToGym));
        choiceButtons.Add(PersonaMenuStyle.CreateButton(choiceRoot, "Race Again", body, "RACE AGAIN", 1125f, 884f, 360f, 70f, 40, -1f, () => ChoiceMade = Choice.RaceAgain));
        for (int i = 0; i < choiceButtons.Count; i++)
        {
            int index = i;
            // The director reads the keys; uGUI's own arrow/WASD navigation
            // would move the focus a second time.
            choiceButtons[i].navigation = new Navigation { mode = Navigation.Mode.None };
            PersonaMenuButton persona = choiceButtons[i].GetComponent<PersonaMenuButton>();
            if (persona != null)
            {
                // Hover and keys share one focus.
                persona.Focused += () =>
                {
                    if (choiceIndex != index) FocusChoice(index);
                };
            }
        }
        choiceRoot.gameObject.SetActive(false);
    }

    public enum Choice
    {
        None,
        BackToGym,
        RaceAgain
    }

    /// <summary>Set by a click or <see cref="ConfirmChoice"/> while the choice is shown.</summary>
    public Choice ChoiceMade { get; private set; }
    public bool ChoiceVisible => choiceRoot != null && choiceRoot.gameObject.activeSelf;
    public int FocusedChoice => choiceIndex;
    /// <summary>Verification: 0 = BACK TO GYM, 1 = RACE AGAIN.</summary>
    public Button ChoiceButtonForVerification(int index) => choiceButtons[index];

    public void ShowChoice()
    {
        GymRuntimeSettings.EnsureEventSystem();
        ChoiceMade = Choice.None;
        choiceRoot.gameObject.SetActive(true);
        group.interactable = true;
        group.blocksRaycasts = true;
        FocusChoice(0);
    }

    public void HideChoice()
    {
        choiceRoot.gameObject.SetActive(false);
        group.interactable = false;
        group.blocksRaycasts = false;
        if (EventSystem.current != null)
        {
            EventSystem.current.SetSelectedGameObject(null);
        }
    }

    public void MoveChoice(int direction)
    {
        FocusChoice((choiceIndex + direction + choiceButtons.Count) % choiceButtons.Count);
    }

    public void ConfirmChoice()
    {
        ChoiceMade = choiceIndex == 0 ? Choice.BackToGym : Choice.RaceAgain;
    }

    private void FocusChoice(int index)
    {
        choiceIndex = index;
        if (EventSystem.current != null)
        {
            EventSystem.current.SetSelectedGameObject(choiceButtons[index].gameObject);
        }
    }

    private RectTransform Corner(string name, Vector2 anchor, Vector2 position, Vector2 size)
    {
        RectTransform rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(transform, false);
        rect.anchorMin = rect.anchorMax = anchor;
        rect.pivot = anchor;
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        return rect;
    }

    private static void FitInside(RectTransform rect, float minX, float minY, float maxX, float maxY)
    {
        rect.anchorMin = new Vector2(minX, minY);
        rect.anchorMax = new Vector2(maxX, maxY);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        rect.localRotation = Quaternion.identity;
    }

    private static RectTransform Stretch(GameObject go, Transform parent)
    {
        RectTransform rect = go.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        return rect;
    }

    private Text Label(RectTransform parent, string name, Font font, string content, Vector2 position, Vector2 size, int fontSize, Color color, TextAnchor alignment)
    {
        Text text = new GameObject(name, typeof(RectTransform), typeof(Text)).GetComponent<Text>();
        RectTransform rect = text.rectTransform;
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
        if (alignment == TextAnchor.UpperRight)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 1f);
        }
        else
        {
            rect.pivot = new Vector2(0f, 1f);
        }
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        text.font = font;
        text.text = content;
        text.fontSize = fontSize;
        text.color = color;
        text.alignment = alignment;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.raycastTarget = false;
        Shadow shadow = text.gameObject.AddComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.8f);
        shadow.effectDistance = new Vector2(3f, -3f);
        return text;
    }

    public void SetVisible(bool visible)
    {
        group.alpha = visible ? 1f : 0f;
    }

    public void SetRaceWidgetsVisible(bool visible)
    {
        placeText.transform.parent.gameObject.SetActive(visible);
        standingRows[0].transform.parent.parent.gameObject.SetActive(visible);
        minimapRect.gameObject.SetActive(visible);
        speedText.transform.parent.gameObject.SetActive(visible);
    }

    public void ShowCenter(string text, Color color, float seconds)
    {
        centerText.text = text;
        centerText.color = color;
        centerText.transform.localScale = Vector3.one * 1.35f;
        centerUntil = Time.time + seconds;
    }

    public void ShowBanner(string text, Color color, float seconds)
    {
        bannerText.text = text;
        bannerText.color = color;
        bannerText.transform.localScale = Vector3.one * 1.2f;
        bannerUntil = Time.time + seconds;
    }

    public void SetHint(string text)
    {
        hintText.text = text;
    }

    public void UpdatePlayer(GymRaceRacer racer, float raceTime, bool showWrongWay)
    {
        if (racer == null)
        {
            return;
        }
        int place = Mathf.Max(1, racer.Finished ? racer.FinishPlace : racer.Place);
        if (place != lastPlace)
        {
            if (lastPlace > 0)
            {
                placePop = 1f;
            }
            lastPlace = place;
        }
        placeText.text = Ordinal(place);
        placeText.color = place == 1 ? PersonaMenuStyle.Gold : place <= 3 ? PersonaMenuStyle.Ink : PersonaMenuStyle.Accent;
        placeOfText.text = "/ " + GymRaceTrackLayout.RacerCount;
        placeText.transform.localScale = Vector3.one * (1f + 0.3f * placePop);
        placePop = Mathf.MoveTowards(placePop, 0f, Time.deltaTime * 3f);

        GymRaceKart kart = racer.Kart;
        // Text changes at 10 Hz; rebuilding strings every frame only made garbage.
        if (Time.unscaledTime >= nextTextRefresh)
        {
            nextTextRefresh = Time.unscaledTime + 0.1f;
            lapText.text = racer.Finished ? "FINISHED" : $"LAP {racer.CurrentLap}/{GymRaceTrackLayout.Laps}";
            timeText.text = FormatTime(racer.Finished ? racer.FinishTime : raceTime);
            int speed = Mathf.RoundToInt(Mathf.Abs(kart.ForwardSpeed) * 3.6f);
            if (speed != shownSpeed)
            {
                shownSpeed = speed;
                speedText.text = speed.ToString();
            }
        }
        if (racer.LapTimes.Count != shownLapCount)
        {
            shownLapCount = racer.LapTimes.Count;
            System.Text.StringBuilder laps = new System.Text.StringBuilder();
            for (int i = 0; i < racer.LapTimes.Count; i++)
            {
                laps.Append("LAP ").Append(i + 1).Append("  ").Append(FormatTime(racer.LapTimes[i])).Append('\n');
            }
            lapTimesText.text = laps.ToString();
        }
        float meter = kart.IsBoosting ? kart.BoostTime / GymRaceKart.BoostDuration : kart.BoostMeter;
        boostFill.sizeDelta = new Vector2(320f * Mathf.Clamp01(meter), 0f);
        bool full = kart.BoostMeter >= 1f && !kart.IsBoosting;
        float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * 10f);
        boostFillImage.color = kart.IsBoosting
            ? new Color(0.45f, 0.85f, 1f)
            : full ? Color.Lerp(PersonaMenuStyle.Gold, Color.white, pulse) : PersonaMenuStyle.Accent;
        boostHint.text = kart.IsBoosting ? "BOOST!" : full ? "BOOST READY  [SHIFT / LMB]" : "BOOST";
        boostHint.color = full ? PersonaMenuStyle.Gold : PersonaMenuStyle.Muted;
        speedLines.Intensity = kart.IsBoosting ? 1f : kart.MiniTurboTime > 0f ? 0.5f : Mathf.Clamp01((Mathf.Abs(kart.ForwardSpeed) - kart.KartStats.TopSpeed * 0.85f) / 6f) * 0.25f;

        if (showWrongWay && Time.time > bannerUntil)
        {
            bannerText.text = "WRONG WAY";
            bannerText.color = PersonaMenuStyle.Accent;
            bannerText.transform.localScale = Vector3.one;
            bannerUntil = Time.time + 0.2f;
        }
    }

    public void UpdateStandings(IList<GymRaceRacer> ordered)
    {
        bool same = shownOrder.Count == ordered.Count;
        for (int i = 0; same && i < ordered.Count; i++)
        {
            same = shownOrder[i] == ordered[i];
        }
        if (same)
        {
            return;
        }
        shownOrder.Clear();
        shownOrder.AddRange(ordered);
        for (int i = 0; i < standingRows.Count; i++)
        {
            bool has = i < ordered.Count;
            standingRows[i].transform.parent.gameObject.SetActive(has);
            if (!has)
            {
                continue;
            }
            GymRaceRacer racer = ordered[i];
            standingPlaces[i].text = (i + 1).ToString();
            standingRows[i].text = racer.DisplayName.ToUpperInvariant();
            standingRows[i].color = racer.IsPlayer ? PersonaMenuStyle.Gold : PersonaMenuStyle.Ink;
            standingHeads[i].texture = racer.Headshot;
            standingHeads[i].enabled = racer.Headshot != null;
        }
    }

    public void UpdateMinimap(IList<GymRaceRacer> racers)
    {
        while (minimapDots.Count < racers.Count)
        {
            RectTransform dot = new GameObject("Dot", typeof(RectTransform), typeof(Image)).GetComponent<RectTransform>();
            dot.SetParent(minimapRect, false);
            dot.anchorMin = dot.anchorMax = Vector2.zero;
            dot.localRotation = Quaternion.Euler(0f, 0f, 45f);
            Image image = dot.GetComponent<Image>();
            image.raycastTarget = false;
            minimapDots.Add(dot);
            minimapDotImages.Add(image);
        }
        Vector2 size = minimapRect.rect.size;
        for (int i = 0; i < minimapDots.Count; i++)
        {
            bool has = i < racers.Count && racers[i] != null;
            minimapDots[i].gameObject.SetActive(has);
            if (!has)
            {
                continue;
            }
            GymRaceRacer racer = racers[i];
            Vector2 normalized = minimap.Project(racer.transform.position);
            minimapDots[i].anchoredPosition = new Vector2(normalized.x * size.x, normalized.y * size.y);
            float dotSize = racer.IsPlayer ? 18f : 12f;
            minimapDots[i].sizeDelta = new Vector2(dotSize, dotSize);
            minimapDotImages[i].color = racer.IsPlayer ? Color.white : racer.Color;
            if (racer.IsPlayer)
            {
                minimapDots[i].SetAsLastSibling();
            }
        }
    }

    public void ShowResults(IList<ResultRow> rows, string xpLine)
    {
        resultsRoot.gameObject.SetActive(true);
        System.Text.StringBuilder text = new System.Text.StringBuilder();
        for (int i = 0; i < rows.Count; i++)
        {
            ResultRow row = rows[i];
            string colour = ColorUtility.ToHtmlStringRGB(row.IsPlayer ? PersonaMenuStyle.Gold : PersonaMenuStyle.Ink);
            string time = row.Finished ? FormatTime(row.Time) : "--:--.--";
            string best = row.BestLap > 0f ? FormatTime(row.BestLap) : "-";
            text.Append($"<color=#{colour}>{Ordinal(row.Place),-5} {row.Name.ToUpperInvariant(),-14} {time}   BEST {best}</color>\n");
        }
        resultsBody.supportRichText = true;
        resultsBody.text = text.ToString();
        resultsXp.text = xpLine;
        resultsTitle.text = rows.Count > 0 && rows[0].IsPlayer ? "YOU WIN!" : "RESULTS";
        SetRaceWidgetsVisible(false);
    }

    public void HideResults()
    {
        resultsRoot.gameObject.SetActive(false);
    }

    public bool ResultsVisible => resultsRoot.gameObject.activeSelf;

    /// <summary>Verification: head portraits currently shown on the standings.</summary>
    public int StandingHeadsShown
    {
        get
        {
            int count = 0;
            for (int i = 0; i < standingHeads.Count; i++)
            {
                if (standingHeads[i].enabled && standingHeads[i].texture != null &&
                    standingHeads[i].transform.parent.gameObject.activeSelf)
                {
                    count++;
                }
            }
            return count;
        }
    }

    /// <summary>Verification: true when no standings row carries a racer colour.</summary>
    public bool StandingsUncoloured
    {
        get
        {
            for (int i = 0; i < standingRows.Count; i++)
            {
                Color c = standingRows[i].color;
                if (c != PersonaMenuStyle.Gold && c != PersonaMenuStyle.Ink)
                {
                    return false;
                }
            }
            return true;
        }
    }

    private void Update()
    {
        if (centerText.text.Length > 0)
        {
            centerText.transform.localScale = Vector3.Lerp(centerText.transform.localScale, Vector3.one, Time.deltaTime * 8f);
            if (Time.time > centerUntil)
            {
                centerText.text = string.Empty;
            }
        }
        if (bannerText.text.Length > 0)
        {
            bannerText.transform.localScale = Vector3.Lerp(bannerText.transform.localScale, Vector3.one, Time.deltaTime * 6f);
            if (Time.time > bannerUntil)
            {
                bannerText.text = string.Empty;
            }
        }
    }

    public static string Ordinal(int place)
    {
        switch (place)
        {
            case 1: return "1ST";
            case 2: return "2ND";
            case 3: return "3RD";
            default: return place + "TH";
        }
    }

    public static string FormatTime(float seconds)
    {
        seconds = Mathf.Max(0f, seconds);
        int minutes = Mathf.FloorToInt(seconds / 60f);
        float rest = seconds - minutes * 60f;
        return $"{minutes}:{rest:00.00}";
    }
}

/// <summary>Track outline for the minimap (both branches) drawn as thick lines.</summary>
public sealed class GymRaceMinimapGraphic : MaskableGraphic
{
    private const float Padding = 0.07f;
    private Vector2 min;
    private Vector2 max;
    private bool measured;

    private void Measure()
    {
        if (measured)
        {
            return;
        }
        min = new Vector2(float.MaxValue, float.MaxValue);
        max = new Vector2(float.MinValue, float.MinValue);
        foreach (GymRaceTrackPath path in new[] { GymRaceTrackLayout.Main, GymRaceTrackLayout.Shortcut })
        {
            for (int i = 0; i < path.Count; i++)
            {
                Vector3 p = path.Points[i];
                min = Vector2.Min(min, new Vector2(p.x, p.z));
                max = Vector2.Max(max, new Vector2(p.x, p.z));
            }
        }
        // Square map: keep the aspect of the world.
        float span = Mathf.Max(max.x - min.x, max.y - min.y);
        Vector2 center = (min + max) * 0.5f;
        min = center - Vector2.one * span * 0.5f;
        max = center + Vector2.one * span * 0.5f;
        measured = true;
    }

    /// <summary>World position to 0..1 map coordinates (north up).</summary>
    public Vector2 Project(Vector3 world)
    {
        Measure();
        Vector2 t = new Vector2(
            Mathf.InverseLerp(min.x, max.x, world.x),
            Mathf.InverseLerp(min.y, max.y, world.z));
        return Vector2.one * Padding + t * (1f - Padding * 2f);
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        Measure();
        Rect rect = rectTransform.rect;
        AddPath(vh, GymRaceTrackLayout.Main, rect, 7f, new Color(0.85f, 0.88f, 0.95f, 0.9f));
        AddPath(vh, GymRaceTrackLayout.Shortcut, rect, 5f, new Color(0.55f, 0.8f, 1f, 0.85f));
        // Start line tick.
        GymRaceTrackLayout.Main.Sample(GymRaceTrackLayout.StartLineDistance, out Vector3 start, out _, out Vector3 right, out _);
        AddLine(vh, ToRect(start - right * 9f, rect), ToRect(start + right * 9f, rect), 4f, PersonaMenuStyle.Accent);
    }

    private Vector2 ToRect(Vector3 world, Rect rect)
    {
        Vector2 n = Project(world);
        return new Vector2(rect.xMin + n.x * rect.width, rect.yMin + n.y * rect.height);
    }

    private void AddPath(VertexHelper vh, GymRaceTrackPath path, Rect rect, float width, Color color)
    {
        int step = 3;
        int count = path.Closed ? path.Count : path.Count - 1;
        for (int i = 0; i < count; i += step)
        {
            int j = path.Closed ? path.Wrap(i + step) : Mathf.Min(i + step, path.Count - 1);
            AddLine(vh, ToRect(path.Points[i], rect), ToRect(path.Points[j], rect), width, color);
        }
    }

    private static void AddLine(VertexHelper vh, Vector2 a, Vector2 b, float width, Color color)
    {
        Vector2 dir = b - a;
        if (dir.sqrMagnitude < 1e-4f)
        {
            return;
        }
        Vector2 n = new Vector2(-dir.y, dir.x).normalized * (width * 0.5f);
        Vector2 extend = dir.normalized * (width * 0.5f);
        int index = vh.currentVertCount;
        UIVertex v = UIVertex.simpleVert;
        v.color = color;
        v.position = a - n - extend; vh.AddVert(v);
        v.position = a + n - extend; vh.AddVert(v);
        v.position = b + n + extend; vh.AddVert(v);
        v.position = b - n + extend; vh.AddVert(v);
        vh.AddTriangle(index, index + 1, index + 2);
        vh.AddTriangle(index, index + 2, index + 3);
    }
}

/// <summary>Anime-style streaks from the screen edges while the kart is boosting.</summary>
public sealed class GymRaceSpeedLines : MaskableGraphic
{
    private const int LineCount = 46;
    public float Intensity;
    private float shown;
    private float nextRebuild;

    private void Update()
    {
        float previous = shown;
        shown = Mathf.MoveTowards(shown, Intensity, Time.deltaTime * 4f);
        if (shown < 0.02f && previous < 0.02f)
        {
            return;
        }
        if (Time.time >= nextRebuild || shown < 0.02f)
        {
            nextRebuild = Time.time + 0.045f;
            SetVerticesDirty();
        }
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        if (shown < 0.02f)
        {
            return;
        }
        Rect rect = rectTransform.rect;
        Vector2 center = rect.center;
        float radius = Mathf.Max(rect.width, rect.height) * 0.75f;
        for (int i = 0; i < LineCount; i++)
        {
            float angle = Random.value * Mathf.PI * 2f;
            Vector2 dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            float inner = radius * Mathf.Lerp(0.42f, 0.62f, Random.value);
            Vector2 a = center + dir * inner;
            Vector2 b = center + dir * radius;
            float width = Mathf.Lerp(2f, 7f, Random.value);
            Vector2 n = new Vector2(-dir.y, dir.x) * width;
            Color color = new Color(1f, 1f, 1f, 0.32f * shown);
            int index = vh.currentVertCount;
            UIVertex v = UIVertex.simpleVert;
            v.color = new Color(1f, 1f, 1f, 0f);
            v.position = a; vh.AddVert(v);
            v.color = color;
            v.position = b + n; vh.AddVert(v);
            v.position = b - n; vh.AddVert(v);
            vh.AddTriangle(index, index + 1, index + 2);
        }
    }
}
