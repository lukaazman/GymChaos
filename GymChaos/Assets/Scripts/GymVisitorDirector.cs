using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Owns the unique enemy pool, daily visit quota and short-day schedule. A
/// visitor is one prebuilt EnemyFighter instance that is disabled outside the
/// room between visits, so the same stable identity can never be duplicated.
/// </summary>
[DefaultExecutionOrder(-20)]
public sealed class GymVisitorDirector : MonoBehaviour
{
    private const float VisitorEntryTimeoutSeconds = 42f;
    private sealed class VisitorRecord
    {
        public EnemyFighter fighter;
        public GymVisitorAgent agent;
        public int visitsToday;
        public int workoutsToday;
        public bool active;
        public float activeSince;
        public float leaveAfter;
        public float entryStartedAt;
        public bool visitInProgress;
        public bool workoutInProgress;
        public bool suspendedForCombat;
        public bool deactivationDeferredLogged;
        public float[] entryTimes = new float[2];
        public float[] workoutTimes = new float[2];
        public int observedWorkoutVersion;
        public GymVisitorVehicle vehicle;
        public bool waitingForVehicle;
        public bool vehicleDepartureStarted;
        public bool walkingToVehicle;
        public bool queuedForcedDeparture;
        public float nextEligibleRealtime;
    }

    private static readonly BodybuilderIdentity[] EligibleIdentities =
    {
        BodybuilderIdentity.Cbum,
        BodybuilderIdentity.Zyzz,
        BodybuilderIdentity.Arnold,
        BodybuilderIdentity.JayCutler,
        BodybuilderIdentity.Goku
    };

    [SerializeField] private int deterministicSeed = -1;
    [SerializeField, Min(12f)] private float minimumVisitSeconds = 25f;
    [SerializeField, Min(18f)] private float maximumVisitSeconds = 36f;
    [SerializeField, Range(0f, 1f)] private float firstScheduleOffset = 0.055f;
    [SerializeField, Range(0f, 1f)] private float secondScheduleStart = 0.62f;
    [SerializeField, Range(0f, 1f)] private float minimumWorkoutDelay = 0.06f;
    [SerializeField, Range(0f, 1f)] private float maximumWorkoutDelay = 0.13f;
    [SerializeField, Min(20f)] private float minimumReturnCooldownSeconds = 45f;
    [SerializeField, Min(30f)] private float maximumReturnCooldownSeconds = 80f;

    private readonly List<VisitorRecord> records = new List<VisitorRecord>();
    private System.Random random;
    private GymTimeOfDay timeOfDay;
    private GymDoorway doorway;
    private PlayerMovement player;
    private int lastDay = -1;
    private bool initialized;
    private bool departureDrainMode;

    public static GymVisitorDirector Instance { get; private set; }
    public int EligibleEnemyCount => records.Count;
    public int ActiveVisitorCount
    {
        get
        {
            int count = 0;
            for (int i = 0; i < records.Count; i++)
            {
                if (records[i].active && records[i].agent != null && records[i].agent.IsInsideGym)
                {
                    count++;
                }
            }
            return count;
        }
    }

    public static GymVisitorDirector CreateForScene(
        PlayerMovement targetPlayer, int deterministicSeedOverride = int.MinValue)
    {
        GymVisitorDirector existing = FindAnyObjectByType<GymVisitorDirector>();
        if (existing != null)
        {
            return existing;
        }

        GameObject directorObject = new GameObject("Gym Visitor Director");
        GymVisitorDirector director = directorObject.AddComponent<GymVisitorDirector>();
        if (deterministicSeedOverride != int.MinValue)
        {
            director.deterministicSeed = deterministicSeedOverride;
        }
        director.Initialize(targetPlayer);
        return director;
    }

    public void SetDeterministicSeed(int seed)
    {
        deterministicSeed = seed;
        random = new System.Random(seed);
        if (initialized)
        {
            BuildDaySchedule();
        }
    }

    public int GetVisitCount(BodybuilderIdentity identity)
    {
        VisitorRecord record = FindRecord(identity);
        return record != null ? record.visitsToday : 0;
    }

    public int GetWorkoutCount(BodybuilderIdentity identity)
    {
        VisitorRecord record = FindRecord(identity);
        return record != null ? record.workoutsToday : 0;
    }

#if UNITY_EDITOR
    public float MinimumReturnCooldownForVerification => minimumReturnCooldownSeconds;
    public float MaximumReturnCooldownForVerification => maximumReturnCooldownSeconds;

    public void SuspendVisitorSimulationForVerification()
    {
        if (!initialized)
        {
            return;
        }

        enabled = false;
        for (int i = 0; i < records.Count; i++)
        {
            VisitorRecord record = records[i];
            record.fighter.gameObject.SetActive(true);
            record.agent.MarkInitialInside();
            record.active = true;
            record.visitInProgress = false;
            record.workoutInProgress = false;
            record.suspendedForCombat = true;
        }

        Debug.Log(
            $"GYMCHAOS_VISITOR_VERIFICATION_SUSPENDED active={records.Count}",
            this);
    }

