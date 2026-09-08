using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Batch Play Mode smoke test for the non-monetary progression, back room,
/// cosmetics and RPG dialogue paths. It deliberately restores PlayerPrefs
/// before leaving Play Mode so the verifier cannot consume a player's save.
/// </summary>
[InitializeOnLoad]
public static class GymChaosProgressionVerifier
{
    private const string VerificationRequestedKey =
        "GymChaos.ProgressionVerificationRequested";
    private const string OriginalSaveKey =
        "GymChaos.ProgressionVerificationOriginalSave";
    private const string OriginalSavePresentKey =
        "GymChaos.ProgressionVerificationOriginalSavePresent";
    private const string VisualVerificationRequestedKey =
        "GymChaos.ProgressionVisualVerificationRequested";
    private const string ProgressionSaveKey = "GymChaos.Progression.v1";

    private static double enteredPlayTime;
    private static int phase;
    private static bool completed;
    private static bool visualOnly;
    private static PlayerMovement player;
    private static GymExperienceService progression;
    private static GymBackRoomInteractable prepPoint;
    private static GymBackRoomInteractable lockerPoint;
    private static EnemyFighter dialogueTarget;
    private static CharacterController playerController;
    private static bool playerControllerWasEnabled;
    private static Vector3 playerPositionBeforeTest;
    private static Quaternion playerRotationBeforeTest;
    private static int experienceBeforeDiscovery;
    private static float reputationBeforeDialogue;
    private static Vector3 cameraLocalPositionBeforeDialogue;
    private static Quaternion cameraLocalRotationBeforeDialogue;
    private static float cameraFovBeforeDialogue;
    private static int cameraMaskBeforeDialogue;
    private static int lockerPreviewCaptureFrame;
    private static readonly string LockerPreviewCapturePath = Path.Combine(
        Directory.GetParent(Directory.GetParent(Application.dataPath).FullName).FullName,
        "codex-locker-outfit-preview.png");
    private static readonly string LockerReflectionCapturePath = Path.Combine(
        Directory.GetParent(Directory.GetParent(Application.dataPath).FullName).FullName,
        "codex-locker-reflection.png");
    private static readonly string LockerPlayerReflectionCapturePath = Path.Combine(
        Directory.GetParent(Directory.GetParent(Application.dataPath).FullName).FullName,
        "codex-locker-player-reflection.png");
    private static Camera lockerPreviewCaptureCamera;
    private static RenderTexture lockerPreviewCaptureTarget;
    private static RenderTexture lockerPreviewPreviousTarget;
    private static RenderTexture lockerPreviewPreviousActive;
    private static bool lockerPreviewPreviousCameraEnabled;
    private static int lockerPreviewCaptureAttempts;
    private static bool lockerReflectionCaptureVisible;
    private static bool lockerPlayerReflectionVisible;

    static GymChaosProgressionVerifier()
    {
        if (!EditorPrefs.GetBool(VerificationRequestedKey, false))
        {
            return;
        }

        visualOnly = EditorPrefs.GetBool(VisualVerificationRequestedKey, false);
        HookPlayModeEvents();
        EditorApplication.delayCall += ResumeAfterDomainReload;
    }

    [MenuItem("Tools/GymChaos/Run Progression Verification")]
    public static void Run()
    {
        visualOnly = false;
        RunInternal();
    }

    [MenuItem("Tools/GymChaos/Run Locker Visual Verification")]
    public static void RunLockerVisual()
    {
        visualOnly = true;
        RunInternal();
    }

    private static void RunInternal()
    {
        string originalSave = PlayerPrefs.GetString(ProgressionSaveKey, string.Empty);
        EditorPrefs.SetBool(OriginalSavePresentKey,
            PlayerPrefs.HasKey(ProgressionSaveKey));
        EditorPrefs.SetString(OriginalSaveKey, originalSave);
        PlayerPrefs.DeleteKey(ProgressionSaveKey);
        PlayerPrefs.Save();
        EditorPrefs.SetBool(VerificationRequestedKey, true);
        EditorPrefs.SetBool(VisualVerificationRequestedKey, visualOnly);
        phase = 0;
        completed = false;
        lockerPreviewCaptureFrame = 0;
        lockerReflectionCaptureVisible = false;
        lockerPlayerReflectionVisible = false;
        if (File.Exists(LockerPreviewCapturePath))
        {
            File.Delete(LockerPreviewCapturePath);
        }
        if (File.Exists(LockerReflectionCapturePath))
        {
            File.Delete(LockerReflectionCapturePath);
        }
        if (File.Exists(LockerPlayerReflectionCapturePath))
        {
            File.Delete(LockerPlayerReflectionCapturePath);
        }
        HookPlayModeEvents();
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        EditorApplication.isPlaying = true;
    }

