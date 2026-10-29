#if UNITY_EDITOR
using System;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Measures Goku's visible body while the long-range flight chase is active.
// The flight must stay low enough to read as a chase (body slightly above the
// player's head, not near the ceiling) and the visible scan must stay enabled
// and inside the chasing player's camera frustum.
[InitializeOnLoad]
public static class GymChaosGokuFlightVerifier
{
    private const string RequestedKey = "GymChaos.GokuFlightVerificationRequested";
    private const string OriginalSaveKey = "GymChaos.GokuFlightVerificationOriginalSave";
    private const string OriginalSavePresentKey =
        "GymChaos.GokuFlightVerificationOriginalSavePresent";
    private const string ProgressionSaveKey = "GymChaos.Progression.v1";
    // The horizontal flying body reads as a chase at the player's chest:
    // belly roughly 0.8-1.1 m above the floor, back below head height.
    private const float MaxBodyTopAboveFloor = 2.0f;
    private const float MinBodyBottomAboveFloor = 0.6f;
    private const float MaxBodyBottomAboveFloor = 1.15f;
    // Every flight must use the same altitude above the floor.
    private const float MaxFlightRootSpread = 0.06f;
    // A grounded Goku stands on the floor; the root is frozen on Y there, so
    // any offset is a permanent hover (and punches happen mid-air).
    private const float MaxGroundedRootOffset = 0.04f;
    private const float MaxGroundedFeetAboveFloor = 0.15f;

    private static double started;
    private static double stageStarted;
    private static int lastFrame;
    private static int stage;
    private static int flyingSamples;
    private static bool finished;
    private static int resultCode;
    private static EnemyFighter goku;
    private static PlayerMovement player;
    private static float groundY;
    private static float floorTopY;
    private static Bounds floorArea;
    private static Vector3 lastSamplePosition;
    private static Vector3 gokuStartPosition;
    private static GameObject obstacle;
    private static GameObject groundWall;
    private static bool groundRouteSeen;
    private static double groundNoClearSince;
    private static bool obstacleSawFlight;
    private static float maxVerticalExtent;
    private static int tailFirstSamples;
    private static float worstTop;
    private static float worstBottomLow;
    private static float worstBottomHigh;
    private static int invisibleSamples;
    private static string lastDetails;
    private static float flightRootLow;
    private static float flightRootHigh;
    private static int flightRootSamples;
    private static int attackStartsBefore;
    private static int auraSamples;
    private static int auraHidden;
    private static int auraTrailing;
    private static int auraFastSamples;
    private static int auraMisaligned;
    private static string auraDetails = "none";

    static GymChaosGokuFlightVerifier()
    {
        if (SessionState.GetBool(RequestedKey, false))
        {
            Hook();
        }
    }

    [MenuItem("Tools/GymChaos/Run Goku Flight Verification")]
    public static void Run()
    {
        SessionState.SetBool(RequestedKey, true);
        // A negative-reputation save auto-aggros every visitor, which starts
        // Goku's chase before the measured setup. Run from a neutral save.
        // Keep an older backup: after a killed run the live key is already
        // the neutral one and the real save only exists in the backup.
        if (!GymChaosVerifierPrefs.HasKey(OriginalSavePresentKey))
        {
            GymChaosVerifierPrefs.SetBool(OriginalSavePresentKey, PlayerPrefs.HasKey(ProgressionSaveKey));
            GymChaosVerifierPrefs.SetString(OriginalSaveKey, PlayerPrefs.GetString(ProgressionSaveKey, string.Empty));
        }
        PlayerPrefs.DeleteKey(ProgressionSaveKey);
        PlayerPrefs.Save();
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
        flyingSamples = 0;
        finished = false;
        resultCode = 1; GymChaosVerifierExit.Record(resultCode);
        goku = null;
        player = null;
        worstTop = float.NegativeInfinity;
        worstBottomLow = float.PositiveInfinity;
        worstBottomHigh = float.NegativeInfinity;
        invisibleSamples = 0;
        flightRootLow = float.PositiveInfinity;
        flightRootHigh = float.NegativeInfinity;
        flightRootSamples = 0;
        attackStartsBefore = 0;
        auraSamples = 0;
        auraHidden = 0;
        auraTrailing = 0;
        auraFastSamples = 0;
        auraMisaligned = 0;
        auraDetails = "none";
        maxVerticalExtent = 0f;
        tailFirstSamples = 0;
        lastDetails = "none";
    }

