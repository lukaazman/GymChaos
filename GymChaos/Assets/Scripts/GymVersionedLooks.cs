using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Which asset a character or a vehicle wears: the DEFAULT original or its POTATO
/// remake. Chosen per character and per vehicle in the appearance menu, kept
/// in the character save, and unlocked by playing: talking to somebody opens
/// their POTATO body, winning races opens POTATO vehicles one at a time. A guest
/// in an online room uses the host's table instead of its own.
/// </summary>
public static class GymVersionedLooks
{
    public const string OgFile = "og";

    // Every character with a body in both versions: ten bodybuilders plus the
    // two residents. Larry has no vehicle; Hank has none either.
    public static readonly string[] MemberKeys =
    {
        "arnold", "cbum", "zyzz", "ronnie", "jaycutler", "goku", "davie", "policeman",
        "mark", "manwithsuit1", "hank", "larry"
    };

    private static readonly string[] MemberLabels =
    {
        "ARNOLD", "CBUM", "ZYZZ", "RONNIE", "JAY CUTLER", "GOKU", "DAVIE", "POLICEMAN",
        "MARK", "MANWITHSUIT", "HANK", "LARRY"
    };

    // Vehicles the remake ships: the five visitors' cars, the bus and the
    // police cruiser. Ronnie, Mark, Manwithsuit, Hank and Larry keep DEFAULT wheels.
    public static readonly string[] VehicleKeys = { "bus", "police", "arnold", "cbum", "jaycutler", "zyzz", "cloud" };

    private static readonly string[] VehicleLabels =
    {
        "DAVIE BUS", "POLICE CAR", "ARNOLD", "CBUM", "JAY CUTLER", "ZYZZ", "GOKU CLOUD"
    };

    private static readonly string[] VehicleRequirements =
    {
        "WIN EASY + NORMAL", "WIN MEDIUM + NORMAL", "WIN HARD + NORMAL",
        "WIN EASY + CHAOS", "WIN MEDIUM + CHAOS", "WIN HARD + CHAOS", "SET A NEW LAP RECORD"
    };

    // vehicle key per (difficulty, chaos) winning combination, matching the table above
    private static readonly string[] LadderVehicles = { "bus", "police", "arnold", "cbum", "jaycutler", "zyzz" };

    // A guest renders what the host picked; its own save keeps its choices for solo.
    private static string[] hostMembers;
    private static string[] hostVehicles;

    public static bool HostOverrideActive => hostMembers != null || hostVehicles != null;

    /// <summary>
    /// Verifier only: GYMCHAOS_VISUAL_STYLE=remake renders every character and
    /// vehicle on its POTATO asset, so the remake checks can run without a save.
    /// </summary>
    public static bool AllSlop { get; private set; }

    public static int MemberCount => MemberKeys.Length;
    public static int VehicleCount => VehicleKeys.Length;
    public static string MemberLabel(int index) => MemberLabels[index];
    public static string VehicleLabel(int index) => VehicleLabels[index];
    public static string VehicleRequirement(int index) => VehicleRequirements[index];
    public static string MemberKeyAt(int index) => MemberKeys[index];
    public static string VehicleKeyAt(int index) => VehicleKeys[index];

    /// <summary>The exported file name of a bodybuilder in both versions, or null (no remake).</summary>
    public static string MemberKey(BodybuilderIdentity identity)
    {
        switch (identity)
        {
            case BodybuilderIdentity.Arnold: return "arnold";
            case BodybuilderIdentity.Cbum: return "cbum";
            case BodybuilderIdentity.Zyzz: return "zyzz";
            case BodybuilderIdentity.Ronnie: return "ronnie";
            case BodybuilderIdentity.JayCutler: return "jaycutler";
            case BodybuilderIdentity.Goku: return "goku";
            case BodybuilderIdentity.Davie: return "davie";
            case BodybuilderIdentity.Policeman: return "policeman";
            case BodybuilderIdentity.Mark: return "mark";
            case BodybuilderIdentity.Manwithsuit1: return "manwithsuit1";
            default: return null;
        }
    }

    public static bool HasMemberVersion(BodybuilderIdentity identity) => MemberKey(identity) != null;

    private static GymCharacterSave Save => GymSessionService.Active;

    public static bool IsSlop(string key)
    {
        if (string.IsNullOrEmpty(key)) return false;
        if (AllSlop) return true;
        string[] source = HostOverrideActive ? hostMembers : Save != null ? Save.slopMembers : null;
        return Contains(source, key);
    }

    public static bool IsSlop(BodybuilderIdentity identity) => IsSlop(MemberKey(identity));

    public static bool IsSlopVehicle(string key)
    {
        if (string.IsNullOrEmpty(key)) return false;
        if (AllSlop) return true;
        string[] source = HostOverrideActive ? hostVehicles : Save != null ? Save.slopVehicles : null;
        return Contains(source, key);
    }

    public static bool IsMemberUnlocked(string key)
    {
        GymCharacterSave save = Save;
        return save == null || HostOverrideActive || Contains(save.unlockedMembers, key);
    }

