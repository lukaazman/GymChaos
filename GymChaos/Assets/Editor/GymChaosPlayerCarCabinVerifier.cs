#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// The player's race buggy (GymPlayerRaceCar) on flat ground: the player
/// walks to the side door and gets in (door opens fully, then shuts, with the
/// door sounds), sits in the driving pose with the head under the roll bar
/// and the hands on the rim while the car rolls and steers (wheels spin and
/// the front wheels turn), and climbs out again. The skin must not pass
/// through the body tub, door or windscreen (worst step at most 1.5 % of the
/// sampled skin). With -Graphics, PNGs go to Logs/agent/race/player-car.
/// </summary>
[InitializeOnLoad]
public static class GymChaosPlayerCarCabinVerifier
{
    private const string RequestedKey = "GymChaos.PlayerCarCabinVerificationRequested";
    private const float MaxDeltaTime = 1f / 30f;
    private const float HipsStepLimit = 0.16f;
    private const float DriveDistance = 9f;
    private const float DriveSeconds = 4f;
    private const float SteerDegrees = 24f;

    private enum Stage { Boot, WaitCar, Settle, Boarding, Driving, Exiting, Standing, Done }

    private static readonly List<string> failures = new List<string>();
    private static readonly List<string> results = new List<string>();
    private static readonly HashSet<string> captured = new HashSet<string>();
    private static Stage stage;
    private static double startedAt;
    private static double stageAt;
    private static bool finished;
    private static int resultCode = 1;
    private static float previousMaxDelta;
    private static bool previousBackfaces;
    private static PlayerMovement player;
    private static PlayerVehicleOccupant occupant;
    private static GymPlayerRaceCar car;
    private static bool carCallback;
    private static Vector3 carStart;
    private static GymChaosCabinPenetrationProbe probe;
    private static int probeTick;
    private static int seatedFrames;
    private static int outsideCabinFrames;
    private static float worstHeadClearance;
    private static float worstHandGap;
    private static float doorMaxBoarding;
    private static float doorMaxExit;
    private static bool doorShutAfterBoarding;
    private static float hipsStepMax;
    private static string hipsStepAt = "";
    private static Vector3? lastHips;
    private static bool boarded;
    private static bool standing;
    private static float steerSeen;
    private static int doorOpenBefore;
    private static int doorCloseBefore;

    static GymChaosPlayerCarCabinVerifier()
    {
        if (!SessionState.GetBool(RequestedKey, false)) return;
        Hook();
    }

