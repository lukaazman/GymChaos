using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// The lollipop spell light: an additive shell over the candy, a soft halo,
/// a warm point light and a sparkle stream around the candy. Everything is
/// driven by one 0..1 envelope so the glow fades in and out with the
/// magic_sign animation instead of switching on.
/// </summary>
public sealed class GymFixerMagicEffect : MonoBehaviour
{
    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static readonly int IntensityId = Shader.PropertyToID("_Intensity");
    private static readonly int ModeId = Shader.PropertyToID("_Mode");
    private static Material sparkleMaterial;

    private Renderer shell;
    private Material shellMaterial;
    private Transform anchor;
    private Vector3 anchorLocalCenter;
    private float candyRadius;
    private Transform halo;
    private Material haloMaterial;
    private Light glowLight;
    private ParticleSystem sparkles;
    private float envelope;

    public float Envelope => envelope;
    public float PeakEnvelope { get; private set; }
    public Vector3 CandyCenter => anchor != null
        ? anchor.TransformPoint(anchorLocalCenter)
        : transform.position;
    public float CandyRadius => candyRadius;

    public static Shader GlowShader
    {
        get
        {
            Shader shader = Resources.Load<Shader>("GymFixerGlow");
            return shader != null ? shader : Shader.Find("GymChaos/GymFixerGlow");
        }
    }

    public void Configure(SkinnedMeshRenderer glowShell, Transform handBone, Bounds candyBounds)
    {
        shell = glowShell;
        anchor = handBone;
        Shader shader = GlowShader;
        if (shell != null && shader != null)
        {
            shellMaterial = new Material(shader) { name = "Jolly Lollipop Glow" };
            shellMaterial.SetColor(ColorId, new Color(1f, 0.78f, 0.42f));
            shellMaterial.SetFloat(ModeId, 0f);
            shellMaterial.SetFloat(IntensityId, 0f);
            shell.sharedMaterial = shellMaterial;
            shell.shadowCastingMode = ShadowCastingMode.Off;
            shell.receiveShadows = false;
            shell.enabled = false;

        }
        // The shell is rigid on the hand bone, so the baked candy centre in
        // hand space stays the candy centre for every frame.
        anchorLocalCenter = anchor != null
            ? anchor.InverseTransformPoint(candyBounds.center)
            : Vector3.zero;
        candyRadius = Mathf.Max(0.03f, Mathf.Max(
            candyBounds.extents.x, Mathf.Max(candyBounds.extents.y, candyBounds.extents.z)) * 0.9f);

        GameObject haloObject = GameObject.CreatePrimitive(PrimitiveType.Quad);
        haloObject.name = "Jolly Lollipop Halo";
        Destroy(haloObject.GetComponent<Collider>());
        halo = haloObject.transform;
        halo.SetParent(transform, false);
        haloMaterial = new Material(shader) { name = "Jolly Lollipop Halo" };
        haloMaterial.SetColor(ColorId, new Color(1f, 0.74f, 0.5f));
        haloMaterial.SetFloat(ModeId, 1f);
        haloMaterial.SetFloat(IntensityId, 0f);
        Renderer haloRenderer = haloObject.GetComponent<Renderer>();
        haloRenderer.sharedMaterial = haloMaterial;
        haloRenderer.shadowCastingMode = ShadowCastingMode.Off;
        haloRenderer.receiveShadows = false;
        haloObject.SetActive(false);

        GameObject lightObject = new GameObject("Jolly Lollipop Light");
        lightObject.transform.SetParent(transform, false);
        glowLight = lightObject.AddComponent<Light>();
        glowLight.type = LightType.Point;
        glowLight.color = new Color(1f, 0.8f, 0.55f);
        glowLight.range = 2.4f;
        glowLight.intensity = 0f;
        glowLight.shadows = LightShadows.None;
        glowLight.enabled = false;

        sparkles = CreateSparkles("Jolly Lollipop Sparkles", transform, 220);
        ParticleSystem.ShapeModule shape = sparkles.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = candyRadius * 1.5f;
        ParticleSystem.EmissionModule emission = sparkles.emission;
        emission.rateOverTime = 0f;
        sparkles.Play();
        SetEnvelope(0f);
    }

    public void ResetPeak()
    {
        PeakEnvelope = 0f;
    }

