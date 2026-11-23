using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Create your character: height and name (class is the page before) beside a live turntable
/// (GymJournalPreview). The body is the DEFAULT class scan — one model for every
/// run — and the height slider is the only thing that changes it. A save from
/// before this page only lacks a name, so it opens on the name field.
/// </summary>
public sealed partial class GymSessionMenu
{
    private GameObject customizePage;
    private Text lookNote;
    private Text previewNametag;
    private Slider heightSlider;
    private Text heightValue;
    private InputField nameInput;
    private Button customizeConfirmButton;
    private RawImage turntableView;
    private GymJournalPreview turntable;
    // Null until a new game reaches customize; kept while the player goes back to change class.
    private GymCharacterSave look;
    private bool customizeLocked;

    public GymCharacterSave CustomizeLook => look;
    public bool CustomizeLocked => customizeLocked;
    public bool TurntableShowing => turntable != null && turntable.PreviewVisible;
    public GymJournalPreview Turntable => turntable;

    /// <summary>
    /// Verifier seam: one press along New Game after the class page (Confirm,
    /// then name and Confirm on Customize). Call until the menu leaves both.
    /// </summary>
    public void ConfirmNewCharacterForVerification(string name)
    {
        if (state == State.ClassSelect) ConfirmClass();
        else if (state == State.Customize)
        {
            SetUsername(name);
            ConfirmCustomize();
        }
    }

    public bool InNewCharacterPages => state == State.ClassSelect || state == State.Customize;

    // The page lives in the 1672 x 941 menu art, so every row, the turntable
    // and the buttons have to fit inside it. Left column: x 100..860; the slash
    // edge runs from x 961 (top) to 1122 (bottom), the turntable sits centred
    // between that edge and the right border of the art.
    private const float ColumnLeft = 100f;
    private const float ColumnWidth = 760f;
    private const float HeightLabelY = 262f;
    private const float HeightSliderY = 346f;
    private const float NameLabelY = 480f;
    private const float NameInputY = 560f;

    private void BuildCustomizePage()
    {
        customizePage = CreatePage("Customize", true);
        Transform parent = customizePage.transform;
        RectTransform titlePlate = PersonaMenuStyle.CreatePanel(parent, "Title Plate", 360f, 74f, 640f, 84f, -3f,
            PersonaMenuStyle.PlateBlack, PersonaMenuStyle.Ink);
        Text title = PersonaMenuStyle.CreateLabel(parent, "Title", titleFont, "CREATE YOUR CHARACTER",
            366f, 74f, 600f, 84f, 56, PersonaMenuStyle.Ink, TextAnchor.MiddleCenter);
        title.rectTransform.localRotation = titlePlate.localRotation;

        // Height: caption left, value right, slider below with its range.
        Row(parent, "HEIGHT", HeightLabelY);
        heightValue = PersonaMenuStyle.CreateLabel(parent, "Height Value", titleFont, string.Empty,
            ColumnLeft + ColumnWidth - 150f, HeightLabelY, 300f, 56f, 48, PersonaMenuStyle.Ink, TextAnchor.MiddleRight);
        heightSlider = CreateSlider(parent, ColumnLeft + ColumnWidth * 0.5f, HeightSliderY, ColumnWidth, 44f);
        PersonaMenuStyle.CreateLabel(parent, "Height Min", bodyFont, PlayerAppearance.MinHeight + " CM",
            ColumnLeft + 60f, HeightSliderY + 42f, 120f, 28f, 22, PersonaMenuStyle.Muted, TextAnchor.MiddleLeft);
        PersonaMenuStyle.CreateLabel(parent, "Height Max", bodyFont, PlayerAppearance.MaxHeight + " CM",
            ColumnLeft + ColumnWidth - 60f, HeightSliderY + 42f, 120f, 28f, 22, PersonaMenuStyle.Muted,
            TextAnchor.MiddleRight);

        // Name: the field's left edge lines up with the caption above it.
        Row(parent, "NAME", NameLabelY);
        nameInput = CreateInput(parent, "Name Input", "Your name", ColumnLeft + ColumnWidth * 0.5f, NameInputY,
            ColumnWidth, 64f, PlayerAppearance.NameMax, SetUsername);
        nameInput.transform.localRotation = Quaternion.identity;
        Outline border = nameInput.GetComponent<Image>().gameObject.AddComponent<Outline>();
        border.effectColor = PersonaMenuStyle.Accent;
        border.effectDistance = new Vector2(2f, -2f);
        PersonaMenuStyle.CreateLabel(parent, "Name Rule", bodyFont,
            "3 TO 16 LETTERS OR NUMBERS.", ColumnLeft + 300f, NameInputY + 54f, 600f, 32f, 24,
            PersonaMenuStyle.Muted, TextAnchor.MiddleLeft);

        lookNote = PersonaMenuStyle.CreateLabel(parent, "Look Note", bodyFont, string.Empty,
            ColumnLeft + 300f, 690f, 600f, 34f, 26, PersonaMenuStyle.Gold, TextAnchor.MiddleLeft);

        turntableView = CreateArt(parent, "Turntable", 1370f, 486f, 520f, 760f);
        turntableView.raycastTarget = true;
        // The one name on this page: the same tag the enemies wear in game,
        // pinned above the previewed head and moving with it.
        previewNametag = CreatePreviewTag(turntableView.rectTransform);

        customizeConfirmButton = PersonaMenuStyle.CreateButton(parent, "Customize Confirm Button", bodyFont,
            "CONFIRM", 640f, 850f, 330f, 84f, 70, -2f, ConfirmCustomize);
        PersonaMenuStyle.CreateButton(parent, "Customize Back Button", bodyFont,
            "BACK", 260f, 856f, 200f, 60f, 46, -1f, Back);
    }