    private static void PlayModeChanged(PlayModeStateChange change)
    {
        if (change == PlayModeStateChange.EnteredPlayMode)
        {
            // Entering play mode reloads the domain, which zeroes the statics
            // that Run() prepared.
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
            if (EditorApplication.timeSinceStartup - started > 210d)
            {
                throw new InvalidOperationException(
                    $"Goku flight verification timed out at stage {stage}: {lastDetails}");
            }
            if (Time.frameCount == lastFrame)
            {
                return;
            }
            lastFrame = Time.frameCount;

            if (stage == 0)
            {
                // Let the bootstrap spawn everyone and settle the scans.
                if (Time.frameCount < 3)
                {
                    return;
                }
                player = UnityEngine.Object.FindAnyObjectByType<PlayerMovement>();
                EnemyFighter[] fighters = UnityEngine.Object.FindObjectsByType<EnemyFighter>(
                    FindObjectsSortMode.None);
                for (int i = 0; i < fighters.Length; i++)
                {
                    if (fighters[i] != null && fighters[i].Identity == BodybuilderIdentity.Goku)
                    {
                        goku = fighters[i];
                        break;
                    }
                }
                if (player == null || goku == null)
                {
                    if (EditorApplication.timeSinceStartup - started > 40d)
                    {
                        throw new InvalidOperationException(
                            $"Missing actors: player={player != null} goku={goku != null}.");
                    }
                    return;
                }
                Renderer floor = GameObject.Find("Rubber Floor")?.GetComponent<Renderer>();
                if (floor == null)
                {
                    throw new InvalidOperationException("Rubber Floor is missing.");
                }
                // Visitors may still be arriving or leaving (Goku rides his
                // cloud high above the city). Freeze the schedule and put Goku
                // on the gym floor so the chase starts from a real ground pose.
                GymVisitorDirector director =
                    UnityEngine.Object.FindAnyObjectByType<GymVisitorDirector>();
                if (director == null)
                {
                    return;
                }
                // Goku's cloud arrival finishes through a vehicle callback that
                // re-stages him outside. Let it complete before placing him.
                foreach (GymVisitorVehicle vehicle in
                    UnityEngine.Object.FindObjectsByType<GymVisitorVehicle>(FindObjectsSortMode.None))
                {
                    if (vehicle != null && vehicle.IsCloud &&
                        (vehicle.IsDriving || vehicle.HasMountedRider))
                    {
                        if (EditorApplication.timeSinceStartup - started > 90d)
                        {
                            throw new InvalidOperationException("Goku's cloud arrival never finished.");
                        }
                        return;
                    }
                }
                director.PauseVisitorScheduleForVerification();
                director.SuspendVisitorSimulationForVerification();
                Bounds floorBounds = floor.bounds;
                Vector3 gokuStart = new Vector3(
                    floorBounds.min.x + 2f, floorBounds.max.y, floorBounds.center.z);
                Vector3 target = new Vector3(
                    Mathf.Min(gokuStart.x + 11f, floorBounds.max.x - 1.5f),
                    player.transform.position.y, floorBounds.center.z);
                floorTopY = floorBounds.max.y;
                floorArea = floorBounds;
                gokuStartPosition = gokuStart;
                goku.SetVisitorSpawnPose(
                    gokuStart, Quaternion.LookRotation(Vector3.right, Vector3.up));
                MovePlayer(target);
                stage = 10;
                stageStarted = EditorApplication.timeSinceStartup;
                return;
            }

            if (stage == 10)
            {
                if (Time.frameCount % 10 == 0)
                {
                    Debug.Log(
                        $"GYMCHAOS_GOKU_FLIGHT_SETTLE pos={goku.transform.position} " +
                        $"grounded={goku.IsGokuGrounded} flying={goku.IsFlying} " +
                        $"active={goku.gameObject.activeInHierarchy}");
                }
                if (EditorApplication.timeSinceStartup - stageStarted < 0.5d)
                {
                    return;
                }
                float driftFromStart = Vector3.ProjectOnPlane(
                    goku.transform.position - gokuStartPosition, Vector3.up).magnitude;
                if (Mathf.Abs(goku.transform.position.y - floorTopY) > 0.6f || driftFromStart > 3f)
                {
                    // A cloud arrival callback that was already in flight can
                    // remount Goku after the schedule pause. Dismount again and
                    // wait for a stable floor pose.
                    if (EditorApplication.timeSinceStartup - started > 60d)
                    {
                        throw new InvalidOperationException(
                            $"Goku did not settle on the gym floor: y={goku.transform.position.y:F2} " +
                            $"floor={floorTopY:F2}.");
                    }
                    UnityEngine.Object.FindAnyObjectByType<GymVisitorDirector>()?
                        .SuspendVisitorSimulationForVerification();
                        goku.EndVisitorVehicleRide(
                        gokuStartPosition, Quaternion.LookRotation(Vector3.right, Vector3.up));
                    Rigidbody gokuBody = goku.GetComponent<Rigidbody>();
                    Vector3 floorPosition = new Vector3(
                        gokuStartPosition.x, floorTopY, gokuStartPosition.z);
                    goku.transform.position = floorPosition;
                    if (gokuBody != null)
                    {
                        gokuBody.position = floorPosition;
                    }
                    Physics.SyncTransforms();
                    stageStarted = EditorApplication.timeSinceStartup;
                    return;
                }
                groundY = floorTopY;
                // The verifier itself dismounts Goku through the visitor
                // vehicle path above; that path must leave him standing on
                // the floor, not hovering at a renderer-derived height.
                float settledOffset = goku.transform.position.y - floorTopY;
                if (Mathf.Abs(settledOffset) > MaxGroundedRootOffset)
                {
                    throw new InvalidOperationException(
                        $"GYMCHAOS_GOKU_GROUNDED_HEIGHT_FAIL stage=settle rootAboveFloor={settledOffset:F3}");
                }
                goku.SetAggressiveForVerification(player);
                Debug.Log(
                    $"GYMCHAOS_GOKU_FLIGHT_HEIGHT_SETUP goku={goku.transform.position} " +
                    $"player={player.transform.position} groundY={groundY:F2}");
                stage = 1;
                stageStarted = EditorApplication.timeSinceStartup;
                return;
            }

            if (stage == 1)
            {
                // Keep the player far away so the flight lasts long enough to sample.
                Vector3 away = Vector3.ProjectOnPlane(
                    player.transform.position - goku.transform.position, Vector3.up);
                if (away.magnitude < 7f && away.sqrMagnitude > 0.01f)
                {
                    MovePlayer(goku.transform.position + away.normalized * 9f);
                }
                AimPlayerCameraAtGoku();

                if (goku.IsFlying)
                {
                    SampleFlightRoot();
                    Sample();
                    SampleAura();
                    flyingSamples++;
                    if (flyingSamples >= 60)
                    {
                        Evaluate();
                        return;
                    }
                }
                if (EditorApplication.timeSinceStartup - stageStarted > 25d)
                {
                    throw new InvalidOperationException(
                        $"Goku did not sustain flight: samples={flyingSamples} " +
                        $"flying={goku.IsFlying} {lastDetails}");
                }
                return;
            }

            if (stage == 2)
            {
                // Obstacle phase: while Goku is still chasing in flight, move
                // the player 10 m away along the roomiest floor axis and drop
                // a wall halfway. Flight must route around it instead of
                // hovering against the wall until the player moves.
                Vector3 from = goku.transform.position;
                Vector3[] axes = { Vector3.right, Vector3.left, Vector3.forward, Vector3.back };
                Vector3 axis = Vector3.right;
                float reach = -1f;
                // Prefer the roomiest axis, but the player must stand where
                // Goku's body fits: a slot between two treadmills is not a
                // wall-detour test, Goku cannot physically get there.
                System.Collections.Generic.List<Vector3> route =
                    new System.Collections.Generic.List<Vector3>();
                float bestScore = float.NegativeInfinity;
                foreach (Vector3 candidate in axes)
                {
                    float free = candidate.x > 0f ? floorArea.max.x - from.x
                        : candidate.x < 0f ? from.x - floorArea.min.x
                        : candidate.z > 0f ? floorArea.max.z - from.z
                        : from.z - floorArea.min.z;
                    for (float candidateReach = Mathf.Min(10f, free - 1.2f);
                        candidateReach >= 6f; candidateReach -= 0.5f)
                    {
                        Vector3 candidateTarget = from + candidate * candidateReach;
                        if (!goku.TryBuildVisitorRoute(candidateTarget, route))
                        {
                            continue;
                        }
                        if (candidateReach > bestScore)
                        {
                            bestScore = candidateReach;
                            axis = candidate;
                            reach = candidateReach;
                        }
                        break;
                    }
                }
                if (reach < 0f)
                {
                    throw new InvalidOperationException(
                        $"No reachable obstacle-phase target around goku={from}.");
                }
                Vector3 target = from + axis * reach;
                MovePlayer(target);
                obstacle = GameObject.CreatePrimitive(PrimitiveType.Cube);
                obstacle.name = "Goku Flight Verification Wall";
                obstacle.transform.position = new Vector3(from.x, floorTopY + 1.6f, from.z) +
                    axis * (reach * 0.45f);
                obstacle.transform.rotation = Quaternion.LookRotation(axis, Vector3.up);
                obstacle.transform.localScale = new Vector3(3.0f, 3.2f, 0.3f);
                Physics.SyncTransforms();
                Debug.Log(
                    $"GYMCHAOS_GOKU_FLIGHT_OBSTACLE_SETUP goku={from} player={player.transform.position} " +
                    $"wall={obstacle.transform.position} reach={reach:F2}");
                obstacleSawFlight = false;
                stage = 4;
                stageStarted = EditorApplication.timeSinceStartup;
                return;
            }

            if (stage == 4)
            {
                obstacleSawFlight |= goku.IsFlying;
                if (goku.IsFlying)
                {
                    SampleFlightRoot();
                }
                float distance = Vector3.ProjectOnPlane(
                    player.transform.position - goku.transform.position, Vector3.up).magnitude;
                if (Time.frameCount % 20 == 0)
                {
                    Debug.Log(
                        $"GYMCHAOS_GOKU_FLIGHT_OBSTACLE_SAMPLE t={EditorApplication.timeSinceStartup - stageStarted:F1} " +
                        $"goku={goku.transform.position} distance={distance:F2} flying={goku.IsFlying} blocker={goku.GokuFlightLastBlocker} " +
                        goku.GokuFlightDiagnosticsForVerification);
                }
                if (distance < 2.6f)
                {
                    if (!obstacleSawFlight)
                    {
                        throw new InvalidOperationException(
                            "Goku reached the player without flying in the obstacle phase.");
                    }
                    Debug.Log(
                        $"GYMCHAOS_GOKU_FLIGHT_OBSTACLE_OK seconds=" +
                        $"{EditorApplication.timeSinceStartup - stageStarted:F1} distance={distance:F2}");
                    if (flightRootSamples == 0 || flightRootHigh - flightRootLow > MaxFlightRootSpread)
                    {
                        throw new InvalidOperationException(
                            $"GYMCHAOS_GOKU_FLIGHT_ALTITUDE_FAIL samples={flightRootSamples} " +
                            $"rootLow={flightRootLow:F3} rootHigh={flightRootHigh:F3}");
                    }
                    Debug.Log(
                        $"GYMCHAOS_GOKU_FLIGHT_ALTITUDE_OK samples={flightRootSamples} " +
                        $"rootLow={flightRootLow:F3} rootHigh={flightRootHigh:F3}");
                    attackStartsBefore = goku.AttackStartsForVerification;
                    stage = 5;
                    stageStarted = EditorApplication.timeSinceStartup;
                    return;
                }
                if (EditorApplication.timeSinceStartup - stageStarted > 12d)
                {
                    throw new InvalidOperationException(
                        $"GYMCHAOS_GOKU_FLIGHT_OBSTACLE_FAIL Goku stayed behind the wall: " +
                        $"goku={goku.transform.position} distance={distance:F2} flying={goku.IsFlying} blocker={goku.GokuFlightLastBlocker}");
                }
                return;
            }

            if (stage == 5)
            {
                // The chase ends in a punch. Goku must have landed first: the
                // first punch after the flight starts from the floor.
                if (goku.AttackStartsForVerification > attackStartsBefore)
                {
                    float rootOffset = goku.transform.position.y - floorTopY;
                    Renderer body = FindBodyRenderer();
                    float feet = body != null
                        ? BakeWorldBounds((SkinnedMeshRenderer)body).min.y - floorTopY
                        : float.NaN;
                    string details =
                        $"rootAboveFloor={rootOffset:F3} feetAboveFloor={feet:F3} " +
                        $"grounded={goku.IsGokuGrounded} flightActive={goku.IsGokuFlightActive}";
                    if (!goku.IsGokuGrounded || goku.IsGokuFlightActive ||
                        Mathf.Abs(rootOffset) > MaxGroundedRootOffset ||
                        !(feet <= MaxGroundedFeetAboveFloor && feet >= -MaxGroundedFeetAboveFloor))
                    {
                        throw new InvalidOperationException(
                            $"GYMCHAOS_GOKU_GROUNDED_HEIGHT_FAIL stage=punch {details}");
                    }
                    Debug.Log($"GYMCHAOS_GOKU_LANDED_PUNCH_OK {details}");
                    // Saiyan punch: 3x a member's damage, much stronger knockback.
                    EnemyFighter member = null;
                    foreach (EnemyFighter f in UnityEngine.Object.FindObjectsByType<EnemyFighter>(FindObjectsSortMode.None))
                    {
                        if (f != null && f != goku && !f.IsPolice) { member = f; break; }
                    }
                    float gokuDamage = goku.GetPlayerPunchDamage();
                    float memberDamage = member != null ? member.GetPlayerPunchDamage() : 5f;
                    Vector3 gokuImpact = goku.GetGokuPlayerImpact(Vector3.forward);
                    float horizontalPush = new Vector2(gokuImpact.x, gokuImpact.z).magnitude;
                    string punch = $"damage={gokuDamage:F1} memberDamage={memberDamage:F1} push={horizontalPush:F1} lift={gokuImpact.y:F1}";
                    if (Mathf.Abs(gokuDamage - memberDamage * 3f) > 0.01f || horizontalPush < 9f * 1.5f || gokuImpact.y < 2f)
                    {
                        throw new InvalidOperationException($"GYMCHAOS_GOKU_PUNCH_POWER_FAIL {punch}");
                    }
                    Debug.Log($"GYMCHAOS_GOKU_PUNCH_POWER_OK {punch}");
                    SetUpGroundChase();
                    stage = 6;
                    stageStarted = EditorApplication.timeSinceStartup;
                    return;
                }
                if (EditorApplication.timeSinceStartup - stageStarted > 8d)
                {
                    throw new InvalidOperationException(
                        $"Goku did not punch after the flight chase: {goku.GokuFlightDiagnosticsForVerification}");
                }
                return;
            }

            if (stage == 6)
            {
                // Original failure: closer than the 5.2 m flight distance Goku
                // chases on foot, and a machine between him and the player
                // left him standing with no-clear-direction. He must walk the
                // room route around the wall and reach the player.
                string reason = goku.ChaseStopReasonForVerification;
                groundRouteSeen |= reason == "chasing-route";
                if (reason == "no-clear-direction")
                {
                    if (groundNoClearSince <= 0d)
                    {
                        groundNoClearSince = EditorApplication.timeSinceStartup;
                    }
                }
                else
                {
                    groundNoClearSince = 0d;
                }
                float distance = Vector3.ProjectOnPlane(
                    player.transform.position - goku.transform.position, Vector3.up).magnitude;
                if (Time.frameCount % 20 == 0)
                {
                    Debug.Log($"GYMCHAOS_GOKU_GROUND_CHASE_SAMPLE distance={distance:F2} " +
                        $"flying={goku.IsGokuFlightActive} {goku.GokuFlightDiagnosticsForVerification}");
                }
                if (goku.IsGokuFlightActive)
                {
                    throw new InvalidOperationException(
                        $"GYMCHAOS_GOKU_GROUND_CHASE_FAIL Goku took off inside the flight distance: distance={distance:F2}");
                }
                if (groundNoClearSince > 0d && EditorApplication.timeSinceStartup - groundNoClearSince > 2d)
                {
                    throw new InvalidOperationException(
                        $"GYMCHAOS_GOKU_GROUND_CHASE_FAIL Goku stood with no-clear-direction: distance={distance:F2} " +
                        goku.GokuFlightDiagnosticsForVerification);
                }
                if (distance < 2.6f)
                {
                    if (!groundRouteSeen)
                    {
                        throw new InvalidOperationException(
                            "GYMCHAOS_GOKU_GROUND_CHASE_FAIL the wall was passed without the route fallback.");
                    }
                    Debug.Log($"GYMCHAOS_GOKU_GROUND_CHASE_OK seconds=" +
                        $"{EditorApplication.timeSinceStartup - stageStarted:F1} distance={distance:F2}");
                    UnityEngine.Object.Destroy(groundWall);
                    groundWall = null;
                    StartLongFlight();
                    stage = 7;
                    stageStarted = EditorApplication.timeSinceStartup;
                    return;
                }
                if (EditorApplication.timeSinceStartup - stageStarted > 15d)
                {
                    throw new InvalidOperationException(
                        $"GYMCHAOS_GOKU_GROUND_CHASE_FAIL Goku did not reach the player around the wall: " +
                        $"distance={distance:F2} routeSeen={groundRouteSeen} {goku.GokuFlightDiagnosticsForVerification}");
                }
                return;
            }

            float gap = Vector3.ProjectOnPlane(
                player.transform.position - goku.transform.position, Vector3.up).magnitude;
            if (stage == 7 || stage == 11)
            {
                // Long chase in flight; then the player stops 5 m short
                // (stage 7) or leaves the gym (stage 11).
                if (goku.IsFlying)
                {
                    if (stage == 7)
                    {
                        Vector3 toPlayer = Vector3.ProjectOnPlane(
                            player.transform.position - goku.transform.position, Vector3.up).normalized;
                        MovePlayer(goku.transform.position + toPlayer * 5f);
                        stage = 8;
                    }
                    else
                    {
                        GymDoorway door = GymDoorway.Instance;
                        Vector3 outward = Vector3.ProjectOnPlane(
                            door.ExteriorPoint - door.InteriorPoint, Vector3.up).normalized;
                        MovePlayer(door.ExteriorPoint + outward * 3f, false);
                        stage = 12;
                    }
                    stageStarted = EditorApplication.timeSinceStartup;
                    return;
                }
                if (EditorApplication.timeSinceStartup - stageStarted > 10d)
                {
                    throw new InvalidOperationException(
                        $"Goku did not take off for the long chase (stage {stage}): gap={gap:F2} " +
                        goku.GokuFlightDiagnosticsForVerification);
                }
                return;
            }

            if (stage == 8)
            {
                // Hysteresis: once airborne Goku keeps flying until close and
                // does not land as soon as the gap drops under the take-off range.
                if (!goku.IsFlying)
                {
                    if (gap > 3.75f)
                    {
                        throw new InvalidOperationException(
                            $"GYMCHAOS_GOKU_FLIGHT_HYSTERESIS_FAIL landed at gap={gap:F2} " +
                            goku.GokuFlightDiagnosticsForVerification);
                    }
                    Debug.Log($"GYMCHAOS_GOKU_FLIGHT_HYSTERESIS_OK landedAt={gap:F2}");
                    stage = 9;
                    stageStarted = EditorApplication.timeSinceStartup;
                    return;
                }
                if (EditorApplication.timeSinceStartup - stageStarted > 6d)
                {
                    throw new InvalidOperationException(
                        $"GYMCHAOS_GOKU_FLIGHT_HYSTERESIS_FAIL never landed: gap={gap:F2}");
                }
                return;
            }

            if (stage == 9)
            {
                if (goku.IsGokuGrounded)
                {
                    StartLongFlight();
                    stage = 11;
                    stageStarted = EditorApplication.timeSinceStartup;
                }
                return;
            }

            if (stage == 12)
            {
                if (Time.frameCount % 30 == 0)
                {
                    Debug.Log($"GYMCHAOS_GOKU_OUTSIDE_SAMPLE goku={goku.transform.position} gap={gap:F2} " +
                        $"flying={goku.IsGokuFlightActive} reason={goku.ChaseStopReasonForVerification}");
                }
                if (GymOutdoorBuilder.IsPlayerOutsideGym(goku.transform.position) && goku.IsGokuGrounded)
                {
                    Debug.Log($"GYMCHAOS_GOKU_FOLLOW_OUTSIDE_OK seconds=" +
                        $"{EditorApplication.timeSinceStartup - stageStarted:F1} gap={gap:F2}");
                    Finish(0);
                    return;
                }
                if (EditorApplication.timeSinceStartup - stageStarted > 25d)
                {
                    throw new InvalidOperationException(
                        $"GYMCHAOS_GOKU_FOLLOW_OUTSIDE_FAIL goku={goku.transform.position} gap={gap:F2} " +
                        $"flying={goku.IsGokuFlightActive} reason={goku.ChaseStopReasonForVerification}");
                }
            }
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            Finish(1);
        }
    }

