using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.Rendering;

/// <summary>
/// Jolly Dog himself. One prepared, hidden actor reused for every visit:
/// falls fast from the sky, lands, runs to each repair, casts one magic_sign
/// per repair, then walks out and flies off once there is open sky above.
/// He has no EnemyFighter, health or hitboxes, so nothing can hurt him.
/// All clips are blended through a manual PlayableGraph mixer so every
/// transition is a crossfade instead of a pose pop.
/// </summary>
[DefaultExecutionOrder(900)]
public sealed class GymFixerAgent : MonoBehaviour
{
    public enum FixerState
    {
        Hidden,
        Falling,
        Landing,
        Running,
        Casting,
        Exiting,
        Flying
    }

    public const string DisplayName = "Lollipop Larry";
    public const float GameplayHeight = 1.4f;
    private const string ResourcePath = "Characters/Fixer/jolly_dog_authored";
    private const string TexturePath = "Characters/Textures/jolly_dog";
    private const float SpawnHeight = 70f;
    private const float FallAcceleration = 45f;
    private const float MaxFallSpeed = 34f;
    private const float BrakeHeight = 6.5f;
    private const float BrakeSpeed = 4.5f;
    private const float RunSpeed = 3.6f;
    private const float TurnSpeed = 600f;
    private const float CastRange = 2.4f;
    private const float FixMoment = 0.6f;
    private const float FlyAcceleration = 24f;
    private const float MaxFlySpeed = 30f;
    private const float DespawnHeight = 90f;
    private const float MaxVisitSeconds = 300f;
    private float visitStartedAt;
    private readonly List<FixerIssue> visitList = new List<FixerIssue>();

    private const int ClipFalling = 0;
    private const int ClipLanding = 1;
    private const int ClipRunning = 2;
    private const int ClipMagicA = 3;
    private const int ClipFlying = 4;
    private const int ClipMagicB = 5;
    private const int ClipCount = 6;
    private static readonly string[] ClipStems =
        { "falling", "landing", "running", "magic_sign", "flying", "magic_sign" };
    private static readonly bool[] ClipLoops = { true, false, true, false, true, false };

    private GymFixerDirector director;
    private Transform pivot;
    private Transform model;
    private Animator animator;
    private PlayableGraph graph;
    private AnimationMixerPlayable mixer;
    private readonly AnimationClip[] clips = new AnimationClip[ClipCount];
    private readonly float[] clipTimes = new float[ClipCount];
    private readonly float[] weights = new float[ClipCount];
    private int targetClip = -1;
    private float fadeSeconds = 0.25f;
    private SkinnedMeshRenderer bodyRenderer;
    private GymFixerMagicEffect magic;
    private Transform footLeft;
    private Transform footRight;
    private Transform head;
    private Transform hand;
    private float soleOffset;
    private float pivotBaseY;
    private float groundLift;
    private float fallingLowest;
    private float landingStartLowest;
    private CapsuleCollider capsule;
    private Rigidbody body;

    private readonly GymFixerNavGrid navGrid = new GymFixerNavGrid();
    private Coroutine gridRoutine;
    private readonly List<Vector3> path = new List<Vector3>();
    private int pathIndex;
    private readonly List<FixerIssue> work = new List<FixerIssue>();
    private FixerIssue currentIssue;
    private bool fixApplied;
    private int magicInput = ClipMagicB;
    private FixerState state = FixerState.Hidden;
    private float groundY;
    private float verticalSpeed;
    private float landingRootOffset;
    private bool impactPlayed;
    private Vector3 landingPoint;
    private bool doorRequested;
    private float flightTime;
    private float stateEnteredAt;
    private Vector3[] lastBonePositions;
    private Transform[] trackedBones;
    private float stuckTimer;
    private float lastWaypointDistance;

    // Verification evidence.
    public readonly List<string> StateHistory = new List<string>();
    public readonly List<float> CastGlowPeaks = new List<float>();
    public FixerState State => state;
    public int CastCount { get; private set; }
    public int VisitIssueCount { get; private set; }
    public float MaxPoseSpeed { get; private set; }
    public float MaxGlowWhileNotCasting { get; private set; }
    public bool TakeoffHadOpenSky { get; private set; }
    public bool TookOffOutside { get; private set; }
    public float PeakAltitude { get; private set; }
    public float MeasuredHeight { get; private set; }
    public float GlowEnvelope => magic != null ? magic.Envelope : 0f;
    public float RunningWeight => weights[ClipRunning];
    public GymFixerNavGrid NavGrid => navGrid;
    public Vector3 LandingPoint => landingPoint;
    public float GroundY => groundY;
    public int PendingWork => work.Count + (currentIssue != null ? 1 : 0);
    public bool IsVisible => gameObject.activeInHierarchy;

