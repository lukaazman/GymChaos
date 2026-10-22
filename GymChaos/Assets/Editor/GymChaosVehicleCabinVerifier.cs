#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Driven visitor cars (everyone but Goku's cloud and Davie's bus): the
/// driver is visible in the cabin while the car drives in (seated, hips in
/// the car, head under the roof, hands on the wheel), climbs out through the
/// driver door after parking (door opens and shuts), and walks off to the gym
/// without any visible snap; on departure the visitor walks back, gets in
/// and the car leaves with the driver inside.
/// GYMCHAOS_CABIN_IDENTITIES (comma list) picks the drivers; departure runs
/// for the first one. With -Graphics, PNGs go to Logs/verify/vehicle-cabin.
/// </summary>
[InitializeOnLoad]
public static class GymChaosVehicleCabinVerifier
{
    private const string RequestedKey = "GymChaos.VehicleCabinVerificationRequested";
    // Bounded game step so a per-frame hips step is a real snap, not load.
    private const float MaxDeltaTime = 1f / 30f;
    private const float HipsStepLimit = 0.16f;
    private const double IdentityTimeout = 75d;
    private const double DepartureTimeout = 160d;

    private enum Stage { Boot, Start, Arrive, Exit, Standing, DepartureWait, Departure, Done }

    private static readonly List<BodybuilderIdentity> queue = new List<BodybuilderIdentity>();
    private static readonly List<string> failures = new List<string>();
    private static readonly List<string> results = new List<string>();
    private static readonly HashSet<string> captured = new HashSet<string>();
    private static Stage stage;
    private static double startedAt;
    private static double stageAt;
    private static bool finished;
    private static int resultCode = 1;
    private static float previousMaxDelta;
    private static GymVisitorDirector director;
    private static EnemyFighter fighter;
    private static GymVisitorVehicle vehicle;
    private static BodybuilderIdentity current;
    private static bool departureIdentity;
    private static int seatedDrivingFrames;
    private static double seatedSince;
    private static GymChaosCabinPenetrationProbe probe;
    private static int probeTick;
    private static int outsideCabinFrames;
    private static float worstHeadClearance;
    private static float worstHandGap;
    private static float doorMax;
    private static float doorMaxBoarding;
    private static float hipsStepMax;
    private static string hipsStepAt = "";
    private static Vector3? lastHips;
    private static double standingAt;
    private static bool sawBoarding;
    private static bool sawDrivingOff;

    static GymChaosVehicleCabinVerifier()
    {
        if (!SessionState.GetBool(RequestedKey, false)) return;
        Hook();
    }

