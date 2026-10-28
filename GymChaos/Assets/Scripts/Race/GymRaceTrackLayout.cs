using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// One sampled race path: a centre line with per-sample half width and bank.
/// Samples are about one metre apart so progress lookups stay cheap.
/// </summary>
public sealed class GymRaceTrackPath
{
    public readonly string Name;
    public readonly bool Closed;
    public readonly Vector3[] Points;
    public readonly Vector3[] Tangents;
    public readonly Vector3[] Rights;
    public readonly Vector3[] Ups;
    public readonly float[] HalfWidths;
    public readonly float[] Distances;
    public readonly float Length;

    public int Count => Points.Length;

    public GymRaceTrackPath(
        string name,
        bool closed,
        List<Vector3> points,
        List<float> halfWidths,
        List<float> banks)
    {
        Name = name;
        Closed = closed;
        int count = points.Count;
        Points = points.ToArray();
        HalfWidths = halfWidths.ToArray();
        Tangents = new Vector3[count];
        Rights = new Vector3[count];
        Ups = new Vector3[count];
        Distances = new float[count];

        float distance = 0f;
        for (int i = 0; i < count; i++)
        {
            if (i > 0)
            {
                distance += Vector3.Distance(Points[i - 1], Points[i]);
            }
            Distances[i] = distance;

            Vector3 previous = i > 0 ? Points[i - 1] : (closed ? Points[count - 1] : Points[i]);
            Vector3 next = i < count - 1 ? Points[i + 1] : (closed ? Points[0] : Points[i]);
            Vector3 tangent = (next - previous).normalized;
            Tangents[i] = tangent;
            Vector3 flatRight = Vector3.Cross(Vector3.up, tangent);
            flatRight.y = 0f;
            flatRight = flatRight.sqrMagnitude > 1e-6f ? flatRight.normalized : Vector3.right;
            // Positive bank tilts the road down toward its right side.
            Quaternion bank = Quaternion.AngleAxis(banks[i], tangent);
            Rights[i] = bank * flatRight;
            Ups[i] = Vector3.Cross(tangent, Rights[i]).normalized;
        }

        Length = closed ? distance + Vector3.Distance(Points[count - 1], Points[0]) : distance;
    }

    public int Wrap(int index)
    {
        if (!Closed)
        {
            return Mathf.Clamp(index, 0, Count - 1);
        }
        index %= Count;
        return index < 0 ? index + Count : index;
    }

    public float WrapDistance(float s)
    {
        if (!Closed)
        {
            return Mathf.Clamp(s, 0f, Length);
        }
        s %= Length;
        return s < 0f ? s + Length : s;
    }

    public int IndexAtDistance(float s)
    {
        s = WrapDistance(s);
        int low = 0;
        int high = Count - 1;
        while (low < high)
        {
            int mid = (low + high + 1) >> 1;
            if (Distances[mid] <= s)
            {
                low = mid;
            }
            else
            {
                high = mid - 1;
            }
        }
        return low;
    }

    public void Sample(
        float s,
        out Vector3 position,
        out Vector3 tangent,
        out Vector3 right,
        out float halfWidth)
    {
        s = WrapDistance(s);
        int a = IndexAtDistance(s);
        int b = Wrap(a + 1);
        float segmentEnd = b == 0 && Closed ? Length : Distances[b];
        float segmentLength = Mathf.Max(segmentEnd - Distances[a], 1e-4f);
        float t = Closed || a < Count - 1 ? Mathf.Clamp01((s - Distances[a]) / segmentLength) : 0f;
        position = Vector3.Lerp(Points[a], Points[b], t);
        tangent = Vector3.Slerp(Tangents[a], Tangents[b], t).normalized;
        right = Vector3.Slerp(Rights[a], Rights[b], t).normalized;
        halfWidth = Mathf.Lerp(HalfWidths[a], HalfWidths[b], t);
    }