    public void SetEnvelope(float value)
    {
        envelope = Mathf.Clamp01(value);
        PeakEnvelope = Mathf.Max(PeakEnvelope, envelope);
        bool visible = envelope > 0.002f;
        // Ease the visual curve so the first and last frames are truly dark.
        float eased = envelope * envelope * (3f - 2f * envelope);
        if (shell != null)
        {
            shell.enabled = visible;
            shellMaterial.SetFloat(IntensityId, eased * 2.4f);
        }
        if (halo != null)
        {
            halo.gameObject.SetActive(visible);
            haloMaterial.SetFloat(IntensityId, eased * 1.1f);
        }
        if (glowLight != null)
        {
            glowLight.enabled = visible;
            glowLight.intensity = eased * 1.3f;
        }
        if (sparkles != null)
        {
            ParticleSystem.EmissionModule emission = sparkles.emission;
            emission.rateOverTime = eased * 90f;
        }
    }

    private void LateUpdate()
    {
        Vector3 center = CandyCenter;
        if (sparkles != null)
        {
            sparkles.transform.position = center;
        }
        if (glowLight != null)
        {
            glowLight.transform.position = center;
        }
        if (halo != null && halo.gameObject.activeSelf)
        {
            Camera camera = Camera.main;
            halo.position = center;
            if (camera != null)
            {
                halo.rotation = Quaternion.LookRotation(
                    halo.position - camera.transform.position, camera.transform.up);
            }
            float pulse = 1f + 0.06f * Mathf.Sin(Time.time * 7f);
            halo.localScale = Vector3.one * candyRadius * 6f * pulse;
        }
    }

    /// <summary>A one-shot sparkle burst, used where something is repaired.</summary>
    public static void Burst(Vector3 position, float radius, int count)
    {
        GameObject root = new GameObject("Jolly Dog Repair Sparkles");
        root.transform.position = position;
        ParticleSystem burst = CreateSparkles(root.name, root.transform, count + 8);
        burst.transform.localPosition = Vector3.zero;
        ParticleSystem.MainModule main = burst.main;
        main.loop = false;
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.6f, 2.2f);
        ParticleSystem.ShapeModule shape = burst.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = Mathf.Max(0.1f, radius);
        burst.Emit(count);
        Destroy(root, 2.5f);
    }

    private static ParticleSystem CreateSparkles(string name, Transform parent, int maxParticles)
    {
        GameObject sparkleObject = new GameObject(name);
        sparkleObject.transform.SetParent(parent, false);
        ParticleSystem particles = sparkleObject.AddComponent<ParticleSystem>();
        particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        ParticleSystem.MainModule main = particles.main;
        main.loop = true;
        main.duration = 1f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.45f, 1.1f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.15f, 0.6f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.018f, 0.05f);
        main.startColor = new ParticleSystem.MinMaxGradient(
            new Color(1f, 0.88f, 0.55f), new Color(0.75f, 0.9f, 1f));
        main.gravityModifier = -0.04f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = maxParticles;
        main.playOnAwake = false;

        ParticleSystem.ColorOverLifetimeModule color = particles.colorOverLifetime;
        color.enabled = true;
        Gradient fade = new Gradient();
        fade.SetKeys(
            new[]
            {
                new GradientColorKey(Color.white, 0f),
                new GradientColorKey(new Color(1f, 0.7f, 0.9f), 0.55f),
                new GradientColorKey(new Color(0.6f, 0.85f, 1f), 1f)
            },
            new[]
            {
                new GradientAlphaKey(0f, 0f),
                new GradientAlphaKey(1f, 0.15f),
                new GradientAlphaKey(0f, 1f)
            });
        color.color = new ParticleSystem.MinMaxGradient(fade);
        ParticleSystem.SizeOverLifetimeModule size = particles.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 1f, 1f, 0.2f));
        ParticleSystem.NoiseModule noise = particles.noise;
        noise.enabled = true;
        noise.strength = 0.35f;
        noise.frequency = 1.6f;

        ParticleSystemRenderer renderer = particles.GetComponent<ParticleSystemRenderer>();
        if (sparkleMaterial == null)
        {
            Shader shader = GlowShader;
            if (shader != null)
            {
                sparkleMaterial = new Material(shader)
                {
                    name = "Jolly Sparkle",
                    hideFlags = HideFlags.DontSave
                };
                sparkleMaterial.SetColor(ColorId, Color.white);
                sparkleMaterial.SetFloat(IntensityId, 2.2f);
                sparkleMaterial.SetFloat(ModeId, 1f);
            }
        }
        renderer.sharedMaterial = sparkleMaterial;
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        return particles;
    }

    private void OnDestroy()
    {
        if (shellMaterial != null) Destroy(shellMaterial);
        if (haloMaterial != null) Destroy(haloMaterial);
    }
}
