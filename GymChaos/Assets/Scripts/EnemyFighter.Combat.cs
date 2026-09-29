using System.Collections.Generic;
using UnityEngine;

public partial class EnemyFighter
{
    public static void ReleaseNonCombatTargetLocks(PlayerMovement target)
    {
        if (target == null)
        {
            return;
        }

        for (int i = 0; i < Fighters.Count; i++)
        {
            EnemyFighter fighter = Fighters[i];
            if (fighter == null || fighter.isDead || fighter.isAggressive)
            {
                continue;
            }

            if (fighter.currentTarget == target.transform ||
                fighter.currentTarget == null)
            {
                fighter.currentTarget = null;
                fighter.currentFighterTarget = null;
                fighter.targetLockedUntil = 0f;
                fighter.nextTargetRefreshTime = Time.time + 1.25f;
            }
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
    public void SetTarget(PlayerMovement player)
    {
        playerTarget = player;
        if (!isPolice && isAggressive)
        {
            currentTarget = player != null ? player.transform : null;
            currentFighterTarget = null;
        }
    }
    public void BecomeAggressive(PlayerMovement source = null)
    {
        if (isDead || isPassive || dialogueLocked)
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
#if UNITY_EDITOR
    // The editor play-mode verifier now has to opt a fighter into combat just
    // like gameplay does, because sight alone is intentionally non-hostile.
    public void SetAggressiveForVerification(PlayerMovement source)
    {
        BecomeAggressive(source);
    }

    public void ResetAggressionForVerification()
    {
        isAggressive = false;
        currentTarget = null;
        currentFighterTarget = null;
        punchInProgress = false;
        verificationPunchOnly = false;
        stunnedUntilTime = 0f;
        throwPushbackUntilTime = 0f;
        health = maxHealth;
        RestoreGokuGroundPhysicsForDeath();
        StopMovingPhysicsImmediately();
        SelectRoamDestination();
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
        if (!TryBeginTreadmillVisit(station))
        {
            return false;
        }

        // Keep this editor-only verification session alive long enough to
        // collect grounded samples even when headless editor frames are slow.
        treadmillUntil = Time.time + 300f;
        treadmillNextSpeedChangeTime = Time.time + 300f;
        return true;
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

    public bool QueueRoamDestinationForVerification(Vector3 destination)
    {
        if (body == null || isDead || isAggressive || isPassive || isPolice)
        {
            return false;
        }

        EndTreadmillVisit();
        currentTarget = null;
        currentFighterTarget = null;
        pendingTreadmillStation = null;
        roamTargetStation = null;
        roamTarget = destination;
        roamTarget.y = ResolveGymFloorY(destination.y);
        hasRoamTarget = true;
        roamTargetPurposeful = true;
        roamTargetInterestLabel = "deadlift route verification";
        hasRoamTargetArrivalRotation = false;
        roamState = RoamState.Walking;
        stalledRoamTime = 0f;
        roamDirection = Vector3.zero;
        roamDirectionHoldUntil = 0f;
        return BuildRoamRouteToTarget(false);
    }

    public void EndTreadmillForVerification()
    {
        EndTreadmillVisit();
    }
#endif

    private void TickRoaming()
    {
        using var profileScope = TickRoamingMarker.Auto();
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
                    treadmillStation.CurrentTreadmillSpeed),
                treadmillMovementMode == TreadmillMovementMode.Running);
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
            // Idling on a machine spot that someone else now uses would box
            // the exercising enemy in; step away instead of waiting it out.
            if (Time.time >= roamIdleUntil || IsIdlingOnAnotherEnemysStation())
            {
                SelectRoamDestination();
            }
            return;
        }

        if (roamTargetStation != null && IsStationClaimedByOther(roamTargetStation))
        {
            // The machine was taken while this enemy was walking to it.
            hasRoamTarget = false;
            ClearRoamRoute();
            SelectRoamDestination();
            return;
        }

        if (roamTargetStation != null && roamTargetStation.IsDeadlift)
        {
            // Clear stale targets as well as preventing new ones from being
            // collected. This covers hot-reload/scene-authoring cases where
            // an enemy already had the player-only station selected.
            hasRoamTarget = false;
            roamTargetStation = null;
            roamTargetPurposeful = false;
            roamTargetInterestLabel = null;
            ClearRoamRoute();
            SelectRoamDestination();
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
            // path-test failure gets the continuity grace period. Do not call
            // SetAnimatedMovement(false) during the grace window: that creates
            // a visible Run -> Idle -> Run flicker while the route is intact.
            bool sameRouteDirection = roamDirection.sqrMagnitude > 0.001f &&
                Vector3.Dot(roamDirection.normalized, desiredDirection) > 0.82f;
            if (stalledRoamTime < 0.24f)
            {
                // A failed capsule probe can be caused by a one-frame enemy
                // separation update, not a real turn. Keep the established
                // direction when it is still clear; otherwise hold physics
                // without changing the previous animation state.
                if (sameRouteDirection)
                {
                    ApplyRoamMovement(roamDirection.normalized);
                }
                else
                {
                    StopMovingPhysicsImmediately();
                }
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

        if (forcedPoliceTarget != null)
        {
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
    private void Attack(Vector3 direction)
    {
        punchInProgress = externalBodyAnimator != null;
        externalBodyAnimator?.TriggerAttack();
        externalBodyAnimator?.SetPunchDirection(direction);
        externalBodyAnimator?.SetPunchTarget(
            currentTarget != null ? currentTarget.position : transform.position + direction);
        GymAudio.Play(
            GymSoundEffect.PunchAction,
            transform.position + Vector3.up * 1.1f + direction * 0.35f,
            0.58f);
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
        if (policeRangedMode)
        {
            return false;
        }
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
#if UNITY_EDITOR
            if (verificationPunchOnly)
            {
                Debug.Log(
                    $"GYMCHAOS_ENEMY_PUNCH_HAND_DIAGNOSTIC attacker={Identity} " +
                    $"left={leftHand?.position.ToString() ?? "missing"} " +
                    $"right={rightHand?.position.ToString() ?? "missing"} " +
                    $"root={transform.position} forward={transform.forward} " +
                    $"target={currentTarget?.position.ToString() ?? "missing"} " +
                    $"leftDistance={(leftHand != null && currentTarget != null ? Vector3.Distance(leftHand.position, currentTarget.position) : -1f):F3} " +
                    $"rightDistance={(rightHand != null && currentTarget != null ? Vector3.Distance(rightHand.position, currentTarget.position) : -1f):F3}",
                    this);
            }
#endif
            bool leftHit = IsPunchHandTouchingTarget(leftHand, currentTarget);
            bool rightHit = IsPunchHandTouchingTarget(rightHand, currentTarget);
            if (leftHit || rightHit)
            {
                Vector3 feedbackPosition = currentTarget != null
                    ? currentTarget.position
                    : transform.position;
                GymAudio.Play(GymSoundEffect.PunchFeedback, feedbackPosition, 1f);
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
                    if (isPolice)
                    {
                        currentFighterTarget.TakeMeleeHit(
                            impact, EnemyPunchDamage, lightStunDuration, this);
                    }
                    else
                    {
                        currentFighterTarget.ReceivePoliceImpact(impact);
                    }
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
        ApplyHit(impulse, damage, stunDuration, true, null);
    }
    public void TakeMeleeHit(
        Vector3 impulse, float damage, float stunDuration, EnemyFighter attacker)
    {
        ApplyHit(
            impulse,
            damage,
            stunDuration,
            true,
            attacker != null ? attacker.transform : null);
    }
    public void TakeThrowableHit(Vector3 impulse, float damage, float stunDuration, bool knockdown)
    {
        // Throwable impacts are physics-only. Keep the signature for existing
        // callers, but never reuse the melee stun window: the fighter should
        // keep its current Run/Attack animation and immediately resume chase.
        _ = stunDuration;
        _ = knockdown;
        ApplyHit(impulse, damage, 0f, false, null);
    }
    private void ApplyHit(
        Vector3 impulse, float damage, float stunDuration, bool applyStun,
        Transform damageSource)
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

        if (damageSource != null)
        {
            lastDamageSource = damageSource;
        }
        else if (playerTarget != null && !playerTarget.IsDead)
        {
            // PlayerMovement's established API predates explicit attacker
            // attribution. For a Ronnie death, an unqualified player hit is
            // still unambiguously caused by the player target.
            lastDamageSource = playerTarget.transform;
        }
        else if (currentFighterTarget != null && currentFighterTarget != this)
        {
            lastDamageSource = currentFighterTarget.transform;
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
        if (isDead)
        {
            return;
        }
        ReleasePoliceDoorRequest();
        RestoreDoorwayCrowdCollisions(true);

        isDead = true;
        health = 0f;
        if (identity == BodybuilderIdentity.Ronnie)
        {
            GymPoliceDirector.NotifyRonnieKilled(this, lastDamageSource);
        }
        EndTreadmillVisit();
        if (visitorAgent != null)
        {
            visitorAgent.CancelForCombat();
        }
        RestoreGokuGroundPhysicsForDeath();
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
    private float GetCurrentAttackRange()
    {
        return policeRangedMode ? attackRange * 3f : attackRange;
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
}
