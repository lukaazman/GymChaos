#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Enemies are hittable in every state (2026-10-21).
// While the normal visitor lifecycle runs (plus forced squat, deadlift,
// treadmill, locker visit, car arrival, Goku's cloud and a departure), every
// living fighter is sampled repeatedly:
// - coverage: the share of its skinned body surface within a few centimetres
//   of an enabled combat hitbox (thrown items and punches only hit those);
// - collidable: its Rigidbody detects collisions and the hitboxes collide
//   with the pickup layer, so a thrown item can land;
// - punch: the real player punch resolution aimed at the chest and at the
//   pelvis from in front hits a fighter (in a car seat: the chest, through
//   the window).
// At the end real throws hit a squatting member, a walking member and the
// rider/driver if one was seen. Results are grouped by state.
[InitializeOnLoad]
public static class GymChaosEnemyHittableVerifier
{
    private const string RequestedKey = "GymChaos.EnemyHittableVerificationRequested";
    private const double SampleSeconds = 70d;
    private const float CoverageDistance = 0.08f;
    private const float MinimumCoverage = 0.85f;
    private const int SkinSamples = 260;

    private sealed class StateStats
    {
        public int samples;
        public float worstCoverage = 1f;
        public string worstCoverageDetail = "";
        public int notCollidable;
        public int punchMisses;
        public string punchMissDetail = "";
    }

    private static double started;
    private static double stageStarted;
    private static int lastFrame;
    private static int stage;
    private static bool finished;
    private static int resultCode;
    private static PlayerMovement player;
    private static GymVisitorDirector director;
    private static int pickupLayer = -1;
    private static int roundRobin;
    private static double nextSample;
    private static readonly Dictionary<string, StateStats> stats = new Dictionary<string, StateStats>();
    private static readonly List<string> forced = new List<string>();
    private static Mesh bakedMesh;
    private static EnemyFighter squatter;
    private static EnemyFighter rider;
    private static EnemyFighter driver;
    private static readonly List<(EnemyFighter fighter, float health, string label, double at)> throws =
        new List<(EnemyFighter, float, string, double)>();
    private static string details = "none";

    static GymChaosEnemyHittableVerifier()
    {
        if (SessionState.GetBool(RequestedKey, false))
        {
            Hook();
        }
    }

