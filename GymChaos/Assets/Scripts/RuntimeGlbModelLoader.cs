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
                GymShared.DestroyRuntimeObject(root);
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
            string path = GymShared.JoinStreamingAssetsPath(relativePath);
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

        if (!GymGlb.TryReadGlb(bytes, out GltfRoot gltf, out byte[] binary))
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

        Vector3[] sourcePositions = GymGlb.ReadVector3Accessor(
            gltf, binary, primitive.attributes.POSITION);
        Vector3[] sourceNormals = GymGlb.ReadVector3Accessor(
            gltf, binary, primitive.attributes.NORMAL);
        Vector2[] sourceUvs = GymGlb.ReadVector2Accessor(
            gltf, binary, primitive.attributes.TEXCOORD_0);
        int[] triangles = GymGlb.ReadIndexAccessor(gltf, binary, primitive.indices);
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
        Shader shader = GymMaterialLibrary.LitShader ?? GymMaterialLibrary.UnlitShader;
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
        bool decoded;
        using (GymProfiling.GlbTextureDecode.Auto())
        {
            decoded = texture.LoadImage(imageBytes, false);
        }
        if (decoded)
        {
            // Scanned wearables share the tiny-island atlas of the character
            // scans; keep their lower mips from mixing islands.
            Texture sampled = relativePath.Contains("wearables/")
                ? ScanTextureMips.Limit(texture, ownsSource: true)
                : texture;
            material.SetTexture("_BaseMap", sampled);
            material.SetTexture("_MainTex", sampled);
            material.mainTexture = sampled;
        }
        else
        {
            GymShared.DestroyRuntimeObject(texture);
        }

        return material;
    }

    private static void SetLayerRecursively(Transform root, int layer)
    {
        root.gameObject.layer = layer;
        for (int i = 0; i < root.childCount; i++)
        {
            SetLayerRecursively(root.GetChild(i), layer);
        }
    }
}
