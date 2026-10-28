#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Cardio pace gates and the daily cardio challenge: on the treadmill and the
/// bike the first 2-unit band is free, every next band opens only after a
/// timing check (not a rep), a miss holds the pace without slowing down,
/// slowing never needs a check, the window narrows with pace and is closed
/// past the sustainable top pace, and the daily challenge completes and pays.
/// </summary>
[InitializeOnLoad]
public static class GymChaosCardioGateVerifier
{
    private const string RequestedKey = "GymChaos.CardioGateVerificationRequested";
    private static double startedAt;
    private static bool finished;
    private static int resultCode = 1;
    private static readonly List<string> passed = new List<string>();

    static GymChaosCardioGateVerifier()
    {
        if (!SessionState.GetBool(RequestedKey, false)) return;
        Hook();
    }

    [MenuItem("Tools/GymChaos/Run Cardio Gate Verification")]
    public static void Run()
    {
        finished = false;
        resultCode = 1;
        passed.Clear();
        GymChaosVerifierExit.Record(resultCode);
        GymChaosVerifierSave.Neutralize();
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
            startedAt = EditorApplication.timeSinceStartup;
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
        try
        {
            double now = EditorApplication.timeSinceStartup;
            if (now - startedAt > 90d) throw new TimeoutException("scene never became ready");
            PlayerMovement player = UnityEngine.Object.FindAnyObjectByType<PlayerMovement>();
            GymExperienceService service = GymExperienceService.Active;
            if (player == null || service == null || service.State == null || now - startedAt < 5d) return;

            CheckWindowCurve();
            CheckStation(FindStation(GymExerciseType.Treadmill), player, service);
            CheckStation(FindStation(GymExerciseType.ExerciseBike), player, service);
            CheckChallenge(service);
            CheckEveryChallengeKind(service);
            Finish(0);
        }
        catch (Exception exception)
        {
            Debug.LogError("GYMCHAOS_CARDIO_GATE_FAIL " + exception.Message);
            Finish(1);
        }
    }

