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

        if (collisionRestoreRoutine != null)
        {
            StopCoroutine(collisionRestoreRoutine);
            collisionRestoreRoutine = null;
        }

        IsHeld = true;
        wasThrown = false;
        transform.rotation = Quaternion.LookRotation(viewForward, Vector3.up);

        body.useGravity = false;
        body.linearDamping = 8f;
        body.angularDamping = 8f;
        body.linearVelocity = Vector3.zero;
        body.angularVelocity = Vector3.zero;

        IgnorePlayerCollisions(playerColliders, true);
        FollowCarryAnchor(anchor.position, anchor.rotation, 99f);
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
    }

    public float GetImpactDamage(float impactSpeed)
    {
        return impactSpeed * impactMultiplier * 2.25f;
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
