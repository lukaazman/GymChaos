#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// The exercise bikes stand in a 2 x 2 block with slots under a metre wide
/// between them, too narrow for a member. Every slot must be closed by a
/// static collider, and a member roaming or chasing the player across the
/// cardio corner must get around the block instead of wedging in it.
/// </summary>
[InitializeOnLoad]
public static class GymChaosBikeGapVerifier
{
    private const string RequestedKey = "GymChaos.BikeGapVerificationRequested";
    private const float ScenarioSeconds = 12f;
    private static readonly List<Vector3> slots = new List<Vector3>();
    private static readonly List<(Vector3 start, Vector3 goal)> routes = new List<(Vector3, Vector3)>();
    private static EnemyFighter fighter;
    private static PlayerMovement player;
    private static int scenario;
    private static bool chase;
    private static Vector3 goal;
    private static float scenarioStartedAt;
    private static double startedAt;
    private static bool finished;
    private static int resultCode;

    static GymChaosBikeGapVerifier()
    {
        if (!SessionState.GetBool(RequestedKey, false)) return;
        Hook();
    }

    [MenuItem("Tools/GymChaos/Run Bike Gap Verification")]
    public static void Run()
    {
        GymChaosVerifierExit.Record(1);
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
            finished = false;
            resultCode = 1;
            fighter = null;
            scenario = -1;
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
        try
        {
            if (EditorApplication.timeSinceStartup - startedAt > 240d)
            {
                throw new InvalidOperationException($"timed out in scenario {scenario}");
            }
            if (fighter == null)
            {
                if (EditorApplication.timeSinceStartup - startedAt < 4d || !TrySetUp()) return;
                StartScenario(0);
                return;
            }
            float elapsed = Time.time - scenarioStartedAt;
            Vector3 position = fighter.transform.position;
            float remaining = Vector3.ProjectOnPlane(goal - position, Vector3.up).magnitude;
            if (Time.frameCount % 30 == 0)
            {
                Debug.Log($"GYMCHAOS_BIKE_GAP_SAMPLE scenario={scenario} chase={chase} t={elapsed:F1} " +
                    $"pos={position} remaining={remaining:F2} reason={fighter.ChaseStopReasonForVerification}");
            }
            if (remaining < (chase ? 2.2f : 1.0f))
            {
                Debug.Log($"GYMCHAOS_BIKE_GAP_SCENARIO_OK scenario={scenario} chase={chase} seconds={elapsed:F1}");
                if (scenario + 1 >= routes.Count * 2)
                {
                    Debug.Log($"GYMCHAOS_BIKE_GAP_OK slots={slots.Count} scenarios={routes.Count * 2}");
                    Finish(0);
                    return;
                }
                StartScenario(scenario + 1);
                return;
            }
            if (elapsed > ScenarioSeconds)
            {
                throw new InvalidOperationException(
                    $"scenario={scenario} chase={chase} start={routes[scenario / 2].start} stuck at {position} " +
                    $"remaining={remaining:F2} reason={fighter.ChaseStopReasonForVerification}");
            }
        }
        catch (Exception exception)
        {
            Debug.LogError("GYMCHAOS_BIKE_GAP_FAILED " + exception.Message);
            Finish(1);
        }
    }