    [MenuItem("Tools/GymChaos/Run Enemy Hittable Verification")]
    public static void Run()
    {
        SessionState.SetBool(RequestedKey, true);
        GymChaosVerifierSave.Neutralize();
        ResetState();
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
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

    private static void ResetState()
    {
        started = EditorApplication.timeSinceStartup;
        stageStarted = started;
        lastFrame = -1;
        stage = 0;
        finished = false;
        resultCode = 1;
        GymChaosVerifierExit.Record(resultCode);
        player = null;
        director = null;
        pickupLayer = -1;
        roundRobin = 0;
        nextSample = 0d;
        stats.Clear();
        forced.Clear();
        squatter = rider = driver = null;
        throws.Clear();
        details = "none";
    }

    private static void PlayModeChanged(PlayModeStateChange change)
    {
        if (change == PlayModeStateChange.EnteredPlayMode)
        {
            ResetState();
        }
        if (change != PlayModeStateChange.EnteredEditMode)
        {
            return;
        }
        EditorApplication.update -= Tick;
        EditorApplication.playModeStateChanged -= PlayModeChanged;
        SessionState.EraseBool(RequestedKey);
        GymChaosVerifierSave.Restore();
        if (Application.isBatchMode)
        {
            GymChaosVerifierExit.Exit(resultCode);
        }
    }

    private static void Tick()
    {
        if (finished || !EditorApplication.isPlaying)
        {
            return;
        }
        try
        {
            double now = EditorApplication.timeSinceStartup;
            if (now - started > 200d)
            {
                throw new TimeoutException($"timed out at stage {stage}: {details}");
            }
            if (Time.frameCount == lastFrame)
            {
                return;
            }
            lastFrame = Time.frameCount;
            if (player != null)
            {
                player.RestoreHealth(player.MaxHealth);
            }
            switch (stage)
            {
                case 0:
                    Setup(now);
                    break;
                case 1:
                    if (now - stageStarted < 4d) return;
                    ForceStates();
                    Next(2, now);
                    break;
                case 2:
                    if (now >= nextSample)
                    {
                        nextSample = now + 0.12d;
                        SampleNext();
                    }
                    if (now - stageStarted >= SampleSeconds)
                    {
                        StartThrows(now);
                        Next(3, now);
                    }
                    break;
                case 3:
                    if (now - stageStarted < 1.2d) return;
                    Report();
                    break;
            }
        }
        catch (Exception exception)
        {
            Debug.LogError($"GYMCHAOS_ENEMY_HITTABLE_FAILED {exception.Message} stage={stage} last=[{details}]");
            Finish(1);
        }
    }

    private static void Setup(double now)
    {
        if (Time.frameCount < 3) return;
        player = UnityEngine.Object.FindAnyObjectByType<PlayerMovement>();
        director = UnityEngine.Object.FindAnyObjectByType<GymVisitorDirector>();
        PickupItem anyItem = UnityEngine.Object.FindAnyObjectByType<PickupItem>();
        if (player == null || director == null || anyItem == null)
        {
            if (now - started > 40d)
                throw new InvalidOperationException(
                    $"missing player={player != null} director={director != null} pickup={anyItem != null}");
            return;
        }
        pickupLayer = anyItem.gameObject.layer;
        Next(1, now);
    }

    private static void ForceStates()
    {
        if (director.BeginWorkoutForVerification(out EnemyFighter squat, out _))
        {
            squatter = squat;
            forced.Add("squat=" + squat.Identity);
        }
        if (director.BeginDeadliftForVerification(out EnemyFighter lifter, out _))
        {
            forced.Add("deadlift=" + lifter.Identity);
        }
        foreach (EnemyFighter fighter in UnityEngine.Object.FindObjectsByType<EnemyFighter>(FindObjectsSortMode.None))
        {
            GymVisitorAgent agent = fighter != null ? fighter.GetComponent<GymVisitorAgent>() : null;
            if (fighter == null || fighter.IsDead || fighter.IsPolice || fighter.IsPassive || agent == null ||
                agent.State != GymVisitorAgent.VisitorState.FreeRoaming || fighter == squatter || fighter.IsOnTreadmill)
            {
                continue;
            }
            GymExerciseStation treadmill = GymExerciseStation.FindClosestTreadmill(fighter.transform.position, 80f);
            if (treadmill != null && fighter.BeginTreadmillForVerification(treadmill, 1.4f))
            {
                forced.Add("treadmill=" + fighter.Identity);
                break;
            }
        }
        if (director.BeginLockerVisitForVerification(out EnemyFighter locker))
        {
            forced.Add("locker=" + locker.Identity);
        }
        if (director.BeginVehicleArrivalForVerification(BodybuilderIdentity.Cbum, out EnemyFighter cbum, out _))
        {
            forced.Add("arrival=" + cbum.Identity);
        }
        if (director.BeginVehicleArrivalForVerification(BodybuilderIdentity.Goku, out EnemyFighter goku, out _))
        {
            forced.Add("arrival=" + goku.Identity);
        }
        if (director.BeginIdentityDepartureForVerification(BodybuilderIdentity.Zyzz, out EnemyFighter zyzz, out _))
        {
            forced.Add("departure=" + zyzz.Identity);
        }
        Debug.Log("GYMCHAOS_ENEMY_HITTABLE_FORCED " + string.Join(" ", forced));
    }

    private static string StateKey(EnemyFighter fighter)
    {
        GymVisitorAgent agent = fighter.GetComponent<GymVisitorAgent>();
        string where = GymOutdoorBuilder.IsPlayerOutsideGym(fighter.transform.position) ? "outside"
            : GymBackRoomBuilder.IsInsideRoom(fighter.transform.position) ? "locker" : "gym";
        string mode = fighter.IsSeatedInVehicle ? "seated"
            : fighter.IsRidingVehicleForVerification ? "riding"
            : fighter.IsOnTreadmill ? "treadmill"
            : fighter.IsGokuFlightActive ? "flying"
            : agent != null ? agent.State.ToString() : "fighter";
        return $"{where}/{mode}/{fighter.AnimationState}";
    }

    private static void SampleNext()
    {
        List<EnemyFighter> living = new List<EnemyFighter>();
        foreach (EnemyFighter fighter in UnityEngine.Object.FindObjectsByType<EnemyFighter>(FindObjectsSortMode.InstanceID))
        {
            // The reception suit is staff, not an enemy (known: its bones sit
            // off its skin, see the hitbox fit log).
            if (fighter != null && fighter.isActiveAndEnabled && !fighter.IsDead &&
                fighter.Identity != BodybuilderIdentity.Manwithsuit1 &&
                fighter.GetComponent<EnemyMeshHitboxRig>() != null)
            {
                living.Add(fighter);
            }
        }
        if (living.Count == 0)
        {
            return;
        }
        EnemyFighter target = living[roundRobin++ % living.Count];
        if (target.IsRidingVehicleForVerification) rider = target;
        if (target.IsSeatedInVehicle) driver = target;
        Sample(target);
    }

    private static void Sample(EnemyFighter fighter)
    {
        EnemyMeshHitboxRig rig = fighter.GetComponent<EnemyMeshHitboxRig>();
        string key = StateKey(fighter);
        if (!stats.TryGetValue(key, out StateStats state))
        {
            state = new StateStats();
            stats.Add(key, state);
        }
        state.samples++;

        // Coverage of the deformed skin by enabled combat colliders.
        List<Collider> colliders = new List<Collider>();
        foreach (Collider collider in rig.CombatCollidersForVerification)
        {
            if (collider != null && collider.enabled && collider.gameObject.activeInHierarchy)
            {
                colliders.Add(collider);
            }
        }
        float coverage = 0f;
        SkinnedMeshRenderer skin = rig.BodyRendererForVerification;
        if (skin != null && skin.sharedMesh != null && colliders.Count > 0)
        {
            if (bakedMesh == null) bakedMesh = new Mesh();
            Vector3[] vertices = BakeWorldVertices(skin);
            int step = Mathf.Max(1, vertices.Length / SkinSamples);
            int covered = 0, total = 0;
            Matrix4x4 toWorld = Matrix4x4.identity;
            float worstY = 0f;
            int[] bandTotal = new int[10];
            int[] bandMissed = new int[10];
            Bounds skinBounds = skin.bounds;
            for (int i = 0; i < vertices.Length; i += step)
            {
                Vector3 point = toWorld.MultiplyPoint3x4(vertices[i]);
                total++;
                bool hit = false;
                for (int c = 0; c < colliders.Count && !hit; c++)
                {
                    hit = (colliders[c].ClosestPoint(point) - point).sqrMagnitude <= CoverageDistance * CoverageDistance;
                }
                int band = Mathf.Clamp((int)((point.y - skinBounds.min.y) / Mathf.Max(0.01f, skinBounds.size.y) * 10f), 0, 9);
                bandTotal[band]++;
                if (hit) covered++;
                else { worstY = point.y - fighter.transform.position.y; bandMissed[band]++; }
            }
            coverage = total > 0 ? covered / (float)total : 0f;
            if (state.samples == 1)
            {
                Bounds baked = new Bounds(toWorld.MultiplyPoint3x4(vertices[0]), Vector3.zero);
                for (int i = 0; i < vertices.Length; i += step) baked.Encapsulate(toWorld.MultiplyPoint3x4(vertices[i]));
                Debug.Log($"GYMCHAOS_ENEMY_HITTABLE_SKIN {fighter.Identity} baked={baked} renderer={skinBounds} coverage={coverage:F2}");
            }
            if (coverage < state.worstCoverage)
            {
                state.worstCoverage = coverage;
                StringBuilder bands = new StringBuilder();
                for (int b = 0; b < 10; b++)
                {
                    bands.Append(bandTotal[b] > 0 ? $"{bandMissed[b] * 100 / bandTotal[b]}%" : "-").Append(b < 9 ? "," : "");
                }
                state.worstCoverageDetail = $"{fighter.Identity} missedByHeightBand(feet..head)=[{bands}]";
            }
        }
        else if (state.worstCoverage > 0f)
        {
            state.worstCoverage = 0f;
            state.worstCoverageDetail = $"{fighter.Identity} skin={skin != null} colliders={colliders.Count}";
        }

        Rigidbody body = fighter.GetComponent<Rigidbody>();
        bool layerCollides = colliders.Count > 0 &&
            !Physics.GetIgnoreLayerCollision(pickupLayer, colliders[0].gameObject.layer);
        if (body == null || !body.detectCollisions || !layerCollides)
        {
            state.notCollidable++;
        }

        {
            // In a car seat only the window height counts: the chest.
            BodybuilderEnemyVisual.Rig bones = fighter.VehicleBodyAnimator != null ? fighter.VehicleBodyAnimator.BodyRig : null;
            Transform[] aims = bones == null ? new Transform[0]
                : fighter.IsSeatedInVehicle ? new[] { bones.Chest }
                : new[] { bones.Chest, bones.Hips };
            for (int i = 0; i < aims.Length; i++)
            {
                if (aims[i] == null) continue;
                Vector3 aim = aims[i].position;
                if (!TryFreeApproach(fighter, aim, 1.1f, out Vector3 origin))
                {
                    continue;   // boxed in by equipment on every side
                }
                EnemyFighter punched = player.ResolvePunchEnemyForVerification(origin, aim - origin);
                // Another member standing in the line takes the punch instead;
                // only a punch that hits nobody is a miss.
                if (punched == null)
                {
                    state.punchMisses++;
                    state.punchMissDetail = $"{fighter.Identity} {(i == 0 ? "chest" : "pelvis")} hit={(punched != null ? punched.Identity.ToString() : "none")}";
                }
            }
        }
        details = $"samples={stats.Count} last={key}";
    }

    // BakeMesh output depends on the importer scale baked into the skinning
    // matrices; take the bake/transform pairing whose bounds match the
    // renderer's own world bounds.
    private static Vector3[] BakeWorldVertices(SkinnedMeshRenderer skin)
    {
        if (bakedMesh == null) bakedMesh = new Mesh();
        Bounds target = skin.bounds;
        Vector3[] best = null;
        float bestError = float.PositiveInfinity;
        Matrix4x4 full = skin.transform.localToWorldMatrix;
        Matrix4x4 noScale = Matrix4x4.TRS(skin.transform.position, skin.transform.rotation, Vector3.one);
        for (int useScale = 0; useScale < 2; useScale++)
        {
            skin.BakeMesh(bakedMesh, useScale == 1);
            Vector3[] local = bakedMesh.vertices;
            for (int m = 0; m < 2; m++)
            {
                Matrix4x4 toWorld = m == 0 ? full : noScale;
                Vector3[] world = new Vector3[local.Length];
                Bounds bounds = new Bounds(toWorld.MultiplyPoint3x4(local[0]), Vector3.zero);
                for (int i = 0; i < local.Length; i++)
                {
                    world[i] = toWorld.MultiplyPoint3x4(local[i]);
                    bounds.Encapsulate(world[i]);
                }
                float error = (bounds.center - target.center).magnitude + (bounds.size - target.size).magnitude;
                if (error < bestError)
                {
                    bestError = error;
                    best = world;
                }
            }
        }
        return best;
    }

    // A point at distance from aim with a clear line to it (nothing but the
    // target in between), trying the fighter's front, back and sides.
    private static bool TryFreeApproach(EnemyFighter target, Vector3 aim, float distance, out Vector3 start)
    {
        Vector3 forward = Vector3.ProjectOnPlane(target.transform.forward, Vector3.up).normalized;
        Vector3 right = Vector3.Cross(Vector3.up, forward);
        foreach (Vector3 direction in new[] { forward, -forward, right, -right })
        {
            start = aim + direction * distance;
            bool blocked = false;
            foreach (RaycastHit hit in Physics.SphereCastAll(start, 0.12f, aim - start, distance, ~0, QueryTriggerInteraction.Ignore))
            {
                if (hit.collider.GetComponentInParent<EnemyFighter>() == null &&
                    hit.collider.GetComponentInParent<PickupItem>() == null)
                {
                    blocked = true;
                    break;
                }
            }
            if (!blocked && !Physics.CheckSphere(start, 0.15f, ~0, QueryTriggerInteraction.Ignore))
            {
                return true;
            }
        }
        start = aim;
        return false;
    }

    private static void StartThrows(double now)
    {
        List<EnemyFighter> targets = new List<EnemyFighter>();
        if (squatter != null && !squatter.IsDead) targets.Add(squatter);
        if (rider != null && !rider.IsDead && rider.IsRidingVehicleForVerification) targets.Add(rider);
        if (driver != null && !driver.IsDead && driver.IsSeatedInVehicle) targets.Add(driver);
        foreach (EnemyFighter fighter in UnityEngine.Object.FindObjectsByType<EnemyFighter>(FindObjectsSortMode.InstanceID))
        {
            GymVisitorAgent agent = fighter != null ? fighter.GetComponent<GymVisitorAgent>() : null;
            if (fighter != null && !fighter.IsDead && !fighter.IsPolice && !fighter.IsPassive &&
                agent != null && agent.State == GymVisitorAgent.VisitorState.FreeRoaming &&
                !targets.Contains(fighter) && fighter.isActiveAndEnabled)
            {
                targets.Add(fighter);
                break;
            }
        }
        List<PickupItem> items = new List<PickupItem>();
        foreach (PickupItem item in UnityEngine.Object.FindObjectsByType<PickupItem>(FindObjectsSortMode.InstanceID))
        {
            if (item != null && !item.IsHeld && item.IsThrowableWeapon && item.GetComponent<Rigidbody>() != null &&
                item.ItemType != WeightType.Barbell && item.ItemType != WeightType.EzBar)
            {
                items.Add(item);
            }
        }
        for (int i = 0; i < targets.Count && i < items.Count; i++)
        {
            EnemyFighter target = targets[i];
            BodybuilderEnemyVisual.Rig bones = target.VehicleBodyAnimator != null ? target.VehicleBodyAnimator.BodyRig : null;
            Transform aimBone = bones == null ? null : target.IsSeatedInVehicle ? bones.Chest : bones.Hips;
            Vector3 aim = aimBone != null ? aimBone.position : target.transform.position + Vector3.up;
            Vector3 start;
            if (target.IsSeatedInVehicle)
            {
                // From beside the car, at the side window.
                Vector3 side = Vector3.ProjectOnPlane(target.transform.right, Vector3.up).normalized;
                start = aim + side * 1.7f;
                if (Physics.CheckSphere(start, 0.25f, ~0, QueryTriggerInteraction.Ignore))
                {
                    start = aim - side * 1.7f;
                }
            }
            else if (!TryFreeApproach(target, aim, 1.4f, out start))
            {
                continue;
            }
            PickupItem item = items[i];
            Rigidbody itemBody = item.GetComponent<Rigidbody>();
            itemBody.isKinematic = false;
            itemBody.position = start;
            item.transform.position = start;
            Physics.SyncTransforms();
            item.Throw((aim - start).normalized * 14f, null, 0f, false);
            throws.Add((target, target.CurrentHealth, StateKey(target), now));
        }
    }

    private static void Report()
    {
        StringBuilder text = new StringBuilder();
        List<string> failures = new List<string>();
        foreach (KeyValuePair<string, StateStats> entry in stats)
        {
            StateStats s = entry.Value;
            text.Append($" [{entry.Key} n={s.samples} cov={s.worstCoverage:F2} noCollide={s.notCollidable} punchMiss={s.punchMisses}]");
            if (s.worstCoverage < MinimumCoverage)
                failures.Add($"coverage {entry.Key} {s.worstCoverage:F2} ({s.worstCoverageDetail})");
            if (s.notCollidable > 0)
                failures.Add($"not collidable {entry.Key} x{s.notCollidable}");
            if (s.punchMisses > 0)
                failures.Add($"punch missed {entry.Key} x{s.punchMisses} ({s.punchMissDetail})");
        }
        foreach ((EnemyFighter fighter, float health, string label, double at) in throws)
        {
            bool damaged = fighter.CurrentHealth < health - 0.01f || fighter.IsDead;
            text.Append($" [throw {fighter.Identity} {label} damaged={damaged}]");
            if (!damaged)
                failures.Add($"throw did not damage {fighter.Identity} in {label}");
        }
        Debug.Log("GYMCHAOS_ENEMY_HITTABLE_STATES forced=" + string.Join(",", forced) + text);
        if (failures.Count > 0)
        {
            throw new InvalidOperationException(string.Join("; ", failures));
        }
        Debug.Log($"GYMCHAOS_ENEMY_HITTABLE_OK states={stats.Count} throws={throws.Count} rider={rider != null} driver={driver != null}");
        Finish(0);
    }

    private static void Next(int next, double now)
    {
        stage = next;
        stageStarted = now;
    }

    private static void Finish(int code)
    {
        if (finished) return;
        finished = true;
        resultCode = code;
        GymChaosVerifierExit.Record(resultCode);
        SessionState.EraseBool(RequestedKey);
        EditorApplication.update -= Tick;
        EditorApplication.isPlaying = false;
    }
}
#endif
