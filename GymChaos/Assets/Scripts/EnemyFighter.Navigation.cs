using System.Collections.Generic;
using UnityEngine;

public partial class EnemyFighter
{
    /// <summary>Closest navigable node (lowest index on ties), testing nodes
    /// nearest-first so only a few clearance queries run.</summary>
    private int FindClosestNavigableNode(
        Vector3[] points, System.Func<int, bool> isNavigable, Vector3 position)
    {
        int count = points.Length;
        float[] distances = new float[count];
        int[] order = new int[count];
        for (int i = 0; i < count; i++)
        {
            distances[i] = Vector3.ProjectOnPlane(points[i] - position, Vector3.up).sqrMagnitude;
            order[i] = i;
        }
        System.Array.Sort(order, (a, b) =>
        {
            int byDistance = distances[a].CompareTo(distances[b]);
            return byDistance != 0 ? byDistance : a.CompareTo(b);
        });
        for (int k = 0; k < count; k++)
        {
            if (isNavigable(order[k])) return order[k];
        }
        return -1;
    }
    private void RefreshDeadliftNavigationStations()
    {
        bool hasActiveStation = false;
        if (deadliftNavigationStations != null)
        {
            for (int index = 0; index < deadliftNavigationStations.Length; index++)
            {
                GymDeadliftStationMarker station = deadliftNavigationStations[index];
                if (station != null && station.isActiveAndEnabled)
                {
                    hasActiveStation = true;
                    break;
                }
            }
        }

        if (hasActiveStation || Time.frameCount - deadliftNavigationLookupFrame < 30)
        {
            return;
        }

        deadliftNavigationLookupFrame = Time.frameCount;
        deadliftNavigationStations = Object.FindObjectsByType<GymDeadliftStationMarker>(
            FindObjectsSortMode.None);
    }
    private bool IsDeadliftNavigationPointClear(Vector3 point)
    {
        RefreshDeadliftNavigationStations();
        float clearance = DeadliftNavigationClearance;
        if (deadliftNavigationStations == null)
        {
            return true;
        }

        for (int index = 0; index < deadliftNavigationStations.Length; index++)
        {
            GymDeadliftStationMarker station = deadliftNavigationStations[index];
            if (station != null && station.isActiveAndEnabled && station.ActiveLifter != this &&
                station.IsInsideNavigationClearance(point, clearance))
            {
                return false;
            }
        }

        return true;
    }
    private bool IsDeadliftNavigationSegmentClear(Vector3 from, Vector3 to)
    {
        RefreshDeadliftNavigationStations();
        float clearance = DeadliftNavigationClearance;
        if (deadliftNavigationStations == null)
        {
            return true;
        }

        Vector3 movement = Vector3.ProjectOnPlane(to - from, Vector3.up);
        if (movement.sqrMagnitude < 0.0025f)
        {
            return true;
        }

        for (int index = 0; index < deadliftNavigationStations.Length; index++)
        {
            GymDeadliftStationMarker station = deadliftNavigationStations[index];
            if (station == null || !station.isActiveAndEnabled || station.ActiveLifter == this)
            {
                continue;
            }

            bool startsInside = station.IsInsideNavigationClearance(from, clearance);
            bool endsInside = station.IsInsideNavigationClearance(to, clearance);
            if (startsInside)
            {
                if (!station.TryGetNavigationFootprint(out Bounds footprint))
                {
                    return false;
                }

                Vector3 localFrom = station.transform.InverseTransformPoint(from);
                Vector3 scale = station.transform.lossyScale;
                float clearanceX = clearance / Mathf.Max(0.001f, Mathf.Abs(scale.x));
                float clearanceZ = clearance / Mathf.Max(0.001f, Mathf.Abs(scale.z));
                float minX = footprint.min.x - clearanceX;
                float maxX = footprint.max.x + clearanceX;
                float minZ = footprint.min.z - clearanceZ;
                float maxZ = footprint.max.z + clearanceZ;
                float left = localFrom.x - minX;
                float right = maxX - localFrom.x;
                float front = localFrom.z - minZ;
                float back = maxZ - localFrom.z;
                float nearest = Mathf.Min(Mathf.Min(left, right), Mathf.Min(front, back));
                Vector3 localExit = localFrom;
                float safetyX = 0.35f / Mathf.Max(0.001f, Mathf.Abs(scale.x));
                float safetyZ = 0.35f / Mathf.Max(0.001f, Mathf.Abs(scale.z));
                if (nearest == left)
                {
                    localExit.x = minX - safetyX;
                }
                else if (nearest == right)
                {
                    localExit.x = maxX + safetyX;
                }
                else if (nearest == front)
                {
                    localExit.z = minZ - safetyZ;
                }
                else
                {
                    localExit.z = maxZ + safetyZ;
                }

                Vector3 exit = station.transform.TransformPoint(localExit);
                exit.y = from.y;
                Vector3 toExit = Vector3.ProjectOnPlane(exit - from, Vector3.up);
                if (toExit.sqrMagnitude < 0.0025f ||
                    Vector3.Dot(movement.normalized, toExit.normalized) < 0.35f ||
                    Vector3.Dot(movement, toExit.normalized) <= 0.01f)
                {
                    return false;
                }

                continue;
            }

            if (endsInside || station.DoesSegmentCrossNavigationClearance(
                    from, to, clearance))
            {
                return false;
            }
        }

        return true;
    }
    private bool IsNavigationPointClear(Vector3 point)
    {
        if (!IsDeadliftNavigationPointClear(point))
        {
            return false;
        }

        Vector3 lower = point + Vector3.up * VisitorProbeLower;
        Vector3 upper = point + Vector3.up * VisitorProbeUpper;
        int count = Physics.OverlapCapsuleNonAlloc(
            lower, upper, GetBodyRadius(), roamOverlapHits,
            ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            Collider hit = roamOverlapHits[i];
            if (hit == null || hit.transform == transform || hit.transform.IsChildOf(transform))
            {
                continue;
            }
            if (isPolice && GetRouteHitOwners(hit).Police)
            {
                continue;
            }
            EnemyFighter blockedFighter = GetRouteHitOwners(hit).Fighter;
            if (blockedFighter != null && blockedFighter == visitorCrowdPass)
            {
                continue;
            }
            if (blockedFighter != null && blockedFighter != this)
            {
                SetRouteBlocker(hit, blockedFighter.IdentityName);
                return false;
            }
            PlayerMovement blockedPlayer = GetRouteHitOwners(hit).Player;
            if (blockedPlayer != null)
            {
                SetRouteBlocker(hit, "Player");
                return false;
            }
            if (
                HasRoomFloorInHierarchy(hit.transform) ||
                IsWalkableFloorSurface(hit) ||
                hit.GetComponentInParent<GymExteriorOnlyVisual>() != null ||
                hit.name == "Player Road Access Blocker" ||
                hit.name == "Exterior Courtyard Foundation")
            {
                continue;
            }
            return false;
        }

        return true;
    }
    private bool IsNavigationSegmentClear(
        Vector3 from, Vector3 to, bool allowTargetEquipment)
    {
        using var profileScope = NavSegmentMarker.Auto();
        if (!IsDeadliftNavigationSegmentClear(from, to))
        {
            return false;
        }

        Vector3 delta = Vector3.ProjectOnPlane(to - from, Vector3.up);
        float distance = delta.magnitude;
        if (distance < 0.05f)
        {
            return true;
        }

        Vector3 direction = delta / distance;
        Vector3 lower = from + Vector3.up * VisitorProbeLower;
        Vector3 upper = from + Vector3.up * VisitorProbeUpper;
        int count = Physics.CapsuleCastNonAlloc(
            lower, upper, GetBodyRadius(), direction, movementHits,
            distance + 0.06f, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            Collider hit = movementHits[i].collider;
            if (hit == null || hit.transform == transform || hit.transform.IsChildOf(transform))
            {
                continue;
            }
            if (GetRouteHitOwners(hit).Fighter != null ||
                GetRouteHitOwners(hit).Player != null)
            {
                continue;
            }
            if (allowTargetEquipment && pendingTreadmillStation != null &&
                pendingTreadmillStation.ContainsEquipmentCollider(hit))
            {
                continue;
            }
            // Walking out of a cage whose colliders are still ignored for
            // this enemy: the probes must ignore that cage too, otherwise
            // every direction reads as blocked and the enemy never leaves.
            if (equipmentEgressStation != null &&
                equipmentEgressStation.ContainsEquipmentCollider(hit))
            {
                continue;
            }
            if (HasRoomFloorInHierarchy(hit.transform) ||
                IsWalkableFloorSurface(hit))
            {
                continue;
            }
            return false;
        }

        return true;
    }
    private bool IsNavigationPointClearForStation(
        Vector3 point, GymExerciseStation station)
    {
        if (!IsDeadliftNavigationPointClear(point))
        {
            return false;
        }

        Vector3 lower = point + Vector3.up * VisitorProbeLower;
        Vector3 upper = point + Vector3.up * VisitorProbeUpper;
        int count = Physics.OverlapCapsuleNonAlloc(
            lower, upper, GetBodyRadius(), roamOverlapHits,
            ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            Collider hit = roamOverlapHits[i];
            if (hit == null || hit.transform == transform || hit.transform.IsChildOf(transform))
            {
                continue;
            }
            if (GetRouteHitOwners(hit).Fighter != null ||
                GetRouteHitOwners(hit).Player != null ||
                HasRoomFloorInHierarchy(hit.transform) ||
                IsWalkableFloorSurface(hit))
            {
                continue;
            }
            if (station != null && station.ContainsEquipmentCollider(hit))
            {
                continue;
            }
            return false;
        }

        return true;
    }
    private static bool ContainsAny(string value, string[] fragments)
    {
        for (int i = 0; i < fragments.Length; i++)
        {
            if (value.Contains(fragments[i]))
            {
                return true;
            }
        }

        return false;
    }
    private bool IsRoamPointClear(Vector3 point)
    {
        if (!IsDeadliftNavigationPointClear(point))
        {
            return false;
        }

        Vector3 lower = point + Vector3.up * VisitorProbeLower;
        Vector3 upper = point + Vector3.up * VisitorProbeUpper;
        int count = Physics.OverlapCapsuleNonAlloc(
            lower, upper, GetBodyRadius(), roamOverlapHits,
            ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            Collider hit = roamOverlapHits[i];
            if (hit == null || hit.transform == transform || hit.transform.IsChildOf(transform))
            {
                continue;
            }
            if (GetRouteHitOwners(hit).Fighter != null)
            {
                return false;
            }
            if (GetRouteHitOwners(hit).Player != null)
            {
                return false;
            }
            if (HasRoomFloorInHierarchy(hit.transform))
            {
                continue;
            }
            if (IsWalkableFloorSurface(hit))
            {
                continue;
            }
            return false;
        }
        return true;
    }
    private Vector3 FindClearMovementDirection(
        Vector3 desiredDirection, float targetDistance, bool avoidCharacters,
        bool allowTargetEquipment)
    {
        desiredDirection = Vector3.ProjectOnPlane(desiredDirection, Vector3.up);
        if (desiredDirection.sqrMagnitude < 0.0001f)
        {
            return Vector3.zero;
        }
        desiredDirection.Normalize();

        if (avoidCharacters)
        {
            Vector3 separation = GetCharacterSeparation();
            if (separation.sqrMagnitude > 0.0001f)
            {
                // Preserve the separation magnitude. Normalizing it made even
                // a barely-nearby enemy apply a full-strength steering shove,
                // which produced the visible left/right indecision.
                desiredDirection = (desiredDirection + separation * 0.85f).normalized;
            }
        }

        float lookAhead = Mathf.Clamp(Mathf.Max(0.72f, targetDistance), 0.72f, 1.25f);
        // Score every probe first (cheap), then cast in descending score
        // order: the first clear probe is the best one. This used to cast all
        // ten probes each physics step, including the ones the alignment
        // filter always rejects.
        int candidateCount = 0;
        for (int i = 0; i < MovementProbeAngles.Length; i++)
        {
            Vector3 candidate = Quaternion.Euler(0f, MovementProbeAngles[i], 0f) * desiredDirection;
            float alignment = Vector3.Dot(candidate, desiredDirection);
            if (alignment < 0.48f ||
                (!chaseIgnoresRoomBounds && !IsInsideRoom(candidate, lookAhead)))
            {
                continue;
            }
            float score = alignment * 4f - Mathf.Abs(MovementProbeAngles[i]) * 0.002f;
            if (avoidCharacters && roamDirection.sqrMagnitude > 0.001f)
            {
                // When an obstacle requires a detour, prefer the previously
                // selected side so two equally valid probes do not alternate
                // on consecutive physics frames.
                score += Vector3.Dot(candidate, roamDirection.normalized) * 0.9f;
            }
            ProbeCandidates[candidateCount] = candidate;
            ProbeScores[candidateCount] = score;
            candidateCount++;
        }

        while (candidateCount > 0)
        {
            // Highest score; ties keep the earlier probe, as before.
            int bestIndex = 0;
            for (int i = 1; i < candidateCount; i++)
            {
                if (ProbeScores[i] > ProbeScores[bestIndex])
                {
                    bestIndex = i;
                }
            }
            Vector3 best = ProbeCandidates[bestIndex];
            if (IsMovementPathClear(best, lookAhead, allowTargetEquipment))
            {
                return best;
            }
            candidateCount--;
            for (int i = bestIndex; i < candidateCount; i++)
            {
                ProbeCandidates[i] = ProbeCandidates[i + 1];
                ProbeScores[i] = ProbeScores[i + 1];
            }
        }

        return Vector3.zero;
    }
    // Scratch for FindClearMovementDirection (main thread only). Sized as a
    // literal: static initializer order across partial files is undefined, so
    // MovementProbeAngles.Length may not be set yet here.
    private static readonly Vector3[] ProbeCandidates = new Vector3[16];
    private static readonly float[] ProbeScores = new float[16];
    private GymExerciseStation equipmentEgressStation;
    internal void SetEquipmentEgressStation(GymExerciseStation station)
    {
        equipmentEgressStation = station;
    }
    internal void ClearEquipmentEgressStation(GymExerciseStation station)
    {
        if (equipmentEgressStation == station)
        {
            equipmentEgressStation = null;
        }
    }
    private bool IsMovementPathClear(Vector3 direction, float distance, bool allowTargetEquipment)
    {
        Vector3 origin = body != null ? body.position : transform.position;
        Vector3 segmentEnd = origin + direction * distance;
        if (!IsDeadliftNavigationSegmentClear(origin, segmentEnd))
        {
            lastVisitorRouteBlocker = "deadlift station navigation clearance";
            return false;
        }
        Vector3 lower = origin + Vector3.up * VisitorProbeLower;
        Vector3 upper = origin + Vector3.up * VisitorProbeUpper;
        int count = Physics.CapsuleCastNonAlloc(
            lower, upper, GetBodyRadius(), direction, movementHits,
            distance + 0.08f, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            Collider hit = movementHits[i].collider;
            if (hit == null || hit.transform == transform || hit.transform.IsChildOf(transform))
            {
                continue;
            }
            if (GetRouteHitOwners(hit).Fighter != null ||
                GetRouteHitOwners(hit).Player != null)
            {
                continue;
            }
            if (allowTargetEquipment && pendingTreadmillStation != null &&
                pendingTreadmillStation.ContainsEquipmentCollider(hit))
            {
                continue;
            }
            // Walking out of a cage whose colliders are still ignored for
            // this enemy: the probes must ignore that cage too, otherwise
            // every direction reads as blocked and the enemy never leaves.
            if (equipmentEgressStation != null &&
                equipmentEgressStation.ContainsEquipmentCollider(hit))
            {
                continue;
            }
            if (HasRoomFloorInHierarchy(hit.transform) ||
                IsWalkableFloorSurface(hit))
            {
                continue;
            }
            lastVisitorRouteBlocker = "static:" + hit.name;
            return false;
        }
        return true;
    }
    private Vector3 GetCharacterSeparation()
    {
        Vector3 separation = Vector3.zero;
        for (int i = 0; i < Fighters.Count; i++)
        {
            EnemyFighter other = Fighters[i];
            if (other == null || !other.isActiveAndEnabled || other == this ||
                other.isDead || other.isPassive)
            {
                continue;
            }

            Vector3 offset = Vector3.ProjectOnPlane(transform.position - other.transform.position, Vector3.up);
            float distance = offset.magnitude;
            if (distance > 0.001f && distance < 2.1f)
            {
                separation += offset.normalized * (2.1f - distance);
            }
        }
        return separation;
    }
    // Set only around a chase step whose fighter or target is off the main floor.
    private bool chaseIgnoresRoomBounds;

