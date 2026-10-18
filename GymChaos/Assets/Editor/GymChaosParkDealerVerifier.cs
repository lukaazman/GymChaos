#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Goal 2026-10-17 checks, through the real boot menu:
/// - manwithsuit1's nametag shows over the boot menu (not over the slot menu);
/// - the main gym has the industrial ceiling with the hex LED grid;
/// - park benches: E sits (not a workout) and stands again, the game clock
///   runs 2x while seated, and on Hank's bench the player sits beside him
///   with a small gap;
/// - Hank's friendly talk ends in his offer; accepting it unlocks F BUY next
///   to him; the stash overlay shows all five products as 3D models with
///   hidden details; buying gear applies effects past the supplement caps,
///   health costs (drain, instant harm, max-health toll) and a reputation hit.
/// Logs GYMCHAOS_MENU_NAMETAG_OK, GYMCHAOS_INDUSTRIAL_CEILING_OK,
/// GYMCHAOS_PARK_BENCH_OK, GYMCHAOS_HANK_DEALER_OK and GYMCHAOS_PARK_DEALER_OK.
/// </summary>
public static class GymChaosParkDealerVerifier
{
    private const string RequestedKey = "GymChaos.ParkDealerVerificationRequested";
    private const string SessionDirectoryKey = "GymChaos.ParkDealerVerifier.SessionDirectory";

    private static IEnumerator script;
    private static double started;
    private static double waitUntil;

    [InitializeOnLoadMethod]
    private static void ResumeAfterReload()
    {
        if (!SessionState.GetBool(RequestedKey, false)) return;
        Hook();
    }