    private static void SetUpGroundChase()
    {
        if (obstacle != null)
        {
            UnityEngine.Object.Destroy(obstacle);
            obstacle = null;
        }
        Vector3 playerPosition = player.transform.position;
        Vector3[] axes = { Vector3.right, Vector3.left, Vector3.forward, Vector3.back };
        System.Collections.Generic.List<Vector3> route = new System.Collections.Generic.List<Vector3>();
        foreach (Vector3 axis in axes)
        {
            Vector3 start = new Vector3(playerPosition.x, floorTopY, playerPosition.z) + axis * 4.2f;
            if (!floorArea.Contains(new Vector3(start.x, floorArea.center.y, start.z)))
            {
                continue;
            }
            goku.SetVisitorSpawnPose(start, Quaternion.LookRotation(-axis, Vector3.up));
            goku.transform.position = start;
            Physics.SyncTransforms();
            Vector3 wallCenter = new Vector3(playerPosition.x, floorTopY + 1.6f, playerPosition.z) + axis * 2.1f;
            groundWall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            groundWall.name = "Goku Ground Chase Verification Wall";
            groundWall.transform.SetPositionAndRotation(wallCenter, Quaternion.LookRotation(axis, Vector3.up));
            groundWall.transform.localScale = new Vector3(2.6f, 3.2f, 0.3f);
            Physics.SyncTransforms();
            if (goku.TryBuildVisitorRoute(new Vector3(playerPosition.x, floorTopY, playerPosition.z), route))
            {
                goku.SetAggressiveForVerification(player);
                groundRouteSeen = false;
                groundNoClearSince = 0d;
                Debug.Log($"GYMCHAOS_GOKU_GROUND_CHASE_SETUP goku={start} player={playerPosition} wall={wallCenter}");
                return;
            }
            UnityEngine.Object.Destroy(groundWall);
            groundWall = null;
        }
        throw new InvalidOperationException("No floor axis with a route around the ground-chase wall.");
    }

