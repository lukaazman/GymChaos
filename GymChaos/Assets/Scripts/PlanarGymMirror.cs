using UnityEngine;
using UnityEngine.Rendering;

public sealed class PlanarGymMirror : MonoBehaviour
{
    public const int MirrorSurfaceLayer = 28;
    public const int MirrorPlayerLayer = 29;
    public const int FirstPersonPlayerLayer = 30;

    private static readonly int ReflectionTextureId = Shader.PropertyToID("_ReflectionTex");
    private static readonly int MirrorViewProjectionId = Shader.PropertyToID("_MirrorVP");

    private Camera sourceCamera;
    private Camera reflectionCamera;
    private RenderTexture reflectionTexture;
    private Material mirrorMaterial;
    private Vector3 planePoint;
    private Vector3 planeNormal;
    private bool invertCulling;
    private bool continuousRefresh;
    private bool useObliqueClip = true;
    private float nextPlayerMaterialRepair;

    public bool ReflectionIncludesPlayerLayer => reflectionCamera != null &&
        (reflectionCamera.cullingMask & (1 << MirrorPlayerLayer)) != 0;
    public Camera ReflectionCamera => reflectionCamera;
    public RenderTexture ReflectionTexture => reflectionTexture;
    public string ReflectionShaderName => mirrorMaterial != null &&
        mirrorMaterial.shader != null ? mirrorMaterial.shader.name : string.Empty;
    public bool HasReadyReflectionTexture => reflectionTexture != null &&
        reflectionTexture.IsCreated();
    public Vector3 PlaneNormal => planeNormal;
    public bool ContinuousRefresh
    {
        get => continuousRefresh;
        set => continuousRefresh = value;
    }

    public void RequestImmediateRefresh()
    {
        UpdateReflectionCamera();
        if (reflectionCamera != null)
        {
            // Enabling the camera here makes an outfit change visible on the
            // next camera pass even when the mirror was in its alternating
            // refresh phase. Locker mode still keeps ContinuousRefresh on.
            reflectionCamera.enabled = true;
        }
    }

    public static void Create(
        Transform parent, Camera playerCamera, Renderer[] mirrorRenderers,
        Vector3 pointOnPlane, Vector3 normal, bool useObliqueClip = true)
    {
        if (playerCamera == null || mirrorRenderers == null || mirrorRenderers.Length == 0)
        {
            return;
        }

        GameObject controller = new GameObject("Realtime Planar Mirror");
        controller.transform.SetParent(parent, false);
        PlanarGymMirror mirror = controller.AddComponent<PlanarGymMirror>();
        mirror.useObliqueClip = useObliqueClip;
        mirror.Initialize(playerCamera, mirrorRenderers, pointOnPlane, normal);
    }

