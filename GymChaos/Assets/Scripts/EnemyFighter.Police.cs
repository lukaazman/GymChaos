using System.Collections.Generic;
using UnityEngine;

public partial class EnemyFighter
{
    /// <summary>
    /// Locks a police intervention to the person who killed Ronnie. The
    /// normal room-wide police observer remains the fallback when no killer
    /// is available, but an explicit killer must never be replaced by a nearer
    /// participant during the intervention.
    /// </summary>
    public void SetPoliceKillerTarget(Transform target)
    {
        if (!isPolice)
        {
            return;
        }

        forcedPoliceTarget = target;
        SetPoliceTarget(target);
    }
    public void SetPoliceRangedMode(bool ranged)
    {
        policeRangedMode = isPolice && ranged;
    }
    private bool TryFindPoliceRoomDestination(out Vector3 point)
    {
        // Prefer the existing long-range open-floor sampler. It explicitly
        // rejects equipment, walls and the previous endpoint.
        if (TryFindRareRandomDestination(out point))
        {
            return true;
        }

        if (!TryGetRoomBounds(out Bounds floorBounds))
        {
            point = transform.position;
            return false;
        }

        float margin = GetBodyRadius() + 0.24f;
        for (int attempt = 0; attempt < 96; attempt++)
        {
            point = new Vector3(
                Random.Range(floorBounds.min.x + margin, floorBounds.max.x - margin),
                floorBounds.max.y,
                Random.Range(floorBounds.min.z + margin, floorBounds.max.z - margin));
            if (Vector3.ProjectOnPlane(
                    point - transform.position, Vector3.up).sqrMagnitude < 16f ||
                (hasLastRoamTarget && Vector3.ProjectOnPlane(
                    point - lastRoamTarget, Vector3.up).sqrMagnitude < 16f) ||
                !IsRoamPointClear(point))
            {
                continue;
            }

            float edgeClearance = Mathf.Min(
                point.x - floorBounds.min.x,
                floorBounds.max.x - point.x,
                point.z - floorBounds.min.z,
                floorBounds.max.z - point.z);
            if (edgeClearance < 1.4f)
            {
                continue;
            }

            return true;
        }

        point = transform.position;
        return false;
    }
    private bool IsCurrentPoliceTargetValid(bool allowPlayer)
    {
        if (forcedPoliceTarget != null)
        {
            if (currentTarget != forcedPoliceTarget)
            {
                return false;
            }

            EnemyFighter forcedFighter =
                forcedPoliceTarget.GetComponentInParent<EnemyFighter>();
            if (forcedFighter != null)
            {
                return forcedFighter != this && !forcedFighter.IsDead;
            }

            return playerTarget != null &&
                forcedPoliceTarget == playerTarget.transform &&
                !playerTarget.IsDead &&
                !playerTarget.IsExercising;
        }

        if (isAggressive)
        {
            return allowPlayer && playerTarget != null && !playerTarget.IsDead &&
                !playerTarget.IsExercising && currentTarget == playerTarget.transform;
        }

        if (!IsFightActive || currentTarget == null)
        {
            return false;
        }
        if (currentFighterTarget != null)
        {
            return !currentFighterTarget.IsDead && currentFighterTarget != this &&
                currentFighterTarget.isAggressive && !currentFighterTarget.isPolice &&
                !currentFighterTarget.isPassive;
        }
        return allowPlayer && playerTarget != null && !playerTarget.IsDead &&
            !playerTarget.IsExercising && currentTarget == playerTarget.transform;
    }
    private Transform FindNearestPoliceTarget(bool allowPlayer)
    {
        Transform best = null;
        float bestDistance = float.PositiveInfinity;

        // A police dispatch supplies an explicit killer. Keep that target
        // locked even when the killer was neutral before the Ronnie hit and
        // even when another fight participant is physically closer.
        if (forcedPoliceTarget != null)
        {
            EnemyFighter forcedFighter =
                forcedPoliceTarget.GetComponentInParent<EnemyFighter>();
            if (forcedFighter != null)
            {
                return forcedFighter != this && !forcedFighter.IsDead
                    ? forcedPoliceTarget : null;
            }

            return allowPlayer && playerTarget != null &&
                forcedPoliceTarget == playerTarget.transform &&
                !playerTarget.IsDead && !playerTarget.IsExercising
                ? forcedPoliceTarget : null;
        }

        // Ronnie's own anger takes precedence over the room-wide fight scan:
        // direct damage from the player must make him pursue that player even
        // when nobody else is currently fighting.
        if (isAggressive)
        {
            return allowPlayer && playerTarget != null && !playerTarget.IsDead &&
                !playerTarget.IsExercising ? playerTarget.transform : null;
        }

        if (!IsFightActive)
        {
            return null;
        }

        if (allowPlayer && playerTarget != null && !playerTarget.IsDead &&
            !playerTarget.IsExercising)
        {
            best = playerTarget.transform;
            bestDistance = Vector3.ProjectOnPlane(
                best.position - transform.position, Vector3.up).sqrMagnitude;
        }

        // Awake/OnDestroy maintain this registry. Reusing it avoids a
        // FindObjectsByType allocation every target-refresh tick while still
        // seeing enemies created during the current room session.
        for (int i = Fighters.Count - 1; i >= 0; i--)
        {
            EnemyFighter candidate = Fighters[i];
            if (candidate == null || !candidate.isActiveAndEnabled ||
                candidate == this || candidate.IsDead || candidate.isPolice ||
                candidate.isPassive || !candidate.isAggressive)
            {
                continue;
            }

            float distance = Vector3.ProjectOnPlane(
                candidate.transform.position - transform.position, Vector3.up).sqrMagnitude;
            if (distance < bestDistance)
            {
                best = candidate.transform;
                bestDistance = distance;
            }
        }
        return best;
    }
    private void SetPoliceTarget(Transform target)
    {
        if (target != null)
        {
            EndTreadmillVisit();
        }
        currentTarget = target;
        currentFighterTarget = target != null
            ? target.GetComponentInParent<EnemyFighter>() : null;
        targetLockedUntil = Time.time + policeMinimumTargetLock;
    }
    private const float PoliceDoorRequestRadius = 4.5f;
    private bool policeDoorOpenRequested;
    public bool PoliceDoorRequestHeldForVerification => policeDoorOpenRequested;

