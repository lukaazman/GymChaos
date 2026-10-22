#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Ronnie dies outside, Larry revives him there, and he walks back into the
/// gym. Every frame he moves on the way in must play a walk or run clip with
/// the same stride as indoors, facing the way he walks: no gliding over the
/// ground in a frozen first frame of the clip.
/// Logs GYMCHAOS_RONNIE_REVIVE_OK.
/// </summary>
public static class GymChaosRonnieReviveVerifier
{
    private const string RequestedKey = "GymChaos.RonnieReviveVerificationRequested";
    private static IEnumerator routine;
    private static readonly Stack<IEnumerator> stack = new Stack<IEnumerator>();
    private static readonly StringBuilder report = new StringBuilder();

    [InitializeOnLoadMethod]
    private static void ResumeAfterReload()
    {
        if (!SessionState.GetBool(RequestedKey, false)) return;
        Hook();
    }

    public static void Run()
    {
        GymChaosVerifierSave.Neutralize();
        SessionState.SetBool(RequestedKey, true);
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        Hook();
        EditorApplication.isPlaying = true;
    }

    private static void Hook()
    {
        routine = null;
        report.Length = 0;
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
        EditorApplication.playModeStateChanged -= Changed;
        EditorApplication.playModeStateChanged += Changed;
    }

    private static void Changed(PlayModeStateChange change)
    {
        if (change == PlayModeStateChange.EnteredPlayMode)
        {
            GymChaosVerifierClock.BeginFixedStep();
            stack.Clear();
            routine = Scenario();
        }
        if (change != PlayModeStateChange.EnteredEditMode) return;
        EditorApplication.update -= Tick;
        GymChaosVerifierClock.EndFixedStep();
        GymChaosVerifierSave.Restore();
        if (Application.isBatchMode && SessionState.GetBool(RequestedKey, false)) EditorApplication.Exit(1);
    }

    private sealed class VerificationFailure : System.Exception
    {
        public VerificationFailure(string message) : base(message) { }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new VerificationFailure(message);
    }

    private static void Tick()
    {
        if (!EditorApplication.isPlaying || routine == null) return;
        try
        {
            if (stack.Count == 0) stack.Push(routine);
            while (stack.Count > 0)
            {
                IEnumerator top = stack.Peek();
                if (!top.MoveNext())
                {
                    stack.Pop();
                    continue;
                }
                if (top.Current is IEnumerator nested)
                {
                    stack.Push(nested);
                    continue;
                }
                return;
            }
            Finish(0, report.ToString());
        }
        catch (VerificationFailure failure)
        {
            Finish(1, failure.Message + " | " + report);
        }
        catch (System.Exception exception)
        {
            Finish(1, "exception " + exception + " | " + report);
        }
    }

    private static IEnumerator Wait(float seconds)
    {
        float until = Time.time + seconds;
        while (Time.time < until) yield return null;
    }

