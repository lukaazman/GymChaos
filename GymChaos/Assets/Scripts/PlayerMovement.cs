using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

[RequireComponent(typeof(CharacterController))]
public class PlayerMovement : MonoBehaviour
{
    [Header("References")]
    public Camera playerCamera;

    [Header("Movement")]
    public float walkSpeed = 6f;
    public float runSpeed = 11f;
    public float jumpPower = 7f;
    public float gravity = 20f;
    public float lookSpeed = 2f;
    public float lookXLimit = 70f;
    public float defaultHeight = 2f;
    public float crouchHeight = 1.2f;
    public float crouchSpeed = 3f;
    public float groundAcceleration = 70f;
    public float airAcceleration = 28f;
    public float groundFriction = 9f;
    public float airStrafeMultiplier = 1.2f;
    public float maxAirSpeed = 13f;
    public float jumpGraceTime = 0.12f;

    [Header("Combat")]
    public float punchRange = 2.35f;
    public float punchRadius = 0.3f;
    public float punchForce = 15f;
    public float punchDamage = 22f;
    public float punchStun = 0.28f;
    public float shoveForce = 12f;
    public float shoveStun = 0.18f;
    public float heldBarShoveRange = 4.65f;
    public float heldBarShoveRadius = 0.39f;
    public float heldBarShoveForce = 27f;
    public float heldBarShoveDamage = 30f;
    public float heldBarShoveDuration = 0.3f;
    public float heldBarShoveReach = 1.725f;
    public float heldPlateShoveRange = 2.7f;
    public float heldPlateShoveRadius = 0.32f;
    public float heldPlateShoveForce = 16f;
    public float heldPlateShoveDamage = 14f;
    public float heldPlateShoveDuration = 0.22f;
    public float heldPlateShoveReach = 0.95f;
    public float attackCooldown = 0.28f;

    [Header("Interaction")]
    public float interactRange = 5.5f;
    public float carryDistance = 1.45f;
    public float carrySmoothness = 16f;
    public float throwForce = 34f;
    public float upwardThrowForce = 5f;
    public float collisionRestoreDelay = 0.18f;
    public float pickupLookDotThreshold = 0.35f;

    private readonly Collider[] overlapHits = new Collider[64];

    private CharacterController characterController;
    private PlayerHandRig handRig;
    private Vector3 planarVelocity = Vector3.zero;
    private Vector3 impactVelocity = Vector3.zero;
    private float rotationX;
    private float lastAttackTime = -999f;
    private float verticalVelocity;
    private float lastGroundedTime = -999f;
    private float heldBarShoveTimer;
    private Vector3 heldItemShoveDirection = Vector3.zero;
    private bool showCursor;
    private bool useRightHandNext = true;

    private Transform carryAnchor;
    private PickupItem heldItem;
    private Collider[] playerColliders;

    private void Start()
    {
        characterController = GetComponent<CharacterController>();

        if (playerCamera == null)
        {
            playerCamera = GetComponentInChildren<Camera>();
        }

        playerColliders = GetComponentsInChildren<Collider>(true);
        CreateCarryAnchor();
        handRig = PlayerHandRig.Create(playerCamera.transform);
        GymArenaBootstrap.EnsureExists(this);
        LockCursor(true);
    }

    private void Update()
    {
        if (playerCamera == null)
        {
            return;
        }

        HandleCursorToggle();
        HandleLook();
        HandleMovement();
        HandleCombatAndInteraction();
        UpdateHeldItem();
        UpdateHands();
    }

    public void ReceiveImpact(Vector3 impulse)
    {
        impactVelocity += new Vector3(impulse.x, 0f, impulse.z);
        verticalVelocity = Mathf.Max(verticalVelocity, impulse.y * 0.2f);
    }

