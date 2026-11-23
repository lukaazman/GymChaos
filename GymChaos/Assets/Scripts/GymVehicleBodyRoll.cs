using UnityEngine;

/// <summary>
/// Small visual body roll and pitch for a remade vehicle: tilts the visual
/// child from the driving frame's lateral and forward acceleration (leans out
/// of turns, dips the nose when braking). Purely visual: the frame, its
/// collider and the route never tilt.
/// shortcut: the wheels are children of the body and tilt with it (under
/// 3 degrees); split them off the body if the tilt ever shows at the tyres.
/// </summary>
public sealed class GymVehicleBodyRoll : MonoBehaviour
{
    private const float MaxRollDegrees = 2.5f;
    private const float MaxPitchDegrees = 1.5f;
    private const float RollPerAcceleration = 0.3f;
    private const float PitchPerAcceleration = 0.2f;
    private const float VelocityResponse = 6f;
    private const float TiltResponse = 5f;
    // Larger jumps are placements (spawn, teleport), not driving.
    private const float MaxStep = 4f;

    private Transform frame;
    private Quaternion rest;
    private Vector3 lastPosition;
    private Vector3 velocity;
    private bool primed;

    public float RollDegrees { get; private set; }
    public float PitchDegrees { get; private set; }

    /// <summary>Tilts <paramref name="visual"/> from the motion of <paramref name="drivingFrame"/>.</summary>
    public static GymVehicleBodyRoll Attach(Transform visual, Transform drivingFrame)
    {
        if (visual == null || drivingFrame == null) return null;
        GymVehicleBodyRoll roll = visual.GetComponent<GymVehicleBodyRoll>() ??
            visual.gameObject.AddComponent<GymVehicleBodyRoll>();
        roll.frame = drivingFrame;
        roll.rest = visual.localRotation;
        roll.primed = false;
        return roll;
    }

    private void LateUpdate()
    {
        float dt = Time.deltaTime;
        if (frame == null || dt <= 1e-5f) return;
        Vector3 position = frame.position;
        Vector3 step = position - lastPosition;
        lastPosition = position;
        if (!primed || step.sqrMagnitude > MaxStep * MaxStep)
        {
            primed = true;
            velocity = Vector3.zero;
            return;
        }
        Vector3 previous = velocity;
        velocity = Vector3.Lerp(velocity, step / dt, 1f - Mathf.Exp(-VelocityResponse * dt));
        Vector3 acceleration = (velocity - previous) / dt;
        float lateral = Vector3.Dot(acceleration, frame.right);
        float forward = Vector3.Dot(acceleration, frame.forward);
        float k = 1f - Mathf.Exp(-TiltResponse * dt);
        // Lean away from the turn (top toward -right) and dip the nose when slowing.
        RollDegrees = Mathf.Lerp(RollDegrees, Mathf.Clamp(lateral * RollPerAcceleration, -MaxRollDegrees, MaxRollDegrees), k);
        PitchDegrees = Mathf.Lerp(PitchDegrees, Mathf.Clamp(-forward * PitchPerAcceleration, -MaxPitchDegrees, MaxPitchDegrees), k);
        transform.localRotation = Quaternion.Euler(PitchDegrees, 0f, RollDegrees) * rest;
    }
}
