using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.Rendering;

/// <summary>
/// Small runtime loader for the single-mesh GLB assets supplied with the game.
/// It deliberately keeps the same Blender-to-Unity handedness conversion as
/// GymLooseItemSpawner, but exposes it for world props and animated cosmetics.
/// </summary>
public sealed class RuntimeGlbModelLoader : MonoBehaviour
{
    private const string HostName = "Gym Runtime GLB Models";
    private const uint GlbMagic = 0x46546C67;
    private const uint JsonChunk = 0x4E4F534A;
    private const uint BinaryChunk = 0x004E4942;

    private static RuntimeGlbModelLoader instance;
    private readonly Dictionary<string, RuntimeAsset> cache =
        new Dictionary<string, RuntimeAsset>(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> pending =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    private sealed class RuntimeAsset
    {
        public Mesh Mesh;
        public Material Material;
        public Bounds LocalBounds;
    }

    [Serializable]
    private sealed class GltfRoot
    {
        public GltfBufferView[] bufferViews;
        public GltfAccessor[] accessors;
        public GltfMesh[] meshes;
        public GltfImage[] images;
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
    }

    [Serializable]
    private sealed class GltfAttributes
    {
        public int POSITION = -1;
        public int NORMAL = -1;
        public int TEXCOORD_0 = -1;
    }

    [Serializable]
    private sealed class GltfImage
    {
        public int bufferView = -1;
        public string mimeType;
    }

    /// <summary>
    /// Creates a model root immediately, then fills it asynchronously from
    /// StreamingAssets. The callback runs after its mesh/material are ready.
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

        RuntimeGlbModelLoader host = EnsureInstance();
        GameObject root = new GameObject(objectName);
        root.transform.SetParent(parent, true);
        root.transform.SetPositionAndRotation(worldPosition, worldRotation);
        root.transform.localScale = localScale;
        SetLayerRecursively(root.transform, layer);
        host.StartCoroutine(host.LoadIntoRoot(
            relativePath.Replace('\\', '/'), root, settleOnSupport, supportY, onLoaded));
        return root;
    }

    private static RuntimeGlbModelLoader EnsureInstance()
    {
        if (instance != null)
        {
            return instance;
        }

        GameObject existing = GameObject.Find(HostName);
        if (existing != null)
        {
            instance = existing.GetComponent<RuntimeGlbModelLoader>();
            if (instance == null)
            {
                instance = existing.AddComponent<RuntimeGlbModelLoader>();
            }

            return instance;
        }

        GameObject hostObject = new GameObject(HostName);
        instance = hostObject.AddComponent<RuntimeGlbModelLoader>();
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
        RuntimeAsset asset = null;
        if (!cache.TryGetValue(relativePath, out asset))
        {
            if (pending.Contains(relativePath))
            {
                // Requests for the same model are uncommon, but waiting for a
                // frame avoids two large texture uploads landing together.
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

        if (root == null || asset == null || asset.Mesh == null)
        {
            if (root != null)
            {
                DestroyRuntimeObject(root);
            }

            // Always complete the request contract. Cosmetic callers use the
            // callback to release their loading state; silently dropping a
            // failed request leaves the locker headwear spinner permanently
            // pending and makes a later selection unable to retry.
            onLoaded?.Invoke(null);

            yield break;
        }

        MeshFilter filter = root.AddComponent<MeshFilter>();
        filter.sharedMesh = asset.Mesh;
        MeshRenderer renderer = root.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = asset.Material;
        renderer.shadowCastingMode = ShadowCastingMode.On;
        renderer.receiveShadows = true;

        if (settleOnSupport)
        {
            root.transform.position += Vector3.up *
                (supportY - renderer.bounds.min.y + 0.008f);
        }

        Debug.Log(
            $"GYMCHAOS_RUNTIME_GLB_READY path={relativePath} object={root.name} " +
            $"bounds={renderer.bounds} layer={root.layer}", root);
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
                    $"GYMCHAOS_RUNTIME_GLB_LOAD_ERROR path={localPath} " +
                    $"error={exception.Message}", this);
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
                        $"GYMCHAOS_RUNTIME_GLB_LOAD_ERROR path={path} " +
                        $"error={request.error}", this);
                    cache[relativePath] = null;
                    yield break;
                }

