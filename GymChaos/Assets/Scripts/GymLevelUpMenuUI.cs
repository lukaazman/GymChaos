using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Level-up stat choice in the start/pause menu language: the slowed game
/// stays visible under the same navy veil, a slanted red LEVEL UP slab with
/// Anton type sits top left, the four stat trees are the slanted Persona
/// buttons of the menu stack and a rimmed plate on the right previews the
/// focused tree. Allocation rules stay in <see cref="GymExperienceService"/>;
/// this only presents and forwards clicks and the 1-4 keys.
/// </summary>
public sealed class GymLevelUpMenuUI : MonoBehaviour
{
    private static readonly GymStat[] Stats =
    {
        GymStat.Strength, GymStat.Endurance, GymStat.Technique, GymStat.Reputation
    };

    private static readonly string[] StatNames = { "STRENGTH", "ENDURANCE", "TECHNIQUE", "KARMA" };

    private static readonly string[] StatBlurbs =
    {
        "HIT HARDER AND SHOVE FURTHER.  RANK 10: ONE-PUNCH KNOCKOUT.",
        "SPRINT FASTER AND LONGER.  RANK 10: TOP SPRINT SPEED.",
        "LONGER COMBOS AND CLEANER TIMING.  RANK 10: EVERY REP IS PERFECT.",
        "SOCIAL MOVES LAND HARDER.  RANK 10: THE GYM LEAVES YOU ALONE OR COMES FOR YOU."
    };

    private static GymLevelUpMenuUI instance;

    private GymExperienceService service;
    private Canvas canvas;
    private CanvasGroup group;
    private RectTransform titleSlab;
    private RectTransform infoPanel;
    private Text levelLabel;
    private Text infoTitle;
    private Text infoRank;
    private Text infoRankCount;
    private Text infoImpact;
    private Text infoBlurb;
    private Button[] statButtons;
    private PersonaShape[] rankPips;
    private int focusedIndex;
    private bool shown;
    private float shownAt;

    public static GymLevelUpMenuUI Active => instance;
    public bool IsShowingForVerification => shown;
    public Button[] StatButtonsForVerification => statButtons;
    public string InfoTitleForVerification => infoTitle != null ? infoTitle.text : string.Empty;

