using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

[InitializeOnLoad]
public static class GymChaosProteinStoreVerifier
{
    private const string RequestedKey = "GymChaos.ProteinStoreVerificationRequested";
    private const string OriginalSaveKey =
        "GymChaos.ProteinStoreVerificationOriginalSave";
    private const string OriginalSavePresentKey =
        "GymChaos.ProteinStoreVerificationOriginalSavePresent";
    private const string ProgressionSaveKey = "GymChaos.Progression.v1";
    private static double startedAt;
    private static bool initialized;
    private static bool storeReached;
    private static bool returnedToGym;
    private static bool modelContract;
    private static bool fenceContract;
    private static bool dialogueContract;
    private static bool dialogueTargetFound;
    private static bool dialogueOpened;
    private static bool dialogueSetupCompleted;
    private static bool obstaclePathBlocked;
    private static bool obstacleDetourObserved;
    private static GameObject routeObstacle;
    private static Vector3 obstacleStart;
    private static Vector3 obstacleDirection;
    private static float obstacleOffset;
    private static float maximumObstacleLateralOffset;
    private static bool completed;
    private static bool verificationFailed;
    private static double routeStartedAt;
    private static EnemyFighter visitor;
    private static GymVisitorAgent visitorAgent;

    static GymChaosProteinStoreVerifier()
    {
        if (!SessionState.GetBool(RequestedKey, false)) return;
        Hook();
        EditorApplication.delayCall += ResumeAfterReload;
    }

