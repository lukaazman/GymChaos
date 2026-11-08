#if UNITY_EDITOR
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Debug = UnityEngine.Debug;

/// <summary>
/// Host death and recovery, in the real scene against the real relay. The
/// scene is the host; a plain client with the guest handshake and the
/// automatic reconnect is the guest.
/// - The host goes (as when its player dies and the scene reloads): the relay
///   keeps the room code, the guest shows itself as reconnecting and retries.
/// - The host comes back with its character and gets the same code; the guest
///   rejoins by itself, receives the world snapshot again, and there is one
///   avatar for it, not two.
/// - A guest whose host never returns gives up after its limit with a reason.
/// </summary>
public static class GymChaosNetHostDeathVerifier
{
    private const string RequestedKey = "GymChaos.NetHostDeathVerificationRequested";
    private const int Port = 18791;
    private const string Url = "ws://127.0.0.1:18791";

    private enum Stage { WaitScene, HostRoom, GuestJoined, HostGone, HostBack, Rejoined, GiveUp, Done }

    private static Stage stage;
    private static double startedAt;
    private static double stageAt;
    private static bool finished;
    private static int resultCode = 1;
    private static Process relay;
    private static GymNetSession session;
    private static GymNetClient guest;
    private static GymNetJoin guestJoin;
    private static GymNetReconnect reconnect;
    private static GymNetClient lonely;
    private static GymNetReconnect lonelyReconnect;
    private static string firstCode;
    private static int worldCount;
    private static int reconnected;
    private static readonly string GuestCharacter = Guid.NewGuid().ToString("N");

    [InitializeOnLoadMethod]
    private static void ResumeAfterReload()
    {
        if (SessionState.GetBool(RequestedKey, false)) Hook();
    }

    public static void Run()
    {
        finished = false;
        worldCount = reconnected = 0;
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
            return;
        }
        if (state != PlayModeStateChange.EnteredEditMode) return;
        EditorApplication.update -= Tick;
        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        SessionState.EraseBool(RequestedKey);
        if (relay != null && !relay.HasExited) relay.Kill();
        if (Application.isBatchMode) GymChaosVerifierExit.Exit(resultCode);
    }

    private static void Enter(Stage next, double now)
    {
        stage = next;
        stageAt = now;
    }

    // One step per editor update: the host session ticks in its own Update, so this must never block.
    private static void Tick()
    {
        if (finished || !EditorApplication.isPlaying) return;
        double now = GymChaosVerifierClock.Now;
        float clock = (float)now;
        try
        {
            if (now - startedAt > 200d) throw new TimeoutException("stage " + stage + " did not finish");
            guest?.Tick();
            reconnect?.Tick(clock);
            lonely?.Tick();
            lonelyReconnect?.Tick(clock);
            guestJoin?.Tick(clock);
            switch (stage)
            {
                case Stage.WaitScene:
                    if (GymTimeOfDay.Instance == null || UnityEngine.Object.FindAnyObjectByType<PlayerMovement>() == null || now - startedAt < 5d) return;
                    relay = StartRelay();
                    session = GymNetSession.Begin(Url, null);
                    Enter(Stage.HostRoom, now);
                    break;
                case Stage.HostRoom:
                    if (session.Client.CurrentPhase != GymNetClient.Phase.Lobby) return;
                    firstCode = session.Client.RoomCode;
                    Require(!string.IsNullOrEmpty(session.Client.Token), "the host got no reservation token");
                    guest = new GymNetClient();
                    guestJoin = new GymNetJoin(guest)
                    {
                        CharacterSource = () => GymCharacterSave.New("cardio"),
                        WorldSink = w => worldCount++
                    };
                    guest.MessageReceived += (from, body) => guestJoin.Handle(from, body);
                    reconnect = new GymNetReconnect(guest, Url, firstCode, GuestCharacter, 60f);
                    reconnect.Reconnected += () => { reconnected++; guestJoin.Reset(); };
                    guest.Join(Url, firstCode, GuestCharacter);
                    Enter(Stage.GuestJoined, now);
                    break;
                case Stage.GuestJoined:
                    if (guest.CurrentPhase != GymNetClient.Phase.Lobby || worldCount < 1 || session.RemoteAvatarCount != 1) return;
                    Require(!reconnect.Active, "reconnecting while the host is up");
                    // The host's player dies: the room is remembered, the scene reloads (the session ends).
                    GymNetSession.RememberRoomForRecovery();
                    GymNetSession.End();
                    Enter(Stage.HostGone, now);
                    break;
                case Stage.HostGone:
                    if (now - stageAt < 2d) return;
                    Require(reconnect.Active && reconnect.Attempts >= 1 && !reconnect.GaveUp, "the guest is not reconnecting");
                    Require(guest.CurrentPhase != GymNetClient.Phase.Lobby, "the guest still thinks it is in the room");
                    // The host loads its save and hosts again: the same code.
                    session = GymNetSession.Begin(Url, null);
                    Enter(Stage.HostBack, now);
                    break;
                case Stage.HostBack:
                    if (session.Client.CurrentPhase != GymNetClient.Phase.Lobby) return;
                    Require(session.Client.RoomCode == firstCode, $"re-hosted code {session.Client.RoomCode}, was {firstCode}");
                    Require(!GymNetSession.HasRecovery, "the recovery stayed after the room came back");
                    Enter(Stage.Rejoined, now);
                    break;
                case Stage.Rejoined:
                    if (reconnected < 1 || guest.CurrentPhase != GymNetClient.Phase.Lobby || worldCount < 2 || session.RemoteAvatarCount != 1) return;
                    Require(guest.LocalId == 1 && !reconnect.Active, "the guest did not rejoin as player 1");
                    // One avatar for the guest, not one per join.
                    int avatars = 0;
                    foreach (GameObject root in UnityEngine.Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None))
                        if (root.name == "Remote Player 1") avatars++;
                    Require(avatars == 1, "avatars for the guest: " + avatars);
                    lonely = new GymNetClient();
                    lonelyReconnect = new GymNetReconnect(lonely, Url, "ZZZZ", Guid.NewGuid().ToString("N"), 3f);
                    lonelyReconnect.Begin(clock);
                    Enter(Stage.GiveUp, now);
                    break;
                case Stage.GiveUp:
                    if (!lonelyReconnect.GaveUp)
                    {
                        if (now - stageAt > 10d) throw new TimeoutException("a guest with no host never gave up");
                        return;
                    }
                    Require(!string.IsNullOrEmpty(lonelyReconnect.GiveUpReason), "no reason for giving up");
                    Debug.Log("GYMCHAOS_NET_HOST_DEATH_OK code=kept rehost=same-code rejoin=auto world=again avatars=1 giveUp=" + lonelyReconnect.GiveUpReason);
                    Enter(Stage.Done, now);
                    Finish(0, null);
                    break;
            }
        }
        catch (Exception exception)
        {
            Finish(1, exception.ToString());
        }
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

    private static void Finish(int code, string error)
    {
        if (error != null) Debug.LogError("GYMCHAOS_NET_HOST_DEATH_FAILED " + error);
        GymNetSession.End();
        finished = true;
        resultCode = code;
        GymChaosVerifierExit.Record(code);
        GymChaosVerifierClock.EndFixedStep();
        if (relay != null && !relay.HasExited) relay.Kill();
        if (Application.isBatchMode) GymChaosVerifierExit.Exit(code);
        else EditorApplication.isPlaying = false;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
#endif
