#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// One save file per character with a character and a world section.
/// Fixtures live in a temporary directory; no real character file is touched.
/// - Migration: the PlayerPrefs v1 progression becomes a character once; a
///   version 1 file keeps its world.
/// - Solo and Host write both sections; Join writes the character section
///   only and leaves the world section byte for byte as loaded.
/// - A write that fails validation keeps the old file; a stale temp file is harmless.
/// - Autosave and manual save pick the same sections.
/// </summary>
public static class GymChaosSaveSplitVerifier
{
    private const string LegacyMigratedKey = "GymChaos.LegacyMigrated.v1";
    private const string LegacyProgressionKey = "GymChaos.Progression.v1";
    private static readonly string[] ClassIds =
        { "bodybuilding", "calisthenics", "cardio", "powerlifting", "strongman" };

    public static void Run()
    {
        string directory = Path.Combine(Path.GetTempPath(), "GymChaosSaveSplit-" + Guid.NewGuid().ToString("N"));
        int backupMigrated = PlayerPrefs.GetInt(LegacyMigratedKey, 0);
        string backupLegacy = PlayerPrefs.GetString(LegacyProgressionKey, string.Empty);
        try
        {
            Environment.SetEnvironmentVariable(GymSessionService.VerificationDirectoryVariable, directory);
            VerifyMigration(directory);
            VerifySections(directory);
            VerifyCrash(directory);
            VerifyRules();
            Debug.Log("GYMCHAOS_SAVE_SPLIT_OK migration=once solo=both join=character-only crash=preserved autosave=manual");
            Finish(0, backupMigrated, backupLegacy, directory);
        }
        catch (Exception exception)
        {
            Debug.LogError("GYMCHAOS_SAVE_SPLIT_FAILED " + exception);
            Finish(1, backupMigrated, backupLegacy, directory);
        }
    }

    private static void Finish(int code, int migrated, string legacy, string directory)
    {
        Environment.SetEnvironmentVariable(GymSessionService.VerificationDirectoryVariable, null);
        PlayerPrefs.SetInt(LegacyMigratedKey, migrated);
        if (string.IsNullOrEmpty(legacy)) PlayerPrefs.DeleteKey(LegacyProgressionKey);
        else PlayerPrefs.SetString(LegacyProgressionKey, legacy);
        if (Directory.Exists(directory)) Directory.Delete(directory, true);
        EditorApplication.Exit(code);
    }

    private static void VerifyMigration(string directory)
    {
        PlayerPrefs.SetString(LegacyProgressionKey, JsonUtility.ToJson(new GymProgressionState { level = 9, totalExperience = 500 }));
        PlayerPrefs.SetInt(LegacyMigratedKey, 0);
        GymSessionService.MigrateLegacyProgression();
        GymSessionService.MigrateLegacyProgression();
        var saves = new GymSaveRepository(directory, ClassIds).List();
        Require(saves.Count == 1, $"legacy PlayerPrefs migrated {saves.Count} times");
        GymCharacterSave migrated = saves[0].Result.Data;
        Require(migrated.progression.level == 9 && migrated.progression.totalExperience == 500, "legacy progress lost");
        Require(!migrated.hasWorld, "migrated legacy character claims a world");
        Require(migrated.version == GymCharacterSave.CurrentVersion, "migrated save not on the current version");

        // A version 1 file stored the world next to the character.
        var repository = new GymSaveRepository(directory, ClassIds);
        string id = Guid.NewGuid().ToString("N");
        string now = DateTime.UtcNow.ToString("O");
        string payload = "{\"version\":1,\"slotId\":\"" + id + "\",\"classId\":\"cardio\",\"createdUtc\":\"" + now +
            "\",\"savedUtc\":\"" + now + "\",\"worldTime\":0.6,\"worldDay\":4,\"progression\":{\"level\":2}}";
        File.WriteAllText(Path.Combine(directory, id + ".json"), GymChaosClassSessionVerifier.Envelope(payload));
        GymSaveReadResult old = repository.Read(id);
        Require(old.Success && old.Data.hasWorld && Mathf.Approximately(old.Data.worldTime, 0.6f) && old.Data.worldDay == 4,
            "version 1 world not kept");
    }

    private static GymCharacterSave Live(int level, float time, int day, string panel)
    {
        return new GymCharacterSave
        {
            progression = new GymProgressionState { level = level }, health = 150f, stamina = 80f,
            worldTime = time, worldDay = day, brokenPanels = new[] { panel }, hasSpawn = true,
            spawn = new Vector3(1f, 2f, 3f), facing = 90f
        };
    }

