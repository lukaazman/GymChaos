#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Graphics-only capture of the boot menu over the live gym view. Writes
/// Logs/agent/start-menu-idle.png and start-menu-hover-settings.png, and
/// checks that focusing a button scales it up.
/// </summary>
[InitializeOnLoad]
public static class GymChaosStartMenuCapture
{
    private const string RequestedKey = "GymChaos.StartMenuCaptureRequested";
    private static double started;
    private static int step;
    private static double stepStarted;
    private static int resultCode;
    private static Quaternion viewBeforePause;

    static GymChaosStartMenuCapture()
    {
        if (SessionState.GetBool(RequestedKey, false)) Hook();
    }

    public static void Run()
    {
        SessionState.SetBool(RequestedKey, true);
        step = 0;
        resultCode = 1;
        GymChaosVerifierExit.Record(resultCode);
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        Hook();
        EditorApplication.isPlaying = true;
    }

    private static void Hook()
    {
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
        EditorApplication.playModeStateChanged -= PlayModeChanged;
        EditorApplication.playModeStateChanged += PlayModeChanged;
    }

    private static void PlayModeChanged(PlayModeStateChange change)
    {
        if (change == PlayModeStateChange.EnteredPlayMode)
        {
            started = EditorApplication.timeSinceStartup;
            stepStarted = started;
            Application.runInBackground = true;
        }
        if (change != PlayModeStateChange.EnteredEditMode) return;
        EditorApplication.update -= Tick;
        EditorApplication.playModeStateChanged -= PlayModeChanged;
        SessionState.EraseBool(RequestedKey);
        if (Application.isBatchMode) GymChaosVerifierExit.Exit(resultCode);
    }

    private static void Tick()
    {
        if (!EditorApplication.isPlaying) return;
        double now = EditorApplication.timeSinceStartup;
        try
        {
            if (now - started > 90d) throw new TimeoutException("Start menu capture timed out.");
            // Let the exterior, city and daylight finish before capturing.
            if (now - started < 10d) return;
            if (step >= 2)
            {
                if (now - stepStarted < 0.6d) return;
                CapturePause(now);
                return;
            }
            GameObject menu = GameObject.Find("Gym Start Screen");
            if (menu == null)
            {
                // Batch boots skip the start screen; open it on demand.
                GymStartScreen.CreateForScene(
                    UnityEngine.Object.FindFirstObjectByType<GymArenaBootstrap>(),
                    UnityEngine.Object.FindFirstObjectByType<PlayerMovement>());
                stepStarted = now;
                return;
            }
            if (now - stepStarted < 0.6d) return;

            Button settings = menu.transform.Find("Options Button")?.GetComponent<Button>();
            Button play = menu.transform.Find("Play Button")?.GetComponent<Button>();
            if (settings == null || play == null)
                throw new InvalidOperationException("Start menu buttons are missing.");
            PersonaMenuButton playPersona = play.GetComponent<PersonaMenuButton>();
            PersonaMenuButton settingsPersona = settings.GetComponent<PersonaMenuButton>();
            if (step == 0)
            {
                Capture(menu, "start-menu-idle");
                if (!playPersona.IsFocusedForVerification || playPersona.CurrentScaleForVerification < 1.05f)
                    throw new InvalidOperationException(
                        $"Default focus button is not enlarged: scale={playPersona.CurrentScaleForVerification:F2}");
                EventSystem.current.SetSelectedGameObject(settings.gameObject);
                step = 1;
                stepStarted = now;
                return;
            }
            if (step >= 2)
            {
                CapturePause(now);
                return;
            }
            Capture(menu, "start-menu-hover-settings");
            bool hoverOk = settingsPersona.IsFocusedForVerification &&
                settingsPersona.CurrentScaleForVerification > 1.05f &&
                playPersona.CurrentScaleForVerification < 1.03f;
            if (!hoverOk)
                throw new InvalidOperationException(
                    $"Focus did not move: settings={settingsPersona.CurrentScaleForVerification:F2} " +
                    $"play={playPersona.CurrentScaleForVerification:F2}");
            bool hasHint = false;
            foreach (Text text in menu.GetComponentsInChildren<Text>(true))
            {
                if (text.text.Contains("WASD") || text.text.Contains("ESC")) hasHint = true;
            }
            if (hasHint) throw new InvalidOperationException("Key hint text is still on the start menu.");
            Debug.Log("GYMCHAOS_START_MENU_CAPTURE_OK focusScale=" +
                settingsPersona.CurrentScaleForVerification.ToString("F2"));
            // Pause menu shares the style; open it over the running game.
            UnityEngine.Object.Destroy(menu);
            step = 2;
            stepStarted = now;
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            resultCode = 1;
            GymChaosVerifierExit.Record(resultCode);
            EditorApplication.isPlaying = false;
        }
    }