    private bool IsOnMainFloor(Vector3 position)
    {
        if (!TryGetRoomBounds(out Bounds floorBounds))
        {
            return true;
        }
        return position.x >= floorBounds.min.x && position.x <= floorBounds.max.x &&
            position.z >= floorBounds.min.z && position.z <= floorBounds.max.z;
    }

    private bool IsInsideRoom(Vector3 direction, float distance)
    {
        if (!TryGetRoomBounds(out Bounds floorBounds))
        {
            return true;
        }

        Vector3 predicted = transform.position + direction * distance;
        float margin = GetBodyRadius() + 0.24f;
        return predicted.x >= floorBounds.min.x + margin &&
               predicted.x <= floorBounds.max.x - margin &&
               predicted.z >= floorBounds.min.z + margin &&
               predicted.z <= floorBounds.max.z - margin;
    }
    private void ClampToRoomBounds()
    {
        // Only keep a roamer on the floor it already stands on; never pull a
        // body in from the locker room or outside (a visible teleport).
        if (body == null || !TryGetRoomBounds(out Bounds floorBounds) || !IsOnMainFloor(body.position))
        {
            return;
        }

        float margin = GetBodyRadius() + 0.24f;
        Vector3 position = body.position;
        Vector3 clamped = position;
        clamped.x = Mathf.Clamp(position.x, floorBounds.min.x + margin, floorBounds.max.x - margin);
        clamped.z = Mathf.Clamp(position.z, floorBounds.min.z + margin, floorBounds.max.z - margin);
        // Ease back from the floor edge (e.g. just in from a doorway) instead
        // of snapping up to the full margin in one step.
        body.position = Vector3.MoveTowards(position, clamped, 2.5f * Time.fixedDeltaTime);
    }
    // The rubber floor never moves: look it up once and keep its bounds (this
    // runs inside every movement probe of every fighter).
    private static Renderer cachedFloorRenderer;
    private static Bounds cachedFloorBounds;
    private static float nextFloorLookupTime;

