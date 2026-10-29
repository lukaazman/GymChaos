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

        GuardFullscreenButton(Path.Combine(outputPath, "index.html"));
        DeleteLegacySoundCloudArtifact(
            Path.Combine(outputPath, "soundcloud-callback.html"));
        DeleteLegacySoundCloudArtifact(
            Path.Combine(outputPath, "soundcloud-config.json"));
        File.WriteAllText(Path.Combine(outputPath, "gymchaos-runtime.json"),
            "{\"runtimeBootstrap\":true,\"scene\":\"SampleScene\",\"features\":[\"mirrors\",\"characters\",\"exercises\",\"combat\",\"pickups\",\"pointer-lock\",\"soundcloud-widget\",\"day-night\",\"tv-screen\",\"webgl-colliders\",\"jay-cutler\",\"goku-flight\"]}");
    }

    // The default template's fullscreen button calls unityInstance.SetFullscreen,
    // which throws "Fullscreen request denied" when the browser refuses and the
    // loader turns that into a fatal error page. The browser API with a caught
    // rejection does the same job and a refusal just leaves the page windowed.
    private const string TemplateFullscreenCall = "unityInstance.SetFullscreen(1);";
    private const string GuardedFullscreenCall =
        "var request = canvas.requestFullscreen || canvas.webkitRequestFullscreen;" +
        " if (request) { try { Promise.resolve(request.call(canvas)).catch(function () {}); } catch (e) {} }";

    private static void GuardFullscreenButton(string indexPath)
    {
        string html = File.ReadAllText(indexPath);
        if (!html.Contains(TemplateFullscreenCall))
        {
            throw new BuildFailedException(
                $"WebGL template changed: '{TemplateFullscreenCall}' not found in {indexPath}; " +
                "re-check the fullscreen guard.");
        }
        File.WriteAllText(indexPath, html.Replace(TemplateFullscreenCall, GuardedFullscreenCall));
        Debug.Log("GYMCHAOS_WEBGL_FULLSCREEN_GUARD_OK");
    }

    private static void DeleteLegacySoundCloudArtifact(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
