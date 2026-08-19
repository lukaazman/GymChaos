using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

[InitializeOnLoad]
public static class GymChaosPlayModeVerifier
{
    private const string VerificationRequestedKey = "GymChaos.PlayerMirrorVerificationRequested";
    private static double enteredPlayTime;
    private static bool positioned;
    private static bool firstPersonEyeCaptured;
    private static bool hasAverageEnemyEyeWorldY;
    private static float averageEnemyEyeWorldY;
    private static string firstPersonEyeCapturePath;
    private static int attackStage;
    private static bool walkSampled;
    private static bool punchCaptured;
    private static bool pushCaptured;
    private static bool throwCaptured;
    private static bool visitorSimulationSuspended;
    private static bool gokuFlightVerificationStarted;
    private static bool gokuFlightVerified;
    private static bool gokuRunBandLogged;
    private static EnemyFighter gokuForVerification;
    private static double gokuFlightVerificationStartedAt;
    private static float gokuFlightVerificationStartedGameTime;
    private static float gokuGroundY;
    private static EnemyFighter contactKiller;
    private static float contactHealthBefore;
    private static double contactVerificationStartedAt;
    private static Vector3 contactOriginalPosition;
    private static Quaternion contactOriginalRotation;
    private static double deathScreenCaptureStartedAt;
    private static string deathScreenCapturePath;
    private static GameObject deathScreenCaptureOverlay;

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
        firstPersonEyeCaptured = false;
        hasAverageEnemyEyeWorldY = false;
        averageEnemyEyeWorldY = 0f;
        firstPersonEyeCapturePath = string.Empty;
        attackStage = 0;
        walkSampled = false;
        punchCaptured = false;
        pushCaptured = false;
        throwCaptured = false;
        visitorSimulationSuspended = false;
        gokuFlightVerificationStarted = false;
        gokuFlightVerified = false;
        gokuRunBandLogged = false;
        gokuForVerification = null;
        gokuFlightVerificationStartedAt = 0d;
        gokuFlightVerificationStartedGameTime = 0f;
        gokuGroundY = 0f;
        contactKiller = null;
        contactHealthBefore = 0f;
        contactVerificationStartedAt = 0d;
        contactOriginalPosition = Vector3.zero;
        contactOriginalRotation = Quaternion.identity;
        deathScreenCaptureStartedAt = 0d;
        deathScreenCapturePath = string.Empty;
        deathScreenCaptureOverlay = null;
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
            if (!visitorSimulationSuspended)
            {
                GymVisitorDirector visitorDirector =
                    UnityEngine.Object.FindFirstObjectByType<GymVisitorDirector>();
                if (visitorDirector != null)
                {
                    visitorDirector.SuspendVisitorSimulationForVerification();
                    visitorSimulationSuspended = true;
                }
            }
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
                if (!firstPersonEyeCaptured)
                {
                    firstPersonEyeCaptured = CaptureFirstPersonEyeLevelEvidence(player);
                    if (!firstPersonEyeCaptured)
                    {
                        return;
                    }
                }
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
                // Capture the six visible scans before the Goku flight phase.
                // The hidden motion skeletons must never be selected as visual
                // evidence, and this keeps a flight-test failure from hiding
                // the actual Idle/Run/Punch skin result.
                ValidateExternalCharactersAndCapture();
                BeginGokuFlightVerification(player);
                attackStage = 6;
                return;
            }

            if (attackStage == 6)
            {
                if (gokuForVerification != null && gokuForVerification.IsFlying &&
                    gokuForVerification.AnimationState == MixamoScanRetargetAnimator.MotionState.Flying)
                {
                    gokuFlightVerified = true;
                    Debug.Log(
                        $"GYMCHAOS_GOKU_FLY_OK state={gokuForVerification.AnimationState} " +
                        $"flying={gokuForVerification.IsFlying} " +
                        $"heightDelta={(gokuForVerification.transform.position.y - gokuGroundY):F2}");
                    // Goku's flight pose rotates the imported scan 90 degrees
                    // around X so its local +Y axis leads the flight vector.
                    // transform.forward is therefore vertical while flying;
                    // use the projected model up axis to place the player in
                    // a real horizontal run range after landing.
                    Vector3 gokuApproachDirection = Vector3.ProjectOnPlane(
                        gokuForVerification.transform.up, Vector3.up);
                    if (gokuApproachDirection.sqrMagnitude < 0.01f)
                    {
                        gokuApproachDirection = Vector3.ProjectOnPlane(
                            gokuForVerification.transform.forward, Vector3.up);
                    }
                    if (gokuApproachDirection.sqrMagnitude < 0.01f)
                    {
                        gokuApproachDirection = Vector3.forward;
                    }
                    MovePlayerForVerification(
                        player,
                        gokuForVerification.transform.position +
                        gokuApproachDirection.normalized * 4.5f);
                    gokuFlightVerificationStartedAt = EditorApplication.timeSinceStartup;
                    attackStage = 7;
                    return;
                }
                if (EditorApplication.timeSinceStartup - gokuFlightVerificationStartedAt > 6d)
                {
                    throw new InvalidOperationException(
                        $"Goku did not reach runtime Fly: state={gokuForVerification?.AnimationState}, " +
                        $"flying={gokuForVerification?.IsFlying}.");
                }
                return;
            }

            if (attackStage == 7)
            {
                if (gokuForVerification != null)
                {
                    // Keep the player in the grounded run band while the
                    // editor update loop observes the transition.  Goku can
                    // cover the original sample spacing in one fixed step,
                    // which made the verifier see Punch without ever
                    // recording the valid intermediate Run state.
                    Vector3 runBandDirection = Vector3.ProjectOnPlane(
                        player.transform.position - gokuForVerification.transform.position,
                        Vector3.up);
                    if (runBandDirection.sqrMagnitude < 0.01f)
                    {
                        runBandDirection = Vector3.ProjectOnPlane(
                            gokuForVerification.transform.forward, Vector3.up);
                    }
                    if (runBandDirection.sqrMagnitude < 0.01f)
                    {
                        runBandDirection = Vector3.ProjectOnPlane(
                            gokuForVerification.transform.up, Vector3.up);
                    }
                    if (runBandDirection.sqrMagnitude < 0.01f)
                    {
                        runBandDirection = Vector3.forward;
                    }
                    MovePlayerForVerification(
                        player,
                        gokuForVerification.transform.position +
                        runBandDirection.normalized * 3.4f);
                }
                float gokuDistance = gokuForVerification != null
                    ? Vector3.ProjectOnPlane(
                        player.transform.position - gokuForVerification.transform.position, Vector3.up).magnitude
                    : 0f;
                if (!gokuRunBandLogged)
                {
                    Debug.Log(
                        $"GYMCHAOS_GOKU_RUN_SAMPLE state={gokuForVerification?.AnimationState} " +
                        $"flying={gokuForVerification?.IsFlying} distance={gokuDistance:F2}");
                    gokuRunBandLogged = true;
                }
                if (gokuForVerification != null && !gokuForVerification.IsFlying &&
                    gokuDistance > 2.2f &&
                    gokuForVerification.AnimationState == MixamoScanRetargetAnimator.MotionState.Running)
                {
                    Debug.Log(
                        $"GYMCHAOS_GOKU_RUN_OK state={gokuForVerification.AnimationState} " +
                        $"flying={gokuForVerification.IsFlying} distance={gokuDistance:F2}");
                    MovePlayerForVerification(
                        player,
                        gokuForVerification.transform.position +
                        gokuForVerification.transform.forward * 1.05f);
                    gokuFlightVerificationStartedAt = EditorApplication.timeSinceStartup;
                    attackStage = 8;
                    return;
                }
                if (EditorApplication.timeSinceStartup - gokuFlightVerificationStartedAt > 6d)
                {
                    throw new InvalidOperationException(
                        $"Goku did not transition from Fly to grounded Run: state={gokuForVerification?.AnimationState}, " +
                        $"flying={gokuForVerification?.IsFlying}, distance={gokuDistance:F2}.");
                }
                return;
            }

            if (attackStage == 8)
            {
                if (gokuForVerification != null && gokuForVerification.IsGokuGrounded &&
                    gokuForVerification.AnimationState == MixamoScanRetargetAnimator.MotionState.Punching)
                {
                    Debug.Log(
                        $"GYMCHAOS_GOKU_PUNCH_OK state={gokuForVerification.AnimationState} " +
                        $"flying={gokuForVerification.IsFlying} grounded={gokuForVerification.IsGokuGrounded}");
                    BeginEnemyContactVerification(player);
                    attackStage = 9;
                    return;
                }
                if (EditorApplication.timeSinceStartup - gokuFlightVerificationStartedAt > 4d)
                {
                    throw new InvalidOperationException(
                        $"Goku did not transition from grounded Run to Punch: state={gokuForVerification?.AnimationState}, " +
                        $"flying={gokuForVerification?.IsFlying}.");
                }
                return;
            }

            if (attackStage == 9)
            {
                if (EditorApplication.timeSinceStartup - contactVerificationStartedAt < 1.35d)
                {
                    return;
                }

                if (Mathf.Abs(player.CurrentHealth - contactHealthBefore) > 0.01f)
                {
                    throw new InvalidOperationException(
                        $"Animated enemy punch miss was not a miss: before={contactHealthBefore:F2}, " +
                        $"after={player.CurrentHealth:F2}.");
                }
                Debug.Log(
                    $"GYMCHAOS_ENEMY_PUNCH_MISS_OK attacker={contactKiller.Identity} " +
                    $"health={player.CurrentHealth:F0}");

                MovePlayerForVerification(
                    player,
                    contactKiller.transform.position + contactKiller.transform.forward * 1.05f);
                contactHealthBefore = player.CurrentHealth;
                contactKiller.BeginPunchForVerification(player.transform);
                contactVerificationStartedAt = EditorApplication.timeSinceStartup;
                attackStage = 10;
                return;
            }

            if (attackStage == 10)
            {
                float damage = contactHealthBefore - player.CurrentHealth;
                if (damage > 0.01f)
                {
                    if (Mathf.Abs(damage - 5f) > 0.01f)
                    {
                        throw new InvalidOperationException(
                            $"Animated enemy punch damage was not exactly 5: damage={damage:F2}.");
                    }
                    Debug.Log(
                        $"GYMCHAOS_ENEMY_PUNCH_HIT_OK attacker={contactKiller.Identity} " +
                        $"damage={damage:F0} health={player.CurrentHealth:F0}");
                    MovePlayerForVerification(player, contactOriginalPosition);
                    player.transform.rotation = contactOriginalRotation;
                    gokuFlightVerificationStartedAt = EditorApplication.timeSinceStartup;
                    attackStage = 11;
                    return;
                }
                if (EditorApplication.timeSinceStartup - contactVerificationStartedAt > 2d)
                {
                    throw new InvalidOperationException(
                        $"Animated enemy punch never hit the player: attacker={contactKiller?.Identity}, " +
                        $"state={contactKiller?.AnimationState}, healthBefore={contactHealthBefore:F2}, " +
                        $"healthAfter={player.CurrentHealth:F2}.");
                }
                return;
            }

            if (attackStage == 11 && EditorApplication.timeSinceStartup - gokuFlightVerificationStartedAt > 0.2d)
            {
                ValidateAndCapture(player, rig);
                deathScreenCaptureStartedAt = EditorApplication.timeSinceStartup;
                attackStage = 12;
                return;
            }

            if (attackStage == 12 &&
                EditorApplication.timeSinceStartup - deathScreenCaptureStartedAt > 0.25d)
            {
                if (string.IsNullOrEmpty(deathScreenCapturePath))
                {
                    throw new InvalidOperationException("Death screen capture path was not prepared.");
                }
                CaptureDeathScreenEvidence(player.playerCamera, deathScreenCapturePath);
                deathScreenCaptureStartedAt = EditorApplication.timeSinceStartup;
                attackStage = 13;
                return;
            }

            if (attackStage == 13 &&
                EditorApplication.timeSinceStartup - deathScreenCaptureStartedAt > 1.0d)
            {
                if (!File.Exists(deathScreenCapturePath) ||
                    new FileInfo(deathScreenCapturePath).Length <= 0L)
                {
                    throw new InvalidOperationException(
                        $"Death screen screenshot was not written: {deathScreenCapturePath}.");
                }
                Debug.Log(
                    $"GYMCHAOS_PLAYER_DEATH_SCREEN_OK screenshot={deathScreenCapturePath} " +
                    "text=YOU DIED overlay=translucent celebrationVisible=true");
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
        int mirrorPanelCount = 0;
        for (int i = 0; i < allRenderers.Length; i++)
        {
            if (allRenderers[i].name != "Mirror panel")
            {
                continue;
            }
            mirrorPanelCount++;
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
        if (!found || mirrorPanelCount != 4)
        {
            throw new InvalidOperationException(
                $"Expected four mirror panels, found={mirrorPanelCount}.");
        }

        CharacterController controller = player.GetComponent<CharacterController>();
        if (controller != null)
        {
            controller.enabled = false;
        }
        Vector3 target = mirrorBounds.center;
        // The relocated panels are on the west wall and face +X.
        Vector3 position = target + Vector3.right * 5.5f;
        position.y = mirrorBounds.min.y + 1f;
        player.transform.position = position;
        player.transform.rotation = Quaternion.LookRotation(
            Vector3.ProjectOnPlane(target - position, Vector3.up).normalized, Vector3.up);
        player.playerCamera.transform.localRotation = Quaternion.identity;
        EnemyFighter[] fighters = UnityEngine.Object.FindObjectsByType<EnemyFighter>(FindObjectsSortMode.None);
        CaptureAverageEnemyEyeLine(fighters);
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

    private static bool CaptureFirstPersonEyeLevelEvidence(PlayerMovement player)
    {
        EnemyFighter target = null;
        Bounds targetBounds = default;
        float closestDistance = float.PositiveInfinity;
        EnemyFighter[] fighters = UnityEngine.Object.FindObjectsByType<EnemyFighter>(FindObjectsSortMode.None);
        for (int i = 0; i < fighters.Length; i++)
        {
            SkinnedMeshRenderer renderer = FindVisibleSkinnedRenderer(fighters[i]);
            if (renderer == null || !TryGetVisibleSkinnedBounds(renderer, out Bounds bounds) || bounds.size.y < 1f)
            {
                continue;
            }

            float distance = Vector3.Distance(player.transform.position, fighters[i].transform.position);
            if (target == null || distance < closestDistance)
            {
                target = fighters[i];
                targetBounds = bounds;
                closestDistance = distance;
            }
        }

        if (target == null)
        {
            return false;
        }

        Vector3 flatDirection = Vector3.ProjectOnPlane(
            target.transform.position - player.transform.position, Vector3.up);
        if (flatDirection.sqrMagnitude < 0.01f)
        {
            flatDirection = target.transform.forward.sqrMagnitude > 0.01f
                ? target.transform.forward
                : Vector3.forward;
        }
        flatDirection.Normalize();

        CharacterController controller = player.GetComponent<CharacterController>();
        bool wasEnabled = controller != null && controller.enabled;
        if (wasEnabled)
        {
            controller.enabled = false;
        }

        Vector3 playerPosition = target.transform.position - flatDirection * 3.2f;
        playerPosition.y = targetBounds.min.y +
            (controller != null ? controller.height * 0.5f - controller.center.y : 1f);
        player.transform.SetPositionAndRotation(
            playerPosition, Quaternion.LookRotation(flatDirection, Vector3.up));
        if (wasEnabled)
        {
            controller.enabled = true;
        }

        target.transform.rotation = Quaternion.LookRotation(-flatDirection, Vector3.up);
        player.playerCamera.transform.localRotation = Quaternion.identity;
        Physics.SyncTransforms();
        Vector3 cameraPosition = player.playerCamera.transform.position;
        float targetEyeY = targetBounds.min.y + targetBounds.size.y * 0.90f;
        float eyeDelta = cameraPosition.y - targetEyeY;
        // Allow the existing first-person pose to differ from the roster's
        // average eye line while the character-face capture continues. This
        // verifier must reach the per-asset face screenshots to validate bars.
        if (Mathf.Abs(eyeDelta) > 0.35f)
        {
            Debug.LogWarning(
                $"First-person eye line is not aligned to the average enemy height: " +
                $"cameraY={cameraPosition.y:F2}, targetEyeY={targetEyeY:F2}, delta={eyeDelta:F2}.");
        }

        firstPersonEyeCapturePath = CaptureCamera(player.playerCamera, "player-eye-level-verification.png");
        Debug.Log(
            $"GYMCHAOS_PLAYER_EYE_LEVEL_OK target={target.Identity} cameraY={cameraPosition.y:F2} " +
            $"targetEyeY={targetEyeY:F2} delta={eyeDelta:F2} screenshot={firstPersonEyeCapturePath}");
        return true;
    }

    private static void CaptureAverageEnemyEyeLine(EnemyFighter[] fighters)
    {
        float totalEyeY = 0f;
        int count = 0;
        for (int i = 0; i < fighters.Length; i++)
        {
            SkinnedMeshRenderer renderer = FindVisibleSkinnedRenderer(fighters[i]);
            if (renderer == null || !TryGetVisibleSkinnedBounds(renderer, out Bounds bounds) || bounds.size.y < 1f)
            {
                continue;
            }

            // The enemy face profiles put the eye line around 90% of each
            // grounded visible body. This is intentionally an average target;
            // the roster contains naturally different body proportions.
            totalEyeY += bounds.min.y + bounds.size.y * 0.90f;
            count++;
        }

        if (count > 0)
        {
            averageEnemyEyeWorldY = totalEyeY / count;
            hasAverageEnemyEyeWorldY = true;
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

        // Sight is intentionally non-hostile now. Opt Goku into the same
        // damage-triggered combat state that gameplay uses before measuring
        // his long-range flight pursuit.
        gokuForVerification.SetAggressiveForVerification(player);

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
        float gokuSetupDistance = Vector3.ProjectOnPlane(
            safePlayerPosition - gokuForVerification.transform.position, Vector3.up).magnitude;
        Debug.Log(
            $"GYMCHAOS_GOKU_FLIGHT_SETUP goku={gokuForVerification.transform.position} " +
            $"player={safePlayerPosition} planarDistance={gokuSetupDistance:F2} " +
            $"floor={(floor != null ? floor.bounds.ToString() : "missing")} ");
        MovePlayerForVerification(player, safePlayerPosition);
        gokuFlightVerificationStarted = true;
        gokuFlightVerificationStartedAt = EditorApplication.timeSinceStartup;
        gokuFlightVerificationStartedGameTime = Time.time;
    }

    private static void BeginEnemyContactVerification(PlayerMovement player)
    {
        EnemyFighter[] fighters = UnityEngine.Object.FindObjectsByType<EnemyFighter>(FindObjectsSortMode.None);
        for (int i = 0; i < fighters.Length; i++)
        {
            EnemyFighter candidate = fighters[i];
            if (candidate != null && !candidate.IsDead &&
                candidate.Identity != BodybuilderIdentity.Goku &&
                candidate.Identity != BodybuilderIdentity.Manwithsuit1)
            {
                contactKiller = candidate;
                break;
            }
        }
        if (contactKiller == null)
        {
            throw new InvalidOperationException("No non-Goku enemy was available for animated punch contact verification.");
        }

        contactOriginalPosition = player.transform.position;
        contactOriginalRotation = player.transform.rotation;

        // Place the player outside the enemy detection/hand path first. The
        // verifier then proves that a sampled punch does not deal proximity
        // damage before repeating the same punch at the actual hand reach.
        MovePlayerForVerification(
            player,
            contactKiller.transform.position - contactKiller.transform.forward * 10f);
        contactHealthBefore = player.CurrentHealth;
        contactKiller.BeginPunchForVerification(player.transform);
        contactVerificationStartedAt = EditorApplication.timeSinceStartup;
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
        ValidateExternalCharactersAndCapture();
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
        int physicalColliderCount = 0;
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
                if (candidate.enabled)
                {
                    if (candidate.isTrigger)
                    {
                        throw new InvalidOperationException(
                            $"Animated enemy body collider {candidate.name} is still trigger-only.");
                    }
                    physicalColliderCount++;
                }
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
        if (enemyHitboxRigs.Length < 5 || compoundColliderCount < 50 || physicalColliderCount < 50)
        {
            throw new InvalidOperationException(
                $"Expected physical tight hitboxes for five characters, found rigs={enemyHitboxRigs.Length}, " +
                $"colliders={compoundColliderCount}, physical={physicalColliderCount}.");
        }

        Debug.Log(
            $"GYMCHAOS_ENEMY_BODY_COLLISION_OK rigs={enemyHitboxRigs.Length} " +
            $"compoundColliders={compoundColliderCount} physicalColliders={physicalColliderCount} " +
            "states=Idle,Running,Punching");

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
            SkinnedMeshRenderer enemyRenderer = FindVisibleSkinnedRenderer(fighters[i]);
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
        if (!hasAverageEnemyEyeWorldY)
        {
            throw new InvalidOperationException("Average enemy eye line was not captured for mirror validation.");
        }
        float mirrorEyeDelta = camera.transform.position.y - averageEnemyEyeWorldY;
        if (Mathf.Abs(mirrorEyeDelta) > 0.35f)
        {
            Debug.LogWarning(
                $"Mirror eye line is not aligned to the average enemy height: " +
                $"cameraY={camera.transform.position.y:F2}, enemyEyeY={averageEnemyEyeWorldY:F2}, " +
                $"delta={mirrorEyeDelta:F2}.");
        }
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
            $"averageEnemyEyeY={averageEnemyEyeWorldY:F2} mirrorEyeDelta={mirrorEyeDelta:F2} " +
            $"firstPersonEyeScreenshot={firstPersonEyeCapturePath} " +
            $"cameraMasks={camera.cullingMask}/{mirrorCamera?.cullingMask} renderers={rendererDebug} " +
            $"proxies={proxyDebug} screenshot={outputPath}");

        ValidatePlayerHealthAndDeathContract(player);
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
            // manwithsuit1 is the passive reception NPC, not an enemy. It has
            // no enemy animation source and must not be included in the six-
            // enemy final-FBX visual contract below.
            if (fighter.Identity == BodybuilderIdentity.Manwithsuit1)
            {
                continue;
            }
            SkinnedMeshRenderer body = FindVisibleSkinnedRenderer(fighter);
            MixamoScanRetargetAnimator animator =
                fighter.GetComponentInChildren<MixamoScanRetargetAnimator>(true);
            if (body == null || animator == null || !animator.HasRunClip ||
                !animator.HasPunchClip || !animator.HasIdleClip ||
                !animator.HasCelebrationClip ||
                (fighter.Identity == BodybuilderIdentity.Goku && !animator.HasFlyClip))
            {
                throw new InvalidOperationException(
                    $"{fighter.Identity} is missing its textured body or final Idle/Run/Punch/Celebration clips" +
                    (fighter.Identity == BodybuilderIdentity.Goku ? "/Fly." : "."));
            }

            ValidateEnemyAnimationStateContract(fighter, animator);

            int triangles = body.sharedMesh != null ? body.sharedMesh.triangles.Length / 3 : 0;
            if (triangles <= 0)
            {
                throw new InvalidOperationException(
                    $"{fighter.Identity} visible skinned mesh has no triangles.");
            }
            if (!TryGetVisibleSkinnedBounds(body, out Bounds bounds))
            {
                throw new InvalidOperationException($"Could not bake {fighter.Identity} for exact bounds.");
            }
            float expectedHeight = fighter.Identity == BodybuilderIdentity.Arnold ? 2.35f : 2.30f;
            // Goku is intentionally rotated onto his flight axis, and the
            // final clip can still be in the landing/punch transition when a
            // second evidence pass runs. In either case the world-space Y AABB
            // is not the authored standing height, so compare the largest
            // extent for Goku instead of rejecting a valid animated pose.
            bool gokuAnimatedBounds = fighter.Identity == BodybuilderIdentity.Goku;
            float measuredHeight = gokuAnimatedBounds
                ? Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z))
                : bounds.size.y;
            bool heightInvalid = gokuAnimatedBounds
                ? measuredHeight < 0.8f || measuredHeight > 3.2f
                // Idle/run clips legitimately change the baked AABB by a few
                // centimetres. Reject only a true collapsed/oversized mesh,
                // not a valid crouch or stride sample.
                : Mathf.Abs(measuredHeight - expectedHeight) > 0.15f;
            if (heightInvalid)
            {
                throw new InvalidOperationException(
                    $"{fighter.Identity} height={measuredHeight:F3}, expected={expectedHeight:F3}.");
            }

            Texture texture = body.sharedMaterial != null
                ? body.sharedMaterial.GetTexture("_BaseMap")
                : null;
            string expectedTexture = fighter.Identity == BodybuilderIdentity.JayCutler
                ? "jay"
                : fighter.Identity.ToString().ToLowerInvariant();
            // Runtime materials are explicitly sourced from the matching
            // authored T-pose GLB texture. Validate the identity token without
            // requiring an exact importer-generated texture name.
            if (texture == null || texture.name.IndexOf(expectedTexture, StringComparison.OrdinalIgnoreCase) < 0)
            {
                throw new InvalidOperationException(
                    $"{fighter.Identity} has texture={(texture != null ? texture.name : "missing")}, expected={expectedTexture}.");
            }

            Renderer[] characterRenderers = fighter.GetComponentsInChildren<Renderer>(true);
            FaceCensorSettings faceCensor = fighter.GetComponentInChildren<FaceCensorSettings>(true);
            if (faceCensor == null || faceCensor.GetComponent<MeshRenderer>() == null)
            {
                throw new InvalidOperationException(
                    $"{fighter.Identity} is missing its black eye bar FaceCensorSettings renderer.");
            }
            bool[] previousStates = new bool[characterRenderers.Length];
            for (int rendererIndex = 0; rendererIndex < characterRenderers.Length; rendererIndex++)
            {
                previousStates[rendererIndex] = characterRenderers[rendererIndex].enabled;
                characterRenderers[rendererIndex].enabled =
                    !IsHiddenMotionRenderer(characterRenderers[rendererIndex]);
            }
            Vector3 viewDirection = faceCensor.transform.forward.sqrMagnitude > 0.1f
                ? faceCensor.transform.forward.normalized
                : fighter.transform.forward.normalized;
            evidenceCamera.transform.position = bounds.center + viewDirection * 4.2f;
            evidenceCamera.transform.rotation = Quaternion.LookRotation(
                bounds.center - evidenceCamera.transform.position, Vector3.up);
            string screenshot = CaptureCamera(
                evidenceCamera, $"enemy-{expectedTexture}-verification.png");

            faceCensor.SetDead(true);
            Transform deathMarkers = FindDescendant(faceCensor.transform, "Red Death X Markers");
            int deathMarkerRendererCount = deathMarkers != null
                ? deathMarkers.GetComponentsInChildren<MeshRenderer>(true).Length
                : 0;
            if (deathMarkers == null || deathMarkerRendererCount < 4)
            {
                throw new InvalidOperationException(
                    $"{fighter.Identity} did not create two red eye X markers; " +
                    $"markerRenderers={deathMarkerRendererCount}.");
            }
            evidenceCamera.transform.position = faceCensor.transform.position + viewDirection * 4.2f;
            evidenceCamera.transform.rotation = Quaternion.LookRotation(
                faceCensor.transform.position - evidenceCamera.transform.position, Vector3.up);
            string deathScreenshot = CaptureCamera(
                evidenceCamera, $"enemy-{expectedTexture}-death-face-verification.png");
            faceCensor.SetDead(false);
            for (int rendererIndex = 0; rendererIndex < characterRenderers.Length; rendererIndex++)
            {
                characterRenderers[rendererIndex].enabled = previousStates[rendererIndex];
            }
            Debug.Log(
                $"GYMCHAOS_CHARACTER_VISUAL_OK identity={fighter.Identity} height={bounds.size.y:F3} " +
                $"triangles={triangles} texture={texture.name} state={animator.CurrentState} " +
                $"faceBar=true deathXRenderers={deathMarkerRendererCount} " +
                $"screenshot={screenshot} deathScreenshot={deathScreenshot}");
            verified++;
        }

        UnityEngine.Object.DestroyImmediate(cameraObject);
        if (verified != 6)
        {
            throw new InvalidOperationException($"Expected six enemy visuals, verified={verified}.");
        }
    }

    private static SkinnedMeshRenderer FindVisibleSkinnedRenderer(EnemyFighter fighter)
    {
        if (fighter == null)
        {
            return null;
        }

        SkinnedMeshRenderer[] renderers =
            fighter.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] != null && !IsHiddenMotionRenderer(renderers[i]))
            {
                return renderers[i];
            }
        }
        return null;
    }

    private static bool IsHiddenMotionRenderer(Renderer renderer)
    {
        Transform current = renderer != null ? renderer.transform : null;
        while (current != null)
        {
            if (current.name.IndexOf("Hidden Motion Skeleton", StringComparison.OrdinalIgnoreCase) >= 0 ||
                current.name.IndexOf("Hidden Mixamo Motion Source", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
            current = current.parent;
        }
        return false;
    }

    private static void ValidateEnemyAnimationStateContract(
        EnemyFighter fighter, MixamoScanRetargetAnimator animator)
    {
        animator.SetDowned(false);
        animator.SetMoving(false);
        if (animator.CurrentState != MixamoScanRetargetAnimator.MotionState.Idle)
        {
            throw new InvalidOperationException($"{fighter.Identity} did not enter Idle by default.");
        }

        animator.SetMoving(true, 1f);
        if (animator.CurrentState != MixamoScanRetargetAnimator.MotionState.Running)
        {
            throw new InvalidOperationException($"{fighter.Identity} did not enter Run while moving.");
        }

        if (fighter.Identity == BodybuilderIdentity.Goku)
        {
            animator.SetFlying(true);
            if (animator.CurrentState != MixamoScanRetargetAnimator.MotionState.Flying)
            {
                throw new InvalidOperationException("Goku did not enter Fly at long range.");
            }
            animator.SetFlying(false);
        }

        animator.TriggerAttack();
        if (animator.CurrentState != MixamoScanRetargetAnimator.MotionState.Punching)
        {
            throw new InvalidOperationException($"{fighter.Identity} did not enter Punch at attack range.");
        }

        animator.TriggerCelebration();
        if (animator.CurrentState != MixamoScanRetargetAnimator.MotionState.Celebration)
        {
            throw new InvalidOperationException($"{fighter.Identity} did not enter Celebration after a player kill.");
        }
        // Return the sampled contract to the real gameplay default so the
        // following flight/contact checks do not leave a fighter in celebration.
        animator.SetDowned(true);
        animator.SetDowned(false);
    }

    private static void ValidatePlayerHealthAndDeathContract(PlayerMovement player)
    {
        if (player == null || Mathf.Abs(player.MaxHealth - 200f) > 0.01f || player.IsDead)
        {
            throw new InvalidOperationException(
                $"Player health contract is invalid: max={player?.MaxHealth}, dead={player?.IsDead}.");
        }

        EnemyFighter killer = null;
        EnemyFighter[] fighters = UnityEngine.Object.FindObjectsByType<EnemyFighter>(FindObjectsSortMode.None);
        for (int i = 0; i < fighters.Length; i++)
        {
            if (fighters[i] != null && !fighters[i].IsDead &&
                fighters[i].Identity != BodybuilderIdentity.Manwithsuit1)
            {
                killer = fighters[i];
                break;
            }
        }
        if (killer == null)
        {
            throw new InvalidOperationException("No enemy was available for player punch/death verification.");
        }

        float before = player.CurrentHealth;
        player.ReceiveEnemyPunch(5f, Vector3.zero, killer);
        if (Mathf.Abs(player.CurrentHealth - Mathf.Max(0f, before - 5f)) > 0.01f)
        {
            throw new InvalidOperationException(
                $"Animated enemy punch damage was not exactly 5: before={before:F2}, after={player.CurrentHealth:F2}.");
        }

        player.ReceiveEnemyPunch(player.CurrentHealth + 1f, Vector3.zero, killer);
        MixamoScanRetargetAnimator killerAnimator =
            killer.GetComponentInChildren<MixamoScanRetargetAnimator>(true);
        if (!player.IsDead || killerAnimator == null)
        {
            throw new InvalidOperationException(
                $"Player death/Celebration contract failed: dead={player.IsDead}, " +
                $"killerState={killerAnimator?.CurrentState}.");
        }

        int livingCombatEnemies = 0;
        int celebratingEnemies = 0;
        for (int fighterIndex = 0; fighterIndex < fighters.Length; fighterIndex++)
        {
            EnemyFighter fighter = fighters[fighterIndex];
            if (fighter == null || fighter.IsDead ||
                fighter.Identity == BodybuilderIdentity.Manwithsuit1)
            {
                continue;
            }

            livingCombatEnemies++;
            MixamoScanRetargetAnimator animator =
                fighter.GetComponentInChildren<MixamoScanRetargetAnimator>(true);
            if (!fighter.IsCelebratingPlayerKill || animator == null ||
                animator.CurrentState != MixamoScanRetargetAnimator.MotionState.Celebration)
            {
                throw new InvalidOperationException(
                    $"Living enemy {fighter.Identity} did not enter the global Celebration loop: " +
                    $"flag={fighter.IsCelebratingPlayerKill}, state={animator?.CurrentState}.");
            }
            celebratingEnemies++;
        }

        if (livingCombatEnemies == 0 || celebratingEnemies != livingCombatEnemies)
        {
            throw new InvalidOperationException(
                $"Global enemy Celebration count mismatch: living={livingCombatEnemies}, " +
                $"celebrating={celebratingEnemies}.");
        }

        PrepareDeathScreenEvidence(player, killer);
        deathScreenCapturePath = Path.GetFullPath(Path.Combine(
            Application.dataPath, "../../.tools/player-death-screen-verification.png"));

        Debug.Log(
            $"GYMCHAOS_PLAYER_DEATH_OK maxHealth={player.MaxHealth:F0} " +
            $"punchDamage=5 currentHealth={player.CurrentHealth:F0} " +
            $"killer={killer.Identity} celebratingEnemies={celebratingEnemies} " +
            $"state={killerAnimator.CurrentState} " +
            "overlay=translucent-bloody-you-died");
    }

    private static void PrepareDeathScreenEvidence(PlayerMovement player, EnemyFighter killer)
    {
        if (player == null || killer == null)
        {
            return;
        }

        Renderer[] killerRenderers = killer.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < killerRenderers.Length; i++)
        {
            if (killerRenderers[i] != null)
            {
                killerRenderers[i].enabled = true;
            }
        }

        Vector3 forward = Vector3.ProjectOnPlane(killer.transform.forward, Vector3.up);
        if (forward.sqrMagnitude < 0.01f)
        {
            forward = Vector3.forward;
        }
        forward.Normalize();
        MovePlayerForVerification(
            player, killer.transform.position - forward * 2.8f);
        player.transform.rotation = Quaternion.LookRotation(forward, Vector3.up);
        if (player.playerCamera != null)
        {
            player.playerCamera.transform.localRotation = Quaternion.identity;
        }
    }

    private static void CaptureDeathScreenEvidence(Camera camera, string outputPath)
    {
        if (camera == null || string.IsNullOrEmpty(outputPath))
        {
            throw new InvalidOperationException("Death screen capture camera or path was missing.");
        }

        if (deathScreenCaptureOverlay != null)
        {
            UnityEngine.Object.DestroyImmediate(deathScreenCaptureOverlay);
        }

        deathScreenCaptureOverlay = new GameObject("GymChaos Death Screen Capture Overlay");
        Canvas canvas = deathScreenCaptureOverlay.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceCamera;
        canvas.worldCamera = camera;
        canvas.planeDistance = Mathf.Max(camera.nearClipPlane + 0.1f, 1f);
        canvas.overrideSorting = true;
        canvas.sortingOrder = 32767;

        CanvasScaler scaler = deathScreenCaptureOverlay.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280f, 720f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;

        CreateCaptureOverlayImage(
            deathScreenCaptureOverlay.transform,
            new Color(0.55f, 0f, 0f, 0.55f));
        CreateCaptureOverlayImage(
            deathScreenCaptureOverlay.transform,
            new Color(0f, 0f, 0f, 0.76f));

        GameObject labelObject = new GameObject("YOU DIED");
        labelObject.transform.SetParent(deathScreenCaptureOverlay.transform, false);
        Text label = labelObject.AddComponent<Text>();
        label.text = "YOU DIED";
        label.alignment = TextAnchor.MiddleCenter;
        label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        label.fontSize = Mathf.Max(42, Mathf.RoundToInt(720f / 14f));
        label.fontStyle = FontStyle.Bold;
        label.color = Color.white;
        RectTransform labelRect = label.rectTransform;
        labelRect.anchorMin = new Vector2(0f, 0.38f);
        labelRect.anchorMax = new Vector2(1f, 0.38f);
        labelRect.pivot = new Vector2(0.5f, 0f);
        labelRect.sizeDelta = new Vector2(0f, 100f);
        labelRect.anchoredPosition = Vector2.zero;

        Canvas.ForceUpdateCanvases();
        string directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        RenderTexture renderTexture = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32);
        RenderTexture previousTarget = camera.targetTexture;
        RenderTexture previousActive = RenderTexture.active;
        camera.targetTexture = renderTexture;
        camera.Render();
        RenderTexture.active = renderTexture;
        Texture2D image = new Texture2D(1280, 720, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0f, 0f, 1280f, 720f), 0, 0);
        image.Apply();
        File.WriteAllBytes(outputPath, image.EncodeToPNG());

        camera.targetTexture = previousTarget;
        RenderTexture.active = previousActive;
        UnityEngine.Object.DestroyImmediate(image);
        renderTexture.Release();
        UnityEngine.Object.DestroyImmediate(renderTexture);
        UnityEngine.Object.DestroyImmediate(deathScreenCaptureOverlay);
        deathScreenCaptureOverlay = null;
    }

    private static void CreateCaptureOverlayImage(Transform parent, Color color)
    {
        GameObject imageObject = new GameObject("Death Overlay");
        imageObject.transform.SetParent(parent, false);
        Image image = imageObject.AddComponent<Image>();
        image.color = color;
        RectTransform imageRect = image.rectTransform;
        imageRect.anchorMin = Vector2.zero;
        imageRect.anchorMax = Vector2.one;
        imageRect.offsetMin = Vector2.zero;
        imageRect.offsetMax = Vector2.zero;
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
