using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The screen half of a tutorial step, drawn with the same uGUI canvas, type and
/// palette as the gameplay HUD: one card with the title, the line and the key.
/// Where the goal is, the floor mark says. An arrow pinned to the screen edge
/// also carried a distance that read wrong and, once you were close, pointed at
/// nothing useful, so the HUD no longer draws anything but the card.
/// </summary>
public sealed class GymTutorialHud : MonoBehaviour
{
    private GymTutorialDirector director;
    private CanvasGroup group;
    private GymTutorialCard card;
    private bool built;

    public bool CardVisible => card != null && card.IsShown;
    public bool CardBuilt => built;

    public void Bind(GymTutorialDirector source)
    {
        director = source;
    }

    private void LateUpdate()
    {
        GymTutorialStep step = director != null && director.Active ? director.Current : null;
        if (step == null)
        {
            if (built)
            {
                card.SetVisible(false);
            }
            return;
        }
        EnsureBuilt();
        card.SetContent(step.Title, step.Line, step.Key, director.Step.ToString("00"));
        card.SetVisible(true);
    }

    private void EnsureBuilt()
    {
        if (built)
        {
            return;
        }
        built = true;
        // Its own canvas object: the floor marker shares this host, and the world
        // markers must not live inside a screen canvas hierarchy.
        GameObject canvasObject = new GameObject("Tutorial HUD Canvas", typeof(RectTransform));
        canvasObject.transform.SetParent(transform, false);
        Canvas canvas = canvasObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        // Above the HUD (50) and the character labels (40), below the menus.
        canvas.sortingOrder = 55;
        CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
        GymRuntimeSettings.ConfigureBalancedCanvasScaler(scaler);
        scaler.matchWidthOrHeight = 1f;
        group = canvasObject.AddComponent<CanvasGroup>();
        group.interactable = false;
        group.blocksRaycasts = false;
        card = GymTutorialCard.Create((RectTransform)canvasObject.transform, "Lesson Card");
        card.SetVisible(false);
    }
}
