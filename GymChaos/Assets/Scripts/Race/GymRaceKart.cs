using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Arcade kart: a frictionless physics sphere that the controller drives at
/// velocity level, with a separately steered heading. The vehicle visual
/// follows the sphere, aligned to the road normal, and swings out while
/// drifting. Karts bump each other through ordinary sphere collisions.
/// </summary>
[RequireComponent(typeof(Rigidbody), typeof(SphereCollider))]
public sealed class GymRaceKart : MonoBehaviour
{
    [Serializable]
    public struct Stats
    {
        public float TopSpeed;
        public float Acceleration;
        public float Handling;
        public float Grip;
        public float Mass;
        public float Radius;

        public static Stats Default => new Stats
        {
            TopSpeed = 27f,
            Acceleration = 9.5f,
            Handling = 96f,
            Grip = 28f,
            Mass = 900f,
            Radius = 1f
        };
    }

    public const float BoostMultiplier = 1.45f;
    public const float BoostDuration = 3f;
    public const float BoostFillSeconds = 12f;
    public const float MiniTurboMultiplier = 1.22f;
    public const float SlipstreamMultiplier = 1.06f;
    public const float DriftVisualAngle = 26f;
    public static readonly float[] DriftTierSeconds = { 0.9f, 1.9f, 3f };
    private static readonly float[] MiniTurboSeconds = { 0.55f, 0.95f, 1.35f };
    private const float ReverseSpeed = 8f;
    private const float BrakeDeceleration = 30f;
    private const float CoastDeceleration = 5f;
    private const float DriftMinSpeed = 9f;
    private const float DriftGrip = 5.2f;
    private const float HopSpeed = 3.2f;
    private const float StickForce = 22f;
    private const float WallSpeedKeep = 0.72f;

    public static readonly List<GymRaceKart> All = new List<GymRaceKart>();

    public Stats KartStats = Stats.Default;
    public GymRaceDriveInput Input;
    /// <summary>False during the countdown: the kart holds still.</summary>
    public bool ControlsEnabled = true;
    /// <summary>False before GO and after the finish: the boost meter does not fill.</summary>
    public bool MeterFilling = true;
    /// <summary>Director-set catch-up factor on top speed (rubber band).</summary>
    public float SpeedScale = 1f;
    public Transform Visual;

    public event Action<Vector3, float, GymRaceKart> Bumped;
    public event Action<int> MiniTurboStarted;
    public event Action BoostStarted;
    public event Action DriftStarted;

    private Rigidbody body;
    private SphereCollider sphere;
    private Vector3 heading = Vector3.forward;
    private Vector3 previousHeading = Vector3.forward;
    private Vector3 visualUp = Vector3.up;
    private float driftVisual;
    private float steerVisual;
    private bool boostRequested;
    private float slipstreamTimer;

    public Rigidbody Body => body;
    public float Radius => KartStats.Radius;
    public bool Grounded { get; private set; }
    public Vector3 GroundNormal { get; private set; } = Vector3.up;
    public Vector3 Heading => heading;
    public float ForwardSpeed { get; private set; }
    public float Speed => body != null ? body.linearVelocity.magnitude : 0f;
    public bool IsDrifting { get; private set; }
    public int DriftDirection { get; private set; }
    public float DriftCharge { get; private set; }
    public int DriftTier { get; private set; }
    public float MiniTurboTime { get; private set; }
    public float BoostMeter { get; private set; }
    public float BoostTime { get; private set; }
    public bool IsBoosting => BoostTime > 0f;
    public bool InSlipstream => slipstreamTimer > 0.8f;
    public float CurrentMaxSpeed { get; private set; }
    public float AirTime { get; private set; }
    /// <summary>Visual yaw offset from the travel heading (degrees), signed by drift direction.</summary>
    public float VisualDriftAngle => driftVisual;
    public int WallHits { get; private set; }
    public int KartHits { get; private set; }
    // Away from the kart this one touches; the drive code sets the velocity
    // every step, so without this it keeps pushing into the other kart.
    private Vector3 kartContactNormal;
    private float kartContactUntil;