    private void Row(Transform parent, string label, float y) =>
        PersonaMenuStyle.CreateLabel(parent, label + " Label", bodyFont, label, ColumnLeft + 150f, y, 300f, 40f, 30,
            PersonaMenuStyle.Muted, TextAnchor.MiddleLeft);

    private Slider CreateSlider(Transform parent, float x, float y, float width, float height)
    {
        GameObject root = new GameObject("Height Slider", typeof(RectTransform), typeof(Slider));
        root.transform.SetParent(parent, false);
        PersonaMenuStyle.PlaceInArt(root.GetComponent<RectTransform>(), x, y, width, height);
        // Invisible hit plate across the whole row, so a click anywhere on the
        // line grabs the slider, not only on the thin rail.
        Image hit = root.AddComponent<Image>();
        hit.color = new Color(1f, 1f, 1f, 0f);
        hit.raycastTarget = true;
        PersonaShape track = PersonaMenuStyle.CreateShape("Track", root.transform, PersonaMenuStyle.Veil);
        // A thin rail inside the row, clear of the rows above and below.
        SetRail(track.rectTransform, 0.36f, 0.64f);
        track.SetSlant(0.02f);
        // The plate is what the pointer hits: without a raycast target on it the
        // whole slider is a picture and never moves.
        track.raycastTarget = true;
        GameObject fillArea = new GameObject("Fill Area", typeof(RectTransform));
        fillArea.transform.SetParent(root.transform, false);
        SetRail(fillArea.GetComponent<RectTransform>(), 0.36f, 0.64f);
        PersonaShape fill = PersonaMenuStyle.CreateShape("Fill", fillArea.transform, PersonaMenuStyle.Accent);
        // The Slider writes this rect's anchors; a leftover 100x100 size made the
        // red plate balloon far past the rail.
        RectTransform fillRect = fill.rectTransform;
        fillRect.anchorMin = Vector2.zero;
        fillRect.anchorMax = Vector2.one;
        fillRect.offsetMin = Vector2.zero;
        fillRect.offsetMax = Vector2.zero;
        fill.SetSlant(0.04f);
        fill.raycastTarget = false;
        GameObject handleArea = new GameObject("Handle Area", typeof(RectTransform));
        handleArea.transform.SetParent(root.transform, false);
        SetRail(handleArea.GetComponent<RectTransform>(), 0.08f, 0.92f);
        PersonaShape handle = PersonaMenuStyle.CreateShape("Handle", handleArea.transform, PersonaMenuStyle.Ink);
        RectTransform handleRect = handle.rectTransform;
        handleRect.anchorMin = new Vector2(0f, 0f);
        handleRect.anchorMax = new Vector2(0f, 1f);
        handleRect.pivot = new Vector2(0.5f, 0.5f);
        handleRect.anchoredPosition = Vector2.zero;
        // A tall white grip over the rail; the Slider only moves its anchor.
        handleRect.sizeDelta = new Vector2(20f, 0f);
        Slider slider = root.GetComponent<Slider>();
        slider.fillRect = fill.rectTransform;
        slider.handleRect = handle.rectTransform;
        slider.targetGraphic = handle;
        slider.minValue = PlayerAppearance.MinHeight;
        slider.maxValue = PlayerAppearance.MaxHeight;
        slider.wholeNumbers = true;
        slider.onValueChanged.AddListener(value => SetHeight(Mathf.RoundToInt(value)));
        return slider;
    }

    /// <summary>Stretches a rect horizontally between two heights of its parent.</summary>
    private static void SetRail(RectTransform rect, float fromY, float toY)
    {
        rect.anchorMin = new Vector2(0f, fromY);
        rect.anchorMax = new Vector2(1f, toY);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        rect.localScale = Vector3.one;
        rect.localRotation = Quaternion.identity;
    }

    // ---- flow ----

