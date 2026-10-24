#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Settings dropdowns in real Play Mode (2026-10-24): the pause menu's options
/// page must be hit by the pointer raycast, a click on each dropdown must open
/// a laid-out list in front of everything, and clicking a row must change the
/// value, fire onValueChanged and close the list. The pause timeScale of 0 is
/// kept on, as in the game.
/// </summary>
[InitializeOnLoad]
public static class GymChaosDropdownVerifier
{
    private const string Key = "GymChaos.DropdownVerificationRequested";
    private static bool launched;

    static GymChaosDropdownVerifier()
    {
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
        EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
    }

    [MenuItem("Tools/GymChaos/Run Dropdown Verification")]
    public static void Run()
    {
        launched = false;
        GymChaosVerifierExit.Record(1);
        GymChaosVerifierSave.Neutralize();
        string directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            "GymChaosDropdownVerify-" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(directory);
        Environment.SetEnvironmentVariable(GymSessionService.VerificationDirectoryVariable, directory);
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
        if (UnityEngine.Object.FindFirstObjectByType<PlayerMovement>() == null || !GymStartScreen.IsMenuVisible) return;
        launched = true;
        new GameObject("Dropdown verifier").AddComponent<GymChaosDropdownVerifierRunner>();
    }

    public static void Finish(int code)
    {
        GymChaosVerifierExit.Record(code);
        Environment.SetEnvironmentVariable(GymSessionService.VerificationDirectoryVariable, null);
        EditorApplication.isPlaying = false;
    }
}

