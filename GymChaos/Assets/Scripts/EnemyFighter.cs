using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class EnemyFighter : MonoBehaviour
{
    public static int ActiveCount { get; private set; }

    [SerializeField] private float maxHealth = 100f;
    [SerializeField] private float moveForce = 11f;
    [SerializeField] private float maxSpeed = 2.75f;
    [SerializeField] private float attackRange = 1.9f;
    [SerializeField] private float attackImpulse = 9f;
    [SerializeField] private float attackCooldown = 1.05f;
    [SerializeField] private float lightStunDuration = 0.3f;
    [SerializeField] private float heavyStunDuration = 1.15f;
    [SerializeField] private float recoveryDuration = 3.75f;

    private PlayerMovement target;
    private Rigidbody body;
    private float health;
    private float lastAttackTime = -999f;
    private float stunnedUntilTime;
    private float knockedUntilTime;
    private bool isKnockedOut;

    public void SetTarget(PlayerMovement player)
    {
        target = player;
    }

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
        health = maxHealth;
        ActiveCount++;
    }

    private void OnDestroy()
    {
        ActiveCount = Mathf.Max(0, ActiveCount - 1);
    }

    private void FixedUpdate()
    {
        if (target == null)
        {
            target = FindFirstObjectByType<PlayerMovement>();
            if (target == null)
            {
                return;
            }
        }

        if (isKnockedOut)
        {
            if (Time.time >= knockedUntilTime)
            {
                Recover();
            }

            return;
        }

        if (Time.time < stunnedUntilTime)
        {
            return;
        }

        Vector3 toTarget = target.transform.position - transform.position;
        Vector3 planarToTarget = Vector3.ProjectOnPlane(toTarget, Vector3.up);
        float distance = planarToTarget.magnitude;

        if (distance > 0.15f)
        {
            Vector3 moveDir = planarToTarget.normalized;
            Vector3 planarVelocity = Vector3.ProjectOnPlane(body.linearVelocity, Vector3.up);
            if (planarVelocity.magnitude < maxSpeed)
            {
                body.AddForce(moveDir * moveForce, ForceMode.Acceleration);
            }

            Quaternion lookRotation = Quaternion.LookRotation(moveDir, Vector3.up);
            transform.rotation = Quaternion.Slerp(transform.rotation, lookRotation, 10f * Time.fixedDeltaTime);
        }

        if (distance <= attackRange && Time.time >= lastAttackTime + attackCooldown)
        {
            lastAttackTime = Time.time;
            Attack(planarToTarget.sqrMagnitude > 0.01f ? planarToTarget.normalized : transform.forward);
        }
    }

    private void Attack(Vector3 direction)
    {
        body.AddForce(direction * 3.5f + Vector3.up, ForceMode.Impulse);
        target.ReceiveImpact(direction * attackImpulse + Vector3.up * 1.1f);
    }

    public void TakeMeleeHit(Vector3 impulse, float damage, float stunDuration)
    {
        ApplyHit(impulse, damage, stunDuration, false);
    }

    public void TakeThrowableHit(Vector3 impulse, float damage, float stunDuration, bool knockdown)
    {
        ApplyHit(impulse, damage, stunDuration, knockdown);
    }

    private void ApplyHit(Vector3 impulse, float damage, float stunDuration, bool knockdown)
    {
        if (body == null)
        {
            return;
        }

        health -= damage;
        body.AddForce(impulse, ForceMode.Impulse);
        body.AddTorque(Random.onUnitSphere * 6f, ForceMode.Impulse);
        stunnedUntilTime = Mathf.Max(stunnedUntilTime, Time.time + stunDuration);

        if (health <= 0f || knockdown)
        {
            KnockOut(impulse);
        }
    }

    private void KnockOut(Vector3 impulse)
    {
        isKnockedOut = true;
        knockedUntilTime = Time.time + recoveryDuration;
        body.constraints = RigidbodyConstraints.None;
        body.AddForce(impulse * 0.8f + Vector3.up * 2f, ForceMode.Impulse);
    }

    private void Recover()
    {
        isKnockedOut = false;
        health = Mathf.Max(maxHealth * 0.6f, 45f);
        stunnedUntilTime = Time.time + lightStunDuration;
        body.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;

        Vector3 uprightPosition = transform.position;
        uprightPosition.y = Mathf.Max(1f, uprightPosition.y);
        transform.position = uprightPosition;
        transform.rotation = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (collision.rigidbody == null)
        {
            return;
        }

        PickupItem item = collision.rigidbody.GetComponent<PickupItem>();
        if (item == null || !item.IsThrowableWeapon)
        {
            return;
        }

        float impactSpeed = collision.relativeVelocity.magnitude;
        if (impactSpeed < 3f)
        {
            return;
        }

        float damage = item.GetImpactDamage(impactSpeed);
        Vector3 impulse = collision.relativeVelocity.normalized * Mathf.Clamp(impactSpeed * item.ImpactMultiplier, 5f, 28f);
        bool heavyHit = item.WasThrownRecently && impactSpeed > 6f;
        float stunDuration = heavyHit ? heavyStunDuration : lightStunDuration;
        bool knockdown = heavyHit && (item.ItemType == WeightType.Barbell || item.ItemType == WeightType.Plate20 || item.ItemType == WeightType.EzBar);

        TakeThrowableHit(impulse, damage, stunDuration, knockdown);
    }
}
