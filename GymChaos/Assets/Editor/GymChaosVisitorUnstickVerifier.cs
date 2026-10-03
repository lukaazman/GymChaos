#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Deterministic check of the visitor unstick rules used after repeated failed
/// exit reroutes: a standing member blocks the route probe until the visitor
/// takes a crowd pass through that member, and a loose prop blocks it until
/// the visitor may push loose items. Clearing the recovery restores both.
/// </summary>
[InitializeOnLoad]
public static class GymChaosVisitorUnstickVerifier
{
    private const string RequestedKey = "GymChaos.VisitorUnstickVerificationRequested";
    private const float ProbeDistance = 2.2f;
    private const float BlockerOffset = 1.2f;
    private static double startedAt;
    private static EnemyFighter autoEndVisitor;
    private static EnemyFighter autoEndMember;
    private static double autoEndStartedAt;
    private static bool finished;
    private static int resultCode;

    static GymChaosVisitorUnstickVerifier()
    {
        if (!SessionState.GetBool(RequestedKey, false))
        {
            return;
        }
        Hook();
        EditorApplication.delayCall += () =>
        {
            if (EditorApplication.isPlaying) startedAt = EditorApplication.timeSinceStartup;
        };
    }

    [MenuItem("Tools/GymChaos/Run Visitor Unstick Verification")]
    public static void Run()
    {
        finished = false;
        resultCode = 1; GymChaosVerifierExit.Record(resultCode);
        autoEndVisitor = null;
        autoEndMember = null;
        SessionState.SetBool(RequestedKey, true);
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        Hook();
        EditorApplication.isPlaying = true;
    }

    private static void Hook()
    {
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }

