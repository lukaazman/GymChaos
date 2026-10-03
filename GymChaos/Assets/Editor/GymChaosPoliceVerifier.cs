#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Runtime verifier for the Ronnie-death police intervention. It drives the
/// real dispatch path, then checks the authored policeman's health, target
/// lock, melee-to-ranged threshold, weapon attachment, one-shot animation
/// cadence, projectile collision, and player-kill celebration.
/// </summary>
[InitializeOnLoad]
public static class GymChaosPoliceVerifier
{
    private const string RequestedKey = "GymChaos.PoliceVerificationRequested";
    private static double started;
    private static bool suspended;
    private static bool killRequested;
    private static bool dispatchValidated;
    private static bool naturalPursuitValidated;
    private static bool damageRequested;
    private static bool combatLogged;
    private static bool sirenLogged;
    private static bool gunScaleLogged;
    private static bool bulletLogged;
    private static bool gunCaptureDone;
    private static bool finished;
    private static int resultCode;
    private static int projectileBaseline;
    private static int collisionBaseline;
    private static int officerShotBaseline;
    private static int weaponShotBaseline;
    private static PlayerMovement player;
    private static EnemyFighter ronnie;
    private static EnemyFighter officer;
    private static GymPoliceOfficer policeOfficer;
    private static GymPoliceWeapon weapon;
    private static PursuitStage pursuitStage;
    private static double pursuitStageStarted;
    private static Vector3 pursuitStageOfficerStart;

    private enum PursuitStage
    {
        Gym,
        Parking,
        Outdoor,
        Store,
        Complete
    }

    static GymChaosPoliceVerifier()
    {
        if (SessionState.GetBool(RequestedKey, false))
        {
            Hook();
        }
    }

    [MenuItem("Tools/GymChaos/Run Police Runtime Verification")]
    public static void Run()
    {
        SessionState.SetBool(RequestedKey, true);
        ResetState();
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        Hook();
        EditorApplication.isPlaying = true;
    }

    private static void Hook()
    {
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
        EditorApplication.playModeStateChanged -= PlayModeChanged;
        EditorApplication.playModeStateChanged += PlayModeChanged;
    }

    private static void ResetState()
    {
        started = GymChaosVerifierClock.Now;
        suspended = false;
        killRequested = false;
        dispatchValidated = false;
        naturalPursuitValidated = false;
        damageRequested = false;
        combatLogged = false;
        gunScaleLogged = false;
        bulletLogged = false;
        gunCaptureDone = false;
        sirenLogged = false;
        finished = false;
        resultCode = 1; GymChaosVerifierExit.Record(resultCode);
        player = null;
        ronnie = null;
        officer = null;
        policeOfficer = null;
        weapon = null;
        pursuitStage = PursuitStage.Gym;
        pursuitStageStarted = 0d;
        pursuitStageOfficerStart = Vector3.zero;
        projectileBaseline = 0;
        collisionBaseline = 0;
        officerShotBaseline = 0;
        weaponShotBaseline = 0;
    }

    private static void PlayModeChanged(PlayModeStateChange change)
    {
        if (change == PlayModeStateChange.EnteredPlayMode)
        {
            GymChaosVerifierClock.BeginFixedStep();
            started = GymChaosVerifierClock.Now;
            Time.timeScale = 3f;
            MuteAllAudio();
        }

        if (change != PlayModeStateChange.EnteredEditMode)
        {
            return;
        }

        GymChaosVerifierClock.EndFixedStep();
        EditorApplication.update -= Tick;
        EditorApplication.playModeStateChanged -= PlayModeChanged;
        SessionState.EraseBool(RequestedKey);
        if (Application.isBatchMode)
        {
            GymChaosVerifierExit.Exit(resultCode);
        }
    }

