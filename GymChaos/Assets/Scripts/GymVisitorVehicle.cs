using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Visible, textured, collidable arrival/parking/departure for one visitor.</summary>
public sealed class GymVisitorVehicle : MonoBehaviour
{
    private const float DriveSpeed = 8.5f;
    private const float CloudSpeed = 48f;
    private const float TurnSpeed = 5f;
    private const float LaneSpawnSpacing = 7.5f;
    private const float ForwardSensorMinimumDistance = 2.35f;
    private const float ForwardSensorMaximumDistance = 3.35f;
    private const float ForwardSensorHalfWidth = 0.92f;
    private const float CrossingPredictionSeconds = 0.45f;
    private static readonly List<GymVisitorVehicle> activeGroundTraffic =
        new List<GymVisitorVehicle>();
    private static int nextTrafficOrder;

    private BodybuilderIdentity identity;
    private PlayerMovement player;
    private AudioSource engine;
    private AudioSource horn;
    private AudioClip hornClip;
    private float currentDriveSpeed;
    private float blockedSince = -1f;
    private float nextHornTime;
    private string lastTrafficBlocker = "none";
    private int hornPlayCount;
    private int trafficOrder;
    private readonly RaycastHit[] roadHits = new RaycastHit[48];
    private readonly Collider[] nearPeople = new Collider[48];
    public bool IsYieldingToPedestrian { get; private set; }

    private Vector3 roadPoint;
    private Vector3 junctionPoint;
    private Vector3 roadTurnPoint;
    private Vector3 aislePoint;
    private Vector3 parkingPoint;
    private Vector3 departureRoadPoint;
    private Vector3 departureRoadTurnPoint;
    private Vector3 departureJunctionPoint;
    private Coroutine driveRoutine;
    private Transform riderAnchor;
    private EnemyFighter mountedRider;

    public bool IsParked { get; private set; }
    public bool IsDriving => driveRoutine != null;
    public bool HasCompletedDeparture { get; private set; }
    public bool IsCloud => identity == BodybuilderIdentity.Goku;
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
    public float BoardingReachDistance => IsCloud ? 0.8f : 0.65f;
#if UNITY_EDITOR
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
    public bool IsEngineStoppedForVerification => engine == null || !engine.isPlaying;
    public static float SpawnSpacingForVerification => LaneSpawnSpacing;
    public static float SensorMaximumDistanceForVerification => ForwardSensorMaximumDistance;
    public static float SensorHalfWidthForVerification => ForwardSensorHalfWidth;
    public static float CrossingPredictionForVerification => CrossingPredictionSeconds;
    public float CurrentDriveSpeedForVerification => currentDriveSpeed;
    public string LastTrafficBlockerForVerification => lastTrafficBlocker;
    public int HornPlayCountForVerification => hornPlayCount;
    public Vector3 ArrivalRoadTurnPointForVerification => roadTurnPoint;
    public Vector3 DepartureRoadTurnPointForVerification => departureRoadTurnPoint;
#endif

    public static GymVisitorVehicle Create(
        BodybuilderIdentity identity, int slot, PlayerMovement player,
        bool initiallyParked)
    {
        string resource = GetResource(identity);
        GameObject prefab = Resources.Load<GameObject>(resource);
        GameObject root = new GameObject(identity + " Visitor Vehicle");
        GymVisitorVehicle vehicle = root.AddComponent<GymVisitorVehicle>();
        vehicle.identity = identity;
        vehicle.player = player;
        vehicle.ConfigureRoute(slot);
        if (prefab != null)
        {
            GameObject visual = Instantiate(prefab, root.transform);
            visual.name = identity + " Vehicle Model";
            // Fit the body to the actual bay footprint. The old 4.5m target
            // was wider than the narrowest generated bay and the old route
            // point was not aligned with the painted bay centers.
            vehicle.FitVisual(visual, identity == BodybuilderIdentity.Goku
                ? 2.8f : GymOutdoorBuilder.ParkingVehicleTargetLength);
            vehicle.ApplyOriginalMaterial(visual);
            vehicle.AddPhysicalBody(visual);
            if (vehicle.IsCloud) vehicle.CreateRiderAnchor(visual);
        }
        else
        {
            Debug.LogError($"GYMCHAOS_VEHICLE_MODEL_MISSING identity={identity} path={resource}");
        }
        vehicle.CreateEngineAudio();
        root.transform.position = initiallyParked ? vehicle.parkingPoint : vehicle.roadPoint;
        root.transform.rotation = vehicle.ParkedRotation;
        vehicle.IsParked = initiallyParked;
        root.SetActive(initiallyParked);
        return vehicle;
    }

