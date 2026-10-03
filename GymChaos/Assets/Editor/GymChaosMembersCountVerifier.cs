#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class GymChaosMembersCountVerifier
{
    private const string RequestedKey =
        "GymChaos.MembersCountVerificationRequested";
    private static double startedAt;
    private static bool finished;
    private static int resultCode;
    private static EnemyFighter transitionCandidate;
    private static bool boundaryTransitionsValidated;
    private static bool deadTransitionValidated;

    static GymChaosMembersCountVerifier()
    {
        if (!SessionState.GetBool(RequestedKey, false))
        {
            return;
        }
        Hook();
        EditorApplication.delayCall += ResumeAfterReload;
    }

    [MenuItem("Tools/GymChaos/Run Members Count Verification")]
    public static void Run()
    {
        finished = false;
        resultCode = 1; GymChaosVerifierExit.Record(resultCode);
        transitionCandidate = null;
        boundaryTransitionsValidated = false;
        deadTransitionValidated = false;
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

    private static void ResumeAfterReload()
    {
        if (EditorApplication.isPlaying)
        {
            startedAt = EditorApplication.timeSinceStartup;
        }
    }

    private static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            startedAt = EditorApplication.timeSinceStartup;
            return;
        }
        if (state != PlayModeStateChange.EnteredEditMode)
        {
            return;
        }
        EditorApplication.update -= Tick;
        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        SessionState.EraseBool(RequestedKey);
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

        double elapsed = EditorApplication.timeSinceStartup - startedAt;
        if (!GymOutdoorBuilder.IsBuilt ||
            !GymInteriorBuilder.TryGetMainGymBounds(out Bounds mainBounds) ||
            !GymBackRoomBuilder.TryGetRoomBounds(out Bounds lockerBounds))
        {
            if (elapsed > 45d)
            {
                Fail("runtime_rooms_not_ready");
            }
            return;
        }

        PlayerMovement player = UnityEngine.Object.FindAnyObjectByType<PlayerMovement>();
        if (player == null)
        {
            if (elapsed > 45d)
            {
                Fail("player_missing");
            }
            return;
        }

        if (transitionCandidate == null)
        {
            EnemyFighter[] candidates = UnityEngine.Object.FindObjectsByType<EnemyFighter>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int index = 0; index < candidates.Length; index++)
            {
                if (IsCountableOpponent(candidates[index]))
                {
                    transitionCandidate = candidates[index];
                    break;
                }
            }
        }

        if (transitionCandidate == null)
        {
            if (elapsed > 45d)
            {
                Fail("transition_candidate_missing");
            }
            return;
        }

        if (!boundaryTransitionsValidated)
        {
            boundaryTransitionsValidated = ValidateBoundaryTransitions(
                player, mainBounds, lockerBounds, transitionCandidate);
            if (!boundaryTransitionsValidated)
            {
                Fail("boundary_transition_mismatch");
                return;
            }
        }

        if (!deadTransitionValidated)
        {
            deadTransitionValidated = ValidateDeadTransition(
                player, mainBounds, transitionCandidate);
            if (!deadTransitionValidated)
            {
                Fail("dead_opponent_not_excluded");
                return;
            }
        }

        bool playerInside = GymMemberRoster.IsLiveMemberRoomPosition(
            player, player.transform.position);
        bool mainProbe = GymMemberRoster.IsLiveMemberRoomPosition(
            null, mainBounds.center);
        bool lockerProbe = GymMemberRoster.IsLiveMemberRoomPosition(
            null, lockerBounds.center);
        Vector3 outdoorProbe = mainBounds.max + Vector3.right * 8f;
        bool outdoorProbeExcluded = !GymMemberRoster.IsLiveMemberRoomPosition(
            null, outdoorProbe);

        int expected = playerInside ? 1 : 0;
        int liveOpponents = 0;
        int liveOutdoorOpponents = 0;
        int livePolice = 0;
        EnemyFighter[] fighters = UnityEngine.Object.FindObjectsByType<EnemyFighter>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int index = 0; index < fighters.Length; index++)
        {
            EnemyFighter fighter = fighters[index];
            if (!IsCountableOpponent(fighter))
            {
                if (fighter != null && fighter.IsPolice &&
                    fighter.isActiveAndEnabled && !fighter.IsDead)
                {
                    livePolice++;
                }
                continue;
            }

            liveOpponents++;
            if (GymMemberRoster.IsLiveMemberRoomPosition(
                null, fighter.transform.position))
            {
                expected++;
            }
            else
            {
                liveOutdoorOpponents++;
            }
        }

        int actual = GymMemberRoster.GetDisplayCount(player);
        bool policeExcluded = livePolice == 0 || actual == expected;
        bool passed = actual == expected && mainProbe && lockerProbe &&
            outdoorProbeExcluded && policeExcluded &&
            boundaryTransitionsValidated && deadTransitionValidated;
        Debug.Log(
            $"GYMCHAOS_MEMBERS_COUNT_{(passed ? "OK" : "FAIL")} " +
            $"display={actual} expected={expected} playerInside={playerInside} " +
            $"mainProbe={mainProbe} lockerProbe={lockerProbe} " +
            $"outdoorProbeExcluded={outdoorProbeExcluded} " +
            $"liveOpponents={liveOpponents} outdoorOpponents={liveOutdoorOpponents} " +
            $"livePolice={livePolice} policeExcluded={policeExcluded} " +
            $"boundaryTransitions={boundaryTransitionsValidated} " +
            $"deadExcluded={deadTransitionValidated}");
        if (passed)
        {
            Finish(0);
        }
        else
        {
            Fail("spatial_membership_mismatch");
        }
    }

    private static bool IsCountableOpponent(EnemyFighter fighter)
    {
        return fighter != null && fighter.isActiveAndEnabled &&
            fighter.gameObject.activeInHierarchy && !fighter.IsDead &&
            !fighter.IsPolice && !fighter.IsPassive &&
            fighter.CountsAsOpponent;
    }

    private static bool ValidateBoundaryTransitions(
        PlayerMovement player,
        Bounds mainBounds,
        Bounds lockerBounds,
        EnemyFighter candidate)
    {
        Vector3 mainPosition = mainBounds.center;
        Vector3 lockerPosition = lockerBounds.center;
        Vector3 outdoorPosition = new Vector3(
            mainBounds.max.x + 8f, mainBounds.center.y, mainBounds.center.z);

        SetCandidatePosition(candidate, mainPosition);
        int mainExpected = ComputeExpectedCount(player);
        int mainActual = GymMemberRoster.GetDisplayCount(player);
        bool mainIncluded = GymMemberRoster.IsLiveMemberRoomPosition(
            null, candidate.transform.position);

        SetCandidatePosition(candidate, lockerPosition);
        int lockerExpected = ComputeExpectedCount(player);
        int lockerActual = GymMemberRoster.GetDisplayCount(player);
        bool lockerIncluded = GymMemberRoster.IsLiveMemberRoomPosition(
            null, candidate.transform.position);

        SetCandidatePosition(candidate, outdoorPosition);
        int outdoorExpected = ComputeExpectedCount(player);
        int outdoorActual = GymMemberRoster.GetDisplayCount(player);
        bool outdoorExcluded = !GymMemberRoster.IsLiveMemberRoomPosition(
            null, candidate.transform.position);

        bool passed = mainIncluded && lockerIncluded && outdoorExcluded &&
            mainActual == mainExpected && lockerActual == lockerExpected &&
            outdoorActual == outdoorExpected;
        Debug.Log(
            $"GYMCHAOS_MEMBERS_BOUNDARY_" + (passed ? "OK" : "FAIL") +
            $" main={mainActual}/{mainExpected} locker={lockerActual}/{lockerExpected} " +
            $"outdoor={outdoorActual}/{outdoorExpected} " +
            $"mainIncluded={mainIncluded} lockerIncluded={lockerIncluded} " +
            $"outdoorExcluded={outdoorExcluded} candidate={candidate.Identity}");
        return passed;
    }

    private static bool ValidateDeadTransition(
        PlayerMovement player, Bounds mainBounds, EnemyFighter candidate)
    {
        SetCandidatePosition(candidate, mainBounds.center);
        candidate.TakeMeleeHit(Vector3.zero, candidate.MaxHealth + 1f, 0.02f);
        int expected = ComputeExpectedCount(player);
        int actual = GymMemberRoster.GetDisplayCount(player);
        bool passed = candidate.IsDead && actual == expected;
        Debug.Log(
            $"GYMCHAOS_MEMBERS_DEAD_EXCLUSION_" + (passed ? "OK" : "FAIL") +
            $" dead={candidate.IsDead} display={actual} expected={expected} " +
            $"candidate={candidate.Identity}");
        return passed;
    }

    private static void SetCandidatePosition(EnemyFighter candidate, Vector3 position)
    {
        Rigidbody body = candidate.GetComponent<Rigidbody>();
        if (body != null)
        {
            body.position = position;
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }
        candidate.transform.position = position;
        Physics.SyncTransforms();
    }

    private static int ComputeExpectedCount(PlayerMovement player)
    {
        int expected = player != null &&
            GymMemberRoster.IsLiveMemberRoomPosition(
                player, player.transform.position) ? 1 : 0;
        EnemyFighter[] fighters = UnityEngine.Object.FindObjectsByType<EnemyFighter>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int index = 0; index < fighters.Length; index++)
        {
            EnemyFighter fighter = fighters[index];
            if (IsCountableOpponent(fighter) &&
                GymMemberRoster.IsLiveMemberRoomPosition(
                    null, fighter.transform.position))
            {
                expected++;
            }
        }
        return expected;
    }

    private static void Finish(int code)
    {
        if (finished)
        {
            return;
        }
        finished = true;
        resultCode = code; GymChaosVerifierExit.Record(resultCode);
        SessionState.EraseBool(RequestedKey);
        EditorApplication.update -= Tick;
        EditorApplication.isPlaying = false;
    }

    private static void Fail(string reason)
    {
        Debug.LogError("GYMCHAOS_MEMBERS_COUNT_FAILED reason=" + reason);
        Finish(1);
    }
}
#endif
