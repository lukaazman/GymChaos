using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

[InitializeOnLoad]
public static class GymChaosLockerVisitVerifier
{
    private const string RequestedKey = "GymChaos.LockerVisitVerificationRequested";
    private const string OriginalSaveKey =
        "GymChaos.LockerVisitVerificationOriginalSave";
    private const string OriginalSavePresentKey =
        "GymChaos.LockerVisitVerificationOriginalSavePresent";
    private const string ProgressionSaveKey = "GymChaos.Progression.v1";
    private static double startedAt;
    private static bool requested;
    private static bool completed;
    private static EnemyFighter fighter;
    private static GymVisitorAgent agent;
    private static bool arrived;
    private static bool bagsObserved;
    private static bool leftBenchBagsObserved;
    private static bool rightBenchBagsObserved;
    private static bool slotObserved;
    private static bool capacityObserved;
    private static bool cohortValidated;
    private static bool layoutValidated;
    private static bool layoutCaptureAttempted;
    private static bool layoutCaptureAvailable;
    private static bool verificationFailed;

    static GymChaosLockerVisitVerifier()
    {
        if (!SessionState.GetBool(RequestedKey, false))
        {
            return;
        }

        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        EditorApplication.delayCall += ResumeAfterDomainReload;
    }

    [MenuItem("Tools/GymChaos/Run Locker Visit Verification")]
    public static void Run()
    {
        string originalSave = PlayerPrefs.GetString(ProgressionSaveKey, string.Empty);
        GymChaosVerifierPrefs.SetBool(OriginalSavePresentKey,
            PlayerPrefs.HasKey(ProgressionSaveKey));
        GymChaosVerifierPrefs.SetString(OriginalSaveKey, originalSave);
        // Locker cohort verification must not inherit a negative-reputation
        // save that intentionally auto-aggros ordinary visitors.
        PlayerPrefs.DeleteKey(ProgressionSaveKey);
        PlayerPrefs.Save();
        startedAt = 0d;
        requested = false;
        completed = false;
        fighter = null;
        agent = null;
        arrived = false;
        bagsObserved = false;
        leftBenchBagsObserved = false;
        rightBenchBagsObserved = false;
        slotObserved = false;
        capacityObserved = false;
        cohortValidated = false;
        layoutValidated = false;
        layoutCaptureAttempted = false;
        layoutCaptureAvailable = false;
        verificationFailed = false;
        SessionState.SetBool(RequestedKey, true);
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        EditorApplication.isPlaying = true;
    }

    private static void ResumeAfterDomainReload()
    {
        if (!EditorApplication.isPlaying)
        {
            return;
        }

        startedAt = EditorApplication.timeSinceStartup;
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
    }

