using System.Collections.Generic;
using UnityEngine;

// A corpse the (very strong) player carries, drops or throws. While thrown it
// knocks into living members it flies through (fighters do not collide with
// each other physically, so contacts are found by overlap).
public partial class EnemyFighter
{
    private const float ThrownCorpseSeconds = 2.5f;
    private const float ThrownCorpseMinSpeed = 3f;

    private bool carriedByPlayer;
    private float carrySnaggedTime;
    private float thrownCorpseUntil = -1f;
    private float corpsePlayerCollisionRestoreAt = -1f;
    private Collider[] corpseIgnoredPlayerColliders;
    private readonly HashSet<EnemyFighter> corpseThrowHits = new HashSet<EnemyFighter>();
    private static readonly Collider[] corpseThrowOverlap = new Collider[24];

    public bool IsCarriedByPlayer => carriedByPlayer;
    public bool IsThrownCorpse => isDead && Time.time < thrownCorpseUntil;

    public bool BeginCarriedByPlayer(Collider[] playerColliders)
    {
        if (!isDead || body == null || carriedByPlayer)
        {
            return false;
        }
        carriedByPlayer = true;
        thrownCorpseUntil = -1f;
        deathPoseFrozen = false;
        body.isKinematic = false;
        body.constraints = RigidbodyConstraints.None;
        body.useGravity = false;
        body.linearDamping = 6f;
        body.angularDamping = 6f;
        body.WakeUp();
        SetPlayerCorpseCollision(playerColliders, true);
        corpseIgnoredPlayerColliders = playerColliders;
        corpsePlayerCollisionRestoreAt = -1f;
        return true;
    }

    public void FollowPlayerCarry(Vector3 target, Quaternion rotation)
    {
        if (!carriedByPlayer || body == null)
        {
            return;
        }
        Vector3 offset = target - body.worldCenterOfMass;
        // Snagged on furniture: bring it back into the player's hands.
        carrySnaggedTime = offset.magnitude > 1f ? carrySnaggedTime + Time.deltaTime : 0f;
        if (carrySnaggedTime > 0.3f)
        {
            carrySnaggedTime = 0f;
            body.position += offset;
            transform.position = body.position;
            offset = Vector3.zero;
        }
        body.linearVelocity = Vector3.ClampMagnitude(offset * 12f, 14f);
        body.angularVelocity = Vector3.zero;
        body.MoveRotation(Quaternion.Slerp(body.rotation, rotation, 8f * Time.deltaTime));
    }

    public void EndCarriedByPlayer(Vector3 velocity, bool thrown, Collider[] playerColliders)
    {
        if (!carriedByPlayer)
        {
            return;
        }
        carriedByPlayer = false;
        deathPoseFrozen = false;
        deathStartedTime = Time.time;
        body.useGravity = true;
        body.linearDamping = 0.7f;
        body.angularDamping = 0.6f;
        body.linearVelocity = velocity;
        if (thrown)
        {
            body.AddTorque(Random.onUnitSphere * 2f, ForceMode.VelocityChange);
            thrownCorpseUntil = Time.time + ThrownCorpseSeconds;
            corpseThrowHits.Clear();
        }
        corpseIgnoredPlayerColliders = playerColliders;
        corpsePlayerCollisionRestoreAt = Time.time + 0.4f;
    }

    private void SetPlayerCorpseCollision(Collider[] playerColliders, bool ignore)
    {
        if (playerColliders == null)
        {
            return;
        }
        Collider[] mine = GetComponentsInChildren<Collider>(false);
        for (int a = 0; a < mine.Length; a++)
        {
            for (int b = 0; b < playerColliders.Length; b++)
            {
                if (mine[a] != null && playerColliders[b] != null && mine[a].enabled && playerColliders[b].enabled)
                {
                    Physics.IgnoreCollision(mine[a], playerColliders[b], ignore);
                }
            }
        }
    }

    // Called from FixedUpdate while dead. True while the player carries it
    // (the death pose must not freeze the body in mid-air).
    private bool TickCarriedOrThrownCorpse()
    {
        if (corpsePlayerCollisionRestoreAt > 0f && Time.time >= corpsePlayerCollisionRestoreAt)
        {
            corpsePlayerCollisionRestoreAt = -1f;
            SetPlayerCorpseCollision(corpseIgnoredPlayerColliders, false);
            corpseIgnoredPlayerColliders = null;
        }
        if (carriedByPlayer)
        {
            return true;
        }
        if (Time.time >= thrownCorpseUntil || body == null ||
            body.linearVelocity.magnitude < ThrownCorpseMinSpeed)
        {
            return false;
        }
        Vector3 center = body.worldCenterOfMass;
        int count = Physics.OverlapSphereNonAlloc(center, 0.75f, corpseThrowOverlap,
            ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            EnemyFighter other = corpseThrowOverlap[i] != null
                ? corpseThrowOverlap[i].GetComponentInParent<EnemyFighter>()
                : null;
            if (other == null || other == this || other.isDead || !corpseThrowHits.Add(other))
            {
                continue;
            }
            float speed = body.linearVelocity.magnitude;
            Vector3 impulse = body.linearVelocity.normalized * Mathf.Clamp(speed * 2.2f, 6f, 28f);
            // Thrown by the player: no attacker argument, like any throwable.
            other.TakeThrowableHit(impulse, Mathf.Clamp(speed * 2.5f, 10f, 40f), 0f, false);
            GymAudio.Play(GymSoundEffect.ThrownEnemyImpact, center, 1f);
            GymExperienceService.Active?.RegisterCombatHit(other);
            if (other.IsDead)
            {
                GymExperienceService.Active?.RegisterEnemyDefeat(other);
            }
            Debug.Log($"GYMCHAOS_CORPSE_THROW_HIT corpse={identity} hit={other.identity}", this);
        }
        return false;
    }
}
