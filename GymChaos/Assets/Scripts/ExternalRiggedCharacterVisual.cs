using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Loads the per-enemy T-pose scan FBX whose mesh, UVs, fitted skeleton and
/// animation clips share one bind hierarchy.  The matching base-color image
/// from Assets/BodyBuilders/enemies is rebound explicitly so the visible
/// material stays on the same UV layout as the animated scan.
/// </summary>
[DefaultExecutionOrder(1000)]
public sealed class ExternalRiggedCharacterVisual : MonoBehaviour
{
    public const float StandardGameplayHeight = 2.30f;
    public const float ArnoldGameplayHeight = 2.35f;

    public float GroundContactError => runtimeRenderer == null
        ? float.PositiveInfinity
        : Mathf.Abs(GetGroundContactY() - (transform.position.y + 0.02f));
    public float GroundContactY => GetGroundContactY();
    public float GroundedFootToMeshOffset => groundedFootToMeshOffset;
    public bool HasGroundedFootReference => hasGroundedFootReference;

    private Transform runtimeModelRoot;
    private SkinnedMeshRenderer runtimeRenderer;
    private BodybuilderIdentity runtimeIdentity;
    private Transform groundedLeftFoot;
    private Transform groundedRightFoot;
    private float groundedFootToMeshOffset;
    private bool hasGroundedFootReference;
    private int groundingSettleFrames;
    private bool groundingSettled;
    private bool initialGroundingCorrectionApplied;
    private int heightCorrectionFrames;
    private bool dynamicHeightCorrection;
    private bool heightCorrectionLogged;

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

        ApplyUprightRestPosture(modelRoot.transform, rig, identity);
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
            // Sample the clips through the same per-enemy hierarchy that is
            // visible on screen. No generic shoulder/arm rig is synthesized,
            // so each scan keeps the proportions of its own T-pose bind.
            MixamoScanRetargetAnimator animator =
                gameObject.AddComponent<MixamoScanRetargetAnimator>();
            if (!animator.Configure(identity, rig))
            {
                Debug.LogError(
                    $"Final rigged FBX animation setup failed for {identity}.", this);
                Destroy(modelRoot);
                Destroy(animator);
                Destroy(this);
                return false;
            }
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

        // Imported FBX renderer bounds are not always refreshed until the
        // first rendered frame.  FitToGameplayHeight therefore establishes
        // the initial scale, and this short post-import pass corrects against
        // the real runtime bounds so every enemy matches the player height.
        runtimeModelRoot = modelRoot.transform;
        runtimeRenderer = renderer;
        runtimeIdentity = identity;
        groundedLeftFoot = rig.LeftFoot;
        groundedRightFoot = rig.RightFoot;
        heightCorrectionFrames = identity == BodybuilderIdentity.Manwithsuit1 ? 0 : 4;
        // Settle the imported renderer for a few grounded frames, then leave
        // the child model transform alone. Rewriting it every frame from an
        // animated pose makes an idle/punch scan float when its AABB changes;
        // the owner Rigidbody is now floor-locked while alive.
        dynamicHeightCorrection = false;
        heightCorrectionLogged = false;
        groundingSettleFrames = 12;
        groundingSettled = false;
        initialGroundingCorrectionApplied = false;

        Bounds verifiedBounds = CalculateBakedWorldBounds(renderer);
        float lowestFootY = GetLowestFootY();
        if (lowestFootY < float.PositiveInfinity)
        {
            groundedFootToMeshOffset = verifiedBounds.min.y - lowestFootY;
            hasGroundedFootReference = true;
        }
        else
        {
            groundedFootToMeshOffset = 0f;
            hasGroundedFootReference = false;
        }
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

    private void LateUpdate()
    {
        if (runtimeModelRoot == null || runtimeRenderer == null)
        {
            return;
        }

        // Do not measure Goku while the flight pose is rotated onto its
        // horizontal axis; its world-Y bounds are intentionally only the body
        // thickness in that state.  Wait for a grounded/idle pose instead.
        EnemyFighter fighter = GetComponent<EnemyFighter>();
        if (runtimeIdentity == BodybuilderIdentity.Goku && fighter != null && fighter.IsGokuFlightActive)
        {
            return;
        }

        if (heightCorrectionFrames > 0)
        {
            heightCorrectionFrames--;
            float measuredHeight = runtimeRenderer.bounds.size.y;
            if (measuredHeight > 0.01f)
            {
                float correction = GetGameplayHeight(runtimeIdentity) / measuredHeight;
                if (Mathf.Abs(correction - 1f) > 0.001f)
                {
                    runtimeModelRoot.localScale *= correction;
                    Physics.SyncTransforms();
                }
            }
        }

        // Every grounded pose can change the skinned AABB. The run clip is
        // allowed to animate the legs, but it must not lift the whole visible
        // character off the enemy root's floor while the visitor is walking.
        // Goku is the only intentional airborne exception.
        if (fighter == null || (!fighter.IsDead && !fighter.IsGokuFlightActive))
        {
            KeepVisibleModelOnFloor();
        }

        if (!heightCorrectionLogged &&
            (dynamicHeightCorrection || heightCorrectionFrames == 0))
        {
            Debug.Log(
                $"GYMCHAOS_EXTERNAL_RIG_HEIGHT_OK identity={runtimeIdentity} " +
                $"height={runtimeRenderer.bounds.size.y:F3}", this);
            heightCorrectionLogged = true;
        }
    }

