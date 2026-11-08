using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Play-mode half of the class/session verifier. It opts into the real boot
/// menu through GYMCHAOS_SESSION_VERIFY_DIR, which also redirects every save
/// to a temporary directory, and drives the same entry points as the buttons.
/// </summary>
[InitializeOnLoad]
public static class GymChaosClassSessionPlay
{
    private const string ScenarioKey = "GymChaos.ClassSession.Scenario";
    private const string DirectoryKey = "GymChaos.ClassSession.Directory";
    private const string LegacyPresentKey = "GymChaos.ClassSession.LegacyPresent";
    private const string LegacyValueKey = "GymChaos.ClassSession.LegacyValue";
    private const string MigratedValueKey = "GymChaos.ClassSession.MigratedValue";
    private const string LegacyProgressionKey = "GymChaos.Progression.v1";
    private const string LegacyMigratedKey = "GymChaos.LegacyMigrated.v1";
    private static readonly string[] ClassIds =
        { "bodybuilding", "calisthenics", "cardio", "powerlifting", "strongman" };

    private static IEnumerator<float> routine;
    private static double resumeAt;
    private static double started;
    private static int resultCode = 1;
    private static bool capture;
    // Return-to-menu reloads the scene; a visitor disabled by that reload is not
    // "disabled inside the gym" and must not log that error.
    private static int reloadDisableErrors;

    static GymChaosClassSessionPlay()
    {
        if (!string.IsNullOrEmpty(SessionState.GetString(ScenarioKey, string.Empty))) Hook();
    }

