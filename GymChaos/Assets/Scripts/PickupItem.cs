using System.Collections;
using UnityEngine;

public enum WeightType
{
    None,
    Barbell,
    EzBar,
    Plate,
    Plate5,
    Plate10,
    Plate20
}

[RequireComponent(typeof(Rigidbody))]
public class PickupItem : MonoBehaviour
{
    [SerializeField] private string displayName;
    [SerializeField] private WeightType weightType = WeightType.None;
    [SerializeField] private float baseMass = 5f;
    [SerializeField] private float impactMultiplier = 1f;

    private Rigidbody body;
    private Collider[] itemColliders;
    private Coroutine collisionRestoreRoutine;
    private bool wasThrown;
    private bool thrownImpactSoundPlayed;

    public bool IsHeld { get; private set; }
    public bool IsThrowableWeapon => weightType != WeightType.None;
    public string DisplayName => displayName;
    public WeightType ItemType => weightType;
    public float BaseMass => baseMass;
    public float ImpactMultiplier => impactMultiplier;
    public bool WasThrownRecently => wasThrown;

    public void Configure(Rigidbody targetBody, WeightType type, Collider[] colliders)
    {
        body = targetBody;
        itemColliders = colliders;
        weightType = type;
        displayName = gameObject.name;
        baseMass = GetMassForType(type);
        impactMultiplier = GetImpactMultiplier(type);

        body.mass = baseMass;
        body.linearDamping = 0.35f;
        body.angularDamping = 0.15f;
        body.useGravity = true;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
    }

    private void Awake()
    {
        if (body == null)
        {
            body = GetComponent<Rigidbody>();
        }

        if (itemColliders == null || itemColliders.Length == 0)
        {
            itemColliders = GetComponentsInChildren<Collider>(true);
        }

        if (string.IsNullOrWhiteSpace(displayName))
        {
            displayName = gameObject.name;
        }
    }

    public void PickUp(Transform anchor, Vector3 viewForward, Collider[] playerColliders)
    {
        if (body == null || anchor == null)
        {
            return;
        }

        if (weightType == WeightType.Barbell || weightType == WeightType.EzBar)
        {
            DetachMountedPlateChildren();
        }

        if (collisionRestoreRoutine != null)
        {
            StopCoroutine(collisionRestoreRoutine);
            collisionRestoreRoutine = null;
        }

        IsHeld = true;
        wasThrown = false;
        thrownImpactSoundPlayed = false;
        transform.rotation = Quaternion.LookRotation(viewForward, Vector3.up);

        body.isKinematic = false;
        body.useGravity = false;
        body.linearDamping = 8f;
        body.angularDamping = 8f;
        body.linearVelocity = Vector3.zero;
        body.angularVelocity = Vector3.zero;

        IgnorePlayerCollisions(playerColliders, true);
        FollowCarryAnchor(anchor.position, anchor.rotation, 99f);
    }

    private void DetachMountedPlateChildren()
    {
        PickupItem[] children = GetComponentsInChildren<PickupItem>(true);
        for (int i = 0; i < children.Length; i++)
        {
            PickupItem child = children[i];
            if (child == null || child == this || !IsPlateType(child.weightType) || child.IsHeld)
            {
                continue;
            }

            child.DetachFromMountedParent();
        }
    }

    private void DetachFromMountedParent()
    {
        transform.SetParent(null, true);
        if (body == null)
        {
            return;
        }

        body.isKinematic = false;
        body.useGravity = true;
        body.linearDamping = 0.35f;
        body.angularDamping = 0.15f;
        body.linearVelocity = Vector3.zero;
        body.angularVelocity = Vector3.zero;
        body.WakeUp();
    }

    public void FollowCarryAnchor(Vector3 targetPosition, Quaternion targetRotation, float smoothness)
    {
        if (!IsHeld || body == null)
        {
            return;
        }

        Vector3 nextPosition = Vector3.Lerp(body.position, targetPosition, smoothness * Time.deltaTime);
        Quaternion nextRotation = Quaternion.Slerp(body.rotation, targetRotation, smoothness * Time.deltaTime);
        body.MovePosition(nextPosition);
        body.MoveRotation(nextRotation);
    }

    public void Drop(Vector3 impulse, Collider[] playerColliders, float restoreDelay)
    {
        Release(playerColliders, restoreDelay);
        if (body != null)
        {
            body.AddForce(impulse, ForceMode.Impulse);
        }
    }

    public void Throw(Vector3 impulse, Collider[] playerColliders, float restoreDelay, bool allowSpin)
    {
        wasThrown = true;
        thrownImpactSoundPlayed = false;
        Release(playerColliders, restoreDelay);
        if (body != null)
        {
            body.linearVelocity = Vector3.zero;
            body.AddForce(impulse, ForceMode.VelocityChange);
            body.angularVelocity = Vector3.zero;
            if (allowSpin)
            {
                body.AddTorque(Random.onUnitSphere * (3f + impactMultiplier * 4f), ForceMode.Impulse);
            }
        }
    }

