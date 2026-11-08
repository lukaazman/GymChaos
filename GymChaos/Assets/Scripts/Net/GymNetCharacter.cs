using System;
using UnityEngine;

/// <summary>A guest's character as sent to the host (the character section of its save).</summary>
[Serializable]
public sealed class GymNetCharacterBlob
{
    public string slotId;
    public string classId;
    public string progressionJson;
    public string[] oneShotRewards;
    public float health;
    public float stamina;
}

/// <summary>The host's world state a joining guest applies (the world section, minus the spawn).</summary>
[Serializable]
public sealed class GymNetWorldSnapshot
{
    public float worldTime;
    public int worldDay;
    public string[] brokenPanels;
    public string[] fixerIssueIds;
    public float[] fixerIssueSince;
}

/// <summary>
/// Everything a peer sends is untrusted. The host rebuilds a clean character
/// from the blob (class must exist, ranks and counters clamped, lists and
/// strings capped); a guest does the same for the host's world snapshot.
/// </summary>
public static class GymNetCharacter
{
    public const int MaxBytes = 16 * 1024;
    public const int MaxLevel = 999;
    public const int MaxRewards = 64;
    public const int MaxTextLength = 64;
    public const int MaxPanels = 512;
    public const float MaxResource = 1000f;
    // Below the relay's 64 KB message limit.
    public const int MaxWorldBytes = 60000;

    public static string ToJson(GymCharacterSave save)
    {
        return JsonUtility.ToJson(new GymNetCharacterBlob
        {
            slotId = save.slotId, classId = save.classId,
            progressionJson = JsonUtility.ToJson(save.progression),
            oneShotRewards = save.oneShotRewards, health = save.health, stamina = save.stamina
        });
    }

    /// <summary>Builds a clean character from untrusted text; false with the reason when it cannot be accepted.</summary>
    public static bool TryValidate(string json, out GymCharacterSave clean, out string reason)
    {
        clean = null;
        reason = null;
        if (string.IsNullOrEmpty(json) || json.Length > MaxBytes) { reason = "character too large"; return false; }
        GymNetCharacterBlob blob;
        GymProgressionState progression;
        try
        {
            blob = JsonUtility.FromJson<GymNetCharacterBlob>(json);
            progression = JsonUtility.FromJson<GymProgressionState>(blob?.progressionJson ?? string.Empty);
        }
        catch (Exception) { reason = "character unreadable"; return false; }
        if (blob == null || progression == null) { reason = "character unreadable"; return false; }
        if (blob.slotId == null || !Guid.TryParseExact(blob.slotId, "N", out _)) { reason = "bad character id"; return false; }
        if (!GymClassCatalog.TryGet(blob.classId, out _)) { reason = "unknown class"; return false; }
        if (!Sane(blob.health) || !Sane(blob.stamina)) { reason = "bad health or stamina"; return false; }

        progression.Normalize();
        progression.level = Mathf.Min(progression.level, MaxLevel);
        progression.skillPoints = Mathf.Min(progression.skillPoints, MaxLevel);
        progression.masteryRank = Mathf.Min(progression.masteryRank, MaxLevel);
        progression.experience = Mathf.Min(progression.experience, int.MaxValue / 2);
        progression.totalExperience = Mathf.Min(progression.totalExperience, int.MaxValue / 2);
        // Enum names only: Enum.TryParse in Normalize also takes numbers and comma lists.
        progression.shirt = NameOr(typeof(GymShirtColor), progression.shirt, nameof(GymShirtColor.Black));
        progression.top = NameOr(typeof(GymTopStyle), progression.top, nameof(GymTopStyle.TankTop));
        progression.bottom = NameOr(typeof(GymBottomStyle), progression.bottom, nameof(GymBottomStyle.Shorts));
        progression.bottomColor = NameOr(typeof(GymShirtColor), progression.bottomColor, nameof(GymShirtColor.Black));
        progression.headwear = NameOr(typeof(GymHeadwear), progression.headwear, nameof(GymHeadwear.None));
        progression.gymDay = Mathf.Min(progression.gymDay, 1000000);
        progression.cardioChallengeDay = Mathf.Clamp(progression.cardioChallengeDay, 0, 1000000);
        progression.cardioChallengeKind = Mathf.Clamp(progression.cardioChallengeKind, 0, 16);
        progression.cardioChallengeTarget = Finite(progression.cardioChallengeTarget);
        progression.cardioChallengeProgress = Finite(progression.cardioChallengeProgress);
        progression.loyaltyStampDay = Mathf.Clamp(progression.loyaltyStampDay, 0, 1000000);
        progression.loyaltyVisitDay = Mathf.Clamp(progression.loyaltyVisitDay, 0, 1000000);
        progression.raceStarts = Mathf.Min(progression.raceStarts, 1000000);
        progression.raceWins = Mathf.Min(progression.raceWins, 1000000);
        for (int i = 0; i < progression.dailyProgress.Length; i++)
            progression.dailyProgress[i] = Mathf.Clamp(progression.dailyProgress[i], 0, 1000000);

        clean = GymCharacterSave.New(blob.classId);
        clean.slotId = blob.slotId;
        clean.progression = progression;
        clean.oneShotRewards = CapList(blob.oneShotRewards, MaxRewards);
        clean.health = Mathf.Clamp(blob.health, 0f, MaxResource);
        clean.stamina = Mathf.Clamp(blob.stamina, 0f, MaxResource);
        return true;
    }

    /// <summary>A world snapshot with every field bounded; null when it cannot be read.</summary>
    public static GymNetWorldSnapshot CleanWorld(string json)
    {
        if (string.IsNullOrEmpty(json) || json.Length > MaxWorldBytes) return null;
        GymNetWorldSnapshot world;
        try { world = JsonUtility.FromJson<GymNetWorldSnapshot>(json); }
        catch (Exception) { return null; }
        if (world == null || !Sane(world.worldTime)) return null;
        world.worldTime = Mathf.Clamp01(world.worldTime);
        world.worldDay = Mathf.Clamp(world.worldDay, 0, 1000000);
        world.brokenPanels = CapList(world.brokenPanels, MaxPanels);
        world.fixerIssueIds = CapList(world.fixerIssueIds, MaxPanels);
        float[] since = world.fixerIssueSince ?? Array.Empty<float>();
        if (since.Length != world.fixerIssueIds.Length) { world.fixerIssueIds = Array.Empty<string>(); since = Array.Empty<float>(); }
        for (int i = 0; i < since.Length; i++) since[i] = Finite(since[i]);
        world.fixerIssueSince = since;
        return world;
    }

    private static bool Sane(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

    private static float Finite(float value) => Sane(value) ? Mathf.Max(0f, value) : 0f;

    private static string NameOr(Type enumType, string value, string fallback) =>
        value != null && value.Length <= 32 && Enum.IsDefined(enumType, value) ? value : fallback;

    private static string[] CapList(string[] values, int max)
    {
        if (values == null) return Array.Empty<string>();
        int count = Mathf.Min(values.Length, max);
        var result = new string[count];
        for (int i = 0; i < count; i++)
        {
            string value = values[i] ?? string.Empty;
            result[i] = value.Length > MaxTextLength ? value.Substring(0, MaxTextLength) : value;
        }
        return result;
    }
}
