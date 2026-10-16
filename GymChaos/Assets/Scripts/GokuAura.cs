using UnityEngine;
using UnityEngine.Rendering;

// Super Saiyan aura: a hand-drawn style flame sheath around Goku while he
// flies. Two camera-facing quads (one behind the body, a faint one in front
// for the golden tint over him) are shaded procedurally: a capsule around
// head and feet, sharp flame tongues licking along the flame axis, a white
// core fading to gold. The flame axis points up while he rises and streams
// back behind him once he flies fast, as in the anime.
public sealed class GokuAura : MonoBehaviour
{
    private const string ShaderName = "GymChaos/GokuAura";
    // Body capsule radius around the head-feet line (shoulders inside).
    private const float BodyRadius = 0.36f;
    // Aura thickness around the body and length of the long flame tongues.
    private const float BaseThickness = 0.24f;
    private const float FlameLength = 1.15f;
    // The back sheet sits behind the body so the body draws over it; the
    // front tint sheet sits just in front of it.
    private const float BackOffset = 0.45f;
    private const float FrontOffset = 0.5f;

    private static Mesh quadMesh;
    private static readonly int CenterId = Shader.PropertyToID("_Center");
    private static readonly int AxisUId = Shader.PropertyToID("_AxisU");
    private static readonly int AxisVId = Shader.PropertyToID("_AxisV");
    private static readonly int RectId = Shader.PropertyToID("_Rect");
    private static readonly int SegmentId = Shader.PropertyToID("_Segment");
    private static readonly int ShapeId = Shader.PropertyToID("_Shape");
    private static readonly int BlendId = Shader.PropertyToID("_AuraBlend");

    private SkinnedMeshRenderer sourceRenderer;
    private Transform[] bones;
    private Transform headBone;
    private Transform footLeft;
    private Transform footRight;
    private MeshRenderer backRenderer;
    private MeshRenderer frontRenderer;
    private Material backMaterial;
    private Material frontMaterial;
    private EnemyFighter fighter;
    private float blend;
    private Vector3 lastCenter;
    private bool hasLastCenter;
    private Vector3 smoothedVelocity;
    private Vector3 flameDirection = Vector3.up;
    private Vector3 head;
    private Vector3 feet;

    public float CurrentBlend => blend;
    public bool IsShowing => backRenderer != null && backRenderer.enabled;
    // World direction the flame tongues stream towards.
    public Vector3 FlameDirection => flameDirection;
    public Vector3 SmoothedVelocity => smoothedVelocity;
    public Renderer BackRenderer => backRenderer;
    public Vector3 HeadPoint => head;
    public Vector3 FeetPoint => feet;
    public string BoneNames =>
        $"{(headBone != null ? headBone.name : "none")}/{(footLeft != null ? footLeft.name : "none")}/{(footRight != null ? footRight.name : "none")}" +
        $" src={(sourceRenderer != null ? sourceRenderer.name : "none")} srcEnabled={(sourceRenderer != null && sourceRenderer.enabled)}";

    public void Configure(SkinnedMeshRenderer source)
    {
        // Both visual builders may configure Goku; keep one pair of sheets.
        DestroySheets();
        sourceRenderer = source;
        bones = source != null ? source.bones : null;
        headBone = null;
        footLeft = null;
        footRight = null;
        fighter = GetComponent<EnemyFighter>();
        FindBones();
        CreateSheets();
        SetVisible(false);
    }

    private void Awake()
    {
        fighter = GetComponent<EnemyFighter>();
    }

    private void OnEnable()
    {
        RenderPipelineManager.beginCameraRendering += OnBeginCamera;
    }

    private void OnDisable()
    {
        RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
    }

    private void FindBones()
    {
        if (sourceRenderer == null)
        {
            return;
        }
        if (bones == null)
        {
            return;
        }
        for (int i = 0; i < bones.Length; i++)
        {
            Transform bone = bones[i];
            if (bone == null)
            {
                continue;
            }
            string name = bone.name.ToLowerInvariant();
            // Mixamo/Unity rigs name it "Head"; Rigify's deform head is
            // the last spine bone.
            if (headBone == null && (name.EndsWith("head") || name.EndsWith("spine.006")))
            {
                headBone = bone;
            }
            else if (name.Contains("foot") && !name.Contains("end"))
            {
                bool left = name.Contains("left") || name.EndsWith(".l") || name.EndsWith("_l");
                if (left && footLeft == null)
                {
                    footLeft = bone;
                }
                else if (!left && footRight == null)
                {
                    footRight = bone;
                }
            }
        }
    }

