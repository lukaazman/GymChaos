#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Open park, tent and homeless guy (2026-10-15).
/// - The path's south wall is gone and the opening is clear; the open lawn
///   is closed by the gym, the store site and the new park walls (a flood
///   fill from the opening never leaves the park).
/// - The tent stands on grass (no path cell under it), rests on the ground,
///   is solid (mesh colliders) and keeps its textured materials.
/// - The homeless guy runs one whole day: sleeping in the tent, getting up
///   out of the door without his body poking through the canvas, walking to
///   the nearest bench, sitting down with his seat on the bench board,
///   getting up near dusk, walking back and lying down in the tent again.
/// With a graphics device it also writes evidence PNGs to Logs/agent/homeless.
/// </summary>
[InitializeOnLoad]
public static class GymChaosHomelessVerifier
{
    private const string RequestedKey = "GymChaos.HomelessVerificationRequested";
    private const float TimeScale = 2.5f;
    private const float PenetrationLimit = 0.06f;
    private const float ClearanceLimit = 0.04f;
    private const float HoverLimit = 0.06f;
    private const float SeatTolerance = 0.06f;

    private enum Stage
    {
        WaitReady,
        Sleeping,
        WaitStandUp,
        StandingUp,
        WaitSitting,
        Sitting,
        WaitStandFromBench,
        WalkingHome,
        LyingDown,
        TalkWalk,
        TalkSit,
        Done
    }

    private static Stage stage;
    private static double stageStarted;
    private static double startedAt;
    private static bool finished;
    private static int resultCode = 1;
    private static float maxPenetration;
    private static string worstPenetration = "none";
    private static float sleepOutside;
    private static float seatError = float.NaN;
    private static float wakeTime01;
    private static float bedTime01;
    private static readonly List<string> notes = new List<string>();
    private static readonly HashSet<string> captured = new HashSet<string>();
    private static float previousMaximumDeltaTime;
    private static float worstFacing;
    private static float sleepOffCentre;
    private static float sleepHeadBack;
    private static float sleepBagGap;
    private static float walkLeanSum;
    private static int walkLeanSamples;
    // Talking to him: mid-walk he stops in an idle, on the bench he keeps
    // sitting; his day waits for the conversation to end.
    private static bool walkTalkDone;
    private static bool sitTalkDone;
    private static Vector3 talkStartPosition;
    private static GymHomelessAgent.HomelessState talkStartState;
    private static string talkClips = "";
    // Tent clearance while crawling in/out: body sunk into the bedding,
    // floor or doormat, poking out through the canvas walls, or inside a
    // pole or prop.
    private static float maxSink;
    private static string worstSink = "none";
    private static float maxWall;
    private static float maxHover;
    private static string worstHover = "none";
    private static string worstWall = "none";
    private static int propHits;
    private static string worstProp = "none";
    private static Collider[] supportColliders;
    private static Collider[] solidColliders;
    private static readonly Dictionary<string, int> hitsByPart = new Dictionary<string, int>();

    static GymChaosHomelessVerifier()
    {
        if (!SessionState.GetBool(RequestedKey, false)) return;
        Hook();
    }

