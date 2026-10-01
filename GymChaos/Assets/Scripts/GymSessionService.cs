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

    private Task writeTask;
    private bool dirty;
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
        Status = GymSaveStatus.Idle;
        StatusMessage = string.Empty;
        CompletedWriteCount = 0;
        SaveStatusChanged = null;
    }

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

        GlassShatterPanel.ResetShatteredRegistry();
        if (!activeIsNew)
        {
            if (GymTimeOfDay.Instance != null)
            {
                GymTimeOfDay.Instance.RestoreSession(active.worldTime, active.worldDay);
            }
            RestoreBrokenPanels(active.brokenPanels);
        }

        if (player != null)
        {
            bool spawnUsable = !activeIsNew && active.hasSpawn && IsSafeSpawn(active.spawn);
            float capacity = progression != null ? progression.GetSprintCapacity() : 100f;
            player.RestoreSessionState(
                spawnUsable, active.spawn, active.facing,
                activeIsNew ? player.MaxHealth : active.health,
                activeIsNew ? capacity : active.stamina);
            runner.safeSpawn = player.transform.position;
            runner.safeFacing = player.transform.eulerAngles.y;
            runner.hasSafeSpawn = true;
        }

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
        if (instance != null && active != null) instance.dirty = true;
    }

    public static void RequestSave(string reason, bool immediate = false, Action<bool, string> onCompleted = null)
    {
        if (active == null || instance == null)
        {
            onCompleted?.Invoke(false, "No character is active.");
            return;
        }
        instance.dirty = true;
        if (onCompleted != null) instance.queuedCallbacks.Add(onCompleted);
        float due = Time.unscaledTime + (immediate ? 0f : RequestDebounceSeconds);
        instance.requestDueAt = instance.requestPending ? Mathf.Min(instance.requestDueAt, due) : due;
        instance.requestPending = true;
        if (immediate) instance.TryStartWrite();
    }

    /// <summary>Drops the session and reloads the scene back to the boot menu.</summary>
    public static void ReturnToMenu()
    {
        if (returningToMenu) return;
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
        Debug.Log("GYMCHAOS_SESSION_RETURN_TO_MENU");
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    private void ResetTimers()
    {
        dirty = false;
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
        try
        {
            Capture();
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

        dirty = false;
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
            dirty = true;
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
    private void Capture()
    {
        GymExperienceService progression = GymExperienceService.Active;
        if (progression != null && progression.State != null)
        {
            progression.State.Normalize();
            // Detach from the live object so later gameplay cannot alter the snapshot.
            active.progression = JsonUtility.FromJson<GymProgressionState>(
                JsonUtility.ToJson(progression.State));
            active.oneShotRewards = progression.CaptureOneShotRewards();
        }

        PlayerMovement player = progression != null ? progression.Player : null;
        if (player != null)
        {
            if (player.IsDead)
            {
                // A defeated character resumes rested at the entrance.
                active.hasSpawn = false;
                active.health = player.MaxHealth;
                active.stamina = progression != null ? progression.GetSprintCapacity() : 100f;
            }
            else
            {
                SampleSafeSpawn();
                active.hasSpawn = hasSafeSpawn;
                active.spawn = safeSpawn;
                active.facing = safeFacing;
                active.health = player.CurrentHealth;
                active.stamina = player.SprintEnergy;
            }
        }

        if (GymTimeOfDay.Instance != null)
        {
            active.worldTime = GymTimeOfDay.Instance.Time01;
            active.worldDay = GymTimeOfDay.Instance.CurrentDay;
        }
        active.brokenPanels = GlassShatterPanel.CaptureShatteredIds();
        active.savedUtc = DateTime.UtcNow.ToString("O");
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
        if (active == null) return;
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
            Capture();
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
