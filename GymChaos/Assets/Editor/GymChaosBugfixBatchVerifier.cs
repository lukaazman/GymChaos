#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Play-mode checks for the 2026-09-29 bug-fix batch: locker bench prep reach,
/// out-of-combat health regeneration and perfect-rep healing, closed fences
/// around the Protein.com shop, seamless outdoor wall joints, and vehicles
/// resting on the asphalt.
/// </summary>
[InitializeOnLoad]
public static class GymChaosBugfixBatchVerifier
{
    private const string RequestedKey = "GymChaos.BugfixBatchVerificationRequested";
    private static double startedAt;
    private static double phaseStartedAt;
    private static bool finished;
    private static int resultCode;
    private static int phase;
    private static float healthAfterHit;
    private static EnemyFighter aggressor;
    private static readonly List<string> passed = new List<string>();

    static GymChaosBugfixBatchVerifier()
    {
        if (!SessionState.GetBool(RequestedKey, false)) return;
        Hook();
    }

    [MenuItem("Tools/GymChaos/Run Bug-fix Batch Verification")]
    public static void Run()
    {
        finished = false;
        phase = 0;
        passed.Clear();
        resultCode = 1;
        GymChaosVerifierExit.Record(resultCode);
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
            phaseStartedAt = startedAt;
            AudioListener.pause = true;
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
        double now = EditorApplication.timeSinceStartup;
        try
        {
            if (now - startedAt > 120d) throw new TimeoutException($"timed out in phase {phase}");
            PlayerMovement player = UnityEngine.Object.FindAnyObjectByType<PlayerMovement>();
            if (!GymOutdoorBuilder.IsBuilt || !GymBackRoomBuilder.TryGetRoomBounds(out _) ||
                player == null || GymExperienceService.Active == null)
            {
                return;
            }

            switch (phase)
            {
                case 0:
                    if (now - startedAt < 6d) return;
                    CheckBenchPrep();
                    CheckStoreFences();
                    CheckFenceJoints();
                    BeginRegen(player);
                    phase = 1;
                    phaseStartedAt = now;
                    break;
                case 1:
                    if (TickRegen(player, now)) { phase = 2; phaseStartedAt = now; }
                    break;
                case 2:
                    if (TickCombatPause(player, now)) { phase = 3; phaseStartedAt = now; }
                    break;
                case 3:
                    if (TickVehicleGround(now)) Finish(0);
                    break;
            }
        }
        catch (Exception exception)
        {
            Debug.LogError("GYMCHAOS_BUGFIX_BATCH_FAIL " + exception.Message);
            Finish(1);
        }
    }

    // Item 1: every point around either bench offers "Get ready".
    private static void CheckBenchPrep()
    {
        GymExperienceService service = GymExperienceService.Active;
        string[] benches = { "Locker Bench Prep Point Left", "Locker Bench Prep Point Right" };
        int samples = 0;
        foreach (string benchName in benches)
        {
            GameObject bench = GameObject.Find(benchName);
            if (bench == null) throw new InvalidOperationException($"{benchName} missing");
            Vector3 center = bench.transform.position;
            for (int i = 0; i < 8; i++)
            {
                float angle = i * 45f;
                Vector3 offset = Quaternion.Euler(0f, angle, 0f) * Vector3.forward * 3.3f;
                Vector3 probe = center + offset + Vector3.down * 0.9f;
                if (!GymBackRoomBuilder.IsInsideRoom(probe)) continue;
                GymBackRoomInteractable found = service.FindNearbyInteractable(probe, 3.1f);
                if (found == null) throw new InvalidOperationException(
                    $"No interaction {3.3f:F1} m from {benchName} at {probe}");
                // Standing right at the changing locker may prefer the locker.
                if (found.InteractionType != GymBackRoomInteractionType.Prep &&
                    found.InteractionType != GymBackRoomInteractionType.Locker)
                    throw new InvalidOperationException(
                        $"{benchName} probe {probe} resolved {found.InteractionType}");
                samples++;
            }
            GymBackRoomInteractable atBench = service.FindNearbyInteractable(center, 3.1f);
            if (atBench == null || atBench.InteractionType != GymBackRoomInteractionType.Prep)
                throw new InvalidOperationException($"{benchName} itself does not offer Get ready");
        }
        passed.Add($"benchPrep=2 probes={samples} range={GymBackRoomBuilder.BenchPrepInteractionRange:F1}");
        Debug.Log($"GYMCHAOS_BENCH_PREP_RANGE_OK probes={samples} " +
            $"range={GymBackRoomBuilder.BenchPrepInteractionRange:F1}");
    }

