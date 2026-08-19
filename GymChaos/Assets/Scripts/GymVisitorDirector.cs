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

    private readonly List<VisitorRecord> records = new List<VisitorRecord>();
    private System.Random random;
    private GymTimeOfDay timeOfDay;
    private GymDoorway doorway;
    private PlayerMovement player;
    private int lastDay = -1;
    private bool initialized;

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
            record.agent.MarkDormant();
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

                ActivateScheduled(record, true);
                fighter = record.fighter;
                break;
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
        EnsureMinimumVisitors();
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

            if (record.active && record.visitInProgress)
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
                else if (Time.time - record.entryStartedAt > 18f)
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
                if (record.visitsToday < 2 && IsDue(now, record.entryTimes[record.visitsToday]))
                {
                    ActivateScheduled(record);
                }
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

            if (!record.agent.IsBusy && !record.fighter.IsOnTreadmill &&
                !record.workoutInProgress && record.workoutsToday < 2 &&
                IsDue(now, record.workoutTimes[record.workoutsToday]))
            {
                TryStartWorkout(record);
            }

            if (Time.time >= record.leaveAfter && !record.agent.IsBusy &&
                !record.fighter.IsOnTreadmill)
            {
                if (insideCount <= 2)
                {
                    EnsureThreeVisitors();
                    insideCount = ActiveVisitorCount;
                }

                if (insideCount > 2)
                {
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
            if (!record.active && !record.suspendedForCombat && record.visitsToday < 2)
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
            if (!record.active && !record.suspendedForCombat && record.visitsToday < 2)
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

    private void ActivateScheduled(VisitorRecord record, bool forced = false)
    {
        if (record == null || record.active || record.visitsToday >= 2 || doorway == null)
        {
            return;
        }

        Vector3 outside = doorway.ExteriorPoint;
        Vector3 inward = Vector3.ProjectOnPlane(doorway.InteriorPoint - outside, Vector3.up);
        Quaternion rotation = inward.sqrMagnitude > 0.01f
            ? Quaternion.LookRotation(inward.normalized, Vector3.up)
            : record.fighter.transform.rotation;
        record.fighter.SetVisitorSpawnPose(outside, rotation);
        record.fighter.gameObject.SetActive(true);
        record.agent.BeginEntry(doorway, ChooseRoomTarget(record.fighter.Identity));
        record.active = true;
        record.visitInProgress = true;
        record.entryStartedAt = Time.time;
        record.activeSince = Time.time;
        record.leaveAfter = float.PositiveInfinity;
        record.observedWorkoutVersion = record.agent.CompletedWorkoutVersion;
        Debug.Log(
            $"GYMCHAOS_VISITOR_ENTRY_REQUESTED enemy={record.fighter.Identity} " +
            $"visitCandidate={record.visitsToday + 1} forced={forced}",
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

        record.deactivationDeferredLogged = false;
        record.active = false;
        record.visitInProgress = false;
        record.workoutInProgress = false;
        record.suspendedForCombat = false;
        record.agent.MarkDormant();
        record.fighter.gameObject.SetActive(false);
        Debug.Log(
            $"GYMCHAOS_VISITOR_VISIT_END enemy={record.fighter.Identity} " +
            $"visits={record.visitsToday} afterDoorExit={record.agent.HasCompletedDoorExit}",
            this);
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
