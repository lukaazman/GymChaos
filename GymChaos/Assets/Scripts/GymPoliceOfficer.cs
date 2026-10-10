using UnityEngine;

/// <summary>
/// Controls the dispatched policeman's hand-to-hand to firearm transition.
/// EnemyFighter owns navigation and ordinary punch cadence; this component
/// owns the explicit gun animation and one-shot-per-clip firing contract.
/// </summary>
public sealed class GymPoliceOfficer : MonoBehaviour
{
    private const float ShotInterval = 0.62f;

    private EnemyFighter fighter;
    private MixamoScanRetargetAnimator animator;
    private GymPoliceWeapon weapon;
    private Transform killerTarget;
    private bool drawingStarted;
    private bool armed;
    private float nextShotTime;
    private bool playerKillCelebrationSent;
    private float handAttackRange;
    private bool handToHandObserved;
    private bool rangedModeObserved;
    private bool preDrawWeaponHiddenObserved;
    private bool gunDrawingClipObserved;
    private bool gunShootingClipObserved;
    private bool shotAnimationObserved;

    public bool IsArmed => armed;
    public Transform KillerTarget => killerTarget;
    public float HandAttackRange => handAttackRange;
    public int GunDrawingCount { get; private set; }
    public int GunShotCount { get; private set; }
    public bool HasObservedHandToHand => handToHandObserved;
    public bool HasObservedRangedMode => rangedModeObserved;
    public bool HasPlayerKillCelebration => playerKillCelebrationSent;
    public bool IsWeaponVisibleForVerification => weapon != null &&
        weapon.IsVisible;
    public bool HasObservedPreDrawWeaponHidden => preDrawWeaponHiddenObserved;
    public bool HasObservedGunDrawingClip => gunDrawingClipObserved;
    public bool HasObservedGunShootingClip => gunShootingClipObserved;
    public bool HasObservedShotAnimation => shotAnimationObserved;

    public void Configure(EnemyFighter policeFighter, Transform target)
    {
        fighter = policeFighter != null
            ? policeFighter : GetComponent<EnemyFighter>();
        animator = GetComponent<MixamoScanRetargetAnimator>();
        weapon = GetComponent<GymPoliceWeapon>();
        killerTarget = target;
        if (weapon == null)
        {
            weapon = gameObject.AddComponent<GymPoliceWeapon>();
        }
        weapon.Configure(fighter);
        if (fighter != null)
        {
            fighter.SetPoliceKillerTarget(killerTarget);
            handAttackRange = fighter.CurrentAttackRange;
        }
    }

    private void Start()
    {
        if (fighter == null)
        {
            fighter = GetComponent<EnemyFighter>();
        }
        if (animator == null)
        {
            animator = GetComponent<MixamoScanRetargetAnimator>();
        }
        if (weapon == null)
        {
            weapon = GetComponent<GymPoliceWeapon>();
            weapon?.Configure(fighter);
        }
        if (fighter != null)
        {
            fighter.SetPoliceKillerTarget(killerTarget);
            if (handAttackRange <= 0f)
            {
                handAttackRange = fighter.CurrentAttackRange;
            }
        }
    }

    private void Update()
    {
        if (fighter == null || fighter.IsDead)
        {
            return;
        }

        if (fighter.IsReturningToPoliceCar)
        {
            // Revived by Jolly Dog: the intervention is over, gun holstered.
            armed = false;
            drawingStarted = false;
            weapon?.SetVisible(false);
            return;
        }

        if (weapon != null && weapon.IsReady && !weapon.IsVisible)
        {
            // This is sampled every frame before armed state is entered, so
            // the verifier can distinguish a genuinely hidden holstered gun
            // from a weapon that was merely hidden after firing.
            preDrawWeaponHiddenObserved = true;
        }

        Transform target = fighter.CurrentTarget != null
            ? fighter.CurrentTarget : killerTarget;
        if (target == null)
        {
            return;
        }

        PlayerMovement targetPlayer = target.GetComponentInParent<PlayerMovement>();
        EnemyFighter targetFighter = target.GetComponentInParent<EnemyFighter>();
        if (targetPlayer != null && targetPlayer.IsDead)
        {
            if (!playerKillCelebrationSent)
            {
                playerKillCelebrationSent = true;
                fighter.CelebratePlayerKill();
                Debug.Log("GYMCHAOS_POLICEMAN_PLAYER_KILL_CELEBRATION", this);
            }
            return;
        }
        if (targetFighter != null && targetFighter.IsDead)
        {
            killerTarget = null;
            fighter.SetPoliceKillerTarget(null);
            return;
        }

        float healthRatio = fighter.CurrentHealth /
            Mathf.Max(1f, fighter.MaxHealth);
        bool ranged = healthRatio <= 0.5f;
        fighter.SetPoliceRangedMode(ranged);
        if (!ranged)
        {
            // The fighter's normal FixedUpdate path performs the punch combo.
            handToHandObserved = true;
            drawingStarted = false;
            armed = false;
            weapon?.SetVisible(false);
            return;
        }

        if (!rangedModeObserved)
        {
            rangedModeObserved = true;
            Debug.Log(
                $"GYMCHAOS_POLICE_RANGED_MODE range={fighter.CurrentAttackRange:F2} " +
                $"handRange={handAttackRange:F2} multiplier=" +
                $"{(handAttackRange > 0.01f ? fighter.CurrentAttackRange / handAttackRange : 0f):F2}",
                this);
        }

        if (animator == null || weapon == null || !weapon.IsReady)
        {
            return;
        }

        float distance = Vector3.ProjectOnPlane(
            target.position - transform.position, Vector3.up).magnitude;
        if (!armed)
        {
            if (distance > fighter.CurrentAttackRange)
            {
                return;
            }

            if (!drawingStarted)
            {
                drawingStarted = animator.BeginGunDrawing();
                if (drawingStarted)
                {
                    weapon.SetVisible(true);
                    GunDrawingCount++;
                    gunDrawingClipObserved |= animator.HasGunDrawingClip;
                    animator.CancelPunch();
                    Debug.Log(
                        $"GYMCHAOS_POLICEMAN_GUN_DRAWING count={GunDrawingCount} " +
                        $"clip={(animator.HasGunDrawingClip ? "ready" : "missing")}",
                        this);
                }
                return;
            }

            if (!animator.IsGunActionPlaying)
            {
                armed = true;
                weapon.SetVisible(true);
                nextShotTime = Time.time;
                Debug.Log("GYMCHAOS_POLICEMAN_ARMED", this);
            }
            return;
        }

        if (Time.time < nextShotTime || distance > fighter.CurrentAttackRange)
        {
            return;
        }

        bool shotAnimationStarted = animator.BeginGunShooting();
        if (shotAnimationStarted)
        {
            gunShootingClipObserved |= animator.HasGunShootingClip;
            shotAnimationObserved = true;
            if (weapon.FireAt(target))
            {
                GunShotCount++;
                nextShotTime = Time.time + ShotInterval;
                Debug.Log(
                    $"GYMCHAOS_POLICE_SHOT_ANIMATION_OK count={GunShotCount} " +
                    $"clip={(animator.HasGunShootingClip ? "ready" : "missing")}",
                    this);
            }
        }
    }
}