    // Item 6: the only way into the shop is the entry walkway, and the
    // strips beside the shop are closed.
    private static void CheckStoreFences()
    {
        if (!GymOutdoorBuilder.HasProteinStoreRoute) throw new InvalidOperationException("store not built");
        string[] required =
        {
            "Protein Store Side Lock North Collision", "Protein Store Side Lock South Collision",
            "Protein Store Entry Fence South Collision", "Protein Store Entry Fence North Collision",
            "Protein Store Facade Guard South", "Protein Store Facade Guard North"
        };
        foreach (string name in required)
            if (GameObject.Find(name) == null) throw new InvalidOperationException($"{name} missing");
        string[] removed =
        {
            "Visitor Road South Wall Between Protein Store Gates",
            "Visitor Road South Wall After Protein Store Gate",
            "Protein Store Entry Fence South Junction Low Wall",
            "Protein Store Entry Fence North Junction Low Wall"
        };
        foreach (string name in removed)
            if (GameObject.Find(name) != null) throw new InvalidOperationException($"{name} still present");

        // The road's south wall is continuous from the gym path to the corner.
        Collider roadWall = GameObject.Find("Visitor Road South Wall Collision").GetComponent<Collider>();
        float y = GymOutdoorBuilder.ParkingBounds.center.y + 0.6f;
        int roadSamples = 0;
        for (float x = roadWall.bounds.min.x + 0.3f; x < roadWall.bounds.max.x - 0.3f; x += 0.5f)
        {
            Vector3 origin = new Vector3(x, y, roadWall.bounds.max.z + 1.5f);
            if (!Physics.Raycast(origin, Vector3.back, out RaycastHit hit, 3f, ~0, QueryTriggerInteraction.Ignore) ||
                hit.collider != roadWall)
                throw new InvalidOperationException($"Road south wall gap at x={x:F1}");
            roadSamples++;
        }

        // From the walkway centre, both sides are fenced all the way to the facade.
        Vector3 walkway = GymProteinStoreEnvironment.StoreEntrancePoint;
        float facadeX = GymProteinStoreEnvironment.FacadeX;
        Collider south = GameObject.Find("Protein Store Entry Fence South Collision").GetComponent<Collider>();
        Collider north = GameObject.Find("Protein Store Entry Fence North Collision").GetComponent<Collider>();
        int corridorSamples = 0;
        for (float x = south.bounds.min.x + 0.2f; x < facadeX - 0.1f; x += 0.4f)
        {
            Vector3 origin = new Vector3(x, walkway.y + 0.6f, walkway.z);
            if (!Physics.Raycast(origin, Vector3.back, out RaycastHit southHit, 4f, ~0, QueryTriggerInteraction.Ignore) ||
                southHit.collider != south ||
                !Physics.Raycast(origin, Vector3.forward, out RaycastHit northHit, 4f, ~0, QueryTriggerInteraction.Ignore) ||
                northHit.collider != north)
                throw new InvalidOperationException($"Store walkway side open at x={x:F1}");
            corridorSamples++;
        }

        // Walking east past the shop on either side hits a side lock.
        Bounds shell = GymProteinStoreEnvironment.ShellFootprint;
        Vector3[] strips =
        {
            new Vector3(facadeX - 1.2f, walkway.y + 0.6f, shell.max.z + 0.9f),
            new Vector3(facadeX - 1.2f, walkway.y + 0.6f, shell.min.z - 1.8f)
        };
        foreach (Vector3 start in strips)
        {
            if (!Physics.Raycast(start, Vector3.right, out RaycastHit hit, 12f, ~0, QueryTriggerInteraction.Ignore) ||
                !hit.collider.name.StartsWith("Protein Store Side Lock"))
                throw new InvalidOperationException(
                    $"Strip beside the shop is open from {start} (hit={(hit.collider != null ? hit.collider.name : "none")})");
        }
        passed.Add($"storeFences road={roadSamples} corridor={corridorSamples} locks=2");
        Debug.Log($"GYMCHAOS_STORE_FENCE_LOCK_OK roadSamples={roadSamples} corridorSamples={corridorSamples} " +
            $"facadeX={facadeX:F2} sideLocks={GymOutdoorFenceFinisher.StoreSideLocks}");
    }

