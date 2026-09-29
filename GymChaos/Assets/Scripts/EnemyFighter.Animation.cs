using System.Collections.Generic;
using UnityEngine;

public partial class EnemyFighter
{
    private void SetAnimatedMovement(
        bool moving, float normalizedSpeed = 0f, bool forceRunningClip = false)
    {
        if (externalBodyAnimator == null)
        {
            externalBodyAnimator = GetComponentInChildren<MixamoScanRetargetAnimator>(true);
        }
        if (IsGoku())
        {
            externalBodyAnimator?.SetFlying(gokuFlightState != GokuFlightState.Grounded);
        }
        bool chasing = currentTarget != null && !isPassive;
        externalBodyAnimator?.SetMoving(
            moving, normalizedSpeed, forceRunningClip || isAggressive || chasing);
    }
    private void SetAnimatedMovementFromVelocity(float referenceSpeed)
    {
        if (body == null)
        {
            SetAnimatedMovement(false);
            return;
        }

        float planarSpeed = Vector3.ProjectOnPlane(body.linearVelocity, Vector3.up).magnitude;
        bool moving = planarSpeed > AnimationMovementSpeedThreshold;
        float normalizedSpeed = moving
            ? planarSpeed / Mathf.Max(0.01f, referenceSpeed)
            : 0f;
        SetAnimatedMovement(moving, Mathf.Clamp01(normalizedSpeed));
    }
    private bool IsGoku()
    {
        return identity == BodybuilderIdentity.Goku;
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
            // Keep zero-distance steering/landing deterministic instead of
            // passing a zero vector to LookRotation.
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
            gokuFlightTargetRotation = GetGokuFlightRotation(direction);
            gokuFlightProgressCheckAt = 0f;
            gokuFlightBoxedTime = 0f;
            gokuFlightRoute.Clear();
            gokuFlightRouteIndex = 0;
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
            Vector3 flightTarget = new Vector3(
                currentTarget != null ? currentTarget.position.x : transform.position.x,
                standingRootY + GokuFlightHeight,
                currentTarget != null ? currentTarget.position.z : transform.position.z);
            Vector3 toTarget = Vector3.ProjectOnPlane(flightTarget - body.position, Vector3.up);
            float step = GetChaseSpeed() * Time.fixedDeltaTime;
            TrackGokuFlightProgress();
            Vector3 steerGoal = toTarget;
            if (TryGetGokuFlightWaypoint(flightTarget, toTarget, out Vector3 waypoint))
            {
                steerGoal = Vector3.ProjectOnPlane(waypoint - body.position, Vector3.up);
            }
            Vector3 steer = ChooseGokuFlightDirection(steerGoal);
            // Turn the flight heading at a bounded rate and travel along it,
            // so detours read as arcs with the head leading instead of the
            // body snapping left/right every physics step.
            Vector3 heading = Vector3.ProjectOnPlane(body.rotation * Vector3.forward, Vector3.up);
            heading = heading.sqrMagnitude > 0.0001f ? heading.normalized : steer;
            heading = Vector3.RotateTowards(
                heading, steer, GokuFlightTurnRate * Mathf.Deg2Rad * Time.fixedDeltaTime, 0f);
            gokuFlightTargetRotation = GetGokuFlightRotation(heading);
            body.rotation = gokuFlightTargetRotation;
            Vector3 next = body.position + heading * Mathf.Min(step, toTarget.magnitude);
            next.y = Mathf.MoveTowards(body.position.y, flightTarget.y, step);
            MoveGokuFlightPosition(next);
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
        // they must still sweep Goku's horizontal flying body so flight cannot
        // tunnel through room geometry.
        Vector3 direction = delta / distance;
        if (TryGetGokuFlightProbe(direction, out Vector3 capsuleBottom, out Vector3 capsuleTop, out float capsuleRadius))
        {
            RaycastHit[] hits = Physics.CapsuleCastAll(
                capsuleBottom, capsuleTop, capsuleRadius, direction, distance,
                Physics.AllLayers, QueryTriggerInteraction.Ignore);
            float safeDistance = distance;
            for (int i = 0; i < hits.Length; i++)
            {
                if (!IsBlockingGokuFlightHit(hits[i], direction, capsuleBottom, false))
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
    private bool IsBlockingGokuFlightHit(
        RaycastHit hit, Vector3 direction, Vector3 probePoint, bool ignoreTarget)
    {
        Collider hitCollider = hit.collider;
        if (hitCollider == null || hitCollider.isTrigger ||
            hitCollider.transform == transform || hitCollider.transform.IsChildOf(transform))
        {
            return false;
        }
        if (ignoreTarget && currentTarget != null &&
            (hitCollider.transform == currentTarget || hitCollider.transform.IsChildOf(currentTarget)))
        {
            return false;
        }

        // CapsuleCast reports colliders that already overlap the start pose
        // at distance 0 with a zero point. Treating those as blockers froze
        // Goku mid-air whenever he started inside a rack edge or a wall:
        // every direction then had a safe distance of zero. Only an overlap
        // that the move pushes deeper into still blocks.
        if (hit.distance <= 0f && hit.point == Vector3.zero)
        {
            Vector3 intoCollider = Vector3.ProjectOnPlane(
                hitCollider.bounds.center - probePoint, Vector3.up);
            return Vector3.Dot(intoCollider, direction) > 0.05f;
        }
        return true;
    }
    private bool IsGokuFlightPathClear(Vector3 direction, float distance)
    {
        if (!TryGetGokuFlightProbe(direction, out Vector3 bottom, out Vector3 top, out float radius))
        {
            return true;
        }
        RaycastHit[] hits = Physics.CapsuleCastAll(
            bottom, top, radius, direction, distance,
            Physics.AllLayers, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < hits.Length; i++)
        {
            if (IsBlockingGokuFlightHit(hits[i], direction, bottom, true))
            {
                gokuFlightLastBlocker = hits[i].collider.transform.root.name + "/" + hits[i].collider.name;
                return false;
            }
        }
        return true;
    }
    // Detours can oscillate in a tight cluster of racks without ever being
    // fully boxed in. If flight has not moved at least 0.3 m within the
    // window, land and let grounded steering take over for a moment.
    private void TrackGokuFlightProgress()
    {
        if (Time.time < gokuFlightProgressCheckAt)
        {
            return;
        }
        Vector3 position = body != null ? body.position : transform.position;
        if (gokuFlightProgressCheckAt > 0f &&
            Vector3.ProjectOnPlane(position - gokuFlightProgressPosition, Vector3.up).magnitude <
                GokuFlightMinimumProgress)
        {
            gokuFlightGroundedUntil = Time.time + GokuFlightBoxedGroundTime;
        }
        gokuFlightProgressPosition = position;
        gokuFlightProgressCheckAt = Time.time + GokuFlightProgressWindow;
    }
    // A row of racks cannot be solved by local steering alone. While the
    // straight line to the target is blocked at body height, follow the room
    // grid route (the same A* planner visitors use) and steer locally toward
    // its next waypoint. The route is rebuilt at a bounded rate.
    private bool TryGetGokuFlightWaypoint(Vector3 flightTarget, Vector3 toTarget, out Vector3 waypoint)
    {
        waypoint = flightTarget;
        float distance = toTarget.magnitude;
        if (distance < 0.5f ||
            IsGokuFlightPathClear(toTarget / distance, Mathf.Min(distance, GokuFlightDirectLookahead)))
        {
            gokuFlightRoute.Clear();
            gokuFlightRouteIndex = 0;
            return false;
        }

        if (gokuFlightRoute.Count == 0 || Time.time >= gokuFlightRouteRebuildAt)
        {
            gokuFlightRouteRebuildAt = Time.time + GokuFlightRouteRebuildInterval;
            gokuFlightRouteIndex = 0;
            if (!TryBuildVisitorRoute(flightTarget, gokuFlightRoute))
            {
                gokuFlightRoute.Clear();
            }
        }

        Vector3 position = body.position;
        while (gokuFlightRouteIndex < gokuFlightRoute.Count &&
            Vector3.ProjectOnPlane(gokuFlightRoute[gokuFlightRouteIndex] - position, Vector3.up).magnitude < 0.8f)
        {
            gokuFlightRouteIndex++;
        }
        if (gokuFlightRouteIndex >= gokuFlightRoute.Count)
        {
            return false;
        }
        waypoint = gokuFlightRoute[gokuFlightRouteIndex];
        return true;
    }
    // Flight steers around blockers instead of pressing into them: probe the
    // direct line first, then fan out left/right in growing angles and keep
    // the side that worked last so Goku does not flip-flop at a wall edge.
    private Vector3 ChooseGokuFlightDirection(Vector3 toTarget)
    {
        float distance = toTarget.magnitude;
        Vector3 desired = distance > 0.001f
            ? toTarget / distance
            : Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
        if (desired.sqrMagnitude < 0.001f)
        {
            desired = Vector3.forward;
        }
        float probe = Mathf.Clamp(GetChaseSpeed() * 0.3f, 0.9f, 1.6f);
        probe = Mathf.Min(probe, Mathf.Max(0.25f, distance));

        if (Time.time >= gokuFlightDetourUntil && IsGokuFlightPathClear(desired, probe))
        {
            gokuFlightDetourSide = 0f;
            gokuFlightBoxedTime = 0f;
            return desired;
        }

        float preferred = gokuFlightDetourSide != 0f ? gokuFlightDetourSide : 1f;
        for (float angle = 25f; angle <= 150f; angle += 25f)
        {
            for (int pass = 0; pass < 2; pass++)
            {
                float side = pass == 0 ? preferred : -preferred;
                Vector3 candidate = Quaternion.AngleAxis(side * angle, Vector3.up) * desired;
                if (IsGokuFlightPathClear(candidate, probe))
                {
                    gokuFlightDetourSide = side;
                    gokuFlightDetourUntil = Time.time + 0.6f;
                    gokuFlightBoxedTime = 0f;
                    return candidate;
                }
            }
        }

        // Boxed in at body height (rack frames, a crowd, a corner): hovering
        // there would freeze Goku mid-air. After a short grace period land
        // and let the grounded obstacle steering walk him out; flight resumes
        // once the ground cooldown ends.
        gokuFlightDetourSide = 0f;
        gokuFlightBoxedTime += Time.fixedDeltaTime;
        if (gokuFlightBoxedTime >= GokuFlightBoxedLandDelay)
        {
            gokuFlightBoxedTime = 0f;
            gokuFlightGroundedUntil = Time.time + GokuFlightBoxedGroundTime;
        }
        return desired;
    }
    // The flying clip lays the body horizontally about 0.83-1.71 m above the
    // root. Sweep that volume, oriented along the travel direction, rather
    // than the upright root capsule: the standing capsule reaches the floor,
    // so benches, plates and dumbbells stopped a flight that clears them.
    private const float GokuFlightProbeHeight = 1.27f * ExternalRiggedCharacterVisual.GokuSizeMultiplier;
    private const float GokuFlightTurnRate = 420f;
    private const float GokuFlightDirectLookahead = 5f;
    private const float GokuFlightRouteRebuildInterval = 0.75f;
    private const float GokuFlightProbeRadius = 0.4f * ExternalRiggedCharacterVisual.GokuSizeMultiplier;
    private const float GokuFlightProbeHalfLength = 0.55f * ExternalRiggedCharacterVisual.GokuSizeMultiplier;
    private bool TryGetGokuFlightProbe(
        Vector3 direction, out Vector3 bottom, out Vector3 top, out float radius)
    {
        Vector3 origin = body != null ? body.position : transform.position;
        Vector3 axis = Vector3.ProjectOnPlane(direction, Vector3.up);
        if (axis.sqrMagnitude < 0.0001f)
        {
            axis = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
        }
        if (axis.sqrMagnitude < 0.0001f)
        {
            axis = Vector3.forward;
        }
        axis.Normalize();
        Vector3 center = origin + Vector3.up * GokuFlightProbeHeight;
        bottom = center - axis * GokuFlightProbeHalfLength;
        top = center + axis * GokuFlightProbeHalfLength;
        radius = GokuFlightProbeRadius;
        return true;
    }
    private static Quaternion GetGokuFlightRotation(Vector3 direction)
    {
        // The authored flying clip already lays the body horizontal with the
        // head leading +Z, so the root only yaws toward the flight vector.
        // Pitching the root as well stacks both rotations and flips Goku
        // upright and upside down.
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
    private static float SmoothStep(float value)
    {
        value = Mathf.Clamp01(value);
        return value * value * (3f - 2f * value);
    }
}
