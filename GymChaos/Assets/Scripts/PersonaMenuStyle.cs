using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Shared Persona-style menu look used by the start and pause menus: slanted
/// plates, Bebas Neue labels, red focus plate with a white slab and the
/// scale-up hover handled by <see cref="PersonaMenuButton"/>.
/// Positions use pixel coordinates of the 1672x941 start-menu art so both
/// menus place their buttons identically.
/// </summary>
public static class PersonaMenuStyle
{
    public const float ArtWidth = 1672f;
    public const float ArtHeight = 941f;
    // Persona red-orange from the reference title and primary button.
    public static readonly Color Accent = new Color(0.93f, 0.2f, 0.09f, 1f);
    public static readonly Color ButtonIdle = new Color(0.055f, 0.07f, 0.12f, 1f);

    // Plate corner offsets (fractions of the button rect). The white focus
    // backing sits 0.025 of the width outside the outline on both sides.
    public static readonly Vector2 OutlineBottomLeft = new Vector2(-0.015f, -0.09f);
    public static readonly Vector2 OutlineTopLeft = new Vector2(-0.02f, 0.08f);
    public static readonly Vector2 OutlineTopRight = new Vector2(0.075f, 0.1f);
    public static readonly Vector2 OutlineBottomRight = new Vector2(0.015f, -0.06f);
    public static readonly Vector2 BackingBottomLeft = new Vector2(-0.04f, -0.13f);
    public static readonly Vector2 BackingTopLeft = new Vector2(-0.045f, 0.12f);
    public static readonly Vector2 BackingTopRight = new Vector2(0.1f, 0.14f);
    public static readonly Vector2 BackingBottomRight = new Vector2(0.04f, -0.10f);

    // Shared tokens for overlays and the gameplay HUD.
    public static readonly Color Ink = new Color(0.97f, 0.97f, 0.95f, 1f);
    public static readonly Color PlateBlack = new Color(0.035f, 0.035f, 0.045f, 1f);
    public static readonly Color Veil = new Color(0.012f, 0.05f, 0.13f, 0.62f);
    public static readonly Color Navy = new Color(0.03f, 0.16f, 0.36f, 1f);
    public static readonly Color Muted = new Color(0.72f, 0.78f, 0.88f, 1f);
    public static readonly Color Gold = new Color(1f, 0.82f, 0.35f, 1f);
    public const float TransitionSeconds = 0.16f;

    public static Font BuiltinFont => Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

    /// <summary>Slanted plate with a contrasting rim, the base of every panel and meter.</summary>
    public static RectTransform CreatePanel(
        Transform parent, string name, float centerX, float centerY, float width, float height,
        float rotation, Color fill, Color rim)
    {
        GameObject root = new GameObject(name, typeof(RectTransform));
        root.transform.SetParent(parent, false);
        RectTransform rect = root.GetComponent<RectTransform>();
        PlaceInArt(rect, centerX, centerY, width, height);
        rect.localRotation = Quaternion.Euler(0f, 0f, rotation);
        PersonaShape edge = CreateShape("Rim", root.transform, rim);
        Stretch(edge.rectTransform);
        edge.SetCorners(new Vector2(-0.02f, -0.035f), new Vector2(-0.012f, 0.03f),
            new Vector2(0.03f, 0.045f), new Vector2(0.014f, -0.025f));
        PersonaShape plate = CreateShape("Plate", root.transform, fill);
        Stretch(plate.rectTransform);
        plate.SetCorners(Vector2.zero, new Vector2(0.006f, 0f), new Vector2(0.018f, 0f), Vector2.zero);
        plate.raycastTarget = true;
        return rect;
    }

