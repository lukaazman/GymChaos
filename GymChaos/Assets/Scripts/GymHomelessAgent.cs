using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.Rendering;

/// <summary>
/// The homeless guy who lives in the tent in the open park. His day:
/// asleep in the tent at night; at daybreak he gets up (stand_up) while
/// crawling out of the door, walks to the nearest park bench, turns his back
/// to it and sits down (sit_to_stand backwards), sits through the day, gets
/// up near dusk (sit_to_stand), walks back, turns his back to the tent and
/// lies down inside it (stand_up backwards), then sleeps.
///
/// The exporter places every clip on one fixed root (standing hips over the
/// root, seated hips behind it, lying body on the stand_up start pose), so
/// transitions only crossfade nearly identical poses. The root itself only
/// moves while walking and while getting in or out of the tent.
/// </summary>
[DefaultExecutionOrder(900)]
public sealed class GymHomelessAgent : MonoBehaviour
{
    public enum HomelessState
    {
        Preparing,
        Sleeping,
        StandingUp,
        WalkingToBench,
        TurningAtBench,
        SittingDown,
        Sitting,
        StandingFromBench,
        WalkingToTent,
        TurningAtTent,
        LyingDown
    }

    public const string ObjectName = "Homeless Guy";
    // A slight older man, shorter than the gym's bodybuilders; at this
    // height he lies on the bag and crawls in and out of the 1.3x tent
    // without touching the canvas.
    public const float GameplayHeight = 1.9f;
    private const string ResourcePath = "Characters/Homeless/homeless_guy_authored";
    private const string TexturePath = "Characters/Textures/homeless_guy";
    // Day plan on GymTimeOfDay.Time01 (sunrise 0.12-0.27, dusk 0.68-0.84).
    public const float WakeTime01 = 0.14f;
    // He should be lying in the tent by the time it is properly dark.
    public const float InBedBy01 = 0.86f;
    private const float TurnSpeed = 240f;
    // A tired shuffle: the walk clip plays at this rate and he moves at the
    // same fraction of its stride speed, so his feet do not slide.
    public const float WalkPace = 0.82f;
    private const float ArriveDistance = 0.08f;
    private const float StandOutsideDoor = 1.0f;
    // Sleeping bag thickness under the lying body.
    private const float BedLift = 0.08f;

    private const int ClipWalking = 0;
    private const int ClipSleeping = 1;
    private const int ClipStandUp = 2;
    private const int ClipSitToStand = 3;
    private const int ClipSitting = 4;
    private const int ClipCount = 5;
    private static readonly string[] ClipStems =
        { "walking", "sleeping", "stand_up", "sit_to_stand", "sitting" };
    private static readonly bool[] ClipLoops = { true, true, false, false, true };

    private Transform model;
    private Animator animator;
    private PlayableGraph graph;
    private AnimationMixerPlayable mixer;
    private readonly AnimationClip[] clips = new AnimationClip[ClipCount];
    private readonly float[] clipTimes = new float[ClipCount];
    private readonly float[] clipSpeeds = { 1f, 1f, 1f, 1f, 1f };
    private readonly float[] weights = new float[ClipCount];
    private int targetClip = -1;
    private float fadeSeconds = 0.3f;
    private SkinnedMeshRenderer bodyRenderer;
    private Transform hips;
    private Transform head;
    private CapsuleCollider capsule;

    private readonly GymFixerNavGrid navGrid = new GymFixerNavGrid();
    private readonly List<Vector3> path = new List<Vector3>();
    private int pathIndex;
    private HomelessState state = HomelessState.Preparing;
    private float groundY;

    // Model-local measurements (agent at identity).
    private Vector3 seatedHips;
    private float seatedButt;
    private Vector3 lyingCenter;
    private float lyingLowest;
    private Vector3 sleepCenter;
    private float sleepLowest;
    private float sleepLift;
    private Vector3 sleepRoot;
    private float walkSpeed = 1.2f;

    // Placement.
    private Vector3 bedRoot;
    private Vector3 doorRoot;
    // Facing into the tent: he lies down and gets up looking at it.
    private Quaternion doorFacing;
    private Vector3 benchRoot;
    private Quaternion benchFacing;
    private float seatLift;
    private float bedLift;
    private float benchToTentSeconds = 20f;
    private int benchIndex = -1;