    private static void Tick()
    {
        if (finished || !EditorApplication.isPlaying)
        {
            return;
        }

        try
        {
            MuteAllAudio();
            double elapsed = GymChaosVerifierClock.Now - started;
            if (elapsed > 240d)
            {
                throw new InvalidOperationException(
                    "Police runtime verification timed out before the complete combat sequence.");
            }

            GymVisitorDirector director =
                UnityEngine.Object.FindAnyObjectByType<GymVisitorDirector>();
            player = UnityEngine.Object.FindAnyObjectByType<PlayerMovement>();
            if (director != null && !suspended)
            {
                director.SuspendVisitorSimulationForVerification();
                suspended = true;
            }

            if (ronnie == null)
            {
                EnemyFighter[] fighters = UnityEngine.Object.FindObjectsByType<EnemyFighter>(
                    FindObjectsInactive.Include, FindObjectsSortMode.None);
                for (int i = 0; i < fighters.Length; i++)
                {
                    if (fighters[i] != null &&
                        fighters[i].Identity == BodybuilderIdentity.Ronnie)
                    {
                        ronnie = fighters[i];
                        break;
                    }
                }
            }

            if (!killRequested && player != null && ronnie != null && !ronnie.IsDead)
            {
                ronnie.TakeMeleeHit(Vector3.zero, ronnie.MaxHealth + 1f, 0.02f);
                killRequested = true;
                Debug.Log(
                    $"GYMCHAOS_POLICE_TEST_RONNIE_KILLED killer={player.name}", ronnie);
            }

            if (!dispatchValidated)
            {
                TryValidateDispatch();
                return;
            }

            if (!naturalPursuitValidated)
            {
                ValidateNaturalPursuit();
                return;
            }

            if (!damageRequested && policeOfficer != null && player != null)
            {
                officer.TakeMeleeHit(Vector3.zero,
                    officer.MaxHealth * 0.55f, 0.02f);
                damageRequested = true;
                projectileBaseline = GymPoliceProjectile.CreatedCount;
                collisionBaseline = GymPoliceProjectile.ResolvedCollisionCount;
                officerShotBaseline = policeOfficer.GunShotCount;
                weaponShotBaseline = weapon.ShotCount;
                Debug.Log(
                    $"GYMCHAOS_POLICE_TEST_HEALTH_CROSSED health={officer.CurrentHealth:F1} " +
                    $"max={officer.MaxHealth:F1}", officer);
            }

            if (damageRequested)
            {
                ValidateCombatProgress(elapsed);
            }
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            Finish(1);
        }
    }