    // Item 8: no doubled walls, no notched corners, redundant pockets gone.
    private static void CheckFenceJoints()
    {
        if (GymOutdoorFenceFinisher.RemainingOverlaps != 0 || GymOutdoorFenceFinisher.ResolvedJoints <= 0)
            throw new InvalidOperationException(
                $"fence joints overlaps={GymOutdoorFenceFinisher.RemainingOverlaps} " +
                $"resolved={GymOutdoorFenceFinisher.ResolvedJoints}");
        string[] removed =
        {
            "Visitor Road North Wall", "Visitor Road North Wall After Bus Bay",
            "Bus Stop East Return Fence Low Wall", "Bus Stop West Return Fence Low Wall"
        };
        foreach (string name in removed)
            if (GameObject.Find(name) != null) throw new InvalidOperationException($"{name} still present");

        // One north boundary line: parking north, bus pocket and bay fence
        // share a centre line.
        string[] line =
        {
            "Parking North Extension Wall", "Bus Bay West Pocket North Wall",
            "Bus Stop Outer Fence Low Wall", "Bus Bay East Pocket North Wall"
        };
        float reference = float.NaN;
        foreach (string name in line)
        {
            GameObject wall = GameObject.Find(name);
            if (wall == null) throw new InvalidOperationException($"{name} missing");
            float z = wall.GetComponent<Renderer>().bounds.center.z;
            if (float.IsNaN(reference)) reference = z;
            else if (Mathf.Abs(z - reference) > 0.02f)
                throw new InvalidOperationException($"{name} is off the north line: z={z:F2} line={reference:F2}");
        }

        // Every wall corner is closed: sample just inside each outer corner
        // of each low wall; a point on a wall end must be inside some wall.
        int corners = 0;
        List<Bounds> walls = CollectLowWalls();
        foreach (Bounds wall in walls)
        {
            bool alongX = wall.size.x >= wall.size.z;
            float[] ends = alongX ? new[] { wall.min.x, wall.max.x } : new[] { wall.min.z, wall.max.z };
            foreach (float end in ends)
            {
                // Is another wall perpendicular and touching this end?
                foreach (Bounds other in walls)
                {
                    if (other == wall) continue;
                    bool otherAlongX = other.size.x >= other.size.z;
                    if (otherAlongX == alongX) continue;
                    bool touches = alongX
                        ? end >= other.min.x - 0.03f && end <= other.max.x + 0.03f &&
                          wall.max.z >= other.min.z - 0.03f && wall.min.z <= other.max.z + 0.03f
                        : end >= other.min.z - 0.03f && end <= other.max.z + 0.03f &&
                          wall.max.x >= other.min.x - 0.03f && wall.min.x <= other.max.x + 0.03f;
                    if (!touches) continue;
                    // The union of the two boxes must cover the joint square
                    // (the other wall's band across this wall's band).
                    Vector2 square = alongX
                        ? new Vector2(other.center.x, wall.center.z)
                        : new Vector2(wall.center.x, other.center.z);
                    if (!Covered(walls, square.x, square.y))
                        throw new InvalidOperationException(
                            $"Open wall corner at x={square.x:F2} z={square.y:F2}");
                    corners++;
                }
            }
        }
        passed.Add($"fenceJoints resolved={GymOutdoorFenceFinisher.ResolvedJoints} corners={corners}");
        Debug.Log($"GYMCHAOS_FENCE_SEAMLESS_OK resolved={GymOutdoorFenceFinisher.ResolvedJoints} " +
            $"overlaps=0 cornersChecked={corners} northLineZ={reference:F2}");
    }

