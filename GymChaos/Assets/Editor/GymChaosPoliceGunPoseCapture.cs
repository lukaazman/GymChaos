#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Graphics-only capture of the policeman's Glock during the draw and shoot
/// clips. Dispatches the real officer, freezes each clip phase with
/// Time.timeScale = 0, and writes Logs/agent/police-pose-*.png.
/// </summary>
[InitializeOnLoad]
public static class GymChaosPoliceGunPoseCapture
{
    private const string RequestedKey = "GymChaos.PoliceGunPoseCaptureRequested";
    // Every 5 % of both clips is checked; PNGs only at the key frames.
    private static readonly float[] DrawPhases = Phases();
    private static readonly float[] ShootPhases = Phases();
    private static readonly string[] PictureLabels = { "draw-35", "draw-60", "draw-80", "draw-95", "shoot-10", "shoot-40", "shoot-70" };

    private static float[] Phases()
    {
        float[] phases = new float[21];
        for (int i = 0; i < phases.Length; i++) phases[i] = Mathf.Min(0.995f, i * 0.05f);
        return phases;
    }
    private static double started;
    private static bool killRequested;
    private static bool frozen;
    private static int step;
    private static int settleFrames;
    private static int resultCode;
    private static EnemyFighter officer;
    private static GymPoliceWeapon weapon;
    private static MixamoScanRetargetAnimator animator;

    static GymChaosPoliceGunPoseCapture()
    {
        if (SessionState.GetBool(RequestedKey, false))
        {
            Hook();
        }
    }

    public static void Run()
    {
        SessionState.SetBool(RequestedKey, true);
        started = EditorApplication.timeSinceStartup;
        killRequested = false;
        frozen = false;
        step = 0;
        settleFrames = 0;
        gripFailures.Clear();
        officer = null;
        weapon = null;
        animator = null;
        resultCode = 1;
        GymChaosVerifierExit.Record(resultCode);
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        Hook();
        EditorApplication.isPlaying = true;
    }

    private static void Hook() => GymChaosVerifierHooks.Hook(Tick, PlayModeChanged);

    private static void PlayModeChanged(PlayModeStateChange change)
    {
        if (change == PlayModeStateChange.EnteredPlayMode)
        {
            // GYMCHAOS_POLICE_GRIP_BAKE=1: solve the hold afresh and, when every
            // pose passes, write Resources/PoliceGrip.json. Otherwise the run
            // checks the hold the game loads.
            GymPistolGrip.ResetForVerification(Environment.GetEnvironmentVariable("GYMCHAOS_POLICE_GRIP_BAKE") == "1");
            started = EditorApplication.timeSinceStartup;
            Time.timeScale = 3f;
            AudioListener.volume = 0f;
            // Keeps play mode ticking while the live Editor is unfocused.
            Application.runInBackground = true;
        }
        if (change != PlayModeStateChange.EnteredEditMode)
        {
            return;
        }
        EditorApplication.update -= Tick;
        EditorApplication.playModeStateChanged -= PlayModeChanged;
        SessionState.EraseBool(RequestedKey);
        if (Application.isBatchMode)
        {
            GymChaosVerifierExit.Exit(resultCode);
        }
    }

    private static void Tick()
    {
        if (!EditorApplication.isPlaying)
        {
            return;
        }
        try
        {
            if (EditorApplication.timeSinceStartup - started > 420d)
            {
                throw new InvalidOperationException(
                    $"Police gun pose capture timed out: kill={killRequested} frozen={frozen} " +
                    $"officer={GymPoliceDirector.LastOfficer != null} " +
                    $"phase={GymPoliceDirector.ActiveInstance?.DispatchPhaseForVerification} " +
                    $"weaponReady={GymPoliceDirector.LastOfficer?.GetComponent<GymPoliceWeapon>()?.IsReady} " +
                    $"time={Time.time:F1} step={step}");
            }
            if (!killRequested)
            {
                RequestDispatch();
                return;
            }
            if (!frozen)
            {
                TryFreezeOfficer();
                return;
            }
            AdvanceCapture();
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            Finish(1);
        }
    }

