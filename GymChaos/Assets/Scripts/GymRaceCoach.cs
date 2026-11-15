using UnityEngine;

/// <summary>
/// Teaches the race one key at a time. The old bottom line listed every control
/// at once; this shows the key that matters right now, for a beat or two, and
/// only while it is useful. One instance per race.
/// </summary>
public sealed class GymRaceCoach
{
    public const float BeatSeconds = 2.5f;

    /// <summary>Keys in the order a new driver needs them.</summary>
    public static readonly string[] Keys = { "W", "A / D", "SPACE / RMB", "SHIFT / LMB", "Q", "R" };

    public static readonly string[] Lines =
    {
        "HOLD THE GAS",
        "STEER THROUGH THE BEND",
        "DRIFT TO CHARGE A BOOST",
        "BOOST IS FULL",
        "A RIVAL IS ON YOUR TAIL",
        "STUCK? RESPAWN"
    };

    public const string FinalLapLine = "LAST LAP   BRING IT HOME";

    // Built once: Tick runs every frame and a stuck kart asks for its line each one.
    private static readonly string[] ShownLines = BuildShown();

    private int beat;
    private float shownAt = -999f;
    private float stuckSince = -1f;
    private bool finalLapShown;
    private string line = string.Empty;

    public int Beat => beat;
    public string Line => line;
    public bool Shown => line.Length > 0;

    public void Reset()
    {
        beat = 0;
        shownAt = -999f;
        stuckSince = -1f;
        finalLapShown = false;
        line = string.Empty;
    }

    /// <summary>One call on the last lap; the line then holds for its beat.</summary>
    public string NotifyFinalLap()
    {
        if (finalLapShown)
        {
            return line;
        }
        finalLapShown = true;
        line = FinalLapLine;
        shownAt = Time.time;
        return line;
    }

    /// <summary>
    /// Advance one frame and return the line to show. Empty means the slot stays
    /// clear: the coach only speaks when the next key is the one that helps.
    /// </summary>
    public string Tick(float now, float speed, float steer, float boostMeter,
        bool bendAhead, bool rivalBehind, bool stuck)
    {
        if (stuck)
        {
            if (stuckSince < 0f)
            {
                stuckSince = now;
            }
            if (now - stuckSince > 0.2f)
            {
                // The respawn tip does not count as a lesson: the ones still to come stay.
                shownAt = now;
                return line = ShownLines[5];
            }
        }
        else
        {
            stuckSince = -1f;
        }

        if (line.Length > 0 && now - shownAt < BeatSeconds)
        {
            return line;
        }

        if (beat == 0)
        {
            return Show(now, 0);
        }
        if (beat == 1 && (speed > 6f || Mathf.Abs(steer) > 0.2f))
        {
            return Show(now, 1);
        }
        if (beat == 2 && speed > 10f && bendAhead && boostMeter < 0.99f)
        {
            return Show(now, 2);
        }
        if (beat == 3 && boostMeter >= 0.99f)
        {
            return Show(now, 3);
        }
        if (beat == 4 && rivalBehind && speed > 8f)
        {
            return Show(now, 4);
        }
        return line = string.Empty;
    }

    private string Show(float now, int index)
    {
        beat = Mathf.Clamp(index + 1, 1, Keys.Length - 1);
        shownAt = now;
        line = ShownLines[index];
        return line;
    }

    private static string[] BuildShown()
    {
        string[] shown = new string[Keys.Length];
        for (int i = 0; i < shown.Length; i++)
        {
            shown[i] = Keys[i] + "   " + Lines[i];
        }
        return shown;
    }
}
