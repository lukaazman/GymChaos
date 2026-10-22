using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Builds the race track geometry from <see cref="GymRaceTrackLayout"/> when
/// the race starts and destroys it afterwards: asphalt with world UVs, lane
/// dashes, rumble strips in the corners, low concrete barriers with a neon
/// edge, raised decks on pillars, the sunken shortcut with retaining walls,
/// and the start gantry with its countdown lights.
/// </summary>
public static class GymRaceTrackBuilder
{
    public const int RaceTrackLayer = 22;
    public const string RootName = "Race Track (Runtime)";
    public const float BarrierHeight = 0.95f;
    public const float BarrierThickness = 0.5f;
    public const float ColliderHeight = 2.6f;
    public const float DeckThickness = 0.8f;
    private const float EmbankmentMaxHeight = 3f;
    private const float PillarSpacing = 9f;
    private const float RumbleCurvature = 1f / 34f;
    // The ring slabs overlap at the corners: every slab over the trench is tiled.
    private static readonly string[] CityGroundNames =
    {
        "City Dystopia Ground West",
        "City Dystopia Ground North",
        "City Dystopia Ground South",
        "City Dystopia Ground East"
    };

    public static GymRaceTrack Build(Transform parent)
    {
        GameObject existing = GameObject.Find(RootName);
        if (existing != null)
        {
            Object.Destroy(existing);
        }

        GameObject root = new GameObject(RootName);
        if (parent != null)
        {
            root.transform.SetParent(parent, false);
        }
        GymRaceTrack track = root.AddComponent<GymRaceTrack>();
        Materials materials = Materials.Create();
        track.OwnedMaterials.AddRange(materials.All);

        GymRaceTrackPath main = GymRaceTrackLayout.Main;
        GymRaceTrackPath shortcut = GymRaceTrackLayout.Shortcut;

        BuildRoad(root.transform, main, shortcut, materials, track);
        BuildRoad(root.transform, shortcut, main, materials, track);
        BuildBarriers(root.transform, main, shortcut, materials, track);
        BuildBarriers(root.transform, shortcut, main, materials, track);
        BuildPillars(root.transform, main, materials);
        BuildMarkings(root.transform, main, materials, track, true);
        BuildMarkings(root.transform, shortcut, materials, track, false);
        BuildTrenchGround(root.transform, shortcut, materials, track);
        BuildGantry(root.transform, main, materials, track);
        BuildChevrons(root.transform, main, materials);

        Debug.Log(
            $"GYMCHAOS_RACE_TRACK_BUILT lap={main.Length:F1} shortcut={shortcut.Length:F1} " +
            $"samples={main.Count}+{shortcut.Count} startLine={GymRaceTrackLayout.StartLineDistance:F1} " +
            $"fork={GymRaceTrackLayout.ShortcutForkDistance:F1} merge={GymRaceTrackLayout.ShortcutMergeDistance:F1}");
        return track;
    }

    private sealed class Materials
    {
        public Material Asphalt;
        public Material Deck;
        public Material Concrete;
        public Material NeonCyan;
        public Material NeonMagenta;
        public Material Paint;
        public Material RumbleRed;
        public Material CheckerDark;
        public Material Gantry;
        public Material Chevron;
        public Material ChevronDark;
        public Material Ground;
        public Material LightOff;
        public readonly List<Material> All = new List<Material>();

        public static Materials Create()
        {
            Materials m = new Materials();
            Material sharedAsphalt = GymExteriorGroundUnifier.SharedGroundMaterial;
            m.Asphalt = sharedAsphalt != null
                ? sharedAsphalt
                : m.Lit("Race Asphalt", new Color(0.12f, 0.12f, 0.13f), 0f, 0.18f);
            m.Deck = m.Lit("Race Deck Concrete", new Color(0.32f, 0.33f, 0.35f), 0f, 0.1f);
            m.Concrete = m.Lit("Race Barrier Concrete", new Color(0.62f, 0.63f, 0.66f), 0f, 0.12f);
            m.NeonCyan = m.Unlit("Race Neon Cyan", new Color(0.15f, 0.95f, 1f));
            m.NeonMagenta = m.Unlit("Race Neon Magenta", new Color(1f, 0.2f, 0.75f));
            m.Paint = m.Lit("Race Paint White", new Color(0.92f, 0.92f, 0.9f), 0f, 0.3f);
            m.RumbleRed = m.Lit("Race Rumble Red", new Color(0.85f, 0.1f, 0.08f), 0f, 0.3f);
            m.CheckerDark = m.Lit("Race Checker Dark", new Color(0.04f, 0.04f, 0.045f), 0f, 0.2f);
            m.Gantry = m.Lit("Race Gantry", new Color(0.08f, 0.09f, 0.1f), 0.6f, 0.55f);
            m.Chevron = m.Unlit("Race Chevron Yellow", new Color(1f, 0.82f, 0.1f));
            m.ChevronDark = m.Unlit("Race Chevron Dark", new Color(0.05f, 0.05f, 0.05f));
            m.Ground = m.Lit("Race Trench Ground", GymOutdoorFoliage.CityPavingColor, 0f, 0.08f);
            m.LightOff = m.Lit("Race Light Off", new Color(0.05f, 0.05f, 0.06f), 0.2f, 0.7f);
            return m;
        }