    // The gym door only opens on request. Visitors request it on their
    // routes; the officer must too, or it waits on a closed door unless a
    // visitor happens to be passing.
    private void UpdatePoliceDoorRequest()
    {
        GymDoorway doorway = GymDoorway.Instance;
        bool wantOpen = doorway != null && body != null && !IsDead &&
            Vector3.ProjectOnPlane(doorway.DoorCenter - body.position, Vector3.up)
                .magnitude <= PoliceDoorRequestRadius;
        if (wantOpen == policeDoorOpenRequested)
        {
            return;
        }
        policeDoorOpenRequested = wantOpen;
        if (wantOpen)
        {
            doorway.RequestOpen();
        }
        else if (doorway != null)
        {
            doorway.ReleaseOpen();
        }
    }

    private void ReleasePoliceDoorRequest()
    {
        if (!policeDoorOpenRequested)
        {
            return;
        }
        policeDoorOpenRequested = false;
        if (GymDoorway.Instance != null)
        {
            GymDoorway.Instance.ReleaseOpen();
        }
    }

    private bool TickAuthoredPolicePursuit()
    {
        UpdatePoliceDoorRequest();
        RestoreDoorwayCrowdCollisions(false);
        if (currentTarget == null || body == null ||
            GymDoorway.Instance == null || !GymOutdoorBuilder.IsBuilt)
        {
            return false;
        }

        Vector3 targetPosition = currentTarget.position;
        Vector3 planarToTarget = Vector3.ProjectOnPlane(
            targetPosition - body.position, Vector3.up);
        if (planarToTarget.magnitude <= GetCurrentAttackRange())
        {
            policePursuitRoute.Clear();
            policePursuitRouteIndex = 0;
            return false;
        }

        bool officerOutside = GymOutdoorBuilder.IsPlayerOutsideGym(body.position);
        bool targetOutside = GymOutdoorBuilder.IsPlayerOutsideGym(targetPosition);
        if (!officerOutside && !targetOutside)
        {
            policePursuitRoute.Clear();
            policePursuitRouteIndex = 0;
            // Still inside the door opening: finish walking into the gym on
            // the authored path (with crowd pass) before the indoor chase AI
            // takes over, so a character in the doorway cannot pin him there.
            GymDoorway doorway = GymDoorway.Instance;
            if (Vector3.ProjectOnPlane(doorway.DoorCenter - body.position, Vector3.up)
                    .magnitude < DoorwayCrowdRadius && !IsPastDoorwayInterior())
            {
                MovePoliceAlongAuthoredWaypoint(GetDoorwayAxisWaypoint(true), 0.35f);
                return true;
            }
            return false;
        }

        bool targetMoved = Vector3.ProjectOnPlane(
            targetPosition - policePursuitTargetSnapshot,
            Vector3.up).sqrMagnitude > 4f;
        if (policePursuitRoute.Count == 0 ||
            policePursuitRouteIndex >= policePursuitRoute.Count ||
            (targetMoved && Time.time >= policePursuitNextRebuildTime))
        {
            BuildPolicePursuitRoute(targetPosition);
        }

        if (policePursuitRoute.Count == 0 ||
            policePursuitRouteIndex >= policePursuitRoute.Count)
        {
            return false;
        }

        int index = policePursuitRouteIndex;
        Vector3 waypoint = index == policePursuitRoute.Count - 1
            ? targetPosition
            : policePursuitRoute[index];
        bool reached = MovePoliceAlongAuthoredWaypoint(
            waypoint,
            index == policePursuitRoute.Count - 1
                ? GetCurrentAttackRange()
                : 0.72f);
        if (reached)
        {
            Debug.Log(
                $"GYMCHAOS_POLICE_PURSUIT_WAYPOINT_OK " +
                $"index={policePursuitRouteIndex} waypoint={waypoint} " +
                $"position={body.position}",
                this);
            policePursuitRouteIndex++;
            if (policePursuitRouteIndex >= policePursuitRoute.Count)
            {
                Debug.Log(
                    $"GYMCHAOS_POLICE_PURSUIT_ROUTE_COMPLETE " +
                    $"target={currentTarget.name} position={body.position}",
                    this);
                policePursuitRoute.Clear();
                policePursuitRouteIndex = 0;
            }
        }
        return true;
    }
    private bool MovePoliceAlongAuthoredWaypoint(
        Vector3 destination, float completionRadius)
    {
        destination.y = standingRootY;
        Vector3 delta = Vector3.ProjectOnPlane(
            destination - body.position, Vector3.up);
        float distance = delta.magnitude;
        if (distance <= Mathf.Max(0.2f, completionRadius))
        {
            return true;
        }

        Vector3 direction = delta / distance;
        Vector3 safeDirection = FindVisitorMovementDirection(
            direction, Mathf.Min(distance, 1.1f), true, null);
        if (safeDirection.sqrMagnitude < 0.001f &&
            TryPassDoorwayCrowd(direction, Mathf.Min(distance, 1.1f)))
        {
            safeDirection = direction;
        }
        if (safeDirection.sqrMagnitude < 0.001f)
        {
            body.linearVelocity = Vector3.Project(body.linearVelocity, Vector3.up);
            SetAnimatedMovementFromVelocity(maxSpeed);
            return false;
        }
        direction = safeDirection.normalized;
        body.WakeUp();
        Vector3 planarVelocity = Vector3.ProjectOnPlane(
            body.linearVelocity, Vector3.up);
        float nextSpeed = Mathf.MoveTowards(
            planarVelocity.magnitude,
            maxSpeed,
            Mathf.Max(moveForce * 0.8f, 12f) * Time.fixedDeltaTime);
        Vector3 velocityDirection = planarVelocity.sqrMagnitude > 0.0025f
            ? Vector3.RotateTowards(
                planarVelocity.normalized,
                direction,
                Mathf.Deg2Rad * 360f * Time.fixedDeltaTime,
                0f).normalized
            : direction;
        body.linearVelocity = velocityDirection * nextSpeed +
            Vector3.Project(body.linearVelocity, Vector3.up);
        body.MoveRotation(Quaternion.RotateTowards(
            body.rotation,
            Quaternion.LookRotation(direction, Vector3.up),
            300f * Time.fixedDeltaTime));
        SetAnimatedMovementFromVelocity(maxSpeed);
        return false;
    }
    // The gym entrance is an open walk-through gap. A visitor waiting in it
    // must not deadlock the officer: when only characters block a statically
    // clear step near the door, pass through them and restore the collision
    // pairs once the officer is clear of the doorway.
    private const float DoorwayCrowdRadius = 4.5f;
    private const float DoorwayCrowdReleaseRadius = 6f;
    private readonly List<Collider> doorwayIgnoredColliders = new List<Collider>();
    private Collider[] officerOwnColliders;