    private static void SampleFlightRoot()
    {
        float root = goku.transform.position.y - floorTopY;
        flightRootLow = Mathf.Min(flightRootLow, root);
        flightRootHigh = Mathf.Max(flightRootHigh, root);
        flightRootSamples++;
    }

    private static void Sample()
    {
        Renderer body = FindBodyRenderer();
        if (body == null)
        {
            invisibleSamples++;
            lastDetails = "no enabled body renderer";
            return;
        }
        // Renderer AABBs of the rotated skinned scan are conservative boxes;
        // skinned vertex positions describe the silhouette the player sees.
        Bounds bounds = BakeWorldBounds((SkinnedMeshRenderer)body);
        float top = bounds.max.y - groundY;
        float bottom = bounds.min.y - groundY;
        worstTop = Mathf.Max(worstTop, top);
        if (bottom < worstBottomLow)
        {
            worstBottomLow = bottom;
            Debug.Log($"GYMCHAOS_GOKU_FLIGHT_LOWEST sample={flyingSamples} bottom={bottom:F2} " +
                $"top={top:F2} root={goku.transform.position.y - groundY:F2} size={bounds.size}");
        }
        worstBottomHigh = Mathf.Max(worstBottomHigh, bottom);
        // Flight is a horizontal Superman pose; a standing-height silhouette
        // means the root pitch and the authored flying clip were stacked.
        maxVerticalExtent = Mathf.Max(maxVerticalExtent, bounds.size.y);
        Transform head = FindBone((SkinnedMeshRenderer)body, "DEF-spine.005");
        Transform hips = FindBone((SkinnedMeshRenderer)body, "DEF-spine");
        // The head must lead the actual travel direction, which is not the
        // player direction while Goku detours around an obstacle.
        Vector3 moved = Vector3.ProjectOnPlane(goku.transform.position - lastSamplePosition, Vector3.up);
        bool hasPrevious = flyingSamples > 0;
        lastSamplePosition = goku.transform.position;
        float headLead = float.NaN;
        if (head != null && hips != null && hasPrevious && moved.magnitude > 0.02f)
        {
            headLead = Vector3.Dot(
                Vector3.ProjectOnPlane(head.position - hips.position, Vector3.up).normalized,
                moved.normalized);
            if (headLead <= 0.3f)
            {
                tailFirstSamples++;
            }
        }
        else if (head == null || hips == null)
        {
            tailFirstSamples++;
        }

        Camera camera = Camera.main;
        bool inFrustum = camera != null &&
            GeometryUtility.TestPlanesAABB(GeometryUtility.CalculateFrustumPlanes(camera), bounds);
        if (!inFrustum)
        {
            invisibleSamples++;
        }
        lastDetails =
            $"pos={goku.transform.position} blocker={goku.GokuFlightLastBlocker} renderer={body.name} root={goku.transform.position.y - groundY:F2} " +
            $"bottom={bottom:F2} top={top:F2} size={bounds.size} headLead={headLead:F2} " +
            $"inFrustum={inFrustum} camera={(camera != null ? camera.transform.position.ToString() : "none")}";
        if (flyingSamples % 15 == 0)
        {
            Debug.Log($"GYMCHAOS_GOKU_FLIGHT_SAMPLE {lastDetails}");
        }
        if (flyingSamples == 30 && SystemInfo.graphicsDeviceType !=
            UnityEngine.Rendering.GraphicsDeviceType.Null)
        {
            Capture(camera, "goku-flight-player.png");
            Vector3 center = bounds.center;
            Vector3 side = Vector3.Cross(Vector3.up,
                Vector3.ProjectOnPlane(player.transform.position - center, Vector3.up).normalized);
            GameObject sideCameraObject = new GameObject("Goku Flight Side Camera");
            Camera sideCamera = sideCameraObject.AddComponent<Camera>();
            sideCamera.fieldOfView = 60f;
            sideCamera.transform.position = new Vector3(
                center.x, groundY + 1.2f, center.z) + side * 6f;
            sideCamera.transform.rotation = Quaternion.LookRotation(
                new Vector3(center.x, groundY + 1.2f, center.z) - sideCamera.transform.position,
                Vector3.up);
            Capture(sideCamera, "goku-flight-side.png");
            UnityEngine.Object.Destroy(sideCameraObject);
        }
    }