    // --------------------------------------------------------------- setup

    public bool Prepare(GymFixerDirector owner)
    {
        director = owner;
        float prepareStarted = Time.realtimeSinceStartup;
        GameObject prefab = Resources.Load<GameObject>(ResourcePath);
        if (prefab == null)
        {
            Debug.LogError($"Jolly Dog model is missing at Resources/{ResourcePath}.fbx", this);
            return false;
        }

        pivot = new GameObject("Jolly Dog Pivot").transform;
        pivot.SetParent(transform, false);
        model = Instantiate(prefab, pivot).transform;
        model.name = "Jolly Dog Authored Rig";
        model.localPosition = Vector3.zero;
        model.localRotation = Quaternion.identity;

        animator = model.GetComponent<Animator>();
        if (animator == null)
        {
            animator = model.gameObject.AddComponent<Animator>();
        }
        animator.runtimeAnimatorController = null;
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        animator.enabled = true;

        SkinnedMeshRenderer glowShell = null;
        SkinnedMeshRenderer[] renderers = model.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            SkinnedMeshRenderer renderer = renderers[i];
            if (renderer.name.IndexOf("lollipop_glow", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                glowShell = renderer;
            }
            else if (bodyRenderer == null || renderer.sharedMesh != null &&
                bodyRenderer.sharedMesh != null &&
                renderer.sharedMesh.vertexCount > bodyRenderer.sharedMesh.vertexCount)
            {
                bodyRenderer = renderer;
            }
        }
        if (bodyRenderer == null)
        {
            Debug.LogError("Jolly Dog FBX has no body renderer.", this);
            return false;
        }
        ApplyBodyMaterial();

        Transform[] bones = model.GetComponentsInChildren<Transform>(true);
        hand = FindBone(bones, "DEF-hand.R");
        footLeft = FindBone(bones, "DEF-foot.L");
        footRight = FindBone(bones, "DEF-foot.R");
        head = FindBone(bones, "DEF-spine.005");
        if (hand == null || footLeft == null || footRight == null)
        {
            Debug.LogError("Jolly Dog FBX is missing hand/foot deform bones.", this);
            return false;
        }

        if (!LoadClips())
        {
            return false;
        }
        BuildGraph();
        FitToGameplayHeight();
        model.gameObject.AddComponent<ScreenSpaceCharacterLabel>().ConfigureStandalone(
            bodyRenderer, DisplayName, GameplayHeight * ScreenSpaceCharacterLabel.NameHeightFactor);

        GameObject magicObject = new GameObject("Jolly Dog Lollipop Magic");
        magicObject.transform.SetParent(transform, false);
        magic = magicObject.AddComponent<GymFixerMagicEffect>();
        SnapTo(ClipMagicA, 0f);
        Bounds candy = glowShell != null ? BakeBounds(glowShell) : new Bounds(hand.position, Vector3.one * 0.1f);
        magic.Configure(glowShell, hand, candy);

        capsule = gameObject.AddComponent<CapsuleCollider>();
        capsule.radius = 0.3f;
        capsule.height = GameplayHeight;
        capsule.center = new Vector3(0f, GameplayHeight * 0.5f, 0f);
        capsule.enabled = false;
        body = gameObject.AddComponent<Rigidbody>();
        body.isKinematic = true;
        body.useGravity = false;
        body.interpolation = RigidbodyInterpolation.None;

        gameObject.SetActive(false);
        Debug.Log(
            $"GYMCHAOS_FIXER_PREPARED height={MeasuredHeight:F3} clips={DescribeClips()} " +
            $"glowShell={(glowShell != null)} fallLow={fallingLowest:F3} landLow={landingStartLowest:F3} " +
            $"ms={(Time.realtimeSinceStartup - prepareStarted) * 1000f:F0}",
            this);
        return true;
    }

    private void ApplyBodyMaterial()
    {
        Shader shader = Shader.Find("GymChaos/BodybuilderUnlit");
        Texture2D texture = Resources.Load<Texture2D>(TexturePath);
        if (shader == null)
        {
            shader = Shader.Find("Universal Render Pipeline/Unlit");
        }
        Material material = new Material(shader) { name = "Jolly Dog Body" };
        material.SetColor("_BaseColor", Color.white);
        material.SetColor("_Color", Color.white);
        // The scan's colour is its atlas multiplied by its vertex colours.
        material.SetFloat("_UseVertexColor", 1f);
        if (texture != null)
        {
            Texture limited = ScanTextureMips.Limit(texture);
            material.SetTexture("_BaseMap", limited);
            material.SetTexture("_MainTex", limited);
            bodyRenderer.sharedMesh = ScanUvInset.Apply(
                bodyRenderer.sharedMesh, ScanUvInset.TextureSize(texture));
        }
        Material[] materials = new Material[Mathf.Max(1, bodyRenderer.sharedMaterials.Length)];
        for (int i = 0; i < materials.Length; i++)
        {
            materials[i] = material;
        }
        bodyRenderer.sharedMaterials = materials;
        bodyRenderer.updateWhenOffscreen = true;
        bodyRenderer.shadowCastingMode = ShadowCastingMode.Off;
        bodyRenderer.receiveShadows = false;
    }