    /// <summary>Real UI text in art pixel coordinates; never baked into artwork.</summary>
    public static Text CreateLabel(
        Transform parent, string name, Font font, string content, float centerX, float centerY,
        float width, float height, int fontSize, Color color, TextAnchor alignment)
    {
        GameObject textObject = new GameObject(name, typeof(RectTransform), typeof(Text));
        textObject.transform.SetParent(parent, false);
        Text text = textObject.GetComponent<Text>();
        PlaceInArt(text.rectTransform, centerX, centerY, width, height);
        text.font = font;
        text.text = content;
        text.fontSize = Mathf.RoundToInt(fontSize * 1080f / ArtHeight);
        text.color = color;
        text.alignment = alignment;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.raycastTarget = false;
        Shadow shadow = textObject.AddComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.75f);
        shadow.effectDistance = new Vector2(2f, -2f);
        return text;
    }

    public static Font LoadButtonFont(Font fallback)
    {
        Font font = Resources.Load<Font>("Fonts/BebasNeue-Regular");
        return font != null ? font : fallback;
    }

    public static Font LoadTitleFont(Font fallback)
    {
        Font font = Resources.Load<Font>("Fonts/Anton-Regular");
        return font != null ? font : fallback;
    }

    // Main menu stack, shared so the pause menu matches it exactly.
    public static Button CreatePrimaryButton(Transform parent, string name, Font font,
        string label, UnityEngine.Events.UnityAction onClick)
    {
        return CreateButton(parent, name, font, label, 262f, 616f, 440f, 96f, 84, -2f, onClick);
    }

    public static Button CreateSecondaryButton(Transform parent, string name, Font font,
        string label, UnityEngine.Events.UnityAction onClick)
    {
        return CreateButton(parent, name, font, label, 200f, 722f, 310f, 72f, 60, -1f, onClick);
    }

    public static Button CreateTertiaryButton(Transform parent, string name, Font font,
        string label, UnityEngine.Events.UnityAction onClick)
    {
        return CreateButton(parent, name, font, label, 176f, 812f, 262f, 70f, 58, -1f, onClick);
    }

    public static Button CreateButton(
        Transform parent, string name, Font font, string label, float centerX, float centerY,
        float width, float height, int fontSize, float rotation,
        UnityEngine.Events.UnityAction onClick)
    {
        GameObject root = new GameObject(name, typeof(RectTransform), typeof(Button));
        root.transform.SetParent(parent, false);
        RectTransform rect = root.GetComponent<RectTransform>();
        PlaceInArt(rect, centerX, centerY, width, height);
        rect.localRotation = Quaternion.Euler(0f, 0f, rotation);

        // Focus backing: a white slab offset behind the red plate. It clears
        // the outline by the same margin on the right as on the left, so the
        // white border reads evenly around the focused plate.
        PersonaShape backing = CreateShape("Focus Backing", root.transform, Color.white);
        Stretch(backing.rectTransform);
        backing.SetCorners(BackingBottomLeft, BackingTopLeft, BackingTopRight, BackingBottomRight);
        PersonaShape outline = CreateShape("Outline", root.transform, Accent);
        Stretch(outline.rectTransform);
        outline.SetCorners(OutlineBottomLeft, OutlineTopLeft, OutlineTopRight, OutlineBottomRight);
        PersonaShape face = CreateShape("Face", root.transform, ButtonIdle);
        Stretch(face.rectTransform);
        face.SetCorners(Vector2.zero, Vector2.zero, new Vector2(0.05f, 0f), Vector2.zero);
        face.raycastTarget = true;

        float scale = 1080f / ArtHeight;
        GameObject textObject = new GameObject("Label", typeof(RectTransform), typeof(Text));
        textObject.transform.SetParent(root.transform, false);
        Text text = textObject.GetComponent<Text>();
        Stretch(text.rectTransform);
        text.rectTransform.offsetMin = new Vector2(width * scale * 0.09f, 0f);
        text.font = font;
        text.text = label;
        text.fontSize = Mathf.RoundToInt(fontSize * scale);
        text.color = Color.white;
        text.alignment = TextAnchor.MiddleLeft;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.raycastTarget = false;
        Shadow shadow = textObject.AddComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.8f);
        shadow.effectDistance = new Vector2(3f, -3f);

        Button button = root.GetComponent<Button>();
        button.targetGraphic = face;
        button.transition = Selectable.Transition.None;
        if (onClick != null) button.onClick.AddListener(onClick);
        PersonaMenuButton persona = root.AddComponent<PersonaMenuButton>();
        persona.Configure(face, outline, backing, text, ButtonIdle, Accent, Accent, Color.black);
        return button;
    }

    // Places a rect using pixel coordinates of the reference art (origin
    // top-left); the canvas reference is 1920x1080, i.e. the art x1.148.
    public static void PlaceInArt(RectTransform rect, float centerX, float centerY,
        float width, float height)
    {
        Vector2 anchor = new Vector2(centerX / ArtWidth, 1f - centerY / ArtHeight);
        rect.anchorMin = anchor;
        rect.anchorMax = anchor;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        float scale = 1080f / ArtHeight;
        rect.sizeDelta = new Vector2(width * scale, height * scale);
        rect.localScale = Vector3.one;
    }

    /// <summary>
    /// Upright HUD-style plate on an existing rect: a border-coloured rim
    /// <paramref name="border"/> px outside the rect and the face inside it.
    /// Used where nothing reacts to the pointer (HUD, dialogue, locker panels).
    /// </summary>
    public static void AddUprightPlate(RectTransform rect, Color border, Color face, float borderWidth = 3f)
    {
        PersonaShape rim = CreateShape("Rim", rect, border);
        rim.rectTransform.anchorMin = Vector2.zero;
        rim.rectTransform.anchorMax = Vector2.one;
        rim.rectTransform.offsetMin = new Vector2(-borderWidth, -borderWidth);
        rim.rectTransform.offsetMax = new Vector2(borderWidth, borderWidth);
        PersonaShape plate = CreateShape("Face", rect, face);
        Stretch(plate.rectTransform);
    }

    public const float LayoutButtonBorder = 3f;
    public const float LayoutButtonPadding = 22f;

    /// <summary>
    /// Persona button for in-game panels (dialogue choices, locker options),
    /// placed in parent-rect coordinates (top-left origin, canvas units). It is
    /// an upright rectangle while idle and tilts only while hovered or focused;
    /// the face is inset exactly one border width inside the red outline, and
    /// the outline stays red on focus, so no dark band ever opens between the
    /// face and the border. Scale pop, press and white slab are the menu's.
    /// </summary>
    public static Button CreateLayoutButton(Transform parent, string name, Font font, string label,
        Vector2 topLeft, Vector2 size, int fontSize, UnityEngine.Events.UnityAction onClick)
    {
        GameObject root = new GameObject(name, typeof(RectTransform), typeof(Button));
        root.transform.SetParent(parent, false);
        RectTransform rect = root.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = size;
        rect.anchoredPosition = new Vector2(topLeft.x + size.x * 0.5f, -topLeft.y - size.y * 0.5f);

        PersonaShape backing = CreateShape("Focus Backing", root.transform, Color.white);
        Inset(backing.rectTransform, -7f, -6f, -9f, -6f);
        PersonaShape outline = CreateShape("Outline", root.transform, Accent);
        Stretch(outline.rectTransform);
        PersonaShape face = CreateShape("Face", root.transform, ButtonIdle);
        Inset(face.rectTransform, LayoutButtonBorder, LayoutButtonBorder, LayoutButtonBorder, LayoutButtonBorder);
        face.raycastTarget = true;

        GameObject textObject = new GameObject("Label", typeof(RectTransform), typeof(Text));
        textObject.transform.SetParent(root.transform, false);
        Text text = textObject.GetComponent<Text>();
        Inset(text.rectTransform, LayoutButtonPadding, 0f, LayoutButtonPadding, 0f);
        text.font = font;
        text.text = label;
        text.fontSize = fontSize;
        text.color = Color.white;
        text.alignment = TextAnchor.MiddleLeft;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.resizeTextForBestFit = true;
        text.resizeTextMinSize = Mathf.Max(12, fontSize / 2);
        text.resizeTextMaxSize = fontSize;
        text.raycastTarget = false;
        Shadow shadow = textObject.AddComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.8f);
        shadow.effectDistance = new Vector2(2f, -2f);

        Button button = root.GetComponent<Button>();
        button.targetGraphic = face;
        button.transition = Selectable.Transition.None;
        if (onClick != null) button.onClick.AddListener(onClick);
        PersonaMenuButton persona = root.AddComponent<PersonaMenuButton>();
        persona.Configure(face, outline, backing, text, ButtonIdle, Accent, Accent, Accent);
        persona.SetFocusMotion(1.06f, 3f);
        return button;
    }

    /// <summary>Width that fits the label at full size plus the side padding.</summary>
    public static float FitLayoutButtonWidth(Button button, float minimumWidth)
    {
        Text text = button.GetComponentInChildren<Text>();
        bool bestFit = text.resizeTextForBestFit;
        text.resizeTextForBestFit = false;
        float width = text.preferredWidth + LayoutButtonPadding * 2f + 6f;
        text.resizeTextForBestFit = bestFit;
        return Mathf.Max(minimumWidth, Mathf.Ceil(width));
    }

    /// <summary>Keeps a layout button's label on one line, shrinking it to the button width.</summary>
    public static void FitLabelToButton(Button button, int maximumSize)
    {
        Text text = button.GetComponentInChildren<Text>();
        RectTransform rect = (RectTransform)button.transform;
        text.resizeTextForBestFit = false;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.fontSize = maximumSize;
        float available = rect.rect.width - LayoutButtonPadding * 2f;
        float preferred = text.preferredWidth;
        if (preferred > available && preferred > 0f)
        {
            text.fontSize = Mathf.Max(12, Mathf.FloorToInt(maximumSize * available / preferred));
        }
    }

    private static void Inset(RectTransform rect, float left, float bottom, float right, float top)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(left, bottom);
        rect.offsetMax = new Vector2(-right, -top);
        rect.localScale = Vector3.one;
        rect.localRotation = Quaternion.identity;
    }

    /// <summary>Plain label in parent-rect coordinates (top-left origin, canvas units).</summary>
    public static Text CreateLayoutLabel(Transform parent, string name, Font font, string content,
        Vector2 topLeft, Vector2 size, int fontSize, Color color, TextAnchor alignment)
    {
        GameObject textObject = new GameObject(name, typeof(RectTransform), typeof(Text));
        textObject.transform.SetParent(parent, false);
        Text text = textObject.GetComponent<Text>();
        RectTransform rect = text.rectTransform;
        rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(topLeft.x, -topLeft.y);
        rect.sizeDelta = size;
        text.font = font;
        text.text = content;
        text.fontSize = fontSize;
        text.color = color;
        text.alignment = alignment;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.raycastTarget = false;
        Shadow shadow = textObject.AddComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.85f);
        shadow.effectDistance = new Vector2(2f, -2f);
        return text;
    }

    public static PersonaShape CreateShape(string name, Transform parent, Color color)
    {
        GameObject shapeObject = new GameObject(name, typeof(RectTransform), typeof(PersonaShape));
        shapeObject.transform.SetParent(parent, false);
        PersonaShape shape = shapeObject.GetComponent<PersonaShape>();
        shape.color = color;
        shape.raycastTarget = false;
        return shape;
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