    public void DriveIn(Action onParked)
    {
        if (driveRoutine != null) StopCoroutine(driveRoutine);
        gameObject.SetActive(true);
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
        Vector3[] route = new[] { roadPoint, roadTurnPoint, junctionPoint, aislePoint, parkingPoint };
        driveRoutine = StartCoroutine(DriveRoute(route, true, onParked));
    }

    public void DriveOut(Action onGone)
    {
        if (driveRoutine != null) StopCoroutine(driveRoutine);
        gameObject.SetActive(true);
        IsParked = false;
        drivingIntoParking = false;
        trafficOrder = ++nextTrafficOrder;
        if (IsCloud)
        {
            driveRoutine = StartCoroutine(DriveCloudRoute(false, onGone));
            return;
        }
        Vector3[] route = new[] {
            aislePoint, departureJunctionPoint, departureRoadTurnPoint, departureRoadPoint };
        int queueRank = CountOutgoingTraffic();
        float releaseDelay = queueRank * 1.6f;
        driveRoutine = StartCoroutine(DriveRoute(route, false, onGone, releaseDelay));
        Debug.Log($"GYMCHAOS_VEHICLE_DEPARTURE_QUEUED identity={identity} " +
            $"rank={queueRank} delay={releaseDelay:F1}", this);
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
        if (engine != null) engine.Play();
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
        if (engine != null) engine.Stop();
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
        Vector3[] points, bool park, Action done, float releaseDelay = 0f)
    {
        if (!IsCloud && !activeGroundTraffic.Contains(this))
            activeGroundTraffic.Add(this);
        if (releaseDelay > 0f)
        {
            currentDriveSpeed = 0f;
            if (engine != null) engine.Stop();
            yield return new WaitForSeconds(releaseDelay);
        }
        if (engine != null) engine.Play();
        float speed = IsCloud ? CloudSpeed : DriveSpeed;
        currentDriveSpeed = 0f;
        for (int i = 0; i < points.Length; i++)
        {
            Vector3 target = points[i];
            bool finalPoint = i == points.Length - 1;
            float reachRadius = finalPoint ? 0.2f : 0.9f;
            Vector3 segmentStart = transform.position;
            Vector3 segment = Vector3.ProjectOnPlane(target - segmentStart, Vector3.up);
            float segmentLength = segment.magnitude;
            Vector3 segmentDirection = segmentLength > 0.01f
                ? segment / segmentLength
                : Vector3.zero;
            while ((transform.position - target).sqrMagnitude > reachRadius * reachRadius)
            {
                // Direction blending deliberately rounds a corner before its
                // mathematical centre. Once the vehicle has entered that
                // rounded corner, advance to the next leg; otherwise it can
                // orbit an already-cut waypoint forever while still moving.
                if (!finalPoint && segmentLength > 0.01f)
                {
                    float progress = Vector3.Dot(
                        Vector3.ProjectOnPlane(transform.position - segmentStart, Vector3.up),
                        segmentDirection);
                    float cornerEntry = Mathf.Max(0f, segmentLength - 1.8f);
                    if (progress >= cornerEntry)
                    {
                        break;
                    }
                }
                Vector3 direction = target - transform.position;
                if (!IsCloud) direction = Vector3.ProjectOnPlane(direction, Vector3.up);
                direction.Normalize();
                float remaining = Vector3.Distance(transform.position, target);
                if (!finalPoint && remaining < 5f)
                {
                    Vector3 outgoing = Vector3.ProjectOnPlane(
                        points[i + 1] - target, Vector3.up);
                    if (outgoing.sqrMagnitude > 0.01f)
                    {
                        float blend = Mathf.InverseLerp(5f, reachRadius, remaining);
                        direction = Vector3.Slerp(
                            direction, outgoing.normalized, blend * 0.72f).normalized;
                    }
                }
                if (direction.sqrMagnitude > 0.01f)
                {
                    transform.rotation = Quaternion.Slerp(
                        transform.rotation,
                        Quaternion.LookRotation(direction, Vector3.up),
                        TurnSpeed * Time.deltaTime);
                }
                bool pedestrianAhead = false;
                float clearance = IsCloud
                    ? float.PositiveInfinity
                    : TrafficClearance(direction, out pedestrianAhead);
                float routeSpeed = finalPoint
                    ? Mathf.Sqrt(2f * 5f * Mathf.Max(0f, remaining - 0.1f))
                    : Mathf.Lerp(4.2f, speed, Mathf.InverseLerp(reachRadius, 5f, remaining));
                float targetSpeed = IsCloud ? speed : Mathf.Min(speed, routeSpeed);
                if (!IsCloud)
                    targetSpeed = Mathf.Min(targetSpeed, Mathf.Sqrt(2f * 7f * Mathf.Max(0f, clearance - 0.5f)));
                currentDriveSpeed = Mathf.MoveTowards(currentDriveSpeed, targetSpeed,
                    (targetSpeed < currentDriveSpeed ? 10f : 6.5f) * Time.deltaTime);
                IsYieldingToPedestrian = !IsCloud && pedestrianAhead && clearance < 3f;
                UpdateHorn(IsYieldingToPedestrian);
                float travel = Mathf.Min(currentDriveSpeed * Time.deltaTime,
                    Mathf.Max(0f, clearance - 0.45f));
                if (finalPoint)
                {
                    transform.position = Vector3.MoveTowards(
                        transform.position, target, travel);
                }
                else
                {
                    transform.position += direction * travel;
                }
                if (engine != null) engine.pitch = Mathf.Lerp(0.72f, 1.15f, currentDriveSpeed / speed);
                UpdateEngineAudibility();
                yield return null;
            }
            if (finalPoint) transform.position = target;
        }
        if (engine != null) engine.Stop();
        activeGroundTraffic.Remove(this);
        IsYieldingToPedestrian = false;
        IsParked = park;
        driveRoutine = null;
        if (!park) HasCompletedDeparture = true;
        if (!park) gameObject.SetActive(false);
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

    private float TrafficClearance(Vector3 direction, out bool pedestrianAhead)
    {
        pedestrianAhead = false;
        lastTrafficBlocker = "none";
        float convoyClearance = SameDirectionConvoyClearance(out string convoyBlocker);
        if (convoyClearance <= 0.01f)
        {
            lastTrafficBlocker = convoyBlocker;
            return 0f;
        }
        BoxCollider bodyCollider = GetComponent<BoxCollider>();
        float frontExtent = 1.7f;
        if (bodyCollider != null)
        {
            Vector3 extents = bodyCollider.bounds.extents;
            frontExtent = Mathf.Abs(direction.x) * extents.x +
                Mathf.Abs(direction.z) * extents.z;
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
            if (!IsTrafficObstacle(hit)) continue;
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
            if (!IsTrafficObstacle(hit)) continue;
            pedestrianAhead |= IsPerson(hit);
            RequestPedestrianYield(hit, direction);
            lastTrafficBlocker = hit.name;
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
            if (!IsTrafficObstacle(hit)) continue;
            pedestrianAhead |= IsPerson(hit);
            RequestPedestrianYield(hit, direction);
            if (roadHits[i].distance < clearance)
            {
                clearance = roadHits[i].distance;
                lastTrafficBlocker = hit.name;
            }
        }
        float crossingClearance = PredictCrossingPedestrianClearance(direction);
        if (crossingClearance < clearance)
        {
            pedestrianAhead = true;
            clearance = crossingClearance;
            lastTrafficBlocker = "crossing pedestrian";
        }
        return clearance;
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
                RequestPedestrianYield(candidate, forward);
                clearance = Mathf.Min(clearance, Mathf.Max(0f, ahead - 0.55f));
            }
        }
        return clearance;
    }

    private void RequestPedestrianYield(Collider hit, Vector3 direction)
    {
        if (hit == null || !IsPerson(hit))
        {
            return;
        }

        GymVisitorAgent agent = hit.GetComponentInParent<GymVisitorAgent>();
        agent?.RequestVehicleYield(this, direction);
    }

    private float SameDirectionConvoyClearance(out string blocker)
    {
        blocker = "none";
        float clearance = float.PositiveInfinity;
        for (int i = activeGroundTraffic.Count - 1; i >= 0; i--)
        {
            GymVisitorVehicle other = activeGroundTraffic[i];
            if (other == null)
            {
                activeGroundTraffic.RemoveAt(i);
                continue;
            }
            if (other == this || !other.IsDriving ||
                other.drivingIntoParking != drivingIntoParking ||
                other.trafficOrder >= trafficOrder)
                continue;
            float separation = Vector3.ProjectOnPlane(
                other.transform.position - transform.position, Vector3.up).magnitude;
            float candidate = Mathf.Max(0f, separation - 3f);
            if (candidate < clearance)
            {
                clearance = candidate;
                blocker = other.name;
            }
        }
        return clearance;
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
        // The parking perimeter remains a real collider, but when the
        // authored stall target is already in front of that perimeter the
        // bumper sensor must not treat the far-side fence as a blocker. The
        // route coroutine stops at parkingPoint; this only removes a false
        // early stop a car-width before the target and never opens a route
        // through the fence itself.
        if (drivingIntoParking &&
            lowerName.Contains("outdoor boundary - parking"))
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
            // This invisible player-only limit intentionally spans the visual
            // road opening. The exterior builder keeps it so the player cannot
            // leave the playable courtyard, while authored vehicle routes are
            // explicitly allowed to continue through that opening.
            !lowerName.Contains("outdoor boundary - path outer") &&
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
        GymVisitorAgent[] agents = FindObjectsByType<GymVisitorAgent>(FindObjectsSortMode.None);
        for (int i = 0; i < agents.Length; i++)
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
        activeGroundTraffic.Remove(this);
    }

    private void Update()
    {
        if (engine != null || horn != null) UpdateVehicleAudioAudibility();
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
        bool audibleDrivingState = IsDriving && !IsParked && gameObject.activeInHierarchy;
        bool audible = outside && audibleDrivingState;
        if (engine != null)
        {
            engine.mute = !audible;
            if (audible)
            {
                if (!engine.isPlaying)
                {
                    engine.Play();
                }
            }
            else if (engine.isPlaying)
            {
                engine.Stop();
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
        float floorY = parking.center.y + 0.08f;
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

    private Quaternion ParkedRotation => Quaternion.LookRotation(
        parkingPoint.z >= aislePoint.z ? Vector3.forward : Vector3.back, Vector3.up);

    private void FitVisual(GameObject visual, float targetLength)
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
        visual.transform.position += Vector3.up * -bounds.min.y;
    }

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
        Texture2D texture = Resources.Load<Texture2D>(GetTextureResource(identity));
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
        }
    }

    private void AddPhysicalBody(GameObject visual)
    {
        Renderer[] renderers = visual.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0) return;
        Bounds bounds = CombinedBounds(renderers);
        BoxCollider box = gameObject.AddComponent<BoxCollider>();
        box.center = transform.InverseTransformPoint(bounds.center);
        box.size = bounds.size;
        Rigidbody rigidbody = gameObject.AddComponent<Rigidbody>();
        rigidbody.isKinematic = true;
        rigidbody.useGravity = false;
        rigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
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
        engine.volume = IsCloud ? 0.18f : 0.24f;
        const int sampleRate = 22050;
        const int sampleCount = sampleRate * 2;
        float[] samples = new float[sampleCount];
        for (int i = 0; i < sampleCount; i++)
        {
            float loop = i / (float)sampleCount;
            float phase = loop * Mathf.PI * 2f;
            if (IsCloud)
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
            IsCloud ? "Goku day flying loop" : "Car driving loop",
            sampleCount, 1, sampleRate, false);
        clip.SetData(samples, 0);
        engine.clip = clip;
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