    private void BeginCustomize(bool locked)
    {
        if (locked || customizeLocked || look == null)
        {
            look = new GymCharacterSave();
            if (locked && GymSessionService.Active != null) look.CopyLook(GymSessionService.Active);
        }
        customizeLocked = locked;
        look.classId = locked && GymSessionService.Active != null ? GymSessionService.Active.classId : SelectedClassId;
        if (turntable == null)
        {
            turntable = GymJournalPreview.Create(transform);
            turntable.SetViewRect(turntableView.rectTransform);
        }
        // The retired look fields stay default so old saves and the network
        // path keep one shape.
        look.gender = 0;
        look.skinTone = 2;
        look.hair = 2;
        look.hairColor = 0;
        look.facialHair = 0;
        if (look.heightCm <= 0) look.heightCm = PlayerAppearance.DefaultMaleHeight;
        lookNote.text = string.Empty;
        Show(State.Customize);
    }

    public void SetHeight(int heightCm)
    {
        if (state != State.Customize) return;
        look.heightCm = Mathf.Clamp(heightCm, PlayerAppearance.MinHeight, PlayerAppearance.MaxHeight);
        RefreshCustomize();
    }

    /// <summary>Typed text; stored only once it is a valid name.</summary>
    public void SetUsername(string text)
    {
        if (state != State.Customize) return;
        if (nameInput != null && nameInput.text != text) nameInput.SetTextWithoutNotify(text);
        look.username = PlayerAppearance.SanitizeName(text) ?? string.Empty;
        lookNote.text = string.Empty;
        RefreshCustomize();
    }

    public void ConfirmCustomize()
    {
        if (!Accepts(State.Customize)) return;
        if (!look.IsCustomized)
        {
            lookNote.text = "ENTER A NAME FIRST";
            GymShared.Select(nameInput);
            return;
        }
        turntable?.Hide();
        if (customizeLocked)
        {
            GymSessionService.ApplyLook(look);
        }
        else
        {
            GymSessionService.BeginNew(SelectedClassId, look);
        }
        Debug.Log("GYMCHAOS_CUSTOMIZE_CONFIRMED name=" + look.username + " height=" + look.heightCm +
            " locked=" + customizeLocked);
        StartSession();
    }

    private void BackFromCustomize()
    {
        turntable?.Hide();
        if (customizeLocked)
        {
            // The old save stays as it was; loading it again asks again.
            GymSessionService.AbandonSession();
            Show(State.SavePreview);
            return;
        }
        Show(State.ClassSelect);
    }

    private void RefreshCustomize()
    {
        if (customizePage == null) return;
        heightSlider.SetValueWithoutNotify(look.heightCm);
        heightValue.text = look.heightCm + " CM";
        if (nameInput.text.Length == 0 && look.IsCustomized) nameInput.SetTextWithoutNotify(look.username);
        if (turntable != null)
        {
            turntableView.texture = turntable.Target;
            turntableView.enabled = true;
            turntable.Show(look.classId, new GymProgressionState(), look);
        }
    }

    /// <summary>The enemy name tag, on the create page: white built-in type, lower centred.</summary>
    private static Text CreatePreviewTag(RectTransform parent)
    {
        GameObject tag = new GameObject("Preview Nametag", typeof(RectTransform), typeof(Text));
        tag.transform.SetParent(parent, false);
        RectTransform rect = tag.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0f);
        rect.sizeDelta = new Vector2(560f, 40f);
        Text text = tag.GetComponent<Text>();
        text.font = PersonaMenuStyle.BuiltinFont;
        text.fontSize = 24;
        text.color = Color.white;
        text.alignment = TextAnchor.LowerCenter;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.raycastTarget = false;
        tag.SetActive(false);
        return text;
    }

    /// <summary>
    /// Pins the tag to the previewed head every frame: the idle clip moves the
    /// skull a little, and the tag rides it exactly like an in-game enemy tag.
    /// </summary>
    private void LateUpdate()
    {
        if (previewNametag == null)
        {
            return;
        }
        bool show = state == State.Customize && turntable != null && turntable.PreviewVisible &&
            turntableView != null;
        Vector2 position = Vector2.zero;
        bool placed = false;
        if (show)
        {
            placed = turntable.TryGetHeadLocal(turntableView.rectTransform, out position);
            if (!placed)
            {
                position = new Vector2(0f, ((RectTransform)turntableView.transform).rect.height * 0.32f);
            }
        }
        if (!show)
        {
            if (previewNametag.gameObject.activeSelf) previewNametag.gameObject.SetActive(false);
            return;
        }
        if (!previewNametag.gameObject.activeSelf) previewNametag.gameObject.SetActive(true);
        string typed = nameInput != null ? nameInput.text.Trim() : string.Empty;
        if (typed.Length == 0)
        {
            previewNametag.gameObject.SetActive(false);
            return;
        }
        previewNametag.text = typed.ToUpperInvariant();
        ((RectTransform)previewNametag.transform).anchoredPosition = position + new Vector2(0f, 10f);
    }
}
