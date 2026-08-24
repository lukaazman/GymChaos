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
    Plate20,
    Ball,
    FoamRoller,
    PaperTowel,
    StepPlatform,
    YogaMat,
    Radio
}

[RequireComponent(typeof(Rigidbody))]
public class PickupItem : MonoBehaviour
{
    [SerializeField] private string displayName;
    [SerializeField] private WeightType weightType = WeightType.None;
    [SerializeField] private float baseMass = 5f;
    [SerializeField] private float impactMultiplier = 1f;
    [SerializeField] private bool canBePickedUp = true;

    private Rigidbody body;
    private Collider[] itemColliders;
    private Coroutine collisionRestoreRoutine;
    private bool wasThrown;
    private bool thrownImpactSoundPlayed;

    public bool IsHeld { get; private set; }
    public bool IsThrowableWeapon => canBePickedUp && weightType != WeightType.None;
    public string DisplayName => displayName;
    public WeightType ItemType => weightType;
    public float BaseMass => baseMass;
    public float ImpactMultiplier => impactMultiplier;
    public bool WasThrownRecently => wasThrown;
    public bool CanBePickedUp => canBePickedUp;

    public void Configure(Rigidbody targetBody, WeightType type, Collider[] colliders)
    {
        Configure(targetBody, type, colliders, true, null);
    }

    public void Configure(
        Rigidbody targetBody, WeightType type, Collider[] colliders,
        bool pickable, string configuredDisplayName, float massOverride = -1f)
    {
        body = targetBody;
        itemColliders = GetOwnedColliders(colliders);
        weightType = type;
        canBePickedUp = pickable;
        displayName = string.IsNullOrWhiteSpace(configuredDisplayName)
            ? gameObject.name
            : configuredDisplayName;
        baseMass = massOverride > 0f ? massOverride : GetMassForType(type);
        impactMultiplier = GetImpactMultiplier(type);

        body.mass = baseMass;
        body.linearDamping = 0.35f;
        body.angularDamping = 0.15f;
        body.useGravity = true;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
    }

    public void SetMassOverride(float mass)
    {
        if (mass <= 0f)
        {
            return;
        }

        baseMass = mass;
        if (body != null)
        {
            body.mass = mass;
        }
    }

