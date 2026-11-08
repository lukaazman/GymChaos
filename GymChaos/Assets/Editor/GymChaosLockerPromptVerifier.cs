#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Locker room prompts: in front of any locker only the locker prompt, at a
/// bench only "Get ready", in front of the big changing-room mirror only the
/// outfit prompt, and where two reach the same spot the nearer target wins.
/// The mirror prompt opens the outfit menu, and a hovered item button never
/// reaches the colour swatches.
/// </summary>
[InitializeOnLoad]
public static class GymChaosLockerPromptVerifier
{
    private const string RequestedKey = "GymChaos.LockerPromptVerificationRequested";
    private const string LockerName = "Changing Locker";
    private const string MirrorName = "Locker Mirror Outfit Point";
    // Peak hover pose of a layout button: focus scale 1.06 plus the 0.05
    // overshoot share, and the 3 degree focus tilt (PersonaMenuStyle).
    private const float HoverPeakScale = 1.06f + 0.05f * 0.06f / 0.08f;
    private const float HoverTilt = 3f;
    private const float MinSwatchGap = 4f;
    private static double startedAt;
    private static bool finished;
    private static int resultCode;
    private static int phase;
    private static readonly List<string> passed = new List<string>();

    static GymChaosLockerPromptVerifier()
    {
        if (!SessionState.GetBool(RequestedKey, false)) return;
        Hook();
    }

    [MenuItem("Tools/GymChaos/Run Locker Prompt Verification")]
    public static void Run()
    {
        finished = false;
        phase = 0;
        passed.Clear();
        resultCode = 1;
        GymChaosVerifierExit.Record(resultCode);
        SessionState.SetBool(RequestedKey, true);
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        Hook();
        EditorApplication.isPlaying = true;
    }

    private static void Hook() => GymChaosVerifierHooks.Hook(Tick, OnPlayModeStateChanged);

