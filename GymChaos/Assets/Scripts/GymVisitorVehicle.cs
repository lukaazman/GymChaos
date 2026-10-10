using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Visible, textured, collidable arrival/parking/departure for one visitor.</summary>
public sealed class GymVisitorVehicle : MonoBehaviour
{
    public const float NormalDriveSpeed = 8.5f;
    private const float DriveSpeed = NormalDriveSpeed;
    private const float CloudSpeed = 48f;
    private const string DavieBusAsset =
        "BodyBuilders/vehicles/Davie_Bus_lod.glb";
    // Every vehicle is 1.25x its original size (GymOutdoorBuilder.VehicleScale);
    // distances measured in vehicle lengths scale with it.
    private const float S = GymOutdoorBuilder.VehicleScale;
    private const float DavieBusTargetLength = 10.4f * S;
    private const string ArnoldHummerAsset =
        "BodyBuilders/vehicles/Arnold_Hummer_lod.glb";
    private const float ArnoldHummerTargetLength = 4.0f * S;
    private const float CloudTargetLength = 2.8f * S;
    private const float TurnSpeed = 5f;
    private const float MaxGroundRouteDeltaTime = 0.05f;
    private const float MaxGroundRouteStep = 0.4f;
    // Ground routes run this far above the asphalt top; the visual body is
    // placed back on the asphalt, with the tyres pressed in very slightly so
    // no gap shows under the wheels.
    private const float RouteHeightAboveFloor = 0.08f;
    public const float TyreContactSink = 0.015f;
    private const float LaneSpawnSpacing = 7.5f * S;
    private const float ForwardSensorMinimumDistance = 2.35f * S;
    private const float ForwardSensorMaximumDistance = 3.35f * S;
    private const float ForwardSensorHalfWidth = 0.92f * S;
    private const float CrossingPredictionSeconds = 0.45f;
    private static readonly List<GymVisitorVehicle> activeGroundTraffic =
        new List<GymVisitorVehicle>();
    private static int nextTrafficOrder;

    private BodybuilderIdentity identity;
    private PlayerMovement player;
    private AudioSource engine;
    private GymEngineAudioFader engineFader;
    private AudioSource horn;
    private AudioClip hornClip;
    private float currentDriveSpeed;
    private float blockedSince = -1f;
    private float nextHornTime;
    private string lastTrafficBlocker = "none";
    private string lastTrafficClearanceSource = "none";
    private float nextRouteStallLogTime;
#if UNITY_EDITOR
    private float nextRouteProgressLogTime;
    private int currentRouteWaypointIndex = -1;
    private Vector3 currentRouteTarget;
    private float currentRouteRemaining;
    private float currentRouteClearance = float.PositiveInfinity;
    private float currentRouteActualTravel;
    private string currentRouteClearanceSource = "none";
#endif
    private int hornPlayCount;
    private int trafficOrder;
    private readonly RaycastHit[] roadHits = new RaycastHit[48];
    private readonly Collider[] nearPeople = new Collider[48];
    public bool IsYieldingToPedestrian { get; private set; }

    private Vector3 roadPoint;
    private Vector3 junctionPoint;
    private Vector3 busBayEntryPoint;
    private Vector3 busBayParkingTurnPoint;
    private Vector3 roadTurnPoint;
    private Vector3 aislePoint;
    private Vector3 parkingPoint;
    private Vector3 departureRoadPoint;
    private Vector3 departureRoadTurnPoint;
    private Vector3 departureJunctionPoint;
    private Vector3 busDepartureApproachPoint;
    private Coroutine driveRoutine;
    private Transform riderAnchor;
    private EnemyFighter mountedRider;
    private bool runtimeVisualReady;
    private bool runtimeVisualLoadFailed;
    // A runtime GLB vehicle owns a coroutine while its visual is loading, but
    // it is not traffic yet. Keep that staging interval out of IsDriving so
    // traffic observers and clearance checks do not treat the initial road
    // point as occupied by two vehicles before the second route is spawned.
    private bool waitingForRuntimeVisual;
    private bool waitingForBusTurnaround;

    public bool IsParked { get; private set; }
    public bool IsDriving => driveRoutine != null && !waitingForRuntimeVisual;
    public static bool HasActiveParkingArrival =>
        HasActiveParkingArrivalExcept(null);

    public static bool HasActiveParkingArrivalExcept(
        GymVisitorVehicle ignoredVehicle)
    {
        for (int i = activeGroundTraffic.Count - 1; i >= 0; i--)
        {
            GymVisitorVehicle other = activeGroundTraffic[i];
            if (other == null)
            {
                activeGroundTraffic.RemoveAt(i);
                continue;
            }
            if (other != ignoredVehicle && other.IsDriving &&
                other.drivingIntoParking)
            {
                return true;
            }
        }
        return false;
    }
    // Distance east of the parking mouth inside which an arriving car
    // already owns the shared connector (about two seconds of driving).
    private const float ParkingConnectorApproachMargin = 14f;

    /// <summary>
    /// True only while another car is actually entering the shared parking
    /// connector. A car still far down the access road, or the bus heading
    /// for the roadside stop, does not cross a passenger's walk to the gym.
    /// A car already stopped for a pedestrian lets the passenger go first,
    /// otherwise both would wait for each other.
    /// </summary>
    public static bool HasConflictingParkingArrivalExcept(
        GymVisitorVehicle ignoredVehicle)
    {
        for (int i = activeGroundTraffic.Count - 1; i >= 0; i--)
        {
            GymVisitorVehicle other = activeGroundTraffic[i];
            if (other == null)
            {
                activeGroundTraffic.RemoveAt(i);
                continue;
            }
            if (other != ignoredVehicle && other.IsDriving &&
                other.drivingIntoParking && !other.UsesRoadsideBusRoute &&
                !other.IsCloud && other.IsNearParkingConnector &&
                !other.IsYieldingToPedestrian)
            {
                return true;
            }
        }
        return false;
    }

    private bool IsNearParkingConnector
    {
        get
        {
            Bounds parking = GymOutdoorBuilder.ParkingBounds;
            Vector3 position = transform.position;
            return position.x <= GymOutdoorBuilder.VehicleRoadJunctionPoint.x +
                    ParkingConnectorApproachMargin &&
                position.z >= parking.min.z - 4f &&
                position.z <= parking.max.z + 4f;
        }
    }
    public bool HasCompletedDeparture { get; private set; }
    public bool IsCloud => identity == BodybuilderIdentity.Goku;
    public bool IsBus => identity == BodybuilderIdentity.Davie;
    public bool IsArnold => identity == BodybuilderIdentity.Arnold;
    private bool NeedsRuntimeVisual => IsBus || IsArnold;
    private bool UsesRoadsideBusRoute =>
        IsBus && GymRoadsideBusStop.IsBuilt;
    public bool HasMountedRider => mountedRider != null;
    public static bool IsGroundTrafficActive => activeGroundTraffic.Count > 0;
    public bool HasPhysicalCollider => GetComponent<BoxCollider>() != null &&
        GetComponent<Rigidbody>() != null;
    public bool HasOriginalTexture
    {
        get
        {
            Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
            for (int r = 0; r < renderers.Length; r++)
            {
                Material[] materials = renderers[r].sharedMaterials;
                for (int i = 0; i < materials.Length; i++)
                {
                    Material material = materials[i];
                    if (material != null &&
                        ((material.HasProperty("_BaseMap") && material.GetTexture("_BaseMap") != null) ||
                         (material.HasProperty("_MainTex") && material.GetTexture("_MainTex") != null)))
                    {
                        return true;
                    }
                }
            }
            return false;
        }
    }
    // Keep the boarding point outside the vehicle collider plus visitor capsule.
    // Goku's cloud is shorter than a car but its broad visual/physical footprint
    // made the old 1.45 m point unreachable when neighbouring bays were occupied.
    // Put him clearly on the aisle side so he dismounts onto the ground, walks,
    // and only mounts again after reaching a collision-safe point by the cloud.
    public Vector3 PassengerPoint
    {
        get
        {
            if (IsBus && GymRoadsideBusStop.IsBuilt)
            {
                return GymRoadsideBusStop.DavieBusPassengerPoint;
            }

            Vector3 towardAisle = Vector3.ProjectOnPlane(
                aislePoint - parkingPoint, Vector3.up);
            if (towardAisle.sqrMagnitude < 0.01f)
            {
                towardAisle = -transform.forward;
            }

            // Derive the dismount/boarding side from the authored parking
            // geometry, never from the visual vehicle yaw. Imported vehicle
            // orientations can be diagonal, which used to place a visitor
            // behind the car and force the first steering probe around it.
            return parkingPoint + towardAisle.normalized *
                (IsCloud ? 2.45f : 2.9f);
        }
    }
    public Vector3 AislePassengerPoint => aislePoint;
    public float BoardingReachDistance => IsCloud || IsBus ? 0.8f : 0.65f;
#if UNITY_EDITOR
    public static float NormalDriveSpeedForVerification => DriveSpeed;
    public Vector3 ParkingPointForVerification => parkingPoint;
    public Vector3 RoadPointForVerification => roadPoint;
    public bool HasDistanceAttenuatedAudioForVerification => engine != null &&
        !engine.playOnAwake && engine.loop && engine.spatialBlend >= 0.99f &&
        engine.rolloffMode == AudioRolloffMode.Logarithmic &&
        engine.maxDistance > engine.minDistance && engine.maxDistance >= 20f;
    public bool HasCorrectDrivingSoundForVerification => engine != null &&
        engine.clip != null && engine.clip.name ==
        (IsCloud ? "Goku day flying loop" : "Car driving loop");
    public string DrivingSoundClipNameForVerification => engine?.clip?.name ?? "missing";
    public bool IsEngineMutedForVerification => engine == null || engine.mute;
    public bool IsHornMutedForVerification => horn == null || horn.mute;
    // Parked: silent, or still fading out after parking.
    public bool IsEngineStoppedForVerification => engine == null || !engine.isPlaying ||
        (engineFader != null && !engineFader.IsDriving);
    public static float SpawnSpacingForVerification => LaneSpawnSpacing;
    public static float SensorMaximumDistanceForVerification => ForwardSensorMaximumDistance;
    public static float SensorHalfWidthForVerification => ForwardSensorHalfWidth;
    public static float CrossingPredictionForVerification => CrossingPredictionSeconds;
    public float CurrentDriveSpeedForVerification => currentDriveSpeed;
    public string LastTrafficBlockerForVerification => lastTrafficBlocker;
    public int CurrentRouteWaypointIndexForVerification => currentRouteWaypointIndex;
    public Vector3 CurrentRouteTargetForVerification => currentRouteTarget;
    public float CurrentRouteRemainingForVerification => currentRouteRemaining;
    public float CurrentRouteClearanceForVerification => currentRouteClearance;
    public float CurrentRouteActualTravelForVerification => currentRouteActualTravel;
    public string CurrentRouteClearanceSourceForVerification => currentRouteClearanceSource;
    public int HornPlayCountForVerification => hornPlayCount;
    public Vector3 ArrivalRoadTurnPointForVerification => roadTurnPoint;
    public Vector3 DepartureRoadTurnPointForVerification => departureRoadTurnPoint;
    public Vector3 AislePointForVerification => aislePoint;
    public Vector3 DepartureJunctionPointForVerification => departureJunctionPoint;
    public Vector3 DepartureRoadPointForVerification => departureRoadPoint;
    public bool RuntimeVisualReadyForVerification => !NeedsRuntimeVisual || runtimeVisualReady;
    public Bounds RuntimeVisualBoundsForVerification
    {
        get
        {
            if (!runtimeVisualReady)
            {
                return default;
            }

            Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
            return renderers.Length > 0 ? CombinedBounds(renderers) : default;
        }
    }
    public Bounds PhysicalBodyBoundsForVerification
    {
        get
        {
            BoxCollider body = GetComponent<BoxCollider>();
            return body != null ? body.bounds : default;
        }
    }
    public Vector3 ParkedForwardForVerification =>
        parkingPoint.z >= aislePoint.z ? Vector3.forward : Vector3.back;
#endif

