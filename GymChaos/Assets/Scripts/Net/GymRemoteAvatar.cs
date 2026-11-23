using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Another player's body in this room. It is the same authored player model
/// the owner plays as (their class body, wearing the outfit they picked), placed
/// at the interpolated pose and played with the game's own walk, run and idle
/// clips, so both screens show the same person doing the same thing. A capsule
/// remains only as a fallback if the model cannot be loaded.
/// </summary>
public sealed class GymRemoteAvatar : MonoBehaviour
{
    private const float WalkBobMetres = 0.05f;
    private const float WalkCycleHz = 2.2f;
    private const float RunClipSpeed = 5.4f;
    // A body lying on its side rests its thickness above the floor.
    private const float CorpseLiftMetres = 0.25f;
    private const float WalkClipSpeed = 3.4f;

    private readonly GymNetInterpolator interpolator = new GymNetInterpolator();
    private Transform body;
    private GameObject model;
    private SkinnedMeshRenderer modelBody;
    private Material material;
    private float cycle;
    private float walkPhase;
    private Vector3 modelBasePosition;
    private Quaternion modelBaseRotation = Quaternion.identity;
    private int lookKey = int.MinValue;

    private SkinnedMeshRenderer bottomShell;
    private bool bottomPending;
    private GymBottomStyle bottomShown = GymBottomStyle.Shorts;
    private GymShirtColor bottomColorShown = GymShirtColor.Black;
    private GymBottomStyle bottomStyleWorn = GymBottomStyle.Shorts;
    private GymShirtColor bottomColorWorn = GymShirtColor.Black;

    private GameObject headwearRoot;
    private GymHeadwear headwearShown = GymHeadwear.None;
    private GymHeadwear headwearWorn = GymHeadwear.None;
    private bool headwearLoading;
    private int headwearVersion;

    private ScreenSpaceCharacterLabel label;

    private static AnimationClip walkClip;
    private static AnimationClip runClip;
    private static AnimationClip idleClip;
    private static AnimationClip punchLeftClip;
    private static AnimationClip punchRightClip;
    private static AnimationClip throwLightClip;
    private static AnimationClip throwHardClip;
    private static AnimationClip jumpClip;
    private static AnimationClip crouchClip;
    private static AnimationClip drivingClip;
    private static bool clipsLoaded;
    private const float PunchSeconds = 0.46f;
    private const float ThrowSeconds = 0.68f;
    private int actionShown;
    private float actionClock;
    /// <summary>The action clip the body is playing right now (a <see cref="GymNetAction"/> id).</summary>
    public int ActionPlaying => actionShown;

    public int PlayerId { get; private set; }
    public int PoseCount => interpolator.Count;
    /// <summary>True while the player behind this avatar is dead: it lies where it fell.</summary>
    public bool IsCorpse { get; private set; }
    /// <summary>True once the real player model is under this avatar.</summary>
    public bool HasRealBody => model != null;
    /// <summary>The lowest point of the drawn body: where its feet stand.</summary>
    public float FeetY => modelBody != null ? modelBody.bounds.min.y : transform.position.y;
    public string ClassIdShown { get; private set; }
    /// <summary>The outfit texture this body wears; empty while a capsule stands in.</summary>
    public string OutfitTextureShown { get; private set; }
    /// <summary>True when the body is played by the authored clips.</summary>
    public bool HasAnimatedBody => model != null && walkClip != null;
    /// <summary>The hat the owner picked, as it arrived on the wire.</summary>
    public GymHeadwear HeadwearShown => headwearShown;
    /// <summary>True once that hat is actually worn by this body.</summary>
    public bool HasHeadwearRenderer => HasVisibleRenderer(headwearRoot);
    /// <summary>The colour of the shorts or pants the owner picked.</summary>
    public GymShirtColor BottomColorShown => bottomColorShown;
    public GymBottomStyle BottomShown => bottomShown;
    /// <summary>True once the chosen bottoms are drawn over this body.</summary>
    public bool HasBottomShell => bottomShell != null && bottomShell.enabled &&
        bottomShell.gameObject.activeInHierarchy;
    /// <summary>The hat that is actually on this body's head (None until it lands).</summary>
    public GymHeadwear HeadwearWorn => headwearWorn;
    /// <summary>The bottoms cloth that is actually drawn, and in which colour.</summary>
    public GymBottomStyle BottomStyleWorn => bottomStyleWorn;
    public GymShirtColor BottomColorWorn => bottomColorWorn;

