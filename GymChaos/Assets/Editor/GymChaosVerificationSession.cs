#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Boots a real gameplay session for a play-mode verifier the way
/// <see cref="GymChaosClassSessionPlay"/> does: a temporary save directory (so
/// the player's own save is never touched), the real boot menu, then New Game
/// through the menu's own entry points (the same calls its buttons make).
/// </summary>
public static class GymChaosVerificationSession
{
    private const string DirectoryKey = "GymChaos.VerificationSession.Directory";

    /// <summary>Points the save repository at a throwaway directory; call before entering play mode.</summary>
    public static void Begin()
    {
        string directory = Path.Combine(Path.GetTempPath(), "GymChaosSession-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        SessionState.SetString(DirectoryKey, directory);
        Environment.SetEnvironmentVariable(GymSessionService.VerificationDirectoryVariable, directory);
    }

    public static void End()
    {
        Environment.SetEnvironmentVariable(GymSessionService.VerificationDirectoryVariable, null);
        string directory = SessionState.GetString(DirectoryKey, string.Empty);
        SessionState.EraseString(DirectoryKey);
        try
        {
            if (!string.IsNullOrEmpty(directory) && Directory.Exists(directory)) Directory.Delete(directory, true);
        }
        catch (IOException)
        {
            // A file still open on Windows: the temp folder can stay behind.
        }
    }

    public static IEnumerable<float> WaitForBootMenu(double seconds = 120d)
    {
        double deadline = EditorApplication.timeSinceStartup + seconds;
        while (!(GymStartScreen.IsMenuVisible && GymStartScreen.SessionMenu != null &&
            GymExperienceService.Active != null))
        {
            if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException("the boot menu");
            yield return 0f;
        }
        yield return 1f;
    }

    /// <summary>Play → New Game → class → name and height → Confirm, then the world starts.</summary>
    public static IEnumerable<float> StartNewCharacter(string classId, int heightCm, string username)
    {
        GymSessionMenu menu = GymStartScreen.SessionMenu;
        if (menu == null) throw new InvalidOperationException("the session menu is not built");

        // The menu locks input while its pages fade, so a click right after a
        // page change is dropped: retry the page's own entry point instead.
        GymStartScreen.PlayButton.onClick.Invoke();
        foreach (float wait in UntilState(menu, GymSessionMenu.State.SessionChoice, null,
            "the session choice")) yield return wait;

        foreach (float wait in UntilState(menu, GymSessionMenu.State.ClassSelect,
            menu.ChooseNewGame, "class selection")) yield return wait;

        double deadline = EditorApplication.timeSinceStartup + 20d;
        while (menu.SelectedClassId != classId)
        {
            menu.SelectClass(classId);
            if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException("the class pick");
            yield return 0.1f;
        }

        foreach (float wait in UntilState(menu, GymSessionMenu.State.Customize,
            menu.ConfirmClass, "the customize page")) yield return wait;
        yield return 0.4f;

        deadline = EditorApplication.timeSinceStartup + 10d;
        while (menu.CustomizeLook == null || menu.CustomizeLook.heightCm != heightCm)
        {
            menu.SetHeight(heightCm);
            if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException("the height slider");
            yield return 0.1f;
        }
        menu.SetUsername(username);

        deadline = EditorApplication.timeSinceStartup + 40d;
        while (!GymSessionService.HasActive || !GymArenaBootstrap.IsGameplayStarted)
        {
            if (menu.Current == GymSessionMenu.State.Customize) menu.ConfirmCustomize();
            if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException("the new session");
            yield return 0.1f;
        }
        yield return 1f;
    }

    // Clicks a page's button until the menu leaves that page (the page lock
    // swallows the first click), then checks where it landed.
    private static IEnumerable<float> UntilState(GymSessionMenu menu, GymSessionMenu.State target,
        Action step, string what)
    {
        double deadline = EditorApplication.timeSinceStartup + 20d;
        while (menu.Current != target)
        {
            step?.Invoke();
            if (EditorApplication.timeSinceStartup > deadline)
            {
                throw new TimeoutException(what + " (still " + menu.Current + ")");
            }
            yield return 0.1f;
        }
        yield return 0.2f;
    }
}
#endif
