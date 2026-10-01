using System.Collections.Generic;
using UnityEngine;

public partial class EnemyFighter
{
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
    public void PrepareVisitorWorkoutPose()
    {
        if (externalBodyAnimator == null)
        {
            externalBodyAnimator = GetComponentInChildren<MixamoScanRetargetAnimator>(true);
        }

        if (externalBodyAnimator != null && externalBodyAnimator.IsWorkoutPoseLocked)
        {
            return;
        }

        externalBodyAnimator?.PrepareForWorkoutPose();
    }
    public void ReleaseVisitorWorkoutPose()
    {
        if (externalBodyAnimator == null)
        {
            externalBodyAnimator = GetComponentInChildren<MixamoScanRetargetAnimator>(true);
        }

        externalBodyAnimator?.ReleaseWorkoutPose();
    }
    public void SetVisitorSpawnPose(
        Vector3 position,
        Quaternion rotation,
        bool keepInterpolationDisabled = false)
    {
        position.y = ResolveGymFloorY(position.y);
        if (body != null)
        {
            if (!visitorPoseInterpolationOverrideActive)
            {
                visitorPoseInterpolationBeforeSnap = body.interpolation;
                visitorPoseInterpolationOverrideActive = true;
            }
            // A final visitor pose is a deliberate authored snap. Keeping
            // Interpolate active for that write renders one frame between the
            // approach pose and the squat pose as a visible microstutter.
            body.interpolation = RigidbodyInterpolation.None;
            body.position = position;
            body.rotation = rotation;
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            visitorPoseSnapFramesRemaining = keepInterpolationDisabled ? 0 : 1;
        }
        else
        {
            transform.SetPositionAndRotation(position, rotation);
        }

        standingRootY = position.y;
        floorRootY = position.y;
        Physics.SyncTransforms();
        GetComponent<ExternalRiggedCharacterVisual>()?.RegroundAfterRootSnap();
    }
    public void BeginVisitorVehicleRide(Transform anchor)
    {
        if (!IsGoku() || anchor == null || isDead) return;
        visitorVehicleRideAnchor = anchor;
        if (body != null)
        {
            visitorVehicleRideDetectCollisions = body.detectCollisions;
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.detectCollisions = false;
            body.useGravity = false;
            body.isKinematic = true;
        }
        transform.SetPositionAndRotation(anchor.position, anchor.rotation);
        StopVisitorMovement();
        externalBodyAnimator?.SetFlying(false);
    }
    public void EndVisitorVehicleRide(Vector3 groundPosition, Quaternion rotation)
    {
        visitorVehicleRideAnchor = null;
        gokuFlightState = GokuFlightState.Grounded;
        gokuFlightTransition = 0f;
        groundPosition.y = ResolveGymFloorY(groundPosition.y);
        if (body != null)
        {
            // A visitor dismount always returns to grounded locomotion. Do not
            // restore a stale kinematic combat-flight state captured before
            // boarding, otherwise Goku walks horizontally at cloud height.
            body.isKinematic = false;
            body.useGravity = false;
            body.detectCollisions = visitorVehicleRideDetectCollisions;
            body.collisionDetectionMode = gokuGroundCollisionMode;
            body.constraints |= RigidbodyConstraints.FreezePositionY;
        }
        SetVisitorSpawnPose(groundPosition, rotation);
        visitorDismountGroundY = groundPosition.y;
        visitorDismountGroundSnapFrames = 4;
        externalBodyAnimator?.SetFlying(false);
        Physics.SyncTransforms();
    }
    public void RestoreVisitorPoseInterpolation()
    {
        if (body == null || !visitorPoseInterpolationOverrideActive)
        {
            return;
        }

        visitorPoseSnapFramesRemaining = 0;
        body.interpolation = visitorPoseInterpolationBeforeSnap;
        visitorPoseInterpolationOverrideActive = false;
        Physics.SyncTransforms();
    }
    public bool MoveVisitorTo(Vector3 destination, float speed, bool allowOutsideRoom)
    {
        return MoveVisitorTo(destination, speed, allowOutsideRoom, null, -1f);
    }
    public bool MoveVisitorAlongExteriorRoute(Vector3 destination, float speed)
    {
        return MoveVisitorAlongExteriorRoute(
            destination, speed, null, -1f, true, true);
    }
    public bool MoveVisitorAlongExteriorRoute(
        Vector3 destination, float speed, Vector3? nextWaypoint,
        float requestedCompletionRadius = -1f)
    {
        // Preserve the legacy contract: a call without a next waypoint is a
        // terminal route point, while callers that provide one continue
        // steering through the point.
        return MoveVisitorAlongExteriorRoute(
            destination, speed, nextWaypoint, requestedCompletionRadius,
            true, !nextWaypoint.HasValue);
    }
    public bool MoveVisitorAlongExteriorRoute(
        Vector3 destination, float speed, Vector3? nextWaypoint,
        float requestedCompletionRadius, bool allowWaypointPlaneCrossing)
    {
        return MoveVisitorAlongExteriorRoute(
            destination, speed, nextWaypoint, requestedCompletionRadius,
            allowWaypointPlaneCrossing, !nextWaypoint.HasValue);
    }
    public bool IsVisitorExternalPathClear(Vector3 direction, float distance)
    {
        return IsVisitorExternalPathClearFrom(
            VisitorPhysicsPosition, direction, distance);
    }
    public bool IsVisitorExternalPathClearFrom(
        Vector3 origin, Vector3 direction, float distance,
        bool ignoreDynamicBlockers = false)
    {
        if (body == null || isDead)
        {
            return false;
        }

        Vector3 planarDirection = Vector3.ProjectOnPlane(direction, Vector3.up);
        if (planarDirection.sqrMagnitude < 0.0001f || distance < 0f)
        {
            return false;
        }

        planarDirection.Normalize();
        float remaining = distance;
        while (remaining > 0.0001f)
        {
            float stepDistance = Mathf.Min(1.2f, remaining);
            if (!IsVisitorPathClear(
                planarDirection,
                Mathf.Clamp(stepDistance, 0.2f, 1.55f),
                true,
                null,
                origin,
                ignoreDynamicBlockers))
            {
                return false;
            }

            origin += planarDirection * stepDistance;
            remaining -= stepDistance;
        }

        return true;
    }
    public bool TickVisitorGroundingForMovement()
    {
        if (!IsGoku() || isDead || gokuFlightState == GokuFlightState.Grounded)
        {
            return true;
        }

        // Visitor travel owns the FixedUpdate early return, so normal combat
        // flight ticking is intentionally skipped while an enemy is entering
        // or leaving the gym. Finish Goku's landing here before writing walk
        // velocity; otherwise the kinematic flight body can remain frozen at
        // the doorway until the old timeout fallback fires.
        return UpdateGokuFlight(false, transform.forward);
    }
    public bool TryBuildVisitorRoute(Vector3 destination, List<Vector3> route)
    {
        if (route == null || body == null || isDead)
        {
            return false;
        }

        route.Clear();
        bool previousHasRoamTarget = hasRoamTarget;
        Vector3 previousRoamTarget = roamTarget;
        bool previousRoamTargetPurposeful = roamTargetPurposeful;
        string previousRoamTargetInterestLabel = roamTargetInterestLabel;
        GymExerciseStation previousRoamTargetStation = roamTargetStation;
        List<Vector3> previousRoute = new List<Vector3>(roamRouteWaypoints);
        int previousRouteIndex = roamRouteIndex;

        roamTarget = destination;
        hasRoamTarget = true;
        roamTargetPurposeful = true;
        roamTargetInterestLabel = "visitor authored route";
        roamTargetStation = null;
        bool built = BuildRoamRouteToTarget(false);
        if (built)
        {
            route.AddRange(roamRouteWaypoints);
        }

        ClearRoamRoute();
        roamRouteWaypoints.AddRange(previousRoute);
        roamRouteIndex = Mathf.Clamp(previousRouteIndex, 0, roamRouteWaypoints.Count);
        hasRoamTarget = previousHasRoamTarget;
        roamTarget = previousRoamTarget;
        roamTargetPurposeful = previousRoamTargetPurposeful;
        roamTargetInterestLabel = previousRoamTargetInterestLabel;
        roamTargetStation = previousRoamTargetStation;
        if (!built && identity == BodybuilderIdentity.Goku)
        {
            Bounds debugFloor;
            bool hasDebugFloor = TryGetRoomBounds(out debugFloor);
            Vector3 debugPosition = body != null ? body.position : transform.position;
            Debug.LogWarning(
                $"GYMCHAOS_VISITOR_ROUTE_DEBUG identity=Goku body={body != null} " +
                $"kinematic={body != null && body.isKinematic} position={debugPosition} " +
                $"destination={destination} hasFloor={hasDebugFloor} " +
                $"floor={debugFloor} bodyRadius={GetBodyRadius():F2}",
                this);
        }
        return built;
    }
    public bool MoveVisitorAlongExteriorRoute(
        Vector3 destination, float speed, Vector3? nextWaypoint,
        float requestedCompletionRadius, bool allowWaypointPlaneCrossing,
        bool stopAtDestination)
    {
        if (body == null || isDead)
        {
            return false;
        }

        destination.y = standingRootY;
        Vector3 delta = Vector3.ProjectOnPlane(destination - body.position, Vector3.up);
        float distance = delta.magnitude;
        float completionRadius = requestedCompletionRadius > 0f
            ? Mathf.Clamp(requestedCompletionRadius, 0.2f, 2.2f)
            : nextWaypoint.HasValue ? 0.78f : 0.2f;
        Vector3 outgoing = Vector3.zero;
        bool hasOutgoing = false;
        bool crossedWaypointPlane = false;
        if (nextWaypoint.HasValue)
        {
            outgoing = Vector3.ProjectOnPlane(
                nextWaypoint.Value - destination, Vector3.up);
            hasOutgoing = outgoing.sqrMagnitude > 0.01f;
            if (allowWaypointPlaneCrossing && hasOutgoing)
            {
                Vector3 outgoingDirection = outgoing.normalized;
                Vector3 fromWaypoint = Vector3.ProjectOnPlane(
                    body.position - destination, Vector3.up);
                float alongOutgoing = Vector3.Dot(fromWaypoint, outgoingDirection);
                Vector3 crossTrack = fromWaypoint - outgoingDirection * alongOutgoing;
                crossedWaypointPlane = alongOutgoing >= -0.05f &&
                    crossTrack.magnitude <= Mathf.Max(completionRadius, 0.48f);
            }
        }
        if (distance <= completionRadius || crossedWaypointPlane)
        {
            if (stopAtDestination)
            {
                StopMovingPhysicsImmediately();
                SetAnimatedMovement(false);
                visitorRouteDirection = Vector3.zero;
            }
            else if (hasOutgoing)
            {
                visitorRouteDirection = visitorRouteDirection.sqrMagnitude > 0.01f
                    ? Vector3.Slerp(
                        visitorRouteDirection.normalized,
                        outgoing.normalized, 0.42f).normalized
                    : outgoing.normalized;
            }
            return true;
        }

        RestoreVisitorPoseInterpolation();
        Vector3 desiredDirection = delta / distance;
        if (hasOutgoing && allowWaypointPlaneCrossing && distance < 7.2f)
        {
            float cornerBlend = Mathf.InverseLerp(7.2f, completionRadius, distance);
            // Anticipate the following segment only on generic routes. The
            // authored protein-shop connector passes close to a physical
            // shell; its next waypoint is retained for continuous steering,
            // but it must not pull the capsule diagonally through that shell.
            desiredDirection = Vector3.RotateTowards(
                desiredDirection, outgoing.normalized,
                Mathf.Deg2Rad * 30f * cornerBlend, 0f).normalized;
        }
        Vector3 direction = FindVisitorMovementDirection(
            desiredDirection, Mathf.Min(distance, 1.55f), true, null);
        if (direction.sqrMagnitude < 0.001f)
        {
            StopMovingPhysicsImmediately();
            SetAnimatedMovement(false);
            return false;
        }
        if (visitorStaticCollisionEgressRequested)
        {
            ApplyVisitorStaticCollisionEgress(direction, speed);
            return false;
        }
        if (visitorRouteDirection.sqrMagnitude < 0.01f)
        {
            visitorRouteDirection = direction;
        }
        else
        {
            // Turn in simulated time, like the velocity integration below. The
            // unscaled step made the turning circle grow with Time.timeScale, so
            // a sharp waypoint was orbited forever at the verifiers' 3x speed.
            float routeTurnRate = distance < 1.15f ? 360f : 105f;
            Vector3 steeredDirection = Vector3.RotateTowards(
                visitorRouteDirection.normalized, direction,
                Mathf.Deg2Rad * routeTurnRate * Time.fixedDeltaTime, 0f).normalized;
            visitorRouteDirection = IsVisitorPathClear(
                    steeredDirection, Mathf.Min(distance, 1.2f), true, null)
                ? steeredDirection
                : direction;
        }
        float movementSpeed = Mathf.Clamp(speed, 0.8f, maxSpeed);
        Vector3 planarVelocity = Vector3.ProjectOnPlane(body.linearVelocity, Vector3.up);
        float acceleration = Mathf.Max(moveForce * 0.55f, 8f);
        float nextSpeed = Mathf.MoveTowards(
            planarVelocity.magnitude, movementSpeed,
            acceleration * Time.fixedDeltaTime);
        Vector3 velocityDirection = visitorRouteDirection;
        if (planarVelocity.sqrMagnitude > 0.0025f)
        {
            float velocityTurnRate = distance < 1.15f ? 720f : 220f;
            velocityDirection = Vector3.RotateTowards(
                planarVelocity.normalized, visitorRouteDirection,
                Mathf.Deg2Rad * velocityTurnRate * Time.fixedDeltaTime, 0f).normalized;
        }
        planarVelocity = velocityDirection * nextSpeed;
        body.linearVelocity = planarVelocity + Vector3.Project(body.linearVelocity, Vector3.up);
        body.MoveRotation(Quaternion.RotateTowards(
            body.rotation,
            Quaternion.LookRotation(visitorRouteDirection, Vector3.up),
            260f * Time.fixedDeltaTime));
        SetAnimatedMovementFromVelocity(movementSpeed);
        return false;
    }
    public bool MoveVisitorTo(
        Vector3 destination,
        float speed,
        bool allowOutsideRoom,
        GymExerciseStation targetStation)
    {
        return MoveVisitorTo(
            destination, speed, allowOutsideRoom, targetStation, -1f);
    }
    public bool MoveVisitorTo(
        Vector3 destination,
        float speed,
        bool allowOutsideRoom,
        GymExerciseStation targetStation,
        float requestedArrivalRadius,
        bool allowStaticCollisionEgress = false)
    {
        if (body == null || isDead)
        {
            return false;
        }

        destination.y = standingRootY;
        Vector3 toDestination = Vector3.ProjectOnPlane(destination - body.position, Vector3.up);
        float distance = toDestination.magnitude;
        // Squat entry immediately switches to a precise authored pose. Keep
        // the final physical approach tighter so that handoff does not snap
        // the visitor across the generic 0.34m arrival radius.
        bool tightSquatApproach = targetStation != null && targetStation.IsSquat &&
            !targetStation.IsEnemySquatReleaseActive;
        float arrivalDistance = requestedArrivalRadius > 0f
            ? Mathf.Clamp(requestedArrivalRadius, 0.2f, 1.25f)
            : tightSquatApproach ? 0.008f : 0.34f;
        if (distance <= arrivalDistance)
        {
            StopMovingPhysicsImmediately();
            SetAnimatedMovement(false);
            return true;
        }

        Vector3 desired = toDestination / distance;
        Vector3 direction = FindVisitorMovementDirection(
            desired, Mathf.Min(distance, 1.25f), allowOutsideRoom, targetStation,
            allowStaticCollisionEgress);
        if (direction.sqrMagnitude < 0.001f)
        {
            StopMovingPhysicsImmediately();
            SetAnimatedMovement(false);
            return false;
        }
        if (visitorStaticCollisionEgressRequested)
        {
            ApplyVisitorStaticCollisionEgress(direction, speed);
            return false;
        }
        visitorRouteDirection = direction;

        float movementSpeed = Mathf.Clamp(speed, 0.8f, maxSpeed);
        Vector3 planarVelocity = Vector3.ProjectOnPlane(body.linearVelocity, Vector3.up);
        Vector3 desiredVelocity = direction * movementSpeed;
        planarVelocity = Vector3.MoveTowards(
            planarVelocity, desiredVelocity,
            Mathf.Max(moveForce * 0.55f, 8f) * Time.fixedDeltaTime);
        body.linearVelocity = planarVelocity + Vector3.Project(body.linearVelocity, Vector3.up);
        Quaternion lookRotation = Quaternion.LookRotation(direction, Vector3.up);
        if (targetStation != null && targetStation.IsSquat)
        {
            // Rotate toward the authored squat-facing direction during the
            // final approach. This leaves almost no orientation correction
            // for the atomic handoff when the visitor reaches the station.
            float alignment = Mathf.SmoothStep(
                0f, 1f, Mathf.InverseLerp(0.9f, arrivalDistance, distance));
            lookRotation = Quaternion.Slerp(
                lookRotation, targetStation.EnemyRotation, alignment);
        }
        body.MoveRotation(Quaternion.RotateTowards(
            body.rotation, lookRotation, 260f * Time.fixedDeltaTime));
        SetAnimatedMovementFromVelocity(movementSpeed);
        return false;
    }
    public void StopVisitorMovement()
    {
        StopMovingPhysicsImmediately();
        SetAnimatedMovement(false);
        visitorRouteDirection = Vector3.zero;
    }
    public void ResumeVisitorRoaming()
    {
        ReleaseVisitorWorkoutPose();
        RestoreVisitorPoseInterpolation();
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
    private Vector3 FindVisitorMovementDirection(
        Vector3 desiredDirection,
        float targetDistance,
        bool allowOutsideRoom,
        GymExerciseStation targetStation,
        bool allowStaticCollisionEgress = false)
    {
        using var profileScope = VisitorDirectionMarker.Auto();
        desiredDirection = Vector3.ProjectOnPlane(desiredDirection, Vector3.up);
        if (desiredDirection.sqrMagnitude < 0.0001f)
        {
            return Vector3.zero;
        }
        desiredDirection.Normalize();
        lastVisitorRouteBlocker = "none";
        visitorStaticCollisionEgressRequested = false;

        // Keep the authored exterior route shortest. Character separation is
        // useful inside the gym, but applying it to an outdoor corridor can
        // turn a direct target vector into a lateral orbit around another
        // visitor. Vehicle traffic already serializes the physical road;
        // visitor capsules still block one another through the probe below.
        Vector3 visitorSeparation = !allowOutsideRoom
            ? GetCharacterSeparation()
            : Vector3.zero;
        if (visitorSeparation.sqrMagnitude > 0.0001f)
        {
            desiredDirection =
                (desiredDirection + visitorSeparation * 0.7f).normalized;
        }

        Vector3 best = Vector3.zero;
        float bestScore = float.NegativeInfinity;
        bool routeBiasActive = visitorRouteDirection.sqrMagnitude > 0.001f &&
            Vector3.Dot(visitorRouteDirection, desiredDirection) > 0.2f;
        for (int i = 0; i < VisitorMovementProbeAngles.Length; i++)
        {
            // Angles are ordered by magnitude, so this bound only shrinks. Once
            // no remaining angle can beat the best clear one, skip the rest of
            // the capsule casts; the chosen direction is unchanged.
            float absAngle = Mathf.Abs(VisitorMovementProbeAngles[i]);
            float scoreBound = Mathf.Cos(absAngle * Mathf.Deg2Rad) * 5f -
                absAngle * 0.002f + (routeBiasActive ? 1.15f : 0f);
            if (scoreBound <= bestScore)
            {
                break;
            }
            Vector3 candidate = Quaternion.Euler(
                0f, VisitorMovementProbeAngles[i], 0f) * desiredDirection;
            if ((!allowOutsideRoom && !IsInsideRoom(candidate, targetDistance)) ||
                !IsVisitorPathClear(
                    candidate, targetDistance, allowOutsideRoom, targetStation))
            {
                continue;
            }

            float alignment = Vector3.Dot(candidate, desiredDirection);
            float score = alignment * 5f -
                Mathf.Abs(VisitorMovementProbeAngles[i]) * 0.002f;
            if (routeBiasActive)
            {
                // Keep a chosen side around an obstacle long enough to pass
                // it instead of alternating left/right every physics tick.
                score += Vector3.Dot(
                    candidate, visitorRouteDirection.normalized) * 1.15f;
            }
            if (score > bestScore)
            {
                bestScore = score;
                best = candidate;
            }
        }

        if (best.sqrMagnitude < 0.001f &&
            (allowOutsideRoom || allowStaticCollisionEgress) &&
            TryGetStaticCollisionEgressDirection(allowOutsideRoom, targetDistance, out Vector3 egressDirection))
        {
            return egressDirection;
        }

        if (best.sqrMagnitude < 0.001f &&
            (allowOutsideRoom || allowStaticCollisionEgress) &&
            Time.time >= nextVisitorPathDiagnosticTime)
        {
            Debug.LogWarning(
                $"GYMCHAOS_VISITOR_MOVE_BLOCKED identity={identity} " +
                $"position={body.position} desired={desiredDirection} " +
                $"distance={targetDistance:F2} blocker={lastVisitorRouteBlocker}",
                this);
            nextVisitorPathDiagnosticTime = Time.time + 1.5f;
        }
        return best;
    }
    private void ApplyVisitorStaticCollisionEgress(Vector3 direction, float speed)
    {
        visitorStaticCollisionEgressRequested = false;
        visitorRouteDirection = Vector3.ProjectOnPlane(direction, Vector3.up).normalized;
        Vector3 verticalVelocity = body != null
            ? Vector3.Project(body.linearVelocity, Vector3.up)
            : Vector3.zero;
        float egressSpeed = Mathf.Clamp(Mathf.Min(speed, 1.35f), 0.8f, maxSpeed);
        body.linearVelocity = visitorRouteDirection * egressSpeed + verticalVelocity;
        if (visitorRouteDirection.sqrMagnitude > 0.001f)
        {
            body.MoveRotation(Quaternion.RotateTowards(
                body.rotation,
                Quaternion.LookRotation(visitorRouteDirection, Vector3.up),
                260f * Time.fixedDeltaTime));
        }
        SetAnimatedMovementFromVelocity(egressSpeed);
    }
    private bool TryGetStaticCollisionEgressDirection(
        bool allowOutsideRoom, float targetDistance, out Vector3 direction)
    {
        direction = Vector3.zero;
        CapsuleCollider bodyCapsule = GetComponent<CapsuleCollider>();
        if (body == null || bodyCapsule == null)
        {
            return false;
        }

        Vector3 lower = body.position + Vector3.up * VisitorProbeLower;
        Vector3 upper = body.position + Vector3.up * VisitorProbeUpper;
        int count = Physics.OverlapCapsuleNonAlloc(
            lower, upper, GetBodyRadius(), roamOverlapHits,
            ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            Collider hit = roamOverlapHits[i];
            if (hit == null || hit.transform == transform ||
                hit.transform.IsChildOf(transform) ||
                hit.GetComponentInParent<EnemyFighter>() != null ||
                hit.GetComponentInParent<PlayerMovement>() != null ||
                HasRoomFloorInHierarchy(hit.transform) ||
                IsWalkableFloorSurface(hit) ||
                hit.name == "Outdoor Boundary - Path Outer" ||
                IsIgnoredVisitorRouteCollision(hit))
            {
                continue;
            }

            if (!Physics.ComputePenetration(
                bodyCapsule, body.position, body.rotation,
                hit, hit.transform.position, hit.transform.rotation,
                out Vector3 separationDirection, out float penetrationDepth) ||
                penetrationDepth <= 0.005f)
            {
                continue;
            }

            Vector3 planarSeparation =
                Vector3.ProjectOnPlane(separationDirection, Vector3.up);
            if (planarSeparation.sqrMagnitude < 0.01f)
            {
                continue;
            }

            Vector3 candidateDirection = planarSeparation.normalized;
            float stepDistance = Mathf.Clamp(
                Mathf.Min(penetrationDepth + 0.04f, 0.42f), 0.08f, 0.42f);
            if (!IsStaticCollisionEgressStepSafe(
                bodyCapsule, hit, candidateDirection, stepDistance,
                penetrationDepth, allowOutsideRoom, targetDistance))
            {
                continue;
            }

            direction = candidateDirection;
            lastVisitorRouteBlocker = hit.name;
            visitorStaticCollisionEgressRequested = true;
            if (Time.time >= nextVisitorEgressLogTime)
            {
                Debug.LogWarning(
                    $"GYMCHAOS_VISITOR_OVERLAP_EGRESS identity={identity} " +
                    $"collider={hit.name} depth={penetrationDepth:F3} " +
                    $"step={stepDistance:F2} direction={direction}",
                    this);
                nextVisitorEgressLogTime = Time.time + 1.5f;
            }
            return true;
        }

        return false;
    }
    private bool IsStaticCollisionEgressStepSafe(
        CapsuleCollider bodyCapsule, Collider separatingCollider,
        Vector3 direction, float distance, float initialPenetration,
        bool allowOutsideRoom, float targetDistance)
    {
        if (body == null || bodyCapsule == null || separatingCollider == null ||
            direction.sqrMagnitude < 0.001f || distance <= 0f)
        {
            return false;
        }

        direction = Vector3.ProjectOnPlane(direction, Vector3.up).normalized;
        if (!allowOutsideRoom && !IsInsideRoom(direction, distance))
        {
            return false;
        }

        GymVisitorAgent visitor = GetComponent<GymVisitorAgent>();
        Vector3 lower = body.position + Vector3.up * VisitorProbeLower;
        Vector3 upper = body.position + Vector3.up * VisitorProbeUpper;
        int castCount = Physics.CapsuleCastNonAlloc(
            lower, upper, GetBodyRadius(), direction, movementHits,
            distance + 0.02f, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < castCount; i++)
        {
            Collider hit = movementHits[i].collider;
            if (hit == null || hit == separatingCollider ||
                hit.transform == transform || hit.transform.IsChildOf(transform))
            {
                continue;
            }
            if (visitor != null && visitor.IsDoorwayWallCollisionIgnored &&
                GymVisitorAgent.IsDoorwayWallColliderForRouting(hit))
            {
                continue;
            }
            if (HasRoomFloorInHierarchy(hit.transform) ||
                IsWalkableFloorSurface(hit) ||
                hit.name == "Player Road Access Blocker" ||
                hit.name == "Exterior Courtyard Foundation")
            {
                continue;
            }
            return false;
        }

        Vector3 endPosition = body.position + direction * distance;
        lower = endPosition + Vector3.up * VisitorProbeLower;
        upper = endPosition + Vector3.up * VisitorProbeUpper;
        int overlapCount = Physics.OverlapCapsuleNonAlloc(
            lower, upper, GetBodyRadius(), roamOverlapHits,
            ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < overlapCount; i++)
        {
            Collider hit = roamOverlapHits[i];
            if (hit == null || hit.transform == transform ||
                hit.transform.IsChildOf(transform) ||
                HasRoomFloorInHierarchy(hit.transform) ||
                IsWalkableFloorSurface(hit) ||
                hit.name == "Player Road Access Blocker" ||
                hit.name == "Exterior Courtyard Foundation")
            {
                continue;
            }

            if (visitor != null && visitor.IsDoorwayWallCollisionIgnored &&
                GymVisitorAgent.IsDoorwayWallColliderForRouting(hit))
            {
                continue;
            }

            if (!Physics.ComputePenetration(
                bodyCapsule, endPosition, body.rotation,
                hit, hit.transform.position, hit.transform.rotation,
                out _, out float remainingPenetration) ||
                remainingPenetration <= 0.005f)
            {
                continue;
            }

            if (hit != separatingCollider ||
                remainingPenetration >= initialPenetration - 0.005f)
            {
                return false;
            }
        }

        return true;
    }
    private bool IsExteriorDynamicEnemySegmentClear(
        Vector3 start, Vector3 direction, float distance)
    {
        Vector3 planarDirection = Vector3.ProjectOnPlane(direction, Vector3.up);
        if (planarDirection.sqrMagnitude < 0.0001f || distance <= 0f)
        {
            return true;
        }

        planarDirection.Normalize();
        IReadOnlyList<EnemyFighter> registered = RegisteredFighters;
        for (int i = 0; i < registered.Count; i++)
        {
            EnemyFighter candidate = registered[i];
            if (candidate == null || candidate == this || candidate == visitorCrowdPass ||
                !candidate.isActiveAndEnabled || candidate.isDead || candidate.body == null)
            {
                continue;
            }

            Vector3 blockerPosition = candidate.VisitorPhysicsPosition;
            if (Mathf.Abs(blockerPosition.y - start.y) > EnemyCapsuleHeight)
            {
                continue;
            }

            Vector3 toBlocker = Vector3.ProjectOnPlane(blockerPosition - start, Vector3.up);
            float currentDistance = toBlocker.magnitude;
            float along = Mathf.Clamp(Vector3.Dot(toBlocker, planarDirection), 0f, distance);
            float closestDistance = Vector3.ProjectOnPlane(
                toBlocker - planarDirection * along, Vector3.up).magnitude;
            float requiredClearance = GetBodyRadiusForIdentity(identity) +
                GetBodyRadiusForIdentity(candidate.identity) + 0.05f;
            if (closestDistance >= requiredClearance ||
                !HasExteriorRouteLineOfSightToEnemy(
                    start, blockerPosition, GetBodyRadiusForIdentity(candidate.identity)))
            {
                continue;
            }

            bool notMovingDeeperIntoExistingContact =
                currentDistance < requiredClearance &&
                closestDistance >= currentDistance - 0.02f;
            if (notMovingDeeperIntoExistingContact)
            {
                continue;
            }

            lastVisitorRouteBlocker = "dynamic visitor clearance owner=" + candidate.IdentityName;
            routeBlockerFighter = candidate;
            return false;
        }

        return true;
    }
    private bool HasExteriorRouteLineOfSightToEnemy(
        Vector3 start, Vector3 blockerPosition, float blockerRadius)
    {
        Vector3 toBlocker = Vector3.ProjectOnPlane(blockerPosition - start, Vector3.up);
        float distance = toBlocker.magnitude;
        if (distance <= GetBodyRadiusForIdentity(identity) + blockerRadius + 0.3f)
        {
            return true;
        }

        Vector3 direction = toBlocker / distance;
        float startInset = GetBodyRadiusForIdentity(identity) + 0.1f;
        float endInset = blockerRadius + 0.1f;
        float rayDistance = distance - startInset - endInset;
        if (rayDistance <= 0.05f)
        {
            return true;
        }

        Vector3 rayOrigin = start + direction * startInset + Vector3.up * 1.15f;
        if (!Physics.Raycast(
                rayOrigin, direction, out RaycastHit hit, rayDistance,
                ~0, QueryTriggerInteraction.Ignore))
        {
            return true;
        }

        return hit.collider != null &&
            hit.collider.GetComponentInParent<EnemyFighter>() != null;
    }
    private bool IsVisitorPathClear(
        Vector3 direction,
        float distance,
        bool allowOutsideRoom,
        GymExerciseStation targetStation,
        Vector3? originOverride = null,
        bool ignoreDynamicBlockers = false)
    {
        using var profileScope = VisitorPathClearMarker.Auto();
        GymVisitorAgent doorwayVisitor = DoorwayVisitor;
        Vector3 origin = originOverride ?? (body != null ? body.position : transform.position);
        if (allowOutsideRoom && !ignoreDynamicBlockers &&
            !IsExteriorDynamicEnemySegmentClear(origin, direction, distance))
        {
            return false;
        }

        if (!IsDeadliftNavigationSegmentClear(origin, origin + direction * distance))
        {
            lastVisitorRouteBlocker = "deadlift station navigation clearance";
            return false;
        }
        Vector3 lower = origin + Vector3.up * VisitorProbeLower;
        Vector3 upper = origin + Vector3.up * VisitorProbeUpper;
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
            if (isPolice && hit.GetComponentInParent<GymPoliceDirector>() != null)
            {
                continue;
            }
            EnemyFighter blockedFighter = hit.GetComponentInParent<EnemyFighter>();
            if (ignoreDynamicBlockers &&
                (blockedFighter != null ||
                 hit.GetComponentInParent<PlayerMovement>() != null ||
                 hit.GetComponentInParent<GymVisitorVehicle>() != null))
            {
                continue;
            }
            if (blockedFighter != null && blockedFighter == visitorCrowdPass)
            {
                continue;
            }
            if (blockedFighter != null && blockedFighter != this)
            {
                SetRouteBlocker(hit, blockedFighter.IdentityName);
                return false;
            }
            PlayerMovement blockedPlayer = hit.GetComponentInParent<PlayerMovement>();
            if (blockedPlayer != null)
            {
                // A visitor already touching the stationary reception player
                // may take one strictly separating egress direction. This is
                // narrower than ignoring the pair and leaves all other
                // character/scene collisions active.
                if (doorwayVisitor != null &&
                    doorwayVisitor.AllowsPlayerContactEgress(
                        blockedPlayer, direction))
                {
                    continue;
                }
                SetRouteBlocker(hit, "Player");
                return false;
            }
            if (doorwayVisitor != null &&
                doorwayVisitor.IsDoorwayWallCollisionIgnored &&
                GymVisitorAgent.IsDoorwayWallColliderForRouting(hit))
            {
                continue;
            }
            if (IsPushedLooseItem(hit))
            {
                continue;
            }
            int nameFlags = GetRouteNameFlags(hit);
            if (
                HasRoomFloorInHierarchy(hit.transform) ||
                IsWalkableFloorSurface(hit) ||
                (nameFlags & RouteNameRoadBlocker) != 0 ||
                (allowOutsideRoom && (nameFlags & RouteNamePathOuter) != 0) ||
                (nameFlags & RouteNameCourtyard) != 0 ||
                (allowOutsideRoom &&
                    IsExteriorBoundaryBehindVisitor(hit, origin, direction)) ||
                (allowOutsideRoom && IsIgnoredVisitorRouteCollision(hit)))
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
            SetRouteBlocker(hit, origin, direction);
            return false;
        }

        return true;
    }
    private bool IsExteriorBoundaryBehindVisitor(
        Collider hit, Vector3 origin, Vector3 direction)
    {
        if (hit == null)
        {
            return false;
        }

        Bounds bounds = hit.bounds;
        float margin = GetBodyRadius() + 0.05f;
        return (origin.x >= bounds.max.x + margin && direction.x >= -0.05f) ||
            (origin.x <= bounds.min.x - margin && direction.x <= 0.05f) ||
            (origin.z >= bounds.max.z + margin && direction.z >= -0.05f) ||
            (origin.z <= bounds.min.z - margin && direction.z <= 0.05f);
    }
    // Set by GymVisitorAgent after repeated failed exit reroutes: loose gym
    // props (foam roller, medicine ball) stop counting as route walls for a
    // few seconds, so a visitor boxed in between a prop and a standing member
    // pushes the prop aside instead of rerouting forever.
    private const float VisitorLooseItemPushSeconds = 6f;
    private float visitorPushesLooseItemsUntil = -1f;
    internal void SetVisitorPushesLooseItems(bool value) =>
        visitorPushesLooseItemsUntil = value ? Time.time + VisitorLooseItemPushSeconds : -1f;
    public bool VisitorPushesLooseItemsForVerification => Time.time < visitorPushesLooseItemsUntil;