    public static bool IsVehicleUnlocked(string key)
    {
        GymCharacterSave save = Save;
        return save == null || HostOverrideActive || Contains(save.unlockedVehicles, key);
    }

    /// <summary>Picks the version for a member; locked POTATO is refused.</summary>
    public static bool SetMemberVersion(string key, bool slop)
    {
        GymCharacterSave save = Save;
        if (save == null || string.IsNullOrEmpty(key) || HostOverrideActive) return false;
        if (slop && !Contains(save.unlockedMembers, key)) return false;
        save.slopMembers = With(save.slopMembers, key, slop);
        if (GymSessionService.Active != null) GymSessionService.RequestSave("member version");
        // Running session: the swap happens now, not on the next scene load.
        GymVisualRebuild.ApplyAll();
        return true;
    }

    public static bool SetVehicleVersion(string key, bool slop)
    {
        GymCharacterSave save = Save;
        if (save == null || string.IsNullOrEmpty(key) || HostOverrideActive) return false;
        if (slop && !Contains(save.unlockedVehicles, key)) return false;
        save.slopVehicles = With(save.slopVehicles, key, slop);
        if (GymSessionService.Active != null) GymSessionService.RequestSave("vehicle version");
        return true;
    }

    /// <summary>Talking to somebody unlocks that person's POTATO body.</summary>
    public static bool UnlockMember(string key)
    {
        GymCharacterSave save = Save;
        if (save == null || string.IsNullOrEmpty(key) || Contains(save.unlockedMembers, key)) return false;
        save.unlockedMembers = Append(save.unlockedMembers, key);
        GymHud.Active?.PushNotice("POTATO version unlocked: " + Label(key), 4f, 2);
        GymSessionService.RequestSave("member unlock");
        Debug.Log("GYMCHAOS_MEMBER_UNLOCKED key=" + key);
        return true;
    }

    /// <summary>A dialogue ended; the speaker's POTATO body is now available.</summary>
    public static void UnlockForSpeaker(string speakerKey)
    {
        if (string.IsNullOrEmpty(speakerKey)) return;
        string key = speakerKey.ToLowerInvariant();
        if (key == "homelessguy" || key == "homeless") key = "hank";
        if (key == "fixer" || key == "jollydog") key = "larry";
        if (Array.IndexOf(MemberKeys, key) < 0) return;
        UnlockMember(key);
    }

    /// <summary>
    /// A race ended: a win opens the vehicle for its difficulty and mode, and a
    /// new personal best lap opens the cloud car. One vehicle per condition.
    /// </summary>
    public static void AwardRace(int difficulty, bool chaos, bool won, bool newBestLap)
    {
        GymCharacterSave save = Save;
        if (save == null) return;
        if (won)
        {
            int index = Mathf.Clamp(difficulty, 0, 2) + (chaos ? 3 : 0);
            UnlockVehicle(LadderVehicles[index]);
        }
        if (newBestLap) UnlockVehicle("cloud");
    }

    public static bool UnlockVehicle(string key)
    {
        GymCharacterSave save = Save;
        if (save == null || string.IsNullOrEmpty(key) || Contains(save.unlockedVehicles, key)) return false;
        save.unlockedVehicles = Append(save.unlockedVehicles, key);
        GymHud.Active?.PushNotice("POTATO vehicle unlocked: " + Label(key), 4f, 2);
        GymSessionService.RequestSave("vehicle unlock");
        Debug.Log("GYMCHAOS_VEHICLE_UNLOCKED key=" + key);
        return true;
    }

    /// <summary>The host's table, sent with the world snapshot; null clears it.</summary>
    public static void ApplyHostVersions(string[] members, string[] vehicles)
    {
        hostMembers = members != null && members.Length > 0 ? members : null;
        hostVehicles = vehicles != null && vehicles.Length > 0 ? vehicles : null;
        GymVisualRebuild.ApplyAll();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        hostMembers = null;
        hostVehicles = null;
        AllSlop = Environment.GetEnvironmentVariable("GYMCHAOS_VISUAL_STYLE") == "remake";
    }

    /// <summary>The display name of a member key (notices).</summary>
    public static string Label(string key)
    {
        int index = Array.IndexOf(MemberKeys, key);
        if (index >= 0) return MemberLabels[index];
        index = Array.IndexOf(VehicleKeys, key);
        return index >= 0 ? VehicleLabels[index] : key;
    }

    private static bool Contains(string[] list, string key) =>
        list != null && list.Length > 0 && Array.IndexOf(list, key) >= 0;

    private static string[] With(string[] list, string key, bool add)
    {
        var result = new List<string>();
        if (list != null)
        {
            for (int i = 0; i < list.Length; i++)
            {
                if (!string.IsNullOrEmpty(list[i]) && list[i] != key) result.Add(list[i]);
            }
        }
        if (add) result.Add(key);
        return result.ToArray();
    }

    private static string[] Append(string[] list, string key)
    {
        if (Contains(list, key)) return list;
        var result = new List<string>();
        if (list != null)
        {
            for (int i = 0; i < list.Length; i++)
            {
                if (!string.IsNullOrEmpty(list[i])) result.Add(list[i]);
            }
        }
        result.Add(key);
        return result.ToArray();
    }
}
