using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Rolls the separated wheel nodes of a vehicle model while it drives.
/// Tools/split_vehicle_wheels.py cuts each tyre out of the scanned body into
/// its own node named "Wheel_*" with the origin on the axle. This component
/// turns only those nodes, by the distance the vehicle travelled along its
/// forward axis divided by the wheel radius, so the tread does not slide. The
/// body and every other part are never rotated, and a parked vehicle keeps
/// its wheels still.
/// </summary>
[DisallowMultipleComponent]
public sealed class GymVehicleWheelSpinner : MonoBehaviour
{
    public const string WheelPrefix = "Wheel_";
    // Larger jumps are placements (spawn, reset, teleport), not driving.
    private const float MaxStepDistance = 4f;
    private const float SearchInterval = 0.25f;
    private const float SearchTimeout = 30f;

    private sealed class Wheel
    {
        public Transform Node;
        public MeshRenderer Renderer;
        public Bounds MeshBounds;
        public int AxleAxis;
        public int UpAxis;
        public float LocalRadius;
        public float Angle;
    }

    private readonly List<Wheel> wheels = new List<Wheel>();
    private Func<bool> isDriving;
    private Vector3 lastPosition;
    private float nextSearchTime;
    private float searchUntil;

    public int WheelCount => wheels.Count;
    public float TotalRolledDistance { get; private set; }
    public float TotalSpinDegrees { get; private set; }

    public static GymVehicleWheelSpinner Attach(GameObject root, Func<bool> isDriving)
    {
        if (root == null)
        {
            return null;
        }

        GymVehicleWheelSpinner spinner = root.GetComponent<GymVehicleWheelSpinner>();
        if (spinner == null)
        {
            spinner = root.AddComponent<GymVehicleWheelSpinner>();
        }
        spinner.isDriving = isDriving;
        return spinner;
    }

    /// <summary>World rolling radius of wheel `index` (rotation independent).</summary>
    public float GetWheelRadius(int index)
    {
        Wheel wheel = wheels[index];
        Vector3 scale = wheel.Node.lossyScale;
        return wheel.LocalRadius * Mathf.Abs(scale[wheel.UpAxis]);
    }

    public Transform GetWheel(int index) => wheels[index].Node;

    private void OnEnable()
    {
        lastPosition = transform.position;
        searchUntil = Time.unscaledTime + SearchTimeout;
        nextSearchTime = 0f;
    }

    private void LateUpdate()
    {
        Vector3 position = transform.position;
        Vector3 delta = position - lastPosition;
        lastPosition = position;

        if (wheels.Count == 0)
        {
            // GLB vehicles load asynchronously; look for the wheel nodes
            // until they appear.
            if (Time.unscaledTime >= nextSearchTime && Time.unscaledTime <= searchUntil)
            {
                nextSearchTime = Time.unscaledTime + SearchInterval;
                FindWheels();
            }
            return;
        }

        if (isDriving != null && !isDriving())
        {
            return;
        }

        Vector3 forward = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
        if (forward.sqrMagnitude < 0.0001f)
        {
            return;
        }
        float distance = Vector3.Dot(delta, forward.normalized);
        if (Mathf.Abs(distance) < 0.00001f ||
            delta.magnitude > MaxStepDistance)
        {
            return;
        }

        // Positive rotation about the vehicle's right axis moves the top of
        // the tyre forward, which is how a wheel rolls when driving forward.
        Vector3 axle = transform.right;
        float spun = 0f;
        for (int index = 0; index < wheels.Count; index++)
        {
            Wheel wheel = wheels[index];
            if (wheel.Node == null)
            {
                continue;
            }
            // The radius comes from the mesh, not the renderer bounds: those
            // grow while the wheel is turned, and GLB vehicles are fitted to
            // their target size after the parts appear.
            float degrees = distance / Mathf.Max(0.02f, GetWheelRadius(index)) * Mathf.Rad2Deg;
            wheel.Node.Rotate(axle, degrees, Space.World);
            wheel.Angle = Mathf.Repeat(wheel.Angle + degrees, 360f);
            KeepBoundsTight(wheel);
            spun = degrees;
        }
        TotalRolledDistance += Mathf.Abs(distance);
        TotalSpinDegrees += Mathf.Abs(spun);
    }

    // A turned box's world AABB is larger than the tyre it holds (up to
    // sqrt(2) at 45 degrees), which would push the wheel bounds below the
    // road. Shrink the two cross-section extents by |cos|+|sin| of the spin
    // angle so the world bounds stay exactly the tyre's circle.
    private static void KeepBoundsTight(Wheel wheel)
    {
        if (wheel.Renderer == null)
        {
            return;
        }
        float radians = wheel.Angle * Mathf.Deg2Rad;
        float shrink = Mathf.Abs(Mathf.Cos(radians)) + Mathf.Abs(Mathf.Sin(radians));
        Vector3 size = wheel.MeshBounds.size;
        float cross = 2f * wheel.LocalRadius / shrink;
        for (int axis = 0; axis < 3; axis++)
        {
            if (axis != wheel.AxleAxis)
            {
                size[axis] = cross;
            }
        }
        wheel.Renderer.localBounds = new Bounds(wheel.MeshBounds.center, size);
    }

    private static int DominantAxis(Vector3 v)
    {
        v = new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));
        return v.x >= v.y ? (v.x >= v.z ? 0 : 2) : (v.y >= v.z ? 1 : 2);
    }

    // Only called before the first turn: the axle and up axes are read from
    // the unrotated wheel nodes.
    private void FindWheels()
    {
        wheels.Clear();
        Transform[] nodes = GetComponentsInChildren<Transform>(true);
        for (int index = 0; index < nodes.Length; index++)
        {
            Transform node = nodes[index];
            if (node == transform ||
                !node.name.StartsWith(WheelPrefix, StringComparison.Ordinal))
            {
                continue;
            }
            MeshRenderer renderer = node.GetComponent<MeshRenderer>();
            MeshFilter filter = node.GetComponent<MeshFilter>();
            if (renderer == null || filter == null || filter.sharedMesh == null)
            {
                continue;
            }
            // The axle is the mesh axis that lines up with the vehicle's
            // right. The rolling radius is the extent along the mesh axis
            // that points up before the first turn: from the axle to the
            // ground contact (stray trim at the sides does not count).
            int axleAxis = DominantAxis(node.InverseTransformDirection(transform.right));
            int upAxis = DominantAxis(node.InverseTransformDirection(transform.up));
            Bounds meshBounds = filter.sharedMesh.bounds;
            float localRadius = meshBounds.extents[upAxis];
            Wheel wheel = new Wheel
            {
                Node = node,
                Renderer = renderer,
                MeshBounds = meshBounds,
                AxleAxis = axleAxis,
                UpAxis = upAxis,
                LocalRadius = localRadius
            };
            wheels.Add(wheel);
            if (GetWheelRadius(wheels.Count - 1) < 0.02f)
            {
                wheels.RemoveAt(wheels.Count - 1);
            }
        }
    }
}
