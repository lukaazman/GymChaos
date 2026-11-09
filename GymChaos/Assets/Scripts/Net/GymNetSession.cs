using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Runs the network side of a game: sends the local player's pose about 12
/// times a second and shows every other player as a <see cref="GymRemoteAvatar"/>.
/// Start with <see cref="Begin"/>. Command line "-gymnet host URL" or
/// "-gymnet join URL CODE" (WebGL page query: net=host, or net=join with code=ABCD,
/// and relay=URL) starts it through <see cref="TryAutoStart"/>.
/// </summary>
public sealed class GymNetSession : MonoBehaviour
{
    /// <summary>Peer-to-peer rooms (WebRTC): no server of ours. A ws:// or wss:// address still reaches a Server/relay room server.</summary>
    public const string DefaultRelayUrl = "peer://";
    public const float SendIntervalSeconds = 1f / 12f;
    private const char PoseTag = 'p';

    public static GymNetSession Instance { get; private set; }

    public readonly GymNetClient Client = new GymNetClient();
    public readonly GymNetJoin Join;
    public readonly GymNetPuppets Puppets = new GymNetPuppets();
    private float nextSnapshot;
    private string latestSnapshot;
    private bool previousRunInBackground;
    private float hostRetryAt;
    private float hostRetryUntil;
    private string lastRoomCode;
    private string lastRoomToken;
    private bool actorsSuppressed;
    private GymNetWorldSnapshot pendingWorld;
    private GymNetJoin.GuestState reportedGuest;
    private readonly Dictionary<int, GymRemoteAvatar> avatars = new Dictionary<int, GymRemoteAvatar>(GymNetClient.MaxPlayers);
    private PlayerMovement player;
    private float nextSend;
    private Vector3 lastPosition;
    private bool hadPosition;
    private float speed;
    private float nextPlayerSearch;
    private GymNetClient.Phase reportedPhase;

    public GymNetSession()
    {
        Join = new GymNetJoin(Client)
        {
            WorldSource = GymSessionService.CaptureWorldSnapshot,
            CharacterSource = GymSessionService.CaptureCharacterForNet,
            WorldSink = world => pendingWorld = world
        };
    }

    public int RemoteAvatarCount => avatars.Count;
    public Dictionary<int, GymRemoteAvatar>.ValueCollection Avatars => avatars.Values;
    public Dictionary<int, GymRemoteAvatar> AvatarsById => avatars;
    /// <summary>The local player once it was found; null before.</summary>
    public PlayerMovement LocalPlayer => player;
    public bool ActorsSuppressed => actorsSuppressed;
    public bool TryGetAvatar(int id, out GymRemoteAvatar avatar) => avatars.TryGetValue(id, out avatar);

    // The room chosen in the session menu; command line and page query still apply when this is unset.
    private static string pendingMode, pendingUrl, pendingCode;
    private Text roomLabel;
    private int roomLabelKey = -1;

    // This session's room: who it plays as and where, so a guest can find the room again.
    private string relayUrl;
    private string joinCode;
    private GymNetReconnect reconnect;
    public GymNetReconnect Reconnect => reconnect;

    // A host that died: the room code and its token are kept so loading its save hosts the same code again.
    public static string RecoveryUrl { get; private set; }
    public static string RecoveryCode { get; private set; }
    private static string recoveryToken;
    public static bool HasRecovery => !string.IsNullOrEmpty(RecoveryCode);
    /// <summary>True while this game is in a room (hosting or joined).</summary>
    public static bool InRoom => Instance != null && Instance.Client.CurrentPhase == GymNetClient.Phase.Lobby;
    // A line for the menu after a room ended on its own (shown once).
    private static string notice;

    public static void ClearRecovery()
    {
        RecoveryUrl = RecoveryCode = recoveryToken = null;
    }

    /// <summary>Host: remember this room before the scene reloads (no-op for anybody else).</summary>
    public static bool RememberRoomForRecovery()
    {
        GymNetSession s = Instance;
        if (s == null || s.joinCode != null || s.Client.CurrentPhase != GymNetClient.Phase.Lobby || !s.Client.IsHost ||
            string.IsNullOrEmpty(s.Client.Token)) return false;
        RecoveryUrl = s.relayUrl;
        RecoveryCode = s.Client.RoomCode;
        recoveryToken = s.Client.Token;
        return true;
    }

    /// <summary>The note left for the menu (and cleared).</summary>
    public static string TakeNotice()
    {
        string text = notice;
        notice = null;
        return text;
    }