    private void Awake()
    {
        if (body == null)
        {
            body = GetComponent<Rigidbody>();
        }

        if (itemColliders == null || itemColliders.Length == 0)
        {
            itemColliders = GetOwnedColliders(GetComponentsInChildren<Collider>(true));
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

        if (IsPlateType(weightType) &&
            GetComponentInParent<GymMountedWeightMarker>() != null)
        {
            // A mounted plate is its own pickup item. Detach it before
            // carrying so a direct plate pickup cannot remain parented to the
            // bar's rigidbody and so it can slide/fall independently.
            GymMountedWeightMarker mountedMarker =
                GetComponentInParent<GymMountedWeightMarker>();
            PickupItem mountedBar = mountedMarker != null
                ? mountedMarker.GetComponent<PickupItem>()
                : null;
            if (mountedBar != null)
            {
                float remainingMass = Mathf.Max(
                    GetBareMountedMass(mountedBar.weightType),
                    mountedBar.BaseMass - baseMass);
                mountedMarker.SetMassOverride(remainingMass);
                mountedBar.SetMassOverride(remainingMass);
                // The runtime plate collider is a solid disk proxy rather
                // than a mesh with a hole. Once detached, ignore the bar
                // shaft pair so gravity can make the plate slide off instead
                // of trapping it against that proxy collider.
                IgnoreCollisionsWith(mountedBar, true);
            }
            DetachFromMountedParent();
        }
        else if (weightType == WeightType.Barbell || weightType == WeightType.EzBar)
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
        bool deadliftMount = GetComponent<GymMountedWeightMarker>() != null;
        bool detachedAny = false;
        for (int i = 0; i < children.Length; i++)
        {
            PickupItem child = children[i];
            if (child == null || child == this || !IsPlateType(child.weightType) || child.IsHeld)
            {
                continue;
            }

            child.IgnoreCollisionsWith(this, true);
            child.DetachFromMountedParent();
            if (deadliftMount)
            {
                DropMountedPlateToFloor(child);
            }
            detachedAny = true;
        }

        if (detachedAny)
        {
            // The plates are now independent dynamic bodies; the carried bar
            // should no longer keep the full mounted-load mass.
            float bareMass = GetBareMountedMass(weightType);
            GymMountedWeightMarker mountedMarker =
                GetComponent<GymMountedWeightMarker>();
            if (mountedMarker != null)
            {
                mountedMarker.SetMassOverride(bareMass);
            }
            SetMassOverride(bareMass);
            Physics.SyncTransforms();
            Debug.Log(
                $"GYMCHAOS_MOUNTED_BAR_PICKUP loadDetached=true " +
                $"bar={displayName} remainingMass={bareMass:0.##} " +
                $"floorDrop={deadliftMount}", this);
        }
    }

    private void DropMountedPlateToFloor(PickupItem plate)
    {
        if (plate == null || plate.body == null || plate.itemColliders == null ||
            plate.itemColliders.Length == 0)
        {
            return;
        }

        RaycastHit[] hits = Physics.RaycastAll(
            plate.body.position + Vector3.up * 1.25f,
            Vector3.down,
            6f,
            ~0,
            QueryTriggerInteraction.Ignore);
        bool foundFloor = false;
        RaycastHit floorHit = default;
        float nearestDistance = float.MaxValue;
        for (int i = 0; i < hits.Length; i++)
        {
            Collider hitCollider = hits[i].collider;
            if (hitCollider == null)
            {
                continue;
            }

            Transform hitTransform = hitCollider.transform;
            if (hitTransform == plate.transform || hitTransform.IsChildOf(plate.transform) ||
                hitTransform == transform || hitTransform.IsChildOf(transform) ||
                hitTransform.GetComponentInParent<PlayerMovement>() != null ||
                hitCollider.GetComponentInParent<PickupItem>() != null)
            {
                continue;
            }

            if (hits[i].distance < nearestDistance)
            {
                nearestDistance = hits[i].distance;
                floorHit = hits[i];
                foundFloor = true;
            }
        }

        if (foundFloor)
        {
            Bounds occupied = plate.itemColliders[0].bounds;
            for (int i = 1; i < plate.itemColliders.Length; i++)
            {
                if (plate.itemColliders[i] != null)
                {
                    occupied.Encapsulate(plate.itemColliders[i].bounds);
                }
            }

            float lift = floorHit.point.y + 0.01f - occupied.min.y;
            plate.body.position += Vector3.up * lift;
        }

        Vector3 outward = Vector3.ProjectOnPlane(
            plate.body.position - body.position, Vector3.up);
        if (outward.sqrMagnitude > 0.001f)
        {
            outward.Normalize();
            plate.body.linearVelocity = outward * 0.28f + Vector3.down * 0.25f;
            plate.body.angularVelocity =
                Vector3.Cross(Vector3.up, outward) * 2.1f;
        }

        plate.body.WakeUp();
        Physics.SyncTransforms();
    }
    private Collider[] GetOwnedColliders(Collider[] colliders)
    {
        if (colliders == null || colliders.Length == 0)
        {
            return new Collider[0];
        }

        System.Collections.Generic.List<Collider> owned =
            new System.Collections.Generic.List<Collider>(colliders.Length);
        for (int i = 0; i < colliders.Length; i++)
        {
            Collider collider = colliders[i];
            if (collider == null)
            {
                continue;
            }

            PickupItem owner = collider.GetComponentInParent<PickupItem>();
            if (owner != null && owner != this)
            {
                continue;
            }

            if (!owned.Contains(collider))
            {
                owned.Add(collider);
            }
        }

        return owned.ToArray();
    }

    private void IgnoreCollisionsWith(PickupItem other, bool ignore)
    {
        if (other == null || itemColliders == null || other.itemColliders == null)
        {
            return;
        }

        for (int itemIndex = 0; itemIndex < itemColliders.Length; itemIndex++)
        {
            Collider itemCollider = itemColliders[itemIndex];
            if (itemCollider == null)
            {
                continue;
            }

            for (int otherIndex = 0; otherIndex < other.itemColliders.Length; otherIndex++)
            {
                Collider otherCollider = other.itemColliders[otherIndex];
                if (otherCollider != null)
                {
                    Physics.IgnoreCollision(itemCollider, otherCollider, ignore);
                }
            }
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
        body.detectCollisions = true;
        body.linearDamping = 0.35f;
        body.angularDamping = 0.15f;
        body.linearVelocity = Vector3.zero;
        body.angularVelocity = Vector3.zero;
        body.WakeUp();
        Physics.SyncTransforms();
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
        float minimumImpactSpeed = GetMinimumImpactSpeed(ItemType);
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

        switch (weightType)
        {
            case WeightType.Barbell:
                return 30f;
            case WeightType.EzBar:
                return 18f;
            case WeightType.Ball:
                return 7f;
            case WeightType.FoamRoller:
                return 5f;
            case WeightType.PaperTowel:
                return 1.5f;
            case WeightType.Radio:
                return 8f;
            default:
                return 5f;
        }
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
                return 20f;
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
            case WeightType.Ball:
                return 0.5f;
            case WeightType.FoamRoller:
                return 1.4f;
            case WeightType.PaperTowel:
                return 0.25f;
            case WeightType.StepPlatform:
                return 4f;
            case WeightType.YogaMat:
                return 1.6f;
            case WeightType.Radio:
                return 1.8f;
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
            case WeightType.Ball:
                return 0.72f;
            case WeightType.FoamRoller:
                return 0.9f;
            case WeightType.PaperTowel:
                return 0.22f;
            case WeightType.Radio:
                return 1.15f;
            default:
                return 1f;
        }
    }

    private static float GetBareMountedMass(WeightType type)
    {
        switch (type)
        {
            case WeightType.Barbell:
                return 20f;
            case WeightType.EzBar:
                return 12f;
            default:
                return GetMassForType(type);
        }
    }

    private static float GetMinimumImpactSpeed(WeightType type)
    {
        switch (type)
        {
            case WeightType.Barbell:
            case WeightType.EzBar:
                return 0.8f;
            case WeightType.Ball:
                return 1.25f;
            case WeightType.FoamRoller:
                return 1.6f;
            case WeightType.PaperTowel:
                return 1.1f;
            case WeightType.Radio:
                return 1f;
            default:
                return 2.5f;
        }
    }
}
