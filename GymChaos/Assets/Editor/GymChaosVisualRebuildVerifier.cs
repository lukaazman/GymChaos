#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// A version change applies to the running scene: with the gym already built,
/// switching Davie to POTATO (and back) rebuilds his body in place — same actor,
/// same position, new resource — with no scene load. Logs
/// GYMCHAOS_VISUAL_REBUILD_OK.
/// </summary>
public static class GymChaosVisualRebuildVerifier
{
    private const string RequestedKey = "GymChaos.VisualRebuildVerificationRequested";
    private static double started;
    private static ExternalRiggedCharacterVisual davie;
    private static Vector3 daviePosition;
    private static Transform davieTransform;
    private static Transform davieModel;
    private static bool waitedForVehicle;
    private static int stage;
    private static GymVisitorVehicle vehicle;
    private static Vector3 vehiclePosition;
    private static double vehicleAt;
    private static string ogVehicleAsset = string.Empty;
    private static Transform vehicleModel;
    private static float vehicleLength;
    private static int targetIndex;
    private static int policePhase;
    private static double policeAt;
    private static Transform policeModel;
    private static Vector3 policePosition;
    private static string ogPoliceAsset = string.Empty;
    private static int vehiclePhase;

    [InitializeOnLoadMethod]
    private static void ResumeAfterReload()
    {
        if (!SessionState.GetBool(RequestedKey, false)) return;
        Hook();
    }

    public static void Run()
    {
        GymChaosVerifierSave.Neutralize();
        SessionState.SetBool(RequestedKey, true);
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        Hook();
        EditorApplication.isPlaying = true;
    }

    private static void Hook() => GymChaosVerifierHooks.Hook(Tick, Changed);

    private static void Changed(PlayModeStateChange change)
    {
        if (change == PlayModeStateChange.EnteredPlayMode)
        {
            started = Time.timeAsDouble;
            stage = 0;
            davie = null;
        }
        if (change != PlayModeStateChange.EnteredEditMode) return;
        EditorApplication.update -= Tick;
        GymVersionedLooks.ApplyHostVersions(null, null);
        GymChaosVerifierSave.Restore();
        if (Application.isBatchMode && SessionState.GetBool(RequestedKey, false)) EditorApplication.Exit(1);
    }