    private bool TryGetRoomBounds(out Bounds bounds)
    {
        if (cachedFloorRenderer == null)
        {
            bounds = default;
            if (Time.unscaledTime < nextFloorLookupTime)
            {
                return false;
            }
            nextFloorLookupTime = Time.unscaledTime + 0.5f;
            GameObject floor = GymSceneLookup.Find("Rubber Floor");
            cachedFloorRenderer = floor != null ? floor.GetComponent<Renderer>() : null;
            if (cachedFloorRenderer == null)
            {
                return false;
            }
            cachedFloorBounds = cachedFloorRenderer.bounds;
        }

        else if (Time.unscaledTime >= nextFloorLookupTime)
        {
            // Builders may still resize the floor while the scene starts.
            nextFloorLookupTime = Time.unscaledTime + 2f;
            cachedFloorBounds = cachedFloorRenderer.bounds;
        }

        bounds = cachedFloorBounds;
        return bounds.size.x > 2f && bounds.size.z > 2f;
    }
    // Name-based classification is static per object; cache it so probes do
    // not allocate lowercase name strings for every hit on every physics step.
    private static readonly Dictionary<Transform, bool> roomFloorCache = new Dictionary<Transform, bool>();
    private static readonly Dictionary<Collider, bool> walkableFloorCache = new Dictionary<Collider, bool>();
    private static readonly Dictionary<Collider, int> routeNameFlagCache = new Dictionary<Collider, int>();
    private const int RouteNameRoadBlocker = 1;
    private const int RouteNamePathOuter = 2;
    private const int RouteNameCourtyard = 4;

