using System;
using UnityEngine;

/// <summary>
/// The player's race buggy (Tools/build_player_race_car.py): loads the GLB,
/// fits it to the game's vehicle scale with its tyres on the ground, rolls
/// and steers its wheels, and gives it the see-through cabin and driver seat
/// the visitor cars use, so the player gets in and out through the side door
/// exactly like a visitor.
/// </summary>
public sealed class GymPlayerRaceCar : MonoBehaviour
{
    public const string Asset = "BodyBuilders/vehicles/player_buggy.glb";
    // Authored 3.4 m long for a 1.8 m driver; cars are 4 m sources fitted to
    // 4 * VehicleScale * CarSizeBoost, so the buggy takes the same factor.
    public const float AuthoredLength = 3.4f;
    public static float TargetLength => AuthoredLength * GymOutdoorBuilder.VehicleScale * GymOutdoorBuilder.CarSizeBoost;

    private GameObject visual;
    private float groundY;

    public GymVehicleCabin Cabin { get; private set; }
    public GymVehicleDriverSeat Seat { get; private set; }
    public GymVehicleWheelSpinner Wheels { get; private set; }
    public Transform CabinFrame { get; private set; }
    public bool IsReady { get; private set; }
    public bool IsDriving { get; set; }
    public Bounds LocalBodyBounds { get; private set; }

    /// <summary>
    /// Starts loading the buggy at <paramref name="groundPosition"/>; <paramref name="ready"/>
    /// gets it once the cabin and seat exist, or null when the GLB failed.
    /// </summary>
    public static GymPlayerRaceCar Spawn(Vector3 groundPosition, Quaternion rotation, Transform parent,
        Action<GymPlayerRaceCar> ready)
    {
        GymPlayerRaceCar car = null;
        bool finished = false;
        GameObject result = null;
        void Finish()
        {
            if (result == null)
            {
                Debug.LogError($"GYMCHAOS_PLAYER_CAR_LOAD_FAILED path={Asset}", car);
                ready?.Invoke(null);
                return;
            }
            car.Build(result);
            ready?.Invoke(car.IsReady ? car : null);
        }
        GameObject root = RuntimeGlbSceneLoader.Request(Asset, parent, groundPosition, rotation, Vector3.one,
            "Player Race Buggy", 0, settleOnSupport: false, supportY: groundPosition.y,
            onLoaded: loaded =>
            {
                finished = true;
                result = loaded;
                // A cached GLB can call back before Request returns.
                if (car != null) Finish();
            });
        if (root == null) return null;
        car = root.AddComponent<GymPlayerRaceCar>();
        car.groundY = groundPosition.y;
        if (finished) Finish();
        return car;
    }

    private void Build(GameObject loaded)
    {
        visual = loaded;
        Fit();
        Wheels = GymVehicleWheelSpinner.Attach(visual, () => IsDriving);
        // The GLB root is scaled to the car length; seat and door work in
        // metres, so they hang off an unscaled frame under it.
        CabinFrame = new GameObject("Player Buggy Cabin Frame").transform;
        CabinFrame.SetParent(visual.transform, false);
        Vector3 rootScale = visual.transform.lossyScale;
        CabinFrame.localScale = new Vector3(1f / rootScale.x, 1f / rootScale.y, 1f / rootScale.z);
        Cabin = GymVehicleCabin.Attach(CabinFrame, visual);
        if (Cabin != null && Cabin.IsReady)
        {
            Seat = CabinFrame.gameObject.AddComponent<GymVehicleDriverSeat>();
            float ground = CabinFrame.InverseTransformPoint(
                new Vector3(CabinFrame.position.x, groundY, CabinFrame.position.z)).y;
            // Out of the door the driver steps a body length back along the car.
            Cabin.TryGetMarker("Hinge", out Vector3 hinge);
            Seat.Initialize(null, Cabin, ground, new Vector3(hinge.x, ground, hinge.z - 1.2f));
        }
        LocalBodyBounds = LocalBounds();
        GymShadowProxy.Apply(visual);
        IsReady = Seat != null;
        Debug.Log(
            $"GYMCHAOS_PLAYER_CAR_READY ready={(IsReady ? 1 : 0)} length={TargetLength:F2} " +
            $"cabin={(Cabin != null && Cabin.IsReady ? 1 : 0)} bounds={LocalBodyBounds.size}", this);
    }

    private void Fit()
    {
        Renderer[] renderers = visual.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0) return;
        Bounds bounds = GymShared.CombinedBounds(renderers);
        float horizontal = Mathf.Max(bounds.size.x, bounds.size.z);
        if (horizontal > 0.001f) visual.transform.localScale *= TargetLength / horizontal;
        Physics.SyncTransforms();
        bounds = GymShared.CombinedBounds(renderers);
        visual.transform.position += Vector3.up * (groundY - bounds.min.y);
        Physics.SyncTransforms();
    }

    // Body extents in this car's frame (rotation independent).
    private Bounds LocalBounds()
    {
        Bounds local = new Bounds();
        bool any = false;
        foreach (MeshFilter filter in visual.GetComponentsInChildren<MeshFilter>(true))
        {
            if (filter.sharedMesh == null ||
                GymVehicleCabin.PartName(filter.name).StartsWith("Marker__", StringComparison.Ordinal)) continue;
            Bounds b = filter.sharedMesh.bounds;
            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 p = transform.InverseTransformPoint(filter.transform.TransformPoint(new Vector3(
                    (corner & 1) == 0 ? b.min.x : b.max.x,
                    (corner & 2) == 0 ? b.min.y : b.max.y,
                    (corner & 4) == 0 ? b.min.z : b.max.z)));
                if (!any) local = new Bounds(p, Vector3.zero);
                else local.Encapsulate(p);
                any = true;
            }
        }
        return local;
    }
}
