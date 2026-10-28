#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Weight selection menu (2026-10-23): opening a barbell or stack station shows
/// the Persona-style panel with one button per load; clicking a load selects
/// it, starts the exercise and closes the panel. With a graphics device it
/// writes a capture to Logs/agent/weight-menu.png.
/// </summary>
[InitializeOnLoad]
public static class GymChaosWeightMenuVerifier
{
    private const string Key = "GymChaos.WeightMenuVerificationRequested";
    private static bool launched;

    static GymChaosWeightMenuVerifier()
    {
        GymChaosVerifierHooks.Hook(Tick, OnPlayModeChanged);
    }

    [MenuItem("Tools/GymChaos/Run Weight Menu Verification")]
    public static void Run()
    {
        launched = false;
        GymChaosVerifierExit.Record(1);
        GymChaosVerifierSave.Neutralize();
        SessionState.SetBool(Key, true);
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        EditorApplication.isPlaying = true;
    }

    private static void OnPlayModeChanged(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredEditMode || !SessionState.GetBool(Key, false)) return;
        SessionState.EraseBool(Key);
        if (Application.isBatchMode) GymChaosVerifierExit.Exit(1);
    }

    private static void Tick()
    {
        if (!SessionState.GetBool(Key, false) || !EditorApplication.isPlaying || launched) return;
        PlayerMovement player = UnityEngine.Object.FindFirstObjectByType<PlayerMovement>();
        if (player == null || UnityEngine.Object.FindFirstObjectByType<GymExerciseStation>() == null) return;
        launched = true;
        new GameObject("Weight menu verifier").AddComponent<GymChaosWeightMenuVerifierRunner>();
    }

    public static void Finish(int code)
    {
        GymChaosVerifierExit.Record(code);
        EditorApplication.isPlaying = false;
    }
}

public sealed class GymChaosWeightMenuVerifierRunner : MonoBehaviour
{
    private IEnumerator Start()
    {
        yield return new WaitForSeconds(2f);
        IEnumerator body = Verify();
        while (true)
        {
            object current;
            try
            {
                if (!body.MoveNext()) break;
                current = body.Current;
            }
            catch (Exception exception)
            {
                Debug.LogError("GYMCHAOS_WEIGHT_MENU_FAIL " + exception.Message);
                GymChaosWeightMenuVerifier.Finish(1);
                yield break;
            }
            yield return current;
        }
        Debug.Log("GYMCHAOS_WEIGHT_MENU_OK");
        GymChaosWeightMenuVerifier.Finish(0);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private IEnumerator Verify()
    {
        PlayerMovement player = FindFirstObjectByType<PlayerMovement>();
        GymExerciseStation station = null;
        foreach (GymExerciseStation candidate in FindObjectsByType<GymExerciseStation>(FindObjectsSortMode.None))
        {
            if (candidate.RequiresWeightSelection && candidate.WeightOptions.Length > 0) station = candidate;
        }
        Require(station != null, "no station with weight selection");

        BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        MethodInfo open = typeof(PlayerMovement).GetMethod("OpenWeightSelection", flags);
        Require(open != null, "OpenWeightSelection missing");
        open.Invoke(player, new object[] { station });
        yield return null;
        yield return null;

        GymWeightMenuUI menu = FindFirstObjectByType<GymWeightMenuUI>();
        Require(menu != null, "menu not created");
        Canvas canvas = menu.GetComponent<Canvas>();
        Require(canvas.enabled, "menu hidden after open");
        Button[] buttons = menu.GetComponentsInChildren<Button>();
        int[] options = station.WeightOptions;
        Require(buttons.Length == options.Length, $"buttons={buttons.Length} options={options.Length}");
        for (int i = 0; i < options.Length; i++)
        {
            Text label = buttons[i].GetComponentInChildren<Text>();
            Require(label != null && label.text == options[i] + " KG", $"button {i} label={label?.text}");
        }

        yield return Capture(canvas);

        buttons[options.Length - 1].onClick.Invoke();
        yield return null;
        yield return null;
        Require(station.SelectedWeight == options[options.Length - 1],
            $"selected={station.SelectedWeight} expected={options[options.Length - 1]}");
        Require(!canvas.enabled, "menu still visible after selection");
    }

    // Renders the overlay canvas through a camera so a headless GPU run still
    // produces a picture of the panel.
    private static IEnumerator Capture(Canvas canvas)
    {
        if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) yield break;
        GameObject cameraObject = new GameObject("Weight menu capture camera");
        Camera camera = cameraObject.AddComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.04f, 0.1f, 0.2f, 1f);
        camera.cullingMask = ~0;
        RenderTexture target = new RenderTexture(1920, 1080, 24);
        camera.targetTexture = target;
        canvas.renderMode = RenderMode.ScreenSpaceCamera;
        canvas.worldCamera = camera;
        canvas.planeDistance = 1f;
        yield return new WaitForSecondsRealtime(0.5f);
        Canvas.ForceUpdateCanvases();
        camera.Render();
        RenderTexture previous = RenderTexture.active;
        RenderTexture.active = target;
        Texture2D picture = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
        picture.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0);
        picture.Apply();
        RenderTexture.active = previous;
        string directory = Path.Combine(Directory.GetCurrentDirectory(), "..", "Logs", "agent");
        Directory.CreateDirectory(directory);
        File.WriteAllBytes(Path.Combine(directory, "weight-menu.png"), picture.EncodeToPNG());
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.worldCamera = null;
        camera.targetTexture = null;
        Destroy(target);
        Destroy(cameraObject);
        Destroy(picture);
    }
}
#endif