    // Play mode without domain reload keeps statics; start every session clean.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetRouteClassificationCaches()
    {
        roomFloorCache.Clear();
        walkableFloorCache.Clear();
        routeNameFlagCache.Clear();
    }

    private static int GetRouteNameFlags(Collider collider)
    {
        Collider id = collider;
        if (routeNameFlagCache.TryGetValue(id, out int flags))
        {
            return flags;
        }
        string name = collider.name;
        flags = (name == "Player Road Access Blocker" ? RouteNameRoadBlocker : 0) |
            (name == "Outdoor Boundary - Path Outer" ? RouteNamePathOuter : 0) |
            (name == "Exterior Courtyard Foundation" ? RouteNameCourtyard : 0);
        routeNameFlagCache[id] = flags;
        return flags;
    }

    private static bool HasRoomFloorInHierarchy(Transform target)
    {
        if (target == null)
        {
            return false;
        }
        Transform id = target;
        if (!roomFloorCache.TryGetValue(id, out bool result))
        {
            result = GymShared.ComputeRoomFloorInHierarchy(target);
            roomFloorCache[id] = result;
        }
        return result;
    }
    private static bool IsWalkableFloorSurface(Collider collider)
    {
        if (collider == null)
        {
            return false;
        }
        Collider id = collider;
        if (!walkableFloorCache.TryGetValue(id, out bool result))
        {
            result = ComputeWalkableFloorSurface(collider);
            walkableFloorCache[id] = result;
        }
        return result;
    }

