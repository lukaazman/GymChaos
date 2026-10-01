using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Conversation surface in the menu and HUD style: black letterbox bars, an
/// upright ink-rimmed plate with the speaker on a red tag, Bebas body text and
/// the same Persona buttons as the start and pause menus for choices (red
/// focus plate, white slab, scale-up on hover or keyboard focus). It only
/// presents the director's current node; input rules stay in the director.
/// </summary>
[DefaultExecutionOrder(-20)]
public sealed class GymDialogueUI : MonoBehaviour
{
    private const float PresentationDuration = 0.34f;
    private const float BeatDuration = 0.24f;
    private const float PanelMargin = 56f;
    private const float PanelPadding = 34f;
    private const float ChoiceHeight = 54f;
    private const float ChoiceGap = 20f;
    private const int MaxChoices = 3;

    private GymDialogueDirector director;
    private Canvas canvas;
    private CanvasGroup group;
    private RectTransform topBar;
    private RectTransform bottomBar;
    private RectTransform panel;
    private RectTransform speakerTag;
    private Text speakerLabel;
    private Text bodyLabel;
    private Text hintLabel;
    private RectTransform accentLine;
    private readonly Button[] choiceButtons = new Button[MaxChoices];
    private readonly Text[] choiceLabels = new Text[MaxChoices];
    private Button continueButton;
    private DialogueNode shownNode;
    private float presentationStartedAt;
    private float beatStartedAt = -1f;
    private bool wasActive;

    public bool IsShowingForVerification => wasActive && group != null && group.alpha > 0f;
    public string SpeakerTextForVerification => speakerLabel != null ? speakerLabel.text : string.Empty;
    public Button[] ChoiceButtonsForVerification => choiceButtons;
    public Button ContinueButtonForVerification => continueButton;
    public RectTransform PanelForVerification => panel;

    public void Bind(GymDialogueDirector owner)
    {
        director = owner;
        if (canvas == null)
        {
            Build();
        }
    }

    private void Build()
    {
        GymRuntimeSettings.EnsureEventSystem();
        GameObject root = new GameObject("Gym Dialogue Canvas", typeof(RectTransform));
        root.transform.SetParent(transform, false);
        canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        // Above the HUD (50), below the start (100) and pause (200) menus.
        canvas.sortingOrder = 60;
        GymRuntimeSettings.ConfigureBalancedCanvasScaler(root.AddComponent<CanvasScaler>());
        root.AddComponent<GraphicRaycaster>();
        group = root.AddComponent<CanvasGroup>();

        Font fallback = PersonaMenuStyle.BuiltinFont;
        Font body = PersonaMenuStyle.LoadButtonFont(fallback);
        Font title = PersonaMenuStyle.LoadTitleFont(fallback);

        topBar = Bar(root.transform, "Letterbox Top", new Vector2(0f, 1f));
        bottomBar = Bar(root.transform, "Letterbox Bottom", new Vector2(0f, 0f));
        Text status = PersonaMenuStyle.CreateLayoutLabel(topBar, "Status", body, "MEMBER TALK",
            new Vector2(PanelMargin, 0f), new Vector2(300f, 0f), 26, PersonaMenuStyle.Muted, TextAnchor.MiddleLeft);
        status.rectTransform.anchorMin = new Vector2(0f, 0f);
        status.rectTransform.anchorMax = new Vector2(0f, 1f);
        status.rectTransform.sizeDelta = new Vector2(300f, 0f);
        accentLine = PersonaMenuStyle.CreateShape("Accent Line", topBar, PersonaMenuStyle.Gold).rectTransform;
        accentLine.anchorMin = accentLine.anchorMax = new Vector2(0f, 0f);
        accentLine.pivot = new Vector2(0f, 0f);
        accentLine.anchoredPosition = new Vector2(PanelMargin, 0f);
        accentLine.sizeDelta = new Vector2(56f, 3f);

        GameObject panelObject = new GameObject("Dialogue Panel", typeof(RectTransform));
        panelObject.transform.SetParent(root.transform, false);
        panel = panelObject.GetComponent<RectTransform>();
        panel.anchorMin = new Vector2(0f, 0f);
        panel.anchorMax = new Vector2(1f, 0f);
        panel.pivot = new Vector2(0.5f, 0f);
        PersonaMenuStyle.AddUprightPlate(panel, PersonaMenuStyle.Ink, PersonaMenuStyle.PlateBlack);

        // Speaker on a red tag that straddles the plate's top edge, like the
        // HUD level box on the class picture.
        GameObject tagObject = new GameObject("Speaker Tag", typeof(RectTransform));
        tagObject.transform.SetParent(panel, false);
        speakerTag = tagObject.GetComponent<RectTransform>();
        speakerTag.anchorMin = speakerTag.anchorMax = new Vector2(0f, 1f);
        speakerTag.pivot = new Vector2(0f, 0.5f);
        speakerTag.anchoredPosition = new Vector2(PanelPadding - 6f, 0f);
        speakerTag.sizeDelta = new Vector2(260f, 56f);
        PersonaMenuStyle.AddUprightPlate(speakerTag, PersonaMenuStyle.Ink, PersonaMenuStyle.Accent);
        speakerLabel = PersonaMenuStyle.CreateLayoutLabel(speakerTag, "Speaker", title, string.Empty,
            new Vector2(18f, 0f), new Vector2(0f, 56f), 34, PersonaMenuStyle.Ink, TextAnchor.MiddleLeft);
        speakerLabel.horizontalOverflow = HorizontalWrapMode.Overflow;

        bodyLabel = PersonaMenuStyle.CreateLayoutLabel(panel, "Body", body, string.Empty,
            new Vector2(PanelPadding, 50f), new Vector2(0f, 0f), 38, PersonaMenuStyle.Ink, TextAnchor.UpperLeft);
        bodyLabel.lineSpacing = 1.05f;

        hintLabel = PersonaMenuStyle.CreateLayoutLabel(panel, "Hint", body, string.Empty,
            Vector2.zero, new Vector2(520f, 30f), 24, PersonaMenuStyle.Muted, TextAnchor.MiddleRight);
        hintLabel.rectTransform.anchorMin = hintLabel.rectTransform.anchorMax = new Vector2(1f, 0f);
        hintLabel.rectTransform.pivot = new Vector2(1f, 0f);
        hintLabel.rectTransform.anchoredPosition = new Vector2(-PanelPadding, 14f);

        for (int i = 0; i < MaxChoices; i++)
        {
            int index = i;
            choiceButtons[i] = PersonaMenuStyle.CreateLayoutButton(panel, "Choice " + (i + 1), body, string.Empty,
                Vector2.zero, new Vector2(300f, ChoiceHeight), 34, () => Choose(index));
            choiceLabels[i] = choiceButtons[i].GetComponentInChildren<Text>();
            choiceButtons[i].gameObject.SetActive(false);
        }
        continueButton = PersonaMenuStyle.CreateLayoutButton(panel, "Continue", body, "CONTINUE",
            Vector2.zero, new Vector2(230f, 54f), 32, Continue);
        continueButton.gameObject.SetActive(false);

        group.alpha = 0f;
        group.blocksRaycasts = false;
        group.interactable = false;
    }

