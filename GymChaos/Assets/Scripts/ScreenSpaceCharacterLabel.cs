using UnityEngine;

public sealed class ScreenSpaceCharacterLabel : MonoBehaviour
{
    private const float NameRevealDistance = 7.5f;
    private const float NameRevealDistanceSqr = NameRevealDistance * NameRevealDistance;

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
        if (bodyRenderer == null || !bodyRenderer.enabled ||
            GymStartScreen.IsMenuVisible || GymDialogueDirector.IsDialogueActive ||
            GymPauseMenu.IsVisible)
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

        if (fighter == null)
        {
            fighter = GetComponentInParent<EnemyFighter>();
        }
        if (fighter == null)
        {
            return;
        }

        Bounds bounds = bodyRenderer.bounds;
        Vector3 screen = playerCamera.WorldToScreenPoint(
            new Vector3(bounds.center.x, bounds.max.y + heightOffset, bounds.center.z));
        if (screen.z <= 0f || screen.x < 0f || screen.x > Screen.width ||
            screen.y < 0f || screen.y > Screen.height)
        {
            return;
        }

        bool nearby = (fighter.transform.position - playerCamera.transform.position).sqrMagnitude <=
            NameRevealDistanceSqr;
        GymDialogueDirector director = GymDialogueDirector.Active;
        bool isDialogueTarget = director != null && director.Target == fighter;
        bool showName = nearby || fighter.IsAggressive || isDialogueTarget;
        bool showHealth = fighter.HasTakenDamage;
        // The anchor sits just above the head: the stack grows upward from
        // it (health bar, then name) so the name stays close to the head.
        float stackBottom = Screen.height - screen.y - AnchorGapPixels;
        if (showHealth)
        {
            DrawHealthBar(
                screen.x, stackBottom - HealthBarHeight,
                fighter.CurrentHealth / Mathf.Max(1f, fighter.MaxHealth),
                fighter.HealthBarColor);
            stackBottom -= HealthBarHeight + 2f;
        }

        if (showName)
        {
            if (style == null)
            {
                style = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.LowerCenter,
                    fontSize = 11,
                    padding = new RectOffset(0, 0, 0, 0),
                    normal = { textColor = Color.white }
                };
            }

            const float width = 180f;
            const float height = 16f;
            GUI.Label(new Rect(
                screen.x - width * 0.5f,
                stackBottom - height,
                width, height), displayName, style);
        }
    }

    private const float AnchorGapPixels = 1f;
    private const float HealthBarHeight = 9f;
    public static float NameHeightFactor => 0.035f;

    private static void DrawHealthBar(float centerX, float top, float health01, Color fillColor)
    {
        const float barWidth = 86f;
        Rect border = new Rect(
            centerX - barWidth * 0.5f,
            top,
            barWidth, HealthBarHeight);
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
