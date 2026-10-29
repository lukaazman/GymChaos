using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Race-only city fill. The city ring was laid out around the gym, not the
/// track, so some stretches of road looked past a single row of towers into
/// the empty edge of the world. Before the carve, both sides of the track
/// are probed; wherever no tower stands within reach of the road edge, an
/// extra copy of the city block (same GLB, shared meshes) is added with its
/// short side to the road and paving under it. The carver then clears the
/// road through it like through the ring, and <see cref="Clear"/> (called by
/// the carver's Restore) removes it after the race.
/// </summary>
public static class GymRaceCityFiller
{
    // Probes: every SampleStep metres of road, at these distances past the edge.
    private const float SampleStep = 12f;
    // A row of towers this close hides everything behind it from the road.
    private static readonly float[] ProbeOffsets = { 8f, 14f, 20f, 26f, 32f };
    private const float BuildingPadding = 2f;
    // A filled stretch covers the road around it this far.
    private const float FillSpacing = 36f;
    private const float RoadGap = 6f;
    private const float Scale = GymCityDystopiaSurroundings.CornerCopyScale;
    private const int MaxFills = 8;

    private static readonly List<GameObject> fills = new List<GameObject>();
    private static readonly List<Rect> rects = new List<Rect>();

    public static int FillCount => fills.Count;
    public static int CopyCount { get; private set; }

    /// <summary>Share of road-side samples (both sides, both branches) with a tower or road beside them.</summary>
    public static float MeasureCoverage(List<(Vector3 edge, Vector3 outward)> gaps = null)
    {
        GymRaceCityCarver.CollectBuildingRects(rects);
        // Towers standing on the road are carved away: they cover nothing.
        rects.RemoveAll(r => GymRaceTrackLayout.SignedDistanceToRoad(new Vector3(r.center.x, 0f, r.center.y)) < 0f);
        int sides = 0, covered = 0;
        foreach (GymRaceTrackPath path in new[] { GymRaceTrackLayout.Main, GymRaceTrackLayout.Shortcut })
        {
            for (float s = 0f; s < path.Length; s += SampleStep)
            {
                path.Sample(s, out Vector3 point, out _, out Vector3 right, out float halfWidth);
                for (int side = -1; side <= 1; side += 2)
                {
                    Vector3 outward = new Vector3(right.x, 0f, right.z).normalized * side;
                    sides++;
                    if (IsCovered(point, outward, halfWidth))
                    {
                        covered++;
                    }
                    else
                    {
                        gaps?.Add((point + outward * halfWidth, outward));
                    }
                }
            }
        }
        return sides > 0 ? covered / (float)sides : 1f;
    }

    private static bool IsCovered(Vector3 point, Vector3 outward, float halfWidth)
    {
        Bounds gym = GymCityDystopiaSurroundings.ProtectedBounds;
        gym.Expand(new Vector3(10f, 1000f, 10f));
        for (int i = 0; i < ProbeOffsets.Length; i++)
        {
            Vector3 probe = point + outward * (halfWidth + ProbeOffsets[i]);
            // Another stretch of road, or the gym grounds: the view is the game.
            if (GymRaceTrackLayout.SignedDistanceToRoad(probe) < 0f ||
                gym.Contains(new Vector3(probe.x, gym.center.y, probe.z))) return true;
            Vector2 flat = new Vector2(probe.x, probe.z);
            for (int r = 0; r < rects.Count; r++)
            {
                if (rects[r].Contains(flat)) return true;
            }
        }
        return false;
    }

