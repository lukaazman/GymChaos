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
    private static readonly string EvidenceFolder = "Assets/VisualFidelity/evidence_unity";
    private static double startedAt;
    private static bool completed;
    private static bool initialized;
    private static Camera captureCamera;
    private static GameObject captureLight;

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
            if (GymProteinStoreEnvironment.TransparentFridgePanelCount < 6)
                throw new InvalidOperationException("Runtime store does not expose its six transparent fridge doors.");

            CreateCaptureRig();
            List<string> captures = CaptureRequiredViews();
            int requiredCaptures = StoreSectionOnly ? 12 : 20;
            if (captures.Count < requiredCaptures)
                throw new InvalidOperationException(
                    $"Expected at least {requiredCaptures} direct art captures, got {captures.Count}.");

            initialized = true;
            completed = true;
            Debug.Log(
                "GYMCHAOS_ART_FIDELITY_OK " +
                "graphics=Direct3D12 " +
                $"surfaces=7 storeExterior=1 storeInterior=1 productFamilies={ProductGroups.Length} " +
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
            // Keep RequestedKey: PlayModeChanged needs it to exit batch mode
            // with a failure code instead of leaving Unity running.
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

    // protein.com v2 merges each category's products into one textured mesh
    // (labels come from the packaging atlas), so check the categories the
    // store must stock and that each holds real geometry.
    private static readonly (string Family, string Group)[] ProductGroups =
    {
        ("whey", "Products_Protein"), ("isolate", "Products_Isolate"),
        ("creatine", "Products_Creatine"), ("pre_workout", "Products_PreWorkout"),
        ("protein_bar", "Products_Bars"), ("drinks", "Products_Drinks"),
        ("snacks_shakers", "Products_Snacks")
    };

    private static string ValidateRuntimeProductFamilies(Transform root)
    {
        if (root == null) throw new InvalidOperationException("ProteinStore runtime root is null.");
        MeshFilter[] filters = root.GetComponentsInChildren<MeshFilter>(true);
        List<string> details = new List<string>();
        foreach ((string family, string group) in ProductGroups)
        {
            int vertices = filters.Where(filter => filter.name == group && filter.sharedMesh != null)
                .Sum(filter => filter.sharedMesh.vertexCount);
            if (vertices < 200)
                throw new InvalidOperationException(
                    $"Product family contract failed: {family} group={group} vertices={vertices}");
            details.Add($"{family}:{vertices}");
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

        if (StoreSectionOnly)
        {
            CaptureStoreViews(absoluteFolder, captures);
            return captures;
        }

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
        // Look at the tiled divider from inside the locker room; a fixed
        // world offset ended up outside the building after the room moved.
        Vector3 intoRoom = Vector3.ProjectOnPlane(
            lockerBounds.center - bathroomTile.bounds.center, Vector3.up);
        if (intoRoom.sqrMagnitude < 0.01f) intoRoom = Vector3.right;
        intoRoom.Normalize();
        Vector3 tileEye = bathroomTile.bounds.center + intoRoom * 3.2f + Vector3.up * 1.2f;
        tileEye.x = Mathf.Clamp(tileEye.x, lockerBounds.min.x + 0.4f, lockerBounds.max.x - 0.4f);
        tileEye.z = Mathf.Clamp(tileEye.z, lockerBounds.min.z + 0.4f, lockerBounds.max.z - 0.4f);
        CaptureBounds("bathroom-tile", bathroomTile.bounds,
            tileEye, bathroomTile.bounds.center, absoluteFolder, captures, fieldOfView: 60f);

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

        CaptureStoreViews(absoluteFolder, captures);
        return captures;
    }

    // GYMCHAOS_ART_SECTION=store: a short run that renders only the shop.
    private static bool StoreSectionOnly => string.Equals(
        Environment.GetEnvironmentVariable("GYMCHAOS_ART_SECTION"), "store",
        StringComparison.OrdinalIgnoreCase);

    private static void CaptureStoreViews(string absoluteFolder, List<string> captures)
    {
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
                new[] { "Solid_Fridge_", "Fridge_" }, out fridgeBounds))
            throw new InvalidOperationException("Runtime fridge bounds disappeared before capture.");
        Vector3 storeCentre = store.center;
        CaptureFixture("protein-store-fridges", fridgeBounds, storeCentre, 3.4f, absoluteFolder, captures);
        // One close view per category fixture, from the aisle in front of it.
        (string Capture, string Fixture)[] shelves =
        {
            ("protein-store-shelf-protein", "Solid_WallBay_N02"),
            ("protein-store-shelf-isolate", "Solid_WallBay_N10"),
            ("protein-store-shelf-preworkout", "Solid_WallBay_S01"),
            ("protein-store-shelf-creatine", "Solid_WallBay_S08"),
            ("protein-store-shelf-bars", "Solid_Gondola_1W"),
            ("protein-store-shelf-snacks", "Solid_WallBay_N15"),
            ("protein-store-counter", "Solid_Counter"),
            ("protein-store-fridge-close", "Solid_Fridge_2"),
            ("protein-store-promo", "Solid_Promo_0"),
        };
        foreach ((string capture, string fixture) in shelves)
        {
            if (!TryFindAggregateBounds(GymProteinStoreEnvironment.RuntimeRoot,
                    new[] { fixture }, out Bounds bounds))
                throw new InvalidOperationException($"Store fixture {fixture} is missing.");
            // The counter faces north (customers); shelves face the store centre.
            Vector3 lookFrom = fixture == "Solid_Counter"
                ? bounds.center + Vector3.forward * 4f
                : storeCentre;
            CaptureFixture(capture, bounds, lookFrom,
                fixture == "Solid_Counter" ? 4.8f : 2.4f, absoluteFolder, captures);
        }
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

    private static void CaptureFixture(
        string name, Bounds bounds, Vector3 storeCentre, float distance,
        string absoluteFolder, List<string> captures)
    {
        Vector3 toCentre = Vector3.ProjectOnPlane(storeCentre - bounds.center, Vector3.up);
        if (toCentre.sqrMagnitude < 0.01f) toCentre = Vector3.left;
        toCentre.Normalize();
        CaptureBounds(name, bounds,
            bounds.center + toCentre * distance + Vector3.up * 0.4f,
            bounds.center,
            absoluteFolder, captures, fieldOfView: 50f);
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
            {
                // Keep the rejected frame next to the evidence for diagnosis.
                File.WriteAllBytes(Path.Combine(absoluteFolder, label + "-rejected.png"),
                    image.EncodeToPNG());
                throw new InvalidOperationException(
                    $"Capture {label} is visually empty: pixels={metrics.VisiblePixels} " +
                    $"luminance={metrics.LuminanceMin:0.000}-{metrics.LuminanceMax:0.000} " +
                    $"variance={metrics.Variance:0.000000} colorful={metrics.ColorfulPixels} " +
                    $"edges={metrics.EdgePixels} bounds={bounds}");
            }

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