    [MenuItem("Tools/GymChaos/Run Player Car Cabin Verification")]
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
            stage = Stage.Boot;
            failures.Clear();
            results.Clear();
            captured.Clear();
            car = null;
            carCallback = false;
            probe = null;
            probeTick = 0;
            seatedFrames = 0;
            outsideCabinFrames = 0;
            worstHeadClearance = float.PositiveInfinity;
            worstHandGap = 0f;
            doorMaxBoarding = 0f;
            doorMaxExit = 0f;
            doorShutAfterBoarding = false;
            hipsStepMax = 0f;
            lastHips = null;
            boarded = false;
            standing = false;
            steerSeen = 0f;
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
            if (now - startedAt > 300d) throw new TimeoutException($"whole run (stage {stage})");
            switch (stage)
            {
                case Stage.Boot: TickBoot(now); break;
                case Stage.WaitCar: TickWaitCar(now); break;
                case Stage.Settle: TickSettle(now); break;
                case Stage.Boarding: TickBoarding(now); break;
                case Stage.Driving: TickDriving(now); break;
                case Stage.Exiting: TickExiting(now); break;
                case Stage.Standing: TickStanding(now); break;
            }
        }
        catch (Exception exception)
        {
            failures.Add(exception.Message);
            Report();
        }
    }

    private static void Enter(Stage next, double now)
    {
        stage = next;
        stageAt = now;
    }

    private static void TickBoot(double now)
    {
        if (!GymOutdoorBuilder.IsBuilt || now - startedAt < 6d) return;
        player = UnityEngine.Object.FindFirstObjectByType<PlayerMovement>();
        if (player == null) return;
        UnityEngine.Object.FindFirstObjectByType<GymVisitorDirector>()?.HoldScheduledArrivalsForVerification();
        previousMaxDelta = Time.maximumDeltaTime;
        Time.maximumDeltaTime = MaxDeltaTime;
        // Shell faces point outwards: a limb leaving the tub from inside hits a back face.
        previousBackfaces = Physics.queriesHitBackfaces;
        Physics.queriesHitBackfaces = true;
        occupant = PlayerVehicleOccupant.Attach(player);
        if (!FindClearGround(out Vector3 ground))
            throw new InvalidOperationException("no clear flat ground for the buggy");
        carStart = ground;
        car = GymPlayerRaceCar.Spawn(ground, Quaternion.Euler(0f, 90f, 0f), null, loaded => carCallback = true);
        if (car == null) throw new InvalidOperationException("buggy request failed");
        results.Add($"ground={ground}");
        Enter(Stage.WaitCar, now);
    }

    // A flat, empty patch of the outdoor ground with room for the car and
    // for the player beside its door.
    private static bool FindClearGround(out Vector3 ground)
    {
        Collider[] own = player.GetComponentsInChildren<Collider>(true);
        for (float x = 150f; x <= 205f; x += 5f)
        {
            for (float z = -25f; z <= 50f; z += 5f)
            {
                if (!Physics.Raycast(new Vector3(x, 30f, z), Vector3.down, out RaycastHit hit, 40f,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) continue;
                if (hit.normal.y < 0.995f || hit.point.y < -0.4f || hit.point.y > 0.3f) continue;
                Collider[] blockers = Physics.OverlapBox(hit.point + Vector3.up * 1.6f,
                    new Vector3(5f, 1.35f, 5f), Quaternion.identity, Physics.DefaultRaycastLayers,
                    QueryTriggerInteraction.Ignore);
                bool clear = true;
                foreach (Collider blocker in blockers)
                {
                    if (Array.IndexOf(own, blocker) < 0) { clear = false; break; }
                }
                if (!clear) continue;
                ground = hit.point;
                return true;
            }
        }
        ground = Vector3.zero;
        return false;
    }

    private static void TickWaitCar(double now)
    {
        if (!carCallback)
        {
            if (now - stageAt > 25d) throw new TimeoutException("buggy GLB never loaded");
            return;
        }
        if (car == null || !car.IsReady)
            throw new InvalidOperationException($"buggy not ready ({GymPlayerRaceCar.Asset}: GLB, cabin or seat missing)");
        GymVehicleCabin cabin = car.Cabin;
        foreach (string marker in new[] { "Seat", "Roof", "Wheel", "Floor", "Hinge" })
            if (!cabin.TryGetMarker(marker, out _)) failures.Add($"marker {marker} missing");
        if (car.Wheels.WheelCount < 4) failures.Add($"only {car.Wheels.WheelCount} wheels found");
        if (cabin.GlassRendererCount < 1) failures.Add("no windscreen glass");
        if (!GymVehicleDriverSeat.SupportsDriver(occupant))
            throw new InvalidOperationException($"player cannot drive (clips={occupant.HasVehicleClips} rig={occupant.BodyRig != null})");
        if (!car.Seat.EnsureFit(occupant)) throw new InvalidOperationException("player seat fit failed");
        // Beside the door, facing it.
        Vector3 door = car.Seat.DoorPointWorld;
        Vector3 outward = Vector3.ProjectOnPlane(cabin.MarkerWorld("Hinge") - car.transform.position, car.transform.forward);
        outward = Vector3.ProjectOnPlane(outward, Vector3.up).normalized;
        occupant.PlaceStanding(door + outward * 1.6f - car.transform.forward * 0.4f,
            Quaternion.LookRotation(-outward, Vector3.up));
        results.Add($"wheels={car.Wheels.WheelCount} glass={cabin.GlassRendererCount} " +
            $"interior={cabin.InteriorRendererCount} doorParts={cabin.DoorRendererCount} length={GymPlayerRaceCar.TargetLength:F2}");
        Enter(Stage.Settle, now);
    }

    private static void TickSettle(double now)
    {
        if (now - stageAt < 1.5d) return;
        Capture("standing-before", Side());
        GymAudio.PlayCountsForVerification.TryGetValue(GymSoundEffect.CarDoorOpen, out doorOpenBefore);
        GymAudio.PlayCountsForVerification.TryGetValue(GymSoundEffect.CarDoorClose, out doorCloseBefore);
        if (!car.Seat.BeginBoarding(occupant, () => boarded = true))
            throw new InvalidOperationException("boarding did not start");
        lastHips = null;
        Probe().ResetMotion();
        Enter(Stage.Boarding, now);
    }

    private static GymChaosCabinPenetrationProbe Probe()
    {
        if (probe == null)
            probe = new GymChaosCabinPenetrationProbe(car.transform, occupant.HandRig.RuntimeModelRoot);
        return probe;
    }

    private static Transform Hips() => occupant.BodyRig?.Hips;

    private static void TrackHips()
    {
        GymVehicleDriverSeat seat = car.Seat;
        if (probeTick++ % 2 == 0) Probe().TrackMotion($"{seat.State}@{seat.ExitPhase:F2}");
        Vector3 hips = Hips().position;
        if (lastHips.HasValue)
        {
            float step = Vector3.Distance(hips, lastHips.Value);
            if (step > hipsStepMax) hipsStepAt = $"{seat.State}@{seat.ExitPhase:F3} d={hips - lastHips.Value}";
            hipsStepMax = Mathf.Max(hipsStepMax, step);
        }
        lastHips = hips;
    }

    private static void TickBoarding(double now)
    {
        if (now - stageAt > 40d) throw new TimeoutException($"boarding never finished (seat={car.Seat.State})");
        GymVehicleDriverSeat seat = car.Seat;
        TrackHips();
        doorMaxBoarding = Mathf.Max(doorMaxBoarding, seat.Cabin.DoorOpen01);
        if (seat.State == GymVehicleDriverSeat.SeatState.Boarding && seat.ExitPhase < 0.5f) Capture("boarding", Side());
        if (!boarded) return;
        doorShutAfterBoarding = seat.Cabin.DoorOpen01 <= 0.001f;
        if (!occupant.IsSeated) failures.Add("player not seated after boarding");
        if (occupant.CurrentVehiclePose != MixamoScanRetargetAnimator.VehiclePose.Driving)
            failures.Add($"pose after boarding {occupant.CurrentVehiclePose}");
        PenetrationFailures("boarding");
        results.Add($"boarding doorMax={doorMaxBoarding:F2} hipsStep={hipsStepMax:F3}");
        if (doorMaxBoarding < 0.95f) failures.Add($"boarding door opened only {doorMaxBoarding:F2}");
        if (!doorShutAfterBoarding) failures.Add($"door left open after boarding {seat.Cabin.DoorOpen01:F2}");
        if (hipsStepMax > HipsStepLimit) failures.Add($"boarding hips snapped {hipsStepMax:F3} m at {hipsStepAt}");
        car.IsDriving = true;
        Enter(Stage.Driving, now);
    }

    private static void TickDriving(double now)
    {
        GymVehicleDriverSeat seat = car.Seat;
        float t = Mathf.Clamp01((float)((now - stageAt) / DriveSeconds));
        if (seat.State != GymVehicleDriverSeat.SeatState.Seated || !occupant.IsSeated ||
            occupant.CurrentVehiclePose != MixamoScanRetargetAnimator.VehiclePose.Driving)
            throw new InvalidOperationException($"player not seated while driving ({seat.State}, {occupant.CurrentVehiclePose})");
        // Judge the pose the last frame drew (before this step moves the car),
        // once the boarding-to-driving blend is over.
        if (now - stageAt > 0.4d) CheckSeated(seat);
        car.transform.position = carStart + car.transform.forward * (DriveDistance * t);
        float steer = Mathf.Sin(t * Mathf.PI * 2f) * SteerDegrees;
        car.Wheels.SetSteer(steer);
        steerSeen = Mathf.Max(steerSeen, Mathf.Abs(steer));
        if (t > 0.3f) Capture("driving-front", Front());
        if (t > 0.5f) Capture("driving-side", Side());
        if (t > 0.6f) Capture("driving-top", Top());
        if (t < 1f) return;
        CheckWheels();
        car.IsDriving = false;
        car.Wheels.SetSteer(0f);
        string line = $"seatedFrames={seatedFrames} headClear={worstHeadClearance:F3} handGap={worstHandGap:F3} " +
            $"sink={seat.SeatSink:F3} fitHeadClear={seat.HeadClearance:F3}";
        results.Add(line);
        Debug.Log("GYMCHAOS_PLAYER_CAR_SEATED " + line);
        if (seatedFrames < 30) failures.Add($"only {seatedFrames} seated frames");
        if (outsideCabinFrames > 0) failures.Add($"hips left the cabin {outsideCabinFrames} frames");
        if (worstHeadClearance < 0f) failures.Add($"head above the roll bar {worstHeadClearance:F3}");
        if (seat.HeadClearance < -0.02f) failures.Add($"crown {seat.HeadClearance:F3} m above the roll bar");
        if (worstHandGap > 0.12f) failures.Add($"hands {worstHandGap:F3} m off the rim");
        PenetrationFailures("seated");
        lastHips = null;
        hipsStepMax = 0f;
        Probe().ResetMotion();
        if (!seat.BeginExit(() => standing = true)) throw new InvalidOperationException("exit did not start");
        Enter(Stage.Exiting, now);
    }

    private static void CheckSeated(GymVehicleDriverSeat seat)
    {
        seatedFrames++;
        if (probeTick++ % 20 == 0) Probe().CheckSeated(seat.Cabin.MarkerWorld("Seat") + car.transform.up * 0.75f);
        GymVehicleCabin cabin = seat.Cabin;
        BodybuilderEnemyVisual.Rig rig = occupant.BodyRig;
        Transform frame = car.CabinFrame;
        Vector3 hipsLocal = car.transform.InverseTransformPoint(Hips().position);
        Bounds body = car.LocalBodyBounds;
        if (!body.Contains(new Vector3(hipsLocal.x, body.center.y, hipsLocal.z))) outsideCabinFrames++;
        cabin.TryGetMarker("Roof", out Vector3 roof);
        float clearance = roof.y - frame.InverseTransformPoint(rig.Head.position).y;
        worstHeadClearance = Mathf.Min(worstHeadClearance, clearance);
        Vector3 wheel = cabin.WheelPivot.position;
        float radius = GymVehicleCabin.AuthoredWheelRadius * cabin.WheelPivot.lossyScale.x;
        float gap = Mathf.Max(
            Mathf.Abs(Vector3.Distance(rig.LeftHand.position, wheel) - radius),
            Mathf.Abs(Vector3.Distance(rig.RightHand.position, wheel) - radius));
        worstHandGap = Mathf.Max(worstHandGap, gap);
    }

    private static void CheckWheels()
    {
        GymVehicleWheelSpinner wheels = car.Wheels;
        results.Add($"wheels spin={wheels.TotalSpinDegrees:F0} rolled={wheels.TotalRolledDistance:F2} " +
            $"steered={wheels.SteeredWheelCount} steerMax={steerSeen:F1}");
        if (wheels.TotalSpinDegrees < 360f) failures.Add($"wheels spun only {wheels.TotalSpinDegrees:F0} degrees");
        if (wheels.SteeredWheelCount != 2) failures.Add($"{wheels.SteeredWheelCount} steered wheels, expected 2");
        for (int i = 0; i < wheels.WheelCount; i++)
        {
            Transform pivot = wheels.GetSteerPivot(i);
            if (pivot == null) continue;
            // Front wheels: ahead of the car centre, turned with the steer.
            if (car.transform.InverseTransformPoint(pivot.position).z <= 0f)
                failures.Add($"steered wheel {wheels.GetWheel(i).name} is not a front wheel");
            float yaw = Vector3.SignedAngle(car.transform.forward,
                Vector3.ProjectOnPlane(pivot.forward, car.transform.up), car.transform.up);
            if (Mathf.Abs(yaw - wheels.SteerDegrees) > 1f)
                failures.Add($"wheel {wheels.GetWheel(i).name} yaw {yaw:F1} != steer {wheels.SteerDegrees:F1}");
        }
    }

    private static void TickExiting(double now)
    {
        if (now - stageAt > 40d) throw new TimeoutException($"exit never finished (seat={car.Seat.State})");
        GymVehicleDriverSeat seat = car.Seat;
        TrackHips();
        doorMaxExit = Mathf.Max(doorMaxExit, seat.Cabin.DoorOpen01);
        if (seat.State == GymVehicleDriverSeat.SeatState.Exiting)
        {
            foreach (float mark in new[] { 0.2f, 0.45f, 0.7f })
                if (seat.ExitPhase > mark) Capture($"exit{mark:F2}", Close());
            LogPose(seat);
        }
        if (!standing) return;
        Enter(Stage.Standing, now);
    }

    private static int loggedPoseBucket = -1;

    // The climb path in the car's authored frame (GLB metres), every 0.1 of the clip.
    private static void LogPose(GymVehicleDriverSeat seat)
    {
        int bucket = Mathf.FloorToInt(seat.ExitPhase * 10f);
        if (bucket == loggedPoseBucket) return;
        loggedPoseBucket = bucket;
        BodybuilderEnemyVisual.Rig rig = occupant.BodyRig;
        string P(Transform bone) => bone != null ? car.transform.InverseTransformPoint(bone.position).ToString("F2") : "-";
        Debug.Log($"GYMCHAOS_PLAYER_CAR_POSE phase={seat.ExitPhase:F2} door={seat.Cabin.DoorOpen01:F2} hips={P(rig.Hips)} " +
            $"head={P(rig.Head)} kneeL={P(rig.LeftShin)} kneeR={P(rig.RightShin)} footL={P(rig.LeftFoot)} footR={P(rig.RightFoot)} " +
            $"handL={P(rig.LeftHand)} handR={P(rig.RightHand)}");
    }

    private static void TickStanding(double now)
    {
        if (now - stageAt < 1.2d) return;
        GymVehicleDriverSeat seat = car.Seat;
        Capture("standing-after", Side());
        if (occupant.IsInVehicleSeatMode) failures.Add("player still in seat mode after exit");
        if (player.IsCinematicLocked) failures.Add("player input still locked after exit");
        CharacterController controller = player.GetComponent<CharacterController>();
        if (controller != null && !controller.enabled) failures.Add("character controller left off");
        if (occupant.CurrentVehiclePose != MixamoScanRetargetAnimator.VehiclePose.None)
            failures.Add($"car pose still active {occupant.CurrentVehiclePose}");
        if (seat.Cabin.DoorOpen01 > 0.001f) failures.Add("door left open after exit");
        if (doorMaxExit < 0.95f) failures.Add($"exit door opened only {doorMaxExit:F2}");
        if (hipsStepMax > HipsStepLimit) failures.Add($"exit hips snapped {hipsStepMax:F3} m at {hipsStepAt}");
        float outside = Mathf.Abs(car.transform.InverseTransformPoint(Hips().position).x) - car.LocalBodyBounds.extents.x;
        if (outside < 0.1f) failures.Add($"player not beside the car after exit ({outside:F2} m)");
        PenetrationFailures("exit");
        GymAudio.PlayCountsForVerification.TryGetValue(GymSoundEffect.CarDoorOpen, out int opens);
        GymAudio.PlayCountsForVerification.TryGetValue(GymSoundEffect.CarDoorClose, out int closes);
        opens -= doorOpenBefore;
        closes -= doorCloseBefore;
        if (opens < 2 || closes < 2) failures.Add($"door sounds open={opens} close={closes}, expected 2 each");
        results.Add($"exit doorMax={doorMaxExit:F2} hipsStep={hipsStepMax:F3} besideCar={outside:F2} " +
            $"doorSfx={opens}/{closes}");
        Report();
    }

    // Seated: no skin sample past the tub or the windscreen (0.1 %, 7 cm
    // seam rounding). Moving: the worst step crosses at most 1.5 % of the
    // sampled skin and all steps together 0.3 %.
    private static void PenetrationFailures(string phase)
    {
        GymChaosCabinPenetrationProbe p = Probe();
        float share = p.SeatedSamples > 0 ? p.SeatedOutside / (float)p.SeatedSamples : 0f;
        float motionShare = p.MotionSamples > 0 && p.SampledVertices > 0
            ? p.Crossings / (float)(p.MotionSamples * p.SampledVertices) : 0f;
        float worstShare = p.SampledVertices > 0 ? p.WorstSampleCrossings / (float)p.SampledVertices : 0f;
        string line = $"{phase} shellParts={p.ShellPartCount} seated={share:P3} depth={p.SeatedDepth:F3} " +
            $"motion={motionShare:P3} worstStep={worstShare:P2} [{p.WorstStep}] {p.WorstCrossing} {p.Histogram()}";
        Debug.Log("GYMCHAOS_PLAYER_CAR_PENETRATION " + line);
        results.Add("penetration " + line);
        if (p.ShellPartCount < 3) failures.Add($"shell probe found {p.ShellPartCount} parts");
        if (phase == "seated" && p.SeatedSamples == 0) failures.Add("no seated skin samples");
        if (share > 0.001f || p.SeatedDepth > 0.07f)
            failures.Add($"{phase}: body outside the car {share:P2} depth {p.SeatedDepth:F3}");
        if (motionShare > 0.003f || worstShare > 0.015f)
            failures.Add($"{phase}: body passed through the car share={motionShare:P3} worstStep={worstShare:P2} ({p.WorstCrossing})");
    }

    private static (Vector3 eye, Vector3 target) Front()
    {
        Transform t = car.transform;
        Vector3 target = car.Cabin.MarkerWorld("Seat") + Vector3.up * 0.7f;
        return (t.position + t.forward * 5.5f + Vector3.up * 1.8f, target);
    }

    private static (Vector3 eye, Vector3 target) Side()
    {
        Transform t = car.transform;
        Vector3 hinge = car.Cabin.MarkerWorld("Hinge");
        Vector3 outward = Vector3.ProjectOnPlane(Vector3.ProjectOnPlane(hinge - t.position, t.forward), Vector3.up).normalized;
        Vector3 target = t.position + outward * 0.6f + Vector3.up * 1.0f;
        return (target + outward * 6f + t.forward * 1.5f + Vector3.up * 1.0f, target);
    }

    private static (Vector3 eye, Vector3 target) Top()
    {
        Transform t = car.transform;
        Vector3 target = car.Cabin.MarkerWorld("Seat");
        return (target + Vector3.up * 5.5f - t.forward * 3.5f + t.right * 1.5f, target);
    }

    private static (Vector3 eye, Vector3 target) Close()
    {
        Transform t = car.transform;
        Vector3 hinge = car.Cabin.MarkerWorld("Hinge");
        Vector3 outward = Vector3.ProjectOnPlane(Vector3.ProjectOnPlane(hinge - t.position, t.forward), Vector3.up).normalized;
        Vector3 target = hinge + outward * 0.4f - t.forward * 0.6f + Vector3.up * 0.5f;
        return (target + outward * 3.6f - t.forward * 1.8f + Vector3.up * 0.9f, target);
    }

    private static void Capture(string label, (Vector3 eye, Vector3 target) view)
    {
        if (Application.isBatchMode && SystemInfo.graphicsDeviceType ==
            UnityEngine.Rendering.GraphicsDeviceType.Null) return;
        if (!captured.Add(label)) return;
        string directory = Path.Combine(
            Directory.GetParent(Application.dataPath).Parent.FullName, "Logs", "agent", "race", "player-car");
        Directory.CreateDirectory(directory);
        GameObject host = new GameObject("Player Car Capture Camera");
        Camera camera = host.AddComponent<Camera>();
        camera.fieldOfView = 42f;
        camera.nearClipPlane = 0.05f;
        // The full body (mirror layer) without the first-person arms.
        camera.cullingMask = ~(1 << PlanarGymMirror.FirstPersonPlayerLayer);
        camera.transform.SetPositionAndRotation(view.eye, Quaternion.LookRotation(view.target - view.eye));
        RenderTexture target = new RenderTexture(900, 600, 24);
        camera.targetTexture = target;
        camera.Render();
        RenderTexture previous = RenderTexture.active;
        RenderTexture.active = target;
        Texture2D image = new Texture2D(900, 600, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0, 0, 900, 600), 0, 0);
        image.Apply();
        RenderTexture.active = previous;
        camera.targetTexture = null;
        File.WriteAllBytes(Path.Combine(directory, $"unity-{label}.png"), image.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(image);
        target.Release();
        UnityEngine.Object.DestroyImmediate(target);
        UnityEngine.Object.DestroyImmediate(host);
    }

    private static void Report()
    {
        if (failures.Count == 0)
        {
            Debug.Log("GYMCHAOS_PLAYER_CAR_CABIN_OK " + string.Join(" | ", results));
            Finish(0);
        }
        else
        {
            Debug.LogError("GYMCHAOS_PLAYER_CAR_CABIN_FAIL " + string.Join(" | ", failures) +
                " || " + string.Join(" | ", results));
            Finish(1);
        }
    }

    private static void Finish(int code)
    {
        finished = true;
        resultCode = code;
        if (previousMaxDelta > 0f) Time.maximumDeltaTime = previousMaxDelta;
        Physics.queriesHitBackfaces = previousBackfaces;
        probe?.Dispose();
        probe = null;
        GymChaosVerifierExit.Record(resultCode);
        SessionState.EraseBool(RequestedKey);
        EditorApplication.isPlaying = false;
    }
}
#endif
