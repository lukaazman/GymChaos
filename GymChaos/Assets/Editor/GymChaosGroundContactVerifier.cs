#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Contact checks for props and people that must rest on a surface:
//  * a visitor exercising on a treadmill keeps its lowest foot on the belt
//    deck under it (not floating above the moving surface);
//  * locker-room bags rest on the bench seat slats under them.
// Surfaces are measured from real mesh vertices under the object, because
// renderer bounds include stray geometry (the bench GLB has a raised spike on
// one end that sits well above the seat).
[InitializeOnLoad]
public static class GymChaosGroundContactVerifier
{
    private const string RequestedKey = "GymChaos.GroundContactVerificationRequested";
    private const string OriginalSaveKey = "GymChaos.GroundContactVerificationOriginalSave";
    private const string OriginalSavePresentKey =
        "GymChaos.GroundContactVerificationOriginalSavePresent";
    private const string ProgressionSaveKey = "GymChaos.Progression.v1";
    private const float MaxContactGap = 0.035f;

    private static double started;
    private static double stageStarted;
    private static int lastFrame;
    private static int stage;
    private static bool finished;
    private static int resultCode;
    private static EnemyFighter runner;
    private static GymExerciseStation treadmill;
    private static readonly List<float> footGaps = new List<float>();
    private static string treadmillSummary;
    private static bool waitingForPlant;
    private static int feetOverObstacle;
    private static float floorBaselineOffset;
    private static string lastObstacle;
    private static bool plantCaptured;
    private static float plantedThreshold;

    static GymChaosGroundContactVerifier()
    {
        if (SessionState.GetBool(RequestedKey, false))
        {
            Hook();
        }
    }

    [MenuItem("Tools/GymChaos/Run Ground Contact Verification")]
    public static void Run()
    {
        SessionState.SetBool(RequestedKey, true);
        GymChaosVerifierPrefs.SetBool(OriginalSavePresentKey, PlayerPrefs.HasKey(ProgressionSaveKey));
        GymChaosVerifierPrefs.SetString(OriginalSaveKey, PlayerPrefs.GetString(ProgressionSaveKey, string.Empty));
        PlayerPrefs.DeleteKey(ProgressionSaveKey);
        PlayerPrefs.Save();
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
        resultCode = 1; GymChaosVerifierExit.Record(resultCode);
        runner = null;
        treadmill = null;
        footGaps.Clear();
        treadmillSummary = "not measured";
        strideSoleGap = float.PositiveInfinity;
        floorSoleGap = float.PositiveInfinity;
        floorSoleSamples = 0;
        waitingForPlant = false;
        feetOverObstacle = 0;
        lastObstacle = "none";
        plantCaptured = false;
    }

