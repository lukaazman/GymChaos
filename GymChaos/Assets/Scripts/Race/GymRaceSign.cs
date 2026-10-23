using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Neon "RACE" sign in the north-east corner of the parking lot, beside the
/// mouth of the visitor road. Pressing F next to it starts the race.
/// </summary>
public sealed class GymRaceSign : MonoBehaviour
{
    public const float InteractionRadius = 4.2f;
    private const float PanelHeight = 2.7f;
    // Free face between the checkered strips, split into a title band and a
    // subtitle band so the two lines never touch.
    private const float TextMaxWidth = 2.0f;
    private const float TitleCenterY = 0.17f;
    private const float TitleMaxHeight = 0.32f;
    private const float SubCenterY = -0.23f;
    private const float SubMaxHeight = 0.13f;

    public static GymRaceSign Instance { get; private set; }
    public Vector3 StandPoint { get; private set; }

    private Material neon;
    private Material[] ownedMaterials = new Material[0];
    private float phase;
    private TextMesh titleLabel;
    private TextMesh subLabel;
    private bool labelsFitted;

    public static bool IsNear(Vector3 position)
    {
        if (Instance == null || !Instance.isActiveAndEnabled)
        {
            return false;
        }
        Vector3 offset = position - Instance.transform.position;
        offset.y = 0f;
        return offset.sqrMagnitude <= InteractionRadius * InteractionRadius;
    }

    public static GymRaceSign Create()
    {
        if (Instance != null || !GymOutdoorBuilder.IsBuilt)
        {
            return Instance;
        }
        Bounds parking = GymOutdoorBuilder.ParkingBounds;
        float roadHalf = GymOutdoorBuilder.VehicleRoadWidthForVerification * 0.5f;
        Vector3 basePosition = new Vector3(
            parking.max.x + 0.3f,
            GymRaceTrackLayout.FloorY,
            GymOutdoorBuilder.TrafficLaneCenterZ + roadHalf + 0.75f);
        GameObject root = new GameObject("Race Sign (Runtime)");
        root.transform.position = basePosition;
        // Face the lot and the road mouth (south-west).
        root.transform.rotation = Quaternion.Euler(0f, 45f, 0f);
        GymRaceSign sign = root.AddComponent<GymRaceSign>();
        // Set first: a failed build must not respawn the sign every frame.
        Instance = sign;
        sign.StandPoint = basePosition + new Vector3(-1.6f, 0f, -2f);
        sign.Build();
        return sign;
    }

    private void Build()
    {
        Shader lit = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        Shader unlit = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
        Material metal = new Material(lit) { name = "Race Sign Metal", color = new Color(0.08f, 0.09f, 0.1f) };
        Material face = new Material(lit) { name = "Race Sign Face", color = new Color(0.03f, 0.03f, 0.05f) };
        Material white = new Material(unlit) { name = "Race Sign White", color = new Color(0.95f, 0.95f, 0.95f) };
        Material black = new Material(unlit) { name = "Race Sign Black", color = new Color(0.02f, 0.02f, 0.02f) };
        neon = new Material(unlit) { name = "Race Sign Neon", color = new Color(1f, 0.2f, 0.7f) };
        ownedMaterials = new[] { metal, face, white, black, neon };
        foreach (Material m in ownedMaterials)
        {
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", m.color);
        }

        Part("Post", PrimitiveType.Cylinder, new Vector3(0f, PanelHeight * 0.5f, 0.12f), new Vector3(0.14f, PanelHeight * 0.5f, 0.14f), metal, true);
        Part("Panel", PrimitiveType.Cube, new Vector3(0f, PanelHeight, 0f), new Vector3(2.3f, 1.15f, 0.12f), face, false);
        // Checkered strip along the top and bottom edges.
        for (int row = 0; row < 2; row++)
        {
            for (int i = 0; i < 12; i++)
            {
                float x = -1.1f + (i + 0.5f) * (2.2f / 12f);
                float y = PanelHeight + (row == 0 ? 0.47f : -0.47f);
                Part("Checker", PrimitiveType.Cube, new Vector3(x, y, -0.065f), new Vector3(2.2f / 12f, 0.13f, 0.01f),
                    (i + row) % 2 == 0 ? white : black, false);
            }
        }
        // Neon frame.
        Part("Neon Left", PrimitiveType.Cube, new Vector3(-1.16f, PanelHeight, -0.07f), new Vector3(0.05f, 1.1f, 0.03f), neon, false);
        Part("Neon Right", PrimitiveType.Cube, new Vector3(1.16f, PanelHeight, -0.07f), new Vector3(0.05f, 1.1f, 0.03f), neon, false);

        GameObject labelObject = new GameObject("Race Sign Label");
        labelObject.transform.SetParent(transform, false);
        labelObject.transform.localPosition = new Vector3(0f, PanelHeight + TitleCenterY, -0.075f);
        TextMesh label = labelObject.AddComponent<TextMesh>();
        titleLabel = label;
        Font font = PersonaMenuStyle.LoadTitleFont(PersonaMenuStyle.BuiltinFont);
        label.font = font;
        label.text = "RACE";
        label.characterSize = 0.1f;
        label.fontSize = 64;
        label.anchor = TextAnchor.MiddleCenter;
        label.alignment = TextAlignment.Center;
        label.color = new Color(1f, 0.85f, 0.3f);
        MeshRenderer textRenderer = labelObject.GetComponent<MeshRenderer>();
        textRenderer.sharedMaterial = font.material;
        textRenderer.shadowCastingMode = ShadowCastingMode.Off;

        GameObject sub = new GameObject("Race Sign Sub");
        sub.transform.SetParent(transform, false);
        sub.transform.localPosition = new Vector3(0f, PanelHeight + SubCenterY, -0.075f);
        subLabel = sub.AddComponent<TextMesh>();
        subLabel.font = PersonaMenuStyle.LoadButtonFont(PersonaMenuStyle.BuiltinFont);
        subLabel.text = "3 LAPS AROUND THE CITY";
        subLabel.characterSize = 0.03f;
        subLabel.fontSize = 64;
        subLabel.anchor = TextAnchor.MiddleCenter;
        subLabel.alignment = TextAlignment.Center;
        subLabel.color = new Color(0.8f, 0.9f, 1f);
        MeshRenderer subRenderer = sub.GetComponent<MeshRenderer>();
        subRenderer.sharedMaterial = subLabel.font.material;
        subRenderer.shadowCastingMode = ShadowCastingMode.Off;
    }

