using System;
using UnityEngine;

/// <summary>Body actions carried by <see cref="GymNetPose.a"/>.</summary>
public static class GymNetAction
{
    public const int None = 0, PunchLeft = 1, PunchRight = 2, ThrowLight = 3, ThrowHard = 4,
        Jump = 5, Crouch = 6, Driving = 7;
}

/// <summary>One player's pose on the wire (about 12 per second).</summary>
[Serializable]
public struct GymNetPose
{
    public float x, y, z;
    // Yaw in degrees.
    public float r;
    // Horizontal speed in m/s; drives the avatar's walk cycle.
    public float s;
    // Health 0..1; the avatar's bar shows once a player is hurt.
    public float h;
    // 1 while the player is dead: the others see a corpse where it fell.
    public float d;
    // Appearance: the sender's class and outfit, so the other screens draw the
    // same body in the same clothes. Indices into the game's own enums and
    // class catalogue; a value out of range falls back to the baseline look.
    public int classIndex;
    public int shirt;
    public int top;
    public int bottom;
    public int bottomColor;
    public int headwear;
    // Body action (GymNetAction) and its clip phase 0..1, so a punch, throw,
    // jump, crouch or drive plays on the other screens.
    public int a;
    public float p;
    // Metres from the sender's root down to its feet: the root is the controller centre.
    public float f;
    public Vector3 Position => new Vector3(x, y, z);
}

/// <summary>
/// Renders a remote player slightly in the past so network jitter never shows:
/// poses are kept with their arrival time and blended between the two that
/// straddle (now - delay). Past the newest pose it holds still.
/// </summary>
public sealed class GymNetInterpolator
{
    public const float DelaySeconds = 0.12f;
    private const int Capacity = 8;

    private readonly double[] times = new double[Capacity];
    private readonly GymNetPose[] poses = new GymNetPose[Capacity];
    private int count;

    public int Count => count;

    public void Add(double arrival, GymNetPose pose)
    {
        if (count == Capacity)
        {
            Array.Copy(times, 1, times, 0, Capacity - 1);
            Array.Copy(poses, 1, poses, 0, Capacity - 1);
            count--;
        }
        times[count] = arrival;
        poses[count] = pose;
        count++;
    }

    public bool TrySample(double now, out GymNetPose pose)
    {
        pose = default;
        if (count == 0) return false;
        double target = now - DelaySeconds;
        if (target <= times[0] || count == 1) { pose = poses[0]; return true; }
        for (int i = 1; i < count; i++)
        {
            if (target > times[i]) continue;
            float t = (float)((target - times[i - 1]) / Math.Max(1e-4, times[i] - times[i - 1]));
            GymNetPose a = poses[i - 1];
            GymNetPose b = poses[i];
            pose = new GymNetPose
            {
                x = Mathf.Lerp(a.x, b.x, t), y = Mathf.Lerp(a.y, b.y, t), z = Mathf.Lerp(a.z, b.z, t),
                r = Mathf.LerpAngle(a.r, b.r, t), s = Mathf.Lerp(a.s, b.s, t), h = Mathf.Lerp(a.h, b.h, t),
                d = t < 0.5f ? a.d : b.d,
                // Actions hop between clips: the later pose wins, its phase is not blended.
                a = b.a, p = b.p, f = Mathf.Lerp(a.f, b.f, t),
                classIndex = b.classIndex, shirt = b.shirt, top = b.top, bottom = b.bottom,
                bottomColor = b.bottomColor, headwear = b.headwear
            };
            return true;
        }
        pose = poses[count - 1];
        return true;
    }
}
