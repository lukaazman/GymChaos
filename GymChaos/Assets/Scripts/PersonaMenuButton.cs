using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Persona-style menu entry: a dark slanted plate with a red outline that
/// snaps into a larger red plate while hovered or focused. Hover also moves
/// the EventSystem selection, so mouse and keyboard/gamepad share one focus.
/// Selected (a committed choice such as the previewed class) is a separate,
/// persistent state: the plate stays red without the focus scale and slab.
/// </summary>
public sealed class PersonaMenuButton : MonoBehaviour,
    IPointerEnterHandler, IPointerDownHandler, IPointerUpHandler,
    ISelectHandler, IDeselectHandler
{
    private const float Sharpness = 18f;
    private const float DisabledAlpha = 0.42f;
    private float focusScale = 1.08f;
    private float focusTilt = 2.5f;

    private PersonaShape face;
    private PersonaShape outline;
    private PersonaShape backing;
    private Text label;
    private Selectable selectable;
    private CanvasGroup group;
    private Color idleFace;
    private Color focusFace;
    private Color idleOutline;
    private Color focusOutline;
    private float idleRotation;
    private float focus;
    private float selection;
    private float pop;
    private float press;
    private bool focused;
    private bool selected;
    private bool pressed;
    private bool settled;

    public event Action Focused;

    public bool IsFocusedForVerification => focused;
    public bool IsSelectedForVerification => selected;
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
        selectable = GetComponent<Selectable>();
        settled = false;
        Apply();
    }

    // Smaller controls (settings rows) use a gentler scale and tilt.
    public void SetFocusMotion(float scale, float tiltDegrees)
    {
        focusScale = Mathf.Max(1f, scale);
        focusTilt = tiltDegrees;
        settled = false;
        Apply();
    }

    // Picture cards keep the rim in the face colour while focused, so no dark
    // band opens between the card stroke and its content.
    public void SetFocusOutlineColor(Color color)
    {
        focusOutline = color;
        settled = false;
        Apply();
    }

    public Color CurrentOutlineColorForVerification => outline != null ? outline.color : Color.clear;
    public Color CurrentFaceColorForVerification => face != null ? face.color : Color.clear;

    public void SetSelected(bool value)
    {
        if (selected == value)
        {
            return;
        }
        selected = value;
        settled = false;
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (EventSystem.current != null && IsInteractable())
        {
            EventSystem.current.SetSelectedGameObject(gameObject);
        }
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        pressed = IsInteractable();
        settled = false;
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        pressed = false;
        settled = false;
    }

    public void OnSelect(BaseEventData eventData)
    {
        if (!focused)
        {
            pop = 1f;
        }
        focused = true;
        settled = false;
        Focused?.Invoke();
    }

    public void OnDeselect(BaseEventData eventData)
    {
        focused = false;
        pressed = false;
        settled = false;
    }

    private void OnDisable()
    {
        // A closed overlay must not leave a half-finished tween or stale focus.
        focused = false;
        pressed = false;
        focus = 0f;
        pop = 0f;
        press = 0f;
        selection = selected ? 1f : 0f;
        settled = false;
        Apply();
    }

    private bool IsInteractable()
    {
        return selectable == null || selectable.IsInteractable();
    }

    private void Update()
    {
        bool interactable = IsInteractable();
        float focusTarget = focused && interactable ? 1f : 0f;
        float selectionTarget = selected ? 1f : 0f;
        float pressTarget = pressed ? 1f : 0f;
        float alphaTarget = interactable ? 1f : DisabledAlpha;
        // Idle buttons stop touching their transform and colors entirely.
        if (settled && Mathf.Approximately(focus, focusTarget) &&
            Mathf.Approximately(selection, selectionTarget) &&
            Mathf.Approximately(press, pressTarget) && pop <= 0f &&
            (group == null ? interactable : Mathf.Approximately(group.alpha, alphaTarget)))
        {
            return;
        }

        float step = 1f - Mathf.Exp(-Sharpness * Time.unscaledDeltaTime);
        focus = Settle(focus, focusTarget, step);
        selection = Settle(selection, selectionTarget, step);
        press = Settle(press, pressTarget, 1f - Mathf.Exp(-40f * Time.unscaledDeltaTime));
        pop = Mathf.MoveTowards(pop, 0f, Time.unscaledDeltaTime * 5f);
        if (!interactable || group != null)
        {
            if (group == null)
            {
                group = gameObject.GetComponent<CanvasGroup>();
                if (group == null) group = gameObject.AddComponent<CanvasGroup>();
            }
            group.alpha = Settle(group.alpha, alphaTarget, step);
        }
        Apply();
        settled = true;
    }

    private static float Settle(float value, float target, float step)
    {
        float next = Mathf.Lerp(value, target, step);
        return Mathf.Abs(next - target) < 0.002f ? target : next;
    }

    private void Apply()
    {
        // A short overshoot on focus gives the snappy Persona "punch".
        float overshoot = Mathf.Sin(pop * Mathf.PI) * 0.05f;
        float scale = Mathf.Lerp(1f, focusScale, focus) +
            overshoot * (focusScale - 1f) / 0.08f - press * 0.045f;
        transform.localScale = new Vector3(scale, scale, 1f);
        transform.localRotation = Quaternion.Euler(0f, 0f,
            Mathf.Lerp(idleRotation, idleRotation - focusTilt, focus));
        float fill = Mathf.Max(focus, selection);
        if (face != null) face.color = Color.Lerp(idleFace, focusFace, fill);
        if (outline != null)
        {
            // Selected-but-unfocused keeps a white rim so it reads as chosen, not hovered.
            Color rim = Color.Lerp(idleOutline, focusOutline, focus);
            outline.color = Color.Lerp(rim, Color.white, selection * (1f - focus));
        }
        if (backing != null)
        {
            Color backingColor = backing.color;
            backingColor.a = focus;
            backing.color = backingColor;
        }
        if (label != null) label.color = Color.white;
    }
}
