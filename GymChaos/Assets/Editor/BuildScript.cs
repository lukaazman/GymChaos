using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

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

        string streamingAssetsRoot = Path.Combine(Application.dataPath, "StreamingAssets");
        string[] requiredStreamingFiles =
        {
            Path.Combine("BodyBuilders", "arnold.glb"),
            Path.Combine("BodyBuilders", "cbum.glb"),
            Path.Combine("BodyBuilders", "goku.glb"),
            Path.Combine("BodyBuilders", "jay.glb"),
            Path.Combine("BodyBuilders", "manwithsuit1.glb"),
            Path.Combine("BodyBuilders", "ronnie.glb"),
            Path.Combine("BodyBuilders", "zyzz.glb"),
            Path.Combine("Videos", "manwithsuit.mp4")
        };
        for (int i = 0; i < requiredStreamingFiles.Length; i++)
        {
            string requiredPath = Path.Combine(streamingAssetsRoot, requiredStreamingFiles[i]);
            if (!File.Exists(requiredPath))
            {
                throw new BuildFailedException($"Required runtime asset is missing: {requiredPath}");
            }
        }

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

        File.WriteAllText(Path.Combine(outputPath, "gymchaos-runtime.json"),
            "{\"runtimeBootstrap\":true,\"scene\":\"SampleScene\",\"features\":[\"mirrors\",\"characters\",\"exercises\",\"combat\",\"pickups\",\"pointer-lock\",\"day-night\",\"tv-screen\",\"webgl-colliders\",\"jay-cutler\",\"goku-flight\"]}");
    }
}