    private bool LoadClips()
    {
        UnityEngine.Object[] assets = Resources.LoadAll<UnityEngine.Object>(ResourcePath);
        for (int i = 0; i < ClipCount; i++)
        {
            for (int a = 0; a < assets.Length; a++)
            {
                AnimationClip clip = assets[a] as AnimationClip;
                if (clip == null || clip.name.StartsWith("__preview__", StringComparison.Ordinal))
                {
                    continue;
                }
                if (string.Equals(clip.name, ClipStems[i], StringComparison.OrdinalIgnoreCase) ||
                    clip.name.EndsWith("|" + ClipStems[i], StringComparison.OrdinalIgnoreCase))
                {
                    clips[i] = clip;
                    break;
                }
            }
            if (clips[i] == null)
            {
                Debug.LogError($"Jolly Dog clip '{ClipStems[i]}' is missing.", this);
                return false;
            }
        }
        return true;
    }

    private void BuildGraph()
    {
        graph = PlayableGraph.Create("Jolly Dog Mixer");
        graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
        mixer = AnimationMixerPlayable.Create(graph, ClipCount);
        for (int i = 0; i < ClipCount; i++)
        {
            AnimationClipPlayable playable = AnimationClipPlayable.Create(graph, clips[i]);
            playable.SetApplyFootIK(false);
            playable.SetApplyPlayableIK(false);
            graph.Connect(playable, 0, mixer, i);
            mixer.SetInputWeight(i, 0f);
        }
        AnimationPlayableOutput output = AnimationPlayableOutput.Create(graph, "Jolly Dog", animator);
        output.SetSourcePlayable(mixer);
    }

    private void FitToGameplayHeight()
    {
        SnapTo(ClipMagicA, 0f);
        Bounds standing = BakeBounds(bodyRenderer);
        float scale = GameplayHeight / Mathf.Max(0.01f, standing.size.y);
        model.localScale *= scale;
        SnapTo(ClipMagicA, 0f);
        standing = BakeBounds(bodyRenderer);
        MeasuredHeight = standing.size.y;
        // Body centre pivot so the flight pitch turns around the torso.
        float lowestLocal = standing.min.y - transform.position.y;
        pivotBaseY = MeasuredHeight * 0.5f;
        pivot.localPosition = Vector3.up * pivotBaseY;
        model.localPosition = Vector3.down * (pivotBaseY + lowestLocal) +
            model.localPosition;
        Physics.SyncTransforms();
        SnapTo(ClipMagicA, 0f);
        standing = BakeBounds(bodyRenderer);
        soleOffset = Mathf.Min(footLeft.position.y, footRight.position.y) - standing.min.y;

        // Feet/lowest point of the first frames, used to hand the fall over
        // to the landing clip without a height jump.
        SnapTo(ClipFalling, clips[ClipFalling].length * 0.5f);
        fallingLowest = BakeBounds(bodyRenderer).min.y - transform.position.y;
        SnapTo(ClipLanding, 0f);
        landingStartLowest = BakeBounds(bodyRenderer).min.y - transform.position.y;
        SnapTo(ClipMagicA, 0f);
        lastBonePositions = null;
    }

    private static Bounds BakeBounds(SkinnedMeshRenderer renderer)
    {
        Mesh baked = new Mesh();
        // Without useScale Unity bakes the vertices at the renderer's world
        // size but in its local orientation (this FBX: 100x scale, -90 deg X).
        renderer.BakeMesh(baked, false);
        Vector3[] vertices = baked.vertices;
        Matrix4x4 matrix = Matrix4x4.TRS(renderer.transform.position, renderer.transform.rotation, Vector3.one);
        Bounds bounds = new Bounds(matrix.MultiplyPoint3x4(vertices[0]), Vector3.zero);
        for (int i = 1; i < vertices.Length; i++)
        {
            bounds.Encapsulate(matrix.MultiplyPoint3x4(vertices[i]));
        }
        Destroy(baked);
        return bounds;
    }

