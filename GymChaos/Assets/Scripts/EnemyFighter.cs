using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class EnemyFighter : MonoBehaviour
{
    private static readonly List<EnemyFighter> Fighters = new List<EnemyFighter>();

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
    [SerializeField] private float policeTargetRefreshInterval = 1.25f;
    [SerializeField] private float policeMinimumTargetLock = 2.5f;

    private PlayerMovement playerTarget;
    private Rigidbody body;
    private MixamoScanRetargetAnimator externalBodyAnimator;
    private BodybuilderIdentity identity;
    private Transform currentTarget;
    private EnemyFighter currentFighterTarget;
    private float health;
    private float lastAttackTime = -999f;
    private float stunnedUntilTime;
    private float nextTargetRefreshTime;
    private float targetLockedUntil;
    private float deathStartedTime;
    private bool isPolice;
    private bool isPassive;
    private bool isDead;
    private bool celebratingPlayerKill;
    private bool punchInProgress;
    private bool deathPoseFrozen;
    private bool activeCounted;
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
    public BodybuilderIdentity Identity => identity;
    public bool IsFlying => gokuFlightState == GokuFlightState.Flying;
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
        standingRootY = transform.position.y;
        gokuFlightState = GokuFlightState.Grounded;
        currentTarget = police ? null : player != null ? player.transform : null;
        currentFighterTarget = null;
        nextTargetRefreshTime = 0f;
        if (body != null)
        {
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
        if (!isPolice)
        {
            currentTarget = player != null ? player.transform : null;
            currentFighterTarget = null;
        }
    }

    private void Awake()
    {
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
        Fighters.Remove(this);
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
            RestoreGokuGroundPhysicsForDeath();
            UpdatePermanentDeathPose();
            return;
        }

        KeepGroundedRoot();

        if (celebratingPlayerKill)
        {
            RestoreGokuGroundPhysicsForDeath();
            StopMovingPhysicsOnly();
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
            RefreshPoliceTarget(false);
        }
        else
        {
            currentTarget = playerTarget != null ? playerTarget.transform : null;
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
            StopMoving();
            return;
        }

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
            StopMoving();
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

    private void StopForAttack(Vector3 planarToTarget)
    {
        Vector3 verticalVelocity = Vector3.Project(body.linearVelocity, Vector3.up);
        Vector3 planarVelocity = Vector3.ProjectOnPlane(body.linearVelocity, Vector3.up);
        body.linearVelocity = Vector3.MoveTowards(
            planarVelocity, Vector3.zero, 18f * Time.fixedDeltaTime) + verticalVelocity;
        if (!punchInProgress)
        {
            SetAnimatedMovement(false);
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
        RestoreGokuGroundPhysicsForDeath();
        StopMovingPhysicsOnly();
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

        if (!force && Time.time < targetLockedUntil)
        {
            return;
        }

        Transform candidate = FindNearestPoliceTarget(allowPlayer);
        if (candidate == null || candidate == currentTarget)
        {
            return;
        }

        float currentDistance = Vector3.ProjectOnPlane(
            currentTarget.position - transform.position, Vector3.up).sqrMagnitude;
        float candidateDistance = Vector3.ProjectOnPlane(
            candidate.position - transform.position, Vector3.up).sqrMagnitude;

        // A new target must be materially closer. This prevents rapid target
        // flipping when two people stand at almost the same distance.
        if (candidateDistance < currentDistance * 0.72f)
        {
            SetPoliceTarget(candidate);
        }
    }

    private bool IsCurrentPoliceTargetValid(bool allowPlayer)
    {
        if (currentTarget == null)
        {
            return false;
        }
        if (currentFighterTarget != null)
        {
            return !currentFighterTarget.IsDead && currentFighterTarget != this;
        }
        return allowPlayer && playerTarget != null && currentTarget == playerTarget.transform;
    }

    private Transform FindNearestPoliceTarget(bool allowPlayer)
    {
        Transform best = null;
        float bestDistance = float.PositiveInfinity;

        if (allowPlayer && playerTarget != null && !playerTarget.IsExercising)
        {
            best = playerTarget.transform;
            bestDistance = Vector3.ProjectOnPlane(
                best.position - transform.position, Vector3.up).sqrMagnitude;
        }

        for (int i = 0; i < Fighters.Count; i++)
        {
            EnemyFighter candidate = Fighters[i];
            if (candidate == null || candidate == this || candidate.IsDead)
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
        currentTarget = target;
        currentFighterTarget = target != null ? target.GetComponent<EnemyFighter>() : null;
        targetLockedUntil = Time.time + policeMinimumTargetLock;
    }

    private void StopMovingPhysicsOnly()
    {
        if (body == null)
        {
            return;
        }

        Vector3 verticalVelocity = Vector3.Project(body.linearVelocity, Vector3.up);
        Vector3 planarVelocity = Vector3.ProjectOnPlane(body.linearVelocity, Vector3.up);
        body.linearVelocity = Vector3.Lerp(
            planarVelocity, Vector3.zero, 8f * Time.fixedDeltaTime) + verticalVelocity;
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

        StopMovingPhysicsOnly();
        SetAnimatedMovement(false);
    }

    private void KeepGroundedRoot()
    {
        if (body == null || body.isKinematic ||
            (IsGoku() && gokuFlightState != GokuFlightState.Grounded))
        {
            return;
        }

        Vector3 position = body.position;
        position.y = standingRootY;
        body.position = position;
        Vector3 velocity = body.linearVelocity;
        body.linearVelocity = new Vector3(velocity.x, 0f, velocity.z);
        body.angularVelocity = Vector3.zero;
    }

    private void Attack(Vector3 direction)
    {
        punchInProgress = externalBodyAnimator != null;
        externalBodyAnimator?.TriggerAttack();
        externalBodyAnimator?.SetPunchDirection(direction);
        externalBodyAnimator?.SetPunchTarget(
            currentTarget != null ? currentTarget.position : transform.position + direction);
        body.AddForce(direction * 0.65f, ForceMode.Impulse);
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
        ApplyHit(impulse, damage, stunDuration);
    }

    public void TakeThrowableHit(Vector3 impulse, float damage, float stunDuration, bool knockdown)
    {
        // Knockdown is intentionally ignored: fighters only fall at zero health.
        ApplyHit(impulse, damage, stunDuration);
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

    private void ApplyHit(Vector3 impulse, float damage, float stunDuration)
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

        health = Mathf.Clamp(health - damage, 0f, maxHealth);
        body.AddForce(impulse, ForceMode.Impulse);
        body.AddTorque(Random.onUnitSphere * 3f, ForceMode.Impulse);
        stunnedUntilTime = Mathf.Max(stunnedUntilTime, Time.time + stunDuration);

        if (health <= 0f)
        {
            Die(impulse);
        }
    }

    private void Die(Vector3 finalImpulse)
    {
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
        float stunDuration = impactSpeed > 6f ? heavyStunDuration : lightStunDuration;
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
            TakeThrowableHit(impulse, damage, stunDuration, false);
        }
    }
}
