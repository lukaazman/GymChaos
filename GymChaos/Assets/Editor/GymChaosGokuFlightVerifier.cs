#if UNITY_EDITOR
using System;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Measures Goku's visible body while the long-range flight chase is active.
// The flight must stay low enough to read as a chase (body slightly above the
// player's head, not near the ceiling) and the visible scan must stay enabled
// and inside the chasing player's camera frustum.
[InitializeOnLoad]
public static class GymChaosGokuFlightVerifier
{
    private const string RequestedKey = "GymChaos.GokuFlightVerificationRequested";
    private const string OriginalSaveKey = "GymChaos.GokuFlightVerificationOriginalSave";
    private const string OriginalSavePresentKey =
        "GymChaos.GokuFlightVerificationOriginalSavePresent";
    private const string ProgressionSaveKey = "GymChaos.Progression.v1";
    private const float MaxBodyTopAboveFloor = 2.6f;
    private const float MinBodyBottomAboveFloor = 0.35f;
    private const float MaxBodyBottomAboveFloor = 1.9f;

    private static double started;
    private static double stageStarted;
    private static int lastFrame;
    private static int stage;
    private static int flyingSamples;
    private static bool finished;
    private static int resultCode;
    private static EnemyFighter goku;
    private static PlayerMovement player;
    private static float groundY;
    private static float floorTopY;
    private static Bounds floorArea;
    private static Vector3 lastSamplePosition;
    private static Vector3 gokuStartPosition;
    private static GameObject obstacle;
    private static bool obstacleSawFlight;
    private static float maxVerticalExtent;
    private static int tailFirstSamples;
    private static float worstTop;
    private static float worstBottomLow;
    private static float worstBottomHigh;
    private static int invisibleSamples;
    private static string lastDetails;

    static GymChaosGokuFlightVerifier()
    {
        if (SessionState.GetBool(RequestedKey, false))
        {
            Hook();
        }
    }

    [MenuItem("Tools/GymChaos/Run Goku Flight Verification")]
    public static void Run()
    {
        SessionState.SetBool(RequestedKey, true);
        // A negative-reputation save auto-aggros every visitor, which starts
        // Goku's chase before the measured setup. Run from a neutral save.
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
        flyingSamples = 0;
        finished = false;
        resultCode = 1; GymChaosVerifierExit.Record(resultCode);
        goku = null;
        player = null;
        worstTop = float.NegativeInfinity;
        worstBottomLow = float.PositiveInfinity;
        worstBottomHigh = float.NegativeInfinity;
        invisibleSamples = 0;
        maxVerticalExtent = 0f;
        tailFirstSamples = 0;
        lastDetails = "none";
    }