    private static RectTransform Bar(Transform parent, string name, Vector2 anchor)
    {
        PersonaShape bar = PersonaMenuStyle.CreateShape(name, parent, new Color(0.005f, 0.007f, 0.012f, 0.98f));
        RectTransform rect = bar.rectTransform;
        rect.anchorMin = new Vector2(0f, anchor.y);
        rect.anchorMax = new Vector2(1f, anchor.y);
        rect.pivot = new Vector2(0.5f, anchor.y);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = new Vector2(0f, 0f);
        return rect;
    }

    private void LateUpdate()
    {
        if (canvas == null || director == null)
        {
            return;
        }

        bool visible = GymDialogueDirector.IsDialogueActive &&
            !GymStartScreen.IsMenuVisible && !GymPauseMenu.IsVisible;
        DialogueNode node = visible ? director.CurrentNode : null;
        if (node == null)
        {
            if (wasActive)
            {
                Hide();
            }
            return;
        }

        float now = Time.unscaledTime;
        if (!wasActive)
        {
            wasActive = true;
            presentationStartedAt = now;
            group.blocksRaycasts = true;
            group.interactable = true;
        }
        if (node != shownNode)
        {
            shownNode = node;
            beatStartedAt = now;
            Present(node);
        }

        float reveal = Ease01((now - presentationStartedAt) / PresentationDuration);
        float beat = beatStartedAt >= 0f ? 1f - Ease01((now - beatStartedAt) / BeatDuration) : 0f;
        float barHeight = Mathf.Lerp(0f, 86f, director.LetterboxBlend);
        topBar.sizeDelta = new Vector2(0f, barHeight);
        bottomBar.sizeDelta = new Vector2(0f, barHeight);
        accentLine.sizeDelta = new Vector2(Mathf.Lerp(26f, 56f, reveal) + beat * 14f, 3f);
        group.alpha = reveal;
        panel.anchoredPosition = new Vector2(0f, barHeight + 26f - (1f - reveal) * 40f);
        float pulse = 1f + beat * 0.06f;
        speakerTag.localScale = new Vector3(pulse, pulse, 1f);
    }

