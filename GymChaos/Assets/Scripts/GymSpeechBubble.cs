using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// A speech bubble as one shape: a rectangle with a curved tail hanging from its bottom edge, a fill and an
/// even outline all the way round. The graphic's own colour is the fill. The rect holds the box and the tail
/// (tail height at the bottom); the tip sits at <see cref="TipFraction"/> of the width.
/// </summary>
public sealed class GymSpeechBubble : MaskableGraphic
{
    private const int CurveSteps = 8;

    public Color OutlineColor = Color.white;
    public float Stroke = 4f;
    public float TailHeight = 34f;
    /// <summary>Where the tail's tip sits along the width (the tail leans toward it from its base).</summary>
    [Range(0.05f, 0.95f)] public float TipFraction = 0.72f;

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        Rect rect = GetPixelAdjustedRect();
        float w = rect.width;
        float top = rect.yMax;
        float boxBottom = rect.yMin + TailHeight;
        float tipX = rect.xMin + Mathf.Clamp01(TipFraction) * w;
        Vector2 tip = new Vector2(tipX, rect.yMin);
        Vector2 baseLeft = new Vector2(Mathf.Min(tipX + 0.05f * w, rect.xMax - 0.06f * w), boxBottom);
        Vector2 baseRight = new Vector2(Mathf.Min(tipX + 0.19f * w, rect.xMax - 0.02f * w), boxBottom);

        // Box.
        Quad(vh, new Vector2(rect.xMin, boxBottom), new Vector2(rect.xMin, top), new Vector2(rect.xMax, top), new Vector2(rect.xMax, boxBottom), color);

        // Tail: the right edge falls steeply then sweeps to the tip, the left edge is a gentle curve back up.
        System.Collections.Generic.List<Vector2> tail = new System.Collections.Generic.List<Vector2>();
        for (int i = 0; i <= CurveSteps; i++)
            tail.Add(Bezier(baseRight, new Vector2(baseRight.x, boxBottom - 0.75f * TailHeight), tip, i / (float)CurveSteps));
        for (int i = 1; i <= CurveSteps; i++)
            tail.Add(Bezier(tip, new Vector2(baseLeft.x - 0.02f * w, boxBottom - 0.45f * TailHeight), baseLeft, i / (float)CurveSteps));
        Vector2 centre = (baseLeft + baseRight) * 0.5f;
        for (int i = 0; i < tail.Count - 1; i++) Triangle(vh, centre, tail[i], tail[i + 1], color);

        // Outline: one closed path (the tail is part of the bottom edge), every segment a quad of equal width,
        // a square at every corner so the joins are filled.
        System.Collections.Generic.List<Vector2> path = new System.Collections.Generic.List<Vector2>
        {
            new Vector2(rect.xMin, boxBottom), new Vector2(rect.xMin, top), new Vector2(rect.xMax, top), new Vector2(rect.xMax, boxBottom)
        };
        path.AddRange(tail);
        path.Add(new Vector2(rect.xMin, boxBottom));
        for (int i = 0; i < path.Count - 1; i++) Line(vh, path[i], path[i + 1], Stroke, OutlineColor);
        for (int i = 0; i < path.Count; i++) Dot(vh, path[i], Stroke, OutlineColor);
    }

    private static Vector2 Bezier(Vector2 a, Vector2 control, Vector2 b, float t)
    {
        float u = 1f - t;
        return u * u * a + 2f * u * t * control + t * t * b;
    }

    private static void Quad(VertexHelper vh, Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color32 tint)
    {
        int start = vh.currentVertCount;
        vh.AddVert(a, tint, Vector4.zero);
        vh.AddVert(b, tint, Vector4.zero);
        vh.AddVert(c, tint, Vector4.zero);
        vh.AddVert(d, tint, Vector4.zero);
        vh.AddTriangle(start, start + 1, start + 2);
        vh.AddTriangle(start, start + 2, start + 3);
    }

    private static void Triangle(VertexHelper vh, Vector2 a, Vector2 b, Vector2 c, Color32 tint)
    {
        int start = vh.currentVertCount;
        vh.AddVert(a, tint, Vector4.zero);
        vh.AddVert(b, tint, Vector4.zero);
        vh.AddVert(c, tint, Vector4.zero);
        vh.AddTriangle(start, start + 1, start + 2);
        // Double-sided: the fan's winding follows the curve direction.
        vh.AddTriangle(start, start + 2, start + 1);
    }

    private static void Line(VertexHelper vh, Vector2 a, Vector2 b, float width, Color32 tint)
    {
        Vector2 direction = b - a;
        if (direction.sqrMagnitude < 1e-6f) return;
        Vector2 side = new Vector2(-direction.y, direction.x).normalized * (width * 0.5f);
        Quad(vh, a - side, a + side, b + side, b - side, tint);
        // Reverse winding too, so the quad shows whichever way the path runs.
        Quad(vh, a - side, b - side, b + side, a + side, tint);
    }

    private static void Dot(VertexHelper vh, Vector2 at, float size, Color32 tint)
    {
        float half = size * 0.5f;
        Quad(vh, at + new Vector2(-half, -half), at + new Vector2(-half, half), at + new Vector2(half, half), at + new Vector2(half, -half), tint);
    }
}
