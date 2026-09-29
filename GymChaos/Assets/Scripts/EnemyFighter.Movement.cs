using System.Collections.Generic;
using UnityEngine;

public partial class EnemyFighter
{
    private Vector3 GetRoamContinuityDirection(
        Vector3 desiredDirection, float targetDistance, bool allowTargetEquipment)
    {
        desiredDirection = Vector3.ProjectOnPlane(desiredDirection, Vector3.up);
        if (desiredDirection.sqrMagnitude < 0.0001f)
        {
            return Vector3.zero;
        }

        desiredDirection.Normalize();
        float shortLookAhead = Mathf.Clamp(
            Mathf.Min(Mathf.Max(0.18f, targetDistance), 0.34f), 0.18f, 0.34f);
        if (roamDirection.sqrMagnitude > 0.001f)
        {
            Vector3 held = Vector3.ProjectOnPlane(roamDirection, Vector3.up).normalized;
            if (Vector3.Dot(held, desiredDirection) > 0.82f &&
                IsInsideRoom(held, shortLookAhead) &&
                IsMovementPathClear(held, shortLookAhead, allowTargetEquipment))
            {
                return held;
            }
        }

        if (IsInsideRoom(desiredDirection, shortLookAhead) &&
            IsMovementPathClear(desiredDirection, shortLookAhead, allowTargetEquipment))
        {
            return desiredDirection;
        }

        return Vector3.zero;
    }
    private void ApplyRoamMovement(Vector3 direction)
    {
        if (body == null || direction.sqrMagnitude < 0.001f)
        {
            return;
        }

        direction = Vector3.ProjectOnPlane(direction, Vector3.up).normalized;
        body.WakeUp();
        Vector3 planarVelocity = Vector3.ProjectOnPlane(body.linearVelocity, Vector3.up);
        Vector3 desiredVelocity = direction * roamSpeed;
        planarVelocity = Vector3.MoveTowards(
            planarVelocity, desiredVelocity,
            Mathf.Max(moveForce * 0.55f, 8f) * Time.fixedDeltaTime);
        body.linearVelocity = planarVelocity + Vector3.Project(body.linearVelocity, Vector3.up);
        ClampToRoomBounds();

        Quaternion lookRotation = Quaternion.LookRotation(direction, Vector3.up);
        body.MoveRotation(Quaternion.RotateTowards(
            body.rotation, lookRotation, 260f * Time.fixedDeltaTime));
        SetAnimatedMovementFromVelocity(roamSpeed);
    }
    // One enemy per machine. A station is off limits when someone is using or
    // has reserved it, or when another neutral enemy is already walking to /
    // standing at it and is closer. Without this, several visitors picked the
    // same interaction point (the Smith machine squat spot) and pushed into
    // each other inside the cage.
    private bool IsStationClaimedByOther(GymExerciseStation station)
    {
        if (station == null)
        {
            return false;
        }
        if (station.IsOccupied && station.EnemyOccupant != this)
        {
            return true;
        }

        Vector3 spot = station.EnemyPosition;
        float myDistance = Vector3.ProjectOnPlane(spot - transform.position, Vector3.up).sqrMagnitude;
        for (int i = 0; i < Fighters.Count; i++)
        {
            EnemyFighter other = Fighters[i];
            if (other == null || other == this || other.isDead || other.isAggressive ||
                !other.isActiveAndEnabled)
            {
                continue;
            }
            bool otherTargets = other.hasRoamTarget && other.roamTargetStation == station;
            float otherDistance = Vector3.ProjectOnPlane(
                spot - other.transform.position, Vector3.up).sqrMagnitude;
            bool otherStandsThere = otherDistance < StationPersonalSpace * StationPersonalSpace;
            if (!otherTargets && !otherStandsThere)
            {
                continue;
            }
            // Deterministic tie-break so two enemies never both yield.
            if (otherStandsThere || otherDistance < myDistance ||
                (Mathf.Approximately(otherDistance, myDistance) &&
                 i < Fighters.IndexOf(this)))
            {
                return true;
            }
        }
        return false;
    }
    private const float StationPersonalSpace = 1.1f;
    private bool IsIdlingOnAnotherEnemysStation()
    {
        for (int i = 0; i < roamInterests.Count; i++)
        {
            GymExerciseStation station = roamInterests[i] != null ? roamInterests[i].station : null;
            if (station == null || !station.IsOccupiedByEnemy || station.EnemyOccupant == this)
            {
                continue;
            }
            if (Vector3.ProjectOnPlane(station.EnemyPosition - transform.position, Vector3.up).sqrMagnitude <
                StationPersonalSpace * StationPersonalSpace)
            {
                return true;
            }
        }
        return false;
    }
    internal GymExerciseStation RoamTargetStationForVerification => roamTargetStation;
    internal bool HasRoamTargetForVerification => hasRoamTarget;
    internal void SelectRoamDestinationForVerification()
    {
        SelectRoamDestination();
    }
    internal void SetRoamTargetStationForVerification(GymExerciseStation station)
    {
        SelectRoamDestination();
        roamTargetStation = station;
        roamTarget = station.EnemyPosition;
        hasRoamTarget = true;
        roamState = RoamState.Walking;
        ClearRoamRoute();
    }

