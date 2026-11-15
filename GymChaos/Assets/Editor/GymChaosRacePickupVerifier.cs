#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Chaos race pickups gate: tubs stand on the road and only go live at GO, a
/// kart that drives through one draws an item and the tub comes back, the
/// pre-workout boosts, creatine soaks up exactly one hit, a hit spins a kart
/// out and it drives on afterwards, a shake spill lands behind its owner and
/// spins the kart that reaches it, an energy can stays on the road and hits
/// the kart ahead, and nothing is left behind after the race.
/// Logs GYMCHAOS_RACE_PICKUP_OK.
/// </summary>
public static class GymChaosRacePickupVerifier
{
    private const string RequestedKey = "GymChaos.RacePickupVerificationRequested";
    private static IEnumerator routine;
    private static readonly Stack<IEnumerator> stack = new Stack<IEnumerator>();
    private static readonly StringBuilder report = new StringBuilder();

    [InitializeOnLoadMethod]
    private static void ResumeAfterReload()
    {
        if (!SessionState.GetBool(RequestedKey, false)) return;
        Hook();
    }

    [MenuItem("Tools/GymChaos/Run Race Pickup Verification")]
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
        float limit = Time.time + 90f;
        while (director.BlockReasonForVerification != null)
        {
            Require(Time.time < limit, "race stayed blocked: " + director.BlockReasonForVerification);
            yield return null;
        }
        Require(director.StartRaceForVerification(player, true, GymRaceDirector.RaceMode.Chaos), "chaos race refused to start");

        // ------------------------------------------------------------ the grid
        limit = Time.time + 60f;
        while (director.CurrentPhase != GymRaceDirector.Phase.Countdown)
        {
            Require(Time.time < limit, $"chaos race never reached the countdown (phase {director.CurrentPhase})");
            Require(director.CurrentPhase != GymRaceDirector.Phase.Idle, "chaos race returned to idle");
            yield return null;
        }
        GymRacePickups pickups = director.Pickups;
        Require(pickups != null, "the chaos race has no pickups");
        Require(pickups.BoxCount >= 8, $"{pickups.BoxCount} pickup tubs on the lap");
        float worstBox = float.MinValue;
        for (int i = 0; i < pickups.BoxCount; i++)
        {
            worstBox = Mathf.Max(worstBox, GymRaceTrackLayout.SignedDistanceToRoad(pickups.BoxPosition(i)));
            Require(pickups.BoxActive(i), $"tub {i} is not out on the grid");
        }
        Require(worstBox < -0.7f, $"a pickup tub sits {worstBox:F2} m from the road edge");
        Require(!pickups.Running, "pickups are live before GO");
        report.Append($"tubs={pickups.BoxCount} edge={worstBox:F2} ");

        while (director.CurrentPhase != GymRaceDirector.Phase.Racing) yield return null;
        Require(pickups.Running, "pickups are not live after GO");
        GymRaceRacer me = Find(director, "You");
        GymRaceRacer zyzz = Find(director, "Zyzz");
        GymRaceRacer arnold = Find(director, "Arnold");
        Require(me != null && zyzz != null && arnold != null, "a named racer is missing");

        // ------------------------------------------- a kart collects, the tub returns
        int taken = -1;
        limit = Time.time + 45f;
        while (taken < 0)
        {
            Require(Time.time < limit, "no kart drove through a tub in 45 s");
            for (int i = 0; i < pickups.BoxCount && taken < 0; i++)
            {
                if (!pickups.BoxActive(i)) taken = i;
            }
            yield return null;
        }
        Require(pickups.Collected > 0, "a tub was taken but nobody drew an item");
        // The rest of the pack takes it again the moment it returns: give the field time to pass.
        limit = Time.time + GymRacePickups.BoxRespawnSeconds + 25f;
        while (!pickups.BoxActive(taken))
        {
            Require(Time.time < limit, $"tub {taken} never came back");
            yield return null;
        }
        report.Append($"collected={pickups.Collected} ");

        // The rest hands items out by name: the drivers stop using their own.
        pickups.AiUsesItems = false;
        pickups.ResetField();
        Require(pickups.SpillCount == 0 && pickups.CanCount == 0, "the field did not clear");