    private static IEnumerator Scenario()
    {
        float started = Time.realtimeSinceStartup;
        EnemyFighter ronnie = null;
        PlayerMovement player = null;
        while (ronnie == null || player == null || GymFixerDirector.Instance == null ||
               GymDoorway.Instance == null || !GymOutdoorBuilder.IsBuilt)
        {
            if (Time.realtimeSinceStartup - started > 120f) throw new VerificationFailure("scene not ready");
            player = Object.FindFirstObjectByType<PlayerMovement>();
            foreach (EnemyFighter fighter in Object.FindObjectsByType<EnemyFighter>(FindObjectsSortMode.None))
                if (fighter.Identity == BodybuilderIdentity.Ronnie && !fighter.IsDead) ronnie = fighter;
            yield return null;
        }
        yield return Wait(3f);
        // Baseline: his normal walk inside the gym before anything happens.
        float baselineSwing = 0f;
        {
            MixamoScanRetargetAnimator a0 = ronnie.GetComponentInChildren<MixamoScanRetargetAnimator>(true);
            Transform f0 = FindFoot(a0);
            Rigidbody b0 = ronnie.GetComponent<Rigidbody>();
            float mn = float.MaxValue, mx = float.MinValue;
            int frames = 0;
            Vector3 l0 = b0.position;
            float until0 = Time.time + 8f;
            while (Time.time < until0)
            {
                yield return null;
                float sp = Vector3.ProjectOnPlane(b0.position - l0, Vector3.up).magnitude / Mathf.Max(Time.deltaTime, 1e-4f);
                l0 = b0.position;
                if (sp < 0.6f || f0 == null) continue;
                frames++;
                float st = Vector3.Dot(f0.position - ronnie.transform.position, ronnie.transform.forward);
                mn = Mathf.Min(mn, st); mx = Mathf.Max(mx, st);
            }
            baselineSwing = frames > 0 ? mx - mn : 0f;
            report.Append($"baselineFrames={frames} baselineSwing={baselineSwing:F2} ");
        }

        // Out on the forecourt, a few metres from the door.
        GymDoorway doorway = GymDoorway.Instance;
        Vector3 outward = Vector3.ProjectOnPlane(doorway.ExteriorPoint - doorway.InteriorPoint, Vector3.up).normalized;
        Vector3 outside = doorway.ExteriorPoint + outward * 7f;
        Rigidbody body = ronnie.GetComponent<Rigidbody>();
        body.linearVelocity = Vector3.zero;
        body.position = new Vector3(outside.x, ronnie.transform.position.y, outside.z);
        ronnie.transform.position = body.position;
        Physics.SyncTransforms();
        yield return Wait(0.5f);
        Require(GymOutdoorBuilder.IsPlayerOutsideGym(body.position), $"Ronnie not outside ({body.position})");

        Vector3 push = Vector3.ProjectOnPlane(ronnie.transform.position - player.transform.position, Vector3.up);
        ronnie.TakeThrowableHit((push.sqrMagnitude > 0.01f ? push.normalized : Vector3.forward) * 4f, 100000f, 0f, false);
        Require(ronnie.IsDead, "Ronnie did not die");
        float settle = Time.time + 20f;
        while (!ronnie.IsSettledCorpse)
        {
            Require(Time.time < settle, "Ronnie's corpse never settled");
            yield return null;
        }
        Require(GymOutdoorBuilder.IsPlayerOutsideGym(ronnie.VisitorPhysicsPosition), "corpse not outside");

        GymFixerDirector.Instance.ReviveForVerification(ronnie);
        float revive = Time.time + 5f;
        while (ronnie.IsDead)
        {
            Require(Time.time < revive, "Larry's revive never happened");
            yield return null;
        }
        Require(GymOutdoorBuilder.IsPlayerOutsideGym(body.position), "Ronnie was revived inside, not where he fell");
        report.Append($"revivedAt={body.position} ");

        MixamoScanRetargetAnimator animator = ronnie.GetComponentInChildren<MixamoScanRetargetAnimator>(true);
        Require(animator != null, "Ronnie has no animator");
        Transform foot = FindFoot(animator);

        // Walk back in: sample until he is through the door.
        int movingFrames = 0;
        int glideFrames = 0;
        float yawOffsetSum = 0f, travelOffsetSum = 0f, visualMin = float.MaxValue, visualMax = float.MinValue;
        HashSet<string> clips = new HashSet<string>();
        Vector3 last = body.position;
        float walk = Time.time + 40f;
        float walkStart = Time.time;
        StringBuilder glideAt = new StringBuilder();
        while (GymOutdoorBuilder.IsPlayerOutsideGym(body.position))
        {
            Require(Time.time < walk, $"Ronnie never walked back in ({body.position}) clips={string.Join(",", clips)}");
            yield return null;
            float dt = Mathf.Max(Time.deltaTime, 1e-4f);
            float speed = Vector3.ProjectOnPlane(body.position - last, Vector3.up).magnitude / dt;
            last = body.position;
            string clip = animator.CurrentAnimationClipName ?? "none";
            clips.Add(clip);
            // The first half second is the start from standing still.
            if (speed < 0.6f || Time.time - walkStart < 0.5f) continue;
            movingFrames++;
            string lower = clip.ToLowerInvariant();
            bool gait = lower.Contains("walk") || lower.Contains("run") || lower.Contains("jog");
            if (!gait)
            {
                glideFrames++;
                if (glideAt.Length < 200) glideAt.Append($"{Time.time - walkStart:F2}@{speed:F1}/{clip} ");
            }
            yawOffsetSum += Mathf.Abs(Mathf.DeltaAngle(0f, Vector3.SignedAngle(ronnie.transform.forward,
                Vector3.ProjectOnPlane(animator.transform.forward, Vector3.up), Vector3.up)));
            Vector3 travel = Vector3.ProjectOnPlane(body.linearVelocity, Vector3.up);
            if (travel.sqrMagnitude > 0.1f)
            {
                travelOffsetSum += Mathf.Abs(Vector3.SignedAngle(travel, Vector3.ProjectOnPlane(animator.transform.forward, Vector3.up), Vector3.up));
            }
            if (foot != null)
            {
                float visualStride = Vector3.Dot(foot.position - ronnie.transform.position, animator.transform.forward);
                visualMin = Mathf.Min(visualMin, visualStride);
                visualMax = Mathf.Max(visualMax, visualStride);
            }
        }
        report.Append($"glideAt=[{glideAt}] ");
        report.Append($"meanVisualYaw={yawOffsetSum / Mathf.Max(1, movingFrames):F0} meanTravelVsVisual={travelOffsetSum / Mathf.Max(1, movingFrames):F0} visualSwing={visualMax - visualMin:F2} ");
        report.Append($"moving={movingFrames} glide={glideFrames} clips={string.Join(",", clips)}");
        Require(movingFrames > 20, $"Ronnie barely moved on the way in ({movingFrames} frames)");
        Require(glideFrames <= movingFrames / 20, $"{glideFrames}/{movingFrames} moving frames without a walk clip");
        // The walk must be the same stride as inside, facing the way he goes.
        Require(foot == null || visualMax - visualMin > baselineSwing * 0.7f,
            $"feet barely step on the way in ({visualMax - visualMin:F2} m vs {baselineSwing:F2} m inside)");
        Require(travelOffsetSum / Mathf.Max(1, movingFrames) < 30f,
            $"walks sideways ({travelOffsetSum / Mathf.Max(1, movingFrames):F0} deg off his facing)");
    }

    private static Transform FindFoot(MixamoScanRetargetAnimator animator)
    {
        if (animator == null) return null;
        foreach (Transform bone in animator.GetComponentsInChildren<Transform>(true))
        {
            string name = bone.name.ToLowerInvariant();
            if (name.Contains("foot") && (name.Contains("left") || name.Contains(".l") || name.Contains("_l")))
                return bone;
        }
        return null;
    }

    private static void Finish(int code, string detail)
    {
        if (code == 0) Debug.Log("GYMCHAOS_RONNIE_REVIVE_OK " + detail);
        else Debug.LogError("GYMCHAOS_RONNIE_REVIVE_FAILED " + detail);
        SessionState.EraseBool(RequestedKey);
        EditorApplication.update -= Tick;
        routine = null;
        GymChaosVerifierClock.EndFixedStep();
        GymChaosVerifierSave.Restore();
        if (Application.isBatchMode)
        {
            GymChaosVerifierExit.Record(code);
            EditorApplication.Exit(code);
        }
        else
        {
            EditorApplication.isPlaying = false;
        }
    }
}
#endif
