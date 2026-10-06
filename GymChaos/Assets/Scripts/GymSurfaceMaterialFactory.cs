using UnityEngine;

public enum GymSurfaceKind
{
    GymRubber,
    LockerRubber,
    BathroomTile,
    Asphalt,
    ConcretePath,
    Courtyard,
    Landscape
}

public static class GymSurfaceMaterialFactory
{
    private const int TextureSize = 32;
    private static bool gymReady;
    private static bool lockerReady;
    private static bool bathroomReady;
    private static bool asphaltReady;
    private static bool pathReady;
    private static bool exteriorReady;
    private static bool markerLogged;

    public static Material CreateGymFloor(string name, Color baseColor)
    {
        return Create(name, baseColor, GymSurfaceKind.GymRubber, new Vector2(7f, 7f));
    }

    public static Material CreateLockerFloor(string name, Color baseColor)
    {
        return Create(name, baseColor, GymSurfaceKind.LockerRubber, new Vector2(5f, 5f));
    }

    // Detailed floors: one texture repeat covers this many metres.
    private const int DetailTextureSize = 512;
    private const float GymTileRepeatMetres = 2f;
    private const float LockerTileRepeatMetres = 1f;

    /// <summary>
    /// Gym training floor: 1 m interlocking rubber tiles with EPDM colour
    /// flecks, per-tile tone and bevelled seams, tiled at true metre scale
    /// over a floor of <paramref name="floorSize"/> (x, z) metres.
    /// </summary>
    public static Material CreateGymFloor(string name, Color baseColor, Vector2 floorSize)
    {
        return CreateDetailed(name, baseColor, GymSurfaceKind.GymRubber,
            floorSize / GymTileRepeatMetres);
    }

    /// <summary>
    /// Locker floor: anti-slip coin-stud rubber in 1 m sheets, tiled at true
    /// metre scale over a floor of <paramref name="floorSize"/> metres.
    /// </summary>
    public static Material CreateLockerFloor(string name, Color baseColor, Vector2 floorSize)
    {
        return CreateDetailed(name, baseColor, GymSurfaceKind.LockerRubber,
            floorSize / LockerTileRepeatMetres);
    }

    public static int DetailTextureSizeForVerification => DetailTextureSize;

    private static Material CreateDetailed(
        string name, Color baseColor, GymSurfaceKind kind, Vector2 tiling)
    {
        Material material = Create(name, baseColor, kind, tiling);
        Texture2D texture = new Texture2D(
            DetailTextureSize, DetailTextureSize, TextureFormat.RGBA32, true, false);
        texture.name = name + " detail texture";
        texture.wrapMode = TextureWrapMode.Repeat;
        texture.filterMode = FilterMode.Trilinear;
        texture.anisoLevel = 4;
        int seed = StableSeed(name, kind);
        Color32[] pixels = kind == GymSurfaceKind.LockerRubber
            ? BuildCoinRubber(baseColor, seed)
            : BuildTileRubber(baseColor, seed);
        texture.SetPixels32(pixels);
        texture.Apply(true, true);
        if (material.HasProperty("_BaseMap"))
        {
            material.SetTexture("_BaseMap", texture);
        }
        if (material.HasProperty("_MainTex"))
        {
            material.SetTexture("_MainTex", texture);
        }
        // Rubber: matte, with a faint sheen from the flecks.
        material.SetFloat("_Smoothness", kind == GymSurfaceKind.LockerRubber ? 0.3f : 0.2f);
        Debug.Log($"GYMCHAOS_FLOOR_DETAIL_OK kind={kind} size={DetailTextureSize} " +
            $"tiling={tiling.x:F1}x{tiling.y:F1}");
        return material;
    }

