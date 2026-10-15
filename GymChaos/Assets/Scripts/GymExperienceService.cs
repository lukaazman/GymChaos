using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Owns the game's non-monetary progression loop: EXP, levels, four stat trees,
/// signed reputation, daily goals and local persistence.
/// </summary>
[DefaultExecutionOrder(-100)]
public sealed class GymExperienceService : MonoBehaviour
{
    public const int MaxStatRank = 10;
    public const int MaxLoyaltyStamps = 20;
    public const int DailyLoyaltyStampCap = 12;
    public const int RepsPerLoyaltyStamp = 10;
    private const string SaveKey = "GymChaos.Progression.v1";
    // Bump this migration whenever the requested launch baseline changes.
    // v7 resets an existing local save once, while later reputation changes
    // still persist normally instead of being wiped on every launch.
    private const string InitialReputationResetKey = "GymChaos.InitialReputationReset.v7";
    private const float SkillChoiceTimeScale = 0.08f;

    private static GymExperienceService instance;
    private static readonly int[] DailyGoalTargets = { 10, 2, 1 };
    private static readonly string[] DailyGoalLabels =
    {
        "Finish 10 workout reps",
        "Talk to 2 people",
        "Cause one piece of chaos"
    };
    private static readonly GymShirtColor[] LockerShirtOptions =
    {
        GymShirtColor.Black, GymShirtColor.White, GymShirtColor.Red,
        GymShirtColor.Blue, GymShirtColor.Gold
    };
    private static readonly GymHeadwear[] LockerHeadwearOptions =
    {
        GymHeadwear.None, GymHeadwear.Cap, GymHeadwear.Beanie,
        GymHeadwear.Headband, GymHeadwear.BucketHat, GymHeadwear.JollyCap
    };

    private readonly HashSet<string> oneShotRewards = new HashSet<string>();
    private readonly Dictionary<string, float> recentRewards = new Dictionary<string, float>();
    private GymProgressionState state;
    private PlayerMovement player;
    private PlayerCosmeticLoadout cosmetics;
    private float nextSaveTime;
    private float feedbackUntil;
    private string feedbackText;
    private float statImpactUntil;
    private string statImpactText;
    private bool levelChoiceVisible;
    private bool levelChoiceWasCursorCaptured;
    private bool levelChoiceCursorReleased;
    private bool lockerMenuOpen;
    private bool lockerMenuWasCursorCaptured;
    private PlayerMovement lockerMenuPlayer;
    private bool lockerMenuPoseCaptured;
    private Vector3 lockerMenuPreviousPosition;
    private Quaternion lockerMenuPreviousRotation;
    private Vector3 lockerMenuPreviousCameraPosition;
    private Quaternion lockerMenuPreviousCameraRotation;
    private bool timeScaleChanged;
    private bool initialized;
    private bool sessionBacked;
    private float temporaryFatigue;

    /// <summary>Raised when level, experience, stats, reputation or goals change.</summary>
    public event Action ProgressionChanged;
    /// <summary>Short gameplay notices: text, seconds, priority (0 info, 1 reward, 2 critical).</summary>
    public event Action<string, float, int> NoticeRaised;

    private GUIStyle progressHudShadowStyle;
    private GUIStyle progressHudTitleStyle;
    private GUIStyle progressHudBodyStyle;
    private GUIStyle progressHudGoalStyle;
    private GUIStyle progressHudFeedbackStyle;
    private GUIStyle progressHudImpactStyle;


    public static GymExperienceService Active => instance;
    public GymProgressionState State => state;
    public PlayerMovement Player => player;
    public bool IsSkillChoiceVisible => levelChoiceVisible;
    public bool IsLockerMenuOpen => lockerMenuOpen;
    public bool IsBlockingPlayerInput => lockerMenuOpen || GymProteinShopUI.IsOpen ||
        (levelChoiceVisible && !EnemyFighter.IsFightActive && !GymDialogueDirector.IsDialogueActive);
    public int Level => state != null ? state.level : 1;
    public int Experience => state != null ? state.experience : 0;
    public int ExperienceToNextLevel => GetExperienceToNextLevel(Level);
    public int SkillPoints => state != null ? state.skillPoints : 0;
    public int Reputation => state != null ? state.reputation : 0;
    public int MasteryRank => state != null ? state.masteryRank : 0;
    public int LoyaltyStamps => state != null ? state.loyaltyStamps : 0;
    public int LoyaltyStampsEarnedToday =>
        state != null && state.loyaltyStampDay == state.gymDay ? state.loyaltyStampsToday : 0;
    public float TemporaryFatigue => temporaryFatigue;
    public bool AllStatsMaxed =>
        GetStatRank(GymStat.Strength) >= MaxStatRank &&
        GetStatRank(GymStat.Endurance) >= MaxStatRank &&
        GetStatRank(GymStat.Technique) >= MaxStatRank &&
        GetStatRank(GymStat.Reputation) >= MaxStatRank;
    public string MasteryTitle => GetMasteryTitle(MasteryRank);
    public string MasteryChallengeLabel => GetMasteryChallengeLabel(MasteryRank);

    public static GymExperienceService CreateForScene(PlayerMovement targetPlayer)
    {
        if (instance != null)
        {
            instance.player = targetPlayer != null ? targetPlayer : instance.player;
            instance.EnsureCosmetics();
            return instance;
        }

        GameObject serviceObject = new GameObject("Gym Progression Service");
        instance = serviceObject.AddComponent<GymExperienceService>();
        instance.Initialize(targetPlayer);
        return instance;
    }

    private void Initialize(PlayerMovement targetPlayer)
    {
        if (initialized)
        {
            return;
        }

        initialized = true;
        player = targetPlayer;
        sessionBacked = GymSessionService.SessionMode;
        if (sessionBacked)
        {
            // Character slots own progression. Stay neutral until the menu
            // applies a confirmed new or loaded character.
            state = new GymProgressionState();
            state.Normalize();
            EnsureCosmetics();
            Debug.Log("GYMCHAOS_PROGRESSION_READY source=session-pending", this);
            return;
        }

        bool hasPreviousSave = PlayerPrefs.HasKey(SaveKey);
        state = LoadState();
        if (PlayerPrefs.GetInt(InitialReputationResetKey, 0) == 0)
        {
            state.reputation = 0;
            PlayerPrefs.SetInt(InitialReputationResetKey, 1);
            Debug.Log("GYMCHAOS_REPUTATION_BASELINE reset=0", this);
        }
        state.gymDay = hasPreviousSave ? Mathf.Max(1, state.gymDay + 1) : 1;
        state.lockerPrepCompleted = false;
        state.dailyProgress = new[] { 0, 0, 0 };
        state.dailyCompleted = new[] { false, false, false };
        SaveNow();
        EnsureCosmetics();

        Debug.Log(
            $"GYMCHAOS_PROGRESSION_READY level={state.level} " +
            $"xp={state.experience}/{GetExperienceToNextLevel(state.level)} " +
            $"day={state.gymDay} reputation={state.reputation}",
            this);
    }