    /// <summary>
    /// The local player died. In a room as a guest the death has a menu and the
    /// room stays; true when that took over. A host (or anyone not in a room)
    /// follows the solo death flow, which remembers the host's room.
    /// </summary>
    public static bool HandleLocalDeath(PlayerMovement dead)
    {
        GymNetSession s = Instance;
        if (s == null || s.joinCode == null) return false;
        bool inRoom = s.Client.CurrentPhase == GymNetClient.Phase.Lobby || (s.reconnect != null && s.reconnect.Active);
        if (!inRoom) return false;
        GymPauseMenu.OpenDeathMenu(dead, () => RespawnLocalPlayer());
        return true;
    }

    /// <summary>LOAD GAME after a death in a room: the character from its own save, alive, where it died.</summary>
    public static bool RespawnLocalPlayer()
    {
        GymNetSession s = Instance;
        PlayerMovement p = s != null ? s.player : null;
        if (p == null) p = FindAnyObjectByType<PlayerMovement>();
        if (p == null || !p.IsDead) return false;
        if (!GymSessionService.ReloadCharacterInPlace())
        {
            // The save could not be read (it may be mid-write): the menu stays so LOAD GAME can be tried again.
            Debug.LogWarning("GYMCHAOS_NET_RESPAWN_SAVE_UNREADABLE");
            return false;
        }
        p.RespawnAt(p.LastDeathPosition, p.transform.eulerAngles.y);
        GymPauseMenu.CloseDeathMenu();
        Debug.Log("GYMCHAOS_NET_RESPAWN position=" + p.transform.position);
        return true;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        Instance = null;
        SetPending(null, null, null);
    }

    /// <summary>The menu's room choice: mode "host" or "join" (with a code), null for solo.</summary>
    public static void SetPending(string mode, string url, string code)
    {
        pendingMode = mode;
        pendingUrl = url;
        pendingCode = code;
    }

    /// <summary>Hosts a room (joinCode null) or joins one. Replaces a running session.</summary>
    public static GymNetSession Begin(string url, string joinCode)
    {
        if (Instance == null)
        {
            Instance = new GameObject("Gym Net Session").AddComponent<GymNetSession>();
            if (Application.isPlaying)
            {
                DontDestroyOnLoad(Instance.gameObject);
                Instance.gameObject.AddComponent<GymNetRaceUi>();
            }
        }
        Instance.Reset();
        // A room does not wait for the window to be in front: poses, snapshots and doors keep flowing.
        Instance.previousRunInBackground = Application.runInBackground;
        Application.runInBackground = true;
        GymSessionService.SetContext(joinCode == null ? GymSaveContext.Host : GymSaveContext.Join);
        string character = GymSessionService.Active?.slotId;
        Instance.relayUrl = url;
        Instance.joinCode = joinCode;
        Instance.reconnect = null;
        bool peer = IsPeerUrl(url);
        if (joinCode == null)
        {
            // After a death the host asks for its old code back.
            bool recover = HasRecovery && RecoveryUrl == url;
            if (peer) GymPeerHost.Start(recover ? RecoveryCode : null);
            if (recover && !peer) Instance.Client.Host(url, character, RecoveryCode, recoveryToken);
            else Instance.Client.Host(HostSocketUrl(url), character);
        }
        else
        {
            string dial = peer ? GymPeerHost.GuestScheme + joinCode.ToUpperInvariant() : url;
            Instance.Client.Join(dial, joinCode, character);
            Instance.reconnect = new GymNetReconnect(Instance.Client, dial, joinCode, character);
            Instance.reconnect.Reconnected += Instance.OnReconnected;
        }
        return Instance;
    }

    private static bool IsPeerUrl(string url) => url != null && url.StartsWith("peer", StringComparison.Ordinal);

    // A peer room's host talks to the room logic in its own game.
    private static string HostSocketUrl(string url) => IsPeerUrl(url) ? GymPeerHost.LoopbackUrl : url;

    public static void End()
    {
        if (Instance == null) return;
        Instance.Reset();
        GymPeerHost.Stop();
        Application.runInBackground = Instance.previousRunInBackground;
        // A Join scene never loaded this character's world: it must not write one. Only the menu return clears Join.
        if (GymSessionService.Active == null || GymSessionService.Context != GymSaveContext.Join)
            GymSessionService.SetContext(GymSaveContext.Solo);
        Discard(Instance.gameObject);
        Instance = null;
    }

    /// <summary>The save context this launch asks for: Join when it will join a room, Host when it will host, else Solo.</summary>
    public static GymSaveContext LaunchContext()
    {
        if (!ParseLaunch(out string mode, out _, out _)) return GymSaveContext.Solo;
        return mode == "host" ? GymSaveContext.Host : GymSaveContext.Join;
    }

