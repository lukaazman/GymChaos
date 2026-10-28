using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Sound and visual feedback for one kart: engine pitch by speed, drift
/// squeal, drift sparks coloured by mini-turbo tier, skid marks, exhaust
/// flames for mini-turbo and boost, bump sparks, steering front wheels and
/// the horn. Everything is plain meshes and trails (no particle systems) so
/// batch verifier runs stay stable.
/// </summary>
[RequireComponent(typeof(GymRaceKart))]
public sealed class GymRaceKartEffects : MonoBehaviour
{
    private static readonly Color[] TierColors =
    {
        new Color(1f, 0.85f, 0.4f),
        new Color(0.25f, 0.6f, 1f),
        new Color(1f, 0.55f, 0.1f),
        new Color(0.85f, 0.3f, 1f)
    };

    public bool IsPlayer;
    public GymRaceChaseCamera Camera;

    private GymRaceKart kart;
    private GymVehicleWheelSpinner wheels;
    private AudioSource engine;
    private AudioSource squeal;
    private Transform[] sparks = new Transform[0];
    private Renderer[] sparkRenderers = new Renderer[0];
    private Transform[] flames = new Transform[0];
    private TrailRenderer[] skids = new TrailRenderer[0];
    private Material sparkMaterial;
    private Material flameMaterial;
    private Material skidMaterial;
    private Transform bumpFlash;
    private float bumpFlashTime;
    private float lastBumpSound;

    public void Setup(GymRaceKart target, GymVehicleWheelSpinner wheelSpinner, Bounds localBounds)
    {
        kart = target;
        wheels = wheelSpinner;
        CreateMaterials();
        CreateAudio();
        Transform visual = kart.Visual;
        float halfWidth = Mathf.Max(0.6f, localBounds.extents.x * 0.82f);
        float rear = localBounds.center.z - localBounds.extents.z * 0.72f;
        float back = localBounds.center.z - localBounds.extents.z;

        // Rear-wheel sparks and skid trails.
        sparks = new Transform[SparksPerWheel * 2];
        sparkRenderers = new Renderer[SparksPerWheel * 2];
        skids = new TrailRenderer[2];
        for (int i = 0; i < 2; i++)
        {
            float side = i == 0 ? -1f : 1f;
            Vector3 contact = new Vector3(side * halfWidth, 0.05f, rear);
            for (int k = 0; k < SparksPerWheel; k++)
            {
                int index = i * SparksPerWheel + k;
                sparks[index] = CreateQuad("Drift Spark", visual, contact + new Vector3(0f, 0.18f, -0.3f), sparkMaterial, 0.3f);
                sparkRenderers[index] = sparks[index].GetComponent<Renderer>();
            }
            if (!Application.isBatchMode)
            {
                GameObject skid = new GameObject("Skid Mark");
                skid.transform.SetParent(visual, false);
                skid.transform.localPosition = contact;
                TrailRenderer trail = skid.AddComponent<TrailRenderer>();
                trail.time = 2.5f;
                trail.minVertexDistance = 0.3f;
                trail.widthMultiplier = 0.32f;
                trail.alignment = LineAlignment.TransformZ;
                skid.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                trail.sharedMaterial = skidMaterial;
                trail.shadowCastingMode = ShadowCastingMode.Off;
                trail.emitting = false;
                skids[i] = trail;
            }
        }

        // Exhaust flames behind the car.
        flames = new Transform[2];
        for (int i = 0; i < 2; i++)
        {
            float side = i == 0 ? -0.35f : 0.35f;
            flames[i] = CreateFlame(visual, new Vector3(side * halfWidth, 0.45f, back - 0.05f));
        }
        bumpFlash = CreateQuad("Bump Flash", visual, Vector3.zero, sparkMaterial, 1.2f);
        bumpFlash.gameObject.SetActive(false);

        kart.Bumped += OnBumped;
        kart.MiniTurboStarted += tier => GymAudio.Play(GymSoundEffect.RaceMiniTurbo, transform.position, IsPlayer ? 0.9f : 0.5f);
        kart.BoostStarted += () =>
        {
            GymAudio.Play(GymSoundEffect.RaceBoost, transform.position, IsPlayer ? 1f : 0.6f);
            if (Camera != null && IsPlayer) Camera.Shake(0.35f);
        };
    }

    private const int SparksPerWheel = 3;