    private void SnapTo(int clip, float time)
    {
        for (int i = 0; i < ClipCount; i++)
        {
            weights[i] = i == clip ? 1f : 0f;
            clipTimes[i] = i == clip ? time : 0f;
        }
        targetClip = clip;
        EvaluateGraph();
        Physics.SyncTransforms();
    }

    // --------------------------------------------------------------- visit

    public bool BeginVisit(List<FixerIssue> issues)
    {
        if (director == null || GymDoorway.Instance == null)
        {
            return false;
        }
        work.Clear();
        work.AddRange(issues);
        VisitIssueCount = issues.Count;
        visitList.Clear();
        visitList.AddRange(issues);
        visitStartedAt = Time.time;
        currentIssue = null;
        StateHistory.Clear();
        CastGlowPeaks.Clear();
        CastCount = 0;
        MaxPoseSpeed = 0f;
        MaxGlowWhileNotCasting = 0f;
        TakeoffHadOpenSky = false;
        TookOffOutside = false;
        PeakAltitude = 0f;
        path.Clear();
        pathIndex = 0;
        groundLift = 0f;
        flightTime = 0f;
        // He was hidden: measure pose speed from this visit's first frame.
        lastBonePositions = null;
        impactPlayed = false;

        landingPoint = director.FindLandingPoint();
        groundY = landingPoint.y;
        GymDoorway doorway = GymDoorway.Instance;
        Vector3 face = Vector3.ProjectOnPlane(doorway.ExteriorPoint - landingPoint, Vector3.up);
        transform.SetPositionAndRotation(
            landingPoint + Vector3.up * SpawnHeight,
            face.sqrMagnitude > 0.01f ? Quaternion.LookRotation(face.normalized) : Quaternion.identity);
        pivot.localRotation = Quaternion.identity;
        pivot.localPosition = Vector3.up * pivotBaseY;
        verticalSpeed = 0f;
        gameObject.SetActive(true);
        capsule.enabled = false;
        SnapTo(ClipFalling, 0f);
        magic.SetEnvelope(0f);
        EnterState(FixerState.Falling);
        if (gridRoutine != null)
        {
            StopCoroutine(gridRoutine);
        }
        gridRoutine = StartCoroutine(BuildNavigation());
        return true;
    }

    private IEnumerator BuildNavigation()
    {
        GymDoorway doorway = GymDoorway.Instance;
        Bounds region = new Bounds(landingPoint, Vector3.one * 6f);
        region.Encapsulate(doorway.ExteriorPoint);
        region.Encapsulate(doorway.InteriorPoint);
        if (GymInteriorBuilder.TryGetMainGymBounds(out Bounds gym))
        {
            region.Encapsulate(gym);
        }
        if (GymBackRoomBuilder.TryGetRoomBounds(out Bounds lockers))
        {
            region.Encapsulate(lockers);
        }
        for (int i = 0; i < work.Count; i++)
        {
            // Far-flung props are fixed from range; keep the grid compact.
            if (work[i].Exists && PlanarDistance(work[i].Position, doorway.DoorCenter) < 45f)
            {
                region.Encapsulate(work[i].Position);
            }
        }
        region.Expand(new Vector3(1.5f, 0f, 1.5f));
        float started = Time.realtimeSinceStartup;
        yield return navGrid.Build(region, doorway.InteriorPoint.y, transform, 700);
        Vector3 outward = Vector3.ProjectOnPlane(
            doorway.ExteriorPoint - doorway.InteriorPoint, Vector3.up).normalized;
        navGrid.ForceWalkable(
            doorway.InteriorPoint - outward * 0.8f, doorway.ExteriorPoint + outward * 0.8f, 0.45f);
        Debug.Log(
            $"GYMCHAOS_FIXER_NAV_READY cells={navGrid.CellCount} walkable={navGrid.WalkableCount} " +
            $"seconds={Time.realtimeSinceStartup - started:F2}", this);
        gridRoutine = null;
    }

    private void EnterState(FixerState next)
    {
        state = next;
        stateEnteredAt = Time.time;
        StateHistory.Add(next.ToString());
        Debug.Log($"GYMCHAOS_FIXER_STATE state={next} position={transform.position}", this);
    }

