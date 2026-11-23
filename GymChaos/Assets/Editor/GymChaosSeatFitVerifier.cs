#if UNITY_EDITOR
using System;
using System.Collections;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Play-mode seat fit gate: starts a real session the way
/// <see cref="GymChaosClassSessionPlay"/> does, then sits the player in the race
/// buggy at 160 cm and at 200 cm through the same seat a visitor uses, and
/// checks that the body still fits under the roof, keeps the seat's own scale
/// budget, and that the height scale composes with the seat scale instead of
/// replacing it. Logs GYMCHAOS_SEAT_FIT_OK.
/// </summary>
[InitializeOnLoad]
public static class GymChaosSeatFitVerifier
{
    private const string Key = "GymChaos.SeatFitVerificationRequested";
    private static bool launched;

    static GymChaosSeatFitVerifier() => GymChaosVerifierHooks.Hook(Tick, OnPlayModeChanged);

    [MenuItem("Tools/GymChaos/Run Seat Fit Verification")]
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
        if (state == PlayModeStateChange.EnteredEditMode) launched = false;
    }

    private static void Tick()
    {
        if (!SessionState.GetBool(Key, false) || !EditorApplication.isPlaying || launched) return;
        launched = true;
        new GameObject("Seat fit verifier").AddComponent<GymChaosSeatFitVerifierRunner>();
    }

    public static void Finish(int code)
    {
        SessionState.EraseBool(Key);
        GymVersionedLooks.ApplyHostVersions(null, null);
        GymChaosVerificationSession.End();
        GymChaosVerifierExit.Record(code);
        if (Application.isBatchMode)
        {
            GymChaosVerifierExit.Exit(code);
            return;
        }
        EditorApplication.isPlaying = false;
    }
}

