using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Peer-to-peer rooms. The host's game is the room server (<see cref="GymPeerRelay"/>); a guest dials
/// the host over a WebRTC data channel. A free public broker only introduces the two, using the
/// room code as the host's address. No server of ours is involved.
/// </summary>
public sealed class GymPeerHost
{
    public const string LoopbackUrl = "peer-host:";
    public const string GuestScheme = "peer://";
    public const string HelloText = "{\"t\":\"hello\"}";
    private const string CodeLetters = "ABCDEFGHJKLMNPQRSTUVWXYZ";
    private const int CodeLength = 6;
    private const float PendingTimeout = 25f;
    private const float RegisterTimeout = 15f;
    private const float ReRegisterSeconds = 3f;
    private const float OfferResendSeconds = 2f;
    // A host with no players gives up after this many dead registrations (a blocked broker).
    private const int ReRegisterAttempts = 5;

    private sealed class Pending
    {
        public string Src;
        public string Cid;
        public IGymRtcLink Link;
        public float Born;
        // Kept so the answer can be repeated: a signal the broker drops while the
        // host is re-registering would otherwise cost the guest its whole 25 s wait.
        public string Offer;
        public string Answer;
        public float NextResend;
    }

    public static GymPeerHost Current { get; private set; }

    public readonly GymPeerRelay Relay;
    private readonly List<Pending> pending = new List<Pending>();
    private GymPeerSignal signal;
    private float registerStarted;
    private float reRegisterAt;
    private int codeAttempts;
    private int registerFailures;

    public string Code { get; private set; }
    /// <summary>The code is registered with the broker: guests can dial it.</summary>
    public bool Ready => signal != null && signal.State == 1;
    /// <summary>The broker could not be reached at all.</summary>
    public bool Failed { get; private set; }

    public static string IdFor(string code) => "gymchaos-v1-" + code.ToUpperInvariant();

    public static void Start(string preferredCode = null)
    {
        Stop();
        Current = new GymPeerHost(preferredCode);
    }

    public static void Stop()
    {
        Current?.Dispose();
        Current = null;
    }

    private GymPeerHost(string preferredCode)
    {
        Relay = new GymPeerRelay(this);
        Code = string.IsNullOrEmpty(preferredCode) ? NewCode() : preferredCode.ToUpperInvariant();
        if (!GymRtc.Available)
        {
            // The page is not a secure context, or the browser has WebRTC blocked:
            // no guest could ever be introduced, so the room says so instead of waiting.
            Failed = true;
            GymLog.Diag("GYMCHAOS_PEER_NO_WEBRTC code=" + Code);
            return;
        }
        Register();
    }

    private static string NewCode()
    {
        char[] code = new char[CodeLength];
        for (int i = 0; i < CodeLength; i++) code[i] = CodeLetters[UnityEngine.Random.Range(0, CodeLetters.Length)];
        return new string(code);
    }

    private void Register()
    {
        signal?.Close();
        signal = new GymPeerSignal(IdFor(Code));
        registerStarted = Time.realtimeSinceStartup;
    }

    /// <summary>The host's own client: a socket pair into the relay.</summary>
    public IGymNetSocket OpenLoopback()
    {
        GymLoopbackSocket.Pair(out GymLoopbackSocket client, out GymLoopbackSocket server);
        Relay.Add(server);
        return client;
    }

    public void Pump()
    {
        PumpSignal();
        PumpGuests();
        Relay.Tick();
    }