    private void Update()
    {
        if (state == FixerState.Hidden)
        {
            return;
        }
        float dt = Mathf.Min(Time.deltaTime, 0.1f);
        if ((state == FixerState.Running || state == FixerState.Casting) &&
            Time.time - visitStartedAt > MaxVisitSeconds)
        {
            // Walk out first; he only ever takes off outside under open sky.
            Debug.LogWarning($"GYMCHAOS_FIXER_VISIT_WATCHDOG state={state} pending={PendingWork}", this);
            work.Clear();
            currentIssue = null;
            BeginExit();
        }
        switch (state)
        {
            case FixerState.Falling:
                TickFalling(dt);
                break;
            case FixerState.Landing:
                TickLanding(dt);
                break;
            case FixerState.Running:
            case FixerState.Exiting:
                TickRunning(dt);
                break;
            case FixerState.Casting:
                TickCasting();
                break;
            case FixerState.Flying:
                TickFlying(dt);
                break;
        }
        if (state == FixerState.Hidden)
        {
            return;
        }
        AdvanceAnimation(dt);
        if (state != FixerState.Casting)
        {
            MaxGlowWhileNotCasting = Mathf.Max(MaxGlowWhileNotCasting, magic.Envelope);
        }
    }

    private void TickFalling(float dt)
    {
        float height = transform.position.y - groundY;
        if (height > BrakeHeight)
        {
            verticalSpeed = Mathf.Min(verticalSpeed + FallAcceleration * dt, MaxFallSpeed);
        }
        else
        {
            // A magic air-brake right above the ground, then the landing.
            verticalSpeed = Mathf.MoveTowards(verticalSpeed, BrakeSpeed, 160f * dt);
        }
        // Hand over when the falling pose's lowest point reaches the landing
        // clip's starting height above the ground.
        float switchHeight = Mathf.Max(0f, landingStartLowest - fallingLowest);
        float next = transform.position.y - verticalSpeed * dt;
        if (next <= groundY + switchHeight)
        {
            next = groundY + switchHeight;
            landingRootOffset = switchHeight;
            transform.position = new Vector3(transform.position.x, next, transform.position.z);
            Play(ClipLanding, 0.15f, true);
            EnterState(FixerState.Landing);
            return;
        }
        transform.position = new Vector3(transform.position.x, next, transform.position.z);
    }

    private void TickLanding(float dt)
    {
        // The root drops the remaining hand-over gap exactly as the landing
        // clip takes over, so the visible body height stays continuous.
        float offset = landingRootOffset * (1f - weights[ClipLanding]);
        SetRootHeight(groundY + offset);
        float normalized = Normalized(ClipLanding);
        if (!impactPlayed && normalized >= 0.42f)
        {
            impactPlayed = true;
            capsule.enabled = true;
            GymAudio.Play(GymSoundEffect.FixerFloor, transform.position, 1f);
            GymFixerMagicEffect.Burst(transform.position + Vector3.up * 0.1f, 0.6f, 40);
        }
        // Never wait forever on the grid: without it he casts from afar.
        if (normalized >= 0.98f && (navGrid.IsReady || Time.time - stateEnteredAt > 10f))
        {
            BeginNextIssue();
        }
    }

    private void BeginNextIssue()
    {
        currentIssue = null;
        while (work.Count > 0)
        {
            int nearest = 0;
            float best = float.PositiveInfinity;
            for (int i = 0; i < work.Count; i++)
            {
                float distance = (work[i].Position - transform.position).sqrMagnitude;
                if (distance < best)
                {
                    best = distance;
                    nearest = i;
                }
            }
            FixerIssue candidate = work[nearest];
            work.RemoveAt(nearest);
            if (director.IsStillBroken(candidate))
            {
                currentIssue = candidate;
                break;
            }
        }

        if (currentIssue == null)
        {
            BeginExit();
            return;
        }

        Vector3 target = currentIssue.Position;
        if (PlanarDistance(transform.position, target) <= CastRange)
        {
            StartCast();
            return;
        }
        Vector3 stand = transform.position;
        if (!navGrid.TryNearestWalkable(target, CastRange - 0.3f, out stand) &&
            !navGrid.TryNearestWalkable(target, 6f, out stand))
        {
            // Nowhere to stand nearby: the spell reaches from here.
            StartCast();
            return;
        }
        if (!navGrid.TryFindPath(transform.position, stand, path))
        {
            path.Clear();
            path.Add(stand);
        }
        pathIndex = 0;
        stuckTimer = 0f;
        lastWaypointDistance = float.PositiveInfinity;
        Play(ClipRunning, 0.25f, false);
        if (state != FixerState.Running)
        {
            EnterState(FixerState.Running);
        }
    }

    private void BeginExit()
    {
        Vector3 exit = landingPoint;
        if (!navGrid.TryFindPath(transform.position, exit, path))
        {
            path.Clear();
            path.Add(GymDoorway.Instance.InteriorPoint);
            path.Add(GymDoorway.Instance.ExteriorPoint);
            path.Add(exit);
        }
        pathIndex = 0;
        stuckTimer = 0f;
        lastWaypointDistance = float.PositiveInfinity;
        Play(ClipRunning, 0.25f, false);
        EnterState(FixerState.Exiting);
    }

