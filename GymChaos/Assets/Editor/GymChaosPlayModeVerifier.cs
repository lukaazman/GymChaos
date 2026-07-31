using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class GymChaosPlayModeVerifier
{
    private const string VerificationRequestedKey = "GymChaos.PlayerMirrorVerificationRequested";
    private static double enteredPlayTime;
    private static bool positioned;
    private static int attackStage;
    private static bool walkSampled;
    private static bool punchCaptured;
    private static bool pushCaptured;
    private static bool throwCaptured;

    static GymChaosPlayModeVerifier()
    {
        if (!EditorPrefs.GetBool(VerificationRequestedKey, false))
        {
            return;
        }
        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        EditorApplication.delayCall += ResumeAfterDomainReload;
    }

    public static void Run()
    {
        positioned = false;
        attackStage = 0;
        walkSampled = false;
        punchCaptured = false;
        pushCaptured = false;
        throwCaptured = false;
        EditorPrefs.SetBool(VerificationRequestedKey, true);
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        EditorApplication.isPlaying = true;
    }

    private static void ResumeAfterDomainReload()
    {
        if (EditorApplication.isPlaying)
        {
            enteredPlayTime = EditorApplication.timeSinceStartup;
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
        }
    }

    private static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            enteredPlayTime = EditorApplication.timeSinceStartup;
            EditorApplication.update += Tick;
        }
        else if (state == PlayModeStateChange.EnteredEditMode)
        {
            EditorPrefs.DeleteKey(VerificationRequestedKey);
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.Exit(0);
        }
    }

    private static void Tick()
    {
        try
        {
            double elapsed = EditorApplication.timeSinceStartup - enteredPlayTime;
            PlayerMovement player = UnityEngine.Object.FindFirstObjectByType<PlayerMovement>();
            PlanarGymMirror mirror = UnityEngine.Object.FindFirstObjectByType<PlanarGymMirror>();
            if (player == null || player.playerCamera == null || mirror == null)
            {
                if (elapsed > 25d)
                {
                    throw new InvalidOperationException("Player, player camera, or planar mirror did not initialize.");
                }
                return;
            }

            if (!positioned && elapsed > 4d)
            {
                PositionPlayerAtMirror(player);
                positioned = true;
            }

            PlayerHandRig rig = player.GetComponentInChildren<PlayerHandRig>(true);
            if (positioned && rig != null && !walkSampled && elapsed > 5d)
            {
                walkSampled = rig.SampleRunForVerification(0.35f);
            }
            if (positioned && attackStage == 0 && elapsed > 6d)
            {
                if (rig == null)
                {
                    throw new InvalidOperationException("Mixamo player rig was not created.");
                }
                if (!rig.HasRequiredMixamoAttackClips)
                {
                    throw new InvalidOperationException(
                        $"Required Mixamo clips were not imported: {rig.MixamoAttackClipSummary}.");
                }
                if (!rig.HasMixamoRunClip || !rig.HasSampledMixamoRunClip)
                {
                    throw new InvalidOperationException(
                        $"Mixamo run clip was not imported and sampled: {rig.MixamoAttackClipSummary}.");
                }
                rig.TriggerPunch(true);
                attackStage = 1;
                return;
            }

            if (attackStage == 1 && !punchCaptured && elapsed > 6.25d)
            {
                CaptureCamera(player.playerCamera, "player-punch-verification.png");
                punchCaptured = true;
                return;
            }

            if (attackStage == 1 && punchCaptured && elapsed > 6.65d)
            {
                rig.TriggerShove();
                attackStage = 2;
                return;
            }

            if (attackStage == 2 && !pushCaptured && elapsed > 6.9d)
            {
                CaptureCamera(player.playerCamera, "player-push-verification.png");
                pushCaptured = true;
                return;
            }

            if (attackStage == 2 && pushCaptured && elapsed > 7.3d)
            {
                rig.TriggerThrow(true);
                attackStage = 3;
                return;
            }

            if (attackStage == 3 && !throwCaptured && elapsed > 7.6d)
            {
                CaptureCamera(player.playerCamera, "player-throw-verification.png");
                throwCaptured = true;
                return;
            }

            if (attackStage == 3 && throwCaptured && elapsed > 8.1d)
            {
                rig.SetHolding(true);
                rig.TriggerShove(0.72f, 0.3f);
                attackStage = 4;
                return;
            }

            if (attackStage == 4 && elapsed > 8.5d)
            {
                rig.SetHolding(false);
                rig.SetHolding(true);
                rig.TriggerShove(0.58f, 0.22f);
                attackStage = 5;
                return;
            }

            if (attackStage == 5 && elapsed > 8.85d)
            {
                rig.SetHolding(false);
                ValidateAndCapture(player, rig);
                EditorApplication.update -= Tick;
                EditorApplication.isPlaying = false;
            }
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            EditorPrefs.DeleteKey(VerificationRequestedKey);
            EditorApplication.update -= Tick;
            EditorApplication.Exit(1);
        }
    }

    private static void PositionPlayerAtMirror(PlayerMovement player)
    {
        Renderer[] allRenderers = UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None);
        Bounds mirrorBounds = default;
        bool found = false;
        for (int i = 0; i < allRenderers.Length; i++)
        {
            if (allRenderers[i].name != "Mirror panel")
            {
                continue;
            }
            if (!found)
            {
                mirrorBounds = allRenderers[i].bounds;
                found = true;
            }
            else
            {
                mirrorBounds.Encapsulate(allRenderers[i].bounds);
            }
        }
        if (!found)
        {
            throw new InvalidOperationException("Mirror panels were not built.");
        }

        CharacterController controller = player.GetComponent<CharacterController>();
        if (controller != null)
        {
            controller.enabled = false;
        }
        Vector3 target = mirrorBounds.center;
        Vector3 position = target + Vector3.back * 5.5f;
        position.y = mirrorBounds.min.y + 1f;
        player.transform.position = position;
        player.transform.rotation = Quaternion.LookRotation(
            Vector3.ProjectOnPlane(target - position, Vector3.up).normalized, Vector3.up);
        player.playerCamera.transform.localRotation = Quaternion.identity;
        EnemyFighter[] fighters = UnityEngine.Object.FindObjectsByType<EnemyFighter>(FindObjectsSortMode.None);
        for (int i = 0; i < fighters.Length; i++)
        {
            Renderer[] fighterRenderers = fighters[i].GetComponentsInChildren<Renderer>(true);
            for (int rendererIndex = 0; rendererIndex < fighterRenderers.Length; rendererIndex++)
            {
                fighterRenderers[rendererIndex].enabled = false;
            }
        }
        if (controller != null)
        {
            controller.enabled = true;
        }
    }

    private static void ValidateAndCapture(PlayerMovement player, PlayerHandRig rig)
    {
        if (!rig.HasSampledAllMixamoAttackClips)
        {
            throw new InvalidOperationException(
                $"Not every Mixamo attack was sampled in Play Mode: {rig.MixamoAttackClipSummary}.");
        }
        if (!rig.HasSampledHeldEquipmentGrips)
        {
            throw new InvalidOperationException(
                "Mixamo push was not sampled with both bar and plate grip overlays.");
        }

        EnemyMeshHitboxRig[] enemyHitboxRigs = UnityEngine.Object.FindObjectsByType<EnemyMeshHitboxRig>(FindObjectsSortMode.None);
        int compoundColliderCount = 0;
        int clearLegGapCount = 0;
        EnemyMeshHitboxRig bloodTestRig = null;
        Collider bloodTestCollider = null;
        for (int i = 0; i < enemyHitboxRigs.Length; i++)
        {
            compoundColliderCount += enemyHitboxRigs[i].GetComponentsInChildren<Collider>(true).Length;
            CapsuleCollider broad = enemyHitboxRigs[i].GetComponent<CapsuleCollider>();
            if (broad != null && broad.enabled)
            {
                throw new InvalidOperationException("An enemy still uses the broad root capsule instead of tight body hitboxes.");
            }

            Transform leftThigh = FindDescendant(enemyHitboxRigs[i].transform, "Left thigh hitbox");
            Transform rightThigh = FindDescendant(enemyHitboxRigs[i].transform, "Right thigh hitbox");
            if (leftThigh == null || rightThigh == null)
            {
                throw new InvalidOperationException("A character is missing separate thigh hitboxes.");
            }
            Vector3 legGap = (leftThigh.position + rightThigh.position) * 0.5f;
            Collider[] colliders = enemyHitboxRigs[i].GetComponentsInChildren<Collider>(true);
            for (int colliderIndex = 0; colliderIndex < colliders.Length; colliderIndex++)
            {
                Collider candidate = colliders[colliderIndex];
                if (candidate.enabled &&
                    (candidate.ClosestPoint(legGap) - legGap).sqrMagnitude < 0.000001f)
                {
                    throw new InvalidOperationException(
                        $"The transparent leg gap is still covered by a collider on {enemyHitboxRigs[i].name}.");
                }
            }
            clearLegGapCount++;

            if (bloodTestCollider == null)
            {
                Transform headHitbox = FindDescendant(enemyHitboxRigs[i].transform, "Head hitbox");
                Collider headCollider = headHitbox != null ? headHitbox.GetComponent<Collider>() : null;
                if (headCollider != null && headCollider.enabled)
                {
                    bloodTestRig = enemyHitboxRigs[i];
                    bloodTestCollider = headCollider;
                }
            }
        }
        if (enemyHitboxRigs.Length < 5 || compoundColliderCount < 50)
        {
            throw new InvalidOperationException(
                $"Expected tight hitboxes for five characters, found rigs={enemyHitboxRigs.Length}, colliders={compoundColliderCount}.");
        }

        float bloodSurfaceDistance = ValidateBloodSurfacePlacement(bloodTestRig, bloodTestCollider);
        ValidateGoreScaling();

        SkinnedMeshRenderer[] renderers = rig.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        int mirrorBodyCount = 0;
        int firstPersonArmCount = 0;
        int firstPersonTriangleCount = 0;
        Bounds bodyBounds = default;
        bool hasBodyBounds = false;
        Bounds armBounds = default;
        bool hasArmBounds = false;
        string rendererDebug = string.Empty;
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i].gameObject.layer == PlanarGymMirror.MirrorPlayerLayer)
            {
                mirrorBodyCount++;
                if (!hasBodyBounds)
                {
                    bodyBounds = renderers[i].bounds;
                    hasBodyBounds = true;
                }
                else
                {
                    bodyBounds.Encapsulate(renderers[i].bounds);
                }
                rendererDebug += $" bodyEnabled={renderers[i].enabled}/{renderers[i].forceRenderingOff}/{renderers[i].shadowCastingMode}/{renderers[i].sharedMaterial?.shader?.name}";
            }
            else if (renderers[i].gameObject.layer == PlanarGymMirror.FirstPersonPlayerLayer)
            {
                firstPersonArmCount++;
                if (renderers[i].sharedMesh != null)
                {
                    firstPersonTriangleCount += renderers[i].sharedMesh.triangles.Length / 3;
                }
                if (!hasArmBounds)
                {
                    armBounds = renderers[i].bounds;
                    hasArmBounds = true;
                }
                else
                {
                    armBounds.Encapsulate(renderers[i].bounds);
                }
                rendererDebug += $" armsEnabled={renderers[i].enabled}/{renderers[i].forceRenderingOff}/{renderers[i].shadowCastingMode}/{renderers[i].sharedMaterial?.shader?.name}";
            }
        }
        if (mirrorBodyCount == 0 || firstPersonArmCount == 0 || firstPersonTriangleCount == 0)
        {
            throw new InvalidOperationException(
                $"Expected mirror body and first-person arms, found body={mirrorBodyCount}, arms={firstPersonArmCount}, armTriangles={firstPersonTriangleCount}.");
        }

        float enemyHeightTotal = 0f;
        int enemyHeightCount = 0;
        EnemyFighter[] fighters = UnityEngine.Object.FindObjectsByType<EnemyFighter>(FindObjectsSortMode.None);
        for (int i = 0; i < fighters.Length; i++)
        {
            SkinnedMeshRenderer enemyRenderer = fighters[i].GetComponentInChildren<SkinnedMeshRenderer>(true);
            if (enemyRenderer != null && enemyRenderer.bounds.size.y > 0.5f)
            {
                enemyHeightTotal += enemyRenderer.bounds.size.y;
                enemyHeightCount++;
            }
        }
        float averageEnemyHeight = enemyHeightCount > 0 ? enemyHeightTotal / enemyHeightCount : 0f;
        float playerToEnemyHeight = averageEnemyHeight > 0f ? bodyBounds.size.y / averageEnemyHeight : 0f;
        if (playerToEnemyHeight < 0.9f || playerToEnemyHeight > 1.12f)
        {
            throw new InvalidOperationException(
                $"Player height is not comparable to enemies: player={bodyBounds.size.y:F2}, enemyAverage={averageEnemyHeight:F2}, ratio={playerToEnemyHeight:F2}.");
        }

        Camera camera = player.playerCamera;
        string outputPath = CaptureCamera(camera, "player-mirror-verification.png");
        Transform leftHand = FindDescendant(rig.transform, "mixamorig:LeftHand");
        Transform rightHand = FindDescendant(rig.transform, "mixamorig:RightHand");
        Vector3 leftHandCamera = leftHand != null ? camera.transform.InverseTransformPoint(leftHand.position) : Vector3.zero;
        Vector3 rightHandCamera = rightHand != null ? camera.transform.InverseTransformPoint(rightHand.position) : Vector3.zero;
        Camera mirrorCamera = GameObject.Find("Gym Mirror Camera")?.GetComponent<Camera>();
        MeshRenderer[] proxyRenderers = rig.GetComponentsInChildren<MeshRenderer>(true);
        string proxyDebug = string.Empty;
        for (int i = 0; i < proxyRenderers.Length; i++)
        {
            MeshFilter filter = proxyRenderers[i].GetComponent<MeshFilter>();
            proxyDebug += $" {proxyRenderers[i].name}:layer={proxyRenderers[i].gameObject.layer}," +
                $"verts={filter?.sharedMesh?.vertexCount},tris={filter?.sharedMesh?.triangles?.Length / 3}," +
                $"bounds={proxyRenderers[i].bounds.center}/{proxyRenderers[i].bounds.size}";
        }
        Debug.Log(
            $"GYMCHAOS_PLAYER_MIRROR_OK body={mirrorBodyCount} arms={firstPersonArmCount} " +
            $"armTriangles={firstPersonTriangleCount} bodyBounds={bodyBounds.center}/{bodyBounds.size} " +
            $"armBounds={armBounds.center}/{armBounds.size} handsCamera={leftHandCamera}/{rightHandCamera} " +
            $"hitboxRigs={enemyHitboxRigs.Length} compoundColliders={compoundColliderCount} clearLegGaps={clearLegGapCount} " +
            $"bloodSurfaceDistance={bloodSurfaceDistance:F4} " +
            $"mixamoClips={rig.MixamoAttackClipSummary} allMixamoAttacksSampled={rig.HasSampledAllMixamoAttackClips} " +
            $"mixamoRunSampled={rig.HasSampledMixamoRunClip} " +
            $"heldEquipmentGripsSampled={rig.HasSampledHeldEquipmentGrips} " +
            $"averageEnemyHeight={averageEnemyHeight:F2} playerToEnemyHeight={playerToEnemyHeight:F2} " +
            $"cameraMasks={camera.cullingMask}/{mirrorCamera?.cullingMask} renderers={rendererDebug} " +
            $"proxies={proxyDebug} screenshot={outputPath}");
    }

    private static string CaptureCamera(Camera camera, string fileName)
    {
        RenderTexture renderTexture = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32);
        RenderTexture previousTarget = camera.targetTexture;
        RenderTexture previousActive = RenderTexture.active;
        camera.targetTexture = renderTexture;
        camera.Render();
        RenderTexture.active = renderTexture;
        Texture2D image = new Texture2D(1280, 720, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
        image.Apply();

        string outputPath = Path.GetFullPath(Path.Combine(
            Application.dataPath, "../../.tools", fileName));
        File.WriteAllBytes(outputPath, image.EncodeToPNG());
        camera.targetTexture = previousTarget;
        RenderTexture.active = previousActive;
        UnityEngine.Object.DestroyImmediate(image);
        renderTexture.Release();
        UnityEngine.Object.DestroyImmediate(renderTexture);
        return outputPath;
    }

    private static float ValidateBloodSurfacePlacement(EnemyMeshHitboxRig hitboxRig, Collider hitCollider)
    {
        if (hitboxRig == null || hitCollider == null)
        {
            throw new InvalidOperationException("No tight head hitbox was available for the blood surface test.");
        }

        EnemyFighter fighter = hitboxRig.GetComponent<EnemyFighter>();
        MethodInfo tightSurfaceMethod = typeof(PlayerMovement).GetMethod(
            "IsTightEnemySurface", BindingFlags.NonPublic | BindingFlags.Static);
        if (fighter == null || tightSurfaceMethod == null ||
            !(bool)tightSurfaceMethod.Invoke(null, new object[] { fighter, hitCollider }))
        {
            throw new InvalidOperationException("A real body-part collider is not accepted as a tight combat surface.");
        }

        Collider broadRoot = fighter.GetComponent<Collider>();
        if (broadRoot != null &&
            (bool)tightSurfaceMethod.Invoke(null, new object[] { fighter, broadRoot }))
        {
            throw new InvalidOperationException("The broad root collider is still accepted as a combat surface.");
        }

        Vector3 approximatePoint = hitCollider.bounds.center;
        if (!hitboxRig.TrySnapToSurface(approximatePoint, out Vector3 surfacePoint, out Vector3 surfaceNormal))
        {
            throw new InvalidOperationException("Could not resolve a real skinned-mesh surface for blood placement.");
        }

        BloodSplatter.SpawnOnBody(fighter, approximatePoint, Vector3.forward, 0.82f, hitCollider.transform);
        GameObject stain = GameObject.Find("Blood stain");
        if (stain == null || stain.transform.parent != hitCollider.transform)
        {
            throw new InvalidOperationException("Blood stain was not attached to the moving body part that was hit.");
        }

        float surfaceDistance = Vector3.Distance(stain.transform.position, surfacePoint + surfaceNormal * 0.0025f);
        if (surfaceDistance > 0.012f)
        {
            throw new InvalidOperationException(
                $"Blood is too far from the real mesh surface: distance={surfaceDistance:F4}m.");
        }

        UnityEngine.Object.DestroyImmediate(stain);
        GameObject burst = GameObject.Find("Blood impact burst");
        if (burst != null)
        {
            UnityEngine.Object.DestroyImmediate(burst);
        }
        return surfaceDistance;
    }

    private static void ValidateGoreScaling()
    {
        float heldPlate5 = BloodSplatter.GetHeldShoveScale(WeightType.Plate5, 5f);
        float heldPlate10 = BloodSplatter.GetHeldShoveScale(WeightType.Plate10, 10f);
        float heldPlate20 = BloodSplatter.GetHeldShoveScale(WeightType.Plate20, 20f);
        float heldEzBar = BloodSplatter.GetHeldShoveScale(WeightType.EzBar, 10f);
        float heldBarbell = BloodSplatter.GetHeldShoveScale(WeightType.Barbell, 20f);
        float thrownPlate5 = BloodSplatter.GetThrownScale(WeightType.Plate5, 5f);
        float thrownPlate10 = BloodSplatter.GetThrownScale(WeightType.Plate10, 10f);
        float thrownPlate20 = BloodSplatter.GetThrownScale(WeightType.Plate20, 20f);
        float thrownEzBar = BloodSplatter.GetThrownScale(WeightType.EzBar, 10f);
        float thrownBarbell = BloodSplatter.GetThrownScale(WeightType.Barbell, 20f);

        if (!(heldPlate5 < heldPlate10 && heldPlate10 < heldPlate20 && heldPlate20 < heldEzBar &&
              heldEzBar < heldBarbell && thrownPlate5 < thrownPlate10 && thrownPlate10 < thrownPlate20 &&
              thrownEzBar < thrownBarbell && thrownPlate5 > heldPlate5 && thrownPlate10 > heldPlate10 &&
              thrownPlate20 > heldPlate20 && thrownEzBar > heldEzBar && thrownBarbell > heldBarbell))
        {
            throw new InvalidOperationException("Held/throw gore intensity no longer scales with weapon type and plate weight.");
        }
    }

    private static Transform FindDescendant(Transform root, string targetName)
    {
        Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < transforms.Length; i++)
        {
            if (transforms[i].name == targetName)
            {
                return transforms[i];
            }
        }
        return null;
    }
}
