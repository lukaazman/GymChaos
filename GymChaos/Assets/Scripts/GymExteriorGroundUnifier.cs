using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Makes every walkable exterior ground surface read as one continuous slab.
///
/// The courtyard, parking lot, door path, landscape strip, road, bus pockets
/// and protein-store route are separate boxes/meshes for gameplay and
/// collider ownership. Rendered with their own materials and per-object UVs
/// they showed visible seams: different tints, different texture density per
/// box size, and smooth-shaded normals on the composite road that produced a
/// lighting gradient towards its region edges. This pass keeps the objects,
/// names and colliders, but gives them one shared material, world-space XZ
/// UVs and flat normals, and drops slab side faces, so neighbouring surfaces blend without a visible edge.
/// </summary>
public static class GymExteriorGroundUnifier
{
    // World metres covered by one repeat of the shared ground texture.
    public const float MetresPerTile = 3.2f;
    private const float MaxSurfaceThickness = 0.6f;
    private const float MaxTopOffset = 0.04f;

    public static int UnifiedSurfaceCount { get; private set; }
    public static Material SharedGroundMaterial { get; private set; }

    public static int Apply(
        Transform root,
        float floorY,
        Material sharedMaterial,
        ICollection<Material> groundMaterials)
    {
        UnifiedSurfaceCount = 0;
        SharedGroundMaterial = sharedMaterial;
        if (root == null || sharedMaterial == null || groundMaterials == null)
        {
            return 0;
        }

        // UVs now carry world-space tiling, so the material must not repeat
        // them a second time.
        if (sharedMaterial.HasProperty("_BaseMap"))
        {
            sharedMaterial.SetTextureScale("_BaseMap", Vector2.one);
            sharedMaterial.SetTextureOffset("_BaseMap", Vector2.zero);
        }
        if (sharedMaterial.HasProperty("_MainTex"))
        {
            sharedMaterial.SetTextureScale("_MainTex", Vector2.one);
            sharedMaterial.SetTextureOffset("_MainTex", Vector2.zero);
        }

        MeshRenderer[] renderers = root.GetComponentsInChildren<MeshRenderer>(true);
        int unified = 0;
        for (int i = 0; i < renderers.Length; i++)
        {
            MeshRenderer renderer = renderers[i];
            if (!IsGroundSurface(renderer, floorY, groundMaterials))
            {
                continue;
            }

            MeshFilter filter = renderer.GetComponent<MeshFilter>();
            Mesh source = filter != null ? filter.sharedMesh : null;
            if (source == null || !source.isReadable)
            {
                continue;
            }

            filter.sharedMesh = BuildWorldUvMesh(source, renderer.transform);
            Material[] materials = new Material[Mathf.Max(1, source.subMeshCount)];
            for (int m = 0; m < materials.Length; m++)
            {
                materials[m] = sharedMaterial;
            }
            renderer.sharedMaterials = materials;
            unified++;
        }

        UnifiedSurfaceCount = unified;
        Debug.Log(
            $"GYMCHAOS_EXTERIOR_GROUND_UNIFIED surfaces={unified} " +
            $"material={sharedMaterial.name} metresPerTile={MetresPerTile:F2}",
            root);
        return unified;
    }

    // Gives one renderer world-space XZ UVs (and flat, top-only faces) without
    // touching its material; used for surfaces outside the unified slab.
    public static void ApplyWorldUv(MeshRenderer renderer)
    {
        MeshFilter filter = renderer != null ? renderer.GetComponent<MeshFilter>() : null;
        if (filter != null && filter.sharedMesh != null && filter.sharedMesh.isReadable)
        {
            filter.sharedMesh = BuildWorldUvMesh(filter.sharedMesh, renderer.transform);
        }
    }

    private static bool IsGroundSurface(
        MeshRenderer renderer, float floorY, ICollection<Material> groundMaterials)
    {
        if (renderer == null)
        {
            return false;
        }

        Material[] materials = renderer.sharedMaterials;
        if (materials.Length == 0)
        {
            return false;
        }
        for (int m = 0; m < materials.Length; m++)
        {
            if (materials[m] == null || !groundMaterials.Contains(materials[m]))
            {
                return false;
            }
        }

        Bounds bounds = renderer.bounds;
        return bounds.size.y <= MaxSurfaceThickness &&
            bounds.max.y <= floorY + MaxTopOffset &&
            bounds.max.y >= floorY - MaxSurfaceThickness;
    }

    // Rebuilds the mesh with one vertex per triangle corner (flat normals),
    // horizontal faces only, and UVs taken from world X/Z. Submesh layout is preserved so region-based
    // checks on the composite road still see one submesh per region.
    private static Mesh BuildWorldUvMesh(Mesh source, Transform transform)
    {
        Vector3[] sourceVertices = source.vertices;
        int subMeshCount = Mathf.Max(1, source.subMeshCount);
        List<Vector3> vertices = new List<Vector3>();
        List<Vector3> normals = new List<Vector3>();
        List<Vector2> uvs = new List<Vector2>();
        int[][] subMeshTriangles = new int[subMeshCount][];
        float inverseTile = 1f / MetresPerTile;

        for (int sub = 0; sub < subMeshCount; sub++)
        {
            int[] triangles = source.GetTriangles(sub);
            List<int> rebuilt = new List<int>(triangles.Length);
            for (int t = 0; t + 2 < triangles.Length; t += 3)
            {
                Vector3 a = sourceVertices[triangles[t]];
                Vector3 b = sourceVertices[triangles[t + 1]];
                Vector3 c = sourceVertices[triangles[t + 2]];
                Vector3 normal = Vector3.Cross(b - a, c - a).normalized;
                Vector3 worldNormal = transform.TransformDirection(normal);
                // Drop vertical slab sides: where two surfaces meet at
                // slightly different depths the side strip rendered as a dark
                // seam line. Colliders are separate and unaffected.
                if (Mathf.Abs(worldNormal.y) < 0.5f)
                {
                    continue;
                }
                for (int corner = 0; corner < 3; corner++)
                {
                    Vector3 local = corner == 0 ? a : corner == 1 ? b : c;
                    Vector3 world = transform.TransformPoint(local);
                    Vector2 uv = new Vector2(world.x, world.z);
                    rebuilt.Add(vertices.Count);
                    vertices.Add(local);
                    normals.Add(normal);
                    uvs.Add(uv * inverseTile);
                }
            }
            subMeshTriangles[sub] = rebuilt.ToArray();
        }

        Mesh mesh = new Mesh { name = source.name + " (Unified Ground)" };
        if (vertices.Count > 65535)
        {
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        }
        mesh.SetVertices(vertices);
        mesh.SetNormals(normals);
        mesh.SetUVs(0, uvs);
        mesh.subMeshCount = subMeshCount;
        for (int sub = 0; sub < subMeshCount; sub++)
        {
            mesh.SetTriangles(subMeshTriangles[sub], sub);
        }
        mesh.RecalculateBounds();
        mesh.RecalculateTangents();
        return mesh;
    }
}