    private void TickRunning(float dt)
    {
        if (state == FixerState.Exiting && CanTakeOff())
        {
            BeginFlight();
            return;
        }
        if (state == FixerState.Exiting && Time.time - stateEnteredAt > 45f)
        {
            // Watchdog: the way out is blocked. Step back to the landing
            // spot, which is outside under open sky, rather than fly up
            // through a roof.
            Debug.LogWarning($"GYMCHAOS_FIXER_EXIT_WATCHDOG position={transform.position}", this);
            groundY = landingPoint.y;
            transform.position = landingPoint;
            SetRootHeight(groundY);
            BeginFlight();
            return;
        }

        UpdateDoorRequest();
        if (pathIndex >= path.Count)
        {
            if (state == FixerState.Exiting)
            {
                // Still under cover at the exit point: keep heading outward.
                Vector3 outward = Vector3.ProjectOnPlane(
                    landingPoint - GymDoorway.Instance.ExteriorPoint, Vector3.up);
                path.Add(transform.position + (outward.sqrMagnitude > 0.01f
                    ? outward.normalized : transform.forward) * 2f);
            }
            else
            {
                StartCast();
                return;
            }
        }

        Vector3 waypoint = path[pathIndex];
        Vector3 delta = Vector3.ProjectOnPlane(waypoint - transform.position, Vector3.up);
        float distance = delta.magnitude;
        if (distance <= 0.22f)
        {
            pathIndex++;
            lastWaypointDistance = float.PositiveInfinity;
            stuckTimer = 0f;
            return;
        }
        // Accelerate with the run blend so speed matches the stride.
        float speed = RunSpeed * Mathf.Clamp01(weights[ClipRunning] * 1.25f);
        Vector3 direction = delta / distance;
        transform.rotation = Quaternion.RotateTowards(
            transform.rotation, Quaternion.LookRotation(direction), TurnSpeed * dt);
        float facing = Vector3.Dot(transform.forward, direction);
        Vector3 next = transform.position + direction * Mathf.Min(distance,
            speed * Mathf.Clamp01(0.35f + facing) * dt);
        transform.position = next;
        if (TryGroundAt(next, groundY, transform, out float ground))
        {
            groundY = Mathf.MoveTowards(groundY, ground, 3f * dt);
        }
        SetRootHeight(groundY);

        if (distance < lastWaypointDistance - 0.05f)
        {
            lastWaypointDistance = distance;
            stuckTimer = 0f;
        }
        else if ((stuckTimer += dt) > 2.5f)
        {
            // Never stall: skip the waypoint he cannot get closer to.
            pathIndex++;
            stuckTimer = 0f;
            lastWaypointDistance = float.PositiveInfinity;
        }
    }

    private void StartCast()
    {
        ReleaseDoor();
        fixApplied = false;
        magicInput = magicInput == ClipMagicA ? ClipMagicB : ClipMagicA;
        Play(magicInput, 0.3f, true);
        magic.ResetPeak();
        CastCount++;
        if (state != FixerState.Casting)
        {
            EnterState(FixerState.Casting);
        }
        Debug.Log(
            $"GYMCHAOS_FIXER_CAST index={CastCount} kind={currentIssue?.Kind} " +
            $"key={currentIssue?.Key}", this);
    }

    private void TickCasting()
    {
        if (currentIssue != null && currentIssue.Exists)
        {
            Vector3 look = Vector3.ProjectOnPlane(
                currentIssue.Position - transform.position, Vector3.up);
            if (look.sqrMagnitude > 0.04f)
            {
                transform.rotation = Quaternion.RotateTowards(
                    transform.rotation, Quaternion.LookRotation(look.normalized),
                    240f * Time.deltaTime);
            }
        }
        SetRootHeight(groundY);

        float t = Normalized(magicInput);
        if (!fixApplied && t >= FixMoment)
        {
            fixApplied = true;
            director.ApplyFix(currentIssue, this);
        }
        if (t >= 0.985f)
        {
            CastGlowPeaks.Add(magic.PeakEnvelope);
            BeginNextIssue();
        }
    }

    private bool CanTakeOff()
    {
        return GymOutdoorBuilder.IsPlayerOutsideGym(transform.position) &&
            HasOpenSky(transform.position + Vector3.up * 0.2f);
    }

    private void BeginFlight()
    {
        ReleaseDoor();
        TakeoffHadOpenSky = HasOpenSky(transform.position + Vector3.up * 0.2f);
        TookOffOutside = GymOutdoorBuilder.IsPlayerOutsideGym(transform.position);
        GymAudio.Play(GymSoundEffect.FixerFloor, transform.position, 1f);
        capsule.enabled = false;
        verticalSpeed = 0f;
        flightTime = 0f;
        Play(ClipFlying, 0.35f, true);
        EnterState(FixerState.Flying);
    }

