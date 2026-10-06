using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

[InitializeOnLoad]
public static class GymChaosArtFidelityVerifier
{
    private const string RequestedKey = "GymChaos.ArtFidelityVerificationRequested";
    private const int CaptureWidth = 960;
    private const int CaptureHeight = 540;
    private const int ProductCaptureLayer = 30;
    private static readonly string EvidenceFolder = "Assets/VisualFidelity/evidence_unity";
    private static double startedAt;
    private static bool completed;
    private static bool initialized;
    private static Camera captureCamera;
    private static GameObject captureLight;

    private sealed class ProductFamily
    {
        public string Name;
        public string[] Prefixes;
        public string LabelToken;
        public string[] FormTokens;

        public ProductFamily(string name, string labelToken, string[] prefixes, string[] formTokens)
        {
            Name = name;
            LabelToken = labelToken;
            Prefixes = prefixes;
            FormTokens = formTokens;
        }
    }

    private sealed class SurfaceContract
    {
        public string Key;
        public string ObjectName;
        public string MaterialName;

        public SurfaceContract(string key, string objectName, string materialName)
        {
            Key = key;
            ObjectName = objectName;
            MaterialName = materialName;
        }
    }

    private sealed class CaptureMetrics
    {
        public int VisiblePixels;
        public float LuminanceMin;
        public float LuminanceMax;
        public float Variance;
        public float CenterVariance;
        public int ColorfulPixels;
        public int EdgePixels;
        public int CenterEdgePixels;
        public int MarkerPixels;
        public int CenterMarkerPixels;
        public bool VisuallyUseful;
        public float Score;
    }

    private sealed class ProductViewCandidate
    {
        public Renderer Renderer;
        public Vector3 Direction;
        public CaptureMetrics Metrics;
    }

    private static readonly SurfaceContract[] RequiredSurfaces =
    {
        new SurfaceContract("gymRubber", "Rubber Floor", "Dark navy gym rubber floor"),
        new SurfaceContract("lockerRubber", "Locker Room Floor", "Locker room rubber floor"),
        new SurfaceContract("bathroomTile", "Bathroom divider south", "Bathroom tile"),
        // Exterior ground is one continuous slab: every named outdoor surface
        // shares the parking asphalt material (GymExteriorGroundUnifier); the
        // landscape strip behind the parking wall is planting grass.
        new SurfaceContract("courtyard", "Exterior Courtyard Foundation", "Outdoor parking asphalt"),
        new SurfaceContract("asphalt", "Mini Parking Lot", "Outdoor parking asphalt"),
        new SurfaceContract("concretePath", "Path from Gym Door", "Outdoor parking asphalt"),
        new SurfaceContract("landscape", "Parking Park Landscape", "Outdoor park ground"),
    };

    private static readonly ProductFamily[] ProductFamilies =
    {
        new ProductFamily("whey", "WHEY", new[] { "Protein_Right" },
            new[] { "PouchBody", "ProteinTubBody" }),
        new ProductFamily("creatine", "CREATINE", new[] { "Creatine_Back" },
            new[] { "PouchBody" }),
        new ProductFamily("pre_workout", "PRE_WORKOUT", new[] { "PreWorkout_Left" },
            new[] { "PreWorkoutBody" }),
        new ProductFamily("shaker", "SHAKER", new[] { "Shakers_Left" },
            new[] { "BottleBody", "ShakerFlipTop" }),
        new ProductFamily("protein_bar", "PROTEIN_BAR", new[] { "ProteinBars" },
            new[] { "BarBody" }),
        new ProductFamily("energy", "ENERGY",
            new[] { "EnergyDrinks_Cooler", "ColdFridge_RearLeft_Can" },
            new[] { "EnergyCanBody" }),
        new ProductFamily("rtd_shake", "RTD_SHAKE", new[] { "ColdFridge_RearLeft_Rtd" },
            new[] { "RtdBody", "RtdShoulder" }),
    };

    static GymChaosArtFidelityVerifier()
    {
        if (!SessionState.GetBool(RequestedKey, false)) return;
        Hook();
        EditorApplication.delayCall += ResumeAfterReload;
    }

    public static void Run()
    {
        ResetState();
        SessionState.SetBool(RequestedKey, true);
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        Hook();
        EditorApplication.isPlaying = true;
    }

    private static void Hook()
    {
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
        EditorApplication.playModeStateChanged -= PlayModeChanged;
        EditorApplication.playModeStateChanged += PlayModeChanged;
    }

    private static void ResumeAfterReload()
    {
        if (EditorApplication.isPlaying) StartTimer();
    }

    private static void StartTimer()
    {
        startedAt = EditorApplication.timeSinceStartup;
        Time.timeScale = 1f;
        AudioListener.pause = true;
    }

    private static void ResetState()
    {
        startedAt = 0d;
        completed = false;
        initialized = false;
        DestroyCaptureRig(true);
    }

