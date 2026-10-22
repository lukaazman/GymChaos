#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// 2026-10-22 batch:
// hank     - the homeless man can be punched sitting and lying in the tent,
//            a throw knocks him out and his emptied health bar shows;
// ronnie   - Ronnie with nobody to chase in the locker room walks back to the
//            main floor through the door (no jump through the wall);
// mats     - members walk across the cardio and lunges floor mats;
// xp       - hits on a body that is already dead earn nothing;
// corpse   - a very strong player lifts a body, it follows him, and a throw
//            knocks over a member in its way;
// police   - an officer shot target fights back at him, the gun sits in the
//            fist (no hand skin inside the gun), and after the target dies the
//            officer holsters (reverse draw) and walks back to his car;
// carryRevive - Larry revives a body the player carries: it leaves his hands
//            and stands up right in front of him;
// revive   - a body revived by Larry is left alone by everyone.
[InitializeOnLoad]
public static class GymChaosBatch1022Verifier
{
    private const string RequestedKey = "GymChaos.Batch1022VerificationRequested";
    private static readonly string[] Names = { "hank", "ronnie", "mats", "xp", "corpse", "carryRevive", "police", "revive" };

    private static double started, stageStarted;
    private static int lastFrame, scenario, step;
    private static bool finished;
    private static int resultCode;
    private static PlayerMovement player;
    private static GymVisitorDirector director;
    private static EnemyFighter ronnie, a, b, officer;
    private static Bounds floor;
    private static Vector3 lastPos;
    private static float maxJump;
    private static int xpBefore;
    private static float healthBefore;
    private static float worstGrip;
    private static int gripSamples;
    private static string summary = "", details = "none";

    static GymChaosBatch1022Verifier()
    {
        if (SessionState.GetBool(RequestedKey, false)) Hook();
    }