    private static bool ComputeWalkableFloorSurface(Collider collider)
    {

        for (Transform current = collider.transform; current != null; current = current.parent)
        {
            string lowerName = current.name.ToLowerInvariant();
            // A thick loose prop (the half-rolled yoga mat) still blocks the
            // body; only flat mats count as floor.
            if (lowerName.StartsWith("loose item") && collider.bounds.size.y > 0.12f)
            {
                return false;
            }
            if (lowerName.Contains("mat") || lowerName.Contains("carpet") ||
                lowerName.Contains("rug") || lowerName.Contains("parking lot") ||
                lowerName.Contains("courtyard foundation") ||
                lowerName.Contains("door landing") ||
                lowerName.Contains("protein store platform") ||
                lowerName.Contains("path from gym") ||
                lowerName.Contains("parking path turn") ||
                lowerName.Contains("vehicle road"))
            {
                return true;
            }
        }

        return false;
    }
    // Root distance a non-lifter keeps from the deadlift platform footprint.
    public float DeadliftNavigationClearance => GetBodyRadius() + 0.18f;
    private float GetBodyRadius()
    {
        return GetBodyRadiusForIdentity(identity);
    }
    public static float GetBodyRadiusForIdentity(BodybuilderIdentity value)
    {
        // Goku is scaled 1.15x, so he needs the heavy clearance too.
        return value == BodybuilderIdentity.Cbum || value == BodybuilderIdentity.Ronnie ||
            value == BodybuilderIdentity.Goku
            ? EnemyHeavyRadius
            : EnemyStandardRadius;
    }
}
