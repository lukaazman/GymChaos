using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The city race minigame. The RACE sign in the parking lot starts it: the
/// track is built, the towers on the road disappear, six cars line up on the
/// visitor road (Zyzz, Cbum, the police, Jay Cutler, Arnold and the player's
/// buggy) with the player already behind the wheel, the camera flies over
/// the lap, and after the 3-2-1 countdown it is three laps around the city.
/// Results award XP; the player then races again or returns to the gym.
/// </summary>
public sealed class GymRaceDirector : MonoBehaviour
{
    public enum Phase
    {
        Idle,
        Building,
        Boarding,
        Countdown,
        Racing,
        Finishing,
        Results,
        Exiting,
        Teardown
    }

    private struct RacerSpec
    {
        public string Name;
        public BodybuilderIdentity Identity;
        public bool Police;
        public bool Player;
        public Color Color;
        public GymRaceKart.Stats Stats;
        public float Lane;
        public float Aggression;
        public float Skill;
    }

    public static readonly int[] PlaceExperience = { 300, 200, 140, 100, 80, 60 };
    public const int FirstWinBonus = 250;
    public const int BestLapBonus = 40;
    public const int WinReputation = 3;
    private const float FlyoverSeconds = 2.6f;
    private const float FinishGraceSeconds = 8f;
    private const float ChoiceInputDelay = 1f;
    private const float RaceTimeLimit = 360f;
    private const float RubberBandRange = 70f;

    public static GymRaceDirector Instance { get; private set; }
    public static bool IsRaceActive => Instance != null && Instance.phase != Phase.Idle;
    /// <summary>The BACK TO GYM / RACE AGAIN popup is up and owns the pointer.</summary>
    public static bool IsChoiceOpen => IsRaceActive && Instance.hud != null && Instance.hud.ChoiceVisible;

    private PlayerMovement player;
    private Phase phase = Phase.Idle;
    private GymRaceTrack track;
    private GymRaceHud hud;
    private GymRaceChaseCamera chase;
    private readonly List<GymRaceVehicleFactory.Car> cars = new List<GymRaceVehicleFactory.Car>();
    private readonly List<EnemyFighter> drivers = new List<EnemyFighter>();
    private readonly List<GymRaceRacer> racers = new List<GymRaceRacer>();
    private readonly List<GymRaceRacer> ordered = new List<GymRaceRacer>();
    private GymRaceRacer playerRacer;
    private GymRaceVehicleFactory.Car playerCar;
    private PlayerVehicleOccupant playerOccupant;
    private bool playerSeated;
    private bool autopilot;
    private float raceStartTime;
    private float playerFinishTime = -1f;
    private int lastPlayerCrossings;
    private float firstThrottleAt = -1f;
    private float countdownStart;
    private readonly Stack<IEnumerator> runStack = new Stack<IEnumerator>();
    private Vector3 savedCameraLocalPosition;
    private Quaternion savedCameraLocalRotation;
    private float resultBestLap;
    private bool resultFinished;
    private float resultFinishTime;

    public Phase CurrentPhase => phase;
    public IReadOnlyList<GymRaceRacer> Racers => racers;
    /// <summary>Drivers by grid slot (null for the player's slot).</summary>
    public IReadOnlyList<EnemyFighter> Drivers => drivers;
    public GymRaceRacer PlayerRacer => playerRacer;
    public IReadOnlyList<GymRaceVehicleFactory.Car> Cars => cars;
    public GymRaceChaseCamera ChaseCamera => chase;
    public GymRaceHud Hud => hud;
    public bool PlayerSeatedForVerification => playerSeated;
    public int LastAwardedExperience { get; private set; }
    public int LastPlayerPlace { get; private set; }
    public string LastRaceSummary { get; private set; }
    public bool AutoContinueResults { get; set; }
    /// <summary>Verification: the next results screen picks RACE AGAIN once.</summary>
    public bool AutoRaceAgainOnce { get; set; }
    public int RacesRunForVerification { get; private set; }

    public string BlockReasonForVerification => BlockReason();

    public string SignPrompt
    {
        get
        {
            string blocked = BlockReason();
            return blocked == null ? "START CITY RACE" : "RACE: " + blocked;
        }
    }

    public static GymRaceDirector CreateForScene(PlayerMovement targetPlayer)
    {
        if (Instance != null)
        {
            return Instance;
        }
        GameObject go = new GameObject("Race Director (Runtime)");
        GymRaceDirector director = go.AddComponent<GymRaceDirector>();
        director.player = targetPlayer;
        Instance = director;
        return director;
    }

    private void Update()
    {
        if (GymRaceSign.Instance == null && GymOutdoorBuilder.IsBuilt)
        {
            GymRaceSign.Create();
        }
        // Paused: the race clock stands still and no race input is read.
        if (runStack.Count > 0 && !GymPauseMenu.IsVisible)
        {
            if (phase != Phase.Idle && phase != Phase.Teardown && player != null && player.IsDead)
            {
                runStack.Clear();
                Debug.LogWarning("GYMCHAOS_RACE_ABORTED reason=player-down");
                AbortRace();
                return;
            }
            StepRun();
        }
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
        GymVisitorDirector.RaceTrafficHold = false;
        if (phase != Phase.Idle)
        {
            // Scene unload mid-race: leave no carved city or hidden walls behind.
            GymRaceCityCarver.Restore();
            GymRaceCorridorClearance.Restore();
        }
    }

    private string BlockReason()
    {
        if (phase != Phase.Idle) return "IN PROGRESS";
        if (player == null) return "NO DRIVER";
        if (player.IsDead) return "YOU ARE DOWN";
        if (EnemyFighter.IsFightActive) return "FINISH YOUR FIGHT";
        if (GymPoliceDirector.IsDispatchActive) return "POLICE NEARBY";
        if (player.IsHoldingItem || player.IsCarryingCorpse) return "HANDS FULL";
        if (!GymRaceCityCarver.CityReady()) return "CITY LOADING";
        return null;
    }

