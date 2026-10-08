using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

[InitializeOnLoad]
public static class GymChaosCityDystopiaVerifier
{
    private const string RequestedKey = "GymChaos.CityDystopiaVerificationRequested";
    private const float MaxAcceptedFrameMs = 50f;
    private static double enteredPlayTime;
    private static int frameSampleCount;
    private static int frameSpikeCount;
    private static float maxFrameTimeMs;
    private static bool frameSamplingStarted;
    private static double sceneReadyAt;
    private static bool verificationFinished;
    private static bool verificationFailed;

    static GymChaosCityDystopiaVerifier()
    {
        if (!SessionState.GetBool(RequestedKey, false))
        {
            return;
        }

        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        EditorApplication.delayCall += ResumeAfterDomainReload;
    }

    [MenuItem("Tools/GymChaos/Run City Dystopia Verification")]
    public static void Run()
    {
        verificationFinished = false;
        verificationFailed = false;
        frameSampleCount = 0;
        frameSpikeCount = 0;
        maxFrameTimeMs = 0f;
        frameSamplingStarted = false;
        sceneReadyAt = 0d;
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

        enteredPlayTime = EditorApplication.timeSinceStartup;
        frameSamplingStarted = false;
        sceneReadyAt = 0d;
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
    }

    private static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            enteredPlayTime = EditorApplication.timeSinceStartup;
            frameSamplingStarted = false;
            sceneReadyAt = 0d;
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
        }
        else if (state == PlayModeStateChange.EnteredEditMode)
        {
            EditorApplication.update -= Tick;
            SessionState.EraseBool(RequestedKey);
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            if (Application.isBatchMode)
            {
                EditorApplication.Exit(verificationFailed ? 1 : 0);
            }
        }
    }

    private static void Tick()
    {
        if (verificationFinished)
        {
            return;
        }

        double now = EditorApplication.timeSinceStartup;
        bool sceneReady = GymOutdoorBuilder.IsBuilt &&
            GymCityDystopiaSurroundings.IsBuilt &&
            GymCityDystopiaSurroundings.ReadyPlacements >=
                GymCityDystopiaSurroundings.ExpectedPlacements &&
            GymLooseItemSpawner.IsReady;
        if (sceneReady && !frameSamplingStarted)
        {
            frameSamplingStarted = true;
            sceneReadyAt = now;
            GameObject cityRootObject =
                GameObject.Find("City Dystopia Surroundings (Runtime)");
            if (cityRootObject != null &&
                cityRootObject.GetComponent<GymCityDystopiaFrameSampler>() == null)
            {
                cityRootObject.AddComponent<GymCityDystopiaFrameSampler>();
            }
        }

        double elapsed = EditorApplication.timeSinceStartup - enteredPlayTime;
        if (!GymOutdoorBuilder.IsBuilt ||
            !GymCityDystopiaSurroundings.IsBuilt ||
            GymCityDystopiaSurroundings.ReadyPlacements <
            GymCityDystopiaSurroundings.ExpectedPlacements ||
            !GymLooseItemSpawner.IsReady)
        {
            if (elapsed < 90d)
            {
                return;
            }

            Finish(false, "runtime build did not finish within 90 seconds");
            return;
        }

        if (frameSamplingStarted && frameSampleCount < 30 &&
            now - sceneReadyAt < 3d)
        {
            return;
        }

        bool passed = VerifyScene(out string details);
        Finish(passed, details);
    }

    private static bool VerifyScene(out string details)
    {
        GameObject outdoor = GameObject.Find("Gym Exterior (Runtime)");
        GameObject cityRoot = GameObject.Find("City Dystopia Surroundings (Runtime)");
        bool outdoorExists = outdoor != null;
        bool cityRootExists = cityRoot != null;
        bool expectedPlacements =
            GymCityDystopiaSurroundings.ExpectedPlacements == 8;
        bool readyPlacements =
            GymCityDystopiaSurroundings.ReadyPlacements == 8;
        bool noLoadFailures =
            GymCityDystopiaSurroundings.LoadFailures == 0;
        bool noHorizontalOverlaps =
            GymCityDystopiaSurroundings.HorizontalOverlaps == 0;
        bool cheapBackgroundProfile = cityRootExists &&
            GymCityDystopiaSurroundings.CityRendererCount > 0 &&
            GymCityDystopiaSurroundings.CheapRendererCount ==
                GymCityDystopiaSurroundings.CityRendererCount &&
            GymCityDystopiaSurroundings.CityColliderCount == 0 &&
            VerifyCheapBackgroundRenderers(cityRoot.transform);
        bool cityChildCount = cityRootExists && cityRoot.transform.childCount == 12;
        bool allSides = cityRootExists &&
            HasChild(cityRoot.transform, "City Dystopia North") &&
            HasChild(cityRoot.transform, "City Dystopia South") &&
            HasChild(cityRoot.transform, "City Dystopia East") &&
            HasChild(cityRoot.transform, "City Dystopia West");
        bool allCorners = cityRootExists &&
            HasChild(cityRoot.transform, "City Dystopia Corner SW") &&
            HasChild(cityRoot.transform, "City Dystopia Corner SE") &&
            HasChild(cityRoot.transform, "City Dystopia Corner NW") &&
            HasChild(cityRoot.transform, "City Dystopia Corner NE");
        bool cityHasGeometry = cityRootExists &&
            // 14 GLB materials per placement plus the four ground infills,
            // minus the dropped window-frame squares.
            cityRoot.GetComponentsInChildren<MeshRenderer>(true).Length >= 8 * 14 + 4 -
                RuntimeGlbSceneLoader.SkippedTowerWindowFramePartsForVerification;
        bool cityGroundInfill = cityRootExists &&
            HasChild(cityRoot.transform, "City Dystopia Ground North") &&
            HasChild(cityRoot.transform, "City Dystopia Ground South") &&
            HasChild(cityRoot.transform, "City Dystopia Ground East") &&
            HasChild(cityRoot.transform, "City Dystopia Ground West");
        bool cityHasNoColliders = cityRootExists &&
            cityRoot.GetComponentsInChildren<Collider>(true).Length == 0;
        bool placementsOutsideEnvelope = cityRootExists &&
            VerifyPlacementsOutsideEnvelope(
                cityRoot.transform,
                GymCityDystopiaSurroundings.ProtectedBounds);
        bool lockerRoomClear = cityRootExists &&
            VerifyCityRingClearOfBackRoom(cityRoot.transform);
        bool firstPersonHandsUseStableMaterial =
            HasStableFirstPersonMaterials();
        bool legacyExternalAbsent = outdoorExists &&
            CountLegacyExternalObjects(outdoor.transform) == 0;
        bool protectedGameplayObjectsPresent = outdoorExists &&
            HasChild(outdoor.transform, "Mini Parking Lot") &&
            HasChild(outdoor.transform, "Path from Gym Door") &&
            HasChild(outdoor.transform, "Visitor Vehicle Road") &&
            HasChild(outdoor.transform, "Visitor Vehicle Road Extension") &&
            HasChild(outdoor.transform, "Outdoor Boundary - Parking North") &&
            HasChild(outdoor.transform, "Outdoor Boundary - Path Outer") &&
            GameObject.Find("Gym Visitor Doorway") != null;
        bool requiredCityMaterials = cityRootExists &&
            HasRequiredMaterials(cityRoot);
        bool texturedFacadeMaterials = cityRootExists &&
            HasTexturedFacadeMaterials(cityRoot);
        bool noForbiddenDressing = cityRootExists &&
            !ContainsForbiddenCityName(cityRoot.transform);

        bool graphicsEvidenceAvailable =
            SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null;
        int visualCaptureCount = CaptureVisualEvidence(cityRoot);
        bool visualEvidenceVerified = graphicsEvidenceAvailable &&
            visualCaptureCount == 4;
        bool frameBudgetVerified = graphicsEvidenceAvailable &&
            frameSampleCount >= 30 && maxFrameTimeMs <= MaxAcceptedFrameMs;
        bool configurationPassed = outdoorExists &&
            cityRootExists &&
            expectedPlacements &&
            readyPlacements &&
            noLoadFailures &&
            noHorizontalOverlaps &&
            cityChildCount &&
            allSides &&
            allCorners &&
            cityHasGeometry &&
            cityHasNoColliders &&
            cityGroundInfill &&
            texturedFacadeMaterials &&
            placementsOutsideEnvelope &&
            lockerRoomClear &&
            firstPersonHandsUseStableMaterial &&
            legacyExternalAbsent &&
            protectedGameplayObjectsPresent &&
            requiredCityMaterials &&
            cheapBackgroundProfile &&
            noForbiddenDressing;

        if (configurationPassed)
        {
            Debug.Log(
                $"GYMCHAOS_CITY_DYSTOPIA_CONFIGURATION_OK " +
                $"cheapBackground={Bool(cheapBackgroundProfile)} " +
                $"cityRenderers={GymCityDystopiaSurroundings.CityRendererCount} " +
                $"cityColliders={GymCityDystopiaSurroundings.CityColliderCount}");
        }

        // Configuration can be checked in headless mode, but visual captures
        // and frame pacing cannot. Keep those as explicit gates so a NullGfx
        // run never claims a visual/stutter pass from structural evidence.
        bool passed = configurationPassed && visualEvidenceVerified &&
            frameBudgetVerified;

        details =
            $"outdoor={Bool(outdoorExists)} cityRoot={Bool(cityRootExists)} " +
            $"expected8={Bool(expectedPlacements)} ready8={Bool(readyPlacements)} " +
            $"loadFailures={GymCityDystopiaSurroundings.LoadFailures} " +
            $"horizontalOverlaps={GymCityDystopiaSurroundings.HorizontalOverlaps} " +
            $"children8={Bool(cityChildCount)} sides4={Bool(allSides)} " +
            $"corners4={Bool(allCorners)} ground4={Bool(cityGroundInfill)} " +
            $"geometry={Bool(cityHasGeometry)} unifiedChromeGlass={Bool(texturedFacadeMaterials)} " +
            $"cityColliders={Bool(!cityHasNoColliders)} " +
            $"placementOutside={Bool(placementsOutsideEnvelope)} " +
            $"lockerClear={Bool(lockerRoomClear)} " +
            $"handsStable={Bool(firstPersonHandsUseStableMaterial)} " +
            $"legacyExternal={CountLegacyExternalObjects(outdoor != null ? outdoor.transform : null)} " +
            $"visualCaptures={visualCaptureCount} " +
            $"gameplayObjects={Bool(protectedGameplayObjectsPresent)} " +
            $"materials={Bool(requiredCityMaterials)} " +
            $"cheapBackground={Bool(cheapBackgroundProfile)} " +
            $"cityRenderers={GymCityDystopiaSurroundings.CityRendererCount} " +
            $"cheapRenderers={GymCityDystopiaSurroundings.CheapRendererCount} " +
            $"cityColliders={GymCityDystopiaSurroundings.CityColliderCount} " +
            $"frameSamples={frameSampleCount} maxFrameMs={maxFrameTimeMs:0.0} " +
            $"configuration={Bool(configurationPassed)} " +
            $"graphics={SystemInfo.graphicsDeviceType} " +
            $"visualEvidence={Bool(visualEvidenceVerified)} " +
            $"frameBudget={Bool(frameBudgetVerified)} " +
            $"forbiddenDressing={Bool(!noForbiddenDressing)}";
        return passed;
    }

    private static bool VerifyCheapBackgroundRenderers(Transform cityRoot)
    {
        string[] buildingNames =
        {
            "City Dystopia North", "City Dystopia South",
            "City Dystopia East", "City Dystopia West",
            "City Dystopia Corner SW", "City Dystopia Corner SE",
            "City Dystopia Corner NW", "City Dystopia Corner NE"
        };
        int rendererCount = 0;
        for (int buildingIndex = 0; buildingIndex < buildingNames.Length;
            buildingIndex++)
        {
            Transform building = FindChildRecursive(cityRoot, buildingNames[buildingIndex]);
            if (building == null)
            {
                return false;
            }
            MeshRenderer[] renderers =
                building.GetComponentsInChildren<MeshRenderer>(true);
            for (int rendererIndex = 0; rendererIndex < renderers.Length;
                rendererIndex++)
            {
                MeshRenderer renderer = renderers[rendererIndex];
                Material material = renderer.sharedMaterial;
                if (material == null || material.shader == null ||
                    material.shader.name.IndexOf("Unlit", StringComparison.OrdinalIgnoreCase) < 0 ||
                    renderer.shadowCastingMode != UnityEngine.Rendering.ShadowCastingMode.Off ||
                    renderer.receiveShadows ||
                    renderer.lightProbeUsage != UnityEngine.Rendering.LightProbeUsage.Off ||
                    renderer.reflectionProbeUsage != UnityEngine.Rendering.ReflectionProbeUsage.Off)
                {
                    return false;
                }
                rendererCount++;
            }
        }
        return rendererCount == GymCityDystopiaSurroundings.CityRendererCount;
    }

    internal static bool IsFrameSamplingActive =>
        frameSamplingStarted && !verificationFinished;

    internal static void RecordUnityFrame(float unscaledDeltaSeconds)
    {
        if (!IsFrameSamplingActive ||
            float.IsNaN(unscaledDeltaSeconds) ||
            float.IsInfinity(unscaledDeltaSeconds) ||
            unscaledDeltaSeconds <= 0f)
        {
            return;
        }

        float frameMs = unscaledDeltaSeconds * 1000f;
        frameSampleCount++;
        maxFrameTimeMs = Mathf.Max(maxFrameTimeMs, frameMs);
        if (frameMs > MaxAcceptedFrameMs && frameSpikeCount < 8)
        {
            frameSpikeCount++;
            Debug.Log(
                $"GYMCHAOS_CITY_DYSTOPIA_FRAME_SPIKE sample={frameSampleCount} " +
                $"frameMs={frameMs:0.0} elapsed={Time.unscaledTime:0.00} " +
                $"graphics={SystemInfo.graphicsDeviceType} " +
                $"readyPlacements={GymCityDystopiaSurroundings.ReadyPlacements} " +
                $"cityRenderers={GymCityDystopiaSurroundings.CityRendererCount} " +
                $"gcBytes={GC.GetTotalMemory(false)}");
        }
    }

    private static int CaptureVisualEvidence(GameObject cityRoot)
    {
        if (cityRoot == null)
        {
            return 0;
        }

        if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
        {
            Debug.LogWarning(
                "GYMCHAOS_CITY_DYSTOPIA_VISUAL_CAPTURE_UNAVAILABLE " +
                "graphics=Null");
            return 0;
        }

        string projectRoot = Directory.GetParent(Application.dataPath).FullName;
        string workspaceRoot = Directory.GetParent(projectRoot).FullName;
        string evidenceDirectory = Path.Combine(
            workspaceRoot,
            "Assets",
            "CityRecreation",
            "evidence_unity");
        Directory.CreateDirectory(evidenceDirectory);

        GameObject probeObject = new GameObject("City Dystopia Visual Probe");
        Camera probe = probeObject.AddComponent<Camera>();
        probe.enabled = false;
        probe.fieldOfView = 62f;
        probe.nearClipPlane = 0.1f;
        probe.farClipPlane = 600f;
        probe.clearFlags = CameraClearFlags.SolidColor;
        probe.backgroundColor = new Color(0.003f, 0.012f, 0.018f, 1f);
        probe.allowHDR = true;

        RenderTexture target = new RenderTexture(
            960,
            540,
            24,
            RenderTextureFormat.ARGB32);
        target.Create();
        RenderTexture previousTarget = RenderTexture.active;
        Vector3 anchor = GymOutdoorBuilder.ParkingBounds.center + Vector3.up * 2.2f;
        string[] viewNames = { "north", "south", "east", "west" };
        string[] cityNames =
        {
            "City Dystopia North",
            "City Dystopia South",
            "City Dystopia East",
            "City Dystopia West"
        };
        int captured = 0;
        try
        {
            for (int viewIndex = 0; viewIndex < cityNames.Length; viewIndex++)
            {
                Transform citySide = cityRoot.transform.Find(cityNames[viewIndex]);
                if (citySide == null)
                {
                    continue;
                }

                Bounds targetBounds = CalculateRendererBounds(citySide);
                Vector3 targetPosition = targetBounds.center;
                targetPosition.y = Mathf.Max(
                    targetBounds.center.y - 8f,
                    GymOutdoorBuilder.ParkingBounds.center.y + 4f);
                probe.transform.SetPositionAndRotation(
                    anchor,
                    Quaternion.LookRotation(
                        targetPosition - anchor,
                        Vector3.up));
                probe.targetTexture = target;
                probe.Render();
                RenderTexture.active = target;
                Texture2D image = new Texture2D(
                    target.width,
                    target.height,
                    TextureFormat.RGBA32,
                    false);
                image.ReadPixels(
                    new Rect(0f, 0f, target.width, target.height),
                    0,
                    0);
                image.Apply(false, false);
                string outputPath = Path.Combine(
                    evidenceDirectory,
                    $"city_{viewNames[viewIndex]}.png");
                File.WriteAllBytes(outputPath, image.EncodeToPNG());
                UnityEngine.Object.Destroy(image);
                captured++;
            }
        }
        catch (Exception exception)
        {
            Debug.LogWarning(
                $"GYMCHAOS_CITY_DYSTOPIA_VISUAL_CAPTURE_WARNING {exception.Message}");
        }
        finally
        {
            RenderTexture.active = previousTarget;
            probe.targetTexture = null;
            UnityEngine.Object.Destroy(probeObject);
            UnityEngine.Object.Destroy(target);
        }

        Debug.Log(
            $"GYMCHAOS_CITY_DYSTOPIA_VISUAL_EVIDENCE captured={captured} " +
            $"directory={evidenceDirectory}");
        return captured;
    }

    private static bool VerifyPlacementsOutsideEnvelope(
        Transform cityRoot,
        Bounds protectedBounds)
    {
        return PlacementBeyond(cityRoot, "City Dystopia North",
                   protectedBounds, 0, 1) &&
            PlacementBeyond(cityRoot, "City Dystopia South",
                protectedBounds, 0, -1) &&
            PlacementBeyond(cityRoot, "City Dystopia East",
                protectedBounds, 1, 0) &&
            PlacementBeyond(cityRoot, "City Dystopia West",
                protectedBounds, -1, 0) &&
            PlacementBeyond(cityRoot, "City Dystopia Corner SW",
                protectedBounds, -1, -1) &&
            PlacementBeyond(cityRoot, "City Dystopia Corner SE",
                protectedBounds, 1, -1) &&
            PlacementBeyond(cityRoot, "City Dystopia Corner NW",
                protectedBounds, -1, 1) &&
            PlacementBeyond(cityRoot, "City Dystopia Corner NE",
                protectedBounds, 1, 1);
    }

    private static bool PlacementBeyond(
        Transform cityRoot,
        string childName,
        Bounds protectedBounds,
        int xDirection,
        int zDirection)
    {
        Transform child = cityRoot.Find(childName);
        if (child == null)
        {
            return false;
        }

        Bounds cityBounds = CalculateRendererBounds(child);
        if (xDirection < 0 && cityBounds.max.x > protectedBounds.min.x)
        {
            return false;
        }

        if (xDirection > 0 && cityBounds.min.x < protectedBounds.max.x)
        {
            return false;
        }

        if (zDirection < 0 && cityBounds.max.z > protectedBounds.min.z)
        {
            return false;
        }

        if (zDirection > 0 && cityBounds.min.z < protectedBounds.max.z)
        {
            return false;
        }

        return xDirection != 0 || zDirection != 0;
    }

    private static bool VerifyCityRingClearOfBackRoom(Transform cityRoot)
    {
        GameObject backRoom = GameObject.Find("Gym Back Area (Runtime)");
        if (backRoom == null)
        {
            return false;
        }

        Renderer[] roomRenderers = backRoom.GetComponentsInChildren<Renderer>(true);
        if (roomRenderers.Length == 0)
        {
            return false;
        }

        Bounds roomBounds = roomRenderers[0].bounds;
        for (int index = 1; index < roomRenderers.Length; index++)
        {
            roomBounds.Encapsulate(roomRenderers[index].bounds);
        }

        string[] cityChildren =
        {
            "City Dystopia North",
            "City Dystopia South",
            "City Dystopia East",
            "City Dystopia West",
            "City Dystopia Corner SW",
            "City Dystopia Corner SE",
            "City Dystopia Corner NW",
            "City Dystopia Corner NE",
            "City Dystopia Ground North",
            "City Dystopia Ground South",
            "City Dystopia Ground East",
            "City Dystopia Ground West"
        };
        for (int index = 0; index < cityChildren.Length; index++)
        {
            Transform child = cityRoot.Find(cityChildren[index]);
            if (child == null)
            {
                return false;
            }

            Bounds childBounds = CalculateRendererBounds(child);
            if (childBounds.min.x < roomBounds.max.x &&
                childBounds.max.x > roomBounds.min.x &&
                childBounds.min.z < roomBounds.max.z &&
                childBounds.max.z > roomBounds.min.z)
            {
                return false;
            }
        }

        return true;
    }

    private static Bounds CalculateRendererBounds(Transform root)
    {
        MeshRenderer[] renderers =
            root.GetComponentsInChildren<MeshRenderer>(true);
        if (renderers.Length == 0)
        {
            return new Bounds(root.position, Vector3.zero);
        }

        Bounds bounds = renderers[0].bounds;
        for (int index = 1; index < renderers.Length; index++)
        {
            bounds.Encapsulate(renderers[index].bounds);
        }

        return bounds;
    }

    private static bool HasRequiredMaterials(GameObject cityRoot)
    {
        HashSet<string> required = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "MAT_City_Chrome_Glass Facade",
            "MAT_City_Chrome_Glass Body",
            "MAT_City_Chrome_Glass Window",
            "MAT_Red_Spine_Emission"
        };
        MeshRenderer[] renderers =
            cityRoot.GetComponentsInChildren<MeshRenderer>(true);
        for (int rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
        {
            Material[] materials = renderers[rendererIndex].sharedMaterials;
            for (int materialIndex = 0; materialIndex < materials.Length; materialIndex++)
            {
                Material material = materials[materialIndex];
                if (material == null)
                {
                    continue;
                }
                string materialName = material.name;
                const string cheapSuffix = " (Cheap Background)";
                if (materialName.EndsWith(cheapSuffix,
                    StringComparison.OrdinalIgnoreCase))
                {
                    materialName = materialName.Substring(
                        0, materialName.Length - cheapSuffix.Length);
                }
                required.Remove(materialName);
            }
        }

        return required.Count == 0;
    }

    // Towers must read as one colour family: every tower renderer except the
    // sign/spine accents uses the shared chrome/glass shader through three
    // shared materials (textured facade, trim, window glass) with one steel
    // tint; only the window glass is strongly reflective, the body is dark.
    private static bool HasTexturedFacadeMaterials(GameObject cityRoot)
    {
        HashSet<Material> chrome = new HashSet<Material>();
        bool facadeTextured = false;
        MeshRenderer[] renderers = cityRoot.GetComponentsInChildren<MeshRenderer>(true);
        for (int index = 0; index < renderers.Length; index++)
        {
            if (renderers[index].name.StartsWith("City Dystopia Ground", StringComparison.Ordinal))
            {
                continue;
            }
            Material[] materials = renderers[index].sharedMaterials;
            for (int materialIndex = 0; materialIndex < materials.Length; materialIndex++)
            {
                Material material = materials[materialIndex];
                if (material == null || material.shader == null)
                {
                    continue;
                }
                if (material.name.StartsWith("MAT_Facade_Procedural_", StringComparison.OrdinalIgnoreCase) ||
                    material.name.StartsWith("MAT_Roof_", StringComparison.OrdinalIgnoreCase) ||
                    material.name.StartsWith("MAT_Structural_", StringComparison.OrdinalIgnoreCase) ||
                    material.name.StartsWith("MAT_Window_", StringComparison.OrdinalIgnoreCase) ||
                    material.name.StartsWith("MAT_Reflection_Streak_", StringComparison.OrdinalIgnoreCase))
                {
                    // A per-source tower colour leaked past the shared material.
                    return false;
                }
                if (material.shader.name == "GymChaos/Unlit Chrome Glass")
                {
                    chrome.Add(material);
                    facadeTextured |= material.HasProperty("_BaseMap") && material.GetTexture("_BaseMap") != null;
                }
            }
        }

        // Only windows reflect: plain window panes and the window-gridded
        // curtain walls; the body (facades, frames, metal, roofs) is one
        // dark, non-reflective colour without a facade pattern.
        if (chrome.Count != 3 || facadeTextured)
        {
            return false;
        }
        float windowReflectivity = -1f;
        float curtainReflectivity = -1f;
        float bodyReflectivity = -1f;
        float bodyGlint = 0f;
        foreach (Material material in chrome)
        {
            float reflectivity = material.GetFloat("_Reflectivity");
            bool grid = material.GetFloat("_WindowGrid") > 0.5f;
            if (material.name.IndexOf(" Window", StringComparison.Ordinal) >= 0 && !grid)
                windowReflectivity = reflectivity;
            else if (material.name.IndexOf(" Facade", StringComparison.Ordinal) >= 0 && grid)
                curtainReflectivity = reflectivity;
            else if (material.name.IndexOf(" Body", StringComparison.Ordinal) >= 0 && !grid)
            {
                bodyReflectivity = reflectivity;
                bodyGlint = material.GetFloat("_SunGlint");
            }
        }
        Debug.Log(
            $"GYMCHAOS_TOWER_MATERIAL_ROLES window={windowReflectivity:F2} " +
            $"curtain={curtainReflectivity:F2} body={bodyReflectivity:F2} bodyGlint={bodyGlint:F2}");
        if (windowReflectivity < 0.8f || curtainReflectivity < 0.8f ||
            bodyReflectivity < 0f || bodyReflectivity > 0.01f || bodyGlint > 0.01f)
        {
            return false;
        }
        Debug.Log("GYMCHAOS_TOWER_WINDOWS_ONLY_OK");
        // The authored frame squares drew small dark tiles over the shader
        // window panes; they must be dropped at load.
        if (RuntimeGlbSceneLoader.SkippedTowerWindowFramePartsForVerification <= 0)
        {
            Debug.LogError("GYMCHAOS_TOWER_FRAME_SQUARES_FAILED skipped=0");
            return false;
        }
        Debug.Log(
            "GYMCHAOS_TOWER_FRAME_SQUARES_REMOVED_OK skipped=" +
            RuntimeGlbSceneLoader.SkippedTowerWindowFramePartsForVerification);
        Color? tint = null;
        foreach (Material material in chrome)
        {
            Color color = material.GetColor("_BaseColor");
            if (tint.HasValue && ((Vector4)(tint.Value - color)).sqrMagnitude > 1e-6f)
            {
                return false;
            }
            tint = color;
        }
        return true;
    }

    private static bool HasStableFirstPersonMaterials()
    {
        PlayerHandRig rig =
            UnityEngine.Object.FindFirstObjectByType<PlayerHandRig>();
        if (rig == null)
        {
            return false;
        }

        SkinnedMeshRenderer[] renderers =
            rig.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        int firstPersonRendererCount = 0;
        for (int index = 0; index < renderers.Length; index++)
        {
            SkinnedMeshRenderer renderer = renderers[index];
            if (renderer == null ||
                renderer.gameObject.layer != PlanarGymMirror.FirstPersonPlayerLayer)
            {
                continue;
            }

            firstPersonRendererCount++;
            Material[] materials = renderer.sharedMaterials;
            for (int materialIndex = 0; materialIndex < materials.Length; materialIndex++)
            {
                Material material = materials[materialIndex];
                if (material == null || material.shader == null ||
                    material.shader.name.IndexOf("Unlit", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    return false;
                }
            }
        }

        return firstPersonRendererCount > 0;
    }

    private static int CountLegacyExternalObjects(Transform root)
    {
        if (root == null)
        {
            return -1;
        }

        string[] forbiddenFragments =
        {
            "neighbourhood",
            "outer nature",
            "distant background block",
            "perimeter tower",
            "perimeter house",
            "courtyard bench",
            "public pavement"
        };
        int count = 0;
        Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
        for (int index = 0; index < transforms.Length; index++)
        {
            string lower = transforms[index].name.ToLowerInvariant();
            for (int fragmentIndex = 0; fragmentIndex < forbiddenFragments.Length; fragmentIndex++)
            {
                if (lower.Contains(forbiddenFragments[fragmentIndex]))
                {
                    count++;
                    break;
                }
            }
        }

        return count;
    }

    private static bool ContainsForbiddenCityName(Transform root)
    {
        Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
        for (int index = 0; index < transforms.Length; index++)
        {
            string lower = transforms[index].name.ToLowerInvariant();
            if (lower.Contains("railing") || lower.Contains("triangle"))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasChild(Transform root, string name)
    {
        return FindChildRecursive(root, name) != null;
    }

    private static Transform FindChildRecursive(Transform root, string name)
    {
        if (root == null)
        {
            return null;
        }
        Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
        for (int index = 0; index < transforms.Length; index++)
        {
            if (transforms[index] != null && transforms[index].name == name)
            {
                return transforms[index];
            }
        }
        return null;
    }

    private static string Bool(bool value)
    {
        return value ? "1" : "0";
    }

    private static void Finish(bool passed, string details)
    {
        verificationFinished = true;
        verificationFailed = !passed;
        Debug.Log(
            $"GYMCHAOS_CITY_DYSTOPIA_VERIFICATION_{(passed ? "OK" : "FAIL")} {details}");
        EditorApplication.isPlaying = false;
    }
}

[DisallowMultipleComponent]
internal sealed class GymCityDystopiaFrameSampler : MonoBehaviour
{
    private void Update()
    {
        GymChaosCityDystopiaVerifier.RecordUnityFrame(Time.unscaledDeltaTime);
    }
}
