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
    private static float SmoothStep(float value)
    {
        value = Mathf.Clamp01(value);
        return value * value * (3f - 2f * value);
    }
}
