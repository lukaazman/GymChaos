#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// The per-character and per-vehicle DEFAULT/POTATO tables: every member with both
/// bodies has an entry and an asset, every vehicle has an entry and a GLB, the
/// default is DEFAULT on all of them, and a character only turns POTATO when its
/// version is chosen and unlocked. Logs GYMCHAOS_VERSIONED_LOOKS_OK.
/// </summary>
public static class GymChaosVersionedLooksVerifier
{
    [MenuItem("Tools/GymChaos/Run Versioned Looks Verification")]
    public static void Run()
    {
        try
        {
            VerifyTables();
            VerifyMemberAssets();
            VerifyVehicleAssets();
            VerifyDefaults();
            Debug.Log($"GYMCHAOS_VERSIONED_LOOKS_OK members={GymVersionedLooks.MemberCount} " +
                $"vehicles={GymVersionedLooks.VehicleCount}");
            EditorApplication.Exit(0);
        }
        catch (Exception exception)
        {
            Debug.LogError("GYMCHAOS_VERSIONED_LOOKS_FAILED " + exception);
            EditorApplication.Exit(1);
        }
    }

    private static void VerifyTables()
    {
        var members = new HashSet<string>(GymVersionedLooks.MemberKeys, StringComparer.Ordinal);
        Require(members.Count == GymVersionedLooks.MemberCount, "duplicate member keys");
        Require(members.Contains("hank") && members.Contains("larry"),
            "Hank and Larry must have their own POTATO toggle");
        var vehicles = new HashSet<string>(GymVersionedLooks.VehicleKeys, StringComparer.Ordinal);
        Require(vehicles.Count == GymVersionedLooks.VehicleCount, "duplicate vehicle keys");
        for (int i = 0; i < GymVersionedLooks.MemberCount; i++)
            Require(!string.IsNullOrEmpty(GymVersionedLooks.MemberLabel(i)), "member " + i + " has no label");
        for (int i = 0; i < GymVersionedLooks.VehicleCount; i++)
        {
            Require(!string.IsNullOrEmpty(GymVersionedLooks.VehicleLabel(i)), "vehicle " + i + " has no label");
            Require(!string.IsNullOrEmpty(GymVersionedLooks.VehicleRequirement(i)),
                "vehicle " + i + " has no unlock requirement");
        }
    }

    // Every bodybuilder with both models resolves to its remake file, and Hank
    // and Larry do too (their own file names).
    private static void VerifyMemberAssets()
    {
        int found = 0;
        foreach (BodybuilderIdentity identity in Enum.GetValues(typeof(BodybuilderIdentity)))
        {
            string key = GymVersionedLooks.MemberKey(identity);
            if (key == null) continue;
            found++;
            Require(Resources.Load<GameObject>(GymVisualStyle.ModelPath(key)) != null,
                identity + " has no POTATO model at " + GymVisualStyle.ModelPath(key));
            Require(Resources.Load<Texture2D>(GymVisualStyle.TexturePath(key)) != null,
                identity + " has no POTATO texture at " + GymVisualStyle.TexturePath(key));
        }
        Require(found == 10, "expected ten bodybuilders with both models, found " + found);
        foreach (string key in new[] { GymVisualStyle.HomelessFile, GymVisualStyle.FixerFile })
        {
            Require(Resources.Load<GameObject>(GymVisualStyle.ModelPath(key)) != null,
                key + " has no POTATO model");
            Require(Resources.Load<Texture2D>(GymVisualStyle.TexturePath(key)) != null,
                key + " has no POTATO texture");
        }
    }

    private static void VerifyVehicleAssets()
    {
        foreach (string key in GymVersionedLooks.VehicleKeys)
        {
            string path = GymVisualStyle.RemakeVehicleFolder + key + ".glb";
            string full = System.IO.Path.Combine(Application.streamingAssetsPath, path);
            Require(System.IO.File.Exists(full), "missing POTATO vehicle at StreamingAssets/" + path);
        }
    }

    // With no character loaded nothing is POTATO: the DEFAULT asset is the default for
    // every character and vehicle, and its names are the DEFAULT names.
    private static void VerifyDefaults()
    {
        Require(!GymVersionedLooks.AllSlop, "the POTATO override must be off outside a remake run");
        foreach (string key in GymVersionedLooks.MemberKeys)
            Require(!GymVersionedLooks.IsSlop(key), key + " defaults to POTATO");
        foreach (string key in GymVersionedLooks.VehicleKeys)
            Require(!GymVersionedLooks.IsSlopVehicle(key), key + " defaults to POTATO");
        Require(!GymVisualStyle.TryRemake(BodybuilderIdentity.Arnold, out _, out _),
            "Arnold has a remake path without choosing POTATO");
        Require(GymVisualStyle.DisplayName(BodybuilderIdentity.Arnold, "Arnold") == "Arnold",
            "the DEFAULT name must be kept");
        Require(GymVisualStyle.HomelessName("Hank") == "Hank", "Hank's DEFAULT name must be kept");
        Require(GymVisualStyle.FixerName("Lollipop Larry") == "Lollipop Larry", "Larry's DEFAULT name must be kept");
        // A save's tables always parse, even when an old file has none.
        var save = new GymCharacterSave();
        Require(save.slopMembers != null && save.slopVehicles != null &&
            save.unlockedMembers != null && save.unlockedVehicles != null,
            "a new save has no version tables");
        Require(GymCharacterSave.CurrentVersion == 5, "the save version must be 5");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
#endif