    private static void CapturePause(double now)
    {
        if (!GymPauseMenu.IsVisible)
        {
            if (now - stepStarted > 5d) throw new InvalidOperationException("Pause menu did not open.");
            PlayerMovement pausePlayer = UnityEngine.Object.FindFirstObjectByType<PlayerMovement>();
            GymPauseMenu.CreateForScene(pausePlayer);
            viewBeforePause = pausePlayer.playerCamera.transform.rotation;
            GymPauseMenu.Open(pausePlayer);
            return;
        }
        GymPauseMenu pause = UnityEngine.Object.FindFirstObjectByType<GymPauseMenu>();
        Button options = pause.transform.Find("Pause Page/Pause Options Button")?.GetComponent<Button>();
        if (options == null || options.GetComponent<PersonaMenuButton>() == null)
            throw new InvalidOperationException("Pause buttons do not use the Persona style.");
        if (step == 2)
        {
            PersonaMenuButton resume = pause.transform.Find("Pause Page/Pause Play Button")
                ?.GetComponent<PersonaMenuButton>();
            if (resume == null || resume.CurrentScaleForVerification < 1.07f ||
                now - stepStarted < 1.5d)
            {
                if (now - stepStarted > 5d)
                    throw new InvalidOperationException("Resume did not take default focus.");
                return;
            }
            Capture(pause.gameObject, "pause-menu-idle");
            EventSystem.current.SetSelectedGameObject(options.gameObject);
            step = 3;
            stepStarted = now;
            return;
        }
        if (step >= 4)
        {
            CaptureSettings(pause, now);
            return;
        }
        Capture(pause.gameObject, "pause-menu-hover-settings");
        PersonaMenuButton persona = options.GetComponent<PersonaMenuButton>();
        if (!persona.IsFocusedForVerification || persona.CurrentScaleForVerification < 1.05f)
            throw new InvalidOperationException("Pause settings button did not react to focus.");
        foreach (Text text in pause.GetComponentsInChildren<Text>(true))
        {
            if (text.gameObject.activeInHierarchy && text.text.Contains("PAUSED"))
                throw new InvalidOperationException("Pause title is still shown.");
        }
        // Pause is a plain overlay: the gameplay view must not move.
        float viewDrift = Quaternion.Angle(viewBeforePause,
            UnityEngine.Object.FindFirstObjectByType<PlayerMovement>().playerCamera.transform.rotation);
        if (viewDrift > 0.5f)
            throw new InvalidOperationException($"Pause menu moved the gameplay view: {viewDrift:F1} deg.");
        Debug.Log("GYMCHAOS_PAUSE_MENU_CAPTURE_OK viewDrift=" + viewDrift.ToString("F2") + " focusScale=" +
            persona.CurrentScaleForVerification.ToString("F2"));
        AssertEvenFocusBorder();
        // Settings share the menu style: open them from the pause page.
        options.onClick.Invoke();
        step = 4;
        stepStarted = now;
    }

    // The white focus slab must clear the outline by the same margin on the
    // right as on the left (the right edge used to be hidden).
    private static void AssertEvenFocusBorder()
    {
        float leftTop = PersonaMenuStyle.OutlineTopLeft.x - PersonaMenuStyle.BackingTopLeft.x;
        float leftBottom = PersonaMenuStyle.OutlineBottomLeft.x - PersonaMenuStyle.BackingBottomLeft.x;
        float rightTop = PersonaMenuStyle.BackingTopRight.x - PersonaMenuStyle.OutlineTopRight.x;
        float rightBottom = PersonaMenuStyle.BackingBottomRight.x - PersonaMenuStyle.OutlineBottomRight.x;
        float faceRightTop = 1f + 0.05f;
        bool even = Mathf.Abs(leftTop - rightTop) < 0.006f &&
            Mathf.Abs(leftBottom - rightBottom) < 0.006f &&
            1f + PersonaMenuStyle.BackingTopRight.x > faceRightTop + 0.02f;
        if (!even)
            throw new InvalidOperationException(
                $"GYMCHAOS_MENU_FOCUS_BORDER_FAIL left={leftTop:F3}/{leftBottom:F3} " +
                $"right={rightTop:F3}/{rightBottom:F3}");
        Debug.Log($"GYMCHAOS_MENU_FOCUS_BORDER_OK left={leftTop:F3}/{leftBottom:F3} " +
            $"right={rightTop:F3}/{rightBottom:F3}");
    }

