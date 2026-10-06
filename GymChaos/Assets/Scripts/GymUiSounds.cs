using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Interface sounds for every menu without touching each menu: a scanner
/// attaches <see cref="GymUiSoundHook"/> to every live uGUI Selectable
/// (start screen, settings, pause, session/class/save menus, dialogue
/// choices, locker outfits, dropdown items). IMGUI choice buttons call
/// <see cref="ImGuiButton"/> instead of GUI.Button.
/// </summary>
public sealed class GymUiSounds : MonoBehaviour
{
    private const float ScanInterval = 0.2f;
    private static GymUiSounds instance;
    private Selectable[] buffer = new Selectable[64];
    private float nextScan;
    private static readonly Dictionary<string, bool> ImGuiHover =
        new Dictionary<string, bool>();

    public static int HookCountForVerification { get; private set; }

    public static void EnsureCreated()
    {
        if (instance != null)
        {
            return;
        }

        instance = FindAnyObjectByType<GymUiSounds>();
        if (instance == null)
        {
            GameObject host = new GameObject("Gym UI Sounds (Runtime)");
            DontDestroyOnLoad(host);
            instance = host.AddComponent<GymUiSounds>();
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        EnsureCreated();
    }

    public static void ScanNow()
    {
        EnsureCreated();
        instance.Scan();
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(this);
            return;
        }
        instance = this;
    }

    private void OnDestroy()
    {
        if (instance == this)
        {
            instance = null;
        }
    }

    private void Update()
    {
        // Unscaled: menus run while the game is paused (timeScale 0).
        if (Time.unscaledTime < nextScan)
        {
            return;
        }
        nextScan = Time.unscaledTime + ScanInterval;
        Scan();
    }

    private void Scan()
    {
        int count = Selectable.allSelectableCount;
        if (count > buffer.Length)
        {
            buffer = new Selectable[Mathf.NextPowerOfTwo(count)];
        }
        count = Selectable.AllSelectablesNoAlloc(buffer);
        for (int i = 0; i < count; i++)
        {
            Selectable selectable = buffer[i];
            if (selectable != null && selectable.GetComponent<GymUiSoundHook>() == null)
            {
                selectable.gameObject.AddComponent<GymUiSoundHook>();
                HookCountForVerification++;
            }
            buffer[i] = null;
        }
    }

    /// <summary>
    /// True when a menu-navigation key moved focus this frame. Some menus
    /// (session/class) drive selection themselves rather than through
    /// uGUI navigation, so their OnSelect carries no AxisEventData.
    /// </summary>
    public static bool NavigationKeyThisFrame()
    {
#if ENABLE_INPUT_SYSTEM
        UnityEngine.InputSystem.Keyboard keyboard = UnityEngine.InputSystem.Keyboard.current;
        if (keyboard != null && (keyboard.upArrowKey.wasPressedThisFrame ||
            keyboard.downArrowKey.wasPressedThisFrame || keyboard.leftArrowKey.wasPressedThisFrame ||
            keyboard.rightArrowKey.wasPressedThisFrame || keyboard.tabKey.wasPressedThisFrame ||
            keyboard.wKey.wasPressedThisFrame || keyboard.sKey.wasPressedThisFrame ||
            keyboard.aKey.wasPressedThisFrame || keyboard.dKey.wasPressedThisFrame))
        {
            return true;
        }
        UnityEngine.InputSystem.Gamepad gamepad = UnityEngine.InputSystem.Gamepad.current;
        return gamepad != null && (gamepad.dpad.up.wasPressedThisFrame ||
            gamepad.dpad.down.wasPressedThisFrame || gamepad.dpad.left.wasPressedThisFrame ||
            gamepad.dpad.right.wasPressedThisFrame);
#else
        return Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.DownArrow) ||
            Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.RightArrow) ||
            Input.GetKeyDown(KeyCode.Tab);
