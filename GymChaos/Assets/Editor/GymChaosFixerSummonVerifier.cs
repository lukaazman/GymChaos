#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Reception's F call for Jolly Dog. Looking at the receptionist, F offers the
/// call (the radio on the same counter keeps F when looked at). With nothing
/// broken the call does nothing; with one moved prop it brings him at night,
/// well before five days. He lands outside under open sky, walks in through
/// the door, repairs, walks out and only takes off outside. He wears a name
/// label like the other characters; touchdown and take-off play
/// floor_start_end, each repair plays glitter. Logs GYMCHAOS_FIXER_SUMMON_OK.
/// </summary>
public static class GymChaosFixerSummonVerifier
{
    private const string RequestedKey = "GymChaos.FixerSummonVerificationRequested";
    private const string ProgressionKey = "GymChaos.Progression.v1";
    private const string ProgressionBackupKey = "GymChaos.FixerSummonVerifier.ProgressionBackup";
    private const string ProgressionHadKey = "GymChaos.FixerSummonVerifier.ProgressionHad";
    private const float NightTime = 0.9f;

    private static IEnumerator script;
    private static double started;
    private static double waitUntil;
    private static float savedMaximumDeltaTime;

    [InitializeOnLoadMethod]
    private static void ResumeAfterReload()
    {
        if (!SessionState.GetBool(RequestedKey, false)) return;
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
        EditorApplication.playModeStateChanged -= PlayModeChanged;
        EditorApplication.playModeStateChanged += PlayModeChanged;
    }

    public static void Run()
    {
        SessionState.SetBool(ProgressionHadKey, PlayerPrefs.HasKey(ProgressionKey));
        SessionState.SetString(ProgressionBackupKey, PlayerPrefs.GetString(ProgressionKey, string.Empty));
        PlayerPrefs.DeleteKey(ProgressionKey);
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        SessionState.SetBool(RequestedKey, true);
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
        EditorApplication.playModeStateChanged -= PlayModeChanged;
        EditorApplication.playModeStateChanged += PlayModeChanged;
        EditorApplication.isPlaying = true;
    }

    private static void PlayModeChanged(PlayModeStateChange change)
    {
        if (change == PlayModeStateChange.EnteredPlayMode)
        {
            started = EditorApplication.timeSinceStartup;
            waitUntil = 0d;
            savedMaximumDeltaTime = Time.maximumDeltaTime;
            Time.maximumDeltaTime = 1f / 30f;
            AudioListener.pause = true;
            script = Scenario();
        }
        if (change != PlayModeStateChange.EnteredEditMode) return;
        EditorApplication.update -= Tick;
        EditorApplication.playModeStateChanged -= PlayModeChanged;
        RestoreProgression();
        if (Application.isBatchMode && SessionState.GetBool(RequestedKey, false))
            EditorApplication.Exit(1);
    }

    private static void Tick()
    {
        if (!EditorApplication.isPlaying || script == null) return;
        AudioListener.pause = true;
        try
        {
            if (EditorApplication.timeSinceStartup - started > 300d)
                throw new InvalidOperationException("Fixer summon verification timed out.");
            if (EditorApplication.timeSinceStartup < waitUntil) return;
            if (!script.MoveNext())
            {
                script = null;
                Debug.Log("GYMCHAOS_FIXER_SUMMON_OK");
                Finish(0);
                return;
            }
            if (script.Current is float seconds)
                waitUntil = EditorApplication.timeSinceStartup + seconds;
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            Debug.LogError("GYMCHAOS_FIXER_SUMMON_FAILED " + exception.Message);
            script = null;
            Finish(1);
        }
    }

