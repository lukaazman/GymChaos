using System;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using UnityEngine;

/// <summary>
/// Hosting on this PC needs a room server on the local relay address. When none listens, this starts
/// Server/relay/relay.js with Node (found next to the project or the build) and stops it on quit.
/// A web build cannot start processes: it uses the address in Resources/RelayUrl.txt.
/// </summary>
public static class GymLocalRelay
{
#if !UNITY_WEBGL
    private static Process relay;

    public static void EnsureRunning(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri address) || !address.IsLoopback || Listening(address.Port)) return;
        string script = FindScript();
        if (script == null)
        {
            UnityEngine.Debug.LogWarning("GYMCHAOS_RELAY_MISSING no Server/relay/relay.js near " + Application.dataPath);
            return;
        }
        try
        {
            ProcessStartInfo info = new ProcessStartInfo("node", "\"" + script + "\"")
            {
                WorkingDirectory = Path.GetDirectoryName(script),
                UseShellExecute = false,
                CreateNoWindow = true
            };
            info.EnvironmentVariables["PORT"] = address.Port.ToString();
            relay = Process.Start(info);
            Application.quitting += Stop;
            // The room opens as soon as the port accepts; Node starts in well under a second.
            float limit = Time.realtimeSinceStartup + 4f;
            while (!Listening(address.Port) && Time.realtimeSinceStartup < limit && !relay.HasExited) System.Threading.Thread.Sleep(50);
            UnityEngine.Debug.Log($"GYMCHAOS_RELAY_STARTED port={address.Port} listening={Listening(address.Port)}");
        }
        catch (Exception exception)
        {
            UnityEngine.Debug.LogWarning("GYMCHAOS_RELAY_START_FAILED (is Node installed?) " + exception.Message);
        }
    }

    internal static void Stop()
    {
        Application.quitting -= Stop;
        try { if (relay != null && !relay.HasExited) relay.Kill(); } catch (Exception) { }
        relay = null;
    }

    internal static bool Listening(int port)
    {
        try
        {
            using (TcpClient probe = new TcpClient())
            {
                IAsyncResult connect = probe.BeginConnect("127.0.0.1", port, null, null);
                return connect.AsyncWaitHandle.WaitOne(250) && probe.Connected;
            }
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static string FindScript()
    {
        string folder = Application.dataPath;
        for (int i = 0; i < 5 && !string.IsNullOrEmpty(folder); i++)
        {
            string candidate = Path.Combine(folder, "Server", "relay", "relay.js");
            if (File.Exists(candidate)) return candidate;
            folder = Path.GetDirectoryName(folder);
        }
        return null;
    }
#else
    public static void EnsureRunning(string url) { }
#endif
}