    // Slots: midpoints between neighbouring bikes and the centre of the block.
    private static bool TrySetUp()
    {
        if (!GymInteriorBuilder.TryGetMainGymBounds(out Bounds gym)) return false;
        player = UnityEngine.Object.FindFirstObjectByType<PlayerMovement>();
        var bikes = new List<Vector3>();
        foreach (GymExerciseStation station in UnityEngine.Object.FindObjectsByType<GymExerciseStation>(
                     FindObjectsSortMode.None))
        {
            if (station.name.Contains("Exercise bike")) bikes.Add(station.transform.position);
        }
        if (bikes.Count < 2 || player == null) return false;
        foreach (EnemyFighter candidate in UnityEngine.Object.FindObjectsByType<EnemyFighter>(
                     FindObjectsSortMode.None))
        {
            GymVisitorAgent agent = candidate != null ? candidate.GetComponent<GymVisitorAgent>() : null;
            if (candidate == null || candidate.IsDead || candidate.IsAggressive ||
                !candidate.IsRoaming || (agent != null && agent.IsBusy) ||
                candidate.GetComponent<Rigidbody>() == null ||
                candidate.Identity == BodybuilderIdentity.Goku ||
                candidate.Identity == BodybuilderIdentity.Ronnie ||
                candidate.Identity == BodybuilderIdentity.Manwithsuit1 ||
                candidate.Identity == BodybuilderIdentity.Mark) continue;
            Vector3 p = candidate.transform.position;
            if (p.x < gym.min.x || p.x > gym.max.x || p.z < gym.min.z || p.z > gym.max.z) continue;
            fighter = candidate;
            break;
        }
        if (fighter == null) return false;
        UnityEngine.Object.FindAnyObjectByType<GymVisitorDirector>()?.PauseVisitorScheduleForVerification();
        float floorY = fighter.transform.position.y;
        Vector3 centre = Vector3.zero;
        slots.Clear();
        for (int i = 0; i < bikes.Count; i++)
        {
            centre += bikes[i];
            for (int j = i + 1; j < bikes.Count; j++)
            {
                if (Vector3.ProjectOnPlane(bikes[i] - bikes[j], Vector3.up).magnitude < 4f &&
                    (Mathf.Abs(bikes[i].x - bikes[j].x) < 0.3f || Mathf.Abs(bikes[i].z - bikes[j].z) < 0.3f))
                {
                    Vector3 mid = (bikes[i] + bikes[j]) * 0.5f;
                    slots.Add(new Vector3(mid.x, floorY, mid.z));
                }
            }
        }
        centre /= bikes.Count;
        slots.Add(new Vector3(centre.x, floorY, centre.z));
        // No member can stand in a slot: each one is inside a static collider.
        foreach (Vector3 slot in slots)
        {
            if (!Physics.CheckSphere(slot + Vector3.up * 1.0f, 0.2f, ~0, QueryTriggerInteraction.Ignore))
            {
                throw new InvalidOperationException($"bike slot {slot} is open");
            }
        }
        // Walk and chase across the cardio corner: around the bike block,
        // never into it.
        Bounds block = new Bounds(slots[slots.Count - 1], Vector3.zero);
        foreach (Vector3 bike in bikes) block.Encapsulate(bike);
        block.Expand(new Vector3(3.6f, 0f, 3.6f));
        routes.Clear();
        Vector3 west = FindOpenFloor(new Vector3(block.min.x, floorY, block.center.z));
        Vector3 south = FindOpenFloor(new Vector3(block.center.x, floorY, block.min.z));
        Vector3 north = FindOpenFloor(new Vector3(block.center.x, floorY, block.max.z));
        routes.Add((north, south));
        routes.Add((west, north));
        Debug.Log($"GYMCHAOS_BIKE_GAP_SETUP fighter={fighter.Identity} slots={string.Join(" ", slots)} " +
            $"north={north} south={south} west={west}");
        return true;
    }

    // Even scenarios roam to the goal, odd ones chase the player standing there.
    private static void StartScenario(int index)
    {
        scenario = index;
        chase = index % 2 == 1;
        Vector3 start = routes[index / 2].start;
        goal = routes[index / 2].goal;
        fighter.ResetAggressionForVerification();
        Place(fighter.transform, start, Quaternion.LookRotation(Vector3.forward));
        if (chase)
        {
            MovePlayer(goal);
            fighter.SetAggressiveForVerification(player);
        }
        else
        {
            MovePlayer(goal + Vector3.forward * 30f);
            bool routed = fighter.QueueRoamDestinationForVerification(goal);
            Debug.Log($"GYMCHAOS_BIKE_GAP_ROUTE scenario={index} start={start} goal={goal} routed={routed}");
        }
        scenarioStartedAt = Time.time;
    }

    // Nearest point around the gym centre where a member fits (2 m clear).
    private static Vector3 FindOpenFloor(Vector3 around)
    {
        for (float radius = 0f; radius < 8f; radius += 0.5f)
        {
            for (int step = 0; step < 16; step++)
            {
                float angle = step * Mathf.PI / 8f;
                Vector3 point = around + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius;
                GymInteriorBuilder.TryGetMainGymBounds(out Bounds gym);
                gym.Expand(new Vector3(-1.5f, 100f, -1.5f));
                if (gym.Contains(point) && !Physics.CheckCapsule(point + Vector3.up * 1.2f, point + Vector3.up * 1.8f, 0.9f,
                        ~0, QueryTriggerInteraction.Ignore))
                {
                    return point;
                }
            }
        }
        throw new InvalidOperationException($"no open floor near {around}");
    }

    private static void MovePlayer(Vector3 position)
    {
        CharacterController controller = player.GetComponent<CharacterController>();
        bool wasEnabled = controller != null && controller.enabled;
        if (wasEnabled) controller.enabled = false;
        player.ResetMovementForVerification();
        position.y = player.transform.position.y;
        player.transform.position = position;
        if (wasEnabled) controller.enabled = true;
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

    private static void Finish(int code)
    {
        if (finished) return;
        finished = true;
        if (fighter != null) fighter.ResetAggressionForVerification();
        resultCode = code; GymChaosVerifierExit.Record(resultCode);
        SessionState.EraseBool(RequestedKey);
        EditorApplication.update -= Tick;
        EditorApplication.isPlaying = false;
    }
}
#endif
