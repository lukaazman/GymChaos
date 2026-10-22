#if UNITY_EDITOR
using System.Collections;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Kart handling gate on a flat test pad and on the real track: acceleration,
/// braking, drift slide and mini-turbo, boost meter and boost speed with the
/// chase camera pulling back, kart-to-kart collisions without tunnelling,
/// respawn from off the track, and AI laps over both branches (spiral, ramp,
/// sunken shortcut) without losing the road. Logs GYMCHAOS_RACE_KART_OK.
/// </summary>
public static class GymChaosRaceKartVerifier
{
    private const string RequestedKey = "GymChaos.RaceKartVerificationRequested";
    private static IEnumerator routine;
    private static readonly System.Collections.Generic.Stack<IEnumerator> stack = new System.Collections.Generic.Stack<IEnumerator>();
    private static readonly StringBuilder report = new StringBuilder();
    private static GymRaceTrack track;
    private static GameObject pad;
    private static GymRaceChaseCamera chase;

    [InitializeOnLoadMethod]
    private static void ResumeAfterReload()
    {
        if (!SessionState.GetBool(RequestedKey, false)) return;
        Hook();
    }

    [MenuItem("Tools/GymChaos/Run Race Kart Verification")]
    public static void Run()
    {
        GymChaosVerifierSave.Neutralize();
        SessionState.SetBool(RequestedKey, true);
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        Hook();
        EditorApplication.isPlaying = true;
    }

    private static void Hook()
    {
        routine = null;
        report.Length = 0;
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
        EditorApplication.playModeStateChanged -= Changed;
        EditorApplication.playModeStateChanged += Changed;
    }

    private static void Changed(PlayModeStateChange change)
    {
        if (change == PlayModeStateChange.EnteredPlayMode)
        {
            GymChaosVerifierClock.BeginFixedStep();
            stack.Clear();
            routine = Scenario();
        }
        if (change != PlayModeStateChange.EnteredEditMode) return;
        EditorApplication.update -= Tick;
        GymChaosVerifierClock.EndFixedStep();
        GymChaosVerifierSave.Restore();
        if (Application.isBatchMode && SessionState.GetBool(RequestedKey, false)) EditorApplication.Exit(1);
    }

    private sealed class VerificationFailure : System.Exception
    {
        public VerificationFailure(string message) : base(message) { }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new VerificationFailure(message);
    }

    private static void Tick()
    {
        if (!EditorApplication.isPlaying || routine == null) return;
        try
        {
            // Nested waits run as a stack of iterators.
            if (stack.Count == 0) stack.Push(routine);
            while (stack.Count > 0)
            {
                IEnumerator top = stack.Peek();
                if (!top.MoveNext())
                {
                    stack.Pop();
                    continue;
                }
                if (top.Current is IEnumerator nested)
                {
                    stack.Push(nested);
                    continue;
                }
                return;
            }
            Finish(0, report.ToString());
        }
        catch (VerificationFailure failure)
        {
            Finish(1, failure.Message + " | " + report);
        }
        catch (System.Exception exception)
        {
            Finish(1, "exception " + exception + " | " + report);
        }
    }

    private static IEnumerator Wait(float seconds)
    {
        float until = Time.time + seconds;
        while (Time.time < until) yield return null;
    }

