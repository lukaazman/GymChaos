using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

/// <summary>
/// Constructs the runtime pause/options uGUI in a fresh editor process so the
/// menu path can be proven without depending on a headless Play Mode scene.
/// This verifier never edits scenes or assets and restores all global state it
/// may touch while GymRuntimeSettings builds the options panel.
/// </summary>
public static class GymChaosPauseOptionsVerifier
{
    private const string ResolutionWidthKey = "GymChaos.UI.ResolutionWidth.v1";
    private const string ResolutionHeightKey = "GymChaos.UI.ResolutionHeight.v1";
    private const string WindowModeKey = "GymChaos.UI.WindowMode.v1";
    private const string TextureQualityKey = "GymChaos.UI.TextureQuality.v1";
    private const string PostProcessingKey = "GymChaos.UI.PostProcessing.v1";
    private const string AntiAliasingKey = "GymChaos.UI.AntiAliasing.v1";
    private const string MasterVolumeKey = "GymChaos.UI.MasterVolume.v1";

    private static readonly string[] IntegerPreferenceKeys =
    {
        ResolutionWidthKey,
        ResolutionHeightKey,
        WindowModeKey,
        TextureQualityKey,
        PostProcessingKey,
        AntiAliasingKey
    };

    private sealed class CameraState
    {
        public UniversalAdditionalCameraData Camera;
        public bool RenderPostProcessing;
        public AntialiasingMode Antialiasing;
        public AntialiasingQuality AntialiasingQuality;
    }

    private sealed class Snapshot
    {
        public readonly bool[] IntegerPreferencePresent =
            new bool[IntegerPreferenceKeys.Length];
        public readonly int[] IntegerPreferenceValues =
            new int[IntegerPreferenceKeys.Length];
        public bool MasterVolumePresent;
        public float MasterVolume;
        public int ScreenWidth;
        public int ScreenHeight;
        public FullScreenMode ScreenMode;
        public bool HasDisplay;
        public int TextureMipmapLimit;
        public float AudioVolume;
        public float TimeScale;
        public CursorLockMode CursorLockState;
        public bool CursorVisible;
        public EventSystem[] ExistingEventSystems;
        public CameraState[] Cameras;
    }

    [MenuItem("Tools/GymChaos/Run Pause and Options UI Verification")]
    public static void Run()
    {
        Snapshot snapshot = CaptureSnapshot();
        GameObject menuRoot = null;
        int exitCode = 1;

        try
        {
            Require(GameObject.Find("Gym Pause Menu") == null,
                "A pre-existing Gym Pause Menu was found; run this verifier in a fresh process.");

            GymPauseMenu menu = GymPauseMenu.CreateForScene(null);
            Require(menu != null, "GymPauseMenu.CreateForScene returned null.");
            menuRoot = menu.gameObject;

            ValidateCanvas(menuRoot);
            ValidateEventSystem(snapshot);
            ValidatePauseControls(menuRoot);
            ValidateOptionsControls(menuRoot);
            ExerciseMenuPath(menuRoot);

            Debug.Log(
                "GYMCHAOS_PAUSE_OPTIONS_UI_OK " +
                "canvasScaler=1920x1080/0.5 controls=11 flow=play-options-back");
            exitCode = 0;
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            Debug.LogError("GYMCHAOS_PAUSE_OPTIONS_UI_FAIL " + exception.Message);
        }
        finally
        {
            try
            {
                CleanupCreatedObjects(menuRoot, snapshot);
            }
            finally
            {
                RestoreSnapshot(snapshot);
            }
        }

        if (Application.isBatchMode)
        {
            EditorApplication.Exit(exitCode);
        }
    }

    private static Snapshot CaptureSnapshot()
    {
        Snapshot snapshot = new Snapshot
        {
            ScreenWidth = Screen.width,
            ScreenHeight = Screen.height,
            ScreenMode = Screen.fullScreenMode,
            HasDisplay = Screen.width >= 320 && Screen.height >= 240,
            TextureMipmapLimit = QualitySettings.globalTextureMipmapLimit,
            AudioVolume = AudioListener.volume,
            TimeScale = Time.timeScale,
            CursorLockState = Cursor.lockState,
            CursorVisible = Cursor.visible,
            ExistingEventSystems = UnityEngine.Object.FindObjectsByType<EventSystem>(
                FindObjectsSortMode.None),
            Cameras = CaptureCameraStates()
        };

        for (int i = 0; i < IntegerPreferenceKeys.Length; i++)
        {
            string key = IntegerPreferenceKeys[i];
            snapshot.IntegerPreferencePresent[i] = PlayerPrefs.HasKey(key);
            snapshot.IntegerPreferenceValues[i] = PlayerPrefs.GetInt(key);
        }

        snapshot.MasterVolumePresent = PlayerPrefs.HasKey(MasterVolumeKey);
        snapshot.MasterVolume = PlayerPrefs.GetFloat(MasterVolumeKey);
        return snapshot;
    }

