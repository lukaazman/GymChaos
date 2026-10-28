#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Grounded art direction in Play Mode: neutral ground and walls (asphalt,
/// concrete, rubber, tile with low saturation), desaturated grass, emission
/// only on light sources (never on paint or building fabric), exactly one
/// enabled directional light (the only shadowed one) and fog on, by day and
/// by night.
/// </summary>
[InitializeOnLoad]
public static class GymChaosPaletteVerifier
{
    private const string Key = "GymChaos.PaletteVerificationRequested";
    private static bool launched;

    static GymChaosPaletteVerifier()
    {
        GymChaosVerifierHooks.Hook(Tick, OnPlayModeChanged);
    }

    [MenuItem("Tools/GymChaos/Run Palette Verification")]
    public static void Run()
    {
        launched = false;
        GymChaosVerifierExit.Record(1);
        GymChaosVerifierSave.Neutralize();
        SessionState.SetBool(Key, true);
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        EditorApplication.isPlaying = true;
    }

    private static void OnPlayModeChanged(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredEditMode || !SessionState.GetBool(Key, false)) return;
        SessionState.EraseBool(Key);
        if (Application.isBatchMode) GymChaosVerifierExit.Exit(1);
    }

    private static void Tick()
    {
        if (!SessionState.GetBool(Key, false) || !EditorApplication.isPlaying || launched) return;
        if (UnityEngine.Object.FindFirstObjectByType<PlayerMovement>() == null) return;
        launched = true;
        new GameObject("Palette verifier").AddComponent<GymChaosPaletteVerifierRunner>();
    }

    public static void Finish(int code)
    {
        GymChaosVerifierExit.Record(code);
        EditorApplication.isPlaying = false;
    }
}

public sealed class GymChaosPaletteVerifierRunner : MonoBehaviour
{
    // Material-name words of real light sources and glowing gameplay FX.
    private static readonly string[] LightWords =
    {
        "light", "lamp", "glow", "sign", "screen", "display", "monitor", "window", "neon", "sun", "moon",
        "star", "led", "fx", "aura", "flame", "spark", "magic", "halo", "headlight", "tail", "beacon",
        "siren", "exit", "console", "panel", "bulb", "fixture", "tv", "nimbus", "rainbow", "chevron",
        "arrow", "marker", "ring", "fridge"
    };
    private static readonly string[] FabricWords =
    {
        "paint", "marking", "line", "coping", "trim", "wall", "floor", "asphalt", "concrete", "curb"
    };

    private readonly List<string> failures = new List<string>();

    private IEnumerator Start()
    {
        // Let the builders, foliage bake and GLB loads finish.
        float until = Time.realtimeSinceStartup + 25f;
        while ((GameObject.Find(GymOutdoorFoliage.DetailsRootName) == null ||
                !(GymProteinStoreEnvironment.IsLoaded || GymProteinStoreEnvironment.LoadFailed)) &&
               Time.realtimeSinceStartup < until)
            yield return null;
        yield return new WaitForSecondsRealtime(3f);
        try
        {
            CheckSurfaces();
            CheckEmission();
            GymTimeOfDay time = FindFirstObjectByType<GymTimeOfDay>();
            if (time == null) throw new InvalidOperationException("GymTimeOfDay missing");
            float original = time.Time01;
            time.SetTimeForVerification(0.5f);
            CheckLighting("day", sunWarm: true);
            time.SetTimeForVerification(0.95f);
            CheckLighting("night", sunWarm: false);
            time.SetTimeForVerification(original);
        }
        catch (Exception exception)
        {
            failures.Add(exception.Message);
        }

        if (failures.Count > 0)
        {
            Debug.LogError($"GYMCHAOS_PALETTE_FAIL count={failures.Count} " + string.Join(" | ", failures));
            GymChaosPaletteVerifier.Finish(1);
            yield break;
        }
        Debug.Log("GYMCHAOS_PALETTE_OK");
        GymChaosPaletteVerifier.Finish(0);
    }

    private void Require(bool condition, string message)
    {
        if (!condition) failures.Add(message);
    }

    // ---- surfaces ----

