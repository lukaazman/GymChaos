using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class EnemyFighter : MonoBehaviour
{
    // Layer 3 is intentionally unused by the project. Enemy hitboxes use it
    // so the physics engine can keep enemies from shoving one another while
    // still colliding with the player and the gym equipment.
    public const int EnemyCollisionLayer = 3;

    private static readonly List<EnemyFighter> Fighters = new List<EnemyFighter>();
    private static readonly float[] MovementProbeAngles =
        { 0f, 30f, -30f, 60f, -60f, 90f, -90f, 135f, -135f, 180f };
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
    private bool isDead;
    private bool celebratingPlayerKill;
    private bool punchInProgress;
    private bool deathPoseFrozen;
    private bool activeCounted;
    private float floorRootY;
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

    private const float GokuFlightHeight = 2.35f;
    private const float GokuFlightGroundClearance = 0.08f;
    private const float GokuFlightMinimumDistance = 5.2f;
    private const float GokuSpeedMultiplier = 1.5f;
    private const float GokuFlightTransitionDuration = 0.42f;
    private const float GokuFlightModelRotation = 90f;
    private GokuFlightState gokuFlightState;
    private float gokuFlightTransition;
    private float standingRootY;
    private float gokuFlightStartY;
    private Quaternion gokuFlightStartRotation;
    private Quaternion gokuFlightTargetRotation;
    private CollisionDetectionMode gokuGroundCollisionMode = CollisionDetectionMode.Discrete;

    public float CurrentHealth => health;
    public float MaxHealth => maxHealth;
    public bool HasTakenDamage => health < maxHealth - 0.001f;
    public bool IsDead => isDead;
    public bool IsCelebratingPlayerKill => celebratingPlayerKill;
    public bool IsPolice => isPolice;
    public bool IsAggressive => isAggressive;
    public bool IsRoaming => !isPassive && !isDead && !isAggressive &&
        currentTarget == null && treadmillStation == null;
    public bool HasRoamDestination => !isPassive && !isDead && !isAggressive &&
        hasRoamTarget;
    public float CurrentRoamTargetDistance => hasRoamTarget
        ? Vector3.ProjectOnPlane(roamTarget - transform.position, Vector3.up).magnitude
        : 0f;
    public Vector3 CurrentRoamTarget => roamTarget;
    public bool CurrentRoamTargetIsPurposeful => hasRoamTarget && roamTargetPurposeful;
    public string CurrentRoamInterestLabel => roamTargetInterestLabel;
    public float CurrentRoamBlockedTime => stalledRoamTime;
    public int CurrentRoamRouteRemaining => Mathf.Max(0, roamRouteWaypoints.Count - roamRouteIndex);
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
    public Transform CurrentTarget => currentTarget;
    public EnemyFighter CurrentFighterTarget => currentFighterTarget;
    public BodybuilderIdentity Identity => identity;
    public bool HasVisitorAgent => visitorAgent != null;
    public bool IsFlying => gokuFlightState == GokuFlightState.Flying;
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

    public static void CelebrateAllLivingEnemies()
    {
        // The receptionist also uses EnemyFighter for damage/death markers,
        // but is a passive NPC and must not join the enemy victory loop.
        for (int i = 0; i < Fighters.Count; i++)
        {
            EnemyFighter fighter = Fighters[i];
            if (fighter == null || fighter.isDead || fighter.isPassive)
            {
                continue;
            }

            fighter.CelebratePlayerKill();
        }
    }

    public void Configure(
        BodybuilderIdentity fighterIdentity, PlayerMovement player,
        float configuredHealth, bool police, bool passive = false, bool countAsOpponent = true)
    {
        identity = fighterIdentity;
        playerTarget = player;
        maxHealth = Mathf.Max(1f, configuredHealth);
        health = maxHealth;
        isPolice = police;
        isPassive = passive;
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
    }

    public void SetTarget(PlayerMovement player)
    {
        playerTarget = player;
        if (!isPolice && isAggressive)
        {
            currentTarget = player != null ? player.transform : null;
            currentFighterTarget = null;
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
        ActiveCount++;
        activeCounted = true;
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
        if (body != null && !activeCounted && !isDead)
        {
            ActiveCount++;
            activeCounted = true;
        }
    }

    private void OnDisable()
    {
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
        ProcessPunchContact();

        if (isDead)
        {
            EndTreadmillVisit();
            RestoreGokuGroundPhysicsForDeath();
            UpdatePermanentDeathPose();
            return;
        }

        KeepGroundedRoot();

        if (visitorAgent != null && visitorAgent.isActiveAndEnabled &&
            visitorAgent.TickPhysics(this))
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
            SetAnimatedMovement(false);
            return;
        }

        Vector3 planarToTarget = Vector3.ProjectOnPlane(
            currentTarget.position - transform.position, Vector3.up);
        float distance = planarToTarget.magnitude;

        float chaseSpeed = GetChaseSpeed();
        if (distance > GetDetectionRange())
        {
            TickRoaming();
            return;
        }

        if (Time.time < throwPushbackUntilTime)
        {
            // Let the impact velocity carry the fighter backward for a short
            // visible beat. Keep the Run animation active and let normal chase
            // steering resume after the pushback window.
            float impactPlanarSpeed =
                Vector3.ProjectOnPlane(body.linearVelocity, Vector3.up).magnitude;
            SetAnimatedMovement(
                true, impactPlanarSpeed / Mathf.Max(0.01f, chaseSpeed));
            return;
        }

        bool shouldGokuFly = IsGoku() && distance > GokuFlightMinimumDistance;
        if (IsGoku() && !UpdateGokuFlight(shouldGokuFly, planarToTarget))
        {
            return;
        }

        body.WakeUp();
        if (distance <= attackRange)
        {
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

        float planarSpeed = Vector3.ProjectOnPlane(body.linearVelocity, Vector3.up).magnitude;
        bool policeStillMovingNearTarget = isPolice && planarSpeed > 0.12f;
        SetAnimatedMovement(
            distance > attackRange || policeStillMovingNearTarget,
            planarSpeed / Mathf.Max(0.01f, chaseSpeed));

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
                    StopMoving();
                    return;
                }
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

    }

    public void BecomeAggressive(PlayerMovement source = null)
    {
        if (isDead || isPassive)
        {
            return;
        }

        // Repeated throwable contacts can call this method several times
        // while Goku is still flying. Do not restart the visitor/combat
        // handoff on every contact; that can keep interrupting the landing
        // frame and leave the fighter in a visual idle state.
        if (isAggressive)
        {
            if (source != null)
            {
                playerTarget = source;
                currentTarget = source.transform;
                currentFighterTarget = null;
            }
            return;
        }

        if (visitorAgent != null)
        {
            visitorAgent.CancelForCombat();
        }

        if (source != null)
        {
            playerTarget = source;
        }
        else if (playerTarget == null)
        {
            playerTarget = FindFirstObjectByType<PlayerMovement>();
        }

        isAggressive = true;
        currentTarget = playerTarget != null ? playerTarget.transform : null;
        currentFighterTarget = null;
        pendingTreadmillStation = null;
        EndTreadmillVisit();
        roamState = RoamState.Idle;
        hasRoamTarget = false;
        hasLastRoamTarget = false;
        roamTargetPurposeful = false;
        roamTargetInterestLabel = null;
        roamTargetStation = null;
        hasRoamTargetArrivalRotation = false;
        ClearRoamRoute();
        roamDirection = Vector3.zero;
        roamDirectionHoldUntil = 0f;

        Debug.Log($"GYMCHAOS_ENEMY_AGGRO identity={identity} source=player", this);
    }

    public void AttachVisitorAgent(GymVisitorAgent agent)
    {
        visitorAgent = agent;
        EndTreadmillVisit();
        currentTarget = null;
        currentFighterTarget = null;
        isAggressive = false;
    }

    public void DetachVisitorAgent(GymVisitorAgent agent)
    {
        if (visitorAgent == agent)
        {
            visitorAgent = null;
        }
    }

    public void SetVisitorSpawnPose(Vector3 position, Quaternion rotation)
    {
        position.y = ResolveGymFloorY(position.y);
        if (body != null)
        {
            body.position = position;
            body.rotation = rotation;
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }
        else
        {
            transform.SetPositionAndRotation(position, rotation);
        }

        standingRootY = position.y;
        floorRootY = position.y;
        transform.SetPositionAndRotation(position, rotation);
    }

    public bool MoveVisitorTo(Vector3 destination, float speed, bool allowOutsideRoom)
    {
        return MoveVisitorTo(destination, speed, allowOutsideRoom, null);
    }

    public bool MoveVisitorTo(
        Vector3 destination,
        float speed,
        bool allowOutsideRoom,
        GymExerciseStation targetStation)
    {
        if (body == null || isDead)
        {
            return false;
        }

        destination.y = standingRootY;
        Vector3 toDestination = Vector3.ProjectOnPlane(destination - body.position, Vector3.up);
        float distance = toDestination.magnitude;
        if (distance <= 0.34f)
        {
            StopMovingPhysicsImmediately();
            SetAnimatedMovement(false);
            return true;
        }

        Vector3 desired = toDestination / distance;
        Vector3 direction = FindVisitorMovementDirection(
            desired, Mathf.Min(distance, 1.25f), allowOutsideRoom, targetStation);
        if (direction.sqrMagnitude < 0.001f)
        {
            StopMovingPhysicsImmediately();
            SetAnimatedMovement(false);
            return false;
        }

        float movementSpeed = Mathf.Clamp(speed, 0.8f, maxSpeed);
        Vector3 planarVelocity = Vector3.ProjectOnPlane(body.linearVelocity, Vector3.up);
        Vector3 desiredVelocity = direction * movementSpeed;
        planarVelocity = Vector3.MoveTowards(
            planarVelocity, desiredVelocity,
            Mathf.Max(moveForce * 0.55f, 8f) * Time.fixedDeltaTime);
        body.linearVelocity = planarVelocity + Vector3.Project(body.linearVelocity, Vector3.up);
        Quaternion lookRotation = Quaternion.LookRotation(direction, Vector3.up);
        transform.rotation = Quaternion.Slerp(
            transform.rotation, lookRotation, 9f * Time.fixedDeltaTime);
        SetAnimatedMovement(true, Mathf.Clamp01(movementSpeed / Mathf.Max(0.01f, maxSpeed)));
        return false;
    }

    public void StopVisitorMovement()
    {
        StopMovingPhysicsImmediately();
        SetAnimatedMovement(false);
    }

    public void ResumeVisitorRoaming()
    {
        if (isDead || isAggressive || isPassive)
        {
            return;
        }

        EndTreadmillVisit();
        currentTarget = null;
        currentFighterTarget = null;
        pendingTreadmillStation = null;
        hasRoamTarget = false;
        roamTargetPurposeful = false;
        roamTargetInterestLabel = null;
        roamTargetStation = null;
        hasRoamTargetArrivalRotation = false;
        ClearRoamRoute();
        roamDirection = Vector3.zero;
        roamDirectionHoldUntil = 0f;
        stalledRoamTime = 0f;
        roamState = RoamState.Walking;
        SelectRoamDestination();
    }

#if UNITY_EDITOR
    // The editor play-mode verifier now has to opt a fighter into combat just
    // like gameplay does, because sight alone is intentionally non-hostile.
    public void SetAggressiveForVerification(PlayerMovement source)
    {
        BecomeAggressive(source);
    }

    public bool BeginTreadmillForVerification(GymExerciseStation station, float speed)
    {
        if (isDead || isPolice || isPassive || isAggressive)
        {
            return false;
        }

        pendingTreadmillStation = null;
        hasRoamTarget = false;
        roamState = RoamState.Walking;
        return TryBeginTreadmillVisit(station);
    }

    public bool QueueTreadmillForVerification(GymExerciseStation station)
    {
        if (station == null || isDead || isPolice || isPassive || isAggressive ||
            !station.IsAvailableForEnemy(this))
        {
            return false;
        }

        currentTarget = null;
        currentFighterTarget = null;
        EndTreadmillVisit();
        pendingTreadmillStation = station;
        roamTargetStation = station;
        roamTarget = TryFindTreadmillApproachPoint(
                station, out Vector3 queuedApproachPoint)
            ? queuedApproachPoint
            : station.EnemyPosition;
        hasRoamTarget = true;
        roamTargetPurposeful = true;
        roamTargetInterestLabel = station.DisplayName;
        roamTargetArrivalRotation = station.EnemyRotation;
        hasRoamTargetArrivalRotation = true;
        roamState = RoamState.Walking;
        stalledRoamTime = 0f;
        roamDirection = Vector3.zero;
        roamDirectionHoldUntil = 0f;
        BuildRoamRouteToTarget(ShouldAllowTargetEquipment());
        return true;
    }

    public void EndTreadmillForVerification()
    {
        EndTreadmillVisit();
    }
#endif

    private void TickRoaming()
    {
        // Angered fighters must stay in their combat state. They may stop or
        // retarget when combat logic requires it, but they must never fall
        // back into the room's idle/wandering loop while angered.
        if (isAggressive)
        {
            StopMovingPhysicsImmediately();
            SetAnimatedMovement(false);
            return;
        }

        // A treadmill visit is its own animation state. It must be evaluated
        // before the normal idle/stun roaming branch so the fighter never
        // drops into an idle pose while the workout session is active.
        if (treadmillStation != null)
        {
            if (treadmillEntryActive)
            {
                TickTreadmillEntry();
                return;
            }

            if (treadmillExitActive)
            {
                TickTreadmillExit();
                return;
            }

            if (Time.time >= treadmillUntil)
            {
                BeginTreadmillExit();
                return;
            }

            UpdateTreadmillTargetSpeed();
            if (!treadmillStation.TickEnemyTreadmill(
                    this, Time.fixedDeltaTime, treadmillSpeed))
            {
                EndTreadmillVisit();
                hasRoamTarget = false;
                ClearRoamRoute();
                SelectRoamDestination();
                return;
            }

            StopMovingPhysicsImmediately();
            body.position = treadmillStation.EnemyPosition;
            body.rotation = Quaternion.Slerp(
                body.rotation, treadmillStation.EnemyRotation, 12f * Time.fixedDeltaTime);
            SetAnimatedMovement(
                true, treadmillStation.TreadmillSpeed01(
                    treadmillStation.CurrentTreadmillSpeed));
            return;
        }

        if (Time.time < stunnedUntilTime)
        {
            StopMovingPhysicsImmediately();
            SetAnimatedMovement(false);
            return;
        }

        if (roamState == RoamState.Idle)
        {
            StopMovingPhysicsImmediately();
            SetAnimatedMovement(false);
            if (Time.time >= roamIdleUntil)
            {
                SelectRoamDestination();
            }
            return;
        }

        if (pendingTreadmillStation != null && !pendingTreadmillStation.IsAvailableForEnemy(this))
        {
            pendingTreadmillStation = null;
            // A treadmill can become occupied while this fighter is walking
            // toward it. Reroute immediately; this is not a natural waypoint
            // pause and should not create an idle flicker.
            SelectRoamDestination();
            return;
        }

        if (!hasRoamTarget)
        {
            SelectRoamDestination();
            return;
        }

        AdvanceRoamRoute();
        Vector3 steeringTarget = GetRoamSteeringTarget();
        Vector3 toTarget = Vector3.ProjectOnPlane(
            steeringTarget - transform.position, Vector3.up);
        float distance = toTarget.magnitude;
        bool routeComplete = roamRouteIndex >= roamRouteWaypoints.Count;
        float finalDistance = Vector3.ProjectOnPlane(
            roamTarget - transform.position, Vector3.up).magnitude;
        if (routeComplete && finalDistance <= 0.72f)
        {
            if (pendingTreadmillStation != null)
            {
                if (TryBeginTreadmillVisit(pendingTreadmillStation))
                {
                    pendingTreadmillStation = null;
                    return;
                }

                pendingTreadmillStation = null;
            }

            if (hasRoamTargetArrivalRotation)
            {
                transform.rotation = roamTargetArrivalRotation;
            }

            // A normal free-roam waypoint is a pass-through. Do not inject an
            // idle animation here: a single-direction route must stay in Run
            // across target handoffs. Idle is reserved for the initial
            // stagger or the genuine no-waypoint fallback below.
            SelectRoamDestination();
            return;
        }

        bool allowTargetEquipment = ShouldAllowTargetEquipment() &&
            (roamRouteWaypoints.Count == 0 ||
             roamRouteIndex >= roamRouteWaypoints.Count - 1);
        Vector3 desiredDirection = toTarget.normalized;
        Vector3 direction = FindClearMovementDirection(
            desiredDirection, distance, true, allowTargetEquipment);
        direction = StabilizeRoamDirection(
            desiredDirection, distance, direction, allowTargetEquipment);
        if (direction.sqrMagnitude < 0.001f)
        {
            stalledRoamTime += Time.fixedDeltaTime;
            // A capsule probe can be blocked for one or two physics frames by
            // a nearby equipment edge or an enemy separation update even when
            // the current straight route is still the correct route. Do a
            // short continuity probe before declaring a real blockage. This
            // prevents the visible Run -> Idle -> Run flicker that looked like
            // a one-second stop on every roaming path handoff.
            Vector3 continuityDirection = GetRoamContinuityDirection(
                desiredDirection, distance, allowTargetEquipment);
            if (continuityDirection.sqrMagnitude > 0.001f)
            {
                stalledRoamTime = 0f;
                ApplyRoamMovement(continuityDirection);
                return;
            }

            // Preserve the locomotion state briefly while a genuine obstacle
            // is being retargeted. Intentional rare idles and actual direction
            // changes still use the normal idle/turn path; only this transient
            // path-test failure gets the continuity grace period.
            bool sameRouteDirection = roamDirection.sqrMagnitude > 0.001f &&
                Vector3.Dot(roamDirection.normalized, desiredDirection) > 0.82f;
            if (sameRouteDirection && stalledRoamTime < 0.18f)
            {
                // A failed capsule probe can be caused by a one-frame enemy
                // separation update, not a real turn. Keep the established
                // direction and Run state so the character does not flicker
                // through a visible stop/glide/Run transition.
                ApplyRoamMovement(roamDirection.normalized);
                return;
            }

            StopMovingPhysicsImmediately();
            SetAnimatedMovement(false);
            // Keep the pause short and recover to a new meaningful target.
            // Waiting several seconds here made a rare wall contact look like
            // a permanent AI freeze.
            if (stalledRoamTime >= roamBlockedRetargetDelay)
            {
                if (roamTargetPurposeful &&
                    BuildRoamRouteToTarget(ShouldAllowTargetEquipment()))
                {
                    stalledRoamTime = 0f;
                    return;
                }

                hasRoamTarget = false;
                pendingTreadmillStation = null;
                ClearRoamRoute();
                roamDirection = Vector3.zero;
                SelectRoamDestination();
            }
            return;
        }

        stalledRoamTime = 0f;
        ApplyRoamMovement(direction);
    }

    private Vector3 GetRoamContinuityDirection(
        Vector3 desiredDirection, float targetDistance, bool allowTargetEquipment)
    {
        desiredDirection = Vector3.ProjectOnPlane(desiredDirection, Vector3.up);
        if (desiredDirection.sqrMagnitude < 0.0001f)
        {
            return Vector3.zero;
        }

        desiredDirection.Normalize();
        float shortLookAhead = Mathf.Clamp(
            Mathf.Min(Mathf.Max(0.18f, targetDistance), 0.34f), 0.18f, 0.34f);
        if (roamDirection.sqrMagnitude > 0.001f)
        {
            Vector3 held = Vector3.ProjectOnPlane(roamDirection, Vector3.up).normalized;
            if (Vector3.Dot(held, desiredDirection) > 0.82f &&
                IsInsideRoom(held, shortLookAhead) &&
                IsMovementPathClear(held, shortLookAhead, allowTargetEquipment))
            {
                return held;
            }
        }

        if (IsInsideRoom(desiredDirection, shortLookAhead) &&
            IsMovementPathClear(desiredDirection, shortLookAhead, allowTargetEquipment))
        {
            return desiredDirection;
        }

        return Vector3.zero;
    }

    private void ApplyRoamMovement(Vector3 direction)
    {
        if (body == null || direction.sqrMagnitude < 0.001f)
        {
            return;
        }

        direction = Vector3.ProjectOnPlane(direction, Vector3.up).normalized;
        body.WakeUp();
        Vector3 planarVelocity = Vector3.ProjectOnPlane(body.linearVelocity, Vector3.up);
        Vector3 desiredVelocity = direction * roamSpeed;
        planarVelocity = Vector3.MoveTowards(
            planarVelocity, desiredVelocity,
            Mathf.Max(moveForce * 0.55f, 8f) * Time.fixedDeltaTime);
        body.linearVelocity = planarVelocity + Vector3.Project(body.linearVelocity, Vector3.up);
        ClampToRoomBounds();

        Quaternion lookRotation = Quaternion.LookRotation(direction, Vector3.up);
        transform.rotation = Quaternion.Slerp(
            transform.rotation, lookRotation, 8f * Time.fixedDeltaTime);
        SetAnimatedMovement(true, Mathf.Clamp01(roamSpeed / Mathf.Max(0.01f, maxSpeed)));
    }

    private void SelectRoamDestination()
    {
        roamState = RoamState.Walking;
        stalledRoamTime = 0f;
        pendingTreadmillStation = null;
        roamTargetStation = null;
        hasRoamTargetArrivalRotation = false;
        ClearRoamRoute();
        roamDirection = Vector3.zero;
        roamDirectionHoldUntil = 0f;
        roamSpeed = Random.Range(roamSpeedMin, roamSpeedMax);

        // Ronnie is a police/intervention NPC, not a gym customer. Keep his
        // neutral patrol on clear room points so a machine interest (most
        // visibly the Smith machine) can never become his startup destination.
        if (isPolice)
        {
            if (TryFindPoliceRoomDestination(out Vector3 policeDestination))
            {
                roamTarget = policeDestination;
                hasRoamTarget = true;
                roamTargetPurposeful = false;
                roamTargetInterestLabel = "police room patrol";
                lastRoamTarget = roamTarget;
                hasLastRoamTarget = true;
                BuildRoamRouteToTarget(false);
                return;
            }

            BeginRoamIdle();
            return;
        }

        if (Time.time >= nextTreadmillDecisionTime)
        {
            nextTreadmillDecisionTime = Time.time + Random.Range(10f, 19f);
            if (Random.value < 0.34f)
            {
                GymExerciseStation treadmill =
                    GymExerciseStation.FindClosestTreadmill(transform.position, 28f);
                if (treadmill != null)
                {
                    pendingTreadmillStation = treadmill;
                    roamTargetStation = treadmill;
                    roamTarget = TryFindTreadmillApproachPoint(
                            treadmill, out Vector3 treadmillApproachPoint)
                        ? treadmillApproachPoint
                        : treadmill.EnemyPosition;
                    hasRoamTarget = true;
                    roamTargetPurposeful = true;
                    roamTargetInterestLabel = treadmill.DisplayName;
                    roamTargetArrivalRotation = treadmill.EnemyRotation;
                    hasRoamTargetArrivalRotation = true;
                    BuildRoamRouteToTarget(ShouldAllowTargetEquipment());
                    return;
                }
            }
        }

        if (Random.value < roamRandomDestinationChance &&
            TryFindRareRandomDestination(out Vector3 randomDestination))
        {
            roamTarget = randomDestination;
            hasRoamTarget = true;
            roamTargetPurposeful = false;
            roamTargetInterestLabel = "rare random room destination";
            lastRoamTarget = roamTarget;
            hasLastRoamTarget = true;
            BuildRoamRouteToTarget(false);
            return;
        }

        hasRoamTarget = TryFindRoamPoint(out roamTarget);
        if (hasRoamTarget)
        {
            lastRoamTarget = roamTarget;
            hasLastRoamTarget = true;
            BuildRoamRouteToTarget(ShouldAllowTargetEquipment());
        }
        else if (identity == BodybuilderIdentity.JayCutler &&
                 TryFindJayEmergencyDestination(
                     out Vector3 jayDestination, out string jayLabel))
        {
            // Jay must never get stuck in the long no-waypoint idle used only
            // when a room is genuinely saturated. Give him one more clear,
            // purposeful destination search before allowing that fallback.
            roamTarget = jayDestination;
            hasRoamTarget = true;
            roamTargetPurposeful = !string.IsNullOrEmpty(jayLabel);
            roamTargetInterestLabel = jayLabel;
            lastRoamTarget = roamTarget;
            hasLastRoamTarget = true;
            BuildRoamRouteToTarget(ShouldAllowTargetEquipment());
        }
        else
        {
            // Only enter a long idle if the room currently has no valid
            // waypoint at all. Ordinary route changes always stay walking.
            BeginRoamIdle();
        }
    }

    private bool TryFindPoliceRoomDestination(out Vector3 point)
    {
        // Prefer the existing long-range open-floor sampler. It explicitly
        // rejects equipment, walls and the previous endpoint.
        if (TryFindRareRandomDestination(out point))
        {
            return true;
        }

        if (!TryGetRoomBounds(out Bounds floorBounds))
        {
            point = transform.position;
            return false;
        }

        float margin = GetBodyRadius() + 0.24f;
        for (int attempt = 0; attempt < 96; attempt++)
        {
            point = new Vector3(
                Random.Range(floorBounds.min.x + margin, floorBounds.max.x - margin),
                floorBounds.max.y,
                Random.Range(floorBounds.min.z + margin, floorBounds.max.z - margin));
            if (Vector3.ProjectOnPlane(
                    point - transform.position, Vector3.up).sqrMagnitude < 16f ||
                (hasLastRoamTarget && Vector3.ProjectOnPlane(
                    point - lastRoamTarget, Vector3.up).sqrMagnitude < 16f) ||
                !IsRoamPointClear(point))
            {
                continue;
            }

            float edgeClearance = Mathf.Min(
                point.x - floorBounds.min.x,
                floorBounds.max.x - point.x,
                point.z - floorBounds.min.z,
                floorBounds.max.z - point.z);
            if (edgeClearance < 1.4f)
            {
                continue;
            }

            return true;
        }

        point = transform.position;
        return false;
    }

    private void ClearRoamRoute()
    {
        roamRouteWaypoints.Clear();
        roamRouteIndex = 0;
    }

    private bool ShouldAllowTargetEquipment()
    {
        if (roamTargetStation == null)
        {
            return false;
        }

        // Treadmill roaming targets are normally an open approach point. Only
        // the authored belt center is allowed to overlap the treadmill, and
        // that overlap is handled by TryBeginTreadmillVisit after arrival.
        if (roamTargetStation.IsTreadmill)
        {
            float distanceToBelt = Vector3.ProjectOnPlane(
                roamTarget - roamTargetStation.EnemyPosition, Vector3.up).magnitude;
            return distanceToBelt <= 0.82f;
        }

        return true;
    }

    private Vector3 GetRoamSteeringTarget()
    {
        return roamRouteIndex < roamRouteWaypoints.Count
            ? roamRouteWaypoints[roamRouteIndex]
            : roamTarget;
    }

    private void AdvanceRoamRoute()
    {
        while (roamRouteIndex < roamRouteWaypoints.Count)
        {
            Vector3 waypoint = roamRouteWaypoints[roamRouteIndex];
            float distance = Vector3.ProjectOnPlane(
                waypoint - transform.position, Vector3.up).magnitude;
            float arrivalDistance = roamRouteIndex == roamRouteWaypoints.Count - 1
                ? 0.52f : 0.72f;
            if (distance > arrivalDistance)
            {
                break;
            }

            roamRouteIndex++;
        }
    }

    private bool BuildRoamRouteToTarget(bool allowTargetEquipment)
    {
        ClearRoamRoute();
        if (!hasRoamTarget || !TryGetRoomBounds(out Bounds floorBounds))
        {
            return false;
        }

        Vector3 start = body != null ? body.position : transform.position;
        Vector3 goal = roamTarget;
        goal.y = floorBounds.max.y;
        Vector3 direct = Vector3.ProjectOnPlane(goal - start, Vector3.up);
        if (direct.sqrMagnitude < 1.2f * 1.2f ||
            (!allowTargetEquipment && IsNavigationSegmentClear(start, goal, false)))
        {
            return true;
        }

        const float cellSize = 1.2f;
        float margin = GetBodyRadius() + 0.28f;
        float minX = floorBounds.min.x + margin;
        float maxX = floorBounds.max.x - margin;
        float minZ = floorBounds.min.z + margin;
        float maxZ = floorBounds.max.z - margin;
        int width = Mathf.Clamp(Mathf.FloorToInt((maxX - minX) / cellSize) + 1, 4, 48);
        int height = Mathf.Clamp(Mathf.FloorToInt((maxZ - minZ) / cellSize) + 1, 4, 48);
        int nodeCount = width * height;
        int goalIndex = nodeCount;

        bool[] navigable = new bool[nodeCount];
        Vector3[] points = new Vector3[nodeCount];
        for (int z = 0; z < height; z++)
        {
            for (int x = 0; x < width; x++)
            {
                int index = z * width + x;
                points[index] = new Vector3(
                    Mathf.Min(minX + x * cellSize, maxX),
                    floorBounds.max.y,
                    Mathf.Min(minZ + z * cellSize, maxZ));
                navigable[index] = IsNavigationPointClear(points[index]);
            }
        }

        int startIndex = FindClosestNavigableNode(points, navigable, start);
        if (startIndex < 0)
        {
            return false;
        }

        float[] gScores = new float[nodeCount + 1];
        float[] fScores = new float[nodeCount + 1];
        int[] cameFrom = new int[nodeCount + 1];
        bool[] closed = new bool[nodeCount + 1];
        List<int> open = new List<int>();
        for (int i = 0; i < gScores.Length; i++)
        {
            gScores[i] = float.PositiveInfinity;
            fScores[i] = float.PositiveInfinity;
            cameFrom[i] = -1;
        }

        gScores[startIndex] = 0f;
        fScores[startIndex] = Vector3.ProjectOnPlane(
            goal - points[startIndex], Vector3.up).magnitude;
        open.Add(startIndex);
        bool foundGoal = false;

        while (open.Count > 0)
        {
            int current = open[0];
            float bestOpenScore = fScores[current];
            for (int i = 1; i < open.Count; i++)
            {
                int candidate = open[i];
                if (fScores[candidate] < bestOpenScore)
                {
                    bestOpenScore = fScores[candidate];
                    current = candidate;
                }
            }
            open.Remove(current);
            if (current == goalIndex)
            {
                foundGoal = true;
                break;
            }
            if (closed[current])
            {
                continue;
            }
            closed[current] = true;

            Vector3 currentPoint = points[current];
            float goalDistance = Vector3.ProjectOnPlane(
                goal - currentPoint, Vector3.up).magnitude;
            if (goalDistance <= cellSize * 3.4f &&
                IsNavigationSegmentClear(currentPoint, goal, allowTargetEquipment))
            {
                float goalScore = gScores[current] + goalDistance;
                if (goalScore < gScores[goalIndex])
                {
                    cameFrom[goalIndex] = current;
                    gScores[goalIndex] = goalScore;
                    fScores[goalIndex] = goalScore;
                    if (!open.Contains(goalIndex))
                    {
                        open.Add(goalIndex);
                    }
                }
            }

            for (int directionIndex = 0;
                 directionIndex < NavigationNeighborX.Length;
                 directionIndex++)
            {
                int currentX = current % width;
                int currentZ = current / width;
                int nextX = currentX + NavigationNeighborX[directionIndex];
                int nextZ = currentZ + NavigationNeighborZ[directionIndex];
                if (nextX < 0 || nextX >= width || nextZ < 0 || nextZ >= height)
                {
                    continue;
                }

                int next = nextZ * width + nextX;
                if (!navigable[next] || closed[next] ||
                    !IsNavigationSegmentClear(currentPoint, points[next], false))
                {
                    continue;
                }

                float stepDistance = Vector3.ProjectOnPlane(
                    points[next] - currentPoint, Vector3.up).magnitude;
                float tentativeScore = gScores[current] + stepDistance;
                if (tentativeScore >= gScores[next])
                {
                    continue;
                }

                cameFrom[next] = current;
                gScores[next] = tentativeScore;
                fScores[next] = tentativeScore + Vector3.ProjectOnPlane(
                    goal - points[next], Vector3.up).magnitude;
                if (!open.Contains(next))
                {
                    open.Add(next);
                }
            }
        }

        if (!foundGoal || cameFrom[goalIndex] < 0)
        {
            return false;
        }

        List<Vector3> rawRoute = new List<Vector3>();
        int routeNode = cameFrom[goalIndex];
        while (routeNode >= 0 && routeNode != startIndex)
        {
            rawRoute.Add(points[routeNode]);
            routeNode = cameFrom[routeNode];
        }
        rawRoute.Reverse();
        rawRoute.Add(goal);

        // Collapse visible, collinear grid steps. The route still preserves
        // the A* detour around machines, but the actor does not make a small
        // left/right correction on every cell.
        Vector3 anchor = start;
        int rawIndex = 0;
        while (rawIndex < rawRoute.Count)
        {
            int furthestVisible = rawIndex;
            for (int candidateIndex = rawRoute.Count - 1;
                 candidateIndex > rawIndex;
                 candidateIndex--)
            {
                bool isFinalTarget = candidateIndex == rawRoute.Count - 1;
                if (IsNavigationSegmentClear(
                        anchor, rawRoute[candidateIndex],
                        isFinalTarget && allowTargetEquipment))
                {
                    furthestVisible = candidateIndex;
                    break;
                }
            }

            roamRouteWaypoints.Add(rawRoute[furthestVisible]);
            anchor = rawRoute[furthestVisible];
            rawIndex = furthestVisible + 1;
        }

        return roamRouteWaypoints.Count > 0;
    }

    private bool TryFindTreadmillApproachPoint(
        GymExerciseStation station, out Vector3 point)
    {
        point = station != null ? station.EnemyPosition : transform.position;
        if (station == null || !TryGetRoomBounds(out Bounds floorBounds))
        {
            return false;
        }

        Vector3 center = station.EnemyPosition;
        center.y = floorBounds.max.y;
        Vector3 preferredDirection = Vector3.ProjectOnPlane(
            station.EnemyRotation * Vector3.back, Vector3.up);
        if (preferredDirection.sqrMagnitude < 0.01f)
        {
            preferredDirection = Vector3.back;
        }
        preferredDirection.Normalize();

        float margin = GetBodyRadius() + 0.28f;
        float bestScore = float.NegativeInfinity;
        bool found = false;
        // Search a ring outside the treadmill rather than asking the route
        // planner to finish inside its belt collider. The preferred side is
        // the rear/aisle side, but the full ring lets differently oriented
        // imported treadmill assets choose whichever side is actually open.
        float[] radii = { 2.05f, 2.45f, 2.9f, 3.35f, 4.1f, 5.0f, 6.0f };
        for (int radiusIndex = 0; radiusIndex < radii.Length; radiusIndex++)
        {
            float radius = radii[radiusIndex];
            for (int sample = 0; sample < 24; sample++)
            {
                float angle = sample * 15f;
                Vector3 direction = Quaternion.Euler(0f, angle, 0f) *
                    preferredDirection;
                Vector3 candidate = center + direction * radius;
                candidate.x = Mathf.Clamp(
                    candidate.x, floorBounds.min.x + margin, floorBounds.max.x - margin);
                candidate.z = Mathf.Clamp(
                    candidate.z, floorBounds.min.z + margin, floorBounds.max.z - margin);

                float edgeClearance = Mathf.Min(
                    candidate.x - floorBounds.min.x,
                    floorBounds.max.x - candidate.x,
                    candidate.z - floorBounds.min.z,
                    floorBounds.max.z - candidate.z);
                if (edgeClearance < 1.15f || !IsRoamPointClear(candidate))
                {
                    continue;
                }

                float preferredAlignment = Vector3.Dot(direction, preferredDirection);
                float score = preferredAlignment * 2.4f +
                    Mathf.Min(edgeClearance, 5f) * 0.65f - radius * 0.12f;
                if (score <= bestScore)
                {
                    continue;
                }

                bestScore = score;
                point = candidate;
                found = true;
            }
        }

        return found;
    }

    private int FindClosestNavigableNode(
        Vector3[] points, bool[] navigable, Vector3 position)
    {
        int closest = -1;
        float closestDistance = float.PositiveInfinity;
        for (int i = 0; i < points.Length; i++)
        {
            if (!navigable[i])
            {
                continue;
            }

            float distance = Vector3.ProjectOnPlane(
                points[i] - position, Vector3.up).sqrMagnitude;
            if (distance < closestDistance)
            {
                closestDistance = distance;
                closest = i;
            }
        }

        return closest;
    }

    private bool IsNavigationPointClear(Vector3 point)
    {
        Vector3 lower = point + Vector3.up * 0.55f;
        Vector3 upper = point + Vector3.up * 1.85f;
        int count = Physics.OverlapCapsuleNonAlloc(
            lower, upper, GetBodyRadius(), roamOverlapHits,
            ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            Collider hit = roamOverlapHits[i];
            if (hit == null || hit.transform == transform || hit.transform.IsChildOf(transform))
            {
                continue;
            }
            if (hit.GetComponentInParent<EnemyFighter>() != null ||
                hit.GetComponentInParent<PlayerMovement>() != null ||
                HasRoomFloorInHierarchy(hit.transform) ||
                IsWalkableFloorSurface(hit))
            {
                continue;
            }
            return false;
        }

        return true;
    }

    private bool IsNavigationSegmentClear(
        Vector3 from, Vector3 to, bool allowTargetEquipment)
    {
        Vector3 delta = Vector3.ProjectOnPlane(to - from, Vector3.up);
        float distance = delta.magnitude;
        if (distance < 0.05f)
        {
            return true;
        }

        Vector3 direction = delta / distance;
        Vector3 lower = from + Vector3.up * 0.55f;
        Vector3 upper = from + Vector3.up * 1.85f;
        int count = Physics.CapsuleCastNonAlloc(
            lower, upper, GetBodyRadius(), direction, movementHits,
            distance + 0.06f, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            Collider hit = movementHits[i].collider;
            if (hit == null || hit.transform == transform || hit.transform.IsChildOf(transform))
            {
                continue;
            }
            if (hit.GetComponentInParent<EnemyFighter>() != null ||
                hit.GetComponentInParent<PlayerMovement>() != null)
            {
                continue;
            }
            if (allowTargetEquipment && pendingTreadmillStation != null &&
                pendingTreadmillStation.ContainsEquipmentCollider(hit))
            {
                continue;
            }
            if (HasRoomFloorInHierarchy(hit.transform) ||
                IsWalkableFloorSurface(hit))
            {
                continue;
            }
            return false;
        }

        return true;
    }

    private bool TryFindJayEmergencyDestination(out Vector3 point, out string label)
    {
        point = transform.position;
        label = null;
        if (!TryGetRoomBounds(out Bounds floorBounds))
        {
            return false;
        }

        float margin = GetBodyRadius() + 0.24f;
        CollectRoamInterests();
        RoamInterest jayInterest;
        if (TryFindPurposefulRoamPoint(
                floorBounds, margin, 2.4f, out point, out label, out jayInterest))
        {
            ApplySelectedRoamInterest(jayInterest);
            return true;
        }

        for (int attempt = 0; attempt < 80; attempt++)
        {
            point = new Vector3(
                Random.Range(floorBounds.min.x + margin, floorBounds.max.x - margin),
                floorBounds.max.y,
                Random.Range(floorBounds.min.z + margin, floorBounds.max.z - margin));
            if (Vector3.ProjectOnPlane(point - transform.position, Vector3.up).sqrMagnitude < 5.5f ||
                !IsRoamPointClear(point))
            {
                continue;
            }

            label = "Jay clear route fallback";
            return true;
        }

        return false;
    }

    private bool TryFindRareRandomDestination(out Vector3 point)
    {
        point = transform.position;
        if (!TryGetRoomBounds(out Bounds floorBounds))
        {
            return false;
        }

        float margin = GetBodyRadius() + 0.24f;
        float roomScale = Mathf.Min(floorBounds.size.x, floorBounds.size.z);
        float minimumDistance = Mathf.Clamp(roomScale * 0.2f, 6f, 9f);
        for (int attempt = 0; attempt < 96; attempt++)
        {
            point = new Vector3(
                Random.Range(floorBounds.min.x + margin, floorBounds.max.x - margin),
                floorBounds.max.y,
                Random.Range(floorBounds.min.z + margin, floorBounds.max.z - margin));
            float distance = Vector3.ProjectOnPlane(
                point - transform.position, Vector3.up).magnitude;
            float edgeClearance = Mathf.Min(
                point.x - floorBounds.min.x,
                floorBounds.max.x - point.x,
                point.z - floorBounds.min.z,
                floorBounds.max.z - point.z);
            if (distance < minimumDistance || edgeClearance < 2.1f ||
                (hasLastRoamTarget &&
                 Vector3.ProjectOnPlane(point - lastRoamTarget, Vector3.up).magnitude < 5.5f) ||
                !IsRoamPointClear(point))
            {
                continue;
            }

            return true;
        }

        return false;
    }

    private bool TryFindRoamPoint(out Vector3 point)
    {
        roamTargetPurposeful = false;
        roamTargetInterestLabel = null;
        roamTargetStation = null;
        hasRoamTargetArrivalRotation = false;

        if (!TryGetRoomBounds(out Bounds floorBounds))
        {
            point = transform.position;
            return true;
        }

        float margin = GetBodyRadius() + 0.24f;
        float roomScale = Mathf.Min(floorBounds.size.x, floorBounds.size.z);
        float minimumDistance = Mathf.Clamp(roomScale * 0.18f, 5.5f, 8.5f);

        // A meaningful target is an area around a real piece of equipment,
        // reception/lockers, or another person. This keeps the long route
        // commitment from sending a character toward an arbitrary empty
        // corner just because that point happened to score well on the floor.
        CollectRoamInterests();
        RoamInterest purposefulInterest;
        string purposefulLabel;
        if (TryFindPurposefulRoamPoint(
                floorBounds, margin, minimumDistance,
                out point, out purposefulLabel, out purposefulInterest) ||
            TryFindPurposefulRoamPoint(
                floorBounds, margin, Mathf.Max(3.8f, minimumDistance * 0.58f),
                out point, out purposefulLabel, out purposefulInterest))
        {
            roamTargetPurposeful = true;
            roamTargetInterestLabel = purposefulLabel;
            ApplySelectedRoamInterest(purposefulInterest);
            return true;
        }

        // If every useful object is temporarily surrounded by other bodies,
        // preserve motion with a clear open-floor fallback. It is only used
        // when no purposeful endpoint can currently be reached; it still
        // avoids walls, equipment, characters, and the previous endpoint.
        float minimumPreviousTargetDistance = Mathf.Max(5.5f, minimumDistance * 0.75f);
        float bestScore = float.NegativeInfinity;
        Vector3 bestPoint = transform.position;
        bool foundPoint = false;

        for (int attempt = 0; attempt < 56; attempt++)
        {
            point = new Vector3(
                Random.Range(floorBounds.min.x + margin, floorBounds.max.x - margin),
                floorBounds.max.y,
                Random.Range(floorBounds.min.z + margin, floorBounds.max.z - margin));

            float distance = Vector3.ProjectOnPlane(point - transform.position, Vector3.up).magnitude;
            float edgeClearance = Mathf.Min(
                point.x - floorBounds.min.x,
                floorBounds.max.x - point.x,
                point.z - floorBounds.min.z,
                floorBounds.max.z - point.z);
            if (distance < minimumDistance || edgeClearance < 1.8f)
            {
                continue;
            }

            float previousTargetDistance = hasLastRoamTarget
                ? Vector3.ProjectOnPlane(point - lastRoamTarget, Vector3.up).magnitude
                : float.PositiveInfinity;
            if (hasLastRoamTarget && previousTargetDistance < minimumPreviousTargetDistance)
            {
                continue;
            }

            if (!IsRoamPointClear(point))
            {
                continue;
            }

            // Prefer a waypoint in a different part of the room and away
            // from the previous endpoint, with a small random component so
            // the patrol does not select the same edge every time.
            float score = distance * 0.34f +
                (hasLastRoamTarget ? previousTargetDistance * 0.22f : 0f) +
                Mathf.Min(edgeClearance, 5f) * 0.9f +
                Random.Range(0f, 2.5f);
            if (score > bestScore)
            {
                bestScore = score;
                bestPoint = point;
                foundPoint = true;
            }
        }

        if (foundPoint)
        {
            point = bestPoint;
            roamTargetInterestLabel = "clear room fallback";
            return true;
        }

        // If machines temporarily occupy most valid room-wide samples, relax
        // only the distance constraint. Keep endpoint and body clearance
        // checks intact rather than falling back to an obstructed point.
        for (int attempt = 0; attempt < 32; attempt++)
        {
            point = new Vector3(
                Random.Range(floorBounds.min.x + margin, floorBounds.max.x - margin),
                floorBounds.max.y,
                Random.Range(floorBounds.min.z + margin, floorBounds.max.z - margin));
            if (Vector3.ProjectOnPlane(point - transform.position, Vector3.up).sqrMagnitude < 16f ||
                !IsRoamPointClear(point))
            {
                continue;
            }

            float edgeClearance = Mathf.Min(
                point.x - floorBounds.min.x,
                floorBounds.max.x - point.x,
                point.z - floorBounds.min.z,
                floorBounds.max.z - point.z);
            if (edgeClearance < 1.2f)
            {
                continue;
            }

            if (hasLastRoamTarget &&
                Vector3.ProjectOnPlane(point - lastRoamTarget, Vector3.up).magnitude < 4f)
            {
                continue;
            }

            roamTargetInterestLabel = "clear room fallback";
            return true;
        }

        point = transform.position;
        point.y = floorBounds.max.y;
        return false;
    }

    private void CollectRoamInterests()
    {
        roamInterests.Clear();
        roamInterestRoots.Clear();
        collectedMachineInterestCount = 0;
        collectedPersonnelInterestCount = 0;
        collectedReceptionInterest = false;
        collectedPlayerInterest = false;

        // Stations are authoritative for the exercise assets. Their helper
        // object is placed at the authored machine center, even when the
        // imported model's child names are inconsistent.
        GymExerciseStation[] stations =
            FindObjectsByType<GymExerciseStation>(FindObjectsSortMode.None);
        for (int i = 0; i < stations.Length; i++)
        {
            GymExerciseStation station = stations[i];
            if (station == null || station.transform == null)
            {
                continue;
            }

            Transform stationParent = station.transform.parent;
            if (stationParent != null)
            {
                // Mark the imported machine root so the renderer scan below
                // cannot add a second, overlapping interest for the same
                // station. Separate stations under one section still retain
                // their own authored center points.
                roamInterestRoots.Add(stationParent);
            }

            if (station.IsSquat)
            {
                // Squat cages and the Smith machine are scheduled workout
                // destinations owned by GymVisitorDirector. They must never
                // be selected as ordinary free-roam interests, otherwise an
                // enemy can pace beside a rack without ever starting a squat.
                continue;
            }

            float footprint = station.IsTreadmill ? 2.5f
                : station.IsCardio ? 2.2f : 2.9f;
            Bounds stationBounds = new Bounds(
                station.transform.position,
                new Vector3(footprint, 2.0f, footprint));
            AddRoamInterest(
                station.transform, stationBounds, station.transform.position,
                false, 1.35f, station.DisplayName,
                station, true, station.PlayerPosition,
                station.EnemyRotation, true);
            collectedMachineInterestCount++;
        }

        // Cover meaningful imported/static objects that are not exercise
        // stations, such as the generated reception desk and lockers.
        Renderer[] renderers = FindObjectsByType<Renderer>(FindObjectsSortMode.None);
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null || !renderer.enabled ||
                renderer is ParticleSystemRenderer ||
                renderer.GetComponentInParent<PlayerMovement>() != null ||
                renderer.GetComponentInParent<EnemyFighter>() != null)
            {
                continue;
            }

            Transform root = FindPurposefulRoamRoot(renderer.transform);
            if (root == null || roamInterestRoots.Contains(root))
            {
                continue;
            }

            if (!TryGetRoamInterestBounds(root, out Bounds bounds) ||
                bounds.size.x > 9.5f || bounds.size.z > 9.5f)
            {
                continue;
            }

            AddRoamInterest(
                root, bounds, bounds.center, false, 1.15f, root.name);
        }

        // Personnel are moving points of interest, but they are only used as
        // neutral roaming destinations. Combat targeting remains entirely in
        // the aggressive/police state machine above.
        for (int i = 0; i < Fighters.Count; i++)
        {
            EnemyFighter other = Fighters[i];
            if (other == null || other == this || other.isDead ||
                other.transform == null)
            {
                continue;
            }

            Vector3 position = other.transform.position;
            Bounds personnelBounds = new Bounds(
                position + Vector3.up * 1f,
                new Vector3(1.1f, 2.2f, 1.1f));
            AddRoamInterest(
                other.transform, personnelBounds, position,
                true, 1.5f, other.identity.ToString());
            collectedPersonnelInterestCount++;
            if (other.isPassive || other.identity == BodybuilderIdentity.Manwithsuit1)
            {
                collectedReceptionInterest = true;
            }
        }

        if (playerTarget == null)
        {
            playerTarget = FindAnyObjectByType<PlayerMovement>();
        }
        if (playerTarget != null && !playerTarget.IsDead)
        {
            Vector3 position = playerTarget.transform.position;
            Bounds playerBounds = new Bounds(
                position + Vector3.up * 1f,
                new Vector3(1.2f, 2.0f, 1.2f));
            AddRoamInterest(
                playerTarget.transform, playerBounds, position,
                true, 1.6f, "Player");
            collectedPlayerInterest = true;
        }
    }

    private bool TryFindPurposefulRoamPoint(
        Bounds floorBounds, float margin, float minimumDistance,
        out Vector3 point, out string label, out RoamInterest selectedInterest)
    {
        point = transform.position;
        label = null;
        selectedInterest = null;
        if (roamInterests.Count == 0)
        {
            return false;
        }

        float minimumPreviousTargetDistance = Mathf.Max(4.5f, minimumDistance * 0.7f);
        float bestScore = float.NegativeInfinity;
        bool foundPoint = false;
        string bestLabel = null;
        RoamInterest bestInterest = null;

        for (int i = 0; i < roamInterests.Count; i++)
        {
            RoamInterest interest = roamInterests[i];
            if (interest == null || interest.root == null)
            {
                continue;
            }
            if (interest.station != null && interest.station.IsTreadmill &&
                !interest.station.IsAvailableForEnemy(this))
            {
                continue;
            }

            int samples = interest.useInteractionPoint ? 1 : (interest.personnel ? 6 : 8);
            for (int sample = 0; sample < samples; sample++)
            {
                Vector2 randomDirection = Random.insideUnitCircle;
                if (randomDirection.sqrMagnitude < 0.08f)
                {
                    randomDirection = Random.insideUnitCircle.normalized;
                }
                if (randomDirection.sqrMagnitude < 0.01f)
                {
                    randomDirection = Vector2.right;
                }
                randomDirection.Normalize();

                Vector3 candidate;
                if (interest.station != null && interest.station.IsTreadmill)
                {
                    if (!TryFindTreadmillApproachPoint(
                            interest.station, out candidate))
                    {
                        continue;
                    }
                }
                else if (interest.useInteractionPoint)
                {
                    // Stations expose an authored interaction point. Use it
                    // directly so walking to a machine has a coherent
                    // approach pose and a treadmill visit can start there.
                    candidate = interest.interactionPoint;
                }
                else
                {
                    float footprint = interest.personnel
                        ? 1.15f
                        : Mathf.Clamp(
                            Mathf.Max(interest.bounds.extents.x, interest.bounds.extents.z),
                            0.9f, 3.2f);
                    float ringDistance = footprint + GetBodyRadius() +
                        Random.Range(0.65f, interest.personnel ? 1.35f : 2.15f);
                    candidate = interest.position +
                        new Vector3(randomDirection.x, 0f, randomDirection.y) * ringDistance;
                }
                candidate.y = floorBounds.max.y;
                candidate.x = Mathf.Clamp(
                    candidate.x, floorBounds.min.x + margin, floorBounds.max.x - margin);
                candidate.z = Mathf.Clamp(
                    candidate.z, floorBounds.min.z + margin, floorBounds.max.z - margin);

                float distance = Vector3.ProjectOnPlane(
                    candidate - transform.position, Vector3.up).magnitude;
                bool endpointClear = interest.station != null
                    ? IsNavigationPointClearForStation(candidate, interest.station)
                    : IsRoamPointClear(candidate);
                if (distance < minimumDistance || !endpointClear)
                {
                    continue;
                }

                float previousDistance = hasLastRoamTarget
                    ? Vector3.ProjectOnPlane(candidate - lastRoamTarget, Vector3.up).magnitude
                    : float.PositiveInfinity;
                if (hasLastRoamTarget && previousDistance < minimumPreviousTargetDistance)
                {
                    continue;
                }

                float edgeClearance = Mathf.Min(
                    candidate.x - floorBounds.min.x,
                    floorBounds.max.x - candidate.x,
                    candidate.z - floorBounds.min.z,
                    floorBounds.max.z - candidate.z);
                float minimumEdgeClearance = interest.useInteractionPoint
                    ? 0.9f
                    : (interest.personnel ? 1.25f : 1.7f);
                if (edgeClearance < minimumEdgeClearance)
                {
                    continue;
                }

                float score = distance * 0.32f +
                    (hasLastRoamTarget ? previousDistance * 0.18f : 0f) +
                    Mathf.Min(edgeClearance, 5f) * 1.1f +
                    interest.weight * 2f + Random.Range(0f, 2.25f);
                if (score <= bestScore)
                {
                    continue;
                }

                bestScore = score;
                point = candidate;
                bestLabel = interest.label;
                bestInterest = interest;
                foundPoint = true;
            }
        }

        if (!foundPoint)
        {
            return false;
        }

        label = bestLabel;
        selectedInterest = bestInterest;
        return true;
    }

    private bool IsNavigationPointClearForStation(
        Vector3 point, GymExerciseStation station)
    {
        Vector3 lower = point + Vector3.up * 0.55f;
        Vector3 upper = point + Vector3.up * 1.85f;
        int count = Physics.OverlapCapsuleNonAlloc(
            lower, upper, GetBodyRadius(), roamOverlapHits,
            ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            Collider hit = roamOverlapHits[i];
            if (hit == null || hit.transform == transform || hit.transform.IsChildOf(transform))
            {
                continue;
            }
            if (hit.GetComponentInParent<EnemyFighter>() != null ||
                hit.GetComponentInParent<PlayerMovement>() != null ||
                HasRoomFloorInHierarchy(hit.transform) ||
                IsWalkableFloorSurface(hit))
            {
                continue;
            }
            if (station != null && station.ContainsEquipmentCollider(hit))
            {
                continue;
            }
            return false;
        }

        return true;
    }

    private void AddRoamInterest(
        Transform root, Bounds bounds, Vector3 position,
        bool personnel, float weight, string label,
        GymExerciseStation station = null, bool useInteractionPoint = false,
        Vector3 interactionPoint = default, Quaternion arrivalRotation = default,
        bool hasArrivalRotation = false)
    {
        if (root == null || roamInterestRoots.Contains(root))
        {
            return;
        }

        roamInterestRoots.Add(root);
        bounds.center = new Vector3(bounds.center.x, position.y, bounds.center.z);
        roamInterests.Add(new RoamInterest
        {
            root = root,
            bounds = bounds,
            position = position,
            interactionPoint = interactionPoint,
            arrivalRotation = arrivalRotation,
            station = station,
            useInteractionPoint = useInteractionPoint,
            hasArrivalRotation = hasArrivalRotation,
            personnel = personnel,
            weight = weight,
            label = string.IsNullOrEmpty(label) ? root.name : label
        });
    }

    private void ApplySelectedRoamInterest(RoamInterest interest)
    {
        roamTargetStation = interest != null ? interest.station : null;
        if (roamTargetStation != null && roamTargetStation.IsTreadmill &&
            roamTargetStation.IsAvailableForEnemy(this))
        {
            pendingTreadmillStation = roamTargetStation;
        }

        hasRoamTargetArrivalRotation = interest != null &&
            interest.hasArrivalRotation;
        if (hasRoamTargetArrivalRotation)
        {
            roamTargetArrivalRotation = interest.arrivalRotation;
        }
        else if (interest != null)
        {
            Vector3 lookDirection = Vector3.ProjectOnPlane(
                interest.position - roamTarget, Vector3.up);
            if (lookDirection.sqrMagnitude > 0.01f)
            {
                roamTargetArrivalRotation =
                    Quaternion.LookRotation(lookDirection.normalized, Vector3.up);
                hasRoamTargetArrivalRotation = true;
            }
        }
    }

    private static Transform FindPurposefulRoamRoot(Transform target)
    {
        for (Transform current = target; current != null; current = current.parent)
        {
            string lowerName = current.name.ToLowerInvariant();
            if (ContainsAny(lowerName, NonPurposefulRoamKeywords))
            {
                continue;
            }
            if (ContainsAny(lowerName, PurposefulRoamKeywords))
            {
                return current;
            }
        }

        return null;
    }

    private static bool ContainsAny(string value, string[] fragments)
    {
        for (int i = 0; i < fragments.Length; i++)
        {
            if (value.Contains(fragments[i]))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryGetRoamInterestBounds(Transform root, out Bounds bounds)
    {
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        bool found = false;
        bounds = default;
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null || !renderer.enabled ||
                renderer is ParticleSystemRenderer)
            {
                continue;
            }

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

        return found && bounds.size.x > 0.1f && bounds.size.z > 0.1f;
    }

    private bool IsRoamPointClear(Vector3 point)
    {
        Vector3 lower = point + Vector3.up * 0.55f;
        Vector3 upper = point + Vector3.up * 1.85f;
        int count = Physics.OverlapCapsuleNonAlloc(
            lower, upper, GetBodyRadius(), roamOverlapHits,
            ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            Collider hit = roamOverlapHits[i];
            if (hit == null || hit.transform == transform || hit.transform.IsChildOf(transform))
            {
                continue;
            }
            if (hit.GetComponentInParent<EnemyFighter>() != null)
            {
                return false;
            }
            if (hit.GetComponentInParent<PlayerMovement>() != null)
            {
                return false;
            }
            if (HasRoomFloorInHierarchy(hit.transform))
            {
                continue;
            }
            if (IsWalkableFloorSurface(hit))
            {
                continue;
            }
            return false;
        }
        return true;
    }

    private Vector3 FindClearMovementDirection(
        Vector3 desiredDirection, float targetDistance, bool avoidCharacters,
        bool allowTargetEquipment)
    {
        desiredDirection = Vector3.ProjectOnPlane(desiredDirection, Vector3.up);
        if (desiredDirection.sqrMagnitude < 0.0001f)
        {
            return Vector3.zero;
        }
        desiredDirection.Normalize();

        if (avoidCharacters)
        {
            Vector3 separation = GetCharacterSeparation();
            if (separation.sqrMagnitude > 0.0001f)
            {
                // Preserve the separation magnitude. Normalizing it made even
                // a barely-nearby enemy apply a full-strength steering shove,
                // which produced the visible left/right indecision.
                desiredDirection = (desiredDirection + separation * 0.85f).normalized;
            }
        }

        float lookAhead = Mathf.Clamp(Mathf.Max(0.72f, targetDistance), 0.72f, 1.25f);
        Vector3 best = Vector3.zero;
        float bestScore = float.NegativeInfinity;
        for (int i = 0; i < MovementProbeAngles.Length; i++)
        {
            Vector3 candidate = Quaternion.Euler(0f, MovementProbeAngles[i], 0f) * desiredDirection;
            if (!IsInsideRoom(candidate, lookAhead) ||
                !IsMovementPathClear(candidate, lookAhead, allowTargetEquipment))
            {
                continue;
            }

            float alignment = Vector3.Dot(candidate, desiredDirection);
            float score = alignment * 4f - Mathf.Abs(MovementProbeAngles[i]) * 0.002f;
            if (avoidCharacters && roamDirection.sqrMagnitude > 0.001f)
            {
                // When an obstacle requires a detour, prefer the previously
                // selected side so two equally valid probes do not alternate
                // on consecutive physics frames.
                score += Vector3.Dot(candidate, roamDirection.normalized) * 0.9f;
            }
            if (score > bestScore)
            {
                bestScore = score;
                best = candidate;
            }
        }

        return best;
    }

    private Vector3 FindVisitorMovementDirection(
        Vector3 desiredDirection,
        float targetDistance,
        bool allowOutsideRoom,
        GymExerciseStation targetStation)
    {
        desiredDirection = Vector3.ProjectOnPlane(desiredDirection, Vector3.up);
        if (desiredDirection.sqrMagnitude < 0.0001f)
        {
            return Vector3.zero;
        }
        desiredDirection.Normalize();

        Vector3 best = Vector3.zero;
        float bestScore = float.NegativeInfinity;
        for (int i = 0; i < MovementProbeAngles.Length; i++)
        {
            Vector3 candidate = Quaternion.Euler(0f, MovementProbeAngles[i], 0f) * desiredDirection;
            if ((!allowOutsideRoom && !IsInsideRoom(candidate, targetDistance)) ||
                !IsVisitorPathClear(candidate, targetDistance, targetStation))
            {
                continue;
            }

            float alignment = Vector3.Dot(candidate, desiredDirection);
            float score = alignment * 4f - Mathf.Abs(MovementProbeAngles[i]) * 0.002f;
            if (score > bestScore)
            {
                bestScore = score;
                best = candidate;
            }
        }

        return best;
    }

    private bool IsVisitorPathClear(
        Vector3 direction,
        float distance,
        GymExerciseStation targetStation)
    {
        Vector3 origin = body != null ? body.position : transform.position;
        Vector3 lower = origin + Vector3.up * 0.55f;
        Vector3 upper = origin + Vector3.up * 1.85f;
        int count = Physics.CapsuleCastNonAlloc(
            lower, upper, GetBodyRadius(), direction, movementHits,
            distance + 0.08f, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            Collider hit = movementHits[i].collider;
            if (hit == null || hit.transform == transform || hit.transform.IsChildOf(transform))
            {
                continue;
            }
            if (hit.GetComponentInParent<EnemyFighter>() != null ||
                hit.GetComponentInParent<PlayerMovement>() != null ||
                HasRoomFloorInHierarchy(hit.transform) ||
                IsWalkableFloorSurface(hit))
            {
                continue;
            }
            if (hit.GetComponentInParent<GymDoorway>() != null)
            {
                continue;
            }
            if (targetStation != null && targetStation.ContainsEquipmentCollider(hit))
            {
                continue;
            }
            return false;
        }

        return true;
    }

    private Vector3 StabilizeRoamDirection(
        Vector3 desiredDirection, float targetDistance, Vector3 candidate,
        bool allowTargetEquipment)
    {
        desiredDirection = Vector3.ProjectOnPlane(desiredDirection, Vector3.up).normalized;
        float lookAhead = Mathf.Clamp(Mathf.Max(0.72f, targetDistance), 0.72f, 1.25f);
        bool hasHeldDirection = roamDirection.sqrMagnitude > 0.001f;
        bool heldDirectionClear = hasHeldDirection &&
            IsInsideRoom(roamDirection, lookAhead) &&
            IsMovementPathClear(roamDirection, lookAhead, allowTargetEquipment);

        if (heldDirectionClear && Time.time < roamDirectionHoldUntil)
        {
            return roamDirection;
        }

        if (candidate.sqrMagnitude < 0.001f)
        {
            return heldDirectionClear ? roamDirection : Vector3.zero;
        }

        candidate.Normalize();
        bool isObstacleDetour = Vector3.Dot(candidate, desiredDirection) < 0.985f;
        if (isObstacleDetour && heldDirectionClear &&
            Vector3.Dot(roamDirection, desiredDirection) > 0.2f &&
            Vector3.Dot(candidate, roamDirection) < 0.55f)
        {
            // Keep the current side of the obstacle briefly even after the
            // hold expires if the new probe would reverse the route.
            roamDirectionHoldUntil = Time.time + 0.36f;
            return roamDirection;
        }

        roamDirection = candidate;
        roamDirectionHoldUntil = Time.time + (isObstacleDetour ? 0.48f : 0.08f);
        return roamDirection;
    }

    private bool IsMovementPathClear(Vector3 direction, float distance, bool allowTargetEquipment)
    {
        Vector3 origin = body != null ? body.position : transform.position;
        Vector3 lower = origin + Vector3.up * 0.55f;
        Vector3 upper = origin + Vector3.up * 1.85f;
        int count = Physics.CapsuleCastNonAlloc(
            lower, upper, GetBodyRadius(), direction, movementHits,
            distance + 0.08f, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            Collider hit = movementHits[i].collider;
            if (hit == null || hit.transform == transform || hit.transform.IsChildOf(transform))
            {
                continue;
            }
            if (hit.GetComponentInParent<EnemyFighter>() != null ||
                hit.GetComponentInParent<PlayerMovement>() != null)
            {
                continue;
            }
            if (allowTargetEquipment && pendingTreadmillStation != null &&
                pendingTreadmillStation.ContainsEquipmentCollider(hit))
            {
                continue;
            }
            if (HasRoomFloorInHierarchy(hit.transform) ||
                IsWalkableFloorSurface(hit))
            {
                continue;
            }
            return false;
        }
        return true;
    }

    private Vector3 GetCharacterSeparation()
    {
        Vector3 separation = Vector3.zero;
        for (int i = 0; i < Fighters.Count; i++)
        {
            EnemyFighter other = Fighters[i];
            if (other == null || other == this || other.isDead || other.isPassive)
            {
                continue;
            }

            Vector3 offset = Vector3.ProjectOnPlane(transform.position - other.transform.position, Vector3.up);
            float distance = offset.magnitude;
            if (distance > 0.001f && distance < 2.1f)
            {
                separation += offset.normalized * (2.1f - distance);
            }
        }
        return separation;
    }

    private bool IsInsideRoom(Vector3 direction, float distance)
    {
        if (!TryGetRoomBounds(out Bounds floorBounds))
        {
            return true;
        }

        Vector3 predicted = transform.position + direction * distance;
        float margin = GetBodyRadius() + 0.24f;
        return predicted.x >= floorBounds.min.x + margin &&
               predicted.x <= floorBounds.max.x - margin &&
               predicted.z >= floorBounds.min.z + margin &&
               predicted.z <= floorBounds.max.z - margin;
    }

    private void ClampToRoomBounds()
    {
        if (body == null || !TryGetRoomBounds(out Bounds floorBounds))
        {
            return;
        }

        float margin = GetBodyRadius() + 0.24f;
        Vector3 position = body.position;
        position.x = Mathf.Clamp(position.x, floorBounds.min.x + margin, floorBounds.max.x - margin);
        position.z = Mathf.Clamp(position.z, floorBounds.min.z + margin, floorBounds.max.z - margin);
        body.position = position;
    }

    private bool TryGetRoomBounds(out Bounds bounds)
    {
        GameObject floor = GameObject.Find("Rubber Floor");
        Renderer renderer = floor != null ? floor.GetComponent<Renderer>() : null;
        if (renderer == null)
        {
            bounds = default;
            return false;
        }

        bounds = renderer.bounds;
        return bounds.size.x > 2f && bounds.size.z > 2f;
    }

    private static bool HasRoomFloorInHierarchy(Transform target)
    {
        for (Transform current = target; current != null; current = current.parent)
        {
            string lowerName = current.name.ToLowerInvariant();
            if (lowerName.Contains("rubber floor") ||
                lowerName == "plane" || lowerName.StartsWith("plane("))
            {
                return true;
            }
        }
        return false;
    }

    private static bool IsWalkableFloorSurface(Collider collider)
    {
        if (collider == null)
        {
            return false;
        }

        for (Transform current = collider.transform; current != null; current = current.parent)
        {
            string lowerName = current.name.ToLowerInvariant();
            if (lowerName.Contains("mat") || lowerName.Contains("carpet") ||
                lowerName.Contains("rug"))
            {
                return true;
            }
        }

        return false;
    }

    private float GetBodyRadius()
    {
        return identity == BodybuilderIdentity.Cbum || identity == BodybuilderIdentity.Ronnie
            ? 0.54f : 0.48f;
    }

    private void BeginRoamIdle()
    {
        roamState = RoamState.Idle;
        hasRoamTarget = false;
        pendingTreadmillStation = null;
        roamTargetStation = null;
        roamTargetPurposeful = false;
        roamTargetInterestLabel = null;
        hasRoamTargetArrivalRotation = false;
        ClearRoamRoute();
        stalledRoamTime = 0f;
        roamDirection = Vector3.zero;
        roamDirectionHoldUntil = 0f;
        roamIdleUntil = Time.time + Random.Range(roamIdleMin, roamIdleMax);
        // End the movement and animation state in the same physics tick. The
        // old implementation waited for the next TickRoaming call, leaving
        // one short residual glide after Run had already switched to Idle.
        StopMovingPhysicsImmediately();
        SetAnimatedMovement(false);
    }

    private bool TryBeginTreadmillVisit(GymExerciseStation station)
    {
        if (station == null || body == null || isAggressive || isPassive || isDead)
        {
            return false;
        }

        float[] speeds = { 5.5f, 7.5f, 10.5f, 13.5f, 16f };
        treadmillSpeed = speeds[Random.Range(0, speeds.Length)];
        if (!station.TryBeginEnemyTreadmill(this, treadmillSpeed))
        {
            treadmillSpeed = 0f;
            return false;
        }

        treadmillStation = station;
        treadmillEntryActive = true;
        treadmillEntryStarted = Time.time;
        treadmillEntryStartPosition = body.position;
        treadmillEntryStartRotation = body.rotation;
        float entryDistance = Vector3.ProjectOnPlane(
            station.EnemyPosition - body.position, Vector3.up).magnitude;
        treadmillEntryDuration = Mathf.Clamp(
            entryDistance / Mathf.Max(1.1f, roamSpeed), 0.85f, 1.65f);
        treadmillExitActive = false;
        treadmillUntil = Time.time + treadmillEntryDuration +
            Random.Range(15f, 26f);
        treadmillNextSpeedChangeTime = Time.time + treadmillEntryDuration +
            Random.Range(5.5f, 9.5f);

        if (roamTargetStation == station && hasRoamTarget &&
            Vector3.ProjectOnPlane(
                roamTarget - station.EnemyPosition, Vector3.up).sqrMagnitude > 0.7f * 0.7f)
        {
            treadmillExitTargetPosition = roamTarget;
        }
        else if (!TryFindTreadmillApproachPoint(
                     station, out treadmillExitTargetPosition))
        {
            Vector3 awayFromScreen = Vector3.ProjectOnPlane(
                station.EnemyRotation * Vector3.back, Vector3.up);
            if (awayFromScreen.sqrMagnitude < 0.01f)
            {
                awayFromScreen = Vector3.back;
            }

            treadmillExitTargetPosition = station.EnemyPosition +
                awayFromScreen.normalized * 2.2f;
        }
        treadmillExitTargetPosition.y = floorRootY;
        standingRootY = floorRootY;
        roamState = RoamState.Walking;
        StopMovingPhysicsImmediately();
        return true;
    }

    private void TickTreadmillEntry()
    {
        if (treadmillStation == null)
        {
            return;
        }

        if (!treadmillStation.TickEnemyTreadmill(
                this, Time.fixedDeltaTime, treadmillSpeed))
        {
            EndTreadmillVisit();
            SelectRoamDestination();
            return;
        }

        float progress = Mathf.Clamp01(
            (Time.time - treadmillEntryStarted) /
            Mathf.Max(0.01f, treadmillEntryDuration));
        float eased = SmoothStep(progress);
        Vector3 targetPosition = treadmillStation.EnemyPosition;
        body.position = Vector3.Lerp(
            treadmillEntryStartPosition, targetPosition, eased);
        body.rotation = Quaternion.Slerp(
            treadmillEntryStartRotation, treadmillStation.EnemyRotation, eased);
        StopMovingPhysicsImmediately();
        SetAnimatedMovement(
            true,
            Mathf.Lerp(
                Mathf.Clamp01(roamSpeed / Mathf.Max(0.01f, maxSpeed)),
                treadmillStation.TreadmillSpeed01(
                    treadmillStation.CurrentTreadmillSpeed),
                eased));

        if (progress < 1f)
        {
            return;
        }

        body.position = targetPosition;
        body.rotation = treadmillStation.EnemyRotation;
        standingRootY = targetPosition.y;
        treadmillEntryActive = false;
    }

    private void UpdateTreadmillTargetSpeed()
    {
        if (Time.time < treadmillNextSpeedChangeTime)
        {
            return;
        }

        float[] speeds = { 4.5f, 6.5f, 8.5f, 11f, 13.5f, 16f };
        int currentSpeedIndex = 0;
        float closestSpeedDistance = float.PositiveInfinity;
        for (int i = 0; i < speeds.Length; i++)
        {
            float distance = Mathf.Abs(speeds[i] - treadmillSpeed);
            if (distance < closestSpeedDistance)
            {
                closestSpeedDistance = distance;
                currentSpeedIndex = i;
            }
        }

        int nextSpeedIndex = Random.Range(0, speeds.Length - 1);
        if (nextSpeedIndex >= currentSpeedIndex)
        {
            nextSpeedIndex++;
        }
        treadmillSpeed = speeds[nextSpeedIndex];
        // Keep each acceleration/deceleration phase long enough to read as a
        // deliberate pace change instead of a rapid idle/animation reset.
        treadmillNextSpeedChangeTime = Time.time + Random.Range(5.5f, 9.5f);
    }

    private void BeginTreadmillExit()
    {
        if (treadmillStation == null || treadmillExitActive)
        {
            return;
        }

        treadmillEntryActive = false;
        treadmillExitActive = true;
        treadmillExitStarted = Time.time;
        Vector3 startPosition = treadmillStation.EnemyPosition;
        Vector3 exitOffset = Vector3.ProjectOnPlane(
            treadmillExitTargetPosition - startPosition, Vector3.up);
        if (exitOffset.sqrMagnitude < 0.2f * 0.2f)
        {
            if (!TryFindTreadmillApproachPoint(
                    treadmillStation, out treadmillExitTargetPosition))
            {
                treadmillExitTargetPosition = startPosition +
                    Vector3.back * 2.2f;
            }
            treadmillExitTargetPosition.y = floorRootY;
            exitOffset = Vector3.ProjectOnPlane(
                treadmillExitTargetPosition - startPosition, Vector3.up);
        }

        treadmillExitTargetPosition.y = floorRootY;
        treadmillExitDuration = Mathf.Clamp(
            exitOffset.magnitude / Mathf.Max(1.1f, roamSpeed),
            0.95f, 1.8f);
    }

    private void TickTreadmillExit()
    {
        if (treadmillStation == null)
        {
            return;
        }

        float progress = Mathf.Clamp01(
            (Time.time - treadmillExitStarted) /
            Mathf.Max(0.01f, treadmillExitDuration));
        float eased = SmoothStep(progress);
        Vector3 startPosition = treadmillStation.EnemyPosition;
        body.position = Vector3.Lerp(
            startPosition, treadmillExitTargetPosition, eased);

        Vector3 exitDirection = Vector3.ProjectOnPlane(
            treadmillExitTargetPosition - startPosition, Vector3.up);
        Quaternion exitRotation = exitDirection.sqrMagnitude > 0.01f
            ? Quaternion.LookRotation(exitDirection.normalized, Vector3.up)
            : treadmillStation.EnemyRotation;
        body.rotation = Quaternion.Slerp(
            treadmillStation.EnemyRotation, exitRotation, eased);
        StopMovingPhysicsImmediately();
        SetAnimatedMovement(
            true, Mathf.Clamp01(roamSpeed / Mathf.Max(0.01f, maxSpeed)));

        if (progress < 1f)
        {
            return;
        }

        body.position = treadmillExitTargetPosition;
        body.rotation = exitRotation;
        EndTreadmillVisit();
        SelectRoamDestination();
    }

    private void EndTreadmillVisit()
    {
        if (treadmillStation != null)
        {
            treadmillStation.EndEnemyTreadmill(this);
        }

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
        standingRootY = floorRootY;
    }

    private void StopForAttack(Vector3 planarToTarget)
    {
        if (!punchInProgress)
        {
            StopMovingPhysicsImmediately();
            SetAnimatedMovement(false);
        }
        else
        {
            StopMovingPhysicsOnly();
        }

        if (planarToTarget.sqrMagnitude > 0.01f)
        {
            Quaternion lookRotation = Quaternion.LookRotation(planarToTarget.normalized, Vector3.up);
            transform.rotation = Quaternion.Slerp(
                transform.rotation, lookRotation, 12f * Time.fixedDeltaTime);
        }
    }

    public void CelebratePlayerKill()
    {
        if (isDead)
        {
            return;
        }

        celebratingPlayerKill = true;
        punchInProgress = false;
        currentTarget = null;
        currentFighterTarget = null;
        EndTreadmillVisit();
        RestoreGokuGroundPhysicsForDeath();
        StopMovingPhysicsImmediately();
        externalBodyAnimator?.TriggerCelebration();
    }

#if UNITY_EDITOR
    // This editor-only entry point is used by GymChaosPlayModeVerifier to drive
    // the same punch path as FixedUpdate.  It deliberately does not apply
    // damage itself: ProcessPunchContact still has to observe the sampled hand
    // overlapping the target collider.
    public void BeginPunchForVerification(Transform target)
    {
        if (isDead || target == null || externalBodyAnimator == null)
        {
            return;
        }

        currentTarget = target;
        currentFighterTarget = target.GetComponent<EnemyFighter>();
        PlayerMovement targetPlayer = target.GetComponent<PlayerMovement>();
        if (targetPlayer != null)
        {
            playerTarget = targetPlayer;
        }

        punchInProgress = false;
        verificationPunchOnly = true;
        lastAttackTime = Time.time - attackCooldown;
        Vector3 direction = Vector3.ProjectOnPlane(
            target.position - transform.position, Vector3.up);
        if (direction.sqrMagnitude < 0.01f)
        {
            direction = transform.forward;
        }
        StopForAttack(direction);
        Attack(direction.normalized);
    }
#endif

    private void RefreshPoliceTarget(bool force, bool allowPlayer = true)
    {
        if (!force && Time.time < nextTargetRefreshTime)
        {
            return;
        }
        nextTargetRefreshTime = Time.time + policeTargetRefreshInterval;

        if (!IsCurrentPoliceTargetValid(allowPlayer))
        {
            SetPoliceTarget(FindNearestPoliceTarget(allowPlayer));
            return;
        }

        Transform candidate = FindNearestPoliceTarget(allowPlayer);
        if (candidate == null || candidate == currentTarget)
        {
            return;
        }

        // Ronnie is an intervention NPC, not a fixed-radius guard. Re-evaluate
        // the nearest active participant frequently so a new punch or chase in
        // another part of the room can immediately redirect him. Do not keep
        // the previous target merely because it was selected a fraction of a
        // second earlier; the nearest fight participant is the source of truth.
        if (force || candidate != currentTarget)
        {
            SetPoliceTarget(candidate);
        }
    }

    private bool IsCurrentPoliceTargetValid(bool allowPlayer)
    {
        if (isAggressive)
        {
            return allowPlayer && playerTarget != null && !playerTarget.IsDead &&
                !playerTarget.IsExercising && currentTarget == playerTarget.transform;
        }

        if (!IsFightActive || currentTarget == null)
        {
            return false;
        }
        if (currentFighterTarget != null)
        {
            return !currentFighterTarget.IsDead && currentFighterTarget != this &&
                currentFighterTarget.isAggressive && !currentFighterTarget.isPolice &&
                !currentFighterTarget.isPassive;
        }
        return allowPlayer && playerTarget != null && !playerTarget.IsDead &&
            !playerTarget.IsExercising && currentTarget == playerTarget.transform;
    }

    private Transform FindNearestPoliceTarget(bool allowPlayer)
    {
        Transform best = null;
        float bestDistance = float.PositiveInfinity;

        // Ronnie's own anger takes precedence over the room-wide fight scan:
        // direct damage from the player must make him pursue that player even
        // when nobody else is currently fighting.
        if (isAggressive)
        {
            return allowPlayer && playerTarget != null && !playerTarget.IsDead &&
                !playerTarget.IsExercising ? playerTarget.transform : null;
        }

        if (!IsFightActive)
        {
            return null;
        }

        if (allowPlayer && playerTarget != null && !playerTarget.IsDead &&
            !playerTarget.IsExercising)
        {
            best = playerTarget.transform;
            bestDistance = Vector3.ProjectOnPlane(
                best.position - transform.position, Vector3.up).sqrMagnitude;
        }

        // Query the live scene rather than relying only on the registration
        // list. This keeps Ronnie responsive if an enemy was instantiated or
        // reconfigured during the current room session.
        EnemyFighter[] sceneFighters =
            FindObjectsByType<EnemyFighter>(FindObjectsSortMode.None);
        for (int i = 0; i < sceneFighters.Length; i++)
        {
            EnemyFighter candidate = sceneFighters[i];
            if (candidate == null || candidate == this || candidate.IsDead ||
                candidate.isPolice || candidate.isPassive || !candidate.isAggressive)
            {
                continue;
            }

            float distance = Vector3.ProjectOnPlane(
                candidate.transform.position - transform.position, Vector3.up).sqrMagnitude;
            if (distance < bestDistance)
            {
                best = candidate.transform;
                bestDistance = distance;
            }
        }
        return best;
    }

    private void SetPoliceTarget(Transform target)
    {
        if (target != null)
        {
            EndTreadmillVisit();
        }
        currentTarget = target;
        currentFighterTarget = target != null ? target.GetComponent<EnemyFighter>() : null;
        targetLockedUntil = Time.time + policeMinimumTargetLock;
    }

    private void StopMovingPhysicsOnly()
    {
        if (body == null || body.isKinematic)
        {
            return;
        }

        Vector3 verticalVelocity = Vector3.Project(body.linearVelocity, Vector3.up);
        Vector3 planarVelocity = Vector3.ProjectOnPlane(body.linearVelocity, Vector3.up);
        body.linearVelocity = Vector3.Lerp(
            planarVelocity, Vector3.zero, 8f * Time.fixedDeltaTime) + verticalVelocity;
    }

    private void StopMovingPhysicsImmediately()
    {
        if (body == null || body.isKinematic)
        {
            return;
        }

        Vector3 verticalVelocity = Vector3.Project(body.linearVelocity, Vector3.up);
        body.linearVelocity = verticalVelocity;
        body.angularVelocity = Vector3.zero;
    }

    private void StopMoving()
    {
        if (IsGoku() && gokuFlightState != GokuFlightState.Grounded)
        {
            UpdateGokuFlight(false, transform.forward);
            SetAnimatedMovement(false);
            if (gokuFlightState != GokuFlightState.Grounded)
            {
                return;
            }
        }

        StopMovingPhysicsImmediately();
        SetAnimatedMovement(false);
    }

    private void KeepGroundedRoot()
    {
        if (body == null || body.isKinematic ||
            (IsGoku() && gokuFlightState != GokuFlightState.Grounded))
        {
            return;
        }

        // Configure() and SetGokuFlightPhysics(false) already freeze the
        // alive root on Y. Do not rewrite body.position or vertical velocity
        // every physics step: even a small corrective write fights Rigidbody
        // interpolation/contact solving and appears as a shake on each step.
        // Only restore the constraint if another alive-state system removed
        // it; this branch never runs for corpses because FixedUpdate exits at
        // the death guard before calling KeepGroundedRoot().
        RigidbodyConstraints groundedConstraints = body.constraints |
            RigidbodyConstraints.FreezePositionY;
        if (groundedConstraints != body.constraints)
        {
            body.constraints = groundedConstraints;
        }
    }

    private float ResolveGymFloorY(float fallback)
    {
        if (gymFloorRenderer == null)
        {
            GameObject floor = GameObject.Find("Rubber Floor");
            if (floor != null)
            {
                gymFloorRenderer = floor.GetComponent<Renderer>();
            }
        }

        return gymFloorRenderer != null ? gymFloorRenderer.bounds.max.y : fallback;
    }

    private void Attack(Vector3 direction)
    {
        punchInProgress = externalBodyAnimator != null;
        externalBodyAnimator?.TriggerAttack();
        externalBodyAnimator?.SetPunchDirection(direction);
        externalBodyAnimator?.SetPunchTarget(
            currentTarget != null ? currentTarget.position : transform.position + direction);
        body.AddForce(direction * 0.65f, ForceMode.Impulse);
        if (IsGoku())
        {
            Debug.Log(
                $"GYMCHAOS_GOKU_ATTACK state={externalBodyAnimator?.CurrentState} " +
                $"grounded={IsGokuGrounded} targetDistance=" +
                $"{(currentTarget != null ? Vector3.ProjectOnPlane(currentTarget.position - transform.position, Vector3.up).magnitude : -1f):0.00}",
                this);
        }
    }

    private bool CanStartAutomaticPunch()
    {
#if UNITY_EDITOR
        return !verificationPunchOnly;
#else
        return true;
#endif
    }

    private void ProcessPunchContact()
    {
        if (!punchInProgress || externalBodyAnimator == null)
        {
            return;
        }

        if (externalBodyAnimator.TryConsumePunchContact(out Transform leftHand, out Transform rightHand))
        {
            // The imported clip and compound limb colliders are sampled in
            // LateUpdate. Synchronize them before the physics-side contact
            // query so this frame tests the actual hand pose, not a stale
            // previous transform.
            Physics.SyncTransforms();
            bool leftHit = IsPunchHandTouchingTarget(leftHand, currentTarget);
            bool rightHit = IsPunchHandTouchingTarget(rightHand, currentTarget);
            if (leftHit || rightHit)
            {
                Vector3 direction = currentTarget != null
                    ? Vector3.ProjectOnPlane(currentTarget.position - transform.position, Vector3.up).normalized
                    : transform.forward;
                if (direction.sqrMagnitude < 0.01f)
                {
                    direction = transform.forward;
                }
                Vector3 impact = direction * attackImpulse + Vector3.up * 1.1f;
                if (currentFighterTarget != null)
                {
                    currentFighterTarget.ReceivePoliceImpact(impact);
                }
                else if (playerTarget != null && currentTarget == playerTarget.transform)
                {
                    playerTarget.ReceiveEnemyPunch(
                        EnemyPunchDamage,
                        IsGoku() ? ConstrainPlayerImpact(impact) : impact,
                        this);
                }
            }
        }

        if (externalBodyAnimator.IsPunchComplete)
        {
            punchInProgress = false;
        }
    }

    private bool IsPunchHandTouchingTarget(Transform hand, Transform target)
    {
        if (hand == null || target == null)
        {
            return false;
        }

        int count = Physics.OverlapSphereNonAlloc(
            hand.position, PunchHandContactRadius, punchContactHits,
            Physics.AllLayers, QueryTriggerInteraction.Collide);
        for (int i = 0; i < count; i++)
        {
            Collider hit = punchContactHits[i];
            if (hit == null)
            {
                continue;
            }
            Transform hitTransform = hit.transform;
            if (hitTransform == target || hitTransform.IsChildOf(target))
            {
                return true;
            }
        }

        // The compound body hitboxes can be between frames during an imported
        // clip sample. ClosestPoint still requires the animated hand itself to
        // be within the contact radius, so this is not proximity damage.
        Collider[] targetColliders = target.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < targetColliders.Length; i++)
        {
            Collider collider = targetColliders[i];
            if (collider != null && collider.enabled &&
                Vector3.Distance(hand.position, collider.ClosestPoint(hand.position)) <= PunchHandContactRadius)
            {
                return true;
            }
        }
        return false;
    }

    private Vector3 ConstrainPlayerImpact(Vector3 impact)
    {
        CharacterController controller = playerTarget != null
            ? playerTarget.GetComponent<CharacterController>()
            : null;
        Vector3 horizontal = Vector3.ProjectOnPlane(impact, Vector3.up);
        if (controller == null || horizontal.sqrMagnitude < 0.0001f)
        {
            return impact;
        }

        // PlayerMovement integrates impactVelocity with exponential damping,
        // so the unblocked travel distance is approximately impulse / 6. A
        // capsule cast limits Goku's punch to the free space before the next
        // wall instead of allowing the post-flight hit to launch the player
        // through geometry or out of the arena.
        Vector3 center = controller.transform.position + controller.center;
        float radius = Mathf.Max(0.05f, controller.radius - 0.025f);
        float halfHeight = Mathf.Max(radius, controller.height * 0.5f - radius);
        Vector3 capsuleBottom = center + Vector3.down * halfHeight;
        Vector3 capsuleTop = center + Vector3.up * halfHeight;
        Vector3 direction = horizontal.normalized;
        float expectedTravel = horizontal.magnitude / 6f;
        RaycastHit[] hits = Physics.CapsuleCastAll(
            capsuleBottom, capsuleTop, radius, direction, expectedTravel,
            Physics.AllLayers, QueryTriggerInteraction.Ignore);
        float freeDistance = expectedTravel;
        for (int i = 0; i < hits.Length; i++)
        {
            Transform hitTransform = hits[i].transform;
            if (hitTransform == null || hitTransform == transform ||
                hitTransform.IsChildOf(transform) ||
                hitTransform == playerTarget.transform ||
                hitTransform.IsChildOf(playerTarget.transform))
            {
                continue;
            }
            freeDistance = Mathf.Min(freeDistance, hits[i].distance);
        }

        if (freeDistance >= expectedTravel - 0.001f)
        {
            return impact;
        }

        float allowedMagnitude = Mathf.Max(0f, (freeDistance - 0.05f) * 6f);
        return direction * Mathf.Min(horizontal.magnitude, allowedMagnitude) +
            Vector3.up * impact.y;
    }

    public void TakeMeleeHit(Vector3 impulse, float damage, float stunDuration)
    {
        ApplyHit(impulse, damage, stunDuration, true);
    }

    public void TakeThrowableHit(Vector3 impulse, float damage, float stunDuration, bool knockdown)
    {
        // Throwable impacts are physics-only. Keep the signature for existing
        // callers, but never reuse the melee stun window: the fighter should
        // keep its current Run/Attack animation and immediately resume chase.
        _ = stunDuration;
        _ = knockdown;
        ApplyHit(impulse, damage, 0f, false);
    }

    public void ReceivePoliceImpact(Vector3 impulse)
    {
        if (isDead || body == null)
        {
            return;
        }
        body.AddForce(impulse * 0.35f, ForceMode.Impulse);
        stunnedUntilTime = Mathf.Max(stunnedUntilTime, Time.time + lightStunDuration);
    }

    private void ApplyHit(
        Vector3 impulse, float damage, float stunDuration, bool applyStun)
    {
        if (body == null || damage <= 0f)
        {
            return;
        }
        if (isDead)
        {
            ApplyCorpseImpact(impulse);
            return;
        }

        // Damage is the only gameplay event that turns a normal enemy from
        // neutral to hostile. Seeing the player, being nearby, or witnessing
        // another NPC move never starts a fight.
        BecomeAggressive();

        if (applyStun)
        {
            health = Mathf.Clamp(health - damage, 0f, maxHealth);
            body.AddForce(impulse, ForceMode.Impulse);
            body.AddTorque(Random.onUnitSphere * 3f, ForceMode.Impulse);
            stunnedUntilTime = Mathf.Max(stunnedUntilTime, Time.time + stunDuration);
        }
        else
        {
            health = Mathf.Clamp(health - damage, 0f, maxHealth);
            ApplyThrowablePushback(impulse);
            // A throw may collide while a previous recovery window is still
            // active. Do not let the throwable re-use or extend that pause.
            stunnedUntilTime = Time.time;
        }

        if (health <= 0f)
        {
            Die(impulse);
        }
    }

    private void ApplyThrowablePushback(Vector3 impulse)
    {
        if (body == null)
        {
            return;
        }

        Vector3 planarImpulse = Vector3.ProjectOnPlane(impulse, Vector3.up);
        if (planarImpulse.sqrMagnitude < 0.0001f)
        {
            return;
        }

        float pushSpeed = Mathf.Lerp(
            throwPushbackMinSpeed,
            throwPushbackMaxSpeed,
            Mathf.InverseLerp(5f, 28f, planarImpulse.magnitude));
        body.WakeUp();
        body.AddForce(
            planarImpulse.normalized * pushSpeed, ForceMode.VelocityChange);
        throwPushbackUntilTime = Time.time + throwPushbackDuration;
    }

    private void Die(Vector3 finalImpulse)
    {
        EndTreadmillVisit();
        if (visitorAgent != null)
        {
            visitorAgent.CancelForCombat();
        }
        isDead = true;
        RestoreGokuGroundPhysicsForDeath();
        health = 0f;
        deathStartedTime = Time.time;
        externalBodyAnimator?.SetDowned(true);
        SetAnimatedMovement(false);
        body.constraints = RigidbodyConstraints.None;
        body.useGravity = true;
        body.linearDamping = 0.25f;
        body.angularDamping = 0.18f;
        // Keep the broad root capsule disabled. The skeleton-following
        // compound colliders also provide corpse floor contact without
        // replacing the body with a large cylinder.
        Vector3 planarImpulse = Vector3.ProjectOnPlane(finalImpulse, Vector3.up);
        Vector3 fallAxis = planarImpulse.sqrMagnitude > 0.01f
            ? Vector3.Cross(Vector3.up, planarImpulse.normalized)
            : transform.right;
        body.AddForce(finalImpulse * 0.65f + Vector3.up * 0.8f, ForceMode.Impulse);
        body.angularVelocity = fallAxis.normalized * 4.25f;

        if (activeCounted)
        {
            ActiveCount = Mathf.Max(0, ActiveCount - 1);
            activeCounted = false;
        }
        ApplyDeadFaceMarker();
    }

    private void UpdatePermanentDeathPose()
    {
        SetAnimatedMovement(false);
        ApplyDeadFaceMarker();
        if (deathPoseFrozen)
        {
            return;
        }

        float elapsed = Time.time - deathStartedTime;
        bool settled = elapsed > 1.1f && body.linearVelocity.sqrMagnitude < 0.12f &&
            body.angularVelocity.sqrMagnitude < 0.3f;
        if (!settled && elapsed < 3.5f)
        {
            return;
        }

        body.linearVelocity = Vector3.zero;
        body.angularVelocity = Vector3.zero;
        body.linearDamping = 2.4f;
        body.angularDamping = 2f;
        body.Sleep();
        deathPoseFrozen = true;
    }

    private void ApplyCorpseImpact(Vector3 impulse)
    {
        if (body == null)
        {
            return;
        }

        deathPoseFrozen = false;
        deathStartedTime = Time.time;
        body.constraints = RigidbodyConstraints.None;
        body.linearDamping = 0.7f;
        body.angularDamping = 0.6f;
        body.WakeUp();
        body.AddForce(impulse, ForceMode.Impulse);
        Vector3 torqueAxis = Vector3.Cross(Vector3.up, Vector3.ProjectOnPlane(impulse, Vector3.up));
        if (torqueAxis.sqrMagnitude < 0.01f)
        {
            torqueAxis = transform.right;
        }
        body.AddTorque(torqueAxis.normalized * Mathf.Clamp(impulse.magnitude * 0.3f, 1.2f, 5f), ForceMode.Impulse);
    }

    private void ApplyDeadFaceMarker()
    {
        FaceCensorSettings censor = GetComponentInChildren<FaceCensorSettings>(true);
        censor?.SetDead(true);
    }

    private void SetAnimatedMovement(bool moving, float normalizedSpeed = 0f)
    {
        if (externalBodyAnimator == null)
        {
            externalBodyAnimator = GetComponentInChildren<MixamoScanRetargetAnimator>(true);
        }
        if (IsGoku())
        {
            externalBodyAnimator?.SetFlying(gokuFlightState != GokuFlightState.Grounded);
        }
        externalBodyAnimator?.SetMoving(moving, normalizedSpeed);
    }

    private bool IsGoku()
    {
        return identity == BodybuilderIdentity.Goku;
    }

    private float GetChaseSpeed()
    {
        return IsGoku() && gokuFlightState != GokuFlightState.Grounded
            ? maxSpeed * GokuSpeedMultiplier
            : maxSpeed;
    }

    private float GetDetectionRange()
    {
        if (isPolice || isAggressive)
        {
            // Ronnie and angered enemies follow activity across the room. A
            // neutral enemy never reaches this method because it is roaming.
            return float.PositiveInfinity;
        }

        return IsGoku() ? detectionRange * GokuSpeedMultiplier : detectionRange;
    }

    private bool UpdateGokuFlight(bool shouldFly, Vector3 direction)
    {
        if (!IsGoku())
        {
            return true;
        }

        direction = Vector3.ProjectOnPlane(direction, Vector3.up);
        if (direction.sqrMagnitude < 0.001f)
        {
            direction = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
        }
        if (direction.sqrMagnitude < 0.001f)
        {
            // The flying Goku mesh is intentionally rotated so its local +Y
            // axis leads the flight direction; its forward axis can therefore
            // be vertical.  Keep zero-distance steering/landing deterministic
            // instead of passing a zero vector to LookRotation.
            direction = Vector3.ProjectOnPlane(transform.up, Vector3.up);
        }
        if (direction.sqrMagnitude < 0.001f)
        {
            direction = Vector3.forward;
        }
        direction.Normalize();

        if (shouldFly && gokuFlightState == GokuFlightState.Grounded)
        {
            gokuFlightState = GokuFlightState.TakingOff;
            gokuFlightTransition = 0f;
            gokuFlightStartY = transform.position.y;
            gokuFlightStartRotation = transform.rotation;
            // The local +Y axis is the model's head direction. Rotating it onto
            // the chase vector makes the head lead the 90-degree horizontal turn.
            gokuFlightTargetRotation = GetGokuFlightRotation(direction);
            SetGokuFlightPhysics(true);
        }
        else if (!shouldFly &&
            (gokuFlightState == GokuFlightState.TakingOff || gokuFlightState == GokuFlightState.Flying))
        {
            gokuFlightState = GokuFlightState.Landing;
            gokuFlightTransition = 0f;
            gokuFlightStartY = transform.position.y;
            gokuFlightStartRotation = transform.rotation;
            gokuFlightTargetRotation = Quaternion.LookRotation(direction, Vector3.up);
        }

        if (gokuFlightState == GokuFlightState.TakingOff)
        {
            gokuFlightTransition = Mathf.Min(
                1f, gokuFlightTransition + Time.fixedDeltaTime / GokuFlightTransitionDuration);
            float eased = SmoothStep(gokuFlightTransition);
            Vector3 horizontalTarget = currentTarget != null
                ? new Vector3(currentTarget.position.x, body.position.y, currentTarget.position.z)
                : body.position;
            Vector3 nextPosition = Vector3.MoveTowards(
                body.position, horizontalTarget, GetChaseSpeed() * Time.fixedDeltaTime);
            MoveGokuFlightPosition(new Vector3(
                nextPosition.x,
                Mathf.Lerp(standingRootY, standingRootY + GokuFlightHeight, eased),
                nextPosition.z));
            body.rotation = Quaternion.Slerp(
                gokuFlightStartRotation, gokuFlightTargetRotation, eased);
            KeepGokuAboveGround();
            externalBodyAnimator?.SetFlying(true);
            if (gokuFlightTransition >= 1f)
            {
                gokuFlightState = GokuFlightState.Flying;
            }
            return false;
        }

        if (gokuFlightState == GokuFlightState.Flying)
        {
            gokuFlightTargetRotation = GetGokuFlightRotation(direction);
            body.rotation = Quaternion.RotateTowards(
                body.rotation, gokuFlightTargetRotation, 1440f * Time.fixedDeltaTime);
            Vector3 flightTarget = new Vector3(
                currentTarget != null ? currentTarget.position.x : transform.position.x,
                standingRootY + GokuFlightHeight,
                currentTarget != null ? currentTarget.position.z : transform.position.z);
            MoveGokuFlightPosition(Vector3.MoveTowards(
                body.position, flightTarget, GetChaseSpeed() * Time.fixedDeltaTime));
            KeepGokuAboveGround();
            externalBodyAnimator?.SetFlying(true);
            return true;
        }

        if (gokuFlightState == GokuFlightState.Landing)
        {
            gokuFlightTransition = Mathf.Min(
                1f, gokuFlightTransition + Time.fixedDeltaTime / GokuFlightTransitionDuration);
            float eased = SmoothStep(gokuFlightTransition);
            body.position = new Vector3(
                body.position.x,
                Mathf.Lerp(gokuFlightStartY, standingRootY, eased),
                body.position.z);
            body.rotation = Quaternion.Slerp(
                gokuFlightStartRotation, gokuFlightTargetRotation, eased);
            KeepGokuAboveGround();
            externalBodyAnimator?.SetFlying(false);
            if (gokuFlightTransition >= 1f)
            {
                gokuFlightState = GokuFlightState.Grounded;
                SetGokuFlightPhysics(false);
                // The landing frame is already on the floor. Let the normal
                // grounded combat branch run immediately so an angered Goku
                // can start a punch without spending one extra frame in Idle.
                return true;
            }
            return false;
        }

        return true;
    }

    private void KeepGokuAboveGround()
    {
        if (!IsGoku())
        {
            return;
        }

        Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
        float minimumY = float.PositiveInfinity;
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] != null && renderers[i].enabled)
            {
                minimumY = Mathf.Min(minimumY, renderers[i].bounds.min.y);
            }
        }

        if (minimumY < float.PositiveInfinity)
        {
            float requiredMinimumY = standingRootY + GokuFlightGroundClearance;
            if (minimumY < requiredMinimumY)
            {
                Vector3 correctedPosition = (body != null ? body.position : transform.position) +
                    Vector3.up * (requiredMinimumY - minimumY);
                if (body != null && body.isKinematic)
                {
                    body.position = correctedPosition;
                }
                else
                {
                    transform.position = correctedPosition;
                }
            }
        }
    }

    private void MoveGokuFlightPosition(Vector3 desiredPosition)
    {
        if (body == null)
        {
            return;
        }

        Vector3 delta = desiredPosition - body.position;
        float distance = delta.magnitude;
        if (distance < 0.0001f)
        {
            return;
        }

        // Flight is kinematic so direct position writes are intentional, but
        // they must still sweep Goku's full horizontal body volume. The broad
        // root capsule is disabled while alive because the animated limb rig
        // supplies the detailed compound body colliders; use this dedicated
        // capsule sweep so flight cannot tunnel through room geometry.
        Vector3 direction = delta / distance;
        if (TryGetGokuCapsule(out Vector3 capsuleBottom, out Vector3 capsuleTop, out float capsuleRadius))
        {
            RaycastHit[] hits = Physics.CapsuleCastAll(
                capsuleBottom, capsuleTop, capsuleRadius, direction, distance,
                Physics.AllLayers, QueryTriggerInteraction.Ignore);
            float safeDistance = distance;
            for (int i = 0; i < hits.Length; i++)
            {
                Collider hitCollider = hits[i].collider;
                if (hitCollider == null || hitCollider.isTrigger ||
                    hitCollider.transform == transform || hitCollider.transform.IsChildOf(transform))
                {
                    continue;
                }
                safeDistance = Mathf.Min(safeDistance, hits[i].distance);
            }

            if (safeDistance < distance)
            {
                body.position += direction * Mathf.Max(0f, safeDistance - 0.05f);
                return;
            }
        }

        body.position = desiredPosition;
    }

    private bool TryGetGokuCapsule(
        out Vector3 bottom, out Vector3 top, out float radius)
    {
        CapsuleCollider capsule = GetComponent<CapsuleCollider>();
        if (capsule == null)
        {
            bottom = top = transform.position;
            radius = 0f;
            return false;
        }

        Vector3 scale = transform.lossyScale;
        Vector3 axis = capsule.direction == 0 ? transform.right
            : capsule.direction == 2 ? transform.forward : transform.up;
        float axisScale = capsule.direction == 0 ? Mathf.Abs(scale.x)
            : capsule.direction == 2 ? Mathf.Abs(scale.z) : Mathf.Abs(scale.y);
        float radialScale = capsule.direction == 0
            ? Mathf.Max(Mathf.Abs(scale.y), Mathf.Abs(scale.z))
            : capsule.direction == 2
                ? Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y))
                : Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
        radius = Mathf.Max(0.04f, capsule.radius * radialScale);
        float height = Mathf.Max(radius * 2f, capsule.height * axisScale);
        float halfSegment = Mathf.Max(0f, height * 0.5f - radius);
        Vector3 center = transform.TransformPoint(capsule.center);
        bottom = center - axis.normalized * halfSegment;
        top = center + axis.normalized * halfSegment;
        return true;
    }

    private static Quaternion GetGokuFlightRotation(Vector3 direction)
    {
        // The imported Goku scan faces the opposite local horizontal direction
        // from the older player-shaped test mesh. +90 makes the head lead the
        // horizontal flight vector instead of sending the feet forward.
        if (direction.sqrMagnitude < 0.0001f ||
            float.IsNaN(direction.x) || float.IsNaN(direction.y) || float.IsNaN(direction.z))
        {
            direction = Vector3.forward;
        }
        else
        {
            direction.Normalize();
        }
        return Quaternion.LookRotation(direction, Vector3.up) *
            Quaternion.Euler(GokuFlightModelRotation, 0f, 0f);
    }

    private void SetGokuFlightPhysics(bool flying)
    {
        if (body == null)
        {
            return;
        }

        if (flying)
        {
            if (punchInProgress)
            {
                punchInProgress = false;
                externalBodyAnimator?.CancelPunch();
            }
            if (!body.isKinematic)
            {
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }
            body.useGravity = false;
            body.isKinematic = true;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        }
        else
        {
            body.isKinematic = false;
            body.useGravity = false;
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.collisionDetectionMode = gokuGroundCollisionMode;
        }
        body.constraints = flying
            ? RigidbodyConstraints.FreezeRotation
            : RigidbodyConstraints.FreezePositionY |
              RigidbodyConstraints.FreezeRotationX |
              RigidbodyConstraints.FreezeRotationZ;
    }

    private void RestoreGokuGroundPhysicsForDeath()
    {
        if (!IsGoku() || body == null || !body.isKinematic)
        {
            return;
        }

        body.isKinematic = false;
        body.useGravity = true;
        body.constraints = RigidbodyConstraints.None;
        gokuFlightState = GokuFlightState.Grounded;
        externalBodyAnimator?.SetFlying(false);
    }

    private static float SmoothStep(float value)
    {
        value = Mathf.Clamp01(value);
        return value * value * (3f - 2f * value);
    }

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
        float minimumImpactSpeed = item.ItemType == WeightType.Barbell || item.ItemType == WeightType.EzBar
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
        }
    }
}