    private void KeepVisibleModelOnFloor()
    {
        if (runtimeRenderer == null || runtimeRenderer.bounds.size.y < 0.1f)
        {
            return;
        }

        // Only settle the imported child once, while its first idle pose and
        // height correction are becoming authoritative. After that initial
        // pass, only a clear airborne offset is corrected; lifting the model
        // back up on every stride would recreate the walking shake.
        if (groundingSettled)
        {
            float settledFloorY = transform.position.y + 0.02f;
            float settledFloorOffset = settledFloorY - GetGroundContactY();
            if (settledFloorOffset < -0.12f)
            {
                // Correct the visual child, not the enemy root, so navigation
                // and Rigidbody movement remain stable while the visible rig
                // cannot continue walking above the floor.
                runtimeModelRoot.position += Vector3.up * settledFloorOffset;
                Physics.SyncTransforms();
            }
            return;
        }

        float floorY = transform.position.y + 0.02f;
        float floorOffset = floorY - GetGroundContactY();
        const float correctionThreshold = 0.18f;
        if (!initialGroundingCorrectionApplied &&
            Mathf.Abs(floorOffset) > correctionThreshold)
        {
            runtimeModelRoot.position += Vector3.up * floorOffset;
            initialGroundingCorrectionApplied = true;
            Physics.SyncTransforms();
            if (Mathf.Abs(floorOffset) > 0.08f)
            {
                Debug.Log(
                    $"GYMCHAOS_GROUNDING_CORRECTED identity={runtimeIdentity} " +
                    $"offset={floorOffset:0.000} floorY={floorY:0.000}",
                    this);
            }
        }

        groundingSettleFrames--;
        if (groundingSettleFrames <= 0)
        {
            groundingSettled = true;
        }
    }

    private float GetGroundContactY()
    {
        if (runtimeRenderer == null)
        {
            return float.PositiveInfinity;
        }

        float lowestFootY = GetLowestFootY();
        if (hasGroundedFootReference && lowestFootY < float.PositiveInfinity)
        {
            return lowestFootY + groundedFootToMeshOffset;
        }

        return runtimeRenderer.bounds.min.y;
    }