    /// <summary>Additive unlit (URP Unlit in transparent mode) so sparks and flames glow instead of reading as solid blocks.</summary>
    private static Material Additive(Shader shader, string name)
    {
        Material material = new Material(shader) { name = name };
        if (material.HasProperty("_Surface"))
        {
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", 2f);
            material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)BlendMode.One);
            material.SetFloat("_ZWrite", 0f);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.SetOverrideTag("RenderType", "Transparent");
            material.renderQueue = (int)RenderQueue.Transparent;
        }
        return material;
    }

    private void CreateMaterials()
    {
        Shader unlit = GymMaterialLibrary.UnlitShader;
        sparkMaterial = Additive(unlit, "Race Drift Spark");
        flameMaterial = Additive(unlit, "Race Exhaust Flame");
        skidMaterial = new Material(unlit) { name = "Race Skid", color = new Color(0.02f, 0.02f, 0.02f) };
        if (skidMaterial.HasProperty("_BaseColor"))
        {
            skidMaterial.SetColor("_BaseColor", skidMaterial.color);
        }
    }

    private void CreateAudio()
    {
        engine = gameObject.AddComponent<AudioSource>();
        engine.clip = GymAudio.GetLoadedClip(GymSoundEffect.VehicleDriving);
        engine.loop = true;
        engine.playOnAwake = false;
        engine.spatialBlend = IsPlayer ? 0.35f : 1f;
        engine.minDistance = 3f;
        engine.maxDistance = 60f;
        engine.dopplerLevel = IsPlayer ? 0f : 0.4f;
        engine.volume = IsPlayer ? 0.55f : 0.4f;
        if (engine.clip != null)
        {
            engine.time = Random.value * engine.clip.length * 0.9f;
            engine.Play();
        }

        squeal = gameObject.AddComponent<AudioSource>();
        squeal.clip = GymAudio.GetLoadedClip(GymSoundEffect.RaceDriftLoop);
        squeal.loop = true;
        squeal.playOnAwake = false;
        squeal.spatialBlend = IsPlayer ? 0.3f : 1f;
        squeal.minDistance = 3f;
        squeal.maxDistance = 45f;
        squeal.volume = 0f;
        if (squeal.clip != null)
        {
            squeal.Play();
        }
    }

    private static Transform CreateQuad(string name, Transform parent, Vector3 localPosition, Material material, float size)
    {
        GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quad.name = name;
        Object.Destroy(quad.GetComponent<Collider>());
        quad.transform.SetParent(parent, false);
        quad.transform.localPosition = localPosition;
        quad.transform.localScale = Vector3.one * size;
        Renderer renderer = quad.GetComponent<Renderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.enabled = false;
        return quad.transform;
    }

    private Transform CreateFlame(Transform parent, Vector3 localPosition)
    {
        GameObject flame = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        flame.name = "Exhaust Flame";
        Object.Destroy(flame.GetComponent<Collider>());
        flame.transform.SetParent(parent, false);
        flame.transform.localPosition = localPosition;
        flame.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        Renderer renderer = flame.GetComponent<Renderer>();
        renderer.sharedMaterial = flameMaterial;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        flame.SetActive(false);
        return flame.transform;
    }

    private void OnBumped(Vector3 point, float strength, GymRaceKart other)
    {
        if (strength < 2.5f)
        {
            return;
        }
        if (Time.time - lastBumpSound > 0.25f)
        {
            lastBumpSound = Time.time;
            GymSoundEffect effect = other != null ? GymSoundEffect.RaceBump : GymSoundEffect.RaceWallHit;
            GymAudio.Play(effect, point, Mathf.Clamp01(strength / 14f) * (IsPlayer ? 1f : 0.7f));
        }
        if (bumpFlash != null)
        {
            bumpFlash.position = point + Vector3.up * 0.4f;
            bumpFlash.gameObject.SetActive(true);
            bumpFlash.GetComponent<Renderer>().enabled = true;
            bumpFlashTime = 0.12f;
        }
        if (IsPlayer && Camera != null)
        {
            Camera.Shake(Mathf.Clamp01(strength / 16f));
        }
    }

    private void Update()
    {
        if (kart == null)
        {
            return;
        }
        EnsureClip(engine, GymSoundEffect.VehicleDriving, true);
        EnsureClip(squeal, GymSoundEffect.RaceDriftLoop, false);
        float speed01 = Mathf.Clamp01(Mathf.Abs(kart.ForwardSpeed) / Mathf.Max(kart.KartStats.TopSpeed, 1f));
        if (engine != null)
        {
            engine.pitch = 0.75f + speed01 * 0.95f + (kart.IsBoosting ? 0.2f : 0f) + Mathf.Clamp01(kart.Input.Throttle) * 0.08f;
        }
        if (squeal != null)
        {
            float target = kart.IsDrifting && kart.Grounded ? (IsPlayer ? 0.45f : 0.3f) : 0f;
            squeal.volume = Mathf.MoveTowards(squeal.volume, target, Time.deltaTime * 3f);
        }
        if (wheels != null)
        {
            wheels.SetSteer(Mathf.Clamp(kart.Input.Steer, -1f, 1f) * 26f);
        }

        // Drift sparks: colour by charged tier, flicker each frame.
        bool sparking = kart.IsDrifting && kart.Grounded;
        Color sparkColor = TierColors[Mathf.Clamp(kart.DriftTier, 0, TierColors.Length - 1)];
        SetColor(sparkMaterial, sparkColor * (1f + Random.value * 0.3f));
        Transform view = UnityEngine.Camera.main != null ? UnityEngine.Camera.main.transform : null;
        for (int i = 0; i < sparks.Length; i++)
        {
            int wheel = i / SparksPerWheel;
            bool outside = (wheel == 0 ? -1 : 1) == -kart.DriftDirection;
            sparkRenderers[i].enabled = sparking && (outside || kart.DriftTier > 0);
            if (sparkRenderers[i].enabled)
            {
                // Thin streaks flung back at random angles read as sparks.
                float length = (0.25f + Random.value * 0.4f) * (1f + kart.DriftTier * 0.3f);
                sparks[i].localScale = new Vector3(0.045f + kart.DriftTier * 0.015f, length, 1f);
                Quaternion face = view != null
                    ? Quaternion.LookRotation(sparks[i].position - view.position)
                    : sparks[i].rotation;
                sparks[i].rotation = face * Quaternion.Euler(0f, 0f, Random.Range(-70f, 70f));
            }
        }
        for (int i = 0; i < skids.Length; i++)
        {
            if (skids[i] != null)
            {
                skids[i].emitting = sparking;
            }
        }

        // Exhaust flames: orange mini-turbo, blue-white boost.
        bool flaming = kart.IsBoosting || kart.MiniTurboTime > 0f;
        Color flameColor = kart.IsBoosting ? new Color(0.45f, 0.8f, 1f) : new Color(1f, 0.55f, 0.12f);
        SetColor(flameMaterial, flameColor * (0.9f + Random.value * 0.3f));
        for (int i = 0; i < flames.Length; i++)
        {
            flames[i].gameObject.SetActive(flaming);
            if (flaming)
            {
                float length = (kart.IsBoosting ? 1.6f : 1f) * (0.8f + Random.value * 0.4f);
                flames[i].localScale = new Vector3(0.15f, length * 0.32f, 0.15f);
            }
        }

        if (bumpFlashTime > 0f)
        {
            bumpFlashTime -= Time.deltaTime;
            if (bumpFlashTime <= 0f)
            {
                bumpFlash.gameObject.SetActive(false);
            }
        }

        if (kart.Input.HornPressed)
        {
            GymAudio.Play(GymSoundEffect.RaceHorn, transform.position, IsPlayer ? 0.9f : 0.6f);
        }
    }

    /// <summary>Clips stream in at startup; pick one up late if it was not ready at setup.</summary>
    private static void EnsureClip(AudioSource source, GymSoundEffect effect, bool randomStart)
    {
        if (source == null || source.clip != null)
        {
            return;
        }
        source.clip = GymAudio.GetLoadedClip(effect);
        if (source.clip == null)
        {
            return;
        }
        if (randomStart)
        {
            source.time = Random.value * source.clip.length * 0.9f;
        }
        source.Play();
    }

    private static void SetColor(Material material, Color color)
    {
        material.color = color;
        if (material.HasProperty("_BaseColor"))
        {
            material.SetColor("_BaseColor", color);
        }
    }

    private void OnDestroy()
    {
        if (kart != null)
        {
            kart.Bumped -= OnBumped;
        }
        if (sparkMaterial != null) Destroy(sparkMaterial);
        if (flameMaterial != null) Destroy(flameMaterial);
        if (skidMaterial != null) Destroy(skidMaterial);
    }
}