    private void PumpSignal()
    {
        if (signal == null) return;
        signal.Pump();
        if (signal.State == 1) registerFailures = 0;
        if (signal.State == 0)
        {
            if (Time.realtimeSinceStartup - registerStarted <= RegisterTimeout) return;
            signal.Close();
        }
        if (signal.State != 2) return;
        // Taken id (a two-in-a-hundred-million clash): pick another code, up to three times, while nobody has it yet.
        if (signal.IdTaken)
        {
            if (Relay.PlayerCount == 0 && ++codeAttempts <= 3)
            {
                Code = NewCode();
                Register();
                GymLog.Diag("GYMCHAOS_PEER_CODE_RETRY code=" + Code);
                return;
            }
            if (Relay.PlayerCount == 0) Failed = true;
            GymLog.Diag($"GYMCHAOS_PEER_ID_TAKEN code={Code} players={Relay.PlayerCount}");
            // shortcut: a held code needs a new one that guests can read, and a running
            // room cannot show it yet; only reachable on a real id clash (rare).
            return;
        }
        // The registration is gone (the broker dropped it, or it never answered).
        // Registering again on a healthy socket every few seconds used to leave a
        // window in which a guest's offer reached nobody and the join timed out;
        // now the address is only claimed again when it is really lost.
        if (++registerFailures > ReRegisterAttempts && Relay.PlayerCount == 0)
        {
            Failed = true;
            GymLog.Diag($"GYMCHAOS_PEER_BROKER_UNREACHABLE code={Code} attempts={registerFailures}");
            return;
        }
        if (Time.realtimeSinceStartup < reRegisterAt) return;
        reRegisterAt = Time.realtimeSinceStartup + ReRegisterSeconds;
        Register();
        GymLog.Diag($"GYMCHAOS_PEER_RE_REGISTER code={Code} players={Relay.PlayerCount} failures={registerFailures}");
    }

    private void PumpGuests()
    {
        while (signal.TryTake(out GymPeerSignal.Incoming message))
        {
            // Every signal travels as the broker's CANDIDATE type: it forwards that one reliably, and the
            // kind is inside the data. A first message from an unknown guest must be its offer.
            if (message.Type != "CANDIDATE" || string.IsNullOrEmpty(message.Data)) continue;
            Pending found = null;
            for (int i = 0; i < pending.Count && found == null; i++)
            {
                if (pending[i].Src == message.Src && pending[i].Cid == message.Cid) found = pending[i];
            }
            if (found == null)
            {
                GymRtcSignal first = JsonUtility.FromJson<GymRtcSignal>(message.Data);
                if (first.k != "sdp" || first.type != "offer" || pending.Count >= 8) continue;
                found = new Pending
                {
                    Src = message.Src, Cid = message.Cid, Link = GymRtc.Create(false),
                    Born = Time.realtimeSinceStartup, Offer = message.Data
                };
                pending.Add(found);
                GymLog.Diag($"GYMCHAOS_PEER_GUEST_PENDING cid={message.Cid} pending={pending.Count}");
            }
            else if (found.Offer == message.Data) continue;
            found.Link.Signal(message.Data);
        }
        for (int i = pending.Count - 1; i >= 0; i--)
        {
            Pending guest = pending[i];
            while (guest.Link.TryTakeSignal(out string text))
            {
                if (text.IndexOf("\"type\":\"answer\"", StringComparison.Ordinal) >= 0) guest.Answer = text;
                signal.Send("CANDIDATE", guest.Src, guest.Cid, text);
            }
            int state = guest.Link.State;
            if (state == 1)
            {
                // The guest waits for this before it speaks: a message sent the moment its end opens could
                // reach this end before it is listening.
                guest.Link.Send(HelloText);
                Relay.Add(guest.Link);
                pending.RemoveAt(i);
                GymLog.Diag($"GYMCHAOS_PEER_GUEST_OPEN cid={guest.Cid} players={Relay.PlayerCount}");
                continue;
            }
            if (state == 2 || Time.realtimeSinceStartup - guest.Born > PendingTimeout)
            {
                guest.Link.Close();
                pending.RemoveAt(i);
                GymLog.Diag($"GYMCHAOS_PEER_GUEST_LOST cid={guest.Cid} state={state}");
                continue;
            }
            // The guest repeats its offer while it waits for this answer, so the
            // answer is repeated the same way: one dropped signal costs one round.
            if (guest.Answer != null && Time.realtimeSinceStartup >= guest.NextResend)
            {
                guest.NextResend = Time.realtimeSinceStartup + OfferResendSeconds;
                signal.Send("CANDIDATE", guest.Src, guest.Cid, guest.Answer);
            }
        }
    }

    private void Dispose()
    {
        foreach (Pending guest in pending) guest.Link.Close();
        pending.Clear();
        Relay.CloseAll();
        signal?.Close();
    }
}

/// <summary>A guest dialling a room code: broker introduction, then one data channel to the host.</summary>
public sealed class GymPeerGuestSocket : IGymNetSocket
{
    private const float Timeout = 25f;
    private const float OfferResendSeconds = 2f;

