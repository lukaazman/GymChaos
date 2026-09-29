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
