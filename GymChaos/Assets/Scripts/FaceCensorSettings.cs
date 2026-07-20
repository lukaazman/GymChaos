using System;
using UnityEngine;
using UnityEngine.Rendering;

[Serializable]
public struct FaceCensorProfile
{
    public Vector3 LocalPosition;
    public Vector3 LocalEulerAngles;
    public Vector2 Size;
    public float FaceDepth;
    public float ProfileCoverage;
    public Color PixelTone;

    public FaceCensorProfile(
        Vector3 localPosition, Vector3 localEulerAngles, Vector2 size,
        float faceDepth, float profileCoverage, Color pixelTone)
    {
        LocalPosition = localPosition;
        LocalEulerAngles = localEulerAngles;
        Size = size;
        FaceDepth = faceDepth;
        ProfileCoverage = profileCoverage;
        PixelTone = pixelTone;
    }
}

public sealed class FaceCensorSettings : MonoBehaviour
{
    [SerializeField] private FaceCensorProfile profile;

    public void Configure(FaceCensorProfile value, Transform head, int textureSeed)
    {
        profile = value;
        transform.SetParent(head, false);
        transform.localPosition = profile.LocalPosition;
        transform.localRotation = Quaternion.Euler(profile.LocalEulerAngles);
        transform.localScale = Vector3.one;

        MeshFilter filter = gameObject.AddComponent<MeshFilter>();
        filter.sharedMesh = CreateCurvedFaceShell(profile);

        MeshRenderer renderer = gameObject.AddComponent<MeshRenderer>();
        Shader shader = Shader.Find("GymChaos/FaceCensor");
        if (shader == null)
        {
            shader = Shader.Find("Universal Render Pipeline/Unlit");
        }

        Material material = new Material(shader) { name = name + " Material" };
        material.SetTexture("_BaseMap", CreateBlackTexture());
        material.SetColor("_Tint", Color.white);
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
    }

    private static Mesh CreateCurvedFaceShell(FaceCensorProfile value)
    {
        const int columns = 20;
        const int rows = 2;
        Vector3[] vertices = new Vector3[(columns + 1) * (rows + 1)];
        Vector2[] uvs = new Vector2[vertices.Length];
        int[] triangles = new int[columns * rows * 6];
        float horizontalArc = Mathf.Clamp(value.ProfileCoverage, 55f, 82f) * Mathf.Deg2Rad;

        for (int y = 0; y <= rows; y++)
        {
            float v = y / (float)rows;
            for (int x = 0; x <= columns; x++)
            {
                float u = x / (float)columns;
                float horizontal = Mathf.Lerp(-horizontalArc, horizontalArc, u);
                int index = y * (columns + 1) + x;
                vertices[index] = new Vector3(
                    Mathf.Sin(horizontal) * value.Size.x * 0.5f,
                    Mathf.Lerp(-value.Size.y * 0.5f, value.Size.y * 0.5f, v),
                    Mathf.Cos(horizontal) * value.FaceDepth);
                uvs[index] = new Vector2(u, v);
            }
        }

        int triangle = 0;
        for (int y = 0; y < rows; y++)
        {
            for (int x = 0; x < columns; x++)
            {
                int a = y * (columns + 1) + x;
                int b = a + 1;
                int c = a + columns + 1;
                int d = c + 1;
                triangles[triangle++] = a;
                triangles[triangle++] = c;
                triangles[triangle++] = b;
                triangles[triangle++] = b;
                triangles[triangle++] = c;
                triangles[triangle++] = d;
            }
        }

        Mesh mesh = new Mesh { name = "Curved Face Censor Mesh" };
        mesh.vertices = vertices;
        mesh.uv = uvs;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    private static Texture2D CreateBlackTexture()
    {
        Texture2D texture = new Texture2D(1, 1, TextureFormat.RGBA32, false)
        {
            name = "Opaque Black Eye Bar",
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp
        };
        texture.SetPixel(0, 0, Color.black);
        texture.Apply(false, true);
        return texture;
    }
}
