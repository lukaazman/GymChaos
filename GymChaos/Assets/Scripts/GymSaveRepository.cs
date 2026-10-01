using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

/// <summary>
/// Validated current + previous-valid files. Future versions and unknown classes are
/// read-only compatibility errors, even when an older backup could be opened.
/// Serialization happens at capture; the worker receives only an immutable string.
/// </summary>
public sealed class GymSaveRepository
{
    private const string Format = "GymChaos.Character";
    private const long MaximumBytes = 4 * 1024 * 1024;
    [Serializable] private sealed class Envelope { public string format; public string checksum; public string payload; }
    private readonly string directory;
    private readonly HashSet<string> knownClasses;
    private readonly object writeLock = new object();
    public string DirectoryPath => directory;

    public GymSaveRepository(string directoryPath, IEnumerable<string> supportedClasses)
    {
        directory = Path.GetFullPath(directoryPath);
        knownClasses = new HashSet<string>(supportedClasses, StringComparer.Ordinal);
    }

    public List<GymSaveListEntry> List()
    {
        var entries = new List<GymSaveListEntry>();
        if (!Directory.Exists(directory)) return entries;
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (string file in Directory.GetFiles(directory, "*.json*"))
        {
            string name = Path.GetFileName(file);
            if (!name.EndsWith(".json", StringComparison.Ordinal) && !name.EndsWith(".json.bak", StringComparison.Ordinal)) continue;
            string id = name.Split('.')[0];
            if (ValidId(id)) ids.Add(id);
        }
        foreach (string id in ids) entries.Add(new GymSaveListEntry { SlotId = id, Result = Read(id) });
        entries.Sort((a, b) => string.CompareOrdinal(b.Result.Data?.savedUtc, a.Result.Data?.savedUtc));
        return entries;
    }

    public GymSaveReadResult Read(string slotId)
    {
        string path = PathFor(slotId);
        GymSaveReadResult current = ReadFile(path, slotId);
        if (current.Success || Protected(current.Failure)) return current;
        GymSaveReadResult backup = ReadFile(path + ".bak", slotId);
        if (!backup.Success) return current.Failure == GymSaveFailure.Missing ? backup : current;
        backup.RecoveredBackup = true;
        backup.Message = "Recovered the previous valid save.";
        return backup;
    }

    public string Serialize(GymCharacterSave data)
    {
        // Detach before normalizing so validation cannot change live progression.
        string payload = JsonUtility.ToJson(data);
        GymSaveReadResult validation = ValidatePayload(payload, data?.slotId);
        if (!validation.Success) throw new InvalidDataException(validation.Message);
        payload = JsonUtility.ToJson(validation.Data);
        return JsonUtility.ToJson(new Envelope { format = Format, payload = payload, checksum = Hash(payload) });
    }

    public void Write(string slotId, string serialized)
    {
        lock (writeLock)
        {
            string path = PathFor(slotId);
            GymSaveReadResult incoming = Decode(serialized, slotId);
            if (!incoming.Success) throw new InvalidDataException(incoming.Message);
            Directory.CreateDirectory(directory);
            GymSaveReadResult current = ReadFile(path, slotId);
            GymSaveReadResult backup = ReadFile(path + ".bak", slotId);
            if (Protected(current.Failure) || Protected(backup.Failure))
                throw new InvalidDataException("This character requires a different game version; its files were preserved.");
            string temporary = path + ".tmp";
            try
            {
                byte[] bytes = Encoding.UTF8.GetBytes(serialized);
                if (bytes.Length > MaximumBytes) throw new InvalidDataException("Save exceeds the supported size.");
                using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(true);
                }
                GymSaveReadResult reread = ReadFile(temporary, slotId);
                if (!reread.Success) throw new InvalidDataException("Temporary save failed validation.");
                if (File.Exists(path))
                {
                    // Never rotate a corrupt current file over the last valid backup.
                    File.Replace(temporary, path, current.Success ? path + ".bak" : null);
                }
                else File.Move(temporary, path);
            }
            finally
            {
                // This path belongs to this slot and this writer only.
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }
    }