    private void CreateSheets()
    {
        Shader shader = Shader.Find(ShaderName);
        if (shader == null)
        {
            Debug.LogWarning($"Could not find {ShaderName}; Goku aura is disabled.", this);
            return;
        }
        if (quadMesh == null)
        {
            quadMesh = new Mesh { name = "Goku Aura Sheet" };
            quadMesh.vertices = new[]
            {
                new Vector3(0f, 0f, 0f), new Vector3(1f, 0f, 0f),
                new Vector3(0f, 1f, 0f), new Vector3(1f, 1f, 0f)
            };
            quadMesh.uv = new[]
            {
                new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(0f, 1f), new Vector2(1f, 1f)
            };
            quadMesh.triangles = new[] { 0, 2, 1, 2, 3, 1 };
            // The vertex shader places the sheet in world space; these bounds
            // only keep the renderer from being culled around Goku.
            quadMesh.bounds = new Bounds(Vector3.zero, Vector3.one * 8f);
        }
        backMaterial = CreateMaterial(shader, "Goku Aura Back", 0f, 3120);
        frontMaterial = CreateMaterial(shader, "Goku Aura Front", 1f, 3121);
        backRenderer = CreateSheet("Goku Aura Back Sheet", backMaterial);
        frontRenderer = CreateSheet("Goku Aura Front Sheet", frontMaterial);
    }

    private static Material CreateMaterial(Shader shader, string name, float front, int queue)
    {
        Material material = new Material(shader) { name = name, renderQueue = queue };
        material.SetFloat("_Front", front);
        material.SetFloat(BlendId, 0f);
        return material;
    }

    private MeshRenderer CreateSheet(string name, Material material)
    {
        GameObject sheet = new GameObject(name);
        sheet.transform.SetParent(transform, false);
        sheet.layer = sourceRenderer != null ? sourceRenderer.gameObject.layer : gameObject.layer;
        sheet.AddComponent<MeshFilter>().sharedMesh = quadMesh;
        MeshRenderer renderer = sheet.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.lightProbeUsage = LightProbeUsage.Off;
        renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
        return renderer;
    }

    private void LateUpdate()
    {
        if (backRenderer == null)
        {
            return;
        }
        if (fighter == null)
        {
            fighter = GetComponent<EnemyFighter>();
        }

        blend = fighter != null ? fighter.GokuFlightAuraBlend : 0f;
        SetVisible(blend > 0.001f);
        if (blend <= 0.001f)
        {
            hasLastCenter = false;
            smoothedVelocity = Vector3.zero;
            flameDirection = Vector3.up;
            return;
        }
        SampleBody();

        Vector3 center = (head + feet) * 0.5f;
        float dt = Mathf.Max(Time.deltaTime, 0.0001f);
        if (hasLastCenter && blend > 0.001f)
        {
            Vector3 velocity = (center - lastCenter) / dt;
            if (velocity.sqrMagnitude > 400f)
            {
                velocity = Vector3.zero; // teleport
            }
            smoothedVelocity = Vector3.Lerp(smoothedVelocity, velocity, 1f - Mathf.Exp(-dt * 6f));
        }
        else
        {
            smoothedVelocity = Vector3.zero;
        }
        lastCenter = center;
        hasLastCenter = true;

        // Rising or hovering: flames go up. Flying fast: they stream back.
        float speed = smoothedVelocity.magnitude;
        Vector3 back = speed > 0.05f ? -smoothedVelocity / speed : Vector3.zero;
        Vector3 target = (Vector3.up + back * (3f * Mathf.Clamp01(speed / 3.5f))).normalized;
        Vector3 eased = Vector3.Lerp(flameDirection, target, 1f - Mathf.Exp(-dt * 8f));
        flameDirection = eased.sqrMagnitude > 0.0001f ? eased.normalized : target;

        backMaterial.SetFloat(BlendId, blend);
        frontMaterial.SetFloat(BlendId, blend);
    }

