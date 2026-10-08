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
    private const float VisitorEntryTimeoutSeconds = 70f;
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
        public bool destinationChoiceMade;
        public bool lockerVisitScheduled;
        public bool bagPickupStarted;
        public bool storeVisitScheduled;
        public bool destinationVisitInProgress;
        public bool suspendedForCombat;
        public bool deactivationDeferredLogged;
        public float[] entryTimes = new float[2];
        public float[] workoutTimes = new float[2];
        public int observedWorkoutVersion;
        public GymVisitorVehicle vehicle;
        public bool waitingForVehicle;
        public bool vehicleDepartureStarted;
        public bool daviePassengerBoarded;
        public bool davieBusDeparturePendingAfterEntry;
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
        BodybuilderIdentity.Goku,
        BodybuilderIdentity.Davie
    };

    private const int LockerCohortSeedSalt = 0x5f3759df;
    [SerializeField] private int deterministicSeed = -1;
    [SerializeField, Min(12f)] private float minimumVisitSeconds = 25f;
    [SerializeField, Min(18f)] private float maximumVisitSeconds = 36f;
    [SerializeField, Range(0f, 1f)] private float firstScheduleOffset = 0.055f;
    [SerializeField, Range(0f, 1f)] private float secondScheduleStart = 0.62f;
    [SerializeField, Range(0f, 1f)] private float minimumWorkoutDelay = 0.06f;
    [SerializeField, Range(0f, 1f)] private float maximumWorkoutDelay = 0.13f;
    // Retained for scene and prefab compatibility; locker selection is cohort-based.
    [SerializeField, Range(0f, 1f)] private float lockerVisitChance = 0.34f;
    // Chance that an arriving member leaves a gym bag on a locker bench.
    [SerializeField, Range(0f, 1f)] private float memberBagChance = 0.75f;
    [SerializeField, Range(0f, 1f)] private float proteinStoreVisitChance = 0.08f;
    [SerializeField, Min(20f)] private float minimumReturnCooldownSeconds = 45f;
    [SerializeField, Min(30f)] private float maximumReturnCooldownSeconds = 80f;

    private readonly List<VisitorRecord> records = new List<VisitorRecord>();
    private System.Random random;
    private System.Random lockerCohortRandom;
    private readonly bool[] lockerCohortAssignments =
        new bool[EligibleIdentities.Length];
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
        lockerCohortRandom = new System.Random(seed ^ LockerCohortSeedSalt);
        if (initialized)
        {
            BuildDaySchedule();
        }
        else
        {
            ResetLockerCohortSelection();
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
    public int LockerEligibleMemberCountForVerification => EligibleIdentities.Length;
    public int LockerAvailableMemberCountForVerification
    {
        get
        {
            int count = 0;
            for (int index = 0; index < EligibleIdentities.Length; index++)
            {
                for (int recordIndex = 0; recordIndex < records.Count; recordIndex++)
                {
                    if (records[recordIndex]?.fighter != null &&
                        records[recordIndex].fighter.Identity == EligibleIdentities[index])
                    {
                        count++;
                        break;
                    }
                }
            }
            return count;
        }
    }
    public int LockerCohortSelectedForVerification
    {
        get
        {
            int count = 0;
            for (int index = 0; index < lockerCohortAssignments.Length; index++)
                if (lockerCohortAssignments[index]) count++;
            return count;
        }
    }
    public float ProteinStoreVisitChanceForVerification => proteinStoreVisitChance;
    public bool DaviePassengerBoardedForVerification
    {
        get
        {
            VisitorRecord record = FindRecord(BodybuilderIdentity.Davie);
            return record != null && record.vehicle != null &&
                record.vehicle.IsBus && record.daviePassengerBoarded;
        }
    }

    public void PauseVisitorScheduleForVerification()
    {
        enabled = false;
    }    public float MaximumReturnCooldownForVerification => maximumReturnCooldownSeconds;

    public bool PrepareProteinStoreVisitForVerification()
    {
        for (int index = 0; index < records.Count; index++)
        {
            VisitorRecord record = records[index];
            if (record == null || record.fighter == null || record.agent == null ||
                record.fighter.IsDead || record.fighter.IsAggressive ||
                IsLockerCohortMember(record.fighter.Identity) ||
                record.fighter.Identity == BodybuilderIdentity.Davie)
            {
                continue;
            }

            if (!record.fighter.gameObject.activeSelf)
                record.fighter.gameObject.SetActive(true);
            record.agent.CancelForCombat();
            record.agent.MarkInitialInside();
            record.active = true;
            record.suspendedForCombat = false;
            record.visitInProgress = false;
            record.workoutInProgress = false;
            record.destinationChoiceMade = true;
            record.lockerVisitScheduled = false;
            record.storeVisitScheduled = false;
            record.destinationVisitInProgress = false;
            record.waitingForVehicle = false;
            record.walkingToVehicle = false;
            record.vehicleDepartureStarted = false;
            record.leaveAfter = float.PositiveInfinity;
            Debug.Log(
                $"GYMCHAOS_PROTEIN_STORE_VERIFICATION_ROSTER_READY enemy={record.fighter.Identity}",
                this);
            return true;
        }
        return false;
    }

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
        return BeginEntryForVerification((BodybuilderIdentity?)null, out fighter);
    }

    public bool BeginEntryForVerification(
        BodybuilderIdentity excludedIdentity, out EnemyFighter fighter)
    {
        return BeginEntryForVerification((BodybuilderIdentity?)excludedIdentity, out fighter);
    }

    private bool BeginEntryForVerification(
        BodybuilderIdentity? excludedIdentity, out EnemyFighter fighter)
    {
        fighter = null;
        for (int pass = 0; pass < 2 && fighter == null; pass++)
        {
            for (int i = 0; i < records.Count; i++)
            {
                VisitorRecord record = records[i];
                if (record.suspendedForCombat || record.active || record.visitsToday >= 2 ||
                    (excludedIdentity.HasValue && record.fighter != null &&
                     record.fighter.Identity == excludedIdentity.Value))
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

        if (fighter == null)
        {
            for (int i = 0; i < records.Count; i++)
            {
                VisitorRecord record = records[i];
                if (record == null || record.fighter == null || record.agent == null ||
                    record.fighter.IsDead || record.fighter.IsAggressive ||
                    record.fighter.Identity == BodybuilderIdentity.Cbum ||
                    record.fighter.Identity == BodybuilderIdentity.Davie ||
                    (excludedIdentity.HasValue && record.fighter.Identity == excludedIdentity.Value) ||
                    record.suspendedForCombat)
                {
                    continue;
                }

                record.agent.ResetForEntryVerification();
                record.fighter.gameObject.SetActive(false);
                record.active = false;
                record.visitsToday = 0;
                record.workoutsToday = 0;
                record.visitInProgress = false;
                record.workoutInProgress = false;
                record.destinationChoiceMade = false;
                record.lockerVisitScheduled = false;
                record.storeVisitScheduled = false;
                record.destinationVisitInProgress = false;
                record.waitingForVehicle = false;
                record.walkingToVehicle = false;
                record.vehicleDepartureStarted = false;
                record.suspendedForCombat = false;
                record.nextEligibleRealtime = 0f;

                if (ActivateScheduled(record, true))
                {
                    fighter = record.fighter;
                    Debug.Log(
                        $"GYMCHAOS_DOORWAY_PRIORITY_ENTRY_RESET enemy={fighter.Identity}",
                        this);
                    break;
                }
            }
        }

        return fighter != null;
    }

    public bool BeginDavieBusLifecycleForVerification(
        out EnemyFighter fighter, out GymVisitorVehicle bus)
    {
        fighter = null;
        bus = null;
        if (!initialized || doorway == null) return false;

        VisitorRecord davie = FindRecord(BodybuilderIdentity.Davie);
        if (davie == null || davie.fighter == null || davie.agent == null)
            return false;

        departureDrainMode = true;
        for (int i = 0; i < records.Count; i++)
        {
            VisitorRecord record = records[i];
            if (record == null || record == davie) continue;
            record.agent?.CancelForCombat();
            record.active = false;
            record.visitInProgress = false;
            record.workoutInProgress = false;
            record.destinationVisitInProgress = false;
            record.suspendedForCombat = true;
            record.fighter?.gameObject.SetActive(false);
            record.vehicle?.gameObject.SetActive(false);
        }

        davie.agent.CancelForCombat();
        davie.vehicle?.gameObject.SetActive(false);
        davie.fighter.gameObject.SetActive(false);
        davie.active = false;
        davie.visitsToday = 0;
        davie.workoutsToday = 0;
        davie.visitInProgress = false;
        davie.workoutInProgress = false;
        davie.destinationVisitInProgress = false;
        davie.suspendedForCombat = false;
        davie.davieBusDeparturePendingAfterEntry = false;
        davie.daviePassengerBoarded = false;
        davie.vehicleDepartureStarted = false;
        davie.walkingToVehicle = false;
        davie.queuedForcedDeparture = false;
        davie.nextEligibleRealtime = 0f;

        if (!ActivateScheduled(davie, true)) return false;
        fighter = davie.fighter;
        bus = davie.vehicle;
        return fighter != null && bus != null && bus.IsBus;
    }

    public bool RequestDavieDepartureForVerification()
    {
        VisitorRecord davie = FindRecord(BodybuilderIdentity.Davie);
        if (davie == null || !davie.active || davie.agent == null ||
            !davie.agent.HasEnteredGym)
        {
            return false;
        }

        davie.agent.CancelForCombat();
        davie.workoutInProgress = false;
        davie.destinationVisitInProgress = false;
        davie.lockerVisitScheduled = false;
        davie.storeVisitScheduled = false;
        davie.queuedForcedDeparture = true;
        davie.leaveAfter = Time.time;
        Debug.Log(
            $"GYMCHAOS_DAVIE_DEPARTURE_TEST_QUEUED state={davie.agent.State} " +
            $"busy={davie.agent.IsBusy}",
            this);
        return true;
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
                station = record.agent.WorkoutStationForVerification != null
                    ? record.agent.WorkoutStationForVerification
                    : candidate;
                return true;
            }
        }

        return false;
    }

    public bool BeginLockerVisitForVerification(out EnemyFighter fighter)
    {
        fighter = null;
        for (int pass = 0; pass < 2 && fighter == null; pass++)
        {
            for (int i = 0; i < records.Count; i++)
            {
                VisitorRecord record = records[i];
                if (record != null && record.fighter != null &&
                    record.agent != null &&
                    IsLockerCohortMember(record.fighter.Identity) &&
                    record.destinationVisitInProgress &&
                    record.lockerVisitScheduled)
                {
                    fighter = record.fighter;
                    return true;
                }

                if (record == null || record.fighter == null ||
                    record.agent == null ||
                    !IsLockerCohortMember(record.fighter.Identity) ||
                    record.fighter.IsDead || record.fighter.IsAggressive ||
                    record.visitInProgress || record.workoutInProgress ||
                    record.destinationVisitInProgress)
                {
                    continue;
                }

                EnsureVehicle(record, true);
                if (!record.active)
                {
                    record.fighter.gameObject.SetActive(true);
                    record.agent.MarkInitialInside();
                    record.active = true;
                    record.visitsToday = 1;
                    record.workoutsToday = 0;
                    record.waitingForVehicle = false;
                    record.walkingToVehicle = false;
                    record.vehicleDepartureStarted = false;
                }

                if (!record.agent.IsInsideGym || record.agent.IsBusy)
                {
                    continue;
                }

                record.destinationChoiceMade = true;
                record.lockerVisitScheduled = true;
                record.storeVisitScheduled = false;
                record.leaveAfter = float.PositiveInfinity;
                if (!TryStartDestinationVisit(record))
                {
                    record.lockerVisitScheduled = false;
                    continue;
                }

                fighter = record.fighter;
                return true;
            }
        }

        return false;
    }
    public bool BeginProteinStoreVisitForVerification(out EnemyFighter fighter)
    {
        fighter = null;
        if (doorway == null || !GymOutdoorBuilder.HasProteinStoreRoute) return false;
        for (int index = 0; index < records.Count; index++)
        {
            VisitorRecord record = records[index];
            if (record == null || record.fighter == null || record.agent == null ||
                record.fighter.IsDead || record.fighter.IsAggressive ||
                record.suspendedForCombat || record.destinationVisitInProgress ||
                record.visitInProgress || record.workoutInProgress ||
                IsLockerCohortMember(record.fighter.Identity) ||
                record.fighter.Identity == BodybuilderIdentity.Davie) continue;

            bool alreadyFreeInside = record.active && record.agent.IsInsideGym &&
                !record.agent.IsBusy &&
                record.agent.State == GymVisitorAgent.VisitorState.FreeRoaming;
            if (record.active && !alreadyFreeInside) continue;
            if (!record.active)
            {
                record.fighter.gameObject.SetActive(true);
                record.agent.MarkInitialInside();
                record.active = true;
            }
            record.visitInProgress = false;
            record.workoutInProgress = false;
            record.destinationChoiceMade = true;
            record.lockerVisitScheduled = false;
            record.storeVisitScheduled = true;
            record.destinationVisitInProgress = false;
            record.waitingForVehicle = false;
            record.walkingToVehicle = false;
            record.vehicleDepartureStarted = false;
            record.leaveAfter = float.PositiveInfinity;
            if (!TryStartDestinationVisit(record))
            {
                record.storeVisitScheduled = false;
                record.destinationVisitInProgress = false;
                continue;
            }
            fighter = record.fighter;
            Debug.Log(
                $"GYMCHAOS_STORE_ROUTE_VERIFICATION_STARTED enemy={fighter.Identity} " +
                $"probability={proteinStoreVisitChance:F2}", this);
            return true;
        }
        return false;
    }