    /// <summary>
    /// Closest centre-line distance to <paramref name="point"/>. With a hint,
    /// only a window around it is searched so a racer on the spiral does not
    /// jump to the lap section passing underneath.
    /// </summary>
    public float ClosestDistance(
        Vector3 point,
        float hint,
        float window,
        out float lateral,
        out float verticalGap)
    {
        int start;
        int end;
        if (hint < 0f || window <= 0f)
        {
            start = 0;
            end = Closed ? Count : Count - 1;
        }
        else
        {
            start = IndexAtDistance(hint - window);
            int hintIndex = IndexAtDistance(hint);
            int span = Mathf.CeilToInt(window * 2f) + 2;
            if (!Closed)
            {
                start = Mathf.Max(0, hintIndex - span / 2);
                end = Mathf.Min(Count - 1, hintIndex + span / 2);
            }
            else
            {
                start = hintIndex - span / 2;
                end = hintIndex + span / 2;
            }
        }

        float bestScore = float.MaxValue;
        float bestS = 0f;
        lateral = 0f;
        verticalGap = 0f;
        for (int raw = start; raw < end; raw++)
        {
            int a = Wrap(raw);
            int b = Wrap(raw + 1);
            if (!Closed && a == Count - 1)
            {
                break;
            }
            Vector3 pa = Points[a];
            Vector3 pb = Points[b];
            Vector3 segment = pb - pa;
            float lengthSq = segment.sqrMagnitude;
            float t = lengthSq > 1e-6f ? Mathf.Clamp01(Vector3.Dot(point - pa, segment) / lengthSq) : 0f;
            Vector3 closest = pa + segment * t;
            Vector3 offset = point - closest;
            // Vertical distance counts extra so stacked spiral decks resolve.
            float score = offset.x * offset.x + offset.z * offset.z + offset.y * offset.y * 4f;
            if (score < bestScore)
            {
                bestScore = score;
                float segmentEnd = b == 0 && Closed ? Length : Distances[b];
                bestS = Mathf.Lerp(Distances[a], segmentEnd, t);
                Vector3 right = Vector3.Slerp(Rights[a], Rights[b], t);
                lateral = Vector3.Dot(offset, right);
                verticalGap = offset.y;
            }
        }
        return WrapDistance(bestS);
    }

    public float ClosestDistance(Vector3 point, float hint = -1f, float window = 0f)
    {
        return ClosestDistance(point, hint, window, out _, out _);
    }
}

/// <summary>
/// Race track layout traced from the user's top-down drawing of the city.
/// World axes: +X east, +Z north. The lap runs counter-clockwise: north from
/// the start grid on the visitor road, west along the north city row, south
/// down the west side (outer road or the sunken shortcut), east along the
/// south row, up the east ramp into the descending spiral, then down the
/// diagonal back to the start line.
/// </summary>
public static class GymRaceTrackLayout
{
    public const float FloorY = -0.15f;
    public const float RoadLift = 0.03f;
    public const int Laps = 3;
    public const int RacerCount = 6;
    public const float GridRowSpacing = 7.2f;

    // Centre of the visitor road's north leg (GymOutdoorBuilder roadTurnX)
    // and its lane offset: the grid sits on the two existing lanes.
    public const float GridRoadCenterX = 208f;
    public const float GridLaneOffset = 2.56f;
    public const float StartLineZ = 68f;

    public const float SpiralRadius = 16f;
    public static readonly Vector2 SpiralCenter = new Vector2(256f, -2f);
    public const float SpiralEntryHeight = 12f;
    public const float SpiralExitHeight = 2.6f;
    public const float ShortcutDepth = 4f;

    private const float MainHalfWidth = 6.5f;
    private const float GridHalfWidth = 5.6f;
    private const float SpiralHalfWidth = 5.4f;
    private const float ShortcutHalfWidth = 4.6f;
    private const float SampleSpacing = 1f;

    private static GymRaceTrackPath main;
    private static GymRaceTrackPath shortcut;

    public static GymRaceTrackPath Main
    {
        get
        {
            EnsureBuilt();
            return main;
        }
    }

    public static GymRaceTrackPath Shortcut
    {
        get
        {
            EnsureBuilt();
            return shortcut;
        }
    }

    /// <summary>Main-path distance of the start/finish line.</summary>
    public static float StartLineDistance { get; private set; }
    /// <summary>Main-path distance where the shortcut leaves the outer road.</summary>
    public static float ShortcutForkDistance { get; private set; }
    /// <summary>Main-path distance where the shortcut rejoins.</summary>
    public static float ShortcutMergeDistance { get; private set; }

    public static float LapLength => Main.Length;

    private struct ControlPoint
    {
        public Vector2 Xz;
        public float Height;
        public float HalfWidth;
        public float Bank;

        public ControlPoint(float x, float z, float height, float halfWidth, float bank = 0f)
        {
            Xz = new Vector2(x, z);
            Height = height;
            HalfWidth = halfWidth;
            Bank = bank;
        }
    }

    private static void EnsureBuilt()
    {
        if (main != null)
        {
            return;
        }

        List<ControlPoint> mainControls = BuildMainControls();
        main = Resample("Race Main", mainControls, true);
        shortcut = Resample("Race Shortcut", BuildShortcutControls(), false);

        StartLineDistance = main.ClosestDistance(
            new Vector3(GridRoadCenterX, FloorY, StartLineZ));
        ShortcutForkDistance = main.ClosestDistance(shortcut.Points[0]);
        ShortcutMergeDistance = main.ClosestDistance(shortcut.Points[shortcut.Count - 1]);
    }

