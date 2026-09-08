using System.Collections.Generic;
using UnityEngine;
#if UNITY_WEBGL && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif
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
    public float punchStun = 0.28f;
    public float shoveForce = 12f;
    public float shoveStun = 0.18f;
    public float heldBarShoveRange = 4.65f;
    public float heldBarShoveRadius = 0.39f;
    public float heldBarShoveForce = 27f;
    public float heldBarShoveDuration = 0.3f;
    public float heldBarShoveReach = 1.725f;
    public float heldPlateShoveRange = 2.7f;
    public float heldPlateShoveRadius = 0.32f;
    public float heldPlateShoveForce = 16f;
    public float heldPlateShoveDuration = 0.22f;
    public float heldPlateShoveReach = 0.95f;
    public float attackCooldown = 0.28f;

    [Header("Health")]
    [SerializeField] private float maxHealth = 200f;
    [SerializeField] private float currentHealth = 200f;

    [Header("Interaction")]
    public float interactRange = 5.5f;
    public float carryDistance = 1.45f;
    public float carrySmoothness = 16f;
    public float throwForce = 34f;
    public float upwardThrowForce = 5f;
    public float collisionRestoreDelay = 0.18f;
    public float pickupLookDotThreshold = 0.35f;

    private readonly Collider[] overlapHits = new Collider[64];
    private readonly Collider[] pickupHits = new Collider[256];
    private readonly HashSet<PickupItem> inspectedPickupItems =
        new HashSet<PickupItem>();
    private readonly List<Collider> pickupColliderScratch =
        new List<Collider>(16);

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
    private float sprintEnergy = 100f;
    private bool showCursor;
    private bool cinematicLock;
    private bool suppressGameplayInputThisFrame;
    private bool useRightHandNext = false;
    private bool useRightThrowNext = true;
    private bool animationSprinting;
    private float crouchAmount;
    private Vector3 cameraBaseLocalPosition;
    private const float PlayerPunchAimLift = 0.16f;
    private const float ContextPromptMinWidth = 220f;
    private const float ContextPromptMaxWidth = 328f;
    private const float ContextPromptHeight = 50f;
    private const float ContextPromptGap = 8f;
    private const float ContextPromptBottomInset = 28f;
    private const float ContextPromptViewportInset = 16f;
    private const float ContextPromptOuterInset = 12f;
    private const float ContextPromptInnerInset = 10f;
    private const float ContextPromptKeyColumnWidth = 54f;
    private const float ContextPromptMessageLeftPadding = 14f;
    private const float ContextPromptMessageRightPadding = 12f;

    private GUIStyle hudShadowStyle;
    private GUIStyle hudTitleStyle;
    private GUIStyle hudBodyStyle;
    private GUIStyle hudMetricStyle;
    private GUIStyle hudHintStyle;
    private GUIStyle hudAccentStyle;
    private GUIStyle hudPromptStyle;
    private GUIStyle hudPromptMessageStyle;

    private Transform carryAnchor;
    private PickupItem heldItem;
    private Collider[] playerColliders;
    private GymExerciseStation nearbyExerciseStation;
    private GymRadio nearbyRadio;
    private PickupItem nearbyPickup;
    private EnemyFighter nearbyTalkTarget;
    private GymBackRoomInteractable nearbyBackRoomInteractable;
    private float nextPickupPromptScanTime;
    private GymExerciseStation activeExerciseStation;
    private GymExerciseStation pendingWeightStation;
    private Vector3 positionBeforeExercise;
    private Quaternion rotationBeforeExercise;
    private Vector3 cameraPositionBeforeExercise;
    private Quaternion cameraRotationBeforeExercise;
    private float cameraFieldOfViewBeforeExercise;
    private bool pullUpMountTransitionActive;
    private GymExerciseStation pullUpMountStation;
    private float pullUpMountElapsed;
    private Vector3 pullUpMountStartPosition;
    private Quaternion pullUpMountStartRotation;
    private Vector3 pullUpMountTargetPosition;
    private Quaternion pullUpMountTargetRotation;
    private Vector3 pullUpMountStartCameraPosition;
    private Quaternion pullUpMountStartCameraRotation;
    private Vector3 pullUpMountTargetCameraPosition;
    private Quaternion pullUpMountTargetCameraRotation;

    private const float PullUpMountDuration = 0.82f;
    private const float PullUpMountJumpHeight = 0.38f;

#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")]
    private static extern int GymChaosDocumentHasFocus();

    [DllImport("__Internal")]
    private static extern void GymChaosInstallPointerLock();

    [DllImport("__Internal")]
    private static extern void GymChaosExitPointerLock();

    [DllImport("__Internal")]
    private static extern int GymChaosIsPointerLocked();
