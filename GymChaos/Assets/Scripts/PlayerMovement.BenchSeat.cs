using UnityEngine;

/// <summary>
/// Sitting on the open-park benches: E sits down (not a workout) and E stands
/// up again. The game clock runs twice as fast while seated. When homeless
/// Hank is on the bench, the player takes the place beside him with a small
/// gap; free benches use the same two side places, so Hank's centre place
/// always stays clear for him.
/// </summary>
public partial class PlayerMovement
{
    public const float BenchSitRange = 2.1f;
    // Hips of the side places from the bench centre, along the bench.
    // Just inside the armrests (0.925 m): a clear gap to Hank in the middle.
    public const float BenchSideSeatOffset = 0.8f;
    public const float SeatedClockScale = 2f;
    // Camera height above the seat board for the tall cast.
    private const float SeatedEyeAboveSeat = 0.86f;
    // Hips sit a little behind the front edge of the seat board.
    private const float SeatedHipsBack = 0.05f;
    private const float StandUpForward = 0.85f;
    private const float SeatedYawLimit = 110f;

    private int nearbyBenchIndex = -1;
    private int seatedBenchIndex = -1;
    private Vector3 seatedHipsPosition;
    private Quaternion seatedFacing;
    private Vector3 cameraLocalBeforeSeat;
    private float seatedYaw;
    private bool seatedBesideHomeless;

    public bool IsSeatedOnBench => seatedBenchIndex >= 0;
    public bool IsSeatedBesideHomeless => IsSeatedOnBench && seatedBesideHomeless;
    public Vector3 SeatedHipsPosition => seatedHipsPosition;