    private static List<ControlPoint> BuildMainControls()
    {
        float w = MainHalfWidth;
        List<ControlPoint> c = new List<ControlPoint>
        {
            // Visitor road north leg: grid, start line, into the north city row.
            new ControlPoint(208.3f, 44f, 0f, GridHalfWidth),
            new ControlPoint(GridRoadCenterX, 54f, 0f, GridHalfWidth),
            new ControlPoint(GridRoadCenterX, StartLineZ, 0f, GridHalfWidth),
            new ControlPoint(208.4f, 82f, 0f, w),
            new ControlPoint(204.5f, 93.5f, 0f, w),
            new ControlPoint(192f, 99f, 0f, w),
            new ControlPoint(167f, 99.5f, 0f, w),
            new ControlPoint(134f, 98.5f, 0f, w),
            new ControlPoint(112f, 94f, 0f, w),
            // "TWO WAY": the outer road swings wide around the west row.
            new ControlPoint(94f, 86f, 0f, w),
            new ControlPoint(82f, 68f, 0f, w),
            new ControlPoint(79.5f, 40f, 0f, w),
            new ControlPoint(79f, 8f, 0f, w),
            new ControlPoint(80f, -24f, 0f, w),
            new ControlPoint(83f, -46f, 0f, w),
            // "SHARP TURN" in the south-west corner.
            new ControlPoint(87f, -62f, 0f, w),
            new ControlPoint(95f, -69.5f, 0f, w),
            new ControlPoint(110f, -71.5f, 0f, w),
            new ControlPoint(143f, -71.5f, 0f, w),
            new ControlPoint(178f, -71f, 0f, w),
            new ControlPoint(205f, -69f, 0f, w),
            new ControlPoint(224f, -63f, 0.6f, w),
            // "UPHILL TO A SPIRAL" along the east row.
            new ControlPoint(235.5f, -51f, 3f, w),
            new ControlPoint(239.5f, -34f, 6.2f, SpiralHalfWidth + 0.6f),
            new ControlPoint(240f, -18f, 9.4f, SpiralHalfWidth),
        };

        // "DOWNHILL SLOW DECLINE": one clockwise turn that drops enough for
        // the exit to pass under its own entry at the west tangent point.
        const int spiralSteps = 18;
        for (int step = 0; step <= spiralSteps; step++)
        {
            float t = step / (float)spiralSteps;
            float angle = Mathf.PI - t * Mathf.PI * 2f;
            float height = Mathf.Lerp(SpiralEntryHeight, SpiralExitHeight, Smooth(t));
            c.Add(new ControlPoint(
                SpiralCenter.x + Mathf.Cos(angle) * SpiralRadius,
                SpiralCenter.y + Mathf.Sin(angle) * SpiralRadius,
                height,
                SpiralHalfWidth,
                8f));
        }

        // Diagonal "downwards" back onto the visitor road behind the grid.
        c.Add(new ControlPoint(240.8f, 14f, 2.2f, SpiralHalfWidth + 0.4f));
        c.Add(new ControlPoint(238f, 25f, 1.6f, w));
        c.Add(new ControlPoint(226f, 31.8f, 0.8f, w));
        c.Add(new ControlPoint(216.5f, 33.8f, 0.2f, w));
        c.Add(new ControlPoint(210.8f, 37.5f, 0f, GridHalfWidth));
        return c;
    }

    private static List<ControlPoint> BuildShortcutControls()
    {
        float w = ShortcutHalfWidth;
        float d = -ShortcutDepth;
        // "SHORT CUT WITH DOWNHILL AND UPHILL": a sunken cut inside the outer
        // road. It leaves the north row early and rejoins before the south-west
        // corner.
        return new List<ControlPoint>
        {
            new ControlPoint(118f, 96f, 0f, w),
            new ControlPoint(109f, 88f, 0f, w),
            new ControlPoint(106f, 76f, -0.5f, w),
            new ControlPoint(104.2f, 67f, -2.2f, w),
            new ControlPoint(103f, 54f, d, w),
            new ControlPoint(102.5f, 40f, d, w),
            new ControlPoint(102f, 20f, d * 0.55f, w),
            new ControlPoint(100.5f, 4f, 0f, w),
            new ControlPoint(97f, -14f, 0f, w),
            new ControlPoint(90.5f, -31f, 0f, w),
            new ControlPoint(83.5f, -44f, 0f, w),
        };
    }