    public bool BeginEntryForVerification(out EnemyFighter fighter)
    {
        fighter = null;
        for (int pass = 0; pass < 2 && fighter == null; pass++)
        {
            for (int i = 0; i < records.Count; i++)
            {
                VisitorRecord record = records[i];
                if (record.suspendedForCombat || record.active || record.visitsToday >= 2)
                {
                    continue;
                }
                if (pass == 0 && record.fighter != null &&
                    record.fighter.Identity == BodybuilderIdentity.Goku)
                {
                    continue;
                }

                if (ActivateScheduled(record, true))
                {
                    fighter = record.fighter;
                    break;
                }
            }
        }

        return fighter != null;
    }

    public bool BeginWorkoutForVerification(
        out EnemyFighter fighter, out GymExerciseStation station)
    {
        fighter = null;
        station = null;
        for (int pass = 0; pass < 2 && fighter == null; pass++)
        {
            for (int i = 0; i < records.Count; i++)
            {
                VisitorRecord record = records[i];
                if (record.suspendedForCombat || !record.active || record.visitInProgress ||
                    record.workoutInProgress || record.workoutsToday >= 2 ||
                    record.fighter.IsDead || record.fighter.IsAggressive ||
                    !record.agent.IsInsideGym || record.agent.IsBusy)
                {
                    continue;
                }
                if (pass == 0 && record.fighter.Identity == BodybuilderIdentity.Goku)
                {
                    continue;
                }

                GymExerciseStation candidate = GymExerciseStation.FindClosestSquat(
                    record.fighter.transform.position, 60f);
                if (candidate == null || !record.agent.BeginWorkoutApproach(candidate, 6, 0.65f))
                {
                    continue;
                }

                record.workoutInProgress = true;
                fighter = record.fighter;
                station = candidate;
                return true;
            }
        }

        return false;
    }

    public bool BeginDepartureForVerification(
        out EnemyFighter fighter, out GymVisitorVehicle vehicle)
    {
        fighter = null;
        vehicle = null;
        if (doorway == null) return false;
        for (int i = 0; i < records.Count; i++)
        {
            VisitorRecord record = records[i];
            if (!record.active || record.waitingForVehicle || record.agent == null ||
                !record.agent.IsInsideGym || record.agent.IsBusy) continue;
            EnsureVehicle(record, true);
            fighter = record.fighter;
            vehicle = record.vehicle;
            record.leaveAfter = Time.time;
            record.agent.BeginExit(doorway);
            return true;
        }
        return false;
    }

    public bool BeginZyzzDepartureForVerification(
        out EnemyFighter fighter, out GymVisitorVehicle vehicle)
    {
        fighter = null;
        vehicle = null;
        if (doorway == null) return false;
        VisitorRecord record = FindRecord(BodybuilderIdentity.Zyzz);
        if (record == null || record.fighter == null || record.agent == null) return false;
        EnsureVehicle(record, true);
        record.fighter.gameObject.SetActive(true);
        record.agent.MarkInitialInside();
        record.active = true;
        record.visitInProgress = false;
        record.workoutInProgress = false;
        record.waitingForVehicle = false;
        record.walkingToVehicle = false;
        record.vehicleDepartureStarted = false;
        fighter = record.fighter;
        vehicle = record.vehicle;
        record.agent.BeginExit(doorway);
        return true;
    }

    public bool BeginGokuDepartureForVerification(
        out EnemyFighter fighter, out GymVisitorVehicle vehicle)
    {
        fighter = null;
        vehicle = null;
        if (doorway == null) return false;
        VisitorRecord record = FindRecord(BodybuilderIdentity.Goku);
        if (record == null || record.fighter == null || record.agent == null) return false;
        EnsureVehicle(record, true);
        record.fighter.gameObject.SetActive(true);
        record.agent.MarkInitialInside();
        record.active = true;
        record.visitInProgress = false;
        record.workoutInProgress = false;
        record.waitingForVehicle = false;
        record.walkingToVehicle = false;
        record.vehicleDepartureStarted = false;
        fighter = record.fighter;
        vehicle = record.vehicle;
        record.agent.BeginExit(doorway);
        return true;
    }

    public int BeginAllDeparturesForVerification(
        List<EnemyFighter> fighters, List<GymVisitorVehicle> vehicles)
    {
        if (doorway == null || fighters == null || vehicles == null)
        {
            return 0;
        }
        fighters.Clear();
        vehicles.Clear();
        departureDrainMode = true;
        for (int i = 0; i < records.Count; i++)
        {
            VisitorRecord record = records[i];
            if (record == null || record.fighter == null || record.agent == null)
            {
                continue;
            }
            EnsureVehicle(record, true);
            record.fighter.gameObject.SetActive(true);
            record.agent.MarkInitialInside();
            record.active = true;
            record.visitInProgress = false;
            record.workoutInProgress = false;
            record.suspendedForCombat = false;
            record.waitingForVehicle = false;
            record.walkingToVehicle = false;
            record.vehicleDepartureStarted = false;
            record.queuedForcedDeparture = true;
            fighters.Add(record.fighter);
            vehicles.Add(record.vehicle);
        }
        return fighters.Count;
    }

#endif