    public static GymVisitorVehicle Create(
        BodybuilderIdentity identity, int slot, PlayerMovement player,
        bool initiallyParked)
    {
        string resource = GetResource(identity);
        GameObject root = new GameObject(identity + " Visitor Vehicle");
        GymVisitorVehicle vehicle = root.AddComponent<GymVisitorVehicle>();
        vehicle.identity = identity;
        vehicle.player = player;
        vehicle.ConfigureRoute(slot);
        root.transform.position = initiallyParked ? vehicle.parkingPoint : vehicle.roadPoint;
        root.transform.rotation = vehicle.ParkedRotation;
        GameObject prefab = vehicle.IsBus ? null : Resources.Load<GameObject>(resource);
        if (vehicle.IsBus)
        {
            GymRoadsideBusStop.SetDavieDynamicBusActive(true);
            RuntimeGlbSceneLoader.Request(
                DavieBusAsset,
                root.transform,
                root.transform.position,
                root.transform.rotation,
                Vector3.one * DavieBusTargetLength,
                "Davie Bus Visitor Vehicle",
                root.layer,
                settleOnSupport: true,
                supportY: vehicle.VisualSupportY,
                onLoaded: vehicle.FinalizeRuntimeBus);
        }
        else if (vehicle.IsArnold)
        {
            RuntimeGlbSceneLoader.Request(
                ArnoldHummerAsset,
                root.transform,
                root.transform.position,
                root.transform.rotation,
                Vector3.one,
                "Arnold Hummer Visitor Vehicle",
                root.layer,
                settleOnSupport: true,
                supportY: vehicle.VisualSupportY,
                onLoaded: vehicle.FinalizeRuntimeArnold);
        }
        else if (prefab != null)
        {
            GameObject visual = Instantiate(prefab, root.transform);
            visual.name = identity + " Vehicle Model";
            // Fit the body to the actual bay footprint. The old 4.5m target
            // was wider than the narrowest generated bay and the old route
            // point was not aligned with the painted bay centers.
            vehicle.FitVisual(visual, identity == BodybuilderIdentity.Goku
                ? CloudTargetLength : GymOutdoorBuilder.ParkingVehicleTargetLength);
            vehicle.ApplyOriginalMaterial(visual);
            vehicle.AddPhysicalBody(visual);
            if (vehicle.IsCloud) vehicle.CreateRiderAnchor(visual);
        }
        else
        {
            Debug.LogError($"GYMCHAOS_VEHICLE_MODEL_MISSING identity={identity} path={resource}");
        }
        vehicle.CreateEngineAudio();
        if (!vehicle.IsCloud)
        {
            // Only the separated tyre nodes roll, and only while driving.
            GymVehicleWheelSpinner.Attach(root, () => !vehicle.IsParked);
        }
        vehicle.IsParked = initiallyParked;
        root.SetActive(initiallyParked);
        return vehicle;
    }

    public void DriveIn(Action onParked)
    {
        if (driveRoutine != null) StopCoroutine(driveRoutine);
        followingBusPath = false;
        gameObject.SetActive(true);
        if (IsBus)
        {
            GymRoadsideBusStop.SetDavieDynamicBusActive(true);
        }
        if (NeedsRuntimeVisual && !runtimeVisualReady && !runtimeVisualLoadFailed)
        {
            waitingForRuntimeVisual = true;
            driveRoutine = StartCoroutine(
                WaitForRuntimeVisualThenDriveIn(onParked));
            return;
        }

        BeginDriveIn(onParked);
    }

    private void BeginDriveIn(Action onParked)
    {
        followingBusPath = false;
        waitingForRuntimeVisual = false;
        waitingForBusTurnaround = false;
        HasCompletedDeparture = false;
        IsParked = false;
        drivingIntoParking = true;
        trafficOrder = ++nextTrafficOrder;
        Vector3 queueDirection = Vector3.ProjectOnPlane(
            roadPoint - roadTurnPoint, Vector3.up).normalized;
        transform.position = roadPoint + queueDirection *
            (CountIncomingTraffic() * LaneSpawnSpacing);
        if (IsCloud)
        {
            driveRoutine = StartCoroutine(DriveCloudRoute(true, onParked));
            return;
        }
        Vector3[] route = IsBus && UsesRoadsideBusRoute
            ? CreateDavieBusArrivalRoute()
            : new[] { roadPoint, roadTurnPoint, junctionPoint, aislePoint, parkingPoint };
        driveRoutine = StartCoroutine(DriveRoute(route, true, onParked, 0f, IsBus));
    }

    public void DriveOut(Action onGone)
    {
        if (driveRoutine != null) StopCoroutine(driveRoutine);
        followingBusPath = false;
        gameObject.SetActive(true);
        if (IsBus)
        {
            GymRoadsideBusStop.SetDavieDynamicBusActive(true);
        }
        if (NeedsRuntimeVisual && !runtimeVisualReady && !runtimeVisualLoadFailed)
        {
            waitingForRuntimeVisual = true;
            driveRoutine = StartCoroutine(
                WaitForRuntimeVisualThenDriveOut(onGone));
            return;
        }

        BeginDriveOut(onGone);
    }

    private void BeginDriveOut(Action onGone)
    {
        followingBusPath = false;
        waitingForRuntimeVisual = false;
        waitingForBusTurnaround = false;
        IsParked = false;
        drivingIntoParking = false;
        trafficOrder = ++nextTrafficOrder;
        if (IsCloud)
        {
            driveRoutine = StartCoroutine(DriveCloudRoute(false, onGone));
            return;
        }
        if (UsesRoadsideBusRoute)
        {
            // When the turnaround is already clear the wait finishes inside
            // StartCoroutine and has itself started the drive, so only keep
            // the wait's handle while it is really still waiting.
            Coroutine wait = StartCoroutine(
                WaitForBusTurnaroundThenDriveOut(onGone));
            if (waitingForBusTurnaround)
            {
                driveRoutine = wait;
            }
            return;
        }
        Vector3[] route = new[] {
            UsesRoadsideBusRoute ? parkingPoint : aislePoint,
            UsesRoadsideBusRoute ? busDepartureApproachPoint : departureJunctionPoint,
            departureRoadTurnPoint,
            departureRoadPoint };
        int queueRank = CountOutgoingTraffic();
        float releaseDelay = queueRank * 1.6f;
        driveRoutine = StartCoroutine(DriveRoute(route, false, onGone, releaseDelay));
        Debug.Log($"GYMCHAOS_VEHICLE_DEPARTURE_QUEUED identity={identity} " +
            $"rank={queueRank} delay={releaseDelay:F1}", this);
    }

    private IEnumerator WaitForBusTurnaroundThenDriveOut(Action onGone)
    {
        waitingForBusTurnaround = true;
        currentDriveSpeed = 0f;
        SetEngineDriving(false);

        float nextWaitLog = Time.time + 18f;
        bool clear = false;
        while (!clear)
        {
            clear = IsDavieBusTurnaroundClear();
            if (clear) break;
            if (Time.time >= nextWaitLog)
            {
                Debug.LogWarning(
                    "GYMCHAOS_DAVIE_BUS_TURNAROUND_WAITING_FOR_CLEARANCE", this);
                nextWaitLog = Time.time + 10f;
            }
            yield return null;
        }


        waitingForBusTurnaround = false;
        BeginDavieBusDriveOut(onGone);
    }

    private void BeginDavieBusDriveOut(Action onGone)
    {
        Vector3[] route = CreateDavieBusDepartureRoute();
        int queueRank = CountOutgoingTraffic();
        float releaseDelay = queueRank * 1.6f;
        driveRoutine = StartCoroutine(DriveRoute(route, false, onGone, releaseDelay, true));
        Debug.Log(
            $"GYMCHAOS_DAVIE_BUS_DEPARTURE_QUEUED rank={queueRank} " +
            $"delay={releaseDelay:F1} uTurn=clearance-gated",
            this);
    }

    // Arrival: swing slightly wide on the side road, turn right into the
    // inbound lane, then pull into the bay on a lane-change S-curve so the
    // 10.4 m body clears the short wall east of the bay mouth. Offsets were
    // tuned in a swept-body simulation against the bay/road colliders.
    private Vector3[] CreateDavieBusArrivalRoute()
    {
        float y = parkingPoint.y;
        float approachX = roadTurnPoint.x + 2f * S;
        float laneZ = junctionPoint.z;
        Vector3 start = transform.position;
        start.x = approachX;
        start.y = y;
        transform.position = start;
        List<Vector3> route = new List<Vector3>(18)
        {
            start,
            new Vector3(approachX, y, laneZ)
        };
        float curveStartX = GymRoadsideBusStop.BusBayEndX + 1f * S;
        float curveEndX = parkingPoint.x + 4f * S;
        const int curveSamples = 12;
        for (int i = 0; i <= curveSamples; i++)
        {
            float t = i / (float)curveSamples;
            float blend = 0.5f - 0.5f * Mathf.Cos(Mathf.PI * t);
            route.Add(new Vector3(
                Mathf.Lerp(curveStartX, curveEndX, t),
                y,
                Mathf.Lerp(laneZ, parkingPoint.z, blend)));
        }
        route.Add(new Vector3(parkingPoint.x, y, parkingPoint.z));
        return route.ToArray();
    }