    public bool TryStartFromSign(PlayerMovement source)
    {
        if (source != null)
        {
            player = source;
        }
        string blocked = BlockReason();
        if (blocked != null)
        {
            Debug.Log($"GYMCHAOS_RACE_BLOCKED reason={blocked}");
            GymHud.Active?.PushNotice("Race unavailable: " + blocked.ToLowerInvariant(), 3f, 1);
            GymAudio.Play2D(GymSoundEffect.UiError);
            return false;
        }
        GymAudio.Play2D(GymSoundEffect.UiConfirm);
        StartRun(RaceRoutine(false));
        return true;
    }

    /// <summary>Verification: start without the sign; the player kart can drive itself.</summary>
    public bool StartRaceForVerification(PlayerMovement source, bool autopilotPlayer)
    {
        player = source != null ? source : player;
        if (BlockReason() != null)
        {
            return false;
        }
        AutoContinueResults = true;
        StartRun(RaceRoutine(autopilotPlayer));
        return true;
    }

    private IEnumerator RaceRoutine(bool autopilotPlayer)
    {
        autopilot = autopilotPlayer;
        phase = Phase.Building;
        GymVisitorDirector.RaceTrafficHold = true;
        player.SetCinematicLock(true);
        savedCameraLocalPosition = player.playerCamera.transform.localPosition;
        savedCameraLocalRotation = player.playerCamera.transform.localRotation;
        hud = GymRaceHud.Create();
        hud.SetRaceWidgetsVisible(false);

        // The first race is always Easy; once a win opens Medium the sign asks.
        int unlocked = UnlockedDifficulty;
        difficulty = 0;
        if (unlocked > 0 && !autopilot)
        {
            yield return PickDifficulty(unlocked, false);
            player.SetCursorCaptured(true);
            if (pickedDifficulty < 0)
            {
                Destroy(hud.gameObject);
                hud = null;
                player.SetCinematicLock(false);
                GymVisitorDirector.RaceTrafficHold = false;
                phase = Phase.Idle;
                yield break;
            }
            difficulty = pickedDifficulty;
        }
        // A visitor car already on the road would drive through the grid. The
        // hold stops new cars, so wait for the last one to park instead of
        // refusing the race (it was refused most of the time on slow WebGL).
        float trafficWait = Time.time + 25f;
        bool noticed = false;
        while (GymVisitorVehicle.IsGroundTrafficActive && Time.time < trafficWait)
        {
            if (!noticed)
            {
                GymHud.Active?.PushNotice("Race: clearing the road", 3f, 1);
                noticed = true;
            }
            yield return null;
        }
        hud.ShowBanner("CITY GRAND PRIX  " + DifficultyNames[difficulty], PersonaMenuStyle.Gold, 3.5f);

        GymRaceCorridorClearance.Clear();
        track = GymRaceTrackBuilder.Build(transform);

        chase = player.playerCamera.GetComponent<GymRaceChaseCamera>();
        if (chase == null)
        {
            chase = player.playerCamera.gameObject.AddComponent<GymRaceChaseCamera>();
        }

        SpawnField();
        float buildStart = Time.time;
        while (!AllCarsReady() && Time.time - buildStart < 15f)
        {
            yield return null;
        }
        yield return GymRaceCityFiller.Fill();
        yield return GymRaceCityCarver.CarveRoutine();
        PlayCrumbles();

        CreateKarts();
        // No walk to the car: the player starts the race behind the wheel.
        SeatPlayerNow();
        CaptureHeadshots();
        chase.Begin(player.playerCamera, playerRacer.Kart);
        chase.StartFlyover(FlyoverSeconds);
        yield return Wait(FlyoverSeconds);
        chase.StartChase();
        yield return Wait(0.4f);

        while (true)
        {
            yield return RunRace();

            // ------------------------------------------------------- results
            phase = Phase.Results;
            List<GymRaceHud.ResultRow> rows = BuildResults();
            int xp = ComputeExperience(out string xpLine);
            resultBestLap = BestPlayerLap();
            resultFinished = playerRacer != null && playerRacer.Finished;
            resultFinishTime = playerRacer != null ? playerRacer.FinishTime : 0f;
            AwardResults(xp);
            RacesRunForVerification++;
            player.SetCursorCaptured(false);
            chase.StartOrbit();
            GymRaceHud.Choice choice = GymRaceHud.Choice.None;
            while (choice == GymRaceHud.Choice.None)
            {
                hud.ShowResults(rows, xpLine);
                hud.ShowChoice(0, false);
                float shownAt = Time.time;
                while (choice == GymRaceHud.Choice.None)
                {
                    CoastAll();
                    choice = ReadChoice(Time.time - shownAt);
                    yield return null;
                }
                hud.HideChoice();
                hud.HideResults();
                // RACE AGAIN opens the difficulty screen once Medium is won;
                // BACK there returns to the results.
                int open = UnlockedDifficulty;
                if (choice == GymRaceHud.Choice.RaceAgain && open > 0 && !autopilot)
                {
                    yield return PickDifficulty(open, true);
                    if (pickedDifficulty < 0)
                    {
                        choice = GymRaceHud.Choice.None;
                        continue;
                    }
                    difficulty = pickedDifficulty;
                }
            }
            player.SetCursorCaptured(true);
            if (choice == GymRaceHud.Choice.RaceAgain)
            {
                ResetToGrid();
                continue;
            }
            break;
        }

        // ------------------------------------------------------------ teardown
        phase = Phase.Teardown;
        StopAll();
        Teardown();
        phase = Phase.Idle;
    }