    public static GymRemoteAvatar Create(int playerId)
    {
        GameObject root = new GameObject("Remote Player " + playerId);
        GymRemoteAvatar avatar = root.AddComponent<GymRemoteAvatar>();
        avatar.PlayerId = playerId;
        avatar.Build();
        return avatar;
    }

    private void Build()
    {
        // Name and health bar like any fighter: the label follows the body and hides behind walls.
        label = gameObject.AddComponent<ScreenSpaceCharacterLabel>();
        label.ConfigureStandalone(null, DisplayName(false), 0.25f);
    }

    public void AddPose(double arrival, GymNetPose pose)
    {
        interpolator.Add(arrival, pose);
        lastPose = pose;
        hasPose = true;
        EnsureLook(pose);
    }

    private GymNetPose lastPose;
    private bool hasPose;
    /// <summary>The owner's name and look (cleaned); null until the first look message.</summary>
    public GymCharacterSave LookShown { get; private set; }
    public string NameShown => DisplayName(false);

    /// <summary>A peer's name and look arrived: relabel, and rebuild a remade body when it changed.</summary>
    public void ApplyLook(GymCharacterSave look)
    {
        if (look == null) return;
        bool changed = LookShown == null || JsonUtility.ToJson(LookShown) != JsonUtility.ToJson(look);
        LookShown = look;
        if (!changed) return;
        if (label != null) label.ConfigureStandalone(modelBody, DisplayName(IsCorpse), 0.25f);
        if (hasPose && (PlayerAppearance.UsesRemakeBody || look.heightCm != PlayerAppearance.DefaultMaleHeight))
            Rebuild(lastPose);
    }

    // ------------------------------------------------------------------ look

    /// <summary>
    /// The owner sends its class and outfit with every pose; a change of either
    /// rebuilds the body once, so a locker visit shows up on both screens.
    /// </summary>
    private void EnsureLook(GymNetPose pose)
    {
        int key = pose.classIndex * 1000000 + pose.shirt * 100000 +
            pose.top * 10000 + pose.bottom * 1000 + pose.bottomColor * 10 + pose.headwear;
        if (key == lookKey && (model != null || body != null))
        {
            return;
        }
        lookKey = key;
        Rebuild(pose);
    }

    private void Rebuild(GymNetPose pose)
    {
        ClearHeadwear();
        bottomShell = null;
        bottomPending = false;
        PlayerAppearance.ReleaseMaterials(model);
        if (model != null) Destroy(model);
        model = null;
        modelBody = null;
        if (body != null)
        {
            Destroy(body.gameObject);
            body = null;
        }
        if (material != null)
        {
            Destroy(material);
            material = null;
        }

        GameObject prefab = LoadModel(pose.classIndex);
        if (prefab == null)
        {
            BuildFallback();
            return;
        }
        model = Instantiate(prefab, transform);
        model.name = "Remote Player Body";
        // The local player model renders on a mirror-only layer so the first
        // person camera never sees it; a remote body has to be in the world.
        Transform[] parts = model.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < parts.Length; i++) parts[i].gameObject.layer = 0;
        Animator[] animators = model.GetComponentsInChildren<Animator>(true);
        for (int i = 0; i < animators.Length; i++) animators[i].enabled = false;

        modelBody = model.GetComponentInChildren<SkinnedMeshRenderer>(true);
        if (PlayerAppearance.UsesRemakeBody)
        {
            PlayerAppearance.ApplyBody(model, LookShown ?? DefaultLook,
                GymClassCatalog.TryGet(ClassIdFor(pose.classIndex) ?? string.Empty, out GymClassDefinition definition)
                    ? definition : null);
            foreach (SkinnedMeshRenderer part in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (part.name == PlayerAppearance.BodyMeshName) modelBody = part;
        }
        FitModel(prefab, pose);
        ApplyOutfit(pose);
        ApplyBottoms(pose);
        LoadClips();
        modelBasePosition = model.transform.localPosition;
        modelBaseRotation = model.transform.localRotation;
        ApplyHeadwear(pose);
        label.ConfigureStandalone(modelBody, DisplayName(IsCorpse), 0.25f);
        Debug.Log("GYMCHAOS_REMOTE_BODY_READY avatar=" + PlayerId + " class=" + ClassIdShown +
            " headwear=" + headwearShown + " bottom=" + bottomShown + "/" + bottomColorShown);
    }

    private static readonly GymCharacterSave DefaultLook = new GymCharacterSave();

