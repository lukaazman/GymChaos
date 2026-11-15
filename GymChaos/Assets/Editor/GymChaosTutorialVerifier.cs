#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Tutorial gate (edit mode, no play needed). Proves the two rules that keep an
/// old player out of the tutorial (arm only on a fresh save, migrate anything
/// with experience to done), the step machine (one signal per step, a stray
/// signal never skips a lesson, a reload resumes), the spawn pose (outside the
/// doors, facing in), the marker (one column, chevrons only while far) and the
/// race coach (the old controls wall is gone, keys arrive one at a time).
/// </summary>
public static class GymChaosTutorialVerifier
{
    [MenuItem("Tools/GymChaos/Run Tutorial Verification")]
    public static void Run()
    {
        int code = 0;
        GameObject markerRoot = null;
        GameObject doorObject = null;
        GymRaceHud raceHud = null;
        try
        {
            Require(GymTutorialSteps.Count == 11, "step count " + GymTutorialSteps.Count);
            StateRules();
            ReloadResumes();
            SignalGating();
            RaceRules();
            CoachOrder();
            markerRoot = SpawnAndMarker(out doorObject);
            raceHud = RaceMinimapMarker();
            Capture(markerRoot);
            Debug.Log("GYMCHAOS_TUTORIAL_OK");
        }
        catch (Exception exception)
        {
            Debug.LogError("GYMCHAOS_TUTORIAL_FAIL " + exception);
            code = 1;
        }
        finally
        {
            if (raceHud != null) UnityEngine.Object.DestroyImmediate(raceHud.gameObject);
            if (markerRoot != null) UnityEngine.Object.DestroyImmediate(markerRoot);
            if (doorObject != null)
            {
                if (GymDoorway.Instance != null &&
                    GymDoorway.Instance.gameObject == doorObject)
                {
                    typeof(GymDoorway)
                        .GetProperty("Instance", System.Reflection.BindingFlags.Static |
                            System.Reflection.BindingFlags.Public)
                        .SetValue(null, null);
                }
                UnityEngine.Object.DestroyImmediate(doorObject);
            }
        }
        if (Application.isBatchMode) EditorApplication.Exit(code);
    }

    // ------------------------------------------------------------ state rules

    private static void StateRules()
    {
        GymProgressionState fresh = new GymProgressionState();
        fresh.Normalize();
        Require(fresh.tutorialStep == 0, "a state nobody armed must stay at 0, was " + fresh.tutorialStep);

        GymTutorialDirector.ArmForNewSave(fresh, true);
        Require(fresh.tutorialStep == 1, "a new save must start at step 1, was " + fresh.tutorialStep);

        GymTutorialDirector.ArmForNewSave(fresh, false);
        Require(fresh.tutorialStep == 1, "re-arming a loaded save must not reset the step");

        GymProgressionState legacy = new GymProgressionState();
        legacy.totalExperience = 540;
        legacy.Normalize();
        Require(legacy.tutorialStep == GymTutorialSteps.Done,
            "a save with experience must migrate to done, was " + legacy.tutorialStep);

        GymProgressionState clamped = new GymProgressionState();
        clamped.tutorialStep = 99;
        clamped.Normalize();
        Require(clamped.tutorialStep == GymTutorialSteps.Done,
            "step must clamp to done, was " + clamped.tutorialStep);

        GymProgressionState skipped = new GymProgressionState();
        GymTutorialDirector.ArmForNewSave(skipped, true);
        skipped.experience = 25;
        GymTutorialDirector.SkipAll(skipped);
        Require(skipped.tutorialStep == GymTutorialSteps.Done, "skip must land on done");
        Require(skipped.experience == 25, "skip must keep the experience already earned");
        skipped.Normalize();
        Require(skipped.tutorialStep == GymTutorialSteps.Done, "a skipped save must stay done after a load");

        // Each step owns one key, one line and one signal; nothing is blank.
        for (int step = 1; step <= GymTutorialSteps.Count; step++)
        {
            GymTutorialStep definition = GymTutorialSteps.Get(step);
            Require(!string.IsNullOrEmpty(definition.Title), "step " + step + " has no title");
            Require(!string.IsNullOrEmpty(definition.Line), "step " + step + " has no line");
            Require(!string.IsNullOrEmpty(definition.Key), "step " + step + " has no key");
            Require(definition.Signal != GymTutorialSignal.None, "step " + step + " has no signal");
            Require(definition.Reward > 0, "step " + step + " pays nothing");
        }
    }