    private static IEnumerator Scenario()
    {
        float started = Time.realtimeSinceStartup;
        while (!GymRaceCityCarver.CityReady())
        {
            if (Time.realtimeSinceStartup - started > 150f) throw new VerificationFailure("city never loaded");
            yield return null;
        }
        yield return Wait(1f);

        // Flat physics pad far below the map for the open-space tests.
        pad = GameObject.CreatePrimitive(PrimitiveType.Cube);
        pad.name = "Race Kart Test Pad";
        pad.layer = GymRaceTrackBuilder.RaceTrackLayer;
        pad.transform.position = new Vector3(0f, -200.5f, 0f);
        pad.transform.localScale = new Vector3(400f, 1f, 400f);
        Vector3 padTop = new Vector3(0f, -200f, -150f);

        Camera camera = Camera.main;
        Require(camera != null, "no main camera");
        chase = camera.gameObject.AddComponent<GymRaceChaseCamera>();

        // ---------------------------------------------------------- accel/brake
        GymRaceKart kart = SpawnKart("Test Kart A", padTop, Vector3.forward);
        chase.Begin(camera, kart);
        yield return Wait(0.5f);
        float top = kart.KartStats.TopSpeed;
        float t0 = Time.time;
        float reach90 = -1f;
        while (Time.time - t0 < 9f)
        {
            kart.Input = new GymRaceDriveInput { Throttle = 1f };
            if (reach90 < 0f && kart.ForwardSpeed >= top * 0.9f) reach90 = Time.time - t0;
            yield return null;
        }
        Require(reach90 > 2.2f && reach90 < 5.5f, $"0-90% top speed took {reach90:F2}s");
        float cruiseDistance = chase.CurrentDistance;
        float cruiseFov = chase.CurrentFieldOfView;
        Vector3 brakeStart = kart.Body.position;
        while (kart.ForwardSpeed > 0.3f)
        {
            kart.Input = new GymRaceDriveInput { Brake = 1f };
            if (Time.time - t0 > 20f) break;
            yield return null;
        }
        float brakeDistance = Vector3.Distance(brakeStart, kart.Body.position);
        Require(brakeDistance < 26f, $"braking from top speed took {brakeDistance:F1} m");
        report.Append($"accel90={reach90:F2}s brake={brakeDistance:F1}m ");

        // Reverse.
        t0 = Time.time;
        while (Time.time - t0 < 1.5f)
        {
            kart.Input = new GymRaceDriveInput { Brake = 1f };
            yield return null;
        }
        Require(kart.ForwardSpeed < -3f, $"no reverse ({kart.ForwardSpeed:F1})");

        // ---------------------------------------------------------------- drift
        kart.Respawn(padTop, Vector3.forward, Vector3.up);
        t0 = Time.time;
        while (Time.time - t0 < 3f)
        {
            kart.Input = new GymRaceDriveInput { Throttle = 1f };
            yield return null;
        }
        float maxAngle = 0f;
        int maxTier = 0;
        int turbos = 0;
        kart.MiniTurboStarted += tier => turbos = Mathf.Max(turbos, tier);
        t0 = Time.time;
        while (Time.time - t0 < 3.4f)
        {
            kart.Input = new GymRaceDriveInput { Throttle = 1f, Steer = 0.8f, DriftHeld = true };
            maxAngle = Mathf.Max(maxAngle, Mathf.Abs(kart.VisualDriftAngle));
            maxTier = Mathf.Max(maxTier, kart.DriftTier);
            yield return null;
        }
        Require(kart.IsDrifting, "kart did not drift");
        float headingVsVelocity = Vector3.Angle(kart.Heading, Vector3.ProjectOnPlane(kart.Body.linearVelocity, Vector3.up));
        kart.Input = new GymRaceDriveInput { Throttle = 1f };
        yield return Wait(0.15f);
        float turboSpeed = 0f;
        t0 = Time.time;
        while (Time.time - t0 < 1f)
        {
            kart.Input = new GymRaceDriveInput { Throttle = 1f };
            turboSpeed = Mathf.Max(turboSpeed, kart.ForwardSpeed);
            yield return null;
        }
        Require(maxAngle >= 15f, $"drift body angle only {maxAngle:F1}");
        Require(maxTier >= 2 && turbos >= 2, $"drift tier {maxTier} turbo {turbos}");
        Require(headingVsVelocity > 4f, $"drift has no slide (heading vs velocity {headingVsVelocity:F1})");
        Require(turboSpeed > top * 1.02f, $"mini-turbo speed {turboSpeed:F1} <= top {top:F1}");
        report.Append($"driftAngle={maxAngle:F1} slide={headingVsVelocity:F1} tier={maxTier} turbo={turboSpeed:F1} ");

        // ---------------------------------------------------------------- boost
        kart.Respawn(padTop, Vector3.forward, Vector3.up);
        kart.SetBoostMeter(0f);
        kart.MeterFilling = true;
        Time.timeScale = 3f;
        t0 = Time.time;
        while (kart.BoostMeter < 1f && Time.time - t0 < 20f)
        {
            kart.Input = new GymRaceDriveInput { Throttle = 0.6f, Steer = 0.3f };
            yield return null;
        }
        float fillTime = Time.time - t0;
        Time.timeScale = 1f;
        Require(kart.BoostMeter >= 1f && fillTime > 9f && fillTime < 14f, $"boost meter fill {fillTime:F1}s meter={kart.BoostMeter:F2}");
        kart.Respawn(padTop, Vector3.forward, Vector3.up);
        kart.SetBoostMeter(1f);
        t0 = Time.time;
        while (Time.time - t0 < 6f)
        {
            kart.Input = new GymRaceDriveInput { Throttle = 1f };
            yield return null;
        }
        float steadyDistance = chase.CurrentDistance;
        float steadyFov = chase.CurrentFieldOfView;
        float boostSpeed = 0f;
        t0 = Time.time;
        while (Time.time - t0 < 2.6f)
        {
            kart.Input = new GymRaceDriveInput { Throttle = 1f, BoostPressed = Time.time - t0 < 0.1f };
            boostSpeed = Mathf.Max(boostSpeed, kart.ForwardSpeed);
            yield return null;
        }
        float boostDistance = chase.CurrentDistance;
        float boostFov = chase.CurrentFieldOfView;
        Require(boostSpeed >= top * 1.35f, $"boost speed {boostSpeed:F1} < 1.35x {top:F1}");
        Require(boostDistance > steadyDistance + 1f && boostFov > steadyFov + 4f,
            $"camera did not pull back for boost dist {steadyDistance:F2}->{boostDistance:F2} fov {steadyFov:F1}->{boostFov:F1}");
        Require(steadyDistance > cruiseDistance - 0.5f && steadyDistance > GymRaceChaseCamera.BaseDistance + 1.5f,
            $"camera not pulled back at speed ({steadyDistance:F2})");
        report.Append($"fill={fillTime:F1}s boost={boostSpeed:F1} cam={steadyDistance:F1}->{boostDistance:F1} fov={steadyFov:F0}->{boostFov:F0} ");
        Object.Destroy(kart.gameObject);
        yield return null;

        // ------------------------------------------------------------ collision
        GymRaceKart left = SpawnKart("Test Kart L", padTop + new Vector3(-0.6f, 0f, 0f), Vector3.forward);
        GymRaceKart right = SpawnKart("Test Kart R", padTop + new Vector3(0.6f, 0f, 60f), Vector3.back);
        chase.SetKart(left);
        float minGap = float.MaxValue;
        // Under lane load one frame can cover several fixed steps; sample the
        // head-on contact at a bounded step so the gap is the physics result.
        float savedMaxDelta = Time.maximumDeltaTime;
        Time.maximumDeltaTime = 1f / 30f;
        t0 = Time.time;
        while (Time.time - t0 < 4.5f)
        {
            left.Input = new GymRaceDriveInput { Throttle = 1f };
            right.Input = new GymRaceDriveInput { Throttle = 1f };
            minGap = Mathf.Min(minGap, Vector3.Distance(left.Body.position, right.Body.position));
            yield return null;
        }
        Time.maximumDeltaTime = savedMaxDelta;
        float sum = left.Radius + right.Radius;
        Require(left.KartHits > 0 && right.KartHits > 0, "karts never collided");
        Require(minGap > sum * 0.75f, $"karts interpenetrated (gap {minGap:F2} < {sum * 0.75f:F2})");
        Require(Vector3.Dot(left.Body.position - right.Body.position, Vector3.forward) < 0f || Mathf.Abs(left.Body.position.x - right.Body.position.x) > sum * 0.9f,
            "karts tunnelled through each other");
        report.Append($"bumpGap={minGap:F2} ");
        Object.Destroy(left.gameObject);
        Object.Destroy(right.gameObject);
        Object.Destroy(pad);
        yield return null;

        // ------------------------------------------------------ real track laps
        GymRaceCorridorClearance.Clear();
        track = GymRaceTrackBuilder.Build(null);
        GymRaceCityCarver.CarveNow();
        yield return null;
        Physics.SyncTransforms();

        float outerLap = 0f;
        float shortcutLap = 0f;
        for (int run = 0; run < 2; run++)
        {
            Pose slot = GymRaceTrackLayout.GridSlot(0);
            GymRaceKart lapKart = SpawnKart(run == 0 ? "Lap Kart Outer" : "Lap Kart Shortcut", slot.position, Vector3.forward);
            GymRaceRacer racer = lapKart.gameObject.AddComponent<GymRaceRacer>();
            GymRaceAIDriver ai = lapKart.gameObject.AddComponent<GymRaceAIDriver>();
            ai.ShortcutChoice = run;
            ai.AllowBoost = false;
            chase.SetKart(lapKart);
            yield return null;
            racer.ResetProgress(Time.time);
            Time.timeScale = 2f;
            int frames = 0;
            int grounded = 0;
            int respawns = 0;
            bool usedShortcut = false;
            float raceStart = Time.time;
            while (racer.Crossings < 2 && Time.time - raceStart < 120f)
            {
                lapKart.Input = ai.Think(Time.time);
                racer.Tick(Time.time, Time.deltaTime, true);
                usedShortcut |= racer.OnShortcut;
                if (racer.NeedsRespawn)
                {
                    respawns++;
                    report.Append($"respawn{run}@{lapKart.Body.position} ");
                    racer.RespawnAtSafePoint();
                }
                frames++;
                if (lapKart.Grounded) grounded++;
                yield return null;
            }
            Time.timeScale = 1f;
            Require(racer.Crossings >= 2, $"run {run} did not finish a lap (crossings={racer.Crossings} s={racer.MainDistance:F0} pos={lapKart.Body.position})");
            Require(racer.LapTimes.Count == 1, $"run {run} lap times {racer.LapTimes.Count}");
            float groundedRatio = grounded / (float)Mathf.Max(frames, 1);
            Require(respawns == 0, $"run {run} needed {respawns} respawns");
            Require(groundedRatio > 0.93f, $"run {run} grounded only {groundedRatio:P0}");
            Require(usedShortcut == (run == 1), $"run {run} shortcut use {usedShortcut}");
            if (run == 0) outerLap = racer.LapTimes[0];
            else shortcutLap = racer.LapTimes[0];
            report.Append($"lap{(run == 0 ? "Outer" : "Short")}={racer.LapTimes[0]:F1}s grounded={groundedRatio:P0} walls={lapKart.WallHits} ");

            if (run == 1)
            {
                // Respawn: throw the kart off the track.
                lapKart.Respawn(lapKart.Body.position + new Vector3(30f, 0f, 30f), Vector3.forward, Vector3.up);
                yield return Wait(0.3f);
                racer.Tick(Time.time, Time.deltaTime, true);
                Require(racer.NeedsRespawn, "off-track kart not flagged for respawn");
                racer.RespawnAtSafePoint();
                yield return Wait(0.8f);
                Require(GymRaceTrackLayout.SignedDistanceToRoad(lapKart.Body.position) < 0f && lapKart.Grounded,
                    $"respawn did not land on the road ({lapKart.Body.position})");
            }
            Object.Destroy(lapKart.gameObject);
            yield return null;
        }
        // ------------------------------------------- outer road after the fork
        // A kart that drifts toward the cut right after the fork, then keeps
        // right on the outer road, must stay on the main lap (no respawn).
        {
            GymRaceTrackPath main = GymRaceTrackLayout.Main;
            GymRaceTrackPath cut = GymRaceTrackLayout.Shortcut;
            float fork = GymRaceTrackLayout.ShortcutForkDistance;
            float span = main.WrapDistance(GymRaceTrackLayout.ShortcutMergeDistance - fork);
            GymRaceKart probe = SpawnKart("Branch Probe", PathPoint(main, fork - 20f), Vector3.forward);
            probe.Body.isKinematic = true;
            GymRaceRacer probeRacer = probe.gameObject.AddComponent<GymRaceRacer>();
            yield return null;
            probeRacer.ResetProgress(Time.time);
            bool tagged = false;
            int probeRespawns = 0;
            for (float d = -20f; d <= span; d += 0.5f)
            {
                main.Sample(main.WrapDistance(fork + d), out Vector3 mainPoint, out _, out _, out _);
                Vector3 position = mainPoint;
                if (d > 4f && d < 24f)
                {
                    cut.Sample(cut.ClosestDistance(mainPoint), out Vector3 cutPoint, out _, out _, out _);
                    position = Vector3.Lerp(mainPoint, cutPoint, 0.65f);
                }
                probe.Body.position = position + Vector3.up * 0.6f;
                probeRacer.Tick(Time.time, 0.02f, true);
                tagged |= probeRacer.OnShortcut;
                if (probeRacer.NeedsRespawn) probeRespawns++;
            }
            Require(probeRespawns == 0, $"outer road after the fork flagged {probeRespawns} respawns");
            Require(!probeRacer.OnShortcut, "outer road kart still tracked on the shortcut");
            report.Append($"outerAfterFork=ok tagged={(tagged ? 1 : 0)} ");
            Object.Destroy(probe.gameObject);
            yield return null;
        }

        float ratio = shortcutLap / Mathf.Max(outerLap, 0.01f);
        Require(ratio > 0.9f && ratio < 1.1f, $"shortcut lap {shortcutLap:F1}s vs outer {outerLap:F1}s ratio {ratio:F2}");
        report.Append($"branchRatio={ratio:F2}");
    }

