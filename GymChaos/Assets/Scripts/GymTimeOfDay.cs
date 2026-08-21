using System;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Single source of truth for the short simulated day used by the gym. It
/// drives both the visible outdoor view and the systems that use day quotas.
/// </summary>
[DefaultExecutionOrder(-40)]
public sealed class GymTimeOfDay : MonoBehaviour
{
    public static GymTimeOfDay Instance { get; private set; }

    [SerializeField, Min(30f)] private float simulatedDayLengthSeconds = 90f;
    [SerializeField, Range(0f, 1f)] private float startTime01 = 0.24f;

    private Vector3 roomCenter;
    private float roomWidth;
    private float roomDepth;
    private float time01;
    private int currentDay;
    private bool configured;
    private bool loggedNight;

    private Light sunLight;
    private Light moonLight;
    private Renderer moonRenderer;
    private Renderer moonHaloRenderer;
    private Material moonMaterial;
    private Material moonHaloMaterial;
    private Material proceduralSkyboxMaterial;
    private Material previousSkyboxMaterial;
    private bool ownsProceduralSkybox;
    private Light[] windowSunLights;
    private bool loggedLighting;
    private bool lastUsingSun;

    private readonly Color dayAmbientSky = new Color(0.3f, 0.44f, 0.66f);
    private readonly Color dayAmbientEquator = new Color(0.14f, 0.22f, 0.34f);
    private readonly Color dayAmbientGround = new Color(0.025f, 0.05f, 0.09f);

    public event Action<int> DayChanged;

    public float Time01 => time01;
    public int CurrentDay => currentDay;
    public float SimulatedDayLengthSeconds => simulatedDayLengthSeconds;
    public bool IsNight => CalculateDaylight(time01) < 0.25f;

    public static GymTimeOfDay CreateForScene(
        Transform parent,
        Vector3 center,
        float width,
        float depth,
        float openingBottom,
        float openingTop)
    {
        GymTimeOfDay existing = FindAnyObjectByType<GymTimeOfDay>();
        if (existing != null)
        {
            existing.Configure(center, width, depth, openingBottom, openingTop);
            return existing;
        }

        GameObject timeObject = new GameObject("Gym Time Of Day");
        timeObject.transform.SetParent(parent, true);
        GymTimeOfDay time = timeObject.AddComponent<GymTimeOfDay>();
        time.Configure(center, width, depth, openingBottom, openingTop);
        return time;
    }

    public void Configure(
        Vector3 center,
        float width,
        float depth,
        float openingBottom,
        float openingTop)
    {
        roomCenter = center;
        roomWidth = width;
        roomDepth = depth;
        time01 = Mathf.Repeat(startTime01, 1f);
        configured = true;
        CacheSceneVisuals();
        ApplyVisuals();
        Debug.Log(
            $"GYMCHAOS_TIME_OK dayLength={simulatedDayLengthSeconds:F1}s start={time01:F3} " +
            $"skybox={(proceduralSkyboxMaterial != null ? "procedural" : "fallback")} sun=single-sky-disk moon=3d",
            this);
    }

    public void SetTimeForVerification(float value01, bool advanceDayIfWrapped = false)
    {
        float normalized = Mathf.Repeat(value01, 1f);
        bool wrapped = advanceDayIfWrapped && normalized < time01;
        if (wrapped)
        {
            currentDay++;
        }
        time01 = normalized;
        if (wrapped)
        {
            // Apply the new time before notifying the scheduler so its next
            // day's schedule is built from the post-midnight value.
            DayChanged?.Invoke(currentDay);
        }
        ApplyVisuals();
    }

    public void AdvanceForVerification(float seconds)
    {
        if (seconds <= 0f || simulatedDayLengthSeconds <= 0.01f)
        {
            return;
        }

        Advance(seconds);
        ApplyVisuals();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }

