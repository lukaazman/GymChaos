#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Dead eyes: the reception suit, Mark (protein store) and Hank (park) die to
/// one punch, show the red X markers over their black eye bar, and Larry's
/// fix brings them back alive (Mark at his counter) with the markers cleared.
/// Logs GYMCHAOS_DEATH_MARKER_OK.
/// </summary>
public static class GymChaosDeathMarkerVerifier
{
    private const string RequestedKey = "GymChaos.DeathMarkerVerificationRequested";
    private static double started;
    private static double killedAt;

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

    private static void Hook()
    {
        killedAt = -1d;
        fixedAt = -1d;
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
        EditorApplication.playModeStateChanged -= Changed;
        EditorApplication.playModeStateChanged += Changed;
    }

    private static void Changed(PlayModeStateChange change)
    {
        if (change == PlayModeStateChange.EnteredPlayMode) started = EditorApplication.timeSinceStartup;
        if (change != PlayModeStateChange.EnteredEditMode) return;
        EditorApplication.update -= Tick;
        GymChaosVerifierSave.Restore();
        if (Application.isBatchMode && SessionState.GetBool(RequestedKey, false)) EditorApplication.Exit(1);
    }

    private static EnemyFighter Find(BodybuilderIdentity identity)
    {
        foreach (EnemyFighter fighter in UnityEngine.Object.FindObjectsByType<EnemyFighter>(
                     FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (fighter != null && fighter.Identity == identity) return fighter;
        return null;
    }

    private static bool Marked(Component root)
    {
        FaceCensorSettings bar = root != null ? root.GetComponentInChildren<FaceCensorSettings>(true) : null;
        return bar != null && bar.HasDeathMarkers;
    }

    private static void Tick()
    {
        if (!EditorApplication.isPlaying) return;
        double elapsed = EditorApplication.timeSinceStartup - started;
        EnemyFighter suit = Find(BodybuilderIdentity.Manwithsuit1);
        EnemyFighter mark = Find(BodybuilderIdentity.Mark);
        GymHomelessAgent hank = GymHomelessAgent.Instance;
        bool ready = suit != null && mark != null && hank != null && hank.IsReady && hank.EyeBar != null;
        if (killedAt < 0d)
        {
            if (!ready && elapsed < 120d) return;
            if (elapsed < 8d) return;
            if (!ready)
            {
                Finish(1, $"actors missing suit={suit != null} mark={mark != null} hank={hank != null}");
                return;
            }
            if (!GymProteinStoreEnvironment.IsLoaded)
            {
                if (elapsed < 120d) return;
                Finish(1, "protein store not loaded");
                return;
            }
            // Behind the counter: the floor must be the first surface below,
            // not an invisible box at counter height.
            string floorIssue = CheckCounterFloor(mark);
            if (floorIssue != null)
            {
                Finish(1, floorIssue);
                return;
            }
            // One player punch: 5 damage, the same melee path as PerformPunch.
            suit.TakeMeleeHit(Vector3.forward * 4f, 5f, 0.2f);
            mark.TakeMeleeHit(Vector3.forward * 4f, 5f, 0.2f);
            hankRef = hank;
            hank.KnockOut(Vector3.forward * 6f, hank.transform.position + Vector3.up);
            killedAt = elapsed;
            return;
        }
        if (fixedAt < 0d)
        {
            // Corpses count for Larry once they settle (up to 3.5 s).
            if (elapsed - killedAt < 4.5d) return;
            if (!(Marked(suit) && Marked(mark) && hankRef != null && hankRef.EyeBar != null && hankRef.EyeBar.HasDeathMarkers))
            {
                Finish(1, $"no dead eyes suitX={Marked(suit)} markX={Marked(mark)} hankX={hankRef?.EyeBar?.HasDeathMarkers}");
                return;
            }
            GymFixerDirector director = GymFixerDirector.Instance;
            if (director == null)
            {
                Finish(1, "no fixer director");
                return;
            }
            fixedCount = director.FixAllForVerification();
            fixedAt = elapsed;
            return;
        }
        if (elapsed - fixedAt < 1.5d) return;
        float markHome = mark != null ? Vector3.Distance(mark.transform.position, mark.FixerHomePosition) : -1f;
        string revived = $"fixed={fixedCount} suitAlive={suit != null && !suit.IsDead} markAlive={mark != null && !mark.IsDead} " +
            $"markHomeError={markHome:F2} hankAlive={hankRef != null && !hankRef.IsDead} hankPos={hankRef?.transform.position} hankVel={hankRef?.GetComponent<Rigidbody>()?.linearVelocity} " +
            $"eyesCleared={!Marked(suit) && !Marked(mark) && hankRef != null && !hankRef.EyeBar.HasDeathMarkers}";
        bool revivedOk = suit != null && !suit.IsDead && mark != null && !mark.IsDead && markHome >= 0f && markHome < 0.3f &&
            hankRef != null && !hankRef.IsDead && GymHomelessAgent.Instance == hankRef &&
            !Marked(suit) && !Marked(mark) && !hankRef.EyeBar.HasDeathMarkers;
        Finish(revivedOk ? 0 : 1, revived);
    }

    private static double fixedAt = -1d;
    private static int fixedCount;


    private static GymHomelessAgent hankRef;

    private static string CheckCounterFloor(EnemyFighter mark)
    {
        Vector3 worker = GymProteinStoreEnvironment.StoreWorkerPoint;
        float worst = 0f;
        Vector3 worstAt = worker;
        for (int dx = -2; dx <= 2; dx++)
        for (int dz = -2; dz <= 2; dz++)
        {
            Vector3 probe = worker + new Vector3(dx * 0.35f, 2.5f, dz * 0.35f);
            RaycastHit[] hits = Physics.RaycastAll(probe, Vector3.down, 4f, ~0, QueryTriggerInteraction.Ignore);
            float top = float.MinValue;
            foreach (RaycastHit hit in hits)
            {
                if (hit.collider.transform.IsChildOf(mark.transform)) continue;
                // The real counter is an exact MeshCollider; anything else up to
                // head height above the walkway is a stray box.
                if (hit.collider is MeshCollider || hit.point.y > worker.y + 1.6f) continue;
                top = Mathf.Max(top, hit.point.y);
            }
            if (top != float.MinValue && top - worker.y > worst)
            {
                worst = top - worker.y;
                worstAt = probe;
            }
        }
        Debug.Log($"GYMCHAOS_COUNTER_FLOOR worker={worker} maxRise={worst:F2} at={worstAt}");
        return worst > 0.15f ? $"invisible platform behind counter rise={worst:F2} at={worstAt}" : null;
    }

    private static void Finish(int code, string detail)
    {
        if (code == 0) Debug.Log("GYMCHAOS_DEATH_MARKER_OK " + detail);
        else Debug.LogError("GYMCHAOS_DEATH_MARKER_FAILED " + detail);
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
}
#endif