    private IEnumerator RunRace()
    {
        // ----------------------------------------------------------- countdown
        phase = Phase.Countdown;
        hud.SetRaceWidgetsVisible(true);
        hud.SetHint("W GAS   S BRAKE   A/D STEER   SPACE / RMB DRIFT   SHIFT / LMB BOOST   Q LOOK BACK   R RESPAWN");
        countdownStart = Time.time;
        firstThrottleAt = -1f;
        for (int i = 0; i < racers.Count; i++)
        {
            racers[i].ResetProgress(Time.time);
        }
        for (int step = 0; step < 3; step++)
        {
            track.SetCountdownLights(step + 1, false);
            hud.ShowCenter((3 - step).ToString(), PersonaMenuStyle.Ink, 0.95f);
            GymAudio.Play2D(GymSoundEffect.RaceBeep, 0.8f);
            float until = countdownStart + step + 1f;
            while (Time.time < until)
            {
                ReadPlayerDuringCountdown();
                UpdateHud();
                yield return null;
            }
        }
        track.SetCountdownLights(0, true);
        hud.ShowCenter("GO!", PersonaMenuStyle.Gold, 1.1f);
        GymAudio.Play2D(GymSoundEffect.RaceGo, 0.9f);
        raceStartTime = Time.time;
        StartRacing();

        // -------------------------------------------------------------- racing
        phase = Phase.Racing;
        hud.SetHint(string.Empty);
        while (phase == Phase.Racing || phase == Phase.Finishing)
        {
            TickRace();
            yield return null;
        }
    }

    /// <summary>Verification: end the current race now and go to the results.</summary>
    public void ForceResultsForVerification()
    {
        if (phase == Phase.Racing || phase == Phase.Finishing)
        {
            phase = Phase.Results;
        }
    }

