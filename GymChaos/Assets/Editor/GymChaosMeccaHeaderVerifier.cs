#if UNITY_EDITOR
using System;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// The FITNESS KING header over the Mecca poster wall: the lettering sits
/// inside the white header, and no two painted faces share a plane (the
/// yellow walls and the header used to overlap at the same depth and
/// flickered).
/// </summary>
public static class GymChaosMeccaHeaderVerifier
{
    private const string RequestedKey = "GymChaos.MeccaHeaderVerificationRequested";
    // Faces closer than this in depth count as one plane.
    private const float PlaneTolerance = 0.002f;
    private const float TextMargin = 0.05f;

    private static double startedAt;
    private static bool finished;
    private static int resultCode = 1;

    [InitializeOnLoadMethod]
    private static void ResumeAfterReload()
    {
        if (SessionState.GetBool(RequestedKey, false)) Hook();
    }

    public static void Run()
    {
        finished = false;
        resultCode = 1;
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
            GymChaosVerifierClock.BeginFixedStep();
            startedAt = GymChaosVerifierClock.Now;
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
        double now = GymChaosVerifierClock.Now;
        GameObject header = GameObject.Find("Mecca white header");
        if (header == null || now - startedAt < 3d)
        {
            if (now - startedAt > 120d) Finish(1, "GYMCHAOS_MECCA_HEADER_FAILED header never built");
            return;
        }
        try
        {
            Check(header.GetComponent<Renderer>());
            Debug.Log("GYMCHAOS_MECCA_HEADER_OK center=" + header.GetComponent<Renderer>().bounds.center + " size=" + header.GetComponent<Renderer>().bounds.size);
            Finish(0, null);
        }
        catch (Exception exception)
        {
            Finish(1, "GYMCHAOS_MECCA_HEADER_FAILED " + exception.Message);
        }
    }

    private static void Check(Renderer header)
    {
        Bounds box = header.bounds;
        TextMesh title = null;
        foreach (TextMesh text in UnityEngine.Object.FindObjectsByType<TextMesh>(FindObjectsSortMode.None))
            if (text.text == "FITNESS KING") title = text;
        if (title == null) throw new InvalidOperationException("FITNESS KING lettering missing");
        Bounds letters = title.GetComponent<Renderer>().bounds;
        if (letters.min.y < box.min.y + TextMargin || letters.max.y > box.max.y - TextMargin)
            throw new InvalidOperationException(
                $"lettering y {letters.min.y:F2}..{letters.max.y:F2} not inside header {box.min.y:F2}..{box.max.y:F2}");
        if (letters.min.x < box.min.x + TextMargin || letters.max.x > box.max.x - TextMargin)
            throw new InvalidOperationException(
                $"lettering x {letters.min.x:F2}..{letters.max.x:F2} not inside header {box.min.x:F2}..{box.max.x:F2}");
        // Fills the header up to its margins: the limiting side within 15 %.
        float fillX = letters.size.x / Mathf.Max(0.01f, box.size.x - 2f * TextMargin);
        float fillY = letters.size.y / Mathf.Max(0.01f, box.size.y - 2f * TextMargin);
        Debug.Log($"GYMCHAOS_MECCA_HEADER_FILL x={fillX:F2} y={fillY:F2}");
        if (Mathf.Max(fillX, fillY) < 0.85f)
            throw new InvalidOperationException($"lettering too small: fills {fillX:P0} of the width, {fillY:P0} of the height");

        // Coplanar faces: another painted box that overlaps the header in
        // height and spans its front face depth.
        float frontZ = box.min.z;
        StringBuilder clash = new StringBuilder();
        foreach (Renderer other in UnityEngine.Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
        {
            if (other == header || other.name.StartsWith("Mecca personality", StringComparison.Ordinal)) continue;
            Bounds b = other.bounds;
            bool sameFace = Mathf.Abs(b.min.z - frontZ) < PlaneTolerance || Mathf.Abs(b.max.z - box.max.z) < PlaneTolerance;
            bool overlapsHeight = Mathf.Min(b.max.y, box.max.y) - Mathf.Max(b.min.y, box.min.y) > PlaneTolerance;
            bool overlapsWidth = Mathf.Min(b.max.x, box.max.x) - Mathf.Max(b.min.x, box.min.x) > PlaneTolerance;
            if (sameFace && overlapsHeight && overlapsWidth)
                clash.Append(other.name).Append(" y=").Append(b.min.y.ToString("F2")).Append("..")
                    .Append(b.max.y.ToString("F2")).Append("; ");
        }
        if (clash.Length > 0) throw new InvalidOperationException("coplanar with header: " + clash);
    }

    private static void Finish(int code, string error)
    {
        if (error != null) Debug.LogError(error);
        finished = true;
        resultCode = code;
        GymChaosVerifierExit.Record(code);
        GymChaosVerifierClock.EndFixedStep();
        if (Application.isBatchMode) GymChaosVerifierExit.Exit(code);
        else EditorApplication.isPlaying = false;
    }
}
#endif
