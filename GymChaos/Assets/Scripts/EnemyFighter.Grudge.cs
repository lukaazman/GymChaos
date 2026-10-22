using System.Collections.Generic;
using UnityEngine;

// Who an angry fighter fights. Every attacker that damages a fighter (the
// player, Ronnie, an officer, another member) goes on its grudge list. The
// fighter goes after the closest one on that list, like Ronnie picks the
// nearest fight participant, but only re-picks every few seconds and only
// switches for a clearly closer attacker, so two attackers at similar range
// do not make it turn back and forth.
public partial class EnemyFighter
{
    public const float GrudgeRetargetInterval = 3f;
    private const float GrudgeNewAttackerDelay = 0.4f;
    private const float GrudgeSwitchMargin = 0.75f;

    private readonly List<EnemyFighter> grudgeFighters = new List<EnemyFighter>();
    private bool grudgePlayer;
    // Angry only at fighters (never hit by the player): once they are all
    // down, calm down instead of turning on the player. A later revive by
    // Larry then finds nobody still angry at the revived person.
    private bool hadFighterGrudge;
    public int CalmDownsForVerification { get; private set; }
    private float nextGrudgeRetargetTime;

    public int GrudgeTargetSwitchesForVerification { get; private set; }
    public bool HasGrudgeAgainstForVerification(EnemyFighter other) => grudgeFighters.Contains(other);
    public bool HasPlayerGrudgeForVerification => grudgePlayer;

    private void RecordGrudge(EnemyFighter attacker)
    {
        bool added;
        if (attacker != null && attacker != this)
        {
            added = !grudgeFighters.Contains(attacker);
            if (added)
            {
                grudgeFighters.Add(attacker);
                hadFighterGrudge = true;
            }
        }
        else
        {
            added = !grudgePlayer;
            grudgePlayer = true;
        }
        if (added)
        {
            // A new attacker is considered soon, but not on the same tick.
            nextGrudgeRetargetTime = Mathf.Min(nextGrudgeRetargetTime, Time.time + GrudgeNewAttackerDelay);
            Debug.Log($"GYMCHAOS_ENEMY_GRUDGE identity={identity} against={(attacker != null ? attacker.identity.ToString() : "player")}", this);
        }
    }

    private void ClearGrudges()
    {
        grudgeFighters.Clear();
        grudgePlayer = false;
        hadFighterGrudge = false;
        nextGrudgeRetargetTime = 0f;
    }

    private bool IsGrudgeFighterValid(EnemyFighter other) =>
        other != null && other != this && other.isActiveAndEnabled && !other.isDead;

    private float PlanarDistanceTo(Transform target) =>
        Vector3.ProjectOnPlane(target.position - transform.position, Vector3.up).magnitude;

    private void RefreshGrudgeTarget()
    {
        for (int i = grudgeFighters.Count - 1; i >= 0; i--)
        {
            if (!IsGrudgeFighterValid(grudgeFighters[i]))
            {
                grudgeFighters.RemoveAt(i);
            }
        }
        if (!grudgePlayer && hadFighterGrudge && grudgeFighters.Count == 0)
        {
            CalmDownAfterFight();
            return;
        }
        bool playerAlive = playerTarget != null && !playerTarget.IsDead;
        // Aggression without a recorded attacker (reputation, older paths)
        // keeps the original contract: the player.
        bool wantsPlayer = playerTarget != null && (grudgePlayer || grudgeFighters.Count == 0);
        bool currentValid = currentTarget != null && (currentFighterTarget != null
            ? grudgeFighters.Contains(currentFighterTarget) && currentTarget == currentFighterTarget.transform
            : wantsPlayer && currentTarget == playerTarget.transform &&
                (playerAlive || grudgeFighters.Count == 0));
        if (currentValid && Time.time < nextGrudgeRetargetTime)
        {
            return;
        }
        nextGrudgeRetargetTime = Time.time + GrudgeRetargetInterval;

        Transform best = null;
        EnemyFighter bestFighter = null;
        float bestDistance = float.PositiveInfinity;
        if (wantsPlayer && (playerAlive || grudgeFighters.Count == 0))
        {
            best = playerTarget.transform;
            bestDistance = PlanarDistanceTo(best);
        }
        for (int i = 0; i < grudgeFighters.Count; i++)
        {
            float distance = PlanarDistanceTo(grudgeFighters[i].transform);
            if (distance < bestDistance)
            {
                best = grudgeFighters[i].transform;
                bestFighter = grudgeFighters[i];
                bestDistance = distance;
            }
        }
        if (best == null || best == currentTarget)
        {
            return;
        }
        if (currentValid && PlanarDistanceTo(currentTarget) - bestDistance < GrudgeSwitchMargin)
        {
            return;
        }
        Transform previous = currentTarget;
        currentTarget = best;
        currentFighterTarget = bestFighter;
        if (previous != null)
        {
            GrudgeTargetSwitchesForVerification++;
        }
        Debug.Log($"GYMCHAOS_ENEMY_GRUDGE_TARGET identity={identity} target={(bestFighter != null ? bestFighter.identity.ToString() : "player")} distance={bestDistance:F1}", this);
    }

    private void CalmDownAfterFight()
    {
        ClearGrudges();
        isAggressive = false;
        currentTarget = null;
        currentFighterTarget = null;
        punchInProgress = false;
        CalmDownsForVerification++;
        Debug.Log($"GYMCHAOS_ENEMY_CALMED identity={identity} reason=attackers-down", this);
        if (visitorAgent == null)
        {
            SelectRoamDestination();
        }
    }

    // Damage while sitting in a car or riding Goku's cloud: get out and fight.
    private void LeaveVehicleForCombat()
    {
        Transform anchor = visitorVehicleRideAnchor;
        if (anchor == null && !visitorVehicleSeated)
        {
            return;
        }
        if (visitorVehicleSeated)
        {
            GymVehicleDriverSeat seat = anchor != null ? anchor.GetComponentInParent<GymVehicleDriverSeat>(true) : null;
            if (seat != null && seat.Driver == this)
            {
                seat.EjectDriverForCombat();
            }
            if (visitorVehicleSeated)
            {
                EndVisitorVehicleSeat(GroundBelow(transform.position), Quaternion.Euler(0f, transform.eulerAngles.y, 0f));
            }
        }
        else
        {
            GymVisitorVehicle vehicle = anchor.GetComponentInParent<GymVisitorVehicle>(true);
            vehicle?.ForgetRider(this);
            EndVisitorVehicleRide(GroundBelow(transform.position), Quaternion.Euler(0f, transform.eulerAngles.y, 0f));
        }
        Debug.Log($"GYMCHAOS_ENEMY_LEFT_VEHICLE_FOR_COMBAT identity={identity} position={transform.position}", this);
    }

    private Vector3 GroundBelow(Vector3 position)
    {
        RaycastHit[] hits = Physics.RaycastAll(position + Vector3.up * 0.5f, Vector3.down, 60f,
            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        float best = float.NegativeInfinity;
        for (int i = 0; i < hits.Length; i++)
        {
            Transform hit = hits[i].transform;
            if (hit == null || hit.IsChildOf(transform) || hits[i].rigidbody != null ||
                hit.GetComponentInParent<GymVisitorVehicle>() != null)
            {
                continue;
            }
            best = Mathf.Max(best, hits[i].point.y);
        }
        if (!float.IsNegativeInfinity(best))
        {
            position.y = best;
        }
        return position;
    }
}