    private GymRaceHud.Choice ReadChoice(float shownFor)
    {
        if (hud.ChoiceMade != GymRaceHud.Choice.None)
        {
            return hud.ChoiceMade;
        }
        if (AutoRaceAgainOnce && shownFor > 2f)
        {
            AutoRaceAgainOnce = false;
            return GymRaceHud.Choice.RaceAgain;
        }
        if (AutoContinueResults && shownFor > 2.5f)
        {
            return GymRaceHud.Choice.BackToGym;
        }
        // A held drift or gas key from the finish must not pick an option.
        if (shownFor < ChoiceInputDelay)
        {
            return GymRaceHud.Choice.None;
        }
#if ENABLE_INPUT_SYSTEM
        UnityEngine.InputSystem.Keyboard keyboard = UnityEngine.InputSystem.Keyboard.current;
        if (keyboard != null)
        {
            if (keyboard.aKey.wasPressedThisFrame || keyboard.leftArrowKey.wasPressedThisFrame ||
                keyboard.wKey.wasPressedThisFrame || keyboard.upArrowKey.wasPressedThisFrame)
            {
                hud.MoveChoice(-1);
            }
            if (keyboard.dKey.wasPressedThisFrame || keyboard.rightArrowKey.wasPressedThisFrame ||
                keyboard.sKey.wasPressedThisFrame || keyboard.downArrowKey.wasPressedThisFrame ||
                keyboard.tabKey.wasPressedThisFrame)
            {
                hud.MoveChoice(1);
            }
            if (keyboard.fKey.wasPressedThisFrame || keyboard.enterKey.wasPressedThisFrame ||
                keyboard.spaceKey.wasPressedThisFrame || keyboard.eKey.wasPressedThisFrame)
            {
                hud.ConfirmChoice();
            }
        }
#else
        if (Input.GetKeyDown(KeyCode.A) || Input.GetKeyDown(KeyCode.LeftArrow)) hud.MoveChoice(-1);
        if (Input.GetKeyDown(KeyCode.D) || Input.GetKeyDown(KeyCode.RightArrow)) hud.MoveChoice(1);
        if (Input.GetKeyDown(KeyCode.F) || Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.Space)) hud.ConfirmChoice();
#endif
        return hud.ChoiceMade;
    }

    /// <summary>Rematch: every kart back on its grid slot, race state cleared.</summary>
    private void ResetToGrid()
    {
        for (int i = 0; i < racers.Count; i++)
        {
            GymRaceRacer racer = racers[i];
            GymRaceKart kart = racer.Kart;
            Pose pose = GymRaceTrackLayout.GridSlot(racer.GridSlot);
            kart.Respawn(pose.position, pose.rotation * Vector3.forward, Vector3.up);
            kart.Input = new GymRaceDriveInput();
            kart.ControlsEnabled = false;
            kart.MeterFilling = false;
            kart.SpeedScale = 1f;
            kart.SetBoostMeter(0f);
            ApplyDifficulty(racer);
            if (racer.IsPlayer && !autopilot)
            {
                // The cool-down lap AI from the last finish hands back control.
                GymRaceAIDriver cooldown = kart.GetComponent<GymRaceAIDriver>();
                if (cooldown != null)
                {
                    Destroy(cooldown);
                }
            }
            racer.ResetProgress(Time.time);
        }
        ordered.Clear();
        playerFinishTime = -1f;
        LastPlayerPlace = 0;
        track.SetCountdownLights(0, false);
        chase.StartChase();
        Debug.Log("GYMCHAOS_RACE_REMATCH_GRID");
    }

    private static IEnumerator Wait(float seconds)
    {
        float until = Time.time + seconds;
        while (Time.time < until)
        {
            yield return null;
        }
    }

    // The race runs as a hand-stepped iterator stack so an exception anywhere
    // aborts cleanly (city, walls, camera and player restored) instead of
    // leaving the game locked in a half-built race.
    private void StartRun(IEnumerator race)
    {
        runStack.Clear();
        runStack.Push(race);
        // Active from this frame: no second start before the first step runs.
        phase = Phase.Building;
    }

    private void StepRun()
    {
        try
        {
            while (runStack.Count > 0)
            {
                IEnumerator top = runStack.Peek();
                if (!top.MoveNext())
                {
                    runStack.Pop();
                    continue;
                }
                if (top.Current is IEnumerator nested)
                {
                    runStack.Push(nested);
                    continue;
                }
                return;
            }
        }
        catch (System.Exception exception)
        {
            runStack.Clear();
            Debug.LogError("GYMCHAOS_RACE_ABORTED " + exception);
            AbortRace();
        }
    }

    private void AbortRace()
    {
        try
        {
            Teardown();
        }
        catch (System.Exception exception)
        {
            Debug.LogError("GYMCHAOS_RACE_TEARDOWN_FAILED " + exception);
            GymRaceCityCarver.Restore();
            GymRaceCorridorClearance.Restore();
            GymVisitorDirector.RaceTrafficHold = false;
            if (player != null)
            {
                player.transform.SetParent(null, true);
                player.SetCinematicLock(false);
            }
        }
        phase = Phase.Idle;
    }

    // -------------------------------------------------------------- spawning

    // ---------------------------------------------------------- difficulty

    public static readonly string[] DifficultyNames = { "EASY", "MEDIUM", "HARD" };
    // Easy is the original race. Every kart, the player's too, gets faster
    // per level (grip and steering follow so it stays drivable) and the AI
    // takes corners closer to the limit.
    private static readonly float[] DifficultySpeed = { 1f, 1.12f, 1.24f };
    private static readonly float[] DifficultySkill = { 1f, 1.07f, 1.14f };
    private int difficulty;
    public int CurrentDifficulty => difficulty;

    public static GymRaceKart.Stats ScaleStats(GymRaceKart.Stats stats, int level)
    {
        float speed = DifficultySpeed[Mathf.Clamp(level, 0, DifficultySpeed.Length - 1)];
        stats.TopSpeed *= speed;
        stats.Acceleration *= speed;
        stats.Grip *= speed;
        stats.Handling *= Mathf.Sqrt(speed);
        return stats;
    }

    public static float AiSkillScale(int level) =>
        DifficultySkill[Mathf.Clamp(level, 0, DifficultySkill.Length - 1)];

    /// <summary>A win opens the level after the one raced; nothing else changes it.</summary>
    public static int UnlockAfterRace(int unlocked, int raced, int place) =>
        place == 1 ? Mathf.Clamp(Mathf.Max(unlocked, raced + 1), 0, DifficultyNames.Length - 1) : unlocked;

    private static int UnlockedDifficulty
    {
        get
        {
            GymProgressionState state = GymExperienceService.Active != null ? GymExperienceService.Active.State : null;
            return state != null ? Mathf.Clamp(state.raceDifficultyUnlocked, 0, DifficultyNames.Length - 1) : 0;
        }
    }

    private int pickedDifficulty;

    // Difficulty screen: BACK plus one button per open level; -1 = back.
    private IEnumerator PickDifficulty(int unlocked, bool coast)
    {
        hud.ShowBanner("SELECT DIFFICULTY", PersonaMenuStyle.Gold, 600f);
        hud.ShowChoice(unlocked, true);
        player.SetCursorCaptured(false);
        GymRaceHud.Choice choice = GymRaceHud.Choice.None;
        float shownAt = Time.time;
        while (choice == GymRaceHud.Choice.None)
        {
            if (coast) CoastAll();
            choice = ReadChoice(Time.time - shownAt);
            yield return null;
        }
        hud.HideChoice();
        hud.ShowBanner(string.Empty, PersonaMenuStyle.Gold, 0f);
        pickedDifficulty = choice == GymRaceHud.Choice.RaceAgain ? hud.ChosenDifficulty : -1;
    }

    private void ApplyDifficulty(GymRaceRacer racer)
    {
        RacerSpec spec = specs[racer.GridSlot];
        racer.Kart.KartStats = ScaleStats(spec.Stats, difficulty);
        GymRaceAIDriver ai = racer.Kart.GetComponent<GymRaceAIDriver>();
        if (ai != null && !spec.Player)
        {
            ai.Skill = spec.Skill * AiSkillScale(difficulty);
        }
    }

    private static List<RacerSpec> Roster()
    {
        GymRaceKart.Stats baseStats = GymRaceKart.Stats.Default;
        baseStats.Radius = 1.15f;
        GymRaceKart.Stats zyzz = baseStats;
        zyzz.Acceleration *= 1.08f;
        zyzz.TopSpeed *= 0.98f;
        zyzz.Handling *= 1.04f;
        zyzz.Mass = 820f;
        GymRaceKart.Stats police = baseStats;
        police.TopSpeed *= 1.01f;
        police.Mass = 1150f;
        GymRaceKart.Stats jay = baseStats;
        jay.Grip *= 1.1f;
        jay.Mass = 1000f;
        GymRaceKart.Stats arnold = baseStats;
        arnold.Radius = 1.3f;
        arnold.Mass = 1600f;
        arnold.Acceleration *= 0.94f;
        arnold.TopSpeed *= 1.03f;
        arnold.Handling *= 0.95f;
        GymRaceKart.Stats buggy = GymRaceKart.Stats.Default;
        buggy.Radius = 0.95f;
        buggy.Mass = 760f;
        return new List<RacerSpec>
        {
            new RacerSpec { Name = "Zyzz", Identity = BodybuilderIdentity.Zyzz, Color = new Color(0.3f, 0.85f, 1f), Stats = zyzz, Lane = -1.4f, Skill = 1.02f, Aggression = 0.1f },
            new RacerSpec { Name = "Cbum", Identity = BodybuilderIdentity.Cbum, Color = new Color(1f, 0.75f, 0.2f), Stats = baseStats, Lane = 1.2f, Skill = 1f, Aggression = 0.2f },
            new RacerSpec { Name = "Officer", Identity = BodybuilderIdentity.Policeman, Police = true, Color = new Color(0.35f, 0.5f, 1f), Stats = police, Lane = 0f, Skill = 0.98f, Aggression = 0.6f },
            new RacerSpec { Name = "Jay Cutler", Identity = BodybuilderIdentity.JayCutler, Color = new Color(0.45f, 1f, 0.45f), Stats = jay, Lane = -0.8f, Skill = 0.99f, Aggression = 0.15f },
            new RacerSpec { Name = "Arnold", Identity = BodybuilderIdentity.Arnold, Color = new Color(1f, 0.35f, 0.3f), Stats = arnold, Lane = 0.9f, Skill = 0.97f, Aggression = 0.35f },
            new RacerSpec { Name = "You", Player = true, Color = Color.white, Stats = buggy, Lane = 0f, Skill = 1f }
        };
    }

    private List<RacerSpec> specs;

    private void SpawnField()
    {
        specs = Roster();
        cars.Clear();
        drivers.Clear();
        for (int slot = 0; slot < specs.Count; slot++)
        {
            RacerSpec spec = specs[slot];
            Pose pose = GymRaceTrackLayout.GridSlot(slot);
            GymRaceVehicleFactory.Car car = spec.Player
                ? GymRaceVehicleFactory.CreatePlayerCar(pose)
                : spec.Police
                    ? GymRaceVehicleFactory.CreatePoliceCar(pose)
                    : GymRaceVehicleFactory.CreateVisitorCar(spec.Identity, pose);
            car.Holder.SetParent(transform, true);
            cars.Add(car);
            if (spec.Player)
            {
                playerCar = car;
                drivers.Add(null);
                continue;
            }
            EnemyFighter driver = GymRaceVehicleFactory.CreateDriver(spec.Identity, player, pose.position + Vector3.left * 3f);
            driver.transform.SetParent(transform, true);
            drivers.Add(driver);
        }
    }

    private bool AllCarsReady()
    {
        for (int i = 0; i < cars.Count; i++)
        {
            if (!cars[i].Ready)
            {
                return false;
            }
        }
        return true;
    }

    private void PlayCrumbles()
    {
        IReadOnlyList<Vector3> centers = GymRaceCityCarver.RemovedBuildingCenters;
        int step = Mathf.Max(1, centers.Count / 6);
        for (int i = 0; i < centers.Count; i += step)
        {
            GymAudio.Play(GymSoundEffect.RaceCrumble, centers[i], 0.9f);
        }
        GymAudio.Play2D(GymSoundEffect.RaceCrumble, 0.5f);
    }

    private void CreateKarts()
    {
        racers.Clear();
        for (int slot = 0; slot < cars.Count; slot++)
        {
            RacerSpec spec = specs[slot];
            GymRaceVehicleFactory.Car car = cars[slot];
            Pose pose = GymRaceTrackLayout.GridSlot(slot);
            Bounds local = LocalBounds(car.Holder);
            GymRaceKart kart = GymRaceKart.Create("Race Kart - " + spec.Name, pose.position, pose.rotation * Vector3.forward, ScaleStats(spec.Stats, difficulty), car.Holder);
            kart.transform.SetParent(transform, true);
            kart.ControlsEnabled = false;
            kart.MeterFilling = false;
            GymRaceRacer racer = kart.gameObject.AddComponent<GymRaceRacer>();
            racer.DisplayName = spec.Name;
            racer.Color = spec.Color;
            racer.IsPlayer = spec.Player;
            racer.GridSlot = slot;
            if (!spec.Player || autopilot)
            {
                GymRaceAIDriver ai = kart.gameObject.AddComponent<GymRaceAIDriver>();
                ai.LaneOffset = spec.Lane;
                ai.Skill = spec.Skill * AiSkillScale(difficulty);
                ai.Aggression = spec.Aggression;
            }
            GymRaceKartEffects effects = kart.gameObject.AddComponent<GymRaceKartEffects>();
            effects.IsPlayer = spec.Player;
            effects.Camera = spec.Player ? chase : null;
            effects.Setup(kart, car.ResolveWheels(), local);
            racers.Add(racer);
            if (spec.Player)
            {
                playerRacer = racer;
            }

            EnemyFighter driver = drivers[slot];
            if (driver != null)
            {
                bool seated = car.Vehicle != null
                    ? car.Vehicle.TrySeatArrivingDriver(driver)
                    : car.ResolveSeat() != null && car.Seat.Mount(driver);
                if (!seated)
                {
                    // No cabin for this rig: keep the car driverless rather than show a T-pose.
                    driver.gameObject.SetActive(false);
                    Debug.LogWarning($"GYMCHAOS_RACE_DRIVER_UNSEATED racer={spec.Name}");
                }
            }
        }
        IgnoreOwnColliders();
    }

    private void IgnoreOwnColliders()
    {
        // Every kart sphere ignores every car body and driver; spheres still hit each other.
        for (int i = 0; i < racers.Count; i++)
        {
            for (int j = 0; j < cars.Count; j++)
            {
                racers[i].Kart.IgnoreColliders(cars[j].Holder);
                if (drivers[j] != null)
                {
                    racers[i].Kart.IgnoreColliders(drivers[j].transform);
                }
            }
            if (player != null)
            {
                racers[i].Kart.IgnoreColliders(player.transform);
            }
        }
    }

    private static Bounds LocalBounds(Transform holder)
    {
        Renderer[] renderers = holder.GetComponentsInChildren<Renderer>(true);
        Bounds bounds = new Bounds(Vector3.zero, new Vector3(1.8f, 1.2f, 3.6f));
        bool first = true;
        foreach (Renderer renderer in renderers)
        {
            if (!renderer.enabled || renderer is ParticleSystemRenderer)
            {
                continue;
            }
            Bounds world = renderer.bounds;
            Vector3 min = holder.InverseTransformPoint(world.min);
            Vector3 max = holder.InverseTransformPoint(world.max);
            Bounds part = new Bounds((min + max) * 0.5f, Vector3.zero);
            part.Encapsulate(min);
            part.Encapsulate(max);
            if (first)
            {
                bounds = part;
                first = false;
            }
            else
            {
                bounds.Encapsulate(part);
            }
        }
        return bounds;
    }

    // -------------------------------------------------------------- boarding

    private void SeatPlayerNow()
    {
        playerSeated = false;
        GymVehicleDriverSeat seat = playerCar.ResolveSeat();
        playerOccupant = player.GetComponent<PlayerVehicleOccupant>();
        if (playerOccupant == null)
        {
            playerOccupant = PlayerVehicleOccupant.Attach(player);
        }
        if (seat != null && playerOccupant != null && GymVehicleDriverSeat.SupportsDriver(playerOccupant) &&
            seat.Mount(playerOccupant))
        {
            playerSeated = true;
        }
        if (!playerSeated)
        {
            // No seat or clips: ride along hidden so no body pokes out of the car.
            Debug.LogWarning("GYMCHAOS_RACE_PLAYER_SEAT_FALLBACK");
            player.transform.SetParent(playerCar.Holder, true);
            player.transform.localPosition = Vector3.up * 0.4f;
            player.transform.localRotation = Quaternion.identity;
            if (player.playerCamera != null)
            {
                player.playerCamera.cullingMask &= ~(1 << PlanarGymMirror.MirrorPlayerLayer);
            }
        }
        Debug.Log($"GYMCHAOS_RACE_PLAYER_BOARDED seated={(playerSeated ? 1 : 0)} instant=1");
    }

    private void CaptureHeadshots()
    {
        int captured = 0;
        for (int i = 0; i < racers.Count; i++)
        {
            GymRaceRacer racer = racers[i];
            Transform character = racer.IsPlayer ? player.transform : drivers[racer.GridSlot] != null ? drivers[racer.GridSlot].transform : null;
            if (character == null || !character.gameObject.activeInHierarchy)
            {
                continue;
            }
            // Drivers face the direction of travel (some car rigs are authored facing -Z).
            Vector3 facing = racer.Kart.Heading;
            try
            {
                PlayerHandRig rig = racer.IsPlayer && playerOccupant != null ? playerOccupant.HandRig : null;
                // The player's root also holds first-person arms: shoot the full body only.
                racer.Headshot = rig != null && rig.RuntimeBodyRenderers.Count > 0
                    // The player head is a plain ball: find the side with the smiley face.
                    ? GymRaceHeadshot.Capture(new List<Renderer>(rig.RuntimeBodyRenderers), player.transform.forward, null, GymRaceHeadshot.FaceSearch.DarkFeatures)
                    : GymRaceHeadshot.Capture(character, facing);
            }
            catch (System.Exception exception)
            {
                Debug.LogWarning("GYMCHAOS_RACE_HEADSHOT_FAILED racer=" + racer.DisplayName + " " + exception.Message);
            }
            if (racer.Headshot != null)
            {
                captured++;
            }
        }
        Debug.Log($"GYMCHAOS_RACE_HEADSHOTS captured={captured}/{racers.Count}");
    }

    // ---------------------------------------------------------------- racing

    private void ReadPlayerDuringCountdown()
    {
        if (autopilot)
        {
            if (firstThrottleAt < 0f && Time.time - countdownStart > 2.1f)
            {
                firstThrottleAt = Time.time - countdownStart;
            }
            return;
        }
        GymRaceDriveInput input = GymRaceInput.Read();
        if (input.Throttle > 0.5f && firstThrottleAt < 0f)
        {
            firstThrottleAt = Time.time - countdownStart;
        }
        if (input.Throttle < 0.1f && firstThrottleAt >= 0f && Time.time - countdownStart < 2.95f)
        {
            // Let go before GO: the press does not count.
            firstThrottleAt = -1f;
        }
        playerRacer.Kart.Input = input;
    }

    private void StartRacing()
    {
        for (int i = 0; i < racers.Count; i++)
        {
            GymRaceKart kart = racers[i].Kart;
            kart.ControlsEnabled = true;
            kart.MeterFilling = true;
            bool rocket = racers[i].IsPlayer
                ? firstThrottleAt >= 1.8f && firstThrottleAt <= 2.75f
                : Random.value < 0.35f;
            if (rocket)
            {
                kart.StartMiniTurbo(1.1f);
                if (racers[i].IsPlayer)
                {
                    hud.ShowBanner("ROCKET START!", PersonaMenuStyle.Gold, 1.4f);
                    GymAudio.Play2D(GymSoundEffect.RaceMiniTurbo);
                }
            }
        }
        lastPlayerCrossings = 0;
        playerFinishTime = -1f;
    }

    private void TickRace()
    {
        float now = Time.time;
        float raceTime = now - raceStartTime;
        for (int i = 0; i < racers.Count; i++)
        {
            GymRaceRacer racer = racers[i];
            GymRaceKart kart = racer.Kart;
            GymRaceAIDriver ai = racer.GetComponent<GymRaceAIDriver>();
            if (racer.IsPlayer && !autopilot && !racer.Finished)
            {
                GymRaceDriveInput input = GymRaceInput.Read();
                kart.Input = input;
                chase.LookBack = input.LookBack;
                if (input.RespawnPressed)
                {
                    racer.RespawnAtSafePoint();
                }
            }
            else if (ai != null)
            {
                kart.Input = ai.Think(now);
            }
            else
            {
                // Player kart after the finish: the AI drives the cool-down lap.
                ai = kart.gameObject.AddComponent<GymRaceAIDriver>();
                kart.Input = ai.Think(now);
            }

            bool finished = racer.Tick(raceTime, Time.deltaTime, true);
            if (racer.NeedsRespawn)
            {
                racer.RespawnAtSafePoint();
            }
            if (finished)
            {
                racer.FinishPlace = NextFinishPlace();
                if (racer.IsPlayer)
                {
                    playerFinishTime = now;
                    phase = Phase.Finishing;
                    kart.MeterFilling = false;
                    GymAudio.Play2D(GymSoundEffect.RaceFinish, 0.9f);
                    hud.ShowBanner(racer.FinishPlace == 1 ? "YOU WIN!" : "FINISH! " + GymRaceHud.Ordinal(racer.FinishPlace),
                        racer.FinishPlace == 1 ? PersonaMenuStyle.Gold : PersonaMenuStyle.Ink, 3f);
                    chase.StartOrbit();
                }
            }
        }

        UpdateRubberBand();
        SortPlaces();
        PlayerLapCues();
        UpdateHud();

        bool allDone = true;
        for (int i = 0; i < racers.Count; i++)
        {
            allDone &= racers[i].Finished;
        }
        if (phase == Phase.Finishing && (allDone || now - playerFinishTime > FinishGraceSeconds))
        {
            phase = Phase.Results;
        }
        if (raceTime > RaceTimeLimit)
        {
            phase = Phase.Results;
        }
    }

    private int NextFinishPlace()
    {
        int place = 1;
        for (int i = 0; i < racers.Count; i++)
        {
            if (racers[i].Finished && racers[i].FinishPlace > 0)
            {
                place++;
            }
        }
        return place;
    }

    private void UpdateRubberBand()
    {
        if (playerRacer == null)
        {
            return;
        }
        float playerProgress = playerRacer.TotalProgress;
        for (int i = 0; i < racers.Count; i++)
        {
            GymRaceRacer racer = racers[i];
            if (racer.IsPlayer)
            {
                racer.Kart.SpeedScale = 1f;
                continue;
            }
            float gap = racer.TotalProgress - playerProgress;
            // Behind the player: a little faster; far ahead: a little slower.
            float scale = 1f - Mathf.Clamp(gap / RubberBandRange, -1f, 1f) * 0.07f;
            racer.Kart.SpeedScale = Mathf.MoveTowards(racer.Kart.SpeedScale, scale, Time.deltaTime * 0.2f);
        }
    }

    private void SortPlaces()
    {
        ordered.Clear();
        ordered.AddRange(racers);
        ordered.Sort((a, b) =>
        {
            if (a.Finished && b.Finished) return a.FinishPlace.CompareTo(b.FinishPlace);
            if (a.Finished) return -1;
            if (b.Finished) return 1;
            return b.TotalProgress.CompareTo(a.TotalProgress);
        });
        for (int i = 0; i < ordered.Count; i++)
        {
            ordered[i].Place = i + 1;
        }
    }

    private void PlayerLapCues()
    {
        if (playerRacer == null || playerRacer.Crossings == lastPlayerCrossings)
        {
            return;
        }
        int crossings = playerRacer.Crossings;
        lastPlayerCrossings = crossings;
        if (crossings <= 1 || playerRacer.Finished)
        {
            return;
        }
        if (crossings == GymRaceTrackLayout.Laps)
        {
            hud.ShowBanner("FINAL LAP", PersonaMenuStyle.Accent, 2.5f);
            GymAudio.Play2D(GymSoundEffect.RaceFinalLap, 0.8f);
        }
        else
        {
            hud.ShowBanner("LAP " + crossings, PersonaMenuStyle.Gold, 1.8f);
            GymAudio.Play2D(GymSoundEffect.RaceLap, 0.8f);
        }
    }

    private void UpdateHud()
    {
        if (hud == null || playerRacer == null)
        {
            return;
        }
        float raceTime = phase == Phase.Countdown ? 0f : Time.time - raceStartTime;
        if (ordered.Count == 0)
        {
            SortPlaces();
        }
        hud.UpdatePlayer(playerRacer, raceTime, playerRacer.WrongWay && phase == Phase.Racing);
        hud.UpdateStandings(ordered);
        hud.UpdateMinimap(racers);
    }

    private void CoastAll()
    {
        for (int i = 0; i < racers.Count; i++)
        {
            GymRaceKart kart = racers[i].Kart;
            GymRaceAIDriver ai = racers[i].GetComponent<GymRaceAIDriver>();
            GymRaceDriveInput input = ai != null ? ai.Think(Time.time) : new GymRaceDriveInput();
            input.Throttle = Mathf.Min(input.Throttle, 0.35f);
            input.BoostPressed = false;
            input.DriftHeld = false;
            kart.Input = input;
            racers[i].Tick(Time.time - raceStartTime, Time.deltaTime, false);
            if (racers[i].NeedsRespawn)
            {
                racers[i].RespawnAtSafePoint();
            }
        }
        UpdateHud();
    }

    private void StopAll()
    {
        for (int i = 0; i < racers.Count; i++)
        {
            GymRaceKart kart = racers[i].Kart;
            kart.Input = new GymRaceDriveInput { Brake = 1f };
            kart.ControlsEnabled = false;
            kart.MeterFilling = false;
            if (kart.Body != null)
            {
                kart.Body.linearVelocity = Vector3.zero;
            }
        }
    }

    // --------------------------------------------------------------- results

    private List<GymRaceHud.ResultRow> BuildResults()
    {
        // Unfinished racers take the remaining places by progress.
        SortPlaces();
        int next = NextFinishPlace();
        for (int i = 0; i < ordered.Count; i++)
        {
            if (!ordered[i].Finished && ordered[i].FinishPlace == 0)
            {
                ordered[i].FinishPlace = next++;
            }
        }
        ordered.Sort((a, b) => a.FinishPlace.CompareTo(b.FinishPlace));
        List<GymRaceHud.ResultRow> rows = new List<GymRaceHud.ResultRow>();
        for (int i = 0; i < ordered.Count; i++)
        {
            GymRaceRacer racer = ordered[i];
            float best = 0f;
            for (int k = 0; k < racer.LapTimes.Count; k++)
            {
                best = best <= 0f ? racer.LapTimes[k] : Mathf.Min(best, racer.LapTimes[k]);
            }
            rows.Add(new GymRaceHud.ResultRow
            {
                Place = racer.FinishPlace,
                Name = racer.DisplayName,
                Color = racer.Color,
                IsPlayer = racer.IsPlayer,
                Time = racer.FinishTime,
                BestLap = best,
                Finished = racer.Finished
            });
        }
        LastPlayerPlace = playerRacer != null ? playerRacer.FinishPlace : 0;
        return rows;
    }

    private int ComputeExperience(out string line)
    {
        int place = Mathf.Clamp(LastPlayerPlace, 1, PlaceExperience.Length);
        int xp = PlaceExperience[place - 1];
        System.Text.StringBuilder text = new System.Text.StringBuilder();
        text.Append($"+{xp} XP  {GymRaceHud.Ordinal(place)} PLACE");
        GymProgressionState state = GymExperienceService.Active != null ? GymExperienceService.Active.State : null;
        if (place == 1 && state != null && state.raceWins == 0)
        {
            xp += FirstWinBonus;
            text.Append($"   +{FirstWinBonus} FIRST WIN");
        }
        float bestLap = BestPlayerLap();
        if (bestLap > 0f && state != null && (state.raceBestLapMs == 0 || bestLap * 1000f < state.raceBestLapMs))
        {
            xp += BestLapBonus;
            text.Append($"   +{BestLapBonus} BEST LAP {GymRaceHud.FormatTime(bestLap)}");
        }
        line = text.ToString();
        return xp;
    }

    private float BestPlayerLap()
    {
        if (playerRacer == null)
        {
            return resultBestLap;
        }
        float best = 0f;
        if (playerRacer == null)
        {
            return best;
        }
        for (int k = 0; k < playerRacer.LapTimes.Count; k++)
        {
            best = best <= 0f ? playerRacer.LapTimes[k] : Mathf.Min(best, playerRacer.LapTimes[k]);
        }
        return best;
    }

    private void AwardResults(int xp)
    {
        LastAwardedExperience = xp;
        GymExperienceService progression = GymExperienceService.Active;
        int place = LastPlayerPlace;
        if (progression != null && progression.State != null)
        {
            GymProgressionState state = progression.State;
            state.raceStarts++;
            float bestLap = BestPlayerLap();
            if (bestLap > 0f && (state.raceBestLapMs == 0 || bestLap * 1000f < state.raceBestLapMs))
            {
                state.raceBestLapMs = Mathf.RoundToInt(bestLap * 1000f);
            }
            if (resultFinished)
            {
                int total = Mathf.RoundToInt(resultFinishTime * 1000f);
                if (state.raceBestTotalMs == 0 || total < state.raceBestTotalMs)
                {
                    state.raceBestTotalMs = total;
                }
            }
            state.raceDifficultyUnlocked = UnlockAfterRace(state.raceDifficultyUnlocked, difficulty, place);
            if (place == 1)
            {
                state.raceWins++;
                progression.ChangeReputation(WinReputation, "won the city race", false);
            }
            // One key per race so the award is never swallowed by the cooldown.
            progression.AwardExperience(xp, "city race " + GymRaceHud.Ordinal(Mathf.Max(place, 1)).ToLowerInvariant(),
                "race-result-" + state.raceStarts);
            GymSessionService.RequestSave("race");
        }
        LastRaceSummary = $"place={place} xp={xp}";
        GymHud.Active?.PushNotice($"City race: {GymRaceHud.Ordinal(Mathf.Max(place, 1))} place  +{xp} XP", 4.5f, 2);
        Debug.Log($"GYMCHAOS_RACE_RESULT place={place} xp={xp} time={resultFinishTime:F2} bestLap={resultBestLap:F2}");
    }

    // -------------------------------------------------------------- teardown

    private void Teardown()
    {
        if (chase != null)
        {
            chase.End();
        }
        // Out of the seat (or fallback ride): put the player back at the sign.
        if (player.transform.parent != null && playerCar != null && player.transform.IsChildOf(playerCar.Holder))
        {
            player.transform.SetParent(null, true);
        }
        if (playerOccupant != null && playerOccupant.IsInVehicleSeatMode)
        {
            playerOccupant.EndVehicleSeat(player.transform.position, player.transform.rotation);
        }
        Vector3 stand = GymRaceSign.Instance != null ? GymRaceSign.Instance.StandPoint : player.transform.position;
        Vector3 face = GymRaceSign.Instance != null
            ? Vector3.ProjectOnPlane(GymRaceSign.Instance.transform.position - stand, Vector3.up)
            : player.transform.forward;
        Quaternion rotation = face.sqrMagnitude > 0.01f ? Quaternion.LookRotation(-face.normalized) : player.transform.rotation;
        player.RestoreCinematicPose(stand + Vector3.up * 0.1f, rotation, savedCameraLocalPosition, savedCameraLocalRotation);
        player.SetCinematicLock(false);

        for (int i = 0; i < drivers.Count; i++)
        {
            if (drivers[i] != null) Destroy(drivers[i].gameObject);
        }
        for (int i = 0; i < racers.Count; i++)
        {
            if (racers[i] != null) Destroy(racers[i].gameObject);
        }
        for (int i = 0; i < cars.Count; i++)
        {
            if (cars[i].Holder != null) Destroy(cars[i].Holder.gameObject);
        }
        drivers.Clear();
        racers.Clear();
        ordered.Clear();
        cars.Clear();
        playerRacer = null;
        playerCar = null;
        if (track != null) Destroy(track.gameObject);
        track = null;
        GymRaceCityCarver.Restore();
        GymRaceCorridorClearance.Restore();
        if (hud != null) Destroy(hud.gameObject);
        hud = null;
        GymVisitorDirector.RaceTrafficHold = false;
        Debug.Log("GYMCHAOS_RACE_TEARDOWN_OK");
    }
}