    private static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            startedAt = Time.timeAsDouble;
            AudioListener.pause = true;
            return;
        }
        if (state != PlayModeStateChange.EnteredEditMode) return;
        EditorApplication.update -= Tick;
        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        SessionState.EraseBool(RequestedKey);
        if (Application.isBatchMode) GymChaosVerifierExit.Exit(resultCode);
    }

    private static void Tick()
    {
        if (finished || !EditorApplication.isPlaying) return;
        double now = Time.timeAsDouble;
        try
        {
            if (now - startedAt > 90d) throw new TimeoutException($"timed out in phase {phase}");
            PlayerMovement player = UnityEngine.Object.FindAnyObjectByType<PlayerMovement>();
            GymExperienceService service = GymExperienceService.Active;
            if (!GymBackRoomBuilder.TryGetRoomBounds(out _) || player == null || service == null) return;

            if (phase == 0)
            {
                if (now - startedAt < 4d) return;
                CheckPrompts(service);
                OpenFromMirror(player, service);
                startedAt = now;
                phase = 1;
            }
            else if (now - startedAt > 0.5d)
            {
                CheckSwatchSpacing();
                service.CloseLockerMenuFromUi();
                Finish(0);
            }
        }
        catch (Exception exception)
        {
            Debug.LogError("GYMCHAOS_LOCKER_PROMPT_FAIL " + exception.Message);
            Finish(1);
        }
    }

    private static void CheckPrompts(GymExperienceService service)
    {
        GymBackRoomInteractable locker = Require(LockerName);
        GymBackRoomInteractable mirror = Require(MirrorName);
        GymBackRoomInteractable[] benches =
        {
            Require("Locker Bench Prep Point Left"), Require("Locker Bench Prep Point Right")
        };
        if (mirror.InteractionType != GymBackRoomInteractionType.Locker ||
            mirror.DisplayName == locker.DisplayName)
            throw new InvalidOperationException($"mirror prompt '{mirror.DisplayName}' is not its own outfit prompt");

        // In front of every locker cabinet, including both ends of the bank.
        int lockers = 0;
        Bounds lockerArea = Area(locker);
        foreach (Transform node in locker.transform.parent.GetComponentsInChildren<Transform>())
        {
            if (node.name != "Locker cabinet") continue;
            Vector3 front = new Vector3(lockerArea.min.x + 0.45f, lockerArea.center.y, node.position.z);
            Expect(service, front, locker, "in front of locker");
            lockers++;
        }
        if (lockers < 7) throw new InvalidOperationException($"only {lockers} locker cabinets");

        // On and beside each bench.
        foreach (GymBackRoomInteractable bench in benches)
        {
            Bounds area = Area(bench);
            Expect(service, area.center, bench, "on bench");
            Expect(service, area.center + Vector3.forward * (area.extents.z + 0.5f), bench, "beside bench +z");
            Expect(service, area.center + Vector3.back * (area.extents.z + 0.5f), bench, "beside bench -z");
        }

        // In front of the mirror, centre and both sides.
        Bounds mirrorArea = Area(mirror);
        for (int i = -1; i <= 1; i++)
            Expect(service, mirrorArea.center + Vector3.right * (i * (mirrorArea.extents.x - 0.2f)), mirror, "mirror");

        // Overlap zones: between the lockers and a bench, and between a bench
        // and the mirror, the nearer target wins on each side of the midpoint.
        foreach (GymBackRoomInteractable bench in benches)
        {
            Bounds area = Area(bench);
            float gapMid = (lockerArea.max.x + area.min.x) * 0.5f;
            Vector3 row = new Vector3(0f, area.center.y, area.center.z);
            Expect(service, row + Vector3.right * (gapMid - 0.3f), locker, "locker side of gap");
            Expect(service, row + Vector3.right * (gapMid + 0.3f), bench, "bench side of gap");
        }
        Bounds left = Area(benches[0]);
        float x = Mathf.Min(left.max.x, mirrorArea.min.x) + 0.1f;
        float zMid = (left.min.z + mirrorArea.max.z) * 0.5f;
        Expect(service, new Vector3(x, left.center.y, zMid - 0.3f), mirror, "mirror side of bench gap");
        Expect(service, new Vector3(x, left.center.y, zMid + 0.3f), benches[0], "bench side of mirror gap");

        passed.Add($"lockers={lockers} benches=2 mirror=3 overlaps=6");
        Debug.Log($"GYMCHAOS_LOCKER_PROMPT_RANGE_OK lockers={lockers} benches=2 mirror=3 overlaps=6 " +
            $"mirrorPrompt=\"{mirror.DisplayName}\"");
    }

    private static void OpenFromMirror(PlayerMovement player, GymExperienceService service)
    {
        GymBackRoomInteractable mirror = Require(MirrorName);
        Vector3 standAt = mirror.transform.position;
        CharacterController controller = player.GetComponent<CharacterController>();
        bool controllerEnabled = controller != null && controller.enabled;
        if (controller != null) controller.enabled = false;
        player.transform.position = standAt;
        if (controller != null) controller.enabled = controllerEnabled;
        if (service.FindNearbyInteractable(player.transform.position, 3.1f) != mirror)
            throw new InvalidOperationException("player at the mirror does not get the mirror prompt");
        if (!GymExperienceService.TryHandlePlayerInteraction(player, true) || !service.IsLockerMenuOpen)
            throw new InvalidOperationException("mirror prompt did not open the outfit menu");
        passed.Add("mirrorOpensOutfit=1");
        Debug.Log("GYMCHAOS_LOCKER_MIRROR_OUTFIT_OK");
    }

    private static void CheckSwatchSpacing()
    {
        GymLockerMenuUI ui = GymLockerMenuUI.Active;
        if (ui == null || !ui.IsShowingForVerification) throw new InvalidOperationException("outfit menu not shown");
        RectTransform panel = (RectTransform)ui.TopButtonsForVerification[0].transform.parent;
        var swatches = new List<Rect>();
        foreach (Button[] row in new[] { ui.ShirtButtonsForVerification, ui.BottomColorButtonsForVerification })
            foreach (Button swatch in row)
                swatches.Add(PanelRect(panel, (RectTransform)swatch.transform.Find("Worn")));

        float minGap = float.PositiveInfinity;
        int buttons = 0;
        foreach (Button[] row in new[]
                 { ui.TopButtonsForVerification, ui.BottomButtonsForVerification, ui.HeadwearButtonsForVerification })
        {
            foreach (Button button in row)
            {
                Transform t = button.transform;
                Vector3 scale = t.localScale;
                Quaternion rotation = t.localRotation;
                t.localScale = new Vector3(HoverPeakScale, HoverPeakScale, 1f);
                t.localRotation = Quaternion.Euler(0f, 0f, -HoverTilt);
                Rect hovered = PanelRect(panel, (RectTransform)t.Find("Focus Backing"));
                t.localScale = scale;
                t.localRotation = rotation;
                foreach (Rect swatch in swatches)
                {
                    float gapX = Mathf.Max(swatch.xMin - hovered.xMax, hovered.xMin - swatch.xMax);
                    if (gapX >= MinSwatchGap) continue;
                    float gapY = Mathf.Max(swatch.yMin - hovered.yMax, hovered.yMin - swatch.yMax);
                    minGap = Mathf.Min(minGap, gapY);
                    if (gapY < MinSwatchGap)
                        throw new InvalidOperationException(
                            $"hovered {button.name} is {gapY:F1} units from a colour swatch (need {MinSwatchGap})");
                }
                buttons++;
            }
        }
        passed.Add($"hoverButtons={buttons} minSwatchGap={minGap:F1}");
        Debug.Log($"GYMCHAOS_LOCKER_SWATCH_SPACING_OK buttons={buttons} minGap={minGap:F1}");
    }

    private static Rect PanelRect(RectTransform panel, RectTransform rect)
    {
        var corners = new Vector3[4];
        rect.GetWorldCorners(corners);
        Vector2 min = Vector2.positiveInfinity;
        Vector2 max = Vector2.negativeInfinity;
        foreach (Vector3 corner in corners)
        {
            Vector2 local = panel.InverseTransformPoint(corner);
            min = Vector2.Min(min, local);
            max = Vector2.Max(max, local);
        }
        return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
    }

    private static void Expect(GymExperienceService service, Vector3 probe,
        GymBackRoomInteractable expected, string label)
    {
        GymBackRoomInteractable found = service.FindNearbyInteractable(probe, 3.1f);
        if (found != expected)
            throw new InvalidOperationException(
                $"{label} at {probe} shows '{(found != null ? found.name : "none")}', expected '{expected.name}'");
    }

    private static GymBackRoomInteractable Require(string name)
    {
        foreach (GymBackRoomInteractable item in GymBackRoomInteractable.RegisteredInteractables)
            if (item != null && item.name == name) return item;
        throw new InvalidOperationException($"{name} missing");
    }

    private static Bounds Area(GymBackRoomInteractable item) => item.GetComponent<Collider>().bounds;

    private static void Finish(int code)
    {
        finished = true;
        resultCode = code;
        if (code == 0) Debug.Log("GYMCHAOS_LOCKER_PROMPT_OK " + string.Join(" ", passed));
        GymChaosVerifierExit.Record(resultCode);
        SessionState.EraseBool(RequestedKey);
        EditorApplication.isPlaying = false;
    }
}
#endif
