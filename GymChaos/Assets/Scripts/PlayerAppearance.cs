using System.Collections.Generic;
using System.Text;
using UnityEngine;

/// <summary>
/// The player's name and look (GymCharacterSave fields) and how they reach a
/// model. The remade player (Tools/build_remake_player.py) carries the shape
/// keys Female, Upper, Lower and Soft on every mesh; they are resolved once
/// into a plain mesh, because cloth shells, first-person arms and the mirror
/// copy the body's vertices and never read blend shapes. Hair_1..5 and
/// Beard_1..3 are toggled parts; the outfit parts (Tank, Onyx, Oversized,
/// Shorts, Pants) belong to the cosmetics and replace the scan's cloth shells.
/// The DEFAULT scan body takes only the height and the name.
/// </summary>
public static class PlayerAppearance
{
    public const int MinHeight = 160;
    public const int MaxHeight = 200;
    public const int DefaultMaleHeight = 180;
    public const int DefaultFemaleHeight = 168;
    public const int NameMin = 3;
    public const int NameMax = 16;
    public const string RemakeModel = "Characters/Remake/player_authored";
    public const string RemakeTexture = "Characters/Remake/Textures/player";
    public const string BodyMeshName = "geometry_ORIGINAL";
    public static readonly string[] HairNames = { "Bald", "Buzz", "Crop", "Curls", "Quiff", "Long" };
    public static readonly string[] FacialHairNames = { "None", "Stubble", "Moustache", "Full beard" };
    // The height every model is fitted to (StandardGameplayHeight) stands for this.
    private const float ReferenceHeight = DefaultMaleHeight;

