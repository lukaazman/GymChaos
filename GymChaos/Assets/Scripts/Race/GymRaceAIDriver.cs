using UnityEngine;

/// <summary>
/// Racing AI: follows the centre line with a personal lane offset, slows for
/// tight corners, drifts through the sharp ones, fires the boost on straights
/// and edges away from karts close ahead. The same driver serves as the
/// verifier autopilot for the player kart.
/// </summary>
[RequireComponent(typeof(GymRaceRacer))]
public sealed class GymRaceAIDriver : MonoBehaviour
{
    public float LaneOffset;
    public float Skill = 1f;
    public float Aggression;
    public bool AllowDrift = true;
    public bool AllowBoost = true;
    /// <summary>-1 decide randomly per lap, 0 always outer road, 1 always shortcut.</summary>
    public int ShortcutChoice = -1;

    private const float LateralGrip = 17f;
    private GymRaceRacer racer;
    private GymRaceKart kart;
    private int decidedForLap = -1;
    private float wobble;

    private void Awake()
    {
        racer = GetComponent<GymRaceRacer>();
        kart = GetComponent<GymRaceKart>();
        wobble = Random.value * 10f;
    }

    public GymRaceDriveInput Think(float raceTime)
    {
        GymRaceDriveInput input = new GymRaceDriveInput();
        if (kart == null || kart.Body == null)
        {
            return input;
        }
        DecideBranch();
        GymRaceTrackPath path = racer.OnShortcut ? GymRaceTrackLayout.Shortcut : GymRaceTrackLayout.Main;
        float s = racer.BranchDistance;
        float speed = Mathf.Max(kart.ForwardSpeed, 0f);
        Vector3 position = kart.Body.position;

        // Look-ahead target with the personal lane (narrowed where the road is).
        float lookAhead = 7f + speed * 0.45f;
        Vector3 target = TargetPoint(path, s + lookAhead, out float halfWidth, out bool switchedToMain);
        float lane = Mathf.Clamp(LaneOffset + Mathf.Sin(raceTime * 0.35f + wobble) * 0.6f, -halfWidth + 1.6f, halfWidth - 1.6f);
        Vector3 right = PathRight(path, s + lookAhead, switchedToMain);
        target += right * lane;
        target += AvoidOffset(position, right);

        Vector3 to = Vector3.ProjectOnPlane(target - position, Vector3.up);
        float angle = Vector3.SignedAngle(kart.Heading, to, Vector3.up);
        input.Steer = Mathf.Clamp(angle / 28f, -1f, 1f);

        // Speed limit from the tightest curvature over the next stretch.
        float curvature = MaxCurvature(path, s + 4f, 10f + speed * 1.1f);
        float cornerSpeed = curvature > 1e-4f ? Mathf.Sqrt(LateralGrip * Skill / curvature) : 999f;
        float limit = Mathf.Min(kart.CurrentMaxSpeed > 0f ? kart.CurrentMaxSpeed : kart.KartStats.TopSpeed, cornerSpeed);
        if (speed > limit + 1.5f)
        {
            input.Brake = Mathf.Clamp01((speed - limit) / 6f);
        }
        else
        {
            input.Throttle = speed < limit ? 1f : 0.4f;
        }

        // Drift through sharp corners at speed.
        float near = MaxCurvature(path, s + 2f, 14f);
        if (AllowDrift && near > 1f / 30f && speed > 13f && Mathf.Abs(input.Steer) > 0.35f)
        {
            input.DriftHeld = true;
            input.Brake = 0f;
            input.Throttle = 1f;
        }
        else if (kart.IsDrifting && near > 1f / 45f)
        {
            input.DriftHeld = true;
        }

        if (AllowBoost && kart.BoostMeter >= 1f && MaxCurvature(path, s, 45f) < 1f / 70f)
        {
            input.BoostPressed = true;
        }
        return input;
    }

    private void DecideBranch()
    {
        int lap = racer.Crossings;
        if (decidedForLap == lap)
        {
            return;
        }
        decidedForLap = lap;
        racer.PreferShortcut = ShortcutChoice < 0 ? Random.value < 0.5f : ShortcutChoice == 1;
    }

    /// <summary>Point on the route the racer will take, following the shortcut if it prefers it.</summary>
    private Vector3 TargetPoint(GymRaceTrackPath path, float s, out float halfWidth, out bool switchedToMain)
    {
        switchedToMain = false;
        GymRaceTrackPath main = GymRaceTrackLayout.Main;
        GymRaceTrackPath shortcut = GymRaceTrackLayout.Shortcut;
        if (path == shortcut && s > shortcut.Length)
        {
            switchedToMain = true;
            float onMain = GymRaceTrackLayout.ShortcutMergeDistance + (s - shortcut.Length);
            main.Sample(onMain, out Vector3 p, out _, out _, out halfWidth);
            return p;
        }
        if (path == main && racer.PreferShortcut)
        {
            float fork = GymRaceTrackLayout.ShortcutForkDistance;
            float past = main.WrapDistance(s - fork);
            bool nearFork = main.WrapDistance(fork - racer.BranchDistance) < 50f ||
                main.WrapDistance(racer.BranchDistance - fork) < 30f;
            if (nearFork && past < 60f)
            {
                shortcut.Sample(past, out Vector3 p, out _, out _, out halfWidth);
                return p;
            }
        }
        path.Sample(s, out Vector3 point, out _, out _, out halfWidth);
        return point;
    }

    private Vector3 PathRight(GymRaceTrackPath path, float s, bool switchedToMain)
    {
        if (switchedToMain)
        {
            path = GymRaceTrackLayout.Main;
            s = GymRaceTrackLayout.ShortcutMergeDistance + (s - GymRaceTrackLayout.Shortcut.Length);
        }
        path.Sample(s, out _, out _, out Vector3 right, out _);
        right.y = 0f;
        return right.normalized;
    }

    private Vector3 AvoidOffset(Vector3 position, Vector3 right)
    {
        Vector3 offset = Vector3.zero;
        for (int i = 0; i < GymRaceKart.All.Count; i++)
        {
            GymRaceKart other = GymRaceKart.All[i];
            if (other == kart || other.Body == null)
            {
                continue;
            }
            Vector3 delta = other.Body.position - position;
            float ahead = Vector3.Dot(delta, kart.Heading);
            float side = Vector3.Dot(delta, right);
            if (ahead < 0f || ahead > 12f || Mathf.Abs(side) > 3.2f || Mathf.Abs(delta.y) > 2.5f)
            {
                continue;
            }
            // Aggressive drivers lean into the other kart instead.
            float push = (3.2f - Mathf.Abs(side)) * (side >= 0f ? -1f : 1f);
            offset += right * push * (1f - Aggression * 1.6f) * 0.8f;
        }
        return offset;
    }

    public static float MaxCurvature(GymRaceTrackPath path, float from, float span)
    {
        float max = 0f;
        int start = path.IndexAtDistance(from);
        int steps = Mathf.Max(1, Mathf.CeilToInt(span));
        for (int k = 0; k < steps; k += 2)
        {
            int index = path.Closed ? path.Wrap(start + k) : Mathf.Min(start + k, path.Count - 1);
            max = Mathf.Max(max, GymRaceTrackBuilder.Curvature(path, index));
        }
        return max;
    }
}
