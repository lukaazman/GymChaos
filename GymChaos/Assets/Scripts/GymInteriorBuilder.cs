using UnityEngine;
using UnityEngine.Rendering;

public static class GymInteriorBuilder
{
    private const string RootName = "Gym Interior (Runtime)";

    public static void Build(PlayerMovement player)
    {
        if (GameObject.Find(RootName) != null)
        {
            return;
        }

        Bounds equipmentBounds = FindEquipmentBounds(player);
        float floorY = player != null ? player.transform.position.y - 1.05f : equipmentBounds.min.y;
        float width = Mathf.Clamp(equipmentBounds.size.x + 14f, 36f, 72f);
        float depth = Mathf.Clamp(equipmentBounds.size.z + 14f, 34f, 72f);
        float height = 8.5f;
        Vector3 center = new Vector3(equipmentBounds.center.x, floorY, equipmentBounds.center.z);

        GameObject root = new GameObject(RootName);
        Material floor = CreateMaterial("Gym rubber floor", new Color(0.055f, 0.065f, 0.075f), 0.05f, 0.34f);
        Material wall = CreateMaterial("Warm concrete walls", new Color(0.36f, 0.39f, 0.41f), 0f, 0.25f);
        Material ceiling = CreateMaterial("Ceiling", new Color(0.12f, 0.14f, 0.16f), 0.05f, 0.24f);
        Material accent = CreateMaterial("Gym accent", new Color(0.78f, 0.13f, 0.07f), 0.05f, 0.45f);
        Material trim = CreateMaterial("Dark trim", new Color(0.025f, 0.03f, 0.035f), 0.4f, 0.68f);
        Material mirror = CreateMaterial("Mirror", new Color(0.62f, 0.7f, 0.74f), 1f, 1f);
        Material lightMaterial = CreateMaterial("Ceiling light", new Color(0.92f, 0.96f, 1f), 0f, 0.9f);
        SetEmission(lightMaterial, new Color(3.8f, 4.2f, 4.8f));

        CreateBox("Rubber Floor", root.transform, center + Vector3.down * 0.12f, new Vector3(width, 0.24f, depth), floor, true);
        CreateBox("Ceiling", root.transform, center + Vector3.up * height, new Vector3(width, 0.24f, depth), ceiling, true);
        CreateBox("North Wall", root.transform, center + new Vector3(0f, height * 0.5f, depth * 0.5f), new Vector3(width, height, 0.32f), wall, true);
        CreateBox("South Wall", root.transform, center + new Vector3(0f, height * 0.5f, -depth * 0.5f), new Vector3(width, height, 0.32f), wall, true);
        CreateBox("East Wall", root.transform, center + new Vector3(width * 0.5f, height * 0.5f, 0f), new Vector3(0.32f, height, depth), wall, true);
        CreateBox("West Wall", root.transform, center + new Vector3(-width * 0.5f, height * 0.5f, 0f), new Vector3(0.32f, height, depth), wall, true);

        CreateWallBand(root.transform, center, width, depth, 1.15f, 0.24f, accent);
        CreateWallBand(root.transform, center, width, depth, 0.18f, 0.18f, trim);
        CreateMirrors(root.transform, center, width, depth, mirror, trim);
        CreateCeilingGrid(root.transform, center, width, depth, height, trim);
        CreateLighting(root.transform, center, width, depth, height, lightMaterial);
        CreateRoomDetails(root.transform, center, width, depth, accent, trim, wall);
        ConfigureAmbientLighting(center, width, depth, height);
    }