    [MenuItem("Tools/GymChaos/Run Homeless Guy Verification")]
    public static void Run()
    {
        finished = false;
        resultCode = 1;
        GymChaosVerifierExit.Record(resultCode);
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
            GymChaosVerifierClock.BeginFixedStep();
            startedAt = GymChaosVerifierClock.Now;
            stage = Stage.WaitReady;
            stageStarted = startedAt;
            maxPenetration = 0f;
            walkTalkDone = false;
            sitTalkDone = false;
            talkClips = "";
            maxSink = 0f;
            worstSink = "none";
            maxWall = 0f;
            maxHover = 0f;
            worstHover = "none";
            worstWall = "none";
            propHits = 0;
            worstProp = "none";
            supportColliders = null;
            solidColliders = null;
            hitsByPart.Clear();
            worstPenetration = "none";
            sleepOutside = 0f;
            seatError = float.NaN;
            notes.Clear();
            captured.Clear();
            walkLeanSum = 0f;
            walkLeanSamples = 0;
            worstFacing = -1f;
            sleepOffCentre = 0f;
            previousMaximumDeltaTime = Time.maximumDeltaTime;
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
        double now = GymChaosVerifierClock.Now;
        try
        {
            if (now - startedAt > 420d)
            {
                throw new TimeoutException($"stage {stage} did not finish");
            }
            GymTimeOfDay time = GymTimeOfDay.Instance;
            GymHomelessAgent agent = GymHomelessAgent.Instance;
            switch (stage)
            {
                case Stage.WaitReady:
                    // Night until he is placed, so he starts asleep.
                    if (time != null) time.SetTimeForVerification(0.95f);
                    if (agent == null || !agent.IsReady) return;
                    Time.timeScale = TimeScale;
                    Time.maximumDeltaTime = 0.1f;
                    CheckStaticLayout();
                    MovePlayerIntoPark();
                    agent.ResetHipsTracking();
                    Next(Stage.Sleeping, now);
                    break;
                case Stage.Sleeping:
                    time.SetTimeForVerification(0.95f);
                    if (now - stageStarted < 1.5d) return;
                    Require(!agent.CanTalk, "talkable while asleep in the tent");
                    Require(agent.EyeBar != null && agent.EyeBar.GetComponent<Renderer>() != null &&
                        agent.EyeBar.GetComponent<Renderer>().enabled, "no black bar over his eyes");
                    Require(agent.State == GymHomelessAgent.HomelessState.Sleeping,
                        $"night state {agent.State}");
                    PlayerMovement walker = UnityEngine.Object.FindAnyObjectByType<PlayerMovement>();
                    float standY = GymParkAccess.FloorY + GymOutdoorFoliage.GroundTopOffset;
                    Require(walker != null && walker.transform.position.y > standY - 0.3f &&
                        GymParkAccess.Contains(walker.transform.position),
                        $"player did not stand in the park ({walker?.transform.position})");
                    notes.Add($"playerInPark={walker.transform.position:F2}");
                    Capture("01_sleeping_in_tent", agent, true);
                    {
                        Renderer[] roof = Find(GymParkAccess.Tent.transform, "Tent_Canvas")
                            .GetComponentsInChildren<Renderer>();
                        foreach (Renderer r in roof) r.enabled = false;
                        CaptureView("01c_sleeping_top", GymParkAccess.TentCenter + new Vector3(0f, 5.5f, -0.01f),
                            GymParkAccess.TentCenter);
                        foreach (Renderer r in roof) r.enabled = true;
                    }
                    CheckInsideTent(agent, out sleepOutside);
                    Require(sleepOutside <= PenetrationLimit,
                        $"sleeping body pokes {sleepOutside:F3} m through the canvas/out of the tent");
                    CheckOnBag(agent);
                    time.SetTimeForVerification(GymHomelessAgent.WakeTime01 - 0.004f);
                    Next(Stage.WaitStandUp, now);
                    break;
                case Stage.WaitStandUp:
                    if (agent.State == GymHomelessAgent.HomelessState.StandingUp)
                    {
                        wakeTime01 = time.Time01;
                        Next(Stage.StandingUp, now);
                    }
                    else if (now - stageStarted > 20d) throw new TimeoutException("never woke up");
                    break;
                case Stage.StandingUp:
                    TrackPenetration(agent, "standUp");
                    TrackClearance(agent, "standUp");
                    CaptureTentSequence(agent, "02s_up");
                    TrackFacing(agent);
                    if (agent.CurrentClipName == "stand_up" && agent.CurrentClipNormalized > 0.45f)
                        Capture("02_crawling_out", agent, false);
                    if (agent.State == GymHomelessAgent.HomelessState.WalkingToBench)
                    {
                        Capture("03_out_of_tent", agent, false);
                        Next(Stage.WaitSitting, now);
                    }
                    else if (now - stageStarted > 30d) throw new TimeoutException("stand_up never ended");
                    break;
                case Stage.WaitSitting:
                    if (!walkTalkDone && walkLeanSamples > 15 &&
                        agent.State == GymHomelessAgent.HomelessState.WalkingToBench &&
                        agent.CurrentClipName == "walking" && agent.TargetWeight > 0.99f)
                    {
                        StartTalk(agent);
                        Next(Stage.TalkWalk, now);
                        return;
                    }
                    if (agent.State == GymHomelessAgent.HomelessState.WalkingToBench &&
                        agent.CurrentClipName == "walking" && agent.TargetWeight > 0.99f)
                    {
                        // His own walk is stooped: the head leads the hips.
                        Vector3 head = agent.transform.InverseTransformPoint(agent.HeadBone.position);
                        Vector3 hipsLocal = agent.transform.InverseTransformPoint(agent.HipsBone.position);
                        walkLeanSum += head.z - hipsLocal.z;
                        walkLeanSamples++;
                    }
                    if (agent.State == GymHomelessAgent.HomelessState.SittingDown)
                        Capture("04_sitting_down", agent, false);
                    if (agent.State == GymHomelessAgent.HomelessState.Sitting)
                    {
                        Next(Stage.Sitting, now);
                    }
                    else if (now - stageStarted > 80d) throw new TimeoutException($"never sat (state {agent.State})");
                    break;
                case Stage.Sitting:
                    if (now - stageStarted < 1.2d) return;
                    Capture("05_sitting_on_bench", agent, false);
                    {
                        // His left leg and hip up close (the old thumb bulge).
                        Vector3 hipsAt = agent.HipsBone.position;
                        Vector3 left = -agent.transform.right;
                        CaptureView("05b_sitting_left_leg",
                            hipsAt + left * 1.3f + agent.transform.forward * 0.9f + Vector3.up * 0.15f,
                            hipsAt + left * 0.15f + agent.transform.forward * 0.25f + Vector3.down * 0.15f);
                    }
                    CaptureWalls();
                    seatError = MeasureSeatError(agent);
                    Require(Mathf.Abs(seatError) <= SeatTolerance,
                        $"seat contact error {seatError:F3} m");
                    Capture("05_sitting_on_bench", agent, false);
                    if (!sitTalkDone)
                    {
                        StartTalk(agent);
                        // Past his leaving time: he must still stay seated.
                        time.SetTimeForVerification(agent.LeaveBench01 + 0.01f);
                        Next(Stage.TalkSit, now);
                        return;
                    }
                    time.SetTimeForVerification(agent.LeaveBench01 - 0.004f);
                    Next(Stage.WaitStandFromBench, now);
                    break;
                case Stage.TalkWalk:
                    Require(agent.IsTalking && GymDialogueDirector.IsDialogueActive, "walking talk ended early");
                    Require(agent.CurrentClipName.StartsWith("idle", StringComparison.Ordinal),
                        $"mid-walk talk plays {agent.CurrentClipName}, not an idle");
                    Require(agent.State == talkStartState, $"cycle moved on during talk ({agent.State})");
                    Require(Vector3.Distance(agent.transform.position, talkStartPosition) < 0.08f,
                        "he kept walking during the talk");
                    if (now - stageStarted < 2.5d) return;
                    talkClips += $"walk:{agent.CurrentClipName}/{agent.TargetWeight:F2} ";
                    Capture("11_talk_mid_walk", agent, false);
                    EndTalk(agent);
                    Require(!agent.IsTalking, "talk did not end");
                    Require(agent.CurrentClipName == "walking", $"did not walk on after the talk ({agent.CurrentClipName})");
                    walkTalkDone = true;
                    Next(Stage.WaitSitting, now);
                    break;
                case Stage.TalkSit:
                    Require(agent.IsTalking && GymDialogueDirector.IsDialogueActive, "bench talk ended early");
                    Require(agent.State == GymHomelessAgent.HomelessState.Sitting,
                        $"left the bench during the talk ({agent.State})");
                    Require(agent.CurrentClipName == "sitting", $"bench talk plays {agent.CurrentClipName}");
                    if (now - stageStarted < 2.5d) return;
                    talkClips += $"sit:{agent.CurrentClipName} ";
                    Capture("12_talk_on_bench", agent, false);
                    {
                        Vector3 bar = agent.EyeBar.transform.position;
                        Vector3 face = Vector3.ProjectOnPlane(agent.EyeBar.transform.forward, Vector3.up).normalized;
                        CaptureView("13_eye_bar", bar + face * 1.1f + Vector3.up * 0.1f, bar);
                    }
                    EndTalk(agent);
                    Require(!agent.IsTalking, "bench talk did not end");
                    sitTalkDone = true;
                    // His leaving time has passed: he gets up now.
                    Next(Stage.WaitStandFromBench, now);
                    break;
                case Stage.WaitStandFromBench:
                    if (agent.State == GymHomelessAgent.HomelessState.StandingFromBench ||
                        agent.State == GymHomelessAgent.HomelessState.WalkingToTent)
                    {
                        Next(Stage.WalkingHome, now);
                    }
                    else if (now - stageStarted > 20d) throw new TimeoutException("never left the bench");
                    break;
                case Stage.WalkingHome:
                    if (agent.State == GymHomelessAgent.HomelessState.LyingDown)
                    {
                        Next(Stage.LyingDown, now);
                    }
                    else if (now - stageStarted > 80d) throw new TimeoutException($"never got home (state {agent.State})");
                    break;
                case Stage.LyingDown:
                    TrackPenetration(agent, "lieDown");
                    TrackClearance(agent, "lieDown");
                    CaptureTentSequence(agent, "06s_down");
                    TrackFacing(agent);
                    if (agent.CurrentClipName == "stand_up" && agent.CurrentClipNormalized < 0.5f)
                        Capture("06_crawling_in", agent, false);
                    if (agent.State == GymHomelessAgent.HomelessState.Sleeping)
                    {
                        bedTime01 = time.Time01;
                        Finish(agent, now);
                    }
                    else if (now - stageStarted > 30d) throw new TimeoutException("never lay down");
                    break;
            }
        }
        catch (Exception exception)
        {
            Debug.LogError("GYMCHAOS_HOMELESS_FAIL " + exception.Message +
                $" stage={stage} notes={string.Join(" ", notes)}");
            End(1);
        }
    }

