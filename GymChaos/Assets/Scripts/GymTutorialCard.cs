using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The lesson banner: the same Persona plate language as the settings sheet and
/// the boot menu, so a tutorial prompt reads as part of the game instead of a
/// separate overlay. A red lesson-number band on the left, the navy sheet with
/// its red edge and white backing slab, an Anton title, a Bebas line and a gold
/// key slab on the right. The tutorial steps and the race coach share it.
/// </summary>
public sealed class GymTutorialCard : MonoBehaviour
{
    public const float Width = 760f;
    public const float Height = 176f;
    private const float BadgeWidth = 96f;
    private const float SidePadding = 26f;
    private const float KeyPlateWidth = 158f;
    private const float KeyPlateHeight = 58f;

    private static readonly Color Sheet = new Color(0.03f, 0.036f, 0.058f, 0.97f);

    private RectTransform rect;
    private Text title;
    private Text line;
    private RectTransform keyPlate;
    private Text keyText;
    private Text badgeText;

    public RectTransform Rect => rect;
    public bool IsShown => gameObject.activeSelf;
    public string TitleText => title != null ? title.text : string.Empty;
    public string LineText => line != null ? line.text : string.Empty;
    public string KeyText => keyText != null ? keyText.text : string.Empty;
    public string BadgeText => badgeText != null ? badgeText.text : string.Empty;

    public static GymTutorialCard Create(Transform parent, string name)
    {
        GameObject root = new GameObject(name, typeof(RectTransform));
        root.transform.SetParent(parent, false);
        GymTutorialCard card = root.AddComponent<GymTutorialCard>();
        card.Build();
        return card;
    }

    private void Build()
    {
        rect = (RectTransform)transform;
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = new Vector2(0f, -30f);
        rect.sizeDelta = new Vector2(Width, Height);
        rect.localRotation = Quaternion.Euler(0f, 0f, -1.2f);
        rect.localScale = Vector3.one;

        // Same three layers as a Persona button: white focus slab, red plate edge,
        // navy sheet on top, all with the slanted corners of the menus.
        PersonaShape backing = PersonaMenuStyle.CreateShape("Focus Backing", transform, Color.white);
        Stretch(backing.rectTransform);
        backing.SetCorners(new Vector2(-0.02f, -0.06f), new Vector2(-0.018f, 0.05f),
            new Vector2(0.03f, 0.07f), new Vector2(0.024f, -0.05f));
        PersonaShape edge = PersonaMenuStyle.CreateShape("Sheet Accent Edge", transform,
            PersonaMenuStyle.Accent);
        Stretch(edge.rectTransform);
        edge.SetCorners(new Vector2(-0.008f, -0.03f), new Vector2(-0.01f, 0.02f),
            new Vector2(0.016f, 0.03f), new Vector2(0.01f, -0.025f));
        PersonaShape sheet = PersonaMenuStyle.CreateShape("Sheet Plate", transform, Sheet);
        Stretch(sheet.rectTransform);
        sheet.SetCorners(Vector2.zero, Vector2.zero, new Vector2(0.012f, 0f), Vector2.zero);

        BuildBadge();
        BuildKeyPlate();

        Font titleFont = PersonaMenuStyle.LoadTitleFont(PersonaMenuStyle.BuiltinFont);
        Font bodyFont = PersonaMenuStyle.LoadButtonFont(PersonaMenuStyle.BuiltinFont);

        float textLeft = BadgeWidth + 30f;
        title = Label("Title", titleFont, 34, PersonaMenuStyle.Accent, TextAnchor.UpperLeft);
        title.transform.SetParent(transform, false);
        Place(title.rectTransform, textLeft, 20f, KeyPlateWidth + SidePadding + 24f, 42f);
        title.horizontalOverflow = HorizontalWrapMode.Overflow;

        // Red section rule under the title, the same mark the settings page uses.
        PersonaShape rule = PersonaMenuStyle.CreateShape("Title Rule", transform,
            new Color(PersonaMenuStyle.Accent.r, PersonaMenuStyle.Accent.g,
                PersonaMenuStyle.Accent.b, 0.7f));
        RectTransform ruleRect = rule.rectTransform;
        ruleRect.anchorMin = new Vector2(0f, 1f);
        ruleRect.anchorMax = new Vector2(0f, 1f);
        ruleRect.pivot = new Vector2(0f, 0.5f);
        ruleRect.sizeDelta = new Vector2(150f, 4f);
        ruleRect.anchoredPosition = new Vector2(textLeft, -66f);
        rule.SetSlant(0.12f);

        line = Label("Line", bodyFont, 27, PersonaMenuStyle.Ink, TextAnchor.UpperLeft);
        line.transform.SetParent(transform, false);
        Place(line.rectTransform, textLeft, 74f, KeyPlateWidth + SidePadding + 24f, Height - 88f);
        line.horizontalOverflow = HorizontalWrapMode.Wrap;
        line.resizeTextForBestFit = true;
        line.resizeTextMinSize = 16;
        line.resizeTextMaxSize = 27;
    }

