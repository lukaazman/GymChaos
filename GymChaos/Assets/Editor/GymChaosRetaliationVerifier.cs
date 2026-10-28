#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Attacked enemies fight back everywhere (2026-10-21).
// - outside: a member walking out to its car is hit by the player on the
//   path/parking lot and attacks the player;
// - locker: a member inside the locker room is hit and attacks;
// - seated: a driver hit through the car window gets out and attacks;
// - ronnie: a member already fighting the player is hit by Ronnie, holds a
//   grudge against him too, goes for the closer one (Ronnie) and damages him;
// - steady: with two attackers at similar range the target is re-picked at
//   most every few seconds (no flip-flopping).
[InitializeOnLoad]
public static class GymChaosRetaliationVerifier
{
    private const string RequestedKey = "GymChaos.RetaliationVerificationRequested";
    private static readonly string[] Names = { "outside", "locker", "seated", "ronnie", "steady" };

    private static double started;
    private static double stageStarted;
    private static int lastFrame;
    private static int stage;
    private static int scenario;
    private static bool finished;
    private static int resultCode;
    private static PlayerMovement player;
    private static GymVisitorDirector director;
    private static EnemyFighter subject;
    private static EnemyFighter ronnie;
    private static int attacksBefore;
    private static float ronnieHealthBefore;
    private static int switchesBefore;
    private static string summary = "";
    private static string details = "none";

    static GymChaosRetaliationVerifier()
    {
        if (SessionState.GetBool(RequestedKey, false))
        {
            Hook();
        }
    }

    [MenuItem("Tools/GymChaos/Run Retaliation Verification")]
    public static void Run()
    {
        SessionState.SetBool(RequestedKey, true);
        GymChaosVerifierSave.Neutralize();
        ResetState();
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        Hook();
        EditorApplication.isPlaying = true;
    }

    private static void Hook() => GymChaosVerifierHooks.Hook(Tick, PlayModeChanged);