    private void Initialize(PlayerMovement targetPlayer)
    {
        player = targetPlayer;
        timeOfDay = GymTimeOfDay.Instance != null
            ? GymTimeOfDay.Instance
            : FindAnyObjectByType<GymTimeOfDay>();
        doorway = GymDoorway.Instance != null
            ? GymDoorway.Instance
            : FindAnyObjectByType<GymDoorway>();
        random = deterministicSeed >= 0
            ? new System.Random(deterministicSeed)
            : new System.Random(Environment.TickCount ^ Time.frameCount);

        CollectUniqueEnemyPool();
        if (timeOfDay != null)
        {
            timeOfDay.DayChanged += HandleDayChanged;
            lastDay = timeOfDay.CurrentDay;
        }

        BuildDaySchedule();
        InitializeRoster();
        LogSquatStationCoverage();
        initialized = true;
    }

    private void CollectUniqueEnemyPool()
    {
        records.Clear();
        EnemyFighter[] fighters = FindObjectsByType<EnemyFighter>(FindObjectsInactive.Include);
        for (int identityIndex = 0; identityIndex < EligibleIdentities.Length; identityIndex++)
        {
            BodybuilderIdentity identity = EligibleIdentities[identityIndex];
            EnemyFighter match = null;
            for (int fighterIndex = 0; fighterIndex < fighters.Length; fighterIndex++)
            {
                EnemyFighter fighter = fighters[fighterIndex];
                if (fighter != null && fighter.Identity == identity)
                {
                    if (match == null)
                    {
                        match = fighter;
                    }
                    else
                    {
                        // A duplicate stable identity is never allowed to be
                        // active. Keep the first runtime record and quarantine
                        // all additional instances before scheduling starts.
                        fighter.gameObject.SetActive(false);
                        Debug.LogError(
                            $"GYMCHAOS_VISITOR_DUPLICATE identity={identity} " +
                            $"quarantined={fighter.name}",
                            fighter);
                    }
                }
            }

            if (match == null)
            {
                Debug.LogWarning($"GYMCHAOS_VISITOR_MISSING identity={identity}", this);
                continue;
            }

            GymVisitorAgent agent = match.GetComponent<GymVisitorAgent>();
            if (agent == null)
            {
                agent = match.gameObject.AddComponent<GymVisitorAgent>();
            }
            agent.Configure(match);
            match.AttachVisitorAgent(agent);
            records.Add(new VisitorRecord
            {
                fighter = match,
                agent = agent
            });
        }
    }

    private void BuildDaySchedule()
    {
        float now = timeOfDay != null ? timeOfDay.Time01 : 0.24f;
        List<float> usedEntryTimes = new List<float>();
        List<float> usedWorkoutTimes = new List<float>();
        for (int i = 0; i < records.Count; i++)
        {
            VisitorRecord record = records[i];
            float firstEntry = now + firstScheduleOffset + i * 0.038f + RandomRange(-0.012f, 0.012f);
            float secondEntry = secondScheduleStart + i * 0.041f + RandomRange(-0.014f, 0.014f);
            record.entryTimes[0] = MakeUniqueTime(firstEntry, usedEntryTimes);
            usedEntryTimes.Add(record.entryTimes[0]);
            record.entryTimes[1] = MakeUniqueTime(secondEntry, usedEntryTimes);
            usedEntryTimes.Add(record.entryTimes[1]);

            record.workoutTimes[0] = MakeUniqueTime(
                record.entryTimes[0] + RandomRange(minimumWorkoutDelay, maximumWorkoutDelay),
                usedWorkoutTimes);
            usedWorkoutTimes.Add(record.workoutTimes[0]);
            record.workoutTimes[1] = MakeUniqueTime(
                record.entryTimes[1] + RandomRange(minimumWorkoutDelay, maximumWorkoutDelay),
                usedWorkoutTimes);
            usedWorkoutTimes.Add(record.workoutTimes[1]);

            Debug.Log(
                $"GYMCHAOS_SCHEDULE enemy={record.fighter.Identity} " +
                $"entry0={record.entryTimes[0]:F3} entry1={record.entryTimes[1]:F3} " +
                $"workout0={record.workoutTimes[0]:F3} workout1={record.workoutTimes[1]:F3}",
                this);
        }
    }

