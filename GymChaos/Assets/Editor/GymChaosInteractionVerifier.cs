#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Fast edit-mode checks (no scene, no play mode) for interaction rules:
/// the rep timing ring scores what it shows, and rewards rank by quality.
/// </summary>
public static class GymChaosInteractionVerifier
{
    public static void Run()
    {
        int code = 0;
        // Batch runs save the open scene as the Editor's last scene; keep the
        // game scene there so the next Editor start is not "Untitled".
        UnityEditor.SceneManagement.EditorSceneManager.OpenScene(GymChaosPlayStartScene.ScenePath);
        try
        {
            VerifyTechniqueRing();
            VerifyContextPromptPriority();
            Debug.Log("GYMCHAOS_INTERACTION_OK");
        }
        catch (Exception exception)
        {
            Debug.LogError("GYMCHAOS_INTERACTION_FAILED " + exception.Message);
            code = 1;
        }
        if (Application.isBatchMode)
        {
            EditorApplication.Exit(code);
        }
    }

    private static void VerifyTechniqueRing()
    {
        // Several rank/difficulty mixes, including a fresh mastery-0 player.
        (int rank, float difficulty)[] cases = { (0, 1f), (0, 1.3f), (4, 0.9f), (10, 1f) };
        foreach ((int rank, float difficulty) in cases)
        {
            var check = new TechniqueSkillCheck();
            check.Begin(rank, difficulty);
            TechniqueSkillCheckTextures.GetRing(check.PerfectHalfAngle, check.AcceptableHalfAngle);
            int perfect = 0, good = 0, miss = 0;
            for (int angle = 0; angle < 360; angle++)
            {
                // Skip the antialiasing seam right at each boundary.
                float distance = Mathf.Abs(Mathf.DeltaAngle(angle, 0f));
                if (Mathf.Abs(distance - check.PerfectHalfAngle) < 1.5f ||
                    Mathf.Abs(distance - check.AcceptableHalfAngle) < 1.5f) continue;

                check.Begin(rank, difficulty);
                check.SetMarkerAngleForVerification(angle);
                WorkoutResult result = check.Resolve();
                WorkoutResult shown = ColourResult(TechniqueSkillCheckTextures.SampleForVerification(angle));
                if (result != shown)
                    throw new InvalidOperationException(
                        $"ring shows {shown} but scores {result} at {angle} deg " +
                        $"(rank={rank} difficulty={difficulty} perfect={check.PerfectHalfAngle:F1} " +
                        $"good={check.AcceptableHalfAngle:F1})");
                if (result == WorkoutResult.Perfect) perfect++;
                else if (result == WorkoutResult.Good) good++;
                else miss++;
            }
            if (perfect == 0 || good == 0 || miss == 0)
                throw new InvalidOperationException($"ring lacks a zone: perfect={perfect} good={good} miss={miss}");
        }

        // A press, as the station delivers it, on each visible zone of a fresh
        // mastery-0 check: green -> Perfect, amber -> Good, grey -> Miss.
        WorkoutResult PressAt(float angle)
        {
            var press = new TechniqueSkillCheck();
            press.Begin(0, 1f);
            press.SetMarkerAngleForVerification(angle);
            return press.Tick(0f, true);
        }
        var probe = new TechniqueSkillCheck();
        probe.Begin(0, 1f);
        float amber = (probe.PerfectHalfAngle + probe.AcceptableHalfAngle) * 0.5f;
        WorkoutResult onGreen = PressAt(0f), onAmber = PressAt(amber), onAmberLeft = PressAt(360f - amber),
            onGrey = PressAt(180f);
        if (onGreen != WorkoutResult.Perfect || onAmber != WorkoutResult.Good ||
            onAmberLeft != WorkoutResult.Good || onGrey != WorkoutResult.Miss)
            throw new InvalidOperationException(
                $"press results green={onGreen} amber={onAmber}/{onAmberLeft} grey={onGrey}");
        Debug.Log($"GYMCHAOS_TECHNIQUE_PRESS_OK green={onGreen}:{GymExperienceService.GetRepReward(onGreen)}xp " +
            $"amber={onAmber}:{GymExperienceService.GetRepReward(onAmber)}xp " +
            $"grey={onGrey}:{GymExperienceService.GetRepReward(onGrey)}xp");

        int perfectXp = GymExperienceService.GetRepReward(WorkoutResult.Perfect);
        int goodXp = GymExperienceService.GetRepReward(WorkoutResult.Good);
        int missXp = GymExperienceService.GetRepReward(WorkoutResult.Miss);
        if (!(perfectXp > goodXp && goodXp > missXp && missXp > 0))
            throw new InvalidOperationException($"rep rewards do not rank: {perfectXp}/{goodXp}/{missXp}");
        Debug.Log($"GYMCHAOS_TECHNIQUE_RING_OK rewards={perfectXp}/{goodXp}/{missXp}");
    }

    private static void VerifyContextPromptPriority()
    {
        // (talk, backRoom, drop, pickup, radio, workout) -> expected E, F
        void Expect(bool talk, bool backRoom, bool drop, bool pickup, bool radio, bool workout,
            PlayerMovement.ContextAction e, PlayerMovement.ContextAction f)
        {
            var gotE = PlayerMovement.ResolveContextAction(PlayerMovement.ContextKey.E, talk, backRoom, drop, pickup, radio, workout);
            var gotF = PlayerMovement.ResolveContextAction(PlayerMovement.ContextKey.F, talk, backRoom, drop, pickup, radio, workout);
            if (gotE != e || gotF != f)
                throw new InvalidOperationException(
                    $"prompt priority talk={talk} backRoom={backRoom} drop={drop} pickup={pickup} radio={radio} " +
                    $"workout={workout}: got E={gotE} F={gotF}, expected E={e} F={f}");
        }
        var A = PlayerMovement.ContextAction.None;
        // Member + machine + pickup: talk on E, workout on F, no pickup prompt.
        Expect(true, false, false, true, false, true, PlayerMovement.ContextAction.Talk, PlayerMovement.ContextAction.Workout);
        // Machine + pickup: pickup on E, workout on F.
        Expect(false, false, false, true, false, true, PlayerMovement.ContextAction.Pickup, PlayerMovement.ContextAction.Workout);
        // Member + pickup: talk wins the shared E.
        Expect(true, false, false, true, false, false, PlayerMovement.ContextAction.Talk, A);
        // Workout beats the radio on the shared F.
        Expect(false, false, false, false, true, true, A, PlayerMovement.ContextAction.Workout);
        Expect(false, false, false, false, true, false, A, PlayerMovement.ContextAction.Radio);
        Debug.Log("GYMCHAOS_CONTEXT_PROMPT_PRIORITY_OK");
    }

    private static WorkoutResult ColourResult(Color colour)
    {
        if (colour.g > 0.8f && colour.r < 0.5f) return WorkoutResult.Perfect;
        if (colour.r > 0.9f && colour.g > 0.5f && colour.b < 0.4f) return WorkoutResult.Good;
        return WorkoutResult.Miss;
    }
}
#endif
