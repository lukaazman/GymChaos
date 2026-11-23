#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Remade vehicles (Tools/build_vehicle_*.py). Spawns every visitor vehicle
/// as a race visual plus the race police car, then checks per style:
/// Remake (GYMCHAOS_VISUAL_STYLE=remake): the remake GLB is loaded, four
/// rolling wheels, a ready cabin with transparent glass and a driver door
/// that swings 78 degrees, the triangle budget, lamps at night with a
/// cookie-shaped road beam, brake lamps when the car slows, reverse lamps,
/// police strobes and a small body roll in a turn.
/// DEFAULT: every vehicle still loads its DEFAULT asset and nothing remake is added.
/// Logs GYMCHAOS_VEHICLE_REMAKE_OK style=remake|og.
/// </summary>
[InitializeOnLoad]
public static class GymChaosVehicleRemakeVerifier
{
    private const string RequestedKey = "GymChaos.VehicleRemakeVerificationRequested";
    private const int MaxTriangles = 8000;
    private const double LoadTimeout = 40d;
    private static readonly BodybuilderIdentity[] Visitors =
    {
        BodybuilderIdentity.Arnold, BodybuilderIdentity.Cbum, BodybuilderIdentity.JayCutler,
        BodybuilderIdentity.Zyzz, BodybuilderIdentity.Goku, BodybuilderIdentity.Davie
    };

    private enum Stage { Boot, Loading, Static, Night, Drive, Brake, Reverse, Turn, Strobe, Done }

    private static readonly List<GymVisitorVehicle> vehicles = new List<GymVisitorVehicle>();
    private static readonly List<string> failures = new List<string>();
    private static GymRaceVehicleFactory.Car police;
    private static Stage stage;
    private static double stageAt;
    private static double startedAt;
    private static bool remake;
    private static bool finished;
    private static int resultCode = 1;
    private static GymVisitorVehicle probe;
    private static GymVehicleLights probeLights;
    private static bool sawRed, sawBlue, sawRoll;
    private static float maxRoll;

    static GymChaosVehicleRemakeVerifier()
    {
        if (!SessionState.GetBool(RequestedKey, false)) return;
        Hook();
    }

    [MenuItem("Tools/GymChaos/Run Vehicle Remake Verification")]
    public static void Run()
    {
        finished = false;
        resultCode = 1;
        GymChaosVerifierExit.Record(resultCode);
        GymChaosVerifierSave.Neutralize();
        SessionState.SetBool(RequestedKey, true);
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        Hook();
        EditorApplication.isPlaying = true;
    }

    private static void Hook() => GymChaosVerifierHooks.Hook(Tick, OnPlayModeStateChanged);