        private Material Lit(string name, Color color, float metallic, float smoothness)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            Material material = new Material(shader) { name = name, color = color, enableInstancing = true };
            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", color);
            }
            if (material.HasProperty("_Metallic"))
            {
                material.SetFloat("_Metallic", metallic);
            }
            if (material.HasProperty("_Smoothness"))
            {
                material.SetFloat("_Smoothness", smoothness);
            }
            All.Add(material);
            return material;
        }

        private Material Unlit(string name, Color color)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
            Material material = new Material(shader) { name = name, color = color, enableInstancing = true };
            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", color);
            }
            All.Add(material);
            return material;
        }
    }

    // ----------------------------------------------------------------- road

    private static void BuildRoad(
        Transform root,
        GymRaceTrackPath path,
        GymRaceTrackPath other,
        Materials materials,
        GymRaceTrack track)
    {
        GymRaceMeshBuilder top = new GymRaceMeshBuilder();
        GymRaceMeshBuilder sides = new GymRaceMeshBuilder();
        // Where the branches overlap at the fork and merge, the shortcut sits a
        // few millimetres higher so the surfaces do not z-fight.
        float lift = path == GymRaceTrackLayout.Shortcut ? 0.006f : 0f;
        int segments = path.Closed ? path.Count : path.Count - 1;
        float tile = GymExteriorGroundUnifier.MetresPerTile;
        for (int i = 0; i < segments; i++)
        {
            int j = path.Wrap(i + 1);
            Vector3 l0 = Edge(path, i, -1f) + Vector3.up * lift;
            Vector3 r0 = Edge(path, i, 1f) + Vector3.up * lift;
            Vector3 l1 = Edge(path, j, -1f) + Vector3.up * lift;
            Vector3 r1 = Edge(path, j, 1f) + Vector3.up * lift;
            int a = top.AddVertex(l0, path.Ups[i], new Vector2(l0.x, l0.z) / tile);
            int b = top.AddVertex(l1, path.Ups[j], new Vector2(l1.x, l1.z) / tile);
            int c = top.AddVertex(r1, path.Ups[j], new Vector2(r1.x, r1.z) / tile);
            int d = top.AddVertex(r0, path.Ups[i], new Vector2(r0.x, r0.z) / tile);
            top.AddTriangle(a, b, c);
            top.AddTriangle(a, c, d);

            // Decks: sides and underside. Low ramps become solid embankments.
            float height0 = path.Points[i].y - GymRaceTrackLayout.FloorY;
            float height1 = path.Points[j].y - GymRaceTrackLayout.FloorY;
            if (height0 < 0.25f && height1 < 0.25f)
            {
                continue;
            }
            Vector3 down0 = Vector3.down * DeckDepth(height0);
            Vector3 down1 = Vector3.down * DeckDepth(height1);
            sides.AddQuad(l0 + down0, l1 + down1, l1, l0, 0, 4f);
            sides.AddQuad(r0, r1, r1 + down1, r0 + down0, 0, 4f);
            if (height0 >= EmbankmentMaxHeight || height1 >= EmbankmentMaxHeight)
            {
                sides.AddQuad(r0 + down0, r1 + down1, l1 + down1, l0 + down0, 0, 4f);
            }
        }

        Mesh topMesh = top.ToMesh(path.Name + " Surface");
        GameObject surface = CreateMeshObject(path.Name + " Surface", root, topMesh, materials.Asphalt, true);
        MeshCollider collider = surface.AddComponent<MeshCollider>();
        collider.sharedMesh = topMesh;
        track.OwnedMeshes.Add(topMesh);
        track.RoadColliders.Add(collider);

        if (sides.VertexCount > 0)
        {
            Mesh sideMesh = sides.ToMesh(path.Name + " Deck");
            CreateMeshObject(path.Name + " Deck", root, sideMesh, materials.Deck, false);
            track.OwnedMeshes.Add(sideMesh);
        }
    }

    private static float DeckDepth(float height)
    {
        // Embankments reach the ground; tall decks keep a slab on pillars.
        return height < EmbankmentMaxHeight ? Mathf.Max(height + 0.1f, 0.2f) : DeckThickness;
    }

    private static Vector3 Edge(GymRaceTrackPath path, int index, float side)
    {
        return path.Points[index] + path.Rights[index] * (side * path.HalfWidths[index]);
    }

    // ------------------------------------------------------------- barriers

    private static void BuildBarriers(
        Transform root,
        GymRaceTrackPath path,
        GymRaceTrackPath other,
        Materials materials,
        GymRaceTrack track)
    {
        GymRaceMeshBuilder concrete = new GymRaceMeshBuilder();
        GymRaceMeshBuilder neon = new GymRaceMeshBuilder(2);
        GymRaceMeshBuilder colliders = new GymRaceMeshBuilder();
        int segments = path.Closed ? path.Count : path.Count - 1;
        for (int sideIndex = 0; sideIndex < 2; sideIndex++)
        {
            float side = sideIndex == 0 ? -1f : 1f;
            bool[] keep = new bool[path.Count];
            for (int i = 0; i < path.Count; i++)
            {
                keep[i] = !InsideOtherRoad(path, i, side, other);
            }
            for (int i = 0; i < segments; i++)
            {
                int j = path.Wrap(i + 1);
                if (!keep[i] || !keep[j])
                {
                    continue;
                }
                BarrierProfile p0 = Profile(path, i, side);
                BarrierProfile p1 = Profile(path, j, side);
                // Quads are wound so the inner face points at the road.
                if (side < 0f)
                {
                    concrete.AddQuad(p0.InnerBottom, p0.InnerTop, p1.InnerTop, p1.InnerBottom, 0, 2f);
                    concrete.AddQuad(p0.InnerTop, p0.OuterTop, p1.OuterTop, p1.InnerTop, 0, 2f);
                    concrete.AddQuad(p1.OuterBottom, p1.OuterTop, p0.OuterTop, p0.OuterBottom, 0, 2f);
                    neon.AddQuad(p0.InnerTop + Vector3.up * 0.012f, p0.NeonOuter, p1.NeonOuter, p1.InnerTop + Vector3.up * 0.012f, 1);
                    colliders.AddQuad(p0.ColliderBottom, p0.ColliderTop, p1.ColliderTop, p1.ColliderBottom);
                }
                else
                {
                    concrete.AddQuad(p1.InnerBottom, p1.InnerTop, p0.InnerTop, p0.InnerBottom, 0, 2f);
                    concrete.AddQuad(p1.InnerTop, p1.OuterTop, p0.OuterTop, p0.InnerTop, 0, 2f);
                    concrete.AddQuad(p0.OuterBottom, p0.OuterTop, p1.OuterTop, p1.OuterBottom, 0, 2f);
                    neon.AddQuad(p1.InnerTop + Vector3.up * 0.012f, p1.NeonOuter, p0.NeonOuter, p0.InnerTop + Vector3.up * 0.012f, 0);
                    colliders.AddQuad(p1.ColliderBottom, p1.ColliderTop, p0.ColliderTop, p0.ColliderBottom);
                }
            }
        }

        Mesh concreteMesh = concrete.ToMesh(path.Name + " Barriers");
        CreateMeshObject(path.Name + " Barriers", root, concreteMesh, materials.Concrete, false);
        Mesh neonMesh = neon.ToMesh(path.Name + " Neon");
        GameObject neonObject = CreateMeshObject(path.Name + " Neon", root, neonMesh, materials.NeonCyan, false);
        neonObject.GetComponent<MeshRenderer>().sharedMaterials = new[] { materials.NeonCyan, materials.NeonMagenta };
        neonObject.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
        Mesh colliderMesh = colliders.ToMesh(path.Name + " Barrier Collider");
        GameObject colliderObject = new GameObject(path.Name + " Barrier Collider");
        colliderObject.layer = RaceTrackLayer;
        colliderObject.transform.SetParent(root, false);
        MeshCollider wallCollider = colliderObject.AddComponent<MeshCollider>();
        wallCollider.sharedMesh = colliderMesh;
        track.OwnedMeshes.Add(concreteMesh);
        track.OwnedMeshes.Add(neonMesh);
        track.OwnedMeshes.Add(colliderMesh);
        track.WallColliders.Add(wallCollider);
    }

    private struct BarrierProfile
    {
        public Vector3 InnerBottom;
        public Vector3 InnerTop;
        public Vector3 OuterTop;
        public Vector3 OuterBottom;
        public Vector3 NeonOuter;
        public Vector3 ColliderBottom;
        public Vector3 ColliderTop;
    }

    private static BarrierProfile Profile(GymRaceTrackPath path, int index, float side)
    {
        Vector3 edge = Edge(path, index, side);
        Vector3 flatOut = path.Rights[index];
        flatOut.y = 0f;
        flatOut = flatOut.normalized * side;
        float floor = GymRaceTrackLayout.FloorY;
        float height = path.Points[index].y - floor;
        bool sunken = height < -0.25f;
        float top = sunken ? Mathf.Max(edge.y + BarrierHeight, floor + BarrierHeight) : edge.y + BarrierHeight;
        float bottom = edge.y - (height >= 0.25f ? DeckDepth(height) : 0.3f);
        Vector3 outer = edge + flatOut * BarrierThickness;
        float outerBottom = sunken ? floor - 0.1f : bottom;
        return new BarrierProfile
        {
            InnerBottom = new Vector3(edge.x, bottom, edge.z),
            InnerTop = new Vector3(edge.x, top, edge.z),
            OuterTop = new Vector3(outer.x, top, outer.z),
            OuterBottom = new Vector3(outer.x, outerBottom, outer.z),
            NeonOuter = new Vector3(edge.x, top + 0.012f, edge.z) + flatOut * 0.14f,
            ColliderBottom = new Vector3(edge.x, edge.y - 0.4f, edge.z),
            ColliderTop = new Vector3(edge.x, Mathf.Max(edge.y, top - BarrierHeight) + ColliderHeight, edge.z)
        };
    }

    /// <summary>True when a barrier base point lies on the other branch's road (fork/merge).</summary>
    private static bool InsideOtherRoad(GymRaceTrackPath path, int index, float side, GymRaceTrackPath other)
    {
        Vector3 edge = Edge(path, index, side);
        float s = other.ClosestDistance(edge, -1f, 0f, out float lateral, out float verticalGap);
        if (Mathf.Abs(verticalGap) > 1.5f)
        {
            return false;
        }
        other.Sample(s, out Vector3 position, out _, out _, out float halfWidth);
        Vector2 flat = new Vector2(edge.x - position.x, edge.z - position.z);
        if (!other.Closed && (s <= 0.01f || s >= other.Length - 0.01f))
        {
            // Past the end of an open branch: measure to the end cap only.
            return flat.magnitude < halfWidth - 0.2f;
        }
        return Mathf.Abs(lateral) < halfWidth - 0.2f;
    }

    // -------------------------------------------------------------- pillars

    private static void BuildPillars(Transform root, GymRaceTrackPath path, Materials materials)
    {
        GameObject group = new GameObject("Race Deck Pillars");
        group.transform.SetParent(root, false);
        float nextAt = 0f;
        for (int i = 0; i < path.Count; i++)
        {
            float height = path.Points[i].y - GymRaceTrackLayout.FloorY;
            if (height < EmbankmentMaxHeight || path.Distances[i] < nextAt)
            {
                continue;
            }
            Vector3 top = path.Points[i] - Vector3.up * DeckThickness;
            Vector3 foot = new Vector3(top.x, GymRaceTrackLayout.FloorY, top.z);
            // Never stand a pillar on a lower road (the spiral exit under its entry).
            if (GymRaceTrackLayout.SignedDistanceToRoad(foot) < 1.2f &&
                LowerRoadBelow(foot, top.y))
            {
                continue;
            }
            nextAt = path.Distances[i] + PillarSpacing;
            GameObject pillar = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            pillar.name = "Race Pillar";
            Object.Destroy(pillar.GetComponent<Collider>());
            pillar.transform.SetParent(group.transform, false);
            float length = top.y - foot.y;
            pillar.transform.position = (top + foot) * 0.5f;
            pillar.transform.localScale = new Vector3(1.3f, length * 0.5f, 1.3f);
            MeshRenderer renderer = pillar.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = materials.Deck;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
        }
    }

    private static bool LowerRoadBelow(Vector3 foot, float deckY)
    {
        GymRaceTrackPath main = GymRaceTrackLayout.Main;
        for (int i = 0; i < main.Count; i++)
        {
            Vector3 p = main.Points[i];
            if (p.y > deckY - 2f)
            {
                continue;
            }
            float dx = p.x - foot.x;
            float dz = p.z - foot.z;
            float reach = main.HalfWidths[i] + BarrierThickness + 1.2f;
            if (dx * dx + dz * dz < reach * reach)
            {
                return true;
            }
        }
        return false;
    }

    // ------------------------------------------------------------- markings

    private static void BuildMarkings(
        Transform root,
        GymRaceTrackPath path,
        Materials materials,
        GymRaceTrack track,
        bool startLine)
    {
        // Sub-meshes: 0 white paint, 1 red rumble, 2 dark checker.
        GymRaceMeshBuilder marks = new GymRaceMeshBuilder(3);
        const float lift = 0.012f;
        int segments = path.Closed ? path.Count : path.Count - 1;

        // Dashed centre line: 3 m on, 3 m off.
        for (int i = 0; i < segments; i++)
        {
            int j = path.Wrap(i + 1);
            if (Mathf.FloorToInt(path.Distances[i] / 3f) % 2 != 0)
            {
                continue;
            }
            Vector3 up = path.Ups[i] * lift;
            Vector3 w0 = path.Rights[i] * 0.08f;
            Vector3 w1 = path.Rights[j] * 0.08f;
            marks.AddQuad(path.Points[i] - w0 + up, path.Points[j] - w1 + up, path.Points[j] + w1 + up, path.Points[i] + w0 + up, 0);
        }

        // Rumble strips inside both edges where the road turns tightly.
        for (int i = 0; i < segments; i++)
        {
            int j = path.Wrap(i + 1);
            if (Curvature(path, i) < RumbleCurvature)
            {
                continue;
            }
            int sub = Mathf.FloorToInt(path.Distances[i] / 1.5f) % 2 == 0 ? 1 : 0;
            for (int s = -1; s <= 1; s += 2)
            {
                Vector3 up = path.Ups[i] * (lift + 0.004f);
                Vector3 o0 = Edge(path, i, s);
                Vector3 o1 = Edge(path, j, s);
                Vector3 i0 = o0 - path.Rights[i] * (s * 0.9f);
                Vector3 i1 = o1 - path.Rights[j] * (s * 0.9f);
                if (s > 0)
                {
                    marks.AddQuad(i0 + up, i1 + up, o1 + up, o0 + up, sub);
                }
                else
                {
                    marks.AddQuad(o0 + up, o1 + up, i1 + up, i0 + up, sub);
                }
            }
        }

        if (startLine)
        {
            // Checkered start/finish line: 2 rows across the road.
            float s0 = GymRaceTrackLayout.StartLineDistance;
            path.Sample(s0, out Vector3 center, out Vector3 tangent, out Vector3 right, out float halfWidth);
            const int columns = 12;
            const float rowDepth = 0.75f;
            for (int row = 0; row < 2; row++)
            {
                for (int column = 0; column < columns; column++)
                {
                    float x0 = -halfWidth + column * (halfWidth * 2f / columns);
                    float x1 = x0 + halfWidth * 2f / columns;
                    float z0 = (row - 1) * rowDepth;
                    float z1 = z0 + rowDepth;
                    Vector3 up = Vector3.up * (lift + 0.008f);
                    int sub = (row + column) % 2 == 0 ? 0 : 2;
                    marks.AddQuad(
                        center + right * x0 + tangent * z0 + up,
                        center + right * x0 + tangent * z1 + up,
                        center + right * x1 + tangent * z1 + up,
                        center + right * x1 + tangent * z0 + up,
                        sub);
                }
            }
        }

        Mesh mesh = marks.ToMesh(path.Name + " Markings");
        GameObject marking = CreateMeshObject(path.Name + " Markings", root, mesh, materials.Paint, false);
        MeshRenderer renderer = marking.GetComponent<MeshRenderer>();
        renderer.sharedMaterials = new[] { materials.Paint, materials.RumbleRed, materials.CheckerDark };
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        track.OwnedMeshes.Add(mesh);
    }

    public static float Curvature(GymRaceTrackPath path, int index)
    {
        int a = path.Wrap(index - 3);
        int b = path.Wrap(index + 3);
        Vector3 ta = path.Tangents[a];
        Vector3 tb = path.Tangents[b];
        ta.y = 0f;
        tb.y = 0f;
        float angle = Vector3.Angle(ta, tb) * Mathf.Deg2Rad;
        float distance = Mathf.Abs(path.Distances[b] - path.Distances[a]);
        if (distance < 0.5f)
        {
            distance = 6f;
        }
        return angle / distance;
    }

    // --------------------------------------------------------- trench ground

    /// <summary>
    /// The city ground ring is one solid slab per side. While the race runs,
    /// every slab over the sunken shortcut is hidden and rebuilt as 1 m tiles
    /// that leave the trench open; the retaining walls cover the tile edges.
    /// </summary>
    private static void BuildTrenchGround(
        Transform root,
        GymRaceTrackPath shortcut,
        Materials materials,
        GymRaceTrack track)
    {
        foreach (string groundName in CityGroundNames)
        {
            GameObject slab = GameObject.Find(groundName);
            if (slab != null)
            {
                TileGroundSlab(root, slab, shortcut, track);
            }
        }
    }

    private static void TileGroundSlab(
        Transform root,
        GameObject west,
        GymRaceTrackPath shortcut,
        GymRaceTrack track)
    {
        Renderer westRenderer = west.GetComponent<Renderer>();
        if (westRenderer == null)
        {
            return;
        }
        Bounds bounds = westRenderer.bounds;
        float topY = bounds.max.y;

        // Cells where the shortcut road dips below the slab top stay open,
        // including the first shallow metres of the downhill.
        const float cell = 1f;
        int width = Mathf.CeilToInt(bounds.size.x / cell);
        int depth = Mathf.CeilToInt(bounds.size.z / cell);
        bool[] open = new bool[width * depth];
        int openCount = 0;
        for (int i = 0; i < shortcut.Count; i++)
        {
            Vector3 p = shortcut.Points[i];
            if (p.y > topY - 0.01f)
            {
                continue;
            }
            float radius = shortcut.HalfWidths[i] + BarrierThickness * 0.6f;
            int x0 = Mathf.Max(0, Mathf.FloorToInt((p.x - radius - bounds.min.x) / cell));
            int x1 = Mathf.Min(width - 1, Mathf.FloorToInt((p.x + radius - bounds.min.x) / cell));
            int z0 = Mathf.Max(0, Mathf.FloorToInt((p.z - radius - bounds.min.z) / cell));
            int z1 = Mathf.Min(depth - 1, Mathf.FloorToInt((p.z + radius - bounds.min.z) / cell));
            for (int z = z0; z <= z1; z++)
            {
                for (int x = x0; x <= x1; x++)
                {
                    float cx = bounds.min.x + (x + 0.5f) * cell - p.x;
                    float cz = bounds.min.z + (z + 0.5f) * cell - p.z;
                    if (cx * cx + cz * cz <= radius * radius && !open[z * width + x])
                    {
                        open[z * width + x] = true;
                        openCount++;
                    }
                }
            }
        }
        if (openCount == 0)
        {
            return;
        }

        // Merge closed cells into row runs to keep the mesh small.
        GymRaceMeshBuilder ground = new GymRaceMeshBuilder();
        for (int z = 0; z < depth; z++)
        {
            int x = 0;
            while (x < width)
            {
                if (open[z * width + x])
                {
                    x++;
                    continue;
                }
                int start = x;
                while (x < width && !open[z * width + x])
                {
                    x++;
                }
                float minX = bounds.min.x + start * cell;
                float maxX = Mathf.Min(bounds.max.x, bounds.min.x + x * cell);
                float minZ = bounds.min.z + z * cell;
                float maxZ = Mathf.Min(bounds.max.z, minZ + cell);
                ground.AddQuad(
                    new Vector3(minX, topY, minZ),
                    new Vector3(minX, topY, maxZ),
                    new Vector3(maxX, topY, maxZ),
                    new Vector3(maxX, topY, minZ),
                    0,
                    GymExteriorGroundUnifier.MetresPerTile);
            }
        }

        // Trench floor walls continue down to the road; the floor itself is the road.
        Mesh mesh = ground.ToMesh("Race " + west.name);
        GameObject replacement = CreateMeshObject("Race " + west.name, root, mesh, westRenderer.sharedMaterial, false);
        replacement.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
        track.OwnedMeshes.Add(mesh);
        track.HiddenRenderers.Add(westRenderer);
        westRenderer.enabled = false;
    }

    // --------------------------------------------------------------- gantry

    private static void BuildGantry(Transform root, GymRaceTrackPath path, Materials materials, GymRaceTrack track)
    {
        path.Sample(GymRaceTrackLayout.StartLineDistance, out Vector3 center, out Vector3 tangent, out Vector3 right, out float halfWidth);
        GameObject gantry = new GameObject("Race Start Gantry");
        gantry.transform.SetParent(root, false);
        gantry.transform.SetPositionAndRotation(center, Quaternion.LookRotation(tangent, Vector3.up));
        float span = halfWidth + BarrierThickness + 0.4f;
        const float height = 6.4f;
        for (int s = -1; s <= 1; s += 2)
        {
            Box(gantry.transform, "Gantry Post", new Vector3(s * span, height * 0.5f, 0f), new Vector3(0.45f, height, 0.45f), materials.Gantry, true);
        }
        Box(gantry.transform, "Gantry Beam", new Vector3(0f, height, 0f), new Vector3(span * 2f + 0.45f, 0.9f, 0.6f), materials.Gantry, false);
        Box(gantry.transform, "Gantry Neon", new Vector3(0f, height - 0.47f, -0.31f), new Vector3(span * 2f, 0.06f, 0.02f), materials.NeonMagenta, false);
        // Checkered banner on the beam face toward the grid.
        const int squares = 16;
        float bannerWidth = span * 2f - 0.6f;
        for (int row = 0; row < 2; row++)
        {
            for (int column = 0; column < squares; column++)
            {
                float x = -bannerWidth * 0.5f + (column + 0.5f) * bannerWidth / squares;
                Material material = (row + column) % 2 == 0 ? materials.Paint : materials.CheckerDark;
                Box(gantry.transform, "Banner", new Vector3(x, height + 0.2f - row * 0.4f, -0.32f),
                    new Vector3(bannerWidth / squares, 0.4f, 0.02f), material, false);
            }
        }

        // Countdown lights: three red then one green, facing the grid.
        Box(gantry.transform, "Light Housing", new Vector3(0f, height - 1.05f, -0.1f), new Vector3(3.6f, 0.95f, 0.4f), materials.Gantry, false);
        for (int i = 0; i < 4; i++)
        {
            GameObject lamp = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            lamp.name = "Countdown Light " + i;
            Object.Destroy(lamp.GetComponent<Collider>());
            lamp.transform.SetParent(gantry.transform, false);
            lamp.transform.localPosition = new Vector3(-1.2f + i * 0.8f, height - 1.05f, -0.32f);
            lamp.transform.localScale = Vector3.one * 0.55f;
            MeshRenderer renderer = lamp.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = materials.LightOff;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            track.CountdownLights.Add(renderer);
        }
        track.LightOffMaterial = materials.LightOff;
    }

    private static GameObject Box(Transform parent, string name, Vector3 localPosition, Vector3 size, Material material, bool collider)
    {
        GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
        box.name = name;
        box.transform.SetParent(parent, false);
        box.transform.localPosition = localPosition;
        box.transform.localScale = size;
        if (collider)
        {
            box.layer = RaceTrackLayer;
        }
        else
        {
            Object.Destroy(box.GetComponent<Collider>());
        }
        MeshRenderer renderer = box.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        return box;
    }

    // ------------------------------------------------------------- chevrons

    private static void BuildChevrons(Transform root, GymRaceTrackPath path, Materials materials)
    {
        // Boards on the outside of the sharp south-west turn and the spiral
        // entry, arrows facing the road and pointing the way the lap runs.
        PlaceChevronRun(root, path, new Vector3(95f, 0f, -69.5f), materials);
        PlaceChevronRun(root, path, new Vector3(GymRaceTrackLayout.SpiralCenter.x, 0f, GymRaceTrackLayout.SpiralCenter.y + GymRaceTrackLayout.SpiralRadius), materials);
        PlaceChevronRun(root, path, new Vector3(210.8f, 0f, 37.5f), materials);
    }

    private static void PlaceChevronRun(Transform root, GymRaceTrackPath path, Vector3 near, Materials materials)
    {
        float apex = path.ClosestDistance(new Vector3(near.x, path.Points[0].y, near.z));
        path.Sample(apex, out _, out Vector3 tangent, out _, out _);
        // Outside of the turn: the side away from where the road is heading.
        path.Sample(apex + 6f, out Vector3 ahead, out _, out _, out _);
        path.Sample(apex, out Vector3 here, out _, out Vector3 right, out float halfWidth);
        float side = Vector3.Dot(ahead - here, right) > 0f ? -1f : 1f;
        for (int k = -2; k <= 2; k++)
        {
            float s = apex + k * 4.5f;
            path.Sample(s, out Vector3 position, out Vector3 t, out Vector3 r, out float hw);
            Vector3 flatOut = new Vector3(r.x, 0f, r.z).normalized * side;
            Vector3 basePoint = position + flatOut * (hw + BarrierThickness * 0.5f);
            GameObject board = new GameObject("Race Chevron");
            board.transform.SetParent(root, false);
            board.transform.SetPositionAndRotation(
                basePoint + Vector3.up * (BarrierHeight + 0.75f),
                Quaternion.LookRotation(-flatOut, Vector3.up));
            Box(board.transform, "Chevron Board", Vector3.zero, new Vector3(1.6f, 1.1f, 0.08f), materials.ChevronDark, false);
            // Seen from the road the board's +X runs to the viewer's left: the
            // arrow points the way the lap continues.
            float direction = Vector3.Dot(t, Vector3.Cross(Vector3.up, -flatOut)) >= 0f ? -1f : 1f;
            for (int stroke = 0; stroke < 2; stroke++)
            {
                float sign = stroke == 0 ? 1f : -1f;
                // The board faces the road (+Z): strokes on that face.
                GameObject bar = Box(board.transform, "Chevron Stroke", new Vector3(0f, sign * 0.2f, 0.05f), new Vector3(0.75f, 0.17f, 0.02f), materials.Chevron, false);
                bar.transform.localRotation = Quaternion.Euler(0f, 0f, -sign * 40f * direction);
            }
            Box(board.transform, "Chevron Post", new Vector3(0f, -1.1f, -0.05f), new Vector3(0.12f, 1.2f, 0.12f), materials.Gantry, false);
        }
    }

    private static GameObject CreateMeshObject(string name, Transform parent, Mesh mesh, Material material, bool collider)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        if (collider)
        {
            go.layer = RaceTrackLayer;
        }
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        MeshRenderer renderer = go.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = true;
        return go;
    }
}

