#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Debug = UnityEngine.Debug;

/// <summary>
/// Multiplayer phase 3 in the real scene, against the real relay: this scene
/// is the host, a plain client is the guest.
/// - Snapshot: the host sends actors and doors about 8 times a second; the
///   guest decodes them and a puppet appears for every actor.
/// - Interaction: the guest asks to break a panel near its avatar: the host
///   breaks it and tells the guest; a request from far away or for an
///   unknown panel changes nothing.
/// - Guest world: turning the local actors off leaves the scene running
///   without a single logged exception, with no fighter or director active.
/// </summary>
public static class GymChaosNetWorldVerifier
{
    private const string RequestedKey = "GymChaos.NetWorldVerificationRequested";
    private const int Port = 18789;
    private const string Url = "ws://127.0.0.1:18789";

    private static double startedAt;
    private static bool finished;
    private static int resultCode = 1;
    private static Process relay;
    private static int exceptions;

    [InitializeOnLoadMethod]
    private static void ResumeAfterReload()
    {
        if (SessionState.GetBool(RequestedKey, false)) Hook();
    }

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

    private enum Stage { WaitScene, HostRoom, GuestJoin, Snapshot, FarRequests, NearRequest, Repair, Avatar, Suppress, Done }

    private static Stage stage;
    private static double stageAt;
    private static GymNetSession session;
    private static GymNetClient guest;
    private static readonly List<string> snapshots = new List<string>();
    private static readonly List<string> results = new List<string>();
    private static GlassShatterPanel near;
    private static GlassShatterPanel far;
    private static string nearId;
    private static string farId;
    private static Vector3 spot;
    private static int exceptionsBefore;
    private static GymRemoteAvatar avatar;

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
        try
        {
            if (now - startedAt > 240d) throw new TimeoutException("stage " + stage + " did not finish");
            guest?.Tick();
            switch (stage)
            {
                case Stage.WaitScene:
                    if (GymTimeOfDay.Instance == null || EnemyFighter.RegisteredFighters.Count == 0 || now - startedAt < 6d ||
                        GymAutoDoor.AllDoors.Count == 0 || UnityEngine.Object.FindFirstObjectByType<GlassShatterPanel>() == null) return;
                    Application.logMessageReceived += CountExceptions;
                    relay = StartRelay();
                    session = GymNetSession.Begin(Url, null);
                    Enter(Stage.HostRoom, now);
                    break;
                case Stage.HostRoom:
                    if (session.Client.CurrentPhase != GymNetClient.Phase.Lobby) return;
                    guest = new GymNetClient();
                    guest.MessageReceived += (from, body) =>
                    {
                        if (body.Length > 0 && body[0] == GymNetWorld.SnapshotTag) snapshots.Add(body);
                        else if (body.Length > 0 && body[0] == GymNetInteractions.ResultTag) results.Add(body);
                    };
                    guest.Join(Url, session.Client.RoomCode);
                    Enter(Stage.GuestJoin, now);
                    break;
                case Stage.GuestJoin:
                    if (guest.CurrentPhase != GymNetClient.Phase.Lobby || session.RemoteAvatarCount != 1 || snapshots.Count < 2) return;
                    CheckSnapshotAndPuppets();
                    PickPanels();
                    guest.Send(0, "p" + JsonUtility.ToJson(new GymNetPose { x = spot.x + 1f, y = spot.y, z = spot.z }));
                    Enter(Stage.Snapshot, now);
                    break;
                case Stage.Snapshot:
                    // The host needs a few frames to place the guest's avatar.
                    if (now - stageAt < 0.6d) return;
                    // Far panel with the impact point faked next to the guest, and a far impact point.
                    guest.Send(0, GymNetInteractions.RequestTag + "glass|" + Payload(farId, spot));
                    guest.Send(0, GymNetInteractions.RequestTag + "glass|" + Payload(farId, far.transform.position));
                    guest.Send(0, GymNetInteractions.RequestTag + "glass|" + Payload("no-such-panel@0,0,0", spot));
                    Enter(Stage.FarRequests, now);
                    break;
                case Stage.FarRequests:
                    if (now - stageAt < 1.0d) return;
                    Require(!far.IsShattered && results.Count == 0, "a request from far away or for an unknown panel changed the world");
                    guest.Send(0, GymNetInteractions.RequestTag + "glass|" + Payload(nearId, spot));
                    Enter(Stage.NearRequest, now);
                    break;
                case Stage.NearRequest:
                    if (results.Count == 0) return;
                    Require(near == null || near.IsShattered, "the host did not break the requested panel");
                    Require(results[0].Contains(nearId.Split('@')[0]), "result names another panel");
                    // The host's fixer repairs the glass: the guest is told and its copy grows back.
                    Require(near.RepairByFixer(), "the host could not repair the broken panel");
                    Enter(Stage.Repair, now);
                    break;
                case Stage.Repair:
                    if (results.Count < 2) { if (now - stageAt > 3d) throw new TimeoutException("the guest was not told about the repair"); return; }
                    Require(results[1].Contains(GymNetInteractions.ResultTag + "repair|" + nearId), "repair message names another panel: " + results[1]);
                    exceptionsBefore = exceptions;
                    // The guest hands its cast to the host: the actors stop
                    // simulating but keep their meshes, so the room is not empty.
                    var adopted = new GymNetActorBodies();
                    var inRoom = new HashSet<EnemyFighter>();
                    foreach (EnemyFighter fighter in EnemyFighter.RegisteredFighters)
                        if (fighter != null && fighter.gameObject.activeInHierarchy) inRoom.Add(fighter);
                    Require(adopted.Adopt() > 0, "no actor was adopted");
                    foreach (EnemyFighter fighter in EnemyFighter.RegisteredFighters)
                    {
                        if (fighter == null) continue;
                        Require(!fighter.enabled, "a fighter still simulates on the guest: " + fighter.name + " active=" + fighter.gameObject.activeInHierarchy + " parent=" + (fighter.transform.parent != null ? fighter.transform.parent.name : "-"));
                        Require(fighter.gameObject.activeInHierarchy == inRoom.Contains(fighter), "an adopted fighter lost its body or came back from outside");
                    }
                    Require(GymVisitorDirector.Instance == null || !GymVisitorDirector.Instance.enabled, "the visitor director still runs on the guest");
                    CheckGuestBodies(adopted);
                    adopted.Clear();
                    avatar = GymRemoteAvatar.Create(77);
                    avatar.AddPose(Time.unscaledTimeAsDouble, new GymNetPose
                    {
                        x = spot.x, y = spot.y + 1.15f, z = spot.z, f = 1.15f, a = GymNetAction.PunchLeft, p = 0.4f
                    });
                    Enter(Stage.Avatar, now);
                    break;
                case Stage.Avatar:
                    if (now - stageAt < 1.5d) return;
                    Require(avatar.HasRealBody && avatar.HasAnimatedBody, "the remote player has no animated body");
                    Require(Mathf.Abs(avatar.FeetY - spot.y) < 0.3f, $"the remote player floats: feet {avatar.FeetY} floor {spot.y}");
                    Require(avatar.ActionPlaying == GymNetAction.PunchLeft, "the remote player does not play the punch");
                    UnityEngine.Object.Destroy(avatar.gameObject);
                    Enter(Stage.Suppress, now);
                    break;
                case Stage.Suppress:
                    if (now - stageAt < 3d) return;
                    Require(exceptions == exceptionsBefore, "exceptions while the guest scene ran without its actors: " + (exceptions - exceptionsBefore));
                    Debug.Log("GYMCHAOS_NET_WORLD_OK snapshot=actors+doors puppets=ok glass=host-decides far=ignored guestWorld=quiet");
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

    private static void CountExceptions(string message, string stack, LogType type)
    {
        if (type == LogType.Exception) exceptions++;
    }

    private static string Payload(string id, Vector3 point) => JsonUtility.ToJson(new HitProbe
    {
        id = id, point = point, normal = Vector3.forward, velocity = Vector3.forward, speed = 10f, multiplier = 1.35f
    });

    private static void PickPanels()
    {
        GlassShatterPanel[] panels = UnityEngine.Object.FindObjectsByType<GlassShatterPanel>(FindObjectsSortMode.None);
        foreach (GlassShatterPanel panel in panels) if (!panel.IsShattered) { near = panel; break; }
        Require(near != null, "no intact panel");
        foreach (GlassShatterPanel panel in panels)
            if (!panel.IsShattered && Vector3.Distance(panel.transform.position, near.transform.position) > GymNetInteractions.MaxReach + 10f) { far = panel; break; }
        Require(far != null, "no distant intact panel");
        nearId = near.StableId;
        farId = far.StableId;
        spot = near.transform.position + Vector3.up * 0.2f;
    }

    private static void CheckSnapshotAndPuppets()
    {
        var actors = new List<GymNetWorld.ActorState>();
        var doors = new Dictionary<string, float>();
        string latest = snapshots[snapshots.Count - 1];
        Require(GymNetWorld.TryDecode(latest, actors, doors), "snapshot did not decode");
        Require(actors.Count > 0 && doors.Count > 0, $"snapshot empty: actors={actors.Count} doors={doors.Count}");
        Require(doors.ContainsKey(GymDoorway.AutoDoorName), "gym door missing from the snapshot");

        // The day clock rides on the snapshot: a guest that drifted (paused, or the host resting on a bench) is pulled back.
        Require(GymNetWorld.TryDecode(latest, actors, doors, out float hostTime, out int hostDay) && hostTime >= 0f, "the snapshot carries no day clock");
        float savedTime = GymTimeOfDay.Instance.Time01;
        int savedDay = GymTimeOfDay.Instance.CurrentDay;
        GymTimeOfDay.Instance.RestoreSession(Mathf.Repeat(hostTime + 0.3f, 1f), hostDay + 2);
        var puppets = new GymNetPuppets();
        double clock = 100d;
        puppets.Apply(latest, clock);
        puppets.Apply(latest, clock + 0.1d);
        puppets.Update(clock + 0.5d, 0.02f);
        Require(GymTimeOfDay.Instance.CurrentDay == hostDay && Mathf.Abs(GymTimeOfDay.Instance.Time01 - hostTime) < 0.02f,
            $"the guest clock was not pulled to the host's: {GymTimeOfDay.Instance.Time01}/{GymTimeOfDay.Instance.CurrentDay} vs {hostTime}/{hostDay}");
        GymTimeOfDay.Instance.RestoreSession(savedTime, savedDay);
        Require(GymAutoDoor.Find(GymDoorway.AutoDoorName).IsNetworkDriven, "a guest door does not follow the host");
        Require(puppets.Count == actors.Count && puppets.TryGetPosition(actors[0].Id, out Vector3 at) &&
            Vector3.Distance(at, actors[0].Pose.Position) < 0.05f, "puppets do not match the snapshot");
        puppets.Update(clock + 5d, 0.02f);
        Require(puppets.Count == 0, "puppets of an actor the host stopped listing were kept");
        // A dead fighter arrives as a body lying down, and standing again when it is revived.
        puppets.Apply("n7,0,1.00,0.00,2.00,90|", clock + 6d);
        puppets.Apply("n7,5,1.00,0.00,2.00,90|", clock + 6.1d);
        Require(GymNetWorld.TryDecode("n7,5,1.00,0.00,2.00,90|", actors, doors) && actors.Count == 1 && actors[0].Kind == GymNetWorld.Kind.Corpse, "a corpse entry did not decode");
        puppets.Update(clock + 6.2d, 0.02f);
        Require(puppets.IsLying(7), "a dead fighter's puppet does not lie down");
        puppets.Apply("n7,0,1.00,0.00,2.00,90|", clock + 6.3d);
        puppets.Update(clock + 6.4d, 0.02f);
        Require(puppets.Count == 1 && !puppets.IsLying(7), "a revived fighter's puppet kept lying");
        puppets.Clear();
        // Bad data from a host is skipped, never thrown on.
        Require(!GymNetWorld.TryDecode("garbage", actors, doors) && GymNetWorld.TryDecode("n1,0,NaN,0,0,0|", actors, doors) && actors.Count == 0,
            "bad snapshot data accepted");
    }

    // A guest draws every host actor as a real animated body, never a capsule, and replays its punches.
    private static void CheckGuestBodies(GymNetActorBodies adopted)
    {
        var actors = new List<GymNetWorld.ActorState>();
        Require(GymNetWorld.TryDecode(snapshots[snapshots.Count - 1], actors, new Dictionary<string, float>()), "snapshot did not decode");
        bool identified = false;
        foreach (GymNetWorld.ActorState actor in actors)
            if (actor.Kind == GymNetWorld.Kind.Fighter && actor.Identity >= 0) identified = true;
        Require(identified, "the snapshot names no fighter identity");
        EnemyFighter sample = null;
        foreach (EnemyFighter fighter in EnemyFighter.RegisteredFighters) if (fighter != null && !fighter.IsPolice && fighter.GetComponentInChildren<MixamoScanRetargetAnimator>(true) != null) { sample = fighter; break; }
        Require(sample != null, "no fighter to copy");
        var puppets = new GymNetPuppets { Bodies = adopted };
        int who = (int)sample.Identity;
        Vector3 at = sample.transform.position;
        string c(float v) => v.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
        // One host actor more than this client has bodies of that identity: the extra one needs a body of its own.
        int same = 0;
        foreach (EnemyFighter fighter in EnemyFighter.RegisteredFighters) if (fighter != null && (int)fighter.Identity == who) same++;
        var snapshot = new System.Text.StringBuilder("n");
        for (int n = 0; n <= same; n++)
            snapshot.Append(n > 0 ? ";" : "").Append(9001 + n).Append(",0,").Append(c(at.x + 3f * n)).Append(',').Append(c(at.y)).Append(',')
                .Append(c(at.z)).Append(",0,").Append(who).Append(',').Append(n == same ? 1 : 0);
        puppets.Apply(snapshot.Append('|').ToString(), Time.unscaledTimeAsDouble);
        puppets.Update(Time.unscaledTimeAsDouble + 0.2d, 0.02f);
        Require(puppets.Count == 0, "a host actor got a capsule stand-in: " + puppets.KindSummaryForVerification);
        Require(adopted.SpawnedCount == 1, "the second actor got no body of its own: " + adopted.SpawnedCount);
        Require(adopted.SpawnedDrawnCount == 1, "the new body is not drawn with its texture");
        Require(adopted.BoundCount == same + 1, "a host actor has no body: " + adopted.BoundCount + " of " + (same + 1));
        Require(adopted.PunchingBodyCount == 1, "the host's punch was not replayed: " + adopted.PunchingBodyCount);
        puppets.Update(Time.unscaledTimeAsDouble + 4d, 0.02f);
        Require(adopted.SpawnedCount == 0, "a body of an actor the host dropped was kept");
    }

    [Serializable]
    private sealed class HitProbe
    {
        public string id;
        public Vector3 point;
        public Vector3 normal;
        public Vector3 velocity;
        public float speed;
        public float multiplier;
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
        Application.logMessageReceived -= CountExceptions;
        if (error != null) Debug.LogError("GYMCHAOS_NET_WORLD_FAILED " + error);
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
