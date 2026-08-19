using System;
using UnityEngine;

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
    private float windowBottom;
    private float windowTop;
    private float exteriorZ;
    private float time01;
    private int currentDay;
    private bool configured;
    private bool loggedNight;

    private Light sunLight;
    private Light moonLight;
    private Renderer skyRenderer;
    private Renderer horizonRenderer;
    private Renderer sunRenderer;
    private Renderer moonRenderer;
    private Material skyMaterial;
    private Material horizonMaterial;
    private Material sunMaterial;
    private Material moonMaterial;
    private Light[] windowSunLights;

    private readonly Color dayAmbientSky = new Color(0.32f, 0.4f, 0.54f);
    private readonly Color dayAmbientEquator = new Color(0.18f, 0.2f, 0.25f);
    private readonly Color dayAmbientGround = new Color(0.035f, 0.045f, 0.075f);

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
        windowBottom = openingBottom;
        windowTop = openingTop;
        exteriorZ = center.z + depth * 0.5f + 8f;
        time01 = Mathf.Repeat(startTime01, 1f);
        configured = true;
        CacheSceneVisuals();
        ApplyVisuals();
        Debug.Log(
            $"GYMCHAOS_TIME_OK dayLength={simulatedDayLengthSeconds:F1}s start={time01:F3}",
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
        skyRenderer = FindRenderer("Exterior sky backdrop");
        horizonRenderer = FindRenderer("Exterior green horizon");
        sunRenderer = FindRenderer("Exterior visible sun");
        moonRenderer = FindRenderer("Exterior visible moon");
        skyMaterial = GetRuntimeMaterial(skyRenderer);
        horizonMaterial = GetRuntimeMaterial(horizonRenderer);
        sunMaterial = GetRuntimeMaterial(sunRenderer);
        moonMaterial = GetRuntimeMaterial(moonRenderer);

        sunLight = FindLight("Warm exterior sun");
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
        float exteriorWidth = Mathf.Max(8f, roomWidth * 0.38f);
        float visibleHeight = Mathf.Max(1f, windowTop - windowBottom);

        if (sunRenderer != null)
        {
            sunRenderer.enabled = daylight >= 0.25f;
            sunRenderer.transform.position = new Vector3(
                roomCenter.x + Mathf.Cos(sunAngle) * exteriorWidth,
                roomCenter.y + windowBottom + visibleHeight * (0.2f + 0.75f * Mathf.Max(0f, Mathf.Sin(sunAngle))),
                exteriorZ - 0.4f);
        }
        if (moonRenderer != null)
        {
            moonRenderer.enabled = night >= 0.25f;
            moonRenderer.transform.position = new Vector3(
                roomCenter.x + Mathf.Cos(moonAngle) * exteriorWidth * 0.85f,
                roomCenter.y + windowBottom + visibleHeight * (0.25f + 0.65f * Mathf.Max(0f, Mathf.Sin(moonAngle))),
                exteriorZ - 0.45f);
        }

        SetMaterialColor(skyMaterial, Color.Lerp(
            new Color(0.18f, 0.48f, 0.86f),
            new Color(0.008f, 0.015f, 0.055f), night));
        SetMaterialEmission(skyMaterial, Color.Lerp(
            new Color(0.22f, 0.58f, 1.1f),
            new Color(0.004f, 0.008f, 0.025f), night));
        SetMaterialColor(horizonMaterial, Color.Lerp(
            new Color(0.075f, 0.2f, 0.07f),
            new Color(0.008f, 0.018f, 0.035f), night));
        SetMaterialColor(sunMaterial, Color.Lerp(
            new Color(1f, 0.74f, 0.24f),
            new Color(0.08f, 0.045f, 0.02f), night));
        SetMaterialEmission(sunMaterial, Color.Lerp(
            new Color(8f, 5.5f, 1.4f),
            Color.black, night));
        SetMaterialColor(moonMaterial, Color.Lerp(
            new Color(0.05f, 0.07f, 0.13f),
            new Color(0.78f, 0.86f, 1f), night));
        SetMaterialEmission(moonMaterial, Color.Lerp(
            Color.black,
            new Color(1.25f, 1.55f, 2.4f), night));

        if (sunLight != null)
        {
            sunLight.intensity = 1.15f * daylight;
            sunLight.color = Color.Lerp(
                new Color(1f, 0.79f, 0.6f),
                new Color(0.16f, 0.12f, 0.18f), night);
            sunLight.transform.rotation = Quaternion.Euler(
                28f + Mathf.Max(0f, Mathf.Sin(sunAngle)) * 54f,
                -32f + time01 * 360f,
                0f);
        }
        if (moonLight != null)
        {
            moonLight.intensity = 0.32f * night;
            moonLight.transform.rotation = Quaternion.Euler(
                35f + Mathf.Max(0f, Mathf.Sin(moonAngle)) * 35f,
                148f + time01 * 360f,
                0f);
        }
        for (int i = 0; i < windowSunLights.Length; i++)
        {
            if (windowSunLights[i] != null)
            {
                windowSunLights[i].intensity = 1450f * daylight;
            }
        }

        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = Color.Lerp(dayAmbientSky, new Color(0.012f, 0.02f, 0.06f), night);
        RenderSettings.ambientEquatorColor = Color.Lerp(dayAmbientEquator, new Color(0.018f, 0.022f, 0.045f), night);
        RenderSettings.ambientGroundColor = Color.Lerp(dayAmbientGround, new Color(0.006f, 0.008f, 0.018f), night);
        RenderSettings.ambientIntensity = Mathf.Lerp(1.12f, 0.38f, night);
        RenderSettings.sun = daylight >= night ? sunLight : moonLight;

        if (night > 0.7f && !loggedNight)
        {
            loggedNight = true;
            Debug.Log($"GYMCHAOS_NIGHT_VISIBLE day={currentDay} time={time01:F3}", this);
        }
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
}