/// <summary>Runtime handle of a built race track; destroys everything it owns.</summary>
public sealed class GymRaceTrack : MonoBehaviour
{
    public readonly List<Mesh> OwnedMeshes = new List<Mesh>();
    public readonly List<Material> OwnedMaterials = new List<Material>();
    public readonly List<MeshCollider> RoadColliders = new List<MeshCollider>();
    public readonly List<MeshCollider> WallColliders = new List<MeshCollider>();
    public readonly List<Renderer> HiddenRenderers = new List<Renderer>();
    public readonly List<MeshRenderer> CountdownLights = new List<MeshRenderer>();
    public Material LightOffMaterial;
    private Material redLight;
    private Material greenLight;

    /// <summary>0..3 red lamps lit; <paramref name="go"/> lights the green one and clears the reds.</summary>
    public void SetCountdownLights(int redCount, bool go)
    {
        if (redLight == null)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
            redLight = new Material(shader) { name = "Race Light Red", color = new Color(1f, 0.12f, 0.08f) };
            greenLight = new Material(shader) { name = "Race Light Green", color = new Color(0.2f, 1f, 0.3f) };
            redLight.SetColor("_BaseColor", redLight.color);
            greenLight.SetColor("_BaseColor", greenLight.color);
            OwnedMaterials.Add(redLight);
            OwnedMaterials.Add(greenLight);
        }
        for (int i = 0; i < CountdownLights.Count; i++)
        {
            Material material = LightOffMaterial;
            if (go && i == CountdownLights.Count - 1)
            {
                material = greenLight;
            }
            else if (!go && i < redCount && i < CountdownLights.Count - 1)
            {
                material = redLight;
            }
            CountdownLights[i].sharedMaterial = material;
        }
    }

    private void OnDestroy()
    {
        for (int i = 0; i < HiddenRenderers.Count; i++)
        {
            if (HiddenRenderers[i] != null)
            {
                HiddenRenderers[i].enabled = true;
            }
        }
        for (int i = 0; i < OwnedMeshes.Count; i++)
        {
            if (OwnedMeshes[i] != null)
            {
                Destroy(OwnedMeshes[i]);
            }
        }
        for (int i = 0; i < OwnedMaterials.Count; i++)
        {
            // The shared exterior asphalt is not ours to destroy.
            if (OwnedMaterials[i] != null && OwnedMaterials[i] != GymExteriorGroundUnifier.SharedGroundMaterial)
            {
                Destroy(OwnedMaterials[i]);
            }
        }
    }
}