    private bool TryPassDoorwayCrowd(Vector3 direction, float distance)
    {
        GymDoorway doorway = GymDoorway.Instance;
        if (doorway == null || body == null ||
            Vector3.ProjectOnPlane(doorway.DoorCenter - body.position, Vector3.up).magnitude >
                DoorwayCrowdRadius ||
            !IsVisitorPathClear(direction, distance, true, null, null, true))
        {
            return false;
        }
        officerOwnColliders ??= GetComponentsInChildren<Collider>(true);
        IReadOnlyList<EnemyFighter> fighters = RegisteredFighters;
        for (int i = 0; i < fighters.Count; i++)
        {
            EnemyFighter other = fighters[i];
            if (other == null || other == this || other.body == null ||
                (currentTarget != null && other.transform == currentTarget) ||
                Vector3.Distance(other.body.position, body.position) > 3f)
            {
                continue;
            }
            foreach (Collider theirs in other.GetComponentsInChildren<Collider>(true))
            {
                if (theirs == null || doorwayIgnoredColliders.Contains(theirs))
                {
                    continue;
                }
                foreach (Collider mine in officerOwnColliders)
                {
                    if (mine != null)
                    {
                        Physics.IgnoreCollision(mine, theirs, true);
                    }
                }
                doorwayIgnoredColliders.Add(theirs);
            }
        }
        if (Time.time >= nextDoorwayCrowdLogTime)
        {
            nextDoorwayCrowdLogTime = Time.time + 1.5f;
            Debug.Log($"GYMCHAOS_POLICE_DOORWAY_CROWD_PASS ignored={doorwayIgnoredColliders.Count}", this);
        }
        return true;
    }