    private static void ReloadResumes()
    {
        GymProgressionState mid = new GymProgressionState();
        GymTutorialDirector.ArmForNewSave(mid, true);
        mid.tutorialStep = 6;
        mid.experience = 120;
        mid.Normalize();
        GymProgressionState reloaded =
            JsonUtility.FromJson<GymProgressionState>(JsonUtility.ToJson(mid));
        reloaded.Normalize();
        Require(reloaded.tutorialStep == 6,
            "a reload mid tutorial must resume the same step, was " + reloaded.tutorialStep);
        Require(reloaded.tutorialRaceDone == false, "reload must keep the race pending");

        reloaded.tutorialStep = GymTutorialSteps.RaceStep;
        reloaded.tutorialRaceDone = true;
        GymProgressionState finished =
            JsonUtility.FromJson<GymProgressionState>(JsonUtility.ToJson(reloaded));
        finished.Normalize();
        Require(finished.tutorialRaceDone, "the finished race flag must survive a reload");
    }

    private static void SignalGating()
    {
        GymProgressionState state = new GymProgressionState();
        GymTutorialDirector.ArmForNewSave(state, true);
        for (int step = 1; step <= GymTutorialSteps.Count; step++)
        {
            Require(state.tutorialStep == step, "expected step " + step + ", was " + state.tutorialStep);
            GymTutorialStep definition = GymTutorialSteps.Get(step);
            // A signal that belongs to another step must never move the chain.
            for (int other = 1; other < 12; other++)
            {
                GymTutorialSignal stray = (GymTutorialSignal)other;
                if (stray == definition.Signal) continue;
                bool advanced = GymTutorialDirector.CompleteStepFor(state, stray);
                Require(!advanced && state.tutorialStep == step,
                    "step " + step + " was moved by foreign signal " + stray);
            }
            if (step == GymTutorialSteps.RaceStep)
            {
                Require(!GymTutorialDirector.CompleteStepFor(state, definition.Signal),
                    "the race step must wait for a finished race");
                GymTutorialDirector.MarkTutorialRaceFinished(state);
                Require(state.tutorialRaceDone, "the race must mark the save");
                Require(state.tutorialStep == GymTutorialSteps.Done,
                    "the race step must end the tutorial, was " + state.tutorialStep);
                break;
            }
            Require(GymTutorialDirector.CompleteStepFor(state, definition.Signal),
                "step " + step + " did not accept its own signal " + definition.Signal);
            Require(state.tutorialStep == step + 1,
                "step " + step + " advanced to " + state.tutorialStep);
        }
        // A done save ignores every signal.
        Require(!GymTutorialDirector.IsActive(state), "a done save must not be active");
        Require(!GymTutorialDirector.CompleteStepFor(state, GymTutorialSignal.LookedAround),
            "a done save must ignore tutorial signals");
    }

    private static void RaceRules()
    {
        GymProgressionState state = new GymProgressionState();
        GymTutorialDirector.ArmForNewSave(state, true);
        state.tutorialStep = GymTutorialSteps.RaceStep;
        Require(GymTutorialDirector.TutorialRacePendingFor(state),
            "the easy race must be pending on the race step");
        GymTutorialDirector.MarkTutorialRaceFinished(state);
        Require(!GymTutorialDirector.TutorialRacePendingFor(state),
            "the easy race must stop being pending once it ran");
        Require(GymTutorialDirector.TutorialRacePendingFor(null) == false,
            "no state means no tutorial race");
        // Finishing the race ends the chain from the race side, not the poller.
        Require(state.tutorialStep == GymTutorialSteps.Done &&
            state.tutorialRaceDone && !GymTutorialDirector.IsActive(state),
            "a finished tutorial race must leave the save done and inactive");

        Require(GymRaceDirector.CountdownHintForVerification.Length == 0,
            "the countdown must not list the controls any more: " +
            GymRaceDirector.CountdownHintForVerification);
        string wall = GymRaceDirector.CountdownHintForVerification;
        Require(!wall.Contains("RESPAWN") && !wall.Contains("LOOK BACK"),
            "the controls wall is still in the countdown");
    }