    public static GymRaceKart Create(string name, Vector3 position, Vector3 forward, Stats stats, Transform visual)
    {
        GameObject root = new GameObject(name);
        root.transform.position = position + Vector3.up * stats.Radius;
        Rigidbody rb = root.AddComponent<Rigidbody>();
        SphereCollider collider = root.AddComponent<SphereCollider>();
        GymRaceKart kart = root.AddComponent<GymRaceKart>();
        kart.KartStats = stats;
        kart.Configure(rb, collider);
        kart.heading = new Vector3(forward.x, 0f, forward.z).normalized;
        kart.previousHeading = kart.heading;
        if (visual != null)
        {
            kart.AttachVisual(visual);
        }
        return kart;
    }

    private void Configure(Rigidbody rb, SphereCollider collider)
    {
        body = rb;
        sphere = collider;
        body.mass = KartStats.Mass;
        body.linearDamping = 0f;
        body.angularDamping = 0f;
        body.useGravity = true;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        // Head-on kart hits at closing speeds near 60 m/s: extra solver passes
        // keep the spheres from sinking into each other on the contact step.
        body.solverIterations = 12;
        body.solverVelocityIterations = 4;
        body.constraints = RigidbodyConstraints.FreezeRotation;
        sphere.radius = KartStats.Radius;
        sphere.material = new PhysicsMaterial("Race Kart")
        {
            dynamicFriction = 0f,
            staticFriction = 0f,
            frictionCombine = PhysicsMaterialCombine.Minimum,
            bounciness = 0.18f,
            bounceCombine = PhysicsMaterialCombine.Maximum
        };
    }

    public void AttachVisual(Transform visual)
    {
        Visual = visual;
        visual.SetParent(transform, true);
        UpdateVisual(1f);
    }

    /// <summary>Ignore collisions between this sphere and every collider of the given roots (drivers, cabins).</summary>
    public void IgnoreColliders(Transform root)
    {
        if (root == null || sphere == null)
        {
            return;
        }
        foreach (Collider other in root.GetComponentsInChildren<Collider>(true))
        {
            if (other != sphere && !(other is SphereCollider && other.GetComponent<GymRaceKart>() != null))
            {
                Physics.IgnoreCollision(sphere, other, true);
            }
        }
    }

    private void Awake()
    {
        if (body == null)
        {
            Configure(GetComponent<Rigidbody>(), GetComponent<SphereCollider>());
        }
    }

    private void OnEnable()
    {
        if (!All.Contains(this))
        {
            All.Add(this);
        }
    }

    private void OnDisable()
    {
        All.Remove(this);
    }

    private void OnDestroy()
    {
        if (sphere != null && sphere.sharedMaterial != null)
        {
            Destroy(sphere.sharedMaterial);
        }
    }

    private void Update()
    {
        if (Input.BoostPressed)
        {
            boostRequested = true;
        }
        float blend = Time.fixedDeltaTime > 0f
            ? Mathf.Clamp01((Time.time - Time.fixedTime) / Time.fixedDeltaTime)
            : 1f;
        UpdateVisual(blend);
    }