    // A visitor that keeps failing to get past a standing, non-hostile member
    // (seen: Ronnie idling in a gap between equipment) walks through that
    // member instead of rerouting forever. Physics and the route probes both
    // let it pass; the pass ends by itself once the two are apart, or when
    // the member turns hostile or is disabled.
    private const float VisitorCrowdPassMaxStartDistance = 2.5f;
    private const float VisitorCrowdPassEndDistance = 3.5f;
    private EnemyFighter visitorCrowdPass;
    private readonly List<Collider> visitorCrowdPassMine = new List<Collider>();
    private readonly List<Collider> visitorCrowdPassTheirs = new List<Collider>();
    public EnemyFighter VisitorCrowdPassForVerification => visitorCrowdPass;

    internal void TryVisitorCrowdPassRouteBlocker()
    {
        // Every probe that blocks on a member records that exact fighter; it
        // must still be next to this visitor (the field survives clear probes).
        EnemyFighter other = routeBlockerFighter;
        if (other == null || other == this || other.isDead || other.isAggressive ||
            (other.isPolice && other.currentTarget != null) ||
            !other.isActiveAndEnabled || visitorCrowdPass == other ||
            PlanarDistanceTo(other) > VisitorCrowdPassMaxStartDistance)
        {
            return;
        }
        ClearVisitorCrowdPass(true);
        Collider[] mine = GetComponentsInChildren<Collider>(false);
        Collider[] theirs = other.GetComponentsInChildren<Collider>(false);
        for (int a = 0; a < mine.Length; a++)
        {
            for (int b = 0; b < theirs.Length; b++)
            {
                // Only pairs this pass changes are recorded and later restored,
                // so pairs ignored by other systems stay as they were.
                if (mine[a] != null && theirs[b] != null && mine[a].enabled && theirs[b].enabled &&
                    !Physics.GetIgnoreCollision(mine[a], theirs[b]))
                {
                    Physics.IgnoreCollision(mine[a], theirs[b], true);
                    visitorCrowdPassMine.Add(mine[a]);
                    visitorCrowdPassTheirs.Add(theirs[b]);
                }
            }
        }
        visitorCrowdPass = other;
        cachedOwnColliders = null;
        Debug.Log($"GYMCHAOS_VISITOR_CROWD_PASS identity={identity} through={other.identity}", this);
    }

