#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Runs a member deadlift set for every gym member with an authored deadlift
/// clip and checks, every editor frame:
///  - one lifter: a second member and the player cannot claim the station,
///    and the player cannot pick up the bar;
///  - the rep runs pull -> slow lockout pause -> reverse lower, starting and
///    ending with the bar settled on the mats;
///  - the bar stays level, never lower than its settled pose, and the plates
///    never go into the platform;
///  - both grip hollows stay on the shaft, and the baked hand skin touches
///    the bar without a visible gap;
///  - the lifter's soles stand on the mats;
///  - the shaft never cuts into the legs or torso between the hands;
///  - after the set the bar is back on the mats and free for the player.
/// Needs -Graphics (skinned poses). Renders views per member to
/// Logs/verify/deadlift-member/. Logs GYMCHAOS_DEADLIFT_MEMBER_OK.
/// GYMCHAOS_DEADLIFT_MEMBER_ONLY=arnold,zyzz limits the members.
/// </summary>
public static class GymChaosDeadliftMemberVerifier
{
    private const string RequestedKey = "GymChaos.DeadliftMemberVerificationRequested";
    private const string LifecycleKey = "GymChaos.DeadliftMemberLifecycleRequested";
    private const float GripAxisLimit = 0.03f;
    private const float HandGapLimit = 0.02f;
    private const float PlatePenetrationLimit = 0.004f;
    private const float BodyPenetrationLimit = 0.004f;
    private const float SoleLimit = 0.035f;
    private const float MinimumPauseSeconds = 0.45f;

    private static readonly List<EnemyFighter> queue = new List<EnemyFighter>();
    private static EnemyFighter fighter;
    private static SquatWorkoutController controller;
    private static GymExerciseStation station;
    private static int phase;
    private static double started;
    private static double phaseStarted;
    private static double pauseStarted;
    private static double longestPause;
    private static string lastSegment;
    private static float maxRise;
    private static float worstGrip;
    private static float worstPlate;
    private static float worstTilt;
    private static float worstBody;
    private static int frames;
    private static HashSet<string> captured = new HashSet<string>();
    private static readonly StringBuilder summary = new StringBuilder();
    private static readonly List<string> failures = new List<string>();
    private static float savedMaxDelta;

    [InitializeOnLoadMethod]
    private static void ResumeAfterReload()
    {
        if (!SessionState.GetBool(RequestedKey, false)) return;
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
        EditorApplication.playModeStateChanged -= PlayModeChanged;
        EditorApplication.playModeStateChanged += PlayModeChanged;
    }

    public static void Run()
    {
        SessionState.SetBool(LifecycleKey, false);
        Start();
    }

    /// <summary>
    /// A scheduled member walks to the platform, lifts, frees the bar and
    /// walks off; meanwhile nobody else can claim the station.
    /// Marker: GYMCHAOS_DEADLIFT_MEMBER_LIFECYCLE_OK.
    /// </summary>
    public static void RunLifecycle()
    {
        SessionState.SetBool(LifecycleKey, true);
        Start();
    }

