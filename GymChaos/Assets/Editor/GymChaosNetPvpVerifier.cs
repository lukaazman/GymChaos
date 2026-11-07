#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Debug = UnityEngine.Debug;

/// <summary>
/// Multiplayer phase 4: player against player, in the real scene against the
/// real relay (this scene is the host and its own player is a target; a plain
/// client is the guest).
/// - A guest punch within range and facing hurts the host player by the base
///   damage; a second punch inside the cooldown, a punch from too far and a
///   punch facing away do nothing.
/// - A punch from another player can kill (no health floor); the player can be put back on its feet.
/// - A host punch reaches the guest as a damage message; a damage message
///   forged by a guest does nothing to the host.
/// - The guest's avatar shows a health bar once it is hurt.
/// </summary>
public static class GymChaosNetPvpVerifier
{
    private const string RequestedKey = "GymChaos.NetPvpVerificationRequested";
    private const int Port = 18790;
    private const string Url = "ws://127.0.0.1:18790";

    private enum Stage { WaitScene, HostRoom, GuestJoin, Close, Burst, Far, Away, Lethal, HostPunch, Forged, Label, Done }

    private static Stage stage;
    private static double startedAt;
    private static double stageAt;
    private static bool finished;
    private static int resultCode = 1;
    private static Process relay;
    private static GymNetSession session;
    private static GymNetClient guest;
    private static PlayerMovement player;
    private static Vector3 hostPos;
    private static float baseline;
    private static readonly List<string> damages = new List<string>();
    private static readonly FieldInfo HealthField =
        typeof(PlayerMovement).GetField("currentHealth", BindingFlags.Instance | BindingFlags.NonPublic);

    [InitializeOnLoadMethod]
    private static void ResumeAfterReload()
    {
        if (SessionState.GetBool(RequestedKey, false)) Hook();
    }

