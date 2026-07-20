using UnityEngine;

public sealed class ScreenSpaceCharacterLabel : MonoBehaviour
{
    private SkinnedMeshRenderer bodyRenderer;
    private Camera playerCamera;
    private string displayName;
    private float heightOffset;
    private GUIStyle style;

    public void Configure(
        SkinnedMeshRenderer targetRenderer, string label, float offset)
    {
        bodyRenderer = targetRenderer;
        displayName = label;
        heightOffset = offset;
        playerCamera = Camera.main;
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
    }
}
