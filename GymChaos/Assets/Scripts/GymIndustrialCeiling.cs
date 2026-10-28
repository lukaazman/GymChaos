using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Main gym ceiling after the warehouse-gym reference: dark corrugated
/// metal deck, open-web steel trusses with purlins, two round supply ducts
/// and a suspended hexagonal LED grid. Every part is merged into a few meshes
/// (one draw per material) so the detail costs almost nothing at runtime.
/// The spot lights that actually light the room hang at the hex grid.
/// </summary>
public static class GymIndustrialCeiling
{
    public const string RootName = "Industrial Ceiling";
    public const string HexGridName = "Hex LED Grid";
    // Hex cell edge length and the light tube's cross-section.
    public const float HexEdge = 1.25f;
    private const float TubeSize = 0.075f;
    public const float HexDrop = 1.75f;
    private const float TrussDepth = 0.9f;
    private const float TrussSpacing = 4.2f;

    public static int HexSegmentCount { get; private set; }
    public static Bounds HexGridBounds { get; private set; }

    public static void Build(Transform parent, Vector3 center, float width, float depth, float height,
        Material lightMaterial)
    {
        GameObject root = new GameObject(RootName);
        root.transform.SetParent(parent, false);
        Mesh cube = PrimitiveMesh(PrimitiveType.Cube);
        Mesh cylinder = PrimitiveMesh(PrimitiveType.Cylinder);

        Material deck = GymMaterialLibrary.Lit("Corrugated ceiling deck", new Color(0.2f, 0.21f, 0.22f), 0.55f, 0.32f);
        Material steel = GymMaterialLibrary.Lit("Ceiling steel", new Color(0.035f, 0.038f, 0.042f), 0.6f, 0.4f);
        Material duct = GymMaterialLibrary.Lit("Galvanised duct", new Color(0.42f, 0.44f, 0.45f), 0.8f, 0.55f);

        var deckParts = new List<CombineInstance>();
        var steelParts = new List<CombineInstance>();
        var ductParts = new List<CombineInstance>();
        var hexParts = new List<CombineInstance>();

        float underside = height - 0.12f;
        // Corrugated deck: ribs across the room, every 0.32 m.
        for (float z = -depth * 0.5f + 0.16f; z < depth * 0.5f; z += 0.32f)
        {
            AddBox(deckParts, cube, center + new Vector3(0f, underside - 0.035f, z),
                Quaternion.identity, new Vector3(width - 0.4f, 0.07f, 0.13f));
        }

        // Open-web trusses along the depth, with purlins across them.
        float topChord = underside - 0.12f;
        float bottomChord = topChord - TrussDepth;
        int trusses = Mathf.Max(2, Mathf.FloorToInt((width - 2f) / TrussSpacing));
        float trussStart = -(trusses - 1) * TrussSpacing * 0.5f;
        for (int t = 0; t < trusses; t++)
        {
            float x = trussStart + t * TrussSpacing;
            AddBox(steelParts, cube, center + new Vector3(x, topChord, 0f), Quaternion.identity,
                new Vector3(0.14f, 0.14f, depth - 0.4f));
            AddBox(steelParts, cube, center + new Vector3(x, bottomChord, 0f), Quaternion.identity,
                new Vector3(0.12f, 0.12f, depth - 1.2f));
            float panel = 1.2f;
            int panels = Mathf.FloorToInt((depth - 1.4f) / panel);
            float zStart = -panels * panel * 0.5f;
            for (int p = 0; p < panels; p++)
            {
                float z0 = zStart + p * panel;
                float z1 = z0 + panel;
                bool up = (p & 1) == 0;
                Vector3 a = center + new Vector3(x, up ? bottomChord : topChord, z0);
                Vector3 b = center + new Vector3(x, up ? topChord : bottomChord, z1);
                AddBeam(steelParts, cube, a, b, 0.06f);
            }
        }
        for (float z = -depth * 0.5f + 2.5f; z < depth * 0.5f - 1f; z += 3.6f)
        {
            AddBox(steelParts, cube, center + new Vector3(0f, topChord + 0.02f, z), Quaternion.identity,
                new Vector3(width - 0.6f, 0.16f, 0.08f));
        }

        // Two round supply ducts running the width, hung between the chords.
        float ductY = topChord - TrussDepth * 0.45f;
        for (int side = -1; side <= 1; side += 2)
        {
            float z = side * depth * 0.28f;
            Quaternion along = Quaternion.Euler(0f, 0f, 90f);
            ductParts.Add(new CombineInstance
            {
                mesh = cylinder,
                transform = Matrix4x4.TRS(center + new Vector3(0f, ductY, z), along,
                    new Vector3(0.62f, (width - 1.2f) * 0.5f, 0.62f))
            });
            for (float x = -width * 0.5f + 2f; x < width * 0.5f - 1f; x += 3.5f)
            {
                // Seam collars and hangers.
                ductParts.Add(new CombineInstance
                {
                    mesh = cylinder,
                    transform = Matrix4x4.TRS(center + new Vector3(x, ductY, z), along,
                        new Vector3(0.66f, 0.03f, 0.66f))
                });
                AddBox(steelParts, cube, center + new Vector3(x, (ductY + topChord) * 0.5f + 0.16f, z),
                    Quaternion.identity, new Vector3(0.025f, topChord - ductY - 0.3f, 0.025f));
            }
        }

        // Suspended honeycomb of LED tubes (flat-top hexagons).
        float hexY = height - HexDrop;
        float halfW = width * 0.5f - 2.4f;
        float halfD = depth * 0.5f - 2.4f;
        float columnStep = HexEdge * 1.5f;
        float rowStep = HexEdge * Mathf.Sqrt(3f);
        var edges = new HashSet<long>();
        var hangers = new HashSet<long>();
        HexSegmentCount = 0;
        Bounds hexBounds = new Bounds(center + Vector3.up * hexY, Vector3.zero);
        int columns = Mathf.FloorToInt((halfW * 2f - HexEdge * 2f) / columnStep) + 1;
        int rows = Mathf.FloorToInt((halfD * 2f - rowStep * 1.5f) / rowStep) + 1;
        float gridWidth = (columns - 1) * columnStep;
        float gridDepth = (rows - 1) * rowStep + rowStep * 0.5f;
        for (int c = 0; c < columns; c++)
        {
            for (int r = 0; r < rows; r++)
            {
                float cx = -gridWidth * 0.5f + c * columnStep;
                float cz = -gridDepth * 0.5f + r * rowStep + ((c & 1) == 1 ? rowStep * 0.5f : 0f);
                for (int k = 0; k < 6; k++)
                {
                    float a0 = Mathf.Deg2Rad * (60f * k);
                    float a1 = Mathf.Deg2Rad * (60f * (k + 1));
                    Vector2 p0 = new Vector2(cx + HexEdge * Mathf.Cos(a0), cz + HexEdge * Mathf.Sin(a0));
                    Vector2 p1 = new Vector2(cx + HexEdge * Mathf.Cos(a1), cz + HexEdge * Mathf.Sin(a1));
                    Vector2 mid = (p0 + p1) * 0.5f;
                    if (!edges.Add(Key(mid)))
                    {
                        continue;
                    }
                    Vector3 w0 = center + new Vector3(p0.x, hexY, p0.y);
                    Vector3 w1 = center + new Vector3(p1.x, hexY, p1.y);
                    AddBeam(hexParts, cube, w0, w1, TubeSize, HexEdge + TubeSize);
                    hexBounds.Encapsulate(w0);
                    hexBounds.Encapsulate(w1);
                    HexSegmentCount++;
                    // A thin wire hangs every other node from the trusses.
                    if (((c + r + k) & 1) == 0 && hangers.Add(Key(p0)))
                    {
                        AddBox(steelParts, cube,
                            new Vector3(w0.x, (hexY + bottomChord) * 0.5f, w0.z), Quaternion.identity,
                            new Vector3(0.012f, bottomChord - hexY, 0.012f));
                    }
                }
            }
        }
        HexGridBounds = hexBounds;

        CreateCombined(root.transform, "Ceiling Deck", deckParts, deck, true);
        CreateCombined(root.transform, "Ceiling Trusses", steelParts, steel, true);
        CreateCombined(root.transform, "Ceiling Ducts", ductParts, duct, true);
        CreateCombined(root.transform, HexGridName, hexParts, lightMaterial, false);
        Debug.Log($"GYMCHAOS_INDUSTRIAL_CEILING trusses={trusses} hexSegments={HexSegmentCount} " +
            $"grid={hexBounds.size} hexY={hexY:F2}");
    }