    public static void Run()
    {
        finished = false;
        farDone = awayDone = lethalDone = false;
        damages.Clear();
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

    private static float Health => player.CurrentHealth;

    private static void SetHealth(float value) => HealthField.SetValue(player, value);

    // Guest pose: 'distance' metres from the host player along +X, facing the host unless turned away.
    private static void PlaceGuest(float distance, bool facingHost, float health = 1f)
    {
        Vector3 at = hostPos + Vector3.right * distance;
        guest.Send(0, "p" + JsonUtility.ToJson(new GymNetPose { x = at.x, y = at.y, z = at.z, r = facingHost ? 270f : 90f, h = health }));
    }

    private static void AttackHost() =>
        guest.Send(0, GymNetPvp.AttackTag + JsonUtility.ToJson(new AimProbe { dx = -1f, dy = 0f, dz = 0f }));

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
                    player = UnityEngine.Object.FindAnyObjectByType<PlayerMovement>();
                    if (player == null || GymTimeOfDay.Instance == null || now - startedAt < 6d || player.IsDead) return;
                    relay = StartRelay();
                    session = GymNetSession.Begin(Url, null);
                    Enter(Stage.HostRoom, now);
                    break;
                case Stage.HostRoom:
                    if (session.Client.CurrentPhase != GymNetClient.Phase.Lobby) return;
                    guest = new GymNetClient();
                    guest.MessageReceived += (from, body) => { if (body.Length > 0 && body[0] == GymNetPvp.DamageTag) damages.Add(body); };
                    guest.Join(Url, session.Client.RoomCode);
                    Enter(Stage.GuestJoin, now);
                    break;
                case Stage.GuestJoin:
                    if (guest.CurrentPhase != GymNetClient.Phase.Lobby || session.RemoteAvatarCount != 1) return;
                    hostPos = player.transform.position;
                    SetHealth(100f);
                    PlaceGuest(1.5f, true);
                    Enter(Stage.Close, now);
                    break;
                case Stage.Close:
                    if (now - stageAt < 0.8d) return;
                    baseline = Health;
                    AttackHost();
                    Enter(Stage.Burst, now);
                    break;
                case Stage.Burst:
                    if (now - stageAt < 0.6d) return;
                    Within(baseline - Health, 3.5f, 5.5f, "a punch in range and facing did not hurt the host player");
                    baseline = Health;
                    // Two punches at once: the cooldown lets one through.
                    AttackHost();
                    AttackHost();
                    Enter(Stage.Far, now);
                    break;
                case Stage.Far:
                    if (now - stageAt < 0.6d) return;
                    Within(baseline - Health, 3.5f, 5.5f, "the cooldown did not stop the second punch (or the first was lost): " + (baseline - Health));
                    PlaceGuest(6f, true);
                    baseline = Health;
                    Enter(Stage.Away, now);
                    break;
                case Stage.Away:
                    if (now - stageAt < 0.8d) return;
                    if (stageAt > 0 && baseline >= 0 && farDone == false)
                    {
                        farDone = true;
                        AttackHost();
                        stageAt = now;
                        return;
                    }
                    Within(baseline - Health, -0.5f, 0.5f, "a punch from 6 m hurt");
                    PlaceGuest(1.5f, false);
                    baseline = Health;
                    Enter(Stage.Lethal, now);
                    break;
                case Stage.Lethal:
                    if (now - stageAt < 0.8d) return;
                    if (!awayDone)
                    {
                        awayDone = true;
                        AttackHost();
                        stageAt = now;
                        return;
                    }
                    Within(baseline - Health, -0.5f, 0.5f, "a punch from a guest facing away hurt");
                    PlaceGuest(1.5f, true);
                    SetHealth(3f);
                    Enter(Stage.HostPunch, now);
                    break;
                case Stage.HostPunch:
                    if (now - stageAt < 0.8d) return;
                    if (!lethalDone)
                    {
                        lethalDone = true;
                        // Health comes back quickly: put it low right at the punch.
                        SetHealth(2f);
                        AttackHost();
                        stageAt = now;
                        return;
                    }
                    Require(player.IsDead, "a player punch with more damage than health left did not kill the host player: " + Health);
                    player.RespawnAt(hostPos, 0f);
                    Require(!player.IsDead && player.CurrentHealth > 1f, "the host player did not come back");
                    SetHealth(100f);
                    // The host punches the guest (+X of the host player).
                    GymNetPvp.LocalPunch(hostPos + Vector3.up * GymNetPvp.EyeAboveRoot, Vector3.right);
                    Enter(Stage.Forged, now);
                    break;
                case Stage.Forged:
                    if (damages.Count == 0)
                    {
                        if (now - stageAt > 3d) throw new TimeoutException("the guest never got the host's punch");
                        return;
                    }
                    Require(damages[0].Contains("\"amount\":5.0,"), "damage message: " + damages[0]);
                    baseline = Health;
                    guest.Send(0, GymNetPvp.DamageTag + "{\"amount\":20}");
                    Enter(Stage.Label, now);
                    break;
                case Stage.Label:
                    if (now - stageAt < 0.6d) return;
                    Within(baseline - Health, -8f, 0.5f, "a damage message from a guest hurt the host");
                    PlaceGuest(1.5f, true, 0.5f);
                    Enter(Stage.Done, now);
                    break;
                case Stage.Done:
                    if (now - stageAt < 1.2d) return;
                    session.TryGetAvatar(1, out GymRemoteAvatar avatar);
                    ScreenSpaceCharacterLabel label = avatar != null ? avatar.GetComponent<ScreenSpaceCharacterLabel>() : null;
                    Require(label != null && Mathf.Abs(label.StandaloneHealthFraction - 0.5f) < 0.05f,
                        "the hurt guest has no health bar: " + (label != null ? label.StandaloneHealthFraction.ToString() : "no label"));
                    Debug.Log("GYMCHAOS_NET_PVP_OK hit=5 cooldown=ok range=ok facing=ok kills=yes hostPunch=ok forged=ignored bar=ok");
                    Finish(0, null);
                    break;
            }
        }
        catch (Exception exception)
        {
            Finish(1, exception.ToString());
        }
    }

    private static bool farDone;
    private static bool awayDone;
    private static bool lethalDone;

    [Serializable]
    private sealed class AimProbe
    {
        public float dx, dy, dz;
    }

    private static void Within(float value, float min, float max, string message)
    {
        if (value < min || value > max) throw new InvalidOperationException($"{message} (delta {value:F2}, wanted {min}..{max})");
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
        if (error != null) Debug.LogError("GYMCHAOS_NET_PVP_FAILED " + error);
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
