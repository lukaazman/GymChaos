using UnityEngine;

// A very strong player (Strength rank CorpseCarryStrengthRank or more) can
// pick up a dead body like a gym item, carry it, drop it with the interact
// key, throw it with attack (it knocks living members over) and shove it.
public partial class PlayerMovement
{
    public const int CorpseCarryStrengthRank = 8;
    private const float CorpseCarryRange = 2.6f;
    private const float CorpseCarryDistance = 1.35f;
    private const float CorpseThrowSpeed = 9f;

    private EnemyFighter carriedCorpse;
    private EnemyFighter nearbyCorpse;
    private static readonly Collider[] corpseScan = new Collider[48];
    public bool IsCarryingCorpse => carriedCorpse != null;

    private bool CanCarryCorpses()
    {
        GymExperienceService progression = GymExperienceService.Active;
        return progression != null &&
            progression.GetStatRank(GymStat.Strength) >= CorpseCarryStrengthRank;
    }

    private EnemyFighter FindCarryableCorpse()
    {
        if (!CanCarryCorpses() || playerCamera == null)
        {
            return null;
        }
        Vector3 eye = playerCamera.transform.position;
        Vector3 forward = playerCamera.transform.forward;
        int count = Physics.OverlapSphereNonAlloc(transform.position, CorpseCarryRange, corpseScan,
            ~0, QueryTriggerInteraction.Ignore);
        EnemyFighter best = null;
        float bestScore = float.NegativeInfinity;
        for (int i = 0; i < count; i++)
        {
            EnemyFighter fighter = corpseScan[i] != null ? corpseScan[i].GetComponentInParent<EnemyFighter>() : null;
            if (fighter == null || !fighter.IsDead || fighter == best)
            {
                continue;
            }
            Vector3 toBody = corpseScan[i].bounds.center - eye;
            float facing = Vector3.Dot(forward, toBody.normalized);
            if (facing < 0.35f)
            {
                continue;
            }
            float score = facing * 2f - toBody.magnitude * 0.3f;
            if (score > bestScore)
            {
                bestScore = score;
                best = fighter;
            }
        }
        return best;
    }

    private bool TryPickupCorpse()
    {
        EnemyFighter corpse = FindCarryableCorpse();
        if (corpse == null || !corpse.BeginCarriedByPlayer(playerColliders))
        {
            return false;
        }
        carriedCorpse = corpse;
        GymAudio.Play2D(GymSoundEffect.Pickup, 0.7f);
        handRig?.SetHolding(true);
        Debug.Log($"GYMCHAOS_CORPSE_PICKED_UP identity={corpse.Identity}", this);
        return true;
    }

    private void DropCarriedCorpse()
    {
        if (carriedCorpse == null)
        {
            return;
        }
        carriedCorpse.EndCarriedByPlayer(transform.forward * 1.5f + Vector3.up, false, playerColliders);
        carriedCorpse = null;
        GymAudio.Play2D(GymSoundEffect.ItemDrop, 0.6f);
        handRig?.SetHolding(false);
    }

    private void ThrowCarriedCorpse()
    {
        if (carriedCorpse == null)
        {
            return;
        }
        GymExperienceService progression = GymExperienceService.Active;
        float strength = progression != null ? progression.GetStrengthForceMultiplier() : 1f;
        Vector3 direction = (playerCamera.transform.forward + Vector3.up * 0.15f).normalized;
        handRig?.TriggerThrow(useRightThrowNext, false);
        handRig?.SetHolding(false);
        useRightThrowNext = !useRightThrowNext;
        carriedCorpse.EndCarriedByPlayer(direction * CorpseThrowSpeed * strength, true, playerColliders);
        Debug.Log($"GYMCHAOS_CORPSE_THROWN identity={carriedCorpse.Identity}", this);
        carriedCorpse = null;
        GymAudio.Play2D(GymSoundEffect.ItemDrop, 0.85f);
    }

    /// <summary>Larry is reviving the carried body: let go of it (no drop push).</summary>
    public void ReleaseCarriedCorpseForRevive(EnemyFighter corpse)
    {
        if (carriedCorpse == null || carriedCorpse != corpse)
        {
            return;
        }
        carriedCorpse.EndCarriedByPlayer(Vector3.zero, false, playerColliders);
        carriedCorpse = null;
        handRig?.SetHolding(false);
    }

    // Held low in front of the chest, following the view.
    private void UpdateCarriedCorpse()
    {
        if (carriedCorpse == null)
        {
            return;
        }
        if (!carriedCorpse.IsDead || !carriedCorpse.isActiveAndEnabled)
        {
            // Revived by Larry or despawned while carried.
            carriedCorpse = null;
            handRig?.SetHolding(false);
            return;
        }
        Vector3 forward = Vector3.ProjectOnPlane(playerCamera.transform.forward, Vector3.up).normalized;
        Vector3 target = playerCamera.transform.position + forward * CorpseCarryDistance + Vector3.down * 0.75f;
        carriedCorpse.FollowPlayerCarry(target, Quaternion.LookRotation(transform.right, Vector3.up));
    }
}
