using System;
using UnityEngine;

/// <summary>Which part of a character file a save may touch.</summary>
[Flags]
public enum GymSaveSections { None = 0, Character = 1, World = 2 }

/// <summary>Solo and Host own the world; a Join session plays in somebody else's world.</summary>
public enum GymSaveContext { Solo, Host, Join }

/// <summary>
/// Durable state only. Effective stats and live scene references never enter saves.
/// One file per character with two sections. Character: class, progression,
/// rewards, health, stamina. World: gym clock, broken panels, fixer ages and
/// the spawn point. A Join session loads and writes the character section only;
/// the world section is never written without a loaded world.
/// </summary>
[Serializable]
public sealed class GymCharacterSave
{
    // 3: the player's look and name.
    // 4: the DEFAULT / POTATO split (the global style; retired).
    // 5: per-character and per-vehicle POTATO versions plus their unlocks.
    // Files from before 4 are reset to one standard character (default look
    // and name); the create screen only asks for a missing name now.
    public const int CurrentVersion = 5;
    /// <summary>The one name every save from before the split loads with.</summary>
    public const string LegacyName = "PLAYER";
    public int version = CurrentVersion;
    public string slotId;
    public string classId;
    public string username = string.Empty;
    public int gender;
    public int heightCm = PlayerAppearance.DefaultMaleHeight;
    public int skinTone = 2;
    public int hair = 2;
    public int hairColor;
    public int facialHair;
    // Appearance menu: which characters and vehicles wear their POTATO remake.
    // The unlocked lists are the permission; a POTATO entry is never written
    // without one. A guest replaces all four with the host's table.
    public string[] slopMembers = Array.Empty<string>();
    public string[] slopVehicles = Array.Empty<string>();
    public string[] unlockedMembers = Array.Empty<string>();
    public string[] unlockedVehicles = Array.Empty<string>();
    public string createdUtc;
    public string savedUtc;
    public double playSeconds;
    // False until a Solo or Host session has written the world section.
    public bool hasWorld;
    public int characterRevision;
    public int worldRevision;
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
    // Jolly Dog: when each still-broken panel was first seen (day + time01).
    public string[] fixerIssueIds = Array.Empty<string>();
    public float[] fixerIssueSince = Array.Empty<float>();

    /// <summary>The world section as text, to prove a save left it untouched.</summary>
    public string WorldFingerprint() => JsonUtility.ToJson(new WorldView
    {
        hasWorld = hasWorld, revision = worldRevision, spawn = spawn, facing = facing, hasSpawn = hasSpawn,
        worldTime = worldTime, worldDay = worldDay, brokenPanels = brokenPanels,
        fixerIssueIds = fixerIssueIds, fixerIssueSince = fixerIssueSince
    });

    [Serializable]
    private sealed class WorldView
    {
        public bool hasWorld;
        public int revision;
        public Vector3 spawn;
        public float facing;
        public bool hasSpawn;
        public float worldTime;
        public int worldDay;
        public string[] brokenPanels;
        public string[] fixerIssueIds;
        public float[] fixerIssueSince;
    }

    /// <summary>Has a name and look; false for files from before version 3.</summary>
    public bool IsCustomized => !string.IsNullOrEmpty(username);

    /// <summary>Copies the look and name (customize screen and migration).</summary>
    public void CopyLook(GymCharacterSave look)
    {
        username = look.username;
        gender = look.gender;
        heightCm = look.heightCm;
        skinTone = look.skinTone;
        hair = look.hair;
        hairColor = look.hairColor;
        facialHair = look.facialHair;
        PlayerAppearance.Normalize(this);
    }

    public static GymCharacterSave New(string confirmedClass, GymCharacterSave look = null)
    {
        GymClassCatalog.Get(confirmedClass);
        string now = DateTime.UtcNow.ToString("O");
        var save = new GymCharacterSave
        {
            slotId = Guid.NewGuid().ToString("N"), classId = confirmedClass,
            createdUtc = now, savedUtc = now, progression = new GymProgressionState(),
            stamina = 100f * GymClassCatalog.Get(confirmedClass).staminaCapacity
        };
        if (look != null) save.CopyLook(look);
        return save;
    }
}

public static class GymSaveRules
{
    public static GymSaveSections Allowed(GymSaveContext context) =>
        context == GymSaveContext.Join ? GymSaveSections.Character : GymSaveSections.Character | GymSaveSections.World;

    /// <summary>Copies only the given sections of a live capture into the save and bumps their revisions.</summary>
    public static void Merge(GymCharacterSave target, GymCharacterSave live, GymSaveSections sections)
    {
        if ((sections & GymSaveSections.Character) != 0)
        {
            if (live.progression != null) target.progression = live.progression;
            if (live.oneShotRewards != null) target.oneShotRewards = live.oneShotRewards;
            target.health = live.health;
            target.stamina = live.stamina;
            target.characterRevision++;
        }
        if ((sections & GymSaveSections.World) != 0)
        {
            target.spawn = live.spawn;
            target.facing = live.facing;
            target.hasSpawn = live.hasSpawn;
            target.worldTime = live.worldTime;
            target.worldDay = live.worldDay;
            target.brokenPanels = live.brokenPanels;
            target.fixerIssueIds = live.fixerIssueIds;
            target.fixerIssueSince = live.fixerIssueSince;
            target.hasWorld = true;
            target.worldRevision++;
        }
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