    private static string ClipResource =>
        PlayerAppearance.UsesRemakeBody ? PlayerAppearance.RemakeModel : PlayerHandRig.DefaultModelResource;

    private static GameObject LoadModel(int classIndex)
    {
        if (PlayerAppearance.UsesRemakeBody)
        {
            GameObject remade = Resources.Load<GameObject>(PlayerAppearance.RemakeModel);
            if (remade != null) return remade;
        }
        string classId = ClassIdFor(classIndex);
        if (!string.IsNullOrEmpty(classId))
        {
            GameObject variant = Resources.Load<GameObject>("Player/Classes/player_" + classId);
            if (variant != null) return variant;
        }
        return Resources.Load<GameObject>(PlayerHandRig.DefaultModelResource);
    }

    private static string ClassIdFor(int classIndex)
    {
        System.Collections.Generic.IReadOnlyList<GymClassDefinition> classes = GymClassCatalog.All;
        if (classIndex < 0 || classIndex >= classes.Count) return null;
        return classes[classIndex].id;
    }

    /// <summary>The authored body is fitted to the same gameplay height as the owner's.</summary>
    private void FitModel(GameObject prefab, GymNetPose pose)
    {
        ClassIdShown = ClassIdFor(pose.classIndex);
        Renderer[] renderers = model.GetComponentsInChildren<Renderer>(false);
        if (renderers.Length == 0) return;
        Bounds bounds = Measure(renderers);
        if (bounds.size.y < 0.01f) return;
        float scale = ExternalRiggedCharacterVisual.StandardGameplayHeight * PlayerAppearance.HeightScale(LookShown) /
            bounds.size.y;
        model.transform.localScale *= scale;
        bounds = Measure(renderers);
        // Feet on the avatar's floor point, not floating above it.
        model.transform.position += Vector3.up * (transform.position.y - bounds.min.y);
    }

    // The body alone: remade hair and outfit parts would add height and shrink the fit.
    private static Bounds Measure(Renderer[] renderers)
    {
        Bounds bounds = renderers[0].bounds;
        bool any = false;
        for (int i = 0; i < renderers.Length; i++)
        {
            if (PlayerAppearance.IsPartName(renderers[i].name)) continue;
            if (!any) bounds = renderers[i].bounds;
            else bounds.Encapsulate(renderers[i].bounds);
            any = true;
        }
        return bounds;
    }

