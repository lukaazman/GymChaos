using System.Collections.Generic;
using UnityEngine;

public static class BloodSplatter
{
    private static Material bloodMaterial;

    public static float GetHeldShoveScale(WeightType type, float mass)
    {
        switch (type)
        {
            case WeightType.Barbell:
                return 1.7f;
            case WeightType.EzBar:
                return 1.15f;
            case WeightType.Plate5:
                return 0.58f;
            case WeightType.Plate:
                return 0.68f;
            case WeightType.Plate10:
                return 0.78f;
            case WeightType.Plate20:
                return 0.96f;
            default:
                return Mathf.Clamp(0.48f + mass * 0.025f, 0.5f, 1f);
        }
    }

    public static float GetThrownScale(WeightType type, float mass)
    {
        switch (type)
        {
            case WeightType.Barbell:
                return 2.5f;
            case WeightType.EzBar:
                return 1.65f;
            case WeightType.Plate5:
                return 1.15f;
            case WeightType.Plate:
                return 1.35f;
            case WeightType.Plate10:
                return 1.58f;
            case WeightType.Plate20:
                return 2.05f;
            default:
                return Mathf.Clamp(1f + mass * 0.04f, 1.1f, 2.2f);
        }
    }

    public static void Spawn(Vector3 point, Vector3 surfaceNormal, float scale, Transform stainParent = null)
    {
        if (scale <= 0f)
        {
            return;
        }

        Vector3 normal = surfaceNormal.sqrMagnitude > 0.001f ? surfaceNormal.normalized : Vector3.up;
        EnsureMaterial();
        CreateBurst(point, normal, scale);
        CreateStain(point, normal, scale, stainParent);
    }

    public static void SpawnOnBody(
        EnemyFighter fighter, Vector3 approximatePoint, Vector3 approximateNormal,
        float scale, Transform movingSurface = null)
    {
        if (fighter == null)
        {
            return;
        }

        Vector3 point = approximatePoint;
        Vector3 normal = approximateNormal;
        EnemyMeshHitboxRig hitboxes = fighter.GetComponent<EnemyMeshHitboxRig>();
        if (hitboxes != null)
        {
            hitboxes.TrySnapToSurface(approximatePoint, out point, out normal);
        }
        Spawn(point, normal, scale, movingSurface != null ? movingSurface : fighter.transform);
    }

    private static void EnsureMaterial()
    {
        if (bloodMaterial != null)
        {
            return;
        }
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null)
        {
            shader = Shader.Find("Unlit/Color");
        }
        bloodMaterial = new Material(shader)
        {
            name = "Blood Splatter Material",
            color = new Color(0.34f, 0.005f, 0.008f, 1f),
            hideFlags = HideFlags.DontSave
        };
        if (bloodMaterial.HasProperty("_BaseColor"))
        {
            bloodMaterial.SetColor("_BaseColor", bloodMaterial.color);
        }
    }

    private static void CreateBurst(Vector3 point, Vector3 normal, float scale)
    {
        GameObject burstObject = new GameObject("Blood impact burst");
        burstObject.transform.SetPositionAndRotation(point + normal * 0.004f, Quaternion.LookRotation(normal));
        ParticleSystem particles = burstObject.AddComponent<ParticleSystem>();
        particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        ParticleSystem.MainModule main = particles.main;
        main.duration = 0.22f;
        main.loop = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.32f, 0.7f + scale * 0.12f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(1.4f * scale, 3.7f * scale);
        main.startSize = new ParticleSystem.MinMaxCurve(0.018f * scale, 0.06f * scale);
        main.startColor = new ParticleSystem.MinMaxGradient(
            new Color(0.22f, 0.002f, 0.004f, 1f),
            new Color(0.55f, 0.01f, 0.012f, 1f));
        main.gravityModifier = 1.15f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 80;

        ParticleSystem.EmissionModule emission = particles.emission;
        emission.enabled = false;
        ParticleSystem.ShapeModule shape = particles.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = Mathf.Lerp(18f, 42f, Mathf.Clamp01(scale / 2.5f));
        shape.radius = 0.014f * scale;

        ParticleSystemRenderer renderer = particles.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = bloodMaterial;
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;

        particles.Emit(Mathf.RoundToInt(Mathf.Lerp(10f, 42f, Mathf.Clamp01(scale / 2.5f))));
        Object.Destroy(burstObject, 2.2f);
    }

    private static void CreateStain(Vector3 point, Vector3 normal, float scale, Transform parent)
    {
        const int rimCount = 14;
        List<Vector3> vertices = new List<Vector3>(rimCount + 1) { Vector3.zero };
        List<int> triangles = new List<int>(rimCount * 3);
        float baseRadius = 0.085f * Mathf.Clamp(scale, 0.55f, 2.5f);
        for (int i = 0; i < rimCount; i++)
        {
            float angle = i * Mathf.PI * 2f / rimCount;
            float irregular = 0.62f + Mathf.PerlinNoise(i * 0.71f, scale * 3.17f) * 0.7f;
            vertices.Add(new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * baseRadius * irregular);
            triangles.Add(0);
            triangles.Add(i + 1);
            triangles.Add(((i + 1) % rimCount) + 1);
        }

        Mesh mesh = new Mesh { name = "Procedural blood stain" };
        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        GameObject stain = new GameObject("Blood stain");
        stain.transform.SetPositionAndRotation(point + normal * 0.0025f, Quaternion.LookRotation(normal));
        if (parent != null)
        {
            stain.transform.SetParent(parent, true);
        }
        stain.AddComponent<MeshFilter>().sharedMesh = mesh;
        stain.AddComponent<MeshRenderer>().sharedMaterial = bloodMaterial;
        Object.Destroy(stain, 14f);
        Object.Destroy(mesh, 14f);
    }
}
