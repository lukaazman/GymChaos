using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.Rendering;

/// <summary>
/// Runtime loader for a compact multi-material GLB scene.
///
/// The city backdrop is exported from the authored Blender scene as one mesh
/// with several material primitives.  This loader keeps all primitives and
/// their glTF PBR/emission colors, unlike RuntimeGlbModelLoader which is
/// intentionally limited to the first primitive of a single-mesh prop.
/// </summary>
public sealed class RuntimeGlbSceneLoader : MonoBehaviour
{
    private const string HostName = "Gym Runtime GLB Scene Models";
    private const uint GlbMagic = 0x46546C67;
    private const uint JsonChunk = 0x4E4F534A;
    private const uint BinaryChunk = 0x004E4942;

    private static RuntimeGlbSceneLoader instance;
    private readonly Dictionary<string, RuntimeSceneAsset> cache =
        new Dictionary<string, RuntimeSceneAsset>(StringComparer.OrdinalIgnoreCase);

    private static readonly Dictionary<string, Texture2D> FacadeFallbackTextures =
        new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);

    private readonly HashSet<string> pending =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    private sealed class RuntimeScenePart
    {
        public Mesh Mesh;
        public Material Material;
    }

    private sealed class RuntimeSceneAsset
    {
        public RuntimeScenePart[] Parts;
        public Vector3 LocalPosition;
        public Quaternion LocalRotation;
        public Vector3 LocalScale;
    }

    [Serializable]
    private sealed class GltfRoot
    {
        public GltfBufferView[] bufferViews;
        public GltfAccessor[] accessors;
        public GltfMesh[] meshes;
        public GltfMaterial[] materials;
        public GltfImage[] images;
        public GltfTexture[] textures;
        public GltfNode[] nodes;
    }

    [Serializable]
    private sealed class GltfBufferView
    {
        public int byteOffset;
        public int byteLength;
        public int byteStride;
    }

    [Serializable]
    private sealed class GltfAccessor
    {
        public int bufferView = -1;
        public int byteOffset;
        public int componentType;
        public int count;
    }

    [Serializable]
    private sealed class GltfMesh
    {
        public GltfPrimitive[] primitives;
    }

    [Serializable]
    private sealed class GltfPrimitive
    {
        public GltfAttributes attributes;
        public int indices = -1;
        public int material = -1;
    }

    [Serializable]
    private sealed class GltfAttributes
    {
        public int POSITION = -1;
        public int NORMAL = -1;
        public int TEXCOORD_0 = -1;
    }

    [Serializable]
    private sealed class GltfNode
    {
        public int mesh = -1;
        public float[] translation;
        public float[] rotation;
        public float[] scale;
    }

    [Serializable]
    private sealed class GltfMaterial
    {
        public string name;
        public GltfPbrMetallicRoughness pbrMetallicRoughness;
        public float[] emissiveFactor;
        public bool doubleSided;
        public GltfMaterialExtensions extensions;
    }

    [Serializable]
    private sealed class GltfPbrMetallicRoughness
    {
        public float[] baseColorFactor;
        public float metallicFactor = 0.0f;
        public float roughnessFactor = 1.0f;
        public GltfTextureInfo baseColorTexture;
    }

    [Serializable]
    private sealed class GltfTextureInfo
    {
        public int index = -1;
        public int texCoord;
    }

    [Serializable]
    private sealed class GltfTexture
    {
        public int source = -1;
    }

    [Serializable]
    private sealed class GltfImage
    {
        public int bufferView = -1;
        public string mimeType;
        public string uri;
    }

    [Serializable]
    private sealed class GltfMaterialExtensions
    {
        public GltfEmissiveStrength KHR_materials_emissive_strength;
    }

    [Serializable]
    private sealed class GltfEmissiveStrength
    {
        public float emissiveStrength = 1.0f;
    }

    /// <summary>
    /// Creates a marker root immediately and fills it asynchronously from
    /// StreamingAssets.  The callback is invoked after every material
    /// primitive has been attached to the root.
    /// </summary>
    public static GameObject Request(
        string relativePath,
        Transform parent,
        Vector3 worldPosition,
        Quaternion worldRotation,
        Vector3 localScale,
        string objectName,
        int layer = 0,
        bool settleOnSupport = false,
        float supportY = 0f,
        Action<GameObject> onLoaded = null)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return null;
        }

        RuntimeGlbSceneLoader host = EnsureInstance();
        GameObject root = new GameObject(objectName);
        root.transform.SetParent(parent, true);
        root.transform.SetPositionAndRotation(worldPosition, worldRotation);
        root.transform.localScale = localScale;
        SetLayerRecursively(root.transform, layer);
        host.StartCoroutine(host.LoadIntoRoot(
            relativePath.Replace('\\', '/'),
            root,
            settleOnSupport,
            supportY,
            onLoaded));
        return root;
    }

    private static RuntimeGlbSceneLoader EnsureInstance()
    {
        if (instance != null)
        {
            return instance;
        }

        GameObject existing = GameObject.Find(HostName);
        if (existing != null)
        {
            instance = existing.GetComponent<RuntimeGlbSceneLoader>();
            if (instance == null)
            {
                instance = existing.AddComponent<RuntimeGlbSceneLoader>();
            }

            return instance;
        }

        GameObject hostObject = new GameObject(HostName);
        instance = hostObject.AddComponent<RuntimeGlbSceneLoader>();
        return instance;
    }

    private void OnDestroy()
    {
        if (instance == this)
        {
            instance = null;
        }
    }

    private IEnumerator LoadIntoRoot(
        string relativePath,
        GameObject root,
        bool settleOnSupport,
        float supportY,
        Action<GameObject> onLoaded)
    {
        RuntimeSceneAsset asset = null;
        if (!cache.TryGetValue(relativePath, out asset))
        {
            if (pending.Contains(relativePath))
            {
                while (!cache.TryGetValue(relativePath, out asset) &&
                    pending.Contains(relativePath))
                {
                    yield return null;
                }
            }
            else
            {
                pending.Add(relativePath);
                yield return StartCoroutine(LoadAsset(relativePath));
                pending.Remove(relativePath);
                cache.TryGetValue(relativePath, out asset);
            }
        }

        if (root == null || asset == null || asset.Parts == null ||
            asset.Parts.Length == 0)
        {
            if (root != null)
            {
                DestroyRuntimeObject(root);
            }

            onLoaded?.Invoke(null);
            yield break;
        }

        GameObject contentRoot = new GameObject("City GLB Content");
        contentRoot.transform.SetParent(root.transform, false);
        contentRoot.transform.localPosition = asset.LocalPosition;
        contentRoot.transform.localRotation = asset.LocalRotation;
        contentRoot.transform.localScale = asset.LocalScale;
        SetLayerRecursively(contentRoot.transform, root.layer);

        Bounds combinedBounds = default;
        bool hasBounds = false;
        for (int partIndex = 0; partIndex < asset.Parts.Length; partIndex++)
        {
            RuntimeScenePart part = asset.Parts[partIndex];
            if (part == null || part.Mesh == null)
            {
                continue;
            }

            GameObject child = new GameObject(
                string.IsNullOrEmpty(part.Mesh.name)
                    ? "City material primitive"
                    : part.Mesh.name);
            child.transform.SetParent(contentRoot.transform, false);
            child.layer = root.layer;
            MeshFilter filter = child.AddComponent<MeshFilter>();
            filter.sharedMesh = part.Mesh;
            MeshRenderer renderer = child.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = part.Material;
            renderer.shadowCastingMode = ShadowCastingMode.On;
            renderer.receiveShadows = true;

            if (!hasBounds)
            {
                combinedBounds = renderer.bounds;
                hasBounds = true;
            }
            else
            {
                combinedBounds.Encapsulate(renderer.bounds);
            }
        }

        if (settleOnSupport && hasBounds)
        {
            root.transform.position += Vector3.up *
                (supportY - combinedBounds.min.y + 0.008f);
        }

        Debug.Log(
            $"GYMCHAOS_RUNTIME_GLB_SCENE_READY path={relativePath} " +
            $"object={root.name} parts={asset.Parts.Length} " +
            $"bounds={combinedBounds} materials={CountMaterials(asset)} " +
            $"layer={root.layer}",
            root);
        onLoaded?.Invoke(root);
    }

    private IEnumerator LoadAsset(string relativePath)
    {
        string localPath = Path.Combine(
            Application.dataPath,
            "StreamingAssets",
            relativePath.Replace('/', Path.DirectorySeparatorChar));
        byte[] bytes = null;
        if (File.Exists(localPath))
        {
            try
            {
                bytes = File.ReadAllBytes(localPath);
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    $"GYMCHAOS_RUNTIME_GLB_SCENE_LOAD_ERROR path={localPath} " +
                    $"error={exception.Message}",
                    this);
                cache[relativePath] = null;
                yield break;
            }
        }
        else
        {
            string path = JoinStreamingAssetsPath(relativePath);
            using (UnityWebRequest request = UnityWebRequest.Get(path))
            {
                request.downloadHandler = new DownloadHandlerBuffer();
                yield return request.SendWebRequest();
                if (request.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogError(
                        $"GYMCHAOS_RUNTIME_GLB_SCENE_LOAD_ERROR path={path} " +
                        $"error={request.error}",
                        this);
                    cache[relativePath] = null;
                    yield break;
                }

                bytes = request.downloadHandler.data;
            }
        }

        if (!TryReadGlb(bytes, out GltfRoot gltf, out byte[] binary))
        {
            Debug.LogError(
                $"GYMCHAOS_RUNTIME_GLB_SCENE_PARSE_ERROR path={relativePath}",
                this);
            cache[relativePath] = null;
            yield break;
        }

        cache[relativePath] = CreateAsset(relativePath, gltf, binary);
        yield return null;
    }

    private static RuntimeSceneAsset CreateAsset(
        string relativePath,
        GltfRoot gltf,
        byte[] binary)
    {
        if (gltf.meshes == null || gltf.meshes.Length == 0)
        {
            return null;
        }

        int meshIndex = 0;
        GltfNode node = null;
        if (gltf.nodes != null)
        {
            for (int nodeIndex = 0; nodeIndex < gltf.nodes.Length; nodeIndex++)
            {
                if (gltf.nodes[nodeIndex] != null &&
                    gltf.nodes[nodeIndex].mesh >= 0)
                {
                    meshIndex = gltf.nodes[nodeIndex].mesh;
                    node = gltf.nodes[nodeIndex];
                    break;
                }
            }
        }

        if (meshIndex < 0 || meshIndex >= gltf.meshes.Length)
        {
            return null;
        }

        GltfPrimitive[] primitives = gltf.meshes[meshIndex].primitives;
        if (primitives == null || primitives.Length == 0)
        {
            return null;
        }

        List<RuntimeScenePart> parts = new List<RuntimeScenePart>(primitives.Length);
        for (int primitiveIndex = 0; primitiveIndex < primitives.Length; primitiveIndex++)
        {
            GltfPrimitive primitive = primitives[primitiveIndex];
            if (primitive == null || primitive.attributes == null ||
                primitive.attributes.POSITION < 0)
            {
                continue;
            }

            Vector3[] sourcePositions = ReadVector3Accessor(
                gltf, binary, primitive.attributes.POSITION);
            Vector3[] sourceNormals = ReadVector3Accessor(
                gltf, binary, primitive.attributes.NORMAL);
            Vector2[] sourceUvs = ReadVector2Accessor(
                gltf, binary, primitive.attributes.TEXCOORD_0);
            int[] triangles = ReadIndexAccessor(
                gltf, binary, primitive.indices);
            if (sourcePositions.Length == 0 || triangles.Length < 3)
            {
                continue;
            }

            Vector3[] positions = new Vector3[sourcePositions.Length];
            Vector3[] normals = sourceNormals.Length == sourcePositions.Length
                ? new Vector3[sourceNormals.Length]
                : null;
            Vector2[] uvs = sourceUvs.Length == sourcePositions.Length
                ? new Vector2[sourceUvs.Length]
                : null;
            for (int vertexIndex = 0; vertexIndex < sourcePositions.Length; vertexIndex++)
            {
                Vector3 source = sourcePositions[vertexIndex];
                positions[vertexIndex] = ConvertPosition(source);
                if (normals != null)
                {
                    normals[vertexIndex] = ConvertDirection(
                        sourceNormals[vertexIndex]).normalized;
                }

                if (uvs != null)
                {
                    uvs[vertexIndex] = new Vector2(
                        sourceUvs[vertexIndex].x,
                        1f - sourceUvs[vertexIndex].y);
                }
            }

            for (int index = 0; index + 2 < triangles.Length; index += 3)
            {
                int swap = triangles[index];
                triangles[index] = triangles[index + 2];
                triangles[index + 2] = swap;
            }

            Mesh mesh = new Mesh
            {
                name = $"Runtime GLB scene mesh {primitiveIndex} - {relativePath}",
                indexFormat = IndexFormat.UInt32,
                vertices = positions,
                triangles = triangles
            };
            if (normals != null)
            {
                mesh.normals = normals;
            }
            else
            {
                mesh.RecalculateNormals();
            }

            if (uvs != null)
            {
                mesh.uv = uvs;
            }

            mesh.RecalculateBounds();
            parts.Add(new RuntimeScenePart
            {
                Mesh = mesh,
                Material = CreateMaterial(gltf, binary, primitive.material, relativePath,
                    primitiveIndex)
            });
        }

        if (parts.Count == 0)
        {
            return null;
        }

        return new RuntimeSceneAsset
        {
            Parts = parts.ToArray(),
            LocalPosition = node != null
                ? ConvertPosition(ToVector3(node.translation, Vector3.zero))
                : Vector3.zero,
            LocalRotation = node != null
                ? ConvertRotation(node.rotation)
                : Quaternion.identity,
            LocalScale = node != null
                ? ToVector3(node.scale, Vector3.one)
                : Vector3.one
        };
    }

    private static Material CreateMaterial(
        GltfRoot gltf,
        byte[] binary,
        int materialIndex,
        string relativePath,
        int primitiveIndex)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit") ??
            Shader.Find("Standard") ??
            Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null)
        {
            Debug.LogError(
                $"No shader available for runtime GLB scene: {relativePath}");
            return null;
        }

        GltfMaterial source = materialIndex >= 0 && gltf.materials != null &&
            materialIndex < gltf.materials.Length
            ? gltf.materials[materialIndex]
            : null;
        GltfPbrMetallicRoughness pbr = source != null
            ? source.pbrMetallicRoughness
            : null;
        bool isFacadeMaterial = source != null &&
            source.name.StartsWith("MAT_Facade_Procedural_", StringComparison.OrdinalIgnoreCase);
        Color baseColor = ToColor(
            pbr != null ? pbr.baseColorFactor : null,
            Color.white);
        Material material = new Material(shader)
        {
            name = source != null && !string.IsNullOrEmpty(source.name)
                ? source.name
                : $"Runtime GLB scene material {primitiveIndex}",
            enableInstancing = true
        };
        Texture2D albedoTexture = pbr != null
            ? LoadGlbTexture(
                gltf,
                binary,
                pbr.baseColorTexture,
                relativePath,
                primitiveIndex)
            : null;
        if (albedoTexture == null && isFacadeMaterial)
        {
            albedoTexture = GetFacadeFallbackTexture(source.name);
        }

        if (albedoTexture != null)
        {
            baseColor = Color.white;
            if (material.HasProperty("_BaseMap"))
            {
                material.SetTexture("_BaseMap", albedoTexture);
            }

            if (material.HasProperty("_MainTex"))
            {
                material.SetTexture("_MainTex", albedoTexture);
            }
        }
        material.SetColor("_BaseColor", baseColor);
        material.SetColor("_Color", baseColor);
        if (pbr != null)
        {
            if (material.HasProperty("_Metallic"))
            {
                material.SetFloat("_Metallic", Mathf.Clamp01(pbr.metallicFactor));
            }

            if (material.HasProperty("_Smoothness"))
            {
                material.SetFloat("_Smoothness", 1f - Mathf.Clamp01(pbr.roughnessFactor));
            }
        }

        Color emission = ToColor(
            source != null ? source.emissiveFactor : null,
            Color.black);
        float emissionStrength = 1f;
        if (source != null && source.extensions != null &&
            source.extensions.KHR_materials_emissive_strength != null)
        {
            emissionStrength = Mathf.Max(0f,
                source.extensions.KHR_materials_emissive_strength.emissiveStrength);
        }

        if (!isFacadeMaterial && emission.maxColorComponent > 0.0001f)
        {
            Color finalEmission = emission * emissionStrength;
            if (material.HasProperty("_EmissionColor"))
            {
                material.SetColor("_EmissionColor", finalEmission);
            }

            if (material.HasProperty("_EmissiveColor"))
            {
                material.SetColor("_EmissiveColor", finalEmission);
            }

            material.EnableKeyword("_EMISSION");
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        }

        if (source != null && source.doubleSided && material.HasProperty("_Cull"))
        {
            material.SetFloat("_Cull", 0f);
        }

        return material;
    }

    private static Texture2D LoadGlbTexture(
        GltfRoot gltf,
        byte[] binary,
        GltfTextureInfo textureInfo,
        string relativePath,
        int primitiveIndex)
    {
        if (textureInfo == null || textureInfo.index < 0 ||
            gltf.textures == null || textureInfo.index >= gltf.textures.Length ||
            binary == null)
        {
            return null;
        }

        GltfTexture texture = gltf.textures[textureInfo.index];
        if (texture == null || texture.source < 0 || gltf.images == null ||
            texture.source >= gltf.images.Length)
        {
            return null;
        }

        GltfImage image = gltf.images[texture.source];
        byte[] encoded = null;
        if (image != null && image.bufferView >= 0 &&
            gltf.bufferViews != null && image.bufferView < gltf.bufferViews.Length)
        {
            GltfBufferView view = gltf.bufferViews[image.bufferView];
            int start = Mathf.Max(0, view.byteOffset);
            if (view.byteLength > 0 && start + view.byteLength <= binary.Length)
            {
                encoded = new byte[view.byteLength];
                Buffer.BlockCopy(binary, start, encoded, 0, view.byteLength);
            }
        }
        else if (image != null && !string.IsNullOrEmpty(image.uri) &&
            image.uri.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            int comma = image.uri.IndexOf(',');
            if (comma >= 0)
            {
                try
                {
                    encoded = Convert.FromBase64String(image.uri.Substring(comma + 1));
                }
                catch (FormatException)
                {
                    encoded = null;
                }
            }
        }

        if (encoded == null || encoded.Length == 0)
        {
            return null;
        }

        Texture2D result = new Texture2D(
            2,
            2,
            TextureFormat.RGBA32,
            false,
            false);
        if (!ImageConversion.LoadImage(result, encoded, true))
        {
            UnityEngine.Object.Destroy(result);
            Debug.LogWarning(
                $"GYMCHAOS_RUNTIME_GLB_TEXTURE_DECODE_FAIL path={relativePath} " +
                $"primitive={primitiveIndex}");
            return null;
        }

        result.name = string.IsNullOrEmpty(image.uri)
            ? $"Runtime GLB embedded texture {textureInfo.index}"
            : image.uri;
        result.wrapMode = TextureWrapMode.Repeat;
        result.filterMode = FilterMode.Bilinear;
        result.anisoLevel = 4;
        return result;
    }

    private static Texture2D GetFacadeFallbackTexture(string materialName)
    {
        if (FacadeFallbackTextures.TryGetValue(materialName, out Texture2D cached))
        {
            return cached;
        }

        Color baseColor = new Color(0.055f, 0.09f, 0.105f, 1f);
        Color panelColor = new Color(0.105f, 0.16f, 0.17f, 1f);
        Color mortarColor = new Color(0.018f, 0.028f, 0.032f, 1f);
        if (materialName.IndexOf("WarmGray", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            baseColor = new Color(0.14f, 0.115f, 0.095f, 1f);
            panelColor = new Color(0.26f, 0.19f, 0.14f, 1f);
            mortarColor = new Color(0.035f, 0.028f, 0.024f, 1f);
        }
        else if (materialName.IndexOf("BlueBlack", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            baseColor = new Color(0.035f, 0.105f, 0.135f, 1f);
            panelColor = new Color(0.055f, 0.17f, 0.19f, 1f);
            mortarColor = new Color(0.012f, 0.035f, 0.045f, 1f);
        }
        else if (materialName.IndexOf("GreenBlack", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            baseColor = new Color(0.035f, 0.115f, 0.085f, 1f);
            panelColor = new Color(0.065f, 0.19f, 0.145f, 1f);
            mortarColor = new Color(0.012f, 0.04f, 0.03f, 1f);
        }

        const int size = 128;
        Color[] pixels = new Color[size * size];
        for (int y = 0; y < size; y++)
        {
            float v = y / (float)(size - 1);
            int row = Mathf.FloorToInt(v * 18f);
            float rowOffset = row % 2 == 0 ? 0f : 0.5f;
            for (int x = 0; x < size; x++)
            {
                float u = x / (float)(size - 1);
                float brickU = (u * 13f + rowOffset) % 1f;
                float brickV = (v * 18f) % 1f;
                bool seam = brickU < 0.035f || brickV < 0.045f;
                float variation = 0.90f + 0.10f *
                    (0.5f + 0.5f * Mathf.Sin(x * 0.17f + y * 0.071f));
                if (seam)
                {
                    pixels[y * size + x] = mortarColor;
                }
                else
                {
                    float mix = 0.18f + 0.18f *
                        (0.5f + 0.5f * Mathf.Sin(u * 37f + row * 1.7f));
                    pixels[y * size + x] = Color.Lerp(baseColor, panelColor, mix) * variation;
                }
            }
        }

        Texture2D fallback = new Texture2D(
            size,
            size,
            TextureFormat.RGBA32,
            false,
            false)
        {
            name = "Runtime Facade Fallback " + materialName,
            wrapMode = TextureWrapMode.Repeat,
            filterMode = FilterMode.Bilinear,
            anisoLevel = 4
        };
        fallback.SetPixels(pixels);
        fallback.Apply(false, true);
        FacadeFallbackTextures[materialName] = fallback;
        return fallback;
    }

    private static int CountMaterials(RuntimeSceneAsset asset)
    {
        HashSet<Material> materials = new HashSet<Material>();
        for (int index = 0; index < asset.Parts.Length; index++)
        {
            if (asset.Parts[index] != null && asset.Parts[index].Material != null)
            {
                materials.Add(asset.Parts[index].Material);
            }
        }

        return materials.Count;
    }

    private static Vector3 ConvertPosition(Vector3 source)
    {
        return new Vector3(-source.x, source.y, source.z);
    }

    private static Vector3 ConvertDirection(Vector3 source)
    {
        return new Vector3(-source.x, source.y, source.z);
    }

    private static Quaternion ConvertRotation(float[] values)
    {
        if (values == null || values.Length < 4)
        {
            return Quaternion.identity;
        }

        Quaternion source = new Quaternion(values[0], values[1], values[2], values[3]);
        Matrix4x4 reflection = Matrix4x4.Scale(new Vector3(-1f, 1f, 1f));
        Matrix4x4 converted = reflection * Matrix4x4.Rotate(source) * reflection;
        return Quaternion.LookRotation(
            converted.MultiplyVector(Vector3.forward),
            converted.MultiplyVector(Vector3.up));
    }

    private static Vector3 ToVector3(float[] values, Vector3 fallback)
    {
        return values != null && values.Length >= 3
            ? new Vector3(values[0], values[1], values[2])
            : fallback;
    }

    private static Color ToColor(float[] values, Color fallback)
    {
        if (values == null || values.Length < 3)
        {
            return fallback;
        }

        return new Color(
            values[0],
            values[1],
            values[2],
            values.Length >= 4 ? values[3] : 1f);
    }

    private static bool TryReadGlb(
        byte[] bytes,
        out GltfRoot gltf,
        out byte[] binary)
    {
        gltf = null;
        binary = null;
        if (bytes == null || bytes.Length < 20 ||
            BitConverter.ToUInt32(bytes, 0) != GlbMagic)
        {
            return false;
        }

        int offset = 12;
        string json = null;
        while (offset + 8 <= bytes.Length)
        {
            int length = (int)BitConverter.ToUInt32(bytes, offset);
            uint type = BitConverter.ToUInt32(bytes, offset + 4);
            offset += 8;
            if (length < 0 || offset + length > bytes.Length)
            {
                return false;
            }

            if (type == JsonChunk)
            {
                json = Encoding.UTF8.GetString(bytes, offset, length)
                    .TrimEnd('\0', ' ', '\n', '\r', '\t');
            }
            else if (type == BinaryChunk)
            {
                binary = new byte[length];
                Buffer.BlockCopy(bytes, offset, binary, 0, length);
            }

            offset += length;
        }

        if (string.IsNullOrEmpty(json) || binary == null)
        {
            return false;
        }

        gltf = JsonUtility.FromJson<GltfRoot>(json);
        return gltf != null && gltf.meshes != null && gltf.meshes.Length > 0 &&
            gltf.accessors != null && gltf.bufferViews != null;
    }

    private static Vector3[] ReadVector3Accessor(
        GltfRoot gltf,
        byte[] binary,
        int accessorIndex)
    {
        if (accessorIndex < 0 || accessorIndex >= gltf.accessors.Length)
        {
            return Array.Empty<Vector3>();
        }

        GltfAccessor accessor = gltf.accessors[accessorIndex];
        if (accessor.bufferView < 0 || accessor.bufferView >= gltf.bufferViews.Length)
        {
            return Array.Empty<Vector3>();
        }

        GltfBufferView view = gltf.bufferViews[accessor.bufferView];
        int stride = view.byteStride > 0 ? view.byteStride : 12;
        int start = view.byteOffset + accessor.byteOffset;
        Vector3[] result = new Vector3[accessor.count];
        for (int index = 0; index < result.Length; index++)
        {
            int offset = start + index * stride;
            result[index] = new Vector3(
                BitConverter.ToSingle(binary, offset),
                BitConverter.ToSingle(binary, offset + 4),
                BitConverter.ToSingle(binary, offset + 8));
        }

        return result;
    }

    private static Vector2[] ReadVector2Accessor(
        GltfRoot gltf,
        byte[] binary,
        int accessorIndex)
    {
        if (accessorIndex < 0 || accessorIndex >= gltf.accessors.Length)
        {
            return Array.Empty<Vector2>();
        }

        GltfAccessor accessor = gltf.accessors[accessorIndex];
        if (accessor.bufferView < 0 || accessor.bufferView >= gltf.bufferViews.Length)
        {
            return Array.Empty<Vector2>();
        }

        GltfBufferView view = gltf.bufferViews[accessor.bufferView];
        int stride = view.byteStride > 0 ? view.byteStride : 8;
        int start = view.byteOffset + accessor.byteOffset;
        Vector2[] result = new Vector2[accessor.count];
        for (int index = 0; index < result.Length; index++)
        {
            int offset = start + index * stride;
            result[index] = new Vector2(
                BitConverter.ToSingle(binary, offset),
                BitConverter.ToSingle(binary, offset + 4));
        }

        return result;
    }

    private static int[] ReadIndexAccessor(
        GltfRoot gltf,
        byte[] binary,
        int accessorIndex)
    {
        if (accessorIndex < 0 || accessorIndex >= gltf.accessors.Length)
        {
            return Array.Empty<int>();
        }

        GltfAccessor accessor = gltf.accessors[accessorIndex];
        if (accessor.bufferView < 0 || accessor.bufferView >= gltf.bufferViews.Length)
        {
            return Array.Empty<int>();
        }

        GltfBufferView view = gltf.bufferViews[accessor.bufferView];
        int componentSize = accessor.componentType == 5125 ? 4 :
            accessor.componentType == 5123 ? 2 : 1;
        int stride = view.byteStride > 0 ? view.byteStride : componentSize;
        int start = view.byteOffset + accessor.byteOffset;
        int[] result = new int[accessor.count];
        for (int index = 0; index < result.Length; index++)
        {
            int offset = start + index * stride;
            result[index] = accessor.componentType == 5125
                ? (int)BitConverter.ToUInt32(binary, offset)
                : accessor.componentType == 5123
                    ? BitConverter.ToUInt16(binary, offset)
                    : binary[offset];
        }

        return result;
    }

    private static string JoinStreamingAssetsPath(string relativePath)
    {
        string path = Application.streamingAssetsPath.TrimEnd('/', '\\') + "/" + relativePath;
        if (path.Contains("://"))
        {
            return path;
        }

        return "file:///" + path.Replace('\\', '/').TrimStart('/');
    }

    private static void SetLayerRecursively(Transform root, int layer)
    {
        root.gameObject.layer = layer;
        for (int childIndex = 0; childIndex < root.childCount; childIndex++)
        {
            SetLayerRecursively(root.GetChild(childIndex), layer);
        }
    }

    private static void DestroyRuntimeObject(UnityEngine.Object target)
    {
        if (target == null)
        {
            return;
        }

        if (Application.isPlaying)
        {
            Destroy(target);
        }
        else
        {
            DestroyImmediate(target);
        }
    }
}