    private static void StartTalk(GymHomelessAgent agent)
    {
        PlayerMovement player = UnityEngine.Object.FindAnyObjectByType<PlayerMovement>();
        Require(player != null, "no player");
        Vector3 spot = agent.transform.position + agent.transform.forward * 1.8f;
        spot.y = player.transform.position.y;
        CharacterController controller = player.GetComponent<CharacterController>();
        if (controller != null) controller.enabled = false;
        player.transform.position = spot;
        if (controller != null) controller.enabled = true;
        Physics.SyncTransforms();
        player.LookAtForVerification(agent.HeadBone.position);
        talkStartPosition = agent.transform.position;
        talkStartState = agent.State;
        Require(GymDialogueDirector.TryStartNearby(player, true), $"could not start talking to him ({agent.State})");
        Require(GymDialogueDirector.Active.HomelessTarget == agent && agent.IsTalking, "talk did not open");
    }

    private static void EndTalk(GymHomelessAgent agent)
    {
        PlayerMovement player = UnityEngine.Object.FindAnyObjectByType<PlayerMovement>();
        GymDialogueDirector.TickActiveInput(player, false, true);
    }

    private static void Next(Stage next, double now)
    {
        stage = next;
        stageStarted = now;
    }

    private static void Finish(GymHomelessAgent agent, double now)
    {
        string[] expected =
        {
            "Sleeping", "StandingUp", "WalkingToBench", "TurningAtBench", "SittingDown",
            "Sitting", "StandingFromBench", "WalkingToTent", "TurningAtTent", "LyingDown", "Sleeping"
        };
        int match = 0;
        for (int i = 0; i < agent.StateHistory.Count && match < expected.Length; i++)
        {
            if (agent.StateHistory[i] == expected[match]) match++;
        }
        string history = string.Join(">", agent.StateHistory);
        Require(match == expected.Length, $"cycle order {history}");
        Require(walkTalkDone && sitTalkDone, $"talk checks missing walk={walkTalkDone} sit={sitTalkDone}");
        notes.Add($"talk={talkClips}");
        foreach (KeyValuePair<string, int> pair in hitsByPart) notes.Add($"[{pair.Key}={pair.Value}]");
        notes.Add($"bedRoot={agent.BedRoot - GymParkAccess.TentCenter:F2} doorRoot={agent.DoorRoot - GymParkAccess.TentCenter:F2}");
        notes.Add($"sink={maxSink:F3}@{worstSink} wall={maxWall:F3}@{worstWall} propHits={propHits}@{worstProp} " +
            $"hover={maxHover:F3}@{worstHover}");
        Require(maxPenetration <= PenetrationLimit,
            $"tent canvas penetration {maxPenetration:F3} m at {worstPenetration}");
        // Lying down and getting up he looks into the tent (no turn on the bag).
        Require(worstFacing < -0.97f, $"not facing the tent while lying down/getting up ({worstFacing:F2})");
        // Crawling in and out (outside the in-bag pose) he never passes
        // through the bedding, props, poles or canvas walls, and rests on
        // what is under him instead of hovering over it.
        Require(maxSink <= ClearanceLimit, $"body sinks {maxSink:F3} m into the tent contents at {worstSink}");
        Require(propHits == 0, $"body passes through tent props/poles ({propHits} samples, last {worstProp})");
        Require(maxWall <= ClearanceLimit, $"body pokes {maxWall:F3} m through the tent wall at {worstWall}");
        Require(maxHover <= HoverLimit, $"body hovers {maxHover:F3} m over the tent floor at {worstHover}");
        Require(agent.MaxHipsStep <= 0.2f, $"pose pop: hips jumped {agent.MaxHipsStep:F3} m in one frame");
        Require(bedTime01 > 0.68f && bedTime01 < 0.95f, $"lay down at {bedTime01:F3} (not dusk)");
        float walkLean = walkLeanSamples > 0 ? walkLeanSum / walkLeanSamples : 0f;
        Require(walkLeanSamples > 10 && walkLean > 0.06f, $"walk is not his stooped walk (lean {walkLean:F3} m)");
        notes.Add($"walkLean={walkLean:F3} walkPace={GymHomelessAgent.WalkPace:F2} walkSpeed={agent.WalkSpeed * GymHomelessAgent.WalkPace:F2}");
        Debug.Log(
            "GYMCHAOS_HOMELESS_OK " +
            $"cycle={history} penetration={maxPenetration:F3}@{worstPenetration} " +
            $"sleepOutside={sleepOutside:F3} facing={worstFacing:F2} headBack={sleepHeadBack:F2} offCentre={sleepOffCentre:F2} " +
            $"bagGap={sleepBagGap:F3} seatError={seatError:F3} hipsStep={agent.MaxHipsStep:F3} " +
            $"wake01={wakeTime01:F3} bed01={bedTime01:F3} leave01={agent.LeaveBench01:F3} " +
            $"bench={agent.BenchPosition} {string.Join(" ", notes)} seconds={now - startedAt:F0}");
        End(0);
    }