    // 2 x 2 tiles per repeat. The pattern is periodic in the texture size, so
    // it tiles without seams.
    private static Color32[] BuildTileRubber(Color baseColor, int seed)
    {
        const int size = DetailTextureSize;
        const int tile = size / 2;
        Color32[] pixels = new Color32[size * size];
        Color fleckGrey = new Color(0.42f, 0.45f, 0.5f);
        Color fleckBlue = new Color(0.12f, 0.27f, 0.62f);
        Color fleckRed = new Color(0.62f, 0.12f, 0.07f);
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                int tileX = x / tile;
                int tileY = y / tile;
                int localX = x % tile;
                int localY = y % tile;
                // Each tile its own slight tone, like real pressed batches.
                float tileTone = 1f + (Hash01(tileX, tileY, seed ^ 0x5bd1) - 0.5f) * 0.16f;
                float mottling = TileableValueNoise(x, y, 32, size, seed) * 0.14f - 0.07f;
                float grain = (Hash01(x, y, seed) - 0.5f) * 0.18f;
                Color color = baseColor * (1.35f * tileTone + mottling + grain);

                // Granule flecks: 2 x 2 px cells, sparse, three colours.
                float fleck = Hash01(x >> 1, y >> 1, seed ^ 0x2c9);
                if (fleck > 0.955f)
                {
                    float pick = Hash01(x >> 1, y >> 1, seed ^ 0x77f);
                    Color fleckColor = pick < 0.62f ? fleckGrey
                        : pick < 0.93f ? fleckBlue : fleckRed;
                    color = Color.Lerp(color, fleckColor, 0.32f + (fleck - 0.955f) * 6f);
                }

                // Bevelled seams: dark groove, lit upper-left lip.
                int edge = Mathf.Min(Mathf.Min(localX, tile - 1 - localX),
                    Mathf.Min(localY, tile - 1 - localY));
                if (edge <= 1)
                {
                    color *= 0.3f;
                }
                else if (edge == 2 && (localX == 2 || localY == 2))
                {
                    color *= 1.32f;
                }
                else if (edge <= 3)
                {
                    color *= 0.82f;
                }
                pixels[y * size + x] = ToColor32(color);
            }
        }
        return pixels;
    }

    // Coin-stud rubber: raised round studs on an 8 x 8 grid per 1 m sheet.
    private static Color32[] BuildCoinRubber(Color baseColor, int seed)
    {
        const int size = DetailTextureSize;
        const int pitch = size / 8;
        const float radius = pitch * 0.3f;
        Color32[] pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float grain = (Hash01(x, y, seed) - 0.5f) * 0.14f;
                float mottling = TileableValueNoise(x, y, 64, size, seed) * 0.1f - 0.05f;
                Color color = baseColor * (2.3f + grain + mottling);
                // Studs in alternate rows sit half a pitch over.
                int row = y / pitch;
                float offset = (row & 1) == 0 ? 0f : pitch * 0.5f;
                float cx = Mathf.Repeat(x - offset, pitch) - pitch * 0.5f;
                float cy = (y % pitch) - pitch * 0.5f;
                float distance = Mathf.Sqrt(cx * cx + cy * cy);
                if (distance < radius)
                {
                    // Domed top lit from the upper left, darker rim.
                    float light = Mathf.Clamp(-(cx + cy) / (radius * 1.4f), -1f, 1f);
                    float rim = Mathf.InverseLerp(radius * 0.7f, radius, distance);
                    color *= 1.45f + light * 0.45f - rim * 0.3f;
                }
                else if (distance < radius + 2.5f)
                {
                    // Contact shadow on the lower right of each stud.
                    float side = Mathf.Clamp01((cx + cy) / (radius * 1.2f));
                    color *= 1f - side * 0.5f;
                }
                // Sheet seams every metre.
                if (x <= 1 || y <= 1)
                {
                    color *= 0.45f;
                }
                pixels[y * size + x] = ToColor32(color);
            }
        }
        return pixels;
    }

    private static float TileableValueNoise(int x, int y, int cell, int period, int seed)
    {
        int cells = Mathf.Max(1, period / cell);
        float fx = (float)x / cell;
        float fy = (float)y / cell;
        int x0 = Mathf.FloorToInt(fx);
        int y0 = Mathf.FloorToInt(fy);
        float tx = fx - x0;
        float ty = fy - y0;
        tx = tx * tx * (3f - 2f * tx);
        ty = ty * ty * (3f - 2f * ty);
        float a = Hash01(x0 % cells, y0 % cells, seed ^ 0x1f3);
        float b = Hash01((x0 + 1) % cells, y0 % cells, seed ^ 0x1f3);
        float c = Hash01(x0 % cells, (y0 + 1) % cells, seed ^ 0x1f3);
        float d = Hash01((x0 + 1) % cells, (y0 + 1) % cells, seed ^ 0x1f3);
        return Mathf.Lerp(Mathf.Lerp(a, b, tx), Mathf.Lerp(c, d, tx), ty);
    }

    private static Color32 ToColor32(Color color)
    {
        return new Color32(
            (byte)(Mathf.Clamp01(color.r) * 255f),
            (byte)(Mathf.Clamp01(color.g) * 255f),
            (byte)(Mathf.Clamp01(color.b) * 255f),
            255);
    }

    public static Material CreateBathroomTile(string name, Color baseColor)
    {
        return Create(name, baseColor, GymSurfaceKind.BathroomTile, new Vector2(4f, 4f));
    }

    public static Material CreateAsphalt(string name, Color baseColor)
    {
        return Create(name, baseColor, GymSurfaceKind.Asphalt, new Vector2(6f, 6f));
    }

    public static Material CreateConcretePath(string name, Color baseColor)
    {
        return Create(name, baseColor, GymSurfaceKind.ConcretePath, new Vector2(5f, 5f));
    }

    public static Material CreateCourtyard(string name, Color baseColor)
    {
        return Create(name, baseColor, GymSurfaceKind.Courtyard, new Vector2(4f, 4f));
    }

    public static Material CreateLandscape(string name, Color baseColor)
    {
        return Create(name, baseColor, GymSurfaceKind.Landscape, new Vector2(5f, 5f));
    }

    private static Material Create(
        string name, Color baseColor, GymSurfaceKind kind, Vector2 tiling)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
        {
            shader = Shader.Find("Standard");
        }

        Material material = new Material(shader);
        material.name = name;
        material.SetFloat("_Metallic", kind == GymSurfaceKind.Asphalt ? 0.08f : 0.02f);
        material.SetFloat("_Smoothness", kind == GymSurfaceKind.BathroomTile ? 0.46f : 0.25f);

        Texture2D texture = new Texture2D(
            TextureSize, TextureSize, TextureFormat.RGBA32, false, false);
        texture.name = name + " low resolution texture";
        texture.wrapMode = TextureWrapMode.Repeat;
        texture.filterMode = FilterMode.Bilinear;
        texture.anisoLevel = 1;

        Color[] pixels = new Color[TextureSize * TextureSize];
        int seed = StableSeed(name, kind);
        for (int y = 0; y < TextureSize; y++)
        {
            for (int x = 0; x < TextureSize; x++)
            {
                float noise = (Hash01(x, y, seed) - 0.5f) * 0.12f;
                float pattern = SurfacePattern(kind, x, y);
                float value = 1f + noise + pattern;
                pixels[y * TextureSize + x] = new Color(
                    Mathf.Clamp01(baseColor.r * value),
                    Mathf.Clamp01(baseColor.g * value),
                    Mathf.Clamp01(baseColor.b * value),
                    1f);
            }
        }
        texture.SetPixels(pixels);
        texture.Apply(false, true);

        if (material.HasProperty("_BaseMap"))
        {
            material.SetTexture("_BaseMap", texture);
            material.SetTextureScale("_BaseMap", tiling);
        }
        if (material.HasProperty("_MainTex"))
        {
            material.SetTexture("_MainTex", texture);
            material.SetTextureScale("_MainTex", tiling);
        }
        if (material.HasProperty("_BaseColor"))
        {
            material.SetColor("_BaseColor", Color.white);
        }
        if (material.HasProperty("_Color"))
        {
            material.SetColor("_Color", Color.white);
        }

        Register(kind, texture);
        return material;
    }

    private static float SurfacePattern(GymSurfaceKind kind, int x, int y)
    {
        switch (kind)
        {
            case GymSurfaceKind.GymRubber:
                return (x % 8 == 0 || y % 8 == 0) ? -0.12f : 0f;
            case GymSurfaceKind.LockerRubber:
                return (x % 12 == 0 || y % 12 == 0) ? -0.06f : 0f;
            case GymSurfaceKind.BathroomTile:
                return (x % 8 == 0 || y % 8 == 0) ? -0.16f : 0.015f;
            case GymSurfaceKind.Asphalt:
                return ((x + y * 3) % 11 == 0) ? -0.08f : 0f;
            case GymSurfaceKind.ConcretePath:
                return (x % 16 == 0 || y % 16 == 0) ? -0.07f : 0f;
            case GymSurfaceKind.Courtyard:
                return ((x * 5 + y * 7) % 13 == 0) ? -0.05f : 0f;
            case GymSurfaceKind.Landscape:
                return ((x * 3 + y * 5) % 9 == 0) ? 0.035f : 0f;
            default:
                return 0f;
        }
    }

    private static int StableSeed(string name, GymSurfaceKind kind)
    {
        unchecked
        {
            int hash = 17 + (int)kind * 31;
            for (int i = 0; i < name.Length; i++)
            {
                hash = hash * 31 + name[i];
            }
            return hash;
        }
    }

    private static float Hash01(int x, int y, int seed)
    {
        unchecked
        {
            uint value = (uint)(seed ^ (x * 374761393) ^ (y * 668265263));
            value = (value ^ (value >> 13)) * 1274126177u;
            value ^= value >> 16;
            return (value & 0xffffu) / 65535f;
        }
    }

    private static void Register(GymSurfaceKind kind, Texture2D texture)
    {
        switch (kind)
        {
            case GymSurfaceKind.GymRubber: gymReady = true; break;
            case GymSurfaceKind.LockerRubber: lockerReady = true; break;
            case GymSurfaceKind.BathroomTile: bathroomReady = true; break;
            case GymSurfaceKind.Asphalt: asphaltReady = true; break;
            case GymSurfaceKind.ConcretePath: pathReady = true; break;
            case GymSurfaceKind.Courtyard:
            case GymSurfaceKind.Landscape: exteriorReady = true; break;
        }

        if (!markerLogged && gymReady && lockerReady && bathroomReady &&
            asphaltReady && pathReady && exteriorReady)
        {
            markerLogged = true;
            Debug.Log(
                "GYMCHAOS_SURFACE_MATERIALS_OK resolution=32 " +
                "gymRubber=1 lockerRubber=1 bathroomTile=1 " +
                "asphalt=1 concretePath=1 exterior=1");
        }
    }
}
