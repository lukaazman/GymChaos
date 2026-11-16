using UnityEngine;

/// <summary>
/// The player model beside the journal's numbers: a body in the clothes the
/// player wears right now, playing the authored idle clip on a loop, in front
/// of its own camera and render texture. Dragging turns it, the wheel zooms it.
/// It lives far below the gym so the world never shows behind it.
/// </summary>
public sealed class GymJournalPreview : MonoBehaviour
{
    private const float StudioY = -900f;
    private const float MinDistance = 2.1f;
    private const float MaxDistance = 4.6f;
    // Framing follows the body: a tall class needs the camera further back.
    private float bodyHeight = 1.8f;
    private float focusY;
    private float minDistance = MinDistance;
    private float maxDistance = MaxDistance;
    // Breathing room around the body: the idle clip swings arms and shoulders
    // past the bind pose and must never reach the edge of the view.
    // The idle clip carries a little root motion, so the drawn body swings a
    // hand's width past its bind-pose mesh; the margin absorbs it.
    private const float FitMargin = 1.22f;
    // The body gets its own layer so the studio's own lights reach it and the
    // gym's lights, windows and skybox never do.
    private const int PreviewLayer = 30;
    private static readonly Vector3 StudioOrigin = new Vector3(0f, StudioY, 0f);

    private Camera previewCamera;
    private GameObject rig;
    private RectTransform view;
    private RenderTexture target;
    private GameObject studio;
    private GameObject model;
    private SkinnedMeshRenderer modelBody;
    private GameObject headwearRoot;
    private Transform head;
    private AnimationClip idleClip;
    private float phase;
    private float distance = 3.2f;
    private float yaw = 180f;
    private string lookKey = string.Empty;
    private string headwearKey = string.Empty;
    private int headwearVersion;

    public RenderTexture Target => target;
    public bool PreviewVisible => model != null && model.activeInHierarchy;
    /// <summary>Model heading in degrees; the verifier turns the body with it.</summary>
    public float Angle => yaw;
    public float Zoom => distance;
    public string BodyShown { get; private set; }

    /// <summary>What the view really shows, sampled from its own pixels.</summary>
    private struct ViewSample
    {
        public bool valid;
        public bool touches;
        public float minY;
        public float maxY;
    }

