using UnityEngine;

/// <summary>
/// Third-person race camera. Takes over a camera (normally the player's) and
/// restores its local pose, field of view and culling mask afterwards.
/// Modes: a spring chase that pulls back and widens with speed and boost, a
/// pre-race flyover along the track, and a slow orbit at the finish.
/// </summary>
public sealed class GymRaceChaseCamera : MonoBehaviour
{
    public enum Mode
    {
        Chase,
        Flyover,
        Orbit
    }

    public const float BaseFieldOfView = 62f;
    public const float SpeedFieldOfView = 12f;
    public const float BoostFieldOfView = 8f;
    public const float BaseDistance = 6f;
    public const float SpeedDistance = 2.5f;
    public const float BoostDistance = 2f;
    private const float Height = 2.35f;
    private const float FlyoverHeight = 14f;

    private Camera target;
    private Vector3 savedLocalPosition;
    private Quaternion savedLocalRotation;
    private float savedFieldOfView;
    private int savedCullingMask;
    private float savedNearClip;
    private bool active;

    private GymRaceKart kart;
    private Mode mode;
    private float distance = BaseDistance;
    private float fieldOfView = BaseFieldOfView;
    private Vector3 smoothedPosition;
    private Vector3 smoothedLook;
    private float shake;
    private float flyoverStart;
    private float flyoverDuration = 4f;
    private float orbitAngle;
    private bool snap;

    public bool IsActive => active;
    public Mode CurrentMode => mode;
    public float CurrentDistance => distance;
    public float CurrentFieldOfView => target != null ? target.fieldOfView : fieldOfView;
    public bool LookBack { get; set; }
    public Camera Target => target;

    public void Begin(Camera camera, GymRaceKart follow)
    {
        if (camera == null)
        {
            return;
        }
        if (active)
        {
            End();
        }
        target = camera;
        savedLocalPosition = camera.transform.localPosition;
        savedLocalRotation = camera.transform.localRotation;
        savedFieldOfView = camera.fieldOfView;
        savedCullingMask = camera.cullingMask;
        savedNearClip = camera.nearClipPlane;
        // Show the full player body, hide the first-person arms.
        camera.cullingMask = (savedCullingMask | (1 << PlanarGymMirror.MirrorPlayerLayer)) &
            ~(1 << PlanarGymMirror.FirstPersonPlayerLayer);
        camera.nearClipPlane = Mathf.Min(savedNearClip, 0.1f);
        kart = follow;
        mode = Mode.Chase;
        active = true;
        snap = true;
    }

    public void SetKart(GymRaceKart follow)
    {
        kart = follow;
        snap = true;
    }

    public void StartFlyover(float duration)
    {
        mode = Mode.Flyover;
        flyoverStart = Time.time;
        flyoverDuration = Mathf.Max(0.5f, duration);
        snap = true;
    }

    public void StartChase()
    {
        mode = Mode.Chase;
    }

    public void StartOrbit()
    {
        mode = Mode.Orbit;
        orbitAngle = 0f;
    }

    public void Shake(float amount)
    {
        shake = Mathf.Max(shake, Mathf.Clamp01(amount));
    }

    public void End()
    {
        if (!active)
        {
            return;
        }
        active = false;
        if (target != null)
        {
            target.transform.localPosition = savedLocalPosition;
            target.transform.localRotation = savedLocalRotation;
            target.fieldOfView = savedFieldOfView;
            target.cullingMask = savedCullingMask;
            target.nearClipPlane = savedNearClip;
        }
        target = null;
        kart = null;
    }

    private void OnDisable()
    {
        End();
    }

