using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Dispatches one authored policeman and one authored police car after Ronnie
/// dies. The car uses the generated road/parking waypoints and waits for a
/// collision-free segment before moving, then remains parked while the officer
/// pursues Ronnie's killer.
/// </summary>
public sealed class GymPoliceDirector : MonoBehaviour
{
    private const string PoliceCarAsset = "BodyBuilders/vehicles/Policecar_lod.glb";
    private const float PoliceCarTargetLength = 4.0f * GymOutdoorBuilder.VehicleScale;
    private const float PoliceCarSpeed =
        GymVisitorVehicle.NormalDriveSpeed * 3f;
    private const float PoliceCarProbeRadius = 1.05f;
    private const float PoliceCarArrivalTimeout = 35f;
    private const float PoliceCarBlockedSnapSeconds = 8f;
    private const float PoliceCarGroundClearance = -0.01f;
    private const float OfficerHealthFraction = 2f / 3f;
    private const string PoliceSirenAsset =
        "BodyBuilders/sound/sfx/police_siren.mp3";

    private static GymPoliceDirector instance;
    private readonly RaycastHit[] routeHits = new RaycastHit[32];
    private readonly Collider[] carOverlapHits = new Collider[64];
    private enum DispatchPhase
    {
        Idle,
        Dispatched,
        Driving,
        Parked,
        OfficerExit,
        Chase
    }

    private bool dispatchActive;

    private GameObject policeCar;
    private bool policeCarReady;
    private bool policeCarFailed;
    private float floorY;
    private Vector3[] currentRoute;
    private string lastLoggedCarBlocker = string.Empty;
    private AudioSource policeSiren;
    private AudioSource policeEngine;
    private AudioClip policeSirenClip;
    private Coroutine policeSirenLoadRoutine;
    private bool policeSirenLoadStarted;
    private bool policeSirenLoadFinished;
    private bool policeSirenLoadFailed;
    private bool policeCarMoving;
    private bool policeSirenStartedObserved;
    private bool policeSirenStoppedObserved;
    private bool policeSirenLoopConfigured;
    private DispatchPhase dispatchPhase;
    private Vector3 policeCarSpawnPoint;
    private Vector3 policeCarStopPoint;
    private Vector3 officerExitPosition;
    private float policeCarSupportY;
    private float maxMeasuredCarSpeed;
    private float lastCarSampleTime;
    private Vector3 lastCarSamplePosition;
    private bool officerExited;
    private BoxCollider policeCarCollider;
    private bool policeCarRouteBoundsClear;
    private bool policeCarStopWithinRoadEnvelope;
    private int policeCarRouteOverlapCount;

    public static bool IsDispatchActive => instance != null && instance.dispatchActive;
    public static GymPoliceDirector ActiveInstance => instance;
    public static EnemyFighter LastOfficer { get; private set; }
    public static Transform LastKillerTarget { get; private set; }
    public static float LastOfficerHealth { get; private set; }

