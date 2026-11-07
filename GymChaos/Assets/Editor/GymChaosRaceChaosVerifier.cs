#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Chaos race gate: the field is the Normal six plus Goku and Davie's bus.
/// Goku stands on the grid through the countdown, takes off at GO and flies
/// the track low, facing the way he travels, never off the road. Davie sits
/// on the front of the bus roof in the looping sitting_idle clip. No two car
/// bodies overlap, drifting or not. With a graphics device it writes
/// Logs/agent/race-chaos-*.png. Logs GYMCHAOS_RACE_CHAOS_OK.
/// </summary>
public static class GymChaosRaceChaosVerifier
{
    private const string RequestedKey = "GymChaos.RaceChaosVerificationRequested";
    private static IEnumerator routine;
    private static readonly Stack<IEnumerator> stack = new Stack<IEnumerator>();
    private static readonly StringBuilder report = new StringBuilder();

    [InitializeOnLoadMethod]
    private static void ResumeAfterReload()
    {
        if (!SessionState.GetBool(RequestedKey, false)) return;
        Hook();
    }

    [MenuItem("Tools/GymChaos/Run Race Chaos Verification")]
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
        GymChaosVerifierHooks.Hook(Tick, Changed);
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

    private static IEnumerator Scenario()
    {
        float started = Time.realtimeSinceStartup;
        PlayerMovement player = null;
        while (player == null || !GymRaceCityCarver.CityReady() || GymRaceDirector.Instance == null)
        {
            if (Time.realtimeSinceStartup - started > 160f)
                throw new VerificationFailure($"scene not ready player={player != null} city={GymRaceCityCarver.CityReady()}");
            player = Object.FindFirstObjectByType<PlayerMovement>();
            yield return null;
        }
        float settle = Time.time + 2f;
        while (Time.time < settle) yield return null;

        GymRaceDirector director = GymRaceDirector.Instance;
        float trafficLimit = Time.time + 90f;
        while (director.BlockReasonForVerification != null)
        {
            Require(Time.time < trafficLimit, "race stayed blocked: " + director.BlockReasonForVerification);
            yield return null;
        }
        Require(director.StartRaceForVerification(player, true, GymRaceDirector.RaceMode.Chaos), "chaos race refused to start");
        Require(director.CurrentMode == GymRaceDirector.RaceMode.Chaos, "mode is not Chaos");
        Time.timeScale = GymChaosVerifierClock.ScenarioTimeScale;

        // ------------------------------------------------------------ the grid
        float limit = Time.time + 60f;
        while (director.CurrentPhase != GymRaceDirector.Phase.Countdown)
        {
            Require(Time.time < limit, $"chaos race never reached the countdown (phase {director.CurrentPhase})");
            Require(director.CurrentPhase != GymRaceDirector.Phase.Idle, "chaos race returned to idle");
            yield return null;
        }
        Require(director.Racers.Count == 8, $"{director.Racers.Count} racers, expected 8");
        GymRaceRacer goku = Find(director, "Goku");
        GymRaceRacer bus = Find(director, "Davie");
        Require(goku != null && bus != null, "no Goku or bus racer");
        EnemyFighter gokuBody = director.Drivers[goku.GridSlot];
        EnemyFighter davie = director.Drivers[bus.GridSlot];
        Require(gokuBody != null && gokuBody.gameObject.activeInHierarchy, "Goku body missing");
        Require(davie != null && davie.gameObject.activeInHierarchy, "Davie missing from the bus");
        Require(!gokuBody.IsGokuFlightActive && gokuBody.AnimationState != MixamoScanRetargetAnimator.MotionState.Flying,
            $"Goku not standing at the countdown ({gokuBody.AnimationState})");
        float gokuGridHeight = gokuBody.transform.position.y - RoadY(goku.Kart);
        Require(Mathf.Abs(gokuGridHeight) < 0.3f, $"Goku stands {gokuGridHeight:F2} m off the road at the grid");

        // Bus body box in its holder frame (Davie is not under the holder).
        Transform busHolder = director.Cars[bus.GridSlot].Holder;
        Bounds busLocal = LocalBounds(busHolder);
        Require(busLocal.size.z > 9f && busLocal.size.z > busLocal.size.x * 2.5f, $"bus body {busLocal.size} is not a bus");
        MixamoScanRetargetAnimator davieAnimator = davie.VehicleBodyAnimator;
        Require(davieAnimator != null && davieAnimator.HasSittingIdleClip, "Davie has no sitting_idle clip");
        CheckDavieSeated(davieAnimator, busHolder, busLocal, "countdown");
        List<Transform> holders = new List<Transform>();
        List<Bounds> bodies = new List<Bounds>();
        for (int i = 0; i < director.Cars.Count; i++)
        {
            Bounds local = LocalBounds(director.Cars[i].Holder);
            if (local.size.sqrMagnitude < 0.1f) continue;
            holders.Add(director.Cars[i].Holder);
            bodies.Add(local);
        }
        Capture(player, gokuBody.transform.position + gokuBody.transform.forward * 4f + Vector3.up * 1.5f, gokuBody.transform.position + Vector3.up, "race-chaos-grid-goku.png");
        Capture(player, busHolder.TransformPoint(new Vector3(-6f, 4.5f, busLocal.max.z + 6f)), davie.VehicleBodyAnimator.BodyRig.Hips.position, "race-chaos-grid-bus.png");
        report.Append($"racers=8 gokuGrid={gokuGridHeight:F2} bus={busLocal.size.z:F1}m ");

        // -------------------------------------------------------------- racing
        while (director.CurrentPhase != GymRaceDirector.Phase.Racing) yield return null;
        float go = Time.time;
        float takeoffBy = go + 1.5f;
        bool flying = false;
        float worstRoad = float.MinValue;
        float worstFacing = 1f;
        float maxLift = 0f;
        float worstOverlap = 0f;
        string worstPair = "-";
        int busDriftSamples = 0;
        int samples = 0;
        float startLoop = davieAnimator.VehicleLoopTimeForVerification;
        bool gokuShot = false;
        bool busShot = false;
        float raceEnd = go + 45f;
        while (Time.time < raceEnd)
        {
            Require(director.CurrentPhase == GymRaceDirector.Phase.Racing || director.CurrentPhase == GymRaceDirector.Phase.Finishing,
                $"race left racing ({director.CurrentPhase})");
            flying |= gokuBody.IsFlying && gokuBody.AnimationState == MixamoScanRetargetAnimator.MotionState.Flying;
            Require(Time.time < takeoffBy || flying, $"Goku did not take off after GO ({gokuBody.AnimationState})");
            if (flying)
            {
                samples++;
                worstRoad = Mathf.Max(worstRoad, GymRaceTrackLayout.SignedDistanceToRoad(goku.Kart.Body.position));
                maxLift = Mathf.Max(maxLift, gokuBody.transform.position.y - RoadY(goku.Kart));
                if (goku.Kart.ForwardSpeed > 4f)
                {
                    worstFacing = Mathf.Min(worstFacing, Vector3.Dot(Flat(gokuBody.transform.forward).normalized, goku.Kart.Heading));
                }
                if (!gokuShot && Time.time > go + 6f)
                {
                    gokuShot = true;
                    Transform g = gokuBody.transform;
                    Capture(player, g.position - g.forward * 5f + g.right * 3f + Vector3.up * 2.5f, g.position + Vector3.up * 0.8f, "race-chaos-goku-flying.png");
                }
            }
            if (bus.Kart.IsDrifting)
            {
                busDriftSamples++;
                if (!busShot)
                {
                    busShot = true;
                    Capture(player, busHolder.TransformPoint(new Vector3(9f, 7f, 6f)), busHolder.TransformPoint(busLocal.center), "race-chaos-bus-drift.png");
                }
            }
            CheckDavieSeated(davieAnimator, busHolder, busLocal, "racing");
            for (int a = 0; a < holders.Count; a++)
            {
                for (int b = a + 1; b < holders.Count; b++)
                {
                    float overlap = Overlap(holders[a], bodies[a], holders[b], bodies[b]);
                    if (overlap > worstOverlap)
                    {
                        worstOverlap = overlap;
                        worstPair = holders[a].name + "/" + holders[b].name;
                    }
                }
            }
            yield return null;
        }
        float loops = (davieAnimator.VehicleLoopTimeForVerification - startLoop) / Mathf.Max(0.01f, davieAnimator.SittingIdleClipLength);
        Require(flying && samples > 100, $"Goku flight samples {samples}");
        Require(worstRoad < 0.3f, $"Goku left the road by {worstRoad:F2} m");
        Require(maxLift < 0.6f, $"Goku flew {maxLift:F2} m above the road (root)");
        Require(worstFacing > 0.85f, $"Goku faced away from his travel ({worstFacing:F2})");
        Require(busDriftSamples > 0, "bus never drifted");
        Require(loops > 1f, $"sitting_idle played {loops:F2} loops in the race");
        Require(worstOverlap < 0.3f, $"car bodies overlapped {worstOverlap:F2} m ({worstPair})");
        report.Append($"road={worstRoad:F2} lift={maxLift:F2} facing={worstFacing:F2} busDrift={busDriftSamples} loops={loops:F1} overlap={worstOverlap:F2} ");

        // ------------------------------------------------------------ teardown
        director.ForceResultsForVerification();
        limit = Time.time + 60f;
        while (director.CurrentPhase != GymRaceDirector.Phase.Idle)
        {
            Require(Time.time < limit, $"teardown stuck in {director.CurrentPhase}");
            yield return null;
        }
        Time.timeScale = 1f;
        yield return null;
        Require(GymRaceKart.All.Count == 0, $"{GymRaceKart.All.Count} karts left");
        Require(gokuBody == null && davie == null, "Goku or Davie left behind");
    }

