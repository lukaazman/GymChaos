#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Measures a vehicle's wheel rotation every frame, independently of
/// GymVehicleWheelSpinner's own counters: the angle each wheel node turned,
/// the distance the vehicle rolled along its forward axis, and how far any
/// non-wheel renderer turned relative to the vehicle root.
/// </summary>
public sealed class GymChaosWheelSpinProbe
{
    private readonly Transform transform;
    public readonly GameObject gameObject;
    private readonly List<Transform> wheels = new List<Transform>();
    private readonly List<Renderer> wheelRenderers = new List<Renderer>();
    private readonly List<Quaternion> previousWheel = new List<Quaternion>();
    private readonly List<Transform> bodyParts = new List<Transform>();
    private readonly List<Quaternion> bodyStart = new List<Quaternion>();
    private Vector3 lastPosition;

    public bool Recording;
    public bool Driving;
    public float MeasuredDegrees;
    public float ExpectedDegrees;
    public float RolledDistance;
    public float ParkedDegrees;
    public float MaxBodyTurn;
    public float MaxWheelAxleError;
    // How far a wheel's world bounds reach below/above its start height
    // relative to the axle, i.e. bounds inflation from the spin.
    public float MaxBoundsGrowth;
    private readonly List<float> startHalfHeight = new List<float>();
    public int WheelCount => wheels.Count;

    // Sampled once per player frame from the verifier's editor update; the
    // verifier bounds Time.maximumDeltaTime so a frame stays short.
    public GymChaosWheelSpinProbe(GameObject root)
    {
        gameObject = root;
        transform = root.transform;
    }

    public void Capture()
    {
        wheels.Clear();
        wheelRenderers.Clear();
        previousWheel.Clear();
        bodyParts.Clear();
        bodyStart.Clear();
        startHalfHeight.Clear();
        foreach (Renderer renderer in gameObject.GetComponentsInChildren<Renderer>(true))
        {
            Transform node = renderer.transform;
            if (node.name.StartsWith(GymVehicleWheelSpinner.WheelPrefix))
            {
                wheels.Add(node);
                wheelRenderers.Add(renderer);
                startHalfHeight.Add(renderer.bounds.extents.y);
                previousWheel.Add(Quaternion.Inverse(transform.rotation) * node.rotation);
            }
            else
            {
                bodyParts.Add(node);
                bodyStart.Add(Quaternion.Inverse(transform.rotation) * node.rotation);
            }
        }
        lastPosition = transform.position;
        MaxWheelAxleError = 0f;
        for (int i = 0; i < wheels.Count; i++)
        {
            MeshFilter filter = wheels[i].GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null) continue;
            // The thinnest local axis of the tyre mesh is its axle.
            Vector3 size = Vector3.Scale(filter.sharedMesh.bounds.size, wheels[i].lossyScale);
            Vector3 local = size.x <= size.y && size.x <= size.z ? Vector3.right
                : size.y <= size.z ? Vector3.up : Vector3.forward;
            float thinnest = Mathf.Min(size.x, Mathf.Min(size.y, size.z));
            float middle = size.x + size.y + size.z - thinnest -
                Mathf.Max(size.x, Mathf.Max(size.y, size.z));
            // A twin rear tyre is about as wide as it is tall; its bounds
            // cannot tell the axle apart, so it is not measured.
            if (thinnest > middle * 0.8f) continue;
            Vector3 axle = wheels[i].TransformDirection(local);
            MaxWheelAxleError = Mathf.Max(MaxWheelAxleError,
                1f - Mathf.Abs(Vector3.Dot(axle.normalized, transform.right)));
        }
    }

    public void Sample()
    {
        if (!Recording)
        {
            lastPosition = transform.position;
            for (int i = 0; i < wheels.Count; i++)
                if (wheels[i] != null) previousWheel[i] = Quaternion.Inverse(transform.rotation) * wheels[i].rotation;
            return;
        }

        Vector3 delta = transform.position - lastPosition;
        lastPosition = transform.position;
        Vector3 forward = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
        float distance = Mathf.Abs(Vector3.Dot(delta, forward));
        for (int i = 0; i < wheels.Count; i++)
        {
            if (wheels[i] == null) continue;
            // Measure in the vehicle's own frame so steering yaw is not
            // counted; the spin is the part about the vehicle's right axis.
            Quaternion current = Quaternion.Inverse(transform.rotation) * wheels[i].rotation;
            Quaternion relative = current * Quaternion.Inverse(previousWheel[i]);
            relative.ToAngleAxis(out float angle, out Vector3 axis);
            if (angle > 180f) angle = 360f - angle;
            float axleComponent = axis.sqrMagnitude > 0f ? Mathf.Abs(axis.normalized.x) : 0f;
            float spin = angle * axleComponent;
            MaxBoundsGrowth = Mathf.Max(MaxBoundsGrowth,
                wheelRenderers[i].bounds.extents.y - startHalfHeight[i]);
            previousWheel[i] = current;
            if (Driving)
            {
                MeasuredDegrees += spin;
                float radius = Mathf.Max(0.02f, wheelRenderers[i].bounds.extents.y);
                ExpectedDegrees += distance / radius * Mathf.Rad2Deg;
            }
            else
            {
                ParkedDegrees += spin;
            }
        }
        if (Driving) RolledDistance += distance;
        for (int i = 0; i < bodyParts.Count; i++)
        {
            if (bodyParts[i] == null) continue;
            Quaternion now = Quaternion.Inverse(transform.rotation) * bodyParts[i].rotation;
            MaxBodyTurn = Mathf.Max(MaxBodyTurn, Quaternion.Angle(bodyStart[i], now));
        }
    }
}
#endif
