using UnityEngine;

/// <summary>
/// The progression journal: level and experience, the four stat trees, today's
/// three goals with their bars, the gym-day streak, loyalty stamps, the race
/// ladder and what the tutorial has next. TAB opens it during normal play, and
/// while the tutorial is running it also carries the SKIP button and the
/// completion card.
/// </summary>
public sealed class GymProgressionJournal : MonoBehaviour
{
    private GymExperienceService service;
    private PlayerMovement player;
    private GUIStyle headerStyle;
    private GUIStyle rowStyle;
    private GUIStyle smallStyle;
    private GUIStyle buttonStyle;
    private Texture2D panelTexture;
    private Texture2D barBackTexture;
    private Texture2D barFillTexture;
    private float shownXp;
    private float fillBlend;

    public static GymProgressionJournal Instance { get; private set; }
    public static bool IsOpen { get; private set; }
    public static bool WasSeen { get; private set; }
    public static bool CompletionPending { get; set; }
    public static string TeaserPending { get; set; } = string.Empty;

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

        if (GymRaceDirector.IsRaceActive)
        {
            Close();
            return;
        }

        if (Input.GetKeyDown(KeyCode.Tab) && !GymPauseMenu.IsVisible && !GymStartScreen.IsMenuVisible)
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
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
            IsOpen = false;
            CompletionPending = false;
        }
    }

    private void Open()
    {
        IsOpen = true;
        WasSeen = true;
        player.SetCursorCaptured(false);
        Debug.Log("GYMCHAOS_JOURNAL_OPEN", this);
    }

    private void Close()
    {
        if (!IsOpen)
        {
            return;
        }
        IsOpen = false;
        if (player != null)
        {
            player.SetCursorCaptured(true);
        }
    }

    private void OnGUI()
    {
        if (service == null || State == null)
        {
            return;
        }
        if (CompletionPending)
        {
            DrawCompletion();
            return;
        }
        if (!IsOpen)
        {
            return;
        }
        EnsureStyles();

        float width = Mathf.Min(1000f, Screen.width - 60f);
        float height = Mathf.Min(620f, Screen.height - 60f);
        float x = (Screen.width - width) * 0.5f;
        float y = (Screen.height - height) * 0.5f;
        GUI.DrawTexture(new Rect(x, y, width, height), panelTexture);

        GUI.Label(new Rect(x + 34f, y + 18f, width - 68f, 44f), "PROGRESSION", headerStyle);
        GUI.Label(new Rect(x + 34f, y + 58f, width - 68f, 30f),
            "DAY " + State.gymDay + "   ·   LEVEL " + State.level +
            "   ·   REPUTATION " + State.reputation +
            "   ·   STAMPS " + State.loyaltyStamps, rowStyle);

        float next = Mathf.Max(1f, service.ExperienceToNextLevel);
        shownXp = Mathf.Lerp(shownXp, State.experience, 0.18f);
        fillBlend = Mathf.Lerp(fillBlend, Mathf.Clamp01(shownXp / next), 0.25f);
        DrawBar(new Rect(x + 34f, y + 96f, width - 68f, 18f), fillBlend);
        GUI.Label(new Rect(x + 34f, y + 116f, width - 68f, 26f),
            Mathf.RoundToInt(State.experience) + " / " + Mathf.RoundToInt(next) +
            " XP   —   " + Mathf.Max(0f, next - State.experience).ToString("0") + " to level " + (State.level + 1),
            smallStyle);

        float listY = y + 156f;
        GUI.Label(new Rect(x + 34f, listY, width * 0.46f, 28f), "STAT TREES", headerStyle);
        string[] statNames = { "STRENGTH", "ENDURANCE", "TECHNIQUE", "REPUTATION" };
        GymStat[] stats = { GymStat.Strength, GymStat.Endurance, GymStat.Technique, GymStat.Reputation };
        for (int i = 0; i < stats.Length; i++)
        {
            float rowY = listY + 34f + i * 34f;
            int rank = service.GetStatRank(stats[i]);
            GUI.Label(new Rect(x + 34f, rowY, 200f, 26f), statNames[i], smallStyle);
            DrawBar(new Rect(x + 220f, rowY + 6f, width * 0.27f, 12f),
                Mathf.Clamp01(rank / (float)GymExperienceService.MaxStatRank));
            GUI.Label(new Rect(x + 220f + width * 0.27f + 10f, rowY, 90f, 26f),
                rank + " / " + GymExperienceService.MaxStatRank, smallStyle);
        }

        float goalY = listY;
        GUI.Label(new Rect(x + width * 0.55f, goalY, width * 0.4f, 28f), "TODAY'S GOALS", headerStyle);
        for (int i = 0; i < 3; i++)
        {
            float rowY = goalY + 34f + i * 40f;
            int target = Mathf.Max(1, service.DailyGoalTarget(i));
            int progress = State.dailyProgress != null && i < State.dailyProgress.Length
                ? State.dailyProgress[i] : 0;
            bool done = State.dailyCompleted != null && i < State.dailyCompleted.Length &&
                State.dailyCompleted[i];
            GUI.Label(new Rect(x + width * 0.55f, rowY, width * 0.36f, 24f),
                (done ? "DONE  " : "") + service.DailyGoalLabel(i), smallStyle);
            DrawBar(new Rect(x + width * 0.55f, rowY + 22f, width * 0.36f, 10f),
                Mathf.Clamp01(progress / (float)target));
            GUI.Label(new Rect(x + width * 0.55f + width * 0.36f + 8f, rowY, 90f, 24f),
                Mathf.Min(progress, target) + " / " + target, smallStyle);
        }

        float raceY = goalY + 34f + 3 * 40f + 12f;
        GUI.Label(new Rect(x + width * 0.55f, raceY, width * 0.4f, 26f),
            "RACE LADDER   " + GymRaceDirector.DifficultyNames[
                Mathf.Clamp(State.raceDifficultyUnlocked, 0, 2)].ToUpperInvariant(),
            headerStyle);
        string bestLap = State.raceBestLapMs > 0
            ? (State.raceBestLapMs / 1000f).ToString("0.00") + " s" : "—";
        GUI.Label(new Rect(x + width * 0.55f, raceY + 28f, width * 0.4f, 26f),
            "STARTS " + State.raceStarts + "   WINS " + State.raceWins + "   BEST LAP " + bestLap,
            smallStyle);

        string teaser = !string.IsNullOrEmpty(TeaserPending)
            ? TeaserPending
            : GymTutorialDirector.NextTeaser(State);
        if (!string.IsNullOrEmpty(teaser))
        {
            GUI.Label(new Rect(x + 34f, y + height - 96f, width - 300f, 30f),
                "NEXT LESSON:  " + teaser, rowStyle);
            if (GUI.Button(new Rect(x + width - 250f, y + height - 100f, 210f, 40f),
                    "SKIP TUTORIAL", buttonStyle))
            {
                GymTutorialDirector.SkipAll(State);
                GymHud.Active?.PushNotice("Tutorial skipped.", 2.5f, 0);
                Close();
            }
        }
        else
        {
            GUI.Label(new Rect(x + 34f, y + height - 92f, width - 68f, 26f),
                "NEXT:  level " + (State.level + 1) + " opens new gear and a harder race.", rowStyle);
        }
        GUI.Label(new Rect(x + 34f, y + height - 44f, width - 68f, 24f), "TAB TO CLOSE", smallStyle);
    }

    private void DrawCompletion()
    {
        EnsureStyles();
        float width = Mathf.Min(760f, Screen.width - 60f);
        float height = 330f;
        float x = (Screen.width - width) * 0.5f;
        float y = (Screen.height - height) * 0.5f;
        GUI.DrawTexture(new Rect(x, y, width, height), panelTexture);
        GUI.Label(new Rect(x + 34f, y + 20f, width - 68f, 46f), "TUTORIAL COMPLETE", headerStyle);
        GUI.Label(new Rect(x + 34f, y + 74f, width - 68f, 120f),
            "You have seen the floor, the locker, a set, the counter, the journal and a race.\n\n" +
            "TODAY'S GOALS are in the journal: three of them, every gym day.\n" +
            "NEXT UNLOCK:  level " + (State.level + 1) + " gear in the locker and a harder race.\n" +
            "The race sign now offers every difficulty you have opened.",
            rowStyle);
        if (GUI.Button(new Rect(x + 34f, y + height - 74f, 220f, 46f), "LET'S TRAIN", buttonStyle))
        {
            CompletionPending = false;
        }
        if (GUI.Button(new Rect(x + width - 254f, y + height - 74f, 220f, 46f), "OPEN JOURNAL", buttonStyle))
        {
            CompletionPending = false;
            Open();
        }
    }

    private void DrawBar(Rect rect, float fill)
    {
        GUI.DrawTexture(rect, barBackTexture);
        Rect inner = new Rect(rect.x + 2f, rect.y + 2f, (rect.width - 4f) * Mathf.Clamp01(fill),
            rect.height - 4f);
        if (inner.width > 0f)
        {
            GUI.DrawTexture(inner, barFillTexture);
        }
    }

    private void EnsureStyles()
    {
        if (headerStyle != null)
        {
            return;
        }
        headerStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 22,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleLeft
        };
        headerStyle.normal.textColor = PersonaMenuStyle.Gold;
        rowStyle = new GUIStyle(GUI.skin.label) { fontSize = 17, alignment = TextAnchor.UpperLeft };
        rowStyle.normal.textColor = PersonaMenuStyle.Ink;
        smallStyle = new GUIStyle(GUI.skin.label) { fontSize = 14, alignment = TextAnchor.MiddleLeft };
        smallStyle.normal.textColor = PersonaMenuStyle.Muted;
        buttonStyle = new GUIStyle(GUI.skin.button) { fontSize = 17, fontStyle = FontStyle.Bold };
        buttonStyle.normal.textColor = PersonaMenuStyle.Ink;
        panelTexture = Solid(new Color(0.02f, 0.045f, 0.1f, 0.94f));
        barBackTexture = Solid(new Color(1f, 1f, 1f, 0.12f));
        barFillTexture = Solid(PersonaMenuStyle.Gold);
    }

    private static Texture2D Solid(Color color)
    {
        Texture2D texture = new Texture2D(1, 1);
        texture.SetPixel(0, 0, color);
        texture.Apply();
        return texture;
    }
}