    private static CameraState[] CaptureCameraStates()
    {
        UniversalAdditionalCameraData[] cameras =
            UnityEngine.Object.FindObjectsByType<UniversalAdditionalCameraData>(
                FindObjectsSortMode.None);
        CameraState[] states = new CameraState[cameras.Length];
        for (int i = 0; i < cameras.Length; i++)
        {
            UniversalAdditionalCameraData camera = cameras[i];
            states[i] = new CameraState
            {
                Camera = camera,
                RenderPostProcessing = camera.renderPostProcessing,
                Antialiasing = camera.antialiasing,
                AntialiasingQuality = camera.antialiasingQuality
            };
        }

        return states;
    }

    private static void ValidateCanvas(GameObject menuRoot)
    {
        Canvas[] canvases = menuRoot.GetComponentsInChildren<Canvas>(true);
        Require(canvases.Length == 1,
            "Gym Pause Menu must contain exactly one Canvas; found " + canvases.Length + ".");

        Canvas canvas = menuRoot.GetComponent<Canvas>();
        Require(canvas != null && canvas.renderMode == RenderMode.ScreenSpaceOverlay,
            "Gym Pause Menu Canvas must be a screen-space overlay.");

        CanvasScaler scaler = menuRoot.GetComponent<CanvasScaler>();
        Require(scaler != null, "Gym Pause Menu is missing CanvasScaler.");
        Require(scaler.uiScaleMode == CanvasScaler.ScaleMode.ScaleWithScreenSize,
            "Gym Pause Menu CanvasScaler must use ScaleWithScreenSize.");
        Require(Mathf.Abs(scaler.referenceResolution.x - 1920f) < 0.01f &&
                Mathf.Abs(scaler.referenceResolution.y - 1080f) < 0.01f,
            "Gym Pause Menu CanvasScaler reference resolution must be 1920x1080.");
        Require(scaler.screenMatchMode == CanvasScaler.ScreenMatchMode.MatchWidthOrHeight &&
                Mathf.Abs(scaler.matchWidthOrHeight - 0.5f) < 0.001f,
            "Gym Pause Menu CanvasScaler must balance width and height at 0.5.");
        Require(menuRoot.GetComponentsInChildren<GraphicRaycaster>(true).Length == 1,
            "Gym Pause Menu must contain exactly one GraphicRaycaster.");
        Require(menuRoot.GetComponent<CanvasGroup>() != null,
            "Gym Pause Menu is missing CanvasGroup visibility/input state.");
    }

    private static void ValidateEventSystem(Snapshot snapshot)
    {
        EventSystem[] systems = UnityEngine.Object.FindObjectsByType<EventSystem>(
            FindObjectsSortMode.None);
        Require(systems.Length == 1,
            "The runtime UI requires exactly one EventSystem; found " + systems.Length + ".");
        Require(systems[0] != null && systems[0].gameObject.activeInHierarchy,
            "The runtime UI EventSystem must be active.");
        Require(snapshot.ExistingEventSystems != null,
            "EventSystem snapshot was not captured before UI construction.");
    }

    private static void ValidatePauseControls(GameObject menuRoot)
    {
        RequireButton(menuRoot, "Pause Play Button");
        RequireButton(menuRoot, "Pause Options Button");
        RequireButton(menuRoot, "Pause Exit Button");
        RequireNamedTransform(menuRoot, "Pause Page");
    }

    private static void ValidateOptionsControls(GameObject menuRoot)
    {
        RequireNamedTransform(menuRoot, "Options Panel");
        RequireDropdown(menuRoot, "Resolution Dropdown");
        RequireDropdown(menuRoot, "Window Mode Dropdown");
        RequireDropdown(menuRoot, "Texture Quality Dropdown");
        RequireToggle(menuRoot, "Post FX Toggle");
        RequireDropdown(menuRoot, "Anti Aliasing Dropdown");
        RequireSlider(menuRoot, "Master Volume Slider");
        RequireButton(menuRoot, "Options Back Button");
    }

