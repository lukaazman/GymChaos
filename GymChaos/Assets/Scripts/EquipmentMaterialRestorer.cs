using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

/// <summary>
/// Restores Blender-authored equipment materials while keeping the original Unity FBX
/// geometry, hierarchy, scale, and scene transforms untouched.
/// </summary>
public static class EquipmentMaterialRestorer
{
    private const string ManifestResourcePath = "EquipmentMaterials/equipment-materials";

    private static readonly Regex BlenderNumericSuffix = new Regex(@"\.\d{3}$", RegexOptions.Compiled);
    private static readonly Dictionary<string, Material> MaterialCache = new Dictionary<string, Material>();
    private static Dictionary<string, EquipmentMaterialDefinition> definitions;

    public static int ApplyToScene()
    {
        EnsureDefinitions();
        if (definitions == null || definitions.Count == 0)
        {
            Debug.LogWarning("Equipment material manifest could not be loaded.");
            return 0;
        }

        int changedSlots = 0;
        Renderer[] renderers = UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None);
        for (int rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
        {
            Renderer renderer = renderers[rendererIndex];
            if (renderer == null || renderer is ParticleSystemRenderer)
            {
                continue;
            }

            Material[] materials = renderer.sharedMaterials;
            bool changed = false;
            for (int materialIndex = 0; materialIndex < materials.Length; materialIndex++)
            {
                Material source = materials[materialIndex];
                if (source == null)
                {
                    continue;
                }

                string normalizedName = NormalizeName(source.name);
                if (!definitions.TryGetValue(normalizedName, out EquipmentMaterialDefinition definition))
                {
                    continue;
                }

                materials[materialIndex] = GetOrCreateMaterial(definition);
                changed = true;
                changedSlots++;
            }

            if (changed)
            {
                renderer.sharedMaterials = materials;
            }
        }

        Debug.Log($"Restored Blender equipment materials on {changedSlots} renderer slots.");
        return changedSlots;
    }

    private static void EnsureDefinitions()
    {
        if (definitions != null)
        {
            return;
        }

        definitions = new Dictionary<string, EquipmentMaterialDefinition>();
        TextAsset manifestAsset = Resources.Load<TextAsset>(ManifestResourcePath);
        if (manifestAsset == null)
        {
            return;
        }

        EquipmentMaterialManifest manifest = JsonUtility.FromJson<EquipmentMaterialManifest>(manifestAsset.text);
        if (manifest == null || manifest.materials == null)
        {
            return;
        }

        for (int i = 0; i < manifest.materials.Length; i++)
        {
            EquipmentMaterialDefinition definition = manifest.materials[i];
            if (definition == null || string.IsNullOrWhiteSpace(definition.name))
            {
                continue;
            }

            definitions[NormalizeName(definition.name)] = definition;
            if (definition.aliases == null)
            {
                continue;
            }

            for (int aliasIndex = 0; aliasIndex < definition.aliases.Length; aliasIndex++)
            {
                definitions[NormalizeName(definition.aliases[aliasIndex])] = definition;
            }
        }
    }

    private static Material GetOrCreateMaterial(EquipmentMaterialDefinition definition)
    {
        string cacheKey = NormalizeName(definition.name);
        if (MaterialCache.TryGetValue(cacheKey, out Material cached) && cached != null)
        {
            return cached;
        }

        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
        {
            shader = Shader.Find("Standard");
        }

        Material material = new Material(shader)
        {
            name = $"Blender {definition.name}"
        };

        Texture2D albedo = LoadTexture(definition.albedo);
        Color baseColor = DefinitionColor(definition, albedo != null ? Color.white : new Color(0.8f, 0.8f, 0.8f, 1f));
        material.SetColor("_BaseColor", baseColor);
        material.SetColor("_Color", baseColor);
        if (albedo != null)
        {
            material.SetTexture("_BaseMap", albedo);
            material.SetTexture("_MainTex", albedo);
        }

        Texture2D normal = LoadTexture(definition.normal);
        if (normal != null)
        {
            material.SetTexture("_BumpMap", normal);
            material.SetFloat("_BumpScale", 1f);
            material.EnableKeyword("_NORMALMAP");
        }

        Texture2D metallicSmoothness = LoadTexture(definition.metallicSmoothness);
        if (metallicSmoothness != null)
        {
            material.SetTexture("_MetallicGlossMap", metallicSmoothness);
            material.SetFloat("_Metallic", 1f);
            material.SetFloat("_Smoothness", 1f);
            material.SetFloat("_SmoothnessTextureChannel", 0f);
            material.EnableKeyword("_METALLICSPECGLOSSMAP");
        }
        else
        {
            material.SetFloat("_Metallic", Mathf.Clamp01(definition.metallic));
            material.SetFloat("_Smoothness", Mathf.Clamp01(definition.smoothness));
        }

        material.SetFloat("_Surface", 0f);
        material.SetFloat("_Cull", 2f);
        material.renderQueue = -1;
        MaterialCache[cacheKey] = material;
        return material;
    }

    private static Texture2D LoadTexture(string resourcePath)
    {
        return string.IsNullOrWhiteSpace(resourcePath) ? null : Resources.Load<Texture2D>(resourcePath);
    }

    private static Color DefinitionColor(EquipmentMaterialDefinition definition, Color fallback)
    {
        if (definition.baseColor == null || definition.baseColor.Length < 3)
        {
            return fallback;
        }

        return new Color(
            definition.baseColor[0],
            definition.baseColor[1],
            definition.baseColor[2],
            definition.baseColor.Length > 3 ? definition.baseColor[3] : 1f);
    }

    private static string NormalizeName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        string normalized = value.Trim();
        if (normalized.EndsWith(" (Instance)", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized.Substring(0, normalized.Length - " (Instance)".Length);
        }

        normalized = BlenderNumericSuffix.Replace(normalized, string.Empty);
        return Regex.Replace(normalized.Trim().ToLowerInvariant(), @"\s+", " ");
    }

    [Serializable]
    private sealed class EquipmentMaterialManifest
    {
        public EquipmentMaterialDefinition[] materials;
    }

    [Serializable]
    private sealed class EquipmentMaterialDefinition
    {
        public string name;
        public string[] aliases;
        public float[] baseColor;
        public float metallic;
        public float smoothness;
        public string albedo;
        public string normal;
        public string metallicSmoothness;
    }
}
