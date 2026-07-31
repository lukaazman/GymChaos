using UnityEngine;

public sealed class ScreenSpaceCharacterLabel : MonoBehaviour
{
    private SkinnedMeshRenderer bodyRenderer;
    private Camera playerCamera;
    private string displayName;
    private float heightOffset;
    private GUIStyle style;
    private EnemyFighter fighter;

    public void Configure(
        SkinnedMeshRenderer targetRenderer, string label, float offset)
    {
        bodyRenderer = targetRenderer;
        displayName = label;
        heightOffset = offset;
        playerCamera = Camera.main;
        fighter = GetComponentInParent<EnemyFighter>();
    }

    private void OnGUI()
    {
        if (bodyRenderer == null)
        {
            return;
        }

        if (playerCamera == null)
        {
            playerCamera = Camera.main;
        }
        if (playerCamera == null)
        {
            return;
        }

        Bounds bounds = bodyRenderer.bounds;
        Vector3 screen = playerCamera.WorldToScreenPoint(
            new Vector3(bounds.center.x, bounds.max.y + heightOffset, bounds.center.z));
        if (screen.z <= 0f)
        {
            return;
        }

        if (style == null)
        {
            style = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 15,
                normal = { textColor = Color.white }
            };
        }

        const float width = 180f;
        const float height = 24f;
        GUI.Label(new Rect(
            screen.x - width * 0.5f,
            Screen.height - screen.y - height * 0.5f,
            width, height), displayName, style);

        if (fighter == null)
        {
            fighter = GetComponentInParent<EnemyFighter>();
        }
        if (fighter != null && fighter.HasTakenDamage)
        {
            DrawHealthBar(
                screen, fighter.CurrentHealth / Mathf.Max(1f, fighter.MaxHealth),
                fighter.HealthBarColor);
        }
    }

    private static void DrawHealthBar(Vector3 screen, float health01, Color fillColor)
    {
        const float barWidth = 86f;
        const float barHeight = 9f;
        Rect border = new Rect(
            screen.x - barWidth * 0.5f,
            Screen.height - screen.y + 11f,
            barWidth, barHeight);
        Rect background = new Rect(border.x + 1f, border.y + 1f, border.width - 2f, border.height - 2f);
        Rect fill = new Rect(
            background.x, background.y,
            background.width * Mathf.Clamp01(health01), background.height);

        Color previous = GUI.color;
        GUI.color = Color.black;
        GUI.DrawTexture(border, Texture2D.whiteTexture);
        GUI.color = new Color(0.18f, 0.04f, 0.04f, 1f);
        GUI.DrawTexture(background, Texture2D.whiteTexture);
        if (fill.width > 0f)
        {
            GUI.color = fillColor;
            GUI.DrawTexture(fill, Texture2D.whiteTexture);
        }
        GUI.color = previous;
    }
}