    public static GymHomelessAgent Instance { get; private set; }
    public HomelessState State => state;
    public bool IsReady { get; private set; }
    public readonly List<string> StateHistory = new List<string>();
    public float MeasuredHeight { get; private set; }
    public Vector3 BenchPosition { get; private set; }
    public Vector3 BenchFacing { get; private set; }
    public Vector3 BedRoot => bedRoot;
    public Vector3 DoorRoot => doorRoot;
    public Vector3 BenchRoot => benchRoot;
    public Quaternion DoorFacing => doorFacing;
    public Quaternion BenchFacingRotation => benchFacing;
    public float SeatLift => seatLift;
    public float WalkSpeed => walkSpeed;
    public float LeaveBench01 => ComputeLeave01();
    public Transform HipsBone => hips;
    public Transform HeadBone => head;
    public SkinnedMeshRenderer Body => bodyRenderer;
    public float TargetWeight => targetClip >= 0 ? weights[targetClip] : 0f;
    public float CurrentClipNormalized => targetClip >= 0 ? Normalized(targetClip) : 0f;
    public string CurrentClipName => targetClip >= 0 ? ClipStems[targetClip] : "none";
    // Largest per-frame jump of the hips (pose pops), excluding teleports.
    public float MaxHipsStep { get; private set; }
    private Vector3 lastHips;
    private bool hasLastHips;

    public static void Spawn(Transform parent)
    {
        if (Instance != null)
        {
            return;
        }
        GameObject agentObject = new GameObject(ObjectName);
        agentObject.transform.SetParent(parent, false);
        agentObject.AddComponent<GymHomelessAgent>();
    }

    private void Awake()
    {
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
        if (graph.IsValid())
        {
            graph.Destroy();
        }
    }

    private IEnumerator Start()
    {
        // The tent loads asynchronously; plants get their colliders as they
        // load. Wait for both so the walking grid sees the final park.
        float waitStarted = Time.realtimeSinceStartup;
        while (!GymParkAccess.TentReady && Time.realtimeSinceStartup - waitStarted < 30f)
        {
            yield return null;
        }
        while (GymOutdoorFoliage.LoadedPlants + GymOutdoorFoliage.FailedPlants <
            GymOutdoorFoliage.RequestedPlants && Time.realtimeSinceStartup - waitStarted < 30f)
        {
            yield return null;
        }
        if (!GymParkAccess.TentReady || !Prepare())
        {
            Debug.LogError(
                $"GYMCHAOS_HOMELESS_FAIL reason=prepare tent={GymParkAccess.TentReady}", this);
            yield break;
        }

        groundY = GymParkAccess.FloorY + GymOutdoorFoliage.GroundTopOffset;
        // Out of sight under the park while the walking grid is probed.
        transform.position = GymParkAccess.TentCenter + Vector3.down * 30f;
        Bounds area = GymParkAccess.Area;
        area.Expand(new Vector3(1f, 0f, 1f));
        yield return navGrid.Build(area, groundY, transform, 900);
        if (!PlanPlaces())
        {
            Debug.LogError("GYMCHAOS_HOMELESS_FAIL reason=no-bench-route", this);
            yield break;
        }

        // Start where the clock says he should be.
        float t01 = TimeOfDay01();
        if (IsBenchTime(t01) && t01 > WakeTime01 + 0.12f)
        {
            EnterSitting(true);
        }
        else
        {
            EnterSleeping(true);
        }
        IsReady = true;
        Debug.Log(
            $"GYMCHAOS_HOMELESS_READY height={MeasuredHeight:F2} walk={walkSpeed:F2} " +
            $"bench={BenchPosition} bed={bedRoot} door={doorRoot} seatLift={seatLift:F3} " +
            $"bedLift={bedLift:F3} leave01={ComputeLeave01():F3} state={state} clips={DescribeClips()}",
            this);
    }

    // --------------------------------------------------------------- setup

