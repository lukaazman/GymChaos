using UnityEngine;

/// <summary>
/// Named world colours (sRGB, as written to _BaseColor). Grounded and neutral:
/// arcade colour belongs to signage, UI and effects, not to ground or walls.
/// GymChaosPaletteVerifier enforces the HSV limits of the key surfaces.
/// </summary>
public static class GymPalette
{
    public static readonly Color Asphalt = new Color(0.155f, 0.155f, 0.15f);
    public static readonly Color Concrete = new Color(0.36f, 0.355f, 0.34f);
    public static readonly Color ConcreteDark = new Color(0.22f, 0.22f, 0.215f);
    public static readonly Color Steel = new Color(0.3f, 0.31f, 0.32f);
    public static readonly Color DarkMetal = new Color(0.06f, 0.065f, 0.07f);
    public static readonly Color LinePaint = new Color(0.92f, 0.92f, 0.88f);
    public static readonly Color SignBlue = new Color(0.06f, 0.22f, 0.5f);
    public static readonly Color LampWarm = new Color(1f, 0.9f, 0.74f);

    public static readonly Color GymRubber = new Color(0.085f, 0.085f, 0.09f);
    public static readonly Color LockerRubber = new Color(0.07f, 0.072f, 0.075f);
    public static readonly Color Tile = new Color(0.62f, 0.62f, 0.6f);
    public static readonly Color PaintedWall = new Color(0.42f, 0.42f, 0.4f);
    public static readonly Color LockerMetal = new Color(0.26f, 0.27f, 0.28f);

    public static readonly Color LawnDark = new Color(0.23f, 0.3f, 0.17f);
    public static readonly Color LawnLight = new Color(0.34f, 0.4f, 0.23f);
    public static readonly Color Foliage = new Color(0.18f, 0.24f, 0.135f);
    public static readonly Color GrassRoot = new Color(0.15f, 0.2f, 0.115f);
    public static readonly Color GrassTipShort = new Color(0.31f, 0.37f, 0.21f);
    public static readonly Color GrassTipLong = new Color(0.38f, 0.43f, 0.24f);
}
