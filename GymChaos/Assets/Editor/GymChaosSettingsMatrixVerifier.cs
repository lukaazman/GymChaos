#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Needs -Graphics. Renders the player camera (with its post stack, mirrors
/// and first-person arms) at fixed views under every option preset, from
/// ultra to lowest, and compares each image with the ultra one on a coarse
/// grid. Lower presets may lose detail, never shapes or colours: a cell whose
/// mean colour moves more than the preset's limit (missing wall, wrong-colour
/// machine, hole) fails. Writes Logs/agent/matrix/{preset}-{view}.png and
/// logs GYMCHAOS_SETTINGS_MATRIX_OK or _FAIL. Restores the option prefs.
/// </summary>
[InitializeOnLoad]
public static class GymChaosSettingsMatrixVerifier
{
    private const string RequestedKey = "GymChaos.SettingsMatrixRequested";
    private const int Width = 960;
    private const int Height = 540;
    private const int GridX = 64;
    private const int GridY = 36;

    private static readonly string[] PrefKeys =
    {
        "GymChaos.UI.TextureQuality.v1", "GymChaos.UI.PostProcessing.v1", "GymChaos.UI.AntiAliasing.v1",
        "GymChaos.UI.RenderScale.v1", "GymChaos.UI.ShadowQuality.v1"
    };

    // name, texture, anti-aliasing, render scale index, shadows, post, allowed cell delta.
    // Texture, AA and scale are swept at ultra shadows; the shadow level is swept
    // alone with a looser limit, since coarser cascades move real shadow edges.
    // POST FX OFF changes the look on purpose (no ACES) and is judged by eye.
    private static readonly (string name, int texture, int aa, int scale, int shadows, bool post, float maxDelta)[] Presets =
    {
        ("ultra", 0, 4, 3, 2, true, 0f),
        ("high", 0, 2, 1, 2, true, 0.2f),
        ("low", 1, 1, 0, 2, true, 0.2f),
        ("lowest", 2, 0, 0, 2, true, 0.2f),
        ("lowshadow", 0, 4, 3, 0, true, 0.3f)
    };

    // name, position, yaw, pitch; "start" keeps the boot framing of the start screen.
    private static readonly (string name, Vector3 position, float yaw, float pitch)[] Views =
    {
        ("start", Vector3.zero, 0f, 0f),
        ("startwide", Vector3.zero, 0f, 0f),
        ("gym", new Vector3(156f, 1.6f, 6f), -90f, 6f),
        ("machines", new Vector3(140f, 1.6f, -6f), 60f, 6f),
        ("corner", new Vector3(150f, 1.6f, -10f), 135f, 4f),
        ("locker", new Vector3(142.5f, 1.6f, -18f), 225f, 8f),
        ("mirror", Vector3.zero, -90f, 8f)
    };

    private static double started;
    private static double builtAt = -1d;
    private static int resultCode = 1;
    // -1 = not started; otherwise the preset being rendered after a few frames.
    private static int presetIndex = -1;
    private static int viewIndex;
    private static int renderAtFrame;
    private static Vector3 bootPosition;
    private static Quaternion bootRotation;
    private static Vector3 mirrorEye;
    private static PlayerMovement player;
    private static readonly Dictionary<string, Color[]> Reference = new Dictionary<string, Color[]>();
    private static readonly Dictionary<string, float> ReferenceSpecks = new Dictionary<string, float>();
    // Share of saturated yellow pixels (scan-atlas bleed on skin) a preset may add.
    private const float MaxSpeckIncrease = 0.002f;
    private static readonly List<string> Failures = new List<string>();
    private static (bool has, int value)[] savedPrefs;

    static GymChaosSettingsMatrixVerifier()
    {
        if (SessionState.GetBool(RequestedKey, false)) Hook();
    }

    [MenuItem("Tools/GymChaos/Run Settings Matrix Verification")]
    public static void Run()
    {
        SessionState.SetBool(RequestedKey, true);
        started = EditorApplication.timeSinceStartup;
        builtAt = -1d;
        presetIndex = -1;
        Reference.Clear();
        ReferenceSpecks.Clear();
        Failures.Clear();
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        Hook();
        EditorApplication.isPlaying = true;
    }

    private static void Hook() => GymChaosVerifierHooks.Hook(Tick, PlayModeChanged);

    private static void PlayModeChanged(PlayModeStateChange change)
    {
        if (change == PlayModeStateChange.EnteredPlayMode)
        {
            started = EditorApplication.timeSinceStartup;
            Application.runInBackground = true;
        }
        if (change != PlayModeStateChange.EnteredEditMode) return;
        EditorApplication.update -= Tick;
        EditorApplication.playModeStateChanged -= PlayModeChanged;
        SessionState.EraseBool(RequestedKey);
        if (Application.isBatchMode) GymChaosVerifierExit.Exit(resultCode);
    }

