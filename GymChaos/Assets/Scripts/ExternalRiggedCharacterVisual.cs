using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Loads externally MIA-rigged, Mixamo-compatible character bodies. The
/// original GLB runtime path remains available when a validated FBX is absent.
/// </summary>
public sealed class ExternalRiggedCharacterVisual : MonoBehaviour
{
    private static readonly Dictionary<BodybuilderIdentity, string> ResourcePaths =
        new Dictionary<BodybuilderIdentity, string>
        {
            { BodybuilderIdentity.Arnold, "Characters/Enemies/arnold_mixamo_rigged" },
            { BodybuilderIdentity.Cbum, "Characters/Enemies/cbum_mixamo_rigged" },
            { BodybuilderIdentity.Zyzz, "Characters/Enemies/zyzz_mixamo_rigged" },
            { BodybuilderIdentity.Ronnie, "Characters/Enemies/ronnie_mixamo_rigged" },
            { BodybuilderIdentity.JayCutler, "Characters/Enemies/jay_mixamo_rigged" },
            { BodybuilderIdentity.Goku, "Characters/Enemies/goku_mixamo_rigged" },
            { BodybuilderIdentity.Manwithsuit1, "Characters/Reception/manwithsuit1_mixamo_rigged" }
        };

    public static bool TryBuild(GameObject owner, BodybuilderIdentity identity)
    {
        if (!ResourcePaths.TryGetValue(identity, out string resourcePath))
        {
            return false;
        }

        GameObject prefab = Resources.Load<GameObject>(resourcePath);
        if (prefab == null)
        {
            return false;
        }

        ExternalRiggedCharacterVisual visual = owner.AddComponent<ExternalRiggedCharacterVisual>();
        return visual.Build(prefab, identity, resourcePath);
    }

    private bool Build(GameObject prefab, BodybuilderIdentity identity, string resourcePath)
    {
        GameObject modelRoot = Instantiate(prefab, transform);
        modelRoot.name = identity + " External Mixamo Rig";
        modelRoot.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);

        Animator[] unityAnimators = modelRoot.GetComponentsInChildren<Animator>(true);
        for (int i = 0; i < unityAnimators.Length; i++)
        {
            unityAnimators[i].enabled = false;
        }

        SkinnedMeshRenderer renderer = FindPrimaryRenderer(modelRoot);
        if (renderer == null)
        {
            Destroy(modelRoot);
            Destroy(this);
            return false;
        }

        FitToGameplayHeight(modelRoot.transform, renderer, identity);
        PreserveImportedTextures(modelRoot, identity);

        BodybuilderEnemyVisual.Rig rig = BuildRig(modelRoot.transform, renderer);
        if (!HasRequiredBones(rig))
        {
            Debug.LogError($"{identity} external FBX is missing required Mixamo bones.", this);
            Destroy(modelRoot);
            Destroy(this);
            return false;
        }

        EnemyMeshHitboxRig.Configure(gameObject, rig, renderer);
        bool neutralNpc = identity == BodybuilderIdentity.Manwithsuit1;
        BodybuilderEnemyVisual.ConfigureImportedVisual(
            modelRoot.transform, renderer, rig, identity, neutralNpc);

        if (neutralNpc)
        {
            ManWithSuitIdleAnimator idleAnimator = gameObject.AddComponent<ManWithSuitIdleAnimator>();
            idleAnimator.Configure(rig);
        }
        else
        {
            ExternalRiggedCharacterAnimator animator =
                gameObject.AddComponent<ExternalRiggedCharacterAnimator>();
            animator.Configure(modelRoot, rig, identity, resourcePath);
        }

        if (identity == BodybuilderIdentity.Goku)
        {
            GokuAura aura = gameObject.GetComponent<GokuAura>();
            if (aura == null)
            {
                aura = gameObject.AddComponent<GokuAura>();
            }
            aura.Configure(renderer);
        }