    private static void End(int code)
    {
        finished = true;
        resultCode = code;
        Time.timeScale = 1f;
        Time.maximumDeltaTime = previousMaximumDeltaTime > 0f ? previousMaximumDeltaTime : 0.3333f;
        GymChaosVerifierClock.EndFixedStep();
        GymChaosVerifierExit.Record(resultCode);
        SessionState.EraseBool(RequestedKey);
        EditorApplication.isPlaying = false;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    // ------------------------------------------------------------ layout

    private static void CheckStaticLayout()
    {
        GameObject exterior = GameObject.Find("Gym Exterior (Runtime)");
        Require(exterior != null, "no exterior");
        Require(Find(exterior.transform, "Outdoor Boundary - Path South") == null &&
            Find(exterior.transform, "Path South Boundary Wall") == null, "path south wall still built");
        Require(Find(exterior.transform, GymParkAccess.SouthWallName + " Collision") != null &&
            Find(exterior.transform, GymParkAccess.EastWallName + " Collision") != null &&
            Find(exterior.transform, GymParkAccess.SouthWallName + " Low Wall") != null &&
            Find(exterior.transform, GymParkAccess.EastWallName + " Low Wall") != null,
            "park walls missing");

        float floorY = GymParkAccess.FloorY;
        Vector3 entrance = GymParkAccess.EntrancePoint;
        // Opening between the gym and the store entry wall: nothing solid.
        Collider[] inOpening = Physics.OverlapBox(
            new Vector3(entrance.x, floorY + 1.0f, entrance.z + 1.2f),
            new Vector3(GymParkAccess.GateWidth * 0.5f - 0.3f, 0.6f, 0.45f), Quaternion.identity, ~0,
            QueryTriggerInteraction.Ignore);
        int solid = 0;
        foreach (Collider c in inOpening)
        {
            if (c.GetComponentInParent<PlayerMovement>() == null &&
                c.GetComponentInParent<EnemyFighter>() == null) solid++;
        }
        Require(solid == 0, $"opening blocked by {solid} colliders");
        // The rest of the old path-end line is walled again, either side of
        // the gate, and the park's gravel path starts in the gate.
        Require(Find(exterior.transform, GymParkAccess.GateWestName + " Collision") != null &&
            Find(exterior.transform, GymParkAccess.GateEastName + " Collision") != null, "gate walls missing");
        Require(GymOutdoorFoliage.HasPathEntrance &&
            GymOutdoorFoliage.IsOnPath(entrance + Vector3.forward * 0.3f) &&
            GymOutdoorFoliage.IsOnPath(entrance + Vector3.back * 1.5f), "park path does not start in the gate");
        // Gravel fills the gate from wall to wall: no lawn strip between the
        // paved path and the park path.
        float gateHalf = GymParkAccess.GateWidth * 0.5f - 0.05f;
        for (float x = -gateHalf; x <= gateHalf + 0.01f; x += 0.25f)
        {
            Vector3 probe = entrance + new Vector3(x, 0f, 0.3f);
            Require(GymOutdoorFoliage.IsOnPath(probe), $"lawn shows in the gate at {probe}");
        }
        // The path runs straight on from the gate (no early bend along the
        // fence): its centre line stays on gravel for the first 14 m.
        for (float t = 0.5f; t <= 14f; t += 0.5f)
        {
            Vector3 probe = entrance + Vector3.back * t;
            Require(GymOutdoorFoliage.IsOnPath(probe), $"park path bends away from the gate line at {probe}");
        }
        // ...and that path is joined to the rest of the park's paths.
        int pathCells = 0;
        {
            HashSet<Vector2Int> seenPath = new HashSet<Vector2Int>();
            Queue<Vector2Int> open = new Queue<Vector2Int>();
            Vector2Int first = new Vector2Int(Mathf.RoundToInt(entrance.x / 0.5f), Mathf.RoundToInt((entrance.z - 1.5f) / 0.5f));
            open.Enqueue(first);
            seenPath.Add(first);
            while (open.Count > 0 && pathCells < 5000)
            {
                Vector2Int c = open.Dequeue();
                pathCells++;
                foreach (Vector2Int d in new[] { Vector2Int.left, Vector2Int.right, Vector2Int.up, Vector2Int.down })
                {
                    Vector2Int n = c + d;
                    if (seenPath.Contains(n)) continue;
                    Vector3 q = new Vector3(n.x * 0.5f, 0f, n.y * 0.5f);
                    if (!GymOutdoorFoliage.IsOnPath(q) || !GymParkAccess.Contains(q, -1f)) continue;
                    seenPath.Add(n);
                    open.Enqueue(n);
                }
            }
        }
        Require(pathCells > 600, $"gate path is cut off from the park paths ({pathCells} cells)");
        notes.Add($"gatePathCells={pathCells}");

        // Flood fill on foot from the opening; the open park must be closed.
        float cell = 0.5f;
        Bounds area = GymParkAccess.Area;
        Bounds frame = area;
        frame.Expand(new Vector3(8f, 0f, 8f));
        int nx = Mathf.CeilToInt(frame.size.x / cell);
        int nz = Mathf.CeilToInt(frame.size.z / cell);
        bool[] seen = new bool[nx * nz];
        Queue<int> queue = new Queue<int>();
        int Index(Vector3 p) =>
            Mathf.FloorToInt((p.z - frame.min.z) / cell) * nx + Mathf.FloorToInt((p.x - frame.min.x) / cell);
        Vector3 Center(int i) => new Vector3(
            frame.min.x + (i % nx + 0.5f) * cell, floorY, frame.min.z + (i / nx + 0.5f) * cell);
        int start = Index(entrance);
        seen[start] = true;
        queue.Enqueue(start);
        int reached = 0;
        bool escaped = false;
        Vector3 escape = Vector3.zero;
        float pathEndZ = entrance.z + 1.2f;
        while (queue.Count > 0)
        {
            int current = queue.Dequeue();
            reached++;
            int cx = current % nx;
            int cz = current / nx;
            Vector3 p = Center(current);
            if (cx == 0 || cz == 0 || cx == nx - 1 || cz == nz - 1)
            {
                escaped = true;
                escape = p;
                break;
            }
            int[] neighbours = { current - 1, current + 1, current - nx, current + nx };
            foreach (int n in neighbours)
            {
                if (seen[n]) continue;
                Vector3 q = Center(n);
                // The way back north up the gym path is the entrance.
                if (q.z >= pathEndZ) continue;
                seen[n] = true;
                bool blocked = Physics.CheckCapsule(
                    q + Vector3.up * 0.55f, q + Vector3.up * 1.6f, 0.22f, ~0, QueryTriggerInteraction.Ignore);
                bool ground = Physics.Raycast(q + Vector3.up * 0.4f, Vector3.down, 1.2f, ~0,
                    QueryTriggerInteraction.Ignore);
                if (!blocked && ground) queue.Enqueue(n);
            }
        }
        Require(!escaped, $"walked out of the park at {escape}");
        Require(reached > 1500, $"park too small or blocked: {reached} cells");
        notes.Add($"parkCells={reached}");

        // Tent: on grass, on the ground, solid, textured.
        GameObject tent = GymParkAccess.Tent;
        Require(tent != null && GymParkAccess.TentReady, "tent missing");
        Renderer[] renderers = tent.GetComponentsInChildren<Renderer>(true);
        Bounds all = renderers[0].bounds;
        int textured = 0;
        foreach (Renderer r in renderers)
        {
            all.Encapsulate(r.bounds);
            foreach (Material m in r.sharedMaterials)
            {
                if (m == null) continue;
                Texture t = m.HasProperty("_BaseMap") ? m.GetTexture("_BaseMap") : m.mainTexture;
                if (t != null) { textured++; break; }
            }
        }
        Require(textured >= 8, $"tent textured renderers {textured}/{renderers.Length}");
        Require(tent.GetComponentsInChildren<MeshCollider>(true).Length >= 1, "tent has no colliders");
        int onPath = 0;
        for (float x = all.min.x; x <= all.max.x; x += 0.25f)
            for (float z = all.min.z; z <= all.max.z; z += 0.25f)
                if (GymOutdoorFoliage.IsOnPath(new Vector3(x, 0f, z))) onPath++;
        Require(onPath == 0, $"tent footprint covers {onPath} path cells");
        // Park benches are seats, not bench-press stations.
        int outdoorStations = 0;
        foreach (GymExerciseStation station in UnityEngine.Object.FindObjectsByType<GymExerciseStation>(FindObjectsSortMode.None))
        {
            if (station.transform.IsChildOf(exterior.transform)) outdoorStations++;
        }
        Require(outdoorStations == 0, $"{outdoorStations} exercise stations on outdoor furniture");
        Vector3 tentFromGate = GymParkAccess.TentCenter - entrance;
        tentFromGate.y = 0f;
        Require(tentFromGate.magnitude >= 12f, $"tent only {tentFromGate.magnitude:F1} m from the park gate");
        notes.Add($"tentFromGate={tentFromGate.magnitude:F1}");
        Require(GymParkAccess.Contains(all.center, 1f), "tent outside the open park");
        float groundTop = floorY + GymOutdoorFoliage.GroundTopOffset;
        Transform floor = Find(tent.transform, "Tent_Floor");
        float floorGap = floor.GetComponentInChildren<Renderer>().bounds.min.y - groundTop;
        Require(Mathf.Abs(floorGap) < 0.04f, $"tent floor gap {floorGap:F3}");
        // The canvas collider is real: its roof stands over the tent centre.
        float roof = CanvasHeight(CanvasColliders(), GymParkAccess.TentCenter + Vector3.up * 0.2f);
        float behind = CanvasHeight(CanvasColliders(),
            GymParkAccess.TentCenter - GymParkAccess.TentDoorDirection * 0.8f + Vector3.up * 0.2f);
        notes.Add($"roofCentre={roof - groundTop:F2} roofBack={behind - groundTop:F2}");
        Require(!float.IsNaN(behind) && behind - groundTop > 1.2f, $"canvas roof not hit ({behind})");
        // A capsule standing inside the tent walls is blocked by the canvas.
        Vector3 side = Vector3.Cross(Vector3.up, GymParkAccess.TentDoorDirection);
        Vector3 wallProbe = GymParkAccess.TentCenter + side * (GymParkAccess.TentDoorDepth + 0.05f);
        notes.Add($"tentSize={all.size.x:F1}x{all.size.z:F1} textured={textured}/{renderers.Length} " +
            $"wallSolid={Physics.Raycast(wallProbe + side * 1.5f + Vector3.up * 1.0f, -side, 3f)}");
        Capture("00_park_overview", null, false);
    }

    // ------------------------------------------------------------ body checks

    private static MeshCollider[] CanvasColliders()
    {
        Transform canvas = Find(GymParkAccess.Tent.transform, "Tent_Canvas");
        return canvas.GetComponentsInChildren<MeshCollider>(true);
    }

    // Height of the canvas over a point, or NaN outside its footprint.
    private static float CanvasHeight(MeshCollider[] canvas, Vector3 point)
    {
        float best = float.NaN;
        Ray ray = new Ray(new Vector3(point.x, point.y + 6f, point.z), Vector3.down);
        bool backfaces = Physics.queriesHitBackfaces;
        Physics.queriesHitBackfaces = true;
        // Collider.Raycast ignores back faces; the canvas may face inwards.
        RaycastHit[] hits = Physics.RaycastAll(ray, 12f, ~0, QueryTriggerInteraction.Ignore);
        foreach (RaycastHit hit in hits)
        {
            if (Array.IndexOf(canvas, hit.collider as MeshCollider) < 0) continue;
            if (float.IsNaN(best) || hit.point.y > best) best = hit.point.y;
        }
        Physics.queriesHitBackfaces = backfaces;
        return best;
    }

    private static void TrackPenetration(GymHomelessAgent agent, string phase)
    {
        if (Time.frameCount % 2 != 0) return;
        MeshCollider[] canvas = CanvasColliders();
        Vector3[] vertices = agent.BakeWorldVertices();
        for (int i = 0; i < vertices.Length; i += 23)
        {
            float h = CanvasHeight(canvas, vertices[i]);
            if (float.IsNaN(h)) continue;
            // Under the roof is fine; above the canvas surface the body
            // pokes through it.
            float depth = vertices[i].y - h;
            if (depth > maxPenetration)
            {
                maxPenetration = depth;
                float along = Vector3.Dot(vertices[i] - GymParkAccess.TentCenter, GymParkAccess.TentDoorDirection);
                worstPenetration = $"{phase}:{agent.CurrentClipNormalized:F2} along={along:F2} y={vertices[i].y - GymParkAccess.FloorY:F2}";
            }
        }
    }

    private static Collider[] PartColliders(params string[] parts)
    {
        List<Collider> colliders = new List<Collider>();
        foreach (string part in parts)
        {
            Transform node = Find(GymParkAccess.Tent.transform, part);
            if (node == null) continue;
            foreach (MeshFilter filter in node.GetComponentsInChildren<MeshFilter>(true))
            {
                MeshCollider collider = filter.GetComponent<MeshCollider>();
                if (collider == null)
                {
                    collider = filter.gameObject.AddComponent<MeshCollider>();
                    collider.sharedMesh = filter.sharedMesh;
                }
                colliders.Add(collider);
            }
        }
        Physics.SyncTransforms();
        return colliders.ToArray();
    }

    private static void TrackClearance(GymHomelessAgent agent, string phase)
    {
        if (Time.frameCount % 2 != 0) return;
        if (supportColliders == null)
        {
            supportColliders = PartColliders("Tent_Bedding", "Tent_Floor", "Tent_Props");
            // Props are in the support columns (sink); poles stand free.
            solidColliders = PartColliders("Tent_Poles");
        }
        MeshCollider[] canvas = CanvasColliders();
        bool backfaces = Physics.queriesHitBackfaces;
        Physics.queriesHitBackfaces = true;
        Vector3 door = GymParkAccess.TentDoorDirection;
        Vector3 centre = GymParkAccess.TentCenter;
        float floorY = GymParkAccess.FloorY;
        string at = $"{phase}:{agent.CurrentClipNormalized:F2}";
        // Asleep (and while easing into/out of that pose) he lies in the
        // bag under the blanket; the crawl itself must rest on top.
        bool inBag = agent.SleepWeight > 0.02f;
        Vector3[] vertices = agent.BakeWorldVertices();
        float groundTop = GymParkAccess.FloorY + GymOutdoorFoliage.GroundTopOffset;
        float lowestGap = float.PositiveInfinity;
        float runtimeGap = float.PositiveInfinity;
        string lowestAt = "";
        string runtimeAt = "";
        for (int i = 0; i < vertices.Length; i += 17)
        {
            Vector3 v = vertices[i];
            // Sinking: the highest support surface in this column is above
            // the body point.
            RaycastHit[] down = Physics.RaycastAll(v + Vector3.up * 1.2f, Vector3.down, 1.6f);
            float top = float.NegativeInfinity;
            foreach (RaycastHit hit in down)
            {
                if (Array.IndexOf(supportColliders, hit.collider) < 0) continue;
                // Bedding is soft: his weight may press it down by
                // GymHomelessAgent.BeddingSquash of its height, no further.
                float surface = hit.collider.name.StartsWith("Tent_Bedding", StringComparison.Ordinal)
                    ? hit.point.y - Mathf.Max(0f, hit.point.y - groundTop) * GymHomelessAgent.BeddingSquash
                    : hit.point.y;
                if (!inBag && surface > v.y + 0.03f)
                {
                    string key = "sink:" + hit.collider.name;
                    hitsByPart[key] = hitsByPart.TryGetValue(key, out int c) ? c + 1 : 1;
                }
                top = Mathf.Max(top, surface);
            }
            float sink = top - v.y;
            float gapHere = v.y - Mathf.Max(top, groundTop);
            if (gapHere < lowestGap)
            {
                lowestGap = gapHere;
                lowestAt = $"v={v - centre:F2} top={Mathf.Max(top, groundTop) - groundTop:F2} field={agent.SupportHeightAt(v) - groundTop:F2} root={agent.transform.position.y - groundTop:F2}";
            }
            if (v.y - agent.SupportHeightAt(v) < runtimeGap)
            {
                runtimeGap = v.y - agent.SupportHeightAt(v);
                runtimeAt = $"rv={v - centre:F2} rtop={Mathf.Max(top, groundTop) - groundTop:F2} rfield={agent.SupportHeightAt(v) - groundTop:F2}";
            }
            if (!inBag && sink > maxSink)
            {
                maxSink = sink;
                worstSink = $"{at} y={v.y - floorY:F2} surface={top - floorY:F2} along={Vector3.Dot(v - centre, door):F2}";
            }
            // Walls: a ray from the tent's centre line at the same height
            // must reach the body point without crossing the canvas.
            float along = Mathf.Clamp(Vector3.Dot(v - centre, door), -1.0f, GymParkAccess.TentDoorDepth);
            Vector3 anchor = new Vector3(centre.x, Mathf.Clamp(v.y, floorY + 0.15f, floorY + 0.9f), centre.z) + door * along;
            Vector3 toV = v - anchor;
            float length = toV.magnitude;
            if (length > 0.02f)
            {
                foreach (RaycastHit hit in Physics.RaycastAll(anchor, toV / length, length))
                {
                    if (Array.IndexOf(canvas, hit.collider as MeshCollider) < 0) continue;
                    float wall = length - hit.distance;
                    if (wall > maxWall)
                    {
                        maxWall = wall;
                        worstWall = $"{at} y={v.y - floorY:F2} along={Vector3.Dot(v - centre, door):F2} side={Vector3.Dot(v - centre, Vector3.Cross(Vector3.up, door)):F2}";
                    }
                }
            }
            // Poles and props (lamp, bottles, stove...).
            foreach (Collider hit in Physics.OverlapSphere(v, 0.012f))
            {
                if (Array.IndexOf(solidColliders, hit) < 0) continue;
                propHits++;
                string key = "in:" + hit.name;
                hitsByPart[key] = hitsByPart.TryGetValue(key, out int c) ? c + 1 : 1;
                worstProp = $"{at} {hit.name} y={v.y - floorY:F2}";
            }
        }
        Physics.queriesHitBackfaces = backfaces;
        // Resting, not hovering: the lowest point touches what is under it.
        if (!inBag && lowestGap > maxHover)
        {
            maxHover = lowestGap;
            worstHover = $"{at} sleepW={agent.SleepWeight:F2} runtimeGap={runtimeGap:F3} {runtimeAt} {lowestAt}";
        }
    }

    // Side cutaway of the tent (canvas hidden) at fixed stages of the
    // crawl, so the clearance numbers can be checked by eye.
    private static void CaptureTentSequence(GymHomelessAgent agent, string prefix)
    {
        if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) return;
        float n = agent.CurrentClipNormalized;
        int bucket = Mathf.FloorToInt(n * 8f);
        if (bucket < 0 || bucket > 7) return;
        string name = $"{prefix}_{bucket}";
        if (captured.Contains(name)) return;
        Renderer[] canvasRenderers = Find(GymParkAccess.Tent.transform, "Tent_Canvas").GetComponentsInChildren<Renderer>();
        foreach (Renderer r in canvasRenderers) r.enabled = false;
        Vector3 door = GymParkAccess.TentDoorDirection;
        Vector3 side = Vector3.Cross(Vector3.up, door);
        Vector3 focus = GymParkAccess.TentCenter + door * 1.0f + Vector3.up * 0.35f;
        CaptureView(name, focus + side * 3.4f + Vector3.up * 0.5f, focus);
        foreach (Renderer r in canvasRenderers) r.enabled = true;
    }