    private void FixedUpdate()
    {
        float dt = Time.fixedDeltaTime;
        previousHeading = heading;
        ProbeGround();
        Vector3 n = GroundNormal;
        Vector3 forward = Vector3.ProjectOnPlane(heading, n).normalized;
        if (forward.sqrMagnitude < 1e-4f)
        {
            forward = heading;
        }
        Vector3 right = Vector3.Cross(n, forward).normalized;

        Vector3 velocity = body.linearVelocity;
        float vF = Vector3.Dot(velocity, forward);
        float vR = Vector3.Dot(velocity, right);
        float vN = Vector3.Dot(velocity, n);

        if (!ControlsEnabled)
        {
            // Countdown: hold position on the grid.
            body.linearVelocity = Grounded ? n * Mathf.Min(vN, 0f) : velocity;
            EndDrift(false);
            ForwardSpeed = 0f;
            return;
        }

        UpdateDrift(vF, dt);
        Steer(vF, n, dt);
        forward = Vector3.ProjectOnPlane(heading, n).normalized;
        right = Vector3.Cross(n, forward).normalized;
        // Re-read the velocity on the turned axes: grip, not the steering,
        // decides how fast the travel direction follows the nose.
        vF = Vector3.Dot(velocity, forward);
        vR = Vector3.Dot(velocity, right);

        UpdateBoost(dt);
        UpdateSlipstream(vF, dt);
        float maxSpeed = KartStats.TopSpeed * SpeedScale;
        if (IsBoosting)
        {
            maxSpeed *= BoostMultiplier;
        }
        else if (MiniTurboTime > 0f)
        {
            maxSpeed *= MiniTurboMultiplier;
        }
        if (InSlipstream)
        {
            maxSpeed *= SlipstreamMultiplier;
        }
        CurrentMaxSpeed = maxSpeed;

        if (Grounded)
        {
            AirTime = 0f;
            float throttle = Mathf.Clamp01(Input.Throttle);
            float brake = Mathf.Clamp01(Input.Brake);
            if (IsBoosting || MiniTurboTime > 0f)
            {
                throttle = 1f;
            }
            if (brake > 0.1f && !IsBoosting)
            {
                vF = vF > 0.5f
                    ? Mathf.MoveTowards(vF, 0f, BrakeDeceleration * brake * dt)
                    : Mathf.MoveTowards(vF, -ReverseSpeed * brake, 10f * dt);
            }
            else if (throttle > 0.05f)
            {
                float target = maxSpeed * throttle;
                float rate = vF < target
                    ? KartStats.Acceleration * Mathf.Lerp(1.45f, 0.5f, Mathf.Clamp01(vF / KartStats.TopSpeed)) *
                      (IsBoosting || MiniTurboTime > 0f ? 2.2f : 1f)
                    : 7f;
                vF = Mathf.MoveTowards(vF, target, rate * dt);
            }
            else
            {
                vF = Mathf.MoveTowards(vF, 0f, CoastDeceleration * dt);
            }

            // Exponential side grip: steady slip angle is atan(yaw rate / grip).
            vR *= Mathf.Exp(-(IsDrifting ? DriftGrip : KartStats.Grip) * dt);
            velocity = forward * vF + right * vR + n * vN;
            velocity = LimitClosingSpeed(velocity, dt);
            if (Time.fixedTime < kartContactUntil)
            {
                float into = Vector3.Dot(velocity, kartContactNormal);
                if (into < 0f)
                {
                    velocity -= kartContactNormal * into;
                }
            }
            body.linearVelocity = velocity;
            body.AddForce(-n * StickForce, ForceMode.Acceleration);
        }
        else
        {
            AirTime += dt;
            body.AddForce(Physics.gravity * 0.6f, ForceMode.Acceleration);
        }
        ForwardSpeed = vF;
        boostRequested = false;
    }

    private void ProbeGround()
    {
        int mask = 1 << GymRaceTrackBuilder.RaceTrackLayer;
        Vector3 down = -GroundNormal;
        float reach = KartStats.Radius + 0.45f;
        if (Physics.Raycast(body.position, down, out RaycastHit hit, reach, mask, QueryTriggerInteraction.Ignore) &&
            hit.normal.y > 0.55f)
        {
            Grounded = true;
            GroundNormal = Vector3.Slerp(GroundNormal, hit.normal, 0.35f).normalized;
            return;
        }
        // Keep the last normal briefly; fall back to world up in the air.
        Grounded = false;
        GroundNormal = Vector3.Slerp(GroundNormal, Vector3.up, 0.06f).normalized;
    }

    private void UpdateDrift(float vF, float dt)
    {
        float steer = Mathf.Clamp(Input.Steer, -1f, 1f);
        if (!IsDrifting)
        {
            if (Input.DriftHeld && Grounded && vF > DriftMinSpeed && Mathf.Abs(steer) > 0.25f)
            {
                IsDrifting = true;
                DriftDirection = steer > 0f ? 1 : -1;
                DriftCharge = 0f;
                DriftTier = 0;
                body.linearVelocity += GroundNormal * HopSpeed;
                DriftStarted?.Invoke();
            }
            return;
        }

        if (!Input.DriftHeld || vF < DriftMinSpeed * 0.65f)
        {
            EndDrift(true);
            return;
        }
        bool into = steer * DriftDirection > 0.1f;
        DriftCharge += dt * (into ? 1.25f : 0.7f);
        int tier = 0;
        for (int i = 0; i < DriftTierSeconds.Length; i++)
        {
            if (DriftCharge >= DriftTierSeconds[i])
            {
                tier = i + 1;
            }
        }
        DriftTier = tier;
        BoostMeter = Mathf.Min(1f, BoostMeter + dt * 0.05f);
    }