    // Departure: pull forward out of the bay, make a left U-turn across the
    // road, return east on the opposite (right-hand) lane, then turn left
    // onto the outbound lane of the side road and leave at the same road
    // spawn point every other departing vehicle uses.
    private Vector3[] CreateDavieBusDepartureRoute()
    {
        float y = parkingPoint.y;
        float returnZ = GymRoadsideBusStop.DavieBusReturnLanePoint.z;
        float turnX = parkingPoint.x - 9f * S;
        // The road has no shoulder: first ease up to the bay's outer side,
        // then turn so the swept body stays between the bay fence and the
        // south fence (tuned in a swept-body simulation of this follower).
        float topZ = parkingPoint.z + BusDepartureBaySwing;
        Vector3 exitTurn = departureRoadTurnPoint;
        Vector3 exitRoad = departureRoadPoint;
        return new[]
        {
            new Vector3(parkingPoint.x, y, parkingPoint.z),
            new Vector3(parkingPoint.x - 2f * S, y, topZ),
            new Vector3(turnX + 7f * S, y, topZ),
            new Vector3(turnX, y, Mathf.Lerp(topZ, returnZ, 0.4f)),
            new Vector3(turnX + 7f * S, y, returnZ),
            // Start the left turn early: the 10.4 m body swings its nose
            // wide, and the corner's east wall sits just past the lane.
            new Vector3(exitTurn.x - BusExitTurnLead, y, returnZ),
            new Vector3(exitRoad.x, y, returnZ + BusExitTurnRise),
            new Vector3(exitRoad.x, y, exitRoad.z)
        };
    }

    private const float BusExitTurnLead = 3.5f * S;
    private const float BusExitTurnRise = 7f * S;

    // Bus path following: the body always moves along its nose and yaws at a
    // speed-limited rate (v / minimum turn radius), so it turns while it
    // drives instead of sliding sideways between waypoints.
    private const float BusArrivalMinTurnRadius = 6f * S;
    private const float BusDepartureMinTurnRadius = 2.4f * S;
    private const float BusDepartureBaySwing = 1.8f;
    private const float BusCornerFillet = 3f * S;
    private const float BusLookaheadMin = 1.2f * S;
    private const float BusLookaheadPerSpeed = 0.35f * S;
    private bool followingBusPath;

    private IEnumerator FollowBusPath(Vector3[] points, bool park, float speed)
    {
        List<Vector3> path = BuildFilletedPath(points, BusCornerFillet);
        float[] lengths = new float[path.Count];
        for (int i = 1; i < path.Count; i++)
        {
            lengths[i] = lengths[i - 1] + Vector3.Distance(path[i - 1], path[i]);
        }
        float total = lengths[path.Count - 1];
        float reach = park ? 0.15f : 1.25f;
        float minTurnRadius = park ? BusArrivalMinTurnRadius : BusDepartureMinTurnRadius;
        float progress = 0f;
        followingBusPath = true;
        if (park)
        {
            // A freshly spawned bus starts aligned with its first leg.
            Vector3 firstLeg = Vector3.ProjectOnPlane(path[1] - path[0], Vector3.up);
            if (firstLeg.sqrMagnitude > 0.01f)
            {
                transform.rotation = Quaternion.LookRotation(firstLeg, Vector3.up);
            }
        }
        while (total - progress > reach)
        {
            int steps = Mathf.Max(1, Mathf.CeilToInt(Time.deltaTime / MaxGroundRouteDeltaTime));
            float dt = Time.deltaTime / steps;
            float clearance = float.PositiveInfinity;
            float travelled = 0f;
            for (int step = 0; step < steps && total - progress > reach; step++)
            {
                travelled += AdvanceBusPathStep(
                    path, lengths, total, ref progress, park, speed,
                    minTurnRadius, dt, out clearance);
            }
#if UNITY_EDITOR
            currentRouteRemaining = total - progress;
            currentRouteClearance = clearance;
            currentRouteActualTravel = travelled;
            currentRouteClearanceSource = lastTrafficClearanceSource;
#endif
            if (Time.time >= nextRouteStallLogTime && travelled <= 0.0001f && Time.deltaTime > 0f)
            {
                Debug.Log(
                    $"GYMCHAOS_VEHICLE_ROUTE_STALL identity={identity} park={park} " +
                    $"position={transform.position} remaining={total - progress:F2} " +
                    $"clearance={clearance:F3} speed={currentDriveSpeed:F2} " +
                    $"blocker={lastTrafficBlocker} source={lastTrafficClearanceSource}", this);
                nextRouteStallLogTime = Time.time + 2.5f;
            }
            if (engine != null) engine.pitch = Mathf.Lerp(0.72f, 1.15f, currentDriveSpeed / speed);
            UpdateEngineAudibility();
            yield return null;
        }
        followingBusPath = false;
        if (park)
        {
            transform.position = path[path.Count - 1];
        }
    }

    private float AdvanceBusPathStep(
        List<Vector3> path, float[] lengths, float total, ref float progress,
        bool park, float speed, float minTurnRadius, float dt, out float clearance)
    {
        progress = ProjectOntoPath(path, lengths, transform.position, progress);
        float remaining = Mathf.Max(0f, total - progress);
        float lookahead = BusLookaheadMin + currentDriveSpeed * BusLookaheadPerSpeed;
        Vector3 aim = SamplePath(path, lengths, progress + lookahead);
        Vector3 forward = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
        Vector3 toAim = Vector3.ProjectOnPlane(aim - transform.position, Vector3.up);
        float headingError = toAim.sqrMagnitude > 0.0001f
            ? Vector3.SignedAngle(forward, toAim, Vector3.up)
            : 0f;
        float maxYaw = currentDriveSpeed / minTurnRadius * Mathf.Rad2Deg * dt;
        forward = Quaternion.AngleAxis(
            Mathf.Clamp(headingError, -maxYaw, maxYaw), Vector3.up) * forward;
        transform.rotation = Quaternion.LookRotation(forward, Vector3.up);

        clearance = TrafficClearance(forward, remaining, out bool pedestrianAhead);
        float cornerFactor = 1f - 0.6f * Mathf.InverseLerp(10f, 60f, Mathf.Abs(headingError));
        float targetSpeed = speed * cornerFactor;
        if (park)
        {
            targetSpeed = Mathf.Min(targetSpeed,
                Mathf.Sqrt(2f * 2.5f * Mathf.Max(0f, remaining - 0.05f)));
        }
        targetSpeed = Mathf.Min(targetSpeed,
            Mathf.Sqrt(2f * 7f * Mathf.Max(0f, clearance - 0.5f)));
        currentDriveSpeed = Mathf.MoveTowards(
            currentDriveSpeed, targetSpeed,
            (targetSpeed < currentDriveSpeed ? 10f : 6.5f) * dt);
        IsYieldingToPedestrian = pedestrianAhead && clearance < 3f;
        UpdateHorn(IsYieldingToPedestrian);

        float travel = Mathf.Min(
            currentDriveSpeed * dt,
            Mathf.Max(0f, clearance - 0.45f),
            MaxGroundRouteStep);
        if (park)
        {
            travel = Mathf.Min(travel, remaining);
        }
        transform.position += forward * travel;
        return travel;
    }

    // Rounds each interior corner with a quadratic Bezier fillet.
    private static List<Vector3> BuildFilletedPath(Vector3[] points, float maxFillet)
    {
        List<Vector3> path = new List<Vector3>(points.Length * 9) { points[0] };
        for (int i = 1; i < points.Length - 1; i++)
        {
            Vector3 previous = points[i - 1];
            Vector3 corner = points[i];
            Vector3 next = points[i + 1];
            Vector3 inbound = corner - previous;
            Vector3 outbound = next - corner;
            float inLength = inbound.magnitude;
            float outLength = outbound.magnitude;
            if (inLength < 0.001f || outLength < 0.001f)
            {
                continue;
            }
            inbound /= inLength;
            outbound /= outLength;
            if (Vector3.Dot(inbound, outbound) > 0.996f)
            {
                path.Add(corner);
                continue;
            }
            float fillet = Mathf.Min(maxFillet, inLength * 0.5f, outLength * 0.5f);
            Vector3 filletStart = corner - inbound * fillet;
            Vector3 filletEnd = corner + outbound * fillet;
            for (int k = 0; k <= 8; k++)
            {
                float t = k / 8f;
                float u = 1f - t;
                path.Add(u * u * filletStart + 2f * u * t * corner + t * t * filletEnd);
            }
        }
        path.Add(points[points.Length - 1]);
        return path;
    }