    private static Transform FindBone(SkinnedMeshRenderer renderer, string boneName)
    {
        Transform[] bones = renderer.bones;
        for (int i = 0; i < bones.Length; i++)
        {
            if (bones[i] != null && bones[i].name == boneName)
            {
                return bones[i];
            }
        }
        return null;
    }

    private static Bounds BakeWorldBounds(SkinnedMeshRenderer renderer)
    {
        Mesh baked = new Mesh();
        renderer.BakeMesh(baked, true);
        Vector3[] vertices = baked.vertices;
        Matrix4x4 matrix = renderer.transform.localToWorldMatrix;
        Bounds world = new Bounds(matrix.MultiplyPoint3x4(vertices[0]), Vector3.zero);
        for (int i = 1; i < vertices.Length; i += 7)
        {
            world.Encapsulate(matrix.MultiplyPoint3x4(vertices[i]));
        }
        UnityEngine.Object.Destroy(baked);
        return world;
    }

    private static void Capture(Camera camera, string fileName)
    {
        if (camera == null)
        {
            return;
        }
        RenderTexture target = new RenderTexture(960, 540, 24);
        RenderTexture previousTarget = camera.targetTexture;
        RenderTexture previousActive = RenderTexture.active;
        camera.targetTexture = target;
        camera.Render();
        RenderTexture.active = target;
        Texture2D image = new Texture2D(960, 540, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0, 0, 960, 540), 0, 0);
        image.Apply();
        string directory = System.IO.Path.GetFullPath(
            System.IO.Path.Combine(Application.dataPath, "..", "..", "Logs", "verify"));
        System.IO.Directory.CreateDirectory(directory);
        System.IO.File.WriteAllBytes(
            System.IO.Path.Combine(directory, fileName), image.EncodeToPNG());
        camera.targetTexture = previousTarget;
        RenderTexture.active = previousActive;
        UnityEngine.Object.Destroy(image);
        target.Release();
        UnityEngine.Object.Destroy(target);
        Debug.Log($"GYMCHAOS_GOKU_FLIGHT_CAPTURE file={fileName}");
    }

    private static Renderer FindBodyRenderer()
    {
        SkinnedMeshRenderer[] renderers = goku.GetComponentsInChildren<SkinnedMeshRenderer>(false);
        SkinnedMeshRenderer best = null;
        int bestVertices = 0;
        for (int i = 0; i < renderers.Length; i++)
        {
            SkinnedMeshRenderer candidate = renderers[i];
            if (candidate == null || !candidate.enabled || candidate.sharedMesh == null ||
                candidate.GetComponentInParent<GokuAura>() != null && candidate.name.Contains("Aura"))
            {
                continue;
            }
            if (candidate.sharedMesh.vertexCount > bestVertices)
            {
                best = candidate;
                bestVertices = candidate.sharedMesh.vertexCount;
            }
        }
        return best;
    }

    private static void Evaluate()
    {
        StringBuilder summary = new StringBuilder();
        summary.Append($"samples={flyingSamples} invisible={invisibleSamples} tailFirst={tailFirstSamples} ");
        summary.Append($"maxTop={worstTop:F2} minBottom={worstBottomLow:F2} maxBottom={worstBottomHigh:F2} ");
        summary.Append($"maxVerticalExtent={maxVerticalExtent:F2} last=[{lastDetails}]");

        if (invisibleSamples > flyingSamples / 5 ||
            tailFirstSamples > flyingSamples / 5 ||
            maxVerticalExtent > 1.3f ||
            worstTop > MaxBodyTopAboveFloor ||
            worstBottomLow < MinBodyBottomAboveFloor ||
            worstBottomHigh > MaxBodyBottomAboveFloor)
        {
            throw new InvalidOperationException($"GYMCHAOS_GOKU_FLIGHT_HEIGHT_FAIL {summary}");
        }
        Debug.Log($"GYMCHAOS_GOKU_FLIGHT_HEIGHT_OK {summary}");
        EvaluateAura();
        stage = 2;
    }

    // Super Saiyan aura: visible for the whole flight, drawn by the flame
    // sheets (the old enlarged skinned body copy is gone), and its flames
    // stream back behind him while he flies fast.
    private static void SampleAura()
    {
        GokuAura aura = goku.GetComponent<GokuAura>();
        auraSamples++;
        if (aura == null || !aura.IsShowing || aura.CurrentBlend < 0.99f)
        {
            auraHidden++;
            auraDetails = $"aura={(aura != null)} showing={(aura != null && aura.IsShowing)} " +
                $"blend={(aura != null ? aura.CurrentBlend : 0f):F2}";
            return;
        }
        Vector3 velocity = aura.SmoothedVelocity;
        if (velocity.magnitude < 2f)
        {
            return;
        }
        auraFastSamples++;
        float trailing = Vector3.Dot(aura.FlameDirection, -velocity.normalized);
        if (trailing > 0.6f)
        {
            auraTrailing++;
        }
        Renderer bodyRenderer = FindBodyRenderer();
        Bounds bodyBounds = bodyRenderer != null ? BakeWorldBounds((SkinnedMeshRenderer)bodyRenderer) : default;
        // The flame sheath wraps the flying body: its head-feet line lies
        // along the body's long (horizontal) extent, not upright.
        Vector3 axis = aura.HeadPoint - aura.FeetPoint;
        if (axis.magnitude < 1.2f || Mathf.Abs(axis.normalized.y) > 0.6f)
        {
            auraMisaligned++;
        }
        auraDetails = $"flame={aura.FlameDirection:F2} velocity={velocity:F2} trailing={trailing:F2} " +
            $"head={aura.HeadPoint:F2} feet={aura.FeetPoint:F2} body={bodyBounds.center:F2}/{bodyBounds.size:F2} " +
            $"visible={(bodyRenderer != null ? bodyRenderer.name : "none")} bones={aura.BoneNames}";
    }

    private static void EvaluateAura()
    {
        int skinnedCopies = 0;
        foreach (SkinnedMeshRenderer renderer in goku.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (renderer.name.IndexOf("Aura", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                skinnedCopies++;
            }
        }
        string summary = $"samples={auraSamples} hidden={auraHidden} fast={auraFastSamples} " +
            $"trailing={auraTrailing} misaligned={auraMisaligned} skinnedCopies={skinnedCopies} last=[{auraDetails}]";
        if (auraHidden > 0 || skinnedCopies > 0 || auraFastSamples < 10 ||
            auraTrailing < auraFastSamples * 0.8f || auraMisaligned > auraSamples / 5)
        {
            throw new InvalidOperationException($"GYMCHAOS_GOKU_AURA_FAIL {summary}");
        }
        Debug.Log($"GYMCHAOS_GOKU_AURA_OK {summary}");
    }

    private static void AimPlayerCameraAtGoku()
    {
        Camera camera = Camera.main;
        if (camera == null)
        {
            return;
        }
        Vector3 look = goku.transform.position + Vector3.up * 1f - camera.transform.position;
        if (look.sqrMagnitude > 0.01f)
        {
            camera.transform.rotation = Quaternion.LookRotation(look, Vector3.up);
        }
    }

    private static void StartLongFlight()
    {
        goku.SetVisitorSpawnPose(gokuStartPosition, Quaternion.LookRotation(Vector3.right, Vector3.up));
        Physics.SyncTransforms();
        MovePlayer(new Vector3(
            Mathf.Min(gokuStartPosition.x + 11f, floorArea.max.x - 1.5f),
            player.transform.position.y, floorArea.center.z));
    }

    private static void MovePlayer(Vector3 position, bool keepHeight = true)
    {
        player.ResetMovementForVerification();
        CharacterController controller = player.GetComponent<CharacterController>();
        bool wasEnabled = controller != null && controller.enabled;
        if (wasEnabled)
        {
            controller.enabled = false;
        }
        if (keepHeight)
        {
            position.y = player.transform.position.y;
        }
        player.transform.position = position;
        player.ResetMovementForVerification();
        if (wasEnabled)
        {
            controller.enabled = true;
        }
    }

    private static void RestoreOriginalProgressionSave()
    {
        if (!GymChaosVerifierPrefs.HasKey(OriginalSavePresentKey))
        {
            return;
        }
        if (GymChaosVerifierPrefs.GetBool(OriginalSavePresentKey, false))
        {
            PlayerPrefs.SetString(ProgressionSaveKey, GymChaosVerifierPrefs.GetString(OriginalSaveKey, string.Empty));
        }
        else
        {
            PlayerPrefs.DeleteKey(ProgressionSaveKey);
        }
        PlayerPrefs.Save();
        GymChaosVerifierPrefs.DeleteKey(OriginalSaveKey);
        GymChaosVerifierPrefs.DeleteKey(OriginalSavePresentKey);
    }

    private static void Finish(int code)
    {
        if (finished)
        {
            return;
        }
        finished = true;
        resultCode = code; GymChaosVerifierExit.Record(resultCode);
        SessionState.EraseBool(RequestedKey);
        EditorApplication.update -= Tick;
        EditorApplication.isPlaying = false;
    }
}
#endif
