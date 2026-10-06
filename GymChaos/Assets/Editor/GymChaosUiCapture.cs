using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// Graphics verifier runs only: saves the world camera image with the given
/// screen-space overlay canvases drawn on top into Logs/agent/&lt;file&gt;.
/// Overlay canvases never reach Camera.Render, so the world is rendered first,
/// then the canvases alone (moved to the UI layer, screen-space-camera on a
/// UI-only camera cleared to transparent), and the two are composited.
/// </summary>
public static class GymChaosUiCapture
{
    private const int Width = 1920;
    private const int Height = 1080;
    private const int UiLayer = 5;

    public static string Capture(Camera camera, string fileName, params Canvas[] canvases)
    {
        if (camera == null || SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
        {
            return string.Empty;
        }

        Color[] world = Render(camera);

        GameObject host = new GameObject("UI Capture Camera");
        Camera uiCamera = host.AddComponent<Camera>();
        uiCamera.transform.SetPositionAndRotation(new Vector3(0f, -5000f, 0f), Quaternion.identity);
        uiCamera.clearFlags = CameraClearFlags.SolidColor;
        uiCamera.backgroundColor = new Color(0f, 0f, 0f, 0f);
        uiCamera.cullingMask = 1 << UiLayer;
        uiCamera.nearClipPlane = 0.01f;
        uiCamera.farClipPlane = 10f;

        var modes = new RenderMode[canvases.Length];
        var cameras = new Camera[canvases.Length];
        var distances = new float[canvases.Length];
        var layers = new Dictionary<Transform, int>();
        for (int i = 0; i < canvases.Length; i++)
        {
            if (canvases[i] == null) continue;
            modes[i] = canvases[i].renderMode;
            cameras[i] = canvases[i].worldCamera;
            distances[i] = canvases[i].planeDistance;
            foreach (Transform child in canvases[i].GetComponentsInChildren<Transform>(true))
            {
                layers[child] = child.gameObject.layer;
                child.gameObject.layer = UiLayer;
            }
            canvases[i].renderMode = RenderMode.ScreenSpaceCamera;
            canvases[i].worldCamera = uiCamera;
            // Higher sorting order sits closer to the camera.
            canvases[i].planeDistance = 1f + (200 - canvases[i].sortingOrder) * 0.01f;
        }
        Canvas.ForceUpdateCanvases();
        Color[] ui = Render(uiCamera);

        for (int i = 0; i < canvases.Length; i++)
        {
            if (canvases[i] == null) continue;
            canvases[i].renderMode = modes[i];
            canvases[i].worldCamera = cameras[i];
            canvases[i].planeDistance = distances[i];
        }
        foreach (KeyValuePair<Transform, int> entry in layers)
        {
            if (entry.Key != null) entry.Key.gameObject.layer = entry.Value;
        }
        Object.DestroyImmediate(host);

        for (int i = 0; i < world.Length; i++)
        {
            float alpha = Mathf.Clamp01(ui[i].a);
            world[i] = new Color(
                ui[i].r + world[i].r * (1f - alpha),
                ui[i].g + world[i].g * (1f - alpha),
                ui[i].b + world[i].b * (1f - alpha), 1f);
        }
        Texture2D image = new Texture2D(Width, Height, TextureFormat.RGB24, false);
        image.SetPixels(world);
        image.Apply();
        string path = Path.Combine(Directory.GetParent(Application.dataPath).Parent.FullName,
            "Logs", "agent", fileName);
        File.WriteAllBytes(path, image.EncodeToPNG());
        Object.DestroyImmediate(image);
        Debug.Log($"GYMCHAOS_UI_CAPTURE path={path}");
        return path;
    }

    private static Color[] Render(Camera camera)
    {
        RenderTexture previousTarget = camera.targetTexture;
        RenderTexture previousActive = RenderTexture.active;
        RenderTexture target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32);
        camera.targetTexture = target;
        camera.Render();
        RenderTexture.active = target;
        Texture2D image = new Texture2D(Width, Height, TextureFormat.RGBA32, false);
        image.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
        image.Apply();
        RenderTexture.active = previousActive;
        camera.targetTexture = previousTarget;
        Color[] pixels = image.GetPixels();
        Object.DestroyImmediate(image);
        target.Release();
        Object.DestroyImmediate(target);
        return pixels;
    }
}
