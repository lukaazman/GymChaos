#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// End-to-end race gate: the RACE sign offers F, the race builds the track and
/// carves the city, six cars line up in three rows of two with seated drivers,
/// the countdown runs, the (autopiloted) player completes three laps, places
/// are ranked, the results award the place XP, and teardown restores the city,
/// the walls, the player and visitor traffic. Logs GYMCHAOS_RACE_FLOW_OK.
/// </summary>
public static class GymChaosRaceFlowVerifier
{
    private const string RequestedKey = "GymChaos.RaceFlowVerificationRequested";
    private static IEnumerator routine;
    private static readonly Stack<IEnumerator> stack = new Stack<IEnumerator>();
    private static readonly StringBuilder report = new StringBuilder();

    [InitializeOnLoadMethod]
    private static void ResumeAfterReload()
    {
        if (!SessionState.GetBool(RequestedKey, false)) return;
        Hook();
    }

    [MenuItem("Tools/GymChaos/Run Race Flow Verification")]
    public static void Run()
    {
        GymChaosVerifierSave.Neutralize();
        SessionState.SetBool(RequestedKey, true);
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        Hook();
        EditorApplication.isPlaying = true;
    }

    private static void Hook()
    {
        routine = null;
        report.Length = 0;
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
        EditorApplication.playModeStateChanged -= Changed;
        EditorApplication.playModeStateChanged += Changed;
    }

    private static void Changed(PlayModeStateChange change)
    {
        if (change == PlayModeStateChange.EnteredPlayMode)
        {
            GymChaosVerifierClock.BeginFixedStep();
            stack.Clear();
            routine = Scenario();
        }
        if (change != PlayModeStateChange.EnteredEditMode) return;
        EditorApplication.update -= Tick;
        GymChaosVerifierClock.EndFixedStep();
        GymChaosVerifierSave.Restore();
        if (Application.isBatchMode && SessionState.GetBool(RequestedKey, false)) EditorApplication.Exit(1);
    }

    private sealed class VerificationFailure : System.Exception
    {
        public VerificationFailure(string message) : base(message) { }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new VerificationFailure(message);
    }

    private static void Tick()
    {
        if (!EditorApplication.isPlaying || routine == null) return;
        try
        {
            if (stack.Count == 0) stack.Push(routine);
            while (stack.Count > 0)
            {
                IEnumerator top = stack.Peek();
                if (!top.MoveNext())
                {
                    stack.Pop();
                    continue;
                }
                if (top.Current is IEnumerator nested)
                {
                    stack.Push(nested);
                    continue;
                }
                return;
            }
            Finish(0, report.ToString());
        }
        catch (VerificationFailure failure)
        {
            Finish(1, failure.Message + " | " + report);
        }
        catch (System.Exception exception)
        {
            Finish(1, "exception " + exception + " | " + report);
        }
    }