    private void ApplyOutfit(GymNetPose pose)
    {
        GymShirtColor shirt = Sane((GymShirtColor)pose.shirt, GymShirtColor.Black);
        GymTopStyle top = Sane((GymTopStyle)pose.top, GymTopStyle.TankTop);
        Texture2D texture = Resources.Load<Texture2D>(PlayerCosmeticLoadout.GetOutfitTexturePath(top, shirt));
        if (PlayerAppearance.UsesRemakeBody)
        {
            PlayerAppearance.SetOutfit(model, top, shirt, Sane((GymBottomStyle)pose.bottom, GymBottomStyle.Shorts),
                Sane((GymShirtColor)pose.bottomColor, GymShirtColor.Black), (LookShown ?? DefaultLook).hair,
                Sane((GymHeadwear)pose.headwear, GymHeadwear.None));
            OutfitTextureShown = texture != null ? texture.name : string.Empty;
            return;
        }
        if (texture == null) texture = Resources.Load<Texture2D>("Characters/Textures/player_authored");
        if (texture == null) return;
        SkinnedMeshRenderer[] renderers = model.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            // Instance per remote body: the owner's own material stays untouched.
            Material instance = renderers[i].material;
            if (instance == null) continue;
            if (instance.HasProperty("_BaseMap")) instance.SetTexture("_BaseMap", texture);
            if (instance.HasProperty("_MainTex")) instance.SetTexture("_MainTex", texture);
            instance.mainTexture = texture;
            renderers[i].sharedMaterial = instance;
            renderers[i].shadowCastingMode = ShadowCastingMode.On;
        }
        OutfitTextureShown = texture.name;
    }

    /// <summary>
    /// The shorts or pants the owner picked, in their colour: the same cloth
    /// shell the local player wears over its scan body, so the bottoms read the
    /// same on both screens instead of the scan's own painted shorts.
    /// </summary>
    private void ApplyBottoms(GymNetPose pose)
    {
        bottomShown = Sane((GymBottomStyle)pose.bottom, GymBottomStyle.Shorts);
        bottomColorShown = Sane((GymShirtColor)pose.bottomColor, GymShirtColor.Black);
        bottomShell = null;
        bottomPending = false;
        AttachBottomShell();
    }

    private void AttachBottomShell()
    {
        if (modelBody == null || modelBody.sharedMesh == null) return;
        // The remade body wears authored outfit parts (PlayerAppearance.SetOutfit), no shells.
        if (PlayerAppearance.UsesRemakeBody) return;
        PlayerTopShell.Style style = bottomShown == GymBottomStyle.Pants
            ? PlayerTopShell.Pants : PlayerTopShell.Shorts;
        Texture2D fabric = Resources.Load<Texture2D>(bottomShown == GymBottomStyle.Pants
            ? PlayerCosmeticLoadout.PantsFabricTexture
            : PlayerCosmeticLoadout.BottomsFabricTexture);
        Color color = PlayerCosmeticLoadout.GetShirtColor(bottomColorShown);
        Color tint = color / PlayerTopShell.BottomsFabricLevel;
        tint.a = 1f;
        bottomShell = PlayerTopShell.Attach(modelBody, style,
            fabric != null ? ScanTextureMips.Limit(fabric) : null, color * 0.85f, tint);
        // Shells are lofted on a worker thread; Update retries until the mesh lands.
        bottomPending = bottomShell == null &&
            !PlayerTopShell.IsSettled(modelBody.sharedMesh, style);
        if (bottomShell != null)
        {
            bottomStyleWorn = bottomShown;
            bottomColorWorn = bottomColorShown;
            Debug.Log("GYMCHAOS_REMOTE_BOTTOMS_READY avatar=" + PlayerId + " style=" + bottomStyleWorn +
                " colour=" + bottomColorWorn + " renderer=" + bottomShell.name);
        }
    }

    /// <summary>The hat the owner picked, worn on this body's own head bone.</summary>
    private void ApplyHeadwear(GymNetPose pose)
    {
        headwearShown = Sane((GymHeadwear)pose.headwear, GymHeadwear.None);
        RequestHeadwear();
    }

    private void RequestHeadwear()
    {
        if (model == null || headwearShown == GymHeadwear.None ||
            headwearRoot != null || headwearLoading)
        {
            return;
        }
        string assetPath = PlayerCosmeticLoadout.GetHeadwearAssetPath(headwearShown);
        if (string.IsNullOrEmpty(assetPath)) return;
        Transform head = HeadBone();
        if (head == null) return;
        int version = ++headwearVersion;
        GymHeadwear requested = headwearShown;
        headwearLoading = true;
        RuntimeGlbModelLoader.Request(
            assetPath,
            null,
            head.position,
            head.rotation,
            Vector3.one,
            "Remote Headwear " + requested,
            0,
            onLoaded: loaded =>
            {
                headwearLoading = false;
                if (loaded == null)
                {
                    return;
                }
                if (version != headwearVersion || head == null || model == null)
                {
                    Destroy(loaded);
                    return;
                }
                loaded.transform.SetParent(head, false);
                loaded.transform.localPosition = Vector3.zero;
                loaded.transform.localRotation = Quaternion.identity;
                loaded.transform.localScale = Vector3.one;
                PlayerCosmeticLoadout.FitHeadwear(loaded, head, transform, SkullBounds(head), requested);
                Transform[] parts = loaded.GetComponentsInChildren<Transform>(true);
                for (int i = 0; i < parts.Length; i++) parts[i].gameObject.layer = 0;
                headwearRoot = loaded;
                headwearWorn = requested;
                Debug.Log("GYMCHAOS_REMOTE_HEADWEAR_READY avatar=" + PlayerId + " type=" + requested +
                    " parent=" + head.name + " world=" + loaded.transform.position);
            });
    }

    private void ClearHeadwear()
    {
        ++headwearVersion;
        headwearLoading = false;
        headwearWorn = GymHeadwear.None;
        if (headwearRoot != null)
        {
            Destroy(headwearRoot);
            headwearRoot = null;
        }
    }

    private Transform HeadBone()
    {
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

    private Bounds SkullBounds(Transform head)
    {
        System.Collections.Generic.List<SkinnedMeshRenderer> renderers =
            new System.Collections.Generic.List<SkinnedMeshRenderer>();
        SkinnedMeshRenderer[] found = model.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        for (int i = 0; i < found.Length; i++)
        {
            if (found[i] == null || PlayerTopShell.IsShellObject(found[i].name)) continue;
            renderers.Add(found[i]);
        }
        return PlayerCosmeticLoadout.SkullBounds(transform, head, renderers);
    }

    private static bool HasVisibleRenderer(GameObject root)
    {
        if (root == null || !root.activeInHierarchy) return false;
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] != null && renderers[i].enabled &&
                renderers[i].gameObject.activeInHierarchy)
            {
                return true;
            }
        }
        return false;
    }

    private static T Sane<T>(T value, T fallback) where T : System.Enum =>
        System.Enum.IsDefined(typeof(T), value) ? value : fallback;

    private void LoadClips()
    {
        if (clipsLoaded) return;
        clipsLoaded = true;
        Object[] assets = Resources.LoadAll<Object>(ClipResource);
        for (int i = 0; i < assets.Length; i++)
        {
            AnimationClip clip = assets[i] as AnimationClip;
            if (clip == null || clip.name.StartsWith("__preview__", System.StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            if (walkClip == null && Matches(clip, "walking")) walkClip = clip;
            else if (runClip == null && Matches(clip, "running")) runClip = clip;
            else if (idleClip == null && Matches(clip, "idle1")) idleClip = clip;
            else if (punchLeftClip == null && Matches(clip, "punch_left")) punchLeftClip = clip;
            else if (punchRightClip == null && Matches(clip, "punch_right")) punchRightClip = clip;
            else if (throwLightClip == null && Matches(clip, "throw_frisbee")) throwLightClip = clip;
            else if (throwHardClip == null && Matches(clip, "throw_object_hard")) throwHardClip = clip;
            else if (jumpClip == null && Matches(clip, "jumping")) jumpClip = clip;
            else if (crouchClip == null && Matches(clip, "crouched_walking")) crouchClip = clip;
            else if (drivingClip == null && Matches(clip, "vehicle_driving")) drivingClip = clip;
        }
    }

    private static bool Matches(AnimationClip clip, string name) =>
        string.Equals(clip.name, name, System.StringComparison.OrdinalIgnoreCase) ||
        clip.name.EndsWith("|" + name, System.StringComparison.OrdinalIgnoreCase);

    private void BuildFallback()
    {
        Color[] tints = { PersonaMenuStyle.Accent, PersonaMenuStyle.Gold, PersonaMenuStyle.Navy, PersonaMenuStyle.Ink };
        material = GymMaterialLibrary.NewLit("Remote Player " + PlayerId,
            tints[PlayerId % tints.Length], 0f, 0.35f);
        GameObject capsule = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        capsule.name = "Body";
        capsule.transform.SetParent(transform, false);
        capsule.transform.localScale = new Vector3(0.6f, 0.9f, 0.6f);
        Destroy(capsule.GetComponent<Collider>());
        MeshRenderer renderer = capsule.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.On;
        body = capsule.transform;
        label.ConfigureStandalone(renderer, DisplayName(IsCorpse), 0.25f);
    }

    private string DisplayName(bool dead) =>
        (LookShown != null && LookShown.IsCustomized ? LookShown.username : "PLAYER " + (PlayerId + 1)) +
        (dead ? " (DEAD)" : string.Empty);

    // ------------------------------------------------------------------ pose

    private void Update()
    {
        if (!interpolator.TrySample(Time.unscaledTimeAsDouble, out GymNetPose pose)) return;
        transform.SetPositionAndRotation(pose.Position, Quaternion.Euler(0f, pose.r, 0f));
        if (lookKey == int.MinValue) EnsureLook(pose);

        bool dead = pose.d > 0.5f;
        if (dead != IsCorpse)
        {
            IsCorpse = dead;
            // Lying down: the body is rolled onto its side and rests on the floor.
            Transform target = model != null ? model.transform : body;
            if (target != null)
            {
                target.localRotation = dead
                    ? modelBaseRotation * Quaternion.Euler(0f, 0f, 90f)
                    : modelBaseRotation;
                if (model == null) target.localPosition = dead ? modelBasePosition + Vector3.down * 0.55f : modelBasePosition;
            }
            label.ConfigureStandalone(modelBody != null ? (Renderer)modelBody : FallbackRenderer(),
                DisplayName(dead), 0.25f);
        }
        // The wire's root is the controller centre: the body's feet go down to the floor.
        if (model != null)
        {
            model.transform.localPosition = modelBasePosition + Vector3.down * pose.f +
                (dead ? Vector3.up * CorpseLiftMetres : Vector3.zero);
        }
        if (dead)
        {
            label.StandaloneHealthFraction = -1f;
            return;
        }
        label.StandaloneHealthFraction = pose.h < 0.999f ? Mathf.Clamp01(pose.h) : -1f;
        if (model != null && (bottomPending || (headwearShown != GymHeadwear.None && headwearRoot == null)))
        {
            RetryLookParts();
        }
        // The browser runs scripts on one thread: a body nobody sees is posed every fourth frame.
        // A remote body never decides a hit (the host does), so a stale pose costs nothing.
        if (modelBody != null && !modelBody.isVisible && Application.platform == RuntimePlatform.WebGLPlayer &&
            Time.frameCount % 4 != 0)
        {
            return;
        }
        Animate(pose);
    }

    /// <summary>Cloth shells and wearables arrive asynchronously; finish them here.</summary>
    private void RetryLookParts()
    {
        if (bottomPending) AttachBottomShell();
        RequestHeadwear();
    }

    private Renderer FallbackRenderer() => body != null ? body.GetComponent<Renderer>() : null;

    /// <summary>
    /// The authored locomotion, at the speed the owner is actually moving:
    /// walking below a run, running above it, idle when standing still.
    /// </summary>
    private void Animate(GymNetPose pose)
    {
        bool moving = pose.s > 0.3f;
        if (model != null && walkClip != null && PlayAction(pose, moving)) return;
        if (model != null && walkClip != null)
        {
            actionShown = GymNetAction.None;
            AnimationClip clip = !moving
                ? (idleClip != null ? idleClip : walkClip)
                : pose.s > RunClipSpeed && runClip != null ? runClip : walkClip;
            walkPhase += Time.deltaTime * (moving
                ? Mathf.Clamp(pose.s / WalkClipSpeed, 0.75f, 2.2f)
                : 0.7f);
            float length = Mathf.Max(0.05f, clip.length - 0.01f);
            GymClipSampler.Sample(clip, model, Mathf.Repeat(walkPhase, length));
            return;
        }
        if (body == null) return;
        if (moving) cycle += Time.deltaTime * WalkCycleHz;
        body.localPosition = new Vector3(0f,
            moving ? Mathf.Abs(Mathf.Sin(cycle * Mathf.PI)) * WalkBobMetres : 0f, 0f);
    }

    /// <summary>The owner's punch, throw, jump, crouch or drive, played from its own clip.</summary>
    private bool PlayAction(GymNetPose pose, bool moving)
    {
        int action = pose.a;
        AnimationClip clip = ActionClip(action);
        if (clip == null)
        {
            actionShown = GymNetAction.None;
            return false;
        }
        bool oneShot = action == GymNetAction.PunchLeft || action == GymNetAction.PunchRight ||
            action == GymNetAction.ThrowLight || action == GymNetAction.ThrowHard || action == GymNetAction.Jump;
        float length = Mathf.Max(0.05f, clip.length - 0.001f);
        if (oneShot)
        {
            float seconds = action == GymNetAction.Jump ? length
                : action == GymNetAction.ThrowLight || action == GymNetAction.ThrowHard ? ThrowSeconds : PunchSeconds;
            // The wire carries about twelve phases a second: in between, the clip runs on its own clock.
            if (action != actionShown || Mathf.Abs(actionClock - pose.p) > 0.2f) actionClock = pose.p;
            else actionClock = Mathf.Min(1f, actionClock + Time.deltaTime / seconds);
            actionShown = action;
            GymClipSampler.Sample(clip, model, Mathf.Clamp01(actionClock) * length);
            return true;
        }
        actionShown = action;
        // Driving and crouching loop; a still crouch holds its first frame.
        if (action == GymNetAction.Driving || moving) walkPhase += Time.deltaTime;
        GymClipSampler.Sample(clip, model, Mathf.Repeat(walkPhase, length));
        return true;
    }

    private static AnimationClip ActionClip(int action)
    {
        switch (action)
        {
            case GymNetAction.PunchLeft: return punchLeftClip;
            case GymNetAction.PunchRight: return punchRightClip;
            case GymNetAction.ThrowLight: return throwLightClip;
            case GymNetAction.ThrowHard: return throwHardClip;
            case GymNetAction.Jump: return jumpClip;
            case GymNetAction.Crouch: return crouchClip;
            case GymNetAction.Driving: return drivingClip;
            default: return null;
        }
    }

    private void OnDestroy()
    {
        ClearHeadwear();
        if (material != null) Destroy(material);
    }
}