    private void InitializeRoster()
    {
        if (records.Count == 0)
        {
            return;
        }

        List<int> order = new List<int>();
        for (int i = 0; i < records.Count; i++)
        {
            order.Add(i);
        }
        for (int i = order.Count - 1; i > 0; i--)
        {
            int swapIndex = random.Next(i + 1);
            int swap = order[i];
            order[i] = order[swapIndex];
            order[swapIndex] = swap;
        }

        int initialCount = records.Count >= 2
            ? random.Next(2, records.Count + 1)
            : records.Count;
        for (int i = 0; i < records.Count; i++)
        {
            VisitorRecord record = records[i];
            record.active = false;
            record.visitsToday = 0;
            record.workoutsToday = 0;
            record.visitInProgress = false;
            record.workoutInProgress = false;
            record.suspendedForCombat = false;
            record.agent.MarkDormant();
            record.fighter.gameObject.SetActive(false);
        }

        for (int selected = 0; selected < initialCount; selected++)
        {
            ActivateInitial(records[order[selected]]);
        }

        Debug.Log(
            $"GYMCHAOS_VISITOR_ROSTER initial={initialCount} eligible={records.Count} " +
            $"active={ActiveVisitorCount}",
            this);
    }

    private void ActivateInitial(VisitorRecord record)
    {
        EnsureVehicle(record, true);
        record.fighter.gameObject.SetActive(true);
        record.agent.MarkInitialInside();
        record.active = true;
        record.visitsToday = 1;
        record.workoutsToday = 0;
        record.activeSince = Time.time;
        record.leaveAfter = Time.time + RandomRange(minimumVisitSeconds, maximumVisitSeconds);
        record.entryStartedAt = 0f;
        record.visitInProgress = false;
        record.workoutInProgress = false;
        record.observedWorkoutVersion = record.agent.CompletedWorkoutVersion;
    }

    private void Update()
    {
        if (!initialized || timeOfDay == null || doorway == null)
        {
            return;
        }

        if (lastDay != timeOfDay.CurrentDay)
        {
            HandleDayChanged(timeOfDay.CurrentDay);
        }

        float now = timeOfDay.Time01;
        if (!departureDrainMode)
        {
            EnsureMinimumVisitors();
        }
        int insideCount = ActiveVisitorCount;
        for (int i = 0; i < records.Count; i++)
        {
            VisitorRecord record = records[i];
            if (record.suspendedForCombat)
            {
                continue;
            }

            if (record.fighter.IsDead || record.fighter.IsAggressive)
            {
                // Cancel every visitor state, including an active squat. A
                // dead fighter does not enter EnemyFighter.FixedUpdate, so
                // relying on the normal state machine here can leave the
                // station occupied and the bar parented to a despawning body.
                if (record.agent != null)
                {
                    record.agent.CancelForCombat();
                }
                record.visitInProgress = false;
                record.workoutInProgress = false;
                record.active = false;
                record.suspendedForCombat = true;
                continue;
            }

            if (record.active && record.visitInProgress && !record.waitingForVehicle)
            {
                if (record.agent.HasEnteredGym)
                {
                    record.visitInProgress = false;
                    record.visitsToday = Mathf.Min(2, record.visitsToday + 1);
                    record.activeSince = Time.time;
                    record.leaveAfter = Time.time +
                        RandomRange(minimumVisitSeconds, maximumVisitSeconds);
                    Debug.Log(
                        $"GYMCHAOS_VISITOR_ENTERED_CONFIRMED enemy={record.fighter.Identity} " +
                        $"visit={record.visitsToday}",
                        this);
                    Debug.Log(
                        $"GYMCHAOS_VISITOR_VISIT_START enemy={record.fighter.Identity} " +
                        $"visit={record.visitsToday}",
                        this);
                }
                else if (Time.time - record.entryStartedAt > VisitorEntryTimeoutSeconds)
                {
                    // A failed incoming route must still return through the
                    // authored doorway. Never hide an object at the room
                    // boundary because a navigation timeout fired.
                    if (record.agent.IsEntryPending)
                    {
                        record.agent.AbortEntryAndReturnThroughDoor(doorway);
                        record.visitInProgress = false;
                        record.leaveAfter = float.PositiveInfinity;
                    }
                    else
                    {
                        // This fallback is only valid for a visitor that never
                        // completed entry and is already dormant outside.
                        record.agent.CancelPendingVisit();
                        if (record.agent.CanDeactivate)
                        {
                            record.active = false;
                            record.visitInProgress = false;
                            record.fighter.gameObject.SetActive(false);
                        }
                    }
                    Debug.LogWarning(
                        $"GYMCHAOS_VISITOR_ENTRY_ABORTED enemy={record.fighter.Identity} " +
                        "reason=timeout_returning_through_door",
                        this);
                    continue;
                }
            }

            if (!record.active)
            {
                if (!departureDrainMode && record.visitsToday < 2 &&
                    IsDue(now, record.entryTimes[record.visitsToday]))
                {
                    ActivateScheduled(record);
                }
                continue;
            }

            // A dormant agent can retain its completed-exit marker while its
            // next vehicle is still approaching. Do not interpret that stale
            // marker as a new departure and cancel DriveIn.
            if (record.waitingForVehicle)
            {
                continue;
            }

            if (record.agent.HasLeftGym)
            {
                FinishVisit(record);
                continue;
            }

            if (!record.agent.IsInsideGym)
            {
                continue;
            }

            if (record.agent.CompletedWorkoutVersion != record.observedWorkoutVersion)
            {
                record.observedWorkoutVersion = record.agent.CompletedWorkoutVersion;
                if (!record.agent.IsWorkoutActive)
                {
                    // The squat controller has already released the rack and
                    // returned the bar. Give the visitor a real free-roam
                    // window before the visit can exit or schedule another
                    // workout; this prevents a completed enemy from remaining
                    // frozen on the cage's interaction point.
                    record.workoutInProgress = false;
                    record.leaveAfter = Mathf.Max(record.leaveAfter, Time.time + 2.25f);
                    Debug.Log(
                        $"GYMCHAOS_VISITOR_WORKOUT_RELEASED enemy={record.fighter.Identity} " +
                        $"freeRoamUntil={Time.time + 2.25f:0.00}",
                        this);
                }
            }

            if (record.workoutInProgress)
            {
                if (record.agent.IsWorkoutActive)
                {
                    record.workoutInProgress = false;
                    record.workoutsToday = Mathf.Min(2, record.workoutsToday + 1);
                    Debug.Log(
                        $"GYMCHAOS_WORKOUT_START_CONFIRMED enemy={record.fighter.Identity} " +
                        $"workout={record.workoutsToday}",
                        this);
                }
                else if (!record.agent.IsBusy)
                {
                    // The station may have become unavailable while the
                    // visitor was walking to it. Retry the same slot later.
                    record.workoutInProgress = false;
                }
            }

            if (!departureDrainMode && !record.agent.IsBusy &&
                !record.fighter.IsOnTreadmill &&
                !record.workoutInProgress && record.workoutsToday < 2 &&
                IsDue(now, record.workoutTimes[record.workoutsToday]))
            {
                TryStartWorkout(record);
            }

            if ((record.queuedForcedDeparture || Time.time >= record.leaveAfter) &&
                !record.agent.IsBusy &&
                !record.fighter.IsOnTreadmill)
            {
                if (insideCount <= 2 && !record.queuedForcedDeparture)
                {
                    EnsureThreeVisitors();
                    insideCount = ActiveVisitorCount;
                }

                if ((insideCount > 2 || record.queuedForcedDeparture) &&
                    !IsDoorTraversalBusy(record))
                {
                    record.queuedForcedDeparture = false;
                    record.agent.BeginExit(doorway);
                    insideCount = ActiveVisitorCount;
                }
            }
        }
    }