    private static void TryValidateDispatch()
    {
        officer = GymPoliceDirector.LastOfficer;
        if (officer == null || officer.IsDead)
        {
            return;
        }

        policeOfficer = officer.GetComponent<GymPoliceOfficer>();
        weapon = officer.GetComponent<GymPoliceWeapon>();
        if (policeOfficer == null || weapon == null)
        {
            throw new InvalidOperationException(
                "Dispatched policeman is missing GymPoliceOfficer or GymPoliceWeapon.");
        }

        GymPoliceDirector director = GymPoliceDirector.ActiveInstance;
        ExternalRiggedCharacterVisual visual = officer.GetComponent<ExternalRiggedCharacterVisual>();
        SkinnedMeshRenderer policeMesh = visual != null ? visual.RuntimeRenderer : null;
        Texture policeTexture = policeMesh != null && policeMesh.sharedMaterial != null
            ? policeMesh.sharedMaterial.GetTexture("_BaseMap") : null;
        if (policeMesh == null || !policeMesh.enabled || policeTexture == null ||
            policeTexture.name != "policeman" || policeMesh.sharedMesh.uv.Length == 0)
        {
            throw new InvalidOperationException("Policeman original textured skinned mesh is missing.");
        }
        Debug.Log($"GYMCHAOS_POLICE_TEXTURE_OK texture={policeTexture.name} " +
            $"size={policeTexture.width}x{policeTexture.height} uv={policeMesh.sharedMesh.uv.Length}");
        bool normalSpawn = director != null &&
            Vector3.Distance(
                director.PoliceCarSpawnPointForVerification,
                GymOutdoorBuilder.VehicleArrivalRoadSpawnPoint) < 0.5f;
        bool exactTripleSpeed = director != null &&
            Mathf.Abs(director.PoliceCarSpeedForVerification -
                GymVisitorVehicle.NormalDriveSpeed * 3f) < 0.01f;
        bool measuredTripleSpeed = director != null &&
            director.MaxMeasuredCarSpeedForVerification >=
                GymVisitorVehicle.NormalDriveSpeed * 2.85f;
        bool roadStop = director != null &&
            director.PoliceCarStopInParkingForVerification;
        bool visitorRoute = director != null &&
            director.PoliceCarUsesVisitorRouteForVerification &&
            director.PoliceCarMaxRouteDeviationForVerification <= 0.5f;
        bool routeBoundsClear = director != null &&
            director.PoliceCarRouteBoundsClearForVerification;
        bool stopWithinRoadEnvelope = director != null &&
            director.PoliceCarStopWithinRoadEnvelopeForVerification;
        bool routePointsDistinct = director != null &&
            director.PoliceCarRoutePointsDistinctForVerification;
        bool parkedByGym = director != null &&
            director.PoliceCarParkedByGymForVerification;
        float groundClearance = director != null
            ? director.PoliceCarGroundClearanceForVerification
            : float.NaN;
        bool grounded = !float.IsNaN(groundClearance) &&
            groundClearance >= -0.03f && groundClearance <= 0.005f;
        bool officerExit = director != null &&
            director.OfficerExitedForVerification &&
            Vector3.Distance(
                director.OfficerExitPositionForVerification,
                officer.transform.position) < 0.25f &&
            (director.DispatchPhaseForVerification == "OfficerExit" ||
                director.DispatchPhaseForVerification == "Chase");
        if (!normalSpawn || !exactTripleSpeed || !measuredTripleSpeed ||
            !roadStop || !visitorRoute || !routeBoundsClear || !stopWithinRoadEnvelope ||
            !routePointsDistinct || !parkedByGym || !grounded || !officerExit)
        {
            throw new InvalidOperationException(
                $"Police dispatch route contract failed: spawn={normalSpawn} " +
                $"visitorRoute={visitorRoute} " +
                $"speed3x={exactTripleSpeed} measuredSpeed={measuredTripleSpeed} " +
                $"roadStop={roadStop} routeBoundsClear={routeBoundsClear} " +
                $"stopWithinRoadEnvelope={stopWithinRoadEnvelope} " +
                $"routePointsDistinct={routePointsDistinct} " +
                $"parkedByGym={parkedByGym} " +
                $"routeOverlaps={director?.PoliceCarRouteOverlapCountForVerification} " +
                $"grounded={grounded} " +
                $"clearance={groundClearance:F3} officerExit={officerExit} " +
                $"phase={director?.DispatchPhaseForVerification}.");
        }
        Debug.Log(
            $"GYMCHAOS_POLICE_ROUTE_OK normalSpawn={normalSpawn} " +
            $"speed={director.PoliceCarSpeedForVerification:F2} " +
            $"normalSpeed={GymVisitorVehicle.NormalDriveSpeed:F2} " +
            $"maxMeasured={director.MaxMeasuredCarSpeedForVerification:F2} " +
            $"roadStop={roadStop} routeBoundsClear={routeBoundsClear} " +
            $"stopWithinRoadEnvelope={stopWithinRoadEnvelope} " +
            $"routePointsDistinct={routePointsDistinct} " +
            $"parkedByGym={parkedByGym} " +
            $"routeOverlaps={director.PoliceCarRouteOverlapCountForVerification} " +
            $"groundClearance={groundClearance:F3} " +
            $"officerExit={officerExit} phase={director.DispatchPhaseForVerification}",
            officer);
        Debug.Log(
            $"GYMCHAOS_POLICE_VISITOR_ROUTE_OK stop={director.PoliceCarStopPointForVerification} " +
            $"maxDeviation={director.PoliceCarMaxRouteDeviationForVerification:F2} " +
            $"parking={GymOutdoorBuilder.ParkingBounds.center} " +
            $"points={director.PoliceCarRoutePointCountForVerification}", officer);

        if (ronnie == null ||
            Mathf.Abs(officer.MaxHealth - ronnie.MaxHealth * (2f / 3f)) > 0.05f)
        {
            throw new InvalidOperationException(
                $"Policeman health ratio failed: officer={officer.MaxHealth:F2} " +
                $"ronnie={(ronnie != null ? ronnie.MaxHealth : 0f):F2}.");
        }

        if (player != null && GymPoliceDirector.LastKillerTarget != player.transform)
        {
            throw new InvalidOperationException(
                "Police dispatch did not lock to the Ronnie killer target.");
        }

        dispatchValidated = true;
        PlaceDoorwayBlocker();
        BeginPursuitStage(
            PursuitStage.Gym,
            // Well inside the main gym, not just past the doorway.
            GymDoorway.Instance.InteriorPoint + Vector3.left * 10f,
            "gym");
        Debug.Log(
            $"GYMCHAOS_POLICE_DISPATCH_OK target={player?.name} " +
            $"healthRatio={(officer.MaxHealth / Mathf.Max(0.01f, ronnie.MaxHealth)):F3} " +
            $"carAsset=Policecar.glb", officer);
    }

