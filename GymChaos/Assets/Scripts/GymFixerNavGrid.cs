using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Walkable grid for Jolly Dog over the gym, the locker room and the yard in
/// front of the door. Cells are probed once per visit (spread over frames
/// while he falls) with a body-sized box above the floor, so equipment,
/// walls and parked cars block while movable props, people and the door
/// panel do not. Paths are 8-connected A* then shortened by line of sight.
/// </summary>
public sealed class GymFixerNavGrid
{
    public const float CellSize = 0.35f;
    private const float BodyRadius = 0.27f;
    private const float StepTolerance = 0.45f;

    private readonly Collider[] overlap = new Collider[24];
    private readonly RaycastHit[] groundHits = new RaycastHit[12];
    private Bounds area;
    private int columns;
    private int rows;
    private bool[] walkable;
    private float[] groundY;
    private Transform ignoredRoot;

    public bool IsReady { get; private set; }
    public int WalkableCount { get; private set; }
    public int CellCount => columns * rows;

    public IEnumerator Build(Bounds region, float referenceY, Transform ignore, int cellsPerFrame)
    {
        IsReady = false;
        area = region;
        ignoredRoot = ignore;
        columns = Mathf.Max(1, Mathf.CeilToInt(region.size.x / CellSize));
        rows = Mathf.Max(1, Mathf.CeilToInt(region.size.z / CellSize));
        walkable = new bool[columns * rows];
        groundY = new float[columns * rows];
        WalkableCount = 0;
        int budget = cellsPerFrame;
        for (int z = 0; z < rows; z++)
        {
            for (int x = 0; x < columns; x++)
            {
                int index = z * columns + x;
                Vector3 center = CellCenter(x, z, referenceY);
                walkable[index] = ProbeCell(center, referenceY, out groundY[index]);
                if (walkable[index])
                {
                    WalkableCount++;
                }
                if (--budget <= 0)
                {
                    budget = cellsPerFrame;
                    yield return null;
                }
            }
        }
        IsReady = true;
    }

    /// <summary>Forces a corridor open (the doorway, whose panel swings).</summary>
    public void ForceWalkable(Vector3 from, Vector3 to, float radius)
    {
        if (walkable == null)
        {
            return;
        }
        for (int z = 0; z < rows; z++)
        {
            for (int x = 0; x < columns; x++)
            {
                Vector3 center = CellCenter(x, z, from.y);
                if (DistanceToSegmentXZ(center, from, to) > radius)
                {
                    continue;
                }
                int index = z * columns + x;
                if (!walkable[index])
                {
                    walkable[index] = true;
                    WalkableCount++;
                    float t = Mathf.Clamp01(Vector3.Dot(
                        Flat(center - from), Flat(to - from)) /
                        Mathf.Max(0.0001f, Flat(to - from).sqrMagnitude));
                    groundY[index] = Mathf.Lerp(from.y, to.y, t);
                }
            }
        }
    }

    public bool TryNearestWalkable(Vector3 point, float maxRadius, out Vector3 result)
    {
        result = point;
        if (!IsReady || !ToCell(point, out int cx, out int cz, true))
        {
            return false;
        }
        int ring = Mathf.CeilToInt(maxRadius / CellSize);
        float best = float.PositiveInfinity;
        int bestIndex = -1;
        for (int dz = -ring; dz <= ring; dz++)
        {
            for (int dx = -ring; dx <= ring; dx++)
            {
                int x = cx + dx;
                int z = cz + dz;
                if (x < 0 || z < 0 || x >= columns || z >= rows)
                {
                    continue;
                }
                int index = z * columns + x;
                if (!walkable[index])
                {
                    continue;
                }
                float distance = Flat(CellCenter(x, z, 0f) - point).sqrMagnitude;
                if (distance < best && distance <= maxRadius * maxRadius)
                {
                    best = distance;
                    bestIndex = index;
                }
            }
        }
        if (bestIndex < 0)
        {
            return false;
        }
        result = CellCenter(bestIndex % columns, bestIndex / columns, groundY[bestIndex]);
        return true;
    }

