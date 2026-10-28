using System;
using System.Text;
using UnityEngine;

// glTF 2.0 JSON schema subset read by the runtime GLB loaders (JsonUtility).
// Missing indices default to -1, so readers can tell "absent" from accessor 0.

[Serializable]
public sealed class GltfRoot
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
public sealed class GltfBufferView
{
    public int byteOffset;
    public int byteLength;
    public int byteStride;
}

[Serializable]
public sealed class GltfAccessor
{
    public int bufferView = -1;
    public int byteOffset;
    public int componentType;
    public int count;
    public string type;
}

[Serializable]
public sealed class GltfMesh
{
    public string name;
    public GltfPrimitive[] primitives;
}

[Serializable]
public sealed class GltfPrimitive
{
    public GltfAttributes attributes;
    public int indices = -1;
    public int material = -1;
}

[Serializable]
public sealed class GltfAttributes
{
    public int POSITION = -1;
    public int NORMAL = -1;
    public int TEXCOORD_0 = -1;
}

[Serializable]
public sealed class GltfNode
{
    public string name;
    public int mesh = -1;
    public float[] translation;
    public float[] rotation;
    public float[] scale;
}

[Serializable]
public sealed class GltfMaterial
{
    public string name;
    public GltfPbrMetallicRoughness pbrMetallicRoughness;
    public float[] emissiveFactor;
    public bool doubleSided;
    public string alphaMode;
    public float alphaCutoff = 0.5f;
    public GltfMaterialExtensions extensions;
}

[Serializable]
public sealed class GltfPbrMetallicRoughness
{
    public float[] baseColorFactor;
    public float metallicFactor = 0.0f;
    public float roughnessFactor = 1.0f;
    public GltfTextureInfo baseColorTexture;
}

[Serializable]
public sealed class GltfTextureInfo
{
    public int index = -1;
    public int texCoord;
}

[Serializable]
public sealed class GltfTexture
{
    public int source = -1;
}

[Serializable]
public sealed class GltfImage
{
    public int bufferView = -1;
    public string mimeType;
    public string uri;
}

[Serializable]
public sealed class GltfMaterialExtensions
{
    public GltfEmissiveStrength KHR_materials_emissive_strength;
}

[Serializable]
public sealed class GltfEmissiveStrength
{
    public float emissiveStrength = 1.0f;
}

/// <summary>Binary glTF container parsing and accessor reads shared by the runtime loaders.</summary>
public static class GymGlb
{
    private const uint GlbMagic = 0x46546C67;
    private const uint JsonChunk = 0x4E4F534A;
    private const uint BinaryChunk = 0x004E4942;

    /// <summary>Splits a .glb into its JSON (parsed) and binary chunks. False when either is missing or the file has no meshes.</summary>
    public static bool TryReadGlb(byte[] bytes, out GltfRoot gltf, out byte[] binary)
    {
        gltf = null;
        binary = null;
        if (bytes == null || bytes.Length < 20 || BitConverter.ToUInt32(bytes, 0) != GlbMagic)
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
                json = Encoding.UTF8.GetString(bytes, offset, length).TrimEnd('\0', ' ', '\n', '\r', '\t');
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

    public static Vector3[] ReadVector3Accessor(GltfRoot gltf, byte[] binary, int accessorIndex)
    {
        if (!TryGetView(gltf, accessorIndex, out GltfAccessor accessor, out GltfBufferView view))
        {
            return Array.Empty<Vector3>();
        }

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

    public static Vector2[] ReadVector2Accessor(GltfRoot gltf, byte[] binary, int accessorIndex)
    {
        if (!TryGetView(gltf, accessorIndex, out GltfAccessor accessor, out GltfBufferView view))
        {
            return Array.Empty<Vector2>();
        }

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

    /// <summary>Reads UNSIGNED_INT (5125), UNSIGNED_SHORT (5123) or UNSIGNED_BYTE indices.</summary>
    public static int[] ReadIndexAccessor(GltfRoot gltf, byte[] binary, int accessorIndex)
    {
        if (!TryGetView(gltf, accessorIndex, out GltfAccessor accessor, out GltfBufferView view))
        {
            return Array.Empty<int>();
        }

        int componentSize = accessor.componentType == 5125 ? 4 : accessor.componentType == 5123 ? 2 : 1;
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

    private static bool TryGetView(GltfRoot gltf, int accessorIndex, out GltfAccessor accessor, out GltfBufferView view)
    {
        accessor = null;
        view = null;
        if (accessorIndex < 0 || accessorIndex >= gltf.accessors.Length)
        {
            return false;
        }

        accessor = gltf.accessors[accessorIndex];
        if (accessor.bufferView < 0 || accessor.bufferView >= gltf.bufferViews.Length)
        {
            return false;
        }

        view = gltf.bufferViews[accessor.bufferView];
        return true;
    }
}
