using System;
using System.Collections.Concurrent;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
#if UNITY_WEBGL && !UNITY_EDITOR
using System.Runtime.InteropServices;
#else
using System.Net.WebSockets;
#endif

/// <summary>Text-message WebSocket: .NET ClientWebSocket off WebGL, the browser WebSocket (jslib) on WebGL.</summary>
public interface IGymNetSocket
{
    /// <summary>0 connecting, 1 open, 2 closed or failed.</summary>
    int State { get; }
    void Send(string text);
    bool TryReceive(out string text);
    void Close();
}

public static class GymNetSocket
{
    public static IGymNetSocket Open(string url)
    {
        // Peer-to-peer rooms: the host's own client talks to the room logic in its game, a guest dials the host.
        if (url.StartsWith(GymPeerHost.LoopbackUrl, StringComparison.Ordinal))
            return GymPeerHost.Current != null ? GymPeerHost.Current.OpenLoopback() : GymLoopbackSocket.Dead();
        if (url.StartsWith(GymPeerHost.GuestScheme, StringComparison.Ordinal))
            return new GymPeerGuestSocket(url.Substring(GymPeerHost.GuestScheme.Length));
#if UNITY_WEBGL && !UNITY_EDITOR
        return new BrowserSocket(url);
#else
        return new DotNetSocket(url);
#endif
    }

#if UNITY_WEBGL && !UNITY_EDITOR
    private sealed class BrowserSocket : IGymNetSocket
    {
        [DllImport("__Internal")] private static extern int GymNetOpen(string url);
        [DllImport("__Internal")] private static extern int GymNetState(int id);
        [DllImport("__Internal")] private static extern void GymNetSend(int id, string text);
        [DllImport("__Internal")] private static extern int GymNetReceive(int id, byte[] buffer, int size);
        [DllImport("__Internal")] private static extern void GymNetClose(int id);

        private readonly int id;
        private byte[] buffer = new byte[4096];

        public BrowserSocket(string url) => id = GymNetOpen(url);
        public int State => GymNetState(id);
        public void Send(string text) => GymNetSend(id, text);
        public void Close() => GymNetClose(id);

        public bool TryReceive(out string text)
        {
            text = null;
            int length = GymNetReceive(id, buffer, buffer.Length);
            while (length < 0)
            {
                // Message larger than the buffer: grow once and ask again.
                Array.Resize(ref buffer, buffer.Length * 4);
                length = GymNetReceive(id, buffer, buffer.Length);
            }
            if (length == 0) return false;
            text = Encoding.UTF8.GetString(buffer, 0, length);
            return true;
        }
    }
#else
    private sealed class DotNetSocket : IGymNetSocket
    {
        private const int MaxMessageBytes = 64 * 1024;
        private readonly ClientWebSocket socket = new ClientWebSocket();
        private readonly ConcurrentQueue<string> inbox = new ConcurrentQueue<string>();
        private readonly ConcurrentQueue<string> outbox = new ConcurrentQueue<string>();
        private readonly CancellationTokenSource cancel = new CancellationTokenSource();
        private int state;

        public DotNetSocket(string url) => Task.Run(() => Run(url));

        public int State => Volatile.Read(ref state);
        public void Send(string text) => outbox.Enqueue(text);
        public bool TryReceive(out string text) => inbox.TryDequeue(out text);

        public void Close()
        {
            cancel.Cancel();
            Volatile.Write(ref state, 2);
        }

        private async Task Run(string url)
        {
            try
            {
                await socket.ConnectAsync(new Uri(url), cancel.Token);
                Volatile.Write(ref state, 1);
                Task receive = ReceiveLoop();
                while (!cancel.IsCancellationRequested && socket.State == WebSocketState.Open)
                {
                    if (outbox.TryDequeue(out string text))
                    {
                        byte[] bytes = Encoding.UTF8.GetBytes(text);
                        await socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, cancel.Token);
                    }
                    else await Task.Delay(5, cancel.Token);
                }
                await receive;
            }
            catch (Exception exception) when (exception is OperationCanceledException || exception is WebSocketException ||
                exception is InvalidOperationException || exception is ObjectDisposedException || exception is System.IO.IOException)
            {
                // Closed or failed: State reports it, the owner decides what to do.
            }
            finally
            {
                Volatile.Write(ref state, 2);
                socket.Dispose();
            }
        }

        private async Task ReceiveLoop()
        {
            byte[] buffer = new byte[8192];
            var message = new StringBuilder();
            while (socket.State == WebSocketState.Open && !cancel.IsCancellationRequested)
            {
                WebSocketReceiveResult result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancel.Token);
                if (result.MessageType == WebSocketMessageType.Close) break;
                message.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                if (message.Length > MaxMessageBytes) { message.Clear(); continue; }
                if (!result.EndOfMessage) continue;
                inbox.Enqueue(message.ToString());
                message.Clear();
            }
        }
    }
#endif
}