#endif
    }

    /// <summary>GUI.Button with hover and click sounds.</summary>
    public static bool ImGuiButton(Rect rect, string label, GUIStyle style)
    {
        Event current = Event.current;
        if (current != null && current.type == EventType.Repaint)
        {
            string key = label + "@" + (int)rect.x + "," + (int)rect.y;
            bool hovered = rect.Contains(current.mousePosition);
            ImGuiHover.TryGetValue(key, out bool wasHovered);
            if (hovered && !wasHovered)
            {
                GymAudio.Play2D(GymSoundEffect.UiHover, 0.35f);
            }
            ImGuiHover[key] = hovered;
            if (ImGuiHover.Count > 256)
            {
                ImGuiHover.Clear();
            }
        }

        bool clicked = GUI.Button(rect, label, style);
        if (clicked)
        {
            GymAudio.Play2D(GymSoundEffect.UiConfirm, 0.6f);
        }
        return clicked;
    }

    /// <summary>Picks the click cue from what the control does.</summary>
    public static GymSoundEffect ResolveClickEffect(Selectable selectable)
    {
        if (selectable is Slider || selectable is Scrollbar)
        {
            return GymSoundEffect.None;
        }
        if (selectable is Toggle || selectable is Dropdown)
        {
            return GymSoundEffect.UiClick;
        }

        string text = selectable.name;
        Text label = selectable.GetComponentInChildren<Text>(true);
        if (label != null)
        {
            text += " " + label.text;
        }
        text = text.ToUpperInvariant();
        if (ContainsAny(text, "BACK", "CLOSE", "CANCEL", "EXIT", "QUIT", "RESUME", "LEAVE", "NO"))
        {
            return GymSoundEffect.UiBack;
        }
        if (ContainsAny(text, "CONFIRM", "START", "ENTER", "SELECT", "EQUIP", "PLAY",
            "NEW", "LOAD", "CONTINUE", "APPLY", "SAVE", "CHOOSE", "YES", "ACCEPT", "WEAR"))
        {
            return GymSoundEffect.UiConfirm;
        }
        return GymSoundEffect.UiClick;
    }

    private static bool ContainsAny(string value, params string[] terms)
    {
        for (int i = 0; i < terms.Length; i++)
        {
            int index = value.IndexOf(terms[i], System.StringComparison.Ordinal);
            while (index >= 0)
            {
                // Whole-word match, so "NO" does not fire inside "NOTE".
                bool startOk = index == 0 || !char.IsLetter(value[index - 1]);
                int end = index + terms[i].Length;
                bool endOk = end >= value.Length || !char.IsLetter(value[end]);
                if (startOk && endOk)
                {
                    return true;
                }
                index = value.IndexOf(terms[i], index + 1, System.StringComparison.Ordinal);
            }
        }
        return false;
    }
}

/// <summary>
/// Per-control sound hook: hover (pointer or keyboard/gamepad navigation),
/// press, refused press on a disabled control, and slider steps.
/// </summary>
[DisallowMultipleComponent]
public sealed class GymUiSoundHook : MonoBehaviour,
    IPointerEnterHandler, ISelectHandler, IPointerClickHandler, ISubmitHandler
{
    private const float HoverRepeatGuard = 0.08f;
    private Selectable selectable;
    private float lastHover = -1f;
    private float lastSliderTick = -1f;

    private void Awake()
    {
        selectable = GetComponent<Selectable>();
        Slider slider = selectable as Slider;
        if (slider != null)
        {
            slider.onValueChanged.AddListener(OnSliderChanged);
        }
    }

    private bool Interactable =>
        selectable != null && selectable.IsInteractable() && selectable.isActiveAndEnabled;

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (Interactable)
        {
            Hover();
        }
    }

    public void OnSelect(BaseEventData eventData)
    {
        // Only navigation moves (arrows, gamepad) sound here; a menu that
        // pre-selects its first entry when it opens stays quiet, and a mouse
        // hover is already covered by OnPointerEnter.
        if ((eventData is AxisEventData || GymUiSounds.NavigationKeyThisFrame()) &&
            Interactable)
        {
            Hover();
        }
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button == PointerEventData.InputButton.Left)
        {
            Press();
        }
    }

    public void OnSubmit(BaseEventData eventData)
    {
        Press();
    }

    private void Hover()
    {
        float now = Time.unscaledTime;
        if (now - lastHover < HoverRepeatGuard)
        {
            return;
        }
        lastHover = now;
        GymAudio.Play2D(GymSoundEffect.UiHover, 0.4f);
    }

    private void Press()
    {
        if (selectable == null)
        {
            return;
        }
        if (!Interactable)
        {
            GymAudio.Play2D(GymSoundEffect.UiError, 0.45f);
            return;
        }
        GymSoundEffect effect = GymUiSounds.ResolveClickEffect(selectable);
        GymAudio.Play2D(effect, effect == GymSoundEffect.UiConfirm ? 0.65f : 0.6f);
    }

    private void OnSliderChanged(float value)
    {
        // Only the player's own drag or arrow steps tick; code that loads a
        // saved value into the slider stays quiet.
        EventSystem events = EventSystem.current;
        if (events == null || events.currentSelectedGameObject != gameObject)
        {
            return;
        }
        float now = Time.unscaledTime;
        if (now - lastSliderTick < 0.07f)
        {
            return;
        }
        lastSliderTick = now;
        GymAudio.Play2D(GymSoundEffect.UiHover, 0.3f);
    }
}