    private static void VerifySections(string directory)
    {
        var repository = new GymSaveRepository(directory, ClassIds);
        GymCharacterSave save = GymCharacterSave.New("bodybuilding");
        Require(!save.hasWorld, "a new character already has a world");

        // Solo and Host write both sections.
        foreach (GymSaveContext context in new[] { GymSaveContext.Solo, GymSaveContext.Host })
        {
            GymCharacterSave probe = GymCharacterSave.New("bodybuilding");
            GymSaveRules.Merge(probe, Live(5, 0.7f, 3, "glass-a"), GymSaveRules.Allowed(context));
            Require(probe.hasWorld && probe.characterRevision == 1 && probe.worldRevision == 1, context + " did not write both sections");
        }
        GymSaveRules.Merge(save, Live(5, 0.7f, 3, "glass-a"), GymSaveRules.Allowed(GymSaveContext.Solo));
        repository.Write(save.slotId, repository.Serialize(save));
        GymCharacterSave solo = repository.Read(save.slotId).Data;
        Require(solo.progression.level == 5 && solo.hasWorld && Mathf.Approximately(solo.worldTime, 0.7f) &&
            solo.worldDay == 3 && solo.brokenPanels[0] == "glass-a" && solo.hasSpawn, "solo write lost a section");

        // Join: the character moves on, the world section stays as loaded.
        string worldBefore = solo.WorldFingerprint();
        GymSaveRules.Merge(solo, Live(6, 0.1f, 9, "glass-b"), GymSaveRules.Allowed(GymSaveContext.Join));
        repository.Write(solo.slotId, repository.Serialize(solo));
        GymCharacterSave joined = repository.Read(solo.slotId).Data;
        Require(joined.progression.level == 6 && joined.characterRevision == 2, "join did not save the character");
        Require(joined.worldRevision == 1 && joined.WorldFingerprint() == worldBefore, "join save changed the world section");

        // Join on a character without a world must not invent one.
        GymCharacterSave fresh = GymCharacterSave.New("cardio");
        GymSaveRules.Merge(fresh, Live(2, 0.9f, 7, "glass-c"), GymSaveRules.Allowed(GymSaveContext.Join));
        repository.Write(fresh.slotId, repository.Serialize(fresh));
        GymCharacterSave freshBack = repository.Read(fresh.slotId).Data;
        Require(!freshBack.hasWorld && freshBack.worldRevision == 0 && freshBack.progression.level == 2,
            "join created a world section");
    }

    private static void VerifyCrash(string directory)
    {
        var repository = new GymSaveRepository(directory, ClassIds);
        GymCharacterSave save = GymCharacterSave.New("strongman");
        GymSaveRules.Merge(save, Live(4, 0.3f, 2, "glass-d"), GymSaveRules.Allowed(GymSaveContext.Solo));
        repository.Write(save.slotId, repository.Serialize(save));
        string path = Path.Combine(directory, save.slotId + ".json");
        byte[] before = File.ReadAllBytes(path);

        // A write cut off half way never reaches the live file.
        string whole = repository.Serialize(save);
        Throws<InvalidDataException>(() => repository.Write(save.slotId, whole.Substring(0, whole.Length / 2)));
        Require(BytesEqual(before, File.ReadAllBytes(path)), "failed write changed the save");

        // A temp file left by a killed process is ignored and replaced.
        File.WriteAllText(path + ".tmp", "{\"half\":");
        Require(repository.Read(save.slotId).Success, "stale temp file broke the read");
        GymSaveRules.Merge(save, Live(5, 0.4f, 2, "glass-d"), GymSaveRules.Allowed(GymSaveContext.Solo));
        repository.Write(save.slotId, repository.Serialize(save));
        Require(repository.Read(save.slotId).Data.progression.level == 5 && !File.Exists(path + ".tmp"),
            "write after a stale temp file failed");
    }

    private static void VerifyRules()
    {
        GymSaveSections both = GymSaveSections.Character | GymSaveSections.World;
        Require(GymSessionService.SectionsToWrite(GymSaveContext.Solo, both) == both, "solo cannot write both sections");
        Require(GymSessionService.SectionsToWrite(GymSaveContext.Host, both) == both, "host cannot write both sections");
        Require(GymSessionService.SectionsToWrite(GymSaveContext.Join, both) == GymSaveSections.Character, "join may write the world");
        Require(GymSessionService.SectionsToWrite(GymSaveContext.Solo, GymSaveSections.None) == GymSaveSections.None,
            "a clean save writes a section");
        // RequestSave has one path for autosave and manual saves and takes no section argument.
        var request = typeof(GymSessionService).GetMethod("RequestSave");
        foreach (var parameter in request.GetParameters())
            Require(parameter.ParameterType != typeof(GymSaveSections), "a save request chooses its own sections");
    }

    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new InvalidOperationException("expected " + typeof(T).Name);
    }

    private static bool BytesEqual(byte[] a, byte[] b)
    {
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
        return true;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
#endif
