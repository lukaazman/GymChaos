using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Persona-style menu entry: a dark slanted plate with a red outline that
/// snaps into a larger red plate while hovered or selected. Hover also moves
/// the EventSystem selection, so mouse and keyboard/gamepad share one focus.
/// </summary>
public sealed class PersonaMenuButton : MonoBehaviour,
    IPointerEnterHandler, ISelectHandler, IDeselectHandler
{
    private const float FocusScale = 1.08f;
    private const float Sharpness = 18f;

    private PersonaShape face;
    private PersonaShape outline;
    private PersonaShape backing;
    private Text label;
    private Color idleFace;
    private Color focusFace;
    private Color idleOutline;
    private Color focusOutline;
    private float idleRotation;
    private float focus;
    private float pop;
    private bool focused;

    public bool IsFocusedForVerification => focused;
    public float CurrentScaleForVerification => transform.localScale.x;

    public void Configure(
        PersonaShape faceShape, PersonaShape outlineShape, PersonaShape backingShape,
        Text labelText, Color idleFaceColor, Color focusFaceColor,
        Color idleOutlineColor, Color focusOutlineColor)
    {
        face = faceShape;
        outline = outlineShape;
        backing = backingShape;
        label = labelText;
        idleFace = idleFaceColor;
        focusFace = focusFaceColor;
        idleOutline = idleOutlineColor;
        focusOutline = focusOutlineColor;
        idleRotation = transform.localEulerAngles.z;
        Apply();
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (EventSystem.current != null)
        {
            EventSystem.current.SetSelectedGameObject(gameObject);
        }
    }

    public void OnSelect(BaseEventData eventData)
    {
        if (!focused)
        {
            pop = 1f;
        }
        focused = true;
    }

    public void OnDeselect(BaseEventData eventData)
    {
        focused = false;
    }

    private void Update()
    {
        float step = 1f - Mathf.Exp(-Sharpness * Time.unscaledDeltaTime);
        focus = Mathf.Lerp(focus, focused ? 1f : 0f, step);
        pop = Mathf.MoveTowards(pop, 0f, Time.unscaledDeltaTime * 5f);
        Apply();
    }

    private void Apply()
    {
        // A short overshoot on focus gives the snappy Persona "punch".
        float overshoot = Mathf.Sin(pop * Mathf.PI) * 0.05f;
        float scale = Mathf.Lerp(1f, FocusScale, focus) + overshoot;
        transform.localScale = new Vector3(scale, scale, 1f);
        transform.localRotation = Quaternion.Euler(0f, 0f,
            Mathf.Lerp(idleRotation, idleRotation - 2.5f, focus));
        if (face != null) face.color = Color.Lerp(idleFace, focusFace, focus);
        if (outline != null) outline.color = Color.Lerp(idleOutline, focusOutline, focus);
        if (backing != null)
        {
            Color backingColor = backing.color;
            backingColor.a = focus;
            backing.color = backingColor;
        }
        if (label != null) label.color = Color.white;
    }
}
