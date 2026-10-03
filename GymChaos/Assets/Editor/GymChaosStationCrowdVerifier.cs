#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// One enemy per machine: neutral enemies must not pick, keep walking to, or
// keep standing on a station that another enemy uses or is already heading
// to. The reported case was two visitors wedged inside the Smith machine.
[InitializeOnLoad]
public static class GymChaosStationCrowdVerifier
{
    private const string RequestedKey = "GymChaos.StationCrowdVerificationRequested";
    private const string OriginalSaveKey = "GymChaos.StationCrowdVerificationOriginalSave";
    private const string OriginalSavePresentKey = "GymChaos.StationCrowdVerificationOriginalSavePresent";
    private const string ProgressionSaveKey = "GymChaos.Progression.v1";

    private static double started;
    private static double stageStarted;
    private static int lastFrame;
    private static int stage;
    private static bool finished;
    private static int resultCode;
    private static GymExerciseStation station;
    private static EnemyFighter user;
    private static EnemyFighter walker;
    private static EnemyFighter rival;
    private static readonly List<string> results = new List<string>();

    static GymChaosStationCrowdVerifier()
    {
        if (SessionState.GetBool(RequestedKey, false))
        {
            Hook();
        }
    }

    [MenuItem("Tools/GymChaos/Run Station Crowd Verification")]
    public static void Run()
    {
        SessionState.SetBool(RequestedKey, true);
        GymChaosVerifierPrefs.SetBool(OriginalSavePresentKey, PlayerPrefs.HasKey(ProgressionSaveKey));
        GymChaosVerifierPrefs.SetString(OriginalSaveKey, PlayerPrefs.GetString(ProgressionSaveKey, string.Empty));
        PlayerPrefs.DeleteKey(ProgressionSaveKey);
        PlayerPrefs.Save();
        ResetState();
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        Hook();
        EditorApplication.isPlaying = true;
    }

    // Live observation: normal visitor simulation at 3x speed, logging every
    // moment two enemies share one squat station footprint.
    public static void RunLive()
    {
        SessionState.SetBool(LiveKey, true);
        Run();
    }

    private const string LiveKey = "GymChaos.StationCrowdVerificationLive";
    private static readonly Dictionary<string, float> overlapSince = new Dictionary<string, float>();
    private static int incidents;

    private static void TickLive()
    {
        double elapsed = EditorApplication.timeSinceStartup - started;
        if (elapsed < 3d) return;
        Time.timeScale = 3f;
        GymExerciseStation[] stations = UnityEngine.Object.FindObjectsByType<GymExerciseStation>(FindObjectsSortMode.None);
        EnemyFighter[] fighters = UnityEngine.Object.FindObjectsByType<EnemyFighter>(FindObjectsSortMode.None);
        foreach (GymExerciseStation candidate in stations)
        {
            if (candidate == null || candidate.EquipmentRoot == null) continue;
            Bounds footprint = new Bounds(candidate.EnemyPosition, new Vector3(1.6f, 10f, 1.6f));
            List<EnemyFighter> inside = new List<EnemyFighter>();
            foreach (EnemyFighter fighter in fighters)
            {
                if (fighter != null && fighter.isActiveAndEnabled && !fighter.IsDead &&
                    footprint.Contains(fighter.transform.position))
                {
                    inside.Add(fighter);
                }
            }
            string key = candidate.GetHashCode().ToString();
            if (inside.Count < 2)
            {
                overlapSince.Remove(key);
                continue;
            }
            if (!overlapSince.TryGetValue(key, out float since))
            {
                overlapSince[key] = Time.time;
                continue;
            }
            float held = Time.time - since;
            bool report = (held > 2f && held < 2f + Time.deltaTime * 1.5f) || (held > 8f && held < 8f + Time.deltaTime * 1.5f);
            if (report)
            {
                if (held < 3f) incidents++;
                string who = "";
                foreach (EnemyFighter fighter in inside)
                {
                    GymVisitorAgent agent = fighter.GetComponent<GymVisitorAgent>();
                    who += $"{fighter.Identity}(state={(agent != null ? agent.State.ToString() : "none")} " +
                        $"roamStation={(fighter.RoamTargetStationForVerification != null ? fighter.RoamTargetStationForVerification.EquipmentName : "none")} " +
                        $"occupant={candidate.EnemyOccupant == fighter} pos={fighter.transform.position}) ";
                }
                Debug.Log($"GYMCHAOS_STATION_CROWD_INCIDENT station={candidate.EquipmentName} " +
                    $"type={(candidate.IsSquat ? "squat" : candidate.IsTreadmill ? "treadmill" : "other")} " +
                    $"occupied={candidate.IsOccupied} occupant={(candidate.EnemyOccupant != null ? candidate.EnemyOccupant.Identity.ToString() : "none")} held={held:F1} spot={candidate.EnemyPosition} t={Time.time:F1} {who}");
            }
        }
        if (elapsed > 100d)
        {
            Debug.Log($"GYMCHAOS_STATION_CROWD_LIVE_DONE incidents={incidents}");
            Finish(incidents == 0 ? 0 : 1);
        }
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
        resultCode = 1; GymChaosVerifierExit.Record(resultCode);
        station = null;
        user = walker = rival = null;
        results.Clear();
    }