    private void LateUpdate()
    {
        if (!active || target == null)
        {
            return;
        }
        float dt = Mathf.Max(Time.deltaTime, 1e-4f);
        Vector3 position;
        Vector3 look;
        float desiredFov = BaseFieldOfView;
        switch (mode)
        {
            case Mode.Flyover:
                Flyover(out position, out look);
                desiredFov = 58f;
                break;
            case Mode.Orbit:
                Orbit(dt, out position, out look);
                desiredFov = 55f;
                break;
            default:
                Chase(dt, out position, out look, out desiredFov);
                break;
        }

        float response = mode == Mode.Chase ? 1f - Mathf.Exp(-dt * 9f) : 1f - Mathf.Exp(-dt * 4f);
        if (snap)
        {
            smoothedPosition = position;
            smoothedLook = look;
            fieldOfView = desiredFov;
            snap = false;
        }
        smoothedPosition = Vector3.Lerp(smoothedPosition, position, response);
        smoothedLook = Vector3.Lerp(smoothedLook, look, 1f - Mathf.Exp(-dt * 14f));
        fieldOfView = Mathf.Lerp(fieldOfView, desiredFov, 1f - Mathf.Exp(-dt * 4f));

        Vector3 shakeOffset = Vector3.zero;
        if (shake > 0f)
        {
            float t = Time.time * 37f;
            shakeOffset = new Vector3(Mathf.PerlinNoise(t, 0.1f) - 0.5f, Mathf.PerlinNoise(0.7f, t) - 0.5f, 0f) * (shake * 0.35f);
            shake = Mathf.Max(0f, shake - dt * 2.8f);
        }
        Quaternion rotation = Quaternion.LookRotation(smoothedLook - smoothedPosition, Vector3.up);
        target.transform.SetPositionAndRotation(smoothedPosition + rotation * shakeOffset, rotation);
        target.fieldOfView = fieldOfView;
    }

    private void Chase(float dt, out Vector3 position, out Vector3 look, out float desiredFov)
    {
        if (kart == null || kart.Visual == null)
        {
            position = target.transform.position;
            look = position + target.transform.forward;
            desiredFov = BaseFieldOfView;
            return;
        }
        float top = Mathf.Max(kart.KartStats.TopSpeed, 1f);
        float speed01 = Mathf.Clamp01(Mathf.Abs(kart.ForwardSpeed) / top);
        float boost = kart.IsBoosting ? 1f : kart.MiniTurboTime > 0f ? 0.45f : 0f;
        float desiredDistance = BaseDistance + SpeedDistance * speed01 + BoostDistance * boost;
        distance = Mathf.Lerp(distance, desiredDistance, 1f - Mathf.Exp(-dt * 2.5f));
        desiredFov = BaseFieldOfView + SpeedFieldOfView * speed01 + BoostFieldOfView * boost;

        // Follow the travel heading, not the drifting body, so the slide reads.
        Vector3 forward = kart.Heading;
        if (LookBack)
        {
            forward = -forward;
        }
        Vector3 pivot = kart.transform.position + Vector3.up * 0.6f;
        position = pivot - forward * distance + Vector3.up * (Height - 0.25f * speed01);
        look = pivot + forward * (3f + 4f * speed01) + Vector3.up * 0.4f;

        // Keep the camera out of barriers and decks.
        Vector3 toCamera = position - pivot;
        int mask = 1 << GymRaceTrackBuilder.RaceTrackLayer;
        if (Physics.SphereCast(pivot, 0.3f, toCamera.normalized, out RaycastHit hit, toCamera.magnitude, mask, QueryTriggerInteraction.Ignore))
        {
            position = pivot + toCamera.normalized * Mathf.Max(1.2f, hit.distance - 0.2f);
        }
    }

    private void Flyover(out Vector3 position, out Vector3 look)
    {
        GymRaceTrackPath main = GymRaceTrackLayout.Main;
        float t = Mathf.Clamp01((Time.time - flyoverStart) / flyoverDuration);
        float eased = t * t * (3f - 2f * t);
        // Sweep the lap from the spiral back to the grid, high above the road.
        float start = GymRaceTrackLayout.StartLineDistance - main.Length * 0.42f;
        float s = start + eased * (main.Length * 0.42f - 18f);
        main.Sample(s, out Vector3 point, out Vector3 tangent, out _, out _);
        float height = Mathf.Lerp(FlyoverHeight * 2.4f, Height + 2f, eased);
        position = point - tangent * Mathf.Lerp(30f, 10f, eased) + Vector3.up * height;
        look = point + tangent * 12f;
        if (t >= 1f && kart != null)
        {
            look = kart.transform.position;
        }
    }

    private void Orbit(float dt, out Vector3 position, out Vector3 look)
    {
        Vector3 center = kart != null ? kart.transform.position : target.transform.position;
        orbitAngle += dt * 28f;
        Vector3 offset = Quaternion.Euler(0f, orbitAngle, 0f) * (kart != null ? -kart.Heading : Vector3.back) * 6.5f;
        position = center + offset + Vector3.up * 2.2f;
        look = center + Vector3.up * 1f;
    }
}