    private void EnsureMinimumVisitors()
    {
        if (CountActiveOrEnteringVisitors() >= 2)
        {
            return;
        }

        for (int i = 0; i < records.Count && CountActiveOrEnteringVisitors() < 2; i++)
        {
            VisitorRecord record = records[i];
            if (CanActivateNow(record, false))
            {
                ActivateScheduled(record, true);
            }
        }
    }

    private void EnsureThreeVisitors()
    {
        if (CountActiveOrEnteringVisitors() >= 3)
        {
            return;
        }

        for (int i = 0; i < records.Count; i++)
        {
            VisitorRecord record = records[i];
            if (CanActivateNow(record, false))
            {
                ActivateScheduled(record, true);
                return;
            }
        }
    }

    private int CountActiveOrEnteringVisitors()
    {
        int count = 0;
        for (int i = 0; i < records.Count; i++)
        {
            VisitorRecord record = records[i];
            if (record.active && record.agent != null &&
                record.agent.State != GymVisitorAgent.VisitorState.ExitingDoor &&
                record.agent.State != GymVisitorAgent.VisitorState.LeavingGym)
            {
                count++;
            }
        }

        return count;
    }

    private bool IsArrivalSequenceBusy(VisitorRecord ignoredRecord = null)
    {
        for (int i = 0; i < records.Count; i++)
        {
            VisitorRecord other = records[i];
            if (other == null || other == ignoredRecord || !other.active)
            {
                continue;
            }

            if (other.waitingForVehicle ||
                (other.agent != null && other.agent.IsEntryPending))
            {
                return true;
            }
        }

        return false;
    }

    private bool IsDoorTraversalBusy(VisitorRecord ignoredRecord = null)
    {
        for (int i = 0; i < records.Count; i++)
        {
            VisitorRecord other = records[i];
            if (other == null || other == ignoredRecord || !other.active ||
                other.agent == null)
            {
                continue;
            }

            GymVisitorAgent.VisitorState otherState = other.agent.State;
            if (otherState == GymVisitorAgent.VisitorState.ExitingDoor ||
                otherState == GymVisitorAgent.VisitorState.LeavingGym ||
                otherState == GymVisitorAgent.VisitorState.EnteringDoor ||
                otherState == GymVisitorAgent.VisitorState.EnteringRoom ||
                other.agent.IsUsingSharedParkingConnector)
            {
                return true;
            }
        }

        return false;
    }

