using UnityEngine;

/// <summary>
/// Keeps the director's existing dialogue panel intact while adding a small,
/// flat presentation marker for the cinematic letterbox. The rule responds to
/// node changes without taking ownership of choices or input routing.
/// </summary>
[DefaultExecutionOrder(-20)]
public sealed class GymDialogueUI : MonoBehaviour
{
    private const float PresentationDuration = 0.34f;
    private const float BeatDuration = 0.24f;

    private GUIStyle statusStyle;
    private GUIContent statusContent;
    private DialogueNode activeNode;
    private float presentationStartedAt;
    private float beatStartedAt = -1f;
    private bool wasActive;

    private void OnGUI()
    {
        if (Event.current == null || Event.current.type != EventType.Repaint)
        {
            return;
        }

        GymDialogueDirector director = GymDialogueDirector.Active;
        bool dialogueVisible = director != null && GymDialogueDirector.IsDialogueActive &&
            !GymStartScreen.IsMenuVisible && !GymPauseMenu.IsVisible;
        if (!dialogueVisible)
        {
            ResetPresentation();
            return;
        }

        DialogueNode node = director.CurrentNode;
        float now = Time.unscaledTime;
        if (!wasActive)
        {
            wasActive = true;
            presentationStartedAt = now;
            beatStartedAt = now;
            activeNode = node;
        }
        else if (node != activeNode)
        {
            activeNode = node;
            beatStartedAt = now;
        }

        float letterboxBlend = director.LetterboxBlend;
        float reveal = Ease01((now - presentationStartedAt) / PresentationDuration);
        float beat = beatStartedAt >= 0f
            ? 1f - Ease01((now - beatStartedAt) / BeatDuration)
            : 0f;
        float barHeight = Mathf.Lerp(
            0f, Mathf.Clamp(Screen.height * 0.1f, 34f, 72f), letterboxBlend);
        float margin = Mathf.Clamp(Screen.width * 0.025f, 18f, 36f);
        float lineWidth = Mathf.Lerp(26f, 56f, reveal) + beat * 14f;
        float opacity = Mathf.Clamp01(letterboxBlend) * 0.88f;

        EnsureStatusStyle();
        Color previousColor = GUI.color;
        GUI.color = new Color(0.72f, 0.78f, 0.9f, opacity * 0.82f);
        GUI.Label(
            new Rect(margin, Mathf.Max(6f, barHeight * 0.5f - 8f), 150f, 18f),
            statusContent,
            statusStyle);
        GUI.color = new Color(1f, 0.78f, 0.28f, opacity);
        GUI.DrawTexture(
            new Rect(margin, Mathf.Max(0f, barHeight - 3f), lineWidth, 2f),
            Texture2D.whiteTexture);
        GUI.color = previousColor;
    }

    private void EnsureStatusStyle()
    {
        if (statusStyle != null)
        {
            return;
        }

        statusStyle = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleLeft,
            fontStyle = FontStyle.Bold,
            fontSize = 11,
            padding = new RectOffset(0, 0, 0, 0)
        };
        statusStyle.normal.textColor = Color.white;
        statusContent = new GUIContent("MEMBER TALK");
    }

    private static float Ease01(float value)
    {
        value = Mathf.Clamp01(value);
        return value * value * (3f - 2f * value);
    }

    private void ResetPresentation()
    {
        wasActive = false;
        activeNode = null;
        beatStartedAt = -1f;
    }

    private void OnDisable()
    {
        ResetPresentation();
    }
}