    private static void ValidateNaturalPursuit()
    {
        GymPoliceDirector director = GymPoliceDirector.ActiveInstance;
        GameObject parkedCar = GameObject.Find("Police Car Arrival");
        if (director == null || parkedCar == null ||
            !director.PoliceCarParkedByGymForVerification ||
            director.DispatchPhaseForVerification != "Chase")
        {
            if (GymChaosVerifierClock.Now - pursuitStageStarted > 4d)
            {
                throw new InvalidOperationException(
                    $"Police car did not remain parked during natural pursuit: " +
                    $"carPresent={parkedCar != null} " +
                    $"phase={director?.DispatchPhaseForVerification}.");
            }
            return;
        }

        float distance = Vector3.Distance(
            Vector3.ProjectOnPlane(officer.transform.position, Vector3.up),
            Vector3.ProjectOnPlane(player.transform.position, Vector3.up));
        float moved = Vector3.Distance(
            Vector3.ProjectOnPlane(officer.transform.position, Vector3.up),
            Vector3.ProjectOnPlane(pursuitStageOfficerStart, Vector3.up));
        bool reached = distance <= Mathf.Max(2.65f, officer.CurrentAttackRange + 0.55f);
        if (!reached)
        {
            if (GymChaosVerifierClock.Now - pursuitStageStarted > 40d)
            {
                throw new InvalidOperationException(
                    $"Policeman did not naturally reach the killer in " +
                    $"{pursuitStage}: distance={distance:F2} moved={moved:F2} " +
                    $"officer={officer.transform.position} player={player.transform.position} " +
                    $"blockers={DescribeOfficerBlockers()}.");
            }
            return;
        }

        if (GymDoorway.Instance != null && officer.PoliceDoorRequestHeldForVerification &&
            Vector3.ProjectOnPlane(GymDoorway.Instance.DoorCenter - officer.transform.position,
                Vector3.up).magnitude > 6f)
        {
            throw new InvalidOperationException(
                $"Policeman still holds the gym door open far from it at {pursuitStage}.");
        }
        Debug.Log(
            $"GYMCHAOS_POLICE_NATURAL_PURSUIT_STAGE_OK stage={pursuitStage} " +
            $"distance={distance:F2} moved={moved:F2} carParked=True",
            officer);

        switch (pursuitStage)
        {
            case PursuitStage.Gym:
                ReleaseDoorwayBlocker();
                BeginPursuitStage(
                    PursuitStage.Parking,
                    GymOutdoorBuilder.ParkingBounds.center,
                    "parking");
                break;
            case PursuitStage.Parking:
                // Outdoor road beside the store's west road gate. The old
                // east-side store point is no longer on a public route.
                BeginPursuitStage(
                    PursuitStage.Outdoor,
                    new Vector3(
                        GymOutdoorBuilder.ProteinStoreWestApproachPoint.x + 2f,
                        0f,
                        GymOutdoorBuilder.VehicleRoadTurnPoint.z - 2f),
                    "outdoor");
                break;
            case PursuitStage.Outdoor:
                BeginPursuitStage(PursuitStage.Store,
                    GymProteinStoreEnvironment.StoreFrontClearPoint + Vector3.right * 1.0f,
                    "store");
                break;
            case PursuitStage.Store:
                pursuitStage = PursuitStage.Complete;
                naturalPursuitValidated = true;
                Debug.Log(
                    "GYMCHAOS_POLICE_NATURAL_RESPONSE_OK " +
                    "stages=gym,parking,outdoor,store carParked=True officerTeleported=False",
                    officer);
                break;
        }
    }

    // Another character standing in the doorway must not deadlock the
    // officer at the entrance (reported in game). Freeze one fighter in the
    // door opening for the gym stage.
    private static EnemyFighter doorwayBlocker;

