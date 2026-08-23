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
    private float skyWindowBottom;
    private float skyWindowTop;
    private float time01;
    private int currentDay;
    private bool configured;
    private bool loggedNight;

    private Light sunLight;
    private Light moonLight;
    private Light sunGlowLight;
    private Light moonGlowLight;
    private Renderer sunRenderer;
    private Renderer moonRenderer;
    private Material sunMaterial;
    private Material moonMaterial;
    private Material proceduralSkyboxMaterial;
    private Material previousSkyboxMaterial;
    private bool ownsProceduralSkybox;
    private bool customGradientSkybox;
    private Light[] windowSunLights;
    private bool loggedLighting;
    private bool lastUsingSun;

    private ParticleSystem nightStars;
    private ParticleSystem dayClouds;
    private ParticleSystem.Particle[] starParticles;
    private ParticleSystem.Particle[] cloudParticles;
    private Color[] starBaseColors;
    private Color[] cloudBaseColors;
    private Vector3[] cloudBasePositions;
    private Material starParticleMaterial;
    private Material cloudParticleMaterial;
    private Texture2D starParticleTexture;
    private Texture2D cloudParticleTexture;
    private float cloudDrift;

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
        skyWindowBottom = openingBottom;
        skyWindowTop = openingTop;
        time01 = Mathf.Repeat(startTime01, 1f);
        configured = true;
        CacheSceneVisuals();
        ApplyVisuals();
        Debug.Log(
            $"GYMCHAOS_TIME_OK dayLength={simulatedDayLengthSeconds:F1}s start={time01:F3} " +
            $"skybox={(customGradientSkybox ? "gradient-procedural" : proceduralSkyboxMaterial != null ? "legacy-procedural" : "fallback")} " +
            $"effects={(customGradientSkybox ? "radial-stars-elevated-clouds" : "particle-fallback")} " +
            "celestials=single-emissive-shape glow=shared-color",
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

        if (starParticleMaterial != null)
        {
            Destroy(starParticleMaterial);
            starParticleMaterial = null;
        }
        if (cloudParticleMaterial != null)
        {
            Destroy(cloudParticleMaterial);
            cloudParticleMaterial = null;
        }
        if (starParticleTexture != null)
        {
            Destroy(starParticleTexture);
            starParticleTexture = null;
        }
        if (cloudParticleTexture != null)
        {
            Destroy(cloudParticleTexture);
            cloudParticleTexture = null;
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
        sunRenderer = FindRenderer("Exterior visible sun");
        moonRenderer = FindRenderer("Exterior visible moon");
        sunMaterial = GetRuntimeMaterial(sunRenderer);
        moonMaterial = GetRuntimeMaterial(moonRenderer);
        sunGlowLight = FindLight("Exterior sun glow");
        moonGlowLight = FindLight("Exterior moon glow");
        EnsureProceduralSkybox();
        EnsureSkyEffects();

        sunLight = FindLight("Warm exterior sun");
        if (sunLight == null)
        {
            GameObject sunObject = new GameObject("Warm exterior sun");
            sunObject.transform.SetParent(transform, true);
            sunLight = sunObject.AddComponent<Light>();
            sunLight.type = LightType.Directional;
            sunLight.color = new Color(1f, 0.84f, 0.12f);
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
            moonLight.color = Color.white;
            moonLight.shadows = LightShadows.Soft;
            moonLight.shadowStrength = 0.22f;
        }

        sunLight.type = LightType.Directional;
        moonLight.type = LightType.Directional;
        sunLight.enabled = true;
        moonLight.enabled = false;

        if (sunGlowLight != null)
        {
            sunGlowLight.type = LightType.Point;
            sunGlowLight.shadows = LightShadows.None;
        }
        if (moonGlowLight != null)
        {
            moonGlowLight.type = LightType.Point;
            moonGlowLight.shadows = LightShadows.None;
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
        float celestialRadius = Mathf.Max(roomWidth, roomDepth) * 1.85f + 95f;
        Vector3 celestialCenter = roomCenter + Vector3.up * 2f;
        Vector3 sunPosition = GetCelestialPosition(celestialCenter, sunAngle, celestialRadius);
        Vector3 moonPosition = GetCelestialPosition(
            celestialCenter, moonAngle, celestialRadius * 0.94f);

        if (sunRenderer != null)
        {
            sunRenderer.enabled = daylight >= 0.25f;
            sunRenderer.transform.position = sunPosition;
        }
        if (sunGlowLight != null)
        {
            sunGlowLight.enabled = daylight >= 0.25f;
            sunGlowLight.transform.position = sunPosition;
            sunGlowLight.color = new Color(1f, 0.84f, 0.12f);
            sunGlowLight.intensity = 8f * daylight;
            sunGlowLight.range = 32f;
        }
        if (moonRenderer != null)
        {
            moonRenderer.enabled = night >= 0.25f;
            moonRenderer.transform.position = moonPosition;
        }
        if (moonGlowLight != null)
        {
            moonGlowLight.enabled = night >= 0.25f;
            moonGlowLight.transform.position = moonPosition;
            moonGlowLight.color = Color.white;
            moonGlowLight.intensity = 4f * night;
            moonGlowLight.range = 30f;
        }

        SetMaterialColor(sunMaterial, Color.Lerp(
            new Color(0.08f, 0.1f, 0.2f),
            new Color(1f, 0.84f, 0.12f), daylight));
        SetMaterialEmission(sunMaterial, Color.Lerp(
            Color.black, new Color(10f, 7.2f, 0.8f), daylight));
        SetMaterialColor(moonMaterial, Color.Lerp(
            new Color(0.08f, 0.11f, 0.2f),
            Color.white, night));
        SetMaterialEmission(moonMaterial, Color.Lerp(
            Color.black,
            new Color(8f, 8f, 8f), night));

        if (sunLight != null)
        {
            sunLight.intensity = 1.15f * daylight;
            sunLight.color = Color.Lerp(
                new Color(1f, 0.84f, 0.12f),
                new Color(0.08f, 0.12f, 0.28f), night);
            sunLight.transform.rotation = Quaternion.LookRotation(
                (sunPosition - roomCenter).normalized, Vector3.up);
        }
        if (moonLight != null)
        {
            moonLight.intensity = 0.32f * night;
            moonLight.color = Color.white;
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
        EnsureGameplaySkyboxCamera(night);
        UpdateSkyEffects(daylight, night);

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
                $"sunGlow={sunGlowLight != null && sunGlowLight.enabled} " +
                $"moonGlow={moonGlowLight != null && moonGlowLight.enabled} " +
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

        Shader shader = Resources.Load<Shader>("GymGradientSky");
        if (shader == null)
        {
            shader = Shader.Find("GymChaos/GymGradientSky");
        }
        if (shader != null && shader.isSupported)
        {
            previousSkyboxMaterial = RenderSettings.skybox;
            proceduralSkyboxMaterial = new Material(shader);
            proceduralSkyboxMaterial.name = "Gym Exterior Gradient Sky";
            customGradientSkybox = true;
            RenderSettings.skybox = proceduralSkyboxMaterial;
            ownsProceduralSkybox = true;
            return;
        }

        shader = Shader.Find("Skybox/Procedural");
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

        if (customGradientSkybox)
        {
            float daylight = 1f - night;
            float cloudVisibility = Mathf.SmoothStep(
                0f, 1f, Mathf.InverseLerp(0.12f, 0.78f, daylight));
            // Hold the last cloud layer in the sky while it fades out, and
            // delay stars until the sky is genuinely deep enough that their
            // appearance reads as a continuous dusk transition rather than a
            // premature particle spawn.
            float starVisibility = Mathf.SmoothStep(
                0f, 1f, Mathf.InverseLerp(0.88f, 0.998f, night));
            cloudDrift = Mathf.Repeat(cloudDrift + Time.deltaTime * 0.11f, 1000f);
            SetSkyboxColor(
                proceduralSkyboxMaterial, "_DayHorizon",
                new Color(0.34f, 0.66f, 1f));
            SetSkyboxColor(
                proceduralSkyboxMaterial, "_DayZenith",
                new Color(0.045f, 0.22f, 0.68f));
            SetSkyboxColor(
                proceduralSkyboxMaterial, "_NightHorizon",
                new Color(0.025f, 0.08f, 0.22f));
            SetSkyboxColor(
                proceduralSkyboxMaterial, "_NightZenith",
                new Color(0.001f, 0.004f, 0.025f));
            SetSkyboxColor(
                proceduralSkyboxMaterial, "_CloudLight",
                new Color(0.94f, 0.98f, 1f));
            SetSkyboxColor(
                proceduralSkyboxMaterial, "_CloudShadow",
                new Color(0.32f, 0.48f, 0.68f));
            proceduralSkyboxMaterial.SetFloat("_Daylight", daylight);
            proceduralSkyboxMaterial.SetFloat("_CloudFade", cloudVisibility);
            proceduralSkyboxMaterial.SetFloat("_StarFade", starVisibility);
            proceduralSkyboxMaterial.SetFloat("_CloudOffset", cloudDrift);
            return;
        }

        SetSkyboxColor(
            proceduralSkyboxMaterial,
            "_SkyTint",
            Color.Lerp(new Color(0.62f, 0.82f, 1f), new Color(0.018f, 0.04f, 0.14f), night));
        SetSkyboxColor(
            proceduralSkyboxMaterial,
            "_GroundColor",
            Color.Lerp(new Color(0.22f, 0.36f, 0.56f), new Color(0.008f, 0.016f, 0.04f), night));
        SetSkyboxColor(
            proceduralSkyboxMaterial,
            "_SunColor",
            Color.Lerp(new Color(1f, 0.84f, 0.36f), new Color(0.06f, 0.12f, 0.34f), night));
        proceduralSkyboxMaterial.SetFloat(
            "_AtmosphereThickness", Mathf.Lerp(1.05f, 0.78f, night));
        proceduralSkyboxMaterial.SetFloat(
            "_Exposure", Mathf.Lerp(1.24f, 0.62f, night));
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

    private void EnsureGameplaySkyboxCamera(float night)
    {
        PlayerMovement player = FindFirstObjectByType<PlayerMovement>();
        Camera gameplayCamera = player != null ? player.playerCamera : null;
        if (gameplayCamera == null)
        {
            gameplayCamera = Camera.main;
        }
        if (gameplayCamera == null)
        {
            return;
        }

        Color fallbackSky = Color.Lerp(
            new Color(0.38f, 0.66f, 0.94f),
            new Color(0.008f, 0.018f, 0.065f),
            night);
        gameplayCamera.backgroundColor = fallbackSky;
        if (customGradientSkybox)
        {
            // The custom shader is a Resources asset, so it is included in
            // player builds and is safe to use as the actual camera sky.
            gameplayCamera.clearFlags = CameraClearFlags.Skybox;
        }
        else
        {
            // Keep the old solid-color fallback for platforms where only the
            // built-in procedural shader is available; this avoids returning
            // to the black URP sky regression.
            gameplayCamera.clearFlags = CameraClearFlags.SolidColor;
        }
        if (proceduralSkyboxMaterial != null &&
            RenderSettings.skybox != proceduralSkyboxMaterial)
        {
            RenderSettings.skybox = proceduralSkyboxMaterial;
        }
    }

    private void EnsureSkyEffects()
    {
        if (customGradientSkybox)
        {
            // The gradient sky shader owns both clouds and stars. Leaving the
            // old billboard particle field active would reintroduce square
            // sprites on top of the radial procedural sky details.
            return;
        }
        if (nightStars != null && dayClouds != null)
        {
            return;
        }

        starParticleTexture = CreateSoftParticleTexture(
            "Gym white star texture", 16, 3.6f);
        cloudParticleTexture = CreateSoftParticleTexture(
            "Gym daytime cloud texture", 32, 1.25f);
        starParticleMaterial = CreateSkyParticleMaterial(
            "Gym realistic white stars", starParticleTexture);
        cloudParticleMaterial = CreateSkyParticleMaterial(
            "Gym realistic daytime clouds", cloudParticleTexture);

        nightStars = CreateSkyParticleSystem(
            "Night sky stars", starParticleMaterial, 180);
        dayClouds = CreateSkyParticleSystem(
            "Day sky clouds", cloudParticleMaterial, 36);
        nightStars.transform.SetParent(transform, true);
        dayClouds.transform.SetParent(transform, true);

        BuildStarParticles();
        BuildCloudParticles();
        Debug.Log(
            $"GYMCHAOS_SKY_EFFECTS_READY stars={starParticles.Length} " +
            $"cloudPuffs={cloudParticles.Length} mode=procedural-soft-particles",
            this);
    }

    private void BuildStarParticles()
    {
        const int starCount = 180;
        starParticles = new ParticleSystem.Particle[starCount];
        starBaseColors = new Color[starCount];
        System.Random random = new System.Random(170823);
        float skyDepth = roomDepth * 0.5f + 8.5f;
        float skySpan = Mathf.Max(12f, roomWidth * 1.22f);

        for (int i = 0; i < starCount; i++)
        {
            float x = Mathf.Lerp(-skySpan * 0.5f, skySpan * 0.5f,
                (float)random.NextDouble());
            float y = Mathf.Lerp(
                skyWindowBottom + 0.18f,
                Mathf.Max(skyWindowTop + 1.6f, 9.5f),
                (float)random.NextDouble());
            float z = skyDepth + Mathf.Lerp(-1.4f, 4.8f,
                (float)random.NextDouble());
            float alpha = Mathf.Lerp(0.58f, 1f, (float)random.NextDouble());
            Color color = new Color(1f, 1f, 1f, alpha);

            ParticleSystem.Particle particle = new ParticleSystem.Particle
            {
                position = new Vector3(x, y, z),
                startSize = Mathf.Lerp(0.018f, 0.052f, (float)random.NextDouble()),
                startLifetime = 100000f,
                remainingLifetime = 100000f,
                startColor = color
            };
            starParticles[i] = particle;
            starBaseColors[i] = color;
        }

        nightStars.SetParticles(starParticles, starParticles.Length);
        nightStars.Play(true);
        nightStars.Pause(true);
    }

    private void BuildCloudParticles()
    {
        const int clusterCount = 7;
        const int puffsPerCluster = 5;
        int cloudCount = clusterCount * puffsPerCluster;
        cloudParticles = new ParticleSystem.Particle[cloudCount];
        cloudBaseColors = new Color[cloudCount];
        cloudBasePositions = new Vector3[cloudCount];
        System.Random random = new System.Random(230823);
        // Put the cloud layer farther beyond the window plane as well as
        // above it. With the old shallow depth, a high cloud projected above
        // the window header and was hidden by the upper wall/ceiling.
        float skyDepth = roomDepth * 0.5f + 22f;
        float skySpan = Mathf.Max(12f, roomWidth * 1.18f);
        // Keep the cloud layer high in the outdoor view, just below the sun
        // rather than near the window sill/floor horizon. The lower bound is
        // well above the window header and still scales if the opening changes
        // later; the extra vertical spread gives the sky real depth without
        // putting any puff at character height.
        float cloudMinY = Mathf.Max(skyWindowTop + 8.5f, 16f);
        float cloudMaxY = Mathf.Max(cloudMinY + 6f, skyWindowTop + 15f);

        for (int cluster = 0; cluster < clusterCount; cluster++)
        {
            float clusterX = Mathf.Lerp(-skySpan * 0.52f, skySpan * 0.52f,
                clusterCount == 1 ? 0.5f : cluster / (float)(clusterCount - 1));
            float clusterY = Mathf.Lerp(
                cloudMinY,
                cloudMaxY,
                (float)random.NextDouble());
            float clusterZ = skyDepth + Mathf.Lerp(-1.2f, 3.6f,
                (float)random.NextDouble());

            for (int puff = 0; puff < puffsPerCluster; puff++)
            {
                int index = cluster * puffsPerCluster + puff;
                Vector3 position = new Vector3(
                    clusterX + Mathf.Lerp(-1.35f, 1.35f, (float)random.NextDouble()),
                    clusterY + Mathf.Lerp(-0.36f, 0.36f, (float)random.NextDouble()),
                    clusterZ + Mathf.Lerp(-0.55f, 0.55f, (float)random.NextDouble()));
                float alpha = Mathf.Lerp(0.16f, 0.34f, (float)random.NextDouble());
                Color color = new Color(
                    Mathf.Lerp(0.88f, 1f, (float)random.NextDouble()),
                    Mathf.Lerp(0.91f, 1f, (float)random.NextDouble()),
                    1f,
                    alpha);

                ParticleSystem.Particle particle = new ParticleSystem.Particle
                {
                    position = position,
                    startSize = Mathf.Lerp(1.05f, 2.35f, (float)random.NextDouble()),
                    startLifetime = 100000f,
                    remainingLifetime = 100000f,
                    startColor = color
                };
                cloudParticles[index] = particle;
                cloudBasePositions[index] = position;
                cloudBaseColors[index] = color;
            }
        }

        dayClouds.SetParticles(cloudParticles, cloudParticles.Length);
        dayClouds.Play(true);
        dayClouds.Pause(true);
    }

    private void UpdateSkyEffects(float daylight, float night)
    {
        if (nightStars == null || dayClouds == null)
        {
            return;
        }

        // Particle coordinates are authored around the room centre, while
        // the time-of-day component is parented to the runtime root.
        nightStars.transform.position = roomCenter;
        dayClouds.transform.position = roomCenter;

        float starVisibility = Mathf.SmoothStep(
            0f, 1f, Mathf.InverseLerp(0.88f, 0.998f, night));
        float cloudVisibility = Mathf.SmoothStep(
            0f, 1f, Mathf.InverseLerp(0.18f, 0.62f, daylight));

        UpdateSkyParticleVisibility(
            nightStars, starParticles, starBaseColors, starVisibility);
        UpdateSkyParticleVisibility(
            dayClouds, cloudParticles, cloudBaseColors, cloudVisibility);

        if (cloudVisibility > 0.001f)
        {
            cloudDrift = Mathf.Repeat(
                cloudDrift + Time.deltaTime * 0.11f,
                Mathf.Max(12f, roomWidth * 1.18f));
            float cloudSpan = Mathf.Max(12f, roomWidth * 1.18f);
            for (int i = 0; i < cloudParticles.Length; i++)
            {
                Vector3 position = cloudBasePositions[i];
                position.x = Mathf.Repeat(
                    position.x + cloudDrift + cloudSpan * 0.5f,
                    cloudSpan) - cloudSpan * 0.5f;
                cloudParticles[i].position = position;
            }
            dayClouds.SetParticles(cloudParticles, cloudParticles.Length);
        }
    }

    private static void UpdateSkyParticleVisibility(
        ParticleSystem system,
        ParticleSystem.Particle[] particles,
        Color[] baseColors,
        float visibility)
    {
        bool visible = visibility > 0.001f;
        if (system.gameObject.activeSelf != visible)
        {
            system.gameObject.SetActive(visible);
        }
        if (!visible)
        {
            return;
        }

        for (int i = 0; i < particles.Length; i++)
        {
            Color color = baseColors[i];
            color.a *= visibility;
            particles[i].startColor = color;
        }
        system.SetParticles(particles, particles.Length);
    }

    private static ParticleSystem CreateSkyParticleSystem(
        string objectName, Material material, int maxParticles)
    {
        GameObject effectObject = new GameObject(objectName);
        ParticleSystem system = effectObject.AddComponent<ParticleSystem>();
        ParticleSystem.MainModule main = system.main;
        main.playOnAwake = false;
        main.loop = true;
        main.maxParticles = maxParticles;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.startLifetime = 100000f;
        main.startSpeed = 0f;
        main.startSize = 1f;
        main.startColor = Color.white;

        ParticleSystem.EmissionModule emission = system.emission;
        emission.enabled = false;
        ParticleSystem.ShapeModule shape = system.shape;
        shape.enabled = false;

        ParticleSystemRenderer renderer = effectObject.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.alignment = ParticleSystemRenderSpace.View;
        renderer.sortMode = ParticleSystemSortMode.Distance;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.material = material;
        return system;
    }

    private static Material CreateSkyParticleMaterial(string materialName, Texture2D texture)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (shader == null)
        {
            shader = Shader.Find("Particles/Standard Unlit");
        }
        if (shader == null)
        {
            shader = Shader.Find("Sprites/Default");
        }
        if (shader == null)
        {
            return null;
        }

        Material material = new Material(shader) { name = materialName };
        material.color = Color.white;
        material.mainTexture = texture;
        if (material.HasProperty("_BaseMap"))
        {
            material.SetTexture("_BaseMap", texture);
        }
        if (material.HasProperty("_MainTex"))
        {
            material.SetTexture("_MainTex", texture);
        }
        if (material.HasProperty("_BaseColor"))
        {
            material.SetColor("_BaseColor", Color.white);
        }
        if (material.HasProperty("_Color"))
        {
            material.SetColor("_Color", Color.white);
        }
        return material;
    }

    private static Texture2D CreateSoftParticleTexture(
        string textureName, int size, float falloff)
    {
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            name = textureName,
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };
        Color[] pixels = new Color[size * size];
        float centre = (size - 1) * 0.5f;
        float radius = Mathf.Max(1f, size * 0.5f);
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float distance = Vector2.Distance(
                    new Vector2(x, y), new Vector2(centre, centre)) / radius;
                float alpha = Mathf.Pow(Mathf.Clamp01(1f - distance), falloff);
                pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
            }
        }
        texture.SetPixels(pixels);
        texture.Apply(false, true);
        return texture;
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