    private static void CheckDavieSeated(MixamoScanRetargetAnimator animator, Transform busHolder, Bounds busLocal, string when)
    {
        Require(animator.CurrentVehiclePose == MixamoScanRetargetAnimator.VehiclePose.SittingIdle,
            $"{when}: Davie pose {animator.CurrentVehiclePose}, expected SittingIdle");
        Vector3 hips = busHolder.InverseTransformPoint(animator.BodyRig.Hips.position);
        Require(hips.y > busLocal.max.y - 0.1f && hips.y < busLocal.max.y + 0.6f,
            $"{when}: Davie hips {hips.y:F2} not on the roof (top {busLocal.max.y:F2})");
        Require(hips.z > busLocal.center.z + busLocal.extents.z * 0.4f && hips.z < busLocal.max.z,
            $"{when}: Davie hips z={hips.z:F2} not on the front of the roof ({busLocal.center.z:F2}..{busLocal.max.z:F2})");
        Require(Mathf.Abs(hips.x - busLocal.center.x) < busLocal.extents.x * 0.6f, $"{when}: Davie off the roof side ({hips.x:F2})");
    }

    private static GymRaceRacer Find(GymRaceDirector director, string name)
    {
        foreach (GymRaceRacer racer in director.Racers)
        {
            if (racer.DisplayName == name) return racer;
        }
        return null;
    }