    private static void PlaceDoorwayBlocker()
    {
        GymDoorway doorway = GymDoorway.Instance;
        if (doorway == null) return;
        foreach (EnemyFighter candidate in UnityEngine.Object.FindObjectsByType<EnemyFighter>(FindObjectsSortMode.None))
        {
            if (candidate == null || candidate.IsDead || candidate == officer ||
                candidate.IsPolice || candidate.Identity == BodybuilderIdentity.Mark ||
                candidate.Identity == BodybuilderIdentity.Manwithsuit1)
            {
                continue;
            }
            Vector3 spot = (doorway.InteriorPoint + doorway.ExteriorPoint) * 0.5f;
            Rigidbody body = candidate.GetComponent<Rigidbody>();
            if (body != null)
            {
                body.position = spot;
                body.linearVelocity = Vector3.zero;
            }
            candidate.transform.position = spot;
            candidate.SetDialogueLocked(true);
            doorwayBlocker = candidate;
            Debug.Log($"GYMCHAOS_POLICE_DOORWAY_BLOCKER_PLACED enemy={candidate.Identity} position={spot}");
            return;
        }
    }

    private static void ReleaseDoorwayBlocker()
    {
        if (doorwayBlocker != null)
        {
            doorwayBlocker.SetDialogueLocked(false);
            doorwayBlocker = null;
        }
    }

    private static void BeginPursuitStage(
        PursuitStage stage, Vector3 destination, string label)
    {
        pursuitStage = stage;
        pursuitStageStarted = GymChaosVerifierClock.Now;
        pursuitStageOfficerStart = officer != null
            ? officer.transform.position
            : Vector3.zero;
        MovePlayerTo(destination);
        Debug.Log(
            $"GYMCHAOS_POLICE_NATURAL_PURSUIT_STAGE_BEGIN stage={label} " +
            $"player={player.transform.position} officer={officer?.transform.position}",
            officer);
    }

    // Graphics runs only: saves Logs/verify/police-gun.png framing the officer.
    private static void CaptureOfficer()
    {
        Transform subject = officer.transform;
        Vector3 focus = weapon.HandPositionForVerification != Vector3.zero
            ? weapon.HandPositionForVerification
            : subject.position + Vector3.up * 1.6f;
        Vector3 eye = focus + subject.forward * 0.9f + subject.right * 0.55f + Vector3.up * 0.15f;
        string path = Capture(focus, eye, 50f, "police-gun.png");
        Debug.Log($"GYMCHAOS_POLICE_GUN_CAPTURE path={path} " +
            $"weapon={weapon.WeaponPositionForVerification} hand={weapon.HandPositionForVerification} " +
            $"length={weapon.WorldLengthForVerification:F3}");
    }

    // Graphics runs only: close side view of a bullet in flight.
    private static void CaptureBullet()
    {
        GymPoliceProjectile bullet = UnityEngine.Object.FindAnyObjectByType<GymPoliceProjectile>();
        if (bullet == null) return;
        Transform subject = bullet.transform;
        Vector3 side = Vector3.Cross(subject.forward, Vector3.up).normalized;
        if (side.sqrMagnitude < 0.01f) side = Vector3.right;
        Vector3 eye = subject.position + side * 0.16f + Vector3.up * 0.04f;
        string path = Capture(subject.position, eye, 30f, "police-bullet.png");
        Debug.Log($"GYMCHAOS_POLICE_BULLET_CAPTURE path={path} position={subject.position}");
    }