    private static void ResetState()
    {
        started = EditorApplication.timeSinceStartup;
        stageStarted = started;
        lastFrame = -1;
        stage = 0;
        scenario = 0;
        finished = false;
        resultCode = 1;
        GymChaosVerifierExit.Record(resultCode);
        player = null;
        director = null;
        subject = null;
        ronnie = null;
        summary = "";
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

    private static string Name => Names[Mathf.Clamp(scenario, 0, Names.Length - 1)];

    private static void Tick()
    {
        if (finished || !EditorApplication.isPlaying)
        {
            return;
        }
        try
        {
            double now = EditorApplication.timeSinceStartup;
            if (now - started > 280d)
            {
                throw new TimeoutException($"timed out at {Name} stage {stage}: {details}");
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
                    Prepare(now);
                    break;
                case 2:
                    Observe(now);
                    break;
            }
        }
        catch (Exception exception)
        {
            Debug.LogError($"GYMCHAOS_RETALIATION_FAILED scenario={Name} {exception.Message} stage={stage} last=[{details}]");
            Finish(1);
        }
    }

    private static void Setup(double now)
    {
        if (Time.frameCount < 3 || now - started < 4d) return;
        player = UnityEngine.Object.FindAnyObjectByType<PlayerMovement>();
        director = UnityEngine.Object.FindAnyObjectByType<GymVisitorDirector>();
        foreach (EnemyFighter fighter in UnityEngine.Object.FindObjectsByType<EnemyFighter>(FindObjectsSortMode.None))
        {
            if (fighter != null && fighter.Identity == BodybuilderIdentity.Ronnie) ronnie = fighter;
        }
        if (player == null || director == null || ronnie == null)
        {
            if (now - started > 40d)
                throw new InvalidOperationException($"missing player={player != null} director={director != null} ronnie={ronnie != null}");
            return;
        }
        director.HoldScheduledArrivalsForVerification();
        Next(1, now);
    }

    // Puts the scenario's subject where the scenario needs it, then attacks it.
    private static void Prepare(double now)
    {
        double elapsed = now - stageStarted;
        switch (scenario)
        {
            case 0:
                if (subject == null)
                {
                    if (!director.BeginIdentityDepartureForVerification(BodybuilderIdentity.Zyzz, out subject, out _))
                    {
                        if (elapsed > 20d) throw new InvalidOperationException("no departure started");
                        return;
                    }
                }
                // Wait until it walks on the outdoor path / parking lot.
                if (!GymOutdoorBuilder.IsPlayerOutsideGym(subject.transform.position) || subject.IsInVehicleSeatMode)
                {
                    if (elapsed > 60d) throw new InvalidOperationException("departing member never got outside");
                    return;
                }
                Attack(subject, null, 2.2f);
                break;
            case 1:
                if (subject == null)
                {
                    // A fresh roaming member, started next to the door: the
                    // walk across the gym is not what this scenario tests.
                    GymBackRoomBuilder.TryGetLockerDoorPoints(out Vector3 doorGymSide, out _);
                    GymBackRoomBuilder.TryGetLockerVisitPose(out Vector3 lockerSpot, out _);
                    foreach (EnemyFighter fighter in UnityEngine.Object.FindObjectsByType<EnemyFighter>(FindObjectsSortMode.None))
                    {
                        GymVisitorAgent agent = fighter != null ? fighter.GetComponent<GymVisitorAgent>() : null;
                        if (fighter == null || fighter.IsDead || fighter.IsPolice || fighter.IsPassive || fighter.IsAggressive ||
                            fighter.Identity == BodybuilderIdentity.Goku || agent == null || !agent.IsInsideGym ||
                            agent.State != GymVisitorAgent.VisitorState.FreeRoaming)
                        {
                            continue;
                        }
                        fighter.SetVisitorSpawnPose(doorGymSide + Vector3.forward * 0.6f, Quaternion.LookRotation(Vector3.back));
                        if (agent.BeginLockerRoomVisit(lockerSpot, 6f, "verification"))
                        {
                            subject = fighter;
                            break;
                        }
                    }
                    if (subject == null)
                    {
                        if (elapsed > 20d) throw new InvalidOperationException("no member could start a locker visit");
                        return;
                    }
                }
                if (!GymBackRoomBuilder.IsInsideRoom(subject.transform.position))
                {
                    if (elapsed > 40d) throw new InvalidOperationException("member never reached the locker room");
                    return;
                }
                // The player stands at the door on the gym side: the member
                // must come out through the door to fight.
                GymBackRoomBuilder.TryGetLockerDoorPoints(out Vector3 gymSide, out _);
                Attack(subject, gymSide + Vector3.forward * 1.5f, 0f);
                break;
            case 2:
                if (subject == null)
                {
                    if (!director.BeginVehicleArrivalForVerification(BodybuilderIdentity.Cbum, out subject, out _))
                    {
                        if (elapsed > 20d) throw new InvalidOperationException("no car arrival started");
                        return;
                    }
                }
                if (!subject.IsSeatedInVehicle)
                {
                    if (elapsed > 40d) throw new InvalidOperationException("driver never sat in its car");
                    return;
                }
                Attack(subject, null, 2.6f);
                break;
            case 3:
            case 4:
                if (subject == null)
                {
                    foreach (EnemyFighter fighter in UnityEngine.Object.FindObjectsByType<EnemyFighter>(FindObjectsSortMode.None))
                    {
                        GymVisitorAgent agent = fighter != null ? fighter.GetComponent<GymVisitorAgent>() : null;
                        if (fighter != null && !fighter.IsDead && !fighter.IsPolice && !fighter.IsPassive &&
                            fighter.Identity != BodybuilderIdentity.Goku && agent != null && agent.IsInsideGym &&
                            agent.State == GymVisitorAgent.VisitorState.FreeRoaming && !fighter.IsAggressive)
                        {
                            subject = fighter;
                            break;
                        }
                    }
                    if (subject == null)
                    {
                        if (elapsed > 20d) throw new InvalidOperationException("no roaming member");
                        return;
                    }
                }
                Renderer floor = GameObject.Find("Rubber Floor").GetComponent<Renderer>();
                Bounds b = floor.bounds;
                Vector3 at = new Vector3(b.center.x, b.max.y, b.center.z);
                subject.SetVisitorSpawnPose(at, Quaternion.identity);
                // Ronnie only ever goes for fight participants: keep him out of
                // it here by standing him still; the hit is what counts.
                ronnie.SetVisitorSpawnPose(at + Vector3.right * (scenario == 3 ? 1.6f : 3.0f), Quaternion.LookRotation(Vector3.left));
                MovePlayer(at + Vector3.left * (scenario == 3 ? 6f : 3.2f));
                subject.TakeMeleeHit(Vector3.zero, 1f, 0.05f);
                subject.TakeMeleeHit(Vector3.zero, 1f, 0.05f, ronnie);
                ronnieHealthBefore = ronnie.CurrentHealth;
                switchesBefore = subject.GrudgeTargetSwitchesForVerification;
                attacksBefore = subject.AttackStartsForVerification;
                Next(2, now);
                break;
        }
    }

    private static void Attack(EnemyFighter fighter, Vector3? playerAt, float playerDistance)
    {
        Vector3 position = playerAt ?? fighter.transform.position +
            Vector3.ProjectOnPlane(fighter.transform.right, Vector3.up).normalized * playerDistance;
        MovePlayer(position);
        attacksBefore = fighter.AttackStartsForVerification;
        // A player punch has no attacker argument (same path as PlayerMovement).
        fighter.TakeMeleeHit((fighter.transform.position - position).normalized * 2f, 1f, 0.1f);
        if (!playerAt.HasValue)
        {
            // A driver jumps out of its car on the hit: stand next to where
            // its body actually is now.
            Physics.SyncTransforms();
            Rigidbody body = fighter.GetComponent<Rigidbody>();
            Vector3 away = Vector3.ProjectOnPlane(body.rotation * Vector3.forward, Vector3.up).normalized;
            MovePlayer(body.position + away * playerDistance);
        }
        Debug.Log($"GYMCHAOS_RETALIATION_ATTACKED scenario={Name} subject={fighter.Identity} at={fighter.transform.position}");
        Next(2, EditorApplication.timeSinceStartup);
    }

    private static void Observe(double now)
    {
        double elapsed = now - stageStarted;
        int attacks = subject.AttackStartsForVerification - attacksBefore;
        float toPlayer = Vector3.ProjectOnPlane(player.transform.position - subject.transform.position, Vector3.up).magnitude;
        details = $"subject={subject.Identity} aggressive={subject.IsAggressive} target={(subject.CurrentTarget != null ? subject.CurrentTarget.name : "none")} " +
            $"attacks={attacks} toPlayer={toPlayer:F1} seated={subject.IsInVehicleSeatMode} stop={subject.ChaseStopReasonForVerification} pos={subject.transform.position}";
        if (Time.frameCount % 40 == 0) Debug.Log($"GYMCHAOS_RETALIATION_SAMPLE scenario={Name} {details}");
        bool done;
        switch (scenario)
        {
            case 3:
                done = subject.HasGrudgeAgainstForVerification(ronnie) &&
                    subject.CurrentFighterTarget == ronnie && ronnie.CurrentHealth < ronnieHealthBefore;
                if (elapsed > 12d && !done)
                    throw new InvalidOperationException(
                        $"no fight with Ronnie: grudge={subject.HasGrudgeAgainstForVerification(ronnie)} " +
                        $"targetRonnie={subject.CurrentFighterTarget == ronnie} ronnieHealth={ronnie.CurrentHealth:F0}/{ronnieHealthBefore:F0}");
                break;
            case 4:
                done = elapsed > 10d;
                if (done)
                {
                    int switches = subject.GrudgeTargetSwitchesForVerification - switchesBefore;
                    if (switches > 4)
                        throw new InvalidOperationException($"target flipped {switches} times in 10 s");
                    details += $" switches={switches}";
                }
                break;
            default:
                done = subject.IsAggressive && !subject.IsInVehicleSeatMode &&
                    subject.CurrentTarget == player.transform && attacks > 0;
                if (elapsed > 15d && !done)
                    throw new InvalidOperationException("attacked member did not fight back");
                break;
        }
        if (!done)
        {
            return;
        }
        string line = $"{Name}:{elapsed:F1}s";
        summary += (summary.Length > 0 ? " " : "") + line;
        Debug.Log($"GYMCHAOS_RETALIATION_SCENARIO_OK {line} {details}");
        subject.ResetAggressionForVerification();
        ronnie.ResetAggressionForVerification();
        subject = null;
        scenario++;
        if (scenario >= Names.Length)
        {
            Debug.Log($"GYMCHAOS_RETALIATION_OK {summary}");
            Finish(0);
            return;
        }
        Next(1, now);
    }

    private static void MovePlayer(Vector3 position)
    {
        player.ResetMovementForVerification();
        CharacterController controller = player.GetComponent<CharacterController>();
        bool wasEnabled = controller != null && controller.enabled;
        if (wasEnabled) controller.enabled = false;
        position.y = Mathf.Max(position.y, player.transform.position.y - 5f);
        player.transform.position = position + Vector3.up * 0.05f;
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