    private static void Tick()
    {
        if (!EditorApplication.isPlaying) return;
        double elapsed = Time.timeAsDouble - started;
        if (elapsed < 6d) return;
        int code = 1;
        try
        {
            if (davie == null)
            {
                foreach (ExternalRiggedCharacterVisual visual in
                    UnityEngine.Object.FindObjectsByType<ExternalRiggedCharacterVisual>(FindObjectsSortMode.None))
                {
                    if (visual.Identity != BodybuilderIdentity.Davie || visual.RuntimeRenderer == null) continue;
                    davie = visual;
                    break;
                }
                // The roster builds over the first seconds; wait before failing.
                if (davie == null)
                {
                    if (elapsed < 60d) return;
                    throw new InvalidOperationException("Davie never spawned");
                }
                daviePosition = davie.transform.position;
                davieTransform = davie.transform;
                davieModel = davie.RuntimeModelRoot;
                Require(davie.RuntimeResourcePath != null &&
                    !davie.RuntimeResourcePath.StartsWith("Characters/Remake/", StringComparison.Ordinal),
                    "Davie did not start on his DEFAULT body");
                // The host's table is the multiplayer path and needs no session save.
                GymVersionedLooks.ApplyHostVersions(new[] { "davie" }, null);
                stage = 1;
                return;
            }
            if (stage == 1)
            {
                Require(davie != null && davie.RuntimeRenderer != null, "Davie lost his body");
                Require(davie.RuntimeResourcePath != null &&
                    davie.RuntimeResourcePath.StartsWith("Characters/Remake/", StringComparison.Ordinal),
                    "Davie did not switch to his POTATO body in place: " + davie.RuntimeResourcePath);
                // Same actor, a freshly built body: the swap happened in place,
                // not by respawning the visitor somewhere else.
                Require(davie.transform == davieTransform, "the rebuild replaced the actor");
                Require(davie.RuntimeModelRoot != null && davie.RuntimeModelRoot != davieModel,
                    "the body was not rebuilt");
                GymVersionedLooks.ApplyHostVersions(null, null);
                stage = 2;
                return;
            }
            Require(davie != null && davie.RuntimeRenderer != null, "Davie lost his body on the way back");
            Require(davie.RuntimeResourcePath != null &&
                !davie.RuntimeResourcePath.StartsWith("Characters/Remake/", StringComparison.Ordinal),
                "Davie did not switch back to his DEFAULT body in place: " + davie.RuntimeResourcePath);
            // Hand over to the vehicles: car, Davie's bus, Goku's cloud.
            stage = 3;

            // Vehicles: the same swap in place on a live car, on Davie's bus and
            // on Goku's cloud. Each keeps its own body length, route and rider
            // anchor; the remake GLBs load asynchronously, so wait on the state.
            if (stage == 3 && targetIndex >= 3 && policePhase == 0)
            {
                // The police cruiser: its director owns the car, so it is not a
                // visitor vehicle. Build one parked and swap it in place too.
                GymPoliceDirector police = GymPoliceDirector.EnsureForVerification();
                police.PrepareCarForVerification(
                    vehiclePosition + new Vector3(7f, 0f, 0f), Quaternion.identity);
                policeAt = Time.timeAsDouble;
                policePhase = 1;
                return;
            }
            if (stage == 3 && targetIndex >= 3)
            {
                GymPoliceDirector police = GymPoliceDirector.EnsureForVerification();
                string asset = police.PoliceCarAssetForVerification ?? string.Empty;
                GameObject root = police.PoliceCarRootForVerification;
                bool ready = police.PoliceCarReadyForVerification && root != null;
                double waited = Time.timeAsDouble - policeAt;
                if (policePhase == 1)
                {
                    if (!ready)
                    {
                        if (waited < 40d) return;
                        throw new InvalidOperationException("the police car never loaded: " + asset);
                    }
                    policeModel = root.transform;
                    policePosition = root.transform.position;
                    ogPoliceAsset = asset;
                    GymVersionedLooks.ApplyHostVersions(null, GymVersionedLooks.VehicleKeys);
                    policeAt = Time.timeAsDouble;
                    policePhase = 2;
                    return;
                }
                if (policePhase == 2)
                {
                    if (!asset.Contains("/remake/") || !ready || root.transform == policeModel)
                    {
                        if (waited < 40d) return;
                        throw new InvalidOperationException("the police car did not swap to POTATO: " + asset);
                    }
                    // The fresh GLB re-grounds on its own tyres (a few cm on y);
                    // in the stall it must not shift along the floor.
                    Vector3 policeDelta = root.transform.position - policePosition;
                    Require(new Vector2(policeDelta.x, policeDelta.z).magnitude < 0.05f,
                        "the swap moved the police car: " + policeDelta);
                    policeModel = root.transform;
                    GymVersionedLooks.ApplyHostVersions(null, null);
                    policeAt = Time.timeAsDouble;
                    policePhase = 3;
                    return;
                }
                if (asset.Contains("/remake/") || !ready || root.transform == policeModel)
                {
                    if (waited < 40d) return;
                    throw new InvalidOperationException("the police car did not swap back to DEFAULT: " + asset);
                }
                Require(asset == ogPoliceAsset,
                    "the police car came back on a different DEFAULT model: " + asset);
                Vector3 policeBackDelta = root.transform.position - policePosition;
                Require(new Vector2(policeBackDelta.x, policeBackDelta.z).magnitude < 0.05f,
                    "the swap back moved the police car: " + policeBackDelta);
                policePhase = 4;
            }
            else if (stage == 3)
            {
                if (vehicle == null)
                {
                    vehicle = FindRebuildTarget(targetIndex);
                    if (vehicle == null)
                    {
                        throw new InvalidOperationException("no " + TargetName(targetIndex) + " to swap");
                    }
                    if (!vehicle.gameObject.activeInHierarchy) vehicle.gameObject.SetActive(true);
                    vehiclePosition = vehicle.transform.position;
                    ogVehicleAsset = vehicle.VisualAssetForVerification ?? string.Empty;
                    vehicleAt = Time.timeAsDouble;
                    vehiclePhase = 0;
                }
                string asset = vehicle.VisualAssetForVerification ?? string.Empty;
                bool ready = vehicle.RuntimeVisualReadyForVerification || HasLoadedVehicleModel(vehicle);
                double waited = Time.timeAsDouble - vehicleAt;
                if (vehiclePhase == 0)
                {
                    if (!ready)
                    {
                        if (waited < 30d) return;
                        throw new InvalidOperationException(
                            "the " + TargetName(targetIndex) + " never loaded its DEFAULT body: " + asset);
                    }
                    vehicleModel = vehicle.VisualRootForVerification;
                    vehicleLength = vehicle.BodyLengthForVerification;
                    // Every vehicle to POTATO, so the body under test is covered.
                    GymVersionedLooks.ApplyHostVersions(null, GymVersionedLooks.VehicleKeys);
                    vehicleAt = Time.timeAsDouble;
                    vehiclePhase = 1;
                    return;
                }
                if (vehiclePhase == 1)
                {
                    if (!asset.Contains("/remake/") || !ready)
                    {
                        if (waited < 30d) return;
                        throw new InvalidOperationException(
                            "the " + TargetName(targetIndex) + " did not swap to its POTATO model: " + asset);
                    }
                    Require(vehicle.VisualRootForVerification != null &&
                        vehicle.VisualRootForVerification != vehicleModel,
                        "the " + TargetName(targetIndex) + " kept its old model");
                    if (targetIndex == 0)
                    {
                        Require(Vector3.Distance(vehicle.transform.position, vehiclePosition) < 0.05f,
                            "the swap moved the car");
                    }
                    else
                    {
                        Require(Mathf.Abs(vehicle.BodyLengthForVerification - vehicleLength) <=
                            Mathf.Max(0.5f, vehicleLength * 0.25f),
                            "the swap changed the " + TargetName(targetIndex) + " body length");
                    }
                    if (vehicle.IsCloud) Require(vehicle.RiderAnchorForVerification,
                        "the cloud came back without its rider anchor");
                    vehicleModel = vehicle.VisualRootForVerification;
                    GymVersionedLooks.ApplyHostVersions(null, null);
                    vehicleAt = Time.timeAsDouble;
                    vehiclePhase = 2;
                    return;
                }
                if (asset.Contains("/remake/") || !ready)
                {
                    if (waited < 30d) return;
                    throw new InvalidOperationException(
                        "the " + TargetName(targetIndex) + " did not swap back to its DEFAULT model: " + asset);
                }
                Require(vehicle.VisualRootForVerification != null &&
                    vehicle.VisualRootForVerification != vehicleModel,
                    "the " + TargetName(targetIndex) + " kept its POTATO model");
                Require(asset == ogVehicleAsset,
                    "the " + TargetName(targetIndex) + " came back on a different DEFAULT model: " + asset);
                if (targetIndex == 0)
                {
                    Require(Vector3.Distance(vehicle.transform.position, vehiclePosition) < 0.05f,
                        "the swap back moved the car");
                }
                else
                {
                    Require(Mathf.Abs(vehicle.BodyLengthForVerification - vehicleLength) <=
                        Mathf.Max(0.5f, vehicleLength * 0.25f),
                        "the swap back changed the " + TargetName(targetIndex) + " body length");
                }
                if (vehicle.IsCloud) Require(vehicle.RiderAnchorForVerification,
                    "the cloud came back without its rider anchor");
                targetIndex++;
                vehicle = null;
                vehiclePhase = 0;
                return;
            }
            Require(targetIndex == 3, "the vehicle phase did not run");
            Require(policePhase == 4, "the police swap did not run");
            Debug.Log("GYMCHAOS_VISUAL_REBUILD_OK identity=Davie og=Characters/Enemies slop=Characters/Remake " +
                "vehicle=car+bus+cloud+police og<->slop reloads=0");
            code = 0;
        }
        catch (Exception exception)
        {
            Debug.LogError("GYMCHAOS_VISUAL_REBUILD_FAILED " + exception);
        }
        SessionState.EraseBool(RequestedKey);
        EditorApplication.update -= Tick;
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

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
    // The body is there when the car renders it again: a live renderer under
    // the root is the observable the flag only reports a frame later.
    private static bool HasLoadedVehicleModel(GymVisitorVehicle car)
    {
        Renderer[] renderers = car.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] != null && renderers[i].enabled) return true;
        }
        return false;
    }

    // 0: the parked car with a cabin, 1: Davie's bus, 2: Goku's cloud.
    private static GymVisitorVehicle FindRebuildTarget(int index)
    {
        GymVisitorVehicle[] all = UnityEngine.Object.FindObjectsByType<GymVisitorVehicle>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (GymVisitorVehicle candidate in all)
        {
            if (candidate == null) continue;
            if (index == 0)
            {
                if (candidate.IsBus || candidate.IsCloud) continue;
                if (!candidate.IsParked || !candidate.gameObject.activeInHierarchy) continue;
                if (candidate.GetComponent<GymVehicleCabin>() == null) continue;
                if (!candidate.RuntimeVisualReadyForVerification) continue;
                string asset = candidate.VisualAssetForVerification ?? string.Empty;
                if (asset.Contains("/remake/")) continue;
                return candidate;
            }
            if (index == 1 && candidate.IsBus) return candidate;
            if (index == 2 && candidate.IsCloud) return candidate;
        }
        return null;
    }

    private static string TargetName(int index) =>
        index == 0 ? "car" : index == 1 ? "bus" : "cloud";

}
#endif
