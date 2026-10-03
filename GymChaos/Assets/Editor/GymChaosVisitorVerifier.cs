using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class GymChaosVisitorVerifier
{
    private const string VerificationRequestedKey =
        "GymChaos.VisitorVerificationRequested";
    private const string OriginalSaveKey =
        "GymChaos.VisitorVerificationOriginalSave";
    private const string OriginalSavePresentKey =
        "GymChaos.VisitorVerificationOriginalSavePresent";
    private const string ProgressionSaveKey = "GymChaos.Progression.v1";

    private static double enteredPlayTime;
    private static bool verificationCompleted;
    private static bool verificationFailed;
    private static PlayerMovement isolatedPlayer;
    private static CharacterController isolatedPlayerController;
    private static Vector3 savedPlayerPosition;
    private static Quaternion savedPlayerRotation;
    private static bool savedPlayerMovementEnabled;
    private static bool savedPlayerControllerEnabled;
    private static bool playerIsolationActive;
    private static bool sceneValidated;
    private static bool entryRequested;
    private static bool entryMoved;
    private static bool entryConfirmed;
    private static EnemyFighter entryFighter;
    private static Vector3 entryStartPosition;
    private static bool workoutRequested;
    private static bool workoutMoved;
    private static bool workoutStarted;
    private static bool workoutCompleted;
    private static EnemyFighter workoutFighter;
    private static GymExerciseStation workoutStation;
    private static int workoutVersionBefore;
    private static Vector3 workoutStartPosition;
    private static bool workoutNeedsMovement;
    private static bool workoutEnteredStation;
    private static bool workoutBarAttached;
    private static double squatDepthWaitStarted = -1d;
    private static bool workoutPoseValidated;
    private const float PoseSampleMaximumDeltaTime = 0.04f;
    private static float savedMaximumDeltaTime = -1f;

    private static void SetPoseSamplingBound(bool bounded)
    {
        if (bounded && savedMaximumDeltaTime < 0f)
        {
            savedMaximumDeltaTime = Time.maximumDeltaTime;
            Time.maximumDeltaTime = PoseSampleMaximumDeltaTime;
            Debug.Log("GYMCHAOS_VISITOR_POSE_SAMPLING_BOUND on");
        }
        else if (!bounded && savedMaximumDeltaTime >= 0f)
        {
            Time.maximumDeltaTime = savedMaximumDeltaTime;
            savedMaximumDeltaTime = -1f;
            Debug.Log("GYMCHAOS_VISITOR_POSE_SAMPLING_BOUND off maximumDeltaTime=" + Time.maximumDeltaTime);
        }
    }
    private static double workoutCompletionTime;
    private static double workoutRequestedAt;
    private static double departureStartedAt;
    private static Vector3 workoutCompletionPosition;
    private static bool workoutReleaseMoveValidated;
    private static bool groundingVerificationLogged;
    private static bool departureRequested;
    private static EnemyFighter departureFighter;
    private static GymVisitorVehicle departureVehicle;
    private static int departureApproachVersion;
    private static EnemyFighter groundingViolationFighter;
    private static int groundingViolationFrames;
    private static double nextEntrySampleAt;
    private static double nextWorkoutPoseDiagnosticAt;
    private static int workoutPoseDiagnosticSamples;

    static GymChaosVisitorVerifier()
    {
        if (!GymChaosVerifierPrefs.GetBool(VerificationRequestedKey, false))
        {
            return;
        }

        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        EditorApplication.delayCall += ResumeAfterDomainReload;
    }

    [MenuItem("Tools/GymChaos/Run Visitor and Time Verification")]
    public static void Run()
    {
        string originalSave = PlayerPrefs.GetString(ProgressionSaveKey, string.Empty);
        GymChaosVerifierPrefs.SetBool(OriginalSavePresentKey,
            PlayerPrefs.HasKey(ProgressionSaveKey));
        GymChaosVerifierPrefs.SetString(OriginalSaveKey, originalSave);
        // Visitor verification expects the roster to remain neutral until the
        // test explicitly asks one visitor to enter. Do not let a player's
        // negative-reputation auto-target rule cancel the roster first.
        PlayerPrefs.DeleteKey(ProgressionSaveKey);
        PlayerPrefs.Save();

        ResetState();
        GymChaosVerifierPrefs.SetBool(VerificationRequestedKey, true);
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        EditorApplication.isPlaying = true;
    }

    private static void ResetState()
    {
        enteredPlayTime = 0d;
        verificationCompleted = false;
        verificationFailed = false;
        sceneValidated = false;
        entryRequested = false;
        entryMoved = false;
        entryConfirmed = false;
        entryFighter = null;
        entryStartPosition = Vector3.zero;
        workoutRequested = false;
        workoutMoved = false;
        workoutStarted = false;
        workoutCompleted = false;
        workoutFighter = null;
        workoutStation = null;
        workoutVersionBefore = 0;
        workoutStartPosition = Vector3.zero;
        workoutNeedsMovement = false;
        workoutEnteredStation = false;
        workoutBarAttached = false;
        workoutPoseValidated = false;
        squatDepthWaitStarted = -1d;
        workoutCompletionTime = 0d;
        workoutRequestedAt = 0d;
        departureStartedAt = 0d;
        workoutCompletionPosition = Vector3.zero;
        workoutReleaseMoveValidated = false;
        groundingVerificationLogged = false;
        departureRequested = false;
        departureFighter = null;
        departureVehicle = null;
        departureApproachVersion = 0;
        groundingViolationFighter = null;
        groundingViolationFrames = 0;
        nextEntrySampleAt = 0d;
        nextWorkoutPoseDiagnosticAt = 0d;
        workoutPoseDiagnosticSamples = 0;
        isolatedPlayer = null;
        isolatedPlayerController = null;
        savedPlayerPosition = Vector3.zero;
        savedPlayerRotation = Quaternion.identity;
        savedPlayerMovementEnabled = false;
        savedPlayerControllerEnabled = false;
        playerIsolationActive = false;
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
            // Hold scheduled NPC behavior until the deterministic scene checks finish.
            Time.timeScale = 0f;
            enteredPlayTime = EditorApplication.timeSinceStartup;
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
        }
        else if (state == PlayModeStateChange.EnteredEditMode)
        {
            Time.timeScale = 1f;
            SetPoseSamplingBound(false);
            RestorePlayerAfterRouteScenario();
            RestoreOriginalProgressionSave();
            GymChaosVerifierPrefs.DeleteKey(VerificationRequestedKey);
            EditorApplication.update -= Tick;
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            if (Application.isBatchMode)
            {
                EditorApplication.Exit(verificationCompleted && !verificationFailed ? 0 : 1);
            }
        }
    }

    private static void Tick()
    {
        try
        {
            double elapsed = EditorApplication.timeSinceStartup - enteredPlayTime;
            PlayerMovement player = UnityEngine.Object.FindAnyObjectByType<PlayerMovement>();
            GymVisitorDirector director =
                UnityEngine.Object.FindAnyObjectByType<GymVisitorDirector>();
            GymTimeOfDay timeOfDay = UnityEngine.Object.FindAnyObjectByType<GymTimeOfDay>();
            GymDoorway doorway = UnityEngine.Object.FindAnyObjectByType<GymDoorway>();
            if (player == null || director == null || timeOfDay == null || doorway == null)
            {
                if (elapsed > 25d)
                {
                    throw new InvalidOperationException(
                        "Visitor verification scene did not initialize its runtime systems.");
                }
                return;
            }

            if (!sceneValidated && elapsed > 1.5d)
            {
                ValidateScene(director, timeOfDay, doorway);
                IsolatePlayerFromRouteScenario(player);
                Time.timeScale = 2f;
                sceneValidated = true;
                Debug.Log(
                    $"GYMCHAOS_VISITOR_SCENE_OK eligible={director.EligibleEnemyCount} " +
                    $"active={director.ActiveVisitorCount}");
            }

            if (sceneValidated && !entryRequested && elapsed > 3d &&
                director.BeginEntryForVerification(out entryFighter))
            {
                entryRequested = true;
                entryStartPosition = entryFighter.transform.position;
                Debug.Log(
                    $"GYMCHAOS_VISITOR_ENTRY_TEST_STARTED enemy={entryFighter.Identity}");
            }

            if (entryRequested && entryFighter != null)
            {
                GymVisitorAgent agent = entryFighter.GetComponent<GymVisitorAgent>();
                float distance = Vector3.ProjectOnPlane(
                    entryFighter.transform.position - entryStartPosition, Vector3.up).magnitude;
                entryMoved |= distance > 0.15f;
                entryConfirmed |= agent != null && agent.HasEnteredGym;
                if (agent != null && elapsed >= nextEntrySampleAt)
                {
                    nextEntrySampleAt = elapsed + 0.75d;
                    Rigidbody entryBody = entryFighter.GetComponent<Rigidbody>();
                    Debug.Log(
                        $"GYMCHAOS_VISITOR_ENTRY_SAMPLE enemy={entryFighter.Identity} " +
                        $"state={agent.State} pos={entryFighter.transform.position} " +
                        $"target={agent.TravelTargetForVerification} " +
                        $"waypoint={agent.VehicleEntryWaypointForVerification} " +
                        $"waitOwner={agent.DoorwayRouteWaitOwnerForVerification} " +
                        $"vel={(entryBody != null ? entryBody.linearVelocity : Vector3.zero)} " +
                        $"blocker={entryFighter.LastVisitorRouteBlocker} " +
                        $"capsule={DescribeEntryCapsule(entryFighter, agent.TravelTargetForVerification)}");
                }
            }

            if (sceneValidated && entryConfirmed && !workoutRequested && elapsed > 4d &&
                director.BeginWorkoutForVerification(
                    out workoutFighter, out workoutStation))
            {
                workoutRequested = true;
                workoutRequestedAt = elapsed;
                workoutVersionBefore = workoutFighter
                    .GetComponent<GymVisitorAgent>().CompletedWorkoutVersion;
                workoutStartPosition = workoutFighter.transform.position;
                workoutNeedsMovement = Vector3.ProjectOnPlane(
                    workoutStation.EnemyPosition - workoutStartPosition, Vector3.up).magnitude > 0.5f;
                nextWorkoutPoseDiagnosticAt = elapsed;
                workoutPoseDiagnosticSamples = 0;
                Debug.Log(
                    $"GYMCHAOS_VISITOR_WORKOUT_TEST_STARTED enemy={workoutFighter.Identity} " +
                    $"station={workoutStation.EquipmentName}");
            }

            if (workoutRequested && workoutFighter != null)
            {
                GymVisitorAgent agent = workoutFighter.GetComponent<GymVisitorAgent>();
                GymExerciseStation activeStation = agent != null
                    ? agent.WorkoutStationForVerification
                    : null;
                if (activeStation != null && activeStation != workoutStation)
                {
                    string previousStation = workoutStation != null
                        ? workoutStation.EquipmentName
                        : "none";
                    workoutStation = activeStation;
                    if (!workoutStarted)
                    {
                        workoutNeedsMovement = Vector3.ProjectOnPlane(
                            workoutStation.EnemyPosition - workoutStartPosition,
                            Vector3.up).magnitude > 0.5f;
                    }
                    Debug.LogWarning(
                        $"GYMCHAOS_SQUAT_STATION_CONTRACT_UPDATED " +
                        $"enemy={workoutFighter.Identity} from={previousStation} " +
                        $"to={workoutStation.EquipmentName}");
                }
                if (!workoutStarted && agent != null && elapsed >= nextWorkoutPoseDiagnosticAt)
                {
                    nextWorkoutPoseDiagnosticAt = elapsed + 0.75d;
                    Rigidbody approachBody = workoutFighter.GetComponent<Rigidbody>();
                    Debug.Log(
                        $"GYMCHAOS_VISITOR_WORKOUT_APPROACH_SAMPLE elapsed={elapsed:0.00} " +
                        $"enemy={workoutFighter.Identity} state={agent.State} busy={agent.IsBusy} " +
                        $"position={workoutFighter.transform.position} " +
                        $"target={agent.TravelTargetForVerification} " +
                        $"station={workoutStation?.EquipmentName} " +
                        $"stationPosition={workoutStation?.EnemyPosition} " +
                        $"blocker={workoutFighter.LastVisitorRouteBlocker} " +
                        $"velocity={(approachBody != null ? approachBody.linearVelocity : Vector3.zero)}");
                }

                if (agent != null && workoutFighter != null &&
                    workoutPoseDiagnosticSamples < 24 && elapsed >= nextWorkoutPoseDiagnosticAt)
                {
                    SquatWorkoutController diagnosticSquat =
                        workoutFighter.GetComponent<SquatWorkoutController>();
                    bool diagnosticAgentActive = agent.IsWorkoutActive;
                    bool diagnosticSquatActive = diagnosticSquat != null && diagnosticSquat.IsActive;
                    bool diagnosticMotionActive = diagnosticSquat != null && diagnosticSquat.CurrentMotion > 0.15f;
                    if (diagnosticAgentActive || diagnosticSquatActive || diagnosticMotionActive)
                    {
                        MixamoScanRetargetAnimator diagnosticAnimator =
                            workoutFighter.GetComponentInChildren<MixamoScanRetargetAnimator>(true);
                        string diagnosticActiveStation = agent.WorkoutStationForVerification != null
                            ? agent.WorkoutStationForVerification.EquipmentName
                            : "none";
                        string diagnosticOccupant = workoutStation != null &&
                            workoutStation.EnemyOccupant != null
                            ? workoutStation.EnemyOccupant.Identity.ToString()
                            : "none";
                        Debug.Log(
                            $"GYMCHAOS_VISITOR_WORKOUT_POSE_SAMPLE " +
                            $"elapsed={elapsed:0.00} enemy={workoutFighter.Identity} " +
                            $"state={agent.State} started={workoutStarted} " +
                            $"agentActive={diagnosticAgentActive} squatActive={diagnosticSquatActive} " +
                            $"motion={diagnosticSquat?.CurrentMotion ?? 0f:0.000} " +
                            $"activeStation={diagnosticActiveStation} " +
                            $"expectedStation={workoutStation?.EquipmentName ?? "none"} " +
                            $"occupant={diagnosticOccupant} " +
                            $"occupantMatches={workoutStation != null && workoutStation.EnemyOccupant == workoutFighter} " +
                            $"authoredLocked={diagnosticAnimator != null && diagnosticAnimator.IsWorkoutPoseLocked} " +
                            $"authoredSquat={diagnosticAnimator != null && diagnosticAnimator.HasAuthoredSquatClip} " +
                            $"clip={diagnosticAnimator?.CurrentAnimationClipName ?? "none"} " +
                            $"rig={diagnosticSquat != null && diagnosticSquat.HasValidSquatRig} " +
                            $"arms={diagnosticSquat != null && diagnosticSquat.HasValidArmRig}");
                        workoutPoseDiagnosticSamples++;
                        nextWorkoutPoseDiagnosticAt = elapsed + 0.35d;
                    }
                }

                if (workoutStarted && !workoutCompleted &&
                    !workoutFighter.gameObject.activeInHierarchy)
                {
                    throw new InvalidOperationException(
                        $"Squat visitor despawned before completion: {workoutFighter.Identity}.");
                }

                float distance = Vector3.ProjectOnPlane(
                    workoutFighter.transform.position - workoutStartPosition, Vector3.up).magnitude;
                workoutMoved |= distance > 0.2f;
                workoutStarted |= agent != null && agent.IsWorkoutActive &&
                    workoutStation != null && workoutStation.EnemyOccupant == workoutFighter;
                // Under parallel regression load one editor frame advanced up
                // to 0.33 s of game time (x2 time scale), so the short rep's
                // motion>0.35 window fell between two samples (seen:
                // 0.08, 0.24, 0.32, then done). Bound game time per frame
                // until the pose is validated.
                SetPoseSamplingBound(workoutStarted && !workoutPoseValidated);
                if (workoutStarted && workoutStation != null && agent != null &&
                    agent.IsWorkoutActive && workoutStation.EnemyOccupant == workoutFighter)
                {
                    SquatWorkoutController squat =
                        workoutFighter.GetComponent<SquatWorkoutController>();
                    float stationDistance = Vector3.ProjectOnPlane(
                        workoutFighter.transform.position - workoutStation.EnemyPosition,
                        Vector3.up).magnitude;
                    // MoveVisitorTo stops at 0.34m from the authored centre;
                    // keep the verification tolerance tight enough to catch
                    // the old front-edge-of-cage staging bug.
                    workoutEnteredStation = stationDistance < 0.42f;
                    workoutBarAttached = workoutStation.IsEnemySquatBarAttached &&
                        squat != null && squat.Traps != null &&
                        Vector3.Distance(
                            workoutStation.EnemySquatBarCenter,
                            squat.BarTargetPosition) < 0.35f &&
                        workoutStation.EnemySquatBarAxisError < 0.08f &&
                        workoutStation.EnemySquatBarTiltError < 0.08f;

                    // This visitor smoke schedules short workouts. Validate an active mid-range squat pose here; the dedicated foot-plant verifier samples full depth and return.
                    if (!workoutPoseValidated && squat != null && squat.CurrentMotion > 0.35f)
                    {
                        MixamoScanRetargetAnimator authoredAnimator =
                            workoutFighter.GetComponentInChildren<MixamoScanRetargetAnimator>(true);
                        Vector3 actualFacing = Vector3.ProjectOnPlane(
                            workoutFighter.transform.forward, Vector3.up).normalized;
                        Vector3 expectedFacing = Vector3.ProjectOnPlane(
                            workoutStation.EnemyRotation * Vector3.forward,
                            Vector3.up).normalized;
                        float squatFacingDot = Vector3.Dot(actualFacing, expectedFacing);
                        // The station occupancy flag can become visible one
                        // frame before the authored rack bar has finished its
                        // attachment.  Validate staging at the first real
                        // squat pose, not during that handoff frame.
                        if (!workoutEnteredStation || !workoutBarAttached)
                        {
                            throw new InvalidOperationException(
                                $"Squat station staging failed: inside={workoutEnteredStation} " +
                                $"barOnTraps={workoutBarAttached} distance={stationDistance:0.00} " +
                                $"axisError={workoutStation.EnemySquatBarAxisError:0.000} " +
                                $"tilt={workoutStation.EnemySquatBarTiltError:0.000}.");
                        }

                        // The motion gate opens early in the descent. Wait (at most
                        // 3 s) for the bar to reach mid-depth so a single early
                        // sample cannot fail an otherwise correct rep.
                        if (squatDepthWaitStarted < 0d)
                        {
                            squatDepthWaitStarted = EditorApplication.timeSinceStartup;
                        }
                        bool waitingForDepth = Mathf.Abs(squat.BarDropFromStart) < 0.035f &&
                            EditorApplication.timeSinceStartup - squatDepthWaitStarted < 3d;
                        bool authoredSquatActive = authoredAnimator != null &&
                            authoredAnimator.IsWorkoutPoseLocked &&
                            authoredAnimator.HasAuthoredSquatClip &&
                            authoredAnimator.CurrentAnimationClipName.IndexOf(
                                "squat", StringComparison.OrdinalIgnoreCase) >= 0;
                        if (waitingForDepth)
                        {
                            // Keep sampling this rep on the next tick.
                        }
                        else if (!authoredSquatActive || !squat.HasValidSquatRig ||
                            !squat.HasValidArmRig || !(squat.CurrentHipDrop >= 0.20f || squat.CurrentKneeBend >= 20f) ||
                            squat.BarBodyFollowError > 0.10f ||
                            Mathf.Abs(squat.BarDropFromStart) < 0.035f ||
                            squatFacingDot < 0.98f)
                        {
                            throw new InvalidOperationException(
                                $"Squat authored clip validation failed: authored={authoredSquatActive} " +
                                $"valid={squat.HasValidSquatRig} " +
                                $"arms={squat.HasValidArmRig} " +
                                $"hipDrop={squat.CurrentHipDrop:0.000} " +
                                $"kneeBend={squat.CurrentKneeBend:0.0} " +
                                $"barFollow={squat.BarBodyFollowError:0.000} " +
                                $"barDrop={squat.BarDropFromStart:0.000} " +
                                $"facingDot={squatFacingDot:0.000}.");
                        }
                        else
                        {
                            workoutPoseValidated = true;
                            Debug.Log(
                                $"GYMCHAOS_SQUAT_POSE_OK enemy={workoutFighter.Identity} " +
                                "mode=authored-clip-only " +
                                $"hipDrop={squat.CurrentHipDrop:0.000} " +
                                $"kneeBend={squat.CurrentKneeBend:0.0} " +
                                $"barFollow={squat.BarBodyFollowError:0.000} " +
                                $"barDrop={squat.BarDropFromStart:0.000} " +
                                $"facingDot={squatFacingDot:0.000} " +
                                $"barOnTraps={workoutBarAttached} " +
                                $"centerDistance={stationDistance:0.000}");
                        }
                    }
                }
                // Completion is handed off in the visitor agent's Update.  The
                // station/bar state can be visible for one editor-update tick
                // before the agent changes its public state to FreeRoaming.
                // Wait for the complete handoff and record it only once; the
                // verifier must not turn that normal frame ordering into a
                // false failure or emit the completion marker repeatedly.
                if (!workoutCompleted && agent != null && workoutStarted &&
                    !agent.IsWorkoutActive &&
                    agent.CompletedWorkoutVersion > workoutVersionBefore &&
                    workoutStation != null &&
                    workoutStation.EnemyOccupant != workoutFighter &&
                    workoutStation.IsSquatBarOnRack &&
                    agent.State == GymVisitorAgent.VisitorState.FreeRoaming)
                {
                    workoutCompleted = true;
                    workoutCompletionTime = elapsed;
                    workoutCompletionPosition = workoutFighter.transform.position;
                    Debug.Log(
                        $"GYMCHAOS_SQUAT_BAR_RETURNED enemy={workoutFighter.Identity} " +
                        $"station={workoutStation.EquipmentName} barOnRack={workoutStation.IsSquatBarOnRack}");
                }
            }

            ValidateQuotas(director);
            ValidateNoInGymVisitorDeactivation();
            // Imported FBX rigs need their first height/foot settle pass and
            // a complete animation frame before a contact measurement is
            // meaningful.  Keep this runtime check strict after that short
            // startup window instead of failing on the initial transition.
            if (elapsed > 6d)
            {
                ValidateGroundedVisitors();
            }

            if (entryRequested && entryConfirmed && !entryMoved)
            {
                throw new InvalidOperationException(
                    "Visitor entry was confirmed without physical movement from the exterior point.");
            }

            if (workoutCompleted)
            {
                if (!workoutReleaseMoveValidated)
                {
                    if (elapsed - workoutCompletionTime < 2.5d)
                    {
                        return;
                    }

                    float releaseDistance = Vector3.ProjectOnPlane(
                        workoutFighter.transform.position - workoutCompletionPosition,
                        Vector3.up).magnitude;
                    if (releaseDistance < 0.45f)
                    {
                        throw new InvalidOperationException(
                            $"Completed squat visitor did not resume free roaming: " +
                            $"enemy={workoutFighter.Identity} " +
                            $"releaseDistance={releaseDistance:0.00}.");
                    }

                    workoutReleaseMoveValidated = true;
                    Debug.Log(
                        $"GYMCHAOS_SQUAT_RELEASE_MOVE_OK enemy={workoutFighter.Identity} " +
                        $"distance={releaseDistance:0.00}");
                }

                if (!entryRequested || !entryConfirmed || !entryMoved ||
                    (workoutNeedsMovement && !workoutMoved) ||
                    !workoutEnteredStation || !workoutBarAttached || !workoutPoseValidated)
                {
                    throw new InvalidOperationException(
                        $"Visitor movement verification was incomplete: entryRequested={entryRequested} " +
                        $"entryMoved={entryMoved} entryConfirmed={entryConfirmed} " +
                        $"workoutMoved={workoutMoved} needsWorkoutMovement={workoutNeedsMovement} " +
                        $"insideStation={workoutEnteredStation} barOnTraps={workoutBarAttached} " +
                        $"poseValidated={workoutPoseValidated}.");
                }

                if (!departureRequested && director.BeginDepartureForVerification(
                    out departureFighter, out departureVehicle))
                {
                    GymVisitorAgent departureAgent =
                        departureFighter.GetComponent<GymVisitorAgent>();
                    departureApproachVersion = departureAgent != null
                        ? departureAgent.CompletedVehicleApproaches : 0;
                    departureRequested = true;
                    departureStartedAt = elapsed;
                    Debug.Log($"GYMCHAOS_VISITOR_DEPARTURE_TEST_STARTED enemy={departureFighter.Identity}");
                    return;
                }
                if (!departureRequested || departureFighter == null || departureVehicle == null)
                {
                    return;
                }
                GymVisitorAgent completedDepartureAgent =
                    departureFighter.GetComponent<GymVisitorAgent>();
                bool walkedToVehicle = completedDepartureAgent != null &&
                    completedDepartureAgent.CompletedVehicleApproaches > departureApproachVersion;
                if (!walkedToVehicle || !departureVehicle.HasCompletedDeparture ||
                    departureFighter.gameObject.activeInHierarchy)
                {
                    // This timer includes clearing the doorway and walking to the
                    // parked vehicle, not only the time the vehicle is driving.
                    if (elapsed - departureStartedAt > 180d)
                    {
                        throw new InvalidOperationException(
                            $"Vehicle departure incomplete: walked={walkedToVehicle} " +
                            $"departed={departureVehicle.HasCompletedDeparture} " +
                            $"fighterActive={departureFighter.gameObject.activeInHierarchy} " +
                            $"vehiclePosition={departureVehicle.transform.position} " +
                            $"vehicleDriving={departureVehicle.IsDriving} parked={departureVehicle.IsParked} " +
                            $"speed={departureVehicle.CurrentDriveSpeedForVerification:F2} " +
                            $"routeWaypoint={departureVehicle.CurrentRouteWaypointIndexForVerification} " +
                            $"routeTarget={departureVehicle.CurrentRouteTargetForVerification} " +
                            $"routeRemaining={departureVehicle.CurrentRouteRemainingForVerification:F3} " +
                            $"routeTravel={departureVehicle.CurrentRouteActualTravelForVerification:F4} " +
                            $"routeClearance={departureVehicle.CurrentRouteClearanceForVerification:F3} " +
                            $"routeSource={departureVehicle.CurrentRouteClearanceSourceForVerification} " +
                            $"blocker={departureVehicle.LastTrafficBlockerForVerification} " +
                            $"aisle={departureVehicle.AislePointForVerification} " +
                            $"departureJunction={departureVehicle.DepartureJunctionPointForVerification} " +
                            $"departureTurn={departureVehicle.DepartureRoadTurnPointForVerification} " +
                            $"departureRoad={departureVehicle.DepartureRoadPointForVerification}.");
                    }
                    return;
                }
                Debug.Log(
                    $"GYMCHAOS_VISITOR_VERIFICATION_OK entryMoved={entryMoved} " +
                    $"entryConfirmed={entryConfirmed} workoutMoved={workoutMoved} " +
                    $"workoutStarted={workoutStarted} workoutCompleted={workoutCompleted} " +
                    $"walkedToVehicle={walkedToVehicle} vehicleDeparted={departureVehicle.HasCompletedDeparture} " +
                    "playerIsolation=True");
                verificationCompleted = true;
                RestorePlayerAfterRouteScenario();
                EditorApplication.isPlaying = false;
                return;
            }

            if ((!workoutRequested && elapsed > 95d) ||
                (workoutRequested && !workoutCompleted &&
                    elapsed - workoutRequestedAt > 45d))
            {
                throw new InvalidOperationException(
                    $"Visitor runtime smoke did not finish: entryRequested={entryRequested} " +
                    $"entryMoved={entryMoved} entryConfirmed={entryConfirmed} " +
                    $"workoutRequested={workoutRequested} workoutMoved={workoutMoved} " +
                    $"workoutStarted={workoutStarted} workoutCompleted={workoutCompleted} " +
                    $"workoutState={workoutFighter?.GetComponent<GymVisitorAgent>()?.State} " +
                    $"workoutPosition={workoutFighter?.transform.position} " +
                    $"workoutTarget={workoutFighter?.GetComponent<GymVisitorAgent>()?.TravelTargetForVerification} " +
                    $"workoutStation={workoutStation?.EquipmentName} " +
                    $"workoutStationPosition={workoutStation?.EnemyPosition} " +
                    $"workoutBlocker={workoutFighter?.LastVisitorRouteBlocker}.");
            }
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            verificationFailed = true;
            verificationCompleted = true;
            EditorApplication.update -= Tick;
            GymChaosVerifierPrefs.DeleteKey(VerificationRequestedKey);
            RestorePlayerAfterRouteScenario();
            EditorApplication.isPlaying = false;
        }
    }

    private static void IsolatePlayerFromRouteScenario(PlayerMovement player)
    {
        if (player == null || playerIsolationActive)
        {
            return;
        }

        isolatedPlayer = player;
        isolatedPlayerController = player.GetComponent<CharacterController>();
        savedPlayerPosition = player.transform.position;
        savedPlayerRotation = player.transform.rotation;
        savedPlayerMovementEnabled = player.enabled;
        savedPlayerControllerEnabled = isolatedPlayerController != null &&
            isolatedPlayerController.enabled;
        playerIsolationActive = true;

        player.enabled = false;
        if (isolatedPlayerController != null)
        {
            isolatedPlayerController.enabled = false;
        }

        player.transform.SetPositionAndRotation(
            new Vector3(10000f, savedPlayerPosition.y, 10000f),
            savedPlayerRotation);
        Physics.SyncTransforms();
        Debug.Log(
            "GYMCHAOS_VISITOR_ROUTE_PLAYER_ISOLATED routeOnly=True " +
            "movementDisabled=True colliderDisabled=True");
    }

    private static void RestorePlayerAfterRouteScenario()
    {
        if (!playerIsolationActive)
        {
            return;
        }

        if (isolatedPlayer != null)
        {
            isolatedPlayer.transform.SetPositionAndRotation(
                savedPlayerPosition, savedPlayerRotation);
            if (isolatedPlayerController != null)
            {
                isolatedPlayerController.enabled = savedPlayerControllerEnabled;
            }

            isolatedPlayer.enabled = savedPlayerMovementEnabled;
            Physics.SyncTransforms();
        }

        isolatedPlayer = null;
        isolatedPlayerController = null;
        playerIsolationActive = false;
    }

    private static void RestoreOriginalProgressionSave()
    {
        bool hadOriginalSave = GymChaosVerifierPrefs.GetBool(OriginalSavePresentKey, false);
        string originalSave = GymChaosVerifierPrefs.GetString(OriginalSaveKey, string.Empty);
        if (hadOriginalSave)
        {
            PlayerPrefs.SetString(ProgressionSaveKey, originalSave);
        }
        else
        {
            PlayerPrefs.DeleteKey(ProgressionSaveKey);
        }
        PlayerPrefs.Save();
        GymChaosVerifierPrefs.DeleteKey(OriginalSaveKey);
        GymChaosVerifierPrefs.DeleteKey(OriginalSavePresentKey);
    }

    private static void ValidateScene(
        GymVisitorDirector director, GymTimeOfDay timeOfDay, GymDoorway doorway)
    {
        EnemyFighter[] fighters = UnityEngine.Object.FindObjectsByType<EnemyFighter>(
            FindObjectsInactive.Include);
        Dictionary<BodybuilderIdentity, int> identityCounts =
            new Dictionary<BodybuilderIdentity, int>();
        for (int i = 0; i < fighters.Length; i++)
        {
            if (fighters[i] == null)
            {
                continue;
            }

            BodybuilderIdentity identity = fighters[i].Identity;
            identityCounts.TryGetValue(identity, out int count);
            identityCounts[identity] = count + 1;
        }

        foreach (KeyValuePair<BodybuilderIdentity, int> entry in identityCounts)
        {
            if (entry.Value > 1)
            {
                throw new InvalidOperationException(
                    $"Duplicate enemy identity detected: {entry.Key} count={entry.Value}.");
            }
        }

        EnemyFighter ronnie = FindIdentity(fighters, BodybuilderIdentity.Ronnie);
        EnemyFighter receptionist = FindIdentity(fighters, BodybuilderIdentity.Manwithsuit1);
        if (ronnie == null || !ronnie.gameObject.activeInHierarchy ||
            receptionist == null || !receptionist.gameObject.activeInHierarchy)
        {
            throw new InvalidOperationException(
                "Ronnie and the passive receptionist were not both present at startup.");
        }

        if (director.EligibleEnemyCount != 6 || director.ActiveVisitorCount < 2 ||
            director.ActiveVisitorCount > director.EligibleEnemyCount)
        {
            throw new InvalidOperationException(
                $"Visitor roster invariant failed: eligible={director.EligibleEnemyCount} " +
                $"active={director.ActiveVisitorCount}.");
        }
        ValidateAllSquatRigs(fighters, director.EligibleEnemyCount);

        float doorwayDistance = Vector3.ProjectOnPlane(
            doorway.InteriorPoint - doorway.ExteriorPoint, Vector3.up).magnitude;
        if (doorwayDistance < 3.5f)
        {
            throw new InvalidOperationException(
                $"Doorway navigation points are too close: {doorwayDistance:0.00}m.");
        }
        ValidateReceptionDoor(doorway);
        ValidateWindowsAndGlass();

        GymExerciseStation[] stations = UnityEngine.Object.FindObjectsByType<GymExerciseStation>(
            FindObjectsInactive.Include);
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
            string name = stations[i].EquipmentName.ToLowerInvariant();
            if (name.Contains("cage")) cageCount++;
            if (name.Contains("smith")) smithCount++;
        }

        if (squatCount != 3 || cageCount != 2 || smithCount != 1)
        {
            throw new InvalidOperationException(
                $"Squat capacity invariant failed: stations={squatCount} " +
                $"cages={cageCount} smith={smithCount}.");
        }
        ValidateSquatStationBars(stations);

        int dayBefore = timeOfDay.CurrentDay;
        timeOfDay.SetTimeForVerification(0.88f);
        Renderer moon = GameObject.Find("Exterior visible moon")?.GetComponent<Renderer>();
        Renderer sun = GameObject.Find("Exterior visible sun")?.GetComponent<Renderer>();
        if (!timeOfDay.IsNight || moon == null || !moon.enabled || sun == null || sun.enabled)
        {
            throw new InvalidOperationException("Nighttime moon/sun window visuals were not applied.");
        }

        timeOfDay.SetTimeForVerification(0.24f, true);
        if (timeOfDay.CurrentDay != dayBefore + 1 || timeOfDay.IsNight ||
            !sun.enabled || moon.enabled)
        {
            throw new InvalidOperationException("Daylight and day-boundary reset were not applied.");
        }

        Debug.Log(
            $"GYMCHAOS_TIME_VERIFICATION_OK day={timeOfDay.CurrentDay} " +
            $"nightMoon={moon.enabled} daySun={sun.enabled}");
    }

    private static void ValidateAllSquatRigs(
        EnemyFighter[] fighters, int expectedVisitorCount)
    {
        int validRigCount = 0;
        for (int i = 0; i < fighters.Length; i++)
        {
            EnemyFighter fighter = fighters[i];
            if (fighter == null || fighter.Identity == BodybuilderIdentity.Ronnie ||
                fighter.Identity == BodybuilderIdentity.Manwithsuit1 ||
                fighter.Identity == BodybuilderIdentity.Mark)
            {
                continue;
            }

            SquatWorkoutController squat =
                fighter.GetComponent<SquatWorkoutController>();
            if (squat == null || !squat.HasValidSquatRig ||
                !squat.HasValidArmRig || !squat.HasFingerContactRig)
            {
                throw new InvalidOperationException(
                    $"Squat rig missing for {fighter.Identity}: " +
                    $"controller={squat != null} legs={squat != null && squat.HasValidSquatRig} " +
                    $"arms={squat != null && squat.HasValidArmRig} " +
                    $"fingers={squat != null && squat.HasFingerContactRig}.");
            }

            validRigCount++;
        }

        if (validRigCount != expectedVisitorCount)
        {
            throw new InvalidOperationException(
                $"Squat rig coverage failed: valid={validRigCount} " +
                $"expected={expectedVisitorCount}.");
        }

        Debug.Log($"GYMCHAOS_SQUAT_RIGS_OK count={validRigCount}");
    }

    private static void ValidateSquatStationBars(GymExerciseStation[] stations)
    {
        List<string> stationNames = new List<string>();
        for (int i = 0; i < stations.Length; i++)
        {
            GymExerciseStation station = stations[i];
            if (station == null || !station.IsSquat)
            {
                continue;
            }

            if (!station.HasAuthoredSquatBar)
            {
                throw new InvalidOperationException(
                    $"Squat station has no authored rack bar: {station.EquipmentName}.");
            }

            stationNames.Add(station.EquipmentName);
        }

        stationNames.Sort(StringComparer.Ordinal);
        Debug.Log(
            $"GYMCHAOS_SQUAT_STATION_BARS_OK count={stationNames.Count} " +
            $"stations={string.Join(",", stationNames)}");
    }

    private static void ValidateQuotas(GymVisitorDirector director)
    {
        BodybuilderIdentity[] identities =
        {
            BodybuilderIdentity.Cbum,
            BodybuilderIdentity.Zyzz,
            BodybuilderIdentity.Arnold,
            BodybuilderIdentity.JayCutler,
            BodybuilderIdentity.Goku,
            BodybuilderIdentity.Davie
        };
        for (int i = 0; i < identities.Length; i++)
        {
            if (director.GetVisitCount(identities[i]) > 2 ||
                director.GetWorkoutCount(identities[i]) > 2)
            {
                throw new InvalidOperationException(
                    $"Daily quota exceeded for {identities[i]}: visits=" +
                    $"{director.GetVisitCount(identities[i])} workouts=" +
                    $"{director.GetWorkoutCount(identities[i])}.");
            }
        }
    }

    private static void ValidateGroundedVisitors()
    {
        EnemyFighter[] fighters = UnityEngine.Object.FindObjectsByType<EnemyFighter>(
            FindObjectsSortMode.None);
        float worstError = 0f;
        EnemyFighter frameViolationFighter = null;
        float frameViolationError = 0f;
        for (int i = 0; i < fighters.Length; i++)
        {
            EnemyFighter fighter = fighters[i];
            if (fighter == null || !fighter.gameObject.activeInHierarchy ||
                fighter.IsDead || fighter.IsGokuFlightActive)
            {
                continue;
            }

            // A run/approach or an active workout intentionally has one foot
            // in motion at times. SquatWorkoutController validates both soles
            // directly while the bar is attached; this visitor-level check is
            // for stable non-workout poses and must not interpret a stride as
            // an airborne character.
            GymVisitorAgent visitor = fighter.GetComponent<GymVisitorAgent>();
            if (visitor != null && visitor.IsBusy)
            {
                continue;
            }
            MixamoScanRetargetAnimator animator =
                fighter.GetComponentInChildren<MixamoScanRetargetAnimator>(true);
            if (animator != null &&
                animator.CurrentState != MixamoScanRetargetAnimator.MotionState.Idle)
            {
                continue;
            }

            ExternalRiggedCharacterVisual visual =
                fighter.GetComponent<ExternalRiggedCharacterVisual>();
            if (visual == null)
            {
                continue;
            }

            float contactError = visual.GroundContactError;
            worstError = Mathf.Max(worstError, contactError);
            if (contactError > 0.18f)
            {
                if (contactError > frameViolationError)
                {
                    frameViolationFighter = fighter;
                    frameViolationError = contactError;
                }
            }
        }

        if (frameViolationFighter == null)
        {
            groundingViolationFighter = null;
            groundingViolationFrames = 0;
        }
        else if (groundingViolationFighter == frameViolationFighter)
        {
            groundingViolationFrames++;
        }
        else
        {
            groundingViolationFighter = frameViolationFighter;
            groundingViolationFrames = 1;
        }

        const int requiredConsecutiveViolationFrames = 5;
        if (groundingViolationFrames >= requiredConsecutiveViolationFrames)
        {
            throw new InvalidOperationException(
                $"Grounding failed for {frameViolationFighter.Identity}: " +
                $"contactError={frameViolationError:0.000} " +
                $"consecutiveFrames={groundingViolationFrames}.");
        }

        if (!groundingVerificationLogged && worstError > 0f)
        {
            groundingVerificationLogged = true;
            Debug.Log(
                $"GYMCHAOS_GROUNDING_VERIFICATION_OK worstError={worstError:0.000}");
        }
    }

    private static void ValidateNoInGymVisitorDeactivation()
    {
        EnemyFighter[] fighters = UnityEngine.Object.FindObjectsByType<EnemyFighter>(
            FindObjectsInactive.Include);
        for (int i = 0; i < fighters.Length; i++)
        {
            EnemyFighter fighter = fighters[i];
            if (fighter == null || fighter.gameObject.activeInHierarchy)
            {
                continue;
            }

            GymVisitorAgent agent = fighter.GetComponent<GymVisitorAgent>();
            if (agent != null && agent.HasEnteredGym && !agent.HasCompletedDoorExit)
            {
                throw new InvalidOperationException(
                    $"Visitor became inactive inside the gym: {fighter.Identity} " +
                    $"state={agent.State} entered={agent.HasEnteredGym} " +
                    $"doorExit={agent.HasCompletedDoorExit}.");
            }
        }
    }

    private static void ValidateReceptionDoor(GymDoorway doorway)
    {
        GameObject floor = GameObject.Find("Rubber Floor");
        GameObject desk = GameObject.Find("Reception desk");
        Renderer floorRenderer = floor != null ? floor.GetComponent<Renderer>() : null;
        if (desk == null || floorRenderer == null)
        {
            throw new InvalidOperationException(
                "Reception door validation could not find the floor or reception desk.");
        }

        Renderer[] deskRenderers = desk.GetComponentsInChildren<Renderer>(true);
        bool hasDeskBounds = false;
        Bounds deskBounds = default;
        for (int i = 0; i < deskRenderers.Length; i++)
        {
            if (deskRenderers[i] == null)
            {
                continue;
            }

            if (!hasDeskBounds)
            {
                deskBounds = deskRenderers[i].bounds;
                hasDeskBounds = true;
            }
            else
            {
                deskBounds.Encapsulate(deskRenderers[i].bounds);
            }
        }

        if (!hasDeskBounds)
        {
            throw new InvalidOperationException("Reception desk has no render bounds.");
        }

        Vector3 roomOffset = Vector3.ProjectOnPlane(
            deskBounds.center - floorRenderer.bounds.center, Vector3.up);
        Vector3 doorOffset = Vector3.ProjectOnPlane(
            doorway.InteriorPoint - floorRenderer.bounds.center, Vector3.up);
        float deskDoorDistance = Vector3.ProjectOnPlane(
            doorway.InteriorPoint - deskBounds.center, Vector3.up).magnitude;
        if (roomOffset.sqrMagnitude < 0.01f ||
            Vector3.Dot(roomOffset.normalized, doorOffset.normalized) < 0.5f ||
            deskDoorDistance > 14f)
        {
            throw new InvalidOperationException(
                $"Visitor door is not on the reception/player side: " +
                $"deskDoorDistance={deskDoorDistance:0.00}m.");
        }

        if (!doorway.HasStaticPanelPose)
        {
            throw new InvalidOperationException(
                "Visitor door black inner panel changed its authored position or rotation.");
        }

        const float visitorDoorWidth = 3.25f;
        float openingMinZ = doorway.DoorCenter.z - visitorDoorWidth * 0.5f;
        float openingMaxZ = doorway.DoorCenter.z + visitorDoorWidth * 0.5f;
        Renderer[] allRenderers = UnityEngine.Object.FindObjectsByType<Renderer>(
            FindObjectsInactive.Include);
        for (int i = 0; i < allRenderers.Length; i++)
        {
            Renderer renderer = allRenderers[i];
            if (renderer == null ||
                !renderer.name.ToLowerInvariant().Contains("east wall stripe"))
            {
                continue;
            }

            if (renderer.bounds.max.z > openingMinZ && renderer.bounds.min.z < openingMaxZ)
            {
                throw new InvalidOperationException(
                    "East wall stripe still crosses the visitor doorway opening.");
            }
        }

        Debug.Log(
            $"GYMCHAOS_DOOR_RECEPTION_OK deskDistance={deskDoorDistance:0.00}m " +
            $"door={doorway.InteriorPoint} staticPanel={doorway.HasStaticPanelPose} " +
            "stripeInterrupted=True");
    }

    private static void ValidateWindowsAndGlass()
    {
        Transform runtimeRoot = GameObject.Find("Gym Interior (Runtime)")?.transform;
        if (runtimeRoot == null)
        {
            throw new InvalidOperationException("Runtime gym interior is missing for window validation.");
        }

        Transform[] transforms = runtimeRoot.GetComponentsInChildren<Transform>(true);
        List<Bounds> glassBounds = new List<Bounds>();
        List<Bounds> mullionBounds = new List<Bounds>();
        Renderer sillRenderer = null;
        Renderer headerRenderer = null;
        int glassColliderCount = 0;
        for (int i = 0; i < transforms.Length; i++)
        {
            Transform candidate = transforms[i];
            if (candidate == null)
            {
                continue;
            }

            Renderer renderer = candidate.GetComponent<Renderer>();
            if (candidate.name == "Window glass")
            {
                if (renderer == null)
                {
                    throw new InvalidOperationException("A generated window pane has no renderer.");
                }

                Collider collider = candidate.GetComponent<Collider>();
                if (collider == null || !collider.enabled || collider.isTrigger)
                {
                    throw new InvalidOperationException(
                        "Generated window glass must have an enabled non-trigger collider.");
                }

                glassBounds.Add(renderer.bounds);
                glassColliderCount++;
            }
            else if (candidate.name == "Window mullion" && renderer != null)
            {
                mullionBounds.Add(renderer.bounds);
            }
            else if (candidate.name == "Window sill")
            {
                sillRenderer = renderer;
            }
            else if (candidate.name == "Window header")
            {
                headerRenderer = renderer;
            }
        }

        if (glassBounds.Count != 5 || glassColliderCount != glassBounds.Count ||
            mullionBounds.Count != 6 || sillRenderer == null || headerRenderer == null)
        {
            throw new InvalidOperationException(
                $"Window construction invariant failed: panes={glassBounds.Count} " +
                $"glassColliders={glassColliderCount} mullions={mullionBounds.Count}.");
        }

        float worstFrameGap = 0f;
        float worstVerticalGap = 0f;
        for (int glassIndex = 0; glassIndex < glassBounds.Count; glassIndex++)
        {
            Bounds pane = glassBounds[glassIndex];
            float leftGap = float.PositiveInfinity;
            float rightGap = float.PositiveInfinity;
            for (int mullionIndex = 0; mullionIndex < mullionBounds.Count; mullionIndex++)
            {
                Bounds mullion = mullionBounds[mullionIndex];
                if (mullion.center.x < pane.center.x)
                {
                    leftGap = Mathf.Min(leftGap, Mathf.Max(0f, pane.min.x - mullion.max.x));
                }
                else if (mullion.center.x > pane.center.x)
                {
                    rightGap = Mathf.Min(rightGap, Mathf.Max(0f, mullion.min.x - pane.max.x));
                }
            }

            worstFrameGap = Mathf.Max(worstFrameGap, leftGap, rightGap);
            worstVerticalGap = Mathf.Max(
                worstVerticalGap,
                Mathf.Max(0f, pane.min.y - sillRenderer.bounds.max.y),
                Mathf.Max(0f, headerRenderer.bounds.min.y - pane.max.y));
        }

        if (worstFrameGap > 0.08f || worstVerticalGap > 0.08f)
        {
            throw new InvalidOperationException(
                $"Window/frame gap is too large: horizontal={worstFrameGap:0.000} " +
                $"vertical={worstVerticalGap:0.000}.");
        }

        Debug.Log(
            $"GYMCHAOS_WINDOWS_OK panes={glassBounds.Count} colliders={glassColliderCount} " +
            $"maxFrameGap={worstFrameGap:0.000} maxVerticalGap={worstVerticalGap:0.000}");
    }

    private static EnemyFighter FindIdentity(
        EnemyFighter[] fighters, BodybuilderIdentity identity)
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

    private static string DescribeEntryCapsule(
        EnemyFighter fighter, Vector3 destination)
    {
        if (fighter == null)
        {
            return "none";
        }

        Vector3 origin = fighter.transform.position;
        Vector3 direction = Vector3.ProjectOnPlane(
            destination - origin, Vector3.up);
        if (direction.sqrMagnitude < 0.01f)
        {
            return "none";
        }

        direction.Normalize();
        Vector3 lower = origin + Vector3.up * EnemyFighter.VisitorProbeLower;
        Vector3 upper = origin + Vector3.up * EnemyFighter.VisitorProbeUpper;
        float radius = EnemyFighter.GetBodyRadiusForIdentity(fighter.Identity);
        RaycastHit[] hits = Physics.CapsuleCastAll(
            lower, upper, radius, direction, 1.35f,
            ~0, QueryTriggerInteraction.Ignore);
        string result = "none";
        for (int i = 0; i < hits.Length; i++)
        {
            Collider hit = hits[i].collider;
            if (hit == null || hit.transform == fighter.transform ||
                hit.transform.IsChildOf(fighter.transform))
            {
                continue;
            }

            string item = hit.name + "@" + hits[i].distance.ToString("F2");
            result = result == "none" ? item : result + "|" + item;
            if (result.Length > 260)
            {
                break;
            }
        }

        return result;
    }}
