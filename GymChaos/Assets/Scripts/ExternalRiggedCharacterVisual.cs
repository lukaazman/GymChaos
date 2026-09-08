using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Loads the per-character FBX exported from that character's own Blender
/// rig.  The mesh, bind hierarchy and baked animation clips therefore stay in
/// one asset; no hidden shared skeleton or runtime retarget source is needed.
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
    public Transform RuntimeModelRoot => runtimeModelRoot;
    public SkinnedMeshRenderer RuntimeRenderer => runtimeRenderer;
    public BodybuilderEnemyVisual.Rig RuntimeRig => runtimeRig;
    public BodybuilderIdentity RuntimeIdentity => runtimeIdentity;
    public string RuntimeResourcePath => runtimeResourcePath;
    public float RuntimeAuthoredHeight => runtimeAuthoredHeight;

    private Transform runtimeModelRoot;
    private SkinnedMeshRenderer runtimeRenderer;
    private BodybuilderIdentity runtimeIdentity;
    private string runtimeResourcePath;
    private float runtimeAuthoredHeight;
    private BodybuilderEnemyVisual.Rig runtimeRig;
    private FaceCensorSettings runtimeFaceCensor;
    private Mesh runtimeFaceCalibrationMesh;
    private bool runtimeFaceCalibrationSettled;
    private bool runtimeFaceRefreshLogged;
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
            { BodybuilderIdentity.Arnold, "Characters/Enemies/arnold_authored" },
            { BodybuilderIdentity.Cbum, "Characters/Enemies/cbum_authored" },
            { BodybuilderIdentity.Zyzz, "Characters/Enemies/zyzz_authored" },
            { BodybuilderIdentity.Ronnie, "Characters/Enemies/ronnie_authored" },
            { BodybuilderIdentity.JayCutler, "Characters/Enemies/jaycutler_authored" },
            { BodybuilderIdentity.Goku, "Characters/Enemies/goku_authored" },
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
        modelRoot.name = identity + " Authored Blender Rig";
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

        renderer.updateWhenOffscreen = true;
        FitToGameplayHeight(modelRoot.transform, renderer, identity);
        PreserveImportedTextures(modelRoot, identity);

        BodybuilderEnemyVisual.Rig rig = BuildRig(modelRoot.transform, renderer);
        if (!HasRequiredBones(rig))
        {
            Debug.LogError($"{identity} authored FBX is missing required deform bones.", this);
            Destroy(modelRoot);
            Destroy(this);
            return false;
        }

        EnemyMeshHitboxRig.Configure(gameObject, rig, renderer);
        bool neutralNpc = identity == BodybuilderIdentity.Manwithsuit1;
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
            if (!animator.Configure(identity, rig, modelRoot.transform))
            {
                Debug.LogError(
                    $"GYMCHAOS_AUTHORED_ANIMATION_DISABLED identity={identity} " +
                    $"requiredPath=Assets/Resources/Characters/Enemies/{identity.ToString().ToLowerInvariant()}_authored.fbx",
                    this);
                Destroy(animator);
            }
            else
            {
                // Configure samples this FBX's actual authored idle. Fit that
                // pose once while the prepared actor is still hidden. Runtime
                // scale/floor corrections fight locomotion and cause shaking.
                FitToGameplayHeight(modelRoot.transform, renderer, identity);
                animator.CaptureFittedModelTransform();
            }
        }

        // Configure the censor after the visible rig has received its initial
        // idle/rest pose. The eye band is sampled from the deformed renderer;
        // doing it before the retargeter runs leaves the bar in the FBX bind
        // pose while the head has already moved to the gameplay pose.
        BodybuilderEnemyVisual.ConfigureImportedVisual(
            modelRoot.transform, renderer, rig, identity, neutralNpc);

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
        runtimeResourcePath = resourcePath;
        runtimeRig = rig;
        runtimeFaceCensor = modelRoot.GetComponentInChildren<FaceCensorSettings>(true);
        if (BodybuilderEnemyVisual.RequiresImportedFaceRefresh(identity))
        {
            runtimeFaceCalibrationMesh = new Mesh
            {
                name = identity + " runtime face calibration mesh"
            };
            runtimeFaceCalibrationMesh.MarkDynamic();
        }
        runtimeFaceCalibrationSettled = false;
        groundedLeftFoot = rig.LeftFoot;
        groundedRightFoot = rig.RightFoot;
        // Allow a few idle frames for Unity to refresh the skinned bounds after
        // import/posture setup. Stop correcting before locomotion starts so a
        // stride can never make a character grow or shrink over time.
        heightCorrectionFrames = 0;
        // Leave the authored child scale alone after the initial fit. The
        // owner Rigidbody is floor-locked while alive.
        dynamicHeightCorrection = false;
        heightCorrectionLogged = false;
        groundingSettleFrames = 0;
        groundingSettled = true;
        initialGroundingCorrectionApplied = true;

        Bounds verifiedBounds = CalculateBakedWorldBounds(renderer);
        runtimeAuthoredHeight = verifiedBounds.size.y;
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
        MixamoScanRetargetAnimator retargetAnimator =
            GetComponentInChildren<MixamoScanRetargetAnimator>(true);
        bool workoutPoseLocked = retargetAnimator != null &&
            retargetAnimator.IsWorkoutPoseLocked;
        if (runtimeIdentity == BodybuilderIdentity.Goku && fighter != null && fighter.IsGokuFlightActive)
        {
            return;
        }

        if (!workoutPoseLocked && heightCorrectionFrames > 0)
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
        // The authored child transform stays fixed after its hidden preload
        // fit. Foot/mesh AABB changes are pose data, not corrections to apply
        // back onto the model root every frame.

        heightCorrectionLogged = true;

        // The face shell is parented to the imported head bone, so animation
        // already carries it with the eyes. Re-baking the full 140k+ triangle
        // scan every frame was the main runtime cost of the recent calibration
        // change. Wait until the short height correction has settled, then
        // sample the visible pose once and keep the result for the session.
        if (!runtimeFaceCalibrationSettled &&
            heightCorrectionFrames == 0 &&
            !workoutPoseLocked)
        {
            RefreshRuntimeFaceCensor();
            runtimeFaceCalibrationSettled = true;
        }
    }

    public bool RefreshFaceCensorForCurrentPose()
    {
        if (runtimeFaceCalibrationMesh == null)
        {
            return false;
        }

        return BodybuilderEnemyVisual.RefreshImportedFaceCensor(
            runtimeModelRoot, runtimeRenderer, runtimeRig, runtimeIdentity,
            runtimeFaceCensor, runtimeFaceCalibrationMesh);
    }

    private void RefreshRuntimeFaceCensor()
    {
        if (!RefreshFaceCensorForCurrentPose())
        {
            return;
        }

        if (!runtimeFaceRefreshLogged &&
            (runtimeIdentity == BodybuilderIdentity.Ronnie ||
            runtimeIdentity == BodybuilderIdentity.JayCutler ||
            runtimeIdentity == BodybuilderIdentity.Goku))
        {
            Vector3 barLocal = runtimeRig.Head.InverseTransformPoint(
                runtimeFaceCensor.transform.position);
            Debug.Log(
                $"FACE_CENSOR_RUNTIME_REFRESH identity={runtimeIdentity} " +
                $"barLocal={barLocal} depth={runtimeFaceCensor.ConfiguredFaceDepth:F4}",
                runtimeFaceCensor);
            runtimeFaceRefreshLogged = true;
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

        // An ankle bone is not a floor-contact point during a walk cycle.
        // Measure the actual deformed surface while leaving the model root
        // untouched; feeding animated bone height back into the root caused
        // the visible per-step shake.
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
        if (shader == null)
        {
            shader = Shader.Find("Universal Render Pipeline/Lit");
        }
        if (shader == null)
        {
            Debug.LogError($"No runtime body shader is available for {identity}; keeping imported materials.");
            return;
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
            Hips = FindBone(bones, "hips", "def-spine"),
            Spine = FindBone(bones, "def-spine.001", "spine"),
            Chest = FindBone(bones, "def-spine.003", "def-spine.002", "spine2", "spine1"),
            Neck = FindBone(bones, "neck", "def-spine.004"),
            Head = FindBone(bones, "head", "def-spine.005"),
            LeftShoulder = FindBone(bones, "leftshoulder", "shoulder.l", "def-shoulder.l"),
            LeftUpperArm = FindBone(bones, "leftarm", "leftupperarm", "upper-arm.l", "def-upperarm.l"),
            LeftForearm = FindBone(bones, "leftforearm", "leftlowerarm", "forearm.l", "def-forearm.l"),
            LeftHand = FindBone(bones, "lefthand", "hand.l", "def-hand.l"),
            RightShoulder = FindBone(bones, "rightshoulder", "shoulder.r", "def-shoulder.r"),
            RightUpperArm = FindBone(bones, "rightarm", "rightupperarm", "upper-arm.r", "def-upperarm.r"),
            RightForearm = FindBone(bones, "rightforearm", "rightlowerarm", "forearm.r", "def-forearm.r"),
            RightHand = FindBone(bones, "righthand", "hand.r", "def-hand.r"),
            LeftThigh = FindBone(bones, "leftupleg", "leftthigh", "thigh.l", "def-thigh.l"),
            LeftShin = FindBone(bones, "leftleg", "leftcalf", "leftlowerleg", "shin.l", "def-shin.l"),
            LeftFoot = FindBone(bones, "leftfoot", "foot.l", "def-foot.l"),
            RightThigh = FindBone(bones, "rightupleg", "rightthigh", "thigh.r", "def-thigh.r"),
            RightShin = FindBone(bones, "rightleg", "rightcalf", "rightlowerleg", "shin.r", "def-shin.r"),
            RightFoot = FindBone(bones, "rightfoot", "foot.r", "def-foot.r")
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
        // Prefer an exact authored deform-bone name before allowing a loose
        // suffix match.  Rigify has both DEF-spine and DEF-spine.001; a
        // scene-order suffix search can otherwise bind Spine to the hips.
        for (int candidateIndex = 0; candidateIndex < candidates.Length; candidateIndex++)
        {
            string candidate = NormalizeBoneName(candidates[candidateIndex]);
            for (int i = 0; i < bones.Length; i++)
            {
                if (NormalizeBoneName(bones[i].name) == candidate)
                {
                    return bones[i];
                }
            }
        }

        for (int candidateIndex = 0; candidateIndex < candidates.Length; candidateIndex++)
        {
            string candidate = NormalizeBoneName(candidates[candidateIndex]);
            for (int i = 0; i < bones.Length; i++)
            {
                if (NormalizeBoneName(bones[i].name).EndsWith(candidate, StringComparison.Ordinal))
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