    private void Present(DialogueNode node)
    {
        int count = Mathf.Min(node.choices.Count, MaxChoices);
        float width = ((RectTransform)canvas.transform).rect.width - PanelMargin * 2f;

        // Choices are as wide as their text plus padding and flow left to
        // right, wrapping to a new row only when the panel is full.
        float rowLimit = width - PanelPadding * 2f - 24f;
        var widths = new float[MaxChoices];
        var columnX = new float[MaxChoices];
        var rowIndex = new int[MaxChoices];
        int rows = 0;
        float cursor = float.PositiveInfinity;
        for (int i = 0; i < count; i++)
        {
            choiceLabels[i].text = (i + 1) + "   " + node.choices[i].text;
            widths[i] = Mathf.Min(rowLimit, PersonaMenuStyle.FitLayoutButtonWidth(choiceButtons[i], 180f));
            if (cursor + widths[i] > rowLimit)
            {
                rows++;
                cursor = 0f;
            }
            rowIndex[i] = rows - 1;
            columnX[i] = cursor;
            cursor += widths[i] + ChoiceGap;
        }
        float choiceArea = rows > 0 ? rows * ChoiceHeight + (rows - 1) * ChoiceGap : 0f;

        speakerLabel.text = string.IsNullOrEmpty(node.speaker) ? string.Empty : node.speaker.ToUpperInvariant();
        speakerTag.sizeDelta = new Vector2(Mathf.Max(200f, speakerLabel.preferredWidth + 44f), 56f);
        bodyLabel.text = node.text;
        bodyLabel.rectTransform.sizeDelta = new Vector2(width - PanelPadding * 2f, 0f);
        float bodyHeight = Mathf.Max(46f, bodyLabel.preferredHeight);
        bodyLabel.rectTransform.sizeDelta = new Vector2(width - PanelPadding * 2f, bodyHeight);

        float choiceTop = 50f + bodyHeight + 30f;
        float panelHeight = choiceTop + (count > 0 ? choiceArea + 58f : 60f + 20f);
        panel.sizeDelta = new Vector2(-PanelMargin * 2f, panelHeight);

        for (int i = 0; i < MaxChoices; i++)
        {
            bool active = i < count;
            choiceButtons[i].gameObject.SetActive(active);
            if (!active) continue;
            RectTransform rect = (RectTransform)choiceButtons[i].transform;
            rect.sizeDelta = new Vector2(widths[i], ChoiceHeight);
            rect.anchoredPosition = new Vector2(
                PanelPadding + 12f + columnX[i] + widths[i] * 0.5f,
                -(choiceTop + rowIndex[i] * (ChoiceHeight + ChoiceGap) + ChoiceHeight * 0.5f));
        }

        continueButton.gameObject.SetActive(count == 0);
        if (count == 0)
        {
            RectTransform rect = (RectTransform)continueButton.transform;
            rect.sizeDelta = new Vector2(PersonaMenuStyle.FitLayoutButtonWidth(continueButton, 160f), rect.sizeDelta.y);
            rect.anchoredPosition = new Vector2(PanelPadding + 12f + rect.sizeDelta.x * 0.5f,
                -(choiceTop + rect.sizeDelta.y * 0.5f));
        }
        hintLabel.text = count > 0 ? "1 - " + count + "  CHOOSE      ESC  CLOSE" : "SPACE  CONTINUE      ESC  CLOSE";

        // Keyboard and controller start on the first option, like the menus.
        if (EventSystem.current != null)
        {
            Button focus = count > 0 ? choiceButtons[0] : continueButton;
            EventSystem.current.SetSelectedGameObject(focus.gameObject);
        }
    }

    private void Choose(int index)
    {
        if (director != null && GymDialogueDirector.IsDialogueActive)
        {
            director.ChooseFromUi(index);
        }
    }

    private void Continue()
    {
        if (director != null && GymDialogueDirector.IsDialogueActive)
        {
            director.AdvanceFromUi();
        }
    }

    private void Hide()
    {
        wasActive = false;
        shownNode = null;
        beatStartedAt = -1f;
        group.alpha = 0f;
        group.blocksRaycasts = false;
        group.interactable = false;
        topBar.sizeDelta = Vector2.zero;
        bottomBar.sizeDelta = Vector2.zero;
        if (EventSystem.current != null && EventSystem.current.currentSelectedGameObject != null &&
            EventSystem.current.currentSelectedGameObject.transform.IsChildOf(canvas.transform))
        {
            EventSystem.current.SetSelectedGameObject(null);
        }
    }

    private static float Ease01(float value)
    {
        value = Mathf.Clamp01(value);
        return value * value * (3f - 2f * value);
    }

    private void OnDisable()
    {
        if (canvas != null && wasActive)
        {
            Hide();
        }
    }
}
