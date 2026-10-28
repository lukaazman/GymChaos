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
        // Brotli cuts the download; GitHub Pages does not add Content-Encoding: br
        // for Unity's .br files, so the loader decompresses them itself.
        // Local smoke builds set GYMCHAOS_WEBGL_COMPRESSION=none to skip the
        // Brotli pass over the data file (about 8 of 13 build minutes).
        bool uncompressed = Environment.GetEnvironmentVariable("GYMCHAOS_WEBGL_COMPRESSION") == "none";
        PlayerSettings.WebGL.compressionFormat =
            uncompressed ? WebGLCompressionFormat.Disabled : WebGLCompressionFormat.Brotli;
        PlayerSettings.WebGL.decompressionFallback = true;

        var outputPath = Environment.GetEnvironmentVariable("UNITY_BUILD_PATH");
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            outputPath = Path.Combine("Build", "WebGL");
        }

        Directory.CreateDirectory(outputPath);

        string streamingAssetsRoot = Path.Combine(Application.dataPath, "StreamingAssets");
        string[] requiredStreamingFiles =
        {
            Path.Combine("BodyBuilders", "manwithsuit1.glb"),
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

        DeleteLegacySoundCloudArtifact(
            Path.Combine(outputPath, "soundcloud-callback.html"));
        DeleteLegacySoundCloudArtifact(
            Path.Combine(outputPath, "soundcloud-config.json"));
        File.WriteAllText(Path.Combine(outputPath, "gymchaos-runtime.json"),
            "{\"runtimeBootstrap\":true,\"scene\":\"SampleScene\",\"features\":[\"mirrors\",\"characters\",\"exercises\",\"combat\",\"pickups\",\"pointer-lock\",\"soundcloud-widget\",\"day-night\",\"tv-screen\",\"webgl-colliders\",\"jay-cutler\",\"goku-flight\"]}");
    }

    private static void DeleteLegacySoundCloudArtifact(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