    public bool TryFindPath(Vector3 from, Vector3 to, List<Vector3> path)
    {
        path.Clear();
        if (!IsReady ||
            !TryNearestWalkable(from, 2.5f, out Vector3 start) ||
            !TryNearestWalkable(to, 2.5f, out Vector3 goal))
        {
            return false;
        }
        ToCell(start, out int sx, out int sz, false);
        ToCell(goal, out int gx, out int gz, false);
        int startIndex = sz * columns + sx;
        int goalIndex = gz * columns + gx;

        int count = columns * rows;
        float[] cost = new float[count];
        int[] parent = new int[count];
        bool[] closed = new bool[count];
        for (int i = 0; i < count; i++)
        {
            cost[i] = float.PositiveInfinity;
            parent[i] = -1;
        }
        MinHeap open = new MinHeap(256);
        cost[startIndex] = 0f;
        open.Push(startIndex, Heuristic(sx, sz, gx, gz));
        bool found = false;
        while (open.Count > 0)
        {
            int current = open.Pop();
            if (closed[current])
            {
                continue;
            }
            if (current == goalIndex)
            {
                found = true;
                break;
            }
            closed[current] = true;
            int cx = current % columns;
            int cz = current / columns;
            for (int dz = -1; dz <= 1; dz++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dz == 0)
                    {
                        continue;
                    }
                    int nx = cx + dx;
                    int nz = cz + dz;
                    if (nx < 0 || nz < 0 || nx >= columns || nz >= rows)
                    {
                        continue;
                    }
                    int next = nz * columns + nx;
                    if (!walkable[next] || closed[next])
                    {
                        continue;
                    }
                    // No corner cutting past a blocked neighbour.
                    if (dx != 0 && dz != 0 &&
                        (!walkable[cz * columns + nx] || !walkable[nz * columns + cx]))
                    {
                        continue;
                    }
                    float step = dx != 0 && dz != 0 ? 1.41421f : 1f;
                    float candidate = cost[current] + step;
                    if (candidate < cost[next])
                    {
                        cost[next] = candidate;
                        parent[next] = current;
                        open.Push(next, candidate + Heuristic(nx, nz, gx, gz));
                    }
                }
            }
        }
        if (!found)
        {
            return false;
        }

        List<int> cells = new List<int>();
        for (int index = goalIndex; index >= 0; index = parent[index])
        {
            cells.Add(index);
            if (index == startIndex)
            {
                break;
            }
        }
        cells.Reverse();
        // String pulling: keep only the cells needed for line of sight.
        int anchor = 0;
        path.Add(IndexToPoint(cells[0]));
        while (anchor < cells.Count - 1)
        {
            int furthest = anchor + 1;
            for (int probe = cells.Count - 1; probe > anchor + 1; probe--)
            {
                if (HasLineOfSight(cells[anchor], cells[probe]))
                {
                    furthest = probe;
                    break;
                }
            }
            path.Add(IndexToPoint(cells[furthest]));
            anchor = furthest;
        }
        return true;
    }

    public bool IsWalkable(Vector3 point)
    {
        return IsReady && ToCell(point, out int x, out int z, false) &&
            walkable[z * columns + x];
    }

    private bool ProbeCell(Vector3 center, float referenceY, out float ground)
    {
        ground = referenceY;
        Vector3 origin = new Vector3(center.x, referenceY + 2.2f, center.z);
        int hits = Physics.RaycastNonAlloc(
            origin, Vector3.down, groundHits, 4.5f,
            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        float best = float.NegativeInfinity;
        for (int i = 0; i < hits; i++)
        {
            Collider collider = groundHits[i].collider;
            if (IsIgnored(collider))
            {
                continue;
            }
            if (groundHits[i].point.y > best)
            {
                best = groundHits[i].point.y;
            }
        }
        if (float.IsNegativeInfinity(best) ||
            Mathf.Abs(best - referenceY) > StepTolerance)
        {
            // No floor here, or the top surface is furniture / a drop.
            return false;
        }
        ground = best;
        Vector3 boxCenter = new Vector3(center.x, ground + 1.0f, center.z);
        Vector3 halfExtents = new Vector3(BodyRadius, 0.6f, BodyRadius);
        int count = Physics.OverlapBoxNonAlloc(
            boxCenter, halfExtents, overlap, Quaternion.identity,
            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            if (!IsIgnored(overlap[i]))
            {
                return false;
            }
        }
        return true;
    }

    private bool IsIgnored(Collider collider)
    {
        if (collider == null)
        {
            return true;
        }
        Transform transform = collider.transform;
        if (ignoredRoot != null && transform.IsChildOf(ignoredRoot))
        {
            return true;
        }
        Rigidbody body = collider.attachedRigidbody;
        if (body != null && (!body.isKinematic ||
            body.GetComponent<PickupItem>() != null))
        {
            return true;
        }
        if (collider.GetComponentInParent<EnemyFighter>() != null ||
            collider.GetComponentInParent<PlayerMovement>() != null)
        {
            return true;
        }
        GymDoorway doorway = GymDoorway.Instance;
        return doorway != null && doorway.DoorPanel != null &&
            transform.IsChildOf(doorway.DoorPanel);
    }

    private bool HasLineOfSight(int fromIndex, int toIndex)
    {
        Vector3 from = IndexToPoint(fromIndex);
        Vector3 to = IndexToPoint(toIndex);
        float distance = Flat(to - from).magnitude;
        int samples = Mathf.CeilToInt(distance / (CellSize * 0.5f));
        for (int i = 1; i < samples; i++)
        {
            Vector3 point = Vector3.Lerp(from, to, i / (float)samples);
            // Sample the body width, not just the centre line.
            Vector3 side = Vector3.Cross(Vector3.up, Flat(to - from).normalized) * BodyRadius;
            if (!IsWalkable(point) || !IsWalkable(point + side) || !IsWalkable(point - side))
            {
                return false;
            }
        }
        return true;
    }

    private Vector3 IndexToPoint(int index)
    {
        return CellCenter(index % columns, index / columns, groundY[index]);
    }

    private Vector3 CellCenter(int x, int z, float y)
    {
        return new Vector3(
            area.min.x + (x + 0.5f) * CellSize, y,
            area.min.z + (z + 0.5f) * CellSize);
    }

    private bool ToCell(Vector3 point, out int x, out int z, bool clamp)
    {
        x = Mathf.FloorToInt((point.x - area.min.x) / CellSize);
        z = Mathf.FloorToInt((point.z - area.min.z) / CellSize);
        if (clamp)
        {
            x = Mathf.Clamp(x, 0, columns - 1);
            z = Mathf.Clamp(z, 0, rows - 1);
            return true;
        }
        return x >= 0 && z >= 0 && x < columns && z < rows;
    }

    private static float Heuristic(int x, int z, int gx, int gz)
    {
        int dx = Mathf.Abs(x - gx);
        int dz = Mathf.Abs(z - gz);
        return Mathf.Max(dx, dz) + 0.41421f * Mathf.Min(dx, dz);
    }

    private static Vector3 Flat(Vector3 value)
    {
        value.y = 0f;
        return value;
    }

    private static float DistanceToSegmentXZ(Vector3 point, Vector3 from, Vector3 to)
    {
        Vector3 segment = Flat(to - from);
        Vector3 offset = Flat(point - from);
        float t = segment.sqrMagnitude > 0.0001f
            ? Mathf.Clamp01(Vector3.Dot(offset, segment) / segment.sqrMagnitude)
            : 0f;
        return (offset - segment * t).magnitude;
    }

    private sealed class MinHeap
    {
        private int[] items;
        private float[] keys;
        public int Count { get; private set; }

        public MinHeap(int capacity)
        {
            items = new int[capacity];
            keys = new float[capacity];
        }

        public void Push(int item, float key)
        {
            if (Count == items.Length)
            {
                System.Array.Resize(ref items, Count * 2);
                System.Array.Resize(ref keys, Count * 2);
            }
            int index = Count++;
            while (index > 0)
            {
                int parent = (index - 1) / 2;
                if (keys[parent] <= key)
                {
                    break;
                }
                items[index] = items[parent];
                keys[index] = keys[parent];
                index = parent;
            }
            items[index] = item;
            keys[index] = key;
        }

        public int Pop()
        {
            int result = items[0];
            int lastItem = items[--Count];
            float lastKey = keys[Count];
            int index = 0;
            while (true)
            {
                int child = index * 2 + 1;
                if (child >= Count)
                {
                    break;
                }
                if (child + 1 < Count && keys[child + 1] < keys[child])
                {
                    child++;
                }
                if (keys[child] >= lastKey)
                {
                    break;
                }
                items[index] = items[child];
                keys[index] = keys[child];
                index = child;
            }
            if (Count > 0)
            {
                items[index] = lastItem;
                keys[index] = lastKey;
            }
            return result;
        }
    }
}
