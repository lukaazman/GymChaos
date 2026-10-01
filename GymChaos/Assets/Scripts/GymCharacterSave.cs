using System;
using UnityEngine;

/// <summary>Durable state only. Effective stats and live scene references never enter saves.</summary>
[Serializable]
public sealed class GymCharacterSave
{
    public const int CurrentVersion = 1;
    public int version = CurrentVersion;
    public string slotId;
    public string classId;
    public string createdUtc;
    public string savedUtc;
    public double playSeconds;
    public GymProgressionState progression;
    public string[] oneShotRewards = Array.Empty<string>();
    public Vector3 spawn;
    public float facing;
    public bool hasSpawn;
    public float health = 200f;
    public float stamina = 100f;
    public float worldTime = 0.24f;
    public int worldDay;
    public string[] brokenPanels = Array.Empty<string>();

    public static GymCharacterSave New(string confirmedClass)
    {
        GymClassCatalog.Get(confirmedClass);
        string now = DateTime.UtcNow.ToString("O");
        return new GymCharacterSave
        {
            slotId = Guid.NewGuid().ToString("N"), classId = confirmedClass,
            createdUtc = now, savedUtc = now, progression = new GymProgressionState(),
            stamina = 100f * GymClassCatalog.Get(confirmedClass).staminaCapacity
        };
    }
}

public enum GymSaveFailure { None, Missing, Corrupt, FutureVersion, UnknownClass, Io }

public sealed class GymSaveReadResult
{
    public GymCharacterSave Data { get; internal set; }
    public GymSaveFailure Failure { get; internal set; }
    public string Message { get; internal set; }
    public bool RecoveredBackup { get; internal set; }
    public bool Success => Data != null && Failure == GymSaveFailure.None;
}

public sealed class GymSaveListEntry
{
    public string SlotId { get; internal set; }
    public GymSaveReadResult Result { get; internal set; }
}
