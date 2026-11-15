using UnityEngine;

/// <summary>
/// The screen half of a tutorial step: one card with the title, the line and
/// the key, the distance to the target, and an edge arrow when the target is
/// behind the camera. Drawn in the game's own colours, above the world HUD.
/// </summary>
public sealed class GymTutorialHud : MonoBehaviour
{
    private GymTutorialDirector director;
    private GUIStyle titleStyle;
    private GUIStyle lineStyle;
    private GUIStyle keyStyle;
    private GUIStyle smallStyle;
    private GUIStyle targetTitleStyle;
    private GUIStyle keyChipStyle;
    private Texture2D cardTexture;
    private Texture2D chipTexture;

    public bool EdgeArrowShown { get; private set; }
    public string LastTitle { get; private set; }
    public string LastKey { get; private set; }

    public void Bind(GymTutorialDirector source)
    {
        director = source;
    }

    private void OnGUI()
    {
        if (director == null || !director.Active || director.Current == null)
        {
            EdgeArrowShown = false;
            return;
        }
        EnsureStyles();
        GymTutorialStep step = director.Current;
        LastTitle = step.Title;
        LastKey = step.Key;

        float width = 620f;
        float x = (Screen.width - width) * 0.5f;
        float y = 26f;
        GUI.DrawTexture(new Rect(x, y, width, 96f), cardTexture);
        GUI.Label(new Rect(x + 22f, y + 8f, width - 44f, 34f), step.Title, titleStyle);
        GUI.Label(new Rect(x + 22f, y + 40f, width - 190f, 28f), step.Line, lineStyle);
        GUI.DrawTexture(new Rect(x + width - 160f, y + 36f, 138f, 40f), chipTexture);
        GUI.Label(new Rect(x + width - 160f, y + 36f, 138f, 40f), step.Key, keyStyle);

        if (director.HasTarget)
        {
            DrawTarget(director.TargetPoint, step);
        }
    }

    private void DrawTarget(Vector3 point, GymTutorialStep step)
    {
        Camera camera = Camera.main;
        if (camera == null)
        {
            return;
        }
        float distance = Vector3.Distance(director.PlayerPosition, point);
        Vector3 viewport = camera.WorldToViewportPoint(point);
        bool behind = viewport.z < 0f;
        bool offScreen = behind || viewport.x < 0.05f || viewport.x > 0.95f ||
            viewport.y < 0.05f || viewport.y > 0.95f;

        if (!offScreen && distance > 1.5f)
        {
            float sx = viewport.x * Screen.width;
            float sy = (1f - viewport.y) * Screen.height;
            // The label sits on the target itself: title, then the key in a box.
            GUI.Label(new Rect(sx - 140f, sy - 92f, 280f, 24f), step.Title, targetTitleStyle);
            GUI.DrawTexture(new Rect(sx - 26f, sy - 64f, 52f, 24f), chipTexture);
            GUI.Label(new Rect(sx - 26f, sy - 64f, 52f, 24f), step.Key, keyChipStyle);
            GUI.Label(new Rect(sx - 90f, sy + 8f, 180f, 24f),
                Mathf.RoundToInt(distance) + " m", smallStyle);
        }

        EdgeArrowShown = offScreen;
        if (!offScreen)
        {
            return;
        }
        Vector3 screen = new Vector3(viewport.x * Screen.width, (1f - viewport.y) * Screen.height, 0f);
        if (behind)
        {
            screen.x = Screen.width - screen.x;
            screen.y = Screen.height - screen.y;
        }
        Vector3 centre = new Vector3(Screen.width * 0.5f, Screen.height * 0.5f, 0f);
        Vector3 direction = screen - centre;
        if (direction.sqrMagnitude < 1f)
        {
            direction = Vector3.up;
        }
        direction.Normalize();
        float radius = Mathf.Min(Screen.width, Screen.height) * 0.36f;
        Vector3 spot = centre + direction * radius;
        spot.x = Mathf.Clamp(spot.x, 40f, Screen.width - 40f);
        spot.y = Mathf.Clamp(spot.y, 40f, Screen.height - 40f);
        float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;

        GUIUtility.RotateAroundPivot(angle, spot);
        GUI.DrawTexture(new Rect(spot.x - 15f, spot.y - 15f, 30f, 30f), chipTexture);
        GUIUtility.RotateAroundPivot(-angle, spot);
        GUI.Label(new Rect(spot.x - 70f, spot.y + 18f, 140f, 26f),
            Mathf.RoundToInt(distance) + " m", smallStyle);
    }

    private void OnDestroy()
    {
        if (cardTexture != null) Destroy(cardTexture);
        if (chipTexture != null) Destroy(chipTexture);
    }

    private void EnsureStyles()
    {
        if (titleStyle != null)
        {
            return;
        }
        titleStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 22,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleLeft
        };
        titleStyle.normal.textColor = PersonaMenuStyle.Gold;
        lineStyle = new GUIStyle(GUI.skin.label) { fontSize = 17, alignment = TextAnchor.MiddleLeft };
        lineStyle.normal.textColor = PersonaMenuStyle.Ink;
        keyStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 18,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter
        };
        keyStyle.normal.textColor = new Color(0.03f, 0.05f, 0.1f);
        smallStyle = new GUIStyle(GUI.skin.label) { fontSize = 15, alignment = TextAnchor.MiddleCenter };
        smallStyle.normal.textColor = PersonaMenuStyle.Gold;
        targetTitleStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 16,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter
        };
        targetTitleStyle.normal.textColor = PersonaMenuStyle.Gold;
        keyChipStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 14,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter
        };
        keyChipStyle.normal.textColor = new Color(0.03f, 0.05f, 0.1f);

        cardTexture = Solid(new Color(0.02f, 0.04f, 0.09f, 0.82f));
        chipTexture = Solid(PersonaMenuStyle.Gold);
    }

    private static Texture2D Solid(Color color)
    {
        Texture2D texture = new Texture2D(1, 1);
        texture.SetPixel(0, 0, color);
        texture.Apply();
        return texture;
    }
}