    private void SelectRoamDestination()
    {
        roamState = RoamState.Walking;
        stalledRoamTime = 0f;
        pendingTreadmillStation = null;
        roamTargetStation = null;
        hasRoamTargetArrivalRotation = false;
        ClearRoamRoute();
        roamDirection = Vector3.zero;
        roamDirectionHoldUntil = 0f;
        roamSpeed = Random.Range(roamSpeedMin, roamSpeedMax);

        // Ronnie is a police/intervention NPC, not a gym customer. Keep his
        // neutral patrol on clear room points so a machine interest (most
        // visibly the Smith machine) can never become his startup destination.
        if (isPolice)
        {
            if (TryFindPoliceRoomDestination(out Vector3 policeDestination))
            {
                roamTarget = policeDestination;
                hasRoamTarget = true;
                roamTargetPurposeful = false;
                roamTargetInterestLabel = "police room patrol";
                lastRoamTarget = roamTarget;
                hasLastRoamTarget = true;
                BuildRoamRouteToTarget(false);
                return;
            }

            BeginRoamIdle();
            return;
        }

        if (Time.time >= nextTreadmillDecisionTime)
        {
            nextTreadmillDecisionTime = Time.time + Random.Range(10f, 19f);
            if (Random.value < 0.34f)
            {
                GymExerciseStation treadmill =
                    GymExerciseStation.FindClosestTreadmill(transform.position, 28f);
                if (treadmill != null)
                {
                    pendingTreadmillStation = treadmill;
                    roamTargetStation = treadmill;
                    roamTarget = TryFindTreadmillApproachPoint(
                            treadmill, out Vector3 treadmillApproachPoint)
                        ? treadmillApproachPoint
                        : treadmill.EnemyPosition;
                    hasRoamTarget = true;
                    roamTargetPurposeful = true;
                    roamTargetInterestLabel = treadmill.DisplayName;
                    roamTargetArrivalRotation = treadmill.EnemyRotation;
                    hasRoamTargetArrivalRotation = true;
                    BuildRoamRouteToTarget(ShouldAllowTargetEquipment());
                    return;
                }
            }
        }

        if (Random.value < roamRandomDestinationChance &&
            TryFindRareRandomDestination(out Vector3 randomDestination))
        {
            roamTarget = randomDestination;
            hasRoamTarget = true;
            roamTargetPurposeful = false;
            roamTargetInterestLabel = "rare random room destination";
            lastRoamTarget = roamTarget;
            hasLastRoamTarget = true;
            BuildRoamRouteToTarget(false);
            return;
        }

        hasRoamTarget = TryFindRoamPoint(out roamTarget);
        if (hasRoamTarget)
        {
            lastRoamTarget = roamTarget;
            hasLastRoamTarget = true;
            BuildRoamRouteToTarget(ShouldAllowTargetEquipment());
        }
        else if (identity == BodybuilderIdentity.JayCutler &&
                 TryFindJayEmergencyDestination(
                     out Vector3 jayDestination, out string jayLabel))
        {
            // Jay must never get stuck in the long no-waypoint idle used only
            // when a room is genuinely saturated. Give him one more clear,
            // purposeful destination search before allowing that fallback.
            roamTarget = jayDestination;
            hasRoamTarget = true;
            roamTargetPurposeful = !string.IsNullOrEmpty(jayLabel);
            roamTargetInterestLabel = jayLabel;
            lastRoamTarget = roamTarget;
            hasLastRoamTarget = true;
            BuildRoamRouteToTarget(ShouldAllowTargetEquipment());
        }
        else
        {
            // Only enter a long idle if the room currently has no valid
            // waypoint at all. Ordinary route changes always stay walking.
            BeginRoamIdle();
        }
    }
    private void ClearRoamRoute()
    {
        roamRouteWaypoints.Clear();
        roamRouteIndex = 0;
    }
    private bool ShouldAllowTargetEquipment()
    {
        if (roamTargetStation == null)
        {
            return false;
        }

        // Treadmill roaming targets are normally an open approach point. Only
        // the authored belt center is allowed to overlap the treadmill, and
        // that overlap is handled by TryBeginTreadmillVisit after arrival.
        if (roamTargetStation.IsTreadmill)
        {
            float distanceToBelt = Vector3.ProjectOnPlane(
                roamTarget - roamTargetStation.EnemyPosition, Vector3.up).magnitude;
            return distanceToBelt <= 0.82f;
        }

        return true;
    }
    private Vector3 GetRoamSteeringTarget()
    {
        return roamRouteIndex < roamRouteWaypoints.Count
            ? roamRouteWaypoints[roamRouteIndex]
            : roamTarget;
    }
    private void AdvanceRoamRoute()
    {
        while (roamRouteIndex < roamRouteWaypoints.Count)
        {
            Vector3 waypoint = roamRouteWaypoints[roamRouteIndex];
            float distance = Vector3.ProjectOnPlane(
                waypoint - transform.position, Vector3.up).magnitude;
            float arrivalDistance = roamRouteIndex == roamRouteWaypoints.Count - 1
                ? 0.52f : 0.72f;
            if (distance > arrivalDistance)
            {
                break;
            }

            roamRouteIndex++;
        }
    }
    private bool BuildRoamRouteToTarget(bool allowTargetEquipment)
    {
        using var profileScope = BuildRoamRouteMarker.Auto();
        ClearRoamRoute();
        if (!hasRoamTarget || !TryGetRoomBounds(out Bounds floorBounds))
        {
            return false;
        }

        Vector3 start = body != null ? body.position : transform.position;
        Vector3 goal = roamTarget;
        goal.y = floorBounds.max.y;
        Vector3 direct = Vector3.ProjectOnPlane(goal - start, Vector3.up);
        if (direct.sqrMagnitude < 1.2f * 1.2f)
        {
            return IsDeadliftNavigationSegmentClear(start, goal);
        }
        if (!allowTargetEquipment && IsNavigationSegmentClear(start, goal, false))
        {
            return true;
        }

        const float cellSize = 1.2f;
        float margin = GetBodyRadius() + 0.28f;
        float minX = floorBounds.min.x + margin;
        float maxX = floorBounds.max.x - margin;
        float minZ = floorBounds.min.z + margin;
        float maxZ = floorBounds.max.z - margin;
        int width = Mathf.Clamp(Mathf.FloorToInt((maxX - minX) / cellSize) + 1, 4, 48);
        int height = Mathf.Clamp(Mathf.FloorToInt((maxZ - minZ) / cellSize) + 1, 4, 48);
        int nodeCount = width * height;
        int goalIndex = nodeCount;

        bool[] navigable = new bool[nodeCount];
        int navigablePointCount = 0;
        Vector3[] points = new Vector3[nodeCount];
        for (int z = 0; z < height; z++)
        {
            for (int x = 0; x < width; x++)
            {
                int index = z * width + x;
                points[index] = new Vector3(
                    Mathf.Min(minX + x * cellSize, maxX),
                    floorBounds.max.y,
                    Mathf.Min(minZ + z * cellSize, maxZ));
                navigable[index] = IsNavigationPointClear(points[index]);
                if (navigable[index])
                {
                    navigablePointCount++;
                }
            }
        }

        int startIndex = FindClosestNavigableNode(points, navigable, start);
        if (startIndex < 0)
        {
            if (identity == BodybuilderIdentity.Goku)
            {
                Debug.LogWarning($"GYMCHAOS_ROAM_ROUTE_FAILED stage=no_start navigable={navigablePointCount}/{nodeCount} start={start} goal={goal}", this);
            }
            return false;
        }

        float[] gScores = new float[nodeCount + 1];
        float[] fScores = new float[nodeCount + 1];
        int[] cameFrom = new int[nodeCount + 1];
        bool[] closed = new bool[nodeCount + 1];
        List<int> open = new List<int>();
        for (int i = 0; i < gScores.Length; i++)
        {
            gScores[i] = float.PositiveInfinity;
            fScores[i] = float.PositiveInfinity;
            cameFrom[i] = -1;
        }

        gScores[startIndex] = 0f;
        fScores[startIndex] = Vector3.ProjectOnPlane(
            goal - points[startIndex], Vector3.up).magnitude;
        open.Add(startIndex);
        bool foundGoal = false;
        int exploredNodeCount = 0;

        while (open.Count > 0)
        {
            int current = open[0];
            float bestOpenScore = fScores[current];
            for (int i = 1; i < open.Count; i++)
            {
                int candidate = open[i];
                if (fScores[candidate] < bestOpenScore)
                {
                    bestOpenScore = fScores[candidate];
                    current = candidate;
                }
            }
            open.Remove(current);
            if (current == goalIndex)
            {
                foundGoal = true;
                break;
            }
            if (closed[current])
            {
                continue;
            }
            closed[current] = true;
            exploredNodeCount++;

            Vector3 currentPoint = points[current];
            float goalDistance = Vector3.ProjectOnPlane(
                goal - currentPoint, Vector3.up).magnitude;
            if (goalDistance <= cellSize * 3.4f &&
                IsNavigationSegmentClear(currentPoint, goal, allowTargetEquipment))
            {
                float goalScore = gScores[current] + goalDistance;
                if (goalScore < gScores[goalIndex])
                {
                    cameFrom[goalIndex] = current;
                    gScores[goalIndex] = goalScore;
                    fScores[goalIndex] = goalScore;
                    if (!open.Contains(goalIndex))
                    {
                        open.Add(goalIndex);
                    }
                }
            }

            for (int directionIndex = 0;
                 directionIndex < NavigationNeighborX.Length;
                 directionIndex++)
            {
                int currentX = current % width;
                int currentZ = current / width;
                int nextX = currentX + NavigationNeighborX[directionIndex];
                int nextZ = currentZ + NavigationNeighborZ[directionIndex];
                if (nextX < 0 || nextX >= width || nextZ < 0 || nextZ >= height)
                {
                    continue;
                }

                int next = nextZ * width + nextX;
                if (!navigable[next] || closed[next] ||
                    !IsNavigationSegmentClear(currentPoint, points[next], false))
                {
                    continue;
                }

                float stepDistance = Vector3.ProjectOnPlane(
                    points[next] - currentPoint, Vector3.up).magnitude;
                float tentativeScore = gScores[current] + stepDistance;
                if (tentativeScore >= gScores[next])
                {
                    continue;
                }

                cameFrom[next] = current;
                gScores[next] = tentativeScore;
                fScores[next] = tentativeScore + Vector3.ProjectOnPlane(
                    goal - points[next], Vector3.up).magnitude;
                if (!open.Contains(next))
                {
                    open.Add(next);
                }
            }
        }

        if (!foundGoal || cameFrom[goalIndex] < 0)
        {
            if (identity == BodybuilderIdentity.Goku)
            {
                Debug.LogWarning($"GYMCHAOS_ROAM_ROUTE_FAILED stage=no_goal navigable={navigablePointCount}/{nodeCount} explored={exploredNodeCount} start={start} goal={goal} blocker={lastVisitorRouteBlocker}", this);
            }
            return false;
        }

        List<Vector3> rawRoute = new List<Vector3>();
        int routeNode = cameFrom[goalIndex];
        while (routeNode >= 0 && routeNode != startIndex)
        {
            rawRoute.Add(points[routeNode]);
            routeNode = cameFrom[routeNode];
        }
        rawRoute.Reverse();
        rawRoute.Add(goal);

        // Collapse visible, collinear grid steps. The route still preserves
        // the A* detour around machines, but the actor does not make a small
        // left/right correction on every cell.
        Vector3 anchor = start;
        int rawIndex = 0;
        while (rawIndex < rawRoute.Count)
        {
            int furthestVisible = rawIndex;
            for (int candidateIndex = rawRoute.Count - 1;
                 candidateIndex > rawIndex;
                 candidateIndex--)
            {
                bool isFinalTarget = candidateIndex == rawRoute.Count - 1;
                if (IsNavigationSegmentClear(
                        anchor, rawRoute[candidateIndex],
                        isFinalTarget && allowTargetEquipment))
                {
                    furthestVisible = candidateIndex;
                    break;
                }
            }

            roamRouteWaypoints.Add(rawRoute[furthestVisible]);
            anchor = rawRoute[furthestVisible];
            rawIndex = furthestVisible + 1;
        }

        return roamRouteWaypoints.Count > 0;
    }
    private bool TryFindTreadmillApproachPoint(
        GymExerciseStation station, out Vector3 point)
    {
        point = station != null ? station.EnemyPosition : transform.position;
        if (station == null || !TryGetRoomBounds(out Bounds floorBounds))
        {
            return false;
        }

        Vector3 center = station.EnemyPosition;
        center.y = floorBounds.max.y;
        Vector3 preferredDirection = Vector3.ProjectOnPlane(
            station.EnemyRotation * Vector3.back, Vector3.up);
        if (preferredDirection.sqrMagnitude < 0.01f)
        {
            preferredDirection = Vector3.back;
        }
        preferredDirection.Normalize();

        float margin = GetBodyRadius() + 0.28f;
        float bestScore = float.NegativeInfinity;
        bool found = false;
        // Search a ring outside the treadmill rather than asking the route
        // planner to finish inside its belt collider. The preferred side is
        // the rear/aisle side, but the full ring lets differently oriented
        // imported treadmill assets choose whichever side is actually open.
        float[] radii = { 2.05f, 2.45f, 2.9f, 3.35f, 4.1f, 5.0f, 6.0f };
        for (int radiusIndex = 0; radiusIndex < radii.Length; radiusIndex++)
        {
            float radius = radii[radiusIndex];
            for (int sample = 0; sample < 24; sample++)
            {
                float angle = sample * 15f;
                Vector3 direction = Quaternion.Euler(0f, angle, 0f) *
                    preferredDirection;
                Vector3 candidate = center + direction * radius;
                candidate.x = Mathf.Clamp(
                    candidate.x, floorBounds.min.x + margin, floorBounds.max.x - margin);
                candidate.z = Mathf.Clamp(
                    candidate.z, floorBounds.min.z + margin, floorBounds.max.z - margin);

                float edgeClearance = Mathf.Min(
                    candidate.x - floorBounds.min.x,
                    floorBounds.max.x - candidate.x,
                    candidate.z - floorBounds.min.z,
                    floorBounds.max.z - candidate.z);
                if (edgeClearance < 1.15f || !IsRoamPointClear(candidate))
                {
                    continue;
                }

                float preferredAlignment = Vector3.Dot(direction, preferredDirection);
                float score = preferredAlignment * 2.4f +
                    Mathf.Min(edgeClearance, 5f) * 0.65f - radius * 0.12f;
                if (score <= bestScore)
                {
                    continue;
                }

                bestScore = score;
                point = candidate;
                found = true;
            }
        }

        return found;
    }
    private bool TryFindJayEmergencyDestination(out Vector3 point, out string label)
    {
        point = transform.position;
        label = null;
        if (!TryGetRoomBounds(out Bounds floorBounds))
        {
            return false;
        }

        float margin = GetBodyRadius() + 0.24f;
        CollectRoamInterests();
        RoamInterest jayInterest;
        if (TryFindPurposefulRoamPoint(
                floorBounds, margin, 2.4f, out point, out label, out jayInterest))
        {
            ApplySelectedRoamInterest(jayInterest);
            return true;
        }

        for (int attempt = 0; attempt < 80; attempt++)
        {
            point = new Vector3(
                Random.Range(floorBounds.min.x + margin, floorBounds.max.x - margin),
                floorBounds.max.y,
                Random.Range(floorBounds.min.z + margin, floorBounds.max.z - margin));
            if (Vector3.ProjectOnPlane(point - transform.position, Vector3.up).sqrMagnitude < 5.5f ||
                !IsRoamPointClear(point))
            {
                continue;
            }

            label = "Jay clear route fallback";
            return true;
        }

        return false;
    }
    private bool TryFindRareRandomDestination(out Vector3 point)
    {
        point = transform.position;
        if (!TryGetRoomBounds(out Bounds floorBounds))
        {
            return false;
        }

        float margin = GetBodyRadius() + 0.24f;
        float roomScale = Mathf.Min(floorBounds.size.x, floorBounds.size.z);
        float minimumDistance = Mathf.Clamp(roomScale * 0.2f, 6f, 9f);
        for (int attempt = 0; attempt < 96; attempt++)
        {
            point = new Vector3(
                Random.Range(floorBounds.min.x + margin, floorBounds.max.x - margin),
                floorBounds.max.y,
                Random.Range(floorBounds.min.z + margin, floorBounds.max.z - margin));
            float distance = Vector3.ProjectOnPlane(
                point - transform.position, Vector3.up).magnitude;
            float edgeClearance = Mathf.Min(
                point.x - floorBounds.min.x,
                floorBounds.max.x - point.x,
                point.z - floorBounds.min.z,
                floorBounds.max.z - point.z);
            if (distance < minimumDistance || edgeClearance < 2.1f ||
                (hasLastRoamTarget &&
                 Vector3.ProjectOnPlane(point - lastRoamTarget, Vector3.up).magnitude < 5.5f) ||
                !IsRoamPointClear(point))
            {
                continue;
            }

            return true;
        }

        return false;
    }
    private bool TryFindRoamPoint(out Vector3 point)
    {
        using var profileScope = FindRoamPointMarker.Auto();
        roamTargetPurposeful = false;
        roamTargetInterestLabel = null;
        roamTargetStation = null;
        hasRoamTargetArrivalRotation = false;

        if (!TryGetRoomBounds(out Bounds floorBounds))
        {
            point = transform.position;
            return true;
        }

        float margin = GetBodyRadius() + 0.24f;
        float roomScale = Mathf.Min(floorBounds.size.x, floorBounds.size.z);
        float minimumDistance = Mathf.Clamp(roomScale * 0.18f, 5.5f, 8.5f);

        // A meaningful target is an area around a real piece of equipment,
        // reception/lockers, or another person. This keeps the long route
        // commitment from sending a character toward an arbitrary empty
        // corner just because that point happened to score well on the floor.
        CollectRoamInterests();
        RoamInterest purposefulInterest;
        string purposefulLabel;
        if (TryFindPurposefulRoamPoint(
                floorBounds, margin, minimumDistance,
                out point, out purposefulLabel, out purposefulInterest) ||
            TryFindPurposefulRoamPoint(
                floorBounds, margin, Mathf.Max(3.8f, minimumDistance * 0.58f),
                out point, out purposefulLabel, out purposefulInterest))
        {
            roamTargetPurposeful = true;
            roamTargetInterestLabel = purposefulLabel;
            ApplySelectedRoamInterest(purposefulInterest);
            return true;
        }

        // If every useful object is temporarily surrounded by other bodies,
        // preserve motion with a clear open-floor fallback. It is only used
        // when no purposeful endpoint can currently be reached; it still
        // avoids walls, equipment, characters, and the previous endpoint.
        float minimumPreviousTargetDistance = Mathf.Max(5.5f, minimumDistance * 0.75f);
        float bestScore = float.NegativeInfinity;
        Vector3 bestPoint = transform.position;
        bool foundPoint = false;

        for (int attempt = 0; attempt < 56; attempt++)
        {
            point = new Vector3(
                Random.Range(floorBounds.min.x + margin, floorBounds.max.x - margin),
                floorBounds.max.y,
                Random.Range(floorBounds.min.z + margin, floorBounds.max.z - margin));

            float distance = Vector3.ProjectOnPlane(point - transform.position, Vector3.up).magnitude;
            float edgeClearance = Mathf.Min(
                point.x - floorBounds.min.x,
                floorBounds.max.x - point.x,
                point.z - floorBounds.min.z,
                floorBounds.max.z - point.z);
            if (distance < minimumDistance || edgeClearance < 1.8f)
            {
                continue;
            }

            float previousTargetDistance = hasLastRoamTarget
                ? Vector3.ProjectOnPlane(point - lastRoamTarget, Vector3.up).magnitude
                : float.PositiveInfinity;
            if (hasLastRoamTarget && previousTargetDistance < minimumPreviousTargetDistance)
            {
                continue;
            }

            if (!IsRoamPointClear(point))
            {
                continue;
            }

            // Prefer a waypoint in a different part of the room and away
            // from the previous endpoint, with a small random component so
            // the patrol does not select the same edge every time.
            float score = distance * 0.34f +
                (hasLastRoamTarget ? previousTargetDistance * 0.22f : 0f) +
                Mathf.Min(edgeClearance, 5f) * 0.9f +
                Random.Range(0f, 2.5f);
            if (score > bestScore)
            {
                bestScore = score;
                bestPoint = point;
                foundPoint = true;
            }
        }

        if (foundPoint)
        {
            point = bestPoint;
            roamTargetInterestLabel = "clear room fallback";
            return true;
        }

        // If machines temporarily occupy most valid room-wide samples, relax
        // only the distance constraint. Keep endpoint and body clearance
        // checks intact rather than falling back to an obstructed point.
        for (int attempt = 0; attempt < 32; attempt++)
        {
            point = new Vector3(
                Random.Range(floorBounds.min.x + margin, floorBounds.max.x - margin),
                floorBounds.max.y,
                Random.Range(floorBounds.min.z + margin, floorBounds.max.z - margin));
            if (Vector3.ProjectOnPlane(point - transform.position, Vector3.up).sqrMagnitude < 16f ||
                !IsRoamPointClear(point))
            {
                continue;
            }

            float edgeClearance = Mathf.Min(
                point.x - floorBounds.min.x,
                floorBounds.max.x - point.x,
                point.z - floorBounds.min.z,
                floorBounds.max.z - point.z);
            if (edgeClearance < 1.2f)
            {
                continue;
            }

            if (hasLastRoamTarget &&
                Vector3.ProjectOnPlane(point - lastRoamTarget, Vector3.up).magnitude < 4f)
            {
                continue;
            }

            roamTargetInterestLabel = "clear room fallback";
            return true;
        }

        point = transform.position;
        point.y = floorBounds.max.y;
        return false;
    }
    private void CollectRoamInterests()
    {
        using var profileScope = CollectRoamInterestsMarker.Auto();
        roamInterests.Clear();
        roamInterestRoots.Clear();
        collectedMachineInterestCount = 0;
        collectedPersonnelInterestCount = 0;
        collectedReceptionInterest = false;
        collectedPlayerInterest = false;

        // Stations are authoritative for the exercise assets. Their helper
        // object is placed at the authored machine center, even when the
        // imported model's child names are inconsistent.
        GymExerciseStation[] stations =
            FindObjectsByType<GymExerciseStation>(FindObjectsSortMode.None);
        for (int i = 0; i < stations.Length; i++)
        {
            GymExerciseStation station = stations[i];
            if (station == null || station.transform == null)
            {
                continue;
            }

            Transform stationParent = station.transform.parent;
            if (stationParent != null)
            {
                // Mark the imported machine root so the renderer scan below
                // cannot add a second, overlapping interest for the same
                // station. Separate stations under one section still retain
                // their own authored center points.
                roamInterestRoots.Add(stationParent);
            }

            if (station.IsSquat)
            {
                // Squat cages and the Smith machine are scheduled workout
                // destinations owned by GymVisitorDirector. They must never
                // be selected as ordinary free-roam interests, otherwise an
                // enemy can pace beside a rack without ever starting a squat.
                continue;
            }
            if (station.IsDeadlift)
            {
                // The deadlift station is player-only. Its bar and loose
                // plates are deliberately physical/pickable, so sending a
                // roaming enemy to the interaction point would let its body
                // wedge into the platform even though the station is not a
                // visitor exercise target.
                continue;
            }

            float footprint = station.IsTreadmill ? 2.5f
                : station.IsCardio ? 2.2f : 2.9f;
            Bounds stationBounds = new Bounds(
                station.transform.position,
                new Vector3(footprint, 2.0f, footprint));
            AddRoamInterest(
                station.transform, stationBounds, station.transform.position,
                false, 1.35f, station.DisplayName,
                station, true, station.PlayerPosition,
                station.EnemyRotation, true);
            collectedMachineInterestCount++;
        }

        // Cover meaningful imported/static objects that are not exercise
        // stations, such as the generated reception desk and lockers.
        Renderer[] renderers = FindObjectsByType<Renderer>(FindObjectsSortMode.None);
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null || !renderer.enabled ||
                renderer is ParticleSystemRenderer ||
                renderer.GetComponentInParent<PlayerMovement>() != null ||
                renderer.GetComponentInParent<EnemyFighter>() != null ||
                GymLooseItemSpawner.IsDeadliftStationObject(renderer.transform))
            {
                continue;
            }