    /// <summary>
    /// The only honest framing test: a skinned mesh's bounds are stale and much
    /// wider than the body it draws, so the body is measured in pixels.
    /// </summary>
    private ViewSample SampleView()
    {
        ViewSample sample = default;
        if (target == null || model == null || !model.activeInHierarchy) return sample;
        const int width = 96;
        const int height = 120;
        RenderTexture previous = RenderTexture.active;
        RenderTexture small = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32);
        Graphics.Blit(target, small);
        RenderTexture.active = small;
        Texture2D probe = new Texture2D(width, height, TextureFormat.RGB24, false);
        probe.ReadPixels(new Rect(0f, 0f, width, height), 0, 0);
        probe.Apply();
        Color[] pixels = probe.GetPixels();
        Color corner = pixels[0];
        float backdrop = corner.r * 0.2126f + corner.g * 0.7152f + corner.b * 0.0722f;
        float minY = height;
        float maxY = -1f;
        bool touches = false;
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                Color pixel = pixels[y * width + x];
                float luminance = pixel.r * 0.2126f + pixel.g * 0.7152f + pixel.b * 0.0722f;
                if (luminance <= backdrop + 0.04f) continue;
                if (y < minY) minY = y;
                if (y > maxY) maxY = y;
                if (x < 3 || x > width - 4 || y < 3 || y > height - 4) touches = true;
            }
        }
        Destroy(probe);
        RenderTexture.active = previous;
        RenderTexture.ReleaseTemporary(small);
        sample.valid = maxY >= 0f;
        sample.touches = touches;
        sample.minY = minY / height;
        sample.maxY = (maxY + 1f) / height;
        return sample;
    }

    /// <summary>Alpha of the view's empty corner: 0 means the sheet shows through.</summary>
    public float CornerAlpha()
    {
        if (target == null) return 1f;
        RenderTexture previous = RenderTexture.active;
        RenderTexture small = RenderTexture.GetTemporary(8, 8, 0, RenderTextureFormat.ARGB32);
        Graphics.Blit(target, small);
        RenderTexture.active = small;
        Texture2D probe = new Texture2D(8, 8, TextureFormat.RGBA32, false);
        probe.ReadPixels(new Rect(0f, 0f, 8f, 8f), 0, 0);
        probe.Apply();
        float alpha = probe.GetPixels()[0].a;
        Destroy(probe);
        RenderTexture.active = previous;
        RenderTexture.ReleaseTemporary(small);
        return alpha;
    }

    /// <summary>Framing numbers, for reading the picture from a log.</summary>
    public string EdgeReport()
    {
        ViewSample sample = SampleView();
        return "touches=" + sample.touches + " lowest=" + sample.minY.ToString("F2") +
            " highest=" + sample.maxY.ToString("F2") + " distance=" + distance.ToString("F2") +
            " focus=" + focusY.ToString("F2") + " visible=" + sample.valid;
    }

    /// <summary>Verifier gate: no body pixel may reach the outer ring of the view.</summary>
    public bool BodyTouchesEdge() => SampleView().touches;

    /// <summary>The on-screen rectangle the pointer has to be over to turn the body.</summary>
    public void SetViewRect(RectTransform rect)
    {
        view = rect;
    }

    public static GymJournalPreview Create(Transform parent)
    {
        GameObject root = new GameObject("Journal Preview");
        root.transform.SetParent(parent, false);
        GymJournalPreview preview = root.AddComponent<GymJournalPreview>();
        preview.Build();
        return preview;
    }

    private void Build()
    {
        // The rig is a scene root, not a child of the UI: under a canvas the body
        // and the camera would inherit the canvas scale and land outside the view.
        rig = new GameObject("Journal Preview Rig");
        studio = new GameObject("Journal Studio");
        studio.transform.SetParent(rig.transform, false);
        studio.transform.position = StudioOrigin;

        GameObject cameraObject = new GameObject("Journal Preview Camera");
        cameraObject.transform.SetParent(rig.transform, false);
        previewCamera = cameraObject.AddComponent<Camera>();
        previewCamera.clearFlags = CameraClearFlags.SolidColor;
        // Clear to transparent and let the journal sheet show through: matching
        // the sheet's colour by hand left the view a different shade from the
        // page around it, a visible black box behind the player.
        previewCamera.backgroundColor = new Color(0f, 0f, 0f, 0f);
        previewCamera.clearFlags = CameraClearFlags.SolidColor;
        previewCamera.allowHDR = false;
        previewCamera.fieldOfView = 32f;
        previewCamera.nearClipPlane = 0.05f;
        previewCamera.farClipPlane = 40f;
        previewCamera.allowMSAA = true;
        previewCamera.cullingMask = 1 << PreviewLayer;

        // Key and rim light for the studio only: 900 m under the gym there is
        // nothing lighting the body, so it rendered as a black cut-out.
        GameObject keyObject = new GameObject("Journal Preview Key Light");
        keyObject.transform.SetParent(transform, false);
        Light key = keyObject.AddComponent<Light>();
        key.type = LightType.Directional;
        key.color = new Color(1f, 0.97f, 0.93f);
        key.intensity = 1.25f;
        key.cullingMask = 1 << PreviewLayer;
        key.shadows = LightShadows.None;
        keyObject.transform.rotation = Quaternion.Euler(24f, 200f, 0f);

        GameObject fillObject = new GameObject("Journal Preview Fill Light");
        fillObject.transform.SetParent(transform, false);
        fillObject.transform.position = StudioOrigin + new Vector3(1.6f, 2.1f, -2.2f);
        Light fill = fillObject.AddComponent<Light>();
        fill.type = LightType.Point;
        fill.color = new Color(0.85f, 0.9f, 1f);
        fill.intensity = 1.4f;
        fill.range = 7f;
        fill.cullingMask = 1 << PreviewLayer;
        fill.shadows = LightShadows.None;

        target = new RenderTexture(512, 640, 24, RenderTextureFormat.ARGB32)
        {
            name = "Journal Preview Texture",
            antiAliasing = 2
        };
        target.Create();
        previewCamera.targetTexture = target;
        PositionCamera();
    }

    /// <summary>Rebuilds the body when the player's look changes.</summary>
    public void Show(string classId, GymProgressionState state)
    {
        if (state == null || studio == null)
        {
            return;
        }
        string key = (classId ?? string.Empty) + "|" + state.shirt + "|" + state.top + "|" +
            state.bottom + "|" + state.bottomColor;
        if (key != lookKey)
        {
            lookKey = key;
            RebuildBody(classId, state);
        }
        RequestHeadwear(state.headwear);
        if (model != null)
        {
            model.SetActive(true);
        }
        if (previewCamera != null && !previewCamera.enabled)
        {
            previewCamera.enabled = true;
        }
    }

    public void Hide()
    {
        if (model != null) model.SetActive(false);
        if (headwearRoot != null) headwearRoot.SetActive(false);
        if (previewCamera != null) previewCamera.enabled = false;
        headwearKey = string.Empty;
        lookKey = string.Empty;
    }

    private void RebuildBody(string classId, GymProgressionState state)
    {
        ClearHeadwear();
        if (model != null) Destroy(model);
        model = null;
        modelBody = null;
        head = null;

        GameObject prefab = null;
        if (!string.IsNullOrEmpty(classId))
        {
            prefab = Resources.Load<GameObject>("Player/Classes/player_" + classId);
        }
        if (prefab == null)
        {
            prefab = Resources.Load<GameObject>(PlayerHandRig.DefaultModelResource);
        }
        if (prefab == null)
        {
            return;
        }

        model = Instantiate(prefab, studio.transform);
        model.name = "Journal Player Body";
        model.transform.localPosition = Vector3.zero;
        Transform[] parts = model.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < parts.Length; i++) parts[i].gameObject.layer = PreviewLayer;
        Animator[] animators = model.GetComponentsInChildren<Animator>(true);
        for (int i = 0; i < animators.Length; i++) animators[i].enabled = false;

        modelBody = model.GetComponentInChildren<SkinnedMeshRenderer>(true);
        // A new body needs its hat loaded again.
        headwearKey = string.Empty;
        // Dress first, frame second: the bottoms shell and the outfit mesh change
        // the real silhouette, and framing the undressed bind pose left the
        // shorts and feet hanging over the bottom of the view.
        ApplyOutfit(state);
        ApplyBottoms(state);
        FitModel();
        BodyShown = classId;
        head = HeadBone();
        LoadIdleClip();
        PositionCamera();
        Renderer[] built = model.GetComponentsInChildren<Renderer>(true);
        Renderer first = built.Length > 0 ? built[0] : null;
        Debug.Log("GYMCHAOS_JOURNAL_PREVIEW_BODY prefab=" + prefab.name + " renderers=" +
            built.Length + " layer=" + model.layer + " pos=" + model.transform.position.ToString("F2") +
            " scale=" + model.transform.lossyScale.ToString("F2") +
            " renderer=" + (first != null ? first.name : "none") +
            " enabled=" + (first != null && first.enabled) +
            " bounds=" + (first != null ? first.bounds.center.ToString("F2") + "/" +
                first.bounds.size.ToString("F2") : "none") +
            " cam=" + (previewCamera != null ? previewCamera.transform.position.ToString("F2") : "none") +
            " camOn=" + (previewCamera != null && previewCamera.enabled) +
            " mask=" + (previewCamera != null ? previewCamera.cullingMask : 0));
    }

    private void FitModel()
    {
        // Frame from the meshes' own vertices. A skinned renderer's bounds only
        // settle after it has drawn once and are narrower than the drawn body
        // (shorts, shoes and the smiley head sit outside them), which is how a
        // framed body still ended up sliced at the top or the feet.
        if (!TryMeasureMeshes(model, out Bounds bounds))
        {
            Renderer[] fallback = model.GetComponentsInChildren<Renderer>(true);
            if (fallback.Length == 0) return;
            bounds = Measure(fallback);
        }
        if (bounds.size.y < 0.05f) return;
        float scale = ExternalRiggedCharacterVisual.StandardGameplayHeight / bounds.size.y;
        model.transform.localScale *= scale;
        Vector3 pivot = model.transform.position;
        bodyHeight = Mathf.Max(0.8f, bounds.size.y * scale);
        focusY = pivot.y + (bounds.center.y - pivot.y) * scale;
        float tangent = Mathf.Tan(previewCamera.fieldOfView * 0.5f * Mathf.Deg2Rad);
        float fit = bodyHeight * 0.5f * FitMargin / tangent;
        minDistance = Mathf.Max(1.2f, fit * 0.55f);
        maxDistance = Mathf.Max(minDistance + 2f, fit * 2.2f);
        distance = fit;
    }

    /// <summary>World bounds from the mesh vertices; immediate and honest.</summary>
    private static bool TryMeasureMeshes(GameObject root, out Bounds bounds)
    {
        bounds = default;
        bool any = false;
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            Mesh mesh = null;
            SkinnedMeshRenderer skinned = renderers[i] as SkinnedMeshRenderer;
            if (skinned != null)
            {
                mesh = skinned.sharedMesh;
            }
            else
            {
                MeshFilter filter = renderers[i].GetComponent<MeshFilter>();
                if (filter != null) mesh = filter.sharedMesh;
            }
            if (mesh == null) continue;
            Matrix4x4 matrix = renderers[i].transform.localToWorldMatrix;
            Vector3[] vertices = mesh.vertices;
            for (int v = 0; v < vertices.Length; v++)
            {
                Vector3 world = matrix.MultiplyPoint3x4(vertices[v]);
                if (!any)
                {
                    bounds = new Bounds(world, Vector3.zero);
                    any = true;
                }
                else
                {
                    bounds.Encapsulate(world);
                }
            }
        }
        return any;
    }

    private static Bounds Measure(Renderer[] renderers)
    {
        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
        return bounds;
    }

    private void ApplyOutfit(GymProgressionState state)
    {
        GymShirtColor shirt = Parse(state.shirt, GymShirtColor.Black);
        GymTopStyle top = Parse(state.top, GymTopStyle.TankTop);
        Texture2D texture = Resources.Load<Texture2D>(PlayerCosmeticLoadout.GetOutfitTexturePath(top, shirt));
        if (texture == null) texture = Resources.Load<Texture2D>("Characters/Textures/player_authored");
        if (texture == null) return;
        SkinnedMeshRenderer[] renderers = model.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            Material instance = renderers[i].material;
            if (instance == null) continue;
            if (instance.HasProperty("_BaseMap")) instance.SetTexture("_BaseMap", texture);
            if (instance.HasProperty("_MainTex")) instance.SetTexture("_MainTex", texture);
            instance.mainTexture = texture;
            renderers[i].sharedMaterial = instance;
        }
    }

    private void ApplyBottoms(GymProgressionState state)
    {
        if (modelBody == null || modelBody.sharedMesh == null) return;
        GymBottomStyle bottom = Parse(state.bottom, GymBottomStyle.Shorts);
        GymShirtColor colour = Parse(state.bottomColor, GymShirtColor.Black);
        PlayerTopShell.Style style = bottom == GymBottomStyle.Pants
            ? PlayerTopShell.Pants : PlayerTopShell.Shorts;
        Texture2D fabric = Resources.Load<Texture2D>(bottom == GymBottomStyle.Pants
            ? PlayerCosmeticLoadout.PantsFabricTexture
            : PlayerCosmeticLoadout.BottomsFabricTexture);
        Color color = PlayerCosmeticLoadout.GetShirtColor(colour);
        Color tint = color / PlayerTopShell.BottomsFabricLevel;
        tint.a = 1f;
        PlayerTopShell.Attach(modelBody, style,
            fabric != null ? ScanTextureMips.Limit(fabric) : null, color * 0.85f, tint);
    }

    private void RequestHeadwear(string name)
    {
        if (model == null || string.IsNullOrEmpty(name)) return;
        if (name == headwearKey) return;
        GymHeadwear headwear = Parse(name, GymHeadwear.None);
        headwearKey = name;
        ClearHeadwear();
        if (headwear == GymHeadwear.None) return;
        Transform headBone = head != null ? head : HeadBone();
        if (headBone == null) return;
        string assetPath = PlayerCosmeticLoadout.GetHeadwearAssetPath(headwear);
        if (string.IsNullOrEmpty(assetPath)) return;
        int version = ++headwearVersion;
        RuntimeGlbModelLoader.Request(assetPath, null, headBone.position, headBone.rotation,
            Vector3.one, "Journal Headwear " + headwear, 0, onLoaded: loaded =>
            {
                if (loaded == null) return;
                if (version != headwearVersion || headBone == null || model == null)
                {
                    Destroy(loaded);
                    return;
                }
                loaded.transform.SetParent(headBone, false);
                loaded.transform.localPosition = Vector3.zero;
                loaded.transform.localRotation = Quaternion.identity;
                loaded.transform.localScale = Vector3.one;
                PlayerCosmeticLoadout.FitHeadwear(loaded, headBone, model.transform,
                    SkullBounds(headBone), headwear);
                Transform[] parts = loaded.GetComponentsInChildren<Transform>(true);
                for (int i = 0; i < parts.Length; i++) parts[i].gameObject.layer = PreviewLayer;
                headwearRoot = loaded;
            });
    }

    private void ClearHeadwear()
    {
        ++headwearVersion;
        if (headwearRoot != null)
        {
            Destroy(headwearRoot);
            headwearRoot = null;
        }
    }

    private Transform HeadBone()
    {
        if (model == null) return null;
        Transform[] parts = model.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < parts.Length; i++)
        {
            string name = parts[i].name;
            if (name == "DEF-spine.005" ||
                name.Equals("head", System.StringComparison.OrdinalIgnoreCase))
            {
                return parts[i];
            }
        }
        return null;
    }

    private Bounds SkullBounds(Transform headBone)
    {
        System.Collections.Generic.List<SkinnedMeshRenderer> renderers =
            new System.Collections.Generic.List<SkinnedMeshRenderer>();
        SkinnedMeshRenderer[] found = model.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        for (int i = 0; i < found.Length; i++)
        {
            if (found[i] == null || PlayerTopShell.IsShellObject(found[i].name)) continue;
            renderers.Add(found[i]);
        }
        return PlayerCosmeticLoadout.SkullBounds(model.transform, headBone, renderers);
    }

    private void LoadIdleClip()
    {
        if (idleClip != null) return;
        Object[] assets = Resources.LoadAll<Object>(PlayerHandRig.DefaultModelResource);
        for (int i = 0; i < assets.Length; i++)
        {
            AnimationClip clip = assets[i] as AnimationClip;
            if (clip == null || clip.name.StartsWith("__preview__", System.StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            if (string.Equals(clip.name, "idle1", System.StringComparison.OrdinalIgnoreCase) ||
                clip.name.EndsWith("|idle1", System.StringComparison.OrdinalIgnoreCase))
            {
                idleClip = clip;
                return;
            }
        }
    }

    /// <summary>Brightest and darkest pixel of the view: proof the body rendered.</summary>
    public Vector2 LuminanceRange()
    {
        if (target == null || model == null || !model.activeInHierarchy) return Vector2.zero;
        RenderTexture previous = RenderTexture.active;
        RenderTexture small = RenderTexture.GetTemporary(96, 120, 0, RenderTextureFormat.ARGB32);
        Graphics.Blit(target, small);
        RenderTexture.active = small;
        Texture2D probe = new Texture2D(96, 120, TextureFormat.RGB24, false);
        probe.ReadPixels(new Rect(0f, 0f, 96f, 120f), 0, 0);
        probe.Apply();
        Color[] pixels = probe.GetPixels();
        float lowest = 1f;
        float highest = 0f;
        for (int i = 0; i < pixels.Length; i++)
        {
            float luminance = pixels[i].r * 0.2126f + pixels[i].g * 0.7152f + pixels[i].b * 0.0722f;
            if (luminance > highest) highest = luminance;
            if (luminance < lowest) lowest = luminance;
        }
        Destroy(probe);
        RenderTexture.active = previous;
        RenderTexture.ReleaseTemporary(small);
        return new Vector2(lowest, highest);
    }

    /// <summary>Verifier seam: turns the body without a mouse.</summary>
    public void RotateForVerification(float degrees)
    {
        yaw = Mathf.Repeat(yaw + degrees, 360f);
    }

    /// <summary>Verifier seam: zooms in or out without a wheel.</summary>
    public void ZoomForVerification(float delta)
    {
        distance = Mathf.Clamp(distance + delta, minDistance, maxDistance);
    }

    private void LateUpdate()
    {
        if (model == null) return;

        // Drag with the left button over the preview, wheel to zoom.
        bool overPreview = view == null ||
            RectTransformUtility.RectangleContainsScreenPoint(view, Input.mousePosition);
        if (overPreview && Input.GetMouseButton(0))
        {
            yaw += Input.GetAxis("Mouse X") * 4.5f;
        }
        float wheel = Input.GetAxis("Mouse ScrollWheel");
        if (Mathf.Abs(wheel) > 0.0001f)
        {
            distance = Mathf.Clamp(distance - wheel * 2.2f, minDistance, maxDistance);
        }
        yaw = Mathf.Repeat(yaw, 360f);

        model.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
        PositionCamera();

        if (idleClip != null)
        {
            phase += Time.unscaledDeltaTime;
            float length = Mathf.Max(0.05f, idleClip.length - 0.01f);
            GymClipSampler.Sample(idleClip, model, Mathf.Repeat(phase, length));
        }
    }

    private void PositionCamera()
    {
        if (previewCamera == null) return;
        Vector3 focus = new Vector3(StudioOrigin.x, focusY, StudioOrigin.z);
        previewCamera.transform.position = focus + Vector3.back * distance;
        previewCamera.transform.LookAt(focus);
    }

    private static T Parse<T>(string text, T fallback) where T : struct, System.Enum
    {
        if (!string.IsNullOrEmpty(text) && System.Enum.TryParse(text, true, out T value) &&
            System.Enum.IsDefined(typeof(T), value))
        {
            return value;
        }
        return fallback;
    }

    private void OnDestroy()
    {
        ClearHeadwear();
        if (rig != null) Destroy(rig);
        if (previewCamera != null) previewCamera.targetTexture = null;
        if (target != null)
        {
            target.Release();
            Destroy(target);
        }
    }
}