    private static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            Time.timeScale = 1f;
            startedAt = EditorApplication.timeSinceStartup;
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
        }
        else if (state == PlayModeStateChange.EnteredEditMode)
        {
            EditorApplication.update -= Tick;
            RestoreOriginalProgressionSave();
            SessionState.EraseBool(RequestedKey);
            if (Application.isBatchMode)
            {
                EditorApplication.Exit(completed && !verificationFailed ? 0 : 1);
            }
        }
    }

    private static void Tick()
    {
        if (completed || !EditorApplication.isPlaying)
        {
            return;
        }

        double elapsed = EditorApplication.timeSinceStartup - startedAt;
        GymVisitorDirector director =
            UnityEngine.Object.FindAnyObjectByType<GymVisitorDirector>();
        if (director == null)
        {
            if (elapsed > 30d)
            {
                Fail("director_not_ready");
            }
            return;
        }

        if (!cohortValidated && director.LockerAvailableMemberCountForVerification > 0)
        {
            int available = director.LockerAvailableMemberCountForVerification;
            int selected = director.LockerCohortSelectedForVerification;
            if (available != 6 || selected != 2 ||
                director.LockerEligibleMemberCountForVerification != 6 ||
                director.ProteinStoreVisitChanceForVerification <= 0f ||
                director.ProteinStoreVisitChanceForVerification > 0.15f)
            {
                Fail($"cohort_contract_failed eligible={director.LockerEligibleMemberCountForVerification} " +
                    $"available={available} selected={selected} " +
                    $"storeChance={director.ProteinStoreVisitChanceForVerification:0.00}");
                return;
            }
            cohortValidated = true;
        }
        if (!layoutValidated && elapsed > 3d)
        {
            // Authored sinks, toilet, benches and bags arrive through the
            // runtime GLB loader.  Wait for that finite import window before
            // treating a missing renderer as a layout failure.
            if (!GymBackRoomBuilder.HasAuthoredLockerProps && elapsed < 12d)
            {
                return;
            }
            layoutValidated = GymBackRoomBuilder.HasRequiredLockerLayoutForVerification(
                out string layoutDetails);
            if (!layoutValidated)
            {
                Fail("locker_layout_contract_failed " + layoutDetails);
                return;
            }
            float passage = GymBackRoomBuilder.BathroomPassageWidthForVerification;
            if (passage < 1.95f || passage > 2.3f)
            {
                Fail($"bathroom_passage_width_failed width={passage:F2}");
                return;
            }
            Debug.Log($"GYMCHAOS_BATHROOM_PASSAGE_OK width={passage:F2}");
        }

        if (layoutValidated && requested && fighter != null &&
            !layoutCaptureAttempted)
        {
            layoutCaptureAttempted = true;
            // Keep the authored bags visible for the direct capture so the
            // image evidence covers both the bench support and the runtime
            // bag placement contract, not only the empty room shell.
            GymBackRoomBuilder.ShowBenchBagsForVisitor(fighter.Identity);
            layoutCaptureAvailable = CaptureLockerLayoutEvidence();
        }

        if (!requested && elapsed > 3d)
        {
            requested = director.BeginLockerVisitForVerification(out fighter);
            if (requested)
            {
                agent = fighter.GetComponent<GymVisitorAgent>();
                Debug.Log(
                    $"GYMCHAOS_LOCKER_VISIT_TEST_STARTED enemy={fighter.Identity} " +
                    $"props={GymBackRoomBuilder.HasAuthoredLockerProps}");
            }
            else if (elapsed > 15d)
            {
                Fail("no_present_locker_cohort_member");
                return;
            }
        }

        if (!requested || fighter == null || agent == null)
        {
            return;
        }

        int leftBags = GymBackRoomBuilder.GetVisibleBagCountForBench(0);
        int rightBags = GymBackRoomBuilder.GetVisibleBagCountForBench(1);
        if (GymBackRoomBuilder.VisibleBenchBagCount > 0)
        {
            bagsObserved = true;
        }
        leftBenchBagsObserved |= leftBags >= 1 && leftBags <= 2;
        rightBenchBagsObserved |= rightBags >= 1 && rightBags <= 2;
        capacityObserved |= GymBackRoomBuilder.LockerSlotCapacity == 4;
        if (GymBackRoomBuilder.ReservedLockerSlotCount > 0)
        {
            slotObserved = true;
        }
        if (agent.State == GymVisitorAgent.VisitorState.LockerRoomVisit)
        {
            arrived = true;
        }

        if (arrived &&
            agent.State != GymVisitorAgent.VisitorState.ApproachingLockerRoom &&
            agent.State != GymVisitorAgent.VisitorState.LockerRoomVisit &&
            // Only this visitor's slot and bag must be gone: other members
            // may keep their gym bags on the benches until they leave.
            !GymBackRoomBuilder.HasLockerSlot(fighter.Identity) &&
            !GymBackRoomBuilder.IsBagVisitor(fighter.Identity) &&
            GymBackRoomBuilder.VisibleBenchBagCount <= GymBackRoomBuilder.MemberBagCount)
        {
            if (!slotObserved || !capacityObserved || !cohortValidated ||
                !layoutValidated || !GymBackRoomBuilder.HasAuthoredLockerProps)
            {
                Fail(
                    $"lifecycle_observation_failed bags={bagsObserved} " +
                    $"left={leftBenchBagsObserved} right={rightBenchBagsObserved} " +
                    $"slot={slotObserved} capacity={capacityObserved} " +
                    $"cohort={cohortValidated} layout={layoutValidated} " +
                    $"props={GymBackRoomBuilder.HasAuthoredLockerProps}");
                return;
            }

            if (!VerifyBagCountVariations(out string bagVariants))
            {
                Fail("bag_variants_failed " + bagVariants);
                return;
            }
            completed = true;
            Debug.Log(
                $"GYMCHAOS_LOCKER_VISIT_OK enemy={fighter.Identity} " +
                $"arrived={arrived} bagsObserved={bagsObserved} " +
                $"leftBags={GymBackRoomBuilder.GetVisibleBagCountForBench(0)} " +
                $"rightBags={GymBackRoomBuilder.GetVisibleBagCountForBench(1)} " +
                $"capacity={GymBackRoomBuilder.LockerSlotCapacity} " +
                $"cohort={cohortValidated} layout={layoutValidated} " +
                $"slotObserved={slotObserved} layoutCapture={layoutCaptureAvailable} " +
                $"bagsHidden=True bagVariants={bagVariants}");
            EditorApplication.isPlaying = false;
            return;
        }

        if (elapsed > 55d)
        {
            Fail(
                $"timeout requested={requested} arrived={arrived} " +
                $"state={agent.State} pos={fighter.transform.position} " +
                $"target={agent.TravelTargetForVerification} " +
                $"reserved={GymBackRoomBuilder.ReservedLockerSlotCount} " +
                $"bags={GymBackRoomBuilder.VisibleBenchBagCount} " +
                $"blocker={fighter.LastVisitorRouteBlocker}");
        }
    }

    private static bool CaptureLockerLayoutEvidence()
    {
        if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
        {
            Debug.LogWarning(
                "GYMCHAOS_LOCKER_LAYOUT_CAPTURE_UNAVAILABLE graphics=Null");
            return false;
        }

        if (!GymBackRoomBuilder.TryGetRoomBounds(out Bounds roomBounds))
        {
            Debug.LogError("GYMCHAOS_LOCKER_LAYOUT_CAPTURE_FAILED bounds=missing");
            return false;
        }

        GameObject cameraObject = new GameObject(
            "GymChaos Locker Layout Evidence Camera");
        Camera camera = cameraObject.AddComponent<Camera>();
        RenderTexture target = new RenderTexture(
            960, 540, 24, RenderTextureFormat.ARGB32);
        RenderTexture previousActive = RenderTexture.active;
        try
        {
            Vector3 overviewTarget = new Vector3(
                roomBounds.center.x,
                roomBounds.min.y + roomBounds.size.y * 0.36f,
                roomBounds.center.z);
            Vector3 overviewCamera = new Vector3(
                roomBounds.center.x - roomBounds.extents.x * 0.42f,
                roomBounds.min.y + roomBounds.size.y * 0.54f,
                roomBounds.center.z + 0.8f);
            target.Create();
            camera.fieldOfView = 64f;
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = 120f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.004f, 0.008f, 0.014f, 1f);
            // Do not capture the gameplay HUD as if it were room geometry.
            camera.cullingMask = ~(1 << 5);
            bool overview = CaptureLockerView(
                camera, target, overviewCamera, overviewTarget, "layout");

            // East-side bathroom view: the camera looks back across the
            // divider, sinks, toilet, and the three bathroom mirrors.
            Vector3 fixturesTarget = new Vector3(
                roomBounds.center.x + 4.25f,
                roomBounds.min.y + 1.45f,
                roomBounds.center.z - 0.65f);
            Vector3 fixturesCamera = new Vector3(
                roomBounds.center.x + 7.25f,
                roomBounds.min.y + 2.55f,
                roomBounds.center.z - 5.25f);
            bool fixtures = CaptureLockerView(
                camera, target, fixturesCamera, fixturesTarget, "fixtures");

            // North-side changing view: keep both benches and their active
            // one/two-bag variants in frame while retaining locker context.
            Vector3 bagsTarget = new Vector3(
                roomBounds.center.x - 4.05f,
                roomBounds.min.y + 1.05f,
                roomBounds.center.z);
            Vector3 bagsCamera = new Vector3(
                roomBounds.center.x + 1.35f,
                roomBounds.min.y + 2.65f,
                roomBounds.center.z + 5.65f);
            bool bags = CaptureLockerView(
                camera, target, bagsCamera, bagsTarget, "bags");

            // South wall mirror view: the large changing-room mirror and its
            // player-reflection surface need a dedicated angle to be visible.
            Vector3 mirrorTarget = new Vector3(
                roomBounds.center.x,
                roomBounds.min.y + 2.35f,
                roomBounds.center.z - 6.20f);
            Vector3 mirrorCamera = new Vector3(
                roomBounds.center.x - 0.55f,
                roomBounds.min.y + 2.45f,
                roomBounds.center.z - 2.25f);
            bool mirror = CaptureLockerMirrorViewWithPlayer(
                camera, target, mirrorCamera, mirrorTarget);
            bool visible = overview && fixtures && bags && mirror;
            Debug.Log(
                $"GYMCHAOS_LOCKER_LAYOUT_CAPTURE_RESULT visible={visible} " +
                $"views=4 overview={overview} fixtures={fixtures} " +
                $"bags={bags} mirror={mirror} " +
                $"paths=locker_layout.png,locker_fixtures.png," +
                $"locker_bags.png,locker_mirror.png bounds={roomBounds}");
            return visible;
        }
        catch (Exception exception)
        {
            Debug.LogError(
                $"GYMCHAOS_LOCKER_LAYOUT_CAPTURE_FAILED " +
                $"{exception.GetType().Name}: {exception.Message}");
            return false;
        }
        finally
        {
            RenderTexture.active = previousActive;
            target.Release();
            UnityEngine.Object.DestroyImmediate(target);
            UnityEngine.Object.DestroyImmediate(cameraObject);
        }
    }

    private static bool CaptureLockerMirrorViewWithPlayer(
        Camera evidenceCamera,
        RenderTexture target,
        Vector3 cameraPosition,
        Vector3 targetPosition)
    {
        PlayerMovement player = UnityEngine.Object.FindFirstObjectByType<PlayerMovement>();
        if (player == null || player.playerCamera == null)
        {
            Debug.LogWarning(
                "GYMCHAOS_LOCKER_MIRROR_CAPTURE_PLAYER_UNAVAILABLE");
            return CaptureLockerView(
                evidenceCamera, target, cameraPosition, targetPosition, "mirror");
        }

        CharacterController controller = player.GetComponent<CharacterController>();
        bool playerWasEnabled = player.enabled;
        bool controllerWasEnabled = controller != null && controller.enabled;
        Vector3 originalPosition = player.transform.position;
        Quaternion originalRotation = player.transform.rotation;
        Vector3 originalCameraLocalPosition = player.playerCamera.transform.localPosition;
        Quaternion originalCameraLocalRotation = player.playerCamera.transform.localRotation;
        PlanarGymMirror mirror = UnityEngine.Object.FindFirstObjectByType<PlanarGymMirror>();
        bool mirrorWasContinuous = mirror != null && mirror.ContinuousRefresh;
        try
        {
            if (!GymBackRoomBuilder.TryGetLockerPreviewPose(
                    out Vector3 mirrorPlayerPosition,
                    out Quaternion mirrorPlayerRotation))
            {
                Debug.LogWarning("GYMCHAOS_LOCKER_MIRROR_CAPTURE_PLAYER_POSE_UNAVAILABLE");
                return CaptureLockerView(
                    evidenceCamera, target, cameraPosition, targetPosition, "mirror");
            }

            player.enabled = false;
            player.SetCinematicPose(
                mirrorPlayerPosition,
                mirrorPlayerRotation,
                originalCameraLocalPosition,
                originalCameraLocalRotation);
            Physics.SyncTransforms();
            if (mirror != null)
            {
                mirror.ContinuousRefresh = true;
                mirror.RequestImmediateRefresh();
            }
            bool captured = CaptureLockerView(
                evidenceCamera, target, cameraPosition, targetPosition, "mirror");
            Debug.Log(
                $"GYMCHAOS_LOCKER_MIRROR_PLAYER_CAPTURE_OK captured={captured} " +
                $"player={mirrorPlayerPosition} camera={cameraPosition}");
            return captured;
        }
        finally
        {
            player.transform.SetPositionAndRotation(originalPosition, originalRotation);
            player.playerCamera.transform.SetLocalPositionAndRotation(
                originalCameraLocalPosition, originalCameraLocalRotation);
            player.enabled = playerWasEnabled;
            if (controller != null) controller.enabled = controllerWasEnabled;
            if (mirror != null)
            {
                mirror.ContinuousRefresh = mirrorWasContinuous;
                mirror.RequestImmediateRefresh();
            }
            Physics.SyncTransforms();
        }
    }

    private static bool CaptureLockerView(
        Camera camera,
        RenderTexture target,
        Vector3 cameraPosition,
        Vector3 targetPosition,
        string viewName)
    {
        camera.transform.SetPositionAndRotation(
            cameraPosition,
            Quaternion.LookRotation(targetPosition - cameraPosition, Vector3.up));
        camera.targetTexture = target;
        PlanarGymMirror[] mirrors = UnityEngine.Object.FindObjectsByType<PlanarGymMirror>(
            FindObjectsSortMode.None);
        foreach (PlanarGymMirror mirror in mirrors)
        {
            Camera source = mirror.SourceCameraForVerification;
            if (source == null || mirror.ReflectionCamera == null) continue;
            Vector3 savedPosition = source.transform.position;
            Quaternion savedRotation = source.transform.rotation;
            float savedAspect = source.aspect;
            float savedFov = source.fieldOfView;
            try
            {
                source.transform.SetPositionAndRotation(camera.transform.position, camera.transform.rotation);
                source.aspect = camera.aspect;
                source.fieldOfView = camera.fieldOfView;
                mirror.RequestImmediateRefresh();
                mirror.ReflectionCamera.Render();
            }
            finally
            {
                source.transform.SetPositionAndRotation(savedPosition, savedRotation);
                source.aspect = savedAspect;
                source.fieldOfView = savedFov;
            }
        }
        camera.Render();
        RenderTexture.active = target;
        Texture2D image = new Texture2D(
            target.width, target.height, TextureFormat.RGB24, false);
        try
        {
            image.ReadPixels(
                new Rect(0f, 0f, target.width, target.height), 0, 0);
            image.Apply(false, false);
            Color32[] pixels = image.GetPixels32();
            int visiblePixels = 0;
            int minimumLuminance = 255;
            int maximumLuminance = 0;
            for (int index = 0; index < pixels.Length; index++)
            {
                Color32 pixel = pixels[index];
                int luminance = (pixel.r * 3 + pixel.g * 6 + pixel.b) / 10;
                minimumLuminance = Mathf.Min(minimumLuminance, luminance);
                maximumLuminance = Mathf.Max(maximumLuminance, luminance);
                if (luminance > 12)
                {
                    visiblePixels++;
                }
            }

            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string workspaceRoot = Directory.GetParent(projectRoot).FullName;
            string evidenceDirectory = Path.Combine(
                workspaceRoot, "Assets", "LockerRecreation", "evidence_unity");
            Directory.CreateDirectory(evidenceDirectory);
            string outputPath = Path.Combine(
                evidenceDirectory, "locker_" + viewName + ".png");
            File.WriteAllBytes(outputPath, image.EncodeToPNG());
            bool visible = visiblePixels >= 1200 &&
                maximumLuminance - minimumLuminance >= 24;
            Debug.Log(
                $"GYMCHAOS_LOCKER_LAYOUT_VIEW name={viewName} visible={visible} " +
                $"pixels={visiblePixels} luminanceRange=" +
                $"{maximumLuminance - minimumLuminance} path={outputPath}");
            return visible;
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(image);
        }
    }

    private static bool VerifyBagCountVariations(out string details)
    {
        UnityEngine.Random.State savedRandom = UnityEngine.Random.state;
        int assigned = 0;
        bool simultaneous = false;
        try
        {
            UnityEngine.Random.InitState(92826);
            // Members' own gym bags may be on the benches: measure the
            // locker-visit bag rule on empty benches.
            GymBackRoomBuilder.HideBenchBags();
            for (int cycle = 0; cycle < 500; cycle++)
            {
                GymBackRoomBuilder.ShowBenchBagsForVisitor(BodybuilderIdentity.Cbum);
                int first = GymBackRoomBuilder.VisibleBenchBagCount;
                GymBackRoomBuilder.ShowBenchBagsForVisitor(BodybuilderIdentity.Cbum);
                if (GymBackRoomBuilder.VisibleBenchBagCount != first)
                    throw new InvalidOperationException("Repeated bag request rerolled an owner.");
                GymBackRoomBuilder.ShowBenchBagsForVisitor(BodybuilderIdentity.Arnold);
                int total = GymBackRoomBuilder.VisibleBenchBagCount;
                int second = total - first;
                if (first > 1 || second < 0 || second > 1 || total > 2 ||
                    GymBackRoomBuilder.GetVisibleBagCountForBench(0) > 1 ||
                    GymBackRoomBuilder.GetVisibleBagCountForBench(1) > 1)
                    throw new InvalidOperationException("Bag owner or bench capacity exceeded.");
                float seatGap = GymBackRoomBuilder.MaxVisibleBenchBagSeatGapForVerification();
                if (seatGap > 0.03f)
                    throw new InvalidOperationException(
                        $"Visible bench bag is not resting on its seat: cycle={cycle} gap={seatGap:F3}.");
                assigned += total;
                simultaneous |= total == 2;
                GymBackRoomBuilder.HideBenchBagsForVisitor(BodybuilderIdentity.Cbum);
                if (GymBackRoomBuilder.VisibleBenchBagCount != second)
                    throw new InvalidOperationException("One visitor removed another visitor's bag.");
                GymBackRoomBuilder.HideBenchBagsForVisitor(BodybuilderIdentity.Arnold);
                if (GymBackRoomBuilder.VisibleBenchBagCount != 0 || GymBackRoomBuilder.HasActiveBagVisitor)
                    throw new InvalidOperationException("Bag cleanup retained an owner or visible bag.");
            }
        }
        finally
        {
            UnityEngine.Random.state = savedRandom;
            GymBackRoomBuilder.HideBenchBags();
        }
        details = $"assigned={assigned}/1000 rare={assigned >= 80 && assigned <= 230} " +
            $"simultaneous={simultaneous} maxPerOwner=1 maxPerBench=1 independentCleanup=True";
        return assigned >= 80 && assigned <= 230 && simultaneous;
    }
    private static void Fail(string reason)
    {
        if (completed)
        {
            return;
        }

        completed = true;
        verificationFailed = true;
        Debug.LogError(
            $"GYMCHAOS_LOCKER_VISIT_FAILED reason={reason} " +
            $"bagsObserved={bagsObserved} slotObserved={slotObserved}");
        EditorApplication.isPlaying = false;
    }

    private static void RestoreOriginalProgressionSave()
    {
        bool hadOriginalSave = GymChaosVerifierPrefs.GetBool(OriginalSavePresentKey, false);
        string originalSave = GymChaosVerifierPrefs.GetString(OriginalSaveKey, string.Empty);
        if (hadOriginalSave)
            PlayerPrefs.SetString(ProgressionSaveKey, originalSave);
        else
            PlayerPrefs.DeleteKey(ProgressionSaveKey);
        PlayerPrefs.Save();
        GymChaosVerifierPrefs.DeleteKey(OriginalSaveKey);
        GymChaosVerifierPrefs.DeleteKey(OriginalSavePresentKey);
    }
}