    // Forward of the body against the door direction: -1 = facing the tent.
    private static void TrackFacing(GymHomelessAgent agent)
    {
        float dot = Vector3.Dot(agent.transform.forward, GymParkAccess.TentDoorDirection);
        worstFacing = Mathf.Max(worstFacing, dot);
    }

    // Asleep with the head on the pillow end (back wall) and lying on the bag.
    private static void CheckOnBag(GymHomelessAgent agent)
    {
        Bounds bed = GymParkAccess.TentBagBounds;
        Vector3 door = GymParkAccess.TentDoorDirection;
        sleepHeadBack = -Vector3.Dot(agent.HeadBone.position - bed.center, door);
        Vector3 hips = agent.HipsBone.position;
        bool hipsOnBag = hips.x >= bed.min.x && hips.x <= bed.max.x && hips.z >= bed.min.z && hips.z <= bed.max.z;
        Vector3[] vertices = agent.BakeWorldVertices();
        float lowest = float.MaxValue;
        Bounds body = new Bounds(vertices[0], Vector3.zero);
        foreach (Vector3 v in vertices)
        {
            lowest = Mathf.Min(lowest, v.y);
            body.Encapsulate(v);
        }
        // Lying evenly over the bag, not along one edge of it.
        Vector3 offset = body.center - bed.center;
        offset.y = 0f;
        sleepOffCentre = offset.magnitude;
        Require(sleepOffCentre <= 0.2f, $"sleeping body {sleepOffCentre:F2} m off the bag centre");
        sleepBagGap = lowest - (GymParkAccess.TentBedBounds.min.y + 0.08f);
        Require(sleepHeadBack > 0.25f, $"sleeping head not at the pillow end ({sleepHeadBack:F2} m behind the bag centre)");
        Require(hipsOnBag, $"sleeping hips off the bag {hips}");
        Require(Mathf.Abs(sleepBagGap) <= 0.05f, $"sleeping body not resting on the bag (gap {sleepBagGap:F3})");
    }

