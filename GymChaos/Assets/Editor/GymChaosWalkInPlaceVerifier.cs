#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// A member that keeps walking into a wall must idle, not play the walk cycle
/// on the spot; the same member walking into open floor must still walk.
/// Drives the normal roam movement through EnemyFighter's editor hook.
/// </summary>
[InitializeOnLoad]
public static class GymChaosWalkInPlaceVerifier
{
    private const string RequestedKey = "GymChaos.WalkInPlaceVerificationRequested";
    private const float SettleSeconds = 1.2f;
    private const float WindowSeconds = 1.0f;
    private enum Stage { Find, Blocked, Open }
    private static Stage stage;
    private static EnemyFighter fighter;
    private static Vector3 wallDirection;
    private static float stageStartedAt;
    private static Vector3 windowStart;
    private static bool windowStarted;
    private static double startedAt;
    private static bool finished;
    private static int resultCode;

    static GymChaosWalkInPlaceVerifier()
    {
        if (!SessionState.GetBool(RequestedKey, false)) return;
        Hook();
        EditorApplication.delayCall += () =>
        {
            if (EditorApplication.isPlaying) startedAt = EditorApplication.timeSinceStartup;
        };
    }

    [MenuItem("Tools/GymChaos/Run Walk In Place Verification")]
    public static void Run()
    {
        finished = false;
        resultCode = 1; GymChaosVerifierExit.Record(resultCode);
        stage = Stage.Find;
        fighter = null;
        SessionState.SetBool(RequestedKey, true);
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        Hook();
        EditorApplication.isPlaying = true;
    }

    private static void Hook()
    {
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }

    private static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            startedAt = EditorApplication.timeSinceStartup;
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
        double elapsed = EditorApplication.timeSinceStartup - startedAt;
        try
        {
            if (elapsed > 90d) throw new InvalidOperationException($"timed out in stage {stage}");
            switch (stage)
            {
                case Stage.Find:
                    if (elapsed < 4d || !GymInteriorBuilder.TryGetMainGymBounds(out Bounds gym)) return;
                    if (!TrySetUp(gym)) return;
                    stage = Stage.Blocked;
                    BeginWindow();
                    return;
                case Stage.Blocked:
                    if (!TickWindow(out float blockedMoved, out var blockedState)) return;
                    Require(blockedMoved < 0.2f,
                        $"member was not held by the wall: moved={blockedMoved:F2}");
                    Require(blockedState != MixamoScanRetargetAnimator.MotionState.Running,
                        $"member pushing into a wall plays {blockedState} (walk in place), moved={blockedMoved:F2}");
                    Debug.Log($"GYMCHAOS_WALK_IN_PLACE_BLOCKED_OK identity={fighter.Identity} " +
                        $"moved={blockedMoved:F2} state={blockedState}");
                    fighter.verificationForcedRoamDirection = -wallDirection;
                    stage = Stage.Open;
                    BeginWindow();
                    return;
                case Stage.Open:
                    if (!TickWindow(out float openMoved, out var openState)) return;
                    Require(openMoved > 0.4f, $"member did not walk into open floor: moved={openMoved:F2}");
                    Require(openState == MixamoScanRetargetAnimator.MotionState.Running,
                        $"member walking into open floor plays {openState}, moved={openMoved:F2}");
                    fighter.verificationForcedRoamDirection = Vector3.zero;
                    Debug.Log($"GYMCHAOS_WALK_IN_PLACE_OK identity={fighter.Identity} " +
                        $"openMoved={openMoved:F2} openState={openState}");
                    Finish(0);
                    return;
            }
        }
        catch (Exception exception)
        {
            Debug.LogError("GYMCHAOS_WALK_IN_PLACE_FAILED " + exception.Message);
            Finish(1);
        }
    }

    // A plain member inside the gym, parked 0.75 m in front of a static wall.
    private static bool TrySetUp(Bounds gym)
    {
        foreach (EnemyFighter candidate in UnityEngine.Object.FindObjectsByType<EnemyFighter>(
                     FindObjectsSortMode.None))
        {
            if (candidate == null || candidate.IsDead || candidate.IsAggressive ||
                candidate.GetComponent<Rigidbody>() == null ||
                candidate.Identity == BodybuilderIdentity.Goku ||
                candidate.Identity == BodybuilderIdentity.Ronnie ||
                candidate.Identity == BodybuilderIdentity.Manwithsuit1 ||
                candidate.Identity == BodybuilderIdentity.Mark ||
                candidate.MotionStateForVerification ==
                    MixamoScanRetargetAnimator.MotionState.Uninitialized) continue;
            Vector3 p = candidate.transform.position;
            if (p.x < gym.min.x || p.x > gym.max.x || p.z < gym.min.z || p.z > gym.max.z) continue;
            fighter = candidate;
            break;
        }
        if (fighter == null) return false;

        float floorY = fighter.transform.position.y;
        Vector3 from = new Vector3(gym.center.x, floorY + 1.1f, gym.center.z);
        Vector3[] directions = { Vector3.forward, Vector3.right, Vector3.back, Vector3.left };
        foreach (Vector3 direction in directions)
        {
            foreach (RaycastHit hit in Physics.RaycastAll(
                         from, direction, 40f, ~0, QueryTriggerInteraction.Ignore))
            {
                // A long static wall face: no rigidbody, roughly facing the ray.
                if (hit.collider.attachedRigidbody != null || hit.distance < 3f ||
                    Vector3.Dot(hit.normal, -direction) < 0.9f ||
                    hit.collider.bounds.size.y < 2f) continue;
                Vector3 spot = hit.point - direction * 0.75f;
                spot.y = floorY;
                // Free side lane back toward the centre for the open stage.
                if (Physics.Raycast(spot + Vector3.up * 1.1f, -direction, 2.5f, ~0,
                        QueryTriggerInteraction.Ignore)) continue;
                Place(fighter.transform, spot, Quaternion.LookRotation(direction));
                wallDirection = direction;
                fighter.verificationForcedRoamDirection = direction;
                return true;
            }
        }
        throw new InvalidOperationException("no wall with a clear 2.5 m lane was found");
    }

    private static void BeginWindow()
    {
        stageStartedAt = Time.time;
        windowStarted = false;
    }

    private static bool TickWindow(out float moved, out MixamoScanRetargetAnimator.MotionState state)
    {
        moved = 0f;
        state = fighter.MotionStateForVerification;
        float stageTime = Time.time - stageStartedAt;
        if (!windowStarted)
        {
            if (stageTime < SettleSeconds) return false;
            windowStarted = true;
            windowStart = fighter.transform.position;
            return false;
        }
        if (stageTime < SettleSeconds + WindowSeconds) return false;
        moved = Vector3.ProjectOnPlane(fighter.transform.position - windowStart, Vector3.up).magnitude;
        return true;
    }

    private static void Place(Transform target, Vector3 position, Quaternion rotation)
    {
        Rigidbody body = target.GetComponent<Rigidbody>();
        if (body != null)
        {
            body.linearVelocity = Vector3.zero;
            body.position = position;
            body.rotation = rotation;
        }
        target.SetPositionAndRotation(position, rotation);
        Physics.SyncTransforms();
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void Finish(int code)
    {
        if (finished) return;
        finished = true;
        if (fighter != null) fighter.verificationForcedRoamDirection = Vector3.zero;
        resultCode = code; GymChaosVerifierExit.Record(resultCode);
        SessionState.EraseBool(RequestedKey);
        EditorApplication.update -= Tick;
        EditorApplication.isPlaying = false;
    }
}
#endif