    private void CheckSurfaces()
    {
        // Procedural textured surfaces: texture mean (their _BaseColor is white).
        Textured("Outdoor parking asphalt", 0.10f, 0.08f, 0.25f);
        Textured("Outdoor concrete path", 0.10f, 0.18f, 0.45f);
        Textured("Exterior courtyard foundation", 0.12f, 0.10f, 0.50f);
        Textured("Gym rubber floor", 0.15f, 0.04f, 0.25f);
        Textured("Locker room rubber floor", 0.15f, 0.04f, 0.30f);
        Textured("Bathroom tile", 0.15f, 0.30f, 0.80f);

        // Plain lit materials in the scene, by name.
        Dictionary<string, Material> scene = SceneMaterials();
        foreach (string name in new[] { "Outdoor curb", "Exterior boundary wall" })
        {
            Require(scene.TryGetValue(name, out Material material), $"material '{name}' not in scene");
            if (material != null) Check(name, material.GetColor("_BaseColor"), 0.12f, 0.10f, 0.50f);
        }

        // The park lawn texture and grass blades are baked from these palette colours.
        Check("LawnDark", GymPalette.LawnDark, 0.45f, 0.12f, 0.5f, 60f, 130f);
        Check("LawnLight", GymPalette.LawnLight, 0.45f, 0.12f, 0.5f, 60f, 130f);
        Check("GrassRoot", GymPalette.GrassRoot, 0.45f, 0.12f, 0.5f, 60f, 130f);
        Check("GrassTipShort", GymPalette.GrassTipShort, 0.45f, 0.12f, 0.5f, 60f, 130f);
        Check("GrassTipLong", GymPalette.GrassTipLong, 0.45f, 0.12f, 0.5f, 60f, 130f);
        Check("Foliage", GymPalette.Foliage, 0.45f, 0.12f, 0.5f, 60f, 130f);
    }

    private void Textured(string name, float maxS, float minV, float maxV, float minH = -1f, float maxH = 361f)
    {
        if (!GymSurfaceMaterialFactory.TryGetMeanAlbedo(name, out Color color))
        {
            failures.Add($"surface '{name}' was never built");
            return;
        }
        Check(name, color, maxS, minV, maxV, minH, maxH);
    }

    private void Check(string name, Color color, float maxS, float minV, float maxV, float minH = -1f, float maxH = 361f)
    {
        Color.RGBToHSV(color, out float h, out float s, out float v);
        h *= 360f;
        bool ok = s <= maxS && v >= minV && v <= maxV && h >= minH && h <= maxH;
        string line = $"{name} h={h:F0} s={s:F2} v={v:F2}";
        Debug.Log("GYMCHAOS_PALETTE_SURFACE " + line + (ok ? " ok" : " OUT"));
        Require(ok, "surface " + line + $" outside s<={maxS} v={minV}-{maxV} h={minH}-{maxH}");
    }

    private static Dictionary<string, Material> SceneMaterials()
    {
        var result = new Dictionary<string, Material>();
        foreach (Renderer renderer in FindObjectsByType<Renderer>(FindObjectsInactive.Exclude))
            foreach (Material material in renderer.sharedMaterials)
                if (material != null) result[material.name.Replace(" (Instance)", "")] = material;
        return result;
    }

    // ---- emission ----

    private void CheckEmission()
    {
        var offenders = new SortedSet<string>();
        int emissive = 0;
        foreach (Material material in SceneMaterials().Values)
        {
            if (!material.IsKeywordEnabled("_EMISSION") || !material.HasProperty("_EmissionColor")) continue;
            if (material.GetColor("_EmissionColor").maxColorComponent <= 0.02f) continue;
            emissive++;
            string lower = material.name.ToLowerInvariant();
            bool light = LightWords.Any(lower.Contains);
            bool fabric = FabricWords.Any(lower.Contains);
            if (!light || fabric) offenders.Add(material.name);
        }
        Debug.Log($"GYMCHAOS_PALETTE_EMISSION emissive={emissive} offenders={offenders.Count}");
        Require(offenders.Count == 0, "emissive non-light materials: " + string.Join(", ", offenders.Take(25)));
    }

    // ---- lighting ----

    private void CheckLighting(string label, bool sunWarm)
    {
        Light[] directional = FindObjectsByType<Light>(FindObjectsInactive.Exclude)
            .Where(l => l.type == LightType.Directional && l.enabled && l.isActiveAndEnabled).ToArray();
        Require(directional.Length == 1,
            $"{label}: {directional.Length} enabled directional lights ({string.Join(",", directional.Select(l => l.name))})");
        Require(RenderSettings.fog, $"{label}: fog off");
        if (directional.Length != 1) return;
        Light main = directional[0];
        Require(RenderSettings.sun == main, $"{label}: RenderSettings.sun is not the enabled directional light");
        Color.RGBToHSV(main.color, out float h, out float s, out float v);
        string line = $"{label} light={main.name} shadows={main.shadows} h={h * 360f:F0} s={s:F2} " +
            $"intensity={main.intensity:F2} fog={RenderSettings.fogMode}/{RenderSettings.fogDensity:F4}";
        Debug.Log("GYMCHAOS_PALETTE_LIGHT " + line);
        if (sunWarm)
        {
            Require(main.shadows != LightShadows.None, $"{label}: sun casts no shadows");
            // Kelvin sun: warm white, not saturated yellow.
            Require(main.color.r >= main.color.g && main.color.g >= main.color.b && s <= 0.3f,
                "sun colour not a warm white: " + line);
        }
        else
        {
            Require(main.color.b >= main.color.r && s <= 0.35f, "moon colour not a cool white: " + line);
        }
    }
}
#endif
