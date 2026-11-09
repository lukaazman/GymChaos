using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A free public PeerJS broker (0.peerjs.com) used only to swap the WebRTC offer, answer and ICE
/// candidates between two players; the game itself never goes through it. Each player registers one
/// id; messages carry the sender id, a connection id and an opaque signal text.
/// </summary>
public sealed class GymPeerSignal
{
    public const string Broker = "wss://0.peerjs.com/peerjs?key=peerjs";
    private const float HeartbeatSeconds = 5f;
    private const string Chars = "abcdefghijklmnopqrstuvwxyz0123456789";

    // The broker closes a connection whose message is not shaped like a PeerJS ICE candidate, so every
    // signal travels as one: the signal text rides in candidate.candidate, the connection id beside it.
    [Serializable]
    private sealed class Candidate
    {
        public string candidate;
        public string sdpMid = "0";
        public int sdpMLineIndex;
    }

    [Serializable]
    private sealed class Payload
    {
        public Candidate candidate;
        public string type = "data";
        public string connectionId;
        public string msg;
    }

    [Serializable]
    private sealed class Envelope
    {
        public string type;
        public string src;
        public string dst;
        public Payload payload;
    }

    public struct Incoming
    {
        public string Type;
        public string Src;
        public string Cid;
        public string Data;
    }

    private readonly IGymNetSocket socket;
    private readonly Queue<Incoming> inbox = new Queue<Incoming>();
    private float nextBeat;

    public string Id { get; }
    /// <summary>0 connecting, 1 registered, 2 failed or closed.</summary>
    public int State { get; private set; }
    public bool IdTaken { get; private set; }

    public GymPeerSignal(string id)
    {
        Id = id;
        socket = GymNetSocket.Open(Broker + "&id=" + id + "&token=" + RandomText(12));
    }

    public static string RandomText(int length)
    {
        char[] text = new char[length];
        for (int i = 0; i < length; i++) text[i] = Chars[UnityEngine.Random.Range(0, Chars.Length)];
        return new string(text);
    }

    public void Pump()
    {
        if (State == 2) return;
        while (socket.TryReceive(out string text))
        {
            Envelope m;
            try { m = JsonUtility.FromJson<Envelope>(text); }
            catch (ArgumentException) { continue; }
            if (m == null || m.type == null) continue;
            switch (m.type)
            {
                case "OPEN":
                    State = 1;
                    nextBeat = Time.realtimeSinceStartup + HeartbeatSeconds;
                    break;
                case "ID-TAKEN":
                    IdTaken = true;
                    Close();
                    return;
                case "INVALID-KEY":
                case "ERROR":
                    Close();
                    return;
                default:
                    inbox.Enqueue(new Incoming { Type = m.type, Src = m.src, Cid = m.payload?.connectionId, Data = m.payload?.candidate?.candidate });
                    break;
            }
        }
        if (socket.State == 2) State = 2;
        else if (State == 1 && Time.realtimeSinceStartup >= nextBeat)
        {
            nextBeat = Time.realtimeSinceStartup + HeartbeatSeconds;
            socket.Send("{\"type\":\"HEARTBEAT\"}");
        }
    }

    public bool TryTake(out Incoming message)
    {
        if (inbox.Count == 0)
        {
            message = default;
            return false;
        }
        message = inbox.Dequeue();
        return true;
    }

    public void Send(string type, string dst, string cid, string data)
    {
        if (State != 1) return;
        string text = JsonUtility.ToJson(new Envelope { type = type, dst = dst, payload = new Payload { connectionId = cid, candidate = new Candidate { candidate = data } } });
        socket.Send(text);
    }

    public void Close()
    {
        State = 2;
        socket.Close();
    }
}
