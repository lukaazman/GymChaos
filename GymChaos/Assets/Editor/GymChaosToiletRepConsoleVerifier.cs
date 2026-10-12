#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Play-mode checks for the 2026-10-12 batch: reputation changes are silent
/// (no REP IMPACT notice, no sound, only the HUD stat moves), the bathroom
/// toilet opens and closes seat and lid together with a sound per action, and
/// every treadmill has its recoloured hood and live console canvas.
/// </summary>
[InitializeOnLoad]
public static class GymChaosToiletRepConsoleVerifier
{
    private const string RequestedKey = "GymChaos.ToiletRepConsoleVerificationRequested";
    private static double startedAt;
    private static double phaseStartedAt;
    private static bool finished;
    private static int resultCode;
    private static int phase;
    private static GymToiletSeat toilet;
    private static int toiletSoundsBefore;
    private static readonly List<string> passed = new List<string>();

    static GymChaosToiletRepConsoleVerifier()
    {
        if (!SessionState.GetBool(RequestedKey, false)) return;
        Hook();
    }

    [MenuItem("Tools/GymChaos/Run Toilet Rep Console Verification")]
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

    private static void Hook()
    {
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }

    private static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            startedAt = EditorApplication.timeSinceStartup;
            phaseStartedAt = startedAt;
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
        double now = EditorApplication.timeSinceStartup;
        try
        {
            if (now - startedAt > 120d) throw new TimeoutException($"timed out in phase {phase}");
            PlayerMovement player = UnityEngine.Object.FindAnyObjectByType<PlayerMovement>();
            if (!GymOutdoorBuilder.IsBuilt || !GymBackRoomBuilder.TryGetRoomBounds(out _) ||
                player == null || GymExperienceService.Active == null)
            {
                return;
            }

            switch (phase)
            {
                case 0:
                    if (now - startedAt < 6d) return;
                    CheckSilentReputation();
                    CheckTreadmillConsoles();
                    CaptureTreadmill();
                    phase = 1;
                    phaseStartedAt = now;
                    break;
                case 1:
                    toilet = UnityEngine.Object.FindAnyObjectByType<GymToiletSeat>();
                    if (toilet == null || !toilet.IsBoundForVerification)
                    {
                        if (now - phaseStartedAt > 40d)
                            throw new InvalidOperationException("Toilet seat and lid never bound");
                        return;
                    }
                    CheckToiletLayout();
                    CheckClosed("initial");
                    CaptureToilet("closed");
                    toiletSoundsBefore = SoundCount(GymSoundEffect.ToiletSeat);
                    toilet.Toggle();
                    phase = 2;
                    phaseStartedAt = now;
                    break;
                case 2:
                    if (now - phaseStartedAt < 1.0d) return;
                    if (!toilet.IsSeatUp ||
                        Mathf.Abs(toilet.SeatAngleForVerification - 92f) > 2f ||
                        Mathf.Abs(toilet.LidAngleForVerification - 96f) > 2f)
                    {
                        throw new InvalidOperationException(
                            $"Toilet did not open fully: seat={toilet.SeatAngleForVerification:F1} " +
                            $"lid={toilet.LidAngleForVerification:F1}");
                    }
                    RequirePrompt("Close toilet");
                    CaptureToilet("open");
                    toilet.Toggle();
                    phase = 3;
                    phaseStartedAt = now;
                    break;
                case 3:
                    if (now - phaseStartedAt < 1.0d) return;
                    CheckClosed("after close");
                    RequirePrompt("Open toilet");
                    int sounds = SoundCount(GymSoundEffect.ToiletSeat) - toiletSoundsBefore;
                    if (sounds != 2)
                        throw new InvalidOperationException($"Expected 2 toilet seat sounds, got {sounds}");
                    passed.Add("toilet=open+close sounds=2");
                    Debug.Log("GYMCHAOS_TOILET_SEAT_OK states=2 sounds=2");
                    Finish(0);
                    break;
            }
        }
        catch (Exception exception)
        {
            Debug.LogError("GYMCHAOS_TOILET_REP_CONSOLE_FAIL " + exception.Message);
            Finish(1);
        }
    }

    private static int SoundCount(GymSoundEffect effect) =>
        GymAudio.PlayCountsForVerification.TryGetValue(effect, out int count) ? count : 0;

    private static void CheckSilentReputation()
    {
        GymExperienceService service = GymExperienceService.Active;
        var notices = new List<string>();
        Action<string, float, int> listener = (text, seconds, priority) => notices.Add(text);
        service.NoticeRaised += listener;
        int before = service.Reputation;
        int levelBefore = service.State != null ? service.State.level : 0;
        int confirmBefore = SoundCount(GymSoundEffect.ActionConfirm);
        try
        {
            // Daily-goal rewards are their own notice and sound; keep them out.
            service.ChangeReputation(3, "verifier-silent-rep", false);
            service.ChangeReputation(-2, "verifier-silent-rep", false);
            service.RegisterDialogueChoice(2, "verifier-silent-rep");
        }
        finally
        {
            service.NoticeRaised -= listener;
        }
        if (service.Reputation == before)
            throw new InvalidOperationException("Reputation did not change");
        foreach (string text in notices)
        {
            if (text.IndexOf("REP", StringComparison.Ordinal) >= 0)
                throw new InvalidOperationException("Reputation still raised a notice: " + text);
        }
        bool leveled = service.State != null && service.State.level != levelBefore;
        int confirmSounds = SoundCount(GymSoundEffect.ActionConfirm) - confirmBefore;
        if (!leveled && confirmSounds != 0)
            throw new InvalidOperationException($"Reputation actions played {confirmSounds} sounds");
        passed.Add($"rep={before}->{service.Reputation} notices={notices.Count}");
        Debug.Log($"GYMCHAOS_SILENT_REPUTATION_OK rep={before}->{service.Reputation} " +
                  $"repNotices=0 sounds={confirmSounds}");
    }

    private static void CheckTreadmillConsoles()
    {
        GymTreadmillConsole[] consoles =
            UnityEngine.Object.FindObjectsByType<GymTreadmillConsole>(FindObjectsSortMode.None);
        if (consoles.Length == 0) throw new InvalidOperationException("No treadmill console");
        foreach (GymTreadmillConsole console in consoles)
        {
            if (console.ScreenRendererForVerification == null)
                throw new InvalidOperationException(console.name + " has no screen renderer");
            if (console.ConsoleCanvasForVerification == null)
                throw new InvalidOperationException(console.name + " has no console canvas");
            if (console.RecoloredHoodPartsForVerification < 3)
                throw new InvalidOperationException(
                    $"{console.name} recoloured {console.RecoloredHoodPartsForVerification} hood parts");
            if (string.IsNullOrEmpty(console.ReadoutForVerification))
                throw new InvalidOperationException(console.name + " readout is empty");
            Bounds screen = console.ScreenRendererForVerification.bounds;
            Vector3 canvas = console.ConsoleCanvasForVerification.transform.position;
            // The readout sits on the console housing face around the screen.
            if (!screen.Contains(canvas) && screen.SqrDistance(canvas) > 0.35f * 0.35f)
                throw new InvalidOperationException($"{console.name} canvas is off the console");
            if (console.ScreenRendererForVerification.enabled)
                throw new InvalidOperationException($"{console.name} glass block still covers the readout");
        }
        LogConsoleOccluders(consoles[0]);
        passed.Add($"treadmillConsoles={consoles.Length}");
        Debug.Log($"GYMCHAOS_TREADMILL_CONSOLE_OK consoles={consoles.Length} " +
                  $"readout=\"{consoles[0].ReadoutForVerification}\"");
    }

    private static void CheckToiletLayout()
    {
        GameObject toiletObject = GameObject.Find("Authored toilet");
        if (toiletObject == null) throw new InvalidOperationException("Authored toilet missing");
        Bounds bounds = default;
        bool any = false;
        foreach (Renderer renderer in toiletObject.GetComponentsInChildren<Renderer>())
        {
            if (renderer.name == "Toilet lid" || renderer.name == "Toilet seat") continue;
            if (!any) { bounds = renderer.bounds; any = true; }
            else bounds.Encapsulate(renderer.bounds);
        }
        if (!any) throw new InvalidOperationException("Toilet has no renderers");
        if (Mathf.Abs(bounds.size.y - 1.2f) > 0.05f)
            throw new InvalidOperationException($"Toilet height {bounds.size.y:F2} is not 1.2x");
        if (GameObject.Find("Toilet paper roll") == null)
            throw new InvalidOperationException("Toilet paper roll missing");
        GameObject plate = GameObject.Find("Toilet paper holder plate");
        float wallX = plate != null ? plate.transform.position.x + 0.012f : float.NaN;
        float backGap = wallX - bounds.max.x;
        if (float.IsNaN(backGap) || backGap < -0.01f || backGap > 0.08f)
            throw new InvalidOperationException($"Toilet back is {backGap:F3} m from the wall");
        foreach (GymBackRoomInteractable item in GymBackRoomInteractable.RegisteredInteractables)
        {
            if (item != null && item.DisplayName != null &&
                item.DisplayName.IndexOf("bathroom", StringComparison.OrdinalIgnoreCase) >= 0)
                throw new InvalidOperationException("Use bathroom action still exists");
        }
        passed.Add($"toiletHeight={bounds.size.y:F2} backGap={backGap:F3}");
        Debug.Log($"GYMCHAOS_TOILET_LAYOUT_OK height={bounds.size.y:F2} backGap={backGap:F3}");
    }

    private static void CheckClosed(string label)
    {
        if (toilet.IsSeatUp || toilet.SeatAngleForVerification > 1f || toilet.LidAngleForVerification > 1f)
            throw new InvalidOperationException(
                $"Toilet not fully closed ({label}): seat={toilet.SeatAngleForVerification:F1} " +
                $"lid={toilet.LidAngleForVerification:F1}");
    }

    private static void RequirePrompt(string expected)
    {
        GymBackRoomInteractable item = toilet.GetComponent<GymBackRoomInteractable>();
        if (item == null || item.DisplayName != expected)
            throw new InvalidOperationException(
                $"Toilet prompt '{item?.DisplayName}' expected '{expected}'");
    }

    // With -Graphics: in-game views of a treadmill console and the toilet
    // stall for visual review (Logs/verify/toilet-rep-console/).
    private static bool CanCapture =>
        SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null;

    private static void CaptureTreadmill()
    {
        if (!CanCapture) return;
        GymTreadmillConsole console = UnityEngine.Object.FindAnyObjectByType<GymTreadmillConsole>();
        if (console == null || console.ConsoleCanvasForVerification == null) return;
        Transform canvas = console.ConsoleCanvasForVerification.transform;
        Vector3 toRunner = -canvas.forward;
        Vector3 c = canvas.position;
        Shoot("treadmill-console", c + toRunner * 0.75f + Vector3.up * 0.12f, c, 50f);
        // Roughly the runner's own view from the belt.
        Shoot("treadmill-runner", console.RunnerEyeForVerification, c, 60f);
        Vector3 side = Vector3.Cross(Vector3.up, toRunner).normalized;
        Bounds machine = console.ScreenRendererForVerification.bounds;
        foreach (Renderer renderer in console.GetComponentsInChildren<MeshRenderer>())
            machine.Encapsulate(renderer.bounds);
        Vector3 centre = machine.center;
        Shoot("treadmill-side",
            centre + side * 2.4f + toRunner * 0.6f + Vector3.up * 1.1f, centre, 50f);
    }

    // Any treadmill part in front of the display plane inside its rectangle
    // would cut across the readout.
    private static void LogConsoleOccluders(GymTreadmillConsole console)
    {
        RectTransform canvas = (RectTransform)console.ConsoleCanvasForVerification.transform;
        Vector3[] corners = new Vector3[4];
        canvas.GetWorldCorners(corners);
        Vector3 normal = -canvas.forward;
        Vector3 origin = canvas.position;
        Vector3 right = (corners[3] - corners[0]).normalized;
        Vector3 up = (corners[1] - corners[0]).normalized;
        float halfW = (corners[3] - corners[0]).magnitude * 0.5f;
        float halfH = (corners[1] - corners[0]).magnitude * 0.5f;
        var lines = new List<string>();
        foreach (MeshRenderer renderer in console.GetComponentsInChildren<MeshRenderer>())
        {
            MeshFilter filter = renderer.GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null || !filter.sharedMesh.isReadable) continue;
            float worst = float.MinValue;
            foreach (Vector3 v in filter.sharedMesh.vertices)
            {
                Vector3 o = renderer.transform.TransformPoint(v) - origin;
                if (Mathf.Abs(Vector3.Dot(o, right)) > halfW || Mathf.Abs(Vector3.Dot(o, up)) > halfH) continue;
                worst = Mathf.Max(worst, Vector3.Dot(o, normal));
            }
            if (worst > float.MinValue)
                lines.Add($"{renderer.name}:{worst:F3}:{(renderer.enabled ? "on" : "off")}");
        }
        Debug.Log($"GYMCHAOS_TREADMILL_CONSOLE_PLANE size={halfW * 2f:F3}x{halfH * 2f:F3} " +
                  $"normal={normal} inFront=[{string.Join(" ", lines)}]");
    }

    private static void CaptureToilet(string label)
    {
        if (!CanCapture) return;
        GameObject toiletObject = GameObject.Find("Authored toilet");
        if (toiletObject == null) return;
        Bounds bounds = toiletObject.GetComponentInChildren<Renderer>().bounds;
        foreach (Renderer renderer in toiletObject.GetComponentsInChildren<Renderer>())
            bounds.Encapsulate(renderer.bounds);
        Vector3 look = bounds.center + Vector3.down * 0.1f;
        Shoot("toilet-" + label, look + new Vector3(-1.5f, 0.75f, -0.7f), look, 55f);
        if (label == "open")
            Shoot("toilet-open-top", look + new Vector3(-0.75f, 1.05f, 0f), look + Vector3.down * 0.1f, 55f);
    }

    private static void Shoot(string name, Vector3 eye, Vector3 target, float fov)
    {
        string directory = System.IO.Path.Combine(
            System.IO.Directory.GetParent(Application.dataPath).Parent.FullName,
            "Logs", "verify", "toilet-rep-console");
        System.IO.Directory.CreateDirectory(directory);
        GameObject host = new GameObject("Toilet Rep Console Capture Camera");
        Camera camera = host.AddComponent<Camera>();
        camera.fieldOfView = fov;
        camera.nearClipPlane = 0.03f;
        camera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(target - eye));
        RenderTexture rt = new RenderTexture(960, 600, 24);
        camera.targetTexture = rt;
        camera.Render();
        RenderTexture previous = RenderTexture.active;
        RenderTexture.active = rt;
        Texture2D image = new Texture2D(960, 600, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0, 0, 960, 600), 0, 0);
        image.Apply();
        RenderTexture.active = previous;
        camera.targetTexture = null;
        System.IO.File.WriteAllBytes(System.IO.Path.Combine(directory, name + ".png"), image.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(image);
        rt.Release();
        UnityEngine.Object.DestroyImmediate(rt);
        UnityEngine.Object.DestroyImmediate(host);
    }

    private static void Finish(int code)
    {
        finished = true;
        resultCode = code;
        if (code == 0)
            Debug.Log("GYMCHAOS_TOILET_REP_CONSOLE_OK " + string.Join(" ", passed));
        GymChaosVerifierExit.Record(resultCode);
        SessionState.EraseBool(RequestedKey);
        EditorApplication.isPlaying = false;
    }
}
#endif
