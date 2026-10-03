using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Focused contracts for class data and character slots. Fixtures use a
/// separate temporary save directory; no real character file is touched.
/// </summary>
public static class GymChaosClassSessionVerifier
{
    private static readonly string[] ClassIds =
        { "bodybuilding", "calisthenics", "cardio", "powerlifting", "strongman" };

    // Balance rule (user, round 2): the six stats a player sees per class
    // (bodyweight, compound, cardio, sprint speed, stamina, sprint economy =
    // 1 / sprint cost) average 1.00; every specialist is clearly above the
    // baseline somewhere and clearly below it elsewhere.
    internal static float[] ShownStats(GymClassDefinition definition)
    {
        return new[]
        {
            definition.bodyweight, definition.compound, definition.cardio,
            definition.sprintSpeed, definition.staminaCapacity, 1f / definition.sprintCost
        };
    }

    // Brief section 6: upper and lower muscle-bulk targets.
    private static readonly float[][] ExpectedBulk =
    {
        new[] { 1.00f, 1.00f }, new[] { 0.85f, 0.50f }, new[] { 0.50f, 0.50f },
        new[] { 1.00f, 1.00f }, new[] { 1.15f, 1.15f }
    };

    /// <summary>Menu flow, save lifecycle and restoration in play mode (G3).</summary>
    public static void Run() => GymChaosClassSessionPlay.Begin("runtime");

    /// <summary>Per-class appearance, stats and exercise integration in play mode (G4).</summary>
    public static void RunClasses() => GymChaosClassSessionPlay.Begin("classes");