    /// <summary>
    /// Replaces progression with a character save. Loading never awards
    /// experience or replays completion events; a loaded character starts a
    /// new gym day exactly like the previous launch-per-day behaviour.
    /// </summary>
    public void ApplySession(GymProgressionState saved, string[] savedOneShotRewards, bool startNewDay)
    {
        // Supplements belong to the session that bought them.
        GymBuffs.Clear();
        state = saved != null
            ? JsonUtility.FromJson<GymProgressionState>(JsonUtility.ToJson(saved))
            : new GymProgressionState();
        state.Normalize();
        if (startNewDay)
        {
            state.gymDay = Mathf.Max(1, state.gymDay + 1);
            state.lockerPrepCompleted = false;
            state.dailyProgress = new[] { 0, 0, 0 };
            state.dailyCompleted = new[] { false, false, false };
        }

        oneShotRewards.Clear();
        if (savedOneShotRewards != null)
        {
            for (int i = 0; i < savedOneShotRewards.Length; i++)
            {
                if (!string.IsNullOrEmpty(savedOneShotRewards[i]))
                {
                    oneShotRewards.Add(savedOneShotRewards[i]);
                }
            }
        }
        recentRewards.Clear();
        feedbackText = null;
        statImpactText = null;
        temporaryFatigue = 0f;
        levelChoiceVisible = state.skillPoints > 0;
        levelChoiceWasCursorCaptured = true;
        levelChoiceCursorReleased = false;
        sessionBacked = true;
        EnsureCosmetics();
        ProgressionChanged?.Invoke();
        Debug.Log(
            $"GYMCHAOS_PROGRESSION_READY source=session level={state.level} " +
            $"xp={state.experience}/{GetExperienceToNextLevel(state.level)} " +
            $"day={state.gymDay} reputation={state.reputation}",
            this);
    }

#if UNITY_EDITOR
    public int NoticeListenerCountForVerification =>
        NoticeRaised != null ? NoticeRaised.GetInvocationList().Length : 0;
#endif

    public string[] CaptureOneShotRewards()
    {
        string[] captured = new string[oneShotRewards.Count];
        oneShotRewards.CopyTo(captured);
        Array.Sort(captured, StringComparer.Ordinal);
        return captured;
    }