public bool BeginDepartureForVerification(
        out EnemyFighter fighter, out GymVisitorVehicle vehicle)
    {
        fighter = null;
        vehicle = null;
        if (doorway == null) return false;
        departureDrainMode = true;
        for (int i = 0; i < records.Count; i++)
        {
            VisitorRecord record = records[i];
            if (!record.active || record.waitingForVehicle || record.agent == null ||
                !record.agent.IsInsideGym || record.agent.IsBusy) continue;
            EnsureVehicle(record, true);
            record.suspendedForCombat = false;
            fighter = record.fighter;
            vehicle = record.vehicle;
            record.leaveAfter = Time.time;
            record.agent.BeginExit(doorway);
            return true;
        }
        return false;
    }

    public bool BeginCbumDepartureForVerification(
        out EnemyFighter fighter, out GymVisitorVehicle vehicle)
    {
        fighter = null;
        vehicle = null;
        if (doorway == null) return false;
        departureDrainMode = true;
        VisitorRecord record = FindRecord(BodybuilderIdentity.Cbum);
        if (record == null || record.fighter == null || record.agent == null) return false;
        EnsureVehicle(record, true);
        record.fighter.gameObject.SetActive(true);
        record.agent.MarkInitialInside();
        PrepareVerificationDeparturePose(record);
        record.active = true;
        record.suspendedForCombat = false;
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

    private void PrepareVerificationDeparturePose(VisitorRecord record)
    {
        if (record == null || record.fighter == null || doorway == null ||
            !GymInteriorBuilder.TryGetMainGymBounds(out Bounds mainGymBounds))
        {
            return;
        }

        Vector3 floorCenter = mainGymBounds.center;
        floorCenter.y = mainGymBounds.min.y;
        Vector3 position = floorCenter + new Vector3(-4f, 0f, 4f);
        if (!mainGymBounds.Contains(position)) position = floorCenter;
        Vector3 towardDoor = Vector3.ProjectOnPlane(
            doorway.InteriorPoint - position, Vector3.up);
        Quaternion rotation = towardDoor.sqrMagnitude > 0.01f
            ? Quaternion.LookRotation(towardDoor.normalized, Vector3.up)
            : record.fighter.transform.rotation;
        record.fighter.SetVisitorSpawnPose(position, rotation);
        Physics.SyncTransforms();
        Debug.Log(
            $"GYMCHAOS_VISITOR_VERIFICATION_DEPARTURE_POSE " +
            $"enemy={record.fighter.Identity} position={position} " +
            $"mainGymBounds={mainGymBounds}", this);
    }

    public bool BeginArnoldDepartureForVerification(
        out EnemyFighter fighter, out GymVisitorVehicle vehicle)
    {
        fighter = null;
        vehicle = null;
        if (doorway == null) return false;
        departureDrainMode = true;
        VisitorRecord record = FindRecord(BodybuilderIdentity.Arnold);
        if (record == null || record.fighter == null || record.agent == null) return false;
        EnsureVehicle(record, true);
        record.fighter.gameObject.SetActive(true);
        record.agent.MarkInitialInside();
        record.active = true;
        record.suspendedForCombat = false;
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

    public bool BeginZyzzDepartureForVerification(
        out EnemyFighter fighter, out GymVisitorVehicle vehicle)
    {
        fighter = null;
        vehicle = null;
        if (doorway == null) return false;
        departureDrainMode = true;
        VisitorRecord record = FindRecord(BodybuilderIdentity.Zyzz);
        if (record == null || record.fighter == null || record.agent == null) return false;
        EnsureVehicle(record, true);
        record.fighter.gameObject.SetActive(true);
        record.agent.MarkInitialInside();
        record.active = true;
        record.suspendedForCombat = false;
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
        departureDrainMode = true;
        VisitorRecord record = FindRecord(BodybuilderIdentity.Goku);
        if (record == null || record.fighter == null || record.agent == null) return false;
        EnsureVehicle(record, true);
        record.fighter.gameObject.SetActive(true);
        record.agent.MarkInitialInside();
        record.active = true;
        record.suspendedForCombat = false;
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
            record.destinationChoiceMade = false;
            record.lockerVisitScheduled = false;
            record.storeVisitScheduled = false;
            record.destinationVisitInProgress = false;
            record.suspendedForCombat = false;
            record.waitingForVehicle = false;
            record.walkingToVehicle = false;
            record.vehicleDepartureStarted = false;
            record.queuedForcedDeparture = true;
            record.agent.SetDepartureQueueHoldForVerification(true);
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
        int seed = deterministicSeed >= 0
            ? deterministicSeed
            : Environment.TickCount ^ Time.frameCount;
        random = new System.Random(seed);
        lockerCohortRandom = new System.Random(seed ^ LockerCohortSeedSalt);

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
        ResetLockerCohortSelection();
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

    private void ResetLockerCohortSelection()
    {
        if (lockerCohortRandom == null)
        {
            int seed = deterministicSeed >= 0
                ? deterministicSeed
                : Environment.TickCount ^ Time.frameCount;
            lockerCohortRandom = new System.Random(seed ^ LockerCohortSeedSalt);
        }

        for (int index = 0; index < lockerCohortAssignments.Length; index++)
        {
            lockerCohortAssignments[index] = false;
        }

        List<int> availableIdentityIndices = new List<int>();
        for (int index = 0; index < EligibleIdentities.Length; index++)
        {
            for (int recordIndex = 0; recordIndex < records.Count; recordIndex++)
            {
                if (records[recordIndex].fighter.Identity == EligibleIdentities[index])
                {
                    availableIdentityIndices.Add(index);
                    break;
                }
            }
        }

        int confirmed = availableIdentityIndices.Count;
        int selected = 0;
        while (selected < 2 && availableIdentityIndices.Count > 0)
        {
            int availableIndex = lockerCohortRandom.Next(
                0, availableIdentityIndices.Count);
            int identityIndex = availableIdentityIndices[availableIndex];
            availableIdentityIndices.RemoveAt(availableIndex);
            lockerCohortAssignments[identityIndex] = true;
            selected++;
        }

        Debug.Log(
            $"GYMCHAOS_LOCKER_COHORT_READY confirmed={confirmed} " +
            $"selected={selected}",
            this);
    }

    private bool IsLockerCohortMember(BodybuilderIdentity identity)
    {
        for (int index = 0; index < EligibleIdentities.Length; index++)
        {
            if (EligibleIdentities[index] == identity)
            {
                return lockerCohortAssignments[index];
            }
        }
        return false;
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
            record.destinationChoiceMade = false;
            record.lockerVisitScheduled = false;
            record.storeVisitScheduled = false;
            record.destinationVisitInProgress = false;
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
        record.suspendedForCombat = false;
        record.visitsToday = 1;
        record.workoutsToday = 0;
        record.activeSince = Time.time;
        record.leaveAfter = Time.time + RandomRange(minimumVisitSeconds, maximumVisitSeconds);
        record.entryStartedAt = 0f;
        record.visitInProgress = false;
        record.workoutInProgress = false;
        ChooseDestinationVisit(record);
        TryLeaveMemberBag(record);
        record.observedWorkoutVersion = record.agent.CompletedWorkoutVersion;
    }

    private void TryLeaveMemberBag(VisitorRecord record)
    {
        record.bagPickupStarted = false;
        if (record.fighter == null || random.NextDouble() >= memberBagChance)
        {
            return;
        }
        GymBackRoomBuilder.TryPlaceMemberBag(record.fighter.Identity);
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

            bool isVerificationDeparture = departureDrainMode &&
                record.agent != null &&
                (record.queuedForcedDeparture ||
                 record.agent.State == GymVisitorAgent.VisitorState.ExitingDoor ||
                 record.agent.State == GymVisitorAgent.VisitorState.LeavingGym ||
                 record.agent.State == GymVisitorAgent.VisitorState.ApproachingVehicle ||
                 record.agent.HasLeftGym);
            if (record.fighter.IsDead ||
                (record.fighter.IsAggressive && !isVerificationDeparture))
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
                    if (record.davieBusDeparturePendingAfterEntry &&
                        record.vehicle != null && record.vehicle.IsBus)
                    {
                        record.davieBusDeparturePendingAfterEntry = false;
                        record.vehicle.DriveOut(null);
                        Debug.Log(
                            "GYMCHAOS_DAVIE_BUS_DEPART_AFTER_ENTRY_STARTED " +
                            $"enemy={record.fighter.Identity}",
                            this);
                    }

                    record.visitInProgress = false;
                    record.visitsToday = Mathf.Min(2, record.visitsToday + 1);
                    record.activeSince = Time.time;
                    record.leaveAfter = Time.time +
                        RandomRange(minimumVisitSeconds, maximumVisitSeconds);
                    ChooseDestinationVisit(record);
                    TryLeaveMemberBag(record);
                    Debug.Log(
                        $"GYMCHAOS_VISITOR_ENTERED_CONFIRMED enemy={record.fighter.Identity} " +
                        $"visit={record.visitsToday}",
                        this);
                    Debug.Log(
                        $"GYMCHAOS_VISITOR_VISIT_START enemy={record.fighter.Identity} " +
                        $"visit={record.visitsToday}",
                        this);
                }
                else if (Time.time - record.entryStartedAt > VisitorEntryTimeoutSeconds &&
                    !record.agent.IsWaitingForSharedCorridor &&
                    !record.agent.IsWaitingForSharedParkingConnector)
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

            if (record.destinationVisitInProgress)
            {
                if (record.agent.State == GymVisitorAgent.VisitorState.FreeRoaming &&
                    !record.agent.IsBusy && !record.agent.IsStoreVisitActive)
                {
                    record.destinationVisitInProgress = false;
                    record.lockerVisitScheduled = false;
                    record.storeVisitScheduled = false;
                    record.leaveAfter = Mathf.Max(record.leaveAfter, Time.time + 2.25f);
                    Debug.Log(
                        $"GYMCHAOS_VISITOR_DESTINATION_COMPLETE enemy={record.fighter.Identity}",
                        this);
                }
                else
                {
                    continue;
                }
            }

            if (!record.agent.IsInsideGym)
            {
                continue;
            }

            if (!departureDrainMode && !record.agent.IsBusy &&
                !record.fighter.IsOnTreadmill &&
                !record.workoutInProgress &&
                (record.lockerVisitScheduled || record.storeVisitScheduled))
            {
                if (TryStartDestinationVisit(record))
                {
                    continue;
                }
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
                !record.workoutInProgress && !record.destinationVisitInProgress &&
                !record.lockerVisitScheduled && !record.storeVisitScheduled &&
                record.workoutsToday < 2 &&
                IsDue(now, record.workoutTimes[record.workoutsToday]))
            {
                TryStartWorkout(record);
            }

            if ((record.queuedForcedDeparture || Time.time >= record.leaveAfter) &&
                !record.agent.IsBusy &&
                !record.fighter.IsOnTreadmill &&
                !record.destinationVisitInProgress &&
                !record.lockerVisitScheduled && !record.storeVisitScheduled)
            {
                if (insideCount <= 2 && !record.queuedForcedDeparture)
                {
                    EnsureThreeVisitors();
                    insideCount = ActiveVisitorCount;
                }

                // A member with a bag on the locker bench first walks to the
                // locker room for it (the bag disappears when collected).
                if (!departureDrainMode && !record.bagPickupStarted &&
                    (insideCount > 2 || record.queuedForcedDeparture) &&
                    GymBackRoomBuilder.HasMemberBag(record.fighter.Identity))
                {
                    record.bagPickupStarted = true;
                    record.lockerVisitScheduled = true;
                    Debug.Log(
                        $"GYMCHAOS_LOCKER_MEMBER_BAG_PICKUP enemy={record.fighter.Identity}", this);
                    continue;
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

            // A visitor can be FreeRoaming while its physical capsule or
            // animated hitboxes still occupy the narrow doorway. State-only
            // serialization lets the next forced departure enter that same
            // space and stall against the first visitor. Keep the normal
            // collision model intact and wait for physical clearance instead.
            if (other.agent.IsDoorwayTraversalAreaOccupied)
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
        record.destinationChoiceMade = false;
        record.lockerVisitScheduled = false;
        record.storeVisitScheduled = false;
        record.destinationVisitInProgress = false;
        record.entryStartedAt = Time.time;
        record.leaveAfter = float.PositiveInfinity;
        record.vehicleDepartureStarted = false;
        record.davieBusDeparturePendingAfterEntry = false;
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
                record.vehicle.PassengerPoint, record.vehicle.AislePassengerPoint,
                record.vehicle);
            if (record.vehicle.IsBus)
            {
                // Keep the shuttle parked at the stop until Davie has crossed
                // the doorway. The bus then leaves independently while he is
                // inside; it returns only when his visit ends.
                record.davieBusDeparturePendingAfterEntry = true;
                Debug.Log(
                    "GYMCHAOS_DAVIE_BUS_WAITING_FOR_ENTRY_CONFIRMATION", this);
            }
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
        // Once the pedestrian has reserved and started the vehicle approach,
        // the agent is intentionally no longer CanDeactivate: its state is
        // ApproachingVehicle until boarding completes. Keep that handoff
        // alive instead of re-entering the pre-approach safety gate every
        // frame and leaving the vehicle permanently parked.
        if (record.agent == null ||
            (!record.walkingToVehicle && !record.agent.CanDeactivate))
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
        if (record.vehicle != null && record.vehicle.IsBus &&
            !record.vehicle.IsParked && !record.vehicle.IsDriving)
        {
            // The arrival bus already completed its route and was hidden.
            // Bring it back only when Davie is leaving, then let the normal
            // pedestrian approach reach the authored front-side door point.
            record.waitingForVehicle = true;
            record.walkingToVehicle = false;
            record.leaveAfter = float.PositiveInfinity;
            record.vehicle.DriveIn(() => BeginDavieBusPickup(record));
            Debug.Log(
                "GYMCHAOS_DAVIE_BUS_PICKUP_REQUESTED reason=visitor_departure",
                this);
            return;
        }
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
            {
                if (record.vehicle.IsBus)
                {
                    // Latch the boarding event before the passenger is hidden
                    // and pooled away from the vehicle's passenger point.
                    record.daviePassengerBoarded = true;
                    Debug.Log(
                        $"GYMCHAOS_DAVIE_BUS_BOARDING point={record.fighter.transform.position}",
                        this);
                }
                record.fighter.gameObject.SetActive(false);
            }
            record.vehicle.DriveOut(() => CompleteVehicleDeparture(record));
        }
    }

    private void BeginDavieBusPickup(VisitorRecord record)
    {
        if (record == null || !record.active || record.fighter == null ||
            record.agent == null || record.vehicle == null ||
            !record.vehicle.IsBus)
        {
            return;
        }

        record.waitingForVehicle = false;
        record.walkingToVehicle = false;
        record.daviePassengerBoarded = false;
        record.leaveAfter = Time.time + 0.25f;
        if (record.agent.BeginVehicleApproach(
            record.vehicle.PassengerPoint, record.vehicle.BoardingReachDistance))
        {
            record.walkingToVehicle = true;
        }
        Debug.Log(
            $"GYMCHAOS_DAVIE_BUS_READY_FOR_PICKUP point={record.vehicle.PassengerPoint}",
            this);
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
        record.davieBusDeparturePendingAfterEntry = false;
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

    private void ChooseDestinationVisit(VisitorRecord record)
    {
        if (record == null || record.fighter == null)
        {
            return;
        }

        record.destinationChoiceMade = true;
        record.lockerVisitScheduled = IsLockerCohortMember(record.fighter.Identity);
        record.storeVisitScheduled = !record.lockerVisitScheduled &&
            GymOutdoorBuilder.HasProteinStoreRoute &&
            random.NextDouble() < proteinStoreVisitChance;
        record.destinationVisitInProgress = false;
        if (record.lockerVisitScheduled)
        {
            Debug.Log(
                $"GYMCHAOS_VISITOR_LOCKER_SCHEDULED enemy={record.fighter.Identity} " +
                $"visit={record.visitsToday}", this);
        }
        else if (record.storeVisitScheduled)
        {
            Debug.Log(
                $"GYMCHAOS_VISITOR_STORE_SCHEDULED enemy={record.fighter.Identity} " +
                $"visit={record.visitsToday}", this);
        }
    }

    private bool TryStartDestinationVisit(VisitorRecord record)
    {
        if (record == null || record.agent == null)
        {
            return false;
        }

        if (record.lockerVisitScheduled)
        {
            if (!GymBackRoomBuilder.TryGetLockerVisitPose(
                record.fighter.Identity, out Vector3 lockerPosition, out Quaternion lockerRotation))
            {
                record.lockerVisitScheduled = false;
                Debug.LogWarning($"GYMCHAOS_VISITOR_LOCKER_SKIPPED enemy={record.fighter.Identity} reason=no_slot_or_room", this);
                return false;
            }
            if (record.agent.BeginLockerRoomVisit(
                lockerPosition, 2.8f, "locker room"))
            {
                record.destinationVisitInProgress = true;
                return true;
            }
            GymBackRoomBuilder.ReleaseLockerSlot(record.fighter.Identity);
            record.lockerVisitScheduled = false;
            Debug.LogWarning($"GYMCHAOS_VISITOR_LOCKER_SKIPPED enemy={record.fighter.Identity} reason=route_start_failed", this);
            return false;
        }

        if (record.storeVisitScheduled)
        {
            if (!GymOutdoorBuilder.HasProteinStoreRoute)
            {
                record.storeVisitScheduled = false;
                return false;
            }
            if (record.agent.BeginProteinStoreVisit(doorway, 3.2f))
            {
                record.destinationVisitInProgress = true;
                return true;
            }
            return true;
        }

        return false;
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
            if (record.active && record.agent.IsInsideGym &&
                !record.destinationVisitInProgress)
            {
                ChooseDestinationVisit(record);
            }
            else if (!record.agent.IsInsideGym)
            {
                record.destinationChoiceMade = false;
                record.lockerVisitScheduled = false;
                record.storeVisitScheduled = false;
            }
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