    // Sleeping: every body point must be under the canvas (inside the tent).
    private static void CheckInsideTent(GymHomelessAgent agent, out float worst)
    {
        worst = 0f;
        MeshCollider[] canvas = CanvasColliders();
        Vector3[] vertices = agent.BakeWorldVertices();
        int outside = 0;
        int open = 0;
        Bounds floor = Find(GymParkAccess.Tent.transform, "Tent_Floor").GetComponentInChildren<Renderer>().bounds;
        for (int i = 0; i < vertices.Length; i += 11)
        {
            Vector3 v = vertices[i];
            // Over the groundsheet (the tent is square to the world axes).
            float beyond = Mathf.Max(
                Mathf.Max(floor.min.x - v.x, v.x - floor.max.x),
                Mathf.Max(floor.min.z - v.z, v.z - floor.max.z));
            if (beyond > 0f)
            {
                outside++;
                worst = Mathf.Max(worst, beyond);
            }
            float h = CanvasHeight(canvas, v);
            if (float.IsNaN(h))
            {
                // Under the door opening.
                open++;
                continue;
            }
            worst = Mathf.Max(worst, v.y - h);
        }
        notes.Add($"sleepUnderDoor={open}");
        Bounds body = new Bounds(vertices[0], Vector3.zero);
        foreach (Vector3 v in vertices) body.Encapsulate(v);
        Bounds canvasBounds = canvas[0].bounds;
        foreach (MeshCollider c in canvas) canvasBounds.Encapsulate(c.bounds);
        notes.Add($"sleepOutsideVerts={outside} body={body.min:F2}->{body.max:F2} " +
            $"canvas={canvasBounds.min:F2}->{canvasBounds.max:F2} bed={GymParkAccess.TentBedBounds.min:F2}->{GymParkAccess.TentBedBounds.max:F2} " +
            $"root={agent.transform.position:F2} fwd={agent.transform.forward:F2} hips={agent.HipsBone.position:F2} head={agent.HeadBone.position:F2}");
    }

