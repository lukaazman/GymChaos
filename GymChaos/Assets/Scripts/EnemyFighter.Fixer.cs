using System;
using UnityEngine;

/// <summary>
/// Jolly Dog's repair hooks: a settled corpse is replaced by the same person
/// at full health and calm, and a revived police officer walks back to the
/// parked police car instead of resuming the intervention.
/// </summary>
public partial class EnemyFighter
{
    private Transform policeReturnTarget;
    private Action<EnemyFighter> policeReturnArrived;
    private const float PoliceReturnArrivalRadius = 2.2f;
    // Rigidbody damping from before death (corpses use heavy settling damping).
    private float livingLinearDamping = 1.5f;
    private float livingAngularDamping = 0.5f;

    /// <summary>Dead and lying still: what the fixer treats as broken.</summary>
    public bool IsSettledCorpse => isDead && deathPoseFrozen;
    public bool IsReturningToPoliceCar => policeReturnTarget != null;

    public bool ReviveByFixer(Vector3 standPosition, Quaternion facing)
    {
        if (!isDead)
        {
            return false;
        }

        isDead = false;
        deathPoseFrozen = false;
        celebratingPlayerKill = false;
        stunnedUntilTime = 0f;
        policeReturnTarget = null;
        policeReturnArrived = null;
        Quaternion upright = Quaternion.Euler(0f, facing.eulerAngles.y, 0f);
        if (body != null)
        {
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.position = standPosition;
            body.rotation = upright;
            body.linearDamping = livingLinearDamping;
            body.angularDamping = livingAngularDamping;
        }
        transform.SetPositionAndRotation(standPosition, upright);
        Physics.SyncTransforms();

        // Configure resets health, aggression, targets, roaming and the
        // floor-locked body exactly like a fresh spawn of this person.
        Configure(identity, playerTarget, maxHealth, isPolice, isPassive, countsAsOpponent);
        externalBodyAnimator?.SetDowned(false);
        SetAnimatedMovement(false);
        FaceCensorSettings censor = GetComponentInChildren<FaceCensorSettings>(true);
        censor?.SetDead(false);
        GetComponent<ExternalRiggedCharacterVisual>()?.RegroundAfterRootSnap();
        Debug.Log(
            $"GYMCHAOS_FIXER_REVIVED identity={identity} health={health:F0} " +
            $"position={standPosition}", this);
        return true;
    }

    /// <summary>
    /// Stops the intervention: walk to <paramref name="carPoint"/> on the
    /// police corridor and report arrival once beside the car.
    /// </summary>
    public void BeginPoliceReturnToCar(Transform carPoint, Action<EnemyFighter> arrived)
    {
        if (!isPolice || carPoint == null)
        {
            return;
        }

        policeReturnTarget = carPoint;
        policeReturnArrived = arrived;
        forcedPoliceTarget = carPoint;
        isAggressive = false;
        policeRangedMode = false;
        policePursuitRoute.Clear();
        policePursuitRouteIndex = 0;
        policePursuitNextRebuildTime = 0f;
    }

    private void TickPoliceReturnToCar()
    {
        currentTarget = policeReturnTarget;
        currentFighterTarget = null;
        bool routed = TickAuthoredPolicePursuit();
        Vector3 planar = Vector3.ProjectOnPlane(
            policeReturnTarget.position - body.position, Vector3.up);
        if (planar.magnitude <= PoliceReturnArrivalRadius)
        {
            StopMoving();
            ReleasePoliceDoorRequest();
            Action<EnemyFighter> arrived = policeReturnArrived;
            policeReturnTarget = null;
            policeReturnArrived = null;
            forcedPoliceTarget = null;
            currentTarget = null;
            arrived?.Invoke(this);
            return;
        }

        if (!routed)
        {
            // Inside the room or already on the last leg: walk straight on.
            MovePoliceAlongAuthoredWaypoint(
                policeReturnTarget.position, PoliceReturnArrivalRadius * 0.8f);
        }
    }
}