    /// <summary>Exit made or state changed: stop pushing props and restore collisions.</summary>
    internal void ClearVisitorStuckRecovery()
    {
        visitorPushesLooseItemsUntil = -1f;
        ClearVisitorCrowdPass(true);
    }

    // Called from FixedUpdate while a pass is active: a few comparisons.
    private void TickVisitorCrowdPass()
    {
        EnemyFighter other = visitorCrowdPass;
        if (other == null || !other.isActiveAndEnabled || other.isDead || other.isAggressive ||
            (other.isPolice && other.currentTarget != null) ||
            PlanarDistanceTo(other) > VisitorCrowdPassEndDistance)
        {
            ClearVisitorCrowdPass(other != null && other.isActiveAndEnabled);
        }
    }

    private void ClearVisitorCrowdPass(bool restore)
    {
        if (restore)
        {
            for (int i = 0; i < visitorCrowdPassMine.Count; i++)
            {
                Collider a = visitorCrowdPassMine[i];
                Collider b = visitorCrowdPassTheirs[i];
                // Disabled colliders lose their ignore state by themselves.
                if (a != null && b != null && a.enabled && b.enabled &&
                    a.gameObject.activeInHierarchy && b.gameObject.activeInHierarchy)
                {
                    Physics.IgnoreCollision(a, b, false);
                }
            }
        }
        visitorCrowdPassMine.Clear();
        visitorCrowdPassTheirs.Clear();
        if (visitorCrowdPass != null)
        {
            cachedOwnColliders = null;
        }
        visitorCrowdPass = null;
    }