    private static GymExerciseStation FindStation(GymExerciseType type)
    {
        foreach (GymExerciseStation station in
            UnityEngine.Object.FindObjectsByType<GymExerciseStation>(FindObjectsSortMode.None))
        {
            if (station.ExerciseType == type && !station.IsOccupied) return station;
        }
        throw new InvalidOperationException("no free " + type);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    // The gate window narrows with every band and is closed at the top pace.
    private static void CheckWindowCurve()
    {
        const float maximum = 18f;
        float previous = float.MaxValue;
        var values = new List<string>();
        for (float gate = 2f; gate <= 20f; gate += 2f)
        {
            float scale = GymExerciseStation.GetCardioGateWindowScale(gate, maximum);
            values.Add($"{gate:0}:{scale:F2}");
            Require(scale <= previous + 1e-4f, $"window grew at gate {gate}: {scale:F3} > {previous:F3}");
            previous = scale;
        }
        Require(GymExerciseStation.GetCardioGateWindowScale(4f, maximum) > 0.75f, "low-pace window too narrow");
        Require(GymExerciseStation.GetCardioGateWindowScale(18f, maximum) < 0.2f, "top-pace window too wide");
        Require(GymExerciseStation.GetCardioGateWindowScale(20f, maximum) <= 0f, "unrealistic pace still reachable");
        passed.Add("windows=" + string.Join(",", values));
    }

    private static void CheckStation(GymExerciseStation station, PlayerMovement player, GymExperienceService service)
    {
        string label = station.ExerciseType.ToString();
        int repGoalBefore = service.State.dailyProgress[0];
        station.BeginSession(player.playerCamera.transform);
        try
        {
            // The first band (0-2) is free: two presses, no check.
            Press(station, up: true);
            Press(station, up: true);
            Require(!station.TechniqueCheck.IsActive, $"{label}: gate opened inside the free band");
            Run(station, 3f);
            Require(Mathf.Abs(station.CurrentTreadmillSpeed - 2f) < 0.2f,
                $"{label}: free band speed {station.CurrentTreadmillSpeed:F2}, expected 2");

            // Next band: a gate opens instead of a speed change.
            Press(station, up: true);
            Require(station.TechniqueCheck.IsActive, $"{label}: no pace gate at the 2 band edge");
            Require(station.GetSessionHud().Contains("PACE GATE"), $"{label}: HUD does not show the gate");
            float perfectWindow = station.TechniqueCheck.PerfectHalfAngle;
            station.TechniqueCheck.SetMarkerAngleForVerification(0f);
            station.TickSession(0f, true, false, false); // hit without moving the marker
            Require(station.LastWorkoutResult == WorkoutResult.Perfect, $"{label}: centre hit was {station.LastWorkoutResult}");
            Require(Mathf.Approximately(station.CardioUnlockedSpeed, 4f), $"{label}: unlocked {station.CardioUnlockedSpeed} after a pass");
            Run(station, 3f);
            Require(station.CurrentTreadmillSpeed > 2.7f, $"{label}: speed did not rise after the pass");

            // Fill the band, then miss the next gate: pace holds, no drop.
            Press(station, up: true);
            Run(station, 2f);
            float held = station.CurrentTreadmillSpeed;
            Press(station, up: true);
            Require(station.TechniqueCheck.IsActive, $"{label}: no gate at the 4 band edge");
            Require(station.TechniqueCheck.PerfectHalfAngle < perfectWindow,
                $"{label}: window did not narrow ({station.TechniqueCheck.PerfectHalfAngle:F2} vs {perfectWindow:F2})");
            station.TechniqueCheck.SetMarkerAngleForVerification(180f);
            station.TickSession(0f, true, false, false); // hit without moving the marker
            Require(station.LastWorkoutResult == WorkoutResult.Miss, $"{label}: far hit was {station.LastWorkoutResult}");
            Require(Mathf.Approximately(station.CardioUnlockedSpeed, 4f), $"{label}: a miss opened the band");
            Run(station, 2f);
            Require(station.CurrentTreadmillSpeed >= held - 0.15f,
                $"{label}: a miss slowed the pace {held:F2} -> {station.CurrentTreadmillSpeed:F2}");

            // Slowing down needs no check.
            Press(station, up: false);
            Require(!station.TechniqueCheck.IsActive, $"{label}: slowing opened a gate");
            Run(station, 2f);
            Require(station.CurrentTreadmillSpeed < held - 0.5f, $"{label}: S did not slow down");

            // Gates are not reps.
            Require(service.State.dailyProgress[0] == repGoalBefore,
                $"{label}: pace gates counted as workout reps");
        }
        finally
        {
            station.EndSession();
        }
        passed.Add($"{label}=free2,gate,perfect,miss-holds,slow-free,no-reps");
        Debug.Log($"GYMCHAOS_CARDIO_GATE_STATION_OK station={label}");
    }

    private static void Press(GymExerciseStation station, bool up)
    {
        station.TickSession(0.02f, false, up, !up);
    }

    private static void Run(GymExerciseStation station, float seconds)
    {
        for (float t = 0f; t < seconds; t += 0.05f)
        {
            station.TickSession(0.05f, false, false, false);
        }
    }

    private static void CheckChallenge(GymExperienceService service)
    {
        string label = service.CardioChallengeLabel;
        Require(!string.IsNullOrEmpty(label), "no daily cardio challenge");
        Require(!service.CardioChallengeCompleted, "challenge already complete on a fresh day");
        int xpBefore = service.State.totalExperience;
        switch (service.CurrentCardioChallengeKind)
        {
            case GymExperienceService.CardioChallengeKind.BikeDistance:
                service.ReportCardioProgress(GymExerciseType.ExerciseBike, 10f, 5000f, 1f);
                break;
            case GymExperienceService.CardioChallengeKind.PaceGates:
                for (int i = 0; i < 10 && !service.CardioChallengeCompleted; i++)
                    service.RegisterCardioGate(WorkoutResult.Perfect, GymExerciseType.Treadmill, 4f);
                break;
            default:
                // Distance, top pace and a long hold on the treadmill.
                service.ReportCardioProgress(GymExerciseType.Treadmill, 20f, 5000f, 600f);
                break;
        }
        Require(service.CardioChallengeCompleted, $"challenge '{label}' did not complete");
        Require(service.State.totalExperience > xpBefore, "challenge paid no XP");
        passed.Add($"challenge=\"{label}\" done");
        Debug.Log($"GYMCHAOS_CARDIO_CHALLENGE_OK kind={service.CurrentCardioChallengeKind} label=\"{label}\"");
    }

    // Walks gym days until every challenge kind has appeared, completing each
    // the way a player would and checking the hold reset and pace target.
    private static void CheckEveryChallengeKind(GymExperienceService service)
    {
        GymProgressionState state = service.State;
        int savedDay = state.gymDay;
        var seen = new HashSet<GymExperienceService.CardioChallengeKind>();
        float reachable = 18f * service.GetCardioCapacityMultiplier() *
            service.GetExercisePerformance(GymExerciseType.Treadmill);
        try
        {
            for (int day = 2; day < 60 && seen.Count < 5; day++)
            {
                state.gymDay = day;
                GymExperienceService.CardioChallengeKind kind = service.CurrentCardioChallengeKind;
                if (!seen.Add(kind)) continue;
                string label = service.CardioChallengeLabel;
                Require(!service.CardioChallengeCompleted, $"day {day}: new challenge already complete");
                switch (kind)
                {
                    case GymExperienceService.CardioChallengeKind.TreadmillDistance:
                        service.ReportCardioProgress(GymExerciseType.ExerciseBike, 10f, 9000f, 1f);
                        Require(!service.CardioChallengeCompleted, "bike distance counted for the treadmill");
                        service.ReportCardioProgress(GymExerciseType.Treadmill, 10f, 9000f, 1f);
                        break;
                    case GymExperienceService.CardioChallengeKind.BikeDistance:
                        service.ReportCardioProgress(GymExerciseType.ExerciseBike, 10f, 9000f, 1f);
                        break;
                    case GymExperienceService.CardioChallengeKind.ReachPace:
                        Require(state.cardioChallengeTarget <= reachable * 0.8f + 0.01f,
                            $"pace target {state.cardioChallengeTarget} above reachable {reachable:F1}");
                        service.ReportCardioProgress(GymExerciseType.Treadmill, state.cardioChallengeTarget, 1f, 0.1f);
                        break;
                    case GymExperienceService.CardioChallengeKind.HoldPace:
                        service.ReportCardioProgress(GymExerciseType.Treadmill, 14f, 1f, 10f);
                        service.ReportCardioStopped();
                        Require(state.cardioChallengeProgress <= 0.001f, "leaving the machine kept the hold");
                        service.ReportCardioProgress(GymExerciseType.Treadmill, 14f, 1f, 5f);
                        service.ReportCardioProgress(GymExerciseType.Treadmill, 1f, 1f, 0.1f);
                        Require(state.cardioChallengeProgress <= 0.001f, "slowing down kept the hold");
                        service.ReportCardioProgress(GymExerciseType.Treadmill, 14f, 1f, 600f);
                        break;
                    default:
                        service.RegisterCardioGate(WorkoutResult.Good, GymExerciseType.Treadmill, 4f);
                        Require(state.cardioChallengeProgress <= 0.001f, "a good gate counted as perfect");
                        for (int i = 0; i < 10 && !service.CardioChallengeCompleted; i++)
                            service.RegisterCardioGate(WorkoutResult.Perfect, GymExerciseType.Treadmill, 4f);
                        break;
                }
                Require(service.CardioChallengeCompleted, $"day {day}: '{label}' did not complete");
            }
            Require(seen.Count == 5, $"only {seen.Count} challenge kinds in 60 days");
        }
        finally
        {
            state.gymDay = savedDay;
            state.cardioChallengeDay = 0;
        }
        passed.Add("allKinds=5");
        Debug.Log("GYMCHAOS_CARDIO_CHALLENGE_KINDS_OK kinds=5 holdReset=1 paceReachable=1");
    }

    private static void Finish(int code)
    {
        finished = true;
        resultCode = code;
        if (code == 0) Debug.Log("GYMCHAOS_CARDIO_GATE_OK " + string.Join(" ", passed));
        GymChaosVerifierExit.Record(resultCode);
        SessionState.EraseBool(RequestedKey);
        EditorApplication.isPlaying = false;
    }
}
#endif
