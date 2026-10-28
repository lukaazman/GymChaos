#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Locker door passing (2026-10-21). Members walking out of and into the
// locker room must not get stuck walking on the spot against each other in
// the door frame.
// - head-on: one walks out while another walks in at the same moment; they
//   pass in their own lanes, nobody walks through anybody.
// - in-door: the incoming member already stands in the frame when the other
//   arrives; one steps aside and both get through.
// - blocker: a member that does not move (in a conversation) stands in the
//   door mouth; the walker gets past within a few seconds.
// GYMCHAOS_LOCKER_DOOR_BASELINE=1 restores the shared centre line and turns
// the stall recovery off; that baseline must fail.
[InitializeOnLoad]
public static class GymChaosLockerDoorwayPassVerifier
{
    private const string RequestedKey = "GymChaos.LockerDoorwayPassVerificationRequested";
    private static readonly string[] ScenarioNames = { "head-on", "in-door", "blocker" };
    private static readonly double[] ScenarioSeconds = { 14d, 14d, 12d };
    private const float WalkInPlaceLimit = 2.5f;

    private static double started;
    private static double stageStarted;
    private static int lastFrame;
    private static int stage;
    private static int scenario;
    private static bool finished;
    private static int resultCode;
    private static PlayerMovement player;
    private static EnemyFighter exiter;
    private static EnemyFighter enterer;
    private static EnemyFighter blocker;
    private static GymVisitorAgent exiterAgent;
    private static GymVisitorAgent entererAgent;
    private static Vector3 destination;
    private static float doorZ;
    private static float doorX;
    private static float floorY;
    private static bool entererStarted;
    private static double exiterThrough;
    private static double entererThrough;
    private static int passesBefore;
    private static int sidestepsBefore;
    private static float exiterInPlace;
    private static float entererInPlace;
    private static float worstInPlace;
    private static string details = "none";
    private static string summary = "";

    static GymChaosLockerDoorwayPassVerifier()
    {
        if (SessionState.GetBool(RequestedKey, false))
        {
            Hook();
        }
    }

    [MenuItem("Tools/GymChaos/Run Locker Doorway Pass Verification")]
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