    private static float RoadY(GymRaceKart kart) => kart.Body.position.y - kart.Radius;

    private static Bounds LocalBounds(Transform holder)
    {
        Bounds bounds = new Bounds();
        bool first = true;
        foreach (Renderer renderer in holder.GetComponentsInChildren<Renderer>())
        {
            if (!renderer.enabled || renderer is ParticleSystemRenderer) continue;
            Bounds world = renderer.bounds;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = world.center + Vector3.Scale(world.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                Vector3 local = holder.InverseTransformPoint(corner);
                if (first) bounds = new Bounds(local, Vector3.zero);
                else bounds.Encapsulate(local);
                first = false;
            }
        }
        return bounds;
    }

    /// <summary>Ground-plane overlap depth of two body boxes held in their holders' frames, 0 when apart.</summary>
    private static float Overlap(Transform a, Bounds la, Transform b, Bounds lb)
    {
        // World AABB corners blow a rotated mesh up: shrink to 85 % of the box.
        Vector3 ca = a.TransformPoint(la.center), cb = b.TransformPoint(lb.center);
        Vector3[] axes = { Flat(a.right).normalized, Flat(a.forward).normalized, Flat(b.right).normalized, Flat(b.forward).normalized };
        float depth = float.MaxValue;
        foreach (Vector3 axis in axes)
        {
            float ea = (Mathf.Abs(Vector3.Dot(Flat(a.right).normalized, axis)) * la.extents.x + Mathf.Abs(Vector3.Dot(Flat(a.forward).normalized, axis)) * la.extents.z) * 0.85f;
            float eb = (Mathf.Abs(Vector3.Dot(Flat(b.right).normalized, axis)) * lb.extents.x + Mathf.Abs(Vector3.Dot(Flat(b.forward).normalized, axis)) * lb.extents.z) * 0.85f;
            float overlap = ea + eb - Mathf.Abs(Vector3.Dot(Flat(cb - ca), axis));
            if (overlap <= 0f) return 0f;
            depth = Mathf.Min(depth, overlap);
        }
        return depth;
    }

    private static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

    // With a graphics device: a still from a free camera into Logs/agent.
    private static void Capture(PlayerMovement player, Vector3 from, Vector3 at, string file)
    {
        if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null || player == null) return;
        Camera source = player.playerCamera;
        GameObject go = new GameObject("Race Chaos Capture Camera");
        Camera camera = go.AddComponent<Camera>();
        camera.CopyFrom(source);
        camera.transform.SetPositionAndRotation(from, Quaternion.LookRotation(at - from, Vector3.up));
        camera.fieldOfView = 55f;
        RenderTexture target = new RenderTexture(1600, 900, 24);
        camera.targetTexture = target;
        camera.Render();
        RenderTexture.active = target;
        Texture2D picture = new Texture2D(1600, 900, TextureFormat.RGB24, false);
        picture.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0);
        RenderTexture.active = null;
        string directory = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "..", "Logs", "agent");
        System.IO.Directory.CreateDirectory(directory);
        System.IO.File.WriteAllBytes(System.IO.Path.Combine(directory, file), picture.EncodeToPNG());
        Object.Destroy(picture);
        camera.targetTexture = null;
        Object.Destroy(target);
        Object.Destroy(go);
    }

    private static void Finish(int code, string detail)
    {
        if (code == 0) Debug.Log("GYMCHAOS_RACE_CHAOS_OK " + detail);
        else Debug.LogError("GYMCHAOS_RACE_CHAOS_FAILED " + detail);
        SessionState.EraseBool(RequestedKey);
        EditorApplication.update -= Tick;
        routine = null;
        Time.timeScale = 1f;
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
