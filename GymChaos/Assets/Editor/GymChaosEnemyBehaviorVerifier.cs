using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class GymChaosEnemyBehaviorVerifier
{
    private const string VerificationRequestedKey =
        "GymChaos.EnemyBehaviorVerificationRequested";

    private static double enteredPlayTime;
    private static bool behaviorSetup;
    private static bool treadmillPending;
    private static bool treadmillRunningObserved;
    private static bool observedIdle;
    private static bool observedRunning;
    private static bool earlyStaggerLogged;
    private static bool observedRoomWideTarget;
    private static bool observedPurposefulTarget;
    private static bool observedInterestCoverage;
    private static float maximumObservedBlockedTime;
    private static double treadmillDeadline;
    private static double ronnieMismatchSince;
    private static bool ronnieDirectAggroTested;
    private static bool visitorSimulationSuspended;
    private static EnemyFighter aggressionTarget;
    private static EnemyFighter treadmillUser;
    private static GymExerciseStation treadmillStation;
    private static PlayerMovement player;
    private static readonly Dictionary<EnemyFighter, Vector3> baselinePositions =
        new Dictionary<EnemyFighter, Vector3>();
    private static readonly HashSet<string> observedStateSignatures =
        new HashSet<string>();
    private static readonly HashSet<BodybuilderIdentity> earlyRunningIdentities =
        new HashSet<BodybuilderIdentity>();
    private static readonly HashSet<BodybuilderIdentity> earlyIdleIdentities =
        new HashSet<BodybuilderIdentity>();
    private static readonly HashSet<BodybuilderIdentity> earlyMovedIdentities =
        new HashSet<BodybuilderIdentity>();
    private static readonly Dictionary<EnemyFighter, Vector3> startupPositions =
        new Dictionary<EnemyFighter, Vector3>();
    private static readonly Dictionary<EnemyFighter, MixamoScanRetargetAnimator.MotionState>
        previousRoamAnimationStates =
        new Dictionary<EnemyFighter, MixamoScanRetargetAnimator.MotionState>();

    static GymChaosEnemyBehaviorVerifier()
    {
        if (!EditorPrefs.GetBool(VerificationRequestedKey, false))
        {
            return;
        }

        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        EditorApplication.delayCall += ResumeAfterDomainReload;
    }

    [MenuItem("Tools/GymChaos/Run Enemy Behavior Verification")]
    public static void Run()
    {
        ResetState();
        EditorPrefs.SetBool(VerificationRequestedKey, true);
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        EditorApplication.isPlaying = true;
    }

    private static void ResetState()
    {
        enteredPlayTime = 0d;
        behaviorSetup = false;
        treadmillPending = false;
        treadmillRunningObserved = false;
        observedIdle = false;
        observedRunning = false;
        earlyStaggerLogged = false;
        observedRoomWideTarget = false;
        observedPurposefulTarget = false;
        observedInterestCoverage = false;
        maximumObservedBlockedTime = 0f;
        treadmillDeadline = 0d;
        ronnieMismatchSince = -1d;
        ronnieDirectAggroTested = false;
        visitorSimulationSuspended = false;
        aggressionTarget = null;
        treadmillUser = null;
        treadmillStation = null;
        player = null;
        baselinePositions.Clear();
        observedStateSignatures.Clear();
        earlyRunningIdentities.Clear();
        earlyIdleIdentities.Clear();
        earlyMovedIdentities.Clear();
        startupPositions.Clear();
        previousRoamAnimationStates.Clear();
    }

    private static void ResumeAfterDomainReload()
    {
        if (EditorApplication.isPlaying)
        {
            enteredPlayTime = EditorApplication.timeSinceStartup;
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
        }
    }

    private static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            enteredPlayTime = EditorApplication.timeSinceStartup;
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
        }
        else if (state == PlayModeStateChange.EnteredEditMode)
        {
            EditorPrefs.DeleteKey(VerificationRequestedKey);
            EditorApplication.update -= Tick;
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            if (Application.isBatchMode)
            {
                EditorApplication.Exit(0);
            }
        }
    }

    private static void Tick()
    {
        try
        {
            double elapsed = EditorApplication.timeSinceStartup - enteredPlayTime;
            player = UnityEngine.Object.FindFirstObjectByType<PlayerMovement>();
            EnemyFighter[] fighters =
                UnityEngine.Object.FindObjectsByType<EnemyFighter>(FindObjectsSortMode.None);
            if (!visitorSimulationSuspended)
            {
                GymVisitorDirector visitorDirector =
                    UnityEngine.Object.FindFirstObjectByType<GymVisitorDirector>();
                if (visitorDirector != null)
                {
                    visitorDirector.SuspendVisitorSimulationForVerification();
                    visitorSimulationSuspended = true;
                    fighters = UnityEngine.Object.FindObjectsByType<EnemyFighter>(FindObjectsSortMode.None);
                }
            }
            if (player == null || CountCombatFighters(fighters) < 6)
            {
                if (elapsed > 25d)
                {
                    throw new InvalidOperationException(
                        "Enemy behavior verification scene did not initialize the player and six combat fighters.");
                }
                return;
            }

            CaptureStartupBehavior(fighters, elapsed);

            if (!behaviorSetup && elapsed >= 2.1d)
            {
                SetupBehaviorChecks(fighters);
            }

            if (!behaviorSetup)
            {
                return;
            }

            ObserveIndependentRoaming(fighters);
            ObserveRonnieTarget(fighters, elapsed);
            ObserveTreadmill();

            if (!treadmillPending && elapsed >= 5.5d)
            {
                FinishBehaviorChecks(fighters);
            }
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            EditorApplication.update -= Tick;
            EditorPrefs.DeleteKey(VerificationRequestedKey);
            EditorApplication.isPlaying = false;
        }
    }

    private static void SetupBehaviorChecks(EnemyFighter[] fighters)
    {
        List<EnemyFighter> combatFighters = GetCombatFighters(fighters);
        for (int i = 0; i < combatFighters.Count; i++)
        {
            EnemyFighter fighter = combatFighters[i];
            if (fighter.IsAggressive)
            {
                throw new InvalidOperationException(
                    $"{fighter.Identity} became aggressive without a player-caused hit.");
            }

            baselinePositions[fighter] = fighter.transform.position;
        }

        Debug.Log(
            $"GYMCHAOS_ENEMY_NEUTRAL_ROAM_OK count={combatFighters.Count} " +
            $"initialStates={BuildStateSignature(combatFighters)}");

        aggressionTarget = combatFighters[0];
        aggressionTarget.TakeMeleeHit(Vector3.zero, 1f, 0.1f);
        if (!aggressionTarget.IsAggressive ||
            aggressionTarget.CurrentTarget != player.transform)
        {
            throw new InvalidOperationException(
                $"{aggressionTarget.Identity} did not acquire the player after damage.");
        }
        if (aggressionTarget.IsRoaming || aggressionTarget.HasRoamDestination)
        {
            throw new InvalidOperationException(
                $"{aggressionTarget.Identity} kept a roaming state after becoming aggressive.");
        }

        Debug.Log(
            $"GYMCHAOS_ENEMY_AGGRO_BEHAVIOR_OK identity={aggressionTarget.Identity} " +
            $"target=Player health={aggressionTarget.CurrentHealth:0.0}");

        for (int i = 1; i < combatFighters.Count; i++)
        {
            if (!combatFighters[i].IsPolice && !combatFighters[i].IsAggressive)
            {
                treadmillUser = combatFighters[i];
                break;
            }
        }
        if (treadmillUser == null)
        {
            throw new InvalidOperationException("No neutral enemy was available for treadmill verification.");
        }

        treadmillStation = GymExerciseStation.FindClosestTreadmill(
            treadmillUser.transform.position, 100f);
        if (treadmillStation == null)
        {
            throw new InvalidOperationException("No unoccupied treadmill was registered for behavior verification.");
        }

        if (!treadmillUser.QueueTreadmillForVerification(treadmillStation))
        {
            throw new InvalidOperationException(
                $"{treadmillUser.Identity} could not queue an available treadmill destination.");
        }

        treadmillPending = true;
        // Walking is intentionally slow and the authored treadmill point can
        // sit behind several equipment rows, so allow the real route enough
        // time to complete instead of mistaking a valid detour for a failure.
        treadmillDeadline = EditorApplication.timeSinceStartup + 20d;
        Debug.Log(
            $"GYMCHAOS_ENEMY_TREADMILL_QUEUE identity={treadmillUser.Identity} " +
            $"start={treadmillUser.transform.position} target={treadmillUser.CurrentRoamTarget} " +
            $"belt={treadmillStation.EnemyPosition} " +
            $"distance={Vector3.ProjectOnPlane(treadmillStation.EnemyPosition - treadmillUser.transform.position, Vector3.up).magnitude:0.00} " +
            $"station={treadmillStation.DisplayName} " +
            $"routeRemaining={treadmillUser.CurrentRoamRouteRemaining}");
        behaviorSetup = true;
    }

    private static void CaptureStartupBehavior(EnemyFighter[] fighters, double elapsed)
    {
        List<EnemyFighter> combatFighters = GetCombatFighters(fighters);
        for (int i = 0; i < combatFighters.Count; i++)
        {
            EnemyFighter fighter = combatFighters[i];
            if (!startupPositions.ContainsKey(fighter))
            {
                startupPositions[fighter] = fighter.transform.position;
            }

            if (fighter.AnimationState == MixamoScanRetargetAnimator.MotionState.Running)
            {
                earlyRunningIdentities.Add(fighter.Identity);
            }
            else if (fighter.AnimationState == MixamoScanRetargetAnimator.MotionState.Idle)
            {
                earlyIdleIdentities.Add(fighter.Identity);
            }
            if (fighter.HasRoamDestination && fighter.CurrentRoamTargetDistance >= 6f)
            {
                observedRoomWideTarget = true;
            }
            if (fighter.CurrentRoamTargetIsPurposeful)
            {
                observedPurposefulTarget = true;
            }
            if (fighter.RoamMachineInterestCount > 0 &&
                fighter.RoamPersonnelInterestCount >= 2 &&
                fighter.HasReceptionRoamInterest &&
                fighter.HasPlayerRoamInterest)
            {
                observedInterestCoverage = true;
            }

            if (Vector3.ProjectOnPlane(
                    fighter.transform.position - startupPositions[fighter], Vector3.up).magnitude > 0.18f)
            {
                earlyMovedIdentities.Add(fighter.Identity);
            }
        }

        if (earlyStaggerLogged || elapsed < 0.9d)
        {
            return;
        }

        earlyStaggerLogged = true;
        GameObject floor = GameObject.Find("Rubber Floor");
        string floorBounds = floor != null && floor.TryGetComponent(out Renderer floorRenderer)
            ? floorRenderer.bounds.ToString()
            : "missing";
        Debug.Log(
            $"GYMCHAOS_ENEMY_EARLY_STAGGER elapsed={elapsed:0.00} " +
            $"running={FormatIdentities(earlyRunningIdentities)} " +
            $"idle={FormatIdentities(earlyIdleIdentities)} " +
            $"moved={FormatIdentities(earlyMovedIdentities)} floor={floorBounds}");
        for (int i = 0; i < combatFighters.Count; i++)
        {
            EnemyFighter fighter = combatFighters[i];
            Debug.Log(
                $"GYMCHAOS_ENEMY_SPAWN identity={fighter.Identity} " +
                $"position={fighter.transform.position} state={fighter.AnimationState} " +
                $"target={fighter.CurrentRoamTarget} targetDistance={fighter.CurrentRoamTargetDistance:0.00} " +
                $"purposeful={fighter.CurrentRoamTargetIsPurposeful} " +
                $"interest={fighter.CurrentRoamInterestLabel ?? "none"} " +
                $"interestCount={fighter.RoamInterestCount} machines={fighter.RoamMachineInterestCount} " +
                $"personnel={fighter.RoamPersonnelInterestCount} " +
                $"reception={fighter.HasReceptionRoamInterest} player={fighter.HasPlayerRoamInterest}");
        }

    }

    private static void ObserveIndependentRoaming(EnemyFighter[] fighters)
    {
        List<EnemyFighter> combatFighters = GetCombatFighters(fighters);
        observedStateSignatures.Add(BuildStateSignature(combatFighters));
        for (int i = 0; i < combatFighters.Count; i++)
        {
            EnemyFighter fighter = combatFighters[i];
            MixamoScanRetargetAnimator.MotionState currentState = fighter.AnimationState;
            if (currentState == MixamoScanRetargetAnimator.MotionState.Idle &&
                previousRoamAnimationStates.TryGetValue(
                    fighter, out MixamoScanRetargetAnimator.MotionState previousState) &&
                previousState == MixamoScanRetargetAnimator.MotionState.Running &&
                fighter.IsRoaming && fighter.CurrentRoamRouteRemaining > 0 &&
                fighter.CurrentRoamBlockedTime < 0.2f)
            {
                throw new InvalidOperationException(
                    $"{fighter.Identity} switched Run->Idle mid-route without " +
                    $"a direction change: routeRemaining={fighter.CurrentRoamRouteRemaining}.");
            }
            previousRoamAnimationStates[fighter] = currentState;
            maximumObservedBlockedTime = Mathf.Max(
                maximumObservedBlockedTime, fighter.CurrentRoamBlockedTime);
            if (fighter.AnimationState == MixamoScanRetargetAnimator.MotionState.Idle)
            {
                observedIdle = true;
                if (fighter.IsRoaming && fighter.CurrentPlanarSpeed > 0.08f)
                {
                    throw new InvalidOperationException(
                        $"{fighter.Identity} moved while its animation state was Idle: " +
                        $"speed={fighter.CurrentPlanarSpeed:0.000}.");
                }
            }
            if (fighter.AnimationState == MixamoScanRetargetAnimator.MotionState.Running)
            {
                observedRunning = true;
            }
            if (fighter.CurrentRoamBlockedTime > 0.95f)
            {
                throw new InvalidOperationException(
                    $"{fighter.Identity} remained blocked too long without a route recovery: " +
                    $"blocked={fighter.CurrentRoamBlockedTime:0.00}s.");
            }
        }
    }

    private static void ObserveRonnieTarget(EnemyFighter[] fighters, double elapsed)
    {
        if (elapsed < 3.2d)
        {
            return;
        }

        EnemyFighter ronnie = FindIdentity(fighters, BodybuilderIdentity.Ronnie);
        if (ronnie == null)
        {
            throw new InvalidOperationException("Ronnie was not found in the behavior verification scene.");
        }

        if (!ronnie.IsAggressive && (ronnie.IsOnTreadmill ||
            (!string.IsNullOrEmpty(ronnie.CurrentRoamInterestLabel) &&
             (ronnie.CurrentRoamInterestLabel.IndexOf("smith", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
              ronnie.CurrentRoamInterestLabel.IndexOf("squat", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
              ronnie.CurrentRoamInterestLabel.IndexOf("cage", System.StringComparison.OrdinalIgnoreCase) >= 0))))
        {
            throw new InvalidOperationException(
                $"Ronnie selected gym equipment during neutral patrol: " +
                $"interest={ronnie.CurrentRoamInterestLabel ?? "none"} " +
                $"onTreadmill={ronnie.IsOnTreadmill}.");
        }

        if (!ronnieDirectAggroTested && elapsed >= 4.4d)
        {
            ronnie.TakeMeleeHit(Vector3.zero, 1f, 0.1f);
            if (!ronnie.IsAggressive || ronnie.CurrentTarget != player.transform ||
                ronnie.IsRoaming || ronnie.HasRoamDestination)
            {
                throw new InvalidOperationException(
                    "Ronnie did not become aggressive toward the player after direct damage.");
            }

            ronnieDirectAggroTested = true;
            Debug.Log("GYMCHAOS_ENEMY_RONNIE_DIRECT_AGGRO_OK target=Player");
        }

        if (ronnie.CurrentTarget == null)
        {
            throw new InvalidOperationException(
                "Ronnie did not select a fight participant after an enemy became aggressive.");
        }

        if (ronnieDirectAggroTested)
        {
            if (!ronnie.IsAggressive || ronnie.CurrentTarget != player.transform)
            {
                throw new InvalidOperationException(
                    "Ronnie did not keep pursuing the player after direct damage.");
            }

            ronnieMismatchSince = -1d;
            return;
        }

        Transform nearest = FindNearestFightParticipant(ronnie, fighters);
        if (nearest != null && ronnie.CurrentTarget != nearest)
        {
            if (ronnieMismatchSince < 0d)
            {
                ronnieMismatchSince = EditorApplication.timeSinceStartup;
                return;
            }

            if (EditorApplication.timeSinceStartup - ronnieMismatchSince <= 0.35d)
            {
                return;
            }

            throw new InvalidOperationException(
                $"Ronnie target was not the nearest participant: current={ronnie.CurrentTarget.name}, " +
                $"nearest={nearest.name} ronniePos={ronnie.transform.position} " +
                $"currentPos={ronnie.CurrentTarget.position} nearestPos={nearest.position} " +
                $"playerPos={(player != null ? player.transform.position.ToString() : "null")}.");
        }

        ronnieMismatchSince = -1d;
    }

    private static void ObserveTreadmill()
    {
        if (!treadmillPending)
        {
            return;
        }

        if (treadmillUser == null || treadmillStation == null)
        {
            throw new InvalidOperationException("Enemy treadmill session was not held by its reserving enemy.");
        }

        if (!treadmillStation.IsOccupiedByEnemy ||
            treadmillUser.CurrentTreadmill != treadmillStation)
        {
            if (EditorApplication.timeSinceStartup < treadmillDeadline)
            {
                return;
            }

            throw new InvalidOperationException(
                $"{treadmillUser.Identity} did not reach and enter its queued treadmill destination: " +
                $"position={treadmillUser.transform.position} target={treadmillStation.EnemyPosition} " +
                $"distance={treadmillUser.CurrentRoamTargetDistance:0.00} " +
                $"blocked={treadmillUser.CurrentRoamBlockedTime:0.00}s " +
                $"state={treadmillUser.AnimationState} " +
                $"routeRemaining={treadmillUser.CurrentRoamRouteRemaining}.");
        }

        if (treadmillStation.IsAvailableForPlayer ||
            treadmillStation.IsAvailableForEnemy(aggressionTarget))
        {
            throw new InvalidOperationException(
                "Treadmill occupancy did not block the player and other enemies.");
        }

        // Occupancy is reserved at the beginning of the authored entry
        // interpolation. Do not validate the final facing until the fighter
        // has actually arrived at the belt; sampling this frame used to make
        // a correct entry fail while the body was still travelling there.
        float treadmillPositionError = Vector3.ProjectOnPlane(
            treadmillUser.transform.position - treadmillStation.EnemyPosition,
            Vector3.up).magnitude;
        if (treadmillPositionError > 0.18f)
        {
            return;
        }

        if (Quaternion.Angle(
                treadmillUser.transform.rotation, treadmillStation.EnemyRotation) > 5f)
        {
            throw new InvalidOperationException(
                $"{treadmillUser.Identity} entered the treadmill with incorrect facing.");
        }

        if (treadmillUser.AnimationState == MixamoScanRetargetAnimator.MotionState.Running)
        {
            treadmillRunningObserved = true;
        }

        if (!treadmillRunningObserved)
        {
            return;
        }

        treadmillUser.EndTreadmillForVerification();
        if (!treadmillRunningObserved)
        {
            throw new InvalidOperationException("Enemy treadmill session did not enter the Running animation state.");
        }

        Debug.Log(
            $"GYMCHAOS_ENEMY_TREADMILL_OK identity={treadmillUser.Identity} " +
            "queuedDestination=true entered=true facing=true " +
            "playerBlocked=true otherEnemyBlocked=true running=true");
        treadmillPending = false;
    }

    private static void FinishBehaviorChecks(EnemyFighter[] fighters)
    {
        List<EnemyFighter> combatFighters = GetCombatFighters(fighters);
        bool moved = false;
        for (int i = 0; i < combatFighters.Count; i++)
        {
            EnemyFighter fighter = combatFighters[i];
            if (baselinePositions.TryGetValue(fighter, out Vector3 start) &&
                Vector3.ProjectOnPlane(fighter.transform.position - start, Vector3.up).magnitude > 0.18f)
            {
                moved = true;
            }

        }

        BodybuilderIdentity[] requestedEarlyWalkers =
        {
            BodybuilderIdentity.Cbum,
            BodybuilderIdentity.Arnold,
            BodybuilderIdentity.Ronnie,
            BodybuilderIdentity.JayCutler
        };
        for (int i = 0; i < requestedEarlyWalkers.Length; i++)
        {
            if (!earlyMovedIdentities.Contains(requestedEarlyWalkers[i]))
            {
                throw new InvalidOperationException(
                    $"{requestedEarlyWalkers[i]} did not move during the independent startup-roam window: " +
                    $"running={FormatIdentities(earlyRunningIdentities)} " +
                    $"moved={FormatIdentities(earlyMovedIdentities)}.");
            }
        }

        EnemyFighter jay = FindIdentity(fighters, BodybuilderIdentity.JayCutler);
        if (jay == null || !baselinePositions.TryGetValue(jay, out Vector3 jayStart) ||
            Vector3.ProjectOnPlane(jay.transform.position - jayStart, Vector3.up).magnitude <= 0.35f)
        {
            throw new InvalidOperationException(
                "Jay Cutler did not continue roaming after startup: " +
                $"position={jay?.transform.position.ToString() ?? "missing"}.");
        }

        if (!moved || !observedRunning || !observedIdle || observedStateSignatures.Count < 2 ||
            !observedRoomWideTarget ||
            !observedPurposefulTarget ||
            !observedInterestCoverage ||
            earlyRunningIdentities.Count < 2 || earlyIdleIdentities.Count < 1)
        {
            throw new InvalidOperationException(
                $"Independent roaming was not observed: moved={moved} running={observedRunning} " +
                $"idle={observedIdle} signatures={observedStateSignatures.Count} " +
                $"roomWideTarget={observedRoomWideTarget} purposefulTarget={observedPurposefulTarget} " +
                $"interestCoverage={observedInterestCoverage} " +
                $"maxBlocked={maximumObservedBlockedTime:0.00} " +
                $"earlyRunning={FormatIdentities(earlyRunningIdentities)} " +
                $"earlyIdle={FormatIdentities(earlyIdleIdentities)}.");
        }

        Debug.Log(
            $"GYMCHAOS_ENEMY_BEHAVIOR_OK moved={moved} running={observedRunning} " +
            $"idle={observedIdle} independentStateSignatures={observedStateSignatures.Count} " +
            $"roomWideTarget={observedRoomWideTarget} purposefulTarget={observedPurposefulTarget} " +
            $"interestCoverage={observedInterestCoverage} " +
            $"maxBlocked={maximumObservedBlockedTime:0.00} " +
            $"earlyRunning={FormatIdentities(earlyRunningIdentities)} " +
            $"earlyIdle={FormatIdentities(earlyIdleIdentities)} " +
            $"earlyMoved={FormatIdentities(earlyMovedIdentities)}");
        EditorApplication.isPlaying = false;
    }

    private static string FormatIdentities(HashSet<BodybuilderIdentity> identities)
    {
        List<string> names = new List<string>();
        foreach (BodybuilderIdentity identity in identities)
        {
            names.Add(identity.ToString());
        }
        names.Sort(StringComparer.Ordinal);
        return names.Count > 0 ? string.Join(",", names) : "none";
    }

    private static int CountCombatFighters(EnemyFighter[] fighters)
    {
        int count = 0;
        for (int i = 0; i < fighters.Length; i++)
        {
            if (IsCombatFighter(fighters[i]))
            {
                count++;
            }
        }
        return count;
    }

    private static List<EnemyFighter> GetCombatFighters(EnemyFighter[] fighters)
    {
        List<EnemyFighter> result = new List<EnemyFighter>();
        for (int i = 0; i < fighters.Length; i++)
        {
            if (IsCombatFighter(fighters[i]))
            {
                result.Add(fighters[i]);
            }
        }
        result.Sort((left, right) => left.Identity.CompareTo(right.Identity));
        return result;
    }

    private static bool IsCombatFighter(EnemyFighter fighter)
    {
        // Ronnie is a police fighter, but he is still part of the room-wide
        // roaming behavior that this verifier is meant to observe.
        return fighter != null &&
            fighter.Identity != BodybuilderIdentity.Manwithsuit1;
    }

    private static EnemyFighter FindIdentity(EnemyFighter[] fighters, BodybuilderIdentity identity)
    {
        for (int i = 0; i < fighters.Length; i++)
        {
            if (fighters[i] != null && fighters[i].Identity == identity)
            {
                return fighters[i];
            }
        }
        return null;
    }

    private static Transform FindNearestFightParticipant(EnemyFighter source, EnemyFighter[] fighters)
    {
        Transform nearest = player != null && !player.IsDead && !player.IsExercising
            ? player.transform : null;
        float nearestDistance = nearest != null
            ? PlanarDistanceSquared(source.transform.position, nearest.position)
            : float.PositiveInfinity;

        for (int i = 0; i < fighters.Length; i++)
        {
            EnemyFighter candidate = fighters[i];
            if (!IsCombatFighter(candidate) || candidate == source || candidate.IsDead ||
                !candidate.IsAggressive)
            {
                continue;
            }

            float distance = PlanarDistanceSquared(
                source.transform.position, candidate.transform.position);
            if (distance < nearestDistance)
            {
                nearest = candidate.transform;
                nearestDistance = distance;
            }
        }

        return nearest;
    }

    private static float PlanarDistanceSquared(Vector3 left, Vector3 right)
    {
        Vector3 offset = Vector3.ProjectOnPlane(right - left, Vector3.up);
        return offset.sqrMagnitude;
    }

    private static string BuildStateSignature(List<EnemyFighter> fighters)
    {
        int idle = 0;
        int running = 0;
        int punching = 0;
        for (int i = 0; i < fighters.Count; i++)
        {
            switch (fighters[i].AnimationState)
            {
                case MixamoScanRetargetAnimator.MotionState.Running:
                    running++;
                    break;
                case MixamoScanRetargetAnimator.MotionState.Punching:
                    punching++;
                    break;
                default:
                    idle++;
                    break;
            }
        }
        return $"idle={idle};running={running};punching={punching}";
    }
}