        Bounds verifiedBounds = CalculateBakedWorldBounds(renderer);
        Texture verifiedTexture = renderer.sharedMaterial != null
            ? renderer.sharedMaterial.GetTexture("_BaseMap")
            : null;
        int triangleCount = renderer.sharedMesh != null
            ? renderer.sharedMesh.triangles.Length / 3
            : 0;
        Debug.Log(
            $"GYMCHAOS_EXTERNAL_RIG_OK identity={identity} resource={resourcePathForLog(identity)} " +
            $"height={verifiedBounds.size.y:F3} triangles={triangleCount} " +
            $"texture={(verifiedTexture != null ? verifiedTexture.name : "missing")}");
        return true;
    }

    private static string resourcePathForLog(BodybuilderIdentity identity)
    {
        return ResourcePaths.TryGetValue(identity, out string value) ? value : "missing";
    }

    private static SkinnedMeshRenderer FindPrimaryRenderer(GameObject modelRoot)
    {
        SkinnedMeshRenderer[] renderers = modelRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        SkinnedMeshRenderer best = null;
        int bestVertices = -1;
        for (int i = 0; i < renderers.Length; i++)
        {
            int vertices = renderers[i] != null && renderers[i].sharedMesh != null
                ? renderers[i].sharedMesh.vertexCount
                : 0;
            if (vertices > bestVertices)
            {
                best = renderers[i];
                bestVertices = vertices;
            }
        }
        return best;
    }

    private static void FitToGameplayHeight(
        Transform modelRoot, SkinnedMeshRenderer renderer, BodybuilderIdentity identity)
    {
        float targetHeight = identity == BodybuilderIdentity.Manwithsuit1
            ? 1.82f * 1.125f
            : identity == BodybuilderIdentity.Arnold ? 2.35f : 2.3f;
        Bounds sourceBounds = CalculateBakedWorldBounds(renderer);
        float sourceHeight = Mathf.Max(0.01f, sourceBounds.size.y);
        float scale = targetHeight / sourceHeight;
        modelRoot.localScale = Vector3.one * scale;
        Physics.SyncTransforms();

        Bounds scaledBounds = CalculateBakedWorldBounds(renderer);
        float floorOffset = 0.02f - scaledBounds.min.y;
        modelRoot.position += Vector3.up * floorOffset;
        Physics.SyncTransforms();
    }

    private static Bounds CalculateBakedWorldBounds(SkinnedMeshRenderer renderer)
    {
        // FBX skinning matrices already include the importer scale. Applying
        // TransformPoint to BakeMesh vertices scales these scans a second time.
        // Unity's renderer bounds are the authoritative world-space result.
        return renderer.bounds;
    }

    private static void PreserveImportedTextures(GameObject modelRoot, BodybuilderIdentity identity)
    {
        Shader shader = Shader.Find("GymChaos/BodybuilderUnlit");
        if (shader == null)
        {
            shader = Shader.Find("Universal Render Pipeline/Unlit");
        }
        Texture2D originalTexture = Resources.Load<Texture2D>(
            "Characters/Textures/" + GetTextureResourceName(identity));

        SkinnedMeshRenderer[] renderers = modelRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        for (int rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
        {
            SkinnedMeshRenderer renderer = renderers[rendererIndex];
            Material[] sourceMaterials = renderer.sharedMaterials;
            Material[] materials = new Material[sourceMaterials.Length];
            for (int materialIndex = 0; materialIndex < sourceMaterials.Length; materialIndex++)
            {
                Material source = sourceMaterials[materialIndex];
                Texture texture = originalTexture != null
                    ? originalTexture
                    : source != null
                    ? source.GetTexture("_BaseMap") ?? source.GetTexture("_MainTex")
                    : null;
                Color color = source != null && source.HasProperty("_BaseColor")
                    ? source.GetColor("_BaseColor")
                    : source != null && source.HasProperty("_Color")
                        ? source.color
                        : Color.white;
                Material material = new Material(shader)
                {
                    name = $"{identity} External Body Material {materialIndex}"
                };
                material.SetColor("_BaseColor", texture != null ? Color.white : color);
                material.SetColor("_Color", texture != null ? Color.white : color);
                if (texture != null)
                {
                    material.SetTexture("_BaseMap", texture);
                    material.SetTexture("_MainTex", texture);
                }
                materials[materialIndex] = material;
            }
            renderer.sharedMaterials = materials;
            renderer.enabled = true;
            renderer.updateWhenOffscreen = true;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }
    }

    private static string GetTextureResourceName(BodybuilderIdentity identity)
    {
        switch (identity)
        {
            case BodybuilderIdentity.JayCutler:
                return "jay";
            case BodybuilderIdentity.Manwithsuit1:
                return "manwithsuit1";
            default:
                return identity.ToString().ToLowerInvariant();
        }
    }

    private static BodybuilderEnemyVisual.Rig BuildRig(
        Transform modelRoot, SkinnedMeshRenderer renderer)
    {
        Transform[] bones = modelRoot.GetComponentsInChildren<Transform>(true);
        BodybuilderEnemyVisual.Rig rig = new BodybuilderEnemyVisual.Rig
        {
            Root = renderer.rootBone != null ? renderer.rootBone : modelRoot,
            Hips = FindBone(bones, "hips"),
            Spine = FindBone(bones, "spine"),
            Chest = FindBone(bones, "spine2", "spine1"),
            Head = FindBone(bones, "head"),
            LeftUpperArm = FindBone(bones, "leftarm"),
            LeftForearm = FindBone(bones, "leftforearm"),
            LeftHand = FindBone(bones, "lefthand"),
            RightUpperArm = FindBone(bones, "rightarm"),
            RightForearm = FindBone(bones, "rightforearm"),
            RightHand = FindBone(bones, "righthand"),
            LeftThigh = FindBone(bones, "leftupleg"),
            LeftShin = FindBone(bones, "leftleg"),
            RightThigh = FindBone(bones, "rightupleg"),
            RightShin = FindBone(bones, "rightleg")
        };

        Transform leftFoot = FindBone(bones, "leftfoot");
        Transform rightFoot = FindBone(bones, "rightfoot");
        rig.LeftHandPosition = renderer.transform.InverseTransformPoint(
            rig.LeftHand != null ? rig.LeftHand.position : renderer.bounds.center);
        rig.RightHandPosition = renderer.transform.InverseTransformPoint(
            rig.RightHand != null ? rig.RightHand.position : renderer.bounds.center);
        rig.LeftFootPosition = renderer.transform.InverseTransformPoint(
            leftFoot != null ? leftFoot.position :
            rig.LeftShin != null ? rig.LeftShin.position : renderer.bounds.min);
        rig.RightFootPosition = renderer.transform.InverseTransformPoint(
            rightFoot != null ? rightFoot.position :
            rig.RightShin != null ? rig.RightShin.position : renderer.bounds.min);
        return rig;
    }

    private static bool HasRequiredBones(BodybuilderEnemyVisual.Rig rig)
    {
        return rig.Hips != null && rig.Spine != null && rig.Chest != null && rig.Head != null &&
            rig.LeftUpperArm != null && rig.LeftForearm != null && rig.LeftHand != null &&
            rig.RightUpperArm != null && rig.RightForearm != null && rig.RightHand != null &&
            rig.LeftThigh != null && rig.LeftShin != null &&
            rig.RightThigh != null && rig.RightShin != null;
    }

    private static Transform FindBone(Transform[] bones, params string[] candidates)
    {
        for (int i = 0; i < bones.Length; i++)
        {
            string normalized = NormalizeBoneName(bones[i].name);
            for (int candidateIndex = 0; candidateIndex < candidates.Length; candidateIndex++)
            {
                if (normalized == candidates[candidateIndex] ||
                    normalized.EndsWith(candidates[candidateIndex], StringComparison.Ordinal))
                {
                    return bones[i];
                }
            }
        }
        return null;
    }

    private static string NormalizeBoneName(string value)
    {
        return value.Replace("mixamorig:", string.Empty)
            .Replace("mixamorig", string.Empty)
            .Replace("_", string.Empty)
            .Replace(" ", string.Empty)
            .ToLowerInvariant();
    }
}
