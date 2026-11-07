using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

public enum GymSaveStatus { Idle, Saving, Saved, Failed }

/// <summary>
/// Owns the active character: which slot and class are in play, when saves are
/// requested and how a save is applied to the generated world. Gameplay state
/// is captured on the main thread; only the serialized text reaches the writer.
/// </summary>
[DefaultExecutionOrder(-200)]
public sealed class GymSessionService : MonoBehaviour
{
    // Single tuning point for the timed autosave (active gameplay seconds).
    public const float AutosaveIntervalSeconds = 180f;
    public const string VerificationDirectoryVariable = "GYMCHAOS_SESSION_VERIFY_DIR";
    private const float RequestDebounceSeconds = 1.5f;
    private const float SafeSpawnSampleSeconds = 0.5f;
    private const string LegacyProgressionKey = "GymChaos.Progression.v1";
    private const string LegacyMigratedKey = "GymChaos.LegacyMigrated.v1";

    private static GymSessionService instance;
    private static GymSaveRepository repository;
    private static GymCharacterSave active;
    private static bool activeIsNew;
    private static bool returningToMenu;
    // Slot whose scene state was applied; a quit before that must not write the unloaded world.
    private static string appliedSlot;
    /// <summary>True from return-to-menu until the reloaded scene is up.</summary>
    public static bool IsReturningToMenu => returningToMenu;

    /// <summary>Marks a scene reload that does not go through ReturnToMenu (WebGL exit).</summary>
    public static void MarkSceneReload() => returningToMenu = true;

    private Task writeTask;
    // Sections changed since they were last written, and the ones in flight.
    private GymSaveSections dirtySections;
    private GymSaveSections writingSections;
    private bool requestPending;
    private float requestDueAt;
    private float autosaveElapsed;
    private float safeSpawnElapsed;
    private Vector3 safeSpawn;
    private float safeFacing;
    private bool hasSafeSpawn;
    // Callbacks wait for a write that started after they were requested.
    private readonly List<Action<bool, string>> queuedCallbacks = new List<Action<bool, string>>();
    private readonly List<Action<bool, string>> writingCallbacks = new List<Action<bool, string>>();

    public static event Action<GymSaveStatus, string> SaveStatusChanged;

    public static GymSaveStatus Status { get; private set; }
    public static string StatusMessage { get; private set; } = string.Empty;
    public static int CompletedWriteCount { get; private set; }
    /// <summary>Which sections of the character file this session may load and write.</summary>
    public static GymSaveContext Context { get; private set; }
    public static GymSaveSections AllowedSections => GymSaveRules.Allowed(Context);
    public static GymCharacterSave Active => active;
    public static bool HasActive => active != null;
    public static bool IsWriting => instance != null && instance.writeTask != null;
    public static float AutosaveElapsedSeconds => instance != null ? instance.autosaveElapsed : 0f;

    /// <summary>Character slots replace the single PlayerPrefs save outside headless verifiers.</summary>
    public static bool SessionMode =>
        !Application.isBatchMode || !string.IsNullOrEmpty(VerificationDirectory);

    public static string VerificationDirectory =>
        Environment.GetEnvironmentVariable(VerificationDirectoryVariable);

    public static GymClassDefinition ActiveClass =>
        active != null && GymClassCatalog.TryGet(active.classId, out GymClassDefinition definition)
            ? definition
            : GymClassCatalog.Baseline;