    private static float ProjectOntoPath(
        List<Vector3> path, float[] lengths, Vector3 position, float progress)
    {
        float bestDistance = float.PositiveInfinity;
        float bestProgress = progress;
        for (int i = 1; i < path.Count; i++)
        {
            if (lengths[i] < progress - 0.5f || lengths[i - 1] > progress + 15f)
            {
                continue;
            }
            Vector3 a = path[i - 1];
            Vector3 segment = Vector3.ProjectOnPlane(path[i] - a, Vector3.up);
            float length = segment.magnitude;
            float t = length < 0.0001f
                ? 0f
                : Mathf.Clamp01(Vector3.Dot(
                    Vector3.ProjectOnPlane(position - a, Vector3.up), segment) / (length * length));
            float distance = Vector3.ProjectOnPlane(
                a + segment * t - position, Vector3.up).sqrMagnitude;
            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestProgress = lengths[i - 1] + (lengths[i] - lengths[i - 1]) * t;
            }
        }
        return Mathf.Max(progress, bestProgress);
    }

    // Point at arc length s; beyond the end it extends the final leg so the
    // lookahead keeps the nose aligned while the bus settles.
    private static Vector3 SamplePath(List<Vector3> path, float[] lengths, float s)
    {
        int last = path.Count - 1;
        if (s >= lengths[last])
        {
            Vector3 direction = (path[last] - path[last - 1]).normalized;
            return path[last] + direction * (s - lengths[last]);
        }
        for (int i = 1; i <= last; i++)
        {
            if (lengths[i] >= s)
            {
                float span = Mathf.Max(0.0001f, lengths[i] - lengths[i - 1]);
                return Vector3.Lerp(path[i - 1], path[i], (s - lengths[i - 1]) / span);
            }
        }
        return path[last];
    }

    private bool IsDavieBusTurnaroundClear()
    {
        if (!UsesRoadsideBusRoute)
        {
            return true;
        }

        Vector3 center = GymRoadsideBusStop.DavieBusTurnaroundCenterPoint;
        float halfX = GymRoadsideBusStop.DavieBusTurnaroundRadiusX +
            DavieBusTargetLength * 0.5f + 0.75f;
        float halfZ = GymRoadsideBusStop.DavieBusTurnaroundRadiusZ +
            1.45f + 0.75f;
        for (int i = activeGroundTraffic.Count - 1; i >= 0; i--)
        {
            GymVisitorVehicle other = activeGroundTraffic[i];
            if (other == null)
            {
                activeGroundTraffic.RemoveAt(i);
                continue;
            }
            if (other == this || !other.IsDriving)
            {
                continue;
            }
            Vector3 relative = other.transform.position - center;
            if (Mathf.Abs(relative.x) <= halfX &&
                Mathf.Abs(relative.z) <= halfZ)
            {
                return false;
            }
        }

        Vector3 overlapCenter = center + Vector3.up * 1.0f;
        int overlapCount = Physics.OverlapBoxNonAlloc(
            overlapCenter,
            new Vector3(halfX, 1.35f, halfZ),
            nearPeople,
            Quaternion.identity,
            ~0,
            QueryTriggerInteraction.Ignore);
        for (int i = 0; i < overlapCount; i++)
        {
            Collider hit = nearPeople[i];
            if (hit == null || hit.transform.IsChildOf(transform))
            {
                continue;
            }
            GymVisitorVehicle otherVehicle =
                hit.GetComponentInParent<GymVisitorVehicle>();
            if (otherVehicle != null && otherVehicle != this)
            {
                return false;
            }
            if (IsPerson(hit))
            {
                return false;
            }
        }
        return true;
    }

    private IEnumerator WaitForRuntimeVisualThenDriveIn(Action onParked)
    {
        float deadline = Time.time + 15f;
        while (!runtimeVisualReady && !runtimeVisualLoadFailed &&
            Time.time < deadline)
        {
            yield return null;
        }

        if (!runtimeVisualReady)
        {
            Debug.LogError(
                IsBus
                    ? $"GYMCHAOS_DAVIE_BUS_ROUTE_WITHOUT_VISUAL path={DavieBusAsset}"
                    : $"GYMCHAOS_ARNOLD_HUMMER_ROUTE_WITHOUT_VISUAL path={ArnoldHummerAsset}",
                this);
            if (IsBus)
            {
                GymRoadsideBusStop.SetDavieDynamicBusActive(false);
            }
        }

        waitingForRuntimeVisual = false;
        driveRoutine = null;
        BeginDriveIn(onParked);
    }

    private IEnumerator WaitForRuntimeVisualThenDriveOut(Action onGone)
    {
        float deadline = Time.time + 15f;
        while (!runtimeVisualReady && !runtimeVisualLoadFailed &&
            Time.time < deadline)
        {
            yield return null;
        }

        waitingForRuntimeVisual = false;
        driveRoutine = null;
        BeginDriveOut(onGone);
    }

    private void FinalizeRuntimeBus(GameObject visual)
    {
        if (visual == null)
        {
            runtimeVisualLoadFailed = true;
            GymRoadsideBusStop.SetDavieDynamicBusActive(false);
            Debug.LogError(
                $"GYMCHAOS_VEHICLE_MODEL_MISSING identity={identity} " +
                $"path={DavieBusAsset}", this);
            return;
        }

        visual.name = identity + " Vehicle Model";
        AddPhysicalBody(visual);
        runtimeVisualReady = true;
        Debug.Log(
            $"GYMCHAOS_DAVIE_VISITOR_BUS_READY path={DavieBusAsset} " +
            $"length={DavieBusTargetLength:F2} roadside=1 " +
            $"passengerPoint={PassengerPoint}",
            this);
    }

    private void FinalizeRuntimeArnold(GameObject visual)
    {
        if (visual == null)
        {
            runtimeVisualLoadFailed = true;
            Debug.LogError(
                $"GYMCHAOS_VEHICLE_MODEL_MISSING identity={identity} " +
                $"path={ArnoldHummerAsset}", this);
            return;
        }

        visual.name = identity + " Hummer Vehicle Model";
        AlignArnoldVisualAxis(visual);
        FitVisual(visual, ArnoldHummerTargetLength, centerHorizontally: true);
        AddPhysicalBody(visual);
        runtimeVisualReady = true;


        Debug.Log(
            $"GYMCHAOS_ARNOLD_HUMMER_READY path={ArnoldHummerAsset} " +
            $"length={ArnoldHummerTargetLength:F2} centered=1 verticalAxis=1",
            this);
    }

    private static void AlignArnoldVisualAxis(GameObject visual)
    {
        if (visual == null)
        {
            return;
        }

        // The authored GLB's long axis is local X and its nose points toward
        // local +X. Generated parking stalls use local Z as their depth/
        // forward axis, so local -90 degrees maps the nose to root +Z (or
        // root -Z for the opposite row). Rotate only the GLB content child;
        // route/root orientation remains unchanged.
        Transform content = visual.transform.Find("City GLB Content");
        if (content != null)
        {
            content.localRotation = Quaternion.Euler(0f, -90f, 0f);
        }
    }

    public void MountRider(EnemyFighter fighter)
    {
        if (!IsCloud || fighter == null || riderAnchor == null) return;
        mountedRider = fighter;
        mountedRider.BeginVisitorVehicleRide(riderAnchor);
    }

    public void DismountRider(Vector3 groundPoint, Quaternion rotation)
    {
        if (mountedRider == null) return;
        EnemyFighter rider = mountedRider;
        mountedRider = null;
        rider.EndVisitorVehicleRide(groundPoint, rotation);
    }

    private IEnumerator DriveCloudRoute(bool park, Action done)
    {
        SetEngineDriving(true);
        Vector3 start = transform.position;
        Vector3 end = park ? parkingPoint : roadPoint;
        Vector3 controlA = park ? Vector3.Lerp(start, end, 0.36f) + Vector3.up * 5f
            : start + new Vector3(10f, 15f, 7f);
        Vector3 controlB = park ? end + new Vector3(7f, 13f, 5f)
            : Vector3.Lerp(start, end, 0.72f) + Vector3.up * 7f;
        float lengthEstimate = Vector3.Distance(start, controlA) +
            Vector3.Distance(controlA, controlB) + Vector3.Distance(controlB, end);
        float duration = Mathf.Max(1.15f, lengthEstimate / CloudSpeed);
        float elapsed = 0f;
        Vector3 previous = start;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float eased = t * t * (3f - 2f * t);
            Vector3 position = CubicBezier(start, controlA, controlB, end, eased);
            Vector3 planarDirection = Vector3.ProjectOnPlane(position - previous, Vector3.up);
            if (planarDirection.sqrMagnitude > 0.001f)
                transform.rotation = Quaternion.Slerp(transform.rotation,
                    Quaternion.LookRotation(planarDirection.normalized, Vector3.up),
                    TurnSpeed * Time.deltaTime);
            transform.position = position;
            previous = position;
            UpdateEngineAudibility();
            yield return null;
        }
        transform.position = end;
        if (park) transform.rotation = ParkedRotation;
        SetEngineDriving(false);
        IsParked = park;
        driveRoutine = null;
        if (!park) HasCompletedDeparture = true;
        done?.Invoke();
        if (!park) gameObject.SetActive(false);
        Debug.Log($"GYMCHAOS_VEHICLE_{(park ? "PARKED" : "DEPARTED")} identity={identity}", this);
    }

    private static Vector3 CubicBezier(Vector3 a, Vector3 b, Vector3 c, Vector3 d, float t)
    {
        float inverse = 1f - t;
        return inverse * inverse * inverse * a + 3f * inverse * inverse * t * b +
            3f * inverse * t * t * c + t * t * t * d;
    }

    private bool drivingIntoParking;

    public bool IsPedestrianClearForYield(Vector3 pedestrianPosition)
    {
        if (!IsDriving || !gameObject.activeInHierarchy)
        {
            return true;
        }

        Vector3 direction = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
        if (direction.sqrMagnitude < 0.01f)
        {
            return true;
        }

        direction.Normalize();
        Vector3 right = Vector3.Cross(Vector3.up, direction).normalized;
        Vector3 relative = Vector3.ProjectOnPlane(
            pedestrianPosition - transform.position, Vector3.up);
        float ahead = Vector3.Dot(relative, direction);
        float lateral = Mathf.Abs(Vector3.Dot(relative, right));
        return ahead < -1.6f || lateral > ForwardSensorHalfWidth + 1.15f;
    }

    private int CountIncomingTraffic()
    {
        int count = 0;
        for (int i = activeGroundTraffic.Count - 1; i >= 0; i--)
        {
            GymVisitorVehicle other = activeGroundTraffic[i];
            if (other == null)
            {
                activeGroundTraffic.RemoveAt(i);
            }
            else if (other != this && other.IsDriving && other.drivingIntoParking)
            {
                count++;
            }
        }
        return count;
    }

    private int CountOutgoingTraffic()
    {
        int count = 0;
        for (int i = activeGroundTraffic.Count - 1; i >= 0; i--)
        {
            GymVisitorVehicle other = activeGroundTraffic[i];
            if (other == null)
            {
                activeGroundTraffic.RemoveAt(i);
            }
            else if (other != this && other.IsDriving && !other.drivingIntoParking)
            {
                count++;
            }
        }
        return count;
    }

    private IEnumerator DriveRoute(
        Vector3[] points, bool park, Action done, float releaseDelay = 0f,
        bool preserveWaypoints = false)
    {
        if (!IsCloud && !activeGroundTraffic.Contains(this))
            activeGroundTraffic.Add(this);
        if (releaseDelay > 0f)
        {
            currentDriveSpeed = 0f;
            SetEngineDriving(false);
            yield return new WaitForSeconds(releaseDelay);
        }
        SetEngineDriving(true);
        float speed = IsCloud ? CloudSpeed : DriveSpeed;
        currentDriveSpeed = 0f;
        bool followBusPath = IsBus && UsesRoadsideBusRoute && !IsCloud;
        if (followBusPath)
        {
            yield return FollowBusPath(points, park, speed);
        }
        for (int i = 0; !followBusPath && i < points.Length; i++)
        {
            Vector3 target = points[i];
#if UNITY_EDITOR
            currentRouteWaypointIndex = i;
            currentRouteTarget = target;
#endif
            bool finalPoint = i == points.Length - 1;
            // The bay-entry leg translates the long bus sideways into the
            // pull-off. Keep its authored left-facing heading while it
            // crosses the gate so the 10.4 m body does not swing its nose
            // into the outer fence; the following parking leg remains
            // aligned with the same heading.
            bool holdBusBayHeading = IsBus && park &&
                UsesRoadsideBusRoute && i == points.Length - 2;
            if (holdBusBayHeading)
            {
                transform.rotation = GymRoadsideBusStop.DavieBusRotation;
            }
            // A departure endpoint is a road-edge handoff, not a parking precision point. Once the vehicle body reaches this safe margin, the remaining collider length is already beyond the visible route.
            float reachRadius = finalPoint ? (!park ? 1.25f : 0.2f) :
                preserveWaypoints ? 0.35f : 0.9f;
            while ((transform.position - target).sqrMagnitude > reachRadius * reachRadius)
            {
                int routeStepCount = IsCloud
                    ? 1
                    : Mathf.Max(1, Mathf.CeilToInt(Time.deltaTime / MaxGroundRouteDeltaTime));
                float routeDeltaTime = IsCloud
                    ? Time.deltaTime
                    : Time.deltaTime / routeStepCount;
                float clearance = float.PositiveInfinity;
                float actualTravel = 0f;
                float maxSubstepTravel = 0f;
                for (int routeStep = 0; routeStep < routeStepCount; routeStep++)
                {
                    if ((transform.position - target).sqrMagnitude <= reachRadius * reachRadius)
                    {
                        break;
                    }
                    float substepTravel = AdvanceRouteStep(
                        target, finalPoint, reachRadius, speed,
                        holdBusBayHeading, routeDeltaTime, out clearance);
                    actualTravel += substepTravel;
                    maxSubstepTravel = Mathf.Max(maxSubstepTravel, substepTravel);
                }
                float remaining = Vector3.Distance(transform.position, target);
                float travel = actualTravel;
                Vector3 direction = Vector3.ProjectOnPlane(target - transform.position, Vector3.up).normalized;
                bool pedestrianAhead = IsYieldingToPedestrian;
#if UNITY_EDITOR
                currentRouteRemaining = Vector3.Distance(transform.position, target);
                currentRouteClearance = clearance;
                currentRouteActualTravel = actualTravel;
                currentRouteClearanceSource = lastTrafficClearanceSource;
                if (!IsCloud && Time.realtimeSinceStartup >= nextRouteProgressLogTime)
                {
                    Debug.Log(
                        $"GYMCHAOS_VEHICLE_ROUTE_SAMPLE identity={identity} " +
                        $"waypoint={i}/{points.Length - 1} park={park} " +
                        $"position={transform.position} target={target} " +
                        $"remaining={currentRouteRemaining:F3} " +
                        $"travel={actualTravel:F4} maxSubstep={maxSubstepTravel:F4} speed={currentDriveSpeed:F2} " +
                        $"dt={Time.deltaTime:F4} substepDt={routeDeltaTime:F4} substeps={routeStepCount} timeScale={Time.timeScale:F2} " +
                        $"clearance={clearance:F3} " +
                        $"source={lastTrafficClearanceSource} " +
                        $"blocker={lastTrafficBlocker}", this);
                    nextRouteProgressLogTime = Time.realtimeSinceStartup + 2.5f;
                }
#endif
                if (!IsCloud && Time.time >= nextRouteStallLogTime &&
                    (actualTravel <= 0.0001f ||
                     (clearance <= 0.001f && remaining > reachRadius)))
                {
                    Debug.Log(
                        $"GYMCHAOS_VEHICLE_ROUTE_STALL identity={identity} " +
                        $"waypoint={i} park={park} position={transform.position} " +
                        $"target={target} remaining={remaining:F2} clearance={clearance:F3} " +
                        $"travel={travel:F4} actualTravel={actualTravel:F4} " +
                        $"speed={currentDriveSpeed:F2} blocker={lastTrafficBlocker} " +
                        $"source={lastTrafficClearanceSource} " +
                        $"pedestrianAhead={pedestrianAhead} direction={direction}", this);
                    nextRouteStallLogTime = Time.time + 2.5f;
                }
                if (engine != null) engine.pitch = Mathf.Lerp(0.72f, 1.15f, currentDriveSpeed / speed);
                UpdateEngineAudibility();
                yield return null;
            }
            if (finalPoint) transform.position = target;
        }
        if (park && IsBus)
        {
            transform.rotation = ParkedRotation;
        }
        SetEngineDriving(false);
        activeGroundTraffic.Remove(this);
        IsYieldingToPedestrian = false;
        IsParked = park;
        driveRoutine = null;
        if (!park) HasCompletedDeparture = true;
        if (!park)
        {
            if (IsBus)
            {
                // Keep both preview and dynamic bus hidden behind the corner.
                // The preview must not reappear at the stop until the next
                // pickup cycle explicitly drives a bus back in.
                GymRoadsideBusStop.SetDavieDynamicBusActive(true);
            }
            gameObject.SetActive(false);
        }
        done?.Invoke();
        Debug.Log($"GYMCHAOS_VEHICLE_{(park ? "PARKED" : "DEPARTED")} identity={identity}", this);
    }

    private bool IsPerson(Collider collider)
    {
        return collider != null && !collider.transform.IsChildOf(transform) &&
            (collider.GetComponentInParent<PlayerMovement>() != null ||
             collider.GetComponentInParent<EnemyFighter>() != null ||
             collider.GetComponentInParent<GymVisitorAgent>() != null);
    }

    private float TrafficClearance(
        Vector3 direction, float waypointDistance, out bool pedestrianAhead)
    {
        pedestrianAhead = false;
        lastTrafficBlocker = "none";
        lastTrafficClearanceSource = "none";
        float convoyClearance = SameDirectionConvoyClearance(
            direction, out string convoyBlocker);
        if (convoyClearance <= 0.01f)
        {
            lastTrafficBlocker = convoyBlocker;
            lastTrafficClearanceSource = "convoy:" + convoyBlocker;
            return 0f;
        }
        BoxCollider bodyCollider = GetComponent<BoxCollider>();
        float frontExtent = 1.7f;
        if (bodyCollider != null)
        {
            frontExtent = GetColliderExtentAlong(bodyCollider, direction);
        }
        // Start sensing just beyond the physical nose. A box centred on the
        // vehicle also covered neighbouring parking bays during a turn and
        // incorrectly treated a side-by-side parked car as one in this lane.
        Vector3 right = Vector3.Cross(Vector3.up, direction).normalized;
        Vector3 bumperCenter = transform.position +
            direction * (frontExtent * 0.55f) + Vector3.up * 0.9f;
        Vector3 bumperHalf = new Vector3(
            ForwardSensorHalfWidth, 0.78f, frontExtent * 0.55f + 0.16f);
        Quaternion orientation = Quaternion.LookRotation(direction, Vector3.up);
        int bumperCount = Physics.OverlapBoxNonAlloc(
            bumperCenter, bumperHalf, nearPeople, orientation,
            ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < bumperCount; i++)
        {
            Collider hit = nearPeople[i];
            if (IsAdjacentBusBayStop(hit)) continue;
            if (!IsTrafficObstacle(hit) ||
                ShouldIgnoreStaticObstacle(
                    hit, direction, waypointDistance, frontExtent)) continue;
            Vector3 relative = hit.bounds.center - transform.position;
            float ahead = Vector3.Dot(relative, direction);
            float lateral = Mathf.Abs(Vector3.Dot(relative, right));
            Vector3 hitExtents = hit.bounds.extents;
            float obstacleHalfWidth = Mathf.Abs(right.x) * hitExtents.x +
                Mathf.Abs(right.z) * hitExtents.z;
            if (ahead <= 0.08f ||
                lateral > ForwardSensorHalfWidth + obstacleHalfWidth * 0.65f)
                continue;
            pedestrianAhead |= IsPerson(hit);
            RequestPedestrianYield(hit, direction);
            lastTrafficBlocker = hit.name;
            lastTrafficClearanceSource = "bumper:" + hit.name;
            return 0f;
        }

        Vector3 center = transform.position + direction * (frontExtent + 0.18f) +
            Vector3.up * 0.9f;
        Vector3 half = new Vector3(ForwardSensorHalfWidth, 0.78f, 0.42f);
        int overlapCount = Physics.OverlapBoxNonAlloc(center, half, nearPeople,
            orientation, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < overlapCount; i++)
        {
            Collider hit = nearPeople[i];
            if (IsAdjacentBusBayStop(hit)) continue;
            if (!IsTrafficObstacle(hit) ||
                ShouldIgnoreStaticObstacle(
                    hit, direction, waypointDistance, frontExtent)) continue;
            pedestrianAhead |= IsPerson(hit);
            RequestPedestrianYield(hit, direction);
            lastTrafficBlocker = hit.name;
            lastTrafficClearanceSource = "overlap:" + hit.name;
            return 0f;
        }
        float distance = Mathf.Lerp(ForwardSensorMinimumDistance, ForwardSensorMaximumDistance,
            Mathf.Clamp01(currentDriveSpeed / DriveSpeed));
        int count = Physics.BoxCastNonAlloc(center, half, direction, roadHits,
            orientation, distance, ~0, QueryTriggerInteraction.Ignore);
        float clearance = convoyClearance;
        for (int i = 0; i < count; i++)
        {
            Collider hit = roadHits[i].collider;
            if (IsAdjacentBusBayStop(hit)) continue;
            if (!IsTrafficObstacle(hit) ||
                ShouldIgnoreStaticObstacle(
                    hit, direction, waypointDistance, frontExtent)) continue;
            pedestrianAhead |= IsPerson(hit);
            bool pedestrianYielded = RequestPedestrianYield(hit, direction);
            if (pedestrianYielded)
            {
                clearance = 0f;
                lastTrafficBlocker = hit.name;
                lastTrafficClearanceSource = "boxcast-pedestrian:" + hit.name;
                continue;
            }
            if (roadHits[i].distance < clearance)
            {
                clearance = roadHits[i].distance;
                lastTrafficBlocker = hit.name;
                lastTrafficClearanceSource = "boxcast:" + hit.name;
            }
        }
        float crossingClearance = PredictCrossingPedestrianClearance(direction);
        if (crossingClearance < clearance)
        {
            pedestrianAhead = true;
            clearance = crossingClearance;
            lastTrafficBlocker = "crossing pedestrian";
            lastTrafficClearanceSource = "crossing pedestrian";
        }
        if (lastTrafficClearanceSource == "none")
        {
            lastTrafficClearanceSource = "clear";
        }
        return clearance;
    }

    private bool ShouldIgnoreStaticObstacle(
        Collider collider, Vector3 direction, float waypointDistance,
        float vehicleFrontExtent)
    {
        if (collider == null ||
            IsPerson(collider))
        {
            return false;
        }
        if (collider.GetComponentInParent<GymVisitorVehicle>() != null)
        {
            return true;
        }
        // The bus's smoothed route is laid out clear of walls and fences;
        // while the nose sweeps through a curve it must not brake for a fence
        // the path turns away from.
        if (followingBusPath)
        {
            return true;
        }

        Vector3 relative = Vector3.ProjectOnPlane(
            collider.bounds.center - transform.position, Vector3.up);
        float ahead = Vector3.Dot(relative, direction);
        float obstacleHalfLength = Mathf.Abs(direction.x) *
                collider.bounds.extents.x +
            Mathf.Abs(direction.z) * collider.bounds.extents.z;
        float vehicleCollisionDistance = ahead -
            obstacleHalfLength - vehicleFrontExtent;
        Vector3 right = Vector3.Cross(Vector3.up, direction).normalized;
        BoxCollider bodyCollider = GetComponent<BoxCollider>();
        float vehicleHalfWidth = bodyCollider != null
            ? GetColliderExtentAlong(bodyCollider, right)
            : ForwardSensorHalfWidth;
        float obstacleHalfWidth = Mathf.Abs(right.x) *
                collider.bounds.extents.x +
            Mathf.Abs(right.z) * collider.bounds.extents.z;
        float lateral = Mathf.Abs(Vector3.Dot(relative, right));
        if (lateral > vehicleHalfWidth + obstacleHalfWidth + 0.08f ||
            ahead + obstacleHalfLength < -0.15f)
        {
            return true;
        }

        return waypointDistance < ForwardSensorMaximumDistance &&
            vehicleCollisionDistance > waypointDistance + 0.15f;
    }

    private float PredictCrossingPedestrianClearance(Vector3 forward)
    {
        Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
        int count = Physics.OverlapSphereNonAlloc(
            transform.position + forward * 1.7f + Vector3.up * 0.9f,
            2.25f, nearPeople, ~0, QueryTriggerInteraction.Ignore);
        float clearance = float.PositiveInfinity;
        for (int i = 0; i < count; i++)
        {
            Collider candidate = nearPeople[i];
            if (!IsPerson(candidate)) continue;
            Vector3 relative = candidate.bounds.center - transform.position;
            float ahead = Vector3.Dot(relative, forward);
            float lateral = Vector3.Dot(relative, right);
            if (ahead < 0.15f || ahead > 3.7f || Mathf.Abs(lateral) > 1.75f)
                continue;
            Rigidbody personBody = candidate.GetComponentInParent<Rigidbody>();
            float lateralSpeed = personBody != null
                ? Vector3.Dot(personBody.linearVelocity, right)
                : 0f;
            float predictedLateral = lateral + lateralSpeed * CrossingPredictionSeconds;
            if (Mathf.Abs(lateral) <= 1.05f || Mathf.Abs(predictedLateral) <= 1.05f)
            {
                bool pedestrianYielded = RequestPedestrianYield(candidate, forward);
                float candidateClearance = pedestrianYielded
                    ? 0f
                    : Mathf.Max(0f, ahead - 0.55f);
                clearance = Mathf.Min(clearance, candidateClearance);
            }
        }
        return clearance;
    }

    private bool RequestPedestrianYield(Collider hit, Vector3 direction)
    {
        if (hit == null || !IsPerson(hit))
        {
            return false;
        }

        GymVisitorAgent agent = hit.GetComponentInParent<GymVisitorAgent>();
        return agent != null && agent.RequestVehicleYield(this, direction);
    }

    private float SameDirectionConvoyClearance(
        Vector3 direction, out string blocker)
    {
        blocker = "none";
        float clearance = float.PositiveInfinity;
        direction = Vector3.ProjectOnPlane(direction, Vector3.up);
        if (direction.sqrMagnitude < 0.01f)
        {
            return clearance;
        }
        direction.Normalize();
        Vector3 right = Vector3.Cross(Vector3.up, direction).normalized;
        for (int i = activeGroundTraffic.Count - 1; i >= 0; i--)
        {
            GymVisitorVehicle other = activeGroundTraffic[i];
            if (other == null)
            {
                activeGroundTraffic.RemoveAt(i);
                continue;
            }
            if (other == this || !other.IsDriving ||
                other.drivingIntoParking != drivingIntoParking)
                continue;
            Vector3 relative = Vector3.ProjectOnPlane(
                other.transform.position - transform.position, Vector3.up);
            float ahead = Vector3.Dot(relative, direction);
            float lateral = Mathf.Abs(Vector3.Dot(relative, right));
            float otherHalfWidth = other.IsBus ? 1.45f : 1.05f;
            if (ahead <= 0.05f || lateral > ForwardSensorHalfWidth +
                otherHalfWidth * 0.8f)
            {
                continue;
            }
            float requiredGap = Mathf.Max(
                4.5f,
                GetForwardExtent(direction) +
                other.GetForwardExtent(direction) + 0.45f);
            float candidate = Mathf.Max(0f, ahead - requiredGap);
            if (candidate < clearance)
            {
                clearance = candidate;
                blocker = other.name;
            }
        }
        return clearance;
    }

    private float GetColliderExtentAlong(
        BoxCollider bodyCollider, Vector3 direction)
    {
        if (bodyCollider == null || direction.sqrMagnitude < 0.0001f)
        {
            return 0f;
        }

        direction.Normalize();
        Transform colliderTransform = bodyCollider.transform;
        Vector3 halfSize = new Vector3(
            bodyCollider.size.x * Mathf.Abs(colliderTransform.lossyScale.x) *
                0.5f,
            bodyCollider.size.y * Mathf.Abs(colliderTransform.lossyScale.y) *
                0.5f,
            bodyCollider.size.z * Mathf.Abs(colliderTransform.lossyScale.z) *
                0.5f);
        return Mathf.Abs(Vector3.Dot(direction, colliderTransform.right)) *
                halfSize.x +
            Mathf.Abs(Vector3.Dot(direction, colliderTransform.up)) *
                halfSize.y +
            Mathf.Abs(Vector3.Dot(direction, colliderTransform.forward)) *
                halfSize.z;
    }

    private float GetForwardExtent(Vector3 direction)
    {
        BoxCollider bodyCollider = GetComponent<BoxCollider>();
        if (bodyCollider == null)
        {
            return IsBus ? DavieBusTargetLength * 0.5f : 1.7f * S;
        }
        return GetColliderExtentAlong(bodyCollider, direction);
    }

    private bool IsAdjacentBusBayStop(Collider collider)
    {
        if (collider == null || !GymRoadsideBusStop.IsBuilt)
        {
            return false;
        }

        GymVisitorVehicle bus = collider.GetComponentInParent<GymVisitorVehicle>();
        bool parkedBus = bus != null && bus.IsBus && bus.IsParked;
        bool previewBus = false;
        for (Transform current = collider.transform; current != null;
            current = current.parent)
        {
            if (current.name == "Davie Bus - Temporary Roadside Stop")
            {
                previewBus = true;
                break;
            }
        }
        if (!parkedBus && !previewBus)
        {
            return false;
        }

        Bounds busBounds = collider.bounds;
        if (busBounds.center.x < GymRoadsideBusStop.BusBayStartX - 0.75f ||
            busBounds.center.x > GymRoadsideBusStop.BusBayEndX + 0.75f ||
            busBounds.center.z < GymRoadsideBusStop.BusBayRoadEdgeZ)
        {
            return false;
        }

        // The bus is parked beyond the normal road edge. A car still on the
        // normal lane should not brake for the adjacent pull-off; once a
        // vehicle is actually in the bay envelope, normal obstacle handling
        // applies again.
        return transform.position.z <= GymRoadsideBusStop.BusBayRoadEdgeZ + 0.65f;
    }

    private bool IsTrafficObstacle(Collider collider)
    {
        if (collider == null || collider.isTrigger ||
            collider.transform.IsChildOf(transform))
        {
            return false;
        }
        if (IsPerson(collider) ||
            collider.GetComponentInParent<GymVisitorVehicle>() != null)
        {
            return true;
        }

        // Ground, paint and the correctly placed wheel stop are route
        // surfaces. Anything rising into the vehicle body (walls, foliage,
        // lamps, props) remains a real blocker.
        if (collider.bounds.max.y <= transform.position.y + 0.38f)
        {
            return false;
        }
        string lowerName = collider.name.ToLowerInvariant();
        if (lowerName == "outdoor boundary - path outer" ||
            lowerName == "player road access blocker")
        {
            return false;
        }
        return !lowerName.Contains("vehicle road") &&
            !lowerName.Contains("parking lot") &&
            !lowerName.Contains("parking aisle") &&
            !lowerName.Contains("parking line") &&
            !lowerName.Contains("road center line") &&
            !lowerName.Contains("road shoulder") &&
            !lowerName.Contains("courtyard foundation") &&
            !lowerName.Contains("wheel stop");
    }

    private void UpdateHorn(bool blocked)
    {
        if (!blocked)
        {
            blockedSince = -1f;
            return;
        }

        if (!IsPlayerOutsideForAudio())
        {
            blockedSince = -1f;
            UpdateVehicleAudioAudibility();
            return;
        }

        if (blockedSince < 0f) blockedSince = Time.time;
        if (Time.time - blockedSince < 0.35f || Time.time < nextHornTime) return;
        nextHornTime = Time.time + 2.5f;
        if (horn == null)
        {
            horn = gameObject.AddComponent<AudioSource>();
            horn.playOnAwake = false; horn.spatialBlend = 1f;
            horn.rolloffMode = AudioRolloffMode.Logarithmic;
            horn.minDistance = 3f; horn.maxDistance = 35f; horn.volume = 0.3f;
            horn.mute = true;
            const int rate = 22050;
            float[] samples = new float[(int)(rate * 0.32f)];
            for (int i = 0; i < samples.Length; i++)
            {
                float t = i / (float)rate;
                float envelope = Mathf.Min(t * 80f, 1f) * Mathf.Clamp01((0.32f - t) * 35f);
                samples[i] = envelope * (Mathf.Sin(t * 440f * 2f * Mathf.PI) +
                    Mathf.Sin(t * 554f * 2f * Mathf.PI)) * 0.35f;
            }
            hornClip = AudioClip.Create("Vehicle warning horn", samples.Length, 1, rate, false);
            hornClip.SetData(samples, 0);
        }
        hornPlayCount++;
        horn.mute = false;
        horn.PlayOneShot(hornClip);
    }

    public static bool IsPedestrianUsingParkingConnector()
    {
        IReadOnlyList<GymVisitorAgent> agents = GymVisitorAgent.ActiveAgents;
        for (int i = 0; i < agents.Count; i++)
        {
            if (agents[i] != null && agents[i].State ==
                GymVisitorAgent.VisitorState.ApproachingVehicle)
            {
                return true;
            }
        }
        return false;
    }

    private void OnDestroy()
    {
        if (hornClip != null) Destroy(hornClip);
    }

    private void OnDisable()
    {
        followingBusPath = false;
        activeGroundTraffic.Remove(this);
    }

    private void Update()
    {
        if (engine != null || horn != null) UpdateVehicleAudioAudibility();
    }

    private float AdvanceRouteStep(
        Vector3 target, bool finalPoint, float reachRadius, float speed,
        bool holdBusBayHeading, float routeDeltaTime, out float clearance)
    {
        Vector3 direction = target - transform.position;
        if (!IsCloud)
        {
            direction = Vector3.ProjectOnPlane(direction, Vector3.up);
        }
        direction.Normalize();
        float remaining = Vector3.Distance(transform.position, target);
        if (direction.sqrMagnitude > 0.01f && !holdBusBayHeading)
        {
            transform.rotation = Quaternion.Slerp(
                transform.rotation,
                Quaternion.LookRotation(direction, Vector3.up),
                TurnSpeed * routeDeltaTime);
        }
        else if (holdBusBayHeading)
        {
            transform.rotation = GymRoadsideBusStop.DavieBusRotation;
        }

        bool pedestrianAhead = false;
        clearance = IsCloud
            ? float.PositiveInfinity
            : TrafficClearance(direction, remaining, out pedestrianAhead);
        float routeSpeed = finalPoint
            ? Mathf.Sqrt(2f * 5f * Mathf.Max(0f, remaining - 0.1f))
            : Mathf.Lerp(4.2f, speed, Mathf.InverseLerp(reachRadius, 5f, remaining));
        float targetSpeed = IsCloud ? speed : Mathf.Min(speed, routeSpeed);
        if (!IsCloud)
        {
            targetSpeed = Mathf.Min(
                targetSpeed,
                Mathf.Sqrt(2f * 7f * Mathf.Max(0f, clearance - 0.5f)));
        }
        currentDriveSpeed = Mathf.MoveTowards(
            currentDriveSpeed, targetSpeed,
            (targetSpeed < currentDriveSpeed ? 10f : 6.5f) * routeDeltaTime);
        IsYieldingToPedestrian = !IsCloud && pedestrianAhead && clearance < 3f;
        UpdateHorn(IsYieldingToPedestrian);

        float travel = Mathf.Min(
            currentDriveSpeed * routeDeltaTime,
            Mathf.Max(0f, clearance - 0.45f),
            IsCloud ? float.PositiveInfinity : MaxGroundRouteStep);
        Vector3 beforeMove = transform.position;
        transform.position = Vector3.MoveTowards(transform.position, target, travel);
        return Vector3.Distance(beforeMove, transform.position);
    }
    private void UpdateEngineAudibility()
    {
        UpdateVehicleAudioAudibility();
    }

    private bool IsPlayerOutsideForAudio()
    {
        if (player == null)
        {
            player = FindAnyObjectByType<PlayerMovement>();
        }

        return player != null &&
            GymOutdoorBuilder.IsPlayerOutsideGym(player.transform.position);
    }

    private void UpdateVehicleAudioAudibility()
    {
        bool outside = IsPlayerOutsideForAudio();
        bool audibleDrivingState = IsDriving && !IsParked &&
            !waitingForBusTurnaround && gameObject.activeInHierarchy;
        if (engine != null)
        {
            RefreshEngineClip(engine, IsCloud);
            // Inside the gym the loop is muted; driving state fades it in
            // and out (start/leave bay, park, despawn).
            engine.mute = !outside;
            if (engineFader != null)
            {
                engineFader.SetDriving(audibleDrivingState);
            }
        }

        if (horn != null)
        {
            horn.mute = !outside;
            if (!outside && horn.isPlaying)
            {
                horn.Stop();
            }
        }
    }

    private void ConfigureRoute(int slot)
    {
        Bounds parking = GymOutdoorBuilder.ParkingBounds;
        float floorY = parking.center.y + RouteHeightAboveFloor;
        if (IsBus && GymRoadsideBusStop.IsBuilt)
        {
            ConfigureDavieBusRoute(floorY);
            return;
        }

        int normalizedSlot = Mathf.Abs(slot) % 6;
        int bayCount = Mathf.Max(1, GymOutdoorBuilder.ParkingBayCount);
        // Spread the six lifecycle slots across the generated bays while
        // using the exact same center calculation as the painted lines.
        int column = Mathf.Clamp(
            Mathf.FloorToInt((normalizedSlot + 0.5f) * bayCount / 6f),
            0, bayCount - 1);
        float x = GymOutdoorBuilder.GetParkingBayCenterX(column);
        float rowSign = normalizedSlot % 2 == 0 ? -1f : 1f;
        parkingPoint = new Vector3(
            x, floorY, GymOutdoorBuilder.GetParkingStallCenterZ(rowSign));
        aislePoint = new Vector3(x, floorY, parking.center.z);
        junctionPoint = GymOutdoorBuilder.VehicleRoadJunctionPoint;
        junctionPoint.y = floorY;
        roadTurnPoint = GymOutdoorBuilder.VehicleRoadTurnPoint;
        roadTurnPoint.y = floorY;
        roadPoint = GymOutdoorBuilder.VehicleRoadSpawnPoint;
        if (IsCloud)
        {
            float side = parkingPoint.z >= parking.center.z ? 1f : -1f;
            roadPoint = parkingPoint + new Vector3(72f, 34f, side * 58f);
        }
        else
        {
            junctionPoint = GymOutdoorBuilder.VehicleArrivalRoadJunctionPoint;
            roadTurnPoint = GymOutdoorBuilder.VehicleArrivalRoadTurnPoint;
            roadPoint = GymOutdoorBuilder.VehicleArrivalRoadSpawnPoint;
            departureJunctionPoint = GymOutdoorBuilder.VehicleDepartureRoadJunctionPoint;
            departureRoadTurnPoint = GymOutdoorBuilder.VehicleDepartureRoadTurnPoint;
            departureRoadPoint = GymOutdoorBuilder.VehicleDepartureRoadSpawnPoint;
            roadPoint.y = floorY;
            departureJunctionPoint.y = floorY;
            departureRoadTurnPoint.y = floorY;
            departureRoadPoint.y = floorY;
        }
    }

    private void ConfigureDavieBusRoute(float fallbackFloorY)
    {
        float busFloorY = GymRoadsideBusStop.DavieBusCenterPoint.y;
        if (Mathf.Abs(busFloorY) < 0.001f)
        {
            busFloorY = fallbackFloorY;
        }

        parkingPoint = GymRoadsideBusStop.DavieBusCenterPoint;
        parkingPoint.y = busFloorY;
        aislePoint = GymRoadsideBusStop.DavieBusPedestrianExitPoint;
        aislePoint.y = busFloorY;
        junctionPoint = GymRoadsideBusStop.DavieBusArrivalApproachPoint;
        junctionPoint.y = busFloorY;
        busBayEntryPoint = GymRoadsideBusStop.DavieBusBayEntryPoint;
        busBayEntryPoint.y = busFloorY;
        busBayParkingTurnPoint = GymRoadsideBusStop.DavieBusBayParkingTurnPoint;
        busBayParkingTurnPoint.y = busFloorY;
        roadTurnPoint = GymOutdoorBuilder.VehicleArrivalRoadTurnPoint;
        roadTurnPoint.y = busFloorY;
        roadPoint = GymOutdoorBuilder.VehicleArrivalRoadSpawnPoint;
        roadPoint.y = busFloorY;
        busDepartureApproachPoint =
            GymRoadsideBusStop.DavieBusTurnaroundStartPoint;
        busDepartureApproachPoint.y = busFloorY;
        departureJunctionPoint = GymOutdoorBuilder.VehicleDepartureRoadJunctionPoint;
        departureJunctionPoint.y = busFloorY;
        departureRoadTurnPoint = GymOutdoorBuilder.VehicleDepartureRoadTurnPoint;
        departureRoadTurnPoint.y = busFloorY;
        departureRoadPoint = GymOutdoorBuilder.VehicleDepartureRoadSpawnPoint;
        departureRoadPoint.y = busFloorY;
    }

    private Quaternion ParkedRotation => Quaternion.LookRotation(
        IsBus && GymRoadsideBusStop.IsBuilt
            ? GymRoadsideBusStop.DavieBusRotation * Vector3.forward
            : parkingPoint.z >= aislePoint.z ? Vector3.forward : Vector3.back,
        Vector3.up);

    private void FitVisual(
        GameObject visual, float targetLength, bool centerHorizontally = false)
    {
        Renderer[] renderers = visual.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0) return;
        Bounds bounds = CombinedBounds(renderers);
        float largestHorizontal = Mathf.Max(bounds.size.x, bounds.size.z);
        if (largestHorizontal > 0.001f)
        {
            visual.transform.localScale *= targetLength / largestHorizontal;
        }
        Physics.SyncTransforms();
        bounds = CombinedBounds(renderers);
        if (centerHorizontally)
        {
            Vector3 horizontalOffset = new Vector3(
                transform.position.x - bounds.center.x,
                0f,
                transform.position.z - bounds.center.z);
            visual.transform.position += horizontalOffset;
            Physics.SyncTransforms();
            bounds = CombinedBounds(renderers);
        }
        // Relative to the vehicle root: the old absolute y=0 target left the
        // body floating above the lowered asphalt.
        float groundY = transform.position.y - RouteHeightAboveFloor - TyreContactSink;
        visual.transform.position += Vector3.up * (groundY - bounds.min.y);
    }

    // The roadside bus route already runs on the asphalt top; every other
    // ground route is lifted by RouteHeightAboveFloor.
    private float VisualSupportY => (IsBus && UsesRoadsideBusRoute
        ? parkingPoint.y
        : parkingPoint.y - RouteHeightAboveFloor) - TyreContactSink;

    private void CreateRiderAnchor(GameObject visual)
    {
        Renderer[] renderers = visual.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0) return;
        Bounds bounds = CombinedBounds(renderers);
        riderAnchor = new GameObject("Goku Rider Anchor").transform;
        riderAnchor.SetParent(transform, true);
        riderAnchor.position = new Vector3(bounds.center.x, bounds.max.y + 0.04f, bounds.center.z);
        riderAnchor.rotation = transform.rotation;
    }

    private void ApplyOriginalMaterial(GameObject visual)
    {
        Texture texture = ScanTextureMips.Limit(Resources.Load<Texture2D>(GetTextureResource(identity)));
        if (texture == null)
        {
            Debug.LogError($"GYMCHAOS_VEHICLE_TEXTURE_MISSING identity={identity}", this);
            return;
        }
        Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        Renderer[] renderers = visual.GetComponentsInChildren<Renderer>(true);
        var replacements = new Dictionary<Material, Material>();
        for (int r = 0; r < renderers.Length; r++)
        {
            Material[] source = renderers[r].sharedMaterials;
            for (int i = 0; i < source.Length; i++)
            {
                Material original = source[i];
                if (!replacements.TryGetValue(original, out Material material))
                {
                    material = new Material(shader) { name = identity + " Original GLB Material" };
                    material.SetTexture("_BaseMap", texture);
                    material.SetTexture("_MainTex", texture);
                    material.SetColor("_BaseColor", Color.white);
                    material.color = Color.white;
                    material.SetFloat("_Smoothness", IsCloud ? 0.2f : 0.48f);
                    material.SetFloat("_Metallic", IsCloud ? 0f : 0.22f);
                    replacements[original] = material;
                }
                source[i] = material;
            }
            renderers[r].sharedMaterials = source;
            if (renderers[r].TryGetComponent(out MeshFilter filter) && filter.sharedMesh != null)
            {
                filter.sharedMesh = ScanUvInset.Apply(filter.sharedMesh, texture);
            }
        }
    }

    private void AddPhysicalBody(GameObject visual)
    {
        if (visual == null ||
            !TryGetOrientedLocalBounds(visual, transform, out Bounds localBounds))
        {
            return;
        }

        BoxCollider box = gameObject.AddComponent<BoxCollider>();
        box.center = localBounds.center;
        box.size = localBounds.size;
        Rigidbody rigidbody = gameObject.AddComponent<Rigidbody>();
        rigidbody.isKinematic = true;
        rigidbody.useGravity = false;
        rigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
    }

    private static bool TryGetOrientedLocalBounds(
        GameObject visual, Transform root, out Bounds bounds)
    {
        bounds = default;
        bool hasBounds = false;
        MeshFilter[] filters = visual.GetComponentsInChildren<MeshFilter>(true);
        for (int filterIndex = 0; filterIndex < filters.Length; filterIndex++)
        {
            MeshFilter filter = filters[filterIndex];
            if (filter == null || filter.sharedMesh == null)
            {
                continue;
            }

            Bounds source = filter.sharedMesh.bounds;
            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 sourcePoint = new Vector3(
                    (corner & 1) == 0 ? source.min.x : source.max.x,
                    (corner & 2) == 0 ? source.min.y : source.max.y,
                    (corner & 4) == 0 ? source.min.z : source.max.z);
                Vector3 localPoint = root.InverseTransformPoint(
                    filter.transform.TransformPoint(sourcePoint));
                if (!hasBounds)
                {
                    bounds = new Bounds(localPoint, Vector3.zero);
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(localPoint);
                }
            }
        }
        if (hasBounds)
        {
            return true;
        }

        Renderer[] renderers = visual.GetComponentsInChildren<Renderer>(true);
        for (int rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
        {
            Renderer renderer = renderers[rendererIndex];
            if (renderer == null) continue;
            Bounds source = renderer.bounds;
            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 worldPoint = new Vector3(
                    (corner & 1) == 0 ? source.min.x : source.max.x,
                    (corner & 2) == 0 ? source.min.y : source.max.y,
                    (corner & 4) == 0 ? source.min.z : source.max.z);
                Vector3 localPoint = root.InverseTransformPoint(worldPoint);
                if (!hasBounds)
                {
                    bounds = new Bounds(localPoint, Vector3.zero);
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(localPoint);
                }
            }
        }
        return hasBounds;
    }
    private static Bounds CombinedBounds(Renderer[] renderers)
    {
        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
        return bounds;
    }

    private void CreateEngineAudio()
    {
        engine = gameObject.AddComponent<AudioSource>();
        engine.mute = true;
        engine.loop = true;
        engine.playOnAwake = false;
        engine.spatialBlend = 1f;
        engine.minDistance = 3f;
        engine.maxDistance = IsCloud ? 36f : 30f;
        engine.rolloffMode = AudioRolloffMode.Logarithmic;
        engine.dopplerLevel = 0.35f;
        engine.clip = GetEngineLoopClip(IsCloud);
        engineFader = GymEngineAudioFader.Attach(engine, IsCloud ? 0.18f : 0.32f);
    }

    // Swaps the procedural fallback for the recorded loop when it finishes
    // loading after the source was created.
    public static void RefreshEngineClip(AudioSource source, bool cloud)
    {
        if (cloud || source == null || source.clip != carEngineLoopClip ||
            carEngineLoopClip == null)
        {
            return;
        }

        AudioClip clip = GetEngineLoopClip(false);
        if (clip == source.clip)
        {
            return;
        }

        bool wasPlaying = source.isPlaying;
        source.clip = clip;
        if (wasPlaying)
        {
            source.Play();
        }
    }

    // Start/stop the driving loop through its fader (fade in/out), never
    // by cutting the source.
    private void SetEngineDriving(bool driving)
    {
        if (engineFader != null)
        {
            engineFader.SetDriving(driving);
        }
        else if (engine != null)
        {
            if (driving) engine.Play();
            else engine.Stop();
        }
    }

    private static AudioClip carEngineLoopClip;
    private static bool loggedRecordedEngineLoop;
    private static AudioClip cloudEngineLoopClip;

    // Shared by visitor cars, the bus and the police car. Built once per
    // kind; every source plays the same in-memory loop.
    public static AudioClip GetEngineLoopClip(bool cloud)
    {
        if (!cloud)
        {
            // The recorded car loop replaces the procedural bed once loaded.
            AudioClip recorded = GymAudio.GetLoadedClip(GymSoundEffect.VehicleDriving);
            if (recorded != null)
            {
                recorded.name = "Car driving loop";
                if (!loggedRecordedEngineLoop)
                {
                    loggedRecordedEngineLoop = true;
                    Debug.Log(
                        $"GYMCHAOS_VEHICLE_RECORDED_ENGINE_LOOP_OK clip=car_driving.wav " +
                        $"length={recorded.length:F2} channels={recorded.channels}");
                }
                return recorded;
            }
        }

        AudioClip cached = cloud ? cloudEngineLoopClip : carEngineLoopClip;
        if (cached != null)
        {
            return cached;
        }
        const int sampleRate = 22050;
        const int sampleCount = sampleRate * 2;
        float[] samples = new float[sampleCount];
        for (int i = 0; i < sampleCount; i++)
        {
            float loop = i / (float)sampleCount;
            float phase = loop * Mathf.PI * 2f;
            if (cloud)
            {
                // A light, airy flight bed for the cloud. Every oscillator
                // completes an integer number of cycles, so the loop boundary
                // stays click-free without an imported asset.
                float wind = Mathf.Sin(phase * 5f) * 0.25f +
                    Mathf.Sin(phase * 11f + 0.7f) * 0.16f +
                    Mathf.Sin(phase * 23f + 1.9f) * 0.09f;
                float shimmer = Mathf.Sin(phase * 41f +
                    Mathf.Sin(phase * 2f) * 0.45f) * 0.06f;
                float swell = 0.72f + 0.28f * Mathf.Sin(phase * 2f - 0.8f);
                samples[i] = (wind + shimmer) * swell * 0.34f;
            }
            else
            {
                // Layered low-frequency engine harmonics plus road texture.
                // This is intentionally a looped effect, rather than a pure
                // sine tone, so parked cars never carry an artificial hum.
                float rumble = Mathf.Sin(phase * 2f) * 0.46f +
                    Mathf.Sin(phase * 4f + 0.25f) * 0.19f +
                    Mathf.Sin(phase * 7f + 1.1f) * 0.11f;
                float roadTexture = Mathf.Sin(phase * 29f + 0.4f) * 0.08f +
                    Mathf.Sin(phase * 47f + 2.3f) * 0.055f +
                    Mathf.Sin(phase * 71f + 0.9f) * 0.035f;
                float load = 0.84f + 0.16f * Mathf.Sin(phase * 2f - 0.5f);
                samples[i] = (rumble + roadTexture) * load * 0.25f;
            }
        }
        AudioClip clip = AudioClip.Create(
            cloud ? "Goku day flying loop" : "Car driving loop",
            sampleCount, 1, sampleRate, false);
        clip.SetData(samples, 0);
        if (cloud) cloudEngineLoopClip = clip;
        else carEngineLoopClip = clip;
        return clip;
    }

    private static string GetResource(BodybuilderIdentity value)
    {
        switch (value)
        {
            case BodybuilderIdentity.Cbum: return "Vehicles/cbum_vehicle";
            case BodybuilderIdentity.Zyzz: return "Vehicles/zyzz_vehicle";
            case BodybuilderIdentity.Arnold: return "Vehicles/arnold_vehicle";
            case BodybuilderIdentity.JayCutler: return "Vehicles/jaycutler_vehicle";
            case BodybuilderIdentity.Goku: return "Vehicles/goku_vehicle";
            case BodybuilderIdentity.Ronnie: return "Vehicles/ronnie_vehicle";
            case BodybuilderIdentity.Davie: return DavieBusAsset;
            default: return "Vehicles/cbum_vehicle";
        }
    }

    private static string GetTextureResource(BodybuilderIdentity value)
    {
        string resource = GetResource(value);
        string stem = resource.Substring(resource.LastIndexOf('/') + 1);
        return "Vehicles/Textures/" + stem;
    }
}