    private static float MeasureSeatError(GymHomelessAgent agent)
    {
        Vector3 hips = agent.HipsBone.position;
        Vector3[] vertices = agent.BakeWorldVertices();
        float lowest = float.MaxValue;
        foreach (Vector3 v in vertices)
        {
            float dx = v.x - hips.x;
            float dz = v.z - hips.z;
            if (dx * dx + dz * dz < 0.16f * 0.16f && v.y < lowest) lowest = v.y;
        }
        float seatTop = agent.BenchPosition.y + GymOutdoorFoliage.BenchSeatTop;
        // Hips over the seat board.
        Vector3 local = Quaternion.Inverse(Quaternion.LookRotation(agent.BenchFacing)) *
            (hips - agent.BenchPosition);
        float halfWidth = 0.85f * GymOutdoorFoliage.BenchScale;
        float halfDepth = 0.23f * GymOutdoorFoliage.BenchScale + 0.08f;
        Require(Mathf.Abs(local.x) <= halfWidth && Mathf.Abs(local.z) <= halfDepth,
            $"hips not over the seat local={local}");
        notes.Add($"hipsOnSeat=({local.x:F2},{local.z:F2})");
        return lowest - seatTop;
    }

    // ------------------------------------------------------------ evidence

    private static void Capture(string name, GymHomelessAgent agent, bool hideCanvas)
    {
        if (captured.Contains(name) || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) return;
        captured.Add(name);
        try
        {
            Vector3 target;
            Vector3 eye;
            if (agent == null)
            {
                Bounds area = GymParkAccess.Area;
                target = area.center;
                eye = area.center + new Vector3(0f, 60f, -0.01f);
            }
            else
            {
                target = agent.HipsBone.position;
                Vector3 side = agent.transform.right;
                eye = target + side * 4.2f + agent.transform.forward * 2.2f + Vector3.up * 1.6f;
            }
            Renderer[] hidden = hideCanvas
                ? Find(GymParkAccess.Tent.transform, "Tent_Canvas").GetComponentsInChildren<Renderer>()
                : new Renderer[0];
            foreach (Renderer r in hidden) r.enabled = false;
            GameObject cameraObject = new GameObject("Homeless Evidence Camera");
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.transform.position = eye;
            camera.transform.LookAt(target);
            camera.fieldOfView = agent == null ? 40f : 50f;
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = 300f;
            RenderTexture rt = new RenderTexture(960, 640, 24);
            camera.targetTexture = rt;
            camera.Render();
            RenderTexture.active = rt;
            Texture2D image = new Texture2D(960, 640, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, 960, 640), 0, 0);
            image.Apply();
            RenderTexture.active = null;
            string folder = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "..", "Logs", "agent", "homeless");
            Directory.CreateDirectory(folder);
            File.WriteAllBytes(Path.Combine(folder, name + ".png"), image.EncodeToPNG());
            camera.targetTexture = null;
            UnityEngine.Object.Destroy(rt);
            UnityEngine.Object.Destroy(image);
            UnityEngine.Object.Destroy(cameraObject);
            foreach (Renderer r in hidden) r.enabled = true;
        }
        catch (Exception exception)
        {
            notes.Add($"capture-{name}-failed:{exception.GetType().Name}");
        }
    }

    private static void MovePlayerIntoPark()
    {
        PlayerMovement player = UnityEngine.Object.FindAnyObjectByType<PlayerMovement>();
        if (player == null) return;
        Vector3 spot = GymParkAccess.EntrancePoint + Vector3.back * 2.5f + Vector3.up * 0.1f;
        CharacterController controller = player.GetComponent<CharacterController>();
        if (controller != null) controller.enabled = false;
        Rigidbody body = player.GetComponent<Rigidbody>();
        if (body != null) { body.position = spot; body.linearVelocity = Vector3.zero; }
        player.transform.position = spot;
        if (controller != null) controller.enabled = true;
        Physics.SyncTransforms();
    }

    private static void CaptureWalls()
    {
        Bounds a = GymParkAccess.Area;
        float y = GymParkAccess.FloorY;
        Vector3 entrance = GymParkAccess.EntrancePoint;
        CaptureView("07c_gate_ground_closeup", entrance + new Vector3(0.6f, 1.7f, 4.2f), entrance + new Vector3(0f, -0.2f, -1.0f));
        CaptureView("07b_gate_from_gym_path", entrance + new Vector3(0f, 2.3f, 10f), entrance + new Vector3(0f, 0.3f, -4f));
        CaptureView("07_opening_from_park", entrance + new Vector3(0f, 2.2f, -11f), entrance + new Vector3(0f, 0.8f, 2f));
        CaptureView("08_south_west_corner", new Vector3(a.min.x + 9f, y + 2.6f, a.min.z + 8f), new Vector3(a.min.x, y + 0.6f, a.min.z));
        CaptureView("09_south_east_corner", new Vector3(a.max.x - 9f, y + 2.6f, a.min.z + 8f), new Vector3(a.max.x, y + 0.6f, a.min.z));
        CaptureView("10_east_wall_store_corner", new Vector3(a.max.x - 8f, y + 2.6f, -6f), new Vector3(a.max.x, y + 0.6f, 2.5f));
    }

    private static void CaptureView(string name, Vector3 eye, Vector3 target)
    {
        if (captured.Contains(name) || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) return;
        captured.Add(name);
        GameObject cameraObject = new GameObject("Homeless Evidence Camera");
        Camera camera = cameraObject.AddComponent<Camera>();
        camera.transform.position = eye;
        camera.transform.LookAt(target);
        camera.fieldOfView = 60f;
        camera.nearClipPlane = 0.05f;
        camera.farClipPlane = 300f;
        RenderTexture rt = new RenderTexture(960, 640, 24);
        camera.targetTexture = rt;
        camera.Render();
        RenderTexture.active = rt;
        Texture2D image = new Texture2D(960, 640, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0, 0, 960, 640), 0, 0);
        image.Apply();
        RenderTexture.active = null;
        string folder = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "..", "Logs", "agent", "homeless");
        Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder, name + ".png"), image.EncodeToPNG());
        camera.targetTexture = null;
        UnityEngine.Object.Destroy(rt);
        UnityEngine.Object.Destroy(image);
        UnityEngine.Object.Destroy(cameraObject);
    }

    private static Transform Find(Transform root, string name)
    {
        if (root == null) return null;
        if (root.name == name) return root;
        for (int i = 0; i < root.childCount; i++)
        {
            Transform found = Find(root.GetChild(i), name);
            if (found != null) return found;
        }
        return null;
    }
}
#endif