    /// <summary>Adds copies at the empty stretches and waits for them to load.</summary>
    public static IEnumerator Fill()
    {
        Clear();
        var gaps = new List<(Vector3 edge, Vector3 outward)>();
        float before = MeasureCoverage(gaps);
        Bounds keepClear = GymCityDystopiaSurroundings.ProtectedBounds;
        keepClear.Expand(new Vector3(8f, 100f, 8f));
        float floorY = keepClear.center.y - GymCityDystopiaSurroundings.ProtectedHalfHeight;
        int pending = 0;
        var placed = new List<Vector3>();
        foreach ((Vector3 edge, Vector3 roadOutward) in gaps)
        {
            if (placed.Count >= MaxFills) break;
            // Face the copy's short (near) side to the road, in a 90-degree
            // step: the carver expects axis-aligned copies.
            Vector3 outward = Snap(roadOutward);
            Vector3 origin = edge + outward * (RoadGap + GymCityDystopiaSurroundings.SourceNearDepthMetres * Scale);
            origin.y = floorY;
            bool near = false;
            for (int i = 0; i < placed.Count && !near; i++)
            {
                near = GymShared.FlatDistance(placed[i], origin) < FillSpacing;
            }
            if (near) continue;
            Quaternion rotation = Quaternion.LookRotation(-outward, Vector3.up);
            Bounds area = CopyArea(origin, rotation);
            if (area.Intersects(keepClear)) continue;
            placed.Add(origin);
            pending++;
            GameObject ground = GroundUnder(area, floorY);
            fills.Add(ground);
            GymCityDystopiaSurroundings.RequestExtraCopy("City Dystopia Race Fill " + fills.Count, origin, rotation, Scale,
                loaded =>
                {
                    pending--;
                    if (loaded == null) return;
                    fills.Add(loaded);
                    CopyCount++;
                });
        }
        float giveUp = Time.realtimeSinceStartup + 20f;
        while (pending > 0 && Time.realtimeSinceStartup < giveUp)
        {
            yield return null;
        }
        float after = MeasureCoverage();
        Debug.Log($"GYMCHAOS_RACE_CITY_FILL copies={placed.Count} coverage={before:P0}->{after:P0} gaps={gaps.Count}");
    }

    /// <summary>Removes the race fill (the carver calls this when it restores the city).</summary>
    public static void Clear()
    {
        for (int i = 0; i < fills.Count; i++)
        {
            if (fills[i] != null) GymShared.DestroyRuntimeObject(fills[i]);
        }
        fills.Clear();
        CopyCount = 0;
    }

    private static Vector3 Snap(Vector3 direction) =>
        Mathf.Abs(direction.x) > Mathf.Abs(direction.z)
            ? new Vector3(Mathf.Sign(direction.x), 0f, 0f)
            : new Vector3(0f, 0f, Mathf.Sign(direction.z));

    // World footprint of one scaled copy: near/far depth along its local Z.
    private static Bounds CopyArea(Vector3 origin, Quaternion rotation)
    {
        float halfWidth = GymCityDystopiaSurroundings.SourceWidthMetres * Scale * 0.5f;
        Vector3 near = rotation * new Vector3(0f, 0f, GymCityDystopiaSurroundings.SourceNearDepthMetres * Scale);
        Vector3 far = rotation * new Vector3(0f, 0f, -GymCityDystopiaSurroundings.SourceFarDepthMetres * Scale);
        Vector3 side = rotation * new Vector3(halfWidth, 0f, 0f);
        Bounds area = new Bounds(origin + near, Vector3.zero);
        area.Encapsulate(origin + far + side);
        area.Encapsulate(origin + far - side);
        area.Encapsulate(origin + near + side);
        area.Encapsulate(origin + near - side);
        area.size = new Vector3(area.size.x, 1f, area.size.z);
        return area;
    }

    private static GameObject GroundUnder(Bounds area, float floorY)
    {
        GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
        ground.name = "City Dystopia Race Fill Ground";
        GymShared.DestroyRuntimeObject(ground.GetComponent<Collider>());
        ground.transform.position = new Vector3(area.center.x, floorY + GymOutdoorFoliage.GroundTopOffset - 0.1f, area.center.z);
        ground.transform.localScale = new Vector3(area.size.x + 4f, 0.16f, area.size.z + 4f);
        ground.GetComponent<MeshRenderer>().sharedMaterial = GymMaterialLibrary.Lit(
            "City Dystopia Ground Infill", GymOutdoorFoliage.CityPavingColor, 0f, 0.08f);
        return ground;
    }
}
