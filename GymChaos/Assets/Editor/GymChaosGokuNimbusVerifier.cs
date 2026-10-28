#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Goku on his flying nimbus (2026-10-21).
// During a cloud arrival Goku holds the nimbus_stance pose from the
// reference figure: wide stance across the cloud with bent knees, soles on
// the cloud top, right fist out to his right side at head height, left fist
// chambered at the hip. He can be punched while riding, and when attacked he
// jumps off the cloud onto the ground and fights.
[InitializeOnLoad]
public static class GymChaosGokuNimbusVerifier
{
    private const string RequestedKey = "GymChaos.GokuNimbusVerificationRequested";
    private const float MaxSoleGap = 0.05f;

    private static double started;
    private static double stageStarted;
    private static int lastFrame;
    private static int stage;
    private static bool finished;
    private static int resultCode;
    private static PlayerMovement player;
    private static EnemyFighter goku;
    private static GymVisitorVehicle cloud;
    private static int rideSamples;
    private static int mountedFrames;
    private static Vector3 firstRootLocal;
    private static int poseFailures;
    private static float worstGap;
    private static string worstPose = "";
    private static bool punchHit;
    private static string details = "none";

    static GymChaosGokuNimbusVerifier()
    {
        if (SessionState.GetBool(RequestedKey, false))
        {
            Hook();
        }
    }

    [MenuItem("Tools/GymChaos/Run Goku Nimbus Verification")]
    public static void Run()
    {
        SessionState.SetBool(RequestedKey, true);
        GymChaosVerifierSave.Neutralize();
        ResetState();
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        Hook();
        EditorApplication.isPlaying = true;
    }

