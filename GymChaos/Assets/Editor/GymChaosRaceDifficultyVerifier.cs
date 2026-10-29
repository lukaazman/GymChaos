#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Race difficulty (edit mode, no play needed): Easy is the original race,
/// Medium and Hard raise every kart's top speed and the AI skill step by
/// step; a win unlocks the next level. Results keep BACK TO GYM / RACE
/// AGAIN; the difficulty screen after RACE AGAIN (or at the sign) offers one
/// button per unlocked level, and is skipped before the first Easy win.
/// </summary>
public static class GymChaosRaceDifficultyVerifier
{
    [MenuItem("Tools/GymChaos/Run Race Difficulty Verification")]
    public static void Run()
    {
        int code = 0;
        GymRaceHud hud = null;
        try
        {
            Require(GymRaceDirector.DifficultyNames.Length == 3, "three difficulty levels");
            GymRaceKart.Stats easy = GymRaceDirector.ScaleStats(GymRaceKart.Stats.Default, 0);
            Require(Mathf.Approximately(easy.TopSpeed, GymRaceKart.Stats.Default.TopSpeed) &&
                Mathf.Approximately(GymRaceDirector.AiSkillScale(0), 1f), "Easy must be the original race");
            for (int d = 1; d < 3; d++)
            {
                GymRaceKart.Stats lower = GymRaceDirector.ScaleStats(GymRaceKart.Stats.Default, d - 1);
                GymRaceKart.Stats higher = GymRaceDirector.ScaleStats(GymRaceKart.Stats.Default, d);
                Require(higher.TopSpeed > lower.TopSpeed && higher.Acceleration > lower.Acceleration,
                    $"level {d} is not faster than {d - 1}");
                Require(GymRaceDirector.AiSkillScale(d) > GymRaceDirector.AiSkillScale(d - 1),
                    $"level {d} AI is not better than {d - 1}");
            }
            // unlocked, raced at, place -> unlocked after
            int[,] unlocks = { { 0, 0, 1, 1 }, { 0, 0, 2, 0 }, { 1, 1, 1, 2 }, { 1, 0, 1, 1 }, { 2, 2, 1, 2 }, { 1, 1, 3, 1 } };
            for (int i = 0; i < unlocks.GetLength(0); i++)
            {
                int after = GymRaceDirector.UnlockAfterRace(unlocks[i, 0], unlocks[i, 1], unlocks[i, 2]);
                Require(after == unlocks[i, 3],
                    $"unlocked={unlocks[i, 0]} raced={unlocks[i, 1]} place={unlocks[i, 2]} gave {after}, expected {unlocks[i, 3]}");
            }

            hud = GymRaceHud.Create();
            string[] expectedAfter = { "BACK TO GYM|RACE AGAIN", "BACK TO GYM|EASY|MEDIUM", "BACK TO GYM|EASY|MEDIUM|HARD" };
            for (int unlocked = 0; unlocked < 3; unlocked++)
            {
                hud.ShowChoice(unlocked, false);
                Require(Labels(hud) == expectedAfter[unlocked], $"results choice unlocked={unlocked}: {Labels(hud)}");
            }
            hud.ShowChoice(1, true);
            Require(Labels(hud) == "BACK|EASY|MEDIUM", $"start choice: {Labels(hud)}");
            hud.MoveChoice(1);
            hud.MoveChoice(1);
            hud.ConfirmChoice();
            Require(hud.ChoiceMade == GymRaceHud.Choice.RaceAgain && hud.ChosenDifficulty == 1,
                $"keys picked {hud.ChoiceMade}/{hud.ChosenDifficulty}, expected MEDIUM");
            hud.ShowChoice(2, true);
            Require(Labels(hud) == "BACK|EASY|MEDIUM|HARD", $"difficulty screen: {Labels(hud)}");
            Capture(hud);
            Debug.Log("GYMCHAOS_RACE_DIFFICULTY_OK");
        }
        catch (Exception exception)
        {
            Debug.LogError("GYMCHAOS_RACE_DIFFICULTY_FAIL " + exception.Message);
            code = 1;
        }
        finally
        {
            if (hud != null) UnityEngine.Object.DestroyImmediate(hud.gameObject);
        }
        if (Application.isBatchMode) EditorApplication.Exit(code);
    }

    // With a graphics device: Logs/agent/race-difficulty.png of the results row.
    private static void Capture(GymRaceHud hud)
    {
        if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) return;
        Canvas canvas = hud.GetComponentInParent<Canvas>() ?? hud.GetComponentInChildren<Canvas>();
        if (canvas == null) return;
        var cameraObject = new GameObject("Race difficulty capture camera");
        Camera camera = cameraObject.AddComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.08f, 0.1f, 0.14f, 1f);
        var target = new RenderTexture(1920, 1080, 24);
        camera.targetTexture = target;
        canvas.renderMode = RenderMode.ScreenSpaceCamera;
        canvas.worldCamera = camera;
        canvas.planeDistance = 1f;
        Canvas.ForceUpdateCanvases();
        camera.Render();
        RenderTexture.active = target;
        var picture = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
        picture.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0);
        RenderTexture.active = null;
        string directory = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "..", "Logs", "agent");
        System.IO.Directory.CreateDirectory(directory);
        System.IO.File.WriteAllBytes(System.IO.Path.Combine(directory, "race-difficulty.png"), picture.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(picture);
        UnityEngine.Object.DestroyImmediate(target);
        UnityEngine.Object.DestroyImmediate(cameraObject);
    }

    private static string Labels(GymRaceHud hud)
    {
        var labels = new System.Collections.Generic.List<string>();
        for (int i = 0; i < 4; i++)
        {
            Button button = hud.ChoiceButtonForVerification(i);
            if (button.gameObject.activeSelf) labels.Add(button.GetComponentInChildren<Text>().text);
        }
        return string.Join("|", labels);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
#endif