    public static GymLevelUpMenuUI Ensure(GymExperienceService owner)
    {
        if (instance == null)
        {
            GameObject root = new GameObject("Gym Level Up Menu");
            instance = root.AddComponent<GymLevelUpMenuUI>();
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
        canvas.sortingOrder = 150;
        GymRuntimeSettings.ConfigureBalancedCanvasScaler(gameObject.AddComponent<CanvasScaler>());
        gameObject.AddComponent<GraphicRaycaster>();
        group = gameObject.AddComponent<CanvasGroup>();

        Font fallback = PersonaMenuStyle.BuiltinFont;
        Font body = PersonaMenuStyle.LoadButtonFont(fallback);
        Font title = PersonaMenuStyle.LoadTitleFont(fallback);

        Image veil = GymRuntimeSettings.CreateImage("Level Up Veil", transform, PersonaMenuStyle.Veil);
        GymRuntimeSettings.Stretch(veil.rectTransform);

        // Title slab: red plate with an ink rim, white Anton type, the same
        // tilt family as the menu buttons.
        titleSlab = PersonaMenuStyle.CreatePanel(transform, "Level Up Slab", 330f, 150f, 520f, 128f, -4f,
            PersonaMenuStyle.Accent, PersonaMenuStyle.Ink);
        Text slabText = PersonaMenuStyle.CreateLabel(titleSlab, "Title", title, "LEVEL UP!",
            0f, 0f, 480f, 128f, 104, PersonaMenuStyle.Ink, TextAnchor.MiddleCenter);
        CenterInParent(slabText.rectTransform, 480f, 128f);
        slabText.horizontalOverflow = HorizontalWrapMode.Overflow;
        levelLabel = PersonaMenuStyle.CreateLabel(transform, "Level", body, string.Empty,
            360f, 248f, 560f, 52f, 46, PersonaMenuStyle.Gold, TextAnchor.MiddleLeft);
        levelLabel.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -2f);

        // Stat stack: staggered like the start menu, focus pops the plate.
        float[] centerX = { 340f, 312f, 292f, 278f };
        statButtons = new Button[Stats.Length];
        for (int i = 0; i < Stats.Length; i++)
        {
            int index = i;
            statButtons[i] = PersonaMenuStyle.CreateButton(transform, "Level Up " + StatNames[i], body,
                StatNames[i], centerX[i], 360f + i * 104f, 470f, 84f, 66, i % 2 == 0 ? -2f : -1f,
                () => Choose(index));
            statButtons[i].GetComponent<PersonaMenuButton>().Focused += () => SetFocused(index);
        }
        LinkVertical(statButtons);

        infoPanel = PersonaMenuStyle.CreatePanel(transform, "Level Up Info", 1150f, 500f, 760f, 380f, -1.5f,
            PersonaMenuStyle.PlateBlack, PersonaMenuStyle.Accent);
        infoTitle = AddInfoLabel("Stat", title, 26f, 680f, 92f, 78, PersonaMenuStyle.Gold);
        infoRank = AddInfoLabel("Rank", body, 108f, 680f, 52f, 44, PersonaMenuStyle.Ink);
        rankPips = new PersonaShape[GymExperienceService.MaxStatRank];
        float pipScale = 1080f / PersonaMenuStyle.ArtHeight;
        for (int i = 0; i < rankPips.Length; i++)
        {
            // Slanted segments like the HUD meters; filled ones turn red.
            PersonaShape pip = PersonaMenuStyle.CreateShape("Rank Pip " + (i + 1), infoPanel, PersonaMenuStyle.Navy);
            RectTransform rect = pip.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.sizeDelta = new Vector2(30f * pipScale, 30f * pipScale);
            rect.anchoredPosition = new Vector2((150f + i * 38f) * pipScale, -119f * pipScale);
            pip.SetSlant(0.35f);
            rankPips[i] = pip;
        }
        infoRankCount = AddInfoLabel("Rank Count", body, 108f, 160f, 52f, 44, PersonaMenuStyle.Ink);
        infoRankCount.rectTransform.anchoredPosition =
            new Vector2(540f * pipScale, infoRankCount.rectTransform.anchoredPosition.y);
        infoImpact = AddInfoLabel("Impact", body, 170f, 680f, 48f, 36, PersonaMenuStyle.Accent);
        infoBlurb = AddInfoLabel("Blurb", body, 228f, 680f, 110f, 34, PersonaMenuStyle.Muted);
        infoBlurb.alignment = TextAnchor.UpperLeft;
        AddInfoLabel("Hint", body, 318f, 680f, 40f, 30, PersonaMenuStyle.Muted).text =
            "1 - 4  OR  ENTER   SPEND ONE POINT";

        SetShown(false);
    }

    private Text AddInfoLabel(string name, Font font, float top, float width, float height, int size, Color color)
    {
        // Rows are laid out in art pixels from the plate's top-left corner.
        Text text = PersonaMenuStyle.CreateLabel(infoPanel, name, font, string.Empty,
            0f, 0f, width, height, size, color, TextAnchor.MiddleLeft);
        RectTransform rect = text.rectTransform;
        float scale = 1080f / PersonaMenuStyle.ArtHeight;
        rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(44f * scale, -top * scale);
        return text;
    }

    private static void CenterInParent(RectTransform rect, float width, float height)
    {
        float scale = 1080f / PersonaMenuStyle.ArtHeight;
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = new Vector2(width * scale, height * scale);
    }

    private static void LinkVertical(Button[] buttons)
    {
        for (int i = 0; i < buttons.Length; i++)
        {
            Navigation navigation = new Navigation { mode = Navigation.Mode.Explicit };
            navigation.selectOnUp = buttons[(i + buttons.Length - 1) % buttons.Length];
            navigation.selectOnDown = buttons[(i + 1) % buttons.Length];
            buttons[i].navigation = navigation;
        }
    }

    private void LateUpdate()
    {
        bool open = service != null && service.IsSkillChoiceVisible && service.SkillPoints > 0 &&
            !GymExperienceService.SkillChoiceDeferred &&
            !service.IsLockerMenuOpen && !GymStartScreen.IsMenuVisible && !GymPauseMenu.IsVisible;
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
        float slide = (1f - reveal) * 60f;
        titleSlab.localScale = Vector3.one * Mathf.Lerp(1.25f, 1f, reveal);
        infoPanel.anchoredPosition = new Vector2(slide, 0f);

        int key = ReadNumberKey();
        if (key >= 0)
        {
            Choose(key);
        }
    }