    private static Vector3 PathPoint(GymRaceTrackPath path, float distance)
    {
        path.Sample(path.WrapDistance(distance), out Vector3 point, out _, out _, out _);
        return point;
    }

    private static GymRaceKart SpawnKart(string name, Vector3 roadPoint, Vector3 forward)
    {
        GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
        visual.name = name + " Visual";
        Object.Destroy(visual.GetComponent<Collider>());
        visual.transform.localScale = new Vector3(1.8f, 1.1f, 3.6f);
        GameObject holder = new GameObject(name + " Visual Root");
        visual.transform.SetParent(holder.transform, false);
        visual.transform.localPosition = new Vector3(0f, 0.55f, 0f);
        GymRaceKart kart = GymRaceKart.Create(name, roadPoint, forward, GymRaceKart.Stats.Default, holder.transform);
        kart.MeterFilling = false;
        return kart;
    }

    private static void Finish(int code, string detail)
    {
        if (code == 0) Debug.Log("GYMCHAOS_RACE_KART_OK " + detail);
        else Debug.LogError("GYMCHAOS_RACE_KART_FAILED " + detail);
        SessionState.EraseBool(RequestedKey);
        EditorApplication.update -= Tick;
        routine = null;
        Time.timeScale = 1f;
        if (chase != null) chase.End();
        GymRaceCityCarver.Restore();
        GymRaceCorridorClearance.Restore();
        GymChaosVerifierClock.EndFixedStep();
        GymChaosVerifierSave.Restore();
        if (Application.isBatchMode)
        {
            GymChaosVerifierExit.Record(code);
            EditorApplication.Exit(code);
        }
        else
        {
            EditorApplication.isPlaying = false;
        }
    }
}
#endif