    [MenuItem("Tools/GymChaos/Run Vehicle Cabin Verification")]
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
            queue.Clear();
            failures.Clear();
            results.Clear();
            captured.Clear();
            string list = Environment.GetEnvironmentVariable("GYMCHAOS_CABIN_IDENTITIES");
            foreach (string name in (string.IsNullOrEmpty(list) ? "Cbum,Zyzz,JayCutler,Arnold" : list).Split(','))
            {
                if (Enum.TryParse(name.Trim(), true, out BodybuilderIdentity id)) queue.Add(id);
            }
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
            if (now - startedAt > 600d) throw new TimeoutException("whole run");
            switch (stage)
            {
                case Stage.Boot:
                    if (!GymOutdoorBuilder.IsBuilt || now - startedAt < 6d) return;
                    director = UnityEngine.Object.FindFirstObjectByType<GymVisitorDirector>();
                    if (director == null) return;
                    director.HoldScheduledArrivalsForVerification();
                    previousMaxDelta = Time.maximumDeltaTime;
                    Time.maximumDeltaTime = MaxDeltaTime;
                    departureIdentity = true;
                    NextIdentity(now);
                    break;
                case Stage.Start:
                    TickStart(now);
                    break;
                case Stage.Arrive:
                    TickArrive(now);
                    break;
                case Stage.Exit:
                    TickExit(now);
                    break;
                case Stage.Standing:
                    TickStanding(now);
                    break;
                case Stage.DepartureWait:
                    if (now - stageAt < 2d) return;
                    // Do not send the driver out through the gym door while
                    // another visitor is coming in through it.
                    if (now - stageAt < 90d && OtherVisitorEntering()) return;
                    if (!director.BeginIdentityDepartureForVerification(current, out fighter, out vehicle))
                        throw new InvalidOperationException($"{current}: departure did not start");
                    Enter(Stage.Departure, now);
                    break;
                case Stage.Departure:
                    TickDeparture(now);
                    break;
            }
        }
        catch (Exception exception)
        {
            failures.Add(exception.Message);
            Report();
        }
    }

    private static bool OtherVisitorEntering()
    {
        foreach (GymVisitorAgent agent in GymVisitorAgent.ActiveAgents)
        {
            if (agent == null || agent.gameObject == fighter.gameObject) continue;
            GymVisitorAgent.VisitorState state = agent.State;
            if (state == GymVisitorAgent.VisitorState.ApproachingGymFromVehicle ||
                state == GymVisitorAgent.VisitorState.EnteringDoor ||
                state == GymVisitorAgent.VisitorState.EnteringRoom ||
                state == GymVisitorAgent.VisitorState.ExitingDoor ||
                state == GymVisitorAgent.VisitorState.LeavingGym ||
                state == GymVisitorAgent.VisitorState.ApproachingVehicle)
            {
                return true;
            }
        }
        return false;
    }

    private static void NextIdentity(double now)
    {
        if (queue.Count == 0)
        {
            Report();
            return;
        }
        current = queue[0];
        queue.RemoveAt(0);
        seatedDrivingFrames = 0;
        outsideCabinFrames = 0;
        worstHeadClearance = float.PositiveInfinity;
        worstHandGap = 0f;
        doorMax = 0f;
        doorMaxBoarding = 0f;
        hipsStepMax = 0f;
        lastHips = null;
        sawBoarding = false;
        sawDrivingOff = false;
        probe?.Dispose();
        probe = null;
        probeTick = 0;
        Enter(Stage.Start, now);
    }

    // A visitor can still be mid-visit from the scene's own start: retry.
    private static void TickStart(double now)
    {
        if (director.BeginVehicleArrivalForVerification(current, out fighter, out vehicle))
        {
            Enter(Stage.Arrive, now);
        }
        else if (now - stageAt > 180d)
        {
            throw new InvalidOperationException($"{current}: arrival did not start");
        }
    }

    private static void Enter(Stage next, double now)
    {
        stage = next;
        stageAt = now;
    }

    private static Transform Hips()
    {
        BodybuilderEnemyVisual.Rig rig = fighter.VehicleBodyAnimator?.BodyRig;
        return rig?.Hips != null ? rig.Hips : rig?.Root;
    }

    private static void TickArrive(double now)
    {
        if (now - stageAt > IdentityTimeout) throw new TimeoutException($"{current}: car never parked");
        GymVehicleDriverSeat seat = vehicle.DriverSeat;
        if (vehicle.IsDriving && seat != null)
        {
            if (!fighter.gameObject.activeInHierarchy || !fighter.IsSeatedInVehicle ||
                fighter.VehicleBodyAnimator.CurrentVehiclePose != MixamoScanRetargetAnimator.VehiclePose.Driving)
            {
                throw new InvalidOperationException($"{current}: driver not seated while the car drives");
            }
            // Mounting blends into the seated pose (0.12 s) at the far spawn
            // corner: judge the pose once that blend is over.
            if (seatedDrivingFrames++ == 0) seatedSince = now;
            if (now - seatedSince > 0.4d) CheckSeated(seat);
            float toBay = Vector3.Distance(vehicle.transform.position, vehicle.ParkingPointForVerification);
            if (toBay < 10f && toBay > 4f) Capture("driving", Front(seat));
        }
        if (seat != null && seat.State == GymVehicleDriverSeat.SeatState.Exiting)
        {
            if (seatedDrivingFrames < 30)
                throw new InvalidOperationException($"{current}: only {seatedDrivingFrames} seated driving frames");
            Probe().ResetMotion();
            Enter(Stage.Exit, now);
        }
    }

    private static GymChaosCabinPenetrationProbe Probe()
    {
        if (probe == null) probe = new GymChaosCabinPenetrationProbe(vehicle.transform, fighter);
        return probe;
    }

    private static void CheckSeated(GymVehicleDriverSeat seat)
    {
        if (probeTick++ % 25 == 0)
        {
            Probe().CheckSeated(seat.Cabin.MarkerWorld("Seat") + vehicle.transform.up * 0.75f);
        }
        GymVehicleCabin cabin = seat.Cabin;
        Transform hips = Hips();
        BodybuilderEnemyVisual.Rig rig = fighter.VehicleBodyAnimator.BodyRig;
        Vector3 hipsLocal = vehicle.transform.InverseTransformPoint(hips.position);
        Bounds body = vehicle.PhysicalBodyBoundsForVerification;
        Vector3 half = body.extents;
        if (Mathf.Abs(hipsLocal.x) > half.x * 0.9f || Mathf.Abs(hipsLocal.z) > half.z * 0.9f)
            outsideCabinFrames++;
        cabin.TryGetMarker("Roof", out Vector3 roof);
        float clearance = roof.y - vehicle.transform.InverseTransformPoint(rig.Head.position).y;
        worstHeadClearance = Mathf.Min(worstHeadClearance, clearance);
        Vector3 wheel = cabin.WheelPivot.position;
        float radius = GymVehicleCabin.AuthoredWheelRadius * cabin.WheelPivot.lossyScale.x;
        float gap = Mathf.Max(
            Mathf.Abs(Vector3.Distance(rig.LeftHand.position, wheel) - radius),
            Mathf.Abs(Vector3.Distance(rig.RightHand.position, wheel) - radius));
        worstHandGap = Mathf.Max(worstHandGap, gap);
    }

    // Seated: under 0.3% of skin samples may sit past the shell, never more
    // than 3 cm (skin rounding at the seams). Moving: no vertex may pass
    // through the shell between two samples beyond a handful of seam hits.
    private static void PenetrationFailures(string phase)
    {
        GymChaosCabinPenetrationProbe p = Probe();
        float share = p.SeatedSamples > 0 ? p.SeatedOutside / (float)p.SeatedSamples : 0f;
        string line = $"{current} {phase} shellParts={p.ShellPartCount} seatedOutside={p.SeatedOutside}/" +
            $"{p.SeatedSamples} depth={p.SeatedDepth:F3} crossings={p.Crossings} {p.WorstCrossing} {p.Histogram()}";
        Debug.Log("GYMCHAOS_VEHICLE_CABIN_PENETRATION " + line);
        results.Add("penetration " + line);
        if (p.ShellPartCount < 3) failures.Add($"{current}: shell probe found {p.ShellPartCount} parts");
        if (p.SeatedSamples == 0) failures.Add($"{current}: no seated skin samples");
        // Contact seams (a hand on the door frame, the seat of the shorts on
        // the sill) touch a few skin samples; a limb or head going through
        // the car moves hundreds of them across the skin in one step.
        float motionShare = p.MotionSamples > 0 && p.SampledVertices > 0
            ? p.Crossings / (float)(p.MotionSamples * p.SampledVertices) : 0f;
        float worstShare = p.SampledVertices > 0 ? p.WorstSampleCrossings / (float)p.SampledVertices : 0f;
        results.Add($"penetrationShare {current} {phase} seated={share:P3} motion={motionShare:P3} worstStep={worstShare:P2}");
        if (share > 0.001f || p.SeatedDepth > 0.07f)
            failures.Add($"{current}: {phase} body outside the car {share:P2} depth {p.SeatedDepth:F3}");
        if (motionShare > 0.003f || worstShare > 0.015f)
            failures.Add($"{current}: {phase} body passed through the car share={motionShare:P3} " +
                $"worstStep={worstShare:P2} ({p.WorstCrossing})");
    }

    private static void TrackHips()
    {
        if (probeTick++ % 2 == 0)
        {
            GymVehicleDriverSeat motionSeat = vehicle.DriverSeat;
            Probe().TrackMotion($"{motionSeat?.State}@{motionSeat?.ExitPhase:F2}");
        }
        Vector3 hips = Hips().position;
        if (lastHips.HasValue)
        {
            float step = Vector3.Distance(hips, lastHips.Value);
            if (step > hipsStepMax)
            {
                GymVehicleDriverSeat seat = vehicle.DriverSeat;
                hipsStepAt = $"{seat?.State}@{seat?.ExitPhase:F3} d={hips - lastHips.Value}";
            }
            hipsStepMax = Mathf.Max(hipsStepMax, step);
        }
        lastHips = hips;
    }

    private static void TickExit(double now)
    {
        if (now - stageAt > 30d) throw new TimeoutException($"{current}: exit never finished");
        GymVehicleDriverSeat seat = vehicle.DriverSeat;
        TrackHips();
        doorMax = Mathf.Max(doorMax, seat.Cabin.DoorOpen01);
        if (seat.State == GymVehicleDriverSeat.SeatState.Exiting && seat.ExitPhase > 0.42f)
            Capture("exit", Side(seat));
        if (seat.State == GymVehicleDriverSeat.SeatState.Exiting)
        {
            foreach (float mark in new[] { 0.15f, 0.3f, 0.6f })
                if (seat.ExitPhase > mark) Capture($"exit{mark:F2}", Close(seat));
        }
        if (seat.State == GymVehicleDriverSeat.SeatState.Empty)
        {
            standingAt = now;
            Enter(Stage.Standing, now);
        }
    }

    private static void TickStanding(double now)
    {
        TrackHips();
        if (now - standingAt < 1.2d) return;
        GymVehicleDriverSeat seat = vehicle.DriverSeat;
        Capture("standing", Side(seat));
        if (fighter.IsSeatedInVehicle) failures.Add($"{current}: still seated after standing");
        if (fighter.VehicleBodyAnimator.CurrentVehiclePose != MixamoScanRetargetAnimator.VehiclePose.None)
            failures.Add($"{current}: car pose still active");
        if (seat.Cabin.DoorOpen01 > 0.001f) failures.Add($"{current}: door left open");
        if (doorMax < 0.95f) failures.Add($"{current}: door opened only {doorMax:F2}");
        if (hipsStepMax > HipsStepLimit) failures.Add($"{current}: hips snapped {hipsStepMax:F3} m at {hipsStepAt}");
        if (outsideCabinFrames > 0) failures.Add($"{current}: hips left the cabin {outsideCabinFrames} frames");
        if (worstHeadClearance < 0f) failures.Add($"{current}: head through the roof {worstHeadClearance:F3}");
        if (seat.HeadClearance < -0.02f) failures.Add($"{current}: crown {seat.HeadClearance:F3} m into the roof");
        if (worstHandGap > 0.12f) failures.Add($"{current}: hands {worstHandGap:F3} m off the rim");
        PenetrationFailures("arrival");
        var agent = fighter.GetComponent<GymVisitorAgent>();
        if (agent == null || agent.State != GymVisitorAgent.VisitorState.ApproachingGymFromVehicle)
            failures.Add($"{current}: agent not walking to the gym ({agent?.State})");
        string line = $"identity={current} seatedFrames={seatedDrivingFrames} doorMax={doorMax:F2} " +
            $"hipsStep={hipsStepMax:F3} headClear={worstHeadClearance:F3} handGap={worstHandGap:F3} " +
            $"sink={seat.SeatSink:F3}";
        results.Add(line);
        Debug.Log("GYMCHAOS_VEHICLE_CABIN_DRIVER " + line);
        if (departureIdentity)
        {
            departureIdentity = false;
            Enter(Stage.DepartureWait, now);
            return;
        }
        NextIdentity(now);
    }

    private static void TickDeparture(double now)
    {
        if (now - stageAt > DepartureTimeout)
            throw new TimeoutException($"{current}: departure did not finish (seat={vehicle.DriverSeat?.State})");
        GymVehicleDriverSeat seat = vehicle.DriverSeat;
        if (seat.State == GymVehicleDriverSeat.SeatState.WalkingIn ||
            seat.State == GymVehicleDriverSeat.SeatState.Boarding)
        {
            if (!sawBoarding)
            {
                lastHips = null;
                Probe().ResetMotion();
            }
            sawBoarding = true;
            TrackHips();
            doorMaxBoarding = Mathf.Max(doorMaxBoarding, seat.Cabin.DoorOpen01);
            if (seat.State == GymVehicleDriverSeat.SeatState.Boarding && seat.ExitPhase < 0.5f)
                Capture("boarding", Side(seat));
            if (seat.State == GymVehicleDriverSeat.SeatState.Boarding && seat.ExitPhase < 0.12f)
                Capture("boarding0.12", Close(seat));
        }
        if (sawBoarding && vehicle.IsDriving && seat.State == GymVehicleDriverSeat.SeatState.Seated)
        {
            sawDrivingOff = true;
            CheckSeated(seat);
        }
        if (vehicle.HasCompletedDeparture)
        {
            if (!sawBoarding) failures.Add($"{current}: never boarded");
            if (!sawDrivingOff) failures.Add($"{current}: car left without its seated driver");
            if (doorMaxBoarding < 0.95f) failures.Add($"{current}: boarding door opened only {doorMaxBoarding:F2}");
            if (hipsStepMax > HipsStepLimit) failures.Add($"{current}: boarding hips snapped {hipsStepMax:F3} m at {hipsStepAt}");
            if (fighter.gameObject.activeSelf) failures.Add($"{current}: driver still visible after the car left");
            PenetrationFailures("departure");
            string line = $"identity={current} boarded=1 doorMax={doorMaxBoarding:F2} hipsStep={hipsStepMax:F3}";
            results.Add("departure " + line);
            Debug.Log("GYMCHAOS_VEHICLE_CABIN_DEPARTURE " + line);
            NextIdentity(now);
        }
    }

    private static (Vector3 eye, Vector3 target) Front(GymVehicleDriverSeat seat)
    {
        Transform car = vehicle.transform;
        Vector3 target = seat.Cabin.MarkerWorld("Seat") + Vector3.up * 0.9f;
        return (car.position + car.forward * 5.2f + Vector3.up * 1.9f, target);
    }

    private static (Vector3 eye, Vector3 target) Side(GymVehicleDriverSeat seat)
    {
        Transform car = vehicle.transform;
        Vector3 hinge = seat.Cabin.MarkerWorld("Hinge");
        Vector3 outward = Vector3.ProjectOnPlane(hinge - car.position, car.forward).normalized;
        outward = Vector3.ProjectOnPlane(outward, Vector3.up).normalized;
        Vector3 target = car.position + outward * 1.2f + Vector3.up * 1.0f;
        return (target + outward * 5.2f + car.forward * 2.4f + Vector3.up * 0.9f, target);
    }

    // Close three-quarter view of the driver door from outside, rear side.
    private static (Vector3 eye, Vector3 target) Close(GymVehicleDriverSeat seat)
    {
        Transform car = vehicle.transform;
        Vector3 hinge = seat.Cabin.MarkerWorld("Hinge");
        Vector3 outward = Vector3.ProjectOnPlane(hinge - car.position, car.forward);
        outward = Vector3.ProjectOnPlane(outward, Vector3.up).normalized;
        Vector3 target = hinge + outward * 0.4f - car.forward * 0.6f + Vector3.up * 0.2f;
        return (target + outward * 3.4f - car.forward * 1.6f + Vector3.up * 0.6f, target);
    }

    private static void Capture(string label, (Vector3 eye, Vector3 target) view)
    {
        if (Application.isBatchMode && SystemInfo.graphicsDeviceType ==
            UnityEngine.Rendering.GraphicsDeviceType.Null) return;
        if (!captured.Add(current + label)) return;
        string directory = Path.Combine(
            Directory.GetParent(Application.dataPath).Parent.FullName, "Logs", "verify", "vehicle-cabin");
        Directory.CreateDirectory(directory);
        GameObject host = new GameObject("Vehicle Cabin Capture Camera");
        Camera camera = host.AddComponent<Camera>();
        camera.fieldOfView = 42f;
        camera.nearClipPlane = 0.05f;
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
        File.WriteAllBytes(Path.Combine(directory, $"{current}-{label}.png"), image.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(image);
        target.Release();
        UnityEngine.Object.DestroyImmediate(target);
        UnityEngine.Object.DestroyImmediate(host);
    }

    private static void Report()
    {
        if (failures.Count == 0)
        {
            Debug.Log("GYMCHAOS_VEHICLE_CABIN_OK " + string.Join(" | ", results));
            Finish(0);
        }
        else
        {
            Debug.LogError("GYMCHAOS_VEHICLE_CABIN_FAIL " + string.Join(" | ", failures) +
                " || " + string.Join(" | ", results));
            Finish(1);
        }
    }

    private static void Finish(int code)
    {
        finished = true;
        resultCode = code;
        if (previousMaxDelta > 0f) Time.maximumDeltaTime = previousMaxDelta;
        GymChaosVerifierExit.Record(resultCode);
        SessionState.EraseBool(RequestedKey);
        EditorApplication.isPlaying = false;
    }
}
#endif
