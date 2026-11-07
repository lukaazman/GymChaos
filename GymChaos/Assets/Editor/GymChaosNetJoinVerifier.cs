#if UNITY_EDITOR
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

/// <summary>
/// Multiplayer phase 2: join flow and world snapshot, against the real relay.
/// - Validation: a peer's character is rebuilt clean (class must exist, level,
///   ranks, lists and resources clamped; bad ids, NaN and oversize refused).
/// - Handshake: a guest's character reaches the host, which keeps a last-known
///   copy and answers with its world snapshot; a bad character is refused and
///   not kept; a repeat updates the copy without resending the world.
/// - Lease: a character can be in one room at a time.
/// - Context: hosting sets Host, joining sets Join, ending returns to Solo.
/// The save side (a Join session never writes the world section) is covered by
/// GymChaosSaveSplitVerifier.
/// </summary>
public static class GymChaosNetJoinVerifier
{
    private const int Port = 18788;
    private const string Url = "ws://127.0.0.1:18788";

    public static void Run()
    {
        Process relay = null;
        int code = 1;
        try
        {
            relay = StartRelay();
            VerifyValidation();
            VerifyHandshakeAndLease();
            VerifyContext();
            Debug.Log("GYMCHAOS_NET_JOIN_OK validation=clamped handshake=character+world refused=bad-class lease=one-per-character context=host/join/solo");
            code = 0;
        }
        catch (Exception exception)
        {
            Debug.LogError("GYMCHAOS_NET_JOIN_FAILED " + exception);
        }
        finally
        {
            GymNetSession.End();
            if (relay != null && !relay.HasExited) relay.Kill();
        }
        EditorApplication.Exit(code);
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

    private static void Pump(Func<bool> done, string what, params GymNetClient[] clients)
    {
        DateTime end = DateTime.UtcNow.AddSeconds(8);
        while (!done())
        {
            if (DateTime.UtcNow > end) throw new TimeoutException(what);
            foreach (GymNetClient client in clients) client?.Tick();
            Thread.Sleep(5);
        }
    }

    private static GymCharacterSave Character(string classId, int level)
    {
        GymCharacterSave save = GymCharacterSave.New(classId);
        save.progression.level = level;
        save.progression.strengthRank = 3;
        save.oneShotRewards = new[] { "first-win" };
        return save;
    }

    private static void VerifyValidation()
    {
        GymCharacterSave good = Character("cardio", 12);
        Require(GymNetCharacter.TryValidate(GymNetCharacter.ToJson(good), out GymCharacterSave clean, out _) &&
            clean.slotId == good.slotId && clean.classId == "cardio" && clean.progression.level == 12 &&
            clean.progression.strengthRank == 3 && clean.oneShotRewards[0] == "first-win", "a good character was changed or refused");

        GymCharacterSave greedy = Character("strongman", 5000);
        greedy.progression.strengthRank = 99;
        greedy.progression.reputation = 9999;
        greedy.health = 99999f;
        greedy.oneShotRewards = new string[100];
        for (int i = 0; i < greedy.oneShotRewards.Length; i++) greedy.oneShotRewards[i] = new string('x', 100);
        Require(GymNetCharacter.TryValidate(GymNetCharacter.ToJson(greedy), out clean, out string reason),
            "a clampable character was refused: " + reason);
        Require(clean.progression.level == GymNetCharacter.MaxLevel && clean.progression.strengthRank == GymExperienceService.MaxStatRank &&
            clean.progression.reputation == 100 && clean.health == GymNetCharacter.MaxResource &&
            clean.oneShotRewards.Length == GymNetCharacter.MaxRewards && clean.oneShotRewards[0].Length == GymNetCharacter.MaxTextLength,
            "character not clamped");

        GymCharacterSave hostile = Character("cardio", 2);
        string hostileJson = GymNetCharacter.ToJson(hostile).Replace("\\\"shirt\\\":\\\"Black\\\"", "\\\"shirt\\\":\\\"999\\\"");
        Require(hostileJson.Contains("999"), "test fixture did not carry the numeric enum");
        hostile.progression.cardioChallengeTarget = float.NaN;
        string hostileNan = GymNetCharacter.ToJson(hostile).Replace("\\\"shirt\\\":\\\"Black\\\"", "\\\"shirt\\\":\\\"Black,Red\\\"");
        Require(GymNetCharacter.TryValidate(hostileJson, out clean, out _) && clean.progression.shirt == "Black", "numeric enum kept");
        // NaN in the JSON may already fail to parse (refused) or be zeroed; either way it is not kept.
        Require(!GymNetCharacter.TryValidate(hostileNan, out clean, out _) || (System.Enum.IsDefined(typeof(GymShirtColor), clean.progression.shirt) &&
            clean.progression.cardioChallengeTarget == 0f), "enum list or NaN kept");

        GymCharacterSave unknown = Character("cardio", 2);
        unknown.classId = "crossfit";
        Require(!GymNetCharacter.TryValidate(GymNetCharacter.ToJson(unknown), out _, out reason) && reason == "unknown class", "unknown class accepted");
        GymCharacterSave nan = Character("cardio", 2);
        nan.stamina = float.NaN;
        Require(!GymNetCharacter.TryValidate(GymNetCharacter.ToJson(nan), out _, out _), "NaN stamina accepted");
        GymCharacterSave badId = Character("cardio", 2);
        badId.slotId = "../escape";
        Require(!GymNetCharacter.TryValidate(GymNetCharacter.ToJson(badId), out _, out _), "bad character id accepted");
        Require(!GymNetCharacter.TryValidate(new string('{', GymNetCharacter.MaxBytes + 1), out _, out _), "oversize accepted");
        Require(!GymNetCharacter.TryValidate("not json", out _, out _), "garbage accepted");

        GymNetWorldSnapshot world = GymNetCharacter.CleanWorld(JsonUtility.ToJson(new GymNetWorldSnapshot
        {
            worldTime = 7f, worldDay = -4, brokenPanels = new[] { "a" }, fixerIssueIds = new[] { "a", "b" }, fixerIssueSince = new[] { 1f }
        }));
        Require(world != null && world.worldTime == 1f && world.worldDay == 0 && world.fixerIssueIds.Length == 0,
            "world snapshot not bounded");
    }

    private static void VerifyHandshakeAndLease()
    {
        GymCharacterSave hostCharacter = Character("bodybuilding", 3);
        GymCharacterSave guestCharacter = Character("cardio", 7);
        var host = new GymNetClient();
        var hostJoin = new GymNetJoin(host)
        {
            WorldSource = () => new GymNetWorldSnapshot
            {
                worldTime = 0.7f, worldDay = 3, brokenPanels = new[] { "glass-1" },
                fixerIssueIds = new[] { "glass-1" }, fixerIssueSince = new[] { 2.5f }
            }
        };
        host.MessageReceived += (from, body) => hostJoin.Handle(from, body);
        host.Host(Url, hostCharacter.slotId);
        Pump(() => host.CurrentPhase == GymNetClient.Phase.Lobby, "host room", host);

        // Guest A: good character, receives the world.
        var guest = new GymNetClient();
        GymNetWorldSnapshot received = null;
        int worldCount = 0;
        var guestJoin = new GymNetJoin(guest)
        {
            CharacterSource = () => guestCharacter,
            WorldSink = w => { received = w; worldCount++; }
        };
        guest.MessageReceived += (from, body) => guestJoin.Handle(from, body);
        guest.Join(Url, host.RoomCode, guestCharacter.slotId);
        Pump(() => guest.CurrentPhase == GymNetClient.Phase.Lobby, "guest joined", host, guest);
        guestJoin.Tick(0f);
        Pump(() => guestJoin.Guest == GymNetJoin.GuestState.Joined, "guest got the world", host, guest);
        Require(received != null && received.worldTime == 0.7f && received.worldDay == 3 && received.brokenPanels[0] == "glass-1" &&
            received.fixerIssueSince[0] == 2.5f, "world snapshot changed on the way");
        Require(hostJoin.LastKnown.TryGetValue(1, out GymCharacterSave kept) && kept.progression.level == 7 && kept.classId == "cardio",
            "host kept no last-known copy");

        // A repeat updates the host's copy and does not resend the world.
        guestCharacter.progression.level = 8;
        guestJoin.SendCharacter();
        Pump(() => hostJoin.LastKnown[1].progression.level == 8, "last-known update", host, guest);
        Thread.Sleep(100);
        guest.Tick();
        Require(worldCount == 1, "the world was resent on a character update");

        // Guest B: a forged class is refused and not kept.
        GymCharacterSave forged = Character("cardio", 2);
        forged.classId = "hax";
        var guestB = new GymNetClient();
        var guestBJoin = new GymNetJoin(guestB) { CharacterSource = () => forged };
        guestB.MessageReceived += (from, body) => guestBJoin.Handle(from, body);
        guestB.Join(Url, host.RoomCode, forged.slotId);
        Pump(() => guestB.CurrentPhase == GymNetClient.Phase.Lobby, "guest B joined", host, guest, guestB);
        guestBJoin.Tick(0f);
        Pump(() => guestBJoin.Guest == GymNetJoin.GuestState.Rejected, "guest B refused", host, guest, guestB);
        Require(guestBJoin.RejectReason == "unknown class" && !hostJoin.LastKnown.ContainsKey(2), "forged character kept or refusal unclear");

        // A second world message after the join is ignored (it costs a scene scan to apply).
        host.Send(1, "w" + JsonUtility.ToJson(new GymNetWorldSnapshot { worldTime = 0.2f, worldDay = 9 }));
        Thread.Sleep(150);
        guest.Tick();
        Require(worldCount == 1 && guestJoin.Guest == GymNetJoin.GuestState.Joined, "an unsolicited world message was applied");

        // Lease: the same character cannot sit in a second room, nor twice in this one.
        var twin = new GymNetClient();
        twin.Join(Url, host.RoomCode, guestCharacter.slotId);
        Pump(() => twin.CurrentPhase == GymNetClient.Phase.Failed, "lease refusal", twin);
        Require(twin.Error == "character in use", "lease error: " + twin.Error);
        var secondHost = new GymNetClient();
        secondHost.Host(Url, hostCharacter.slotId);
        Pump(() => secondHost.CurrentPhase == GymNetClient.Phase.Failed, "host lease refusal", secondHost);
        Require(secondHost.Error == "character in use", "host lease error: " + secondHost.Error);

        // The lease frees when the socket goes.
        guest.Disconnect();
        Thread.Sleep(300);
        var again = new GymNetClient();
        again.Join(Url, host.RoomCode, guestCharacter.slotId);
        GymNetWorldSnapshot reuseWorld = null;
        var againJoin = new GymNetJoin(again) { CharacterSource = () => guestCharacter, WorldSink = w => reuseWorld = w };
        again.MessageReceived += (from, body) => againJoin.Handle(from, body);
        Pump(() => again.CurrentPhase == GymNetClient.Phase.Lobby, "rejoin after leaving", host, again);
        // The relay hands the freed id to the newcomer: it is a new guest and gets the world.
        againJoin.Tick(0f);
        Pump(() => reuseWorld != null, "world for a guest on a reused id", host, again);
        Require(again.LocalId == 1, "relay did not reuse the freed id: " + again.LocalId);
        foreach (GymNetClient client in new[] { host, guestB, again, twin, secondHost }) client.Disconnect();
    }

    private static void VerifyContext()
    {
        Require(GymNetSession.LaunchContext() == GymSaveContext.Solo, "a plain launch is not Solo");
        GymNetSession.Begin(Url, null);
        Require(GymSessionService.Context == GymSaveContext.Host, "hosting did not set Host");
        GymNetSession.Begin(Url, "ABCD");
        Require(GymSessionService.Context == GymSaveContext.Join, "joining did not set Join");
        Require((GymSessionService.AllowedSections & GymSaveSections.World) == 0, "Join may write the world section");
        // A Join scene never loaded the world: ending the session must keep Join while a character is loaded.
        var activeField = typeof(GymSessionService).GetField("active", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        activeField.SetValue(null, GymCharacterSave.New("cardio"));
        GymNetSession.End();
        Require(GymSessionService.Context == GymSaveContext.Join, "ending a Join session opened the world section");
        activeField.SetValue(null, null);
        GymNetSession.Begin(Url, null);
        GymNetSession.End();
        Require(GymSessionService.Context == GymSaveContext.Solo, "ending did not return to Solo");

        // The session menu's room choice decides the context before any character is created or loaded.
        GymNetSession.SetPending("join", Url, "ABCD");
        Require(GymNetSession.LaunchContext() == GymSaveContext.Join, "menu join did not give Join");
        GymNetSession.SetPending("host", Url, "");
        Require(GymNetSession.LaunchContext() == GymSaveContext.Host, "menu host did not give Host");
        GymNetSession.SetPending("join", Url, "");
        Require(GymNetSession.LaunchContext() == GymSaveContext.Solo, "menu join without a code did not fall back to Solo");
        GymNetSession.SetPending(null, null, null);
        Require(GymNetSession.LaunchContext() == GymSaveContext.Solo, "cleared menu choice is not Solo");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
#endif