    private void HandleMovement()
    {
        Vector2 moveInput = ReadMoveInput();
        bool sprintHeld = ReadSprintHeld();
        bool crouchHeld = ReadCrouchHeld();
        bool jumpPressed = ReadJumpPressed();

        bool grounded = characterController.isGrounded;
        if (grounded)
        {
            lastGroundedTime = Time.time;
        }

        float targetSpeed = crouchHeld ? crouchSpeed : (sprintHeld ? runSpeed : walkSpeed);
        Vector3 wishDirection = (transform.forward * moveInput.y) + (transform.right * moveInput.x);
        wishDirection = Vector3.ClampMagnitude(wishDirection, 1f);
        Vector3 desiredVelocity = wishDirection * targetSpeed;

        if (grounded)
        {
            ApplyGroundFriction(moveInput);
            planarVelocity = Vector3.MoveTowards(planarVelocity, desiredVelocity, groundAcceleration * Time.deltaTime);

            if (verticalVelocity < 0f)
            {
                verticalVelocity = -2f;
            }

            if (jumpPressed || (ReadJumpHeld() && Time.time - lastGroundedTime <= jumpGraceTime))
            {
                verticalVelocity = jumpPower;
                grounded = false;
            }
        }
        else
        {
            ApplyAirAcceleration(wishDirection, targetSpeed, moveInput);
        }

        verticalVelocity -= gravity * Time.deltaTime;

        float desiredHeight = crouchHeld ? crouchHeight : defaultHeight;
        characterController.height = Mathf.Lerp(characterController.height, desiredHeight, 12f * Time.deltaTime);

        Vector3 totalMotion = planarVelocity + impactVelocity;
        totalMotion.y = verticalVelocity;
        characterController.Move(totalMotion * Time.deltaTime);
        impactVelocity = Vector3.Lerp(impactVelocity, Vector3.zero, 6f * Time.deltaTime);
    }

    private void ApplyGroundFriction(Vector2 moveInput)
    {
        Vector3 horizontal = Vector3.ProjectOnPlane(planarVelocity, Vector3.up);
        float speed = horizontal.magnitude;
        if (speed <= 0.001f)
        {
            planarVelocity = Vector3.zero;
            return;
        }

        float control = moveInput.sqrMagnitude > 0.01f ? 0.35f : 1f;
        float drop = speed * groundFriction * control * Time.deltaTime;
        float newSpeed = Mathf.Max(speed - drop, 0f);
        if (newSpeed == speed)
        {
            return;
        }

        planarVelocity = horizontal.normalized * newSpeed;
    }

    private void ApplyAirAcceleration(Vector3 wishDirection, float targetSpeed, Vector2 moveInput)
    {
        if (wishDirection.sqrMagnitude <= 0.0001f)
        {
            return;
        }

        float cappedWishSpeed = Mathf.Min(targetSpeed, maxAirSpeed);
        float currentSpeedInWishDir = Vector3.Dot(planarVelocity, wishDirection);
        float addSpeed = cappedWishSpeed - currentSpeedInWishDir;
        if (addSpeed <= 0f)
        {
            return;
        }

        float strafeFactor = Mathf.Abs(moveInput.x) > 0.01f ? airStrafeMultiplier : 1f;
        float accelSpeed = airAcceleration * cappedWishSpeed * strafeFactor * Time.deltaTime;
        accelSpeed = Mathf.Min(accelSpeed, addSpeed);
        planarVelocity += wishDirection * accelSpeed;

        float planarSpeed = planarVelocity.magnitude;
        if (planarSpeed > maxAirSpeed && Vector3.Dot(planarVelocity.normalized, wishDirection) > 0.5f)
        {
            planarVelocity = planarVelocity.normalized * maxAirSpeed;
        }
    }

    private void HandleLook()
    {
        if (showCursor)
        {
            return;
        }

        Vector2 lookInput = ReadLookInput();
        rotationX += -lookInput.y * lookSpeed;
        rotationX = Mathf.Clamp(rotationX, -lookXLimit, lookXLimit);

        playerCamera.transform.localRotation = Quaternion.Euler(rotationX, 0f, 0f);
        transform.rotation *= Quaternion.Euler(0f, lookInput.x * lookSpeed, 0f);
    }