    private static void CaptureSettings(GymPauseMenu pause, double now)
    {
        Transform panel = pause.transform.Find("Options Panel");
        if (panel == null || !panel.gameObject.activeInHierarchy)
        {
            if (now - stepStarted > 5d) throw new InvalidOperationException("Settings did not open.");
            return;
        }
        Transform resolution = panel.Find("Resolution Dropdown");
        PersonaMenuButton resolutionPersona = resolution != null
            ? resolution.GetComponent<PersonaMenuButton>() : null;
        PersonaMenuButton backPersona = panel.Find("Options Back Button")
            ?.GetComponent<PersonaMenuButton>();
        if (resolutionPersona == null || backPersona == null)
            throw new InvalidOperationException("Settings controls do not use the Persona style.");
        if (step == 4)
        {
            if (now - stepStarted < 0.8d) return;
            int bebas = 0;
            int other = 0;
            foreach (Text text in panel.GetComponentsInChildren<Text>(true))
            {
                string fontName = text.font != null ? text.font.name : "none";
                if (fontName.Contains("Bebas") || fontName.Contains("Anton")) bebas++;
                else other++;
            }
            if (other > 0 || bebas == 0)
                throw new InvalidOperationException(
                    $"Settings text does not use the menu fonts: menuFonts={bebas} other={other}");
            Capture(pause.gameObject, "settings-menu-idle");
            EventSystem.current.SetSelectedGameObject(resolution.gameObject);
            step = 5;
            stepStarted = now;
            return;
        }
        if (now - stepStarted < 0.8d) return;
        Capture(pause.gameObject, "settings-menu-focus");
        if (!resolutionPersona.IsFocusedForVerification ||
            resolutionPersona.CurrentScaleForVerification < 1.02f)
            throw new InvalidOperationException(
                $"Settings row did not react to focus: scale={resolutionPersona.CurrentScaleForVerification:F2}");
        Debug.Log("GYMCHAOS_SETTINGS_MENU_STYLE_OK focusScale=" +
            resolutionPersona.CurrentScaleForVerification.ToString("F3"));
        resultCode = 0;
        GymChaosVerifierExit.Record(resultCode);
        EditorApplication.isPlaying = false;
    }

    private static void Capture(GameObject menu, string label)
    {
        Canvas canvas = menu.GetComponent<Canvas>();
        Camera source = Camera.main;
        PlayerMovement player = UnityEngine.Object.FindFirstObjectByType<PlayerMovement>();
        if (player != null && player.playerCamera != null) source = player.playerCamera;
        RenderTexture target = new RenderTexture(1920, 1080, 24);
        RenderTexture previousTarget = source.targetTexture;
        RenderMode previousMode = canvas.renderMode;
        canvas.renderMode = RenderMode.ScreenSpaceCamera;
        canvas.worldCamera = source;
        canvas.planeDistance = Mathf.Max(source.nearClipPlane + 0.05f, 0.3f);
        source.targetTexture = target;
        Canvas.ForceUpdateCanvases();
        float previousTimeScale = Time.timeScale;
        Time.timeScale = 1f;
        source.Render();
        Time.timeScale = previousTimeScale;
        RenderTexture.active = target;
        Texture2D image = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0);
        image.Apply();
        RenderTexture.active = null;
        source.targetTexture = previousTarget;
        canvas.renderMode = previousMode;
        string path = Path.Combine(Directory.GetParent(Application.dataPath).Parent.FullName,
            "Logs", "agent", label + ".png");
        File.WriteAllBytes(path, image.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(image);
        target.Release();
        UnityEngine.Object.DestroyImmediate(target);
    }
}
#endif
