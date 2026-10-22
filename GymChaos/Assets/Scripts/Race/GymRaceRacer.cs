using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Lap and position bookkeeping for one kart: which branch it is on, its
/// distance along that branch, laps, lap times, wrong-way and stuck state.
/// </summary>
[RequireComponent(typeof(GymRaceKart))]
public sealed class GymRaceRacer : MonoBehaviour
{
    private const float SearchWindow = 14f;
    private const float StuckSeconds = 3f;
    private const float OffTrackFall = 6f;

    public string DisplayName = "Racer";
    public Color Color = Color.white;
    public bool IsPlayer;
    public int GridSlot;

    public GymRaceKart Kart { get; private set; }
    public bool OnShortcut { get; private set; }
    public float BranchDistance { get; private set; }
    /// <summary>Distance along the main lap (shortcut mapped onto the outer road).</summary>
    public float MainDistance { get; private set; }
    /// <summary>Start-line crossings; lap 1 begins with the first one.</summary>
    public int Crossings { get; private set; }
    public int CurrentLap => Mathf.Clamp(Crossings, 1, GymRaceTrackLayout.Laps);
    public bool Finished { get; private set; }
    public float FinishTime { get; private set; }
    public int FinishPlace { get; set; }
    public int Place { get; set; }
    public bool WrongWay { get; private set; }
    public float Lateral { get; private set; }
    public float LastSafeDistance { get; private set; }
    public bool LastSafeOnShortcut { get; private set; }
    public readonly List<float> LapTimes = new List<float>();
    public bool NeedsRespawn { get; private set; }
    /// <summary>Set by the AI before the fork; the branch is confirmed from position.</summary>
    public bool PreferShortcut;
    /// <summary>Head portrait for the standings (owned; null without a GPU).</summary>
    public Texture2D Headshot;

    private bool[] sectors = new bool[3];
    private float lapStartTime;
    private float wrongWayTimer;
    private float stuckTimer;
    private bool initialised;

    public float TotalProgress
    {
        get
        {
            float L = GymRaceTrackLayout.LapLength;
            float fromLine = GymRaceTrackLayout.Main.WrapDistance(MainDistance - GymRaceTrackLayout.StartLineDistance);
            // Before the first crossing the grid sits just behind the line.
            if (Crossings == 0)
            {
                return fromLine - L;
            }
            return (Crossings - 1) * L + fromLine;
        }
    }

    private void Awake()
    {
        Kart = GetComponent<GymRaceKart>();
    }

    private void OnDestroy()
    {
        if (Headshot != null)
        {
            Destroy(Headshot);
        }
    }

    public void ResetProgress(float raceTime)
    {
        OnShortcut = false;
        BranchDistance = GymRaceTrackLayout.Main.ClosestDistance(transform.position);
        MainDistance = BranchDistance;
        LastSafeDistance = BranchDistance;
        LastSafeOnShortcut = false;
        Crossings = 0;
        Finished = false;
        FinishTime = 0f;
        FinishPlace = 0;
        LapTimes.Clear();
        sectors = new bool[3];
        lapStartTime = raceTime;
        wrongWayTimer = 0f;
        stuckTimer = 0f;
        NeedsRespawn = false;
        initialised = true;
    }