public sealed class GymChaosSeatFitVerifierRunner : MonoBehaviour
{
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
                Debug.LogError("GYMCHAOS_SEAT_FIT_FAILED " + exception);
                failed = true;
                break;
            }
            yield return step;
        }
        GymChaosSeatFitVerifier.Finish(failed ? 1 : 0);
    }

    private IEnumerator Scenario()
    {
        var report = new StringBuilder();
        foreach (float wait in GymChaosVerificationSession.WaitForBootMenu()) yield return wait;
        foreach (float wait in GymChaosVerificationSession.StartNewCharacter(
            "bodybuilding", 160, "Seat Fit")) yield return wait;

        PlayerMovement player = UnityEngine.Object.FindFirstObjectByType<PlayerMovement>();
        Require(player != null, "no player after the session started");
        float shorterCrown = 0f, tallerCrown = 0f, shorterNorm = 0f, tallerNorm = 0f;
        float shorterVisible = 0f, tallerVisible = 0f;

        foreach (int height in new[] { 160, 200 })
        {
            GymCharacterSave save = GymSessionService.Active;
            Require(save != null, "no active character");
            save.heightCm = height;
            GymSessionService.ApplyToWorld(player);
            yield return 0.5f;

            PlayerHandRig rig = player.GetComponentInChildren<PlayerHandRig>();
            Require(rig != null, "the player has no body rig");
            // The body is fitted to a standard figure, so its world height is not
            // centimetres: the height scale is the absolute truth, the rendered
            // height is checked as a ratio between the two heights.
            Require(Mathf.Abs(rig.HeightScaleForVerification - height / 180f) < 0.01f,
                "the " + height + " cm body carries scale " + rig.HeightScaleForVerification.ToString("F3"));
            // Standing, before any car pose bends him: the height itself.
            float standing = rig.RuntimeVisibleHeight;

            GymPlayerRaceCar built = null;
            Vector3 spot = player.transform.position + player.transform.forward * 4f;
            GymPlayerRaceCar.Spawn(spot,
                Quaternion.Euler(0f, player.transform.eulerAngles.y + 90f, 0f), null,
                car => built = car);
            double deadline = EditorApplication.timeSinceStartup + 40d;
            while (built == null || !built.IsReady)
            {
                if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException("the player buggy");
                yield return 0f;
            }

            PlayerVehicleOccupant occupant = PlayerVehicleOccupant.Attach(player);
            GymVehicleDriverSeat seat = built.Seat;
            Require(occupant != null && seat != null, "the buggy has no seat");
            Require(GymVehicleDriverSeat.SupportsDriver(occupant), "the player cannot use the car seat");
            Require(seat.Mount(occupant), "the player could not be seated");
            // A skinned mesh keeps its culling bounds until it is drawn; without a camera pass
            // they lag the pose by up to 7 cm. Recompute them from the live pose every frame.
            foreach (SkinnedMeshRenderer skinned in rig.RuntimeBodyRenderers)
                if (skinned != null) skinned.updateWhenOffscreen = true;
            yield return 0.5f;
            Require(seat.State == GymVehicleDriverSeat.SeatState.Seated, "the player is not seated");

            Require(seat.FitValidForVerification, "the seat fit is invalid at " + height + " cm");
            float clearance = seat.HeadClearance;
            float sink = seat.SeatSink;
            float inCar = seat.InCarScaleForVerification;
            float crown = seat.CrownAboveCushionForVerification;
            float heightScale = rig.HeightScaleForVerification;
            float scale = rig.RuntimeModelRoot != null ? rig.RuntimeModelRoot.lossyScale.y : 0f;
            float norm = heightScale * inCar > 0.001f ? scale / (heightScale * inCar) : 0f;
            Bounds body = BodyBounds(rig);
            bool hasRoof = seat.TryGetCabinMarkerForVerification("Roof", out Vector3 roof);
            float roofGap = hasRoof ? body.max.y - roof.y : 0f;
            report.Append($"h{height}(clear={clearance:F3} sink={sink:F3} inCar={inCar:F2} " +
                $"crown={crown:F2} scale={scale:F2} norm={norm:F3} roofGap={roofGap:F3}) ");
            Debug.Log($"GYMCHAOS_SEAT_FIT height={height} clearance={clearance:F3} sink={sink:F3} " +
                $"inCar={inCar:F2} crown={crown:F2} scale={scale:F2} norm={norm:F3} roofGap={roofGap:F3}");

            Require(clearance >= -0.02f,
                "the " + height + " cm body pokes through the roof: clearance=" + clearance.ToString("F3"));
            Require(sink <= seat.MaxSinkForVerification + 0.006f,
                "the " + height + " cm body sinks " + sink.ToString("F3") + " m into the seat");
            Require(inCar >= 0.795f && inCar <= 1.0001f,
                "the seat scale at " + height + " cm is " + inCar.ToString("F3"));
            Require(heightScale * inCar > 0.001f, "the seat lost the body scale");
            Require(!hasRoof || roofGap <= 0.05f,
                "the " + height + " cm crown is " + roofGap.ToString("F3") + " m above the roof");

            if (height == 160)
            {
                shorterCrown = crown;
                shorterNorm = norm;
                shorterVisible = standing;
            }
            else
            {
                tallerCrown = crown;
                tallerNorm = norm;
                tallerVisible = standing;
            }

            occupant.EndVehicleSeat(player.transform.position, player.transform.rotation);
            UnityEngine.Object.Destroy(built.gameObject);
            yield return 0.5f;
        }

        Require(tallerCrown > shorterCrown + 0.02f,
            "the 200 cm body sits no taller than the 160 cm one: " + shorterCrown.ToString("F2") +
            " vs " + tallerCrown.ToString("F2"));
        Require(shorterVisible > 0.5f && tallerVisible > shorterVisible &&
            Mathf.Abs(tallerVisible / shorterVisible - 1.25f) <= 0.03f,
            "the rendered body did not grow from 160 to 200 cm: " + shorterVisible.ToString("F2") +
            " -> " + tallerVisible.ToString("F2"));
        Require(Mathf.Abs(tallerNorm - shorterNorm) <= Mathf.Max(0.02f, shorterNorm * 0.05f),
            "the seat scale does not compose with height: norm " + shorterNorm.ToString("F3") +
            " vs " + tallerNorm.ToString("F3"));
        Debug.Log("GYMCHAOS_SEAT_FIT_OK heights=160,200 " + report);
    }

    private static Bounds BodyBounds(PlayerHandRig rig)
    {
        Bounds bounds = new Bounds(rig.transform.position, Vector3.zero);
        bool any = false;
        foreach (SkinnedMeshRenderer renderer in rig.RuntimeBodyRenderers)
        {
            if (renderer == null) continue;
            if (!any)
            {
                bounds = renderer.bounds;
                any = true;
            }
            else
            {
                bounds.Encapsulate(renderer.bounds);
            }
        }
        return bounds;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
#endif