    private bool Prepare()
    {
        GameObject prefab = Resources.Load<GameObject>(ResourcePath);
        if (prefab == null)
        {
            Debug.LogError($"Homeless guy model is missing at Resources/{ResourcePath}.fbx", this);
            return false;
        }
        transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        model = Instantiate(prefab, transform).transform;
        model.name = "Homeless Guy Authored Rig";
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

        SkinnedMeshRenderer[] renderers = model.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            if (bodyRenderer == null || renderers[i].sharedMesh != null && bodyRenderer.sharedMesh != null &&
                renderers[i].sharedMesh.vertexCount > bodyRenderer.sharedMesh.vertexCount)
            {
                bodyRenderer = renderers[i];
            }
        }
        if (bodyRenderer == null)
        {
            return false;
        }
        ApplyBodyMaterial();

        Transform[] bones = model.GetComponentsInChildren<Transform>(true);
        hips = FindBone(bones, "DEF-spine");
        head = FindBone(bones, "DEF-spine.006") ?? FindBone(bones, "DEF-spine.005");
        if (hips == null || head == null || !LoadClips())
        {
            return false;
        }
        BuildGraph();
        Measure();

        capsule = gameObject.AddComponent<CapsuleCollider>();
        capsule.radius = 0.32f;
        capsule.height = GameplayHeight;
        capsule.center = new Vector3(0f, GameplayHeight * 0.5f, 0f);
        Rigidbody body = gameObject.AddComponent<Rigidbody>();
        body.isKinematic = true;
        body.useGravity = false;
        return true;
    }

    private void ApplyBodyMaterial()
    {
        Shader shader = Shader.Find("GymChaos/BodybuilderUnlit") ??
            Shader.Find("Universal Render Pipeline/Unlit");
        Texture2D texture = Resources.Load<Texture2D>(TexturePath);
        Material material = new Material(shader) { name = "Homeless Guy Body" };
        material.SetColor("_BaseColor", Color.white);
        material.SetColor("_Color", Color.white);
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
                Debug.LogError($"Homeless guy clip '{ClipStems[i]}' is missing.", this);
                return false;
            }
        }
        return true;
    }

    private void BuildGraph()
    {
        graph = PlayableGraph.Create("Homeless Guy Mixer");
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
        AnimationPlayableOutput output = AnimationPlayableOutput.Create(graph, "Homeless Guy", animator);
        output.SetSourcePlayable(mixer);
    }

    private void Measure()
    {
        // Standing at the end of stand_up: scale to the gameplay height and
        // put the soles on the root.
        SnapTo(ClipStandUp, clips[ClipStandUp].length);
        Bounds standing = BakeLocalBounds();
        model.localScale *= GameplayHeight / Mathf.Max(0.01f, standing.size.y);
        SnapTo(ClipStandUp, clips[ClipStandUp].length);
        standing = BakeLocalBounds();
        model.localPosition += Vector3.down * standing.min.y;
        // The clips face the model's forward; seated hips must sit behind
        // the root. If they are in front the import faces the other way.
        SnapTo(ClipSitToStand, 0f);
        if (Local(hips.position).z > 0f)
        {
            model.localRotation = Quaternion.AngleAxis(180f, Vector3.up) * model.localRotation;
        }
        SnapTo(ClipStandUp, clips[ClipStandUp].length);
        standing = BakeLocalBounds();
        Vector3 standHips = Local(hips.position);
        model.localPosition -= new Vector3(standHips.x, standing.min.y, standHips.z);
        SnapTo(ClipStandUp, clips[ClipStandUp].length);
        MeasuredHeight = BakeLocalBounds().size.y;

        SnapTo(ClipSitToStand, 0f);
        seatedHips = Local(hips.position);
        seatedButt = SeatedButtHeight(seatedHips);
        SnapTo(ClipStandUp, 0f);
        Bounds lying = BakeLocalBounds();
        lyingCenter = lying.center;
        lyingLowest = lying.min.y;
        SnapTo(ClipSleeping, 0f);
        Bounds sleeping = BakeLocalBounds();
        sleepCenter = sleeping.center;
        sleepLowest = sleeping.min.y;

        // Walk speed from the stance foot: it slides back under the body at
        // the walking speed for a little over half the cycle.
        Transform footL = FindBone(model.GetComponentsInChildren<Transform>(true), "DEF-foot.L");
        if (footL != null)
        {
            float minZ = float.MaxValue;
            float maxZ = float.MinValue;
            float length = clips[ClipWalking].length;
            for (int i = 0; i < 24; i++)
            {
                SnapTo(ClipWalking, length * i / 24f);
                float z = Local(footL.position).z;
                minZ = Mathf.Min(minZ, z);
                maxZ = Mathf.Max(maxZ, z);
            }
            walkSpeed = Mathf.Clamp((maxZ - minZ) / (length * 0.55f), 0.6f, 2.2f);
        }
        SnapTo(ClipSleeping, 0f);
    }

    // Lowest body point under the seated hips (the buttocks), so the seat
    // height can be matched exactly.
    private float SeatedButtHeight(Vector3 hipsLocal)
    {
        Mesh baked = new Mesh();
        bodyRenderer.BakeMesh(baked, false);
        Vector3[] vertices = baked.vertices;
        Matrix4x4 toWorld = Matrix4x4.TRS(
            bodyRenderer.transform.position, bodyRenderer.transform.rotation, Vector3.one);
        float lowest = float.MaxValue;
        for (int i = 0; i < vertices.Length; i++)
        {
            Vector3 local = Local(toWorld.MultiplyPoint3x4(vertices[i]));
            float dx = local.x - hipsLocal.x;
            float dz = local.z - hipsLocal.z;
            if (dx * dx + dz * dz < 0.16f * 0.16f && local.y < lowest)
            {
                lowest = local.y;
            }
        }
        Destroy(baked);
        return lowest < float.MaxValue ? lowest : hipsLocal.y - 0.12f;
    }

    private bool PlanPlaces()
    {
        // Tent: he faces the tent from just outside the door, lies down
        // into it head first (stand_up backwards) and ends prone with his
        // head on the pillow at the back; getting up is the same in reverse,
        // backing out of the door. Both lying poses sit centred on the
        // sleeping bag itself.
        Vector3 door = GymParkAccess.TentDoorDirection;
        doorFacing = Quaternion.LookRotation(-door, Vector3.up);
        Bounds bed = GymParkAccess.TentBedBounds;
        Bounds bag = GymParkAccess.TentBagBounds;
        Vector3 bagCenter = new Vector3(bag.center.x, groundY, bag.center.z);
        bedRoot = bagCenter - doorFacing * new Vector3(lyingCenter.x, 0f, lyingCenter.z);
        sleepRoot = bagCenter - doorFacing * new Vector3(sleepCenter.x, 0f, sleepCenter.z);
        bedLift = (bed.min.y + BedLift) - (groundY + lyingLowest);
        sleepLift = (bed.min.y + BedLift) - (groundY + sleepLowest);
        Vector3 tentCenter = GymParkAccess.TentCenter;
        doorRoot = new Vector3(tentCenter.x, groundY, tentCenter.z) +
            door * (GymParkAccess.TentDoorDepth + StandOutsideDoor);

        // Nearest open-park bench by walking distance from the door.
        List<int> candidates = new List<int>();
        for (int i = 0; i < GymOutdoorFoliage.Benches.Count; i++)
        {
            if (GymParkAccess.Contains(GymOutdoorFoliage.Benches[i].Position, 0.8f))
            {
                candidates.Add(i);
            }
        }
        candidates.Sort((a, b) =>
            FlatDistance(GymOutdoorFoliage.Benches[a].Position, doorRoot).CompareTo(
                FlatDistance(GymOutdoorFoliage.Benches[b].Position, doorRoot)));
        float bestLength = float.MaxValue;
        for (int c = 0; c < candidates.Count && c < 4; c++)
        {
            GymOutdoorFoliage.ParkBench bench = GymOutdoorFoliage.Benches[candidates[c]];
            Quaternion facing = Quaternion.LookRotation(bench.Facing, Vector3.up);
            Vector3 seat = new Vector3(bench.Position.x, groundY, bench.Position.z);
            Vector3 root = seat - facing * new Vector3(seatedHips.x, 0f, seatedHips.z);
            if (!navGrid.TryFindPath(doorRoot, root, path))
            {
                continue;
            }
            float length = PathLength(doorRoot, root);
            if (length < bestLength)
            {
                bestLength = length;
                benchIndex = candidates[c];
                benchRoot = root;
                benchFacing = facing;
            }
        }
        if (benchIndex < 0)
        {
            return false;
        }
        GymOutdoorFoliage.ParkBench chosen = GymOutdoorFoliage.Benches[benchIndex];
        BenchPosition = chosen.Position;
        BenchFacing = chosen.Facing;
        seatLift = (GymOutdoorFoliage.BenchSeatTop + chosen.Position.y - groundY) - seatedButt;
        benchToTentSeconds = bestLength / Mathf.Max(0.3f, walkSpeed * WalkPace);
        path.Clear();
        return true;
    }

    // --------------------------------------------------------------- states

    private void Update()
    {
        if (!IsReady)
        {
            return;
        }
        float dt = Mathf.Min(Time.deltaTime, 0.1f);
        float t01 = TimeOfDay01();
        switch (state)
        {
            case HomelessState.Sleeping:
                PlaceAtBed(0f);
                if (IsBenchTime(t01))
                {
                    Enter(HomelessState.StandingUp);
                    clipTimes[ClipStandUp] = 0f;
                    Play(ClipStandUp, 1.0f, 1f);
                }
                break;
            case HomelessState.StandingUp:
                PlaceAtBed(Normalized(ClipStandUp));
                if (clipTimes[ClipStandUp] >= clips[ClipStandUp].length)
                {
                    StartWalk(benchRoot, HomelessState.WalkingToBench);
                }
                break;
            case HomelessState.WalkingToBench:
                if (!IsBenchTime(t01))
                {
                    StartWalk(doorRoot, HomelessState.WalkingToTent);
                }
                else if (FollowPath(dt))
                {
                    Enter(HomelessState.TurningAtBench);
                }
                break;
            case HomelessState.TurningAtBench:
                if (TurnInPlace(benchFacing, dt))
                {
                    Enter(HomelessState.SittingDown);
                    clipTimes[ClipSitToStand] = clips[ClipSitToStand].length;
                    Play(ClipSitToStand, 0.3f, -1f);
                }
                break;
            case HomelessState.SittingDown:
                PlaceAtBench(Normalized(ClipSitToStand));
                if (clipTimes[ClipSitToStand] <= 0f)
                {
                    EnterSitting(false);
                }
                break;
            case HomelessState.Sitting:
                PlaceAtBench(0f);
                if (!IsBenchTime(t01))
                {
                    Enter(HomelessState.StandingFromBench);
                    clipTimes[ClipSitToStand] = 0f;
                    Play(ClipSitToStand, 0.45f, 1f);
                }
                break;
            case HomelessState.StandingFromBench:
                PlaceAtBench(Normalized(ClipSitToStand));
                if (clipTimes[ClipSitToStand] >= clips[ClipSitToStand].length)
                {
                    StartWalk(doorRoot, HomelessState.WalkingToTent);
                }
                break;
            case HomelessState.WalkingToTent:
                if (FollowPath(dt))
                {
                    Enter(HomelessState.TurningAtTent);
                }
                break;
            case HomelessState.TurningAtTent:
                if (TurnInPlace(doorFacing, dt))
                {
                    Enter(HomelessState.LyingDown);
                    clipTimes[ClipStandUp] = clips[ClipStandUp].length;
                    Play(ClipStandUp, 0.3f, -1f);
                }
                break;
            case HomelessState.LyingDown:
                PlaceAtBed(Normalized(ClipStandUp));
                if (clipTimes[ClipStandUp] <= 0f)
                {
                    EnterSleeping(false);
                }
                break;
        }
        AdvanceAnimation(dt);
        TrackHips();
    }

    private void Enter(HomelessState next)
    {
        state = next;
        StateHistory.Add(next.ToString());
        if (StateHistory.Count > 64)
        {
            StateHistory.RemoveAt(0);
        }
        if (capsule != null)
        {
            // Inside the tent the canvas is the obstacle.
            capsule.enabled = next != HomelessState.Sleeping &&
                next != HomelessState.StandingUp && next != HomelessState.LyingDown;
        }
    }

    private void EnterSleeping(bool snap)
    {
        Enter(HomelessState.Sleeping);
        clipTimes[ClipSleeping] = 0f;
        Play(ClipSleeping, snap ? 0.01f : 1.0f, 1f);
        if (snap)
        {
            SnapTo(ClipSleeping, 0f);
            hasLastHips = false;
        }
        PlaceAtBed(0f);
    }

    private void EnterSitting(bool snap)
    {
        Enter(HomelessState.Sitting);
        clipTimes[ClipSitting] = 0f;
        Play(ClipSitting, snap ? 0.01f : 0.5f, 1f);
        if (snap)
        {
            SnapTo(ClipSitting, 0f);
            hasLastHips = false;
        }
        PlaceAtBench(0f);
    }

    // stand_up time 0 = lying on the bag, 1 = standing outside the door.
    // He crawls backwards out of the door while still low, so his body
    // clears the canvas, and rises fully once outside (and the same in
    // reverse when he crawls in).
    public static float TentTravel(float t)
    {
        return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.08f, 0.62f, t));
    }

    private void PlaceAtBed(float t)
    {
        Vector3 root = Vector3.Lerp(bedRoot, doorRoot, TentTravel(t));
        float lift = bedLift * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.05f, 0.35f, t)));
        // The sleeping pose has its own centre on the bag; follow its blend
        // weight so the crossfade to and from it does not slide the body.
        float sleep = weights[ClipSleeping];
        root = Vector3.Lerp(root, sleepRoot, sleep);
        lift = Mathf.Lerp(lift, sleepLift, sleep);
        transform.SetPositionAndRotation(root + Vector3.up * lift, doorFacing);
    }

    // sit_to_stand time 0 = seated, 1 = standing in front of the bench.
    private void PlaceAtBench(float t)
    {
        float lift = seatLift * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.1f, 0.6f, t)));
        transform.SetPositionAndRotation(benchRoot + Vector3.up * lift, benchFacing);
    }

    private void StartWalk(Vector3 goal, HomelessState walkState)
    {
        Enter(walkState);
        Vector3 from = transform.position;
        if (!navGrid.TryFindPath(from, goal, path) || path.Count == 0)
        {
            path.Clear();
        }
        path.Add(goal);
        pathIndex = 0;
        Play(ClipWalking, 0.35f, 1f);
    }

    // Returns true once standing on the last waypoint.
    private bool FollowPath(float dt)
    {
        if (pathIndex >= path.Count)
        {
            return true;
        }
        Vector3 position = transform.position;
        Vector3 target = path[pathIndex];
        target.y = groundY;
        Vector3 offset = target - position;
        offset.y = 0f;
        float distance = offset.magnitude;
        bool last = pathIndex == path.Count - 1;
        if (distance <= (last ? ArriveDistance : 0.35f))
        {
            pathIndex++;
            if (pathIndex >= path.Count)
            {
                transform.position = new Vector3(target.x, groundY, target.z);
                return true;
            }
            return false;
        }

        Quaternion look = Quaternion.LookRotation(offset / distance, Vector3.up);
        transform.rotation = Quaternion.RotateTowards(transform.rotation, look, TurnSpeed * dt);
        // Slow down for sharp turns and on the final approach.
        float align = Mathf.Clamp01(Vector3.Dot(transform.forward, offset / distance));
        float speed = walkSpeed * WalkPace * Mathf.Lerp(0.25f, 1f, align);
        if (last)
        {
            speed = Mathf.Min(speed, Mathf.Max(0.35f, distance * 2.5f));
        }
        Vector3 step = (offset / distance) * Mathf.Min(distance, speed * dt);
        transform.position = new Vector3(position.x + step.x, groundY, position.z + step.z);
        clipSpeeds[ClipWalking] = Mathf.Clamp(speed / walkSpeed, 0.3f, 1.2f);
        return false;
    }

    private bool TurnInPlace(Quaternion goal, float dt)
    {
        transform.position = new Vector3(transform.position.x, groundY, transform.position.z);
        transform.rotation = Quaternion.RotateTowards(transform.rotation, goal, TurnSpeed * 0.6f * dt);
        clipSpeeds[ClipWalking] = 0.5f;
        return Quaternion.Angle(transform.rotation, goal) < 1f;
    }

    // --------------------------------------------------------------- clock

    private static float TimeOfDay01()
    {
        GymTimeOfDay time = GymTimeOfDay.Instance;
        return time != null ? time.Time01 : 0.4f;
    }

    private float ComputeLeave01()
    {
        if (clips[ClipSitToStand] == null || clips[ClipStandUp] == null)
        {
            return 0.66f;
        }
        GymTimeOfDay time = GymTimeOfDay.Instance;
        float day = time != null ? Mathf.Max(30f, time.SimulatedDayLengthSeconds) : 90f;
        float seconds = clips[ClipSitToStand].length + benchToTentSeconds + 1.5f +
            clips[ClipStandUp].length;
        return Mathf.Clamp(InBedBy01 - seconds / day, 0.4f, InBedBy01 - 0.02f);
    }

    public bool IsBenchTime(float t01)
    {
        return t01 >= WakeTime01 && t01 < ComputeLeave01();
    }

    // --------------------------------------------------------------- animation

    private void Play(int clip, float fade, float speed)
    {
        clipSpeeds[clip] = speed;
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
            if (i == targetClip || (weights[i] > 0f && ClipLoops[i]))
            {
                clipTimes[i] += dt * clipSpeeds[i];
                if (!ClipLoops[i])
                {
                    clipTimes[i] = Mathf.Clamp(clipTimes[i], 0f, clips[i].length);
                }
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
    }

    private void EvaluateGraph()
    {
        for (int i = 0; i < ClipCount; i++)
        {
            mixer.SetInputWeight(i, weights[i]);
            float length = Mathf.Max(0.001f, clips[i].length);
            float time = ClipLoops[i] ? Mathf.Repeat(clipTimes[i], length)
                : Mathf.Clamp(clipTimes[i], 0f, length);
            Playable input = mixer.GetInput(i);
            input.SetTime(time);
            input.SetTime(time);
        }
        graph.Evaluate(0f);
    }

    private void TrackHips()
    {
        Vector3 current = hips.position;
        if (hasLastHips)
        {
            float step = (current - lastHips).magnitude;
            if (Time.deltaTime > 0f && Time.deltaTime < 0.051f)
            {
                MaxHipsStep = Mathf.Max(MaxHipsStep, step);
            }
        }
        lastHips = current;
        hasLastHips = true;
    }

    public void ResetHipsTracking()
    {
        MaxHipsStep = 0f;
        hasLastHips = false;
    }

    // --------------------------------------------------------------- helpers

    private Vector3 Local(Vector3 world)
    {
        return transform.InverseTransformPoint(world);
    }

    private Bounds BakeLocalBounds()
    {
        Mesh baked = new Mesh();
        bodyRenderer.BakeMesh(baked, false);
        Vector3[] vertices = baked.vertices;
        Matrix4x4 toWorld = Matrix4x4.TRS(
            bodyRenderer.transform.position, bodyRenderer.transform.rotation, Vector3.one);
        Bounds bounds = new Bounds(Local(toWorld.MultiplyPoint3x4(vertices[0])), Vector3.zero);
        for (int i = 1; i < vertices.Length; i++)
        {
            bounds.Encapsulate(Local(toWorld.MultiplyPoint3x4(vertices[i])));
        }
        Destroy(baked);
        return bounds;
    }

    /// <summary>World-space body vertices of the current pose (verification).</summary>
    public Vector3[] BakeWorldVertices()
    {
        Mesh baked = new Mesh();
        bodyRenderer.BakeMesh(baked, false);
        Vector3[] vertices = baked.vertices;
        Matrix4x4 toWorld = Matrix4x4.TRS(
            bodyRenderer.transform.position, bodyRenderer.transform.rotation, Vector3.one);
        for (int i = 0; i < vertices.Length; i++)
        {
            vertices[i] = toWorld.MultiplyPoint3x4(vertices[i]);
        }
        Destroy(baked);
        return vertices;
    }

    private float PathLength(Vector3 from, Vector3 to)
    {
        float length = 0f;
        Vector3 previous = from;
        for (int i = 0; i < path.Count; i++)
        {
            length += FlatDistance(previous, path[i]);
            previous = path[i];
        }
        return length + FlatDistance(previous, to);
    }

    private static float FlatDistance(Vector3 a, Vector3 b)
    {
        a.y = 0f;
        b.y = 0f;
        return Vector3.Distance(a, b);
    }

    private static Transform FindBone(Transform[] bones, string name)
    {
        for (int i = 0; i < bones.Length; i++)
        {
            if (bones[i].name == name)
            {
                return bones[i];
            }
        }
        return null;
    }

    private string DescribeClips()
    {
        System.Text.StringBuilder builder = new System.Text.StringBuilder();
        for (int i = 0; i < ClipCount; i++)
        {
            if (i > 0) builder.Append('|');
            builder.Append(clips[i] != null ? $"{ClipStems[i]}:{clips[i].length:F2}" : "missing");
        }
        return builder.ToString();
    }
}