    private void EndDrift(bool reward)
    {
        if (!IsDrifting)
        {
            return;
        }
        int tier = DriftTier;
        IsDrifting = false;
        DriftDirection = 0;
        DriftCharge = 0f;
        DriftTier = 0;
        if (reward && tier > 0)
        {
            MiniTurboTime = Mathf.Max(MiniTurboTime, MiniTurboSeconds[tier - 1]);
            BoostMeter = Mathf.Min(1f, BoostMeter + 0.05f * tier);
            MiniTurboStarted?.Invoke(tier);
        }
    }

    private void Steer(float vF, Vector3 n, float dt)
    {
        float steer = Mathf.Clamp(Input.Steer, -1f, 1f);
        float speedFactor = Mathf.Clamp01(Mathf.Abs(vF) / 6f);
        float highSpeedTrim = 1f - 0.22f * Mathf.Clamp01(Mathf.Abs(vF) / KartStats.TopSpeed);
        float rate;
        if (IsDrifting)
        {
            // Steering into the drift tightens it, counter-steer widens it.
            float shape = Mathf.Lerp(0.5f, 1.15f, Mathf.Clamp01(steer * DriftDirection * 0.5f + 0.5f));
            rate = DriftDirection * KartStats.Handling * shape;
        }
        else
        {
            rate = steer * KartStats.Handling * speedFactor * highSpeedTrim * (vF < -0.5f ? -1f : 1f);
        }
        if (!Grounded)
        {
            rate *= 0.35f;
        }
        // The heading stays flat; the road normal tilts it at use.
        Vector3 turned = Vector3.ProjectOnPlane(Quaternion.AngleAxis(rate * dt, n) * heading, Vector3.up);
        if (turned.sqrMagnitude > 1e-4f)
        {
            heading = turned.normalized;
        }
    }

    private void UpdateBoost(float dt)
    {
        if (MeterFilling && !IsBoosting)
        {
            BoostMeter = Mathf.Min(1f, BoostMeter + dt / BoostFillSeconds);
        }
        if (boostRequested && BoostMeter >= 1f && !IsBoosting)
        {
            BoostTime = BoostDuration;
            BoostMeter = 0f;
            BoostStarted?.Invoke();
        }
        BoostTime = Mathf.Max(0f, BoostTime - dt);
        MiniTurboTime = Mathf.Max(0f, MiniTurboTime - dt);
    }

    private void UpdateSlipstream(float vF, float dt)
    {
        bool drafting = false;
        if (vF > 12f)
        {
            Vector3 position = body.position;
            for (int i = 0; i < All.Count; i++)
            {
                GymRaceKart other = All[i];
                if (other == this || other.body == null)
                {
                    continue;
                }
                Vector3 offset = other.body.position - position;
                float distance = offset.magnitude;
                if (distance < 2.5f || distance > 14f)
                {
                    continue;
                }
                if (Vector3.Dot(offset / distance, heading) > 0.95f)
                {
                    drafting = true;
                    break;
                }
            }
        }
        slipstreamTimer = drafting ? slipstreamTimer + dt : 0f;
        if (InSlipstream && MeterFilling)
        {
            BoostMeter = Mathf.Min(1f, BoostMeter + dt * 0.08f);
        }
    }

    /// <summary>Rocket start / pad style burst.</summary>
    public void StartMiniTurbo(float seconds)
    {
        MiniTurboTime = Mathf.Max(MiniTurboTime, seconds);
    }

    public void SetBoostMeter(float value)
    {
        BoostMeter = Mathf.Clamp01(value);
    }

    public void Respawn(Vector3 roadPoint, Vector3 forward, Vector3 up)
    {
        EndDrift(false);
        BoostTime = 0f;
        MiniTurboTime = 0f;
        heading = Vector3.ProjectOnPlane(forward, Vector3.up).normalized;
        previousHeading = heading;
        GroundNormal = up;
        visualUp = up;
        body.position = roadPoint + up * (KartStats.Radius + 0.05f);
        transform.position = body.position;
        body.linearVelocity = Vector3.zero;
        UpdateVisual(1f);
    }