    private static void RequestDispatch()
    {
        PlayerMovement player = UnityEngine.Object.FindAnyObjectByType<PlayerMovement>();
        GymVisitorDirector director = UnityEngine.Object.FindAnyObjectByType<GymVisitorDirector>();
        if (player == null)
        {
            return;
        }
        director?.SuspendVisitorSimulationForVerification();
        foreach (EnemyFighter fighter in UnityEngine.Object.FindObjectsByType<EnemyFighter>(
            FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (fighter != null && fighter.Identity == BodybuilderIdentity.Ronnie && !fighter.IsDead)
            {
                fighter.TakeMeleeHit(Vector3.zero, fighter.MaxHealth + 1f, 0.02f);
                killRequested = true;
                return;
            }
        }
    }

    private static void TryFreezeOfficer()
    {
        officer = GymPoliceDirector.LastOfficer;
        if (officer == null || officer.IsDead)
        {
            return;
        }
        weapon = officer.GetComponent<GymPoliceWeapon>();
        animator = officer.GetComponent<MixamoScanRetargetAnimator>();
        if (weapon == null || animator == null || !weapon.IsReady)
        {
            return;
        }
        GymPoliceOfficer brain = officer.GetComponent<GymPoliceOfficer>();
        if (brain != null)
        {
            brain.enabled = false;
        }
        Time.timeScale = 0f;
        // Stand the player where the "player" camera renders from, so the
        // held gun aims at the viewer as in gameplay.
        PlayerMovement player = UnityEngine.Object.FindAnyObjectByType<PlayerMovement>();
        if (player != null)
        {
            Vector3 spot = officer.transform.position + officer.transform.forward * 1.5f;
            Rigidbody body = player.GetComponent<Rigidbody>();
            if (body != null)
            {
                body.position = spot;
            }
            player.transform.position = spot;
            Physics.SyncTransforms();
        }
        weapon.SetVisible(true);
        ExternalRiggedCharacterVisual visual = officer.GetComponent<ExternalRiggedCharacterVisual>();
        Transform hand = visual != null && visual.RuntimeRig != null ? visual.RuntimeRig.RightHand : null;
        if (hand != null)
        {
            var names = new System.Collections.Generic.List<string>();
            foreach (Transform child in hand.GetComponentsInChildren<Transform>(true))
            {
                names.Add(child.name);
            }
            Debug.Log($"GYMCHAOS_POLICE_GUN_POSE_HAND bones={string.Join(",", names)}");
        }
        frozen = true;
        settleFrames = 0;
        ApplyStep();
    }

    private static int TotalSteps => DrawPhases.Length + ShootPhases.Length;

    private static void ApplyStep()
    {
        bool drawing = step < DrawPhases.Length;
        float phase = drawing ? DrawPhases[step] : ShootPhases[step - DrawPhases.Length];
        if (!animator.HoldGunActionForVerification(drawing, phase))
        {
            throw new InvalidOperationException("Policeman gun clip is missing.");
        }
        weapon.SetVisible(true);
    }

    private static void AdvanceCapture()
    {
        // Let Update/LateUpdate apply the held pose and weapon placement.
        if (++settleFrames < 4)
        {
            return;
        }
        bool drawing = step < DrawPhases.Length;
        float phase = drawing ? DrawPhases[step] : ShootPhases[step - DrawPhases.Length];
        string label = $"{(drawing ? "draw" : "shoot")}-{Mathf.RoundToInt(phase * 100f):D2}";
        Transform body = officer.transform;
        Vector3 hand = weapon.HandPositionForVerification;
        Vector3 chest = body.position + Vector3.up * 1.45f;
        if (SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null &&
            Array.IndexOf(PictureLabels, label) >= 0)
        {
            Capture(label + "-player", chest + body.forward * 1.5f + Vector3.up * 0.2f, hand);
            Capture(label + "-side", hand + body.right * 0.8f + body.forward * 0.15f + Vector3.up * 0.05f, hand);
            Capture(label + "-close", hand + body.right * 0.35f - body.forward * 0.05f + Vector3.up * 0.12f, hand);
            Capture(label + "-far", hand - body.right * 0.55f + body.forward * 0.2f + Vector3.up * 0.05f, hand);
            // The reference photos' cameras: the gun's left side, level with it
            // (one-hand draw), and in front to the right of the muzzle (two-hand stance).
            Vector3 centre = weapon.WeaponCenterForVerification;
            Vector3 barrel = weapon.HeldBarrelDirectionForVerification;
            Vector3 gunUp = Vector3.ProjectOnPlane(weapon.WeaponUpForVerification, barrel).normalized;
            Vector3 gunRight = Vector3.Cross(gunUp, barrel).normalized;
            Capture(label + "-ref2", centre - gunRight * 0.6f, centre);
            Capture(label + "-ref3", centre + barrel * 0.55f + gunRight * 0.3f + gunUp * 0.05f, centre - barrel * 0.05f);
        }
        CheckGrip(label);
        Debug.Log(
            $"GYMCHAOS_POLICE_GUN_POSE label={label} " +
            $"handLocal={body.InverseTransformPoint(hand)} " +
            $"palmLocal={body.InverseTransformPoint(weapon.PalmPointForVerification)} " +
            $"gunLocal={body.InverseTransformPoint(weapon.WeaponCenterForVerification)} " +
            $"length={weapon.WorldLengthForVerification:F3}", weapon);
        step++;
        settleFrames = 0;
        if (step >= TotalSteps)
        {
            bool baking = Environment.GetEnvironmentVariable("GYMCHAOS_POLICE_GRIP_BAKE") == "1";
            // The game path must load the bake: a run-time solve costs a few
            // hundred milliseconds on the first draw.
            if (!baking && !weapon.GripUsedBakeForVerification)
                gripFailures.Add("hold was solved at run time: Resources/PoliceGrip.json missing or stale (re-run with GYMCHAOS_POLICE_GRIP_BAKE=1)");
            if (gripFailures.Count > 0)
            {
                Debug.LogError("GYMCHAOS_POLICE_GRIP_POSE_FAIL " + string.Join(" | ", gripFailures));
                Finish(1);
                return;
            }
            Debug.Log("GYMCHAOS_POLICE_GUN_POSE_CAPTURE_OK");
            Debug.Log("GYMCHAOS_POLICE_GRIP_POSE_OK poses=" + TotalSteps);
            if (baking)
            {
                string json = weapon.ExportGripForVerification();
                if (json == null)
                {
                    Debug.LogError("GYMCHAOS_POLICE_GRIP_BAKE_FAIL no solved two-hand hold to bake");
                    Finish(1);
                    return;
                }
                File.WriteAllText(Path.Combine(Application.dataPath, "Resources", "PoliceGrip.json"), json);
                Debug.Log("GYMCHAOS_POLICE_GRIP_BAKED");
            }
            Finish(0);
            return;
        }
        ApplyStep();
    }

    // Held in the fist in every frozen pose: grip on the palm side, fingers
    // curled toward the palm, barrel along the hand, slide up away from the
    // pinky, and almost no hand skin inside the gun.
    private static readonly System.Collections.Generic.List<string> gripFailures =
        new System.Collections.Generic.List<string>();

    private static void CheckGrip(string label)
    {
        if (!weapon.TryMeasureGripForVerification(out GymPistolGrip.Measures m))
        {
            gripFailures.Add($"{label}: grip not solved");
            return;
        }
        float cant = weapon.SlideTiltFromUpForVerification;
        float aimDeg = weapon.BarrelYawFromFacingForVerification;
        float support = weapon.SupportWeightForVerification;
        Debug.Log($"GYMCHAOS_POLICE_GRIP_POSE label={label} rightInside={m.RightInside:F3} wrapGap={m.WrapGap:F3} " +
            $"triggerGap={m.TriggerGap:F3} thumbSide={m.ThumbSide:F2} thumbGap={m.ThumbGap:F3} thumbInside={m.ThumbInside} " +
            $"support={support:F2} leftInside={m.LeftInside:F3} leftThroughRight={m.LeftThroughRight:F3} " +
            $"cantDeg={cant:F1} yawDeg={aimDeg:F1} insideBones={m.InsideBones}");
        // Held, not floating and not through: almost no hand skin inside the
        // gun, middle/ring/pinky tips on the handle, index at the trigger,
        // thumb on the far side of the frame.
        if (m.RightInside > 0.02f) gripFailures.Add($"{label}: {m.RightInside:P1} of the right hand inside the gun");
        if (m.WrapGap > 0.012f) gripFailures.Add($"{label}: fingertips {m.WrapGap:F3} m off the handle");
        if (m.TriggerGap > 0.02f) gripFailures.Add($"{label}: index {m.TriggerGap:F3} m off the trigger");
        if (m.ThumbSide < 0.8f || m.ThumbInside) gripFailures.Add($"{label}: thumb not on the far side ({m.ThumbSide:F2})");
        if (m.ThumbGap > 0.012f) gripFailures.Add($"{label}: thumb {m.ThumbGap:F3} m off the frame");
        if (cant > 25f) gripFailures.Add($"{label}: gun canted {cant:F0} deg");
        if (aimDeg > 15f) gripFailures.Add($"{label}: barrel {aimDeg:F0} deg off the officer's facing");
        // Two-hand stance: the left hand supports without passing through.
        if (label.StartsWith("shoot"))
        {
            if (support < 0.5f) gripFailures.Add($"{label}: support hand not on the gun ({support:F2})");
            if (m.LeftInside > 0.02f) gripFailures.Add($"{label}: {m.LeftInside:P1} of the left hand inside the gun");
            if (m.LeftThroughRight > 0.03f) gripFailures.Add($"{label}: {m.LeftThroughRight:P1} of the left fingers through the right");
        }
    }

    private static void Capture(string label, Vector3 eye, Vector3 focus)
    {
        GameObject host = new GameObject("Police Gun Pose Camera");
        Camera camera = host.AddComponent<Camera>();
        camera.fieldOfView = 45f;
        camera.nearClipPlane = 0.03f;
        camera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(focus - eye));
        RenderTexture target = new RenderTexture(720, 720, 24);
        camera.targetTexture = target;
        camera.Render();
        RenderTexture previous = RenderTexture.active;
        RenderTexture.active = target;
        Texture2D image = new Texture2D(720, 720, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0, 0, 720, 720), 0, 0);
        image.Apply();
        RenderTexture.active = previous;
        camera.targetTexture = null;
        string path = Path.Combine(
            Directory.GetParent(Application.dataPath).Parent.FullName,
            "Logs", "agent", $"police-pose-{label}.png");
        File.WriteAllBytes(path, image.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(image);
        target.Release();
        UnityEngine.Object.DestroyImmediate(target);
        UnityEngine.Object.DestroyImmediate(host);
    }

    private static void Finish(int code)
    {
        resultCode = code;
        GymChaosVerifierExit.Record(code);
        Time.timeScale = 1f;
        EditorApplication.isPlaying = false;
    }
}
#endif