        // -------------------------------------------------------- pre-workout
        pickups.Give(me, GymRaceItem.PreWorkout);
        Require(pickups.Display(me).Contains("PRE-WORKOUT"), $"item slot reads '{pickups.Display(me)}'");
        yield return null;
        yield return null;
        Require(director.Hud.ItemLabelForVerification == pickups.Display(me),
            $"the HUD item slot reads '{director.Hud.ItemLabelForVerification}'");
        Require(pickups.Use(me) && me.Kart.IsBoosting && me.Item == GymRaceItem.None, "the pre-workout did not boost");
        Require(!pickups.Use(me), "an empty item slot was used");

        // ------------------------------------------ creatine blocks one hit, then a spin
        yield return AtSpeed(zyzz, 15f);
        pickups.Give(zyzz, GymRaceItem.Creatine);
        Require(pickups.Use(zyzz) && pickups.IsShielded(zyzz), "creatine did not shield");
        Require(!pickups.Hit(zyzz) && zyzz.Kart.SpinTime <= 0f, "creatine did not block the hit");
        Require(!pickups.IsShielded(zyzz), "creatine survived the hit it blocked");
        float before = zyzz.Kart.ForwardSpeed;
        int landed = pickups.HitsLanded;
        Require(pickups.Hit(zyzz) && zyzz.Kart.SpinTime > 0f && pickups.HitsLanded == landed + 1, "the second hit did not spin the kart");
        float slowest = before;
        limit = Time.time + 4f;
        while (zyzz.Kart.SpinTime > 0f)
        {
            Require(Time.time < limit, "the spin never ended");
            slowest = Mathf.Min(slowest, zyzz.Kart.ForwardSpeed);
            yield return null;
        }
        Require(slowest < before * 0.5f, $"a spun kart only slowed from {before:F1} to {slowest:F1} m/s");
        yield return AtSpeed(zyzz, 12f);
        report.Append($"spin={before:F0}->{slowest:F0} ");

        // --------------------------------------------------------- shake spill
        yield return AtSpeed(arnold, 12f);
        pickups.Give(arnold, GymRaceItem.ShakeSpill);
        Require(pickups.Use(arnold) && pickups.SpillCount == 1, "the shake spill was not dropped");
        Vector3 spill = pickups.SpillPosition(0);
        float behind = Vector3.Dot(spill - arnold.Kart.Body.position, arnold.Kart.Heading);
        Require(behind < -1f, $"the spill landed {behind:F1} m along the kart's nose");
        Require(GymRaceTrackLayout.SignedDistanceToRoad(spill) < 0f, "the spill landed off the road");
        pickups.ResetField();
        // One straight in front of a kart: it has to spin and take the spill with it.
        // The kart needs the road to itself, or a rival ahead takes the spill first.
        GymRaceRacer victim = null;
        limit = Time.time + 60f;
        while (victim == null)
        {
            Require(Time.time < limit, "no kart had a clear road for the spill in 60 s");
            foreach (GymRaceRacer racer in director.Racers)
            {
                // On a straight: a kart sliding through a corner crosses the lane in a few metres.
                if (!ClearRoad(racer) || racer.Kart.IsDrifting ||
                    GymRaceAIDriver.MaxCurvature(GymRaceTrackLayout.Main, racer.BranchDistance - 10f, 50f) > 1f / 70f) continue;
                bool alone = true;
                foreach (GymRaceRacer other in director.Racers)
                {
                    float gap = other.TotalProgress - racer.TotalProgress;
                    alone &= other == racer || gap < -3f || gap > 40f;
                }
                if (!alone) continue;
                victim = racer;
                break;
            }
            yield return null;
        }
        victim.ShieldUntil = 0f;
        landed = pickups.HitsLanded;
        pickups.DropSpill(victim.OnShortcut, victim.BranchDistance + 8f, victim.Lateral, null);
        Vector3 trap = pickups.SpillPosition(0);
        float trapS = GymRaceTrackLayout.Main.ClosestDistance(trap, victim.BranchDistance, 30f, out float trapLateral, out float trapHeight);
        string drop = $"drop s={victim.BranchDistance:F1} lat={victim.Lateral:F2} v={victim.Kart.ForwardSpeed:F0} trap s={trapS:F1} lat={trapLateral:F2} h={trapHeight:F2}";
        string pass = "-";
        float closest = float.MaxValue;
        limit = Time.time + 4f;
        while (victim.Kart.SpinTime <= 0f)
        {
            float distance = Vector3.Distance(victim.Kart.transform.position, trap);
            if (distance < closest)
            {
                closest = distance;
                pass = $"s={victim.BranchDistance:F1} lat={victim.Lateral:F2} y={victim.Kart.transform.position.y - trap.y:F2}";
            }
            Require(Time.time < limit,
                $"{victim.DisplayName} drove past a spill without spinning (closest {closest:F2} m at {pass}; {drop}; spills left {pickups.SpillCount}, hits {pickups.HitsLanded - landed})");
            yield return null;
        }
        Require(pickups.SpillCount == 0, "the spill stayed after it was hit");
        report.Append($"spillBehind={behind:F1} ");