    /// <summary>Starts a session from the command line or the page URL; false when none was asked for.</summary>
    public static bool TryAutoStart()
    {
        if (Instance != null || !ParseLaunch(out string mode, out string url, out string code)) return false;
        Begin(url, mode == "host" ? null : code);
        return true;
    }

    private static bool ParseLaunch(out string mode, out string url, out string code)
    {
        mode = null;
        url = DefaultRelayUrl;
        code = null;
        string[] args = Environment.GetCommandLineArgs();
        for (int i = 0; i + 1 < args.Length; i++)
        {
            if (args[i] != "-gymnet") continue;
            mode = args[i + 1];
            if (i + 2 < args.Length) url = args[i + 2];
            if (mode == "join" && i + 3 < args.Length) code = args[i + 3];
        }
        string page = Application.absoluteURL;
        int query = string.IsNullOrEmpty(page) ? -1 : page.IndexOf('?');
        if (query >= 0)
        {
            foreach (string pair in page.Substring(query + 1).Split('&'))
            {
                string[] kv = pair.Split('=');
                if (kv.Length != 2) continue;
                if (kv[0] == "net") mode = kv[1];
                else if (kv[0] == "relay") url = PageRelayAllowed(page, Uri.UnescapeDataString(kv[1])) ? Uri.UnescapeDataString(kv[1]) : url;
                else if (kv[0] == "code") code = kv[1];
            }
        }
        if (pendingMode != null)
        {
            mode = pendingMode;
            url = pendingUrl;
            code = pendingCode;
        }
        // The page link is untrusted: only websocket URLs.
        if (!IsPeerUrl(url) && !url.StartsWith("ws://", StringComparison.Ordinal) && !url.StartsWith("wss://", StringComparison.Ordinal)) return false;
        return mode == "host" || (mode == "join" && !string.IsNullOrEmpty(code));
    }

    // A link must not send the player's character to a server of its choosing: loopback or the page's own host only.
    private static bool PageRelayAllowed(string page, string relay)
    {
        try
        {
            Uri relayUri = new Uri(relay);
            return relayUri.IsLoopback || string.Equals(relayUri.Host, new Uri(page).Host, StringComparison.OrdinalIgnoreCase);
        }
        catch (UriFormatException) { return false; }
    }

    // Back in the room after the host was down: the handshake runs again and the world comes again.
    private void OnReconnected()
    {
        Join.Reset();
        Puppets.Clear();
        pendingWorld = null;
        Debug.Log("GYMCHAOS_NET_RECONNECTED code=" + Client.RoomCode);
    }

    private void Reset()
    {
        Client.Disconnect();
        Client.PeerJoined -= OnPeerJoined;
        Client.PeerLeft -= OnPeerLeft;
        Client.MessageReceived -= OnMessage;
        Client.PeerJoined += OnPeerJoined;
        Client.PeerLeft += OnPeerLeft;
        Client.MessageReceived += OnMessage;
        ClearAvatars();
        GymNetRace.Reset();
        Join.Reset();
        Puppets.Clear();
        actorsSuppressed = false;
        pendingWorld = null;
        reportedGuest = GymNetJoin.GuestState.Idle;
        hadPosition = false;
    }

    // Edit-mode verifier runs have no frame loop for Destroy.
    private static void Discard(GameObject target)
    {
        if (Application.isPlaying) Destroy(target); else DestroyImmediate(target);
    }

    private void ClearAvatars()
    {
        foreach (GymRemoteAvatar avatar in avatars.Values) if (avatar != null) Discard(avatar.gameObject);
        avatars.Clear();
    }

    private void OnPeerJoined(int id)
    {
        Debug.Log($"GYMCHAOS_NET_PEER joined id={id} players={Client.Peers.Count + 1}");
        EnsureAvatar(id);
    }

    private void OnPeerLeft(int id)
    {
        Debug.Log($"GYMCHAOS_NET_PEER left id={id}");
        GymNetPvp.Forget(id);
        GymNetInteractions.Forget(id);
        GymNetRace.PeerLeft(id, Client.IsHost);
        if (!avatars.TryGetValue(id, out GymRemoteAvatar avatar)) return;
        if (avatar != null) Discard(avatar.gameObject);
        avatars.Remove(id);
    }

    private GymRemoteAvatar EnsureAvatar(int id)
    {
        // A scene reload destroys avatars but leaves the dictionary entry: a destroyed one counts as missing.
        if (avatars.TryGetValue(id, out GymRemoteAvatar existing) && existing != null) return existing;
        GymRemoteAvatar avatar = GymRemoteAvatar.Create(id);
        avatars[id] = avatar;
        return avatar;
    }