    public static void Run()
    {
        string originalSave = PlayerPrefs.GetString(ProgressionSaveKey, string.Empty);
        GymChaosVerifierPrefs.SetBool(OriginalSavePresentKey,
            PlayerPrefs.HasKey(ProgressionSaveKey));
        GymChaosVerifierPrefs.SetString(OriginalSaveKey, originalSave);
        // The negative-reputation ultimate intentionally makes neutral
        // visitors auto-aggressive. Store-route verification needs the same
        // neutral baseline as the independent behavior verifier.
        PlayerPrefs.DeleteKey(ProgressionSaveKey);
        PlayerPrefs.Save();
        ResetState();
        SessionState.SetBool(RequestedKey, true);
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

    private static void ResumeAfterReload()
    {
        if (EditorApplication.isPlaying) StartTimer();
    }

    private static void StartTimer()
    {
        startedAt = EditorApplication.timeSinceStartup;
        Time.timeScale = 1f;
        AudioListener.pause = true;
    }

    private static void ResetState()
    {
        startedAt = 0d;
        initialized = false;
        storeReached = false;
        returnedToGym = false;
        modelContract = false;
        fenceContract = false;
        dialogueContract = false;
        dialogueTargetFound = false;
        dialogueOpened = false;
        dialogueSetupCompleted = false;
        obstaclePathBlocked = false;
        obstacleDetourObserved = false;
        obstacleStart = Vector3.zero;
        obstacleDirection = Vector3.zero;
        obstacleOffset = 0f;
        maximumObstacleLateralOffset = 0f;
        ClearRouteObstacleImmediate();
        completed = false;
        verificationFailed = false;
        routeStartedAt = 0d;
        storeRouteReachedAt = -1d;
        visitor = null;
        visitorAgent = null;
    }

    private static void PlayModeChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            StartTimer();
            return;
        }
        if (state != PlayModeStateChange.EnteredEditMode) return;
        EditorApplication.update -= Tick;
        EditorApplication.playModeStateChanged -= PlayModeChanged;
        ClearRouteObstacleImmediate();
        RestoreOriginalProgressionSave();
        bool wasRequested = SessionState.GetBool(RequestedKey, false);
        SessionState.EraseBool(RequestedKey);
        if (Application.isBatchMode && wasRequested)
            EditorApplication.Exit(completed && !verificationFailed ? 0 : 1);
    }

    private static void Tick()
    {
        if (!EditorApplication.isPlaying) return;
        Time.timeScale = 1f;
        AudioListener.pause = true;
        if (startedAt <= 0d) StartTimer();
        double elapsed = EditorApplication.timeSinceStartup - startedAt;
        try
        {
            GymVisitorDirector director = UnityEngine.Object.FindAnyObjectByType<GymVisitorDirector>();
            if (director == null || !GymOutdoorBuilder.IsBuilt ||
                !GymProteinStoreEnvironment.IsBuilt || elapsed < 2d) return;

            if (!initialized)
            {
                if (!GymProteinStoreEnvironment.IsLoaded ||
                    GymProteinStoreEnvironment.RuntimeRoot == null) return;
                modelContract = ValidateStoreModel(GymProteinStoreEnvironment.RuntimeRoot);
                if (!modelContract) throw new InvalidOperationException(
                    "Runtime shop GLB failed dual-fridge, drinks, transparency or lighting checks.");
                fenceContract = GymProteinStoreEnvironment.HasConnectedEntryFenceContract(
                    out string fenceDetails);
                if (!fenceContract) throw new InvalidOperationException(
                    "Protein.com entry fence contract failed: " + fenceDetails);
                Debug.Log("GYMCHAOS_PROTEIN_STORE_FENCE_OK " + fenceDetails);

                if (!dialogueSetupCompleted)
                {
                    EnemyFighter mark = FindMark();
                    PlayerMovement player = UnityEngine.Object.FindAnyObjectByType<PlayerMovement>();
                    GymDialogueDirector dialogue = GymDialogueDirector.Active;
                    if (mark == null || player == null || dialogue == null ||
                        !dialogue.HasAuthoredOpeningForVerification(mark, "protein.com"))
                        throw new InvalidOperationException("Mark's protein.com dialogue opening is missing.");
                    dialogueContract = true;
                    dialogueTargetFound = CanTargetMark(dialogue, mark, player);
                    if (!dialogueTargetFound)
                    {
                        if (elapsed < 30d) return;
                        throw new InvalidOperationException(
                            "Mark was not a reachable nearby dialogue target in the shop.");
                    }

                    // The test player's default spawn is inside the doorway route
                    // at world origin. Move and freeze it after dialogue reachability
                    // is proven so the route is judged against shop/gym geometry,
                    // rather than the test harness standing in the doorway.
                    CharacterController playerController =
                        player.GetComponent<CharacterController>();
                    if (playerController != null) playerController.enabled = false;
                    player.enabled = false;
                    player.transform.position += new Vector3(1000f, 0f, 1000f);
                    Physics.SyncTransforms();
                    Debug.Log(
                        $"GYMCHAOS_STORE_TEST_PLAYER_DOORWAY_CLEAR position={player.transform.position}");
                    if (!director.PrepareProteinStoreVisitForVerification())
                        throw new InvalidOperationException(
                            "No eligible visitor could be prepared for the Protein.com route.");
                    dialogueSetupCompleted = true;
                }

                // The first roster can be temporarily occupied by the locker
                // cohort or by a vehicle arrival. Let the normal roster
                // settle briefly before freezing it for the deterministic
                // store-route check; otherwise the verifier can fail before
                // the next eligible non-locker member becomes available.
                if (!director.BeginProteinStoreVisitForVerification(out visitor))
                {
                    if (elapsed < 30d)
                        return;
                    throw new InvalidOperationException(
                        "No non-locker member could start a Protein.com visit " +
                        $"after the roster settle window: eligible=" +
                        $"{director.LockerEligibleMemberCountForVerification} " +
                        $"lockerSelected={director.LockerCohortSelectedForVerification}.");
                }
                director.PauseVisitorScheduleForVerification();
                visitorAgent = visitor.GetComponent<GymVisitorAgent>();
                if (visitorAgent == null) throw new InvalidOperationException("Store visitor agent is missing.");
                initialized = true;
                routeStartedAt = EditorApplication.timeSinceStartup;
                Debug.Log(
                    $"GYMCHAOS_STORE_ROUTE_TEST_STARTED enemy={visitor.Identity} " +
                    $"rareChance={director.ProteinStoreVisitChanceForVerification:F2}");
            }

            if (visitor == null || visitorAgent == null)
                throw new InvalidOperationException("Store visitor was lost during the route.");
            if (visitorAgent.State == GymVisitorAgent.VisitorState.VisitingProteinStore)
            {
                // The placement window starts on the outdoor store route. It
                // used to start at the request, so under parallel regression
                // load the walk out of the gym alone could use up the 35 s.
                if (storeRouteReachedAt < 0d)
                    storeRouteReachedAt = EditorApplication.timeSinceStartup;
                if (!obstaclePathBlocked)
                    TryPlaceRouteObstacle();
                if (routeObstacle != null)
                    ObserveRouteObstacleDetour();
            }
            if (!obstaclePathBlocked &&
                ((storeRouteReachedAt > 0d && EditorApplication.timeSinceStartup - storeRouteReachedAt > 35d) ||
                 (routeStartedAt > 0d && EditorApplication.timeSinceStartup - routeStartedAt > 120d)))
                throw new TimeoutException(
                    "Could not place a collision-check obstacle on a clear store route segment: " +
                    $"state={visitorAgent.State} onRouteFor=" +
                    $"{(storeRouteReachedAt > 0d ? EditorApplication.timeSinceStartup - storeRouteReachedAt : -1d):F1}s.");
            storeReached |= visitorAgent.State == GymVisitorAgent.VisitorState.ProteinStoreDwell ||
                visitorAgent.State == GymVisitorAgent.VisitorState.ReturningFromProteinStore;
            if (storeReached && visitorAgent.HasEnteredGym && visitorAgent.IsInsideGym &&
                !visitorAgent.IsStoreVisitActive &&
                visitorAgent.State == GymVisitorAgent.VisitorState.FreeRoaming)
            {
                returnedToGym = true;
                if (!obstacleDetourObserved)
                    throw new InvalidOperationException("Store visitor did not pass the route obstacle with a visible detour.");
                completed = true;
                Debug.Log(
                    $"GYMCHAOS_PROTEIN_STORE_FULL_CONTRACT_OK rareChance=" +
                    $"{GymVisitorDirector.Instance.ProteinStoreVisitChanceForVerification:F2} " +
                    $"storeReached={storeReached} returnedToGym={returnedToGym} " +
                    $"fridgePanels={GymProteinStoreEnvironment.TransparentFridgePanelCount} " +
                    $"fenceContract={fenceContract} " +
                    $"energyDrinks={energyDrinkCount} coldDrinks={coldDrinkCount} " +
                    $"runtimeLights={GymProteinStoreEnvironment.RuntimeInteriorLightCount} " +
                    $"markDialogue={dialogueContract} markTarget={dialogueTargetFound} " +
                    $"dialogueOpened={dialogueOpened} " +
                    $"obstacleDirectBlocked={obstaclePathBlocked} detour={obstacleDetourObserved} " +
                    $"maxLateral={maximumObstacleLateralOffset:0.00}m");
                EditorApplication.isPlaying = false;
                return;
            }
            if (routeStartedAt > 0d &&
                EditorApplication.timeSinceStartup - routeStartedAt > 150d)
            {
                throw new TimeoutException(
                    $"Protein.com visitor route timed out: reached={storeReached} " +
                    $"returned={returnedToGym} state={visitorAgent.State} " +
                    $"position={visitor.VisitorPhysicsPosition} " +
                    $"target={visitorAgent.TravelTargetForVerification} " +
                    $"blocker={visitor.LastVisitorRouteBlocker}.");
            }
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            verificationFailed = true;
            // Keep the request flag until EnteredEditMode so batchmode can
            // return a failing exit code instead of hanging after a failed
            // verification assertion.
            EditorApplication.isPlaying = false;
        }
    }

    private static void RestoreOriginalProgressionSave()
    {
        bool hadOriginalSave = GymChaosVerifierPrefs.GetBool(OriginalSavePresentKey, false);
        string originalSave = GymChaosVerifierPrefs.GetString(OriginalSaveKey, string.Empty);
        if (hadOriginalSave)
            PlayerPrefs.SetString(ProgressionSaveKey, originalSave);
        else
            PlayerPrefs.DeleteKey(ProgressionSaveKey);
        PlayerPrefs.Save();
        GymChaosVerifierPrefs.DeleteKey(OriginalSaveKey);
        GymChaosVerifierPrefs.DeleteKey(OriginalSavePresentKey);
    }

    private static int energyDrinkCount;
    private static int coldDrinkCount;

    private static double storeRouteReachedAt = -1d;

    private static void TryPlaceRouteObstacle()
    {
        Vector3 start = visitor.VisitorPhysicsPosition;
        Vector3 delta = Vector3.ProjectOnPlane(
            visitorAgent.TravelTargetForVerification - start, Vector3.up);
        float distance = delta.magnitude;
        if (distance < 4f)
            return;
        Vector3 direction = delta / distance;
        Vector3 lateral = Vector3.Cross(Vector3.up, direction).normalized;
        bool leftDetourClear =
            visitor.IsVisitorExternalPathClearFrom(start, lateral, 1.55f);
        bool rightDetourClear =
            visitor.IsVisitorExternalPathClearFrom(start, -lateral, 1.55f);
        if ((!leftDetourClear && !rightDetourClear) ||
            !visitor.IsVisitorExternalPathClearFrom(start, direction, 2.8f))
        {
            // Keep the synthetic crate inside an open route segment so the
            // verifier measures obstacle avoidance rather than a corridor
            // narrower than the visitor capsule.
            return;
        }

        obstacleStart = start;
        obstacleDirection = direction;
        obstacleOffset = 1.85f;
        Vector3 center = start + direction * obstacleOffset + Vector3.up * 1.05f;
        routeObstacle = GameObject.CreatePrimitive(PrimitiveType.Cube);
        routeObstacle.name = "Protein Store Verification Route Blocker";
        routeObstacle.transform.SetPositionAndRotation(
            center, Quaternion.LookRotation(direction, Vector3.up));
        routeObstacle.transform.localScale = new Vector3(1.05f, 2.1f, 0.8f);
        Physics.SyncTransforms();
        obstaclePathBlocked = !visitor.IsVisitorExternalPathClearFrom(
            start, direction, 2.8f);
        if (!obstaclePathBlocked)
        {
            ClearRouteObstacleImmediate();
            throw new InvalidOperationException(
                "Verification crate did not block the visitor's direct route.");
        }
        Debug.Log(
            $"GYMCHAOS_STORE_ROUTE_OBSTACLE_PLACED directBlocked=True " +
            $"start={start} center={center} target={visitorAgent.TravelTargetForVerification}");
    }

    private static void ObserveRouteObstacleDetour()
    {
        Vector3 offset = Vector3.ProjectOnPlane(
            visitor.VisitorPhysicsPosition - obstacleStart, Vector3.up);
        float progress = Vector3.Dot(offset, obstacleDirection);
        Vector3 lateral = offset - obstacleDirection * progress;
        if (progress >= 0f && progress <= obstacleOffset + 1.75f)
            maximumObstacleLateralOffset = Mathf.Max(
                maximumObstacleLateralOffset, lateral.magnitude);

        if (progress < obstacleOffset + 1f)
            return;
        if (maximumObstacleLateralOffset < 0.58f)
            throw new InvalidOperationException(
                $"Blocked store route did not detour around the crate: " +
                $"progress={progress:F2} lateral={maximumObstacleLateralOffset:F2}.");

        obstacleDetourObserved = true;
        Debug.Log(
            $"GYMCHAOS_STORE_ROUTE_OBSTACLE_CLEARED directBlocked=True " +
            $"detourLateral={maximumObstacleLateralOffset:F2} progress={progress:F2}");
        ClearRouteObstacle();
    }

    private static void ClearRouteObstacle()
    {
        if (routeObstacle != null)
            UnityEngine.Object.Destroy(routeObstacle);
        routeObstacle = null;
    }

    private static void ClearRouteObstacleImmediate()
    {
        if (routeObstacle != null)
            UnityEngine.Object.DestroyImmediate(routeObstacle);
        routeObstacle = null;
    }
    private static bool ValidateStoreModel(Transform root)
    {
        energyDrinkCount = 0;
        coldDrinkCount = 0;
        bool energyGlass = false;
        bool coldGlass = false;
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        for (int index = 0; index < renderers.Length; index++)
        {
            Renderer renderer = renderers[index];
            if (renderer == null || renderer.sharedMaterial == null) continue;
            string name = renderer.name;
            bool transparent = IsTransparent(renderer.sharedMaterial);
            if (name.Contains("EnergyCooler_FrontRight_Glass")) energyGlass = transparent;
            if (name.Contains("ColdFridge_Extra_Glass")) coldGlass = transparent;
            if (name.StartsWith("EnergyDrinks_Cooler"))
                energyDrinkCount++;
            if (name.Contains("ColdFridge_RearLeft") &&
                (name.Contains("Can") || name.Contains("Rtd"))) coldDrinkCount++;
        }
        return energyGlass && coldGlass && energyDrinkCount >= 18 &&
            coldDrinkCount >= 10 &&
            GymProteinStoreEnvironment.TransparentFridgePanelCount >= 2 &&
            GymProteinStoreEnvironment.RuntimeInteriorLightCount == 2;
    }

    private static bool IsTransparent(Material material)
    {
        return material != null &&
            ((material.HasProperty("_Surface") && material.GetFloat("_Surface") > 0.5f) ||
             (material.renderQueue >= (int)RenderQueue.Transparent &&
              material.HasProperty("_BaseColor") && material.GetColor("_BaseColor").a < 0.99f));
    }

    private static EnemyFighter FindMark()
    {
        EnemyFighter[] fighters = UnityEngine.Object.FindObjectsByType<EnemyFighter>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int index = 0; index < fighters.Length; index++)
        {
            EnemyFighter fighter = fighters[index];
            if (fighter != null && fighter.Identity == BodybuilderIdentity.Mark &&
                fighter.isActiveAndEnabled && !fighter.IsDead) return fighter;
        }
        return null;
    }

    private static string DescribeHits(PlayerMovement player, EnemyFighter mark)
    {
        Vector3 origin = player.playerCamera != null
            ? player.playerCamera.transform.position
            : player.transform.position + Vector3.up * 1.6f;
        Vector3 target = mark.transform.position + Vector3.up * 1.6f;
        RaycastHit[] hits = Physics.RaycastAll(origin, (target - origin).normalized,
            Vector3.Distance(origin, target), ~0, QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        var names = new System.Text.StringBuilder();
        for (int i = 0; i < hits.Length && i < 8; i++)
            names.Append(hits[i].collider.name).Append('@').Append(hits[i].distance.ToString("F2")).Append(';');
        return names.ToString();
    }

    private static bool CanTargetMark(
        GymDialogueDirector dialogue, EnemyFighter mark, PlayerMovement player)
    {
        Vector3 originalPosition = player.transform.position;
        Quaternion originalRotation = player.transform.rotation;
        try
        {
            foreach (float distance in new[] { 3.8f, 4.8f, 5.4f, 5.7f })
            {
                Vector3 acrossCounter = mark.transform.position + mark.transform.forward * distance;
                acrossCounter.y = originalPosition.y;
                player.transform.SetPositionAndRotation(acrossCounter,
                    Quaternion.LookRotation(mark.transform.position - acrossCounter, Vector3.up));
                Physics.SyncTransforms();
                bool found = dialogue.FindNearbyTalkTarget(player) == mark;
                if (found != (distance <= 5.5f))
                    throw new InvalidOperationException(
                        $"Mark counter range failed at {distance:F2} m: found={found} " +
                        $"player={acrossCounter} mark={mark.transform.position} " +
                        $"markForward={mark.transform.forward} hits={DescribeHits(player, mark)}.");
            }
            Debug.Log("GYMCHAOS_MARK_COUNTER_RANGE_OK positive=3.8,4.8,5.4 negative=5.7");
        }
        finally
        {
            player.transform.SetPositionAndRotation(originalPosition, originalRotation);
            Physics.SyncTransforms();
        }
        Vector3[] directions =
        {
            Vector3.left, Vector3.right, Vector3.forward, Vector3.back,
            (Vector3.left + Vector3.forward).normalized,
            (Vector3.left + Vector3.back).normalized,
            (Vector3.right + Vector3.forward).normalized,
            (Vector3.right + Vector3.back).normalized
        };
        float[] distances = { 1.25f, 1.8f, 2.35f };
        for (int distanceIndex = 0; distanceIndex < distances.Length; distanceIndex++)
        {
            for (int directionIndex = 0; directionIndex < directions.Length; directionIndex++)
            {
                Vector3 candidate = mark.transform.position +
                    directions[directionIndex] * distances[distanceIndex];
                candidate.y = originalPosition.y;
                player.transform.SetPositionAndRotation(
                    candidate, Quaternion.LookRotation(
                        Vector3.ProjectOnPlane(mark.transform.position - candidate, Vector3.up),
                        Vector3.up));
                Physics.SyncTransforms();
                if (dialogue.FindNearbyTalkTarget(player) == mark)
                {
                    dialogueOpened = GymDialogueDirector.TryStartNearby(player, true) &&
                        GymDialogueDirector.IsDialogueActive && dialogue.Target == mark &&
                        dialogue.CurrentNode != null &&
                        dialogue.CurrentNode.text.IndexOf(
                            "protein.com", System.StringComparison.OrdinalIgnoreCase) >= 0;
                    if (dialogueOpened)
                    {
                        Debug.Log("GYMCHAOS_MARK_SHOP_DIALOGUE_OPEN_OK speaker=Mark topic=protein.com");
                    }
                    if (GymDialogueDirector.IsDialogueActive)
                    {
                        GymDialogueDirector.TickActiveInput(player, false, true);
                    }
                    player.transform.SetPositionAndRotation(originalPosition, originalRotation);
                    Physics.SyncTransforms();
                    return dialogueOpened;
                }
            }
        }
        player.transform.SetPositionAndRotation(originalPosition, originalRotation);
        Physics.SyncTransforms();
        return false;
    }
}
