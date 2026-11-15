using UnityEngine;

/// <summary>
/// Drives the first-save tutorial: which step is live, where the marker points,
/// what proves the step is done, and where a new character wakes up. All of the
/// state lives in <see cref="GymProgressionState"/> so a quit resumes on the
/// same step; the statics at the bottom are the testable half.
/// </summary>
public sealed class GymTutorialDirector : MonoBehaviour
{
    public const float SpawnDistanceFromDoor = 6f;
    public const float WalkDistance = 6f;
    public const float LookDegrees = 120f;
    public const float InsideDistance = 5f;
    public const float SignDistance = 5f;

    private GymExperienceService service;
    private PlayerMovement player;
    private GymTutorialMarker marker;
    private GymTutorialHud hud;

    private int announcedStep = -1;
    private int jumpBaseline;
    private float yawSeen;
    private float lastYaw;
    private Vector3 walkStart;
    private bool spawnApplied;
    private bool wasActive;
    private bool completionAnnounced;

    public static GymTutorialDirector Instance { get; private set; }

    public GymTutorialStep Current =>
        Active ? GymTutorialSteps.Get(Step) : null;

    public int Step => State != null ? State.tutorialStep : 0;

    // The lesson is a local, solo thing: a co-op room leaves both players alone,
    // and the race HUD owns the screen while a race runs.
    public bool Active => IsActive(State) && !GymNetSession.InRoom && !GymRaceDirector.IsRaceActive;

    public bool HasTarget { get; private set; }
    public Vector3 TargetPoint { get; private set; }
    public Vector3 PlayerPosition => player != null ? player.transform.position : TargetPoint;

    private GymProgressionState State => service != null ? service.State : null;

    public static GymTutorialDirector EnsureCreated(PlayerMovement targetPlayer)
    {
        if (Instance != null)
        {
            if (targetPlayer != null)
            {
                Instance.player = targetPlayer;
            }
            return Instance;
        }
        GameObject root = new GameObject("Gym Tutorial");
        Instance = root.AddComponent<GymTutorialDirector>();
        Instance.player = targetPlayer;
        Instance.marker = GymTutorialMarker.Create(root.transform);
        Instance.hud = root.AddComponent<GymTutorialHud>();
        Instance.hud.Bind(Instance);
        Instance.service = GymExperienceService.Active;
        Debug.Log("GYMCHAOS_TUTORIAL_READY", root);
        return Instance;
    }

    #region testable state

    /// <summary>Arms the tutorial, but only for a character that has never played.</summary>
    public static void ArmForNewSave(GymProgressionState state, bool isNewSave)
    {
        if (state == null || !isNewSave)
        {
            return;
        }
        if (state.tutorialStep == 0 && state.totalExperience == 0 && state.raceStarts == 0)
        {
            state.tutorialStep = 1;
        }
    }

    public static bool IsActive(GymProgressionState state) =>
        state != null && GymTutorialSteps.IsActiveStep(state.tutorialStep);

    public static void SkipAll(GymProgressionState state)
    {
        if (state == null)
        {
            return;
        }
        state.tutorialStep = GymTutorialSteps.Done;
    }

    /// <summary>True while the easy tutorial race is the thing the player is on.</summary>
    public static bool TutorialRacePendingFor(GymProgressionState state) =>
        state != null && !state.tutorialRaceDone && state.tutorialStep == GymTutorialSteps.RaceStep;

    public static void MarkTutorialRaceFinished(GymProgressionState state)
    {
        if (state == null)
        {
            return;
        }
        state.tutorialRaceDone = true;
        if (state.tutorialStep == GymTutorialSteps.RaceStep)
        {
            state.tutorialStep = GymTutorialSteps.Done;
        }
    }

