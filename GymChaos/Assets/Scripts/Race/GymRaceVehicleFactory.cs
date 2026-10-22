using UnityEngine;

/// <summary>
/// Builds the six race cars with their drivers: the visitor cars of Arnold,
/// Cbum, Jay Cutler and Zyzz, the police car, and the player's buggy. Each
/// car hangs under a holder whose origin is the tyre contact point, which is
/// what <see cref="GymRaceKart"/> moves.
/// </summary>
public static class GymRaceVehicleFactory
{
    public const string PlayerBuggyAsset = "BodyBuilders/vehicles/player_buggy.glb";
    private const string PoliceCarAsset = "BodyBuilders/vehicles/Policecar_lod.glb";
    private const float PoliceCarLength = 4.0f * GymOutdoorBuilder.VehicleScale * GymOutdoorBuilder.CarSizeBoost;
    // Fallback length when the buggy GLB is not exported yet.
    private const float PlayerBuggyLength = 3.4f;

    public sealed class Car
    {
        public BodybuilderIdentity Identity;
        public bool IsPolice;
        public bool IsPlayer;
        public Transform Holder;
        public GymVisitorVehicle Vehicle;
        public GameObject Model;
        public GymVehicleDriverSeat Seat;
        public GymVehicleWheelSpinner Wheels;
        public bool Failed;
        private bool glbDone;

        public bool Ready => Failed || (Vehicle != null ? Vehicle.RaceVisualReady : glbDone);

        public void MarkLoaded(bool failed)
        {
            glbDone = true;
            Failed |= failed;
        }

        public GymVehicleDriverSeat ResolveSeat()
        {
            if (Seat == null && Vehicle != null)
            {
                Seat = Vehicle.DriverSeat;
            }
            return Seat;
        }

        public GymVehicleWheelSpinner ResolveWheels()
        {
            if (Wheels == null && Holder != null)
            {
                Wheels = Holder.GetComponentInChildren<GymVehicleWheelSpinner>(true);
            }
            return Wheels;
        }
    }

    public static Car CreateVisitorCar(BodybuilderIdentity identity, Pose pose)
    {
        Car car = new Car { Identity = identity };
        car.Holder = new GameObject(identity + " Race Car Holder").transform;
        car.Holder.SetPositionAndRotation(pose.position, pose.rotation);
        Vector3 rootPosition = pose.position + Vector3.up * GymVisitorVehicle.RaceGroundOffset;
        car.Vehicle = GymVisitorVehicle.CreateRaceVisual(identity, rootPosition, pose.rotation);
        car.Vehicle.transform.SetParent(car.Holder, true);
        GymVehicleLights.Attach(car.Holder, identity.ToString());
        return car;
    }

    public static Car CreatePoliceCar(Pose pose)
    {
        Car car = new Car { Identity = BodybuilderIdentity.Policeman, IsPolice = true };
        car.Holder = new GameObject("Police Race Car Holder").transform;
        car.Holder.SetPositionAndRotation(pose.position, pose.rotation);
        GymVehicleLights.Attach(car.Holder, "police");
        RuntimeGlbSceneLoader.Request(
            PoliceCarAsset,
            car.Holder,
            pose.position,
            pose.rotation,
            Vector3.one,
            "Police Race Car",
            0,
            settleOnSupport: false,
            supportY: pose.position.y,
            onLoaded: root => FinishGlbCar(car, root, PoliceCarLength, 1.5f));
        return car;
    }

    public static Car CreatePlayerCar(Pose pose)
    {
        Car car = new Car { IsPlayer = true };
        car.Holder = new GameObject("Player Race Buggy Holder").transform;
        car.Holder.SetPositionAndRotation(pose.position, pose.rotation);
        GymVehicleLights.Attach(car.Holder);
        RuntimeGlbSceneLoader.Request(
            PlayerBuggyAsset,
            car.Holder,
            pose.position,
            pose.rotation,
            Vector3.one,
            "Player Race Buggy",
            0,
            settleOnSupport: false,
            supportY: pose.position.y,
            onLoaded: root =>
            {
                if (root == null)
                {
                    // Not exported yet: drive Cbum's car body without a seat.
                    Debug.LogWarning("GYMCHAOS_RACE_PLAYER_BUGGY_MISSING path=" + PlayerBuggyAsset);
                    car.Vehicle = GymVisitorVehicle.CreateRaceVisual(
                        BodybuilderIdentity.Cbum,
                        car.Holder.position + Vector3.up * GymVisitorVehicle.RaceGroundOffset,
                        car.Holder.rotation);
                    car.Vehicle.transform.SetParent(car.Holder, true);
                    car.MarkLoaded(false);
                    return;
                }
                FinishGlbCar(car, root, 0f, 1f);
            });
        return car;
    }