    private void Part(string name, PrimitiveType type, Vector3 localPosition, Vector3 scale, Material material, bool collider)
    {
        GameObject part = GameObject.CreatePrimitive(type);
        part.name = "Race Sign " + name;
        part.transform.SetParent(transform, false);
        part.transform.localPosition = localPosition;
        part.transform.localScale = scale;
        if (!collider)
        {
            Destroy(part.GetComponent<Collider>());
        }
        MeshRenderer renderer = part.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
    }

    /// <summary>Shrinks a label to fit its band; true once its glyphs fit without a change.</summary>
    private static bool FitLabel(TextMesh label, float maxWidth, float maxHeight)
    {
        if (!TryMeasure(label, out Bounds glyphs))
        {
            return false;
        }
        Vector3 size = glyphs.size;
        float scale = Mathf.Min(1f, maxWidth / size.x, maxHeight / size.y);
        if (scale < 0.999f)
        {
            // Re-measured next frame from the rebuilt glyph mesh.
            label.characterSize *= scale * 0.98f;
            return false;
        }
        return true;
    }

    /// <summary>Verification: title and subtitle glyph boxes in sign space (x = width, y = min, z = max).</summary>
    public bool TryGetLabelExtents(out Vector3 title, out Vector3 subtitle)
    {
        title = LabelExtent(titleLabel);
        subtitle = LabelExtent(subLabel);
        return labelsFitted && title.x > 0f && subtitle.x > 0f;
    }

    /// <summary>
    /// Glyph box in label space: the generated mesh when it exists, else the
    /// font metrics (TextMesh maps one font pixel to characterSize / 10 m).
    /// </summary>
    private static bool TryMeasure(TextMesh label, out Bounds glyphs)
    {
        glyphs = default;
        if (label == null)
        {
            return false;
        }
        MeshFilter filter = label.GetComponent<MeshFilter>();
        Mesh mesh = filter != null ? filter.sharedMesh : null;
        if (mesh != null && mesh.vertexCount > 0 && mesh.bounds.size.x > 0f)
        {
            glyphs = mesh.bounds;
            return true;
        }
        Font font = label.font;
        if (font == null || string.IsNullOrEmpty(label.text))
        {
            return false;
        }
        font.RequestCharactersInTexture(label.text, label.fontSize, label.fontStyle);
        float width = 0f;
        float minY = float.MaxValue;
        float maxY = float.MinValue;
        foreach (char c in label.text)
        {
            if (!font.GetCharacterInfo(c, out CharacterInfo info, label.fontSize, label.fontStyle))
            {
                continue;
            }
            width += info.advance;
            if (c != ' ')
            {
                minY = Mathf.Min(minY, info.minY);
                maxY = Mathf.Max(maxY, info.maxY);
            }
        }
        if (width <= 0f || maxY <= minY)
        {
            return false;
        }
        float scale = label.characterSize * 0.1f;
        float height = (maxY - minY) * scale;
        // MiddleCenter anchor: the glyph block is centred on the label.
        glyphs = new Bounds(Vector3.zero, new Vector3(width * scale, height, 0f));
        return true;
    }

    private static Vector3 LabelExtent(TextMesh label)
    {
        if (!TryMeasure(label, out Bounds bounds))
        {
            return Vector3.zero;
        }
        float y = label.transform.localPosition.y - PanelHeight;
        return new Vector3(bounds.size.x, y + bounds.min.y, y + bounds.max.y);
    }

    private void Update()
    {
        if (!labelsFitted)
        {
            // Glyph meshes exist only after the first text build.
            bool title = FitLabel(titleLabel, TextMaxWidth, TitleMaxHeight);
            bool sub = FitLabel(subLabel, TextMaxWidth, SubMaxHeight);
            labelsFitted = title && sub;
        }
        // Slow neon breathing so the sign reads as interactive.
        phase += Time.deltaTime;
        float pulse = 0.75f + 0.25f * Mathf.Sin(phase * 3f);
        Color color = new Color(1f, 0.2f, 0.7f) * pulse;
        neon.color = color;
        if (neon.HasProperty("_BaseColor")) neon.SetColor("_BaseColor", color);
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
        foreach (Material material in ownedMaterials)
        {
            if (material != null)
            {
                Destroy(material);
            }
        }
    }
}