    private float nextDoorwayCrowdLogTime;

    private bool IsPastDoorwayInterior()
    {
        GymDoorway doorway = GymDoorway.Instance;
        Vector3 axis = Vector3.ProjectOnPlane(
            doorway.InteriorPoint - doorway.ExteriorPoint, Vector3.up);
        float length = axis.magnitude;
        axis /= Mathf.Max(length, 0.001f);
        float along = Vector3.Dot(
            Vector3.ProjectOnPlane(body.position - doorway.ExteriorPoint, Vector3.up), axis);
        return along >= length - 0.15f;
    }

    // Next step along the doorway centre line. A body pushed sideways by a
    // character in the opening first rejoins the axis instead of cutting the
    // wall corner.
    private Vector3 GetDoorwayAxisWaypoint(bool inward)
    {
        GymDoorway doorway = GymDoorway.Instance;
        Vector3 from = inward ? doorway.ExteriorPoint : doorway.InteriorPoint;
        Vector3 to = inward ? doorway.InteriorPoint : doorway.ExteriorPoint;
        Vector3 axis = Vector3.ProjectOnPlane(to - from, Vector3.up);
        float length = axis.magnitude;
        axis /= Mathf.Max(length, 0.001f);
        float along = Vector3.Dot(Vector3.ProjectOnPlane(body.position - from, Vector3.up), axis);
        Vector3 onAxis = from + axis * Mathf.Clamp(along, 0f, length);
        onAxis.y = body.position.y;
        float lateral = Vector3.ProjectOnPlane(body.position - onAxis, Vector3.up).magnitude;
        return lateral > 0.25f ? onAxis + axis * 0.4f : onAxis + axis * 2f;
    }