    private void TickFlying(float dt)
    {
        flightTime += dt;
        // Lift off gently while the flying pose blends in, then rocket up.
        verticalSpeed = Mathf.Min(
            verticalSpeed + FlyAcceleration * Mathf.Clamp01(flightTime / 0.4f) * dt, MaxFlySpeed);
        transform.position += (Vector3.up * verticalSpeed +
            transform.forward * Mathf.Min(verticalSpeed * 0.12f, 2.5f)) * dt;
        // The flying clip is a horizontal superhero pose: pitch it upward.
        float pitch = Mathf.SmoothStep(0f, -78f, flightTime / 0.75f);
        pivot.localRotation = Quaternion.Euler(pitch, 0f, 0f);
        float altitude = transform.position.y - groundY;
        PeakAltitude = Mathf.Max(PeakAltitude, altitude);
        if (altitude >= DespawnHeight)
        {
            Finish();
        }
    }

    private void Finish()
    {
        Debug.Log(
            $"GYMCHAOS_FIXER_DESPAWNED altitude={PeakAltitude:F1} casts={CastCount}", this);
        magic.SetEnvelope(0f);
        ReleaseDoor();
        EnterState(FixerState.Hidden);
        gameObject.SetActive(false);
        director.NotifyVisitFinished(visitList);
    }

    // ----------------------------------------------------------- animation

    private void Play(int clip, float fade, bool restart)
    {
        if (targetClip == clip && !restart)
        {
            return;
        }
        if (restart)
        {
            clipTimes[clip] = 0f;
        }
        targetClip = clip;
        fadeSeconds = Mathf.Max(0.01f, fade);
    }

    private float Normalized(int clip)
    {
        return Mathf.Clamp01(clipTimes[clip] / Mathf.Max(0.001f, clips[clip].length));
    }

    private void AdvanceAnimation(float dt)
    {
        float total = 0f;
        for (int i = 0; i < ClipCount; i++)
        {
            float goal = i == targetClip ? 1f : 0f;
            weights[i] = Mathf.MoveTowards(weights[i], goal, dt / fadeSeconds);
            total += weights[i];
            if (weights[i] > 0f || i == targetClip)
            {
                clipTimes[i] += dt;
            }
        }
        if (total > 0.0001f)
        {
            for (int i = 0; i < ClipCount; i++)
            {
                weights[i] /= total;
            }
        }
        EvaluateGraph();
        ApplyGroundLift(dt);
        UpdateMagicEnvelope();
        TrackPoseSpeed(dt);
    }

    private void EvaluateGraph()
    {
        for (int i = 0; i < ClipCount; i++)
        {
            mixer.SetInputWeight(i, weights[i]);
            float length = Mathf.Max(0.001f, clips[i].length);
            float time = ClipLoops[i] ? Mathf.Repeat(clipTimes[i], length)
                : Mathf.Min(clipTimes[i], length);
            Playable input = mixer.GetInput(i);
            input.SetTime(time);
            input.SetTime(time);
        }
        graph.Evaluate(0f);
    }

    private void UpdateMagicEnvelope()
    {
        float envelope = 0f;
        for (int i = ClipMagicA; i < ClipCount; i += ClipMagicB - ClipMagicA)
        {
            float t = Normalized(i);
            float shape = Mathf.SmoothStep(0f, 1f, t / 0.15f) *
                (1f - Mathf.SmoothStep(0f, 1f, (t - 0.8f) / 0.2f));
            envelope = Mathf.Max(envelope, shape * weights[i]);
        }
        magic.SetEnvelope(envelope);
    }

    private void ApplyGroundLift(float dt)
    {
        // Grounded clips must never push a foot or the lollipop (the hand
        // touches down in the landing) through the floor.
        float targetLift = 0f;
        if (state == FixerState.Landing || state == FixerState.Running ||
            state == FixerState.Casting || state == FixerState.Exiting)
        {
            float floor = transform.position.y;
            float lowest = Mathf.Min(footLeft.position.y, footRight.position.y) - soleOffset;
            Vector3 candy = magic.CandyCenter;
            lowest = Mathf.Min(lowest, candy.y - magic.CandyRadius);
            targetLift = Mathf.Max(0f, floor - (lowest - groundLift));
        }
        groundLift = Mathf.MoveTowards(groundLift, targetLift, 2.5f * dt);
        if (state != FixerState.Flying)
        {
            pivot.localPosition = Vector3.up * (pivotBaseY + groundLift);
        }
    }

