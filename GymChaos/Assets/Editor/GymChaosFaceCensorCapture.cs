#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Graphics-only close-up of each enemy's black eye bar. Freezes play mode
/// once every listed identity is built and writes Logs/agent/face-*.png.
/// </summary>
[InitializeOnLoad]
public static class GymChaosFaceCensorCapture
{
    private const string RequestedKey = "GymChaos.FaceCensorCaptureRequested";
    private static readonly BodybuilderIdentity[] Identities =
    {
        BodybuilderIdentity.Zyzz, BodybuilderIdentity.JayCutler,
        BodybuilderIdentity.Arnold, BodybuilderIdentity.Cbum,
        BodybuilderIdentity.Goku
    };
    private static double started;
    private static int settleFrames;
    private static int resultCode;

    static GymChaosFaceCensorCapture()
    {
        if (SessionState.GetBool(RequestedKey, false))
        {
            Hook();
        }
    }

    public static void Run()
    {
        SessionState.SetBool(RequestedKey, true);
        settleFrames = 0;
        resultCode = 1;
        GymChaosVerifierExit.Record(resultCode);
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        Hook();
        EditorApplication.isPlaying = true;
    }

    private static void Hook()
    {
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
        EditorApplication.playModeStateChanged -= PlayModeChanged;
        EditorApplication.playModeStateChanged += PlayModeChanged;
    }

    private static void PlayModeChanged(PlayModeStateChange change)
    {
        if (change == PlayModeStateChange.EnteredPlayMode)
        {
            started = EditorApplication.timeSinceStartup;
            AudioListener.volume = 0f;
            Application.runInBackground = true;
        }
        if (change != PlayModeStateChange.EnteredEditMode)
        {
            return;
        }
        EditorApplication.update -= Tick;
        EditorApplication.playModeStateChanged -= PlayModeChanged;
        SessionState.EraseBool(RequestedKey);
        if (Application.isBatchMode)
        {
            GymChaosVerifierExit.Exit(resultCode);
        }
    }

    private static void Tick()
    {
        if (!EditorApplication.isPlaying)
        {
            return;
        }
        try
        {
            EnemyFighter[] fighters = UnityEngine.Object.FindObjectsByType<EnemyFighter>(
                FindObjectsInactive.Include);
            int ready = 0;
            string missing = string.Empty;
            foreach (BodybuilderIdentity identity in Identities)
            {
                if (FindBar(fighters, identity, out _, out _))
                {
                    ready++;
                }
                else
                {
                    missing += identity + " ";
                }
            }
            if (EditorApplication.timeSinceStartup - started > 120d)
            {
                string present = string.Empty;
                foreach (EnemyFighter fighter in fighters)
                {
                    present += fighter.Identity + " ";
                }
                throw new InvalidOperationException(
                    $"Face censor capture timed out: missing={missing} fighters={present}");
            }
            if (ready < Identities.Length || ++settleFrames < 30)
            {
                return;
            }
            Time.timeScale = 0f;
            foreach (BodybuilderIdentity identity in Identities)
            {
                FindBar(fighters, identity, out EnemyFighter fighter, out Transform bar);
                // Roster members that are off shift stay inactive; show them
                // for this frozen capture only.
                bool wasActive = fighter.gameObject.activeSelf;
                string hidden = string.Empty;
                for (Transform node = bar; node != null; node = node.parent)
                {
                    if (!node.gameObject.activeSelf)
                    {
                        hidden += node.name + ";";
                        node.gameObject.SetActive(true);
                    }
                }
                Debug.Log($"GYMCHAOS_FACE_CENSOR_HIDDEN identity={identity} nodes={hidden}");
                Debug.Log($"GYMCHAOS_FACE_CENSOR_STATE identity={identity} wasActive={wasActive} " +
                    $"barActive={bar.gameObject.activeInHierarchy}");
                // The bar's forward is the measured face direction; the
                // fighter root can face elsewhere while idling.
                // Face-on view so image distances are true face-plane distances
                // even when the idle pose pitches the head.
                Vector3 focus = bar.position;
                Capture($"face-{identity}", focus + bar.forward * 0.75f, focus, bar.up);
                Debug.Log($"GYMCHAOS_FACE_CENSOR_CAPTURE identity={identity} bar={bar.position}");
            }
            Debug.Log("GYMCHAOS_FACE_CENSOR_CAPTURE_OK");
            Finish(0);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            Finish(1);
        }
    }

    private static bool FindBar(EnemyFighter[] fighters, BodybuilderIdentity identity,
        out EnemyFighter fighter, out Transform bar)
    {
        foreach (EnemyFighter candidate in fighters)
        {
            if (candidate == null || candidate.Identity != identity)
            {
                continue;
            }
            foreach (Transform child in candidate.GetComponentsInChildren<Transform>(true))
            {
                if (child.name.EndsWith("Black Eye Bar"))
                {
                    fighter = candidate;
                    bar = child;
                    return true;
                }
            }
        }
        fighter = null;
        bar = null;
        return false;
    }

    private static void Capture(string label, Vector3 eye, Vector3 focus, Vector3 up)
    {
        GameObject host = new GameObject("Face Censor Capture Camera");
        Camera camera = host.AddComponent<Camera>();
        camera.fieldOfView = 35f;
        camera.nearClipPlane = 0.03f;
        camera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(focus - eye, up));
        RenderTexture target = new RenderTexture(480, 480, 24);
        camera.targetTexture = target;
        camera.Render();
        RenderTexture previous = RenderTexture.active;
        RenderTexture.active = target;
        Texture2D image = new Texture2D(480, 480, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0, 0, 480, 480), 0, 0);
        image.Apply();
        RenderTexture.active = previous;
        camera.targetTexture = null;
        string path = Path.Combine(
            Directory.GetParent(Application.dataPath).Parent.FullName,
            "Logs", "agent", label + ".png");
        File.WriteAllBytes(path, image.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(image);
        target.Release();
        UnityEngine.Object.DestroyImmediate(target);
        UnityEngine.Object.DestroyImmediate(host);
    }

    private static void Finish(int code)
    {
        resultCode = code;
        GymChaosVerifierExit.Record(code);
        Time.timeScale = 1f;
        EditorApplication.isPlaying = false;
    }
}
#endif