    /// <summary>Edit-mode verifier: shows the settled menu without LateUpdate.</summary>
    public void ShowForVerification()
    {
        SetShown(true);
        group.alpha = 1f;
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
            SetFocused(focusedIndex);
            if (EventSystem.current != null)
            {
                EventSystem.current.SetSelectedGameObject(statButtons[focusedIndex].gameObject);
            }
        }
        else if (EventSystem.current != null && EventSystem.current.currentSelectedGameObject != null &&
            EventSystem.current.currentSelectedGameObject.transform.IsChildOf(transform))
        {
            EventSystem.current.SetSelectedGameObject(null);
        }
    }

    private void Choose(int index)
    {
        if (service == null || index < 0 || index >= Stats.Length)
        {
            return;
        }
        int pointsBefore = service.SkillPoints;
        service.AllocateSkill(Stats[index]);
        if (service.SkillPoints < pointsBefore)
        {
            GymAudio.Play2D(GymSoundEffect.ActionConfirm, 0.6f);
        }
        else
        {
            GymAudio.Play2D(GymSoundEffect.UiBack, 0.55f);
        }
        focusedIndex = index;
        Refresh();
        SetFocused(index);
    }

    private void SetFocused(int index)
    {
        focusedIndex = Mathf.Clamp(index, 0, Stats.Length - 1);
        if (service == null)
        {
            return;
        }
        GymStat stat = Stats[focusedIndex];
        int rank = service.GetStatRank(stat);
        bool maxed = rank >= GymExperienceService.MaxStatRank;
        infoTitle.text = StatNames[focusedIndex];
        infoRank.text = "RANK";
        for (int i = 0; i < rankPips.Length; i++)
        {
            rankPips[i].color = i < rank ? PersonaMenuStyle.Accent : PersonaMenuStyle.Navy;
        }
        infoRankCount.text = rank + " / " + GymExperienceService.MaxStatRank;
        infoImpact.text = maxed
            ? (service.AllStatsMaxed ? "MASTERY  +1" : "TREE MAXED - PICK ANOTHER")
            : service.GetSkillChoicePreview(stat);
        infoBlurb.text = StatBlurbs[focusedIndex];
    }

    private void Refresh()
    {
        if (service == null)
        {
            return;
        }
        int points = service.SkillPoints;
        levelLabel.text = "LEVEL " + service.Level + "   " + points + (points == 1 ? " POINT" : " POINTS");
        for (int i = 0; i < Stats.Length; i++)
        {
            int rank = service.GetStatRank(Stats[i]);
            statButtons[i].GetComponentInChildren<Text>().text = StatNames[i] + "   " + rank;
        }
    }

    private static int ReadNumberKey()
    {
#if ENABLE_INPUT_SYSTEM
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null) return -1;
        if (keyboard.digit1Key.wasPressedThisFrame || keyboard.numpad1Key.wasPressedThisFrame) return 0;
        if (keyboard.digit2Key.wasPressedThisFrame || keyboard.numpad2Key.wasPressedThisFrame) return 1;
        if (keyboard.digit3Key.wasPressedThisFrame || keyboard.numpad3Key.wasPressedThisFrame) return 2;
        if (keyboard.digit4Key.wasPressedThisFrame || keyboard.numpad4Key.wasPressedThisFrame) return 3;
        return -1;
#else
        if (Input.GetKeyDown(KeyCode.Alpha1) || Input.GetKeyDown(KeyCode.Keypad1)) return 0;
        if (Input.GetKeyDown(KeyCode.Alpha2) || Input.GetKeyDown(KeyCode.Keypad2)) return 1;
        if (Input.GetKeyDown(KeyCode.Alpha3) || Input.GetKeyDown(KeyCode.Keypad3)) return 2;
        if (Input.GetKeyDown(KeyCode.Alpha4) || Input.GetKeyDown(KeyCode.Keypad4)) return 3;
        return -1;
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