    private static IEnumerator Scenario()
    {
        foreach (object step in Until(() => GymFixerDirector.Instance != null &&
                     GymTimeOfDay.Instance != null && GymDialogueDirector.Active != null &&
                     GymOutdoorBuilder.IsBuilt && GymDoorway.Instance != null,
                     90d, "the fixer director")) yield return step;
        GymFixerDirector fixers = GymFixerDirector.Instance;
        GymTimeOfDay time = GymTimeOfDay.Instance;
        fixers.SetSpawnSuspendedForVerification(true);
        GymVisitorDirector.Instance?.PauseVisitorScheduleForVerification();
        PlayerMovement player = UnityEngine.Object.FindAnyObjectByType<PlayerMovement>();
        Require(player != null, "No player");
        EnemyFighter receptionist = null;
        foreach (EnemyFighter fighter in UnityEngine.Object.FindObjectsByType<EnemyFighter>(FindObjectsSortMode.None))
            if (fighter.Identity == BodybuilderIdentity.Manwithsuit1) receptionist = fighter;
        Require(receptionist != null, "Receptionist is missing");

        yield return 4f;
        fixers.ScanNowForVerification();
        Require(fixers.CurrentIssues.Count == 0,
            $"A fresh gym already reports {fixers.CurrentIssues.Count} issues");

        // --- F at the reception desk ------------------------------------
        // Step up to the desk, 2.6 m from the receptionist on the player's side.
        Vector3 fromDesk = Vector3.ProjectOnPlane(
            player.transform.position - receptionist.transform.position, Vector3.up).normalized;
        Vector3 standAt = receptionist.transform.position + fromDesk * 2.6f;
        standAt.y = player.transform.position.y;
        CharacterController controller = player.GetComponent<CharacterController>();
        if (controller != null) controller.enabled = false;
        player.transform.position = standAt;
        if (controller != null) controller.enabled = true;
        Physics.SyncTransforms();
        Vector3 receptionHead = receptionist.transform.position + Vector3.up * 1.5f;
        player.LookAtForVerification(receptionHead);
        PlayerMovement.ContextAction atReception = player.ResolveFActionForVerification();
        Require(player.NearbyTalkTargetForVerification == receptionist,
            $"Receptionist is not the talk target from the start spot ({player.NearbyTalkTargetForVerification}) " +
            $"player={player.transform.position} receptionist={receptionist.transform.position} " +
            $"outside={GymOutdoorBuilder.IsPlayerOutsideGym(player.transform.position)} " +
            $"enabled={receptionist.isActiveAndEnabled} dead={receptionist.IsDead} aggressive={receptionist.IsAggressive}");
        Require(atReception == PlayerMovement.ContextAction.CallFixer,
            $"F at the receptionist resolves to {atReception}, not CallFixer");
        GymRadio radio = player.NearbyRadioForVerification;
        if (radio != null)
        {
            player.LookAtForVerification(radio.transform.position);
            PlayerMovement.ContextAction atRadio = player.ResolveFActionForVerification();
            Require(atRadio == PlayerMovement.ContextAction.Radio,
                $"F looking at the radio resolves to {atRadio}, not Radio");
            player.LookAtForVerification(receptionHead);
        }

        player.PressCallFixerForVerification();
        yield return 0.5f;
        Require(!fixers.IsVisitActive, "Calling with nothing broken still brought Jolly Dog");

        // --- break one prop, call at night --------------------------------
        PickupItem movedItem = FindLooseItem(fixers);
        Require(movedItem != null, "No resting loose prop inside the gym");
        Rigidbody itemBody = movedItem.GetComponent<Rigidbody>();
        GymInteriorBuilder.TryGetMainGymBounds(out Bounds gym);
        Vector3 drop = new Vector3(gym.center.x + 1.3f, gym.min.y + 0.6f, gym.center.z - 1.7f);
        itemBody.position = drop;
        movedItem.transform.position = drop;
        itemBody.linearVelocity = Vector3.zero;
        Physics.SyncTransforms();
        time.SetTimeForVerification(NightTime);
        // The prop must come to rest for two seconds before it counts.
        for (int i = 0; i < 24 && fixers.CurrentIssues.Count == 0; i++)
        {
            yield return 0.5f;
            fixers.ScanNowForVerification();
        }
        Require(fixers.CurrentIssues.Count >= 1,
            $"The moved prop is not reported as an issue: {movedItem.name} at {movedItem.transform.position}");
        Require(fixers.OldestIssueAgeDays < 1f, $"Issue is already {fixers.OldestIssueAgeDays:F2} days old");
        Require(time.IsNight, "Clock is not at night");

        Require(player.ResolveFActionForVerification() == PlayerMovement.ContextAction.CallFixer,
            "F no longer offers the call once something is broken");
        int floorBefore = PlayCount(GymSoundEffect.FixerFloor);
        int glitterBefore = PlayCount(GymSoundEffect.FixerGlitter);
        player.PressCallFixerForVerification();
        Require(fixers.IsVisitActive, "The reception call did not bring Jolly Dog");
        Require(fixers.Summon() == GymFixerDirector.SummonResult.AlreadyHere,
            "A second call during the visit was not refused");
        Require(player.ResolveFActionForVerification() != PlayerMovement.ContextAction.CallFixer,
            "F still offers the call while he is already here");

        GymFixerAgent dog = fixers.Agent;
        Require(dog != null && dog.State == GymFixerAgent.FixerState.Falling, "Visit did not start falling");
        Require(dog.GetComponentInChildren<ScreenSpaceCharacterLabel>(true) != null, "Jolly Dog has no name label");
        Vector3 landing = dog.LandingPoint;
        Require(GymOutdoorBuilder.IsPlayerOutsideGym(landing), $"Landing point {landing} is inside the gym");
        Require(GymFixerAgent.HasOpenSky(landing + Vector3.up * 0.2f), $"Landing point {landing} is under a roof");
        Require(dog.transform.position.y - landing.y > 40f, "Jolly Dog did not start high in the sky");

        bool cameInside = false;
        bool airborneInside = false;
        Vector3 airborneInsideAt = Vector3.zero;
        int fixedBefore = fixers.FixedCount;
        foreach (object step in Until(() =>
                 {
                     GymFixerAgent.FixerState state = dog.State;
                     Vector3 position = dog.transform.position;
                     bool outside = GymOutdoorBuilder.IsPlayerOutsideGym(position);
                     if (state == GymFixerAgent.FixerState.Running ||
                         state == GymFixerAgent.FixerState.Casting) cameInside |= !outside;
                     if ((state == GymFixerAgent.FixerState.Falling ||
                          state == GymFixerAgent.FixerState.Landing ||
                          state == GymFixerAgent.FixerState.Flying) && !outside && !airborneInside)
                     {
                         airborneInside = true;
                         airborneInsideAt = position;
                     }
                     return state == GymFixerAgent.FixerState.Hidden;
                 }, 200d, "the summoned visit to end")) yield return step;

        string history = string.Join(">", dog.StateHistory);
        Debug.Log($"GYMCHAOS_FIXER_SUMMON_VISIT states={history} landing={landing} " +
            $"cameInside={cameInside} fixed={fixers.FixedCount - fixedBefore}");
        RequireOrder(dog.StateHistory, "Falling", "Landing", "Running", "Casting", "Exiting", "Flying", "Hidden");
        Require(!airborneInside, $"Fell or flew inside the building at {airborneInsideAt}");
        Require(cameInside, "Never walked into the gym");
        Require(dog.TakeoffHadOpenSky && dog.TookOffOutside, "Flew off under a ceiling or inside");
        Require(fixers.FixedCount - fixedBefore >= 1, "The moved prop was not repaired");
        // Touchdown and take-off each play floor_start_end; every repair glitter.
        yield return 1f;
        int floorPlays = PlayCount(GymSoundEffect.FixerFloor) - floorBefore;
        int glitterPlays = PlayCount(GymSoundEffect.FixerGlitter) - glitterBefore;
        Debug.Log($"GYMCHAOS_FIXER_SUMMON_SOUNDS floor={floorPlays} glitter={glitterPlays}");
        Require(floorPlays == 2, $"floor_start_end played {floorPlays} times, expected landing + take-off");
        Require(glitterPlays == fixers.FixedCount - fixedBefore,
            $"glitter played {glitterPlays} times for {fixers.FixedCount - fixedBefore} repairs");
    }