    /// <summary>Advance tracking; returns true on the frame the racer finishes.</summary>
    public bool Tick(float raceTime, float dt, bool racing)
    {
        if (!initialised)
        {
            ResetProgress(raceTime);
        }
        GymRaceTrackPath main = GymRaceTrackLayout.Main;
        GymRaceTrackPath shortcut = GymRaceTrackLayout.Shortcut;
        Vector3 position = Kart.Body != null ? Kart.Body.position : transform.position;
        float previousMain = MainDistance;
        float lateral;
        float vertical;

        if (OnShortcut)
        {
            BranchDistance = shortcut.ClosestDistance(position, BranchDistance, SearchWindow, out lateral, out vertical);
            if (BranchDistance >= shortcut.Length - 2f)
            {
                OnShortcut = false;
                BranchDistance = main.ClosestDistance(position, GymRaceTrackLayout.ShortcutMergeDistance, SearchWindow, out lateral, out vertical);
            }
            else
            {
                // Both roads run side by side after the fork: a kart that
                // was tagged onto the cut but keeps right on the outer road
                // goes back to the main lap instead of being respawned.
                float hint = GymRaceTrackLayout.MainEquivalentDistance(true, BranchDistance);
                float mainS = main.ClosestDistance(position, hint, SearchWindow * 3f, out float mainLateral, out float mainVertical);
                main.Sample(mainS, out _, out _, out _, out float mainHalf);
                if (GymRaceTrackLayout.IsOnBypassedSpan(mainS) && Mathf.Abs(mainLateral) < mainHalf + 1.5f &&
                    Mathf.Abs(mainLateral) + Mathf.Abs(mainVertical) < Mathf.Abs(lateral) + Mathf.Abs(vertical))
                {
                    OnShortcut = false;
                    BranchDistance = mainS;
                    lateral = mainLateral;
                    vertical = mainVertical;
                }
            }
        }
        else
        {
            BranchDistance = main.ClosestDistance(position, BranchDistance, SearchWindow, out lateral, out vertical);
            float sinceFork = main.WrapDistance(BranchDistance - GymRaceTrackLayout.ShortcutForkDistance);
            if (sinceFork > 6f && sinceFork < 30f)
            {
                float shortS = shortcut.ClosestDistance(position, -1f, 0f, out float shortLateral, out float shortVertical);
                if (shortS < shortcut.Length * 0.5f &&
                    Mathf.Abs(shortLateral) + Mathf.Abs(shortVertical) < Mathf.Abs(lateral) + Mathf.Abs(vertical))
                {
                    OnShortcut = true;
                    BranchDistance = shortS;
                    lateral = shortLateral;
                    vertical = shortVertical;
                }
            }
        }
        Lateral = lateral;
        MainDistance = GymRaceTrackLayout.MainEquivalentDistance(OnShortcut, BranchDistance);

        GymRaceTrackPath path = OnShortcut ? shortcut : main;
        path.Sample(BranchDistance, out Vector3 center, out Vector3 tangent, out _, out float halfWidth);
        bool onRoad = Mathf.Abs(lateral) < halfWidth + 1.5f && vertical > -OffTrackFall;
        if (onRoad && Kart.Grounded)
        {
            LastSafeDistance = BranchDistance;
            LastSafeOnShortcut = OnShortcut;
        }

        if (!racing || Finished)
        {
            return false;
        }

        // Sectors guard against crossing the line backwards for a lap.
        float L = main.Length;
        float fromLine = main.WrapDistance(MainDistance - GymRaceTrackLayout.StartLineDistance);
        for (int i = 0; i < sectors.Length; i++)
        {
            if (fromLine > L * (i + 1) / 4f && fromLine < L * (i + 1) / 4f + 40f)
            {
                sectors[i] = true;
            }
        }

        float delta = MainDistance - previousMain;
        float startLine = GymRaceTrackLayout.StartLineDistance;
        bool crossedForward = Crossed(previousMain, MainDistance, startLine, L, delta);
        bool finishedNow = false;
        if (crossedForward)
        {
            bool fullLap = sectors[0] && sectors[1] && sectors[2];
            if (Crossings == 0 || fullLap)
            {
                if (Crossings > 0)
                {
                    LapTimes.Add(raceTime - lapStartTime);
                }
                Crossings++;
                lapStartTime = raceTime;
                sectors = new bool[3];
                if (Crossings > GymRaceTrackLayout.Laps)
                {
                    Finished = true;
                    FinishTime = raceTime;
                    finishedNow = true;
                }
            }
        }

        float along = Vector3.Dot(Kart.Body != null ? Kart.Body.linearVelocity : Vector3.zero, tangent);
        wrongWayTimer = along < -3f ? wrongWayTimer + dt : Mathf.Max(0f, wrongWayTimer - dt * 2f);
        WrongWay = wrongWayTimer > 1f;

        bool trying = Kart.Input.Throttle > 0.5f || Kart.Input.Brake > 0.5f;
        stuckTimer = trying && Kart.Speed < 1.2f ? stuckTimer + dt : 0f;
        NeedsRespawn = stuckTimer > StuckSeconds || vertical < -OffTrackFall ||
            Mathf.Abs(lateral) > halfWidth + 6f;
        return finishedNow;
    }

    private static bool Crossed(float previous, float current, float line, float length, float delta)
    {
        // Forward motion only: a wrap backwards (delta > L/2) is not a crossing.
        if (Mathf.Abs(delta) > length * 0.5f)
        {
            delta = delta > 0f ? delta - length : delta + length;
        }
        if (delta <= 0f || delta > 30f)
        {
            return false;
        }
        float a = Mathf.Repeat(previous - line, length);
        float b = Mathf.Repeat(current - line, length);
        return a > length - 30f && b < 30f;
    }

    /// <summary>Puts the kart back on the last safe road point.</summary>
    public void RespawnAtSafePoint()
    {
        GymRaceTrackPath path = LastSafeOnShortcut ? GymRaceTrackLayout.Shortcut : GymRaceTrackLayout.Main;
        float s = Mathf.Max(0f, LastSafeDistance - (path.Closed ? 4f : 0f));
        path.Sample(s, out Vector3 point, out Vector3 tangent, out Vector3 right, out _);
        Vector3 up = Vector3.Cross(tangent, right).normalized;
        Kart.Respawn(point, tangent, up);
        OnShortcut = LastSafeOnShortcut;
        BranchDistance = s;
        stuckTimer = 0f;
        NeedsRespawn = false;
    }
}