    public static void Begin(string scenario)
    {
        string directory = Path.Combine(Path.GetTempPath(), "GymChaosSession-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        SessionState.SetString(ScenarioKey, scenario);
        SessionState.SetString(DirectoryKey, directory);
        // The legacy PlayerPrefs save belongs to the user; keep it byte-for-byte.
        SessionState.SetBool(LegacyPresentKey, PlayerPrefs.HasKey(LegacyProgressionKey));
        SessionState.SetString(LegacyValueKey, PlayerPrefs.GetString(LegacyProgressionKey, string.Empty));
        SessionState.SetInt(MigratedValueKey, PlayerPrefs.GetInt(LegacyMigratedKey, -1));
        // Crash safety: the same values also go to a git-ignored file.
        string backupPath = Path.Combine(Directory.GetParent(Application.dataPath).Parent.FullName,
            "Logs", "agent", "classes-playerprefs-backup.json");
        // Lane mirrors start without a Logs/agent folder.
        Directory.CreateDirectory(Path.GetDirectoryName(backupPath));
        File.WriteAllText(backupPath, PlayerPrefs.GetString(LegacyProgressionKey, string.Empty));
        PlayerPrefs.DeleteKey(LegacyProgressionKey);
        PlayerPrefs.DeleteKey(LegacyMigratedKey);
        PlayerPrefs.Save();
        Environment.SetEnvironmentVariable(GymSessionService.VerificationDirectoryVariable, directory);
        GymChaosVerifierExit.Record(1);
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        Hook();
        EditorApplication.isPlaying = true;
    }

    private static void Hook() => GymChaosVerifierHooks.Hook(Tick, PlayModeChanged);

    private static void PlayModeChanged(PlayModeStateChange change)
    {
        if (change == PlayModeStateChange.EnteredPlayMode)
        {
            Application.runInBackground = true;
            started = EditorApplication.timeSinceStartup;
            resumeAt = started;
            capture = SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null;
            reloadDisableErrors = 0;
            Application.logMessageReceived -= CountReloadDisableErrors;
            Application.logMessageReceived += CountReloadDisableErrors;
            string scenario = SessionState.GetString(ScenarioKey, string.Empty);
            routine = scenario == "classes" ? ClassScenario()
                : scenario == "death" ? DeathScenario()
                : scenario == "netDeath" ? NetDeathScenario()
                : scenario == "netHost" ? NetHostScenario()
                : scenario == "netGuest" ? NetGuestScenario() : RuntimeScenario();
        }
        if (change != PlayModeStateChange.EnteredEditMode) return;
        EditorApplication.update -= Tick;
        EditorApplication.playModeStateChanged -= PlayModeChanged;
        Application.logMessageReceived -= CountReloadDisableErrors;
        Cleanup();
        if (Application.isBatchMode) GymChaosVerifierExit.Exit(resultCode);
    }

    private static void Cleanup()
    {
        if (netRelay != null && !netRelay.HasExited) netRelay.Kill();
        netRelay = null;
        GymNetSession.End();
        string directory = SessionState.GetString(DirectoryKey, string.Empty);
        if (SessionState.GetBool(LegacyPresentKey, false))
            PlayerPrefs.SetString(LegacyProgressionKey, SessionState.GetString(LegacyValueKey, string.Empty));
        else
            PlayerPrefs.DeleteKey(LegacyProgressionKey);
        int migrated = SessionState.GetInt(MigratedValueKey, -1);
        if (migrated >= 0) PlayerPrefs.SetInt(LegacyMigratedKey, migrated);
        else PlayerPrefs.DeleteKey(LegacyMigratedKey);
        PlayerPrefs.Save();
        Environment.SetEnvironmentVariable(GymSessionService.VerificationDirectoryVariable, null);
        SessionState.EraseString(ScenarioKey);
        SessionState.EraseString(DirectoryKey);
        try
        {
            if (!string.IsNullOrEmpty(directory) && Directory.Exists(directory)) Directory.Delete(directory, true);
        }
        catch (IOException)
        {
        }
    }

    private static void CountReloadDisableErrors(string message, string stackTrace, LogType type)
    {
        if (type == LogType.Error && message.StartsWith("GYMCHAOS_VISITOR_DISABLED_INSIDE")) reloadDisableErrors++;
    }

    private static void Tick()
    {
        if (!EditorApplication.isPlaying || routine == null) return;
        double now = EditorApplication.timeSinceStartup;
        try
        {
            bool net = SessionState.GetString(ScenarioKey, string.Empty).StartsWith("net", StringComparison.Ordinal);
            if (now - started > (net ? 1200d : 420d)) throw new TimeoutException("Class/session verification timed out.");
            if (now < resumeAt) return;
            if (!routine.MoveNext())
            {
                if (reloadDisableErrors > 0)
                    throw new InvalidOperationException(
                        $"Scene reload logged GYMCHAOS_VISITOR_DISABLED_INSIDE {reloadDisableErrors} times.");
                routine = null;
                resultCode = 0;
                GymChaosVerifierExit.Record(0);
                EditorApplication.isPlaying = false;
                return;
            }
            resumeAt = now + routine.Current;
        }
        catch (Exception exception)
        {
            routine = null;
            Debug.LogException(exception);
            string scenarioName = SessionState.GetString(ScenarioKey, string.Empty);
            string marker = scenarioName == "classes" ? "GYMCHAOS_CLASS_BEHAVIOR_FAILED "
                : scenarioName.StartsWith("net", StringComparison.Ordinal) ? "GYMCHAOS_NET_TWO_PROCESS_FAILED " + scenarioName + " "
                : "GYMCHAOS_CLASS_SESSION_RUNTIME_FAILED ";
            Debug.LogError(marker + exception.Message);
            resultCode = 1;
            GymChaosVerifierExit.Record(1);
            EditorApplication.isPlaying = false;
        }
    }

    // ---- shared steps ----

    private const float Step = 0.3f;

    private static void Require(bool condition, string message) =>
        GymChaosClassSessionVerifier.Require(condition, message);

    private static IEnumerable<float> Until(Func<bool> condition, double seconds, string what)
    {
        double deadline = EditorApplication.timeSinceStartup + seconds;
        while (!condition())
        {
            if (EditorApplication.timeSinceStartup > deadline)
                throw new TimeoutException("Timed out waiting for " + what);
            yield return 0f;
        }
    }

    private static GymSessionMenu Menu => GymStartScreen.SessionMenu;
    private static PlayerMovement Player => UnityEngine.Object.FindFirstObjectByType<PlayerMovement>();
    private static string SaveDirectory => SessionState.GetString(DirectoryKey, string.Empty);

    private static IEnumerable<float> WaitForBootMenu()
    {
        foreach (float wait in Until(() => GymStartScreen.IsMenuVisible && Menu != null &&
            GymExperienceService.Active != null, 120d, "the boot menu")) yield return wait;
        yield return 1f;
        Require(!GymArenaBootstrap.IsGameplayStarted, "Gameplay started behind the boot menu");
        Require(!Player.enabled, "Player input is enabled behind the boot menu");
        Require(Menu.Current == GymSessionMenu.State.Closed, "Session overlay is open at boot");
        Require(Time.timeScale == 1f, "Time scale is not reset at the boot menu");
        Require(!GymSessionService.HasActive, "A character is active at the boot menu");
    }

    private static IEnumerable<float> OpenSessionChoice()
    {
        GymStartScreen.PlayButton.onClick.Invoke();
        yield return Step;
        Require(Menu.Current == GymSessionMenu.State.SessionChoice, "Play did not open the session choice");
        Require(!GymArenaBootstrap.IsGameplayStarted, "Play started gameplay without a character");
    }

    private static IEnumerable<float> StartNewCharacter(string classId)
    {
        foreach (float wait in OpenSessionChoice()) yield return wait;
        Menu.ChooseNewGame();
        yield return Step;
        // Two game processes start at once: the page change can take longer than one step.
        foreach (float wait in Until(() => Menu.Current == GymSessionMenu.State.ClassSelect, 10d, "New Game to open class selection")) yield return wait;
        Require(Menu.HighlightedClassId == "bodybuilding", "Fresh class selection does not highlight Bodybuilding");
        FocusCard(classId);
        yield return Step;
        Require(Menu.HighlightedClassId == classId, "Focusing the " + classId + " card did not preview it");
        Require(Menu.SelectedClassId == "bodybuilding", "Hovering a card changed the selected class");
        Menu.SelectClass(classId);
        if (classId != "bodybuilding")
        {
            FocusCard("bodybuilding");
            yield return Step;
            Require(Menu.SelectedClassId == classId, "Hovering another card broke the clicked selection");
        }
        int before = GymSessionService.CompletedWriteCount;
        Menu.ConfirmClass();
        // A second activation in the same frame must not create a second character.
        Menu.ConfirmClass();
        foreach (float wait in Until(() => GymArenaBootstrap.IsGameplayStarted &&
            GymSessionService.CompletedWriteCount > before && !GymSessionService.IsWriting, 30d,
            "the new character's initial save")) yield return wait;
        Require(GymSessionService.Active != null && GymSessionService.Active.classId == classId,
            "Confirmed class is not the active class");
        yield return 0.5f;
    }

    private static void FocusCard(string classId)
    {
        Transform card = Menu.transform.Find("Class Select/Class Card " + classId);
        Require(card != null, "Class card for " + classId + " is missing");
        EventSystem.current.SetSelectedGameObject(card.gameObject);
    }

    private static IEnumerable<float> SaveNow()
    {
        int before = GymSessionService.CompletedWriteCount;
        bool done = false;
        bool ok = false;
        GymSessionService.RequestSave("verifier", true, (succeeded, message) => { done = true; ok = succeeded; });
        foreach (float wait in Until(() => done, 20d, "a requested save")) yield return wait;
        Require(ok && GymSessionService.CompletedWriteCount > before, "Requested save failed");
    }

    private static IEnumerable<float> ReturnToMenu()
    {
        GymSessionService.ReturnToMenu();
        yield return 0.5f;
        foreach (float wait in WaitForBootMenu()) yield return wait;
    }

    private static IEnumerable<float> LoadCharacter(string slotId, bool viaPreviewBack)
    {
        foreach (float wait in OpenSessionChoice()) yield return wait;
        Require(Menu.LoadGameOffered, "Load Game is not offered although a valid save exists");
        Require(Menu.FocusedObject != null && Menu.FocusedObject.name == "Load Game Button",
            "Load Game is not the initial focus when saves exist");
        Menu.ChooseLoadGame();
        yield return Step;
        Require(Menu.Current == GymSessionMenu.State.SaveList, "Load Game did not open the save list");
        Menu.SelectSave(slotId);
        yield return Step;
        Require(Menu.Current == GymSessionMenu.State.SavePreview, "Selecting a save did not open its preview");
        if (viaPreviewBack)
        {
            Menu.Back();
            yield return Step;
            Require(Menu.Current == GymSessionMenu.State.SaveList, "Preview Back did not return to the list");
            Require(!GymArenaBootstrap.IsGameplayStarted, "Cancelling a preview started gameplay");
            Menu.SelectSave(slotId);
            yield return Step;
        }
        Menu.ConfirmLoad();
        foreach (float wait in Until(() => GymArenaBootstrap.IsGameplayStarted, 30d, "the loaded session"))
            yield return wait;
        Require(GymSessionService.Active != null && GymSessionService.Active.slotId == slotId,
            "Loaded session is not the selected character");
        yield return Step;
    }

    private static void AssertSingletons(string when)
    {
        Require(UnityEngine.Object.FindObjectsByType<GymSessionService>(FindObjectsSortMode.None).Length <= 1,
            "Duplicate session service " + when);
        Require(UnityEngine.Object.FindObjectsByType<GymExperienceService>(FindObjectsSortMode.None).Length == 1,
            "Duplicate progression service " + when);
        Require(UnityEngine.Object.FindObjectsByType<GymHud>(FindObjectsSortMode.None).Length <= 1,
            "Duplicate HUD " + when);
        Require(UnityEngine.Object.FindObjectsByType<GymPauseMenu>(FindObjectsSortMode.None).Length <= 1,
            "Duplicate pause menu " + when);
        Require(UnityEngine.Object.FindObjectsByType<EventSystem>(FindObjectsSortMode.None).Length == 1,
            "Duplicate event system " + when);
        Require(UnityEngine.Object.FindObjectsByType<PlayerHandRig>(FindObjectsInactive.Include,
            FindObjectsSortMode.None).Length == 1, "Duplicate player avatar rig " + when);
    }

    private static Button PauseButton(string name)
    {
        GymPauseMenu pause = UnityEngine.Object.FindFirstObjectByType<GymPauseMenu>();
        Require(pause != null, "Pause menu is missing");
        foreach (Button button in pause.GetComponentsInChildren<Button>(true))
        {
            if (button.name == name) return button;
        }
        throw new InvalidOperationException("Pause button missing: " + name);
    }

    // ---- G3: menu flow and save lifecycle ----

    private static IEnumerator<float> RuntimeScenario()
    {
        foreach (float wait in WaitForBootMenu()) yield return wait;
        GymSaveRepository repository = GymSessionService.Repository;
        Require(repository.DirectoryPath == Path.GetFullPath(SaveDirectory), "Saves are not redirected");

        // No-save boot: only New Game, focused; Back returns to the main menu.
        foreach (float wait in OpenSessionChoice()) yield return wait;
        Require(!Menu.LoadGameOffered, "Load Game is offered without any save");
        Require(Menu.FocusedObject != null && Menu.FocusedObject.name == "New Game Button",
            "New Game is not focused when no save exists");
        Shot("session-choice-empty");
        Menu.Back();
        yield return Step;
        Require(Menu.Current == GymSessionMenu.State.Closed &&
            Menu.FocusedObject == GymStartScreen.PlayButton.gameObject, "Back did not restore main-menu focus");

        // Highlighting is not a commitment: Back from class selection persists nothing.
        foreach (float wait in OpenSessionChoice()) yield return wait;
        Menu.ChooseNewGame();
        yield return Step;
        FocusCard("cardio");
        yield return Step;
        Require(Menu.HighlightedClassId == "cardio", "Card focus did not change the preview");
        Menu.SelectClass("cardio");
        yield return 0.5f;
        PersonaMenuButton focusedCard = Menu.transform.Find("Class Select/Class Card cardio")
            .GetComponent<PersonaMenuButton>();
        Color rim = focusedCard.CurrentOutlineColorForVerification;
        Color cardFace = focusedCard.CurrentFaceColorForVerification;
        Require(Mathf.Abs(rim.r - cardFace.r) + Mathf.Abs(rim.g - cardFace.g) + Mathf.Abs(rim.b - cardFace.b) < 0.05f,
            "Focused class card shows a dark band between its stroke and its face");
        Shot("class-card-focus");
        Transform confirm = Menu.transform.Find("Class Select/Class Confirm Button");
        EventSystem.current.SetSelectedGameObject(confirm.gameObject);
        yield return Step;
        PersonaMenuButton cardioCard = Menu.transform.Find("Class Select/Class Card cardio")
            .GetComponent<PersonaMenuButton>();
        Require(cardioCard.IsSelectedForVerification && !cardioCard.IsFocusedForVerification &&
            confirm.GetComponent<PersonaMenuButton>().IsFocusedForVerification,
            "Selected and focused are not distinct states");
        Shot("class-select-cardio");
        Menu.Back();
        yield return Step;
        Require(Menu.Current == GymSessionMenu.State.SessionChoice, "Class Back did not return to session choice");
        Require(!GymSessionService.HasActive && Directory.GetFiles(SaveDirectory).Length == 0,
            "A highlighted class was persisted without Confirm");
        Menu.Back();
        yield return Step;

        // New character: calisthenics.
        foreach (float wait in StartNewCharacter("calisthenics")) yield return wait;
        string firstSlot = GymSessionService.Active.slotId;
        Require(repository.List().Count == 1, "Confirm created " + repository.List().Count + " slots");
        Require(!GymStartScreen.IsMenuVisible && Player.enabled, "Gameplay input is not enabled after Confirm");
        AssertSingletons("after the first new game");
        GymExperienceService progression = GymExperienceService.Active;
        Require(Mathf.Abs(progression.GetSprintCapacity() - 100f) < 0.01f, "Calisthenics stamina capacity is wrong");
        Require(GymHud.Active != null, "HUD is not bound after starting a session");
        yield return 0.5f;
        Require(Mathf.Abs(GymHud.Active.StaminaMaximumShown - 100f) < 0.5f,
            "HUD stamina maximum does not follow the class");
        Shot("hud-calisthenics");
        AssertHudLayout(GymHud.Active, "calisthenics", "1");
        GymHud.ProfiledTicks = 0;
        GymHud.ProfiledBytes = 0;
        GymHud.ProfiledFrames = 0;
        // Sample a frame count, not a fixed time: under load 2 s can hold
        // fewer than the 20 frames the average needs.
        for (float waited = 0f; waited < 2f || (GymHud.ProfiledFrames <= 20 && waited < 20f); waited += 0.25f)
        {
            yield return 0.25f;
        }
        double hudMicroseconds = GymHud.ProfiledTicks * 1000000.0 /
            System.Diagnostics.Stopwatch.Frequency / Mathf.Max(1, GymHud.ProfiledFrames);
        double hudBytes = GymHud.ProfiledBytes / (double)Mathf.Max(1, GymHud.ProfiledFrames);
        Debug.Log($"GYMCHAOS_HUD_COST frames={GymHud.ProfiledFrames} avgMicroseconds={hudMicroseconds:F1} " +
            $"bytesPerFrame={hudBytes:F1}");
        // Timing is only a coarse guard in batch mode; allocation is the strict part.
        Require(GymHud.ProfiledFrames > 20 && hudMicroseconds < 1000.0 && hudBytes < 64.0,
            $"HUD refresh costs {hudMicroseconds:F1} us and {hudBytes:F1} B per frame");
        AssertPromptShadowFont(Player);

        // Durable progress, a once-only reward, resources and position.
        progression.AwardExperience(30, "verifier", "once:verifier-reward");
        progression.AwardExperience(30, "verifier", "once:verifier-reward");
        Require(progression.State.experience == 30, "Once-only reward was granted twice");
        foreach (float wait in Until(() => Player.IsGroundedForSession, 15d, "the player to land")) yield return wait;
        Vector3 savedPosition = Player.transform.position + Player.transform.forward * 1.5f;
        Player.RestoreSessionState(true, savedPosition, 135f, 120f, 50f);
        foreach (float wait in Until(() => Player.IsGroundedForSession, 15d, "the moved player to land"))
            yield return wait;
        yield return 0.8f;
        savedPosition = Player.transform.position;
        // World flags: clock and one broken panel.
        GymTimeOfDay.Instance.SetTimeForVerification(0.61f);
        GlassShatterPanel panel = UnityEngine.Object.FindFirstObjectByType<GlassShatterPanel>();
        Require(panel != null, "No breakable panel in the scene");
        string brokenPanel = panel.StableId;
        int panelsBefore = UnityEngine.Object.FindObjectsByType<GlassShatterPanel>(FindObjectsSortMode.None).Length;
        panel.ShatterFromPowerImpact(panel.transform.position, panel.transform.forward, panel.transform.forward * 12f);
        yield return 0.3f;
        Player.RestoreSessionState(false, Vector3.zero, 0f, 120f, 50f);
        foreach (float wait in SaveNow()) yield return wait;
        GymCharacterSave onDisk = repository.Read(firstSlot).Data;
        Require(Array.IndexOf(onDisk.brokenPanels, brokenPanel) >= 0 && Mathf.Abs(onDisk.worldTime - 0.61f) < 0.08f,
            "World clock or broken panel was not saved");

        // Saving mid-exercise keeps the last standing position, not the station pose.
        progression.CompleteLockerPrep();
        foreach (GymExerciseStation candidate in UnityEngine.Object.FindObjectsByType<GymExerciseStation>(
            FindObjectsSortMode.None))
        {
            // Any free non-pull-up station will do; visitors may hold some of them.
            if (candidate.ExerciseType == GymExerciseType.PullUps || candidate.RequiresWeightSelection) continue;
            InvokePrivate(Player, "BeginExercise", candidate);
            if (Player.IsExercising) break;
        }
        yield return 0.4f;
        Require(Player.IsExercising, "Exercise did not start for the mid-exercise save");
        foreach (float wait in SaveNow()) yield return wait;
        GymCharacterSave duringExercise = repository.Read(firstSlot).Data;
        Require(duringExercise.hasSpawn && (duringExercise.spawn - savedPosition).magnitude < 0.6f,
            "Mid-exercise save stored the station pose instead of a safe standing spot");
        InvokePrivate(Player, "EndExercise");
        yield return 0.4f;
        Require(!Player.IsExercising, "Exercise did not end");
        Player.RestoreSessionState(true, savedPosition, 135f, 120f, 50f);
        yield return 0.8f;
        Player.RestoreSessionState(false, Vector3.zero, 0f, 120f, 50f);
        foreach (float wait in SaveNow()) yield return wait;
        onDisk = repository.Read(firstSlot).Data;
        Require(onDisk.progression.experience == progression.State.experience && onDisk.progression.experience >= 30 &&
            onDisk.classId == "calisthenics" && onDisk.hasSpawn &&
            Array.IndexOf(onDisk.oneShotRewards, "once:verifier-reward") >= 0 &&
            onDisk.health >= 120f && onDisk.health < 130f && onDisk.stamina >= 50f && onDisk.stamina < 80f,
            "Manual save did not capture progression, rewards and resources");
        Require((onDisk.spawn - savedPosition).magnitude < 0.6f, "Saved spawn is not the player's position");

        // Notices: bounded queue, critical first, repeats merged.
        GymHud hud = GymHud.Active;
        foreach (float wait in Until(() => hud.QueuedNoticeCount == 0, 15d, "earlier notices to expire"))
            yield return wait;
        for (int i = 0; i < 12; i++) hud.PushNotice("Notice " + i, 3f, 0);
        hud.PushNotice("Notice 0", 3f, 0);
        hud.PushNotice("Critical", 3f, 2);
        Require(hud.QueuedNoticeCount <= GymHud.MaxQueuedNotices, "Notice queue is unbounded");
        Require(hud.TopNoticeText == "Critical", "Critical notice is not first");
        yield return 0.3f;
        Shot("hud-notices");

        // Autosave timer: runs in active play, stops while paused, fires at the interval.
        float elapsedA = GymSessionService.AutosaveElapsedSeconds;
        yield return 1f;
        float elapsedB = GymSessionService.AutosaveElapsedSeconds;
        Require(elapsedB > elapsedA + 0.5f, "Autosave timer does not advance during active gameplay");
        GymPauseMenu.Open(Player);
        yield return Step;
        Require(GymPauseMenu.IsVisible && Time.timeScale == 0f, "Pause did not open");
        Require(PauseButton("Pause Save Button").gameObject.activeSelf &&
            PauseButton("Pause Save Return Button").gameObject.activeSelf, "Pause menu lacks the save actions");
        Shot("pause-save");
        float pausedA = GymSessionService.AutosaveElapsedSeconds;
        double playA = GymSessionService.Active.playSeconds;
        yield return 1f;
        Require(Mathf.Approximately(pausedA, GymSessionService.AutosaveElapsedSeconds) &&
            playA == GymSessionService.Active.playSeconds, "Autosave or playtime advanced while paused");
        int beforeManual = GymSessionService.CompletedWriteCount;
        PauseButton("Pause Save Button").onClick.Invoke();
        foreach (float wait in Until(() => GymSessionService.CompletedWriteCount > beforeManual, 20d,
            "the pause-menu save")) yield return wait;
        Require(GymSessionService.Status == GymSaveStatus.Saved, "Pause-menu save did not report success");
        PauseButton("Pause Play Button").onClick.Invoke();
        yield return Step;
        Require(!GymPauseMenu.IsVisible && Time.timeScale == 1f, "Resume did not close the pause menu");
        int beforeAuto = GymSessionService.CompletedWriteCount;
        GymSessionService.AdvanceAutosaveForVerification(GymSessionService.AutosaveIntervalSeconds);
        foreach (float wait in Until(() => GymSessionService.CompletedWriteCount > beforeAuto, 20d, "the autosave"))
            yield return wait;
        Require(GymSessionService.AutosaveElapsedSeconds < 5f, "Autosave timer did not restart");
        Require(GymSessionService.AutosaveIntervalSeconds == 180f, "Autosave default is not 180 seconds");

        // Rapid requests coalesce; the last state still reaches the disk.
        foreach (float wait in Until(() => !GymSessionService.IsWriting, 20d, "writes to settle")) yield return wait;
        int beforeBurst = GymSessionService.CompletedWriteCount;
        for (int i = 0; i < 20; i++)
        {
            progression.AwardExperience(1, "burst", null);
            GymSessionService.RequestSave("burst", true);
        }
        progression.AwardExperience(1, "burst-tail", "burst-tail");
        GymSessionService.RequestSave("burst-tail", true);
        int expectedExperience = progression.State.experience;
        foreach (float wait in Until(() => !GymSessionService.IsWriting &&
            !GymSessionService.HasPendingRequestForVerification, 30d, "the burst to settle")) yield return wait;
        yield return 0.3f;
        int burstWrites = GymSessionService.CompletedWriteCount - beforeBurst;
        Require(burstWrites >= 1 && burstWrites <= 3, "Burst of 21 requests produced " + burstWrites + " writes");
        Require(repository.Read(firstSlot).Data.progression.experience == expectedExperience,
            "A change made during a write was lost");
        Require(Directory.GetFiles(SaveDirectory, firstSlot + "*").Length == 2,
            "Autosaves grew beyond current + backup");

        // Failed write: last good file intact, feedback shown, retry works.
        string slotPath = Path.Combine(SaveDirectory, firstSlot + ".json");
        byte[] good = File.ReadAllBytes(slotPath);
        progression.AwardExperience(2, "after-good", null);
        bool failedDone = false;
        bool failedOk = true;
        using (new FileStream(slotPath + ".tmp", FileMode.Create, FileAccess.Write, FileShare.None))
        {
            GymSessionService.RequestSave("blocked", true, (ok, message) => { failedDone = true; failedOk = ok; });
            foreach (float wait in Until(() => failedDone, 20d, "the blocked save to fail")) yield return wait;
        }
        Require(!failedOk && GymSessionService.Status == GymSaveStatus.Failed, "Blocked save did not report failure");
        Require(Equal(good, File.ReadAllBytes(slotPath)), "Failed write damaged the last good save");
        yield return 0.2f;
        Require(hud.SaveIndicatorText == "SAVE FAILED", "HUD does not show the save failure");
        Shot("hud-save-failed");
        if (File.Exists(slotPath + ".tmp")) File.Delete(slotPath + ".tmp");
        foreach (float wait in SaveNow()) yield return wait;
        Require(repository.Read(firstSlot).Data.progression.experience == progression.State.experience,
            "Retry after the failure did not save");
        int firstExperience = progression.State.experience;
        int firstTotal = progression.State.totalExperience;

        // Save & Return with a failing write keeps the player in a usable menu.
        GymPauseMenu.Open(Player);
        yield return Step;
        FileStream blocker = new FileStream(slotPath + ".tmp", FileMode.Create, FileAccess.Write, FileShare.None);
        PauseButton("Pause Save Return Button").onClick.Invoke();
        GymPauseMenu pauseMenu = UnityEngine.Object.FindFirstObjectByType<GymPauseMenu>();
        Transform failurePage = pauseMenu.transform.Find("Save Failed Page");
        foreach (float wait in Until(() => failurePage.gameObject.activeSelf, 20d, "the save-failed page"))
            yield return wait;
        blocker.Dispose();
        Require(GymSessionService.HasActive && GymPauseMenu.IsVisible,
            "A failed Save & Return left the session or the menu");
        Shot("pause-save-failed");
        if (File.Exists(slotPath + ".tmp")) File.Delete(slotPath + ".tmp");
        PauseButton("Save Retry Button").onClick.Invoke();
        yield return 0.5f;
        foreach (float wait in WaitForBootMenu()) yield return wait;
        AssertSingletons("after Save & Return");

        // Newer-version and unknown-class saves: never listed, never touched.
        string stamp = DateTime.UtcNow.ToString("O");
        string futureId = Guid.NewGuid().ToString("N");
        string unknownId = Guid.NewGuid().ToString("N");
        string futureFile = Path.Combine(SaveDirectory, futureId + ".json");
        string unknownFile = Path.Combine(SaveDirectory, unknownId + ".json");
        File.WriteAllText(futureFile, GymChaosClassSessionVerifier.Envelope(
            "{\"version\":" + (GymCharacterSave.CurrentVersion + 1) + ",\"slotId\":\"" + futureId +
            "\",\"classId\":\"cardio\",\"createdUtc\":\"" + stamp + "\",\"savedUtc\":\"" + stamp +
            "\",\"progression\":{\"level\":9}}"));
        File.WriteAllText(unknownFile, GymChaosClassSessionVerifier.Envelope(
            "{\"version\":1,\"slotId\":\"" + unknownId + "\",\"classId\":\"crossfit\",\"createdUtc\":\"" + stamp +
            "\",\"savedUtc\":\"" + stamp + "\",\"progression\":{\"level\":3}}"));
        byte[] futureBytes = File.ReadAllBytes(futureFile);
        byte[] unknownBytes = File.ReadAllBytes(unknownFile);

        // Keyboard/controller navigation through real uGUI move and submit events.
        foreach (float wait in OpenSessionChoice()) yield return wait;
        Require(Menu.ValidSaveCount == 1 && Menu.ChoiceNoteText.Contains("2 SAVES COULD NOT BE READ"),
            "Incompatible saves are listed or not reported: " + Menu.ChoiceNoteText);
        Require(Menu.FocusedObject.name == "Load Game Button", "Load Game is not focused with a save present");
        Move(MoveDirection.Down);
        yield return Step;
        Require(Menu.FocusedObject != null && Menu.FocusedObject.name == "New Game Button",
            "Down did not move focus from Load Game to New Game");
        Submit();
        yield return Step;
        Require(Menu.Current == GymSessionMenu.State.ClassSelect, "Submit did not activate New Game");
        Move(MoveDirection.Right);
        yield return Step;
        Require(Menu.HighlightedClassId == "calisthenics",
            "Right did not move the class highlight: " + Menu.HighlightedClassId);
        Require(!GymSessionService.HasActive && !GymArenaBootstrap.IsGameplayStarted && !Player.enabled,
            "Navigating the menu leaked into gameplay");
        Menu.Back();
        yield return Step;
        Menu.Back();
        yield return Step;

        // Existing-save boot: second, independent character.
        foreach (float wait in StartNewCharacter("strongman")) yield return wait;
        string secondSlot = GymSessionService.Active.slotId;
        Require(secondSlot != firstSlot && GymSessionService.ListValidSaves(out _).Count == 2,
            "New Game replaced the existing character");
        Require(Equal(futureBytes, File.ReadAllBytes(futureFile)) && Equal(unknownBytes, File.ReadAllBytes(unknownFile)),
            "An incompatible save file was modified");
        progression = GymExperienceService.Active;
        Require(progression.State.experience == 0 && progression.State.level == 1 &&
            progression.CaptureOneShotRewards().Length == 0, "New character inherited the previous progress");
        Require(Mathf.Abs(progression.GetSprintCapacity() - 115f) < 0.01f, "Strongman stamina capacity is wrong");
        yield return 0.5f;
        Require(Mathf.Abs(GymHud.Active.StaminaMaximumShown - 115f) < 0.5f,
            "HUD stamina maximum does not follow the class");
        progression.AwardExperience(7, "second", null);
        GymSessionService.AdvanceAutosaveForVerification(GymSessionService.AutosaveIntervalSeconds);
        int beforeSecond = GymSessionService.CompletedWriteCount;
        foreach (float wait in Until(() => GymSessionService.CompletedWriteCount > beforeSecond, 20d,
            "the second character's autosave")) yield return wait;
        Require(repository.Read(firstSlot).Data.progression.experience == firstExperience &&
            repository.Read(secondSlot).Data.progression.experience == 7,
            "Autosave of one character changed the other");
        AssertHudLayout(GymHud.Active, "strongman", "1");

        // Quit writes even when only position and playtime changed.
        foreach (float wait in Until(() => !GymSessionService.IsWriting, 20d, "writes to settle")) yield return wait;
        string secondPath = Path.Combine(SaveDirectory, secondSlot + ".json");
        string savedBeforeQuit = repository.Read(secondSlot).Data.savedUtc;
        yield return 0.2f;
        UnityEngine.Object.FindFirstObjectByType<GymSessionService>().SendMessage("OnApplicationQuit");
        Require(repository.Read(secondSlot).Data.savedUtc != savedBeforeQuit, "Quit did not write the character");

        // Death: the character resumes rested at the entrance, class intact.
        InvokePrivate(Player, "Die", new object[] { null });
        Require(Player.IsDead, "Player did not die");
        foreach (float wait in SaveNow()) yield return wait;
        GymCharacterSave afterDeath = repository.Read(secondSlot).Data;
        Require(!afterDeath.hasSpawn && afterDeath.health >= Player.MaxHealth - 0.01f &&
            afterDeath.classId == "strongman" && Mathf.Abs(afterDeath.stamina - 115f) < 0.5f,
            "A save made while dead does not resume rested at the entrance");
        foreach (float wait in ReturnToMenu()) yield return wait;
        foreach (float wait in LoadCharacter(secondSlot, false)) yield return wait;
        Require(!Player.IsDead && Player.CurrentHealth >= Player.MaxHealth - 0.01f &&
            Mathf.Abs(GymExperienceService.Active.GetSprintCapacity() - 115f) < 0.01f &&
            GymSessionService.ActiveClass.id == "strongman",
            "Respawn after death lost the class or its stats");
        foreach (float wait in ReturnToMenu()) yield return wait;

        // Load with preview, cancel, then confirm: class restored without re-selection.
        foreach (float wait in OpenSessionChoice()) yield return wait;
        Shot("session-choice-saves");
        Menu.ChooseLoadGame();
        yield return Step;
        Shot("save-list");
        Menu.SelectSave(firstSlot);
        yield return Step;
        Shot("save-preview");
        Menu.Back();
        yield return Step;
        Menu.Back();
        yield return Step;
        Menu.Back();
        yield return Step;
        GymCharacterSave expectedLoad = repository.Read(firstSlot).Data;
        foreach (float wait in LoadCharacter(firstSlot, true)) yield return wait;
        progression = GymExperienceService.Active;
        Require(GymSessionService.Active.classId == "calisthenics", "Loaded character has the wrong class");
        Require(progression.State.experience == firstExperience && progression.State.totalExperience == firstTotal,
            "Loading changed or replayed experience");
        Require(progression.State.gymDay == 1, "A same-day reload advanced the gym day and re-armed daily rewards");
        progression.AwardExperience(30, "verifier", "once:verifier-reward");
        Require(progression.State.experience == firstExperience, "Loading allowed a one-time reward again");
        Require(Mathf.Abs(progression.GetSprintCapacity() - 100f) < 0.01f &&
            Mathf.Abs(progression.GetSprintDrainPerSecond() - 18f * 1.1111f) < 0.001f,
            "Class modifiers stacked or vanished after a reload");
        // Regeneration may have run for a moment since the restore.
        Require(Player.CurrentHealth >= expectedLoad.health - 0.5f && Player.CurrentHealth <= expectedLoad.health + 25f &&
            Player.SprintEnergy >= Mathf.Min(expectedLoad.stamina, 100f) - 0.5f && Player.SprintEnergy <= 100f,
            $"Resources were not restored: health {Player.CurrentHealth:F1}/{expectedLoad.health:F1} " +
            $"stamina {Player.SprintEnergy:F1}/{expectedLoad.stamina:F1}");
        Require((Player.transform.position - savedPosition).magnitude < 0.8f, "Spawn was not restored");
        Require(Mathf.Abs(Mathf.DeltaAngle(GymTimeOfDay.Instance.Time01 * 360f, 0.61f * 360f)) < 40f,
            "World clock was not restored: " + GymTimeOfDay.Instance.Time01);
        bool panelStillThere = false;
        foreach (GlassShatterPanel candidate in UnityEngine.Object.FindObjectsByType<GlassShatterPanel>(
            FindObjectsSortMode.None))
        {
            if (candidate.StableId == brokenPanel && !candidate.IsShattered) panelStillThere = true;
        }
        yield return 0.2f;
        Require(!panelStillThere || UnityEngine.Object.FindObjectsByType<GlassShatterPanel>(
            FindObjectsSortMode.None).Length < panelsBefore, "Broken panel came back after loading");
        Require(Array.IndexOf(GlassShatterPanel.CaptureShatteredIds(), brokenPanel) >= 0,
            "Broken panel is missing from the restored world flags");
        AssertSingletons("after loading");
        yield return 0.5f;
        Require(GymHud.Active.QueuedNoticeCount == 0, "Notices lingered across a load");

        // Stale resources clamp against the class maximum.
        Player.RestoreSessionState(false, Vector3.zero, 0f, 9999f, 9999f);
        Require(Player.CurrentHealth <= Player.MaxHealth && Player.SprintEnergy <= 100.001f,
            "Restored resources exceed the derived maximum");
        foreach (float wait in ReturnToMenu()) yield return wait;

        // Corrupt saves never appear as valid; a bad load keeps the menu usable.
        File.WriteAllText(Path.Combine(SaveDirectory, secondSlot + ".json"), "corrupt");
        File.WriteAllText(Path.Combine(SaveDirectory, secondSlot + ".json.bak"), "corrupt");
        foreach (float wait in OpenSessionChoice()) yield return wait;
        Require(Menu.LoadGameOffered && Menu.ValidSaveCount == 1, "Corrupt save is counted as valid");
        Menu.ChooseLoadGame();
        yield return Step;
        Menu.SelectSave(firstSlot);
        yield return Step;
        string firstPath = Path.Combine(SaveDirectory, firstSlot + ".json");
        byte[] firstCurrent = File.ReadAllBytes(firstPath);
        byte[] firstBackup = File.ReadAllBytes(firstPath + ".bak");
        File.WriteAllText(firstPath, "corrupt");
        File.WriteAllText(firstPath + ".bak", "corrupt");
        Menu.ConfirmLoad();
        yield return Step;
        Require(Menu.Current == GymSessionMenu.State.Error && !GymArenaBootstrap.IsGameplayStarted,
            "A corrupt save was loaded instead of reporting an error");
        Shot("load-error");
        Menu.Back();
        yield return Step;
        Require(Menu.Current == GymSessionMenu.State.SessionChoice && !Menu.LoadGameOffered,
            "Recovery from a load error did not return to a usable menu");

        // Backup recovery in the real flow: corrupt current, intact backup.
        File.WriteAllBytes(firstPath + ".bak", firstBackup);
        Menu.Back();
        yield return Step;
        foreach (float wait in LoadCharacter(firstSlot, false)) yield return wait;
        Require(GymSessionService.Active.classId == "calisthenics", "Backup recovery loaded the wrong character");
        File.WriteAllBytes(firstPath, firstCurrent);
        foreach (float wait in ReturnToMenu()) yield return wait;

        // Legacy PlayerPrefs progression becomes one Bodybuilding character, once.
        PlayerPrefs.SetString(LegacyProgressionKey,
            "{\"level\":6,\"experience\":12,\"totalExperience\":800,\"strengthRank\":2,\"gymDay\":4}");
        PlayerPrefs.DeleteKey(LegacyMigratedKey);
        int beforeLegacy = GymSessionService.ListValidSaves(out _).Count;
        List<GymSaveListEntry> afterLegacy = GymSessionService.ListValidSaves(out _);
        Require(afterLegacy.Count == beforeLegacy, "Legacy migration ran twice");
        GymSaveListEntry legacy = afterLegacy.Find(entry => entry.Result.Data.progression.level == 6);
        Require(legacy != null && legacy.Result.Data.classId == "bodybuilding" &&
            legacy.Result.Data.progression.totalExperience == 800 &&
            legacy.Result.Data.progression.strengthRank == 2, "Legacy progression was not migrated intact");
        Require(PlayerPrefs.GetString(LegacyProgressionKey, string.Empty).Length > 0,
            "Legacy PlayerPrefs save was removed by the migration");
        foreach (float wait in LoadCharacter(legacy.SlotId, false)) yield return wait;
        Require(GymSessionService.ActiveClass.id == "bodybuilding" &&
            GymExperienceService.Active.State.level == 6 &&
            Mathf.Abs(GymExperienceService.Active.GetSprintCapacity() - 100f) < 0.01f,
            "Migrated character did not load as the Bodybuilding baseline");

        // After many scene reloads there is still one HUD and one pause listener.
        Require(GymExperienceService.Active.NoticeListenerCountForVerification <= 1,
            "Notice listeners leaked: " + GymExperienceService.Active.NoticeListenerCountForVerification);
        Require(GymSessionService.SaveStatusListenerCountForVerification <= 2,
            "Save-status listeners leaked: " + GymSessionService.SaveStatusListenerCountForVerification);

        Debug.Log("GYMCHAOS_CLASS_SESSION_RUNTIME_OK flow=new-back-confirm-load-preview-cancel " +
            "saves=manual-autosave-paused-burst-failed-retry-return recovery=backup-corrupt-legacy " +
            "burstWrites=" + burstWrites);
    }

    // ---- G4: per-class appearance, stats and exercise integration ----

    // Player death: YOU DIED, then the reloaded menu opens on the save list;
    // picking a save respawns the player alive in that character.
    private static IEnumerator<float> DeathScenario()
    {
        foreach (float wait in WaitForBootMenu()) yield return wait;
        foreach (float wait in StartNewCharacter("bodybuilding")) yield return wait;
        foreach (float wait in SaveNow()) yield return wait;
        string slotId = GymSessionService.Active.slotId;
        Player.ReceiveEnemyPunch(100000f, Vector3.zero, null);
        yield return Step;
        Require(Player.IsDead, "Lethal damage did not kill the player");
        Require(!GymStartScreen.IsMenuVisible, "Menu opened before the death screen was shown");
        foreach (float wait in Until(() => GymStartScreen.IsMenuVisible && Menu != null &&
            Menu.Current == GymSessionMenu.State.SaveList, 30d, "the save list after death")) yield return wait;
        Require(!GymSessionService.HasActive, "The dead character is still active at the save list");
        Require(!GymArenaBootstrap.IsGameplayStarted, "Gameplay runs behind the death save list");
        Debug.Log($"GYMCHAOS_DEATH_LOAD_LIST_OK saves={Menu.ValidSaveCount}");
        yield return 0.5f;
        Menu.SelectSave(slotId);
        yield return Step;
        Require(Menu.Current == GymSessionMenu.State.SavePreview, "Selecting the save after death did not preview it");
        Menu.ConfirmLoad();
        foreach (float wait in Until(() => GymArenaBootstrap.IsGameplayStarted, 30d, "the respawned session"))
            yield return wait;
        yield return 1f;
        Require(GymSessionService.Active != null && GymSessionService.Active.slotId == slotId,
            "Respawn did not continue the chosen save");
        Require(Player != null && !Player.IsDead && Player.CurrentHealth > 0f, "Player is not alive after respawn");
        Debug.Log($"GYMCHAOS_DEATH_RESPAWN_OK slot={slotId} health={Player.CurrentHealth:F0}");
    }

    private static IEnumerator<float> ClassScenario()
    {
        foreach (float wait in WaitForBootMenu()) yield return wait;
        float baselineHeight = 0f;
        float baselineCameraY = 0f;
        float baselineControllerHeight = 0f;
        float baselineRadius = 0f;
        float baselineRun = 0f;
        Vector3 baselineHead = Vector3.zero;
        Vector3 baselineLeftHand = Vector3.zero;
        Vector3 baselineLeftThigh = Vector3.zero;
        var summary = new List<string>();

        for (int index = 0; index < ClassIds.Length; index++)
        {
            string classId = ClassIds[index];
            GymClassDefinition definition = GymClassCatalog.Get(classId);
            foreach (float wait in StartNewCharacter(classId)) yield return wait;
            for (int pass = 0; pass < 2; pass++)
            {
                // pass 0 = fresh character, pass 1 = the same character after save, menu and reload.
                GymExperienceService progression = GymExperienceService.Active;
                PlayerMovement player = Player;
                PlayerHandRig rig = player.GetComponentInChildren<PlayerHandRig>(true);
                string expectedModel = index == 0 ? "Player/player_authored" : "Player/Classes/player_" + classId;
                Require(rig != null && rig.RuntimeModelResourcePath == expectedModel,
                    classId + " uses model " + (rig != null ? rig.RuntimeModelResourcePath : "none"));
                Require(Resources.Load<GameObject>(expectedModel) != null, "Missing class model " + expectedModel);
                Require(rig.RuntimeModelRoot != null &&
                    rig.RuntimeModelRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length > 0,
                    classId + " avatar has no skinned mesh");

                // Exactly-once derivation, stable across repeated reads.
                for (int read = 0; read < 3; read++)
                {
                    Require(Near(progression.GetSprintCapacity(), 100f * definition.staminaCapacity) &&
                        Near(progression.GetSprintMultiplier(), definition.sprintSpeed) &&
                        Near(progression.GetSprintDrainPerSecond(), 18f * definition.sprintCost) &&
                        Near(progression.GetSprintRecoveryPerSecond(), 14f),
                        classId + " locomotion modifiers are wrong on pass " + pass);
                }
                Require(Near(progression.GetExercisePerformance(GymExerciseType.PullUps), definition.bodyweight) &&
                    Near(progression.GetExercisePerformance(GymExerciseType.Dips), definition.bodyweight) &&
                    Near(progression.GetExercisePerformance(GymExerciseType.BarbellSquat), definition.compound) &&
                    Near(progression.GetExercisePerformance(GymExerciseType.FlatBenchPress), definition.compound) &&
                    Near(progression.GetExercisePerformance(GymExerciseType.Deadlift), definition.compound) &&
                    Near(progression.GetExercisePerformance(GymExerciseType.Treadmill), definition.cardio) &&
                    Near(progression.GetExercisePerformance(GymExerciseType.ExerciseBike), definition.cardio) &&
                    Near(progression.GetExercisePerformance(GymExerciseType.LatPulldown), 1f),
                    classId + " exercise performance is wrong on pass " + pass);
                // Health, combat and jump stay at the baseline for every class.
                Require(Near(player.MaxHealth, 200f) && Near(progression.ScaleStrengthDamage(10f), 10f) &&
                    Near(progression.GetStrengthForceMultiplier(), 1f), classId + " changed baseline combat or health");

                // Shared skeleton contract: height, camera and joints match the baseline.
                yield return 0.6f;
                CharacterController controller = player.GetComponent<CharacterController>();
                float height = rig.RuntimeVisibleHeight;
                Vector3 head = player.transform.InverseTransformPoint(rig.RuntimeHead.position);
                Vector3 leftHand = player.transform.InverseTransformPoint(rig.RuntimeLeftHand.position);
                Vector3 leftThigh = player.transform.InverseTransformPoint(rig.RuntimeLeftThigh.position);
                if (index == 0 && pass == 0)
                {
                    baselineHeight = height;
                    baselineCameraY = player.StandingCameraLocalPosition.y;
                    baselineControllerHeight = controller.height;
                    baselineRadius = controller.radius;
                    baselineRun = progression.GetSprintMultiplier();
                    baselineHead = head;
                    baselineLeftHand = leftHand;
                    baselineLeftThigh = leftThigh;
                    Require(Near(baselineRun, 1f), "Baseline sprint multiplier changed");
                }
                else
                {
                    Require(Mathf.Abs(height - baselineHeight) < baselineHeight * 0.025f,
                        $"{classId} standing height {height:F3} differs from baseline {baselineHeight:F3}");
                    Require(Near(player.StandingCameraLocalPosition.y, baselineCameraY) &&
                        Near(controller.height, baselineControllerHeight) && Near(controller.radius, baselineRadius),
                        classId + " changed the camera or collider contract");
                    // The idle clip sways head and hands, so live joints only get a loose
                    // bound; the exact skeleton contract is the bind-pose comparison below.
                    Require((head - baselineHead).magnitude < 0.2f && (leftThigh - baselineLeftThigh).magnitude < 0.05f &&
                        (leftHand - baselineLeftHand).magnitude < 0.25f,
                        $"{classId} joints moved: head {(head - baselineHead).magnitude:F3} " +
                        $"hand {(leftHand - baselineLeftHand).magnitude:F3} thigh {(leftThigh - baselineLeftThigh).magnitude:F3}");
                    RequireSameSkeleton(classId, expectedModel);
                }

                // The mirror body is the class mesh itself, on the mirror layer.
                int mirrorBodies = 0;
                Mesh baselineMesh = Resources.Load<GameObject>("Player/player_authored")
                    .GetComponentInChildren<SkinnedMeshRenderer>(true).sharedMesh;
                foreach (SkinnedMeshRenderer renderer in
                    rig.RuntimeModelRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    if (renderer.gameObject.layer != PlanarGymMirror.MirrorPlayerLayer || !renderer.enabled) continue;
                    mirrorBodies++;
                    // Scan meshes are swapped for their UV-inset copy at load.
                    Require(ScanUvInset.IsInsetOf(renderer.sharedMesh, baselineMesh) == (index == 0),
                        classId + " reflection does not use its own class body");
                }
                Require(mirrorBodies > 0 && UnityEngine.Object.FindFirstObjectByType<PlanarGymMirror>() != null,
                    classId + " has no body for the mirrors to reflect");

                if (pass == 0)
                {
                    foreach (float wait in ExerciseChecks(definition, summary)) yield return wait;
                    foreach (float wait in EveryStationSession(classId)) yield return wait;
                    Shot("class-body-" + classId, BodyView(player));
                    string slot = GymSessionService.Active.slotId;
                    foreach (float wait in SaveNow()) yield return wait;
                    foreach (float wait in ReturnToMenu()) yield return wait;
                    foreach (float wait in LoadCharacter(slot, false)) yield return wait;
                    Require(GymSessionService.Active.classId == classId, "Reload changed the class of " + classId);
                }
            }
            AssertSingletons("after reloading " + classId);
            foreach (float wait in ReturnToMenu()) yield return wait;
        }

        Debug.Log("GYMCHAOS_CLASS_BEHAVIOR_OK classes=5 reload=stable height=" + baselineHeight.ToString("F3") +
            " " + string.Join(" ", summary));
    }

    // Drives the real stations: strength timing window and cardio top pace.
    private static IEnumerable<float> ExerciseChecks(GymClassDefinition definition, List<string> summary)
    {
        GymExerciseStation dips = FindStation(GymExerciseType.Dips);
        GymExerciseStation bench = FindStation(GymExerciseType.FlatBenchPress);
        GymExerciseStation treadmill = FindStation(GymExerciseType.Treadmill);
        Transform camera = Player.playerCamera.transform;

        dips.BeginSession(camera);
        dips.TickSession(0.02f, true, false, false);
        float dipsWindow = dips.TechniqueCheck.PerfectHalfAngle;
        dips.EndSession();
        float expectedDips = 5.5f / Mathf.Max(0.75f, 1f / definition.bodyweight);
        Require(Mathf.Abs(dipsWindow - expectedDips) < 0.02f,
            $"{definition.id} dips window {dipsWindow:F3}, expected {expectedDips:F3}");
        yield return 0.1f;

        bench.BeginSession(camera);
        float load = Mathf.Lerp(0.88f, 1.3f, Mathf.InverseLerp(20f, 140f, bench.SelectedWeight));
        bench.TickSession(0.02f, true, false, false);
        float benchWindow = bench.TechniqueCheck.PerfectHalfAngle;
        bench.EndSession();
        float expectedBench = 5.5f / Mathf.Max(0.75f, load / definition.compound);
        Require(Mathf.Abs(benchWindow - expectedBench) < 0.02f,
            $"{definition.id} bench window {benchWindow:F3}, expected {expectedBench:F3}");
        yield return 0.1f;

        treadmill.BeginSession(camera);
        // Top pace only: pace gates are covered by GymChaosCardioGateVerifier.
        treadmill.UnlockCardioPaceForVerification();
        for (int i = 0; i < 40; i++) treadmill.TickSession(0.02f, false, true, false);
        for (int i = 0; i < 200; i++) treadmill.TickSession(0.1f, false, false, false);
        float topPace = treadmill.CurrentTreadmillSpeed;
        treadmill.EndSession();
        float expectedPace = 18f * definition.cardio;
        Require(Mathf.Abs(topPace - expectedPace) < 0.75f,
            $"{definition.id} treadmill top pace {topPace:F2}, expected about {expectedPace:F2}");
        summary.Add($"{definition.id}:dips={dipsWindow:F2}/bench={benchWindow:F2}/pace={topPace:F1}");
        yield return 0.2f;
    }

    private static void InvokePrivate(object target, string method, params object[] arguments)
    {
        System.Reflection.MethodInfo info = target.GetType().GetMethod(method,
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Require(info != null, "Missing method " + method);
        info.Invoke(target, arguments);
    }

    private static void Move(MoveDirection direction)
    {
        GameObject selected = EventSystem.current.currentSelectedGameObject;
        Require(selected != null, "Nothing is focused for navigation");
        var data = new AxisEventData(EventSystem.current)
        {
            moveDir = direction,
            moveVector = direction == MoveDirection.Down ? Vector2.down :
                direction == MoveDirection.Up ? Vector2.up :
                direction == MoveDirection.Left ? Vector2.left : Vector2.right
        };
        ExecuteEvents.Execute(selected, data, ExecuteEvents.moveHandler);
    }

    private static void Submit()
    {
        GameObject selected = EventSystem.current.currentSelectedGameObject;
        Require(selected != null, "Nothing is focused for submit");
        ExecuteEvents.Execute(selected, new BaseEventData(EventSystem.current), ExecuteEvents.submitHandler);
    }

    // Upright rectangles only, class picture with a corner level box, plates apart.
    private static void AssertHudLayout(GymHud hud, string classId, string level)
    {
        Require(hud != null, "HUD is missing");
        foreach (RectTransform rect in hud.LayoutRectsForVerification)
        {
            Require(Mathf.Abs(Mathf.DeltaAngle(rect.eulerAngles.z, 0f)) < 0.01f,
                "HUD element is tilted: " + rect.name);
            PersonaShape shape = rect.GetComponent<PersonaShape>();
            Require(shape == null || !shape.HasCornerOffsets, "HUD element is slanted: " + rect.name);
        }
        RawImage picture = hud.ClassPictureForVerification;
        Texture expected = Resources.Load<Texture2D>("Classes/Art/" + classId);
        Require(picture != null && picture.enabled && expected != null && picture.texture == expected,
            "HUD square does not show the " + classId + " class picture");
        Require(hud.LevelBadgeForVerification == level, "HUD level box shows " + hud.LevelBadgeForVerification);
        Transform levelBox = picture.transform.parent.Find("Level Box");
        Require(levelBox != null, "Level box is missing");
        Rect square = WorldRect((RectTransform)picture.transform.parent);
        Vector2 boxCentre = WorldRect((RectTransform)levelBox).center;
        // Small box on the bottom-right corner of the picture, not a big level panel.
        Require(boxCentre.x > square.center.x && boxCentre.y < square.center.y &&
            Mathf.Abs(boxCentre.x - square.xMax) < square.width * 0.25f &&
            Mathf.Abs(boxCentre.y - square.yMin) < square.height * 0.25f &&
            WorldRect((RectTransform)levelBox).width < square.width * 0.5f,
            "Level box is not a small box on the picture corner");
        // The box straddles the picture border but covers it half as much as the
        // original (-6, 6) inset did (about 0.64 of the box width and height).
        Rect box = WorldRect((RectTransform)levelBox);
        float overX = (square.xMax - box.xMin) / box.width;
        float overY = (box.yMax - square.yMin) / box.height;
        Require(overX > 0.2f && overX < 0.4f && overY > 0.2f && overY < 0.4f,
            $"Level box overlap with the picture is wrong: x={overX:F2} y={overY:F2}");
        RectTransform healthRow = (RectTransform)hud.transform.Find("Vitals/HP Meter");
        Require(healthRow != null && WorldRect(healthRow).xMin - (box.xMax + 3f * box.width / 42f) >
            12f * healthRow.lossyScale.x,
            "HP / stamina / members column is not padded clear of the level box");
        Require(GymHud.SprintStateText(false, true).Length == 0 &&
            GymHud.SprintStateText(true, true).Length == 0,
            "The stamina bar still shows a sprint or exhaustion text");
        if (Player != null)
        {
            GymChaosUiCapture.Capture(Player.playerCamera, "hud-" + classId + ".png", hud.GetComponent<Canvas>());
        }
        Require(hud.transform.Find("Vitals/Name Plate") == null, "Class name plate is still present");
        RectTransform levelPlate = (RectTransform)hud.transform.Find("Progress/Level Plate");
        RectTransform goalPlate = (RectTransform)hud.transform.Find("Progress/Goal Plate");
        Require(levelPlate != null && goalPlate != null && !Overlaps(Outer(levelPlate), Outer(goalPlate)),
            "Level and daily-goal plates overlap");
    }

    private static Rect Outer(RectTransform rect)
    {
        // Include the 3 px border drawn outside the plate.
        Rect world = WorldRect(rect);
        float border = 3f * rect.lossyScale.x;
        return new Rect(world.xMin - border, world.yMin - border, world.width + 2f * border, world.height + 2f * border);
    }

    private static Rect WorldRect(RectTransform rect)
    {
        var corners = new Vector3[4];
        rect.GetWorldCorners(corners);
        return Rect.MinMaxRect(corners[0].x, corners[0].y, corners[2].x, corners[2].y);
    }
    private static bool Overlaps(Rect a, Rect b) => a.Overlaps(b);

    // The prompt shadow must use the label font, or the text shows twice.
    private static void AssertPromptShadowFont(PlayerMovement player)
    {
        // IMGUI does not run in batch mode, so the shared style rule is checked directly.
        Font menuFont = PersonaMenuStyle.LoadButtonFont(null);
        Require(menuFont != null, "Menu font is missing");
        var message = new GUIStyle
        {
            font = menuFont, fontSize = 17, alignment = TextAnchor.MiddleLeft, clipping = TextClipping.Clip
        };
        var shadow = new GUIStyle { fontSize = 16, fontStyle = FontStyle.Bold };
        PlayerMovement.MirrorShadowStyle(shadow, message);
        Require(shadow.font == message.font && shadow.fontSize == message.fontSize &&
            shadow.fontStyle == message.fontStyle && shadow.alignment == message.alignment &&
            shadow.clipping == message.clipping,
            "Prompt shadow does not mirror the prompt text style");
    }

    // One real session on every kind of station: reps resolve, cardio moves, nothing throws.
    private static IEnumerable<float> EveryStationSession(string classId)
    {
        Transform camera = Player.playerCamera.transform;
        var seen = new HashSet<GymExerciseType>();
        foreach (GymExerciseStation station in UnityEngine.Object.FindObjectsByType<GymExerciseStation>(
            FindObjectsSortMode.None))
        {
            if (!seen.Add(station.ExerciseType)) continue;
            station.BeginSession(camera);
            if (station.IsCardio)
            {
                for (int i = 0; i < 6; i++) station.TickSession(0.02f, false, true, false);
                for (int i = 0; i < 40; i++) station.TickSession(0.1f, false, false, false);
                Require(station.CurrentTreadmillSpeed > 1f && station.CardioDistanceMetres > 0.5f,
                    $"{classId} could not run {station.ExerciseType}");
            }
            else
            {
                station.TickSession(0.02f, true, false, false);
                Require(station.TechniqueCheck.IsActive, $"{classId} could not start a rep on {station.ExerciseType}");
                station.TickSession(0.05f, true, false, false);
                Require(station.LastWorkoutResult != WorkoutResult.None,
                    $"{classId} rep on {station.ExerciseType} never resolved");
                for (int i = 0; i < 40; i++) station.TickSession(0.1f, false, false, false);
            }
            station.EndSession();
            yield return 0.05f;
        }
        Require(seen.Count >= 8, classId + " found only " + seen.Count + " station types");
    }

    private static GymExerciseStation FindStation(GymExerciseType type)
    {
        foreach (GymExerciseStation station in UnityEngine.Object.FindObjectsByType<GymExerciseStation>(
            FindObjectsSortMode.None))
        {
            if (station.ExerciseType == type) return station;
        }
        throw new InvalidOperationException("No station of type " + type);
    }

    // Same bones, same hierarchy, same bind matrices: limb lengths and joint
    // locations are identical by construction, independent of the live pose.
    private static void RequireSameSkeleton(string classId, string modelResource)
    {
        SkinnedMeshRenderer baseline = Resources.Load<GameObject>("Player/player_authored")
            .GetComponentInChildren<SkinnedMeshRenderer>(true);
        SkinnedMeshRenderer variant = Resources.Load<GameObject>(modelResource)
            .GetComponentInChildren<SkinnedMeshRenderer>(true);
        Require(baseline != null && variant != null, classId + " model has no skinned mesh");
        Transform[] baseBones = baseline.bones;
        Transform[] bones = variant.bones;
        Matrix4x4[] basePoses = baseline.sharedMesh.bindposes;
        Matrix4x4[] poses = variant.sharedMesh.bindposes;
        Require(bones.Length == baseBones.Length && poses.Length == basePoses.Length,
            $"{classId} has {bones.Length} bones, baseline {baseBones.Length}");
        var byName = new Dictionary<string, int>();
        for (int i = 0; i < baseBones.Length; i++) byName[baseBones[i].name] = i;
        float worst = 0f;
        for (int i = 0; i < bones.Length; i++)
        {
            Require(byName.TryGetValue(bones[i].name, out int match), classId + " has an extra bone " + bones[i].name);
            Transform baseParent = baseBones[match].parent;
            Require(bones[i].parent != null && baseParent != null && bones[i].parent.name == baseParent.name,
                classId + " re-parented bone " + bones[i].name);
            for (int element = 0; element < 16; element++)
            {
                worst = Mathf.Max(worst, Mathf.Abs(poses[i][element] - basePoses[match][element]));
            }
        }
        Require(worst < 0.0005f, $"{classId} skeleton differs from the baseline by {worst:F5}");
        Require(variant.sharedMesh.subMeshCount == baseline.sharedMesh.subMeshCount &&
            variant.sharedMesh.uv.Length == variant.sharedMesh.vertexCount &&
            variant.sharedMaterials.Length == baseline.sharedMaterials.Length,
            classId + " changed sub-meshes, UVs or material slots");
    }

    private static bool Near(float a, float b) => Mathf.Abs(a - b) < 0.0005f * Mathf.Max(1f, Mathf.Abs(b));

    private static bool Equal(byte[] a, byte[] b)
    {
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
        return true;
    }

    // ---- captures (graphics runs only) ----

    private struct View
    {
        public Vector3 position;
        public Quaternion rotation;
        public bool valid;
    }

    private static View BodyView(PlayerMovement player)
    {
        Vector3 target = player.transform.position + Vector3.up * 0.15f;
        Vector3 position = target + player.transform.forward * 3.4f + player.transform.right * 1.2f;
        return new View { position = position, rotation = Quaternion.LookRotation(target - position), valid = true };
    }

    private static void Shot(string label, View view = default)
    {
        if (!capture) return;
        PlayerMovement player = Player;
        if (player == null || player.playerCamera == null) return;
        Camera source = player.playerCamera;
        int[][] sizes = view.valid
            ? new[] { new[] { 1280, 1280 } }
            : new[] { new[] { 1920, 1080 }, new[] { 1680, 1050 }, new[] { 2560, 1080 }, new[] { 1280, 720 } };
        Canvas[] canvases = UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None);
        var modes = new RenderMode[canvases.Length];
        Vector3 previousPosition = source.transform.position;
        Quaternion previousRotation = source.transform.rotation;
        int previousMask = source.cullingMask;
        RenderTexture previousTarget = source.targetTexture;
        float previousTimeScale = Time.timeScale;
        for (int i = 0; i < sizes.Length; i++)
        {
            int width = sizes[i][0];
            int height = sizes[i][1];
            RenderTexture target = new RenderTexture(width, height, 24);
            for (int c = 0; c < canvases.Length; c++)
            {
                modes[c] = canvases[c].renderMode;
                if (!canvases[c].isRootCanvas || canvases[c].renderMode != RenderMode.ScreenSpaceOverlay) continue;
                if (view.valid) { canvases[c].enabled = false; continue; }
                canvases[c].renderMode = RenderMode.ScreenSpaceCamera;
                canvases[c].worldCamera = source;
                canvases[c].planeDistance = Mathf.Max(source.nearClipPlane + 0.05f, 0.3f) + 0.001f * (300 - canvases[c].sortingOrder);
            }
            if (view.valid)
            {
                source.transform.SetPositionAndRotation(view.position, view.rotation);
                source.cullingMask |= 1 << PlanarGymMirror.MirrorPlayerLayer;
                source.cullingMask &= ~(1 << PlanarGymMirror.FirstPersonPlayerLayer);
            }
            source.targetTexture = target;
            Canvas.ForceUpdateCanvases();
            Time.timeScale = 1f;
            source.Render();
            Time.timeScale = previousTimeScale;
            RenderTexture.active = target;
            Texture2D image = new Texture2D(width, height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            image.Apply();
            RenderTexture.active = null;
            source.targetTexture = previousTarget;
            for (int c = 0; c < canvases.Length; c++)
            {
                if (!canvases[c].isRootCanvas) continue;
                canvases[c].enabled = true;
                canvases[c].renderMode = modes[c];
            }
            string suffix = i == 0 ? string.Empty : "-" + width + "x" + height;
            string path = Path.Combine(Directory.GetParent(Application.dataPath).Parent.FullName,
                "Logs", "agent", "classes-" + label + suffix + ".png");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllBytes(path, image.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(image);
            target.Release();
            UnityEngine.Object.DestroyImmediate(target);
        }
        source.transform.SetPositionAndRotation(previousPosition, previousRotation);
        source.cullingMask = previousMask;
    }

    // ---- one real game as a guest against a scripted host, then as the host of a scripted guest ----

    private static System.Diagnostics.Process netRelay;
    private static GymNetClient scripted;

    private static System.Diagnostics.Process StartNetRelay(int port)
    {
        string script = null;
        for (string probe = Directory.GetParent(Application.dataPath).FullName; probe != null && script == null; probe = Directory.GetParent(probe)?.FullName)
        {
            string candidate = Path.Combine(probe, "Server", "relay", "relay.js");
            if (File.Exists(candidate)) script = candidate;
        }
        Require(script != null, "Server/relay/relay.js not found");
        var info = new System.Diagnostics.ProcessStartInfo("node", "\"" + script + "\"")
        {
            UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(script)
        };
        info.Environment["PORT"] = port.ToString();
        System.Diagnostics.Process process = System.Diagnostics.Process.Start(info);
        string line = null;
        var reader = new System.Threading.Thread(() => line = process.StandardOutput.ReadLine());
        reader.Start();
        Require(reader.Join(10000) && line != null && line.Contains("GYMCHAOS_RELAY_LISTENING"), "relay did not start: " + line);
        return process;
    }

    // Like Until, and keeps the scripted client's socket moving.
    private static IEnumerable<float> NetUntil(Func<bool> condition, double seconds, string what)
    {
        double deadline = EditorApplication.timeSinceStartup + seconds;
        while (!condition())
        {
            if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException("Timed out waiting for " + what);
            scripted?.Tick();
            yield return 0f;
        }
    }

    private static IEnumerable<float> NetWait(double seconds)
    {
        double end = EditorApplication.timeSinceStartup + seconds;
        foreach (float wait in NetUntil(() => EditorApplication.timeSinceStartup >= end, seconds + 5d, "a pause")) yield return wait;
    }

    private static IEnumerator<float> NetDeathScenario()
    {
        foreach (float wait in WaitForBootMenu()) yield return wait;
        netRelay = StartNetRelay(18792);
        const string url = "ws://127.0.0.1:18792";
        var observed = new List<GymNetPose>();
        scripted = new GymNetClient();
        GymNetJoin scriptedJoin = new GymNetJoin(scripted) { WorldSource = GymSessionService.CaptureWorldSnapshot };
        scripted.MessageReceived += (from, body) =>
        {
            if (scriptedJoin.Handle(from, body)) return;
            if (body.Length > 1 && body[0] == 'p') observed.Add(JsonUtility.FromJson<GymNetPose>(body.Substring(1)));
        };
        scripted.Host(url, Guid.NewGuid().ToString("N"));
        foreach (float wait in NetUntil(() => scripted.CurrentPhase == GymNetClient.Phase.Lobby, 20d, "the scripted host's room")) yield return wait;

        // Part 1: this game is a guest. Through the real menu with the room set to JOIN.
        GymNetSession.SetPending("join", url, scripted.RoomCode);
        GymNetReconnect.LimitOverrideSeconds = 4f;
        foreach (float wait in StartNewCharacter("cardio")) yield return wait;
        Require(GymSessionService.Context == GymSaveContext.Join, "a joining game is not in the Join context");
        foreach (float wait in NetUntil(() => NetSession != null && NetSession.Join.Guest == GymNetJoin.GuestState.Joined &&
            NetSession.ActorsSuppressed && scriptedJoin.LastKnown.ContainsKey(1) && observed.Count >= 3, 90d, "the join")) yield return wait;
        PlayerMovement guest = Player;
        // Progress after the last save: LOAD GAME must take it away again.
        foreach (float wait in NetUntil(() => GymSessionService.CompletedWriteCount >= 1 && !GymSessionService.IsWriting, 30d, "the first save")) yield return wait;
        GymExperienceService.Active.State.level = 5;
        SetPlayerHealth(guest, 10f);
        observed.Clear();
        // Even paused: a death in a room does not freeze the game behind its menu.
        Time.timeScale = 0f;
        // Damage from another player kills: there is no 1 health floor.
        scripted.Send(1, GymNetPvp.DamageTag + "{\"amount\":25.0,\"ix\":0.0,\"iy\":0.0,\"iz\":0.0}");
        foreach (float wait in NetUntil(() => guest.IsDead, 10d, "a punch from another player to kill")) yield return wait;
        Vector3 deathPosition = guest.transform.position;
        foreach (float wait in NetWait(1d)) yield return wait;
        Require(observed.Count > 0 && observed[observed.Count - 1].d > 0.5f, "the others were not told the guest died");
        foreach (float wait in NetUntil(() => GymPauseMenu.IsDeathMenuVisible, 15d, "the death menu")) yield return wait;
        Require(NetSession != null && NetSession.Client.CurrentPhase == GymNetClient.Phase.Lobby, "the guest left the room when it died");
        Require(GymSessionService.HasActive, "the character was dropped on death");
        Require(Time.timeScale == 1f, "the death menu left the game frozen");
        yield return 0.5f;
        Shot("net-death-menu");
        observed.Clear();
        Require(GymNetSession.RespawnLocalPlayer(), "LOAD GAME in the death menu did nothing");
        foreach (float wait in NetWait(1.2d)) yield return wait;
        Require(!guest.IsDead && guest.CurrentHealth > 1f, "LOAD GAME did not bring the guest back alive");
        Require(Vector3.Distance(guest.transform.position, deathPosition) < 0.75f, "respawned away from where it died");
        Require(!GymPauseMenu.IsDeathMenuVisible, "the death menu stayed open");
        Require(GymExperienceService.Active.State.level == 1, "the character was not loaded from its save (level " + GymExperienceService.Active.State.level + ")");
        Require(GymSessionService.Context == GymSaveContext.Join && NetSession.Client.CurrentPhase == GymNetClient.Phase.Lobby, "the guest left Join or the room");
        Require(observed.Count > 0 && observed[observed.Count - 1].d < 0.5f, "the others still see the guest dead");
        int writes = GymSessionService.CompletedWriteCount;
        GymSessionService.RequestSave("manual", true);
        foreach (float wait in NetUntil(() => GymSessionService.CompletedWriteCount > writes && !GymSessionService.IsWriting, 20d, "the save")) yield return wait;
        GymSaveReadResult read = GymSessionService.Repository.Read(GymSessionService.Active.slotId);
        Require(read.Success && !read.Data.hasWorld && read.Data.worldRevision == 0, "a guest save wrote the world section");

        // Part 1b: the host goes and never comes back. The guest gives up after its limit and the menu says why.
        scripted.Disconnect();
        foreach (float wait in NetUntil(() => NetSession != null && NetSession.Reconnect != null && NetSession.Reconnect.Active, 20d, "the guest to start reconnecting")) yield return wait;
        foreach (float wait in NetUntil(() => !GymArenaBootstrap.IsGameplayStarted && !GymSessionService.HasActive, 40d, "the guest to give up and return to the menu")) yield return wait;
        GymNetReconnect.LimitOverrideSeconds = 0f;
        foreach (float wait in WaitForBootMenu()) yield return wait;
        foreach (float wait in OpenSessionChoice()) yield return wait;
        Require(Menu.ChoiceNoteText.StartsWith("HOST NOT BACK"), "the menu does not say why the room ended: '" + Menu.ChoiceNoteText + "'");
        Menu.Back();
        yield return Step;
        // Part 1c: a code that does not exist: the game says so and goes back to the menu.
        GymNetSession.SetPending("join", url, "QQQQ");
        foreach (float wait in StartNewCharacter("cardio")) yield return wait;
        foreach (float wait in NetUntil(() => !GymArenaBootstrap.IsGameplayStarted && !GymSessionService.HasActive, 30d, "the failed join to return to the menu")) yield return wait;
        foreach (float wait in WaitForBootMenu()) yield return wait;
        foreach (float wait in OpenSessionChoice()) yield return wait;
        Require(Menu.ChoiceNoteText.StartsWith("ROOM FAILED"), "the menu does not say the room failed: '" + Menu.ChoiceNoteText + "'");
        Menu.Back();
        yield return Step;
        scripted = new GymNetClient();
        GymNetSession.SetPending(null, null, null);
        foreach (float wait in StartNewCharacter("bodybuilding")) yield return wait;
        guest = Player;

        // Part 2: this game is the host and a scripted guest dies; the corpse stays until it respawns.
        GymNetSession.End();
        GymSessionService.SetContext(GymSaveContext.Solo);
        GymNetSession host = GymNetSession.Begin(url, null);
        foreach (float wait in NetUntil(() => host.Client.CurrentPhase == GymNetClient.Phase.Lobby, 20d, "the host room")) yield return wait;
        scripted = new GymNetClient();
        scripted.Join(url, host.Client.RoomCode, Guid.NewGuid().ToString("N"));
        foreach (float wait in NetUntil(() => scripted.CurrentPhase == GymNetClient.Phase.Lobby && host.RemoteAvatarCount == 1, 20d, "the scripted guest")) yield return wait;
        Vector3 at = guest.transform.position + guest.transform.right * 4f;
        scripted.Send(0, "p" + JsonUtility.ToJson(new GymNetPose { x = at.x, y = at.y, z = at.z, h = 0f, d = 1f }));
        foreach (float wait in NetWait(1.5d)) yield return wait;
        host.TryGetAvatar(1, out GymRemoteAvatar corpse);
        Require(corpse != null && corpse.IsCorpse && Vector3.Distance(corpse.transform.position, at) < 0.5f, "the dead guest is not a corpse where it fell");
        Shot("net-death-corpse", LookAt(at));
        scripted.Send(0, "p" + JsonUtility.ToJson(new GymNetPose { x = at.x, y = at.y, z = at.z, h = 1f, d = 0f }));
        foreach (float wait in NetWait(1.5d)) yield return wait;
        Require(!corpse.IsCorpse, "the corpse stayed after the guest respawned");

        // Three players: guest 1 punches guest 2, the host decides and only guest 2 is told; a dead guest cannot punch.
        GymNetClient second = new GymNetClient();
        var secondDamage = new List<string>();
        second.MessageReceived += (from, body) => { if (body.Length > 0 && body[0] == GymNetPvp.DamageTag) secondDamage.Add(from + ":" + body); };
        var firstDamage = new List<string>();
        scripted.MessageReceived += (from, body) => { if (body.Length > 0 && body[0] == GymNetPvp.DamageTag) firstDamage.Add(body); };
        second.Join(url, host.Client.RoomCode, Guid.NewGuid().ToString("N"));
        double joinEnd = EditorApplication.timeSinceStartup + 20d;
        while (!(second.CurrentPhase == GymNetClient.Phase.Lobby && host.RemoteAvatarCount == 2))
        {
            if (EditorApplication.timeSinceStartup > joinEnd) throw new TimeoutException("the third player");
            second.Tick();
            scripted.Tick();
            yield return 0f;
        }
        Vector3 firstAt = guest.transform.position + guest.transform.forward * 5f;
        Vector3 secondAt = firstAt + Vector3.right * 1.5f;
        scripted.Send(0, "p" + JsonUtility.ToJson(new GymNetPose { x = firstAt.x, y = firstAt.y, z = firstAt.z, r = 90f, h = 1f }));
        second.Send(0, "p" + JsonUtility.ToJson(new GymNetPose { x = secondAt.x, y = secondAt.y, z = secondAt.z, r = 270f, h = 1f }));
        double settle = EditorApplication.timeSinceStartup + 1.2d;
        while (EditorApplication.timeSinceStartup < settle) { second.Tick(); scripted.Tick(); yield return 0f; }
        scripted.Send(0, GymNetPvp.AttackTag + "{\"dx\":1.0,\"dy\":0.0,\"dz\":0.0}");
        double hitEnd = EditorApplication.timeSinceStartup + 5d;
        while (secondDamage.Count == 0)
        {
            if (EditorApplication.timeSinceStartup > hitEnd) throw new TimeoutException("guest 1 punching guest 2");
            second.Tick();
            scripted.Tick();
            yield return 0f;
        }
        Require(secondDamage[0].StartsWith("0:") && firstDamage.Count == 0, "a guest-against-guest punch reached the wrong player: " + secondDamage[0]);
        // Guest 1 lies dead: its punches count for nothing.
        scripted.Send(0, "p" + JsonUtility.ToJson(new GymNetPose { x = firstAt.x, y = firstAt.y, z = firstAt.z, r = 90f, h = 0f, d = 1f }));
        double deadEnd = EditorApplication.timeSinceStartup + 1.5d;
        while (EditorApplication.timeSinceStartup < deadEnd) { second.Tick(); scripted.Tick(); yield return 0f; }
        secondDamage.Clear();
        scripted.Send(0, GymNetPvp.AttackTag + "{\"dx\":1.0,\"dy\":0.0,\"dz\":0.0}");
        double quietEnd = EditorApplication.timeSinceStartup + 1.5d;
        while (EditorApplication.timeSinceStartup < quietEnd) { second.Tick(); scripted.Tick(); yield return 0f; }
        Require(secondDamage.Count == 0, "a dead guest landed a punch");
        second.Disconnect();
        Debug.Log("GYMCHAOS_NET_DEATH_OK pvpKills=yes replicated=yes menu=yes respawn=in-place level=from-save world=untouched corpse=until-respawn guestVsGuest=host-decides deadCannotPunch=yes");
    }

    // ---- two real game processes: one hosts, one joins (see Tools/Invoke-NetTwoProcess.ps1) ----

    private const string NetUrl = "ws://127.0.0.1:18793";

    private static string SyncDir => Environment.GetEnvironmentVariable("GYMCHAOS_NET_SYNC_DIR");

    private static void SyncWrite(string name, string text = "1") =>
        File.WriteAllText(Path.Combine(SyncDir, name), text);

    private static bool SyncHas(string name) => File.Exists(Path.Combine(SyncDir, name));

    private static IEnumerable<float> SyncWait(string name, double seconds)
    {
        foreach (float wait in Until(() => SyncHas(name), seconds, "the other process: " + name)) yield return wait;
    }

    private static GymNetSession NetSession => GymNetSession.Instance;

    private static void SetPlayerHealth(PlayerMovement player, float value) =>
        typeof(PlayerMovement).GetField("currentHealth", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            .SetValue(player, value);

    private static View LookAt(Vector3 point, float distance = 3.5f, float height = 1.4f)
    {
        Vector3 position = point + new Vector3(distance * 0.6f, height, -distance * 0.8f);
        return new View { position = position, rotation = Quaternion.LookRotation(point - position), valid = true };
    }

    private static int CountNamed(string name)
    {
        int count = 0;
        foreach (GameObject candidate in UnityEngine.Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None))
            if (candidate.name == name) count++;
        return count;
    }

    // Host: a real new character in a real room, the guest walks in, they fight, the guest dies and
    // respawns, then the host dies, loads its save and hosts the same code again.
    private static IEnumerator<float> NetHostScenario()
    {
        foreach (float wait in WaitForBootMenu()) yield return wait;
        GymNetSession.SetPending("host", NetUrl, null);
        foreach (float wait in StartNewCharacter("bodybuilding")) yield return wait;
        foreach (float wait in Until(() => NetSession != null && NetSession.Client.CurrentPhase == GymNetClient.Phase.Lobby, 60d, "the room")) yield return wait;
        Require(GymSessionService.Context == GymSaveContext.Host, "a hosting game is not in the Host context");
        string code = NetSession.Client.RoomCode;
        SyncWrite("code", code);
        Debug.Log("GYMCHAOS_NET_HOST_ROOM code=" + code);
        foreach (float wait in Until(() => NetSession.RemoteAvatarCount == 1 && NetSession.TryGetAvatar(1, out GymRemoteAvatar a) && a.PoseCount > 0, 240d, "the guest")) yield return wait;
        foreach (float wait in SyncWait("guest-ready", 240d)) yield return wait;
        yield return 1f;
        NetSession.TryGetAvatar(1, out GymRemoteAvatar guestAvatar);
        Shot("net-host-1-guest-avatar", LookAt(guestAvatar.transform.position));
        Debug.Log("GYMCHAOS_NET_HOST_SEES_GUEST position=" + guestAvatar.transform.position);

        // World actions that depend on both: glass breaking and being repaired by the host's fixer, and the day clock.
        GlassShatterPanel glass = null;
        foreach (GlassShatterPanel candidate in UnityEngine.Object.FindObjectsByType<GlassShatterPanel>(FindObjectsSortMode.None))
            if (!candidate.IsShattered) { glass = candidate; break; }
        Require(glass != null, "no intact glass panel for the coop glass test");
        string glassId = glass.StableId;
        glass.ShatterFromPowerImpact(glass.transform.position, glass.transform.forward, glass.transform.forward * 12f);
        Require(glass.IsShattered, "the host's own glass did not break");
        SyncWrite("glass-id", glassId);
        foreach (float wait in SyncWait("guest-saw-glass", 60d)) yield return wait;
        Require(glass.RepairByFixer(), "the host could not repair the glass");
        SyncWrite("glass-repaired");
        foreach (float wait in SyncWait("guest-saw-repair", 60d)) yield return wait;
        GymTimeOfDay.Instance.RestoreSession(0.77f, 7);
        SyncWrite("clock-set");
        foreach (float wait in SyncWait("guest-saw-clock", 60d)) yield return wait;
        // The other way round: the guest breaks glass, the host decides and both see it.
        foreach (float wait in SyncWait("guest-glass-id", 60d)) yield return wait;
        string guestGlassId = File.ReadAllText(Path.Combine(SyncDir, "guest-glass-id")).Trim();
        foreach (float wait in Until(() => GlassShatterPanel.FindById(guestGlassId) != null && GlassShatterPanel.FindById(guestGlassId).IsShattered, 30d,
            "the glass the guest broke")) yield return wait;
        SyncWrite("host-saw-guest-glass");
        Debug.Log("GYMCHAOS_NET_HOST_WORLD_ACTIONS_OK glass=" + glassId + " guestGlass=" + guestGlassId);

        // PvP: walk up to the guest and punch.
        PlayerMovement host = Player;
        Vector3 stand = guestAvatar.transform.position + Vector3.right * 1.8f;
        host.RestoreSessionState(true, stand, 270f, host.MaxHealth, 100f);
        yield return 1.2f;
        for (int i = 0; i < 6; i++)
        {
            Vector3 eye = host.transform.position + Vector3.up * GymNetPvp.EyeAboveRoot;
            Vector3 aim = (guestAvatar.transform.position - eye).normalized;
            GymNetPvp.LocalPunch(eye, aim);
            yield return 0.35f;
        }
        SyncWrite("pvp-done");
        foreach (float wait in SyncWait("guest-hurt", 60d)) yield return wait;
        foreach (float wait in SyncWait("guest-low", 60d)) yield return wait;
        // Finish the guest off.
        for (int i = 0; i < 12 && !guestAvatar.IsCorpse; i++)
        {
            Vector3 eye = host.transform.position + Vector3.up * GymNetPvp.EyeAboveRoot;
            GymNetPvp.LocalPunch(eye, (guestAvatar.transform.position - eye).normalized);
            yield return 0.4f;
        }
        foreach (float wait in Until(() => guestAvatar.IsCorpse, 20d, "the guest's corpse")) yield return wait;
        yield return 1f;
        Shot("net-host-2-guest-corpse", LookAt(guestAvatar.transform.position));
        Debug.Log("GYMCHAOS_NET_HOST_SEES_CORPSE");
        SyncWrite("host-saw-corpse");
        foreach (float wait in SyncWait("guest-respawned", 90d)) yield return wait;
        foreach (float wait in Until(() => !guestAvatar.IsCorpse, 30d, "the guest standing again")) yield return wait;
        yield return 1f;
        Shot("net-host-3-guest-standing", LookAt(guestAvatar.transform.position));
        Debug.Log("GYMCHAOS_NET_HOST_SEES_RESPAWN");

        // The host dies: solo flow, then the same room again.
        foreach (float wait in SaveNow()) yield return wait;
        string slotId = GymSessionService.Active.slotId;
        SyncWrite("host-dying");
        Player.ReceiveEnemyPunch(100000f, Vector3.zero, null);
        yield return Step;
        Require(Player.IsDead, "lethal damage did not kill the host");
        foreach (float wait in Until(() => GymStartScreen.IsMenuVisible && Menu != null &&
            Menu.Current == GymSessionMenu.State.SaveList, 60d, "the save list after the host died")) yield return wait;
        Require(GymNetSession.HasRecovery && GymNetSession.RecoveryCode == code, "the room was not remembered through the host's death");
        Shot("net-host-4-save-list");
        yield return 0.5f;
        Menu.SelectSave(slotId);
        yield return Step;
        Menu.ConfirmLoad();
        foreach (float wait in Until(() => GymArenaBootstrap.IsGameplayStarted, 60d, "the host's world again")) yield return wait;
        foreach (float wait in Until(() => NetSession != null && NetSession.Client.CurrentPhase == GymNetClient.Phase.Lobby, 60d, "the room again")) yield return wait;
        Require(NetSession.Client.RoomCode == code, "the room came back as " + NetSession.Client.RoomCode + ", not " + code);
        Require(GymSessionService.Context == GymSaveContext.Host && !GymNetSession.HasRecovery, "host context or recovery wrong after reload");
        SyncWrite("host-back", NetSession.Client.RoomCode);
        Debug.Log("GYMCHAOS_NET_HOST_REHOSTED code=" + NetSession.Client.RoomCode);
        foreach (float wait in Until(() => NetSession.RemoteAvatarCount == 1 && NetSession.TryGetAvatar(1, out GymRemoteAvatar a) && a.PoseCount > 0, 180d, "the guest to rejoin")) yield return wait;
        foreach (float wait in SyncWait("guest-rejoined", 120d)) yield return wait;
        yield return 1.5f;
        Require(CountNamed("Remote Player 1") == 1, "avatars for the guest: " + CountNamed("Remote Player 1"));
        NetSession.TryGetAvatar(1, out GymRemoteAvatar again);
        Shot("net-host-5-guest-rejoined", LookAt(again.transform.position));
        SyncWrite("host-checked");
        // The guest leaves through MAIN MENU: its avatar goes.
        foreach (float wait in Until(() => NetSession.RemoteAvatarCount == 0, 120d, "the guest leaving")) yield return wait;
        Debug.Log("GYMCHAOS_NET_TWO_PROCESS_HOST_OK code=" + code + " pvp=ok guestDeath=seen respawn=seen hostDeath=same-code rejoin=ok avatars=1 leave=ok");
    }

    // Guest: joins by code with its own new character, is hit, dies, uses the death menu, survives the host going down.
    private static IEnumerator<float> NetGuestScenario()
    {
        foreach (float wait in WaitForBootMenu()) yield return wait;
        foreach (float wait in SyncWait("code", 360d)) yield return wait;
        string code = File.ReadAllText(Path.Combine(SyncDir, "code")).Trim();
        GymNetSession.SetPending("join", NetUrl, code);
        foreach (float wait in StartNewCharacter("cardio")) yield return wait;
        Require(GymSessionService.Context == GymSaveContext.Join, "a joining game is not in the Join context");
        foreach (float wait in Until(() => NetSession != null && NetSession.Client.CurrentPhase == GymNetClient.Phase.Lobby &&
            NetSession.Join.Guest == GymNetJoin.GuestState.Joined && NetSession.ActorsSuppressed && NetSession.Puppets.Count > 0, 240d,
            "the host's world")) yield return wait;
        Require(NetSession.Client.LocalId == 1, "guest id " + NetSession.Client.LocalId);
        Debug.Log("GYMCHAOS_NET_GUEST_IN_WORLD puppets=" + NetSession.Puppets.Count);
        yield return 1f;
        Shot("net-guest-1-world-with-puppets");
        SyncWrite("guest-ready");

        // The host breaks and repairs glass and sets the clock: this world follows.
        foreach (float wait in SyncWait("glass-id", 120d)) yield return wait;
        string glassId = File.ReadAllText(Path.Combine(SyncDir, "glass-id")).Trim();
        foreach (float wait in Until(() => GlassShatterPanel.FindById(glassId) != null && GlassShatterPanel.FindById(glassId).IsShattered, 30d, "the host's glass to break here")) yield return wait;
        SyncWrite("guest-saw-glass");
        foreach (float wait in SyncWait("glass-repaired", 60d)) yield return wait;
        foreach (float wait in Until(() => !GlassShatterPanel.FindById(glassId).IsShattered, 30d, "the host's repair to reach this glass")) yield return wait;
        SyncWrite("guest-saw-repair");
        foreach (float wait in SyncWait("clock-set", 60d)) yield return wait;
        foreach (float wait in Until(() => GymTimeOfDay.Instance.CurrentDay == 7 && Mathf.Abs(GymTimeOfDay.Instance.Time01 - 0.77f) < 0.03f, 30d,
            "the host's day clock")) yield return wait;
        SyncWrite("guest-saw-clock");
        // The guest breaks glass near itself: its own copy stays whole until the host decides.
        GlassShatterPanel nearest = null;
        float nearestDistance = float.MaxValue;
        foreach (GlassShatterPanel candidate in UnityEngine.Object.FindObjectsByType<GlassShatterPanel>(FindObjectsSortMode.None))
        {
            float d = Vector3.Distance(candidate.transform.position, Player.transform.position);
            if (!candidate.IsShattered && d < nearestDistance) { nearest = candidate; nearestDistance = d; }
        }
        Require(nearest != null && nearestDistance < GymNetInteractions.MaxReach - 2f, "no glass within the host's reach of the guest (" + nearestDistance + ")");
        string guestGlassId = nearest.StableId;
        nearest.ShatterFromPowerImpact(nearest.transform.position, nearest.transform.forward, nearest.transform.forward * 12f);
        Require(!nearest.IsShattered, "the guest's glass broke before the host decided");
        SyncWrite("guest-glass-id", guestGlassId);
        foreach (float wait in Until(() => GlassShatterPanel.FindById(guestGlassId).IsShattered, 30d, "the host to break the glass the guest asked for")) yield return wait;
        foreach (float wait in SyncWait("host-saw-guest-glass", 60d)) yield return wait;
        Debug.Log("GYMCHAOS_NET_GUEST_WORLD_ACTIONS_OK");

        foreach (float wait in SyncWait("pvp-done", 240d)) yield return wait;
        PlayerMovement guest = Player;
        foreach (float wait in Until(() => guest.CurrentHealth < guest.MaxHealth - 1f, 30d, "damage from the host's punches")) yield return wait;
        Debug.Log("GYMCHAOS_NET_GUEST_HURT health=" + guest.CurrentHealth);
        Shot("net-guest-2-hurt");
        SyncWrite("guest-hurt");
        SetPlayerHealth(guest, 10f);
        SyncWrite("guest-low");
        foreach (float wait in Until(() => guest.IsDead, 60d, "the guest's death")) yield return wait;
        Vector3 deathPosition = guest.transform.position;
        Debug.Log("GYMCHAOS_NET_GUEST_DIED position=" + deathPosition);
        foreach (float wait in Until(() => GymPauseMenu.IsDeathMenuVisible, 30d, "the death menu")) yield return wait;
        Require(NetSession != null && NetSession.Client.CurrentPhase == GymNetClient.Phase.Lobby, "the guest left the room when it died");
        yield return 0.7f;
        Shot("net-guest-3-death-menu");
        foreach (float wait in SyncWait("host-saw-corpse", 120d)) yield return wait;
        Require(GymNetSession.RespawnLocalPlayer(), "LOAD GAME in the death menu did nothing");
        yield return 0.8f;
        Require(!guest.IsDead && Vector3.Distance(guest.transform.position, deathPosition) < 0.75f, "the guest did not respawn where it died");
        Require(!GymPauseMenu.IsDeathMenuVisible, "the death menu stayed open after LOAD GAME");
        Shot("net-guest-4-respawned");
        SyncWrite("guest-respawned");
        Debug.Log("GYMCHAOS_NET_GUEST_RESPAWNED");

        // The host dies and reloads: this guest waits and rejoins.
        foreach (float wait in SyncWait("host-dying", 180d)) yield return wait;
        foreach (float wait in Until(() => NetSession != null && NetSession.Reconnect != null && NetSession.Reconnect.Active, 60d, "the guest to notice the host is down")) yield return wait;
        yield return 1f;
        Shot("net-guest-5-host-down");
        Debug.Log("GYMCHAOS_NET_GUEST_HOST_DOWN");
        Require(!guest.IsDead && GymSessionService.HasActive, "the guest lost its character while the host was down");
        foreach (float wait in SyncWait("host-back", 360d)) yield return wait;
        foreach (float wait in Until(() => NetSession != null && NetSession.Client.CurrentPhase == GymNetClient.Phase.Lobby &&
            !NetSession.Reconnect.Active && NetSession.Join.Guest == GymNetJoin.GuestState.Joined, 240d, "the automatic rejoin")) yield return wait;
        Require(NetSession.Client.RoomCode == code, "rejoined room " + NetSession.Client.RoomCode);
        yield return 1.5f;
        Shot("net-guest-6-rejoined");
        Debug.Log("GYMCHAOS_NET_GUEST_REJOINED");
        SyncWrite("guest-rejoined");

        // The guest never wrote the host's world into its own save.
        foreach (float wait in SaveNow()) yield return wait;
        GymSaveReadResult read = GymSessionService.Repository.Read(GymSessionService.Active.slotId);
        Require(read.Success && !read.Data.hasWorld && read.Data.worldRevision == 0, "the guest save holds a world section");
        // MAIN MENU leaves the room (after the host has looked at the rejoined guest).
        foreach (float wait in SyncWait("host-checked", 120d)) yield return wait;
        yield return 1f;
        GymSessionService.ReturnToMenu();
        yield return 2f;
        Debug.Log("GYMCHAOS_NET_TWO_PROCESS_GUEST_OK world=untouched death=menu+respawn hostDown=reconnected leave=ok");
    }
}