    private static void Tick()
    {
        if (!EditorApplication.isPlaying) return;
        double now = EditorApplication.timeSinceStartup;
        bool done = false;
        try
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
                throw new InvalidOperationException("Settings matrix needs -Graphics.");
            player = UnityEngine.Object.FindFirstObjectByType<PlayerMovement>();
            GameObject floor = GameObject.Find("Rubber Floor");
            if (player == null || player.playerCamera == null || !GymOutdoorBuilder.IsBuilt || floor == null)
            {
                if (now - started > 90d) throw new TimeoutException("Scene did not build.");
                return;
            }
            if (builtAt < 0d) builtAt = now;
            if (now - builtAt < 8d) return;
            Camera camera = player.playerCamera;

            if (presetIndex < 0)
            {
                savedPrefs = new (bool, int)[PrefKeys.Length];
                for (int i = 0; i < PrefKeys.Length; i++)
                    savedPrefs[i] = (PlayerPrefs.HasKey(PrefKeys[i]), PlayerPrefs.GetInt(PrefKeys[i]));
                bootPosition = camera.transform.position;
                bootRotation = camera.transform.rotation;
                // 3 m in front of the west mirror wall.
                Bounds gym = floor.GetComponent<Renderer>().bounds;
                Vector3 stand = new Vector3(gym.min.x + 3.2f, gym.max.y + 0.05f, gym.center.z);
                mirrorEye = stand + Vector3.up * 1.6f;
                // Captures are 16:9; the mirror cameras copy this aspect, so it must match.
                camera.aspect = (float)Width / Height;
                // Freeze NPCs and physics: only the options may change between presets.
                Time.timeScale = 0f;
                presetIndex = 0;
                viewIndex = 0;
                ApplyPreset(0);
                PoseCamera(camera, Views[0]);
                renderAtFrame = Time.frameCount + 6;
                return;
            }
            // Each view gets its own frames: mirror cameras reflect the pose of
            // the last rendered frame, so the camera must sit there first.
            if (Time.frameCount < renderAtFrame) return;

            var preset = Presets[presetIndex];
            var view = Views[viewIndex];
            string directory = Path.Combine(Directory.GetParent(Application.dataPath).Parent.FullName,
                "Logs", "agent", "matrix");
            Directory.CreateDirectory(directory);
            PoseCamera(camera, view);
            Color[] grid = Render(camera, Path.Combine(directory, $"{preset.name}-{view.name}.png"),
                out float speckRatio);
            // The first-person arms idle-bob between captures: views that show them
            // compare only the top rows, and the arms are checked for specks instead.
            bool withArms = view.name.StartsWith("start", StringComparison.Ordinal);
            int firstCell = withArms ? GridX * (GridY * 2 / 5) : 0;
            if (presetIndex == 0)
            {
                Reference[view.name] = grid;
                ReferenceSpecks[view.name] = speckRatio;
            }
            else
            {
                if (withArms && speckRatio > ReferenceSpecks[view.name] + MaxSpeckIncrease)
                    Failures.Add($"{preset.name}/{view.name} skin specks {speckRatio:P2} vs {ReferenceSpecks[view.name]:P2}");
                Color[] baseline = Reference[view.name];
                float worst = 0f;
                int worstCell = firstCell;
                for (int c = firstCell; c < grid.Length; c++)
                {
                    float delta = Mathf.Max(Mathf.Abs(grid[c].r - baseline[c].r),
                        Mathf.Abs(grid[c].g - baseline[c].g), Mathf.Abs(grid[c].b - baseline[c].b));
                    if (delta > worst) { worst = delta; worstCell = c; }
                }
                Debug.Log(string.Format(CultureInfo.InvariantCulture,
                    "GYMCHAOS_SETTINGS_MATRIX_SAMPLE preset={0} view={1} worstDelta={2:F3} cell={3},{4} specks={5:P2}",
                    preset.name, view.name, worst, worstCell % GridX, worstCell / GridX, speckRatio));
                if (worst > preset.maxDelta)
                    Failures.Add($"{preset.name}/{view.name} delta={worst:F2} cell={worstCell % GridX},{worstCell / GridX}");
            }

            viewIndex++;
            if (viewIndex >= Views.Length)
            {
                viewIndex = 0;
                presetIndex++;
                if (presetIndex < Presets.Length) ApplyPreset(presetIndex);
            }
            if (presetIndex < Presets.Length)
            {
                PoseCamera(camera, Views[viewIndex]);
                renderAtFrame = Time.frameCount + (viewIndex == 0 ? 6 : 2);
                return;
            }
            done = true;
            CheckArmTextures();
            if (Failures.Count > 0)
                throw new InvalidOperationException("GYMCHAOS_SETTINGS_MATRIX_FAIL " + string.Join("; ", Failures));
            Debug.Log($"GYMCHAOS_SETTINGS_MATRIX_OK presets={Presets.Length} views={Views.Length}");
            resultCode = 0;
        }
        catch (Exception exception)
        {
            done = true;
            Debug.LogException(exception);
            resultCode = 1;
        }
        if (!done) return;
        if (savedPrefs != null)
        {
            for (int i = 0; i < PrefKeys.Length; i++)
            {
                if (savedPrefs[i].has) PlayerPrefs.SetInt(PrefKeys[i], savedPrefs[i].value);
                else PlayerPrefs.DeleteKey(PrefKeys[i]);
            }
            PlayerPrefs.Save();
        }
        GymChaosVerifierExit.Record(resultCode);
        EditorApplication.update -= Tick;
        EditorApplication.isPlaying = false;
    }

    private static void PoseCamera(Camera camera, (string name, Vector3 position, float yaw, float pitch) view)
    {
        Vector3 position = view.name == "mirror" ? mirrorEye
            : view.name.StartsWith("start", StringComparison.Ordinal) ? bootPosition : view.position;
        Quaternion rotation = view.name.StartsWith("start", StringComparison.Ordinal) ? bootRotation
            : Quaternion.Euler(view.pitch, view.yaw, 0f);
        // The body stays put: moving it or toggling the cinematic lock sends the
        // first-person arms into pose transitions that freeze at timeScale 0.
        camera.transform.SetPositionAndRotation(position, rotation);
        // 21:9 for the start screen on an ultrawide monitor, 16:9 otherwise.
        camera.aspect = view.name == "startwide" ? 21f / 9f : (float)Width / Height;
    }

    // The arms share the body's scan atlas: with a mip chain, TEXTURE below FULL
    // drops them to mips that blend UV islands into specks and dark holes.
    private static void CheckArmTextures()
    {
        int checkedCount = 0;
        foreach (Renderer renderer in player.GetComponentsInChildren<Renderer>(true))
        {
            if (renderer.gameObject.layer != PlanarGymMirror.FirstPersonPlayerLayer) continue;
            foreach (Material material in renderer.sharedMaterials)
            {
                Texture texture = material != null && material.HasProperty("_BaseMap") ? material.GetTexture("_BaseMap") : null;
                if (texture == null) continue;
                checkedCount++;
                if (texture.mipmapCount > ScanTextureMips.DefaultLevels)
                    Failures.Add($"arm texture {texture.name} keeps {texture.mipmapCount} mips");
            }
        }
        if (checkedCount == 0) Failures.Add("no first-person arm textures found");
        Debug.Log($"GYMCHAOS_SETTINGS_MATRIX_ARMS textures={checkedCount}");
    }

    private static void ApplyPreset(int index)
    {
        var preset = Presets[index];
        GymRuntimeSettings.ApplyForVerification(preset.texture, preset.aa, preset.scale, preset.shadows, preset.post);
    }

    // Renders the real player camera and returns a GridX x GridY grid of mean sRGB colours.
    private static Color[] Render(Camera camera, string png, out float speckRatio)
    {
        int width = Mathf.RoundToInt(Height * camera.aspect);
        RenderTexture target = new RenderTexture(width, Height, 24, RenderTextureFormat.ARGB32);
        RenderTexture previousTarget = camera.targetTexture;
        camera.targetTexture = target;
        camera.Render();
        camera.targetTexture = previousTarget;
        RenderTexture previous = RenderTexture.active;
        RenderTexture.active = target;
        Texture2D image = new Texture2D(width, Height, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0, 0, width, Height), 0, 0);
        image.Apply();
        RenderTexture.active = previous;
        File.WriteAllBytes(png, image.EncodeToPNG());
        Color32[] pixels = image.GetPixels32();
        int specks = 0;
        for (int i = 0; i < pixels.Length; i++)
        {
            Color.RGBToHSV(pixels[i], out float h, out float sat, out float v);
            if (h > 0.1f && h < 0.18f && sat > 0.45f && v > 0.45f) specks++;
        }
        speckRatio = (float)specks / pixels.Length;
        Color[] grid = new Color[GridX * GridY];
        int cellW = width / GridX;
        int cellH = Height / GridY;
        for (int gy = 0; gy < GridY; gy++)
            for (int gx = 0; gx < GridX; gx++)
            {
                double r = 0d, g = 0d, b = 0d;
                for (int y = gy * cellH; y < (gy + 1) * cellH; y++)
                    for (int x = gx * cellW; x < (gx + 1) * cellW; x++)
                    {
                        Color32 p = pixels[y * width + x];
                        r += p.r; g += p.g; b += p.b;
                    }
                double n = cellW * cellH * 255d;
                grid[gy * GridX + gx] = new Color((float)(r / n), (float)(g / n), (float)(b / n));
            }
        UnityEngine.Object.DestroyImmediate(image);
        target.Release();
        UnityEngine.Object.DestroyImmediate(target);
        return grid;
    }
}
#endif
