#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Peer-to-peer rooms over real WebRTC (Unity WebRTC, free PeerJS broker for the introduction, Google STUN):
/// a host and two guests in this process share one room, each over its own data channel.
/// - Room: the host gets id 0 and a six-letter code; guests dial the code and get ids 1 and 2.
/// - Messages: guest to host, host to everyone, with the sender id.
/// - Leaving: a guest leaving is announced; the host stopping closes the room for the guests.
/// - Errors: dialling a code nobody hosts fails instead of hanging.
/// Needs internet (broker + STUN). Logs GYMCHAOS_PEER_OK.
/// </summary>
public static class GymChaosPeerVerifier
{
    private static readonly GymNetClient host = new GymNetClient();
    private static readonly GymNetClient guestA = new GymNetClient();
    private static readonly GymNetClient guestB = new GymNetClient();
    private static readonly GymNetClient stray = new GymNetClient();
    private static readonly GymNetClient guestC = new GymNetClient();
    private static readonly List<string> hostInbox = new List<string>();
    private static readonly List<string> aInbox = new List<string>();
    private static readonly List<string> bInbox = new List<string>();
    private static int step;
    private static float started;
    private static bool peerLeft;
    private static bool peerJoined;
    private static string code;

    private const string RequestedKey = "GymChaos.PeerVerifierRequested";

    [InitializeOnLoadMethod]
    private static void ResumeAfterReload()
    {
        if (SessionState.GetBool(RequestedKey, false)) GymChaosVerifierHooks.Hook(Tick, Changed);
    }

