using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The room logic of Server/relay/relay.js, running inside the host's game: the host is player 0,
/// guests get ids 1..3, messages are forwarded to one player or to everyone else. Every link is a
/// text socket: the host's own client (loopback) or a guest's WebRTC data channel.
/// </summary>
public sealed class GymPeerRelay
{
    public const int MaxPlayers = 4;

    [Serializable]
    private sealed class Msg
    {
        public string t;
        public string code;
        public string m;
        public string d;
        public int id = -1;
        public int from = -1;
        public int to = -1;
        public int[] peers;
        public string chr;
        public string token;
    }

    private sealed class Link
    {
        public IGymNetSocket Socket;
        public int Pid = -1;
        public string Lease;
        public bool WaitingHost;
        public string Character;
    }

    private readonly GymPeerHost host;
    private readonly List<Link> links = new List<Link>();
    private readonly Dictionary<int, Link> players = new Dictionary<int, Link>();
    private readonly Dictionary<string, Link> leases = new Dictionary<string, Link>();

    public GymPeerRelay(GymPeerHost host) => this.host = host;

    public int PlayerCount => players.Count;

    public void Add(IGymNetSocket socket) => links.Add(new Link { Socket = socket });

    public void Tick()
    {
        for (int i = links.Count - 1; i >= 0; i--)
        {
            Link link = links[i];
            while (link.Socket.TryReceive(out string text)) Handle(link, text);
            if (link.WaitingHost) TryOpenRoom(link);
            if (link.Socket.State == 2)
            {
                Leave(link);
                links.RemoveAt(i);
            }
        }
    }

    public void CloseAll()
    {
        foreach (Link link in links) link.Socket.Close();
        links.Clear();
        players.Clear();
        leases.Clear();
    }

    private static void Send(Link link, Msg message) => link.Socket.Send(JsonUtility.ToJson(message));

    private static void Refuse(Link link, string why) => Send(link, new Msg { t = "err", m = why });

    private bool TakeLease(Link link, string character)
    {
        if (string.IsNullOrEmpty(character)) return true;
        if (character.Length > 64) return false;
        if (leases.TryGetValue(character, out Link holder) && holder != link && holder.Socket.State == 1) return false;
        leases[character] = link;
        link.Lease = character;
        return true;
    }

    private void ReleaseLease(Link link)
    {
        if (link.Lease != null && leases.TryGetValue(link.Lease, out Link holder) && holder == link) leases.Remove(link.Lease);
        link.Lease = null;
    }

    private void Handle(Link link, string text)
    {
        Msg m;
        try { m = JsonUtility.FromJson<Msg>(text); }
        catch (ArgumentException) { Refuse(link, "bad json"); return; }
        if (m == null || m.t == null) { Refuse(link, "bad message"); return; }
        if (m.t == "host" && link.Pid < 0 && !players.ContainsKey(0))
        {
            link.WaitingHost = true;
            link.Character = m.chr;
        }
        else if (m.t == "join" && link.Pid < 0) Join(link, m);
        else if (m.t == "msg" && link.Pid >= 0 && m.d != null) Forward(link, m);
    }

    // The room opens once the code is registered with the signaling broker (it is the address guests dial).
    private void TryOpenRoom(Link link)
    {
        if (host.Failed)
        {
            link.WaitingHost = false;
            Refuse(link, "room server unreachable");
            return;
        }
        if (!host.Ready) return;
        link.WaitingHost = false;
        if (!TakeLease(link, link.Character)) { Refuse(link, "character in use"); return; }
        link.Pid = 0;
        players[0] = link;
        Send(link, new Msg { t = "room", code = host.Code, id = 0, token = host.Code });
    }

    private void Join(Link link, Msg m)
    {
        string wanted = (m.code ?? string.Empty).ToUpperInvariant();
        if (!players.ContainsKey(0) || wanted != host.Code) { Refuse(link, "no such room"); return; }
        if (players.Count >= MaxPlayers) { Refuse(link, "room full"); return; }
        if (!TakeLease(link, m.chr)) { Refuse(link, "character in use"); return; }
        int id = 1;
        while (players.ContainsKey(id)) id++;
        foreach (Link peer in players.Values) Send(peer, new Msg { t = "peer", id = id });
        int[] existing = new int[players.Count];
        players.Keys.CopyTo(existing, 0);
        link.Pid = id;
        Send(link, new Msg { t = "joined", code = host.Code, id = id, peers = existing });
        players[id] = link;
    }

    private void Forward(Link from, Msg m)
    {
        Msg outgoing = new Msg { t = "msg", from = from.Pid, d = m.d };
        if (m.to < 0)
        {
            foreach (KeyValuePair<int, Link> peer in players) if (peer.Key != from.Pid) Send(peer.Value, outgoing);
        }
        else if (players.TryGetValue(m.to, out Link target) && target != from) Send(target, outgoing);
    }

    private void Leave(Link link)
    {
        ReleaseLease(link);
        if (link.Pid < 0) return;
        players.Remove(link.Pid);
        if (link.Pid == 0)
        {
            foreach (Link peer in players.Values)
            {
                Send(peer, new Msg { t = "closed" });
                peer.Pid = -1;
                ReleaseLease(peer);
            }
            players.Clear();
        }
        else foreach (Link peer in players.Values) Send(peer, new Msg { t = "left", id = link.Pid });
        link.Pid = -1;
    }
}
