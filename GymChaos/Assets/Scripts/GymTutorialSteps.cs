using UnityEngine;

/// <summary>What the tutorial is waiting for on the current step.</summary>
public enum GymTutorialSignal
{
    None,
    LookedAround,
    MovedAround,
    SprintAndJump,
    WentInside,
    LockerReady,
    FirstSet,
    Collected,
    JournalSeen,
    MetBodybuilder,
    ReachedRaceSign,
    RaceFinished
}

/// <summary>Where the marker and the chevrons point on the current step.</summary>
public enum GymTutorialTarget
{
    None,
    Entrance,
    Locker,
    Station,
    Counter,
    Bodybuilder,
    RaceSign
}

/// <summary>One line of the tutorial: what to do, which key, and what proves it.</summary>
public sealed class GymTutorialStep
{
    public string Title;
    public string Line;
    public string Key;
    public GymTutorialTarget Target;
    public GymTutorialSignal Signal;
    public int Reward;
}

/// <summary>
/// The first-save tutorial: a short chain that shows the gym, the locker, one
/// set, the protein counter, the daily goals, a member, the race sign and then
/// the easy race. Every step has one line, one key and one target, so the HUD
/// and the world marker never have to guess.
/// </summary>
public static class GymTutorialSteps
{
    public const int Count = 11;
    /// <summary>Step number a finished tutorial sits on; anything above is neutral.</summary>
    public const int Done = Count + 1;
    /// <summary>The easy race is the last active step.</summary>
    public const int RaceStep = 11;

    public static readonly GymTutorialStep[] All =
    {
        new GymTutorialStep
        {
            Title = "LOOK AROUND",
            Line = "Move the mouse to look over the parking lot.",
            Key = "MOUSE",
            Target = GymTutorialTarget.None,
            Signal = GymTutorialSignal.LookedAround,
            Reward = 10
        },
        new GymTutorialStep
        {
            Title = "WALK",
            Line = "WASD to walk toward the gym doors.",
            Key = "WASD",
            Target = GymTutorialTarget.Entrance,
            Signal = GymTutorialSignal.MovedAround,
            Reward = 10
        },
        new GymTutorialStep
        {
            Title = "SPRINT AND JUMP",
            Line = "Hold SHIFT to sprint, tap SPACE to jump.",
            Key = "SHIFT + SPACE",
            Target = GymTutorialTarget.Entrance,
            Signal = GymTutorialSignal.SprintAndJump,
            Reward = 10
        },
        new GymTutorialStep
        {
            Title = "GO INSIDE",
            Line = "Walk through the entrance and into the gym.",
            Key = "WASD",
            Target = GymTutorialTarget.Entrance,
            Signal = GymTutorialSignal.WentInside,
            Reward = 20
        },
        new GymTutorialStep
        {
            Title = "CHANGE OUTFIT",
            Line = "At the locker, press E and pick a look.",
            Key = "E",
            Target = GymTutorialTarget.Locker,
            Signal = GymTutorialSignal.LockerReady,
            Reward = 25
        },
        new GymTutorialStep
        {
            Title = "FIRST SET",
            Line = "Pick a station: E to set up, then hit your reps.",
            Key = "E",
            Target = GymTutorialTarget.Station,
            Signal = GymTutorialSignal.FirstSet,
            Reward = 30
        },
        new GymTutorialStep
        {
            Title = "COLLECT",
            Line = "Earn your first protein.com stamp at the counter.",
            Key = "F",
            Target = GymTutorialTarget.Counter,
            Signal = GymTutorialSignal.Collected,
            Reward = 25
        },
        new GymTutorialStep
        {
            Title = "DAILY GOALS",
            Line = "Open the journal with TAB to see today's goals.",
            Key = "TAB",
            Target = GymTutorialTarget.None,
            Signal = GymTutorialSignal.JournalSeen,
            Reward = 15
        },
        new GymTutorialStep
        {
            Title = "MEET A MEMBER",
            Line = "Talk to a bodybuilder: F when the prompt shows.",
            Key = "F",
            Target = GymTutorialTarget.Bodybuilder,
            Signal = GymTutorialSignal.MetBodybuilder,
            Reward = 20
        },
        new GymTutorialStep
        {
            Title = "RACE SIGN",
            Line = "Outside, walk to the race sign by the road.",
            Key = "WASD",
            Target = GymTutorialTarget.RaceSign,
            Signal = GymTutorialSignal.ReachedRaceSign,
            Reward = 20
        },
        new GymTutorialStep
        {
            Title = "EASY RACE",
            Line = "Your first race is Easy. The coach shows one key at a time.",
            Key = "W",
            Target = GymTutorialTarget.RaceSign,
            Signal = GymTutorialSignal.RaceFinished,
            Reward = 40
        }
    };

    public static bool IsActiveStep(int step) => step >= 1 && step <= Count;

    public static GymTutorialStep Get(int step) =>
        All[Mathf.Clamp(step - 1, 0, Count - 1)];

    public static int RewardFor(int step) => IsActiveStep(step) ? Get(step).Reward : 0;
}
