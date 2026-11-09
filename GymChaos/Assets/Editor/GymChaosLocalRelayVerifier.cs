#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>Hosting on this PC starts the local room server when nothing listens on its port.</summary>
public static class GymChaosLocalRelayVerifier
{
    private const int Port = 18799;

    public static void Run()
    {
        int code = 1;
        try
        {
            if (GymLocalRelay.Listening(Port)) throw new System.Exception("port already in use");
            GymLocalRelay.EnsureRunning("ws://127.0.0.1:" + Port);
            if (!GymLocalRelay.Listening(Port)) throw new System.Exception("relay did not start");
            // A second host call finds it running and starts nothing more.
            GymLocalRelay.EnsureRunning("ws://127.0.0.1:" + Port);
            Debug.Log("GYMCHAOS_LOCAL_RELAY_OK port=" + Port);
            code = 0;
        }
        catch (System.Exception exception)
        {
            Debug.LogError("GYMCHAOS_LOCAL_RELAY_FAILED " + exception.Message);
        }
        finally
        {
            GymLocalRelay.Stop();
        }
        EditorApplication.Exit(code);
    }
}
#endif