public sealed class GymChaosDropdownVerifierRunner : MonoBehaviour
{
    private IEnumerator Start()
    {
        yield return new WaitForSecondsRealtime(2f);
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
                Debug.LogError("GYMCHAOS_DROPDOWN_FAIL " + exception.Message);
                GymChaosDropdownVerifier.Finish(1);
                yield break;
            }
            yield return current;
        }
        Debug.Log("GYMCHAOS_DROPDOWN_OK");
        GymChaosDropdownVerifier.Finish(0);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static IEnumerator Frames(int count)
    {
        for (int i = 0; i < count; i++) yield return null;
    }

    private IEnumerator Verify()
    {
        // Start screen (menu visible at boot), then the in-game pause menu.
        Require(GymStartScreen.IsMenuVisible, "start screen not visible at boot");
        GymStartScreen start = FindFirstObjectByType<GymStartScreen>();
        Require(start != null, "GymStartScreen missing");
        Find<Button>(start.gameObject, "Options Button").onClick.Invoke();
        yield return Frames(3);
        yield return ExerciseDropdowns(start.gameObject, "start");
        Find<Button>(start.gameObject, "Options Back Button").onClick.Invoke();
        yield return Frames(2);

        GymStartScreen.PlayButton.onClick.Invoke();
        GymSessionMenu menu = GymStartScreen.SessionMenu;
        Require(menu != null, "no session menu");
        float until = Time.realtimeSinceStartup + 20f;
        while (menu.Current != GymSessionMenu.State.SessionChoice && Time.realtimeSinceStartup < until)
            yield return null;
        until = Time.realtimeSinceStartup + 20f;
        while (menu.Current != GymSessionMenu.State.ClassSelect && Time.realtimeSinceStartup < until)
        {
            menu.ChooseNewGame();
            yield return null;
        }
        until = Time.realtimeSinceStartup + 20f;
        while (menu.Current == GymSessionMenu.State.ClassSelect && Time.realtimeSinceStartup < until)
        {
            menu.ConfirmClass();
            yield return null;
        }
        until = Time.realtimeSinceStartup + 60f;
        while (!GymArenaBootstrap.IsGameplayStarted && Time.realtimeSinceStartup < until) yield return null;
        Require(GymArenaBootstrap.IsGameplayStarted, "gameplay did not start");
        yield return new WaitForSecondsRealtime(1f);
        PlayerMovement player = FindFirstObjectByType<PlayerMovement>();
        GymPauseMenu.Open(player);
        yield return Frames(3);
        Require(GymPauseMenu.IsVisible, "pause menu did not open (startVisible=" +
            GymStartScreen.IsMenuVisible + ")");
        GameObject menuRoot = GameObject.Find("Gym Pause Menu");
        Require(menuRoot != null, "Gym Pause Menu missing");
        Find<Button>(menuRoot, "Pause Options Button").onClick.Invoke();
        yield return Frames(3);
        yield return ExerciseDropdowns(menuRoot, "pause");
        Time.timeScale = 1f;
    }

    private IEnumerator ExerciseDropdowns(GameObject menuRoot, string label)
    {
        string[] names =
        {
            "Resolution Dropdown", "Window Mode Dropdown",
            "Texture Quality Dropdown", "Anti Aliasing Dropdown"
        };
        for (int n = 0; n < names.Length; n++)
        {
            Dropdown dropdown = Find<Dropdown>(menuRoot, names[n]);
            Require(dropdown.IsInteractable(), names[n] + " not interactable");
            RectTransform rect = (RectTransform)dropdown.transform;
            Vector3[] corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            Vector2 center = (corners[0] + corners[2]) * 0.5f;

            // Pointer raycast: the dropdown (or a child) must be the top hit.
            PointerEventData pointer = new PointerEventData(EventSystem.current) { position = center };
            List<RaycastResult> hits = new List<RaycastResult>();
            EventSystem.current.RaycastAll(pointer, hits);
            Require(hits.Count > 0, names[n] + ": raycast hit nothing at " + center);
            Require(hits[0].gameObject.transform.IsChildOf(dropdown.transform),
                names[n] + ": top raycast hit is " + hits[0].gameObject.name + ", not the dropdown");

            // Real click path through the event system.
            pointer.button = PointerEventData.InputButton.Left;
            pointer.pointerPress = dropdown.gameObject;
            ExecuteEvents.Execute(dropdown.gameObject, pointer, ExecuteEvents.pointerClickHandler);
            yield return Frames(2);
            yield return new WaitForSecondsRealtime(0.4f);
            Transform list = dropdown.transform.Find("Dropdown List");
            Require(list != null, names[n] + ": click did not open Dropdown List");

            RectTransform listRect = (RectTransform)list;
            Require(listRect.rect.width > 10f && listRect.rect.height > 10f,
                $"{names[n]}: list has no size {listRect.rect}");
            if (n == 0 || n == 2) yield return Capture(menuRoot.GetComponent<Canvas>(), label + "-" + n);
            Toggle[] rows = list.GetComponentsInChildren<Toggle>(false);
            Require(rows.Length == dropdown.options.Count,
                $"{names[n]}: rows={rows.Length} options={dropdown.options.Count}");
            if (rows.Length <= 4)
            {
                Require(listRect.rect.height >= rows.Length * 44f - 0.5f,
                    $"{names[n]}: list height {listRect.rect.height} hides rows of {rows.Length}x44");
            }
            CanvasGroup group = list.GetComponent<CanvasGroup>();
            Require(group != null && group.alpha > 0.99f, names[n] + ": list never became opaque");

            // The list must be on screen and its rows hit by the pointer.
            listRect.GetWorldCorners(corners);
            Require(corners[0].y >= -1f && corners[2].y <= Screen.height + 1f &&
                    corners[0].x >= -1f && corners[2].x <= Screen.width + 1f,
                $"{names[n]}: list off screen {corners[0]} {corners[2]} screen={Screen.width}x{Screen.height}");
            RectTransform firstRow = (RectTransform)rows[0].transform;
            firstRow.GetWorldCorners(corners);
            pointer.position = (corners[0] + corners[2]) * 0.5f;
            hits.Clear();
            EventSystem.current.RaycastAll(pointer, hits);
            Require(hits.Count > 0 && hits[0].gameObject.transform.IsChildOf(list),
                names[n] + ": first row is not the top raycast hit (" +
                (hits.Count > 0 ? hits[0].gameObject.name : "none") + ") row=" + rows[0].name +
                " rowCenter=" + pointer.position + " rowRect=" + firstRow.rect + " rowPos=" +
                firstRow.anchoredPosition + " listRect=" + listRect.rect + " itemImgRaycast=" +
                rows[0].GetComponent<Image>().raycastTarget + " active=" + rows[0].gameObject.activeInHierarchy);

            if (names[n] == "Texture Quality Dropdown" || names[n] == "Anti Aliasing Dropdown")
            {
                int before = dropdown.value;
                int target = before == 0 ? 1 : 0;
                int fired = -1;
                dropdown.onValueChanged.AddListener(v => fired = v);
                rows[target].isOn = true;
                yield return Frames(2);
                Require(dropdown.value == target && fired == target,
                    $"{names[n]}: row {target} gave value={dropdown.value} fired={fired}");
                // restore
                dropdown.value = before;
            }
            else
            {
                dropdown.Hide();
            }
            // Hide fades for 0.15 s of unscaled time and then destroys the list.
            yield return new WaitForSecondsRealtime(0.4f);
            Require(dropdown.transform.Find("Dropdown List") == null,
                names[n] + ": list did not close");
        }

    }

    private static IEnumerator Capture(Canvas canvas, string name)
    {
        if (canvas == null || SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) yield break;
        GameObject cameraObject = new GameObject("Dropdown capture camera");
        Camera camera = cameraObject.AddComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.04f, 0.1f, 0.2f, 1f);
        RenderTexture target = new RenderTexture(1920, 1080, 24);
        camera.targetTexture = target;
        canvas.renderMode = RenderMode.ScreenSpaceCamera;
        canvas.worldCamera = camera;
        canvas.planeDistance = 1f;
        yield return new WaitForSecondsRealtime(0.3f);
        Canvas.ForceUpdateCanvases();
        camera.Render();
        RenderTexture previous = RenderTexture.active;
        RenderTexture.active = target;
        Texture2D picture = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
        picture.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0);
        picture.Apply();
        RenderTexture.active = previous;
        string directory = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "..", "Logs", "agent");
        System.IO.Directory.CreateDirectory(directory);
        System.IO.File.WriteAllBytes(System.IO.Path.Combine(directory, "dropdown-" + name + ".png"), picture.EncodeToPNG());
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.worldCamera = null;
        camera.targetTexture = null;
        Destroy(target);
        Destroy(cameraObject);
        Destroy(picture);
    }

    private static T Find<T>(GameObject root, string name) where T : Component
    {
        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
        {
            if (child.name == name)
            {
                T component = child.GetComponent<T>();
                if (component != null) return component;
            }
        }
        throw new InvalidOperationException(name + " not found");
    }
}
#endif