    private static void PlayModeChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            StartTimer();
            return;
        }
        if (state != PlayModeStateChange.EnteredEditMode) return;
        EditorApplication.update -= Tick;
        EditorApplication.playModeStateChanged -= PlayModeChanged;
        DestroyCaptureRig(true);
        bool wasRequested = SessionState.GetBool(RequestedKey, false);
        SessionState.EraseBool(RequestedKey);
        if (Application.isBatchMode && wasRequested)
            EditorApplication.Exit(completed ? 0 : 1);
    }

    private static void Tick()
    {
        if (!EditorApplication.isPlaying) return;
        Time.timeScale = 1f;
        AudioListener.pause = true;
        if (startedAt <= 0d) StartTimer();
        double elapsed = EditorApplication.timeSinceStartup - startedAt;
        if (elapsed < 2d || initialized) return;

        try
        {
            if (SystemInfo.graphicsDeviceType != GraphicsDeviceType.Direct3D12)
                throw new InvalidOperationException(
                    $"Direct3D12 required for visual evidence, got {SystemInfo.graphicsDeviceType}.");

            if (!GymOutdoorBuilder.IsBuilt || !GymBackRoomBuilder.TryGetRoomBounds(out _) ||
                !GymInteriorBuilder.TryGetMainGymBounds(out _) ||
                !GymProteinStoreEnvironment.IsLoaded ||
                GymProteinStoreEnvironment.RuntimeRoot == null)
            {
                if (elapsed > 90d)
                    throw new TimeoutException("Scene art builders did not settle before the visual gate timeout.");
                return;
            }

            string surfaceDetails = ValidateSurfaces();
            string productDetails = ValidateRuntimeProductFamilies(
                GymProteinStoreEnvironment.RuntimeRoot);
            if (GymProteinStoreEnvironment.TransparentFridgePanelCount < 2)
                throw new InvalidOperationException("Runtime store does not expose both transparent fridge panels.");

            CreateCaptureRig();
            List<string> captures = CaptureRequiredViews();
            if (captures.Count < 20)
                throw new InvalidOperationException(
                    $"Expected at least 20 direct art captures, got {captures.Count}.");

            initialized = true;
            completed = true;
            Debug.Log(
                "GYMCHAOS_ART_FIDELITY_OK " +
                "graphics=Direct3D12 " +
                $"surfaces=7 storeExterior=1 storeInterior=1 productFamilies=7 " +
                $"packageLabels={productDetails} " +
                $"captures={captures.Count} " +
                $"paths={string.Join(",", captures)} " +
                $"surfaceDetails={surfaceDetails} " +
                $"renderers={GymProteinStoreEnvironment.LoadedRendererCount} " +
                $"materials={GymProteinStoreEnvironment.LoadedMaterialCount}");
            EditorApplication.isPlaying = false;
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            Debug.LogError("GYMCHAOS_ART_FIDELITY_FAIL " + exception.Message);
            SessionState.EraseBool(RequestedKey);
            EditorApplication.isPlaying = false;
        }
    }

    private static string ValidateSurfaces()
    {
        List<string> details = new List<string>();
        for (int index = 0; index < RequiredSurfaces.Length; index++)
        {
            SurfaceContract contract = RequiredSurfaces[index];
            Renderer renderer = FindRenderer(contract.ObjectName);
            if (renderer == null || !renderer.enabled)
                throw new InvalidOperationException(
                    $"Surface renderer missing or disabled: {contract.ObjectName}");
            Material material = renderer.sharedMaterial;
            if (material == null || !material.name.StartsWith(contract.MaterialName,
                    StringComparison.Ordinal))
                throw new InvalidOperationException(
                    $"Surface material mismatch: {contract.ObjectName} -> " +
                    $"{material?.name ?? "null"}, expected {contract.MaterialName}");
            Texture2D texture = material.mainTexture as Texture2D;
            if (texture == null || texture.width < 16 || texture.height < 16)
                throw new InvalidOperationException(
                    $"Surface texture missing or too small: {contract.ObjectName}");
            if (material.mainTextureScale.x <= 0f || material.mainTextureScale.y <= 0f)
                throw new InvalidOperationException(
                    $"Surface tiling is invalid: {contract.ObjectName}");
            int variedPixels = texture.isReadable ? CountTextureVariation(texture) : -1;
            if (texture.isReadable && variedPixels < 8)
                throw new InvalidOperationException(
                    $"Surface texture has no measurable pattern: {contract.ObjectName}");
            details.Add(
                $"{contract.Key}={material.name}/tex{texture.width}x{texture.height}/" +
                (texture.isReadable ? $"var{variedPixels}" : "gpuTexture=1"));
        }
        return string.Join(";", details);
    }

    private static int CountTextureVariation(Texture2D texture)
    {
        int varied = 0;
        Color first = texture.GetPixel(0, 0);
        for (int y = 0; y < texture.height; y++)
        {
            for (int x = 0; x < texture.width; x++)
            {
                Color pixel = texture.GetPixel(x, y);
                if (Mathf.Abs(pixel.r - first.r) + Mathf.Abs(pixel.g - first.g) +
                    Mathf.Abs(pixel.b - first.b) > 0.012f)
                    varied++;
            }
        }
        return varied;
    }

    private static string ValidateRuntimeProductFamilies(Transform root)
    {
        if (root == null) throw new InvalidOperationException("ProteinStore runtime root is null.");
        Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
        List<string> names = transforms.Select(transform => transform.name).ToList();
        List<string> details = new List<string>();
        for (int index = 0; index < ProductFamilies.Length; index++)
        {
            ProductFamily family = ProductFamilies[index];
            List<string> familyNames = names.Where(name =>
                family.Prefixes.Any(prefix => name.StartsWith(prefix, StringComparison.Ordinal))).ToList();
            List<string> labels = familyNames.Where(name =>
                name.IndexOf("_LabelPanel_PROTEINI_SI_", StringComparison.Ordinal) >= 0 &&
                name.IndexOf(family.LabelToken, StringComparison.Ordinal) >= 0).ToList();
            bool form = familyNames.Any(name => family.FormTokens.Any(
                token => name.Contains(token, StringComparison.Ordinal)));
            if (familyNames.Count == 0 || labels.Count == 0 || !form)
            {
                string sample = string.Join("|", familyNames
                    .Where(name => name.IndexOf("Label", StringComparison.OrdinalIgnoreCase) >= 0)
                    .Take(4));
                throw new InvalidOperationException(
                    $"Product family contract failed: {family.Name} " +
                    $"geometry={familyNames.Count} labels={labels.Count} form={form} sample={sample}");
            }
            details.Add($"{family.Name}:{labels.Count}");
        }
        return string.Join(",", details);
    }

    private static void CreateCaptureRig()
    {
        DestroyCaptureRig(true);
        GameObject cameraObject = new GameObject("GymChaos Art Fidelity Capture Camera");
        captureCamera = cameraObject.AddComponent<Camera>();
        captureCamera.enabled = false;
        captureCamera.fieldOfView = 52f;
        captureCamera.nearClipPlane = 0.05f;
        captureCamera.farClipPlane = 500f;
        captureCamera.clearFlags = CameraClearFlags.SolidColor;
        captureCamera.backgroundColor = new Color(0.012f, 0.018f, 0.032f, 1f);

        captureLight = new GameObject("GymChaos Art Fidelity Fill Light");
        Light light = captureLight.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 0.8f;
        light.color = new Color(1f, 0.92f, 0.82f);
        light.shadows = LightShadows.None;
        captureLight.transform.rotation = Quaternion.Euler(48f, -35f, 0f);
    }

    private static List<string> CaptureRequiredViews()
    {
        string projectRoot = Directory.GetParent(Application.dataPath).FullName;
        string absoluteFolder = Path.Combine(projectRoot, EvidenceFolder.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(absoluteFolder);
        List<string> captures = new List<string>();

        Bounds gymBounds;
        Bounds lockerBounds;
        if (!GymInteriorBuilder.TryGetMainGymBounds(out gymBounds) ||
            !GymBackRoomBuilder.TryGetRoomBounds(out lockerBounds))
            throw new InvalidOperationException("Gym or locker bounds disappeared before capture.");

        CaptureBounds("gym-floor", gymBounds,
            gymBounds.center + new Vector3(-gymBounds.extents.x * 0.62f,
                gymBounds.extents.y * 0.65f, -gymBounds.extents.z * 0.62f),
            gymBounds.center + Vector3.up * 0.45f, absoluteFolder, captures);
        CaptureBounds("locker-bathroom", lockerBounds,
            lockerBounds.center + new Vector3(-lockerBounds.extents.x * 0.62f,
                lockerBounds.extents.y * 0.72f, -lockerBounds.extents.z * 0.72f),
            lockerBounds.center + Vector3.up * 0.55f, absoluteFolder, captures);
        Renderer bathroomTile = FindRenderer("Bathroom divider south");
        if (bathroomTile == null)
            throw new InvalidOperationException("Bathroom tile renderer disappeared before capture.");
        CaptureBounds("bathroom-tile", bathroomTile.bounds,
            bathroomTile.bounds.center + new Vector3(4.8f, 2.4f, -4.8f),
            bathroomTile.bounds.center, absoluteFolder, captures, fieldOfView: 48f);

        Bounds parking = GymOutdoorBuilder.ParkingBounds;
        CaptureBounds("outdoor-surfaces", parking,
            parking.center + new Vector3(-parking.extents.x * 0.82f,
                Mathf.Max(8f, parking.extents.x * 0.40f), -parking.extents.z * 0.82f),
            parking.center + Vector3.up * 0.1f, absoluteFolder, captures);
        Renderer outdoorPath = FindRenderer("Path from Gym Door");
        if (outdoorPath == null)
            throw new InvalidOperationException("Outdoor concrete path renderer disappeared before capture.");
        CaptureBounds("outdoor-path", outdoorPath.bounds,
            outdoorPath.bounds.center + new Vector3(8f, 8f, -8f),
            outdoorPath.bounds.center, absoluteFolder, captures, fieldOfView: 46f);

        Renderer courtyard = FindRenderer("Exterior Courtyard Foundation");
        Renderer asphalt = FindRenderer("Mini Parking Lot");
        Renderer landscape = FindRenderer("Parking Park Landscape");
        if (courtyard == null || asphalt == null || landscape == null)
            throw new InvalidOperationException(
                "One or more outdoor surface renderers disappeared before closeup capture.");
        Renderer courtyardInset = FindRenderer("Courtyard Visual Inset") ?? courtyard;
        CaptureBounds("outdoor-courtyard", courtyardInset.bounds,
            courtyardInset.bounds.center + new Vector3(7f, 7f, -7f),
            courtyardInset.bounds.center + Vector3.up * 0.08f,
            absoluteFolder, captures, fieldOfView: 48f);
        CaptureBounds("outdoor-asphalt", asphalt.bounds,
            asphalt.bounds.center + new Vector3(-8f, 8f, -8f),
            asphalt.bounds.center + Vector3.up * 0.08f,
            absoluteFolder, captures, fieldOfView: 50f);
        CaptureBounds("outdoor-landscape", landscape.bounds,
            landscape.bounds.center + new Vector3(8f, 8f, 8f),
            landscape.bounds.center + Vector3.up * 0.08f,
            absoluteFolder, captures, fieldOfView: 50f);

        Bounds store = GymProteinStoreEnvironment.StoreBounds;
        Vector3 frontDirection = Vector3.ProjectOnPlane(
            GymProteinStoreEnvironment.StoreFrontClearPoint - store.center, Vector3.up);
        if (frontDirection.sqrMagnitude < 0.01f) frontDirection = Vector3.back;
        frontDirection.Normalize();
        Vector3 exteriorPosition = GymProteinStoreEnvironment.StoreFrontClearPoint +
            frontDirection * 8.0f +
            Vector3.up * Mathf.Max(6.5f, store.extents.y * 1.05f);
        CaptureBounds("protein-store-exterior", store, exteriorPosition,
            store.center + Vector3.up * store.extents.y * 0.46f,
            absoluteFolder, captures, fieldOfView: 58f);
        CaptureBounds("protein-store-interior", store,
            store.center + frontDirection * 0.35f + Vector3.up * store.extents.y * 0.52f,
            store.center + Vector3.up * store.extents.y * 0.35f,
            absoluteFolder, captures);
        Bounds fridgeBounds;
        if (!TryFindAggregateBounds(GymProteinStoreEnvironment.RuntimeRoot,
                new[] { "EnergyCooler_FrontRight", "ColdFridge_Extra" }, out fridgeBounds))
            throw new InvalidOperationException("Runtime fridge bounds disappeared before capture.");
        CaptureBounds("protein-store-fridges", fridgeBounds,
            fridgeBounds.center + frontDirection * 3.4f + Vector3.up * 1.2f,
            fridgeBounds.center + Vector3.up * 0.15f,
            absoluteFolder, captures, fieldOfView: 46f);
        Bounds frontRightFridge;
        Bounds extraFridge;
        if (!TryFindAggregateBounds(GymProteinStoreEnvironment.RuntimeRoot,
                new[] { "EnergyCooler_FrontRight" }, out frontRightFridge) ||
            !TryFindAggregateBounds(GymProteinStoreEnvironment.RuntimeRoot,
                new[] { "ColdFridge_Extra" }, out extraFridge))
            throw new InvalidOperationException(
                "Both transparent fridge bounds disappeared before closeup capture.");
        Renderer frontRightGlass = FindRenderer("EnergyCooler_FrontRight_Glass");
        Renderer extraGlass = FindRenderer("ColdFridge_Extra_Glass");
        Vector3 frontRightDirection = FridgeFrontDirection(frontRightFridge, frontRightGlass);
        Vector3 extraDirection = FridgeFrontDirection(extraFridge, extraGlass);
        CaptureFridgeCloseup(
            "protein-store-fridge-front-right", frontRightFridge, frontRightGlass,
            frontRightDirection, absoluteFolder, captures);
        CaptureFridgeCloseup(
            "protein-store-fridge-extra", extraFridge, extraGlass,
            extraDirection, absoluteFolder, captures);

        for (int index = 0; index < ProductFamilies.Length; index++)
        {
            ProductFamily family = ProductFamilies[index];
            if (!CaptureProductFamily(family, GymProteinStoreEnvironment.RuntimeRoot,
                    absoluteFolder, captures))
                throw new InvalidOperationException(
                    $"No direct visual product capture was usable for {family.Name}.");
        }

        return captures;
    }

    private static Vector3 FridgeFrontDirection(Bounds fridgeBounds, Renderer glass)
    {
        Vector3 direction = glass != null
            ? Vector3.ProjectOnPlane(glass.bounds.center - fridgeBounds.center, Vector3.up)
            : Vector3.zero;
        if (direction.sqrMagnitude < 0.01f)
            direction = Vector3.ProjectOnPlane(
                GymProteinStoreEnvironment.StoreFrontClearPoint - fridgeBounds.center,
                Vector3.up);
        return direction.sqrMagnitude >= 0.01f ? direction.normalized : Vector3.back;
    }

    private static void CaptureFridgeCloseup(
        string label,
        Bounds fridgeBounds,
        Renderer glass,
        Vector3 outwardDirection,
        string absoluteFolder,
        List<string> captures)
    {
        Vector3 front = glass != null ? glass.bounds.center : fridgeBounds.center;
        Vector3 cameraPosition = front + outwardDirection * 3.1f + Vector3.up * 0.42f;
        Vector3 target = front + Vector3.up * 0.16f;
        string[] prefixes = label.EndsWith("front-right", StringComparison.Ordinal)
            ? new[] { "EnergyCooler_FrontRight", "EnergyDrinks_Cooler" }
            : new[] { "ColdFridge_Extra", "ColdFridge_RearLeft" };
        Dictionary<GameObject, int> previousLayers = new Dictionary<GameObject, int>();
        Renderer[] targetRenderers = GymProteinStoreEnvironment.RuntimeRoot
            .GetComponentsInChildren<Renderer>(true)
            .Where(renderer => renderer != null && prefixes.Any(prefix =>
                renderer.name.StartsWith(prefix, StringComparison.Ordinal)))
            .ToArray();
        for (int index = 0; index < targetRenderers.Length; index++)
        {
            GameObject targetObject = targetRenderers[index].gameObject;
            if (!previousLayers.ContainsKey(targetObject))
                previousLayers.Add(targetObject, targetObject.layer);
            targetObject.layer = ProductCaptureLayer;
        }

        int previousCullingMask = captureCamera.cullingMask;
        captureCamera.cullingMask = 1 << ProductCaptureLayer;
        try
        {
            CaptureBounds(
                label, fridgeBounds, cameraPosition, target, absoluteFolder, captures,
                fieldOfView: 48f);
        }
        finally
        {
            foreach (KeyValuePair<GameObject, int> entry in previousLayers)
                if (entry.Key != null) entry.Key.layer = entry.Value;
            captureCamera.cullingMask = previousCullingMask;
        }
    }

    private static bool CaptureProductFamily(
        ProductFamily family,
        Transform root,
        string absoluteFolder,
        List<string> captures)
    {
        List<Renderer> renderers = FindFamilyLabelRenderers(family, root);
        Renderer labelRenderer = renderers.FirstOrDefault();
        if (labelRenderer == null)
        {
            Debug.LogError(
                $"GYMCHAOS_ART_PRODUCT_VIEW_FAIL family={family.Name} labels=0");
            return false;
        }

        List<Renderer> productRenderers = FindProductUnitRenderers(labelRenderer, root);
        if (productRenderers.Count == 0)
        {
            Debug.LogError(
                $"GYMCHAOS_ART_PRODUCT_VIEW_FAIL family={family.Name} " +
                $"label={labelRenderer.name} productUnit=0");
            return false;
        }

        Bounds productBounds = CalculateRendererBounds(productRenderers);
        int previousCullingMask = captureCamera.cullingMask;
        Dictionary<GameObject, int> previousLayers = new Dictionary<GameObject, int>();
        for (int index = 0; index < productRenderers.Count; index++)
        {
            GameObject productObject = productRenderers[index].gameObject;
            if (!previousLayers.ContainsKey(productObject))
                previousLayers.Add(productObject, productObject.layer);
            productObject.layer = ProductCaptureLayer;
        }

        captureCamera.cullingMask = 1 << ProductCaptureLayer;
        List<ProductViewCandidate> candidates = new List<ProductViewCandidate>();
        try
        {
            Material[] originalLabelMaterials = labelRenderer.sharedMaterials;
            Material markerMaterial = CreateProductMarkerMaterial();
            Material[] markerMaterials = new Material[Mathf.Max(1, originalLabelMaterials.Length)];
            for (int materialIndex = 0; materialIndex < markerMaterials.Length; materialIndex++)
                markerMaterials[materialIndex] = markerMaterial;
            try
            {
                labelRenderer.sharedMaterials = markerMaterials;
                List<Vector3> directions = GetProductViewDirections(labelRenderer);
                for (int directionIndex = 0; directionIndex < directions.Count; directionIndex++)
                {
                    Vector3 direction = directions[directionIndex];
                    Vector3 cameraPosition = ProductCameraPosition(productBounds, direction);
                    CaptureMetrics metrics = RenderPreview(
                        productBounds, cameraPosition, labelRenderer.bounds.center);
                    candidates.Add(new ProductViewCandidate
                    {
                        Renderer = labelRenderer,
                        Direction = direction,
                        Metrics = metrics,
                    });
                }
            }
            finally
            {
                labelRenderer.sharedMaterials = originalLabelMaterials;
                UnityEngine.Object.DestroyImmediate(markerMaterial);
            }

            ProductViewCandidate best = candidates
                .Where(candidate => candidate.Metrics.MarkerPixels > 20)
                .Where(candidate => candidate.Metrics.VisuallyUseful)
                .OrderByDescending(candidate => candidate.Metrics.Score)
                .FirstOrDefault();
            if (best == null)
            {
                best = candidates
                    .Take(Mathf.Min(2, candidates.Count))
                    .Where(candidate => candidate.Metrics.VisuallyUseful)
                    .OrderByDescending(candidate => candidate.Metrics.Score)
                    .FirstOrDefault();
            }
            if (best == null)
            {
                string bestDiagnostics = string.Join(" | ", candidates
                    .OrderByDescending(candidate => candidate.Metrics.Score)
                    .Take(3)
                    .Select(candidate =>
                        $"{candidate.Direction}:{candidate.Metrics.Score:0.000}"));
                Debug.LogError(
                    $"GYMCHAOS_ART_PRODUCT_VIEW_FAIL family={family.Name} " +
                    $"candidates={candidates.Count} top={bestDiagnostics}");
                return false;
            }

            ProductViewCandidate opposite = candidates
                .Where(candidate => Vector3.Dot(candidate.Direction, best.Direction) < -0.55f)
                .OrderByDescending(candidate => candidate.Metrics.Score)
                .FirstOrDefault();
            if (opposite == null)
            {
                opposite = candidates
                    .Where(candidate => Vector3.Dot(candidate.Direction, best.Direction) < 0.95f)
                    .OrderByDescending(candidate => candidate.Metrics.Score)
                    .FirstOrDefault();
            }
            if (opposite == null) opposite = best;

            CaptureBounds(
                "product-" + family.Name + "-normal",
                productBounds,
                ProductCameraPosition(productBounds, best.Direction),
                labelRenderer.bounds.center,
                absoluteFolder,
                captures,
                fieldOfView: 38f,
                allowEmpty: false);
            CaptureBounds(
                "product-" + family.Name + "-opposite",
                productBounds,
                ProductCameraPosition(productBounds, opposite.Direction),
                labelRenderer.bounds.center,
                absoluteFolder,
                captures,
                fieldOfView: 38f,
                allowEmpty: true);
            Debug.Log(
                $"GYMCHAOS_ART_PRODUCT_VIEW_OK family={family.Name} " +
                $"unit={labelRenderer.name} productRenderers={productRenderers.Count} " +
                $"labelForward={labelRenderer.transform.forward} " +
                $"normalDirection={best.Direction} score={best.Metrics.Score:0.000} " +
                $"markerPixels={best.Metrics.MarkerPixels} " +
                $"oppositeDirection={opposite.Direction} " +
                $"oppositeScore={opposite.Metrics.Score:0.000}");
            return true;
        }
        finally
        {
            foreach (KeyValuePair<GameObject, int> entry in previousLayers)
                if (entry.Key != null) entry.Key.layer = entry.Value;
            captureCamera.cullingMask = previousCullingMask;
        }
    }

    private static Material CreateProductMarkerMaterial()
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ??
            Shader.Find("Unlit/Color") ?? Shader.Find("Standard");
        if (shader == null)
            throw new InvalidOperationException("No shader available for product marker preview.");
        Material marker = new Material(shader)
        {
            name = "GymChaos Product Capture Marker",
        };
        Color markerColor = new Color(0.05f, 0.95f, 0.12f, 1f);
        if (marker.HasProperty("_BaseColor")) marker.SetColor("_BaseColor", markerColor);
        if (marker.HasProperty("_Color")) marker.SetColor("_Color", markerColor);
        return marker;
    }

    private static List<Renderer> FindProductUnitRenderers(Renderer labelRenderer, Transform root)
    {
        string labelMarker = "_LabelPanel_PROTEINI_SI_";
        int markerIndex = labelRenderer.name.IndexOf(labelMarker,
            StringComparison.Ordinal);
        if (markerIndex <= 0) return new List<Renderer>();
        string unitPrefix = labelRenderer.name.Substring(0, markerIndex);
        Renderer[] allRenderers = root.GetComponentsInChildren<Renderer>(true);
        return allRenderers.Where(renderer => renderer != null &&
                (renderer.name.Equals(unitPrefix, StringComparison.Ordinal) ||
                    renderer.name.StartsWith(unitPrefix + "_", StringComparison.Ordinal) ||
                    renderer.name.StartsWith(unitPrefix + " ", StringComparison.Ordinal)))
            .ToList();
    }

    private static Bounds CalculateRendererBounds(List<Renderer> renderers)
    {
        Bounds bounds = renderers[0].bounds;
        for (int index = 1; index < renderers.Count; index++)
            bounds.Encapsulate(renderers[index].bounds);
        return bounds;
    }

    private static List<Renderer> FindFamilyLabelRenderers(ProductFamily family, Transform root)
    {
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        List<Renderer> matches = renderers.Where(renderer => renderer != null &&
            renderer.name.IndexOf("_LabelPanel_PROTEINI_SI_", StringComparison.Ordinal) >= 0 &&
            renderer.name.IndexOf(family.LabelToken, StringComparison.Ordinal) >= 0 &&
            family.Prefixes.Any(prefix => renderer.name.StartsWith(prefix,
                StringComparison.Ordinal))).ToList();

        // Energy cans have a dedicated cooler. Prefer that front-facing family
        // before the legacy rear-fridge can fallback, otherwise a valid label
        // contract can still produce the wrong category in the direct capture.
        if (family.Name == "energy")
        {
            List<Renderer> cooler = matches.Where(renderer =>
                renderer.name.StartsWith("EnergyDrinks_Cooler", StringComparison.Ordinal))
                .ToList();
            if (cooler.Count > 0) matches = cooler;
        }

        return matches
            .OrderBy(renderer => Mathf.Abs(renderer.bounds.center.y -
                GymProteinStoreEnvironment.StoreBounds.center.y))
            .ThenBy(renderer => renderer.name, StringComparer.Ordinal)
            .Take(24)
            .ToList();
    }

    private static List<Vector3> GetProductViewDirections(Renderer renderer)
    {
        List<Vector3> directions = new List<Vector3>();
        AddProductViewDirection(directions,
            Vector3.ProjectOnPlane(renderer.transform.forward, Vector3.up));
        AddProductViewDirection(directions,
            Vector3.ProjectOnPlane(-renderer.transform.forward, Vector3.up));
        AddProductViewDirection(directions, Vector3.forward);
        AddProductViewDirection(directions, Vector3.back);
        AddProductViewDirection(directions, Vector3.right);
        AddProductViewDirection(directions, Vector3.left);
        return directions;
    }

    private static void AddProductViewDirection(List<Vector3> directions, Vector3 direction)
    {
        direction = Vector3.ProjectOnPlane(direction, Vector3.up);
        if (direction.sqrMagnitude < 0.01f) return;
        direction.Normalize();
        for (int index = 0; index < directions.Count; index++)
        {
            if (Vector3.Dot(directions[index], direction) > 0.98f) return;
        }
        directions.Add(direction);
    }

    private static Vector3 ProductCameraPosition(Bounds bounds, Vector3 direction)
    {
        float radius = Mathf.Max(bounds.extents.x, bounds.extents.y, bounds.extents.z);
        float distance = Mathf.Clamp(Mathf.Max(0.90f, radius * 3.8f), 0.90f, 2.7f);
        return bounds.center + direction.normalized * distance + Vector3.up * 0.12f;
    }

    private static CaptureMetrics RenderPreview(
        Bounds bounds, Vector3 cameraPosition, Vector3 target)
    {
        const int previewWidth = 320;
        const int previewHeight = 180;
        if (captureCamera == null) throw new InvalidOperationException("Capture camera missing.");
        captureCamera.fieldOfView = 38f;
        captureCamera.transform.SetPositionAndRotation(
            cameraPosition,
            Quaternion.LookRotation(target - cameraPosition, Vector3.up));
        RenderTexture targetTexture = new RenderTexture(
            previewWidth, previewHeight, 24, RenderTextureFormat.ARGB32);
        RenderTexture previousActive = RenderTexture.active;
        RenderTexture previousTarget = captureCamera.targetTexture;
        try
        {
            captureCamera.targetTexture = targetTexture;
            captureCamera.Render();
            RenderTexture.active = targetTexture;
            Texture2D image = new Texture2D(previewWidth, previewHeight,
                TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, previewWidth, previewHeight), 0, 0);
            image.Apply(false, false);
            CaptureMetrics metrics = AnalyzePixels(image.GetPixels32(), previewWidth,
                previewHeight);
            UnityEngine.Object.DestroyImmediate(image);
            return metrics;
        }
        finally
        {
            captureCamera.targetTexture = previousTarget;
            RenderTexture.active = previousActive;
            targetTexture.Release();
            UnityEngine.Object.DestroyImmediate(targetTexture);
        }
    }

    private static bool TryFindAggregateBounds(
        Transform root, string[] namePrefixes, out Bounds bounds)
    {
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        bool found = false;
        bounds = default;
        for (int index = 0; index < renderers.Length; index++)
        {
            Renderer renderer = renderers[index];
            if (renderer == null || !namePrefixes.Any(prefix =>
                    renderer.name.StartsWith(prefix, StringComparison.Ordinal)))
                continue;
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

    private static Renderer FindRenderer(string objectName)
    {
        Renderer[] renderers = UnityEngine.Object.FindObjectsByType<Renderer>(
            FindObjectsSortMode.None);
        for (int index = 0; index < renderers.Length; index++)
        {
            Renderer renderer = renderers[index];
            if (renderer != null && renderer.name == objectName)
                return renderer;
        }
        return null;
    }

    private static bool CaptureBounds(
        string label,
        Bounds bounds,
        Vector3 cameraPosition,
        Vector3 target,
        string absoluteFolder,
        List<string> captures,
        float fieldOfView = 52f,
        bool allowEmpty = false)
    {
        if (captureCamera == null) throw new InvalidOperationException("Capture camera missing.");
        captureCamera.fieldOfView = fieldOfView;
        captureCamera.transform.SetPositionAndRotation(
            cameraPosition,
            Quaternion.LookRotation(target - cameraPosition, Vector3.up));
        RenderTexture targetTexture = new RenderTexture(
            CaptureWidth, CaptureHeight, 24, RenderTextureFormat.ARGB32);
        RenderTexture previousActive = RenderTexture.active;
        RenderTexture previousTarget = captureCamera.targetTexture;
        try
        {
            captureCamera.targetTexture = targetTexture;
            captureCamera.Render();
            RenderTexture.active = targetTexture;
            Texture2D image = new Texture2D(CaptureWidth, CaptureHeight,
                TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, CaptureWidth, CaptureHeight), 0, 0);
            image.Apply(false, false);
            CaptureMetrics metrics = AnalyzePixels(
                image.GetPixels32(), CaptureWidth, CaptureHeight);
            if (!metrics.VisuallyUseful && !allowEmpty)
                throw new InvalidOperationException(
                    $"Capture {label} is visually empty: pixels={metrics.VisiblePixels} " +
                    $"luminance={metrics.LuminanceMin:0.000}-{metrics.LuminanceMax:0.000} " +
                    $"variance={metrics.Variance:0.000000} colorful={metrics.ColorfulPixels} " +
                    $"edges={metrics.EdgePixels} bounds={bounds}");

            string filename = label + ".png";
            File.WriteAllBytes(Path.Combine(absoluteFolder, filename), image.EncodeToPNG());
            captures.Add(EvidenceFolder + "/" + filename);
            Debug.Log(
                $"GYMCHAOS_ART_CAPTURE label={label} graphics=Direct3D12 " +
                $"visiblePixels={metrics.VisiblePixels} " +
                $"luminance={metrics.LuminanceMin:0.000}-{metrics.LuminanceMax:0.000} " +
                $"variance={metrics.Variance:0.000000} colorful={metrics.ColorfulPixels} " +
                $"edges={metrics.EdgePixels} visuallyUseful={(metrics.VisuallyUseful ? 1 : 0)} " +
                $"path={EvidenceFolder}/{filename}");
            UnityEngine.Object.DestroyImmediate(image);
            return metrics.VisuallyUseful;
        }
        finally
        {
            captureCamera.targetTexture = previousTarget;
            RenderTexture.active = previousActive;
            targetTexture.Release();
            UnityEngine.Object.DestroyImmediate(targetTexture);
        }
    }

    private static CaptureMetrics AnalyzePixels(Color32[] pixels, int width, int height)
    {
        CaptureMetrics metrics = new CaptureMetrics
        {
            LuminanceMin = 1f,
            LuminanceMax = 0f,
        };
        double sum = 0d;
        double sumSquares = 0d;
        int centerColorful = 0;
        int centerPixels = 0;
        int centerMarkerPixels = 0;
        double centerSum = 0d;
        double centerSumSquares = 0d;
        int centerEdgePixels = 0;
        for (int y = 0; y < height; y++)
        {
            int rowOffset = y * width;
            for (int x = 0; x < width; x++)
            {
                Color32 pixel = pixels[rowOffset + x];
                float luminance = (0.2126f * pixel.r + 0.7152f * pixel.g +
                    0.0722f * pixel.b) / 255f;
                float colorSpread = (Mathf.Max(pixel.r, Mathf.Max(pixel.g, pixel.b)) -
                    Mathf.Min(pixel.r, Mathf.Min(pixel.g, pixel.b))) / 255f;
                bool markerPixel = pixel.g > 150 && pixel.r < 110 && pixel.b < 110;
                metrics.LuminanceMin = Mathf.Min(metrics.LuminanceMin, luminance);
                metrics.LuminanceMax = Mathf.Max(metrics.LuminanceMax, luminance);
                if (luminance > 0.035f) metrics.VisiblePixels++;
                if (colorSpread > 0.10f) metrics.ColorfulPixels++;
                if (markerPixel) metrics.MarkerPixels++;
                sum += luminance;
                sumSquares += luminance * luminance;

                bool inCenter = x >= width / 3 && x < width * 2 / 3 &&
                    y >= height / 3 && y < height * 2 / 3;
                if (inCenter)
                {
                    centerPixels++;
                    if (colorSpread > 0.10f) centerColorful++;
                    if (markerPixel) centerMarkerPixels++;
                    centerSum += luminance;
                    centerSumSquares += luminance * luminance;
                }
                if (x > 0)
                {
                    float previous = Luminance(pixels[rowOffset + x - 1]);
                    if (Mathf.Abs(luminance - previous) > 0.055f)
                    {
                        metrics.EdgePixels++;
                        if (x > width / 3 && x < width * 2 / 3 &&
                            y >= height / 3 && y < height * 2 / 3)
                            centerEdgePixels++;
                    }
                }
                if (y > 0)
                {
                    float previous = Luminance(pixels[(y - 1) * width + x]);
                    if (Mathf.Abs(luminance - previous) > 0.055f)
                    {
                        metrics.EdgePixels++;
                        if (x >= width / 3 && x < width * 2 / 3 &&
                            y > height / 3 && y < height * 2 / 3)
                            centerEdgePixels++;
                    }
                }
            }
        }

        float pixelCount = Mathf.Max(1, pixels.Length);
        float mean = (float)(sum / pixelCount);
        metrics.Variance = Mathf.Max(0f, (float)(sumSquares / pixelCount) - mean * mean);
        float visibleRatio = metrics.VisiblePixels / pixelCount;
        float colorfulRatio = metrics.ColorfulPixels / pixelCount;
        float edgeRatio = metrics.EdgePixels / (pixelCount * 2f);
        float centerColorfulRatio = centerColorful / (float)Mathf.Max(1, centerPixels);
        float centerPixelCount = Mathf.Max(1, centerPixels);
        float centerMean = (float)(centerSum / centerPixelCount);
        metrics.CenterVariance = Mathf.Max(0f,
            (float)(centerSumSquares / centerPixelCount) - centerMean * centerMean);
        metrics.CenterEdgePixels = centerEdgePixels;
        metrics.CenterMarkerPixels = centerMarkerPixels;
        float centerEdgeRatio = centerEdgePixels / (centerPixelCount * 2f);
        metrics.Score = visibleRatio * 0.25f + metrics.Variance * 12f +
            colorfulRatio * 3f + edgeRatio * 2f + centerColorfulRatio * 4f +
            metrics.CenterVariance * 28f + centerEdgeRatio * 12f +
            (centerMarkerPixels / centerPixelCount) * 100f;
        metrics.VisuallyUseful = metrics.VisiblePixels >= pixels.Length / 80 &&
            metrics.LuminanceMax - metrics.LuminanceMin >= 0.04f &&
            (metrics.Variance >= 0.0008f || metrics.ColorfulPixels >= pixels.Length / 180 ||
                metrics.EdgePixels >= pixels.Length / 90) &&
            (metrics.CenterVariance >= 0.00025f ||
                metrics.CenterEdgePixels >= centerPixels / 100);
        return metrics;
    }

    private static float Luminance(Color32 pixel)
    {
        return (0.2126f * pixel.r + 0.7152f * pixel.g + 0.0722f * pixel.b) / 255f;
    }

    private static void DestroyCaptureRig(bool immediate)
    {
        if (captureCamera != null)
        {
            if (immediate) UnityEngine.Object.DestroyImmediate(captureCamera.gameObject);
            else UnityEngine.Object.Destroy(captureCamera.gameObject);
        }
        if (captureLight != null)
        {
            if (immediate) UnityEngine.Object.DestroyImmediate(captureLight);
            else UnityEngine.Object.Destroy(captureLight);
        }
        captureCamera = null;
        captureLight = null;
    }
}