    public bool PoliceSirenStartedForVerification =>
        policeSirenStartedObserved;
    public bool PoliceSirenStoppedForVerification =>
        policeSirenStoppedObserved;
    public bool PoliceSirenLoopForVerification =>
        policeSirenLoopConfigured;
    public string PoliceSirenAssetForVerification => PoliceSirenAsset;
    public string DispatchPhaseForVerification => dispatchPhase.ToString();
    public Vector3 PoliceCarSpawnPointForVerification => policeCarSpawnPoint;
    public Vector3 PoliceCarStopPointForVerification => policeCarStopPoint;
    public float PoliceCarSpeedForVerification => PoliceCarSpeed;
    public float PoliceCarSupportYForVerification => policeCarSupportY;
    public Vector3 OfficerExitPositionForVerification => officerExitPosition;
    public bool OfficerExitedForVerification => officerExited;
    public float MaxMeasuredCarSpeedForVerification => maxMeasuredCarSpeed;
    public bool PoliceCarRouteBoundsClearForVerification =>
        policeCarRouteBoundsClear && policeCarRouteOverlapCount == 0;
    public bool PoliceCarStopWithinRoadEnvelopeForVerification =>
        policeCarStopWithinRoadEnvelope;
    public int PoliceCarRouteOverlapCountForVerification =>
        policeCarRouteOverlapCount;
    public bool PoliceCarStopInParkingForVerification =>
        policeCarStopWithinRoadEnvelope && IsInsideParking(policeCarStopPoint);
    public int PoliceCarRoutePointCountForVerification =>
        currentRoute != null ? currentRoute.Length : 0;
    public bool PoliceCarUsesVisitorRouteForVerification
    {
        get
        {
            if (currentRoute == null || currentRoute.Length != 5)
            {
                return false;
            }
            Vector3[] visitorLegs =
            {
                GymOutdoorBuilder.VehicleArrivalRoadSpawnPoint,
                GymOutdoorBuilder.VehicleArrivalRoadTurnPoint,
                GymOutdoorBuilder.VehicleArrivalRoadJunctionPoint
            };
            for (int i = 0; i < visitorLegs.Length; i++)
            {
                if (Vector3.Distance(
                        Vector3.ProjectOnPlane(currentRoute[i], Vector3.up),
                        Vector3.ProjectOnPlane(visitorLegs[i], Vector3.up)) > 0.1f)
                {
                    return false;
                }
            }
            // Aisle and stall stay inside the parking lot; no store bypass.
            return IsInsideParking(currentRoute[3]) && IsInsideParking(currentRoute[4]) &&
                Mathf.Abs(currentRoute[3].x - currentRoute[4].x) < 0.01f;
        }
    }
    public bool PoliceCarPresentForVerification => policeCar != null;
    public bool PoliceCarParkedByGymForVerification =>
        policeCar != null &&
        !policeCarMoving &&
        (dispatchPhase == DispatchPhase.Parked ||
            dispatchPhase == DispatchPhase.OfficerExit ||
            dispatchPhase == DispatchPhase.Chase) &&
        Vector3.Distance(
            Vector3.ProjectOnPlane(policeCar.transform.position, Vector3.up),
            Vector3.ProjectOnPlane(policeCarStopPoint, Vector3.up)) <= 0.25f &&
        IsInsideParking(policeCarStopPoint);
    public bool PoliceCarRoutePointsDistinctForVerification
    {
        get
        {
            if (currentRoute == null || currentRoute.Length < 2)
            {
                return false;
            }
            for (int i = 1; i < currentRoute.Length; i++)
            {
                if (Vector3.Distance(currentRoute[i - 1], currentRoute[i]) < 0.5f)
                {
                    return false;
                }
            }
            return true;
        }
    }
    public float PoliceCarGroundClearanceForVerification
    {
        get
        {
            if (policeCar == null)
            {
                return float.NaN;
            }
            Renderer[] renderers = policeCar.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                return float.NaN;
            }
            Bounds bounds = renderers[0].bounds;
            for (int index = 1; index < renderers.Length; index++)
            {
                bounds.Encapsulate(renderers[index].bounds);
            }
            return bounds.min.y - floorY;
        }
    }

    public static void NotifyRonnieKilled(EnemyFighter ronnie, Transform killer)
    {
        if (instance == null)
        {
            GameObject host = new GameObject("Gym Police Director");
            instance = host.AddComponent<GymPoliceDirector>();
        }
        if (instance.dispatchActive)
        {
            return;
        }
        instance.ResetPoliceSirenState();
        instance.dispatchActive = true;
        instance.dispatchPhase = DispatchPhase.Dispatched;
        LastKillerTarget = killer;
        LastOfficer = null;
        LastOfficerHealth = 0f;
        instance.StartCoroutine(instance.Dispatch(ronnie, killer));
    }

    private IEnumerator Dispatch(EnemyFighter ronnie, Transform killer)
    {
        float deadline = Time.time + PoliceCarArrivalTimeout;
        while ((!GymOutdoorBuilder.IsBuilt || GymDoorway.Instance == null) &&
            Time.time < deadline)
        {
            yield return null;
        }

        PlayerMovement player = FindFirstObjectByType<PlayerMovement>();
        Transform target = ResolveKiller(killer, player);
        if (target == null)
        {
            Debug.LogWarning("GYMCHAOS_POLICE_DISPATCH_WITHOUT_KILLER", this);
            dispatchActive = false;
            yield break;
        }

        if (GymDoorway.Instance == null || !GymOutdoorBuilder.IsBuilt)
        {
            Debug.LogError("GYMCHAOS_POLICE_DISPATCH_ROUTE_UNAVAILABLE", this);
            dispatchActive = false;
            yield break;
        }

        floorY = GymDoorway.Instance.ExteriorPoint.y;
        currentRoute = BuildArrivalRoute(floorY);
        Vector3 spawn = currentRoute[0];
        policeCarSpawnPoint = spawn;
        policeCarStopPoint = currentRoute[currentRoute.Length - 1];
        officerExited = false;
        policeCarRouteBoundsClear = true;
        policeCarStopWithinRoadEnvelope = false;
        policeCarRouteOverlapCount = 0;
        maxRouteDeviation = 0f;
        maxMeasuredCarSpeed = 0f;
        lastCarSampleTime = 0f;
        Vector3 direction = Vector3.ProjectOnPlane(currentRoute[1] - spawn, Vector3.up);
        Quaternion rotation = direction.sqrMagnitude > 0.01f
            ? Quaternion.LookRotation(direction.normalized, Vector3.up)
            : Quaternion.identity;

        Debug.Log(
            $"GYMCHAOS_POLICE_DISPATCHED target={target.name} " +
            $"health={(ronnie != null ? ronnie.MaxHealth * OfficerHealthFraction : 66.7f):F1}",
            this);

        policeCar = RuntimeGlbSceneLoader.Request(
            PoliceCarAsset,
            transform,
            spawn,
            rotation,
            Vector3.one,
            "Police Car Arrival",
            0,
            settleOnSupport: false,
            supportY: floorY,
            onLoaded: HandlePoliceCarLoaded);

        float loadDeadline = Time.time + 15f;
        while (!policeCarReady && !policeCarFailed && Time.time < loadDeadline)
        {
            yield return null;
        }
        if (!policeCarReady)
        {
            Debug.LogError(
                $"GYMCHAOS_POLICE_CAR_LOAD_FAILED path={PoliceCarAsset}", this);
            CleanupDispatch();
            yield break;
        }

        dispatchPhase = DispatchPhase.Driving;
        yield return WaitForPoliceSiren(8f);
        yield return MoveCarAlongRoute(currentRoute, true);
        if (policeCar == null)
        {
            CleanupDispatch();
            yield break;
        }
        dispatchPhase = DispatchPhase.Parked;
        policeCarStopWithinRoadEnvelope =
            VerifyPoliceCarStopWithinRoadEnvelope();
        Debug.Log("GYMCHAOS_POLICE_CAR_ARRIVED", this);

        EnemyFighter officer = SpawnOfficer(ronnie, target, player);
        if (officer == null)
        {
            Debug.LogError("GYMCHAOS_POLICEMAN_SPAWN_FAILED", this);
            CleanupDispatch();
            yield break;
        }

        LastOfficer = officer;
        LastOfficerHealth = officer.MaxHealth;
        dispatchPhase = DispatchPhase.OfficerExit;
        Debug.Log(
            $"GYMCHAOS_POLICEMAN_READY target={target.name} " +
            $"health={officer.MaxHealth:F1} ratio={OfficerHealthFraction:F3}", officer);
        yield return new WaitForSeconds(0.45f);
        dispatchPhase = DispatchPhase.Chase;
        Debug.Log(
            $"GYMCHAOS_POLICE_CAR_PARKED_RETAINED " +
            $"position={policeCar?.transform.position} target={policeCarStopPoint}",
            this);
    }

    // Jolly Dog revived the officer: he walks back to the parked car, gets in,
    // and the car leaves along its arrival route without the siren.
    private bool calmDeparture;
    private Transform officerReturnPoint;
    public bool IsCalmDepartureActiveForVerification => calmDeparture;

    public static bool BeginOfficerReturn(EnemyFighter officer)
    {
        if (instance == null || officer == null || !officer.IsPolice ||
            officer.Identity != BodybuilderIdentity.Policeman)
        {
            return false;
        }
        return instance.StartOfficerReturn(officer);
    }

    private bool StartOfficerReturn(EnemyFighter officer)
    {
        if (policeCar == null || calmDeparture)
        {
            // No car left to return to: the officer simply leaves the scene.
            Debug.Log("GYMCHAOS_POLICE_RETURN_WITHOUT_CAR", officer);
            Destroy(officer.gameObject);
            if (policeCar == null)
            {
                dispatchActive = false;
            }
            return true;
        }

        if (officerReturnPoint == null)
        {
            officerReturnPoint = new GameObject("Police Officer Return Point").transform;
            officerReturnPoint.SetParent(transform, false);
        }
        Vector3 point = officerExitPosition;
        if (point == Vector3.zero)
        {
            Vector3 carForward = Vector3.ProjectOnPlane(
                policeCar.transform.forward, Vector3.up).normalized;
            point = policeCar.transform.position -
                carForward * (PoliceCarTargetLength * 0.5f + 1.1f);
        }
        point.y = floorY;
        officerReturnPoint.position = point;
        officer.BeginPoliceReturnToCar(officerReturnPoint, HandleOfficerReachedCar);
        Debug.Log($"GYMCHAOS_POLICE_OFFICER_RETURNING point={point}", officer);
        StartCoroutine(OfficerReturnWatchdog(officer));
        return true;
    }

    private IEnumerator OfficerReturnWatchdog(EnemyFighter officer)
    {
        yield return new WaitForSeconds(90f);
        if (officer != null && officer.IsReturningToPoliceCar && !calmDeparture)
        {
            // Blocked on the way: he gets in anyway so the car can leave.
            Debug.LogWarning("GYMCHAOS_POLICE_OFFICER_RETURN_TIMEOUT", officer);
            HandleOfficerReachedCar(officer);
        }
    }

    private void HandleOfficerReachedCar(EnemyFighter officer)
    {
        if (officer != null)
        {
            Destroy(officer.gameObject);
        }
        if (LastOfficer == officer)
        {
            LastOfficer = null;
        }
        Debug.Log("GYMCHAOS_POLICE_OFFICER_BOARDED", this);
        StartCoroutine(DriveAwayCalmly());
    }

    private IEnumerator DriveAwayCalmly()
    {
        calmDeparture = true;
        dispatchPhase = DispatchPhase.Driving;
        yield return new WaitForSeconds(0.8f);
        if (policeCar != null && currentRoute != null && currentRoute.Length >= 2)
        {
            yield return MoveCarAlongRoute(currentRoute, false);
        }
        Debug.Log("GYMCHAOS_POLICE_CAR_DEPARTED", this);
        CleanupDispatch();
        dispatchPhase = DispatchPhase.Idle;
        calmDeparture = false;
    }

    private void HandlePoliceCarLoaded(GameObject root)
    {
        if (root == null)
        {
            policeCarFailed = true;
            return;
        }

        policeCar = root;
        FitCarVisual(root);
        GymVehicleWheelSpinner.Attach(root, () => policeCarMoving);
        AddCarCollider(root);
        policeCarRouteBoundsClear = true;
        policeCarStopWithinRoadEnvelope = false;
        policeCarRouteOverlapCount = 0;
        policeCarReady = true;
        CreatePoliceEngineAudio(root);
        BeginPoliceSirenLoad();
        Debug.Log(
            $"GYMCHAOS_POLICE_CAR_READY asset={PoliceCarAsset} " +
            $"targetLength={PoliceCarTargetLength:F2}", root);
    }

    private EnemyFighter SpawnOfficer(
        EnemyFighter ronnie, Transform target, PlayerMovement player)
    {
        Vector3 spawn = GymDoorway.Instance.ExteriorPoint;
        Vector3 carForward = policeCar != null
            ? Vector3.ProjectOnPlane(policeCar.transform.forward, Vector3.up)
            : Vector3.forward;
        if (carForward.sqrMagnitude < 0.01f)
        {
            carForward = Vector3.forward;
        }
        carForward.Normalize();
        if (policeCar != null)
        {
            // The car faces into its stall, so the parking aisle is behind it.
            spawn = policeCar.transform.position -
                carForward * (PoliceCarTargetLength * 0.5f + 1.1f);
        }
        spawn.y = floorY;
        officerExitPosition = spawn;
        officerExited = true;
        Vector3 look = Vector3.ProjectOnPlane(target.position - spawn, Vector3.up);
        Quaternion rotation = look.sqrMagnitude > 0.01f
            ? Quaternion.LookRotation(look.normalized, Vector3.up)
            : Quaternion.identity;

        GameObject officerObject = new GameObject("Policeman Officer");
        officerObject.transform.position = spawn;
        officerObject.transform.rotation = rotation;
        // The officer is a response NPC, not a generic gym enemy. It still
        // owns an EnemyFighter for the police-specific combat state machine,
        // but must not enter enemy-tag queries, opponent counts, or the
        // shared enemy collision layer.
        officerObject.tag = "Untagged";
        officerObject.layer = EnemyFighter.EnemyCollisionLayer;

        CapsuleCollider capsule = officerObject.AddComponent<CapsuleCollider>();
        capsule.center = new Vector3(0f, EnemyFighter.EnemyCapsuleCenterY, 0f);
        capsule.height = EnemyFighter.EnemyCapsuleHeight;
        capsule.radius = EnemyFighter.GetBodyRadiusForIdentity(
            BodybuilderIdentity.Policeman);
        Rigidbody body = officerObject.AddComponent<Rigidbody>();
        body.mass = 85f;
        body.linearDamping = 1.5f;
        body.angularDamping = 0.5f;
        body.constraints = RigidbodyConstraints.FreezeRotationX |
            RigidbodyConstraints.FreezeRotationZ;
        body.interpolation = RigidbodyInterpolation.Interpolate;

        BodybuilderEnemyVisual.Build(officerObject, BodybuilderIdentity.Policeman);
        EnemyFighter fighter = officerObject.AddComponent<EnemyFighter>();
        float ronnieHealth = ronnie != null ? ronnie.MaxHealth : 100f;
        fighter.Configure(
            BodybuilderIdentity.Policeman,
            player,
            ronnieHealth * OfficerHealthFraction,
            police: true,
            passive: false,
            countAsOpponent: false);
        SetPoliceClassification(officerObject);
        Collider[] officerColliders =
            officerObject.GetComponentsInChildren<Collider>(true);
        if (policeCarCollider != null)
        {
            // Apply pair filters only after the officer hierarchy/layers are
            // final. Runtime layer reclassification can rebuild PhysX shape
            // filters and otherwise discard an earlier ignore pair.
            for (int i = 0; i < officerColliders.Length; i++)
            {
                Physics.IgnoreCollision(
                    policeCarCollider, officerColliders[i], true);
            }
        }
        IgnoreOfficerRouteBlocker(
            officerColliders, "Outdoor Boundary - Path Outer");
        IgnoreOfficerRouteBlocker(
            officerColliders, "Player Road Access Blocker");
        fighter.SetPoliceKillerTarget(target);
        GymLooseItemSpawner.IgnoreDeadliftStationForEnemy(fighter);

        GymPoliceWeapon weapon = officerObject.AddComponent<GymPoliceWeapon>();
        weapon.Configure(fighter);
        GymPoliceOfficer officer = officerObject.AddComponent<GymPoliceOfficer>();
        officer.Configure(fighter, target);
        return fighter;
    }

    private static void IgnoreOfficerRouteBlocker(
        Collider[] officerColliders, string blockerName)
    {
        if (officerColliders == null || officerColliders.Length == 0)
        {
            return;
        }
        GameObject blocker = GameObject.Find(blockerName);
        Collider blockerCollider = blocker != null
            ? blocker.GetComponent<Collider>()
            : null;
        if (blockerCollider != null)
        {
            bool active = true;
            for (int i = 0; i < officerColliders.Length; i++)
            {
                Collider officerCollider = officerColliders[i];
                if (officerCollider == null)
                {
                    continue;
                }
                Physics.IgnoreCollision(officerCollider, blockerCollider, true);
                active &= Physics.GetIgnoreCollision(
                    officerCollider, blockerCollider);
            }
            Debug.Log(
                $"GYMCHAOS_POLICE_ROUTE_COLLISION_IGNORED " +
                $"blocker={blockerName} colliders={officerColliders.Length} " +
                $"active={active}",
                officerColliders[0]);
        }
    }

    private static void SetPoliceClassification(GameObject root)
    {
        if (root == null)
        {
            return;
        }

        Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < transforms.Length; i++)
        {
            Transform current = transforms[i];
            if (current == null)
            {
                continue;
            }

            current.gameObject.tag = "Untagged";
            current.gameObject.layer = 0;
        }
    }

    // Same legs as a visitor car (GymVisitorVehicle.BeginDriveIn): arrival
    // road spawn, road turn, junction, parking aisle, then a free stall.
    private Vector3[] BuildArrivalRoute(float y)
    {
        Vector3 stall = FindFreeParkingStall(y);
        Vector3 aisle = new Vector3(
            stall.x, y, GymOutdoorBuilder.ParkingBounds.center.z);
        return new[]
        {
            SetY(GymOutdoorBuilder.VehicleArrivalRoadSpawnPoint, y),
            SetY(GymOutdoorBuilder.VehicleArrivalRoadTurnPoint, y),
            SetY(GymOutdoorBuilder.VehicleArrivalRoadJunctionPoint, y),
            aisle,
            stall
        };
    }

    // Prefers a physically empty stall that no visitor lifecycle slot maps
    // to, closest to the gym door; falls back to any empty stall.
    private Vector3 FindFreeParkingStall(float y)
    {
        int bayCount = Mathf.Max(1, GymOutdoorBuilder.ParkingBayCount);
        HashSet<int> visitorStalls = new HashSet<int>();
        for (int slot = 0; slot < 6; slot++)
        {
            int column = Mathf.Clamp(
                Mathf.FloorToInt((slot + 0.5f) * bayCount / 6f), 0, bayCount - 1);
            visitorStalls.Add(column * 2 + (slot % 2 == 0 ? 0 : 1));
        }

        Vector3 door = GymDoorway.Instance != null
            ? GymDoorway.Instance.ExteriorPoint
            : GymOutdoorBuilder.ParkingBounds.center;
        Vector3 best = Vector3.zero;
        float bestScore = float.PositiveInfinity;
        for (int column = 0; column < bayCount; column++)
        {
            for (int row = 0; row < 2; row++)
            {
                float rowSign = row == 0 ? -1f : 1f;
                Vector3 stall = new Vector3(
                    GymOutdoorBuilder.GetParkingBayCenterX(column), y,
                    GymOutdoorBuilder.GetParkingStallCenterZ(rowSign));
                if (!IsParkingStallEmpty(stall))
                {
                    continue;
                }
                float score = Vector3.Distance(
                    Vector3.ProjectOnPlane(stall, Vector3.up),
                    Vector3.ProjectOnPlane(door, Vector3.up));
                if (visitorStalls.Contains(column * 2 + row))
                {
                    score += 1000f;
                }
                if (score < bestScore)
                {
                    bestScore = score;
                    best = stall;
                }
            }
        }
        if (float.IsPositiveInfinity(bestScore))
        {
            best = new Vector3(
                GymOutdoorBuilder.GetParkingBayCenterX(bayCount - 1), y,
                GymOutdoorBuilder.GetParkingStallCenterZ(-1f));
        }
        return best;
    }

    private bool IsParkingStallEmpty(Vector3 stall)
    {
        Vector3 halfExtents = new Vector3(1.1f * GymOutdoorBuilder.VehicleScale, 0.9f,
            GymOutdoorBuilder.ParkingVehicleTargetLength * 0.5f);
        int count = Physics.OverlapBoxNonAlloc(
            stall + Vector3.up * 1.2f, halfExtents, carOverlapHits,
            Quaternion.identity, Physics.AllLayers, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            Collider collider = carOverlapHits[i];
            if (collider != null && collider.bounds.max.y > floorY + 0.25f)
            {
                return false;
            }
        }
        return true;
    }

    private IEnumerator MoveCarAlongRoute(Vector3[] route, bool forward)
    {
        if (policeCar == null || route == null || route.Length < 2)
        {
            SetPoliceCarMoving(false);
            yield break;
        }

        if (!forward)
        {
            List<Vector3> reversed = new List<Vector3>(route);
            reversed.Reverse();
            route = reversed.ToArray();
        }

        SetPoliceCarMoving(true);
        for (int i = 1; i < route.Length && policeCar != null; i++)
        {
            yield return MoveCarTo(route[i]);
        }
        SetPoliceCarMoving(false);
    }

    private void BeginPoliceSirenLoad()
    {
        if (policeSirenLoadStarted ||
            policeSirenClip != null ||
            policeSirenLoadFailed)
        {
            return;
        }

        policeSirenLoadStarted = true;
        policeSirenLoadRoutine = StartCoroutine(LoadPoliceSiren());
    }

    private IEnumerator WaitForPoliceSiren(float timeout)
    {
        BeginPoliceSirenLoad();
        float deadline = Time.time + Mathf.Max(0.5f, timeout);
        while (!policeSirenLoadFinished &&
            !policeSirenLoadFailed &&
            Time.time < deadline)
        {
            yield return null;
        }

        if (!policeSirenLoadFinished)
        {
            Debug.LogWarning(
                $"GYMCHAOS_POLICE_SIREN_UNAVAILABLE asset={PoliceSirenAsset} " +
                $"failed={policeSirenLoadFailed}",
                this);
        }
    }

    private IEnumerator LoadPoliceSiren()
    {
        string path = JoinStreamingAssetsPath(PoliceSirenAsset);
        AudioClip clip = null;
        using (UnityWebRequest request =
            UnityWebRequestMultimedia.GetAudioClip(path, AudioType.MPEG))
        {
            DownloadHandlerAudioClip handler =
                request.downloadHandler as DownloadHandlerAudioClip;
            if (handler != null)
            {
                handler.streamAudio = false;
            }

            yield return request.SendWebRequest();
            if (request.result == UnityWebRequest.Result.Success)
            {
                clip = DownloadHandlerAudioClip.GetContent(request);
            }
            else
            {
                Debug.LogWarning(
                    $"GYMCHAOS_POLICE_SIREN_LOAD_ERROR asset={PoliceSirenAsset} " +
                    $"error={request.error}",
                    this);
            }
        }

        policeSirenLoadRoutine = null;
        if (clip == null)
        {
            policeSirenLoadFailed = true;
            yield break;
        }

        if (policeCar == null)
        {
            Destroy(clip);
            policeSirenLoadFailed = true;
            yield break;
        }

        policeSirenClip = clip;
        policeSirenClip.name = "Police siren";
        policeSiren = policeCar.AddComponent<AudioSource>();
        policeSiren.playOnAwake = false;
        policeSiren.loop = true;
        policeSiren.clip = policeSirenClip;
        // Louder and wider than the shared engine loop under it.
        policeSiren.volume = 0.95f;
        policeSiren.spatialBlend = 1f;
        policeSiren.rolloffMode = AudioRolloffMode.Logarithmic;
        policeSiren.minDistance = 6f;
        policeSiren.maxDistance = 60f;
        policeSiren.dopplerLevel = 0f;
        policeSirenLoopConfigured = true;
        policeSirenLoadFinished = true;
        Debug.Log(
            $"GYMCHAOS_POLICE_SIREN_READY asset={PoliceSirenAsset} " +
            $"loop={policeSiren.loop} spatial={policeSiren.spatialBlend:F1}",
            policeCar);

        if (policeCarMoving)
        {
            StartPoliceSirenPlayback();
        }
    }

    // Engine loop under the siren: plays only while the car drives, the
    // parked car is silent (same rule as visitor vehicles).
    private void CreatePoliceEngineAudio(GameObject car)
    {
        policeEngine = car.AddComponent<AudioSource>();
        policeEngine.playOnAwake = false;
        policeEngine.loop = true;
        policeEngine.clip = GymVisitorVehicle.GetEngineLoopClip(false);
        policeEngineFader = GymEngineAudioFader.Attach(policeEngine, 0.3f);
        policeEngine.spatialBlend = 1f;
        policeEngine.rolloffMode = AudioRolloffMode.Logarithmic;
        policeEngine.minDistance = 3f;
        policeEngine.maxDistance = 32f;
        policeEngine.dopplerLevel = 0.35f;
        policeEngine.pitch = 1.08f;
    }

    private GymEngineAudioFader policeEngineFader;
    public bool IsPoliceEnginePlayingForVerification =>
        policeEngine != null && policeEngine.isPlaying &&
        (policeEngineFader == null || policeEngineFader.IsDriving);
    public bool HasPoliceEngineForVerification => policeEngine != null;

    private void SetPoliceCarMoving(bool moving)
    {
        bool changed = policeCarMoving != moving;
        policeCarMoving = moving;
        if (policeEngine != null)
        {
            GymVisitorVehicle.RefreshEngineClip(policeEngine, false);
            if (policeEngineFader != null) policeEngineFader.SetDriving(moving);
            else if (moving && !policeEngine.isPlaying) policeEngine.Play();
            else if (!moving && policeEngine.isPlaying) policeEngine.Stop();
        }

        if (moving)
        {
            StartPoliceSirenPlayback();
            return;
        }

        if (policeSiren != null)
        {
            policeSiren.Stop();
        }

        if ((changed || policeSirenStartedObserved) &&
            policeSirenStartedObserved &&
            !policeSirenStoppedObserved)
        {
            policeSirenStoppedObserved = true;
            Debug.Log(
                $"GYMCHAOS_POLICE_SIREN_STOPPED asset={PoliceSirenAsset} " +
                $"moving=False",
                this);
        }
    }

    private void StartPoliceSirenPlayback()
    {
        if (!policeCarMoving ||
            calmDeparture ||
            policeSiren == null ||
            policeSirenClip == null)
        {
            return;
        }

        if (!policeSiren.isPlaying)
        {
            policeSiren.Play();
        }

        if (!policeSirenStartedObserved)
        {
            policeSirenStartedObserved = true;
            Debug.Log(
                $"GYMCHAOS_POLICE_SIREN_PLAYING asset={PoliceSirenAsset} " +
                $"loop={policeSiren.loop} moving=True",
                policeCar);
        }
    }

    private static string JoinStreamingAssetsPath(string relativePath)
    {
        string path =
            Application.streamingAssetsPath.TrimEnd('/', '\\') + "/" +
            relativePath;
        if (path.Contains("://"))
        {
            return path;
        }

        return "file:///" + path.Replace('\\', '/').TrimStart('/');
    }

    private void ResetPoliceSirenState()
    {
        policeSirenLoadStarted = false;
        policeSirenLoadFinished = false;
        policeSirenLoadFailed = false;
        policeCarMoving = false;
        policeSirenStartedObserved = false;
        policeSirenStoppedObserved = false;
        policeSirenLoopConfigured = false;
        policeSiren = null;
        policeSirenClip = null;
        policeSirenLoadRoutine = null;
    }

    private IEnumerator MoveCarTo(Vector3 destination)
    {
        float blockedSince = -1f;
        while (policeCar != null)
        {
            Vector3 planar = Vector3.ProjectOnPlane(
                destination - policeCar.transform.position, Vector3.up);
            if (planar.magnitude <= 0.12f)
            {
                policeCar.transform.position = SetY(
                    destination, policeCarSupportY);
                SamplePoliceCarRouteBounds();
                yield break;
            }

            Vector3 direction = planar.normalized;
            float totalStep = Mathf.Min(
                PoliceCarSpeed * Mathf.Max(0f, Time.deltaTime),
                planar.magnitude);
            int substeps = Mathf.Max(1, Mathf.CeilToInt(totalStep / 0.40f));
            float step = totalStep / substeps;
            bool blocked = false;
            for (int substep = 0; substep < substeps; substep++)
            {
                if (!HasClearCarSegment(direction, step + 0.1f))
                {
                    blocked = true;
                    break;
                }

                Vector3 next = policeCar.transform.position + direction * step;
                next.y = policeCarSupportY;
                policeCar.transform.position = next;
                policeCar.transform.rotation = Quaternion.Slerp(
                    policeCar.transform.rotation,
                    Quaternion.LookRotation(direction, Vector3.up),
                    10f * Mathf.Max(0f, Time.deltaTime));
                SamplePoliceCarRouteBounds();
            }

            if (blocked)
            {
                if (blockedSince < 0f)
                {
                    blockedSince = Time.time;
                }
                // A person or car can hold the probe indefinitely on the shared
                // road. Never strand the dispatch: after a long wait, finish
                // this leg directly.
                if (Time.time - blockedSince >= PoliceCarBlockedSnapSeconds)
                {
                    Debug.LogWarning(
                        $"GYMCHAOS_POLICE_CAR_BLOCKED_SNAP blocker={lastLoggedCarBlocker} " +
                        $"destination={destination}", this);
                    policeCar.transform.SetPositionAndRotation(
                        SetY(destination, policeCarSupportY),
                        Quaternion.LookRotation(direction, Vector3.up));
                    SamplePoliceCarRouteBounds();
                    yield break;
                }
                yield return null;
                continue;
            }

            blockedSince = -1f;
            if (Time.deltaTime > 0.0001f)
            {
                maxMeasuredCarSpeed = Mathf.Max(
                    maxMeasuredCarSpeed, totalStep / Time.deltaTime);
            }
            yield return null;
        }
    }

    private bool HasClearCarSegment(Vector3 direction, float distance)
    {
        if (policeCar == null)
        {
            return false;
        }

        Vector3 origin = policeCar.transform.position + Vector3.up * 1.0f;
        int count = Physics.SphereCastNonAlloc(
            origin,
            PoliceCarProbeRadius,
            direction,
            routeHits,
            distance,
            Physics.AllLayers,
            QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            Collider collider = routeHits[i].collider;
            if (collider == null ||
                collider.transform.IsChildOf(policeCar.transform))
            {
                continue;
            }
            // Horizontal ground slabs overlap the probe's lower hemisphere
            // by design. They are support surfaces, not route blockers;
            // vertical walls, fences, people and vehicles still fail here.
            // This invisible collider is retained for the player's outdoor
            // boundary, but the authored vehicle opening intentionally passes
            // through it. Do not make police traffic collide with a player-only
            // limit that normal visitor vehicles already ignore.
            if (collider.name == "Outdoor Boundary - Path Outer" ||
                collider.name == "Player Road Access Blocker")
            {
                continue;
            }
            if (collider.bounds.max.y <= floorY + 0.25f)
            {
                continue;
            }
            string blockerName = collider.name + "/" +
                (collider.transform.parent != null
                    ? collider.transform.parent.name
                    : "root");
            if (!string.Equals(lastLoggedCarBlocker, blockerName))
            {
                lastLoggedCarBlocker = blockerName;
                Debug.LogWarning(
                    $"GYMCHAOS_POLICE_CAR_BLOCKER collider={blockerName} " +
                    $"bounds={collider.bounds}", this);
            }
            return false;
        }
        return true;
    }

    // Largest planar distance of the sampled car position from the route
    // polyline. Proves the car actually drove the visitor legs.
    private float maxRouteDeviation;
    public float PoliceCarMaxRouteDeviationForVerification => maxRouteDeviation;

    private void SampleRouteDeviation()
    {
        if (policeCar == null || currentRoute == null || currentRoute.Length < 2)
        {
            return;
        }
        Vector3 point = Vector3.ProjectOnPlane(policeCar.transform.position, Vector3.up);
        float best = float.PositiveInfinity;
        for (int i = 1; i < currentRoute.Length; i++)
        {
            Vector3 a = Vector3.ProjectOnPlane(currentRoute[i - 1], Vector3.up);
            Vector3 b = Vector3.ProjectOnPlane(currentRoute[i], Vector3.up);
            Vector3 ab = b - a;
            float t = ab.sqrMagnitude > 0.0001f
                ? Mathf.Clamp01(Vector3.Dot(point - a, ab) / ab.sqrMagnitude)
                : 0f;
            best = Mathf.Min(best, Vector3.Distance(point, a + ab * t));
        }
        maxRouteDeviation = Mathf.Max(maxRouteDeviation, best);
    }

    private bool SamplePoliceCarRouteBounds()
    {
        SampleRouteDeviation();
        if (policeCar == null || policeCarCollider == null)
        {
            policeCarRouteBoundsClear = false;
            return false;
        }

        Physics.SyncTransforms();
        Bounds broadBounds = policeCarCollider.bounds;
        int count = Physics.OverlapBoxNonAlloc(
            broadBounds.center,
            broadBounds.extents + Vector3.one * 0.002f,
            carOverlapHits,
            Quaternion.identity,
            Physics.AllLayers,
            QueryTriggerInteraction.Ignore);
        bool clear = true;
        for (int i = 0; i < count; i++)
        {
            Collider collider = carOverlapHits[i];
            if (collider == null || collider == policeCarCollider ||
                collider.transform.IsChildOf(policeCar.transform))
            {
                continue;
            }

            // Ground and road slabs support the vehicle by design.  The two
            // player-only blockers are intentionally ignored by traffic and
            // are also ignored by HasClearCarSegment above.
            if (collider.bounds.max.y <= floorY + 0.25f ||
                collider.name == "Outdoor Boundary - Path Outer" ||
                collider.name == "Player Road Access Blocker")
            {
                continue;
            }

            if (!Physics.ComputePenetration(
                    policeCarCollider,
                    policeCarCollider.transform.position,
                    policeCarCollider.transform.rotation,
                    collider,
                    collider.transform.position,
                    collider.transform.rotation,
                    out Vector3 _,
                    out float depth) || depth <= 0.001f)
            {
                continue;
            }

            clear = false;
            policeCarRouteOverlapCount++;
            Debug.LogWarning(
                $"GYMCHAOS_POLICE_CAR_ROUTE_OVERLAP collider={collider.name} " +
                $"depth={depth:F3} bounds={collider.bounds}", this);
            break;
        }

        if (!clear)
        {
            policeCarRouteBoundsClear = false;
        }
        return clear;
    }

    private bool VerifyPoliceCarStopWithinRoadEnvelope()
    {
        if (policeCar == null || policeCarCollider == null)
        {
            return false;
        }

        float stopDistance = Vector3.Distance(
            Vector3.ProjectOnPlane(policeCar.transform.position, Vector3.up),
            Vector3.ProjectOnPlane(policeCarStopPoint, Vector3.up));
        return stopDistance <= 0.25f && IsInsideParking(policeCar.transform.position);
    }

    private static bool IsInsideParking(Vector3 point)
    {
        Bounds parking = GymOutdoorBuilder.ParkingBounds;
        return point.x >= parking.min.x && point.x <= parking.max.x &&
            point.z >= parking.min.z && point.z <= parking.max.z;
    }

    private void FitCarVisual(GameObject root)
    {
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
        {
            return;
        }
        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
        {
            bounds.Encapsulate(renderers[i].bounds);
        }
        float horizontal = Mathf.Max(bounds.size.x, bounds.size.z);
        if (horizontal > 0.001f)
        {
            root.transform.localScale *= PoliceCarTargetLength / horizontal;
        }
        Physics.SyncTransforms();
        bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
        {
            bounds.Encapsulate(renderers[i].bounds);
        }
        root.transform.position += Vector3.up *
            (floorY - bounds.min.y + PoliceCarGroundClearance);
        policeCarSupportY = root.transform.position.y;
    }

    private void AddCarCollider(GameObject root)
    {
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
        {
            return;
        }
        Bounds world = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
        {
            world.Encapsulate(renderers[i].bounds);
        }
        BoxCollider collider = root.AddComponent<BoxCollider>();
        policeCarCollider = collider;
        collider.center = root.transform.InverseTransformPoint(world.center);
        collider.size = new Vector3(
            world.size.x / Mathf.Max(0.01f, root.transform.lossyScale.x),
            world.size.y / Mathf.Max(0.01f, root.transform.lossyScale.y),
            world.size.z / Mathf.Max(0.01f, root.transform.lossyScale.z));
        Rigidbody body = root.AddComponent<Rigidbody>();
        body.isKinematic = true;
        body.useGravity = false;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
    }

    private static Transform ResolveKiller(Transform killer, PlayerMovement player)
    {
        if (killer != null)
        {
            EnemyFighter fighter = killer.GetComponentInParent<EnemyFighter>();
            if (fighter != null && !fighter.IsDead)
            {
                return fighter.transform;
            }
            PlayerMovement playerTarget = killer.GetComponentInParent<PlayerMovement>();
            if (playerTarget != null && !playerTarget.IsDead)
            {
                return playerTarget.transform;
            }
        }
        return player != null && !player.IsDead ? player.transform : null;
    }

    private static Vector3 SetY(Vector3 point, float y)
    {
        point.y = y;
        return point;
    }

    private void CleanupDispatch()
    {
        SetPoliceCarMoving(false);
        if (policeSirenLoadRoutine != null)
        {
            StopCoroutine(policeSirenLoadRoutine);
            policeSirenLoadRoutine = null;
        }
        if (policeCar != null)
        {
            Destroy(policeCar);
            policeCar = null;
        }
        if (policeSirenClip != null)
        {
            Destroy(policeSirenClip);
            policeSirenClip = null;
        }
        policeSiren = null;
        policeCarReady = false;
        policeCarFailed = false;
        dispatchActive = false;
    }

    private void OnDestroy()
    {
        SetPoliceCarMoving(false);
        if (policeSirenClip != null)
        {
            Destroy(policeSirenClip);
            policeSirenClip = null;
        }
        if (instance == this)
        {
            instance = null;
        }
    }
}