    /// <summary>
    /// Advances only when the signal belongs to the current step: a stray
    /// signal from elsewhere must never skip a lesson.
    /// </summary>
    public static bool CompleteStepFor(GymProgressionState state, GymTutorialSignal signal)
    {
        if (!IsActive(state) || signal == GymTutorialSignal.None)
        {
            return false;
        }
        GymTutorialStep step = GymTutorialSteps.Get(state.tutorialStep);
        if (step.Signal != signal)
        {
            return false;
        }
        if (state.tutorialStep == GymTutorialSteps.RaceStep && !state.tutorialRaceDone)
        {
            return false;
        }
        state.tutorialStep = Mathf.Min(state.tutorialStep + 1, GymTutorialSteps.Done);
        return true;
    }

    #endregion

    private void Update()
    {
        if (service == null)
        {
            service = GymExperienceService.Active;
        }
        if (service == null)
        {
            return;
        }
        if (player == null)
        {
            player = service.Player;
        }
        if (player == null)
        {
            return;
        }
        if (!Active)
        {
            if (marker != null) marker.Hide();
            HasTarget = false;
            // A finished tutorial race ends the chain outside the step poller; the
            // card still has to appear, but only for someone who was in the lesson.
            // Only a finished race earns the card: a skip must not pop it up.
            if (wasActive && !completionAnnounced && State.tutorialRaceDone &&
                State.tutorialStep >= GymTutorialSteps.Done)
            {
                completionAnnounced = true;
                GymProgressionJournal.CompletionPending = true;
                GymHud.Active?.PushNotice("TUTORIAL COMPLETE  —  the gym is yours.", 5f, 2);
                Debug.Log("GYMCHAOS_TUTORIAL_COMPLETE", this);
            }
            return;
        }
        wasActive = true;

        GymTutorialStep step = Current;
        if (announcedStep != Step)
        {
            BeginStep(step);
        }
        if (!spawnApplied && Step <= 4)
        {
            spawnApplied = true;
            ApplySpawn();
        }

        HasTarget = TryResolveTarget(step.Target, out Vector3 point);
        if (HasTarget && marker != null)
        {
            marker.Show(point, player.transform.position);
        }
        else if (marker != null)
        {
            marker.Hide();
        }

        Poll(step);
    }

    private void BeginStep(GymTutorialStep step)
    {
        announcedStep = Step;
        jumpBaseline = player.JumpSoundCountForVerification;
        yawSeen = 0f;
        lastYaw = player.transform.eulerAngles.y;
        walkStart = player.transform.position;
        GymHud.Active?.PushNotice(step.Title + "  —  " + step.Key, 3.2f, 1);
        GymProgressionJournal.TeaserPending = string.Empty;
        Debug.Log($"GYMCHAOS_TUTORIAL_STEP step={Step} title={step.Title} key={step.Key}", this);
    }

