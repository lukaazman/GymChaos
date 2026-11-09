using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using UnityEngine;
#if UNITY_WEBGL && !UNITY_EDITOR
using System.Runtime.InteropServices;
#else
using Unity.WebRTC;
#endif

/// <summary>One WebRTC data channel as a text socket. The two ends swap opaque signal texts (offer, answer, ICE) by any means.</summary>
public interface IGymRtcLink : IGymNetSocket
{
    /// <summary>Feeds a signal produced by the other end.</summary>
    void Signal(string json);
    /// <summary>Takes the next signal this end produced for the other.</summary>
    bool TryTakeSignal(out string json);
}

[Serializable]
public sealed class GymRtcSignal
{
    /// <summary>"sdp" or "ice".</summary>
    public string k;
    /// <summary>"offer" or "answer" for sdp.</summary>
    public string type;
    public string sdp;
    public string c;
    public string mid;
    public int idx;
}

public static class GymRtc
{
    public static readonly string[] StunServers = { "stun:stun.l.google.com:19302", "stun:stun1.l.google.com:19302" };

    /// <summary>The offering end opens the data channel; the answering end receives it.</summary>
    public static IGymRtcLink Create(bool offering)
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        return new BrowserLink(offering);
#else
        return new NativeLink(offering);
#endif
    }

#if UNITY_WEBGL && !UNITY_EDITOR
    private sealed class BrowserLink : IGymRtcLink
    {
        [DllImport("__Internal")] private static extern int GymRtcOpen(int offering, string stunUrls);
        [DllImport("__Internal")] private static extern int GymRtcState(int id);
        [DllImport("__Internal")] private static extern void GymRtcSend(int id, string text);
        [DllImport("__Internal")] private static extern int GymRtcReceive(int id, byte[] buffer, int size);
        [DllImport("__Internal")] private static extern void GymRtcSignalIn(int id, string json);
        [DllImport("__Internal")] private static extern int GymRtcSignalOut(int id, byte[] buffer, int size);
        [DllImport("__Internal")] private static extern void GymRtcClose(int id);

        private readonly int id;
        private byte[] buffer = new byte[4096];

        public BrowserLink(bool offering) => id = GymRtcOpen(offering ? 1 : 0, string.Join(",", StunServers));
        public int State => GymRtcState(id);
        public void Send(string text) => GymRtcSend(id, text);
        public void Signal(string json) => GymRtcSignalIn(id, json);
        public void Close() => GymRtcClose(id);
        public bool TryReceive(out string text) => Take(false, out text);
        public bool TryTakeSignal(out string json) => Take(true, out json);

        private bool Take(bool signal, out string text)
        {
            text = null;
            int length = signal ? GymRtcSignalOut(id, buffer, buffer.Length) : GymRtcReceive(id, buffer, buffer.Length);
            while (length < 0)
            {
                Array.Resize(ref buffer, buffer.Length * 4);
                length = signal ? GymRtcSignalOut(id, buffer, buffer.Length) : GymRtcReceive(id, buffer, buffer.Length);
            }
            if (length == 0) return false;
            text = Encoding.UTF8.GetString(buffer, 0, length);
            return true;
        }
    }