    public static void Run()
    {
        GymChaosVerifierSave.Neutralize();
        // Character slots go to a throwaway folder, also in batch mode, so
        // the run boots through the real start screen.
        string directory = Path.Combine(Path.GetTempPath(), "GymChaosParkDealerVerify-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        SessionState.SetString(SessionDirectoryKey, directory);
        Environment.SetEnvironmentVariable(GymSessionService.VerificationDirectoryVariable, directory);
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        SessionState.SetBool(RequestedKey, true);
        Hook();
        EditorApplication.isPlaying = true;
    }

    private static void Hook()
    {
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
        EditorApplication.playModeStateChanged -= PlayModeChanged;
        EditorApplication.playModeStateChanged += PlayModeChanged;
    }

    private static void PlayModeChanged(PlayModeStateChange change)
    {
        if (change == PlayModeStateChange.EnteredPlayMode)
        {
            started = EditorApplication.timeSinceStartup;
            waitUntil = 0d;
            AudioListener.pause = true;
            Application.runInBackground = true;
            script = Scenario();
        }
        if (change != PlayModeStateChange.EnteredEditMode) return;
        EditorApplication.update -= Tick;
        EditorApplication.playModeStateChanged -= PlayModeChanged;
        Cleanup();
        if (Application.isBatchMode && SessionState.GetBool(RequestedKey, false))
            EditorApplication.Exit(1);
    }

    private static void Tick()
    {
        if (!EditorApplication.isPlaying || script == null) return;
        AudioListener.pause = true;
        if (!Application.isBatchMode) EditorApplication.QueuePlayerLoopUpdate();
        try
        {
            if (EditorApplication.timeSinceStartup - started > 300d)
                throw new InvalidOperationException("Park/dealer verification timed out.");
            if (EditorApplication.timeSinceStartup < waitUntil) return;
            if (!script.MoveNext())
            {
                script = null;
                Debug.Log("GYMCHAOS_PARK_DEALER_OK");
                Finish(0);
                return;
            }
            if (script.Current is float seconds)
                waitUntil = EditorApplication.timeSinceStartup + seconds;
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            Debug.LogError("GYMCHAOS_PARK_DEALER_FAILED " + exception.Message);
            script = null;
            GymDealerShopUI.Close();
            Finish(1);
        }
    }

    private static IEnumerator Scenario()
    {
        // --- boot menu nametag -------------------------------------------
        foreach (object step in Until(() => GymStartScreen.IsMenuVisible && GymStartScreen.SessionMenu != null &&
                     ReceptionistLabel() != null && GymExperienceService.Active != null, 120d,
                     "the boot menu with the receptionist")) yield return step;
        yield return 0.5f;
        ScreenSpaceCharacterLabel label = ReceptionistLabel();
        Require(GymStartScreen.ShowsReceptionistNametag && label.WouldShowNameInMenu,
            "manwithsuit1's nametag is hidden on the boot menu");
        Camera view = Camera.main;
        Vector3 screen = view.WorldToScreenPoint(label.transform.position + Vector3.up * 2.2f);
        Require(screen.z > 0f && screen.x > Screen.width * 0.5f && screen.x < Screen.width,
            $"receptionist is not in the open right side of the menu (screen={screen})");
        GymSessionMenu menu = GymStartScreen.SessionMenu;
        GymStartScreen.PlayButton.onClick.Invoke();
        foreach (object step in Until(() => menu.Current == GymSessionMenu.State.SessionChoice, 20d,
                     "the session choice")) yield return step;
        Require(!GymStartScreen.ShowsReceptionistNametag, "nametag stays over the slot menu");
        Debug.Log($"GYMCHAOS_MENU_NAMETAG_OK screenX={screen.x / Screen.width:F2}");
        foreach (object step in Until(() =>
                 {
                     menu.ChooseNewGame();
                     return menu.Current == GymSessionMenu.State.ClassSelect;
                 }, 20d, "class selection")) yield return step;
        foreach (object step in Until(() =>
                 {
                     menu.ConfirmClass();
                     return menu.Current != GymSessionMenu.State.ClassSelect;
                 }, 20d, "the class confirmation")) yield return step;
        foreach (object step in Until(() => GymArenaBootstrap.IsGameplayStarted, 60d, "gameplay after the menu"))
            yield return step;

        // --- industrial ceiling -------------------------------------------
        GameObject ceiling = GameObject.Find(GymIndustrialCeiling.RootName);
        Require(ceiling != null, "no industrial ceiling");
        Transform hex = ceiling.transform.Find(GymIndustrialCeiling.HexGridName);
        Require(hex != null && hex.GetComponent<MeshRenderer>() != null, "no hex LED grid");
        Require(GymIndustrialCeiling.HexSegmentCount >= 120, $"hex grid too small ({GymIndustrialCeiling.HexSegmentCount})");
        Require(ceiling.transform.Find("Ceiling Trusses") != null && ceiling.transform.Find("Ceiling Deck") != null &&
            ceiling.transform.Find("Ceiling Ducts") != null, "trusses, deck or ducts missing");
        Require(GameObject.Find("LED panel") == null, "old flat LED panels still hang");
        Require(GymInteriorBuilder.TryGetMainGymBounds(out Bounds gym), "no gym bounds");
        Bounds grid = GymIndustrialCeiling.HexGridBounds;
        Require(gym.Contains(grid.min) && gym.Contains(grid.max) && grid.size.x > gym.size.x * 0.6f &&
            grid.size.z > gym.size.z * 0.6f, $"hex grid {grid} does not span the gym {gym}");
        Require(grid.center.y < gym.max.y - 1f && grid.center.y > gym.min.y + 5.5f,
            $"hex grid height {grid.center.y:F2} not hung under the trusses");
        Debug.Log($"GYMCHAOS_INDUSTRIAL_CEILING_OK hexSegments={GymIndustrialCeiling.HexSegmentCount} grid={grid.size}");

        // --- park benches ---------------------------------------------------
        GymTimeOfDay time = GymTimeOfDay.Instance;
        foreach (object step in Until(() => GymHomelessAgent.Instance != null && GymHomelessAgent.Instance.IsReady &&
                     GymDialogueDirector.Active != null, 120d, "homeless Hank")) yield return step;
        GymHomelessAgent hank = GymHomelessAgent.Instance;
        time.SetTimeForVerification(0.5f);
        hank.SitOnBenchForVerification();
        yield return 0.6f;
        Require(hank.State == GymHomelessAgent.HomelessState.Sitting && hank.OccupiesBench,
            $"Hank is not on his bench ({hank.State})");
        PlayerMovement player = UnityEngine.Object.FindAnyObjectByType<PlayerMovement>();
        Require(player != null, "No player");
        GymExperienceService service = GymExperienceService.Active;
        GymProgressionState state = service.State;

        GymOutdoorFoliage.ParkBench hankBench = GymOutdoorFoliage.Benches[hank.BenchIndex];
        Vector3 along = Quaternion.LookRotation(hankBench.Facing, Vector3.up) * Vector3.right;
        Vector3 approach = hankBench.Position + hankBench.Facing * 1.25f + along * 0.9f;
        Teleport(player, approach);
        player.RefreshNearbyBenchForVerification();
        Require(player.NearbyBenchForVerification == hank.BenchIndex,
            $"Hank's bench not found nearby ({player.NearbyBenchForVerification} vs {hank.BenchIndex})");
        Vector3 seatPoint = player.GetBenchSeatHips(hank.BenchIndex, player.transform.position, out _);
        player.LookAtForVerification(seatPoint);
        player.RefreshNearbyBenchForVerification();
        Require(player.PrefersSitForVerification, "E does not offer SIT when looking at the free place");
        Require(PlayerMovement.ResolveContextAction(PlayerMovement.ContextKey.E, false, false, false, false,
            false, false, sit: true) == PlayerMovement.ContextAction.Sit, "E is not SIT on a bench");
        player.LookAtForVerification(hank.HeadBone.position);
        player.RefreshNearbyBenchForVerification();
        Require(!player.PrefersSitForVerification, "E does not offer TALK when looking at Hank");

        // Clock speed baseline while standing.
        float standRate = 0f;
        foreach (object step in MeasureClock(time, rate => standRate = rate)) yield return step;

        Require(player.SitOnBench(hank.BenchIndex), "could not sit on Hank's bench");
        yield return 0.3f;
        Require(player.IsSeatedOnBench && player.IsSeatedBesideHomeless, "not seated beside Hank");
        Require(!player.IsExercising, "sitting counts as a workout");
        Vector3 hankHips = hank.HipsBone.position;
        Vector3 hips = player.SeatedHipsPosition;
        float sideGap = Mathf.Abs(Vector3.Dot(hips - hankHips, along));
        float depthOff = Mathf.Abs(Vector3.Dot(hips - hankHips, hankBench.Facing));
        Require(sideGap >= 0.7f && sideGap <= 0.95f, $"seat beside Hank is {sideGap:F2} m away along the bench");
        Require(depthOff <= 0.3f, $"seat is {depthOff:F2} m in front of or behind Hank");
        float eyeAboveSeat = player.playerCamera.transform.position.y -
            (hankBench.Position.y + GymOutdoorFoliage.BenchSeatTop);
        Require(eyeAboveSeat > 0.6f && eyeAboveSeat < 1.1f, $"seated eye line {eyeAboveSeat:F2} m above the seat");
        Require(Mathf.Approximately(GymTimeOfDay.ClockScale, PlayerMovement.SeatedClockScale), "clock not 2x seated");
        float seatRate = 0f;
        foreach (object step in MeasureClock(time, rate => seatRate = rate)) yield return step;
        float ratio = seatRate / Mathf.Max(1e-6f, standRate);
        Require(ratio > 1.8f && ratio < 2.2f, $"seated clock runs {ratio:F2}x");
        Vector3 seatedAt = player.transform.position;
        player.StandUpFromBench();
        yield return 0.3f;
        Require(!player.IsSeatedOnBench && Mathf.Approximately(GymTimeOfDay.ClockScale, 1f),
            "stand up left the player seated or the clock fast");
        Require(player.GetComponent<CharacterController>().enabled, "controller off after standing");
        Require(Vector3.Dot(player.transform.position - seatedAt, hankBench.Facing) > 0.6f,
            "did not step forward off the bench");

        // A free bench: the side place, no homeless flag.
        int freeBench = -1;
        for (int i = 0; i < GymOutdoorFoliage.Benches.Count && freeBench < 0; i++)
            if (i != hank.BenchIndex && GymParkAccess.Contains(GymOutdoorFoliage.Benches[i].Position, 0.8f))
                freeBench = i;
        if (freeBench >= 0)
        {
            GymOutdoorFoliage.ParkBench bench = GymOutdoorFoliage.Benches[freeBench];
            Teleport(player, bench.Position + bench.Facing * 1.2f);
            Require(player.SitOnBench(freeBench) && player.IsSeatedOnBench && !player.IsSeatedBesideHomeless,
                "could not sit on a free bench");
            yield return 0.2f;
            player.StandUpFromBench();
        }
        Debug.Log($"GYMCHAOS_PARK_BENCH_OK sideGap={sideGap:F2} eye={eyeAboveSeat:F2} clockRatio={ratio:F2} " +
            $"freeBench={freeBench}");

        // --- Hank's offer -------------------------------------------------
        time.SetTimeForVerification(0.5f);
        state.hankDealerUnlocked = false;
        Teleport(player, hankBench.Position + hankBench.Facing * 1.6f);
        player.LookAtForVerification(hank.HeadBone.position);
        yield return 0.2f;
        Require(!GymDealerShopUI.CanDealWith(player), "Hank sells before the offer");
        Require(GymDialogueDirector.TryStartNearby(player, true), "could not talk to Hank");
        yield return 0.2f;
        GymDialogueDirector director = GymDialogueDirector.Active;
        director.ChooseFromUi(0); // You need anything?
        yield return 0.1f;
        director.ChooseFromUi(0); // I'll keep an eye out for you.
        yield return 0.1f;
        Require(GymDialogueDirector.IsDialogueActive, "friendly talk ended without the offer");
        director.ChooseFromUi(0); // Yeah. Show me what you've got.
        yield return 0.1f;
        Require(service.IsHankDealerUnlocked && state.hankDealerUnlocked, "accepting did not unlock the stash");
        director.ChooseFromUi(0); // Deal.
        foreach (object step in Until(() => !GymDialogueDirector.IsDialogueActive, 10d, "the talk to close"))
            yield return step;
        yield return 0.3f;
        player.LookAtForVerification(hank.HeadBone.position);
        Require(player.ResolveFActionForVerification() == PlayerMovement.ContextAction.Dealer,
            "F next to Hank does not offer BUY");
        Require(PlayerMovement.GetContextActionKey(PlayerMovement.ContextAction.Dealer) == PlayerMovement.ContextKey.F &&
            PlayerMovement.ResolveContextAction(PlayerMovement.ContextKey.E, true, false, false, false, false, false,
                dealer: true) == PlayerMovement.ContextAction.Talk, "E is not TALK beside F BUY");

        // --- the stash overlay ----------------------------------------------
        state.level = Mathf.Max(state.level, 10);
        state.loyaltyStamps = GymExperienceService.MaxLoyaltyStamps;
        GymBuffs.Clear();
        GymDealerShopUI shop = GymDealerShopUI.Open(player);
        yield return 0.3f;
        Require(shop != null && GymDealerShopUI.IsOpen && service.IsBlockingPlayerInput, "stash did not open");
        // Hank is frozen while you browse: past his leave time he still sits.
        Require(shop.PausedDealer == hank && hank.IsTalking, "Hank keeps his day going under the stash");
        Vector3 pausedAt = hank.transform.position;
        time.SetTimeForVerification(hank.LeaveBench01 + 0.02f);
        yield return 1.5f;
        Require(hank.State == GymHomelessAgent.HomelessState.Sitting &&
            Vector3.Distance(hank.transform.position, pausedAt) < 0.05f, $"Hank moved while browsing ({hank.State})");
        time.SetTimeForVerification(0.5f);
        Require(ScreenSpaceCharacterLabel.CurrentAlpha < 0.5f, "name labels stay opaque over the stash");
        for (int i = 0; i < GymDealerCatalog.Products.Length; i++)
        {
            foreach (object step in Until(() => shop.InspectView.ModelLoaded && shop.InspectView.ModelFitted, 20d,
                         "model " + shop.Current.id)) yield return step;
            Require(shop.EffectLabelForVerification.text.Contains(GymDealerCatalog.Unknown),
                shop.Current.id + " shows every detail");
            Require(shop.RiskLabelForVerification.text.Contains("#"), shop.Current.id + " has no risk meter");
            shop.Step(1);
            yield return 0.1f;
        }
        Require(shop.Index == 0, "browsing did not wrap round the five products");

        // Buy TREN: past the supplement cap, health costs, reputation hit.
        int rep = service.Reputation;
        int stamps = service.LoyaltyStamps;
        float healthBefore = player.CurrentHealth;
        Require(shop.Current.id == "dealer_tren", "first product is not TREN");
        Require(shop.Buy() == GymDealerShopUI.PurchaseResult.Bought, "could not buy TREN");
        float punch = GymBuffs.GetMultiplier(GymBuffStat.StrengthDamage);
        float cardio = GymBuffs.GetMultiplier(GymBuffStat.CardioCapacity);
        Require(punch > GymBuffs.MaxBoost + 0.1f, $"TREN punch x{punch:F2} not past the supplement cap");
        Require(cardio < 0.75f, $"TREN cardio x{cardio:F2} not hurt");
        Require(service.LoyaltyStamps == stamps - 6 && service.Reputation < rep, "price or reputation not taken");
        Require(GymBuffs.MaxHealthToll01 > 0.1f && player.CurrentHealth <= player.HealthCeiling + 0.01f &&
            player.CurrentHealth < healthBefore, "no instant harm or max-health toll");
        Require(GymBuffs.HealthDrainPerSecond >= 2f, "TREN does not drain health");
        // RAD-140 and BPC-157 fit; a fourth product hits the shared cap.
        shop.Step(3);
        Require(shop.Current.id == "dealer_rad140" && shop.Buy() == GymDealerShopUI.PurchaseResult.Bought, "RAD-140");
        shop.Step(1);
        Require(shop.Current.id == "dealer_bpc157" && shop.Buy() == GymDealerShopUI.PurchaseResult.Bought, "BPC-157");
        shop.Step(-3);
        Require(shop.Current.id == "dealer_anavar" &&
            shop.Buy() == GymDealerShopUI.PurchaseResult.TooManyActive, "fourth product ignored the cap");
        float tollAll = GymBuffs.MaxHealthToll01;
        Require(tollAll > 0.19f && tollAll <= GymBuffs.MaxHealthTollCap, $"stacked toll {tollAll:F2}");
        GymBuffs.ExpireAllForVerification();
        Require(GymBuffs.Active.Count > 0 && GymBuffs.GetMultiplier(GymBuffStat.StrengthDamage) < 1f,
            "TREN left no crash after it wore off");
        Require(GymBuffs.MaxHealthToll01 == tollAll, "max-health toll healed with the effect");
        GymDealerShopUI.Close();
        yield return 0.2f;
        Require(!hank.IsTalking && shop.PausedDealer == null, "Hank stays paused after the stash closed");
        Debug.Log($"GYMCHAOS_HANK_DEALER_OK punch={punch:F2} cardio={cardio:F2} toll={tollAll:F2} " +
            $"rep={rep}->{service.Reputation}");
    }

    private static IEnumerable MeasureClock(GymTimeOfDay time, Action<float> result)
    {
        time.SetTimeForVerification(0.5f);
        float t0 = time.Time01;
        float s0 = Time.time;
        yield return 2f;
        float elapsed = Mathf.Max(1e-4f, Time.time - s0);
        result((time.Time01 - t0) / elapsed);
        time.SetTimeForVerification(0.5f);
    }

    private static ScreenSpaceCharacterLabel ReceptionistLabel()
    {
        foreach (EnemyFighter fighter in UnityEngine.Object.FindObjectsByType<EnemyFighter>(FindObjectsSortMode.None))
        {
            if (fighter != null && fighter.Identity == BodybuilderIdentity.Manwithsuit1)
                return fighter.GetComponentInChildren<ScreenSpaceCharacterLabel>();
        }
        return null;
    }

    private static void Teleport(PlayerMovement player, Vector3 position)
    {
        if (player.IsSeatedOnBench) player.StandUpFromBench();
        position.y = GymParkAccess.FloorY + 1.2f;
        CharacterController controller = player.GetComponent<CharacterController>();
        if (controller != null) controller.enabled = false;
        player.transform.position = position;
        if (controller != null) controller.enabled = true;
        Physics.SyncTransforms();
    }

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
            yield return 0.1f;
        }
    }

    private static void Cleanup()
    {
        GymBuffs.Clear();
        GymTimeOfDay.ClockScale = 1f;
        string directory = SessionState.GetString(SessionDirectoryKey, string.Empty);
        Environment.SetEnvironmentVariable(GymSessionService.VerificationDirectoryVariable, null);
        if (!string.IsNullOrEmpty(directory))
        {
            SessionState.EraseString(SessionDirectoryKey);
            try { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
            catch (IOException) { }
        }
        GymChaosVerifierSave.Restore();
    }

    private static void Finish(int code)
    {
        SessionState.EraseBool(RequestedKey);
        EditorApplication.update -= Tick;
        Cleanup();
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