    private GymProgressionState LoadState()
    {
        string json = PlayerPrefs.GetString(SaveKey, string.Empty);
        if (string.IsNullOrWhiteSpace(json))
        {
            GymProgressionState fresh = new GymProgressionState();
            fresh.Normalize();
            return fresh;
        }

        try
        {
            GymProgressionState loaded = JsonUtility.FromJson<GymProgressionState>(json);
            if (loaded == null)
            {
                loaded = new GymProgressionState();
            }
            loaded.Normalize();
            return loaded;
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"Gym progression save could not be read: {exception.Message}", this);
            GymProgressionState fresh = new GymProgressionState();
            fresh.Normalize();
            return fresh;
        }
    }

    private void Update()
    {
        if (state == null)
        {
            return;
        }

        if (levelChoiceVisible && !timeScaleChanged &&
            !EnemyFighter.IsFightActive && !GymDialogueDirector.IsDialogueActive)
        {
            Time.timeScale = SkillChoiceTimeScale;
            timeScaleChanged = true;
        }

        if (levelChoiceVisible)
        {
            // The uGUI choice (menu style) shows itself while a point is open.
            GymLevelUpMenuUI.Ensure(this);
        }

        if (levelChoiceVisible && !EnemyFighter.IsFightActive &&
            !GymDialogueDirector.IsDialogueActive && !levelChoiceCursorReleased &&
            player != null)
        {
            // PlayerMovement intentionally stops consuming gameplay input while
            // this overlay is open. Release the gameplay pointer here as well,
            // otherwise the locked cursor never reaches the menu buttons.
            player.SetCursorCaptured(false);
            levelChoiceCursorReleased = true;
        }

        if (Time.unscaledTime >= feedbackUntil)
        {
            feedbackText = null;
        }
        if (Time.unscaledTime >= statImpactUntil)
        {
            statImpactText = null;
        }

        temporaryFatigue = Mathf.MoveTowards(
            temporaryFatigue, 0f, Time.unscaledDeltaTime * 0.012f);

        // The back room is a real progression discovery, not a frame-based
        // reward. The once-key makes walking in and out safe and keeps this
        // extensible for future spaces/interactables.
        if (player != null && GymBackRoomBuilder.IsInsideRoom(player.transform.position))
        {
            RegisterSpaceDiscovery("back-room", "New space discovered  +10 XP");
        }

        if (Time.unscaledTime >= nextSaveTime)
        {
            SaveNow();
        }
    }

    public int GetStatRank(GymStat stat)
    {
        return state != null ? state.GetStatRank(stat) : 0;
    }

    public bool HasStrengthUltimate => GetStatRank(GymStat.Strength) >= MaxStatRank;
    public bool HasEnduranceUltimate => GetStatRank(GymStat.Endurance) >= MaxStatRank;
    public bool HasTechniqueUltimate => GetStatRank(GymStat.Technique) >= MaxStatRank;
    public bool HasPositiveReputationUltimate =>
        GetStatRank(GymStat.Reputation) >= MaxStatRank && Reputation >= 90;
    public bool HasNegativeReputationUltimate =>
        GetStatRank(GymStat.Reputation) >= MaxStatRank && Reputation <= -90;

    public float ScaleStrengthDamage(float baseDamage)
    {
        return baseDamage * (1f + GetStatRank(GymStat.Strength) * 0.08f) *
            GymBuffs.GetMultiplier(GymBuffStat.StrengthDamage);
    }

    public float GetStrengthForceMultiplier()
    {
        return (1f + GetStatRank(GymStat.Strength) * 0.042f) *
            GymBuffs.GetMultiplier(GymBuffStat.StrengthForce);
    }

    // Class modifiers are derived here on every read from the active class
    // definition; nothing multiplied is ever stored, so reloads cannot stack.
    public float GetSprintMultiplier()
    {
        float progression = HasEnduranceUltimate
            ? 1.72f
            : 1f + GetStatRank(GymStat.Endurance) * 0.055f;
        return progression * GymSessionService.ActiveClass.sprintSpeed *
            GymBuffs.GetMultiplier(GymBuffStat.SprintSpeed);
    }

    public float GetSprintCapacity()
    {
        return (100f + GetStatRank(GymStat.Endurance) * 8f) *
            GymSessionService.ActiveClass.staminaCapacity *
            GymBuffs.GetMultiplier(GymBuffStat.SprintCapacity);
    }

    public float GetSprintDrainPerSecond()
    {
        return Mathf.Max(7f, 18f - GetStatRank(GymStat.Endurance) * 0.75f) *
            GymSessionService.ActiveClass.sprintCost *
            GymBuffs.GetMultiplier(GymBuffStat.SprintDrain);
    }

    /// <summary>
    /// Class performance for one exercise, by its single primary category.
    /// Strength stations divide their timing difficulty by it; cardio
    /// machines scale their sustainable top pace with it.
    /// </summary>
    public float GetExercisePerformance(GymExerciseType exerciseType)
    {
        return GymSessionService.ActiveClass.Performance(GymClassCatalog.Category(exerciseType)) *
            GymBuffs.GetMultiplier(GymBuffStat.ExercisePerformance);
    }

    public float GetSprintRecoveryPerSecond()
    {
        return 14f + GetStatRank(GymStat.Endurance) * 2.2f;
    }

    public float GetCardioCapacityMultiplier()
    {
        return (1f + GetStatRank(GymStat.Endurance) * 0.035f) *
            GymBuffs.GetMultiplier(GymBuffStat.CardioCapacity);
    }

    public int GetTechniqueComboCap()
    {
        return 3 + Mathf.FloorToInt(GetStatRank(GymStat.Technique) * 0.5f);
    }

    public float GetTechniqueDifficultyScale(float baseScale)
    {
        float masteryChallengeScale = MasteryRank > 0 &&
            (MasteryRank - 1) % 4 == 2 ? 1.08f : 1f;
        return baseScale * masteryChallengeScale *
            (1f + temporaryFatigue * 0.45f) *
            GymBuffs.GetMultiplier(GymBuffStat.TechniqueDifficulty);
    }

    public static string GetMasteryTitle(int masteryRank)
    {
        if (masteryRank <= 0)
        {
            return "No title";
        }
        if (masteryRank < 3)
        {
            return "Gym Regular";
        }
        if (masteryRank < 6)
        {
            return "Iron Fixture";
        }
        if (masteryRank < 10)
        {
            return "Chaos Veteran";
        }
        return "Gym Myth";
    }

    public static string GetMasteryChallengeLabel(int masteryRank)
    {
        if (masteryRank <= 0)
        {
            return "Locked";
        }

        switch ((masteryRank - 1) % 4)
        {
            case 0: return "Clean Rep: misses break the streak";
            case 1: return "Crowded Floor: social choices matter more";
            case 2: return "Heavy Day: timing moves faster";
            default: return "No Quiet Exit: chaos follows you";
        }
    }

    public bool StrengthUltimateApplies(EnemyFighter enemy)
    {
        return HasStrengthUltimate && enemy != null && !enemy.IsDead &&
            enemy.Identity != BodybuilderIdentity.Ronnie &&
            enemy.Identity != BodybuilderIdentity.Goku &&
            enemy.Identity != BodybuilderIdentity.Manwithsuit1;
    }

    public bool ShouldSuppressEnemyAutoTarget(EnemyFighter enemy)
    {
        return HasPositiveReputationUltimate && enemy != null &&
            !enemy.IsAggressive && enemy.Identity != BodybuilderIdentity.Manwithsuit1;
    }

    public bool ShouldForceNegativeAutoTarget(EnemyFighter enemy)
    {
        return HasNegativeReputationUltimate && enemy != null &&
            enemy.Identity != BodybuilderIdentity.Manwithsuit1 && !enemy.IsDead;
    }

    public static int GetExperienceToNextLevel(int level)
    {
        level = Mathf.Max(1, level);
        int offset = level - 1;
        return 100 + offset * 35 + offset * offset * 6;
    }

    public void AwardExperience(
        int amount, string reason, string sourceKey = null, bool playSound = true)
    {
        if (state == null || amount <= 0)
        {
            return;
        }

        if (!CanReward(sourceKey))
        {
            return;
        }

        state.experience += amount;
        state.totalExperience += amount;
        bool leveled = false;
        while (state.experience >= GetExperienceToNextLevel(state.level))
        {
            state.experience -= GetExperienceToNextLevel(state.level);
            state.level++;
            state.skillPoints++;
            leveled = true;
        }

        if (leveled || playSound)
        {
            GymAudio.Play2D(
                leveled ? GymSoundEffect.LevelUp : GymSoundEffect.ActionConfirm,
                leveled ? 0.75f : 0.32f);
        }
        if (leveled)
        {
            levelChoiceVisible = true;
            levelChoiceWasCursorCaptured = player != null && player.CursorCaptured;
            levelChoiceCursorReleased = false;
            Debug.Log(
                $"GYMCHAOS_LEVEL_UP level={state.level} skillPoints={state.skillPoints}",
                this);
            ShowFeedback($"Level {state.level}. Choose a stat point when the room is clear.", 5f);
        }

        if (sessionBacked)
        {
            // Routine experience rides along with the timed autosave; only a
            // level-up or a one-time reward is worth an immediate request.
            GymSessionService.MarkDirty();
            if (leveled || (sourceKey != null && sourceKey.StartsWith("once:", StringComparison.Ordinal)))
            {
                GymSessionService.RequestSave("reward");
            }
        }
        else
        {
            nextSaveTime = Time.unscaledTime + 1.5f;
        }
        if (!string.IsNullOrWhiteSpace(reason))
        {
            Debug.Log($"GYMCHAOS_XP +{amount} reason={reason} total={state.totalExperience}", this);
        }
    }

    // ---- protein.com loyalty card ----

    /// <summary>
    /// Adds stamps to the loyalty card, limited by the card size and the
    /// per-gym-day cap so the shop rewards playing rather than grinding.
    /// Returns the stamps actually added.
    /// </summary>
    public int AwardLoyaltyStamps(int count, string reason)
    {
        if (state == null || count <= 0)
        {
            return 0;
        }
        if (state.loyaltyStampDay != state.gymDay)
        {
            state.loyaltyStampDay = state.gymDay;
            state.loyaltyStampsToday = 0;
        }
        int added = Mathf.Min(count,
            DailyLoyaltyStampCap - state.loyaltyStampsToday,
            MaxLoyaltyStamps - state.loyaltyStamps);
        if (added <= 0)
        {
            return 0;
        }
        state.loyaltyStamps += added;
        state.loyaltyStampsToday += added;
        NoticeRaised?.Invoke("protein.com stamp +" + added + "  (" + state.loyaltyStamps + ")", 2.2f, 1);
        Debug.Log($"GYMCHAOS_LOYALTY_STAMP +{added} reason={reason} total={state.loyaltyStamps} " +
            $"today={state.loyaltyStampsToday}", this);
        MarkProgressDirty();
        return added;
    }

    public bool TrySpendLoyaltyStamps(int count)
    {
        if (state == null || count <= 0 || state.loyaltyStamps < count)
        {
            return false;
        }
        state.loyaltyStamps -= count;
        MarkProgressDirty();
        return true;
    }

    /// <summary>The first counter visit each gym day stamps the card once.</summary>
    public void RegisterShopVisit()
    {
        if (state == null || state.loyaltyVisitDay == state.gymDay)
        {
            return;
        }
        state.loyaltyVisitDay = state.gymDay;
        AwardLoyaltyStamps(1, "daily shop visit");
    }

    private void MarkProgressDirty()
    {
        ProgressionChanged?.Invoke();
        if (sessionBacked)
        {
            GymSessionService.MarkDirty();
        }
        else
        {
            nextSaveTime = Time.unscaledTime + 1.5f;
        }
    }

    public void AllocateSkill(GymStat stat)
    {
        if (state == null || !levelChoiceVisible || state.skillPoints <= 0 ||
            EnemyFighter.IsFightActive || GymDialogueDirector.IsDialogueActive)
        {
            return;
        }

        int currentRank = state.GetStatRank(stat);
        if (currentRank < MaxStatRank)
        {
            state.SetStatRank(stat, currentRank + 1);
            ShowStatImpact(GetSkillImpactText(stat, currentRank), 2.4f);
        }
        else if (AllStatsMaxed)
        {
            state.masteryRank++;
            ShowStatImpact("MASTERY IMPACT   +1 MASTERY POINT", 2.4f);
        }
        else
        {
            ShowFeedback("That tree is maxed. Put the point into another stat first.", 2.5f);
            return;
        }

        state.skillPoints--;
        if (state.skillPoints <= 0)
        {
            levelChoiceVisible = false;
            RestoreTimeScale();
            if (player != null)
            {
                player.SetCursorCaptured(levelChoiceWasCursorCaptured);
            }
            levelChoiceCursorReleased = false;
        }

        EnsureCosmetics();
        SaveNow();
        Debug.Log(
            $"GYMCHAOS_STATS_UPDATED stat={stat} ranks=" +
            $"{state.strengthRank}/{state.enduranceRank}/{state.techniqueRank}/{state.reputationRank} " +
            $"mastery={state.masteryRank}",
            this);
    }

    public int ChangeReputation(int delta, string reason, bool countDailyGoal = true)
    {
        if (state == null || delta == 0)
        {
            return 0;
        }

        int previous = state.reputation;
        float multiplier = 1f + state.reputationRank * 0.12f;
        if (MasteryRank > 0 && (MasteryRank - 1) % 4 == 1)
        {
            multiplier *= 1.12f;
        }
        int adjusted = Mathf.RoundToInt(delta * multiplier);
        state.reputation = Mathf.Clamp(state.reputation + adjusted, -100, 100);
        int applied = state.reputation - previous;
        if (countDailyGoal)
        {
            RegisterDailyGoal(delta > 0 ? 1 : 2);
        }
        // Reputation changes are silent: only the top-right REP stat updates.
        Debug.Log(
            $"GYMCHAOS_REPUTATION_CHANGED delta={applied} value={state.reputation} reason={reason}",
            this);
        SaveNow(false);
        return applied;
    }

    /// <summary>Base XP per rep: Perfect clearly pays most, Good less, a miss little.</summary>
    public static int GetRepReward(WorkoutResult result) =>
        result == WorkoutResult.Perfect || result == WorkoutResult.AutoPerfect ? 10 :
        result == WorkoutResult.Good ? 5 : 1;

    public void RegisterWorkoutRep(
        WorkoutResult result, GymExerciseType exerciseType, int weight, int comboMultiplier = 1)
    {
        if (result == WorkoutResult.None)
        {
            return;
        }

        int baseReward = GetRepReward(result);
        int multiplier = Mathf.Clamp(comboMultiplier, 1, 8);
        int reward = baseReward * multiplier;
        string resultLabel = result == WorkoutResult.AutoPerfect ? "AUTO PERFECT" : result.ToString().ToUpperInvariant();
        if ((result == WorkoutResult.Perfect || result == WorkoutResult.AutoPerfect) &&
            player != null)
        {
            // Every perfect rep of any exercise gives a little health back.
            player.RestorePerfectRepHealth();
        }
        UpdateTemporaryFatigue(result);
        ShowFeedback($"{resultLabel}  x{multiplier}  +{reward} XP", 1.15f);
        // The feedback line above already carries this rep; one notice per rep.
        ShowStatImpact($"{resultLabel} IMPACT   XP +{reward}", 1.15f, false);
        string key = $"workout-rep-{Time.frameCount}";
        AwardExperience(reward, result.ToString(), key);
        RegisterDailyGoal(0);
        if (result != WorkoutResult.Miss && state != null)
        {
            state.loyaltyRepCounter++;
            if (state.loyaltyRepCounter >= RepsPerLoyaltyStamp)
            {
                state.loyaltyRepCounter = 0;
                AwardLoyaltyStamps(1, "10 reps");
            }
        }
    }

    // ---- daily cardio challenge ----

    public enum CardioChallengeKind
    {
        TreadmillDistance,
        BikeDistance,
        ReachPace,
        HoldPace,
        PaceGates
    }

    private const int CardioChallengeReward = 60;

    private void EnsureCardioChallenge()
    {
        if (state == null || state.cardioChallengeDay == state.gymDay)
        {
            return;
        }

        // Deterministic per day, so a reload keeps the same challenge.
        var random = new System.Random(state.gymDay * 7919 + 17);
        state.cardioChallengeDay = state.gymDay;
        state.cardioChallengeKind = random.Next(0, 5);
        state.cardioChallengeProgress = 0f;
        state.cardioChallengeCompleted = false;
        switch ((CardioChallengeKind)state.cardioChallengeKind)
        {
            case CardioChallengeKind.TreadmillDistance:
                state.cardioChallengeTarget = 500f + random.Next(0, 7) * 100f;
                break;
            case CardioChallengeKind.BikeDistance:
                state.cardioChallengeTarget = 800f + random.Next(0, 8) * 100f;
                break;
            case CardioChallengeKind.ReachPace:
            {
                // Never above what this character can open on a treadmill.
                float reachable = 18f * GetCardioCapacityMultiplier() *
                    GetExercisePerformance(GymExerciseType.Treadmill) * 0.8f;
                float pace = 8f + random.Next(0, 4) * 2f;
                state.cardioChallengeTarget = Mathf.Max(
                    4f, Mathf.Min(pace, Mathf.Floor(reachable / 2f) * 2f));
                break;
            }
            case CardioChallengeKind.HoldPace:
                state.cardioChallengeTarget = 20f + random.Next(0, 5) * 10f;
                break;
            default:
                state.cardioChallengeTarget = 2f + random.Next(0, 3);
                break;
        }
        Debug.Log(
            $"GYMCHAOS_CARDIO_CHALLENGE day={state.gymDay} kind={(CardioChallengeKind)state.cardioChallengeKind} " +
            $"target={state.cardioChallengeTarget:0}",
            this);
    }

    private float GetHoldPace()
    {
        // Held pace scales with the challenge day but stays a jog.
        return 6f + (state.gymDay % 3) * 2f;
    }

    public CardioChallengeKind CurrentCardioChallengeKind
    {
        get
        {
            EnsureCardioChallenge();
            return state != null ? (CardioChallengeKind)state.cardioChallengeKind : CardioChallengeKind.TreadmillDistance;
        }
    }

    public bool CardioChallengeCompleted => state != null && state.cardioChallengeCompleted;

    public float CardioChallengeFraction
    {
        get
        {
            EnsureCardioChallenge();
            return state == null ? 0f : Mathf.Clamp01(
                state.cardioChallengeProgress / Mathf.Max(0.01f, state.cardioChallengeTarget));
        }
    }

    public string CardioChallengeLabel
    {
        get
        {
            EnsureCardioChallenge();
            if (state == null)
            {
                return string.Empty;
            }
            float target = state.cardioChallengeTarget;
            switch ((CardioChallengeKind)state.cardioChallengeKind)
            {
                case CardioChallengeKind.TreadmillDistance:
                    return $"Run {target / 1000f:0.0} km on a treadmill";
                case CardioChallengeKind.BikeDistance:
                    return $"Ride {target / 1000f:0.0} km on a bike";
                case CardioChallengeKind.ReachPace:
                    return $"Reach {target:0} km/h on a treadmill";
                case CardioChallengeKind.HoldPace:
                    return $"Hold {GetHoldPace():0}+ pace for {target:0} s";
                default:
                    return $"Clear {target:0} pace gates perfectly";
            }
        }
    }

    public string CardioChallengeProgressText
    {
        get
        {
            EnsureCardioChallenge();
            if (state == null)
            {
                return string.Empty;
            }
            if (state.cardioChallengeCompleted)
            {
                return "DONE";
            }
            float progress = state.cardioChallengeProgress;
            float target = state.cardioChallengeTarget;
            switch ((CardioChallengeKind)state.cardioChallengeKind)
            {
                case CardioChallengeKind.TreadmillDistance:
                case CardioChallengeKind.BikeDistance:
                    return $"{progress / 1000f:0.00} / {target / 1000f:0.0} km";
                case CardioChallengeKind.ReachPace:
                    return $"{progress:0.0} / {target:0} km/h";
                case CardioChallengeKind.HoldPace:
                    return $"{progress:0} / {target:0} s";
                default:
                    return $"{progress:0} / {target:0}";
            }
        }
    }

    /// <summary>Per-frame cardio progress from the player's station.</summary>
    public void ReportCardioProgress(
        GymExerciseType type, float speed, float metres, float seconds)
    {
        EnsureCardioChallenge();
        if (state == null || state.cardioChallengeCompleted)
        {
            return;
        }
        bool treadmill = type == GymExerciseType.Treadmill;
        switch ((CardioChallengeKind)state.cardioChallengeKind)
        {
            case CardioChallengeKind.TreadmillDistance:
                if (treadmill) state.cardioChallengeProgress += Mathf.Max(0f, metres);
                break;
            case CardioChallengeKind.BikeDistance:
                if (type == GymExerciseType.ExerciseBike) state.cardioChallengeProgress += Mathf.Max(0f, metres);
                break;
            case CardioChallengeKind.ReachPace:
                if (treadmill) state.cardioChallengeProgress = Mathf.Max(state.cardioChallengeProgress, speed);
                break;
            case CardioChallengeKind.HoldPace:
                // A drop below the pace restarts the hold.
                state.cardioChallengeProgress = speed >= GetHoldPace() - 0.05f
                    ? state.cardioChallengeProgress + Mathf.Max(0f, seconds)
                    : 0f;
                break;
            default:
                return;
        }
        CheckCardioChallenge();
    }

    /// <summary>A hold only counts while on the machine: leaving resets it.</summary>
    public void ReportCardioStopped()
    {
        if (state != null && !state.cardioChallengeCompleted &&
            (CardioChallengeKind)state.cardioChallengeKind == CardioChallengeKind.HoldPace)
        {
            state.cardioChallengeProgress = 0f;
        }
    }

    /// <summary>
    /// A pace gate result. Gates are not reps: they only open the next speed
    /// band, pay a little XP, and count for the gate challenge.
    /// </summary>
    public void RegisterCardioGate(WorkoutResult result, GymExerciseType type, float unlockedSpeed)
    {
        EnsureCardioChallenge();
        if (state == null)
        {
            return;
        }
        bool perfect = result == WorkoutResult.Perfect || result == WorkoutResult.AutoPerfect;
        bool passed = perfect || result == WorkoutResult.Good;
        string unit = type == GymExerciseType.Treadmill ? "km/h" : "pace";
        if (passed)
        {
            int reward = perfect ? 4 : 2;
            AwardExperience(reward, "pace gate", "pace-gate-" + Time.frameCount);
            ShowFeedback($"PACE GATE {(perfect ? "PERFECT" : "GOOD")}   UP TO {unlockedSpeed:0} {unit}   +{reward} XP", 1.4f);
            if (perfect && !state.cardioChallengeCompleted &&
                (CardioChallengeKind)state.cardioChallengeKind == CardioChallengeKind.PaceGates)
            {
                state.cardioChallengeProgress += 1f;
                CheckCardioChallenge();
            }
        }
        else
        {
            ShowFeedback($"PACE GATE MISSED   HOLDING {unlockedSpeed:0} {unit}", 1.4f);
        }
    }

    private void CheckCardioChallenge()
    {
        if (state.cardioChallengeCompleted ||
            state.cardioChallengeProgress < state.cardioChallengeTarget - 0.001f)
        {
            return;
        }
        state.cardioChallengeCompleted = true;
        state.cardioChallengeProgress = state.cardioChallengeTarget;
        string label = CardioChallengeLabel;
        AwardExperience(CardioChallengeReward, "cardio challenge", "once:cardio-challenge-" + state.gymDay);
        ShowFeedback("Cardio challenge complete: " + label, 4f, 2);
        AwardLoyaltyStamps(3, "cardio challenge");
        if (sessionBacked)
        {
            GymSessionService.RequestSave("cardio-challenge");
        }
        else
        {
            SaveNow(false);
        }
        Debug.Log($"GYMCHAOS_CARDIO_CHALLENGE_COMPLETE day={state.gymDay} label={label}", this);
    }

    public void RegisterCardio(float distanceMetres, float seconds)
    {
        if (state == null || (distanceMetres < 2f && seconds < 8f))
        {
            return;
        }

        int reward = Mathf.Clamp(Mathf.RoundToInt(distanceMetres * 0.12f + seconds * 0.4f), 1, 35);
        AwardExperience(
            reward, "cardio milestone",
            "cardio-session-" + state.gymDay + "-" + Time.frameCount);
        RegisterDailyGoal(0);
    }

    public void RegisterCombatHit(EnemyFighter enemy)
    {
        if (enemy == null)
        {
            return;
        }

        AwardExperience(3, "combat hit", "combat-hit-" + enemy.Identity + "-" + enemy.name, false);
        ChangeReputation(-2, "attacked " + enemy.Identity);
    }

    public void RegisterEnemyDefeat(EnemyFighter enemy)
    {
        if (enemy == null)
        {
            return;
        }

        AwardExperience(35, "enemy defeated", "enemy-defeat-" + enemy.Identity + "-" + enemy.name);
        RegisterDailyGoal(2);
        AwardLoyaltyStamps(1, "enemy defeated");
    }

    public void RegisterMirrorBreak(string sourceKey)
    {
        AwardExperience(12, "mirror broken", "mirror-break-" + sourceKey);
        RegisterDailyGoal(2);
    }

    public void RegisterFriendlyAction(string reason)
    {
        if (state == null)
        {
            return;
        }

        AwardExperience(5, "social action", "social-" + state.gymDay + "-" + reason, false);
        ChangeReputation(3, reason);
    }

    public void RegisterDialogueChoice(int reputationDelta, string sourceKey = null)
    {
        if (state == null)
        {
            return;
        }

        if (reputationDelta > 0)
        {
            ChangeReputation(reputationDelta, "dialogue", false);
        }
        else if (reputationDelta < 0)
        {
            ChangeReputation(reputationDelta, "dialogue", false);
            RegisterDailyGoal(2);
        }
        string rewardKey = string.IsNullOrWhiteSpace(sourceKey)
            ? "dialogue-choice-" + state.gymDay + "-" + Time.frameCount
            : "dialogue-choice-" + state.gymDay + "-" + sourceKey;
        AwardExperience(6, "dialogue choice", rewardKey, reputationDelta == 0);
    }

    public void RegisterDialogueStarted(BodybuilderIdentity identity)
    {
        if (state == null)
        {
            return;
        }

        string rewardKey = "once:dialogue-open-" + state.gymDay + "-" + identity;
        bool firstToday = !oneShotRewards.Contains(rewardKey);
        AwardExperience(
            4,
            "dialogue started",
            rewardKey);
        if (firstToday)
        {
            RegisterDailyGoal(1);
        }
    }

    public void RegisterVisitorHelp(
        BodybuilderIdentity identity, bool countDailyGoal = true)
    {
        if (state == null)
        {
            return;
        }

        string rewardKey = "once:visitor-help-" + identity + "-" + state.gymDay;
        bool firstToday = !oneShotRewards.Contains(rewardKey);
        if (!firstToday)
        {
            return;
        }

        AwardExperience(18, "helped visitor", rewardKey, false);
        ChangeReputation(4, "helped " + identity, countDailyGoal);
        AwardLoyaltyStamps(1, "helped visitor");
    }

    public void RegisterSpaceDiscovery(string discoveryKey, string feedback = null)
    {
        if (state == null || string.IsNullOrWhiteSpace(discoveryKey))
        {
            return;
        }

        string rewardKey = "once:discovery-" + state.gymDay + "-" + discoveryKey;
        bool alreadyDiscovered = oneShotRewards.Contains(rewardKey);
        AwardExperience(10, "new space discovered", rewardKey);
        if (!alreadyDiscovered)
        {
            ShowFeedback(string.IsNullOrWhiteSpace(feedback)
                ? "New space discovered  +10 XP"
                : feedback, 2.8f);
            Debug.Log("GYMCHAOS_DISCOVERY_COMPLETE key=" + discoveryKey, this);
        }
    }

    public bool CanStartWorkout()
    {
        if (state == null || state.lockerPrepCompleted)
        {
            return true;
        }

        ShowFeedback("Visit the locker room and get ready before your first workout today.", 3.5f);
        Debug.Log("GYMCHAOS_WORKOUT_BLOCKED reason=locker_room_prep_required", this);
        return false;
    }

    public void CompleteLockerPrep()
    {
        if (state == null || state.lockerPrepCompleted)
        {
            return;
        }

        state.lockerPrepCompleted = true;
        AwardExperience(8, "locker room prep", "locker-prep-" + state.gymDay);
        ShowFeedback("Ready. Pick a station and put in the work.", 3f);
        Debug.Log("GYMCHAOS_LOCKER_PREP_COMPLETE day=" + state.gymDay, this);
        SaveNow();
    }

    private void UpdateTemporaryFatigue(WorkoutResult result)
    {
        // Technique is more than a wider green slice: a trained player also
        // keeps a bad rep from snowballing into a full session shutdown.
        float techniqueRelief = (1f + GetStatRank(GymStat.Technique) * 0.08f) /
            GymBuffs.GetMultiplier(GymBuffStat.FatigueGain);
        if (result == WorkoutResult.Miss)
        {
            temporaryFatigue = Mathf.Clamp01(
                temporaryFatigue + 0.14f / techniqueRelief);
        }
        else if (result == WorkoutResult.Good)
        {
            temporaryFatigue = Mathf.Clamp01(
                temporaryFatigue + 0.025f / techniqueRelief);
        }
        else if (result == WorkoutResult.Perfect || result == WorkoutResult.AutoPerfect)
        {
            temporaryFatigue = Mathf.Max(0f, temporaryFatigue - 0.04f);
        }
    }

    public bool IsCosmeticUnlocked(GymShirtColor shirt)
    {
        if (shirt == GymShirtColor.Gold)
        {
            return MasteryRank >= 1;
        }

        int requiredLevel = shirt == GymShirtColor.Black ? 1 :
            shirt == GymShirtColor.White ? 2 : shirt == GymShirtColor.Red ? 4 : 6;
        return Level >= requiredLevel;
    }

    public bool IsCosmeticUnlocked(GymHeadwear headwear)
    {
        if (headwear == GymHeadwear.BucketHat)
        {
            return MasteryRank >= 2;
        }

        int requiredLevel = headwear == GymHeadwear.None ? 1 :
            headwear == GymHeadwear.Cap ? 3 : headwear == GymHeadwear.Headband ? 5 : 8;
        return Level >= requiredLevel;
    }

    public void EquipShirt(GymShirtColor shirt)
    {
        if (state == null || !IsCosmeticUnlocked(shirt))
        {
            ShowFeedback("That shirt unlocks at a higher level.", 2f);
            return;
        }

        state.shirt = shirt.ToString();
        EnsureCosmetics();
        cosmetics?.ApplyFromState(state);
        SaveNow();
    }

    public void EquipHeadwear(GymHeadwear headwear)
    {
        if (state == null || !IsCosmeticUnlocked(headwear))
        {
            ShowFeedback("That headwear unlocks at a higher level.", 2f);
            return;
        }

        state.headwear = headwear.ToString();
        cosmetics?.ApplyFromState(state);
        SaveNow();
    }

    public GymBackRoomInteractable FindNearbyInteractable(Vector3 position, float maxDistance)
    {
        IReadOnlyList<GymBackRoomInteractable> interactables =
            GymBackRoomInteractable.RegisteredInteractables;
        GymBackRoomInteractable closest = null;
        float best = float.PositiveInfinity;
        for (int i = 0; i < interactables.Count; i++)
        {
            GymBackRoomInteractable candidate = interactables[i];
            if (candidate == null || !candidate.isActiveAndEnabled)
            {
                continue;
            }

            // Planar reach: the prompt must not depend on whether the player
            // pivot sits at the feet or the capsule centre.
            Vector3 offset = candidate.transform.position - position;
            if (Mathf.Abs(offset.y) > 2.5f)
            {
                continue;
            }
            offset.y = 0f;
            float reach = Mathf.Max(maxDistance, candidate.InteractionRange);
            float distance = offset.sqrMagnitude;
            if (distance <= reach * reach && distance < best)
            {
                best = distance;
                closest = candidate;
            }
        }
        return closest;
    }

    public static bool TryHandlePlayerInteraction(PlayerMovement targetPlayer, bool interactPressed)
    {
        if (instance == null || targetPlayer == null)
        {
            return false;
        }

        if (instance.IsBlockingPlayerInput)
        {
            return true;
        }

        if (!interactPressed)
        {
            return false;
        }

        GymBackRoomInteractable nearby =
            instance.FindNearbyInteractable(targetPlayer.transform.position, 3.1f);
        if (nearby == null)
        {
            return false;
        }

        switch (nearby.InteractionType)
        {
            case GymBackRoomInteractionType.Locker:
                instance.OpenLockerMenu(targetPlayer);
                break;
            case GymBackRoomInteractionType.Prep:
                instance.CompleteLockerPrep();
                break;
            case GymBackRoomInteractionType.ToiletSeat:
                nearby.GetComponent<GymToiletSeat>()?.Toggle();
                break;
        }
        return true;
    }

    private void OpenLockerMenu(PlayerMovement targetPlayer)
    {
        if (lockerMenuOpen || targetPlayer == null)
        {
            return;
        }

        lockerMenuPlayer = targetPlayer;
        lockerMenuPreviousPosition = targetPlayer.transform.position;
        lockerMenuPreviousRotation = targetPlayer.transform.rotation;
        if (targetPlayer.playerCamera != null)
        {
            lockerMenuPreviousCameraPosition = targetPlayer.playerCamera.transform.localPosition;
            lockerMenuPreviousCameraRotation = targetPlayer.playerCamera.transform.localRotation;
        }

        if (GymBackRoomBuilder.TryGetLockerPreviewPose(
                out Vector3 previewPosition, out Quaternion previewRotation) &&
            targetPlayer.playerCamera != null)
        {
            targetPlayer.SetCinematicPose(
                previewPosition,
                previewRotation,
                targetPlayer.StandingCameraLocalPosition,
                Quaternion.Euler(-4f, 0f, 0f));
            lockerMenuPoseCaptured = true;
        }

        PlayerHandRig playerRig =
            targetPlayer.GetComponentInChildren<PlayerHandRig>(true);
        playerRig?.EnsureMirrorAppearance();
        lockerMenuOpen = true;
        GymLockerMenuUI.Ensure(this);
        SetLockerMirrorContinuousRefresh(true);
        lockerMenuWasCursorCaptured = targetPlayer.CursorCaptured;
        targetPlayer.SetCinematicLock(true);
        targetPlayer.SetCursorCaptured(false);
        Debug.Log(
            $"GYMCHAOS_LOCKER_MENU_OPEN preview={lockerMenuPoseCaptured}", this);
    }

    private void CloseLockerMenu()
    {
        lockerMenuOpen = false;
        SetLockerMirrorContinuousRefresh(false);
        if (lockerMenuPlayer != null)
        {
            if (lockerMenuPoseCaptured)
            {
                lockerMenuPlayer.RestoreCinematicPose(
                    lockerMenuPreviousPosition,
                    lockerMenuPreviousRotation,
                    lockerMenuPreviousCameraPosition,
                    lockerMenuPreviousCameraRotation);
            }

            lockerMenuPlayer.SetCinematicLock(false);
            lockerMenuPlayer.SetCursorCaptured(lockerMenuWasCursorCaptured);
        }
        lockerMenuPlayer = null;
        lockerMenuPoseCaptured = false;
        Debug.Log("GYMCHAOS_LOCKER_MENU_CLOSED", this);
    }

    private static void SetLockerMirrorContinuousRefresh(bool enabled)
    {
        PlanarGymMirror[] mirrors = UnityEngine.Object.FindObjectsByType<PlanarGymMirror>(
            FindObjectsSortMode.None);
        for (int i = 0; i < mirrors.Length; i++)
        {
            PlanarGymMirror mirror = mirrors[i];
            if (mirror != null &&
                Vector3.Dot(mirror.PlaneNormal, Vector3.forward) > 0.9f)
            {
                mirror.ContinuousRefresh = enabled;
                mirror.RequestImmediateRefresh();
            }
        }
    }

    private void EnsureCosmetics()
    {
        if (player == null)
        {
            return;
        }

        cosmetics = player.GetComponent<PlayerCosmeticLoadout>();
        if (cosmetics == null)
        {
            cosmetics = player.gameObject.AddComponent<PlayerCosmeticLoadout>();
        }
        cosmetics.Initialize(player);
        cosmetics.ApplyFromState(state);
    }

    private void RegisterDailyGoal(int goalIndex)
    {
        if (state == null || goalIndex < 0 || goalIndex >= DailyGoalTargets.Length ||
            state.dailyCompleted[goalIndex])
        {
            return;
        }

        state.dailyProgress[goalIndex] = Mathf.Min(
            DailyGoalTargets[goalIndex], state.dailyProgress[goalIndex] + 1);
        if (state.dailyProgress[goalIndex] < DailyGoalTargets[goalIndex])
        {
            return;
        }

        state.dailyCompleted[goalIndex] = true;
        AwardExperience(45, "daily goal", "daily-goal-" + state.gymDay + "-" + goalIndex);
        ShowFeedback("Daily goal complete: " + DailyGoalLabels[goalIndex], 4f, 2);
        if (sessionBacked)
        {
            GymSessionService.RequestSave("goal");
        }
        Debug.Log(
            $"GYMCHAOS_DAILY_GOAL_COMPLETE day={state.gymDay} goal={goalIndex} " +
            $"label={DailyGoalLabels[goalIndex]}",
            this);
    }

    private bool CanReward(string sourceKey)
    {
        if (string.IsNullOrWhiteSpace(sourceKey))
        {
            return true;
        }

        if (sourceKey.StartsWith("once:", StringComparison.Ordinal))
        {
            return oneShotRewards.Add(sourceKey);
        }

        float cooldown = 0.45f;
        if (sourceKey.StartsWith("dialogue-open-", StringComparison.Ordinal))
        {
            cooldown = 6f;
        }
        else if (sourceKey.StartsWith("dialogue-choice-", StringComparison.Ordinal))
        {
            cooldown = 1.2f;
        }
        else if (sourceKey.StartsWith("visitor-help-", StringComparison.Ordinal))
        {
            cooldown = 8f;
        }
        else if (sourceKey.StartsWith("social-", StringComparison.Ordinal))
        {
            cooldown = 3f;
        }

        if (recentRewards.TryGetValue(sourceKey, out float lastTime) &&
            Time.unscaledTime - lastTime < cooldown)
        {
            return false;
        }

        recentRewards[sourceKey] = Time.unscaledTime;
        return true;
    }

    private void ShowFeedback(string text, float seconds, int priority = 0)
    {
        feedbackText = text;
        feedbackUntil = Time.unscaledTime + seconds;
        NoticeRaised?.Invoke(text, seconds, priority);
    }

    private void ShowStatImpact(string text, float seconds, bool notify = true)
    {
        statImpactText = text;
        statImpactUntil = Time.unscaledTime + seconds;
        if (notify)
        {
            NoticeRaised?.Invoke(text, seconds, 1);
        }
    }

    public string GetDailyGoalLabel(int index) =>
        index >= 0 && index < DailyGoalLabels.Length ? DailyGoalLabels[index] : string.Empty;
    public int GetDailyGoalTarget(int index) =>
        index >= 0 && index < DailyGoalTargets.Length ? DailyGoalTargets[index] : 1;
    public int DailyGoalCount => DailyGoalTargets.Length;

    /// <summary>Impact line the level-up menu shows for the next point in a tree.</summary>
    public string GetSkillChoicePreview(GymStat stat) => GetSkillImpactText(stat, GetStatRank(stat));

    private string GetSkillImpactText(GymStat stat, int previousRank)
    {
        switch (stat)
        {
            case GymStat.Strength:
                return "STR IMPACT   DAMAGE +8%   FORCE +4.2%";
            case GymStat.Endurance:
                return "END IMPACT   SPRINT +5.5%   CAPACITY +8";
            case GymStat.Technique:
            {
                int previousCombo = 3 + Mathf.FloorToInt(previousRank * 0.5f);
                int nextCombo = 3 + Mathf.FloorToInt((previousRank + 1) * 0.5f);
                return nextCombo > previousCombo
                    ? "TECH IMPACT   COMBO CAP +1"
                    : "TECH IMPACT   TIMING CONTROL +1";
            }
            case GymStat.Reputation:
                return "REP IMPACT   SOCIAL EFFECT +12%";
            default:
                return "STAT IMPACT   +1";
        }
    }

    private void SaveNow(bool durableChange = true)
    {
        if (state == null)
        {
            return;
        }

        state.Normalize();
        ProgressionChanged?.Invoke();
        if (sessionBacked)
        {
            // The character slot is written by the session service: durable
            // changes request a debounced save, frequent ones (reputation per
            // hit) only flag the state for the next timed autosave.
            nextSaveTime = float.PositiveInfinity;
            if (durableChange)
            {
                GymSessionService.RequestSave("progression");
            }
            else
            {
                GymSessionService.MarkDirty();
            }
            return;
        }

        PlayerPrefs.SetString(SaveKey, JsonUtility.ToJson(state));
        PlayerPrefs.Save();
        nextSaveTime = Time.unscaledTime + 2f;
    }

    private void RestoreTimeScale()
    {
        if (timeScaleChanged)
        {
            Time.timeScale = 1f;
            timeScaleChanged = false;
        }
    }

    private void OnGUI()
    {
        if (state == null || GymStartScreen.IsMenuVisible || GymPauseMenu.IsVisible)
        {
            return;
        }

        if (GymDialogueDirector.IsDialogueActive)
        {
            return;
        }

        EnsureProgressHudStyles();
        if (!lockerMenuOpen && !GymHud.IsActive)
        {
            // The uGUI HUD replaces these panels and the feedback line.
            DrawProgressHud();
            DrawDailyGoals();
        }
    }


    private void DrawProgressHud()
    {
        float width = Mathf.Min(360f, Screen.width - 32f);
        float x = Mathf.Max(16f, Screen.width - width - 16f);
        bool hasImpact = !string.IsNullOrEmpty(statImpactText);
        float panelHeight = hasImpact ? 136f : 112f;
        float y = 18f;
        Rect panel = new Rect(x, y, width, panelHeight);
        DrawProgressHudRect(panel, new Color(0.018f, 0.025f, 0.055f, 0.9f));
        DrawProgressHudRect(
            new Rect(x, y, 3f, panelHeight),
            new Color(0.95f, 0.2f, 0.08f, 0.9f));

        int xpToNextLevel = Mathf.Max(1, GetExperienceToNextLevel(state.level));
        float xpProgress = Mathf.Clamp01(state.experience / (float)xpToNextLevel);
        DrawProgressHudText(
            new Rect(x + 14f, y + 8f, width - 28f, 20f),
            $"LEVEL {state.level}   XP {state.experience}/{xpToNextLevel}",
            progressHudTitleStyle);
        DrawProgressHudBar(
            new Rect(x + 14f, y + 34f, width - 28f, 8f),
            xpProgress,
            new Color(1f, 0.82f, 0.35f, 0.95f));
        DrawProgressHudText(
            new Rect(x + 14f, y + 49f, width - 28f, 18f),
            $"STR {state.strengthRank}  END {state.enduranceRank}  TECH {state.techniqueRank}",
            progressHudBodyStyle);
        DrawProgressHudText(
            new Rect(x + 14f, y + 69f, width - 28f, 18f),
            $"REP {state.reputation:+#;-#;0}  MASTERY {state.masteryRank}",
            progressHudBodyStyle);

        if (hasImpact)
        {
            DrawProgressHudText(
                new Rect(x + 14f, y + 93f, width - 28f, 22f),
                statImpactText,
                progressHudImpactStyle);
        }

        if (!string.IsNullOrEmpty(feedbackText))
        {
            float feedbackWidth = Mathf.Min(620f, Screen.width - 40f);
            DrawProgressHudText(
                new Rect(
                    (Screen.width - feedbackWidth) * 0.5f,
                    Screen.height - 172f,
                    feedbackWidth,
                    48f),
                feedbackText,
                progressHudFeedbackStyle);
        }
    }

    private void DrawDailyGoals()
    {
        float width = Mathf.Min(360f, Screen.width - 32f);
        float x = Mathf.Max(16f, Screen.width - width - 16f);
        float top = string.IsNullOrEmpty(statImpactText) ? 140f : 164f;
        const float panelHeight = 88f;
        Rect panel = new Rect(x, top, width, panelHeight);
        DrawProgressHudRect(panel, new Color(0.018f, 0.025f, 0.055f, 0.86f));
        DrawProgressHudRect(
            new Rect(x, top, 3f, panelHeight),
            new Color(0.48f, 0.86f, 1f, 0.9f));
        DrawProgressHudText(
            new Rect(x + 14f, top + 8f, width - 28f, 18f),
            $"DAY {state.gymDay}   GOALS",
            progressHudTitleStyle);

        for (int i = 0; i < DailyGoalLabels.Length; i++)
        {
            float rowY = top + 31f + i * 17f;
            float progress = Mathf.Clamp01(
                state.dailyProgress[i] / (float)Mathf.Max(1, DailyGoalTargets[i]));
            string marker = state.dailyCompleted[i]
                ? "OK"
                : $"{state.dailyProgress[i]}/{DailyGoalTargets[i]}";
            DrawProgressHudText(
                new Rect(x + 14f, rowY - 2f, 52f, 16f),
                GetDailyGoalShortLabel(i),
                progressHudGoalStyle);
            DrawProgressHudBar(
                new Rect(x + 72f, rowY + 2f, width - 128f, 7f),
                progress,
                state.dailyCompleted[i]
                    ? new Color(1f, 0.82f, 0.35f, 0.95f)
                    : new Color(0.48f, 0.86f, 1f, 0.9f));
            DrawProgressHudText(
                new Rect(x + width - 48f, rowY - 2f, 34f, 16f),
                marker,
                progressHudGoalStyle);
        }
    }

    private static string GetDailyGoalShortLabel(int index)
    {
        switch (index)
        {
            case 0: return "REPS";
            case 1: return "SOCIAL";
            case 2: return "CHAOS";
            default: return "GOAL";
        }
    }

    private static void DrawProgressHudRect(Rect rect, Color color)
    {
        Color previousColor = GUI.color;
        GUI.color = color;
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = previousColor;
    }

    private static void DrawProgressHudBar(Rect rect, float progress, Color fillColor)
    {
        DrawProgressHudRect(rect, new Color(0f, 0f, 0f, 0.5f));
        float fillWidth = rect.width * Mathf.Clamp01(progress);
        if (fillWidth > 0.5f)
        {
            DrawProgressHudRect(
                new Rect(rect.x, rect.y, fillWidth, rect.height),
                fillColor);
        }
    }

    private void EnsureProgressHudStyles()
    {
        if (progressHudTitleStyle != null)
        {
            return;
        }

        progressHudShadowStyle = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.UpperLeft,
            fontSize = 14,
            fontStyle = FontStyle.Bold,
            padding = new RectOffset(0, 0, 0, 0)
        };
        progressHudShadowStyle.normal.textColor = new Color(0f, 0f, 0f, 0.82f);

        progressHudTitleStyle = CreateProgressHudStyle(15, FontStyle.Bold, TextAnchor.UpperLeft);
        progressHudTitleStyle.normal.textColor = new Color(1f, 0.82f, 0.35f);
        progressHudBodyStyle = CreateProgressHudStyle(13, FontStyle.Bold, TextAnchor.UpperLeft);
        progressHudBodyStyle.normal.textColor = Color.white;
        progressHudGoalStyle = CreateProgressHudStyle(12, FontStyle.Normal, TextAnchor.UpperLeft);
        progressHudGoalStyle.normal.textColor = new Color(0.86f, 0.91f, 0.98f);
        progressHudFeedbackStyle = CreateProgressHudStyle(16, FontStyle.Bold, TextAnchor.MiddleCenter);
        progressHudFeedbackStyle.normal.textColor = new Color(1f, 0.82f, 0.35f);
        progressHudImpactStyle = CreateProgressHudStyle(12, FontStyle.Bold, TextAnchor.UpperLeft);
        progressHudImpactStyle.normal.textColor = new Color(0.48f, 0.86f, 1f);

    }

    private static GUIStyle CreateProgressHudStyle(
        int fontSize, FontStyle fontStyle, TextAnchor alignment)
    {
        GUIStyle style = new GUIStyle(GUI.skin.label)
        {
            alignment = alignment,
            fontSize = fontSize,
            fontStyle = fontStyle,
            padding = new RectOffset(0, 0, 0, 0),
            wordWrap = true
        };
        style.normal.background = null;
        style.hover.background = null;
        style.active.background = null;
        style.focused.background = null;
        return style;
    }

    private void DrawProgressHudText(Rect rect, string text, GUIStyle style)
    {
        progressHudShadowStyle.font = style.font;
        progressHudShadowStyle.fontSize = style.fontSize;
        progressHudShadowStyle.fontStyle = style.fontStyle;
        progressHudShadowStyle.alignment = style.alignment;
        progressHudShadowStyle.wordWrap = style.wordWrap;
        GUI.Label(new Rect(rect.x + 1f, rect.y + 1f, rect.width, rect.height), text, progressHudShadowStyle);
        GUI.Label(rect, text, style);
    }

    // ---- locker outfit menu (presented by GymLockerMenuUI) ----

    public static System.Collections.Generic.IReadOnlyList<GymShirtColor> LockerShirts => LockerShirtOptions;
    public static System.Collections.Generic.IReadOnlyList<GymHeadwear> LockerHeadwear => LockerHeadwearOptions;

    public bool IsShirtWorn(GymShirtColor shirt) =>
        state != null && string.Equals(state.shirt, shirt.ToString(), StringComparison.OrdinalIgnoreCase);

    public bool IsHeadwearWorn(GymHeadwear item) =>
        state != null && string.Equals(state.headwear, item.ToString(), StringComparison.OrdinalIgnoreCase);

    public string GetLockerShirtLabel(GymShirtColor shirt)
    {
        string name = shirt.ToString().ToUpperInvariant();
        if (IsCosmeticUnlocked(shirt)) return name;
        return shirt == GymShirtColor.Gold ? name + "  M1" : name + "  L" + GetShirtUnlockLevel(shirt);
    }

    public string GetLockerHeadwearLabel(GymHeadwear item)
    {
        string name = PlayerCosmeticLoadout.GetHeadwearDisplayName(item).ToUpperInvariant();
        if (IsCosmeticUnlocked(item)) return name;
        return item == GymHeadwear.BucketHat ? name + "  M2" : name + "  L" + GetHeadwearUnlockLevel(item);
    }

    public string LockerRankLine => state != null
        ? "LEVEL " + state.level + "   MASTERY " + state.masteryRank
        : string.Empty;

    public string LockerWearingLine => state == null ? string.Empty :
        "WEARING   " + state.shirt.ToUpperInvariant() + "   " +
        (cosmetics != null
            ? PlayerCosmeticLoadout.GetHeadwearDisplayName(cosmetics.CurrentHeadwear)
            : state.headwear).ToUpperInvariant();

    /// <summary>DONE button or ESC in the locker panel.</summary>
    public void CloseLockerMenuFromUi()
    {
        if (lockerMenuOpen)
        {
            CloseLockerMenu();
        }
    }

    private static int GetShirtUnlockLevel(GymShirtColor shirt)
    {
        return shirt == GymShirtColor.Black ? 1 :
            shirt == GymShirtColor.White ? 2 : shirt == GymShirtColor.Red ? 4 : 6;
    }

    private static int GetHeadwearUnlockLevel(GymHeadwear headwear)
    {
        return headwear == GymHeadwear.Cap ? 3 :
            headwear == GymHeadwear.Headband ? 5 :
            headwear == GymHeadwear.Beanie || headwear == GymHeadwear.JollyCap ? 8 : 1;
    }

    private void OnApplicationQuit()
    {
        RestoreTimeScale();
        SaveNow();
    }

    private void OnDestroy()
    {
        RestoreTimeScale();
        if (instance == this)
        {
            instance = null;
        }
    }
}
