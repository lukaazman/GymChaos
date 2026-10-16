#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Ronnie intervention and fight-time dialogue (2026-10-16).
// - Before the fight a neutral member next to the player is a talk target.
// - Once the player angers that member, nobody is talkable and an open
//   dialogue would close.
// - Ronnie runs to the nearest fight participant (the member, who stands
//   between him and the player), stops when in reach and punches him,
//   instead of running on the spot or past him.
[InitializeOnLoad]
public static class GymChaosRonnieInterventionVerifier
{
    private const string RequestedKey = "GymChaos.RonnieInterventionVerificationRequested";
    private const float ReachRange = 1.9f + 0.3f;
    private const double FightSeconds = 14d;

    private static double started;
    private static double stageStarted;
    private static int lastFrame;
    private static int stage;
    private static bool finished;
    private static int resultCode;
    private static PlayerMovement player;
    private static EnemyFighter ronnie;
    private static EnemyFighter member;
    private static int attacksBefore;
    private static double firstReach;
    private static int inReachFrames;
    private static int runningInReach;
    private static int wrongTargetFrames;
    private static float closest;
    private static string details = "none";

    static GymChaosRonnieInterventionVerifier()
    {
        if (SessionState.GetBool(RequestedKey, false))
        {
            Hook();
        }
    }

    [MenuItem("Tools/GymChaos/Run Ronnie Intervention Verification")]
    public static void Run()
    {
        SessionState.SetBool(RequestedKey, true);
        GymChaosVerifierSave.Neutralize();
        ResetState();
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        Hook();
        EditorApplication.isPlaying = true;
    }

    private static void Hook()
    {
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
        EditorApplication.playModeStateChanged -= PlayModeChanged;
        EditorApplication.playModeStateChanged += PlayModeChanged;
    }

    private static void ResetState()
    {
        started = EditorApplication.timeSinceStartup;
        stageStarted = started;
        lastFrame = -1;
        stage = 0;
        finished = false;
        resultCode = 1;
        GymChaosVerifierExit.Record(resultCode);
        player = null;
        ronnie = null;
        member = null;
        attacksBefore = 0;
        firstReach = -1d;
        inReachFrames = 0;
        runningInReach = 0;
        wrongTargetFrames = 0;
        closest = float.PositiveInfinity;
        details = "none";
    }

    private static void PlayModeChanged(PlayModeStateChange change)
    {
        if (change == PlayModeStateChange.EnteredPlayMode)
        {
            ResetState();
        }
        if (change != PlayModeStateChange.EnteredEditMode)
        {
            return;
        }
        EditorApplication.update -= Tick;
        EditorApplication.playModeStateChanged -= PlayModeChanged;
        SessionState.EraseBool(RequestedKey);
        GymChaosVerifierSave.Restore();
        if (Application.isBatchMode)
        {
            GymChaosVerifierExit.Exit(resultCode);
        }
    }

    private static void Tick()
    {
        if (finished || !EditorApplication.isPlaying)
        {
            return;
        }
        try
        {
            double now = EditorApplication.timeSinceStartup;
            if (now - started > 150d)
            {
                throw new TimeoutException($"timed out at stage {stage}: {details}");
            }
            if (Time.frameCount == lastFrame)
            {
                return;
            }
            lastFrame = Time.frameCount;
            if (player != null)
            {
                player.RestoreHealth(player.MaxHealth);
            }

            switch (stage)
            {
                case 0:
                    Setup(now);
                    break;
                case 1:
                    // Neutral: the member in front of the player can be talked to.
                    if (now - stageStarted < 0.8d) return;
                    player.LookAtForVerification(member.transform.position + Vector3.up * 1.6f);
                    EnemyFighter talk = GymDialogueDirector.Active != null
                        ? GymDialogueDirector.Active.FindNearbyTalkTarget(player)
                        : null;
                    if (GymDialogueDirector.IsPlayerInFight(player))
                        throw new InvalidOperationException("player counted as fighting before any fight");
                    if (talk == null)
                    {
                        if (now - stageStarted > 6d)
                            throw new InvalidOperationException(
                                $"no talk target before the fight (member at {member.transform.position}, player at {player.transform.position})");
                        return;
                    }
                    member.BecomeAggressive(player);
                    attacksBefore = ronnie.AttackStartsForVerification;
                    Debug.Log($"GYMCHAOS_RONNIE_SETUP talkBefore={talk.Identity} member={member.Identity} " +
                        $"ronnie={ronnie.transform.position} memberPos={member.transform.position} player={player.transform.position}");
                    Next(2, now);
                    break;
                case 2:
                    Observe(now);
                    break;
            }
        }
        catch (Exception exception)
        {
            Debug.LogError($"GYMCHAOS_RONNIE_INTERVENTION_FAIL {exception.Message} stage={stage} last=[{details}]");
            Finish(1);
        }
    }