    /// <summary>Height the room lights hang at (the hex grid).</summary>
    public static float LightHeight(float height) => height - HexDrop - 0.05f;

    private static long Key(Vector2 point)
    {
        long x = Mathf.RoundToInt(point.x * 100f);
        long z = Mathf.RoundToInt(point.y * 100f);
        return (x << 32) ^ (z & 0xffffffffL);
    }

    private static void AddBox(List<CombineInstance> parts, Mesh cube, Vector3 position, Quaternion rotation,
        Vector3 size)
    {
        parts.Add(new CombineInstance { mesh = cube, transform = Matrix4x4.TRS(position, rotation, size) });
    }

    private static void AddBeam(List<CombineInstance> parts, Mesh cube, Vector3 a, Vector3 b, float thickness,
        float length = -1f)
    {
        Vector3 span = b - a;
        if (span.sqrMagnitude < 1e-6f)
        {
            return;
        }
        Quaternion rotation = Quaternion.LookRotation(span.normalized, Vector3.up);
        if (Mathf.Abs(Vector3.Dot(span.normalized, Vector3.up)) > 0.99f)
        {
            rotation = Quaternion.LookRotation(span.normalized, Vector3.forward);
        }
        float size = length > 0f ? length : span.magnitude;
        AddBox(parts, cube, (a + b) * 0.5f, rotation, new Vector3(thickness, thickness, size));
    }

    private static void CreateCombined(Transform parent, string name, List<CombineInstance> parts,
        Material material, bool castShadows)
    {
        if (parts.Count == 0)
        {
            return;
        }
        Mesh mesh = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
        mesh.CombineMeshes(parts.ToArray(), true, true);
        mesh.RecalculateBounds();
        GameObject holder = new GameObject(name);
        holder.transform.SetParent(parent, false);
        holder.AddComponent<MeshFilter>().sharedMesh = mesh;
        MeshRenderer renderer = holder.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
    }

    private static Mesh PrimitiveMesh(PrimitiveType type)
    {
        GameObject temp = GameObject.CreatePrimitive(type);
        Mesh mesh = temp.GetComponent<MeshFilter>().sharedMesh;
        Object.Destroy(temp);
        return mesh;
    }
}
