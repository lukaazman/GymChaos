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
        GymHeadwear.Headband, GymHeadwear.Visor
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
    private float temporaryFatigue;

    private GUIStyle lockerTitleStyle;
    private GUIStyle lockerSectionStyle;
    private GUIStyle lockerBodyStyle;
    private GUIStyle lockerButtonStyle;
    private GUIStyle lockerSelectedButtonStyle;
    private GUIStyle lockerFooterStyle;
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
    public bool IsBlockingPlayerInput => lockerMenuOpen ||
        (levelChoiceVisible && !EnemyFighter.IsFightActive && !GymDialogueDirector.IsDialogueActive);
    public int Level => state != null ? state.level : 1;
    public int Experience => state != null ? state.experience : 0;
    public int ExperienceToNextLevel => GetExperienceToNextLevel(Level);
    public int SkillPoints => state != null ? state.skillPoints : 0;
    public int Reputation => state != null ? state.reputation : 0;
    public int MasteryRank => state != null ? state.masteryRank : 0;
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

        if (levelChoiceVisible && !EnemyFighter.IsFightActive &&
            !GymDialogueDirector.IsDialogueActive && !levelChoiceCursorReleased &&
            player != null)
        {
            // PlayerMovement intentionally stops consuming gameplay input while
            // this overlay is open. Release the gameplay pointer here as well,
            // otherwise IMGUI can draw clickable buttons but the locked cursor
            // never reaches them.
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
        return baseDamage * (1f + GetStatRank(GymStat.Strength) * 0.08f);
    }

    public float GetStrengthForceMultiplier()
    {
        return 1f + GetStatRank(GymStat.Strength) * 0.042f;
    }

    public float GetSprintMultiplier()
    {
        return HasEnduranceUltimate
            ? 1.72f
            : 1f + GetStatRank(GymStat.Endurance) * 0.055f;
    }

    public float GetSprintCapacity()
    {
        return 100f + GetStatRank(GymStat.Endurance) * 8f;
    }

    public float GetSprintDrainPerSecond()
    {
        return Mathf.Max(7f, 18f - GetStatRank(GymStat.Endurance) * 0.75f);
    }

    public float GetSprintRecoveryPerSecond()
    {
        return 14f + GetStatRank(GymStat.Endurance) * 2.2f;
    }

    public float GetCardioCapacityMultiplier()
    {
        return 1f + GetStatRank(GymStat.Endurance) * 0.035f;
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
            (1f + temporaryFatigue * 0.45f);
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

    public void AwardExperience(int amount, string reason, string sourceKey = null)
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

        nextSaveTime = Time.unscaledTime + 1.5f;
        if (!string.IsNullOrWhiteSpace(reason))
        {
            Debug.Log($"GYMCHAOS_XP +{amount} reason={reason} total={state.totalExperience}", this);
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
        // Show the action's applied impact, not the final reputation total.
        ShowStatImpact($"REP IMPACT   {applied:+#;-#;0}", 1.8f);
        Debug.Log(
            $"GYMCHAOS_REPUTATION_CHANGED delta={applied} value={state.reputation} reason={reason}",
            this);
        SaveNow();
        return applied;
    }

    public void RegisterWorkoutRep(
        WorkoutResult result, GymExerciseType exerciseType, int weight, int comboMultiplier = 1)
    {
        if (result == WorkoutResult.None)
        {
            return;
        }

        int baseReward = result == WorkoutResult.Perfect || result == WorkoutResult.AutoPerfect ? 8 :
            result == WorkoutResult.Good ? 5 : 2;
        int multiplier = Mathf.Clamp(comboMultiplier, 1, 8);
        int reward = baseReward * multiplier;
        string resultLabel = result == WorkoutResult.AutoPerfect ? "AUTO PERFECT" : result.ToString().ToUpperInvariant();
        UpdateTemporaryFatigue(result);
        ShowFeedback($"{resultLabel}  x{multiplier}  +{reward} XP", 1.15f);
        ShowStatImpact($"{resultLabel} IMPACT   XP +{reward}", 1.15f);
        string key = $"workout-rep-{Time.frameCount}";
        AwardExperience(reward, result.ToString(), key);
        RegisterDailyGoal(0);
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

        AwardExperience(3, "combat hit", "combat-hit-" + enemy.Identity + "-" + enemy.name);
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

        AwardExperience(5, "social action", "social-" + state.gymDay + "-" + reason);
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
        AwardExperience(6, "dialogue choice", rewardKey);
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

        AwardExperience(18, "helped visitor", rewardKey);
        ChangeReputation(4, "helped " + identity, countDailyGoal);
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

    public void UseBathroom()
    {
        if (EnemyFighter.IsFightActive)
        {
            ShowFeedback("Not while the gym is throwing hands.", 2.5f);
            return;
        }

        temporaryFatigue = 0f;
        EnemyFighter.ReleaseNonCombatTargetLocks(player);
        ShowFeedback("You take a breath, wash up and head back out.", 2.8f);
        Debug.Log("GYMCHAOS_BATHROOM_COOLDOWN_USED", this);
    }

    private void UpdateTemporaryFatigue(WorkoutResult result)
    {
        // Technique is more than a wider green slice: a trained player also
        // keeps a bad rep from snowballing into a full session shutdown.
        float techniqueRelief = 1f + GetStatRank(GymStat.Technique) * 0.08f;
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
        if (headwear == GymHeadwear.Visor)
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
        float best = maxDistance * maxDistance;
        for (int i = 0; i < interactables.Count; i++)
        {
            GymBackRoomInteractable candidate = interactables[i];
            if (candidate == null || !candidate.isActiveAndEnabled)
            {
                continue;
            }

            float distance = (candidate.transform.position - position).sqrMagnitude;
            if (distance < best)
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
            case GymBackRoomInteractionType.Bathroom:
                instance.UseBathroom();
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
        ShowFeedback("Daily goal complete: " + DailyGoalLabels[goalIndex], 4f);
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

    private void ShowFeedback(string text, float seconds)
    {
        feedbackText = text;
        feedbackUntil = Time.unscaledTime + seconds;
    }

    private void ShowStatImpact(string text, float seconds)
    {
        statImpactText = text;
        statImpactUntil = Time.unscaledTime + seconds;
    }

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

    private void SaveNow()
    {
        if (state == null)
        {
            return;
        }

        state.Normalize();
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
        if (lockerMenuOpen)
        {
            DrawLockerMenu();
        }
        else
        {
            DrawProgressHud();
            DrawDailyGoals();
        }
        if (levelChoiceVisible && !EnemyFighter.IsFightActive &&
            !GymDialogueDirector.IsDialogueActive)
        {
            DrawSkillChoiceOverlay();
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
        progressHudShadowStyle.fontSize = style.fontSize;
        progressHudShadowStyle.fontStyle = style.fontStyle;
        progressHudShadowStyle.alignment = style.alignment;
        progressHudShadowStyle.wordWrap = style.wordWrap;
        GUI.Label(new Rect(rect.x + 1f, rect.y + 1f, rect.width, rect.height), text, progressHudShadowStyle);
        GUI.Label(rect, text, style);
    }

    private void DrawSkillChoiceOverlay()
    {
        GUI.color = new Color(0f, 0f, 0f, 0.72f);
        GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);
        GUI.color = Color.white;

        float width = Mathf.Min(680f, Screen.width - 40f);
        Rect panel = new Rect((Screen.width - width) * 0.5f, Screen.height * 0.5f - 180f, width, 360f);
        GUI.color = new Color(0.035f, 0.045f, 0.09f, 0.98f);
        GUI.DrawTexture(panel, Texture2D.whiteTexture);
        GUI.color = Color.white;
        GUIStyle title = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = 28,
            fontStyle = FontStyle.Bold
        };
        title.normal.textColor = new Color(1f, 0.82f, 0.35f);
        GUI.Label(new Rect(panel.x + 20f, panel.y + 22f, panel.width - 40f, 40f),
            $"LEVEL {state.level}  |  CHOOSE A STAT", title);

        GUIStyle body = new GUIStyle(title)
        {
            fontSize = 16,
            fontStyle = FontStyle.Normal,
            wordWrap = true
        };
        body.normal.textColor = Color.white;
        GUI.Label(new Rect(panel.x + 45f, panel.y + 70f, panel.width - 90f, 44f),
            $"One point per level. Rank {MaxStatRank} unlocks the tree's ultimate. " +
            "Max all four trees to turn later points into Mastery.", body);

        string[] choices = { "1  STRENGTH", "2  ENDURANCE", "3  TECHNIQUE", "4  REPUTATION" };
        GymStat[] stats = { GymStat.Strength, GymStat.Endurance, GymStat.Technique, GymStat.Reputation };
        GUIStyle choiceStyle = CreateProgressHudStyle(15, FontStyle.Bold, TextAnchor.MiddleCenter);
        choiceStyle.normal.textColor = new Color(0.9f, 0.94f, 1f);
        choiceStyle.hover.textColor = Color.white;
        choiceStyle.active.textColor = Color.white;
        choiceStyle.focused.textColor = Color.white;
        for (int i = 0; i < choices.Length; i++)
        {
            Rect button = new Rect(panel.x + 44f + (i % 2) * (panel.width * 0.5f - 56f),
                panel.y + 136f + (i / 2) * 72f, panel.width * 0.5f - 66f, 54f);
            if (DrawSkillChoiceButton(button, choices[i], choiceStyle))
            {
                AllocateSkill(stats[i]);
            }
        }

        GUIStyle footer = new GUIStyle(body)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = 14
        };
        footer.normal.textColor = new Color(0.66f, 0.72f, 0.85f);
        GUI.Label(new Rect(panel.x + 20f, panel.y + 302f, panel.width - 40f, 28f),
            EnemyFighter.IsFightActive ? "Move away from the fight to spend the point." : "Press 1, 2, 3 or 4", footer);

        Event current = Event.current;
        if (current.type == EventType.KeyDown && !EnemyFighter.IsFightActive)
        {
            GymStat stat;
            bool hasStat = true;
            switch (current.keyCode)
            {
                case KeyCode.Alpha1:
                case KeyCode.Keypad1:
                    stat = GymStat.Strength;
                    break;
                case KeyCode.Alpha2:
                case KeyCode.Keypad2:
                    stat = GymStat.Endurance;
                    break;
                case KeyCode.Alpha3:
                case KeyCode.Keypad3:
                    stat = GymStat.Technique;
                    break;
                case KeyCode.Alpha4:
                case KeyCode.Keypad4:
                    stat = GymStat.Reputation;
                    break;
                default:
                    stat = GymStat.Strength;
                    hasStat = false;
                    break;
            }

            if (hasStat)
            {
                AllocateSkill(stat);
                current.Use();
            }
        }
    }

    private static bool DrawSkillChoiceButton(
        Rect rect, string label, GUIStyle style)
    {
        bool hovered = rect.Contains(Event.current.mousePosition);
        DrawProgressHudRect(
            rect,
            hovered
                ? new Color(0.11f, 0.16f, 0.23f, 0.98f)
                : new Color(0.055f, 0.08f, 0.12f, 0.98f));
        DrawProgressHudRect(
            new Rect(rect.x, rect.y, 3f, rect.height),
            hovered
                ? new Color(1f, 0.82f, 0.35f, 1f)
                : new Color(0.48f, 0.86f, 1f, 0.78f));
        return GUI.Button(rect, label, style);
    }
    private void EnsureLockerStyles()
    {
        if (lockerTitleStyle != null)
        {
            return;
        }

        lockerTitleStyle = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleLeft,
            fontSize = 25,
            fontStyle = FontStyle.Bold
        };
        lockerTitleStyle.normal.textColor = new Color(1f, 0.82f, 0.35f);

        lockerSectionStyle = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleLeft,
            fontSize = 12,
            fontStyle = FontStyle.Bold
        };
        lockerSectionStyle.normal.textColor = new Color(0.48f, 0.72f, 1f);

        lockerBodyStyle = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleLeft,
            fontSize = 13,
            wordWrap = true
        };
        lockerBodyStyle.normal.textColor = new Color(0.82f, 0.87f, 0.96f);

        lockerButtonStyle = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = 12,
            wordWrap = true,
            padding = new RectOffset(6, 6, 4, 4)
        };
        lockerButtonStyle.normal.textColor = new Color(0.88f, 0.92f, 1f);
        lockerButtonStyle.hover.textColor = Color.white;
        lockerButtonStyle.active.textColor = Color.white;
        lockerButtonStyle.normal.background = null;
        lockerButtonStyle.hover.background = null;
        lockerButtonStyle.active.background = null;
        lockerButtonStyle.focused.background = null;

        lockerSelectedButtonStyle = new GUIStyle(lockerButtonStyle);
        lockerSelectedButtonStyle.normal.textColor = new Color(1f, 0.82f, 0.35f);
        lockerSelectedButtonStyle.hover.textColor = Color.white;

        lockerFooterStyle = new GUIStyle(lockerBodyStyle)
        {
            alignment = TextAnchor.MiddleLeft,
            fontSize = 12
        };
        lockerFooterStyle.normal.textColor = new Color(0.68f, 0.78f, 0.93f);
    }

    private static bool DrawLockerButton(
        Rect rect, string label, GUIStyle style, bool selected)
    {
        DrawProgressHudRect(
            rect,
            selected
                ? new Color(0.11f, 0.075f, 0.04f, 0.98f)
                : new Color(0.055f, 0.08f, 0.12f, 0.98f));
        DrawProgressHudRect(
            new Rect(rect.x, rect.y, 2f, rect.height),
            selected
                ? new Color(1f, 0.82f, 0.35f, 1f)
                : new Color(0.48f, 0.86f, 1f, 0.72f));
        return GUI.Button(rect, label, style);
    }
    private void DrawLockerMenu()
    {
        EnsureLockerStyles();

        float width = Mathf.Min(500f, Screen.width - 28f);
        float panelHeight = Mathf.Min(448f, Screen.height - 24f);
        Rect panel = new Rect(
            14f,
            Mathf.Max(12f, Screen.height * 0.5f - panelHeight * 0.5f),
            width,
            panelHeight);

        Color previousColor = GUI.color;
        GUI.color = new Color(0.018f, 0.025f, 0.055f, 0.96f);
        GUI.DrawTexture(panel, Texture2D.whiteTexture);
        GUI.color = new Color(0.95f, 0.2f, 0.08f, 0.9f);
        GUI.DrawTexture(new Rect(panel.x, panel.y, 4f, panel.height), Texture2D.whiteTexture);
        GUI.color = previousColor;

        const float left = 24f;
        const float gap = 8f;
        const int columns = 3;
        float buttonWidth = (panel.width - left * 2f - gap * (columns - 1)) / columns;
        float buttonTop = panel.y + 112f;
        float buttonHeight = 38f;

        GUI.Label(new Rect(panel.x + left, panel.y + 16f, panel.width - left * 2f, 34f),
            "OUTFIT", lockerTitleStyle);
        GUI.Label(new Rect(panel.x + left, panel.y + 50f, panel.width - left * 2f, 24f),
            $"Level {state.level}  ·  Mastery {state.masteryRank}", lockerBodyStyle);

        GUI.Label(new Rect(panel.x + left, panel.y + 82f, panel.width - left * 2f, 22f),
            "SHIRT", lockerSectionStyle);
        for (int i = 0; i < LockerShirtOptions.Length; i++)
        {
            GymShirtColor shirt = LockerShirtOptions[i];
            bool selected = string.Equals(state.shirt, shirt.ToString(), StringComparison.OrdinalIgnoreCase);
            bool unlocked = IsCosmeticUnlocked(shirt);
            string label = selected ? $"✓ {shirt}" : unlocked ? shirt.ToString() :
                shirt == GymShirtColor.Gold
                    ? $"{shirt} · M1"
                    : $"{shirt} · L{GetShirtUnlockLevel(shirt)}";
            int column = i % columns;
            int row = i / columns;
            Rect buttonRect = new Rect(
                panel.x + left + column * (buttonWidth + gap),
                buttonTop + row * (buttonHeight + gap),
                buttonWidth,
                buttonHeight);
            if (DrawLockerButton(buttonRect, label,
                    selected ? lockerSelectedButtonStyle : lockerButtonStyle, selected))
            {
                EquipShirt(shirt);
            }
        }

        GUI.Label(new Rect(panel.x + left, panel.y + 212f, panel.width - left * 2f, 22f),
            "HEADWEAR", lockerSectionStyle);
        float headwearTop = panel.y + 242f;
        for (int i = 0; i < LockerHeadwearOptions.Length; i++)
        {
            GymHeadwear item = LockerHeadwearOptions[i];
            bool selected = string.Equals(state.headwear, item.ToString(), StringComparison.OrdinalIgnoreCase);
            bool unlocked = IsCosmeticUnlocked(item);
            string label = selected ? $"✓ {item}" : unlocked ? item.ToString() :
                item == GymHeadwear.Visor
                    ? $"{item} · M2"
                    : $"{item} · L{GetHeadwearUnlockLevel(item)}";
            int column = i % columns;
            int row = i / columns;
            Rect buttonRect = new Rect(
                panel.x + left + column * (buttonWidth + gap),
                headwearTop + row * (buttonHeight + gap),
                buttonWidth,
                buttonHeight);
            if (DrawLockerButton(buttonRect, label,
                    selected ? lockerSelectedButtonStyle : lockerButtonStyle, selected))
            {
                EquipHeadwear(item);
            }
        }

        GUI.Label(new Rect(panel.x + left, panel.y + 350f, panel.width - left * 2f, 22f),
            $"Wearing  {state.shirt}  ·  {state.headwear}", lockerFooterStyle);
        GUI.Label(new Rect(panel.x + left, panel.y + 372f, panel.width - left * 2f, 22f),
            MasteryChallengeLabel, lockerFooterStyle);

        Rect doneRect = new Rect(panel.x + panel.width - 138f, panel.y + 402f, 114f, 32f);
        if (DrawLockerButton(doneRect, "DONE  [ESC]", lockerButtonStyle, false))
        {
            CloseLockerMenu();
        }

        Event current = Event.current;
        if (current.type == EventType.KeyDown && current.keyCode == KeyCode.Escape)
        {
            CloseLockerMenu();
            current.Use();
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
            headwear == GymHeadwear.Headband ? 5 : headwear == GymHeadwear.Beanie ? 8 : 1;
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
