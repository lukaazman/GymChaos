#if UNITY_EDITOR
using System;
using System.Collections;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Appearance menu gate: three tabs build, PLAYER is the page it opens on,
/// every POTATO plate starts locked with its unlock hint on hover, picking a
/// version writes the character save, and in a room the guest renders the
/// host's table read-only. Logs GYMCHAOS_APPEARANCE_MENU_OK.
/// </summary>
[InitializeOnLoad]
public static class GymChaosAppearanceMenuVerifier
{
    private const string Key = "GymChaos.AppearanceMenuVerificationRequested";
    private static bool launched;

    static GymChaosAppearanceMenuVerifier() => GymChaosVerifierHooks.Hook(Tick, OnPlayModeChanged);

    [MenuItem("Tools/GymChaos/Run Appearance Menu Verification")]
    public static void Run()
    {
        launched = false;
        GymChaosVerifierExit.Record(1);
        GymChaosVerifierSave.Neutralize();
        SessionState.SetBool(Key, true);
        GymChaosVerificationSession.Begin();
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        EditorApplication.isPlaying = true;
    }

    private static void OnPlayModeChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredEditMode)
        {
            launched = false;
            GymChaosVerificationSession.End();
            return;
        }
        if (state != PlayModeStateChange.EnteredPlayMode) return;
        GymVersionedLooks.ApplyHostVersions(null, null);
    }

    private static void Tick()
    {
        if (!SessionState.GetBool(Key, false) || !EditorApplication.isPlaying || launched) return;
        launched = true;
        new GameObject("Appearance menu verifier").AddComponent<GymChaosAppearanceMenuVerifierRunner>();
    }

    public static void Finish(int code)
    {
        SessionState.EraseBool(Key);
        GymVersionedLooks.ApplyHostVersions(null, null);
        GymChaosVerifierExit.Record(code);
        if (Application.isBatchMode)
        {
            GymChaosVerifierExit.Exit(code);
            return;
        }
        EditorApplication.isPlaying = false;
    }
}

public sealed class GymChaosAppearanceMenuVerifierRunner : MonoBehaviour
{
    private static readonly string[] TabNames = { "PLAYER", "MEMBERS", "VEHICLES" };

    private bool failed;

    private IEnumerator Start()
    {
        IEnumerator scenario = Scenario();
        while (true)
        {
            object step;
            try
            {
                if (!scenario.MoveNext()) break;
                step = scenario.Current;
            }
            catch (Exception exception)
            {
                Debug.LogError("GYMCHAOS_APPEARANCE_MENU_FAILED " + exception);
                failed = true;
                break;
            }
            yield return step;
        }
        GymChaosAppearanceMenuVerifier.Finish(failed ? 1 : 0);
    }