    private static void Start()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        SessionState.SetBool(RequestedKey, true);
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
        EditorApplication.playModeStateChanged -= PlayModeChanged;
        EditorApplication.playModeStateChanged += PlayModeChanged;
        EditorApplication.isPlaying = true;
    }

    private static void PlayModeChanged(PlayModeStateChange change)
    {
        if (change == PlayModeStateChange.EnteredPlayMode)
        {
            started = EditorApplication.timeSinceStartup;
            phase = 0;
            cancelSubject = null;
            queue.Clear();
            summary.Clear();
            failures.Clear();
            Time.timeScale = 1f;
            AudioListener.pause = true;
            // Editor-update sampling: keep one frame short so the pause and
            // the floor contacts are observed.
            savedMaxDelta = Time.maximumDeltaTime;
            Time.maximumDeltaTime = 0.05f;
        }
        if (change != PlayModeStateChange.EnteredEditMode) return;
        EditorApplication.update -= Tick;
        EditorApplication.playModeStateChanged -= PlayModeChanged;
        if (Application.isBatchMode && SessionState.GetBool(RequestedKey, false))
            EditorApplication.Exit(1);
    }

    private static void Tick()
    {
        if (!EditorApplication.isPlaying) return;
        AudioListener.pause = true;
        try
        {
            double elapsed = EditorApplication.timeSinceStartup - started;
            if (elapsed > 420d) throw new InvalidOperationException($"timed out phase={phase} fighter={fighter?.Identity}");
            GymVisitorDirector director = UnityEngine.Object.FindAnyObjectByType<GymVisitorDirector>();
            if (director == null || elapsed < 2d) return;
            if (SessionState.GetBool(LifecycleKey, false))
            {
                TickLifecycle(director, elapsed);
                return;
            }
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
                throw new InvalidOperationException("requires_graphics_device run_with=-Graphics");
            if (phase == 0)
            {
                director.SuspendVisitorSimulationForVerification();
                station = FindDeadliftStation();
                if (station == null) throw new InvalidOperationException("no deadlift station");
                if (!station.IsDeadliftBarAtRest) throw new InvalidOperationException("deadlift bar not at rest at start");
                BuildQueue();
                if (queue.Count == 0) throw new InvalidOperationException("no deadlift candidates");
                phase = 1;
            }
            if (phase == 1)
            {
                if (queue.Count == 0)
                {
                    if (cancelSubject == null) throw new InvalidOperationException("no cancel subject");
                    StartCancelStage();
                    return;
                }
                StartNext();
                return;
            }
            if (phase == 5)
            {
                TickCancelStage();
                return;
            }
            if (phase == 2) TickSet();
            if (phase == 3 && EditorApplication.timeSinceStartup - phaseStarted > 0.8d) phase = 1;
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            Debug.LogError("GYMCHAOS_DEADLIFT_MEMBER_FAILED " + exception.Message);
            Exit(1);
        }
    }

    private static double lifecycleRequestedAt = -1d;
    private static double lifecycleCompletedAt = -1d;
    private static bool lifecycleStarted;
    private static double lifecycleNextSample;
    private static bool lifecycleExclusiveChecked;

    private static void TickLifecycle(GymVisitorDirector director, double elapsed)
    {
        if (elapsed > 200d) throw new InvalidOperationException(
            $"lifecycle timed out requested={lifecycleRequestedAt >= 0d} started={lifecycleStarted} " +
            $"state={fighter?.GetComponent<GymVisitorAgent>()?.State}");
        if (phase == 0)
        {
            station = FindDeadliftStation();
            if (station == null) throw new InvalidOperationException("no deadlift station");
            lifecycleRequestedAt = -1d;
            lifecycleCompletedAt = -1d;
            lifecycleStarted = false;
            lifecycleExclusiveChecked = false;
            phase = 1;
        }
        if (phase == 1)
        {
            if (elapsed < 6d || !director.BeginDeadliftForVerification(out fighter, out GymExerciseStation claimed))
                return;
            if (claimed != station) throw new InvalidOperationException("member claimed another station");
            controller = fighter.GetComponent<SquatWorkoutController>();
            lifecycleRequestedAt = elapsed;
            frames = 0;
            worstGrip = 0f;
            worstPlate = 0f;
            maxRise = 0f;
            captured = new HashSet<string>();
            failures.Clear();
            Debug.Log($"GYMCHAOS_DEADLIFT_MEMBER_LIFECYCLE_REQUESTED enemy={fighter.Identity}");
            phase = 2;
            return;
        }
        GymVisitorAgent agent = fighter.GetComponent<GymVisitorAgent>();
        if (phase == 2)
        {
            if (elapsed >= lifecycleNextSample)
            {
                lifecycleNextSample = elapsed + 0.5d;
                Debug.Log($"GYMCHAOS_DEADLIFT_MEMBER_APPROACH_SAMPLE enemy={fighter.Identity} state={agent.State} " +
                    $"pos={fighter.transform.position} target={agent.TravelTargetForVerification} " +
                    $"blocker={fighter.LastVisitorRouteBlocker}");
            }
            if (controller.IsDeadliftActive)
            {
                lifecycleStarted = true;
                phase = 3;
                Debug.Log($"GYMCHAOS_DEADLIFT_MEMBER_LIFECYCLE_STARTED enemy={fighter.Identity} " +
                    $"after={elapsed - lifecycleRequestedAt:0.0}s");
            }
            else if (agent.WorkoutStationForVerification != station && !controller.IsDeadliftActive)
            {
                throw new InvalidOperationException($"approach cancelled state={agent.State}");
            }
            else if (elapsed - lifecycleRequestedAt > 60d)
            {
                throw new InvalidOperationException($"member never reached the platform state={agent.State} pos={fighter.transform.position}");
            }
            return;
        }
        if (phase == 3)
        {
            if (!lifecycleExclusiveChecked && controller.DeadliftSegment == "pull")
            {
                lifecycleExclusiveChecked = true;
                if (!station.IsOccupied || station.IsAvailableForPlayer)
                    throw new InvalidOperationException("station shown free while a member lifts");
                foreach (EnemyFighter other in UnityEngine.Object.FindObjectsByType<EnemyFighter>(FindObjectsSortMode.None))
                    if (other != fighter && station.TryReserveEnemyDeadlift(other))
                        throw new InvalidOperationException($"second member {other.Identity} claimed the platform");
                PlayerMovement player = UnityEngine.Object.FindAnyObjectByType<PlayerMovement>();
                if (player != null && station.TryReserveForPlayer(player))
                    throw new InvalidOperationException("player claimed the platform during a member set");
                if (AnyBarItemPickable())
                    throw new InvalidOperationException("bar or plate pickable during a member set");
            }
            if (controller.IsDeadliftActive)
            {
                float grip = Mathf.Max(controller.DeadliftLeftGripError, controller.DeadliftRightGripError);
                if (++frames > 3) worstGrip = Mathf.Max(worstGrip, grip);
                worstPlate = Mathf.Max(worstPlate, station.DeadliftPlatformTopY - LowestPlateY(station.DeadliftBar, out _));
                maxRise = Mathf.Max(maxRise, station.DeadliftBar.position.y - station.DeadliftBarRestPosition.y);
                if (controller.DeadliftSegment == "pause" && !captured.Contains("pause"))
                {
                    MeasureContact("pause");
                    Capture("pause");
                }
            }
            if (!controller.IsDeadliftActive)
            {
                if (worstGrip > GripAxisLimit || worstPlate > PlatePenetrationLimit || maxRise < 0.25f || failures.Count > 0)
                    throw new InvalidOperationException(
                        $"lifecycle set grip={worstGrip:0.000} plate={worstPlate:0.000} rise={maxRise:0.000} {string.Join(" ", failures)}");
                if (controller.DeadliftRepsDone < 3) throw new InvalidOperationException($"set ended after {controller.DeadliftRepsDone} reps");
                lifecycleCompletedAt = elapsed;
                phase = 4;
            }
            return;
        }
        if (phase == 4)
        {
            GymDeadliftStationMarker marker = station.DeadliftMarker;
            bool offPlatform = marker == null ||
                !marker.IsInsideNavigationClearance(fighter.transform.position, 0.05f);
            if (station.IsDeadliftBarAtRest && station.EnemyOccupant == null && offPlatform &&
                agent.State != GymVisitorAgent.VisitorState.Squatting)
            {
                PlayerMovement player = UnityEngine.Object.FindAnyObjectByType<PlayerMovement>();
                if (player != null)
                {
                    if (!station.TryReserveForPlayer(player))
                        throw new InvalidOperationException("player cannot use the platform after the set");
                    station.CancelPlayerReservation(player);
                }
                Debug.Log($"GYMCHAOS_DEADLIFT_MEMBER_LIFECYCLE_OK enemy={fighter.Identity} " +
                    $"leftAfter={elapsed - lifecycleCompletedAt:0.0}s state={agent.State}");
                Exit(0);
                return;
            }
            if (elapsed - lifecycleCompletedAt > 15d)
                throw new InvalidOperationException(
                    $"after the set barAtRest={station.IsDeadliftBarAtRest} occupant={station.EnemyOccupant} " +
                    $"offPlatform={offPlatform} state={agent.State}");
        }
    }

    private static EnemyFighter cancelSubject;

    private static bool AnyBarItemPickable()
    {
        foreach (PickupItem item in station.DeadliftBar.GetComponentsInChildren<PickupItem>(true))
            if (item.IsThrowableWeapon) return true;
        return false;
    }

    // A fight interrupts a set mid-pull: the reservation, the lifter and the
    // locks are released and the bar is back on the mats.
    private static void StartCancelStage()
    {
        fighter = cancelSubject;
        controller = fighter.GetComponent<SquatWorkoutController>();
        fighter.gameObject.SetActive(true);
        fighter.SetVisitorSpawnPose(station.EnemyPosition, station.EnemyRotation, true);
        Physics.SyncTransforms();
        if (!controller.BeginDeadlift(station, fighter, 3))
            throw new InvalidOperationException($"{fighter.Identity} cancel-stage deadlift did not start");
        phase = 5;
        phaseStarted = EditorApplication.timeSinceStartup;
    }

    private static void TickCancelStage()
    {
        if (EditorApplication.timeSinceStartup - phaseStarted > 20d)
            throw new InvalidOperationException("cancel stage never reached the pull");
        if (!controller.IsDeadliftActive || controller.DeadliftSegment != "pull" ||
            station.DeadliftBar.position.y - station.DeadliftBarRestPosition.y < 0.2f) return;
        GymVisitorAgent agent = fighter.GetComponent<GymVisitorAgent>();
        if (agent == null) throw new InvalidOperationException("cancel subject has no visitor agent");
        agent.CancelForCombat();
        string id = fighter.Identity.ToString();
        if (controller.IsDeadliftActive) failures.Add($"{id}:cancel_left_set_running");
        if (station.EnemyOccupant != null) failures.Add($"{id}:cancel_left_station_occupied");
        if (station.DeadliftMarker != null && station.DeadliftMarker.ActiveLifter != null)
            failures.Add($"{id}:cancel_left_lifter");
        if (!station.IsDeadliftBarAtRest) failures.Add($"{id}:cancel_left_bar_raised");
        foreach (PickupItem item in station.DeadliftBar.GetComponentsInChildren<PickupItem>(true))
            if (item.IsWorkoutLocked) failures.Add($"{id}:cancel_left_{item.name}_locked");
        Debug.Log($"GYMCHAOS_DEADLIFT_MEMBER_CANCEL enemy={id} released={station.EnemyOccupant == null} " +
            $"barAtRest={station.IsDeadliftBarAtRest}");
        fighter.gameObject.SetActive(false);
        Finish();
    }

    private static GymExerciseStation FindDeadliftStation()
    {
        foreach (GymExerciseStation candidate in UnityEngine.Object.FindObjectsByType<GymExerciseStation>(FindObjectsSortMode.None))
            if (candidate != null && candidate.IsDeadlift) return candidate;
        return null;
    }

    private static void BuildQueue()
    {
        HashSet<BodybuilderIdentity> seen = new HashSet<BodybuilderIdentity>();
        string only = Environment.GetEnvironmentVariable("GYMCHAOS_DEADLIFT_MEMBER_ONLY");
        foreach (EnemyFighter candidate in UnityEngine.Object.FindObjectsByType<EnemyFighter>(
                     FindObjectsInactive.Include, FindObjectsSortMode.InstanceID))
        {
            if (candidate == null || candidate.IsDead || seen.Contains(candidate.Identity)) continue;
            if (!string.IsNullOrEmpty(only) &&
                only.IndexOf(candidate.Identity.ToString(), StringComparison.OrdinalIgnoreCase) < 0) continue;
            SquatWorkoutController workout = candidate.GetComponent<SquatWorkoutController>();
            MixamoScanRetargetAnimator animator = candidate.GetComponentInChildren<MixamoScanRetargetAnimator>(true);
            if (workout == null || !workout.HasValidArmRig || animator == null ||
                !animator.HasAuthoredDeadliftClip) continue;
            seen.Add(candidate.Identity);
            queue.Add(candidate);
        }
    }

    private static void StartNext()
    {
        fighter = queue[0];
        queue.RemoveAt(0);
        controller = fighter.GetComponent<SquatWorkoutController>();
        fighter.gameObject.SetActive(true);
        fighter.SetVisitorSpawnPose(station.EnemyPosition, station.EnemyRotation, true);
        Physics.SyncTransforms();
        if (!controller.BeginDeadlift(station, fighter, 3))
            throw new InvalidOperationException($"{fighter.Identity} deadlift did not start");
        foreach (SkinnedMeshRenderer skin in fighter.GetComponentsInChildren<SkinnedMeshRenderer>())
            skin.updateWhenOffscreen = true;

        // Exclusive station: another member and the player are refused.
        foreach (EnemyFighter other in queue)
        {
            if (station.TryReserveEnemyDeadlift(other))
                failures.Add($"{fighter.Identity}:second_member_reserved={other.Identity}");
            break;
        }
        PlayerMovement player = UnityEngine.Object.FindAnyObjectByType<PlayerMovement>();
        if (player != null && station.TryReserveForPlayer(player))
        {
            station.CancelPlayerReservation(player);
            failures.Add($"{fighter.Identity}:player_reserved_during_set");
        }
        if (AnyBarItemPickable())
            failures.Add($"{fighter.Identity}:bar_or_plate_pickable_during_set");
        if (cancelSubject == null) cancelSubject = fighter;

        pauseStarted = -1d;
        longestPause = 0d;
        lastSegment = null;
        maxRise = 0f;
        worstGrip = 0f;
        worstPlate = 0f;
        worstTilt = 0f;
        worstBody = 0f;
        frames = 0;
        captured = new HashSet<string>();
        phase = 2;
        phaseStarted = EditorApplication.timeSinceStartup;
    }

    private static void TickSet()
    {
        double now = EditorApplication.timeSinceStartup;
        if (!controller.IsDeadliftActive)
        {
            CompleteSet();
            return;
        }
        if (now - phaseStarted > 45d)
            throw new InvalidOperationException($"{fighter.Identity} set did not finish");

        Transform bar = station.DeadliftBar;
        string segment = controller.DeadliftSegment;
        frames++;
        float rise = bar.position.y - station.DeadliftBarRestPosition.y;
        maxRise = Mathf.Max(maxRise, rise);
        if (rise < -0.001f) failures.Add($"{fighter.Identity}:bar_below_rest {rise:0.000}");
        float tilt = Quaternion.Angle(bar.rotation, station.DeadliftBarRestRotation);
        worstTilt = Mathf.Max(worstTilt, tilt);
        float plateMin = LowestPlateY(bar, out string lowestName);
        float plateDepth = station.DeadliftPlatformTopY - plateMin;
        if (plateDepth > worstPlate)
        {
            worstPlate = plateDepth;
            if (plateDepth > PlatePenetrationLimit)
                Debug.Log($"GYMCHAOS_DEADLIFT_MEMBER_PLATE_LOW enemy={fighter.Identity} depth={plateDepth:0.000} " +
                    $"renderer={lowestName} rise={rise:0.000} segment={segment} phase={controller.DeadliftPhase:0.000} " +
                    $"barPos={bar.position} rest={station.DeadliftBarRestPosition}");
        }
        if (frames > 2)
        {
            float grip = Mathf.Max(controller.DeadliftLeftGripError, controller.DeadliftRightGripError);
            if (grip > worstGrip && grip > GripAxisLimit)
                Debug.Log($"GYMCHAOS_DEADLIFT_MEMBER_GRIP_WORST enemy={fighter.Identity} grip={grip:0.000} " +
                    $"left={controller.DeadliftLeftGripError:0.000} right={controller.DeadliftRightGripError:0.000} " +
                    $"segment={segment} phase={controller.DeadliftPhase:0.000} rep={controller.DeadliftRepsDone} frame={frames} rise={rise:0.000}");
            worstGrip = Mathf.Max(worstGrip, grip);
            // Skin bakes are slow: sample the shaft against the body every
            // third frame.
            if (frames % 3 == 0)
            {
                float body = controller.MeasureDeadliftBodyPenetration(out string blocker);
                if (body > worstBody && body > BodyPenetrationLimit)
                    Debug.Log($"GYMCHAOS_DEADLIFT_MEMBER_BODY_WORST enemy={fighter.Identity} depth={body:0.000} " +
                        $"blocker={blocker} segment={segment} phase={controller.DeadliftPhase:0.000} rise={rise:0.000}");
                worstBody = Mathf.Max(worstBody, body);
            }
        }

        if (segment == "pause")
        {
            if (lastSegment != "pause") pauseStarted = now;
            longestPause = Math.Max(longestPause, now - pauseStarted);
        }
        if (segment == "floor" && controller.DeadliftRepsDone == 0 && frames > 3 && !captured.Contains("floor"))
        {
            MeasureContact("floor");
            Capture("floor");
        }
        else if (segment == "pull" && !captured.Contains("pull") &&
                 Mathf.InverseLerp(controller.DeadliftStartPhase, 1f, controller.DeadliftPhase) > 0.45f)
        {
            MeasureContact("pull");
            Capture("pull");
        }
        else if (segment == "pause" && !captured.Contains("pause") && now - pauseStarted > 0.25d)
        {
            MeasureContact("pause");
            Capture("pause");
        }
        else if (segment == "lower" && !captured.Contains("lower") &&
                 Mathf.InverseLerp(controller.DeadliftStartPhase, 1f, controller.DeadliftPhase) < 0.3f)
        {
            MeasureContact("lower");
            Capture("lower");
        }
        lastSegment = segment;
    }

    private static void CompleteSet()
    {
        Transform bar = station.DeadliftBar;
        string id = fighter.Identity.ToString();
        if (controller.DeadliftRepsDone < 3) failures.Add($"{id}:reps={controller.DeadliftRepsDone}");
        if (!station.IsDeadliftBarAtRest) failures.Add($"{id}:bar_not_back_on_mats");
        if (station.EnemyOccupant != null) failures.Add($"{id}:station_still_occupied");
        if (station.DeadliftMarker != null && station.DeadliftMarker.ActiveLifter != null)
            failures.Add($"{id}:lifter_not_cleared");
        PickupItem barPickup = bar.GetComponent<PickupItem>();
        if (barPickup != null && !barPickup.IsThrowableWeapon) failures.Add($"{id}:bar_still_locked");
        if (longestPause < MinimumPauseSeconds) failures.Add($"{id}:pause={longestPause:0.00}s");
        if (maxRise < 0.25f) failures.Add($"{id}:bar_barely_lifted rise={maxRise:0.000}");
        if (worstGrip > GripAxisLimit) failures.Add($"{id}:grip_off_bar {worstGrip:0.000}");
        if (worstPlate > PlatePenetrationLimit) failures.Add($"{id}:plates_into_mats {worstPlate:0.000}");
        if (worstTilt > 0.5f) failures.Add($"{id}:bar_tilted {worstTilt:0.0}");
        if (worstBody > BodyPenetrationLimit) failures.Add($"{id}:bar_through_body {worstBody:0.000}");
        foreach (string view in new[] { "floor", "pull", "pause", "lower" })
            if (!captured.Contains(view)) failures.Add($"{id}:not_sampled_{view}");
        string line = $"{id} start={controller.DeadliftStartPhase:0.000} rise={maxRise:0.000} pause={longestPause:0.00} " +
            $"grip={worstGrip:0.000} plate={worstPlate:0.000} tilt={worstTilt:0.0} body={worstBody:0.000} " +
            $"step={controller.DeadliftStanceStepBack:0.000} lift={controller.DeadliftGroundLift:0.000}";
        Debug.Log("GYMCHAOS_DEADLIFT_MEMBER_SET " + line);
        summary.Append(line).Append(" | ");
        fighter.gameObject.SetActive(false);
        phase = 3;
        phaseStarted = EditorApplication.timeSinceStartup;
    }

    private static float LowestPlateY(Transform bar, out string lowestName)
    {
        float lowest = float.PositiveInfinity;
        lowestName = "none";
        foreach (Renderer renderer in bar.GetComponentsInChildren<Renderer>())
        {
            if (!renderer.enabled || renderer.bounds.min.y >= lowest) continue;
            lowest = renderer.bounds.min.y;
            lowestName = renderer.name;
        }
        return lowest;
    }

    // Baked skin against the shaft: each hand touches it (no gap) and the
    // soles stand on the mats.
    private static void MeasureContact(string label)
    {
        Transform bar = station.DeadliftBar;
        Vector3 axis = station.DeadliftBarAxis;
        Vector3 center = bar.position;
        float radius = station.DeadliftShaftRadius;
        Transform leftHand = controller.LeftHandBoneForVerification;
        Transform rightHand = controller.RightHandBoneForVerification;
        float[] handMin = { float.PositiveInfinity, float.PositiveInfinity };
        float sole = float.PositiveInfinity;
        Mesh scratch = new Mesh();
        foreach (SkinnedMeshRenderer skin in fighter.GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            if (!skin.enabled || skin.sharedMesh == null) continue;
            Vector3[] vertices = GymSkinSampler.BakeWorld(skin, scratch);
            int[] dominant = GymSkinSampler.GetDominantBones(skin.sharedMesh);
            Transform[] bones = skin.bones;
            for (int i = 0; i < vertices.Length; i++)
            {
                sole = Mathf.Min(sole, vertices[i].y);
                int bone = i < dominant.Length ? dominant[i] : -1;
                Transform t = bone >= 0 && bone < bones.Length ? bones[bone] : null;
                int side = GymSkinSampler.IsInChain(t, leftHand) ? 0 : GymSkinSampler.IsInChain(t, rightHand) ? 1 : -1;
                if (side < 0) continue;
                Vector3 offset = vertices[i] - center;
                Vector3 radial = offset - axis * Vector3.Dot(offset, axis);
                handMin[side] = Mathf.Min(handMin[side], radial.magnitude - radius);
            }
        }
        UnityEngine.Object.Destroy(scratch);
        float soleError = sole - station.DeadliftPlatformTopY;
        string line = $"{fighter.Identity}:{label} handL={handMin[0]:0.000} handR={handMin[1]:0.000} " +
            $"sole={soleError:0.000} barR={radius:0.000} phase={controller.DeadliftPhase:0.000}";
        Debug.Log("GYMCHAOS_DEADLIFT_MEMBER_SAMPLE " + line);
        for (int side = 0; side < 2; side++)
            if (handMin[side] > HandGapLimit) failures.Add(line + (side == 0 ? " left_hand_gap" : " right_hand_gap"));
        if (Mathf.Abs(soleError) > SoleLimit) failures.Add(line + " soles_off_mats");
    }

    private static void Capture(string label)
    {
        captured.Add(label);
        string directory = Path.Combine(
            Directory.GetParent(Application.dataPath).Parent.FullName, "Logs", "verify", "deadlift-member");
        Directory.CreateDirectory(directory);
        Transform subject = fighter.transform;
        Vector3 focus = subject.position + Vector3.up * 1.4f;
        Vector3 barCenter = station.DeadliftBar.position;
        var views = new List<(string, Vector3, Vector3, float)>
        {
            ("front", subject.position + Vector3.up * 1.0f, subject.forward * 4.2f + Vector3.up * 0.5f, 40f),
            ("hand", barCenter + station.DeadliftBarAxis * 0.45f, subject.forward * 1.4f + subject.right * 0.9f + Vector3.up * 0.35f, 40f),
            // From the side with the plates hidden: shaft against the legs.
            ("profile", subject.position + Vector3.up * 0.95f, subject.right * 4.2f + Vector3.up * 0.2f, 40f),
        };
        const int CaptureLayer = 31;
        Dictionary<Transform, int> layers = new Dictionary<Transform, int>();
        foreach (Transform t in subject.GetComponentsInChildren<Transform>(true))
        {
            layers[t] = t.gameObject.layer;
            t.gameObject.layer = CaptureLayer;
        }
        Transform platform = station.DeadliftMarker != null ? station.DeadliftMarker.transform : null;
        foreach (Transform root in new[] { station.DeadliftBar, platform })
        {
            if (root == null) continue;
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                if (layers.ContainsKey(t)) continue;
                layers[t] = t.gameObject.layer;
                t.gameObject.layer = CaptureLayer;
            }
        }
        List<Renderer> plates = new List<Renderer>();
        PickupItem barItem = station.DeadliftBar.GetComponent<PickupItem>();
        foreach (PickupItem item in station.DeadliftBar.GetComponentsInChildren<PickupItem>())
            if (item != barItem) plates.AddRange(item.GetComponentsInChildren<Renderer>());
        foreach ((string view, Vector3 target, Vector3 offset, float fov) in views)
        {
            foreach (Renderer plate in plates) plate.enabled = view != "profile";
            GameObject host = new GameObject("Deadlift Member Capture Camera");
            Camera camera = host.AddComponent<Camera>();
            camera.cullingMask = 1 << CaptureLayer;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.55f, 0.58f, 0.62f);
            camera.fieldOfView = fov;
            camera.nearClipPlane = 0.02f;
            Vector3 eye = target + offset;
            camera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(target - eye));
            RenderTexture renderTarget = new RenderTexture(520, 520, 24);
            camera.targetTexture = renderTarget;
            camera.Render();
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = renderTarget;
            Texture2D image = new Texture2D(520, 520, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, 520, 520), 0, 0);
            image.Apply();
            RenderTexture.active = previous;
            camera.targetTexture = null;
            File.WriteAllBytes(Path.Combine(directory, $"{fighter.Identity}-{label}-{view}.png"), image.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(image);
            renderTarget.Release();
            UnityEngine.Object.DestroyImmediate(renderTarget);
            UnityEngine.Object.DestroyImmediate(host);
        }
        foreach (Renderer plate in plates) plate.enabled = true;
        foreach (KeyValuePair<Transform, int> entry in layers)
            if (entry.Key != null) entry.Key.gameObject.layer = entry.Value;
    }

    private static void Finish()
    {
        if (failures.Count > 0)
        {
            foreach (string failure in failures) Debug.LogError("GYMCHAOS_DEADLIFT_MEMBER_FAILED " + failure);
            Exit(1);
            return;
        }
        Debug.Log("GYMCHAOS_DEADLIFT_MEMBER_OK " + summary.ToString().Trim());
        Exit(0);
    }

    private static void Exit(int code)
    {
        SessionState.EraseBool(RequestedKey);
        SessionState.EraseBool(LifecycleKey);
        EditorApplication.update -= Tick;
        Time.maximumDeltaTime = savedMaxDelta > 0f ? savedMaxDelta : Time.maximumDeltaTime;
        if (Application.isBatchMode) EditorApplication.Exit(code);
        else EditorApplication.isPlaying = false;
    }
}
#endif
