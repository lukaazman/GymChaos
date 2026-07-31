using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

public static class BuildScript
{
    public static void BuildWebGL()
    {
        // GitHub Pages does not add Content-Encoding: br for Unity's .br files.
        // Emit uncompressed files so the browser can load the build directly.
        PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Disabled;

        var outputPath = Environment.GetEnvironmentVariable("UNITY_BUILD_PATH");
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            outputPath = Path.Combine("Build", "WebGL");
        }

        Directory.CreateDirectory(outputPath);

        var scenes = new[] { "Assets/Scenes/SampleScene.unity" };
        var options = new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = outputPath,
            target = BuildTarget.WebGL,
            options = BuildOptions.None
        };

        var report = BuildPipeline.BuildPlayer(options);
        if (report.summary.result != BuildResult.Succeeded)
        {
            throw new BuildFailedException($"WebGL build failed with result: {report.summary.result}");
        }
    }
}
