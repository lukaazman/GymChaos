using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Barbell and stack load selector in the menu and HUD style: an upright
/// ink-rimmed plate with a red title tag, Bebas labels and a Persona button for
/// every load. Cancelling (Q, E, ESC) stays with <see cref="PlayerMovement"/>;
/// this only presents the options and forwards the chosen weight.
/// </summary>
public sealed class GymWeightMenuUI : MonoBehaviour
{
    private const float PanelWidth = 646f;
    private const float Padding = 34f;
    private const float ButtonWidth = 170f;
    private const float ButtonHeight = 56f;
    private const float ButtonGap = 22f;
    private const float RowGap = 18f;
    private const float ButtonsTop = 146f;
    private const float FooterHeight = 74f;
    private const int Columns = 3;

    private static GymWeightMenuUI instance;

    private Canvas canvas;
    private CanvasGroup group;
    private RectTransform panel;
    private Font body;
    private Font title;
    private float shownAt;
    private bool shown;

    public static GymWeightMenuUI Ensure()
    {
        if (instance == null)
        {
            GameObject root = new GameObject("Gym Weight Menu");
            instance = root.AddComponent<GymWeightMenuUI>();
            instance.Build();
        }
        return instance;
    }

    public static void Hide()
    {
        if (instance != null && instance.shown)
        {
            instance.SetShown(false);
        }
    }

    public void Show(GymExerciseStation station, System.Action<int> onSelect)
    {
        int[] options = station.WeightOptions;
        if (options == null || options.Length == 0)
        {
            return;
        }
        BuildPanel(station, options, onSelect);
        SetShown(true);
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
        body = PersonaMenuStyle.LoadButtonFont(fallback);
        title = PersonaMenuStyle.LoadTitleFont(fallback);
        SetShown(false);
    }

    private void BuildPanel(GymExerciseStation station, int[] options, System.Action<int> onSelect)
    {
        if (panel != null)
        {
            Destroy(panel.gameObject);
        }

        int rows = Mathf.CeilToInt(options.Length / (float)Columns);
        float panelHeight = ButtonsTop + rows * ButtonHeight + (rows - 1) * RowGap + FooterHeight;

        GameObject panelObject = new GameObject("Weight Panel", typeof(RectTransform));
        panelObject.transform.SetParent(transform, false);
        panel = panelObject.GetComponent<RectTransform>();
        panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(0.5f, 0.5f);
        panel.anchoredPosition = Vector2.zero;
        panel.sizeDelta = new Vector2(PanelWidth, panelHeight);
        PersonaMenuStyle.AddUprightPlate(panel, PersonaMenuStyle.Ink, PersonaMenuStyle.PlateBlack);

        GameObject tagObject = new GameObject("Title Tag", typeof(RectTransform));
        tagObject.transform.SetParent(panel, false);
        RectTransform titleTag = tagObject.GetComponent<RectTransform>();
        titleTag.anchorMin = titleTag.anchorMax = new Vector2(0f, 1f);
        titleTag.pivot = new Vector2(0f, 0.5f);
        titleTag.anchoredPosition = new Vector2(Padding - 6f, 0f);
        titleTag.sizeDelta = new Vector2(260f, 60f);
        PersonaMenuStyle.AddUprightPlate(titleTag, PersonaMenuStyle.Ink, PersonaMenuStyle.Accent);
        Text titleText = PersonaMenuStyle.CreateLayoutLabel(titleTag, "Title", title, "SELECT LOAD",
            new Vector2(20f, 0f), new Vector2(230f, 60f), 38, PersonaMenuStyle.Ink, TextAnchor.MiddleLeft);
        titleText.horizontalOverflow = HorizontalWrapMode.Overflow;

        PersonaMenuStyle.CreateLayoutLabel(panel, "Station", body, station.DisplayName.ToUpperInvariant(),
            new Vector2(Padding, 52f), new Vector2(PanelWidth - Padding * 2f, 38f), 34,
            PersonaMenuStyle.Gold, TextAnchor.MiddleLeft);
        string weightInfo = station.EmptyBarWeight > 0
            ? $"BAR  {station.EmptyBarWeight} KG"
            : "WEIGHT STACK";
        PersonaMenuStyle.CreateLayoutLabel(panel, "Bar", body, weightInfo,
            new Vector2(Padding, 94f), new Vector2(PanelWidth - Padding * 2f, 30f), 28,
            PersonaMenuStyle.Muted, TextAnchor.MiddleLeft);

        for (int i = 0; i < options.Length; i++)
        {
            int weight = options[i];
            Vector2 cell = new Vector2(
                Padding + 12f + (i % Columns) * (ButtonWidth + ButtonGap),
                ButtonsTop + (i / Columns) * (ButtonHeight + RowGap));
            Button button = PersonaMenuStyle.CreateLayoutButton(panel, "Weight " + weight, body, weight + " KG",
                cell, new Vector2(ButtonWidth, ButtonHeight), 32, () => onSelect?.Invoke(weight));
            PersonaMenuStyle.FitLabelToButton(button, 32);
        }

        PersonaMenuStyle.CreateLayoutLabel(panel, "Cancel Hint", body, "Q / E / ESC   CANCEL",
            new Vector2(Padding, panelHeight - Padding - 22f), new Vector2(PanelWidth - Padding * 2f, 28f), 26,
            PersonaMenuStyle.Muted, TextAnchor.MiddleLeft);
    }

    private void LateUpdate()
    {
        if (!shown)
        {
            return;
        }
        canvas.enabled = !GymStartScreen.IsMenuVisible && !GymPauseMenu.IsVisible;
        float reveal = Mathf.Clamp01((Time.unscaledTime - shownAt) / PersonaMenuStyle.TransitionSeconds);
        group.alpha = reveal;
        panel.anchoredPosition = new Vector2(0f, -(1f - reveal) * 28f);
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
            if (EventSystem.current != null && panel != null)
            {
                Button first = panel.GetComponentInChildren<Button>();
                if (first != null) EventSystem.current.SetSelectedGameObject(first.gameObject);
            }
        }
        else
        {
            if (panel != null)
            {
                Destroy(panel.gameObject);
                panel = null;
            }
            if (EventSystem.current != null && EventSystem.current.currentSelectedGameObject != null &&
                EventSystem.current.currentSelectedGameObject.transform.IsChildOf(transform))
            {
                EventSystem.current.SetSelectedGameObject(null);
            }
        }
    }

    private void OnDestroy()
    {
        if (instance == this)
        {
            instance = null;
        }
    }
}