    public static GymSaveRepository Repository
    {
        get
        {
            if (repository == null)
            {
                string overrideDirectory = VerificationDirectory;
                string directory = string.IsNullOrEmpty(overrideDirectory)
                    ? Path.Combine(Application.persistentDataPath, "Characters")
                    : overrideDirectory;
                var ids = new List<string>();
                foreach (GymClassDefinition definition in GymClassCatalog.All) ids.Add(definition.id);
                repository = new GymSaveRepository(directory, ids);
            }
            return repository;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        instance = null;
        repository = null;
        active = null;
        activeIsNew = false;
        returningToMenu = false;
        Context = GymSaveContext.Solo;
        appliedSlot = null;
        Status = GymSaveStatus.Idle;
        StatusMessage = string.Empty;
        CompletedWriteCount = 0;
        SaveStatusChanged = null;
    }

    /// <summary>Solo and Host play their own world; Join plays in the host world.</summary>
    public static void SetContext(GymSaveContext context) => Context = context;

    /// <summary>The sections one write captures: dirty and allowed in the context. Autosave and manual save share it.</summary>
    public static GymSaveSections SectionsToWrite(GymSaveContext context, GymSaveSections dirty) =>
        dirty & GymSaveRules.Allowed(context);

    private static GymSessionService EnsureRunner()
    {
        if (instance != null) return instance;
        GameObject runner = new GameObject("Gym Session Service");
        DontDestroyOnLoad(runner);
        instance = runner.AddComponent<GymSessionService>();
        return instance;
    }

    /// <summary>Valid characters, newest first. Corrupt or incompatible files are excluded.</summary>
    public static List<GymSaveListEntry> ListValidSaves(out int unreadable)
    {
        MigrateLegacyProgression();
        var valid = new List<GymSaveListEntry>();
        unreadable = 0;
        foreach (GymSaveListEntry entry in Repository.List())
        {
            if (entry.Result.Success) valid.Add(entry);
            else if (entry.Result.Failure != GymSaveFailure.Missing) unreadable++;
        }
        return valid;
    }

    /// <summary>
    /// One-time import of the pre-slot PlayerPrefs progression as a Bodybuilding
    /// character. The PlayerPrefs value is left in place.
    /// </summary>
    public static void MigrateLegacyProgression()
    {
        if (!SessionMode || PlayerPrefs.GetInt(LegacyMigratedKey, 0) != 0) return;
        string json = PlayerPrefs.GetString(LegacyProgressionKey, string.Empty);
        if (!string.IsNullOrWhiteSpace(json))
        {
            try
            {
                GymProgressionState legacy = JsonUtility.FromJson<GymProgressionState>(json);
                if (legacy != null)
                {
                    GymCharacterSave migrated = GymCharacterSave.New(GymClassCatalog.BaselineId);
                    migrated.progression = legacy;
                    Repository.Write(migrated.slotId, Repository.Serialize(migrated));
                    Debug.Log("GYMCHAOS_LEGACY_SAVE_MIGRATED class=" + migrated.classId);
                }
            }
            catch (Exception exception) when (exception is IOException ||
                exception is UnauthorizedAccessException || exception is InvalidDataException ||
                exception is ArgumentException)
            {
                // Retry on the next listing instead of marking a failed import done.
                Debug.LogWarning("Legacy progression could not be migrated: " + exception.Message);
                return;
            }
        }
        PlayerPrefs.SetInt(LegacyMigratedKey, 1);
        PlayerPrefs.Save();
    }

    /// <summary>Starts a separate character. Nothing is written until the world is ready.</summary>
    public static GymCharacterSave BeginNew(string confirmedClassId)
    {
        active = GymCharacterSave.New(confirmedClassId);
        activeIsNew = true;
        Context = GymNetSession.LaunchContext();
        EnsureRunner().ResetTimers();
        return active;
    }

    public static bool BeginLoad(string slotId, out string error)
    {
        GymSaveReadResult result = Repository.Read(slotId);
        if (!result.Success)
        {
            error = string.IsNullOrEmpty(result.Message) ? "The character could not be loaded." : result.Message;
            return false;
        }
        active = result.Data;
        activeIsNew = false;
        Context = GymNetSession.LaunchContext();
        EnsureRunner().ResetTimers();
        error = string.Empty;
        return true;
    }

    /// <summary>
    /// Restoration order: appearance and derived stats, progression and world
    /// flags, safe spawn, then resources clamped against the derived maximum.
    /// The caller enables input and binds the HUD afterwards.
    /// </summary>
    public static void ApplyToWorld(PlayerMovement player)
    {
        if (active == null) return;
        GymSessionService runner = EnsureRunner();
        GymClassDefinition definition = ActiveClass;
        if (player != null) player.ApplyClassAppearance(definition);

        GymExperienceService progression = GymExperienceService.Active;
        if (progression != null)
        {
            // A reload on the same calendar day continues that gym day, so daily
            // goals and day-keyed rewards cannot be farmed by saving and loading.
            progression.ApplySession(active.progression, active.oneShotRewards,
                !activeIsNew && ShouldStartNewDay(active.savedUtc, DateTime.Now));
        }

        // The world section loads only in a context that owns a world, and only
        // once a Solo or Host session has written one.
        bool ownsWorld = (AllowedSections & GymSaveSections.World) != 0;
        bool restoreWorld = ownsWorld && !activeIsNew && active.hasWorld;
        if (ownsWorld)
        {
            GlassShatterPanel.ResetShatteredRegistry();
            if (restoreWorld)
            {
                if (GymTimeOfDay.Instance != null)
                {
                    GymTimeOfDay.Instance.RestoreSession(active.worldTime, active.worldDay);
                }
                RestoreBrokenPanels(active.brokenPanels);
                GymFixerDirector.RestoreIssueAges(active.fixerIssueIds, active.fixerIssueSince);
            }
            else
            {
                GymFixerDirector.RestoreIssueAges(null, null);
            }
        }

        if (player != null)
        {
            bool spawnUsable = restoreWorld && active.hasSpawn && IsSafeSpawn(active.spawn);
            float capacity = progression != null ? progression.GetSprintCapacity() : 100f;
            player.RestoreSessionState(
                spawnUsable, active.spawn, active.facing,
                activeIsNew ? player.MaxHealth : active.health,
                activeIsNew ? capacity : active.stamina);
            runner.safeSpawn = player.transform.position;
            runner.safeFacing = player.transform.eulerAngles.y;
            runner.hasSafeSpawn = true;
        }

        appliedSlot = active.slotId;
        Debug.Log($"GYMCHAOS_SESSION_APPLIED slot={active.slotId} class={active.classId} new={activeIsNew}");
        if (activeIsNew)
        {
            // The fresh character exists on disk as soon as it stands in the gym.
            activeIsNew = false;
            RequestSave("initial", true);
        }
    }

    /// <summary>A loaded character starts a new gym day only when the real date has moved on.</summary>
    public static bool ShouldStartNewDay(string savedUtc, DateTime localNow)
    {
        return DateTime.TryParse(savedUtc, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.RoundtripKind, out DateTime saved) &&
            saved.ToLocalTime().Date < localNow.Date;
    }

    /// <summary>Drops a character that could not be applied, leaving the menu usable.</summary>
    public static void AbandonSession()
    {
        active = null;
        activeIsNew = false;
        if (instance != null) instance.ResetTimers();
    }

    /// <summary>Progress changed; include it in the next save without forcing one.</summary>
    public static void MarkDirty()
    {
        if (instance != null && active != null) instance.dirtySections |= GymSaveSections.Character;
    }

    public static void RequestSave(string reason, bool immediate = false, Action<bool, string> onCompleted = null)
    {
        if (active == null || instance == null)
        {
            onCompleted?.Invoke(false, "No character is active.");
            return;
        }
        // Every section this context may write is saved; manual and timed saves alike.
        instance.dirtySections |= AllowedSections;
        if (onCompleted != null) instance.queuedCallbacks.Add(onCompleted);
        float due = Time.unscaledTime + (immediate ? 0f : RequestDebounceSeconds);
        instance.requestDueAt = instance.requestPending ? Mathf.Min(instance.requestDueAt, due) : due;
        instance.requestPending = true;
        if (immediate) instance.TryStartWrite();
    }

    /// <summary>Drops the session and reloads the scene back to the boot menu.</summary>
    /// <summary>Set by a player death: the reloaded menu opens straight on the save list.</summary>
    public static bool OpenLoadListOnNextMenu { get; set; }

    /// <summary>
    /// After the player dies: reload the scene into the Load Game list so a
    /// save can be picked to continue from. The dead state is never saved.
    /// </summary>
    public static void ReturnToLoadList()
    {
        OpenLoadListOnNextMenu = true;
        // A host that dies keeps its room code for when it loads its save again.
        ReturnToMenu(GymNetSession.RememberRoomForRecovery());
    }

    /// <summary>
    /// Re-reads the active character from its own save file into the running
    /// game, without a scene reload: progress since the last save is gone, the
    /// world section is not touched. False when the save cannot be read.
    /// </summary>
    public static bool ReloadCharacterInPlace()
    {
        if (active == null) return false;
        GymSaveReadResult result = Repository.Read(active.slotId);
        if (!result.Success) return false;
        active.progression = result.Data.progression;
        active.oneShotRewards = result.Data.oneShotRewards;
        GymExperienceService.Active?.ApplySession(active.progression, active.oneShotRewards, false);
        return true;
    }

    /// <summary>Back to the boot menu. The room (host recovery) is dropped unless the caller keeps it.</summary>
    public static void ReturnToMenu(bool keepRoom = false)
    {
        if (returningToMenu) return;
        if (!keepRoom) GymNetSession.ClearRecovery();
        appliedSlot = null;
        returningToMenu = true;
        active = null;
        activeIsNew = false;
        if (instance != null)
        {
            instance.ResetTimers();
            instance.queuedCallbacks.Clear();
            instance.writingCallbacks.Clear();
        }
        SetStatus(GymSaveStatus.Idle, string.Empty);
        Time.timeScale = 1f;
        GlassShatterPanel.ResetShatteredRegistry();
        GymNetSession.End();
        Debug.Log("GYMCHAOS_SESSION_RETURN_TO_MENU");
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    private void ResetTimers()
    {
        dirtySections = GymSaveSections.None;
        requestPending = false;
        autosaveElapsed = 0f;
        safeSpawnElapsed = 0f;
        hasSafeSpawn = false;
    }

    private void OnEnable()
    {
        SceneManager.sceneLoaded += HandleSceneLoaded;
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;
    }

    private static void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        returningToMenu = false;
    }

    private static bool IsActiveGameplay()
    {
        return active != null && !returningToMenu && GymArenaBootstrap.IsGameplayStarted &&
            !GymStartScreen.IsMenuVisible && !GymPauseMenu.IsVisible && Time.timeScale > 0f;
    }

    private void Update()
    {
        PollWrite();
        if (active == null) return;

        if (IsActiveGameplay())
        {
            float delta = Time.unscaledDeltaTime;
            active.playSeconds += delta;
            autosaveElapsed += delta;
            safeSpawnElapsed += delta;
            if (safeSpawnElapsed >= SafeSpawnSampleSeconds)
            {
                safeSpawnElapsed = 0f;
                SampleSafeSpawn();
            }
            if (autosaveElapsed >= AutosaveIntervalSeconds)
            {
                autosaveElapsed = 0f;
                RequestSave("autosave", true);
            }
        }

        if (requestPending && Time.unscaledTime >= requestDueAt) TryStartWrite();
    }

    private void SampleSafeSpawn()
    {
        GymExperienceService progression = GymExperienceService.Active;
        PlayerMovement player = progression != null ? progression.Player : null;
        if (player == null || player.IsDead || player.IsExercising || player.IsCinematicLocked ||
            !player.IsGroundedForSession)
        {
            return;
        }
        safeSpawn = player.transform.position;
        safeFacing = player.transform.eulerAngles.y;
        hasSafeSpawn = true;
    }

    private void TryStartWrite()
    {
        if (writeTask != null || active == null || !requestPending) return;
        requestPending = false;
        writingCallbacks.AddRange(queuedCallbacks);
        queuedCallbacks.Clear();
        string serialized;
        string slot = active.slotId;
        writingSections = SectionsToWrite(Context, dirtySections);
        try
        {
            Capture(active, writingSections);
            serialized = Repository.Serialize(active);
        }
        catch (Exception exception)
        {
            // Any capture failure must still release the waiting callbacks
            // (Save & Return would otherwise stay locked).
            Debug.LogException(exception);
            FinishWrite(false, "Save failed: " + exception.Message);
            return;
        }

        dirtySections &= ~writingSections;
        autosaveElapsed = 0f;
        SetStatus(GymSaveStatus.Saving, "SAVING");
        GymSaveRepository target = Repository;
        if (Application.platform == RuntimePlatform.WebGLPlayer)
        {
            // WebGL has no worker threads; the write is small and synchronous.
            try
            {
                target.Write(slot, serialized);
                FinishWrite(true, "SAVED");
            }
            catch (Exception exception) when (IsWriteFailure(exception))
            {
                FinishWrite(false, "Save failed: " + exception.Message);
            }
            return;
        }
        writeTask = Task.Run(() => target.Write(slot, serialized));
    }

    private void PollWrite()
    {
        if (writeTask == null || !writeTask.IsCompleted) return;
        Task finished = writeTask;
        writeTask = null;
        if (finished.IsFaulted)
        {
            Exception cause = finished.Exception != null ? finished.Exception.GetBaseException() : null;
            FinishWrite(false, "Save failed: " + (cause != null ? cause.Message : "unknown error"));
        }
        else
        {
            FinishWrite(true, "SAVED");
        }
    }

    private void FinishWrite(bool succeeded, string message)
    {
        if (succeeded)
        {
            CompletedWriteCount++;
        }
        else
        {
            // Keep the unsaved progress flagged so a retry or the next autosave writes it.
            dirtySections |= writingSections;
            Debug.LogWarning("GYMCHAOS_SAVE_NOT_WRITTEN " + message);
        }
        SetStatus(succeeded ? GymSaveStatus.Saved : GymSaveStatus.Failed, message);
        if (writingCallbacks.Count > 0)
        {
            var callbacks = writingCallbacks.ToArray();
            writingCallbacks.Clear();
            foreach (Action<bool, string> callback in callbacks) callback(succeeded, message);
        }
    }

    private static bool IsWriteFailure(Exception exception)
    {
        return exception is IOException || exception is UnauthorizedAccessException ||
            exception is InvalidDataException || exception is ArgumentException;
    }

    private static void SetStatus(GymSaveStatus status, string message)
    {
        Status = status;
        StatusMessage = message ?? string.Empty;
        SaveStatusChanged?.Invoke(status, StatusMessage);
    }

    /// <summary>Copies authoritative gameplay state into the active save (main thread only).</summary>
    private void Capture(GymCharacterSave target, GymSaveSections sections)
    {
        // Scene state lands in a scratch copy first; only the requested
        // sections are merged into the save, so an unloaded world is never written.
        GymCharacterSave live = new GymCharacterSave
        {
            progression = target.progression, oneShotRewards = target.oneShotRewards,
            health = target.health, stamina = target.stamina, worldTime = target.worldTime,
            worldDay = target.worldDay, hasSpawn = target.hasSpawn, spawn = target.spawn,
            facing = target.facing, brokenPanels = target.brokenPanels,
            fixerIssueIds = target.fixerIssueIds, fixerIssueSince = target.fixerIssueSince
        };
        bool character = (sections & GymSaveSections.Character) != 0;
        bool world = (sections & GymSaveSections.World) != 0;
        GymExperienceService progression = GymExperienceService.Active;
        if (character && progression != null && progression.State != null)
        {
            progression.State.Normalize();
            // Detach from the live object so later gameplay cannot alter the snapshot.
            live.progression = JsonUtility.FromJson<GymProgressionState>(
                JsonUtility.ToJson(progression.State));
            live.oneShotRewards = progression.CaptureOneShotRewards();
        }

        PlayerMovement player = progression != null ? progression.Player : null;
        if (player != null)
        {
            if (player.IsDead)
            {
                // A defeated character resumes rested at the entrance.
                live.hasSpawn = false;
                live.health = player.MaxHealth;
                live.stamina = progression != null ? progression.GetSprintCapacity() : 100f;
            }
            else
            {
                SampleSafeSpawn();
                live.hasSpawn = hasSafeSpawn;
                live.spawn = safeSpawn;
                live.facing = safeFacing;
                live.health = player.CurrentHealth;
                live.stamina = player.SprintEnergy;
            }
        }

        if (world)
        {
            if (GymTimeOfDay.Instance != null)
            {
                live.worldTime = GymTimeOfDay.Instance.Time01;
                live.worldDay = GymTimeOfDay.Instance.CurrentDay;
            }
            live.brokenPanels = GlassShatterPanel.CaptureShatteredIds();
            GymFixerDirector.CaptureIssueAges(out live.fixerIssueIds, out live.fixerIssueSince);
        }
        GymSaveRules.Merge(target, live, sections);
        target.savedUtc = DateTime.UtcNow.ToString("O");
    }

    /// <summary>The host's world as a guest needs it to join: clock, broken glass, fixer ages.</summary>
    public static GymNetWorldSnapshot CaptureWorldSnapshot()
    {
        var world = new GymNetWorldSnapshot { brokenPanels = GlassShatterPanel.CaptureShatteredIds() };
        if (GymTimeOfDay.Instance != null)
        {
            world.worldTime = GymTimeOfDay.Instance.Time01;
            world.worldDay = GymTimeOfDay.Instance.CurrentDay;
        }
        GymFixerDirector.CaptureIssueAges(out world.fixerIssueIds, out world.fixerIssueSince);
        return world;
    }

    /// <summary>Applies a host's world to the scene only; no save is touched. False while the scene is not ready.</summary>
    public static bool ApplyWorldSnapshot(GymNetWorldSnapshot world)
    {
        if (world == null || GymTimeOfDay.Instance == null) return false;
        GymTimeOfDay.Instance.RestoreSession(world.worldTime, world.worldDay);
        GlassShatterPanel.ResetShatteredRegistry();
        // Glass the host has repaired since (a repair message may have been missed): grow it back.
        var broken = new HashSet<string>(world.brokenPanels ?? Array.Empty<string>(), StringComparer.Ordinal);
        foreach (GlassShatterPanel panel in FindObjectsByType<GlassShatterPanel>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (panel != null && panel.IsShattered && !broken.Contains(panel.StableId)) panel.ApplyNetworkRepair();
        }
        RestoreBrokenPanels(world.brokenPanels);
        GymFixerDirector.RestoreIssueAges(world.fixerIssueIds, world.fixerIssueSince);
        return true;
    }

    /// <summary>The live character section, for the host. Null without an active character.</summary>
    public static GymCharacterSave CaptureCharacterForNet()
    {
        if (instance == null || active == null) return null;
        // A copy: the net path must not touch the save's revision or timestamps.
        GymCharacterSave copy = JsonUtility.FromJson<GymCharacterSave>(JsonUtility.ToJson(active));
        instance.Capture(copy, GymSaveSections.Character);
        return copy;
    }

    private static void RestoreBrokenPanels(string[] ids)
    {
        if (ids == null || ids.Length == 0) return;
        var wanted = new HashSet<string>(ids, StringComparer.Ordinal);
        foreach (GlassShatterPanel panel in FindObjectsByType<GlassShatterPanel>(
            FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (panel != null && wanted.Contains(panel.StableId)) panel.RestoreShattered();
        }
    }

    private static bool IsSafeSpawn(Vector3 position)
    {
        if (float.IsNaN(position.x) || float.IsNaN(position.y) || float.IsNaN(position.z)) return false;
        // Require walkable floor close below the saved root and no fall into the void.
        return Physics.Raycast(position + Vector3.up * 0.5f, Vector3.down, 6f,
            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
    }

    private void OnApplicationQuit()
    {
        // Best effort only: the timed and event saves are the real protection.
        if (active == null || appliedSlot != active.slotId) return;
        try
        {
            if (writeTask != null) writeTask.Wait(TimeSpan.FromSeconds(3));
        }
        catch (AggregateException)
        {
        }
        writeTask = null;
        // Always write on quit: position and playtime change without marking progress dirty.
        try
        {
            Capture(active, AllowedSections);
            Repository.Write(active.slotId, Repository.Serialize(active));
        }
        catch (Exception exception) when (IsWriteFailure(exception))
        {
            Debug.LogWarning("GYMCHAOS_SAVE_NOT_WRITTEN quit " + exception.Message);
        }
    }

#if UNITY_EDITOR
    public static void AdvanceAutosaveForVerification(float seconds)
    {
        if (instance != null) instance.autosaveElapsed += seconds;
    }

    public static bool HasPendingRequestForVerification => instance != null && instance.requestPending;
    public static int SaveStatusListenerCountForVerification =>
        SaveStatusChanged != null ? SaveStatusChanged.GetInvocationList().Length : 0;
#endif
}