    private static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            stage = Stage.Boot;
            startedAt = Time.timeAsDouble;
            vehicles.Clear();
            failures.Clear();
            police = null;
            driver = null;
            sawRed = sawBlue = sawRoll = false;
            maxRoll = 0f;
            return;
        }
        if (state != PlayModeStateChange.EnteredEditMode) return;
        EditorApplication.update -= Tick;
        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        SessionState.EraseBool(RequestedKey);
        GymChaosVerifierSave.Restore();
        if (Application.isBatchMode) GymChaosVerifierExit.Exit(resultCode);
    }

    private static void Tick()
    {
        if (finished || !EditorApplication.isPlaying) return;
        double now = Time.timeAsDouble;
        try
        {
            if (now - startedAt > 180d) throw new TimeoutException("whole run");
            switch (stage)
            {
                case Stage.Boot:
                    if (!GymOutdoorBuilder.IsBuilt || GymTimeOfDay.Instance == null || now - startedAt < 3d) return;
                    remake = GymVersionedLooks.AllSlop;
                    Spawn();
                    Next(Stage.Loading, now);
                    break;
                case Stage.Loading:
                    if (vehicles.TrueForAll(v => v.RaceVisualReady) && police.Ready && now - stageAt > 1d)
                    {
                        Next(Stage.Static, now);
                    }
                    else if (now - stageAt > LoadTimeout)
                    {
                        throw new TimeoutException("vehicle load");
                    }
                    break;
                case Stage.Static:
                    CheckStatic();
                    if (!remake)
                    {
                        Finish();
                        return;
                    }
                    SetNight();
                    Next(Stage.Night, now);
                    break;
                case Stage.Night:
                    if (now - stageAt < 0.5d) return;
                    CheckNight();
                    probe = vehicles.Find(v => v.Identity == BodybuilderIdentity.Cbum);
                    probeLights = probe.GetComponent<GymVehicleLights>();
                    Next(Stage.Drive, now);
                    break;
                case Stage.Drive:
                    // Forward at a steady 8 m/s, then a hard stop.
                    Drive(8f);
                    if (now - stageAt > 1.5d)
                    {
                        Expect(!probeLights.BrakeLit, "brake lamps lit while cruising");
                        Drive(0f);
                        Next(Stage.Brake, now);
                    }
                    break;
                case Stage.Brake:
                    if (probeLights.BrakeLit)
                    {
                        Next(Stage.Reverse, now);
                    }
                    else if (now - stageAt > 0.5d)
                    {
                        throw new Exception("brake lamps stayed off after a hard stop");
                    }
                    break;
                case Stage.Reverse:
                    Drive(-2f);
                    if (probeLights.ReverseLit)
                    {
                        Drive(8f, -70f);
                        Next(Stage.Turn, now);
                    }
                    else if (now - stageAt > 1.5d)
                    {
                        throw new Exception("reverse lamps stayed off while backing up");
                    }
                    break;
                case Stage.Turn:
                    // A tight left turn at 8 m/s: the body leans out of it.
                    GymVehicleBodyRoll roll = probe.GetComponentInChildren<GymVehicleBodyRoll>();
                    if (roll == null) throw new Exception("no body roll on the remade car");
                    maxRoll = Mathf.Max(maxRoll, Mathf.Abs(roll.RollDegrees));
                    sawRoll |= maxRoll > 0.3f;
                    if (now - stageAt > 1.2d)
                    {
                        Expect(sawRoll && maxRoll <= 3f, $"body roll {maxRoll:F2} deg not in 0.3-3");
                        Drive(0f);
                        Next(Stage.Strobe, now);
                    }
                    break;
                case Stage.Strobe:
                    GymVehicleLights strobe = police.Holder.GetComponent<GymVehicleLights>();
                    sawRed |= strobe.StrobeRedLit;
                    sawBlue |= strobe.StrobeBlueLit;
                    Expect(!(strobe.StrobeRedLit && strobe.StrobeBlueLit), "red and blue strobes lit together");
                    if (now - stageAt > 1.2d)
                    {
                        Expect(strobe.StrobeLampCount >= 2 && sawRed && sawBlue,
                            $"strobes lamps={strobe.StrobeLampCount} red={sawRed} blue={sawBlue}");
                        Finish();
                    }
                    break;
            }
        }
        catch (Exception exception)
        {
            failures.Add(exception.Message);
            Finish();
        }
    }

    private static void Next(Stage next, double now)
    {
        stage = next;
        stageAt = now;
    }

    /// <summary>Drives the probe car every game frame (the editor update ticks irregularly).</summary>
    private sealed class Driver : MonoBehaviour
    {
        public float Speed;
        public float YawRate;

        private void Update()
        {
            transform.Rotate(0f, YawRate * Time.deltaTime, 0f, Space.World);
            transform.position += transform.forward * (Speed * Time.deltaTime);
        }
    }

    private static Driver driver;

    private static void Drive(float speed, float yawRate = 0f)
    {
        if (driver == null) driver = probe.gameObject.AddComponent<Driver>();
        driver.Speed = speed;
        driver.YawRate = yawRate;
    }

    private static void Spawn()
    {
        for (int i = 0; i < Visitors.Length; i++)
        {
            Vector3 position = new Vector3(400f + i * 16f, 40f, 400f);
            GymVisitorVehicle vehicle = GymVisitorVehicle.CreateRaceVisual(Visitors[i], position, Quaternion.identity);
            if (!vehicle.IsCloud) GymVehicleLights.Attach(vehicle.transform, Visitors[i].ToString());
            vehicles.Add(vehicle);
        }
        police = GymRaceVehicleFactory.CreatePoliceCar(new Pose(new Vector3(400f + Visitors.Length * 16f, 40f, 400f), Quaternion.identity));
    }

    private static void CheckStatic()
    {
        foreach (GymVisitorVehicle vehicle in vehicles)
        {
            string who = vehicle.Identity.ToString();
            string expected = remake ? GymVisualStyle.RemakeVehicleAsset(vehicle.Identity) : null;
            if (remake)
            {
                Expect(vehicle.VisualAssetForVerification == expected, $"{who} loads {vehicle.VisualAssetForVerification}");
                int tris = Triangles(vehicle.gameObject);
                Expect(tris > 0 && tris <= MaxTriangles, $"{who} triangles={tris}");
                Expect(vehicle.GetComponentInChildren<GymVehicleBodyRoll>() != null || vehicle.IsCloud, $"{who} has no body roll");
            }
            else
            {
                Expect(!vehicle.VisualAssetForVerification.Contains(GymVisualStyle.RemakeVehicleFolder),
                    $"{who} DEFAULT style loads {vehicle.VisualAssetForVerification}");
                Expect(vehicle.GetComponentInChildren<GymVehicleBodyRoll>() == null, $"{who} DEFAULT car has body roll");
            }
            if (vehicle.IsCloud || !remake) continue;
            GymVehicleWheelSpinner wheels = vehicle.GetComponent<GymVehicleWheelSpinner>();
            Expect(wheels != null && wheels.WheelCount == 4, $"{who} wheels={(wheels != null ? wheels.WheelCount : -1)}");
            if (vehicle.IsBus) continue;
            GymVehicleCabin cabin = vehicle.GetComponent<GymVehicleCabin>();
            Expect(cabin != null && cabin.IsReady, $"{who} cabin not ready");
            if (cabin == null || !cabin.IsReady) continue;
            Expect(cabin.GlassRendererCount >= 2 && GlassIsTransparent(vehicle.gameObject), $"{who} glass={cabin.GlassRendererCount} not transparent");
            Expect(cabin.TryGetMarker("Seat", out _) && cabin.TryGetMarker("Roof", out _) && cabin.TryGetMarker("Floor", out _),
                $"{who} markers missing");
            cabin.SetDoorOpen01(1f);
            float open = cabin.DoorAngleDegrees;
            cabin.SetDoorOpen01(0f);
            Expect(Mathf.Abs(open - GymVehicleCabin.DoorOpenDegrees) < 2f && cabin.DoorAngleDegrees < 0.5f,
                $"{who} door swings {open:F1} deg");
        }
        string policeAsset = GymVisualStyle.VehicleAsset(BodybuilderIdentity.Policeman, "og");
        Expect(remake ? policeAsset.Contains(GymVisualStyle.RemakeVehicleFolder) : policeAsset == "og", $"police asset {policeAsset}");
        Expect(police.Model != null && !police.Failed, "police car failed to load");
        if (police.Model != null)
        {
            bool strobeLens = HasMaterial(police.Model, "Lamp_StrobeRed");
            Expect(strobeLens == remake, $"police strobe lens present={strobeLens}");
            Expect(!remake || police.Seat != null, "police cabin not ready");
            Expect(!remake || police.Model.GetComponentInChildren<GymVehicleBodyRoll>(true) != null, "police car has no body roll");
            if (remake) Expect(Triangles(police.Model) <= MaxTriangles, $"police triangles={Triangles(police.Model)}");
        }
    }

    private static void SetNight()
    {
        GymTimeOfDay clock = GymTimeOfDay.Instance;
        float best = 0f, darkest = float.MaxValue;
        for (float t = 0f; t < 1f; t += 0.05f)
        {
            clock.SetTimeForVerification(t);
            if (clock.Daylight01 < darkest)
            {
                darkest = clock.Daylight01;
                best = t;
            }
        }
        clock.SetTimeForVerification(best);
    }

    private static void CheckNight()
    {
        int beamCars = 0;
        foreach (GymVisitorVehicle vehicle in vehicles)
        {
            if (vehicle.IsCloud) continue;
            GymVehicleLights lights = vehicle.GetComponent<GymVehicleLights>();
            lights.RefreshNow();
            Expect(lights.IsLit && lights.LampPieceCount >= 2, $"{vehicle.Identity} lamps lit={lights.IsLit} pieces={lights.LampPieceCount}");
            Expect(lights.BeamCookie != null, $"{vehicle.Identity} beam has no cookie");
            Expect(lights.BrakeLampCount >= 1, $"{vehicle.Identity} brake lamps={lights.BrakeLampCount}");
            if (lights.BeamsOn > 0) beamCars++;
        }
        Expect(beamCars <= 4, $"road beams on {beamCars} cars (budget 4)");
    }

    private static void Expect(bool condition, string failure)
    {
        if (!condition && !failures.Contains(failure)) failures.Add(failure);
    }

    private static int Triangles(GameObject root)
    {
        int total = 0;
        foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>(true))
        {
            if (filter.sharedMesh == null || filter.name.StartsWith("Vehicle Lamp", StringComparison.Ordinal)) continue;
            // Shadow proxies are copies of the body; count the visible model only.
            MeshRenderer renderer = filter.GetComponent<MeshRenderer>();
            if (renderer != null && renderer.shadowCastingMode == UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly) continue;
            total += filter.sharedMesh.triangles.Length / 3;
        }
        return total;
    }

    private static bool GlassIsTransparent(GameObject root)
    {
        foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            string part = GymVehicleCabin.PartName(renderer.name);
            if (part.EndsWith("__Glass", StringComparison.Ordinal) &&
                (renderer.sharedMaterial == null || renderer.sharedMaterial.renderQueue < 2500)) return false;
        }
        return true;
    }

    private static bool HasMaterial(GameObject root, string prefix)
    {
        foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            foreach (Material material in renderer.sharedMaterials)
            {
                if (material != null && material.name.StartsWith(prefix, StringComparison.Ordinal)) return true;
            }
        }
        return false;
    }

    private static void Finish()
    {
        if (finished) return;
        finished = true;
        string style = remake ? "remake" : "og";
        if (failures.Count == 0)
        {
            resultCode = 0;
            Debug.Log($"GYMCHAOS_VEHICLE_REMAKE_OK style={style} vehicles={vehicles.Count + 1} maxRoll={maxRoll:F2}");
        }
        else
        {
            Debug.LogError($"GYMCHAOS_VEHICLE_REMAKE_FAILED style={style} " + string.Join("; ", failures));
        }
        GymChaosVerifierExit.Record(resultCode);
        EditorApplication.isPlaying = false;
    }
}
#endif