    private float PlanarDistanceTo(EnemyFighter other) =>
        Vector3.ProjectOnPlane(other.transform.position - transform.position, Vector3.up).magnitude;

    private bool IsPushedLooseItem(Collider hit)
    {
        if (Time.time >= visitorPushesLooseItemsUntil || hit.attachedRigidbody == null ||
            hit.attachedRigidbody.isKinematic)
        {
            return false;
        }
        PickupItem item = hit.attachedRigidbody.GetComponent<PickupItem>();
        return item != null && !item.IsHeld;
    }

    private bool IsIgnoredVisitorRouteCollision(Collider hit)
    {
        if (hit == null)
        {
            return false;
        }

        if (IsPushedLooseItem(hit))
        {
            return true;
        }

        // Hitbox colliders are added after spawn; refresh the cached set
        // periodically instead of allocating a new array per probe hit.
        if (cachedOwnColliders == null || Time.time >= nextOwnCollidersRefresh)
        {
            cachedOwnColliders = GetComponentsInChildren<Collider>(true);
            nextOwnCollidersRefresh = Time.time + 2f;
        }
        Collider[] visitorColliders = cachedOwnColliders;
        for (int i = 0; i < visitorColliders.Length; i++)
        {
            Collider visitorCollider = visitorColliders[i];
            if (visitorCollider != null && visitorCollider != hit &&
                Physics.GetIgnoreCollision(visitorCollider, hit))
            {
                return true;
            }
        }

        return false;
    }
}