    private void Poll(GymTutorialStep step)
    {
        switch (step.Signal)
        {
            case GymTutorialSignal.LookedAround:
            {
                float yaw = player.transform.eulerAngles.y;
                float delta = Mathf.DeltaAngle(lastYaw, yaw);
                lastYaw = yaw;
                yawSeen += Mathf.Abs(delta);
                if (yawSeen >= LookDegrees)
                {
                    Complete(GymTutorialSignal.LookedAround);
                }
                break;
            }
            case GymTutorialSignal.MovedAround:
            {
                Vector3 offset = player.transform.position - walkStart;
                offset.y = 0f;
                if (offset.magnitude >= WalkDistance)
                {
                    Complete(GymTutorialSignal.MovedAround);
                }
                break;
            }
            case GymTutorialSignal.SprintAndJump:
            {
                bool sprinted = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
                bool jumped = player.JumpSoundCountForVerification > jumpBaseline;
                if (sprinted && jumped)
                {
                    Complete(GymTutorialSignal.SprintAndJump);
                }
                break;
            }
            case GymTutorialSignal.WentInside:
            {
                GymDoorway door = GymDoorway.Instance;
                if (door != null &&
                    Vector3.Distance(player.transform.position, door.InteriorPoint) < InsideDistance)
                {
                    Complete(GymTutorialSignal.WentInside);
                }
                break;
            }
            case GymTutorialSignal.LockerReady:
            {
                if (State.lockerPrepCompleted)
                {
                    Complete(GymTutorialSignal.LockerReady);
                }
                break;
            }
            case GymTutorialSignal.FirstSet:
            {
                if (State.dailyProgress != null && State.dailyProgress[0] > 0)
                {
                    Complete(GymTutorialSignal.FirstSet);
                }
                break;
            }
            case GymTutorialSignal.Collected:
            {
                if (State.loyaltyStamps > 0)
                {
                    Complete(GymTutorialSignal.Collected);
                }
                break;
            }
            case GymTutorialSignal.JournalSeen:
            {
                if (GymProgressionJournal.WasSeen)
                {
                    Complete(GymTutorialSignal.JournalSeen);
                }
                break;
            }
            case GymTutorialSignal.MetBodybuilder:
            {
                if (State.dailyProgress != null && State.dailyProgress[1] > 0)
                {
                    Complete(GymTutorialSignal.MetBodybuilder);
                }
                break;
            }
            case GymTutorialSignal.ReachedRaceSign:
            {
                GymRaceSign sign = GymRaceSign.Instance;
                bool atSign = sign != null &&
                    Vector3.Distance(player.transform.position, sign.transform.position) < SignDistance;
                bool racing = GymRaceDirector.Instance != null && GymRaceDirector.Instance.RaceActive;
                if (atSign || racing)
                {
                    Complete(GymTutorialSignal.ReachedRaceSign);
                }
                break;
            }
            case GymTutorialSignal.RaceFinished:
            {
                if (State.tutorialRaceDone)
                {
                    Complete(GymTutorialSignal.RaceFinished);
                }
                break;
            }
        }
    }

    private void Complete(GymTutorialSignal signal)
    {
        int finished = Step;
        if (!CompleteStepFor(State, signal))
        {
            return;
        }
        int reward = GymTutorialSteps.RewardFor(finished);
        if (reward > 0 && service != null)
        {
            service.AwardExperience(reward, "tutorial: " + GymTutorialSteps.Get(finished).Title,
                "tutorial-step-" + finished);
        }
        if (State.tutorialStep >= GymTutorialSteps.Done)
        {
            GymProgressionJournal.CompletionPending = true;
            GymHud.Active?.PushNotice("TUTORIAL COMPLETE  —  the gym is yours.", 5f, 2);
            Debug.Log("GYMCHAOS_TUTORIAL_COMPLETE", this);
        }
        else
        {
            GymTutorialStep next = GymTutorialSteps.Get(State.tutorialStep);
            GymProgressionJournal.TeaserPending = next.Title + "  —  " + next.Key;
            // The teaser is the reward that pulls the player to the next lesson.
            GymHud.Active?.PushNotice("NEXT:  " + GymProgressionJournal.TeaserPending, 3.2f, 1);
        }
        if (GymSessionService.SessionMode)
        {
            GymSessionService.RequestSave("tutorial");
        }
        announcedStep = -1;
    }

    /// <summary>Where a brand new character wakes up: outside the gym doors, facing them.</summary>
    public static bool TryGetSpawnPose(out Vector3 position, out Quaternion rotation)
    {
        position = Vector3.zero;
        rotation = Quaternion.identity;
        GymDoorway door = GymDoorway.Instance;
        if (door == null)
        {
            return false;
        }
        Vector3 centre = door.DoorCenter;
        Vector3 outward = door.ExteriorPoint - centre;
        outward.y = 0f;
        if (outward.sqrMagnitude < 0.01f)
        {
            outward = Vector3.back;
        }
        outward.Normalize();
        position = centre + outward * SpawnDistanceFromDoor;
        position.y = centre.y;
        rotation = Quaternion.LookRotation(-outward, Vector3.up);
        return true;
    }

