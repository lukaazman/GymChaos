using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Small procedural mesh accumulator with sub-meshes (race track geometry).</summary>
public sealed class GymRaceMeshBuilder
{
    private readonly List<Vector3> vertices = new List<Vector3>();
    private readonly List<Vector3> normals = new List<Vector3>();
    private readonly List<Vector2> uvs = new List<Vector2>();
    private readonly List<List<int>> subMeshes = new List<List<int>>();

    public int VertexCount => vertices.Count;

    public GymRaceMeshBuilder(int subMeshCount = 1)
    {
        for (int i = 0; i < subMeshCount; i++)
        {
            subMeshes.Add(new List<int>());
        }
    }

    public int AddVertex(Vector3 position, Vector3 normal, Vector2 uv)
    {
        vertices.Add(position);
        normals.Add(normal);
        uvs.Add(uv);
        return vertices.Count - 1;
    }

    public void AddTriangle(int a, int b, int c, int subMesh = 0)
    {
        List<int> list = subMeshes[subMesh];
        list.Add(a);
        list.Add(b);
        list.Add(c);
    }

    /// <summary>Quad a-b-c-d in clockwise order seen from the front (Unity winding).</summary>
    public void AddQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, int subMesh = 0, float uvScale = 0f)
    {
        Vector3 normal = Vector3.Cross(b - a, d - a).normalized;
        if (normal.sqrMagnitude < 1e-6f)
        {
            normal = Vector3.up;
        }
        Vector2 ua;
        Vector2 ub;
        Vector2 uc;
        Vector2 ud;
        if (uvScale > 0f)
        {
            // Box-style world mapping: pick the plane facing the normal.
            ua = Project(a, normal, uvScale);
            ub = Project(b, normal, uvScale);
            uc = Project(c, normal, uvScale);
            ud = Project(d, normal, uvScale);
        }
        else
        {
            ua = new Vector2(0f, 0f);
            ub = new Vector2(0f, 1f);
            uc = new Vector2(1f, 1f);
            ud = new Vector2(1f, 0f);
        }
        int ia = AddVertex(a, normal, ua);
        int ib = AddVertex(b, normal, ub);
        int ic = AddVertex(c, normal, uc);
        int id = AddVertex(d, normal, ud);
        AddTriangle(ia, ib, ic, subMesh);
        AddTriangle(ia, ic, id, subMesh);
    }

    private static Vector2 Project(Vector3 p, Vector3 normal, float scale)
    {
        Vector3 n = new Vector3(Mathf.Abs(normal.x), Mathf.Abs(normal.y), Mathf.Abs(normal.z));
        if (n.y >= n.x && n.y >= n.z)
        {
            return new Vector2(p.x, p.z) / scale;
        }
        if (n.x >= n.z)
        {
            return new Vector2(p.z, p.y) / scale;
        }
        return new Vector2(p.x, p.y) / scale;
    }

    public Mesh ToMesh(string name)
    {
        Mesh mesh = new Mesh
        {
            name = name,
            indexFormat = vertices.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16
        };
        mesh.SetVertices(vertices);
        mesh.SetNormals(normals);
        mesh.SetUVs(0, uvs);
        mesh.subMeshCount = subMeshes.Count;
        for (int i = 0; i < subMeshes.Count; i++)
        {
            mesh.SetTriangles(subMeshes[i], i, false);
        }
        mesh.RecalculateBounds();
        return mesh;
    }
}
