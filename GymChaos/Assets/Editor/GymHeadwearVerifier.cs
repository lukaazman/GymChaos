using System;
using System.Collections;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class GymHeadwearVerifier
{
    const string Key = "GymChaos.HeadwearVerifier";
    static bool launched;
    static GymHeadwearVerifier() { EditorApplication.update += Tick; }
    public static void Run()
    {
        SessionState.SetBool(Key, true);
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        EditorApplication.isPlaying = true;
    }
    static void Tick()
    {
        if (!SessionState.GetBool(Key, false) || !EditorApplication.isPlaying || launched) return;
        if (UnityEngine.Object.FindFirstObjectByType<PlayerHandRig>()?.RuntimeHead == null) return;
        launched = true;
        new GameObject("Headwear verifier").AddComponent<GymHeadwearVerifierRunner>();
    }
    public static void Finish(int result)
    {
        SessionState.SetBool(Key, false);
        EditorApplication.Exit(result);
    }
}

public class GymHeadwearVerifierRunner : MonoBehaviour
{
    IEnumerator Start()
    {
        yield return new WaitForSeconds(3f);
        var player = FindFirstObjectByType<PlayerMovement>();
        var rig = player.GetComponentInChildren<PlayerHandRig>();
        var loadout = player.GetComponent<PlayerCosmeticLoadout>();
        var progression = FindFirstObjectByType<GymExperienceService>();
        if (progression != null) progression.enabled = false;
        player.enabled = false;
        rig.enabled = false;
        if (GymBackRoomBuilder.TryGetLockerPreviewPose(out Vector3 p, out Quaternion q))
            player.SetCinematicPose(p, q, player.StandingCameraLocalPosition, Quaternion.Euler(-4f, 0f, 0f));
        rig.HoldStablePoseForVerification("idle1", 0f, out _);
        var output = Path.GetFullPath("../.tools/headwear/runtime");
        Directory.CreateDirectory(output);
        var cam = new GameObject("Headwear evidence camera").AddComponent<Camera>();
        cam.cullingMask = 1 << PlanarGymMirror.MirrorPlayerLayer;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.25f, 0.28f, 0.31f);
        cam.nearClipPlane = 0.01f;
        cam.farClipPlane = 15f;
        cam.orthographic = true;
        cam.orthographicSize = 0.75f;
        foreach (GymHeadwear item in Enum.GetValues(typeof(GymHeadwear)))
        {
            loadout.ApplyFromState(new GymProgressionState { headwear = item.ToString() });
            float limit = Time.realtimeSinceStartup + 15f;
            while (!loadout.IsHeadwearVisualReady && Time.realtimeSinceStartup < limit) yield return null;
            if (!loadout.IsHeadwearVisualReady) { Debug.LogError("HEADWEAR_FAILED loading " + item); GymHeadwearVerifier.Finish(1); yield break; }
            yield return null;
            rig.HoldStablePoseForVerification("idle1", 0f, out _);
            var head = rig.RuntimeHead;
            foreach (var r in rig.GetComponentsInChildren<MeshRenderer>())
                if (r.name.StartsWith("Equipped "))
                {
                    Debug.Log($"HEADWEAR_BOUNDS item={item} head={head.position} hat={r.bounds} scale={r.transform.lossyScale} local={r.transform.localPosition}");
                    if (Vector3.Distance(head.position, r.bounds.center) > 1.2f || r.bounds.size.magnitude > 2f)
                    { Debug.LogError("HEADWEAR_FAILED fit " + item); GymHeadwearVerifier.Finish(1); yield break; }
                }
            Vector3 target = head.position + Vector3.up * 0.25f;
            cam.transform.position = target + player.transform.forward * 2.4f;
            cam.transform.LookAt(target);
            Capture(cam, Path.Combine(output, item + ".png"));
            cam.transform.position = target + player.transform.right * 2.4f;
            cam.transform.LookAt(target);
            Capture(cam, Path.Combine(output, item + "-side.png"));
            foreach (var mirror in FindObjectsByType<PlanarGymMirror>(FindObjectsSortMode.None))
            {
                mirror.RequestImmediateRefresh();
                mirror.ReflectionCamera.Render();
            }
            Capture(player.playerCamera, Path.Combine(output, item + "-locker.png"));
            foreach (string clip in new[] { "walking", "running", "jumping", "punch_left" })
            {
                rig.HoldStablePoseForVerification(clip, 0.5f, out _);
                foreach (var r in rig.GetComponentsInChildren<MeshRenderer>())
                    if (r.name.StartsWith("Equipped ") &&
                        (r.transform.parent != rig.RuntimeHead || Vector3.Distance(r.bounds.center, rig.RuntimeHead.position) > 1.2f))
                    { Debug.LogError("HEADWEAR_FAILED attachment " + item + " " + clip); GymHeadwearVerifier.Finish(1); yield break; }
            }
            rig.HoldStablePoseForVerification("idle1", 0f, out _);
            Debug.Log($"HEADWEAR_CAPTURE item={item} ready={loadout.IsHeadwearVisualReady}");
        }
        Debug.Log("HEADWEAR_VERIFIER_CAPTURE_COMPLETE");
        GymHeadwearVerifier.Finish(0);
    }
    static void Capture(Camera camera, string path)
    {
        var rt = new RenderTexture(512, 512, 24);
        var old = RenderTexture.active;
        camera.targetTexture = rt;
        camera.Render();
        RenderTexture.active = rt;
        var image = new Texture2D(512, 512, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0, 0, 512, 512), 0, 0);
        image.Apply();
        File.WriteAllBytes(path, image.EncodeToPNG());
        camera.targetTexture = null;
        RenderTexture.active = old;
        Destroy(image);
        Destroy(rt);
    }
}