    private static GymRaceTrackPath Resample(string name, List<ControlPoint> controls, bool closed)
    {
        List<Vector3> points = new List<Vector3>();
        List<float> halfWidths = new List<float>();
        List<float> banks = new List<float>();
        int count = controls.Count;
        int segments = closed ? count : count - 1;
        for (int i = 0; i < segments; i++)
        {
            ControlPoint p0 = controls[closed ? (i - 1 + count) % count : Mathf.Max(i - 1, 0)];
            ControlPoint p1 = controls[i];
            ControlPoint p2 = controls[(i + 1) % count];
            ControlPoint p3 = controls[closed ? (i + 2) % count : Mathf.Min(i + 2, count - 1)];
            Vector3 a = ToWorld(p0);
            Vector3 b = ToWorld(p1);
            Vector3 cc = ToWorld(p2);
            Vector3 dd = ToWorld(p3);
            float chord = Vector3.Distance(b, cc);
            int steps = Mathf.Max(1, Mathf.CeilToInt(chord / SampleSpacing));
            for (int step = 0; step < steps; step++)
            {
                float t = step / (float)steps;
                points.Add(CatmullRom(a, b, cc, dd, t));
                halfWidths.Add(Mathf.Lerp(p1.HalfWidth, p2.HalfWidth, t));
                banks.Add(Mathf.Lerp(p1.Bank, p2.Bank, t));
            }
        }
        if (!closed)
        {
            ControlPoint last = controls[count - 1];
            points.Add(ToWorld(last));
            halfWidths.Add(last.HalfWidth);
            banks.Add(last.Bank);
        }
        return new GymRaceTrackPath(name, closed, points, halfWidths, banks);
    }

    private static Vector3 ToWorld(ControlPoint point)
    {
        return new Vector3(point.Xz.x, FloorY + RoadLift + point.Height, point.Xz.y);
    }

    private static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
    {
        // Centripetal-like weighting is unnecessary at these spacings; uniform
        // Catmull-Rom keeps the hand-traced corners.
        float t2 = t * t;
        float t3 = t2 * t;
        return 0.5f * (
            2f * p1 +
            (-p0 + p2) * t +
            (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 +
            (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
    }

    private static float Smooth(float t)
    {
        return t * t * (3f - 2f * t);
    }

    /// <summary>
    /// Grid slot pose: three rows of two on the visitor road lanes behind the
    /// start line, slot 0 on pole. Slot 5 (last row) is the player's default.
    /// </summary>
    public static Pose GridSlot(int slot)
    {
        int row = slot / 2;
        int lane = slot % 2;
        float z = StartLineZ - 5.5f - row * GridRowSpacing;
        float x = GridRoadCenterX + (lane == 0 ? -GridLaneOffset : GridLaneOffset);
        return new Pose(new Vector3(x, FloorY + RoadLift, z), Quaternion.identity);
    }

    /// <summary>
    /// Race progress along the main lap for a point on either branch. The
    /// shortcut maps linearly onto the outer-road span it bypasses.
    /// </summary>
    public static float MainEquivalentDistance(bool onShortcut, float branchDistance)
    {
        if (!onShortcut)
        {
            return branchDistance;
        }
        float span = Main.WrapDistance(ShortcutMergeDistance - ShortcutForkDistance);
        float t = Shortcut.Length > 0f ? Mathf.Clamp01(branchDistance / Shortcut.Length) : 0f;
        return Main.WrapDistance(ShortcutForkDistance + span * t);
    }

    /// <summary>True when the main sample at distance <paramref name="s"/> lies on the outer road the shortcut bypasses.</summary>
    public static bool IsOnBypassedSpan(float s)
    {
        float span = Main.WrapDistance(ShortcutMergeDistance - ShortcutForkDistance);
        return Main.WrapDistance(s - ShortcutForkDistance) <= span;
    }

    /// <summary>Distance (m) of a world point to the nearest drivable surface edge; negative inside.</summary>
    public static float SignedDistanceToRoad(Vector3 point)
    {
        float best = float.MaxValue;
        EvaluateRoadDistance(Main, point, ref best);
        EvaluateRoadDistance(Shortcut, point, ref best);
        return best;
    }

    private static void EvaluateRoadDistance(GymRaceTrackPath path, Vector3 point, ref float best)
    {
        float s = path.ClosestDistance(point);
        path.Sample(s, out Vector3 position, out _, out _, out float halfWidth);
        Vector2 flat = new Vector2(point.x - position.x, point.z - position.z);
        float distance = flat.magnitude - halfWidth;
        if (distance < best)
        {
            best = distance;
        }
    }
}