    private static void CoachOrder()
    {
        GymRaceCoach coach = new GymRaceCoach();
        coach.Reset();
        List<string> seen = new List<string>();
        string line = coach.Tick(0f, 0f, 0f, 0f, false, false, false);
        seen.Add(line);
        line = coach.Tick(3f, 8f, 0.3f, 0.1f, false, false, false);
        seen.Add(line);
        line = coach.Tick(6f, 14f, 0.8f, 0.2f, true, false, false);
        seen.Add(line);
        line = coach.Tick(9f, 14f, 0f, 1f, false, false, false);
        seen.Add(line);
        line = coach.Tick(12f, 14f, 0f, 0.4f, false, true, false);
        seen.Add(line);
        line = coach.Tick(15f, 0f, 0f, 0f, false, false, true);
        Require(string.IsNullOrEmpty(line), "one frame of being stuck is not enough: " + line);
        line = coach.Tick(15.3f, 0f, 0f, 0f, false, false, true);
        seen.Add(line);

        Require(seen.Count == GymRaceCoach.Keys.Length,
            "coach produced " + seen.Count + " beats, expected " + GymRaceCoach.Keys.Length);
        for (int i = 0; i < seen.Count; i++)
        {
            Require(!string.IsNullOrEmpty(seen[i]), "coach beat " + i + " was empty");
            Require(seen[i].StartsWith(GymRaceCoach.Keys[i], StringComparison.Ordinal),
                "coach beat " + i + " is '" + seen[i] + "', expected key " + GymRaceCoach.Keys[i]);
            Require(!seen[i].Contains("BRAKE") || i == 0,
                "coach must not fall back to the controls list: " + seen[i]);
        }
        Require(coach.NotifyFinalLap() == GymRaceCoach.FinalLapLine, "the last lap needs its line");
        Require(coach.NotifyFinalLap() == GymRaceCoach.FinalLapLine,
            "the last lap line must hold, not repeat or clear");

        // The dwell: within a beat the same line stays, so nothing flickers.
        coach.Reset();
        string first = coach.Tick(0f, 0f, 0f, 0f, false, false, false);
        string second = coach.Tick(1f, 9f, 0.4f, 0f, true, true, false);
        Require(first == second, "the coach must hold a line for its dwell");

        // Getting stuck early shows the respawn key without skipping the lessons still to come.
        coach.Reset();
        coach.Tick(0f, 0f, 0f, 0f, false, false, false);
        coach.Tick(3f, 0f, 0f, 0f, false, false, true);
        string stuckLine = coach.Tick(3.3f, 0f, 0f, 0f, false, false, true);
        string after = coach.Tick(6f, 8f, 0.3f, 0.1f, false, false, false);
        Require(stuckLine.StartsWith(GymRaceCoach.Keys[5], StringComparison.Ordinal) &&
            after.StartsWith(GymRaceCoach.Keys[1], StringComparison.Ordinal),
            "a stuck moment skipped the steering lesson ('" + stuckLine + "' then '" + after + "')");
    }

    // --------------------------------------------------------- spawn + marker

    private static GameObject SpawnAndMarker(out GameObject doorObject)
    {
        doorObject = new GameObject("Tutorial verifier doorway");
        GymDoorway door = doorObject.AddComponent<GymDoorway>();
        Vector3 centre = new Vector3(4f, 0f, 10f);
        door.Configure(centre, centre + Vector3.forward * 5f, centre - Vector3.forward * 5f,
            null, null);
        // Awake does not run for a component added in edit mode: publish it by hand.
        typeof(GymDoorway)
            .GetProperty("Instance", System.Reflection.BindingFlags.Static |
                System.Reflection.BindingFlags.Public)
            .SetValue(null, door);

        Require(GymTutorialDirector.TryGetSpawnPose(out Vector3 position, out Quaternion rotation),
            "the spawn pose must come from the doorway");
        float offset = Vector3.Distance(new Vector3(position.x, centre.y, position.z), centre);
        Require(offset > 4f && offset < 8f,
            "the spawn must sit outside the doors, offset was " + offset);
        Vector3 towardDoor = centre - position;
        towardDoor.y = 0f;
        towardDoor.Normalize();
        float facing = Vector3.Dot(rotation * Vector3.forward, towardDoor);
        Require(facing > 0.8f, "the spawn must face the doors, dot was " + facing);

        GameObject root = new GameObject("Tutorial verifier marker");
        GymTutorialMarker marker = GymTutorialMarker.Create(root.transform);
        Vector3 target = position + rotation * Vector3.forward * 20f;
        marker.Show(target, position);
        Require(marker.IsVisible, "the marker must show");
        Require(marker.PillarActive && marker.RingActive, "the marker needs its column and ring");
        Require(marker.VisibleChevrons == GymTutorialMarker.ChevronTotal,
            "a far target needs every chevron, showed " + marker.VisibleChevrons);

        Require(marker.ArrowActive, "a far target needs its floating arrow");
        marker.UpdateChevrons(position, position + rotation * Vector3.forward * 2f);
        Require(marker.VisibleChevrons == 0,
            "a close target must hide the chevrons, showed " + marker.VisibleChevrons);
        Require(!marker.ArrowActive,
            "inside the interaction radius the floating arrow must go away");

        marker.Hide();
        Require(!marker.IsVisible && !marker.PillarActive,
            "hiding must take the column with it");
        marker.Show(target, position);
        return root;
    }