    private float GetLowestFootY()
    {
        float lowest = float.PositiveInfinity;
        if (groundedLeftFoot != null)
        {
            lowest = Mathf.Min(lowest, groundedLeftFoot.position.y);
        }
        if (groundedRightFoot != null)
        {
            lowest = Mathf.Min(lowest, groundedRightFoot.position.y);
        }
        return lowest;
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

    private static void ApplyUprightRestPosture(
        Transform modelRoot, BodybuilderEnemyVisual.Rig rig,
        BodybuilderIdentity identity)
    {
        if (modelRoot == null || rig == null || rig.Hips == null ||
            rig.Spine == null || rig.Chest == null || rig.Head == null)
        {
            return;
        }

        Vector3 up = modelRoot.up;
        Vector3 faceForward = Vector3.ProjectOnPlane(rig.Head.forward, up);
        if (faceForward.sqrMagnitude < 0.0001f)
        {
            faceForward = Vector3.ProjectOnPlane(modelRoot.forward, up);
        }
        if (faceForward.sqrMagnitude < 0.0001f)
        {
            return;
        }

        faceForward.Normalize();
        Vector3 correctionAxis = Vector3.Cross(up, faceForward).normalized;
        Vector3 headFromHips = rig.Head.position - rig.Hips.position;
        float forwardOffset = Vector3.Dot(headFromHips, faceForward);
        float verticalOffset = Mathf.Max(0.01f, Vector3.Dot(headFromHips, up));
        float forwardLeanDegrees = Mathf.Atan2(forwardOffset, verticalOffset) * Mathf.Rad2Deg;
        float correctionDegrees = -Mathf.Clamp(forwardLeanDegrees, 0f, 18f);
        if (Mathf.Abs(correctionDegrees) < 0.1f)
        {
            return;
        }

        // Correct the complete upper-body chain around its own joints. Hips,
        // legs and authored foot contacts stay planted; distributing the
        // correction through spine, chest, neck and head avoids a sharp neck
        // bend and pulls the shoulders back with the chest.
        RotateBoneAroundAxis(rig.Spine, correctionAxis, correctionDegrees * 0.45f);
        RotateBoneAroundAxis(rig.Chest, correctionAxis, correctionDegrees * 0.30f);
        RotateBoneAroundAxis(rig.Neck, correctionAxis, correctionDegrees * 0.15f);
        RotateBoneAroundAxis(rig.Head, correctionAxis, correctionDegrees * 0.10f);

        // The imported scans also carry a downward-facing head orientation.
        // Level the neck/head after the torso correction so the whole upper
        // body reads upright instead of leaving the chin tucked forward.
        float headPitchDegrees = Mathf.Atan2(
            Vector3.Dot(rig.Head.forward, up),
            Mathf.Max(0.01f, Vector3.Dot(rig.Head.forward, faceForward))) *
            Mathf.Rad2Deg;
        float levelCorrectionDegrees = Mathf.Clamp(headPitchDegrees, -14f, 14f);
        RotateBoneAroundAxis(rig.Neck, correctionAxis, levelCorrectionDegrees * 0.45f);
        RotateBoneAroundAxis(rig.Head, correctionAxis, levelCorrectionDegrees * 0.55f);
        Debug.Log(
            "GYMCHAOS_UPRIGHT_POSTURE identity=" + identity +
            " forwardLean=" + forwardLeanDegrees.ToString("F2") +
            " correction=" + correctionDegrees.ToString("F2") +
            " headLevel=" + levelCorrectionDegrees.ToString("F2"),
            modelRoot);
    }

    private static void RotateBoneAroundAxis(
        Transform bone, Vector3 axis, float degrees)
    {
        if (bone == null || axis.sqrMagnitude < 0.0001f ||
            Mathf.Abs(degrees) < 0.0001f)
        {
            return;
        }

        bone.rotation = Quaternion.AngleAxis(degrees, axis) * bone.rotation;
    }
    private static void FitToGameplayHeight(
        Transform modelRoot, SkinnedMeshRenderer renderer, BodybuilderIdentity identity)
    {
        float targetHeight = GetGameplayHeight(identity);
        Bounds sourceBounds = CalculateBakedWorldBounds(renderer);
        float sourceHeight = Mathf.Max(0.01f, sourceBounds.size.y);
        float scale = targetHeight / sourceHeight;
        modelRoot.localScale = Vector3.one * scale;
        Physics.SyncTransforms();

        Bounds scaledBounds = CalculateBakedWorldBounds(renderer);
        float measuredHeight = scaledBounds.size.y;
        if (measuredHeight > 0.01f && Mathf.Abs(measuredHeight - targetHeight) > 0.005f)
        {
            // Imported FBX roots can carry a non-unit armature scale. A
            // second measured correction keeps every visible scan comparable
            // to the enemy root capsule and hitbox layout.
            modelRoot.localScale *= targetHeight / measuredHeight;
            Physics.SyncTransforms();
            scaledBounds = CalculateBakedWorldBounds(renderer);
        }
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

    private static float GetGameplayHeight(BodybuilderIdentity identity)
    {
        if (identity == BodybuilderIdentity.Manwithsuit1)
        {
            return 1.82f * 1.125f;
        }
        return identity == BodybuilderIdentity.Arnold
            ? ArnoldGameplayHeight
            : StandardGameplayHeight;
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
                // Always bind the base-color image extracted from the
                // matching authored T-pose GLB first. Its UV atlas is the one
                // used by the visible final FBX, so an importer-generated FBX
                // material cannot accidentally select a different atlas.
                Texture texture = originalTexture;
                if (texture == null && source != null)
                {
                    texture = source.GetTexture("_BaseMap") ?? source.GetTexture("_MainTex");
                }
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
            Neck = FindBone(bones, "neck"),
            Head = FindBone(bones, "head"),
            LeftShoulder = FindBone(bones, "leftshoulder"),
            LeftUpperArm = FindBone(bones, "leftarm"),
            LeftForearm = FindBone(bones, "leftforearm"),
            LeftHand = FindBone(bones, "lefthand"),
            RightShoulder = FindBone(bones, "rightshoulder"),
            RightUpperArm = FindBone(bones, "rightarm"),
            RightForearm = FindBone(bones, "rightforearm"),
            RightHand = FindBone(bones, "righthand"),
            LeftThigh = FindBone(bones, "leftupleg"),
            LeftShin = FindBone(bones, "leftleg"),
            LeftFoot = FindBone(bones, "leftfoot"),
            RightThigh = FindBone(bones, "rightupleg"),
            RightShin = FindBone(bones, "rightleg"),
            RightFoot = FindBone(bones, "rightfoot")
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
            rig.LeftShoulder != null && rig.RightShoulder != null &&
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
