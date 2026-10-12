using UnityEngine;

/// <summary>
/// Hinged seat and lid on the bathroom toilet (toilet_v2.glb). Two states
/// only: both closed, or both lifted against the tank. The seat and lid nodes
/// have their origins on the hinge line, so a rotation about local X opens
/// them.
/// </summary>
[DisallowMultipleComponent]
public sealed class GymToiletSeat : MonoBehaviour
{
    private const float SeatOpenAngle = -92f;
    private const float LidOpenAngle = -96f;
    private const float SwingSeconds = 0.22f;

    private Transform seat;
    private Transform lid;
    private Quaternion seatClosedRotation;
    private Quaternion lidClosedRotation;
    private GymBackRoomInteractable interactable;
    private float swing = 0f;
    private float swingTarget = 0f;

    public bool IsSeatUp => swingTarget > 0.5f;
    internal bool IsBoundForVerification => seat != null && lid != null;
    internal float SeatAngleForVerification =>
        seat == null ? 0f : Quaternion.Angle(seatClosedRotation, seat.localRotation);
    internal float LidAngleForVerification =>
        lid == null ? 0f : Quaternion.Angle(lidClosedRotation, lid.localRotation);

    private void Awake()
    {
        interactable = GetComponent<GymBackRoomInteractable>();
        RefreshPrompt();
    }

    public void Bind(Transform toiletRoot)
    {
        if (toiletRoot == null)
        {
            return;
        }
        foreach (Transform child in toiletRoot.GetComponentsInChildren<Transform>(true))
        {
            if (child.name == "Toilet seat")
            {
                seat = child;
            }
            else if (child.name == "Toilet lid")
            {
                lid = child;
            }
        }
        if (seat != null)
        {
            seatClosedRotation = seat.localRotation;
        }
        if (lid != null)
        {
            lidClosedRotation = lid.localRotation;
        }
        ApplySwing();
    }

    public void Toggle()
    {
        if (seat == null && lid == null)
        {
            return; // toilet model not loaded: nothing to move or hear
        }
        swingTarget = IsSeatUp ? 0f : 1f;
        GymAudio.Play(GymSoundEffect.ToiletSeat,
            seat != null ? seat.position : transform.position, 0.9f);
        RefreshPrompt();
        Debug.Log($"GYMCHAOS_TOILET_SEAT_TOGGLED up={IsSeatUp}", this);
    }

    private void Update()
    {
        if (Mathf.Approximately(swing, swingTarget))
        {
            return;
        }
        swing = Mathf.MoveTowards(swing, swingTarget, Time.deltaTime / SwingSeconds);
        ApplySwing();
    }

    private void ApplySwing()
    {
        // Ease in when lifting, drop with gravity when closing.
        float eased = swingTarget > swing
            ? Mathf.SmoothStep(0f, 1f, swing)
            : 1f - (1f - swing) * (1f - swing);
        if (seat != null)
        {
            seat.localRotation = seatClosedRotation * Quaternion.Euler(SeatOpenAngle * eased, 0f, 0f);
        }
        if (lid != null)
        {
            lid.localRotation = lidClosedRotation * Quaternion.Euler(LidOpenAngle * eased, 0f, 0f);
        }
    }

    private void RefreshPrompt()
    {
        if (interactable != null)
        {
            interactable.SetDisplayName(IsSeatUp ? "Close toilet" : "Open toilet");
        }
    }
}
