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
            Vector2 position = corners[i] + Vector2.Scale(offset, rect.size);
            Vector2 uv = pattern != null
                ? position / patternTileSize
                : new Vector2(i >= 2 ? 1f : 0f, i == 1 || i == 2 ? 1f : 0f);
            vh.AddVert(position, color, uv);
        }
        vh.AddTriangle(0, 1, 2);
        vh.AddTriangle(2, 3, 0);
    }
}