    private static Bounds FindEquipmentBounds(PlayerMovement player)
    {
        Renderer[] renderers = Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None);
        bool hasBounds = false;
        Bounds bounds = new Bounds(player != null ? player.transform.position : Vector3.zero, Vector3.one * 10f);

        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null || renderer is ParticleSystemRenderer || renderer.GetComponentInParent<PlayerMovement>() != null)
            {
                continue;
            }

            string lower = renderer.name.ToLowerInvariant();
            if (lower.Contains("plane") || lower.Contains("flooring") || lower.Contains("global volume"))
            {
                continue;
            }

            if (!hasBounds)
            {
                bounds = renderer.bounds;
                hasBounds = true;
            }
            else
            {
                bounds.Encapsulate(renderer.bounds);
            }
        }

        return bounds;
    }

    private static void CreateWallBand(Transform parent, Vector3 center, float width, float depth, float y, float thickness, Material material)
    {
        CreateBox("North wall stripe", parent, center + new Vector3(0f, y, depth * 0.5f - 0.19f), new Vector3(width - 0.5f, thickness, 0.05f), material, false);
        CreateBox("South wall stripe", parent, center + new Vector3(0f, y, -depth * 0.5f + 0.19f), new Vector3(width - 0.5f, thickness, 0.05f), material, false);
        CreateBox("East wall stripe", parent, center + new Vector3(width * 0.5f - 0.19f, y, 0f), new Vector3(0.05f, thickness, depth - 0.5f), material, false);
        CreateBox("West wall stripe", parent, center + new Vector3(-width * 0.5f + 0.19f, y, 0f), new Vector3(0.05f, thickness, depth - 0.5f), material, false);
    }

    private static void CreateMirrors(Transform parent, Vector3 center, float width, float depth, Material mirror, Material frame)
    {
        float panelWidth = Mathf.Min(4.5f, (width - 5f) / 6f);
        for (int i = -2; i <= 2; i++)
        {
            Vector3 panelCenter = center + new Vector3(i * (panelWidth + 0.18f), 3.05f, depth * 0.5f - 0.2f);
            CreateBox("Mirror panel", parent, panelCenter, new Vector3(panelWidth, 4.6f, 0.055f), mirror, false);
            CreateBox("Mirror top frame", parent, panelCenter + Vector3.up * 2.36f, new Vector3(panelWidth + 0.12f, 0.09f, 0.09f), frame, false);
            CreateBox("Mirror bottom frame", parent, panelCenter - Vector3.up * 2.36f, new Vector3(panelWidth + 0.12f, 0.09f, 0.09f), frame, false);
        }

        ReflectionProbe probe = new GameObject("Gym Reflection Probe").AddComponent<ReflectionProbe>();
        probe.transform.SetParent(parent, false);
        probe.transform.position = center + Vector3.up * 3.4f;
        probe.mode = ReflectionProbeMode.Realtime;
        probe.refreshMode = ReflectionProbeRefreshMode.OnAwake;
        probe.timeSlicingMode = ReflectionProbeTimeSlicingMode.AllFacesAtOnce;
        probe.boxProjection = true;
        probe.size = new Vector3(width - 1f, 7f, depth - 1f);
        probe.intensity = 1.15f;
        probe.resolution = 128;
    }

    private static void CreateCeilingGrid(Transform parent, Vector3 center, float width, float depth, float height, Material material)
    {
        for (float x = -width * 0.5f + 4f; x < width * 0.5f; x += 6f)
        {
            CreateBox("Ceiling beam", parent, center + new Vector3(x, height - 0.28f, 0f), new Vector3(0.16f, 0.24f, depth - 0.5f), material, false);
        }

        for (float z = -depth * 0.5f + 4f; z < depth * 0.5f; z += 6f)
        {
            CreateBox("Ceiling beam", parent, center + new Vector3(0f, height - 0.3f, z), new Vector3(width - 0.5f, 0.2f, 0.16f), material, false);
        }
    }

    private static void CreateLighting(Transform parent, Vector3 center, float width, float depth, float height, Material lightMaterial)
    {
        Light[] existingLights = Object.FindObjectsByType<Light>(FindObjectsSortMode.None);
        for (int i = 0; i < existingLights.Length; i++)
        {
            if (existingLights[i].type == LightType.Directional)
            {
                existingLights[i].intensity = 0.18f;
                existingLights[i].color = new Color(0.72f, 0.8f, 0.9f);
            }
        }

        int xCount = Mathf.Max(3, Mathf.RoundToInt(width / 10f));
        int zCount = Mathf.Max(3, Mathf.RoundToInt(depth / 10f));
        for (int x = 0; x < xCount; x++)
        {
            for (int z = 0; z < zCount; z++)
            {
                float px = Mathf.Lerp(-width * 0.42f, width * 0.42f, xCount == 1 ? 0.5f : (float)x / (xCount - 1));
                float pz = Mathf.Lerp(-depth * 0.4f, depth * 0.4f, zCount == 1 ? 0.5f : (float)z / (zCount - 1));
                Vector3 position = center + new Vector3(px, height - 0.38f, pz);
                CreateBox("LED panel", parent, position, new Vector3(2.8f, 0.08f, 0.65f), lightMaterial, false);

                GameObject lightObject = new GameObject("LED area light");
                lightObject.transform.SetParent(parent, false);
                lightObject.transform.position = position - Vector3.up * 0.08f;
                lightObject.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
                Light light = lightObject.AddComponent<Light>();
                light.type = LightType.Spot;
                light.color = new Color(0.86f, 0.92f, 1f);
                light.intensity = 900f;
                light.range = 13f;
                light.spotAngle = 105f;
                light.innerSpotAngle = 70f;
                light.shadows = LightShadows.Soft;
                light.shadowStrength = 0.55f;
            }
        }
    }

    private static void CreateRoomDetails(Transform parent, Vector3 center, float width, float depth, Material accent, Material trim, Material wall)
    {
        Vector3 deskPosition = center + new Vector3(-width * 0.33f, 0.65f, -depth * 0.5f + 2.5f);
        CreateBox("Reception desk", parent, deskPosition, new Vector3(5.2f, 1.3f, 1.1f), trim, true);
        CreateBox("Reception accent", parent, deskPosition + new Vector3(0f, 0.05f, -0.57f), new Vector3(3.4f, 0.56f, 0.08f), accent, false);

        for (int i = -2; i <= 2; i++)
        {
            Vector3 lockerPosition = center + new Vector3(width * 0.5f - 0.46f, 1.25f, i * 1.65f);
            CreateBox("Locker", parent, lockerPosition, new Vector3(0.72f, 2.5f, 1.45f), i % 2 == 0 ? trim : wall, true);
            CreateBox("Locker handle", parent, lockerPosition + new Vector3(-0.39f, 0f, -0.42f), new Vector3(0.05f, 0.25f, 0.05f), accent, false);
        }

        for (int i = -2; i <= 2; i++)
        {
            Vector3 posterPosition = center + new Vector3(i * 4.2f, 4.7f, -depth * 0.5f + 0.19f);
            Material poster = CreateMaterial("Poster", Color.HSVToRGB(Mathf.Repeat(0.02f + i * 0.11f, 1f), 0.7f, 0.72f), 0f, 0.35f);
            CreateBox("Training poster", parent, posterPosition, new Vector3(2.6f, 2.2f, 0.05f), poster, false);
        }
    }

    private static void ConfigureAmbientLighting(Vector3 center, float width, float depth, float height)
    {
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.2f, 0.24f, 0.3f);
        RenderSettings.ambientEquatorColor = new Color(0.12f, 0.14f, 0.17f);
        RenderSettings.ambientGroundColor = new Color(0.045f, 0.05f, 0.06f);
        RenderSettings.ambientIntensity = 1.05f;
        RenderSettings.fog = true;
        RenderSettings.fogColor = new Color(0.075f, 0.085f, 0.1f);
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogStartDistance = Mathf.Min(width, depth) * 0.55f;
        RenderSettings.fogEndDistance = Mathf.Max(width, depth) * 1.15f;
    }

    private static Material CreateMaterial(string name, Color color, float metallic, float smoothness)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
        {
            shader = Shader.Find("Standard");
        }

        Material material = new Material(shader);
        material.name = name;
        material.color = color;
        material.SetFloat("_Metallic", metallic);
        material.SetFloat("_Smoothness", smoothness);
        return material;
    }

    private static void SetEmission(Material material, Color emission)
    {
        material.EnableKeyword("_EMISSION");
        material.SetColor("_EmissionColor", emission);
    }

    private static GameObject CreateBox(string name, Transform parent, Vector3 position, Vector3 scale, Material material, bool keepCollider)
    {
        GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
        box.name = name;
        box.transform.SetParent(parent, true);
        box.transform.position = position;
        box.transform.localScale = scale;
        box.GetComponent<Renderer>().sharedMaterial = material;
        if (!keepCollider)
        {
            Object.Destroy(box.GetComponent<Collider>());
        }

        return box;
    }
}
