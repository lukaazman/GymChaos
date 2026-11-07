#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Debug = UnityEngine.Debug;

/// <summary>
/// Co-op race against the real relay: this scene is the host, a plain client is the guest.
/// - Guest request logic: one request in flight, cooldown after a no, timeout.
/// - Host start: the guest is in the race at once, in an extra back-row slot (all five AI
///   racers kept), the countdown waits for the guest, laps come from the guest's kart poses,
///   and a guest leaving mid-race takes only its kart away.
/// - Guest request: popup on the host, double request ignored, deny with cooldown, accept
///   starts the race for both; the guest finishes, gets the results, a rematch drops old-round
///   poses, and the end of the race reaches the guest. Logs GYMCHAOS_NET_RACE_OK.
/// </summary>
public static class GymChaosNetRaceVerifier
{
    private const string RequestedKey = "GymChaos.NetRaceVerificationRequested";
    private const int Port = 18791;
    private const string Url = "ws://127.0.0.1:18791";
    private const float DriveSpeed = 50f;

    private static IEnumerator routine;
    private static readonly Stack<IEnumerator> stack = new Stack<IEnumerator>();
    private static readonly StringBuilder report = new StringBuilder();
    private static Process relay;
    private static GymNetClient guest;
    private static readonly List<string> inbox = new List<string>();
    private static bool driving;
    private static float driveS;
    private static int driveRound;

    [InitializeOnLoadMethod]
    private static void ResumeAfterReload()
    {
        if (SessionState.GetBool(RequestedKey, false)) Hook();
    }