    private void BuildBadge()
    {
        GameObject badge = new GameObject("Lesson Number", typeof(RectTransform));
        badge.transform.SetParent(transform, false);
        RectTransform rectTransform = badge.GetComponent<RectTransform>();
        rectTransform.anchorMin = new Vector2(0f, 0f);
        rectTransform.anchorMax = new Vector2(0f, 1f);
        rectTransform.pivot = new Vector2(0f, 0.5f);
        rectTransform.sizeDelta = new Vector2(BadgeWidth, 0f);
        rectTransform.anchoredPosition = Vector2.zero;

        PersonaShape plate = PersonaMenuStyle.CreateShape("Badge Plate", badge.transform,
            PersonaMenuStyle.Accent);
        Stretch(plate.rectTransform);
        plate.SetCorners(Vector2.zero, new Vector2(0.06f, 0f), new Vector2(0.2f, 0f),
            new Vector2(0.12f, 0f));

        Font titleFont = PersonaMenuStyle.LoadTitleFont(PersonaMenuStyle.BuiltinFont);
        badgeText = Label("Number", titleFont, 54, Color.white, TextAnchor.MiddleCenter);
        badgeText.transform.SetParent(badge.transform, false);
        Stretch(badgeText.rectTransform);
        badgeText.text = "01";
        Shadow shadow = badgeText.GetComponent<Shadow>();
        if (shadow != null)
        {
            shadow.effectColor = new Color(0f, 0f, 0f, 0.85f);
            shadow.effectDistance = new Vector2(3f, -3f);
        }
    }

    private void BuildKeyPlate()
    {
        GameObject key = new GameObject("Key Plate", typeof(RectTransform));
        key.transform.SetParent(transform, false);
        keyPlate = key.GetComponent<RectTransform>();
        keyPlate.anchorMin = keyPlate.anchorMax = new Vector2(1f, 0.5f);
        keyPlate.pivot = new Vector2(1f, 0.5f);
        keyPlate.anchoredPosition = new Vector2(-SidePadding, 0f);
        keyPlate.sizeDelta = new Vector2(KeyPlateWidth, KeyPlateHeight);

        PersonaShape edge = PersonaMenuStyle.CreateShape("Key Edge", keyPlate, Color.white);
        Stretch(edge.rectTransform);
        edge.SetCorners(new Vector2(-0.04f, -0.1f), new Vector2(-0.05f, 0.1f),
            new Vector2(0.06f, 0.12f), new Vector2(0.05f, -0.1f));
        PersonaShape face = PersonaMenuStyle.CreateShape("Key Face", keyPlate,
            PersonaMenuStyle.Gold);
        Stretch(face.rectTransform);
        face.SetCorners(Vector2.zero, Vector2.zero, new Vector2(0.05f, 0f), Vector2.zero);

        Font bodyFont = PersonaMenuStyle.LoadButtonFont(PersonaMenuStyle.BuiltinFont);
        keyText = Label("Key", bodyFont, 30, new Color(0.03f, 0.035f, 0.05f), TextAnchor.MiddleCenter);
        keyText.transform.SetParent(keyPlate, false);
        keyText.rectTransform.anchorMin = Vector2.zero;
        keyText.rectTransform.anchorMax = Vector2.one;
        keyText.rectTransform.offsetMin = Vector2.zero;
        keyText.rectTransform.offsetMax = Vector2.zero;
        keyText.horizontalOverflow = HorizontalWrapMode.Overflow;
        keyText.resizeTextForBestFit = true;
        keyText.resizeTextMinSize = 14;
        keyText.resizeTextMaxSize = 30;
    }

    public void SetContent(string heading, string body, string key, string badge = null)
    {
        if (title == null)
        {
            return;
        }
        title.text = heading ?? string.Empty;
        line.text = body ?? string.Empty;
        if (badgeText != null && !string.IsNullOrEmpty(badge))
        {
            badgeText.text = badge;
        }
        bool hasKey = !string.IsNullOrEmpty(key);
        keyPlate.gameObject.SetActive(hasKey);
        if (keyText != null) keyText.text = hasKey ? key : string.Empty;
    }

    public void SetVisible(bool visible)
    {
        if (gameObject.activeSelf != visible)
        {
            gameObject.SetActive(visible);
        }
    }

    /// <summary>
    /// The card never spills off a narrow screen: it shrinks as one piece to
    /// the canvas width instead of clipping its own key slab.
    /// </summary>
    private void LateUpdate()
    {
        if (rect == null || !(rect.parent is RectTransform parent))
        {
            return;
        }
        float available = Mathf.Max(240f, parent.rect.width - 48f);
        float scale = Mathf.Min(1f, available / Width);
        rect.localScale = new Vector3(scale, scale, 1f);
    }

    private static Text Label(string name, Font font, int size, Color color, TextAnchor alignment)
    {
        GameObject textObject = new GameObject(name, typeof(RectTransform), typeof(Text));
        Text text = textObject.GetComponent<Text>();
        text.font = font;
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

    // Top-left placement inside the card: fixed insets on every side.
    private static void Place(RectTransform rect, float left, float top, float right, float height)
    {
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(left, -top);
        rect.sizeDelta = new Vector2(-(left + right), height);
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