    /// <summary>The tutorial race marks its goal on the minimap; every other race does not.</summary>
    private static GymRaceHud RaceMinimapMarker()
    {
        GymRaceHud hud = GymRaceHud.Create();
        List<GymRaceRacer> none = new List<GymRaceRacer>();
        hud.UpdateMinimap(none);
        Require(!hud.MinimapObjectiveVisible,
            "a normal race must not draw the tutorial objective");
        hud.SetRaceWidgetsVisible(true);
        hud.UpdateMinimap(none);
        Require(!hud.MinimapObjectiveVisible,
            "the objective must stay hidden while nobody asked for it");

        GymRaceTrackLayout.Main.Sample(GymRaceTrackLayout.StartLineDistance,
            out Vector3 goal, out _, out _, out _);
        hud.SetMinimapObjective(goal, true);
        hud.UpdateMinimap(none);
        Require(hud.MinimapObjectiveVisible,
            "the tutorial race needs its minimap marker");
        Require(hud.MinimapObjectiveColor == PersonaMenuStyle.Gold,
            "the minimap objective must be gold, was " + hud.MinimapObjectiveColor);
        Require(hud.MinimapObjectivePosition.sqrMagnitude > 0f,
            "the minimap objective must sit on the track, not at the corner");

        CaptureRaceHud(hud, "minimap-objective.png");

        hud.SetMinimapObjective(goal, false);
        hud.UpdateMinimap(none);
        Require(!hud.MinimapObjectiveVisible,
            "the marker must disappear when the objective is cleared");
        return hud;
    }

    // With a graphics device: Logs/agent/tutorial/minimap-objective.png of the HUD.
    private static void CaptureRaceHud(GymRaceHud hud, string fileName)
    {
        if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
        {
            return;
        }
        Canvas canvas = hud.GetComponentInParent<Canvas>() ?? hud.GetComponentInChildren<Canvas>();
        if (canvas == null)
        {
            return;
        }
        hud.SetRaceWidgetsVisible(true);
        GameObject cameraObject = new GameObject("Tutorial minimap capture camera");
        Camera camera = cameraObject.AddComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.08f, 0.1f, 0.14f, 1f);
        RenderTexture target = new RenderTexture(1280, 720, 24);
        camera.targetTexture = target;
        canvas.renderMode = RenderMode.ScreenSpaceCamera;
        canvas.worldCamera = camera;
        canvas.planeDistance = 1f;
        Canvas.ForceUpdateCanvases();
        camera.Render();
        RenderTexture.active = target;
        Texture2D picture = new Texture2D(1280, 720, TextureFormat.RGB24, false);
        picture.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
        RenderTexture.active = null;
        string directory = System.IO.Path.Combine(
            System.IO.Directory.GetCurrentDirectory(), "..", "Logs", "agent", "tutorial");
        System.IO.Directory.CreateDirectory(directory);
        System.IO.File.WriteAllBytes(
            System.IO.Path.Combine(directory, fileName), picture.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(picture);
        UnityEngine.Object.DestroyImmediate(target);
        UnityEngine.Object.DestroyImmediate(cameraObject);
    }

    private static void Capture(GameObject root)
    {
        if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
        {
            return;
        }
        GameObject cameraObject = new GameObject("Tutorial capture camera");
        Camera camera = cameraObject.AddComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.02f, 0.04f, 0.09f, 1f);
        camera.transform.position = new Vector3(0f, 3f, -10f);
        camera.transform.LookAt(new Vector3(0f, 1.6f, 0f));
        RenderTexture target = new RenderTexture(1280, 720, 24);
        camera.targetTexture = target;
        camera.Render();
        RenderTexture.active = target;
        Texture2D picture = new Texture2D(1280, 720, TextureFormat.RGB24, false);
        picture.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
        RenderTexture.active = null;
        string directory = System.IO.Path.Combine(
            System.IO.Directory.GetCurrentDirectory(), "..", "Logs", "agent", "tutorial");
        System.IO.Directory.CreateDirectory(directory);
        System.IO.File.WriteAllBytes(
            System.IO.Path.Combine(directory, "marker.png"), picture.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(picture);
        UnityEngine.Object.DestroyImmediate(target);
        UnityEngine.Object.DestroyImmediate(cameraObject);
        Require(root != null, "marker root vanished");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
#endif
