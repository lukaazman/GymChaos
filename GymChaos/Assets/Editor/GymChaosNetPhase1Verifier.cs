#if UNITY_EDITOR
using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

/// <summary>
/// Multiplayer phase 1: transport, lobby and avatars. Starts the real relay
/// (Server/relay, node) on a private port and drives it with real clients.
/// - Lobby: a host and three guests share a room with distinct ids 0..3; a fifth
///   client is refused; a guest leaving is announced; the host leaving closes the room.
/// - Messages: broadcast and direct messages arrive with the sender id.
/// - Interpolation: poses are blended 0.12 s in the past, angles wrap, a late
///   stream holds the newest pose.
/// - Avatars: a GymNetSession host shows an avatar for each guest and moves it
///   with the guest's pose messages.
/// </summary>
public static class GymChaosNetPhase1Verifier
{
    private const int Port = 18787;
    private const string Url = "ws://127.0.0.1:18787";

    public static void Run()
    {
        Process relay = null;
        int code = 1;
        try
        {
            relay = StartRelay();
            VerifyLobby();
            VerifyInterpolator();
            VerifyAvatars();
            Debug.Log("GYMCHAOS_NET_PHASE1_OK lobby=4 refused=5th messages=broadcast+direct interpolation=ok avatars=session");
            code = 0;
        }
        catch (Exception exception)
        {
            Debug.LogError("GYMCHAOS_NET_PHASE1_FAILED " + exception);
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
        string directory = Directory.GetParent(Application.dataPath).FullName;
        string script = null;
        for (string probe = directory; probe != null && script == null; probe = Directory.GetParent(probe)?.FullName)
        {
            string candidate = Path.Combine(probe, "Server", "relay", "relay.js");
            if (File.Exists(candidate)) script = candidate;
        }
        if (script == null) throw new FileNotFoundException("Server/relay/relay.js not found above " + directory);
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

    private static void VerifyLobby()
    {
        var host = new GymNetClient();
        host.Host(Url);
        Pump(() => host.CurrentPhase == GymNetClient.Phase.Lobby, "host room", host);
        Require(host.LocalId == 0 && host.IsHost && host.RoomCode.Length == 6, "host did not get id 0 and a code");

        var guests = new GymNetClient[3];
        var all = new GymNetClient[4];
        all[0] = host;
        var received = new string[4];
        var senders = new int[4];
        for (int i = 0; i < 3; i++)
        {
            guests[i] = new GymNetClient();
            all[i + 1] = guests[i];
            guests[i].Join(Url, host.RoomCode);
            Pump(() => guests[i].CurrentPhase == GymNetClient.Phase.Lobby, "guest " + i, all);
            Require(guests[i].LocalId == i + 1, "guest id " + guests[i].LocalId);
        }
        Pump(() => host.Peers.Count == 3, "host sees three peers", all);

        var fifth = new GymNetClient();
        fifth.Join(Url, host.RoomCode);
        Pump(() => fifth.CurrentPhase == GymNetClient.Phase.Failed, "fifth refused", fifth);
        Require(fifth.Error == "room full", "fifth client error: " + fifth.Error);

        for (int i = 0; i < 4; i++)
        {
            int index = i;
            all[i].MessageReceived += (from, body) => { received[index] = body; senders[index] = from; };
        }
        host.Send(-1, "hello");
        Pump(() => received[1] == "hello" && received[2] == "hello" && received[3] == "hello", "broadcast", all);
        Require(received[0] == null && senders[1] == 0, "broadcast reached the sender or lost the sender id");
        guests[1].Send(0, "direct");
        Pump(() => received[0] == "direct", "direct message", all);
        Require(senders[0] == 2 && received[1] == "hello" && received[3] == "hello", "direct message leaked or lost its sender");

        int left = -1;
        host.PeerLeft += id => left = id;
        guests[1].Disconnect();
        Pump(() => left == 2, "peer left", all);
        host.Disconnect();
        Pump(() => guests[0].CurrentPhase == GymNetClient.Phase.Closed, "room closed", guests[0]);
        foreach (GymNetClient client in all) client.Disconnect();
    }

    private static void VerifyInterpolator()
    {
        var interpolator = new GymNetInterpolator();
        Require(!interpolator.TrySample(5d, out _), "an empty buffer produced a pose");
        interpolator.Add(1d, new GymNetPose { x = 0f, r = 350f, s = 0f });
        interpolator.Add(2d, new GymNetPose { x = 10f, r = 10f, s = 2f });
        double mid = 1.5d + GymNetInterpolator.DelaySeconds;
        Require(interpolator.TrySample(mid, out GymNetPose pose), "no pose between two samples");
        Require(Mathf.Abs(pose.x - 5f) < 0.01f && Mathf.Abs(Mathf.DeltaAngle(pose.r, 0f)) < 0.01f && Mathf.Abs(pose.s - 1f) < 0.01f,
            $"blend wrong x={pose.x} r={pose.r} s={pose.s}");
        interpolator.TrySample(9d, out pose);
        Require(Mathf.Approximately(pose.x, 10f), "a late stream did not hold the newest pose");
        for (int i = 0; i < 20; i++) interpolator.Add(3d + i, new GymNetPose { x = i });
        Require(interpolator.Count == 8, "interpolator buffer is not bounded");
        Require(GymNetSession.SendIntervalSeconds >= 1f / 15f && GymNetSession.SendIntervalSeconds <= 1f / 10f, "pose rate outside 10..15 Hz");
    }

    private static void VerifyAvatars()
    {
        GymNetSession session = GymNetSession.Begin(Url, null);
        MethodInfo update = typeof(GymNetSession).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic);
        Action tick = () => update.Invoke(session, null);
        DateTime end = DateTime.UtcNow.AddSeconds(8);
        while (session.Client.CurrentPhase != GymNetClient.Phase.Lobby)
        {
            if (DateTime.UtcNow > end) throw new TimeoutException("session host room");
            tick();
            Thread.Sleep(5);
        }
        var guest = new GymNetClient();
        guest.Join(Url, session.Client.RoomCode);
        end = DateTime.UtcNow.AddSeconds(8);
        while (session.RemoteAvatarCount < 1)
        {
            if (DateTime.UtcNow > end) throw new TimeoutException("avatar for the guest");
            tick();
            guest.Tick();
            Thread.Sleep(5);
        }
        Require(session.TryGetAvatar(1, out GymRemoteAvatar avatar), "no avatar for guest id 1");

        // Two poses 0.1 s apart sent in a burst arrive almost together, so the
        // blend is checked on the avatar itself: feed it timed poses directly.
        double now = Time.unscaledTimeAsDouble;
        avatar.AddPose(now - 0.2d, new GymNetPose { x = 0f, y = 1f, z = 4f, r = 90f });
        avatar.AddPose(now - 0.1d, new GymNetPose { x = 10f, y = 1f, z = 4f, r = 90f });
        typeof(GymRemoteAvatar).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(avatar, null);
        Require(Mathf.Abs(avatar.transform.position.x - 8f) < 0.5f && Mathf.Abs(avatar.transform.position.z - 4f) < 0.01f,
            "avatar not at the interpolated pose: " + avatar.transform.position);

        // A pose message over the wire reaches the avatar of its sender.
        int before = avatar.PoseCount;
        guest.Send(-1, "p" + JsonUtility.ToJson(new GymNetPose { x = 50f, y = 1f, z = 60f }));
        end = DateTime.UtcNow.AddSeconds(8);
        while (avatar.PoseCount == before)
        {
            if (DateTime.UtcNow > end) throw new TimeoutException("pose message");
            tick();
            guest.Tick();
            Thread.Sleep(5);
        }

        guest.Disconnect();
        end = DateTime.UtcNow.AddSeconds(8);
        while (session.RemoteAvatarCount > 0)
        {
            if (DateTime.UtcNow > end) throw new TimeoutException("avatar removal");
            tick();
            Thread.Sleep(5);
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
#endif