    private void HandleCombatAndInteraction()
    {
        if (ReadInteractPressed())
        {
            if (heldItem != null)
            {
                DropHeldItem();
            }
            else
            {
                TryPickupItem();
            }
        }

        if (ReadAttackPressed() && Time.time >= lastAttackTime + attackCooldown)
        {
            lastAttackTime = Time.time;

            if (heldItem != null)
            {
                ThrowHeldItem();
            }
            else
            {
                PerformPunch();
            }
        }

        if (ReadSecondaryAttackPressed() && Time.time >= lastAttackTime + attackCooldown * 0.65f)
        {
            lastAttackTime = Time.time;
            PerformShove();
        }
    }

    private void PerformPunch()
    {
        if (handRig != null)
        {
            handRig.TriggerPunch(useRightHandNext);
        }

        Vector3 origin = playerCamera.transform.position;
        Vector3 direction = playerCamera.transform.forward;
        if (!Physics.SphereCast(origin, punchRadius, direction, out RaycastHit hit, punchRange, ~0, QueryTriggerInteraction.Ignore))
        {
            return;
        }

        Vector3 impulse = direction * punchForce + Vector3.up * 1.2f;

        EnemyFighter enemy = hit.collider.GetComponentInParent<EnemyFighter>();
        if (enemy != null)
        {
            enemy.TakeMeleeHit(impulse, punchDamage, punchStun);
        }

        PickupItem pickup = hit.collider.GetComponentInParent<PickupItem>();
        if (pickup != null)
        {
            pickup.MarkAsMeleePushed();
            pickup.ApplyImpact(impulse * 0.7f);
        }

        Rigidbody body = hit.rigidbody;
        if (body != null)
        {
            body.AddForceAtPosition(impulse, hit.point, ForceMode.Impulse);
        }

        useRightHandNext = !useRightHandNext;
    }

    private void PerformShove()
    {
        if (handRig != null)
        {
            handRig.TriggerShove();
        }

        if (heldItem != null && (heldItem.ItemType == WeightType.Barbell || heldItem.ItemType == WeightType.EzBar))
        {
            PerformHeldBarShove();
            return;
        }

        if (heldItem != null && IsPlateType(heldItem.ItemType))
        {
            PerformHeldPlateShove();
            return;
        }

        Vector3 origin = transform.position + Vector3.up * (characterController.height * 0.45f);
        int hitCount = Physics.OverlapSphereNonAlloc(origin + transform.forward * 1.05f, 1.1f, overlapHits, ~0, QueryTriggerInteraction.Ignore);
        Vector3 impulse = transform.forward * shoveForce + Vector3.up * 0.75f;

        for (int i = 0; i < hitCount; i++)
        {
            Collider hit = overlapHits[i];
            if (hit == null || hit.transform.IsChildOf(transform))
            {
                continue;
            }

            EnemyFighter enemy = hit.GetComponentInParent<EnemyFighter>();
            if (enemy != null)
            {
                enemy.TakeMeleeHit(impulse, 10f, shoveStun);
            }

            PickupItem pickup = hit.GetComponentInParent<PickupItem>();
            if (pickup != null)
            {
                pickup.MarkAsMeleePushed();
                pickup.ApplyImpact(impulse * 0.65f);
            }

            Rigidbody body = hit.attachedRigidbody;
            if (body != null)
            {
                body.AddForce(impulse, ForceMode.Impulse);
            }
        }
    }

