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

    private static double enteredPlayTime;
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
    private static bool workoutPoseValidated;
    private static double workoutCompletionTime;
    private static Vector3 workoutCompletionPosition;
    private static bool workoutReleaseMoveValidated;
    private static bool groundingVerificationLogged;
    private static EnemyFighter groundingViolationFighter;
    private static int groundingViolationFrames;

    static GymChaosVisitorVerifier()
    {
        if (!EditorPrefs.GetBool(VerificationRequestedKey, false))
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
        workoutCompletionTime = 0d;
        workoutCompletionPosition = Vector3.zero;
        workoutReleaseMoveValidated = false;
        groundingVerificationLogged = false;
        groundingViolationFighter = null;
        groundingViolationFrames = 0;
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
            Time.timeScale = 2f;
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
        }
        else if (state == PlayModeStateChange.EnteredEditMode)
        {
            Time.timeScale = 1f;
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
            }

            if (sceneValidated && entryConfirmed && !workoutRequested && elapsed > 4d &&
                director.BeginWorkoutForVerification(
                    out workoutFighter, out workoutStation))
            {
                workoutRequested = true;
                workoutVersionBefore = workoutFighter
                    .GetComponent<GymVisitorAgent>().CompletedWorkoutVersion;
                workoutStartPosition = workoutFighter.transform.position;
                workoutNeedsMovement = Vector3.ProjectOnPlane(
                    workoutStation.EnemyPosition - workoutStartPosition, Vector3.up).magnitude > 0.5f;
                Debug.Log(
                    $"GYMCHAOS_VISITOR_WORKOUT_TEST_STARTED enemy={workoutFighter.Identity} " +
                    $"station={workoutStation.EquipmentName}");
            }

            if (workoutRequested && workoutFighter != null)
            {
                GymVisitorAgent agent = workoutFighter.GetComponent<GymVisitorAgent>();
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

                    if (!workoutPoseValidated && squat != null && squat.CurrentMotion > 0.82f)
                    {
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

                        if (!squat.HasValidSquatRig || squat.FootPlantError > 0.14f ||
                            squat.FootSoleError > 0.045f ||
                            squat.FootGroundError > 0.035f ||
                            squat.FootRotationError > 0.5f ||
                            !squat.HasValidArmRig || squat.CurrentHipDrop < 0.34f ||
                            squat.CurrentKneeBend < 45f ||
                            squat.KneeBendDifference > 8f ||
                            squat.LegDepthDifference > 0.16f ||
                            squat.GripError > 0.28f || !squat.HasOverhandGrip ||
                            squat.HandSpreadRatio < 1.25f ||
                            squat.ForearmOutwardError > 0.35f ||
                            squat.ArmCrossingError > 0.08f ||
                            squat.ElbowOutwardError > 0.04f ||
                            squat.UpperArmReferenceError > 0.08f ||
                            squat.ForearmReferenceError > 0.08f ||
                            squat.ArmShapeError > 1.05f ||
                            squat.HandContactError > 0.16f ||
                            squat.BarBodyFollowError > 0.10f ||
                            squat.BarDropFromStart < 0.05f ||
                            squatFacingDot < 0.98f)
                        {
                            throw new InvalidOperationException(
                                $"Squat rig validation failed: valid={squat.HasValidSquatRig} " +
                                $"arms={squat.HasValidArmRig} " +
                                $"footError={squat.FootPlantError:0.000} " +
                                $"soleError={squat.FootSoleError:0.000} " +
                                $"groundError={squat.FootGroundError:0.000} " +
                                $"footRotation={squat.FootRotationError:0.0} " +
                                $"hipDrop={squat.CurrentHipDrop:0.000} " +
                                $"kneeBend={squat.CurrentKneeBend:0.0} " +
                                $"kneeDelta={squat.KneeBendDifference:0.0} " +
                                $"legDepthDelta={squat.LegDepthDifference:0.000} " +
                                $"gripError={squat.GripError:0.000} " +
                                $"overhand={squat.HasOverhandGrip} " +
                                $"handSpreadRatio={squat.HandSpreadRatio:0.000} " +
                                $"forearmOutward={squat.ForearmOutwardError:0.000} " +
                                $"armCrossing={squat.ArmCrossingError:0.000} " +
                                $"elbowOutward={squat.ElbowOutwardError:0.000} " +
                                $"upperArmRef={squat.UpperArmReferenceError:0.000} " +
                                $"forearmRef={squat.ForearmReferenceError:0.000} " +
                                $"armShape={squat.ArmShapeError:0.000} " +
                                $"handContact={squat.HandContactError:0.000} " +
                                $"barFollow={squat.BarBodyFollowError:0.000} " +
                                $"barDrop={squat.BarDropFromStart:0.000} " +
                                $"facingDot={squatFacingDot:0.000}.");
                        }

                        workoutPoseValidated = true;
                        Debug.Log(
                            $"GYMCHAOS_SQUAT_POSE_OK enemy={workoutFighter.Identity} " +
                            $"footError={squat.FootPlantError:0.000} " +
                            $"soleError={squat.FootSoleError:0.000} " +
                            $"groundError={squat.FootGroundError:0.000} " +
                            $"footRotation={squat.FootRotationError:0.0} " +
                            $"hipDrop={squat.CurrentHipDrop:0.000} " +
                            $"kneeBend={squat.CurrentKneeBend:0.0} " +
                            $"kneeDelta={squat.KneeBendDifference:0.0} " +
                            $"legDepthDelta={squat.LegDepthDifference:0.000} " +
                            $"gripError={squat.GripError:0.000} " +
                            $"overhand={squat.HasOverhandGrip} " +
                            $"handSpread={squat.HandSpread:0.000} " +
                            $"handSpreadRatio={squat.HandSpreadRatio:0.000} " +
                            $"forearmOutward={squat.ForearmOutwardError:0.000} " +
                            $"armCrossing={squat.ArmCrossingError:0.000} " +
                            $"elbowOutward={squat.ElbowOutwardError:0.000} " +
                            $"upperArmRef={squat.UpperArmReferenceError:0.000} " +
                            $"forearmRef={squat.ForearmReferenceError:0.000} " +
                            $"armShape={squat.ArmShapeError:0.000} " +
                            $"handContact={squat.HandContactError:0.000} " +
                            $"barFollow={squat.BarBodyFollowError:0.000} " +
                            $"barDrop={squat.BarDropFromStart:0.000} " +
                            $"facingDot={squatFacingDot:0.000} " +
                            $"barOnTraps={workoutBarAttached} " +
                            $"centerDistance={stationDistance:0.000}");
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
                    !workoutStation.IsOccupied &&
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

                Debug.Log(
                    $"GYMCHAOS_VISITOR_VERIFICATION_OK entryMoved={entryMoved} " +
                    $"entryConfirmed={entryConfirmed} workoutMoved={workoutMoved} " +
                    $"workoutStarted={workoutStarted} workoutCompleted={workoutCompleted}");
                EditorApplication.isPlaying = false;
                return;
            }

            if (elapsed > 50d)
            {
                throw new InvalidOperationException(
                    $"Visitor runtime smoke did not finish: entryRequested={entryRequested} " +
                    $"entryMoved={entryMoved} entryConfirmed={entryConfirmed} " +
                    $"workoutRequested={workoutRequested} workoutMoved={workoutMoved} " +
                    $"workoutStarted={workoutStarted} workoutCompleted={workoutCompleted}.");
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

        if (director.EligibleEnemyCount != 5 || director.ActiveVisitorCount < 2 ||
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
                fighter.Identity == BodybuilderIdentity.Manwithsuit1)
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
            BodybuilderIdentity.Goku
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
}