    private bool ActivateScheduled(VisitorRecord record, bool forced = false)
    {
        if (record == null || record.active || record.visitsToday >= 2 || doorway == null)
        {
            return false;
        }
        if (!forced && Time.unscaledTime < record.nextEligibleRealtime)
        {
            return false;
        }
        EnsureVehicle(record, false);
        record.active = true;
        record.waitingForVehicle = true;
        record.visitInProgress = true;
        record.entryStartedAt = Time.time;
        record.leaveAfter = float.PositiveInfinity;
        record.vehicleDepartureStarted = false;
        record.walkingToVehicle = false;
        record.queuedForcedDeparture = false;
        if (record.vehicle.IsCloud)
        {
            record.fighter.gameObject.SetActive(true);
            record.vehicle.MountRider(record.fighter);
        }
        else
        {
            record.fighter.gameObject.SetActive(false);
        }
        record.vehicle.DriveIn(() => CompleteVehicleArrival(record, forced));
        Debug.Log($"GYMCHAOS_VEHICLE_ARRIVAL_STARTED enemy={record.fighter.Identity}", this);
        return true;
    }

    private void CompleteVehicleArrival(VisitorRecord record, bool forced)
    {
        if (record == null || record.fighter == null || record.agent == null) return;
        bool stagedOutside = record.vehicle != null;
        Vector3 outside = record.vehicle != null
            ? record.vehicle.PassengerPoint
            : ChooseVisitorArrivalStage(record, out stagedOutside);
        Vector3 approach = Vector3.ProjectOnPlane(
            doorway.ExteriorPoint - outside, Vector3.up);
        if (approach.sqrMagnitude < 0.01f)
        {
            approach = Vector3.ProjectOnPlane(
                doorway.InteriorPoint - outside, Vector3.up);
        }
        Quaternion rotation = approach.sqrMagnitude > 0.01f
            ? Quaternion.LookRotation(approach.normalized, Vector3.up)
            : record.fighter.transform.rotation;
        record.fighter.gameObject.SetActive(true);
        if (record.vehicle != null)
        {
            if (record.vehicle.IsCloud)
                record.vehicle.DismountRider(outside, rotation);
            record.agent.BeginEntryFromVehicle(
                doorway, ChooseRoomTarget(record.fighter.Identity),
                record.vehicle.PassengerPoint, record.vehicle.AislePassengerPoint);
        }
        else
        {
            record.fighter.SetVisitorSpawnPose(outside, rotation);
            Physics.SyncTransforms();
            record.agent.BeginEntry(doorway, ChooseRoomTarget(record.fighter.Identity));
        }
        record.waitingForVehicle = false;
        record.visitInProgress = true;
        record.entryStartedAt = Time.time;
        record.activeSince = Time.time;
        record.leaveAfter = float.PositiveInfinity;
        record.observedWorkoutVersion = record.agent.CompletedWorkoutVersion;
        Debug.Log(
            $"GYMCHAOS_VISITOR_ENTRY_REQUESTED enemy={record.fighter.Identity} " +
            $"visitCandidate={record.visitsToday + 1} forced={forced} " +
            $"stagedOutside={stagedOutside} stage={outside}",
            this);
    }

    private void FinishVisit(VisitorRecord record)
    {
        if (record.agent == null || !record.agent.CanDeactivate)
        {
            // HasLeftGym is only set after the visitor reaches the exterior
            // doorway point. If this invariant is ever violated, keep the
            // unique enemy instance alive instead of creating an in-gym
            // despawn path.
            record.leaveAfter = Time.time + 1.2f;
            if (!record.deactivationDeferredLogged)
            {
                record.deactivationDeferredLogged = true;
                Debug.LogWarning(
                    $"GYMCHAOS_VISITOR_DESPAWN_DEFERRED enemy={record.fighter.Identity} " +
                    $"state={record.agent?.State} reason=door_exit_not_confirmed",
                    this);
            }
            return;
        }

        if (record.agent.IsSquatLifecycleActive)
        {
            // Never deactivate an enemy while its rack reservation or squat
            // controller is still alive. The next frame can finish the
            // workout and the normal visit handoff will retry safely.
            record.leaveAfter = Time.time + 1.2f;
            if (!record.deactivationDeferredLogged)
            {
                record.deactivationDeferredLogged = true;
                Debug.LogWarning(
                    $"GYMCHAOS_VISITOR_DESPAWN_DEFERRED enemy={record.fighter.Identity} " +
                    $"state={record.agent.State}",
                    this);
            }
            return;
        }

        EnsureVehicle(record, true);
        if (!record.walkingToVehicle)
        {
            if (record.agent.BeginVehicleApproach(
                record.vehicle.PassengerPoint, record.vehicle.BoardingReachDistance))
            {
                record.walkingToVehicle = true;
            }
            record.leaveAfter = Time.time + 0.25f;
            return;
        }
        if (!record.agent.HasReachedVehicle)
        {
            record.leaveAfter = Time.time + 0.25f;
            return;
        }
        if (!record.vehicleDepartureStarted)
        {
            record.vehicleDepartureStarted = true;
            record.waitingForVehicle = true;
            record.agent.ReleaseVehicleApproachReservation();
            if (record.vehicle.IsCloud)
                record.vehicle.MountRider(record.fighter);
            else
                record.fighter.gameObject.SetActive(false);
            record.vehicle.DriveOut(() => CompleteVehicleDeparture(record));
        }
    }

