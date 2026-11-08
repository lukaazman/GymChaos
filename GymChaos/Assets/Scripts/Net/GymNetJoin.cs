using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Join handshake on top of <see cref="GymNetClient"/>. The guest sends its
/// character ('c'); the host validates it, keeps a last-known copy and answers
/// with the world snapshot ('w') or a refusal ('r'). The guest applies the
/// snapshot to its scene only; its own save is never given the host's world.
/// A guest repeats 'c' now and then so the host's copy stays current.
/// </summary>
public sealed class GymNetJoin
{
    public const char CharacterTag = 'c';
    public const char WorldTag = 'w';
    public const char RejectTag = 'r';
    public const float ResendSeconds = 30f;

    public enum GuestState { Idle, Sent, Joined, Rejected }

    private readonly GymNetClient client;
    private readonly HashSet<int> welcomed = new HashSet<int>();
    private float nextResend;

    // Host: where the world comes from. Guest: where the character comes from and where the world goes.
    public Func<GymNetWorldSnapshot> WorldSource;
    public Func<GymCharacterSave> CharacterSource;
    public Action<GymNetWorldSnapshot> WorldSink;

    public GuestState Guest { get; private set; }
    public string RejectReason { get; private set; }
    /// <summary>Host side: the last clean character each guest sent, by player id. Kept after the guest leaves.</summary>
    public readonly Dictionary<int, GymCharacterSave> LastKnown = new Dictionary<int, GymCharacterSave>(GymNetClient.MaxPlayers);

    public GymNetJoin(GymNetClient client)
    {
        this.client = client;
        // A freed player id is reused by the relay: the next guest on it is new.
        client.PeerLeft += id => welcomed.Remove(id);
    }

    /// <summary>Guest: sends the character to the host.</summary>
    public void SendCharacter()
    {
        if (client.IsHost) return;
        GymCharacterSave save = CharacterSource?.Invoke();
        if (save == null) return;
        client.Send(0, CharacterTag + GymNetCharacter.ToJson(save));
        if (Guest == GuestState.Idle) Guest = GuestState.Sent;
    }

    /// <summary>Guest: call each frame once in the lobby; resends the character periodically.</summary>
    public void Tick(float now)
    {
        if (client.CurrentPhase != GymNetClient.Phase.Lobby || client.IsHost) return;
        if (Guest == GuestState.Idle || (Guest == GuestState.Joined && now >= nextResend))
        {
            nextResend = now + ResendSeconds;
            SendCharacter();
        }
    }

    public void Reset()
    {
        Guest = GuestState.Idle;
        RejectReason = null;
        nextResend = 0f;
        LastKnown.Clear();
        welcomed.Clear();
    }

    // A host's text goes into logs: printable ASCII only, short.
    private static string Printable(string text)
    {
        var clean = new System.Text.StringBuilder(GymNetCharacter.MaxTextLength);
        foreach (char c in text)
        {
            if (clean.Length >= GymNetCharacter.MaxTextLength) break;
            if (c >= ' ' && c < 127) clean.Append(c);
        }
        return clean.ToString();
    }

    /// <summary>True when the message was part of the handshake.</summary>
    public bool Handle(int from, string body)
    {
        if (string.IsNullOrEmpty(body)) return false;
        switch (body[0])
        {
            case CharacterTag when client.IsHost:
                if (!GymNetCharacter.TryValidate(body.Substring(1), out GymCharacterSave clean, out string reason))
                {
                    client.Send(from, RejectTag + reason);
                    return true;
                }
                bool first = welcomed.Add(from);
                LastKnown[from] = clean;
                if (first && WorldSource != null) client.Send(from, WorldTag + JsonUtility.ToJson(WorldSource()));
                return true;
            case WorldTag when !client.IsHost && from == 0 && Guest == GuestState.Sent:
                GymNetWorldSnapshot world = GymNetCharacter.CleanWorld(body.Substring(1));
                if (world == null)
                {
                    // An unreadable world would leave this guest in a duplicate world: leave instead.
                    RejectReason = "bad world";
                    Guest = GuestState.Rejected;
                    return true;
                }
                WorldSink?.Invoke(world);
                Guest = GuestState.Joined;
                nextResend = Time.unscaledTime + ResendSeconds;
                return true;
            case RejectTag when !client.IsHost && from == 0:
                RejectReason = Printable(body.Substring(1));
                Guest = GuestState.Rejected;
                return true;
        }
        return false;
    }
}