    private static void PlayModeChanged(PlayModeStateChange change)
    {
        if (change == PlayModeStateChange.EnteredPlayMode)
        {
            // The domain reload on entering play mode zeroes the statics.
            ResetState();
            Time.timeScale = 1f;
        }
        if (change != PlayModeStateChange.EnteredEditMode)
        {
            return;
        }
        EditorApplication.update -= Tick;
        EditorApplication.playModeStateChanged -= PlayModeChanged;
        SessionState.EraseBool(RequestedKey);
        RestoreOriginalProgressionSave();
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
            if (EditorApplication.timeSinceStartup - started > 150d)
            {
                throw new InvalidOperationException($"Ground contact verification timed out at stage {stage}.");
            }
            if (Time.frameCount == lastFrame)
            {
                return;
            }
            lastFrame = Time.frameCount;

            if (stage == 0)
            {
                if (EditorApplication.timeSinceStartup - started < 3d)
                {
                    return;
                }
                UnityEngine.Object.FindAnyObjectByType<GymVisitorDirector>()?
                    .SuspendVisitorSimulationForVerification();
                VerifyTreadmillStandPoints();
                EnemyFighter[] fighters = UnityEngine.Object.FindObjectsByType<EnemyFighter>(
                    FindObjectsSortMode.None);
                foreach (EnemyFighter fighter in fighters)
                {
                    if (fighter == null || fighter.IsPolice || fighter.IsDead ||
                        fighter.IsAggressive || fighter.Identity == BodybuilderIdentity.Goku ||
                        !fighter.gameObject.activeInHierarchy)
                    {
                        continue;
                    }
                    GymExerciseStation station = GymExerciseStation.FindClosestTreadmill(
                        fighter.transform.position, 100f);
                    ExternalRiggedCharacterVisual floorVisual = fighter.GetComponent<ExternalRiggedCharacterVisual>();
                    SkinnedMeshRenderer floorBody = FindBodyRenderer(fighter.gameObject);
                    if (floorBody != null)
                    {
                        Debug.Log($"GYMCHAOS_GROUND_CONTACT_FLOOR_BASELINE fighter={fighter.Identity} " +
                            $"root={fighter.transform.position.y:F3} lowest={LowestVertex(floorBody):F3} sole={FootSoleY(fighter.gameObject):F3} " +
                            $"contactY={(floorVisual != null ? floorVisual.GroundContactY : float.NaN):F3} " +
                            $"modelRootY={(floorVisual != null && floorVisual.RuntimeModelRoot != null ? floorVisual.RuntimeModelRoot.position.y : float.NaN):F3} ");
                        Capture(fighter.transform.position - fighter.transform.forward * 2.2f +
                            fighter.transform.right * 1.1f + Vector3.up * 0.75f,
                            fighter.transform.position + Vector3.up * 0.25f, "ground-contact-floor.png");
                        // Eye exactly at floor level, looking level: the floor is the
                        // horizontal centre line, so planted soles must sit on it.
                        Vector3 floorEye = fighter.transform.position + Vector3.up * 0.005f;
                        Capture(floorEye + fighter.transform.right * 2f, floorEye, "ground-contact-floor-level.png");
                    }
                    if (station != null && fighter.BeginTreadmillForVerification(station, 2.2f))
                    {
                        runner = fighter;
                        floorRootY = fighter.transform.position.y;
                        floorBaselineOffset = floorBody != null ? LowestVertex(floorBody) - fighter.transform.position.y : 0f;
                        treadmill = station;
                        break;
                    }
                }
                if (runner == null)
                {
                    if (EditorApplication.timeSinceStartup - started > 40d)
                    {
                        throw new InvalidOperationException("No visitor could start a treadmill session.");
                    }
                    return;
                }
                Debug.Log($"GYMCHAOS_GROUND_CONTACT_TREADMILL_START runner={runner.Identity} " +
                    $"station={treadmill.EquipmentName}");
                if (treadmill.EquipmentRoot != null)
                {
                    foreach (MeshFilter part in treadmill.EquipmentRoot.GetComponentsInChildren<MeshFilter>(true))
                    {
                        Renderer partRenderer = part.GetComponent<Renderer>();
                        SortedDictionary<float, int> heights = new SortedDictionary<float, int>();
                        if (part.sharedMesh != null && part.sharedMesh.isReadable)
                        {
                            foreach (Vector3 vertex in part.sharedMesh.vertices)
                            {
                                float y = Mathf.Round(part.transform.TransformPoint(vertex).y * 100f) / 100f;
                                heights[y] = heights.TryGetValue(y, out int count) ? count + 1 : 1;
                            }
                        }
                        string histogram = "";
                        foreach (KeyValuePair<float, int> pair in heights) histogram += $"{pair.Key:F2}x{pair.Value} ";
                        Debug.Log($"GYMCHAOS_GROUND_CONTACT_TREADMILL_PART name={part.name} " +
                            $"bounds={(partRenderer != null ? partRenderer.bounds.ToString() : "none")} heights={histogram}");
                    }
                }
                stage = 1;
                stageStarted = EditorApplication.timeSinceStartup;
                return;
            }

            if (stage == 1)
            {
                // Let the approach and belt entry finish before sampling.
                if (!runner.IsOnTreadmill)
                {
                    throw new InvalidOperationException("Treadmill session ended before sampling.");
                }
                float onBelt = Vector3.ProjectOnPlane(
                    runner.transform.position - treadmill.EnemyPosition, Vector3.up).magnitude;
                // While the runner still walks across the gym floor, record its
                // lowest sole over the strides (planted foot = floor contact).
                if (onBelt > 0.6f && runner.transform.position.y < floorRootY + 0.02f)
                {
                    floorSoleGap = Mathf.Min(floorSoleGap,
                        LowestVertex(FindBodyRenderer(runner.gameObject)) - runner.transform.position.y);
                    floorSoleSamples++;
                }
                if (onBelt > 0.05f || EditorApplication.timeSinceStartup - stageStarted < 1.5d)
                {
                    if (EditorApplication.timeSinceStartup - stageStarted > 40d)
                    {
                        throw new InvalidOperationException(
                            $"Visitor never reached the belt: offset={onBelt:F2}.");
                    }
                    return;
                }
                SampleFoot();
                if (footGaps.Count == 90) { plantedThreshold = Mathf.Min(footGaps.ToArray()) + 0.01f; waitingForPlant = true; }
                if (plantCaptured)
                {
                    EvaluateTreadmill();
                    stage = 3;
                    stageStarted = EditorApplication.timeSinceStartup;
                }
                return;
            }

            if (stage == 3)
            {
                // After a few strides the run must hold its planted foot at
                // the same height above the belt as the walk does on the floor.
                if (!runner.IsOnTreadmill)
                    throw new InvalidOperationException("Treadmill session ended before the stride check.");
                if (EditorApplication.timeSinceStartup - stageStarted > 3.5d)
                {
                    float beltTop = treadmill.TreadmillBeltRendererForVerification.bounds.max.y;
                    strideSoleGap = Mathf.Min(strideSoleGap,
                        LowestVertex(FindBodyRenderer(runner.gameObject)) - beltTop);
                }
                if (EditorApplication.timeSinceStartup - stageStarted < 6d) return;
                if (floorSoleSamples >= 10 && (floorSoleGap > 0.04f || floorSoleGap < -0.05f))
                    throw new InvalidOperationException(
                        $"GYMCHAOS_GROUND_CONTACT_FAIL {runner.Identity} soles float on the floor: " +
                        $"minGap={floorSoleGap:F3} samples={floorSoleSamples}");
                Debug.Log($"GYMCHAOS_GROUND_CONTACT_FLOOR_SOLE_OK runner={runner.Identity} " +
                    $"minGap={floorSoleGap:F3} samples={floorSoleSamples}");
                string summary = $"runner={runner.Identity} strideSoleGap={strideSoleGap:F3}";
                if (strideSoleGap > 0.05f || strideSoleGap < -0.05f)
                    throw new InvalidOperationException("GYMCHAOS_GROUND_CONTACT_FAIL treadmill stride floats: " + summary);
                Vector3 beltEye = runner.transform.position;
                beltEye.y = treadmill.TreadmillBeltRendererForVerification.bounds.max.y + 0.005f;
                Capture(beltEye - runner.transform.right * 2f, beltEye, "ground-contact-treadmill-level.png");
                Debug.Log("GYMCHAOS_GROUND_CONTACT_TREADMILL_STRIDE_OK " + summary);
                stage = 2;
                return;
            }

            if (stage == 2)
            {
                VerifyBags();
                Finish(0);
            }
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            Finish(1);
        }
    }

    private static void SampleFoot()
    {
        SkinnedMeshRenderer body = FindBodyRenderer(runner.gameObject);
        Renderer belt = treadmill.TreadmillBeltRendererForVerification;
        if (body == null || belt == null)
        {
            throw new InvalidOperationException("Treadmill body or belt renderer is missing.");
        }
        Mesh baked = new Mesh();
        body.BakeMesh(baked, true);
        Vector3[] vertices = baked.vertices;
        Matrix4x4 matrix = body.transform.localToWorldMatrix;
        float lowest = float.PositiveInfinity;
        Bounds feet = new Bounds();
        bool hasFeet = false;
        for (int i = 0; i < vertices.Length; i++)
        {
            Vector3 world = matrix.MultiplyPoint3x4(vertices[i]);
            lowest = Mathf.Min(lowest, world.y);
        }
        for (int i = 0; i < vertices.Length; i++)
        {
            Vector3 world = matrix.MultiplyPoint3x4(vertices[i]);
            if (world.y > lowest + 0.12f) continue;
            if (!hasFeet) { feet = new Bounds(world, Vector3.zero); hasFeet = true; }
            else feet.Encapsulate(world);
        }
        UnityEngine.Object.Destroy(baked);
        float surface = SurfaceTopUnder(belt.gameObject, feet, 0.995f);
        float gap = lowest - surface;
        footGaps.Add(gap);
        // The planted feet must be over the open belt, not over the motor
        // hood or any other part that sits on the deck.
        if (treadmill.EquipmentRoot != null)
        {
            foreach (Renderer part in treadmill.EquipmentRoot.GetComponentsInChildren<Renderer>(false))
            {
                if (part == belt || !IsDeckObstacle(part.bounds, belt.bounds)) continue;
                Bounds planted = new Bounds(feet.center, new Vector3(feet.size.x, 1f, feet.size.z));
                Bounds obstacle = new Bounds(part.bounds.center, new Vector3(part.bounds.size.x, 1f, part.bounds.size.z));
                if (planted.Intersects(obstacle))
                {
                    feetOverObstacle++;
                    lastObstacle = part.name;
                }
            }
        }
        if (waitingForPlant && !plantCaptured && gap <= plantedThreshold)
        {
            plantCaptured = true;
            ExternalRiggedCharacterVisual visual = runner.GetComponent<ExternalRiggedCharacterVisual>();
            Debug.Log($"GYMCHAOS_GROUND_CONTACT_TREADMILL_VISUAL contactY={(visual != null ? visual.GroundContactY : float.NaN):F3} " +
                $"modelRootY={(visual != null && visual.RuntimeModelRoot != null ? visual.RuntimeModelRoot.position.y : float.NaN):F3} " +
                $"modelRootLocalY={(visual != null && visual.RuntimeModelRoot != null ? visual.RuntimeModelRoot.localPosition.y : float.NaN):F3}");
            Debug.Log($"GYMCHAOS_GROUND_CONTACT_TREADMILL_SAMPLE lowestFoot={lowest:F3} " +
                $"beltSurface={surface:F3} beltBoundsTop={belt.bounds.max.y:F3} " +
                $"root={runner.transform.position.y:F3} gap={gap:F3} sole={FootSoleY(runner.gameObject):F3} " +
                $"rootPos={runner.transform.position} stand={treadmill.EnemyPosition} feetCenter={feet.center} " +
                $"beltBounds={belt.bounds.center}/{belt.bounds.extents} ");
            Capture(runner.transform.position - runner.transform.forward * 2.2f + runner.transform.right * 1.1f + Vector3.up * 0.75f,
                runner.transform.position + Vector3.up * 0.25f, "ground-contact-treadmill.png");
            Vector3 feetPoint = new Vector3(runner.transform.position.x, surface + 0.25f, runner.transform.position.z);
            // Hide everything but this runner and its treadmill so the side
            // view is not blocked by the neighbouring machines.
            List<Renderer> hidden = new List<Renderer>();
            foreach (Renderer other in UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                if (!other.enabled || other.transform.IsChildOf(runner.transform) ||
                    (treadmill.EquipmentRoot != null && other.transform.IsChildOf(treadmill.EquipmentRoot)) ||
                    other.name.Contains("Floor"))
                {
                    continue;
                }
                other.enabled = false;
                hidden.Add(other);
            }
            Capture(feetPoint + runner.transform.right * 1.8f + Vector3.up * 0.1f, feetPoint, "ground-contact-treadmill-side.png");
            foreach (Renderer other in hidden) other.enabled = true;
            Vector3 eye = runner.transform.position + runner.transform.right * 2.4f - runner.transform.forward * 1.6f;
            eye.y = surface + 1.55f;
            Capture(eye, runner.transform.position + Vector3.up * 0.6f, "ground-contact-treadmill-player.png");
            Capture(feetPoint - runner.transform.right * 1.8f + Vector3.up * 0.1f, feetPoint, "ground-contact-treadmill-side2.png");
        }
    }

    // Sole height from the skeleton-following foot hitbox spheres.
    internal static float FootSoleY(GameObject fighter)
    {
        float lowest = float.PositiveInfinity;
        foreach (SphereCollider sphere in fighter.GetComponentsInChildren<SphereCollider>(true))
        {
            if (!sphere.name.EndsWith("foot hitbox")) continue;
            float radius = sphere.radius * Mathf.Max(
                Mathf.Abs(sphere.transform.lossyScale.x),
                Mathf.Abs(sphere.transform.lossyScale.y),
                Mathf.Abs(sphere.transform.lossyScale.z));
            lowest = Mathf.Min(lowest, sphere.transform.TransformPoint(sphere.center).y - radius);
        }
        return lowest;
    }

    private static float strideSoleGap = float.PositiveInfinity;
    private static float floorSoleGap = float.PositiveInfinity;
    private static int floorSoleSamples;
    private static float floorRootY = -0.15f;

    private static float LowestVertex(SkinnedMeshRenderer body)
    {
        Mesh baked = new Mesh();
        body.BakeMesh(baked, true);
        Matrix4x4 matrix = body.transform.localToWorldMatrix;
        float lowest = float.PositiveInfinity;
        foreach (Vector3 vertex in baked.vertices)
        {
            lowest = Mathf.Min(lowest, matrix.MultiplyPoint3x4(vertex).y);
        }
        UnityEngine.Object.Destroy(baked);
        return lowest;
    }

    // Every treadmill's visitor stand point must be over open black belt,
    // with room for a stride, not over the grey motor hood.
    private static void VerifyTreadmillStandPoints()
    {
        int checkedStations = 0;
        foreach (GymExerciseStation station in UnityEngine.Object.FindObjectsByType<GymExerciseStation>(
            FindObjectsSortMode.None))
        {
            Renderer belt = station != null && station.IsTreadmill
                ? station.TreadmillBeltRendererForVerification : null;
            if (belt == null || station.EquipmentRoot == null) continue;
            Vector3 stand = station.EnemyPosition;
            Bounds stride = new Bounds(stand, new Vector3(0.7f, 1f, 0.7f));
            bool onBelt = stand.x > belt.bounds.min.x && stand.x < belt.bounds.max.x &&
                stand.z > belt.bounds.min.z && stand.z < belt.bounds.max.z;
            foreach (Renderer part in station.EquipmentRoot.GetComponentsInChildren<Renderer>(true))
            {
                if (part == belt || !IsDeckObstacle(part.bounds, belt.bounds)) continue;
                Bounds obstacle = new Bounds(part.bounds.center,
                    new Vector3(part.bounds.size.x, 1f, part.bounds.size.z));
                if (!onBelt || stride.Intersects(obstacle))
                {
                    throw new InvalidOperationException(
                        $"GYMCHAOS_GROUND_CONTACT_FAIL treadmill {station.EquipmentName} stand point " +
                        $"{stand} overlaps {part.name} {part.bounds} (belt {belt.bounds}).");
                }
            }
            checkedStations++;
        }
        if (checkedStations == 0)
        {
            throw new InvalidOperationException("No treadmill stations were found.");
        }
        Debug.Log($"GYMCHAOS_GROUND_CONTACT_TREADMILL_STANDPOINTS_OK stations={checkedStations}");
    }

    private static bool IsDeckObstacle(Bounds part, Bounds belt)
    {
        const float margin = 0.02f;
        bool overlapsDeck = part.max.x > belt.min.x + margin && part.min.x < belt.max.x - margin &&
            part.max.z > belt.min.z + margin && part.min.z < belt.max.z - margin;
        return overlapsDeck && part.max.y > belt.max.y + 0.05f && part.min.y < belt.max.y + 0.05f;
    }

    private static void EvaluateTreadmill()
    {
        if (feetOverObstacle > footGaps.Count / 10)
        {
            throw new InvalidOperationException(
                $"GYMCHAOS_GROUND_CONTACT_FAIL treadmill feet stand on {lastObstacle} instead of the belt: " +
                $"samples={feetOverObstacle}/{footGaps.Count} runner={runner.Identity}");
        }
        footGaps.Sort();
        // Baked vertices of these rigs sit a constant offset above the true
        // sole. Compare against the same runner standing on the gym floor.
        float minimum = footGaps[0] - floorBaselineOffset;
        float median = footGaps[footGaps.Count / 2] - floorBaselineOffset;
        treadmillSummary = $"runner={runner.Identity} minGap={minimum:F3} medianGap={median:F3}";
        // A running gait has short flight phases, so check the planted foot
        // (minimum) tightly and the typical frame (median) loosely.
        if (minimum > 0.05f || minimum < -0.12f || median > 0.15f)
        {
            throw new InvalidOperationException(
                $"GYMCHAOS_GROUND_CONTACT_FAIL treadmill feet are not on the belt: {treadmillSummary}");
        }
        Debug.Log($"GYMCHAOS_GROUND_CONTACT_TREADMILL_OK {treadmillSummary}");
    }

    private static void VerifyBags()
    {
        GameObject leftBench = GameObject.Find("Authored wooden locker bench Left");
        GameObject rightBench = GameObject.Find("Authored wooden locker bench Right");
        if (leftBench == null || rightBench == null)
        {
            throw new InvalidOperationException("Locker benches are missing.");
        }
        int checkedBags = 0;
        float worst = 0f;
        GameObject captureBag = null;
        foreach (Transform node in UnityEngine.Object.FindObjectsByType<Transform>(
            FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (!node.name.EndsWith("color bag") && !node.name.EndsWith("black bag"))
            {
                continue;
            }
            GameObject bag = node.gameObject;
            bool wasActive = bag.activeSelf;
            bag.SetActive(true);
            Bounds bagBounds = RendererBounds(bag);
            GameObject bench = node.name.StartsWith("Left ") ? leftBench : rightBench;
            float seat = SurfaceTopUnder(bench, bagBounds, 0.995f);
            float gap = bagBounds.min.y - seat;
            worst = Mathf.Max(worst, Mathf.Abs(gap));
            Debug.Log($"GYMCHAOS_GROUND_CONTACT_BAG bag={node.name} bagBottom={bagBounds.min.y:F3} " +
                $"seatSurface={seat:F3} benchBoundsTop={RendererBounds(bench).max.y:F3} gap={gap:F3}");
            if (captureBag == null && node.name.StartsWith("Left ")) captureBag = bag;
            else bag.SetActive(wasActive);
            checkedBags++;
        }
        if (checkedBags < 4)
        {
            throw new InvalidOperationException($"Expected 4 bench bags, found {checkedBags}.");
        }
        if (captureBag != null)
        {
            Bounds b = RendererBounds(captureBag);
            Capture(b.center + new Vector3(0f, 0.35f, 0f) +
                Vector3.ProjectOnPlane(Camera.main != null ? -Camera.main.transform.forward : Vector3.back, Vector3.up).normalized * 0.01f +
                SideOf(captureBag) * 2.2f,
                b.center, "ground-contact-bag.png");
            captureBag.SetActive(false);
        }
        if (worst > MaxContactGap)
        {
            throw new InvalidOperationException(
                $"GYMCHAOS_GROUND_CONTACT_FAIL bench bag is not resting on the seat: worstGap={worst:F3}");
        }
        Debug.Log($"GYMCHAOS_GROUND_CONTACT_OK {treadmillSummary} bags={checkedBags} worstBagGap={worst:F3}");
    }

    private static Vector3 SideOf(GameObject bag)
    {
        // View the bag across the bench width so the seat edge is visible.
        GameObject bench = GameObject.Find(bag.name.StartsWith("Left ")
            ? "Authored wooden locker bench Left" : "Authored wooden locker bench Right");
        Vector3 across = bench != null ? bench.transform.forward : Vector3.forward;
        across = Vector3.ProjectOnPlane(across, Vector3.up);
        return across.sqrMagnitude > 0.01f ? across.normalized : Vector3.forward;
    }

    // Highest mesh surface directly under the footprint, trimming the top
    // fraction of vertices so an isolated spike does not define the surface.
    internal static float SurfaceTopUnder(GameObject root, Bounds footprint, float percentile)
    {
        List<float> heights = new List<float>();
        foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>(true))
        {
            Mesh mesh = filter.sharedMesh;
            if (mesh == null || !mesh.isReadable) continue;
            Matrix4x4 matrix = filter.transform.localToWorldMatrix;
            Vector3[] vertices = mesh.vertices;
            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 world = matrix.MultiplyPoint3x4(vertices[i]);
                if (world.x < footprint.min.x - 0.02f || world.x > footprint.max.x + 0.02f ||
                    world.z < footprint.min.z - 0.02f || world.z > footprint.max.z + 0.02f)
                {
                    continue;
                }
                heights.Add(world.y);
            }
        }
        Renderer surfaceRenderer = root.GetComponent<Renderer>();
        if (heights.Count < 8 && surfaceRenderer != null &&
            footprint.center.x >= surfaceRenderer.bounds.min.x &&
            footprint.center.x <= surfaceRenderer.bounds.max.x &&
            footprint.center.z >= surfaceRenderer.bounds.min.z &&
            footprint.center.z <= surfaceRenderer.bounds.max.z)
        {
            // Low-poly flat part (a treadmill belt is a thin box): only its
            // corners are vertices, so the footprint catches none. Its top
            // face is the surface.
            return surfaceRenderer.bounds.max.y;
        }
        if (heights.Count < 8)
        {
            string meshes = "";
            foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>(true))
                meshes += $"{filter.name}:readable={filter.sharedMesh != null && filter.sharedMesh.isReadable};";
            Renderer rootRenderer = root.GetComponent<Renderer>();
            throw new InvalidOperationException(
                $"No surface vertices under {root.name}: footprint={footprint} " +
                $"rendererBounds={(rootRenderer != null ? rootRenderer.bounds.ToString() : "none")} " +
                $"rendererType={(rootRenderer != null ? rootRenderer.GetType().Name : "none")} meshes={meshes}");
        }
        heights.Sort();
        return heights[Mathf.Clamp(Mathf.FloorToInt(heights.Count * percentile), 0, heights.Count - 1)];
    }

    private static Bounds RendererBounds(GameObject root)
    {
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(false);
        Bounds bounds = renderers[0].bounds;
        foreach (Renderer renderer in renderers) bounds.Encapsulate(renderer.bounds);
        return bounds;
    }

    private static SkinnedMeshRenderer FindBodyRenderer(GameObject root)
    {
        SkinnedMeshRenderer best = null;
        int bestVertices = 0;
        foreach (SkinnedMeshRenderer candidate in root.GetComponentsInChildren<SkinnedMeshRenderer>(false))
        {
            if (candidate == null || !candidate.enabled || candidate.sharedMesh == null) continue;
            if (candidate.sharedMesh.vertexCount > bestVertices)
            {
                best = candidate;
                bestVertices = candidate.sharedMesh.vertexCount;
            }
        }
        return best;
    }

    private static void Capture(Vector3 position, Vector3 target, string fileName)
    {
        if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
        {
            return;
        }
        GameObject cameraObject = new GameObject("Ground Contact Capture Camera");
        Camera camera = cameraObject.AddComponent<Camera>();
        camera.fieldOfView = 45f;
        camera.nearClipPlane = 0.05f;
        camera.transform.SetPositionAndRotation(
            position, Quaternion.LookRotation(target - position, Vector3.up));
        RenderTexture texture = new RenderTexture(900, 700, 24);
        camera.targetTexture = texture;
        camera.Render();
        RenderTexture previous = RenderTexture.active;
        RenderTexture.active = texture;
        Texture2D image = new Texture2D(900, 700, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0, 0, 900, 700), 0, 0);
        image.Apply();
        RenderTexture.active = previous;
        string directory = System.IO.Path.GetFullPath(
            System.IO.Path.Combine(Application.dataPath, "..", "..", "Logs", "agent"));
        System.IO.Directory.CreateDirectory(directory);
        System.IO.File.WriteAllBytes(System.IO.Path.Combine(directory, fileName), image.EncodeToPNG());
        camera.targetTexture = null;
        texture.Release();
        UnityEngine.Object.Destroy(texture);
        UnityEngine.Object.Destroy(image);
        UnityEngine.Object.Destroy(cameraObject);
        Debug.Log($"GYMCHAOS_GROUND_CONTACT_CAPTURE file={fileName}");
    }

    private static void RestoreOriginalProgressionSave()
    {
        if (!GymChaosVerifierPrefs.HasKey(OriginalSavePresentKey))
        {
            return;
        }
        if (GymChaosVerifierPrefs.GetBool(OriginalSavePresentKey, false))
        {
            PlayerPrefs.SetString(ProgressionSaveKey, GymChaosVerifierPrefs.GetString(OriginalSaveKey, string.Empty));
        }
        else
        {
            PlayerPrefs.DeleteKey(ProgressionSaveKey);
        }
        PlayerPrefs.Save();
        GymChaosVerifierPrefs.DeleteKey(OriginalSaveKey);
        GymChaosVerifierPrefs.DeleteKey(OriginalSavePresentKey);
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
}
#endif