    private void OnCollisionEnter(Collision collision)
    {
        GymRaceKart other = collision.rigidbody != null ? collision.rigidbody.GetComponent<GymRaceKart>() : null;
        Vector3 point = collision.contactCount > 0 ? collision.GetContact(0).point : transform.position;
        Vector3 normal = collision.contactCount > 0 ? collision.GetContact(0).normal : Vector3.up;
        float strength = collision.relativeVelocity.magnitude;
        if (other != null)
        {
            NoteKartContact(other);
            KartHits++;
            Bumped?.Invoke(point, strength, other);
            return;
        }
        if (collision.gameObject.layer == GymRaceTrackBuilder.RaceTrackLayer && Vector3.Dot(normal, GroundNormal) < 0.5f)
        {
            WallHits++;
            body.linearVelocity *= WallSpeedKeep;
            Bumped?.Invoke(point, strength, null);
        }
    }

    private void OnCollisionStay(Collision collision)
    {
        GymRaceKart other = collision.rigidbody != null ? collision.rigidbody.GetComponent<GymRaceKart>() : null;
        if (other != null)
        {
            NoteKartContact(other);
        }
    }

    /// <summary>
    /// Head-on karts close at up to 60 m/s, more than a sphere radius per
    /// step: each kart takes half of any closing speed that would carry the
    /// pair past touching within this step, so they meet instead of overlap.
    /// </summary>
    private Vector3 LimitClosingSpeed(Vector3 velocity, float dt)
    {
        for (int i = 0; i < All.Count; i++)
        {
            GymRaceKart other = All[i];
            if (other == this || other.body == null || other.sphere == null)
            {
                continue;
            }
            Vector3 offset = Vector3.ProjectOnPlane(other.body.position - body.position, GroundNormal);
            float distance = offset.magnitude;
            float touching = sphere.radius + other.sphere.radius;
            if (distance < 1e-3f || distance > touching + 6f)
            {
                continue;
            }
            Vector3 direction = offset / distance;
            float closing = Vector3.Dot(velocity - other.body.linearVelocity, direction);
            float allowed = Mathf.Max(0f, distance - touching) / Mathf.Max(dt, 1e-4f);
            if (closing > allowed)
            {
                velocity -= direction * ((closing - allowed) * 0.5f);
            }
        }
        return velocity;
    }

    private void NoteKartContact(GymRaceKart other)
    {
        Vector3 away = Vector3.ProjectOnPlane(body.position - other.body.position, GroundNormal);
        if (away.sqrMagnitude < 1e-4f)
        {
            return;
        }
        kartContactNormal = away.normalized;
        kartContactUntil = Time.fixedTime + Time.fixedDeltaTime * 2.5f;
    }

    private void UpdateVisual(float blend)
    {
        if (Visual == null)
        {
            return;
        }
        float dt = Mathf.Max(Time.deltaTime, 1e-4f);
        visualUp = Vector3.Slerp(visualUp, GroundNormal, 1f - Mathf.Exp(-dt * 12f)).normalized;
        Vector3 travel = Vector3.Slerp(previousHeading, heading, blend);
        float targetDrift = IsDrifting ? DriftDirection * DriftVisualAngle : 0f;
        driftVisual = Mathf.Lerp(driftVisual, targetDrift, 1f - Mathf.Exp(-dt * 9f));
        steerVisual = Mathf.Lerp(steerVisual, Mathf.Clamp(Input.Steer, -1f, 1f), 1f - Mathf.Exp(-dt * 8f));
        Vector3 facing = Quaternion.AngleAxis(driftVisual, visualUp) * Vector3.ProjectOnPlane(travel, visualUp).normalized;
        float roll = -steerVisual * 3f - (IsDrifting ? DriftDirection * 5f : 0f);
        Quaternion rotation = Quaternion.LookRotation(facing, visualUp) * Quaternion.Euler(0f, 0f, roll);
        Visual.SetPositionAndRotation(transform.position - visualUp * KartStats.Radius, rotation);
    }
}