    private void Initialize(
        Camera playerCamera, Renderer[] mirrorRenderers,
        Vector3 pointOnPlane, Vector3 normal)
    {
        sourceCamera = playerCamera;
        planePoint = pointOnPlane;
        planeNormal = normal.normalized;
        // A previous mirror removes MirrorPlayerLayer from the gameplay
        // camera. Locker-room mirrors are created after the gym mirrors, so
        // deriving this mask directly from sourceCamera would make the later
        // reflection camera silently omit the player and outfit overlays.
        int reflectionCullingMask =
            sourceCamera.cullingMask | (1 << MirrorPlayerLayer);

        // Keep the player body out of the gameplay camera even if a platform
        // strips the custom mirror shader. Without this guard WebGL can fall
        // through into a camera-inside-the-player view.
        sourceCamera.cullingMask &= ~(1 << MirrorPlayerLayer);

        Shader shader = Shader.Find("GymChaos/PlanarMirror");
        if (shader == null)
        {
            shader = Shader.Find("Universal Render Pipeline/Unlit");
        }
        if (shader == null)
        {
            Debug.LogError("GymChaos mirror shader is unavailable on this platform.");
            return;
        }
        mirrorMaterial = new Material(shader)
        {
            name = "Realtime Gym Mirror",
            hideFlags = HideFlags.DontSave
        };

        int width = Mathf.Clamp(Mathf.RoundToInt(Screen.width * 0.62f), 512, 1024);
        int height = Mathf.Clamp(Mathf.RoundToInt(Screen.height * 0.62f), 288, 576);
        reflectionTexture = new RenderTexture(width, height, 16, RenderTextureFormat.Default)
        {
            name = "Gym Planar Reflection",
            antiAliasing = 1,
            useMipMap = false,
            autoGenerateMips = false,
            hideFlags = HideFlags.DontSave
        };
        reflectionTexture.Create();
        mirrorMaterial.SetTexture(ReflectionTextureId, reflectionTexture);

        GameObject cameraObject = new GameObject("Gym Mirror Camera");
        cameraObject.transform.SetParent(transform, false);
        reflectionCamera = cameraObject.AddComponent<Camera>();
        reflectionCamera.enabled = true;
        reflectionCamera.targetTexture = reflectionTexture;
        reflectionCamera.depth = sourceCamera.depth - 1f;
        reflectionCamera.cullingMask = reflectionCullingMask &
            ~(1 << MirrorSurfaceLayer) & ~(1 << FirstPersonPlayerLayer);
        reflectionCamera.clearFlags = sourceCamera.clearFlags;
        reflectionCamera.backgroundColor = sourceCamera.backgroundColor;
        reflectionCamera.allowHDR = false;
        reflectionCamera.allowMSAA = false;

        for (int i = 0; i < mirrorRenderers.Length; i++)
        {
            if (mirrorRenderers[i] == null)
            {
                continue;
            }
            mirrorRenderers[i].gameObject.layer = MirrorSurfaceLayer;
            mirrorRenderers[i].sharedMaterial = mirrorMaterial;
        }

        RenderPipelineManager.beginCameraRendering += BeginCameraRendering;
        RenderPipelineManager.endCameraRendering += EndCameraRendering;
        UpdateReflectionCamera();
        Debug.Log(
            $"GYMCHAOS_PLANAR_MIRROR_READY normal={planeNormal} " +
            $"playerLayer={(reflectionCamera.cullingMask & (1 << MirrorPlayerLayer)) != 0}",
            this);
    }

    private void LateUpdate()
    {
        UpdateReflectionCamera();
        if (Time.unscaledTime >= nextPlayerMaterialRepair)
        {
            nextPlayerMaterialRepair = Time.unscaledTime + 0.5f;
            EnforceOpaqueMirrorPlayerMaterials();
        }
        if (reflectionCamera != null)
        {
            reflectionCamera.enabled = true;
        }
    }

    private void UpdateReflectionCamera()
    {
        if (sourceCamera == null || reflectionCamera == null)
        {
            return;
        }

        reflectionCamera.fieldOfView = sourceCamera.fieldOfView;
        reflectionCamera.aspect = sourceCamera.aspect;
        reflectionCamera.nearClipPlane = Mathf.Min(sourceCamera.nearClipPlane, 0.03f);
        reflectionCamera.farClipPlane = sourceCamera.farClipPlane;
        reflectionCamera.projectionMatrix = sourceCamera.projectionMatrix;
        reflectionCamera.rect = new Rect(0f, 0f, 1f, 1f);
        reflectionCamera.pixelRect = new Rect(
            0f, 0f, reflectionTexture.width, reflectionTexture.height);

        float signedDistance = Vector3.Dot(sourceCamera.transform.position - planePoint, planeNormal);
        Vector3 reflectedPosition = sourceCamera.transform.position - 2f * signedDistance * planeNormal;
        Vector3 reflectedForward = Vector3.Reflect(sourceCamera.transform.forward, planeNormal);
        Vector3 reflectedUp = Vector3.Reflect(sourceCamera.transform.up, planeNormal);
        reflectionCamera.transform.SetPositionAndRotation(
            reflectedPosition, Quaternion.LookRotation(reflectedForward, reflectedUp));

        // A reflection changes handedness. LookRotation alone builds a normal
        // camera and cannot be paired with inverted culling (it shows backsides).
        Vector4 plane = new Vector4(planeNormal.x, planeNormal.y, planeNormal.z,
            -Vector3.Dot(planeNormal, planePoint));
        Matrix4x4 reflection = Matrix4x4.identity;
        for (int row = 0; row < 3; row++)
            for (int column = 0; column < 4; column++)
                reflection[row, column] -= 2f * plane[row] * plane[column];
        reflectionCamera.worldToCameraMatrix = sourceCamera.worldToCameraMatrix * reflection;

        if (useObliqueClip)
        {
            Vector3 viewerNormal = signedDistance >= 0f ? planeNormal : -planeNormal;
            Vector4 clipPlane = CameraSpacePlane(
                reflectionCamera, planePoint, viewerNormal, 1f, 0.008f);
            reflectionCamera.projectionMatrix = reflectionCamera.CalculateObliqueMatrix(clipPlane);
        }
        Matrix4x4 gpuProjection = GL.GetGPUProjectionMatrix(reflectionCamera.projectionMatrix, true);
        mirrorMaterial.SetMatrix(
            MirrorViewProjectionId, gpuProjection * reflectionCamera.worldToCameraMatrix);
    }