    // Unity WebRTC marshals its callbacks through the player's synchronization context: play mode only.
    public static void Run()
    {
        SessionState.SetBool(RequestedKey, true);
        UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene);
        GymChaosVerifierHooks.Hook(Tick, Changed);
        EditorApplication.isPlaying = true;
    }

    private static void Changed(PlayModeStateChange change)
    {
        if (change != PlayModeStateChange.EnteredPlayMode) return;
        started = Time.realtimeSinceStartup;
        host.PeerJoined += _ => peerJoined = true;
        host.PeerLeft += _ => peerLeft = true;
        host.MessageReceived += (from, body) => hostInbox.Add(from + ":" + body);
        guestA.MessageReceived += (from, body) => aInbox.Add(from + ":" + body);
        guestB.MessageReceived += (from, body) => bInbox.Add(from + ":" + body);
        // Cross-platform runs: GYMCHAOS_PEER_MODE=host waits for a WebGL guest, =guest dials GYMCHAOS_PEER_CODE (a WebGL host).
        mode = Environment.GetEnvironmentVariable("GYMCHAOS_PEER_MODE") ?? string.Empty;
        if (mode == "guest")
        {
            code = Environment.GetEnvironmentVariable("GYMCHAOS_PEER_CODE");
            guestA.Join(GymPeerHost.GuestScheme + code, code, "native-guest");
        }
        else
        {
            GymPeerHost.Start();
            host.Host(GymPeerHost.LoopbackUrl);
        }
        running = true;
    }

    private static string mode;

    private static bool running;

    private static void Tick()
    {
        if (!running) return;
        try
        {
            GymPeerHost.Current?.Pump();
            host.Tick();
            guestA.Tick();
            guestB.Tick();
            guestC.Tick();
            stray.Tick();
            if (Time.realtimeSinceStartup - started > 50f) throw new Exception("timeout at step " + step);
            Advance();
        }
        catch (Exception exception)
        {
            Finish(1, "GYMCHAOS_PEER_FAILED " + exception);
        }
    }

    private static void Next() => step++;

    private static void AdvanceExternal()
    {
        float wait = float.Parse(Environment.GetEnvironmentVariable("GYMCHAOS_PEER_WAIT") ?? "150");
        if (Time.realtimeSinceStartup - started > wait) throw new Exception($"no peer within {wait}s (step {step})");
        if (mode == "host")
        {
            if (host.CurrentPhase == GymNetClient.Phase.Failed) throw new Exception("host failed: " + host.Error);
            if (step == 0 && host.CurrentPhase == GymNetClient.Phase.Lobby)
            {
                Debug.Log("GYMCHAOS_PEER_HOSTCODE " + host.RoomCode);
                step = 1;
            }
            // A remote guest speaks first (its game or this verifier): answer once, then hold a little.
            if (step == 1 && hostInbox.Count > 0) { host.Send(-1, "hello all"); stepAt = Time.realtimeSinceStartup; step = 2; }
            if (step == 2 && Time.realtimeSinceStartup - stepAt > 6f)
                Finish(0, $"GYMCHAOS_PEER_HOST_OK peers={host.Peers.Count} firstMessage={hostInbox[0].Substring(0, Math.Min(40, hostInbox[0].Length))}");
        }
        else
        {
            if (guestA.CurrentPhase == GymNetClient.Phase.Failed) throw new Exception("guest failed: " + guestA.Error);
            if (step == 0 && guestA.CurrentPhase == GymNetClient.Phase.Lobby)
            {
                guestA.Send(0, "hello host");
                step = 1;
            }
            if (step == 1 && aInbox.Count > 0)
                Finish(0, $"GYMCHAOS_PEER_GUEST_OK id={guestA.LocalId} peers={guestA.Peers.Count} firstMessage={aInbox[0].Substring(0, Math.Min(40, aInbox[0].Length))}");
        }
    }

    private static float stepAt;
    private static GymNetSession session;
    private static GymRemoteAvatar avatar;
    private static int poseBefore;

    private static void Advance()
    {
        if (mode == "host" || mode == "guest") { AdvanceExternal(); return; }
        switch (step)
        {
            case 0:
                if (host.CurrentPhase == GymNetClient.Phase.Failed) throw new Exception("host failed: " + host.Error);
                if (host.CurrentPhase != GymNetClient.Phase.Lobby) return;
                code = host.RoomCode;
                if (host.LocalId != 0 || code == null || code.Length != 6) throw new Exception("host room " + host.LocalId + " " + code);
                guestA.Join(GymPeerHost.GuestScheme + code, code, "alice");
                Next();
                break;
            case 1:
                if (guestA.CurrentPhase == GymNetClient.Phase.Failed) throw new Exception("guest A failed: " + guestA.Error);
                if (guestA.CurrentPhase != GymNetClient.Phase.Lobby) return;
                if (guestA.LocalId != 1) throw new Exception("guest A id " + guestA.LocalId);
                if (!peerJoined) return;
                guestB.Join(GymPeerHost.GuestScheme + code, code, "bob");
                Next();
                break;
            case 2:
                if (guestB.CurrentPhase == GymNetClient.Phase.Failed) throw new Exception("guest B failed: " + guestB.Error);
                if (guestB.CurrentPhase != GymNetClient.Phase.Lobby) return;
                if (guestB.LocalId != 2) throw new Exception("guest B id " + guestB.LocalId);
                guestA.Send(0, "hello host");
                host.Send(-1, "hello all");
                Next();
                break;
            case 3:
                if (hostInbox.Count < 1 || aInbox.Count < 1 || bInbox.Count < 1) return;
                if (hostInbox[0] != "1:hello host") throw new Exception("host got " + hostInbox[0]);
                if (aInbox[0] != "0:hello all" || bInbox[0] != "0:hello all") throw new Exception("guests got " + aInbox[0] + " / " + bInbox[0]);
                // Same character twice is refused by the room.
                stray.Join(GymPeerHost.GuestScheme + code, code, "alice");
                Next();
                break;
            case 4:
                if (stray.CurrentPhase == GymNetClient.Phase.Lobby) throw new Exception("duplicate character was let in");
                if (stray.CurrentPhase != GymNetClient.Phase.Failed) return;
                if (stray.Error != "character in use") throw new Exception("stray error " + stray.Error);
                stray.Join(GymPeerHost.GuestScheme + "ZZZZZZ", "ZZZZZZ", "carol");
                Next();
                break;
            case 5:
                if (stray.CurrentPhase == GymNetClient.Phase.Lobby) throw new Exception("joined a room nobody hosts");
                if (stray.CurrentPhase != GymNetClient.Phase.Failed) return;
                guestC.Join(GymPeerHost.GuestScheme + code, code, "cat");
                step = 10;
                break;
            case 10:
                if (guestC.CurrentPhase == GymNetClient.Phase.Failed) throw new Exception("guest C failed: " + guestC.Error);
                if (guestC.CurrentPhase != GymNetClient.Phase.Lobby) return;
                if (guestC.LocalId != 3) throw new Exception("guest C id " + guestC.LocalId);
                // Host + three guests fill the room: a fifth is refused.
                stray.Join(GymPeerHost.GuestScheme + code, code, "dave");
                step = 11;
                break;
            case 11:
                if (stray.CurrentPhase == GymNetClient.Phase.Lobby) throw new Exception("fifth player was let in");
                if (stray.CurrentPhase != GymNetClient.Phase.Failed) return;
                if (stray.Error != "room full") throw new Exception("fifth player error " + stray.Error);
                guestB.Disconnect();
                step = 6;
                break;
            case 6:
                if (!peerLeft) return;
                GymPeerHost.Stop();
                Next();
                break;
            case 7:
                if (guestA.CurrentPhase != GymNetClient.Phase.Closed && guestA.CurrentPhase != GymNetClient.Phase.Idle) return;
                // The game's own session as host: the same room, but with avatars for the guests.
                session = GymNetSession.Begin(GymNetSession.DefaultRelayUrl, null);
                step = 20;
                break;
            case 20:
                if (session.Client.CurrentPhase == GymNetClient.Phase.Failed) throw new Exception("session host failed: " + session.Client.Error);
                if (session.Client.CurrentPhase != GymNetClient.Phase.Lobby) return;
                code = session.Client.RoomCode;
                guestA.Join(GymPeerHost.GuestScheme + code, code, "session-guest");
                step = 21;
                break;
            case 21:
                if (guestA.CurrentPhase == GymNetClient.Phase.Failed) throw new Exception("session guest failed: " + guestA.Error);
                if (guestA.CurrentPhase != GymNetClient.Phase.Lobby || session.RemoteAvatarCount < 1) return;
                if (!session.TryGetAvatar(1, out avatar)) throw new Exception("no avatar for the guest");
                poseBefore = avatar.PoseCount;
                guestA.Send(-1, "p" + JsonUtility.ToJson(new GymNetPose { x = 50f, y = 1f, z = 60f }));
                step = 22;
                break;
            case 22:
                if (avatar.PoseCount <= poseBefore) return;
                GymNetSession.End();
                Finish(0, $"GYMCHAOS_PEER_OK code={code} ids=0,1,2,3 full=5th_refused session=avatar+pose messages=direct+broadcast duplicate=refused unknownRoom=failed leave=announced seconds={Time.realtimeSinceStartup - started:F1}");
                break;
        }
    }

    private static void Finish(int exitCode, string message)
    {
        running = false;
        SessionState.SetBool(RequestedKey, false);
        EditorApplication.update -= Tick;
        if (exitCode == 0) Debug.Log(message); else Debug.LogError(message);
        host.Disconnect();
        guestA.Disconnect();
        guestB.Disconnect();
        guestC.Disconnect();
        stray.Disconnect();
        GymPeerHost.Stop();
        EditorApplication.Exit(exitCode);
    }
}
#endif