    [MenuItem("Tools/GymChaos/Run Batch 2026-10-22 Verification")]
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
        started = stageStarted = EditorApplication.timeSinceStartup;
        lastFrame = -1; scenario = 0; step = 0; finished = false; resultCode = 1;
        GymChaosVerifierExit.Record(resultCode);
        player = null; director = null; ronnie = a = b = officer = null;
        summary = ""; details = "none";
    }

    private static void PlayModeChanged(PlayModeStateChange change)
    {
        if (change == PlayModeStateChange.EnteredPlayMode) ResetState();
        if (change != PlayModeStateChange.EnteredEditMode) return;
        EditorApplication.update -= Tick;
        EditorApplication.playModeStateChanged -= PlayModeChanged;
        SessionState.EraseBool(RequestedKey);
        GymChaosVerifierSave.Restore();
        if (Application.isBatchMode) GymChaosVerifierExit.Exit(resultCode);
    }

    private static double Elapsed => EditorApplication.timeSinceStartup - stageStarted;
    private static void Step(int next) { step = next; stageStarted = EditorApplication.timeSinceStartup; }

    private static void Pass(string line)
    {
        summary += (summary.Length > 0 ? " " : "") + line;
        Debug.Log($"GYMCHAOS_BATCH1022_SCENARIO_OK {Names[scenario]} {line}");
        scenario++;
        Step(0);
        if (scenario >= Names.Length)
        {
            Debug.Log($"GYMCHAOS_BATCH1022_OK {summary}");
            Finish(0);
        }
    }

    private static void Tick()
    {
        if (finished || !EditorApplication.isPlaying) return;
        try
        {
            if (EditorApplication.timeSinceStartup - started > 420d)
                throw new TimeoutException($"timed out: {details}");
            if (Time.frameCount == lastFrame) return;
            lastFrame = Time.frameCount;
            if (player != null) player.RestoreHealth(player.MaxHealth);
            if (player == null) { Setup(); return; }
            switch (scenario)
            {
                case 0: Hank(); break;
                case 1: Ronnie(); break;
                case 2: Mats(); break;
                case 3: Xp(); break;
                case 4: Corpse(); break;
                case 5: CarryRevive(); break;
                case 6: Police(); break;
                case 7: Revive(); break;
            }
        }
        catch (Exception exception)
        {
            Debug.LogError($"GYMCHAOS_BATCH1022_FAILED scenario={Names[Mathf.Min(scenario, Names.Length - 1)]} step={step} {exception.Message} last=[{details}]");
            Finish(1);
        }
    }

    private static void Setup()
    {
        if (Time.frameCount < 3 || EditorApplication.timeSinceStartup - started < 5d) return;
        PlayerMovement p = UnityEngine.Object.FindAnyObjectByType<PlayerMovement>();
        director = UnityEngine.Object.FindAnyObjectByType<GymVisitorDirector>();
        Renderer floorRenderer = GameObject.Find("Rubber Floor")?.GetComponent<Renderer>();
        if (p == null || director == null || floorRenderer == null || GymHomelessAgent.Instance == null ||
            !GymHomelessAgent.Instance.IsReady)
        {
            if (EditorApplication.timeSinceStartup - started > 60d) throw new InvalidOperationException("actors missing");
            return;
        }
        floor = floorRenderer.bounds;
        director.SuspendVisitorSimulationForVerification();
        foreach (EnemyFighter f in UnityEngine.Object.FindObjectsByType<EnemyFighter>(FindObjectsSortMode.InstanceID))
        {
            if (f == null || f.IsDead) continue;
            if (f.Identity == BodybuilderIdentity.Ronnie) ronnie = f;
            else if (!f.IsPolice && !f.IsPassive && f.Identity != BodybuilderIdentity.Goku && f.GetComponent<GymVisitorAgent>() != null)
            {
                if (a == null) a = f; else if (b == null) b = f;
            }
        }
        if (ronnie == null || a == null || b == null) throw new InvalidOperationException("need Ronnie and two members");
        player = p;
        Step(0);
    }

    private static void MovePlayer(Vector3 position, Vector3 lookAt)
    {
        player.ResetMovementForVerification();
        CharacterController controller = player.GetComponent<CharacterController>();
        bool was = controller != null && controller.enabled;
        if (was) controller.enabled = false;
        player.transform.position = position;
        Vector3 look = Vector3.ProjectOnPlane(lookAt - position, Vector3.up);
        if (look.sqrMagnitude > 0.01f) player.transform.rotation = Quaternion.LookRotation(look);
        player.ResetMovementForVerification();
        if (was) controller.enabled = true;
        player.LookAtForVerification(lookAt);
    }

    private static Vector3 OnFloor(float x, float z) => new Vector3(x, floor.max.y, z);

    // ---------------------------------------------------------------- hank
    private static void Hank()
    {
        if (hankRef == null) hankRef = GymHomelessAgent.Instance;
        GymHomelessAgent hank = hankRef;
        GymTimeOfDay time = GymTimeOfDay.Instance;
        switch (step)
        {
            case 0:
                time?.SetTimeForVerification(0.95f);
                hank.SleepInTentForVerification();
                Step(1);
                break;
            case 1:
                if (Elapsed < 1.5d) return;
                CheckHankPunch("sleeping");
                time?.SetTimeForVerification(hank.LeaveBench01 - 0.03f);
                hank.SitOnBenchForVerification();
                Step(2);
                break;
            case 2:
                if (Elapsed < 1.5d) return;
                CheckHankPunch("sitting");
                // A real throw at him on the bench.
                PickupItem item = FindThrowable();
                Bounds volume = hank.HitVolumeBoundsForVerification;
                Vector3 start = volume.center + hank.transform.forward * 1.6f + Vector3.up * 0.3f;
                Rigidbody body = item.GetComponent<Rigidbody>();
                body.isKinematic = false;
                body.position = start;
                item.transform.position = start;
                Physics.SyncTransforms();
                item.Throw((volume.center - start).normalized * 12f, null, 0f, false);
                Step(3);
                break;
            case 3:
                if (Elapsed < 0.6d && thrownItem != null)
                    Debug.Log($"GYMCHAOS_BATCH1022_BALL t={Elapsed:F2} pos={thrownItem.transform.position} vel={thrownItem.GetComponent<Rigidbody>().linearVelocity} thrown={thrownItem.WasThrownRecently} volume={hank.HitVolumeBoundsForVerification.center}");
                details = $"hankDead={hank.IsDead} state={hank.State} item={(thrownItem != null ? thrownItem.name + "@" + thrownItem.transform.position : "none")} volume={hank.HitVolumeBoundsForVerification}";
                if (!hank.IsDead)
                {
                    if (Elapsed > 3d) throw new InvalidOperationException("throw did not knock Hank out on the bench");
                    return;
                }
                ScreenSpaceCharacterLabel label = hank.GetComponent<ScreenSpaceCharacterLabel>();
                if (label == null || label.StandaloneHealthFraction != 0f)
                    throw new InvalidOperationException("no emptied health bar after Hank's death");
                Pass("punchSleeping=ok punchSitting=ok throwKnockout=ok healthBar=0");
                break;
        }
    }

    private static GymHomelessAgent hankRef;

    private static void CheckHankPunch(string pose)
    {
        GymHomelessAgent hank = hankRef;
        Bounds volume = hank.HitVolumeBoundsForVerification;
        if (volume.size == Vector3.zero) throw new InvalidOperationException("Hank has no hit volume");
        Vector3 target = volume.center;
        foreach (Vector3 dir in new[] { Vector3.forward, Vector3.back, Vector3.left, Vector3.right })
        {
            Vector3 origin = target + dir * 1.1f + Vector3.up * 0.4f;
            if (player.ResolvePunchHomelessForVerification(origin, target - origin) == hank)
            {
                Debug.Log($"GYMCHAOS_BATCH1022_HANK_PUNCH pose={pose} state={hank.State} from={dir}");
                return;
            }
        }
        throw new InvalidOperationException($"Hank cannot be punched while {pose} (state={hank.State})");
    }

    private static PickupItem thrownItem;

    private static PickupItem FindThrowable()
    {
        foreach (PickupItem item in UnityEngine.Object.FindObjectsByType<PickupItem>(FindObjectsSortMode.InstanceID))
        {
            if (item != null && !item.IsHeld && item.IsThrowableWeapon && item.GetComponent<Rigidbody>() != null &&
                (item.ItemType == WeightType.Ball || item.ItemType == WeightType.Plate5 || item.ItemType == WeightType.Plate10) &&
                item.transform.parent == null || (item != null && item.transform.parent != null &&
                item.transform.parent.GetComponentInParent<PickupItem>() == null && item.GetComponentInParent<GymMountedWeightMarker>() == null &&
                (item.ItemType == WeightType.Ball || item.ItemType == WeightType.Plate5) && !item.IsHeld && item.IsThrowableWeapon))
            {
                thrownItem = item;
                return item;
            }
        }
        throw new InvalidOperationException("no throwable item");
    }

    // -------------------------------------------------------------- ronnie
    private static void Ronnie()
    {
        switch (step)
        {
            case 0:
                if (!GymBackRoomBuilder.TryGetRoomBounds(out Bounds room)) throw new InvalidOperationException("no locker room");
                ronnie.ResetAggressionForVerification();
                ronnie.SetVisitorSpawnPose(new Vector3(room.center.x + 2f, floor.max.y, room.center.z), Quaternion.identity);
                MovePlayer(OnFloor(floor.max.x - 2f, floor.max.z - 2f), floor.center);
                lastPos = ronnie.transform.position;
                maxJump = 0f;
                Step(1);
                break;
            case 1:
                Vector3 now = ronnie.transform.position;
                if (Elapsed > 0.3d) maxJump = Mathf.Max(maxJump, Vector3.ProjectOnPlane(now - lastPos, Vector3.up).magnitude);
                lastPos = now;
                bool onFloor = now.x > floor.min.x && now.x < floor.max.x && now.z > floor.min.z && now.z < floor.max.z;
                details = $"ronnie={now} maxJump={maxJump:F2} onFloor={onFloor} stop={ronnie.ChaseStopReasonForVerification}";
                if (maxJump > 0.6f) throw new InvalidOperationException($"Ronnie jumped {maxJump:F2} m in one frame");
                if (onFloor && Elapsed > 1d)
                {
                    Pass($"walkedBack={Elapsed:F1}s maxStep={maxJump:F2}m");
                    return;
                }
                if (Elapsed > 25d) throw new InvalidOperationException("Ronnie did not walk back to the main floor");
                break;
        }
    }

    // ---------------------------------------------------------------- mats
    private static readonly (Vector3 from, Vector3 dir, float goal, string name)[] matRuns =
    {
        (new Vector3(152.6f, 0f, 5.5f), Vector3.right, 158.2f, "calisthenics"),
        (new Vector3(148.5f, 0f, 1.6f), Vector3.forward, 5.4f, "cardio-lunges"),
    };
    private static int matRun;

    private static void Mats()
    {
        var run = matRuns[matRun];
        switch (step)
        {
            case 0:
                a.ResetAggressionForVerification();
                a.GetComponent<GymVisitorAgent>().CancelForCombat();
                a.SetVisitorSpawnPose(OnFloor(run.from.x, run.from.z), Quaternion.LookRotation(run.dir));
                a.verificationForcedRoamDirection = run.dir;
                Step(1);
                break;
            case 1:
                Vector3 p = a.transform.position;
                float progress = run.dir == Vector3.right ? p.x : p.z;
                details = $"{run.name} member={a.Identity} at={p}";
                if (progress >= run.goal)
                {
                    a.verificationForcedRoamDirection = Vector3.zero;
                    Debug.Log($"GYMCHAOS_BATCH1022_MAT_CROSSED mat={run.name} seconds={Elapsed:F1}");
                    matRun++;
                    if (matRun >= matRuns.Length) { Pass("calisthenics=crossed cardioLunges=crossed"); return; }
                    Step(0);
                    return;
                }
                if (Elapsed > 8d)
                {
                    a.verificationForcedRoamDirection = Vector3.zero;
                    throw new InvalidOperationException($"member stuck at the {run.name} mat edge");
                }
                break;
        }
    }

    // ------------------------------------------------------------------ xp
    private static void Xp()
    {
        GymExperienceService xp = GymExperienceService.Active;
        switch (step)
        {
            case 0:
                b.SetVisitorSpawnPose(OnFloor(floor.center.x - 3f, floor.center.z), Quaternion.identity);
                b.TakeMeleeHit(Vector3.up, 9999f, 0.1f);
                if (!b.IsDead) throw new InvalidOperationException("member did not die");
                Step(1);
                break;
            case 1:
                if (Elapsed < 0.3d) return;
                int before = xp.Experience;
                // What a shove, bar or plate hit on the body reports.
                b.TakeMeleeHit(Vector3.forward * 2f, 5f, 0.1f);
                xp.RegisterCombatHit(b);
                xp.RegisterEnemyDefeat(b);
                if (xp.Experience != before) throw new InvalidOperationException($"corpse hit earned {xp.Experience - before} XP");
                Pass("corpseXp=0");
                break;
        }
    }

    // -------------------------------------------------------------- corpse
    private static void Corpse()
    {
        switch (step)
        {
            case 0:
                if (Elapsed < 2d) return;   // let the body settle
                GymExperienceService.Active.SetStatRankForVerification(GymStat.Strength, GymExperienceService.MaxStatRank);
                Vector3 body = b.GetComponent<Rigidbody>().worldCenterOfMass;
                MovePlayer(new Vector3(body.x, player.transform.position.y, body.z) + Vector3.back * 1.4f, body);
                Step(1);
                break;
            case 1:
                if (Elapsed < 0.3d) return;
                if (!player.TryPickupCorpseForVerification()) throw new InvalidOperationException("strong player could not lift the body");
                Step(2);
                break;
            case 2:
                if (Elapsed < 1d) return;
                float distance = Vector3.Distance(b.GetComponent<Rigidbody>().worldCenterOfMass, player.playerCamera.transform.position);
                if (distance > 2.6f) throw new InvalidOperationException($"carried body is {distance:F2} m from the camera");
                a.verificationForcedRoamDirection = Vector3.zero;
                a.ResetAggressionForVerification();
                Vector3 ahead = player.transform.position + player.transform.forward * 3f;
                a.SetVisitorSpawnPose(OnFloor(ahead.x, ahead.z), Quaternion.identity);
                healthBefore = a.CurrentHealth;
                Step(3);
                break;
            case 3:
                if (Elapsed < 0.4d) return;
                player.LookAtForVerification(a.transform.position + Vector3.up * 1.1f);
                player.ThrowCarriedCorpseForVerification();
                Step(4);
                break;
            case 4:
                details = $"memberHealth={a.CurrentHealth:F0}/{healthBefore:F0}";
                if (a.CurrentHealth < healthBefore - 0.01f)
                {
                    Pass($"lift=ok carry=ok throwHit={healthBefore - a.CurrentHealth:F0}");
                    return;
                }
                if (Elapsed > 3d) throw new InvalidOperationException("thrown body did not hit the member");
                break;
        }
    }

    // --------------------------------------------------------- carryRevive
    private static void CarryRevive()
    {
        switch (step)
        {
            case 0:
                if (Elapsed < 2.5d) return;   // the thrown body lands
                Vector3 body = b.GetComponent<Rigidbody>().worldCenterOfMass;
                MovePlayer(new Vector3(body.x, player.transform.position.y, body.z) + Vector3.back * 1.4f, body);
                Step(1);
                break;
            case 1:
                if (Elapsed < 0.3d) return;
                if (!player.TryPickupCorpseForVerification())
                {
                    // Still rolling from the throw: step up to it again.
                    Vector3 rolling = b.GetComponent<Rigidbody>().worldCenterOfMass;
                    MovePlayer(new Vector3(rolling.x, player.transform.position.y, rolling.z) + Vector3.back * 1.3f, rolling);
                    if (Elapsed > 4d) throw new InvalidOperationException("could not lift the body again");
                    return;
                }
                Step(2);
                break;
            case 2:
                if (Elapsed < 0.8d) return;
                GymFixerDirector fixer = UnityEngine.Object.FindAnyObjectByType<GymFixerDirector>();
                if (fixer == null) throw new InvalidOperationException("no fixer director");
                fixer.ReviveForVerification(b);
                Step(3);
                break;
            case 3:
                Vector3 offset = Vector3.ProjectOnPlane(b.transform.position - player.transform.position, Vector3.up);
                float ahead = Vector3.Dot(offset.normalized, Vector3.ProjectOnPlane(player.transform.forward, Vector3.up).normalized);
                details = $"alive={!b.IsDead} carried={player.IsCarryingCorpse} distance={offset.magnitude:F2} ahead={ahead:F2}";
                if (!b.IsDead)
                {
                    if (player.IsCarryingCorpse || b.IsCarriedByPlayer) throw new InvalidOperationException("revived member still in the player's hands");
                    if (offset.magnitude > 2.2f || ahead < 0.5f)
                        throw new InvalidOperationException($"revived away from the carry side: {details}");
                    Pass($"besidePlayer={offset.magnitude:F2}m ahead={ahead:F2} released=ok");
                    return;
                }
                if (Elapsed > 3d) throw new InvalidOperationException("carried body was not revived");
                break;
        }
    }

    // -------------------------------------------------------------- police
    private static void Police()
    {
        switch (step)
        {
            case 0:
                // Member a kills Ronnie: the police come for member a.
                a.ResetAggressionForVerification();
                ronnie.ResetAggressionForVerification();
                Vector3 spot = OnFloor(floor.center.x + 2f, floor.center.z - 3f);
                a.SetVisitorSpawnPose(spot, Quaternion.identity);
                ronnie.SetVisitorSpawnPose(spot + Vector3.right * 1.5f, Quaternion.identity);
                MovePlayer(OnFloor(floor.min.x + 2f, floor.max.z - 2f), floor.center);
                ronnie.TakeMeleeHit(Vector3.right, 9999f, 0.1f, a);
                if (!ronnie.IsDead) throw new InvalidOperationException("Ronnie did not die");
                Step(1);
                break;
            case 1:
                officer = GymPoliceDirector.LastOfficer;
                details = $"officer={(officer != null)} dispatch={GymPoliceDirector.IsDispatchActive}";
                if (officer == null)
                {
                    if (Elapsed > 60d) throw new InvalidOperationException("no officer arrived");
                    return;
                }
                if (Elapsed < 3d) return;
                // Hurt him to half health (by member a): he draws the gun.
                officer.TakeMeleeHit(Vector3.zero, officer.CurrentHealth - officer.MaxHealth * 0.4f, 0.05f, a);
                worstGrip = 0f; gripSamples = 0;
                Step(2);
                break;
            case 2:
                GymPoliceWeapon weapon = officer.GetComponent<GymPoliceWeapon>();
                if (weapon != null && weapon.IsVisible && weapon.IsReady)
                {
                    float inside = weapon.HandSkinInsideGunForVerification(out int handVertices);
                    if (inside >= 0f)
                    {
                        worstGrip = Mathf.Max(worstGrip, inside);
                        gripSamples++;
                    }
                    details = $"gripInside={inside:F3} hand={handVertices} length={weapon.GunWorldLengthForVerification:F3} thumb={weapon.ThumbToGripForVerification:F3} grudge={a.HasGrudgeAgainstForVerification(officer)}";
                }
                if (a.HasGrudgeAgainstForVerification(officer) && gripSamples >= 20)
                {
                    if (worstGrip > 0.06f) throw new InvalidOperationException($"gun passes through the hand: {worstGrip:P0} of the hand skin inside it");
                    Debug.Log($"GYMCHAOS_BATCH1022_POLICE_GRIP worstInside={worstGrip:F3} {details}");
                    a.TakeMeleeHit(Vector3.forward, 9999f, 0.1f, officer);
                    Step(3);
                    return;
                }
                if (Elapsed > 40d) throw new InvalidOperationException("officer shots did not turn member a on him (or no gun)");
                break;
            case 3:
                GymPoliceOfficer brain = officer.GetComponent<GymPoliceOfficer>();
                GymPoliceWeapon gun = officer.GetComponent<GymPoliceWeapon>();
                details = $"holsters={brain.HolsterCountForVerification} leaving={brain.IsLeavingForVerification} returning={officer.IsReturningToPoliceCar} gunVisible={gun.IsVisible}";
                if (officer.IsReturningToPoliceCar && !gun.IsVisible)
                {
                    if (brain.HolsterCountForVerification < 1) throw new InvalidOperationException("officer left without the holster animation");
                    Pass($"shotRetaliation=ok grip={worstGrip:F3} holster=ok leaving=ok");
                    return;
                }
                if (Elapsed > 10d) throw new InvalidOperationException("officer did not holster and leave after his target died");
                break;
        }
    }

    // -------------------------------------------------------------- revive
    private static void Revive()
    {
        switch (step)
        {
            case 0:
                if (!a.ReviveByFixer(a.transform.position, Quaternion.identity))
                    throw new InvalidOperationException("revive failed");
                Step(1);
                break;
            case 1:
                foreach (EnemyFighter f in UnityEngine.Object.FindObjectsByType<EnemyFighter>(FindObjectsSortMode.InstanceID))
                {
                    if (f == null || f.IsDead || f == a) continue;
                    if (f.HasGrudgeAgainstForVerification(a) || f.CurrentTarget == a.transform)
                        throw new InvalidOperationException($"{f.Identity} went after the revived {a.Identity}");
                }
                if (a.IsAggressive) throw new InvalidOperationException("revived member is still angry");
                if (Elapsed > 5d) Pass("nobodyReangered=ok");
                break;
        }
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