#else
    /// <summary>Unity WebRTC (native). Its callbacks only queue; every WebRTC call happens in Pump on the caller's thread.</summary>
    private sealed class NativeLink : IGymRtcLink
    {
        private readonly bool offering;
        private readonly RTCPeerConnection connection;
        private readonly ConcurrentQueue<GymRtcSignal> signals = new ConcurrentQueue<GymRtcSignal>();
        private readonly ConcurrentQueue<string> inbox = new ConcurrentQueue<string>();
        private readonly List<RTCIceCandidateInit> earlyCandidates = new List<RTCIceCandidateInit>();
        private RTCDataChannel channel;
        private RTCSessionDescriptionAsyncOperation createOp;
        private RTCSetSessionDescriptionAsyncOperation localOp;
        private RTCSetSessionDescriptionAsyncOperation remoteOp;
        private bool remoteSet;
        private bool disposed;
        private int state;

        public NativeLink(bool offering)
        {
            this.offering = offering;
            RTCConfiguration configuration = new RTCConfiguration { iceServers = new[] { new RTCIceServer { urls = StunServers } } };
            connection = new RTCPeerConnection(ref configuration);
            connection.OnIceCandidate = candidate => signals.Enqueue(new GymRtcSignal
            {
                k = "ice", c = candidate.Candidate, mid = candidate.SdpMid, idx = candidate.SdpMLineIndex ?? 0
            });
            connection.OnConnectionStateChange = s =>
            {
                GymLog.Diag($"GYMCHAOS_RTC conn offering={offering} {s}");
                if (s == RTCPeerConnectionState.Failed || s == RTCPeerConnectionState.Closed) Volatile.Write(ref state, 2);
            };
            if (offering)
            {
                Wire(connection.CreateDataChannel("gym", new RTCDataChannelInit { ordered = true }));
                createOp = connection.CreateOffer();
            }
            else connection.OnDataChannel = Wire;
        }

        public int State
        {
            get
            {
                Pump();
                return Volatile.Read(ref state);
            }
        }

        private void Wire(RTCDataChannel opened)
        {
            channel = opened;
            opened.OnOpen = () => { GymLog.Diag($"GYMCHAOS_RTC open offering={offering}"); Interlocked.CompareExchange(ref state, 1, 0); };
            opened.OnClose = () => { GymLog.Diag($"GYMCHAOS_RTC close offering={offering}"); Volatile.Write(ref state, 2); };
            opened.OnMessage = bytes => inbox.Enqueue(Encoding.UTF8.GetString(bytes));
        }

        public void Send(string text)
        {
            if (!disposed && channel != null && channel.ReadyState == RTCDataChannelState.Open) channel.Send(text);
        }

        public bool TryReceive(out string text)
        {
            Pump();
            return inbox.TryDequeue(out text);
        }

        public void Signal(string json)
        {
            if (disposed) return;
            GymRtcSignal signal = JsonUtility.FromJson<GymRtcSignal>(json);
            if (signal.k == "sdp")
            {
                RTCSessionDescription description = new RTCSessionDescription
                {
                    type = signal.type == "offer" ? RTCSdpType.Offer : RTCSdpType.Answer, sdp = signal.sdp
                };
                remoteOp = connection.SetRemoteDescription(ref description);
            }
            else if (signal.k == "ice")
            {
                RTCIceCandidateInit init = new RTCIceCandidateInit { candidate = signal.c, sdpMid = signal.mid, sdpMLineIndex = signal.idx };
                if (remoteSet) connection.AddIceCandidate(new RTCIceCandidate(init));
                else earlyCandidates.Add(init);
            }
        }

        public bool TryTakeSignal(out string json)
        {
            Pump();
            json = null;
            if (!signals.TryDequeue(out GymRtcSignal signal)) return false;
            json = JsonUtility.ToJson(signal);
            return true;
        }

        private void Pump()
        {
            if (disposed || Volatile.Read(ref state) == 2) return;
            // The answering end is handed a channel that may already be open: its OnOpen never fires then.
            if (channel != null && channel.ReadyState == RTCDataChannelState.Open) Interlocked.CompareExchange(ref state, 1, 0);
            if (createOp != null && createOp.IsDone)
            {
                RTCSessionDescriptionAsyncOperation done = createOp;
                createOp = null;
                if (done.IsError) { GymLog.Diag($"GYMCHAOS_RTC create failed offering={offering} {done.Error.message}"); Close(); return; }
                RTCSessionDescription description = done.Desc;
                localOp = connection.SetLocalDescription(ref description);
            }
            if (localOp != null && localOp.IsDone)
            {
                RTCSetSessionDescriptionAsyncOperation done = localOp;
                localOp = null;
                if (done.IsError) { GymLog.Diag($"GYMCHAOS_RTC local failed offering={offering} {done.Error.message}"); Close(); return; }
                RTCSessionDescription local = connection.LocalDescription;
                signals.Enqueue(new GymRtcSignal { k = "sdp", type = local.type == RTCSdpType.Offer ? "offer" : "answer", sdp = local.sdp });
            }
            if (remoteOp != null && remoteOp.IsDone)
            {
                RTCSetSessionDescriptionAsyncOperation done = remoteOp;
                remoteOp = null;
                if (done.IsError) { GymLog.Diag($"GYMCHAOS_RTC remote failed offering={offering} {done.Error.message}"); Close(); return; }
                remoteSet = true;
                foreach (RTCIceCandidateInit init in earlyCandidates) connection.AddIceCandidate(new RTCIceCandidate(init));
                earlyCandidates.Clear();
                if (!offering) createOp = connection.CreateAnswer();
            }
        }

        public void Close()
        {
            Volatile.Write(ref state, 2);
            if (disposed) return;
            disposed = true;
            try
            {
                channel?.Close();
                connection.Close();
                connection.Dispose();
            }
            catch (Exception exception)
            {
                Debug.LogWarning("GYMCHAOS_RTC_CLOSE " + exception.Message);
            }
        }
    }
#endif
}
