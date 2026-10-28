using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// One place for the runtime-built URP materials. Shaders are looked up once.
/// <see cref="Lit"/> returns a shared material per
/// (name, colour, metallic, smoothness, emission) key, so identical props share
/// one instance and can batch or instance. Shared materials must not be changed
/// by the caller; use <see cref="NewLit"/> for a
/// material that gets its own texture, keywords or per-instance values.
/// Every material has GPU instancing on.
/// </summary>
public static class GymMaterialLibrary
{
    private readonly struct Key : System.IEquatable<Key>
    {
        private readonly string name;
        private readonly Color color;
        private readonly Color emission;
        private readonly float metallic;
        private readonly float smoothness;

        public Key(string name, Color color, float metallic, float smoothness, Color emission)
        {
            this.name = name;
            this.color = color;
            this.metallic = metallic;
            this.smoothness = smoothness;
            this.emission = emission;
        }

        // Color == is approximate; compare exactly so Equals agrees with GetHashCode.
        public bool Equals(Key other) =>
            name == other.name && color.Equals(other.color) && emission.Equals(other.emission) &&
            metallic == other.metallic && smoothness == other.smoothness;

        public override bool Equals(object obj) => obj is Key other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = name != null ? name.GetHashCode() : 0;
                hash = hash * 31 + color.GetHashCode();
                hash = hash * 31 + emission.GetHashCode();
                hash = hash * 31 + metallic.GetHashCode();
                return hash * 31 + smoothness.GetHashCode();
            }
        }
    }

    private static readonly Dictionary<Key, Material> shared = new Dictionary<Key, Material>();
    private static Shader litShader;
    private static Shader unlitShader;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        shared.Clear();
    }

    /// <summary>URP Lit (Standard outside URP).</summary>
    public static Shader LitShader
    {
        get
        {
            if (litShader == null)
                litShader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            return litShader;
        }
    }

    /// <summary>URP Unlit (Unlit/Color outside URP).</summary>
    public static Shader UnlitShader
    {
        get
        {
            if (unlitShader == null)
                unlitShader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
            return unlitShader;
        }
    }

    /// <summary>Shared lit material. Do not modify it; emission black means none.</summary>
    public static Material Lit(string name, Color color, float metallic = 0f, float smoothness = 0.5f, Color emission = default)
    {
        Key key = new Key(name, color, metallic, smoothness, emission);
        if (shared.TryGetValue(key, out Material material) && material != null) return material;
        material = NewLit(name, color, metallic, smoothness);
        if (emission != default) SetEmission(material, emission);
        shared[key] = material;
        return material;
    }

    /// <summary>New lit material the caller owns and may change.</summary>
    public static Material NewLit(string name, Color color, float metallic = 0f, float smoothness = 0.5f)
    {
        Material material = new Material(LitShader) { name = name, color = color, enableInstancing = true };
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
        if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", metallic);
        if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", smoothness);
        return material;
    }

    /// <summary>Switches a material the caller owns to URP alpha-blended transparency.</summary>
    public static Material MakeTransparent(Material material)
    {
        material.SetFloat("_Surface", 1f);
        material.SetFloat("_Blend", 0f);
        material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        material.SetFloat("_ZWrite", 0f);
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.DisableKeyword("_ALPHATEST_ON");
        material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        return material;
    }

    public static void SetEmission(Material material, Color emission)
    {
        material.EnableKeyword("_EMISSION");
        material.SetColor("_EmissionColor", emission);
    }
}