    public GymSaveReadResult Decode(string serialized, string expectedId)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(serialized) || serialized.Length > MaximumBytes)
                return Fail(GymSaveFailure.Corrupt, "The save is empty or too large.");
            Envelope envelope = JsonUtility.FromJson<Envelope>(serialized);
            if (envelope == null || envelope.format != Format || string.IsNullOrEmpty(envelope.payload) ||
                !string.Equals(Hash(envelope.payload), envelope.checksum, StringComparison.Ordinal))
                return Fail(GymSaveFailure.Corrupt, "The save did not pass its integrity check.");
            return ValidatePayload(envelope.payload, expectedId);
        }
        catch (Exception exception) when (exception is ArgumentException || exception is FormatException)
        { return Fail(GymSaveFailure.Corrupt, "The save could not be decoded."); }
    }

    private GymSaveReadResult ValidatePayload(string payload, string expectedId)
    {
        GymCharacterSave data;
        try { data = JsonUtility.FromJson<GymCharacterSave>(payload); }
        catch (ArgumentException) { return Fail(GymSaveFailure.Corrupt, "Invalid character data."); }
        if (data == null) return Fail(GymSaveFailure.Corrupt, "Character data is missing.");
        if (data.version > GymCharacterSave.CurrentVersion)
            return Fail(GymSaveFailure.FutureVersion, "This character was saved by a newer game version.");
        if (data.version < 0 || !ValidId(data.slotId) || data.slotId != expectedId || data.progression == null ||
            !DateTime.TryParse(data.createdUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out _) ||
            !DateTime.TryParse(data.savedUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out _) ||
            !Finite(data.playSeconds) || data.playSeconds < 0 || !Finite(data.health) || !Finite(data.stamina) ||
            !Finite(data.spawn.x) || !Finite(data.spawn.y) || !Finite(data.spawn.z) || !Finite(data.facing) ||
            !Finite(data.worldTime)) return Fail(GymSaveFailure.Corrupt, "Character data is incomplete or invalid.");
        // Legitimate older records may omit class, but never silently reinterpret an unknown ID.
        if (string.IsNullOrEmpty(data.classId)) data.classId = GymClassCatalog.BaselineId;
        if (!knownClasses.Contains(data.classId))
            return Fail(GymSaveFailure.UnknownClass, "The class '" + data.classId + "' is unavailable. The original save is preserved.");
        data.version = GymCharacterSave.CurrentVersion;
        data.progression.Normalize();
        data.oneShotRewards = data.oneShotRewards ?? Array.Empty<string>();
        data.brokenPanels = data.brokenPanels ?? Array.Empty<string>();
        data.worldTime = Mathf.Clamp01(data.worldTime);
        data.worldDay = Math.Max(0, data.worldDay);
        return new GymSaveReadResult { Data = data, Message = string.Empty };
    }

    private GymSaveReadResult ReadFile(string path, string expectedId)
    {
        try
        {
            if (!File.Exists(path)) return Fail(GymSaveFailure.Missing, "No character save was found.");
            if (new FileInfo(path).Length > MaximumBytes) return Fail(GymSaveFailure.Corrupt, "The save is too large.");
            return Decode(File.ReadAllText(path, Encoding.UTF8), expectedId);
        }
        catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
        { return Fail(GymSaveFailure.Io, "The save could not be read: " + exception.Message); }
    }

    private string PathFor(string slotId)
    {
        if (!ValidId(slotId)) throw new ArgumentException("Invalid character identity.", nameof(slotId));
        return Path.Combine(directory, slotId + ".json");
    }

    private static bool ValidId(string id) => id != null && id.Length == 32 && Guid.TryParseExact(id, "N", out _);
    private static bool Protected(GymSaveFailure failure) => failure == GymSaveFailure.FutureVersion || failure == GymSaveFailure.UnknownClass;
    private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    private static GymSaveReadResult Fail(GymSaveFailure failure, string message) => new GymSaveReadResult { Failure = failure, Message = message };
    private static string Hash(string value)
    {
        using (SHA256 sha = SHA256.Create())
            return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-", string.Empty);
    }
}