    private static void Setup(double now)
    {
        if (Time.frameCount < 3) return;
        player = UnityEngine.Object.FindAnyObjectByType<PlayerMovement>();
        EnemyFighter[] fighters = UnityEngine.Object.FindObjectsByType<EnemyFighter>(FindObjectsSortMode.None);
        foreach (EnemyFighter fighter in fighters)
        {
            if (fighter == null || fighter.IsDead) continue;
            if (fighter.Identity == BodybuilderIdentity.Ronnie) ronnie = fighter;
            else if (fighter.Identity == BodybuilderIdentity.Zyzz) member = fighter;
        }
        GymVisitorDirector director = UnityEngine.Object.FindAnyObjectByType<GymVisitorDirector>();
        Renderer floor = GameObject.Find("Rubber Floor")?.GetComponent<Renderer>();
        if (player == null || ronnie == null || member == null || director == null || floor == null ||
            GymDialogueDirector.Active == null)
        {
            if (now - started > 40d)
                throw new InvalidOperationException(
                    $"missing actors player={player != null} ronnie={ronnie != null} member={member != null} " +
                    $"director={director != null} floor={floor != null} dialogue={GymDialogueDirector.Active != null}");
            return;
        }
        director.PauseVisitorScheduleForVerification();
        director.SuspendVisitorSimulationForVerification();
        member.gameObject.SetActive(true);
        ronnie.gameObject.SetActive(true);

        // One clear line across the floor: player, member next to him, Ronnie
        // further out, so the member is the participant nearest to Ronnie.
        Bounds bounds = floor.bounds;
        float y = bounds.max.y;
        Vector3 playerAt = new Vector3(bounds.min.x + 2.5f, y, bounds.center.z);
        Vector3 memberAt = playerAt + Vector3.right * 1.7f;
        Vector3 ronnieAt = playerAt + Vector3.right * Mathf.Min(10f, bounds.size.x - 4f);
        MovePlayer(playerAt);
        member.SetVisitorSpawnPose(memberAt, Quaternion.LookRotation(Vector3.left, Vector3.up));
        ronnie.SetVisitorSpawnPose(ronnieAt, Quaternion.LookRotation(Vector3.left, Vector3.up));
        Next(1, now);
    }

    private static void Observe(double now)
    {
        EnemyFighter talk = GymDialogueDirector.Active.FindNearbyTalkTarget(player);
        if (!GymDialogueDirector.IsPlayerInFight(player))
            throw new InvalidOperationException("angered member did not count as a fight");
        if (talk != null)
            throw new InvalidOperationException($"talk target {talk.Identity} offered during the fight");

        Transform target = ronnie.CurrentTarget;
        if (target != null && target != member.transform)
        {
            wrongTargetFrames++;
        }
        Vector3 toMember = Vector3.ProjectOnPlane(member.transform.position - ronnie.transform.position, Vector3.up);
        float distance = toMember.magnitude;
        closest = Mathf.Min(closest, distance);
        Rigidbody body = ronnie.GetComponent<Rigidbody>();
        float speed = body != null ? Vector3.ProjectOnPlane(body.linearVelocity, Vector3.up).magnitude : 0f;
        bool running = ronnie.AnimationState == MixamoScanRetargetAnimator.MotionState.Running;
        if (distance <= ReachRange)
        {
            if (firstReach < 0d) firstReach = now;
            inReachFrames++;
            if (running && speed > 0.9f)
            {
                runningInReach++;
            }
        }
        int attacks = ronnie.AttackStartsForVerification - attacksBefore;
        details = $"distance={distance:F2} closest={closest:F2} speed={speed:F2} state={ronnie.AnimationState} " +
            $"stop={ronnie.ChaseStopReasonForVerification} target={(target != null ? target.name : "none")} " +
            $"attacks={attacks} inReach={inReachFrames} runningInReach={runningInReach} wrongTarget={wrongTargetFrames} " +
            $"ronnie={ronnie.transform.position} member={member.transform.position} memberAggro={member.IsAggressive}";
        if (Time.frameCount % 30 == 0)
        {
            Debug.Log($"GYMCHAOS_RONNIE_SAMPLE {details}");
        }
        if (now - stageStarted < FightSeconds)
        {
            return;
        }

        if (firstReach < 0d)
            throw new InvalidOperationException("Ronnie never reached the fight participant");
        if (attacks < 2)
            throw new InvalidOperationException($"Ronnie reached the participant but attacked {attacks} times");
        if (runningInReach > inReachFrames * 0.15f)
            throw new InvalidOperationException("Ronnie kept running while in reach of the participant");
        if (wrongTargetFrames > 10)
            throw new InvalidOperationException("Ronnie did not target the nearest participant");
        Debug.Log($"GYMCHAOS_RONNIE_INTERVENTION_OK reachAfter={firstReach - stageStarted:F1}s {details}");
        Finish(0);
    }

    private static void MovePlayer(Vector3 position)
    {
        player.ResetMovementForVerification();
        CharacterController controller = player.GetComponent<CharacterController>();
        bool wasEnabled = controller != null && controller.enabled;
        if (wasEnabled) controller.enabled = false;
        position.y = player.transform.position.y;
        player.transform.position = position;
        player.ResetMovementForVerification();
        if (wasEnabled) controller.enabled = true;
    }

    private static void Next(int next, double now)
    {
        stage = next;
        stageStarted = now;
    }

    private static void Finish(int code)
    {
        if (finished) return;
        finished = true;
        resultCode = code;
        GymChaosVerifierExit.Record(resultCode);
        SessionState.EraseBool(RequestedKey);
        EditorApplication.update -= Tick;
        EditorApplication.isPlaying = false;
    }
}
#endif
