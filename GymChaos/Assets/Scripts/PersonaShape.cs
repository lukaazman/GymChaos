using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// uGUI quad with individually offset corners, used for the Persona-style
/// slanted plates of the start menu. Corner offsets are fractions of the rect
/// size, so the shape scales with the canvas. An optional tiled texture
/// (halftone dots) is mapped in rect space instead of being stretched.
/// </summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class PersonaShape : MaskableGraphic
{
    // Order: bottom-left, top-left, top-right, bottom-right.
    [SerializeField] private Vector2[] cornerOffsets = new Vector2[4];
    [SerializeField] private Texture pattern;
    [SerializeField] private float patternTileSize = 24f;

    public override Texture mainTexture => pattern != null ? pattern : s_WhiteTexture;

    /// <summary>True when any corner is displaced, i.e. the quad is not an upright rectangle.</summary>
    public bool HasCornerOffsets
    {
        get
        {
            if (cornerOffsets == null) return false;
            for (int i = 0; i < cornerOffsets.Length; i++)
            {
                if (cornerOffsets[i] != Vector2.zero) return true;
            }
            return false;
        }
    }

    public void SetCorners(Vector2 bottomLeft, Vector2 topLeft, Vector2 topRight, Vector2 bottomRight)
    {
        cornerOffsets = new[] { bottomLeft, topLeft, topRight, bottomRight };
        SetVerticesDirty();
    }

    // Shifts the top edge sideways by a fraction of the width: a parallelogram.
    public void SetSlant(float topShift)
    {
        SetCorners(Vector2.zero, new Vector2(topShift, 0f),
            new Vector2(topShift, 0f), Vector2.zero);
    }

    public void SetPattern(Texture texture, float tileSize)
    {
        pattern = texture;
        patternTileSize = Mathf.Max(1f, tileSize);
        SetMaterialDirty();
        SetVerticesDirty();
    }

    // Width in screen pixels of the transparent fringe that anti-aliases slanted edges.
    public const float EdgeFeatherPixels = 1.25f;

    /// <summary>True when the last mesh build added the anti-aliasing fringe.</summary>
    public bool EdgeFeathered { get; private set; }

#if UNITY_EDITOR
    // Before/after captures only: builds plates without the fringe.
    internal static bool FringeOffForComparison;
#endif

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        Rect rect = GetPixelAdjustedRect();
        Vector2[] corners =
        {
            new Vector2(rect.xMin, rect.yMin),
            new Vector2(rect.xMin, rect.yMax),
            new Vector2(rect.xMax, rect.yMax),
            new Vector2(rect.xMax, rect.yMin)
        };
        for (int i = 0; i < 4; i++)
        {
            Vector2 offset = cornerOffsets != null && i < cornerOffsets.Length
                ? cornerOffsets[i] : Vector2.zero;
            corners[i] += Vector2.Scale(offset, rect.size);
        }

        // Overlay UI gets no camera AA: slanted or rotated edges get a fringe
        // that fades to transparent over ~1 px. Upright plates stay crisp.
        Canvas owner = canvas;
        EdgeFeathered = owner != null && (HasCornerOffsets ||
            Quaternion.Angle(transform.rotation, owner.transform.rotation) > 0.05f);
#if UNITY_EDITOR
        if (FringeOffForComparison) EdgeFeathered = false;
#endif
        float feather = EdgeFeathered
            ? EdgeFeatherPixels / Mathf.Max(0.01f, owner.scaleFactor * transform.lossyScale.x /
                Mathf.Max(0.0001f, owner.transform.lossyScale.x))
            : 0f;
        Color32 clear = color;
        clear.a = 0;
        for (int ring = 0; ring < (EdgeFeathered ? 2 : 1); ring++)
        {
            for (int i = 0; i < 4; i++)
            {
                Vector2 position = corners[i];
                if (EdgeFeathered)
                {
                    position += Miter(corners[(i + 3) % 4], corners[i], corners[(i + 1) % 4]) *
                        (ring == 0 ? -0.5f : 0.5f) * feather;
                }
                Vector2 uv = pattern != null
                    ? position / patternTileSize
                    : new Vector2(i >= 2 ? 1f : 0f, i == 1 || i == 2 ? 1f : 0f);
                vh.AddVert(position, ring == 0 ? (Color32)color : clear, uv);
            }
        }
        vh.AddTriangle(0, 1, 2);
        vh.AddTriangle(2, 3, 0);
        if (!EdgeFeathered) return;
        for (int i = 0; i < 4; i++)
        {
            int next = (i + 1) % 4;
            vh.AddTriangle(i, i + 4, next + 4);
            vh.AddTriangle(next + 4, next, i);
        }
    }

    // Outward miter at b for the clockwise outline a -> b -> c (y up), capped for sharp corners.
    private static Vector2 Miter(Vector2 a, Vector2 b, Vector2 c)
    {
        Vector2 d1 = (b - a).normalized;
        Vector2 d2 = (c - b).normalized;
        Vector2 n1 = new Vector2(-d1.y, d1.x);
        Vector2 n2 = new Vector2(-d2.y, d2.x);
        Vector2 sum = n1 + n2;
        if (sum.sqrMagnitude < 1e-6f) return n1;
        Vector2 direction = sum.normalized;
        return direction / Mathf.Max(0.35f, Vector2.Dot(direction, n1));
    }
}