    private static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            startedAt = EditorApplication.timeSinceStartup;
            return;
        }
        if (state != PlayModeStateChange.EnteredEditMode) return;
        EditorApplication.update -= Tick;
        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        SessionState.EraseBool(RequestedKey);
        if (Application.isBatchMode) GymChaosVerifierExit.Exit(resultCode);
    }

    private static void Tick()
    {
        if (finished || !EditorApplication.isPlaying) return;
        double elapsed = EditorApplication.timeSinceStartup - startedAt;
        try
        {
            if (autoEndVisitor != null)
            {
                // Stage 2: the pass must end by itself once the two are apart.
                if (autoEndVisitor.VisitorCrowdPassForVerification == null)
                {
                    Require(CountCollidingPairs(autoEndVisitor, autoEndMember) > 0,
                        "the automatic end did not restore the collision pairs");
                    Debug.Log($"GYMCHAOS_VISITOR_UNSTICK_OK visitor={autoEndVisitor.Identity} " +
                        $"member={autoEndMember.Identity} autoEnd=True");
                    Finish(0);
                }
                else if (EditorApplication.timeSinceStartup - autoEndStartedAt > 5d)
                {
                    throw new InvalidOperationException("crowd pass did not end after the member moved away");
                }
                return;
            }

            if (!GymInteriorBuilder.TryGetMainGymBounds(out Bounds gym) || elapsed < 4d)
            {
                if (elapsed > 45d) throw new InvalidOperationException("Gym was not built.");
                return;
            }

            EnemyFighter visitor = null;
            EnemyFighter member = null;
            // A real visitor and an ordinary member: no police, Goku or staff.
            foreach (EnemyFighter fighter in UnityEngine.Object.FindObjectsByType<EnemyFighter>(
                         FindObjectsSortMode.None))
            {
                if (fighter == null || fighter.IsDead || fighter.IsAggressive ||
                    fighter.GetComponent<Rigidbody>() == null ||
                    fighter.GetComponent<GymVisitorAgent>() == null ||
                    fighter.Identity == BodybuilderIdentity.Goku ||
                    fighter.Identity == BodybuilderIdentity.Ronnie ||
                    fighter.Identity == BodybuilderIdentity.Manwithsuit1 ||
                    fighter.Identity == BodybuilderIdentity.Mark) continue;
                if (visitor == null) visitor = fighter;
                else if (member == null) { member = fighter; break; }
            }
            PickupItem prop = null;
            foreach (PickupItem item in UnityEngine.Object.FindObjectsByType<PickupItem>(FindObjectsSortMode.None))
            {
                Rigidbody itemBody = item != null ? item.GetComponent<Rigidbody>() : null;
                Collider itemCollider = item != null ? item.GetComponentInChildren<Collider>() : null;
                // A prop big enough to fill the probe capsule, not held by anyone.
                // Floor props from GymLooseItemSpawner (balls, foam roller, step);
                // plates and bars have their own gym-equipment route rules.
                if (itemBody != null && !itemBody.isKinematic && !item.IsHeld && itemCollider != null &&
                    item.name.StartsWith("Loose Item - ", StringComparison.Ordinal) &&
                    itemCollider.bounds.size.x >= 0.2f && itemCollider.bounds.size.y >= 0.2f &&
                    itemCollider.bounds.size.z >= 0.2f)
                { prop = item; break; }
            }
            if (visitor == null || member == null || prop == null)
            {
                if (elapsed > 45d)
                    throw new InvalidOperationException(
                        $"Scene is missing actors: visitor={visitor} member={member} prop={prop}.");
                return;
            }

            // Park the member and the prop well away, then find an open lane.
            Place(member.transform, gym.center + new Vector3(0f, 0f, -200f));
            Place(prop.transform, gym.center + new Vector3(3f, 20f, -200f));
            if (!TryFindOpenLane(visitor, gym, out Vector3 origin, out Vector3 direction))
                throw new InvalidOperationException("No open 2.2 m lane was found in the gym.");
            Vector3 blockerSpot = origin + direction * BlockerOffset;
            Place(visitor.transform, origin);

            // 1) Crowd pass through a standing member.
            Place(member.transform, blockerSpot);
            Require(!visitor.IsVisitorExternalPathClearFrom(origin, direction, ProbeDistance),
                "a member standing in the lane did not block the route probe");

            visitor.TryVisitorCrowdPassRouteBlocker();
            Require(visitor.VisitorCrowdPassForVerification == member,
                $"crowd pass did not select the member: selected={visitor.VisitorCrowdPassForVerification} " +
                $"member={member.name} aggressive={member.IsAggressive} dead={member.IsDead} " +
                $"blocker={visitor.LastVisitorRouteBlocker}");
            Require(CountCollidingPairs(visitor, member) == 0,
                $"crowd pass left {CountCollidingPairs(visitor, member)} enabled collider pairs colliding");
            Require(visitor.IsVisitorExternalPathClearFrom(origin, direction, ProbeDistance),
                "route probe still blocked by the crowd-pass member: " + visitor.LastVisitorRouteBlocker);
            visitor.ClearVisitorStuckRecovery();
            Require(CountCollidingPairs(visitor, member) > 0,
                "clearing the recovery did not restore the collision pairs");
            Require(!visitor.IsVisitorExternalPathClearFrom(origin, direction, ProbeDistance),
                "the member no longer blocks after the crowd pass was cleared");

            // 2) Pushing a loose prop. Members are ignored here (other ambient
            //    members may walk by); only the prop is under test.
            Place(member.transform, gym.center + new Vector3(0f, 0f, -200f));
            // Centre the prop at the probe capsule's mid height; nothing
            // simulates between placing it and the probes below.
            Place(prop.transform, blockerSpot + Vector3.up * 0.9f);
            Require(!visitor.IsVisitorExternalPathClearFrom(origin, direction, ProbeDistance, true),
                $"a loose prop in the lane did not block the route probe: {prop.name} blocker={visitor.LastVisitorRouteBlocker}");
            visitor.SetVisitorPushesLooseItems(true);
            Require(visitor.IsVisitorExternalPathClearFrom(origin, direction, ProbeDistance, true),
                "route probe still blocked by a pushable loose prop: " + visitor.LastVisitorRouteBlocker);
            visitor.ClearVisitorStuckRecovery();
            Require(!visitor.IsVisitorExternalPathClearFrom(origin, direction, ProbeDistance, true),
                "the loose prop no longer blocks after the recovery was cleared");

            // 3) Take the pass again, then move the member away: FixedUpdate ends it.
            Place(prop.transform, gym.center + new Vector3(3f, 20f, -200f));
            Place(member.transform, blockerSpot);
            Require(!visitor.IsVisitorExternalPathClearFrom(origin, direction, ProbeDistance),
                "the member did not block before the automatic-end check");
            visitor.TryVisitorCrowdPassRouteBlocker();
            Require(visitor.VisitorCrowdPassForVerification == member, "second crowd pass was not taken");
            Place(member.transform, origin + direction * 6f);
            autoEndVisitor = visitor;
            autoEndMember = member;
            autoEndStartedAt = EditorApplication.timeSinceStartup;
        }
        catch (Exception exception)
        {
            Debug.LogError("GYMCHAOS_VISITOR_UNSTICK_FAILED " + exception.Message);
            Finish(1);
        }
    }

    private static bool TryFindOpenLane(EnemyFighter visitor, Bounds gym, out Vector3 origin, out Vector3 direction)
    {
        float floorY = visitor.transform.position.y;
        for (int x = -3; x <= 3; x++)
        {
            for (int z = -3; z <= 3; z++)
            {
                origin = new Vector3(gym.center.x + x * 2.5f, floorY, gym.center.z + z * 2.5f);
                for (int turn = 0; turn < 4; turn++)
                {
                    direction = Quaternion.Euler(0f, turn * 90f, 0f) * Vector3.forward;
                    if (visitor.IsVisitorExternalPathClearFrom(origin, direction, ProbeDistance))
                    {
                        return true;
                    }
                }
            }
        }
        origin = Vector3.zero;
        direction = Vector3.forward;
        return false;
    }

    private static void Place(Transform target, Vector3 position)
    {
        Rigidbody body = target.GetComponent<Rigidbody>();
        if (body != null)
        {
            body.linearVelocity = Vector3.zero;
            body.position = position;
        }
        target.position = position;
        Physics.SyncTransforms();
    }

    private static int CountCollidingPairs(EnemyFighter a, EnemyFighter b)
    {
        int count = 0;
        foreach (Collider mine in a.GetComponentsInChildren<Collider>(false))
        {
            if (!mine.enabled) continue;
            foreach (Collider theirs in b.GetComponentsInChildren<Collider>(false))
            {
                if (theirs.enabled && !Physics.GetIgnoreCollision(mine, theirs)) count++;
            }
        }
        return count;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void Finish(int code)
    {
        if (finished) return;
        finished = true;
        resultCode = code; GymChaosVerifierExit.Record(resultCode);
        SessionState.EraseBool(RequestedKey);
        EditorApplication.update -= Tick;
        EditorApplication.isPlaying = false;
    }
}
#endif