    /// <summary>Scale (optional), ground, wheels, cabin frame and driver seat for a GLB car.</summary>
    private static void FinishGlbCar(Car car, GameObject root, float targetLength, float playbackSpeed)
    {
        if (root == null)
        {
            car.MarkLoaded(true);
            return;
        }
        car.Model = root;
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length > 0 && targetLength > 0f)
        {
            Bounds bounds = Combined(renderers);
            float horizontal = Mathf.Max(bounds.size.x, bounds.size.z);
            if (horizontal > 0.001f)
            {
                root.transform.localScale *= targetLength / horizontal;
            }
        }
        if (renderers.Length > 0)
        {
            Physics.SyncTransforms();
            Bounds bounds = Combined(renderers);
            root.transform.position += Vector3.up * (car.Holder.position.y - bounds.min.y);
        }
        car.Wheels = GymVehicleWheelSpinner.Attach(root, () => true);

        // Seat and door work in metres: hang them off an unscaled frame.
        Transform frame = new GameObject("Race Cabin Frame").transform;
        frame.SetParent(root.transform, false);
        Vector3 rootScale = root.transform.lossyScale;
        frame.localScale = new Vector3(1f / rootScale.x, 1f / rootScale.y, 1f / rootScale.z);
        GymVehicleCabin cabin = GymVehicleCabin.Attach(frame, root);
        if (cabin != null && cabin.IsReady)
        {
            GymVehicleDriverSeat seat = frame.gameObject.AddComponent<GymVehicleDriverSeat>();
            float ground = frame.InverseTransformPoint(car.Holder.position).y;
            float halfLength = renderers.Length > 0 ? Combined(renderers).extents.z : 2f;
            // Lane point beside the driver door, a little behind it.
            seat.Initialize(null, cabin, ground, new Vector3(-1.9f, ground, -halfLength * 0.3f));
            seat.PlaybackSpeed = playbackSpeed;
            car.Seat = seat;
        }
        else
        {
            Debug.LogWarning($"GYMCHAOS_RACE_CABIN_MISSING car={root.name}");
        }
        GymShadowProxy.Apply(root);
        car.MarkLoaded(false);
    }

    private static Bounds Combined(Renderer[] renderers)
    {
        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
        {
            bounds.Encapsulate(renderers[i].bounds);
        }
        return bounds;
    }

    /// <summary>
    /// A passive race-only clone of a roster character: not tagged as an
    /// enemy, not an opponent, ignored by visitor and grudge logic.
    /// </summary>
    public static EnemyFighter CreateDriver(BodybuilderIdentity identity, PlayerMovement player, Vector3 position)
    {
        GameObject driver = new GameObject($"Race Driver - {identity}");
        driver.tag = "Untagged";
        driver.layer = EnemyFighter.EnemyCollisionLayer;
        driver.transform.position = position;

        CapsuleCollider collider = driver.AddComponent<CapsuleCollider>();
        collider.center = new Vector3(0f, EnemyFighter.EnemyCapsuleCenterY, 0f);
        collider.height = EnemyFighter.EnemyCapsuleHeight;
        collider.radius = EnemyFighter.GetBodyRadiusForIdentity(identity);
        Rigidbody body = driver.AddComponent<Rigidbody>();
        body.mass = 85f;
        body.isKinematic = true;
        body.interpolation = RigidbodyInterpolation.Interpolate;

        BodybuilderEnemyVisual.Build(driver, identity);
        EnemyFighter fighter = driver.AddComponent<EnemyFighter>();
        fighter.Configure(identity, player, 1000f, police: false, passive: true, countAsOpponent: false);
        return fighter;
    }
}