    private void OnMessage(int from, string body)
    {
        if (Join.Handle(from, body)) return;
        if (GymNetInteractions.Handle(this, from, body, Time.unscaledTime)) return;
        if (GymNetPvp.Handle(this, from, body, Time.unscaledTime)) return;
        if (GymNetRace.Handle(this, from, body, Time.unscaledTime)) return;
        if (!string.IsNullOrEmpty(body) && body[0] == GymNetWorld.SnapshotTag)
        {
            // Only the newest one matters: after a stall the older ones are applied for nothing.
            if (!Client.IsHost && from == 0 && actorsSuppressed) latestSnapshot = body;
            return;
        }
        if (string.IsNullOrEmpty(body) || body[0] != PoseTag) return;
        GymNetPose pose;
        try { pose = JsonUtility.FromJson<GymNetPose>(body.Substring(1)); }
        catch (ArgumentException) { return; }
        // A peer is untrusted: reject non-finite or absurd positions.
        if (!IsFinite(pose.x) || !IsFinite(pose.y) || !IsFinite(pose.z) || !IsFinite(pose.r) || !IsFinite(pose.s)) return;
        EnsureAvatar(from).AddPose(Time.unscaledTimeAsDouble, pose);
    }

    private static bool IsFinite(float value) => !float.IsNaN(value) && Mathf.Abs(value) < 100000f;