    private static string Capture(Vector3 focus, Vector3 eye, float fieldOfView, string fileName)
    {
        GameObject host = new GameObject("Police Capture Camera");
        Camera camera = host.AddComponent<Camera>();
        camera.fieldOfView = fieldOfView;
        camera.nearClipPlane = 0.02f;
        camera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(focus - eye));
        RenderTexture target = new RenderTexture(960, 720, 24);
        camera.targetTexture = target;
        camera.Render();
        RenderTexture previous = RenderTexture.active;
        RenderTexture.active = target;
        Texture2D image = new Texture2D(960, 720, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0, 0, 960, 720), 0, 0);
        image.Apply();
        RenderTexture.active = previous;
        camera.targetTexture = null;
        string path = System.IO.Path.Combine(
            System.IO.Directory.GetParent(Application.dataPath).Parent.FullName,
            "Logs", "verify", fileName);
        System.IO.File.WriteAllBytes(path, image.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(image);
        target.Release();
        UnityEngine.Object.DestroyImmediate(target);
        UnityEngine.Object.DestroyImmediate(host);
        return path;
    }

    private static void MovePlayerTo(Vector3 destination)
    {
        if (player == null)
        {
            return;
        }

        destination.y = GymDoorway.Instance != null
            ? GymDoorway.Instance.ExteriorPoint.y + 0.05f
            : player.transform.position.y;
        Rigidbody body = player.GetComponent<Rigidbody>();
        if (body != null)
        {
            body.position = destination;
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }
        player.transform.position = destination;
        Physics.SyncTransforms();
    }

    private static string DescribeOfficerBlockers()
    {
        if (officer == null || player == null)
        {
            return "unavailable";
        }
        Vector3 origin = officer.transform.position;
        Vector3 direction = Vector3.ProjectOnPlane(
            player.transform.position - origin, Vector3.up).normalized;
        RaycastHit[] hits = Physics.CapsuleCastAll(
            origin + Vector3.up * EnemyFighter.VisitorProbeLower,
            origin + Vector3.up * EnemyFighter.VisitorProbeUpper,
            EnemyFighter.GetBodyRadiusForIdentity(BodybuilderIdentity.Policeman),
            direction,
            2.5f,
            Physics.AllLayers,
            QueryTriggerInteraction.Ignore);
        string description = string.Empty;
        for (int i = 0; i < hits.Length && i < 8; i++)
        {
            Collider hit = hits[i].collider;
            if (hit == null || hit.transform.IsChildOf(officer.transform))
            {
                continue;
            }
            description += $"[{hit.name} root={hit.transform.root.name} " +
                $"center={hit.bounds.center} size={hit.bounds.size}]";
        }
        return string.IsNullOrEmpty(description) ? "none" : description;
    }

    private static void ValidateCombatProgress(double elapsed)
    {
        if (policeOfficer.HasObservedHandToHand &&
            policeOfficer.HasObservedRangedMode &&
            policeOfficer.HandAttackRange > 0.01f &&
            officer.CurrentAttackRange >= policeOfficer.HandAttackRange * 2.95f)
        {
            if (!combatLogged)
            {
                Debug.Log(
                    $"GYMCHAOS_POLICE_COMBAT_MODE_OK handRange={policeOfficer.HandAttackRange:F2} " +
                    $"rangedRange={officer.CurrentAttackRange:F2} " +
                    $"handObserved={policeOfficer.HasObservedHandToHand}", officer);
            }
        }

        if (weapon.IsReady && !weapon.IsAttachedToRightHand)
        {
            throw new InvalidOperationException(
                "Glock17 loaded but is not parented to the policeman right hand.");
        }
        if (weapon.IsReady && !gunScaleLogged)
        {
            float gunLength = weapon.WorldLengthForVerification;
            if (gunLength < 0.15f || gunLength > 0.40f)
            {
                throw new InvalidOperationException(
                    $"Glock17 world length is not hand-sized: length={gunLength:F3} m.");
            }
            float expectedLength = GymPoliceWeapon.GlockBaseWorldLength *
                GymPoliceWeapon.GlockScaleMultiplier;
            if (Mathf.Abs(gunLength - expectedLength) > 0.01f)
            {
                throw new InvalidOperationException(
                    $"GYMCHAOS_POLICE_GUN_SCALE_FAIL length={gunLength:F3} " +
                    $"expected={expectedLength:F3} (1.15x of 0.235 m).");
            }
            Debug.Log(
                $"GYMCHAOS_POLICE_GUN_SCALE_OK length={gunLength:F3} " +
                $"target={GymPoliceWeapon.GlockWorldLength:F3} multiplier={GymPoliceWeapon.GlockScaleMultiplier:F2}", weapon);
            gunScaleLogged = true;
        }
        if (gunScaleLogged && !gunCaptureDone && weapon.IsVisible && weapon.ShotCount >= 2 &&
            SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null)
        {
            gunCaptureDone = true;
            CaptureOfficer();
            Vector3 barrel = Vector3.ProjectOnPlane(
                weapon.HeldBarrelDirectionForVerification, Vector3.up).normalized;
            Vector3 facing = Vector3.ProjectOnPlane(
                officer.transform.forward, Vector3.up).normalized;
            float barrelDot = Vector3.Dot(barrel, facing);
            if (barrelDot < 0.3f)
            {
                throw new InvalidOperationException(
                    $"Glock17 barrel does not point forward while shooting: dot={barrelDot:F2}.");
            }
            float bodyDot = Vector3.Dot(Vector3.ProjectOnPlane(
                weapon.BodyDirectionForVerification, Vector3.up).normalized, facing);
            if (bodyDot < 0.3f)
            {
                throw new InvalidOperationException(
                    $"Glock17 body points back up the forearm: bodyDot={bodyDot:F2}.");
            }
            float upDot = Vector3.Dot(weapon.WeaponUpForVerification.normalized, Vector3.up);
            float palmDistance = Vector3.Distance(
                weapon.PalmPointForVerification, weapon.WeaponCenterForVerification);
            if (barrelDot < 0.9f || upDot < 0.7f || palmDistance > 0.2f)
            {
                throw new InvalidOperationException(
                    $"Glock17 not held upright in the palm: barrelDot={barrelDot:F2} " +
                    $"upDot={upDot:F2} palmDistance={palmDistance:F3}.");
            }
            Debug.Log($"GYMCHAOS_POLICE_GUN_AIM_OK barrelDot={barrelDot:F2} bodyDot={bodyDot:F2} " +
                $"upDot={upDot:F2} palmDistance={palmDistance:F3}", weapon);
        }

        if (!bulletLogged && GymPoliceProjectile.GlbVisualCount > 0 && weapon.IsReady)
        {
            // bullet.glb replaces the old sphere: real cartridge proportions at
            // the Glock's scale, tip first along the flight, base at the muzzle,
            // and the muzzle at the front of the barrel rather than the gun centre.
            float length = GymPoliceProjectile.LastVisualWorldLength;
            float tipDot = GymPoliceProjectile.LastVisualTipAlignment;
            float spawnGap = GymPoliceProjectile.LastSpawnMuzzleDistance;
            Vector3 muzzleLocal = weapon.MuzzleLocalForVerification;
            float muzzleAhead = Vector3.Dot(
                weapon.Muzzle.position - weapon.WeaponCenterForVerification,
                weapon.HeldBarrelDirectionForVerification);
            if (Mathf.Abs(length - GymPoliceProjectile.BulletWorldLength) >
                    GymPoliceProjectile.BulletWorldLength * 0.1f ||
                tipDot < 0.99f || spawnGap < 0f || spawnGap > 0.005f ||
                muzzleAhead < GymPoliceWeapon.GlockWorldLength * 0.35f)
            {
                throw new InvalidOperationException(
                    $"GYMCHAOS_POLICE_BULLET_FAIL length={length:F4} " +
                    $"expected={GymPoliceProjectile.BulletWorldLength:F4} tipDot={tipDot:F3} " +
                    $"spawnGap={spawnGap:F4} muzzleAhead={muzzleAhead:F3} muzzleLocal={muzzleLocal}");
            }
            if (SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null)
            {
                CaptureBullet();
            }
            Debug.Log(
                $"GYMCHAOS_POLICE_BULLET_OK length={length:F4} tipDot={tipDot:F3} " +
                $"spawnGap={spawnGap:F4} muzzleAhead={muzzleAhead:F3}", weapon);
            bulletLogged = true;
        }

        int newShots = policeOfficer.GunShotCount - officerShotBaseline;
        int newProjectiles = GymPoliceProjectile.CreatedCount - projectileBaseline;
        int weaponShots = weapon.ShotCount - weaponShotBaseline;
        bool hasProjectile = newProjectiles >= 2 &&
            newProjectiles == newShots && newShots == weaponShots;
        bool hasCollision =
            GymPoliceProjectile.ResolvedCollisionCount > collisionBaseline;
        bool hasShots = policeOfficer.GunShotCount >= 2 &&
            weapon.ShotCount == policeOfficer.GunShotCount;
        bool playerCelebration = policeOfficer.HasPlayerKillCelebration;
        bool animationPresentation =
            policeOfficer.HasObservedPreDrawWeaponHidden &&
            policeOfficer.HasObservedGunDrawingClip &&
            policeOfficer.HasObservedGunShootingClip &&
            policeOfficer.HasObservedShotAnimation;

        if (policeOfficer.GunDrawingCount > 1)
        {
            throw new InvalidOperationException(
                $"Gun drawing played more than once: {policeOfficer.GunDrawingCount}.");
        }

        GymPoliceDirector director = GymPoliceDirector.ActiveInstance;
        bool sirenReady = director != null &&
            director.PoliceSirenStartedForVerification &&
            director.PoliceSirenStoppedForVerification &&
            director.PoliceSirenLoopForVerification;
        if (hasShots && hasProjectile && hasCollision && gunScaleLogged && bulletLogged &&
            animationPresentation && playerCelebration && sirenReady)
        {
            if (!sirenLogged)
            {
                Debug.Log(
                    $"GYMCHAOS_POLICE_SIREN_OK " +
                    $"asset={director.PoliceSirenAssetForVerification} " +
                    $"loop={director.PoliceSirenLoopForVerification} " +
                    $"started={director.PoliceSirenStartedForVerification} " +
                    $"stopped={director.PoliceSirenStoppedForVerification}",
                    director);
                sirenLogged = true;
            }
            Debug.Log(
                $"GYMCHAOS_POLICE_COMBAT_OK drawing={policeOfficer.GunDrawingCount} " +
                $"shots={policeOfficer.GunShotCount} celebration={playerCelebration} " +
                $"preDrawHidden={policeOfficer.HasObservedPreDrawWeaponHidden} " +
                $"drawClip={policeOfficer.HasObservedGunDrawingClip} " +
                $"shootClip={policeOfficer.HasObservedGunShootingClip} " +
                $"shotAnimation={policeOfficer.HasObservedShotAnimation}", officer);
            Debug.Log(
                $"GYMCHAOS_POLICE_PROJECTILE_OK created={GymPoliceProjectile.CreatedCount} " +
                $"collisions={GymPoliceProjectile.ResolvedCollisionCount} " +
                $"newShots={newShots} newProjectiles={newProjectiles} " +
                $"last={GymPoliceProjectile.LastCollisionKind} " +
                $"glockAttached={weapon.IsAttachedToRightHand}", weapon);
            combatLogged = true;
            Finish(0);
            return;
        }

        if (elapsed > 230d)
        {
            throw new InvalidOperationException(
                $"Police combat incomplete: hand={policeOfficer.HasObservedHandToHand} " +
                $"ranged={policeOfficer.HasObservedRangedMode} " +
                $"drawing={policeOfficer.GunDrawingCount} shots={policeOfficer.GunShotCount} " +
                $"newShots={newShots} newProjectiles={newProjectiles} " +
                $"weaponShots={weaponShots} collisions={hasCollision} " +
                $"animationPresentation={animationPresentation} " +
                $"preDrawHidden={policeOfficer.HasObservedPreDrawWeaponHidden} " +
                $"drawClip={policeOfficer.HasObservedGunDrawingClip} " +
                $"shootClip={policeOfficer.HasObservedGunShootingClip} " +
                $"shotAnimation={policeOfficer.HasObservedShotAnimation} " +
                $"celebration={playerCelebration} sirenReady={sirenReady} " +
                $"officer={officer.transform.position} player={player?.transform.position} " +
                $"lastShotOrigin={weapon.LastShotOrigin} lastShotDirection={weapon.LastShotDirection} " +
                $"lastHit={GymPoliceProjectile.LastCollisionKind}.");
        }
    }

    private static void Finish(int code)
    {
        if (finished)
        {
            return;
        }
        finished = true;
        resultCode = code; GymChaosVerifierExit.Record(resultCode);
        SessionState.EraseBool(RequestedKey);
        EditorApplication.update -= Tick;
        if (EditorApplication.isPlaying)
        {
            EditorApplication.isPlaying = false;
        }
        else if (Application.isBatchMode)
        {
            GymChaosVerifierExit.Exit(resultCode);
        }
    }

    private static void MuteAllAudio()
    {
        AudioSource[] sources = UnityEngine.Object.FindObjectsByType<AudioSource>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < sources.Length; i++)
        {
            if (sources[i] != null)
            {
                sources[i].mute = true;
            }
        }
    }
}
#endif