    public static void RunData()
    {
        string directory = Path.Combine(Path.GetTempPath(), "GymChaosClassData-" + Guid.NewGuid().ToString("N"));
        try
        {
            VerifyCatalog();
            VerifyExerciseCategories();
            VerifySlots(directory);
            VerifyCompatibility(directory);
            VerifyFailedWrite(directory);
            Debug.Log("GYMCHAOS_CLASS_SESSION_DATA_OK classes=5 slots=independent " +
                "recovery=backup future=refused unknownClass=refused failedWrite=preserved");
            EditorApplication.Exit(0);
        }
        catch (Exception exception)
        {
            Debug.LogError("GYMCHAOS_CLASS_SESSION_DATA_FAILED " + exception);
            EditorApplication.Exit(1);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    private static void VerifyCatalog()
    {
        IReadOnlyList<GymClassDefinition> classes = GymClassCatalog.All;
        Require(classes.Count == 5, "Catalog does not hold exactly five classes");
        for (int i = 0; i < ClassIds.Length; i++)
        {
            GymClassDefinition definition = GymClassCatalog.Get(ClassIds[i]);
            float[] shown = ShownStats(definition);
            float sum = 0f;
            float best = 0f;
            float worst = 10f;
            foreach (float value in shown)
            {
                sum += value;
                best = Mathf.Max(best, value);
                worst = Mathf.Min(worst, value);
            }
            Require(Mathf.Abs(sum / shown.Length - 1f) < 0.002f,
                $"{ClassIds[i]} stats average {sum / shown.Length:F4}, not 1.00");
            if (i == 0)
            {
                Require(best == 1f && worst == 1f, "Bodybuilding is not flat 1.00");
            }
            else
            {
                Require(best >= 1.2f && worst <= 0.9f,
                    $"{ClassIds[i]} lacks a clear strength ({best:F2}) or weakness ({worst:F2})");
            }
            Require(Mathf.Abs(definition.upperBulk - ExpectedBulk[i][0]) < 0.0001f &&
                Mathf.Abs(definition.lowerBulk - ExpectedBulk[i][1]) < 0.0001f,
                ClassIds[i] + " bulk targets differ from the brief");
            Require(!string.IsNullOrWhiteSpace(definition.displayName) &&
                !string.IsNullOrWhiteSpace(definition.description) &&
                !string.IsNullOrWhiteSpace(definition.strength) &&
                !string.IsNullOrWhiteSpace(definition.weakness) &&
                !string.IsNullOrWhiteSpace(definition.artwork),
                ClassIds[i] + " is missing display text or artwork");
        }

        GymClassDefinition baseline = GymClassCatalog.Baseline;
        Require(baseline.id == "bodybuilding" && string.IsNullOrEmpty(baseline.appearance) &&
            baseline.softTissue == 0f, "Bodybuilding is not the unchanged baseline");
        GymClassDefinition powerlifting = GymClassCatalog.Get("powerlifting");
        GymClassDefinition strongman = GymClassCatalog.Get("strongman");
        GymClassDefinition calisthenics = GymClassCatalog.Get("calisthenics");
        GymClassDefinition runner = GymClassCatalog.Get("cardio");
        // Each specialty belongs to one class, and no two classes share a stat line.
        foreach (GymClassDefinition other in classes)
        {
            Require(other == calisthenics || other.bodyweight < calisthenics.bodyweight,
                "Calisthenics is not the best bodyweight class");
            Require(other == runner || (other.cardio < runner.cardio && other.sprintSpeed < runner.sprintSpeed),
                "Cardio is not the best cardio and sprint class");
            Require(other == strongman || other.compound < strongman.compound,
                "Strongman is not the best compound class");
            Require(other == strongman || other == powerlifting || other.compound < powerlifting.compound,
                "Powerlifting is not second in compound lifts");
        }
        for (int a = 0; a < classes.Count; a++)
        {
            for (int b = a + 1; b < classes.Count; b++)
            {
                float[] statsA = ShownStats(classes[a]);
                float[] statsB = ShownStats(classes[b]);
                float difference = 0f;
                for (int j = 0; j < statsA.Length; j++) difference += Mathf.Abs(statsA[j] - statsB[j]);
                Require(difference >= 0.3f, classes[a].id + " and " + classes[b].id + " are too similar");
            }
        }
        Require(strongman.cardio < powerlifting.cardio && strongman.sprintSpeed < powerlifting.sprintSpeed,
            "Strongman is not slower than Powerlifting");
        Require(strongman.softTissue > powerlifting.softTissue && powerlifting.softTissue > 0f,
            "Soft-tissue ordering is wrong");
        Require(!GymClassCatalog.TryGet("Bodybuilding", out _) && !GymClassCatalog.TryGet("wizard", out _),
            "Catalog accepted an unknown or differently cased id");

        // Reloading on the same calendar day must not start a new gym day.
        DateTime noon = new DateTime(2026, 9, 30, 12, 0, 0, DateTimeKind.Local);
        Require(!GymSessionService.ShouldStartNewDay(noon.AddHours(-3).ToUniversalTime().ToString("O"), noon) &&
            GymSessionService.ShouldStartNewDay(noon.AddDays(-1).ToUniversalTime().ToString("O"), noon) &&
            !GymSessionService.ShouldStartNewDay("not a date", noon) &&
            !GymSessionService.ShouldStartNewDay(noon.AddDays(1).ToUniversalTime().ToString("O"), noon),
            "Gym-day rollover rule is wrong");

        // Derivation is a pure function of the definition: repeated reads never compound.
        GymClassDefinition cardio = GymClassCatalog.Get("cardio");
        float first = cardio.Performance(GymExerciseCategory.Cardio);
        for (int i = 0; i < 50; i++) cardio.Performance(GymExerciseCategory.Cardio);
        Require(first == cardio.Performance(GymExerciseCategory.Cardio) && first == 1.25f,
            "Repeated performance reads changed the value");
        Require(cardio.Performance(GymExerciseCategory.GeneralResistance) == 1f,
            "General resistance must stay at baseline for every class");
    }

    private static void VerifyExerciseCategories()
    {
        var expected = new Dictionary<GymExerciseType, GymExerciseCategory>
        {
            { GymExerciseType.PullUps, GymExerciseCategory.Bodyweight },
            { GymExerciseType.Dips, GymExerciseCategory.Bodyweight },
            { GymExerciseType.BarbellSquat, GymExerciseCategory.Compound },
            { GymExerciseType.FlatBenchPress, GymExerciseCategory.Compound },
            { GymExerciseType.InclineBenchPress, GymExerciseCategory.Compound },
            { GymExerciseType.Deadlift, GymExerciseCategory.Compound },
            { GymExerciseType.Treadmill, GymExerciseCategory.Cardio },
            { GymExerciseType.ExerciseBike, GymExerciseCategory.Cardio },
            { GymExerciseType.PreacherCurl, GymExerciseCategory.GeneralResistance },
            { GymExerciseType.LatPulldown, GymExerciseCategory.GeneralResistance }
        };
        // Every implemented exercise has exactly one primary category.
        foreach (GymExerciseType type in Enum.GetValues(typeof(GymExerciseType)))
        {
            Require(expected.TryGetValue(type, out GymExerciseCategory category),
                "Exercise " + type + " has no expected category in the verifier");
            Require(GymClassCatalog.Category(type) == category, type + " maps to the wrong category");
        }
    }

    private static void VerifySlots(string directory)
    {
        var repository = new GymSaveRepository(directory, ClassIds);
        Require(repository.List().Count == 0, "Empty directory listed saves");

        GymCharacterSave first = GymCharacterSave.New("calisthenics");
        GymCharacterSave second = GymCharacterSave.New("strongman");
        Require(first.slotId != second.slotId, "Two new characters share a slot id");
        first.progression.level = 4;
        first.progression.totalExperience = 900;
        first.playSeconds = 125.5;
        first.oneShotRewards = new[] { "once:discovery-1-back-room" };
        second.progression.level = 2;
        repository.Write(first.slotId, repository.Serialize(first));
        repository.Write(second.slotId, repository.Serialize(second));

        // Serialization detaches: mutating the live object after Serialize changes nothing on disk.
        string snapshot = repository.Serialize(first);
        first.progression.level = 99;
        GymSaveReadResult decoded = repository.Decode(snapshot, first.slotId);
        Require(decoded.Success && decoded.Data.progression.level == 4, "Serialized snapshot followed live state");
        first.progression.level = 4;

        GymSaveReadResult readFirst = repository.Read(first.slotId);
        GymSaveReadResult readSecond = repository.Read(second.slotId);
        Require(readFirst.Success && readFirst.Data.classId == "calisthenics" &&
            readFirst.Data.progression.level == 4 && Math.Abs(readFirst.Data.playSeconds - 125.5) < 0.001 &&
            readFirst.Data.oneShotRewards.Length == 1, "First character did not round-trip");
        Require(readSecond.Success && readSecond.Data.classId == "strongman" &&
            readSecond.Data.progression.level == 2, "Second character did not round-trip");

        // Saving one character never alters the other.
        byte[] secondBytes = File.ReadAllBytes(Path.Combine(directory, second.slotId + ".json"));
        first.progression.level = 5;
        repository.Write(first.slotId, repository.Serialize(first));
        first.progression.level = 6;
        repository.Write(first.slotId, repository.Serialize(first));
        Require(BytesEqual(secondBytes, File.ReadAllBytes(Path.Combine(directory, second.slotId + ".json"))),
            "Saving one slot modified another");

        // One current file and one previous valid backup; no growing history, no temp left behind.
        string[] firstFiles = Directory.GetFiles(directory, first.slotId + "*");
        Require(firstFiles.Length == 2, "Slot keeps " + firstFiles.Length + " files instead of current + backup");
        Require(repository.Read(first.slotId).Data.progression.level == 6, "Current save is not the newest");
        GymSaveReadResult backup = repository.Decode(
            File.ReadAllText(Path.Combine(directory, first.slotId + ".json.bak")), first.slotId);
        Require(backup.Success && backup.Data.progression.level == 5, "Backup is not the previous valid save");
        Require(repository.List().Count == 2, "Listing does not show both characters");

        // Corrupt current: recover from the validated backup.
        string currentPath = Path.Combine(directory, first.slotId + ".json");
        File.WriteAllText(currentPath, "{ truncated");
        GymSaveReadResult recovered = repository.Read(first.slotId);
        Require(recovered.Success && recovered.RecoveredBackup && recovered.Data.progression.level == 5,
            "Corrupt current save did not recover from the backup");
        // Writing over a corrupt current must not rotate the garbage over the good backup.
        first.progression.level = 7;
        repository.Write(first.slotId, repository.Serialize(first));
        backup = repository.Decode(
            File.ReadAllText(Path.Combine(directory, first.slotId + ".json.bak")), first.slotId);
        Require(backup.Success && backup.Data.progression.level == 5, "Corrupt file replaced the valid backup");
        Require(repository.Read(first.slotId).Data.progression.level == 7, "Write after corruption failed");

        // Tampered payload with an intact-looking envelope fails the integrity check.
        string tampered = File.ReadAllText(currentPath).Replace("\\\"level\\\":7", "\\\"level\\\":70");
        Require(!repository.Decode(tampered, first.slotId).Success, "Tampered payload passed the checksum");

        // Corrupt current and backup: the slot is reported, never listed as valid.
        File.WriteAllText(Path.Combine(directory, second.slotId + ".json"), "garbage");
        GymSaveReadResult broken = repository.Read(second.slotId);
        Require(!broken.Success && broken.Failure == GymSaveFailure.Corrupt, "Unrecoverable save reported success");
        int valid = 0;
        foreach (GymSaveListEntry entry in repository.List()) if (entry.Result.Success) valid++;
        Require(valid == 1, "Corrupt save appears as a valid character");

        // A file stored under another identity is rejected.
        File.Copy(currentPath, Path.Combine(directory, second.slotId + ".json"), true);
        Require(!repository.Read(second.slotId).Success, "Save with a mismatched slot id was accepted");
        File.Delete(Path.Combine(directory, second.slotId + ".json"));
    }

    private static void VerifyCompatibility(string directory)
    {
        var repository = new GymSaveRepository(directory, ClassIds);

        // Legacy record without a class field: Bodybuilding, all progress kept.
        string legacyId = Guid.NewGuid().ToString("N");
        string now = DateTime.UtcNow.ToString("O");
        string legacyProgression = "{\"level\":7,\"experience\":33,\"totalExperience\":1500,\"skillPoints\":1," +
            "\"strengthRank\":3,\"enduranceRank\":2,\"techniqueRank\":1,\"reputationRank\":0,\"reputation\":14," +
            "\"gymDay\":9,\"shirt\":\"Red\",\"headwear\":\"Cap\"}";
        string legacyPayload = "{\"version\":0,\"slotId\":\"" + legacyId + "\",\"createdUtc\":\"" + now +
            "\",\"savedUtc\":\"" + now + "\",\"playSeconds\":42.0,\"progression\":" + legacyProgression + "}";
        File.WriteAllText(Path.Combine(directory, legacyId + ".json"), Envelope(legacyPayload));
        GymSaveReadResult legacy = repository.Read(legacyId);
        Require(legacy.Success && legacy.Data.classId == "bodybuilding", "Legacy save did not migrate to Bodybuilding");
        GymProgressionState migrated = legacy.Data.progression;
        Require(migrated.level == 7 && migrated.experience == 33 && migrated.totalExperience == 1500 &&
            migrated.skillPoints == 1 && migrated.strengthRank == 3 && migrated.enduranceRank == 2 &&
            migrated.techniqueRank == 1 && migrated.reputation == 14 && migrated.gymDay == 9 &&
            migrated.shirt == "Red" && migrated.headwear == "Cap" &&
            legacy.Data.version == GymCharacterSave.CurrentVersion, "Legacy migration lost progress");

        // Future version: refused, never overwritten, never replaced by an older backup.
        string futureId = Guid.NewGuid().ToString("N");
        GymCharacterSave older = GymCharacterSave.New("cardio");
        older.slotId = futureId;
        repository.Write(futureId, repository.Serialize(older));
        repository.Write(futureId, repository.Serialize(older));
        string futurePath = Path.Combine(directory, futureId + ".json");
        string futurePayload = "{\"version\":" + (GymCharacterSave.CurrentVersion + 1) + ",\"slotId\":\"" + futureId +
            "\",\"classId\":\"cardio\",\"createdUtc\":\"" + now + "\",\"savedUtc\":\"" + now +
            "\",\"progression\":{\"level\":50}}";
        File.WriteAllText(futurePath, Envelope(futurePayload));
        byte[] futureBytes = File.ReadAllBytes(futurePath);
        GymSaveReadResult future = repository.Read(futureId);
        Require(!future.Success && future.Failure == GymSaveFailure.FutureVersion && !future.RecoveredBackup,
            "Future-version save was opened or silently replaced by its backup");
        RequireThrows<InvalidDataException>(
            () => repository.Write(futureId, repository.Serialize(older)), "Future-version save was overwritten");
        Require(BytesEqual(futureBytes, File.ReadAllBytes(futurePath)), "Future-version file changed on disk");

        // Unknown class: documented policy is refuse and preserve, no reinterpretation.
        string unknownId = Guid.NewGuid().ToString("N");
        string unknownPath = Path.Combine(directory, unknownId + ".json");
        string unknownPayload = "{\"version\":1,\"slotId\":\"" + unknownId + "\",\"classId\":\"crossfit\"," +
            "\"createdUtc\":\"" + now + "\",\"savedUtc\":\"" + now + "\",\"progression\":{\"level\":3}}";
        File.WriteAllText(unknownPath, Envelope(unknownPayload));
        byte[] unknownBytes = File.ReadAllBytes(unknownPath);
        GymSaveReadResult unknown = repository.Read(unknownId);
        Require(!unknown.Success && unknown.Failure == GymSaveFailure.UnknownClass,
            "Unknown class was accepted or reinterpreted");
        GymCharacterSave replacement = GymCharacterSave.New("bodybuilding");
        replacement.slotId = unknownId;
        RequireThrows<InvalidDataException>(
            () => repository.Write(unknownId, repository.Serialize(replacement)), "Unknown-class save was overwritten");
        Require(BytesEqual(unknownBytes, File.ReadAllBytes(unknownPath)), "Unknown-class file changed on disk");

        // Invalid content never serializes.
        GymCharacterSave invalid = GymCharacterSave.New("bodybuilding");
        invalid.health = float.NaN;
        RequireThrows<InvalidDataException>(() => repository.Serialize(invalid), "NaN health was serialized");
        invalid = GymCharacterSave.New("bodybuilding");
        invalid.slotId = "../escape";
        RequireThrows<InvalidDataException>(() => repository.Serialize(invalid), "Path-like slot id was serialized");
        RequireThrows<ArgumentException>(() => GymCharacterSave.New("wizard"), "Unknown class created a character");
    }

    private static void VerifyFailedWrite(string directory)
    {
        var repository = new GymSaveRepository(directory, ClassIds);
        GymCharacterSave save = GymCharacterSave.New("powerlifting");
        save.progression.level = 3;
        repository.Write(save.slotId, repository.Serialize(save));
        string path = Path.Combine(directory, save.slotId + ".json");
        byte[] good = File.ReadAllBytes(path);

        // Hold the temporary path open exclusively so the write cannot proceed.
        save.progression.level = 4;
        string serialized = repository.Serialize(save);
        bool failed = false;
        using (new FileStream(path + ".tmp", FileMode.Create, FileAccess.Write, FileShare.None))
        {
            try
            {
                repository.Write(save.slotId, serialized);
            }
            catch (IOException)
            {
                failed = true;
            }
        }
        Require(failed, "A blocked write reported success");
        Require(BytesEqual(good, File.ReadAllBytes(path)), "Failed write damaged the last good save");
        Require(repository.Read(save.slotId).Data.progression.level == 3, "Last good save is unreadable after a failure");

        // Retry succeeds once the obstruction is gone.
        if (File.Exists(path + ".tmp")) File.Delete(path + ".tmp");
        repository.Write(save.slotId, serialized);
        Require(repository.Read(save.slotId).Data.progression.level == 4, "Retry after a failed write did not save");
    }

    internal static string Envelope(string payload)
    {
        string hash;
        using (SHA256 sha = SHA256.Create())
        {
            hash = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(payload))).Replace("-", string.Empty);
        }
        return "{\"format\":\"GymChaos.Character\",\"checksum\":\"" + hash + "\",\"payload\":" +
            Quote(payload) + "}";
    }

    private static string Quote(string value)
    {
        return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    }

    private static bool BytesEqual(byte[] a, byte[] b)
    {
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
        return true;
    }

    private static void RequireThrows<T>(Action action, string message) where T : Exception
    {
        try
        {
            action();
        }
        catch (T)
        {
            return;
        }
        throw new InvalidOperationException(message);
    }

    internal static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
