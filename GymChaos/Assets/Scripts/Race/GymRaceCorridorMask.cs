using UnityEngine;

/// <summary>
/// Flat XZ occupancy grid of the race road (both branches) grown by a margin.
/// Used to decide which city buildings stand in the way and to verify that
/// nothing is left on the road.
/// </summary>
public sealed class GymRaceCorridorMask
{
    public readonly float Margin;
    public readonly float Cell;
    public readonly float MinX;
    public readonly float MinZ;
    public readonly int Width;
    public readonly int Height;
    private readonly bool[] cells;

    public int MarkedCells { get; private set; }

    private GymRaceCorridorMask(float margin, float cell, float minX, float minZ, int width, int height)
    {
        Margin = margin;
        Cell = cell;
        MinX = minX;
        MinZ = minZ;
        Width = width;
        Height = height;
        cells = new bool[width * height];
    }

    public static GymRaceCorridorMask Build(float margin, float cell = 1f)
    {
        GymRaceTrackPath main = GymRaceTrackLayout.Main;
        GymRaceTrackPath shortcut = GymRaceTrackLayout.Shortcut;
        float minX = float.MaxValue;
        float minZ = float.MaxValue;
        float maxX = float.MinValue;
        float maxZ = float.MinValue;
        Grow(main, margin, ref minX, ref minZ, ref maxX, ref maxZ);
        Grow(shortcut, margin, ref minX, ref minZ, ref maxX, ref maxZ);
        int width = Mathf.CeilToInt((maxX - minX) / cell) + 1;
        int height = Mathf.CeilToInt((maxZ - minZ) / cell) + 1;
        GymRaceCorridorMask mask = new GymRaceCorridorMask(margin, cell, minX, minZ, width, height);
        mask.Mark(main);
        mask.Mark(shortcut);
        return mask;
    }

    private static void Grow(
        GymRaceTrackPath path,
        float margin,
        ref float minX,
        ref float minZ,
        ref float maxX,
        ref float maxZ)
    {
        for (int i = 0; i < path.Count; i++)
        {
            float r = path.HalfWidths[i] + margin + 1f;
            Vector3 p = path.Points[i];
            minX = Mathf.Min(minX, p.x - r);
            minZ = Mathf.Min(minZ, p.z - r);
            maxX = Mathf.Max(maxX, p.x + r);
            maxZ = Mathf.Max(maxZ, p.z + r);
        }
    }

    private void Mark(GymRaceTrackPath path)
    {
        // Samples are ~1 m apart; a disc per sample plus a half-sample overlap
        // covers the band without gaps.
        for (int i = 0; i < path.Count; i++)
        {
            Vector3 p = path.Points[i];
            float radius = path.HalfWidths[i] + Margin;
            float radiusSq = radius * radius;
            int x0 = Mathf.Max(0, Mathf.FloorToInt((p.x - radius - MinX) / Cell));
            int x1 = Mathf.Min(Width - 1, Mathf.CeilToInt((p.x + radius - MinX) / Cell));
            int z0 = Mathf.Max(0, Mathf.FloorToInt((p.z - radius - MinZ) / Cell));
            int z1 = Mathf.Min(Height - 1, Mathf.CeilToInt((p.z + radius - MinZ) / Cell));
            for (int z = z0; z <= z1; z++)
            {
                float cz = MinZ + (z + 0.5f) * Cell - p.z;
                for (int x = x0; x <= x1; x++)
                {
                    float cx = MinX + (x + 0.5f) * Cell - p.x;
                    if (cx * cx + cz * cz > radiusSq)
                    {
                        continue;
                    }
                    int index = z * Width + x;
                    if (!cells[index])
                    {
                        cells[index] = true;
                        MarkedCells++;
                    }
                }
            }
        }
    }

    public bool Contains(float x, float z)
    {
        int cx = Mathf.FloorToInt((x - MinX) / Cell);
        int cz = Mathf.FloorToInt((z - MinZ) / Cell);
        if (cx < 0 || cz < 0 || cx >= Width || cz >= Height)
        {
            return false;
        }
        return cells[cz * Width + cx];
    }

    /// <summary>True when any marked cell lies inside the world XZ rectangle.</summary>
    public bool Overlaps(float minX, float minZ, float maxX, float maxZ)
    {
        int x0 = Mathf.Max(0, Mathf.FloorToInt((minX - MinX) / Cell));
        int x1 = Mathf.Min(Width - 1, Mathf.FloorToInt((maxX - MinX) / Cell));
        int z0 = Mathf.Max(0, Mathf.FloorToInt((minZ - MinZ) / Cell));
        int z1 = Mathf.Min(Height - 1, Mathf.FloorToInt((maxZ - MinZ) / Cell));
        for (int z = z0; z <= z1; z++)
        {
            int row = z * Width;
            for (int x = x0; x <= x1; x++)
            {
                if (cells[row + x])
                {
                    return true;
                }
            }
        }
        return false;
    }
}