    private static int PlayCount(GymSoundEffect effect) =>
        GymAudio.PlayCountsForVerification.TryGetValue(effect, out int count) ? count : 0;

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static IEnumerable Until(Func<bool> condition, double timeoutSeconds, string what)
    {
        double deadline = EditorApplication.timeSinceStartup + timeoutSeconds;
        while (!condition())
        {
            if (EditorApplication.timeSinceStartup > deadline)
                throw new InvalidOperationException("Timed out waiting for " + what);
            yield return null;
        }
    }

    private static void RequireOrder(List<string> history, params string[] expected)
    {
        int cursor = 0;
        for (int i = 0; i < history.Count && cursor < expected.Length; i++)
            if (history[i] == expected[cursor]) cursor++;
        Require(cursor == expected.Length, "State order wrong: " + string.Join(">", history));
    }

    private static PickupItem FindLooseItem(GymFixerDirector fixers)
    {
        if (!GymInteriorBuilder.TryGetMainGymBounds(out Bounds gym)) return null;
        foreach (PickupItem item in UnityEngine.Object.FindObjectsByType<PickupItem>(FindObjectsSortMode.None))
        {
            Rigidbody body = item.GetComponent<Rigidbody>();
            if (body == null || body.isKinematic || item.IsHeld || item.ItemType == WeightType.None ||
                item.ItemType == WeightType.Radio || item.ItemType == WeightType.Barbell ||
                item.ItemType == WeightType.Ball) continue;
            Vector3 position = item.transform.position;
            if (position.x > gym.min.x && position.x < gym.max.x && position.z > gym.min.z && position.z < gym.max.z &&
                Vector3.Distance(position, new Vector3(gym.center.x + 1.3f, position.y, gym.center.z - 1.7f)) > 1f &&
                fixers.IsTrackingForVerification(item))
                return item;
        }
        return null;
    }

    private static void RestoreProgression()
    {
        if (!SessionState.GetBool(ProgressionHadKey, false))
            PlayerPrefs.DeleteKey(ProgressionKey);
        else
            PlayerPrefs.SetString(ProgressionKey, SessionState.GetString(ProgressionBackupKey, string.Empty));
        PlayerPrefs.Save();
    }

    private static void Finish(int code)
    {
        Time.maximumDeltaTime = savedMaximumDeltaTime;
        SessionState.EraseBool(RequestedKey);
        EditorApplication.update -= Tick;
        RestoreProgression();
        if (Application.isBatchMode)
        {
            GymChaosVerifierExit.Record(code);
            EditorApplication.Exit(code);
        }
        else
        {
            EditorApplication.isPlaying = false;
        }
    }
}
#endif