#endif

    public bool IsExercising => activeExerciseStation != null ||
        pendingWeightStation != null || pullUpMountTransitionActive;
    public float MaxHealth => maxHealth;
    public float CurrentHealth => currentHealth;
    public float MissingHealth01 => 1f - Mathf.Clamp01(currentHealth / Mathf.Max(1f, maxHealth));
    public float SprintEnergy => sprintEnergy;
    public bool IsDead { get; private set; }
    public bool IsCinematicLocked => cinematicLock;
    public bool CursorCaptured => IsCursorCaptured;
    public Vector3 StandingCameraLocalPosition => cameraBaseLocalPosition;
    public bool CanShowCursorRecapturePrompt
    {
        get
        {
            GymExperienceService progression = GymExperienceService.Active;
            return GymArenaBootstrap.IsGameplayStarted &&
                !IsDead && !cinematicLock && !IsExercising &&
                !GymStartScreen.IsMenuVisible && !GymPauseMenu.IsVisible &&
                !GymRadioSoundCloudPopup.IsAnyVisible &&
                !GymDialogueDirector.IsDialogueActive &&
                (progression == null || !progression.IsBlockingPlayerInput) &&
                !IsCursorCaptured;
        }
    }

    public void SetCinematicLock(bool locked)
    {
        cinematicLock = locked;
        if (locked)
        {
            planarVelocity = Vector3.zero;
            impactVelocity = Vector3.zero;
            verticalVelocity = 0f;
        }
    }

    public void SetCursorCaptured(bool captured)
    {
        LockCursor(captured);
    }

    public void SetCinematicPose(
        Vector3 worldPosition, Quaternion worldRotation,
        Vector3 cameraLocalPosition, Quaternion cameraLocalRotation)
    {
        if (playerCamera == null)
        {
            return;
        }

        if (characterController != null)
        {
            characterController.enabled = false;
        }

        transform.SetPositionAndRotation(worldPosition, worldRotation);
        playerCamera.transform.localPosition = cameraLocalPosition;
        playerCamera.transform.localRotation = cameraLocalRotation;
        rotationX = NormalizeCameraPitch(cameraLocalRotation.eulerAngles.x);
        planarVelocity = Vector3.zero;
        impactVelocity = Vector3.zero;
        verticalVelocity = 0f;
    }

    public void RestoreCinematicPose(
        Vector3 worldPosition, Quaternion worldRotation,
        Vector3 cameraLocalPosition, Quaternion cameraLocalRotation)
    {
        SetCinematicPose(worldPosition, worldRotation, cameraLocalPosition, cameraLocalRotation);
        if (characterController != null)
        {
            characterController.enabled = true;
        }
    }

    private static float NormalizeCameraPitch(float angle)
    {
        if (angle > 180f)
        {
            angle -= 360f;
        }

        return angle;
    }

    public void CaptureCursorForGameplay()
    {
        if (playerCamera == null || IsDead)
        {
            return;
        }

        // PLAY is already a trusted user gesture. Capture the cursor here so
        // the first gameplay frame is immediately ready for mouse look, while
        // suppressing the same click from becoming a punch or shove.
        suppressGameplayInputThisFrame = true;
#if UNITY_WEBGL && !UNITY_EDITOR
        // The WebGL canvas mousedown handler requests pointer lock directly
        // from the browser gesture before Unity invokes this callback.
        bool webGlCaptured = IsCursorCaptured;
        Cursor.visible = !webGlCaptured;
        showCursor = !webGlCaptured;
#else
        LockCursor(true);
#endif
        Debug.Log("GYMCHAOS_CURSOR_CAPTURED_AFTER_PLAY", this);
    }

    private void Start()
    {
        maxHealth = 200f;
        currentHealth = maxHealth;
        IsDead = false;
        sprintEnergy = 100f;
        characterController = GetComponent<CharacterController>();

        if (playerCamera == null)
        {
            playerCamera = GetComponentInChildren<Camera>();
        }

        if (playerCamera != null)
        {
            cameraBaseLocalPosition = playerCamera.transform.localPosition;
            // The player model and enemies are both about 2.30 m tall. With
            // the player root at the CharacterController centre, this places
            // the first-person camera at the average enemy eye line instead
            // of looking down from the old 2.75 m player height.
            cameraBaseLocalPosition.y = characterController.height * 0.55f;
            playerCamera.transform.localPosition = cameraBaseLocalPosition;
        }

        playerColliders = GetComponentsInChildren<Collider>(true);
        CreateCarryAnchor();
        handRig = PlayerHandRig.Create(playerCamera.transform);
        GymArenaBootstrap.EnsureExists(this);
#if UNITY_WEBGL && !UNITY_EDITOR
        // Install the request handler before the first click. The browser
        // must receive requestPointerLock directly from that DOM gesture;
        // Unity's deferred Cursor.lockState call is too late for some pages.
        GymChaosInstallPointerLock();
#endif
        // Browsers only permit pointer lock from a focused user gesture.
        // Do not request it during WebGL startup; TryCaptureCursor() retries
        // the lock from the first real click on the game canvas instead.
        if (!GymArenaBootstrap.IsGameplayStarted)
        {
            LockCursor(false);
        }
        else if (Application.platform == RuntimePlatform.WebGLPlayer)
        {
            LockCursor(false);
        }
        else
        {
            LockCursor(true);
        }
    }

    private void Update()
    {
        if (playerCamera == null)
        {
            return;
        }

        suppressGameplayInputThisFrame = false;

        if (GymDialogueDirector.IsDialogueActive)
        {
            GymDialogueDirector.TickActiveInput(
                this,
                ReadExerciseActionPressed(),
                ReadPauseToggle());
            return;
        }

        if (GymPauseMenu.IsVisible)
        {
            GymPauseMenu.HandlePauseInput();
            return;
        }

        if (GymRadioSoundCloudPopup.IsAnyVisible)
        {
            return;
        }

        if (GymExperienceService.Active != null &&
            GymExperienceService.Active.IsBlockingPlayerInput)
        {
            return;
        }

        if (cinematicLock)
        {
            return;
        }

        // Browsers reject the initial lock request because Start is not a
        // user gesture. Wait for a real click, then retry the same request so
        // the deployed build behaves like the editor without stealing the
        // click as an attack or shove.
        if (!IsDead && pendingWeightStation == null && TryCaptureCursor())
        {
            return;
        }

        if (IsDead)
        {
            HandleCursorToggle();
            return;
        }

        if (pendingWeightStation != null)
        {
            HandleWeightSelectionMode();
            return;
        }

        HandleCursorToggle();

        if (showCursor)
        {
            return;
        }

        if (pullUpMountTransitionActive)
        {
            HandlePullUpMountTransition();
            return;
        }

        if (activeExerciseStation != null)
        {
            HandleExerciseMode();
            return;
        }

        nearbyExerciseStation = GymExerciseStation.FindClosest(transform.position, 3.15f);
        nearbyRadio = GymRadio.FindClosest(transform.position, 3.1f);
        if (Time.unscaledTime >= nextPickupPromptScanTime)
        {
            nearbyPickup = heldItem == null ? FindBestPickup() : null;
            nearbyTalkTarget = GymDialogueDirector.Active != null
                ? GymDialogueDirector.Active.FindNearbyTalkTarget(transform.position)
                : null;
            nearbyBackRoomInteractable = GymExperienceService.Active != null
                ? GymExperienceService.Active.FindNearbyInteractable(transform.position, 3.1f)
                : null;
            nextPickupPromptScanTime = Time.unscaledTime + 0.12f;
        }
        if (GymDialogueDirector.TryStartNearby(this, ReadInteractPressed()))
        {
            return;
        }
        if (GymExperienceService.TryHandlePlayerInteraction(this, ReadInteractPressed()))
        {
            return;
        }
        if (nearbyRadio != null && ReadRadioTogglePressed())
        {
            nearbyRadio.ToggleMusic();
            return;
        }

        if (nearbyExerciseStation != null && ReadExerciseStartPressed())
        {
            if (nearbyExerciseStation.RequiresWeightSelection)
            {
                OpenWeightSelection(nearbyExerciseStation);
            }
            else
            {
                BeginExercise(nearbyExerciseStation);
            }
            return;
        }

        HandleLook();
        HandleMovement();
        HandleCombatAndInteraction();
        UpdateHeldItem();
        UpdateHands();
    }

    public void ReceiveImpact(Vector3 impulse)
    {
        if (IsDead || IsExercising)
        {
            return;
        }

        impactVelocity += new Vector3(impulse.x, 0f, impulse.z);
        verticalVelocity = Mathf.Max(verticalVelocity, impulse.y * 0.2f);
    }

    public void ReceiveEnemyPunch(float damage, Vector3 impulse, EnemyFighter attacker)
    {
        if (IsDead || damage <= 0f)
        {
            return;
        }

        currentHealth = Mathf.Clamp(currentHealth - damage, 0f, maxHealth);
        ReceiveImpact(impulse);
        if (currentHealth <= 0f)
        {
            Die(attacker);
        }
    }

    private void OnControllerColliderHit(ControllerColliderHit hit)
    {
        Rigidbody body = hit.rigidbody;
        if (body == null || body.isKinematic)
        {
            return;
        }

        PickupItem item = body.GetComponent<PickupItem>();
        if (item == null || item.ItemType != WeightType.Ball)
        {
            return;
        }

        Vector3 pushDirection = Vector3.ProjectOnPlane(hit.moveDirection, Vector3.up);
        if (pushDirection.sqrMagnitude < 0.01f)
        {
            pushDirection = Vector3.ProjectOnPlane(planarVelocity, Vector3.up);
        }
        if (pushDirection.sqrMagnitude < 0.01f)
        {
            return;
        }

        float speed = Mathf.Max(planarVelocity.magnitude,
            new Vector2(hit.moveDirection.x, hit.moveDirection.z).magnitude);
        float impulse = Mathf.Lerp(0.45f, 2.2f,
            Mathf.InverseLerp(0f, runSpeed, speed));
        body.WakeUp();
        body.AddForceAtPosition(pushDirection.normalized * impulse,
            hit.point, ForceMode.Impulse);
    }

#if UNITY_EDITOR
    public void ResetMovementForVerification()
    {
        planarVelocity = Vector3.zero;
        impactVelocity = Vector3.zero;
        verticalVelocity = 0f;
    }