    private void OnPreCull()
    {
        if (reflectionTexture != null && !reflectionTexture.IsCreated())
        {
            reflectionTexture.Create();
            mirrorMaterial?.SetTexture(ReflectionTextureId, reflectionTexture);
        }
    }

    private static void EnforceOpaqueMirrorPlayerMaterials()
    {
        PlayerHandRig[] playerRigs = FindObjectsByType<PlayerHandRig>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < playerRigs.Length; i++)
        {
            if (playerRigs[i] != null)
            {
                playerRigs[i].EnsureMirrorAppearance();
            }
        }

        Renderer[] renderers = FindObjectsByType<Renderer>(FindObjectsSortMode.None);
        for (int r = 0; r < renderers.Length; r++)
        {
            Renderer renderer = renderers[r];
            if (renderer == null || renderer.gameObject.layer != MirrorPlayerLayer ||
                renderer.GetComponentInParent<PlayerHandRig>() != null) continue;
            Material[] materials = renderer.sharedMaterials;
            for (int i = 0; i < materials.Length; i++)
            {
                Material material = materials[i];
                if (material == null) continue;
                if (material.HasProperty("_BaseColor"))
                {
                    Color color = material.GetColor("_BaseColor");
                    color.a = 1f;
                    material.SetColor("_BaseColor", color);
                }
                if (material.HasProperty("_Color"))
                {
                    Color color = material.GetColor("_Color");
                    color.a = 1f;
                    material.SetColor("_Color", color);
                }
                if (material.HasProperty("_Surface")) material.SetFloat("_Surface", 0f);
                if (material.HasProperty("_AlphaClip")) material.SetFloat("_AlphaClip", 0f);
                material.SetOverrideTag("RenderType", "Opaque");
                material.renderQueue = (int)RenderQueue.Geometry;
            }
        }
    }

    private static Vector4 CameraSpacePlane(
        Camera camera, Vector3 point, Vector3 normal, float sideSign, float offset)
    {
        Vector3 offsetPoint = point + normal * offset;
        Matrix4x4 worldToCamera = camera.worldToCameraMatrix;
        Vector3 cameraPoint = worldToCamera.MultiplyPoint(offsetPoint);
        Vector3 cameraNormal = worldToCamera.MultiplyVector(normal).normalized * sideSign;
        return new Vector4(
            cameraNormal.x, cameraNormal.y, cameraNormal.z,
            -Vector3.Dot(cameraPoint, cameraNormal));
    }

    private void BeginCameraRendering(ScriptableRenderContext context, Camera camera)
    {
        if (camera == reflectionCamera && !invertCulling)
        {
            GL.invertCulling = true;
            invertCulling = true;
        }
    }

    private void EndCameraRendering(ScriptableRenderContext context, Camera camera)
    {
        if (camera == reflectionCamera && invertCulling)
        {
            GL.invertCulling = false;
            invertCulling = false;
        }
    }

    private void OnDestroy()
    {
        RenderPipelineManager.beginCameraRendering -= BeginCameraRendering;
        RenderPipelineManager.endCameraRendering -= EndCameraRendering;
        if (invertCulling)
        {
            GL.invertCulling = false;
        }
        if (reflectionTexture != null)
        {
            reflectionTexture.Release();
            Destroy(reflectionTexture);
        }
        if (mirrorMaterial != null)
        {
            Destroy(mirrorMaterial);
        }
    }
}
