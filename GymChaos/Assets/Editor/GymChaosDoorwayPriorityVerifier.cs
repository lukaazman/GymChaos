using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class GymChaosDoorwayPriorityVerifier
{
    private const string RequestedKey = "GymChaos.DoorwayPriorityVerificationRequested";
    private static double startedAt;
    private static bool entryStarted;
    private static bool exitStarted;
    private static bool yielded;
    private static bool retreatReached;
    private static bool resumed;
    private static bool lastEntryYielding;
    private static int yieldTransitions;
    private static int resumeTransitions;
    private static bool completed;
    private static bool playerIsolated;
    private static bool raceActorsIsolated;
    private static EnemyFighter entrant;
    private static EnemyFighter leaver;
    private static GymVisitorAgent entryAgent;
    private static GymVisitorAgent exitAgent;
    private static GymVisitorVehicle exitVehicle;

    static GymChaosDoorwayPriorityVerifier()
    {
        if (!SessionState.GetBool(RequestedKey, false)) return;
        Hook();
        EditorApplication.delayCall += ResumeAfterReload;
    }

    public static void Run()
    {
        ResetState();
        SessionState.SetBool(RequestedKey, true);
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

    private static void ResumeAfterReload()
    {
        if (EditorApplication.isPlaying) StartTimer();
    }

    private static void StartTimer()
    {
        startedAt = EditorApplication.timeSinceStartup;
        Time.timeScale = 1f;
        AudioListener.pause = true;
    }

    private static void ResetState()
    {
        startedAt = 0d;
        entryStarted = false;
        exitStarted = false;
        yielded = false;
        retreatReached = false;
        resumed = false;
        lastEntryYielding = false;
        yieldTransitions = 0;
        resumeTransitions = 0;
        completed = false;
        playerIsolated = false;
        raceActorsIsolated = false;
        entrant = null;
        leaver = null;
        entryAgent = null;
        exitAgent = null;
        exitVehicle = null;
    }

    private static void IsolateNonRaceActors(EnemyFighter inbound)
    {
        EnemyFighter[] fighters = UnityEngine.Object.FindObjectsByType<EnemyFighter>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        int disabled = 0;
        for (int index = 0; index < fighters.Length; index++)
        {
            EnemyFighter fighter = fighters[index];
            if (fighter == null || fighter == inbound ||
                fighter.Identity == BodybuilderIdentity.Cbum)
                continue;

            GymVisitorAgent agent = fighter.GetComponent<GymVisitorAgent>();
            if (agent != null) agent.CancelForCombat();
            fighter.gameObject.SetActive(false);
            disabled++;
        }
        Debug.Log(
            $"GYMCHAOS_DOORWAY_PRIORITY_NON_RACE_ACTORS_HIDDEN count={disabled}");
    }

    private static void PlayModeChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            StartTimer();
            return;
        }
        if (state != PlayModeStateChange.EnteredEditMode) return;
        EditorApplication.update -= Tick;
        EditorApplication.playModeStateChanged -= PlayModeChanged;
        bool wasRequested = SessionState.GetBool(RequestedKey, false);
        SessionState.EraseBool(RequestedKey);
        if (Application.isBatchMode && wasRequested)
            EditorApplication.Exit(completed ? 0 : 1);
    }

    private static void Tick()
    {
        if (!EditorApplication.isPlaying) return;
        Time.timeScale = 1f;
        AudioListener.pause = true;
        if (startedAt <= 0d) StartTimer();
        double elapsed = EditorApplication.timeSinceStartup - startedAt;
        try
        {
            GymVisitorDirector director = UnityEngine.Object.FindAnyObjectByType<GymVisitorDirector>();
            if (director == null || !GymOutdoorBuilder.IsBuilt || elapsed < 2d) return;
            if (!playerIsolated)
            {
                PlayerMovement player = UnityEngine.Object.FindAnyObjectByType<PlayerMovement>();
                if (player == null) return;
                CharacterController controller = player.GetComponent<CharacterController>();
                if (controller != null) controller.enabled = false;
                player.enabled = false;
                player.transform.position += new Vector3(1000f, 0f, 1000f);
                Physics.SyncTransforms();
                playerIsolated = true;
                Debug.Log(
                    $"GYMCHAOS_DOORWAY_PRIORITY_TEST_PLAYER_CLEAR position={player.transform.position}");
            }
            if (!entryStarted)
            {
                director.PauseVisitorScheduleForVerification();
                if (!director.BeginEntryForVerification(BodybuilderIdentity.Cbum, out entrant))
                    throw new InvalidOperationException("No inbound visitor could be started.");
                entryAgent = entrant.GetComponent<GymVisitorAgent>();
                if (entryAgent == null) throw new InvalidOperationException("Inbound visitor agent is missing.");
                entryStarted = true;
                Debug.Log($"GYMCHAOS_DOORWAY_PRIORITY_ENTRY_STARTED enemy={entrant.Identity}");
            }
            if (entryStarted && !raceActorsIsolated)
            {
                IsolateNonRaceActors(entrant);
                raceActorsIsolated = true;
            }
            if (!exitStarted && entryAgent.IsAtDoorwayEntryQueueForVerification)
            {
                if (!director.BeginCbumDepartureForVerification(out leaver, out exitVehicle))
                    throw new InvalidOperationException("Cbum exit could not be started at the inbound queue point.");
                exitAgent = leaver.GetComponent<GymVisitorAgent>();
                if (exitAgent == null) throw new InvalidOperationException("Outbound visitor agent is missing.");
                exitStarted = true;
                Debug.Log($"GYMCHAOS_DOORWAY_PRIORITY_EXIT_STARTED inbound={entrant.Identity} outbound={leaver.Identity}");
            }
            if (exitStarted)
            {
                bool entryYieldingNow = entryAgent.IsDoorwayEntryYieldingForVerification;
                if (entryYieldingNow && !lastEntryYielding) yieldTransitions++;
                if (!entryYieldingNow && lastEntryYielding) resumeTransitions++;
                lastEntryYielding = entryYieldingNow;
                yielded |= entryYieldingNow;
                retreatReached |= yielded && entryAgent.IsAtDoorwayEntryQueueForVerification;
                resumed |= yielded && !entryAgent.IsDoorwayEntryYieldingForVerification &&
                    !entryAgent.IsAtDoorwayEntryQueueForVerification && !entryAgent.HasEnteredGym;
                bool bothCompleted = leaver != null && exitVehicle != null &&
                    exitAgent.HasCompletedDoorExit && entryAgent.HasEnteredGym;
                if (bothCompleted && yielded && retreatReached && resumed && yieldTransitions == 1 && resumeTransitions == 1 &&
                    exitAgent.IsDoorwayExitClearForVerification &&
                    exitAgent.DoorwayExitClearanceReleasedForVerification &&
                    exitAgent.DoorwayExitRecoveryCountForVerification == 0)
                {
                    completed = true;
                    Debug.Log(
                        $"GYMCHAOS_VISITOR_DOORWAY_PRIORITY_OK " +
                        $"priorityGranted={yielded} retreatReached={retreatReached} " +
                        $"exitClear={exitAgent.IsDoorwayExitClearForVerification} " +
                        $"clearanceLatched={exitAgent.DoorwayExitClearanceReleasedForVerification} " +
                        $"yieldTransitions={yieldTransitions} resumeTransitions={resumeTransitions} " +
                        $"resumed={resumed} bothCompleted={bothCompleted} exitRecoveries={exitAgent.DoorwayExitRecoveryCountForVerification}");
                    EditorApplication.isPlaying = false;
                    return;
                }
            }
            if (elapsed > 150d)
            {
                throw new TimeoutException(
                    $"Doorway race timed out: entryStarted={entryStarted} exitStarted={exitStarted} " +
                    $"yielded={yielded} retreatReached={retreatReached} resumed={resumed} " +
                    $"entryState={entryAgent?.State} entryPosition={entrant?.VisitorPhysicsPosition} " +
                    $"exitState={exitAgent?.State} exitPosition={leaver?.VisitorPhysicsPosition} "+
                    $"exitClear={exitAgent?.IsDoorwayExitClearForVerification} " +
                    $"exitClearance={exitAgent?.DoorwayExitClearanceMetersForVerification:0.00} " +
                    $"clearanceLatched={exitAgent?.DoorwayExitClearanceReleasedForVerification} " +
                    $"yieldTransitions={yieldTransitions} resumeTransitions={resumeTransitions} " +
                    $"entryBlocker={entrant?.LastVisitorRouteBlocker} exitBlocker={leaver?.LastVisitorRouteBlocker}.");
            }
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            SessionState.EraseBool(RequestedKey);
            EditorApplication.isPlaying = false;
        }
    }
}