    private static void Hook() => GymChaosVerifierHooks.Hook(Tick, PlayModeChanged);

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
        goku = null;
        cloud = null;
        rideSamples = 0;
        mountedFrames = 0;
        poseFailures = 0;
        worstGap = 0f;
        worstPose = "";
        punchHit = false;
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
            if (now - started > 150d)
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
                    Observe(now);
                    break;
                case 2:
                    // Attacked mid-ride: off the cloud, on the ground, fighting.
                    if (now - stageStarted < 1.5d) return;
                    float ground = goku.transform.position.y;
                    if (goku.IsRidingVehicleForVerification)
                        throw new InvalidOperationException("Goku stayed on the cloud after being hit");
                    if (!goku.IsAggressive)
                        throw new InvalidOperationException("Goku did not turn on his attacker");
                    if (cloud.HasMountedRider)
                        throw new InvalidOperationException("the cloud still lists Goku as its rider");
                    Debug.Log($"GYMCHAOS_GOKU_CLOUD_POSE_OK samples={rideSamples} worstSoleGap={worstGap:F3} " +
                        $"punchWhileRiding={punchHit} leftCloudY={ground:F2} {worstPose}");
                    Finish(0);
                    break;
            }
        }
        catch (Exception exception)
        {
            Debug.LogError($"GYMCHAOS_GOKU_CLOUD_POSE_FAILED {exception.Message} stage={stage} last=[{details}]");
            Finish(1);
        }
    }

    private static void Setup(double now)
    {
        if (Time.frameCount < 3) return;
        player = UnityEngine.Object.FindAnyObjectByType<PlayerMovement>();
        GymVisitorDirector director = UnityEngine.Object.FindAnyObjectByType<GymVisitorDirector>();
        if (player == null || director == null)
        {
            if (now - started > 40d) throw new InvalidOperationException("missing player or director");
            return;
        }
        if (now - started < 4d) return;
        if (!director.BeginVehicleArrivalForVerification(BodybuilderIdentity.Goku, out goku, out cloud))
        {
            if (now - started > 30d) throw new InvalidOperationException("Goku cloud arrival did not start");
            return;
        }
        if (cloud == null || !cloud.IsCloud)
            throw new InvalidOperationException("Goku's vehicle is not the cloud");
        Debug.Log("GYMCHAOS_GOKU_NIMBUS_ARRIVAL_STARTED");
        Next(1, now);
    }

    private static void Observe(double now)
    {
        if (!goku.IsRidingVehicleForVerification)
        {
            if (rideSamples > 0)
                throw new InvalidOperationException($"the ride ended before the attack test (samples={rideSamples})");
            if (now - stageStarted > 25d)
                throw new InvalidOperationException("Goku never mounted the cloud");
            return;
        }
        MixamoScanRetargetAnimator animator = goku.VehicleBodyAnimator;
        if (animator == null || !animator.IsNimbusStanceActive)
            throw new InvalidOperationException("riding without the nimbus stance");
        BodybuilderEnemyVisual.Rig rig = animator.BodyRig;
        // The pose blends in from the previous clip over the first frames.
        if (++mountedFrames < 15)
        {
            return;
        }
        rideSamples++;
        // The real gap: lowest boot skin under each foot against the cloud mesh.
        float leftGap = SkinSoleGap(rig.LeftFoot), rightGap = SkinSoleGap(rig.RightFoot);
        float gap = Mathf.Max(Mathf.Abs(leftGap), Mathf.Abs(rightGap));
        worstGap = Mathf.Max(worstGap, gap);
        bool onCloud = !float.IsNaN(cloud.CloudSurfaceY(rig.LeftFoot.position)) &&
            !float.IsNaN(cloud.CloudSurfaceY(rig.RightFoot.position));

        // Pose in Goku's own frame.
        Transform root = goku.transform;
        Vector3 L(Transform t) => root.InverseTransformPoint(t.position);
        float stance = Mathf.Abs(L(rig.LeftFoot).x - L(rig.RightFoot).x);
        float soleY = (L(rig.LeftFoot).y + L(rig.RightFoot).y) * 0.5f;
        float headHeight = L(rig.Head).y - soleY;
        float hipsHeight = (L(rig.Hips).y - soleY) / Mathf.Max(0.01f, headHeight);
        float fistOut = L(rig.RightHand).x - L(rig.RightShoulder).x;
        float fistHeight = (L(rig.RightHand).y - soleY) / Mathf.Max(0.01f, headHeight);
        float chamberHeight = (L(rig.LeftHand).y - soleY) / Mathf.Max(0.01f, headHeight);
        float chamberToHip = Vector3.Distance(rig.LeftHand.position, rig.Hips.position) / Mathf.Max(0.01f, headHeight);
        string pose = $"stance={stance:F2} hips={hipsHeight:F2} fistOut={fistOut:F2} fistHeight={fistHeight:F2} " +
            $"chamber={chamberHeight:F2} chamberToHip={chamberToHip:F2} onCloud={onCloud} gap={gap:F3} soles={leftGap:F3}/{rightGap:F3} " +
            $"ik={animator.NimbusLeftSoleGapForVerification:F3}/{animator.NimbusRightSoleGapForVerification:F3} " +
            $"footsLocal={cloud.transform.InverseTransformPoint(rig.LeftFoot.position)}/{cloud.transform.InverseTransformPoint(rig.RightFoot.position)} " +
            $"rootLocal={cloud.transform.InverseTransformPoint(root.position)}";
        details = pose;
        // Goku rides with the cloud: his root stays on the rider anchor.
        Vector3 rootLocal = cloud.transform.InverseTransformPoint(root.position);
        if (rideSamples == 1) firstRootLocal = rootLocal;
        bool onAnchor = Vector3.Distance(rootLocal, firstRootLocal) < 0.15f;
        pose += $" anchorDrift={Vector3.Distance(rootLocal, firstRootLocal):F2}";
        bool good = onCloud && onAnchor && gap <= MaxSoleGap &&
            stance > 0.9f &&                     // feet spread wide across the cloud
            hipsHeight < 0.6f &&                 // knees bent: standing is ~0.63
            fistOut > 0.35f && fistHeight > 0.7f && // right fist out to the side, high
            chamberHeight > 0.35f && chamberHeight < 0.62f && chamberToHip < 0.35f;
        if (!good)
        {
            poseFailures++;
            worstPose = pose;
        }
        if (Time.frameCount % 20 == 0) Debug.Log($"GYMCHAOS_GOKU_NIMBUS_SAMPLE {pose}");
        if (rideSamples < 20)
        {
            return;
        }
        if (poseFailures > rideSamples / 10)
            throw new InvalidOperationException($"nimbus stance off in {poseFailures}/{rideSamples} samples: {worstPose}");

        // Hittable while riding: a punch at the chest from in front lands.
        Vector3 chest = rig.Chest.position;
        Vector3 origin = chest + root.forward * 1.1f;
        EnemyFighter punched = player.ResolvePunchEnemyForVerification(origin, chest - origin);
        punchHit = punched == goku;
        if (!punchHit)
            throw new InvalidOperationException($"punch at riding Goku hit {(punched != null ? punched.Identity.ToString() : "nothing")}");
        goku.TakeMeleeHit(-root.forward * 4f, 1f, 0.2f);
        if (string.IsNullOrEmpty(worstPose)) worstPose = pose;
        Next(2, now);
    }

    private static Mesh soleScratch;

    // Lowest skin point within 0.3 m (horizontally) of a foot bone, minus the
    // cloud top under it: + floats, - sinks in.
    private static float SkinSoleGap(Transform foot)
    {
        SkinnedMeshRenderer skin = null;
        foreach (SkinnedMeshRenderer candidate in goku.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (candidate.sharedMesh != null && (skin == null || candidate.sharedMesh.vertexCount > skin.sharedMesh.vertexCount))
                skin = candidate;
        }
        if (skin == null) return float.NaN;
        if (soleScratch == null) soleScratch = new Mesh();
        Vector3[] world = GymSkinSampler.BakeWorld(skin, soleScratch);
        Vector3 sole = foot.position;
        float lowest = float.PositiveInfinity;
        for (int i = 0; i < world.Length; i += 2)
        {
            Vector3 p = world[i];
            float dx = p.x - foot.position.x, dz = p.z - foot.position.z;
            if (dx * dx + dz * dz < 0.09f && p.y < lowest && p.y < foot.position.y + 0.1f)
            {
                lowest = p.y;
                sole = p;
            }
        }
        float surface = cloud.CloudSurfaceY(sole);
        return float.IsNaN(surface) ? float.NaN : lowest - surface;
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