    private void TrackPoseSpeed(float dt)
    {
        if (trackedBones == null)
        {
            trackedBones = head != null
                ? new[] { hand, footLeft, footRight, head }
                : new[] { hand, footLeft, footRight };
        }
        Transform[] tracked = trackedBones;
        if (lastBonePositions == null)
        {
            lastBonePositions = new Vector3[tracked.Length];
            for (int i = 0; i < tracked.Length; i++)
            {
                lastBonePositions[i] = pivot.InverseTransformPoint(tracked[i].position);
            }
            return;
        }
        for (int i = 0; i < tracked.Length; i++)
        {
            if (tracked[i] == null)
            {
                continue;
            }
            Vector3 local = pivot.InverseTransformPoint(tracked[i].position);
            if (dt > 0.0001f && dt <= 0.05f)
            {
                MaxPoseSpeed = Mathf.Max(MaxPoseSpeed, (local - lastBonePositions[i]).magnitude / dt);
            }
            lastBonePositions[i] = local;
        }
    }

    // ------------------------------------------------------------- helpers

    private void SetRootHeight(float y)
    {
        Vector3 position = transform.position;
        position.y = y;
        transform.position = position;
    }

    private void UpdateDoorRequest()
    {
        GymDoorway doorway = GymDoorway.Instance;
        bool want = doorway != null &&
            PlanarDistance(doorway.DoorCenter, transform.position) <= 3.5f;
        if (want == doorRequested)
        {
            return;
        }
        doorRequested = want;
        if (want)
        {
            doorway.RequestOpen();
        }
        else
        {
            doorway?.ReleaseOpen();
        }
    }

    private void ReleaseDoor()
    {
        if (!doorRequested)
        {
            return;
        }
        doorRequested = false;
        GymDoorway.Instance?.ReleaseOpen();
    }

    private static readonly RaycastHit[] GroundHits = new RaycastHit[12];

    /// <summary>Highest floor-like surface near <paramref name="referenceY"/>.</summary>
    public static bool TryGroundAt(Vector3 point, float referenceY, Transform ignore, out float ground)
    {
        ground = referenceY;
        Vector3 origin = new Vector3(point.x, referenceY + 1.2f, point.z);
        int count = Physics.RaycastNonAlloc(
            origin, Vector3.down, GroundHits, 3f,
            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        float best = float.NegativeInfinity;
        for (int i = 0; i < count; i++)
        {
            Collider collider = GroundHits[i].collider;
            if (collider == null || (ignore != null && collider.transform.IsChildOf(ignore)))
            {
                continue;
            }
            Rigidbody attached = collider.attachedRigidbody;
            if ((attached != null && !attached.isKinematic) ||
                collider.GetComponentInParent<EnemyFighter>() != null ||
                collider.GetComponentInParent<PlayerMovement>() != null ||
                collider.GetComponentInParent<PickupItem>() != null)
            {
                continue;
            }
            float y = GroundHits[i].point.y;
            if (y > best && y <= referenceY + 0.6f)
            {
                best = y;
            }
        }
        if (float.IsNegativeInfinity(best))
        {
            return false;
        }
        ground = best;
        return true;
    }

    public static bool HasOpenSky(Vector3 point)
    {
        return !Physics.Raycast(
            point + Vector3.up * (GameplayHeight + 0.2f), Vector3.up, 150f,
            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
    }

    private static float PlanarDistance(Vector3 a, Vector3 b)
    {
        a.y = 0f;
        b.y = 0f;
        return Vector3.Distance(a, b);
    }

    private static Transform FindBone(Transform[] bones, string name)
    {
        string wanted = Normalize(name);
        for (int i = 0; i < bones.Length; i++)
        {
            if (Normalize(bones[i].name) == wanted)
            {
                return bones[i];
            }
        }
        return null;
    }

    private static string Normalize(string value)
    {
        System.Text.StringBuilder builder = new System.Text.StringBuilder(value.Length);
        for (int i = 0; i < value.Length; i++)
        {
            if (char.IsLetterOrDigit(value[i]))
            {
                builder.Append(char.ToLowerInvariant(value[i]));
            }
        }
        return builder.ToString();
    }

    private string DescribeClips()
    {
        System.Text.StringBuilder builder = new System.Text.StringBuilder();
        for (int i = 0; i < ClipCount; i++)
        {
            if (i > 0) builder.Append('|');
            builder.Append(clips[i] != null ? $"{clips[i].name}:{clips[i].length:F2}" : "missing");
        }
        return builder.ToString();
    }

    private void OnDestroy()
    {
        ReleaseDoor();
        if (graph.IsValid())
        {
            graph.Destroy();
        }
    }
}