    private void RestoreDoorwayCrowdCollisions(bool force)
    {
        if (doorwayIgnoredColliders.Count == 0)
        {
            return;
        }
        GymDoorway doorway = GymDoorway.Instance;
        if (!force && doorway != null && body != null &&
            Vector3.ProjectOnPlane(doorway.DoorCenter - body.position, Vector3.up).magnitude <=
                DoorwayCrowdReleaseRadius)
        {
            return;
        }
        foreach (Collider theirs in doorwayIgnoredColliders)
        {
            if (theirs == null || officerOwnColliders == null)
            {
                continue;
            }
            foreach (Collider mine in officerOwnColliders)
            {
                if (mine != null)
                {
                    Physics.IgnoreCollision(mine, theirs, false);
                }
            }
        }
        doorwayIgnoredColliders.Clear();
    }

    private void BuildPolicePursuitRoute(Vector3 targetPosition)
    {
        policePursuitRoute.Clear();
        policePursuitRouteIndex = 0;
        policePursuitTargetSnapshot = targetPosition;
        policePursuitNextRebuildTime = Time.time + 0.75f;

        // Linear walking corridor shared with visitors: gym interior, door,
        // the gym path north, then the parking entry. The store is reached
        // from the door through its west entry walkway; the old loop around
        // the store used the now-closed east road opening.
        List<Vector3> corridor = new List<Vector3>(8);
        float y = body.position.y;
        float pathX = GymOutdoorBuilder.ProteinStoreParkingBypassPoint.x;
        Vector3 exterior = GymDoorway.Instance.ExteriorPoint;
        Vector3 pathSouth = new Vector3(pathX, y, exterior.z + 2.8f);
        // North end of the gym path: past the end of the path's inner wall
        // (it runs up to the road's south edge), so the leg to the parking
        // entry clears the wall whatever the road width.
        float roadSouthEdgeZ = GymOutdoorBuilder.ParkingBounds.center.z -
            GymOutdoorBuilder.VehicleRoadWidthForVerification * 0.5f;
        Vector3 pathNorth = new Vector3(
            pathX, y, Mathf.Max(
                GymOutdoorBuilder.ProteinStoreParkingBypassPoint.z + 3.0f,
                roadSouthEdgeZ + 0.5f));
        AddDistinctPoliceWaypoint(corridor, GymDoorway.Instance.InteriorPoint);
        AddDistinctPoliceWaypoint(corridor, exterior);
        AddDistinctPoliceWaypoint(corridor, pathSouth);
        AddDistinctPoliceWaypoint(corridor, pathNorth);
        AddDistinctPoliceWaypoint(
            corridor, GymOutdoorBuilder.VisitorParkingEntryPoint);
        const int exteriorIndex = 1;

        Bounds storeBounds = GymProteinStoreEnvironment.StoreBounds;
        bool InStore(Vector3 point) => point.x >= storeBounds.min.x &&
            point.x <= storeBounds.max.x && point.z >= storeBounds.min.z &&
            point.z <= storeBounds.max.z;
        bool officerInStore = InStore(body.position);
        bool targetInStore = InStore(targetPosition);
        Vector3[] storeEntry =
        {
            GymOutdoorBuilder.ProteinStoreGateWestClearPoint,
            GymOutdoorBuilder.ProteinStoreGymPathClearPoint,
            GymProteinStoreEnvironment.StoreVisitApproachPoint,
            GymProteinStoreEnvironment.StoreEntrancePoint,
            GymProteinStoreEnvironment.StoreFrontClearPoint
        };
        if (officerInStore && targetInStore)
        {
            AddDistinctPoliceWaypoint(policePursuitRoute, targetPosition);
            return;
        }
        if (officerInStore)
        {
            for (int i = storeEntry.Length - 1; i >= 0; i--)
            {
                AddDistinctPoliceWaypoint(policePursuitRoute, storeEntry[i]);
            }
        }
        int startIndex = officerInStore ||
            !GymOutdoorBuilder.IsPlayerOutsideGym(body.position)
            ? (officerInStore ? exteriorIndex : 0)
            : FindNearestPoliceWaypoint(corridor, body.position);
        int targetIndex = targetInStore
            ? exteriorIndex
            : !GymOutdoorBuilder.IsPlayerOutsideGym(targetPosition)
                ? 0 : FindNearestPoliceWaypoint(corridor, targetPosition);
        if (startIndex < 0 || targetIndex < 0)
        {
            return;
        }

        // Include the nearest corridor point when the officer is behind it;
        // cutting straight to the next point runs into the path walls.
        int step = targetIndex >= startIndex ? 1 : -1;
        int firstIndex = startIndex + step;
        if (startIndex != targetIndex && firstIndex >= 0 && firstIndex < corridor.Count)
        {
            Vector3 officerFlat = Vector3.ProjectOnPlane(body.position, Vector3.up);
            float officerToNext = Vector3.Distance(officerFlat,
                Vector3.ProjectOnPlane(corridor[firstIndex], Vector3.up));
            float startToNext = Vector3.Distance(
                Vector3.ProjectOnPlane(corridor[startIndex], Vector3.up),
                Vector3.ProjectOnPlane(corridor[firstIndex], Vector3.up));
            if (officerToNext > startToNext + 1f)
            {
                firstIndex = startIndex;
            }
        }
        else if (startIndex == targetIndex)
        {
            firstIndex = startIndex + step;
        }
        for (int index = firstIndex;
            index != targetIndex + step;
            index += step)
        {
            AddDistinctPoliceWaypoint(policePursuitRoute, corridor[index]);
        }
        if (targetInStore)
        {
            for (int i = 0; i < storeEntry.Length; i++)
            {
                AddDistinctPoliceWaypoint(policePursuitRoute, storeEntry[i]);
            }
        }
        AddDistinctPoliceWaypoint(policePursuitRoute, targetPosition);

        Debug.Log(
            $"GYMCHAOS_POLICE_PURSUIT_ROUTE_READY " +
            $"waypoints={policePursuitRoute.Count} from={body.position} " +
            $"to={targetPosition} outside={GymOutdoorBuilder.IsPlayerOutsideGym(targetPosition)}",
            this);
    }
    private static void AddDistinctPoliceWaypoint(
        List<Vector3> route, Vector3 waypoint)
    {
        if (route.Count == 0 ||
            Vector3.ProjectOnPlane(
                route[route.Count - 1] - waypoint,
                Vector3.up).sqrMagnitude >= 0.16f)
        {
            route.Add(waypoint);
        }
    }
    private static int FindNearestPoliceWaypoint(
        List<Vector3> route, Vector3 position)
    {
        int bestIndex = -1;
        float bestDistance = float.PositiveInfinity;
        for (int index = 0; index < route.Count; index++)
        {
            float distance = Vector3.ProjectOnPlane(
                route[index] - position,
                Vector3.up).sqrMagnitude;
            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestIndex = index;
            }
        }
        return bestIndex;
    }
    public void ReceivePoliceImpact(Vector3 impulse)
    {
        if (isDead || body == null)
        {
            return;
        }
        body.AddForce(impulse * 0.35f, ForceMode.Impulse);
        stunnedUntilTime = Mathf.Max(stunnedUntilTime, Time.time + lightStunDuration);
    }
}