    private static bool Covered(List<Bounds> walls, float x, float z)
    {
        foreach (Bounds wall in walls)
            if (x >= wall.min.x - 0.01f && x <= wall.max.x + 0.01f &&
                z >= wall.min.z - 0.01f && z <= wall.max.z + 0.01f)
                return true;
        return false;
    }

    private static List<Bounds> CollectLowWalls()
    {
        List<Bounds> result = new List<Bounds>();
        GameObject root = GameObject.Find("Gym Exterior (Runtime)");
        if (root == null) return result;
        foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(false))
        {
            string name = renderer.name;
            if (!(name.EndsWith(" Low Wall") || name.EndsWith(" Wall") || name.EndsWith(" Wall South") ||
                  name.EndsWith(" Wall North") || name.EndsWith(" Wall Middle"))) continue;
            Transform coping = renderer.transform.parent.Find(
                (name.EndsWith(" Low Wall") ? name.Substring(0, name.Length - 9) : name) + " Coping");
            if (coping == null) continue;
            Bounds bounds = renderer.bounds;
            if (Mathf.Min(bounds.size.x, bounds.size.z) > 1.2f) continue;
            result.Add(bounds);
        }
        return result;
    }

    // Item 5a: out of combat the player heals; item 5b: a perfect rep heals.
    private static void BeginRegen(PlayerMovement player)
    {
        foreach (EnemyFighter fighter in UnityEngine.Object.FindObjectsByType<EnemyFighter>(FindObjectsSortMode.None))
        {
            if (fighter != null && fighter.IsAggressive) fighter.ResetAggressionForVerification();
        }
        player.ReceiveEnemyPunch(60f, Vector3.zero, null);
        healthAfterHit = player.CurrentHealth;
    }

    private static bool TickRegen(PlayerMovement player, double now)
    {
        if (now - phaseStartedAt < 7.5d) return false;
        float healed = player.CurrentHealth - healthAfterHit;
        if (healed < 3f)
            throw new InvalidOperationException(
                $"No out-of-combat regeneration: {healthAfterHit:F1} -> {player.CurrentHealth:F1}");
        float beforeRep = player.CurrentHealth;
        GymExperienceService.Active.RegisterWorkoutRep(WorkoutResult.Perfect, GymExerciseType.FlatBenchPress, 20);
        float repHeal = player.CurrentHealth - beforeRep;
        if (repHeal < player.PerfectRepHealAmount - 0.01f)
            throw new InvalidOperationException($"Perfect rep did not heal: +{repHeal:F2}");
        float beforeGood = player.CurrentHealth;
        GymExperienceService.Active.RegisterWorkoutRep(WorkoutResult.Good, GymExerciseType.FlatBenchPress, 20);
        if (player.CurrentHealth - beforeGood > 0.5f)
            throw new InvalidOperationException("A non-perfect rep healed the player");
        passed.Add($"regen=+{healed:F1} perfectRep=+{repHeal:F1}");
        Debug.Log($"GYMCHAOS_HEALTH_REGEN_OK regen={healed:F1} perfectRepHeal={repHeal:F1}");

        // Next: an attacking enemy must pause regeneration.
        aggressor = null;
        foreach (EnemyFighter fighter in UnityEngine.Object.FindObjectsByType<EnemyFighter>(FindObjectsSortMode.None))
        {
            // Passive staff (reception, Mark) and dialogue-locked members
            // ignore SetAggressive; picking one let regeneration run on.
            if (fighter != null && fighter.isActiveAndEnabled && !fighter.IsDead && !fighter.IsPolice &&
                !fighter.IsPassive && !fighter.IsDialogueLocked)
            {
                aggressor = fighter;
                break;
            }
        }
        if (aggressor == null) throw new InvalidOperationException("No enemy available for the combat check");
        player.ReceiveEnemyPunch(30f, Vector3.zero, null);
        aggressor.SetAggressiveForVerification(player);
        if (!aggressor.IsAggressive)
            throw new InvalidOperationException($"Combat-check aggressor {aggressor.Identity} did not turn aggressive");
        healthAfterHit = player.CurrentHealth;
        return true;
    }

    private static bool TickCombatPause(PlayerMovement player, double now)
    {
        if (!aggressor.IsAggressive)
        {
            aggressor.SetAggressiveForVerification(player);
        }
        if (player.IsHealthRegenerating && aggressor.IsAggressive)
            throw new InvalidOperationException("Health regenerated while an enemy was attacking");
        if (now - phaseStartedAt < 6d) return false;
        // Any damage the aggressor dealt only lowers health further.
        if (player.CurrentHealth > healthAfterHit + 0.5f)
            throw new InvalidOperationException(
                $"Health rose during combat: {healthAfterHit:F1} -> {player.CurrentHealth:F1}");
        aggressor.ResetAggressionForVerification();
        passed.Add("combatPause=1");
        Debug.Log($"GYMCHAOS_HEALTH_REGEN_COMBAT_PAUSE_OK health={player.CurrentHealth:F1}");
        return true;
    }

    // Item 9: every visible vehicle body rests on the asphalt.
    private static bool TickVehicleGround(double now)
    {
        GymVisitorVehicle[] vehicles = UnityEngine.Object.FindObjectsByType<GymVisitorVehicle>(FindObjectsSortMode.None);
        float floorY = GymOutdoorBuilder.ParkingBounds.center.y;
        int measured = 0;
        float worst = 0f;
        string details = "";
        foreach (GymVisitorVehicle vehicle in vehicles)
        {
            if (vehicle == null || !vehicle.gameObject.activeInHierarchy || vehicle.IsCloud ||
                !vehicle.IsParked || !vehicle.RuntimeVisualReadyForVerification) continue;
            Bounds bounds = vehicle.RuntimeVisualBoundsForVerification;
            if (bounds.size.sqrMagnitude < 0.01f) continue;
            float clearance = bounds.min.y - floorY;
            details += $"{vehicle.name}:{clearance:F3} ";
            if (clearance > 0.005f || clearance < -0.04f)
                throw new InvalidOperationException(
                    $"{vehicle.name} is not on the asphalt: clearance={clearance:F3} m");
            worst = Mathf.Max(worst, Mathf.Abs(clearance));
            measured++;
        }
        if (measured == 0)
        {
            if (now - phaseStartedAt > 30d) throw new InvalidOperationException("No parked vehicle to measure");
            return false;
        }
        passed.Add($"vehicles={measured} worst={worst:F3}");
        Debug.Log($"GYMCHAOS_VEHICLE_GROUND_OK vehicles={measured} {details.Trim()}");
        return true;
    }

    private static void Finish(int code)
    {
        finished = true;
        resultCode = code;
        if (code == 0)
            Debug.Log("GYMCHAOS_BUGFIX_BATCH_OK " + string.Join(" ", passed));
        GymChaosVerifierExit.Record(resultCode);
        SessionState.EraseBool(RequestedKey);
        EditorApplication.isPlaying = false;
    }
}
#endif