            Transform root = FindPurposefulRoamRoot(renderer.transform);
            if (root == null || roamInterestRoots.Contains(root))
            {
                continue;
            }

            if (!TryGetRoamInterestBounds(root, out Bounds bounds) ||
                bounds.size.x > 9.5f || bounds.size.z > 9.5f)
            {
                continue;
            }

            AddRoamInterest(
                root, bounds, bounds.center, false, 1.15f, root.name);
        }

        // Personnel are moving points of interest, but they are only used as
        // neutral roaming destinations. Combat targeting remains entirely in
        // the aggressive/police state machine above.
        for (int i = 0; i < Fighters.Count; i++)
        {
            EnemyFighter other = Fighters[i];
            if (other == null || other == this || other.isDead ||
                other.transform == null)
            {
                continue;
            }

            Vector3 position = other.transform.position;
            Bounds personnelBounds = new Bounds(
                position + Vector3.up * 1f,
                new Vector3(1.1f, 2.2f, 1.1f));
            AddRoamInterest(
                other.transform, personnelBounds, position,
                true, 1.5f, other.identity.ToString());
            collectedPersonnelInterestCount++;
            if (other.isPassive || other.identity == BodybuilderIdentity.Manwithsuit1)
            {
                collectedReceptionInterest = true;
            }
        }

        if (playerTarget == null)
        {
            playerTarget = FindAnyObjectByType<PlayerMovement>();
        }
        if (playerTarget != null && !playerTarget.IsDead)
        {
            Vector3 position = playerTarget.transform.position;
            Bounds playerBounds = new Bounds(
                position + Vector3.up * 1f,
                new Vector3(1.2f, 2.0f, 1.2f));
            AddRoamInterest(
                playerTarget.transform, playerBounds, position,
                true, 1.6f, "Player");
            collectedPlayerInterest = true;
        }
    }
    private bool TryFindPurposefulRoamPoint(
        Bounds floorBounds, float margin, float minimumDistance,
        out Vector3 point, out string label, out RoamInterest selectedInterest)
    {
        using var profileScope = FindPurposefulRoamMarker.Auto();
        point = transform.position;
        label = null;
        selectedInterest = null;
        if (roamInterests.Count == 0)
        {
            return false;
        }

        float minimumPreviousTargetDistance = Mathf.Max(4.5f, minimumDistance * 0.7f);
        float bestScore = float.NegativeInfinity;
        bool foundPoint = false;
        string bestLabel = null;
        RoamInterest bestInterest = null;

        for (int i = 0; i < roamInterests.Count; i++)
        {
            RoamInterest interest = roamInterests[i];
            if (interest == null || interest.root == null)
            {
                continue;
            }
            if (interest.station != null && interest.station.IsDeadlift)
            {
                continue;
            }
            if (interest.station != null && interest.station.IsTreadmill &&
                !interest.station.IsAvailableForEnemy(this))
            {
                continue;
            }
            if (interest.station != null && IsStationClaimedByOther(interest.station))
            {
                continue;
            }

            int samples = interest.useInteractionPoint ? 1 : (interest.personnel ? 6 : 8);
            for (int sample = 0; sample < samples; sample++)
            {
                Vector2 randomDirection = Random.insideUnitCircle;
                if (randomDirection.sqrMagnitude < 0.08f)
                {
                    randomDirection = Random.insideUnitCircle.normalized;
                }
                if (randomDirection.sqrMagnitude < 0.01f)
                {
                    randomDirection = Vector2.right;
                }
                randomDirection.Normalize();

                Vector3 candidate;
                if (interest.station != null && interest.station.IsTreadmill)
                {
                    if (!TryFindTreadmillApproachPoint(
                            interest.station, out candidate))
                    {
                        continue;
                    }
                }
                else if (interest.useInteractionPoint)
                {
                    // Stations expose an authored interaction point. Use it
                    // directly so walking to a machine has a coherent
                    // approach pose and a treadmill visit can start there.
                    candidate = interest.interactionPoint;
                }
                else
                {
                    float footprint = interest.personnel
                        ? 1.15f
                        : Mathf.Clamp(
                            Mathf.Max(interest.bounds.extents.x, interest.bounds.extents.z),
                            0.9f, 3.2f);
                    float ringDistance = footprint + GetBodyRadius() +
                        Random.Range(0.65f, interest.personnel ? 1.35f : 2.15f);
                    candidate = interest.position +
                        new Vector3(randomDirection.x, 0f, randomDirection.y) * ringDistance;
                }
                candidate.y = floorBounds.max.y;
                candidate.x = Mathf.Clamp(
                    candidate.x, floorBounds.min.x + margin, floorBounds.max.x - margin);
                candidate.z = Mathf.Clamp(
                    candidate.z, floorBounds.min.z + margin, floorBounds.max.z - margin);

                float distance = Vector3.ProjectOnPlane(
                    candidate - transform.position, Vector3.up).magnitude;
                bool endpointClear = interest.station != null
                    ? IsNavigationPointClearForStation(candidate, interest.station)
                    : IsRoamPointClear(candidate);
                if (distance < minimumDistance || !endpointClear)
                {
                    continue;
                }

                float previousDistance = hasLastRoamTarget
                    ? Vector3.ProjectOnPlane(candidate - lastRoamTarget, Vector3.up).magnitude
                    : float.PositiveInfinity;
                if (hasLastRoamTarget && previousDistance < minimumPreviousTargetDistance)
                {
                    continue;
                }

                float edgeClearance = Mathf.Min(
                    candidate.x - floorBounds.min.x,
                    floorBounds.max.x - candidate.x,
                    candidate.z - floorBounds.min.z,
                    floorBounds.max.z - candidate.z);
                float minimumEdgeClearance = interest.useInteractionPoint
                    ? 0.9f
                    : (interest.personnel ? 1.25f : 1.7f);
                if (edgeClearance < minimumEdgeClearance)
                {
                    continue;
                }

                float score = distance * 0.32f +
                    (hasLastRoamTarget ? previousDistance * 0.18f : 0f) +
                    Mathf.Min(edgeClearance, 5f) * 1.1f +
                    interest.weight * 2f + Random.Range(0f, 2.25f);
                if (score <= bestScore)
                {
                    continue;
                }

                bestScore = score;
                point = candidate;
                bestLabel = interest.label;
                bestInterest = interest;
                foundPoint = true;
            }
        }

        if (!foundPoint)
        {
            return false;
        }

        label = bestLabel;
        selectedInterest = bestInterest;
        return true;
    }
    private void AddRoamInterest(
        Transform root, Bounds bounds, Vector3 position,
        bool personnel, float weight, string label,
        GymExerciseStation station = null, bool useInteractionPoint = false,
        Vector3 interactionPoint = default, Quaternion arrivalRotation = default,
        bool hasArrivalRotation = false)
    {
        if (root == null || roamInterestRoots.Contains(root))
        {
            return;
        }

        roamInterestRoots.Add(root);
        bounds.center = new Vector3(bounds.center.x, position.y, bounds.center.z);
        roamInterests.Add(new RoamInterest
        {
            root = root,
            bounds = bounds,
            position = position,
            interactionPoint = interactionPoint,
            arrivalRotation = arrivalRotation,
            station = station,
            useInteractionPoint = useInteractionPoint,
            hasArrivalRotation = hasArrivalRotation,
            personnel = personnel,
            weight = weight,
            label = string.IsNullOrEmpty(label) ? root.name : label
        });
    }
    private void ApplySelectedRoamInterest(RoamInterest interest)
    {
        roamTargetStation = interest != null ? interest.station : null;
        if (roamTargetStation != null && roamTargetStation.IsTreadmill &&
            roamTargetStation.IsAvailableForEnemy(this))
        {
            pendingTreadmillStation = roamTargetStation;
        }

        hasRoamTargetArrivalRotation = interest != null &&
            interest.hasArrivalRotation;
        if (hasRoamTargetArrivalRotation)
        {
            roamTargetArrivalRotation = interest.arrivalRotation;
        }
        else if (interest != null)
        {
            Vector3 lookDirection = Vector3.ProjectOnPlane(
                interest.position - roamTarget, Vector3.up);
            if (lookDirection.sqrMagnitude > 0.01f)
            {
                roamTargetArrivalRotation =
                    Quaternion.LookRotation(lookDirection.normalized, Vector3.up);
                hasRoamTargetArrivalRotation = true;
            }
        }
    }
    private static Transform FindPurposefulRoamRoot(Transform target)
    {
        for (Transform current = target; current != null; current = current.parent)
        {
            string lowerName = current.name.ToLowerInvariant();
            if (ContainsAny(lowerName, NonPurposefulRoamKeywords))
            {
                continue;
            }
            if (ContainsAny(lowerName, PurposefulRoamKeywords))
            {
                return current;
            }
        }

        return null;
    }
    private static bool TryGetRoamInterestBounds(Transform root, out Bounds bounds)
    {
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        bool found = false;
        bounds = default;
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null || !renderer.enabled ||
                renderer is ParticleSystemRenderer)
            {
                continue;
            }

            if (!found)
            {
                bounds = renderer.bounds;
                found = true;
            }
            else
            {
                bounds.Encapsulate(renderer.bounds);
            }
        }

        return found && bounds.size.x > 0.1f && bounds.size.z > 0.1f;
    }
    private Vector3 StabilizeRoamDirection(
        Vector3 desiredDirection, float targetDistance, Vector3 candidate,
        bool allowTargetEquipment)
    {
        desiredDirection = Vector3.ProjectOnPlane(desiredDirection, Vector3.up).normalized;
        float lookAhead = Mathf.Clamp(Mathf.Max(0.72f, targetDistance), 0.72f, 1.25f);
        bool hasHeldDirection = roamDirection.sqrMagnitude > 0.001f;
        bool heldDirectionClear = hasHeldDirection &&
            IsInsideRoom(roamDirection, lookAhead) &&
            IsMovementPathClear(roamDirection, lookAhead, allowTargetEquipment);

        if (heldDirectionClear && Time.time < roamDirectionHoldUntil)
        {
            return roamDirection;
        }

        if (candidate.sqrMagnitude < 0.001f)
        {
            return heldDirectionClear ? roamDirection : Vector3.zero;
        }

        candidate.Normalize();
        bool isObstacleDetour = Vector3.Dot(candidate, desiredDirection) < 0.985f;
        if (isObstacleDetour && heldDirectionClear &&
            Vector3.Dot(roamDirection, desiredDirection) > 0.2f &&
            Vector3.Dot(candidate, roamDirection) < 0.55f)
        {
            // Keep the current side of the obstacle briefly even after the
            // hold expires if the new probe would reverse the route.
            roamDirectionHoldUntil = Time.time + 0.36f;
            return roamDirection;
        }

        roamDirection = candidate;
        roamDirectionHoldUntil = Time.time + (isObstacleDetour ? 0.48f : 0.08f);
        return roamDirection;
    }
    private void BeginRoamIdle()
    {
        roamState = RoamState.Idle;
        hasRoamTarget = false;
        pendingTreadmillStation = null;
        roamTargetStation = null;
        roamTargetPurposeful = false;
        roamTargetInterestLabel = null;
        hasRoamTargetArrivalRotation = false;
        ClearRoamRoute();
        stalledRoamTime = 0f;
        roamDirection = Vector3.zero;
        roamDirectionHoldUntil = 0f;
        roamIdleUntil = Time.time + Random.Range(roamIdleMin, roamIdleMax);
        // End the movement and animation state in the same physics tick. The
        // old implementation waited for the next TickRoaming call, leaving
        // one short residual glide after Run had already switched to Idle.
        StopMovingPhysicsImmediately();
        SetAnimatedMovement(false);
    }
    private bool TryBeginTreadmillVisit(GymExerciseStation station)
    {
        if (station == null || body == null || isAggressive || isPassive || isDead)
        {
            return false;
        }

        bool runSession = Random.value >= 0.5f;
        treadmillMovementMode = runSession
            ? TreadmillMovementMode.Running
            : TreadmillMovementMode.Walking;
        float[] speeds = runSession
            ? new[] { 11.5f, 13.5f, 16f }
            : new[] { 4.5f, 6.5f, 8.5f };
        treadmillSpeed = speeds[Random.Range(0, speeds.Length)];
        if (!station.TryBeginEnemyTreadmill(this, treadmillSpeed))
        {
            treadmillSpeed = 0f;
            return false;
        }

        treadmillStation = station;
        treadmillEntryActive = true;
        treadmillEntryStarted = Time.time;
        treadmillEntryStartPosition = body.position;
        treadmillEntryStartRotation = body.rotation;
        float entryDistance = Vector3.ProjectOnPlane(
            station.EnemyPosition - body.position, Vector3.up).magnitude;
        treadmillEntryDuration = Mathf.Clamp(
            entryDistance / Mathf.Max(1.1f, roamSpeed), 0.85f, 1.65f);
        treadmillExitActive = false;
        treadmillUntil = Time.time + treadmillEntryDuration +
            Random.Range(15f, 26f);
        treadmillNextSpeedChangeTime = Time.time + treadmillEntryDuration +
            Random.Range(5.5f, 9.5f);

        if (roamTargetStation == station && hasRoamTarget &&
            Vector3.ProjectOnPlane(
                roamTarget - station.EnemyPosition, Vector3.up).sqrMagnitude > 0.7f * 0.7f)
        {
            treadmillExitTargetPosition = roamTarget;
        }
        else if (!TryFindTreadmillApproachPoint(
                     station, out treadmillExitTargetPosition))
        {
            Vector3 awayFromScreen = Vector3.ProjectOnPlane(
                station.EnemyRotation * Vector3.back, Vector3.up);
            if (awayFromScreen.sqrMagnitude < 0.01f)
            {
                awayFromScreen = Vector3.back;
            }

            treadmillExitTargetPosition = station.EnemyPosition +
                awayFromScreen.normalized * 2.2f;
        }
        treadmillExitTargetPosition.y = floorRootY;
        standingRootY = floorRootY;
        roamState = RoamState.Walking;
        StopMovingPhysicsImmediately();
        return true;
    }
    private void TickTreadmillEntry()
    {
        if (treadmillStation == null)
        {
            return;
        }

        if (!treadmillStation.TickEnemyTreadmill(
                this, Time.fixedDeltaTime, treadmillSpeed))
        {
            EndTreadmillVisit();
            SelectRoamDestination();
            return;
        }

        float progress = Mathf.Clamp01(
            (Time.time - treadmillEntryStarted) /
            Mathf.Max(0.01f, treadmillEntryDuration));
        float eased = SmoothStep(progress);
        Vector3 targetPosition = treadmillStation.EnemyPosition;
        body.position = Vector3.Lerp(
            treadmillEntryStartPosition, targetPosition, eased);
        body.rotation = Quaternion.Slerp(
            treadmillEntryStartRotation, treadmillStation.EnemyRotation, eased);
        StopMovingPhysicsImmediately();
        SetAnimatedMovement(
            true,
            Mathf.Lerp(
                Mathf.Clamp01(roamSpeed / Mathf.Max(0.01f, maxSpeed)),
                treadmillStation.TreadmillSpeed01(
                    treadmillStation.CurrentTreadmillSpeed),
                eased),
            treadmillMovementMode == TreadmillMovementMode.Running);

        if (progress < 1f)
        {
            return;
        }

        body.position = targetPosition;
        body.rotation = treadmillStation.EnemyRotation;
        standingRootY = targetPosition.y;
        treadmillEntryActive = false;
    }
    private void UpdateTreadmillTargetSpeed()
    {
        if (Time.time < treadmillNextSpeedChangeTime)
        {
            return;
        }

        float[] speeds = treadmillMovementMode == TreadmillMovementMode.Running
            ? new[] { 11.5f, 13.5f, 16f }
            : new[] { 4.5f, 6.5f, 8.5f };
        int currentSpeedIndex = 0;
        float closestSpeedDistance = float.PositiveInfinity;
        for (int i = 0; i < speeds.Length; i++)
        {
            float distance = Mathf.Abs(speeds[i] - treadmillSpeed);
            if (distance < closestSpeedDistance)
            {
                closestSpeedDistance = distance;
                currentSpeedIndex = i;
            }
        }

        int nextSpeedIndex = Random.Range(0, speeds.Length - 1);
        if (nextSpeedIndex >= currentSpeedIndex)
        {
            nextSpeedIndex++;
        }
        treadmillSpeed = speeds[nextSpeedIndex];
        // Keep each acceleration/deceleration phase long enough to read as a
        // deliberate pace change instead of a rapid idle/animation reset.
        treadmillNextSpeedChangeTime = Time.time + Random.Range(5.5f, 9.5f);
    }
    private void BeginTreadmillExit()
    {
        if (treadmillStation == null || treadmillExitActive)
        {
            return;
        }

        treadmillEntryActive = false;
        treadmillExitActive = true;
        treadmillExitStarted = Time.time;
        Vector3 startPosition = treadmillStation.EnemyPosition;
        Vector3 exitOffset = Vector3.ProjectOnPlane(
            treadmillExitTargetPosition - startPosition, Vector3.up);
        if (exitOffset.sqrMagnitude < 0.2f * 0.2f)
        {
            if (!TryFindTreadmillApproachPoint(
                    treadmillStation, out treadmillExitTargetPosition))
            {
                treadmillExitTargetPosition = startPosition +
                    Vector3.back * 2.2f;
            }
            treadmillExitTargetPosition.y = floorRootY;
            exitOffset = Vector3.ProjectOnPlane(
                treadmillExitTargetPosition - startPosition, Vector3.up);
        }

        treadmillExitTargetPosition.y = floorRootY;
        treadmillExitDuration = Mathf.Clamp(
            exitOffset.magnitude / Mathf.Max(1.1f, roamSpeed),
            0.95f, 1.8f);
    }
    private void TickTreadmillExit()
    {
        if (treadmillStation == null)
        {
            return;
        }

        float progress = Mathf.Clamp01(
            (Time.time - treadmillExitStarted) /
            Mathf.Max(0.01f, treadmillExitDuration));
        float eased = SmoothStep(progress);
        Vector3 startPosition = treadmillStation.EnemyPosition;
        body.position = Vector3.Lerp(
            startPosition, treadmillExitTargetPosition, eased);

        Vector3 exitDirection = Vector3.ProjectOnPlane(
            treadmillExitTargetPosition - startPosition, Vector3.up);
        Quaternion exitRotation = exitDirection.sqrMagnitude > 0.01f
            ? Quaternion.LookRotation(exitDirection.normalized, Vector3.up)
            : treadmillStation.EnemyRotation;
        body.rotation = Quaternion.Slerp(
            treadmillStation.EnemyRotation, exitRotation, eased);
        StopMovingPhysicsImmediately();
        SetAnimatedMovement(
            true, Mathf.Clamp01(roamSpeed / Mathf.Max(0.01f, maxSpeed)),
            treadmillMovementMode == TreadmillMovementMode.Running);

        if (progress < 1f)
        {
            return;
        }

        body.position = treadmillExitTargetPosition;
        body.rotation = exitRotation;
        EndTreadmillVisit();
        SelectRoamDestination();
    }
    private void EndTreadmillVisit()
    {
        if (treadmillStation != null)
        {
            treadmillStation.EndEnemyTreadmill(this);
        }

        treadmillStation = null;
        treadmillSpeed = 0f;
        treadmillUntil = 0f;
        treadmillEntryActive = false;
        treadmillEntryStarted = 0f;
        treadmillEntryDuration = 0f;
        treadmillEntryStartPosition = Vector3.zero;
        treadmillEntryStartRotation = Quaternion.identity;
        treadmillExitActive = false;
        treadmillExitStarted = 0f;
        treadmillExitDuration = 0f;
        treadmillExitTargetPosition = Vector3.zero;
        treadmillNextSpeedChangeTime = 0f;
        treadmillMovementMode = TreadmillMovementMode.Walking;
        standingRootY = floorRootY;
    }
    private void StopMovingPhysicsOnly()
    {
        if (body == null || body.isKinematic)
        {
            return;
        }

        Vector3 verticalVelocity = Vector3.Project(body.linearVelocity, Vector3.up);
        Vector3 planarVelocity = Vector3.ProjectOnPlane(body.linearVelocity, Vector3.up);
        body.linearVelocity = Vector3.Lerp(
            planarVelocity, Vector3.zero, 8f * Time.fixedDeltaTime) + verticalVelocity;
    }
    private void StopMovingPhysicsImmediately()
    {
        if (body == null || body.isKinematic)
        {
            return;
        }

        Vector3 verticalVelocity = Vector3.Project(body.linearVelocity, Vector3.up);
        body.linearVelocity = verticalVelocity;
        body.angularVelocity = Vector3.zero;
    }
    private void StopMoving()
    {
        if (IsGoku() && gokuFlightState != GokuFlightState.Grounded)
        {
            UpdateGokuFlight(false, transform.forward);
            SetAnimatedMovement(false);
            if (gokuFlightState != GokuFlightState.Grounded)
            {
                return;
            }
        }

        StopMovingPhysicsImmediately();
        SetAnimatedMovement(false);
    }
    private void KeepGroundedRoot()
    {
        if (body == null || body.isKinematic ||
            (IsGoku() && gokuFlightState != GokuFlightState.Grounded))
        {
            return;
        }

        // Configure() and SetGokuFlightPhysics(false) already freeze the
        // alive root on Y. Do not rewrite body.position or vertical velocity
        // every physics step: even a small corrective write fights Rigidbody
        // interpolation/contact solving and appears as a shake on each step.
        // Only restore the constraint if another alive-state system removed
        // it; this branch never runs for corpses because FixedUpdate exits at
        // the death guard before calling KeepGroundedRoot().
        RigidbodyConstraints groundedConstraints = body.constraints |
            RigidbodyConstraints.FreezePositionY;
        if (groundedConstraints != body.constraints)
        {
            body.constraints = groundedConstraints;
        }
    }
    private float ResolveGymFloorY(float fallback)
    {
        if (gymFloorRenderer == null)
        {
            GameObject floor = GameObject.Find("Rubber Floor");
            if (floor != null)
            {
                gymFloorRenderer = floor.GetComponent<Renderer>();
            }
        }

        return gymFloorRenderer != null ? gymFloorRenderer.bounds.max.y : fallback;
    }
    private float GetChaseSpeed()
    {
        return IsGoku() && gokuFlightState != GokuFlightState.Grounded
            ? maxSpeed * GokuSpeedMultiplier
            : maxSpeed;
    }
    private float GetDetectionRange()
    {
        if (isPolice || isAggressive)
        {
            // Ronnie and angered enemies follow activity across the room. A
            // neutral enemy never reaches this method because it is roaming.
            return float.PositiveInfinity;
        }

        return IsGoku() ? detectionRange * GokuSpeedMultiplier : detectionRange;
    }
}