    /// <summary>Closest open-park bench the player can sit on, or -1.</summary>
    public int FindNearbyParkBench()
    {
        int best = -1;
        float bestDistance = BenchSitRange;
        for (int i = 0; i < GymOutdoorFoliage.Benches.Count; i++)
        {
            GymOutdoorFoliage.ParkBench bench = GymOutdoorFoliage.Benches[i];
            if (!GymParkAccess.Contains(bench.Position, 0.8f) ||
                Mathf.Abs(transform.position.y - bench.Position.y) > 2.5f)
            {
                continue;
            }
            float distance = Vector2.Distance(
                new Vector2(transform.position.x, transform.position.z),
                new Vector2(bench.Position.x, bench.Position.z));
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = i;
            }
        }
        return best;
    }

    private static bool IsHomelessOnBench(int benchIndex)
    {
        GymHomelessAgent hank = GymHomelessAgent.Instance;
        return hank != null && hank.IsReady && hank.OccupiesBench && hank.BenchIndex == benchIndex;
    }

    /// <summary>Hips position and facing of the side place nearer to the player.</summary>
    public Vector3 GetBenchSeatHips(int benchIndex, Vector3 from, out Quaternion facing)
    {
        GymOutdoorFoliage.ParkBench bench = GymOutdoorFoliage.Benches[benchIndex];
        facing = Quaternion.LookRotation(bench.Facing, Vector3.up);
        Vector3 along = facing * Vector3.right;
        float side = Vector3.Dot(from - bench.Position, along) >= 0f ? 1f : -1f;
        return bench.Position + Vector3.up * GymOutdoorFoliage.BenchSeatTop +
            along * (side * BenchSideSeatOffset) - bench.Facing * SeatedHipsBack;
    }

    // E sits when the bench place is in front of the camera more than the
    // person to talk to (the reception F key uses the same rule).
    private bool PrefersSitOverTalk()
    {
        if (nearbyBenchIndex < 0)
        {
            return false;
        }
        if (!HasTalkTarget)
        {
            return true;
        }
        Transform view = playerCamera != null ? playerCamera.transform : transform;
        Vector3 seat = GetBenchSeatHips(nearbyBenchIndex, transform.position, out _);
        float seatAngle = Vector3.Angle(view.forward, seat - view.position);
        Vector3 talkPoint;
        if (nearbyTalkTarget != null)
        {
            talkPoint = nearbyTalkTarget.transform.position + Vector3.up * 1.4f;
        }
        else
        {
            GymHomelessAgent hank = GymHomelessAgent.Instance;
            if (hank == null || hank.HeadBone == null)
            {
                return true;
            }
            talkPoint = hank.HeadBone.position;
        }
        float talkAngle = Vector3.Angle(view.forward, talkPoint - view.position);
        return seatAngle < talkAngle;
    }

    public bool SitOnBench(int benchIndex)
    {
        if (benchIndex < 0 || benchIndex >= GymOutdoorFoliage.Benches.Count ||
            IsSeatedOnBench || playerCamera == null || IsDead)
        {
            return false;
        }
        seatedBesideHomeless = IsHomelessOnBench(benchIndex);
        seatedHipsPosition = GetBenchSeatHips(benchIndex, transform.position, out seatedFacing);
        cameraLocalBeforeSeat = playerCamera.transform.localPosition;
        seatedBenchIndex = benchIndex;
        seatedYaw = 0f;
        // The nearby scan pauses while seated: drop what it last found.
        nearbyTalkTarget = null;
        nearbyHomelessTalk = false;
        nearbyPickup = null;
        nearbyExerciseStation = null;
        nearbyRadio = null;
        nearbyBackRoomInteractable = null;
        if (characterController != null)
        {
            characterController.enabled = false;
        }
        // The root keeps its standing height above the ground: only the
        // camera drops to the seated eye line.
        float rootAboveGround = transform.position.y - GymOutdoorFoliage.Benches[benchIndex].Position.y;
        Vector3 root = new Vector3(seatedHipsPosition.x,
            GymOutdoorFoliage.Benches[benchIndex].Position.y + rootAboveGround, seatedHipsPosition.z);
        transform.SetPositionAndRotation(root, seatedFacing);
        Vector3 eye = seatedHipsPosition + Vector3.up * SeatedEyeAboveSeat;
        playerCamera.transform.position = eye;
        rotationX = 6f;
        playerCamera.transform.localRotation = Quaternion.Euler(rotationX, 0f, 0f);
        planarVelocity = Vector3.zero;
        impactVelocity = Vector3.zero;
        verticalVelocity = 0f;
        GymTimeOfDay.ClockScale = SeatedClockScale;
        GymAudio.Play2D(GymSoundEffect.UiClick, 0.35f);
        Debug.Log($"GYMCHAOS_BENCH_SIT bench={benchIndex} besideHomeless={seatedBesideHomeless} " +
            $"hips={seatedHipsPosition} clock={GymTimeOfDay.ClockScale:F1}", this);
        return true;
    }

    // Never leave the game clock fast without a seated player.
    private void ResetSeatClockOnDisable()
    {
        if (IsSeatedOnBench)
        {
            GymTimeOfDay.ClockScale = 1f;
        }
    }

    public void StandUpFromBench()
    {
        if (!IsSeatedOnBench)
        {
            return;
        }
        GymOutdoorFoliage.ParkBench bench = GymOutdoorFoliage.Benches[seatedBenchIndex];
        Vector3 stand = transform.position + bench.Facing * StandUpForward;
        transform.SetPositionAndRotation(stand, transform.rotation);
        if (playerCamera != null)
        {
            playerCamera.transform.localPosition = cameraLocalBeforeSeat;
        }
        if (characterController != null)
        {
            characterController.enabled = true;
        }
        int bench01 = seatedBenchIndex;
        seatedBenchIndex = -1;
        seatedBesideHomeless = false;
        GymTimeOfDay.ClockScale = 1f;
        Debug.Log($"GYMCHAOS_BENCH_STAND bench={bench01} pos={transform.position}", this);
    }

    private void HandleSeated()
    {
        if (ReadInteractPressed())
        {
            StandUpFromBench();
            return;
        }
        if (ReadExerciseStartPressed() && CanDealNearby)
        {
            GymDealerShopUI.Open(this);
            return;
        }
        // Look around while seated; the body keeps facing the path.
        if (!showCursor && IsCursorCaptured)
        {
            Vector2 look = ReadLookInput();
            rotationX = Mathf.Clamp(rotationX - look.y * lookSpeed, -lookXLimit, lookXLimit);
            seatedYaw = Mathf.Clamp(seatedYaw + look.x * lookSpeed, -SeatedYawLimit, SeatedYawLimit);
            transform.rotation = seatedFacing * Quaternion.Euler(0f, seatedYaw, 0f);
            playerCamera.transform.localRotation = Quaternion.Euler(rotationX, 0f, 0f);
        }
        UpdateHands();
    }

#if UNITY_EDITOR
    internal int NearbyBenchForVerification => nearbyBenchIndex;
    internal void RefreshNearbyBenchForVerification()
    {
        nearbyBenchIndex = FindNearbyParkBench();
        nearbyTalkTarget = GymDialogueDirector.Active != null
            ? GymDialogueDirector.Active.FindNearbyTalkTarget(this)
            : null;
        nearbyHomelessTalk = nearbyTalkTarget == null && GymDialogueDirector.Active != null &&
            GymDialogueDirector.Active.FindNearbyHomeless(this) != null;
    }
    internal bool PrefersSitForVerification => PrefersSitOverTalk();
#endif
}