    // Resolved meshes by source and quantized weights; few combinations, kept for reuse.
    private static readonly Dictionary<string, Mesh> resolved = new Dictionary<string, Mesh>();
    private static readonly Dictionary<Mesh, Mesh> sourceOf = new Dictionary<Mesh, Mesh>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        resolved.Clear();
        sourceOf.Clear();
    }

    /// <summary>The POTATO player body is retired: every run is the DEFAULT class scan.</summary>
    public static bool UsesRemakeBody => false;

    public static float HeightScale(GymCharacterSave look) =>
        look == null ? 1f : Mathf.Clamp(look.heightCm, MinHeight, MaxHeight) / ReferenceHeight;

    public static int DefaultHeight(int gender) => gender == 1 ? DefaultFemaleHeight : DefaultMaleHeight;

    public static string DisplayName(GymCharacterSave save, string fallback) =>
        save != null && save.IsCustomized ? save.username : fallback;

    /// <summary>Dialogue lines: {name} becomes the active character's name ("champ" without one).</summary>
    public static string FillName(string text)
    {
        if (string.IsNullOrEmpty(text) || text.IndexOf("{name}", System.StringComparison.Ordinal) < 0) return text;
        GymCharacterSave active = GymSessionService.Active;
        return text.Replace("{name}", active != null && active.IsCustomized ? active.username : "champ");
    }

    /// <summary>The active character's name in capitals plus the separator, or empty (HUD, level-up, journal).</summary>
    public static string NamePrefix(string separator)
    {
        GymCharacterSave active = GymSessionService.Active;
        return active != null && active.IsCustomized ? active.username.ToUpperInvariant() + separator : string.Empty;
    }

    /// <summary>
    /// Trimmed name of letters, digits, spaces, '_', '-' and '.', at most
    /// NameMax characters, or null when shorter than NameMin. Hosts run it on
    /// every name a guest sends.
    /// </summary>
    public static string SanitizeName(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return null;
        var builder = new StringBuilder(NameMax);
        bool space = false;
        foreach (char c in raw)
        {
            if (builder.Length >= NameMax) break;
            if (char.IsWhiteSpace(c))
            {
                space = builder.Length > 0;
                continue;
            }
            // ASCII only: the nametag and menu fonts have no other glyphs.
            bool ascii = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9');
            if (!ascii && c != '_' && c != '-' && c != '.') continue;
            if (space && builder.Length < NameMax - 1) builder.Append(' ');
            space = false;
            builder.Append(c);
        }
        return builder.Length >= NameMin ? builder.ToString() : null;
    }

    /// <summary>Clamps every look field into range; a bad name becomes empty (customize again).</summary>
    public static void Normalize(GymCharacterSave save)
    {
        save.username = SanitizeName(save.username) ?? string.Empty;
        save.gender = Mathf.Clamp(save.gender, 0, 1);
        save.heightCm = save.heightCm <= 0 ? DefaultHeight(save.gender) : Mathf.Clamp(save.heightCm, MinHeight, MaxHeight);
        save.skinTone = Mathf.Clamp(save.skinTone, 0, GymPalette.SkinTones.Length - 1);
        save.hair = Mathf.Clamp(save.hair, 0, HairNames.Length - 1);
        save.hairColor = Mathf.Clamp(save.hairColor, 0, GymPalette.HairColors.Length - 1);
        save.facialHair = Mathf.Clamp(save.facialHair, 0, FacialHairNames.Length - 1);
    }

    /// <summary>Shape key weights (0..1) for a class body: bulk 0.5 is the lean base, 1.15 the top.</summary>
    public static Vector4 Weights(GymCharacterSave look, GymClassDefinition definition)
    {
        float upper = definition != null ? definition.upperBulk : 1f;
        float lower = definition != null ? definition.lowerBulk : 1f;
        float soft = definition != null ? definition.softTissue : 0f;
        return new Vector4(look != null && look.gender == 1 ? 1f : 0f,
            Mathf.Clamp01((upper - 0.5f) / 0.65f), Mathf.Clamp01((lower - 0.5f) / 0.65f), Mathf.Clamp01(soft / 0.8f));
    }

    /// <summary>
    /// Body shape, skin, hair and facial hair on an instance of the remade
    /// model. Safe to call again (turntable): meshes resolve from their
    /// source, materials are owned per renderer.
    /// </summary>
    public static void ApplyBody(GameObject modelRoot, GymCharacterSave look, GymClassDefinition definition)
    {
        if (modelRoot == null || look == null) return;
        Vector4 weights = Weights(look, definition);
        Texture2D skinTexture = Resources.Load<Texture2D>(RemakeTexture);
        Color skin = GymPalette.SkinTones[Mathf.Clamp(look.skinTone, 0, GymPalette.SkinTones.Length - 1)];
        Color hair = GymPalette.HairColors[Mathf.Clamp(look.hairColor, 0, GymPalette.HairColors.Length - 1)];
        foreach (SkinnedMeshRenderer renderer in modelRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            string name = renderer.name;
            if (TryPartIndex(name, "Hair_", out int hairIndex))
                renderer.gameObject.SetActive(hairIndex == look.hair);
            else if (TryPartIndex(name, "Beard_", out int beardIndex))
                renderer.gameObject.SetActive(beardIndex == look.facialHair);
            renderer.sharedMesh = Resolve(renderer.sharedMesh, weights);
            Material[] materials = OwnedMaterials(renderer);
            for (int i = 0; i < materials.Length; i++)
            {
                string slot = materials[i].name;
                if (slot.StartsWith("Skin") || slot.StartsWith("Gear"))
                {
                    materials[i].SetTexture("_BaseMap", skinTexture);
                    materials[i].SetColor("_BaseColor", slot.StartsWith("Skin") ? skin : Color.white);
                }
                else if (slot.StartsWith("Hair"))
                {
                    materials[i].SetColor("_BaseColor", hair);
                }
            }
        }
    }

    /// <summary>
    /// The locker outfit on a remade model: one top part (Tank tinted, Onyx
    /// and Oversized wearing their DEFAULT atlas for the colour) and one bottom
    /// part (tinted), and the chosen hair hidden under a hat (Headband keeps it).
    /// </summary>
    public static void SetOutfit(GameObject modelRoot, GymTopStyle top, GymShirtColor shirt, GymBottomStyle bottom,
        GymShirtColor bottomColor, int hair, GymHeadwear headwear)
    {
        if (modelRoot == null) return;
        bool hatOn = headwear != GymHeadwear.None && headwear != GymHeadwear.Headband;
        foreach (SkinnedMeshRenderer renderer in modelRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            string name = renderer.name;
            if (TryPartIndex(name, "Hair_", out int hairIndex))
            {
                renderer.gameObject.SetActive(hairIndex == hair && !hatOn);
                continue;
            }
            bool isTop = name == "Tank" || name == "Onyx" || name == "Oversized";
            if (!isTop && name != "Shorts" && name != "Pants") continue;
            bool worn = isTop ? name == TopPart(top) : name == (bottom == GymBottomStyle.Pants ? "Pants" : "Shorts");
            renderer.gameObject.SetActive(worn);
            if (!worn) continue;
            Texture atlas = name == "Onyx" || name == "Oversized"
                ? Resources.Load<Texture2D>(PlayerCosmeticLoadout.GetOutfitTexturePath(top, shirt)) : null;
            Color color = atlas != null ? Color.white
                : PlayerCosmeticLoadout.GetShirtColor(isTop ? shirt : bottomColor);
            foreach (Material material in renderer.sharedMaterials)
            {
                if (material == null) continue;
                material.SetTexture("_BaseMap", atlas);
                material.SetColor("_BaseColor", color);
            }
        }
    }

    private static string TopPart(GymTopStyle top) =>
        top == GymTopStyle.Onyx ? "Onyx" : top == GymTopStyle.Oversized ? "Oversized" : "Tank";

    /// <summary>Destroys the look materials ApplyBody made for a model about to be destroyed.</summary>
    public static void ReleaseMaterials(GameObject modelRoot)
    {
        if (modelRoot == null) return;
        foreach (SkinnedMeshRenderer renderer in modelRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            foreach (Material material in renderer.sharedMaterials)
                if (material != null && material.name.Contains(" (look)")) GymShared.DestroyRuntimeObject(material);
    }

    public static bool IsPartName(string name) =>
        name == "Tank" || name == "Onyx" || name == "Oversized" || name == "Shorts" || name == "Pants" ||
        name.StartsWith("Hair_") || name.StartsWith("Beard_");

    private static bool TryPartIndex(string name, string prefix, out int index)
    {
        index = 0;
        return name.StartsWith(prefix) && int.TryParse(name.Substring(prefix.Length), out index);
    }

    private static Material[] OwnedMaterials(SkinnedMeshRenderer renderer)
    {
        Material[] materials = renderer.sharedMaterials;
        bool changed = false;
        for (int i = 0; i < materials.Length; i++)
        {
            Material source = materials[i];
            if (source != null && source.name.EndsWith(" (look)")) continue;
            string slot = source != null ? source.name : renderer.name;
            Color color = source != null && source.HasProperty("_BaseColor") ? source.GetColor("_BaseColor") : Color.white;
            materials[i] = GymMaterialLibrary.NewLit(slot + " (look)", color, 0f, 0.25f);
            changed = true;
        }
        if (changed) renderer.sharedMaterials = materials;
        return materials;
    }

    /// <summary>The mesh with its shape keys baked at these weights, without blend shapes.</summary>
    public static Mesh Resolve(Mesh mesh, Vector4 weights)
    {
        if (mesh == null) return null;
        Mesh source = sourceOf.TryGetValue(mesh, out Mesh original) ? original : mesh;
        if (source.blendShapeCount == 0) return source;
        string key = source.GetHashCode() + "|" + Mathf.RoundToInt(weights.x * 100f) + "|" +
            Mathf.RoundToInt(weights.y * 100f) + "|" + Mathf.RoundToInt(weights.z * 100f) + "|" +
            Mathf.RoundToInt(weights.w * 100f);
        if (resolved.TryGetValue(key, out Mesh cached) && cached != null) return cached;

        int count = source.vertexCount;
        Vector3[] vertices = source.vertices;
        Vector3[] normals = source.normals;
        var deltaVertices = new Vector3[count];
        var deltaNormals = new Vector3[count];
        var deltaTangents = new Vector3[count];
        string[] names = { "Female", "Upper", "Lower", "Soft" };
        for (int k = 0; k < names.Length; k++)
        {
            int shape = source.GetBlendShapeIndex(names[k]);
            float w = weights[k];
            if (shape < 0 || w <= 0f) continue;
            source.GetBlendShapeFrameVertices(shape, 0, deltaVertices, deltaNormals, deltaTangents);
            for (int i = 0; i < count; i++)
            {
                vertices[i] += deltaVertices[i] * w;
                if (normals.Length == count) normals[i] += deltaNormals[i] * w;
            }
        }
        Mesh result = Object.Instantiate(source);
        result.name = source.name + " (look)";
        result.ClearBlendShapes();
        result.vertices = vertices;
        if (normals.Length == count)
        {
            for (int i = 0; i < count; i++) normals[i] = normals[i].normalized;
            result.normals = normals;
        }
        result.RecalculateBounds();
        resolved[key] = result;
        sourceOf[result] = source;
        return result;
    }
}
