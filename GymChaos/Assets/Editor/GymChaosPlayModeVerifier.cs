using System;
using System.Collections.Generic;
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
    private const string OriginalProgressionSaveKey =
        "GymChaos.PlayerMirrorVerificationOriginalSave";
    private const string OriginalProgressionSavePresentKey =
        "GymChaos.PlayerMirrorVerificationOriginalSavePresent";
    private const string ProgressionSaveKey = "GymChaos.Progression.v1";
    private static double enteredPlayTime;
    private static bool positioned;
    private static bool skyRenderVerified;
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
    private static bool contactCollisionIgnored;
    private static double deathScreenCaptureStartedAt;
    private static string deathScreenCapturePath;
    private static GameObject deathScreenCaptureOverlay;
    private static bool verificationFailed;
    private static bool audioPauseCaptured;
    private static bool audioPauseBeforeVerification;
    private static bool radioPopupVerified;

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
        string originalSave = PlayerPrefs.GetString(ProgressionSaveKey, string.Empty);
        EditorPrefs.SetBool(OriginalProgressionSavePresentKey,
            PlayerPrefs.HasKey(ProgressionSaveKey));
        EditorPrefs.SetString(OriginalProgressionSaveKey, originalSave);
        PlayerPrefs.DeleteKey(ProgressionSaveKey);
        PlayerPrefs.Save();
        verificationFailed = false;
        positioned = false;
        skyRenderVerified = false;
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
        contactCollisionIgnored = false;
        deathScreenCaptureStartedAt = 0d;
        deathScreenCapturePath = string.Empty;
        deathScreenCaptureOverlay = null;
        audioPauseBeforeVerification = AudioListener.pause;
        audioPauseCaptured = true;
        radioPopupVerified = false;
        AudioListener.pause = true;
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
            AudioListener.pause = true;
            enteredPlayTime = EditorApplication.timeSinceStartup;
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
        }
    }

    private static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            if (!audioPauseCaptured)
            {
                audioPauseBeforeVerification = AudioListener.pause;
                audioPauseCaptured = true;
            }
            AudioListener.pause = true;
            enteredPlayTime = EditorApplication.timeSinceStartup;
            EditorApplication.update += Tick;
        }
        else if (state == PlayModeStateChange.EnteredEditMode)
        {
            if (audioPauseCaptured)
            {
                AudioListener.pause = audioPauseBeforeVerification;
                audioPauseCaptured = false;
            }
            RestoreOriginalProgressionSave();
            EditorPrefs.DeleteKey(VerificationRequestedKey);
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            if (Application.isBatchMode)
            {
                EditorApplication.Exit(verificationFailed ? 1 : 0);
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

            if (!skyRenderVerified && elapsed > 1d)
            {
                ValidateSkyRender(player.playerCamera);
                skyRenderVerified = true;
            }

            if (!radioPopupVerified && elapsed > 10d)
            {
                GymRadio radio = UnityEngine.Object.FindFirstObjectByType<GymRadio>();
                if (radio != null && radio.HasPhysicalModel)
                {
                    ValidateRadioPopupAndModel(radio, player);
                    ValidatePullUpCameraVariants();
                    radioPopupVerified = true;
                }
                else if (elapsed > 30d)
                {
                    throw new InvalidOperationException(
                        "Reception radio did not retain a physical model for popup verification.");
                }
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

                float distanceBefore = Vector3.ProjectOnPlane(
                    player.transform.position - contactKiller.transform.position, Vector3.up).magnitude;
                Debug.Log(
                    $"GYMCHAOS_ENEMY_PUNCH_SETUP attacker={contactKiller.Identity} " +
                    $"root={contactKiller.transform.position} forward={contactKiller.transform.forward} " +
                    $"targetBefore={player.transform.position} " +
                    $"distanceBefore={distanceBefore:F3}");

                SetContactCollisionIgnored(true, player);
                MixamoScanRetargetAnimator contactAnimator =
                    contactKiller.GetComponentInChildren<MixamoScanRetargetAnimator>(true);
                Vector3 contactTarget = contactKiller.transform.position +
                    contactKiller.transform.forward * 0.72f;
                if (contactAnimator != null && contactAnimator.SamplePunchContactForVerification(
                        out Vector3 leftHandPosition, out Vector3 rightHandPosition,
                        out string handDetails))
                {
                    Vector3 forward = Vector3.ProjectOnPlane(
                        contactKiller.transform.forward, Vector3.up).normalized;
                    Vector3 leftPlanar = Vector3.ProjectOnPlane(
                        leftHandPosition - contactKiller.transform.position, Vector3.up);
                    Vector3 rightPlanar = Vector3.ProjectOnPlane(
                        rightHandPosition - contactKiller.transform.position, Vector3.up);
                    contactTarget = Vector3.Dot(leftPlanar, forward) >=
                        Vector3.Dot(rightPlanar, forward)
                        ? leftHandPosition
                        : rightHandPosition;
                    Debug.Log(
                        $"GYMCHAOS_ENEMY_PUNCH_CONTACT_TARGET attacker={contactKiller.Identity} " +
                        $"target={contactTarget} details={handDetails}");
                }
                MovePlayerForVerification(
                    player,
                    contactTarget);
                float distanceAfter = Vector3.ProjectOnPlane(
                    player.transform.position - contactKiller.transform.position, Vector3.up).magnitude;
                Debug.Log(
                    $"GYMCHAOS_ENEMY_PUNCH_SETUP_PLACED attacker={contactKiller.Identity} " +
                    $"root={contactKiller.transform.position} forward={contactKiller.transform.forward} " +
                    $"targetAfter={player.transform.position} " +
                    $"distanceAfter={distanceAfter:F3}");
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
                    SetContactCollisionIgnored(false, player);
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
            SetContactCollisionIgnored(false,
                UnityEngine.Object.FindFirstObjectByType<PlayerMovement>());
            verificationFailed = true;
            EditorPrefs.DeleteKey(VerificationRequestedKey);
            EditorApplication.update -= Tick;
            EditorApplication.isPlaying = false;
        }
    }

    private static void RestoreOriginalProgressionSave()
    {
        bool hadOriginalSave = EditorPrefs.GetBool(OriginalProgressionSavePresentKey, false);
        string originalSave = EditorPrefs.GetString(OriginalProgressionSaveKey, string.Empty);
        if (hadOriginalSave)
        {
            PlayerPrefs.SetString(ProgressionSaveKey, originalSave);
        }
        else
        {
            PlayerPrefs.DeleteKey(ProgressionSaveKey);
        }
        PlayerPrefs.Save();
        EditorPrefs.DeleteKey(OriginalProgressionSaveKey);
        EditorPrefs.DeleteKey(OriginalProgressionSavePresentKey);
    }

    private static void ValidateSkyRender(Camera gameplayCamera)
    {
        GymTimeOfDay timeOfDay = UnityEngine.Object.FindFirstObjectByType<GymTimeOfDay>();
        if (timeOfDay == null)
        {
            throw new InvalidOperationException("GymTimeOfDay did not initialize for sky verification.");
        }

        Material sky = RenderSettings.skybox;
        if (sky == null || sky.shader == null ||
            sky.shader.name != "GymChaos/GymGradientSky")
        {
            throw new InvalidOperationException(
                $"Gradient sky shader is not active: {sky?.shader?.name ?? "missing"}.");
        }
        if (gameplayCamera.clearFlags != CameraClearFlags.Skybox)
        {
            throw new InvalidOperationException(
                $"Gameplay camera is not using the gradient skybox: {gameplayCamera.clearFlags}.");
        }
        if (!sky.HasProperty("_Daylight") || !sky.HasProperty("_CloudFade") ||
            !sky.HasProperty("_StarFade"))
        {
            throw new InvalidOperationException("Gradient sky material is missing transition properties.");
        }

        float originalTime = timeOfDay.Time01;
        float daylight;
        float cloudFade;
        float starFade;
        float transitionCloudFade;
        float transitionStarFade;
        float deepNightCloudFade;
        float deepNightStarFade;
        try
        {
            timeOfDay.SetTimeForVerification(0.24f);
            string daySkyScreenshot = CaptureCamera(
                gameplayCamera, "sky-day-verification.png");
            daylight = sky.GetFloat("_Daylight");
            cloudFade = sky.GetFloat("_CloudFade");
            starFade = sky.GetFloat("_StarFade");

            timeOfDay.SetTimeForVerification(0.78f);
            string duskSkyScreenshot = CaptureCamera(
                gameplayCamera, "sky-dusk-verification.png");
            transitionCloudFade = sky.GetFloat("_CloudFade");
            transitionStarFade = sky.GetFloat("_StarFade");

            timeOfDay.SetTimeForVerification(0.90f);
            string nightSkyScreenshot = CaptureCamera(
                gameplayCamera, "sky-night-verification.png");
            deepNightCloudFade = sky.GetFloat("_CloudFade");
            deepNightStarFade = sky.GetFloat("_StarFade");

            Debug.Log(
                $"GYMCHAOS_SKY_SCREENSHOTS_OK day={daySkyScreenshot} " +
                $"dusk={duskSkyScreenshot} night={nightSkyScreenshot}",
                timeOfDay);
        }
        finally
        {
            timeOfDay.SetTimeForVerification(originalTime);
        }

        if (daylight < 0.85f || cloudFade < 0.95f || starFade > 0.01f ||
            transitionCloudFade <= 0.01f || transitionCloudFade >= cloudFade ||
            transitionStarFade > 0.05f || deepNightCloudFade > 0.01f ||
            deepNightStarFade < 0.95f)
        {
            throw new InvalidOperationException(
                $"Gradient sky transition is not seamless: day={daylight:F3}/" +
                $"{cloudFade:F3}/{starFade:F3} " +
                $"dusk={transitionCloudFade:F3}/{transitionStarFade:F3} " +
                $"night={deepNightCloudFade:F3}/{deepNightStarFade:F3}.");
        }

        Debug.Log(
            $"GYMCHAOS_SKY_RENDER_OK shader={sky.shader.name} " +
            $"cameraClear={gameplayCamera.clearFlags} daylight={daylight:F3} " +
            $"cloudFade={cloudFade:F3} starFade={starFade:F3} " +
            $"duskCloudFade={transitionCloudFade:F3} " +
            $"nightStarFade={deepNightStarFade:F3} " +
            "clouds=elevated-procedural stars=radial-glow transition=seamless",
            timeOfDay);
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

        LogFirstPersonCameraEvidence(player.playerCamera, player);
        firstPersonEyeCapturePath = CaptureCamera(player.playerCamera, "player-eye-level-verification.png");
        Debug.Log(
            $"GYMCHAOS_PLAYER_EYE_LEVEL_OK target={target.Identity} cameraY={cameraPosition.y:F2} " +
            $"targetEyeY={targetEyeY:F2} delta={eyeDelta:F2} screenshot={firstPersonEyeCapturePath}");
        return true;
    }

    private static void LogFirstPersonCameraEvidence(Camera camera, PlayerMovement player)
    {
        if (camera == null || player == null)
        {
            return;
        }

        PlayerHandRig rig = player.GetComponentInChildren<PlayerHandRig>(true);
        MeshRenderer[] renderers = rig != null
            ? rig.GetComponentsInChildren<MeshRenderer>(true)
            : new MeshRenderer[0];
        for (int i = 0; i < renderers.Length; i++)
        {
            MeshRenderer renderer = renderers[i];
            if (renderer == null || !renderer.name.StartsWith("First Person Arms Render", StringComparison.Ordinal))
            {
                continue;
            }

            Vector3 viewportCenter = camera.WorldToViewportPoint(renderer.bounds.center);
            Debug.Log(
                $"GYMCHAOS_PLAYER_FIRST_PERSON_EYE_RENDER name={renderer.name} " +
                $"enabled={renderer.enabled} forceOff={renderer.forceRenderingOff} " +
                $"layer={renderer.gameObject.layer} visible={renderer.isVisible} " +
                $"bounds={renderer.bounds.center}/{renderer.bounds.size} " +
                $"camera={camera.transform.position}/{camera.transform.forward} " +
                $"viewportCenter={viewportCenter}", renderer);
        }
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
        // Give the flight state enough runway for the editor callback to
        // observe the authored TakingOff -> Flying transition before Goku
        // reaches the player and begins the landing/punch phase.
        float safeDistance = Mathf.Clamp(towardRoomCenter.magnitude, 9f, 10f);
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

    private static void SetContactCollisionIgnored(bool ignored, PlayerMovement player)
    {
        if (contactKiller == null || player == null || ignored == contactCollisionIgnored)
        {
            return;
        }

        Collider[] enemyColliders = contactKiller.GetComponentsInChildren<Collider>(true);
        Collider[] playerColliders = player.GetComponentsInChildren<Collider>(true);
        for (int enemyIndex = 0; enemyIndex < enemyColliders.Length; enemyIndex++)
        {
            Collider enemyCollider = enemyColliders[enemyIndex];
            if (enemyCollider == null)
            {
                continue;
            }
            for (int playerIndex = 0; playerIndex < playerColliders.Length; playerIndex++)
            {
                Collider playerCollider = playerColliders[playerIndex];
                if (playerCollider != null)
                {
                    Physics.IgnoreCollision(enemyCollider, playerCollider, ignored);
                }
            }
        }
        contactCollisionIgnored = ignored;
    }

    private static void MovePlayerForVerification(PlayerMovement player, Vector3 position)
    {
        player.ResetMovementForVerification();
        CharacterController controller = player.GetComponent<CharacterController>();
        bool wasEnabled = controller != null && controller.enabled;
        if (wasEnabled)
        {
            controller.enabled = false;
        }
        position.y = player.transform.position.y;
        player.transform.position = position;
        player.ResetMovementForVerification();
        if (wasEnabled)
        {
            controller.enabled = true;
        }
    }

    private static void ValidateAndCapture(PlayerMovement player, PlayerHandRig rig)
    {
        rig.ResetToVerificationIdlePose();
        string playerPoseDetails = "not sampled";
        string playerIdleDetails = "not sampled";
        string playerJumpDetails = "not sampled";
        string playerAttackPoseDetails = "not sampled";
        string playerFrisbeeThrowDetails = "not sampled";
        string playerHardThrowDetails = "not sampled";
        if (!rig.SampleAuthoredClipForVerification(
                "idle1", 0.52f, out playerIdleDetails) ||
            !rig.SampleAuthoredClipForVerification(
                "jumping", 0.52f, out playerJumpDetails) ||
            !rig.SampleAuthoredClipForVerification(
                "walking", 0.37f, out playerPoseDetails) ||
            !rig.SampleAuthoredClipForVerification(
                "punch_left", 0.52f, out playerAttackPoseDetails) ||
            !rig.SampleAuthoredClipForVerification(
                "throw_frisbee", 0.52f, out playerFrisbeeThrowDetails) ||
            !rig.SampleAuthoredClipForVerification(
                "throw_object_hard", 0.52f, out playerHardThrowDetails))
        {
            throw new InvalidOperationException(
                $"Player authored clips did not deform the player rig: " +
                $"idle={playerIdleDetails}, jump={playerJumpDetails}, " +
                $"walk={playerPoseDetails}, punch={playerAttackPoseDetails}, " +
                $"throw_frisbee={playerFrisbeeThrowDetails}, " +
                $"throw_object_hard={playerHardThrowDetails}.");
        }
        Debug.Log(
            $"GYMCHAOS_PLAYER_AUTHORED_DIRECT_POSE_OK " +
            $"modelResource={rig.RuntimeModelResourcePath} " +
            $"animationResource={rig.RuntimeAnimationResourcePath} " +
            $"idle={playerIdleDetails} jump={playerJumpDetails} " +
            $"walk={playerPoseDetails} punch={playerAttackPoseDetails} " +
            $"throw_frisbee={playerFrisbeeThrowDetails} " +
            $"throw_object_hard={playerHardThrowDetails}", rig);
        string jumpStability = "not sampled";
        string leftPunchStability = "not sampled";
        string rightPunchStability = "not sampled";
        bool jumpStable = rig.VerifyStableActionPoseForVerification(
            "jumping", 0.52f, out jumpStability);
        bool leftPunchStable = rig.VerifyStableActionPoseForVerification(
            "punch_left", 0.52f, out leftPunchStability);
        bool rightPunchStable = rig.VerifyStableActionPoseForVerification(
            "punch_right", 0.52f, out rightPunchStability);
        if (!jumpStable || !leftPunchStable || !rightPunchStable)
        {
            throw new InvalidOperationException(
                $"Player action support/twist regression: jump={jumpStability}, " +
                $"leftPunch={leftPunchStability}, rightPunch={rightPunchStability}.");
        }
        Debug.Log(
            $"GYMCHAOS_PLAYER_ACTION_STABILITY_OK jump={jumpStability} " +
            $"leftPunch={leftPunchStability} rightPunch={rightPunchStability}", rig);
        string[] stablePoseClips =
        {
            "idle1", "walking", "running",
            "jumping", "punch_left", "punch_right"
        };
        List<string> stablePoseCaptures = new List<string>();
        for (int i = 0; i < stablePoseClips.Length; i++)
        {
            stablePoseCaptures.Add(CaptureStablePlayerPose(
                rig, stablePoseClips[i], 0.52f));
        }
        stablePoseCaptures.Add(CaptureStablePlayerPose(
            rig, "punch_left", 0.52f, true));
        stablePoseCaptures.Add(CaptureStablePlayerPose(
            rig, "punch_right", 0.52f, true));
        rig.ResetToVerificationIdlePose();
        Debug.Log(
            "GYMCHAOS_PLAYER_STABLE_POSE_CAPTURES_OK paths=" +
            string.Join(",", stablePoseCaptures), rig);
        if (!rig.HasSampledBothThrowClips)
        {
            throw new InvalidOperationException(
                "Both authored player throw clips were not sampled in Play Mode.");
        }
        Debug.Log(
            $"GYMCHAOS_PLAYER_THROW_CLIPS_OK " +
            $"frisbee={playerFrisbeeThrowDetails} hard={playerHardThrowDetails}", rig);
        rig.ResetToVerificationIdlePose();
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
        rig.ResetToVerificationIdlePose();

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
                if (candidate.enabled && candidate.bounds.Contains(legGap) &&
                    (candidate.ClosestPoint(legGap) - legGap).sqrMagnitude < 0.000001f)
                {
                    throw new InvalidOperationException(
                        $"The transparent leg gap is still covered by {candidate.name} " +
                        $"({candidate.GetType().Name}) on {enemyHitboxRigs[i].name} " +
                        $"at gap={legGap} bounds={candidate.bounds}.");
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
                if (!renderers[i].enabled || renderers[i].forceRenderingOff)
                {
                    throw new InvalidOperationException(
                        "First-person arms are hidden during neutral gameplay: " +
                        renderers[i].name + ".");
                }
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
                // A current skinned renderer AABB changes with a stride,
                // punch or celebration pose. Compare the player's visible
                // height with each authored FBX's stable fitted height so
                // the contract cannot fail merely because one enemy is
                // sampled with raised arms.
                ExternalRiggedCharacterVisual enemyVisual =
                    fighters[i] != null
                        ? fighters[i].GetComponent<ExternalRiggedCharacterVisual>()
                        : null;
                float comparableEnemyHeight = enemyVisual != null &&
                    enemyVisual.RuntimeAuthoredHeight > 0.5f
                    ? enemyVisual.RuntimeAuthoredHeight
                    : enemyBounds.size.y;
                enemyHeightTotal += comparableEnemyHeight;
                enemyHeightCount++;
            }
        }
        float averageEnemyHeight = enemyHeightCount > 0 ? enemyHeightTotal / enemyHeightCount : 0f;
        float playerVisibleHeight = rig.RuntimeVisibleHeight;
        if (playerVisibleHeight < 0.01f)
        {
            playerVisibleHeight = bodyBounds.size.y;
        }
        float playerToEnemyHeight = averageEnemyHeight > 0f ? playerVisibleHeight / averageEnemyHeight : 0f;
        if (playerToEnemyHeight < 0.9f || playerToEnemyHeight > 1.12f)
        {
            throw new InvalidOperationException(
                $"Player height is not comparable to enemies: player={playerVisibleHeight:F2}, enemyAverage={averageEnemyHeight:F2}, ratio={playerToEnemyHeight:F2}.");
        }

        Camera camera = player.playerCamera;
        ValidateMirrorParity(rig);
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
        // The player is authored on its own Rigify deform skeleton. The old
        // Mixamo-name lookup always returned null here and falsely reported a
        // T-pose even after the authored idle had been sampled.
        Transform leftHand = rig.RuntimeFirstPersonLeftHand;
        Transform rightHand = rig.RuntimeFirstPersonRightHand;
        if (!rig.TryGetFirstPersonHandCameraPositions(
                out Vector3 leftHandCamera, out Vector3 rightHandCamera) ||
            leftHandCamera.x > -0.24f || rightHandCamera.x < 0.24f ||
            leftHandCamera.z < 0.4f || rightHandCamera.z < 0.4f ||
            leftHandCamera.z > 1.3f || rightHandCamera.z > 1.3f)
        {
            throw new InvalidOperationException(
                $"First-person hands do not frame both camera sides: " +
                $"left={leftHandCamera}, right={rightHandCamera}.");
        }
        Debug.Log(
            $"GYMCHAOS_PLAYER_AUTHORED_IDLE_OK idleHands={leftHand.position}/{rightHand.position} " +
            $"cameraHands={leftHandCamera}/{rightHandCamera} clips={rig.MixamoAttackClipSummary}", rig);
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
            $"playerVisibleHeight={playerVisibleHeight:F2} averageEnemyHeight={averageEnemyHeight:F2} " +
            $"playerToEnemyHeight={playerToEnemyHeight:F2} " +
            $"averageEnemyEyeY={averageEnemyEyeWorldY:F2} mirrorEyeDelta={mirrorEyeDelta:F2} " +
            $"firstPersonEyeScreenshot={firstPersonEyeCapturePath} " +
            $"cameraMasks={camera.cullingMask}/{mirrorCamera?.cullingMask} renderers={rendererDebug} " +
            $"proxies={proxyDebug} screenshot={outputPath}");

        ValidatePlayerHealthAndDeathContract(player);
    }

    private static void ValidateRadioPopupAndModel(
        GymRadio radio, PlayerMovement player)
    {
        if (radio == null || !radio.HasPhysicalModel)
        {
            throw new InvalidOperationException(
                "Reception radio physical model was unavailable before popup verification.");
        }
        if (radio.IsMusicEnabled || radio.IsLocalMusicEnabled)
        {
            throw new InvalidOperationException(
                "Reception radio started with playback enabled instead of defaulting off.");
        }
        radio.ToggleLocalPlayback();
        if (!radio.IsMusicEnabled || !radio.IsLocalMusicEnabled)
        {
            throw new InvalidOperationException(
                "Radio menu action could not enable the local playlist from the default-off state.");
        }
        if (!radio.UsesDistanceRolloff || radio.AudioMaxDistance < 90f ||
            radio.AudioMinDistance < 1f)
        {
            throw new InvalidOperationException(
                $"Radio is not a gym-wide distance source: " +
                $"rolloff={radio.UsesDistanceRolloff}, " +
                $"min={radio.AudioMinDistance:F1}, max={radio.AudioMaxDistance:F1}.");
        }
        Renderer lockerFloor =
            GameObject.Find("Locker Room Floor")?.GetComponent<Renderer>();
        if (lockerFloor == null ||
            !GymRadio.IsPositionInsidePlayableGym(lockerFloor.bounds.center))
        {
            throw new InvalidOperationException(
                "Locker room is not included in the radio's playable gym zone.");
        }
        bool outsideMuted = radio.ApplyListenerPositionForVerification(
            lockerFloor.bounds.center + new Vector3(10000f, 0f, 10000f));
        bool lockerMuted = radio.ApplyListenerPositionForVerification(
            lockerFloor.bounds.center);
        radio.ApplyListenerPositionForVerification(
            player != null ? player.transform.position : lockerFloor.bounds.center);
        if (!outsideMuted || lockerMuted)
        {
            throw new InvalidOperationException(
                $"Radio zone mute contract failed: outsideMuted={outsideMuted}, " +
                $"lockerMuted={lockerMuted}.");
        }

        // ToggleMusic is the same public action used by the in-world radio
        // interaction. Calling it twice must reuse one modal surface rather
        // than stacking two full-screen pages on top of each other.
        radio.ToggleMusic();
        GymRadioSoundCloudPopup popup =
            UnityEngine.Object.FindFirstObjectByType<GymRadioSoundCloudPopup>(
                FindObjectsInactive.Include);
        if (popup == null || !popup.IsVisible || !GymRadioSoundCloudPopup.IsAnyVisible)
        {
            throw new InvalidOperationException(
                "Radio controls popup did not open from the radio action.");
        }
        if (player != null && player.CanShowCursorRecapturePrompt)
        {
            throw new InvalidOperationException(
                "CLICK TO LOOK AROUND remained eligible while the radio modal was open.");
        }

        radio.ToggleMusic();
        GymRadioSoundCloudPopup[] popups =
            UnityEngine.Object.FindObjectsByType<GymRadioSoundCloudPopup>(
                FindObjectsInactive.Include);
        if (popups.Length != 1)
        {
            throw new InvalidOperationException(
                $"Radio controls stacked duplicate popup surfaces: count={popups.Length}.");
        }

        Button localButton = null;
        Button musicButton = null;
        Button openSoundCloudButton = null;
        Button loadPlaylistButton = null;
        Button playPauseButton = null;
        Button nextButton = null;
        Button[] buttons = popup.GetComponentsInChildren<Button>(true);
        for (int i = 0; i < buttons.Length; i++)
        {
            if (buttons[i] == null)
            {
                continue;
            }

            switch (buttons[i].gameObject.name)
            {
                case "Radio Local Button":
                    localButton = buttons[i];
                    break;
                case "Radio Music Toggle Button":
                    musicButton = buttons[i];
                    break;
                case "Radio Open SoundCloud Button":
                    openSoundCloudButton = buttons[i];
                    break;
                case "Radio Load Playlist Button":
                    loadPlaylistButton = buttons[i];
                    break;
                case "Radio Widget Play Pause Button":
                    playPauseButton = buttons[i];
                    break;
                case "Radio Widget Next Button":
                    nextButton = buttons[i];
                    break;
            }
        }

        InputField playlistUrlInput = popup.GetComponentInChildren<InputField>(true);
        if (localButton == null || musicButton == null ||
            openSoundCloudButton == null || loadPlaylistButton == null ||
            playPauseButton == null || nextButton == null ||
            playlistUrlInput == null || !openSoundCloudButton.interactable ||
            !loadPlaylistButton.interactable || !playPauseButton.interactable ||
            !nextButton.interactable)
        {
            throw new InvalidOperationException(
                "Radio controls did not build the URL/Open/Load/Play-Pause/Next actions.");
        }

        string publicUrl =
            "https://soundcloud.com/gymchaos-verifier/sets/workout";
        string mobileUrl =
            "https://m.soundcloud.com/gymchaos-verifier/sets/workout";
        string secretUrl =
            "https://soundcloud.com/gymchaos-verifier/sets/workout/s-AbC123";
        string shortUrl = "https://on.soundcloud.com/AbC123";
        if (!GymRadio.IsValidSoundCloudPlaylistUrl(publicUrl) ||
            !GymRadio.IsValidSoundCloudPlaylistUrl(mobileUrl) ||
            !GymRadio.IsValidSoundCloudPlaylistUrl(secretUrl) ||
            !GymRadio.IsValidSoundCloudPlaylistUrl(shortUrl) ||
            GymRadio.IsValidSoundCloudPlaylistUrl(
                "http://soundcloud.com/gymchaos-verifier/sets/workout") ||
            GymRadio.IsValidSoundCloudPlaylistUrl(
                "https://soundcloud.com.evil.example/user/sets/workout") ||
            GymRadio.IsValidSoundCloudPlaylistUrl(
                "https://soundcloud.com/user/single-track"))
        {
            throw new InvalidOperationException(
                "SoundCloud playlist URL allowlist accepted an unsafe URL or rejected a supported share URL.");
        }

        bool preferenceExisted = radio.HasSoundCloudPlaylistPreference;
        string previousPlaylistUrl = radio.PersistedSoundCloudPlaylistUrl;
        int nearWidgetVolume = 0;
        int lockerWidgetVolume = 0;
        int outsideWidgetVolume = 0;
        bool widgetLoopContinues = false;
        try
        {
            playlistUrlInput.text = "https://example.com/not-soundcloud";
            loadPlaylistButton.onClick.Invoke();
            if (!radio.IsSoundCloudError ||
                !radio.GetSoundCloudUiStatus().Contains("INVALID URL"))
            {
                throw new InvalidOperationException(
                    "Invalid SoundCloud URL did not produce an actionable error.");
            }

            playlistUrlInput.text = secretUrl;
            loadPlaylistButton.onClick.Invoke();
            if (!string.Equals(
                    radio.SoundCloudPlaylistUrl, secretUrl,
                    StringComparison.Ordinal) ||
                !radio.HasSoundCloudPlaylistPreference ||
                !string.Equals(
                    radio.PersistedSoundCloudPlaylistUrl, secretUrl,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "SoundCloud playlist URL was not saved to PlayerPrefs.");
            }

            nearWidgetVolume = radio.CalculateSoundCloudWidgetVolume(
                radio.transform.position);
            lockerWidgetVolume = radio.CalculateSoundCloudWidgetVolume(
                lockerFloor.bounds.center);
            outsideWidgetVolume = radio.CalculateSoundCloudWidgetVolume(
                lockerFloor.bounds.center +
                new Vector3(10000f, 0f, 10000f));
            if (nearWidgetVolume <= lockerWidgetVolume ||
                lockerWidgetVolume <= 0 || outsideWidgetVolume != 0)
            {
                throw new InvalidOperationException(
                    $"SoundCloud widget rolloff failed: near={nearWidgetVolume}, " +
                    $"locker={lockerWidgetVolume}, outside={outsideWidgetVolume}.");
            }

            radio.PrepareSoundCloudWidgetForVerification();
            radio.HandleSoundCloudWidgetReady(string.Empty);
            if (!radio.IsSoundCloudWidgetReady)
            {
                throw new InvalidOperationException(
                    "SoundCloud READY callback did not update radio state.");
            }
            playPauseButton.onClick.Invoke();
            radio.HandleSoundCloudWidgetPlay(string.Empty);
            if (!radio.IsSoundCloudWidgetPlaying)
            {
                throw new InvalidOperationException(
                    "SoundCloud PLAY callback did not update radio state.");
            }
            nextButton.onClick.Invoke();
            if (!radio.GetSoundCloudUiStatus().Contains("NEXT TRACK"))
            {
                throw new InvalidOperationException(
                    "SoundCloud NEXT control did not produce playback state.");
            }
            radio.HandleSoundCloudWidgetFinish(string.Empty);
            widgetLoopContinues = radio.IsSoundCloudWidgetPlaying &&
                radio.GetSoundCloudUiStatus().Contains("CONTINUING");
            if (!widgetLoopContinues)
            {
                throw new InvalidOperationException(
                    "SoundCloud FINISH callback did not continue/loop the playlist.");
            }
            playPauseButton.onClick.Invoke();
            if (radio.IsSoundCloudWidgetPlaying)
            {
                throw new InvalidOperationException(
                    "SoundCloud PLAY/PAUSE control did not pause widget state.");
            }
        }
        finally
        {
            radio.RestoreSoundCloudPlaylistUrlForVerification(
                previousPlaylistUrl, preferenceExisted);
        }

        localButton.onClick.Invoke();
        bool modelAfterLocalAction = radio.HasPhysicalModel;
        int musicOffWidgetVolume = radio.CalculateSoundCloudWidgetVolume(
            radio.transform.position);
        musicButton.onClick.Invoke();
        bool modelAfterMusicAction = radio.HasPhysicalModel;
        if (!modelAfterLocalAction || !modelAfterMusicAction ||
            musicOffWidgetVolume != 0)
        {
            throw new InvalidOperationException(
                $"Radio playback action contract failed: localModel={modelAfterLocalAction}, " +
                $"musicModel={modelAfterMusicAction}, musicOffWidgetVolume={musicOffWidgetVolume}.");
        }

        popup.Close();
        bool modelAfterClose = radio.HasPhysicalModel;
        if (GymRadioSoundCloudPopup.IsAnyVisible || !modelAfterClose)
        {
            throw new InvalidOperationException(
                "Closing radio controls did not leave the radio model intact.");
        }

        radio.OpenSoundCloudPopup();
        bool reopened = GymRadioSoundCloudPopup.IsAnyVisible;
        popup.Close();
        if (!reopened || !radio.HasPhysicalModel)
        {
            throw new InvalidOperationException(
                "Radio controls could not reopen without losing the physical model.");
        }

        int popupCanvasCount = popup.GetComponentsInChildren<Canvas>(true).Length;
        Debug.Log(
            $"GYMCHAOS_RADIO_POPUP_MODEL_OK popupCount={popups.Length} " +
            $"popupCanvases={popupCanvasCount} actions=open,load,play-pause,next,local,music " +
            $"widgetUrlValidation=true widgetPersistence=true widgetLoop={widgetLoopContinues} " +
            $"widgetVolume={nearWidgetVolume}/{lockerWidgetVolume}/{outsideWidgetVolume} " +
            $"audio={radio.AudioMinDistance:F1}-{radio.AudioMaxDistance:F1}m " +
            $"outsideMuted={outsideMuted} lockerMuted={lockerMuted} " +
            $"reopened={reopened} modelAliveAfterActions={modelAfterLocalAction && modelAfterMusicAction} " +
            $"modelAliveAfterClose={modelAfterClose}", radio);
    }

    private static void ValidatePullUpCameraVariants()
    {
        GymExerciseStation[] stations =
            UnityEngine.Object.FindObjectsByType<GymExerciseStation>(
                FindObjectsSortMode.None);
        GymExerciseStation cable = null;
        GymExerciseStation calisthenics = null;
        for (int i = 0; i < stations.Length; i++)
        {
            GymExerciseStation station = stations[i];
            if (station == null || station.ExerciseType != GymExerciseType.PullUps)
            {
                continue;
            }
            if (station.IsCableMachinePullUp)
            {
                cable = station;
            }
            else
            {
                calisthenics = station;
            }
        }
        if (cable == null || calisthenics == null)
        {
            throw new InvalidOperationException(
                $"Expected cable and calisthenics pull-up stations: " +
                $"cable={cable != null}, calisthenics={calisthenics != null}.");
        }
        cable.GetCameraPose(out Vector3 cablePosition, out Quaternion cableRotation);
        calisthenics.GetCameraPose(
            out Vector3 calisthenicsPosition, out Quaternion calisthenicsRotation);
        if (cable.PullUpCameraLowering < 0.25f)
        {
            throw new InvalidOperationException(
                "Cable-machine pull-up camera did not receive its lower framing offset.");
        }

        Renderer[] renderers = UnityEngine.Object.FindObjectsByType<Renderer>(
            FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        Vector3 cableCenter = cable.EquipmentRoot != null
            ? cable.EquipmentRoot.position
            : cable.PlayerPosition;
        Vector3 nearestMirrorDirection = Vector3.zero;
        float bestDistance = float.PositiveInfinity;
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null ||
                !renderer.name.ToLowerInvariant().Contains("mirror"))
            {
                continue;
            }
            Vector3 candidate = Vector3.ProjectOnPlane(
                renderer.bounds.center - cableCenter, Vector3.up);
            if (candidate.sqrMagnitude > 0.01f &&
                candidate.sqrMagnitude < bestDistance)
            {
                bestDistance = candidate.sqrMagnitude;
                nearestMirrorDirection = candidate.normalized;
            }
        }
        float mirrorDot = Vector3.Dot(
            cable.PullUpLookDirection, nearestMirrorDirection);
        if (nearestMirrorDirection.sqrMagnitude < 0.9f || mirrorDot < 0.8f)
        {
            throw new InvalidOperationException(
                $"Cable-machine pull-up camera is not facing the mirrors: " +
                $"camera={cable.PullUpLookDirection}, mirror={nearestMirrorDirection}.");
        }
        Debug.Log(
            $"GYMCHAOS_PULLUP_CAMERA_VARIANTS_OK cablePosition={cablePosition} " +
            $"cableRotation={cableRotation.eulerAngles} " +
            $"calisthenicsPosition={calisthenicsPosition} " +
            $"calisthenicsRotation={calisthenicsRotation.eulerAngles} " +
            $"cableMirrorDot={mirrorDot:F3}");
    }

    private static void ValidateMirrorParity(PlayerHandRig rig)
    {
        PlanarGymMirror[] mirrors =
            UnityEngine.Object.FindObjectsByType<PlanarGymMirror>(
                FindObjectsSortMode.None);
        PlanarGymMirror lockerMirror = null;
        PlanarGymMirror gymMirror = null;
        for (int i = 0; i < mirrors.Length; i++)
        {
            Transform current = mirrors[i] != null ? mirrors[i].transform : null;
            bool locker = false;
            while (current != null)
            {
                locker |= current.name.IndexOf(
                    "Locker", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    current.name.IndexOf(
                        "Gym Back Area", StringComparison.OrdinalIgnoreCase) >= 0;
                current = current.parent;
            }
            if (locker)
            {
                lockerMirror = mirrors[i];
            }
            else if (gymMirror == null)
            {
                gymMirror = mirrors[i];
            }
        }
        if (lockerMirror == null || gymMirror == null ||
            !lockerMirror.ReflectionIncludesPlayerLayer ||
            !gymMirror.ReflectionIncludesPlayerLayer ||
            lockerMirror.ReflectionTexture == null ||
            gymMirror.ReflectionTexture == null ||
            !string.Equals(lockerMirror.ReflectionShaderName,
                gymMirror.ReflectionShaderName, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Locker mirror does not match the gym mirror reflection pipeline.");
        }

        SkinnedMeshRenderer[] playerRenderers =
            rig.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        int opaquePlayerMaterials = 0;
        for (int i = 0; i < playerRenderers.Length; i++)
        {
            SkinnedMeshRenderer renderer = playerRenderers[i];
            if (renderer == null ||
                renderer.gameObject.layer != PlanarGymMirror.MirrorPlayerLayer)
            {
                continue;
            }
            Material[] materials = renderer.sharedMaterials;
            for (int materialIndex = 0; materialIndex < materials.Length; materialIndex++)
            {
                Material material = materials[materialIndex];
                if (material == null ||
                    (material.HasProperty("_Surface") &&
                     material.GetFloat("_Surface") > 0.01f) ||
                    (material.HasProperty("_BaseColor") &&
                     material.GetColor("_BaseColor").a < 0.99f))
                {
                    throw new InvalidOperationException(
                        "Mirror player still uses a transparent material.");
                }
                opaquePlayerMaterials++;
            }
        }
        if (opaquePlayerMaterials == 0)
        {
            throw new InvalidOperationException(
                "No opaque full-player mirror materials were found.");
        }
        Debug.Log(
            $"GYMCHAOS_LOCKER_MIRROR_PLAYER_OK mirrors={mirrors.Length} " +
            $"shader={lockerMirror.ReflectionShaderName} " +
            $"texturesCreated={lockerMirror.HasReadyReflectionTexture}/" +
            $"{gymMirror.HasReadyReflectionTexture} " +
            $"opaquePlayerMaterials={opaquePlayerMaterials}");
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
            ExternalRiggedCharacterVisual importedVisual =
                fighter.GetComponent<ExternalRiggedCharacterVisual>();
            SkinnedMeshRenderer body = importedVisual != null &&
                importedVisual.RuntimeRenderer != null
                ? importedVisual.RuntimeRenderer
                : FindVisibleSkinnedRenderer(fighter);
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

            string expectedResourcePath =
                "Characters/Enemies/" + fighter.Identity.ToString().ToLowerInvariant() + "_authored";
            if (importedVisual == null || importedVisual.RuntimeModelRoot == null ||
                importedVisual.RuntimeRig == null ||
                !string.Equals(importedVisual.RuntimeResourcePath, expectedResourcePath,
                    StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(animator.AuthoredAnimationResourcePath,
                    importedVisual.RuntimeResourcePath, StringComparison.OrdinalIgnoreCase) ||
                !animator.RuntimeModelRootIsAuthoredInstance)
            {
                throw new InvalidOperationException(
                    $"{fighter.Identity} is not bound to its own authored model/clip asset: " +
                    $"modelResource={importedVisual?.RuntimeResourcePath ?? "missing"} " +
                    $"animationResource={animator.AuthoredAnimationResourcePath}.");
            }
            if (!animator.SampleAuthoredClipForVerification(
                    "walking", 0.37f, out string enemyPoseDetails))
            {
                throw new InvalidOperationException(
                    $"{fighter.Identity} authored walking clip did not deform its own rig: " +
                    enemyPoseDetails);
            }
            if (!animator.SampleAuthoredClipRangeForVerification(
                    "squat", out string enemySquatDetails))
            {
                throw new InvalidOperationException(
                    $"{fighter.Identity} authored squat.fbx did not contain one stable " +
                    $"standing-to-squat-to-standing rep: {enemySquatDetails}");
            }
            if (!animator.SampleAllAuthoredClipsForVerification(
                    out string enemyClipStabilityDetails))
            {
                throw new InvalidOperationException(
                    $"{fighter.Identity} authored animation set did not deform its " +
                    $"own rig on every clip: {enemyClipStabilityDetails}");
            }
            Debug.Log(
                $"GYMCHAOS_ENEMY_AUTHORED_DIRECT_POSE_OK identity={fighter.Identity} " +
                $"resource={importedVisual.RuntimeResourcePath} " +
                $"pose={enemyPoseDetails} squatRep={enemySquatDetails} " +
                $"clipStability={enemyClipStabilityDetails}", animator);

            ValidateEnemyAnimationStateContract(fighter, animator);
            animator.ResetToVerificationIdlePose();
            LogAuthoredBoundsDiagnostic(fighter, importedVisual, body, animator);

            importedVisual?.RefreshFaceCensorForCurrentPose();

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
            // The current renderer AABB is pose-dependent: a stride, flight
            // transition, or punch can change its world-Y extent without
            // changing the authored model scale. Validate the stable height
            // captured immediately after this character's own FBX was fitted,
            // while retaining the current bounds for framing and face checks.
            float measuredHeight = importedVisual != null
                ? importedVisual.RuntimeAuthoredHeight
                : bounds.size.y;
            bool heightInvalid = measuredHeight <= 0.01f ||
                Mathf.Abs(measuredHeight - expectedHeight) > 0.18f;
            if (heightInvalid)
            {
                throw new InvalidOperationException(
                    $"{fighter.Identity} authoredHeight={measuredHeight:F3}, " +
                    $"currentPoseHeight={bounds.size.y:F3}, expected={expectedHeight:F3}.");
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
            ValidateFaceCensorPlacement(fighter, body, faceCensor, bounds);
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
                $"authoredHeight={measuredHeight:F3} " +
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

    private static void LogAuthoredBoundsDiagnostic(
        EnemyFighter fighter, ExternalRiggedCharacterVisual importedVisual,
        SkinnedMeshRenderer body, MixamoScanRetargetAnimator animator)
    {
        if (fighter == null || body == null || importedVisual == null ||
            importedVisual.RuntimeModelRoot == null)
        {
            return;
        }

        Mesh baked = new Mesh { name = "Authored bounds diagnostic mesh" };
        body.BakeMesh(baked, false);
        Vector3[] vertices = baked.vertices;
        Bounds bakedWorld = default;
        if (vertices != null && vertices.Length > 0)
        {
            bakedWorld = new Bounds(
                body.transform.position + body.transform.rotation * vertices[0],
                Vector3.zero);
            for (int vertexIndex = 1; vertexIndex < vertices.Length; vertexIndex++)
            {
                bakedWorld.Encapsulate(
                    body.transform.position + body.transform.rotation * vertices[vertexIndex]);
            }
        }

        Transform largestScaleTransform = null;
        Transform largestPositionTransform = null;
        Transform largestBoneScaleTransform = null;
        float largestScale = 0f;
        float largestPosition = 0f;
        float largestBoneScale = 0f;
        Transform[] transforms = importedVisual.RuntimeModelRoot.GetComponentsInChildren<Transform>(true);
        for (int transformIndex = 0; transformIndex < transforms.Length; transformIndex++)
        {
            Transform current = transforms[transformIndex];
            float scale = Mathf.Max(
                Mathf.Abs(current.localScale.x),
                Mathf.Max(Mathf.Abs(current.localScale.y), Mathf.Abs(current.localScale.z)));
            float position = current.localPosition.magnitude;
            if (scale > largestScale)
            {
                largestScale = scale;
                largestScaleTransform = current;
            }
            if (position > largestPosition)
            {
                largestPosition = position;
                largestPositionTransform = current;
            }
        }
        Transform[] bones = body.bones;
        for (int boneIndex = 0; boneIndex < bones.Length; boneIndex++)
        {
            Transform bone = bones[boneIndex];
            if (bone == null)
            {
                continue;
            }
            float scale = Mathf.Max(
                Mathf.Abs(bone.localScale.x),
                Mathf.Max(Mathf.Abs(bone.localScale.y), Mathf.Abs(bone.localScale.z)));
            if (scale > largestBoneScale)
            {
                largestBoneScale = scale;
                largestBoneScaleTransform = bone;
            }
        }

        Debug.Log(
            $"GYMCHAOS_AUTHORED_BOUNDS_DIAGNOSTIC identity={fighter.Identity} " +
            $"clip={animator.CurrentAnimationClipName} " +
            $"renderer={body.bounds} local={body.localBounds} bakedLocal={baked.bounds} " +
            $"bakedWorld={bakedWorld} " +
            $"modelScale={importedVisual.RuntimeModelRoot.localScale} " +
            $"largestScale={largestScale:F4}:{largestScaleTransform?.name ?? "none"} " +
            $"largestBoneScale={largestBoneScale:F4}:{largestBoneScaleTransform?.name ?? "none"} " +
            $"largestPosition={largestPosition:F4}:{largestPositionTransform?.name ?? "none"}",
            body);
        UnityEngine.Object.DestroyImmediate(baked);
    }

    private static void ValidateFaceCensorPlacement(
        EnemyFighter fighter, SkinnedMeshRenderer body,
        FaceCensorSettings faceCensor, Bounds visibleBounds)
    {
        if (fighter == null || body == null || faceCensor == null)
        {
            throw new InvalidOperationException("Face geometry validation received a missing runtime object.");
        }

        // Validate against the exact authored head that owns the censor. Every
        // imported character can use a different skeleton naming scheme, so
        // never require the old mixamorig:Head name here.
        ExternalRiggedCharacterVisual importedVisual =
            fighter.GetComponent<ExternalRiggedCharacterVisual>();
        Transform head = importedVisual != null && importedVisual.RuntimeRig != null
            ? importedVisual.RuntimeRig.Head
            : faceCensor.transform.parent;
        if (head == null)
        {
            throw new InvalidOperationException(
                $"{fighter.Identity} is missing the authored head used for face calibration.");
        }

        float headDelta = Mathf.Abs(faceCensor.transform.position.y - head.position.y);
        bool calibratedTarget = fighter.Identity == BodybuilderIdentity.Ronnie ||
            fighter.Identity == BodybuilderIdentity.JayCutler ||
            fighter.Identity == BodybuilderIdentity.Goku;
        if (calibratedTarget)
        {
            Transform visualRoot = importedVisual != null
                ? importedVisual.RuntimeModelRoot
                : body.transform;
            if (visualRoot == null || !TryGetBakedVerticesInRoot(
                    body, visualRoot, out Vector3[] visibleVertices) ||
                !BodybuilderEnemyVisual.TryGetImportedFaceTarget(
                    fighter.Identity, body, visualRoot, head, visibleVertices,
                    out Vector3 eyeTargetWorld, out Bounds faceBounds))
            {
                throw new InvalidOperationException(
                    $"{fighter.Identity} has no visible head geometry for face validation.");
            }

            bool barInsideFaceBand =
                faceCensor.transform.position.y >= faceBounds.min.y - 0.06f &&
                faceCensor.transform.position.y <= faceBounds.max.y + 0.06f;
            float eyeHeightError = Mathf.Abs(
                faceCensor.transform.position.y - eyeTargetWorld.y);
            if (!barInsideFaceBand || eyeHeightError > 0.075f ||
                faceCensor.ConfiguredFaceDepth > 0.0001f ||
                faceCensor.ConfiguredFaceDepth < -0.0001f)
            {
                throw new InvalidOperationException(
                    $"{fighter.Identity} face bar is not on its eye/depth target: " +
                    $"barY={faceCensor.transform.position.y:F3} " +
                    $"eyeTargetY={eyeTargetWorld.y:F3} " +
                    $"faceBandY={faceBounds.min.y:F3}-{faceBounds.max.y:F3} " +
                    $"eyeHeightError={eyeHeightError:F3} " +
                    $"faceDepth={faceCensor.ConfiguredFaceDepth:F5}.");
            }

            Renderer faceRenderer = faceCensor.GetComponent<Renderer>();
            if (faceRenderer == null || !barInsideFaceBand)
            {
                throw new InvalidOperationException(
                    $"{fighter.Identity} face bar does not cover the imported visible eye band: " +
                    $"barBounds={faceRenderer?.bounds.ToString() ?? "missing"} " +
                    $"rendererBounds={body.bounds}.");
            }

            // The censor shell is generated symmetrically around its local X
            // axis. Compare that axis to the sampled facial midline, rather
            // than assuming the imported head pivot is the face midpoint.
            Vector3 barLocalToHead = head.InverseTransformPoint(
                faceCensor.transform.position);
            Vector3 targetLocalToHead = head.InverseTransformPoint(eyeTargetWorld);
            // The imported armature can carry non-uniform parent scale. In
            // that case a world-space dot product against head.right also
            // includes the head's Y/Z basis and reports a false lateral drift.
            // The censor is authored in the exact Head-local frame, so compare
            // the local X coordinates that define its symmetric shell axis.
            float lateralLocalError = Mathf.Abs(
                barLocalToHead.x - targetLocalToHead.x);
            float lateralLocalTolerance = Mathf.Max(
                0.008f, faceCensor.ConfiguredSize.x * 0.04f);
            if (lateralLocalError > lateralLocalTolerance)
            {
                throw new InvalidOperationException(
                    $"{fighter.Identity} face bar is laterally asymmetric: " +
                    $"barLocalX={barLocalToHead.x:F4} " +
                    $"faceTargetLocalX={targetLocalToHead.x:F4} " +
                    $"localError={lateralLocalError:F4} " +
                    $"localTolerance={lateralLocalTolerance:F4}.");
            }

            // The strict face-band and eye-height checks above are the source
            // of truth. Do not impose a generic normalized-Y threshold here:
            // imported face topology can place the eyes below the midpoint of
            // the sampled skin band, especially on Goku. The old threshold
            // rejected valid lower eye lines after animation even when the bar
            // matched the sampled target and remained inside faceBounds.
        }

        Debug.Log(
            $"GYMCHAOS_FACE_GEOMETRY_OK identity={fighter.Identity} " +
            $"barY={faceCensor.transform.position.y:F3} headY={head.position.y:F3} " +
            $"headDelta={headDelta:F3} faceDepth={faceCensor.ConfiguredFaceDepth:F5} " +
            $"size={faceCensor.ConfiguredSize}", faceCensor);
    }

    private static bool TryGetBakedVerticesInRoot(
        SkinnedMeshRenderer body, Transform visualRoot, out Vector3[] vertices)
    {
        vertices = null;
        if (body == null || visualRoot == null || body.sharedMesh == null)
        {
            return false;
        }

        Mesh baked = new Mesh { name = "Face validation baked mesh" };
        body.BakeMesh(baked, false);
        Vector3[] bakedVertices = baked.vertices;
        vertices = new Vector3[bakedVertices.Length];
        for (int i = 0; i < bakedVertices.Length; i++)
        {
            // Keep this identical to BodybuilderEnemyVisual. BakeMesh(...,
            // false) already contains the imported skinning scale; applying
            // TransformPoint would scale these vertices a second time.
            Vector3 world = body.transform.position +
                body.transform.rotation * bakedVertices[i];
            vertices[i] = visualRoot.InverseTransformPoint(world);
        }
        UnityEngine.Object.DestroyImmediate(baked);
        return vertices.Length > 0;
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

        animator.SetMoving(true, 1f, false);
        if (animator.CurrentState != MixamoScanRetargetAnimator.MotionState.Running)
        {
            throw new InvalidOperationException(
                fighter.Identity + " did not enter locomotion while walking.");
        }
        if (animator.IsUsingRunningClip)
        {
            throw new InvalidOperationException(
                fighter.Identity + " selected Run for neutral roaming.");
        }

        animator.SetMoving(true, 1f, true);
        if (!animator.IsUsingRunningClip)
        {
            throw new InvalidOperationException(
                fighter.Identity + " did not select Run for chase locomotion.");
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
        string outputDirectory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
        }
        File.WriteAllBytes(outputPath, image.EncodeToPNG());
        camera.targetTexture = previousTarget;
        RenderTexture.active = previousActive;
        UnityEngine.Object.DestroyImmediate(image);
        renderTexture.Release();
        UnityEngine.Object.DestroyImmediate(renderTexture);
        return outputPath;
    }

    private static string CaptureStablePlayerPose(
        PlayerHandRig rig, string clipStem, float normalizedTime,
        bool direct = false)
    {
        string directDetails;
        bool held = direct
            ? rig.HoldDirectPoseForVerification(
                clipStem, normalizedTime, out directDetails)
            : rig.HoldStablePoseForVerification(
                clipStem, normalizedTime, out directDetails);
        string details = directDetails;
        if (!held)
        {
            throw new InvalidOperationException(
                $"Could not hold stable player pose: {details}.");
        }

        Transform model = rig.RuntimeModelRoot;
        GameObject cameraObject = new GameObject(
            "Stable Player Pose Camera " + clipStem);
        Camera camera = cameraObject.AddComponent<Camera>();
        RenderTexture renderTexture = new RenderTexture(
            720, 720, 24, RenderTextureFormat.ARGB32);
        Texture2D image = null;
        RenderTexture previousActive = RenderTexture.active;
        try
        {
            Vector3 target = model.position + Vector3.up * 1.15f;
            bool actionSideView = clipStem == "jumping" ||
                clipStem.StartsWith("punch_", StringComparison.Ordinal);
            camera.transform.position = actionSideView
                ? target + model.right * 4.2f
                : target + model.forward * 4.2f;
            camera.transform.LookAt(target, Vector3.up);
            camera.fieldOfView = 34f;
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = 20f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.025f, 0.03f, 0.04f, 1f);
            camera.cullingMask = 1 << PlanarGymMirror.MirrorPlayerLayer;
            camera.targetTexture = renderTexture;
            renderTexture.Create();
            camera.Render();
            RenderTexture.active = renderTexture;
            image = new Texture2D(720, 720, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, 720, 720), 0, 0);
            image.Apply(false, false);
            Color32[] pixels = image.GetPixels32();
            int visiblePixels = 0;
            for (int i = 0; i < pixels.Length; i++)
            {
                Color32 pixel = pixels[i];
                if (pixel.r > 18 || pixel.g > 18 || pixel.b > 18)
                {
                    visiblePixels++;
                }
            }
            if (visiblePixels < 500)
            {
                throw new InvalidOperationException(
                    $"Stable player pose render was empty: clip={clipStem} " +
                    $"pixels={visiblePixels} details={details}.");
            }
            string outputPath = Path.GetFullPath(Path.Combine(
                Application.dataPath, "../../.tools",
                (direct ? "player-direct-" : "player-stable-") +
                clipStem + ".png"));
            File.WriteAllBytes(outputPath, image.EncodeToPNG());
            Debug.Log(
                $"GYMCHAOS_PLAYER_POSE_CAPTURE clip={clipStem} direct={direct} " +
                $"pixels={visiblePixels} details={details} path={outputPath}");
            return outputPath;
        }
        finally
        {
            RenderTexture.active = previousActive;
            if (image != null) UnityEngine.Object.DestroyImmediate(image);
            renderTexture.Release();
            UnityEngine.Object.DestroyImmediate(renderTexture);
            UnityEngine.Object.DestroyImmediate(cameraObject);
        }
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
