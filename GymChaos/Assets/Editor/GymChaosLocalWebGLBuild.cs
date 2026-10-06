#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Local batch entry for the WebGL build: runs the same BuildScript.BuildWebGL
/// that CI uses, then exits, so Tools/Invoke-UnityCheck.ps1 returns
/// (BuildWebGL itself leaves batch Unity running, which CI's runner expects).
/// Set UNITY_BUILD_PATH to choose the output folder.
/// </summary>
public static class GymChaosLocalWebGLBuild
{
    public static void Run()
    {
        int code = 0;
        try
        {
            BuildScript.BuildWebGL();
            Debug.Log("GYMCHAOS_LOCAL_WEBGL_BUILD_OK");
        }
        catch (Exception exception)
        {
            Debug.LogError("GYMCHAOS_LOCAL_WEBGL_BUILD_FAILED " + exception.Message);
            code = 1;
        }
        if (Application.isBatchMode)
        {
            EditorApplication.Exit(code);
        }
    }
}
#endif