    private IEnumerator Scenario()
    {
        var report = new StringBuilder();
        {
            // A live session, started the way GymChaosClassSessionPlay does.
            foreach (float wait in GymChaosVerificationSession.WaitForBootMenu()) yield return wait;
            foreach (float wait in GymChaosVerificationSession.StartNewCharacter(
                "bodybuilding", 180, "Look Tester")) yield return wait;
            GymExperienceService service = GymExperienceService.Active;
            Require(service != null, "no experience service");
            GymLockerMenuUI ui = GymLockerMenuUI.Ensure(service);
            ui.ShowForVerification();
            yield return null;

            // 1. Three tabs, PLAYER selected, PLAYER page shown.
            Require(ui.TabButtonsForVerification.Length == 3, "the locker does not have three tabs");
            for (int i = 0; i < TabNames.Length; i++)
            {
                Button tab = ui.TabButtonsForVerification[i];
                Require(tab != null, "tab " + TabNames[i] + " is missing");
                Require(tab.GetComponentInChildren<Text>().text.ToUpperInvariant() == TabNames[i],
                    "tab " + i + " is labelled " + tab.GetComponentInChildren<Text>().text);
                Require(tab.GetComponent<PersonaMenuButton>().IsSelectedForVerification == (i == 0),
                    (i == 0 ? "PLAYER is not selected on open" : TabNames[i] + " is selected on open"));
            }
            Require(ui.TabForVerification == 0, "the locker did not open on PLAYER");
            Require(!ui.MembersTabShownForVerification && !ui.VehiclesTabShownForVerification,
                "a version page is visible behind the PLAYER page");

            // 2. Locked POTATO plates, each with its own hover hint.
            Require(ui.MemberSlopButtonsForVerification.Length == GymVersionedLooks.MemberCount,
                "the member page lost a row");
            Require(ui.VehicleSlopButtonsForVerification.Length == GymVersionedLooks.VehicleCount,
                "the vehicle page lost a row");
            for (int i = 0; i < ui.MemberSlopButtonsForVerification.Length; i++)
            {
                Require(!ui.MemberSlopButtonsForVerification[i].interactable,
                    GymVersionedLooks.MemberKeyAt(i) + " starts unlocked");
            }
            ui.SelectTab(1);
            yield return null;
            Require(ui.MembersTabShownForVerification && !ui.VehiclesTabShownForVerification,
                "MEMBERS did not replace the PLAYER page");
            ui.HoverForVerification(true, 0, true);
            Require(ui.HintTextForVerification == "TALK TO UNLOCK",
                "a locked member plate explains '" + ui.HintTextForVerification + "'");
            ui.HoverForVerification(true, 0, false);

            ui.SelectTab(2);
            yield return null;
            Require(ui.VehiclesTabShownForVerification && !ui.MembersTabShownForVerification,
                "VEHICLES did not replace the MEMBERS page");
            for (int i = 0; i < ui.VehicleSlopButtonsForVerification.Length; i++)
            {
                Require(!ui.VehicleSlopButtonsForVerification[i].interactable,
                    GymVersionedLooks.VehicleKeyAt(i) + " starts unlocked");
                ui.HoverForVerification(false, i, true);
                Require(ui.HintTextForVerification == GymVersionedLooks.VehicleRequirement(i),
                    GymVersionedLooks.VehicleKeyAt(i) + " explains '" + ui.HintTextForVerification + "'");
                ui.HoverForVerification(false, i, false);
            }

            // 3. A pick is written into the character save.
            int arnold = Array.IndexOf(GymVersionedLooks.MemberKeys, "arnold");
            Require(GymVersionedLooks.UnlockMember("arnold"), "talking to Arnold did not unlock his POTATO body");
            ui.SelectTab(1);
            yield return null;
            Button slop = ui.MemberSlopButtonsForVerification[arnold];
            Require(slop.interactable, "Arnold's POTATO plate stayed locked after unlocking");
            int writesBefore = GymSessionService.CompletedWriteCount;
            slop.onClick.Invoke();
            yield return null;
            Require(Array.IndexOf(GymSessionService.Active.slopMembers, "arnold") >= 0,
                "the POTATO pick was not written to the active save");
            Require(GymVersionedLooks.IsSlop("arnold"), "the pick does not show up in the version table");
            for (float wait = 0f; wait < 10f && GymSessionService.CompletedWriteCount == writesBefore; wait += 0.1f)
                yield return null;
            Require(GymSessionService.CompletedWriteCount > writesBefore,
                "the save was not written after the pick");
            report.Append("member=arnold ");

            // 3b. The police cruiser's own plate: unlock it, pick POTATO, and the
            // pick reaches the save and the version table the scene reads.
            Require(GymVersionedLooks.UnlockVehicle("police"), "the police cruiser did not unlock");
            int police = Array.IndexOf(GymVersionedLooks.VehicleKeys, "police");
            Require(police >= 0, "the vehicle table has no police cruiser");
            ui.SelectTab(2);
            yield return null;
            Button policeSlop = ui.VehicleSlopButtonsForVerification[police];
            Require(policeSlop.interactable, "the police POTATO plate stayed locked after unlocking");
            policeSlop.onClick.Invoke();
            yield return null;
            Require(Array.IndexOf(GymSessionService.Active.slopVehicles, "police") >= 0,
                "the police POTATO pick was not written to the active save");
            Require(GymVersionedLooks.IsSlopVehicle("police"),
                "the police pick is not in the version table the scene reads");
            report.Append("vehicle=police ");

            // 4. A guest renders the host's table read-only.
            GymVersionedLooks.ApplyHostVersions(new[] { "davie" }, new[] { "bus" });
            Require(GymVersionedLooks.HostOverrideActive, "the host table was not applied");
            Require(GymVersionedLooks.IsSlop("davie"), "the guest does not render the host's POTATO Davie");
            Require(GymVersionedLooks.IsSlopVehicle("bus"), "the guest does not render the host's POTATO bus");
            Require(!GymVersionedLooks.SetMemberVersion("cbum", true), "a guest could change a member version");
            ui.SelectTab(1);
            yield return null;
            for (int i = 0; i < ui.MemberOgButtonsForVerification.Length; i++)
                Require(!ui.MemberOgButtonsForVerification[i].interactable && !ui.MemberSlopButtonsForVerification[i].interactable,
                    "a guest can still press a member version plate");
            Require(GymVersionedLooks.IsSlop("arnold") == false,
                "the guest kept its own pick instead of the host's table");
            GymVersionedLooks.ApplyHostVersions(null, null);
            Require(GymVersionedLooks.IsSlop("arnold"), "the guest's own pick was lost after the room");
            Require(!GymVersionedLooks.IsSlop("davie"), "the host table stayed applied after the room");
            report.Append("guest=readonly ");

            ui.HideForVerification();
            Debug.Log($"GYMCHAOS_APPEARANCE_MENU_OK tabs=3 player=default locked=potato " + report +
                $"members={GymVersionedLooks.MemberCount} vehicles={GymVersionedLooks.VehicleCount}");
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
#endif
