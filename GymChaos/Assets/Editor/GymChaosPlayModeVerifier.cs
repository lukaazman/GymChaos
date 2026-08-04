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
    private static bool gokuFlightVerificationStarted;
    private static bool gokuFlightVerified;
    private static EnemyFighter gokuForVerification;
    private static double gokuFlightVerificationStartedAt;
    private static float gokuFlightVerificationStartedGameTime;
    private static float gokuGroundY;

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

    [MenuItem("Tools/GymChaos/Run Full Play Mode Verification")]
    public static void Run()
    {
        positioned = false;
        attackStage = 0;
        walkSampled = false;
        punchCaptured = false;
        pushCaptured = false;
        throwCaptured = false;
        gokuFlightVerificationStarted = false;
        gokuFlightVerified = false;
        gokuForVerification = null;
        gokuFlightVerificationStartedAt = 0d;
        gokuFlightVerificationStartedGameTime = 0f;
        gokuGroundY = 0f;
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
            if (Application.isBatchMode)
            {
                EditorApplication.Exit(0);
            }
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
            if (Application.isBatchMode)
            {
                EditorApplication.Exit(1);
            }
            else
            {
                EditorApplication.isPlaying = false;
            }
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

    private static void BeginGokuFlightVerification(PlayerMovement player)
    {
        EnemyFighter[] fighters = UnityEngine.Object.FindObjectsByType<EnemyFighter>(FindObjectsSortMode.None);
        for (int i = 0; i < fighters.Length; i++)
        {
            if (fighters[i] != null && fighters[i].Identity == BodybuilderIdentity.Goku)
            {
                gokuForVerification = fighters[i];
                break;
            }
        }
        if (gokuForVerification == null)
        {
            throw new InvalidOperationException("Goku was not spawned for flight verification.");
        }

        gokuGroundY = gokuForVerification.transform.position.y;
        Renderer floor = GameObject.Find("Rubber Floor")?.GetComponent<Renderer>();
        Vector3 towardRoomCenter = floor != null
            ? Vector3.ProjectOnPlane(floor.bounds.center - gokuForVerification.transform.position, Vector3.up)
            : Vector3.ProjectOnPlane(player.transform.position - gokuForVerification.transform.position, Vector3.up);
        if (towardRoomCenter.sqrMagnitude < 0.01f)
        {
            towardRoomCenter = Vector3.forward;
        }
        float safeDistance = Mathf.Clamp(towardRoomCenter.magnitude, 6.4f, 10f);
        Vector3 safePlayerPosition = gokuForVerification.transform.position +
            towardRoomCenter.normalized * safeDistance;
        if (floor != null)
        {
            safePlayerPosition.x = Mathf.Clamp(
                safePlayerPosition.x, floor.bounds.min.x + 1.5f, floor.bounds.max.x - 1.5f);
            safePlayerPosition.z = Mathf.Clamp(
                safePlayerPosition.z, floor.bounds.min.z + 1.5f, floor.bounds.max.z - 1.5f);
        }
        MovePlayerForVerification(player, safePlayerPosition);
        gokuFlightVerificationStarted = true;
        gokuFlightVerificationStartedAt = EditorApplication.timeSinceStartup;
        gokuFlightVerificationStartedGameTime = Time.time;
    }

    private static void MovePlayerForVerification(PlayerMovement player, Vector3 position)
    {
        CharacterController controller = player.GetComponent<CharacterController>();
        bool wasEnabled = controller != null && controller.enabled;
        if (wasEnabled)
        {
            controller.enabled = false;
        }
        position.y = player.transform.position.y;
        player.transform.position = position;
        if (wasEnabled)
        {
            controller.enabled = true;
        }
    }

    private static void ValidateAndCapture(PlayerMovement player, PlayerHandRig rig)
    {
        ValidateRuntimeRoster();
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
        if (!rig.SampleCrouchForVerification(0.45f) || !rig.HasSampledMixamoCrouchClip)
        {
            throw new InvalidOperationException("The Mixamo crouch clip was not imported and sampled.");
        }
        CaptureCamera(player.playerCamera, "player-crouch-verification.png");

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
                if (!TryGetVisibleSkinnedBounds(renderers[i], out Bounds visibleBounds))
                {
                    continue;
                }
                if (!hasBodyBounds)
                {
                    bodyBounds = visibleBounds;
                    hasBodyBounds = true;
                }
                else
                {
                    bodyBounds.Encapsulate(visibleBounds);
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
        if (mirrorBodyCount == 0 || !hasBodyBounds || firstPersonArmCount == 0 || firstPersonTriangleCount == 0)
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
            if (enemyRenderer != null && TryGetVisibleSkinnedBounds(enemyRenderer, out Bounds enemyBounds) &&
                enemyBounds.size.y > 0.5f)
            {
                enemyHeightTotal += enemyBounds.size.y;
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
        if (leftHand == null || rightHand == null || leftHandCamera.y > -0.42f || rightHandCamera.y > -0.42f)
        {
            throw new InvalidOperationException(
                $"Neutral first-person hands are still too high: leftY={leftHandCamera.y:F2}, rightY={rightHandCamera.y:F2}.");
        }
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

    private static void ValidateExternalCharactersAndCapture()
    {
        EnemyFighter[] fighters = UnityEngine.Object.FindObjectsByType<EnemyFighter>(FindObjectsSortMode.None);
        int verified = 0;
        GameObject cameraObject = new GameObject("Character evidence camera");
        Camera evidenceCamera = cameraObject.AddComponent<Camera>();
        evidenceCamera.clearFlags = CameraClearFlags.SolidColor;
        evidenceCamera.backgroundColor = new Color(0.055f, 0.065f, 0.09f);
        evidenceCamera.fieldOfView = 42f;
        evidenceCamera.nearClipPlane = 0.03f;
        evidenceCamera.farClipPlane = 100f;

        for (int i = 0; i < fighters.Length; i++)
        {
            EnemyFighter fighter = fighters[i];
            if (fighter == null)
            {
                continue;
            }
            SkinnedMeshRenderer body = fighter.GetComponentInChildren<SkinnedMeshRenderer>(true);
            ExternalRiggedCharacterAnimator animator =
                fighter.GetComponentInChildren<ExternalRiggedCharacterAnimator>(true);
            if (body == null || animator == null || !animator.HasRunClip || !animator.HasPunchClip)
            {
                throw new InvalidOperationException(
                    $"{fighter.Identity} is missing its external body, Run clip, or Punch clip.");
            }

            int expectedTriangles = ExpectedCharacterTriangles(fighter.Identity);
            int triangles = body.sharedMesh != null ? body.sharedMesh.triangles.Length / 3 : 0;
            if (triangles != expectedTriangles)
            {
                throw new InvalidOperationException(
                    $"{fighter.Identity} topology changed: triangles={triangles}, expected={expectedTriangles}.");
            }
            if (!TryGetVisibleSkinnedBounds(body, out Bounds bounds))
            {
                throw new InvalidOperationException($"Could not bake {fighter.Identity} for exact bounds.");
            }
            float expectedHeight = fighter.Identity == BodybuilderIdentity.Arnold ? 2.35f : 2.3f;
            if (Mathf.Abs(bounds.size.y - expectedHeight) > 0.04f)
            {
                throw new InvalidOperationException(
                    $"{fighter.Identity} height={bounds.size.y:F3}, expected={expectedHeight:F3}.");
            }

            Texture texture = body.sharedMaterial != null
                ? body.sharedMaterial.GetTexture("_BaseMap")
                : null;
            string expectedTexture = fighter.Identity == BodybuilderIdentity.JayCutler
                ? "jay"
                : fighter.Identity.ToString().ToLowerInvariant();
            if (texture == null || !string.Equals(texture.name, expectedTexture, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"{fighter.Identity} has texture={(texture != null ? texture.name : "missing")}, expected={expectedTexture}.");
            }

            Renderer[] characterRenderers = fighter.GetComponentsInChildren<Renderer>(true);
            bool[] previousStates = new bool[characterRenderers.Length];
            for (int rendererIndex = 0; rendererIndex < characterRenderers.Length; rendererIndex++)
            {
                previousStates[rendererIndex] = characterRenderers[rendererIndex].enabled;
                characterRenderers[rendererIndex].enabled = true;
            }
            Vector3 viewDirection = fighter.transform.forward.sqrMagnitude > 0.1f
                ? fighter.transform.forward.normalized
                : Vector3.forward;
            evidenceCamera.transform.position = bounds.center + viewDirection * 4.2f;
            evidenceCamera.transform.rotation = Quaternion.LookRotation(
                bounds.center - evidenceCamera.transform.position, Vector3.up);
            string screenshot = CaptureCamera(
                evidenceCamera, $"enemy-{expectedTexture}-verification.png");
            for (int rendererIndex = 0; rendererIndex < characterRenderers.Length; rendererIndex++)
            {
                characterRenderers[rendererIndex].enabled = previousStates[rendererIndex];
            }
            Debug.Log(
                $"GYMCHAOS_CHARACTER_VISUAL_OK identity={fighter.Identity} height={bounds.size.y:F3} " +
                $"triangles={triangles} texture={texture.name} screenshot={screenshot}");
            verified++;
        }

        UnityEngine.Object.DestroyImmediate(cameraObject);
        if (verified != 6)
        {
            throw new InvalidOperationException($"Expected six enemy visuals, verified={verified}.");
        }
    }

    private static int ExpectedCharacterTriangles(BodybuilderIdentity identity)
    {
        switch (identity)
        {
            case BodybuilderIdentity.Arnold: return 70106;
            case BodybuilderIdentity.Cbum: return 69708;
            case BodybuilderIdentity.Zyzz: return 74063;
            case BodybuilderIdentity.Ronnie: return 70728;
            case BodybuilderIdentity.JayCutler: return 144640;
            case BodybuilderIdentity.Goku: return 142054;
            default: return 0;
        }
    }

    private static void ValidateRuntimeRoster()
    {
        EnemyFighter[] fighters = UnityEngine.Object.FindObjectsByType<EnemyFighter>(FindObjectsSortMode.None);
        bool hasJay = false;
        bool hasGoku = false;
        for (int i = 0; i < fighters.Length; i++)
        {
            EnemyFighter fighter = fighters[i];
            if (fighter == null)
            {
                continue;
            }

            if (fighter.Identity == BodybuilderIdentity.JayCutler)
            {
                hasJay = true;
                ValidateNamedEnemy(fighter, 100f, "Jay Cutler");
            }
            else if (fighter.Identity == BodybuilderIdentity.Goku)
            {
                hasGoku = true;
                ValidateNamedEnemy(fighter, 1000f, "Goku");
            }
        }

        if (!hasJay || !hasGoku)
        {
            throw new InvalidOperationException(
                $"Expected Jay Cutler and Goku in runtime roster, found Jay={hasJay}, Goku={hasGoku}.");
        }
    }

    private static void ValidateNamedEnemy(EnemyFighter fighter, float expectedHealth, string displayName)
    {
        if (!fighter.CompareTag("Enemies"))
        {
            throw new InvalidOperationException($"{displayName} is missing the Enemies tag.");
        }
        if (Mathf.Abs(fighter.MaxHealth - expectedHealth) > 0.01f)
        {
            throw new InvalidOperationException(
                $"{displayName} has max health {fighter.MaxHealth}, expected {expectedHealth}.");
        }
        if (fighter.IsPolice)
        {
            throw new InvalidOperationException($"{displayName} must not use the police target behavior.");
        }
    }

    private static bool TryGetVisibleSkinnedBounds(SkinnedMeshRenderer renderer, out Bounds bounds)
    {
        if (renderer == null || renderer.sharedMesh == null)
        {
            bounds = default;
            return false;
        }
        bounds = renderer.bounds;
        return bounds.size.sqrMagnitude > 0.0001f;
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