#endif

    private void Die(EnemyFighter attacker)
    {
        if (IsDead)
        {
            return;
        }

        IsDead = true;
        currentHealth = 0f;
        planarVelocity = Vector3.zero;
        impactVelocity = Vector3.zero;
        verticalVelocity = 0f;
        nearbyExerciseStation = null;
        pendingWeightStation = null;
        if (activeExerciseStation != null || pullUpMountTransitionActive)
        {
            EndExercise();
        }
        LockCursor(false);
        // Player death is a global end-of-round state: every living combat
        // enemy enters its looping Celebration clip, not only the attacker.
        EnemyFighter.CelebrateAllLivingEnemies();
    }

    private void HandleMovement()
    {
        Vector2 moveInput = ReadMoveInput();
        bool sprintHeld = ReadSprintHeld();
        bool crouchHeld = ReadCrouchHeld();
        crouchAmount = Mathf.MoveTowards(crouchAmount, crouchHeld ? 1f : 0f, 10f * Time.deltaTime);
        bool jumpPressed = ReadJumpPressed();

        GymExperienceService progression = GymExperienceService.Active;
        float sprintCapacity = progression != null
            ? progression.GetSprintCapacity()
            : 100f;
        sprintEnergy = Mathf.Clamp(sprintEnergy, 0f, sprintCapacity);
        bool sprinting = sprintHeld && !crouchHeld && sprintEnergy > 0.01f;
        animationSprinting = sprinting;
        if (sprinting && moveInput.sqrMagnitude > 0.01f)
        {
            float drain = progression != null
                ? progression.GetSprintDrainPerSecond()
                : 18f;
            sprintEnergy = Mathf.Max(0f, sprintEnergy - drain * Time.deltaTime);
        }
        else
        {
            float recovery = progression != null
                ? progression.GetSprintRecoveryPerSecond()
                : 14f;
            sprintEnergy = Mathf.Min(
                sprintCapacity, sprintEnergy + recovery * Time.deltaTime);
        }

        bool grounded = characterController.isGrounded;
        if (grounded)
        {
            lastGroundedTime = Time.time;
        }

        float runMultiplier = sprinting && progression != null
            ? progression.GetSprintMultiplier()
            : 1f;
        float targetSpeed = crouchHeld ? crouchSpeed :
            (sprinting ? runSpeed * runMultiplier : walkSpeed);
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

        Vector3 cameraPosition = playerCamera.transform.localPosition;
        cameraPosition.y = Mathf.Lerp(
            cameraPosition.y, cameraBaseLocalPosition.y - crouchAmount * 0.34f, 12f * Time.deltaTime);
        playerCamera.transform.localPosition = cameraPosition;

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
        if (showCursor || !IsCursorCaptured)
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
        if (suppressGameplayInputThisFrame)
        {
            return;
        }

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
        Vector3 punchSoundPosition = transform.position +
            Vector3.up * (characterController.height * 0.58f) +
            transform.forward * 0.42f;
        GymAudio.Play(GymSoundEffect.PunchAction, punchSoundPosition, 0.58f);

        if (handRig != null)
        {
            handRig.TriggerPunch(useRightHandNext);
            useRightHandNext = !useRightHandNext;
        }

        Vector3 origin = playerCamera.transform.position;
        Vector3 aimPoint = origin + playerCamera.transform.forward * punchRange +
                           playerCamera.transform.up * PlayerPunchAimLift;
        Vector3 direction = (aimPoint - origin).normalized;
        if (!Physics.SphereCast(origin, punchRadius, direction, out RaycastHit hit, punchRange, ~0, QueryTriggerInteraction.Collide))
        {
            return;
        }

        GymExperienceService progression = GymExperienceService.Active;
        float strengthForce = progression != null
            ? progression.GetStrengthForceMultiplier()
            : 1f;
        Vector3 impulse = direction * punchForce * strengthForce +
            Vector3.up * (1.2f * strengthForce);

        EnemyFighter enemy = hit.collider.GetComponentInParent<EnemyFighter>();
        if (!IsTightEnemySurface(enemy, hit.collider))
        {
            enemy = null;
        }
        if (enemy != null)
        {
            GymAudio.Play(GymSoundEffect.PunchFeedback, hit.point, 1f);
            bool onePunch = progression != null &&
                progression.StrengthUltimateApplies(enemy);
            float damage = onePunch ? enemy.CurrentHealth :
                (progression != null
                    ? progression.ScaleStrengthDamage(5f)
                    : 5f);
            enemy.TakeMeleeHit(impulse, damage, punchStun);
            progression?.RegisterCombatHit(enemy);
            if (enemy.IsDead)
            {
                progression?.RegisterEnemyDefeat(enemy);
            }
            BloodSplatter.SpawnOnBody(enemy, hit.point, hit.normal, 0.82f, hit.collider.transform);
        }

        GymExperienceService service = progression;
        GlassShatterPanel unarmedPanel = hit.collider.GetComponentInParent<GlassShatterPanel>();
        if (service != null && service.HasStrengthUltimate && unarmedPanel != null &&
            unarmedPanel.ShatterFromPowerImpact(hit.point, hit.normal, impulse))
        {
            Debug.Log("GYMCHAOS_STRENGTH_MIRROR_BREAK", unarmedPanel);
        }

        PickupItem pickup = hit.collider.GetComponentInParent<PickupItem>();
        if (pickup != null)
        {
            pickup.MarkAsMeleePushed();
            pickup.ApplyImpact(impulse * 0.7f);
        }

        Rigidbody body = hit.rigidbody;
        if (body != null && enemy == null)
        {
            body.AddForceAtPosition(impulse, hit.point, ForceMode.Impulse);
        }

    }

    private void PerformShove()
    {
        if (heldItem != null && (heldItem.ItemType == WeightType.Barbell ||
            heldItem.ItemType == WeightType.EzBar || heldItem.ItemType == WeightType.Radio))
        {
            handRig?.TriggerHeldShove(GetHeldShoveVisualReach(), heldBarShoveDuration);
            PerformHeldBarShove();
            return;
        }

        if (heldItem != null && IsPlateType(heldItem.ItemType))
        {
            handRig?.TriggerHeldShove(GetHeldShoveVisualReach(), heldPlateShoveDuration);
            PerformHeldPlateShove();
            return;
        }

        handRig?.TriggerShove();

        Vector3 origin = transform.position + Vector3.up * (characterController.height * 0.45f);
        int hitCount = Physics.OverlapSphereNonAlloc(origin + transform.forward * 1.05f, 1.1f, overlapHits, ~0, QueryTriggerInteraction.Collide);
        GymExperienceService progression = GymExperienceService.Active;
        float strengthForce = progression != null
            ? progression.GetStrengthForceMultiplier()
            : 1f;
        Vector3 impulse = transform.forward * shoveForce * strengthForce +
            Vector3.up * (0.75f * strengthForce);
        HashSet<EnemyFighter> damagedFighters = new HashSet<EnemyFighter>();

        for (int i = 0; i < hitCount; i++)
        {
            Collider hit = overlapHits[i];
            if (hit == null || hit.transform.IsChildOf(transform))
            {
                continue;
            }

            EnemyFighter enemy = hit.GetComponentInParent<EnemyFighter>();
            if (!IsTightEnemySurface(enemy, hit))
            {
                enemy = null;
            }
            if (enemy != null && damagedFighters.Add(enemy))
            {
                float damage = GymExperienceService.Active != null
                    ? GymExperienceService.Active.ScaleStrengthDamage(2f)
                    : 2f;
                enemy.TakeMeleeHit(impulse, damage, shoveStun);
                GymExperienceService.Active?.RegisterCombatHit(enemy);
                if (enemy.IsDead)
                {
                    GymExperienceService.Active?.RegisterEnemyDefeat(enemy);
                }
            }

            PickupItem pickup = hit.GetComponentInParent<PickupItem>();
            if (pickup != null)
            {
                pickup.MarkAsMeleePushed();
                pickup.ApplyImpact(impulse * 0.65f);
            }

            Rigidbody body = hit.attachedRigidbody;
            if (body != null && enemy == null)
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
        if (!Physics.SphereCast(origin, heldBarShoveRadius, direction, out RaycastHit hit, heldBarShoveRange, ~0, QueryTriggerInteraction.Collide))
        {
            return;
        }

        GymExperienceService progression = GymExperienceService.Active;
        float strengthForce = progression != null
            ? progression.GetStrengthForceMultiplier()
            : 1f;
        Vector3 impulse = direction * heldBarShoveForce * strengthForce +
            Vector3.up * (0.6f * strengthForce);
        EnemyFighter enemy = hit.collider.GetComponentInParent<EnemyFighter>();
        if (!IsTightEnemySurface(enemy, hit.collider))
        {
            enemy = null;
        }
        if (enemy != null)
        {
            float baseDamage = heldItem.ItemType == WeightType.Barbell
                ? 15f
                : heldItem.ItemType == WeightType.Radio
                    ? heldItem.GetImpactDamage(heldBarShoveForce) * 0.7f
                    : 5f;
            float damage = progression != null
                ? progression.ScaleStrengthDamage(baseDamage)
                : baseDamage;
            enemy.TakeMeleeHit(impulse, damage, shoveStun);
            progression?.RegisterCombatHit(enemy);
            if (enemy.IsDead)
            {
                progression?.RegisterEnemyDefeat(enemy);
            }
            BloodSplatter.SpawnOnBody(
                enemy, hit.point, hit.normal,
                BloodSplatter.GetHeldShoveScale(heldItem.ItemType, heldItem.BaseMass),
                hit.collider.transform);
        }

        PickupItem pickup = hit.collider.GetComponentInParent<PickupItem>();
        if (pickup != null && pickup != heldItem)
        {
            pickup.MarkAsMeleePushed();
            pickup.ApplyImpact(impulse * 0.65f);
        }

        TryShatterGlassFromHeldImpact(hit, impulse);

        Rigidbody body = hit.rigidbody;
        if (body != null && enemy == null)
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
        if (!Physics.SphereCast(origin, heldPlateShoveRadius, direction, out RaycastHit hit, heldPlateShoveRange, ~0, QueryTriggerInteraction.Collide))
        {
            return;
        }

        GymExperienceService progression = GymExperienceService.Active;
        float strengthForce = progression != null
            ? progression.GetStrengthForceMultiplier()
            : 1f;
        Vector3 impulse = direction * heldPlateShoveForce * strengthForce;
        EnemyFighter enemy = hit.collider.GetComponentInParent<EnemyFighter>();
        if (!IsTightEnemySurface(enemy, hit.collider))
        {
            enemy = null;
        }
        if (enemy != null)
        {
            float damage = progression != null
                ? progression.ScaleStrengthDamage(heldItem.BaseMass * 0.75f)
                : heldItem.BaseMass * 0.75f;
            enemy.TakeMeleeHit(impulse, damage, shoveStun);
            progression?.RegisterCombatHit(enemy);
            if (enemy.IsDead)
            {
                progression?.RegisterEnemyDefeat(enemy);
            }
            BloodSplatter.SpawnOnBody(
                enemy, hit.point, hit.normal,
                BloodSplatter.GetHeldShoveScale(heldItem.ItemType, heldItem.BaseMass),
                hit.collider.transform);
        }

        PickupItem pickup = hit.collider.GetComponentInParent<PickupItem>();
        if (pickup != null && pickup != heldItem)
        {
            pickup.MarkAsMeleePushed();
            pickup.ApplyImpact(impulse * 0.6f);
        }

        TryShatterGlassFromHeldImpact(hit, impulse);

        Rigidbody body = hit.rigidbody;
        if (body != null && enemy == null)
        {
            body.AddForceAtPosition(impulse, hit.point, ForceMode.Impulse);
        }
    }

    private void TryShatterGlassFromHeldImpact(RaycastHit hit, Vector3 impulse)
    {
        if (heldItem == null || !heldItem.IsThrowableWeapon || hit.collider == null)
        {
            return;
        }

        GlassShatterPanel panel = hit.collider.GetComponentInParent<GlassShatterPanel>();
        panel?.ShatterFromPlayerImpact(heldItem, hit.point, hit.normal, impulse);
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
        bool isPlateThrow = IsPlateType(heldItem.ItemType);

        if (handRig != null)
        {
            handRig.TriggerThrow(useRightThrowNext, isPlateThrow);
            handRig.SetHolding(false);
        }

        bool allowSpin = !isPlateThrow;
        Vector3 throwDirection = isPlateThrow ? GetFlatThrowDirection() : (playerCamera.transform.forward + Vector3.up * 0.12f).normalized;
        GymExperienceService progression = GymExperienceService.Active;
        float strengthForce = progression != null
            ? progression.GetStrengthForceMultiplier()
            : 1f;
        float scaledThrowForce = (throwForce + heldItem.BaseMass * 0.9f) * strengthForce;
        Vector3 throwImpulse = isPlateThrow
            ? GetPlateThrowImpulse(throwDirection, heldItem.BaseMass)
            : throwDirection * scaledThrowForce +
              Vector3.up * (upwardThrowForce * strengthForce);

        heldItem.Throw(throwImpulse, playerColliders, collisionRestoreDelay, allowSpin);
        heldItem = null;
        useRightThrowNext = !useRightThrowNext;
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
        if (heldBarShoveTimer > 0f && (heldItem.ItemType == WeightType.Barbell ||
            heldItem.ItemType == WeightType.EzBar || heldItem.ItemType == WeightType.Radio))
        {
            float normalized = 1f - (heldBarShoveTimer / heldBarShoveDuration);
            shoveOffset = Mathf.Sin(normalized * Mathf.PI) * GetHeldShoveVisualReach();
        }
        else if (heldBarShoveTimer > 0f && IsPlateType(heldItem.ItemType))
        {
            float normalized = 1f - (heldBarShoveTimer / heldPlateShoveDuration);
            shoveOffset = Mathf.Sin(normalized * Mathf.PI) * GetHeldShoveVisualReach();
            targetRotation = Quaternion.LookRotation(heldItemShoveDirection.sqrMagnitude > 0.001f ? heldItemShoveDirection : GetFlatThrowDirection(), Vector3.up);
        }

        Vector3 shoveDirection = heldItemShoveDirection.sqrMagnitude > 0.001f ? heldItemShoveDirection : playerCamera.transform.forward;
        Vector3 targetPosition = carryAnchor.position + shoveDirection * shoveOffset;
        heldItem.FollowCarryAnchor(targetPosition, targetRotation, carrySmoothness);
    }

    private float GetHeldShoveVisualReach()
    {
        if (heldItem == null)
        {
            return 0f;
        }
        return IsPlateType(heldItem.ItemType)
            ? Mathf.Min(heldPlateShoveReach, 0.58f)
            : Mathf.Min(heldBarShoveReach, 0.72f);
    }

    private void UpdateHands()
    {
        if (handRig == null)
        {
            return;
        }

        Vector3 planarVelocity = new Vector3(characterController.velocity.x, 0f, characterController.velocity.z);
        float moveAmount = Mathf.Clamp01(planarVelocity.magnitude / Mathf.Max(runSpeed, 0.01f));
        handRig.Tick(moveAmount, crouchAmount, animationSprinting, characterController.isGrounded);
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

    private static bool IsTightEnemySurface(EnemyFighter enemy, Collider hitCollider)
    {
        if (enemy == null || hitCollider == null)
        {
            return false;
        }

        if (!hitCollider.enabled || hitCollider.isTrigger)
        {
            return false;
        }

        EnemyMeshHitboxRig hitboxRig = enemy.GetComponent<EnemyMeshHitboxRig>();
        if (hitboxRig != null)
        {
            // Once the animated rig exists, only its moving body-part
            // colliders are valid combat surfaces. The broad root capsule is
            // deliberately disabled and must never become a hidden damage
            // volume during external-model loading or animation handoff.
            return hitboxRig.IsTightCombatSurface(hitCollider);
        }

        // Keep the temporary fallback for enemies that have not produced an
        // animated rig yet, but never accept a collider on an unrelated
        // hierarchy or a disabled root.
        return hitCollider.transform != enemy.transform &&
               hitCollider.transform.IsChildOf(enemy.transform);
    }

    private Vector3 GetPlateThrowImpulse(Vector3 direction, float plateMass)
    {
        // Preserve the previous Plate5 throw exactly, then make heavier plates
        // travel only slightly less far and begin dropping a little sooner.
        float mass01 = Mathf.InverseLerp(5f, 20f, plateMass);
        float distanceScale = Mathf.Lerp(1f, 0.90f, mass01);
        float downwardSpeed = Mathf.Lerp(0f, 0.65f, mass01);
        float strengthForce = GymExperienceService.Active != null
            ? GymExperienceService.Active.GetStrengthForceMultiplier()
            : 1f;
        float plate5Speed = (throwForce + 5f * 0.9f + 8f) * strengthForce;
        return direction * (plate5Speed * distanceScale) + Vector3.down * downwardSpeed;
    }

    private PickupItem FindBestPickup()
    {
        Vector3 playerCenter = transform.position + Vector3.up * Mathf.Max(characterController.height * 0.45f, 0.9f);
        Vector3 viewOrigin = playerCamera.transform.position;
        Vector3 viewForward = playerCamera.transform.forward;
        PickupItem bestItem = null;
        float bestScore = float.MinValue;
        inspectedPickupItems.Clear();

        int hitCount = Physics.OverlapSphereNonAlloc(playerCenter, interactRange, pickupHits, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < hitCount; i++)
        {
            Collider candidateCollider = pickupHits[i];
            if (candidateCollider == null || candidateCollider.transform.IsChildOf(transform))
            {
                continue;
            }

            PickupItem item = candidateCollider.GetComponentInParent<PickupItem>();
            item = ResolveMountedPickup(item);
            if (item == null || item.IsHeld || !item.IsThrowableWeapon ||
                !inspectedPickupItems.Add(item))
            {
                continue;
            }

            Vector3 closestPoint = GetClosestPickupPoint(item, playerCenter);
            Vector3 toItem = closestPoint - playerCenter;
            float distanceSqr = toItem.sqrMagnitude;
            if (distanceSqr > interactRange * interactRange)
            {
                continue;
            }

            Vector3 toViewPoint = GetClosestPickupPoint(item, viewOrigin) - viewOrigin;
            if (toViewPoint.sqrMagnitude <= 0.0001f)
            {
                toViewPoint = item.transform.position - viewOrigin;
            }

            float alignment = Vector3.Dot(viewForward, toViewPoint.normalized);
            bool weightStandPlate = IsWeightStandPlate(item);
            float requiredAlignment = weightStandPlate
                ? Mathf.Min(0.08f, pickupLookDotThreshold)
                : pickupLookDotThreshold;
            if (alignment < requiredAlignment)
            {
                continue;
            }

            // Proximity alone must never select props through locker-room or
            // gym walls. Only the candidate hierarchy may be first blocker.
            if (!HasPickupLineOfSight(item, viewOrigin, toViewPoint))
            {
                continue;
            }

            float score = alignment * (weightStandPlate ? 2f : 3f) -
                Mathf.Sqrt(distanceSqr) * (weightStandPlate ? 0.65f : 0.4f);
            if (score > bestScore)
            {
                bestScore = score;
                bestItem = item;
            }
        }

        return bestItem;
    }

    private bool HasPickupLineOfSight(
        PickupItem item, Vector3 origin, Vector3 directionToItem)
    {
        float distance = directionToItem.magnitude;
        if (item == null || distance <= 0.001f)
        {
            return item != null;
        }

        RaycastHit[] hits = Physics.RaycastAll(
            origin, directionToItem / distance, distance + 0.06f,
            ~0, QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (left, right) =>
            left.distance.CompareTo(right.distance));
        for (int i = 0; i < hits.Length; i++)
        {
            Collider hit = hits[i].collider;
            if (hit == null || hit.transform.IsChildOf(transform))
            {
                continue;
            }
            PickupItem hitItem = hit.GetComponentInParent<PickupItem>();
            return hitItem == item;
        }
        return false;
    }

    private static PickupItem ResolveMountedPickup(PickupItem candidate)
    {
        if (candidate == null || !IsPlateType(candidate.ItemType))
        {
            return candidate;
        }

        GymMountedWeightMarker mountedMarker =
            candidate.GetComponentInParent<GymMountedWeightMarker>();
        PickupItem mountedBar = mountedMarker != null
            ? mountedMarker.GetComponent<PickupItem>()
            : null;
        return mountedBar != null && mountedBar.IsThrowableWeapon
            ? mountedBar
            : candidate;
    }

    private static bool IsWeightStandPlate(PickupItem item)
    {
        if (item == null || !IsPlateType(item.ItemType))
        {
            return false;
        }

        Transform current = item.transform;
        while (current != null)
        {
            if (ContainsNormalizedWeightStandFlat(current.name))
            {
                return true;
            }
            current = current.parent;
        }
        return false;
    }

    private static bool ContainsNormalizedWeightStandFlat(string value)
    {
        const string marker = "weightstandflat";
        int markerIndex = 0;
        for (int i = 0; i < value.Length; i++)
        {
            char character = value[i];
            if (character == ' ' || character == '_' || character == '-')
            {
                continue;
            }

            char normalizedCharacter = char.ToLowerInvariant(character);
            if (normalizedCharacter == marker[markerIndex])
            {
                markerIndex++;
                if (markerIndex == marker.Length)
                {
                    return true;
                }
            }
            else
            {
                markerIndex = normalizedCharacter == marker[0] ? 1 : 0;
            }
        }
        return false;
    }

    private Vector3 GetClosestPickupPoint(PickupItem item, Vector3 origin)
    {
        pickupColliderScratch.Clear();
        Transform itemTransform = item != null ? item.transform : null;
        if (itemTransform == null)
        {
            return origin;
        }

        Vector3 closest = itemTransform.position;
        float closestDistance = (closest - origin).sqrMagnitude;
        try
        {
            item.GetComponentsInChildren<Collider>(true, pickupColliderScratch);
            for (int i = 0; i < pickupColliderScratch.Count; i++)
            {
                Collider collider = pickupColliderScratch[i];
                if (collider == null || !collider.enabled)
                {
                    continue;
                }
                Vector3 point = collider.ClosestPoint(origin);
                float distance = (point - origin).sqrMagnitude;
                if (distance < closestDistance)
                {
                    closest = point;
                    closestDistance = distance;
                }
            }
        }
        finally
        {
            pickupColliderScratch.Clear();
        }

        return closest;
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
        if (!ReadPauseToggle() || GymPauseMenu.IsVisible)
        {
            return;
        }

        if (IsDead)
        {
            if (IsCursorCaptured)
            {
                LockCursor(false);
            }
            return;
        }

        if (activeExerciseStation != null || pendingWeightStation != null ||
            pullUpMountTransitionActive ||
            (GymExperienceService.Active != null &&
             GymExperienceService.Active.IsBlockingPlayerInput))
        {
            return;
        }

        if (IsCursorCaptured)
        {
            LockCursor(false);
        }
        GymPauseMenu.Open(this);
    }

    private void BeginExercise(GymExerciseStation station)
    {
        if (station == null || activeExerciseStation != null ||
            pullUpMountTransitionActive)
        {
            return;
        }

        if (GymExperienceService.Active != null &&
            !GymExperienceService.Active.CanStartWorkout())
        {
            return;
        }

        // Reserve before moving the player into the authored exercise pose.
        // The nearby query normally filters occupied treadmills, but this
        // second check closes the race where an enemy claims the belt between
        // the query and the F key press.
        if (!station.TryReserveForPlayer(this))
        {
            nearbyExerciseStation = null;
            return;
        }

        if (heldItem != null)
        {
            DropHeldItem();
        }

        positionBeforeExercise = transform.position;
        rotationBeforeExercise = transform.rotation;
        cameraPositionBeforeExercise = playerCamera.transform.localPosition;
        cameraRotationBeforeExercise = playerCamera.transform.localRotation;
        cameraFieldOfViewBeforeExercise = playerCamera.fieldOfView;
        planarVelocity = Vector3.zero;
        impactVelocity = Vector3.zero;
        verticalVelocity = 0f;

        characterController.enabled = false;
        LockCursor(true);

        if (station.ExerciseType == GymExerciseType.PullUps)
        {
            BeginPullUpMountTransition(station);
            return;
        }

        activeExerciseStation = station;
        transform.SetPositionAndRotation(station.PlayerPosition, station.PlayerRotation);
        station.BeginSession(playerCamera.transform);
        station.GetCameraPose(out Vector3 cameraPosition, out Quaternion cameraRotation);
        playerCamera.transform.localPosition = cameraPosition;
        playerCamera.transform.localRotation = cameraRotation;
        if (handRig != null)
        {
            handRig.gameObject.SetActive(false);
        }
    }

    private void BeginPullUpMountTransition(GymExerciseStation station)
    {
        pullUpMountStation = station;
        pullUpMountTransitionActive = true;
        pullUpMountElapsed = 0f;
        pullUpMountStartPosition = transform.position;
        pullUpMountStartRotation = transform.rotation;
        pullUpMountTargetPosition = station.PlayerPosition;
        pullUpMountTargetRotation = station.PlayerRotation;
        pullUpMountStartCameraPosition = playerCamera.transform.localPosition;
        pullUpMountStartCameraRotation = playerCamera.transform.localRotation;
        station.GetCameraPose(
            out pullUpMountTargetCameraPosition,
            out pullUpMountTargetCameraRotation);

        // The controller is disabled only for this short authored mount so
        // input and collision resolution cannot fight the jump arc. The root
        // is still moved every frame, so the player sees the approach instead
        // of receiving the old instant teleport to the bar.
        if (handRig != null)
        {
            handRig.gameObject.SetActive(true);
        }
    }

    private void HandlePullUpMountTransition()
    {
        if (pullUpMountStation == null)
        {
            EndExercise();
            return;
        }

        if (ReadExerciseExitPressed())
        {
            EndExercise();
            return;
        }

        pullUpMountElapsed += Time.deltaTime;
        float rawT = Mathf.Clamp01(
            pullUpMountElapsed / Mathf.Max(0.01f, PullUpMountDuration));
        float easedT = Mathf.SmoothStep(0f, 1f, rawT);
        Vector3 mountPosition = Vector3.Lerp(
            pullUpMountStartPosition, pullUpMountTargetPosition, easedT);
        mountPosition.y += Mathf.Sin(rawT * Mathf.PI) * PullUpMountJumpHeight;
        Quaternion mountRotation = Quaternion.Slerp(
            pullUpMountStartRotation, pullUpMountTargetRotation, easedT);
        transform.SetPositionAndRotation(mountPosition, mountRotation);

        playerCamera.transform.localPosition = Vector3.Lerp(
            pullUpMountStartCameraPosition,
            pullUpMountTargetCameraPosition,
            easedT);
        Quaternion worldCameraRotation = Quaternion.Slerp(
            pullUpMountStartRotation * pullUpMountStartCameraRotation,
            pullUpMountTargetRotation * pullUpMountTargetCameraRotation,
            easedT);
        playerCamera.transform.localRotation = Quaternion.Inverse(mountRotation) *
            worldCameraRotation;

        if (rawT >= 1f)
        {
            FinishPullUpMountTransition();
        }
    }

    private void FinishPullUpMountTransition()
    {
        GymExerciseStation station = pullUpMountStation;
        pullUpMountTransitionActive = false;
        pullUpMountStation = null;
        pullUpMountElapsed = 0f;
        transform.SetPositionAndRotation(
            pullUpMountTargetPosition, pullUpMountTargetRotation);
        playerCamera.transform.localPosition = pullUpMountTargetCameraPosition;
        playerCamera.transform.localRotation = pullUpMountTargetCameraRotation;

        activeExerciseStation = station;
        station.BeginSession(playerCamera.transform);
        station.GetCameraPose(out Vector3 cameraPosition, out Quaternion cameraRotation);
        playerCamera.transform.localPosition = cameraPosition;
        playerCamera.transform.localRotation = cameraRotation;
        if (handRig != null)
        {
            handRig.gameObject.SetActive(false);
        }
    }

    private void OpenWeightSelection(GymExerciseStation station)
    {
        if (station == null || !station.RequiresWeightSelection)
        {
            return;
        }

        pendingWeightStation = station;
        planarVelocity = Vector3.zero;
        impactVelocity = Vector3.zero;
        verticalVelocity = 0f;
        LockCursor(false);
    }

    private void HandleWeightSelectionMode()
    {
        if (pendingWeightStation == null)
        {
            return;
        }

        if (ReadExerciseExitPressed() || ReadPauseToggle())
        {
            pendingWeightStation = null;
            LockCursor(true);
        }
    }

    private void SelectWeightAndBegin(int totalWeight)
    {
        GymExerciseStation station = pendingWeightStation;
        if (station == null)
        {
            return;
        }

        station.SelectWeight(totalWeight);
        pendingWeightStation = null;
        BeginExercise(station);
    }

    private void HandleExerciseMode()
    {
        if (activeExerciseStation == null)
        {
            return;
        }

        if (ReadExerciseExitPressed())
        {
            EndExercise();
            return;
        }

        activeExerciseStation.TickSession(
            Time.deltaTime,
            ReadExerciseActionPressed(),
            ReadExerciseIncreasePressed(),
            ReadExerciseDecreasePressed());

        activeExerciseStation.GetCameraPose(out Vector3 cameraPosition, out Quaternion cameraRotation);
        playerCamera.transform.localPosition = Vector3.Lerp(playerCamera.transform.localPosition, cameraPosition, 10f * Time.deltaTime);
        playerCamera.transform.localRotation = Quaternion.Slerp(playerCamera.transform.localRotation, cameraRotation, 10f * Time.deltaTime);
        playerCamera.fieldOfView = Mathf.Lerp(playerCamera.fieldOfView, activeExerciseStation.GetCameraFieldOfView(cameraFieldOfViewBeforeExercise), 4f * Time.deltaTime);
    }

    private void EndExercise()
    {
        if (activeExerciseStation == null && !pullUpMountTransitionActive)
        {
            return;
        }

        if (activeExerciseStation != null)
        {
            activeExerciseStation.EndSession();
        }
        else if (pullUpMountStation != null)
        {
            pullUpMountStation.CancelPlayerReservation(this);
        }

        activeExerciseStation = null;
        pullUpMountTransitionActive = false;
        pullUpMountStation = null;
        pullUpMountElapsed = 0f;
        transform.SetPositionAndRotation(positionBeforeExercise, rotationBeforeExercise);
        playerCamera.transform.localPosition = cameraPositionBeforeExercise;
        playerCamera.transform.localRotation = cameraRotationBeforeExercise;
        playerCamera.fieldOfView = cameraFieldOfViewBeforeExercise;
        characterController.enabled = true;
        if (handRig != null)
        {
            handRig.gameObject.SetActive(true);
            handRig.SetHolding(false);
        }

        nearbyExerciseStation = null;
    }

    private void LockCursor(bool locked)
    {
        if (locked && !CanRequestCursorLock)
        {
            locked = false;
        }

        if (!locked)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            GymChaosExitPointerLock();
#else
            Cursor.lockState = CursorLockMode.None;
#endif
            Cursor.visible = true;
            showCursor = true;
            return;
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        bool webGlCaptured = IsCursorCaptured;
        Cursor.visible = !webGlCaptured;
        showCursor = !webGlCaptured;
        return;
#else
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
        bool captured = IsCursorCaptured;
        showCursor = !captured;
        if (!captured)
        {
            Cursor.visible = true;
        }
#endif
    }

    private bool TryCaptureCursor()
    {
        if (IsCursorCaptured)
        {
            suppressGameplayInputThisFrame = showCursor;
            showCursor = false;
            Cursor.visible = false;
            return false;
        }

        showCursor = true;
        Cursor.visible = true;
        if (!ReadPointerCapturePressed())
        {
            return false;
        }

        // A browser click can reach Unity just before the WebGL document has
        // reported focus. Avoid issuing requestPointerLock in that window;
        // the next click will retry without raising WrongDocumentError.
        if (!CanRequestCursorLock)
        {
            return false;
        }

        suppressGameplayInputThisFrame = true;
#if UNITY_WEBGL && !UNITY_EDITOR
        // The canvas mousedown handler has already requested pointer lock in
        // the browser's trusted gesture. Do not call Cursor.lockState here:
        // Unity defers that request and can raise WrongDocumentError.
        bool webGlCaptured = IsCursorCaptured;
        Cursor.visible = !webGlCaptured;
        showCursor = !webGlCaptured;
        return true;
#else
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
        bool captured = IsCursorCaptured;
        showCursor = !captured;
        if (!captured)
        {
            Cursor.visible = true;
        }
        return true;
#endif
    }

    private bool CanRequestCursorLock
    {
        get
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return GymChaosDocumentHasFocus() != 0;
#else
            return Application.platform != RuntimePlatform.WebGLPlayer || Application.isFocused;
#endif
        }
    }

    private bool IsCursorCaptured
    {
        get
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return GymChaosIsPointerLocked() != 0;
#else
            return Cursor.lockState == CursorLockMode.Locked && !Cursor.visible;
#endif
        }
    }

    private void OnApplicationFocus(bool hasFocus)
    {
        if (!hasFocus)
        {
            LockCursor(false);
        }
    }

    private void OnApplicationPause(bool pauseStatus)
    {
        if (pauseStatus)
        {
            LockCursor(false);
        }
    }

    private void OnGUI()
    {
        // The fullscreen start screen owns the frame until Play is selected.
        // Keep the legacy IMGUI HUD from leaking through the cinematic menu.
        if (GymStartScreen.IsMenuVisible || GymPauseMenu.IsVisible)
        {
            return;
        }

        GymExperienceService progressionService = GymExperienceService.Active;
        if (progressionService != null && progressionService.IsLockerMenuOpen)
        {
            return;
        }

        if (IsDead)
        {
            DrawDeathOverlay();
            return;
        }

        if (GymDialogueDirector.IsDialogueActive)
        {
            return;
        }

        DrawBloodyOverlay();
        EnsureHudStyles();

        if (pendingWeightStation != null)
        {
            DrawWeightSelection();
            return;
        }

        if (pullUpMountTransitionActive)
        {
            DrawHudText(
                new Rect(24f, 20f, 780f, 28f),
                "PULL UPS",
                hudTitleStyle);
            DrawHudText(
                new Rect(24f, 49f, 780f, 28f),
                "MOUNTING BAR...   [Q] CANCEL",
                hudHintStyle);
            return;
        }

        if (activeExerciseStation != null)
        {
            DrawActiveExerciseHud();
            if (activeExerciseStation.TechniqueCheck != null &&
                activeExerciseStation.TechniqueCheck.IsActive)
            {
                Rect ringRect = new Rect(
                    Screen.width * 0.5f - 118f,
                    Screen.height * 0.5f - 118f,
                    236f,
                    236f);
                activeExerciseStation.TechniqueCheck.DrawGUI(ringRect);
            }
            return;
        }

        float sprintCapacity = GymExperienceService.Active != null
            ? GymExperienceService.Active.GetSprintCapacity()
            : 100f;
        float hudLeft = Mathf.Clamp(Screen.width * 0.02f, 18f, 32f);
        float labelWidth = Mathf.Clamp(Screen.width * 0.11f, 64f, 92f);
        float barX = hudLeft + labelWidth + 10f;
        float barWidth = Mathf.Clamp(Screen.width * 0.2f, 100f, 220f);
        float valueX = barX + barWidth + 10f;
        float valueWidth = Mathf.Max(42f, Screen.width - valueX - hudLeft);
        DrawHudText(new Rect(hudLeft, 18f, labelWidth, 20f), "HP", hudMetricStyle);
        DrawHudText(new Rect(hudLeft, 45f, labelWidth, 20f), "SPRINT", hudMetricStyle);
        DrawHudBar(
            new Rect(barX, 23f, barWidth, 8f),
            currentHealth / Mathf.Max(1f, maxHealth),
            new Color(0.84f, 0.24f, 0.18f, 1f));
        DrawHudBar(
            new Rect(barX, 50f, barWidth, 8f),
            sprintEnergy / Mathf.Max(1f, sprintCapacity),
            new Color(0.95f, 0.62f, 0.16f, 1f));
        DrawHudText(
            new Rect(valueX, 15f, valueWidth, 22f),
            $"{Mathf.CeilToInt(currentHealth):0}",
            hudMetricStyle);
        DrawHudText(
            new Rect(valueX, 42f, valueWidth, 22f),
            $"{Mathf.CeilToInt(sprintEnergy):0}",
            hudMetricStyle);
        DrawHudText(
            new Rect(hudLeft, 77f, 300f, 20f),
            $"MEMBERS  {EnemyFighter.ActiveCount:00}",
            hudHintStyle);
        if (heldItem != null)
        {
            DrawHudText(
                new Rect(hudLeft, 101f, Mathf.Max(220f, Screen.width - hudLeft * 2f), 22f),
                $"HELD  {heldItem.DisplayName.ToUpperInvariant()}",
                hudAccentStyle);
        }

        if (CanShowCursorRecapturePrompt)
        {
            float width = Mathf.Min(360f, Screen.width - 32f);
            Rect captureRect = new Rect(
                (Screen.width - width) * 0.5f,
                Screen.height * 0.5f - 26f,
                width,
                52f);
            DrawHudPrompt(captureRect, "LMB", "CLICK TO LOOK AROUND");
        }

        DrawContextInteractionPrompts();
    }

    private void DrawContextInteractionPrompts()
    {
        if (GymExperienceService.Active != null &&
            GymExperienceService.Active.IsBlockingPlayerInput)
        {
            return;
        }

        bool hasE = false;
        string eMessage = string.Empty;
        if (nearbyTalkTarget != null)
        {
            hasE = true;
            eMessage = "TALK TO MEMBER";
        }
        else if (nearbyBackRoomInteractable != null)
        {
            hasE = true;
            eMessage = string.IsNullOrWhiteSpace(nearbyBackRoomInteractable.DisplayName)
                ? "INTERACT"
                : nearbyBackRoomInteractable.DisplayName.ToUpperInvariant();
        }
        else if (heldItem != null)
        {
            hasE = true;
            eMessage = "DROP " + GetPromptItemName(heldItem);
        }
        else if (nearbyPickup != null)
        {
            hasE = true;
            eMessage = "PICK UP " + GetPromptItemName(nearbyPickup);
        }

        bool hasF = false;
        string fMessage = string.Empty;
        if (nearbyRadio != null)
        {
            hasF = true;
            fMessage = StripPromptKey(nearbyRadio.GetInteractionPrompt());
        }
        else if (nearbyExerciseStation != null &&
                 nearbyExerciseStation.IsAvailableForPlayer)
        {
            hasF = true;
            fMessage = StripPromptKey(nearbyExerciseStation.GetInteractionPrompt());
        }

        if (!hasE && !hasF)
        {
            return;
        }

        float eWidth = hasE ? GetHudPromptWidth(eMessage) : 0f;
        float fWidth = hasF ? GetHudPromptWidth(fMessage) : 0f;
        float eHeight = hasE ? GetHudPromptHeight(eMessage, eWidth) : 0f;
        float fHeight = hasF ? GetHudPromptHeight(fMessage, fWidth) : 0f;
        float promptHeight = Mathf.Max(eHeight, fHeight, ContextPromptHeight);
        float maxAllowedWidth = GetHudPromptViewportWidth();
        bool sideBySide = hasE && hasF && Screen.width >= 640f &&
            eWidth + ContextPromptGap + fWidth <= maxAllowedWidth;
        if (sideBySide)
        {
            float totalWidth = eWidth + ContextPromptGap + fWidth;
            float left = (Screen.width - totalWidth) * 0.5f;
            DrawHudPrompt(
                new Rect(left, Screen.height - ContextPromptBottomInset - promptHeight, eWidth, promptHeight),
                "E",
                eMessage);
            DrawHudPrompt(
                new Rect(left + eWidth + ContextPromptGap, Screen.height - ContextPromptBottomInset - promptHeight,
                    fWidth, promptHeight),
                "F",
                fMessage);
            return;
        }

        float stackedBottom = Screen.height - ContextPromptBottomInset;
        float stackedTotalHeight = (hasE ? eHeight : 0f) +
            (hasF ? fHeight : 0f) +
            (hasE && hasF ? ContextPromptGap : 0f);
        float stackedTop = stackedBottom - stackedTotalHeight;

        if (hasE)
        {
            DrawHudPrompt(
                new Rect(
                    (Screen.width - eWidth) * 0.5f,
                    stackedTop,
                    eWidth,
                    eHeight),
                "E",
                eMessage);
            stackedTop += eHeight + ContextPromptGap;
        }

        if (hasF)
        {
            DrawHudPrompt(
                new Rect(
                    (Screen.width - fWidth) * 0.5f,
                    stackedTop,
                    fWidth,
                    fHeight),
                "F",
                fMessage);
        }
    }

    private static float GetHudPromptViewportWidth()
    {
        return Mathf.Max(1f, Screen.width - ContextPromptViewportInset * 2f);
    }

    private float GetHudPromptWidth(string message)
    {
        string safeMessage = string.IsNullOrEmpty(message) ? " " : message;
        float measuredMessageWidth = hudPromptMessageStyle != null
            ? hudPromptMessageStyle.CalcSize(new GUIContent(safeMessage)).x
            : safeMessage.Length * 7f;
        float desiredWidth = ContextPromptOuterInset * 2f +
            ContextPromptKeyColumnWidth +
            ContextPromptMessageLeftPadding +
            ContextPromptMessageRightPadding +
            measuredMessageWidth;
        float availableWidth = GetHudPromptViewportWidth();
        float minimumWidth = Mathf.Min(ContextPromptMinWidth, availableWidth);
        return Mathf.Clamp(
            desiredWidth,
            minimumWidth,
            Mathf.Min(ContextPromptMaxWidth, availableWidth));
    }

    private float GetHudPromptHeight(string message, float promptWidth)
    {
        string safeMessage = string.IsNullOrEmpty(message) ? " " : message;
        float messageWidth = GetHudPromptMessageWidth(promptWidth);
        float measuredMessageHeight = hudPromptMessageStyle != null
            ? hudPromptMessageStyle.CalcHeight(new GUIContent(safeMessage), messageWidth)
            : 18f;
        return Mathf.Max(
            ContextPromptHeight,
            Mathf.Ceil(measuredMessageHeight) + ContextPromptInnerInset * 2f);
    }

    private static float GetHudPromptKeyWidth(float promptWidth)
    {
        float availableKeyWidth = promptWidth - ContextPromptOuterInset * 2f -
            ContextPromptMessageLeftPadding - ContextPromptMessageRightPadding;
        return Mathf.Min(ContextPromptKeyColumnWidth, Mathf.Max(1f, availableKeyWidth));
    }

    private static float GetHudPromptMessageWidth(float promptWidth)
    {
        return Mathf.Max(
            1f,
            promptWidth - ContextPromptOuterInset * 2f -
            GetHudPromptKeyWidth(promptWidth) -
            ContextPromptMessageLeftPadding -
            ContextPromptMessageRightPadding);
    }

    private static Rect GetHudPromptContentRect(Rect promptRect)
    {
        return new Rect(
            promptRect.x + ContextPromptOuterInset,
            promptRect.y + ContextPromptInnerInset,
            Mathf.Max(1f, promptRect.width - ContextPromptOuterInset * 2f),
            Mathf.Max(1f, promptRect.height - ContextPromptInnerInset * 2f));
    }

    private static string GetPromptItemName(PickupItem item)
    {
        return item == null || string.IsNullOrWhiteSpace(item.DisplayName)
            ? "ITEM"
            : item.DisplayName.ToUpperInvariant();
    }

    private static string StripPromptKey(string prompt)
    {
        if (string.IsNullOrEmpty(prompt))
        {
            return string.Empty;
        }

        return prompt.StartsWith("[F] ") || prompt.StartsWith("[E] ")
            ? prompt.Substring(4)
            : prompt;
    }

    private static void DrawHudPanel(Rect rect)
    {
        Color previousColor = GUI.color;
        GUI.color = new Color(0.012f, 0.022f, 0.04f, 0.86f);
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = new Color(0.98f, 0.34f, 0.13f, 0.95f);
        GUI.DrawTexture(new Rect(rect.x, rect.y, 3f, rect.height), Texture2D.whiteTexture);
        GUI.color = previousColor;
    }

    private void DrawHudBar(Rect rect, float value, Color fillColor)
    {
        Color previousColor = GUI.color;
        GUI.color = new Color(0.08f, 0.11f, 0.16f, 0.9f);
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = fillColor;
        GUI.DrawTexture(
            new Rect(rect.x, rect.y, rect.width * Mathf.Clamp01(value), rect.height),
            Texture2D.whiteTexture);
        GUI.color = previousColor;
    }

    private void DrawHudPrompt(Rect rect, string key, string message)
    {
        DrawHudPanel(rect);
        Rect contentRect = GetHudPromptContentRect(rect);
        float keyWidth = GetHudPromptKeyWidth(rect.width);
        Rect keyRect = new Rect(
            contentRect.x,
            contentRect.y,
            keyWidth,
            contentRect.height);
        Color previousColor = GUI.color;
        GUI.color = new Color(0.95f, 0.62f, 0.16f, 0.95f);
        GUI.DrawTexture(keyRect, Texture2D.whiteTexture);
        GUI.color = previousColor;
        DrawHudText(keyRect, key, hudPromptStyle);
        Rect messageRect = new Rect(
            keyRect.xMax + ContextPromptMessageLeftPadding,
            contentRect.y,
            GetHudPromptMessageWidth(rect.width),
            contentRect.height);
        DrawHudText(
            messageRect,
            message,
            hudPromptMessageStyle);
    }
    private void EnsureHudStyles()
    {
        if (hudTitleStyle != null)
        {
            return;
        }

        hudShadowStyle = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.UpperLeft,
            fontSize = 16,
            fontStyle = FontStyle.Bold,
            padding = new RectOffset(0, 0, 0, 0)
        };
        hudShadowStyle.normal.textColor = new Color(0f, 0f, 0f, 0.82f);

        hudTitleStyle = CreateHudStyle(20, FontStyle.Bold, TextAnchor.UpperLeft);
        hudTitleStyle.normal.textColor = Color.white;
        hudBodyStyle = CreateHudStyle(16, FontStyle.Bold, TextAnchor.UpperLeft);
        hudBodyStyle.normal.textColor = Color.white;
        hudMetricStyle = CreateHudStyle(15, FontStyle.Bold, TextAnchor.UpperLeft);
        hudMetricStyle.normal.textColor = new Color(0.72f, 0.84f, 1f);
        hudHintStyle = CreateHudStyle(13, FontStyle.Normal, TextAnchor.UpperLeft);
        hudHintStyle.normal.textColor = new Color(0.86f, 0.9f, 0.96f);
        hudAccentStyle = CreateHudStyle(14, FontStyle.Bold, TextAnchor.UpperLeft);
        hudAccentStyle.normal.textColor = new Color(1f, 0.82f, 0.35f);
        hudPromptStyle = CreateHudStyle(17, FontStyle.Bold, TextAnchor.MiddleCenter);
        hudPromptStyle.normal.textColor = new Color(1f, 0.82f, 0.35f);
        hudPromptMessageStyle = CreateHudStyle(14, FontStyle.Bold, TextAnchor.MiddleLeft);
        hudPromptMessageStyle.normal.textColor = Color.white;
        hudPromptMessageStyle.clipping = TextClipping.Clip;
    }

    private static GUIStyle CreateHudStyle(
        int fontSize, FontStyle fontStyle, TextAnchor alignment)
    {
        GUIStyle style = new GUIStyle(GUI.skin.label)
        {
            alignment = alignment,
            fontSize = fontSize,
            fontStyle = fontStyle,
            padding = new RectOffset(0, 0, 0, 0),
            wordWrap = true
        };
        style.normal.background = null;
        style.hover.background = null;
        style.active.background = null;
        style.focused.background = null;
        return style;
    }

    private void DrawHudText(Rect rect, string text, GUIStyle style)
    {
        hudShadowStyle.fontSize = style.fontSize;
        hudShadowStyle.fontStyle = style.fontStyle;
        hudShadowStyle.alignment = style.alignment;
        hudShadowStyle.wordWrap = style.wordWrap;
        GUI.Label(new Rect(rect.x + 1f, rect.y + 1f, rect.width, rect.height), text, hudShadowStyle);
        GUI.Label(rect, text, style);
    }

    private void DrawBloodyOverlay()
    {
        if (MissingHealth01 <= 0.001f)
        {
            return;
        }

        // The damage feedback is visible during normal play and scales with
        // missing health; the death path adds its own translucent black layer.
        GUI.color = new Color(0.62f, 0f, 0f, Mathf.Lerp(0f, 0.68f, MissingHealth01));
        GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);
        GUI.color = Color.white;
    }

    private void DrawDeathOverlay()
    {
        // The overlay deliberately remains translucent so the enemy that
        // delivered the contact punch can continue its Celebration clip behind
        // the death screen.
        GUI.color = new Color(0.55f, 0f, 0f, Mathf.Lerp(0f, 0.68f, MissingHealth01));
        GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);

        GUI.color = new Color(0f, 0f, 0f, 0.76f);
        GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);

        GUIStyle deathStyle = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = Mathf.Max(42, Screen.height / 14),
            fontStyle = FontStyle.Bold
        };
        deathStyle.normal.textColor = Color.white;
        GUI.color = Color.white;
        GUI.Label(new Rect(0f, Screen.height * 0.38f, Screen.width, 100f), "YOU DIED", deathStyle);
    }

    private void DrawActiveExerciseHud()
    {
        GymExerciseStation station = activeExerciseStation;
        if (station == null)
        {
            return;
        }

        float panelWidth = Mathf.Min(460f, Screen.width - 40f);
        Rect panel = new Rect(20f, 20f, panelWidth, 122f);
        DrawHudPanel(panel);
        DrawHudText(
            new Rect(panel.x + 22f, panel.y + 14f, panel.width - 44f, 24f),
            station.DisplayName.ToUpperInvariant(),
            hudTitleStyle);

        if (station.IsCardio)
        {
            DrawHudText(
                new Rect(panel.x + 22f, panel.y + 48f, 58f, 20f),
                "PACE",
                hudHintStyle);
            DrawHudText(
                new Rect(panel.x + 86f, panel.y + 45f, 92f, 26f),
                $"{station.CurrentTreadmillSpeed:0.0}",
                hudTitleStyle);
            DrawHudText(
                new Rect(panel.x + 180f, panel.y + 50f, 36f, 18f),
                "KM/H",
                hudHintStyle);
            DrawHudText(
                new Rect(panel.x + 236f, panel.y + 48f, panel.width - 258f, 20f),
                $"DIST  {station.CardioDistanceMetres / 1000f:0.00} KM",
                hudMetricStyle);
            DrawHudBar(
                new Rect(panel.x + 22f, panel.y + 80f, panel.width - 44f, 5f),
                station.TreadmillSpeed01(station.CurrentTreadmillSpeed),
                new Color(0.95f, 0.62f, 0.16f, 1f));
            DrawHudText(
                new Rect(panel.x + 22f, panel.y + 98f, panel.width - 44f, 16f),
                "W / S  SPEED     SPACE  START / STOP     Q  EXIT",
                hudHintStyle);
            return;
        }

        string load = station.SelectedWeight > 0
            ? $"{station.SelectedWeight} KG"
            : "BODYWEIGHT";
        DrawHudText(
            new Rect(panel.x + 22f, panel.y + 48f, 58f, 20f),
            "REPS",
            hudHintStyle);
        DrawHudText(
            new Rect(panel.x + 86f, panel.y + 45f, 76f, 26f),
            $"{station.Repetitions:00}",
            hudTitleStyle);
        DrawHudText(
            new Rect(panel.x + 190f, panel.y + 48f, 58f, 20f),
            "LOAD",
            hudHintStyle);
        DrawHudText(
            new Rect(panel.x + 252f, panel.y + 48f, panel.width - 274f, 20f),
            load,
            hudMetricStyle);
        DrawHudText(
            new Rect(panel.x + 22f, panel.y + 73f, 68f, 18f),
            "COMBO",
            hudHintStyle);
        DrawHudText(
            new Rect(panel.x + 96f, panel.y + 70f, 72f, 22f),
            $"x{station.ComboMultiplier:0}",
            hudAccentStyle);
        DrawHudBar(
            new Rect(panel.x + 190f, panel.y + 80f, panel.width - 212f, 5f),
            Mathf.Clamp01(station.ComboMultiplier / 5f),
            new Color(1f, 0.82f, 0.35f, 1f));
        DrawHudText(
            new Rect(panel.x + 22f, panel.y + 98f, panel.width - 44f, 16f),
            station.TechniqueCheck != null && station.TechniqueCheck.IsActive
                ? "SPACE  HIT TIMING     Q  EXIT"
                : "SPACE  START REP     Q  EXIT",
            hudHintStyle);
    }

    private bool DrawWeightChoiceButton(Rect rect, string label, GUIStyle style)
    {
        bool hovered = rect.Contains(Event.current.mousePosition);
        Color previousColor = GUI.color;
        GUI.color = hovered
            ? new Color(0.12f, 0.16f, 0.22f, 0.96f)
            : new Color(0.05f, 0.075f, 0.11f, 0.96f);
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = hovered
            ? new Color(0.98f, 0.34f, 0.13f, 1f)
            : new Color(0.34f, 0.48f, 0.64f, 0.9f);
        GUI.DrawTexture(new Rect(rect.x, rect.y, 3f, rect.height), Texture2D.whiteTexture);
        GUI.color = previousColor;
        return GUI.Button(rect, label, style);
    }

    private void DrawWeightSelection()
    {
        int[] options = pendingWeightStation.WeightOptions;
        if (options == null || options.Length == 0)
        {
            return;
        }

        const int columns = 3;
        const float buttonWidth = 138f;
        const float buttonHeight = 58f;
        const float gap = 12f;
        const float headerHeight = 124f;
        int rows = Mathf.CeilToInt(options.Length / (float)columns);
        float panelWidth = columns * buttonWidth + (columns + 1) * gap;
        float panelHeight = headerHeight + rows * buttonHeight + (rows + 1) * gap;
        Rect panel = new Rect(
            (Screen.width - panelWidth) * 0.5f,
            (Screen.height - panelHeight) * 0.5f,
            panelWidth,
            panelHeight);
        DrawHudPanel(panel);

        DrawHudText(
            new Rect(panel.x + 20f, panel.y + 14f, panel.width - 40f, 26f),
            pendingWeightStation.DisplayName.ToUpperInvariant(),
            hudTitleStyle);
        DrawHudText(
            new Rect(panel.x + 20f, panel.y + 47f, panel.width - 40f, 22f),
            "SELECT LOAD",
            hudAccentStyle);
        string weightInfo = pendingWeightStation.EmptyBarWeight > 0
            ? $"BAR  {pendingWeightStation.EmptyBarWeight} KG"
            : "WEIGHT STACK";
        DrawHudText(
            new Rect(panel.x + 20f, panel.y + 72f, panel.width - 40f, 18f),
            weightInfo,
            hudHintStyle);
        DrawHudBar(
            new Rect(panel.x + 20f, panel.y + 100f, panel.width - 40f, 2f),
            1f,
            new Color(0.95f, 0.62f, 0.16f, 0.85f));
        DrawHudText(
            new Rect(panel.x + 20f, panel.y + 104f, panel.width - 40f, 16f),
            "Q / E / ESC  CANCEL",
            hudHintStyle);

        GUIStyle choiceStyle = CreateHudStyle(19, FontStyle.Bold, TextAnchor.MiddleCenter);
        choiceStyle.normal.textColor = Color.white;
        choiceStyle.hover.textColor = new Color(1f, 0.82f, 0.35f);
        choiceStyle.active.textColor = new Color(0.98f, 0.34f, 0.13f);
        choiceStyle.focused.textColor = Color.white;

        for (int i = 0; i < options.Length; i++)
        {
            int row = i / columns;
            int column = i % columns;
            Rect button = new Rect(
                panel.x + gap + column * (buttonWidth + gap),
                panel.y + headerHeight + gap + row * (buttonHeight + gap),
                buttonWidth,
                buttonHeight);
            if (DrawWeightChoiceButton(button, $"{options[i]} KG", choiceStyle))
            {
                SelectWeightAndBegin(options[i]);
            }
        }
    }
    private bool ReadExerciseActionPressed()
    {
#if ENABLE_INPUT_SYSTEM
        return Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame;
#else
        return Input.GetKeyDown(KeyCode.Space);
#endif
    }

    private bool ReadExerciseStartPressed()
    {
#if ENABLE_INPUT_SYSTEM
        return Keyboard.current != null && Keyboard.current.fKey.wasPressedThisFrame;
#else
        return Input.GetKeyDown(KeyCode.F);
#endif
    }

    private bool ReadExerciseIncreasePressed()
    {
#if ENABLE_INPUT_SYSTEM
        return Keyboard.current != null && (Keyboard.current.wKey.wasPressedThisFrame || Keyboard.current.upArrowKey.wasPressedThisFrame);
#else
        return Input.GetKeyDown(KeyCode.W) || Input.GetKeyDown(KeyCode.UpArrow);
#endif
    }

    private bool ReadExerciseDecreasePressed()
    {
#if ENABLE_INPUT_SYSTEM
        return Keyboard.current != null && (Keyboard.current.sKey.wasPressedThisFrame || Keyboard.current.downArrowKey.wasPressedThisFrame);
#else
        return Input.GetKeyDown(KeyCode.S) || Input.GetKeyDown(KeyCode.DownArrow);
#endif
    }

    private bool ReadExerciseExitPressed()
    {
#if ENABLE_INPUT_SYSTEM
        return Keyboard.current != null && (Keyboard.current.qKey.wasPressedThisFrame || Keyboard.current.eKey.wasPressedThisFrame);
#else
        return Input.GetKeyDown(KeyCode.Q) || Input.GetKeyDown(KeyCode.E);
#endif
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

    private bool ReadPointerCapturePressed()
    {
#if ENABLE_INPUT_SYSTEM
        return Mouse.current != null &&
               (Mouse.current.leftButton.wasPressedThisFrame ||
                Mouse.current.rightButton.wasPressedThisFrame);
#else
        return Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1);
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

    private bool ReadRadioTogglePressed()
    {
        // Radio uses the same contextual interaction key as exercise stations.
        return ReadExerciseStartPressed();
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
