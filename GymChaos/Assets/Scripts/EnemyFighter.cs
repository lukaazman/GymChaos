using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public partial class EnemyFighter : MonoBehaviour
{
    // Profiling scopes for the hot AI paths (see GymChaosPerformanceVerifier).
    private static readonly ProfilerMarker FixedUpdateMarker = new ProfilerMarker("GymChaos.FixedUpdate");
    private const float DeadliftIgnoreRefreshInterval = 0.4f;
    private float nextDeadliftIgnoreRefreshTime;
    private bool deadliftCollisionIgnoreReady;
    private static readonly ProfilerMarker PunchContactMarker = new ProfilerMarker("GymChaos.PunchContact");
    private static readonly ProfilerMarker GroundedRootMarker = new ProfilerMarker("GymChaos.GroundedRoot");
    private static readonly ProfilerMarker VisitorTickMarker = new ProfilerMarker("GymChaos.VisitorTick");
    private static readonly ProfilerMarker TickRoamingMarker = new ProfilerMarker("GymChaos.TickRoaming");
    private static readonly ProfilerMarker BuildRoamRouteMarker = new ProfilerMarker("GymChaos.BuildRoamRoute");
    private static readonly ProfilerMarker FindRoamPointMarker = new ProfilerMarker("GymChaos.FindRoamPoint");
    private static readonly ProfilerMarker FindPurposefulRoamMarker = new ProfilerMarker("GymChaos.FindPurposefulRoam");
    private static readonly ProfilerMarker CollectRoamInterestsMarker = new ProfilerMarker("GymChaos.CollectRoamInterests");
    private static readonly ProfilerMarker VisitorDirectionMarker = new ProfilerMarker("GymChaos.VisitorDirection");
    private static readonly ProfilerMarker VisitorPathClearMarker = new ProfilerMarker("GymChaos.VisitorPathClear");
    private static readonly ProfilerMarker NavSegmentMarker = new ProfilerMarker("GymChaos.NavSegment");
    // Layer 3 is intentionally unused by the project. Enemy hitboxes use it
    // so the physics engine can keep enemies from shoving one another while
    // still colliding with the player and the gym equipment.
    public const int EnemyCollisionLayer = 3;
    // Shared scale/support contract for all runtime characters.
    public const float GameplayScale = 1.075f;
    public const float EnemyCapsuleHeight = 2.30f * GameplayScale;
    public const float EnemyCapsuleCenterY = 1.15f * GameplayScale;
    public const float EnemyStandardRadius = 0.48f * GameplayScale;
    public const float EnemyHeavyRadius = 0.54f * GameplayScale;
    public const float EnemySpawnProbeRadius = 0.52f * GameplayScale;
    public const float VisitorProbeLower = 0.55f * GameplayScale;
    public const float VisitorProbeUpper = 1.85f * GameplayScale;
    public const float GroundedVisualClearance = 0.02f;
    private const float AnimationMovementSpeedThreshold = 0.08f;

    private static readonly List<EnemyFighter> Fighters = new List<EnemyFighter>();
    private static readonly float[] MovementProbeAngles =
        { 0f, 30f, -30f, 60f, -60f, 90f, -90f, 135f, -135f, 180f };
    // Visitor route probe.
    private static readonly float[] VisitorMovementProbeAngles =
        {
            0f, 10f, -10f, 20f, -20f, 32f, -32f, 45f, -45f,
            58f, -58f, 72f, -72f, 90f, -90f, 120f, -120f, 150f, -150f,
            180f
        };
    private static readonly int[] NavigationNeighborX =
        { 1, -1, 0, 0, 1, 1, -1, -1 };
    private static readonly int[] NavigationNeighborZ =
        { 0, 0, 1, -1, 1, -1, 1, -1 };
    private static readonly string[] PurposefulRoamKeywords =
    {
        "treadmill", "bike", "bench", "smith", "latpulldown", "lat pulldown",
        "cable", "squat", "curl", "press", "dip", "rack", "rower", "rowing",
        "weightstand", "weight stand", "barbell", "dumbbell", "calisthenics",
        "reception", "locker"
    };
    private static readonly string[] NonPurposefulRoamKeywords =
    {
        "floor", "wall", "ceiling", "poster", "window", "mirror", "light",
        "beam", "column", "pillar", "mat", "carpet", "trim", "accent"
    };

    public static int ActiveCount { get; private set; }
    internal static IReadOnlyList<EnemyFighter> RegisteredFighters => Fighters;

    [SerializeField] private float maxHealth = 100f;
    [SerializeField] private float moveForce = 26f;
    [SerializeField] private float maxSpeed = 4.8f;
    [SerializeField] private float detectionRange = 7.5f;
    [SerializeField] private float attackRange = 1.9f;
    [SerializeField] private float attackImpulse = 9f;
    [SerializeField] private float attackCooldown = 1.05f;
    [SerializeField] private float lightStunDuration = 0.3f;
    [SerializeField] private float heavyStunDuration = 1.15f;
    [SerializeField] private float throwPushbackDuration = 0.28f;
    [SerializeField] private float throwPushbackMinSpeed = 2.3f;
    [SerializeField] private float throwPushbackMaxSpeed = 4.2f;
    [SerializeField] private float policeTargetRefreshInterval = 0.18f;
    [SerializeField] private float policeMinimumTargetLock = 0f;
    [SerializeField] private float roamSpeedMin = 1.15f;
    [SerializeField] private float roamSpeedMax = 2.35f;
    [SerializeField] private float roamIdleMin = 3.4f;
    [SerializeField] private float roamIdleMax = 6.2f;
    [SerializeField] private float roamRandomDestinationChance = 0.1f;
    [SerializeField] private float roamBlockedRetargetDelay = 0.72f;

    private PlayerMovement playerTarget;
    private Rigidbody body;
    private Renderer gymFloorRenderer;
    private MixamoScanRetargetAnimator externalBodyAnimator;
    private BodybuilderIdentity identity;
    private Transform currentTarget;
    private EnemyFighter currentFighterTarget;
    private Transform forcedPoliceTarget;
    private readonly List<Vector3> policePursuitRoute = new List<Vector3>();
    private int policePursuitRouteIndex;
    private Vector3 policePursuitTargetSnapshot;
    private float policePursuitNextRebuildTime;
    private bool policeRangedMode;
    private Transform lastDamageSource;
    private float health;
    private float lastAttackTime = -999f;
    private float stunnedUntilTime;
    private float throwPushbackUntilTime;
    private float nextTargetRefreshTime;
    private float targetLockedUntil;
    private float deathStartedTime;
    private bool isPolice;
    private bool isPassive;
    private bool isAggressive;
    private bool dialogueLocked;
    private bool isDead;
    private bool celebratingPlayerKill;
    private bool punchInProgress;
    private bool deathPoseFrozen;
    private bool activeCounted;
    private bool countsAsOpponent = true;
    private float floorRootY;
    private RigidbodyInterpolation visitorPoseInterpolationBeforeSnap;
    private bool visitorPoseInterpolationOverrideActive;
    private int visitorPoseSnapFramesRemaining;
    private RoamState roamState;
    private Vector3 roamTarget;
    private bool hasRoamTarget;
    private Vector3 lastRoamTarget;
    private bool hasLastRoamTarget;
    private bool roamTargetPurposeful;
    private string roamTargetInterestLabel;
    private GymExerciseStation roamTargetStation;
    private Quaternion roamTargetArrivalRotation;
    private bool hasRoamTargetArrivalRotation;
    private float roamIdleUntil;
    private float nextTreadmillDecisionTime;
    private float roamSpeed;
    private float stalledRoamTime;
    private Vector3 roamDirection;
    private float roamDirectionHoldUntil;
    private Vector3 visitorRouteDirection;
    // Route-blocker description for diagnostics. Hot probe paths store the
    // blocker parts and the text is only formatted when something reads it.
    private string routeBlockerText = "none";
    private Collider routeBlockerCollider;
    private string routeBlockerOwner;
    // Fighter behind the last route block, when known (crowd-pass recovery).
    private EnemyFighter routeBlockerFighter;
    private Vector3 routeBlockerOrigin;
    private Vector3 routeBlockerDirection;
    private bool routeBlockerHasVectors;
    private string lastVisitorRouteBlocker
    {
        get
        {
            if (routeBlockerCollider == null && routeBlockerOwner == null)
            {
                return routeBlockerText;
            }
            string name = routeBlockerCollider != null ? routeBlockerCollider.name : "missing";
            if (routeBlockerOwner != null)
            {
                return $"{name} owner={routeBlockerOwner}";
            }
            return routeBlockerHasVectors && routeBlockerCollider != null
                ? $"{name} center={routeBlockerCollider.bounds.center} size={routeBlockerCollider.bounds.size} " +
                  $"origin={routeBlockerOrigin} direction={routeBlockerDirection}"
                : name;
        }
        set
        {
            routeBlockerText = value;
            routeBlockerCollider = null;
            routeBlockerOwner = null;
            routeBlockerFighter = null;
            routeBlockerHasVectors = false;
        }
    }

    private void SetRouteBlocker(Collider hit, string owner)
    {
        routeBlockerCollider = hit;
        routeBlockerOwner = owner;
        routeBlockerFighter = hit != null ? hit.GetComponentInParent<EnemyFighter>() : null;
        routeBlockerHasVectors = false;
    }

    private void SetRouteBlocker(Collider hit, Vector3 origin, Vector3 direction)
    {
        routeBlockerCollider = hit;
        routeBlockerOwner = null;
        routeBlockerFighter = null;
        routeBlockerOrigin = origin;
        routeBlockerDirection = direction;
        routeBlockerHasVectors = true;
    }

    // Cached identity text avoids an enum-to-string allocation per probe.
    private string identityName;
    private string IdentityName => identityName ??= identity.ToString();

    // Cached lookups used by the per-probe collision filters.
    private GymVisitorAgent cachedDoorwayVisitor;
    private float nextDoorwayVisitorLookup;
    private GymVisitorAgent DoorwayVisitor
    {
        get
        {
            if (cachedDoorwayVisitor == null && Time.time >= nextDoorwayVisitorLookup)
            {
                cachedDoorwayVisitor = GetComponent<GymVisitorAgent>();
                nextDoorwayVisitorLookup = Time.time + 1f;
            }
            return cachedDoorwayVisitor;
        }
    }
    private Collider[] cachedOwnColliders;
    private float nextOwnCollidersRefresh;
    private float nextVisitorPathDiagnosticTime;
    private float nextVisitorEgressLogTime;
    private bool visitorStaticCollisionEgressRequested;
    private int deadliftCollisionRetryFrames;
    private bool deadliftEscapeApplied;
    private int deadliftEscapeCount;
    private GymDeadliftStationMarker[] deadliftNavigationStations;
    private int deadliftNavigationLookupFrame = -30;
    private GymExerciseStation treadmillStation;
    private float treadmillSpeed;
    private float treadmillUntil;
    private bool treadmillEntryActive;
    private float treadmillEntryStarted;
    private float treadmillEntryDuration;
    private Vector3 treadmillEntryStartPosition;
    private Quaternion treadmillEntryStartRotation;
    private bool treadmillExitActive;
    private float treadmillExitStarted;
    private float treadmillExitDuration;
    private Vector3 treadmillExitTargetPosition;
    private float treadmillNextSpeedChangeTime;
    private TreadmillMovementMode treadmillMovementMode;
    private GymExerciseStation pendingTreadmillStation;
    private GymVisitorAgent visitorAgent;
    private readonly RaycastHit[] movementHits = new RaycastHit[32];
    private readonly Collider[] roamOverlapHits = new Collider[64];
    private readonly List<RoamInterest> roamInterests = new List<RoamInterest>();
    private readonly List<Vector3> roamRouteWaypoints = new List<Vector3>();
    private readonly HashSet<Transform> roamInterestRoots = new HashSet<Transform>();
    private int roamRouteIndex;
    private int collectedMachineInterestCount;
    private int collectedPersonnelInterestCount;
    private bool collectedReceptionInterest;
    private bool collectedPlayerInterest;

    private sealed class RoamInterest
    {
        public Transform root;
        public Bounds bounds;
        public Vector3 position;
        public Vector3 interactionPoint;
        public Quaternion arrivalRotation;
        public GymExerciseStation station;
        public bool useInteractionPoint;
        public bool hasArrivalRotation;
        public bool personnel;
        public float weight;
        public string label;
    }

    private enum RoamState
    {
        Idle,
        Walking
    }

    public enum TreadmillMovementMode
    {
        Walking,
        Running
    }
#if UNITY_EDITOR
    // The Play Mode verifier drives one explicit hand-contact punch and then
    // measures its exact damage. Keep that editor-driven fighter from starting
    // a second automatic punch before the verifier has sampled the result;
    // normal gameplay punch cadence is unchanged outside the verifier path.
    private bool verificationPunchOnly;
#endif
    private readonly Collider[] punchContactHits = new Collider[48];
    private const float EnemyPunchDamage = 5f;
    private const float PunchHandContactRadius = 0.42f;
    private enum GokuFlightState
    {
        Grounded,
        TakingOff,
        Flying,
        Landing
    }

    // Root lift during flight. The authored flying clip already raises the
    // horizontal body about 0.85 m above the root, so this keeps the body at
    // the player's chest/eye height (about 1.2-2.1 m) instead of the ceiling.
    private const float GokuFlightHeight = 0.4f;
    private const float GokuFlightGroundClearance = 0.08f;
    private const float GokuFlightMinimumDistance = 5.2f;
    private const float GokuSpeedMultiplier = 1.5f;
    private const float GokuFlightTransitionDuration = 0.42f;
    private const float GokuFlightModelRotation = 0f;
    private GokuFlightState gokuFlightState;
    private float gokuFlightTransition;
    private float standingRootY;
    private float gokuFlightStartY;
    private float gokuFlightDetourSide;
    private float gokuFlightDetourUntil;
    private string gokuFlightLastBlocker;
    private float gokuFlightBoxedTime;
    private float gokuFlightGroundedUntil;
    private const float GokuFlightBoxedLandDelay = 0.35f;
    private const float GokuFlightBoxedGroundTime = 1.5f;
    private const float GokuFlightProgressWindow = 1.2f;
    private const float GokuFlightMinimumProgress = 0.3f;
    private float gokuFlightProgressCheckAt;
    private Vector3 gokuFlightProgressPosition;
    private readonly List<Vector3> gokuFlightRoute = new List<Vector3>();
    private int gokuFlightRouteIndex;
    private float gokuFlightRouteRebuildAt;
    private Quaternion gokuFlightStartRotation;
    private Quaternion gokuFlightTargetRotation;
    private CollisionDetectionMode gokuGroundCollisionMode = CollisionDetectionMode.Discrete;
    private Transform visitorVehicleRideAnchor;
    private bool visitorVehicleRideDetectCollisions;
    private int visitorDismountGroundSnapFrames;
    private float visitorDismountGroundY;

    public float CurrentHealth => health;
    public float MaxHealth => maxHealth;
    public bool HasTakenDamage => health < maxHealth - 0.001f;
    public bool IsDead => isDead;
    public bool IsCelebratingPlayerKill => celebratingPlayerKill;
    public bool IsPolice => isPolice;
    public bool CountsAsOpponent => countsAsOpponent;
    public bool IsPassive => isPassive;
    public bool IsAggressive => isAggressive;
    public bool IsDialogueLocked => dialogueLocked;
    public bool IsRoaming => !dialogueLocked && !isPassive && !isDead && !isAggressive &&
        currentTarget == null && treadmillStation == null;
    public bool HasRoamDestination => !dialogueLocked && !isPassive && !isDead && !isAggressive &&
        hasRoamTarget;
    public bool HasDeadliftRoamTarget => roamTargetStation != null &&
        roamTargetStation.IsDeadlift;
    public float CurrentRoamTargetDistance => hasRoamTarget
        ? Vector3.ProjectOnPlane(roamTarget - transform.position, Vector3.up).magnitude
        : 0f;
    public Vector3 CurrentRoamTarget => roamTarget;
    public bool CurrentRoamTargetIsPurposeful => hasRoamTarget && roamTargetPurposeful;
    public string CurrentRoamInterestLabel => roamTargetInterestLabel;
    public float CurrentRoamBlockedTime => stalledRoamTime;
    public int CurrentRoamRouteRemaining => Mathf.Max(0, roamRouteWaypoints.Count - roamRouteIndex);
    public int DeadliftEscapeCountForVerification => deadliftEscapeCount;
    public float CurrentPlanarSpeed => body != null
        ? Vector3.ProjectOnPlane(body.linearVelocity, Vector3.up).magnitude
        : 0f;
    public int RoamInterestCount => roamInterests.Count;
    public int RoamMachineInterestCount => collectedMachineInterestCount;
    public int RoamPersonnelInterestCount => collectedPersonnelInterestCount;
    public bool HasReceptionRoamInterest => collectedReceptionInterest;
    public bool HasPlayerRoamInterest => collectedPlayerInterest;
    public bool IsOnTreadmill => treadmillStation != null;
    public GymExerciseStation CurrentTreadmill => treadmillStation;
    public TreadmillMovementMode CurrentTreadmillMovementMode => treadmillMovementMode;
    public bool IsTreadmillRunning => treadmillStation != null &&
        treadmillMovementMode == TreadmillMovementMode.Running;
    public string TreadmillMovementModeForVerification =>
        treadmillMovementMode == TreadmillMovementMode.Running ? "running" : "walking";
    public Transform CurrentTarget => currentTarget;
    public EnemyFighter CurrentFighterTarget => currentFighterTarget;
    public Transform LastDamageSource => lastDamageSource;
    public bool IsPoliceRangedMode => policeRangedMode;
    public float CurrentAttackRange => GetCurrentAttackRange();
    public BodybuilderIdentity Identity => identity;
    public bool HasVisitorAgent => visitorAgent != null;
    public bool IsFlying => gokuFlightState == GokuFlightState.Flying;
    // Last reason the combat chase stopped or moved (constant strings, no allocation).
    private string chaseStopReason = "none";
    public string ChaseStopReasonForVerification => chaseStopReason;
    public string GokuFlightLastBlocker => gokuFlightLastBlocker;
    public bool IsGokuGrounded => identity == BodybuilderIdentity.Goku &&
        gokuFlightState == GokuFlightState.Grounded;
    public bool IsGokuFlightActive => identity == BodybuilderIdentity.Goku &&
        gokuFlightState != GokuFlightState.Grounded;
    public MixamoScanRetargetAnimator.MotionState AnimationState => externalBodyAnimator != null
        ? externalBodyAnimator.CurrentState
        : MixamoScanRetargetAnimator.MotionState.Idle;
    public float GokuFlightAuraBlend
    {
        get
        {
            if (!IsGoku() || isDead)
            {
                return 0f;
            }

            switch (gokuFlightState)
            {
                case GokuFlightState.TakingOff:
                    return SmoothStep(gokuFlightTransition);
                case GokuFlightState.Flying:
                    return 1f;
                case GokuFlightState.Landing:
                    return 1f - SmoothStep(gokuFlightTransition);
                default:
                    return 0f;
            }
        }
    }
    public Color HealthBarColor => identity == BodybuilderIdentity.Ronnie
        ? new Color(0.035f, 0.12f, 0.32f, 1f)
        : identity == BodybuilderIdentity.Manwithsuit1
            ? new Color(0.05f, 0.78f, 0.2f, 1f)
            : new Color(0.92f, 0.04f, 0.04f, 1f);

    public static bool IsFightActive
    {
        get
        {
            for (int i = 0; i < Fighters.Count; i++)
            {
                EnemyFighter fighter = Fighters[i];
                if (fighter != null && fighter.isAggressive && !fighter.isDead &&
                    !fighter.isPassive && !fighter.isPolice)
                {
                    return true;
                }
            }

            return false;
        }
    }

    public void Configure(
        BodybuilderIdentity fighterIdentity, PlayerMovement player,
        float configuredHealth, bool police, bool passive = false, bool countAsOpponent = true)
    {
        // Runtime-preloaded enemies are configured while their shared parent
        // is inactive, before Unity invokes Awake. Resolve the already-added
        // Rigidbody here so physics setup and later visitor spawn poses cannot
        // be skipped and then overwritten by the stale body pose on activation.
        if (body == null)
        {
            body = GetComponent<Rigidbody>();
        }
        identity = fighterIdentity;
        identityName = null;
        playerTarget = player;
        maxHealth = Mathf.Max(1f, configuredHealth);
        health = maxHealth;
        isPolice = police;
        isPassive = passive;
        countsAsOpponent = countAsOpponent;
        forcedPoliceTarget = null;
        policePursuitRoute.Clear();
        policePursuitRouteIndex = 0;
        policePursuitNextRebuildTime = 0f;
        policeRangedMode = false;
        lastDamageSource = null;
        floorRootY = ResolveGymFloorY(transform.position.y);
        standingRootY = floorRootY;
        isAggressive = false;
        throwPushbackUntilTime = 0f;
        roamState = RoamState.Idle;
        hasRoamTarget = false;
        pendingTreadmillStation = null;
        treadmillStation = null;
        treadmillSpeed = 0f;
        treadmillUntil = 0f;
        treadmillEntryActive = false;
        treadmillEntryStarted = 0f;
        treadmillEntryDuration = 0f;
        treadmillEntryStartPosition = Vector3.zero;
        treadmillEntryStartRotation = Quaternion.identity;
        treadmillExitActive = false;
        treadmillExitStarted = 0f;
        treadmillExitDuration = 0f;
        treadmillExitTargetPosition = Vector3.zero;
        treadmillNextSpeedChangeTime = 0f;
        roamSpeed = Random.Range(roamSpeedMin, roamSpeedMax);
        // Give each identity a deterministic stagger with a small random
        // nudge: some are already walking on Play, while at least one remains
        // idle briefly. This avoids the whole room switching state together.
        float identityStagger = Mathf.Repeat((int)identity * 0.31f, 1.35f);
        roamIdleUntil = Time.time + 0.16f + identityStagger + Random.Range(-0.08f, 0.08f);
        nextTreadmillDecisionTime = Time.time + Random.Range(3f, 8f);
        stalledRoamTime = 0f;
        lastRoamTarget = Vector3.zero;
        hasLastRoamTarget = false;
        roamTargetPurposeful = false;
        roamTargetInterestLabel = null;
        roamTargetStation = null;
        roamTargetArrivalRotation = Quaternion.identity;
        hasRoamTargetArrivalRotation = false;
        ClearRoamRoute();
        roamDirection = Vector3.zero;
        roamDirectionHoldUntil = 0f;
        deadliftCollisionRetryFrames = 180;
        deadliftEscapeApplied = false;
        gokuFlightState = GokuFlightState.Grounded;
        // Normal enemies begin neutral. They only acquire the player after a
        // player-caused hit calls BecomeAggressive; Ronnie normally uses the
        // police target search below but also becomes hostile when hit, while
        // the receptionist remains passive.
        currentTarget = null;
        currentFighterTarget = null;
        nextTargetRefreshTime = 0f;
        if (body != null)
        {
            Vector3 groundedPosition = body.position;
            groundedPosition.y = floorRootY;
            body.position = groundedPosition;
            // The animated limb hitboxes are physical compound colliders. Keep
            // the fighter root on the gym floor so animation contacts do not
            // create vertical launch impulses beside equipment.
            body.useGravity = false;
            body.isKinematic = false;
            body.constraints = RigidbodyConstraints.FreezePositionY |
                RigidbodyConstraints.FreezeRotationX |
                RigidbodyConstraints.FreezeRotationZ;
            body.linearVelocity = new Vector3(body.linearVelocity.x, 0f, body.linearVelocity.z);
            body.angularVelocity = Vector3.zero;
        }
        if (!countAsOpponent && activeCounted)
        {
            ActiveCount = Mathf.Max(0, ActiveCount - 1);
            activeCounted = false;
        }
        else if (countAsOpponent && !activeCounted && isActiveAndEnabled && !isDead)
        {
            ActiveCount++;
            activeCounted = true;
        }
    }

    private void Awake()
    {
        gameObject.layer = EnemyCollisionLayer;
        Physics.IgnoreLayerCollision(EnemyCollisionLayer, EnemyCollisionLayer, true);
        body = GetComponent<Rigidbody>();
        gokuGroundCollisionMode = body.collisionDetectionMode;
        externalBodyAnimator = GetComponentInChildren<MixamoScanRetargetAnimator>(true);
        health = maxHealth;
        Fighters.Add(this);
        if (countsAsOpponent)
        {
            ActiveCount++;
            activeCounted = true;
        }
    }

    private void OnDestroy()
    {
        EndTreadmillVisit();
        Fighters.Remove(this);
        if (activeCounted)
        {
            ActiveCount = Mathf.Max(0, ActiveCount - 1);
            activeCounted = false;
        }
    }

    private void OnEnable()
    {
        if (body != null && countsAsOpponent && !activeCounted && !isDead)
        {
            ActiveCount++;
            activeCounted = true;
        }
    }

    private void OnDisable()
    {
        ReleasePoliceDoorRequest();
        RestoreDoorwayCrowdCollisions(true);
        // Disabling resets ignore pairs on our colliders; only drop the records.
        visitorPushesLooseItemsUntil = -1f;
        ClearVisitorCrowdPass(false);
        // Keep station ownership and attached squat bars from surviving a
        // disable before the visitor director gets another Update tick.
        if (visitorAgent != null)
        {
            visitorAgent.CancelForCombat();
        }
        if (activeCounted)
        {
            ActiveCount = Mathf.Max(0, ActiveCount - 1);
            activeCounted = false;
        }
    }

    private void FixedUpdate()
    {
        using var profileScope = FixedUpdateMarker.Auto();
        if (visitorCrowdPass != null)
        {
            TickVisitorCrowdPass();
        }
        if (visitorVehicleRideAnchor != null && !isDead)
        {
            Vector3 ridePosition = visitorVehicleRideAnchor.position;
            Quaternion rideRotation = visitorVehicleRideAnchor.rotation;
            if (body != null)
            {
                body.position = ridePosition;
                body.rotation = rideRotation;
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }
            transform.SetPositionAndRotation(ridePosition, rideRotation);
            SetAnimatedMovement(false);
            externalBodyAnimator?.SetFlying(false);
            return;
        }
        // The station is runtime-created and imported enemy colliders can be
        // attached after the first physics tick. Keep the ignore pair alive
        // for the complete session, not only the initial retry window; an
        // enemy can otherwise walk back onto the platform after the one-shot
        // escape has already expired.
        // Re-asserting the ignore pairs walks every collider of both sides,
        // so refresh it a few times per second instead of on every physics
        // step; the pairs persist between refreshes.
        if (Time.time >= nextDeadliftIgnoreRefreshTime)
        {
            nextDeadliftIgnoreRefreshTime = Time.time + DeadliftIgnoreRefreshInterval;
            deadliftCollisionIgnoreReady =
                GymLooseItemSpawner.EnsureDeadliftStationCollisionIgnore(this);
        }
        bool collisionIgnoreReady = deadliftCollisionIgnoreReady;
        // Keep the deadlift platform collision exception, but never teleport an
        // enemy out of a route collision. Steer toward the nearest safe point
        // for one physics tick and let the normal visitor/roaming controller
        // resume once the platform footprint is clear.
        if (body != null)
        {
            if (GymLooseItemSpawner.TryGetDeadliftEscapePointForEnemy(
                    this, 0.55f, out Vector3 escapePoint))
            {
                Vector3 away = Vector3.ProjectOnPlane(
                    escapePoint - body.position, Vector3.up);
                if (away.sqrMagnitude > 0.01f)
                {
                    float rerouteSpeed = Mathf.Clamp(
                        4.4f + away.magnitude * 2f, 4.4f, 6.8f);
                    body.linearVelocity =
                        away.normalized * rerouteSpeed +
                        Vector3.Project(body.linearVelocity, Vector3.up);
                    body.MoveRotation(Quaternion.RotateTowards(
                        body.rotation,
                        Quaternion.LookRotation(away.normalized, Vector3.up),
                        420f * Time.fixedDeltaTime));
                    SetAnimatedMovementFromVelocity(rerouteSpeed);
                }
                if (!deadliftEscapeApplied)
                {
                    deadliftEscapeCount++;
                    Debug.Log(
                        $"GYMCHAOS_DEADLIFT_ENEMY_REROUTE identity={identity} " +
                        $"fromPlatform=true collisionsIgnored={collisionIgnoreReady} " +
                        $"position={body.position} target={escapePoint}", this);
                }
                deadliftEscapeApplied = true;
                return;
            }

            // Re-arm the guard after the enemy has left. This makes a later
            // accidental route/collision safe without resetting its destination.
            deadliftEscapeApplied = false;
        }
        if (deadliftCollisionRetryFrames > 0)
        {
            deadliftCollisionRetryFrames--;
        }

        if (dialogueLocked && !isDead)
        {
            StopMovingPhysicsImmediately();
            SetAnimatedMovement(false);
            return;
        }

        using (PunchContactMarker.Auto()) ProcessPunchContact();

        if (isDead)
        {
            EndTreadmillVisit();
            RestoreGokuGroundPhysicsForDeath();
            UpdatePermanentDeathPose();
            return;
        }

        using (GroundedRootMarker.Auto()) KeepGroundedRoot();

        if (visitorPoseSnapFramesRemaining > 0)
        {
            visitorPoseSnapFramesRemaining--;
            if (visitorPoseSnapFramesRemaining == 0 && body != null)
            {
                body.interpolation = visitorPoseInterpolationBeforeSnap;
                visitorPoseInterpolationOverrideActive = false;
            }
        }

        bool visitorHandled;
        using (VisitorTickMarker.Auto())
        {
            visitorHandled = visitorAgent != null && visitorAgent.isActiveAndEnabled &&
                visitorAgent.TickPhysics(this);
        }
        if (visitorHandled)
        {
            return;
        }

        if (celebratingPlayerKill)
        {
            RestoreGokuGroundPhysicsForDeath();
            StopMovingPhysicsImmediately();
            externalBodyAnimator?.TriggerCelebration();
            return;
        }

        if (isPassive)
        {
            StopMoving();
            return;
        }

        if (playerTarget == null)
        {
            playerTarget = FindFirstObjectByType<PlayerMovement>();
        }

        GymExperienceService progression = GymExperienceService.Active;
        if (progression != null && progression.ShouldSuppressEnemyAutoTarget(this) &&
            !isAggressive)
        {
            currentTarget = null;
            currentFighterTarget = null;
            TickRoaming();
            return;
        }
        if (progression != null && progression.ShouldForceNegativeAutoTarget(this) &&
            !isAggressive)
        {
            BecomeAggressive(playerTarget);
        }

        if (isPolice)
        {
            // Police/Ronnie is a room-wide intervention observer. Refresh on
            // every physics step so a fight participant that becomes the
            // nearest person is selected immediately, regardless of the
            // previous target's distance or refresh timer.
            RefreshPoliceTarget(true);
        }
        else if (isAggressive
#if UNITY_EDITOR
            || verificationPunchOnly
#endif
            )
        {
            currentTarget = playerTarget != null ? playerTarget.transform : null;
            currentFighterTarget = null;
        }
        else
        {
            currentTarget = null;
            currentFighterTarget = null;
        }

        if (currentFighterTarget == null && playerTarget != null && playerTarget.IsDead &&
            currentTarget == playerTarget.transform)
        {
            StopMoving();
            return;
        }

        if (currentTarget == null)
        {
            chaseStopReason = "no-target";
            TickRoaming();
            return;
        }

        EndTreadmillVisit();

        if (currentFighterTarget == null && playerTarget != null &&
            currentTarget == playerTarget.transform && playerTarget.IsExercising)
        {
            if (isPolice)
            {
                RefreshPoliceTarget(true, false);
            }
            if (currentTarget == playerTarget.transform)
            {
                StopMoving();
                return;
            }

            // RefreshPoliceTarget can clear the player target when the player
            // starts exercising and no aggressive fighter remains in the room.
            // Re-enter the roaming path instead of dereferencing a cleared
            // target below.
            if (currentTarget == null)
            {
                TickRoaming();
                return;
            }
        }

        if (Time.time < stunnedUntilTime)
        {
            chaseStopReason = "stunned";
            SetAnimatedMovement(false);
            return;
        }

        if (isPolice)
        {
            // Pursuit can stop near the door; always release crowd-pass pairs
            // once the officer is clear of the doorway.
            RestoreDoorwayCrowdCollisions(false);
        }
        if (isPolice && TickAuthoredPolicePursuit())
        {
            return;
        }

        Vector3 planarToTarget = Vector3.ProjectOnPlane(
            currentTarget.position - transform.position, Vector3.up);
        float distance = planarToTarget.magnitude;

        float chaseSpeed = GetChaseSpeed();
        if (distance > GetDetectionRange())
        {
            chaseStopReason = "out-of-detection-range";
            TickRoaming();
            return;
        }

        if (Time.time < throwPushbackUntilTime)
        {
            // Let the impact velocity carry the fighter backward for a short
            // visible beat. The animation must follow the velocity that
            // actually exists; forcing Run while the body is stopped creates
            // the reported foot-glide.
            SetAnimatedMovementFromVelocity(chaseSpeed);
            return;
        }

        bool shouldGokuFly = IsGoku() && distance > GokuFlightMinimumDistance &&
            Time.time >= gokuFlightGroundedUntil;
        if (IsGoku() && !UpdateGokuFlight(shouldGokuFly, planarToTarget))
        {
            chaseStopReason = "goku-flight";
            return;
        }

        body.WakeUp();
        if (distance <= GetCurrentAttackRange())
        {
            chaseStopReason = "attack-range";
            StopForAttack(planarToTarget);
            if (CanStartAutomaticPunch() && !punchInProgress &&
                Time.time >= lastAttackTime + attackCooldown)
            {
                lastAttackTime = Time.time;
                Attack(planarToTarget.sqrMagnitude > 0.01f
                    ? planarToTarget.normalized : transform.forward);
            }
            return;
        }

        if (distance > 0.15f)
        {
            Vector3 moveDirection = planarToTarget.normalized;
            if (IsGoku() && gokuFlightState == GokuFlightState.Flying)
            {
                // UpdateGokuFlight already steers toward the current target every
                // FixedUpdate. Do not apply the grounded velocity path as well.
            }
            else
            {
                moveDirection = FindClearMovementDirection(moveDirection, distance, false, false);
                if (moveDirection.sqrMagnitude < 0.001f)
                {
                    chaseStopReason = "no-clear-direction";
                    StopMoving();
                    return;
                }
                chaseStopReason = "chasing";
                Vector3 planarVelocity = Vector3.ProjectOnPlane(body.linearVelocity, Vector3.up);
                Vector3 desiredVelocity = moveDirection * chaseSpeed;
                // Directly steer the planar Rigidbody velocity. The old 11 N force
                // was negligible against the 85 kg body and made the walking clip
                // play while the fighter remained effectively stationary.
                planarVelocity = Vector3.MoveTowards(
                    planarVelocity, desiredVelocity, Mathf.Max(moveForce, 12f) * Time.fixedDeltaTime);
                body.linearVelocity = planarVelocity + Vector3.Project(body.linearVelocity, Vector3.up);

                Quaternion lookRotation = Quaternion.LookRotation(moveDirection, Vector3.up);
                transform.rotation = Quaternion.Slerp(
                    transform.rotation, lookRotation, 10f * Time.fixedDeltaTime);
            }
        }

        // Apply the desired Rigidbody velocity first, then choose the visible
        // locomotion state from the resulting velocity. The old order selected
        // Run from target distance before steering, so a blocked or freshly
        // spawned enemy could visibly run in place and slide across the floor.
        if (!(IsGoku() && gokuFlightState == GokuFlightState.Flying))
        {
            SetAnimatedMovementFromVelocity(chaseSpeed);
        }

    }

    public void SetDialogueLocked(bool locked, Transform conversationPartner = null)
    {
        if (externalBodyAnimator == null)
        {
            externalBodyAnimator = GetComponentInChildren<MixamoScanRetargetAnimator>(true);
        }
        externalBodyAnimator?.SetConversationActive(locked);

        if (dialogueLocked == locked)
        {
            return;
        }

        dialogueLocked = locked;
        StopMovingPhysicsImmediately();
        SetAnimatedMovement(false);

        if (locked)
        {
            if (conversationPartner != null)
            {
                Vector3 direction = Vector3.ProjectOnPlane(
                    conversationPartner.position - transform.position, Vector3.up);
                if (direction.sqrMagnitude > 0.001f)
                {
                    transform.rotation = Quaternion.LookRotation(direction, Vector3.up);
                }
            }
            return;
        }

        if (!isDead && !isAggressive && !isPassive)
        {
            roamState = RoamState.Idle;
            roamIdleUntil = Time.time + 0.35f;
            stalledRoamTime = 0f;
        }
    }

    private void LateUpdate()
    {
        if (visitorDismountGroundSnapFrames <= 0 ||
            visitorVehicleRideAnchor != null || isDead)
        {
            return;
        }

        visitorDismountGroundSnapFrames--;
        Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
        float lowestVisiblePoint = float.PositiveInfinity;
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null || !renderer.enabled ||
                (!(renderer is SkinnedMeshRenderer) && !(renderer is MeshRenderer)))
            {
                continue;
            }
            lowestVisiblePoint = Mathf.Min(lowestVisiblePoint, renderer.bounds.min.y);
        }

        if (lowestVisiblePoint == float.PositiveInfinity) return;
        float correction = visitorDismountGroundY - lowestVisiblePoint;
        if (Mathf.Abs(correction) <= 0.01f || Mathf.Abs(correction) > 2.5f) return;
        Vector3 corrected = body != null ? body.position : transform.position;
        corrected.y += correction;
        if (body != null)
        {
            body.position = corrected;
            body.linearVelocity = Vector3.ProjectOnPlane(body.linearVelocity, Vector3.up);
        }
        transform.position = corrected;
        standingRootY = corrected.y;
        floorRootY = corrected.y;
        Physics.SyncTransforms();
    }

    public string LastVisitorRouteBlocker => lastVisitorRouteBlocker;

    public Vector3 VisitorPhysicsPosition =>
        body != null ? body.position : transform.position;

    private void OnCollisionEnter(Collision collision)
    {
        PickupItem item = collision.rigidbody != null
            ? collision.rigidbody.GetComponentInParent<PickupItem>()
            : null;
        if (item == null || !item.IsThrowableWeapon || !item.WasThrownRecently)
        {
            return;
        }

        float impactSpeed = collision.relativeVelocity.magnitude;
        float minimumImpactSpeed = item.ItemType == WeightType.Barbell ||
            item.ItemType == WeightType.EzBar || item.ItemType == WeightType.Radio
            ? 0.8f : 3f;
        if (impactSpeed < minimumImpactSpeed || !item.TryConsumeThrownHit())
        {
            return;
        }

        float damage = item.GetImpactDamage(impactSpeed);
        Vector3 impulse = collision.relativeVelocity.normalized *
            Mathf.Clamp(impactSpeed * item.ImpactMultiplier, 5f, 28f);
        ContactPoint contact = collision.contactCount > 0 ? collision.GetContact(0) : default;
        Vector3 bloodPoint = collision.contactCount > 0 ? contact.point : transform.position + Vector3.up;
        Vector3 bloodNormal = collision.contactCount > 0 ? contact.normal : -collision.relativeVelocity.normalized;
        GymAudio.Play(GymSoundEffect.ThrownEnemyImpact, bloodPoint, 0.9f);
        BloodSplatter.SpawnOnBody(
            this, bloodPoint, bloodNormal,
            BloodSplatter.GetThrownScale(item.ItemType, item.BaseMass),
            collision.contactCount > 0 && contact.thisCollider != null
                ? contact.thisCollider.transform
                : transform);
        if (isDead)
        {
            ApplyCorpseImpact(impulse * 0.75f);
        }
        else
        {
            TakeThrowableHit(impulse, damage, 0f, false);
            GymExperienceService.Active?.RegisterCombatHit(this);
            if (IsDead)
            {
                GymExperienceService.Active?.RegisterEnemyDefeat(this);
            }
        }
    }
}
