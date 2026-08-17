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
        float width = Mathf.Clamp(equipmentBounds.size.x + 10f, 34f, 68f);
        float depth = Mathf.Clamp(equipmentBounds.size.z + 10f, 32f, 68f);
        float height = 8.5f;
        Vector3 center = new Vector3(equipmentBounds.center.x, floorY, equipmentBounds.center.z);

        GameObject root = new GameObject(RootName);
        Material floor = CreateMaterial("Dark navy gym rubber floor", new Color(0.012f, 0.026f, 0.082f), 0.05f, 0.34f);
        Material wall = CreateMaterial("Warm concrete walls", new Color(0.36f, 0.39f, 0.41f), 0f, 0.25f);
        Material ceiling = CreateMaterial("Ceiling", new Color(0.12f, 0.14f, 0.16f), 0.05f, 0.24f);
        Material accent = CreateMaterial("Gym accent", new Color(0.78f, 0.13f, 0.07f), 0.05f, 0.45f);
        Material trim = CreateMaterial("Dark trim", new Color(0.025f, 0.03f, 0.035f), 0.4f, 0.68f);
        Material mirror = CreateMaterial("Mirror", new Color(0.62f, 0.7f, 0.74f), 1f, 1f);
        Material glass = CreateTransparentMaterial(
            "Sunlit window glass", new Color(0.62f, 0.82f, 0.98f, 0.16f), 0.94f);
        Material lightMaterial = CreateMaterial("Ceiling light", new Color(0.92f, 0.96f, 1f), 0f, 0.9f);
        SetEmission(lightMaterial, new Color(3.8f, 4.2f, 4.8f));

        CreateBox("Rubber Floor", root.transform, center + Vector3.down * 0.12f, new Vector3(width, 0.24f, depth), floor, true);
        CreateBox("Ceiling", root.transform, center + Vector3.up * height, new Vector3(width, 0.24f, depth), ceiling, true);
        CreateBox("South Wall", root.transform, center + new Vector3(0f, height * 0.5f, -depth * 0.5f), new Vector3(width, height, 0.32f), wall, true);
        CreateBox("East Wall", root.transform, center + new Vector3(width * 0.5f, height * 0.5f, 0f), new Vector3(0.32f, height, depth), wall, true);
        CreateBox("West Wall", root.transform, center + new Vector3(-width * 0.5f, height * 0.5f, 0f), new Vector3(0.32f, height, depth), wall, true);

        CreateWindowWall(root.transform, center, width, depth, height, wall, trim, glass);
        CreateWallBand(root.transform, center, width, depth, 1.15f, 0.24f, accent);
        CreateWallBand(root.transform, center, width, depth, 0.18f, 0.18f, trim);
        CreateMirrors(root.transform, center, width, depth, mirror, trim, player != null ? player.playerCamera : null);
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

    private static void CreateWindowWall(
        Transform parent, Vector3 center, float width, float depth, float height,
        Material wall, Material frame, Material glass)
    {
        const float openingBottom = 2.05f;
        const float openingTop = 7.05f;
        float openingHeight = openingTop - openingBottom;
        float openingWidth = Mathf.Min(width - 6f, 28f);
        float sideWidth = Mathf.Max(1.5f, (width - openingWidth) * 0.5f);
        float wallZ = center.z + depth * 0.5f;

        CreateBox(
            "North Wall Lower",
            parent,
            new Vector3(center.x, center.y + openingBottom * 0.5f, wallZ),
            new Vector3(width, openingBottom, 0.32f),
            wall,
            true);
        CreateBox(
            "North Wall Upper",
            parent,
            new Vector3(center.x, center.y + openingTop + (height - openingTop) * 0.5f, wallZ),
            new Vector3(width, height - openingTop, 0.32f),
            wall,
            true);
        CreateBox(
            "North Wall West",
            parent,
            new Vector3(center.x - openingWidth * 0.5f - sideWidth * 0.5f,
                center.y + openingBottom + openingHeight * 0.5f, wallZ),
            new Vector3(sideWidth, openingHeight, 0.32f),
            wall,
            true);
        CreateBox(
            "North Wall East",
            parent,
            new Vector3(center.x + openingWidth * 0.5f + sideWidth * 0.5f,
                center.y + openingBottom + openingHeight * 0.5f, wallZ),
            new Vector3(sideWidth, openingHeight, 0.32f),
            wall,
            true);

        const int windowCount = 5;
        float mullionWidth = 0.13f;
        float panelWidth = (openingWidth - mullionWidth * (windowCount + 1)) / windowCount;
        float panelCenterY = center.y + openingBottom + openingHeight * 0.5f;
        for (int i = 0; i < windowCount; i++)
        {
            float panelX = center.x - openingWidth * 0.5f + mullionWidth + panelWidth * 0.5f +
                i * (panelWidth + mullionWidth);
            CreateBox(
                "Window glass",
                parent,
                new Vector3(panelX, panelCenterY, wallZ),
                new Vector3(panelWidth, openingHeight - 0.18f, 0.035f),
                glass,
                false);
        }

        CreateBox("Window sill", parent,
            new Vector3(center.x, center.y + openingBottom, wallZ - 0.03f),
            new Vector3(openingWidth + 0.3f, 0.15f, 0.42f), frame, true);
        CreateBox("Window header", parent,
            new Vector3(center.x, center.y + openingTop, wallZ - 0.03f),
            new Vector3(openingWidth + 0.3f, 0.15f, 0.42f), frame, false);
        for (int i = 0; i <= windowCount; i++)
        {
            float x = center.x - openingWidth * 0.5f + i * (panelWidth + mullionWidth);
            CreateBox("Window mullion", parent,
                new Vector3(x, panelCenterY, wallZ - 0.03f),
                new Vector3(mullionWidth, openingHeight, 0.38f), frame, false);
        }

        CreateExteriorView(parent, center, width, depth, openingBottom, openingTop);
    }

    private static void CreateExteriorView(
        Transform parent, Vector3 center, float width, float depth, float openingBottom, float openingTop)
    {
        float exteriorZ = center.z + depth * 0.5f + 8f;
        Material sky = CreateMaterial("Exterior blue sky", new Color(0.18f, 0.48f, 0.86f), 0f, 0.08f);
        SetEmission(sky, new Color(0.22f, 0.58f, 1.1f));
        Material ground = CreateMaterial("Exterior landscape", new Color(0.075f, 0.2f, 0.07f), 0f, 0.12f);
        Material sunMaterial = CreateMaterial("Visible sun", new Color(1f, 0.74f, 0.24f), 0f, 0.2f);
        SetEmission(sunMaterial, new Color(8f, 5.5f, 1.4f));

        CreateBox(
            "Exterior sky backdrop",
            parent,
            new Vector3(center.x, center.y + (openingBottom + openingTop) * 0.5f + 1.2f, exteriorZ),
            new Vector3(width * 1.35f, openingTop - openingBottom + 4f, 0.2f),
            sky,
            false);
        CreateBox(
            "Exterior green horizon",
            parent,
            new Vector3(center.x, center.y + openingBottom - 0.55f, center.z + depth * 0.5f + 4.5f),
            new Vector3(width * 1.2f, 1.1f, 8f),
            ground,
            false);

        GameObject sun = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        sun.name = "Exterior visible sun";
        sun.transform.SetParent(parent, true);
        sun.transform.position = new Vector3(
            center.x + Mathf.Min(width * 0.28f, 9f), center.y + openingTop - 0.65f, exteriorZ - 0.4f);
        sun.transform.localScale = Vector3.one * 1.35f;
        sun.GetComponent<Renderer>().sharedMaterial = sunMaterial;
        Object.Destroy(sun.GetComponent<Collider>());

        for (int i = -2; i <= 2; i++)
        {
            GameObject beamObject = new GameObject("Window sunlight");
            beamObject.transform.SetParent(parent, false);
            beamObject.transform.position = center + new Vector3(i * 4.2f, 6.5f, depth * 0.5f + 0.55f);
            Vector3 target = center + new Vector3(i * 3.2f - 2.2f, 0.2f, depth * 0.12f);
            beamObject.transform.rotation = Quaternion.LookRotation(target - beamObject.transform.position, Vector3.up);
            Light beam = beamObject.AddComponent<Light>();
            beam.type = LightType.Spot;
            beam.color = new Color(1f, 0.79f, 0.54f);
            beam.intensity = 1450f;
            beam.range = 18f;
            beam.spotAngle = 38f;
            beam.innerSpotAngle = 25f;
            // The directional sun supplies the window shadows. Extra realtime
            // spot shadows overflow URP's atlas and add avoidable Play Mode lag.
            beam.shadows = LightShadows.None;
        }
    }

    private static void CreateWallBand(Transform parent, Vector3 center, float width, float depth, float y, float thickness, Material material)
    {
        CreateBox("North wall stripe", parent, center + new Vector3(0f, y, depth * 0.5f - 0.19f), new Vector3(width - 0.5f, thickness, 0.05f), material, false);
        CreateBox("South wall stripe", parent, center + new Vector3(0f, y, -depth * 0.5f + 0.19f), new Vector3(width - 0.5f, thickness, 0.05f), material, false);
        CreateBox("East wall stripe", parent, center + new Vector3(width * 0.5f - 0.19f, y, 0f), new Vector3(0.05f, thickness, depth - 0.5f), material, false);
        CreateBox("West wall stripe", parent, center + new Vector3(-width * 0.5f + 0.19f, y, 0f), new Vector3(0.05f, thickness, depth - 0.5f), material, false);
    }

    private static void CreateMirrors(
        Transform parent, Vector3 center, float width, float depth,
        Material mirror, Material frame, Camera playerCamera)
    {
        // The short west wall is the camera-facing wall beside the Smith
        // machines. Keep the north windows and the south poster wall clear,
        // and use four panels rotated onto the wall instead of filling the long
        // wall with a row of mirrors. Leave a margin at both ends and account
        // for every inter-panel gap so the panels never overlap.
        const int mirrorCount = 4;
        float panelGap = 0.18f;
        float usableDepth = Mathf.Max(
            4f, depth - 4f - panelGap * (mirrorCount - 1));
        float panelWidth = Mathf.Min(5.1f, usableDepth / mirrorCount);
        Renderer[] mirrorRenderers = new Renderer[mirrorCount];
        for (int i = 0; i < mirrorCount; i++)
        {
            float zOffset = (i - (mirrorCount - 1) * 0.5f) * (panelWidth + panelGap);
            Vector3 panelCenter = center + new Vector3(-width * 0.5f + 0.2f, 3.05f, zOffset);
            GameObject panel = CreateBox("Mirror panel", parent, panelCenter, new Vector3(panelWidth, 4.6f, 0.055f), mirror, false);
            panel.transform.rotation = Quaternion.Euler(0f, 90f, 0f);
            mirrorRenderers[i] = panel.GetComponent<Renderer>();
            GameObject topFrame = CreateBox("Mirror top frame", parent, panelCenter + Vector3.up * 2.36f, new Vector3(panelWidth + 0.12f, 0.09f, 0.09f), frame, false);
            topFrame.transform.rotation = Quaternion.Euler(0f, 90f, 0f);
            GameObject bottomFrame = CreateBox("Mirror bottom frame", parent, panelCenter - Vector3.up * 2.36f, new Vector3(panelWidth + 0.12f, 0.09f, 0.09f), frame, false);
            bottomFrame.transform.rotation = Quaternion.Euler(0f, 90f, 0f);
        }

        Vector3 mirrorPlanePoint = center + new Vector3(-width * 0.5f + 0.23f, 3.05f, 0f);
        PlanarGymMirror.Create(parent, playerCamera, mirrorRenderers, mirrorPlanePoint, Vector3.right);
        Debug.Log(
            $"GYMCHAOS_MIRRORS_OK count={mirrorCount} wall=West normal=+X " +
            $"panelWidth={panelWidth:F2} panelGap={panelGap:F2}", parent);

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
        bool foundDirectional = false;
        for (int i = 0; i < existingLights.Length; i++)
        {
            if (existingLights[i].type == LightType.Directional)
            {
                foundDirectional = true;
                existingLights[i].name = "Warm exterior sun";
                existingLights[i].intensity = 1.15f;
                existingLights[i].color = new Color(1f, 0.79f, 0.6f);
                existingLights[i].transform.rotation = Quaternion.Euler(42f, -32f, 0f);
                existingLights[i].shadows = LightShadows.Soft;
                existingLights[i].shadowStrength = 0.78f;
            }
        }

        if (!foundDirectional)
        {
            GameObject sunObject = new GameObject("Warm exterior sun");
            sunObject.transform.SetParent(parent, false);
            sunObject.transform.rotation = Quaternion.Euler(42f, -32f, 0f);
            Light sun = sunObject.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1f, 0.79f, 0.6f);
            sun.intensity = 1.15f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.78f;
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
                light.intensity = 620f;
                light.range = 13f;
                light.spotAngle = 105f;
                light.innerSpotAngle = 70f;
                light.shadows = LightShadows.None;
            }
        }
    }

    private static void CreateRoomDetails(Transform parent, Vector3 center, float width, float depth, Material accent, Material trim, Material wall)
    {
        // Keep reception in the far north-east corner. The old south-west
        // placement put the desk directly in the player's starting sightline
        // and left the locker wall visually disconnected from reception.
        // Leave a generous aisle to the east wall so the desk, receptionist,
        // and roaming characters never spawn inside the wall or lockers.
        Vector3 deskPosition = center + new Vector3(width * 0.5f - 4.6f, 0.65f, depth * 0.5f - 1.8f);
        CreateBox("Reception desk", parent, deskPosition, new Vector3(5.2f, 1.3f, 1.1f), trim, true);
        CreateBox("Reception accent", parent, deskPosition + new Vector3(0f, 0.05f, -0.57f), new Vector3(3.4f, 0.56f, 0.08f), accent, false);

        for (int i = -2; i <= 2; i++)
        {
            Vector3 lockerPosition = center + new Vector3(width * 0.5f - 0.46f, 1.25f, i * 1.65f);
            CreateBox("Locker", parent, lockerPosition, new Vector3(0.72f, 2.5f, 1.45f), i % 2 == 0 ? trim : wall, true);
            CreateBox("Locker handle", parent, lockerPosition + new Vector3(-0.39f, 0f, -0.42f), new Vector3(0.05f, 0.25f, 0.05f), accent, false);
        }

        string[] posterResources =
        {
            "Environment/Posters/Tom_Platz_1995",
            "Environment/Posters/Lee_Priest_Pec_Fly",
            "Environment/Posters/Flex_Wheeler_2023",
            "Environment/Posters/Kevin_Levrone_2013",
            "Environment/Posters/Markus_Ruhl_2004",
            "Environment/Posters/Phil_Heath_2012"
        };
        float posterWidth = Mathf.Min(4.0f, (width - 5f) / posterResources.Length);
        float posterHeight = 4.9f;
        float posterGap = 0.38f;
        for (int i = 0; i < posterResources.Length; i++)
        {
            float x = (i - (posterResources.Length - 1) * 0.5f) * (posterWidth + posterGap);
            Vector3 posterPosition = center + new Vector3(x, 5.25f, -depth * 0.5f + 0.185f);
            Material poster = CreatePosterMaterial(posterResources[i], i);
            CreateBox("Golden era bodybuilder poster", parent, posterPosition, new Vector3(posterWidth, posterHeight, 0.055f), poster, false);
            CreateBox("Golden poster frame", parent, posterPosition + Vector3.forward * 0.035f,
                new Vector3(posterWidth + 0.18f, posterHeight + 0.18f, 0.035f), trim, false);
            CreateBox("Golden era bodybuilder poster", parent, posterPosition + Vector3.forward * 0.075f,
                new Vector3(posterWidth, posterHeight, 0.025f), poster, false);
        }
    }

    private static void ConfigureAmbientLighting(Vector3 center, float width, float depth, float height)
    {
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.32f, 0.4f, 0.54f);
        RenderSettings.ambientEquatorColor = new Color(0.18f, 0.2f, 0.25f);
        RenderSettings.ambientGroundColor = new Color(0.035f, 0.045f, 0.075f);
        RenderSettings.ambientIntensity = 1.12f;
        RenderSettings.fog = false;
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

    private static Material CreatePosterMaterial(string resourcePath, int index)
    {
        Material material = CreateMaterial(
            $"Golden era poster {index + 1}", Color.white, 0f, 0.3f);
        Texture2D texture = Resources.Load<Texture2D>(resourcePath);
        if (texture == null)
        {
            Debug.LogWarning($"Poster texture is missing at Resources/{resourcePath}.");
            material.color = new Color(0.32f, 0.2f, 0.08f);
            return material;
        }

        material.mainTexture = texture;
        material.SetTexture("_BaseMap", texture);
        material.SetTexture("_MainTex", texture);
        material.SetColor("_BaseColor", Color.white);
        material.SetColor("_Color", Color.white);
        return material;
    }

    private static Material CreateTransparentMaterial(
        string name, Color color, float smoothness)
    {
        Material material = CreateMaterial(name, color, 0f, smoothness);
        material.SetFloat("_Surface", 1f);
        material.SetFloat("_Blend", 0f);
        material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        material.SetFloat("_ZWrite", 0f);
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.DisableKeyword("_ALPHATEST_ON");
        material.renderQueue = (int)RenderQueue.Transparent;
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