    private void CompleteVehicleDeparture(VisitorRecord record)
    {
        if (record == null) return;

        if (record.vehicle != null && record.vehicle.IsCloud)
            record.vehicle.DismountRider(record.fighter.transform.position,
                record.fighter.transform.rotation);
        record.deactivationDeferredLogged = false;
        record.active = false;
        record.visitInProgress = false;
        record.workoutInProgress = false;
        record.suspendedForCombat = false;
        record.waitingForVehicle = false;
        record.vehicleDepartureStarted = false;
        record.walkingToVehicle = false;
        record.queuedForcedDeparture = false;
        record.agent.MarkDormant();
        record.fighter.gameObject.SetActive(false);
        record.nextEligibleRealtime = Time.unscaledTime + RandomRange(
            minimumReturnCooldownSeconds,
            Mathf.Max(minimumReturnCooldownSeconds, maximumReturnCooldownSeconds));
        Debug.Log(
            $"GYMCHAOS_VISITOR_VISIT_END enemy={record.fighter.Identity} " +
            $"visits={record.visitsToday} afterDoorExit={record.agent.HasCompletedDoorExit}",
            this);
    }

    private static bool CanActivateNow(VisitorRecord record, bool forced)
    {
        return record != null && !record.active && !record.suspendedForCombat &&
            record.visitsToday < 2 &&
            (forced || Time.unscaledTime >= record.nextEligibleRealtime);
    }

    private void EnsureVehicle(VisitorRecord record, bool parked)
    {
        if (record == null || record.vehicle != null) return;
        int slot = Mathf.Max(0, records.IndexOf(record));
        record.vehicle = GymVisitorVehicle.Create(
            record.fighter.Identity, slot, player, parked);
    }

    private void TryStartWorkout(VisitorRecord record)
    {
        GymExerciseStation station = GymExerciseStation.FindClosestSquat(
            record.fighter.transform.position, 60f);
        if (station == null)
        {
            // Keep the scheduled slot alive when a player temporarily occupies
            // every squat station; retry shortly without granting a third slot.
            record.workoutTimes[record.workoutsToday] = Mathf.Repeat(
                timeOfDay.Time01 + 0.025f, 1f);
            return;
        }

        int repetitions = random.Next(6, 13);
        float repDuration = RandomRange(0.78f, 1.12f);
        if (!record.agent.BeginWorkoutApproach(station, repetitions, repDuration))
        {
            record.workoutTimes[record.workoutsToday] = Mathf.Repeat(
                timeOfDay.Time01 + 0.025f, 1f);
            return;
        }

        record.workoutInProgress = true;
        Debug.Log(
            $"GYMCHAOS_WORKOUT_REQUESTED enemy={record.fighter.Identity} " +
            $"type=squat station={station.EquipmentName} reps={repetitions}",
            this);
    }