    public static void Run()
    {
        GymChaosVerifierExit.Record(1);
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
            return;
        }
        if (change != PlayModeStateChange.EnteredEditMode) return;
        EditorApplication.update -= Tick;
        GymChaosVerifierClock.EndFixedStep();
        KillRelay();
        bool unfinished = SessionState.GetBool(RequestedKey, false);
        SessionState.EraseBool(RequestedKey);
        if (Application.isBatchMode) GymChaosVerifierExit.Exit(unfinished ? 1 : 0);
        else GymChaosVerifierSave.Restore();
    }

    private sealed class VerificationFailure : Exception
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
            guest?.Tick();
            if (driving) SendKart();
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
        catch (Exception exception)
        {
            Finish(1, "exception " + exception + " | " + report);
        }
    }

    // Real-time deadline: the scenario runs at the scenario time scale.
    private static IEnumerator Until(Func<bool> condition, float seconds, string what)
    {
        float limit = Time.realtimeSinceStartup + seconds;
        while (!condition())
        {
            Require(Time.realtimeSinceStartup < limit, "timed out: " + what);
            yield return null;
        }
    }

    private static IEnumerator Hold(float seconds)
    {
        float until = Time.realtimeSinceStartup + seconds;
        while (Time.realtimeSinceStartup < until) yield return null;
    }

    private static int Count(string prefix)
    {
        int n = 0;
        foreach (string m in inbox) if (m.StartsWith(prefix, StringComparison.Ordinal)) n++;
        return n;
    }

    private static string Last(string prefix)
    {
        for (int i = inbox.Count - 1; i >= 0; i--) if (inbox[i].StartsWith(prefix, StringComparison.Ordinal)) return inbox[i];
        return null;
    }

    private static void JoinGuest(string code)
    {
        inbox.Clear();
        guest = new GymNetClient();
        guest.MessageReceived += (from, body) =>
        {
            if (from == 0 && !string.IsNullOrEmpty(body) && body[0] == GymNetRace.Tag) inbox.Add(body);
        };
        guest.Join(Url, code);
    }

    private static void SendPose(Vector3 at)
    {
        guest.Send(0, "p" + JsonUtility.ToJson(new GymNetPose { x = at.x, y = at.y, z = at.z, h = 1f }));
    }

    private static void StartDriving(int slot, int round)
    {
        driveS = GymRaceTrackLayout.Main.ClosestDistance(GymRaceTrackLayout.GridSlot(slot).position);
        driveRound = round;
        driving = true;
    }

    private static void SendKart()
    {
        if (guest == null || guest.CurrentPhase != GymNetClient.Phase.Lobby) return;
        driveS += DriveSpeed * Time.deltaTime;
        GymRaceTrackLayout.Main.Sample(driveS, out Vector3 center, out Vector3 tangent, out _, out _);
        guest.Send(0, GymNetRace.FormatKart(driveRound, center + Vector3.up * 1.2f, Mathf.Atan2(tangent.x, tangent.z) * Mathf.Rad2Deg, DriveSpeed));
    }

    private static GymRaceRacer GuestRacer(GymRaceDirector director, int id)
    {
        foreach (GymRaceRacer racer in director.Racers) if (racer.RemoteHuman && racer.NetId == id) return racer;
        return null;
    }

    private static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

    private static IEnumerator Scenario()
    {
        CheckGuestRequestLogic();

        PlayerMovement player = null;
        yield return Until(() =>
        {
            player = UnityEngine.Object.FindFirstObjectByType<PlayerMovement>();
            return player != null && GymRaceCityCarver.CityReady() && GymRaceSign.Instance != null && GymRaceDirector.Instance != null;
        }, 160f, "scene ready");
        yield return Hold(2f);
        GymRaceDirector director = GymRaceDirector.Instance;
        GymExperienceService progression = GymExperienceService.Active;
        Require(progression != null && progression.State != null, "no progression");
        int startsBefore = progression.State.raceStarts;

        relay = StartRelay();
        GymNetSession session = GymNetSession.Begin(Url, null);
        yield return Until(() => session.Client.CurrentPhase == GymNetClient.Phase.Lobby, 15f, "host room");
        GymNetRaceUi ui = session.GetComponent<GymNetRaceUi>();
        Require(ui != null, "no race UI on the session");

        // ================================================== race 1: the host starts
        JoinGuest(session.Client.RoomCode);
        yield return Until(() => guest.CurrentPhase == GymNetClient.Phase.Lobby && session.RemoteAvatarCount == 1, 15f, "guest join");
        int guestId = guest.LocalId;
        Vector3 nearSign = GymRaceSign.Instance.StandPoint + Vector3.right * 2f;
        SendPose(nearSign);
        yield return Until(() => session.TryGetAvatar(guestId, out GymRemoteAvatar a) && a.PoseCount > 0, 5f, "guest avatar pose");
        var eligible = new List<int>();
        GymNetRace.CollectEligible(session, eligible);
        Require(eligible.Count == 1 && eligible[0] == guestId, "guest not eligible: " + string.Join(",", eligible));

        yield return Until(() => director.BlockReasonForVerification == null, 90f, "race unblocked");
        Require(director.StartRaceForVerification(player, true), "host race refused");
        Time.timeScale = GymChaosVerifierClock.ScenarioTimeScale;
        yield return Until(() => Count("Rs|") == 1, 15f, "guest told the race starts");
        Require(Last("Rs|") == "Rs|0|" + guestId, "start message " + Last("Rs|"));
        Require(director.NetRole == GymRaceDirector.NetRoleKind.Host, "host role " + director.NetRole);

        yield return Until(() => director.Racers.Count == 7, 60f, "seven karts");
        Require(director.Hud.RacerTotal == 7, "hud total " + director.Hud.RacerTotal);
        CheckGrid(director, guestId);
        // The countdown waits for the guest's ready.
        yield return Hold(5f);
        Require(director.CurrentPhase == GymRaceDirector.Phase.Building, "host did not wait for the guest: " + director.CurrentPhase);
        Require(Count("Rc") == 0, "countdown sent before the guest was ready");
        guest.Send(0, "Rr");
        yield return Until(() => Count("Rc") == 1 && director.CurrentPhase == GymRaceDirector.Phase.Countdown, 10f, "countdown after ready");
        yield return Until(() => Count("Rg") == 1 && director.CurrentPhase == GymRaceDirector.Phase.Racing, 15f, "go");
        StartDriving(6, 0);
        yield return Until(() => GuestRacer(director, guestId) != null && GuestRacer(director, guestId).Crossings >= 1, 20f, "host counts the guest's start-line crossing");
        string state = Last("Rt|");
        Require(state != null && state.Split('|').Length == 3 + 7, "race state " + state);
        // A pose from another round is dropped.
        GymRaceRacer remote = GuestRacer(director, guestId);
        guest.Send(0, GymNetRace.FormatKart(9, new Vector3(5000f, 50f, 5000f), 0f, 0f));
        yield return Hold(0.5f);
        Require(Flat(remote.Kart.Body.position).magnitude < 4000f, "an old-round pose moved the kart");
        report.Append($"race1 crossings={remote.Crossings} ");

        // Guest leaves mid-race: only its kart goes.
        driving = false;
        guest.Disconnect();
        guest = null;
        yield return Until(() => director.Racers.Count == 6, 10f, "guest kart removed");
        CheckAiKept(director);
        yield return Hold(2f);
        Require(director.CurrentPhase == GymRaceDirector.Phase.Racing, "race stopped after the guest left: " + director.CurrentPhase);
        director.ForceResultsForVerification();
        yield return Until(() => director.CurrentPhase == GymRaceDirector.Phase.Idle, 60f, "race 1 teardown");
        Time.timeScale = 1f;
        Require(director.NetRole == GymRaceDirector.NetRoleKind.None && GymRaceKart.All.Count == 0, "race 1 left state behind");
        Require(progression.State.raceStarts == startsBefore + 1, "host race not recorded once");

        // ================================================== race 2: the guest asks
        GymNetRace.RequestCooldownSeconds = 1.5f;
        JoinGuest(session.Client.RoomCode);
        yield return Until(() => guest.CurrentPhase == GymNetClient.Phase.Lobby && session.RemoteAvatarCount == 1, 15f, "guest rejoin");
        guestId = guest.LocalId;
        SendPose(nearSign);
        yield return Until(() => session.TryGetAvatar(guestId, out GymRemoteAvatar a) && a.PoseCount > 0, 5f, "rejoined avatar pose");
        guest.Send(0, "Rq");
        guest.Send(0, "Rq");
        yield return Until(() => GymNetRace.PopupVisible && ui.PopupActive, 5f, "host popup");
        Require(GymNetRace.Pending.Count == 1 && GymNetRace.PopupRequester == guestId, "double request queued twice");
        Require(ui.PopupText == "PLAYER " + (guestId + 1) + " WANTS TO RACE", "popup text " + ui.PopupText);
        Require(director.CurrentPhase == GymRaceDirector.Phase.Idle, "a request started the race");
        GymNetRace.Deny();
        yield return Until(() => Count("Rd|") == 1, 5f, "deny reaches the guest");
        yield return null;
        Require(!GymNetRace.PopupVisible && !ui.PopupActive && director.CurrentPhase == GymRaceDirector.Phase.Idle, "deny left a popup or a race");
        guest.Send(0, "Rq");
        yield return Until(() => Count("Rd|") == 2, 5f, "cooldown answer");
        Require(Last("Rd|") == "Rd|ask again later" && !GymNetRace.PopupVisible, "request inside the cooldown was queued");
        yield return Hold(1.7f);
        yield return Until(() => director.BlockReasonForVerification == null, 90f, "race unblocked again");
        guest.Send(0, "Rq");
        yield return Until(() => GymNetRace.PopupVisible, 5f, "popup after the cooldown");
        GymNetRace.AutopilotForVerification = true;
        director.AutoRaceAgainOnce = true;
        Require(GymNetRace.Accept(), "accept did not start the race");
        Require(!GymNetRace.PopupVisible, "popup kept after accept");
        Time.timeScale = GymChaosVerifierClock.ScenarioTimeScale;
        yield return Until(() => Count("Rs|") == 1, 15f, "start after accept");
        yield return Until(() => director.Racers.Count == 7, 60f, "seven karts (accepted)");
        CheckGrid(director, guestId);
        guest.Send(0, "Rr");
        yield return Until(() => Count("Rg") == 1, 30f, "go (accepted)");
        StartDriving(6, 0);
        guest.Send(0, "Rq");
        yield return Until(() => Count("Rd|") == 3, 5f, "request during a race refused");
        Require(Last("Rd|") == "Rd|race running", "busy answer " + Last("Rd|"));
        remote = GuestRacer(director, guestId);
        yield return Until(() => remote.Finished, 300f, "guest finishes (laps " + remote.Crossings + ")");
        Require(remote.FinishPlace >= 1 && remote.FinishPlace <= 7, "guest place " + remote.FinishPlace);
        yield return Until(() => Count("Re") >= 1, 120f, "results to the guest");
        string results = Last("Re");
        string[] rows = results.Split('|');
        Require(rows.Length == 8, "result rows " + results);
        bool guestRow = false;
        var places = new HashSet<string>();
        for (int i = 1; i < rows.Length; i++)
        {
            string[] v = rows[i].Split(';');
            places.Add(v[1]);
            if (v[0] == "6") guestRow = v[1] == remote.FinishPlace.ToString() && v[4] == "1";
        }
        Require(guestRow && places.Count == 7, "results " + results);
        report.Append($"race2 guestPlace={remote.FinishPlace} hostPlace={director.LastPlayerPlace} ");

        // Rematch: the guest is told, poses of the old round are dropped.
        yield return Until(() => Count("Ra|") == 1, 30f, "rematch to the guest");
        Require(Last("Ra|") == "Ra|0|1" && director.NetRound == 1, "rematch message " + Last("Ra|"));
        driving = false;
        yield return Until(() => director.CurrentPhase == GymRaceDirector.Phase.Countdown, 30f, "rematch countdown");
        guest.Send(0, GymNetRace.FormatKart(0, new Vector3(5000f, 50f, 5000f), 0f, 0f));
        yield return Hold(0.5f);
        Require(Vector3.Distance(Flat(remote.Kart.Body.position), Flat(GymRaceTrackLayout.GridSlot(6).position)) < 2f &&
            !remote.Finished, "rematch kart not reset or moved by an old pose");
        yield return Until(() => director.CurrentPhase == GymRaceDirector.Phase.Racing, 15f, "rematch racing");
        StartDriving(6, 1);
        yield return Hold(2f);
        director.ForceResultsForVerification();
        yield return Until(() => Count("Rx") == 1, 60f, "race end to the guest");
        yield return Until(() => director.CurrentPhase == GymRaceDirector.Phase.Idle, 30f, "race 2 teardown");
        driving = false;
        Time.timeScale = 1f;
        Require(Count("Re") == 2, "rematch results not sent");
        Require(progression.State.raceStarts == startsBefore + 3, "host races recorded " + (progression.State.raceStarts - startsBefore));
        Require(GymRaceKart.All.Count == 0 && !GymRaceDirector.IsRaceActive, "race 2 left karts behind");
        Require(session.TryGetAvatar(guestId, out GymRemoteAvatar back) && back.gameObject.activeSelf, "guest avatar still hidden");
        report.Append("rematch=1");
    }

    private static void CheckGrid(GymRaceDirector director, int guestId)
    {
        CheckAiKept(director);
        GymRaceRacer remote = GuestRacer(director, guestId);
        Require(remote != null && remote.GridSlot == 6 && remote.Kart.RemoteDriven, "guest kart not in slot 6");
        Require(remote.DisplayName == "Player " + (guestId + 1), "guest name " + remote.DisplayName);
        Require(director.PlayerRacer != null && director.PlayerRacer.GridSlot == 5, "host kart not in slot 5");
        Pose slot = GymRaceTrackLayout.GridSlot(6);
        Require(slot.position.z < GymRaceTrackLayout.GridSlot(5).position.z - 1f, "slot 6 not behind the last row");
        Require(Vector3.Distance(Flat(remote.Kart.Body.position), Flat(slot.position)) < 1.2f, "guest kart off its slot");
    }

    private static void CheckAiKept(GymRaceDirector director)
    {
        var names = new HashSet<string>();
        foreach (GymRaceRacer racer in director.Racers) names.Add(racer.DisplayName);
        foreach (string ai in new[] { "Zyzz", "Cbum", "Officer", "Jay Cutler", "Arnold", "You" })
        {
            Require(names.Contains(ai), "racer missing: " + ai + " (" + string.Join(",", names) + ")");
        }
    }

    private static void CheckGuestRequestLogic()
    {
        float cooldown = GymNetRace.RequestCooldownSeconds;
        var sent = new List<string>();
        var request = new GymNetRace.GuestRequest(sent.Add);
        Require(request.TryRequest(0f) && request.Current == GymNetRace.GuestRequest.State.Waiting && sent.Count == 1 && sent[0] == "Rq", "first request");
        Require(!request.TryRequest(1f) && sent.Count == 1, "second request while waiting was sent");
        request.Denied(2f);
        Require(request.Current == GymNetRace.GuestRequest.State.Cooldown && !request.TryRequest(2f + cooldown * 0.5f), "request inside the cooldown");
        Require(request.TryRequest(2.1f + cooldown) && sent.Count == 2, "request after the cooldown");
        request.Tick(2.2f + cooldown + GymNetRace.WaitTimeoutSeconds);
        Require(request.Current == GymNetRace.GuestRequest.State.Cooldown, "unanswered request did not time out");
        request.Tick(2.3f + 2f * cooldown + GymNetRace.WaitTimeoutSeconds);
        Require(request.Current == GymNetRace.GuestRequest.State.None, "cooldown did not end");
        request.TryRequest(1000f);
        request.Clear();
        Require(request.Current == GymNetRace.GuestRequest.State.None, "clear");
        Require(GymNetRace.TryParseKart(GymNetRace.FormatKart(2, new Vector3(1.5f, 2f, -3f), 90f, 20f), out int round, out Vector3 p, out float yaw, out float speed) &&
            round == 2 && Vector3.Distance(p, new Vector3(1.5f, 2f, -3f)) < 0.01f && Mathf.Abs(yaw - 90f) < 0.1f && Mathf.Abs(speed - 20f) < 0.1f, "kart pose round trip");
        Require(!GymNetRace.TryParseKart("Rk|0|NaN;0;0;0;0", out _, out _, out _, out _) &&
            !GymNetRace.TryParseKart("Rk|0|1;2;3", out _, out _, out _, out _) &&
            !GymNetRace.TryParseKart("Rk|0|99999;0;0;0;0", out _, out _, out _, out _), "bad kart pose accepted");
        report.Append("request-logic=1 ");
    }

    private static Process StartRelay()
    {
        string script = null;
        for (string probe = Directory.GetParent(Application.dataPath).FullName; probe != null && script == null; probe = Directory.GetParent(probe)?.FullName)
        {
            string candidate = Path.Combine(probe, "Server", "relay", "relay.js");
            if (File.Exists(candidate)) script = candidate;
        }
        if (script == null) throw new FileNotFoundException("Server/relay/relay.js not found");
        var info = new ProcessStartInfo("node", "\"" + script + "\"")
        {
            UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(script)
        };
        info.Environment["PORT"] = Port.ToString();
        Process process = Process.Start(info);
        string line = null;
        var reader = new Thread(() => line = process.StandardOutput.ReadLine());
        reader.Start();
        if (!reader.Join(10000) || line == null || !line.Contains("GYMCHAOS_RELAY_LISTENING"))
            throw new InvalidOperationException("relay did not start: " + line);
        return process;
    }

    private static void KillRelay()
    {
        if (relay != null && !relay.HasExited) relay.Kill();
        relay = null;
    }

    private static void Finish(int code, string detail)
    {
        if (code == 0) Debug.Log("GYMCHAOS_NET_RACE_OK " + detail);
        else Debug.LogError("GYMCHAOS_NET_RACE_FAILED " + detail);
        routine = null;
        driving = false;
        guest?.Disconnect();
        guest = null;
        GymNetRace.RequestCooldownSeconds = 8f;
        GymNetSession.End();
        Time.timeScale = 1f;
        GymRaceCityCarver.Restore();
        GymRaceCorridorClearance.Restore();
        KillRelay();
        GymChaosVerifierExit.Record(code);
        SessionState.EraseBool(RequestedKey);
        EditorApplication.update -= Tick;
        GymChaosVerifierClock.EndFixedStep();
        if (Application.isBatchMode) GymChaosVerifierExit.Exit(code);
        else EditorApplication.isPlaying = false;
    }
}
#endif
