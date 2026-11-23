#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// Compiles the runtime scripts the way a player build does (no UNITY_EDITOR),
/// so editor-only members used from runtime code fail here and not in the CI
/// WebGL build. Scripts only: no assets, no player output kept.
/// Logs GYMCHAOS_PLAYER_SCRIPTS_OK or GYMCHAOS_PLAYER_SCRIPTS_FAILED.
/// </summary>
public static class GymChaosPlayerScriptCheck
{
    public static void Run()
    {
        int code = 1;
        try
        {
            var options = new BuildPlayerOptions
            {
                scenes = new[] { "Assets/Scenes/SampleScene.unity" },
                locationPathName = "Temp/PlayerScriptCheck/GymChaos.exe",
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.BuildScriptsOnly
            };
            BuildReport report = BuildPipeline.BuildPlayer(options);
            // Scripts-only builds always end "Failure" with "Can't verify script
            // data layout" (no data build ran); only a C# error is a real failure.
            string errors = report.SummarizeErrors();
            bool ok = report.summary.result == BuildResult.Succeeded ||
                (errors.Contains("script data layout") && !errors.Contains("error CS"));
            Debug.Log(ok
                ? "GYMCHAOS_PLAYER_SCRIPTS_OK"
                : "GYMCHAOS_PLAYER_SCRIPTS_FAILED " + errors);
            code = ok ? 0 : 1;
        }
        catch (Exception exception)
        {
            Debug.LogError("GYMCHAOS_PLAYER_SCRIPTS_FAILED " + exception.Message);
        }
        EditorApplication.Exit(code);
    }
}
#endif
