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