    private void PerformHeldBarShove()
    {
        heldBarShoveTimer = heldBarShoveDuration;
        heldItemShoveDirection = playerCamera.transform.forward;

        Vector3 origin = playerCamera.transform.position;
        Vector3 direction = heldItemShoveDirection;
        if (!Physics.SphereCast(origin, heldBarShoveRadius, direction, out RaycastHit hit, heldBarShoveRange, ~0, QueryTriggerInteraction.Ignore))
        {
            return;
        }

        Vector3 impulse = direction * heldBarShoveForce + Vector3.up * 0.6f;
        EnemyFighter enemy = hit.collider.GetComponentInParent<EnemyFighter>();
        if (enemy != null)
        {
            enemy.TakeMeleeHit(impulse, heldBarShoveDamage, shoveStun);
        }

        PickupItem pickup = hit.collider.GetComponentInParent<PickupItem>();
        if (pickup != null && pickup != heldItem)
        {
            pickup.MarkAsMeleePushed();
            pickup.ApplyImpact(impulse * 0.65f);
        }

        Rigidbody body = hit.rigidbody;
        if (body != null)
        {
            body.AddForceAtPosition(impulse, hit.point, ForceMode.Impulse);
        }
    }

    private void PerformHeldPlateShove()
    {
        heldBarShoveTimer = heldPlateShoveDuration;
        heldItemShoveDirection = GetFlatThrowDirection();

        Vector3 origin = playerCamera.transform.position;
        Vector3 direction = heldItemShoveDirection;
        if (!Physics.SphereCast(origin, heldPlateShoveRadius, direction, out RaycastHit hit, heldPlateShoveRange, ~0, QueryTriggerInteraction.Ignore))
        {
            return;
        }

        Vector3 impulse = direction * heldPlateShoveForce;
        EnemyFighter enemy = hit.collider.GetComponentInParent<EnemyFighter>();
        if (enemy != null)
        {
            enemy.TakeMeleeHit(impulse, heldPlateShoveDamage, shoveStun);
        }

        PickupItem pickup = hit.collider.GetComponentInParent<PickupItem>();
        if (pickup != null && pickup != heldItem)
        {
            pickup.MarkAsMeleePushed();
            pickup.ApplyImpact(impulse * 0.6f);
        }

        Rigidbody body = hit.rigidbody;
        if (body != null)
        {
            body.AddForceAtPosition(impulse, hit.point, ForceMode.Impulse);
        }
    }

    private void TryPickupItem()
    {
        PickupItem candidate = FindBestPickup();
        if (candidate == null)
        {
            return;
        }

        heldItem = candidate;
        heldItem.PickUp(carryAnchor, playerCamera.transform.forward, playerColliders);
        if (handRig != null)
        {
            handRig.SetHolding(true);
        }
    }

    private void DropHeldItem()
    {
        if (heldItem == null)
        {
            return;
        }

        heldItem.Drop(transform.forward * 2f + Vector3.up, playerColliders, collisionRestoreDelay);
        heldItem = null;
        if (handRig != null)
        {
            handRig.SetHolding(false);
        }
    }

    private void ThrowHeldItem()
    {
        if (heldItem == null)
        {
            return;
        }

        if (handRig != null)
        {
            handRig.TriggerThrow(useRightHandNext);
            handRig.SetHolding(false);
        }

        bool isPlateThrow = heldItem.ItemType == WeightType.Plate || heldItem.ItemType == WeightType.Plate5 ||
                            heldItem.ItemType == WeightType.Plate10 || heldItem.ItemType == WeightType.Plate20;
        bool allowSpin = !isPlateThrow;
        Vector3 throwDirection = isPlateThrow ? GetFlatThrowDirection() : (playerCamera.transform.forward + Vector3.up * 0.12f).normalized;
        float scaledThrowForce = throwForce + heldItem.BaseMass * 0.9f;
        Vector3 throwImpulse = isPlateThrow
            ? throwDirection * (scaledThrowForce + 8f)
            : throwDirection * scaledThrowForce + Vector3.up * upwardThrowForce;

        heldItem.Throw(throwImpulse, playerColliders, collisionRestoreDelay, allowSpin);
        heldItem = null;
        useRightHandNext = !useRightHandNext;
    }

