using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// Checks that every scanned character and vehicle renders its texture with
/// the short mip chain from ScanTextureMips (lower mips mix the tiny UV
/// islands into skin/grey specks on clothes). With -Graphics it writes
/// close-ups of the characters that are active at boot to
/// Logs/agent/scan_textures.
[InitializeOnLoad]
public static class GymChaosScanTextureVerifier
{
    private const string RequestedKey = "GymChaos.ScanTextureVerificationRequested";
    private static double startTime;
    private static bool finished;
    private static int resultCode;

    static GymChaosScanTextureVerifier()
    {
        if (!SessionState.GetBool(RequestedKey, false))
        {
            return;
        }
        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        EditorApplication.delayCall += () =>
        {
            if (EditorApplication.isPlaying)
            {
                startTime = EditorApplication.timeSinceStartup;
                EditorApplication.update -= Tick;
                EditorApplication.update += Tick;
            }
        };
    }

    [MenuItem("Tools/GymChaos/Run Scan Texture Verification")]
    public static void Run()
    {
        finished = false;
        resultCode = 1;
        GymChaosVerifierExit.Record(resultCode);
        SessionState.SetBool(RequestedKey, true);
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        EditorApplication.isPlaying = true;
    }

    private static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            startTime = EditorApplication.timeSinceStartup;
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
        }
        else if (state == PlayModeStateChange.EnteredEditMode)
        {
            EditorApplication.update -= Tick;
            SessionState.EraseBool(RequestedKey);
            if (Application.isBatchMode)
            {
                GymChaosVerifierExit.Exit(resultCode);
            }
        }
    }

    private static void Tick()
    {
        if (finished || !EditorApplication.isPlaying)
        {
            return;
        }
        double elapsed = EditorApplication.timeSinceStartup - startTime;
        GameObject receptionist = GameObject.Find("NPC - manwithsuit1");
        bool receptionistReady = receptionist != null &&
            receptionist.GetComponentInChildren<SkinnedMeshRenderer>() != null;
        if (!receptionistReady || elapsed < 8d)
        {
            if (elapsed > 70d)
            {
                Finish(false, "characters_not_ready");
            }
            return;
        }

        bool copySupported =
            (SystemInfo.copyTextureSupport & UnityEngine.Rendering.CopyTextureSupport.Basic) != 0;
        int checkedCharacters = 0, checkedVehicles = 0;
        List<string> bad = new List<string>();
        foreach (EnemyFighter fighter in Object.FindObjectsByType<EnemyFighter>(
                     FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            foreach (SkinnedMeshRenderer body in fighter.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                Material material = body.sharedMaterial;
                if (material == null || material.shader == null ||
                    material.shader.name != "GymChaos/BodybuilderUnlit")
                {
                    continue;
                }
                checkedCharacters++;
                CheckTexture(material, fighter.Identity.ToString(), copySupported, bad);
            }
        }
        foreach (GymVisitorVehicle vehicle in Object.FindObjectsByType<GymVisitorVehicle>(
                     FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            foreach (Renderer renderer in vehicle.GetComponentsInChildren<Renderer>(true))
            {
                Material material = renderer.sharedMaterial;
                if (material != null && material.name.Contains("Original GLB Material"))
                {
                    checkedVehicles++;
                    CheckTexture(material, vehicle.name, copySupported, bad);
                    break;
                }
            }
        }

        int captures = 0;
        if (SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null)
        {
            captures = CaptureActiveCharacters();
        }

        // WebGL/GLES without CopyTexture: the flat render-texture copy.
        ScanTextureMips.ForceFlatCopy = true;
        Texture flat = ScanTextureMips.Limit(Resources.Load<Texture2D>("Characters/Textures/player"));
        ScanTextureMips.ForceFlatCopy = false;
        bool flatOk = flat is RenderTexture rt && rt.IsCreated() && flat.mipmapCount == 1;
        if (!flatOk)
        {
            bad.Add($"flatFallback:{(flat != null ? flat.GetType().Name + ":" + flat.mipmapCount : "null")}");
        }

        string details = $"characters={checkedCharacters} vehicles={checkedVehicles} flatFallback={flatOk} " +
            $"copySupported={copySupported} captures={captures} bad={(bad.Count == 0 ? "none" : string.Join(",", bad))}";
        Finish(checkedCharacters > 0 && bad.Count == 0, details);
    }

    private static void CheckTexture(Material material, string owner, bool copySupported, List<string> bad)
    {
        Texture texture = material.HasProperty("_BaseMap") ? material.GetTexture("_BaseMap") : null;
        if (texture == null)
        {
            bad.Add(owner + ":missing");
        }
        else if (copySupported && texture.mipmapCount > ScanTextureMips.DefaultLevels)
        {
            bad.Add($"{owner}:{texture.name}:mips={texture.mipmapCount}");
        }
    }

    private static int CaptureActiveCharacters()
    {
        string folder = Path.Combine(Path.GetDirectoryName(Application.dataPath), "..", "Logs", "agent", "scan_textures");
        Directory.CreateDirectory(folder);
        RenderTexture target = new RenderTexture(720, 900, 24);
        GameObject probeObject = new GameObject("Scan Texture Probe");
        Camera probe = probeObject.AddComponent<Camera>();
        probe.nearClipPlane = 0.05f;
        probe.fieldOfView = 34f;
        int written = 0;
        try
        {
            foreach (EnemyFighter fighter in Object.FindObjectsByType<EnemyFighter>(FindObjectsSortMode.None))
            {
                SkinnedMeshRenderer body = fighter.GetComponentInChildren<SkinnedMeshRenderer>();
                if (body == null || !body.enabled)
                {
                    continue;
                }
                Bounds bounds = body.bounds;
                Vector3 forward = Vector3.ProjectOnPlane(fighter.transform.forward, Vector3.up).normalized;
                // Torso, from the distance the player usually talks to them.
                Vector3 focus = bounds.center + Vector3.up * bounds.extents.y * 0.25f;
                foreach (var view in new[] { ("front", forward), ("back", -forward) })
                {
                    for (int d = 0; d < 2; d++)
                    {
                        float distance = d == 0 ? 2.2f : 5.5f;
                        Vector3 position = focus + view.Item2 * distance + Vector3.up * 0.25f;
                        probe.transform.SetPositionAndRotation(position, Quaternion.LookRotation(focus - position, Vector3.up));
                        probe.targetTexture = target;
                        probe.Render();
                        RenderTexture.active = target;
                        Texture2D image = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
                        image.ReadPixels(new Rect(0f, 0f, target.width, target.height), 0, 0);
                        image.Apply(false, false);
                        File.WriteAllBytes(Path.Combine(folder,
                            $"{fighter.Identity}_{view.Item1}_{(d == 0 ? "near" : "far")}.png"), image.EncodeToPNG());
                        Object.Destroy(image);
                        written++;
                    }
                }
            }
        }
        finally
        {
            RenderTexture.active = null;
            probe.targetTexture = null;
            Object.Destroy(probeObject);
            target.Release();
            Object.Destroy(target);
        }
        return written;
    }

    private static void Finish(bool pass, string details)
    {
        finished = true;
        resultCode = pass ? 0 : 1;
        GymChaosVerifierExit.Record(resultCode);
        if (pass)
        {
            Debug.Log("GYMCHAOS_SCAN_TEXTURE_OK " + details);
        }
        else
        {
            Debug.LogError("GYMCHAOS_SCAN_TEXTURE_FAIL " + details);
        }
        EditorApplication.isPlaying = false;
    }
}