    public void ApplyImpact(Vector3 impulse)
    {
        if (body == null)
        {
            return;
        }

        body.AddForce(impulse, ForceMode.Impulse);
        body.AddTorque(Random.onUnitSphere * Mathf.Max(1f, impactMultiplier * 3f), ForceMode.Impulse);
    }

    public void MarkAsMeleePushed()
    {
        wasThrown = false;
        thrownImpactSoundPlayed = false;
    }

    public bool TryConsumeThrownHit()
    {
        if (!wasThrown)
        {
            return false;
        }
        wasThrown = false;
        return true;
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (!wasThrown || thrownImpactSoundPlayed || !IsThrowableWeapon || collision == null ||
            collision.collider == null)
        {
            return;
        }

        Collider target = collision.collider;
        if (target.GetComponentInParent<GlassShatterPanel>() != null ||
            target.GetComponentInParent<EnemyFighter>() != null ||
            target.GetComponentInParent<PickupItem>() == this)
        {
            // GlassShatterPanel and EnemyFighter own their authenticated hit
            // handling, so this object must not consume their thrown flag first.
            return;
        }

        float impactSpeed = collision.relativeVelocity.magnitude;
        float minimumImpactSpeed = ItemType == WeightType.Barbell || ItemType == WeightType.EzBar
            ? 0.8f
            : 2.5f;
        if (impactSpeed < minimumImpactSpeed)
        {
            return;
        }

        ContactPoint contact = collision.contactCount > 0 ? collision.GetContact(0) : default;
        Vector3 impactPoint = collision.contactCount > 0
            ? contact.point
            : transform.position;
        GymSoundEffect effect = GymAudio.ResolveThrownImpact(this, target);
        if (effect == GymSoundEffect.None)
        {
            return;
        }

        thrownImpactSoundPlayed = true;
        GymAudio.Play(effect, impactPoint, 0.88f);
    }

    public float GetImpactDamage(float impactSpeed)
    {
        _ = impactSpeed;
        bool isPlate = weightType == WeightType.Plate || weightType == WeightType.Plate5 ||
            weightType == WeightType.Plate10 || weightType == WeightType.Plate20;
        if (isPlate)
        {
            return baseMass * 1.25f;
        }
        return weightType == WeightType.Barbell ? 30f : 5f;
    }

    private void Release(Collider[] playerColliders, float restoreDelay)
    {
        if (body == null)
        {
            return;
        }

        IsHeld = false;
        body.useGravity = true;
        body.linearDamping = 0.08f;
        body.angularDamping = 0.08f;

        if (collisionRestoreRoutine != null)
        {
            StopCoroutine(collisionRestoreRoutine);
        }

        collisionRestoreRoutine = StartCoroutine(RestorePlayerCollisionAfterDelay(playerColliders, restoreDelay));
    }

    private IEnumerator RestorePlayerCollisionAfterDelay(Collider[] playerColliders, float delay)
    {
        yield return new WaitForSeconds(delay);
        IgnorePlayerCollisions(playerColliders, false);
        collisionRestoreRoutine = null;
    }

    private void IgnorePlayerCollisions(Collider[] playerColliders, bool ignore)
    {
        if (itemColliders == null || playerColliders == null)
        {
            return;
        }

        for (int i = 0; i < itemColliders.Length; i++)
        {
            Collider itemCollider = itemColliders[i];
            if (itemCollider == null)
            {
                continue;
            }

            for (int j = 0; j < playerColliders.Length; j++)
            {
                Collider playerCollider = playerColliders[j];
                if (playerCollider == null)
                {
                    continue;
                }

                Physics.IgnoreCollision(itemCollider, playerCollider, ignore);
            }
        }
    }

    private static float GetMassForType(WeightType type)
    {
        switch (type)
        {
            case WeightType.Barbell:
                return 18f;
            case WeightType.EzBar:
                return 12f;
            case WeightType.Plate20:
                return 20f;
            case WeightType.Plate10:
                return 10f;
            case WeightType.Plate:
                return 8f;
            case WeightType.Plate5:
                return 5f;
            default:
                return 5f;
        }
    }

    private static bool IsPlateType(WeightType type)
    {
        return type == WeightType.Plate || type == WeightType.Plate5 ||
            type == WeightType.Plate10 || type == WeightType.Plate20;
    }

    private static float GetImpactMultiplier(WeightType type)
    {
        switch (type)
        {
            case WeightType.Barbell:
                return 2.8f;
            case WeightType.EzBar:
                return 2.1f;
            case WeightType.Plate20:
                return 2.4f;
            case WeightType.Plate10:
                return 1.8f;
            case WeightType.Plate:
                return 1.5f;
            case WeightType.Plate5:
                return 1.25f;
            default:
                return 1f;
        }
    }
}