    private static void HookPlayModeEvents()
    {
        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
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
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
        }
        else if (state == PlayModeStateChange.EnteredEditMode)
        {
            EditorPrefs.DeleteKey(VerificationRequestedKey);
            EditorPrefs.DeleteKey(VisualVerificationRequestedKey);
            EditorPrefs.DeleteKey(OriginalSaveKey);
            EditorPrefs.DeleteKey(OriginalSavePresentKey);
            EditorApplication.update -= Tick;
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            if (Application.isBatchMode)
            {
                EditorApplication.Exit(completed ? 0 : 1);
            }
        }
    }

    private static void Tick()
    {
        try
        {
            double elapsed = EditorApplication.timeSinceStartup - enteredPlayTime;
            player = UnityEngine.Object.FindAnyObjectByType<PlayerMovement>();
            progression = GymExperienceService.Active;
            if (player == null || progression == null)
            {
                if (elapsed > 30d)
                {
                    Fail("Progression verifier scene did not initialize the player and service.");
                }
                return;
            }

            if (phase == 0)
            {
                if (elapsed < 3d)
                {
                    return;
                }

                if (progression.Reputation != 0)
                {
                    Fail("The player did not launch with zero reputation.");
                    return;
                }

                if (visualOnly)
                {
                    if (elapsed < 10d)
                    {
                        return;
                    }

                    lockerPoint = FindInteractable(GymBackRoomInteractionType.Locker);
                    if (lockerPoint == null ||
                        !GymBackRoomBuilder.IsInsideRoom(lockerPoint.transform.position))
                    {
                        Fail("Locker visual verifier could not find the back-room locker point.");
                        return;
                    }

                    playerController = player.GetComponent<CharacterController>();
                    playerControllerWasEnabled = playerController != null && playerController.enabled;
                    playerPositionBeforeTest = player.transform.position;
                    playerRotationBeforeTest = player.transform.rotation;
                    if (playerController != null)
                    {
                        playerController.enabled = false;
                    }

                    progression.State.lockerPrepCompleted = true;
                    MovePlayer(lockerPoint.transform.position);
                    phase = 2;
                    return;
                }

                if (!ValidateLooseItems(out string looseItemFailure))
                {
                    if (elapsed > 60d)
                    {
                        Fail("Loose item verification failed: " + looseItemFailure);
                    }
                    return;
                }

                prepPoint = FindInteractable(GymBackRoomInteractionType.Prep);
                lockerPoint = FindInteractable(GymBackRoomInteractionType.Locker);
                if (prepPoint == null || lockerPoint == null ||
                    !GymBackRoomBuilder.IsInsideRoom(prepPoint.transform.position))
                {
                    Fail("Back room interactables or room bounds were not built.");
                    return;
                }

                playerController = player.GetComponent<CharacterController>();
                playerControllerWasEnabled = playerController != null && playerController.enabled;
                playerPositionBeforeTest = player.transform.position;
                playerRotationBeforeTest = player.transform.rotation;
                if (playerController != null)
                {
                    playerController.enabled = false;
                }

                experienceBeforeDiscovery = progression.State.totalExperience;
                if (progression.CanStartWorkout())
                {
                    Fail("A first workout was allowed before locker-room preparation.");
                    return;
                }

                MovePlayer(prepPoint.transform.position);
                phase = 1;
                return;
            }

            if (phase == 1)
            {
                if (progression.State.totalExperience < experienceBeforeDiscovery + 10)
                {
                    if (elapsed > 10d)
                    {
                        Fail("Entering the back room did not award its one-shot discovery XP.");
                    }
                    return;
                }

                if (progression.State.lockerPrepCompleted)
                {
                    Fail("Back-room discovery completed locker preparation without interaction.");
                    return;
                }

                if (!GymExperienceService.TryHandlePlayerInteraction(player, true) ||
                    !progression.State.lockerPrepCompleted ||
                    !progression.CanStartWorkout())
                {
                    Fail("Locker-room prep did not unlock the first workout gate.");
                    return;
                }

                MovePlayer(lockerPoint.transform.position);
                phase = 2;
                return;
            }

            if (phase == 2)
            {
                if (!progression.IsLockerMenuOpen)
                {
                    GymExperienceService.TryHandlePlayerInteraction(player, true);
                }

                if (!progression.IsLockerMenuOpen)
                {
                    if (progression.IsSkillChoiceVisible && progression.SkillPoints > 0)
                    {
                        progression.AllocateSkill(GymStat.Strength);
                        return;
                    }

                    if (elapsed > 12d)
                    {
                        Fail("Locker interaction did not open the cosmetic menu.");
                    }
                    return;
                }

                if (!player.IsCinematicLocked || player.CursorCaptured)
                {
                    Fail("Locker menu did not take the cinematic input lock.");
                    return;
                }

                if (!GymBackRoomBuilder.TryGetLockerPreviewPose(
                        out Vector3 expectedPreviewPosition, out Quaternion expectedPreviewRotation) ||
                    Vector3.Distance(player.transform.position, expectedPreviewPosition) > 0.05f ||
                    Quaternion.Angle(player.transform.rotation, expectedPreviewRotation) > 1f)
                {
                    Fail("Locker menu did not move the player into the mirror preview pose.");
                    return;
                }

                PlanarGymMirror[] mirrors = UnityEngine.Object.FindObjectsByType<PlanarGymMirror>(
                    FindObjectsSortMode.None);
                if (mirrors.Length < 2)
                {
                    Fail("Expected both gym and locker-room planar mirrors.");
                    return;
                }
                for (int i = 0; i < mirrors.Length; i++)
                {
                    if (mirrors[i] == null || !mirrors[i].ReflectionIncludesPlayerLayer)
                    {
                        Fail("A planar mirror reflection camera omitted the player layer.");
                        return;
                    }
                }

                progression.State.level = Mathf.Max(progression.State.level, 8);
                progression.State.masteryRank = Mathf.Max(progression.State.masteryRank, 2);
                if (!progression.IsCosmeticUnlocked(GymShirtColor.Gold) ||
                    !progression.IsCosmeticUnlocked(GymHeadwear.Visor))
                {
                    Fail("Mastery cosmetics did not unlock at their mastery thresholds.");
                    return;
                }

                progression.EquipShirt(GymShirtColor.Red);
                PlayerCosmeticLoadout loadout = player.GetComponent<PlayerCosmeticLoadout>();
                if (loadout == null || !loadout.IsShirtVisualReady ||
                    !IsColorClose(loadout.RenderedShirtColor,
                        new Color(0.72f, 0.045f, 0.025f)))
                {
                    Fail("Red shirt selection did not update the visible shirt material.");
                    return;
                }

                progression.EquipShirt(GymShirtColor.Blue);
                if (loadout == null || !loadout.IsShirtVisualReady ||
                    !IsColorClose(loadout.RenderedShirtColor,
                        new Color(0.035f, 0.22f, 0.78f)))
                {
                    Fail("Blue shirt selection did not update the visible shirt material.");
                    return;
                }

                progression.EquipShirt(GymShirtColor.Blue);
                progression.EquipHeadwear(GymHeadwear.Visor);

                // The GLB is loaded asynchronously. Do not capture the mirror
                // until the selected wearable has a live renderer; otherwise
                // a valid outfit can be falsely reported as invisible simply
                // because the capture happened in the request frame.
                if (loadout == null || !loadout.IsHeadwearVisualReady)
                {
                    if (elapsed > 45d)
                    {
                        Fail("Headwear asset did not become visible in the locker mirror.");
                    }
                    return;
                }
                Debug.Log(
                    $"GYMCHAOS_HEADWEAR_VERIFIED type={loadout.CurrentHeadwear} " +
                    $"asset={loadout.CurrentHeadwearAssetPath}", loadout);

                if (lockerPreviewCaptureFrame == 0)
                {
                    BeginLockerPreviewCapture();
                    lockerPreviewCaptureFrame = 1;
                    Debug.Log(
                        "GYMCHAOS_LOCKER_OUTFIT_PREVIEW_CAPTURE_REQUESTED " +
                        "mirror=locker playerPose=1 outfit=blue+visor path=" +
                        LockerPreviewCapturePath);
                    return;
                }

                if (lockerPreviewCaptureFrame == 1)
                {
                    if (FinishLockerPreviewCapture())
                    {
                        lockerPreviewCaptureFrame = 2;
                    }
                    else if (lockerPreviewCaptureTarget == null)
                    {
                        lockerPreviewCaptureFrame = 2;
                    }
                    return;
                }

                if (!File.Exists(LockerPreviewCapturePath) ||
                    !File.Exists(LockerReflectionCapturePath) ||
                    !lockerReflectionCaptureVisible ||
                    !File.Exists(LockerPlayerReflectionCapturePath) ||
                    !lockerPlayerReflectionVisible)
                {
                    lockerPreviewCaptureFrame++;
                    if (lockerPreviewCaptureFrame < 6)
                    {
                        return;
                    }

                    Fail("Locker mirror reflection capture was not visible: " +
                        LockerReflectionCapturePath);
                    return;
                }

                CloseLockerMenuForVerification();
                if (progression.IsLockerMenuOpen || player.IsCinematicLocked)
                {
                    Fail("Locker menu did not restore the player input state.");
                    return;
                }

                if (visualOnly)
                {
                    Complete(true, "locker visual preview, mirror player layer and outfit capture");
                    return;
                }

                phase = 3;
                return;
            }

            if (phase == 3)
            {
                PlayerCosmeticLoadout loadout =
                    player.GetComponent<PlayerCosmeticLoadout>();
                Transform shirt = FindDescendant(player.transform, "Player Cosmetic Shirt");
                Transform headwear = FindDescendant(player.transform, "Player Cosmetic Headwear");
                if (loadout == null || shirt != null || headwear != null)
                {
                    Fail("Procedural shirt/headwear primitives obscured the authored player mesh.");
                    return;
                }

                Transform avatarRig = FindDescendant(player.transform, "PlayerAvatarRig");
                Renderer[] avatarRenderers = avatarRig != null
                    ? avatarRig.GetComponentsInChildren<Renderer>(true)
                    : Array.Empty<Renderer>();
                bool hasVisibleMirrorBody = false;
                for (int i = 0; i < avatarRenderers.Length; i++)
                {
                    Renderer renderer = avatarRenderers[i];
                    if (renderer != null && renderer.enabled &&
                        renderer.gameObject.layer == PlanarGymMirror.MirrorPlayerLayer)
                    {
                        hasVisibleMirrorBody = true;
                        break;
                    }
                }
                if (!hasVisibleMirrorBody)
                {
                    Fail("Player avatar renderers were not available on the mirror player layer.");
                    return;
                }

                dialogueTarget = FindDialogueTarget();
                if (dialogueTarget == null)
                {
                    Fail("No neutral gym NPC was available for dialogue verification.");
                    return;
                }

                MovePlayer(dialogueTarget.transform.position -
                    dialogueTarget.transform.forward * 2f);
                Camera camera = player.playerCamera;
                cameraLocalPositionBeforeDialogue = camera.transform.localPosition;
                cameraLocalRotationBeforeDialogue = camera.transform.localRotation;
                cameraFovBeforeDialogue = camera.fieldOfView;
                cameraMaskBeforeDialogue = camera.cullingMask;
                reputationBeforeDialogue = progression.Reputation;
                phase = 4;
                return;
            }

            if (phase == 4)
            {
                if (!GymDialogueDirector.IsDialogueActive)
                {
                    GymDialogueDirector.TryStartNearby(player, true);
                    return;
                }

                DialogueNode node = GymDialogueDirector.Active.CurrentNode;
                if (node == null || node.choices.Count > 3 ||
                    !player.IsCinematicLocked || player.CursorCaptured ||
                    (player.playerCamera.cullingMask &
                        (1 << PlanarGymMirror.MirrorPlayerLayer)) != 0 ||
                    (player.playerCamera.cullingMask &
                        (1 << PlanarGymMirror.FirstPersonPlayerLayer)) != 0)
                {
                    Fail("Dialogue did not open with the expected RPG camera/input state.");
                    return;
                }

                if (GymDialogueDirector.Active.LetterboxBlend <= 0.01f)
                {
                    return;
                }

                GymDialogueDirector.TickActiveInput(player, false, false);
                if (!GymDialogueDirector.IsDialogueActive ||
                    progression.Reputation != reputationBeforeDialogue)
                {
                    Fail("Dialogue input changed state or reputation before a choice was selected.");
                    return;
                }

                GymDialogueDirector.TickActiveInput(player, false, true);
                phase = 5;
                return;
            }

            if (phase == 5)
            {
                if (GymDialogueDirector.IsDialogueActive ||
                    progression.Reputation != reputationBeforeDialogue ||
                    player.IsCinematicLocked ||
                    Vector3.Distance(player.playerCamera.transform.localPosition,
                        cameraLocalPositionBeforeDialogue) > 0.01f ||
                    Quaternion.Angle(player.playerCamera.transform.localRotation,
                        cameraLocalRotationBeforeDialogue) > 0.5f ||
                    Mathf.Abs(player.playerCamera.fieldOfView - cameraFovBeforeDialogue) > 0.01f ||
                    player.playerCamera.cullingMask != cameraMaskBeforeDialogue)
                {
                    Fail("Dialogue close did not preserve reputation and restore camera state.");
                    return;
                }

                ValidateProgressionUltimatesAndTiming();
                Complete(true, "progression, locker, cosmetics, technique, ultimates and dialogue");
            }
        }
        catch (Exception exception)
        {
            Fail(exception.ToString());
        }
    }

    private static void BeginLockerPreviewCapture()
    {
        Camera camera = player != null ? player.playerCamera : null;
        if (camera == null)
        {
            Debug.LogError("GYMCHAOS_LOCKER_OUTFIT_PREVIEW_CAPTURE_FAILED camera=null");
            return;
        }

        lockerPreviewCaptureCamera = camera;
        lockerPreviewPreviousTarget = camera.targetTexture;
        lockerPreviewPreviousActive = RenderTexture.active;
        lockerPreviewPreviousCameraEnabled = camera.enabled;
        lockerPreviewCaptureTarget = new RenderTexture(960, 540, 24, RenderTextureFormat.ARGB32)
        {
            name = "GymChaos Locker Preview Capture"
        };

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LockerPreviewCapturePath));
            camera.enabled = false;
            camera.targetTexture = lockerPreviewCaptureTarget;
            lockerPreviewCaptureTarget.Create();
            SubmitLockerPreviewRenderRequest();
            lockerPreviewCaptureAttempts = 0;
        }
        catch (Exception exception)
        {
            Debug.LogError("GYMCHAOS_LOCKER_OUTFIT_PREVIEW_CAPTURE_FAILED " +
                exception.GetType().Name + ": " + exception.Message);
            CleanupLockerPreviewCapture();
        }
    }

    private static bool FinishLockerPreviewCapture()
    {
        if (lockerPreviewCaptureTarget == null || lockerPreviewCaptureCamera == null)
        {
            return false;
        }

        lockerPreviewCaptureAttempts++;
        Texture2D image = null;
        try
        {
            RenderTexture.active = lockerPreviewCaptureTarget;
            image = new Texture2D(
                lockerPreviewCaptureTarget.width,
                lockerPreviewCaptureTarget.height,
                TextureFormat.RGB24,
                false);
            image.ReadPixels(
                new Rect(0, 0, lockerPreviewCaptureTarget.width,
                    lockerPreviewCaptureTarget.height), 0, 0);
            image.Apply(false, false);

            Color32[] pixels = image.GetPixels32();
            bool hasRenderedPixels = false;
            for (int i = 0; i < pixels.Length; i++)
            {
                Color32 pixel = pixels[i];
                if (pixel.r > 10 || pixel.g > 10 || pixel.b > 10)
                {
                    hasRenderedPixels = true;
                    break;
                }
            }

            if (!hasRenderedPixels && lockerPreviewCaptureAttempts < 5)
            {
                UnityEngine.Object.DestroyImmediate(image);
                SubmitLockerPreviewRenderRequest();
                return false;
            }

            if (!hasRenderedPixels)
            {
                throw new InvalidOperationException(
                    "Locker preview render produced only clear-color pixels.");
            }

            File.WriteAllBytes(LockerPreviewCapturePath, image.EncodeToPNG());
            Debug.Log("GYMCHAOS_LOCKER_OUTFIT_PREVIEW_CAPTURE_WRITTEN path=" +
                LockerPreviewCapturePath);
            CaptureLockerReflectionTexture();

            return true;
        }
        catch (Exception exception)
        {
            Debug.LogError("GYMCHAOS_LOCKER_OUTFIT_PREVIEW_CAPTURE_FAILED " +
                exception.GetType().Name + ": " + exception.Message);
            return false;
        }
        finally
        {
            if (image != null)
            {
                UnityEngine.Object.DestroyImmediate(image);
            }
            if (lockerPreviewCaptureAttempts >= 5 ||
                File.Exists(LockerPreviewCapturePath))
            {
                CleanupLockerPreviewCapture();
            }
        }
    }

    private static void SubmitLockerPreviewRenderRequest()
    {
        if (lockerPreviewCaptureCamera == null || lockerPreviewCaptureTarget == null)
        {
            return;
        }

        RenderPipeline.StandardRequest request = new RenderPipeline.StandardRequest
        {
            destination = lockerPreviewCaptureTarget
        };
        lockerPreviewCaptureCamera.SubmitRenderRequest(request);
    }

    private static void CaptureLockerReflectionTexture()
    {
        PlanarGymMirror[] mirrors = UnityEngine.Object.FindObjectsByType<PlanarGymMirror>(
            FindObjectsSortMode.None);
        PlanarGymMirror lockerMirror = null;
        for (int i = 0; i < mirrors.Length; i++)
        {
            if (mirrors[i] != null &&
                Vector3.Dot(mirrors[i].PlaneNormal, Vector3.forward) > 0.9f)
            {
                lockerMirror = mirrors[i];
                break;
            }
        }

        RenderTexture texture = lockerMirror != null
            ? lockerMirror.ReflectionTexture
            : null;
        if (texture == null)
        {
            Debug.LogError("GYMCHAOS_LOCKER_REFLECTION_CAPTURE_FAILED texture=null");
            return;
        }

        Texture2D image = null;
        RenderTexture previousActive = RenderTexture.active;
        try
        {
            RenderTexture.active = texture;
            image = new Texture2D(texture.width, texture.height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0);
            image.Apply(false, false);
            Color32[] pixels = image.GetPixels32();
            int minimumLuminance = 255;
            int maximumLuminance = 0;
            HashSet<int> colorBuckets = new HashSet<int>();
            for (int i = 0; i < pixels.Length; i++)
            {
                Color32 pixel = pixels[i];
                int luminance = (pixel.r * 3 + pixel.g * 6 + pixel.b) / 10;
                minimumLuminance = Mathf.Min(minimumLuminance, luminance);
                maximumLuminance = Mathf.Max(maximumLuminance, luminance);
                colorBuckets.Add(
                    ((pixel.r >> 5) << 6) |
                    ((pixel.g >> 5) << 3) |
                    (pixel.b >> 5));
            }

            bool hasRenderedPixels = maximumLuminance > 10 &&
                maximumLuminance - minimumLuminance > 30 &&
                colorBuckets.Count >= 12;

            File.WriteAllBytes(LockerReflectionCapturePath, image.EncodeToPNG());
            lockerReflectionCaptureVisible = hasRenderedPixels;
            lockerPlayerReflectionVisible =
                CaptureLockerPlayerOnlyReflection(lockerMirror);
            Debug.Log(
                "GYMCHAOS_LOCKER_REFLECTION_CAPTURE_WRITTEN path=" +
                LockerReflectionCapturePath + " visible=" + hasRenderedPixels +
                " luminanceRange=" + (maximumLuminance - minimumLuminance) +
                " colorBuckets=" + colorBuckets.Count +
                " playerVisible=" + lockerPlayerReflectionVisible);
        }
        catch (Exception exception)
        {
            Debug.LogError("GYMCHAOS_LOCKER_REFLECTION_CAPTURE_FAILED " +
                exception.GetType().Name + ": " + exception.Message);
        }
        finally
        {
            RenderTexture.active = previousActive;
            if (image != null)
            {
                UnityEngine.Object.DestroyImmediate(image);
            }
        }
    }

    private static bool CaptureLockerPlayerOnlyReflection(
        PlanarGymMirror lockerMirror)
    {
        Camera camera = lockerMirror != null ? lockerMirror.ReflectionCamera : null;
        RenderTexture texture = lockerMirror != null
            ? lockerMirror.ReflectionTexture : null;
        if (camera == null || texture == null)
        {
            return false;
        }

        int previousMask = camera.cullingMask;
        CameraClearFlags previousClearFlags = camera.clearFlags;
        Color previousBackground = camera.backgroundColor;
        Texture2D image = null;
        RenderTexture previousActive = RenderTexture.active;
        try
        {
            int texturedPlayerRenderers = 0;
            Renderer[] renderers = UnityEngine.Object.FindObjectsByType<Renderer>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null ||
                    renderer.gameObject.layer != PlanarGymMirror.MirrorPlayerLayer)
                {
                    continue;
                }
                Material material = renderer.sharedMaterial;
                Texture baseTexture = material != null && material.HasProperty("_BaseMap")
                    ? material.GetTexture("_BaseMap") : null;
                if (material != null && baseTexture != null &&
                    material.renderQueue <= (int)RenderQueue.GeometryLast)
                {
                    texturedPlayerRenderers++;
                }
            }
            if (texturedPlayerRenderers == 0)
            {
                return false;
            }

            camera.cullingMask = 1 << PlanarGymMirror.MirrorPlayerLayer;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            lockerMirror.RequestImmediateRefresh();
            camera.Render();
            RenderTexture.active = texture;
            image = new Texture2D(
                texture.width, texture.height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0);
            image.Apply(false, false);
            Color32[] pixels = image.GetPixels32();
            int visiblePixels = 0;
            for (int i = 0; i < pixels.Length; i++)
            {
                if (pixels[i].r > 12 || pixels[i].g > 12 || pixels[i].b > 12)
                {
                    visiblePixels++;
                }
            }
            File.WriteAllBytes(
                LockerPlayerReflectionCapturePath, image.EncodeToPNG());
            bool visible = visiblePixels >= 180;
            Debug.Log(
                $"GYMCHAOS_LOCKER_PLAYER_REFLECTION_OK visible={visible} " +
                $"pixels={visiblePixels} texturedRenderers={texturedPlayerRenderers} " +
                $"path={LockerPlayerReflectionCapturePath}");
            return visible;
        }
        finally
        {
            camera.cullingMask = previousMask;
            camera.clearFlags = previousClearFlags;
            camera.backgroundColor = previousBackground;
            RenderTexture.active = previousActive;
            if (image != null) UnityEngine.Object.DestroyImmediate(image);
            lockerMirror.RequestImmediateRefresh();
        }
    }

    private static void CleanupLockerPreviewCapture()
    {
        if (lockerPreviewCaptureCamera != null)
        {
            lockerPreviewCaptureCamera.targetTexture = lockerPreviewPreviousTarget;
            lockerPreviewCaptureCamera.enabled = lockerPreviewPreviousCameraEnabled;
        }
        RenderTexture.active = lockerPreviewPreviousActive;
        if (lockerPreviewCaptureTarget != null)
        {
            lockerPreviewCaptureTarget.Release();
            UnityEngine.Object.DestroyImmediate(lockerPreviewCaptureTarget);
        }
        lockerPreviewCaptureCamera = null;
        lockerPreviewCaptureTarget = null;
        lockerPreviewPreviousTarget = null;
        lockerPreviewPreviousActive = null;
        lockerPreviewCaptureAttempts = 0;
    }

    private static void ValidateProgressionUltimatesAndTiming()
    {
        GymProgressionState state = progression.State;
        state.strengthRank = GymExperienceService.MaxStatRank;
        state.enduranceRank = GymExperienceService.MaxStatRank;
        state.techniqueRank = GymExperienceService.MaxStatRank;
        state.reputationRank = GymExperienceService.MaxStatRank;
        state.reputation = 100;

        EnemyFighter normalEnemy = FindNormalEnemy();
        if (normalEnemy == null || !progression.StrengthUltimateApplies(normalEnemy) ||
            !progression.HasEnduranceUltimate || !progression.HasTechniqueUltimate ||
            !progression.HasPositiveReputationUltimate ||
            progression.GetSprintMultiplier() <= 1.7f)
        {
            throw new InvalidOperationException(
                "Strength, Endurance, Technique or positive Reputation ultimate did not activate.");
        }

        state.reputation = -100;
        if (!progression.HasNegativeReputationUltimate)
        {
            throw new InvalidOperationException("Negative Reputation ultimate did not activate.");
        }

        TechniqueSkillCheck lowRank = new TechniqueSkillCheck();
        TechniqueSkillCheck maxRank = new TechniqueSkillCheck();
        lowRank.Begin(0, 1f);
        maxRank.Begin(GymExperienceService.MaxStatRank, 1f);
        if (maxRank.PerfectHalfAngle <= lowRank.PerfectHalfAngle ||
            maxRank.AcceptableHalfAngle <= lowRank.AcceptableHalfAngle)
        {
            throw new InvalidOperationException("Technique rank did not widen the timing windows.");
        }

        GlassShatterPanel mirror = GameObject.Find("Locker room mirror panel") != null
            ? GameObject.Find("Locker room mirror panel").GetComponent<GlassShatterPanel>()
            : null;
        if (mirror == null || !mirror.ShatterFromPowerImpact(
            mirror.transform.position, -mirror.transform.forward, Vector3.forward * 18f) ||
            !mirror.IsShattered)
        {
            throw new InvalidOperationException("Strength ultimate did not break the unarmed mirror.");
        }

        int levelBefore = state.level;
        state.experience = GymExperienceService.GetExperienceToNextLevel(levelBefore) - 1;
        state.skillPoints = 0;
        state.strengthRank = 0;
        progression.AwardExperience(1, "progression verifier level", "once:progression-verifier-level");
        if (progression.Level != levelBefore + 1 || progression.SkillPoints != 1 ||
            !progression.IsSkillChoiceVisible)
        {
            throw new InvalidOperationException("Level-up did not grant a deferred skill point choice.");
        }

        progression.AllocateSkill(GymStat.Strength);
        if (progression.SkillPoints != 0 || state.strengthRank != 1)
        {
            throw new InvalidOperationException("Level-up skill point allocation did not update Strength.");
        }

        Debug.Log(
            "GYMCHAOS_PROGRESSION_FEATURES_OK " +
            "discovery=1 prepGate=1 locker=1 masteryCosmetics=1 " +
            "techniqueWindows=1 ultimates=4 mirrorBreak=1 levelChoice=1 dialogue=1");
    }

    private static bool ValidateLooseItems(out string failure)
    {
        failure = string.Empty;
        string[] requiredItems =
        {
            "Loose Item - Rolled-out yoga mat",
            "Loose Item - Half-rolled yoga mat",
            "Loose Item - Foam roller",
            "Loose Item - Step platform",
            "Loose Item - Red medicine ball",
            "Loose Item - Blue medicine ball",
            "Loose Item - Paper towel roll",
            "Loose Item - Paper towel roll (right)"
        };

        for (int i = 0; i < requiredItems.Length; i++)
        {
            GameObject item = GameObject.Find(requiredItems[i]);
            if (item == null)
            {
                failure = "Waiting for " + requiredItems[i] + ".";
                return false;
            }

            if (item.GetComponent<Renderer>() == null ||
                item.GetComponents<Collider>().Length == 0)
            {
                failure = requiredItems[i] + " has no renderer or collision.";
                return false;
            }
        }

        if (!ValidatePickableItem("Loose Item - Foam roller", out failure) ||
            !ValidatePickableItem("Loose Item - Step platform", out failure) ||
            !ValidatePickableItem("Loose Item - Red medicine ball", out failure) ||
            !ValidatePickableItem("Loose Item - Blue medicine ball", out failure) ||
            !ValidatePickableItem("Loose Item - Paper towel roll", out failure) ||
            !ValidatePickableItem("Loose Item - Paper towel roll (right)", out failure))
        {
            return false;
        }

        if (!ValidateUniformScale(
                "Loose Item - Foam roller", 0.81f * 0.75f, out failure) ||
            !ValidateUniformScale(
                "Loose Item - Red medicine ball", 0.95f * 1.25f, out failure) ||
            !ValidateUniformScale(
                "Loose Item - Blue medicine ball", 0.95f * 1.25f, out failure))
        {
            return false;
        }

        if (!TryFindLargestAuthoredSurface(
                new[] { "matt", "yogamat", "lungesmatt" }, out Bounds matBounds))
        {
            failure = "The authored yoga mat surface could not be found.";
            return false;
        }

        Renderer rolledOutRenderer =
            GameObject.Find("Loose Item - Rolled-out yoga mat").GetComponent<Renderer>();
        Renderer halfRolledRenderer =
            GameObject.Find("Loose Item - Half-rolled yoga mat").GetComponent<Renderer>();
        Renderer foamRenderer =
            GameObject.Find("Loose Item - Foam roller").GetComponent<Renderer>();
        GameObject deadliftStation = GameObject.Find("Freeweights Deadlift Station");
        bool matsAreBesideDeadlift = false;
        if (deadliftStation != null &&
            TryGetRendererBounds(deadliftStation.transform, out Bounds deadliftBounds))
        {
            Vector3 rolledOutLocal = deadliftStation.transform.InverseTransformPoint(
                rolledOutRenderer.bounds.center);
            Vector3 halfRolledLocal = deadliftStation.transform.InverseTransformPoint(
                halfRolledRenderer.bounds.center);
            matsAreBesideDeadlift = rolledOutLocal.x < -1.1f &&
                halfRolledLocal.x > 1.1f &&
                Mathf.Abs(rolledOutLocal.z - halfRolledLocal.z) < 1.35f &&
                IsNearBounds(rolledOutRenderer.bounds.center, deadliftBounds, 0.9f) &&
                IsNearBounds(halfRolledRenderer.bounds.center, deadliftBounds, 0.9f);
        }

        bool matsAreBesideAuthoredSurface =
            IsBesideSurface(rolledOutRenderer.bounds, matBounds) &&
            IsBesideSurface(halfRolledRenderer.bounds, matBounds);
        if ((!matsAreBesideDeadlift && !matsAreBesideAuthoredSurface) ||
            !IsOnSurface(foamRenderer.bounds, matBounds))
        {
            failure = "Loose yoga mats are neither beside the authored mat nor correctly split beside the deadlift platform, or the foam roller left its surface.";
            return false;
        }

        if (!HasHorizontalClearance(rolledOutRenderer.bounds, halfRolledRenderer.bounds))
        {
            failure = "The two loose yoga mats overlap beside the authored mat.";
            return false;
        }

        if (!ValidateDeadliftSetup(out failure))
        {
            return false;
        }

        GameObject shelf = GameObject.Find("Paper towel shelf");
        Renderer shelfRenderer = shelf != null ? shelf.GetComponent<Renderer>() : null;
        if (shelfRenderer == null)
        {
            failure = "Paper towel shelf was not built.";
            return false;
        }

        Renderer paperLeft =
            GameObject.Find("Loose Item - Paper towel roll").GetComponent<Renderer>();
        Renderer paperRight =
            GameObject.Find("Loose Item - Paper towel roll (right)").GetComponent<Renderer>();
        if (!IsOnShelf(paperLeft.bounds, shelfRenderer.bounds) ||
            !IsOnShelf(paperRight.bounds, shelfRenderer.bounds))
        {
            failure = "Paper towel rolls are not resting on the shelf.";
            return false;
        }

        if (TryFindFloorBounds(out Bounds floorBounds) &&
            !TryFindNearestNamedBounds(
                new[] { "cage", "smithmachine", "squatrack", "powerrack" },
                floorBounds.center, out Bounds rackBounds))
        {
            failure = "The squat-rack anchor could not be found.";
            return false;
        }

        GameObject step = GameObject.Find("Loose Item - Step platform");
        if (step != null && TryFindFloorBounds(out floorBounds) &&
            TryFindNearestNamedBounds(
                new[] { "cage", "smithmachine", "squatrack", "powerrack" },
                floorBounds.center, out rackBounds))
        {
            Renderer stepRenderer = step.GetComponent<Renderer>();
            if (stepRenderer == null ||
                !IsNearBounds(stepRenderer.bounds.center, rackBounds, 1.1f))
            {
                failure = "Step platform is not placed beside the squat-rack area.";
                return false;
            }
        }

        Debug.Log(
            "GYMCHAOS_LOOSE_ITEMS_VERIFICATION_OK " +
            "items=8 collisions=8 pickup=6 scales=foam0.6075 balls1.1875 " +
            "matPlacement=validated deadlift=1 shelfPlacement=1 rackPlacement=1");
        return true;
    }

    private static bool ValidatePickableItem(string name, out string failure)
    {
        GameObject item = GameObject.Find(name);
        if (item == null || item.GetComponent<Rigidbody>() == null ||
            item.GetComponent<PickupItem>() == null)
        {
            failure = name + " is missing Rigidbody or PickupItem.";
            return false;
        }

        failure = string.Empty;
        return true;
    }

    private static bool ValidateDeadliftSetup(out string failure)
    {
        failure = string.Empty;
        GameObject stationObject = GameObject.Find("Freeweights Deadlift Station");
        GameObject barObject = GameObject.Find("Barbell DeadliftStation Loaded");
        GameObject exerciseObject = GameObject.Find("Exercise Station - Deadlift");
        if (stationObject == null || barObject == null || exerciseObject == null ||
            stationObject.GetComponent<GymDeadliftStationMarker>() == null ||
            stationObject.GetComponentsInChildren<Renderer>(true).Length < 8)
        {
            failure = "Deadlift platform, marker, flooring mats, or loaded bar is missing.";
            return false;
        }

        GymDeadliftStationMarker stationMarker =
            stationObject.GetComponent<GymDeadliftStationMarker>();
        Collider[] loadedBarColliders = barObject.GetComponentsInChildren<Collider>(true);
        for (int colliderIndex = 0; colliderIndex < loadedBarColliders.Length; colliderIndex++)
        {
            if (loadedBarColliders[colliderIndex] != null &&
                !stationMarker.ContainsCollider(loadedBarColliders[colliderIndex]))
            {
                failure = "A loaded deadlift bar/plate collider is not registered with the station marker.";
                return false;
            }
        }
        EnemyFighter[] enemies = UnityEngine.Object.FindObjectsByType<EnemyFighter>(
            FindObjectsSortMode.None);
        for (int i = 0; i < enemies.Length; i++)
        {
            if (enemies[i] == null)
            {
                continue;
            }

            if (enemies[i].Identity == BodybuilderIdentity.Ronnie &&
                !stationMarker.AreEnemyCollisionsIgnored(enemies[i]))
            {
                failure = "Ronnie still has an active collision with the deadlift station.";
                return false;
            }
            if (GymLooseItemSpawner.TryGetDeadliftEscapePointForEnemy(
                    enemies[i], 0.55f, out _))
            {
                failure = $"{enemies[i].Identity} is still inside the deadlift platform footprint.";
                return false;
            }
            if (enemies[i].HasDeadliftRoamTarget)
            {
                failure = $"{enemies[i].Identity} still has a player-only deadlift roam target.";
                return false;
            }
        }

        GymExerciseStation station = exerciseObject.GetComponent<GymExerciseStation>();
        PickupItem barPickup = barObject.GetComponent<PickupItem>();
        Rigidbody barBody = barObject.GetComponent<Rigidbody>();
        BoxCollider barShaftCollider = barObject.GetComponent<BoxCollider>();
        int[] expectedDeadliftWeights = { 60, 80, 100, 140, 180, 200, 300, 400 };
        bool hasExpectedDeadliftWeights = station != null && station.RequiresWeightSelection &&
            station.WeightOptions.Length == expectedDeadliftWeights.Length;
        if (hasExpectedDeadliftWeights)
        {
            for (int i = 0; i < expectedDeadliftWeights.Length; i++)
            {
                hasExpectedDeadliftWeights &= station.WeightOptions[i] == expectedDeadliftWeights[i];
            }
        }
        if (station == null || !station.IsDeadlift || !hasExpectedDeadliftWeights ||
            station.WeightOptions[0] != 60 || station.WeightOptions[station.WeightOptions.Length - 1] != 400 ||
            barPickup == null ||
            barPickup.ItemType != WeightType.Barbell || barBody == null ||
            barShaftCollider == null || barShaftCollider.size.y > 0.3f ||
            barShaftCollider.size.z > 0.3f ||
            !barPickup.CanBePickedUp || Mathf.Abs(barBody.mass - 60f) > 0.01f)
        {
            failure = "Deadlift station is missing its 60-400 kg weight-selection grid or pickable loaded barbell.";
            return false;
        }

        PickupItem[] mountedPickups = barObject.GetComponentsInChildren<PickupItem>(true);
        Rigidbody[] mountedBodies = barObject.GetComponentsInChildren<Rigidbody>(true);
        int mountedPlateCount = 0;
        bool hasNegativeSide = false;
        bool hasPositiveSide = false;
        List<Collider> mountedPlateColliders = new List<Collider>();
        for (int i = 0; i < mountedPickups.Length; i++)
        {
            PickupItem pickup = mountedPickups[i];
            if (pickup == null || pickup == barPickup ||
                (pickup.ItemType != WeightType.Plate20 &&
                 pickup.ItemType != WeightType.Plate10 &&
                 pickup.ItemType != WeightType.Plate5 &&
                 pickup.ItemType != WeightType.Plate))
            {
                continue;
            }

            mountedPlateCount++;
            hasNegativeSide |= pickup.transform.localPosition.x < -0.1f;
            hasPositiveSide |= pickup.transform.localPosition.x > 0.1f;
            Collider[] plateColliders = pickup.GetComponentsInChildren<Collider>(true);
            for (int colliderIndex = 0; colliderIndex < plateColliders.Length; colliderIndex++)
            {
                if (plateColliders[colliderIndex] != null)
                {
                    mountedPlateColliders.Add(plateColliders[colliderIndex]);
                }
            }
            if (Mathf.Abs(Mathf.Abs(pickup.transform.localPosition.x) -
                    GymExerciseStation.DeadliftLoadedPlateCenter) > 0.08f)
            {
                failure = "Loaded deadlift plates are not tight to the outer loading sleeve's inner pin.";
                return false;
            }
        }

        for (int first = 0; first < mountedPlateColliders.Count; first++)
        {
            for (int second = first + 1; second < mountedPlateColliders.Count; second++)
            {
                if (Physics.ComputePenetration(
                    mountedPlateColliders[first], mountedPlateColliders[first].transform.position,
                    mountedPlateColliders[first].transform.rotation,
                    mountedPlateColliders[second], mountedPlateColliders[second].transform.position,
                    mountedPlateColliders[second].transform.rotation,
                    out _, out _))
                {
                    failure = "Loaded deadlift plates overlap instead of sitting tightly on the loading sleeve.";
                    return false;
                }
            }
        }

        if (mountedBodies.Length < 3 || mountedPlateCount < 2 ||
            !hasNegativeSide || !hasPositiveSide)
        {
            failure = "Loaded deadlift bar is missing independent pickup rigidbodies on both sides.";
            return false;
        }

        if (TryFindFloorBounds(out Bounds floorBounds) &&
            TryGetRendererBounds(barObject.transform, out Bounds barBounds) &&
            barBounds.min.y < floorBounds.max.y - 0.04f)
        {
            failure = "Loaded deadlift bar or its largest plate intersects the floor.";
            return false;
        }

        if (TryGetRendererBounds(stationObject.transform, out Bounds platformBounds) &&
            TryGetRendererBounds(barObject.transform, out barBounds) &&
            Mathf.Abs(barBounds.min.y - platformBounds.max.y) > 0.035f)
        {
            failure = "The largest loaded deadlift plate is not resting on the platform surface.";
            return false;
        }

        PickupItem[] allPickups = UnityEngine.Object.FindObjectsByType<PickupItem>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        int loosePlateCount = 0;
        for (int i = 0; i < allPickups.Length; i++)
        {
            if (allPickups[i] != null &&
                allPickups[i].gameObject.name.Contains("Freeweight Loose"))
            {
                loosePlateCount++;
                Collider[] looseColliders =
                    allPickups[i].GetComponentsInChildren<Collider>(true);
                for (int colliderIndex = 0; colliderIndex < looseColliders.Length; colliderIndex++)
                {
                    if (looseColliders[colliderIndex] != null &&
                        !stationMarker.ContainsCollider(looseColliders[colliderIndex]))
                    {
                        failure = "A loose deadlift plate collider is not registered with the station marker.";
                        return false;
                    }
                }
                if (TryFindFloorBounds(out floorBounds) &&
                    TryGetRendererBounds(allPickups[i].transform, out Bounds looseBounds) &&
                    (looseBounds.min.y < floorBounds.max.y - 0.025f ||
                     looseBounds.min.y > floorBounds.max.y + 0.16f))
                {
                    failure = "A loose deadlift plate is not settled just above the floor.";
                    return false;
                }
            }
        }

        if (loosePlateCount < 7)
        {
            failure = "Deadlift platform is missing the spread loose plate set.";
            return false;
        }

        if (!ValidateDeadliftExerciseRuntime(station, out failure))
        {
            return false;
        }

        return true;
    }

    private static bool ValidateDeadliftExerciseRuntime(
        GymExerciseStation station, out string failure)
    {
        failure = string.Empty;
        if (station == null || player == null || player.playerCamera == null)
        {
            failure = "Deadlift runtime smoke test has no station, player, or camera.";
            return false;
        }

        bool began = false;
        Dictionary<Transform, Vector3> loosePlatePositions =
            new Dictionary<Transform, Vector3>();
        PickupItem[] loosePlatePickups = UnityEngine.Object.FindObjectsByType<PickupItem>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < loosePlatePickups.Length; i++)
        {
            PickupItem pickup = loosePlatePickups[i];
            if (pickup == null || pickup.gameObject.name.IndexOf(
                    "Freeweight Loose", System.StringComparison.OrdinalIgnoreCase) < 0)
            {
                continue;
            }

            Rigidbody body = pickup.GetComponent<Rigidbody>();
            if (body != null)
            {
                loosePlatePositions[pickup.transform] = body.position;
            }
        }
        try
        {
            station.BeginSession(player.playerCamera.transform);
            began = true;
            if (!station.IsSessionActive ||
                station.GetSessionHud().IndexOf("DEADLIFT", StringComparison.OrdinalIgnoreCase) < 0)
            {
                failure = "Deadlift session did not open with its exercise HUD.";
                return false;
            }

            GameObject loadedBar = GameObject.Find("Barbell DeadliftStation Loaded");
            if (loadedBar != null)
            {
                Vector3 barAxis = Vector3.ProjectOnPlane(
                    loadedBar.transform.right, Vector3.up).normalized;
                Vector3 cameraDirection = Vector3.ProjectOnPlane(
                    station.PlayerRotation * Vector3.forward, Vector3.up).normalized;
                if (barAxis.sqrMagnitude < 0.0001f || cameraDirection.sqrMagnitude < 0.0001f ||
                    Mathf.Abs(Vector3.Dot(barAxis, cameraDirection)) > 0.25f)
                {
                    failure = "Deadlift camera is still looking along the bar instead of toward the mirrors.";
                    return false;
                }
            }

            station.GetCameraPose(
                out Vector3 setupCameraPosition,
                out Quaternion setupCameraRotation);
            station.TickSession(0.05f, true, false, false);
            station.TickSession(0.05f, true, false, false);
            if (station.LastWorkoutResult == WorkoutResult.None)
            {
                failure = "Deadlift timing input did not start a workout rep.";
                return false;
            }

            station.TickSession(1.3f, false, false, false);
            station.GetCameraPose(
                out Vector3 hingeCameraPosition,
                out Quaternion hingeCameraRotation);
            if (Mathf.Abs(hingeCameraPosition.y - setupCameraPosition.y) < 0.15f ||
                Quaternion.Angle(hingeCameraRotation, setupCameraRotation) < 4f)
            {
                failure = "Deadlift camera did not follow the configured hinge-to-lockout range of motion.";
                return false;
            }

            foreach (KeyValuePair<Transform, Vector3> entry in loosePlatePositions)
            {
                if (entry.Key == null)
                {
                    continue;
                }

                Rigidbody body = entry.Key.GetComponent<Rigidbody>();
                if (body != null && Vector3.Distance(body.position, entry.Value) > 0.002f)
                {
                    failure = "A loose deadlift plate moved while the selected barbell load was lifting.";
                    return false;
                }
            }

            station.TickSession(1.4f, false, false, false);
            if (station.Repetitions < 1)
            {
                failure = "Deadlift rep timer did not complete a repetition.";
                return false;
            }

            return true;
        }
        finally
        {
            if (began && station.IsSessionActive)
            {
                station.EndSession();
            }
        }
    }

    private static bool ValidateUniformScale(
        string name, float expected, out string failure)
    {
        GameObject item = GameObject.Find(name);
        Vector3 scale = item != null ? item.transform.localScale : Vector3.zero;
        if (item == null || Mathf.Abs(scale.x - expected) > 0.001f ||
            Mathf.Abs(scale.y - expected) > 0.001f ||
            Mathf.Abs(scale.z - expected) > 0.001f)
        {
            failure = name + " scale was " + scale + ", expected " + expected + ".";
            return false;
        }

        failure = string.Empty;
        return true;
    }

    private static bool IsOnSurface(Bounds item, Bounds surface)
    {
        const float edgeTolerance = 0.05f;
        return item.min.y >= surface.max.y - 0.04f &&
            item.min.x >= surface.min.x - edgeTolerance &&
            item.max.x <= surface.max.x + edgeTolerance &&
            item.min.z >= surface.min.z - edgeTolerance &&
            item.max.z <= surface.max.z + edgeTolerance;
    }

    private static bool IsBesideSurface(Bounds item, Bounds surface)
    {
        const float separation = 0.04f;
        bool outsideX = item.max.x <= surface.min.x - separation ||
            item.min.x >= surface.max.x + separation;
        float maximumBandDistance = surface.extents.z + item.extents.z + 0.32f;
        bool sameZBand = Mathf.Abs(item.center.z - surface.center.z) <= maximumBandDistance;
        bool onFloor = item.min.y <= surface.min.y + 0.2f;
        return outsideX && sameZBand && onFloor;
    }

    private static bool IsColorClose(Color actual, Color expected)
    {
        return Mathf.Abs(actual.r - expected.r) < 0.02f &&
            Mathf.Abs(actual.g - expected.g) < 0.02f &&
            Mathf.Abs(actual.b - expected.b) < 0.02f;
    }

    private static bool IsOnShelf(Bounds item, Bounds shelf)
    {
        const float edgeTolerance = 0.04f;
        return item.min.y >= shelf.max.y - edgeTolerance &&
            item.min.x >= shelf.min.x - edgeTolerance &&
            item.max.x <= shelf.max.x + edgeTolerance &&
            item.min.z >= shelf.min.z - edgeTolerance &&
            item.max.z <= shelf.max.z + edgeTolerance;
    }

    private static bool HasHorizontalClearance(Bounds first, Bounds second)
    {
        const float separation = 0.02f;
        return first.max.x <= second.min.x - separation ||
            first.min.x >= second.max.x + separation ||
            first.max.z <= second.min.z - separation ||
            first.min.z >= second.max.z + separation;
    }

    private static bool TryFindLargestAuthoredSurface(
        string[] keywords, out Bounds bounds)
    {
        bounds = default;
        Transform[] transforms = UnityEngine.Object.FindObjectsByType<Transform>();
        bool found = false;
        float largestArea = 0f;
        for (int i = 0; i < transforms.Length; i++)
        {
            Transform candidate = transforms[i];
            if (candidate == null || ShouldSkipSurfaceCandidate(candidate))
            {
                continue;
            }

            string normalizedName = NormalizeName(candidate.name);
            bool matches = false;
            for (int keywordIndex = 0; keywordIndex < keywords.Length; keywordIndex++)
            {
                if (normalizedName.Contains(NormalizeName(keywords[keywordIndex])))
                {
                    matches = true;
                    break;
                }
            }

            if (!matches || !TryGetRendererBounds(candidate, out Bounds candidateBounds))
            {
                continue;
            }

            float area = candidateBounds.size.x * candidateBounds.size.z;
            if (!found || area > largestArea)
            {
                found = true;
                largestArea = area;
                bounds = candidateBounds;
            }
        }

        return found;
    }

    private static bool TryFindNearestNamedBounds(
        string[] keywords, Vector3 origin, out Bounds bounds)
    {
        bounds = default;
        Transform[] transforms = UnityEngine.Object.FindObjectsByType<Transform>();
        bool found = false;
        float bestDistance = float.PositiveInfinity;
        for (int i = 0; i < transforms.Length; i++)
        {
            Transform candidate = transforms[i];
            if (candidate == null || ShouldSkipSurfaceCandidate(candidate))
            {
                continue;
            }

            string normalizedName = NormalizeName(candidate.name);
            bool matches = false;
            for (int keywordIndex = 0; keywordIndex < keywords.Length; keywordIndex++)
            {
                if (normalizedName.Contains(NormalizeName(keywords[keywordIndex])))
                {
                    matches = true;
                    break;
                }
            }

            if (!matches || !TryGetRendererBounds(candidate, out Bounds candidateBounds))
            {
                continue;
            }

            float distance = (candidateBounds.center - origin).sqrMagnitude;
            if (!found || distance < bestDistance)
            {
                found = true;
                bestDistance = distance;
                bounds = candidateBounds;
            }
        }

        return found;
    }

    private static bool TryFindFloorBounds(out Bounds bounds)
    {
        bounds = default;
        GameObject floor = GameObject.Find("Rubber Floor");
        if (floor == null)
        {
            return false;
        }

        Collider collider = floor.GetComponent<Collider>();
        Renderer renderer = floor.GetComponent<Renderer>();
        if (collider != null)
        {
            bounds = collider.bounds;
            return true;
        }

        if (renderer != null)
        {
            bounds = renderer.bounds;
            return true;
        }

        return false;
    }

    private static bool IsNearBounds(Vector3 point, Bounds bounds, float margin)
    {
        float xDistance = Mathf.Max(bounds.min.x - point.x, 0f, point.x - bounds.max.x);
        float zDistance = Mathf.Max(bounds.min.z - point.z, 0f, point.z - bounds.max.z);
        return xDistance * xDistance + zDistance * zDistance <= margin * margin;
    }

    private static bool ShouldSkipSurfaceCandidate(Transform candidate)
    {
        if (candidate.GetComponentInParent<PlayerMovement>() != null ||
            candidate.GetComponentInParent<EnemyFighter>() != null)
        {
            return true;
        }

        for (Transform current = candidate; current != null; current = current.parent)
        {
            string normalized = NormalizeName(current.name);
            if (normalized.Contains("gyminteriorruntime") ||
                normalized.Contains("gymbackarearuntime") ||
                normalized.Contains("gymexteriorruntime") ||
                normalized.Contains("gymlooseitemsruntime"))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryGetRendererBounds(Transform target, out Bounds bounds)
    {
        bounds = default;
        Renderer[] renderers = target.GetComponentsInChildren<Renderer>(true);
        bool found = false;
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null || !renderer.enabled ||
                renderer is ParticleSystemRenderer)
            {
                continue;
            }

            if (!found)
            {
                bounds = renderer.bounds;
                found = true;
            }
            else
            {
                bounds.Encapsulate(renderer.bounds);
            }
        }

        return found;
    }

    private static string NormalizeName(string value)
    {
        return value.ToLowerInvariant()
            .Replace(" ", string.Empty)
            .Replace("_", string.Empty)
            .Replace("-", string.Empty)
            .Replace("(", string.Empty)
            .Replace(")", string.Empty);
    }

    private static GymBackRoomInteractable FindInteractable(
        GymBackRoomInteractionType type)
    {
        GymBackRoomInteractable[] all = UnityEngine.Object.FindObjectsByType<
            GymBackRoomInteractable>(FindObjectsSortMode.None);
        for (int i = 0; i < all.Length; i++)
        {
            if (all[i] != null && all[i].InteractionType == type)
            {
                return all[i];
            }
        }
        return null;
    }

    private static EnemyFighter FindDialogueTarget()
    {
        EnemyFighter[] fighters = UnityEngine.Object.FindObjectsByType<EnemyFighter>(
            FindObjectsSortMode.None);
        for (int i = 0; i < fighters.Length; i++)
        {
            if (fighters[i] != null && !fighters[i].IsDead &&
                !fighters[i].IsAggressive &&
                fighters[i].Identity == BodybuilderIdentity.Manwithsuit1)
            {
                return fighters[i];
            }
        }
        for (int i = 0; i < fighters.Length; i++)
        {
            if (fighters[i] != null && !fighters[i].IsDead && !fighters[i].IsAggressive)
            {
                return fighters[i];
            }
        }
        return null;
    }

    private static EnemyFighter FindNormalEnemy()
    {
        EnemyFighter[] fighters = UnityEngine.Object.FindObjectsByType<EnemyFighter>(
            FindObjectsSortMode.None);
        for (int i = 0; i < fighters.Length; i++)
        {
            if (fighters[i] != null && !fighters[i].IsDead &&
                fighters[i].Identity != BodybuilderIdentity.Ronnie &&
                fighters[i].Identity != BodybuilderIdentity.Goku &&
                fighters[i].Identity != BodybuilderIdentity.Manwithsuit1)
            {
                return fighters[i];
            }
        }
        return null;
    }

    private static void MovePlayer(Vector3 position)
    {
        player.transform.SetPositionAndRotation(position, player.transform.rotation);
        Physics.SyncTransforms();
    }

    private static void CloseLockerMenuForVerification()
    {
        MethodInfo closeMethod = typeof(GymExperienceService).GetMethod(
            "CloseLockerMenu", BindingFlags.Instance | BindingFlags.NonPublic);
        if (closeMethod == null)
        {
            throw new MissingMethodException("GymExperienceService.CloseLockerMenu");
        }
        closeMethod.Invoke(progression, null);
    }

    private static Transform FindDescendant(Transform root, string objectName)
    {
        if (root == null)
        {
            return null;
        }
        if (root.name == objectName)
        {
            return root;
        }
        for (int i = 0; i < root.childCount; i++)
        {
            Transform result = FindDescendant(root.GetChild(i), objectName);
            if (result != null)
            {
                return result;
            }
        }
        return null;
    }

    private static void Complete(bool success, string summary)
    {
        if (completed)
        {
            return;
        }
        completed = success;
        phase = -1;
        RestorePlayerPrefsAndPlayer();
        Debug.Log(success
            ? "GYMCHAOS_PROGRESSION_VERIFICATION_OK " + summary
            : "GYMCHAOS_PROGRESSION_VERIFICATION_FAILED " + summary);
        EditorApplication.isPlaying = false;
    }

    private static void Fail(string message)
    {
        Complete(false, message);
    }

    private static void RestorePlayerPrefsAndPlayer()
    {
        Time.timeScale = 1f;
        if (player != null)
        {
            player.transform.SetPositionAndRotation(
                playerPositionBeforeTest, playerRotationBeforeTest);
            if (playerController != null)
            {
                playerController.enabled = playerControllerWasEnabled;
            }
        }

        bool hadOriginalSave = EditorPrefs.GetBool(OriginalSavePresentKey, false);
        string originalSave = EditorPrefs.GetString(OriginalSaveKey, string.Empty);
        if (hadOriginalSave)
        {
            PlayerPrefs.SetString(ProgressionSaveKey, originalSave);
        }
        else
        {
            PlayerPrefs.DeleteKey(ProgressionSaveKey);
        }
        PlayerPrefs.Save();
    }
}