    private void UpdateHeldItem()
    {
        if (heldItem == null || carryAnchor == null)
        {
            return;
        }

        if (heldBarShoveTimer > 0f)
        {
            heldBarShoveTimer = Mathf.Max(0f, heldBarShoveTimer - Time.deltaTime);
        }

        float shoveOffset = 0f;
        Quaternion targetRotation = carryAnchor.rotation;
        if (heldBarShoveTimer > 0f && (heldItem.ItemType == WeightType.Barbell || heldItem.ItemType == WeightType.EzBar))
        {
            float normalized = 1f - (heldBarShoveTimer / heldBarShoveDuration);
            shoveOffset = Mathf.Sin(normalized * Mathf.PI) * heldBarShoveReach;
        }
        else if (heldBarShoveTimer > 0f && IsPlateType(heldItem.ItemType))
        {
            float normalized = 1f - (heldBarShoveTimer / heldPlateShoveDuration);
            shoveOffset = Mathf.Sin(normalized * Mathf.PI) * heldPlateShoveReach;
            targetRotation = Quaternion.LookRotation(heldItemShoveDirection.sqrMagnitude > 0.001f ? heldItemShoveDirection : GetFlatThrowDirection(), Vector3.up);
        }

        Vector3 shoveDirection = heldItemShoveDirection.sqrMagnitude > 0.001f ? heldItemShoveDirection : playerCamera.transform.forward;
        Vector3 targetPosition = carryAnchor.position + shoveDirection * shoveOffset;
        heldItem.FollowCarryAnchor(targetPosition, targetRotation, carrySmoothness);
    }

    private void UpdateHands()
    {
        if (handRig == null)
        {
            return;
        }

        Vector3 planarVelocity = new Vector3(characterController.velocity.x, 0f, characterController.velocity.z);
        float moveAmount = Mathf.Clamp01(planarVelocity.magnitude / Mathf.Max(runSpeed, 0.01f));
        handRig.Tick(moveAmount);
        handRig.SetHolding(heldItem != null);
    }

    private Vector3 GetFlatThrowDirection()
    {
        Vector3 flatDirection = Vector3.ProjectOnPlane(playerCamera.transform.forward, Vector3.up).normalized;
        if (flatDirection.sqrMagnitude < 0.001f)
        {
            flatDirection = transform.forward;
        }

        return flatDirection;
    }

    private static bool IsPlateType(WeightType itemType)
    {
        return itemType == WeightType.Plate || itemType == WeightType.Plate5 ||
               itemType == WeightType.Plate10 || itemType == WeightType.Plate20;
    }

    private PickupItem FindBestPickup()
    {
        Vector3 playerCenter = transform.position + Vector3.up * Mathf.Max(characterController.height * 0.45f, 0.9f);
        Vector3 viewOrigin = playerCamera.transform.position;
        Vector3 viewForward = playerCamera.transform.forward;
        PickupItem bestItem = null;
        float bestScore = float.MinValue;

        int hitCount = Physics.OverlapSphereNonAlloc(playerCenter, interactRange, overlapHits, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < hitCount; i++)
        {
            Collider candidateCollider = overlapHits[i];
            if (candidateCollider == null || candidateCollider.transform.IsChildOf(transform))
            {
                continue;
            }

            PickupItem item = candidateCollider.GetComponentInParent<PickupItem>();
            if (item == null || item.IsHeld || !item.IsThrowableWeapon)
            {
                continue;
            }

            Vector3 closestPoint = candidateCollider.ClosestPoint(playerCenter);
            Vector3 toItem = closestPoint - playerCenter;
            float distanceSqr = toItem.sqrMagnitude;
            if (distanceSqr > interactRange * interactRange)
            {
                continue;
            }

            Vector3 toViewPoint = candidateCollider.ClosestPoint(viewOrigin) - viewOrigin;
            if (toViewPoint.sqrMagnitude <= 0.0001f)
            {
                toViewPoint = item.transform.position - viewOrigin;
            }

            float alignment = Vector3.Dot(viewForward, toViewPoint.normalized);
            if (alignment < pickupLookDotThreshold)
            {
                continue;
            }

            float score = alignment * 3f - Mathf.Sqrt(distanceSqr) * 0.4f;
            if (score > bestScore)
            {
                bestScore = score;
                bestItem = item;
            }
        }

        return bestItem;
    }