    private static void ExerciseMenuPath(GameObject menuRoot)
    {
        GameObject pausePage = RequireNamedTransform(menuRoot, "Pause Page").gameObject;
        GameObject optionsPanel = RequireNamedTransform(menuRoot, "Options Panel").gameObject;
        Button playButton = RequireButton(menuRoot, "Pause Play Button");
        Button optionsButton = RequireButton(menuRoot, "Pause Options Button");
        Button backButton = RequireButton(menuRoot, "Options Back Button");

        Require(!pausePage.activeSelf && !optionsPanel.activeSelf,
            "Pause and options pages must start hidden after construction.");

        optionsButton.onClick.Invoke();
        Require(optionsPanel.activeSelf && !pausePage.activeSelf,
            "Pause Options button did not activate Options Panel exclusively.");

        backButton.onClick.Invoke();
        Require(pausePage.activeSelf && !optionsPanel.activeSelf,
            "Options Back button did not return to Pause Page.");

        playButton.onClick.Invoke();
        Require(!GymPauseMenu.IsVisible && !pausePage.activeSelf && !optionsPanel.activeSelf,
            "Pause Play button did not resume and hide the menu.");
    }

    private static Button RequireButton(GameObject root, string name)
    {
        return RequireComponent<Button>(root, name);
    }

    private static Dropdown RequireDropdown(GameObject root, string name)
    {
        return RequireComponent<Dropdown>(root, name);
    }

    private static Toggle RequireToggle(GameObject root, string name)
    {
        return RequireComponent<Toggle>(root, name);
    }

    private static Slider RequireSlider(GameObject root, string name)
    {
        return RequireComponent<Slider>(root, name);
    }

    private static T RequireComponent<T>(GameObject root, string name)
        where T : Component
    {
        Transform target = RequireNamedTransform(root, name);
        T component = target.GetComponent<T>();
        Require(component != null,
            name + " is missing required " + typeof(T).Name + " component.");
        return component;
    }

    private static Transform RequireNamedTransform(GameObject root, string name)
    {
        Transform[] children = root.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < children.Length; i++)
        {
            if (string.Equals(children[i].name, name, StringComparison.Ordinal))
            {
                return children[i];
            }
        }

        throw new InvalidOperationException(
            "Gym Pause Menu is missing required object: " + name + ".");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void CleanupCreatedObjects(GameObject menuRoot, Snapshot snapshot)
    {
        if (menuRoot != null)
        {
            UnityEngine.Object.DestroyImmediate(menuRoot);
        }

        HashSet<EventSystem> existingEventSystems = new HashSet<EventSystem>();
        if (snapshot.ExistingEventSystems != null)
        {
            for (int i = 0; i < snapshot.ExistingEventSystems.Length; i++)
            {
                EventSystem system = snapshot.ExistingEventSystems[i];
                if (system != null)
                {
                    existingEventSystems.Add(system);
                }
            }
        }

        EventSystem[] systems = UnityEngine.Object.FindObjectsByType<EventSystem>(
            FindObjectsSortMode.None);
        for (int i = 0; i < systems.Length; i++)
        {
            EventSystem system = systems[i];
            if (system != null && !existingEventSystems.Contains(system))
            {
                UnityEngine.Object.DestroyImmediate(system.gameObject);
            }
        }
    }

    private static void RestoreSnapshot(Snapshot snapshot)
    {
        if (snapshot == null)
        {
            return;
        }

        for (int i = 0; i < IntegerPreferenceKeys.Length; i++)
        {
            string key = IntegerPreferenceKeys[i];
            if (snapshot.IntegerPreferencePresent[i])
            {
                PlayerPrefs.SetInt(key, snapshot.IntegerPreferenceValues[i]);
            }
            else
            {
                PlayerPrefs.DeleteKey(key);
            }
        }

        if (snapshot.MasterVolumePresent)
        {
            PlayerPrefs.SetFloat(MasterVolumeKey, snapshot.MasterVolume);
        }
        else
        {
            PlayerPrefs.DeleteKey(MasterVolumeKey);
        }

        PlayerPrefs.Save();
        QualitySettings.globalTextureMipmapLimit = snapshot.TextureMipmapLimit;
        AudioListener.volume = snapshot.AudioVolume;
        Time.timeScale = snapshot.TimeScale;
        Cursor.lockState = snapshot.CursorLockState;
        Cursor.visible = snapshot.CursorVisible;

        if (snapshot.HasDisplay)
        {
            Screen.SetResolution(snapshot.ScreenWidth, snapshot.ScreenHeight,
                snapshot.ScreenMode);
        }

        if (snapshot.Cameras != null)
        {
            for (int i = 0; i < snapshot.Cameras.Length; i++)
            {
                CameraState state = snapshot.Cameras[i];
                if (state.Camera == null)
                {
                    continue;
                }

                state.Camera.renderPostProcessing = state.RenderPostProcessing;
                state.Camera.antialiasing = state.Antialiasing;
                state.Camera.antialiasingQuality = state.AntialiasingQuality;
            }
        }
    }
}