    // The host reads the code from here to tell friends; guests see which room they are in.
    private void UpdateRoomLabel()
    {
        if (!Application.isPlaying) return;
        bool reconnecting = reconnect != null && reconnect.Active;
        bool show = (reconnecting || (Client.CurrentPhase == GymNetClient.Phase.Lobby && !string.IsNullOrEmpty(Client.RoomCode))) &&
            !GymStartScreen.IsMenuVisible;
        if (roomLabel == null)
        {
            if (!show) return;
            GameObject canvasObject = new GameObject("Room Label", typeof(Canvas));
            canvasObject.transform.SetParent(transform, false);
            canvasObject.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            Font font = PersonaMenuStyle.LoadButtonFont(Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"));
            roomLabel = GymRuntimeSettings.CreateText("Room Text", canvasObject.transform, font, string.Empty, 34,
                PersonaMenuStyle.Ink, FontStyle.Normal, TextAnchor.UpperCenter, new Vector2(0.3f, 0.93f), new Vector2(0.7f, 0.99f));
            roomLabel.raycastTarget = false;
        }
        roomLabel.transform.parent.gameObject.SetActive(show);
        if (!show) return;
        // Only rebuilt when the text would change (a count or a second), not every frame.
        int key = reconnecting ? 1000 + Mathf.FloorToInt(reconnect.Elapsed(Time.unscaledTime)) : avatars.Count + 1;
        if (key == roomLabelKey && roomLabel.text.Length > 0) return;
        roomLabelKey = key;
        roomLabel.text = reconnecting
            ? "HOST DOWN - RECONNECTING " + (key - 1000) + "s"
            : "ROOM " + Client.RoomCode + "   " + key + "/" + GymNetClient.MaxPlayers;
    }

    private void Update()
    {
        GymPeerHost.Current?.Pump();
        Client.Tick();
        if (Client.CurrentPhase != reportedPhase)
        {
            reportedPhase = Client.CurrentPhase;
            Debug.Log($"GYMCHAOS_NET_STATE phase={reportedPhase} id={Client.LocalId} code={Client.RoomCode} error={Client.Error}");
        }
        if (joinCode == null && Client.CurrentPhase == GymNetClient.Phase.Lobby && HasRecovery) ClearRecovery();
        if (Client.CurrentPhase == GymNetClient.Phase.Lobby && !string.IsNullOrEmpty(Client.Token))
        {
            lastRoomCode = Client.RoomCode;
            lastRoomToken = Client.Token;
            hostRetryUntil = 0f;
        }
        // The room never formed or was refused (wrong code, full, character in use, relay unreachable): say why and leave the room mode.
        if (Client.CurrentPhase == GymNetClient.Phase.Failed && (reconnect == null || !reconnect.Active) && GymSessionService.HasActive &&
            (hostRetryUntil <= 0f || Time.unscaledTime >= hostRetryUntil))
        {
            string reason = string.IsNullOrEmpty(Client.Error) ? "UNKNOWN" : Client.Error.ToUpperInvariant();
            // Nothing on this machine starts a relay: "could not connect" is
            // an empty address, never a room the host did wrong.
            notice = reason == "COULD NOT CONNECT" ? "ROOM NOT FOUND OR BLOCKED" : "ROOM FAILED - " + reason;
            GymSessionService.ReturnToMenu();
            return;
        }
        // A host whose connection dropped asks for its own room code back, for a while.
        if (joinCode == null && !string.IsNullOrEmpty(lastRoomToken) &&
            (Client.CurrentPhase == GymNetClient.Phase.Closed || (Client.CurrentPhase == GymNetClient.Phase.Failed && hostRetryUntil > 0f)))
        {
            if (hostRetryUntil <= 0f) { hostRetryUntil = Time.unscaledTime + 60f; hostRetryAt = Time.unscaledTime; }
            if (Time.unscaledTime >= hostRetryAt && Time.unscaledTime < hostRetryUntil)
            {
                hostRetryAt = Time.unscaledTime + 3f;
                Client.Host(HostSocketUrl(relayUrl), GymSessionService.Active?.slotId, lastRoomCode, lastRoomToken);
            }
        }
        if (reconnect != null && GymSessionService.HasActive)
        {
            reconnect.Tick(Time.unscaledTime);
            if (reconnect.GaveUp)
            {
                // The host did not come back in time: leave with a reason the menu shows.
                notice = "HOST NOT BACK - " + reconnect.GiveUpReason.ToUpperInvariant();
                GymSessionService.ReturnToMenu();
                return;
            }
        }
        UpdateRoomLabel();
        if (Client.CurrentPhase != GymNetClient.Phase.Lobby)
        {
            if (avatars.Count > 0) ClearAvatars();
            if (actorsSuppressed)
            {
                // The host is gone: its puppets and door control go with it.
                Puppets.Clear();
                GymNetWorld.ReleaseDoors();
            }
            return;
        }
        Join.Tick(Time.unscaledTime);
        if (pendingWorld != null && GymArenaBootstrap.IsGameplayStarted && GymSessionService.ApplyWorldSnapshot(pendingWorld))
        {
            pendingWorld = null;
            // The host runs everyone from here on: this scene stops simulating them.
            if (!actorsSuppressed)
            {
                actorsSuppressed = true;
                Debug.Log("GYMCHAOS_NET_ACTORS_SUPPRESSED count=" + GymNetWorld.SuppressLocalActors());
            }
        }
        if (latestSnapshot != null)
        {
            Puppets.Apply(latestSnapshot, Time.unscaledTimeAsDouble);
            latestSnapshot = null;
        }
        if (actorsSuppressed) Puppets.Update(Time.unscaledTimeAsDouble, Time.unscaledDeltaTime);
        if (Client.IsHost && Client.Peers.Count > 0 && Time.unscaledTime >= nextSnapshot)
        {
            nextSnapshot = Time.unscaledTime + GymNetWorld.SnapshotIntervalSeconds;
            Client.Send(-1, GymNetWorld.EncodeSnapshot());
        }
        if (Join.Guest != reportedGuest)
        {
            reportedGuest = Join.Guest;
            Debug.Log($"GYMCHAOS_NET_JOIN guest={reportedGuest} reason={Join.RejectReason}");
            if (reportedGuest == GymNetJoin.GuestState.Rejected)
            {
                notice = "JOIN REFUSED - " + (string.IsNullOrEmpty(Join.RejectReason) ? "UNKNOWN" : Join.RejectReason.ToUpperInvariant());
                GymSessionService.ReturnToMenu();
                return;
            }
        }
        // Players already in the room when this client joined.
        for (int i = 0; i < Client.Peers.Count; i++) EnsureAvatar(Client.Peers[i]);
        if (Time.unscaledTime < nextSend) return;
        nextSend = Time.unscaledTime + SendIntervalSeconds;
        if (player == null)
        {
            if (Time.unscaledTime < nextPlayerSearch) return;
            nextPlayerSearch = Time.unscaledTime + 1f;
            player = FindAnyObjectByType<PlayerMovement>();
            if (player == null) return;
        }
        Vector3 position = player.transform.position;
        if (hadPosition) speed = Vector3.ProjectOnPlane(position - lastPosition, Vector3.up).magnitude / SendIntervalSeconds;
        lastPosition = position;
        hadPosition = true;
        GymNetPose pose = new GymNetPose { x = position.x, y = position.y, z = position.z, r = player.transform.eulerAngles.y, s = speed,
            h = Mathf.Clamp01(player.CurrentHealth / Mathf.Max(1f, player.MaxHealth)),
            d = player.IsDead ? 1f : 0f };
        Client.Send(-1, PoseTag + JsonUtility.ToJson(pose));
    }

    private void OnDestroy()
    {
        Client.Disconnect();
        if (Instance == this) Instance = null;
    }
}