    private static void PlayModeChanged(PlayModeStateChange change)
    {
        if (change == PlayModeStateChange.EnteredPlayMode)
        {
            // Entering play mode reloads the domain, which zeroes the statics
            // that Run() prepared.
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
                throw new InvalidOperationException(
                    $"Goku flight verification timed out at stage {stage}: {lastDetails}");
            }
            if (Time.frameCount == lastFrame)
            {
                return;
            }
            lastFrame = Time.frameCount;

            if (stage == 0)
            {
                // Let the bootstrap spawn everyone and settle the scans.
                if (Time.frameCount < 3)
                {
                    return;
                }
                player = UnityEngine.Object.FindAnyObjectByType<PlayerMovement>();
                EnemyFighter[] fighters = UnityEngine.Object.FindObjectsByType<EnemyFighter>(
                    FindObjectsSortMode.None);
                for (int i = 0; i < fighters.Length; i++)
                {
                    if (fighters[i] != null && fighters[i].Identity == BodybuilderIdentity.Goku)
                    {
                        goku = fighters[i];
                        break;
                    }
                }
                if (player == null || goku == null)
                {
                    if (EditorApplication.timeSinceStartup - started > 40d)
                    {
                        throw new InvalidOperationException(
                            $"Missing actors: player={player != null} goku={goku != null}.");
                    }
                    return;
                }
                Renderer floor = GameObject.Find("Rubber Floor")?.GetComponent<Renderer>();
                if (floor == null)
                {
                    throw new InvalidOperationException("Rubber Floor is missing.");
                }
                // Visitors may still be arriving or leaving (Goku rides his
                // cloud high above the city). Freeze the schedule and put Goku
                // on the gym floor so the chase starts from a real ground pose.
                GymVisitorDirector director =
                    UnityEngine.Object.FindAnyObjectByType<GymVisitorDirector>();
                if (director == null)
                {
                    return;
                }
                // Goku's cloud arrival finishes through a vehicle callback that
                // re-stages him outside. Let it complete before placing him.
                foreach (GymVisitorVehicle vehicle in
                    UnityEngine.Object.FindObjectsByType<GymVisitorVehicle>(FindObjectsSortMode.None))
                {
                    if (vehicle != null && vehicle.IsCloud &&
                        (vehicle.IsDriving || vehicle.HasMountedRider))
                    {
                        if (EditorApplication.timeSinceStartup - started > 90d)
                        {
                            throw new InvalidOperationException("Goku's cloud arrival never finished.");
                        }
                        return;
                    }
                }
                director.PauseVisitorScheduleForVerification();
                director.SuspendVisitorSimulationForVerification();
                Bounds floorBounds = floor.bounds;
                Vector3 gokuStart = new Vector3(
                    floorBounds.min.x + 2f, floorBounds.max.y, floorBounds.center.z);
                Vector3 target = new Vector3(
                    Mathf.Min(gokuStart.x + 11f, floorBounds.max.x - 1.5f),
                    player.transform.position.y, floorBounds.center.z);
                floorTopY = floorBounds.max.y;
                floorArea = floorBounds;
                gokuStartPosition = gokuStart;
                goku.SetVisitorSpawnPose(
                    gokuStart, Quaternion.LookRotation(Vector3.right, Vector3.up));
                MovePlayer(target);
                stage = 10;
                stageStarted = EditorApplication.timeSinceStartup;
                return;
            }

            if (stage == 10)
            {
                if (Time.frameCount % 10 == 0)
                {
                    Debug.Log(
                        $"GYMCHAOS_GOKU_FLIGHT_SETTLE pos={goku.transform.position} " +
                        $"grounded={goku.IsGokuGrounded} flying={goku.IsFlying} " +
                        $"active={goku.gameObject.activeInHierarchy}");
                }
                if (EditorApplication.timeSinceStartup - stageStarted < 0.5d)
                {
                    return;
                }
                float driftFromStart = Vector3.ProjectOnPlane(
                    goku.transform.position - gokuStartPosition, Vector3.up).magnitude;
                if (Mathf.Abs(goku.transform.position.y - floorTopY) > 0.6f || driftFromStart > 3f)
                {
                    // A cloud arrival callback that was already in flight can
                    // remount Goku after the schedule pause. Dismount again and
                    // wait for a stable floor pose.
                    if (EditorApplication.timeSinceStartup - started > 60d)
                    {
                        throw new InvalidOperationException(
                            $"Goku did not settle on the gym floor: y={goku.transform.position.y:F2} " +
                            $"floor={floorTopY:F2}.");
                    }
                    UnityEngine.Object.FindAnyObjectByType<GymVisitorDirector>()?
                        .SuspendVisitorSimulationForVerification();
                        goku.EndVisitorVehicleRide(
                        gokuStartPosition, Quaternion.LookRotation(Vector3.right, Vector3.up));
                    Rigidbody gokuBody = goku.GetComponent<Rigidbody>();
                    Vector3 floorPosition = new Vector3(
                        gokuStartPosition.x, floorTopY, gokuStartPosition.z);
                    goku.transform.position = floorPosition;
                    if (gokuBody != null)
                    {
                        gokuBody.position = floorPosition;
                    }
                    Physics.SyncTransforms();
                    stageStarted = EditorApplication.timeSinceStartup;
                    return;
                }
                groundY = floorTopY;
                goku.SetAggressiveForVerification(player);
                Debug.Log(
                    $"GYMCHAOS_GOKU_FLIGHT_HEIGHT_SETUP goku={goku.transform.position} " +
                    $"player={player.transform.position} groundY={groundY:F2}");
                stage = 1;
                stageStarted = EditorApplication.timeSinceStartup;
                return;
            }

            if (stage == 1)
            {
                // Keep the player far away so the flight lasts long enough to sample.
                Vector3 away = Vector3.ProjectOnPlane(
                    player.transform.position - goku.transform.position, Vector3.up);
                if (away.magnitude < 7f && away.sqrMagnitude > 0.01f)
                {
                    MovePlayer(goku.transform.position + away.normalized * 9f);
                }
                AimPlayerCameraAtGoku();

                if (goku.IsFlying)
                {
                    Sample();
                    flyingSamples++;
                    if (flyingSamples >= 60)
                    {
                        Evaluate();
                        return;
                    }
                }
                if (EditorApplication.timeSinceStartup - stageStarted > 25d)
                {
                    throw new InvalidOperationException(
                        $"Goku did not sustain flight: samples={flyingSamples} " +
                        $"flying={goku.IsFlying} {lastDetails}");
                }
                return;
            }

            if (stage == 2)
            {
                // Obstacle phase: while Goku is still chasing in flight, move
                // the player 10 m away along the roomiest floor axis and drop
                // a wall halfway. Flight must route around it instead of
                // hovering against the wall until the player moves.
                Vector3 from = goku.transform.position;
                Vector3[] axes = { Vector3.right, Vector3.left, Vector3.forward, Vector3.back };
                Vector3 axis = Vector3.right;
                float room = -1f;
                foreach (Vector3 candidate in axes)
                {
                    float free = candidate.x > 0f ? floorArea.max.x - from.x
                        : candidate.x < 0f ? from.x - floorArea.min.x
                        : candidate.z > 0f ? floorArea.max.z - from.z
                        : from.z - floorArea.min.z;
                    if (free > room)
                    {
                        room = free;
                        axis = candidate;
                    }
                }
                float reach = Mathf.Min(10f, room - 1.2f);
                Vector3 target = from + axis * reach;
                MovePlayer(target);
                obstacle = GameObject.CreatePrimitive(PrimitiveType.Cube);
                obstacle.name = "Goku Flight Verification Wall";
                obstacle.transform.position = new Vector3(from.x, floorTopY + 1.6f, from.z) +
                    axis * (reach * 0.45f);
                obstacle.transform.rotation = Quaternion.LookRotation(axis, Vector3.up);
                obstacle.transform.localScale = new Vector3(3.0f, 3.2f, 0.3f);
                Physics.SyncTransforms();
                Debug.Log(
                    $"GYMCHAOS_GOKU_FLIGHT_OBSTACLE_SETUP goku={from} player={player.transform.position} " +
                    $"wall={obstacle.transform.position} reach={reach:F2}");
                obstacleSawFlight = false;
                stage = 4;
                stageStarted = EditorApplication.timeSinceStartup;
                return;
            }

            if (stage == 4)
            {
                obstacleSawFlight |= goku.IsFlying;
                float distance = Vector3.ProjectOnPlane(
                    player.transform.position - goku.transform.position, Vector3.up).magnitude;
                if (Time.frameCount % 20 == 0)
                {
                    Debug.Log(
                        $"GYMCHAOS_GOKU_FLIGHT_OBSTACLE_SAMPLE t={EditorApplication.timeSinceStartup - stageStarted:F1} " +
                        $"goku={goku.transform.position} distance={distance:F2} flying={goku.IsFlying} blocker={goku.GokuFlightLastBlocker}");
                }
                if (distance < 2.6f)
                {
                    if (!obstacleSawFlight)
                    {
                        throw new InvalidOperationException(
                            "Goku reached the player without flying in the obstacle phase.");
                    }
                    Debug.Log(
                        $"GYMCHAOS_GOKU_FLIGHT_OBSTACLE_OK seconds=" +
                        $"{EditorApplication.timeSinceStartup - stageStarted:F1} distance={distance:F2}");
                    Finish(0);
                    return;
                }
                if (EditorApplication.timeSinceStartup - stageStarted > 12d)
                {
                    throw new InvalidOperationException(
                        $"GYMCHAOS_GOKU_FLIGHT_OBSTACLE_FAIL Goku stayed behind the wall: " +
                        $"goku={goku.transform.position} distance={distance:F2} flying={goku.IsFlying} blocker={goku.GokuFlightLastBlocker}");
                }
            }
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            Finish(1);
        }
    }

    private static void Sample()
    {
        Renderer body = FindBodyRenderer();
        if (body == null)
        {
            invisibleSamples++;
            lastDetails = "no enabled body renderer";
            return;
        }
        // Renderer AABBs of the rotated skinned scan are conservative boxes;
        // skinned vertex positions describe the silhouette the player sees.
        Bounds bounds = BakeWorldBounds((SkinnedMeshRenderer)body);
        float top = bounds.max.y - groundY;
        float bottom = bounds.min.y - groundY;
        worstTop = Mathf.Max(worstTop, top);
        if (bottom < worstBottomLow)
        {
            worstBottomLow = bottom;
            Debug.Log($"GYMCHAOS_GOKU_FLIGHT_LOWEST sample={flyingSamples} bottom={bottom:F2} " +
                $"top={top:F2} root={goku.transform.position.y - groundY:F2} size={bounds.size}");
        }
        worstBottomHigh = Mathf.Max(worstBottomHigh, bottom);
        // Flight is a horizontal Superman pose; a standing-height silhouette
        // means the root pitch and the authored flying clip were stacked.
        maxVerticalExtent = Mathf.Max(maxVerticalExtent, bounds.size.y);
        Transform head = FindBone((SkinnedMeshRenderer)body, "DEF-spine.005");
        Transform hips = FindBone((SkinnedMeshRenderer)body, "DEF-spine");
        // The head must lead the actual travel direction, which is not the
        // player direction while Goku detours around an obstacle.
        Vector3 moved = Vector3.ProjectOnPlane(goku.transform.position - lastSamplePosition, Vector3.up);
        bool hasPrevious = flyingSamples > 0;
        lastSamplePosition = goku.transform.position;
        float headLead = float.NaN;
        if (head != null && hips != null && hasPrevious && moved.magnitude > 0.02f)
        {
            headLead = Vector3.Dot(
                Vector3.ProjectOnPlane(head.position - hips.position, Vector3.up).normalized,
                moved.normalized);
            if (headLead <= 0.3f)
            {
                tailFirstSamples++;
            }
        }
        else if (head == null || hips == null)
        {
            tailFirstSamples++;
        }

        Camera camera = Camera.main;
        bool inFrustum = camera != null &&
            GeometryUtility.TestPlanesAABB(GeometryUtility.CalculateFrustumPlanes(camera), bounds);
        if (!inFrustum)
        {
            invisibleSamples++;
        }
        lastDetails =
            $"pos={goku.transform.position} blocker={goku.GokuFlightLastBlocker} renderer={body.name} root={goku.transform.position.y - groundY:F2} " +
            $"bottom={bottom:F2} top={top:F2} size={bounds.size} headLead={headLead:F2} " +
            $"inFrustum={inFrustum} camera={(camera != null ? camera.transform.position.ToString() : "none")}";
        if (flyingSamples % 15 == 0)
        {
            Debug.Log($"GYMCHAOS_GOKU_FLIGHT_SAMPLE {lastDetails}");
        }
        if (flyingSamples == 30 && SystemInfo.graphicsDeviceType !=
            UnityEngine.Rendering.GraphicsDeviceType.Null)
        {
            Capture(camera, "goku-flight-player.png");
            Vector3 center = bounds.center;
            Vector3 side = Vector3.Cross(Vector3.up,
                Vector3.ProjectOnPlane(player.transform.position - center, Vector3.up).normalized);
            GameObject sideCameraObject = new GameObject("Goku Flight Side Camera");
            Camera sideCamera = sideCameraObject.AddComponent<Camera>();
            sideCamera.fieldOfView = 60f;
            sideCamera.transform.position = new Vector3(
                center.x, groundY + 1.2f, center.z) + side * 6f;
            sideCamera.transform.rotation = Quaternion.LookRotation(
                new Vector3(center.x, groundY + 1.2f, center.z) - sideCamera.transform.position,
                Vector3.up);
            Capture(sideCamera, "goku-flight-side.png");
            UnityEngine.Object.Destroy(sideCameraObject);
        }
    }

    private static Transform FindBone(SkinnedMeshRenderer renderer, string boneName)
    {
        Transform[] bones = renderer.bones;
        for (int i = 0; i < bones.Length; i++)
        {
            if (bones[i] != null && bones[i].name == boneName)
            {
                return bones[i];
            }
        }
        return null;
    }

    private static Bounds BakeWorldBounds(SkinnedMeshRenderer renderer)
    {
        Mesh baked = new Mesh();
        renderer.BakeMesh(baked, true);
        Vector3[] vertices = baked.vertices;
        Matrix4x4 matrix = renderer.transform.localToWorldMatrix;
        Bounds world = new Bounds(matrix.MultiplyPoint3x4(vertices[0]), Vector3.zero);
        for (int i = 1; i < vertices.Length; i += 7)
        {
            world.Encapsulate(matrix.MultiplyPoint3x4(vertices[i]));
        }
        UnityEngine.Object.Destroy(baked);
        return world;
    }

    private static void Capture(Camera camera, string fileName)
    {
        if (camera == null)
        {
            return;
        }
        RenderTexture target = new RenderTexture(960, 540, 24);
        RenderTexture previousTarget = camera.targetTexture;
        RenderTexture previousActive = RenderTexture.active;
        camera.targetTexture = target;
        camera.Render();
        RenderTexture.active = target;
        Texture2D image = new Texture2D(960, 540, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0, 0, 960, 540), 0, 0);
        image.Apply();
        string directory = System.IO.Path.GetFullPath(
            System.IO.Path.Combine(Application.dataPath, "..", "..", "Logs", "agent"));
        System.IO.Directory.CreateDirectory(directory);
        System.IO.File.WriteAllBytes(
            System.IO.Path.Combine(directory, fileName), image.EncodeToPNG());
        camera.targetTexture = previousTarget;
        RenderTexture.active = previousActive;
        UnityEngine.Object.Destroy(image);
        target.Release();
        UnityEngine.Object.Destroy(target);
        Debug.Log($"GYMCHAOS_GOKU_FLIGHT_CAPTURE file={fileName}");
    }

    private static Renderer FindBodyRenderer()
    {
        SkinnedMeshRenderer[] renderers = goku.GetComponentsInChildren<SkinnedMeshRenderer>(false);
        SkinnedMeshRenderer best = null;
        int bestVertices = 0;
        for (int i = 0; i < renderers.Length; i++)
        {
            SkinnedMeshRenderer candidate = renderers[i];
            if (candidate == null || !candidate.enabled || candidate.sharedMesh == null ||
                candidate.GetComponentInParent<GokuAura>() != null && candidate.name.Contains("Aura"))
            {
                continue;
            }
            if (candidate.sharedMesh.vertexCount > bestVertices)
            {
                best = candidate;
                bestVertices = candidate.sharedMesh.vertexCount;
            }
        }
        return best;
    }

    private static void Evaluate()
    {
        StringBuilder summary = new StringBuilder();
        summary.Append($"samples={flyingSamples} invisible={invisibleSamples} tailFirst={tailFirstSamples} ");
        summary.Append($"maxTop={worstTop:F2} minBottom={worstBottomLow:F2} maxBottom={worstBottomHigh:F2} ");
        summary.Append($"maxVerticalExtent={maxVerticalExtent:F2} last=[{lastDetails}]");

        if (invisibleSamples > flyingSamples / 5 ||
            tailFirstSamples > flyingSamples / 5 ||
            maxVerticalExtent > 1.3f ||
            worstTop > MaxBodyTopAboveFloor ||
            worstBottomLow < MinBodyBottomAboveFloor ||
            worstBottomHigh > MaxBodyBottomAboveFloor)
        {
            throw new InvalidOperationException($"GYMCHAOS_GOKU_FLIGHT_HEIGHT_FAIL {summary}");
        }
        Debug.Log($"GYMCHAOS_GOKU_FLIGHT_HEIGHT_OK {summary}");
        stage = 2;
    }

    private static void AimPlayerCameraAtGoku()
    {
        Camera camera = Camera.main;
        if (camera == null)
        {
            return;
        }
        Vector3 look = goku.transform.position + Vector3.up * 1f - camera.transform.position;
        if (look.sqrMagnitude > 0.01f)
        {
            camera.transform.rotation = Quaternion.LookRotation(look, Vector3.up);
        }
    }

    private static void MovePlayer(Vector3 position)
    {
        player.ResetMovementForVerification();
        CharacterController controller = player.GetComponent<CharacterController>();
        bool wasEnabled = controller != null && controller.enabled;
        if (wasEnabled)
        {
            controller.enabled = false;
        }
        position.y = player.transform.position.y;
        player.transform.position = position;
        player.ResetMovementForVerification();
        if (wasEnabled)
        {
            controller.enabled = true;
        }
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