    private static bool Baseline =>
        Environment.GetEnvironmentVariable("GYMCHAOS_LOCKER_DOOR_BASELINE") == "1";

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
        exiter = enterer = blocker = null;
        exiterAgent = entererAgent = null;
        details = "none";
        summary = "";
        GymBackRoomBuilder.LockerDoorLanesDisabledForVerification = Baseline;
        GymVisitorAgent.LockerStallRecoveryDisabledForVerification = Baseline;
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
        GymBackRoomBuilder.LockerDoorLanesDisabledForVerification = false;
        GymVisitorAgent.LockerStallRecoveryDisabledForVerification = false;
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
            if (now - started > 170d)
            {
                throw new TimeoutException($"timed out at stage {stage}: {details}");
            }
            if (Time.frameCount == lastFrame)
            {
                return;
            }
            lastFrame = Time.frameCount;
            switch (stage)
            {
                case 0:
                    Setup(now);
                    break;
                case 1:
                    PlaceScenario(now);
                    break;
                case 3:
                    Observe(now);
                    break;
            }
        }
        catch (Exception exception)
        {
            Debug.LogError($"GYMCHAOS_LOCKER_DOORWAY_PASS_FAILED scenario={ScenarioName} {exception.Message} stage={stage} last=[{details}]");
            Finish(1);
        }
    }

    private static string ScenarioName => ScenarioNames[Mathf.Clamp(scenario, 0, ScenarioNames.Length - 1)];

    private static void Setup(double now)
    {
        if (Time.frameCount < 3) return;
        player = UnityEngine.Object.FindAnyObjectByType<PlayerMovement>();
        GymVisitorDirector director = UnityEngine.Object.FindAnyObjectByType<GymVisitorDirector>();
        bool roomReady = GymBackRoomBuilder.TryGetLockerVisitPose(out destination, out _);
        if (player == null || director == null || !roomReady)
        {
            if (now - started > 40d)
                throw new InvalidOperationException(
                    $"missing player={player != null} director={director != null} room={roomReady}");
            return;
        }
        director.SuspendVisitorSimulationForVerification();
        foreach (GymVisitorAgent agent in UnityEngine.Object.FindObjectsByType<GymVisitorAgent>(FindObjectsSortMode.None))
        {
            EnemyFighter fighter = agent != null ? agent.GetComponent<EnemyFighter>() : null;
            if (fighter == null || fighter.IsDead || fighter.IsPolice || fighter.IsPassive ||
                fighter.Identity == BodybuilderIdentity.Ronnie || fighter.Identity == BodybuilderIdentity.Goku)
            {
                continue;
            }
            if (exiter == null) { exiter = fighter; exiterAgent = agent; }
            else if (enterer == null) { enterer = fighter; entererAgent = agent; }
            else if (blocker == null) { blocker = fighter; }
        }
        if (exiter == null || enterer == null || blocker == null)
            throw new InvalidOperationException("need three members");

        if (!GymBackRoomBuilder.TryGetLockerVisitRoute(destination, false, out Vector3[] inRoute, out _) ||
            !GymBackRoomBuilder.TryGetLockerVisitRoute(destination, true, out Vector3[] outRoute, out _))
            throw new InvalidOperationException("no locker route");
        doorZ = (inRoute[0].z + inRoute[1].z) * 0.5f;
        doorX = (inRoute[0].x + outRoute[0].x) * 0.5f;
        floorY = inRoute[0].y;
        MovePlayer(new Vector3(doorX + 7f, player.transform.position.y, doorZ + 7f));
        Debug.Log($"GYMCHAOS_LOCKER_DOORWAY_PASS_SETUP door=({doorX:F2},{doorZ:F2}) exiter={exiter.Identity} " +
            $"enterer={enterer.Identity} blocker={blocker.Identity} baseline={Baseline}");
        Next(1, now);
    }

    private static void PlaceScenario(double now)
    {
        GymVisitorAgent blockerAgent = blocker.GetComponent<GymVisitorAgent>();
        foreach (GymVisitorAgent agent in new[] { exiterAgent, entererAgent, blockerAgent })
        {
            agent.CancelForCombat();
        }
        blocker.SetDialogueLocked(false);
        enterer.SetDialogueLocked(false);
        Vector3 parked = new Vector3(doorX + 6f, floorY, doorZ + 6f);
        exiter.SetVisitorSpawnPose(new Vector3(doorX, floorY, doorZ - 2.3f), Quaternion.LookRotation(Vector3.forward));
        switch (scenario)
        {
            case 0:
                enterer.SetVisitorSpawnPose(new Vector3(doorX, floorY, doorZ + 3.6f), Quaternion.LookRotation(Vector3.back));
                blocker.SetVisitorSpawnPose(parked, Quaternion.identity);
                break;
            case 1:
                // Already in the frame and not moving until the other is close.
                enterer.SetVisitorSpawnPose(new Vector3(doorX, floorY, doorZ + 0.3f), Quaternion.LookRotation(Vector3.back));
                enterer.SetDialogueLocked(true);
                blocker.SetVisitorSpawnPose(parked, Quaternion.identity);
                break;
            default:
                enterer.SetVisitorSpawnPose(parked + Vector3.right * 2.5f, Quaternion.identity);
                blocker.SetVisitorSpawnPose(new Vector3(doorX, floorY, doorZ + 0.9f), Quaternion.LookRotation(Vector3.back));
                blocker.SetDialogueLocked(true);
                break;
        }
        entererStarted = scenario == 2;
        exiterThrough = entererThrough = -1d;
        exiterInPlace = entererInPlace = worstInPlace = 0f;
        passesBefore = exiterAgent.LockerVisitCrowdPassCountForVerification +
            entererAgent.LockerVisitCrowdPassCountForVerification;
        sidestepsBefore = exiterAgent.LockerVisitSidestepCountForVerification +
            entererAgent.LockerVisitSidestepCountForVerification;
        // Start in the same frame: a roaming member is clamped to the main
        // floor, which would pull the exiter back out of the locker room.
        if (!exiterAgent.BeginLockerReturnForVerification(destination))
            throw new InvalidOperationException("exit walk did not start");
        Next(3, now);
    }

    private static void Observe(double now)
    {
        double elapsed = now - stageStarted;
        Vector3 a = exiter.transform.position;
        Vector3 b = enterer.transform.position;
        if (!entererStarted)
        {
            bool go = scenario == 0
                ? elapsed > 0.35d
                : Vector3.ProjectOnPlane(a - b, Vector3.up).magnitude < 1.7f || elapsed > 4d;
            if (go)
            {
                enterer.SetDialogueLocked(false);
                if (!entererAgent.BeginLockerRoomVisit(destination, 0.5f, "verification"))
                    throw new InvalidOperationException($"entry walk did not start state={entererAgent.State}");
                entererStarted = true;
            }
        }
        if (exiterThrough < 0d && a.z > doorZ + 1.0f) exiterThrough = elapsed;
        if (entererThrough < 0d && b.z < doorZ - 1.0f) entererThrough = elapsed;
        exiterInPlace = InPlace(exiter, exiterThrough < 0d, exiterInPlace);
        entererInPlace = InPlace(enterer, entererStarted && entererThrough < 0d, entererInPlace);
        worstInPlace = Mathf.Max(worstInPlace, Mathf.Max(exiterInPlace, entererInPlace));
        int passes = exiterAgent.LockerVisitCrowdPassCountForVerification +
            entererAgent.LockerVisitCrowdPassCountForVerification - passesBefore;
        int sidesteps = exiterAgent.LockerVisitSidestepCountForVerification +
            entererAgent.LockerVisitSidestepCountForVerification - sidestepsBefore;
        details = $"exiter={exiterAgent.State} at {a} through={exiterThrough:F1} " +
            $"enterer={entererAgent.State} at {b} through={entererThrough:F1} " +
            $"walkInPlace={worstInPlace:F1}s crowdPasses={passes} sidesteps={sidesteps} blocker={exiter.LastVisitorRouteBlocker}";
        if (Time.frameCount % 30 == 0)
        {
            Debug.Log($"GYMCHAOS_LOCKER_DOORWAY_PASS_SAMPLE scenario={ScenarioName} {details}");
        }
        if (worstInPlace > WalkInPlaceLimit)
            throw new InvalidOperationException($"a member walked on the spot for {worstInPlace:F1}s");

        bool done = exiterThrough >= 0d && (scenario == 2 || entererThrough >= 0d);
        if (!done)
        {
            if (elapsed > ScenarioSeconds[scenario])
                throw new InvalidOperationException("members did not get through the locker door");
            return;
        }
        if (scenario == 0 && passes > 0)
            throw new InvalidOperationException("head-on members only got through by walking through each other");
        string line = $"{ScenarioName}:exit={exiterThrough:F1}s,enter={entererThrough:F1}s,inPlace={worstInPlace:F1}s,passes={passes},sidesteps={sidesteps}";
        summary += (summary.Length > 0 ? " " : "") + line;
        Debug.Log($"GYMCHAOS_LOCKER_DOORWAY_SCENARIO_OK {line}");
        blocker.SetDialogueLocked(false);
        enterer.SetDialogueLocked(false);
        scenario++;
        if (scenario >= ScenarioNames.Length)
        {
            Debug.Log($"GYMCHAOS_LOCKER_DOORWAY_PASS_OK {summary}");
            Finish(0);
            return;
        }
        Next(1, now);
    }

    // Seconds a member has been showing its walk cycle without getting anywhere.
    private static float InPlace(EnemyFighter fighter, bool active, float seconds)
    {
        if (!active)
        {
            return 0f;
        }
        Rigidbody body = fighter.GetComponent<Rigidbody>();
        float speed = body != null ? Vector3.ProjectOnPlane(body.linearVelocity, Vector3.up).magnitude : 0f;
        bool walking = fighter.AnimationState == MixamoScanRetargetAnimator.MotionState.Running;
        return walking && speed < 0.2f ? seconds + Time.deltaTime : 0f;
    }

    private static void MovePlayer(Vector3 position)
    {
        player.ResetMovementForVerification();
        CharacterController controller = player.GetComponent<CharacterController>();
        bool wasEnabled = controller != null && controller.enabled;
        if (wasEnabled) controller.enabled = false;
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