    private readonly GymPeerSignal signal;
    private readonly IGymRtcLink link;
    private readonly string hostId;
    private readonly string connectionId = GymPeerSignal.RandomText(10);
    private readonly float born = Time.realtimeSinceStartup;
    private int state;
    private bool hostListening;
    // The offer, repeated until the host answers: the broker drops what arrives
    // while the host is re-registering, and a single lost offer used to end the join.
    private string offer;
    private float nextOfferAt;

    public GymPeerGuestSocket(string code)
    {
        hostId = GymPeerHost.IdFor(code);
        if (!GymRtc.Available)
        {
            GymLog.Diag("GYMCHAOS_PEER_GUEST_NO_WEBRTC code=" + code);
            state = 2;
            return;
        }
        signal = new GymPeerSignal("gc" + GymPeerSignal.RandomText(14));
        link = GymRtc.Create(true);
    }

    public int State
    {
        get
        {
            Pump();
            return state;
        }
    }

    public void Send(string text)
    {
        if (state == 1) link.Send(text);
    }

    public bool TryReceive(out string text)
    {
        Pump();
        if (link == null)
        {
            text = null;
            return false;
        }
        return link.TryReceive(out text);
    }

    public void Close()
    {
        state = 2;
        signal?.Close();
        link?.Close();
    }

    private void Pump()
    {
        if (state == 2) return;
        if (state == 1)
        {
            if (link.State == 2) Close();
            return;
        }
        signal.Pump();
        if (signal.State == 2 || Time.realtimeSinceStartup - born > Timeout)
        {
            GymLog.Diag($"GYMCHAOS_PEER_GUEST_FAILED signal={signal.State} link={link.State} age={Time.realtimeSinceStartup - born:F1}");
            Close();
            return;
        }
        if (signal.State == 1)
        {
            while (link.TryTakeSignal(out string text))
            {
                if (offer == null && text.IndexOf("\"type\":\"offer\"", StringComparison.Ordinal) >= 0)
                {
                    offer = text;
                    nextOfferAt = Time.realtimeSinceStartup + OfferResendSeconds;
                }
                signal.Send("CANDIDATE", hostId, connectionId, text);
            }
            if (offer != null && link.State == 0 && Time.realtimeSinceStartup >= nextOfferAt)
            {
                nextOfferAt = Time.realtimeSinceStartup + OfferResendSeconds;
                signal.Send("CANDIDATE", hostId, connectionId, offer);
            }
        }
        while (signal.TryTake(out GymPeerSignal.Incoming message))
        {
            // No answer from an unknown host: the broker expires the offer.
            if (message.Type == "EXPIRE") { GymLog.Diag("GYMCHAOS_PEER_GUEST_EXPIRED"); Close(); return; }
            if (message.Type == "CANDIDATE" && message.Cid == connectionId) link.Signal(message.Data);
        }
        int linkState = link.State;
        if (linkState == 1)
        {
            // Open is not enough: the host says hello once it listens.
            while (!hostListening && link.TryReceive(out string text)) hostListening = text == GymPeerHost.HelloText;
            if (!hostListening) return;
            state = 1;
            signal.Close();
        }
        else if (linkState == 2) { GymLog.Diag("GYMCHAOS_PEER_GUEST_LINK_DOWN"); Close(); }
    }
}

/// <summary>Two in-process sockets wired to each other: the host's client and the relay it runs.</summary>
public sealed class GymLoopbackSocket : IGymNetSocket
{
    private readonly Queue<string> inbox = new Queue<string>();
    private GymLoopbackSocket partner;
    private int state = 1;

    public static void Pair(out GymLoopbackSocket a, out GymLoopbackSocket b)
    {
        a = new GymLoopbackSocket();
        b = new GymLoopbackSocket();
        a.partner = b;
        b.partner = a;
    }

    public static GymLoopbackSocket Dead() => new GymLoopbackSocket { state = 2 };

    public int State => state;

    public void Send(string text)
    {
        if (state == 1 && partner != null) partner.inbox.Enqueue(text);
    }

    public bool TryReceive(out string text)
    {
        if (inbox.Count == 0)
        {
            text = null;
            return false;
        }
        text = inbox.Dequeue();
        return true;
    }

    public void Close()
    {
        state = 2;
        if (partner != null && partner.state == 1) partner.state = 2;
    }
}