    private static void PlayModeChanged(PlayModeStateChange change)
    {
        if (change == PlayModeStateChange.EnteredPlayMode)
        {
            ResetState();
            Time.timeScale = 1f;
        }
        if (change != PlayModeStateChange.EnteredEditMode)
        {
            return;
        }
        EditorApplication.update -= Tick;
        EditorApplication.playModeStateChanged -= PlayModeChanged;
        SessionState.EraseBool(RequestedKey);
        SessionState.EraseBool(LiveKey);
        RestoreOriginalProgressionSave();
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
            if (EditorApplication.timeSinceStartup - started > 130d)
            {
                throw new InvalidOperationException($"Station crowd verification timed out at stage {stage}.");
            }
            if (Time.frameCount == lastFrame)
            {
                return;
            }
            lastFrame = Time.frameCount;
            if (SessionState.GetBool(LiveKey, false))
            {
                TickLive();
                return;
            }
            double inStage = EditorApplication.timeSinceStartup - stageStarted;

            switch (stage)
            {
                case 0:
                    if (EditorApplication.timeSinceStartup - started < 3d) return;
                    Setup();
                    Next();
                    return;
                case 1:
                {
                    // Station reserved by `user`: fresh roam choices must skip it.
                    int picked = 0;
                    for (int i = 0; i < 300; i++)
                    {
                        walker.SelectRoamDestinationForVerification();
                        if (walker.RoamTargetStationForVerification == station) picked++;
                    }
                    Require(picked == 0, $"walker picked the occupied station {picked}/300 times");
                    results.Add("occupiedSkipped=300/300");
                    walker.SetRoamTargetStationForVerification(station);
                    Next();
                    return;
                }
                case 2:
                    // Taken while walking: reroute within a second.
                    if (inStage < 1d) return;
                    Require(walker.RoamTargetStationForVerification != station,
                        $"walker kept walking to a station another enemy reserved: walker={walker.Identity} agent={(walker.GetComponent<GymVisitorAgent>() != null ? walker.GetComponent<GymVisitorAgent>().State.ToString() : "none")} roaming={walker.IsRoaming} dialogue={walker.IsDialogueLocked} treadmill={walker.IsOnTreadmill} pos={walker.transform.position}");
                    results.Add("rerouteWhenTaken=True");
                    // The reported wedge: the reserving enemy is inside the
                    // cage when its approach is cancelled (switch/stall).
                    user.SetVisitorSpawnPose(station.EnemyPosition, station.EnemyRotation);
                    station.CancelEnemySquatApproach(user);
                    Require(!station.IsAvailableForEnemy(walker),
                        "station was free for another enemy while the cancelled one was still inside");
                    user.SelectRoamDestinationForVerification();
                    Next();
                    return;
                case 3:
                {
                    // The cancelled enemy must be able to walk out of the cage.
                    float left = Vector3.ProjectOnPlane(
                        user.transform.position - station.EnemyPosition, Vector3.up).magnitude;
                    if (left <= 1.3f)
                    {
                        if (inStage > 6d)
                        {
                            Require(false, $"cancelled enemy stayed trapped in the cage: distance={left:F2} user={user.Identity} agent={(user.GetComponent<GymVisitorAgent>() != null ? user.GetComponent<GymVisitorAgent>().State.ToString() : "none")} roaming={user.IsRoaming} hasTarget={user.HasRoamTargetForVerification} blocker={user.LastVisitorRouteBlocker} kinematic={user.GetComponent<Rigidbody>().isKinematic} vel={user.GetComponent<Rigidbody>().linearVelocity}");
                        }
                        if (inStage > 1d && !user.HasRoamTargetForVerification)
                        {
                            user.SelectRoamDestinationForVerification();
                        }
                        return;
                    }
                    results.Add($"leftCage={left:F2}m after={inStage:F1}s");
                    Require(!station.IsOccupied, "station stayed blocked after the enemy left");
                    walker.SetRoamTargetStationForVerification(station);
                    rival.SetRoamTargetStationForVerification(station);
                    Next();
                    return;
                }
                case 4:
                {
                    // Two free roamers aimed at one free machine: one yields.
                    if (inStage < 0.6d) return;
                    int targeting = (walker.RoamTargetStationForVerification == station ? 1 : 0) +
                        (rival.RoamTargetStationForVerification == station ? 1 : 0);
                    Require(targeting <= 1, "two enemies kept heading to the same station");
                    results.Add($"sharedTargetResolved=True remaining={targeting}");
                    Debug.Log($"GYMCHAOS_STATION_CROWD_OK station={station.EquipmentName} " +
                        $"user={user.Identity} walker={walker.Identity} rival={rival.Identity} " +
                        string.Join(" ", results));
                    Finish(0);
                    return;
                }
            }
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            Finish(1);
        }
    }

    private static void Setup()
    {
        UnityEngine.Object.FindAnyObjectByType<GymVisitorDirector>()?.SuspendVisitorSimulationForVerification();
        foreach (GymExerciseStation candidate in UnityEngine.Object.FindObjectsByType<GymExerciseStation>(
            FindObjectsSortMode.None))
        {
            if (candidate != null && candidate.IsSquat && candidate.HasAuthoredSquatBar && !candidate.IsOccupied)
            {
                station = candidate;
                break;
            }
        }
        Require(station != null, "no free squat station");

        List<EnemyFighter> neutral = new List<EnemyFighter>();
        foreach (EnemyFighter fighter in UnityEngine.Object.FindObjectsByType<EnemyFighter>(FindObjectsSortMode.None))
        {
            if (fighter != null && !fighter.IsPolice && !fighter.IsDead && !fighter.IsAggressive &&
                fighter.gameObject.activeInHierarchy && !fighter.IsOnTreadmill && fighter.IsRoaming &&
                fighter.Identity != BodybuilderIdentity.Manwithsuit1 &&
                (fighter.GetComponent<GymVisitorAgent>() == null ||
                 fighter.GetComponent<GymVisitorAgent>().State == GymVisitorAgent.VisitorState.FreeRoaming))
            {
                neutral.Add(fighter);
            }
        }
        Require(neutral.Count >= 3, $"need three neutral enemies, found {neutral.Count}");
        user = neutral[0];
        walker = neutral[1];
        rival = neutral[2];
        Require(station.TryReserveEnemySquatApproach(user), "could not reserve the squat station");
    }

    private static void Next()
    {
        stage++;
        stageStarted = EditorApplication.timeSinceStartup;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException("GYMCHAOS_STATION_CROWD_FAIL " + message);
        }
    }

    private static void RestoreOriginalProgressionSave()
    {
        if (!GymChaosVerifierPrefs.HasKey(OriginalSavePresentKey)) return;
        if (GymChaosVerifierPrefs.GetBool(OriginalSavePresentKey, false))
            PlayerPrefs.SetString(ProgressionSaveKey, GymChaosVerifierPrefs.GetString(OriginalSaveKey, string.Empty));
        else
            PlayerPrefs.DeleteKey(ProgressionSaveKey);
        PlayerPrefs.Save();
        GymChaosVerifierPrefs.DeleteKey(OriginalSaveKey);
        GymChaosVerifierPrefs.DeleteKey(OriginalSavePresentKey);
    }

    private static void Finish(int code)
    {
        if (finished) return;
        finished = true;
        resultCode = code; GymChaosVerifierExit.Record(resultCode);
        SessionState.EraseBool(RequestedKey);
        EditorApplication.update -= Tick;
        EditorApplication.isPlaying = false;
    }
}
#endif