    private static IEnumerator Scenario()
    {
        float started = Time.realtimeSinceStartup;
        PlayerMovement player = null;
        while (player == null || !GymRaceCityCarver.CityReady() || GymRaceSign.Instance == null ||
               GymRaceDirector.Instance == null)
        {
            if (Time.realtimeSinceStartup - started > 160f)
                throw new VerificationFailure($"scene not ready player={player != null} city={GymRaceCityCarver.CityReady()} sign={GymRaceSign.Instance != null} director={GymRaceDirector.Instance != null}");
            player = Object.FindFirstObjectByType<PlayerMovement>();
            yield return null;
        }
        float settle = Time.time + 2f;
        while (Time.time < settle) yield return null;

        // ------------------------------------------------------------- the sign
        GymRaceSign sign = GymRaceSign.Instance;
        Bounds parking = GymOutdoorBuilder.ParkingBounds;
        Require(sign.transform.position.x > parking.center.x && sign.transform.position.z > parking.center.z,
            $"sign not in the north-east of the lot ({sign.transform.position})");
        CharacterController controller = player.GetComponent<CharacterController>();
        if (controller != null) controller.enabled = false;
        player.transform.position = sign.StandPoint + Vector3.up * 0.1f;
        if (controller != null) controller.enabled = true;
        yield return null;
        PlayerMovement.ContextAction fAction = player.ResolveFActionForVerification();
        Require(fAction == PlayerMovement.ContextAction.Race, $"F next to the sign resolves to {fAction}");
        report.Append($"sign={sign.transform.position} ");
        float signWait = Time.time + 5f;
        Vector3 titleExtent;
        Vector3 subExtent;
        while (!sign.TryGetLabelExtents(out titleExtent, out subExtent))
        {
            Require(Time.time < signWait, "sign labels never fitted");
            yield return null;
        }
        Require(titleExtent.y > subExtent.z + 0.03f,
            $"sign title (bottom {titleExtent.y:F2}) overlaps the subtitle (top {subExtent.z:F2})");
        Require(titleExtent.x <= 2.1f && subExtent.x <= 2.1f,
            $"sign text wider than the face (title {titleExtent.x:F2} sub {subExtent.x:F2})");
        Require(titleExtent.z < 0.405f && subExtent.y > -0.405f, "sign text runs into the checkered strips");
        report.Append($"signGap={titleExtent.y - subExtent.z:F2} ");

        GymExperienceService progression = GymExperienceService.Active;
        Require(progression != null && progression.State != null, "no progression");
        int totalBefore = progression.State.totalExperience;
        int startsBefore = progression.State.raceStarts;
        long cityBefore = GymRaceCityCarver.CountCityTriangles();

        GymRaceDirector director = GymRaceDirector.Instance;
        // Visitor cars already on the road block the start until they park or leave.
        float trafficLimit = Time.time + 90f;
        while (director.BlockReasonForVerification != null)
        {
            Require(Time.time < trafficLimit, "race stayed blocked: " + director.BlockReasonForVerification);
            yield return null;
        }
        Require(director.StartRaceForVerification(player, true), "race refused to start");
        director.AutoRaceAgainOnce = true;
        Time.timeScale = GymChaosVerifierClock.ScenarioTimeScale;

        // ------------------------------------------------------ grid and drivers
        float limit = Time.time + 60f;
        bool sawBoarding = false;
        while (director.CurrentPhase != GymRaceDirector.Phase.Countdown)
        {
            Require(Time.time < limit, $"race never reached the countdown (phase {director.CurrentPhase})");
            Require(director.CurrentPhase != GymRaceDirector.Phase.Idle, "race returned to idle before the countdown");
            sawBoarding |= director.CurrentPhase == GymRaceDirector.Phase.Boarding;
            if (director.PlayerRacer != null && director.CurrentPhase == GymRaceDirector.Phase.Building)
            {
                // Seated from the moment the karts exist: no walk to the car.
                Transform car = director.Cars[director.PlayerRacer.GridSlot].Holder;
                Require(Vector3.Distance(player.transform.position, car.position) < 2.5f,
                    $"player {Vector3.Distance(player.transform.position, car.position):F1} m from the buggy before the countdown");
            }
            yield return null;
        }
        Require(!sawBoarding, "race played a boarding phase");
        Transform playerCar = director.Cars[director.PlayerRacer.GridSlot].Holder;
        Require(Vector3.Distance(player.transform.position, playerCar.position) < 2.5f, "player not in the buggy at the countdown");
        Require(director.Hud.StandingsUncoloured, "standings rows still use racer colours");
        report.Append($"heads={director.Hud.StandingHeadsShown} ");
        Require(GymRaceCityCarver.IsCarved && GymRaceCityCarver.RemovedBuildingCount > 0, "city not carved");
        Require(GymRaceCityCarver.CountTrianglesOnRoad(0f) == 0, "towers remain on the road");
        Require(GameObject.Find(GymRaceTrackBuilder.RootName) != null, "no track");
        Require(GymVisitorDirector.RaceTrafficHold, "visitor traffic not held");
        Require(director.Racers.Count == 6, $"{director.Racers.Count} racers");
        HashSet<string> rows = new HashSet<string>();
        int seated = 0;
        for (int i = 0; i < director.Racers.Count; i++)
        {
            GymRaceRacer racer = director.Racers[i];
            Pose slot = GymRaceTrackLayout.GridSlot(racer.GridSlot);
            float offset = Vector3.Distance(Flat(racer.Kart.Body.position), Flat(slot.position));
            Require(offset < 1.2f, $"{racer.DisplayName} {offset:F2} m off its grid slot");
            rows.Add($"{Mathf.RoundToInt(slot.position.z)}");
            if (!racer.IsPlayer)
            {
                EnemyFighter driver = director.Drivers[racer.GridSlot];
                bool inPose = driver != null && driver.IsSeatedInVehicle &&
                    ((IVehicleSeatOccupant)driver).CurrentVehiclePose == MixamoScanRetargetAnimator.VehiclePose.Driving;
                if (inPose) seated++;
                else report.Append($"unseated={racer.DisplayName}:found={driver != null},seated={driver != null && driver.IsSeatedInVehicle},pose={(driver != null ? ((IVehicleSeatOccupant)driver).CurrentVehiclePose.ToString() : "-")} ");
            }
        }
        Require(rows.Count == 3, $"grid rows {rows.Count}");
        Require(seated == 5, $"only {seated}/5 drivers seated in the driving pose");
        report.Append($"seated={seated} playerSeated={(director.PlayerSeatedForVerification ? 1 : 0)} ");
        Require(director.ChaseCamera != null && director.ChaseCamera.IsActive, "chase camera inactive");
        Require((director.ChaseCamera.Target.cullingMask & (1 << PlanarGymMirror.MirrorPlayerLayer)) != 0 || !director.PlayerSeatedForVerification,
            "third-person camera hides the seated player body");

        // ---------------------------------------------------------------- pause
        while (director.CurrentPhase != GymRaceDirector.Phase.Racing) yield return null;
        float drive = Time.time + 8f;
        while (Time.time < drive) yield return null;
        // The batch start screen stays up (GymPauseMenu.Open refuses then): show the page directly.
        GymPauseMenu.CreateForScene(player);
        object pauseInstance = typeof(GymPauseMenu).GetField("instance", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)?.GetValue(null);
        typeof(GymPauseMenu).GetMethod("ShowPausePage", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.Invoke(pauseInstance, null);
        yield return null;
        Require(GymPauseMenu.IsVisible, "pause menu did not open during the race");
        Vector3 pausedAt = director.PlayerRacer.Kart.Body.position;
        double pauseUntil = EditorApplication.timeSinceStartup + 0.6d;
        while (EditorApplication.timeSinceStartup < pauseUntil) yield return null;
        float drift = Vector3.Distance(pausedAt, director.PlayerRacer.Kart.Body.position);
        Require(drift < 0.05f && GymRaceDirector.IsRaceActive, $"kart moved {drift:F2} m while paused");
        typeof(GymPauseMenu).GetMethod("Resume", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.Invoke(pauseInstance, null);
        yield return null;
        Require(!GymPauseMenu.IsVisible, "pause menu did not close");
        Time.timeScale = GymChaosVerifierClock.ScenarioTimeScale;
        report.Append("pause=1 ");

        // ----------------------------------------------------------------- race
        limit = Time.time + 420f;
        int maxCrossings = 0;
        while (director.CurrentPhase == GymRaceDirector.Phase.Countdown ||
               director.CurrentPhase == GymRaceDirector.Phase.Racing ||
               director.CurrentPhase == GymRaceDirector.Phase.Finishing)
        {
            Require(Time.time < limit, $"race did not finish (phase {director.CurrentPhase} laps {director.PlayerRacer?.Crossings})");
            if (director.PlayerRacer != null) maxCrossings = Mathf.Max(maxCrossings, director.PlayerRacer.Crossings);
            yield return null;
        }
        // Results: the racers stay alive while the choice popup is up.
        Require(director.CurrentPhase == GymRaceDirector.Phase.Results, $"race ended in {director.CurrentPhase}");
        float popupWait = Time.time + 1.5f;
        while (director.Hud != null && !director.Hud.ChoiceVisible && Time.time < popupWait) yield return null;
        Require(director.Hud != null && director.Hud.ChoiceVisible, "no back/race-again popup after the race");
        Require(director.Hud.ResultsVisible, "results board hidden under the popup");
        GymRaceRacer player0 = director.PlayerRacer;
        Require(player0 != null && player0.Finished, "player did not finish");
        Require(player0.LapTimes.Count == GymRaceTrackLayout.Laps, $"player lap times {player0.LapTimes.Count}");
        HashSet<int> places = new HashSet<int>();
        foreach (GymRaceRacer racer in director.Racers) places.Add(racer.FinishPlace);
        Require(places.Count == 6 && places.Contains(1) && places.Contains(6), $"places not 1..6 ({string.Join(",", places)})");
        int place = director.LastPlayerPlace;
        int firstAward = director.LastAwardedExperience;
        int expected = GymRaceDirector.PlaceExperience[Mathf.Clamp(place, 1, 6) - 1];
        Require(firstAward >= expected, $"awarded {firstAward} < place XP {expected}");
        Require(progression.State.raceStarts == startsBefore + 1, "first race not recorded at the results");
        report.Append($"place={place} laps={string.Join("/", player0.LapTimes.ConvertAll(t => t.ToString("F1")))} total={player0.FinishTime:F1} ");

        // -------------------------------------------------------------- rematch
        limit = Time.time + 30f;
        while (director.CurrentPhase != GymRaceDirector.Phase.Countdown)
        {
            Require(Time.time < limit, $"race again never reached the countdown (phase {director.CurrentPhase})");
            Require(director.CurrentPhase != GymRaceDirector.Phase.Idle, "race again tore the race down");
            yield return null;
        }
        Require(!director.Hud.ChoiceVisible && !director.Hud.ResultsVisible, "popup still shown in the rematch");
        Require(GymRaceCityCarver.IsCarved && GameObject.Find(GymRaceTrackBuilder.RootName) != null, "rematch lost the track or carve");
        for (int i = 0; i < director.Racers.Count; i++)
        {
            GymRaceRacer racer = director.Racers[i];
            Pose slot = GymRaceTrackLayout.GridSlot(racer.GridSlot);
            float offset = Vector3.Distance(Flat(racer.Kart.Body.position), Flat(slot.position));
            Require(offset < 1.2f, $"rematch: {racer.DisplayName} {offset:F2} m off its grid slot");
            Require(!racer.Finished && racer.Crossings == 0 && racer.LapTimes.Count == 0, $"rematch: {racer.DisplayName} kept its race state");
        }
        Require(Vector3.Distance(player.transform.position, playerCar.position) < 2.5f, "player left the buggy for the rematch");
        while (director.CurrentPhase != GymRaceDirector.Phase.Racing) yield return null;
        float rematchDrive = Time.time + 3f;
        while (Time.time < rematchDrive) yield return null;
        Require(director.PlayerRacer.Kart.Speed > 2f, "rematch kart did not drive off");
        director.ForceResultsForVerification();
        report.Append("rematch=1 ");

        // ------------------------------------------------------------- teardown
        limit = Time.time + 60f;
        while (director.CurrentPhase != GymRaceDirector.Phase.Idle)
        {
            Require(Time.time < limit, $"teardown stuck in {director.CurrentPhase}");
            yield return null;
        }
        Time.timeScale = 1f;
        yield return null;
        int awarded = director.LastAwardedExperience;
        int gained = progression.State.totalExperience - totalBefore;
        Require(gained >= firstAward + awarded, $"total experience grew {gained} < {firstAward}+{awarded}");
        Require(progression.State.raceStarts == startsBefore + 2, "rematch start not recorded");
        Require(director.RacesRunForVerification >= 2, "rematch never reached its results");
        Require(progression.State.raceBestLapMs > 0, "best lap not stored");
        Require(GymRaceCityCarver.CountCityTriangles() == cityBefore, "city not restored");
        Require(GameObject.Find(GymRaceTrackBuilder.RootName) == null, "track still present");
        Require(GymRaceCorridorClearance.DisabledColliderCount == 0, "walls not restored");
        Require(!GymVisitorDirector.RaceTrafficHold, "visitor traffic still held");
        Require(!GymRaceDirector.IsRaceActive, "race still active");
        Require(Vector3.Distance(Flat(player.transform.position), Flat(sign.StandPoint)) < 1.5f, $"player not back at the sign ({player.transform.position})");
        Require(controller == null || controller.enabled, "character controller still disabled");
        Require(player.transform.parent == null || !player.transform.parent.name.Contains("Race"), "player still parented to a race object");
        Require(GymRaceKart.All.Count == 0, $"{GymRaceKart.All.Count} karts left");
        report.Append($"xp={firstAward}+{awarded} gained={gained}");
    }

    private static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

    private static void Finish(int code, string detail)
    {
        if (code == 0) Debug.Log("GYMCHAOS_RACE_FLOW_OK " + detail);
        else Debug.LogError("GYMCHAOS_RACE_FLOW_FAILED " + detail);
        SessionState.EraseBool(RequestedKey);
        EditorApplication.update -= Tick;
        routine = null;
        Time.timeScale = 1f;
        GymRaceCityCarver.Restore();
        GymRaceCorridorClearance.Restore();
        GymChaosVerifierClock.EndFixedStep();
        GymChaosVerifierSave.Restore();
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
