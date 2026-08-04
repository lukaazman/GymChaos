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
    private BodybuilderEnemyAnimator bodyAnimator;
    private ExternalRiggedCharacterAnimator externalBodyAnimator;
    private MixamoScanRetargetAnimator mixamoScanAnimator;
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
    private bool deathPoseFrozen;
    private bool activeCounted;
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

    public float CurrentHealth => health;
    public float MaxHealth => maxHealth;
    public bool HasTakenDamage => health < maxHealth - 0.001f;
    public bool IsDead => isDead;
    public bool IsPolice => isPolice;
    public BodybuilderIdentity Identity => identity;
    public bool IsFlying => gokuFlightState == GokuFlightState.Flying;
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
        bodyAnimator = GetComponent<BodybuilderEnemyAnimator>();
        externalBodyAnimator = GetComponent<ExternalRiggedCharacterAnimator>();
        mixamoScanAnimator = GetComponent<MixamoScanRetargetAnimator>();
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
        if (isDead)
        {
            RestoreGokuGroundPhysicsForDeath();
            UpdatePermanentDeathPose();
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
            if (Time.time >= lastAttackTime + attackCooldown)
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
        SetAnimatedMovement(false);

        if (planarToTarget.sqrMagnitude > 0.01f)
        {
            Quaternion lookRotation = Quaternion.LookRotation(planarToTarget.normalized, Vector3.up);
            transform.rotation = Quaternion.Slerp(
                transform.rotation, lookRotation, 12f * Time.fixedDeltaTime);
        }
    }

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

        Vector3 verticalVelocity = Vector3.Project(body.linearVelocity, Vector3.up);
        Vector3 planarVelocity = Vector3.ProjectOnPlane(body.linearVelocity, Vector3.up);
        body.linearVelocity = Vector3.Lerp(
            planarVelocity, Vector3.zero, 5f * Time.fixedDeltaTime) + verticalVelocity;
        SetAnimatedMovement(false);
    }

    private void Attack(Vector3 direction)
    {
        bodyAnimator?.TriggerAttack();
        externalBodyAnimator?.TriggerAttack();
        mixamoScanAnimator?.TriggerAttack();
        body.AddForce(direction * 0.65f, ForceMode.Impulse);
        Vector3 impact = direction * attackImpulse + Vector3.up * 1.1f;
        if (currentFighterTarget != null)
        {
            currentFighterTarget.ReceivePoliceImpact(impact);
        }
        else if (playerTarget != null && currentTarget == playerTarget.transform)
        {
            playerTarget.ReceiveImpact(impact);
        }
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
        bodyAnimator?.SetDowned(true);
        externalBodyAnimator?.SetDowned(true);
        mixamoScanAnimator?.SetDowned(true);
        SetAnimatedMovement(false);
        body.constraints = RigidbodyConstraints.None;
        body.useGravity = true;
        body.linearDamping = 0.25f;
        body.angularDamping = 0.18f;
        // The detailed moving hitbox rig intentionally disables the broad
        // standing capsule. Re-enable it for corpses so the body has a stable
        // floor contact instead of falling through gaps between limb colliders.
        CapsuleCollider corpseCollider = GetComponent<CapsuleCollider>();
        if (corpseCollider != null)
        {
            corpseCollider.enabled = true;
        }
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
        if (bodyAnimator == null)
        {
            bodyAnimator = GetComponent<BodybuilderEnemyAnimator>();
        }
        if (externalBodyAnimator == null)
        {
            externalBodyAnimator = GetComponent<ExternalRiggedCharacterAnimator>();
        }
        if (mixamoScanAnimator == null)
        {
            mixamoScanAnimator = GetComponent<MixamoScanRetargetAnimator>();
        }
        if (IsGoku())
        {
            bodyAnimator?.SetFlying(gokuFlightState != GokuFlightState.Grounded);
            externalBodyAnimator?.SetFlying(gokuFlightState != GokuFlightState.Grounded);
            mixamoScanAnimator?.SetFlying(gokuFlightState != GokuFlightState.Grounded);
        }
        bodyAnimator?.SetMoving(moving, normalizedSpeed);
        externalBodyAnimator?.SetMoving(moving, normalizedSpeed);
        mixamoScanAnimator?.SetMoving(moving, normalizedSpeed);
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
            body.position = new Vector3(
                nextPosition.x,
                Mathf.Lerp(standingRootY, standingRootY + GokuFlightHeight, eased),
                nextPosition.z);
            body.rotation = Quaternion.Slerp(
                gokuFlightStartRotation, gokuFlightTargetRotation, eased);
            KeepGokuAboveGround();
            bodyAnimator?.SetFlying(true);
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
            body.position = Vector3.MoveTowards(
                body.position, flightTarget, GetChaseSpeed() * Time.fixedDeltaTime);
            KeepGokuAboveGround();
            bodyAnimator?.SetFlying(true);
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
            bodyAnimator?.SetFlying(false);
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

    private static Quaternion GetGokuFlightRotation(Vector3 direction)
    {
        // The imported Goku scan faces the opposite local horizontal direction
        // from the older player-shaped test mesh. +90 makes the head lead the
        // horizontal flight vector instead of sending the feet forward.
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
        }
        else
        {
            body.isKinematic = false;
            body.useGravity = true;
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }
        body.constraints = flying
            ? RigidbodyConstraints.FreezeRotation
            : RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
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
        bodyAnimator?.SetFlying(false);
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