                bytes = request.downloadHandler.data;
            }
        }

        if (!TryReadGlb(bytes, out GltfRoot gltf, out byte[] binary))
        {
            Debug.LogError($"GYMCHAOS_RUNTIME_GLB_PARSE_ERROR path={relativePath}", this);
            cache[relativePath] = null;
            yield break;
        }

        RuntimeAsset asset = CreateAsset(relativePath, gltf, binary);
        cache[relativePath] = asset;
        yield return null;
    }

    private static RuntimeAsset CreateAsset(
        string relativePath, GltfRoot gltf, byte[] binary)
    {
        if (gltf.meshes == null || gltf.meshes.Length == 0 ||
            gltf.meshes[0].primitives == null ||
            gltf.meshes[0].primitives.Length == 0)
        {
            Debug.LogError($"Runtime GLB has no primitive: {relativePath}");
            return null;
        }

        GltfPrimitive primitive = gltf.meshes[0].primitives[0];
        if (primitive.attributes == null || primitive.attributes.POSITION < 0)
        {
            Debug.LogError($"Runtime GLB has no POSITION accessor: {relativePath}");
            return null;
        }

        Vector3[] sourcePositions = ReadVector3Accessor(
            gltf, binary, primitive.attributes.POSITION);
        Vector3[] sourceNormals = ReadVector3Accessor(
            gltf, binary, primitive.attributes.NORMAL);
        Vector2[] sourceUvs = ReadVector2Accessor(
            gltf, binary, primitive.attributes.TEXCOORD_0);
        int[] triangles = ReadIndexAccessor(gltf, binary, primitive.indices);
        if (sourcePositions.Length == 0 || triangles.Length < 3)
        {
            Debug.LogError($"Runtime GLB has empty geometry: {relativePath}");
            return null;
        }

        Vector3[] positions = new Vector3[sourcePositions.Length];
        Vector3[] normals = sourceNormals.Length == sourcePositions.Length
            ? new Vector3[sourceNormals.Length]
            : null;
        Vector2[] uvs = sourceUvs.Length == sourcePositions.Length
            ? new Vector2[sourceUvs.Length]
            : null;
        for (int i = 0; i < sourcePositions.Length; i++)
        {
            Vector3 source = sourcePositions[i];
            positions[i] = new Vector3(-source.x, source.y, source.z);
            if (normals != null)
            {
                Vector3 normal = sourceNormals[i];
                normals[i] = new Vector3(-normal.x, normal.y, normal.z).normalized;
            }

            if (uvs != null)
            {
                uvs[i] = new Vector2(sourceUvs[i].x, 1f - sourceUvs[i].y);
            }
        }

        for (int i = 0; i + 2 < triangles.Length; i += 3)
        {
            int swap = triangles[i];
            triangles[i] = triangles[i + 2];
            triangles[i + 2] = swap;
        }

        Mesh mesh = new Mesh
        {
            name = "Runtime GLB mesh - " + relativePath,
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
        return new RuntimeAsset
        {
            Mesh = mesh,
            Material = CreateMaterial(relativePath, gltf, binary),
            LocalBounds = mesh.bounds
        };
    }

    private static Material CreateMaterial(
        string relativePath, GltfRoot gltf, byte[] binary)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit") ??
            Shader.Find("Standard") ?? Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null)
        {
            Debug.LogError($"No shader available for runtime GLB: {relativePath}");
            return null;
        }

        Material material = new Material(shader)
        {
            name = "Runtime GLB material - " + relativePath,
            enableInstancing = true
        };
        material.SetColor("_BaseColor", Color.white);
        material.SetColor("_Color", Color.white);
        if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", 0.08f);
        if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0.48f);

        if (gltf.images == null || gltf.images.Length == 0)
        {
            return material;
        }

        GltfImage image = gltf.images[0];
        if (image == null || image.bufferView < 0 ||
            image.bufferView >= gltf.bufferViews.Length)
        {
            return material;
        }

        GltfBufferView view = gltf.bufferViews[image.bufferView];
        if (view == null || view.byteOffset < 0 ||
            view.byteOffset + view.byteLength > binary.Length)
        {
            return material;
        }

        byte[] imageBytes = new byte[view.byteLength];
        Buffer.BlockCopy(binary, view.byteOffset, imageBytes, 0, view.byteLength);
        Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, true)
        {
            name = "Runtime GLB texture - " + relativePath,
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Repeat
        };
        if (texture.LoadImage(imageBytes, false))
        {
            material.SetTexture("_BaseMap", texture);
            material.SetTexture("_MainTex", texture);
            material.mainTexture = texture;
        }
        else
        {
            DestroyRuntimeObject(texture);
        }

        return material;
    }

    private static bool TryReadGlb(
        byte[] bytes, out GltfRoot gltf, out byte[] binary)
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
        GltfRoot gltf, byte[] binary, int accessorIndex)
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
        for (int i = 0; i < result.Length; i++)
        {
            int offset = start + i * stride;
            result[i] = new Vector3(
                BitConverter.ToSingle(binary, offset),
                BitConverter.ToSingle(binary, offset + 4),
                BitConverter.ToSingle(binary, offset + 8));
        }

        return result;
    }

    private static Vector2[] ReadVector2Accessor(
        GltfRoot gltf, byte[] binary, int accessorIndex)
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
        for (int i = 0; i < result.Length; i++)
        {
            int offset = start + i * stride;
            result[i] = new Vector2(
                BitConverter.ToSingle(binary, offset),
                BitConverter.ToSingle(binary, offset + 4));
        }

        return result;
    }

    private static int[] ReadIndexAccessor(
        GltfRoot gltf, byte[] binary, int accessorIndex)
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
        for (int i = 0; i < result.Length; i++)
        {
            int offset = start + i * stride;
            result[i] = accessor.componentType == 5125
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
        for (int i = 0; i < root.childCount; i++)
        {
            SetLayerRecursively(root.GetChild(i), layer);
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
