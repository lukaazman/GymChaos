using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Room protocol over the relay: host a room (this client is id 0) or join one
/// by its six-letter code. Messages are small JSON texts; call Tick every frame.
/// </summary>
public sealed class GymNetClient
{
    public const int MaxPlayers = 4;

    public enum Phase { Idle, Connecting, Lobby, Failed, Closed }

    [Serializable]
    private sealed class Envelope
    {
        public string t;
        public string code;
        public string m;
        public string d;
        public int id = -1;
        public int from = -1;
        public int to = -1;
        public int[] peers;
        // Character id for the relay's one-session-per-character lease.
        public string chr;
        // Reservation proof: given with the room, shown to claim the same code again.
        public string token;
    }

    private IGymNetSocket socket;
    private string pendingCode;
    private string characterId;
    private string reserveToken;
    private bool hosting;
    private bool handshakeSent;

    public Phase CurrentPhase { get; private set; }
    public string RoomCode { get; private set; }
    /// <summary>Host: proof of this room, needed to host the same code again after the room closed.</summary>
    public string Token { get; private set; }
    public int LocalId { get; private set; } = -1;
    public string Error { get; private set; }
    public bool IsHost => LocalId == 0;
    public readonly List<int> Peers = new List<int>(MaxPlayers);

    public event Action<int> PeerJoined;
    public event Action<int> PeerLeft;
    /// <summary>Sender id and the message body.</summary>
    public event Action<int, string> MessageReceived;

    public void Host(string url, string character = null, string reserveCode = null, string token = null)
    {
        Begin(url, reserveCode, character, true);
        reserveToken = token;
    }

    public void Join(string url, string code, string character = null) => Begin(url, code, character, false);

    private void Begin(string url, string code, string character, bool host)
    {
        Disconnect();
        hosting = host;
        pendingCode = code;
        reserveToken = null;
        characterId = character;
        handshakeSent = false;
        Error = null;
        Peers.Clear();
        socket = GymNetSocket.Open(url);
        CurrentPhase = Phase.Connecting;
    }

    public void Disconnect()
    {
        socket?.Close();
        socket = null;
        LocalId = -1;
        RoomCode = null;
        Peers.Clear();
        CurrentPhase = Phase.Idle;
    }

    /// <summary>Sends to one player, or to everyone else when <paramref name="to"/> is -1.</summary>
    public void Send(int to, string body)
    {
        if (CurrentPhase != Phase.Lobby) return;
        // Built by hand into one reused buffer: a snapshot goes out many times a second and must not allocate an Envelope and an escaped copy each time.
        envelope.Clear();
        envelope.Append("{\"t\":\"msg\",\"to\":").Append(to < 0 ? "-1" : to.ToString()).Append(",\"d\":\"");
        for (int i = 0; i < body.Length; i++)
        {
            char c = body[i];
            if (c == '"' || c == '\\') envelope.Append('\\').Append(c);
            else if (c < ' ') envelope.Append("\\u").Append(((int)c).ToString("x4"));
            else envelope.Append(c);
        }
        envelope.Append("\"}");
        socket.Send(envelope.ToString());
    }

    private readonly System.Text.StringBuilder envelope = new System.Text.StringBuilder(4096);

    public void Tick()
    {
        if (socket == null) return;
        if (CurrentPhase == Phase.Connecting)
        {
            if (socket.State == 2) { Fail("could not connect"); return; }
            if (socket.State == 1 && !handshakeSent)
            {
                socket.Send(JsonUtility.ToJson(new Envelope { t = hosting ? "host" : "join", code = pendingCode, chr = characterId, token = reserveToken }));
                handshakeSent = true;
            }
        }
        while (socket != null && socket.TryReceive(out string text)) Handle(text);
        if (socket != null && socket.State == 2 && CurrentPhase == Phase.Lobby) CurrentPhase = Phase.Closed;
    }

    private void Handle(string text)
    {
        Envelope m = JsonUtility.FromJson<Envelope>(text);
        switch (m.t)
        {
            case "room":
            case "joined":
                RoomCode = m.code;
                Token = m.token;
                LocalId = m.id;
                if (m.peers != null) foreach (int id in m.peers) Peers.Add(id);
                CurrentPhase = Phase.Lobby;
                break;
            case "peer":
                Peers.Add(m.id);
                PeerJoined?.Invoke(m.id);
                break;
            case "left":
                Peers.Remove(m.id);
                PeerLeft?.Invoke(m.id);
                break;
            case "msg":
                MessageReceived?.Invoke(m.from, m.d);
                break;
            case "closed":
                CurrentPhase = Phase.Closed;
                socket?.Close();
                break;
            case "err":
                Fail(m.m);
                break;
        }
    }

    private void Fail(string message)
    {
        Error = message;
        CurrentPhase = Phase.Failed;
        socket?.Close();
        socket = null;
    }
}