    private void SampleBody()
    {
        if (footLeft != null || footRight != null)
        {
            feet = footLeft != null && footRight != null
                ? (footLeft.position + footRight.position) * 0.5f
                : (footLeft != null ? footLeft : footRight).position;
            // The far end of the sheath: the head standing, the leading
            // fist when he flies arm-first. Whichever bone is furthest out.
            head = headBone != null ? headBone.position : feet + Vector3.up;
            float far = (head - feet).sqrMagnitude;
            for (int i = 0; i < bones.Length; i++)
            {
                if (bones[i] == null)
                {
                    continue;
                }
                float distance = (bones[i].position - feet).sqrMagnitude;
                if (distance > far)
                {
                    far = distance;
                    head = bones[i].position;
                }
            }
            // Bones end inside the skull/fist; the hair and knuckles go further.
            Vector3 axis = head - feet;
            if (axis.sqrMagnitude > 0.0001f)
            {
                head += axis.normalized * 0.22f;
            }
            return;
        }
        Bounds bounds = sourceRenderer != null
            ? sourceRenderer.bounds
            : new Bounds(transform.position + Vector3.up, new Vector3(0.8f, 2f, 0.8f));
        head = new Vector3(bounds.center.x, bounds.max.y, bounds.center.z);
        feet = new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
    }

    private void OnBeginCamera(ScriptableRenderContext context, Camera camera)
    {
        if (camera == null || backRenderer == null || !backRenderer.enabled ||
            (camera.cameraType != CameraType.Game && camera.cameraType != CameraType.SceneView))
        {
            return;
        }
        Vector3 center = (head + feet) * 0.5f;
        Vector3 toCamera = camera.transform.position - center;
        if (toCamera.sqrMagnitude < 0.0001f)
        {
            return;
        }
        Vector3 normal = toCamera.normalized;
        Vector3 projected = Vector3.ProjectOnPlane(flameDirection, normal);
        float alongView = projected.magnitude;
        Vector3 axisV = alongView > 0.05f
            ? projected / alongView
            : Vector3.ProjectOnPlane(camera.transform.up, normal).normalized;
        Vector3 axisU = Vector3.Cross(normal, axisV).normalized;
        // Looking down the flame axis (he flies straight at the camera):
        // the tongues burst out all round instead of streaming one way.
        float radial = 1f - Mathf.Clamp01((alongView - 0.15f) / 0.5f);

        Vector2 a = new Vector2(Vector3.Dot(head - center, axisU), Vector3.Dot(head - center, axisV));
        Vector2 b = new Vector2(Vector3.Dot(feet - center, axisU), Vector3.Dot(feet - center, axisV));
        float tongue = FlameLength * Mathf.Lerp(0.35f, 0.6f, radial);
        float margin = BodyRadius + BaseThickness + tongue + 0.3f;
        Vector4 rect = new Vector4(
            Mathf.Min(a.x, b.x) - margin, Mathf.Min(a.y, b.y) - margin,
            Mathf.Max(a.x, b.x) + margin, Mathf.Max(a.y, b.y) + margin + FlameLength * (1f - radial));

        Apply(backMaterial, center - normal * BackOffset, axisU, axisV, rect, a, b, radial);
        Apply(frontMaterial, center + normal * FrontOffset, axisU, axisV, rect, a, b, radial);
    }

    private static void Apply(
        Material material, Vector3 center, Vector3 axisU, Vector3 axisV,
        Vector4 rect, Vector2 a, Vector2 b, float radial)
    {
        material.SetVector(CenterId, center);
        material.SetVector(AxisUId, axisU);
        material.SetVector(AxisVId, axisV);
        material.SetVector(RectId, rect);
        material.SetVector(SegmentId, new Vector4(a.x, a.y, b.x, b.y));
        material.SetVector(ShapeId, new Vector4(BodyRadius, BaseThickness, FlameLength, radial));
    }

    private void SetVisible(bool visible)
    {
        if (backRenderer != null)
        {
            backRenderer.enabled = visible;
        }
        if (frontRenderer != null)
        {
            frontRenderer.enabled = visible;
        }
    }

    private void DestroySheets()
    {
        if (backRenderer != null)
        {
            Destroy(backRenderer.gameObject);
        }
        if (frontRenderer != null)
        {
            Destroy(frontRenderer.gameObject);
        }
        if (backMaterial != null)
        {
            Destroy(backMaterial);
        }
        if (frontMaterial != null)
        {
            Destroy(frontMaterial);
        }
        backRenderer = null;
        frontRenderer = null;
        backMaterial = null;
        frontMaterial = null;
    }

    private void OnDestroy()
    {
        DestroySheets();
    }
}