        Instance = this;
    }

    private void Start()
    {
        if (configured)
        {
            CacheSceneVisuals();
            ApplyVisuals();
        }
    }

    private void OnDestroy()
    {
        if (ownsProceduralSkybox && RenderSettings.skybox == proceduralSkyboxMaterial)
        {
            RenderSettings.skybox = previousSkyboxMaterial;
        }

        if (proceduralSkyboxMaterial != null)
        {
            Destroy(proceduralSkyboxMaterial);
            proceduralSkyboxMaterial = null;
        }

        if (Instance == this)
        {
            Instance = null;
        }
    }

    private void Update()
    {
        if (!configured)
        {
            return;
        }

        Advance(Time.deltaTime);
        ApplyVisuals();
    }

    private void Advance(float seconds)
    {
        float previous = time01;
        time01 += seconds / Mathf.Max(30f, simulatedDayLengthSeconds);
        while (time01 >= 1f)
        {
            time01 -= 1f;
            currentDay++;
            DayChanged?.Invoke(currentDay);
            loggedNight = false;
            Debug.Log($"GYMCHAOS_DAY_CHANGED day={currentDay}", this);
        }

        if (previous < 0.75f && time01 >= 0.75f)
        {
            loggedNight = false;
        }
    }

    private void CacheSceneVisuals()
    {
        moonRenderer = FindRenderer("Exterior visible moon");
        moonHaloRenderer = FindRenderer("Exterior moon halo");
        moonMaterial = GetRuntimeMaterial(moonRenderer);
        moonHaloMaterial = GetRuntimeMaterial(moonHaloRenderer);
        EnsureProceduralSkybox();

        sunLight = FindLight("Warm exterior sun");
        if (sunLight == null)
        {
            GameObject sunObject = new GameObject("Warm exterior sun");
            sunObject.transform.SetParent(transform, true);
            sunLight = sunObject.AddComponent<Light>();
            sunLight.type = LightType.Directional;
            sunLight.color = new Color(0.72f, 0.86f, 1f);
            sunLight.shadows = LightShadows.Soft;
            sunLight.shadowStrength = 0.78f;
        }

        moonLight = FindLight("Cool moonlight");
        if (moonLight == null)
        {
            GameObject moonObject = new GameObject("Cool moonlight");
            moonObject.transform.SetParent(transform, true);
            moonLight = moonObject.AddComponent<Light>();
            moonLight.type = LightType.Directional;
            moonLight.color = new Color(0.34f, 0.46f, 0.78f);
            moonLight.shadows = LightShadows.Soft;
            moonLight.shadowStrength = 0.22f;
        }

        sunLight.type = LightType.Directional;
        moonLight.type = LightType.Directional;
        sunLight.enabled = true;
        moonLight.enabled = false;

        Light[] allLights = FindObjectsByType<Light>();
        System.Collections.Generic.List<Light> windowLights =
            new System.Collections.Generic.List<Light>();
        for (int i = 0; i < allLights.Length; i++)
        {
            if (allLights[i] != null && allLights[i].name.Contains("Window sunlight"))
            {
                windowLights.Add(allLights[i]);
            }
        }
        windowSunLights = windowLights.ToArray();
    }

    private void ApplyVisuals()
    {
        if (!configured)
        {
            return;
        }

        float daylight = CalculateDaylight(time01);
        float night = 1f - daylight;
        float sunAngle = (time01 - 0.25f) * Mathf.PI * 2f;
        float moonAngle = sunAngle + Mathf.PI;
        float celestialRadius = Mathf.Max(roomWidth, roomDepth) * 1.85f + 95f;
        Vector3 celestialCenter = roomCenter + Vector3.up * 2f;
        Vector3 sunPosition = GetCelestialPosition(celestialCenter, sunAngle, celestialRadius);
        Vector3 moonPosition = GetCelestialPosition(
            celestialCenter, moonAngle, celestialRadius * 0.94f);

        if (moonRenderer != null)
        {
            moonRenderer.enabled = night >= 0.25f;
            moonRenderer.transform.position = moonPosition;
        }
        if (moonHaloRenderer != null)
        {
            moonHaloRenderer.enabled = night >= 0.25f;
            moonHaloRenderer.transform.position = moonPosition;
        }

        SetMaterialColor(moonMaterial, Color.Lerp(
            new Color(0.08f, 0.11f, 0.2f),
            new Color(0.78f, 0.86f, 1f), night));
        SetMaterialEmission(moonMaterial, Color.Lerp(
            Color.black,
            new Color(2.2f, 3.5f, 6f), night));
        SetMaterialColor(moonHaloMaterial, Color.Lerp(
            new Color(0.08f, 0.18f, 0.42f, 0f),
            new Color(0.2f, 0.45f, 1f, 0.13f), night));
        SetMaterialEmission(moonHaloMaterial, Color.Lerp(
            Color.black, new Color(1.2f, 2.2f, 5f), night));

        if (sunLight != null)
        {
            sunLight.intensity = 1.15f * daylight;
            sunLight.color = Color.Lerp(
                new Color(0.72f, 0.86f, 1f),
                new Color(0.08f, 0.12f, 0.28f), night);
            sunLight.transform.rotation = Quaternion.LookRotation(
                (sunPosition - roomCenter).normalized, Vector3.up);
        }
        if (moonLight != null)
        {
            moonLight.intensity = 0.32f * night;
            moonLight.transform.rotation = Quaternion.LookRotation(
                (moonPosition - roomCenter).normalized, Vector3.up);
        }

        // URP has one main directional light. Keep the day/night pair
        // mutually exclusive so WebGL cannot select the stale scene sun
        // instead of the moon when the simulated night starts.
        bool useSun = daylight >= night;
        if (sunLight != null)
        {
            sunLight.enabled = useSun && daylight > 0.001f;
        }
        if (moonLight != null)
        {
            moonLight.enabled = !useSun && night > 0.001f;
        }

        for (int i = 0; i < windowSunLights.Length; i++)
        {
            if (windowSunLights[i] != null)
            {
                windowSunLights[i].intensity = 1450f * daylight;
                windowSunLights[i].enabled = daylight > 0.001f;
            }
        }

        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = Color.Lerp(dayAmbientSky, new Color(0.012f, 0.025f, 0.09f), night);
        RenderSettings.ambientEquatorColor = Color.Lerp(dayAmbientEquator, new Color(0.012f, 0.03f, 0.08f), night);
        RenderSettings.ambientGroundColor = Color.Lerp(dayAmbientGround, new Color(0.006f, 0.014f, 0.04f), night);
        RenderSettings.ambientIntensity = Mathf.Lerp(1.08f, 0.38f, night);
        RenderSettings.sun = useSun ? sunLight : moonLight;
        UpdateProceduralSkybox(night);

        if (!loggedLighting || lastUsingSun != useSun)
        {
            loggedLighting = true;
            lastUsingSun = useSun;
            string pipeline = GraphicsSettings.currentRenderPipeline != null
                ? GraphicsSettings.currentRenderPipeline.name
                : "BuiltIn";
            Debug.Log(
                $"GYMCHAOS_LIGHTING_OK pipeline={pipeline} " +
                $"sunEnabled={sunLight != null && sunLight.enabled} " +
                $"moonEnabled={moonLight != null && moonLight.enabled} " +
                $"active={(useSun ? "sun" : "moon")} " +
                $"windowLights={windowSunLights.Length} daylight={daylight:F3}",
                this);
        }

        if (night > 0.7f && !loggedNight)
        {
            loggedNight = true;
            Debug.Log($"GYMCHAOS_NIGHT_VISIBLE day={currentDay} time={time01:F3}", this);
        }
    }

    private void EnsureProceduralSkybox()
    {
        if (proceduralSkyboxMaterial != null)
        {
            return;
        }

        Shader shader = Shader.Find("Skybox/Procedural");
        if (shader == null)
        {
            Debug.LogWarning("Gym exterior could not create the procedural skybox because Skybox/Procedural is missing.", this);
            return;
        }

        previousSkyboxMaterial = RenderSettings.skybox;
        proceduralSkyboxMaterial = new Material(shader);
        proceduralSkyboxMaterial.name = "Gym Exterior Procedural Sky";
        proceduralSkyboxMaterial.SetFloat("_SunSize", 0.035f);
        proceduralSkyboxMaterial.SetFloat("_SunSizeConvergence", 5f);
        proceduralSkyboxMaterial.SetFloat("_AtmosphereThickness", 1.1f);
        proceduralSkyboxMaterial.SetFloat("_Exposure", 1f);
        if (proceduralSkyboxMaterial.HasProperty("_SunDisk"))
        {
            proceduralSkyboxMaterial.SetFloat("_SunDisk", 1f);
        }

        RenderSettings.skybox = proceduralSkyboxMaterial;
        ownsProceduralSkybox = true;
    }

    private void UpdateProceduralSkybox(float night)
    {
        if (proceduralSkyboxMaterial == null)
        {
            return;
        }

        SetSkyboxColor(
            proceduralSkyboxMaterial,
            "_SkyTint",
            Color.Lerp(new Color(0.42f, 0.62f, 0.9f), new Color(0.018f, 0.04f, 0.14f), night));
        SetSkyboxColor(
            proceduralSkyboxMaterial,
            "_GroundColor",
            Color.Lerp(new Color(0.1f, 0.18f, 0.28f), new Color(0.008f, 0.016f, 0.04f), night));
        SetSkyboxColor(
            proceduralSkyboxMaterial,
            "_SunColor",
            Color.Lerp(new Color(0.88f, 0.96f, 1f), new Color(0.06f, 0.12f, 0.34f), night));
        proceduralSkyboxMaterial.SetFloat(
            "_AtmosphereThickness", Mathf.Lerp(1.15f, 0.78f, night));
        proceduralSkyboxMaterial.SetFloat(
            "_Exposure", Mathf.Lerp(1.04f, 0.62f, night));
        proceduralSkyboxMaterial.SetFloat("_SunSize", Mathf.Lerp(0.045f, 0.028f, night));
        proceduralSkyboxMaterial.SetFloat("_SunSizeConvergence", 5f);
        if (proceduralSkyboxMaterial.HasProperty("_SunDisk"))
        {
            // RenderSettings.sun points at the active moon after the night
            // hand-off. Hide the procedural disk then so the 3D moon is the
            // only visible celestial source instead of a duplicate disk.
            proceduralSkyboxMaterial.SetFloat("_SunDisk", night > 0.5f ? 0f : 1f);
        }
    }

    private static Vector3 GetCelestialPosition(Vector3 center, float angle, float radius)
    {
        Vector3 direction = new Vector3(
            Mathf.Cos(angle) * 0.62f,
            0.34f + Mathf.Sin(angle) * 0.48f,
            0.74f);
        return center + direction.normalized * radius;
    }

    private static float CalculateDaylight(float value01)
    {
        float sunrise = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.12f, 0.27f, value01));
        float sunset = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.68f, 0.84f, value01));
        return Mathf.Clamp01(Mathf.Min(sunrise, sunset));
    }

    private static Renderer FindRenderer(string objectName)
    {
        GameObject found = GameObject.Find(objectName);
        return found != null ? found.GetComponent<Renderer>() : null;
    }

    private static Light FindLight(string objectName)
    {
        GameObject found = GameObject.Find(objectName);
        return found != null ? found.GetComponent<Light>() : null;
    }

    private static Material GetRuntimeMaterial(Renderer renderer)
    {
        return renderer != null && renderer.sharedMaterial != null
            ? renderer.material
            : null;
    }

    private static void SetMaterialColor(Material material, Color color)
    {
        if (material == null)
        {
            return;
        }

        material.color = color;
        if (material.HasProperty("_BaseColor"))
        {
            material.SetColor("_BaseColor", color);
        }
        if (material.HasProperty("_Color"))
        {
            material.SetColor("_Color", color);
        }
    }

    private static void SetMaterialEmission(Material material, Color color)
    {
        if (material == null)
        {
            return;
        }

        material.EnableKeyword("_EMISSION");
        if (material.HasProperty("_EmissionColor"))
        {
            material.SetColor("_EmissionColor", color);
        }
    }

    private static void SetSkyboxColor(Material material, string propertyName, Color color)
    {
        if (material != null && material.HasProperty(propertyName))
        {
            material.SetColor(propertyName, color);
        }
    }
}