    private Vector3 ChooseVisitorArrivalStage(
        VisitorRecord record, out bool stagedOutside)
    {
        stagedOutside = false;
        Vector3 fallback = doorway != null
            ? doorway.ExteriorPoint
            : record != null && record.fighter != null
                ? record.fighter.transform.position
                : transform.position;
        if (doorway == null || record == null || record.fighter == null ||
            !GymOutdoorBuilder.IsBuilt)
        {
            return fallback;
        }

        Bounds accessible = GymOutdoorBuilder.AccessibleBounds;
        Bounds parking = GymOutdoorBuilder.ParkingBounds;
        if (accessible.size.x < 3f || accessible.size.z < 3f ||
            parking.size.x < 3f || parking.size.z < 3f)
        {
            return fallback;
        }

        Vector3 door = doorway.ExteriorPoint;
        Vector3 pathDirection = Vector3.ProjectOnPlane(
            parking.center - door, Vector3.up);
        if (pathDirection.sqrMagnitude < 0.25f)
        {
            pathDirection = Vector3.ProjectOnPlane(
                accessible.center - door, Vector3.up);
        }
        if (pathDirection.sqrMagnitude < 0.25f)
        {
            return fallback;
        }

        pathDirection.Normalize();
        Vector3 lateral = Vector3.Cross(Vector3.up, pathDirection).normalized;
        int recordIndex = records.IndexOf(record);
        if (recordIndex < 0)
        {
            recordIndex = 0;
        }

        // Keep each stable record on a repeatable, separated path/courtyard
        // slot. The offset is intentionally outside the authored door landing.
        float distanceAlongPath = 4.5f + recordIndex * 2.4f;
        float lateralOffset = (recordIndex & 1) == 0 ? -0.7f : 0.7f;
        Vector3 candidate = door + pathDirection * distanceAlongPath +
            lateral * lateralOffset;
        const float edgeMargin = 0.9f;
        float minX = accessible.min.x + edgeMargin;
        float maxX = accessible.max.x - edgeMargin;
        float minZ = accessible.min.z + edgeMargin;
        float maxZ = accessible.max.z - edgeMargin;
        if (minX >= maxX || minZ >= maxZ)
        {
            return fallback;
        }

        candidate.x = Mathf.Clamp(candidate.x, minX, maxX);
        candidate.z = Mathf.Clamp(candidate.z, minZ, maxZ);
        candidate.y = accessible.center.y;
        if (Vector3.ProjectOnPlane(candidate - door, Vector3.up).sqrMagnitude < 6.25f)
        {
            return fallback;
        }

        stagedOutside = true;
        return candidate;
    }
    private Vector3 ChooseRoomTarget(BodybuilderIdentity identity)
    {
        GameObject floor = GameObject.Find("Rubber Floor");
        if (floor == null || !floor.TryGetComponent(out Renderer renderer))
        {
            return doorway != null ? doorway.InteriorPoint : transform.position;
        }

        Bounds bounds = renderer.bounds;
        float y = bounds.max.y;
        for (int attempt = 0; attempt < 32; attempt++)
        {
            Vector3 candidate = new Vector3(
                RandomRange(bounds.min.x + 2.5f, bounds.max.x - 2.5f),
                y,
                RandomRange(bounds.min.z + 2.5f, bounds.max.z - 2.5f));
            if (doorway != null &&
                Vector3.ProjectOnPlane(candidate - doorway.InteriorPoint, Vector3.up).sqrMagnitude < 7f)
            {
                continue;
            }
            return candidate;
        }

        return new Vector3(bounds.center.x, y, bounds.center.z);
    }

    private void HandleDayChanged(int day)
    {
        lastDay = day;
        BuildDaySchedule();
        for (int i = 0; i < records.Count; i++)
        {
            VisitorRecord record = records[i];
            record.visitsToday = record.active && record.agent.IsInsideGym ? 1 : 0;
            record.workoutsToday = 0;
            record.visitInProgress = record.active && record.agent.IsEntryPending;
            record.workoutInProgress = false;
            record.activeSince = Time.time;
            record.entryStartedAt = record.visitInProgress ? Time.time : 0f;
            record.leaveAfter = record.agent.IsInsideGym
                ? Time.time + RandomRange(minimumVisitSeconds, maximumVisitSeconds)
                : float.PositiveInfinity;
            record.observedWorkoutVersion = record.agent.CompletedWorkoutVersion;
        }
        Debug.Log($"GYMCHAOS_VISITOR_QUOTA_RESET day={day}", this);
    }

    private void LogSquatStationCoverage()
    {
        GymExerciseStation[] stations = FindObjectsByType<GymExerciseStation>();
        int squatCount = 0;
        int cageCount = 0;
        int smithCount = 0;
        for (int i = 0; i < stations.Length; i++)
        {
            if (stations[i] == null || !stations[i].IsSquat)
            {
                continue;
            }
            squatCount++;
            string lowerName = stations[i].EquipmentName.ToLowerInvariant();
            if (lowerName.Contains("cage")) cageCount++;
            if (lowerName.Contains("smith")) smithCount++;
        }
        Debug.Log(
            $"GYMCHAOS_SQUAT_STATIONS count={squatCount} cages={cageCount} smith={smithCount}",
            this);
    }

    private VisitorRecord FindRecord(BodybuilderIdentity identity)
    {
        for (int i = 0; i < records.Count; i++)
        {
            if (records[i].fighter != null && records[i].fighter.Identity == identity)
            {
                return records[i];
            }
        }
        return null;
    }

    private float RandomRange(float minimum, float maximum)
    {
        return Mathf.Lerp(minimum, maximum, (float)random.NextDouble());
    }

    private float MakeUniqueTime(float value, List<float> used)
    {
        float candidate = Mathf.Repeat(value, 1f);
        for (int attempt = 0; attempt < 24; attempt++)
        {
            bool unique = true;
            for (int i = 0; i < used.Count; i++)
            {
                float distance = Mathf.Abs(Mathf.DeltaAngle(candidate * 360f, used[i] * 360f)) / 360f;
                if (distance < 0.012f)
                {
                    unique = false;
                    break;
                }
            }
            if (unique)
            {
                return candidate;
            }
            candidate = Mathf.Repeat(candidate + 0.017f, 1f);
        }
        return candidate;
    }

    private static bool IsDue(float now, float scheduled)
    {
        return now + 0.0005f >= scheduled;
    }

    private void OnDestroy()
    {
        if (timeOfDay != null)
        {
            timeOfDay.DayChanged -= HandleDayChanged;
        }
        if (Instance == this)
        {
            Instance = null;
        }
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }
}