        // ---------------------------------------------------------- energy can
        GymRaceRacer shooter = null;
        GymRaceRacer target = null;
        limit = Time.time + 60f;
        while (shooter == null)
        {
            Require(Time.time < limit, "no two karts lined up for a can in 60 s");
            foreach (GymRaceRacer a in director.Racers)
            {
                // The can goes for the nearest kart ahead: that one has to be in range.
                GymRaceRacer next = null;
                float nextGap = float.MaxValue;
                foreach (GymRaceRacer b in director.Racers)
                {
                    float gap = b.TotalProgress - a.TotalProgress;
                    if (a == b || b.Finished || gap <= 0f || gap >= nextGap) continue;
                    next = b;
                    nextGap = gap;
                }
                if (next == null || nextGap < 12f || nextGap > 35f || !ClearRoad(a) || !ClearRoad(next)) continue;
                shooter = a;
                target = next;
                break;
            }
            yield return null;
        }
        target.ShieldUntil = 0f;
        landed = pickups.HitsLanded;
        pickups.Give(shooter, GymRaceItem.EnergyCan);
        Require(pickups.Use(shooter) && pickups.CanCount == 1, "the energy can was not thrown");
        float worstRoad = float.MinValue;
        limit = Time.time + GymRacePickups.CanSeconds + 2f;
        while (pickups.CanCount > 0)
        {
            Require(Time.time < limit, "the energy can never ended");
            worstRoad = Mathf.Max(worstRoad, GymRaceTrackLayout.SignedDistanceToRoad(pickups.CanPosition(0)));
            yield return null;
        }
        Require(worstRoad < 0.3f, $"the can left the road by {worstRoad:F2} m");
        Require(pickups.HitsLanded > landed,
            $"the can from {shooter.DisplayName} missed {target.DisplayName} and everyone else ahead");
        report.Append($"can={shooter.DisplayName}>{target.DisplayName} road={worstRoad:F2} ");

        // ------------------------------------------------------------ teardown
        director.ForceResultsForVerification();
        limit = Time.time + 60f;
        while (director.CurrentPhase != GymRaceDirector.Phase.Idle)
        {
            Require(Time.time < limit, $"teardown stuck in {director.CurrentPhase}");
            yield return null;
        }
        yield return null;
        Require(director.Pickups == null && pickups == null, "the pickups outlived the race");
    }

    private static IEnumerator AtSpeed(GymRaceRacer racer, float speed)
    {
        float limit = Time.time + 20f;
        while (racer.Kart.ForwardSpeed < speed || racer.Kart.SpinTime > 0f)
        {
            Require(Time.time < limit, $"{racer.DisplayName} never reached {speed:F0} m/s");
            yield return null;
        }
    }

    /// <summary>On the main lap, at speed, and nowhere near the fork where the roads part.</summary>
    private static bool ClearRoad(GymRaceRacer racer)
    {
        if (racer.Finished || racer.OnShortcut || racer.Kart.ForwardSpeed < 12f || racer.Kart.SpinTime > 0f) return false;
        GymRaceTrackPath main = GymRaceTrackLayout.Main;
        float toFork = main.WrapDistance(GymRaceTrackLayout.ShortcutForkDistance - racer.BranchDistance);
        return toFork > 160f && !GymRaceTrackLayout.IsOnBypassedSpan(racer.BranchDistance);
    }

    private static GymRaceRacer Find(GymRaceDirector director, string name)
    {
        foreach (GymRaceRacer racer in director.Racers)
        {
            if (racer.DisplayName == name) return racer;
        }
        return null;
    }

    private static void Finish(int code, string detail)
    {
        if (code == 0) Debug.Log("GYMCHAOS_RACE_PICKUP_OK " + detail);
        else Debug.LogError("GYMCHAOS_RACE_PICKUP_FAILED " + detail);
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