    private void ApplySpawn()
    {
        if (player == null || !TryGetSpawnPose(out Vector3 position, out Quaternion rotation))
        {
            return;
        }
        Vector3 current = player.transform.position;
        position.y = current.y;
        if (Vector3.Distance(current, position) < 1f)
        {
            return;
        }
        CharacterController controller = player.GetComponent<CharacterController>();
        if (controller != null)
        {
            controller.enabled = false;
            player.transform.SetPositionAndRotation(position, rotation);
            controller.enabled = true;
        }
        else
        {
            player.transform.SetPositionAndRotation(position, rotation);
        }
        Debug.Log($"GYMCHAOS_TUTORIAL_SPAWN position={position} facing={rotation.eulerAngles.y:F0}", this);
    }

    /// <summary>Anchor for the current step's target; false when the step has none in the scene.</summary>
    public bool TryResolveTarget(GymTutorialTarget kind, out Vector3 point)
    {
        point = Vector3.zero;
        switch (kind)
        {
            case GymTutorialTarget.Entrance:
            {
                GymDoorway door = GymDoorway.Instance;
                if (door == null)
                {
                    return false;
                }
                bool inside = Vector3.Distance(player.transform.position, door.InteriorPoint) <
                    Vector3.Distance(player.transform.position, door.ExteriorPoint);
                point = inside ? door.InteriorPoint : door.ExteriorPoint;
                return true;
            }
            case GymTutorialTarget.Locker:
            {
                GymBackRoomInteractable[] rooms =
                    Object.FindObjectsByType<GymBackRoomInteractable>(FindObjectsSortMode.None);
                GymBackRoomInteractable best = null;
                float bestDistance = float.MaxValue;
                for (int i = 0; i < rooms.Length; i++)
                {
                    GymBackRoomInteractable room = rooms[i];
                    if (room == null) continue;
                    string name = room.DisplayName != null ? room.DisplayName.ToLowerInvariant() : string.Empty;
                    float distance = Vector3.Distance(player.transform.position, room.transform.position);
                    if (name.Contains("locker") || name.Contains("changing") || name.Contains("outfit"))
                    {
                        distance -= 40f;
                    }
                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        best = room;
                    }
                }
                if (best == null)
                {
                    return false;
                }
                point = best.transform.position;
                return true;
            }
            case GymTutorialTarget.Station:
            {
                GymExerciseStation station =
                    GymExerciseStation.FindClosest(player.transform.position, 60f);
                if (station == null)
                {
                    return false;
                }
                point = station.PlayerPosition;
                return true;
            }
            case GymTutorialTarget.Counter:
            {
                GymProteinShopUI shop = Object.FindFirstObjectByType<GymProteinShopUI>();
                if (shop == null)
                {
                    return false;
                }
                point = shop.transform.position;
                return true;
            }
            case GymTutorialTarget.Bodybuilder:
            {
                GymVisitorAgent[] visitors = Object.FindObjectsByType<GymVisitorAgent>(FindObjectsSortMode.None);
                GymVisitorAgent best = null;
                float bestDistance = float.MaxValue;
                for (int i = 0; i < visitors.Length; i++)
                {
                    if (visitors[i] == null) continue;
                    float distance = Vector3.Distance(player.transform.position, visitors[i].transform.position);
                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        best = visitors[i];
                    }
                }
                if (best == null)
                {
                    return false;
                }
                point = best.transform.position;
                return true;
            }
            case GymTutorialTarget.RaceSign:
            {
                GymRaceSign sign = GymRaceSign.Instance;
                if (sign == null)
                {
                    return false;
                }
                point = sign.StandPoint;
                return true;
            }
            default:
                return false;
        }
    }

    /// <summary>Story beats the journal reads to tease the next lesson.</summary>
    public static string NextTeaser(GymProgressionState state)
    {
        if (!IsActive(state))
        {
            return string.Empty;
        }
        GymTutorialStep step = GymTutorialSteps.Get(state.tutorialStep);
        return step.Title;
    }
}