    private void CreateCarryAnchor()
    {
        Transform existing = transform.Find("CarryAnchor");
        if (existing != null)
        {
            carryAnchor = existing;
            return;
        }

        GameObject anchor = new GameObject("CarryAnchor");
        carryAnchor = anchor.transform;
        carryAnchor.SetParent(playerCamera.transform, false);
        carryAnchor.localPosition = new Vector3(0.34f, -0.28f, carryDistance);
        carryAnchor.localRotation = Quaternion.identity;
    }

    private void HandleCursorToggle()
    {
        if (ReadPauseToggle())
        {
            LockCursor(showCursor);
        }
    }

    private void LockCursor(bool locked)
    {
        showCursor = !locked;
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked;
    }

    private void OnGUI()
    {
        GUI.color = Color.white;
        string heldText = heldItem == null ? "Hands free" : $"Holding: {heldItem.DisplayName}";
        string hud = $"Gym Chaos  |  Opponents: {EnemyFighter.ActiveCount}\n" +
                     $"LMB punch / throw   RMB shove   E pick up / drop   Shift sprint   C crouch   Space jump\n" +
                     $"{heldText}";
        GUI.Label(new Rect(16f, 16f, 940f, 60f), hud);
    }

    private Vector2 ReadMoveInput()
    {
#if ENABLE_INPUT_SYSTEM
        Vector2 move = Vector2.zero;
        if (Keyboard.current != null)
        {
            if (Keyboard.current.wKey.isPressed) move.y += 1f;
            if (Keyboard.current.sKey.isPressed) move.y -= 1f;
            if (Keyboard.current.dKey.isPressed) move.x += 1f;
            if (Keyboard.current.aKey.isPressed) move.x -= 1f;
        }

        return Vector2.ClampMagnitude(move, 1f);
#else
        return new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
#endif
    }

    private Vector2 ReadLookInput()
    {
#if ENABLE_INPUT_SYSTEM
        return Mouse.current != null ? Mouse.current.delta.ReadValue() * 0.02f : Vector2.zero;
#else
        return new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y"));
#endif
    }

    private bool ReadAttackPressed()
    {
#if ENABLE_INPUT_SYSTEM
        return Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
#else
        return Input.GetMouseButtonDown(0);
#endif
    }

    private bool ReadSecondaryAttackPressed()
    {
#if ENABLE_INPUT_SYSTEM
        return Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame;
#else
        return Input.GetMouseButtonDown(1);
#endif
    }

    private bool ReadInteractPressed()
    {
#if ENABLE_INPUT_SYSTEM
        return Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame;
#else
        return Input.GetKeyDown(KeyCode.E);
#endif
    }

    private bool ReadJumpPressed()
    {
#if ENABLE_INPUT_SYSTEM
        return Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame;
#else
        return Input.GetButtonDown("Jump");
#endif
    }

    private bool ReadJumpHeld()
    {
#if ENABLE_INPUT_SYSTEM
        return Keyboard.current != null && Keyboard.current.spaceKey.isPressed;
#else
        return Input.GetButton("Jump");
#endif
    }

    private bool ReadSprintHeld()
    {
#if ENABLE_INPUT_SYSTEM
        return Keyboard.current != null && Keyboard.current.leftShiftKey.isPressed;
#else
        return Input.GetKey(KeyCode.LeftShift);
#endif
    }

    private bool ReadCrouchHeld()
    {
#if ENABLE_INPUT_SYSTEM
        return Keyboard.current != null && Keyboard.current.cKey.isPressed;
#else
        return Input.GetKey(KeyCode.C);
#endif
    }

    private bool ReadPauseToggle()
    {
#if ENABLE_INPUT_SYSTEM
        return Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame;
#else
        return Input.GetKeyDown(KeyCode.Escape);
#endif
    }
}
