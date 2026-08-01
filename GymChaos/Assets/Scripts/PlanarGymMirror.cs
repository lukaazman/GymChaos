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

    public static void Create(
        Transform parent, Camera playerCamera, Renderer[] mirrorRenderers,
        Vector3 pointOnPlane, Vector3 normal)
    {
        if (playerCamera == null || mirrorRenderers == null || mirrorRenderers.Length == 0)
        {
            return;
        }

        GameObject controller = new GameObject("Realtime Planar Mirror");
        controller.transform.SetParent(parent, false);
        PlanarGymMirror mirror = controller.AddComponent<PlanarGymMirror>();
        mirror.Initialize(playerCamera, mirrorRenderers, pointOnPlane, normal);
    }

    private void Initialize(
        Camera playerCamera, Renderer[] mirrorRenderers,
        Vector3 pointOnPlane, Vector3 normal)
    {
        sourceCamera = playerCamera;
        planePoint = pointOnPlane;
        planeNormal = normal.normalized;
        int reflectionCullingMask = sourceCamera.cullingMask;

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

        int width = Mathf.Clamp(Mathf.RoundToInt(Screen.width * 0.55f), 512, 960);
        int height = Mathf.Clamp(Mathf.RoundToInt(Screen.height * 0.55f), 288, 540);
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
    }

    private void LateUpdate()
    {
        UpdateReflectionCamera();
        if (reflectionCamera != null)
        {
            reflectionCamera.enabled = (Time.frameCount & 1) == 0;
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
        reflectionCamera.nearClipPlane = sourceCamera.nearClipPlane;
        reflectionCamera.farClipPlane = sourceCamera.farClipPlane;
        reflectionCamera.projectionMatrix = sourceCamera.projectionMatrix;

        float signedDistance = Vector3.Dot(sourceCamera.transform.position - planePoint, planeNormal);
        Vector3 reflectedPosition = sourceCamera.transform.position - 2f * signedDistance * planeNormal;
        Vector3 reflectedForward = Vector3.Reflect(sourceCamera.transform.forward, planeNormal);
        Vector3 reflectedUp = Vector3.Reflect(sourceCamera.transform.up, planeNormal);
        reflectionCamera.transform.SetPositionAndRotation(
            reflectedPosition, Quaternion.LookRotation(reflectedForward, reflectedUp));

        Vector4 clipPlane = CameraSpacePlane(
            reflectionCamera, planePoint, planeNormal, 1f, 0.03f);
        reflectionCamera.projectionMatrix = reflectionCamera.CalculateObliqueMatrix(clipPlane);
        Matrix4x4 gpuProjection = GL.GetGPUProjectionMatrix(reflectionCamera.projectionMatrix, true);
        mirrorMaterial.SetMatrix(
            MirrorViewProjectionId, gpuProjection * reflectionCamera.worldToCameraMatrix);
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
